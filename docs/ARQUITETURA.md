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
| `HellClock.Level.LevelObjects.SetupEnemies` | Pre (C) | `return false`: o cliente não gera inimigos (vêm na Fase 3); desde a 0.22.0 calcula antes o teto de inimigos do andar (`TetoDoAndar`, §12.11) |
| `HellClock.Level.LevelObjects.SetupLevelObjects` | Pre (C) | Totens, santuários e portais replicados pelo host; desde a 0.22.0 calcula antes o teto de baús do andar (`TetoDoAndar`) |
| `LevelObjects.SetupPlayerSpawn` | Post (C) | Usar o mesmo `_currentPlayerSpawnIndex` do host, com pequeno deslocamento por jogador |
| `TimerManager.Update` | Pre (C) | `return false`; aplicar o valor recebido do host (4 Hz e em eventos) via `_timer.Setter` |
| `TimerManager.IncrementTimer` | Pre (C) | Virar pedido `TimerBonus{s}` ao host |
| `TimerManager.Update` | Pre (H) | Continuar decrementando enquanto **qualquer** jogador estiver vivo (o `Update` do jogo para com o jogador local morto; desde a 0.15.0, no host caído o `Run_TimerUpdate` chama `UpdateDefaultMode`/`UpdateRelaxedMode` com as mesmas condições, sem o teste de morto; ver §12.4) |
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
| `DropBalance.CalculateExperienceDrop` | Prefixo | XP: não é por `attacker.OnKill`. Cada máquina, na morte local do inimigo (`DropManager.CalculateEnemyDrop` → `RollDrop`), dá `xp × ExperienceGain` ao próprio jogador, sem checar distância; chefe pelo `FeedbackBossXp`. O mod multiplica `baseExperience` pelo fator de XP em run multiplayer |
| `GoldTreasureClass.RollGoldValue` / `SoulStoneTreasureClass.RollSoulStonesAmount` / `CurrencyTreasureClass.RollLoot` | Postfix / Postfix / Prefixo | Ouro e Soulstones são sorteados no drop (e na bênção) com os atributos do jogador local e coletados no chão (`PlayerInventoryController.Collect`). O mod multiplica o valor; a moeda de criação é sempre 1, então o fator vira a chance de o drop sair. Venda (`SellGear`), ouro inicial (`StartedRun`) e cheats não passam por aqui |

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
7. **Desempenho (0.9.7).** Travadinhas esporádicas na sessão de 21/11/2026, sem número no log. Antes de otimizar, `Desempenho.cs` mede: cada parte do laço do mod (`Update`, `LateUpdate`, `OnGUI`) e os ganchos por golpe, número de dano e efeito somam o próprio tempo no quadro; criar avatar, carregar prefab de efeito (`WaitForCompletion`, síncrono) e copiar efeito são medidos à parte (desde a 0.26.0 também buscar ouvintes do Esc, medir texto do menu, criar linha do menu e buscar botões do jogo; o painel mede de novo a linha de informação só quando o texto muda de conteúdo, não de referência). Quadro ≥ 50 ms e ≥ 2,5× o normal vira linha `[Desempenho] Travada` (até 40 por janela), com coleta de lixo (`GC.CollectionCount`), mensagens recebidas e linhas de log do quadro; a cada 5 min em jogo, ou na troca solo/eco/coop, sai o `[Desempenho] Resumo`. Carregamento (sem personagem ou troca de cena) fica fora da conta. O mscorlib não tem `GC.GetTotalMemory`: a memória vem de `Profiler.GetMonoUsedSizeLong`.
   **Ampliado na 0.30.1** (etapa 2 do trabalho de desempenho; só mede):
   - **Carregamento de andar:** quadro com cena `-1` (o personagem já existe, o andar ainda não) também fica fora da conta. Antes só o quadro em que a cena *mudava* saía, e os quadros longos seguintes viravam `Travada` (58 das 69 linhas do log de 23/11/2026). O resumo diz quantos quadros longos ficaram de fora e o maior tempo do mod num deles, com a parte.
   - **Chamadas à Steam por quadro:** `SteamUtils.GetIPCCallCount()`, lido uma vez por quadro (devolve a contagem desde a leitura anterior), com média e pior no resumo e o valor do quadro na linha da travada. Conta o jogo e o mod juntos: o peso do mod sai da comparação entre solo e coop. Primeira medição (hub, 24/11/2026): solo 0,0 por quadro; host de sala vazia 19,0 (pior 31), com o mod em 0,80 ms/quadro contra 0,10 a 0,16 no solo.
   - **Partes novas:** `OnGUI` (antes somado em Interface), `Avatares` (o `LateUpdate` de cada `RemotePlayerAvatar`, que roda fora do laço do `CoopRuntime`) e `Medidor` (o custo do próprio `NovoQuadro`).
   - **Ganchos que estavam fora da conta:** `EnemyController.CheckForSpawn`, `TimerManager.Update` e o contexto de som inteiro (inclusive as chamadas que saem logo no começo). Cada um tem um detalhe com o número de chamadas e o tempo; na linha da travada só aparecem com 0,5 ms ou mais.
   - **Detalhe novo:** buscar peças da HUD (`AliadosNaTela.Achar` e a busca do `StatusIconsView` em `PainelGrupo.LerCores`). Os detalhes do resumo mostram chamadas, tempo total e o pior quadro.
   - **Memória:** além da leitura no resumo, a memória gerenciada é lida no primeiro quadro depois de cada coleta de lixo (o piso: mínimo, máximo e último do período).
   - **Rede:** segunda linha, `[Desempenho] Rede no período`, quando houve tráfego: mensagens e bytes enviados por tipo (1º byte do pacote, com o nome lido das constantes `Tipo*` do `Protocolo`; cada destinatário conta uma vez), o total recebido e em quantos quadros a leitura de um canal chegou ao limite do buffer de 64 mensagens (o que não coube fica para o quadro seguinte). A rede, o piso de memória e os detalhes contam também nos quadros de carregamento de andar; com a janela fechada (menu, carregamento do save) nada conta.
   - **Contexto:** em sala, o eco (F8) entra no nome do contexto (`host com 0 aliado(s) e eco`) e abre janela própria.

   **Etapa 3 (0.30.2): dono do lobby guardado** (§12.11). Medido em 24/11/2026 no PC do autor, com o mesmo roteiro antes (0.30.1) e depois, trechos de 45 a 90 s, personagem parado (na run, no começo do andar 1 do Ato II):

   | Contexto | Chamadas à Steam por quadro | Mod (ms por quadro) | fps |
   |---|---|---|---|
   | Hub, solo | 0,0 → 0,0 | 0,08 → 0,08 | 202 → 201 |
   | Hub, host de sala vazia | 17,5 → 1,4 | 0,77 → 0,23 | 186 → 196 |
   | Hub, sala vazia com eco | 14,0 → 1,0 | 0,73 → 0,27 | 186 → 195 |
   | Run, sala vazia com eco | 20,0 → 1,0 | 0,81 → 0,25 | 174 → 190 |
   | Run, host de sala vazia | 21,5 → 1,6 | 0,77 → 0,20 | 173 → 196 |
   | Run, solo | 0,0 → 0,0 | 0,07 → 0,07 | 197 → 200 |

   O gancho de `TimerManager.Update` caiu de 60 a 85 µs para ~0,5 µs por chamada em sala (igual ao solo), e as partes Run, Inimigos, Portas e Ganchos saíram da lista do resumo (pior quadro abaixo de 0,5 ms). Sobra a parte Rede, ~0,10 ms por quadro: a releitura do dono e as duas leituras de mensagens. O `[Desempenho] Resumo` sai normalmente dentro da run. `CheckForSpawn` não apareceu em `detalhes` com o personagem parado no começo do andar (o jogo não chamou o método nenhuma vez); falta conferir andando pelo andar.

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
- **Criar:** "Criar coop e convidar amigos" no menu do coop (§12.2; até a 0.9.2, a F7) cria um lobby `k_ELobbyTypeFriendsOnly` de até 4 jogadores e grava nos dados do lobby `hcc_mod`, `hcc_proto`, `hcc_game` e `hcc_host`. Depois abre `ActivateGameOverlayInviteDialog`. Desde a 0.18.0 o lobby nasce `k_ELobbyTypePublic` (padrão) ou `FriendsOnly`, pelo `[Coop] SalaPublica`, e ganha os dados da lista de salas (§12.7).
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
| 1 | `PlayerSnapshot` (formato 5 desde a 0.28.0) | sequência, tempo, andar (`HashID` da LevelDefinition e índice), morto, vida (fração 0..1 em 16 bits, 0.14.0), barreira/convicção (fração 0..1 em 16 bits, 0.28.0, §12.16), posição, rotações, 3 Animators (estado, tempo e peso por layer; parâmetros), 5 ossos do tronco |
| 10 | `Hello` | protocolo, versão do mod e do jogo, nome, `JaTeOuvi` |
| 11 | `Aparencia` | ativo/inativo dos nós até 2 níveis sob `Pajeu_RIG_360/Mesh` |
| 12 | `Tchau` | saída voluntária |
| 13 | `Expulso` (0.18.0) | sem campos: o host tira o convidado da sala |
| 14 | `ExpulsoAviso` (0.21.1) | SteamID do expulso, aos outros convidados |
| 15 | `PainelAliado` (0.27.0) | HashID das skills da barra (até 8) e até 4 status (HashID e acúmulos), para o painel de grupo (§12.15) |
| 16 | `PedidoTp` (0.29.0) | convidado → host: SteamID do aliado e índice do andar, depois da canalização (§12.17) |
| 17 | `RespostaTp` (0.29.0) | host → quem pediu: SteamID do aliado, motivo (0 = autorizado) e recarga restante (§12.17) |

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

**DLC e modos de jogo (para a Fase 2):** na Fase 1 não interferem. Nós de aparência e estados de animação que não existem no outro jogo são ignorados (`Find` nulo, `HasState` falso). Na Fase 2, a run vai ser a do host. (Desde a 0.21.0, §12.10: a Cursed War não tem depot próprio, o conteúdo dela vem no jogo base de todos e é liberado só pela posse; quem não tem a DLC divide a run do jogo base e só o Ato 4 trava.) Vai ser preciso anunciar as opções da run (`hardcoreModeEnabled`, `relaxedModeEnabled`, pausa) nos dados do lobby.

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
- Até a 0.20.0, a mesma DLC Cursed War era exigida para dividir a run. Desde a 0.21.0, DLC diferente divide a run do jogo base e o Ato 4 só com a Cursed War (§12.10). A Deluxe, cosmética, só gera aviso.
- **Andar vazio** no convidado até a Fase 3.

**Modos (`SessaoCoop`):**
- `ModoHost`: dono do lobby com ao menos um convidado compatível (até a 0.20.0, também com a mesma DLC).
- `ModoConvidado`: o host trocou Hello e é compatível (até a 0.20.0, também com a mesma DLC).
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
  - `AdvanceLevel` (portais, inclusive o vermelho); com o host caído (0.15.0), o portal do convidado vira `PedidoAvanco` ao host (§12.4);
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
| 36 | InimigoAjuste | confiável | host → convidados | `SetupSummon`/`SetupDisposable`, `SetExperienceMultiplier` e `SetInstanceTreasureClassOverride` feitos logo após o spawn (invocações sem XP/loot; XP das ondas de sobrevivência; tesouro próprio da sobrevivência e das temporadas, protocolo 13) |

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
- `EnemyController.SetExperienceMultiplier` / `SetInstanceTreasureClassOverride` postfix → InimigoAjuste com o valor
  (marcas `Xp` = 4 e `Tesouro` = 8; o tesouro vai pelo `HashID` e o convidado acha na `TREASURE_CLASS_COLLECTION`).
  `InimigoSpawn` guarda os mesmos valores para quem entra no meio da run (0.10.1, protocolo 13).
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
- **Quebráveis** (tipo 42 Quebrou). `BreakableController` vem na cena do andar: identificação pela posição.
  Postfix em `Handle_OnDie` avisa; quem recebe chama `Execute()` (Kill) no quebrável vivo mais próximo,
  até 0,75 m. Loot próprio. O host repassa. Não achou: aviso no log com a distância do mais próximo (até 5
  por andar).
- **Sorteio da cena (0.9.6, protocolo 12).** Parte dos quebráveis sai de um `DrawRandomSceneObject`, que no
  `OnEnable` sorteia, com pesos, qual objeto do ponto aparece (gerador `K10Random`, sem semente: cada PC dava
  um). O host sorteia uma `SementeCena` por andar e a manda no `PlanoAndar`; no `GenerateNextLevel` os dois
  lados a guardam (o convidado, a do plano), e o prefixo em `DrawRandomSceneObject.OnEnable` semeia o
  `K10Random` com semente + posição do ponto (em cm). O finalizer devolve o estado anterior do `K10Random`
  (campos `_random` e `_useSeed`). A ordem do jogo garante a semente a tempo: `GenerateNextLevel` →
  carrega a cena → `SetRootObjectsState(true)` (sorteio) → `SetupLevel`. Fim da run zera a semente (hub e
  solo sorteiam como no jogo). Ao entrar no andar, cada PC loga o número de quebráveis e uma assinatura
  (XOR das posições), para comparar os dois logs.
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

*0.9.4:* o avatar é clone do jogador local, então levava junto os efeitos presos nele na hora da cópia (adagas do
Reflexos, orbe de poder) sem a lógica: ficavam parados ao lado da cópia animada que chega por aqui. O
`AvatarFactory.Criar` agora tira do clone, antes de remover a lógica, toda subárvore cujo original é um efeito
rastreado (`FxCompartilhados.EhEfeitoDoJogador`), percorrendo original e clone juntos (o `Instantiate` preserva a
ordem dos filhos). Vale para o eco e para os avatares dos aliados. O modo eco também continua ligado ao trocar de
andar ou de cena: a troca destrói o fantasma junto com a cena e o `LateUpdate` o recria quando o personagem está em
cena (antes, `_ecoAtivo` ficava ligado sem fantasma e o primeiro F8 só desligava).

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

*0.9.9:* o boneco de treino do hub (`DummyEnemyController`) não é compartilhado: cada PC cria o seu pelo
`DummyInteractable.SpawnEnemy` → `EnemyManager.SpawnEnemy`, fora da run (o host só numera inimigos da run). O
postfix de `SpawnEnemy` guarda o boneco local; o número num boneco vai com `DanosVistos.IdBoneco` (0, que o host
nunca dá) e quem recebe mostra no próprio boneco, se estiver chamado. Só o número: a vida do boneco é de cada um.

*0.5.3:* números dos aliados com cor própria (verde/ciano/lilás na ordem em que os aliados aparecem; o eco é
verde, como a seta do minimapa) e escala 0,8. O `SpawnDamageView` escolhe cor e escala por dentro, a partir dos
campos `_normalDamageColor`, `_criticalDamageColor`, `_randomStartScale` e `_randomFinalScale` do DamageViewManager:
o mod troca esses campos só durante a chamada e devolve em seguida. Configuração: `Interface.MostrarDanoDosAliados`
e `Interface.TamanhoDanoDosAliados`. *0.26.0:* `Interface.OpacidadeDanoDosAliados` (0,1–1, padrão 0,5) vai no alfa das
duas cores: o `DamageView.Animate` põe a `FontColor` no `Text.color` e o sumiço é à parte, no `CanvasGroup.alpha` (IL).

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
- *Morte do convidado:* desde a 0.15.0 ele fica caído e volta no próximo andar do host (§12.4). Reserva: se a morte do
  jogo rodar mesmo assim, `FinishDungeon(Defeat)` com o jogador local morto deixa de ser bloqueado e a run dele acaba
  como no solo; o próximo PlanoAndar do host o traz de volta (entrada no meio da run).

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
  e a liberação de `CanUseSkill`. Também o som de estado (`EnemyAIController.SetupStateSfx`, uma vez) e o grito de aggro
  (`PlayOnAggressive`).
- **Som do movimento (0.9.8):** sem o `Update` do personagem, o `MovementController` da réplica não calcula a velocidade
  (`SpeedRatio` fica 0). Os passos (`TemporaryAnimatorEventCallbacks.FootR/FootL` → `WalkSurfaceDetector.PlayWalkEffects`,
  que só toca com `_speedRatio` ≥ 0,4, posto pelo `MovementController.Update`) e o parâmetro parado/andando do som de estado
  (`_stateIdleWalkBlendAudioEventParameter`, posto pelo `EnemyAIController.UpdateAI`) ficavam em "parado". O convidado mede
  quanto a réplica anda por quadro, divide por `MovementController.MovementSpeed` (suavizado) e põe nos dois. Os sons que
  saem de eventos de animação (`CharacterAnimatorEventCallback.TriggerSfx`), skills, golpe e morte não dependem desse
  `Update` (conferido no IL do 2.1.1.1).

### 11.4 Status do convidado valendo no host (versão 0.9.0, protocolo 10)

- **Mensagem** (tipo 38 StatusInimigos, confiável, 20 Hz junto com os Golpes, convidado → host): por inimigo e status,
  `HashID` da `StatusDefinition`, duração, intensidade, `HashID` da skill de origem e pilhas. O mesmo status no mesmo
  inimigo, na janela de 50 ms, vira um item (maior duração, maior intensidade; pilhas somadas até 255, ou a maior, se o
  status não acumula pilhas: `StatusDefinition.CanStack` falso, ou seja, `Refresh` e instâncias múltiplas). Só é aceita de
  par compatível (`Compativel`; até a 0.20.0, também `DlcOk`).
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

### 11.5 Barreiras de sangue (versão 0.9.5, protocolo 11)

- **No jogo:** a barreira é a `SmokeDoor` (`Open(bool animate)`/`Close(bool animate)`, estado privado `_state`
  NONE/OPENED/CLOSED, iniciado em `Start` por `SetState(!_startClosed)`). Fecha por `SurvivalEventManager.StartSurvival`
  (`CloseAllSurvivalDoors`: andar de sobrevivência, `LevelDefinition.IsSurvivalFloor`/`AlwaysStartSurvival` no `ChangeLevel`,
  e santuário amaldiçoado) e por roteiros Fungus (`OpenCloseSmokeDoor`, `OpenCloseSmokeRoomCommand` → `SmokeRoom.Close`,
  que liga `_toActivate` e fecha `_smokeDoors`). No convidado o `StartSurvival` é bloqueado (Fase 2) e o roteiro não roda:
  por isso ele passava livre (achado da sessão de 21/11/2026).
- **Mensagem** (tipo 45 PortaFumaca, host → convidados, confiável), em `Fase3/PortasCompartilhadas.cs`: postfix em
  `SmokeDoor.Close/Open` no host manda andar, posição da porta, fechada/aberta, `animate` e a posição do host, quando o
  estado muda (as mudanças dentro de `Start` não contam: são iguais nos dois jogos). O host reenvia as fechadas a cada 2 s.
- **Convidado:** aplica na porta mais próxima (até 1,5 m) do mesmo andar; porta dentro de um `SmokeRoom` desligado fecha
  pela sala. Quando a barreira fecha nele (inclusive ao chegar depois), vai para perto do host com
  `MovementController.Warp` (decisão do autor: ninguém fica de fora da luta), se a posição do host estiver a até 60 m da
  porta. Ao sair do coop, reabre as barreiras que fechou por ordem do host. Até 64 ordens pendentes; uma busca por quadro.

### 11.6 Sons das skills e golpes dos aliados (versão 0.11.0, protocolo 14)

**De onde sai o som (IL do 2.1.1.1).** Todo som passa por `AudioController.PlaySfx(EventReference, Vector3?, List, bool, bool)`
(num ponto) ou `PlayAndAttachSfx(EventReference, ISkillCaster | GameObject, List, bool)` (preso); as versões com
`HcAudioEventReference` só repassam para estes. O `EventReference` do FMOD é um GUID (`getEventByID`), igual nos dois
jogos. Nas skills: `ActiveSkill.Execute`/`ExecuteRaw` (`ActiveSkillDefinition.Sfx`, modo 1 no ponto do lançador, modo 2
preso a ele em `_attachedActivationSfx`), `SkillMeleeAttack.Execute` (`CustomActivationSfx`), o fim do `GameplayFx.CreateFx`
(`GameplaySfxConfig.SetupSfx` → `ActivateSfx(vfx, caster, pos)`, na hora ou no `IActivationVfx.OnActivation`; cinco
posições: objeto, preso ao objeto, lançador, preso ao lançador, âncora do objeto), os disparos (`FireProjectile*`,
`RangedAttack.FireSingleBullet`) e corrotinas de algumas skills (Reflexos, SecondWind, ChargedAttack…). Eventos de
animação do jogador: `HellClockCharacterController.OnTriggerSfx` → `PlayAndAttachSfx(this)`. Golpe: `CharacterEffectsController.CreateDamageFX`
(sangue, `_hurtSfx` com o parâmetro do tipo de dano quando há `EFxOnDamage.PlayDamageTypeHitSfx`, efeito do tipo e luz), chamado só
pelo `DamageDealer.PerformDamageWrapper`, depois do `Hurt`.

**Por que saía mudo.** A cópia do efeito (3b) nunca passa pelo `CreateFx` (o som mora na configuração do `GameplayFx`, não no
prefab) e a limpeza tira tudo fora do `UnityEngine` (também um eventual `StudioEventEmitter`; o log `[Fx] Cópia de … sem N
emissor(es)` mostra se algum prefab tem). Nada da skill em si ia pela rede. O avatar tem `fireEvents = false`. O golpe do
convidado é aplicado no host com `LifeController.Hurt` direto e o do host chega ao convidado como vida: nenhum dos dois passa
pelo `PerformDamageWrapper`.

**Sons (55 SonsAliado e 56 SonsFim, confiáveis, todos → todos no mesmo andar), `Fase3/SonsAliados.cs`.**
- *Captura:* prefixo/postfix/finalizador nos dois métodos centrais (`Fase3/PatchesSons.cs`); só a chamada de fora conta
  (profundidade). O som é do jogador local quando vai preso a ele ou a algo dele (`SourceOrSelf`; companheiro vai como ponto),
  ou quando toca dentro de um método de skill do jogador local: contador de contexto ligado por prefixo e finalizador nos
  métodos de `ContextosDeSom.Lista` (em `Fase3/SonsAliados.cs`; a lista do IL acima; corrotina pelo `MoveNext` de `<Metodo>d__N`). O lançador sai da
  instância (`Skill.SkillCaster`), de um parâmetro (`ISkillCaster`, `IAttacker`, `Skill`, `SingleBulletRequest`) ou de um
  campo da máquina de estados (`<>4__this`, `skillCaster`). Skills de inimigo passam pelo mesmo prefixo e não contam.
- *Item:* GUID (4 × int), modo (ponto, preso ao jogador, preso ao efeito pelo id do `FxCompartilhados`), posição de mundo e
  até 4 parâmetros (nome, valor). Som preso a um efeito que não vai pela rede vira ponto. Som que dura (`isOneShot` falso, ou
  `AttachedSfx._isOneShot` falso) ganha um Id; quando para (`AttachedSfx.Stopped` ou `PLAYBACK_STATE.STOPPED`), vai o
  `SonsFim`. Até 64 itens por mensagem e 4 parâmetros por som (acima disso a mensagem é recusada inteira).
- *Quem recebe:* espera 0,1 s (como os efeitos; no eco, o atraso do fantasma) e toca pelo mesmo `AudioController`: preso ao
  avatar, preso à cópia do efeito (sem a cópia ainda carregada, no ponto) ou no ponto; `EventInstance.setVolume` com
  `[Audio] VolumeDosAliados`; `[Audio] SonsDosAliados = false` desliga. Som que dura é parado no `SonsFim`, ao sumir o avatar,
  ao sair do coop ou em 20 s.
- *Golpe dentro da skill:* tiro, facas e golpes de corrotina aplicam o dano dentro do método de skill. Enquanto roda o
  `DamageDealer.PerformDamageWrapper` (contador próprio), nada é capturado: o som de dor vai pelos golpes vistos e o de morte
  sai da morte do inimigo em cada PC; sem isso, os dois tocariam duas vezes em quem recebe.
- *Parâmetros:* o `PlaySfx` devolve a lista ao pool (que a esvazia) antes de sair; o prefixo copia os parâmetros.
- *Evento aceito:* quem recebe consulta o FMOD (`getEventByID`, guardado por GUID): só evento 3D que não é snapshot (um 2D
  tocaria em volume cheio em qualquer lugar; um snapshot mexe na mixagem) e som que dura só com Id. Até 64 sons que duram por
  aliado; som de uma vez preso ao avatar ou à cópia é parado quando o FMOD termina (o `AudioController` guardaria a peça até
  a próxima cena). Volume e parâmetros são reaplicados quando o `AudioController` recria a instância (corte por distância).
- *Duas vezes no PC de quem lançou:* não. O som local é o do próprio jogo; o recebido só vem de mensagens dos outros; cópia e
  avatar não tocam nada. O modo eco repete os próprios sons no fantasma de propósito (teste com um PC só).
- *Pendente:* corrotina com espera entre sons fora da lista (o `ProjectileSpawnerConfig.FireProjectilesCoroutine` não sabe
  quem lança: só o primeiro disparo, dentro do `Execute`, vai); sons dos gatilhos que rodam dentro do golpe (ao acertar e ao
matar: `OnDealDamage`, `OnKill`, `TryToApplyAilments`) e loops de status presos ao jogador criados nesse momento, cortados
junto com o som de dor; passos do avatar (`FootL/FootR`), fora desta etapa.

**Golpes vistos (57 GolpesVistos, não confiável, 20 Hz, todos → todos no mesmo andar), `Fase3/GolpesVisiveis.cs`.** Postfix em
`CharacterEffectsController.CreateDamageFX`: golpe do jogador local (atacante ou `SourceOrSelf`) num inimigo compartilhado (ou no
boneco de treino, `DanosVistos.IdBoneco`) manda netId, `EFxOnDamage`, os tipos de dano presentes, o tipo principal (maior parte),
crítico, direção e o ponto do golpe em relação ao inimigo. Quem recebe chama `CreateDamageFX` no mesmo inimigo com um
`DamageInstance.RentDummy` dos mesmos tipos. Cada PC mostra cada golpe uma vez (o `Hurt` direto do host não cria efeito). Com
`SonsDosAliados = false`, vai sem o som de dor.

**Validação na recepção** (`Validacao`, casos no `codectest`): som com GUID não nulo, ponto finito a até 40 m do avatar de
quem mandou, no máximo 40 sons por segundo por aliado, parâmetro com nome de 1 a 64 letras e valor finito; golpe visto só com
os bits conhecidos de `EFxOnDamage` e de tipo, principal entre os presentes, vetores finitos, ponto a até 10 m do inimigo e no
máximo 120 por segundo por aliado, e o inimigo a até 40 m do avatar de quem mandou. Avisos no máximo a cada 5 s por
aliado. Todo texto lido da rede (todos os codecs) passa por `Bin.LerTexto`, que confere o tamanho declarado antes de ler: o
`ReadString` do mscorlib do jogo reserva a memória pelo tamanho declarado. Os alvos são da fase "8" do `PatchTargets`: se uma atualização do jogo
tirar um deles, só o som daquele ponto some (aviso no log) e o resto do mod roda.

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
- *Jogadores:* 1 + aliados compatíveis (igual nos dois lados; desde a 0.21.0 com ou sem a mesma DLC, e fora quem avisou ForaDaRun). Valores do autor, fixos no
  código desde a 0.22.0 (`Escalonamento.VidaGrupo` 1,7/2,4/3,1, `VidaChefeGrupo` 1,8/2,6/3,4, `DanoGrupo` 1,1/1,2/1,3,
  `QuantidadeGrupo` 1,2/1,4/1,6 e `TempoGrupo` 0,55/0,4/0,32 com 2/3/4 jogadores), sem liga/desliga: quem usa o mod usa
  os critérios do mod, e nenhum jogador (nem o host) muda o balanceamento. Até a 0.21.x ficavam na seção `[Balanceamento]`
  do `.cfg`; ao iniciar, a 0.22.0 apaga do arquivo as opções antigas dessa seção (`Escalonamento.Configurar`).
- *XP, ouro, Soulstones e moedas de criação (0.10.0):* decisão do autor, cada um ganha o valor de cada inimigo × fator
  (`Escalonamento.XpGrupo` e `OuroGrupo`, fixos no código desde a 0.16.3: 0,83 / 0,71 / 0,63 = 1/quantidade, para o ganho por andar ficar perto do solo). Só com
  run iniciada e mais de um jogador; aplicado em cada PC, no próprio ganho (o loot é individual). Pontos:
  prefixo em `DropBalance.CalculateExperienceDrop` (`baseExperience`), postfix em `GoldTreasureClass.RollGoldValue` e
  `SoulStoneTreasureClass.RollSoulStonesAmount` (drop e bênção; vale para inimigo, chefe, vaso, baú, loot do andar, goblin e Sanctum, tudo que rola esses tesouros) e prefixo em `CurrencyTreasureClass.RollLoot` (a moeda é
  sempre 1: o fator vira a chance de sair). `Escalonamento.Escalar` arredonda por sorteio, para a média ficar
  valor × fator sem zerar ganhos pequenos (teste no `codectest`).

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
validados; leitura, alteração e exclusão negadas (a análise é feita pelo console ou pelo painel do autor, com o login do
gcloud; as duas leituras passam pelo IAM do projeto, não por estas regras).

**Privacidade (LGPD).** Os resumos são pseudonimizados: só dados do jogo (números, dungeon, resultado, versões), sem
nome, SteamID, log ou caminhos, mas com o id aleatório da instalação, que vai no campo `instalacao` e no id do documento
(`<instalação>-<data>`) e liga as runs da mesma instalação. O autor não tem como ligar um id a uma pessoa; o jogador
tem o próprio id em `instalacao.txt`. O IP chega ao Google no transporte (o envio sai da máquina do jogador pela
`ISteamHTTP`) e não é gravado no documento. Guarda: até o fim da calibragem do balanceamento; depois o autor apaga a
coleção `runs` pelo console e a cópia local do painel (`build/painel-balanceamento.html`). Exclusão antes disso: o jogador abre uma issue com o conteúdo de `instalacao.txt` e o
autor apaga `runs/<id>-*` pelo console. Para parar, desliga o envio; apagar `instalacao.txt` gera um id novo.

**Aceite (0.8.1).** `Balanceamento/JanelaAceite.cs`: no primeiro início, sem personagem carregado (menu), uma janela
IMGUI pergunta "Enviar resumos" / "Não enviar". A resposta grava `[Metricas] EnviarResumoAnonimo` e
`[Metricas] AceitePerguntado = true` (o BepInEx salva o `.cfg` na hora) e a pergunta não volta; quem já tinha ligado o
envio à mão é marcado como perguntado. Enquanto aberta, segura a entrada do jogo pelo `BloqueioEntrada` (§12.2; desde a 0.13.1, antes só o módulo de entrada da UI, e o Esc que adiava abria "fechar o jogo?"): o módulo `EventSystem.currentInputModule`
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

**Painel.** `scripts/painel-balanceamento.py` (ferramenta do autor, fora do mod) junta os `run-*.json`, os pendentes
e, com `--firestore`, a coleção `runs` (leitura pelo login do gcloud, por fora das regras) numa página local com solo
× grupo por andar (ver `DESENVOLVIMENTO.md`). Só mostra; o ajuste é do autor, no código.

**Próximos passos.** Ajuste individual por diferença de progressão e calibragem do jogador caído com os dados da dupla (§12.4).

### 12.1 Janela Novidades (versões 0.9.2 e 0.13.0)

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
pelo item "Novidades" do menu do coop (§12.2; desde a 0.13.0, só na reserva; na 0.9.2, um botão abaixo do painel do coop) ou pela tecla
`[Teclas] Novidades` (F10, também em jogo). Desde a 0.9.3, aberta, segura a entrada do jogo pelo `BloqueioEntrada`
(§12.2) e responde ao mouse também em jogo; enquanto aberta, o `Update` do runtime não lê o menu do coop nem as teclas
F8/F9. Setas esquerda/direita ou LB/RB (`JoystickButton4/5` no Input legado) trocam de versão; setas
cima/baixo ou o direcional rolam; Esc, F10 ou B (`JoystickButton1`) fecham. Se o semáforo do jogo não travar
(`BloqueioEntrada.SeguraOJogo` falso), em jogo volta ao comportamento da 0.9.2: só teclado e controle, rodapé só com a
F10 (Esc, B e o mouse chegariam ao jogo), corpo recortado com `GUI.BeginGroup` e rolagem só pelas setas. Com mouse usa
`GUI.BeginScrollView`, que só existe na sobrecarga de 5 parâmetros no jogo (stripping). Sem efeito no balanceamento nem no protocolo.

**Desde a 0.13.0** as novidades são a terceira aba da janela do coop (§12.2) e esta janela vira reserva, só quando a
interface do jogo não pode ser montada. A F10 chama `MenuCoop.MostrarNovidades`: com o menu fechado, abre na aba
Novidades; aberto em outra aba, troca para ela; já nela, fecha (regra pura `RespostaNovidades`, testada no codectest).
A abertura automática espera a janela das Opções do jogo existir (`PainelNativo.EsperandoJanela`, no máximo 60 s; depois, a janela antiga; o painel é montado
quando o botão "Coop" entra no menu do jogo, com nova tentativa a cada 2 s), abre o menu na aba Novidades e, como antes, só deixa Esc, B e o [X]
fecharem depois de 1,5 s (`FechamentoLiberado`; F7, View e F10 fecham sempre). `[Interface] NovidadesVistas` é gravado
ao fechar o menu depois de a aba ter aparecido (`Novidades.MarcarVista`). A aba lista as 4 versões embutidas numa lista
só, montada uma vez (texto fixo da DLL): seção por versão, data, título em destaque, itens e "O QUE TESTAR" nas não
testadas; sem item selecionável, as setas e o direcional rolam (`PainelNativo.Rolar`), além da roda.

### 12.2 Menu do coop e bloqueio da entrada (versões 0.9.3, 0.12.0, 0.13.0 e 0.13.1)

Desde a 0.12.0, `Interface/MenuCoop.cs` guarda o conteúdo, o foco e a entrada, e `Interface/PainelNativo.cs` desenha o
menu com a interface do próprio jogo: uma janela no centro da tela até a 0.23.0 (720 × 900 no canvas de 1080 de altura; 1100 × 960 desde a 0.21.0, §12.10) e,
desde a 0.24.0, uma coluna de 900 colada à direita, na altura toda, que entra e sai deslizando como as Opções (§12.13), montada na
entrada do botão "Coop" na lista do menu do jogo (`BotoesDoJogo`, 0.13.0; até a 0.12.0, na primeira exibição do botão "Menu do coop"), na abertura automática da aba Novidades ou na primeira abertura, com cópias, em tempo de execução, das peças da `HellClock.UI.Options.OptionsWindow` de PC (a de
celular, `OptionsScreenMobile`, fica de fora): fundo `Container/MiddleLeftAnchor/Background`, cabeçalho `Header` (linhas,
título "COOP" e o [X]), botão de aba `SwitchButtons/GameplayButton` (uma cópia por aba: Sessão, Ajustes e, desde a 0.13.0, Novidades; desde a 0.18.0, Salas entre Sessão e Ajustes), fundo do conteúdo
`Content/Bg`, título de seção `TitleGameplay`, a primeira linha `ToggleWithLabel` (caixa de marcar) e a primeira
`SliderWithLabel` (barra). Fontes (Avara, Avrile), sprites e cores são os do jogo; nada é extraído para o repositório
nem para o pacote.
- Das cópias saem todos os scripts antes de ativar (copiadas para um depósito inativo, o `Awake` não roda): ficam só
  `Graphic` (imagens e textos, incluindo o `RTLTextMeshPro` do jogo, que é um TMP), `Mask`, `RectMask2D`, `CanvasGroup` e
  `CanvasRenderer`. Saem `ToggleWithLabel`/`SliderWithLabel` (presos aos `BoolState`/`FloatState` das opções), o
  `TextMeshLocalizer` (trocaria o texto do mod), sons, `ButtonAnimator`, `Animator`, botões e layouts. Assim o menu não
  liga nada às opções do jogo nem passa pelo `OptionsWindow.Save()`; os valores vão só para o `.cfg` do mod.
- Canvas próprio em sobreposição (ordem 900, abaixo do aviso de save do jogo), escala do jogo (1920 × 1080 pela altura:
  em ultrawide a janela não estica). Sem `GraphicRaycaster`: o mouse é lido pelo `MenuCoop` (Input System, legado de
  reserva) e testado nos retângulos do painel (`AcaoSob`, que devolve a ação desenhada e não o índice; `AbaSob`, `FecharSob`, `NaBarra`, `FracaoNoDeslizante`).
- Botão "Coop" nas listas de botões do jogo (0.13.0, `Interface/BotoesDoJogo.cs`, sem patch Harmony). Na tela de saves,
  cópia do botão "Créditos" (`SaveSelectionScreen._creditsButton`) em `ButtonsContainer`, à esquerda dele (a lista do
  canto superior direito: Créditos, engrenagem, X). Há duas `SaveSelectionScreen` na cena; vale só a ativa, então o
  botão nasce quando a tela de saves aparece. Na pausa, cópia do item "Opções" (`PauseHUD.OptionsButton`) logo abaixo dele
  na coluna `BottomAnchor` (`VerticalLayoutGroup` de altura fixa, encostada no título), que cresce uma linha para baixo.
  As cópias são feitas num depósito inativo (`HellClockCoop.DepositoDeBotoes`) e só perdem o `TextMeshLocalizer` (trocaria
  "Coop" pelo texto original) e o atalho do controle (`JoystickFocusPoint._hasShortcut` falso, `ButtonHotkey` apagado);
  ficam `Button` (com `ButtonClickedEvent` novo), `BaseButtonBehaviour` (destaque), `UIElementSounds`, `ButtonHitZone`,
  `CanvasGroup` (espelha o do original a cada quadro) e `JoystickFocusPoint`. O clique só marca o pedido; o `Update` do
  runtime abre o menu (na pausa, antes chama `PauseHUD.Unpause`, como o "Continuar"). Busca a cada 2 s enquanto a cópia da
  tela atual não existe (troca de cena recria). Falha na cópia do menu: volta o botão do canto descrito abaixo; na pausa,
  fica só a F7/View.
- Reserva do botão do menu (desde a 0.13.0, só com a cópia acima falhando): "Menu do coop (F7)" abaixo do painel do
  canto, outra cópia do botão de aba, num segundo canvas (`HellClockCoop.BotaoDoMenu`, ordem 890). O `MenuCoop.DesenharBotaoMenu` (OnGUI) chama `PainelNativo.BotaoDoMenu`, que o põe nos pixels do OnGUI
  divididos por `Screen.height / 1080` e acende o destaque com o mouse em cima; um `GUI.Button` invisível na mesma área
  recebe o clique. O `EsconderBotaoParado`, no começo do `AtualizarMenus` (antes da saída com a Novidades aberta), apaga o
  canvas quando o OnGUI deixa de pedir o botão (menu aberto, run, Novidades, aceite). Nas telas de abertura, antes de a
  `OptionsWindow` existir, o botão não aparece (`EsperandoJanela`); com a janela presente e alguma peça faltando, o
  botão simples do OnGUI.
- Sem patch Harmony novo e sem mudança no `BloqueioEntrada`: o módulo de entrada da UI do jogo continua desligado com o
  menu aberto, porque a navegação é do mod.
- Se uma peça não é achada (ex.: o jogo renomeou), o menu usa a gaveta desenhada no OnGUI (IMGUI, 1080p escalado, à
  direita, 30% da tela entre 380 e 460 px, lista única com o conteúdo das duas abas), como na 0.9.3; se a montagem ou o
  desenho falham, o painel nativo fica desligado até reabrir o jogo. O log diz qual abriu
  (`[Menu] Menu do coop aberto (...; interface do jogo|menu simples)`).

Conteúdo das três abas (LB/RB, setas laterais fora de um deslizante ou clique na aba):
- **Sessão:** recado do atualizador desta abertura (0.13.0; antes ficava no painel do canto), estado da sessão (e, em
  jogo, o aviso de que o jogo continua rodando), ALIADOS (só leitura, `CoopRuntime.EstadoDoPar`, o mesmo texto do painel
  do canto) e COOP (criar e convidar, sair com confirmação, modo eco).
- **Ajustes:** aliados na tela (nomes, barra de vida e seta fora da tela desde a 0.14.0, números de dano e tamanho do dano), som (sons das skills dos aliados e volume) e
  balanceamento só para leitura (`Escalonamento.LinhasDoMenu`: os multiplicadores do número de jogadores atual, ou de 2
  fora do coop; desde a 0.22.0 todos são valores do autor, fixos no código). Na reserva (gaveta), também o item Novidades.
- **Novidades** (0.13.0): §12.1.
- Deslizantes: esquerda/direita andam um passo (tamanho 0,3–2 em passos de 0,1, a mesma faixa que `DanosVisiveis`
  aplica; volume 0–1 em passos de 0,05); clique ou arrasto na barra escolhe o valor (o clique no rótulo só foca). Regras puras testadas no codectest:
  `ProximaAba`, `AjustarNumero`, `NumeroPelaFracao`.

No menu do jogo escurece o resto da tela; em jogo não escurece nem pausa (o jogo continua rodando e o personagem fica
parado). O foco é guardado pela ação, não pelo índice, porque a lista muda (ex.: "Sair do coop" some ao sair); a regra
de navegação (`ProximoFoco`, pula cabeçalhos, informação e item indisponível, gira nas pontas) e a da confirmação de
saída (`ConfirmaSaida`) são puras e testadas no codectest. Altura mínima das linhas: 48 no canvas e 44 px reais em
telas abaixo de 1080p. O painel de status do canto mostra, desde a 0.13.0, só a sessão (aliados e balanceamento), o
modo eco e os avisos curtos de 6 s: saiu a dica "F7 abre o menu do coop" (os botões "Coop" a substituem) e o recado do
atualizador foi para a aba Sessão.

`Interface/BloqueioEntrada.cs` segura a entrada do jogo enquanto o menu do coop, a Novidades ou (desde a 0.13.1) a janela do aceite (§12) estão abertos:
- Semáforo do próprio jogo: `HellClock.GameInput.InputHandler.DisableInput` (`Semaphore` do K10, o mesmo que o console
  de cheats do jogo usa), travado com `BlockButDoNotIncrease(dono)` e solto com `Release(dono)`. Travado, o
  `InputHandler` responde falso em `GetActionDown/State/Up`, `GetAnyButtonDown` e nos eventos de ação
  (`EventForAction`), que cobrem o clique de mover (`MoveInteract`), skills, poção, pausa (`PauseButton`, `PauseGame`),
  `Back` e as janelas do jogo (`WindowController`).
- Os eixos não passam pelo semáforo (`GetAxisRaw`/`GetAxis` leem o `Rewired.Player` direto): o patch
  `BloqueioEntrada_InputHandler_Update` (Alvo `InputHandler.Update`, fase 5 em `PatchTargets.cs`, fora das fases ativas)
  zera `MovementDirection` e `AimDirection` depois da leitura. WASD e o analógico ficam parados.
- Desliga também `EventSystem.currentInputModule` (o `RewiredStandaloneInputModule` da UI do jogo).
- Desde a 0.13.0 desliga também o `HellClock.UI.AndroidBackButtonListener` da tela de saves (procurado na abertura e, desde a 0.13.1, de novo a cada 0,5 s enquanto travado — desde a 0.26.0 só sem personagem: em jogo o `Resources.FindObjectsOfTypeAll` custava ~55 ms por busca e a busca é uma por abertura, repetida se o personagem sumir com o painel aberto —, porque a janela do aceite abre antes de a cena com ele carregar; religado na liberação; antes, procurado uma vez por
  abertura, religado na liberação): ele lê o Esc direto (`Input.GetKeyDown(Escape)`, por fora do semáforo) e, no mesmo
  quadro em que o Esc fechava o menu do coop, abria "Você realmente quer fechar o jogo?".
- O painel chama `Pedir()` a cada quadro aberto; o `Update` do runtime chama `Atualizar()` antes de tudo, que libera só
  dois quadros depois do último pedido (`DeveLiberar`, testada): o Esc ou o clique que fechou não chega ao jogo.
- Nunca usa `InputHandler.Instance`: o getter cria um `InputHandler` se não houver (como o `PlayerController.Instance`).
  Lê o campo estático `_instance` e guarda o semáforo travado; se a cena troca o `InputHandler`, solta o antigo e trava
  o novo. Sem `InputHandler` (ou sem o campo), `SeguraOJogo` fica falso e, em jogo, menu e Novidades respondem só a
  teclado e controle.
- A confirmar no jogo: abrir o menu com o botão do mouse segurado pode engolir o "soltar" do jogo; o botão View pode ter
  função no jogo no mesmo quadro em que abre o menu.

Mapa dos controles do mod (fonte canônica; README e LEIA-ME descrevem para o jogador). Efeito: R = navega ou abre,
S = executa uma ação, P = grava no `.cfg`, ∅ = só leitura. Ícones: nenhum do mod (o [X], a caixa de marcar e a barra são as imagens do próprio jogo; na reserva, só texto).

| ID | Controle | Onde | Efeito | `.cfg` |
|---|---|---|---|---|
| COOP-MENU-TECLA | F7 abre e fecha o menu do coop | menu e jogo | R | `[Teclas] MenuDoCoop` (a antiga `CriarOuConvidar` fica sem uso; `SairDoCoop`, a F6, saiu) |
| COOP-MENU-VIEW | View/Select do controle (`selectButton`, `JoystickButton6` no legado) abre e fecha | menu e jogo | R | — |
| COOP-MENU-ABRIR | Botão "Coop" na lista do canto superior direito da tela de saves, à esquerda de "Créditos" (0.13.0: cópia do botão do jogo, com destaque, som e foco do controle). Reserva, se a cópia falhar: "Menu do coop (F7)" abaixo do painel do canto (peça de aba das Opções; sem ela, o botão simples do OnGUI) | só no menu (sem personagem) | R | — |
| COOP-MENU-PAUSA | Item "Coop" na pausa do jogo (Esc em jogo), abaixo de "Opções" (0.13.0: cópia do item do jogo): tira a pausa e abre o menu do coop | jogo, com a pausa aberta | R | — |
| COOP-MENU-FECHAR | [X] no cabeçalho; Esc, B, F7 ou View | menu aberto | R (libera a entrada 2 quadros depois) | — |
| COOP-ABA-SESSAO / COOP-ABA-AJUSTES / COOP-ABA-NOVIDADES | Abas "Sessão", "Ajustes" e "Novidades" (0.13.0) (botão de aba das Opções do jogo); LB/RB, setas laterais fora de um deslizante ou clique | janela (0.12.0); na reserva, Sessão, Salas (0.18.0) e Ajustes numa lista só | R | — |
| COOP-NOVIDADES-LISTA | Aba Novidades: versão atual e as 3 anteriores, com "O que testar" nas não testadas; setas, direcional e roda rolam | aba Novidades | ∅ | `[Interface] NovidadesVistas` (gravado ao fechar) |
| COOP-NOVIDADES-TECLA | F10 abre o menu na aba Novidades, troca para ela ou, já nela, fecha; abre sozinha uma vez por versão (Esc, B e [X] só depois de 1,5 s) | menu e jogo | R | `[Teclas] Novidades` |
| COOP-ALIADO | Linha por aliado: nome e estado | aba Sessão, em sessão | ∅ | — |
| COOP-EXPULSAR | "Expulsar <nome>" abaixo de cada aliado; o primeiro aperto vira "Confirmar: expulsar <nome>?" por 3 s (0.18.0, §12.7) | aba Sessão, só o host | S (`SessaoCoop.Expulsar`) | — |
| COOP-BLOQUEAR-ALIADO | Ação secundária da linha do aliado (0.23.0, §12.12): X no controle, Delete ou clique direito (no painel nativo e na reserva); o primeiro aperto vira "Confirmar (X de novo): bloquear <nome>?" por 3 s. Como convidado a linha é "Bloquear <nome>" e o Enter/A também bloqueia. O rodapé mostra "X: bloquear" com o foco na linha | aba Sessão, em sessão | S + P (`SessaoCoop.Bloquear`; host também expulsa) | `bloqueados.txt` |
| COOP-SALA-PUBLICA | "Sala pública (aparece na aba Salas)" (caixa de marcar); vale para o próximo coop e, como host, muda a sala na hora; convidado não muda (0.18.0) | aba Sessão (sem sessão ou como host) | P + S (`SessaoCoop.AlternarSalaPublica`, `SetLobbyType`) | `[Coop] SalaPublica` |
| COOP-NOME-SALA | "Nome da sala:  <nome>" com "Renomear" à direita (0.25.0, §12.14). Enter ou clique: a linha vira campo ("_" piscando, "17/32", embaixo "Enter salva · Esc cancela · vazio volta a "Sala de <você>""); Backspace apaga; clique fora, trocar de aba ou fechar cancela. No controle, o A abre o teclado de tela da Steam (Big Picture e Steam Deck; fora deles, aviso e edição pelo teclado). Desde a 0.26.0 as letras vêm dos eventos de tecla do OnGUI (`Event.character`), porque no jogo o `Keyboard.current` do Input System é nulo e o `Input.inputString` foi removido pelo stripping. Convidado vê o nome com "só o host muda" | aba Sessão (em coop) | S (`SessaoCoop.RenomearSala`, `hcc_sala`) | — (só a sala atual) |
| COOP-ABA-SALAS | Aba "Salas" (0.18.0), entre Sessão e Ajustes; abas de 160 para caber 4. De 0.21.0 a 0.25.0 o menu abria nela; desde a 0.26.0 abre na Sessão (F7, View, botão Coop) | janela; na reserva, na lista única depois da Sessão | R | — |
| COOP-SALAS-ATUALIZAR | "Atualizar lista" com a idade da lista ("há 12 s"); "Buscando salas..." indisponível. Busca sozinha uma vez ao entrar na aba (desde a 0.26.1; antes, também a cada 30 s com a aba à vista) e depois só por este botão; no máximo 1 busca a cada 3 s; sem resposta em 15 s, libera nova busca | aba Salas | S (`SessaoCoop.BuscarSalas`) | — |
| COOP-SALAS-SUA-SALA | Seção "Sua sala" no topo (0.25.0, §12.14), só em coop, como host ou convidado, também privada ou cheia: "<nome da sala>  ·  você está aqui" e "Host <nome>  ·  2/4 jogadores  ·  pública" (o tipo só para o host). Só informação, sem foco nem ação | aba Salas | ∅ | — |
| COOP-SALAS-SALA | Um item por sala pública compatível. Desde a 0.25.0 a linha de cima traz o nome da sala (`hcc_sala`; sem ele, "Sala de <host>") e a de baixo começa por "Host <nome>  ·  ". Desde a 0.21.0 em 2 linhas: "Host  ·  Cursed War" e "Nv 34  2/4" em cima, o lugar ("Ato 2, andar 5") embaixo; desabilitada ao conectar; no Ato 4 sem a DLC (§12.10) ou numa run aonde o save não chegou (§12.8), indisponível: à direita "indisponível", o motivo na linha de baixo e uma linha de explicação abaixo da lista. Até a 0.20.0, uma linha "Host  -  lugar" | aba Salas | S (`SessaoCoop.EntrarNaSala`; já num coop, o primeiro aperto vira "Confirmar: sair do seu coop e entrar?" por 3 s) | — |
| COOP-SALAS-BLOQUEAR-HOST | Ação secundária da linha da sala (0.23.0, §12.12): X, Delete ou clique direito, dois apertos ("Confirmar (X de novo): bloquear <host>?"); a sala some da lista. Sala indisponível não recebe foco (não dá para bloquear por ela); sala sem `hcc_dono` válido só avisa | aba Salas | S + P (`SessaoCoop.Bloquear`) | `bloqueados.txt` |
| COOP-AVISO-ATUALIZACAO | Recado do atualizador desta abertura (versão nova instalada ou atualização recusada), no topo da lista (0.13.0; antes no painel do canto) | aba Sessão, quando houver | ∅ | `[Atualizacao]` |
| COOP-CRIAR | "Criar coop e convidar amigos" (sem sessão); desde a 0.26.0 é o primeiro item da aba (foco ao abrir), logo abaixo das linhas de estado; "Esperando o Steam..." e "Conectando..." indisponíveis | aba Sessão | S (`SessaoCoop.HospedarOuConvidar`) | — |
| COOP-CONVIDAR | "Convidar amigos (Steam)"; desde a 0.26.0 no lugar do COOP-CRIAR (primeiro item) | aba Sessão, em sessão | S (reabre o overlay de convite) | — |
| COOP-SAIR | "Sair do coop"; o primeiro aperto vira "Confirmar: sair do coop?" por 3 s | aba Sessão, em sessão | S (`SessaoCoop.Sair`) | — |
| COOP-ECO | "Modo eco (teste com um PC)" LIGADO/DESLIGADO; sem personagem: "entre num save" | aba Sessão | S (`CoopRuntime.AlternarEco`, também F8) | `[Teclas] ModoEco` |
| COOP-CFG-NOMES | "Nomes dos aliados" (caixa de marcar) | aba Ajustes | P | `[Interface] MostrarNomesDosAliados` |
| COOP-CFG-VIDA | "Barra de vida dos aliados" (caixa de marcar) (0.14.0) | aba Ajustes | P | `[Interface] MostrarVidaDosAliados` |
| COOP-CFG-SETA | "Seta do aliado fora da tela" (caixa de marcar) (0.14.0) | aba Ajustes | P | `[Interface] MostrarSetaDosAliados` |
| COOP-CFG-DANO | "Números de dano dos aliados" (caixa de marcar) | aba Ajustes | P | `[Interface] MostrarDanoDosAliados` |
| COOP-CFG-DANO-TAM | "Tamanho do dano" (barra, 30–200%; indisponível com os números desligados) | aba Ajustes | P | `[Interface] TamanhoDanoDosAliados` |
| COOP-CFG-DANO-OPAC | "Opacidade dos danos dos aliados" (barra, 10–100%, padrão 50%; indisponível com os números desligados) (0.26.0) | aba Ajustes | P | `[Interface] OpacidadeDanoDosAliados` |
| COOP-CFG-SOM | "Sons das skills dos aliados" (caixa de marcar) | aba Ajustes | P | `[Audio] SonsDosAliados` |
| COOP-CFG-VOL | "Volume dos sons" (barra, 0–100%; indisponível com os sons desligados) | aba Ajustes | P | `[Audio] VolumeDosAliados` |
| COOP-BAL-LEITURA | Multiplicadores do balanceamento, todos fixos do autor (XP e ouro desde a 0.16.3; vida, chefes, dano, quantidade e tempo desde a 0.22.0) | aba Ajustes | ∅ (só leitura; ninguém muda) | `Escalonamento.VidaGrupo`/`VidaChefeGrupo`/`DanoGrupo`/`QuantidadeGrupo`/`TempoGrupo`/`XpGrupo`/`OuroGrupo` |
| COOP-CFG-BLOQUEADOS | Seção "Bloqueados (n)" (0.23.0, §12.12): um item por bloqueado (nome, "Desbloquear" à direita; Enter/A/clique, dois apertos: "Confirmar: desbloquear <nome>?" por 3 s), até 50 e uma linha "E mais N no arquivo"; vazia: "Nenhum bloqueado." e a dica de onde bloquear; sempre a linha do bloqueio da Steam | aba Ajustes | P (`SessaoCoop.Desbloquear`) | `bloqueados.txt` |
| COOP-NOVIDADES | "Novidades da <versão>": fecha o menu e abre a janela antiga (§12.1) | só na reserva (gaveta); no painel nativo virou a aba Novidades (0.13.0) | R | `[Teclas] Novidades` (F10) |
| COOP-NAV | Setas ou direcional movem o foco; esquerda/direita ajustam a barra focada; Enter ou A executam; mouse: passar por cima foca, clique executa, clique ou arrasto na barra escolhe o valor, roda rola | menu aberto | R | — |
| COOP-ECO-TECLA / COOP-DIAG-TECLA | F8 (modo eco) e F9 (diagnóstico) | jogo, com o menu do coop e a Novidades fechados | S | `[Teclas] ModoEco`, `SalvarHierarquia` |
| COOP-MARCADOR-TECLA | Botão do meio do mouse (marca o chão sob o cursor) ou R3 no controle (4 m à frente) (0.17.0, §12.6) | jogo, com o menu do coop e a Novidades fechados, sem pausa, com alguém no mesmo andar ou o eco ligado | S (`Marcadores.LerTecla`; recarga de 1 s) | `[Teclas] Marcador` (o R3 é fixo) |

Sem efeito nos números do balanceamento nem no protocolo (10). Com o menu aberto em jogo o personagem fica parado e o
jogo segue (em coop não há pausa), o que só deixa o jogo mais difícil para quem abre o menu no meio do combate.

### 12.3 Aliados na tela: barra de vida e seta (versão 0.14.0, protocolo 15)

Entrega 1 de 4 da etapa "aliados na tela e reanimação" (as próximas: jogador caído, reanimação, calibragem).
- **Rede:** o `PlayerSnapshot` (formato 4; 5 desde a 0.28.0, com a barreira) leva a fração da vida do remetente (`LifeController.CurrentRatio`, 0 se morto)
  em 16 bits, logo depois de `Morto`. Não há mensagem nova: a vida vai nos 20 snapshots por segundo, no canal não
  confiável. O `RemotePlayerAvatar` guarda a do último pacote aceito (`Vida`). `codectest`: ida e volta e o valor preso em 0..1.
- **Visual (`Interface/AliadosNaTela`, opção A do autor: peças do jogo):** a barra é uma cópia da barra flutuante que o
  jogo põe sobre o companheiro (`UIObjectPoolController._companionFloatingLifeView`), sem os scripts (`PainelNativo.Limpar`),
  com nome, textos, escudo e dano atrasado apagados; ficam o preenchimento (`_fill`) e a faixa clara que alcança a vida
  devagar (`_backgroundFill`). A seta é a imagem de seta da HUD (`SurvivalView._arrowSprite` ou `QuestTabBar._foldoutArrow`; no teste veio
  `UI_Icon_DropDownArrow`, que aponta para baixo, em 44 unidades), girada para o aliado e presa a 56 px (em 1080p) da borda, com o nome do aliado na fonte da barra do jogo, para dentro da tela.
  Tudo vai para `GenericHUD.LifeBars`, onde o jogo põe as barras dele, e some com a HUD. `GenericHUD.Instance` é
  Guaranteed: HUD e pool são achados com `Resources.FindObjectsOfTypeAll`. Posição e preenchimento no `LateUpdate` do
  `CoopRuntime`, depois dos avatares (medidor: parte Interface).
- **Quando aparece:** barra com o corpo do aliado na tela (20 px abaixo do centro do nome; sem nome, no lugar dele);
  seta com o corpo fora da tela ou atrás da câmera. Só aliados com avatar ativo, isto é, no mesmo andar.
- **Reserva:** sem a HUD do jogo (menus, carregamento, até 2 s depois de trocar de cena) nada aparece, nem a reserva; com a
  HUD mas sem a barra ou a seta do jogo, ou com erro, o
  `OnGUI` desenha barra e seta simples até reabrir o jogo.
- **Modo eco:** o fantasma entra como aliado "Eco". `[Teste] DistanciaDoEco` (padrão 2,5 m, limite ±60) afasta o fantasma para o lado;
  com 30 ele fica fora da tela e a seta aparece. Longe, ele atravessa paredes e sai do mapa (é só a sua pose deslocada,
  sem colisão) e os números de dano dele seguem no seu alvo (`DanosVisiveis` repete os seus no mesmo inimigo): serve só
  para ver a seta.
- **Balanceamento:** só informação na tela; dificuldade e multiplicadores iguais.

### 12.4 Jogador caído (versão 0.15.0, protocolo 16)

Entrega 2 de 4 da etapa "aliados na tela e reanimação" (as próximas: reanimação com métricas no resumo, calibragem com
os dados da dupla). Decisões do autor em 22/11/2026: o caído volta no próximo andar com 50% da vida, cada queda tira
10 s do Hell Clock, a escala continua contando o grupo todo, com o host caído o portal do convidado leva o grupo, e o
modo eco conta como aliado de pé (só teste).
- **Queda (`Fase2/JogadorCaido`):** prefixo em `PlayerController.Handle_OnDie` (privado) do jogador local. Em run com
  aliados (host com convidado, convidado ou eco), a morte do jogo (`PlayDeathAnimation`: pausa, esconde a HUD, câmera de
  morte, `FinishDungeon(Defeat)`) não roda. No lugar entra a receita da segunda chance do jogo (`SkillSecondWind`):
  gatilho `dead`, `MovementController.StopNow`, `SkillsController.ResetSkillsExecution`, `CanPerformAction` bloqueado
  (chave do mod), sem alvo e companheiros desligados. A vida fica em 0 (`IsDead`): os inimigos largam o alvo
  (`AlvosAliados` no host) e a regeneração do jogo não age em quem está morto. O `PlayerController.Update` já ignora a
  entrada de quem está morto. O Hell Clock segue correndo: o `TimerManager.Update` do jogo para com o jogador local
  morto, então no host caído o prefixo dele chama `UpdateDefaultMode`/`UpdateRelaxedMode` (mesmas condições do jogo:
  `PauseTimer` livre, run em andamento, sem o truque de tempo infinito, sem fim executado). As quedas não sobem
  `cumulativeTotalDeaths` do save (a morte do jogo não roda); só a morte final conta.
- **Subir de nível caído:** a opção do jogo "restaurar vida ao subir de nível" chama `StatBasedValueController.Refill`;
  um prefixo recusa o `Refill` da vida do jogador local caído (o XP continua contando).
- **Volta:** no postfix de `LevelObjects.SetupPlayerSpawn` de um andar da run (no hub, `LevelBlockInfo` é nulo):
  `ClearDelayedDamage`, `ForceRegeneratePercentage(VidaAoVoltar)` (0,5) (passa por cima do teste de morto), gatilho
  `revive` e as ações liberadas. **Último andar** (decisão do autor): a saída é o portal do fim da dungeon, que não passa
  por `AdvanceLevel` e só o host usa; quando o andar fica limpo (`EnemyManager.OnAllEnemiesCleared`, `HostAndarLimpo`)
  e `IsLastLevel`, o host caído levanta com a mesma fração para usar o portal; quem cai depois de o andar estar limpo
  levanta no `Atualizar` seguinte (o evento dispara uma vez por andar). Fim da run com alguém caído (postfix de
  `FinishDungeon`; no convidado só no fim mandado pelo host): a vida volta cheia, sem animação, e as ações são liberadas
  (tempo zerado ou vitória não passam pela morte do jogo).
- **Fim da run:** quem decide é o host. Sem ninguém de pé (o último a cair é o host, ou ele está caído e o último
  convidado cai, sai ou fica 5 s sem sinal), o host roda a morte do jogo (`Handle_OnDie` original), que termina a run e
  manda `FimRun`. Um par conta como de pé se é compatível, tem sinal há menos de 5 s, está no andar ou carregando e não
  está `Morto`. Durante a troca de andar do próprio PC (`IsTransitioning`, `IsChangingLevel` ou cena -1) e por 3 s depois
  dela ou de aceitar um `PedidoAvanco`, o host não decide (os aliados aparecem em outra cena até ele chegar). O convidado
  caído espera o host e só morre como no solo se perder a sessão ou se o host ficar 20 s sem sinal.
- **Tempo:** o host tira `SegundosPorQueda` (10 s) do Hell Clock a cada queda: a dele
  (em `Cair`) e a dos convidados (`Morto` de falso para verdadeiro no snapshot, no mesmo andar, de par compatível), no máximo uma por SteamID e por andar (snapshot fora de ordem ou forjado não cobra de novo). O valor vai direto no
  relógio (`_timer`, ou `_relaxedTimer` no modo relaxado) sem `IncrementTimer`, para não contar como tempo ganho; relógio
  já em 0 ou negativo (modo relaxado) não muda; zerado, o próprio jogo encerra a run por tempo.
- **Rede (protocolo 16):** `PedidoAvanco` (tipo 25, convidado → host: andar atual e quantos andares). O prefixo de
  `AdvanceLevel` do convidado manda o pedido quando o host (pelo snapshot) está `Morto` no mesmo andar; o host só aceita
  caído (filtros na tabela da §14). O avanço do host gera o PlanoAndar de sempre, e todos voltam no andar novo.
- **Visual (`Interface/CaidoNaTela` e `AliadosNaTela`, opção B "Segunda chance"):**
  - Quem caiu: tela em cinza com `CameraPostEffectController.LerpToGrayscale` (1 em 0,5 s; volta a 0 em 0,25 s, e só
    desfaz o que o mod aplicou; `Instance` é Guaranteed, o efeito é achado com `FindAnyObjectByType`) e faixa escura no
    terço de baixo, numa camada própria por cima da HUD (Canvas overlay, ordem 50, escala de 1080p): "VOCÊ CAIU"
    (vermelho), a volta com a porcentagem do `.cfg` e um ponto por jogador (âmbar cheio = de pé) com "N de M de pé".
  - Aliado caído: barra sem preenchimento com o rótulo "caído" sobre a moldura, seta e nome da seta em cinza.
  - Queda de um aliado: faixa no topo, ao centro, por 2,5 s: "Fulano caiu  -10 s" (o tempo só no host, que é quem
    tira). Nada no canto de cima à esquerda.
  - Textos na fonte da barra flutuante do jogo (`AliadosNaTela.Fonte`, achada quando a barra monta). Sem ela, ou com
    erro, a faixa e o aviso saem no `OnGUI`, até reabrir o jogo. Nada é clicável e nada pausa o jogo.
  - **Desde a 0.16.2 (opção C "Aviso de sangue" com o título da A, escolha do autor em 22/11/2026):** a faixa escura
    sai (a reserva no `OnGUI` mantém a faixa). Quem caiu vê, além do cinza: a luz do jogador (`PlayerLightController`, cujo tamanho é o tempo do Hell Clock)
    encolher a 35% em 1 s, presa pela trava `BlockUpdate` com a chave do mod (o fim do `LerpSize` solta a trava do
    jogo) até levantar, quando volta ao tamanho do tempo em 0,5 s; a vinheta de vida baixa (`LowLifeUIController._health`,
    `FullScreenHealthUI.SetIntensity`) pulsando (0,5 ± 0,12 a cada 2,4 s; reanimando, 0,35 a cada 4 s; golpe no
    reanimador, pico de até 0,95, escalado pela opção de intensidade dos flashes, que cai em 0,45 s; nada se a opção da vinheta
    estiver desligada), recalculada ao levantar pelo próprio jogo com a vida nova (`OnHealthChanged`); o título "VOCÊ CAIU"
    acima do personagem, cópia do título da tela de morte (`GameOverScreen._titleMask`, sem os scripts) revelada pela
    máscara em 0,7 s, com o som de abertura dela (`_gameOverShowSfx`, tocado por `FMODUnity.RuntimeManager` para não
    passar pelo patch de sons dos aliados). Se o título do jogo não tiver um texto TMP dentro de uma máscara `Filled`,
    vale o título do mod (64 px, vermelho, surge em 0,3 s); o log registra a hierarquia copiada. As linhas de baixo
    ficam sem fundo, com contorno preto.
  - Avisos de queda de aliado e de reanimação, inclusive "Fulano te levantou" (0.16.2): na pilha de avisos da HUD do jogo
    (`HUDNotificationManager.AddNotification`, cor de raridade vermelha ou âmbar, sem ícone). O rótulo do aviso passa
    pela localização (`SetLocalizedText`), que troca texto sem chave pelo formato de "texto não encontrado": o texto
    do mod leva uma marca invisível e o patch `GenericHudNotificationView.Write` (fase 5) tira o tradutor daquele
    rótulo e escreve o texto como está (o jogo recria o tradutor no próximo aviso dele, e o `richText` volta ao do
    jogo). Sem o patch aplicado ou sem a HUD, o aviso do topo continua.
- **Valores do autor (0.16.3):** `VidaAoVoltar` (0,5) e `SegundosPorQueda` (10) são constantes em `JogadorCaido`, fora
  do `.cfg` (as chaves `VidaAoVoltarDoChao` e `SegundosPerdidosPorQueda` deixam de ser lidas), como a reanimação desde a 0.16.1.
- **Balanceamento:** cair tira o jogador da luta até o fim do andar e custa 10 s a todos, enquanto os inimigos continuam
  escalados para o grupo inteiro. Quem fica de pé luta sozinho contra inimigos de dupla. Comparado à 0.14.0, em que a
  morte do host encerrava a run de todos, o grupo dura mais; o custo de tempo e a escala cheia mantêm o coop um pouco
  mais difícil que o solo. A calibragem (entrega 4) usa os dados da dupla.
- **Menu com você caído (Esc):** o `PauseButton.TogglePause` do jogo recusa a pausa quando
  `PlayerController.IsPlayerBlockedFromPause` é verdadeiro, e ele é verdadeiro com o jogador local morto
  (`LifeController.IsDead`), que é como o caído fica. O `Caido_BloqueioPausa` (postfix) devolve falso com você caído
  numa run em andamento (`RunStarted` e sem `EndingExecuted`). O mesmo método entra no `IsPlayerBlockedFromUIActions`,
  mas ali o `CanPerformAction` bloqueado pelo caído continua barrando inventário e janelas. No coop o menu não congela o
  mundo (`Run_PauseGame`). "Terminar run" no menu (decisões do autor em 22/11/2026):
  - **Host caído:** encerra a run para todos, como o host de pé (`FinishDungeon(ManualFinish)` → `FimRun`).
  - **Convidado caído:** sai sozinho (`RunCompartilhada.ConvidadoAbandonou`, só com `ManualFinish` e você caído). A
    run dele acaba como no solo e os outros continuam. Até o `FimRun` daquele host ou o primeiro plano da run seguinte,
    os planos dele são ignorados, para o convidado não ser puxado de volta no próximo andar. No host, o convidado no
    hub deixa de contar como "na run" (`NaRun`), então não segura a run de ninguém. De pé, o "Terminar run" do
    convidado continua bloqueado. A trava zera também quando este PC deixa de ser convidado (saiu ou caiu da sessão).
  - Caso conhecido, não tratado: na Penitência, no último andar sem inimigos, o "Terminar run" do jogo vira
    `Disappear` + `FinishDungeon(Completed)`, não `ManualFinish`. O convidado caído ali segue bloqueado como o de pé
    (o personagem some e a run continua até o host usar o portal). É raro, porque o andar limpo já levanta o caído.
  - O `Run_FinishDungeon` só devolve a vida do caído (`FimDaRun`) quando o fim roda de fato neste PC (`__state`).

### 12.5 Reanimação do aliado caído (versão 0.16.0, protocolo 17)

Entrega 3 de 4 da etapa "aliados na tela e reanimação" (a próxima: calibragem com os dados da dupla). Decisões do autor
em 22/11/2026: reanimação automática, a até 2 m e parado; 3 s na primeira do andar, 6 s na segunda, 9 s na terceira;
o reanimado volta com 30% da vida; golpe em quem reanima congela e faz o progresso cair; nenhum custo além dos −10 s da
queda; visual da opção B ("barra do aliado").
- **Quem conta (`Fase2/Reanimacao`):** quem reanima, porque só ele sabe se andou, usou skill ou apanhou. A cada quadro
  (depois do `JogadorCaido.Atualizar`), com você de pé na run, o aliado caído mais perto (no plano do chão, pela posição
  do avatar dele) a até `RaioMetros` (2 m) vira o alvo. Parado (`MovementController.IsMoving` falso) e sem skill
  (`PlayerSkillsController.OnSuccessExecuteSkill`, menos a poção, `UsePotionSkill`), o progresso sobe 1 s por segundo
  (`Time.unscaledDeltaTime`). Golpe (`LifeController.OnHurt` com `LifeDamageValue > 0`) congela o avanço por 1 s;
  congelado, o progresso cai 1 s por segundo. Andar, usar skill, sair do raio ou trocar de alvo zera. Sem patch Harmony
  novo: só eventos do jogo, registrados uma vez por personagem.
- **Duração:** `Reanimacao.SegundosBase` (3 s) vezes a ordem da reanimação no andar. Cada PC conta as
  reanimações do andar (as suas e os `Reanimou` que ouviu); a conta zera na troca de andar e no fim da run.
- **Rede (protocolo 17):** `Reanimando` (tipo 26, canal não confiável, a 5 Hz, todos → mesmo andar: alvo, andar, estado
  0 enchendo / 1 interrompido / 2 "fique parado" e fração) e `Reanimou` (tipo 27, confiável, todos → mesmo andar: alvo e
  andar e ordem). O `Reanimando` entra pelo desvio do canal de snapshots da `SessaoCoop`. Cada um é dono da própria vida: o caído levanta na máquina dele (`JogadorCaido.Reanimado`, mesma receita da
  volta: `ForceRegeneratePercentage(VidaAoReanimar)`, gatilho `revive`, ações liberadas). Depois de terminar, o mesmo
  alvo não recomeça por 3 s (o snapshot dele ainda pode dizer caído). Filtros na tabela da §14.
- **Tempo:** com um `Reanimou` válido de outro par (alvo na run, caído ou já de pé, porque o snapshot "de pé" pode
  chegar antes), todos esquecem a queda já vista (`JogadorCaido.EsquecerQueda`): a próxima mostra "Fulano caiu" de
  novo e o host volta a cobrar os 10 s. O alvo nunca é quem manda: um caído não reabre a própria cobrança alternando `Morto` no
  snapshot. A queda do próprio host já cobrava sempre.
- **Conta do andar entre os PCs:** o `Reanimou` leva a `Ordem` da reanimação (1 = primeira do andar); cada PC fica
  com o maior valor visto, subindo no máximo um por mensagem (`Validacao.OrdemReanimacao`): repetida não conta de
  novo, perdida é recuperada na seguinte e uma `Ordem` forjada alta sobe só um. Uma série de `Reanimou` forjados
  ainda alonga a duração para todos (+1 por mensagem); num coop entre amigos esse risco fica aceito. Duas reanimações
  terminadas no mesmo instante levam a mesma `Ordem` e contam uma vez.
- **Snapshot fora de ordem:** a `SessaoCoop` guarda a `Sequencia` do snapshot que decidiu `Morto`; um pacote igual ou
  até 64 atrás não muda `Morto` (`Validacao.SequenciaAtrasada`), para um "caído" velho não cobrar a queda de novo
  depois da reanimação. Diferença maior conta como contagem recomeçada no outro PC.
- **Visual (opção B, `AliadosNaTela` e `CaidoNaTela`):**
  - Barra do aliado caído: barra fina 90x7 logo abaixo dela (trilho preto 62%, preenchimento verde #7FB77E, vermelho
    #B3261E quando interrompido); o rótulo "caído" vira "reanimando" (verde), "interrompido" (vermelho) ou "fique
    parado" (branco 60%). Quem reanima vê o próprio progresso; os outros, o último `Reanimando` (some 0,6 s depois).
  - Faixa de quem caiu: a linha da volta vira "Fulano está te reanimando" ("Fulano está ao seu lado" se ele se mexe)
    e uma barra 360x12 (trilho âmbar 22%) enche entre a linha e os pontos.
  - Fim: aviso no topo, ao centro, em âmbar: "Fulano te levantou", "Você levantou Fulano" ou "Fulano levantou
    Beltrano" (desde a 0.16.2, na pilha de avisos da HUD do jogo, ver §12.4; o topo fica como reserva). O
    preenchimento é pela âncora (o `Image.Filled` sem sprite não corta). Reserva no `OnGUI` igual.
- **Modo eco:** o fantasma não reanima nem é reanimado (ele repete você). O teste é em dupla.
- **Valores fixos (fora do `.cfg`):** `SegundosBase` (3 s) e `VidaAoReanimar` (30%) são constantes da `Reanimacao`,
  iguais em todos os PCs. O autor do mod os calibra; o jogador não os muda (decisão de 22/11/2026). As linhas
  `SegundosParaReanimar` e `VidaAoReanimar` que a 0.16.0 gravou no `.cfg` ficam órfãs e não são lidas.
- **Métricas:** cada andar do resumo ganhou `quedas` (as suas), `reanimacoes` (aliados que você levantou) e `reanimado`
  (vezes que te levantaram). As regras do Firestore só conferem os campos do topo, então o `formato` continua 1 e as
  regras não mudam. `mortes` continua contando o `OnDie` do jogo (queda e morte final).
- **Balanceamento:** reanimar tira quem levanta da luta por 3 s ou mais, e a duração cresce no andar; o reanimado
  volta mais fraco (30%) que na volta do próximo andar (50%). A queda segue custando 10 s, também na repetida. O grupo
  dura mais que na 0.15.0; a calibragem (entrega 4) confere com os dados.

### 12.6 Marcador (ping) no chão (versão 0.17.0, protocolo 18)

Trilha 5 (comunicação sem chat). Decisões do autor em 22/11/2026: botão do meio do mouse e R3 no controle; o marcador
dura 5 s; 1 ativo por jogador (marcar de novo move o antigo), recarga de 1 s; nome de quem marcou na cor do aliado;
visual da opção A ("seta no chão", só peças do jogo).
- **Tecla (`Interface/Marcadores.LerTecla`, no `Update`, com os menus do coop e a Novidades fechados e o jogo sem
  pausa):** `[Teclas] Marcador` (padrão `Mouse2`; `Teclas.Pressionou` passou a aceitar `Mouse0`..`Mouse4`, além das
  teclas) marca o chão sob o cursor: raio da câmera contra o plano horizontal na altura do personagem, limitado a
  `Validacao.AlcanceMarcadorMetros` (30 m; além disso, na borda do alcance, na direção do cursor). Com o cursor sobre a
  interface do jogo (inventário, mapa: `EventSystem.IsPointerOverGameObject`) não marca. O R3 (`rightStickButton`,
  `JoystickButton9` no legado) marca 4 m à frente do modelo do personagem. Só com alguém da sessão no mesmo andar ou
  com o modo eco ligado (sozinho não há quem veja). Caído também marca (pedir ajuda).
- **Rede:** `Marcador` (tipo 28, confiável, todos → mesmo andar): cena (`LevelDefinition.HashID`), índice do andar
  (4294967295 fora da run, no hub) e o ponto. Quem recebe confere cena, andar, ponto finito e a distância entre o ponto
  e o avatar de quem marcou (30 m + 10 m de folga no chão, até 10 m de desnível, `Validacao.MarcadorValido`); um
  segundo marcador do mesmo par em menos de 0,5 s é ignorado (sem linha no log padrão: só `LogDebug`). Não confere a
  DLC (como o resto do que vai pelo `EnviarParaMesmoAndar`; desde a 0.21.0 nada confere a DLC).
  Filtros na tabela da §14.
- **Vida do marcador:** 5 s (`Marcadores.Duracao`) desde a marcação; some antes na troca de cena ou de andar, quando o
  par sai da sessão e, o do eco, quando o eco desliga.
- **Visual (opção A, `Marcadores` com as peças do `AliadosNaTela`):** a seta da HUD do jogo (a mesma da seta dos
  aliados, que aponta para baixo sem giro) 1,2 m acima do ponto, com contorno preto 60% (a mesma seta 18% maior por
  trás) e uma sombra elíptica 40x14 no chão (textura feita por código, sem arte nova). Entra caindo 0,8 m com escala
  1,4 → 1 em 0,25 s e quica uma vez; pulsa ±8%; nos últimos 0,6 s some subindo 0,3 m. Cor: a dos números de dano do
  aliado (`DanosVisiveis.CorDoAliado`; o eco, verde); o seu, na cor da seta do jogo e sem nome. Nome acima da seta, na
  fonte da barra flutuante do jogo. Fora da tela: a seta vai para a borda (`AliadosNaTela.Borda`) apontando o ponto,
  com "Nome 18 m" (o seu, só a distância). Peças na `GenericHUD.LifeBars` (somem com a HUD). Sem as peças, reserva no
  `OnGUI` ("▼" e "►" na cor do aliado), só com a HUD na tela.
- **Modo eco:** o fantasma repete o seu marcador 1 s depois, deslocado como ele (`DistanciaDoEco`), como aliado "Eco".
- **Balanceamento:** só informação, sem efeito no jogo, no dano nem no tempo; não muda a dificuldade do grupo.

### 12.7 Lista de salas, etapa 1/3 (versão 0.18.0, protocolo 19)

Decisões do autor (22/11/2026): sala pública por padrão; entrar também com a run em andamento; mostrar o nível do host;
o host pode expulsar já nesta etapa; visual A (uma linha por sala). Etapa 2: trava pelo progresso da campanha de quem
entra. Etapa 3: segurança com desconhecidos e filtros. Troca de host quando ele sai: só no roadmap (a run em andamento
existe só no host; quem herda o lobby hoje vira "host" sem estado).

- **Tipo do lobby:** `k_ELobbyTypePublic` com `[Coop] SalaPublica = true` (padrão), senão `FriendsOnly`. O host muda na
  hora com `SetLobbyType`.
- **Dados da sala** (o dono do lobby grava a cada 2 s, só o que mudou, `SessaoCoop.AtualizarDadosDaSala`): `hcc_dlc`
  ("1" com Cursed War), `hcc_lugar` (`menu`, `hub` ou `run`), `hcc_ato` (do nome do `DungeonConfig`, "Act02_..." → 2;
  0 fora do padrão, como Sobrevivência), `hcc_andar` (1 = primeiro), `hcc_nivel` (`PlayerSheet.Experience.Level`) e
  `hcc_host`. Quem herda o lobby passa a gravar os dele.
- **Busca:** `RequestLobbyList` com `k_ELobbyDistanceFilterWorldwide` (sem ele a Steam só olha a região), filtros de
  igualdade em `hcc_proto` e `hcc_mod` (até a 0.20.0 também `hcc_dlc`), com vaga e até 50. Cada resultado passa de novo por
  `Salas.SalaVisivel` (versões, vaga, não é a nossa nem uma de onde fomos expulsos). Desde a 0.21.0 `hcc_dlc` só marca a sala (§12.10).
  Desde a 0.23.0 some também a sala cujo `hcc_dono` é um jogador bloqueado (§12.12).
- **Dados não confiáveis:** tudo do lobby vem de qualquer jogador. Nome sem `<`, `>`, controles e formatação invisível
  (U+202E, U+200B), até 32 caracteres sem partir emoji (`Salas.LimparNome`; desde a 0.18.0 vale também para o nome do
  `Hello` e os nomes da Steam do par e do dono do lobby); números fora da faixa viram 0
  (`Salas.LerInteiro`); lugar só os três valores. Casos no `tools/codectest`.
- **Entrar com a run em andamento:** o caminho de sempre (`Entrar` → `LobbyEnter_t` → `Hello`); o host manda o último
  `PlanoAndar` e `SpawnAndar` (`RunCompartilhada.AoNovoConvidado`), como no convite.
- **Expulsar:** o host manda `Expulso` (tipo 13, 1 byte), tira o par de `Pares` na hora (sem avatar, sem conversa, sem
  contar na escala) e deixa a sessão P2P aberta até ele sair do lobby, reenviando a cada 2 s. Quem volta ao lobby não
  vira par e recebe `Expulso` de novo. O convidado só aceita `Expulso` do dono do lobby (`Salas.ExpulsaoValida`), sai e
  esconde a sala da lista até fechar o jogo. A Steam não tem expulsão de lobby: um cliente alterado pode ficar no lobby,
  mas não conversa com o host. O botão "Expulsar <nome>" usa o nome da Steam ligado ao SteamID (o do `Hello` é escolhido
  pelo par) e a confirmação fica armada pelo SteamID, não pelo lugar na lista.
- **Pendências para a etapa 3** (auditoria de 22/11/2026): os outros convidados não sabem da expulsão (um cliente alterado
  que ignore o `Expulso` segue par deles e conta na escala deles; se o host sair, pode herdar o lobby); conferir se o P2P
  com quem não é amigo passa sempre pelo relay da Steam (IP); mudar para privada não tira quem já entrou; e as mensagens
  que levam a progresso salvo (`PlanoAndar`, `FimRun`, inimigos e baús do host; `TimerBonus`, `Golpes` e objetos do
  convidado) precisam de teto ou validação contra desconhecidos.
- **Balanceamento:** sem efeito nos números. Expulsar reduz o grupo e a escala acompanha (`Pares`), como quem sai.

### 12.8 Lista de salas, etapa 2/3: trava pelo progresso (versão 0.19.0, protocolo 20)

Decisões do autor (23/11/2026): o coop não dá progresso de campanha. Regra pela masmorra exata e pelo andar; a sala
aparece travada na lista (não escondida); pelo convite, ou com o host indo mais longe, o convidado fica no coop sem
acompanhar, o host é avisado e a escala deixa de contar esse convidado.

- **Fatos do IL (2.1.1.1):** `PlayerDungeonData.maxFloorReached` recebe `_currentLevelIndex + 1` em
  `PlayerSaveManager.CurrentLevelChanged`, ou seja, já conta a partir de 1, como o `hcc_andar` (0 = nunca entrou). O
  recorde é por `DungeonConfig.HashID`, e cada ato tem um `DungeonConfig` por tier (`WorldTierConfig.GetDungeonConfig`:
  Normal, Abyss, Oblivion, Void). Sobrevivência (`DynamicDungeonConfig`) e Ascensão (`AscensionDungeonConfig`) são
  `DungeonConfig` com recorde próprio. `PlayerData.GetDungeonDataByID` devolve um objeto novo com 0 quando não há
  registro, sem gravar. A liberação de um ato fica em condições dos assets (`HasCompletedDungeonCondition`), que o mod
  não replica. Por isso, o andar 1 de um ato nunca começado conta como não alcançado.
- **Regra (`Salas.Liberado`):** recorde do save na masmorra do host ≥ andar do host. A leitura falha (-1) trava. Hub,
  menu e atalhos especiais (`ESpecialFloor`, sem `DungeonConfig`) não travam.
- **Lobby:** o host grava `hcc_dungeon` (`HashID` da masmorra; 0 fora da run ou em atalho especial). Lido com
  `Salas.LerHash` (qualquer int; lixo vira 0).
- **Aba Salas:** o recorde é lido do save carregado a cada montagem da lista (`RunCompartilhada.RecordeNoSave`, só com
  personagem). A sala travada (`Salas.SalaTravada`) fica indisponível e mostra o recorde no lugar do nível e das vagas.
  Sem save carregado (menu), a sala aparece normal. `EntrarNaSala` confere de novo.
- **Convidado (`RunCompartilhada.AtualizarConvidado`):** ao ir iniciar a run do host (também entrando no meio),
  confere a regra. Se não chegou lá: não inicia, avisa (`Salas.TextoForaDaRun`), manda `ForaDaRun { Fora = true }`
  (tipo 29, 1 byte) a todos os pares (quem entra depois recebe ao chegar, depois do `Hello`:
  `RunCompartilhada.AoNovoPar`) e ignora os planos seguintes desta run. Run nova do host (`Primeiro`) confere de
  novo, e `FimRun` libera. Ao voltar a acompanhar, manda `Fora = false`. A trava vale só na entrada: quem já acompanha
  (`_seguindoHost`, a run foi iniciada por ordem do host) segue o host para andares novos. Quem entra no coop com uma
  run própria aberta também passa pela trava antes de ser avançado até o andar do host, e não acompanha se a masmorra
  for outra.
- **Todos os PCs:** `Par.SemLiberar` (aceito de qualquer par compatível, porque cada um só fala de si) tira o par de
  `Escalonamento.Jogadores` (inimigos, tempo, XP, ouro e réplicas), a não ser que esteja no mesmo andar
  (`Escalonamento.ForaDaEscala`). No host, os `Golpes` e `StatusInimigos` de quem está fora da escala são ignorados.
  Assim, um `ForaDaRun` falso com cena falsa não tira da escala quem ainda bate. O aviso e o log ficam só no host, no
  máximo 1 a cada 30 s por par. Como está no hub, o `JogadorCaido` já não o contava (`MesmoAndar`). Limite: entre o
  plano do primeiro andar e a chegada do `ForaDaRun`, os inimigos que nascem já usam a escala com ele, o que só deixa
  o andar mais difícil.
- **Dados não confiáveis:** a trava roda no PC de quem entra. Um cliente alterado pode pular a trava, mas só ganha
  progresso no próprio save (o host não grava nada do convidado). O `hcc_dungeon` falso de um host só trava ou libera
  a linha na lista: a checagem da entrada usa o `DungeonHash` do `PlanoAndar`. Fica para a etapa 3 junto do resto.

### 12.9 Lista de salas, etapa 3/3 parte 1: segurança com desconhecidos (versão 0.20.0, protocolo 20)

Decisões do autor (23/11/2026): a etapa 3 sai em fatias (3a-1 = 0.20.0, 3a-2 = 0.20.1, 3b bloquear jogador, 3c
filtros). Os tetos valem para todos (amigos e desconhecidos), folgados (≥ 2x o que o jogo gera, quando o IL mostra),
fixos no código, em `Validacao` com caso no `codectest`. **Sem teto de dano de golpe** (quebraria skills cujo dano o
mod não conhece). Nenhum formato de mensagem mudou: o protocolo continua 20.

- **Fatos do IL (2.1.1.1):** `LevelObjects.SetupLevelObjects` cria no máximo 1 santuário amaldiçoado por andar
  (`LevelDefinition.GenerateCursedShrineSpawnInfo` devolve um só). O ponto de constelação da purificação já tem teto no
  jogo: `CursedShrineDefinition.Use` só soma se `DungeonConfig.ClaimShrineLevel(andar)` aceitar (1 por andar, no save).
  O multiplicador de XP das ondas (`SurvivalEventManager._xpMultiplier`), a quantidade de afixos
  (`DungeonConfig._rareAffixesPerFloor`) e a de santuários comuns (`LevelDefinition._shrineRangeAmount`) são campos
  serializados do asset, sem valor no IL: tetos folgados, para recusar só o absurdo.
- **Porta de entrada (`SessaoCoop.TratarConfiavel`, `Validacao.PortaDeEntrada`):** de par sem `Hello` compatível só
  passam `Hello`, `Tchau` e `Expulso`. O canal de snapshots já exigia `Compativel`.
- **FimRun (`Validacao.MotivoFimRunAplicado`):** no convidado, `Completed` (grava a masmorra como concluída) só vale no
  último andar (`IsLastLevel`) de uma run que acompanha o host (`_seguindoHost`); senão vira `ManualFinish`.
- **PlanoAndar com a run aberta (`PlanoDaRunAberta`, `SaltoPlano`):** plano de atalho especial exige o mesmo
  atalho; plano comum exige a mesma masmorra, também saindo de um atalho (a run segue do andar especial para os comuns:
  `AdvanceLevel` zera o `ESpecialFloor`); no máximo 3 andares por plano. O `PlanoValido` conferia o índice contra a masmorra do plano, não contra
  a da run aberta. Limite: convidado com run própria mais de 3 andares atrás cai num andar intermediário sem plano do
  host (bloco sorteado por ele) e só volta a sincronizar no próximo andar do host; numa run acompanhada desde o início
  o salto é sempre 1.
- **Inimigos (`InimigoSpawnValido`, `AjusteValido`):** tipo no `EEnemyType` (0..6), até 32 afixos, marcas de ajuste
  conhecidas, multiplicador de XP finito de 0 a 10, vida de 0 a 1, atraso de spawn de 0 a 30 s, giro e posições finitos
  (até 100 000). Recusa vai ao log com teto por par (`AvisarRecusa`, o antigo aviso de status).
- **Santuários (`ObjetoSpawnValido`, `ObjetoCabeNoAndar`, `ObjetoUsadoAceito`):** tipo conhecido, raio finito de 0 a
  200, posição válida; no convidado, até 2 amaldiçoados e 32 objetos por andar. `Concluido` (purificação) só do host e
  só depois que o santuário foi ativado aqui (uso local, ordem `Usado` ou entrada com a sobrevivência em andamento).
  Limite: um host alterado ainda pode criar o santuário já `Usado` e mandar `Concluido`; o ganho fica no teto do jogo
  (1 ponto por andar, `ClaimShrineLevel`) e só no save de quem recebe.
- **Golpes (`GolpesAceitos`):** convidado `Compativel`, `MesmoAndar` (até a 0.20.0, também `DlcOk`), até 512 itens por mensagem (acima
  disso a mensagem inteira é recusada).
- **IP (`SteamTransporte`):** a SteamNetworkingMessages copia o valor global de `P2P_Transport_ICE_Enable` ao criar a
  sessão. Quando a Steam fica pronta (antes de qualquer lobby) o global vai a 0 (sem ICE, só relay SDR); na 1ª sessão com cada par (envio ou aceite), vai a
  -1 (padrão do usuário) se o par é amigo (`SteamFriends.HasFriend`, `Immediate`) e volta a 0 logo depois. `Fechar`
  esquece o par, para a próxima sessão ser ajustada de novo. Fail-closed: se a Steam recusar o 0, não abre sessão com
  quem não é amigo. Depois do 1º `Hello`, `ConferirCaminho` lê `GetSessionConnectionInfo` e registra no log se a
  conexão é pelo relay ou direta; quem não é amigo fora do relay (flag `Relayed` ausente; `Validacao.CaminhoAceito`)
  é desconectado, sai da escala (`Compativel` falso) e fica recusado até sair do coop (`Esquecer`), o que cobre qualquer ordem de eventos em que a Steam leia o global.
- **Reconexão (`RetomaRunDoHost`):** a queda da sessão zera `_seguindoHost`. Com a run aberta no mesmo andar do plano,
  na mesma run e com o save liberado ali, o convidado volta a acompanhar; sem isso o `Completed` do host viraria
  `ManualFinish` para ele. O `IsLastLevel` só é lido num `Completed`, protegido por `try` (erro = não conclui).
- **Limites conhecidos para a 3a-2:** a quantidade de `InimigoSpawn` por andar não tem teto (cada campo é validado,
  mas um host alterado pode mandar muitos inimigos com XP x10 e o tesouro de um chefe); `InimigosEstado` grava posição
  sem validar também em réplica viva; o salto de 3 andares dá ritmo, não barreira (planos seguidos levam ao último
  andar, como um host honesto com o convidado parado); `StatusInimigos` não exige `MesmoAndar`.
- **Fica para a 3a-2 (0.20.1):** host fixo da run até o `FimRun`, expulsão avisada, `BauAberto`/`Quebrou`/`ObjetoUsado`
  só do host, teto de baús, `AndarLimpo` 1 vez por andar, `TimerBonus` com as mesmas exigências e floats de
  `Snapshot`/`InimigosEstado`.

### 12.10 Salas: DLC Cursed War e menu maior (versão 0.21.0, protocolo 20)

Decisões do autor (23/11/2026), antes da 3a-2: a lista mostra salas com e sem a Cursed War. Sala num andar ou modo da
DLC só aceita quem tem a DLC, e os modos normais não restringem. "Modo da DLC" é só o **Ato 4**: a Ascensão nos atos
1 a 3 fica livre, porque a Penitência é a árvore do save de cada um. O menu do coop passou a abrir direto na aba Salas (até a 0.25.0), numa
janela maior (visual A das 3 opções: lista larga, com cada sala em 2 linhas).

**O que o IL e a instalação mostram (2.1.1.1):**
- A Cursed War não tem depot próprio. A instalação tem o depot base (1782461) e o 5326530, que é só o Art Book da
  Deluxe. O conteúdo da DLC vem no jogo base de todo mundo e é liberado pela posse: `DlcConfig.HasCursedWar` =
  `SteamApps.BIsDlcInstalled(_cursedWarDlcAppId)` (ou `GameConfig.UnlockAllDlc`). Ou seja, o convidado sem a DLC
  carregaria o Ato 4 sem erro; a trava é regra nossa, para não dar conteúdo pago a quem não tem.
- Usos de `HasCursedWar`: `GameConfig.MaxActEnabled` (4 com a DLC); `CorruptedShrinesInfo.GetPresentationData` (tira
  o `EDungeon` 8 = Act04 sem a DLC); `StartDungeonPortalInteractable` e `PenanceHudView` (Penitência na Ascensão);
  `PlayerSaveData.TryToUnlockAct4`/`SkipCampaign`; cosméticos; `HasCursedWarDLCCondition`.
- `HasCursedWarDLCCondition` é uma `Condition` usada pelos assets. Entre os campos que aceitam condições estão a
  disponibilidade de relíquias únicas e de afixos de relíquia, `EnemySpawnComposition`, `BlessingLootTreasureList`, os
  tiers de pledge e as bênçãos de Miracles. Quais assets usam a condição da DLC não aparece no IL: fica nos bundles do
  Addressables. Efeito no coop: o loot é sorteado no jogo de cada um, com as condições avaliadas pela DLC de quem pega.
  A composição de inimigos é do host e chega replicada. Então, numa run do jogo base com host que tem a DLC, o
  convidado sem a DLC pode ver nos atos 1 a 3 inimigos que a DLC liberou, se algum asset fizer isso. É aceito pela
  decisão de que os modos normais não restringem.
- `EDungeon` do Ato 4: Act04 = 8, Act04Endgame = 9, Act04Abyss = 13, Act04Oblivion = 17, Act04Void = 21. O jogo troca
  `EDungeon` por `DungeonConfig` em `GameDifficultyController.GetDungeonConfig`: na Ascensão, por
  `DungeonConfig.GetByID((int)e)`; fora dela, por `WorldTierConfig.GetDungeonConfig(e)` do tier atual (que cai no mesmo
  `GetByID` quando o tier não tem a masmorra).

**Como ficou:**
- Fim do `Par.DlcOk`. Tudo o que exigia `Compativel && DlcOk` passa a exigir só `Compativel`: escala, run, golpes,
  status, caído, reanimação, objetos, santuários e alvos. `Validacao.GolpesAceitos` perdeu o parâmetro da DLC. As
  mensagens não mudaram (protocolo 20); a versão do mod (0.21.0) separa quem tem a regra nova.
- `RunCompartilhada.MasmorraDaDlc(hash)` junta os `HashID` das masmorras do Ato 4 pelos dois caminhos acima, em todos
  os tiers de `GameConfig.WorldTierConfigs`. Só uma leitura completa e não vazia fica guardada; até lá (dados ainda
  não carregados ou exceção no meio), cada chamada lê de novo e confere também o nome da masmorra ("Act04..."). Falha
  de leitura avisa uma vez no log. O convidado sem a DLC que recebe o plano
  de uma run do Ato 4 não acompanha. O caminho é o mesmo da trava de progresso (`_semLiberar`, `ForaDaRun` aos pares,
  fora da escala), com o aviso `Salas.TextoForaDaRunDlc`. Na escala, `Escalonamento.ForaDaEscala` também tira o par
  sem a DLC fora do nosso andar numa run do Ato 4 (`RunCompartilhada.RunAtualDaDlc`), sem esperar o ForaDaRun, que não
  chega se ele está no menu. Sem a DLC o save nunca chega ao Ato 4, então a trava de
  progresso já barrava; a conferência explícita cobre o save que teve a DLC antes e dá o motivo certo.
- Lista: a busca não filtra mais `hcc_dlc`, que vira `SalaListada.CursedWar` (a marca "Cursed War" ao lado do host).
  `SessaoCoop.AtualizarTrava` roda a cada quadro da aba e de novo em `EntrarNaSala`. Ela calcula o recorde (§12.8) e
  `SemDlc`: sala na run com `hcc_ato` = 4, ou masmorra do Ato 4 (`MasmorraDaDlc` no hash do lobby, conferido com os
  dados do próprio jogo), e este PC sem a DLC. Mentir no lobby só trava mais; quem decide acompanhar é o plano do host,
  conferido no convidado.
- No Hello com DLC diferente, o aviso é `Salas.TextoDlcDiferente`: jogam juntos os atos 1 a 3, e o Ato 4 é só de quem
  tem a DLC.
- Menu: `MenuCoop.Abrir` abria na aba Salas (até a 0.25.0; desde a 0.26.0, na Sessão); a abertura automática das Novidades continua na aba Novidades. O painel
  nativo passou a 1100 × 960, que cabe em 4:3 (canvas de 1440 × 1080). `MenuCoop.Linha.Detalhe` é a segunda linha do
  item. No painel, ela é um texto "Detalhe" (cópia do texto da linha, no tamanho de informação, recuado) na faixa de
  baixo, de 26, e o título e o valor sobem para a faixa de cima. Na reserva (gaveta, agora com até 720 de largura e
  40% da tela), é um rótulo a mais, de 22. Na sala: `Salas.TextoSala` (host e marca), `ValorSala` ("Nv 34  2/4" ou
  "indisponível") e `DetalheSala` (o lugar e, se indisponível, o motivo; a DLC vem antes do recorde).
- codectest: golpes sem DLC, `SalaVisivel` sem DLC, as 2 linhas da sala, a marca da DLC, `PrecisaDaDlc`,
  `SalaIndisponivel` e os textos novos.

### 12.11 Lista de salas, etapa 3/3 parte 2: host fixo, expulsão avisada e tetos (versão 0.21.1, protocolo 21)

Segunda fatia da segurança das salas (decisões do autor de 23/11/2026, §12.9): tetos valem para todos (amigos e
desconhecidos), folgados, fixos no código; **sem teto de dano de golpe**.

**Host fixo da run.** `SessaoCoop._hostDaRun` guarda o SteamID do host enquanto o convidado está numa run com ele
(`RunCompartilhada.Atualizar` chama `FixarHostDaRun` com a run aberta e `SoltarHostDaRun` fora dela; o `FimRun` também
solta, e `Sair` zera). `SessaoCoop.IdDoHost` (`Validacao.HostEfetivo`) é, sem run, o dono do lobby e, com run, o host
dela só enquanto ainda é o dono; se o lobby mudou de dono no meio da run, ninguém (0); `Host` (e por ele `ModoConvidado`,
os planos, inimigos, baús, santuários e o fim da run) e a validade do `Expulso` passam a usar `IdDoHost`. Se o host sai
no meio e a Steam passa o lobby a outro membro (até a um expulso que ignorou o `Expulso`), o novo dono não comanda a run
aberta de ninguém, e o host original que sai e volta como membro comum também não (auditoria: ele poderia expulsar o
novo dono); a run segue sem novos andares do host e, na volta ao hub, vale o dono do lobby de novo. Se a Steam passa o
lobby a um expulso, o convidado recebe um aviso único para sair e entrar em outra sala.

**Dono do lobby guardado (0.30.2).** `SouHost` e `IdDoHost` leem `SessaoCoop._donoDoLobby` em vez de chamar
`SteamMatchmaking.GetLobbyOwner` a cada leitura (etapa 3 do trabalho de desempenho: eram de 14 a 21 chamadas à Steam por quadro
como host de sala vazia). O campo é relido da Steam em três pontos: no início de `SessaoCoop.Atualizar` (todo quadro, no
`LateUpdate`, antes do reenvio do `Expulso`, da checagem do dono expulso e do `Receber`), em `OnEntrouNoLobby` (antes de
o estado virar `NoLobby`) e em `OnMembrosMudaram` (antes do `AtualizarMembros`); `Sair` zera. Para a segurança: `Expulso`,
`ExpulsoAviso`, planos e ordens são conferidos contra o dono lido no mesmo quadro em que chegam, como antes; fora do
`Atualizar` (ganchos e menus, no `Update`) o valor tem no máximo um quadro. Não há callback de `LobbyDataUpdate_t`: a
releitura por quadro não depende de qual aviso a Steam manda quando o dono muda. Trocar por um cache só de eventos
(zero chamadas por quadro) abre uma janela maior que um quadro e pede nova auditoria deste trecho.

**Expulsão avisada.** Mensagem nova `ExpulsoAviso` (tipo 14, 8 bytes do SteamID): o host manda aos outros convidados
compatíveis ao expulsar, a quem chega depois (Hello) e de novo a cada 2 s, junto com o reenvio do `Expulso`, enquanto
o expulso está no lobby (quem estava com outro host fixado recusou o primeiro; repetido não tem efeito). Quem recebe confere
`Validacao.AvisoExpulsaoValido` (do `IdDoHost`, sobre um terceiro), põe o id em `_expulsos` e remove o par: sem
avatar, sem conversa, fora da escala. `AtualizarMembros` já pulava `_expulsos`; o convidado também recusa pedido de
sessão de um expulso (o host aceita, porque é por ela que o `Expulso` chega). Quem herdar o lobby já reenvia o `Expulso`
aos expulsos que ainda estão nele.

**Mensagens do host revistas.**
- `PlanoAndar.AndarEspecial`: só 0, `DesertShortcut` (1) e `LaboratoryShortcut` (2) (`Validacao.AndarEspecialValido`).
  O plano de atalho não passa pelas travas de progresso e de DLC (§12.8, §12.10); `Sandbox` (3) e valores fora do enum
  eram uma porta para pular as duas (auditoria da 0.21.0).
- `SpawnAndar`: índice negativo é descartado ao chegar; o teto (`PlayerSpawnInfos` do andar) é conferido em
  `RunCompartilhada.AjustarSpawn` (`Validacao.SpawnValido`), que agora devolve se ajustou; fora dele, fica o spawn
  sorteado aqui e o reposicionamento não roda.
- `InimigosEstado`: posição finita e dentro do teto (`PosicaoValida`) também para a réplica viva, não só a pendente.
- `AndarLimpo`: o jogo dispara `_onAllEnemiesCleared` toda vez que os inimigos não opcionais chegam a 0
  (`EnemyManager.RemoveEnemy`; escutam o `ClearFloorTrigger` e o portal de chefe). No convidado, repetido no mesmo
  andar só vale se o host criou inimigos depois do último (`Validacao.AndarLimpoNovo`; zera no hub e ao sair).
- Teto por andar no convidado, contado na criação (`InimigosCompartilhados.CriarPendentes`) e zerado ao trocar de andar.
  Na 0.21.1 era genérico (6000 inimigos, 32 baús); **desde a 0.22.0 é calculado no convidado com os dados do próprio
  jogo para aquele andar** (`Fase3.TetoDoAndar`), nos prefixos que o convidado já tinha em `LevelObjects.SetupEnemies` e
  `SetupLevelObjects` (os dois pulados por ele; recebem o `LevelInfo` do andar do host). Fontes, no IL da 2.1.1.1:
  - *Geração do andar:* a composição (`LevelDefinition.EnemySpawnCompositions`, uma sorteada) põe `AmountPerSpawn[i]` de
    cada `EnemyGroupSpawns` no ponto i (`EnemyManager.SpawnRegularEnemyGroup` cria um por unidade) e até
    `EliteEnemyRange.MaxValue` elites; sem grupos, os esquadrões (`composition.SquadsPool` ou `LevelInfo.SquadPool`,
    `TryUseSquadPool`) saem de um `AggregatedSubsetSelector`: cada seletor sorteia de `Min` a `Max` entradas
    (`SubsetSelectorExtension.Roll`) mais as garantidas, e cada esquadrão sorteia os inimigos do mesmo jeito; o pool é
    sorteado `ceil(RegularEnemyQuantity)` vezes (as garantidas contam em cada uma), e o teto usa `ceil(1,6 × 1,5)` = 3
    repetições; as elites da composição valem também para o pool do `LevelInfo`. A elite vem em pack: campeão (`ChampionEnemyDefinition.AmountRange`) ou
    raro com lacaios (`RareEnemyDefinition.MinionAmountRange`); conta o maior pack da coleção de inimigos do jogo.
    Opcionais, goblin, par do chefe e o único de Nightmare entram na folga fixa (`FolgaInimigosAndar` = 40).
  - *Sobrevivência:* cada onda até `floor(TotalToEndWave (ou o override da onda) × densidade)` (`SurvivalEventManager
    .StartWave`; a densidade é o `RegularEnemyQuantity`), com as elites dentro do total, mais o chefe. A sobrevivência pode
    seguir por vários andares (`MultiFloorCount`, `OnChangeLevel`) com a config do andar em que começou: nos andares
    seguintes da run vale o teto das ondas da última sobrevivência calculada.
  - *Baús:* `min(_chestsRangeAmount.MaxValue, pontos de baú)` (`LevelDefinition.GenerateChestsSpawnInfo`) mais os
    `ChestInteractable` que vêm na cena do andar (o host varre todos), com `FolgaBaus` = 4 (santuário amaldiçoado e
    baús de evento).
  O teto (`Validacao.TetoInimigos`) é o total do jogo × o maior `QuantidadeGrupo` (1,6: o grupo pode crescer no andar)
  × `FolgaQuantidade` 1,5 (nós da árvore do host que aumentam a quantidade) + a folga fixa, nunca acima do genérico.
  Três contagens, porque as fontes são diferentes (`Validacao.ContagemDoSpawn`): **andar** (o teto acima);
  **sobrevivência** (marca do spawn): o teto das ondas (andar de sobrevivência ou a que segue de um andar anterior) e, no
  mínimo, `SobrevivenciaEventoMaximo` = 1500 para os eventos com config própria (santuário, Miracles) em qualquer andar; **invocações** (ajuste `Invocacao`
  sem `Xp` e sem tesouro próprio (o vazio que o próprio `SetupSummon` põe não conta): skills de inimigo, `SkillSpawnEnemy`/`SkillSpawnEnemyWaves`; o `SetupSummon` tira XP e loot):
  `InvocacoesPorAndarMaximo` = 3000. O `Descartavel` fica na contagem do andar: o `SetupDisposable` só tira o XP, o loot
  continua. As contagens são de criações: a réplica nunca devolve a vaga em que entrou (virar invocação depois não libera o
  andar); `Xp` ou `Tesouro` numa invocação a leva à contagem do andar e é recusado se lá não houver vaga
  (`InimigosCompartilhados.Recontar`). XP acima de 1 e tesouro próprio não vazio só valem em inimigo de sobrevivência ou
  chefe, no spawn e no ajuste (`Validacao.RecompensaPermitida`; as fontes no jogo são o `SurvivalEventManager` e as
  temporadas), para o host não pôr XP x10 e tesouro de chefe em inimigos comuns até o teto. O tesouro vazio
  (`EmptyTreasureClass`, que o `SetupSummon` e o goblin põem; tem `HashID` próprio) sempre passa. Limite conhecido: se o
  spawn de uma invocação chegar num quadro e o ajuste `Invocacao` em outro, ela fica na contagem do andar (costumam
  chegar juntos, no mesmo quadro do host). Chefes: `ChefesPorAndarMaximo` = 16 (`EEnemyType` Boss 3 e Unique 6), em
  todas as contagens. Dados que não puderem ser lidos deixam o genérico (6000 inimigos, também na sobrevivência, e 32 baús), com aviso `[Teto]` no log.
  **Calibração:** o convidado registra o teto de cada andar (`[Teto] Andar N (...): até X inimigos na geração do jogo`,
  `[Teto] Andar N: até X baús`) e o total criado (`[Inimigos] Andar N: X inimigos do host (A do andar, S de
  sobrevivência, I invocações; C chefes)` e `[Objetos] Andar N: X baús do host`). Os valores fixos de evento e de
  invocações saem desses logs.

**Mensagens dos pares revistas.**
- `BauAberto`, `Quebrou` e `ObjetoUsado`: o convidado só obedece ao host (o que outro convidado fez chega repassado por
  ele); o host aceita de quem está no mesmo andar (`Validacao.OrdemDeObjetoAceita`, `ObjetosCompartilhados.OrdemAceita`).
- `TimerBonus`: só com a run do host aberta e de quem divide a run (compatível e fora de `ForaDaEscala`,
  `Validacao.BonusDoParAceito`).
- `StatusInimigos`: exige também o mesmo andar, como os golpes.
- `FxNovo`, `FxMovimento`, `FxNos` e `DanosVistos`: só de quem está no mesmo andar (o `FxFim` passa sempre: só remove a
  cópia).
- Limite: `MesmoAndar` vem do `CenaHash` que o próprio par declara no snapshot. As checagens de mesmo andar evitam
  efeito errado por atraso ou troca de andar, mas não barram um cliente alterado (o impacto é pequeno: cada um saqueia o
  próprio baú, e a purificação segue só do host).
- Snapshot: `Validacao.SnapshotValido` recusa tempo, posição (teto de 100 000), rotações, tempos e floats dos animators
  e rotações dos ossos NaN ou infinitos, sem log (20 Hz).

**Protocolo 21** (mensagem nova). codectest: ida e volta do `ExpulsoAviso`, `AvisoExpulsaoValido`, `Expulso` do novo dono
recusado com a run aberta, `HostEfetivo`, atalhos, spawn, tetos de inimigos/chefes/baús, `AndarLimpo` (repetido só com inimigos novos), ordens de objeto,
bônus de tempo e snapshots com NaN/infinito.

### 12.12 Lista de salas, etapa 3/3 parte 3: bloquear jogador (versão 0.23.0, protocolo 21)

Decisões do autor (23/11/2026): bloquear pela aba Sessão (aliado) e pela aba Salas (host da sala); o bloqueio esconde as
salas, o host expulsa o bloqueado sempre que ele entra e o convidado só recebe aviso (não expulsa nem sai sozinho); lista em
arquivo próprio, com Desbloquear na aba Ajustes; o bloqueio da própria Steam também conta; dois apertos, sem janela.

- **Lista (`Net/Bloqueados.cs`):** `BepInEx/HellClockCoop/bloqueados.txt`, uma linha `SteamID64<TAB>último nome`, com
  cabeçalho de comentário; até 500 (`Bloqueados.Maximo`). Lida uma vez, na primeira consulta; gravada num `.tmp` e trocada.
  `Ler` ignora comentário, linha ruim, repetida e id que não é de conta individual (`IdValido`: universo 1, tipo 1, conta
  ≠ 0). Só local: nunca vai para métricas, rede nem log (o log cita só o nome). Auditoria da 0.23.0: leitura que falha
  (arquivo travado por outro programa) ou arquivo acima de 64 KB deixa a lista sem gravar nesta sessão, para não
  sobrescrever o que está no disco; troca interrompida (sobrou só o `.tmp`) é recuperada ao ler.
- **Bloqueio da Steam:** `SessaoCoop.EstaBloqueado` = lista ou `GetFriendRelationship` Ignored/IgnoredFriend. Não entra
  no arquivo nem na aba Ajustes (desbloqueia-se pela Steam).
- **Aba Salas:** o dono do lobby grava `hcc_dono` (SteamID64) ao criar e a cada 2 s com os outros dados (quem herda o lobby
  grava o dele). `Salas.LerSteamId` só aceita id de jogador; a sala de dono bloqueado não entra na lista (o log conta quantas
  foram escondidas). A chave é declarada pelo host (não confiável): um host bloqueado pode apagá-la e aparecer, mas ao
  entrar a conferência é pelo membro real do lobby (abaixo). Diferente da expulsão (§12.7), que esconde a sala só até
  fechar o jogo, o bloqueio dura entre aberturas. Privacidade: a sala pública passa a mostrar o SteamID64 do host a quem
  busca salas com o mod (quem entra já o via como dono do lobby; sala privada não aparece na busca); dito no LEIA-ME. Um
  lobby que forje `hcc_dono` com o id de outro faria quem bloqueia pela lista bloquear esse outro: o nome gravado é o da
  Steam para o id (quando ela o conhece), para a aba Ajustes mostrar quem foi bloqueado de fato.
- **Entrada:** em `AtualizarMembros`, membro novo bloqueado: o host chama `Expulsar` sem `Hello` (o `Expulso` e o
  `ExpulsoAviso` da §12.11 fazem o resto; ele fica em `_expulsos` até sair, e a cada reentrada é expulso de novo); o
  convidado recebe "<nome>, que você bloqueou, está neste coop." e segue o coop normalmente. Quem herda o lobby (troca de
  dono) não expulsa um bloqueado que já estava dentro, só quem entra depois (aceito: troca de host é só roadmap).
- **Bloquear agora:** `SessaoCoop.Bloquear` grava, tira da lista atual as salas do bloqueado e, sendo host com ele na sala,
  o expulsa. `Desbloquear` só tira do arquivo (a sala volta na próxima busca).
- **Menu (visual B):** ação secundária na mesma linha (X no controle, Delete, clique direito), armada pelo SteamID por 3 s,
  zerada ao trocar de aba e ao reabrir; o rodapé mostra a tecla com o foco numa linha de aliado ou de sala. Mapa: §12.2
  (COOP-BLOQUEAR-ALIADO, COOP-SALAS-BLOQUEAR-HOST, COOP-CFG-BLOQUEADOS).
- **Faixa de foco:** desde a 0.21.0 a janela é mais larga que a linha da tela de Opções de onde a peça é copiada, e a faixa
  escura de foco ficava com a largura antiga. `PainelNativo.Destaque` estica na horizontal tudo dentro do `highlight` e solta
  `preserveAspect`/`AspectRatioFitter`/`LayoutElement` (a confirmar no jogo).
- **Caixa do canto (pedido do autor):** `CoopRuntime.DesenharInterface` deixa de mostrar o estado da sessão, os aliados,
  o resumo do balanceamento e "Conectando..." (estão na aba Sessão e em Ajustes); ficam só o modo eco e os avisos curtos.

**Protocolo 21, sem mudança** (nenhuma mensagem nova; `hcc_dono` é dado do lobby). codectest: `IdValido`, `Ler` (linhas
ruins, repetidas, teto), ida e volta `Formatar`/`Ler` com nome limpo, `LerSteamId`.

### 12.13 Menu do coop na lateral direita (versão 0.24.0, protocolo 21)

Pedido do autor: menu "lateralizado, igual ao menu Opções, só que do lado oposto", com a mesma animação de abertura.
Visual B (escolhido entre 3): coluna larga na altura toda.

- **O que o jogo faz (IL, HellClock.dll 2.1.1.1):** `OptionsWindow.Show` chama `GenericHUD.MoveTo(MiddleLeftAnchor,
  Vector2.right, 760)` e `Hide` chama `GenericHUD.MoveBack(..., 760)`: `DOKill(true)` e `DOAnchorPos(atual ± 760, 0,35 s)`
  com `SetEase(Ease.OutBack, 0,8)` ao abrir, `SetEase(Ease.InOutBack, 0,8)` ao fechar e `SetUpdate(true)` (tempo sem
  escala). A janela não tem Animator nem fade de CanvasGroup: é só o deslize da âncora.
- **Coluna:** `PainelNativo` ancora o painel na borda direita (`anchorMin (1,0)`, `anchorMax (1,1)`, pivô `(1, 0,5)`),
  900 de largura e a altura toda do canvas (1080). Em 4:3 (canvas de 1440) ocupa 62%. O fundo copiado passa 40 da borda
  direita (`FolgaFundo`) para não abrir vão no recuo do OutBack (~2% da largura).
- **Deslize:** as mesmas curvas do DOTween (`EaseManager`), escritas no `PainelNativo` (`SaidaRecuo`, `EntradaSaidaRecuo`), e a
  mesma duração; a distância é a largura da coluna (o jogo anda 760, a largura da janela dele), para ela sair inteira.
  Parte da posição atual, como o `DOAnchorPos`: reabrir no meio da saída volta dali. `Mostrar` e `Esconder` só escolhem o
  destino; `PainelNativo.Animar`, chamado em todo quadro no `CoopRuntime.AtualizarMenus` antes dos retornos antecipados
  (Novidades aberta), anda o deslize e apaga o canvas no fim da saída. Durante a saída, `DentroDoPainel` é falso (a
  roda não rola o painel que está saindo); a entrada do jogo volta como antes (`BloqueioEntrada`, 2 quadros depois do fechar).
- **Escurecer (só no menu do jogo):** em vez da tela toda a 50%, uma `RawImage` com textura de 128 × 1 gerada em tempo de
  execução: transparente à esquerda, sobe em `SmoothStep` por 520 à esquerda da coluna até 50% de preto e fica cheia sob
  ela. Um `CanvasGroup` acompanha o deslize (alfa = fração da coluna na tela).

**Protocolo 21, sem mudança.** Sem patch Harmony novo.

### 12.14 Nome da sala e a sua sala na lista (versão 0.25.0, protocolo 21)

Pedido do autor: mostrar a própria sala na lista e deixar nomear a sala, com o padrão "Sala de [nome na Steam]".
Decisões do autor: a sua sala sempre no topo em coop (host ou convidado); renomear na aba Sessão, com edição na linha; o
nome vale só para a sala atual (não vai para o `.cfg`).

- **Dado do lobby `hcc_sala`** (`Protocolo.ChaveLobbySala`): o host grava ao criar (`Salas.NomePadrao`) e no
  `AtualizarDadosDaSala` (a cada 2 s, só o que mudou), então quem herda o lobby anuncia "Sala de <ele>". O nome dado fica em
  `SessaoCoop._nomeSala` (null = padrão), zerado ao sair e ao criar sala. Não é mensagem: o protocolo não muda.
- **Não confiável:** quem lista lê pelo `Salas.NomeDaSalaListada` (mesma limpeza do `LimparNome`, pelo `Salas.CaractereAceito`: sem controle, sem `\` (escape do TMP), sem U+2028/U+2029, sem
  formatação invisível, sem `<` `>`, até 32, sem partir emoji); vazio ou só lixo cai no padrão com o `hcc_host`. A linha
  de cima da sala passa a ser o nome; o host vai para a linha de baixo ("Host <nome>  ·  lugar"). O nome do host continua
  vindo do `hcc_host`; bloquear pela lista segue pelo `hcc_dono` (§12.12).
- **Renomear** (`MenuCoop.ComecarEdicaoDoNome`/`EditarNome`, `SessaoCoop.RenomearSala`): só o dono do lobby. O texto chega
  pelo `Keyboard.onTextInput` do Input System ou, desde a 0.26.0, pelo `Event.character` do OnGUI (no jogo o `Keyboard.current` é nulo; a primeira fonte que entregar uma letra fica com a edição), sem controle nem `<` `>` e com no máximo 32 caracteres; Enter grava (limpo
  pelo `Salas.NomeDigitado`; vazio volta ao padrão), Esc ou B cancelam, Backspace apaga sem partir emoji. Enquanto edita, o
  Esc não fecha o menu e as setas não navegam; a tecla do menu não fecha (pode ser uma letra no .cfg) e o View fecha (cancela). A entrada do jogo já fica travada pelo
  semáforo do `InputHandler` (o mesmo do console de cheats do jogo), então as letras não abrem janelas do jogo.
  No controle, `SteamUtils.ShowGamepadTextInput` (até 32) e o callback `GamepadTextInputDismissed_t`; quando a Steam não
  mostra o teclado (fora do Big Picture e do Steam Deck), aviso e edição pelo teclado.
- **A sua sala na lista** (`MenuCoop.MontarSalas`): vem do estado da sessão, não da busca (a Steam só devolve salas
  públicas com vaga, e o `SalaVisivel` tira a nossa). Duas linhas de informação, sem foco: nome e "você está aqui"; host,
  jogadores e, para o host, pública ou privada.

**Protocolo 21, sem mudança** (dado do lobby novo, como o `hcc_dono`). codectest: `NomePadrao`, `NomeDigitado`,
`NomeDaSalaListada` e as linhas da sala com o nome.

### 12.15 Painel de grupo (versão 0.27.0, protocolo 22)

Pedido do autor (item 6 do roadmap): painel à esquerda com os participantes, a vida, as skills da barra embaixo e os
buffs/debuffs em cima. Entre 3 opções, o autor escolheu a **A "cartão compacto"**, em X = 24 e Y = 300 px de 1080p,
linha de 300 de largura; status e skills só com o ícone. A opção A deixava o jogador local de fora; depois da escolha o
autor pediu a própria linha também, no topo (o grupo inteiro num lugar só, mesmo com a vida já na HUD): nome da Steam na
cor do seu marcador (`AliadosNaTela.CorSeta`), vida do `LifeController`, caído pelo `JogadorCaido.Caido` e o progresso de
quem te reanima (`Reanimacao.MeuProgresso`). Ela só aparece com algum aliado ou o eco no painel.

- **Balanceamento:** sem recarga das skills e sem duração dos status dos aliados. Saber quando o aliado está sem a skill
  de defesa daria coordenação de graça ao grupo, que deve ficar um pouco mais difícil que o solo.
- **Mensagem `PainelAliado`** (tipo 15, `Net/MensagensPainel.cs`): o `HashID` de cada slot de `SkillSlotController.SkillSlots`
  (estático no jogo, 0 = vazio, até 8) e os 4 status mais recentes de `StatusController.StatusEffects` que o jogo mostra na HUD
  (sem `HideFromHud`, com ícone, sem repetir a definição), com `StackOrInstanceAmount`. Lida a cada 0,5 s; vai a todos os pares
  compatíveis só quando muda e a cada 5 s (quem chega depois recebe sem gancho no Hello). A vida segue no `PlayerSnapshot`.
- **Ícones:** quem recebe resolve pelo `SKILL_DEFINITION_COLLECTION` (`SkillDefinition.Icon`) e pelo
  `STATUS_DEFINITION_COLLECTION` (`StatusDisplayInfo.Icon`, `IsBuff`), com cache (zera aos 512 hashes).
- **Visual (`Interface/PainelGrupo`):** filho de `GenericHUD.LifeBars` (some com a HUD), montado com as peças do
  `AliadosNaTela` (`PecasDoPainel`, `ImagensDaBarra`: fonte e sprites da barra do companheiro). Medidas em px de 1080p,
  convertidas para a camada do jogo a cada quadro pela distância de 100 px na tela (vale para qualquer modo de canvas).
  Moldura dos status nas cores `_buffIconColor`/`_debuffIconColor` do `StatusIconsView`. Host primeiro, com a coroa do
  Streamline (`icons/streamline/`, PNG embutido em base64 em `IconesStreamline.cs`, crédito no `LICENCAS.txt`).
- **Estados:** caído → linha a 55%, barra vazia, "caído" ou o progresso da reanimação (`Reanimacao.ProgressoDe`), sem
  skills nem status; sem avatar no seu andar → 40% com "hub", "outro andar" ou "sem sinal" (mesmo andar sem pacote há 5 s).
- **Modo eco (F8):** o fantasma entra como "Eco" com as suas próprias skills e status.
- `MostrarPainelDeGrupo` no `.cfg` (seção Interface) desliga. Erro: o painel desliga até reabrir o jogo, com aviso no log.

**Protocolo 22.** codectest: ida e volta do `PainelAliado`, `IgualA`, cortes em 8 skills e 4 status, cabeçalhos acima do
teto e mensagem cortada recusados.

### 12.16 Convicção (barreira) no painel e sobre a cabeça (versão 0.28.0, protocolo 23)

Pedido do autor: a barra de convicção do jogo também no painel de grupo, para você e para os aliados. Decisões com o autor:
o dado vai no **`PlayerSnapshot` (formato 5)**, não no `PainelAliado` (a barreira muda a cada golpe, como a vida, e o
`PainelAliado` é confiável e só vai quando muda); o visual é o **mesmo da barra do jogo**, sem 3 opções; e a barreira também
aparece na **barra sobre a cabeça** dos aliados.

- **No jogo (IL):** `LifeView.UpdateBarrier` lê `LifeController.BarrierController.CurrentRatio` (0 sem barreira); o
  `Update` põe no `_barrierFill.fillAmount` o `Map(0, 1, conv.x, conv.y, fração)` com `_barrierMapConversion`, e o
  `_barrierGlow` (CanvasGroup) acende enquanto a fração é maior que zero.
- **Rede:** `LocalPlayerSampler.Barreira` amostra a fração (0 se morto) e o snapshot leva 2 bytes a mais (ushort, presa em
  0..1). `RemotePlayerAvatar.Barreira` guarda a do último snapshot aceito.
- **Sobre a cabeça (`AliadosNaTela`):** o modelo copiado da barra do companheiro mantém o `_barrierFill` (antes desligado) e o
  `_barrierGlow`; o fillAmount usa o mesmo mapa do jogo e o brilho acende/apaga em 0,25 s. Sem a barra do jogo, a reserva
  por OnGUI desenha uma faixa clara por cima da vida.
- **Painel (`PainelGrupo`):** preenchimento "Barreira" por cima da vida, com o sprite e a cor do `_barrierFill` (reserva em
  `AliadosNaTela.BarreiraReserva`), escondido sem barreira e no caído. A sua linha lê o `BarrierController` local.
- **Balanceamento:** só mostra o que o aliado já tem, como a vida; não muda dano, vida nem convicção.

**Protocolo 23.** codectest: ida e volta da barreira, presa em 0..1 (acima de 1 e negativa), 0 por padrão e snapshot de
formato 4 recusado.

### 12.17 Teleporte até o aliado (versão 0.29.0, protocolo 24)

Decisões do autor (23/11/2026): vale só **na run, no mesmo andar, com os dois de pé e fora de combate** (5 s sem dano
recebido nem causado), **canaliza 2 s parado** (mover, levar ou causar dano cancela), **recarga de 60 s por jogador**,
aciona com a **tecla T + clique no símbolo** (controle: D-pad ←) e visual **A** (símbolo à direita do nome). Valores fixos
no código (`Validacao.Tp*`), não no .cfg; só a tecla vai para o .cfg (`[Teclas] TeleporteAliado`).

- **`Fase2/TpAliado.cs`:** estado local (recarga, combate, escolha, canalização). Combate: `LifeController.OnHurt` com
  `Value > 0` e o dano causado visto no `DanosVisiveis.NumeroCriado`. Com um aliado disponível, T canaliza direto; com dois
  ou mais, abre a escolha (5 s): 1/2/3 no teclado (Digit e Numpad; no Input legado viram Alpha/Keypad em `Teclas`), ↑/↓ e A
  no controle; T, Esc, B ou D-pad ← cancelam. A escolha só abre se as regras locais deixam (de pé, fora da recarga e de
  combate) e com o painel visível. Durante a escolha e com o mouse sobre um símbolo que responde (pronto, ou o TP em
  andamento, que o clique cancela; cinza não prende), `BloqueioEntrada.PedirLeve`
  prende a entrada do jogo (só semáforo e módulo da UI, sem a busca do ouvinte do Esc e sem log), para o número, o A e o
  clique não chegarem ao personagem. Cursor escondido (controle) não conta como mouse sobre o símbolo.
- **Autoridade do host:** o convidado canaliza e manda `PedidoTp`; o host aplica `Validacao.MotivoTp` (andar do pedido e de
  quem pede, de pé, alvo no andar e de pé, combate, recarga com 1 s de folga) e guarda o último TP autorizado por SteamID.
  O combate do convidado, no host, é o último `Golpes` recebido dele ou a vida do snapshot caindo (`Par.UltimoCombate`).
  Responde `RespostaTp` (uma a cada 0,5 s por par); com "ok", o convidado faz `MovementController.Warp` até o avatar do
  aliado. Sem resposta em 3 s, o pedido expira; resposta depois disso só acerta a recarga. O host decide o próprio TP pela mesma regra, sem rede.
- **Painel (`PainelGrupo`):** desde a 0.30.0 (opção A "raio + aura" do autor), disco escuro de 22 px na ponta direita da
  linha do nome, com o raio Streamline "Flash 1" (16 px) dentro e um anel radial por cima (`Image` Filled Radial360, origem
  no topo; disco e anel gerados em textura no código). Pronto: anel cheio e raio na cor do aliado; recarga: cinza, o anel
  varre `1 − restante/60` e os segundos ao lado; combate: cinza; canalizando: o anel enche com a fração e o raio pulsa;
  "..." aguardando o host e 1/2/3 na escolha. O clique e o hover usam o disco. Some com o aliado caído, fora do andar ou
  sem você poder teleportar. A ordem dos números é a ordem do painel (`TpAliado.OrdemDoPainel`).
- **Efeito no personagem (`Fase2/TpEfeito.cs`, 0.30.0):** só no PC de quem teleporta (os aliados só o veem chegar). Na
  canalização, o fresnel adicional dos materiais do personagem (`CharacterEffectsController.AllMaterials`, não público,
  e os ids estáticos `AdditionalFresnelColor`/`AdditionalFresnelIntensity`, por reflexão) fica na cor do aliado, com a
  intensidade pulsando entre 0,25 e 1 numa frequência que sobe de 1,5 a 6,5 Hz nos 2 s (8 ciclos inteiros: termina no máximo); cor e intensidade de cada material
  são guardadas e voltam no fim, no cancelamento e na recusa. Autorizado o TP: `DisappearFx` + `LerpDissolve(1, 0,2 s,
  InQuad)`; 0,2 s depois o `TpAliado` relê o destino e faz o `Warp`, e vem `ReappearFx` + `LerpDissolve(0, 0,35 s,
  Linear)` (sem destino ou sem poder teleportar, o dissolver volta onde está). Os efeitos são os do `TeleportConfig` da
  primeira `TeleportSkillDefinition` da coleção de skills com os dois (o nome vai ao log); sem ela, só o dissolver. Criados
  com `GameplayFx.CreateFx(jogador, posição, null, null)` como no `Teleport.ExecuteTeleport` do jogo, com
  `FxCompartilhados.IgnorarCriados` ligado e o objeto marcado por 3 s (`MarcarSoLocal`): nem o efeito nem o som vão aos
  aliados, inclusive o som ativado depois (`GameplaySfxConfig.ActivateSfx(vfx, …)` com o objeto marcado silencia a captura
  do `SonsAliados`). O tween de sumir é morto antes de voltar (o `LerpDissolve` do jogo não mata o anterior), e o Warp
  reconfere o aliado no andar. Erro desliga só o efeito (log).
- **Modo eco:** o fantasma vale como aliado só para quem joga sozinho (fora de sessão, ou host de sala sem ninguém, desde a
  0.29.1), com a regra local e sem rede; com alguém na sala o TP até ele passaria por cima do host, e o eco pode estar
  deslocado até 60 m.
- **Destino:** a posição do avatar do aliado, levada ao ponto andável mais próximo (`NavMesh.SamplePosition`, até 2 m);
  sem chão por perto (aliado fora do mapa), não teleporta. O host descarta do dicionário as recargas vencidas e só registra
  no log padrão os TPs autorizados (as recusas vão para o Debug).
- **Balanceamento:** serve para reagrupar, não para fugir: fora de combate, canalização interrompível e recarga longa; não
  muda a escala, o tempo nem o dano.

**Protocolo 24.** codectest: ida e volta de `PedidoTp` e `RespostaTp`, cada motivo de `Validacao.MotivoTp` (inclusive NaN,
∞ e negativos) e as recargas restante e recebida presas em 0..60 s.

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

- **Morte e volta:** desde a 0.15.0 quem morre fica caído e segue na mesma run (§12.4): o save não registra fim de run
  nem run nova até a run do grupo acabar. Na reserva (`ConvidadoMorreu`), a run do convidado acaba como no solo e, ao
  voltar pela entrada no meio da run, o jogo dele conta uma run nova.
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
| `PlanoAndar` | convidado, só do host | `IndiceAndar < DungeonConfig.GetByID(DungeonHash).MaxLevelIndex` (`MaxLevelIndex` é a quantidade de andares: o jogo usa `IsLastLevel = índice + 1 >= MaxLevelIndex`); dungeon desconhecida é descartada; andar especial (sem dungeon): até 200; desde a 0.21.1, `AndarEspecial` só 0, 1 ou 2 (`Validacao.AndarEspecialValido`, §12.11). Aviso no log |
| `SpawnAndar` | convidado, só do host | índice negativo descartado ao chegar; ao usar, de 0 ao total de `PlayerSpawnInfos` do andar menos 1 (`Validacao.SpawnValido`, 0.21.1). Aviso no log |
| `FimRun` | convidado, só do host | `Motivo` dentro de `EGameOverSource` (0..4). Aviso no log |
| `PedidoAvanco` (0.15.0) | host, só caído, com a run em andamento e sem troca de andar, de par compatível e no mesmo andar (até a 0.20.0, também com a mesma DLC) | `IndiceAndar` igual ao andar atual do host (pedido repetido ou atrasado é recusado), `Andares` ≥ 1 (o host limita a 3) e nunca no último andar, onde o avanço viraria `FinishDungeon(Completed)` no save do host (`Validacao.PedidoAvancoValido`); um avanço aceito por andar (o Endless Nightmare gera o bloco seguinte de forma assíncrona); o host chama `AdvanceLevel(n, false)` e o jogo ainda limita pelo máximo dele. O convidado manda no máximo um a cada 2 s. Log de recusa no máximo a cada 5 s |
| `Reanimando` (0.16.0) | todos, de par compatível, no mesmo andar e de pé (até a 0.20.0, também com a mesma DLC), com a run em andamento | andar igual ao seu, estado 0 a 2 e fração finita de 0 a 1 (`Validacao.ReanimandoValido`); o alvo é você caído ou um aliado caído na run (outro alvo é descartado, para não ocupar memória). Só aparece na tela; não muda nada do jogo |
| `Reanimou` (0.16.0) | todos, com os mesmos filtros do `Reanimando` | andar igual ao seu e alvo diferente de quem mandou (ninguém se levanta sozinho). A conta do andar fica com o maior `Ordem` visto, subindo no máximo um (`Validacao.OrdemReanimacao`). Com o alvo na run (caído ou já de pé), todos esquecem a queda já vista dele (a próxima avisa de novo e o host volta a cobrar). Para o caído: ele precisa estar caído e quem mandou a até 2 m + 2 m de folga (atraso do snapshot), pela posição do avatar (`Validacao.ReanimouValido`); sem avatar, recusa. Para os outros: o alvo precisa jogar a run. Efeito: o caído levanta com `VidaAoReanimar` e todos contam a reanimação do andar |
| `Marcador` (0.17.0) | todos, de par compatível, no mesmo andar (pela cena do snapshot), com o seu personagem no mundo | cena conhecida e igual à sua, mesmo índice de andar (fora da run, 4294967295 dos dois lados), coordenadas finitas até 100 000 e o ponto a até 30 m + 10 m de folga do avatar de quem marcou no chão e até 10 m de desnível (`Validacao.MarcadorValido`; sem avatar, recusa). Do mesmo par, um marcador a cada 0,5 s no máximo, e só 1 guardado por par. Só aparece na tela; não muda nada do jogo |
| `Expulso` (0.18.0) | convidado, só do dono do lobby (nunca de si mesmo); desde a 0.21.1, com a run aberta, só do host dela e enquanto ele é o dono (`SessaoCoop.IdDoHost`) | sem campos; sai do coop e esconde a sala da lista (`Salas.ExpulsaoValida`) |
| `ExpulsoAviso` (0.21.1) | convidado, só do host (`IdDoHost`) | o SteamID é de um terceiro (nem de quem manda nem de quem recebe; `Validacao.AvisoExpulsaoValido`); o expulso sai dos pares e fica sem conversa (§12.11) |
| `PainelAliado` (0.27.0) | todos, de par compatível | até 8 skills e 4 status (acima disso a mensagem cai); hash desconhecido só não aparece, e o cache de ícones zera aos 512 hashes (§12.15). Só aparece na tela; não muda nada do jogo |
| `PedidoTp` (0.29.0) | host, de par compatível; no máximo uma resposta a cada 0,5 s por par | `Validacao.MotivoTp`: andar do pedido igual ao do host e quem pede no andar e de pé; alvo (outro par ou o host, nunca quem pede nem o eco, chave 0) no andar e de pé; 5 s sem combate visto pelo host; recarga de 60 s (folga de 1 s). Só o autorizado grava a recarga (§12.17) |
| `RespostaTp` (0.29.0) | convidado, só do host (`SessaoCoop.Host`) | só vale para o pedido em espera (até 3 s), com o mesmo alvo (senão só acerta a recarga); recarga presa em 0..60 s (`Validacao.RecargaTpRecebida`). Efeito: o próprio convidado teleporta até o avatar do aliado |
| `TimerBonus` | host; desde a 0.21.1, só com a run aberta e de par compatível dentro da escala (`Validacao.BonusDoParAceito`) | finito, `> 0` e `<= 120 s`; desde a 0.8.5, também no máximo 120 s e 120 bônus somados por convidado (chave: SteamID, para sair e voltar ao lobby não zerar) numa janela fixa de 60 s (`Validacao.AceitarBonusNaJanela`; o recusado não conta; na virada da janela até o dobro passa em sequência, a média fica no teto). Um aviso de recusa por janela |
| `TimerSync` | convidado, só do host | os dois campos finitos e `>= 0` (4 Hz: descarte sem log) |
| `PlayerSnapshot` | qualquer par compatível | desde a 0.21.1, tempo, posição (teto de 100 000), rotações, tempos e floats dos animators e ossos finitos (`Validacao.SnapshotValido`, §12.11; 20 Hz: descarte sem log) |
| `FxNovo`, `FxMovimento`, `FxNos`, `DanosVistos` | qualquer par compatível; desde a 0.21.1, só do mesmo andar (o `FxFim` passa sempre) | §12.11 |
| `InimigoSpawn`, `InimigosEstado`, `InimigoMorte`, `InimigoSumiu`, `AndarLimpo`, `InimigoAjuste` | convidado, só do host | valores do spawn e do ajuste (0.20.0, §12.9); desde a 0.21.1: teto de inimigos e 16 chefes (Boss e Unique) criados por andar (0.22.0: calculado com os dados do andar, §12.11), posição do `InimigosEstado` finita e dentro do teto também na réplica viva, `AndarLimpo` repetido no mesmo andar só com inimigos novos (§12.11) |
| `BauSpawn`, `ObjetoSpawn` | convidado, só do host | `ObjetoSpawn`: valores e teto por andar (0.20.0); `BauSpawn`: teto de baús por andar (0.21.1: 32; 0.22.0: dados do andar + 4, até 32) |
| `ObjetoUsado` | convidado, só do host; host, de quem está no mesmo andar (0.21.1; até a 0.21.0, qualquer par) | `Estado = Concluido` (purificação) só do host. Aviso no log |
| `BauAberto`, `Quebrou`, `ObjetoUsado` (`Usado`) | convidado, só do host; host, de quem está no mesmo andar (0.21.1, `Validacao.OrdemDeObjetoAceita`; até a 0.21.0, qualquer par) | — (o host repassa) |
| `StatusInimigos` (0.9.0) | host, de convidado compatível e, desde a 0.21.1, no mesmo andar (até a 0.20.0, também com a mesma DLC) | duração e intensidade finitas e `> 0`, pelo menos 1 pilha (`Validacao.StatusValido`); duração limitada a 30 s, intensidade a 1–10 e pilhas ao `MaxStacks` (sem ele, 10; status que não acumula pilhas, `CanStack` falso, 1); mais de 512 itens recusa a mensagem; status desconhecido ou que não é de controle recusado (desde a 0.9.1, modificador de atributo só se for lentidão; seção 11.4). No máximo 20 avisos por convidado na sessão |
| `SonsAliado`, `SonsFim` (0.11.0) | qualquer par compatível no mesmo andar, com avatar | GUID não nulo de evento 3D deste jogo (não snapshot), ponto finito a até 40 m do avatar, até 40 sons/s por aliado, até 64 itens e 4 parâmetros (nome de 1–64 letras, valor finito) por som; som que dura só com Id, até 64 por aliado, parado em 20 s (seção 11.6). Aviso a cada 5 s no máximo |
| `GolpesVistos` (0.11.0) | qualquer par compatível no mesmo andar | `EFxOnDamage` e tipos só com bits conhecidos, principal entre os presentes, vetores finitos, ponto a até 10 m do inimigo, inimigo a até 40 m do avatar, até 120/s por aliado. Aviso a cada 5 s no máximo |
| `Golpes` (0.8.5) | host, de qualquer convidado | dano finito e `> 0` (`Validacao.DanoValido`); NaN ou infinito mataria qualquer inimigo. Valor finito grande segue aceito: o dano do convidado é confiável (seção 2). Descarte sem log (20 Hz) |

**Backup.** `SaveBackup.LimparAntigos` mantém no mínimo 1 cópia, mesmo com `BackupsMantidos` 0 ou negativo no `.cfg`.

## 15. Como reproduzir a análise

1. Compilar o decompilador (ICSharpCode.Decompiler 9.1, a partir do código-fonte do ILSpy) ou usar o ILSpy no Windows.
2. Decompilar `Hell Clock_Data/Managed/HellClock.dll`, `K10.dll` e `Assembly-CSharp.dll` para uma pasta **fora** do repositório (ver `.gitignore`).
3. Os `grep` usados para os números da seção 1 estão em `scripts/contar-singletons.sh`.
