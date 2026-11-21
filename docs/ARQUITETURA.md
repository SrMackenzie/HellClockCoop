# Hell Clock Coop — Arquitetura e pontos de patch (Fase 0)

> Mapeado contra **Hell Clock v2.1.1.1**, Unity 6000.0.60f1, Mono.
> Fonte: `HellClock.dll`, `K10.dll` e `Assembly-CSharp.dll` decompilados com ICSharpCode.Decompiler 9.1. O código decompilado fica só na máquina de quem desenvolve e **nunca** entra no repositório.
> Todo alvo citado aqui também está em `src/HellClockCoop/PatchTargets.cs`. O plugin confere cada um a cada inicialização e loga o que estiver faltando.

---

## 1. Conclusões principais

1. **O jogo assume um jogador só, e em muitos lugares.** 185 arquivos de `HellClock.dll` acessam o jogador por estática:
   - `PlayerController.Instance`, implementado como `Guaranteed<PlayerController>.Instance`: 119 arquivos;
   - `ClassSingleton<PlayerManager>.Instance.PlayerController`: 79 arquivos;
   - `Singleton<PlayerController>`, registrado em `PlayerController.Awake` via `Singleton.SayHello(this)`.

   Isso abrange HUD, telas, drops, condições, triggers, sazonais, timer e câmera. **Reescrever isso para N jogadores é inviável e quebraria a cada atualização.**

2. **Decisão de arquitetura: cada máquina tem exatamente um `PlayerController`, o seu.** Ele é carregado do próprio save, com o input local do Rewired. Os aliados nunca são `PlayerController`. Na máquina de cada jogador, o aliado é um **avatar remoto**: um objeto visual mais um alvo (`ITarget`) que os inimigos enxergam. Com isso, todas essas referências continuam corretas, porque "o jogador" é sempre o jogador local.

3. **Os inimigos já sabem lidar com vários alvos.** `EnemyManager` mantém `List<ITarget> _availableTargets`. A cada `Update`, `TryToGetTargetForEnemy` escolhe **o alvo mais próximo dentro do raio de visão**. Hoje entram nessa lista o jogador (`PlayerController.Setup` → `AddToAvailableTargets`) e os companions (`CompanionAIDirector`). Registrar o avatar remoto nessa lista, no host, basta para os inimigos perseguirem o aliado. O desafio 2 da especificação está praticamente resolvido pelo próprio jogo.

4. **O RNG não é determinístico.** O jogo usa o `UnityEngine.Random` global (146 chamadas em `HellClock.dll`). O wrapper `K10Random` só fixa a seed em dois casos: a seed do boss (`LevelBlockInfo.BossSpawnSeed`) e a escolha inicial da Ascensão. **Sincronizar seeds não reproduz a mesma fase nos dois PCs.** O host precisa enviar **o resultado de cada sorteio** (qual `LevelDefinition`, quais inimigos e onde, quais totens), não a seed.

5. **Já existe Steamworks.NET com Steam inicializado pelo próprio jogo.** `HellClock.Steam.SteamManager` é o `SteamManager` padrão do Steamworks.NET (`SteamManager.Initialized`). A DLL inclui `SteamMatchmaking` (lobbies), `SteamNetworkingMessages` e `SteamNetworkingSockets`. **Atenção:** o getter `SteamManager.Instance` *cria* um `SteamManager` se não houver um, e o jogo lança exceção se a SteamAPI for inicializada duas vezes. O mod lê o estado por reflexão (`s_instance`, `m_bInitialized`) e nunca chama esse getter.

6. **O loot individual vem quase de graça.** `DropManager` se inscreve em `EnemyManager.OnAfterEnemyKilled` e rola o drop usando os modificadores do `PlayerController` local. Se cada cliente "matar" localmente a sua réplica do inimigo quando o host avisar, cada um rola o próprio loot com os próprios bônus, sem sincronizar itens.

---

## 2. Topologia de rede

| Item | Escolha |
|---|---|
| Autoridade | **Host autoritativo** para inimigos, andar, timer e fim de run. **Cada cliente é autoritativo sobre o próprio personagem**: posição, vida, mana e cálculo do dano que causa, porque esses valores dependem das relíquias, Bênçãos e stats que só existem no save dele. |
| Transporte | `SteamNetworkingMessages`, com relay da Steam (SDR): sem abrir portas e sem expor IP. |
| Sessão | Lobby da Steam (`SteamMatchmaking.CreateLobby`, tipo *friends only*), com convite pelo overlay e `GameLobbyJoinRequested_t`. Máximo de 4 jogadores. |
| Canais | 0 = confiável (controle, eventos, dano, mortes); 1 = não confiável (snapshots de posição e animação, de 15 a 20 Hz). |
| Serialização | Binária própria e enxuta (`BinaryWriter`). Nada de JSON por pacote. |
| Compatibilidade | No handshake vão a versão do mod, `Application.version` e um hash da tabela de `PatchTargets`. Versões diferentes: a conexão é recusada com mensagem clara. |

> **Por que vida e dano no cliente?** A defesa e o dano de cada jogador saem de `CharacterSheet` (fórmula `(base + Σaditivos) × Πmultiplicadores × (1 + Σ(v−1))`), alimentado pelo save local. Replicar o sheet inteiro de cada jogador no host seria enorme e frágil. Entre amigos, a confiança no cliente é aceitável. O host só aplica *limites de sanidade*, como dano máximo por golpe e taxa de golpes.

---

## 3. Fluxos do jogo que o mod precisa entender

### 3.1 Entrada no jogo e spawn do jogador
`BootstrapDungeon.AwakeCoroutine` executa, nesta ordem:
1. Instancia `ManagersPrefab` e o HUD.
2. Instancia `PlayerPrefab` **dentro de um container desativado**, chama `playerController.Setup()` e reativa o container.
3. Chama `PlayerManager.Instance.Setup(playerController)` → `OnPlayerSetupComplete`.
4. `WaitForSetup` → `ObjectPoolService.Init` → `DungeonProgressManager.SetupDungeon(gameConfig)` → `PlayerController.Instance.FinishSetup()`.

O truque do container desativado é o mesmo que o mod deve usar para criar o avatar remoto: instanciar desativado, remover os componentes de lógica e só então ativar. Assim, nenhum `Awake` do `PlayerController` roda (ele chamaria `Singleton.SayHello`, e o jogo logaria conflito).

`PlayerController.Setup()` puxa dados de `PlayerSaveManager.CurrentPlayerData`, registra eventos em `TimeManager`, `DungeonProgressManager`, `DropManager` e `EnemyManager` e cria `GameInput` com a câmera principal. **Nunca chamar isso para um aliado.**

### 3.2 Run e troca de andar
- `DungeonProgressManager` é um singleton de classe comum (`Instance`), não um MonoBehaviour.
- `StartRunFromDungeon(DungeonConfig)` → `StartRun()` → `TimerManager.StartTimer(GameConfig.TimeLimit)` → `ChangeLevel(isFirstLevel: true)`.
- `AdvanceLevel(uint)` → `InternalAdvanceLevel` → `ChangeLevel(false)`. É chamado pelos portais (`PortalInteractable`).
- `ChangeLevel` faz:
  - `DungeonConfig.GetLevelBlockInfoFromLevel(index)`: qual bloco, determinístico por índice; na Ascensão, usa `ascensionBlockOrder` do save;
  - `LevelBlock.GenerateNextLevel(...)`: **sorteia** a `LevelDefinition` com `ConditionalLevelDefinitions.GetRandomElement(...)`;
  - `LevelChanger.ChangeLevel(LevelInfo, ...)`: carrega a cena por Addressables (aditiva) e depois chama `LevelObjects.SetupLevel(levelInfo)`.
- `LevelObjects.SetupLevel` executa:
  - `SetupPlayerSpawn`: índice de spawn rotativo (`_currentPlayerSpawnIndex++`), warp do jogador e câmera;
  - `SetupEnemies`: `LevelDefinition.RandomizeEnemySpawnComposition`, elites, goblins e bosses, **tudo com `Random`**;
  - `SetupLevelObjects`: totens de Bênção, santuários amaldiçoados e portais, também sorteados.

### 3.3 O Hell Clock (timer)
- `TimerManager` (namespace global, `Guaranteed<TimerManager>.Instance`) guarda `_timer` e `_relaxedTimer` (`FloatState`).
- `Update()` decrementa com `Time.deltaTime` só se `PauseTimer` estiver livre, a run tiver começado e **o jogador local estiver vivo**. Ao chegar a zero, chama `DungeonProgressManager.FinishDungeon(EGameOverSource.Timer)`.
- `IncrementTimer(float)` recebe os bônus de tempo (drops `HellClockTimeTreasureClass`, relíquias).
- `FreezeTimer` (semáforo) congela no hub, em andares especiais e com `TimeManager.TimePaused`.
- `LevelChanger.LoadLevel` bloqueia `PauseTimer` durante o carregamento.

### 3.4 Pausa
- `TimeManager.PauseTime(key)` zera o `timeScale` direto. É chamado por `WindowBehaviour` (janelas), `PopupScreen` (popups de confirmação) e `LevelChanger.LoadFirstGameScene`.
- `TimeManager.PauseGame(key)` só pausa o tempo se a opção `Gameplay.AllowPauseToggle` estiver ligada (`OnChangeAllowPause`). Essa é a opção "permitir pausa" da tela de setup. É chamado por `PauseHUD`, `WindowBehaviour`, `VideoController`, `OnboardingSpotlight` e `PlayerController`.
- **Em coop:** forçar `AllowPauseToggle = false`, o que neutraliza todo `PauseGame`, e transformar `PauseTime` em no-op enquanto houver sessão (exceto a chave do `LevelChanger`). Abrir o inventário não pode congelar o mundo dos outros.

### 3.5 Dano
- Origem: `DamageDealer.PerformDamage(DamageSourceInfo, IAttacker, IDamageable, EFxOnDamage, DamageHitInfo)` → `PerformDamageWrapper`, que faz crit (`CritRoll`), modificadores e `DamageInstance`.
- **Ponto único de aplicação:** `target.LifeController.Hurt(damageInstance)`. Logo depois disparam `damageSourceInfo.OnDealDamage`, `attacker.OnDealDamage` e, em caso de morte, `OnTargetKill` e `attacker.OnKill`.
- Por isso, `LifeController.Hurt` é o melhor lugar para interceptar dano em réplicas.

### 3.6 Inimigos
- `EnemyManager` (MonoBehaviour com `Instance` estático): `SpawnEnemy(EnemyDefinition, Vector3, EnemyTypeConfig, ...)`, `Enemies`, `OnEnemyKilled`, `OnAfterEnemyKilled`, `SheetScalingModifiers` e `EnemyGlobalModifiers`.
- Escolha de alvo: `Update` → `TryToGetTargetForEnemy` → `EnemyAIController.SetCurrentTarget(ITarget)`.
- **Suposições de jogador único no inimigo:**
  - `EnemyController.CheckForSpawn()`: só "acorda" o inimigo pela distância até `PlayerManager.PlayerController`;
  - `EnemyController.Setup`: `LookAt(PlayerController.Position)` (cosmético).

### 3.7 Drops
- `DropManager` (namespace global, **não** `HellClock.Drop`) escuta `EnemyManager.OnAfterEnemyKilled` → `TryCalculateEnemyDrop(IEnemy)`. Usa `_playerController`, definido em `SetupPlayerController`, para alcance de coleta e modificadores de drop.
- Bosses chamam `TryCalculateEnemyDrop` diretamente (`EnemyBossController`).

---

## 4. Pontos de patch por fase

Legenda: **Pre** = prefix, **Post** = postfix, **(H)** = só no host, **(C)** = só no cliente.

### Fase 0 — Diagnóstico (implementada, só loga)
| Alvo | Tipo | Para quê |
|---|---|---|
| `PlayerManager.Setup(PlayerController)` | Post | Confirmar spawn do jogador local e estado do Steam |
| `TimerManager.StartTimer(float)` | Post | Confirmar início da run e valor do timer |
| `EnemyManager.AddToAvailableTargets(ITarget)` | Post | Ver a lista de alvos crescer (jogador e companions) |
| `HellClock.Level.LevelChanger.ChangeLevel(LevelInfo, …)` | Pre | Logar `LevelDefinition` sorteada, `HashID`, bloco e boss |

### Fase 1 — Fantasma
| Alvo | Tipo | O que fazer |
|---|---|---|
| `HellClock.Steam.SteamManager.Awake` | Post | Quando o Steam estiver pronto, registrar callbacks de lobby e de rede (`GameLobbyJoinRequested_t`, `LobbyEnter_t`, `SteamNetworkingMessagesSessionRequest_t`) |
| `PlayerManager.Setup` | Post | Iniciar o envio de snapshots do jogador local |
| `HellClockCharacterController.UpdateAnimationParameters` | Post | Amostrar `Animator`: hash do estado por layer, `normalizedTime`, `speed`, `move_speed`, `attack_speed` |
| *(sem patch)* `MonoBehaviour` do mod | — | `RemotePlayerAvatar`: clone visual do modelo, interpolação de posição e rotação e `Animator.CrossFade` quando o hash muda |

Primeira tarefa da Fase 1: **logar a hierarquia do `PlayerPrefab`** para decidir o que clonar (só o modelo com `Animator`, `SkinnedMeshRenderer` e `DynamicClothController`) e o que remover.

### Fase 2 — Mesma run
| Alvo | Tipo | O que fazer |
|---|---|---|
| `DungeonProgressManager.StartRunFromDungeon` | Post (H) / Pre (C) | Host envia `RunStart{dungeonHashId, worldTier}`; o cliente só inicia a run por mensagem |
| `DungeonProgressManager.AdvanceLevel` | Post (H) / Pre (C) | Host envia `AdvanceLevel{n}`; no cliente, o portal vira pedido ao host |
| `HellClock.Level.LevelBlock.GenerateNextLevel` | Post (H) / Post (C) | Host envia `levelDefinitionHashId`; o cliente substitui o resultado por um `LevelInfo` com a mesma definição (o construtor de `LevelInfo` é público) |
| `HellClock.Level.LevelObjects.SetupEnemies` | Pre (C) | `return false`: o cliente não gera inimigos (vêm na Fase 3) |
| `HellClock.Level.LevelObjects.SetupLevelObjects` | Pre (C) | Totens, santuários e portais replicados pelo host |
| `LevelObjects.SetupPlayerSpawn` | Post (C) | Usar o mesmo `_currentPlayerSpawnIndex` do host, com pequeno deslocamento por jogador |
| `TimerManager.Update` | Pre (C) | `return false`; aplicar o valor recebido do host (4 Hz e em eventos) via `_timer.Setter` |
| `TimerManager.IncrementTimer` | Pre (C) | Virar pedido `TimerBonus{s}` ao host |
| `TimerManager.Update` | Pre (H) | Continuar decrementando enquanto **qualquer** jogador estiver vivo (hoje para se o local morrer) |
| `HellClock.Manager.TimeManager.PauseTime` / `PauseGame` | Pre | No-op durante a sessão; forçar `AllowPauseToggle = false` |

### Fase 3 — Combate
| Alvo | Tipo | O que fazer |
|---|---|---|
| `EnemyManager.SpawnEnemy` | Post (H) | Atribuir `netId`; enviar `EnemySpawn{netId, enemyDefinitionHashId, pos, tipo/afixos}` |
| *(cliente)* | — | Criar a réplica pelo mesmo `EnemyManager.SpawnEnemy` e desligar a IA dela (patch em `EnemyAIController.UpdateAI` para réplicas) |
| `EnemyManager.TryToGetTargetForEnemy` | — | **Sem patch**: já escolhe o alvo mais próximo. No host, registrar o avatar remoto com `AddToAvailableTargets` |
| `HellClock.Character.Enemy.EnemyController.CheckForSpawn` | Pre (H) | Usar a menor distância entre todos os jogadores |
| `HellClock.AI.Enemy.EnemyAIController.SetCurrentTarget` | Post (H) | Mandar o alvo atual junto no snapshot (para animação e mira nas réplicas) |
| `HellClock.Character.LifeController.Hurt` | Pre (C) | Se o alvo é réplica: cancelar e enviar `HitReport{netId, valores por tipo, crit, status}` |
| `LifeController.Hurt` | Pre (H) | Se o alvo é o avatar remoto: cancelar e enviar `YouWereHit{…}` ao dono, que aplica no próprio `PlayerController` |
| `HellClock.Damage.DamageDealer.PerformDamage` | — | Referência; hooks extras só se `Hurt` não bastar (DoTs, `LifeLinkStatusEffect`) |
| `HellClock.Manager.DungeonProgressManager.FinishDungeon` | Pre (C) | Só o host encerra a run (timer zerado ou todos mortos) |

**Hipótese a testar:** modelar o avatar remoto, no host, como um *companion* (`CompanionAIDirector` / `PlayerCompanionDefinition`). Companions já têm `LifeController`, `StatusController`, já são `ITarget` e já entram em `_availableTargets`. Trocar só o "cérebro" pela rede pode poupar muito código.

### Fase 4 — Loot e progressão
| Alvo | Tipo | O que fazer |
|---|---|---|
| `DropManager.SetupPlayerController` | — | Referência: cada máquina rola o próprio loot com o próprio `PlayerController` |
| `EnemyManager.OnAfterEnemyKilled` (evento) | — | Garantir que a morte de réplica no cliente passe pelo fluxo normal de kill; o drop sai sozinho |
| **A investigar** | — | Como XP, ouro e Soulstones são concedidos: por `attacker.OnKill` (só quem mata) ou por evento global? |

### Fase 5 — Polimento
- Escala por número de jogadores: `EnemyManager.SheetScalingModifiers` e `EnemyGlobalModifiers` já existem para modificadores de sheet dos inimigos.
- Narrativa: `NarrativeManager` e `VideoController` (chama `PauseGame`). Proposta: rodar local, com barreira de "pronto" no host antes de liberar o andar.

---

## 5. Protocolo (rascunho)

| Mensagem | Canal | Direção | Fase |
|---|---|---|---|
| `Hello{modVer, gameVer, patchHash}` / `Welcome{playerIndex}` / `Reject{motivo}` | 0 | C→H / H→C | 1 |
| `PlayerSnapshot{pos, rot, animLayers[], params}` | 1 | todos | 1 |
| `RunStart`, `LevelLoad{levelIndex, blockHash, levelDefHash, spawnIndex}`, `Ready` | 0 | H↔C | 2 |
| `TimerSync{valor, frozen}` (4 Hz) e `TimerBonus{s}` | 1 / 0 | H→C / C→H | 2 |
| `EnemySpawn`, `EnemySnapshot[]` (10–15 Hz, só os que mudaram), `EnemyDeath{netId, killerIndex}` | 0 / 1 / 0 | H→C | 3 |
| `HitReport`, `YouWereHit`, `PlayerDied`, `PlayerRevived` | 0 | C↔H | 3 |

---

## 6. Riscos e decisões em aberto

1. **Atualizações do jogo.** Toda a lista acima está em `PatchTargets.cs`. O plugin valida no boot e não aplica nada se faltar um alvo da fase ativa.
2. **Conquistas (regra do projeto: "não mexer").** Decidido pelo autor em 21/11/2026: as conquistas seguem **globais**, como o jogo faz. O mod não bloqueia nem desbloqueia nada; o que o jogo de cada um desbloqueia em coop fica valendo (seção 14).
3. **Progressão no save local.** Cada jogador grava só o próprio save, como manda a regra. Ainda assim, flags de narrativa e de bosses derrotados entram no save de quem estava como cliente. Auditado na 0.8.4 e aceito pelo projeto (seção 14).
4. **Determinismo de VFX e projéteis.** Projéteis dos inimigos (`HellClock.Skill.Projectile`) colidem localmente. Nas réplicas, desligar dano de projétil e confiar no host.
5. **Pooling** (`HellClock.Pool`, `ObjectPoolService`): réplicas e avatares precisam vir dos mesmos pools ou ser excluídos deles, para não serem reciclados por engano.
6. **Unity 6 + BepInEx 5.** Usamos o BepInEx 5.4.23.6 oficial. **Confirmado no primeiro teste:** o `mscorlib.dll` do jogo é enxuto (2.072 tipos, sem `Module.GetPEKind`) e o preloader do BepInEx quebra. Faltam também APIs essenciais ao Harmony e ao MonoMod (`MethodBody`, `RuntimeMethodHandle.GetFunctionPointer`, `RuntimeHelpers.PrepareMethod`), então contornar só o `GetPEKind` não basta. A correção é um corlib completo **de Windows**, de `swyf-modding/Setup` (Unity 6000.3.10f1), em `BepInEx\unstripped_corlib`, via `dll_search_path_override`. O zip 6000.0.60 do unity.bepinex.dev é a variante Unix e trava o jogo no "Carregando". Compatibilidade conferida: zero referências ausentes e mesmas chamadas nativas do runtime. Há relatos de que o *listener* do log da Unity precisou de ajuste no Unity 6 em outros jogos. Se o `LogOutput.log` não mostrar as mensagens da Unity, os logs do próprio plugin continuam funcionando.

---

## 7. Teste da Fase 0 no jogo (20/11/2026) — confirmado

Hell Clock 2.1.1.1, Unity 6000.0.60f1, BepInEx 5.4.23.6 com o corlib de `BepInEx\unstripped_corlib`:

| Verificação | Resultado |
|---|---|
| BepInEx carrega (preloader e chainloader) | OK: "Supports SRE: True", Windows 64 bits |
| Plugin carrega | OK |
| Backup do save ao iniciar | OK: 15 arquivos, 1,3 MB |
| Versão do jogo | 2.1.1.1, igual à mapeada |
| Validação dos alvos de patch | 23/23 encontrados |
| Ordem dos eventos | `AddToAvailableTargets(Player)` → `PlayerManager.Setup` → hub (`QuixeramobimLevelDefinition`) → `StartTimer` → 1º andar (`CaatingaA1LevelDefinition`, HashID 8, bloco 3) |
| Timer base | 420 s (`GameConfig.TimeLimit`) |
| Steam | Inicializado pelo jogo; `SteamFriends`/`SteamUser` acessíveis |

Observações:
- No `PlayerManager.Setup`, o jogador ainda está na posição de espera (99999, 99999, 99999). A posição real só vale depois de `LevelObjects.SetupPlayerSpawn`. A Fase 1 deve começar a enviar snapshots só após o primeiro andar carregar.
- O `ETargetType` do jogador é `None`; isso serve para diferenciar o avatar remoto, se necessário.
- **O `LogOutput.log` não traz as mensagens da Unity.** *Correção (0.2.1): não é incompatibilidade com o Unity 6.* É a opção `[Logging.Disk] WriteUnityLog`, desligada por padrão no BepInEx. Em vez de ligá-la (gravaria tudo), a 0.2.1 copia só **erros e exceções** da Unity para o log, agrupando repetições (`LogDaUnity`).
- **Erro conhecido do jogo na tela de saves (0.8.3).** A `ArgumentException: An item with the same key has already been added. Key: [-1]` (pilha `SerializedDictionary.OnAfterDeserialize` ← `Resources.Load` ← `GuaranteedSO.Reference` ← `DungeonConfig.GetByID` ← `PlayerSaveBlock.SetSaveIndex`/`Show`) é do Hell Clock 2.1.1.1: em 21/11/2026 apareceu 12 vezes no `Player.log` com o `winhttp.dll` desativado (sem o mod), igual com o mod, e nenhum patch nosso toca esse caminho. Não corrigimos o dado do jogo. O `LogDaUnity` reconhece a assinatura (a mensagem + `DungeonConfig:GetByID` ou `DungeonConfig.GetByID` na pilha) e grava só uma linha de aviso por sessão, sem pilha: `[Jogo] erro conhecido do Hell Clock 2.1.1.1 na tela de saves (acontece sem o mod; pode ignorar)`. Os demais erros seguem com a pilha e o agrupamento. Se uma versão nova do jogo corrigir ou mudar o erro, a assinatura deixa de casar e ele volta ao tratamento normal.

---

## 8. Fase 1a — fantasma em modo eco (versão 0.1.0)

**O que é:** F8 cria um clone visual do jogador. Os snapshots do jogador local percorrem o caminho completo que vão percorrer na rede (amostra a 20 Hz → `PlayerSnapshot.Serializar` → bytes → `Desserializar` → buffer de interpolação → `RemotePlayerAvatar`), com 1 s de atraso e 2,5 m de deslocamento. F9 salva a hierarquia do jogador em `BepInEx/HellClockCoop/hierarquia-jogador.txt`.

**Como o clone é feito (`AvatarFactory`):**
- `Instantiate` do `PlayerController` vivo dentro de um pai desativado.
- Remoção, em várias passadas, de tudo que não seja Transform, Animator, SkinnedMeshRenderer, MeshRenderer ou MeshFilter.
- Nos Animators: `fireEvents = false`, `applyRootMotion = false`, `cullingMode = AlwaysAnimate`.
- Só então ativação.
- Como o clone nunca esteve ativo, nenhum `Awake`, `OnEnable` ou `OnDestroy` do jogo roda nele.

**Animação genérica (`AnimatorSync`):** são sincronizados os três Animators do personagem (corpo, `LashAnimator`, `SpearAnimator`):
- estado de cada layer (o de destino, se estiver em transição) e `normalizedTime`;
- todos os parâmetros float, int e bool, na ordem do controller.

No fantasma, a troca de estado usa `CrossFade` curto, e o mesmo estado com mais de 15% de desvio é ressincronizado com `Play`.

**Teste da 0.1.0 (20/11/2026):**
- O clone apareceu limpo: 535 componentes de lógica removidos, 108 renderers mantidos, sem seta, indicador ou efeito solto.
- A movimentação ficou fiel.
- As skills não apareceram no clone, por dois motivos:
  1. **Pesos de layer.** `Movement360LayerBehaviour` controla por script o peso das layers de corpo inteiro, braço esquerdo, braço direito e modo ferido, onde ficam os ataques. Corrigido na 0.1.1: o peso de cada layer vai no snapshot, num byte.
  2. **Projéteis e VFX** são objetos de pool separados, criados pelo sistema de skills, e não fazem parte do personagem. Vão precisar de uma mensagem `SkillCast` própria, só cosmética no cliente.

**Teste da 0.1.1 (20/11/2026) — Fase 1a confirmada:** animações de ataque com arma corretas no clone. Projéteis e números de dano não aparecem, como esperado: ficam para a mensagem `SkillCast` e para o `HitReport` da Fase 3.
- **Gancho para o `SkillCast`:** `PlayerSkillsController.OnSuccessExecuteSkill` (`EventSlot<ActiveSkill>`), disparado em `ExecuteSkill(ActiveSkill)` quando a skill é aceita. No outro lado, o projétil cosmético precisa ter colisão e dano neutralizados. Por isso entra junto com a Fase 3.
- **Dano causado pelo aliado (ex.: no boneco de treino):** calculado na máquina de quem ataca, com as relíquias e stats do save dele, enviado ao host num `HitReport`. O host aplica com `LifeController.Hurt` e replica vida e números de dano para todos, como na seção 2.

**Mira do tronco:** o `Pajeu_RIG_360` tem `RigBuilder` com `ChestRotationRig`, `WaistRotationRig` (OverrideTransform), `ChestAimRig` e `GibaoRig` (MultiRotationConstraint), com pesos também controlados pelo `Movement360LayerBehaviour`. Em vez de recriar o IK no clone, a 0.1.1 envia a rotação local final de `Hip_jnt`, `Spine_1..3_jnt` e `Neck_jnt` (já com o IK aplicado) e sobrescreve os ossos do clone no `LateUpdate`.

**Aparência:** os equipamentos visuais são variantes `*_lvl_01..05` sob `Pajeu_RIG_360/Mesh/{Bota,Calca,Chapeu,Gibao}`, ativadas por `PlayerGearVisuals`. No eco o clone copia a aparência local. Na rede vai ser preciso uma mensagem `Aparencia` com os níveis ativos de cada peça.

**Achado importante — os módulos da Unity também foram enxutos.** O corlib trocado cobre só as bibliotecas .NET. `UnityEngine.*Module.dll` continuam enxutos:
- não existem `LODGroup` nem `AnimatorControllerParameter.type` (o campo interno `m_Type` existe; o mod lê via `AccessTools.FieldRefAccess`);
- `System.IO.InvalidDataException` também não está no `System.dll` do jogo.

**Regra para todo o código do mod:** compilar sempre contra as DLLs do próprio jogo (o `scripts/build.sh` já faz isso). O compilador é a primeira barreira contra APIs removidas.

---

## 9. Fase 1b — coop pela Steam (versão 0.2.0)

**Sessão (`Net/SessaoCoop.cs`):**
- **Criar:** "Criar coop e convidar amigos" no menu do coop (§12.2; até a 0.9.2, a F7) cria um lobby `k_ELobbyTypeFriendsOnly` de até 4 jogadores e grava nos dados do lobby `hcc_mod`, `hcc_proto`, `hcc_game` e `hcc_host`. Depois abre `ActivateGameOverlayInviteDialog`.
- **Entrar:**
  - pelo convite: `GameLobbyJoinRequested_t`;
  - pela lista de amigos, com "Entrar no jogo": `GameRichPresenceJoinRequested_t` lê a chave `connect` = `+connect_lobby <id>`;
  - com o jogo fechado: `+connect_lobby` na linha de comando.
- **Checagem de versão:** quem entra compara a versão do mod e do protocolo com os dados do lobby. Se forem diferentes, sai com aviso. O dono não se checa contra si mesmo.
- **Membros:** `LobbyChatUpdate_t` atualiza a lista de pares. Quem sai tem a sessão P2P fechada e o avatar destruído.
- **Aperto de mão:** `Hello` com o flag `JaTeOuvi`. Cada lado reenvia a cada 2 s até ouvir o outro e saber que foi ouvido. Isso cobre o caso em que o primeiro pacote chega antes de o outro aceitar a sessão. Só são aceitos pedidos de sessão (`SteamNetworkingMessagesSessionRequest_t`) de membros do lobby.
- **Topologia:** na Fase 1, malha completa (cada um manda snapshot para cada um; com 4 jogadores são 3 envios). O dono do lobby é o host para as fases seguintes.

**Transporte (`Net/SteamTransporte.cs`):** `SteamNetworkingMessages`, com relay da Steam (`InitRelayNetworkAccess`).
- Canal 0: confiável (`Hello`, `Aparencia`, `Tchau`).
- Canal 1: não confiável + NoNagle, com snapshots a 20 Hz (cerca de 190 bytes cada, perto de 4 KB/s por aliado).

**Protocolo 4:**

| Tipo | Mensagem | Conteúdo |
|---|---|---|
| 1 | `PlayerSnapshot` (formato 3) | sequência, tempo, andar (`HashID` da LevelDefinition e índice), morto, posição, rotações, 3 Animators (estado, tempo e peso por layer; parâmetros), 5 ossos do tronco |
| 10 | `Hello` | protocolo, versão do mod e do jogo, nome, `JaTeOuvi` |
| 11 | `Aparencia` | ativo/inativo dos nós até 2 níveis sob `Pajeu_RIG_360/Mesh` |
| 12 | `Tchau` | saída voluntária |

**Visibilidade:** o avatar de um aliado só aparece se `CenaHash` for igual nos dois lados e válida (-1 = carregando). Ao reaparecer, o buffer de interpolação é zerado para não "voar" de um andar a outro. Sem pacote por 5 s, o avatar some.

**Testes automatizados:** `tools/codectest` faz ida e volta de `Hello`, `Aparencia` e `PlayerSnapshot`, campo a campo. Resultado na 0.2.0: 27/27.

**Distribuição (`scripts/empacotar.py`):** gera `dist/HellClockCoop-v<versão>.zip`, com conteúdo para extrair direto na pasta do jogo. O pacote leva:
- BepInEx 5.4.23.6 sem os `.xml`;
- o `doorstop_config.ini` com `dll_search_path_override = BepInEx\unstripped_corlib`;
- as 8 DLLs do corlib com `LICENSE-Mono`;
- o plugin;
- o atualizador em `BepInEx/patchers` (a partir da 0.8.0, seção 13);
- `BepInEx.cfg` com o console desligado (desde a 0.8.2; antes vinha ligado). Quem decide é o plugin: `[Diagnostico] MostrarConsole` (padrão `false`) é copiado a cada início para `[Logging.Console] Enabled` pela entrada `ConsoleManager.ConfigConsoleEnabled` do próprio BepInEx (reflexão; o CoreConfig regrava o arquivo com os comentários). O console abre antes do plugin, então a mudança vale na abertura seguinte. Fica no plugin porque o atualizador não troca o `BepInEx.cfg`;
- `LEIA-ME` e `LICENCAS` em pt-BR (UTF-8 com BOM, CRLF).

O zip é reproduzível (datas fixas). Na 0.2.0, os 24 binários e configs do zip conferem byte a byte com a instalação testada no PC do autor.

**1º teste com amigo (0.2.0, 20/11/2026):**
- Funcionou: convite, entrada no lobby, checagem de versão e Hello compatível nos dois lados.
- O amigo relatou um erro e não apareceu. No PC do host ficou "aguardando o personagem", ou seja, nenhum snapshot chegou.
- O `LogOutput.log` recebido era de uma reabertura posterior, que sobrescreve o arquivo, então a causa ainda não se sabe. Falta o `Player-prev.log` dele.
- O mesmo teste revelou NREs de `BlockLayerMotions` nos clones (`StateMachineBehaviour`). Corrigido na 0.2.1 com `AvatarAnimatorGuard`.

**0.2.1:**
- Erros da Unity copiados para o `LogOutput.log`.
- Guarda dos `StateMachineBehaviour` nos avatares.
- Log do primeiro snapshot enviado e recebido, e de cada mudança de cena dos aliados.
- `try/catch` com log na amostragem.

**DLC e modos de jogo (para a Fase 2):** na Fase 1 não interferem. Nós de aparência e estados de animação que não existem no outro jogo são ignorados (`Find` nulo, `HasState` falso). Na Fase 2, a run vai ser a do host. Se o host tiver a DLC e o convidado não, andares e itens da DLC não carregam no convidado. Vai ser preciso exigir a mesma DLC ou restringir ao conteúdo base, e anunciar as opções da run (`hardcoreModeEnabled`, `relaxedModeEnabled`, pausa) nos dados do lobby.

**2º teste com amigo (0.2.1, 20/11/2026) — Fase 1b confirmada na rede:**
- Convite, Hello compatível e snapshots (274 bytes a 20/s) funcionaram nos dois sentidos.
- Um viu o outro no hub e no 1º andar da run.
- Diferenças esperadas, porque cada um ainda roda a própria run:
  - monstros diferentes (os que um mata não morrem no outro);
  - baú e boneco de DPS vistos só por quem os ativou;
  - 2º andar diferente para cada um.

  Isso é a Fase 2 (mesma run) e a Fase 3 (inimigos do host).
- O autor quer que **as skills de um apareçam para o outro**. Isso entra junto com a Fase 3 (`SkillCast` cosmético).
- A roupa não balançava no avatar, porque o clone removia o MagicaCloth 2. Corrigido na 0.2.2: o clone mantém `MagicaCloth` (`SleevesCloth`, `CapeCloth`, `FrontCloth`, `ClockCloth`) e os colisores `Magica*Collider`. O `RemotePlayerAvatar` passou a rodar com `[DefaultExecutionOrder(-500)]`, para a pose do tronco ser aplicada antes da simulação de tecido.
- Erros do jogo vistos (não são do mod): `SerializedDictionary` na tela de saves e `Conflict with Singleton<HellAmbientOcclusion>` ao entrar no hub.

**Pendente para a Fase 1 fechar:** confirmar a física da roupa no avatar (0.2.2), primeiro com F8 e depois com o amigo.

---

## 10. Fase 2 — run compartilhada (versão 0.3.0)

**Decisões do autor (20/11/2026):**
- Convidado **puxado junto** quando o host inicia a run.
- **Mesma DLC Cursed War** exigida para dividir a run (a Deluxe, cosmética, só gera aviso).
- **Andar vazio** no convidado até a Fase 3.

**Modos (`SessaoCoop`):**
- `ModoHost`: dono do lobby com ao menos um convidado compatível e com a mesma DLC.
- `ModoConvidado`: o host trocou Hello, é compatível e tem a mesma DLC.
- `ComAliados`: alguém no lobby (afeta a pausa).

Fora desses modos, todos os patches deixam o jogo seguir como sem o mod.

**Host (captura e envio):**
- **`LevelBlock.GenerateNextLevel` (postfix)**, o único ponto de sorteio da `LevelDefinition`, com uma chamada no jogo (`DungeonProgressManager.ChangeLevel`). Envia o `PlanoAndar`: índice, primeiro andar (`!RunStarted`), `DungeonConfig.HashID`, `ESpecialFloor`, `LevelBlock.HashID`, índice local, semente do boss, boss forçado, `LevelDefinition.HashID` e se é boss.
- **`LevelObjects.SetupPlayerSpawn` (postfix):** `SpawnAndar` com o `_currentPlayerSpawnIndex` usado.
- **Timer:** `TimerSync` a 4 Hz, com `_timer` e `_relaxedTimer`, no canal não confiável.
- **`FinishDungeon` (postfix):** `FimRun` com o motivo.
- **Entrada no meio da run:** quem chega depois recebe o último plano e o último spawn (`AoNovoConvidado`).

**Convidado (obediência):**
- **Início:** no hub e com a cena carregada, chama `StartRunFromDungeon(DungeonConfig.GetByID(...))` ou `StartRunFromSpecialLevelBlock`.
- **Avanço:** `InternalAdvanceLevel(delta)` via reflexão. Isso pula a geração de blocos do Endless Nightmare, porque o bloco vem do host.
- **Entrada no meio da run:** o `StartRun` do jogo começa no andar 0. No postfix de `GetLevelBlockInfoFromLevel`, com `IsTransitioning` ativo, o `_currentLevelIndex` é posto no andar do host antes de `GenerateNextLevel` lê-lo.
- **Substituições:**
  - `DungeonConfig/AscensionDungeonConfig/DynamicDungeonConfig.GetLevelBlockInfoFromLevel` (postfix) → bloco do host. Na Ascensão a ordem dos blocos vem do save de cada um; no dinâmico, do `Random`.
  - `GenerateNextLevel` (postfix) → `LevelInfo` com a `LevelDefinition` do host, mantendo sheet modifier e squad pool.
  - `SetupPlayerSpawn` (prefix) → índice do host − 1, porque o jogo incrementa antes de usar. Se o spawn do host chega depois, o convidado é reposicionado chamando `SetupPlayerSpawn` de novo.
- **Andar vazio:**
  - `LevelObjects.SetupEnemies` e `SetupLevelObjects` (baús, santuários, totens, portais) são pulados.
  - `EnemyManager.SpawnEnemy(SpawnEnemyData)`, ponto único de todos os geradores, retorna null durante a run. As temporadas Nightmare e Miracles já tratam o nulo.
  - `SurvivalEventManager.StartSurvival` não inicia, porque o laço de ondas não trata nulo.
  - O "andar limpo" só dispara quando um inimigo morre, então nunca acontece no andar vazio.
- **Bloqueios** (só passam quando o próprio mod executa uma ordem do host):
  - `StartRunFromDungeon(DungeonConfig)`, `StartRunFromDungeonCoroutine`, `StartRunFromSpecialLevelBlock`;
  - `AdvanceLevel` (portais, inclusive o vermelho);
  - `FinishDungeon`.
- **Timer:** `TimerManager.Update` não roda durante a run, e o valor vem do host. `IncrementTimer` vira `TimerBonus` para o host.

**Pausa:**
- Com aliados no coop, `TimeManager.PauseTime` vira no-op, exceto a chave `LevelChanger`.
- `PauseGame` também vira no-op.
- Abrir inventário, mapa ou menu não congela o jogo.

**Robustez:**
- Os patches são aplicados classe a classe, com try/catch: um patch quebrado numa atualização do jogo cai sozinho, com o nome no log.
- A validação bloqueia todos os patches se faltar algum alvo das Fases 0 a 2: 33 alvos no catálogo.

**Protocolo 5:**
- Novos tipos 20 (`PlanoAndar`), 21 (`SpawnAndar`), 22 (`TimerSync`, não confiável), 23 (`TimerBonus`) e 24 (`FimRun`).
- O `Hello` passou a levar `Deluxe` e `CursedWar`.
- `codectest`: 52/52.

**Limitações conhecidas da 0.3.0:**
- Se o host sai no meio da run, o convidado segue sozinho num andar vazio e sem portais. O timer volta a correr e encerra a run.
- Enquanto acompanha o host, o convidado não abandona a run pelo menu. Precisa sair do coop antes ("Sair do coop" no menu do coop, §12.2; até a 0.9.2, a F6).
- O dano, os inimigos, o loot e as skills de um não aparecem para o outro (Fase 3).

**1º teste (0.3.0, 20/11/2026) — confirmado:**
- O convidado foi puxado para a run, e no log do host consta `PlanoAndar` com a definição 33; os dois ficaram na cena 33.
- O andar do convidado ficou vazio, sem inimigos e sem baús.
- A movimentação melhorou com a física de tecido.
- O boneco de DPS ativado pelo convidado no hub não aparece para o host. Isso é esperado: os inimigos ainda são locais (Fase 3).

**0.3.1:**
- **Seta verde do aliado no minimapa.** O clone mantém o `SpriteRenderer` da layer `MinimapIcon`, que é o ícone `IconType.Player` do Pajeú. Esse tipo não é girado para a câmera em `MinimapIcon.LateUpdate`, então gira junto com o personagem. O sprite fica tingido de verde (0.35, 1, 0.35), com `sortingOrder − 1`, para a seta do próprio jogador ficar por cima.
- **Remoção de componentes em três categorias** (MonoBehaviour, outros Behaviours e o resto). Isso elimina os avisos "Can't remove Canvas because CanvasScaler depends on it".

**Teste a fazer (Fase 2):**
- host inicia a run → convidado vai junto, para o mesmo mapa e o mesmo ponto de spawn;
- timer igual nos dois;
- portal do host leva os dois ao andar 2 igual;
- portal vermelho;
- fim por timer e por abandono do host;
- convidado entrando no meio da run.

---

## 11. Fase 3a — inimigos do host no convidado (versão 0.4.0)

**Modelo.** O host é dono dos inimigos. O convidado cria uma réplica de cada um pelo próprio
`EnemyManager.SpawnEnemy` (o bloqueio da Fase 2 é liberado só durante essa chamada), com a mesma
definição, tipo, arquétipo de campeão e afixos já sorteados no host. Na réplica, `EnemyController.Update`
é pulado (sem IA, sem movimento próprio): posição, rotação, vida e animação vêm do host.

**Mensagens (protocolo 6).**

| Tipo | Nome | Canal | Sentido | Conteúdo |
|---|---|---|---|---|
| 30 | InimigoSpawn | confiável | host → convidados | netId (u16), andar, HashID da definição, EEnemyType, arquétipo, afixos, SpawnBehaviourConfig, posição, yaw, vida, ajustes |
| 31 | InimigoMorte | confiável | host → convidados | netId, sem drop (limpeza dos opcionais com `IgnoreAllDrops`) |
| 32 | InimigoSumiu | confiável | host → convidados | netId (descarte sem morte fora da troca de andar) |
| 33 | InimigosEstado | não confiável, 10 Hz | host → convidados | vários itens por pacote (≤1100 bytes): netId, posição, yaw e vida em 16 bits, nasceu, AnimatorFrame opcional |
| 34 | Golpes | confiável, 20 Hz | convidado → host | dano somado por inimigo e máscara de tipos, nº de críticos |
| 35 | AndarLimpo | confiável | host → convidados | andar (o convidado dispara o próprio `OnAllEnemiesCleared`) |
| 36 | InimigoAjuste | confiável | host → convidados | `SetupSummon`/`SetupDisposable` feitos logo após o spawn (invocações sem XP/loot) |

**Relevância.** O estado só vai para inimigos a até 45 m de algum aliado (no máximo 160 por tick, os mais
próximos primeiro). Os demais ficam parados na última posição conhecida.

**Andar.** O spawn leva o índice do andar do host. O convidado guarda os spawns de andares que ainda não
carregou e cria as réplicas quando chega lá (até 25 por quadro). Spawns nunca criados expiram em 90 s.
Atenção: `DungeonProgressManager.StartRun` só liga `RunStarted` *depois* do `ChangeLevel` do primeiro
andar, então "em run" no host = `RunStarted` **ou** (plano sorteado **e** `IsChangingLevel`).

**Dano.** Prefixo em `LifeController.TryHurt` (por onde passam `Hurt`, `Kill` e efeitos de status): golpe
numa réplica é somado e relatado ao host. Localmente o golpe fatal é reduzido para deixar um fio de vida;
quem mata é o host. O host aplica com `DamageInstance.RentDummy` (sem números na tela dele), respeitando as
próprias regras de imunidade. Por 0,5 s depois de um golpe local o convidado só aceita do host vida menor
que a local (evita a barra "voltar" antes de o host aplicar).

**Morte e loot.** O host manda InimigoMorte no `Handle_EnemyDied`. O convidado chama `LifeController.Kill`
na réplica: o fluxo normal do jogo dispara `OnAfterEnemyKilled` → `DropManager`, então loot e XP do
convidado saem do próprio jogo dele (Fase 4 quase pronta). Morte com `SemDrop` bloqueia `IgnoreAllDrops`.

**Andar limpo.** No convidado, `EnemyManager._ignoreLevelClear` fica ligado enquanto ele está num andar de
run: a lista local zera por motivos do mod e não pode disparar portal/missões. O host manda AndarLimpo e o
convidado dispara `_onAllEnemiesCleared` (missões de "limpar andar").

**Acordar inimigos.** No host, `EnemyController.CheckForSpawn` também considera a posição dos avatares
dos aliados.

**Réplica.** `NavMeshAgent.updatePosition/updateRotation = false` (o `nextPosition` acompanha a posição);
Animator com `fireEvents = false` e `applyRootMotion = false` e registrado no `AvatarAnimatorGuard`
(eventos e StateMachineBehaviours de ataque não podem acertar ninguém no convidado). Tudo volta ao normal
antes de morrer ou ser descartado, porque o inimigo volta ao pool.

**Patches novos (alvo → motivo).**
- `EnemyManager.SpawnEnemy(SpawnEnemyData)` postfix → numerar e enviar (host).
- `EnemyManager.Handle_EnemyDied` (privado) postfix → morte.
- `EnemyManager.DisposeEnemy(float, IEnemy)` prefix → sumiço (host) / esquecer réplica (convidado).
- `EnemyController.Update` prefix → réplica sem IA.
- `EnemyController.CheckForSpawn` (privado) prefix → aliados acordam inimigos.
- `EnemyController.SetupSummon` / `SetupDisposable` postfix → InimigoAjuste.
- `LifeController.TryHurt` prefix → golpes do convidado.
- Campos privados lidos/escritos: `EnemyController._spawned`, `EnemyController._spawnOrigin`,
  `EnemyManager._ignoreLevelClear`, `EnemyManager._onAllEnemiesCleared`, `StatBasedValueController._currentValue`.

**Limites conhecidos da 0.4.0.** Inimigos só miram o host (3c). Status aplicados pelo convidado (lentidão,
congelamento) só existem na réplica (resolvido na 0.9.0, seção 11.4). Boss: a fase "agressiva" (`EnemyBossController.OnAggressive`, que
libera o dano) não é replicada; o host libera e aplica o dano normalmente, mas o convidado não vê números
nem barra do boss. O boneco de DPS do hub não é replicado (só inimigos de run).

### 11.1 Baús e quebráveis (versão 0.4.1, protocolo 7)

- **Baús** (tipos 40 BauSpawn e 41 BauAberto). O host varre 1x/s os `ChestInteractable` ativos do andar
  (pega os sorteados em `SetupLevelObjects` e qualquer outro), numera e manda HashID da `ChestDefinition`,
  posição e rotação. O convidado cria a cópia com `LevelObjectPoolController.SpawnChest` e a põe em
  `LevelObjects.LevelObjectsList` e `Chests` (o prefixo da Fase 2 em `SetupLevelObjects` agora limpa essas
  listas no convidado, como o jogo faria), para ela ser desativada na troca de andar. Baús que só existem
  no convidado são escondidos (com linha no log). Abrir: postfix em `ChestInteractable.ExecuteInteraction`
  avisa; quem recebe chama `ExecuteInteraction` com `OrdemRemota` ligado e o prefixo em
  `PlayerController.InteractWith` evita parar o jogador local e gravar a flag no save. Cada um rola o
  próprio loot (`ChestDefinition.Use`). O host repassa o aviso dos convidados aos demais.
- **Quebráveis** (tipo 42 Quebrou). `BreakableController` faz parte do mapa (mesmo lugar nos dois jogos):
  identificação pela posição. Postfix em `Handle_OnDie` avisa; quem recebe chama `Execute()` (Kill) no
  quebrável vivo mais próximo, até 0,75 m. Loot próprio. O host repassa.
- Limite: quem entra no meio do andar não recebe o que já foi quebrado.

### 11.2 Fase 3b e 3c (versão 0.5.0, protocolo 8)

**3b — efeitos das skills.** Todo efeito de skill nasce em `GameplayFx.CreateFx(ISkillCaster, Vector3?, Quaternion?, Skill)`
a partir de um prefab do Addressables (`_vfxPrefab`, AssetReferenceGameObject). Postfix nesse método: se o
lançador é o jogador local (ou `SourceOrSelf` é ele), o objeto é guardado e, no LateUpdate, anunciado
(tipo 50 FxNovo: GUID do prefab, caminho do osso se estiver preso no jogador, posição/rotação/escala e os
pontos do primeiro LineRenderer, para os tiros). Efeitos soltos que se movem mandam posição a 20 Hz (51,
não confiável); quando o objeto sai de cena (volta ao pool), 52 FxFim. Quem recebe carrega o prefab com
`Addressables.LoadAssetAsync(guid)` (handle mantido aberto), instancia sob um pai desativado, remove todo
componente fora do namespace UnityEngine e também colisores, Rigidbody, áudio, câmera e NavMesh (fica o
visual) e prende no avatar (mesma hierarquia de ossos) ou solta no mundo com extrapolação. Animators da cópia:
`fireEvents = false` e guarda de StateMachineBehaviours. Validade: 30 s solto sem atualização, 120 s preso.
O modo eco também recebe as cópias. Efeitos de inimigos não vão pela rede (a réplica os cria ao repetir a skill).

*0.5.1:* `GameplayFx` tem `CreateFx<T>` com os mesmos parâmetros; o patch escolhe o método à mão (`TargetMethod`,
não genérico, retorna GameObject). Sem isso o Harmony falhava com "Ambiguous match".

*0.5.2:* (a) os efeitos são rastreados sempre, e reanunciados quando aparece alguém para ver (eco ligado, aliado
chegando ao andar): a skill passiva de orbes cria os objetos uma vez só. (b) Efeito preso dentro de outro efeito
(orbe no "holder" que o script gira) vai solto, com posição de mundo. (c) Efeitos soltos interpolam entre amostras
0,1 s no passado, como o avatar (antes extrapolavam e erravam nos círculos). (d) Tudo que chega da rede espera
0,1 s, e o eco espera o mesmo atraso do fantasma (1 s + 0,1 s), para o efeito sair junto com a animação.
(e) Números de dano (tipo 53 DanosVistos, não confiável): postfix em `DamageViewManager.SpawnDamageView` manda os
números do jogador local em inimigos compartilhados (netId, valor, críticos); quem recebe mostra no mesmo inimigo.
No eco, o número se repete com o atraso do fantasma.

*0.5.3:* números dos aliados com cor própria (verde/ciano/lilás na ordem em que os aliados aparecem; o eco é
verde, como a seta do minimapa) e escala 0,8. O `SpawnDamageView` escolhe cor e escala por dentro, a partir dos
campos `_normalDamageColor`, `_criticalDamageColor`, `_randomStartScale` e `_randomFinalScale` do DamageViewManager:
o mod troca esses campos só durante a chamada e devolve em seguida. Configuração: `Interface.MostrarDanoDosAliados`
e `Interface.TamanhoDanoDosAliados`.

*0.5.4:* (a) efeito preso num inimigo (estacas cravadas, efeitos de status via `CharacterEffectsController.AddCharacterEffectVfx`)
vai preso ao mesmo inimigo (netId + caminho do osso; no eco, o próprio objeto). (b) Partes internas mexidas por script
(tipo 54 FxNos): por efeito, até 48 nós internos (5 níveis, sem outros efeitos rastreados), identificados por índices
de filho ("0/3/1"); a 20 Hz vai só o que mudou (ativo, posição/rotação/escala locais), o primeiro estado completo pelo
canal confiável. Se o script do remetente criou partes a mais (ReflexesKnivesController instancia facas extras), quem
recebe clona a primeira irmã. Era o caso das facas da skill Reflexos girando em volta do jogador.

**3c — inimigos atacam todos.**
- *Alvos no host:* `AlvoAvatar` (ITarget) por aliado no mesmo andar, registrado com
  `EnemyManager.AddToAvailableTargets` (o mesmo caminho dos companheiros). `TryToGetTargetForEnemy` já escolhe o
  mais próximo. O LifeController do alvo usa uma cópia da ficha base de um inimigo (a do jogador liga pedras da
  alma e níveis); morre/revive com o `Morto` do snapshot, para os inimigos largarem quem morreu.
- *Skills:* postfix em `ActiveSkill.Execute(ITarget)` no host (as sobrescritas chamam `base.Execute`): skill de
  inimigo replicado vira 37 InimigoSkill (netId, HashID da skill, alvo: host / convidado+SteamID / nenhum, posição
  do alvo). O convidado acha a skill na réplica (`SkillsController.GetSkill` ou `ActiveSkillPool`), vira a réplica
  para o alvo e chama `Execute` com o alvo traduzido: o jogador local de verdade (`GetTarget()`), se era ele, ou o
  `AlvoAvatar` do host/outro convidado. Durante a chamada, prefixo no getter
  `ActiveSkill.CurrentSkillAvailabilityStatus` devolve AVAILABLE (a réplica não tem IA nem fica "agressiva").
  Skills de invocação não são repetidas (os invocados vêm do host).
- *Dano no convidado:* sai do próprio jogo dele (eventos de animação da réplica agora ligados, hitboxes e projéteis
  da skill), com as defesas dele. No host, o avatar não tem colisor: o golpe não acerta nada.
- *Animação:* enquanto a réplica executa a própria skill (0,4 s, ou enquanto `IsUsing`, até 3 s), a animação do host
  não é aplicada por cima.
- *Morte do convidado:* `FinishDungeon(Defeat)` deixa de ser bloqueado quando o jogador local do convidado está
  morto: a run dele acaba como no solo, e o próximo PlanoAndar do host o traz de volta (entrada no meio da run).

### 11.3 Santuários, totens e chefes (versão 0.6.0, protocolo 9)

- **Santuários e totens** (tipos 43 ObjetoSpawn e 44 ObjetoUsado), em `Fase3/SantuariosCompartilhados.cs`. O host varre 1x/s
  os `ShrineInteractable` (inclui `CursedShrineInteractable`) e os `BlessingTotemInteractable` ativos e manda HashID da
  `ShrineDefinition`, posição, raio (amaldiçoado: `_radius`) e estado (livre / usado / purificado). O convidado cria a cópia
  com `LevelObjectPoolController.SpawnShrine` e, para o totem, chama `SpawnBlessingTotem(posição)`: o sorteio da bênção é
  do convidado (progressão individual), e só se `LevelObjects.TotemSpawnUnlocked` for verdadeiro no save dele.
- **Uso:** prefixo em `ShrineInteractable.ExecuteInteraction` avisa (só se o santuário estava disponível). Quem recebe chama
  `ExecuteInteraction` com `OrdemRemota` (o prefixo em `PlayerController.InteractWith` não para o jogador local):
  santuário comum dá o efeito ao jogador local; amaldiçoado no host começa a sobrevivência (`SurvivalEventManager.StartSurvival`
  segue bloqueado no convidado; as ondas chegam como inimigos replicados). Postfix em `CursedShrineInteractable.OnSurvivalEnd`
  (privado) no host manda "purificado"; o convidado chama `OnSurvivalEnd(true)` na cópia: animação e `UseShrine`, que dá o
  ponto de constelação dele (`ClaimCurrentFloorCursedShrine` no próprio save).
- **Chefes e agressividade:** o item de estado ganhou o bit 4 = `EnemyAIController.IsAggressive` (internal, lido por delegate
  no getter). Quando chega verdadeiro, a réplica recebe `IsAggressive.Setter(true)`: disparam os registros do jogo, entre eles
  `EnemyBossController.OnAggressive` (libera `CanTakeDamage`, `TargetController.ChangeTargetBoss` = barra do chefe, câmera)
  e a liberação de `CanUseSkill`.

### 11.4 Status do convidado valendo no host (versão 0.9.0, protocolo 10)

- **Mensagem** (tipo 38 StatusInimigos, confiável, 20 Hz junto com os Golpes, convidado → host): por inimigo e status,
  `HashID` da `StatusDefinition`, duração, intensidade, `HashID` da skill de origem e pilhas. O mesmo status no mesmo
  inimigo, na janela de 50 ms, vira um item (maior duração, maior intensidade; pilhas somadas até 255, ou a maior, se o
  status não acumula pilhas: `StatusDefinition.CanStack` falso, ou seja, `Refresh` e instâncias múltiplas). Só é aceita de
  par compatível e com a mesma DLC (`Compativel && DlcOk`).
- **Convidado:** postfix em `StatusController.AddStatus`. Vai ao host o status que entrou numa réplica viva (`__result`
  não nulo, ou seja, a réplica não era imune), aplicado pelo jogador local ou por uma invocação dele
  (`PlayerController`/`PlayerCompanionController`; status de inimigo em inimigo fica de fora). O `AddStatus` reescreve
  o argumento `duration` (resistência a controle do alvo e `DurationModifier` de quem aplica, os bônus da build do
  convidado), e o postfix lê o valor já reescrito; a resistência a controle da réplica é desfeita (`/ (1 − resistência)`),
  porque o host aplica a dele (também quando ela é negativa: o jogo multiplica por `1 − resistência` sempre que ela é
  diferente de 0 e menor que 1). A cópia que a skill faz da definição (`StatusDefinition.Instantiate`, um
  `Object.Instantiate`) mantém o `HashID` (`_hashId` é `[SerializeField]`).
- **Quais status** (estreitado na 0.9.1, decisão do autor): só o que mexe no **corpo** do inimigo é compartilhado; o que
  mexe no **dano** fica pessoal, como no solo (vulnerabilidade, resistência, óleo, reações elementais e os bônus contra
  alvo com status de quem aplicou). Combo entre jogadores fica para depois, guiado pelas métricas. Pelo tipo da definição
  e `IsBuff = false`: `FreezeStatusDefinition`, `StunStatusDefinition`, `RootedStatusDefinition` e, das
  `StatModifierStatusDefinition`, só a lentidão: todos os modificadores da lista que o efeito aplica
  (`GetStatModifierDefinitionsOverriden`, lida em `StatModifierStatusEffect.OnStart`) reduzem `MovementSpeed` (6) ou
  `AttackSpeed` (7) (`Validacao.ModificadorReduzVelocidade`: `Additive` abaixo de 0; `Multiplicative` e
  `MultiplicativeAdditive`, que são fatores — o jogo empilha com `(v − 1) · pilhas + 1` —, de 0 até abaixo de 1;
  `BaseOverride`/`AbsoluteOverride` recusados). O jogo considera "lento" o personagem com `MoveSpeed` abaixo da base
  (`HellClockCharacterController.IsSlowed`). `ElementalReactionStatusDefinition` e `ClosedBodyAuraStatusDefinition`
  (subclasses de `StatModifierStatusDefinition`) ficam de fora sempre. A mesma função (`StatusDeControle`) filtra no
  envio (convidado, sobre a cópia da skill) e na recepção (host, sobre a definição da coleção). Ficam de fora: dano contínuo (sangramento, queimadura etc.; o dano dele já passa pelo `TryHurt` do convidado e chega
  pelos Golpes), provocação (`TauntedStatusEffect` usa quem aplicou como alvo), auras, barreiras, curas, buffs e o acúmulo
  (`BuildupStatusDefinition`): quando o acúmulo enche no convidado, o congelamento que ele dispara é o que vai.
- **Host:** acha a definição em `Constants.Collections.STATUS_DEFINITION_COLLECTION` e chama `AddStatus` no inimigo com o
  **próprio inimigo como fonte**. O jogo não aceita fonte nula (lê `source.StatusController` sem checar). Com o inimigo
  como fonte, não disparam as passivas do jogador host (`ISkillCaster.OnApplyStatus`) nem os modificadores de duração dele
  (`OnBeforeInflictStatusToTarget`). Congelamento, atordoamento, enraizamento e modificador de atributo não leem a fonte
  (só o construtor e o efeito visual). Imunidade (`CrowdControlResistance >= 1`), resistência a controle e acúmulo de
  pilhas são os do host. Os ailments não passam pelo `Hurt` do host (saem do `DamageDealer.PerformDamageWrapper` de quem
  bate), então o golpe relatado não aplica o status de novo.
- **Validação na recepção** (seção 14): duração finita e `> 0`, intensidade finita e `> 0`, pelo menos 1 pilha. Duração
  acima de 30 s vira 30 s; intensidade fora de 1–10 vira o limite (o jogo usa 1 como padrão; no modificador
  multiplicativo abaixo de 1 o jogo divide pela intensidade, e abaixo de 1 uma lentidão de 0,7 viraria reforço do
  inimigo); pilhas até o `MaxStacks` do status (o `StatusEffect.AddStack` do jogo só limita quando ele é `> 0`; sem
  ele, até 10) e 1 se o status não acumula pilhas (`CanStack` falso: num status que já existe o jogo só renova ou cria
  outra instância, sem somar pilhas, mas as soma ao criar). Mensagem com mais de 512 itens é recusada inteira; status desconhecido ou fora da lista é recusado. Avisos
  (recusa e falha do `AddStatus`): no máximo 20 por convidado na sessão, contados pela conta Steam.
- **Limites:** o caminho contrário (status do host valendo na réplica do convidado, por exemplo "dano extra em inimigo
  congelado" na build do convidado) não existe; a réplica só anda como o host manda, então o convidado vê o inimigo parado
  ou lento. Status sem duração (0) não vai. Reenviar o mesmo congelamento sem parar mantém o inimigo parado (o teto de
  30 s vale por aplicação); fica aceito porque o dano do convidado já é confiável (seção 2). Com o inimigo como fonte,
  disparam os eventos do próprio inimigo (`OnBeforeInflictStatusToTarget` e `OnApplyStatus` dele): conferir no teste se
  algum afixo reage a isso. O `HashID` da skill vai como está (só as auras, que ficam de fora, leem o `SourceSkillID`).
  O host usa a definição base da coleção: valores que a skill do convidado sobrescreve na cópia dela
  (`StatusDefinition.GetOverridableValue`) não vão; duração e intensidade vão, porque são argumentos do `AddStatus`.

## 12. Balanceamento (trilha permanente, versão 0.7.0)

Decisão do projeto: **em grupo, um pouco mais difícil que o solo**. Prioridade 1: toda versão passa por revisão.

**Alavancas (as do próprio jogo).**
- *Vida e dano:* prefixo em `EnemyManager.ApplySheetScaling` (privado), que roda dentro do `SpawnEnemy` antes do
  `FinishSetup`: modificador multiplicativo em `EnemySheet` (`Life`, `BaseDamage`) com chave própria. A chave é sempre
  removida antes, porque o inimigo volta do pool com a ficha anterior. Vale no host e nas réplicas (o dano que o convidado
  leva sai da ficha da réplica). Aumentar `Life` depois de criado soma a diferença na vida atual (`UpdateMaxValue`).
- *Quantidade:* `EnemyGlobalModifiers.GetModifier(RegularEnemyQuantity).AddOrUpdateModifier(chave, fator, Multiplicative)`,
  lido por `LevelObjects` ao montar o andar e pelo `SurvivalEventManager` nas ondas. Vale a partir do andar seguinte.
- *Tempo:* prefixo em `TimerManager.IncrementTimer` no host, só para ganhos (> 0). O tempo do convidado chega por
  TimerBonus e passa pelo mesmo ponto. Motivo: com loot individual, cada jogador pega as próprias recompensas de tempo.
- *Jogadores:* 1 + aliados compatíveis com a mesma DLC (igual nos dois lados). Valores em `[Balanceamento]`, "2, 3, 4"
  (aceita "1,7; 2,4; 3,1").

**Métricas.** `Balanceamento/Metricas.cs` grava no fim de cada run (volta ao hub) `metricas/run-AAAAMMDD-hhmmss.json`:
formato, versões, id aleatório da instalação, papel (solo/host/convidado), jogadores, dungeon, tier, Ascensão, nível,
resultado (`FinishDungeon`), duração e, por andar: definição, chefe, jogadores, segundos, inimigos mortos
(`OnEnemyKilled`), dano causado (`OnDealDamage`), dano recebido (`LifeController.OnHurt`, `LifeDamageValue`), mortes e
Hell Clock na entrada e na saída. Sem nome, SteamID ou caminhos.

**Envio (0.7.1).** Só com `[Metricas] EnviarResumoAnonimo = true`. O resumo salvo com o aceite ligado vai para
`metricas/pendentes/<instalação>-<data>.json` já no formato tipado da API REST do Firestore e sai por `ISteamHTTP`
(POST `.../documents/runs?documentId=...&key=...`), sem depender do TLS do Mono. 2xx ou 409 (já existia) apaga o
pendente; 400/403 move para `recusadas`; sem rede ou 5xx espera 10 min. Projeto Firebase exclusivo do mod, plano
gratuito; a chave da Web identifica o projeto (não dá acesso) e fica fora do repositório, em `EnvioConfig.local.cs`
(sem ele o envio fica desligado). Regras em `docs/firebase/firestore.rules`: só criação em `/runs`, com id e campos
validados; leitura, alteração e exclusão negadas (a análise é feita pelo console).

**Privacidade (LGPD).** Os resumos são pseudonimizados: só dados do jogo (números, dungeon, resultado, versões), sem
nome, SteamID, log ou caminhos, mas com o id aleatório da instalação, que vai no campo `instalacao` e no id do documento
(`<instalação>-<data>`) e liga as runs da mesma instalação. O autor não tem como ligar um id a uma pessoa; o jogador
tem o próprio id em `instalacao.txt`. O IP chega ao Google no transporte (o envio sai da máquina do jogador pela
`ISteamHTTP`) e não é gravado no documento. Guarda: até o fim da calibragem do balanceamento; depois o autor apaga a
coleção `runs` pelo console. Exclusão antes disso: o jogador abre uma issue com o conteúdo de `instalacao.txt` e o
autor apaga `runs/<id>-*` pelo console. Para parar, desliga o envio; apagar `instalacao.txt` gera um id novo.

**Aceite (0.8.1).** `Balanceamento/JanelaAceite.cs`: no primeiro início, sem personagem carregado (menu), uma janela
IMGUI pergunta "Enviar resumos" / "Não enviar". A resposta grava `[Metricas] EnviarResumoAnonimo` e
`[Metricas] AceitePerguntado = true` (o BepInEx salva o `.cfg` na hora) e a pergunta não volta; quem já tinha ligado o
envio à mão é marcado como perguntado. Enquanto aberta, o módulo de entrada da UI (`EventSystem.currentInputModule`)
fica desligado (o clique e o Enter não chegam ao menu por trás) e volta ao fechar; o `EventSystem` em si fica ligado,
porque `HellClock.GameInput.InputHandler.Update` chama `EventSystem.current.IsPointerOverGameObject()` todo quadro e
quebra (NullReferenceException em série) se ele for nulo. O jogo lê o teclado pelo Rewired e o `Teclas` do mod cai no
Input legado (o log diz qual caminho na primeira tecla); lá os nomes do Input System `Enter`/`NumpadEnter` viram
`Return`/`KeypadEnter`, e o B do controle é `JoystickButton1`. Enter = "Enviar resumos", só depois de 3 s sem apertar Enter (quem aperta Enter para
pular a abertura não aceita sem ver a janela); os botões só respondem 1,5 s depois de abrir (o clique para pular a
abertura também não responde); N = "Não enviar" (recusar tem tecla, como aceitar); Esc ou B no controle fecha sem
responder até o próximo início. A janela diz o destino (Firebase do mod), o IP e a guarda. Ao aceitar, os
`metricas/run-*.json` já salvos são lidos de volta (`Serializar.ResumoParaEnvio`: o JSON local preserva int × double) e
entram em `pendentes` com o mesmo id que teriam (`<instalação>-<carimbo do arquivo>`, instalação com 32 hex como
nas regras; JSON com no máximo 16 níveis); a janela diz quantos são. Runs
solo e coop entram igual (papel `solo` é a base de comparação). Compilação sem `EnvioConfig.local.cs` não mostra a
janela. O aviso no fim da run (0.7.1) saiu.

**Próximos passos.** Painel solo × grupo, ajuste individual por diferença de progressão e jogador caído/reanimação.

### 12.1 Janela Novidades (versão 0.9.2)

`Interface/Novidades.cs` mostra, em IMGUI, a versão atual do mod e as 3 anteriores, com abas por versão e, nas que
têm `testado: false`, a seção "O que testar" (campo opcional `testar` do `docs/changelog.json`). O texto vem de
`Interface/NovidadesTexto.gerado.cs`, que `scripts/gerar-novidades.py` gera do `changelog.json` antes de cada build
(`build.ps1` e `build.sh`): nada de JSON nem rede em runtime (o mscorlib do jogo é stripped). O arquivo gerado vai
para o git, como `CHANGELOG.md`, mas não se edita à mão; o codectest confere que a primeira versão embutida é a
`PluginVersion` (pela constante `Novidades.VersaoAtual`, para não carregar o `Plugin`, que depende do BepInEx).

Abre sozinha uma vez, no menu (sem personagem), quando `[Interface] NovidadesVistas` difere da versão do mod (também
na primeira instalação), e só com a janela do aceite (12) fechada; ao fechar, grava a versão atual ali. Como a
exibição automática é única e abre já nas telas de abertura, Esc, B e o botão "Fechar" só respondem 1,5 s depois
(quem aperta Esc para pular a abertura não a perde sem ver); a F10 fecha sempre. Depois, abre
pelo item "Novidades" do menu do coop (§12.2; na 0.9.2, um botão abaixo do painel do coop) ou pela tecla
`[Teclas] Novidades` (F10, também em jogo). Desde a 0.9.3, aberta, segura a entrada do jogo pelo `BloqueioEntrada`
(§12.2) e responde ao mouse também em jogo; enquanto aberta, o `Update` do runtime não lê o menu do coop nem as teclas
F8/F9. Setas esquerda/direita ou LB/RB (`JoystickButton4/5` no Input legado) trocam de versão; setas
cima/baixo ou o direcional rolam; Esc, F10 ou B (`JoystickButton1`) fecham. Se o semáforo do jogo não travar
(`BloqueioEntrada.SeguraOJogo` falso), em jogo volta ao comportamento da 0.9.2: só teclado e controle, rodapé só com a
F10 (Esc, B e o mouse chegariam ao jogo), corpo recortado com `GUI.BeginGroup` e rolagem só pelas setas. Com mouse usa
`GUI.BeginScrollView`, que só existe na sobrecarga de 5 parâmetros no jogo (stripping). Sem efeito no balanceamento nem no protocolo.

### 12.2 Menu do coop e bloqueio da entrada (versão 0.9.3)

`Interface/MenuCoop.cs` desenha, em IMGUI (1080p escalado, como as outras janelas), uma gaveta à direita da tela, no
lugar das teclas F6/F7. Largura de 30% da tela, entre 380 e 460 px; no menu do jogo escurece o resto a 35%, em jogo
não escurece nem pausa (o jogo continua rodando e o personagem fica parado). Lista única, com o foco num item de cada
vez (moldura creme): ALIADOS (só leitura, `CoopRuntime.EstadoDoPar`, o mesmo texto do painel do canto), COOP e AJUSTES.
O foco é guardado pela ação, não pelo índice, porque a lista muda (ex.: "Sair do coop" some ao sair); a regra de
navegação (`ProximoFoco`, pula cabeçalhos, informação e item indisponível, gira nas pontas) e a da confirmação de saída
(`ConfirmaSaida`) são puras e testadas no codectest. O painel de status do canto continua como antes (as linhas citam a
tecla do menu).

`Interface/BloqueioEntrada.cs` segura a entrada do jogo enquanto o menu do coop ou a Novidades estão abertos:
- Semáforo do próprio jogo: `HellClock.GameInput.InputHandler.DisableInput` (`Semaphore` do K10, o mesmo que o console
  de cheats do jogo usa), travado com `BlockButDoNotIncrease(dono)` e solto com `Release(dono)`. Travado, o
  `InputHandler` responde falso em `GetActionDown/State/Up`, `GetAnyButtonDown` e nos eventos de ação
  (`EventForAction`), que cobrem o clique de mover (`MoveInteract`), skills, poção, pausa (`PauseButton`, `PauseGame`),
  `Back` e as janelas do jogo (`WindowController`).
- Os eixos não passam pelo semáforo (`GetAxisRaw`/`GetAxis` leem o `Rewired.Player` direto): o patch
  `BloqueioEntrada_InputHandler_Update` (Alvo `InputHandler.Update`, fase 5 em `PatchTargets.cs`, fora das fases ativas)
  zera `MovementDirection` e `AimDirection` depois da leitura. WASD e o analógico ficam parados.
- Desliga também `EventSystem.currentInputModule` (o `RewiredStandaloneInputModule` da UI do jogo), como a JanelaAceite.
- O painel chama `Pedir()` a cada quadro aberto; o `Update` do runtime chama `Atualizar()` antes de tudo, que libera só
  dois quadros depois do último pedido (`DeveLiberar`, testada): o Esc ou o clique que fechou não chega ao jogo.
- Nunca usa `InputHandler.Instance`: o getter cria um `InputHandler` se não houver (como o `PlayerController.Instance`).
  Lê o campo estático `_instance` e guarda o semáforo travado; se a cena troca o `InputHandler`, solta o antigo e trava
  o novo. Sem `InputHandler` (ou sem o campo), `SeguraOJogo` fica falso e, em jogo, menu e Novidades respondem só a
  teclado e controle.
- A confirmar no jogo: abrir o menu com o botão do mouse segurado pode engolir o "soltar" do jogo; o botão View pode ter
  função no jogo no mesmo quadro em que abre o menu.

Mapa dos controles do mod (fonte canônica; README e LEIA-ME descrevem para o jogador). Efeito: R = navega ou abre,
S = executa uma ação, P = grava no `.cfg`, ∅ = só leitura. Ícones: nenhum (só texto).

| ID | Controle | Onde | Efeito | `.cfg` |
|---|---|---|---|---|
| COOP-MENU-TECLA | F7 abre e fecha o menu do coop | menu e jogo | R | `[Teclas] MenuDoCoop` (a antiga `CriarOuConvidar` fica sem uso; `SairDoCoop`, a F6, saiu) |
| COOP-MENU-VIEW | View/Select do controle (`selectButton`, `JoystickButton6` no legado) abre e fecha | menu e jogo | R | — |
| COOP-MENU-ABRIR | Botão "Menu do coop (F7)" abaixo do painel do canto | só no menu (sem personagem) | R | — |
| COOP-MENU-FECHAR | [X] no cabeçalho; Esc, B, F7 ou View | gaveta aberta | R (libera a entrada 2 quadros depois) | — |
| COOP-ALIADO | Linha por aliado: nome e estado | gaveta, em sessão | ∅ | — |
| COOP-CRIAR | "Criar coop e convidar amigos" (sem sessão); "Esperando o Steam..." e "Conectando..." indisponíveis | gaveta | S (`SessaoCoop.HospedarOuConvidar`) | — |
| COOP-CONVIDAR | "Convidar amigos (Steam)" | gaveta, em sessão | S (reabre o overlay de convite) | — |
| COOP-SAIR | "Sair do coop"; o primeiro aperto vira "Confirmar: sair do coop?" por 3 s | gaveta, em sessão | S (`SessaoCoop.Sair`) | — |
| COOP-ECO | "Modo eco (teste com um PC)" LIGADO/DESLIGADO; sem personagem: "entre num save" | gaveta | S (`CoopRuntime.AlternarEco`, também F8) | `[Teclas] ModoEco` |
| COOP-CFG-NOMES | "Nomes dos aliados" | gaveta | P | `[Interface] MostrarNomesDosAliados` |
| COOP-CFG-DANO | "Números de dano dos aliados" | gaveta | P | `[Interface] MostrarDanoDosAliados` |
| COOP-NOVIDADES | "Novidades da <versão>": fecha a gaveta e abre a janela (§12.1) | gaveta | R | `[Teclas] Novidades` (F10) |
| COOP-NAV | Setas ou direcional movem o foco; Enter ou A executam; mouse: passar por cima foca, clique executa, roda rola | gaveta | R | — |
| COOP-ECO-TECLA / COOP-DIAG-TECLA | F8 (modo eco) e F9 (diagnóstico) | jogo, com gaveta e Novidades fechadas | S | `[Teclas] ModoEco`, `SalvarHierarquia` |

Sem efeito nos números do balanceamento nem no protocolo (10). Com o menu aberto em jogo o personagem fica parado e o
jogo segue (em coop não há pausa), o que só deixa o jogo mais difícil para quem abre o menu no meio do combate.

## 13. Atualizador automático (versão 0.8.0)

**Onde roda.** `BepInEx\patchers\HellClockCoop.Atualizador.dll`, um preloader patcher do BepInEx 5 sem DLL alvo: só o
`Initialize()` é usado, antes do chainloader, quando o plugin ainda não foi carregado. Compila contra o corlib completo
(`BepInEx\unstripped_corlib`), o `BepInEx.dll` e o `Mono.Cecil.dll` (`scripts/build.ps1`).

**Fluxo.**
1. `[Atualizacao] VerificarAoIniciar` (lido direto do `.cfg`, sem criá-lo: quem cria e migra o arquivo é o plugin).
2. Troca interrompida (jogo fechado entre guardar o arquivo atual e pôr o novo): o que falta no destino volta de
   `\anterior` (`Instalacao.RecuperarTrocaInterrompida`; nunca sobrescreve um arquivo que existe). Limite: se o que
   faltar for o próprio atualizador, não há quem rode a recuperação; o plugin segue funcionando, mas as atualizações
   param até o zip ser instalado à mão (a janela é a de dois `File.Move` seguidos).
3. Versão instalada: o `[BepInPlugin]` de `plugins\HellClockCoop\HellClockCoop.dll`, lido pelo Mono.Cecil sem carregar.
4. Numa thread, sem escrever em disco: `releases/latest/download/manifesto.txt`, a assinatura `manifesto.txt.sig` da
   mesma tag (`releases/download/v<versão>/`) e os arquivos cujo SHA-256 mudou. Versão publicada maior que a instalada:
   tudo o que mudou; igual: se o atualizador estiver diferente (troca anterior incompleta), tudo o que não confere;
   menor: nada. Com o
   plugin ilegível, o piso é a versão do próprio atualizador. Cada download tem o tempo que sobra do prazo, também
   dentro do laço de leitura. A thread do preloader espera até 5 s (+1 de folga); passou disso, abandona o resultado e
   o jogo segue.
5. `Instalacao.Aplicar`: grava em `HellClockCoop\atualizador\novo`, confere, move os arquivos substituídos para
   `\anterior` (só os da última atualização), põe os novos no lugar e valida (SHA-256 no lugar definitivo e versão do
   `[BepInPlugin]` = versão do manifesto). Qualquer falha desfaz na ordem inversa. Não há volta se a versão nova
   instalar bem e falhar depois, já no jogo: aí vale a release seguinte ou a cópia à mão de `\anterior`.
6. Recado de uma linha em `HellClockCoop\atualizador\aviso.txt` (versão nova instalada, falha na instalação ou recusa
   por assinatura/hash; falha de rede fica só no log), que o plugin mostra no painel do coop e apaga.

**Rede.** WinHTTP por P/Invoke (`WinHttp.cs`), proxy automático do Windows, redirecionamento HTTPS → HTTPS do GitHub.
No preloader o Mono não tem TLS 1.2 utilizável (o UnityTls ainda não subiu). Testado no preloader real em 21/11/2026, com
o `winhttp.dll` do doorstop na pasta do jogo (o proxy repassa as chamadas): 404 sem release, 200 com redirecionamento,
0,4–0,6 s por pedido. O único dado enviado é o pedido de download em si: o GitHub vê o IP e o User-Agent
`HellClockCoop-Atualizador/<versão>` (e os contadores públicos de download dos anexos passam a contar aberturas).

**Assinatura.** RSA 3072, PKCS#1 v1.5 com SHA-256 (`RSACryptoServiceProvider` do Mono, conferido no preloader). A chave
pública está em `ChavePublica.cs`; a privada fica só na máquina de quem publica (`tools/assinar`, padrão
`%USERPROFILE%\.hellclockcoop\atualizador-privada.pem`, cifrável com senha por `assinar proteger`; `*.pem` está no
`.gitignore`). Quem tiver a privada publica código que todos instalam sozinhos: senha e cópia fora do computador. Perder
ou vazar a privada obriga a trocar a pública e todos a instalar o zip novo à mão (não há revogação). O manifesto só
aceita destinos `plugins/HellClockCoop/<nome>` e `patchers/HellClockCoop.Atualizador.dll`, nomes sem pastas, SHA-256 e
tamanho de cada arquivo, e precisa conter o plugin; a versão lida antes da verificação tem de ser a mesma do manifesto
verificado. Versão menor que a instalada é ignorada (sem rebaixar com um manifesto antigo).

**Antivírus.** Sem PowerShell, curl ou programa baixado; nada executado a partir de bytes (as DLLs são carregadas pelo
BepInEx, do disco, como sempre); sem ofuscação; nenhum arquivo em uso é escrito por cima. O plugin ainda não foi
carregado; o arquivo do atualizador, já carregado, não fica travado pelo Mono (testado no preloader real em 21/11/2026:
renomear e abrir para escrita funcionaram), então ele é movido para `\anterior` e a versão nova vale a partir da próxima
abertura. Tudo vai para o log.

**Publicar.** `scripts/empacotar.py` monta `dist/release-v<versão>/` (zip, `manifesto.txt` + `.sig`, `HellClockCoop.dll`,
`LICENCAS.txt`, `HellClockCoop.Atualizador.dll`); a release do GitHub tem a tag `v<versão>` e esses anexos. A
`Atualizador.Versao` tem de ser igual ao `PluginVersion` (o empacotar confere). Testes: `tools/atualizadortest`.

## 14. Save e progressão (auditoria, versão 0.8.4)

**Quem grava.** O mod não tem nenhum caminho de escrita no save: só copia a pasta (`SaveBackup`, ao iniciar). Quem grava
é o próprio jogo de cada máquina, no save local, nos pontos em que já gravaria no solo: `CurrentLevelChanged` (troca de
andar), `FinishRun` e `FinishDungeonRoutine` (fim da run). Nenhuma mensagem leva dados de save de um jogador para outro.

**Ordens do host gravam o save do convidado.** Quando o host avança de andar (`InternalAdvanceLevel`), encerra a run
(`FinishDungeon`) ou purifica um santuário amaldiçoado, o jogo do convidado executa a mesma ação e grava o save dele, com
os dados dele (andar alcançado, run, ponto de constelação). O projeto aceita isso: é progressão individual, no próprio
save, como o convidado teria se jogasse a mesma run.

- **Morte e volta:** o convidado que morre termina a run dele como no solo (`ConvidadoMorreu`); ao voltar pela entrada no
  meio da run, o jogo dele conta uma run nova.
- **Conquistas: globais (decisão do autor, 21/11/2026).** O mod não toca em conquistas (regra do projeto). O jogo
  desbloqueia pelo funil `AchievementManager.TryToUnlockAchievement` (e ressincroniza pelas flags do save em
  `SyncAllAchievements`, ao escolher o save), então em coop uma ação de um jogador pode valer para os outros:
  - chefes ("Defeated [X]", "matar X em até N runs", chefes da Cursed War): quem estava na run ganha, não só quem deu o
    golpe final, nos dois sentidos (a morte replicada no convidado e o golpe do convidado no host passam pelo abate normal);
  - dano total da run (7.777.777 e seguintes): o dano de um entra na conta do outro;
  - concluir campanha, Hell Level e entrar no trem: o convidado ganha quando o host conclui ou avança;
  - continuam individuais: dano de um golpe só, telas, loja, bênçãos e relíquias;
  - "Finish Subway Survival" não sai para o convidado em coop (a sobrevivência é do host).
  Ficar individual exigiria saber quem deu o golpe final (protocolo novo), filtrar o desbloqueio e a ressincronização e
  guardar as conquistas negadas fora do save, porque a flag do chefe é a mesma da campanha.

**Validação na recepção (0.8.4).** Como cada ordem do host vira gravação no save do convidado, o que chega pela rede é
filtrado antes de chamar o jogo. O protocolo não mudou (`Protocolo.Versao` igual): só a recepção ficou mais estrita. Os
filtros puros ficam em `Net/Validacao.cs` e têm casos no `tools/codectest`.

| Mensagem | Quem aceita | Filtro |
|---|---|---|
| `PlanoAndar` | convidado, só do host | `IndiceAndar < DungeonConfig.GetByID(DungeonHash).MaxLevelIndex` (`MaxLevelIndex` é a quantidade de andares: o jogo usa `IsLastLevel = índice + 1 >= MaxLevelIndex`); dungeon desconhecida é descartada; andar especial (sem dungeon): até 200. Aviso no log |
| `FimRun` | convidado, só do host | `Motivo` dentro de `EGameOverSource` (0..4). Aviso no log |
| `TimerBonus` | host | finito, `> 0` e `<= 120 s`; desde a 0.8.5, também no máximo 120 s e 120 bônus somados por convidado (chave: SteamID, para sair e voltar ao lobby não zerar) numa janela fixa de 60 s (`Validacao.AceitarBonusNaJanela`; o recusado não conta; na virada da janela até o dobro passa em sequência, a média fica no teto). Um aviso de recusa por janela |
| `TimerSync` | convidado, só do host | os dois campos finitos e `>= 0` (4 Hz: descarte sem log) |
| `InimigoSpawn`, `InimigosEstado`, `InimigoMorte`, `InimigoSumiu`, `AndarLimpo`, `InimigoAjuste` | convidado, só do host | — |
| `BauSpawn`, `ObjetoSpawn` | convidado, só do host | — |
| `ObjetoUsado` | qualquer par | `Estado = Concluido` (purificação) só do host. Aviso no log |
| `BauAberto`, `Quebrou`, `ObjetoUsado` (`Usado`) | qualquer par | — (o host repassa) |
| `StatusInimigos` (0.9.0) | host, de convidado compatível e com a mesma DLC | duração e intensidade finitas e `> 0`, pelo menos 1 pilha (`Validacao.StatusValido`); duração limitada a 30 s, intensidade a 1–10 e pilhas ao `MaxStacks` (sem ele, 10; status que não acumula pilhas, `CanStack` falso, 1); mais de 512 itens recusa a mensagem; status desconhecido ou que não é de controle recusado (desde a 0.9.1, modificador de atributo só se for lentidão; seção 11.4). No máximo 20 avisos por convidado na sessão |
| `Golpes` (0.8.5) | host, de qualquer convidado | dano finito e `> 0` (`Validacao.DanoValido`); NaN ou infinito mataria qualquer inimigo. Valor finito grande segue aceito: o dano do convidado é confiável (seção 2). Descarte sem log (20 Hz) |

**Backup.** `SaveBackup.LimparAntigos` mantém no mínimo 1 cópia, mesmo com `BackupsMantidos` 0 ou negativo no `.cfg`.

## 15. Como reproduzir a análise

1. Compilar o decompilador (ICSharpCode.Decompiler 9.1, a partir do código-fonte do ILSpy) ou usar o ILSpy no Windows.
2. Decompilar `Hell Clock_Data/Managed/HellClock.dll`, `K10.dll` e `Assembly-CSharp.dll` para uma pasta **fora** do repositório (ver `.gitignore`).
3. Os `grep` usados para os números da seção 1 estão em `scripts/contar-singletons.sh`.
