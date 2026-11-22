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
    /// </summary>
    internal static class CaidoNaTela
    {
        private const float DuracaoAviso = 2.5f, SumirAviso = 0.5f;
        private static readonly Color Vermelho = new Color(0.70f, 0.15f, 0.12f);   // #B3261E
        private static readonly Color Ambar = new Color(0.85f, 0.64f, 0.25f);      // #D9A441
        private static readonly Color Fundo = new Color(0f, 0f, 0f, 0.62f);

        private static GameObject _raiz;
        private static RectTransform _faixa, _pontos;
        private static TMP_Text _titulo, _volta, _contagem, _aviso;
        private static readonly List<Image> Pontos = new List<Image>();
        private static bool _comErro, _cinzaAplicado;
        private static string _textoAviso;
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
            _avisoAte = Time.unscaledTime + DuracaoAviso;
        }

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
            _volta.text = TextoVolta();
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
                GUI.Label(new Rect(r.x, r.y + 64f * k, r.width, 32f * k), TextoVolta(), _estiloLinha);
                JogadorCaido.Contar(out int dePe, out int total);
                GUI.color = Ambar;
                GUI.Label(new Rect(r.x, r.y + 112f * k, r.width, 32f * k), $"{dePe} de {total} de pé", _estiloLinha);
                GUI.color = Color.white;
            }
            if (aviso)
            {
                GUI.color = new Color(Vermelho.r, Vermelho.g, Vermelho.b, Mathf.Clamp01((_avisoAte - Time.unscaledTime) / SumirAviso));
                GUI.Label(new Rect(Screen.width / 2f - 450f * k, 125f * k, 900f * k, 50f * k), _textoAviso, _estiloAviso);
                GUI.color = Color.white;
            }
        }
    }
}
