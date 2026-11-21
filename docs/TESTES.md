# Roteiro de teste no jogo — Hell Clock Coop

Lista para testar com dois PCs (autor = host, testador = convidado), contas Steam diferentes. Marque `[x]` o que
passou. Quando um item falhar, anote o que aconteceu e mande o trecho do log indicado.

Regra do projeto: uma versão só vira ✅ no roadmap depois de confirmada aqui, rodando no jogo.

## Antes de começar (os dois PCs)

- [ ] Os dois com a **mesma versão** do mod (zip da release mais recente). No log: `Hell Clock Coop <versão> carregado`, com a mesma versão nos dois.
- [ ] Backup do save ativo. No log: `Backup do save: N arquivo(s) copiados para ...`. Sem essa linha, **não teste**:
      confira `BackupDoSaveAoIniciar = true` em `BepInEx\config\br.srmackenzie.hellclockcoop.cfg`.
- [ ] Validação completa. No log: `[Validação] N/N alvos de patch encontrados` (os dois números iguais).
- [ ] Host aperta **F7** e convida; convidado entra. No painel do host: `• <testador>: no mesmo andar` depois que a run começa.

**Sobre o log:** fica em `<pasta do jogo>\BepInEx\LogOutput.log` e é **sobrescrito quando o jogo abre**. Se algo der
errado, copie o arquivo **antes** de abrir o jogo de novo. Os dois logs (host e convidado) ajudam; o do lado onde o
problema apareceu é o mais importante. Em qualquer item, mande também as linhas `[Unity Exception]` e `[Unity Error]`
que aparecerem perto do horário do problema.

---

## 0.4.1 — Baús e quebráveis

- [ ] **Baús no mesmo lugar**
  - Fazer: entrar num andar com baú e comparar a posição nos dois PCs.
  - Esperado: o convidado vê o baú do host no mesmo lugar; baús que só existiam no convidado somem.
  - Se falhar, mandar (convidado): linhas `[Objetos] Baú ... do host` e `[Objetos] Baú local ... escondido`.
- [ ] **Abrir um baú abre para todos**
  - Fazer: o convidado abre um baú; em outro andar, o host abre um.
  - Esperado: o baú abre nos dois PCs; cada um ganha o próprio loot (itens diferentes é normal).
  - Se falhar, mandar (os dois): `[Objetos] <nome> abriu o baú N` e qualquer `[Objetos] Abrir o baú N falhou`.
- [ ] **Vaso ou caixa quebra para todos**
  - Fazer: cada um quebra alguns vasos/caixas.
  - Esperado: quebram nos dois PCs, cada um com o próprio loot.
  - Se falhar, mandar: `[Objetos] Quebrar em ... falhou`.

## 0.5.x — Combate com o convidado

- [ ] **Inimigos atacam o convidado**
  - Fazer: o convidado vai na frente, longe do host, e espera os inimigos.
  - Esperado: os inimigos perseguem e atacam quem está mais perto; o convidado perde vida no próprio jogo, com as
    próprias defesas.
  - Se falhar, mandar (host): `[Alvos] <testador> agora pode ser atacado pelos inimigos` (ou `[Alvos] Ficha do alvo não
    criada`); (convidado): `[Inimigos] Skill ... da réplica N falhou`.
- [ ] **Morte do convidado**
  - Fazer: o convidado morre de propósito; o host passa de andar.
  - Esperado: a run do convidado acaba como no solo e ele volta para a run do host no próximo andar. A run do host
    continua. (Se o **host** morre, a run acaba para todos: isso ainda é o esperado nesta versão.)
  - Se falhar, mandar (convidado): `[Run] Você morreu: ...` e as linhas `[Run]` seguintes; (host): `[Run] Enviando
    plano aos convidados` do andar seguinte.
- [ ] **Estacas das adagas e facas que giram**
  - Fazer: usar a skill que crava estacas nos inimigos e a skill Reflexos (facas girando).
  - Esperado: no outro PC, as estacas ficam presas no mesmo inimigo e as facas giram em volta do aliado.
  - Se falhar, mandar (quem vê o problema): linhas `[Fx]` (`Cópia de ...`, `Prefab ... não carregou`, `Erro no laço`).

## 0.6.0 — Santuários, totens e chefes

- [ ] **Santuário comum**
  - Fazer: um dos dois ativa um santuário.
  - Esperado: ativa nos dois PCs; cada um recebe o efeito.
  - Se falhar, mandar: `[Objetos] <nome> ativou o santuário N` e `[Objetos] Santuário N: ...`.
- [ ] **Santuário amaldiçoado**
  - Fazer: ativar um santuário amaldiçoado e sobreviver às ondas.
  - Esperado: as ondas aparecem para os dois; ao purificar, cada um ganha o próprio ponto de constelação.
  - Se falhar, mandar: `[Objetos] Santuário amaldiçoado N purificado pelo host` (convidado) e as linhas `[Objetos]`
    do host.
- [ ] **Totem de bênção**
  - Fazer: achar um totem e escolher uma bênção em cada PC.
  - Esperado: mesmo lugar nos dois; escolha e compra individuais. Quem ainda não liberou os totens não vê o totem.
  - Se falhar, mandar: linhas `[Objetos] Totem ...` e `[Objetos] '...' local escondido`.
- [ ] **Chefe e barra do chefe**
  - Fazer: lutar contra um chefe; o convidado também bate nele.
  - Esperado: quando o chefe fica agressivo no host, o convidado vê a barra de vida e o dano dele conta.
  - Se falhar, mandar (convidado): `[Inimigos] Réplica N agressiva falhou` e as linhas `[Unity ...]` da luta.

## 0.7.0 — Balanceamento por número de jogadores

- [ ] **Linha do balanceamento no painel**
  - Fazer: com os dois na sessão, olhar o painel do coop (canto da tela).
  - Esperado: `Balanceamento 2J: vida ×... · chefes ×... · dano ×... · inimigos ×... · tempo ×...` e o aviso
    `Balanceamento para 2 jogadores: inimigos com N% mais vida a partir de agora`.
  - Se falhar, mandar: linha `[Balanceamento] 2 jogador(es): ...`.
- [ ] **Inimigos mais fortes**
  - Fazer: comparar com uma run solo do mesmo andar e dificuldade.
  - Esperado: inimigos com mais vida e um pouco mais de dano; mais inimigos a partir do andar seguinte. O grupo deve
    achar **um pouco mais difícil** que o solo. Anote se ficou fácil ou difícil demais.
  - Se falhar, mandar: linhas `[Balanceamento]` (inclusive `Quantidade de inimigos: ...`).
- [ ] **Resumo da run salvo**
  - Fazer: terminar uma run (voltar ao hub) nos dois PCs.
  - Esperado: arquivo `BepInEx\HellClockCoop\metricas\run-AAAAMMDD-hhmmss.json` e, no log,
    `[Métricas] Resumo da run salvo em metricas/run-...json (N andares, 2 jogador(es), ...)`.
  - Se falhar, mandar: `[Métricas] Não deu para salvar o resumo: ...`. O arquivo `run-*.json` pode ser enviado
    (só tem números e o id aleatório da instalação).

## 0.7.1 — Envio anônimo das métricas

- [ ] **Ligar o envio nos dois PCs**
  - Fazer: com o jogo fechado, em `BepInEx\config\br.srmackenzie.hellclockcoop.cfg`, seção `[Metricas]`, trocar
    para `EnviarResumoAnonimo = true`. Abrir o jogo e jogar uma run até voltar ao hub.
  - Esperado: no log, `[Métricas] Resumo ... enviado. Obrigado por ajudar no balanceamento!` (sem internet, o resumo
    espera em `metricas\pendentes` e sai depois).
  - Se falhar, mandar: as linhas `[Métricas]` (`O servidor recusou ... (código N)`, `Sem conexão ...`,
    `A Steam recusou o envio ...`). Se houver arquivo em `metricas\recusadas`, mande também (só números).

---

## 0.7.2 — Identificador novo do mod

- [ ] **Configuração antiga copiada para o arquivo novo**
  - Fazer: antes de instalar, anotar um ajuste feito no `.cfg` antigo (ex.: uma tecla trocada). Instalar a versão nova
    e abrir o jogo.
  - Esperado: aparece `BepInEx\config\br.srmackenzie.hellclockcoop.cfg` com o mesmo ajuste e, no log,
    `Configuração de <arquivo antigo> copiada para br.srmackenzie.hellclockcoop.cfg.` (só na primeira abertura).
  - Se falhar, mandar: a linha `Não deu para copiar a configuração antiga: ...` e os nomes dos arquivos `.cfg` da pasta
    `BepInEx\config`.

---

## 0.8.0 — Atualização automática

Já confirmado no preloader real (PC do autor, 21/11/2026, protótipo): WinHTTP pelo proxy do doorstop (404 sem release,
200 com redirecionamento, 0,4–0,6 s), RSA-SHA256 conferindo no Mono (e recusando o adulterado), Mono.Cecil lendo a versão
do plugin, e o arquivo do patcher carregado podendo ser renomeado e aberto para escrita.

Ponta a ponta com a release v0.8.0 publicada (PC do autor, 21/11/2026): plugin 0.7.1 + atualizador 0.8.0 → `Atualizado
da 0.7.1 para a 0.8.0 (2 arquivo(s))`, a 0.8.0 carregou na mesma abertura (com a migração do `.cfg` antigo), o painel
mostrou o aviso e a abertura seguinte deu `Em dia: versão 0.8.0`. Falta no PC do testador.

- [x] **Versão nova instalada sozinha** (instalar o zip de uma versão anterior com o atualizador, ou trocar só o plugin
  por um mais antigo)
  - Esperado: no log, `[Atualizador] Atualizado da <antiga> para a <nova> (N arquivo(s))`; no painel do coop,
    `» Coop atualizado da versão <antiga> para a <nova>.`; o console mostra `Hell Clock Coop <nova> carregado`.
  - Se falhar, mandar: todas as linhas `Atualizador` do `LogOutput.log`.
- [x] **Já em dia** (abrir de novo)
  - Esperado: `[Atualizador] Em dia: versão <x> (última publicada: <x>).` e nada no painel.
- [ ] **Sem internet** (desligar a rede antes de abrir)
  - Esperado: o jogo abre normal em até ~6 s a mais, com `[Atualizador] Não deu para verificar: ...` ou
    `Sem resposta em 5 s`; nada no painel.
- [ ] **Desligado** (`[Atualizacao] VerificarAoIniciar = false`)
  - Esperado: `[Atualizador] Verificação desligada` e nenhum acesso à rede.
- [ ] **Troca do próprio atualizador** (de uma versão com atualizador para a seguinte, ex.: 0.8.0 → 0.8.1)
  - Esperado: `BepInEx\patchers\HellClockCoop.Atualizador.dll` novo e o antigo em
    `BepInEx\HellClockCoop\atualizador\anterior\patchers\`; na abertura seguinte, `Em dia: versão <nova>`.
  - Se falhar, mandar: as linhas `Atualizador` (procure `falhou e foi desfeita`).

## 0.8.1 — Aceite das métricas no primeiro início

Para repetir o primeiro início: no `.cfg`, `[Metricas] AceitePerguntado = false` e `EnviarResumoAnonimo = false`.

- [x] **A janela aparece no menu**
  - Esperado: ao chegar no menu, tela escurecida e a janela "Hell Clock Coop: ajudar no balanceamento?" com
    "Enviar resumos" e "Não enviar"; no log, `[Métricas] Janela do aceite aberta no menu.` Clicar fora da janela não
    aciona o menu do jogo.
- [ ] **Não enviar** (clique ou tecla N)
  - Esperado: `[Métricas] Envio dos resumos recusado ...`; no `.cfg`, `EnviarResumoAnonimo = false` e
    `AceitePerguntado = true`; o menu volta a responder; na abertura seguinte a janela não aparece.
- [ ] **Enviar resumos (clique ou Enter depois de 3 s), com resumos já salvos** (clique e Enter confirmados no PC do
  autor, 21/11/2026, sem resumos salvos; falta o caso com resumos)
  - Esperado: a janela diz "Vão junto os N resumos..."; no log, `Envio dos resumos aceito` e `Resumos já salvos postos
    na fila: N`, depois `Resumo ... enviado` para cada um; no `.cfg`, `EnviarResumoAnonimo = true`.
- [ ] **Enter apertado sem parar desde a abertura não aceita**
  - Esperado: a janela continua aberta enquanto o Enter for apertado em menos de 3 s um do outro.
- [ ] **Esc (ou B no controle)**
  - Esperado: `Aceite adiado`; a janela some até a próxima abertura; o `.cfg` não muda; o menu volta a responder ao
    teclado e ao controle, sem `NullReferenceException` no log.
- [ ] **Clique logo ao abrir não responde**
  - Esperado: clicar nos botões no primeiro 1,5 s não faz nada (botões apagados).
- [ ] **Run solo enviada** (com o aceite ligado, sem coop)
  - Esperado: `Resumo da run salvo ... 1 jogador(es)` e depois `Resumo ... enviado`; no console do Firebase, `papel` = `solo`.
- Se falhar, mandar: as linhas `[Métricas]` do `LogOutput.log` e a seção `[Metricas]` do `.cfg`.

## 0.8.2 — Modo silencioso (console do BepInEx)

- [x] **Quem vem de versão anterior (console ligado) passa a silencioso** (PC do autor, 21/11/2026: `BepInEx.cfg`
  regravado com as mesmas 151 linhas; na abertura seguinte, nenhuma janela "BepInEx ..." e o log completo)
  - Esperado: na 1ª abertura da 0.8.2 o console ainda abre (já abriu antes do plugin); no log, `Console do BepInEx
    desligado a partir da próxima abertura do jogo.`; no `BepInEx.cfg`, `[Logging.Console] Enabled = false` com os
    comentários preservados. Na abertura seguinte, nenhuma janela preta, e o `LogOutput.log` continua completo.
- [ ] **Ligar de volta**
  - `[Diagnostico] MostrarConsole = true` no `.cfg` do mod. Esperado: na abertura seguinte o console não abre ainda e
    o log diz `Console do BepInEx ligado ...`; na outra, o console abre.
- [ ] **Instalação nova pelo zip:** nenhuma janela preta já na 1ª abertura (o zip traz `Enabled = false`).
- Se falhar, mandar: o `LogOutput.log` e a seção `[Logging.Console]` do `BepInEx.cfg`.

## 0.8.3 — Erro conhecido da tela de saves

- [x] **Tela de saves** (PC do autor, 21/11/2026: 12 ocorrências no `Player.log`, 1 linha de aviso e 0 `Unity Exception`
  no `LogOutput.log`)
  - Abrir o jogo até a tela de saves e passar pelos slots. Esperado: no `LogOutput.log`, uma única linha
    `[Warning:Hell Clock Coop] [Jogo] erro conhecido do Hell Clock 2.1.1.1 na tela de saves (acontece sem o mod; pode ignorar)`
    e nenhuma `[Unity Exception] ArgumentException: An item with the same key ...` com pilha.
- [ ] **Outros erros da Unity** seguem com a pilha e o agrupamento (`(repetido Nx)`).
- Se falhar, mandar: o `LogOutput.log` e o `Player.log`.

## 0.8.4 — Save protegido de mensagens inválidas

- [ ] **Backup ao iniciar:** abrir o jogo e conferir uma pasta nova `yyyy-MM-dd_HH-mm-ss` em
  `BepInEx\HellClockCoop\save-backups`, com os mesmos arquivos da pasta de save do jogo
  (`Application.persistentDataPath`). Com `BackupsMantidos = 0` no `.cfg`, sobra 1 pasta (a nova).
- [ ] **Run no modo eco (F8):** anotar a data de modificação dos arquivos de save, jogar 2 ou 3 andares e encerrar a run.
  Esperado: os arquivos mudam ao trocar de andar e no fim da run (como no solo); nenhum `[Run] Plano do host ignorado`,
  `Bônus de tempo inválido` ou `Fim de run com motivo desconhecido` no `LogOutput.log`.
- [ ] **Coop real (autor + testador):** subir andares até o último da dungeon e concluir. Esperado: o convidado acompanha
  todos os andares (nenhum plano ignorado), o tempo ganho pelo convidado soma no Hell Clock do host, inimigos, baús e
  santuários aparecem para o convidado e a purificação de santuário amaldiçoado dá o ponto de constelação aos dois.
- Se falhar, mandar: o `LogOutput.log` dos dois PCs.

## 0.8.5 — Teto do tempo ganho e golpes inválidos

- [ ] **Coop real, tempo ganho:** o convidado pega relíquias e drops de tempo normalmente durante 2 ou 3 andares.
  Esperado: o host registra `[Run] <nome> ganhou Ns de Hell Clock` a cada bônus e o tempo soma no Hell Clock; nenhum
  `acima do teto` no `LogOutput.log` do host em jogo normal.
- [ ] **Coop real, dano:** o convidado mata inimigos e chefes como antes (o dano dele continua valendo no host).
- Se falhar, mandar: o `LogOutput.log` dos dois PCs.

## 0.9.0 — Lentidão e congelamento do convidado valem para todos

Só em coop real (o modo eco não tem convidado). Os dois PCs na 0.9.0 (protocolo 10).

- [ ] **Lentidão e congelamento:** o convidado usa uma skill que deixa lento ou congela num inimigo que está indo
  atrás do host. Esperado: no PC do host o inimigo também fica lento ou parado pelo mesmo tempo que o convidado vê.
- [ ] **Atordoamento ou enraizamento** (se alguém tiver skill assim): o inimigo para de atacar ou de andar também no host.
- [ ] **Enfraquecimento ou reação elemental** (se alguém tiver skill que reduz defesa ou marca o inimigo): o host causa
  mais dano no inimigo marcado pelo convidado, como causaria se ele mesmo tivesse marcado.
- [ ] **Chefe:** o convidado tenta congelar ou deixar lento um chefe. Esperado: o mesmo que acontece no solo (chefe imune
  segue imune; resistência encurta o efeito).
- [ ] **Dano contínuo:** sangramento ou queimadura do convidado tiram vida no mesmo ritmo de antes (não dobrou).
- [ ] **Inimigos com afixos (campeões):** congelar ou deixar lento um campeão do convidado não faz nada estranho no host
  (o inimigo não ganha buff, não cura e não solta efeito sozinho).
- [ ] **Log do host:** nenhuma linha `[Inimigos] De <nome>: status ...` (recusado ou falhou) em jogo normal.
- Se falhar, mandar: o `LogOutput.log` dos dois PCs e o nome da skill usada.

---

## Mensagem para o testador

> E aí! Saiu versão nova do coop. Da 0.8.0 em diante ele se atualiza sozinho ao abrir o jogo (o painel do coop avisa).
> Se você ainda está numa versão antes da 0.8.0, é a última vez à mão: baixa o zip da release mais recente
> (`HellClockCoop-v<versão>.zip`), abre a pasta do jogo (Steam → Hell Clock → Gerenciar → Procurar arquivos locais) e
> extrai o zip lá dentro, substituindo tudo. Abre o jogo uma vez pra ele criar a config e fecha.
>
> Na primeira abertura da 0.8.1 aparece uma janela no menu perguntando se pode enviar os resumos das runs (solo e
> coop). Se puder, clica em "Enviar resumos": vai só número da run (tempo, dano, mortes) e o id aleatório da
> instalação, nada de nome, Steam ou arquivo teu. Joga umas runs solo também, que elas são a base de comparação. Se der algo estranho, me manda o `BepInEx\LogOutput.log` antes de abrir o jogo de novo (ele apaga ao
> abrir). Valeu!
