using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HellClock.Player;
using HellClock.Skill;
using HellClock.Status;
using HellClock.UI.HUD;
using HellClockCoop.Avatar;
using HellClockCoop.Net;
using HarmonyLib;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Painel de grupo à esquerda (0.27.0, opção A "cartão compacto" do autor). Informação, mais o símbolo de TP (0.29.0).
    /// - Uma linha por jogador, você no topo (na cor do seu marcador; pedido do autor: o grupo inteiro num lugar só, mesmo
    ///   com a sua vida já na HUD; só aparece com algum aliado ou o eco): buffs/debuffs em cima (até 4, sem
    ///   duração), nome na cor do aliado com a coroa do host, barra de vida e as skills da barra embaixo (só o ícone, sem
    ///   recarga: ver a recarga dos aliados facilitaria o grupo, e o balanceamento quer o grupo um pouco mais difícil).
    /// - Caído: linha a 55%, barra vazia com "caído" (ou o progresso da reanimação), sem skills nem status.
    ///   Fora do andar (outro andar ou hub): etiqueta "outro andar"/"hub" e o resto a 40% (dado parado, não engana).
    /// - Vida e barreira (convicção, 0.28.0, por cima da vida com o sprite do _barrierFill do jogo): do PlayerSnapshot. Skills e status: mensagem PainelAliado, enviada só quando muda e a cada 5 s
    ///   (quem chegou depois recebe sem gancho no Hello). Ícones resolvidos pelas coleções do jogo pelo HashID.
    /// - Peças: filho de GenericHUD.LifeBars (some com a HUD em cinemática e tela preta), fonte e sprites da barra do
    ///   companheiro que o AliadosNaTela já copia; cores de buff/debuff do StatusIconsView do jogo. Coroa: Streamline.
    /// - Medidas em pixels de 1080p, convertidas para a camada do jogo a cada quadro (qualquer resolução e modo do canvas).
    /// - Modo eco (F8): o fantasma entra como aliado "Eco" com as suas próprias skills e status.
    /// - TP até o aliado (0.29.0; visual 0.30.0, opção A "raio + aura" do autor): disco escuro com o raio (Flash 1, Streamline)
    ///   e um anel radial na ponta direita da linha do nome de cada aliado de pé no seu andar. Pronto: anel cheio e raio na cor
    ///   do aliado. Recarga: cinza, o anel varre (1 − restante/recarga) e os segundos ao lado. Combate: cinza. Canalizando: o
    ///   anel enche com a canalização e o raio pulsa. Na escolha mostra 1/2/3. Indisponível some. Clique no disco pede o TP
    ///   (Fase2.TpAliado).
    /// - Erro: o painel desliga até reabrir o jogo (log), sem derrubar o resto da interface.
    /// </summary>
    internal static class PainelGrupo
    {
        // Medidas em px de 1080p (opção A).
        private const float MargemX = 24f, Topo = 300f, Largura = 300f;
        private const float AlturaStatus = 22f, AlturaNome = 22f, AlturaBarra = 10f, AlturaSkill = 24f;
        private const float EspacoNomeBarra = 3f, EspacoBarraSkills = 5f, EspacoLinhas = 12f, EspacoIcones = 3f;
        private const float AlturaLinha = AlturaStatus + AlturaNome + EspacoNomeBarra + AlturaBarra + EspacoBarraSkills + AlturaSkill;
        private const float LadoCoroa = 18f;
        private const float LadoTp = 22f, LadoRaio = 16f, LarguraTextoTp = 48f; // disco com o anel; raio dentro do anel
        private const int LadoTexturaTp = 64;
        private const float EspessuraAnel = 2.5f;          // px de 1080p, no disco de LadoTp

        private const float AlphaCaido = 0.55f, AlphaFora = 0.4f;
        private const float AtrasoQueda = 0.6f;            // como a barra sobre a cabeça
        private const float IntervaloLeitura = 0.5f;       // leitura das skills e status locais
        private const float IntervaloReenvio = 5f;         // reenvio mesmo sem mudança (quem chegou depois)
        private const int TetoCacheIcones = 512;           // hashes inventados por um par (sala pública) não enchem a memória

        private static readonly Color Dourado = new Color(0.91f, 0.76f, 0.35f);          // coroa do host
        private static readonly Color CinzaEstado = new Color(0.62f, 0.62f, 0.62f, 0.9f);
        private static readonly Color FundoIcone = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color FundoDiscoTp = new Color(0.04f, 0.03f, 0.03f, 0.8f);
        private static readonly Color FundoBarraReserva = new Color(0.08f, 0.04f, 0.04f, 0.85f);
        private static readonly Color VidaReserva = new Color(0.78f, 0.12f, 0.10f);
        private static readonly Color AtrasoReserva = new Color(0.95f, 0.85f, 0.75f, 0.8f);
        private static readonly Color BuffReserva = new Color(0.45f, 0.75f, 0.40f);
        private static readonly Color DebuffReserva = new Color(0.78f, 0.22f, 0.16f);

        internal static ConfigEntry<bool> Mostrar;

        private sealed class Linha
        {
            public RectTransform Raiz;
            public CanvasGroup Grupo;
            public TextMeshProUGUI Nome, Estado;
            public Image Coroa, Tp, TpAnel, TpRaio; // Tp = disco (o clique e o hover usam ele)
            public TextMeshProUGUI TpTexto;
            public RectTransform Barra, Preenche, Atraso, Barreira, Reanima;
            public Image ReanimaCor;
            public readonly Image[] StatusMoldura = new Image[PainelAliado.MaximoStatus];
            public readonly Image[] StatusIcone = new Image[PainelAliado.MaximoStatus];
            public readonly TextMeshProUGUI[] StatusAcumulos = new TextMeshProUGUI[PainelAliado.MaximoStatus];
            public readonly Image[] Skill = new Image[PainelAliado.MaximoSkills];
            public float ValorAtraso = -1f;
            public bool Vista;
        }

        private struct Aliado
        {
            public ulong Chave;
            public string Nome;
            public float Vida, Barreira;
            public bool Voce, Caido, Fora, SemSinal, Hub, Host;
            public PainelAliado Dados;
        }

        private static readonly Dictionary<ulong, PainelAliado> Recebidos = new Dictionary<ulong, PainelAliado>();
        private static readonly Dictionary<ulong, Linha> Linhas = new Dictionary<ulong, Linha>();
        private static readonly List<Aliado> DoQuadro = new List<Aliado>();
        private static readonly List<ulong> Apagar = new List<ulong>();
        private static readonly Dictionary<int, Sprite> IconesSkill = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, KeyValuePair<Sprite, bool>> IconesStatus = new Dictionary<int, KeyValuePair<Sprite, bool>>();
        private static readonly List<StatusEffect> Efeitos = new List<StatusEffect>();

        private const ulong ChaveVoce = 1; // nenhum SteamID de jogador é 1; o eco é 0
        private static string _meuNome;
        private static PainelAliado _meu, _enviado;
        private static float _proximaLeitura, _ultimoEnvio = -100f;
        private static bool _desativado;
        private static RectTransform _raiz;
        private static Camera _camUi;
        private static readonly List<ulong> OrdemTp = new List<ulong>();
        private static Sprite _coroa, _tp, _disco, _anel, _spriteFundo, _spritePreenche, _spriteAtraso, _spriteBarreira;
        private static Color _corFundo = FundoBarraReserva, _corPreenche = VidaReserva, _corAtraso = AtrasoReserva, _corBarreira = AliadosNaTela.BarreiraReserva;
        private static Color _corBuff = BuffReserva, _corDebuff = DebuffReserva;
        private static bool _coresLidas;

        internal static void Configurar(ConfigFile config)
        {
            Mostrar = config.Bind("Interface", "MostrarPainelDeGrupo", true,
                "Mostra à esquerda o painel do grupo (você no topo e os aliados): nome, vida, skills equipadas e buffs/debuffs de cada um.");
        }

        // ---------- Rede ----------

        /// <summary>PainelAliado de um par (SessaoCoop, canal confiável). Formato inválido derruba só esta mensagem.</summary>
        internal static void Receber(Par de, byte[] dados)
        {
            if (_desativado) return; // painel desligado por erro: o Coletar não limpa mais, então não guarda
            Recebidos[de.Id.m_SteamID] = PainelAliado.Desserializar(dados);
        }

        /// <summary>Lê as skills e status do jogador local e manda aos pares quando muda (ou a cada IntervaloReenvio).</summary>
        private static void LerEEnviar(SessaoCoop sessao)
        {
            if (Time.unscaledTime < _proximaLeitura) return;
            _proximaLeitura = Time.unscaledTime + IntervaloLeitura;
            PlayerController jogador = CoopRuntime.JogadorLocal();
            if (jogador == null) return;
            _meu = Capturar(jogador);
            if (sessao == null || !sessao.EmSessao) return;
            bool mudou = !_meu.IgualA(_enviado);
            if (!mudou && Time.unscaledTime - _ultimoEnvio < IntervaloReenvio) return;
            sessao.EnviarParaConvidados(_meu.Serializar(), confiavel: true);
            _enviado = _meu;
            _ultimoEnvio = Time.unscaledTime;
        }

        private static PainelAliado Capturar(PlayerController jogador)
        {
            var m = new PainelAliado();
            // Estático no jogo: os slots da barra do jogador local, lidos do save (PlayerSaveManager). Num quadro de transição o
            // save pode faltar: fica sem skills neste ciclo, sem desligar o painel (como o RecordeNoSave da RunCompartilhada).
            List<SkillSlot> lista;
            try { lista = SkillSlotController.SkillSlots; }
            catch (Exception) { lista = null; }
            if (lista != null)
            {
                for (int i = 0; i < lista.Count && m.Skills.Count < PainelAliado.MaximoSkills; i++)
                {
                    SkillSlot s = lista[i];
                    m.Skills.Add(s != null ? s.SkillHashId : 0);
                }
                while (m.Skills.Count > 0 && m.Skills[m.Skills.Count - 1] == 0) m.Skills.RemoveAt(m.Skills.Count - 1);
            }

            Efeitos.Clear();
            var ativos = jogador.StatusController?.StatusEffects;
            if (ativos != null)
            {
                for (int i = 0; i < ativos.Count; i++)
                {
                    StatusEffect e = ativos[i];
                    StatusDefinition d = e?.StatusDefinition;
                    if (d == null || d.HideFromHud || d.StatusDisplayInfo.Icon == null) continue;
                    Efeitos.Add(e);
                }
            }
            Efeitos.Sort((a, b) => b.StartTime.CompareTo(a.StartTime)); // os mais recentes primeiro
            foreach (StatusEffect e in Efeitos)
            {
                if (m.Efeitos.Count >= PainelAliado.MaximoStatus) break;
                int hash = e.StatusDefinition.HashID;
                bool repetido = false;
                foreach (PainelAliado.Status s in m.Efeitos) if (s.Hash == hash) { repetido = true; break; }
                if (repetido) continue;
                int acumulos = e.StackOrInstanceAmount;
                m.Efeitos.Add(new PainelAliado.Status { Hash = hash, Acumulos = (byte)Mathf.Clamp(acumulos, 0, 255) });
            }
            Efeitos.Clear();
            return m;
        }

        // ---------- Quadro ----------

        /// <summary>LateUpdate do CoopRuntime, depois do AliadosNaTela (usa as peças dele).</summary>
        internal static void Atualizar(SessaoCoop sessao, RemotePlayerAvatar eco)
        {
            if (_desativado) return;
            try
            {
                LerEEnviar(sessao);
                Coletar(sessao, eco);
                OrdemTp.Clear();
                foreach (Aliado a in DoQuadro) if (!a.Voce) OrdemTp.Add(a.Chave);
                Fase2.TpAliado.OrdemDoPainel(OrdemTp); // os números da escolha do TP seguem a ordem do painel
                if (!Mostrar.Value || DoQuadro.Count == 0 || !AliadosNaTela.PecasDoPainel(out RectTransform camada, out Camera camUi))
                {
                    Esconder();
                    return;
                }
                Desenhar(camada, camUi);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Painel] Erro no painel de grupo; desligado até reabrir o jogo: {e}");
                _desativado = true;
                Destruir();
            }
        }

        private static void Coletar(SessaoCoop sessao, RemotePlayerAvatar eco)
        {
            DoQuadro.Clear();
            if (sessao != null && sessao.EmSessao)
            {
                ulong host = sessao.IdDoHost;
                foreach (Par p in sessao.Pares.Values)
                {
                    if (!p.Compativel || !p.HelloRecebido) continue;
                    bool presente = p.Avatar != null && p.Avatar.gameObject.activeInHierarchy;
                    Recebidos.TryGetValue(p.Id.m_SteamID, out PainelAliado d);
                    DoQuadro.Add(new Aliado
                    {
                        Chave = p.Id.m_SteamID, Nome = p.Nome, Vida = p.Avatar != null ? p.Avatar.Vida : 1f, Barreira = p.Avatar != null ? p.Avatar.Barreira : 0f, Caido = p.Morto,
                        Fora = !presente, SemSinal = !presente && p.MesmoAndar, Hub = p.CenaRemota == 0, Host = p.Id.m_SteamID == host, Dados = d,
                    });
                }
                DoQuadro.Sort((a, b) => a.Host != b.Host ? (a.Host ? -1 : 1) : string.CompareOrdinal(a.Nome, b.Nome));
                // Quem saiu da sessão não guarda dados velhos.
                Apagar.Clear();
                foreach (ulong k in Recebidos.Keys) if (!sessao.Pares.ContainsKey(k)) Apagar.Add(k);
                foreach (ulong k in Apagar) Recebidos.Remove(k);
            }
            else Recebidos.Clear();
            if (eco != null && eco.gameObject.activeInHierarchy)
                DoQuadro.Add(new Aliado { Chave = 0, Nome = "Eco", Vida = eco.Vida, Barreira = eco.Barreira, Dados = _meu });

            // Você no topo (pedido do autor depois da escolha da opção A: o grupo inteiro num lugar só), só com alguém no painel.
            if (DoQuadro.Count == 0) return;
            PlayerController jogador = CoopRuntime.JogadorLocal();
            if (jogador == null) return;
            bool caido = Fase2.JogadorCaido.Caido;
            DoQuadro.Insert(0, new Aliado
            {
                Chave = ChaveVoce, Voce = true, Nome = MeuNome(),
                Vida = caido || jogador.LifeController == null ? (caido ? 0f : 1f) : jogador.LifeController.CurrentRatio,
                Barreira = caido ? 0f : LocalPlayerSampler.Barreira(jogador),
                Caido = caido, Host = sessao != null && sessao.EmSessao && sessao.SouHost, Dados = _meu,
            });
        }

        private static string MeuNome()
        {
            if (_meuNome != null) return _meuNome;
            string nome = null;
            try { nome = Salas.LimparNome(SteamFriends.GetPersonaName()); }
            catch (Exception) { } // Steam ainda não pronta (só o eco): "Você" agora, o nome quando ela ficar pronta
            if (string.IsNullOrEmpty(nome)) return "Você";
            _meuNome = nome;
            return _meuNome;
        }

        private static void Desenhar(RectTransform camada, Camera camUi)
        {
            if (_raiz == null || _raiz.parent != camada) Montar(camada);
            _camUi = camUi;
            if (!_raiz.gameObject.activeSelf) _raiz.gameObject.SetActive(true);

            // Escala: 1 unidade do painel = 1 px de 1080p na tela, qualquer que seja o canvas do jogo.
            float s = Screen.height / 1080f;
            var canto = new Vector2(MargemX * s, Screen.height - Topo * s);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(camada, canto, camUi, out Vector2 local) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(camada, canto + new Vector2(100f, 0f), camUi, out Vector2 local2)) return;
            float porPixel = (local2.x - local.x) / 100f;
            if (porPixel <= 0f) return;
            float k = s * porPixel;
            _raiz.localScale = new Vector3(k, k, 1f);
            _raiz.localPosition = new Vector3(local.x, local.y, 0f);
            _raiz.SetAsLastSibling();

            foreach (Linha l in Linhas.Values) l.Vista = false;
            float y = 0f;
            foreach (Aliado a in DoQuadro)
            {
                Linha l = ObterLinha(a.Chave);
                l.Vista = true;
                l.Raiz.anchoredPosition = new Vector2(0f, -y);
                Preencher(l, a);
                y += AlturaLinha + EspacoLinhas;
            }
            Apagar.Clear();
            foreach (KeyValuePair<ulong, Linha> kv in Linhas) if (!kv.Value.Vista) Apagar.Add(kv.Key);
            foreach (ulong c in Apagar)
            {
                if (Linhas[c].Raiz != null) Object.Destroy(Linhas[c].Raiz.gameObject);
                Linhas.Remove(c);
            }
        }

        private static void Preencher(Linha l, Aliado a)
        {
            l.Grupo.alpha = a.Caido ? AlphaCaido : a.Fora ? AlphaFora : 1f;

            if (l.Nome.text != a.Nome) l.Nome.text = a.Nome;
            l.Nome.color = a.Voce ? AliadosNaTela.CorSeta : Fase3.DanosVisiveis.CorDoAliado(a.Chave); // você: a cor do seu marcador
            if (l.Coroa.gameObject.activeSelf != a.Host) l.Coroa.gameObject.SetActive(a.Host);
            l.Nome.rectTransform.anchoredPosition = new Vector2(a.Host ? LadoCoroa + 4f : 0f, l.Nome.rectTransform.anchoredPosition.y);

            // Estado à direita do nome.
            Fase2.Reanimacao.Progresso pr = default;
            bool reanimando = a.Caido && !a.Fora && (a.Voce ? Fase2.Reanimacao.MeuProgresso(out pr) : Fase2.Reanimacao.ProgressoDe(a.Chave, out pr));
            string estado = a.Fora ? (a.SemSinal ? "sem sinal" : a.Hub ? "hub" : "outro andar") : a.Caido ? (reanimando ? AliadosNaTela.TextoReanimacao(pr.Estado) : "caído") : "";
            if (l.Estado.text != estado) l.Estado.text = estado;
            l.Estado.color = reanimando ? AliadosNaTela.CorReanimacao(pr.Estado) : CinzaEstado;

            PreencherTp(l, a);

            // Vida com a faixa clara que alcança (como a barra sobre a cabeça).
            float vida = a.Caido ? 0f : Mathf.Clamp01(a.Vida);
            l.ValorAtraso = a.Caido ? 0f : vida >= l.ValorAtraso || l.ValorAtraso < 0f ? vida : Mathf.Max(vida, l.ValorAtraso - AtrasoQueda * Time.unscaledDeltaTime);
            l.Preenche.anchorMax = new Vector2(vida, 1f);
            l.Atraso.anchorMax = new Vector2(l.ValorAtraso, 1f);
            // Barreira (convicção) por cima da vida, como na barra do jogo (0.28.0).
            float barreira = a.Caido ? 0f : Mathf.Clamp01(a.Barreira);
            if (l.Barreira.gameObject.activeSelf != barreira > 0f) l.Barreira.gameObject.SetActive(barreira > 0f);
            l.Barreira.anchorMax = new Vector2(barreira, 1f);
            if (l.Reanima.gameObject.activeSelf != reanimando) l.Reanima.gameObject.SetActive(reanimando);
            if (reanimando)
            {
                l.Reanima.anchorMax = new Vector2(pr.Fracao, 1f);
                l.ReanimaCor.color = pr.Estado == Fase2.Reanimacao.Estado.Interrompido ? AliadosNaTela.VermelhoInterrompido : AliadosNaTela.VerdeReanimar;
            }

            // Caído ou fora do andar: sem status; caído também sem skills (fora, ficam a 40%).
            PainelAliado d = a.Dados;
            for (int i = 0; i < PainelAliado.MaximoStatus; i++)
            {
                Sprite sp = null;
                bool eBuff = false;
                bool tem = !a.Caido && !a.Fora && d != null && i < d.Efeitos.Count && IconeStatus(d.Efeitos[i].Hash, out sp, out eBuff);
                Image moldura = l.StatusMoldura[i];
                if (moldura.gameObject.activeSelf != tem) moldura.gameObject.SetActive(tem);
                if (!tem) continue;
                if (l.StatusIcone[i].sprite != sp) l.StatusIcone[i].sprite = sp;
                moldura.color = eBuff ? _corBuff : _corDebuff;
                int ac = d.Efeitos[i].Acumulos;
                string txt = ac > 1 ? ac.ToString() : "";
                if (l.StatusAcumulos[i].text != txt) l.StatusAcumulos[i].text = txt;
            }
            for (int i = 0; i < PainelAliado.MaximoSkills; i++)
            {
                Sprite icone = !a.Caido && d != null && i < d.Skills.Count && d.Skills[i] != 0 ? IconeSkill(d.Skills[i]) : null;
                bool tem = icone != null;
                Image img = l.Skill[i];
                GameObject slot = img.transform.parent.gameObject;
                if (slot.activeSelf != tem) slot.SetActive(tem);
                if (tem && img.sprite != icone) img.sprite = icone;
            }
        }

        /// <summary>Símbolo de TP (0.29.0; disco, anel e raio na 0.30.0): situação vinda do Fase2.TpAliado.</summary>
        private static void PreencherTp(Linha l, Aliado a)
        {
            float seg = 0f;
            Fase2.TpAliado.Situacao sit = a.Voce || a.Caido || a.Fora ? Fase2.TpAliado.Situacao.Oculto : Fase2.TpAliado.SituacaoDe(a.Chave, out seg);
            bool tem = sit != Fase2.TpAliado.Situacao.Oculto && _tp != null;
            if (l.Tp.gameObject.activeSelf != tem) l.Tp.gameObject.SetActive(tem);
            if (l.TpTexto.gameObject.activeSelf != tem) l.TpTexto.gameObject.SetActive(tem);
            if (!tem) return;
            Color cor = Fase3.DanosVisiveis.CorDoAliado(a.Chave);
            Color corAnel = cor;
            float cheio = 1f;
            string txt = "";
            Color corTexto = CinzaEstado;
            int numero = Fase2.TpAliado.NumeroNaEscolha(a.Chave, out bool realcado);
            switch (sit)
            {
                case Fase2.TpAliado.Situacao.Recarga:
                    cor = corAnel = CinzaEstado;
                    cheio = 1f - seg / Validacao.TpRecargaSegundos; // varre de vazio a cheio na recarga
                    txt = Mathf.CeilToInt(seg).ToString();
                    break;
                case Fase2.TpAliado.Situacao.Combate:
                    cor = corAnel = CinzaEstado;
                    break;
                case Fase2.TpAliado.Situacao.Canalizando:
                    cheio = 1f - seg / Validacao.TpCanalizaSegundos; // enche com a canalização
                    cor.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
                    txt = seg.ToString("0.0");
                    corTexto = Color.white;
                    break;
                case Fase2.TpAliado.Situacao.Aguardando:
                    txt = "...";
                    corTexto = Color.white;
                    break;
            }
            if (numero > 0) // escolha com a tecla: o número na frente do resto
            {
                txt = realcado ? $"> {numero}" : numero.ToString();
                corTexto = Color.white;
            }
            l.TpRaio.color = cor;
            l.TpAnel.color = corAnel;
            l.TpAnel.fillAmount = Mathf.Clamp01(cheio);
            if (l.TpTexto.text != txt) l.TpTexto.text = txt;
            l.TpTexto.color = corTexto;
        }

        /// <summary>Painel na tela neste quadro (a escolha do TP com números só abre com ele visível).</summary>
        internal static bool Visivel => !_desativado && _raiz != null && _raiz.gameObject.activeInHierarchy;

        /// <summary>Mouse sobre o símbolo de TP de um aliado (Update do TpAliado, com o desenho do quadro anterior).</summary>
        internal static bool SimboloTpSobMouse(out ulong chave)
        {
            chave = 0;
            // Cursor escondido (controle): o mouse parado sobre o símbolo não pode prender a entrada do jogo.
            if (_desativado || _raiz == null || !_raiz.gameObject.activeInHierarchy || !Cursor.visible || !Teclas.PosicaoMouse(out Vector2 mouse)) return false;
            foreach (KeyValuePair<ulong, Linha> kv in Linhas)
            {
                Image tp = kv.Value.Tp;
                if (tp == null || !tp.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(tp.rectTransform, mouse, _camUi)) continue;
                chave = kv.Key;
                return true;
            }
            return false;
        }

        // ---------- Ícones do jogo ----------

        private static Sprite IconeSkill(int hash)
        {
            if (IconesSkill.TryGetValue(hash, out Sprite s)) return s;
            try { s = global::HellClock.Constants.Collections.SKILL_DEFINITION_COLLECTION.Reference.GetElement(hash)?.Icon; }
            catch (Exception) { s = null; } // hash de outra versão do jogo: só não aparece
            if (IconesSkill.Count >= TetoCacheIcones) IconesSkill.Clear();
            IconesSkill[hash] = s;
            return s;
        }

        private static bool IconeStatus(int hash, out Sprite icone, out bool buff)
        {
            if (!IconesStatus.TryGetValue(hash, out KeyValuePair<Sprite, bool> v))
            {
                StatusDefinition d = null;
                try { d = global::HellClock.Constants.Collections.STATUS_DEFINITION_COLLECTION.Reference.GetElement(hash); }
                catch (Exception) { d = null; }
                v = new KeyValuePair<Sprite, bool>(d != null && !d.HideFromHud ? d.StatusDisplayInfo.Icon : null, d != null && d.IsBuff);
                if (IconesStatus.Count >= TetoCacheIcones) IconesStatus.Clear();
                IconesStatus[hash] = v;
            }
            icone = v.Key;
            buff = v.Value;
            return icone != null;
        }

        // ---------- Montagem ----------

        private static void Montar(RectTransform camada)
        {
            Destruir();
            LerCores();
            var go = new GameObject("HellClockCoop.PainelGrupo", typeof(RectTransform));
            _raiz = (RectTransform)go.transform;
            _raiz.SetParent(camada, false);
            _raiz.anchorMin = _raiz.anchorMax = new Vector2(0.5f, 0.5f);
            _raiz.pivot = new Vector2(0f, 1f);
            _raiz.sizeDelta = new Vector2(Largura, AlturaLinha);
            if (_coroa == null) _coroa = CriarIcone(IconesStreamline.CoroaPng, "HellClockCoop.Coroa");
            if (_tp == null) _tp = CriarIcone(IconesStreamline.TpPng, "HellClockCoop.Tp");
            if (_disco == null) _disco = CriarCirculo("HellClockCoop.TpDisco", 0f);
            if (_anel == null) _anel = CriarCirculo("HellClockCoop.TpAnel", 1f - EspessuraAnel / (LadoTp / 2f));
        }

        /// <summary>
        /// Círculo branco suavizado (tingido no código), gerado em textura: furo = 0 é o disco cheio; furo &gt; 0 é o anel, com o
        /// raio de dentro nessa fração do de fora.
        /// </summary>
        private static Sprite CriarCirculo(string nome, float furo)
        {
            int n = LadoTexturaTp;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = nome, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float r = n / 2f, dentro = r * furo;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d) * (furo > 0f ? Mathf.Clamp01(d - dentro) : 1f); // borda de 1 texel suave
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Sprites e cores da barra do companheiro e cores de buff/debuff do StatusIconsView (uma vez por jogo).</summary>
        private static void LerCores()
        {
            if (_coresLidas) return;
            _coresLidas = true;
            AliadosNaTela.ImagensDaBarra(out Image fundo, out Image preenche, out Image atraso, out Image barreira);
            if (fundo != null) { _spriteFundo = fundo.sprite; _corFundo = fundo.color; }
            if (preenche != null) { _spritePreenche = preenche.sprite; _corPreenche = preenche.color; }
            if (atraso != null) { _spriteAtraso = atraso.sprite; _corAtraso = atraso.color; }
            if (barreira != null && barreira.sprite != null) { _spriteBarreira = barreira.sprite; _corBarreira = barreira.color; }
            // 0.30.3: o StatusIconsView do jogador vem da própria HUD (GenericHUD._statusIconsView); a varredura de todos os
            // objetos carregados (50 a 90 ms em jogo) fica só de reserva.
            GenericHUD hud = HudDoJogo.Atual();
            var icones = hud != null ? AccessTools.Field(typeof(GenericHUD), "_statusIconsView")?.GetValue(hud) as StatusIconsView : null;
            if (icones == null)
            {
                double inicio = Desempenho.Agora();
                foreach (StatusIconsView v in Resources.FindObjectsOfTypeAll<StatusIconsView>())
                {
                    if (v == null) continue;
                    icones = v;
                    break;
                }
                Desempenho.SomarDetalhe(Desempenho.Detalhe.BuscarPecasHud, inicio);
            }
            if (icones != null)
            {
                Color? b = Cor(icones, "_buffIconColor"), d = Cor(icones, "_debuffIconColor");
                if (b.HasValue && b.Value.a > 0.05f) _corBuff = b.Value;
                if (d.HasValue && d.Value.a > 0.05f) _corDebuff = d.Value;
            }
            Plugin.Log.LogInfo($"[Painel] Painel de grupo montado (barra: {(preenche != null ? "do jogo" : "simples")}, barreira: {(_spriteBarreira != null ? _spriteBarreira.name : "simples")}, cores de status: {_corBuff}/{_corDebuff}).");
        }

        private static Color? Cor(object dono, string campo)
        {
            object v = AccessTools.Field(dono.GetType(), campo)?.GetValue(dono);
            return v is Color c ? c : (Color?)null;
        }

        private static Sprite CriarIcone(string png, string nome)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = nome };
            if (!ImageConversion.LoadImage(tex, Convert.FromBase64String(png))) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Linha ObterLinha(ulong chave)
        {
            if (Linhas.TryGetValue(chave, out Linha l) && l.Raiz != null) return l;
            l = new Linha();
            l.Raiz = Retangulo("Linha", _raiz, 0f, 0f, Largura, AlturaLinha);
            l.Grupo = l.Raiz.gameObject.AddComponent<CanvasGroup>();
            l.Grupo.blocksRaycasts = false;
            l.Grupo.interactable = false;

            // Status em cima.
            for (int i = 0; i < PainelAliado.MaximoStatus; i++)
            {
                RectTransform m = Retangulo("Status", l.Raiz, i * (AlturaStatus + EspacoIcones), 0f, AlturaStatus, AlturaStatus);
                l.StatusMoldura[i] = Imagem(m, null, _corBuff);
                RectTransform f = Retangulo("Fundo", m, 1.5f, -1.5f, AlturaStatus - 3f, AlturaStatus - 3f);
                Imagem(f, null, FundoIcone);
                RectTransform ic = Retangulo("Icone", m, 2f, -2f, AlturaStatus - 4f, AlturaStatus - 4f);
                l.StatusIcone[i] = Imagem(ic, null, Color.white);
                l.StatusIcone[i].preserveAspect = true;
                RectTransform t = Retangulo("Acumulos", m, 0f, -AlturaStatus + 12f, AlturaStatus, 12f);
                l.StatusAcumulos[i] = Texto(t, 12f, Color.white, TextAlignmentOptions.BottomRight);
                m.gameObject.SetActive(false);
            }

            // Nome (com a coroa do host) e estado à direita.
            float yNome = -AlturaStatus;
            RectTransform cr = Retangulo("Coroa", l.Raiz, 0f, yNome - (AlturaNome - LadoCoroa) / 2f, LadoCoroa, LadoCoroa);
            l.Coroa = Imagem(cr, _coroa, Dourado);
            l.Coroa.preserveAspect = true;
            cr.gameObject.SetActive(false);
            l.Nome = Texto(Retangulo("Nome", l.Raiz, 0f, yNome, Largura - 110f, AlturaNome), 20f, Color.white, TextAlignmentOptions.MidlineLeft);
            l.Estado = Texto(Retangulo("Estado", l.Raiz, Largura - 120f, yNome, 120f, AlturaNome), 16f, CinzaEstado, TextAlignmentOptions.MidlineRight);
            // Símbolo de TP na ponta direita da linha do nome (0.29.0); só aparece com o aliado de pé no andar (sem o estado).
            // 0.30.0: disco escuro, raio dentro e o anel radial por cima (enche a partir do topo, no sentido do relógio).
            RectTransform tp = Retangulo("Tp", l.Raiz, Largura - LadoTp, yNome - (AlturaNome - LadoTp) / 2f, LadoTp, LadoTp);
            l.Tp = Imagem(tp, _disco, FundoDiscoTp);
            RectTransform raio = Retangulo("Raio", tp, (LadoTp - LadoRaio) / 2f, -(LadoTp - LadoRaio) / 2f, LadoRaio, LadoRaio);
            l.TpRaio = Imagem(raio, _tp, Color.white);
            l.TpRaio.preserveAspect = true;
            RectTransform anel = Retangulo("Anel", tp, 0f, 0f, LadoTp, LadoTp);
            l.TpAnel = Imagem(anel, _anel, Color.white);
            l.TpAnel.type = Image.Type.Filled;
            l.TpAnel.fillMethod = Image.FillMethod.Radial360;
            l.TpAnel.fillOrigin = (int)Image.Origin360.Top;
            l.TpAnel.fillClockwise = true;
            tp.gameObject.SetActive(false);
            l.TpTexto = Texto(Retangulo("TpTexto", l.Raiz, Largura - LadoTp - 4f - LarguraTextoTp, yNome, LarguraTextoTp, AlturaNome), 16f, CinzaEstado, TextAlignmentOptions.MidlineRight);
            l.TpTexto.gameObject.SetActive(false);

            // Barra de vida: fundo, faixa clara, vida, barreira e progresso da reanimação (preenchimento pela âncora).
            float yBarra = yNome - AlturaNome - EspacoNomeBarra;
            l.Barra = Retangulo("Barra", l.Raiz, 0f, yBarra, Largura, AlturaBarra);
            Imagem(l.Barra, _spriteFundo, _spriteFundo != null ? _corFundo : FundoBarraReserva);
            l.Atraso = Preenchimento("Atraso", l.Barra, _spriteAtraso, _spriteAtraso != null ? _corAtraso : AtrasoReserva);
            l.Preenche = Preenchimento("Vida", l.Barra, _spritePreenche, _spritePreenche != null ? _corPreenche : VidaReserva);
            l.Barreira = Preenchimento("Barreira", l.Barra, _spriteBarreira, _corBarreira);
            l.Barreira.gameObject.SetActive(false);
            l.Reanima = Preenchimento("Reanimacao", l.Barra, null, AliadosNaTela.VerdeReanimar);
            l.ReanimaCor = l.Reanima.GetComponent<Image>();
            l.Reanima.gameObject.SetActive(false);

            // Skills embaixo (só o ícone).
            float ySkills = yBarra - AlturaBarra - EspacoBarraSkills;
            for (int i = 0; i < PainelAliado.MaximoSkills; i++)
            {
                RectTransform f = Retangulo("Skill", l.Raiz, i * (AlturaSkill + EspacoIcones), ySkills, AlturaSkill, AlturaSkill);
                Imagem(f, null, FundoIcone);
                RectTransform ic = Retangulo("Icone", f, 1f, -1f, AlturaSkill - 2f, AlturaSkill - 2f);
                l.Skill[i] = Imagem(ic, null, Color.white);
                l.Skill[i].preserveAspect = true;
                f.gameObject.SetActive(false);
            }

            Linhas[chave] = l;
            return l;
        }

        /// <summary>Retângulo com âncora e pivô no canto de cima à esquerda do pai, em (x, y) com y para cima (negativo desce).</summary>
        private static RectTransform Retangulo(string nome, Transform pai, float x, float y, float largura, float altura)
        {
            var go = new GameObject(nome, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(pai, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(largura, altura);
            return rt;
        }

        private static RectTransform Preenchimento(string nome, RectTransform barra, Sprite sprite, Color cor)
        {
            var go = new GameObject(nome, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(barra, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(1f, 1f);
            rt.offsetMax = new Vector2(-1f, -1f);
            Imagem(rt, sprite, cor);
            return rt;
        }

        private static Image Imagem(RectTransform rt, Sprite sprite, Color cor)
        {
            Image i = rt.gameObject.AddComponent<Image>();
            i.sprite = sprite;
            if (sprite != null && sprite.border != Vector4.zero) i.type = Image.Type.Sliced;
            i.color = cor;
            i.raycastTarget = false;
            return i;
        }

        private static TextMeshProUGUI Texto(RectTransform rt, float tamanho, Color cor, TextAlignmentOptions alinhamento)
        {
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (AliadosNaTela.Fonte != null) t.font = AliadosNaTela.Fonte;
            if (AliadosNaTela.MaterialFonte != null) t.fontSharedMaterial = AliadosNaTela.MaterialFonte;
            t.fontSize = tamanho;
            t.color = cor;
            t.alignment = alinhamento;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.richText = false; // nome da Steam com <tags> sai como texto
            return t;
        }

        private static void Esconder()
        {
            if (_raiz != null && _raiz.gameObject.activeSelf) _raiz.gameObject.SetActive(false);
        }

        private static void Destruir()
        {
            if (_raiz != null) Object.Destroy(_raiz.gameObject);
            _raiz = null;
            Linhas.Clear();
        }
    }
}
