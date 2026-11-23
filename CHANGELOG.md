# Novidades — Hell Clock Coop

> Gerado a partir de [`docs/changelog.json`](docs/changelog.json) por `scripts/gerar-progresso.py`.

Legenda: ✅ testada no jogo · 🧪 aguardando teste no jogo.

## 0.19.0 — Lista de salas (2/3): trava pelo progresso 🧪
_23/11/2026_

- O coop não adianta a campanha de ninguém: só acompanha a run do host quem já chegou àquela masmorra (mesmo ato e mesmo tier: Normal, Abyss, Oblivion ou Void; Sobrevivência e Ascensão também) e àquele andar no próprio save
- Na aba Salas, a sala numa run aonde o seu save não chegou aparece indisponível, com o seu recorde no lugar das vagas ("você chegou ao andar 4")
- Convite de amigo e host que vai mais longe: quem não chegou lá fica no coop, no hub, com um aviso, e acompanha a próxima run liberada. O host é avisado, e esse jogador sai da escala do grupo (inimigos, tempo, XP e ouro) enquanto não acompanha
- Hub, menu e atalhos especiais não têm trava. O andar 1 de um ato que você nunca começou conta como não alcançado
- Exige a 0.19.0 em todos os PCs (protocolo 20)

## 0.18.0 — Lista de salas (1/3): salas públicas 🧪
_22/11/2026_

- Nova aba Salas no menu do coop: as salas públicas do mundo inteiro com a mesma versão do mod e a mesma DLC Cursed War, com host, lugar (hub ou ato e andar), nível do host e vagas
- Escolha uma sala para entrar, também com a run em andamento (você entra no andar do host). Salas cheias, de outra versão ou de outra DLC não aparecem
- O coop novo nasce como sala pública. O interruptor "Sala pública" da aba Sessão a deixa só para amigos (por convite); como host, muda a sala na hora. Fica no .cfg, em [Coop] SalaPublica
- O host pode expulsar: na seção Aliados, "Expulsar <nome>" e confirmar. Quem foi expulso sai e não volta à mesma sala
- Exige a 0.18.0 em todos os PCs (protocolo 19)

## 0.17.0 — Marcador no chão (ping) 🧪
_22/11/2026_

- O botão do meio do mouse marca o chão sob o cursor; no controle, o R3 (apertar o analógico direito) marca alguns metros à sua frente
- Todos do grupo veem o marcador por 5 s: a seta do jogo apontando o chão, com o nome de quem marcou na cor dele (a mesma dos números de dano)
- Fora da tela, a seta fica na borda apontando o ponto, com o nome e a distância
- Cada jogador tem 1 marcador: marcar de novo move o antigo (no máximo 1 por segundo). A tecla muda no .cfg, em [Teclas] Marcador
- Só informação: não muda o jogo nem a dificuldade. Exige a 0.17.0 em todos os PCs (protocolo 18)

## 0.16.3 — Valores de balanceamento do autor fixos no mod 🧪
_22/11/2026_

- A volta do jogador caído no próximo andar (50% da vida) e os 10 s que cada queda tira do Hell Clock agora são fixos no mod, iguais para todos
- O XP e o ouro (com Soulstones e moedas de criação) em grupo também ficaram fixos: × 0,83 / 0,71 / 0,63 com 2 / 3 / 4 jogadores
- Saíram do .cfg: VidaAoVoltarDoChao, SegundosPerdidosPorQueda, XpGanho e OuroGanho não são mais lidos (se ainda estiverem no arquivo, não fazem efeito). Os valores são calibrados pelo autor com os resumos das runs
- Vida, chefes, dano, quantidade de inimigos e tempo ganho continuam no .cfg, seção [Balanceamento]
- Exige a 0.16.3 em todos os PCs (o protocolo continua o 17)

## 0.16.2 — Tela do jogador caído com peças do jogo 🧪
_22/11/2026_

- Caído, a faixa escura saiu: além do cinza, a sua luz encolhe e as bordas da tela pulsam em vermelho, com a vinheta de vida baixa do próprio jogo
- "VOCÊ CAIU" aparece acima do personagem com a revelação e o som da tela de morte do jogo
- Reanimando, as bordas pulsam mais devagar; golpe no reanimador dá um pico vermelho. A volta, a barra e os pontos de quem está de pé continuam embaixo
- Os avisos de queda de aliado e de reanimação (inclusive "Fulano te levantou") agora entram na pilha de avisos da HUD do jogo, em vermelho ou âmbar
- Só visual, mas o lobby pede a mesma versão: exige a 0.16.2 em todos os PCs (o protocolo continua o 17)

## 0.16.1 — Menu do jogo com você caído 🧪
_22/11/2026_

- Caído, o Esc abre o menu do jogo, como de pé. Inventário e janelas continuam fechados até você levantar
- Host caído: "Terminar run" encerra a run para todos, como o host de pé
- Convidado caído: "Terminar run" tira só você. O grupo continua e você não é puxado de volta no próximo andar; volta na próxima run do host
- A duração (3 s, 6 s, 9 s...) e a vida da reanimação (30%) agora são fixas no mod: saíram do .cfg (SegundosParaReanimar e VidaAoReanimar não são mais lidos)
- Exige a 0.16.1 em todos os PCs (o protocolo continua o 17)

## 0.16.0 — Reanimação do aliado caído 🧪
_22/11/2026_

- Fique parado ao lado de um aliado caído, a até 2 m e sem usar skill, para levantá-lo: não precisa de tecla. Ele volta com 30% da vida
- A primeira reanimação de cada andar leva 3 s, a segunda 6 s, a terceira 9 s, e assim por diante: o grupo não vira imortal
- Golpe recebido durante a reanimação congela o progresso por 1 s, e ele vai caindo enquanto você apanha. Andar, usar skill ou se afastar zera o progresso
- Visual: uma barra verde enche sob a barra de vida do aliado, com o rótulo "reanimando", "interrompido" ou "fique parado". Quem está caído vê na faixa de baixo "Fulano está te reanimando" e a barra. No fim, todos veem "Fulano levantou Beltrano" no topo da tela
- Balanceamento: a queda continua tirando 10 s do Hell Clock, também quando o mesmo jogador cai de novo depois de reanimado. No .cfg, seção [Balanceamento]: SegundosParaReanimar e VidaAoReanimar
- Resumo da run (métricas com aceite): cada andar passa a contar as suas quedas, os aliados que você levantou e as vezes que te levantaram. Protocolo 17: exige a 0.16.0 em todos os PCs

## 0.15.0 — Jogador caído: a run só acaba quando todos caem 🧪
_22/11/2026_

- No coop, quem morre fica caído no chão, em vez de a run acabar (host) ou de voltar ao hub (convidado). A run só acaba quando todos caem
- Quem caiu vê a tela em cinza, como na segunda chance do jogo, e uma faixa embaixo dizendo que volta no próximo andar com 50% da vida e quantos seguem de pé
- Os aliados veem "caído" na barra de vida e a seta em cinza. Cada queda mostra "Fulano caiu" no topo da tela
- Com o host caído, o convidado leva o grupo: o portal dele pede ao host que avance o andar. No último andar, quando o chefão morre, o host caído levanta com metade da vida para usar o portal final
- Balanceamento: cada queda tira 10 s do Hell Clock e a dificuldade continua contando o grupo todo, para o coop seguir um pouco mais difícil que o solo. No .cfg, seção [Balanceamento]: SegundosPerdidosPorQueda (vale o do host) e VidaAoVoltarDoChao (vale o de cada PC)
- Para testar sozinho, o fantasma do modo eco (F8) conta como aliado de pé. Desligue o eco com você caído e a run acaba. Protocolo 16: exige a 0.15.0 nos dois PCs

## 0.14.0 — Barra de vida e seta dos aliados 🧪
_22/11/2026_

- Cada aliado ganhou uma barra de vida sob o nome, a mesma barra que o jogo põe sobre o companheiro. Quando ele perde vida, a parte perdida fica clara um instante antes de sumir
- Aliado fora da tela: uma seta do jogo na borda aponta para ele, com o nome ao lado
- Na aba Ajustes do menu do coop, "Barra de vida dos aliados" e "Seta do aliado fora da tela" ligam e desligam cada um (no .cfg: MostrarVidaDosAliados e MostrarSetaDosAliados)
- No modo eco (F8) o fantasma também tem barra e seta. Para testar a seta, ponha DistanciaDoEco = 30 na seção [Teste] do .cfg: o fantasma fica fora da tela
- Se a barra ou a seta do jogo não puderem ser montadas, aparecem num desenho simples
- Balanceamento: sem mudança, é só informação na tela. Protocolo 15 (a vida agora vai junto da posição): exige a 0.14.0 nos dois PCs

## 0.13.1 — Esc da janela de aceite das métricas 🧪
_22/11/2026_

- Corrigido: na tela de saves, o Esc que adia a janela de aceite das métricas também abria "Você realmente quer fechar o jogo?". A janela agora segura a entrada do jogo do mesmo jeito que o menu do coop
- O mesmo bloqueio passa a achar o ouvinte do Esc do jogo também quando ele carrega depois de a janela abrir (telas de abertura)
- Balanceamento: sem mudança. Protocolo 14, o mesmo da 0.13.0, mas o coop continua exigindo a mesma versão do mod nos dois PCs (0.13.1)

## 0.13.0 — Botão Coop no menu e na pausa do jogo 🧪
_22/11/2026_

- O menu do jogo (tela de saves) ganhou um botão "Coop" na lista de botões do canto superior direito, ao lado de "Créditos", com o mesmo visual e o mesmo som dos botões do jogo. Ele substitui o botão que ficava abaixo do painel do canto
- A pausa do jogo (Esc em jogo) ganhou o item "Coop", logo abaixo de "Opções": tira a pausa e abre o menu do coop
- As novidades viraram a terceira aba da janela do coop, com a mesma moldura e as mesmas fontes. A F10 abre direto nela, e ela abre sozinha uma vez depois de cada atualização, já com a tela de saves carregada
- O texto do canto superior esquerdo ficou mais limpo: saiu a dica "F7 abre o menu do coop" e o recado do atualizador foi para a aba Sessão. No canto ficam só a sessão, o modo eco e os avisos curtos
- Se a interface do jogo não puder ser montada, tudo volta ao visual antigo: o botão abaixo do painel do canto, a gaveta do menu e a janela separada das novidades
- Balanceamento: só interface, sem mudança de dificuldade. Protocolo 14, o mesmo da 0.12.0, mas o coop continua exigindo a mesma versão do mod nos dois PCs (0.13.0)

## 0.12.0 — Menu do coop com a cara do jogo ✅
_22/11/2026_

- O menu do coop (F7 ou View/Select) virou uma janela no centro da tela feita com as peças das Opções do próprio jogo: moldura, fontes, cores, abas, caixas de marcar e barras. O botão "Menu do coop" no canto do menu do jogo também ganhou esse visual
- Duas abas, trocadas com LB/RB, com as setas laterais ou com o clique: Sessão (estado, aliados, criar e convidar, sair, modo eco) e Ajustes
- Na aba Ajustes, além dos nomes e dos números de dano dos aliados: o tamanho do dano, os sons das skills dos aliados e o volume deles, que antes só mudavam no .cfg. Tudo continua gravado no .cfg do mod, nunca nas opções do jogo
- A aba Ajustes mostra também o balanceamento deste .cfg, só para leitura
- Se a interface do jogo não puder ser montada (por exemplo, depois de uma atualização do jogo), o menu volta ao visual antigo, com as mesmas funções
- Balanceamento: sem mudança de dificuldade. Protocolo 14, o mesmo da 0.11.0, mas o coop continua exigindo a mesma versão do mod nos dois PCs (0.12.0)

## 0.11.0 — Sons das skills e golpes dos aliados 🧪
_22/11/2026_

- As skills do aliado apareciam sem som: a cópia dos efeitos é só visual e o jogo toca o som de cada efeito num ponto por onde a cópia não passa. A skill em si, o ataque com a arma e os disparos também não tocavam nada no outro PC
- Agora o som de cada skill, ataque e disparo do aliado vai pela rede (só o identificador do som) e toca no seu PC, perto dele ou preso ao efeito, junto com a animação. Sons que duram param quando param no PC dele
- O golpe do aliado num inimigo passa a fazer o som de dor do tipo de dano, o sangue e a luz, como o seu; antes saía mudo nos dois sentidos
- Novo no .cfg, seção [Audio]: SonsDosAliados (liga e desliga) e VolumeDosAliados (0 a 1). No modo eco (F8), o fantasma repete os seus sons para testar com um PC só
- Balanceamento: só som e efeito, sem mudança de dificuldade. Protocolo 14: o coop exige a 0.11.0 nos dois PCs

## 0.10.1 — Sobrevivência e temporadas iguais no convidado 🧪
_21/11/2026_

- Nas ondas de sobrevivência e nos inimigos das temporadas, o convidado ganhava XP e loot diferentes do host: a réplica do inimigo não levava o multiplicador de XP das ondas nem o tesouro próprio que o jogo dá a eles
- Agora a réplica recebe os dois. O convidado ganha o XP das ondas como o host e deixa de sortear loot comum onde o jogo não dá nenhum (ondas do santuário de Miracles, que o jogo abre com tesouro vazio; único de Nightmare) ou dá um tesouro próprio (ondas e chefe da sobrevivência)
- Invocações já saíam sem XP e sem loot nos dois PCs; XP, ouro, Soulstones e moedas dessas fontes já passavam pelos fatores da 0.10.0. O equipamento continua sem redução em grupo, por decisão do autor: é o prêmio do coop
- Ainda não sincronizado: os prêmios de temporada (portal de Nightmare, fim do santuário de Miracles) só saem no PC do host, e cada um joga com a temporada do próprio save
- Balanceamento: tira um ganho extra que só o convidado tinha. Protocolo 13: o coop exige a 0.10.1 nos dois PCs

## 0.10.0 — XP e ouro em grupo 🧪
_21/11/2026_

- Em grupo cada jogador ganhava 100% do XP, do ouro, das Soulstones e das moedas de criação de cada inimigo, e com mais inimigos por andar (×1,2 / 1,4 / 1,6) o ganho passava do solo
- Agora, só em run com aliados, cada um ganha o próprio × 0,83 / 0,71 / 0,63 com 2 / 3 / 4 jogadores, o que deixa o ganho por andar perto do solo. Vale para tudo que o jogo sorteia como drop: inimigos, chefes, vasos e caixas, baús, loot do andar e as bênçãos de ouro e de Soulstones. Nas moedas de criação o fator é a chance de o drop sair
- Valores no .cfg (seção Balanceamento: XpGanho e OuroGanho), lidos no PC de cada um. No solo e no hub nada muda. Venda de equipamento e ouro inicial não mudam
- Balanceamento: o ganho por andar fica perto do solo (0,83 × 1,2 ≈ 1,0), sem deixar o grupo subir de nível mais rápido. Protocolo não muda (12), mas o coop exige a mesma versão do mod nos dois PCs

## 0.9.9 — Dano do aliado no boneco de treino 🧪
_21/11/2026_

- Dano do convidado no boneco de treino do hub não aparecia no host (sessão de 21/11): cada PC cria o próprio boneco, que não é um inimigo compartilhado, então o número não tinha para onde ir
- Agora o número de dano do aliado no boneco aparece no seu boneco, com a cor do aliado, nos dois sentidos. Para ver, o seu boneco precisa estar chamado (ativado no hub)
- Só visual: o boneco continua de cada um, e a vida dele não muda com o golpe do aliado. Balanceamento: sem efeito na dificuldade. Protocolo não muda (12), mas o coop exige a mesma versão do mod nos dois PCs

## 0.9.8 — Passos dos inimigos no convidado 🧪
_21/11/2026_

- Inimigos sem som no convidado (sessão de 21/11): a cópia do inimigo no convidado não calculava a própria velocidade, então os passos nunca tocavam e o som de estado (rosnado, respiração) ficava sempre no modo parado
- Agora o convidado mede quanto cada inimigo anda na tela e passa essa velocidade aos passos e ao som de estado, como o jogo faz com os inimigos dele
- Só som: posição, dano e vida não mudam. Balanceamento: sem efeito na dificuldade. Protocolo não muda (12), mas o coop exige a mesma versão do mod nos dois PCs

## 0.9.7 — Medidor de travadas 🧪
_21/11/2026_

- Diagnóstico das travadinhas: o mod agora cronometra cada parte dele a cada quadro e, quando um quadro passa de 50 ms (e de 2,5 vezes o normal), escreve no log quanto foi do mod e de qual parte, se teve coleta de lixo, rede e log naquele quadro
- A cada 5 minutos em jogo, e ao passar do solo para o coop, o log ganha um resumo: quadros, média, pior quadro, travadas e o peso de cada parte do mod; os carregamentos ficam fora da conta
- Medidos à parte os três suspeitos: criar o avatar do aliado, carregar o prefab de um efeito do aliado (carga síncrona) e copiar o efeito
- Só mede: nada muda no jogo, na rede ou no save. Balanceamento: sem efeito na dificuldade. Protocolo não muda (12), mas o coop exige a mesma versão do mod nos dois PCs

## 0.9.6 — Vasos e caixas iguais nos dois PCs 🧪
_21/11/2026_

- Vasos, caixas e outros objetos que o jogo sorteia em cada ponto do mapa agora saem iguais no host e no convidado: o host manda uma semente por andar e os dois PCs sorteiam com ela (antes cada PC sorteava os seus, e quebrar num não quebrava no outro)
- Quebrar um vaso ou caixa quebra também no outro PC, nos dois sentidos, e cada um continua com o próprio loot
- Diagnóstico: ao entrar no andar, o log mostra quantos quebráveis há e uma assinatura (devem ser iguais nos dois PCs); quando um quebrável do outro não é achado, o log diz onde foi e a que distância está o mais próximo
- Balanceamento: sem efeito na dificuldade; o loot por vaso ou caixa de cada jogador é o mesmo do solo. Protocolo 12: os dois PCs precisam da 0.9.6

## 0.9.5 — Barreiras de sangue também no convidado 🧪
_21/11/2026_

- Barreiras de sangue (luta de chefe, andar de sobrevivência e santuário amaldiçoado): quando fecham ou abrem no host, fecham ou abrem também no convidado, que antes passava livre por elas
- Quando as barreiras fecham, o convidado que estiver longe é levado para perto do host, como o jogo já faz com os companheiros
- Quem chega ao andar com as barreiras já fechadas também as vê fechadas (o host reenvia a cada 2 s) e é levado para perto do host; se o host sair do coop, as barreiras fechadas por ele reabrem no convidado
- Balanceamento: o grupo volta a lutar preso na arena como no solo, sem o convidado escapar; um pouco mais difícil que antes. Protocolo 11: os dois PCs precisam da 0.9.5

## 0.9.4 — Modo eco que fica ligado e adagas sem cópia parada 🧪
_21/11/2026_

- Modo eco: continua ligado ao trocar de andar, entrar numa run ou voltar ao menu; o fantasma reaparece junto com o personagem (antes ele sumia e o primeiro F8 só desligava o eco)
- Adagas do Reflexos e orbes de poder não aparecem mais duplicadas no fantasma do eco (umas paradas, outras girando): o boneco não leva mais uma cópia parada dos efeitos que estavam ativos quando ele foi criado
- A mesma correção vale no coop: o boneco de um aliado não aparece mais com as suas adagas ou orbes paradas
- Balanceamento: sem efeito (só visual e modo de teste). Protocolo não muda (10), mas o coop exige a mesma versão do mod nos dois PCs

## 0.9.3 — Menu do coop dentro do jogo ✅
_21/11/2026_

- Novo menu do coop: uma gaveta à direita da tela com os aliados e o estado de cada um, Criar coop e convidar amigos (Steam), Sair do coop (pede confirmação), Modo eco e os ajustes de nomes e números de dano dos aliados, além das Novidades
- Abre e fecha pela F7, pelo botão View/Select do controle ou pelo botão "Menu do coop" no canto do menu; fecha também por Esc, B e pelo X. A F6 deixou de existir: sair do coop agora é pelo menu (a F7 deixou de criar o coop direto; se você tinha mudado a tecla CriarOuConvidar no .cfg, ajuste a nova MenuDoCoop)
- Com o menu ou a janela Novidades aberta, o jogo não recebe mouse, teclado nem controle: dá para clicar durante a run sem mover o personagem e Esc ou B não abrem a pausa. O jogo continua rodando e o personagem fica parado
- A janela Novidades agora responde ao mouse também durante a run
- Balanceamento: sem efeito nos números; com o menu aberto o seu personagem fica parado e o jogo segue (no coop não pausa), então abra o menu longe dos inimigos. Protocolo não muda (10), mas o coop exige a mesma versão do mod nos dois PCs

## 0.9.2 — Janela de novidades dentro do jogo 🧪
_21/11/2026_

- Nova janela "Novidades": mostra o que mudou na versão atual do mod e nas 3 anteriores, sem precisar abrir a página da release
- Abre sozinha uma vez, no menu, na primeira vez que o jogo abre depois de uma atualização; depois, pelo botão "Novidades" no menu ou pela tecla F10 (também durante o jogo)
- Versões ainda não testadas no jogo mostram a seção "O que testar", com o roteiro curto para quem testa
- Setas ou LB/RB trocam de versão; Esc, F10 ou B fecham. Durante o jogo a janela responde só ao teclado e ao controle, para o clique não mover o personagem; feche com F10 (Esc e B também chegam ao jogo)
- Balanceamento: sem efeito (só interface). Protocolo não muda (10), mas o coop exige a mesma versão do mod nos dois PCs (o atualizador baixa a 0.9.2 sozinho ao abrir o jogo)

## 0.9.1 — Só o controle do convidado vale para todos 🧪
_21/11/2026_

- Continuam valendo no jogo do host o congelamento, o atordoamento, o enraizamento e a lentidão que o convidado aplica: o que mexe no corpo do inimigo é de todos
- Deixam de valer no host os efeitos do convidado que mexem no dano (vulnerabilidade, menos resistência, óleo, reações elementais): ficam pessoais, como no solo
- Balanceamento: na 0.9.0 o host também ganhava os enfraquecimentos do convidado, o que deixava o coop mais fácil. Agora cada um só aproveita os próprios; combos entre jogadores ficam para depois, guiados pelos resumos das runs
- Protocolo não muda (10), mas o coop exige a mesma versão do mod nos dois PCs (o atualizador baixa a 0.9.1 sozinho ao abrir o jogo)

## 0.9.0 — Lentidão e congelamento do convidado valem para todos 🧪
_21/11/2026_

- Lentidão, congelamento, atordoamento e enraizamento que o convidado aplica agora valem no jogo do host: o inimigo fica lento ou parado para todos (antes só na tela do convidado). Enfraquecimentos de atributo (como menos defesa) e reações elementais do convidado também passam a valer
- Valem as regras do host: inimigo imune segue imune e a resistência a controle encurta o efeito como no solo
- Dano contínuo (sangramento, queimadura) não muda: o dano dele já chegava ao host
- Duração ou força fora do normal vindas do convidado são limitadas (até 30 s); valores inválidos são ignorados, com aviso no log
- Balanceamento: controle e enfraquecimentos do convidado passam a valer para o grupo, o que deixa o coop um pouco mais fácil que antes; o escalonamento por número de jogadores não muda nesta versão. Acompanhar nos resumos das runs e ajustar se o coop ficar mais fácil que o solo
- Protocolo 10: todos no coop precisam estar na 0.9.0 (o atualizador baixa sozinho ao abrir o jogo)

## 0.8.5 — Teto do tempo ganho e golpes inválidos 🧪
_21/11/2026_

- O host soma no máximo 120 s de Hell Clock ganhos por um mesmo convidado a cada 60 s (e até 120 bônus); o que passar disso é ignorado, com um aviso no log por minuto
- Sair e voltar ao lobby não zera esse teto (ele é contado pela conta Steam do convidado)
- Golpe de convidado com dano não numérico ou infinito é ignorado pelo host (antes podia matar qualquer inimigo de uma vez)
- Balanceamento: o tempo ganho normal não muda; o teto só barra rajadas de tempo acima de 120 s por minuto vindas do mesmo convidado

## 0.8.4 — Save protegido de mensagens inválidas 🧪
_21/11/2026_

- Auditoria do save concluída: o mod nunca escreve no save de outro jogador; o que entra no save do convidado é gravado pelo próprio jogo dele
- O convidado ignora um plano de andar além do último andar da dungeon e um fim de run com motivo desconhecido (aviso no log)
- O host ignora bônus de tempo inválido (não numérico, zero, negativo ou acima de 120 s); o convidado ignora timer não numérico ou negativo
- Inimigos, baús e santuários novos e a purificação de santuário amaldiçoado só são aceitos quando vêm do host
- Backup do save: BackupsMantidos = 0 ou negativo no .cfg passa a manter pelo menos 1 cópia
- Balanceamento: sem mudança nos valores normais; só um bônus de tempo acima de 120 s vindo de um convidado deixa de valer

## 0.8.3 — Erro conhecido da tela de saves ✅
_21/11/2026_

- O erro "An item with the same key has already been added. Key: [-1]" da tela de saves é do próprio Hell Clock 2.1.1.1: acontece também sem o mod
- No LogOutput.log ele aparece agora numa única linha de aviso, sem a pilha: "[Jogo] erro conhecido do Hell Clock 2.1.1.1 na tela de saves (acontece sem o mod; pode ignorar)"
- Os outros erros da Unity continuam no log como antes, com a pilha e o agrupamento das repetições
- Sem efeito no balanceamento

## 0.8.2 — Modo silencioso 🧪
_21/11/2026_

- A janela preta do console do BepInEx não abre mais junto com o jogo
- Para ver o log ao vivo: [Diagnostico] MostrarConsole = true no .cfg do mod; vale a partir da próxima abertura do jogo (o console abre antes do mod carregar)
- Quem atualiza pelo atualizador automático também passa para o modo silencioso: o mod grava a opção no BepInEx.cfg ao iniciar
- O LogOutput.log continua sendo gravado com ou sem o console
- Sem efeito no balanceamento

## 0.8.1 — Pergunta do envio das métricas 🧪
_21/11/2026_

- No primeiro início, uma janela no menu pergunta se pode enviar os resumos das runs: "Enviar resumos" ou "Não enviar"
- A janela diz o que vai (só dados da run e o id aleatório da instalação), o que não vai, para onde vai e por quanto tempo, e como mudar depois no .cfg
- A resposta fica em [Metricas] EnviarResumoAnonimo e a pergunta não volta ([Metricas] AceitePerguntado)
- Enter escolhe "Enviar resumos" só depois de 3 s sem apertar Enter e N escolhe "Não enviar"; Esc (ou B no controle) deixa para o próximo início
- Ao aceitar, os resumos já salvos no computador também entram na fila (a janela diz quantos)
- Runs solo entram igual às de coop: são a base de comparação do balanceamento
- Sem efeito no balanceamento

## 0.8.0 — Atualização automática ✅
_21/11/2026_

- Ao abrir o jogo, o mod procura a versão nova no GitHub e instala sozinho; o painel do coop avisa
- Só aceita o manifesto assinado pelo autor e arquivos com o SHA-256 conferido; troca só a pasta do mod e o próprio atualizador
- Os arquivos substituídos ficam guardados e voltam sozinhos se a conferência da instalação falhar; a pasta do mod também volta se o jogo fechar no meio da troca
- Sem internet ou cerca de 5 s sem resposta: o jogo abre com a versão instalada
- Opção [Atualizacao] VerificarAoIniciar para desligar
- Privacidade das métricas: resumos pseudonimizados, guardados até o fim da calibragem; exclusão pelo id da instalação
- Sem efeito no balanceamento

## 0.7.2 — Identificador novo do mod 🧪
_21/11/2026_

- Identificador do mod trocado para br.srmackenzie.hellclockcoop; o arquivo de configuração passa a ter esse nome
- Na primeira abertura, a configuração antiga é copiada para o arquivo novo (nenhum ajuste se perde)

## 0.7.1 — Envio opcional das métricas 🧪
_21/11/2026_

- Opção EnviarResumoAnonimo (desligada por padrão): envia o resumo de cada run para calibrar o balanceamento
- Só números, sem nome da Steam, SteamID, log ou arquivos do computador; o envio passa pelo cliente da Steam
- Banco exclusivo do mod que só aceita criar resumos no formato certo; ninguém lê pela internet
- Fila local: sem internet, o resumo espera e é enviado depois

## 0.7.0 — Balanceamento por número de jogadores 🧪
_21/11/2026_

- Coop um pouco mais difícil que o solo: inimigos e chefes com mais vida, um pouco mais de dano e mais inimigos por andar
- Tempo ganho no Hell Clock ajustado ao grupo (cada um pega as próprias recompensas de tempo)
- Todos os valores ajustáveis no arquivo de configuração (seção Balanceamento)
- Resumo de balanceamento de cada run salvo em BepInEx/HellClockCoop/metricas (anônimo, só números)
- Linha do balanceamento atual no painel do coop

## 0.6.0 — Santuários, totens e chefes no coop 🧪
_21/11/2026_

- Santuários do host para todos: quem usar ativa para todos, cada um com o próprio efeito
- Santuário amaldiçoado: a sobrevivência roda no host para todos e cada um ganha o próprio ponto de constelação
- Totem de bênção no mesmo lugar, com escolha e compra individuais (só para quem já liberou os totens)
- Chefes no convidado: quando o chefe fica agressivo no host, o convidado vê a barra de vida e consegue causar dano
- Inimigos agressivos no host ficam agressivos também no convidado

## 0.5.4 — Eco completo e efeitos que se mexem por dentro 🧪
_21/11/2026_

- Facas que giram em volta do jogador (Reflexos) e outras partes de efeitos mexidas por script aparecem no aliado
- Efeitos cravados em inimigos (estacas, status) ficam presos no mesmo inimigo
- O modo eco recebe tudo o que o coop recebe

## 0.5.3 — Números de dano com cor por aliado ✅
_21/11/2026_

- Verde, ciano e lilás por aliado (críticos em verde-limão, azul e magenta)
- Números dos aliados a 80% do tamanho, configurável
- Opções MostrarDanoDosAliados e TamanhoDanoDosAliados

## 0.5.2 — Atraso certo, orbes e números de dano ✅
_21/11/2026_

- Efeitos saem junto com a animação do avatar (e com o atraso do eco)
- Orbes giratórias e efeitos criados antes de o aliado chegar
- Números de dano dos aliados nos inimigos

## 0.5.1 — Correção dos efeitos das skills ✅
_21/11/2026_

- O patch de GameplayFx.CreateFx não ligava (Ambiguous match): agora escolhe o método certo

## 0.5.0 — Fase 3b e 3c: skills visíveis e inimigos atacam todos 🧪
_21/11/2026_

- Efeitos das skills do aliado (tiros, projéteis, áreas, auras)
- Inimigos perseguem e atacam o host ou o convidado, o que estiver mais perto
- O convidado leva o dano no próprio jogo, com as próprias defesas
- Convidado que morre volta no próximo andar do host

## 0.4.1 — Baús e quebráveis 🧪
_21/11/2026_

- Baús do host no mesmo lugar para todos; abriu um, abre para todos
- Vaso ou caixa quebrado quebra para todos
- Loot próprio de cada jogador

## 0.4.0 — Fase 3a: os mesmos inimigos para todos ✅
_20/11/2026_

- Inimigos do host replicados no convidado: tipo, afixos de elite, vida e animação
- Dano do convidado aplicado pelo host; morte vale para todos
- Loot e XP de cada um saem do próprio jogo

## 0.3.1 — Seta do aliado no minimapa ✅
_20/11/2026_

- Seta verde mostrando para onde o aliado olha
- Avatar sem avisos de Canvas

## 0.3.0 — Fase 2: a mesma run ✅
_20/11/2026_

- Convidado puxado para a run do host
- Mesmo andar, mapa e ponto de spawn
- Hell Clock do host e fim da run para todos
- Sem pausa com aliados

## 0.2.1 — Guarda de animação ✅
_20/11/2026_

- StateMachineBehaviours do jogo neutralizados no avatar (sem erros a cada troca de estado)

## 0.2.0 — Fase 1b: coop pela Steam ✅
_20/11/2026_

- Lobby de amigos, convite e aperto de mão
- Checagem de versão e DLC
- Aparência e roupas do aliado
- Zip pronto para os jogadores

## 0.1.1 — Ataques no fantasma ✅
_20/11/2026_

- Pesos das layers de ataque e mira pelos ossos do tronco

## 0.1.0 — Fase 1a: modo eco ✅
_20/11/2026_

- Fantasma local que repete o jogador com 1 s de atraso

## 0.0.1 — Fase 0: ambiente ✅
_20/11/2026_

- BepInEx num jogo com managed stripping
- Backup automático do save
- Validação dos alvos de patch e diagnóstico
