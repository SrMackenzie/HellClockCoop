using System;
using System.Collections.Generic;
using HellClock.UI;
using HellClock.UI.Options;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Menu do coop com a interface do próprio jogo (0.12.0), montado com as peças da tela de Opções. Desde a 0.24.0 é uma
    /// coluna colada à direita, na altura toda (as Opções do jogo ficam à esquerda), que entra e sai deslizando como elas.
    /// - Na primeira abertura, copia em tempo de execução os pedaços da OptionsWindow do jogo (fundo, cabeçalho com o [X],
    ///   botão de aba, fundo do conteúdo, título de seção, linha com caixa de marcar e linha com barra). Nada sai do jogo
    ///   para o repositório ou para o pacote: são as imagens e fontes já carregadas.
    /// - Das cópias saem todos os scripts (do jogo, da localização, dos sons, botões e layouts): ficam só imagens, textos,
    ///   máscaras e CanvasGroup. Assim nada se liga às opções do jogo, nada grava o save das opções e o texto do mod não é
    ///   trocado pela localização. Quem posiciona, foca e grava (no .cfg do mod) é o MenuCoop.
    /// - Canvas próprio (sobreposição, ordem 900), escala do jogo (1920x1080, pela altura). Sem GraphicRaycaster: o
    ///   EventSystem fica desligado pelo BloqueioEntrada e o mouse é lido pelo MenuCoop (AcaoSob, AbaSob, FecharSob, NaBarra).
    /// - Montado quando o botão "Coop" entra na lista do menu do jogo (BotoesDoJogo, 0.13.0), na abertura automática da aba
    ///   Novidades ou na primeira abertura. Reserva do botão do menu (só se a cópia do BotoesDoJogo falhou): a peça de aba num
    ///   segundo canvas (HellClockCoop.BotaoDoMenu, ordem 890), posta nos pixels do OnGUI divididos por Screen.height/1080 (o
    ///   BotaoDoMenu tenta montar a cada 2 s); o EsconderBotaoParado o apaga no Update quando o OnGUI deixa de pedi-lo.
    /// - Qualquer peça ausente ou erro: o MenuCoop volta à gaveta desenhada no OnGUI.
    /// </summary>
    internal static class PainelNativo
    {
        // 0.24.0 (visual B): coluna de 900 colada à direita, na altura toda; a lista de salas cabe sem quebrar e, em 4:3
        // (canvas de 1440 × 1080), sobra tela à esquerda.
        private const float LarguraPainel = 900f, AlturaSecao = 46f, AlturaMinimaItem = 48f, AlturaDetalhe = 26f;
        // Deslize das Opções do jogo (OptionsWindow.Show/Hide → GenericHUD.MoveTo/MoveBack): DOAnchorPos em 0,35 s, tempo sem
        // escala, Ease.OutBack 0,8 ao abrir e Ease.InOutBack 0,8 ao fechar. O jogo anda 760 (a largura da janela dele); aqui
        // anda a largura da coluna, para ela sair inteira da tela.
        private const float DuracaoDeslize = 0.35f, Recuo = 0.8f;
        private const float FolgaFundo = 40f; // o fundo passa da borda direita: no recuo do OutBack (~2% da largura) não abre vão
        private const float LarguraDegrade = 520f; // escurecer (só no menu do jogo): degradê à esquerda da coluna, cheio sob ela
        private const float TopoAbas = -150f, TopoConteudo = -196f, BaseConteudo = 78f;
        private const float LarguraAba = 160f, AlturaAba = 44f, LarguraValor = 180f, LarguraBotao = 260f;
        private const float TamanhoTexto = 18f, TamanhoInfo = 17f, TamanhoRodape = 15f;

        private static readonly Color CorTexto = new Color(0.62f, 0.57f, 0.55f);
        private static readonly Color CorFoco = new Color(0.96f, 0.9f, 0.82f);
        private static readonly Color CorValor = new Color(0.92f, 0.55f, 0.32f);
        private static readonly Color CorInfo = new Color(0.72f, 0.68f, 0.65f);

        private sealed class Visual
        {
            public MenuCoop.Tipo Tipo;
            public MenuCoop.Acao Acao; // a ação desenhada (o mouse devolve a ação, não o índice da lista)
            public RectTransform Rt, Preenche, Alca, Barra;
            public TMP_Text Texto, Valor, Detalhe;
            public GameObject Destaque, Caixa, Marca;
            public string Medido; // informação: texto e largura da última medida (a altura só é recalculada quando mudam)
            public float LarguraMedida, AlturaMedida;
        }

        private sealed class Aba
        {
            public RectTransform Rt;
            public TMP_Text Texto;
            public GameObject Destaque;
        }

        private static GameObject _raiz, _escurecer, _botaoRaiz, _botaoDestaque;
        private static RectTransform _painel, _viewport, _lista, _fechar, _botao;
        private static TMP_Text _botaoTexto;
        private static int _botaoQuadro;
        private static float _proximaTentativa;
        private static bool _semJanela = true;

        /// <summary>
        /// A janela das Opções do jogo ainda não existe (telas de abertura, antes do menu de saves): o botão do menu espera
        /// por ela em vez de aparecer no visual simples. Com a janela presente e a montagem falhando, vale o botão simples.
        /// </summary>
        internal static bool EsperandoJanela => !_desativado && _raiz == null && _semJanela;
        private static Transform _deposito;
        private static GameObject _mSecao, _mInfo, _mLinha, _mDeslizante;
        private static TMP_Text _rodape;
        private static readonly List<Aba> Abas = new List<Aba>();
        private static readonly List<Visual> Visuais = new List<Visual>();
        private static int _mostradas;
        private static float _rolagem;
        private static CanvasGroup _grupoEscurecer;
        private static float _xDe, _xPara, _inicioDeslize;
        private static bool _deslizando, _fechando;
        private static bool _desativado, _avisouSemModelo;

        // ---------- Montagem ----------

        /// <summary>Monta o painel se ainda não existe. Falso: usar a gaveta do OnGUI (tenta de novo na próxima abertura).</summary>
        internal static bool Preparar()
        {
            if (_desativado) return false;
            if (_raiz != null) return true;
            try
            {
                OptionsWindow janela = JanelaDoJogo();
                _semJanela = janela == null;
                Transform ancora = janela != null ? janela.transform.Find("Container/MiddleLeftAnchor") : null;
                Transform fundo = ancora != null ? ancora.Find("Background") : null;
                Transform cabecalho = ancora != null ? ancora.Find("Header") : null;
                Transform aba = ancora != null ? ancora.Find("SwitchButtons/GameplayButton") : null;
                Transform fundoConteudo = ancora != null ? ancora.Find("Content/Bg") : null;
                Transform secao = janela != null ? Procurar(janela.transform, "TitleGameplay") : null;
                ToggleWithLabel[] caixas = janela != null ? janela.GetComponentsInChildren<ToggleWithLabel>(true) : null;
                SliderWithLabel[] barras = janela != null ? janela.GetComponentsInChildren<SliderWithLabel>(true) : null;
                Transform linha = caixas != null && caixas.Length > 0 ? caixas[0].transform : null;
                Transform deslizante = barras != null && barras.Length > 0 ? barras[0].transform : null;
                if (fundo == null || cabecalho == null || aba == null || fundoConteudo == null || secao == null || linha == null || deslizante == null)
                {
                    if (janela != null && !_avisouSemModelo) // sem a janela (telas de abertura) é só esperar, sem aviso
                    {
                        Plugin.Log.LogInfo("[Menu] Peças da interface do jogo não encontradas na janela das Opções; usando o menu simples.");
                        _avisouSemModelo = true;
                    }
                    return false;
                }
                Construir(fundo, cabecalho, aba, fundoConteudo, secao, linha, deslizante);
                Plugin.Log.LogInfo("[Menu] Painel do coop montado com a interface do jogo.");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Menu] Não deu para montar o painel com a interface do jogo; usando o menu simples: {e}");
                Desativar();
                return false;
            }
        }

        /// <summary>Desliga o painel nativo até o jogo reiniciar (erro): o MenuCoop usa a gaveta do OnGUI.</summary>
        internal static void Desativar()
        {
            _desativado = true;
            if (_raiz != null) Object.Destroy(_raiz);
            if (_botaoRaiz != null) Object.Destroy(_botaoRaiz);
            _raiz = _botaoRaiz = null;
            _botao = null;
            _deslizando = _fechando = false;
            Abas.Clear();
            Visuais.Clear();
        }

        /// <summary>A OptionsWindow de PC (a do celular também existe na cena e fica de fora).</summary>
        private static OptionsWindow JanelaDoJogo()
        {
            OptionsWindow escolhida = null;
            foreach (OptionsWindow j in Resources.FindObjectsOfTypeAll<OptionsWindow>())
            {
                if (j == null || !j.gameObject.scene.IsValid()) continue; // prefab sem cena
                if (j.name.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                escolhida = j;
                break;
            }
            return escolhida;
        }

        private static void Construir(Transform fundo, Transform cabecalho, Transform aba, Transform fundoConteudo,
                                      Transform secao, Transform linha, Transform deslizante)
        {
            _raiz = NovoCanvas("HellClockCoop.MenuDoCoop", 900); // acima das janelas do jogo, abaixo do aviso de save (1000)

            var deposito = new GameObject("Modelos");
            deposito.transform.SetParent(_raiz.transform, false);
            deposito.SetActive(false); // modelos copiados aqui não acordam (Awake) os scripts do jogo antes da limpeza
            _deposito = deposito.transform;

            _escurecer = new GameObject("Escurecer");
            RectTransform rtEsc = _escurecer.AddComponent<RectTransform>();
            rtEsc.SetParent(_raiz.transform, false);
            Esticar(rtEsc, 0f, 0f, 0f, 0f);
            _grupoEscurecer = _escurecer.AddComponent<CanvasGroup>(); // acompanha o deslize (MostrarDeslize)
            _grupoEscurecer.blocksRaycasts = false;
            var degrade = new GameObject("Degrade");
            RectTransform rtDeg = degrade.AddComponent<RectTransform>();
            rtDeg.SetParent(rtEsc, false);
            rtDeg.anchorMin = new Vector2(1f, 0f);
            rtDeg.anchorMax = new Vector2(1f, 1f);
            rtDeg.pivot = new Vector2(1f, 0.5f);
            rtDeg.sizeDelta = new Vector2(LarguraPainel + LarguraDegrade, 0f);
            rtDeg.anchoredPosition = Vector2.zero;
            RawImage imgDeg = degrade.AddComponent<RawImage>();
            imgDeg.texture = TexturaDegrade(LarguraDegrade / (LarguraPainel + LarguraDegrade));
            imgDeg.raycastTarget = false;

            // 0.24.0: coluna presa à borda direita, na altura toda (o canvas tem sempre 1080 de altura); começa fora da tela.
            var painel = new GameObject("Painel");
            _painel = painel.AddComponent<RectTransform>();
            _painel.SetParent(_raiz.transform, false);
            _painel.anchorMin = new Vector2(1f, 0f);
            _painel.anchorMax = new Vector2(1f, 1f);
            _painel.pivot = new Vector2(1f, 0.5f);
            _painel.sizeDelta = new Vector2(LarguraPainel, 0f);
            _painel.anchoredPosition = new Vector2(LarguraPainel, 0f);

            // Fundo da janela das Opções.
            RectTransform f = Copiar(fundo, _painel, "Fundo");
            Esticar(f, 0f, 0f, FolgaFundo, 0f);

            // Cabeçalho do jogo: linhas, título e [X] (sem o ícone de atalho do controle).
            RectTransform cab = Copiar(cabecalho, _painel, "Cabecalho");
            cab.offsetMin = new Vector2(0f, cab.offsetMin.y);
            cab.offsetMax = new Vector2(0f, cab.offsetMax.y);
            Esconder(cab, "SubtitleText");
            Esconder(cab, "CloseButton/Content/ButtonHotkey");
            Esconder(cab, "CloseButton/Content/ButtonHighlight/Fill");
            TMP_Text titulo = Texto(cab, "TitleTextLabel");
            if (titulo != null) titulo.text = "COOP";
            _fechar = cab.Find("CloseButton") as RectTransform;
            CaberNaJanela(cab);

            // Abas: o botão de aba das Opções, uma cópia por aba (Sessão, Salas, Ajustes, Novidades: 4 × 160 + 3 × 16 = 688 cabem nos 900 da coluna).
            var abas = new GameObject("Abas");
            RectTransform rtAbas = abas.AddComponent<RectTransform>();
            rtAbas.SetParent(_painel, false);
            rtAbas.anchorMin = rtAbas.anchorMax = new Vector2(0.5f, 1f);
            rtAbas.pivot = new Vector2(0.5f, 0.5f);
            rtAbas.anchoredPosition = new Vector2(0f, TopoAbas);
            int nAbas = MenuCoop.NomesDasAbas.Length;
            rtAbas.sizeDelta = new Vector2(nAbas * LarguraAba + (nAbas - 1) * 16f, AlturaAba);
            Abas.Clear();
            for (int i = 0; i < MenuCoop.NomesDasAbas.Length; i++)
            {
                RectTransform rt = Copiar(aba, rtAbas, "Aba" + i);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(LarguraAba, AlturaAba);
                rt.anchoredPosition = new Vector2((i - (MenuCoop.NomesDasAbas.Length - 1) / 2f) * (LarguraAba + 16f), 0f);
                Transform bg = rt.Find("Background");
                if (bg != null) Esticar((RectTransform)bg, 0f, 0f, 0f, 0f);
                TMP_Text t = Texto(rt, "Text");
                if (t != null)
                {
                    Esticar(t.rectTransform, 0f, 0f, 0f, 0f);
                    Estilo(t, TamanhoTexto, TextAlignmentOptions.Center, false);
                    t.text = MenuCoop.NomesDasAbas[i];
                }
                Transform destaque = rt.Find("GuideHighlight (1)");
                Abas.Add(new Aba { Rt = rt, Texto = t, Destaque = destaque != null ? destaque.gameObject : null });
            }

            // Botão "Menu do coop" do menu do jogo: a mesma peça das abas, num canvas próprio (a janela só fica ativa aberta).
            // Quem posiciona e trata o clique é o MenuCoop, no OnGUI, logo abaixo do painel do canto.
            _botaoRaiz = NovoCanvas("HellClockCoop.BotaoDoMenu", 890);
            _botao = Copiar(aba, _botaoRaiz.transform, "Botao");
            _botao.anchorMin = _botao.anchorMax = new Vector2(0f, 1f);
            _botao.pivot = new Vector2(0f, 1f);
            Transform fundoBotao = _botao.Find("Background");
            if (fundoBotao != null) Esticar((RectTransform)fundoBotao, 0f, 0f, 0f, 0f);
            _botaoTexto = Texto(_botao, "Text");
            if (_botaoTexto != null)
            {
                Esticar(_botaoTexto.rectTransform, 0f, 0f, 0f, 0f);
                Estilo(_botaoTexto, TamanhoTexto, TextAlignmentOptions.Center, false);
            }
            Transform destaqueBotao = _botao.Find("GuideHighlight (1)");
            _botaoDestaque = destaqueBotao != null ? destaqueBotao.gameObject : null;

            // Conteúdo: fundo interno das Opções, com recorte e a lista que rola.
            RectTransform conteudo = Copiar(fundoConteudo, _painel, "Conteudo");
            conteudo.anchorMin = new Vector2(0f, 0f);
            conteudo.anchorMax = new Vector2(1f, 1f);
            conteudo.pivot = new Vector2(0.5f, 0.5f);
            conteudo.offsetMin = new Vector2(20f, BaseConteudo);
            conteudo.offsetMax = new Vector2(-20f, TopoConteudo);
            var vp = new GameObject("Recorte");
            _viewport = vp.AddComponent<RectTransform>();
            _viewport.SetParent(conteudo, false);
            Esticar(_viewport, 10f, 12f, -10f, -12f);
            vp.AddComponent<RectMask2D>();
            var lista = new GameObject("Lista");
            _lista = lista.AddComponent<RectTransform>();
            _lista.SetParent(_viewport, false);
            _lista.anchorMin = new Vector2(0f, 1f);
            _lista.anchorMax = new Vector2(1f, 1f);
            _lista.pivot = new Vector2(0.5f, 1f);
            _lista.offsetMin = new Vector2(16f, 0f);
            _lista.offsetMax = new Vector2(-16f, 0f);

            // Modelos limpos das linhas (no depósito inativo).
            _mSecao = ModeloSecao(secao);
            _mLinha = ModeloLinha(linha);
            _mDeslizante = ModeloDeslizante(deslizante);
            _mInfo = ModeloTexto(linha.Find("Text"), "Info", TamanhoInfo, true);

            // Rodapé com as teclas.
            GameObject rod = Object.Instantiate(_mInfo, _painel, false);
            rod.name = "Rodape";
            RectTransform rtRod = (RectTransform)rod.transform;
            rtRod.anchorMin = new Vector2(0f, 0f);
            rtRod.anchorMax = new Vector2(1f, 0f);
            rtRod.pivot = new Vector2(0.5f, 0f);
            rtRod.offsetMin = new Vector2(32f, 14f);
            rtRod.offsetMax = new Vector2(-32f, BaseConteudo - 8f);
            _rodape = rod.GetComponent<TMP_Text>();
            Estilo(_rodape, TamanhoRodape, TextAlignmentOptions.Center, true);
            _rodape.color = CorTexto;
        }

        private static GameObject ModeloSecao(Transform secao)
        {
            RectTransform rt = Copiar(secao, _deposito, "Secao");
            Topo(rt);
            Transform moldura = rt.Find("FrameImage");
            if (moldura != null)
            {
                var m = (RectTransform)moldura;
                m.anchorMin = new Vector2(0f, 0.5f);
                m.anchorMax = new Vector2(1f, 0.5f);
                m.pivot = new Vector2(0.5f, 0.5f);
                m.sizeDelta = new Vector2(0f, 36f);
                m.anchoredPosition = new Vector2(0f, -4f);
            }
            Esconder(rt, "BgImage");
            TMP_Text t = Texto(rt, "TextLabel");
            if (t != null)
            {
                t.enableAutoSizing = false;
                t.fontSize = TamanhoTexto;
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            }
            return rt.gameObject;
        }

        private static GameObject ModeloLinha(Transform linha)
        {
            RectTransform rt = Copiar(linha, _deposito, "Linha");
            Topo(rt);
            Destaque(rt);
            TMP_Text t = Texto(rt, "Text");
            if (t != null)
            {
                Esticar(t.rectTransform, 10f, 0f, -56f, 0f);
                Estilo(t, TamanhoTexto, TextAlignmentOptions.MidlineLeft, false);
                GameObject v = Object.Instantiate(t.gameObject, rt, false);
                v.name = "Valor";
                var rv = (RectTransform)v.transform;
                rv.anchorMin = new Vector2(1f, 0f);
                rv.anchorMax = new Vector2(1f, 1f);
                rv.pivot = new Vector2(1f, 0.5f);
                rv.sizeDelta = new Vector2(LarguraValor, 0f);
                rv.anchoredPosition = new Vector2(-10f, 0f);
                TMP_Text tv = v.GetComponent<TMP_Text>();
                tv.alignment = TextAlignmentOptions.MidlineRight;
                tv.color = CorValor;
                // 0.21.0: segunda linha (aba Salas, visual A): faixa de baixo, recuada, na cor de informação; some sem detalhe.
                GameObject d = Object.Instantiate(t.gameObject, rt, false);
                d.name = "Detalhe";
                var rd = (RectTransform)d.transform;
                rd.anchorMin = new Vector2(0f, 0f);
                rd.anchorMax = new Vector2(1f, 0f);
                rd.pivot = new Vector2(0f, 0f);
                rd.offsetMin = new Vector2(34f, 6f);
                rd.offsetMax = new Vector2(-10f, 6f + AlturaDetalhe);
                TMP_Text td = d.GetComponent<TMP_Text>();
                td.fontSize = TamanhoInfo;
                td.color = CorInfo;
                d.SetActive(false);
            }
            Transform caixa = rt.Find("UI_ToggleBox");
            if (caixa != null)
            {
                var rc = (RectTransform)caixa;
                rc.anchorMin = rc.anchorMax = new Vector2(1f, 0.5f);
                rc.pivot = new Vector2(1f, 0.5f);
                rc.anchoredPosition = new Vector2(-10f, 0f);
                Esconder(rc, "highlight");
            }
            return rt.gameObject;
        }

        private static GameObject ModeloDeslizante(Transform deslizante)
        {
            RectTransform rt = Copiar(deslizante, _deposito, "Deslizante");
            Topo(rt);
            Destaque(rt);
            Transform barra = rt.Find("UI_Slider");
            float larguraBarra = 256f;
            if (barra != null)
            {
                var rb = (RectTransform)barra;
                larguraBarra = rb.rect.width > 1f ? rb.rect.width : larguraBarra;
                rb.anchorMin = rb.anchorMax = new Vector2(1f, 0.5f);
                rb.pivot = new Vector2(1f, 0.5f);
                rb.anchoredPosition = new Vector2(-10f, 0f);
                Esconder(rb, "highlight");
                TMP_Text n = Texto(rb, "Number");
                if (n != null) n.color = CorValor;
            }
            TMP_Text t = Texto(rt, "Text");
            if (t != null)
            {
                Esticar(t.rectTransform, 10f, 0f, -(larguraBarra + 76f), 0f);
                Estilo(t, TamanhoTexto, TextAlignmentOptions.MidlineLeft, false);
            }
            return rt.gameObject;
        }

        private static GameObject ModeloTexto(Transform texto, string nome, float tamanho, bool quebra)
        {
            if (texto == null) throw new InvalidOperationException("texto da linha das Opções ausente");
            RectTransform rt = Copiar(texto, _deposito, nome);
            for (int i = rt.childCount - 1; i >= 0; i--) Object.DestroyImmediate(rt.GetChild(i).gameObject);
            Topo(rt);
            TMP_Text t = rt.GetComponent<TMP_Text>();
            if (t == null) throw new InvalidOperationException("texto das Opções sem TextMeshPro");
            Estilo(t, tamanho, TextAlignmentOptions.MidlineLeft, quebra);
            t.color = CorInfo;
            return rt.gameObject;
        }

        // ---------- Uso (no Update, pelo MenuCoop) ----------

        /// <summary>Começa a saída deslizando para a direita; o Animar apaga o painel no fim (chamado a cada quadro fechado).</summary>
        internal static void Esconder()
        {
            if (_raiz == null || !_raiz.activeSelf || _fechando) return;
            Deslizar(LarguraPainel, true);
        }

        /// <summary>
        /// A cada quadro, no Update do runtime (antes de qualquer retorno antecipado, para a saída terminar até com a Novidades
        /// aberta): anda o deslize de abrir ou fechar; ao fim de fechar, apaga o painel.
        /// </summary>
        internal static void Animar()
        {
            if (!_deslizando || _raiz == null) return;
            float t = Mathf.Clamp01((Time.unscaledTime - _inicioDeslize) / DuracaoDeslize);
            float e = _fechando ? EntradaSaidaRecuo(t) : SaidaRecuo(t);
            MostrarDeslize(_xDe + (_xPara - _xDe) * e);
            if (t < 1f) return;
            _deslizando = false;
            if (_fechando)
            {
                _fechando = false;
                _raiz.SetActive(false);
            }
        }

        private static void Deslizar(float alvo, bool fechar)
        {
            _xDe = _painel.anchoredPosition.x; // parte de onde está, como o DOAnchorPos (reabrir no meio da saída volta dali)
            _xPara = alvo;
            _inicioDeslize = Time.unscaledTime;
            _deslizando = true;
            _fechando = fechar;
        }

        private static void MostrarDeslize(float x)
        {
            _painel.anchoredPosition = new Vector2(x, 0f);
            if (_grupoEscurecer != null) _grupoEscurecer.alpha = Mathf.Clamp01(1f - x / LarguraPainel);
        }

        // Curvas do DOTween (EaseManager), com o recuo de 0,8 que o GenericHUD usa.
        private static float SaidaRecuo(float t)
        {
            t -= 1f;
            return t * t * ((Recuo + 1f) * t + Recuo) + 1f;
        }

        private static float EntradaSaidaRecuo(float t)
        {
            float s = Recuo * 1.525f;
            t *= 2f;
            if (t < 1f) return 0.5f * (t * t * ((s + 1f) * t - s));
            t -= 2f;
            return 0.5f * (t * t * ((s + 1f) * t + s) + 2f);
        }

        /// <summary>Faixa de 128 × 1: transparente à esquerda, sobe suave até 50% de preto em <paramref name="rampa"/> e fica cheia.</summary>
        private static Texture2D TexturaDegrade(float rampa)
        {
            const int largura = 128;
            var tex = new Texture2D(largura, 1, TextureFormat.RGBA32, false)
            {
                name = "HellClockCoop.Degrade",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            for (int i = 0; i < largura; i++)
            {
                float x = (i + 0.5f) / largura;
                float a = rampa > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x / rampa)) : 1f;
                tex.SetPixel(i, 0, new Color(0f, 0f, 0f, 0.5f * a)); // o jogo mistura em espaço linear: 0,5 como o escurecer antigo
            }
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// Mostra o botão "Menu do coop" com a interface do jogo a partir de (x, y) em pixels da tela (coordenadas do OnGUI)
        /// e devolve a área dele, para o clique. Falso: interface do jogo indisponível, o MenuCoop desenha o botão simples.
        /// Chamado no OnGUI a cada quadro em que o botão deve aparecer; sem chamada, o EsconderBotaoParado o apaga.
        /// </summary>
        internal static bool BotaoDoMenu(float x, float y, string texto, out Rect area)
        {
            area = default(Rect);
            if (_desativado) return false;
            if (_raiz == null)
            {
                if (Time.unscaledTime < _proximaTentativa) return false; // sem a janela das Opções: tenta de novo a cada 2 s
                _proximaTentativa = Time.unscaledTime + 2f;
                if (!Preparar()) return false;
            }
            if (_botao == null) return false;
            try
            {
                float escala = Screen.height / 1080f; // escala do canvas (pela altura)
                if (escala <= 0f) return false;
                float alturaPx = Mathf.Max(44f, AlturaAba * escala); // alvo de 44 px reais também abaixo de 1080p
                area = new Rect(x, y, LarguraBotao * escala, alturaPx);
                _botao.anchoredPosition = new Vector2(x / escala, -y / escala);
                _botao.sizeDelta = new Vector2(LarguraBotao, alturaPx / escala);
                bool sob = area.Contains(Event.current.mousePosition);
                if (_botaoTexto != null)
                {
                    if (_botaoTexto.text != texto) _botaoTexto.text = texto;
                    _botaoTexto.color = sob ? Color.white : new Color(1f, 1f, 1f, 0.8f);
                }
                if (_botaoDestaque != null && _botaoDestaque.activeSelf != sob) _botaoDestaque.SetActive(sob);
                if (!_botaoRaiz.activeSelf) _botaoRaiz.SetActive(true);
                _botaoQuadro = Time.frameCount;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Menu] Botão do menu com a interface do jogo falhou; volta ao botão simples: {e}");
                Desativar();
                return false;
            }
        }

        /// <summary>No Update: apaga o botão do menu se o OnGUI parou de pedi-lo (menu aberto, run, outra janela).</summary>
        internal static void EsconderBotaoParado()
        {
            if (_botaoRaiz != null && _botaoRaiz.activeSelf && Time.frameCount - _botaoQuadro > 1) _botaoRaiz.SetActive(false);
        }

        internal static void ZerarRolagem() => _rolagem = 0f;

        internal static void Rolar(float delta) => _rolagem += delta;

        internal static void Mostrar(List<MenuCoop.Linha> linhas, int foco, int aba, string rodape, bool escurecer, ref bool rolarParaFoco)
        {
            if (_raiz == null) throw new InvalidOperationException("painel não montado");
            if (!_raiz.activeSelf)
            {
                MostrarDeslize(LarguraPainel); // entra de fora da tela, pela direita
                _raiz.SetActive(true);
                Deslizar(0f, false);
            }
            else if (_fechando) Deslizar(0f, false); // reaberto no meio da saída
            if (_escurecer.activeSelf != escurecer) _escurecer.SetActive(escurecer); // só no menu do jogo; em jogo, a tela fica limpa

            for (int i = 0; i < Abas.Count; i++)
            {
                bool ativa = i == aba;
                if (Abas[i].Destaque != null && Abas[i].Destaque.activeSelf != ativa) Abas[i].Destaque.SetActive(ativa);
                if (Abas[i].Texto != null) Abas[i].Texto.color = ativa ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            }
            _rodape.text = rodape;

            float escala = Screen.height / 1080f; // escala do canvas (pela altura)
            float alturaItem = Mathf.Max(AlturaMinimaItem, escala > 0f ? 44f / escala : AlturaMinimaItem); // alvo de 44 px reais
            float largura = _lista.rect.width > 1f ? _lista.rect.width : LarguraPainel - 120f;

            float y = 0f, topoFoco = -1f, alturaFoco = 0f;
            for (int i = 0; i < linhas.Count; i++)
            {
                MenuCoop.Linha l = linhas[i];
                Visual v = Obter(i, l.Tipo);
                v.Acao = l.Tipo == MenuCoop.Tipo.Secao || l.Tipo == MenuCoop.Tipo.Info ? MenuCoop.Acao.Nenhuma : l.Acao;
                float altura;
                if (l.Tipo == MenuCoop.Tipo.Secao)
                {
                    if (i > 0) y += 10f;
                    altura = AlturaSecao;
                    v.Texto.text = l.Texto;
                }
                else if (l.Tipo == MenuCoop.Tipo.Info)
                {
                    v.Texto.text = l.Texto;
                    v.Texto.color = l.Enfase == MenuCoop.Enfase.Titulo ? CorFoco : l.Enfase == MenuCoop.Enfase.Rotulo ? CorValor : CorInfo;
                    if (!ReferenceEquals(v.Medido, l.Texto) || !Mathf.Approximately(v.LarguraMedida, largura))
                    {
                        v.Medido = l.Texto;
                        v.LarguraMedida = largura;
                        v.AlturaMedida = Mathf.Max(30f, v.Texto.GetPreferredValues(l.Texto, largura - 4f, 0f).y + 8f);
                    }
                    altura = v.AlturaMedida;
                }
                else
                {
                    bool comDetalhe = l.Tipo == MenuCoop.Tipo.Item && !string.IsNullOrEmpty(l.Detalhe);
                    altura = comDetalhe ? alturaItem + AlturaDetalhe : alturaItem;
                    MostrarDetalhe(v, comDetalhe ? l.Detalhe : null);
                    bool focado = i == foco;
                    if (focado) { topoFoco = y; alturaFoco = altura; }
                    if (v.Destaque != null && v.Destaque.activeSelf != focado) v.Destaque.SetActive(focado);
                    v.Texto.text = l.Texto;
                    v.Texto.color = !l.Habilitada ? new Color(CorTexto.r, CorTexto.g, CorTexto.b, 0.45f) : focado ? CorFoco : CorTexto;
                    if (l.Tipo == MenuCoop.Tipo.Item) MostrarValor(v, l);
                    else if (l.Tipo == MenuCoop.Tipo.Interruptor)
                    {
                        if (v.Marca != null && v.Marca.activeSelf != l.Ligado) v.Marca.SetActive(l.Ligado);
                    }
                    else MostrarBarra(v, l);
                }
                v.Rt.anchoredPosition = new Vector2(0f, -y);
                v.Rt.sizeDelta = new Vector2(0f, altura);
                y += altura + (l.Tipo == MenuCoop.Tipo.Info ? 2f : 8f);
            }
            for (int i = linhas.Count; i < Visuais.Count; i++)
            {
                if (Visuais[i].Rt.gameObject.activeSelf) Visuais[i].Rt.gameObject.SetActive(false);
            }
            _mostradas = linhas.Count;

            // Rolagem: segue o foco do teclado/controle e a roda do mouse; presa ao tamanho da lista.
            // 0.24.0: a coluna tem a altura do canvas (sempre 1080, escala pela altura); a conta não depende do rect, que no
            // primeiro quadro do canvas recém-ligado ainda não tem o tamanho certo. 24 = recuo do Recorte (12 em cima e embaixo).
            const float janela = 1080f - BaseConteudo + TopoConteudo - 24f;
            if (rolarParaFoco && topoFoco >= 0f && janela > 1f)
            {
                if (topoFoco < _rolagem) _rolagem = topoFoco;
                else if (topoFoco + alturaFoco > _rolagem + janela) _rolagem = topoFoco + alturaFoco - janela;
                rolarParaFoco = false;
            }
            _rolagem = Mathf.Clamp(_rolagem, 0f, Mathf.Max(0f, y - janela));
            _lista.sizeDelta = new Vector2(_lista.sizeDelta.x, y);
            _lista.anchoredPosition = new Vector2(0f, _rolagem);
        }

        private static void MostrarValor(Visual v, MenuCoop.Linha l)
        {
            bool temValor = !string.IsNullOrEmpty(l.Valor);
            if (v.Valor == null) return;
            if (v.Valor.gameObject.activeSelf != temValor) v.Valor.gameObject.SetActive(temValor);
            if (temValor) v.Valor.text = l.Valor;
            v.Texto.rectTransform.offsetMax = new Vector2(temValor ? -(LarguraValor + 16f) : -10f, 0f);
        }

        /// <summary>
        /// 0.21.0: segunda linha do item. Com detalhe, título e valor sobem para a faixa de cima (a de baixo é do detalhe);
        /// sem detalhe, voltam a ocupar a altura toda (o Visual é reaproveitado entre abas).
        /// </summary>
        private static void MostrarDetalhe(Visual v, string detalhe)
        {
            bool tem = detalhe != null;
            if (v.Detalhe != null)
            {
                if (v.Detalhe.gameObject.activeSelf != tem) v.Detalhe.gameObject.SetActive(tem);
                if (tem) v.Detalhe.text = detalhe;
            }
            float baixo = tem && v.Detalhe != null ? AlturaDetalhe : 0f;
            if (v.Texto != null) v.Texto.rectTransform.offsetMin = new Vector2(v.Texto.rectTransform.offsetMin.x, baixo);
            if (v.Valor != null && v.Tipo == MenuCoop.Tipo.Item) v.Valor.rectTransform.offsetMin = new Vector2(v.Valor.rectTransform.offsetMin.x, baixo);
        }

        private static void MostrarBarra(Visual v, MenuCoop.Linha l)
        {
            float f = Mathf.Clamp01(l.Fracao);
            if (v.Preenche != null)
            {
                v.Preenche.anchorMin = new Vector2(0f, v.Preenche.anchorMin.y);
                v.Preenche.anchorMax = new Vector2(f, v.Preenche.anchorMax.y);
            }
            if (v.Alca != null)
            {
                v.Alca.anchorMin = new Vector2(f, v.Alca.anchorMin.y);
                v.Alca.anchorMax = new Vector2(f, v.Alca.anchorMax.y);
            }
            if (v.Valor != null) v.Valor.text = l.Valor;
        }

        /// <summary>Visual da linha i, criado ou trocado quando o tipo muda (ex.: trocou de aba).</summary>
        private static Visual Obter(int i, MenuCoop.Tipo tipo)
        {
            if (i < Visuais.Count && Visuais[i].Tipo != tipo)
            {
                Object.Destroy(Visuais[i].Rt.gameObject);
                Visuais[i] = Criar(tipo);
            }
            else if (i >= Visuais.Count) Visuais.Add(Criar(tipo));
            Visual v = Visuais[i];
            if (!v.Rt.gameObject.activeSelf) v.Rt.gameObject.SetActive(true);
            return v;
        }

        private static Visual Criar(MenuCoop.Tipo tipo)
        {
            GameObject modelo = tipo == MenuCoop.Tipo.Secao ? _mSecao
                : tipo == MenuCoop.Tipo.Info ? _mInfo
                : tipo == MenuCoop.Tipo.Deslizante ? _mDeslizante : _mLinha;
            GameObject go = Object.Instantiate(modelo, _lista, false);
            go.SetActive(true);
            var v = new Visual { Tipo = tipo, Rt = (RectTransform)go.transform };
            switch (tipo)
            {
                case MenuCoop.Tipo.Secao:
                    v.Texto = Texto(v.Rt, "TextLabel");
                    break;
                case MenuCoop.Tipo.Info:
                    v.Texto = go.GetComponent<TMP_Text>();
                    break;
                case MenuCoop.Tipo.Deslizante:
                    v.Texto = Texto(v.Rt, "Text");
                    v.Destaque = Filho(v.Rt, "highlight");
                    v.Barra = v.Rt.Find("UI_Slider/Handle Slide Area") as RectTransform ?? v.Rt.Find("UI_Slider") as RectTransform;
                    v.Preenche = v.Rt.Find("UI_Slider/Fill Area/Fill") as RectTransform;
                    v.Alca = v.Rt.Find("UI_Slider/Handle Slide Area/Handle") as RectTransform;
                    v.Valor = Texto(v.Rt, "UI_Slider/Number");
                    break;
                default:
                    v.Texto = Texto(v.Rt, "Text");
                    v.Valor = Texto(v.Rt, "Valor");
                    v.Detalhe = Texto(v.Rt, "Detalhe");
                    v.Destaque = Filho(v.Rt, "highlight");
                    v.Caixa = Filho(v.Rt, "UI_ToggleBox");
                    v.Marca = Filho(v.Rt, "UI_ToggleBox/Checkmark");
                    bool interruptor = tipo == MenuCoop.Tipo.Interruptor;
                    if (v.Caixa != null) v.Caixa.SetActive(interruptor);
                    if (v.Valor != null) v.Valor.gameObject.SetActive(!interruptor);
                    break;
            }
            if (v.Texto == null) throw new InvalidOperationException("linha do painel sem texto (" + tipo + ")");
            if (v.Destaque != null) v.Destaque.SetActive(false);
            return v;
        }

        // ---------- Mouse (posição em pixels da tela, origem embaixo à esquerda) ----------

        internal static bool DentroDoPainel(Vector2 tela) =>
            _raiz != null && _raiz.activeSelf && !_fechando && RectTransformUtility.RectangleContainsScreenPoint(_painel, tela, null);

        internal static bool FecharSob(Vector2 tela) =>
            DentroDoPainel(tela) && _fechar != null && RectTransformUtility.RectangleContainsScreenPoint(_fechar, tela, null);

        internal static int AbaSob(Vector2 tela)
        {
            if (!DentroDoPainel(tela)) return -1;
            for (int i = 0; i < Abas.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(Abas[i].Rt, tela, null)) return i;
            }
            return -1;
        }

        /// <summary>Ação da linha desenhada sob o mouse (Nenhuma fora da lista, em seção ou em informação).</summary>
        internal static MenuCoop.Acao AcaoSob(Vector2 tela)
        {
            if (!DentroDoPainel(tela) || !RectTransformUtility.RectangleContainsScreenPoint(_viewport, tela, null)) return MenuCoop.Acao.Nenhuma;
            for (int i = 0; i < _mostradas && i < Visuais.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(Visuais[i].Rt, tela, null)) return Visuais[i].Acao;
            }
            return MenuCoop.Acao.Nenhuma;
        }

        /// <summary>O mouse está sobre a barra do deslizante (com folga na vertical), e não no rótulo.</summary>
        internal static bool NaBarra(MenuCoop.Acao acao, Vector2 tela)
        {
            RectTransform barra = BarraDe(acao);
            if (barra == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(barra, tela, null, out Vector2 local)) return false;
            Rect r = barra.rect;
            return local.x >= r.xMin - 8f && local.x <= r.xMax + 8f && local.y >= r.yMin - 10f && local.y <= r.yMax + 10f;
        }

        /// <summary>Posição do mouse ao longo da barra do deslizante (0 = começo, 1 = fim), presa às pontas durante o arrasto.</summary>
        internal static bool FracaoNoDeslizante(MenuCoop.Acao acao, Vector2 tela, out float fracao)
        {
            fracao = 0f;
            RectTransform barra = BarraDe(acao);
            if (barra == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(barra, tela, null, out Vector2 local)) return false;
            Rect r = barra.rect;
            if (r.width <= 1f) return false;
            fracao = Mathf.Clamp01((local.x - r.xMin) / r.width);
            return true;
        }

        private static RectTransform BarraDe(MenuCoop.Acao acao)
        {
            if (acao == MenuCoop.Acao.Nenhuma) return null;
            for (int i = 0; i < _mostradas && i < Visuais.Count; i++)
            {
                if (Visuais[i].Acao == acao && Visuais[i].Tipo == MenuCoop.Tipo.Deslizante) return Visuais[i].Barra;
            }
            return null;
        }

        // ---------- Cópia e limpeza ----------

        /// <summary>Canvas próprio, inativo: sobreposição na ordem dada e a escala do jogo (1920x1080, pela altura).</summary>
        private static GameObject NovoCanvas(string nome, int ordem)
        {
            var go = new GameObject(nome);
            Object.DontDestroyOnLoad(go);
            go.SetActive(false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = ordem;
            CanvasScaler escala = go.AddComponent<CanvasScaler>();
            escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            escala.referenceResolution = new Vector2(1920f, 1080f);
            escala.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            escala.matchWidthOrHeight = 1f; // como o jogo: pela altura (ultrawide ganha largura, não escala)
            return go;
        }

        /// <summary>Copia uma peça do jogo para o depósito inativo, tira os scripts e só então a põe no destino.</summary>
        private static RectTransform Copiar(Transform modelo, Transform destino, string nome)
        {
            GameObject go = Object.Instantiate(modelo.gameObject, _deposito, false);
            go.name = nome;
            Limpar(go);
            foreach (CanvasGroup g in go.GetComponentsInChildren<CanvasGroup>(true)) g.alpha = 1f; // a animação do jogo pode ter deixado em 0
            go.SetActive(true); // inativo no depósito; ativo de verdade ao entrar no destino
            go.transform.SetParent(destino, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Fica só o que desenha: imagens, textos (TMP e o RTL do jogo, que é um TMP), máscaras e CanvasGroup.</summary>
        internal static void Limpar(GameObject go) // também usado pelo AliadosNaTela (0.14.0)
        {
            // 1ª passada: scripts do jogo e de terceiros (dependem dos componentes da Unity); 2ª: o resto (botões, layouts, Animator).
            for (int passada = 0; passada < 2; passada++)
            {
                foreach (Component c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || Manter(c)) continue;
                    bool doJogo = c is MonoBehaviour && !(c is UnityEngine.EventSystems.UIBehaviour);
                    if (passada == 0 && !doJogo) continue;
                    Object.DestroyImmediate(c);
                }
            }
        }

        private static bool Manter(Component c) =>
            c is Transform || c is CanvasRenderer || c is CanvasGroup || c is Graphic || c is Mask || c is RectMask2D;

        // ---------- Utilitários ----------

        private static Transform Procurar(Transform t, string nome)
        {
            if (t.name == nome) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform achado = Procurar(t.GetChild(i), nome);
                if (achado != null) return achado;
            }
            return null;
        }

        private static GameObject Filho(Transform t, string caminho)
        {
            Transform f = t.Find(caminho);
            return f != null ? f.gameObject : null;
        }

        private static TMP_Text Texto(Transform t, string caminho)
        {
            Transform f = t.Find(caminho);
            return f != null ? f.GetComponent<TMP_Text>() : null;
        }

        /// <summary>
        /// As linhas do cabeçalho das Opções são da largura da janela do jogo, maior que a nossa, e vazavam pelos lados:
        /// toda imagem do cabeçalho (fora o [X]) que passa da largura do painel é encurtada até caber. As contas são no
        /// espaço do pai de cada peça, então peças espelhadas (escala negativa) e com pivô fora do centro também servem.
        /// </summary>
        private static void CaberNaJanela(RectTransform cab)
        {
            var cantos = new Vector3[4];
            Rect lim = _painel.rect;
            var ajustadas = new List<string>();
            foreach (Image img in cab.GetComponentsInChildren<Image>(true))
            {
                RectTransform r = img.rectTransform;
                if (r == cab || (_fechar != null && r.IsChildOf(_fechar))) continue;
                Transform pai = r.parent;
                float s = r.localScale.x;
                if (pai == null || Mathf.Abs(s) < 0.001f) continue;
                float a = pai.InverseTransformPoint(_painel.TransformPoint(new Vector3(lim.xMin, 0f, 0f))).x;
                float b = pai.InverseTransformPoint(_painel.TransformPoint(new Vector3(lim.xMax, 0f, 0f))).x;
                r.GetWorldCorners(cantos);
                float c0 = pai.InverseTransformPoint(cantos[0]).x, c2 = pai.InverseTransformPoint(cantos[2]).x;
                float min = Mathf.Min(c0, c2), max = Mathf.Max(c0, c2);
                float novoMin = Mathf.Max(min, Mathf.Min(a, b)), novoMax = Mathf.Min(max, Mathf.Max(a, b));
                if (novoMin - min < 0.5f && max - novoMax < 0.5f) continue; // já cabe
                if (novoMax - novoMin < 1f) { r.gameObject.SetActive(false); ajustadas.Add(r.name + " (escondida)"); continue; }
                // Nova largura e novo centro (no espaço do pai); a mudança de largura desloca o centro conforme o pivô.
                float dL = (novoMax - novoMin - (max - min)) / Mathf.Abs(s);
                float dC = (novoMin + novoMax - min - max) / 2f;
                r.sizeDelta = new Vector2(r.sizeDelta.x + dL, r.sizeDelta.y);
                r.anchoredPosition = new Vector2(r.anchoredPosition.x + dC - (0.5f - r.pivot.x) * dL * s, r.anchoredPosition.y);
                ajustadas.Add($"{r.name} {max - min:0}→{novoMax - novoMin:0}");
            }
            if (ajustadas.Count > 0)
                Plugin.Log.LogInfo($"[Menu] Cabeçalho cabendo na janela ({lim.width:0}): {string.Join(", ", ajustadas.ToArray())}.");
        }

        private static void Esconder(Transform t, string caminho)
        {
            Transform f = t.Find(caminho);
            if (f != null) f.gameObject.SetActive(false);
        }

        private static void Esticar(RectTransform rt, float esq, float baixo, float dir, float cima)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(esq, baixo);
            rt.offsetMax = new Vector2(dir, cima);
        }

        /// <summary>Linha da lista: presa ao topo, largura toda; a altura e a posição vêm do Mostrar.</summary>
        private static void Topo(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, AlturaMinimaItem);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Destaque da linha focada (a faixa das Opções), um pouco maior que a linha.</summary>
        private static void Destaque(RectTransform linha)
        {
            Transform d = linha.Find("highlight");
            if (d == null) return;
            Esticar((RectTransform)d, -12f, -5f, 12f, 5f);
            // 0.23.0: a faixa de foco ficava com a largura da linha da tela de Opções (mais estreita que a janela larga da
            // 0.21.0): o que desenha a faixa pode ser um filho com largura fixa ou uma imagem que guarda a proporção. Estica
            // na horizontal tudo dentro do highlight e solta a proporção; a altura de cada peça fica como o jogo fez.
            foreach (RectTransform f in d.GetComponentsInChildren<RectTransform>(true))
            {
                if (f == d) continue;
                f.anchorMin = new Vector2(0f, f.anchorMin.y);
                f.anchorMax = new Vector2(1f, f.anchorMax.y);
                f.offsetMin = new Vector2(0f, f.offsetMin.y);
                f.offsetMax = new Vector2(0f, f.offsetMax.y);
            }
            foreach (Image img in d.GetComponentsInChildren<Image>(true)) img.preserveAspect = false;
            foreach (AspectRatioFitter a in d.GetComponentsInChildren<AspectRatioFitter>(true)) a.enabled = false;
            foreach (LayoutElement e in d.GetComponentsInChildren<LayoutElement>(true)) e.ignoreLayout = true;
            d.gameObject.SetActive(false);
        }

        private static void Estilo(TMP_Text t, float tamanho, TextAlignmentOptions alinhamento, bool quebra)
        {
            t.enableAutoSizing = false;
            t.fontSize = tamanho;
            t.alignment = alinhamento;
            t.textWrappingMode = quebra ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = quebra ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
        }
    }
}
