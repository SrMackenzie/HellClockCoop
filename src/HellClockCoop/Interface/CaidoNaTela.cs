using System;
using System.Collections.Generic;
using DG.Tweening;
using HellClock.PostProcessing;
using HellClockCoop.Fase2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Jogador caído na tela (0.15.0, opção B "Segunda chance", escolhida pelo autor em 22/11/2026):
    /// - Quem caiu: tela em tons de cinza com o efeito do próprio jogo (CameraPostEffectController.LerpToGrayscale, o
    ///   mesmo da segunda chance) e faixa escura no terço de baixo: "VOCÊ CAIU", a volta (próximo andar, com X% da
    ///   vida) e um ponto por jogador (cheio = de pé, apagado = caído) com "N de M de pé".
    /// - Queda de um aliado: faixa curta no topo, ao centro (2,5 s): "Fulano caiu  -10 s" (o tempo só no host, que é
    ///   quem tira). Nada no canto de cima à esquerda (decisão do autor).
    /// - Textos na fonte da barra flutuante do jogo (AliadosNaTela.Fonte), numa camada própria por cima da HUD. Sem a
    ///   fonte do jogo (barra ainda não montada) ou com erro: reserva no OnGUI, como o visual antigo.
    /// - CameraPostEffectController.Instance é Guaranteed (criaria um): o efeito é achado na cena.
    /// - Só informação: nenhum clique, nada pausa o jogo.
    /// - Reanimação (0.16.0, opção B): com um aliado ao seu lado, a linha da volta vira "Fulano está te reanimando"
    ///   ("Fulano está ao seu lado" se ele se mexe) e uma barra 360x12 enche em verde (vermelha quando ele é golpeado),
    ///   sobre um trilho âmbar 22%. Ao terminar, o aviso do topo sai em âmbar: "Fulano te levantou", "Você levantou
    ///   Fulano" ou "Fulano levantou Beltrano".
    /// </summary>
    internal static class CaidoNaTela
    {
        private const float DuracaoAviso = 2.5f, SumirAviso = 0.5f;
        private static readonly Color Vermelho = new Color(0.70f, 0.15f, 0.12f);   // #B3261E
        private static readonly Color Ambar = new Color(0.85f, 0.64f, 0.25f);      // #D9A441
        private static readonly Color Fundo = new Color(0f, 0f, 0f, 0.62f);

        private static GameObject _raiz;
        private static RectTransform _faixa, _pontos, _barra, _barraEnche;
        private static Image _barraCor;
        private static TMP_Text _titulo, _volta, _contagem, _aviso;
        private static readonly List<Image> Pontos = new List<Image>();
        private static bool _comErro, _cinzaAplicado;
        private static string _textoAviso;
        private static Color _corAviso = Vermelho;
        private static float _avisoAte = -1f;
        private static GUIStyle _estiloTitulo, _estiloLinha, _estiloAviso;

        private static bool Nativo => !_comErro && _raiz != null;

        // ---------------- Eventos ----------------

        internal static void AoCair()
        {
            Cinza(true);
        }

        internal static void AoLevantar()
        {
            Cinza(false);
            if (_faixa != null) _faixa.gameObject.SetActive(false);
        }

        /// <summary>Aviso de queda de um aliado; <paramref name="segundos"/> &gt; 0 só no host (quem tira o tempo).</summary>
        internal static void AvisarQueda(string nome, float segundos)
        {
            _textoAviso = segundos > 0f ? $"{nome} caiu  -{segundos:0} s" : $"{nome} caiu";
            _corAviso = Vermelho;
            _avisoAte = Time.unscaledTime + DuracaoAviso;
        }

        /// <summary>Aviso de reanimação concluída (0.16.0), em âmbar: notícia boa.</summary>
        internal static void AvisarReanimacao(string texto)
        {
            _textoAviso = texto;
            _corAviso = Ambar;
            _avisoAte = Time.unscaledTime + DuracaoAviso;
        }

        /// <summary>Linha da faixa: quem está te reanimando, ou a volta no próximo andar.</summary>
        private static string TextoLinha(bool reanimando, Reanimacao.Progresso p) =>
            !reanimando ? TextoVolta()
            : p.Estado == Reanimacao.Estado.FiqueParado ? $"{p.Quem} está ao seu lado" : $"{p.Quem} está te reanimando";

        private static Color CorBarra(Reanimacao.Estado e) =>
            e == Reanimacao.Estado.Interrompido ? AliadosNaTela.VermelhoInterrompido : AliadosNaTela.VerdeReanimar;

        // ---------------- Laço (LateUpdate do CoopRuntime) ----------------

        internal static void Atualizar()
        {
            if (_comErro) return;
            try
            {
                bool faixa = JogadorCaido.Caido;
                bool aviso = Time.unscaledTime < _avisoAte;
                if (!faixa && !aviso && _raiz == null) return;
                if (_raiz == null && !Montar()) return;
                AtualizarFaixa(faixa);
                AtualizarAviso(aviso);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Erro na faixa do jogador caído; usando o desenho simples: {e}");
                _comErro = true;
                if (_raiz != null) Object.Destroy(_raiz);
                _raiz = null;
            }
        }

        private static void AtualizarFaixa(bool ligada)
        {
            if (_faixa.gameObject.activeSelf != ligada) _faixa.gameObject.SetActive(ligada);
            if (!ligada) return;
            bool reanimando = Reanimacao.MeuProgresso(out Reanimacao.Progresso pr);
            string linha = TextoLinha(reanimando, pr);
            if (_volta.text != linha) _volta.text = linha;
            if (_barra.gameObject.activeSelf != reanimando) _barra.gameObject.SetActive(reanimando);
            if (reanimando)
            {
                _barraEnche.anchorMax = new Vector2(pr.Fracao, 1f);
                _barraCor.color = CorBarra(pr.Estado);
            }
            JogadorCaido.Contar(out int dePe, out int total);
            _contagem.text = $"{dePe} de {total} de pé";
            while (Pontos.Count < total) Pontos.Add(NovoPonto());
            for (int i = 0; i < Pontos.Count; i++)
            {
                Image p = Pontos[i];
                p.gameObject.SetActive(i < total);
                p.color = i < dePe ? Ambar : new Color(Ambar.r, Ambar.g, Ambar.b, 0.22f);
            }
        }

        private static void AtualizarAviso(bool ligado)
        {
            if (_aviso.gameObject.activeSelf != ligado) _aviso.gameObject.SetActive(ligado);
            if (!ligado) return;
            if (_aviso.text != _textoAviso) _aviso.text = _textoAviso;
            _aviso.color = _corAviso;
            float resta = _avisoAte - Time.unscaledTime;
            _aviso.alpha = Mathf.Clamp01(resta / SumirAviso);
        }

        private static string TextoVolta() => $"Volta no próximo andar, com {JogadorCaido.FracaoAoVoltar * 100f:0}% da vida";

        // ---------------- Montagem ----------------

        private static bool Montar()
        {
            if (AliadosNaTela.Fonte == null) return false; // a reserva do OnGUI desenha até a fonte do jogo aparecer
            _raiz = new GameObject("HellClockCoop.Caido", typeof(RectTransform));
            Object.DontDestroyOnLoad(_raiz);
            Canvas c = _raiz.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 50; // por cima da HUD do jogo, abaixo das janelas do coop (OnGUI)
            CanvasScaler e = _raiz.AddComponent<CanvasScaler>();
            e.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            e.referenceResolution = new Vector2(1920f, 1080f);
            e.matchWidthOrHeight = 1f;

            _faixa = Caixa("Faixa", _raiz.transform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(760f, 168f));
            Image fundo = _faixa.gameObject.AddComponent<Image>();
            fundo.color = Fundo;
            fundo.raycastTarget = false;
            _titulo = Texto("Titulo", _faixa, new Vector2(0f, 52f), 46f, Vermelho, FontStyles.Bold);
            _titulo.text = "VOCÊ CAIU";
            _volta = Texto("Volta", _faixa, new Vector2(0f, 2f), 25f, Color.white, FontStyles.Normal);
            // Barra da reanimação (0.16.0): trilho âmbar 22%, preenchimento pela âncora (Image sem sprite não corta).
            _barra = Caixa("Reanimacao", _faixa, new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(360f, 12f));
            _barra.pivot = new Vector2(0.5f, 0.5f);
            Image trilho = _barra.gameObject.AddComponent<Image>();
            trilho.color = new Color(Ambar.r, Ambar.g, Ambar.b, 0.22f);
            trilho.raycastTarget = false;
            var enche = new GameObject("Enche", typeof(RectTransform));
            _barraEnche = (RectTransform)enche.transform;
            _barraEnche.SetParent(_barra, false);
            _barraEnche.anchorMin = Vector2.zero;
            _barraEnche.anchorMax = new Vector2(0f, 1f);
            _barraEnche.offsetMin = _barraEnche.offsetMax = Vector2.zero;
            _barraCor = enche.AddComponent<Image>();
            _barraCor.raycastTarget = false;
            _barra.gameObject.SetActive(false);
            _pontos = Caixa("Pontos", _faixa, new Vector2(0.5f, 0.5f), new Vector2(-150f, -48f), new Vector2(120f, 18f));
            HorizontalLayoutGroup fila = _pontos.gameObject.AddComponent<HorizontalLayoutGroup>();
            fila.spacing = 10f;
            fila.childAlignment = TextAnchor.MiddleRight;
            fila.childControlWidth = fila.childControlHeight = false;
            fila.childForceExpandWidth = fila.childForceExpandHeight = false;
            _contagem = Texto("Contagem", _faixa, new Vector2(70f, -48f), 23f, Ambar, FontStyles.Normal);
            _contagem.alignment = TextAlignmentOptions.Left;
            _contagem.rectTransform.sizeDelta = new Vector2(300f, 34f);
            _faixa.gameObject.SetActive(false);

            RectTransform topo = Caixa("Aviso", _raiz.transform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(900f, 50f));
            _aviso = Texto("Texto", topo, Vector2.zero, 34f, Vermelho, FontStyles.Bold);
            _aviso.outlineWidth = 0.2f;
            _aviso.outlineColor = Color.black;
            _aviso.gameObject.SetActive(false);
            Plugin.Log.LogInfo("[Caído] Faixa do jogador caído montada com a fonte do jogo.");
            return true;
        }

        private static RectTransform Caixa(string nome, Transform pai, Vector2 ancora, Vector2 posicao, Vector2 tamanho)
        {
            var go = new GameObject(nome, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(pai, false);
            rt.anchorMin = rt.anchorMax = ancora;
            rt.pivot = new Vector2(0.5f, ancora.y);
            rt.anchoredPosition = posicao;
            rt.sizeDelta = tamanho;
            return rt;
        }

        private static TMP_Text Texto(string nome, Transform pai, Vector2 posicao, float tamanho, Color cor, FontStyles estilo)
        {
            RectTransform rt = Caixa(nome, pai, new Vector2(0.5f, 0.5f), posicao, new Vector2(740f, tamanho + 10f));
            rt.pivot = new Vector2(0.5f, 0.5f);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = AliadosNaTela.Fonte;
            if (AliadosNaTela.MaterialFonte != null) t.fontSharedMaterial = AliadosNaTela.MaterialFonte;
            t.fontSize = tamanho;
            t.color = cor;
            t.fontStyle = estilo;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.richText = false; // nomes da Steam com <tags> saem como texto
            return t;
        }

        private static Image NovoPonto()
        {
            var go = new GameObject("Ponto", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_pontos, false);
            rt.sizeDelta = new Vector2(14f, 14f);
            Image i = go.AddComponent<Image>();
            i.raycastTarget = false;
            return i;
        }

        // ---------------- Efeito de cinza do jogo ----------------

        private static void Cinza(bool ligar)
        {
            if (ligar == _cinzaAplicado) return; // só desfaz o que o mod aplicou
            try
            {
                CameraPostEffectController efeito = Object.FindAnyObjectByType<CameraPostEffectController>();
                if (efeito == null) { _cinzaAplicado = false; return; }
                efeito.LerpToGrayscale(ligar ? 1f : 0f, ligar ? 0.5f : 0.25f, Ease.OutQuad);
                _cinzaAplicado = ligar;
            }
            catch (Exception e)
            {
                _cinzaAplicado = false;
                Plugin.Log.LogWarning($"[Caído] Efeito de cinza do jogo indisponível: {e.Message}");
            }
        }

        // ---------------- Reserva (OnGUI) ----------------

        /// <summary>OnGUI do CoopRuntime: faixa e aviso desenhados à mão quando a camada com a fonte do jogo não montou.</summary>
        internal static void DesenharReserva()
        {
            if (Nativo || Event.current.type != EventType.Repaint) return;
            bool faixa = JogadorCaido.Caido;
            bool aviso = Time.unscaledTime < _avisoAte;
            if (!faixa && !aviso) return;
            if (_estiloTitulo == null)
            {
                _estiloTitulo = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 30, fontStyle = FontStyle.Bold };
                _estiloLinha = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
                _estiloAviso = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 24, fontStyle = FontStyle.Bold };
            }
            float k = Screen.height / 1080f;
            if (faixa)
            {
                var r = new Rect(Screen.width / 2f - 380f * k, Screen.height - (150f + 168f) * k, 760f * k, 168f * k);
                GUI.color = Fundo;
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Vermelho;
                GUI.Label(new Rect(r.x, r.y + 12f * k, r.width, 50f * k), "VOCÊ CAIU", _estiloTitulo);
                GUI.color = Color.white;
                bool reanimando = Reanimacao.MeuProgresso(out Reanimacao.Progresso pr);
                GUI.Label(new Rect(r.x, r.y + 64f * k, r.width, 32f * k), TextoLinha(reanimando, pr), _estiloLinha);
                if (reanimando)
                {
                    var trilho = new Rect(r.x + r.width / 2f - 180f * k, r.y + 98f * k, 360f * k, 12f * k);
                    GUI.color = new Color(Ambar.r, Ambar.g, Ambar.b, 0.22f);
                    GUI.DrawTexture(trilho, Texture2D.whiteTexture);
                    GUI.color = CorBarra(pr.Estado);
                    GUI.DrawTexture(new Rect(trilho.x, trilho.y, trilho.width * pr.Fracao, trilho.height), Texture2D.whiteTexture);
                }
                JogadorCaido.Contar(out int dePe, out int total);
                GUI.color = Ambar;
                GUI.Label(new Rect(r.x, r.y + 112f * k, r.width, 32f * k), $"{dePe} de {total} de pé", _estiloLinha);
                GUI.color = Color.white;
            }
            if (aviso)
            {
                GUI.color = new Color(_corAviso.r, _corAviso.g, _corAviso.b, Mathf.Clamp01((_avisoAte - Time.unscaledTime) / SumirAviso));
                GUI.Label(new Rect(Screen.width / 2f - 450f * k, 125f * k, 900f * k, 50f * k), _textoAviso, _estiloAviso);
                GUI.color = Color.white;
            }
        }
    }
}
