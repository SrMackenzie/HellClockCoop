using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using FMOD.Studio;
using HarmonyLib;
using HellClock.Character;
using HellClock.Options;
using HellClock.Player;
using HellClock.PostProcessing;
using HellClock.UI;
using HellClock.UI.HUD;
using HellClockCoop.Fase2;
using K10.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Jogador caído na tela (0.16.2, opção C "Aviso de sangue" com o título da A, escolhida pelo autor em 22/11/2026),
    /// montada com peças do próprio jogo, copiadas ou dirigidas em tempo de execução (nada sai do jogo para o repositório):
    /// - Quem caiu: o cinza da segunda chance (CameraPostEffectController.LerpToGrayscale), a luz do jogador encolhendo
    ///   (PlayerLightController.LerpSize até 35%, presa pela trava BlockUpdate até levantar) e a vinheta de vida baixa
    ///   (LowLifeUIController._health, FullScreenHealthUI) pulsando nas bordas.
    /// - Título "VOCÊ CAIU" acima do personagem: cópia do título da tela de morte (GameOverScreen._titleMask), revelado
    ///   pela máscara em 0,7 s como no jogo, com o som de abertura dela (_gameOverShowSfx, tocado direto pelo FMOD para
    ///   não passar pelo patch de sons dos aliados). Se o título do jogo não tiver texto (sprite com "Game Over"), vale o
    ///   título do mod, na fonte da barra flutuante, surgindo em 0,3 s.
    /// - Abaixo, sem fundo: a volta no próximo andar (ou quem está te reanimando), a barra da reanimação e os pontos com
    ///   "N de M de pé". Reanimando, a vinheta pulsa mais devagar e mais fraca; golpe no reanimador: pico vermelho.
    /// - Queda e reanimação de aliado: na pilha de avisos da HUD do jogo (HUDNotificationManager.AddNotification), com a
    ///   cor de raridade vermelha ou âmbar. O texto do mod é marcado e o patch do Write (abaixo) tira o tradutor do
    ///   rótulo, que trocaria o texto pelo formato de "texto não encontrado". Sem o patch ou sem a HUD: aviso do mod no topo.
    /// - Só informação: nenhum clique, nada pausa nem desacelera o jogo. Erro: reserva no OnGUI, como o visual antigo.
    /// - Singletons Guaranteed (criariam um): CameraPostEffectController e AudioController não são chamados; o efeito e a
    ///   vinheta são achados na cena. GameOverScreen.Instance e HUDNotificationManager.Instance só leem o campo.
    /// </summary>
    internal static class CaidoNaTela
    {
        private const float DuracaoAviso = 2.5f, SumirAviso = 0.5f;
        private const float LuzCaido = 0.35f, RevelarTitulo = 0.7f, SurgirTitulo = 0.3f, DuracaoPico = 0.45f;
        internal const string MarcaAviso = "⁣hcc⁣"; // separador invisível: nenhuma chave do jogo começa assim
        private static readonly Color Vermelho = new Color(0.70f, 0.15f, 0.12f);   // #B3261E
        private static readonly Color Ambar = new Color(0.85f, 0.64f, 0.25f);      // #D9A441
        private static readonly object ChaveLuz = new object();

        private static readonly FieldInfo CampoTitulo = AccessTools.Field(typeof(GameOverScreen), "_titleMask");
        private static readonly FieldInfo CampoSomTitulo = AccessTools.Field(typeof(GameOverScreen), "_gameOverShowSfx");
        private static readonly FieldInfo CampoVinheta = AccessTools.Field(typeof(LowLifeUIController), "_health");
        private static readonly FieldInfo CampoIntensidade = AccessTools.Field(typeof(FullScreenHealthUI), "_toIntensity");
        private static readonly FieldInfo CampoFeedback = AccessTools.Field(typeof(LowLifeUIController), "_enableLowLifeFeedback");
        private static readonly FieldInfo CampoVida = AccessTools.Field(typeof(LowLifeUIController), "_lifeController");
        private static readonly MethodInfo MetodoSaude = AccessTools.Method(typeof(LowLifeUIController), "OnHealthChanged", new[] { typeof(float), typeof(bool) });

        private static GameObject _raiz, _deposito;
        private static RectTransform _caixaTitulo, _faixa, _pontos, _barra, _barraEnche;
        private static Image _barraCor, _mascaraTitulo;
        private static TMP_Text _titulo, _volta, _contagem, _aviso;
        private static readonly List<Image> Pontos = new List<Image>();
        private static bool _comErro, _cinzaAplicado, _luzPresa, _avisoNativo = true;
        private static bool? _patchAviso;
        private static string _textoAviso;
        private static Color _corAviso = Vermelho;
        private static float _avisoAte = -1f, _caiuEm = -1f, _picoAte = -1f, _vinhetaAntes, _flash = 1f;
        private static Reanimacao.Estado _estadoAntes;
        private static PlayerLightController _luz;
        private static FullScreenHealthUI _vinheta;
        private static LowLifeUIController _vidaUI;
        private static EventInstance _som;
        private static GUIStyle _estiloTitulo, _estiloLinha, _estiloAviso;

        private static bool Nativo => !_comErro && _raiz != null;

        // ---------------- Eventos ----------------

        internal static void AoCair()
        {
            _caiuEm = Time.unscaledTime;
            if (_mascaraTitulo != null) _mascaraTitulo.fillAmount = 0f;
            Cinza(true);
            Luz(true);
            Vinheta(true);
            Som(true);
        }

        internal static void AoLevantar()
        {
            _caiuEm = -1f;
            Cinza(false);
            Luz(false);
            Vinheta(false);
            Som(false);
            if (_faixa != null) _faixa.gameObject.SetActive(false);
            if (_caixaTitulo != null) _caixaTitulo.gameObject.SetActive(false);
        }

        /// <summary>Aviso de queda de um aliado; <paramref name="segundos"/> &gt; 0 só no host (quem tira o tempo).</summary>
        internal static void AvisarQueda(string nome, float segundos) =>
            Avisar(segundos > 0f ? $"{nome} caiu  -{segundos:0} s" : $"{nome} caiu", Vermelho);

        /// <summary>Aviso de reanimação concluída (0.16.0), em âmbar: notícia boa.</summary>
        internal static void AvisarReanimacao(string texto) => Avisar(texto, Ambar);

        /// <summary>Aviso do teleporte até o aliado (0.29.0): recusa, recarga ou cancelamento, em branco.</summary>
        internal static void AvisarTp(string texto) => Avisar(texto, Color.white);

        private static void Avisar(string texto, Color cor)
        {
            if (AvisoDaHud(texto, cor)) return;
            _textoAviso = texto;
            _corAviso = cor;
            _avisoAte = Time.unscaledTime + DuracaoAviso;
        }

        /// <summary>Linha de baixo: quem está te reanimando, ou a volta no próximo andar.</summary>
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
                bool caido = JogadorCaido.Caido;
                bool aviso = Time.unscaledTime < _avisoAte;
                if (!caido && !aviso && _raiz == null) return;
                if (_raiz == null && !Montar()) return;
                AtualizarCaido(caido);
                AtualizarAviso(aviso);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Erro na tela do jogador caído; usando o desenho simples: {e}");
                _comErro = true;
                if (_raiz != null) Object.Destroy(_raiz);
                _raiz = null;
            }
        }

        private static void AtualizarCaido(bool ligada)
        {
            GameObject titulo = _caixaTitulo.gameObject;
            if (_faixa.gameObject.activeSelf != ligada) _faixa.gameObject.SetActive(ligada);
            if (titulo.activeSelf != ligada) titulo.SetActive(ligada);
            if (!ligada) return;
            float agora = Time.unscaledTime;
            float desde = _caiuEm < 0f ? agora : agora - _caiuEm;
            if (_mascaraTitulo != null) _mascaraTitulo.fillAmount = Mathf.Clamp01(desde / RevelarTitulo);
            else _titulo.alpha = Mathf.Clamp01(desde / SurgirTitulo);

            bool reanimando = Reanimacao.MeuProgresso(out Reanimacao.Progresso pr);
            string linha = TextoLinha(reanimando, pr);
            if (_volta.text != linha) _volta.text = linha;
            if (_barra.gameObject.activeSelf != reanimando) _barra.gameObject.SetActive(reanimando);
            if (reanimando)
            {
                _barraEnche.anchorMax = new Vector2(pr.Fracao, 1f);
                _barraCor.color = CorBarra(pr.Estado);
            }
            Reanimacao.Estado estado = reanimando ? pr.Estado : Reanimacao.Estado.FiqueParado;
            if (estado == Reanimacao.Estado.Interrompido && _estadoAntes != Reanimacao.Estado.Interrompido) _picoAte = agora + DuracaoPico;
            _estadoAntes = estado;
            PulsarVinheta(agora, reanimando);

            JogadorCaido.Contar(out int dePe, out int total);
            string contagem = $"{dePe} de {total} de pé";
            if (_contagem.text != contagem) _contagem.text = contagem;
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
            _deposito = new GameObject("Deposito");
            _deposito.SetActive(false); // a cópia nasce aqui, inativa: nenhum Awake do jogo roda nela
            _deposito.transform.SetParent(_raiz.transform, false);

            // Título acima do personagem (o personagem fica no centro da tela).
            RectTransform caixaTitulo = _caixaTitulo = Caixa("Titulo", _raiz.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 210f), new Vector2(900f, 120f));
            caixaTitulo.pivot = new Vector2(0.5f, 0.5f);
            if (!CopiarTituloDoJogo(caixaTitulo))
            {
                _titulo = Texto("Texto", caixaTitulo, Vector2.zero, 64f, Vermelho, FontStyles.Bold);
                Contorno(_titulo, 0.2f);
            }
            _titulo.text = "VOCÊ CAIU";
            caixaTitulo.gameObject.SetActive(false);

            // Bloco de baixo, sem fundo: o contorno preto segura a leitura sobre o cinza.
            _faixa = Caixa("Faixa", _raiz.transform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(760f, 120f));
            _volta = Texto("Volta", _faixa, new Vector2(0f, 30f), 27f, Color.white, FontStyles.Normal);
            Contorno(_volta, 0.25f);
            // Barra da reanimação (0.16.0): trilho âmbar 22%, preenchimento pela âncora (Image sem sprite não corta).
            _barra = Caixa("Reanimacao", _faixa, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(360f, 12f));
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
            _pontos = Caixa("Pontos", _faixa, new Vector2(0.5f, 0.5f), new Vector2(-150f, -36f), new Vector2(120f, 18f));
            HorizontalLayoutGroup fila = _pontos.gameObject.AddComponent<HorizontalLayoutGroup>();
            fila.spacing = 10f;
            fila.childAlignment = TextAnchor.MiddleRight;
            fila.childControlWidth = fila.childControlHeight = false;
            fila.childForceExpandWidth = fila.childForceExpandHeight = false;
            _contagem = Texto("Contagem", _faixa, new Vector2(70f, -36f), 23f, Ambar, FontStyles.Normal);
            _contagem.alignment = TextAlignmentOptions.Left;
            _contagem.rectTransform.sizeDelta = new Vector2(300f, 34f);
            Contorno(_contagem, 0.25f);
            _faixa.gameObject.SetActive(false);

            // Reserva do aviso de aliado (sem a pilha da HUD do jogo).
            RectTransform topo = Caixa("Aviso", _raiz.transform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(900f, 50f));
            _aviso = Texto("Texto", topo, Vector2.zero, 34f, Vermelho, FontStyles.Bold);
            Contorno(_aviso, 0.2f);
            _aviso.gameObject.SetActive(false);
            Plugin.Log.LogInfo($"[Caído] Tela do jogador caído montada ({(_mascaraTitulo != null ? "título da tela de morte do jogo" : "título do mod")}).");
            return true;
        }

        /// <summary>
        /// Copia o título da tela de morte (a máscara e o que está dentro), sem os scripts. Só serve se dentro houver um
        /// texto (TMP) para virar "VOCÊ CAIU"; sem texto (título desenhado), a cópia é descartada e vale o título do mod.
        /// </summary>
        private static bool CopiarTituloDoJogo(RectTransform destino)
        {
            try
            {
                GameOverScreen tela = GameOverScreen.Instance;
                Image mascara = tela != null && CampoTitulo != null ? CampoTitulo.GetValue(tela) as Image : null;
                if (mascara == null) { Plugin.Log.LogInfo("[Caído] Tela de morte do jogo ausente: título do mod."); return false; }
                GameObject go = Object.Instantiate(mascara.gameObject, _deposito.transform, false);
                go.name = "TituloDoJogo";
                PainelNativo.Limpar(go);
                TMP_Text texto = go.GetComponentInChildren<TMP_Text>(true);
                Image img = go.GetComponent<Image>();
                Plugin.Log.LogInfo($"[Caído] Título da tela de morte: {Descrever(go.transform)}");
                if (texto == null || img == null || img.type != Image.Type.Filled)
                {
                    Object.Destroy(go);
                    Plugin.Log.LogInfo("[Caído] O título da tela de morte não tem texto revelável: título do mod.");
                    return false;
                }
                foreach (CanvasGroup g in go.GetComponentsInChildren<CanvasGroup>(true)) g.alpha = 1f;
                var rt = (RectTransform)go.transform;
                rt.SetParent(destino, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                go.SetActive(true);
                texto.richText = false;
                texto.textWrappingMode = TextWrappingModes.NoWrap;
                texto.raycastTarget = false;
                foreach (Graphic g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
                img.fillOrigin = 0; // o Hide do jogo deixa 1 (revelaria do outro lado); o ResetAlphas dele volta a 0
                img.fillAmount = 0f;
                _mascaraTitulo = img;
                _titulo = texto;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Não deu para copiar o título da tela de morte: {e.Message}");
                return false;
            }
        }

        private static string Descrever(Transform t)
        {
            string comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is CanvasRenderer)).Select(c => c.GetType().Name));
            string filhos = "";
            for (int i = 0; i < t.childCount; i++) filhos += (i == 0 ? " { " : "; ") + Descrever(t.GetChild(i)) + (i == t.childCount - 1 ? " }" : "");
            return $"{t.name}[{comps}]{filhos}";
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
            RectTransform rt = Caixa(nome, pai, new Vector2(0.5f, 0.5f), posicao, new Vector2(880f, tamanho + 10f));
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

        private static void Contorno(TMP_Text t, float largura)
        {
            t.outlineWidth = largura;
            t.outlineColor = Color.black;
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

        // ---------------- Peças do jogo dirigidas pelo mod ----------------

        private static void Cinza(bool ligar)
        {
            if (ligar == _cinzaAplicado) return; // só desfaz o que o mod aplicou
            try
            {
                double inicio = Desempenho.Agora(); // busca na cena na queda e ao levantar: medida desde a 0.30.3, ainda sem número
                CameraPostEffectController efeito = Object.FindAnyObjectByType<CameraPostEffectController>();
                Desempenho.SomarDetalhe(Desempenho.Detalhe.BuscarPecasHud, inicio);
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

        /// <summary>
        /// Luz do jogador (o tamanho dela é o tempo do Hell Clock): encolhe a 35% em 1 s e fica presa pela trava do jogo
        /// (o fim do LerpSize solta a trava dele, então o mod põe a própria). Ao levantar, solta e volta ao tamanho do tempo.
        /// </summary>
        private static void Luz(bool encolher)
        {
            try
            {
                if (encolher)
                {
                    PlayerController pc = CoopRuntime.JogadorLocal();
                    _luz = pc != null ? pc.PlayerLightController : null;
                    if (_luz == null) return;
                    PlayerLightController luz = _luz;
                    luz.LerpSize(LuzCaido, 1f, Ease.OutQuad, () =>
                    {
                        if (!JogadorCaido.Caido || luz == null || luz != _luz) return;
                        luz.BlockUpdate.BlockButDoNotIncrease(ChaveLuz);
                        _luzPresa = true;
                    });
                    return;
                }
                PlayerLightController atual = _luz;
                _luz = null;
                if (atual == null) { _luzPresa = false; return; } // trocou de cena: a luz nova não tem a trava
                if (_luzPresa) atual.BlockUpdate.Release(ChaveLuz);
                _luzPresa = false;
                float alvo = TimerManager.Instance.GetTimeRatio(); // Guaranteed, mas a luz viva garante a cena da run
                if (alvo >= 0f) atual.LerpSize(LuzCaido, alvo, 0.5f, Ease.OutQuad, null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Luz do jogador indisponível: {e.Message}");
            }
        }

        /// <summary>
        /// Vinheta de vida baixa do jogo: o mod dirige a intensidade enquanto você está caído. Respeita a opção do jogo que
        /// desliga a vinheta (_enableLowLifeFeedback) e a intensidade dos flashes no pico. Ao levantar, o próprio jogo
        /// recalcula pela vida nova (OnHealthChanged); sem ele, volta à intensidade de antes da queda.
        /// </summary>
        private static void Vinheta(bool ligar)
        {
            try
            {
                if (ligar)
                {
                    double inicio = Desempenho.Agora(); // idem: medida desde a 0.30.3
                    _vidaUI = Object.FindAnyObjectByType<LowLifeUIController>();
                    Desempenho.SomarDetalhe(Desempenho.Detalhe.BuscarPecasHud, inicio);
                    _vinheta = _vidaUI != null && CampoVinheta != null ? CampoVinheta.GetValue(_vidaUI) as FullScreenHealthUI : null;
                    if (_vinheta != null && CampoFeedback != null && !(bool)CampoFeedback.GetValue(_vidaUI)) _vinheta = null; // opção desligada
                    if (_vinheta == null) return;
                    _vinhetaAntes = CampoIntensidade != null ? (float)CampoIntensidade.GetValue(_vinheta) : 0f;
                    _flash = Mathf.Clamp01(ClassSingleton<OptionsRuntime>.Instance.Graphics.FlashEffectsIntensity.Value);
                    _picoAte = -1f;
                    return;
                }
                FullScreenHealthUI v = _vinheta;
                LowLifeUIController ui = _vidaUI;
                _vinheta = null;
                _vidaUI = null;
                if (v == null) return;
                LifeController vida = ui != null && CampoVida != null ? CampoVida.GetValue(ui) as LifeController : null;
                if (vida != null && MetodoSaude != null) MetodoSaude.Invoke(ui, new object[] { vida.CurrentRatio, true });
                else v.Lerp(_vinhetaAntes, 0.4f);
            }
            catch (Exception e)
            {
                _vinheta = null;
                _vidaUI = null;
                Plugin.Log.LogWarning($"[Caído] Vinheta do jogo indisponível: {e.Message}");
            }
        }

        private static void PulsarVinheta(float agora, bool reanimando)
        {
            if (_vinheta == null) return;
            float periodo = reanimando ? 4f : 2.4f, base_ = reanimando ? 0.35f : 0.5f;
            float v = base_ + 0.12f * Mathf.Sin(agora * 6.2831855f / periodo);
            if (agora < _picoAte) v = Mathf.Lerp(v, Mathf.Lerp(v, 0.95f, _flash), (_picoAte - agora) / DuracaoPico); // opção dos flashes
            _vinheta.SetIntensity(Mathf.Clamp01(v));
        }

        /// <summary>Som de abertura da tela de morte, tocado direto pelo FMOD (sem passar pelo patch de sons dos aliados).</summary>
        private static void Som(bool tocar)
        {
            try
            {
                if (_som.isValid())
                {
                    _som.stop(STOP_MODE.ALLOWFADEOUT);
                    _som.release();
                    _som.clearHandle();
                }
                if (!tocar) return;
                GameOverScreen tela = GameOverScreen.Instance;
                if (tela == null || CampoSomTitulo == null) return;
                var evento = (FMODUnity.EventReference)CampoSomTitulo.GetValue(tela);
                if (evento.IsNull) return;
                _som = FMODUnity.RuntimeManager.CreateInstance(evento);
                _som.start();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Som da queda indisponível: {e.Message}");
            }
        }

        /// <summary>Aviso na pilha da HUD do jogo; falso se a HUD ou o patch do rótulo faltarem (vale o aviso do mod).</summary>
        private static bool AvisoDaHud(string texto, Color cor)
        {
            if (!_avisoNativo) return false;
            try
            {
                if (_patchAviso == null)
                {
                    MethodBase alvo = AccessTools.Method(typeof(GenericHudNotificationView), "Write");
                    Patches info = alvo != null ? Harmony.GetPatchInfo(alvo) : null;
                    _patchAviso = CaidoNaTela_AvisoDaHud_Write.CampoRotulo != null && info != null && info.Postfixes.Any(p => p.owner == Plugin.PluginGuid);
                    if (_patchAviso == false) Plugin.Log.LogInfo("[Caído] Patch do aviso da HUD ausente: avisos de aliado no topo, pelo mod.");
                }
                if (_patchAviso != true) return false;
                HUDNotificationManager hud = HUDNotificationManager.Instance;
                if (hud == null || !hud.isActiveAndEnabled) return false;
                hud.AddNotification(MarcaAviso + texto, string.Empty, null, cor); // subtexto vazio, como o jogo (nulo quebra o tradutor dele)
                return true;
            }
            catch (Exception e)
            {
                _avisoNativo = false;
                Plugin.Log.LogWarning($"[Caído] Aviso da HUD do jogo indisponível; avisos de aliado pelo mod: {e.Message}");
                return false;
            }
        }

        // ---------------- Reserva (OnGUI) ----------------

        /// <summary>OnGUI do CoopRuntime: título, linhas e aviso desenhados à mão quando a camada com a fonte do jogo não montou.</summary>
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
                GUI.color = new Color(0f, 0f, 0f, 0.62f);
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

    /// <summary>
    /// Alvo: HellClock.UI.HUD.GenericHudNotificationView.Write(ref NotificationData) — escreve o rótulo do aviso da HUD
    /// pela localização (SetLocalizedText), que troca texto livre pelo formato de "texto não encontrado". Com a marca do
    /// mod (CaidoNaTela.MarcaAviso), tira o tradutor desse rótulo e escreve o texto como está. O jogo põe um tradutor
    /// novo no próximo aviso dele (SetLocalizedText cria um quando falta).
    /// </summary>
    [HarmonyPatch(typeof(GenericHudNotificationView), "Write")]
    internal static class CaidoNaTela_AvisoDaHud_Write
    {
        internal static readonly FieldInfo CampoRotulo = AccessTools.Field(typeof(HudNotificationView), "_label");
        private static readonly Dictionary<TMP_Text, bool> RichTextDoJogo = new Dictionary<TMP_Text, bool>();

        private static void Postfix(GenericHudNotificationView __instance, ref NotificationData __0)
        {
            try
            {
                if (!(CampoRotulo?.GetValue(__instance) is TMP_Text rotulo)) return;
                string texto = __0.Text;
                bool doMod = texto != null && texto.StartsWith(CaidoNaTela.MarcaAviso, StringComparison.Ordinal);
                if (!doMod)
                {
                    // Aviso do jogo num rótulo que já mostrou um do mod: devolve o richText dele.
                    if (RichTextDoJogo.TryGetValue(rotulo, out bool rico)) { rotulo.richText = rico; RichTextDoJogo.Remove(rotulo); }
                    return;
                }
                TextMeshLocalizer tradutor = rotulo.GetComponent<TextMeshLocalizer>();
                if (tradutor != null) Object.DestroyImmediate(tradutor);
                if (!RichTextDoJogo.ContainsKey(rotulo)) RichTextDoJogo[rotulo] = rotulo.richText;
                rotulo.richText = false; // nomes da Steam com <tags> saem como texto
                rotulo.text = texto.Substring(CaidoNaTela.MarcaAviso.Length);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Rótulo do aviso da HUD: {e.Message}");
            }
        }
    }
}
