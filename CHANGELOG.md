# Novidades — Hell Clock Coop

> Gerado a partir de [`docs/changelog.json`](docs/changelog.json) por `scripts/gerar-progresso.py`.

Legenda: ✅ testada no jogo · 🧪 aguardando teste no jogo.

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
