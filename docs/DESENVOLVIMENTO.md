# Desenvolvimento — Hell Clock Coop

Guia do ambiente. A arquitetura (patches, mensagens, decisões) está em [`ARQUITETURA.md`](ARQUITETURA.md).

## Requisitos
- Hell Clock 2.1.1.1 (Steam), Unity 6000.0.60f1, scripting backend Mono.
- .NET SDK 10 (o compilador e as ferramentas de `tools/`, que são net10.0; sem NuGet).
- Python 3 (build, empacotamento e geração do progresso: o build roda `scripts/gerar-novidades.py`).

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
No Windows (PowerShell), com o jogo no caminho padrão da Steam:
```powershell
scripts\build.ps1            # só compila
scripts\build.ps1 -Instalar  # compila e copia para <jogo>\BepInEx\plugins\HellClockCoop e <jogo>\BepInEx\patchers
```
Saem dois arquivos: `build/HellClockCoop.dll` (o plugin, contra as DLLs do jogo) e `build/HellClockCoop.Atualizador.dll`
(o atualizador, preloader patcher, contra `BepInEx\unstripped_corlib`, `BepInEx.dll` e `Mono.Cecil.dll`; ver
ARQUITETURA, seção 13). Com builds locais instaladas, use `[Atualizacao] VerificarAoIniciar = false`: com a mesma versão
da última release e o atualizador diferente dela, ele troca a build local pela publicada.
Em bash (Linux, Git Bash):
```bash
scripts/build.sh "<jogo>/Hell Clock_Data/Managed" "<BepInEx>/BepInEx/core"
# saída: build/HellClockCoop.dll  →  <jogo>/BepInEx/plugins/HellClockCoop/
```
Antes do csc, os dois scripts rodam `scripts/gerar-novidades.py`, que gera
`src/HellClockCoop/Interface/NovidadesTexto.gerado.cs` (texto da janela Novidades) a partir do `docs/changelog.json`;
o arquivo vai para o git, mas não se edita à mão.
O build compila com `-nostdlib` contra as DLLs **do próprio jogo**. Assim o compilador acusa na hora qualquer API
removida pelo stripping (já apareceram: `LODGroup`, `AnimatorControllerParameter.type`, `InvalidDataException`, o setter
de `GUIStyle.padding`, `GUI.Box(Rect,string,GUIStyle)`, `int.MinValue`, `float.MaxValue`, `Exception.GetBaseException`).

## Envio de métricas (opcional)
O destino fica em `src/HellClockCoop/Balanceamento/EnvioConfig.local.cs`, fora do git. Sem esse arquivo o mod compila
e o envio fica desligado. Formato: um `partial` de `EnvioConfig` que preenche `projeto` e `chave` em `Carregar`.

## Testes
```bash
# ida e volta de todas as mensagens de rede
dotnet run --project tools/codectest -c Release -p:HellClockManaged="<jogo>/Hell Clock_Data/Managed" -- build/HellClockCoop.dll
# MemberRefs do plugin que não existem no mscorlib/System/System.Core do jogo (precisa dar 0)
dotnet run --project tools/refcheck -c Release -- "<jogo>/Hell Clock_Data/Managed" build/HellClockCoop.dll
# o mesmo para o atualizador, contra o corlib completo que ele usa no preloader (precisa dar 0)
dotnet run --project tools/refcheck -c Release -- "<jogo>/BepInEx/unstripped_corlib" build/HellClockCoop.Atualizador.dll
# atualizador: assinatura, validação do manifesto e troca/volta dos arquivos
dotnet run --project tools/atualizadortest -c Release
```

## Empacotar
```bash
python3 scripts/empacotar.py --bepinex BepInEx_win_x64_5.4.23.6.zip --corlib <swyf-modding/Setup>/vendor/corlib --plugin build/HellClockCoop.dll
# saída: dist/HellClockCoop-v<versão>.zip — o jogador extrai dentro da pasta do jogo
#        dist/release-v<versão>/ — anexos da release: zip, manifesto.txt + .sig e os arquivos avulsos do atualizador
```
O LEIA-ME e as licenças que vão no zip estão em `dist-template/`.

## Publicar uma versão (atualizador)
1. Uma vez por máquina: `dotnet run --project tools/assinar -c Release -- gerar-chave` cria a chave privada em
   `%USERPROFILE%\.hellclockcoop\atualizador-privada.pem` (fora do repositório) e mostra a pública, que vai em
   `src/HellClockCoop.Atualizador/ChavePublica.cs`. A chave atual já existe: **não gere outra**; guarde uma cópia da
   privada fora do computador. Sem ela, nenhuma versão nova chega pelo atualizador. Se ela estiver sem senha,
   `... -- proteger` a cifra (a pública não muda); daí em diante a senha é pedida ao assinar (ou vem de
   `HELLCLOCKCOOP_SENHA_CHAVE`).
2. `Atualizador.Versao` igual ao `PluginVersion`, no formato `x.y.z` (o empacotar confere).
3. `scripts/empacotar.py ...` (assina o manifesto) e depois
   `gh release create v<versão> dist/release-v<versão>/* --title "Hell Clock Coop <versão>" --notes-file <notas>`.
   O atualizador lê `releases/latest`: rascunho e pré-release não contam.

## Progresso, roadmap, novidades e site
`docs/roadmap.json` e `docs/changelog.json` são a fonte única. Depois de editar:
```bash
python3 scripts/gerar-progresso.py   # assets/progresso.svg, ROADMAP.md, CHANGELOG.md, docs/index.html
```
Cada versão do `changelog.json` pode ter `testar` (3 a 5 itens curtos do roteiro de `docs/TESTES.md`): a janela
Novidades do jogo mostra esses itens em "O que testar" enquanto `testado` for `false`. A janela leva a versão atual e
as 3 anteriores, embutidas na DLL no build.

## Testar no jogo
- **Console:** desde a 0.8.2 vem desligado. Para acompanhar ao vivo, `[Diagnostico] MostrarConsole = true` no .cfg do
  mod e reabra o jogo (o console abre antes do plugin). O `LogOutput.log` é gravado nos dois casos.
- **Fase 0:** ao abrir o jogo, o log mostra `Hell Clock Coop <versão> carregado`, `Backup do save: N arquivo(s)`,
  `Versão do jogo: 2.1.1.1` e `[Validação] N/N alvos de patch encontrados`.
- **Modo eco (F8):** um clone aparece 2,5 m ao lado e repete tudo com 1 s de atraso, inclusive os efeitos das skills e
  os números de dano. Serve para testar sem um segundo PC. Fica ligado ao trocar de andar, entrar numa run ou voltar
  ao menu (o fantasma volta junto com o personagem) até ser desligado.
- **Coop:** dois PCs com contas Steam diferentes. O log fica em `Hell Clock/BepInEx/LogOutput.log`; copie-o logo
  depois do problema (o jogo sobrescreve ao abrir).
- **Diagnóstico (F9):** salva a hierarquia do personagem em `BepInEx/HellClockCoop/hierarquia-jogador.txt`.

## Estudo do código do jogo
Para entender os sistemas do jogo, o código foi decompilado **localmente** com o ICSharpCode.Decompiler. Nada
decompilado entra no repositório (`.gitignore` bloqueia `decompilado/`, `decompiled/` e qualquer `.dll`).
