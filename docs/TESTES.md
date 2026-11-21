# Roteiro de teste no jogo — Hell Clock Coop

Lista para testar com dois PCs (autor = host, testador = convidado), contas Steam diferentes. Marque `[x]` o que
passou. Quando um item falhar, anote o que aconteceu e mande o trecho do log indicado.

Regra do projeto: uma versão só vira ✅ no roadmap depois de confirmada aqui, rodando no jogo.

## Antes de começar (os dois PCs)

- [ ] Os dois com a **mesma versão** do mod (zip da release mais recente). No log: `Hell Clock Coop <versão> carregado`, com a mesma versão nos dois.
- [ ] Backup do save ativo. No log: `Backup do save: N arquivo(s) copiados para ...`. Sem essa linha, **não teste**:
      confira `BackupDoSaveAoIniciar = true` em `BepInEx\config\br.srmackenzie.hellclockcoop.cfg`.
- [ ] Validação completa. No log: `[Validação] N/N alvos de patch encontrados` (os dois números iguais).
- [ ] Host abre o menu do coop (**F7**; até a 0.9.2, a F7 criava direto), escolhe **Criar coop e convidar amigos** e convida; convidado entra. No painel do host: `• <testador>: no mesmo andar` depois que a run começa.

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
- [ ] ~~**Enfraquecimento ou reação elemental**~~: deixou de valer no host na 0.9.1 (ver abaixo).
- [ ] **Chefe:** o convidado tenta congelar ou deixar lento um chefe. Esperado: o mesmo que acontece no solo (chefe imune
  segue imune; resistência encurta o efeito).
- [ ] **Dano contínuo:** sangramento ou queimadura do convidado tiram vida no mesmo ritmo de antes (não dobrou).
- [ ] **Inimigos com afixos (campeões):** congelar ou deixar lento um campeão do convidado não faz nada estranho no host
  (o inimigo não ganha buff, não cura e não solta efeito sozinho).
- [ ] **Log do host:** nenhuma linha `[Inimigos] De <nome>: status ...` (recusado ou falhou) em jogo normal.
- Se falhar, mandar: o `LogOutput.log` dos dois PCs e o nome da skill usada.

## 0.9.1 — Só o controle do convidado vale para todos

Só em coop real. Os dois PCs na 0.9.1 (protocolo 10, o mesmo da 0.9.0). Vale todo o roteiro da 0.9.0, menos o item de
enfraquecimento, mais:

- [ ] **Lentidão:** a lentidão do convidado continua deixando o inimigo lento também no host.
- [ ] **Enfraquecimento ou reação elemental** (skill do convidado que reduz defesa, aplica óleo ou marca o inimigo): o
  host NÃO causa dano a mais nesse inimigo; na tela do convidado o bônus dele segue valendo, como no solo.
- [ ] **Log do host:** nenhuma linha `[Inimigos] De <nome>: status ... recusado` com os dois na 0.9.1 (o convidado
  já não manda o que o host recusaria). Um aviso isolado pode vir de skill que muda os valores do status (a cópia do
  convidado difere da definição do host): não é falha; mandar o log se repetir em toda run.
- Se falhar, mandar: o `LogOutput.log` dos dois PCs e o nome da skill usada.

## 0.9.2 — Janela de novidades dentro do jogo

Solo basta (é só interface). Atualizar a partir da 0.9.1 (pelo atualizador ou à mão).

> Testando já na 0.9.3: o botão "Novidades" do menu virou o item "Novidades" do menu do coop, e em jogo a janela passa
> a segurar a entrada do jogo (responde ao mouse; Esc e B fecham sem abrir a pausa). Os itens "Botão do menu" e
> "Durante a run" abaixo valem como estão só na 0.9.2; na 0.9.3, siga a seção 0.9.3.

- [ ] **Abertura automática:** ao abrir o jogo depois de atualizar, a janela Novidades aparece sozinha no menu, com a
  aba `v0.9.2 NOVA` selecionada. Esc, B ou clique em "Fechar" logo ao abrir (no primeiro 1,5 s) não fecham, para
  quem pula a abertura do jogo não perder a janela; a F10 fecha sempre. Na primeira instalação com a janela do aceite das métricas, a das Novidades só abre
  depois de responder (ou adiar) o aceite.
- [ ] **Uma vez só:** fechar (botão "Fechar", Esc ou F10) e reabrir o jogo: a janela não volta sozinha
  (`[Interface] NovidadesVistas = 0.9.2` no .cfg). Apagar o valor faz ela voltar no próximo início.
- [ ] **Botão do menu:** o botão "Novidades da 0.9.2 (F10)" no canto superior esquerdo do menu reabre a janela; com
  personagem carregado o botão some.
- [ ] **Navegação:** setas esquerda/direita (ou LB/RB) trocam de versão e giram nas pontas; no menu, clicar na aba
  também troca. A aba `v0.9.1` mostra "O que testar"; texto longo rola com a roda do mouse ou as setas cima/baixo.
- [ ] **Durante a run:** F10 abre e fecha a janela (o rodapé mostra só a F10); enquanto aberta, F6/F7 não disparam e
  clicar na tela não troca aba nem rola. Anotar o que Esc e B fazem em jogo: o mod não segura o Rewired, então o Esc
  pode abrir também o menu de pausa, o B, LB/RB, o direcional e as setas podem disparar a ação deles (risco conhecido, não é falha da janela).
- [ ] **Telas:** em 1080p e numa resolução menor (ex.: 1366x768 em janela) a janela cabe inteira e o texto não
  corta.
- Se falhar, mandar: o `LogOutput.log` (linhas `[Novidades]`) e uma captura da tela.

## 0.9.3 — Menu do coop dentro do jogo

Solo basta para quase tudo (modo eco); os itens de convite e saída precisam de dois PCs. Atualizar a partir da 0.9.2.

- [ ] **Abrir e fechar no menu do jogo:** o botão "Menu do coop (F7)" abaixo do painel do canto e a F7 abrem a gaveta
  à direita (o resto da tela escurece um pouco); Esc, F7 e o X fecham. Com a gaveta aberta, clicar fora dela não aciona
  nada do menu do jogo.
- [ ] **Em jogo, entrada segura:** durante uma run, F7 abre a gaveta sem escurecer a tela. Com ela aberta: clicar nos
  itens (e fora deles) não move o personagem; WASD e o analógico não andam; skills e poção não disparam; Esc e B fecham
  a gaveta **sem** abrir a pausa. Ao fechar, o personagem volta a responder normalmente. No log:
  `[Entrada] Entrada do jogo bloqueada` e, ao fechar, `[Entrada] Entrada do jogo liberada`.
- [ ] **Controle:** View/Select abre e fecha a gaveta; anotar se o View faz mais alguma coisa no jogo no mesmo aperto
  (minimapa, HUD). Direcional cima/baixo move o foco, A confirma, B fecha; o rodapé passa a mostrar as dicas do controle.
- [ ] **Clique segurado:** segurar o botão do mouse andando, abrir a gaveta pela F7, soltar o mouse e fechar a gaveta: o
  personagem não deve continuar andando sozinho (se continuar, anotar: é o "soltar" engolido pelo bloqueio).
- [ ] **Ações:** "Modo eco" liga e desliga o fantasma (no menu do jogo aparece "entre num save", indisponível). Com dois
  PCs: "Criar coop e convidar amigos" cria o coop e abre o overlay; "Convidar amigos (Steam)" reabre o convite; "Sair do
  coop" muda para "Confirmar: sair do coop?" e só sai no segundo aperto em até 3 s. A F6 não faz mais nada.
- [ ] **Ajustes:** "Nomes dos aliados" e "Números de dano dos aliados" mudam na hora (LIGADO/DESLIGADO) e continuam
  iguais depois de reabrir o jogo (`MostrarNomesDosAliados` e `MostrarDanoDosAliados` no .cfg).
- [ ] **Novidades:** o item "Novidades da 0.9.3" fecha a gaveta e abre a janela; em jogo a janela responde ao mouse e
  Esc ou B fecham sem abrir a pausa. A F10 com a gaveta aberta troca a gaveta pela janela.
- [ ] **Telas:** em 1080p e em 1366x768 (janela) a gaveta cabe inteira, nenhum texto de item corta e os itens têm
  altura confortável para o clique.
- Se falhar, mandar: o `LogOutput.log` (linhas `[Menu]`, `[Entrada]` e `[Novidades]`) e uma captura da tela.

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
