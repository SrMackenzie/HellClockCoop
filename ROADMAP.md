# Roadmap — Hell Clock Coop

> Atualizado em 26/11/2026 · versão 0.31.6. Gerado a partir de [`docs/roadmap.json`](docs/roadmap.json) por `scripts/gerar-progresso.py`: edite o JSON, não este arquivo.

<p align="center"><img src="assets/progresso.svg" alt="Progresso por fase" width="880"></p>

**Legenda:** ✅ feito · 🧪 pronto, falta testar no jogo · 🔨 em andamento · ⬜ planejado

Regra do projeto: uma fase só é dada como concluída depois de confirmada **rodando no jogo**.

## Trilha permanente — Balanceamento · 62%

_Prioridade 1, trilha permanente: o coop deve ser um pouco mais difícil que o solo, com ajustes guiados por dados. Toda versão passa por revisão de balanceamento._

| | Item |
|:-:|---|
| 🧪 | Escalonamento por número de jogadores: vida, chefes, dano, quantidade de inimigos e tempo ganho |
| 🧪 | XP, ouro, Soulstones e moedas de criação em grupo: cada um × 0,83 / 0,71 / 0,63 (2 / 3 / 4 jogadores), só em run |
| 🧪 | Valores do autor, fixos no código (vida, chefes, dano, quantidade, tempo, XP, ouro, volta do caído e reanimação), sem liga/desliga |
| 🧪 | Resumo de balanceamento de cada run, salvo no computador |
| 🧪 | Envio anônimo dos resumos, só com aceite (Firebase, só criação) |
| 🧪 | Pergunta do aceite no primeiro início, numa janela no menu (runs solo e coop) |
| 🧪 | Teto do tempo ganho por convidado (120 s a cada 60 s) e golpe com dano inválido ignorado |
| 🧪 | Teto de inimigos e baús por andar calculado no convidado com os dados do próprio jogo × quantidade do mod, com conta à parte para sobrevivência e invocações |
| ✅ | Painel de balanceamento: solo × 2, 3 e 4 jogadores, por andar e dificuldade |
| ⬜ | Ajuste individual quando o convidado está muito abaixo ou acima do host |
| 🧪 | Jogador caído: a run só acaba quando todos caem; o caído volta no próximo andar com 50% da vida e cada queda tira 10 s do Hell Clock |
| 🧪 | Reanimação do aliado caído, com as quedas e reanimações no resumo da run |
| 🧪 | Tela do jogador caído refeita com peças do próprio jogo |
| 🧪 | Menu do jogo com você caído, inclusive para abandonar a run ali |
| ⬜ | Calibragem do jogador caído com os dados da dupla |
| ⬜ | Apagar os resumos enviados ao fechar a calibragem |

## Fase 0 — Ambiente e mapeamento · 100%

_BepInEx funcionando num jogo com managed stripping, backup do save e mapa dos pontos de patch._

| | Item |
|:-:|---|
| ✅ | BepInEx 5 + bibliotecas base completas de Windows (unstripped_corlib) |
| ✅ | Backup automático da pasta de save a cada início (15 cópias) |
| ✅ | Checagem da versão do jogo e validação de todos os alvos de patch |
| ✅ | Patches de diagnóstico (spawn, run, andares, alvos, Steam) |
| ✅ | Erros da Unity copiados para o log do BepInEx |
| ✅ | Erro conhecido do jogo na tela de saves ("same key [-1]") vira uma linha de aviso, sem pilha |

## Fase 1 — Fantasma · 100%

_Um jogador vê o outro andando e atacando, pela Steam._

| | Item |
|:-:|---|
| ✅ | Modo eco (F8): fantasma local para testar sem amigo |
| ✅ | Avatar-clone sem lógica, com animações sincronizadas por estado e parâmetros |
| ✅ | Mira (ossos do tronco), roupas com física (MagicaCloth) e aparência |
| ✅ | Lobby de amigos da Steam, convite, aperto de mão e checagem de versão/DLC |
| ✅ | Nome sobre a cabeça e seta verde no minimapa |

## Fase 2 — Mesma run · 95%

_O host é dono da run: todos no mesmo andar, mesmo mapa, mesmo Hell Clock._

| | Item |
|:-:|---|
| ✅ | Convidado puxado para a run do host |
| ✅ | Mesmo andar sorteado e mesmo ponto de spawn |
| ✅ | Hell Clock do host (4x por segundo) e tempo ganho pelo convidado |
| ✅ | Fim da run do host encerra a de todos; sem pausa com aliados |
| 🧪 | Entrada no meio da run, portal vermelho e fim por tempo |

## Fase 3 — Combate · 83%

_Mesmos inimigos para todos, cada um vê as skills do outro, inimigos atacam qualquer jogador._

| | Item |
|:-:|---|
| ✅ | 3a: inimigos do host replicados no convidado (tipo, afixos, vida, animação) |
| ✅ | 3a: dano do convidado aplicado pelo host; morte vale para todos |
| 🧪 | Baús iguais para todos e vasos/caixas quebrados para todos |
| ✅ | 3b: efeitos das skills (tiros, projéteis, áreas, auras, orbes) no avatar |
| ✅ | 3b: números de dano dos aliados, com cor própria por aliado |
| 🧪 | 3b: facas que giram e estacas cravadas nos inimigos |
| 🧪 | 3c: inimigos atacam o convidado, com o dano calculado no jogo dele |
| 🧪 | 3c: convidado que morre fica caído e volta no próximo andar do host (0.15.0; antes, a run dele acabava) |
| 🧪 | Santuários e totens de bênção compartilhados |
| 🧪 | Chefe no convidado: fase agressiva, barra de vida e dano |
| 🧪 | Barreiras de sangue (chefe, sobrevivência, santuário amaldiçoado) iguais para todos, com o convidado levado para perto do host |
| 🧪 | Status de controle do convidado (lentidão, congelamento, atordoamento, enraizamento) valendo no host; os de dano ficam pessoais |
| 🧪 | Boneco de DPS do hub visível para o outro |

## Fase 4 — Loot · 90%

_Progressão individual: cada um ganha o próprio loot, ouro e XP, no próprio save._

| | Item |
|:-:|---|
| ✅ | Loot e XP de cada inimigo morto saem do jogo de cada um |
| 🧪 | Loot próprio de baús e quebráveis |
| ✅ | Revisar XP de inimigos mortos longe do jogador (o jogo não confere distância: cada um ganha o XP de todo inimigo morto) |
| 🧪 | Revisar drops de invocações, ondas de sobrevivência e temporadas (convidado com o XP e o tesouro próprio do host; equipamento sem redução, decisão do autor) |
| ✅ | Auditoria: o mod nunca escreve no save de outro jogador |

## Fase 5 — Polimento · 57%

_Deixar o coop confortável para qualquer pessoa instalar e jogar._

| | Item |
|:-:|---|
| ✅ | Atualização automática ao abrir o jogo: assinada, conferida e com volta se falhar |
| 🧪 | Janela de novidades dentro do jogo: abre sozinha depois de cada atualização, com o que testar |
| 🧪 | Modo silencioso: console do BepInEx desligado por padrão, com opção no .cfg para ligar |
| ✅ | Menu de coop dentro do jogo (no lugar das teclas F6/F7): gaveta com F7 ou View/Select, que segura a entrada do jogo enquanto aberta |
| ✅ | Menu do coop com a interface do jogo: janela com as abas Sessão e Ajustes, nas fontes e cores das Opções, e os ajustes do mod (nomes, dano, sons e volume dos aliados) gravados no .cfg |
| 🧪 | Botão Coop na lista de botões do menu do jogo e item Coop na pausa; novidades como aba da janela do coop |
| 🧪 | Menu do coop colado à direita, na altura toda, com a animação de abertura das Opções do jogo (0.24.0) |
| 🧪 | Nome da sala (padrão "Sala de <host>", o host renomeia na aba Sessão) e a sua sala no topo da aba Salas (0.25.0) |
| 🧪 | Nome da sala digitável no jogo, menu abrindo na Sessão com Criar/Convidar em primeiro, menu sem picos em jogo e opacidade dos danos dos aliados (0.26.0) |
| ✅ | Painel de grupo à esquerda: você no topo e os aliados embaixo, nome com a coroa do host, barra de vida, skills equipadas embaixo e até 4 buffs/debuffs em cima, sem recarga nem duração (0.27.0) |
| 🧪 | Barra de convicção (barreira) por cima da vida no painel de grupo e na barra sobre a cabeça dos aliados, igual à barra do jogo (0.28.0) |
| 🧪 | Teleporte até um aliado: símbolo de TP na linha dele no painel de grupo, tecla T (D-pad ←) ou clique; mesmo andar, fora de combate, canaliza 2 s e recarga de 60 s (0.29.0) |
| 🧪 | Correção: o nome dos jogadores e os acúmulos dos buffs voltam a aparecer no painel de grupo (0.31.4) |
| 🧪 | Correções da dupla, etapa 1 (0.31.6): o chefe volta a atacar quem está de pé quando um aliado cai perto dele; o host que sai para o menu no meio da run encerra a run dos convidados; o convidado que travou ao subir de nível ganhou registro no log (causa ainda não achada) |
| ⬜ | Correções da dupla, etapa 2: dano do convidado que às vezes aparece 0 nos inimigos e ouro/XP que parecem não ser divididos |
| 🧪 | Visual do teleporte: raio com anel de recarga no painel, contorno na cor do aliado durante a canalização e o efeito de sumir e reaparecer do jogo (0.30.0) |
| ⬜ | Reconexão e troca de host sem perder a run |
| 🧪 | Sons das skills e dos golpes dos aliados |
| 🧪 | Aliados na tela: barra de vida sobre o aliado e seta apontando quem está fora da tela |
| 🧪 | Marcador (ping): o botão do meio do mouse (R3 no controle) marca um ponto no chão que todos veem por 5 s, com o nome na cor do aliado |
| 🧪 | Lista de salas (1/3): ao criar o coop, sala pública (padrão) ou privada; aba Salas com as salas compatíveis (versão e DLC), host, jogadores, hub ou andar da run e nível; entrar também com a run em andamento; o host pode expulsar |
| 🧪 | Lista de salas (2/3): só acompanha a run quem já chegou àquela masmorra (ato e tier) e andar no próprio save; a sala aparece travada na lista, e quem não chegou fica no hub, fora da escala do grupo |
| 🧪 | Lista de salas (3/3, parte 1): coop mais seguro com desconhecidos: o host não conclui a masmorra nem avança a run de outra masmorra no save do convidado, valores de inimigos e santuários fora do jogo são recusados, par sem versão compatível não manda nada além do aperto de mão, e com quem não é amigo na Steam a conexão passa só pelo relay (sem IP) |
| 🧪 | Salas com e sem a DLC Cursed War: os atos 1 a 3 e a Ascensão são para todos, o Ato 4 só para quem tem a DLC (sala indisponível na lista e convidado no hub); o menu do coop abre na aba Salas, numa janela maior, com cada sala em 2 linhas |
| 🧪 | Lista de salas (3/3, parte 2): o host da run fica fixo até ela acabar (quem herda o lobby no meio não comanda a sua run), o expulso é cortado por todos, baús, quebráveis e santuários só valem do host (ou, no host, de quem está no mesmo andar), tetos de inimigos, chefes e baús por andar, andar limpo repetido só com inimigos novos, bônus de tempo só de quem divide a run, atalhos só os dois do jogo e valores fora do jogo recusados nos snapshots |
| 🧪 | Lista de salas (3/3, parte 3): bloquear jogador pela aba Sessão ou Salas (X, Delete ou clique direito), salas do bloqueado escondidas, host expulsa o bloqueado sempre que ele entra, bloqueio da Steam também conta, lista só local com Desbloquear em Ajustes |
| ⬜ | Lista de salas (3/3, restante): filtros na lista |
| ⬜ | Troca de host: se o host sair ou cair, quem herdar a sala vira o host de verdade (a run em andamento termina e o grupo segue junto a partir do hub; assumir a run no meio não é viável, porque inimigos, chão e tempo só existem no host) |
| ⬜ | Frases rápidas: avisos curtos prontos que aparecem sobre o personagem, sem chat de texto |
| ⬜ | Ir até o aliado: botão que leva para perto do host quem ficou para trás, com tempo de recarga |
| ⬜ | Ver a build do aliado: aba só de leitura com as skills e o nível de cada jogador da sessão |
| ⬜ | Resumo da run em grupo: dano, abates e tempo ganho de cada jogador no fim da run |
| 🔨 | Correção das travadas esporádicas, com os números do medidor de travadas |
| 🔨 | Desempenho com 4 jogadores e muitos inimigos |
| 🔨 | Desempenho, segunda fase (sobras medidas na primeira): engasgo do dono da sala a cada 5 s na rede (0.31.5: não voltou em 10 min medidos; o medidor separa as chamadas à Steam), cópia de efeito pesada no começo do andar, busca das peças da HUD ao chegar ao andar, mensagens de efeito (FxNos) e medição em dupla |
| ⬜ | Tradução para inglês e instalador |
