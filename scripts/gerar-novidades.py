#!/usr/bin/env python3
"""
Gera src/HellClockCoop/Interface/NovidadesTexto.gerado.cs a partir de docs/changelog.json (fonte única das novidades).

O texto vai embutido na DLL: o mod não lê JSON nem usa rede para isso (o mscorlib do jogo é stripped).
Entram a versão mais recente e as 3 anteriores (v, data, título, testado, itens e o campo opcional "testar").
O arquivo gerado vai para o git (como CHANGELOG.md e docs/index.html), mas não se edita à mão: scripts/build.ps1 e
build.sh rodam este script antes de compilar, então a DLL sempre sai com o changelog.json atual.

Uso: python scripts/gerar-novidades.py
"""
import json
import os
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ORIGEM = os.path.join(RAIZ, "docs", "changelog.json")
DESTINO = os.path.join(RAIZ, "src", "HellClockCoop", "Interface", "NovidadesTexto.gerado.cs")
QUANTAS = 4  # a atual + 3 anteriores


def literal(texto, onde):
    """String C# comum (não verbatim), com escape de barra, aspas e quebras de linha (inclusive as Unicode que o csc
    também trata como fim de linha: U+0085, U+2028, U+2029)."""
    if not isinstance(texto, str):
        raise ValueError(f"{onde}: esperava texto, veio {type(texto).__name__}: {texto!r}")
    s = texto.replace("\\", "\\\\").replace('"', '\\"').replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t")
    s = s.replace("\u0085", "\\u0085").replace("\u2028", "\\u2028").replace("\u2029", "\\u2029")
    return '"' + s + '"'


def lista(itens, campo, versao):
    if itens is None:
        return "new string[0]"
    if not isinstance(itens, list):
        raise ValueError(f"versão {versao}: '{campo}' precisa ser uma lista")
    return "new[] { " + ", ".join(literal(i, f"versão {versao}, '{campo}'") for i in itens) + " }" if itens else "new string[0]"


def gerar(versoes):
    linhas = [
        "// <auto-gerado> por scripts/gerar-novidades.py a partir de docs/changelog.json. Não editar: mude o JSON.",
        "namespace HellClockCoop.Interface",
        "{",
        "    internal static class NovidadesTexto",
        "    {",
        "        internal static readonly VersaoNovidade[] Versoes =",
        "        {",
    ]
    for v in versoes[:QUANTAS]:
        for campo in ("v", "data", "titulo", "itens"):
            if campo not in v:
                raise ValueError(f"versão {v.get('v', '?')}: falta o campo '{campo}'")
        linhas.append(
            "            new VersaoNovidade({}, {}, {}, {}, {}, {}),".format(
                literal(v["v"], "campo 'v'"), literal(v["data"], f"versão {v['v']}, 'data'"),
                literal(v["titulo"], f"versão {v['v']}, 'titulo'"),
                "true" if v.get("testado") else "false",
                lista(v["itens"], "itens", v["v"]), lista(v.get("testar"), "testar", v["v"])))
    linhas += ["        };", "    }", "}", ""]
    return "\n".join(linhas)


def main():
    with open(ORIGEM, encoding="utf-8") as f:
        versoes = json.load(f)["versoes"]
    if not versoes:
        raise ValueError("docs/changelog.json sem versões")
    os.makedirs(os.path.dirname(DESTINO), exist_ok=True)
    conteudo = gerar(versoes)
    # newline="\n": o mesmo arquivo no Windows e no Linux (build determinística)
    with open(DESTINO, "w", encoding="utf-8", newline="\n") as f:
        f.write(conteudo)
    print(f"OK: {os.path.relpath(DESTINO, RAIZ)} ({min(len(versoes), QUANTAS)} versões, a mais recente {versoes[0]['v']})")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as e:
        print(f"ERRO ao gerar as novidades: {e}", file=sys.stderr)
        sys.exit(1)
