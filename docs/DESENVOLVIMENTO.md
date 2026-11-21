# Desenvolvimento — Hell Clock Coop

Guia do ambiente. A arquitetura (patches, mensagens, decisões) está em [`ARQUITETURA.md`](ARQUITETURA.md).

## Requisitos
- Hell Clock 2.1.1.1 (Steam), Unity 6000.0.60f1, scripting backend Mono.
- .NET SDK 8 ou superior (só o compilador; sem NuGet).
- Python 3 (empacotamento e geração do progresso).

## BepInEx no Hell Clock (passo obrigatório)
O build do jogo usa *managed stripping*: o `mscorlib.dll` embutido não tem `Module.GetPEKind`,
`MethodBody.GetILAsByteArray`, `RuntimeMethodHandle.GetFunctionPointer` e outros métodos de que o BepInEx, o Harmony e
o MonoMod precisam. O BepInEx 5 quebra no preloader (`MissingMethodException`). A correção é usar bibliotecas base
completas **de Windows**:

1. Extrair o BepInEx 5.4.23.6 (win x64) na pasta do jogo.
2. Copiar as 8 DLLs de `vendor/corlib` do repositório [swyf-modding/Setup](https://github.com/swyf-modding/Setup)
   (Unity 6000.3.10f1, Windows x64, licença MIT do Mono) para `BepInEx\unstripped_corlib\`.
3. Em `doorstop_config.ini`, seção `[UnityMono]`: `dll_search_path_override = BepInEx\unstripped_corlib`.

**Não use** `unity.bepinex.dev/corlibs/6000.0.60.zip`: é a variante Unix (`Interop.Sys`/`System.Native`). No Windows,
`File.Exists` falha, as Addressables não carregam e o jogo trava no "Carregando".

O `scripts/empacotar.py` já monta o zip com tudo isso.

## Compilar
```bash
scripts/build.sh "<jogo>/Hell Clock_Data/Managed" "<BepInEx>/BepInEx/core"
# saída: build/HellClockCoop.dll  →  <jogo>/BepInEx/plugins/HellClockCoop/
```
O build compila com `-nostdlib` contra as DLLs **do próprio jogo**. Assim o compilador acusa na hora qualquer API
removida pelo stripping (já apareceram: `LODGroup`, `AnimatorControllerParameter.type`, `InvalidDataException`, o setter
de `GUIStyle.padding`, `GUI.Box(Rect,string,GUIStyle)`, `int.MinValue`, `float.MaxValue`, `Exception.GetBaseException`).

## Testes
```bash
# ida e volta de todas as mensagens de rede
dotnet run --project tools/codectest -c Release -p:HellClockManaged="<jogo>/Hell Clock_Data/Managed"
# MemberRefs do plugin que não existem no mscorlib/System/System.Core do jogo (precisa dar 0)
dotnet run --project tools/refcheck -c Release -- "<jogo>/Hell Clock_Data/Managed" build/HellClockCoop.dll
```

## Empacotar
```bash
python3 scripts/empacotar.py --bepinex BepInEx_win_x64_5.4.23.6.zip --corlib <swyf-modding/Setup>/vendor/corlib --plugin build/HellClockCoop.dll
# saída: dist/HellClockCoop-v<versão>.zip — o jogador extrai dentro da pasta do jogo
```
O LEIA-ME e as licenças que vão no zip estão em `dist-template/`.

## Progresso, roadmap, novidades e site
`docs/roadmap.json` e `docs/changelog.json` são a fonte única. Depois de editar:
```bash
python3 scripts/gerar-progresso.py   # assets/progresso.svg, ROADMAP.md, CHANGELOG.md, docs/index.html
```

## Testar no jogo
- **Fase 0:** ao abrir o jogo, o console mostra `Hell Clock Coop <versão> carregado`, `Backup do save: N arquivo(s)`,
  `Versão do jogo: 2.1.1.1` e `[Validação] N/N alvos de patch encontrados`.
- **Modo eco (F8):** um clone aparece 2,5 m ao lado e repete tudo com 1 s de atraso, inclusive os efeitos das skills e
  os números de dano. Serve para testar sem um segundo PC.
- **Coop:** dois PCs com contas Steam diferentes. O log fica em `Hell Clock/BepInEx/LogOutput.log`; copie-o logo
  depois do problema (o jogo sobrescreve ao abrir).
- **Diagnóstico (F9):** salva a hierarquia do personagem em `BepInEx/HellClockCoop/hierarquia-jogador.txt`.

## Estudo do código do jogo
Para entender os sistemas do jogo, o código foi decompilado **localmente** com o ICSharpCode.Decompiler. Nada
decompilado entra no repositório (`.gitignore` bloqueia `decompilado/`, `decompiled/` e qualquer `.dll`).
