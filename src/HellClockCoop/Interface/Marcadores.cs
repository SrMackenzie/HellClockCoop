using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HellClock.Player;
using HellClockCoop.Avatar;
using HellClockCoop.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Marcador (ping, 0.17.0, trilha 5). Decisões do autor em 22/11/2026:
    /// - Botão do meio do mouse marca o chão sob o cursor; R3 no controle marca alguns metros à frente do personagem.
    ///   A tecla fica no .cfg ([Teclas] Marcador); o controle é fixo (R3).
    /// - O marcador dura 5 s; cada jogador tem 1 ativo (marcar de novo move o antigo), com recarga de 1 s.
    /// - Nome de quem marcou na cor do aliado (a mesma dos números de dano); o seu marcador sai sem nome.
    /// - Visual opção A "seta no chão", só com peças do jogo: a seta da HUD (a mesma da seta dos aliados) apontando o
    ///   ponto, com sombra no chão; cai e quica ao surgir, pulsa enquanto vive e sobe sumindo no fim. Fora da tela, a
    ///   seta vai para a borda com o nome e a distância. Peças na camada GenericHUD.LifeBars (somem com a HUD).
    ///   Sem as peças, reserva no OnGUI (como o AliadosNaTela).
    /// Rede: Marcador (todos → todos no mesmo andar, confiável). Quem recebe confere cena, andar e a distância entre o
    /// ponto e o avatar de quem marcou (Validacao.MarcadorValido). Só informação: não muda o jogo nem a dificuldade.
    /// Modo eco: o fantasma repete o seu marcador 1 s depois, deslocado como ele, como aliado "Eco".
    /// </summary>
    internal static class Marcadores
    {
        internal const float Duracao = 5f;
        internal const float Recarga = 1f;
        private const float RecargaRecebida = 0.5f;     // folga para o atraso da rede sobre a recarga de 1 s
        private const float FrenteControle = 4f;        // metros à frente do personagem no controle
        private const float AlturaSeta = 1.2f;          // metros acima do ponto
        private const float Entrada = 0.25f, Quique = 0.15f, Saida = 0.6f;
        private const ulong ChaveVoce = 1;              // 0 é o eco; SteamIDs são bem maiores
        private const uint ForaDaRun = 4294967295u;     // uint.MaxValue (removido do mscorlib do jogo)

        internal static ConfigEntry<string> Tecla;

        private sealed class Marca
        {
            public ulong Chave;
            public string Nome;
            public Vector3 Ponto;
            public int Cena;
            public uint Andar;
            public float Criado;
            public Color Cor;
            // Peças do jogo (nulas na reserva)
            public RectTransform Sombra, Contorno, Seta, Rotulo;
            public Image SombraImg, ContornoImg, SetaImg;
            public TMP_Text Texto;
        }

        private static readonly Dictionary<ulong, Marca> Marcas = new Dictionary<ulong, Marca>();
        private static readonly List<ulong> Apagar = new List<ulong>();
        private static readonly Queue<KeyValuePair<float, Vector3>> FilaEco = new Queue<KeyValuePair<float, Vector3>>();
        private static float _ultimoEnvio = -10f;
        private static bool _desativado, _reservaComErro, _parado;
        private static Sprite _sombra;
        private static GUIStyle _estiloSeta, _estiloNome;

        private static SessaoCoop Sessao => Fase2.RunCompartilhada.Sessao;

        internal static void Configurar(ConfigFile config)
        {
            Tecla = config.Bind("Teclas", "Marcador", "Mouse2",
                "Marca um ponto no chão onde está o cursor, que todos do grupo veem por 5 s. Mouse2 = botão do meio do mouse " +
                "(Mouse3 e Mouse4 = botões laterais); aceita também uma tecla, como G. No controle: R3 (apertar o analógico direito).");
        }

        // ---------------- Tecla e envio ----------------

        /// <summary>Update do CoopRuntime, com os menus do coop fechados: lê a tecla e marca.</summary>
        internal static void LerTecla(PlayerController jogador, bool ecoAtivo)
        {
            if (_parado || jogador == null || Time.timeScale <= 0f) return; // pausa do jogo
            bool mouse = HellClockCoop.Teclas.Pressionou(Tecla.Value);
            bool controle = !mouse && HellClockCoop.Teclas.Controle(c => c.rightStickButton, KeyCode.JoystickButton9);
            if (!mouse && !controle) return;
            SessaoCoop sessao = Sessao;
            bool grupo = sessao != null && sessao.EmSessao && sessao.AlguemNoMesmoAndar;
            if (!grupo && !ecoAtivo) return; // sozinho não há quem veja
            float agora = Time.unscaledTime;
            if (agora - _ultimoEnvio < Recarga) return;
            if (mouse && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return; // inventário, mapa...
            if (!PontoMarcado(jogador, mouse, out Vector3 ponto)) return;
            _ultimoEnvio = agora;

            int cena = LocalPlayerSampler.CenaAtual();
            uint andar = AndarLocal();
            Colocar(ChaveVoce, "Você", ponto, cena, andar, CorVoce);
            if (grupo)
                sessao.EnviarParaMesmoAndar(new Marcador { Cena = cena, IndiceAndar = andar, X = ponto.x, Y = ponto.y, Z = ponto.z }.Serializar(), confiavel: true);
            if (ecoAtivo) FilaEco.Enqueue(new KeyValuePair<float, Vector3>(agora + CoopRuntime.AtrasoEco, ponto));
        }

        /// <summary>Mouse: o chão sob o cursor (plano na altura do personagem), até o alcance. Controle: à frente.</summary>
        private static bool PontoMarcado(PlayerController jogador, bool mouse, out Vector3 ponto)
        {
            Vector3 pe = jogador.transform.position;
            ponto = pe;
            if (!mouse)
            {
                Transform modelo = jogador.Animator != null ? jogador.Animator.transform : jogador.transform;
                Vector3 frente = modelo.forward;
                frente.y = 0f;
                if (frente.sqrMagnitude < 1e-4f) frente = Vector3.forward;
                ponto = pe + frente.normalized * FrenteControle;
                return true;
            }
            Camera cam = Camera.main;
            if (cam == null || !HellClockCoop.Teclas.PosicaoMouse(out Vector2 tela)) return false;
            Ray raio = cam.ScreenPointToRay(new Vector3(tela.x, tela.y, 0f));
            var chao = new Plane(Vector3.up, pe);
            if (!chao.Raycast(raio, out float d)) return false;
            ponto = raio.GetPoint(d);
            Vector3 delta = ponto - pe;
            delta.y = 0f;
            float alcance = Validacao.AlcanceMarcadorMetros;
            if (delta.sqrMagnitude > alcance * alcance) ponto = pe + delta.normalized * alcance; // cursor além do alcance: na direção dele
            return true;
        }

        /// <summary>Andar da run (ou ForaDaRun no hub e nos menus): o marcador só vale no mesmo andar de quem marcou.</summary>
        private static uint AndarLocal() => Fase2.Reanimacao.EmRun ? Fase2.RunCompartilhada.AndarAtual : ForaDaRun;

        private static Color CorVoce => AliadosNaTela.CorSeta;

        private static void Colocar(ulong chave, string nome, Vector3 ponto, int cena, uint andar, Color cor)
        {
            if (!Marcas.TryGetValue(chave, out Marca m))
            {
                m = new Marca { Chave = chave };
                Marcas[chave] = m;
            }
            m.Nome = nome;
            m.Ponto = ponto;
            m.Cena = cena;
            m.Andar = andar;
            m.Cor = cor;
            m.Criado = Time.unscaledTime; // novo substitui: a animação de entrada recomeça no ponto novo
        }

        // ---------------- Rede ----------------

        internal static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            if (_parado || !de.Compativel || !de.MesmoAndar || CoopRuntime.JogadorLocal() == null) return;
            Marcador m = Marcador.Desserializar(dados);
            var ponto = new Vector3(m.X, m.Y, m.Z);
            float d = 3.4e38f, desnivel = 3.4e38f; // sem avatar de quem marcou: recusa
            if (de.Avatar != null && Validacao.Finito(m.X) && Validacao.Finito(m.Y) && Validacao.Finito(m.Z))
            {
                Vector3 delta = ponto - de.Avatar.transform.position;
                desnivel = Mathf.Abs(delta.y);
                delta.y = 0f;
                d = delta.magnitude;
            }
            if (!Validacao.MarcadorValido(m.Cena, m.IndiceAndar, m.X, m.Y, m.Z, LocalPlayerSampler.CenaAtual(), AndarLocal(), d, desnivel)) return;
            ulong chave = de.Id.m_SteamID;
            if (Marcas.TryGetValue(chave, out Marca antiga) && Time.unscaledTime - antiga.Criado < RecargaRecebida) return;
            Colocar(chave, de.Nome, ponto, m.Cena, m.IndiceAndar, Fase3.DanosVisiveis.CorDoAliado(chave));
            Plugin.Log.LogDebug($"[Marcador] {de.Nome} marcou um ponto."); // até 2 por segundo por par: fora do log padrão
        }

        // ---------------- Tela ----------------

        /// <summary>LateUpdate do CoopRuntime (depois do AliadosNaTela): eco, validade e peças na tela.</summary>
        internal static void Atualizar(SessaoCoop sessao, RemotePlayerAvatar eco)
        {
            if (_parado) return;
            try
            {
                AtualizarQuadro(sessao, eco);
            }
            catch (Exception e) // fora do laço de rede: um erro aqui não pode repetir a cada quadro
            {
                foreach (Marca m in Marcas.Values) Destruir(m);
                if (_desativado) // já na reserva e errou de novo: o erro não é das peças; desliga o marcador
                {
                    Plugin.Log.LogWarning($"[Marcador] Erro de novo, já com o desenho simples; marcador desligado até reabrir o jogo: {e}");
                    _parado = true;
                    Marcas.Clear();
                    FilaEco.Clear();
                    return;
                }
                Plugin.Log.LogWarning($"[Marcador] Erro ao desenhar o marcador; desligadas as peças do jogo: {e}");
                _desativado = true;
            }
        }

        private static void AtualizarQuadro(SessaoCoop sessao, RemotePlayerAvatar eco)
        {
            float agora = Time.unscaledTime;
            PlayerController local = CoopRuntime.JogadorLocal();
            int cena = local != null ? LocalPlayerSampler.CenaAtual() : -1;
            uint andar = AndarLocal();

            if (eco == null) FilaEco.Clear();
            while (FilaEco.Count > 0 && FilaEco.Peek().Key <= agora)
            {
                Vector3 p = FilaEco.Dequeue().Value;
                if (eco != null) Colocar(0, "Eco", p + eco.DeslocamentoMundo, cena, andar, Fase3.DanosVisiveis.CorDoAliado(0));
            }

            Apagar.Clear();
            foreach (Marca m in Marcas.Values)
            {
                bool saiu = m.Chave > ChaveVoce && (sessao == null || !sessao.Pares.ContainsKey(m.Chave));
                if (agora - m.Criado >= Duracao || m.Cena != cena || m.Andar != andar || saiu || (m.Chave == 0 && eco == null))
                    Apagar.Add(m.Chave);
            }
            foreach (ulong k in Apagar)
            {
                Destruir(Marcas[k]);
                Marcas.Remove(k);
            }
            if (Marcas.Count == 0) return;

            if (_desativado || !AliadosNaTela.PecasDoMarcador(out RectTransform camada, out Camera camUi, out Sprite seta, out Vector2 tamanho))
            {
                foreach (Marca m in Marcas.Values) Destruir(m); // reserva no OnGUI
                return;
            }
            Camera cam = Camera.main;
            if (cam == null) return;
            float escala = Screen.height / 1080f;
            foreach (Marca m in Marcas.Values)
            {
                if (m.Seta == null) Montar(m, camada, seta, tamanho);
                Posicionar(m, cam, camUi, escala, tamanho, local, agora);
            }
        }

        /// <summary>Altura (m) acima do ponto, escala e alfa do marcador na idade dada: cai e quica, pulsa, some subindo.</summary>
        private static void Animacao(float idade, out float altura, out float tamanho, out float alfa)
        {
            altura = AlturaSeta;
            tamanho = 1f + 0.08f * Mathf.Sin(idade * 9.424778f); // pulso de ±8% (1,5 por segundo)
            alfa = 1f;
            if (idade < Entrada)
            {
                float t = idade / Entrada;
                float s = 1f - (1f - t) * (1f - t);
                altura = Mathf.Lerp(AlturaSeta + 0.8f, AlturaSeta, s);
                tamanho = Mathf.Lerp(1.4f, 1f, s);
            }
            else if (idade < Entrada + Quique)
            {
                altura += 0.15f * Mathf.Sin((idade - Entrada) / Quique * 3.1415927f);
            }
            float resta = Duracao - idade;
            if (resta < Saida)
            {
                float a = Mathf.Clamp01(resta / Saida);
                alfa = a;
                altura += (1f - a) * 0.3f;
            }
        }

        private static void Posicionar(Marca m, Camera cam, Camera camUi, float escala, Vector2 tamanhoSeta, PlayerController local, float agora)
        {
            Animacao(agora - m.Criado, out float altura, out float tamanho, out float alfa);
            Vector3 topo = cam.WorldToScreenPoint(m.Ponto + Vector3.up * altura);
            Vector3 chao = cam.WorldToScreenPoint(m.Ponto);
            bool naTela = chao.z > 0f && chao.x >= 0f && chao.x <= Screen.width && chao.y >= 0f && chao.y <= Screen.height;
            Vector2 lado = tamanhoSeta * tamanho; // unidades da camada, como a seta dos aliados; `escala` só nos deslocamentos em pixels

            m.SetaImg.color = Alfa(m.Cor, alfa);
            m.ContornoImg.color = new Color(0f, 0f, 0f, 0.6f * alfa);
            m.Texto.color = Alfa(m.Cor, alfa);
            Ativar(m.Sombra, naTela);
            if (naTela)
            {
                m.SombraImg.color = new Color(0f, 0f, 0f, 0.45f * alfa);
                m.Sombra.sizeDelta = new Vector2(40f, 14f);
                AliadosNaTela.ColocarNaCamada(m.Sombra, chao, camUi);
                m.Seta.sizeDelta = lado;
                m.Contorno.sizeDelta = lado * 1.18f;
                m.Seta.localEulerAngles = m.Contorno.localEulerAngles = Vector3.zero; // a seta do jogo aponta para baixo
                AliadosNaTela.ColocarNaCamada(m.Contorno, topo, camUi);
                AliadosNaTela.ColocarNaCamada(m.Seta, topo, camUi);
                bool comNome = m.Chave != ChaveVoce;
                Ativar(m.Rotulo, comNome);
                if (comNome)
                {
                    Texto(m, m.Nome);
                    AliadosNaTela.ColocarNaCamada(m.Rotulo, new Vector2(topo.x, topo.y + (lado.y / 2f + 14f) * escala), camUi);
                }
                return;
            }
            AliadosNaTela.Borda(cam, m.Ponto, escala, out Vector2 ponto, out float angulo, out Vector2 direcao);
            m.Seta.sizeDelta = lado;
            m.Contorno.sizeDelta = lado * 1.18f;
            m.Seta.localEulerAngles = m.Contorno.localEulerAngles = new Vector3(0f, 0f, angulo + AliadosNaTela.GiroSeta);
            AliadosNaTela.ColocarNaCamada(m.Contorno, ponto, camUi);
            AliadosNaTela.ColocarNaCamada(m.Seta, ponto, camUi);
            Ativar(m.Rotulo, true);
            Texto(m, Rotulo(m, local));
            AliadosNaTela.ColocarNaCamada(m.Rotulo, ponto - direcao * (lado.y + 14f) * escala, camUi);
        }

        /// <summary>Fora da tela: "Nome 18 m" (o seu, só a distância).</summary>
        private static string Rotulo(Marca m, PlayerController local)
        {
            string metros = "";
            if (local != null)
            {
                Vector3 d = m.Ponto - local.transform.position;
                d.y = 0f;
                metros = $"{d.magnitude:0} m";
            }
            return m.Chave == ChaveVoce ? metros : (metros.Length > 0 ? $"{m.Nome} {metros}" : m.Nome);
        }

        private static void Texto(Marca m, string t)
        {
            if (m.Texto.text != t) m.Texto.text = t;
        }

        private static Color Alfa(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        private static void Ativar(RectTransform rt, bool ligado)
        {
            if (rt != null && rt.gameObject.activeSelf != ligado) rt.gameObject.SetActive(ligado);
        }

        private static void Montar(Marca m, RectTransform camada, Sprite seta, Vector2 tamanho)
        {
            double inicio = Desempenho.Agora();
            Destruir(m);
            m.Sombra = Peca("HellClockCoop.MarcadorSombra", camada, SpriteSombra(), out m.SombraImg);
            m.Contorno = Peca("HellClockCoop.MarcadorContorno", camada, seta, out m.ContornoImg);
            m.Seta = Peca("HellClockCoop.Marcador", camada, seta, out m.SetaImg);
            m.Contorno.sizeDelta = m.Seta.sizeDelta = tamanho;

            var n = new GameObject("HellClockCoop.MarcadorNome", typeof(RectTransform));
            m.Rotulo = (RectTransform)n.transform;
            m.Rotulo.SetParent(camada, false);
            m.Rotulo.sizeDelta = new Vector2(260f, 30f);
            TextMeshProUGUI texto = n.AddComponent<TextMeshProUGUI>();
            if (AliadosNaTela.Fonte != null) texto.font = AliadosNaTela.Fonte;
            if (AliadosNaTela.MaterialFonte != null) texto.fontSharedMaterial = AliadosNaTela.MaterialFonte;
            texto.fontSize = 20f;
            texto.alignment = TextAlignmentOptions.Center;
            texto.textWrappingMode = TextWrappingModes.NoWrap;
            texto.raycastTarget = false;
            texto.richText = false; // nome da Steam com <tags> sai como texto
            m.Texto = texto;
            Desempenho.SomarDetalhe(Desempenho.Detalhe.MontarMarcador, inicio);
        }

        private static RectTransform Peca(string nome, RectTransform camada, Sprite sprite, out Image img)
        {
            var o = new GameObject(nome, typeof(RectTransform));
            var rt = (RectTransform)o.transform;
            rt.SetParent(camada, false);
            img = o.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = sprite != _sombra;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>Elipse escura de borda suave, feita por código (sem arte nova): a sombra do marcador no chão.</summary>
        private static Sprite SpriteSombra()
        {
            if (_sombra != null) return _sombra;
            const int lado = 32;
            var tex = new Texture2D(lado, lado, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[lado * lado];
            for (int y = 0; y < lado; y++)
            {
                for (int x = 0; x < lado; x++)
                {
                    float dx = (x + 0.5f) / lado * 2f - 1f, dy = (y + 0.5f) / lado * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * lado + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - r) * 2.5f));
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            _sombra = Sprite.Create(tex, new Rect(0f, 0f, lado, lado), new Vector2(0.5f, 0.5f));
            _sombra.name = "HellClockCoop.SombraMarcador";
            return _sombra;
        }

        private static void Destruir(Marca m)
        {
            foreach (RectTransform rt in new[] { m.Sombra, m.Contorno, m.Seta, m.Rotulo })
                if (rt != null) Object.Destroy(rt.gameObject);
            m.Sombra = m.Contorno = m.Seta = m.Rotulo = null;
        }

        // ---------------- Reserva (OnGUI) ----------------

        /// <summary>OnGUI do CoopRuntime: desenho simples quando as peças do jogo não montaram, com a HUD na tela.</summary>
        internal static void DesenharReserva()
        {
            if (_reservaComErro || Marcas.Count == 0 || Event.current.type != EventType.Repaint) return;
            try
            {
                Camera cam = Camera.main;
                if (cam == null || !AliadosNaTela.HudVisivel) return;
                if (_estiloSeta == null)
                {
                    _estiloSeta = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 30, fontStyle = FontStyle.Bold };
                    _estiloNome = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold, richText = false }; // nome da Steam com <tags> sai como texto
                }
                PlayerController local = CoopRuntime.JogadorLocal();
                float agora = Time.unscaledTime;
                foreach (Marca m in Marcas.Values)
                {
                    if (m.Seta != null) continue; // já nas peças do jogo
                    Animacao(agora - m.Criado, out float altura, out _, out float alfa);
                    GUI.color = Alfa(m.Cor, alfa);
                    Vector3 chao = cam.WorldToScreenPoint(m.Ponto);
                    if (chao.z > 0f && chao.x >= 0f && chao.x <= Screen.width && chao.y >= 0f && chao.y <= Screen.height)
                    {
                        Vector3 topo = cam.WorldToScreenPoint(m.Ponto + Vector3.up * altura);
                        float y = Screen.height - topo.y;
                        GUI.Label(new Rect(topo.x - 20f, y - 20f, 40f, 40f), "▼", _estiloSeta);
                        if (m.Chave != ChaveVoce) GUI.Label(new Rect(topo.x - 100f, y - 44f, 200f, 22f), m.Nome, _estiloNome);
                    }
                    else
                    {
                        AliadosNaTela.Borda(cam, m.Ponto, Screen.height / 1080f, out Vector2 ponto, out float angulo, out Vector2 direcao);
                        var centro = new Vector2(ponto.x, Screen.height - ponto.y); // OnGUI: y para baixo
                        Matrix4x4 antes = GUI.matrix;
                        GUIUtility.RotateAroundPivot(-angulo, centro);
                        GUI.Label(new Rect(centro.x - 20f, centro.y - 20f, 40f, 40f), "►", _estiloSeta);
                        GUI.matrix = antes;
                        Vector2 nome = centro - new Vector2(direcao.x, -direcao.y) * 46f;
                        GUI.Label(new Rect(nome.x - 100f, nome.y - 11f, 200f, 22f), Rotulo(m, local), _estiloNome);
                    }
                }
                GUI.color = Color.white;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Marcador] Erro no desenho simples do marcador; desligado até reabrir o jogo: {e}");
                _reservaComErro = true;
                GUI.color = Color.white;
            }
        }
    }
}
