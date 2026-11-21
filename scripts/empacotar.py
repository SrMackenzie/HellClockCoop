#!/usr/bin/env python3
"""
Monta o zip para os jogadores: o conteúdo vai direto na pasta do jogo (ao lado do Hell Clock.exe).

Uso:
  python3 scripts/empacotar.py --bepinex BepInEx_win_x64_5.4.23.6.zip \
      --corlib <pasta com as 8 DLLs + LICENSE-Mono> --plugin build/HellClockCoop.dll

Saída: dist/HellClockCoop-v<versão>.zip
Nada do jogo entra no pacote (só BepInEx, corlib do Mono e o nosso plugin).
"""
import argparse, hashlib, io, os, re, sys, zipfile

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERSAO_JOGO = "2.1.1.1"
CORLIB_ESPERADO = {"mscorlib.dll", "System.dll", "System.Core.dll", "System.Xml.dll", "System.Configuration.dll",
                   "System.Numerics.dll", "System.Security.dll", "Mono.Security.dll", "LICENSE-Mono"}

CFG_BEPINEX = (
    "[Logging.Console]\r\n\r\n"
    "## Abre a janela de console do BepInEx junto com o jogo (útil enquanto o mod está em teste).\r\n"
    "Enabled = true\r\n"
)


def versao_plugin():
    fonte = open(os.path.join(RAIZ, "src", "HellClockCoop", "Plugin.cs"), encoding="utf-8").read()
    return re.search(r'PluginVersion = "([^"]+)"', fonte).group(1)


def texto_windows(caminho, versao):
    t = open(caminho, encoding="utf-8").read().replace("{VERSAO}", versao).replace("{VERSAO_JOGO}", VERSAO_JOGO)
    return t.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8-sig")  # BOM: o Bloco de Notas lê acentos certo


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bepinex", required=True)
    ap.add_argument("--corlib", required=True)
    ap.add_argument("--plugin", default=os.path.join(RAIZ, "build", "HellClockCoop.dll"))
    a = ap.parse_args()

    versao = versao_plugin()
    faltando = CORLIB_ESPERADO - set(os.listdir(a.corlib))
    if faltando:
        sys.exit(f"corlib incompleto, faltando: {sorted(faltando)}")

    arquivos = {}  # caminho no zip -> bytes
    with zipfile.ZipFile(a.bepinex) as z:
        for info in z.infolist():
            nome = info.filename
            if info.is_dir() or nome.endswith(".xml") or nome == "changelog.txt":
                continue
            dados = z.read(info)
            if nome == "doorstop_config.ini":
                texto = dados.decode("utf-8")
                texto, n = re.subn(r"(?m)^dll_search_path_override =.*?(\r?)$",
                                   r"dll_search_path_override = BepInEx\\unstripped_corlib\1", texto)
                if n != 1:
                    sys.exit("doorstop_config.ini: não achei dll_search_path_override")
                dados = texto.encode("utf-8")
            arquivos[nome] = dados

    for nome in sorted(os.listdir(a.corlib)):
        arquivos[f"BepInEx/unstripped_corlib/{nome}"] = open(os.path.join(a.corlib, nome), "rb").read()
    arquivos["BepInEx/plugins/HellClockCoop/HellClockCoop.dll"] = open(a.plugin, "rb").read()
    arquivos["BepInEx/plugins/HellClockCoop/LICENCAS.txt"] = texto_windows(os.path.join(RAIZ, "dist-template", "LICENCAS.txt"), versao)
    arquivos["BepInEx/config/BepInEx.cfg"] = CFG_BEPINEX.encode("utf-8")
    arquivos["LEIA-ME - Hell Clock Coop.txt"] = texto_windows(os.path.join(RAIZ, "dist-template", "LEIA-ME - Hell Clock Coop.txt"), versao)

    os.makedirs(os.path.join(RAIZ, "dist"), exist_ok=True)
    saida = os.path.join(RAIZ, "dist", f"HellClockCoop-v{versao}.zip")
    with zipfile.ZipFile(saida, "w", zipfile.ZIP_DEFLATED) as z:
        for nome in sorted(arquivos):
            info = zipfile.ZipInfo(nome, date_time=(2026, 1, 1, 0, 0, 0))  # datas fixas: zip reproduzível
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, arquivos[nome])

    print(f"OK: {saida} ({os.path.getsize(saida) / 1024:.0f} KB, {len(arquivos)} arquivos)")
    print("sha256:", hashlib.sha256(open(saida, "rb").read()).hexdigest())
    for nome in sorted(arquivos):
        print(f"  {len(arquivos[nome]):>9}  {nome}")


if __name__ == "__main__":
    main()
