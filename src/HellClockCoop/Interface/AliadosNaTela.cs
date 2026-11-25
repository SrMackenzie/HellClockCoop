using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using HellClock.Pool;
using HellClock.Quest;
using HellClock.UI.HUD;
using HellClockCoop.Avatar;
using HellClockCoop.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Aliados na tela (0.14.0): barra de vida sob o nome de cada aliado e seta na borda da tela apontando o aliado que
    /// está fora dela. Só informação: não muda nada do jogo nem a dificuldade.
    /// - A vida e a barreira (convicção, 0.28.0) vêm do PlayerSnapshot (frações 0..1, formato 5); o RemotePlayerAvatar guarda as do último pacote.
    /// - Visual com as peças do jogo (escolha A do autor): a barra é uma cópia da barra flutuante que o jogo põe sobre o
    ///   companheiro (UIObjectPoolController._companionFloatingLifeView); a seta usa a imagem de seta da HUD do jogo
    ///   (SurvivalView._arrowSprite ou, sem ela, QuestTabBar._foldoutArrow) e o nome sai na fonte da barra do jogo.
    ///   As cópias perdem os scripts (como no PainelNativo) e vão para GenericHUD.LifeBars, onde o jogo põe as barras dele:
    ///   somem junto com a HUD (cinemáticas, tela preta). Quem posiciona e preenche é este módulo, a cada quadro.
    /// - GenericHUD.Instance é Guaranteed (criaria uma HUD). Desde a 0.30.3 as peças vêm por referência direta, sem varrer a
    ///   cena: a HUD pelo registro do jogo (HudDoJogo), o pool por UIObjectPoolController.Instance (só consulta o dicionário
    ///   do jogo, não cria) e as setas de dentro da própria HUD. O Resources.FindObjectsOfTypeAll (50 a 90 ms por busca em
    ///   jogo: 272 ms em 4 buscas na primeira aparição do aliado) fica só de reserva, quando a referência direta vem vazia.
    /// - Peça ausente ou erro: reserva no OnGUI (barra e seta desenhadas à mão), como o menu antigo.
    /// - Modo eco: o fantasma entra como aliado "Eco" (chave 0). DistanciaDoEco no .cfg afasta o fantasma para testar a seta.
    /// - Reanimação (0.16.0, opção B "barra do aliado"): com alguém ao lado do aliado caído, uma barra fina verde de
    ///   progresso aparece logo abaixo da barra dele e o rótulo "caído" vira "reanimando" (verde), "interrompido"
    ///   (vermelho) ou "fique parado" (branco 60%).
    /// - 0.31.3: o que está ligado ou desligado em cada peça fica guardado (sem perguntar à Unity a cada quadro), o tamanho
    ///   da tela e o tempo do quadro são lidos uma vez por quadro, e o Aquecer monta e posiciona uma barra e uma seta de
    ///   mentira ao entrar numa sala, para o primeiro aliado não pagar a primeira vez.
    /// </summary>
    internal static class AliadosNaTela
    {
        private const float IntervaloBusca = 2f;          // nova tentativa de montar enquanto a HUD do jogo não existe
        private const float AlturaCabeca = 2.4f;          // mesmo ponto do nome (CoopRuntime)
        private const float AlturaCorpo = 1.2f;           // ponto que decide se o aliado está na tela
        private const float AbaixoDoNome = 20f;           // pixels entre o centro do nome e o centro da barra
        private const float MargemBorda = 56f;            // pixels da seta até a borda (em 1080p; escala pela altura)
        private const float AtrasoQueda = 0.6f;           // fração por segundo com que a faixa clara alcança a vida
        /// <summary>Cor da barreira quando não há sprite do jogo (reserva do painel e da barra desenhada por OnGUI).</summary>
        internal static readonly Color BarreiraReserva = new Color(0.92f, 0.86f, 0.62f, 0.85f);
        private const float DuracaoBrilho = 0.25f;        // segundos para o brilho da barreira aparecer ou sumir
        private const float LarguraReserva = 90f, AlturaReserva = 7f;

        /// <summary>
        /// Giro (graus) que leva a seta do jogo a apontar para a direita: a imagem achada (UI_Icon_DropDownArrow) aponta para
        /// baixo. Conferido no jogo em 22/11/2026 com o modo eco e DistanciaDoEco = 30: a seta leva ao fantasma.
        /// </summary>
        private const float GiroBaseSeta = 90f;

        internal static ConfigEntry<bool> MostrarVida, MostrarSeta;

        private sealed class Visual
        {
            public RectTransform Barra, Seta, SetaNome;
            public Image Preenche, Atraso, SetaImagem;
            public Image Barreira;   // _barrierFill do jogo: a convicção por cima da vida (0.28.0)
            public CanvasGroup Brilho; // _barrierGlow do jogo: aparece enquanto há barreira
            public TMP_Text SetaTexto, Rotulo; // Rotulo: "caído" sobre a moldura da barra (0.15.0)
            public RectTransform Progresso, ProgressoEnche; // reanimação, abaixo da barra (0.16.0)
            public Image ProgressoCor;
            public float ValorAtraso = -1f; // < 0: barra nova, começa na vida atual (sem cair de 100%)
            public bool Visto;
            // 0.31.3: o activeSelf de cada peça e o alfa do brilho como estão agora (postos na criação, no Obter)
            public GameObject BarraGo, SetaGo, SetaNomeGo, RotuloGo, ProgressoGo;
            public bool BarraLigada, SetaLigada, SetaNomeLigada, RotuloLigado, ProgressoLigado;
            public float BrilhoPosto;
        }

        // Um aliado do quadro: chave (SteamID; 0 = eco), nome, avatar e se está caído (o eco nunca cai: 0.15.0).
        private struct Aliado
        {
            public ulong Chave;
            public string Nome;
            public RemotePlayerAvatar Avatar;
            public bool Caido;
        }

        /// <summary>Aliado caído (0.15.0, opção B): seta e nome em cinza, barra sem preenchimento com o rótulo "caído".</summary>
        private static readonly Color CinzaCaido = new Color(0.62f, 0.62f, 0.62f, 0.9f);
        private const string TextoCaido = "caído";
        private const ulong ChaveAquecimento = 2; // nenhum SteamID de jogador é 2 (o eco é 0): o aliado de mentira do Aquecer

        /// <summary>Reanimação (0.16.0): verde só para o progresso; vermelho para interrompido; branco 60% para a dica.</summary>
        internal static readonly Color VerdeReanimar = new Color(0.50f, 0.72f, 0.49f);      // #7FB77E
        internal static readonly Color VermelhoInterrompido = new Color(0.70f, 0.15f, 0.12f); // #B3261E
        private static readonly Color BrancoDica = new Color(1f, 1f, 1f, 0.6f);
        private const float AbaixoDaBarra = 14f, LarguraProgresso = 90f, AlturaProgresso = 7f;

        private static string TextoEstado(Fase2.Reanimacao.Estado e) =>
            e == Fase2.Reanimacao.Estado.Interrompido ? "interrompido" : e == Fase2.Reanimacao.Estado.FiqueParado ? "fique parado" : "reanimando";

        private static Color CorEstado(Fase2.Reanimacao.Estado e) =>
            e == Fase2.Reanimacao.Estado.Interrompido ? VermelhoInterrompido : e == Fase2.Reanimacao.Estado.FiqueParado ? BrancoDica : VerdeReanimar;

        private static readonly Dictionary<ulong, Visual> Visuais = new Dictionary<ulong, Visual>();
        private static readonly List<ulong> Apagar = new List<ulong>();
        private static readonly List<Aliado> DoQuadro = new List<Aliado>();

        // Campo renomeado numa atualização do jogo: a referência fica nula e a peça cai na reserva, sem derrubar o
        // construtor estático (que roda no Configurar, dentro do CoopRuntime.Criar).
        private static readonly AccessTools.FieldRef<UIObjectPoolController, GameObject> BarraCompanheiroRef =
            CriarRef<UIObjectPoolController, GameObject>("_companionFloatingLifeView");
        private static readonly AccessTools.FieldRef<SurvivalView, RectTransform> SetaSobrevivenciaRef =
            CriarRef<SurvivalView, RectTransform>("_arrowSprite");
        private static readonly AccessTools.FieldRef<QuestTabBar, RectTransform> SetaMissoesRef =
            CriarRef<QuestTabBar, RectTransform>("_foldoutArrow");

        private static AccessTools.FieldRef<T, F> CriarRef<T, F>(string campo) where T : class
        {
            try { return AccessTools.FieldRefAccess<T, F>(campo); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Aliados] {typeof(T).Name}.{campo} indisponível (barra ou seta dos aliados): {(e.InnerException ?? e).Message}");
                return null;
            }
        }

        private static bool _desativado, _avisouSemPeca, _reservaComErro;
        private static int _buscasNaCena; // varreduras de reserva feitas (o log da montagem diz quantas foram precisas)
        private static float _proximaBusca;
        private static GenericHUD _hud;
        private static RectTransform _camada;              // GenericHUD.LifeBars
        private static Canvas _canvas;
        private static GameObject _deposito, _modeloBarra;
        private static string _caminhoPreenche, _caminhoAtraso, _caminhoBarreira, _caminhoBrilho;
        private static Vector2 _mapaBarreira = new Vector2(0f, 1f); // _barrierMapConversion: fração → fillAmount do sprite
        private static Sprite _spriteSeta;
        private static Color _corSeta = Color.white;
        private static Vector2 _tamanhoSeta = new Vector2(36f, 36f);
        private static TMP_FontAsset _fonte;
        private static Material _materialFonte;
        private static Color _corNome = Color.white;

        private static GUIStyle _estiloSeta, _estiloNomeSeta;

        internal static void Configurar(ConfigFile config)
        {
            MostrarVida = config.Bind("Interface", "MostrarVidaDosAliados", true, "Mostra a barra de vida sob o nome de cada aliado.");
            MostrarSeta = config.Bind("Interface", "MostrarSetaDosAliados", true, "Mostra na borda da tela uma seta apontando o aliado que está fora dela.");
        }

        /// <summary>Fonte (e material) da barra flutuante do jogo, achada ao montar a barra; nula antes disso. Usada pelo CaidoNaTela.</summary>
        internal static TMP_FontAsset Fonte => _fonte;
        internal static Material MaterialFonte => _materialFonte;

        /// <summary>True quando a barra e a seta saem com as peças do jogo (o OnGUI não desenha a reserva).</summary>
        private static bool Nativo => !_desativado && _camada != null && _modeloBarra != null;

        // ---------- Peças para o Marcador (0.17.0) ----------

        /// <summary>Cor da seta da HUD do jogo (o seu marcador sai nela).</summary>
        internal static Color CorSeta => _corSeta;

        /// <summary>Giro que leva a seta do jogo a apontar para a direita (ver GiroBaseSeta).</summary>
        internal const float GiroSeta = GiroBaseSeta;

        /// <summary>A HUD do jogo está na tela (sem cinemática nem tela preta): a reserva do marcador só desenha com ela.</summary>
        internal static bool HudVisivel => _camada != null && _camada.gameObject.activeInHierarchy;

        /// <summary>
        /// Camada, câmera da interface e seta do jogo para o Marcador; monta as peças se ainda não montou (mesma busca
        /// espaçada da barra e da seta dos aliados). False: o marcador usa a reserva no OnGUI.
        /// </summary>
        internal static bool PecasDoMarcador(out RectTransform camada, out Camera camUi, out Sprite seta, out Vector2 tamanhoSeta)
        {
            camada = null;
            camUi = null;
            seta = null;
            tamanhoSeta = default;
            if (!Nativo && !Preparar()) return false;
            tamanhoSeta = _tamanhoSeta; // depois do Preparar: a seta achada agora já vale neste quadro
            camada = _camada;
            camUi = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            seta = _spriteSeta;
            return seta != null;
        }

        /// <summary>
        /// Peças para o painel de grupo (0.27.0): camada de barras e câmera da interface, depois de montada a barra dos
        /// aliados (a fonte do jogo vem dela). False: sem a HUD do jogo ou sem as peças; o painel não aparece.
        /// </summary>
        internal static bool PecasDoPainel(out RectTransform camada, out Camera camUi)
        {
            camada = null;
            camUi = null;
            if (!Nativo && !Preparar()) return false;
            camada = _camada;
            camUi = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            return true;
        }

        /// <summary>Imagens da barra do companheiro (fundo, preenchimento e faixa clara) para o painel copiar sprite e cor; nulas sem o modelo.</summary>
        internal static void ImagensDaBarra(out Image fundo, out Image preenche, out Image atraso, out Image barreira)
        {
            fundo = preenche = atraso = barreira = null;
            if (_modeloBarra == null) return;
            preenche = Peca(_modeloBarra.transform, _caminhoPreenche);
            atraso = _caminhoAtraso != null ? Peca(_modeloBarra.transform, _caminhoAtraso) : null;
            barreira = _caminhoBarreira != null ? Peca(_modeloBarra.transform, _caminhoBarreira) : null;
            foreach (Image i in _modeloBarra.GetComponentsInChildren<Image>(true))
            {
                if (i == preenche || i == atraso || i == barreira || !i.enabled || i.sprite == null) continue;
                fundo = i; // a primeira imagem visível que não é preenchimento: a moldura/fundo da barra
                break;
            }
        }

        /// <summary>Rótulo e cor do aliado caído com alguém reanimando (os mesmos da barra sobre a cabeça).</summary>
        internal static string TextoReanimacao(Fase2.Reanimacao.Estado e) => TextoEstado(e);
        internal static Color CorReanimacao(Fase2.Reanimacao.Estado e) => CorEstado(e);

        /// <summary>Põe o centro da peça no ponto da tela (pixels, y para cima), na camada de barras do jogo.</summary>
        internal static void ColocarNaCamada(RectTransform rt, Vector2 tela, Camera camUi) => Colocar(rt, tela, camUi);

        /// <summary>LateUpdate do CoopRuntime (depois dos avatares): monta, posiciona e preenche as peças do jogo.</summary>
        internal static void Atualizar(SessaoCoop sessao, RemotePlayerAvatar eco)
        {
            try
            {
                AtualizarQuadro(sessao, eco);
            }
            catch (Exception e) // fora do laço de rede do CoopRuntime: um erro aqui não pode repetir a cada quadro
            {
                Plugin.Log.LogWarning($"[Aliados] Erro ao atualizar a barra e a seta; desligadas as peças do jogo: {e}");
                Desativar();
            }
        }

        private static void AtualizarQuadro(SessaoCoop sessao, RemotePlayerAvatar eco)
        {
            Coletar(sessao, eco);
            bool algo = DoQuadro.Count > 0 && (MostrarVida.Value || MostrarSeta.Value);
            if (!algo)
            {
                EsconderTudo();
                return;
            }
            if (!Nativo && !Preparar())
            {
                EsconderTudo();
                return;
            }
            try
            {
                Posicionar();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Aliados] Erro na barra ou na seta com a interface do jogo; usando o desenho simples: {e}");
                Desativar();
            }
        }

        /// <summary>OnGUI do CoopRuntime: reserva quando as peças do jogo não montaram, só com a HUD do jogo na tela.</summary>
        internal static void DesenharReserva(SessaoCoop sessao, RemotePlayerAvatar eco, bool comNome)
        {
            if (_reservaComErro) return;
            try
            {
                DesenharReservaQuadro(sessao, eco, comNome);
            }
            catch (Exception e) // o OnGUI segue para o menu do coop, a Novidades e a janela de aceite
            {
                Plugin.Log.LogWarning($"[Aliados] Erro no desenho simples da barra e da seta; desligado até reabrir o jogo: {e}");
                _reservaComErro = true;
                GUI.color = Color.white;
            }
        }

        private static void DesenharReservaQuadro(SessaoCoop sessao, RemotePlayerAvatar eco, bool comNome)
        {
            if (Nativo || _camada == null || !_camada.gameObject.activeInHierarchy || Event.current.type != EventType.Repaint) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Coletar(sessao, eco);
            if (_estiloSeta == null)
            {
                _estiloSeta = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 30, fontStyle = FontStyle.Bold };
                _estiloNomeSeta = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            }
            float larguraTela = Screen.width, alturaTela = Screen.height;
            foreach (Aliado a in DoQuadro)
            {
                Vector3 cabeca = cam.WorldToScreenPoint(a.Avatar.transform.position + Vector3.up * AlturaCabeca);
                if (NaTela(cam, a.Avatar.transform.position, larguraTela, alturaTela))
                {
                    if (!MostrarVida.Value || cabeca.z <= 0f) continue;
                    float y = Screen.height - cabeca.y + (comNome && a.Chave != 0 ? AbaixoDoNome : 0f);
                    var fundo = new Rect(cabeca.x - LarguraReserva / 2f, y - AlturaReserva / 2f, LarguraReserva, AlturaReserva);
                    GUI.color = new Color(0.08f, 0.04f, 0.04f, 0.85f);
                    GUI.DrawTexture(fundo, Texture2D.whiteTexture);
                    GUI.color = new Color(0.78f, 0.12f, 0.10f);
                    if (!a.Caido) GUI.DrawTexture(new Rect(fundo.x + 1f, fundo.y + 1f, (fundo.width - 2f) * Mathf.Clamp01(a.Avatar.Vida), fundo.height - 2f), Texture2D.whiteTexture);
                    GUI.color = BarreiraReserva;
                    if (!a.Caido && a.Avatar.Barreira > 0f) GUI.DrawTexture(new Rect(fundo.x + 1f, fundo.y + 1f, (fundo.width - 2f) * Mathf.Clamp01(a.Avatar.Barreira), fundo.height - 2f), Texture2D.whiteTexture);
                    Fase2.Reanimacao.Progresso pr = default;
                    bool reanimando = a.Caido && Fase2.Reanimacao.ProgressoDe(a.Chave, out pr);
                    GUI.color = reanimando ? CorEstado(pr.Estado) : CinzaCaido;
                    if (a.Caido) GUI.Label(new Rect(fundo.x - 30f, fundo.y + 4f, fundo.width + 60f, 20f), reanimando ? TextoEstado(pr.Estado) : TextoCaido, _estiloNomeSeta);
                    if (reanimando)
                    {
                        var trilho = new Rect(cabeca.x - LarguraProgresso / 2f, y + AbaixoDaBarra - AlturaProgresso / 2f, LarguraProgresso, AlturaProgresso);
                        GUI.color = new Color(0f, 0f, 0f, 0.62f);
                        GUI.DrawTexture(trilho, Texture2D.whiteTexture);
                        GUI.color = pr.Estado == Fase2.Reanimacao.Estado.Interrompido ? VermelhoInterrompido : VerdeReanimar;
                        GUI.DrawTexture(new Rect(trilho.x + 1f, trilho.y + 1f, (trilho.width - 2f) * pr.Fracao, trilho.height - 2f), Texture2D.whiteTexture);
                    }
                    GUI.color = Color.white;
                }
                else if (MostrarSeta.Value)
                {
                    Borda(cam, a.Avatar.transform.position, Screen.height / 1080f, out Vector2 ponto, out float angulo, out Vector2 direcao);
                    var centro = new Vector2(ponto.x, Screen.height - ponto.y); // OnGUI: y para baixo
                    Matrix4x4 antes = GUI.matrix;
                    GUIUtility.RotateAroundPivot(-angulo, centro);
                    if (a.Caido) GUI.color = CinzaCaido;
                    GUI.Label(new Rect(centro.x - 20f, centro.y - 20f, 40f, 40f), "►", _estiloSeta);
                    GUI.matrix = antes;
                    Vector2 nome = centro - new Vector2(direcao.x, -direcao.y) * 46f;
                    GUI.Label(new Rect(nome.x - 80f, nome.y - 11f, 160f, 22f), a.Nome, _estiloNomeSeta);
                    GUI.color = Color.white;
                }
            }
        }

        // ---------- Quadro ----------

        private static void Coletar(SessaoCoop sessao, RemotePlayerAvatar eco)
        {
            DoQuadro.Clear();
            if (sessao != null)
            {
                foreach (Par p in sessao.Pares.Values)
                {
                    if (p.Avatar == null || !p.Avatar.gameObject.activeInHierarchy) continue;
                    DoQuadro.Add(new Aliado { Chave = p.Id.m_SteamID, Nome = p.Nome, Avatar = p.Avatar, Caido = p.Morto });
                }
            }
            if (eco != null && eco.gameObject.activeInHierarchy) DoQuadro.Add(new Aliado { Chave = 0, Nome = "Eco", Avatar = eco });
        }

        private static void Posicionar()
        {
            Camera cam = Camera.main;
            foreach (Visual v in Visuais.Values) v.Visto = false;
            if (cam != null)
            {
                Camera camUi = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
                // 0.31.3: uma leitura por quadro (antes, a cada aliado e a cada conta).
                float larguraTela = Screen.width, alturaTela = Screen.height, dt = Time.unscaledDeltaTime;
                float escala = alturaTela / 1080f;
                bool comNome = CoopRuntime.MostrarNomes.Value, mostrarVida = MostrarVida.Value, mostrarSeta = MostrarSeta.Value;
                foreach (Aliado a in DoQuadro)
                {
                    Visual v = Obter(a.Chave);
                    v.Visto = true;
                    Vector3 pos = a.Avatar.transform.position;
                    bool naTela = NaTela(cam, pos, larguraTela, alturaTela);

                    Vector3 cabeca = cam.WorldToScreenPoint(pos + Vector3.up * AlturaCabeca);
                    bool barra = mostrarVida && naTela && cabeca.z > 0f;
                    Ativar(v.BarraGo, ref v.BarraLigada, barra);
                    if (barra)
                    {
                        float baixo = comNome && a.Chave != 0 ? AbaixoDoNome : 0f; // o eco não tem nome sobre a cabeça
                        Colocar(v.Barra, new Vector2(cabeca.x, cabeca.y - baixo), camUi);
                        float vida = a.Caido ? 0f : Mathf.Clamp01(a.Avatar.Vida);
                        v.ValorAtraso = a.Caido ? 0f : vida >= v.ValorAtraso || v.ValorAtraso < 0f ? vida : Mathf.Max(vida, v.ValorAtraso - AtrasoQueda * dt);
                        if (v.Preenche != null) v.Preenche.fillAmount = vida;
                        if (v.Atraso != null) v.Atraso.fillAmount = v.ValorAtraso;
                        float barreira = a.Caido ? 0f : Mathf.Clamp01(a.Avatar.Barreira);
                        if (v.Barreira != null) v.Barreira.fillAmount = FracaoBarreira(barreira);
                        if (v.Brilho != null)
                        {
                            float brilho = Mathf.MoveTowards(v.BrilhoPosto, barreira > 0f ? 1f : 0f, dt / DuracaoBrilho);
                            if (brilho != v.BrilhoPosto)
                            {
                                v.BrilhoPosto = brilho;
                                v.Brilho.alpha = brilho;
                            }
                        }
                    }
                    if (v.Rotulo != null) Ativar(v.RotuloGo, ref v.RotuloLigado, barra && a.Caido);
                    Fase2.Reanimacao.Progresso pr = default;
                    bool reanimando = barra && a.Caido && Fase2.Reanimacao.ProgressoDe(a.Chave, out pr);
                    if (v.Rotulo != null && barra && a.Caido)
                    {
                        string texto = reanimando ? TextoEstado(pr.Estado) : TextoCaido;
                        if (v.Rotulo.text != texto) v.Rotulo.text = texto;
                        v.Rotulo.color = reanimando ? CorEstado(pr.Estado) : CinzaCaido;
                    }
                    Ativar(v.ProgressoGo, ref v.ProgressoLigado, reanimando);
                    if (reanimando)
                    {
                        v.ProgressoEnche.anchorMax = new Vector2(pr.Fracao, 1f);
                        v.ProgressoCor.color = pr.Estado == Fase2.Reanimacao.Estado.Interrompido ? VermelhoInterrompido : VerdeReanimar;
                    }

                    bool seta = mostrarSeta && !naTela;
                    Ativar(v.SetaGo, ref v.SetaLigada, seta);
                    Ativar(v.SetaNomeGo, ref v.SetaNomeLigada, seta);
                    if (seta)
                    {
                        Borda(cam, pos, escala, larguraTela, alturaTela, out Vector2 ponto, out float angulo, out Vector2 direcao);
                        Colocar(v.Seta, ponto, camUi);
                        v.Seta.localEulerAngles = new Vector3(0f, 0f, angulo + GiroBaseSeta);
                        Colocar(v.SetaNome, ponto - direcao * (_tamanhoSeta.y + 14f) * escala, camUi);
                        if (v.SetaTexto.text != a.Nome) v.SetaTexto.text = a.Nome;
                        v.SetaImagem.color = a.Caido ? CinzaCaido : _corSeta;
                        v.SetaTexto.color = a.Caido ? CinzaCaido : _corNome;
                    }
                }
            }
            Apagar.Clear();
            foreach (KeyValuePair<ulong, Visual> kv in Visuais)
            {
                if (!kv.Value.Visto) Apagar.Add(kv.Key);
            }
            foreach (ulong chave in Apagar)
            {
                Destruir(Visuais[chave]);
                Visuais.Remove(chave);
            }
        }

        /// <summary>O corpo do aliado (não a cabeça) está dentro da tela e na frente da câmera.</summary>
        private static bool NaTela(Camera cam, Vector3 pos, float larguraTela, float alturaTela)
        {
            Vector3 t = cam.WorldToScreenPoint(pos + Vector3.up * AlturaCorpo);
            return t.z > 0f && t.x >= 0f && t.x <= larguraTela && t.y >= 0f && t.y <= alturaTela;
        }

        /// <summary>
        /// Ponto na borda da tela (pixels, y para cima) na direção do aliado, a MargemBorda da beirada, e o ângulo dessa
        /// direção em graus (0 = direita, anti-horário). A direção sai do espaço da câmera (x, y): na frente dela é a mesma
        /// da tela (perspectiva ou ortográfica) e atrás continua com sentido, sem a divisão por z da projeção.
        /// </summary>
        internal static void Borda(Camera cam, Vector3 pos, float escala, out Vector2 ponto, out float angulo, out Vector2 direcao) =>
            Borda(cam, pos, escala, Screen.width, Screen.height, out ponto, out angulo, out direcao);

        /// <summary>A mesma conta, com o tamanho da tela já lido por quem chama (uma leitura por quadro).</summary>
        private static void Borda(Camera cam, Vector3 pos, float escala, float larguraTela, float alturaTela, out Vector2 ponto, out float angulo, out Vector2 direcao)
        {
            Vector3 c = cam.transform.InverseTransformPoint(pos + Vector3.up * AlturaCorpo);
            var centro = new Vector2(larguraTela / 2f, alturaTela / 2f);
            var d = new Vector2(c.x, c.y);
            if (d.sqrMagnitude < 1e-6f) d = Vector2.down; // bem atrás da câmera, no eixo: aponta para baixo
            direcao = d.normalized;
            float margem = MargemBorda * escala;
            float meiaL = Mathf.Max(1f, centro.x - margem), meiaA = Mathf.Max(1f, centro.y - margem);
            float k = Mathf.Min(direcao.x != 0f ? meiaL / Mathf.Abs(direcao.x) : 1e9f, direcao.y != 0f ? meiaA / Mathf.Abs(direcao.y) : 1e9f);
            ponto = centro + direcao * k;
            angulo = Mathf.Atan2(direcao.y, direcao.x) * 57.29578f; // Mathf.Rad2Deg foi removido pelo stripping
        }

        /// <summary>Põe o centro da peça no ponto da tela (pixels, y para cima), no espaço da camada de barras do jogo.</summary>
        private static void Colocar(RectTransform rt, Vector2 tela, Camera camUi)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_camada, tela, camUi, out Vector2 local)) return;
            Vector2 meio = Vector2.Scale(rt.rect.center, rt.localScale);
            rt.localPosition = new Vector3(local.x - meio.x, local.y - meio.y, 0f);
        }

        /// <summary>Liga ou desliga a peça só quando muda; "atual" é o estado guardado no Visual (0.31.3).</summary>
        private static void Ativar(GameObject peca, ref bool atual, bool ligado)
        {
            if (atual == ligado || peca == null) return; // peça destruída fora daqui: fica como está, sem erro (como antes)
            atual = ligado;
            peca.SetActive(ligado);
        }

        private static void EsconderTudo()
        {
            if (Visuais.Count == 0) return;
            foreach (Visual v in Visuais.Values) Destruir(v);
            Visuais.Clear();
        }

        // ---------- Peças do jogo ----------

        private static bool Preparar()
        {
            if (Time.unscaledTime < _proximaBusca) return false;
            _proximaBusca = Time.unscaledTime + IntervaloBusca;
            int buscasAntes = _buscasNaCena;
            try
            {
                if (_camada == null) // a HUD anterior morreu com a cena (as cópias foram junto) ou ainda não existe
                {
                    EsconderTudo();
                    _hud = HudDoJogo.Atual() ?? Achar<GenericHUD>();
                    _camada = _hud != null ? _hud.LifeBars : null;
                    Canvas perto = _camada != null ? _camada.GetComponentInParent<Canvas>(true) : null; // a HUD pode estar inativa
                    _canvas = perto != null ? perto.rootCanvas : null; // o modo (e a câmera) vale o do canvas raiz
                }
                // Telas sem HUD (menu, carregamento): só esperar. Desativado: a HUD segue sendo achada para a reserva.
                if (_camada == null || _desativado) return false;
                if (_modeloBarra != null) return true;

                double inicio = Desempenho.Agora();
                UIObjectPoolController pool = UIObjectPoolController.Instance; // nulo sem registro: não cria (ObjectPoolController.GetInstance)
                if (pool == null) pool = Achar<UIObjectPoolController>(qualquer: true);
                GameObject prefab = pool != null && BarraCompanheiroRef != null ? BarraCompanheiroRef(pool) : null;
                if (prefab == null || !AcharSeta())
                {
                    if (!_avisouSemPeca)
                    {
                        Plugin.Log.LogInfo($"[Aliados] Peças da interface do jogo não encontradas (barra: {(prefab != null ? "ok" : "faltou")}, seta: {(_spriteSeta != null ? "ok" : "faltou")}); usando o desenho simples.");
                        _avisouSemPeca = true;
                    }
                    return false;
                }
                MontarModelo(prefab);
                Desempenho.SomarDetalhe(Desempenho.Detalhe.MontarPecasAliados, inicio);
                Plugin.Log.LogInfo($"[Aliados] Barra de vida e seta montadas com a interface do jogo (seta: {_spriteSeta.name}, {_tamanhoSeta.x:0}x{_tamanhoSeta.y:0}; buscas na cena: {_buscasNaCena - buscasAntes}).");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Aliados] Não deu para montar a barra e a seta com a interface do jogo; usando o desenho simples: {e}");
                Desativar();
                return false;
            }
        }

        /// <summary>Desliga as peças do jogo até reabrir o jogo (erro): o OnGUI desenha a reserva.</summary>
        private static void Desativar()
        {
            _desativado = true;
            EsconderTudo();
            if (_deposito != null) Object.Destroy(_deposito);
            _deposito = _modeloBarra = null;
        }

        /// <summary>
        /// Objeto ativo da cena; sem ele, um inativo da cena; com qualquer = true, até o prefab (sem cena), que serve
        /// quando só interessam as referências guardadas nele.
        /// </summary>
        private static T Achar<T>(bool qualquer = false) where T : Component
        {
            double inicio = Desempenho.Agora(); // varre todos os objetos carregados: 50 a 90 ms em jogo (reserva desde a 0.30.3)
            _buscasNaCena++;
            try { return Varrer<T>(qualquer); }
            finally { Desempenho.SomarDetalhe(Desempenho.Detalhe.BuscarPecasHud, inicio); }
        }

        private static T Varrer<T>(bool qualquer) where T : Component
        {
            T daCena = null, reserva = null;
            foreach (T o in Resources.FindObjectsOfTypeAll<T>())
            {
                if (o == null) continue;
                if (o.gameObject.scene.IsValid())
                {
                    if (o.gameObject.activeInHierarchy) return o;
                    if (daCena == null) daCena = o;
                }
                else if (qualquer && reserva == null) reserva = o;
            }
            return daCena ?? reserva;
        }

        private static bool AcharSeta()
        {
            if (_spriteSeta != null) return true;
            // As duas ficam dentro da HUD (a SurvivalView é uma página da barra de missões); a varredura é só a reserva.
            SurvivalView sobrevivencia = _hud != null ? _hud.GetComponentInChildren<SurvivalView>(true) : null;
            QuestTabBar missoes = _hud != null ? _hud.QuestTabBar : null;
            Image img = (SetaSobrevivenciaRef != null ? ImagemDe(sobrevivencia != null ? sobrevivencia : Achar<SurvivalView>(qualquer: true), v => SetaSobrevivenciaRef(v)) : null)
                        ?? (SetaMissoesRef != null ? ImagemDe(missoes != null ? missoes : Achar<QuestTabBar>(qualquer: true), q => SetaMissoesRef(q)) : null);
            if (img == null || img.sprite == null) return false;
            _spriteSeta = img.sprite;
            _corSeta = img.color.a > 0.05f ? img.color : Color.white;
            Vector2 t = img.rectTransform.rect.size;
            const float lado = 44f; // a seta da lista suspensa (UI_Icon_DropDownArrow) tem margem: em 28 saía com ~15 px na tela
            _tamanhoSeta = t.x > 0f && t.y > 0f ? t * (lado / Mathf.Max(t.x, t.y)) : new Vector2(lado, lado);
            return true;
        }

        private static Image ImagemDe<T>(T dono, Func<T, RectTransform> campo) where T : Component
        {
            if (dono == null) return null;
            RectTransform rt = campo(dono);
            if (rt == null) return null;
            Image img = rt.GetComponent<Image>();
            return img != null && img.sprite != null ? img : rt.GetComponentInChildren<Image>(true);
        }

        /// <summary>
        /// Copia a barra do companheiro para o depósito inativo, guarda onde ficam o preenchimento e a faixa clara
        /// (_fill e _backgroundFill do StatBasedValueView) e a barreira (_barrierFill e _barrierGlow do LifeView, 0.28.0),
        /// apaga nome, textos e dano atrasado e tira os scripts.
        /// </summary>
        private static void MontarModelo(GameObject prefab)
        {
            _deposito = new GameObject("HellClockCoop.AliadosDeposito");
            Object.DontDestroyOnLoad(_deposito);
            _deposito.SetActive(false);
            GameObject m = Object.Instantiate(prefab, _deposito.transform, false);
            m.name = "HellClockCoop.BarraAliado";
            Component vista = m.GetComponent<TargetFloatingLifeView>();
            if (vista == null) throw new InvalidOperationException("a barra do companheiro não tem TargetFloatingLifeView");

            Image preenche = Campo<Image>(vista, "_fill");
            Image atraso = Campo<Image>(vista, "_backgroundFill");
            if (preenche == null) throw new InvalidOperationException("a barra do companheiro não tem _fill");
            _caminhoPreenche = Caminho(m.transform, preenche.transform);
            _caminhoAtraso = atraso != null ? Caminho(m.transform, atraso.transform) : null;

            TMP_Text nome = Campo<TMP_Text>(vista, "_nameLabel");
            if (nome != null)
            {
                _fonte = nome.font;
                _materialFonte = nome.fontSharedMaterial;
                _corNome = nome.color;
            }
            foreach (string f in new[] { "_nameLabel", "_subNameLabel", "_valueText", "_barrierText" })
            {
                TMP_Text t = Campo<TMP_Text>(vista, f);
                if (t != null) t.enabled = false;
            }
            foreach (string f in new[] { "_descriptionBg", "_delayedDamageFill", "_delayedDamageBgFill" })
            {
                Image i = Campo<Image>(vista, f);
                if (i != null && i != preenche && i != atraso) i.enabled = false;
            }
            Image barreira = Campo<Image>(vista, "_barrierFill");
            CanvasGroup brilho = Campo<CanvasGroup>(vista, "_barrierGlow");
            _caminhoBarreira = barreira != null && barreira != preenche && barreira != atraso ? Caminho(m.transform, barreira.transform) : null;
            _caminhoBrilho = brilho != null ? Caminho(m.transform, brilho.transform) : null;
            object mapa = AccessTools.Field(vista.GetType(), "_barrierMapConversion")?.GetValue(vista);
            if (mapa is Vector2 mp && mp.y > mp.x) _mapaBarreira = mp;

            PainelNativo.Limpar(m);
            foreach (CanvasGroup g in m.GetComponentsInChildren<CanvasGroup>(true))
            {
                g.alpha = g == brilho ? 0f : 1f; // a barra do jogo começa escondida (só aparece com dano)
                g.blocksRaycasts = false;
                g.interactable = false;
            }
            foreach (Graphic g in m.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            Preencher(preenche);
            if (atraso != null) Preencher(atraso);
            if (_caminhoBarreira != null)
            {
                barreira.enabled = true;
                Preencher(barreira);
                barreira.fillAmount = 0f;
            }
            _modeloBarra = m;
        }

        private static void Preencher(Image i)
        {
            if (i.type == Image.Type.Filled) return;
            i.type = Image.Type.Filled;
            i.fillMethod = Image.FillMethod.Horizontal;
            i.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        private static T Campo<T>(object dono, string nome) where T : class =>
            AccessTools.Field(dono.GetType(), nome)?.GetValue(dono) as T;

        private static string Caminho(Transform raiz, Transform t)
        {
            var partes = new List<string>();
            for (Transform a = t; a != null && a != raiz; a = a.parent) partes.Insert(0, a.name);
            return string.Join("/", partes.ToArray());
        }

        private static Visual Obter(ulong chave)
        {
            if (Visuais.TryGetValue(chave, out Visual v) && v.Barra != null && v.Seta != null) return v;
            if (v != null) Destruir(v);
            double inicio = Desempenho.Agora();
            v = new Visual();

            GameObject b = Object.Instantiate(_modeloBarra, _camada, false);
            b.name = "HellClockCoop.BarraAliado";
            v.Barra = (RectTransform)b.transform;
            v.Preenche = Peca(v.Barra, _caminhoPreenche);
            v.Atraso = _caminhoAtraso != null ? Peca(v.Barra, _caminhoAtraso) : null;
            v.Barreira = _caminhoBarreira != null ? Peca(v.Barra, _caminhoBarreira) : null;
            Transform brilho = _caminhoBrilho != null ? (_caminhoBrilho.Length == 0 ? v.Barra : v.Barra.Find(_caminhoBrilho)) : null;
            v.Brilho = brilho != null ? brilho.GetComponent<CanvasGroup>() : null;
            v.BarraGo = b;
            v.BarraLigada = b.activeSelf;
            v.BrilhoPosto = v.Brilho != null ? v.Brilho.alpha : 0f;

            var s = new GameObject("HellClockCoop.SetaAliado", typeof(RectTransform));
            v.Seta = (RectTransform)s.transform;
            v.Seta.SetParent(_camada, false);
            v.Seta.sizeDelta = _tamanhoSeta;
            v.SetaImagem = s.AddComponent<Image>();
            v.SetaImagem.sprite = _spriteSeta;
            v.SetaImagem.color = _corSeta;
            v.SetaImagem.preserveAspect = true;
            v.SetaImagem.raycastTarget = false;
            v.SetaGo = s;
            v.SetaLigada = true; // objeto novo nasce ligado

            var n = new GameObject("HellClockCoop.SetaNome", typeof(RectTransform));
            v.SetaNome = (RectTransform)n.transform;
            v.SetaNome.SetParent(_camada, false);
            v.SetaNome.sizeDelta = new Vector2(240f, 30f);
            TextMeshProUGUI texto = n.AddComponent<TextMeshProUGUI>();
            if (_fonte != null) texto.font = _fonte;
            if (_materialFonte != null) texto.fontSharedMaterial = _materialFonte;
            texto.color = _corNome;
            texto.fontSize = 20f;
            texto.alignment = TextAlignmentOptions.Center;
            texto.textWrappingMode = TextWrappingModes.NoWrap;
            texto.raycastTarget = false;
            texto.richText = false; // nome da Steam com <tags> sai como texto
            v.SetaTexto = texto;
            v.SetaNomeGo = n;
            v.SetaNomeLigada = true;

            var r = new GameObject("HellClockCoop.Caido", typeof(RectTransform)); // "caído" sobre a moldura da barra
            var rr = (RectTransform)r.transform;
            rr.SetParent(v.Barra, false);
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0.5f, 0.5f);
            rr.anchoredPosition = Vector2.zero;
            rr.sizeDelta = new Vector2(200f, 24f);
            TextMeshProUGUI rotulo = r.AddComponent<TextMeshProUGUI>();
            if (_fonte != null) rotulo.font = _fonte;
            if (_materialFonte != null) rotulo.fontSharedMaterial = _materialFonte;
            rotulo.color = CinzaCaido;
            rotulo.fontSize = 16f;
            rotulo.alignment = TextAlignmentOptions.Center;
            rotulo.textWrappingMode = TextWrappingModes.NoWrap;
            rotulo.raycastTarget = false;
            rotulo.text = TextoCaido;
            r.SetActive(false);
            v.Rotulo = rotulo;
            v.RotuloGo = r;

            // Progresso da reanimação (0.16.0): trilho preto 62% com o preenchimento pela âncora (sem sprite, o
            // Image.Filled não corta), logo abaixo da barra.
            var g = new GameObject("HellClockCoop.Reanimacao", typeof(RectTransform));
            v.Progresso = (RectTransform)g.transform;
            v.Progresso.SetParent(v.Barra, false);
            v.Progresso.anchorMin = v.Progresso.anchorMax = v.Progresso.pivot = new Vector2(0.5f, 0.5f);
            v.Progresso.anchoredPosition = new Vector2(0f, -AbaixoDaBarra);
            v.Progresso.sizeDelta = new Vector2(LarguraProgresso, AlturaProgresso);
            Image trilho = g.AddComponent<Image>();
            trilho.color = new Color(0f, 0f, 0f, 0.62f);
            trilho.raycastTarget = false;
            var e = new GameObject("Enche", typeof(RectTransform));
            v.ProgressoEnche = (RectTransform)e.transform;
            v.ProgressoEnche.SetParent(v.Progresso, false);
            v.ProgressoEnche.anchorMin = Vector2.zero;
            v.ProgressoEnche.anchorMax = new Vector2(0f, 1f);
            v.ProgressoEnche.offsetMin = new Vector2(1f, 1f);
            v.ProgressoEnche.offsetMax = new Vector2(-1f, -1f);
            v.ProgressoCor = e.AddComponent<Image>();
            v.ProgressoCor.color = VerdeReanimar;
            v.ProgressoCor.raycastTarget = false;
            g.SetActive(false);
            v.ProgressoGo = g;

            Visuais[chave] = v;
            Desempenho.SomarDetalhe(Desempenho.Detalhe.CriarBarraAliado, inicio);
            return v;
        }

        // ---------- Aquecimento (0.31.3) ----------

        /// <summary>
        /// A HUD do jogo foi achada e está na tela. Se ainda não achou, só procura quando o registro do jogo já tem a HUD
        /// (HudDoJogo): a espera do aquecimento não pode cair na varredura de reserva do Preparar (50 a 90 ms por busca).
        /// </summary>
        internal static bool HudPronta()
        {
            if (_camada == null)
            {
                if (HudDoJogo.Atual() == null) return false;
                Preparar();
            }
            return HudVisivel;
        }

        /// <summary>
        /// Monta a barra, a seta e o nome de um aliado de mentira (o avatar de aquecimento, que fica longe da tela) e passa
        /// uma vez pelo posicionamento, ao entrar numa sala (CoopRuntime), fora do quadro em que o primeiro aliado aparece:
        /// nesse quadro esta parte custava 11,4 ms, quase tudo custo de primeira vez. Nada chega à tela: o canvas monta as
        /// peças aqui mesmo e elas são destruídas no mesmo passo. Só sem aliado na tela. Falso: a HUD do jogo ou a câmera
        /// ainda não estão prontas (tentar de novo).
        /// </summary>
        internal static bool Aquecer(RemotePlayerAvatar avatar)
        {
            if (_desativado || _avisouSemPeca || Visuais.Count > 0 || !(MostrarVida.Value || MostrarSeta.Value)) return true; // nada a aquecer
            if (avatar == null || Camera.main == null || (!Nativo && !Preparar())) return _desativado || _avisouSemPeca;
            try
            {
                DoQuadro.Clear();
                DoQuadro.Add(new Aliado { Chave = ChaveAquecimento, Nome = "Aliado", Avatar = avatar });
                Posicionar();
                Canvas.ForceUpdateCanvases(); // a seta e o nome são montados agora, não no primeiro aliado
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Aliados] Aquecimento da barra e da seta falhou (elas seguem; a primeira aparição de um aliado pode pesar): {e.Message}");
            }
            finally
            {
                DoQuadro.Clear();
                if (Visuais.TryGetValue(ChaveAquecimento, out Visual falso)) // desligadas já: a destruição só vale no fim do quadro
                {
                    if (falso.Barra != null) falso.BarraGo.SetActive(false);
                    if (falso.Seta != null) falso.SetaGo.SetActive(false);
                    if (falso.SetaNome != null) falso.SetaNomeGo.SetActive(false);
                }
                EsconderTudo(); // destrói as peças de mentira (o modelo e as peças do jogo ficam montados)
            }
            return true;
        }

        /// <summary>fillAmount da imagem da barreira para a fração (LifeView.Update: Map(0, 1, conv.x, conv.y, fração)).</summary>
        internal static float FracaoBarreira(float fracao) => fracao <= 0f ? 0f : Mathf.Lerp(_mapaBarreira.x, _mapaBarreira.y, Mathf.Clamp01(fracao));

        private static Image Peca(Transform raiz, string caminho)
        {
            Transform t = string.IsNullOrEmpty(caminho) ? raiz : raiz.Find(caminho);
            return t != null ? t.GetComponent<Image>() : null;
        }

        private static void Destruir(Visual v)
        {
            if (v.Barra != null) Object.Destroy(v.Barra.gameObject);
            if (v.Seta != null) Object.Destroy(v.Seta.gameObject);
            if (v.SetaNome != null) Object.Destroy(v.SetaNome.gameObject);
        }
    }
}
