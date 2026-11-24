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
  - Esperado (desde a 0.15.0): o convidado fica caído e volta de pé no próximo andar do host, com metade da vida. A run
    do host continua. Ver a seção 0.15.0.
  - Se falhar, mandar (convidado): as linhas `[Caído]` e `[Run]`; (host): `[Run] Enviando plano aos convidados` do
    andar seguinte.
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
  `Bônus de tempo de ... recusado` ou `Fim de run com motivo desconhecido` no `LogOutput.log`.
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

- [x] **Abrir e fechar no menu do jogo:** o botão "Menu do coop (F7)" abaixo do painel do canto e a F7 abrem a gaveta
  à direita (o resto da tela escurece um pouco); Esc, F7 e o X fecham. Com a gaveta aberta, clicar fora dela não aciona
  nada do menu do jogo.
- [x] **Em jogo, entrada segura:** durante uma run, F7 abre a gaveta sem escurecer a tela. Com ela aberta: clicar nos
  itens (e fora deles) não move o personagem; WASD e o analógico não andam; skills e poção não disparam; Esc e B fecham
  a gaveta **sem** abrir a pausa. Ao fechar, o personagem volta a responder normalmente. No log:
  `[Entrada] Entrada do jogo bloqueada` e, ao fechar, `[Entrada] Entrada do jogo liberada`.
- [ ] (sem controle para testar em 21/11/2026) **Controle:** View/Select abre e fecha a gaveta; anotar se o View faz mais alguma coisa no jogo no mesmo aperto
  (minimapa, HUD). Direcional cima/baixo move o foco, A confirma, B fecha; o rodapé passa a mostrar as dicas do controle.
- [x] **Clique segurado:** segurar o botão do mouse andando, abrir a gaveta pela F7, soltar o mouse e fechar a gaveta: o
  personagem não deve continuar andando sozinho (se continuar, anotar: é o "soltar" engolido pelo bloqueio).
- [x] **Ações:** (o eco sumia na troca de andar e duplicava as adagas: corrigido na 0.9.4) "Modo eco" liga e desliga o fantasma (no menu do jogo aparece "entre num save", indisponível). Com dois
  PCs: "Criar coop e convidar amigos" cria o coop e abre o overlay; "Convidar amigos (Steam)" reabre o convite; "Sair do
  coop" muda para "Confirmar: sair do coop?" e só sai no segundo aperto em até 3 s. A F6 não faz mais nada.
- [x] **Ajustes:** "Nomes dos aliados" e "Números de dano dos aliados" mudam na hora (LIGADO/DESLIGADO) e continuam
  iguais depois de reabrir o jogo (`MostrarNomesDosAliados` e `MostrarDanoDosAliados` no .cfg).
- [x] **Novidades:** o item "Novidades da 0.9.3" fecha a gaveta e abre a janela; em jogo a janela responde ao mouse e
  Esc ou B fecham sem abrir a pausa. A F10 com a gaveta aberta troca a gaveta pela janela.
- [x] **Telas:** em 1080p e em 1366x768 (janela) a gaveta cabe inteira, nenhum texto de item corta e os itens têm
  altura confortável para o clique.
- Se falhar, mandar: o `LogOutput.log` (linhas `[Menu]`, `[Entrada]` e `[Novidades]`) e uma captura da tela.

## 0.9.4 — Modo eco que fica ligado e adagas sem cópia parada

Solo basta para os três primeiros itens; o último precisa de dois PCs. Corrige o que o teste da 0.9.3 achou no eco.

- [ ] **Eco na troca de cena:** ligar o eco no hub, entrar numa run e passar de andar: o fantasma reaparece sozinho a
  cada andar. No log: `[Eco] Fantasma recriado depois da troca de cena`.
- [ ] **Eco no menu:** com o eco ligado, voltar ao menu e carregar o save: o fantasma volta. No menu do jogo, o menu do
  coop mostra Modo eco LIGADO; um aperto desliga (`[Eco] Desligado (...)` no log).
- [ ] **Adagas e orbes:** com as adagas do Reflexos (ou o orbe de poder) ativas, ligar o eco e trocar de andar: o
  fantasma mostra só as adagas girando, sem cópias paradas. No log, a linha `[Avatar] 'HellClockCoop_Avatar_Eco'
  criado: N efeito(s) de skill do jogador fora do clone` com N maior que 0 quando as adagas estavam ativas.
- [ ] **Coop:** com as adagas ativas quando o aliado chega ao andar, o boneco dele não mostra adagas paradas (as dele,
  girando, continuam aparecendo se ele usar a skill).
- Se falhar, mandar: o `LogOutput.log` (linhas `[Eco]`, `[Avatar]` e `[Fx]`) e uma captura da tela.

## 0.9.5 — Barreiras de sangue também no convidado

Só em coop real. Os dois PCs na 0.9.5 (protocolo 11). No início, `[Validação] N/N alvos de patch encontrados` com os três alvos novos da
`SmokeDoor` (Start, Close, Open). Corrige o achado da sessão de 21/11: o convidado passava livre
pelas barreiras de sangue da área do chefe.

- [ ] **Chefe:** o host entra na área do chefe com barreira. Esperado: quando a barreira fecha no host, ela aparece e
  bloqueia a passagem também no convidado. Log do host: `[Portas] Barreira em (...) fechou no host (andar N).`; do
  convidado: `[Portas] Barreira em (...) fechada pelo host.`
- [ ] **Convidado longe:** o convidado fica bem atrás (fora da arena) quando a barreira fecha. Esperado: ele é levado
  para perto do host e o painel do coop avisa. Log do convidado: `[Portas] Barreiras fecharam: você foi levado para perto
  do host.`
- [ ] **Fim da luta:** a barreira abre nos dois PCs quando a luta acaba. Log do convidado: `[Portas] Barreira em (...) aberta pelo host.`
  Depois de abrir, o convidado passa livre (nada invisível segurando) e não aparece `desligada neste jogo` no log.
- [ ] **Sobrevivência e santuário amaldiçoado:** as barreiras desses eventos também fecham e abrem nos dois PCs.
- [ ] **Chegou depois:** o convidado que entra no andar com a barreira já fechada (voltando de uma morte, ou entrando
  no meio da run) também a vê fechada, em até 2 s, e é levado para perto do host.
- [ ] **Host sai no meio da luta:** com a barreira fechada, o host sai do coop (ou o convidado sai). Esperado: a
  barreira reabre no convidado. Log do convidado: `[Portas] Barreiras reabertas (o host saiu do coop): N fechada(s) pelo
  host.` (ou `(fim do coop)` quando é o convidado que sai).
- [ ] **Sem teleporte repetido:** depois de levado para perto do host, o convidado não é levado de novo a cada 2 s.
- [ ] **Balanceamento:** com os dois presos na arena, a luta do chefe fica um pouco mais difícil que no solo (anotar).
- Se falhar, mandar: o `LogOutput.log` dos dois PCs (linhas `[Portas]`, sobretudo `não existe neste jogo` ou `falhou`).

## 0.9.6 — Vasos e caixas iguais nos dois PCs

Só em coop real. Os dois PCs na 0.9.6 (protocolo 12). No início, `[Validação] N/N alvos de patch encontrados` com o
alvo novo `DrawRandomSceneObject.OnEnable`. Corrige o achado da sessão de 21/11: vaso e caixa quebrados num PC não
quebravam no outro. Causa achada no IL: o jogo sorteia em cada PC qual objeto aparece em cada ponto do mapa
(`DrawRandomSceneObject`); agora o host manda a semente desse sorteio.

- [ ] **Mesma assinatura:** ao entrar em cada andar, os dois logs têm a linha `[Objetos] Andar N: X quebráveis
  (assinatura H); Y sorteios da cena com a semente S.` com os mesmos X, H e S nos dois PCs. Y > 0 confirma que o
  sorteio foi semeado.
- [ ] **Mesmos lugares:** os vasos e caixas estão nos mesmos pontos nos dois PCs (comparar dois ou três pontos perto do
  spawn).
- [ ] **Host quebra:** o host quebra alguns vasos e caixas. Esperado: quebram também no convidado. Sem `[Objetos] ...
  aqui não há quebrável` no log do convidado.
- [ ] **Convidado quebra:** o mesmo no sentido contrário, sem esse aviso no log do host.
- [ ] **Loot próprio:** cada um recebe o próprio loot do vaso ou caixa, inclusive do que o outro quebrou.
- [ ] **Controle (solo, sem coop):** numa run sozinho, os vasos e caixas continuam variando de uma run para outra, e a
  linha `[Objetos] Andar N` não aparece (ela só sai com o coop criado).
- Se falhar, mandar: o `LogOutput.log` dos dois PCs (linhas `[Objetos]` e `[Run] Plano do host`, sobretudo `aqui não
  há quebrável`, `sem plano do host` ou `K10Random mudou`).

## 0.9.7 — Medidor de travadas

Só mede: nada muda no jogo. Responde ao achado "travadinhas esporádicas" da sessão de 21/11, que veio sem número. Uma
travada é um quadro com 50 ms ou mais e pelo menos 2,5 vezes o quadro normal (a média dos quadros sem travada). Quadros
em carregamento (sem personagem ou na troca de cena) ficam fora da conta.

- [ ] **Resumo no solo (controle):** jogar uns 6 min sozinho, sem coop nem eco. Esperado: uma linha `[Desempenho] Resumo
  de 5,0 min em jogo (solo)` (ou `5.0`, conforme o Windows) com quadros, média, travadas e as partes do mod. Ela dá a base para comparar com o coop.
- [ ] **Resumo em coop:** na sessão em dupla, os dois logs têm linhas `[Desempenho] Resumo ... (host com 1 aliado(s))` e
  `(convidado com 1 aliado(s))`; ao criar ou entrar no coop sai também o resumo do trecho solo (`contexto mudou`), se ele tiver 30 s ou mais.
- [ ] **Travada anotada:** quando sentir uma travadinha, anotar a hora. Esperado: uma linha `[Desempenho] Travada de N ms`
  perto dela, dizendo quanto foi do mod (e de qual parte), se teve coleta de lixo, rede e log naquele quadro.
- [ ] **Sem peso:** a média de `mod` no resumo fica bem abaixo de 1 ms por quadro.
- Depois da sessão, mandar o `LogOutput.log` dos dois PCs (linhas `[Desempenho]`). Com esses números sai a correção:
  os suspeitos medidos à parte são criar avatar, carregar prefab de efeito (carga síncrona, a primeira
  vez de cada efeito do aliado) e copiar efeito.

## 0.9.8 — Passos dos inimigos no convidado

Responde ao achado "inimigos sem som no convidado" da sessão de 21/11. Causa achada no IL: a réplica pula o `Update` do
personagem, então não calcula a própria velocidade. Os passos só tocam com o inimigo andando a 40% da velocidade dele ou
mais, e o som de estado (rosnado, respiração) tem um modo parado e um andando: nos dois, a réplica ficava sempre parada.
Golpe, ataque e morte não dependem disso. Se ainda faltar som depois desta versão, a causa é outra: anotar qual.

- [ ] **Passos (convidado):** chegar perto de um grupo andando na sua direção. Esperado: barulho de passos, como no solo.
- [ ] **Som de estado (convidado):** o rosnado ou a respiração do inimigo muda quando ele sai de parado e começa a andar.
- [ ] **Outros sons (convidado):** golpe no inimigo, ataque dele e morte têm som. Se algum não tiver, anotar qual
  (passo, ataque, golpe, morte, rosnado) e o nome do inimigo.
- [ ] **Host (controle):** os inimigos do host soam como antes.
- Se falhar, mandar: o `LogOutput.log` do convidado (linhas `[Inimigos]`, sobretudo `indisponível (som de movimento da
  réplica)`) e a lista do que falta de som.

## 0.10.1 — Sobrevivência e temporadas iguais no convidado

Achado no IL: a réplica do inimigo no convidado não levava o multiplicador de XP das ondas de sobrevivência
(`SetExperienceMultiplier`) nem o tesouro próprio (`SetInstanceTreasureClassOverride`: o da onda e o do chefe da
sobrevivência, ou o vazio das ondas do santuário de Miracles, passado por `MiraclesSeasonInstance.PlaySanctumFlow` a
`StartSurvival`, e do único de Nightmare). Agora vão no `InimigoAjuste`
(protocolo 13). Equipamento sem redução em grupo, por decisão do autor.

- [ ] **Santuário amaldiçoado em dupla:** o XP de cada inimigo da onda é igual nos dois PCs (mesma barra de XP subindo
  na mesma proporção, com o mesmo nível).
- [ ] **Andar de sobrevivência em dupla:** o chefe final dá ao convidado o loot da mesma tabela que dá ao host.
- [ ] **Temporada Miracles (se o host jogar com ela):** os inimigos das ondas do santuário não deixam loot comum no convidado.
- [ ] **Invocações (controle):** os inimigos invocados continuam sem XP e sem loot nos dois PCs.
- [ ] **Versão:** com 0.10.0 de um lado e 0.10.1 do outro, o coop recusa a conexão (protocolo 12 × 13).
- Se falhar, mandar: o `LogOutput.log` do convidado (linhas `[Inimigos]`, sobretudo `Tesouro … não existe aqui`).

## 0.31.0 — Rede dos inimigos: o host manda só o que mudou (protocolo 25)

Muda a mensagem `InimigosEstado` (host → convidados): cada inimigo leva só os campos que mudaram, cada mudança vai em
dois envios seguidos e o estado inteiro vai uma vez por segundo; o convidado descarta pacote fora de ordem (ARQUITETURA,
§11 "Só o que mudou" e riscos, item 7, etapa 5). Não muda regra de jogo nem save. Medido sozinho em 24/11/2026 com a
contagem simulada (tabela na ARQUITETURA). As réplicas no convidado ainda não foram vistas no jogo com esta versão:

- [x] **Contagem simulada (sozinho, 24/11/2026):** crie o coop com "Sala pública" desmarcada, ligue o eco (F8) **antes**
  de entrar no portal, jogue 1 min ou mais e desligue o eco (fecha a janela do medidor). No log sai
  `[Desempenho] Rede no período (host com 0 aliado(s) e eco): ... estado dos inimigos (simulado: eco como convidado,
  montado e não enviado): N envio(s); por envio, X inimigo(s) perto de aliado, Y no pacote (...) e B bytes por convidado`.
  Medido: 100 bytes por envio parado (4,6 inimigos perto) e 149 com combate (14,6 perto); antes eram 51 bytes por inimigo.
- [ ] **Inimigos no convidado (dupla):** numa run, os inimigos andam, viram para o alvo, atacam e perdem vida no
  convidado como na 0.30.3. Nenhum fica deslizando parado, preso numa pose ou repetindo o golpe.
- [ ] **Depois do golpe:** inimigo que ataca volta para a animação de andar ou de parado no convidado, sem repetir o
  golpe (a animação que chega durante a skill da réplica fica guardada e entra quando a skill acaba, se chegou há menos
  de 0,3 s).
- [ ] **Vida:** a barra de vida dos inimigos no convidado acompanha os golpes do host e os do próprio convidado, sem
  "voltar" para cima depois de um golpe.
- [ ] **Chegada atrasada:** o convidado chega ao andar depois do host (ou entra no meio da run): os inimigos aparecem no
  lugar certo; a animação e, no chefe, a barra e o dano podem levar 1 ou 2 s para acertar (antes era na hora). Se
  passar disso, anotar.
- [ ] **Chefe:** a barra de vida, o dano e a câmera do chefe liberam no convidado quando ele fica agressivo no host.
- [ ] **Rede no log:** o host tem `estado dos inimigos: ... no pacote ... bytes por convidado` (sem "simulado") e
  `InimigosEstado N msg/X KB` na lista por tipo; o convidado tem `recebeu N msg`. Mandar o `LogOutput.log` dos dois PCs:
  são os números reais desta etapa e o "antes" da etapa 6 (recepção com teto por quadro).
- [ ] **Versão:** com 0.30.3 de um lado e 0.31.0 do outro, o coop recusa a conexão (protocolo 24 × 25).
- Se falhar, mandar: o `LogOutput.log` dos dois PCs e o que o convidado viu (qual inimigo, parado ou andando, em que
  momento da luta).

## 0.30.3 — Travadas pontuais (menu, primeira aparição do aliado, primeiro aviso, efeitos)

Não muda regra de jogo, rede nem save. O ouvinte do Esc da tela de saves passa a ser segurado por patch, as peças da
interface do jogo vêm por referência direta, o texto do `OnGUI` é preparado na abertura do jogo e o prefab de efeito do
aliado carrega sem segurar o quadro (ARQUITETURA, riscos, item 7, etapa 4; §11.2, §12.2, §12.3). Medido sozinho em
24/11/2026 (tela de saves, hub e andar 1 do Sertão, com o eco; tabela na ARQUITETURA, riscos, item 7). O que depende de um
segundo jogador ainda não foi testado no jogo:

- [x] **Esc na tela de saves:** com o menu do coop (ou a Novidades) aberto na tela de saves, apertar Esc. Esperado: só o
  painel fecha, sem "Você realmente quer fechar o jogo?"; com o painel fechado, o Esc volta a abrir a pergunta. Feito em
  24/11/2026, com a Novidades.
- [x] **Abrir o menu em jogo:** no hub e na run, abrir e fechar o menu (F7) 2 vezes. Esperado: nenhuma linha
  `[Desempenho] Travada` na abertura (eram 79 a 92 ms em "buscar ouvintes do Esc") e o detalhe não aparece mais no resumo.
  Feito em 24/11/2026: nenhuma linha em 5 aberturas.
- [x] **Primeira aparição do aliado:** no hub, ligar o eco (F8) 2 vezes. Esperado: `[Aliados] Barra de vida e seta
  montadas ... buscas na cena: 0`, o painel de grupo com as mesmas cores de status de antes
  (`cores de status: RGBA(1.000, 1.000, 1.000, 1.000)/RGBA(1.000, 1.000, 1.000, 1.000)`) e, no resumo, `buscar peças da
  HUD` ausente; a seta continua `UI_Icon_DropDownArrow`. Na primeira vez da sessão de jogo ainda sai uma `Travada` perto
  de 60 ms (eram 326 ms; é a montagem da barra, da seta e do painel mais a criação do avatar); da segunda em diante,
  nenhuma. Feito em 24/11/2026: 60 ms na primeira, nenhuma nas outras três (hub e run).
- [x] **Primeiro aviso do canto:** o primeiro aviso em jogo (ligar o eco, criar o coop). Esperado: sem `Travada` com
  `OnGUI` alto (eram 105 a 484 ms); na abertura do jogo, `[Interface] Texto do OnGUI preparado em N ms`. Feito em
  24/11/2026: OnGUI 5,5 ms no quadro do primeiro aviso; 106 ms na abertura.
- [x] **Item "Coop" da pausa:** Esc em jogo. Esperado: o item "Coop" continua abaixo de "Opções" e abre o menu; no
  resumo, `buscar botões do jogo` ausente em jogo. Feito em 24/11/2026.
- [x] **Efeitos com o eco:** na run, com o eco ligado, usar as skills. Esperado: as cópias aparecem no fantasma como
  antes (`[Fx] Cópia de ...`), com `copiar efeito` no resumo abaixo de 1 ms por cópia depois da primeira. Feito em
  24/11/2026: 53 cópias, 0,70 ms por cópia (pior quadro 4,2 ms).
- [ ] **Efeitos em dupla:** cada um usa todas as skills perto do outro. Esperado: os efeitos aparecem no aliado, sem
  `Travada` com `carregar prefab de efeito` no primeiro uso de cada skill; efeito que termina antes de o prefab chegar não
  fica parado na tela.
- [ ] **Tela do caído (medição):** cair numa run em dupla. Esperado: o resumo mostra `buscar peças da HUD` com 2× por queda
  (o cinza e a vinheta) e mais 1× ao levantar (o cinza é desfeito com outra busca), com o tempo delas (ainda sem número;
  se for alto, vira etapa própria).
- [ ] **CheckForSpawn (pendência da 0.30.1):** numa run como host, andar pelo andar até achar inimigos. Esperado: o
  resumo lista `CheckForSpawn N×` em `detalhes`. Em 24/11/2026, sozinho com o eco, andando e lutando no andar 1 do Sertão,
  não apareceu (o jogo não chamou o método nesse andar): repetir em outros andares, como host de uma sala com aliado.

## 0.30.2 — Dono do lobby guardado (menos chamadas à Steam)

Não muda regra de jogo, rede nem save. `SouHost`/`IdDoHost` leem o dono guardado, relido da Steam uma vez por quadro
(ARQUITETURA §12.11). Medido sozinho em 24/11/2026 (hub e run, solo, sala vazia e sala com eco; tabela na ARQUITETURA,
riscos, item 7). O que depende de um segundo jogador ainda não foi testado no jogo:

- [x] **Chamadas à Steam (sozinho):** criar o coop (sala privada) e ficar 1 min; ligar o eco (F8) por 1 min; repetir
  dentro da run. Esperado: no resumo `(host com 0 aliado(s); ...)`, `chamadas à Steam por quadro (jogo e mod): média`
  entre 1 e 2 (eram 14 a 21 na 0.30.1) e `mod: média` perto de 0,25 ms (era 0,77). Feito em 24/11/2026.
- [ ] **Expulsão:** na sessão em dupla, o host expulsa o convidado. Esperado: o convidado sai (`Saiu do coop (expulso
  pelo host)`), a sala some da lista dele e ele não consegue voltar.
- [ ] **Host sai no hub:** o host sai do coop no hub. Esperado: o convidado passa a ver `Você é o host` no menu e
  consegue começar uma run; o resumo seguinte dele sai como `host com 0 aliado(s)`.
- [ ] **Host sai no meio da run:** o host sai do coop com a run aberta. Esperado: o convidado termina a run sozinho, sem
  receber plano de andar de ninguém; na volta ao hub ele é o host.
- [ ] **Bloqueado ao entrar:** o host bloqueia um jogador, que tenta entrar pela lista. Esperado: `[Bloqueio] ... está
  bloqueado e foi expulso ao entrar`.
- [ ] **CheckForSpawn (pendência da 0.30.1):** numa run como host, andar pelo andar até achar inimigos. Esperado: o
  resumo lista `CheckForSpawn N×` em `detalhes` (parado no começo do andar 1 não apareceu).

## 0.30.1 — Medidor de desempenho ampliado

Só mede: nada muda no jogo, na rede ou no save. Completa a seção 0.9.7. O "normal" de lá é uma média móvel dos quadros
em jogo (anda devagar também nas travadas), e o carregamento que fica fora da conta agora inclui o andar ainda
carregando (cena -1), não só o quadro da troca de cena. Um resumo sai quando o contexto muda (solo, eco, coop) ou a
cada 5 min, se o trecho tiver 30 s ou mais; dá para fazer sozinho, com o eco (F8).

**A partir da 0.30.2** os números esperados em sala mudaram: as chamadas à Steam do host ficam entre 1 e 2 por quadro
(não mais 19) e a parte `Ganchos` deixa de aparecer em `partes` em sala vazia. Para esses dois itens vale o roteiro da
seção 0.30.2, acima.

- [ ] **Chamadas à Steam, solo x sala:** jogar 1 min sozinho, criar o coop (sala privada), jogar mais 1 min e sair do
  coop. Esperado: no resumo `(solo; contexto mudou ...)`, `chamadas à Steam por quadro (jogo e mod): média` perto de 0;
  no resumo `(host com 0 aliado(s); ...)`, bem mais (19 por quadro no hub, em 24/11/2026).
- [ ] **Carregamento não é travada:** numa run, trocar de andar algumas vezes. Esperado: nenhuma linha
  `[Desempenho] Travada` com `cena -1`; o resumo conta os quadros em `quadros longos em carregamento (fora da conta)`,
  com o maior tempo do mod e a parte.
- [ ] **Ganchos na conta:** numa run como host com um aliado (ou o eco em sala), o resumo lista em `detalhes` as linhas
  `CheckForSpawn N×`, `TimerManager.Update N×` e `contexto de som N×`, e a parte `Ganchos` em `partes`.
- [ ] **Partes novas:** com o eco ligado, `partes` pode trazer `OnGUI` e `Avatares` (só aparecem com pior quadro de
  0,5 ms ou mais). `buscar peças da HUD` sai em `detalhes` do resumo que fecha ao ligar o eco pela primeira vez (o do
  trecho solo; jogue 30 s ou mais antes de ligar).
- [ ] **Memória:** o resumo traz `memória gerenciada N MB agora, piso depois de coleta: mínimo ..., máximo ..., último ...`
  (ou `sem coleta no período`).
- [ ] **Rede (sessão em dupla):** depois do resumo vem `[Desempenho] Rede no período (...)`, com `enviou N msg` e a
  lista por tipo (`Snapshot`, `InimigosEstado`, ...), `recebeu N msg` e `buffer de recepção (64 msg) cheio em N quadro(s)`.
  Sozinho (sem aliado) essa linha não sai; desde a 0.31.0 sai também para o dono de sala vazia com o eco ligado, com a
  contagem simulada do estado dos inimigos (seção 0.31.0).
- Depois da sessão em dupla, mandar o `LogOutput.log` dos dois PCs: são os números de "antes" das próximas etapas.

## 0.22.0 — Balanceamento só do autor e teto de inimigos por andar

Exige a 0.22.0 em todos os PCs (o protocolo não muda). O teto só age no convidado.
- [ ] **.cfg antigo:** num PC que já teve a 0.21.x, abrir o jogo. Esperado: no log, `[Balanceamento] N opção(ões)
  antiga(s) de [Balanceamento] apagada(s) do .cfg`, e o `.cfg` sem a seção. A aba Ajustes mostra os valores só para ler.
- [ ] **Teto por andar (convidado):** jogar alguns andares comuns, um de sobrevivência e um com inimigo que invoca.
  Esperado: os inimigos do host aparecem todos no convidado; no log do convidado, uma linha `[Teto] Andar N (...): até X
  inimigos na geração do jogo` por andar e, ao trocar de andar, `[Inimigos] Andar N: X inimigos do host (A do andar, S
  de sobrevivência, I invocações; C chefes)`, com A abaixo do teto do andar.
- [ ] **Santuário de sobrevivência ou Miracles:** se aparecer, completar. Esperado: nenhum aviso `além do teto`.
- Se falhar, mandar (convidado): todas as linhas `[Teto]`, `[Inimigos] Andar` e `além do teto`. Esses números calibram
  os tetos fixos de evento (1500) e de invocações (3000).

## 0.18.0 — Lista de salas (1/3)

Precisa de dois PCs com contas Steam diferentes (a lista não mostra a própria sala; o eco não ajuda aqui). Exige a
0.18.0 em todos os PCs (protocolo 19). Sem efeito nos números do balanceamento.
- [ ] **Aba (solo):** F7 mostra 4 abas (Sessão, Salas, Ajustes, Novidades) sem cortar o texto; a aba Salas busca sozinha
  e mostra "Nenhuma sala pública aberta..." e "Atualizar lista  há N s". Anotar se a busca falha ou demora.
- [ ] **Sala pública:** host cria o coop com "Sala pública" ligado; no outro PC, aba Salas > Atualizar. Esperado: a
  linha "Host  -  no hub" com "Nv <nível>  1/4".
- [ ] **Run em andamento:** host entra numa run; depois de alguns segundos, Atualizar no outro PC mostra "Ato N, andar M".
  Entrar pela lista: o convidado vai para o andar do host.
- [ ] **Expulsar:** host, aba Sessão > "Expulsar <nome>" duas vezes. Esperado: o convidado sai com o aviso "expulso pelo
  host" e a sala some da lista dele; no host, o aliado some e a escala volta a solo.
- [ ] **Privada:** host desliga "Sala pública" com o coop aberto; no outro PC, Atualizar. Esperado: a sala some.
- [ ] **Nome da sala (0.25.0):** host cria o coop; no outro PC a sala aparece como "Sala de <host>", com "Host <host>  ·
  no hub" embaixo. Host, aba Sessão > "Nome da sala" > Enter, digita "Os Malditos", Enter: aviso "Sala renomeada";
  Atualizar no outro PC mostra "Os Malditos". Apagar tudo e Enter volta ao padrão; Esc cancela sem mudar; com `<b>` o
  `<` e o `>` não entram. No controle, A abre o teclado de tela da Steam (Big Picture) ou avisa e deixa digitar.
- [ ] **Sua sala (0.25.0):** em coop, como host e como convidado, a aba Salas começa por "Sua sala" com o nome e "você
  está aqui", também com a sala privada ou cheia; a sua sala não aparece de novo na lista de baixo.
- Se falhar, mandar: o `LogOutput.log` dos dois PCs (linhas `[Sessão]`, sobretudo `Salas: N da Steam, M na lista`).

## 0.17.0 — Marcador no chão (ping)

Solo com o modo eco (F8) cobre o mouse, o visual e a seta da borda; em dupla, conferir o nome na cor do aliado. Exige a
0.17.0 em todos os PCs (protocolo 18). Só informação: não muda o balanceamento.

**Teste do autor (22/11/2026, DLL 0.17.0, solo com o eco):** ping funcionando com o botão do meio do mouse; o seu
marcador e o do Eco na cor certa, e a seta na borda com o ponto fora da tela. Falta a dupla; o controle (R3) ficou
para depois.
- [x] **Mouse (eco):** com o eco ligado, aperte o botão do meio do mouse. Esperado: a seta do jogo cai no ponto sob o
  cursor, com sombra no chão, pulsa e some em 5 s; 1 s depois, a do Eco aparece verde ao lado, com o nome.
- [ ] **Controle (pendente):** aperte o R3. Esperado: a marca sai uns 4 m à frente do personagem. Anotar se o R3 ou o
  botão do meio já fazem alguma coisa no próprio jogo.
- [x] **Fora da tela:** marque longe e ande até o ponto sair da tela. Esperado: a seta vai para a borda com a distância.
- [ ] **Dupla:** o aliado vê o seu marcador com o seu nome na cor dos seus números de dano, e você vê o dele.

## 0.16.0 — Reanimação do aliado caído

Só em dupla: o fantasma do modo eco não reanima. Exige a 0.16.0 nos dois PCs (protocolo 17). Dá para testar junto com
a 0.15.0 (seção abaixo), que só teve o teste solo do autor (22/11/2026). Balanceamento (prioridade 1): anotar se reanimar fica fácil
demais no meio da luta ou se 30% de vida é pouco.
- [ ] **Reanimar:** um cai; o outro fica parado ao lado (até 2 m). Esperado: sob a barra do caído, uma barra verde
  enche em 3 s com o rótulo "reanimando"; o caído vê na faixa "<nome> está te reanimando" e a barra. No fim, o caído
  levanta com 30% da vida e todos veem "<nome> te levantou" / "Você levantou <nome>" em âmbar no topo. Log de quem
  reanima: `[Reanimação] Você levantou <nome> (3 s; reanimação 1 do andar N).`; do caído: `[Reanimação] <nome> te
  levantou: você volta com 30% da vida.`
- [ ] **Segunda do andar:** no mesmo andar, caia de novo e reanime. Esperado: leva 6 s (log `Você levantou <nome> (6 s;
  reanimação 2 do andar N)`). No andar seguinte, volta a 3 s.
- [ ] **Golpe:** reanimando, leve um golpe. Esperado: a barra fica vermelha, o rótulo "interrompido" e o progresso
  recua; sem novo golpe por 1 s, volta a encher em verde.
- [ ] **Mover ou usar skill:** reanimando, ande ou use uma skill. Esperado: a barra zera, o rótulo "fique parado" e o
  caído vê "<nome> está ao seu lado". Beber poção não zera.
- [ ] **Queda repetida:** o reanimado cai de novo no mesmo andar. Esperado: no host, o Hell Clock perde mais 10 s.
- [ ] **Métricas:** no fim da run, o `metricas\run-*.json` de cada PC traz `quedas`, `reanimacoes` e `reanimado` no
  andar em que isso aconteceu.
- Se falhar, mandar: o `LogOutput.log` dos dois (linhas `[Reanimação]` e `[Caído]`) e uma captura da tela.

## 0.15.0 — Jogador caído

Solo com o modo eco (F8) cobre a queda, a tela do caído e o fim da run; a volta no próximo andar, o aviso de queda do
aliado e o portal com o host caído ficam para a dupla. Exige a 0.15.0 nos dois PCs (protocolo 16). Balanceamento
(prioridade 1): anotar se o grupo fica fácil demais ou se a perda de 10 s pesa demais.

**Teste solo do autor (22/11/2026, DLL 0.16.0):** queda e relógio confirmados (o personagem fica caído, o Hell Clock
segue correndo). Pedidos para a próxima entrega: (1) refazer a tela do caído com peças do próprio jogo, porque a faixa
atual ficou fraca; (2) com você caído, o menu do jogo não abre: precisa abrir, inclusive para abandonar a run ali.
- [x] **Queda (eco):** numa run, com o eco ligado, morra. Esperado: o personagem deita e não anda nem ataca; a tela fica
  em cinza; embaixo, a faixa "VOCÊ CAIU", "Volta no próximo andar, com 50% da vida" e "1 de 2 de pé"; o Hell Clock
  perde 10 s; os inimigos largam você; a HUD do jogo continua e a run não acaba. Log: `[Caído] Você caiu (−10 s no Hell
  Clock); volta no próximo andar ou se um aliado te reanimar.` e `[Caído] Faixa do jogador caído montada com a fonte do
  jogo.` (sem essa linha, a faixa sai no desenho simples: anotar). Confirmado em 22/11/2026; a faixa vai ser refeita.
- [ ] **Menu caído (eco, falhou em 22/11/2026):** caído, aperte Esc. Hoje o menu do jogo não abre; a correção vem na
  próxima entrega (menu e abandono da run com você caído).
- [ ] **Subir de nível caído (eco):** se o XP dos aliados subir o nível, você continua no chão. Esperado, não é falha:
  sozinho com o eco, ninguém mata inimigos, então isso só aparece na dupla.
- [ ] **Fim da run (eco):** ainda caído, desligue o eco (F8). Esperado: a morte do jogo roda (câmera, resultados) e a
  run acaba como no solo; no hub, o personagem de pé e com a cor normal. Log: `[Caído] Ninguém de pé: a run acaba.`
- [ ] **Dupla, convidado cai:** o convidado morre; o host continua. Host: aviso "<testador> caiu  -10 s" no topo, a barra
  dele com "caído" e a seta em cinza; o Hell Clock perde 10 s. O host passa de andar: o convidado volta de pé com metade
  da vida. Log do convidado: `[Caído] Andar N: você volta com 50% da vida.`
- [ ] **Dupla, host cai:** o host morre; o convidado limpa o andar e usa o portal. Esperado: o grupo todo avança e o
  host volta com metade da vida. Log do convidado: `[Run] O host está caído: pedido para avançar 1 andar(es)`; do host:
  `[Run] Você está caído: <testador> leva o grupo`.
- [ ] **Dupla, os dois caem:** a run acaba para os dois (resultados normais). Log do host: `[Caído] Ninguém de pé: a run
  acaba.` e `[Run] Fim da run (Defeat); avisando os convidados.` O convidado não vê "Sua run acabou...".
- [x] **Hell Clock com o host caído (eco):** caído, o relógio continua descendo (não para; confirmado em 22/11/2026).
- [ ] **Relógio zerado caído (eco):** se zerar com você caído, a run acaba por tempo e, no hub, o personagem está de pé
  com a vida cheia (ainda não visto).
- [ ] **Último andar com o host caído (dupla):** o convidado mata o chefão; o host levanta com metade da vida e usa o
  portal final. Log do host: `[Caído] Último andar limpo: você levanta`.
  Se o host cair depois de o último andar já estar limpo, ele levanta logo em seguida (log igual).
- [ ] **Host em segundo plano (dupla):** com o convidado caído, o host faz alt-tab por mais de 20 s. Anotar se a run do
  convidado acaba (log `[Caído] O host está sem sinal há mais de 20 s.`): o host minimizado pode parar de mandar posição.
- Se falhar, mandar: o `LogOutput.log` dos dois (linhas `[Caído]` e `[Run]`) e uma captura da tela.

## 0.14.0 — Barra de vida e seta dos aliados

Solo com o modo eco (F8) cobre barra e seta; o coop real (vida do outro PC chegando) fica para a sessão em dupla.
Atualizar a partir da 0.13.1. Exige a 0.14.0 nos dois PCs (protocolo 15).

Teste do autor em solo, no hub, em 22/11/2026: passaram montagem, barra, seta e Ajustes. O log mostrou
`[Aliados] Barra de vida e seta montadas com a interface do jogo (seta: UI_Icon_DropDownArrow, 28x25)`; a seta na borda
levou ao fantasma e o giro de 90° confere. A seta saía com ~15 px na tela (a imagem tem margem): passou a 44 unidades
da HUD, ainda não vista no jogo. Ficam abertos a HUD escondida e a dupla.
- [x] **Montagem:** ao entrar numa run (ou no hub) com o eco ligado, o log mostra `[Aliados] Barra de vida e seta
  montadas com a interface do jogo (seta: …)`. Se aparecer "usando o desenho simples", anotar a linha.
- [x] **Barra:** com o eco ligado, a barra do companheiro do jogo aparece sobre o fantasma e acompanha quando ele anda.
  Ao tomar dano, a barra do fantasma cai 1 s depois, com a parte perdida clara por um instante. Poção ou regeneração
  enchem a barra. Ao morrer, a barra do fantasma zera.
- [x] **Seta:** no `.cfg`, `[Teste] DistanciaDoEco = 30`, reabrir o jogo e ligar o eco: o fantasma fica fora da tela e
  a seta na borda aponta para ele, com "Eco" ao lado, do lado de dentro. Andando, a seta corre pela borda; trazendo o
  fantasma para a tela, a seta some e a barra aparece. Anotar se a seta aponta para o lado errado (`GiroBaseSeta` no
  `AliadosNaTela`). Voltar `DistanciaDoEco` para 2,5 depois. Esperado, não é falha: longe, o fantasma atravessa
  paredes e sai do mapa (ele só repete você com a posição deslocada, sem colisão) e os números de dano dele continuam
  aparecendo no seu alvo (o eco repete os seus números no mesmo inimigo; ver `DanosVisiveis`), como se estivesse perto.
- [x] **Ajustes:** "Barra de vida dos aliados" e "Seta do aliado fora da tela" marcam e desmarcam na hora e continuam
  iguais ao reabrir o jogo (`MostrarVidaDosAliados` e `MostrarSetaDosAliados`).
- [ ] **HUD:** barra e seta somem junto com a HUD do jogo (tela preta de troca de andar, cinemática) e não ficam por
  cima do inventário nem da pausa de forma estranha (anotar se ficarem).
- [ ] **Dupla (sessão futura):** a barra do aliado mostra a vida dele, não a sua; com ele longe, a seta aponta para ele.

## 0.13.1 — Esc da janela de aceite das métricas

Solo basta. Para a janela aparecer de novo: no `.cfg`, `[Metricas] AceitePerguntado = false` e
`EnviarResumoAnonimo = false` (com o envio ligado o mod marca como perguntado); depois reponha os valores antigos.
Já conferido no PC do autor em 22/11/2026 (Esc simulado com o jogo em primeiro plano): na tela de título, o Esc fechava a
janela e abria o "fechar o jogo?" mesmo com o bloqueio, porque o ouvinte do Esc carrega depois de a janela abrir e só era
procurado na abertura; agora o log mostra `[Entrada] Ouvinte do Esc da tela de saves desligado (1; 1 no total).` e o Esc
fecha só a janela. Com a janela fechada, o Esc na tela de saves volta a abrir o aviso do jogo. Falta:
- [ ] **Esc:** na tela de saves, com a janela aberta, Esc fecha só a janela (log `[Métricas] Aceite adiado`), sem o
  "Você realmente quer fechar o jogo?".
- [ ] **Respostas:** Enter depois de 3 s envia, N recusa, os botões respondem depois de 1,5 s; o clique não chega ao
  menu do jogo por trás.
- [ ] **Depois:** com a janela fechada, o Esc na tela de saves volta a abrir o "fechar o jogo?" do jogo.
- Se falhar, mandar: o `LogOutput.log` (linhas `[Métricas]` e `[Entrada]`) e uma captura da tela.

## 0.13.0 — Botão Coop no menu e na pausa do jogo

Solo basta (sem mudança de rede nem de balanceamento). Atualizar a partir da 0.12.0. Já conferido no PC do autor em
22/11/2026, com teclas e cliques simulados (só com o jogo em primeiro plano) e capturas: botão "COOP" à esquerda de
"CRÉDITOS" na tela de saves, clique abre o menu; aba Novidades aberta sozinha na primeira abertura da 0.13.0 (grava
`NovidadesVistas = 0.13.0`); F10 abre e fecha na aba, setas rolam; Esc fecha o menu na tela de saves sem o "Você
realmente quer fechar o jogo?" (antes aparecia: o `AndroidBackButtonListener` do jogo lia o Esc por fora do bloqueio);
em jogo, sem texto no canto superior esquerdo; Esc abre a pausa com CONTINUAR, OPÇÕES, COOP e SAIR, o "COOP" tira a
pausa e abre o menu, e o Esc fecha sem reabrir a pausa; ao voltar ao menu pelo "Sair", o botão "COOP" volta. Falta:
- [ ] **Controle na pausa:** com o controle, o direcional chega ao "Coop" da pausa e o A abre o menu; o atalho de botão do
  "Opções" continua abrindo só as Opções (a cópia não herda o atalho).
- [ ] **Controle no menu do jogo:** o direcional chega ao "Coop" ao lado de "Créditos" e o A abre o menu.
- [ ] **Som e destaque:** passar o mouse e clicar no "Coop" (menu e pausa) faz o mesmo destaque e o mesmo som dos botões
  vizinhos.
- [ ] **Animação da tela de saves:** ao escolher um save e ao voltar dele, o "Coop" some e volta junto com "Créditos".
- [ ] **Recado do atualizador:** depois de uma atualização automática, o recado aparece no topo da aba Sessão (e não mais
  no canto).
- Se falhar, mandar: o `LogOutput.log` (linhas `[Menu]` e `[Entrada]`) e uma captura da tela.

## 0.12.0 — Menu do coop com a interface do jogo

Testado pelo autor em solo (22/11/2026): passou; `testado: true` no changelog desde a 0.13.0.

Solo basta (o convite e a saída não mudaram desde a 0.9.3). Atualizar a partir da 0.11.0. Teclas simuladas no PC do
autor em 22/11/2026: a janela abre com a interface do jogo e a seta para a direita troca de aba (com o painel ainda à
direita, antes de ir para o centro). Teste do autor no jogo, em 22/11/2026: a janela central e os itens abaixo passaram,
menos as linhas do cabeçalho, que vazavam pelos lados (eram da largura da janela das Opções). Corrigido: o log mostra
`[Menu] Cabeçalho cabendo na janela (720): …` com as peças encurtadas (conferido: TopLine 880→720, BottomLine 854→720).
Depois, o botão "Menu do coop (F7)" do canto do menu, que ainda era o botão cinza do Unity, passou a usar a peça de aba
das Opções:
- [ ] **Botão do menu:** no menu do jogo, abaixo do painel do canto, o botão "Menu do coop (F7)" tem o visual das abas
  da janela; ao passar o mouse o texto clareia e aparece o enfeite de baixo; o clique abre a janela. Some com a janela
  aberta, durante a run e com a Novidades (F10) aberta, e volta ao fechar. Nas telas de abertura (patrocínio, antes do
  menu de saves) o botão não aparece; antes aparecia o cinza e trocava sozinho.

- [ ] **Abrir e visual:** no menu do jogo, F7 (e o botão "Menu do coop (F7)") abre a janela **colada à direita**, na altura
  toda, entrando pela direita com o deslize das Opções do jogo (0,35 s, leve recuo no fim), com a moldura, o título "COOP",
  o [X] e as abas das Opções; o lado direito da tela escurece em degradê (0.24.0). Ao fechar, sai deslizando para a direita. No log:
  `[Menu] Painel do coop montado com a interface do jogo.` e `[Menu] Menu do coop aberto (tecla F7; interface do jogo).`
- [ ] **Abas:** LB/RB, setas esquerda/direita (fora de uma barra) e o clique na aba trocam entre Sessão e Ajustes, girando
  nas pontas; o foco vai para o primeiro item da aba.
- [ ] **Teclado:** na aba Ajustes, setas cima/baixo movem o foco (o destaque das Opções acompanha), Enter executa. Na aba
  Sessão do menu do jogo sem coop só "Criar coop" é selecionável (o eco pede um save), então ali a seta não move. **Anotar se a seta para
  baixo não mexer o foco** (no teste com teclas simuladas ela não mexeu; pode ter sido o foco do Windows).
- [ ] **Ajustes:** as caixas "Nomes dos aliados", "Números de dano dos aliados" e "Sons das skills dos aliados" marcam e
  desmarcam; as barras "Tamanho do dano" e "Volume dos sons" andam com esquerda/direita e com clique ou arrasto na barra (clicar no rótulo só foca, sem mudar o valor); com os
  números (ou os sons) desligados, a barra correspondente fica apagada. Tudo continua igual depois de reabrir o jogo
  (`[Interface]` e `[Audio]` no .cfg) e as Opções do jogo não mudam.
- [ ] **Balanceamento:** a aba Ajustes mostra os multiplicadores (com 2 jogadores fora do coop), só para leitura.
- [ ] **Em jogo:** durante uma run, F7 abre a janela sem escurecer e sem pausar; clicar nela (e fora) não move o personagem;
  Esc, B, F7 e o [X] fecham sem abrir a pausa.
- [ ] **Controle:** View abre, LB/RB trocam de aba, direcional move e ajusta, A confirma, B fecha; o rodapé mostra as dicas
  do controle.
- [ ] **Telas:** em 1080p, 1366x768 (janela) e ultrawide a janela cabe inteira e não estica; nenhum rótulo corta.
- [ ] **Reserva:** (opcional) se o log disser `menu simples`, o menu antigo (gaveta à direita) aparece com as duas abas
  numa lista só e as barras mudam com esquerda/direita ou clique nas metades.
- Se falhar, mandar: o `LogOutput.log` (linhas `[Menu]` e `[Entrada]`) e uma captura da tela.

## 0.11.0 — Sons das skills e golpes dos aliados

Achado no IL: a cópia dos efeitos do aliado nunca passava pelo `GameplayFx.CreateFx`, que é onde o jogo toca o som do
efeito, e as skills, o golpe e o disparo do aliado não tocavam nada no outro PC. O golpe do aliado num inimigo também saía
sem som de dor e sem sangue, porque o jogo só cria esses efeitos no próprio golpe. Agora os sons vão pela rede (só o
identificador do som) e o golpe se repete no mesmo inimigo. Protocolo 14: o coop exige a 0.11.0 nos dois PCs.

- [ ] **Log ao abrir o jogo:** `[Sons] Contexto de som em N método(s) de skill (0 com falha, 0 ausente(s), 0 sem lançador)`
  e nenhuma linha `[Validação] Fase 8`.
- [ ] **Modo eco (F8), sozinho:** cada skill sua toca de novo ~1 s depois, no fantasma (ataque com arma, skills com
  projétil, área e explosão). O golpe do fantasma no inimigo repete sangue e som de dor.
- [ ] **Coop, cada um na sua vez:** as skills do aliado soam perto dele (mais baixo quando ele está longe), o ataque com a
  arma e os disparos também; o golpe do aliado num inimigo faz o som de dor e o sangue, nos dois sentidos (host e convidado).
- [ ] **Sem som em dobro:** as suas próprias skills soam uma vez só no seu PC (com e sem o aliado perto). O golpe e a morte
  de um inimigo pelo aliado (tiro, facas) soam uma vez só no seu PC.
- [ ] **Sons que duram:** skill contínua ou canalizada do aliado para de soar quando ele para; nada fica tocando sozinho
  depois (no máximo 20 s).
- [ ] **Configuração:** `[Audio] SonsDosAliados = false` no `.cfg` deixa as skills e o som de dor dos aliados mudos (o sangue
  continua); `VolumeDosAliados` entre 0 e 1 muda o volume dos sons das skills (o som de dor dos golpes fica no volume normal).
- [ ] **Versão:** com 0.10.1 de um lado e 0.11.0 do outro, o coop recusa a conexão (protocolo 13 × 14).
- Se faltar som em alguma skill, anotar qual e mandar o `LogOutput.log` dos dois PCs (linhas `[Sons]`, `[Golpes]` e
  `[Fx] Cópia de … sem N emissor(es)`). Corrotinas que tocam vários sons espaçados podem soar só no primeiro: é a parte
  pendente da etapa.

---

## 0.10.0 — XP, ouro, Soulstones e moedas em grupo

Decisão do autor: cada jogador ganha o valor de cada inimigo × 0,83 / 0,71 / 0,63 (2 / 3 / 4 jogadores), só em run com
aliados. Achado no IL: o ganho não é de quem mata; cada PC dá XP e rola o próprio loot na morte local do inimigo, sem
checar distância. O fator é aplicado em cada PC (sem mensagem de rede). Ouro e Soulstones arredondam por sorteio
(a média fica exata); a moeda de criação (sempre 1) sai com chance igual ao fator.

- [ ] **Solo (controle):** XP de um inimigo comum e ouro do chão como na 0.9.9; o log não mostra XP/ouro escalados.
- [ ] **Run em dupla:** em cada PC, o XP por inimigo e o ouro do chão ficam em torno de 83% do solo (comparar o mesmo
  andar e dificuldade). A linha `[Balanceamento] 2 jogador(es): … XP ×0,83, ouro/Soulstones/moedas ×0,83` aparece.
- [ ] **Chefe em dupla:** o XP do chefe também cai para ~83% nos dois PCs.
- [ ] **Bênção de ouro ou de Soulstones em dupla:** o valor oferecido é ~83% do solo.
- [ ] **Vasos e baús em dupla:** o ouro de cada vaso ou baú também cai para ~83% (muda o "igual ao solo" da 0.9.6).
- [ ] **Moedas de missão:** nenhuma moeda obrigatória de progressão deixa de cair (o fator vale para toda moeda do
  `CurrencyTreasureClass`, inclusive fragmentos).
- [ ] **Hub com aliado (controle):** vender equipamento e o ouro inicial da run continuam normais.
- Se falhar, mandar: o `LogOutput.log` dos dois (linhas `[Balanceamento]` e qualquer erro de patch ao iniciar).

---

## 0.9.9 — Dano do aliado no boneco de treino

Responde ao achado "dano do convidado no boneco de treino do hub não aparece no host". Causa achada no IL: o boneco
não é um inimigo compartilhado; cada PC cria o seu ao chamá-lo no hub (`DummyInteractable`), fora da run, e o número
só ia para inimigos com número de rede. Agora o número no boneco vai marcado como "boneco" e aparece no boneco de quem
recebe. Só o número: a vida do boneco continua de cada um.

- [ ] **Convidado → host:** no hub, os dois chamam o boneco; o convidado bate. Esperado: no host, o número aparece no
  boneco dele, na cor do aliado e um pouco menor.
- [ ] **Host → convidado:** o host bate no boneco dele. Esperado: o número aparece no boneco do convidado.
- [ ] **Boneco não chamado (controle):** com o boneco do host guardado, o convidado bate no dele. Esperado: nada no host,
  sem erro no log.
- [ ] **Run (controle):** números do aliado nos inimigos aparecem como antes.
- Se falhar, mandar: o `LogOutput.log` dos dois (linhas `[Dano]`; ao chamar o boneco, cada PC registra
  `[Dano] Boneco de treino registrado`).

---

## Sessão em dupla na 0.9.4 — roteiro único

Decisão do autor (21/11/2026): antes de código novo, uma sessão que cubra tudo que está `testar` no roadmap e o que
falta das versões acima. Aqui vai só a **ordem** e o **que observar**; o esperado completo está na seção da versão
indicada entre parênteses. Sem itens de controle (o autor não tem controle: View/Select, B, LB/RB ficam para depois).

Passou: marque `[x]` no item da seção de origem (é a fonte; o `[x]` daqui só acompanha a sessão). Falhou: anote o que viu, copie o `LogOutput.log` **antes**
de reabrir o jogo e siga para o próximo item. Durante toda a sessão, olhe também se cada `[Unity Exception]` sai com a
pilha e se as repetidas se agrupam em `(repetido Nx)` (0.8.3). As linhas do atualizador saem no log como
`[Info   :Atualizador] ...` (aqui abreviadas para `[Atualizador] ...`).

### Parte A — autor sozinho (modo eco), antes da sessão

**Abrir o jogo**

- [ ] **Backup** (0.8.4): pasta nova em `BepInEx\HellClockCoop\save-backups`. Log: `Backup do save: N arquivo(s) copiados`.
- [ ] **Atualizador sem internet e desligado** (0.8.0): duas aberturas. Log: `[Atualizador] Não deu para verificar` (ou
  `Sem resposta em 5 s`) e `[Atualizador] Verificação desligada`. Volte `VerificarAoIniciar = true` no fim.
- [ ] **Console de volta** (0.8.2): `MostrarConsole = true`, duas aberturas (na 2ª a janela preta abre). Log:
  `Console do BepInEx ligado`. Volte para `false` no fim.

**Menu** (para repetir o primeiro início: `AceitePerguntado = false`, `EnviarResumoAnonimo = false` e `NovidadesVistas =`
vazio no `.cfg`)

- [ ] **Aceite, casos que faltam** (0.8.1), uma abertura cada: clique no primeiro 1,5 s não responde; Enter apertado sem
  parar não aceita; **Esc** adia (log `Aceite adiado`, `.cfg` igual); **Não enviar** (log `Envio dos resumos recusado`);
  por último **Enviar resumos** com resumos já salvos (a janela diz "Vão junto os N resumos"; log `Resumos já salvos postos
  na fila: N` e `Resumo ... enviado`).
- [ ] **Novidades** (0.9.2): na primeira abertura em que a janela do aceite fecha (Esc ou resposta), a janela abre sozinha na aba `v0.9.4 NOVA`; Esc e "Fechar" no
  primeiro 1,5 s não fecham, F10 fecha; não volta na abertura seguinte (`NovidadesVistas = 0.9.4`); setas e clique na aba
  trocam de versão; cabe inteira em 1080p e em 1366x768 (janela). Log: linhas `[Novidades]`. Os itens "Botão do menu"
  e "Durante a run" da 0.9.2 mudaram na 0.9.3 e já passaram lá (item Novidades da 0.9.3).
- [ ] **Eco no menu** (0.9.4): eco ligado, voltar ao menu e carregar o save: o fantasma volta; no menu do jogo, um aperto
  em "Modo eco" desliga. Log: `[Eco] Desligado (...)`.

**Hub, run e andares**

- [ ] **Eco na troca de cena** (0.9.4): eco ligado no hub, entrar na run, passar 2 ou 3 andares: o fantasma reaparece a
  cada andar. Log: `[Eco] Fantasma recriado depois da troca de cena`.
- [ ] **Adagas e orbes no eco** (0.9.4): com o Reflexos (ou o orbe) ativo, trocar de andar: só as adagas girando, sem cópia
  parada. Log: `[Avatar] 'HellClockCoop_Avatar_Eco' criado: N efeito(s) de skill do jogador fora do clone` (N > 0).
- [ ] **Save no eco** (0.8.4): os arquivos de save mudam ao trocar de andar e no fim. Log: nenhum `Plano do host ignorado`,
  `Bônus de tempo de ... recusado` ou `Fim de run com motivo desconhecido`.

**Fim da run**

- [ ] **Run solo enviada** (0.8.1): log `[Métricas] Resumo da run salvo ... 1 jogador(es)` e depois `Resumo ... enviado`;
  no console do Firebase, `papel` = `solo`.

### Parte B — dois PCs (autor = host, testador = convidado)

**Instalar (testador)**

- [ ] **Atualização sozinha no PC do testador** (0.8.0): log `[Atualizador] Atualizado da <antiga> para a 0.9.4`; vindo
  de uma versão com atualizador, o antigo fica em `BepInEx\HellClockCoop\atualizador\anterior\patchers\` e a abertura
  seguinte diz `Em dia: versão 0.9.4`. Instalação nova pelo zip: nenhuma janela preta (0.8.2). Vindo de antes da 0.7.2:
  o `.cfg` antigo é copiado (0.7.2, log `Configuração de <arquivo antigo> copiada`). Falhou: linhas `Atualizador`.
- [ ] **Antes de começar** (topo deste arquivo): mesma versão nos dois e `[Validação] N/N alvos de patch encontrados`. O
  testador responde **Enviar resumos** no aceite.

**Menu e hub**

- [ ] **Valores do autor** (desde a 0.22.0 fixos no código, roadmap B): com o coop criado, o painel mostra
  `Balanceamento 2J: vida ×1,7...`. Log do host: `[Balanceamento] 2 jogador(es): ...`.

**Run 1 — entrada no meio** (o host começa sozinho)

- [ ] **Entrada no meio da run** (roadmap fase 2): o host entra na run, passa 1 andar e só então cria o coop e convida; o
  testador cai no andar do host. Log do convidado: `[Run] Acompanhando a run do host: ... (entrando no meio)` e
  `[Run] Entrando direto no andar N do host`.
- [ ] **Balanceamento no painel** (0.7.0): linha `Balanceamento 2J: ...` e o aviso `inimigos com N% mais vida`.

**Andares** (os dois em cada item; o testador faz a parte de convidado)

- [ ] **Baús e quebráveis** (0.4.1): mesmo lugar, abrem e quebram nos dois, loot de cada um. Log: `[Objetos] ...`.
- [ ] **Inimigos atacam o convidado** (0.5.x). Log do host: `[Alvos] <testador> agora pode ser atacado pelos inimigos`.
- [ ] **Estacas e facas** (0.5.x) e **adagas sem cópia parada** (0.9.4, item Coop). Log: linhas `[Fx]` e `[Avatar]`.
- [ ] **Santuário comum, amaldiçoado e totem** (0.6.0): o amaldiçoado dá o ponto de constelação aos dois. Log: `[Objetos]`.
- [ ] **Tempo ganho pelo convidado** (0.8.5): soma no Hell Clock do host. Log do host: `[Run] <testador> ganhou Ns de Hell
  Clock` e nenhum `acima do teto`.
- [ ] **Status de controle do convidado** (0.9.0 e 0.9.1): lentidão e congelamento (atordoamento/enraizamento, se houver
  skill) valem no host; enfraquecimento/óleo/marca **não** dão dano a mais no host; dano contínuo no mesmo ritmo;
  campeões sem nada estranho. Log do host: nenhum `[Inimigos] De <testador>: status ... recusado` repetido. Anote a skill.
- [ ] **Morte do convidado** (0.15.0), num andar do meio: ele fica caído e volta no próximo andar do host. Log do
  convidado: `[Caído] Andar N: você volta com 50% da vida.`; do host: `[Run] Enviando plano aos convidados` do andar seguinte.
- [ ] **Inimigos mais fortes e dificuldade** (0.7.0, prioridade 1): comparando com a run solo da Parte A (mesmo andar e
  dificuldade), mais vida, um pouco mais de dano e mais inimigos a partir do andar seguinte (falhou: aviso `[Balanceamento]
  Quantidade de inimigos: ...`). Anotar se o grupo achou um pouco mais difícil que o solo, fácil ou difícil demais.

**Chefe**

- [ ] **Chefe no convidado** (0.6.0): barra de vida aparece quando ele fica agressivo e o dano do convidado conta (0.8.5);
  congelar/deixar lento o chefe faz o mesmo que no solo (0.9.0). Log do convidado: nenhum `[Inimigos] Réplica N agressiva falhou`.

**Fim**

- [ ] **Concluir a dungeon** (0.8.4): o convidado acompanha todos os andares. Log do host: `[Run] Fim da run (Completed);
  avisando os convidados.`; do convidado: `[Run] Host encerrou a run (Completed).`
- [ ] **Resumo salvo e enviado nos dois** (0.7.0 e 0.7.1): log `Resumo da run salvo ... 2 jogador(es)` e `Resumo ... enviado`.
- [ ] **Run 2 — portal vermelho** (roadmap fase 2): o host sai pelo portal vermelho no meio da run. Log do convidado:
  `[Run] Host encerrou a run (ManualFinishWithPortal).`
- [ ] **Run 3 — fim por tempo** (roadmap fase 2, se der tempo): deixar o Hell Clock zerar. Log do convidado:
  `[Run] Host encerrou a run (Timer).`

### Achados da sessão de 21/11/2026 (parcial)

Relato do autor no meio da sessão. Nada foi marcado `[x]` ainda.

- Passou: baú aberto pelo convidado abriu também no host (0.4.1, falta o caso do host abrindo).
- Falhou: vaso e caixa quebrados num PC não quebram no outro, nos dois sentidos (0.4.1). Causa (IL): cada PC sorteia
  os seus no `DrawRandomSceneObject`; a busca por posição (0,75 m) falhava sem log. Corrigido na 0.9.6 (seção 0.9.6,
  a testar).
- Falhou: barreira de sangue da área do chefe não aparece no convidado, que passa livre (0.6.0). Corrigido na 0.9.5
  (seção 0.9.5, a testar).
- Falhou: inimigos sem som no convidado. Causa (IL): a réplica não calcula a própria velocidade, e passos e som de
  estado ficavam no modo parado. Corrigido na 0.9.8 (seção 0.9.8, a testar).
- Falhou (menor): dano do convidado no boneco de treino do hub não aparece no host; nos inimigos aparece. Causa (IL):
  cada PC cria o próprio boneco, que não tem número de rede. Corrigido na 0.9.9 (seção 0.9.9, a testar).
- A investigar: travadinhas esporádicas. O log não tinha número de tempo; a 0.9.7 traz o medidor (seção 0.9.7, a
  testar) e a correção sai depois, com os números.

### Depois da sessão

Só o que o autor confirmar: `[x]` na seção de origem; `testado = true` no `docs/changelog.json` da versão cujos
itens passaram todos (fora os de controle); `feito` no `docs/roadmap.json` só quando todos os itens que cobrem aquele item do roadmap
passaram (fora os de controle); depois
`python scripts/gerar-progresso.py`. Itens do roadmap cobertos:

| Item `testar` do roadmap | Onde neste roteiro |
| --- | --- |
| B: escalonamento por número de jogadores | B: balanceamento no painel, dificuldade |
| B: valores ajustáveis no `.cfg` | B: valores ajustáveis |
| B: resumo de cada run salvo | A: run solo; B: resumo nos dois |
| B: envio anônimo com aceite | A: run solo; B: resumo nos dois |
| B: pergunta do aceite no primeiro início | A: aceite |
| B: teto do tempo ganho e golpe inválido | B: tempo ganho, chefe |
| 2: entrada no meio, portal vermelho, fim por tempo | B: run 1, run 2, run 3 |
| 3: baús e quebráveis; 4: loot próprio | B: baús e quebráveis |
| 3b: facas e estacas | B: estacas e facas |
| 3c: inimigos atacam o convidado; convidado caído volta no próximo andar (0.15.0) | B: inimigos, morte do convidado; seção 0.15.0 |
| 3: santuários e totens | B: santuários |
| 3: chefe no convidado | B: chefe |
| 3: status de controle do convidado | B: status de controle |
| 5: janela de novidades | A: novidades |
| 5: modo silencioso | A: console de volta; B: instalação nova |

---

## Mensagem para o testador

> E aí! Saiu a 0.9.5 do coop e queria marcar uma sessão em dupla pra testar tudo de uma vez (uma run inteira, mais duas
> curtas). Se você já está na 0.8.0 ou mais nova, é só abrir o jogo que ele se atualiza sozinho (o painel do coop avisa);
> abre, espera chegar no menu, fecha e abre de novo. Se está numa versão antes da 0.8.0, baixa o zip da release mais
> recente (`HellClockCoop-v0.9.5.zip`), abre a pasta do jogo (Steam → Hell Clock → Gerenciar → Procurar arquivos locais)
> e extrai lá dentro, substituindo tudo.
>
> No menu pode aparecer a janela perguntando se pode enviar os resumos das runs: se puder, clica em "Enviar resumos" (vai
> só número da run e o id aleatório da instalação, nada de nome, Steam ou arquivo teu). Depois abre a janela Novidades,
> com o que mudou. O menu do coop agora fica na F7.
>
> Na sessão eu vou te pedir umas coisas de convidado: ir na frente pros inimigos te atacarem, usar skill de lentidão ou
> congelamento, as adagas do Reflexos, pegar tempo, bater no chefe e morrer de propósito num andar. Se der algo estranho,
> me fala na hora e, antes de abrir o jogo de novo, me manda o `BepInEx\LogOutput.log` (ele apaga ao abrir). Valeu!
