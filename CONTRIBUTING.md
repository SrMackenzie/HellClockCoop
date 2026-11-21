# Como contribuir

Obrigado pelo interesse no **Hell Clock Coop**! Este é um mod de fã, sem fins lucrativos, mantido por
[SrMackenzie](https://github.com/SrMackenzie).

## Regras do projeto (não negociáveis)

1. **Nada do jogo entra no repositório.** Nenhum arquivo, arte, texto, áudio ou código decompilado do Hell Clock.
   O mod é só o nosso código e o carregador BepInEx.
2. **Só cooperativo.** Não mexer em conquistas, placares ou recursos online do jogo.
3. **Progressão individual.** Cada jogador mantém o próprio save. O mod **nunca** escreve no save de outro jogador.
4. **Backup antes de testar.** O plugin já copia a pasta de save a cada início; não remova isso.
5. **Todo patch Harmony é documentado** com a classe e o método alvo (comentário no patch) e entra em
   `src/HellClockCoop/PatchTargets.cs`, que é validado ao iniciar. Uma atualização do jogo pode quebrá-los.
6. **Uma fase só avança depois de confirmada rodando no jogo.**
7. **Português do Brasil** em código, comentários, mensagens, documentação e commits.

## Antes de abrir um PR

- [ ] `scripts/build.sh` compila sem erros contra as DLLs do jogo.
- [ ] `tools/refcheck` mostra **0 ausentes**.
- [ ] `tools/codectest` passa (e ganhou testes para mensagens novas).
- [ ] Mudou o formato de alguma mensagem? Suba `Protocolo.Versao`.
- [ ] Patch novo? Está em `PatchTargets.cs` e documentado em `docs/ARQUITETURA.md`.
- [ ] Atualizou `docs/changelog.json` e/ou `docs/roadmap.json` e rodou `python3 scripts/gerar-progresso.py`.
- [ ] Testou no jogo (modo eco e, se mexeu em rede, com dois PCs) e descreveu o teste no PR.

## Relatar um problema

Use o modelo de issue **"Problema"** e anexe o `BepInEx/LogOutput.log` de quem teve o problema, copiado logo depois
do erro. Diga as versões do mod dos dois jogadores e se cada um tem a DLC Cursed War.

## Ambiente

Veja [`docs/DESENVOLVIMENTO.md`](docs/DESENVOLVIMENTO.md).
