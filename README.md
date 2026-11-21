<p align="center">
  <img src="assets/banner.svg" alt="Hell Clock Co-op — mod de fã cooperativo para Hell Clock" width="100%">
</p>

<p align="center">
  <img alt="Versão" src="https://img.shields.io/badge/vers%C3%A3o-0.7.0-ff7a1a?style=for-the-badge">
  <img alt="Status" src="https://img.shields.io/badge/status-alpha%20jog%C3%A1vel-b3121f?style=for-the-badge">
  <img alt="Hell Clock" src="https://img.shields.io/badge/Hell%20Clock-2.1.1.1-4a1a1f?style=for-the-badge">
  <img alt="Jogadores" src="https://img.shields.io/badge/jogadores-2%20a%204-5fe08a?style=for-the-badge">
  <br>
  <img alt="BepInEx" src="https://img.shields.io/badge/BepInEx-5.4.23.6-2b2b2b?style=flat-square">
  <img alt="HarmonyX" src="https://img.shields.io/badge/HarmonyX-patches-2b2b2b?style=flat-square">
  <img alt="Unity" src="https://img.shields.io/badge/Unity-6000.0.60f1%20(Mono)-2b2b2b?style=flat-square&logo=unity">
  <img alt="Steam" src="https://img.shields.io/badge/rede-Steam%20P2P-2b2b2b?style=flat-square&logo=steam">
  <img alt="C#" src="https://img.shields.io/badge/C%23-.NET%20Framework%204.x-2b2b2b?style=flat-square&logo=csharp">
  <img alt="Licença" src="https://img.shields.io/badge/licen%C3%A7a-MIT-2b2b2b?style=flat-square">
  <img alt="Idioma" src="https://img.shields.io/badge/idioma-pt--BR-2b2b2b?style=flat-square">
</p>

<p align="center">
  <b>Jogue Hell Clock com os amigos.</b> Mesma run, mesmos inimigos, um vendo as skills do outro —<br>
  e cada um com a própria progressão, no próprio save.
</p>

<p align="center">
  <a href="#-o-que-já-funciona">O que funciona</a> ·
  <a href="#-progresso">Progresso</a> ·
  <a href="#-instalação">Instalação</a> ·
  <a href="#-como-jogar">Como jogar</a> ·
  <a href="#%EF%B8%8F-como-funciona">Como funciona</a> ·
  <a href="ROADMAP.md">Roadmap</a> ·
  <a href="CHANGELOG.md">Novidades</a>
</p>

---

> [!IMPORTANT]
> **Mod de fã, gratuito e sem fins lucrativos.** Não é feito nem apoiado pela Rogue Snail. O repositório e o pacote
> contêm só o código do mod e o carregador BepInEx: **nenhum arquivo, arte ou texto do jogo** é redistribuído.
> Para jogar, cada pessoa precisa ter o próprio Hell Clock na Steam.

## 🔥 O que já funciona

<table>
<tr>
<td width="50%" valign="top">

### 👥 Juntos pela Steam
- Lobby de amigos, convite pelo overlay e entrada pelo "Entrar no jogo".
- Checagem de versão do mod e da DLC Cursed War.
- Avatar do aliado com animações, mira, roupas com física e aparência.
- Nome sobre a cabeça e **seta verde no minimapa**.

### 🗺️ A mesma run
- A run é do **host**: os convidados vão junto, automaticamente.
- Mesmo andar, mesmo mapa, mesmo ponto de spawn.
- O **Hell Clock é um só** — tempo ganho por qualquer um vai para ele.
- Quem entra no meio da run cai no andar do host.

</td>
<td width="50%" valign="top">

### ⚔️ Combate compartilhado
- **Os mesmos inimigos para todos**: tipo, afixos de elite, vida, animação.
- O dano de cada um conta; quando morre, morre para todos.
- **Um vê as skills do outro**: tiros, projéteis, áreas, auras, orbes, facas.
- **Números de dano dos aliados** com cor própria por jogador.
- Inimigos atacam quem estiver mais perto — host ou convidado.
- Baús, vasos/caixas, **santuários** e **totens** iguais para todos.
- **Chefes** com barra de vida e dano também para o convidado.

### 🎒 Progressão individual
- Loot, ouro, XP, relíquias e Bênçãos: **cada um no próprio save**.
- O mod **nunca escreve no save de outro jogador**.
- Backup automático do save a cada vez que o jogo abre.

</td>
</tr>
</table>

## 📈 Progresso

<p align="center">
  <img src="assets/progresso.svg" alt="Progresso por fase" width="880">
</p>

O plano completo, item por item, está no **[ROADMAP](ROADMAP.md)**, e as versões no **[CHANGELOG](CHANGELOG.md)**.
Também há um **site do projeto** em [`docs/index.html`](docs/index.html) (abra no navegador; com o repositório público,
vira página do GitHub Pages sem mudar nada). Uma fase só conta como concluída depois de
confirmada rodando no jogo, com dois PCs.

| Fase | Entrega | Situação |
|:-:|---|---|
| 0 | Ambiente: BepInEx num jogo com *managed stripping*, backup do save, mapa dos patches | ✅ confirmada no jogo |
| 1 | Fantasma: um vê o outro pela Steam | ✅ confirmada no jogo |
| 2 | Mesma run: andar, mapa, spawn e Hell Clock do host | ✅ confirmada no jogo |
| 3 | Combate: inimigos, skills, dano, baús e quebráveis | 🧪 em teste (3a confirmada) |
| 4 | Loot individual | 🧪 parcial |
| 5 | Polimento: menu de coop, reconexão, sons, dificuldade, tradução | ⬜ planejada |

## ⚖️ Balanceamento — prioridade 1

Hell Clock foi feito para uma pessoa. Cada sistema que o coop libera (dano, loot, tempo, chefes) muda de escala quando
entra mais gente, então o balanceamento é uma **trilha permanente**: toda versão passa por ele.

- **Meta:** em grupo, o jogo fica **um pouco mais difícil** que no solo.
- **Como:** o mod usa as mesmas alavancas que o jogo usa na dificuldade e na Ascensão: vida e dano dos inimigos,
  quantidade de inimigos por andar e tempo ganho no Hell Clock, com valores por número de jogadores
  (configuráveis, seção `[Balanceamento]` do `.cfg`).
- **Com dados:** cada run gera um resumo anônimo (tempo por andar, inimigos mortos, dano causado e recebido, mortes)
  em `BepInEx/HellClockCoop/metricas`. O envio opcional, com aceite, e um painel solo × grupo estão no roadmap.

| | 2 jogadores | 3 jogadores | 4 jogadores |
|---|:-:|:-:|:-:|
| Vida dos inimigos | ×1,7 | ×2,4 | ×3,1 |
| Vida dos chefes | ×1,8 | ×2,6 | ×3,4 |
| Dano dos inimigos | ×1,1 | ×1,2 | ×1,3 |
| Quantidade de inimigos | ×1,2 | ×1,4 | ×1,6 |
| Tempo ganho no Hell Clock | ×0,55 | ×0,40 | ×0,32 |

<sub>Valores iniciais, a calibrar com as métricas.</sub>

## 📦 Instalação

> [!TIP]
> Baixe o zip **da mesma versão** que os seus amigos. Versões diferentes não se conectam.

1. Na Steam: botão direito em **Hell Clock → Gerenciar → Procurar arquivos locais**.
2. Extraia **todo** o conteúdo do zip `HellClockCoop-v<versão>.zip` **dentro dessa pasta** (ao lado do `Hell Clock.exe`), substituindo os arquivos.
3. Abra o jogo pela Steam. Uma janela preta (console do BepInEx) abre junto: é o registro do mod.
4. No canto superior esquerdo aparece **"Hell Clock Coop … F7 para jogar com amigos"**.

<details>
<summary><b>O que o zip instala</b></summary>

| Caminho | O que é |
|---|---|
| `winhttp.dll`, `doorstop_config.ini` | Carregador do BepInEx (UnityDoorstop) |
| `BepInEx/core` | BepInEx 5.4.23.6 + HarmonyX |
| `BepInEx/unstripped_corlib` | Bibliotecas base completas do Mono para Windows (o jogo vem com versões reduzidas) |
| `BepInEx/plugins/HellClockCoop/HellClockCoop.dll` | O mod |

Licenças de terceiros: `BepInEx/plugins/HellClockCoop/LICENCAS.txt`.
</details>

<details>
<summary><b>Desinstalar</b></summary>

- **Desativar sem apagar:** renomeie `winhttp.dll` para `winhttp.dll.off`.
- **Remover:** apague `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` e a pasta `BepInEx`
  (copie antes os backups de save, se quiser).
- "Verificar integridade dos arquivos" na Steam **não** remove o mod (ele só adiciona arquivos).
</details>

## 🎮 Como jogar

Requisito: overlay da Steam ligado (*Steam → Configurações → No jogo*).

| Tecla | Ação |
|:-:|---|
| <kbd>F7</kbd> | Cria o coop e abre o convite da Steam (de novo: reabre o convite) |
| <kbd>F6</kbd> | Sai do coop |
| <kbd>F8</kbd> | **Modo eco**: um fantasma seu repete tudo com 1 s de atraso — testa o mod sem amigo |
| <kbd>F9</kbd> | Diagnóstico: salva a estrutura do personagem |

**Host** (quem convida): entre num save e aperte <kbd>F7</kbd>. **Convidado**: aceite o convite na Steam (com o jogo
fechado, ele abre e entra sozinho). Quando o host entra numa run, todos vão junto.

<details>
<summary><b>Regras do coop nesta versão</b></summary>

- Portais, início e abandono de run são do host. Para sair sozinho: <kbd>F6</kbd> e depois o menu.
- Se o **convidado** morre, a run dele acaba (resultados normais) e ele volta quando o host passar de andar.
  Se o **host** morre, a run acaba para todos.
- Com alguém no coop, abrir inventário, mapa ou menu **não pausa** o jogo.
- Todos precisam ter (ou não ter) a DLC **Cursed War**. A Deluxe Edition (cosmética) não importa.
- Ajustes em `BepInEx\config\br.srmackenzie.hellclockcoop.cfg`: teclas, nomes sobre a cabeça,
  números de dano dos aliados (`MostrarDanoDosAliados`, `TamanhoDanoDosAliados`).
</details>

## ⚙️ Como funciona

O mod roda dentro de cada jogo, via **BepInEx 5**, e altera o comportamento com patches **HarmonyX**. A rede usa o
Steamworks que o próprio jogo já traz (lobbies + *SteamNetworkingMessages*, com relay da Steam). Cada máquina tem
**um único jogador de verdade**; os aliados são avatares.

```mermaid
flowchart LR
    subgraph H["🖥️ Host"]
        HJ["Jogador local"]
        HR["Run, andar, Hell Clock<br/>inimigos com IA"]
        HA["Avatares dos aliados<br/>(alvos dos inimigos)"]
    end
    subgraph C["🖥️ Convidado"]
        CJ["Jogador local"]
        CR["Run seguindo o host<br/>réplicas sem IA"]
        CA["Avatar do host"]
    end
    HR -- "plano do andar · timer · spawn/morte/estado dos inimigos · skills dos inimigos" --> CR
    CJ -- "golpes nas réplicas" --> HR
    HJ <-- "snapshots 20 Hz · efeitos das skills · números de dano" --> CJ
    HJ -. desenha .-> CA
    CJ -. desenha .-> HA
```

| Quem decide | O quê |
|---|---|
| **Host** | Andar sorteado, spawn, Hell Clock, inimigos (IA, vida, morte), baús, fim da run |
| **Cada jogador** | O próprio personagem, o próprio cálculo de dano, o dano que recebe, loot, XP e save |

<details>
<summary><b>Detalhes técnicos</b></summary>

- **Managed stripping:** o `mscorlib` do jogo vem reduzido e o BepInEx não sobe com ele. O pacote usa as bibliotecas
  base completas do Mono para Windows em `BepInEx\unstripped_corlib` (`dll_search_path_override` no Doorstop).
  O build compila contra as DLLs do próprio jogo, e `tools/refcheck` garante que nenhuma API removida é usada.
- **Avatar:** clone do personagem com toda a lógica removida (ficam malhas, Animator, panos MagicaCloth e a seta do
  minimapa). Animação sincronizada por estado de cada layer, peso, parâmetros e rotação dos ossos do tronco.
- **Inimigos:** o host numera cada inimigo e manda definição, tipo e afixos já sorteados; o convidado cria a réplica
  pelo próprio `EnemyManager`, sem IA. Golpes do convidado viram relatos ao host; a morte vem do host e dispara o
  loot local de cada um. Quando um inimigo usa uma skill, a réplica repete a skill no convidado — se o alvo é ele,
  o dano é calculado no jogo dele, com as defesas dele.
- **Skills:** todo efeito nasce em `GameplayFx.CreateFx` a partir de um prefab do Addressables, com o mesmo GUID nos
  dois jogos. Quem recebe instancia uma cópia só visual (sem scripts, colisores ou som) presa ao avatar, ao inimigo
  ou solta no mundo, com posição e partes internas transmitidas.
- **Segurança:** todo alvo de patch é validado ao iniciar; se algum sumir numa atualização do jogo, nada é patcheado.

A documentação completa, com cada patch, mensagem e decisão, está em
**[`docs/ARQUITETURA.md`](docs/ARQUITETURA.md)**.
</details>

## 🛠️ Para desenvolvedores

```bash
# compilar (só .NET SDK 8+, sem NuGet)
scripts/build.sh "<jogo>/Hell Clock_Data/Managed" "<BepInEx>/BepInEx/core"

# testes
dotnet run --project tools/codectest -p:HellClockManaged="<jogo>/Hell Clock_Data/Managed"   # ida e volta das mensagens
dotnet run --project tools/refcheck -- "<jogo>/Hell Clock_Data/Managed" build/HellClockCoop.dll  # APIs removidas pelo stripping

# zip para os jogadores
python3 scripts/empacotar.py --bepinex BepInEx_win_x64_5.4.23.6.zip --corlib <swyf-modding/Setup>/vendor/corlib

# progresso (README + ROADMAP) a partir de docs/roadmap.json
python3 scripts/gerar-progresso.py
```

Veja **[CONTRIBUTING.md](CONTRIBUTING.md)** para as regras do projeto e **[docs/DESENVOLVIMENTO.md](docs/DESENVOLVIMENTO.md)**
para o ambiente (BepInEx no Hell Clock, corlib, testes da Fase 0).

<details>
<summary><b>Estrutura do repositório</b></summary>

```
src/HellClockCoop/
├── Plugin.cs, PatchTargets.cs      entrada do BepInEx, versão, catálogo de alvos de patch
├── CoopRuntime.cs                  laço principal, teclas, interface, modo eco
├── Net/                            Steam (lobby, P2P), protocolo e mensagens
├── Avatar/                         clone do jogador, animação, aparência
├── Fase2/                          run compartilhada
└── Fase3/                          inimigos, alvos, efeitos, dano, baús e quebráveis
tools/codectest, tools/refcheck     testes
scripts/                            build, empacotamento, progresso
dist-template/                      LEIA-ME e licenças que vão no zip
docs/                               arquitetura, desenvolvimento, roadmap.json
```
</details>

## 🧭 Projeto irmão

**[Hell Clock — Leitor de Save e Montador de Builds](https://claude.ai/artifact/VUQi2ne8Ho6CN3ixZwLCUK)**, também de
SrMackenzie: lê o save, mostra presets, relicário e dano por skill de cada run, e monta builds. O visual deste
repositório (paleta brasa e ouro, serifa, brasas) segue o dele.

## 🤝 Créditos

- **Autor:** [SrMackenzie](https://github.com/SrMackenzie)
- **Testes:** amigos testadores
- **Hell Clock** © Rogue Snail — jogue o original na Steam.
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) · [HarmonyX](https://github.com/BepInEx/HarmonyX) (MIT) ·
  [UnityDoorstop](https://github.com/NeighTools/UnityDoorstop) (LGPL-2.1) · bibliotecas do Mono (MIT) via
  [swyf-modding/Setup](https://github.com/swyf-modding/Setup) · [ILSpy / ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy) (MIT), usado só localmente para estudo.

## 📜 Licença

O código do mod é distribuído sob a **[licença MIT](LICENSE)** © 2026 SrMackenzie.
Hell Clock, seus nomes, arte e conteúdo pertencem à Rogue Snail e **não** fazem parte desta licença.

<p align="center"><sub>Feito com ⏳ e 🔥 por SrMackenzie · mod cooperativo, offline do ponto de vista de competição: não mexe em conquistas, placares nem recursos online do jogo.</sub></p>
