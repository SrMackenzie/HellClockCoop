#!/usr/bin/env bash
# Reproduz os números da seção 1 do docs/ARQUITETURA.md.
# Uso: scripts/contar-singletons.sh <pasta com o HellClock.dll decompilado>
set -euo pipefail
SRC="$1"

echo "Arquivos que acessam o jogador por estática:"
grep -rlE "PlayerController\.Instance|PlayerManager>\.Instance|Singleton<PlayerController>|Guaranteed<PlayerController>" --include=*.cs "$SRC" | wc -l

echo "  ...via PlayerController.Instance:"
grep -rl "PlayerController\.Instance" --include=*.cs "$SRC" | wc -l

echo "  ...via ClassSingleton<PlayerManager>.Instance:"
grep -rl "Singleton<PlayerManager>\.Instance" --include=*.cs "$SRC" | wc -l

echo "Chamadas ao UnityEngine.Random global:"
grep -rhoE "(UnityEngine\.)?Random\.(Range|value|insideUnitCircle|insideUnitSphere|onUnitSphere|rotation)" --include=*.cs "$SRC" | wc -l

echo "Quem registra alvos para os inimigos:"
grep -rn "AddToAvailableTargets(" --include=*.cs "$SRC" | grep -v "public void"
