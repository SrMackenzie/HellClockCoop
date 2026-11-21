using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HellClockCoop.Interface
{
    /// <summary>Uma versão do docs/changelog.json, embutida na DLL por scripts/gerar-novidades.py.</summary>
    internal sealed class VersaoNovidade
    {
        internal readonly string V, Data, Titulo;
        internal readonly bool Testado;
        internal readonly string[] Itens, Testar;

        internal VersaoNovidade(string v, string data, string titulo, bool testado, string[] itens, string[] testar)
        {
            V = v; Data = data; Titulo = titulo; Testado = testado;
            Itens = itens ?? new string[0];
            Testar = testar ?? new string[0];
        }
    }

    /// <summary>
    /// Janela "Novidades": o que mudou na versão atual do mod e nas 3 anteriores, com "O que testar" nas versões ainda
    /// não testadas no jogo. O texto vem de NovidadesTexto (gerado do docs/changelog.json; sem JSON nem rede em runtime).
    /// - Abre sozinha uma vez, no menu (sem personagem), quando a versão do mod difere de [Interface] NovidadesVistas;
    ///   ao fechar, grava a versão atual ali. Espera a janela do aceite das métricas fechar antes.
    /// - Abre pela tecla ([Teclas] Novidades, F10) ou pelo item "Novidades" do menu do coop, no menu e em jogo.
    /// - Aberta, segura a entrada do jogo (BloqueioEntrada: semáforo do jogo e módulo de UI); abas e "Fechar" respondem
    ///   ao mouse. Em jogo, se o semáforo do jogo não travar, volta ao modo só teclado/controle da 0.9.2 (o clique numa
    ///   aba moveria o personagem) e o rodapé indica só a F10.
    /// - Setas esquerda/direita ou LB/RB trocam de versão; setas cima/baixo (ou o direcional) rolam; Esc, F10 ou B fecham.
    /// </summary>
    internal static class Novidades
    {
        private const float Largura = 720f;
        private const float Margem = 24f;
        private const float AlturaBotao = 48f;
        private const float AlturaAba = 44f;
        private const float PassoRolagem = 60f;

        private static ConfigEntry<string> _vista;
        internal static ConfigEntry<string> Tecla;

        private static bool _aberta, _semPersonagem, _automaticaFeita;
        private static int _aba;
        private static Vector2 _rolagem;
        private static float _fecharLiberadoEm; // abertura automática: Esc/B/clique para pular a abertura do jogo não fecham
        private const float EsperaFechar = 1.5f;
        private static GUIStyle _titulo, _cabVersao, _tituloVersao, _item, _rotuloTestar, _itemTestar, _aba0, _rodape, _botao, _invisivel;

        internal static bool Aberta => _aberta;

        /// <summary>Cópia da PluginVersion (const: o codectest lê sem carregar o Plugin, que depende do BepInEx).</summary>
        internal const string VersaoAtual = Plugin.PluginVersion;

        internal static void Configurar(ConfigFile config)
        {
            Tecla = config.Bind("Teclas", "Novidades", "F10", "Abre a janela de novidades do mod (o que mudou nas últimas versões).");
            _vista = config.Bind("Interface", "NovidadesVistas", "",
                "Última versão do mod cuja janela de novidades já apareceu (o mod preenche; apague para ver de novo no próximo início).");
        }

        // ---------- Regras puras (testadas no tools/codectest) ----------

        /// <summary>Abre sozinha quando a versão vista (da config) não é a atual, inclusive na primeira instalação.</summary>
        internal static bool DeveAbrirSozinha(string vista, string atual) =>
            !string.IsNullOrEmpty(atual) && (vista ?? "").Trim() != atual;

        /// <summary>Próxima aba, girando nas pontas (da última volta para a primeira).</summary>
        internal static int TrocarAba(int aba, int delta, int total) =>
            total <= 0 ? 0 : (((aba + delta) % total) + total) % total;

        /// <summary>"O que testar" só aparece em versão ainda não testada no jogo e com roteiro preenchido.</summary>
        internal static bool MostraTestar(VersaoNovidade v) => v != null && !v.Testado && v.Testar.Length > 0;

        // ---------- Entrada (no Update do runtime, depois da JanelaAceite) ----------

        internal static void Atualizar(bool semPersonagem)
        {
            _semPersonagem = semPersonagem;
            if (!_aberta)
            {
                if (Balanceamento.JanelaAceite.Aberta) return;
                if (Teclas.Pressionou(Tecla.Value)) Abrir("tecla " + Tecla.Value);
                else if (!_automaticaFeita && semPersonagem && DeveAbrirSozinha(_vista.Value, VersaoAtual))
                {
                    _automaticaFeita = true;
                    Abrir("primeira abertura da " + Plugin.PluginVersion);
                    _fecharLiberadoEm = Time.unscaledTime + EsperaFechar; // a exibição é única: não gastar sem ver
                }
                return;
            }

            BloqueioEntrada.Pedir();
            bool liberado = Time.unscaledTime >= _fecharLiberadoEm; // a tecla do mod fecha sempre
            if (Teclas.Pressionou(Tecla.Value) || (liberado && (Teclas.Pressionou("Escape") || Teclas.Controle(c => c.buttonEast, KeyCode.JoystickButton1))))
            {
                Fechar();
                return;
            }
            int total = NovidadesTexto.Versoes.Length;
            if (Teclas.Pressionou("LeftArrow") || Teclas.Controle(c => c.leftShoulder, KeyCode.JoystickButton4)) MudarAba(TrocarAba(_aba, -1, total));
            if (Teclas.Pressionou("RightArrow") || Teclas.Controle(c => c.rightShoulder, KeyCode.JoystickButton5)) MudarAba(TrocarAba(_aba, 1, total));
            if (Teclas.Pressionou("UpArrow") || Teclas.Controle(c => c.dpad.up, null)) _rolagem.y -= PassoRolagem;
            if (Teclas.Pressionou("DownArrow") || Teclas.Controle(c => c.dpad.down, null)) _rolagem.y += PassoRolagem; // o limite vem no Desenhar
        }

        /// <summary>Item "Novidades" do menu do coop.</summary>
        internal static void AbrirPeloMenu()
        {
            if (!_aberta) Abrir("menu do coop");
        }

        private static void Abrir(string motivo)
        {
            _fecharLiberadoEm = 0f;
            _aberta = true;
            _aba = 0;
            _rolagem = Vector2.zero;
            BloqueioEntrada.Pedir(); // já no quadro em que abre (pelo menu, no OnGUI, o Update só viria no quadro seguinte)
            Plugin.Log.LogInfo($"[Novidades] Janela aberta ({motivo}).");
        }

        private static void MudarAba(int aba)
        {
            _aba = aba;
            _rolagem = Vector2.zero;
        }

        private static void Fechar()
        {
            if (!_aberta) return;
            _aberta = false; // a entrada do jogo volta dois quadros depois (BloqueioEntrada)
            if (_vista.Value != Plugin.PluginVersion) _vista.Value = Plugin.PluginVersion; // o BepInEx grava o .cfg
            Plugin.Log.LogInfo("[Novidades] Janela fechada.");
        }

        // ---------- Desenho (no OnGUI do runtime) ----------

        internal static void Desenhar()
        {
            if (!_aberta) return;
            VersaoNovidade[] versoes = NovidadesTexto.Versoes;
            if (versoes.Length == 0) return;
            if (_aba >= versoes.Length) _aba = 0;
            VersaoNovidade v = versoes[_aba];
            Estilos();

            // Tudo desenhado em 1080p e escalado, como a JanelaAceite.
            float escala = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            Matrix4x4 matrizAntes = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(escala, escala, 1f));
            float telaL = Screen.width / escala, telaA = Screen.height / escala;
            Color corAntes = GUI.color, fundoAntes = GUI.backgroundColor;
            bool mouse = _semPersonagem || BloqueioEntrada.SeguraOJogo; // em jogo, só com o jogo travado (o clique vazaria)

            Retangulo(new Rect(0f, 0f, telaL, telaA), new Color(0f, 0f, 0f, 0.72f));

            float largura = Mathf.Min(Largura, telaL - 32f);
            float interna = largura - 2f * Margem;
            var titulo = new GUIContent("Novidades do Hell Clock Coop");
            var rodape = new GUIContent(mouse
                ? "Setas ou LB/RB: trocar versão  ·  Esc, " + Tecla.Value + " ou B: fechar"
                : "Setas: trocar versão e rolar  ·  " + Tecla.Value + ": fechar"); // jogo sem trava: Esc e B também chegariam ao jogo
            float aTitulo = _titulo.CalcHeight(titulo, interna);
            float lRodape = mouse ? interna - 160f - 12f : interna;
            float aRodape = Mathf.Max(_rodape.CalcHeight(rodape, lRodape), mouse ? AlturaBotao : 0f);

            float larguraCorpo = interna - 18f; // espaço da barra de rolagem
            float aCorpo = Corpo(v, 0f, 0f, larguraCorpo, false);
            float fixo = Margem + aTitulo + 12f + AlturaAba + 16f + 16f + aRodape + Margem;
            float visivel = Mathf.Max(80f, Mathf.Min(aCorpo, telaA - 32f - fixo));
            float altura = fixo + visivel;
            var caixa = new Rect(Mathf.Round((telaL - largura) / 2f), Mathf.Round(Mathf.Max(16f, (telaA - altura) / 2f)), largura, altura);

            Retangulo(caixa, new Color(0.55f, 0.14f, 0.1f, 1f));
            Retangulo(new Rect(caixa.x + 2f, caixa.y + 2f, caixa.width - 4f, caixa.height - 4f), new Color(0.07f, 0.06f, 0.06f, 0.98f));

            float x = caixa.x + Margem, y = caixa.y + Margem;
            GUI.Label(new Rect(x, y, interna, aTitulo), titulo, _titulo);
            y += aTitulo + 12f;

            // Abas: uma por versão; a atual (primeira) marcada como NOVA.
            float lAba = (interna - 8f * (versoes.Length - 1)) / versoes.Length;
            for (int i = 0; i < versoes.Length; i++)
            {
                var r = new Rect(x + i * (lAba + 8f), y, lAba, AlturaAba);
                Retangulo(r, i == _aba ? new Color(0.55f, 0.14f, 0.1f, 1f) : new Color(0.16f, 0.13f, 0.12f, 1f));
                GUI.Label(r, new GUIContent(i == 0 ? "v" + versoes[i].V + "  NOVA" : "v" + versoes[i].V), _aba0);
                if (mouse && GUI.Button(r, GUIContent.none, _invisivel)) MudarAba(i);
            }
            y += AlturaAba + 16f;

            // Corpo com rolagem quando passa da altura da tela.
            _rolagem.y = Mathf.Clamp(_rolagem.y, 0f, Mathf.Max(0f, aCorpo - visivel));
            var areaCorpo = new Rect(x, y, interna, visivel);
            if (aCorpo > visivel && mouse)
            {
                _rolagem = GUI.BeginScrollView(areaCorpo, _rolagem, new Rect(0f, 0f, larguraCorpo, aCorpo), false, false);
                Corpo(v, 0f, 0f, larguraCorpo, true);
                GUI.EndScrollView();
            }
            else if (aCorpo > visivel)
            {
                // Em jogo: só recorta e rola pelas setas (a barra e a roda do ScrollView responderiam ao mouse, que vaza).
                GUI.BeginGroup(areaCorpo, GUIContent.none, GUIStyle.none);
                Corpo(v, 0f, -_rolagem.y, larguraCorpo, true);
                GUI.EndGroup();
                float aBarra = Mathf.Max(24f, visivel * visivel / aCorpo);
                float yBarra = (visivel - aBarra) * (_rolagem.y / (aCorpo - visivel));
                Retangulo(new Rect(x + interna - 6f, y + yBarra, 6f, aBarra), new Color(0.55f, 0.14f, 0.1f, 0.8f));
            }
            else
            {
                Corpo(v, x, y, interna, true);
            }
            y += visivel + 16f;

            Retangulo(new Rect(x, y - 8f, interna, 1f), new Color(0.55f, 0.14f, 0.1f, 0.6f));
            GUI.Label(new Rect(x, y, lRodape, aRodape), rodape, _rodape);
            bool fecharLiberado = Time.unscaledTime >= _fecharLiberadoEm;
            GUI.enabled = fecharLiberado; // desativado na espera da abertura automática
            bool fechar = mouse && GUI.Button(new Rect(x + interna - 160f, y, 160f, AlturaBotao), new GUIContent("Fechar"), _botao);
            GUI.enabled = true;

            GUI.color = corAntes;
            GUI.backgroundColor = fundoAntes;
            GUI.matrix = matrizAntes;
            if (fechar && fecharLiberado) Fechar();
        }

        /// <summary>Desenha (ou só mede, com desenhar = false) o corpo de uma versão; devolve a altura.</summary>
        private static float Corpo(VersaoNovidade v, float x, float y0, float w, bool desenhar)
        {
            float y = y0;
            y += Linha(new GUIContent("v" + v.V + "  ·  " + v.Data + (v.Testado ? "" : "  ·  ainda não testada no jogo")), _cabVersao, x, y, w, desenhar) + 4f;
            y += Linha(new GUIContent(v.Titulo), _tituloVersao, x, y, w, desenhar) + 12f;
            foreach (string item in v.Itens) y += Marcador("•", item, _item, x, y, w, desenhar) + 8f;

            if (MostraTestar(v))
            {
                y += 8f;
                float inicio = y;
                float xi = x + 14f, wi = w - 28f;
                float h = 12f + _rotuloTestar.CalcHeight(new GUIContent("O QUE TESTAR"), wi) + 6f;
                foreach (string t in v.Testar) h += _itemTestar.CalcHeight(new GUIContent(t), wi - 18f) + 6f;
                h += 6f;
                if (desenhar) Retangulo(new Rect(x, inicio, w, h), new Color(0.13f, 0.07f, 0.06f, 1f));
                float yi = inicio + 12f;
                yi += Linha(new GUIContent("O QUE TESTAR"), _rotuloTestar, xi, yi, wi, desenhar) + 6f;
                foreach (string t in v.Testar) yi += Marcador("›", t, _itemTestar, xi, yi, wi, desenhar) + 6f;
                y = inicio + h;
            }
            return y - y0;
        }

        private static float Linha(GUIContent c, GUIStyle s, float x, float y, float w, bool desenhar)
        {
            float h = s.CalcHeight(c, w);
            if (desenhar) GUI.Label(new Rect(x, y, w, h), c, s);
            return h;
        }

        /// <summary>Item com recuo: o marcador numa coluna e o texto quebrando alinhado à direita dele.</summary>
        private static float Marcador(string marca, string texto, GUIStyle s, float x, float y, float w, bool desenhar)
        {
            var c = new GUIContent(texto);
            float h = s.CalcHeight(c, w - 18f);
            if (desenhar)
            {
                GUI.Label(new Rect(x, y, 18f, h), new GUIContent(marca), s);
                GUI.Label(new Rect(x + 18f, y, w - 18f, h), c, s);
            }
            return h;
        }

        private static void Estilos()
        {
            if (_titulo != null) return;
            var creme = new Color(0.96f, 0.9f, 0.82f);
            _titulo = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
            _titulo.normal.textColor = creme;
            _cabVersao = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _cabVersao.normal.textColor = new Color(0.62f, 0.6f, 0.57f);
            _tituloVersao = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, wordWrap = true };
            _tituloVersao.normal.textColor = creme;
            _item = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
            _item.normal.textColor = new Color(0.86f, 0.84f, 0.8f);
            _rotuloTestar = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true };
            _rotuloTestar.normal.textColor = new Color(0.92f, 0.5f, 0.38f);
            _itemTestar = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = false };
            _itemTestar.normal.textColor = new Color(0.86f, 0.84f, 0.8f);
            _aba0 = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _aba0.normal.textColor = creme;
            _rodape = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            _rodape.normal.textColor = new Color(0.62f, 0.6f, 0.57f);
            _botao = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            _invisivel = new GUIStyle(); // botão sem desenho por cima da aba (só recebe o clique)
        }

        private static void Retangulo(Rect r, Color cor)
        {
            Color antes = GUI.color;
            GUI.color = cor;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = antes;
        }
    }
}
