#!/usr/bin/env python3
"""
Gera, a partir de docs/roadmap.json e docs/changelog.json:
  - assets/progresso.svg  (painel de progresso do README)
  - ROADMAP.md
  - CHANGELOG.md
  - docs/index.html       (site do projeto, a partir de docs/site-template.html)

Uso: python3 scripts/gerar-progresso.py
Pesos: feito = 1, testar = 0,75 (pronto, falta confirmar no jogo), fazendo = 0,5, planejado = 0.
"""
import json, os, re
from xml.sax.saxutils import escape

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PESO = {"feito": 1.0, "testar": 0.75, "fazendo": 0.5, "planejado": 0.0}
ICONE = {"feito": "✅", "testar": "🧪", "fazendo": "🔨", "planejado": "⬜"}
ROTULO = {"feito": "feito", "testar": "pronto, falta testar no jogo", "fazendo": "em andamento", "planejado": "planejado"}


def progresso(fase):
    itens = fase["itens"]
    return sum(PESO[s] for s, _ in itens) / len(itens) if itens else 0.0


def svg(dados):
    fases = dados["fases"]
    total = sum(progresso(f) * len(f["itens"]) for f in fases) / sum(len(f["itens"]) for f in fases)
    largura, linha, topo = 880, 46, 118
    altura = topo + linha * len(fases) + 34
    barra_x, barra_w = 300, 470
    p = []
    p.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{largura}" height="{altura}" viewBox="0 0 {largura} {altura}" '
             'font-family="Georgia, \'Times New Roman\', serif">')
    p.append('<defs>'
             '<linearGradient id="fundo" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#1d1210"/><stop offset="1" stop-color="#120a08"/></linearGradient>'
             '<linearGradient id="fogo" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#8a2418"/><stop offset="1" stop-color="#e0763a"/></linearGradient>'
             '<linearGradient id="teste" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#b8862e"/><stop offset="1" stop-color="#f2b04a"/></linearGradient>'
             '</defs>')
    p.append(f'<rect width="{largura}" height="{altura}" rx="16" fill="url(#fundo)"/>')
    p.append(f'<rect x="1" y="1" width="{largura-2}" height="{altura-2}" rx="15" fill="none" stroke="#4a2f25"/>')
    p.append('<text x="32" y="46" fill="#eadbc8" font-size="22" font-weight="700">Progresso do projeto</text>')
    p.append(f'<text x="32" y="72" fill="#a8927c" font-size="13">versão {escape(dados["versao"])} · atualizado em {escape(dados["atualizado"])}</text>')
    # total
    p.append(f'<text x="{largura-32}" y="52" fill="#f2b04a" font-size="34" font-weight="800" text-anchor="end">{round(total*100)}%</text>')
    p.append(f'<text x="{largura-32}" y="72" fill="#a8927c" font-size="12" text-anchor="end">geral</text>')
    p.append(f'<rect x="32" y="88" width="{largura-64}" height="6" rx="3" fill="#0c0706"/>')
    p.append(f'<rect x="32" y="88" width="{(largura-64)*total:.1f}" height="6" rx="3" fill="url(#fogo)"/>')
    for i, f in enumerate(fases):
        y = topo + i * linha
        feitos = sum(1 for s, _ in f["itens"] if s == "feito")
        testar = sum(1 for s, _ in f["itens"] if s == "testar")
        pf = feitos / len(f["itens"])
        pt = testar * PESO["testar"] / len(f["itens"])
        prog = progresso(f)
        cor = "#5fe08a" if prog >= 0.999 else "#eadbc8"
        rotulo = ("FASE " + f["id"]) if f["id"].isdigit() else "TRILHA"
        p.append(f'<text x="32" y="{y+20}" fill="#e0763a" font-size="13" font-weight="700">{escape(rotulo)}</text>')
        p.append(f'<text x="100" y="{y+20}" fill="{cor}" font-size="15" font-weight="600">{escape(f["nome"])}</text>')
        p.append(f'<rect x="{barra_x}" y="{y+8}" width="{barra_w}" height="16" rx="8" fill="#0c0706"/>')
        if pf > 0:
            p.append(f'<rect x="{barra_x}" y="{y+8}" width="{barra_w*pf:.1f}" height="16" rx="8" fill="url(#fogo)"/>')
        if pt > 0:
            p.append(f'<rect x="{barra_x + barra_w*pf:.1f}" y="{y+8}" width="{barra_w*pt:.1f}" height="16" rx="0" fill="url(#teste)" opacity="0.9"/>')
        p.append(f'<text x="{largura-32}" y="{y+21}" fill="#eadbc8" font-size="14" font-weight="700" text-anchor="end">{round(prog*100)}%</text>')
    yl = altura - 18
    p.append(f'<rect x="32" y="{yl-10}" width="12" height="12" rx="3" fill="url(#fogo)"/><text x="50" y="{yl}" fill="#a8927c" font-size="12">feito</text>')
    p.append(f'<rect x="100" y="{yl-10}" width="12" height="12" rx="3" fill="url(#teste)"/><text x="118" y="{yl}" fill="#a8927c" font-size="12">pronto, falta testar no jogo</text>')
    p.append(f'<rect x="300" y="{yl-10}" width="12" height="12" rx="3" fill="#0c0706" stroke="#4a2f25"/><text x="318" y="{yl}" fill="#a8927c" font-size="12">planejado</text>')
    p.append('</svg>')
    return "\n".join(p)


def roadmap_md(dados):
    l = ["# Roadmap — Hell Clock Coop", "",
         f"> Atualizado em {dados['atualizado']} · versão {dados['versao']}. Gerado a partir de [`docs/roadmap.json`](docs/roadmap.json) "
         "por `scripts/gerar-progresso.py`: edite o JSON, não este arquivo.", "",
         "<p align=\"center\"><img src=\"assets/progresso.svg\" alt=\"Progresso por fase\" width=\"880\"></p>", "",
         "**Legenda:** " + " · ".join(f"{ICONE[k]} {ROTULO[k]}" for k in ("feito", "testar", "fazendo", "planejado")), "",
         "Regra do projeto: uma fase só é dada como concluída depois de confirmada **rodando no jogo**.", ""]
    for f in dados["fases"]:
        prog = round(progresso(f) * 100)
        titulo = f"Fase {f['id']}" if f['id'].isdigit() else "Trilha permanente"
        l.append(f"## {titulo} — {f['nome']} · {prog}%")
        l.append("")
        l.append(f"_{f['resumo']}_")
        l.append("")
        l.append("| | Item |")
        l.append("|:-:|---|")
        for s, texto in f["itens"]:
            l.append(f"| {ICONE[s]} | {texto} |")
        l.append("")
    return "\n".join(l)


def changelog_md(cl):
    l = ["# Novidades — Hell Clock Coop", "",
         "> Gerado a partir de [`docs/changelog.json`](docs/changelog.json) por `scripts/gerar-progresso.py`.", "",
         "Legenda: ✅ testada no jogo · 🧪 aguardando teste no jogo.", ""]
    for v in cl["versoes"]:
        l.append(f"## {v['v']} — {v['titulo']} {'✅' if v['testado'] else '🧪'}")
        l.append(f"_{v['data']}_")
        l.append("")
        for i in v["itens"]:
            l.append(f"- {i}")
        l.append("")
    return "\n".join(l)


def site(dados, cl):
    with open(os.path.join(RAIZ, "docs", "site-template.html"), encoding="utf-8") as f:
        modelo = f.read()
    alvos = open(os.path.join(RAIZ, "src", "HellClockCoop", "PatchTargets.cs"), encoding="utf-8").read()
    proto = open(os.path.join(RAIZ, "src", "HellClockCoop", "Net", "Protocolo.cs"), encoding="utf-8").read()
    pacote = {
        "roadmap": dados,
        "changelog": cl["versoes"],
        "patches": len(re.findall(r"new Alvo\(", alvos)),
        "mensagens": len(re.findall(r"public const byte Tipo\w+ = ", proto)),
        "versoes": len(cl["versoes"]),
    }
    js = json.dumps(pacote, ensure_ascii=False).replace("</", "<\\/")
    return (modelo.replace("{{DADOS}}", js).replace("{{VERSAO}}", dados["versao"]).replace("{{ATUALIZADO}}", dados["atualizado"]))


def main():
    with open(os.path.join(RAIZ, "docs", "roadmap.json"), encoding="utf-8") as f:
        dados = json.load(f)
    os.makedirs(os.path.join(RAIZ, "assets"), exist_ok=True)
    with open(os.path.join(RAIZ, "assets", "progresso.svg"), "w", encoding="utf-8") as f:
        f.write(svg(dados))
    with open(os.path.join(RAIZ, "ROADMAP.md"), "w", encoding="utf-8") as f:
        f.write(roadmap_md(dados))
    with open(os.path.join(RAIZ, "docs", "changelog.json"), encoding="utf-8") as f:
        cl = json.load(f)
    with open(os.path.join(RAIZ, "CHANGELOG.md"), "w", encoding="utf-8") as f:
        f.write(changelog_md(cl))
    with open(os.path.join(RAIZ, "docs", "index.html"), "w", encoding="utf-8") as f:
        f.write(site(dados, cl))
    total = sum(progresso(x) * len(x["itens"]) for x in dados["fases"]) / sum(len(x["itens"]) for x in dados["fases"])
    print(f"OK: assets/progresso.svg, ROADMAP.md, CHANGELOG.md e docs/index.html ({round(total*100)}% geral)")


if __name__ == "__main__":
    main()
