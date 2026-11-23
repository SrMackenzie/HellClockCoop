#!/usr/bin/env python3
"""
Painel de balanceamento (ferramenta do autor): lê os resumos de run (metricas/run-*.json do mod e, com --firestore,
a coleção runs do projeto de métricas) e gera uma página HTML com solo × 2, 3 e 4 jogadores, andar por andar,
separada por dungeon, tier e Ascensão.

O painel só mostra os números: não muda nenhum valor do mod. O ajuste do balanceamento é decisão do autor e fica
fixo no código.

Uso:
  python scripts/painel-balanceamento.py [arquivo-ou-pasta ...] [--firestore] [--saida build/painel-balanceamento.html]

Sem arquivo nem pasta, lê a pasta de métricas do jogo instalado (Steam padrão). --firestore usa o login do gcloud
(`gcloud auth print-access-token`, conta dona do projeto) e só lê a coleção runs. O token nunca é gravado nem
impresso. A página gerada fica em build/ (fora do git): não tem o id da instalação, mas tem os números das runs.
"""
import argparse, html, json, os, re, shutil, subprocess, sys, urllib.parse, urllib.request
from collections import defaultdict

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PASTA_PADRAO = r"C:\Program Files (x86)\Steam\steamapps\common\Hell Clock\BepInEx\HellClockCoop\metricas"
PROJETO = "hellclockcoop-metricas"
NOME_RESUMO = re.compile(r"^run-(\d{8}-\d{6})\.json$")
ID_RUN = re.compile(r"^[0-9a-f]{32}-\d{8}-\d{6}$")

# Faixa em que o grupo fica "um pouco mais difícil que o solo" (diferença relativa ao solo, no sentido da dificuldade).
ALVO_MIN, ALVO_MAX = 0.0, 0.25


# ---------------------------------------------------------------- leitura

def valor_firestore(v):
    """Valor tipado da API REST do Firestore → valor Python."""
    if "integerValue" in v: return int(v["integerValue"])
    if "doubleValue" in v: return float(v["doubleValue"])
    if "booleanValue" in v: return bool(v["booleanValue"])
    if "stringValue" in v: return v["stringValue"]
    if "nullValue" in v: return None
    if "arrayValue" in v: return [valor_firestore(x) for x in v["arrayValue"].get("values", [])]
    if "mapValue" in v: return {k: valor_firestore(x) for k, x in v["mapValue"].get("fields", {}).items()}
    return None


def doc_firestore(d):
    """Documento da API REST ({"name", "fields"}) ou corpo de pendente ({"fields"}) → (id, resumo)."""
    resumo = {k: valor_firestore(x) for k, x in d.get("fields", {}).items()}
    nome = d.get("name", "")
    return (nome.rsplit("/", 1)[-1] if nome else None), resumo


def ler_arquivo(caminho):
    """Devolve [(id, resumo)] de um run-*.json local, de um pendente tipado ou de uma listagem salva da API."""
    with open(caminho, encoding="utf-8") as f:
        dados = json.load(f)
    nome = os.path.basename(caminho)
    if isinstance(dados, dict) and "documents" in dados:
        return [doc_firestore(d) for d in dados["documents"]]
    if isinstance(dados, dict) and "fields" in dados:
        i, r = doc_firestore(dados)
        return [(i or os.path.splitext(nome)[0], r)]
    m = NOME_RESUMO.match(nome)
    inst = dados.get("instalacao") if isinstance(dados, dict) else None
    if m and isinstance(inst, str):
        return [(f"{inst}-{m.group(1)}", dados)]
    return [(os.path.splitext(nome)[0], dados)]


def arquivos(alvos):
    for alvo in alvos:
        if os.path.isdir(alvo):
            for raiz, _, nomes in os.walk(alvo):
                if os.path.basename(raiz) == "recusadas":
                    continue  # recusados pelo banco: formato inválido
                for n in sorted(nomes):
                    if n.endswith(".json"):
                        yield os.path.join(raiz, n)
        elif os.path.isfile(alvo):
            yield alvo
        else:
            print(f"aviso: {alvo} não existe, ignorado", file=sys.stderr)


def token_gcloud():
    exe = shutil.which("gcloud")
    if not exe:
        sys.exit("erro: gcloud não encontrado no PATH (instale o Google Cloud SDK e rode gcloud auth login)")
    r = subprocess.run([exe, "auth", "print-access-token"], capture_output=True, text=True)
    if r.returncode != 0 or not r.stdout.strip():
        sys.exit("erro: o gcloud não devolveu token; rode gcloud auth login com a conta dona do projeto")
    return r.stdout.strip()


def ler_firestore(projeto):
    """Lista a coleção runs (só leitura), página por página."""
    token = token_gcloud()
    base = f"https://firestore.googleapis.com/v1/projects/{projeto}/databases/(default)/documents/runs"
    saida, pagina = [], None
    while True:
        q = {"pageSize": "300"}
        if pagina:
            q["pageToken"] = pagina
        req = urllib.request.Request(base + "?" + urllib.parse.urlencode(q), headers={
            "Authorization": "Bearer " + token, "x-goog-user-project": projeto})
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                dados = json.load(resp)
        except urllib.error.HTTPError as e:
            sys.exit(f"erro: o Firestore respondeu {e.code} ao listar runs do projeto {projeto}")
        except urllib.error.URLError as e:
            sys.exit(f"erro: sem acesso ao Firestore ({e.reason})")
        saida += [doc_firestore(d) for d in dados.get("documents", [])]
        pagina = dados.get("nextPageToken")
        if not pagina:
            return saida


def valido(r):
    return (isinstance(r, dict) and r.get("formato") == 1 and isinstance(r.get("andares"), list)
            and isinstance(r.get("jogadores"), int) and 1 <= r["jogadores"] <= 4)


# ---------------------------------------------------------------- agregação

def num(a, campo):
    v = a.get(campo)
    return float(v) if isinstance(v, (int, float)) and not isinstance(v, bool) else None


def media(xs):
    xs = [x for x in xs if x is not None]
    return sum(xs) / len(xs) if xs else None


def por_minuto(a, campo):
    v, s = num(a, campo), num(a, "segundos")
    return v * 60.0 / s if v is not None and s and s > 0 else None


def relogio_gasto(a):
    e, s = num(a, "relogioEntrada"), num(a, "relogioSaida")
    return e - s if e is not None and s is not None else None


# (rótulo, função por andar, casas, sentido: +1 = maior é mais difícil, -1 = menor é mais difícil, 0 = neutro,
#  só coop: a linha só aparece com runs de grupo)
METRICAS = [
    ("segundos no andar", lambda a: num(a, "segundos"), 1, +1, False),
    ("inimigos mortos/min", lambda a: por_minuto(a, "mortos"), 1, 0, False),
    ("dano causado/min", lambda a: por_minuto(a, "danoCausado"), 0, -1, False),
    ("dano recebido/min", lambda a: por_minuto(a, "danoRecebido"), 0, +1, False),
    ("Hell Clock gasto (s)", relogio_gasto, 1, 0, False),
    ("mortes", lambda a: num(a, "mortes"), 2, +1, False),
    ("quedas", lambda a: num(a, "quedas"), 2, +1, True),
    ("reanimações feitas", lambda a: num(a, "reanimacoes"), 2, 0, True),
    ("vezes reanimado", lambda a: num(a, "reanimado"), 2, 0, True),
]


def tabela_grupo(runs):
    """{jogadores: {"total": n, "andares": {indice: [andar, ...]}}}"""
    t = {}
    for r in runs:
        col = t.setdefault(r["jogadores"], {"total": 0, "andares": defaultdict(list)})
        col["total"] += 1
        for a in r["andares"]:
            if isinstance(a, dict) and isinstance(a.get("indice"), int):
                col["andares"][a["indice"]].append(a)
    return dict(sorted(t.items()))


# ---------------------------------------------------------------- HTML

def fmt(v, casas):
    if v is None:
        return "—"
    s = f"{v:,.{casas}f}"
    return s.replace(",", "\x00").replace(".", ",").replace("\x00", ".").replace("-", "−")


def classe_delta(d, sentido):
    """d = (grupo − solo) / solo. Ajusta pelo sentido para que positivo = grupo mais difícil."""
    if d is None or sentido == 0:
        return "neutro"
    dif = d * sentido
    if dif < ALVO_MIN:
        return "facil"
    if dif > ALVO_MAX:
        return "dificil"
    return "alvo"


def celula_delta(base, v, sentido):
    if base is None or v is None or base == 0:
        return '<td class="d neutro">—</td>'
    d = (v - base) / abs(base)
    mostrado = d * sentido if sentido else d  # com sentido, positivo = grupo mais difícil; neutro, a diferença bruta
    return f'<td class="d {classe_delta(d, sentido)}">{"+" if mostrado >= 0 else "−"}{abs(mostrado) * 100:.0f}%</td>'


NOMES_COL = {1: "solo", 2: "dupla", 3: "3 jog.", 4: "4 jog."}


def secao(chave, runs):
    nome, tier, asc = chave
    t = tabela_grupo(runs)
    cols = list(t)
    tem_solo = 1 in t
    tem_coop = any(c > 1 for c in cols)
    indices = sorted({i for c in t.values() for i in c["andares"]})
    p = [f'<section><h2>{html.escape(nome)} <span>tier {tier}{" · Ascensão" if asc else ""}</span></h2>']
    p.append('<p class="contagem">' + " · ".join(
        f'{NOMES_COL[c]}: {t[c]["total"]} run(s) de jogador' for c in cols) + "</p>")
    p.append('<div class="rolagem"><table><thead><tr><th scope="col">andar</th><th scope="col" class="metrica">métrica</th>')
    for c in cols:
        p.append(f'<th scope="col">{NOMES_COL[c]}</th>')
        if c > 1 and tem_solo:
            p.append(f'<th scope="col" class="d">Δ solo</th>')
    p.append("</tr></thead><tbody>")
    for i in indices:
        linhas = [("chegaram", None, 0, -1)] + [(m[0], m[1], m[2], m[3]) for m in METRICAS if not m[4] or tem_coop]
        for k, (rot, f, casas, sentido) in enumerate(linhas):
            p.append('<tr class="inicio">' if k == 0 else "<tr>")
            if k == 0:
                chefe = any(a.get("chefe") for c in t.values() for a in c["andares"].get(i, []))
                p.append(f'<th scope="rowgroup" rowspan="{len(linhas)}">{i + 1}{" (chefe)" if chefe else ""}</th>')
            p.append(f'<td class="metrica">{rot}</td>')
            base = None
            for c in cols:
                andares = t[c]["andares"].get(i, [])
                if f is None:
                    v = len(andares) / t[c]["total"] if t[c]["total"] else None
                    txt = f'{len(andares)}/{t[c]["total"]}'
                else:
                    v = media(f(a) for a in andares)
                    txt = fmt(v, casas)
                p.append(f"<td>{txt}</td>")
                if c == 1:
                    base = v
                elif tem_solo:
                    p.append(celula_delta(base, v, sentido))
            p.append("</tr>")
    p.append("</tbody></table></div></section>")
    return "".join(p)


def lista_runs(runs):
    p = ['<section><h2>Runs lidas</h2><div class="rolagem"><table><thead><tr>'
         '<th scope="col">data</th><th scope="col">mod</th><th scope="col">papel</th><th scope="col">jogadores</th>'
         '<th scope="col">dungeon</th><th scope="col">tier</th><th scope="col">andares</th>'
         '<th scope="col">resultado</th><th scope="col">balanceamento</th></tr></thead><tbody>']
    for i, r in sorted(runs, key=lambda x: x[0][-15:]):
        c = i[-15:]
        data = f"{c[6:8]}/{c[4:6]}/{c[0:4]} {c[9:11]}:{c[11:13]}" if re.match(r"\d{8}-\d{6}$", c) else "—"
        p.append("<tr>" + "".join(f"<td>{html.escape(str(x))}</td>" for x in (
            data, r.get("mod"), r.get("papel"), r.get("jogadores"), r.get("dungeonNome"), r.get("tier"),
            len(r["andares"]), r.get("resultado"), r.get("balanceamento"))) + "</tr>")
    p.append("</tbody></table></div></section>")
    return "".join(p)


CSS = """
:root{color-scheme:dark;--fundo:#1d1210;--painel:#261815;--texto:#f1e6dc;--fraco:#b39a8a;--linha:#3d2a24;
--fogo:#e0763a;--facil:#e2574c;--alvo:#6fbf73;--dificil:#f2b04a}
:root[data-tema=claro]{color-scheme:light;--fundo:#faf5f0;--painel:#fff;--texto:#2a1a14;--fraco:#6d5547;
--linha:#e4d6cb;--fogo:#b4521f;--facil:#b3261e;--alvo:#2e7d32;--dificil:#9a6400}
@media (prefers-color-scheme:light){:root:not([data-tema]){color-scheme:light;--fundo:#faf5f0;--painel:#fff;
--texto:#2a1a14;--fraco:#6d5547;--linha:#e4d6cb;--fogo:#b4521f;--facil:#b3261e;--alvo:#2e7d32;--dificil:#9a6400}}
*{box-sizing:border-box}body{margin:0;background:var(--fundo);color:var(--texto);font:15px/1.45 system-ui,sans-serif}
header,main{max-width:1100px;margin:0 auto;padding:16px}header h1{font-family:Georgia,serif;color:var(--fogo);margin:0 0 4px}
header p,.contagem{color:var(--fraco);margin:4px 0}.barra{display:flex;flex-wrap:wrap;gap:12px;align-items:center;margin-top:12px}
select{min-height:44px;min-width:44px;padding:0 12px;background:var(--painel);color:var(--texto);border:1px solid var(--linha);
border-radius:6px;font:inherit}.legenda span{display:inline-block;padding:2px 8px;border-radius:4px;margin-right:6px}
section{background:var(--painel);border:1px solid var(--linha);border-radius:8px;padding:12px;margin:16px 0}
h2{font-family:Georgia,serif;margin:0 0 4px;font-size:1.2rem}h2 span{color:var(--fraco);font-size:.9rem;font-weight:normal}
.rolagem{overflow-x:auto}table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}
th,td{padding:6px 10px;border-bottom:1px solid var(--linha);text-align:right;white-space:nowrap}
td.metrica,th.metrica,tbody th{text-align:left}tbody th{vertical-align:top;color:var(--fogo)}
tr.inicio>*{border-top:2px solid var(--linha)}thead th{color:var(--fraco);font-weight:600}
.d{font-weight:600}.facil{color:var(--facil)}.alvo{color:var(--alvo)}.dificil{color:var(--dificil)}.neutro{color:var(--fraco)}
"""

JS = """
const s=document.getElementById('tema'),r=document.documentElement;
function aplicar(v){if(v==='sistema')r.removeAttribute('data-tema');else r.setAttribute('data-tema',v);}
const salvo=localStorage.getItem('painel-tema')||'sistema';s.value=salvo;aplicar(salvo);
s.addEventListener('change',()=>{localStorage.setItem('painel-tema',s.value);aplicar(s.value);});
"""


def pagina(runs, origens):
    secoes = "".join(secao(k, [r for _, r in v]) for k, v in agrupar_com_id(runs).items())
    vazio = "" if runs else "<section><p>Nenhuma run válida encontrada.</p></section>"
    return f"""<!DOCTYPE html>
<html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Hell Clock Coop · painel de balanceamento</title><style>{CSS}</style></head><body>
<header><h1>Painel de balanceamento</h1>
<p>{len(runs)} run(s) de jogador · origem: {html.escape(", ".join(origens))}. Cada run é a de um jogador (no coop, host e
convidado mandam cada um a sua). Médias por andar entre as runs que chegaram a ele.</p>
<p>Δ solo: diferença do grupo para o solo, no sentido da dificuldade (positivo = grupo mais difícil; nas linhas
cinzas, sem sentido de dificuldade, é a diferença bruta). Alvo: grupo um pouco mais difícil que o solo
(0 a +{ALVO_MAX * 100:.0f}%). Só leitura: o ajuste é decidido pelo autor no código.</p>
<div class="barra"><label>Tema <select id="tema"><option value="sistema">sistema</option><option value="claro">claro</option>
<option value="escuro">escuro</option></select></label>
<p class="legenda"><span class="facil">grupo mais fácil</span><span class="alvo">no alvo</span><span class="dificil">bem mais difícil</span></p></div>
</header><main>{vazio}{secoes}{lista_runs(runs) if runs else ""}</main><script>{JS}</script></body></html>
"""


def agrupar_com_id(runs):
    grupos = defaultdict(list)
    for i, r in runs:
        grupos[(str(r.get("dungeonNome") or r.get("dungeon")), int(r.get("tier") or 0), bool(r.get("ascensao")))].append((i, r))
    return dict(sorted(grupos.items()))


# ---------------------------------------------------------------- principal

def main():
    ap = argparse.ArgumentParser(description="Painel de balanceamento a partir dos resumos de run.")
    ap.add_argument("alvos", nargs="*", help="arquivos ou pastas de run-*.json (padrão: métricas do jogo instalado)")
    ap.add_argument("--firestore", action="store_true", help="lê também a coleção runs com o login do gcloud")
    ap.add_argument("--saida", default=os.path.join(RAIZ, "build", "painel-balanceamento.html"))
    args = ap.parse_args()

    alvos = args.alvos or ([PASTA_PADRAO] if os.path.isdir(PASTA_PADRAO) else [])
    vistos, ignorados, origens = {}, 0, []
    for caminho in arquivos(alvos):
        try:
            lidos = ler_arquivo(caminho)
        except (OSError, ValueError, AttributeError, TypeError):  # ilegível ou com estrutura que não é de resumo
            ignorados += 1
            continue
        for i, r in lidos:
            if valido(r):
                vistos.setdefault(i, r)
            else:
                ignorados += 1
    if alvos:
        origens.append(f"{len(vistos)} de arquivos")
    if args.firestore:
        antes = len(vistos)
        for i, r in ler_firestore(PROJETO):
            if i and ID_RUN.match(i) and valido(r):
                vistos.setdefault(i, r)
            else:
                ignorados += 1
        origens.append(f"{len(vistos) - antes} novas do Firestore")
    if not origens:
        origens.append("nenhuma (sem pasta de métricas; passe arquivos ou --firestore)")

    runs = sorted(vistos.items())
    os.makedirs(os.path.dirname(os.path.abspath(args.saida)), exist_ok=True)
    with open(args.saida, "w", encoding="utf-8", newline="\n") as f:
        f.write(pagina(runs, origens))
    print(f"{len(runs)} run(s) no painel ({ignorados} ignorada(s)) → {args.saida}")


if __name__ == "__main__":
    main()
