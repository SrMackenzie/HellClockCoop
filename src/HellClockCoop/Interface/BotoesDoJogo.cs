using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Botão "Coop" nas listas de botões do próprio jogo (0.13.0), sem patch Harmony:
    /// - Menu do jogo (tela de saves, SaveSelectionScreen): cópia do botão "Créditos" na lista do canto superior direito
    ///   (ButtonsContainer: Créditos, engrenagem das Opções e o X), à esquerda dele.
    /// - Pausa (PauseHUD, Esc em jogo): cópia do item "Opções" na coluna de botões (BottomAnchor), logo abaixo dele; a coluna
    ///   cresce para baixo uma linha. O clique tira a pausa (PauseHUD.Unpause, como o "Continuar") e abre o menu do coop.
    /// Das cópias sai só o tradutor do texto (TextMeshLocalizer, que trocaria "Coop" pelo texto do botão original) e o atalho de
    /// controle do item copiado (JoystickFocusPoint._hasShortcut; o ícone ButtonHotkey fica apagado). Ficam o Button, o visual de
    /// destaque (BaseButtonBehaviour), os sons e o foco do controle, como nos botões vizinhos. A cópia é feita num depósito inativo
    /// (os scripts só acordam já limpos, dentro da lista) e ganha um ButtonClickedEvent novo (nenhum ouvinte do original).
    /// O clique só marca o pedido; o menu abre no Update do runtime (fora do processamento do EventSystem do jogo).
    /// Busca a cada 2 s enquanto o botão da tela atual não existe (as telas são recriadas ao trocar de cena). Erro na cópia do
    /// menu: volta ao botão "Menu do coop" do canto (MenuCoop.DesenharBotaoMenu); erro na pausa: só F7/View, sem item.
    /// </summary>
    internal static class BotoesDoJogo
    {
        internal const string Rotulo = "Coop";
        private const float IntervaloBusca = 2f;

        private static readonly FieldInfo CampoCreditos = AccessTools.Field(typeof(SaveSelectionScreen), "_creditsButton");
        private static readonly FieldInfo CampoAtalho = AccessTools.Field(typeof(JoystickFocusPoint), "_hasShortcut");

        private static GameObject _botaoMenu, _itemPausa;
        private static CanvasGroup _grupoMenu, _grupoCreditos, _grupoPausa, _grupoOpcoes; // cópia e original
        private static PauseHUD _pausa;
        private static Transform _deposito;
        private static float _proximaBusca;
        private static bool _menuFalhou, _pausaFalhou, _pedidoMenu, _pedidoPausa;

        /// <summary>A cópia do botão no menu do jogo falhou: o MenuCoop desenha o botão do canto, como na 0.12.0.</summary>
        internal static bool MenuFalhou => _menuFalhou;

        /// <summary>No Update do runtime, antes do MenuCoop: atende o clique do quadro anterior e procura as listas que faltam.</summary>
        internal static void Atualizar(bool semPersonagem)
        {
            if (_pedidoMenu || _pedidoPausa)
            {
                bool pausa = _pedidoPausa;
                _pedidoMenu = _pedidoPausa = false;
                if (pausa && _pausa != null && _pausa.IsPaused) _pausa.Unpause(); // como o "Continuar": some a pausa e o jogo volta
                MenuCoop.AbrirPeloJogo(pausa ? "item Coop da pausa" : "botão Coop do menu do jogo");
            }

            // A animação do jogo mexe no CanvasGroup dos botões originais (entrada da tela de saves): a cópia acompanha.
            if (_botaoMenu != null) Espelhar(_grupoMenu, _grupoCreditos);
            if (_itemPausa != null) Espelhar(_grupoPausa, _grupoOpcoes);

            if (Time.unscaledTime < _proximaBusca) return;
            if (semPersonagem && _botaoMenu != null && PainelNativo.EsperandoJanela)
            {
                // A janela das Opções ainda não existia quando o botão nasceu: tenta montar o painel de novo (a Novidades espera por ele).
                _proximaBusca = Time.unscaledTime + IntervaloBusca;
                PainelNativo.Preparar();
                return;
            }
            if (semPersonagem ? _menuFalhou || _botaoMenu != null : _pausaFalhou || _itemPausa != null) return;
            _proximaBusca = Time.unscaledTime + IntervaloBusca;
            if (semPersonagem) CriarNoMenu();
            else CriarNaPausa();
        }

        private static void CriarNoMenu()
        {
            SaveSelectionScreen tela = NaCena<SaveSelectionScreen>(true, out int quantas);
            if (tela == null) return; // telas de abertura: a tela de saves ainda não apareceu
            try
            {
                Button creditos = CampoCreditos != null ? CampoCreditos.GetValue(tela) as Button : null;
                if (creditos == null) throw new InvalidOperationException("botão Créditos da tela de saves não encontrado");
                _botaoMenu = Copiar(creditos, "HellClockCoop.BotaoCoop", () => _pedidoMenu = true);
                _botaoMenu.transform.SetSiblingIndex(creditos.transform.GetSiblingIndex()); // à esquerda de Créditos
                _grupoMenu = _botaoMenu.GetComponent<CanvasGroup>();
                _grupoCreditos = creditos.GetComponent<CanvasGroup>();
                Plugin.Log.LogInfo($"[Menu] Botão \"Coop\" na lista de botões do menu do jogo ({Caminho(creditos.transform.parent)}; {quantas} tela(s) de saves na cena).");
                PainelNativo.Preparar(); // a janela das Opções já existe: monta o painel agora, e não no primeiro clique
            }
            catch (Exception e)
            {
                _menuFalhou = true;
                Descartar(ref _botaoMenu);
                Plugin.Log.LogWarning($"[Menu] Não deu para pôr o botão \"Coop\" no menu do jogo; usando o botão do canto: {e.Message}");
            }
        }

        private static void CriarNaPausa()
        {
            PauseHUD hud = NaCena<PauseHUD>(false, out _);
            if (hud == null) return; // cena sem HUD (carregamento, vídeo)
            try
            {
                if (hud.OptionsButton == null) throw new InvalidOperationException("item Opções da pausa não encontrado");
                Transform opcoes = hud.OptionsButton.transform;
                _itemPausa = Copiar(hud.OptionsButton, "HellClockCoop.ItemCoop", () => _pedidoPausa = true);
                _itemPausa.transform.SetSiblingIndex(opcoes.GetSiblingIndex() + 1); // logo abaixo de Opções
                _pausa = hud;
                _grupoPausa = _itemPausa.GetComponent<CanvasGroup>();
                _grupoOpcoes = hud.OptionsButton.GetComponent<CanvasGroup>();

                // A coluna tem altura fixa e encosta no título: cresce uma linha para baixo, sem apertar os itens.
                var coluna = opcoes.parent as RectTransform;
                var vertical = coluna != null ? coluna.GetComponent<VerticalLayoutGroup>() : null;
                float linha = ((RectTransform)opcoes).sizeDelta.y + (vertical != null ? vertical.spacing : 0f);
                if (coluna != null && linha > 0f)
                {
                    coluna.sizeDelta = new Vector2(coluna.sizeDelta.x, coluna.sizeDelta.y + linha);
                    coluna.anchoredPosition = new Vector2(coluna.anchoredPosition.x, coluna.anchoredPosition.y - linha * (1f - coluna.pivot.y));
                }
                Plugin.Log.LogInfo($"[Menu] Item \"Coop\" na pausa do jogo (coluna +{linha:0} de altura).");
            }
            catch (Exception e)
            {
                _pausaFalhou = true;
                Descartar(ref _itemPausa);
                Plugin.Log.LogWarning($"[Menu] Não deu para pôr o item \"Coop\" na pausa do jogo (o menu segue no F7/View): {e.Message}");
            }
        }

        /// <summary>Copia o botão no depósito inativo, limpa o que não serve e só então o põe ao lado do original.</summary>
        private static GameObject Copiar(Button modelo, string nome, UnityAction aoClicar)
        {
            if (_deposito == null)
            {
                var d = new GameObject("HellClockCoop.DepositoDeBotoes");
                Object.DontDestroyOnLoad(d);
                d.SetActive(false); // a cópia não acorda (Awake/OnEnable) antes da limpeza
                _deposito = d.transform;
            }
            GameObject go = Object.Instantiate(modelo.gameObject, _deposito, false);
            go.name = nome;
            try
            {
                foreach (Component c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c != null && c.GetType().Name.IndexOf("Localizer", StringComparison.Ordinal) >= 0) Object.DestroyImmediate(c);
                }
                TMP_Text texto = null;
                foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.name == "Text") { texto = t; break; }
                }
                if (texto == null) throw new InvalidOperationException("texto do botão " + modelo.name + " não encontrado");
                texto.text = Rotulo;

                foreach (JoystickFocusPoint p in go.GetComponentsInChildren<JoystickFocusPoint>(true))
                {
                    if (CampoAtalho != null) CampoAtalho.SetValue(p, false); // o atalho do original abriria a tela dele também
                }
                Transform atalho = go.transform.Find("ButtonHotkey");
                if (atalho != null) atalho.gameObject.SetActive(false);

                Button botao = go.GetComponent<Button>();
                if (botao == null) throw new InvalidOperationException("cópia sem Button");
                botao.onClick = new Button.ButtonClickedEvent();
                botao.onClick.AddListener(aoClicar);

                go.transform.SetParent(modelo.transform.parent, false);
                return go;
            }
            catch
            {
                Object.Destroy(go);
                throw;
            }
        }

        private static void Espelhar(CanvasGroup copia, CanvasGroup original)
        {
            if (copia == null || original == null) return;
            if (copia.alpha != original.alpha) copia.alpha = original.alpha;
            if (copia.interactable != original.interactable) copia.interactable = original.interactable;
            if (copia.blocksRaycasts != original.blocksRaycasts) copia.blocksRaycasts = original.blocksRaycasts;
        }

        /// <summary>Primeira instância numa cena (fora os prefabs); com soAtiva, só a que está na tela.</summary>
        private static T NaCena<T>(bool soAtiva, out int quantas) where T : Component
        {
            double inicio = Desempenho.Agora();
            T achada = null;
            quantas = 0;
            foreach (T t in Resources.FindObjectsOfTypeAll<T>())
            {
                if (t == null || !t.gameObject.scene.IsValid()) continue;
                if (t.name.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) >= 0) continue; // versão de celular, como na OptionsWindow
                quantas++;
                if (achada == null && (!soAtiva || t.gameObject.activeInHierarchy)) achada = t;
            }
            Desempenho.SomarDetalhe(Desempenho.Detalhe.BuscarBotoesJogo, inicio);
            return achada;
        }

        private static string Caminho(Transform t) => t == null ? "" : t.parent == null ? t.name : Caminho(t.parent) + "/" + t.name;

        private static void Descartar(ref GameObject go)
        {
            if (go != null) Object.Destroy(go);
            go = null;
        }
    }
}
