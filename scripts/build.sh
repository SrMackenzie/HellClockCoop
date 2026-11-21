#!/usr/bin/env bash
# Compila HellClockCoop.dll com o csc do .NET SDK, referenciando direto as DLLs do jogo
# (pasta Managed) e do BepInEx (pasta BepInEx/core), e o atualizador (HellClockCoop.Atualizador.dll). Não precisa de NuGet.
# Uso: scripts/build.sh <pasta Managed do jogo> <pasta BepInEx/core>
# (o atualizador usa <pasta BepInEx/core>/../unstripped_corlib, que precisa existir: é a do BepInEx instalado no jogo)
set -euo pipefail
MANAGED="$1"; CORE="$2"
CSC=$(find "$(dirname "$(readlink -f "$(command -v dotnet)")")/sdk" -name csc.dll | head -1)
cd "$(dirname "$0")/.."
mkdir -p build
# Texto da janela Novidades: gerado do docs/changelog.json antes de compilar (fica embutido na DLL).
PY=$(command -v python3 || command -v python || true)
[ -n "$PY" ] || { echo "ERRO: Python 3 não encontrado (o build roda scripts/gerar-novidades.py)." >&2; exit 1; }
"$PY" scripts/gerar-novidades.py
REFS=()
for f in "$MANAGED"/*.dll; do REFS+=("-r:$f"); done
dotnet "$CSC" -nologo -noconfig -nostdlib+ -target:library -langversion:9 -optimize+ -deterministic \
  -nowarn:1701,1702 -out:build/HellClockCoop.dll -recurse:"src/HellClockCoop/*.cs" "${REFS[@]}" \
  "-r:$CORE/BepInEx.dll" "-r:$CORE/0Harmony.dll"
echo "OK: build/HellClockCoop.dll"
# Atualizador (preloader patcher): roda antes da Unity, contra o corlib completo do BepInEx (BepInEx/unstripped_corlib).
CORLIB="$CORE/../unstripped_corlib"
dotnet "$CSC" -nologo -noconfig -nostdlib+ -target:library -langversion:9 -optimize+ -deterministic \
  -nowarn:1701,1702 -out:build/HellClockCoop.Atualizador.dll -recurse:"src/HellClockCoop.Atualizador/*.cs" \
  "-r:$CORLIB/mscorlib.dll" "-r:$CORLIB/System.dll" "-r:$CORLIB/System.Core.dll" \
  "-r:$CORE/BepInEx.dll" "-r:$CORE/Mono.Cecil.dll"
echo "OK: build/HellClockCoop.Atualizador.dll"
