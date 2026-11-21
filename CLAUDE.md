# Hell Clock Coop — instruções para o Claude Code

Mod cooperativo de fã para Hell Clock (Steam 1782460, v2.1.1.1, Unity 6000.0.60f1, Mono) com BepInEx 5.4.23.6 + HarmonyX.
Autor: SrMackenzie. Guia do ambiente: `docs/DESENVOLVIMENTO.md`. Arquitetura e decisões: `docs/ARQUITETURA.md`.

## Regras do projeto (sempre)
- Responder sempre em português do Brasil. Código, logs, comentários e documentação em pt-BR.
- Mod de fã, sem fins lucrativos. Nunca redistribuir arquivos do jogo: o mod é só a nossa DLL e o loader.
- Não incluir no repositório nenhum arquivo, arte ou texto extraído do jogo (nem código decompilado).
- Só cooperativo: não mexer em conquistas, placares ou recursos online do jogo.
- Progressão individual: cada jogador mantém o próprio save; o mod nunca escreve no save de outro.
- Antes de qualquer teste, backup automático da pasta de save (o mod já faz ao iniciar; não remover).
- Cada patch Harmony documentado com a classe e o método alvo (comentário `/// Alvo:` e entrada em `PatchTargets.cs`).
- Checar a versão do jogo ao iniciar o mod.
- **Balanceamento é prioridade 1:** em grupo o jogo deve ficar um pouco mais difícil que no solo. Toda mudança passa por
  essa revisão (trilha "Balanceamento" no roadmap).
- Métricas: só com aceite, anônimas (id aleatório da instalação), só o resumo numérico. Nunca nome/ID da Steam, log ou caminhos.
- `EnvioConfig.local.cs` (destino do envio) é local e fica fora do git. Nunca commitar chaves, e-mails ou dados de outros projetos.

## Armadilhas conhecidas
- O mscorlib do jogo é *stripped*: compilar sempre contra a pasta `Managed` do jogo e rodar o `refcheck` (0 ausentes).
  Ex.: `float.MaxValue` não existe (usar `3.4e38f`).
- Nunca chamar `SteamManager.Instance` nem `PlayerController.Instance` (é Guaranteed e cria um). Usar `CoopRuntime.JogadorLocal()`.
- Rede: `Protocolo.Versao` sobe sempre que uma mensagem muda; toda mensagem nova ganha teste em `tools/codectest`.
- Host é a autoridade; convidado manda pedidos (golpes, tempo, interações) e recebe estado.
- Atualizador (`src/HellClockCoop.Atualizador`, preloader): compila contra `BepInEx\unstripped_corlib`, não contra a
  `Managed`; não usa Unity. A chave privada que assina as releases fica fora do git e nunca é regerada nem commitada.

## Rotina de entrega
1. Compilar (`scripts/build.ps1` no Windows ou `scripts/build.sh`), rodar `refcheck` (plugin e atualizador),
   `codectest` e `atualizadortest`.
2. Atualizar `docs/roadmap.json` e `docs/changelog.json`, depois `python scripts/gerar-progresso.py`.
3. Atualizar `PluginVersion` em `Plugin.cs` e `Atualizador.Versao` (iguais), o LEIA-ME em `dist-template/` e, se for o
   caso, README/ARQUITETURA.
4. Empacotar com `scripts/empacotar.py` (assina o manifesto), instalar as DLLs com `scripts\build.ps1 -Instalar`.
5. Commit como SrMackenzie (e-mail noreply) e push para `SrMackenzie/HellClockCoop` (público, MIT).
6. Release `v<versão>` com os anexos de `dist/release-v<versão>/` (o atualizador dos jogadores lê a última release).

## Testes no jogo
- Log: `<jogo>\BepInEx\LogOutput.log` (sobrescrito ao abrir o jogo).
- F8 = modo eco (clone com 1 s de atraso, para testar sem segundo PC). F9 = diagnóstico da hierarquia do personagem.
- Coop real: autor (host) + testador, contas Steam diferentes.
