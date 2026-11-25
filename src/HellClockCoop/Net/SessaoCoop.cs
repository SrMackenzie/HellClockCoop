using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using HellClock.Player;
using HellClockCoop.Avatar;
using Steamworks;
using UnityEngine;

namespace HellClockCoop.Net
{
    /// <summary>Um outro jogador do lobby.</summary>
    internal sealed class Par
    {
        public CSteamID Id;
        public string Nome;
        public float ProximoHello;      // reenvio periódico até o aperto de mão fechar
        public bool HelloRecebido;      // já ouvimos o par
        public bool MeOuviu;            // o par confirmou que nos ouviu
        public bool Compativel;
        public string Incompatibilidade;
        public float UltimoPacote = -1f;
        public bool MesmoAndar;
        public bool Morto;
        public int SequenciaMorto = -1; // sequência do último snapshot que decidiu Morto (0.16.0; -1 = nenhum)
        public float UltimoCombate = -100f; // 0.29.0, no host: último golpe dado (Golpes) ou vida perdida (snapshot), para o TP
        public float VidaVista = -1f;       // 0.29.0: vida do último snapshot (-1 = nenhum)
        public bool Deluxe, CursedWar;  // 0.21.0: DLC diferente divide a run do jogo base; o Ato 4 só com a Cursed War (ForaDaRun)
        public bool SemLiberar;         // 0.19.0: o save do convidado não chegou à run (ForaDaRun); fora da escala
        public float ProximoAvisoFora;  // 0.19.0, no host: limite do aviso de ForaDaRun (1 a cada 30 s)
        public int CenaRemota = -2;   // -2 = ainda não visto (int.MinValue foi removido do mscorlib do jogo)
        public Aparencia Aparencia;
        public RemotePlayerAvatar Avatar;
    }

    /// <summary>
    /// Sessão coop sobre um lobby da Steam (até 4). Fase 1b: todos mandam snapshots para todos
    /// (malha completa, no máximo 3 pares); o dono do lobby é o host para as fases seguintes.
    /// 0.18.0: a sala é pública (aparece na aba Salas para o mundo todo) ou privada (só amigos), pelo .cfg
    /// [Coop] SalaPublica; o host grava nos dados do lobby o lugar da run e o nível dele, e pode expulsar alguém.
    /// Os callbacks da Steam são processados pelo SteamManager do próprio jogo (SteamAPI.RunCallbacks).
    /// </summary>
    internal sealed class SessaoCoop
    {
        public const int MaximoJogadores = 4;
        private const float SemSinalApos = 5f;

        public enum Estado { AguardandoSteam, Livre, CriandoLobby, EntrandoLobby, NoLobby }

        public Estado EstadoAtual { get; private set; } = Estado.AguardandoSteam;
        public CSteamID Lobby { get; private set; } = CSteamID.Nil;
        public CSteamID Eu { get; private set; }
        public readonly Dictionary<ulong, Par> Pares = new Dictionary<ulong, Par>();
        public event Action<string> Aviso;

        /// <summary>Sala nova aparece na aba Salas de todo mundo (padrão) ou só para amigos.</summary>
        internal static ConfigEntry<bool> SalaPublica;

        /// <summary>Resultado da última busca da aba Salas (só salas compatíveis e com vaga).</summary>
        public readonly List<SalaListada> SalasListadas = new List<SalaListada>();
        public bool BuscandoSalas { get; private set; }
        public float UltimaBusca { get; private set; } = -1f;
        public bool FalhaNaBusca { get; private set; }

        internal static void Configurar(ConfigFile config)
        {
            SalaPublica = config.Bind("Coop", "SalaPublica", true,
                "Ao criar o coop: true = sala pública (aparece na aba Salas para jogadores do mundo todo com a mesma versão do mod); false = só amigos da Steam, por convite.");
        }

        public bool EmSessao => EstadoAtual == Estado.NoLobby;
        public bool SouHost => EmSessao && _donoDoLobby == Eu.m_SteamID;

        /// <summary>
        /// 0.30.2: dono do lobby guardado (0 fora do coop). SteamMatchmaking.GetLobbyOwner é uma chamada ao cliente da Steam, e
        /// SouHost, IdDoHost, Host, ModoHost e ModoConvidado são lidos dezenas de vezes por quadro (14 a 21 chamadas por quadro como
        /// host de sala vazia na 0.30.1). Relido da Steam no início de cada Atualizar, antes de qualquer mensagem ser tratada,
        /// e nos callbacks do lobby (entrada e mudança de membros): nenhuma decisão de autoridade usa um dono mais velho que
        /// um quadro, e as mensagens recebidas são conferidas contra o dono lido naquele mesmo quadro, como antes.
        /// </summary>
        private ulong _donoDoLobby;

        private void LerDonoDoLobby() => _donoDoLobby = SteamMatchmaking.GetLobbyOwner(Lobby).m_SteamID;

        /// <summary>DLCs deste PC (lidas uma vez, quando o Steam fica pronto).</summary>
        public bool MinhaDeluxe { get; private set; }
        public bool MinhaCursedWar { get; private set; }

        /// <summary>
        /// 0.21.1: host da run aberta neste PC (0 = nenhuma). Fixado enquanto o convidado está numa run com host e solto quando
        /// a run acaba (FimRun ou volta ao hub): se o host sai no meio e o lobby passa a outro membro (inclusive um expulso que
        /// ignorou o Expulso), o novo dono não comanda a run aberta (planos, inimigos, fim da run, expulsão).
        /// </summary>
        private ulong _hostDaRun;

        /// <summary>
        /// SteamID de quem manda neste PC (Validacao.HostEfetivo): sem run, o dono do lobby; com run, o host dela só enquanto
        /// ainda é o dono. Se o lobby trocou de dono no meio da run, ninguém (0) até a run acabar: nem o novo dono nem o host
        /// que saiu e voltou como membro comum.
        /// </summary>
        public ulong IdDoHost => Validacao.HostEfetivo(_hostDaRun, _donoDoLobby);

        /// <summary>O host (o da run aberta ou o dono do lobby), se já trocou Hello conosco.</summary>
        public Par Host => EmSessao && !SouHost && Pares.TryGetValue(IdDoHost, out Par h) ? h : null;

        /// <summary>Convidado numa run com host: fixa quem manda até a run acabar (só a 1ª vez).</summary>
        public void FixarHostDaRun()
        {
            if (_hostDaRun != 0) return;
            Par h = Host;
            if (h == null) return;
            _hostDaRun = h.Id.m_SteamID;
            Plugin.Log.LogInfo($"[Sessão] Run aberta com {h.Nome}: até ela acabar, só ele comanda a sua run.");
        }

        /// <summary>A run acabou (FimRun, hub ou saída da sala): volta a valer o dono do lobby.</summary>
        public void SoltarHostDaRun() => _hostDaRun = 0;

        /// <summary>Fase 2: este PC segue a run do host (andar, timer, início e fim).</summary>
        public bool ModoConvidado
        {
            get { Par h = Host; return h != null && h.Compativel; }
        }

        /// <summary>Fase 2: este PC é o host e há ao menos um convidado acompanhando a run.</summary>
        public bool ModoHost
        {
            get
            {
                if (!SouHost) return false;
                foreach (Par p in Pares.Values) if (p.Compativel) return true;
                return false;
            }
        }

        /// <summary>Há alguém no coop (afeta a pausa).</summary>
        public bool ComAliados => EmSessao && Pares.Count > 0;

        // Referências mantidas para o GC não recolher os callbacks.
        private Callback<GameLobbyJoinRequested_t> _cbConvite;
        private Callback<GameRichPresenceJoinRequested_t> _cbEntrarPelaLista;
        private Callback<LobbyEnter_t> _cbEntrou;
        private Callback<LobbyChatUpdate_t> _cbMembros;
        private Callback<SteamNetworkingMessagesSessionRequest_t> _cbPedidoSessao;
        private Callback<SteamNetworkingMessagesSessionFailed_t> _cbFalhaSessao;
        private Callback<GamepadTextInputDismissed_t> _cbTecladoDaSteam; // 0.25.0: nome da sala pelo controle
        private CallResult<LobbyCreated_t> _crLobbyCriado;
        private CallResult<LobbyMatchList_t> _crListaSalas;

        // Expulsão (0.18.0): quem o host tirou desta sala (não volta a ela enquanto ela existir; zera ao sair) e as salas
        // de onde fomos tirados (escondidas até fechar o jogo). 0.21.1: o host avisa os outros convidados (ExpulsoAviso), que
        // também põem o expulso aqui; um cliente alterado que ignore o Expulso deixa de ser par de todos.
        private readonly HashSet<ulong> _expulsos = new HashSet<ulong>();
        private readonly HashSet<ulong> _salasQueMeExpulsaram = new HashSet<ulong>();
        private float _proximoReenvioExpulso;
        private bool _avisouDonoExpulso; // 0.21.1: o lobby passou a quem o host anterior expulsou (um aviso por sala)

        // Dados da sala no lobby: o dono grava a cada 2 s, só o que mudou.
        private readonly Dictionary<string, string> _dadosGravados = new Dictionary<string, string>();
        private float _proximosDadosSala;

        private CSteamID _lobbyPendenteLinhaDeComando = CSteamID.Nil;
        private Aparencia _ultimaAparenciaEnviada;

        // ---------- Inicialização ----------

        /// <summary>Chamado a cada frame até o Steam do jogo estar pronto.</summary>
        public void TentarInicializar()
        {
            if (EstadoAtual != Estado.AguardandoSteam || !SteamEstado.Pronto()) return;

            Eu = SteamUser.GetSteamID();
            _cbConvite = Callback<GameLobbyJoinRequested_t>.Create(OnConviteAceito);
            _cbEntrarPelaLista = Callback<GameRichPresenceJoinRequested_t>.Create(OnEntrarPelaLista);
            _cbEntrou = Callback<LobbyEnter_t>.Create(OnEntrouNoLobby);
            _cbMembros = Callback<LobbyChatUpdate_t>.Create(OnMembrosMudaram);
            _cbPedidoSessao = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnPedidoDeSessao);
            _cbFalhaSessao = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnFalhaDeSessao);
            _cbTecladoDaSteam = Callback<GamepadTextInputDismissed_t>.Create(OnTecladoDaSteamFechou);
            _crLobbyCriado = CallResult<LobbyCreated_t>.Create(OnLobbyCriado);
            _crListaSalas = CallResult<LobbyMatchList_t>.Create(OnListaDeSalas);
            SteamNetworkingUtils.InitRelayNetworkAccess(); // adianta a conexão com os relays da Steam
            SteamTransporte.Preparar(); // 0.20.0: global sem ICE desde já; só a 1ª sessão com um amigo abre com o padrão do usuário

            try
            {
                MinhaDeluxe = global::HellClock.Constants.DlcConfig.HasDeluxeEdition();
                MinhaCursedWar = global::HellClock.Constants.DlcConfig.HasCursedWar();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Sessão] Não foi possível ler as DLCs: {e.Message}");
            }

            EstadoAtual = Estado.Livre;
            Plugin.Log.LogInfo($"[Sessão] DLCs: Deluxe={(MinhaDeluxe ? "sim" : "não")}, Cursed War={(MinhaCursedWar ? "sim" : "não")}.");
            Plugin.Log.LogInfo($"[Sessão] Steam pronto ({SteamFriends.GetPersonaName()}). O menu do coop (F7 ou View no controle) cria, convida e sai.");

            // Jogo aberto por um convite com o jogo fechado: a Steam passa "+connect_lobby <id>".
            _lobbyPendenteLinhaDeComando = LerLobby(string.Join(" ", Environment.GetCommandLineArgs()));
        }

        /// <summary>Extrai o id de "+connect_lobby &lt;id&gt;" de um texto (linha de comando ou rich presence).</summary>
        private static CSteamID LerLobby(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return CSteamID.Nil;
            string[] partes = texto.Split(' ');
            for (int i = 0; i < partes.Length - 1; i++)
            {
                if (partes[i] == "+connect_lobby" && ulong.TryParse(partes[i + 1], out ulong id)) return new CSteamID(id);
            }
            return CSteamID.Nil;
        }

        // ---------- Ações do jogador ----------

        public void HospedarOuConvidar()
        {
            switch (EstadoAtual)
            {
                case Estado.AguardandoSteam:
                    Avisar("O Steam ainda não está pronto. Espere chegar ao menu.");
                    return;
                case Estado.CriandoLobby:
                case Estado.EntrandoLobby:
                    return;
                case Estado.NoLobby:
                    SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
                    return;
            }
            EstadoAtual = Estado.CriandoLobby;
            SteamAPICall_t chamada = SteamMatchmaking.CreateLobby(TipoDaSala(), MaximoJogadores);
            _crLobbyCriado.Set(chamada);
            Avisar("Criando o coop...");
        }

        private static ELobbyType TipoDaSala() =>
            SalaPublica == null || SalaPublica.Value ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly;

        /// <summary>Interruptor "Sala pública" do menu: grava no .cfg e, sendo o host, muda o tipo do lobby na hora.</summary>
        public void AlternarSalaPublica()
        {
            if (SalaPublica == null) return;
            SalaPublica.Value = !SalaPublica.Value; // o BepInEx grava o .cfg
            Plugin.Log.LogInfo($"[Sessão] Sala {(SalaPublica.Value ? "pública" : "privada (só amigos)")}.");
            if (SouHost && !SteamMatchmaking.SetLobbyType(Lobby, TipoDaSala()))
                Plugin.Log.LogWarning("[Sessão] A Steam recusou mudar o tipo da sala.");
        }

        // ---------- Nome da sala (0.25.0) ----------

        private string _nomeSala; // dado pelo host a esta sala; null = "Sala de <nome na Steam>". Não fica guardado (pedido do autor).

        /// <summary>Nome da sala atual, limpo: o que este PC deu (host) ou o que o lobby anuncia (convidado). Null fora do coop.</summary>
        public string NomeDaSalaAtual()
        {
            if (!EmSessao) return null;
            if (SouHost) return _nomeSala ?? Salas.NomePadrao(SteamFriends.GetPersonaName());
            return Salas.NomeDaSalaListada(SteamMatchmaking.GetLobbyData(Lobby, Protocolo.ChaveLobbySala), HostDaSalaAtual());
        }

        /// <summary>Nome na Steam do dono do lobby (o host agora), limpo. Null fora do coop.</summary>
        public string HostDaSalaAtual() =>
            EmSessao ? Salas.LimparNome(SteamFriends.GetFriendPersonaName(new CSteamID(_donoDoLobby))) : null;

        public int JogadoresNaSala => EmSessao ? SteamMatchmaking.GetNumLobbyMembers(Lobby) : 0;

        /// <summary>Host renomeia a sala atual (limpo, até 32; vazio volta ao padrão). Vale até a sala fechar.</summary>
        public void RenomearSala(string digitado)
        {
            if (!SouHost) return;
            _nomeSala = Salas.NomeDigitado(digitado);
            string nome = NomeDaSalaAtual();
            GravarDado(Protocolo.ChaveLobbySala, nome);
            Plugin.Log.LogInfo($"[Sessão] Sala renomeada: {nome}{(_nomeSala == null ? " (padrão)" : "")}.");
            Avisar($"Sala renomeada: {nome}.");
        }

        private bool _tecladoDaSteamAberto;

        /// <summary>
        /// Controle: abre o teclado de tela da Steam com o nome atual. Falso quando a Steam não mostra (fora do Big Picture e
        /// do Steam Deck): aí o menu edita na linha, pelo teclado.
        /// </summary>
        public bool PedirNomeNoTecladoDaSteam()
        {
            if (!SouHost) return false;
            try
            {
                _tecladoDaSteamAberto = SteamUtils.ShowGamepadTextInput(EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                    EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine, "Nome da sala", Salas.NomeMaximo, NomeDaSalaAtual() ?? "");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Sessão] Teclado de tela da Steam indisponível: {e.Message}");
                _tecladoDaSteamAberto = false;
            }
            return _tecladoDaSteamAberto;
        }

        private void OnTecladoDaSteamFechou(GamepadTextInputDismissed_t r)
        {
            if (!_tecladoDaSteamAberto) return; // aberto por outro (o jogo não usa, mas não é nosso)
            _tecladoDaSteamAberto = false;
            if (!r.m_bSubmitted) return; // cancelado
            uint tamanho = Math.Min(SteamUtils.GetEnteredGamepadTextLength(), 1024u) + 1u;
            if (SteamUtils.GetEnteredGamepadTextInput(out string texto, tamanho)) RenomearSala(texto);
        }

        /// <summary>Busca as salas públicas compatíveis no mundo inteiro (sem o filtro Worldwide a Steam só olha a região).</summary>
        public void BuscarSalas()
        {
            if (EstadoAtual == Estado.AguardandoSteam || BuscandoSalas || !Salas.PodeBuscar(Time.unscaledTime, UltimaBusca)) return;
            BuscandoSalas = true;
            FalhaNaBusca = false;
            UltimaBusca = Time.unscaledTime;
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListStringFilter(Protocolo.ChaveLobbyProtocolo, Protocolo.Versao.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(Protocolo.ChaveLobbyMod, Plugin.PluginVersion, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            _crListaSalas.Set(SteamMatchmaking.RequestLobbyList());
            Plugin.Log.LogInfo("[Sessão] Buscando salas públicas (mundo inteiro).");
        }

        /// <summary>
        /// Trava da sala com o save carregado agora (0.19.0: recorde; pode ter trocado de save desde a busca) e a DLC deste
        /// PC (0.21.0: o Ato 4, pelo ato do lobby ou pela masmorra, precisa da Cursed War).
        /// </summary>
        internal void AtualizarTrava(SalaListada sala)
        {
            sala.Recorde = sala.Dungeon != 0 ? Fase2.RunCompartilhada.RecordeNoSave(sala.Dungeon) : -1;
            bool atoDaDlc = sala.Lugar == Salas.LugarRun && (sala.Ato == Salas.AtoDaDlc || Fase2.RunCompartilhada.MasmorraDaDlc(sala.Dungeon));
            sala.SemDlc = Salas.PrecisaDaDlc(atoDaDlc, MinhaCursedWar);
        }

        /// <summary>Entra numa sala da lista (sai da sala atual, se houver).</summary>
        public void EntrarNaSala(SalaListada sala)
        {
            if (sala == null || EstadoAtual == Estado.AguardandoSteam || EstadoAtual == Estado.CriandoLobby || EstadoAtual == Estado.EntrandoLobby) return;
            if (_salasQueMeExpulsaram.Contains(sala.Lobby))
            {
                Avisar("Você foi expulso dessa sala.");
                return;
            }
            AtualizarTrava(sala);
            if (sala.SemDlc) // 0.21.0: o menu já mostra indisponível; aqui confere de novo
            {
                Avisar("Essa sala está no Ato 4, que precisa da DLC Cursed War.");
                return;
            }
            if (Salas.SalaTravada(sala)) // 0.19.0: o menu já mostra indisponível; aqui confere de novo
            {
                Avisar($"O seu save ainda não chegou aonde essa sala está ({Salas.TextoRecorde(sala.Recorde)}).");
                return;
            }
            Plugin.Log.LogInfo($"[Sessão] Entrando na sala de {sala.Host} pela lista.");
            Entrar(new CSteamID(sala.Lobby));
        }

        /// <summary>Host: tira o jogador da sala. Ele recebe Expulso e sai; até sair, fica sem conversa e sem contar na escala.</summary>
        public void Expulsar(Par p)
        {
            if (!SouHost || p == null || !Pares.ContainsKey(p.Id.m_SteamID)) return;
            _expulsos.Add(p.Id.m_SteamID);
            SteamTransporte.Enviar(p.Id, new[] { Protocolo.TipoExpulso }, confiavel: true, Protocolo.CanalConfiavel);
            _proximoReenvioExpulso = Time.unscaledTime + 2f;
            DestruirAvatar(p);
            Pares.Remove(p.Id.m_SteamID); // a sessão P2P fica aberta até ele sair do lobby, para o Expulso chegar
            byte[] aviso = new ExpulsoAviso { SteamId = p.Id.m_SteamID }.Serializar(); // 0.21.1: os outros cortam a conversa com ele
            foreach (Par outro in Pares.Values) if (outro.Compativel) SteamTransporte.Enviar(outro.Id, aviso, confiavel: true, Protocolo.CanalConfiavel);
            Plugin.Log.LogInfo($"[Sessão] {p.Nome} foi expulso da sala.");
            Avisar($"{p.Nome} foi expulso da sala.");
        }

        public void Sair(string motivo)
        {
            if (Lobby != CSteamID.Nil)
            {
                foreach (Par p in Pares.Values)
                {
                    SteamTransporte.Enviar(p.Id, new[] { Protocolo.TipoTchau }, confiavel: true, Protocolo.CanalConfiavel);
                }
                SteamMatchmaking.LeaveLobby(Lobby);
                SteamFriends.SetRichPresence("connect", ""); // "" apaga a chave
            }
            foreach (Par p in Pares.Values)
            {
                DestruirAvatar(p);
                SteamTransporte.Fechar(p.Id);
            }
            SteamTransporte.Esquecer(); // 0.20.0: recusados fora do relay voltam a ser conferidos no próximo coop
            Pares.Clear();
            EncerrarSimulacao();
            Fase3.InimigosCompartilhados.Encerrar();
            Fase3.ObjetosCompartilhados.Encerrar();
            Fase3.SantuariosCompartilhados.Encerrar();
            Fase3.PortasCompartilhadas.Encerrar();
            Fase3.FxCompartilhados.Encerrar();
            Fase3.SonsAliados.Encerrar();
            Fase3.GolpesVisiveis.Encerrar();
            Fase3.DanosVisiveis.Encerrar();
            Fase3.AlvosAliados.Encerrar();
            Lobby = CSteamID.Nil;
            _donoDoLobby = 0;
            _ultimaAparenciaEnviada = null;
            _expulsos.Clear();
            _hostDaRun = 0;
            _avisouDonoExpulso = false;
            _dadosGravados.Clear();
            _nomeSala = null; // o nome dado vale só para a sala que fechou
            _tecladoDaSteamAberto = false; // um fechamento atrasado do teclado não renomeia a próxima sala
            if (EstadoAtual != Estado.AguardandoSteam) EstadoAtual = Estado.Livre;
            Plugin.Log.LogInfo($"[Sessão] Saiu do coop ({motivo}).");
            Avisar($"Você saiu do coop ({motivo}).");
        }

        private void Entrar(CSteamID lobby)
        {
            if (EstadoAtual == Estado.NoLobby && lobby == Lobby) return;
            if (EstadoAtual == Estado.NoLobby) Sair("entrando em outro coop");
            EstadoAtual = Estado.EntrandoLobby;
            SteamMatchmaking.JoinLobby(lobby);
            Avisar("Entrando no coop...");
        }

        // ---------- Callbacks da Steam ----------

        private void OnLobbyCriado(LobbyCreated_t r, bool falhaIO)
        {
            if (falhaIO || r.m_eResult != EResult.k_EResultOK)
            {
                EstadoAtual = Estado.Livre;
                Avisar($"Não foi possível criar o coop ({(falhaIO ? "falha de comunicação" : r.m_eResult.ToString())}).");
                return;
            }
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(lobby, Protocolo.ChaveLobbyMod, Plugin.PluginVersion);
            SteamMatchmaking.SetLobbyData(lobby, Protocolo.ChaveLobbyProtocolo, Protocolo.Versao.ToString());
            SteamMatchmaking.SetLobbyData(lobby, Protocolo.ChaveLobbyJogo, Application.version);
            SteamMatchmaking.SetLobbyData(lobby, Protocolo.ChaveLobbyNome, SteamFriends.GetPersonaName());
            SteamMatchmaking.SetLobbyData(lobby, Protocolo.ChaveLobbyDono, Eu.m_SteamID.ToString()); // 0.23.0: bloquear pela aba Salas
            SteamMatchmaking.SetLobbyData(lobby, Protocolo.ChaveLobbyDlc, MinhaCursedWar ? "1" : "0");
            _nomeSala = null; // sala nova começa com o padrão
            SteamMatchmaking.SetLobbyData(lobby, Protocolo.ChaveLobbySala, Salas.NomePadrao(SteamFriends.GetPersonaName())); // 0.25.0
            Plugin.Log.LogInfo($"[Sessão] Coop criado (lobby {r.m_ulSteamIDLobby}, sala {(TipoDaSala() == ELobbyType.k_ELobbyTypePublic ? "pública" : "privada")}).");
            // O LobbyEnter_t também chega para quem cria; lá o estado vira NoLobby.
            SteamFriends.ActivateGameOverlayInviteDialog(lobby);
        }

        private void OnListaDeSalas(LobbyMatchList_t r, bool falhaIO)
        {
            BuscandoSalas = false;
            SalasListadas.Clear();
            if (falhaIO)
            {
                FalhaNaBusca = true;
                Plugin.Log.LogWarning("[Sessão] A busca de salas falhou (comunicação com a Steam).");
                return;
            }
            FalhaNaBusca = false; // resposta depois do tempo limite: a lista vale
            string proto = Protocolo.Versao.ToString();
            int ocultas = 0;
            for (int i = 0; i < r.m_nLobbiesMatching; i++)
            {
                CSteamID l = SteamMatchmaking.GetLobbyByIndex(i);
                int jogadores = SteamMatchmaking.GetNumLobbyMembers(l);
                int vagas = Math.Min(SteamMatchmaking.GetLobbyMemberLimit(l), MaximoJogadores);
                if (!Salas.SalaVisivel(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyMod), SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyProtocolo),
                        Plugin.PluginVersion, proto, jogadores, vagas, l == Lobby, _salasQueMeExpulsaram.Contains(l.m_SteamID))) continue;
                ulong dono = Salas.LerSteamId(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyDono));
                if (dono != 0 && EstaBloqueado(dono)) // 0.23.0: a sala de quem você bloqueou some da lista
                {
                    ocultas++;
                    continue;
                }
                SalasListadas.Add(new SalaListada
                {
                    Lobby = l.m_SteamID,
                    Dono = dono,
                    CursedWar = SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyDlc) == "1", // 0.21.0: só marca; a trava é o Ato 4
                    Host = Salas.LimparNome(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyNome)),
                    Nome = Salas.NomeDaSalaListada(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbySala),
                        Salas.LimparNome(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyNome))), // 0.25.0
                    Lugar = Salas.LerLugar(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyLugar)),
                    Ato = Salas.LerInteiro(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyAto), Salas.AtoMaximo),
                    Andar = Salas.LerInteiro(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyAndar), Salas.AndarMaximo),
                    Nivel = Salas.LerInteiro(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyNivel), Salas.NivelMaximo),
                    Dungeon = Salas.LerHash(SteamMatchmaking.GetLobbyData(l, Protocolo.ChaveLobbyDungeon)),
                    Jogadores = jogadores, Vagas = vagas,
                });
            }
            Plugin.Log.LogInfo($"[Sessão] Salas: {r.m_nLobbiesMatching} da Steam, {SalasListadas.Count} na lista" +
                (ocultas > 0 ? $", {ocultas} de bloqueado(s) escondida(s)." : "."));
        }

        // ---------- Bloqueio (0.23.0) ----------

        /// <summary>Na lista deste PC (bloqueados.txt) ou bloqueado na própria Steam (Ignorado).</summary>
        public static bool EstaBloqueado(ulong id)
        {
            if (Bloqueados.Contem(id)) return true;
            try
            {
                EFriendRelationship r = SteamFriends.GetFriendRelationship(new CSteamID(id));
                return r == EFriendRelationship.k_EFriendRelationshipIgnored || r == EFriendRelationship.k_EFriendRelationshipIgnoredFriend;
            }
            catch (InvalidOperationException)
            {
                return false; // Steam ainda não iniciada
            }
        }

        /// <summary>
        /// Bloqueia o jogador: grava na lista, esconde as salas dele e, sendo host, o expulsa agora. Convidado não expulsa.
        /// </summary>
        public void Bloquear(ulong id, string nome)
        {
            if (id == Eu.m_SteamID) return;
            // O nome da sala é declarado pelo host: se a Steam conhece o id, vale o nome dela (a aba Ajustes mostra quem foi
            // bloqueado de fato, mesmo que o lobby tenha forjado hcc_dono e hcc_host).
            string daSteam = SteamFriends.GetFriendPersonaName(new CSteamID(id));
            if (!string.IsNullOrEmpty(daSteam) && daSteam != "[unknown]") nome = daSteam;
            nome = Salas.LimparNome(nome);
            if (!Bloqueados.Bloquear(id, nome))
            {
                Avisar(Bloqueados.IdValido(id) ? $"A lista de bloqueados está cheia ({Bloqueados.Maximo})." : "Não deu para bloquear esse jogador.");
                return;
            }
            SalasListadas.RemoveAll(s => s.Dono == id);
            Plugin.Log.LogInfo($"[Bloqueio] {nome} foi bloqueado.");
            if (SouHost && Pares.TryGetValue(id, out Par p)) Expulsar(p); // o Expulsar já avisa "foi expulso da sala"
            Avisar($"{nome} foi bloqueado. As salas dele somem da lista.");
        }

        public void Desbloquear(ulong id, string nome)
        {
            if (!Bloqueados.Desbloquear(id)) return;
            Plugin.Log.LogInfo($"[Bloqueio] {Salas.LimparNome(nome)} foi desbloqueado.");
            Avisar($"{Salas.LimparNome(nome)} foi desbloqueado.");
        }

        private void OnConviteAceito(GameLobbyJoinRequested_t c)
        {
            Plugin.Log.LogInfo($"[Sessão] Convite aceito de {Salas.LimparNome(SteamFriends.GetFriendPersonaName(c.m_steamIDFriend))}.");
            Entrar(c.m_steamIDLobby);
        }

        /// <summary>"Entrar no jogo" pela lista de amigos, com o jogo já aberto (usa a chave "connect" do rich presence).</summary>
        private void OnEntrarPelaLista(GameRichPresenceJoinRequested_t r)
        {
            CSteamID lobby = LerLobby(r.m_rgchConnect);
            if (lobby != CSteamID.Nil) Entrar(lobby);
        }

        private void OnEntrouNoLobby(LobbyEnter_t e)
        {
            var lobby = new CSteamID(e.m_ulSteamIDLobby);
            if (e.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                EstadoAtual = Estado.Livre;
                Avisar($"Não foi possível entrar no coop ({(EChatRoomEnterResponse)e.m_EChatRoomEnterResponse}).");
                return;
            }

            string modHost = SteamMatchmaking.GetLobbyData(lobby, Protocolo.ChaveLobbyMod);
            string protoHost = SteamMatchmaking.GetLobbyData(lobby, Protocolo.ChaveLobbyProtocolo);
            string jogoHost = SteamMatchmaking.GetLobbyData(lobby, Protocolo.ChaveLobbyJogo);
            bool souDono = SteamMatchmaking.GetLobbyOwner(lobby) == Eu; // quem criou não se checa contra si mesmo
            if (!souDono && (protoHost != Protocolo.Versao.ToString() || modHost != Plugin.PluginVersion))
            {
                SteamMatchmaking.LeaveLobby(lobby);
                EstadoAtual = Estado.Livre;
                Avisar($"Versão do mod diferente: o host usa {Vazio(modHost)}, você usa {Plugin.PluginVersion}. Atualizem para a mesma versão.");
                return;
            }
            if (!souDono && jogoHost != Application.version)
            {
                Avisar($"Atenção: versões do jogo diferentes (host {Vazio(jogoHost)}, você {Application.version}).");
            }

            Lobby = lobby;
            LerDonoDoLobby(); // antes da primeira leitura de SouHost nesta sala
            EstadoAtual = Estado.NoLobby;
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobby.m_SteamID);
            string dono = HostDaSalaAtual(); // sala pública: pode ser desconhecido
            Plugin.Log.LogInfo($"[Sessão] No coop (lobby {lobby.m_SteamID}, host {dono}, {(SouHost ? "você é o host" : "você é convidado")}).");
            Avisar(SouHost ? "Coop criado! Convide amigos pelo overlay (o menu do coop abre de novo)." : $"Você entrou no coop de {dono}.");
            AtualizarMembros();
        }

        private void OnMembrosMudaram(LobbyChatUpdate_t u)
        {
            if (u.m_ulSteamIDLobby != Lobby.m_SteamID) return;
            LerDonoDoLobby(); // quem saiu pode ser o dono: o AtualizarMembros (expulsar o bloqueado) já vê o novo
            AtualizarMembros();
        }

        private void OnPedidoDeSessao(SteamNetworkingMessagesSessionRequest_t pedido)
        {
            CSteamID quem = pedido.m_identityRemote.GetSteamID();
            // 0.21.1: convidado não reabre conversa com quem o host expulsou (o host aceita: é por ela que o Expulso chega).
            if (EmSessao && EhMembro(quem) && (SouHost || !_expulsos.Contains(quem.m_SteamID)))
            {
                SteamTransporte.Aceitar(pedido.m_identityRemote);
            }
            else
            {
                Plugin.Log.LogInfo("[Rede] Pedido de conexão ignorado: o jogador não está no seu coop (ou foi expulso)."); // 0.21.1: sem o SteamID
            }
        }

        private void OnFalhaDeSessao(SteamNetworkingMessagesSessionFailed_t f)
        {
            Plugin.Log.LogWarning($"[Rede] Falha na conexão P2P: {f.m_info.m_eEndReason} {f.m_info.m_szEndDebug}");
        }

        // ---------- Membros ----------

        private bool EhMembro(CSteamID id)
        {
            int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < n; i++)
            {
                if (SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i) == id) return true;
            }
            return false;
        }

        private void AtualizarMembros()
        {
            var presentes = new HashSet<ulong>();
            int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < n; i++)
            {
                CSteamID id = SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i);
                if (id == Eu) continue;
                if (_expulsos.Contains(id.m_SteamID)) continue; // ainda no lobby, mas fora do coop até sair
                presentes.Add(id.m_SteamID);
                if (!Pares.ContainsKey(id.m_SteamID))
                {
                    var par = new Par { Id = id, Nome = Salas.LimparNome(SteamFriends.GetFriendPersonaName(id)) };
                    Pares[id.m_SteamID] = par;
                    if (EstaBloqueado(id.m_SteamID))
                    {
                        // 0.23.0: o host tira quem ele bloqueou (sem Hello); o convidado não expulsa, só fica sabendo.
                        if (SouHost)
                        {
                            Plugin.Log.LogInfo($"[Bloqueio] {par.Nome} está bloqueado e foi expulso ao entrar.");
                            Expulsar(par);
                            continue;
                        }
                        Avisar($"{par.Nome}, que você bloqueou, está neste coop.");
                    }
                    Plugin.Log.LogInfo($"[Sessão] {par.Nome} está no coop.");
                    Avisar($"{par.Nome} entrou no coop.");
                    EnviarHello(par);
                    _ultimaAparenciaEnviada = null; // garante que o recém-chegado receba a aparência
                }
            }
            var saiu = new List<ulong>();
            foreach (KeyValuePair<ulong, Par> kv in Pares)
            {
                if (!presentes.Contains(kv.Key)) saiu.Add(kv.Key);
            }
            foreach (ulong id in saiu)
            {
                RemoverPar(Pares[id], "saiu do coop");
            }
            if (_expulsos.Count > 0)
            {
                foreach (ulong id in _expulsos)
                {
                    if (!EhMembro(new CSteamID(id))) SteamTransporte.Fechar(new CSteamID(id)); // saiu: fecha a sessão que ficou aberta
                }
            }
        }

        /// <summary>Dono do lobby: grava lugar, nível e nome nos dados da sala (aba Salas dos outros), só o que mudou.</summary>
        private void AtualizarDadosDaSala(PlayerController jogadorLocal)
        {
            if (!SouHost || Time.unscaledTime < _proximosDadosSala) return;
            _proximosDadosSala = Time.unscaledTime + 2f;
            string lugar = Salas.LugarMenu;
            int ato = 0, andar = 0, nivel = 0, dungeonHash = 0;
            if (jogadorLocal != null)
            {
                lugar = Salas.LugarHub;
                if (Fase2.RunCompartilhada.LugarDaRun(out string dungeon, out dungeonHash, out andar))
                {
                    lugar = Salas.LugarRun;
                    ato = Salas.AtoDoNome(dungeon);
                }
                try { nivel = Math.Min(jogadorLocal.PlayerSheet?.Experience?.Level?.Value ?? 0, Salas.NivelMaximo); }
                catch (Exception) { nivel = 0; }
            }
            GravarDado(Protocolo.ChaveLobbyDlc, MinhaCursedWar ? "1" : "0"); // quem herda o lobby anuncia a DLC dele
            GravarDado(Protocolo.ChaveLobbyLugar, lugar);
            GravarDado(Protocolo.ChaveLobbyAto, ato.ToString());
            GravarDado(Protocolo.ChaveLobbyAndar, andar.ToString());
            GravarDado(Protocolo.ChaveLobbyDungeon, dungeonHash.ToString()); // 0.19.0: trava de progresso de quem entra
            GravarDado(Protocolo.ChaveLobbyNivel, nivel.ToString());
            GravarDado(Protocolo.ChaveLobbyNome, SteamFriends.GetPersonaName());
            GravarDado(Protocolo.ChaveLobbyDono, Eu.m_SteamID.ToString()); // quem herda o lobby anuncia o próprio SteamID
            GravarDado(Protocolo.ChaveLobbySala, NomeDaSalaAtual()); // 0.25.0: quem herda anuncia "Sala de <ele>"
        }

        private void GravarDado(string chave, string valor)
        {
            if (_dadosGravados.TryGetValue(chave, out string antes) && antes == valor) return;
            if (SteamMatchmaking.SetLobbyData(Lobby, chave, valor)) _dadosGravados[chave] = valor;
        }

        private void RemoverPar(Par p, string motivo)
        {
            DestruirAvatar(p);
            SteamTransporte.Fechar(p.Id);
            Pares.Remove(p.Id.m_SteamID);
            Plugin.Log.LogInfo($"[Sessão] {p.Nome} {motivo}.");
            Avisar($"{p.Nome} {motivo}.");
        }

        // ---------- Mensagens ----------

        private void EnviarHello(Par p)
        {
            var h = new Hello
            {
                Protocolo = Protocolo.Versao, VersaoMod = Plugin.PluginVersion, VersaoJogo = Application.version,
                Nome = SteamFriends.GetPersonaName(), JaTeOuvi = p.HelloRecebido,
                Deluxe = MinhaDeluxe, CursedWar = MinhaCursedWar,
            };
            SteamTransporte.Enviar(p.Id, h.Serializar(), confiavel: true, Protocolo.CanalConfiavel);
            p.ProximoHello = Time.unscaledTime + 2f;
        }

        /// <summary>Recebe tudo o que chegou e cuida de timeouts. Chamar a cada frame.</summary>
        public void Atualizar(PlayerController jogadorLocal, int cenaLocal)
        {
            if (!SteamEstado.Pronto())
            {
                return;
            }
            if (EstadoAtual == Estado.Livre && _lobbyPendenteLinhaDeComando != CSteamID.Nil)
            {
                CSteamID l = _lobbyPendenteLinhaDeComando;
                _lobbyPendenteLinhaDeComando = CSteamID.Nil;
                Entrar(l);
            }
            if (BuscandoSalas && Time.unscaledTime - UltimaBusca > 15f)
            {
                BuscandoSalas = false; // a Steam não respondeu: "A busca falhou" e o botão volta
                FalhaNaBusca = true;
            }
            if (!EmSessao) return;

            LerDonoDoLobby(); // uma leitura por quadro, antes de tudo o que decide por SouHost/IdDoHost abaixo (expulsão, mensagens)
            AtualizarDadosDaSala(jogadorLocal);
            if (_expulsos.Count > 0 && SouHost && Time.unscaledTime >= _proximoReenvioExpulso)
            {
                _proximoReenvioExpulso = Time.unscaledTime + 2f;
                foreach (ulong id in _expulsos)
                {
                    var quem = new CSteamID(id);
                    if (!EhMembro(quem)) continue;
                    SteamTransporte.Enviar(quem, new[] { Protocolo.TipoExpulso }, confiavel: true, Protocolo.CanalConfiavel);
                    // 0.21.1: o aviso aos outros também se repete (quem estava com outro host fixado recusou o primeiro)
                    byte[] aviso = new ExpulsoAviso { SteamId = id }.Serializar();
                    foreach (Par outro in Pares.Values) if (outro.Compativel) SteamTransporte.Enviar(outro.Id, aviso, confiavel: true, Protocolo.CanalConfiavel);
                }
            }
            if (!_avisouDonoExpulso && !SouHost && _expulsos.Contains(_donoDoLobby))
            {
                _avisouDonoExpulso = true; // 0.21.1: a Steam passou a sala a um expulso; ninguém comanda a sua run
                Plugin.Log.LogWarning("[Sessão] A sala passou a um jogador que o host anterior expulsou; ele não comanda a sua run.");
                Avisar("A sala passou a um jogador que o host anterior expulsou. Saia do coop e entre em outra sala.");
            }

            // Sempre depois do LerDonoDoLobby acima: as validações de autoridade (Expulso, planos, ordens) usam o dono deste
            // quadro. Tratar mensagem em outro ponto (Update, callback) exige reler o dono antes.
            SteamTransporte.Receber(Protocolo.CanalConfiavel, (de, dados) => TratarConfiavel(de, dados, jogadorLocal));
            SteamTransporte.Receber(Protocolo.CanalSnapshots, (de, dados) => TratarSnapshot(de, dados, jogadorLocal, cenaLocal));
            ReceberSimulado(); // 0.31.1: só o dono de sala vazia com o eco (medidor); com um par no lobby não faz nada

            foreach (Par p in Pares.Values)
            {
                // Aperto de mão de duas vias: reenvia até ouvir o par E ele confirmar que nos ouviu.
                // (O primeiro Hello pode se perder se chegar antes de o outro lado aceitar a sessão.)
                if ((!p.HelloRecebido || !p.MeOuviu) && Time.unscaledTime >= p.ProximoHello) EnviarHello(p);
                if (p.Avatar != null && Time.unscaledTime - p.UltimoPacote > SemSinalApos && p.Avatar.gameObject.activeSelf)
                {
                    p.Avatar.gameObject.SetActive(false);
                }
            }
        }

        private void TratarConfiavel(CSteamID de, byte[] dados, PlayerController jogadorLocal)
        {
            if (dados.Length == 0 || !Pares.TryGetValue(de.m_SteamID, out Par p)) return;
            // 0.20.0: sem Hello compatível, só Hello, Tchau e Expulso passam (desconhecido do lobby não manda plano nem inimigo).
            if (!Validacao.PortaDeEntrada(dados[0], p.HelloRecebido, p.Compativel)) return;
            switch (dados[0])
            {
                case Protocolo.TipoHello:
                    Hello h = Hello.Desserializar(dados);
                    bool primeiroHello = !p.HelloRecebido;
                    p.HelloRecebido = true;
                    p.MeOuviu = h.JaTeOuvi;
                    if (!h.JaTeOuvi) EnviarHello(p);   // responde na hora: "também te ouvi"
                    if (!primeiroHello) break;
                    p.Nome = string.IsNullOrEmpty(h.Nome) ? p.Nome : Salas.LimparNome(h.Nome); // 0.18.0: com sala pública, o nome vem de desconhecidos
                    if (!SteamTransporte.ConferirCaminho(p.Id)) // 0.20.0: quem não é amigo só fica se a conexão é pelo relay
                    {
                        p.Compativel = false; // fora da escala e da porta de entrada
                        p.Incompatibilidade = "conexão fora do relay da Steam";
                        Avisar($"{p.Nome} foi desconectado: a conexão não passou pelo relay da Steam e o seu IP chegaria a ele.");
                        break;
                    }
                    p.Compativel = h.Protocolo == Protocolo.Versao && h.VersaoMod == Plugin.PluginVersion;
                    p.Incompatibilidade = p.Compativel ? null : $"mod {h.VersaoMod} (você {Plugin.PluginVersion})";
                    p.Deluxe = h.Deluxe;
                    p.CursedWar = h.CursedWar;
                    Plugin.Log.LogInfo($"[Sessão] Hello de {p.Nome}: mod {h.VersaoMod}, protocolo {h.Protocolo}, jogo {h.VersaoJogo}, " +
                                       $"Cursed War={(h.CursedWar ? "sim" : "não")}, Deluxe={(h.Deluxe ? "sim" : "não")} — {(p.Compativel ? "compatível" : "INCOMPATÍVEL")}.");
                    if (!p.Compativel) Avisar($"{p.Nome} usa outra versão do mod ({h.VersaoMod}). Ele não vai aparecer para você.");
                    else if (h.CursedWar != MinhaCursedWar) Avisar(Salas.TextoDlcDiferente(p.Nome, h.CursedWar)); // 0.21.0: dividem o jogo base
                    else if (h.Deluxe != MinhaDeluxe) Avisar($"Aviso: só um de vocês tem a Deluxe Edition (cosmética). A run funciona normalmente.");
                    if (p.Compativel && SouHost)
                    {
                        Fase2.RunCompartilhada.AoNovoConvidado(this, p);
                        Fase3.InimigosCompartilhados.AoNovoConvidado(this, p);
                        Fase3.ObjetosCompartilhados.AoNovoConvidado(this, p);
                        Fase3.SantuariosCompartilhados.AoNovoConvidado(this, p);
                        foreach (ulong idExpulso in _expulsos) // 0.21.1: quem chega depois também corta a conversa com quem ainda está no lobby
                        {
                            if (EhMembro(new CSteamID(idExpulso)))
                                SteamTransporte.Enviar(p.Id, new ExpulsoAviso { SteamId = idExpulso }.Serializar(), confiavel: true, Protocolo.CanalConfiavel);
                        }
                    }
                    if (p.Compativel) Fase2.RunCompartilhada.AoNovoPar(this, p); // 0.19.0: ForaDaRun a quem chegou depois
                    if (p.Compativel && jogadorLocal != null)
                    {
                        // Manda a aparência atual direto para quem acabou de chegar.
                        Aparencia a = AparenciaPersonagem.Capturar(jogadorLocal.Animator != null ? jogadorLocal.Animator.transform : null);
                        SteamTransporte.Enviar(p.Id, a.Serializar(), confiavel: true, Protocolo.CanalConfiavel);
                    }
                    break;
                case Protocolo.TipoAparencia:
                    p.Aparencia = Aparencia.Desserializar(dados);
                    if (p.Avatar != null) AparenciaPersonagem.Aplicar(p.Avatar.ModeloCorpo, p.Aparencia);
                    break;
                case Protocolo.TipoTchau:
                    RemoverPar(p, "saiu do coop");
                    break;
                case Protocolo.TipoExpulso: // 0.18.0: só vale do dono do lobby; 0.21.1: com run aberta, do host dela
                    if (!Salas.ExpulsaoValida(de.m_SteamID, IdDoHost, Eu.m_SteamID)) break;
                    _salasQueMeExpulsaram.Add(Lobby.m_SteamID);
                    Sair("expulso pelo host");
                    break;
                case Protocolo.TipoExpulsoAviso: // 0.21.1: o host expulsou um terceiro
                    ExpulsoAviso av = ExpulsoAviso.Desserializar(dados);
                    if (!Validacao.AvisoExpulsaoValido(de.m_SteamID, IdDoHost, av.SteamId, Eu.m_SteamID)) break;
                    if (!EhMembro(new CSteamID(av.SteamId))) break; // só de quem está no lobby (id inventado não enche a lista)
                    if (!_expulsos.Add(av.SteamId)) break;
                    if (Pares.TryGetValue(av.SteamId, out Par expulso)) RemoverPar(expulso, "foi expulso pelo host");
                    break;
                case Protocolo.TipoPlanoAndar:
                case Protocolo.TipoSpawnAndar:
                case Protocolo.TipoTimerBonus:
                case Protocolo.TipoFimRun:
                case Protocolo.TipoPedidoAvanco:
                case Protocolo.TipoForaDaRun:
                    Fase2.RunCompartilhada.Receber(this, p, dados);
                    break;
                case Protocolo.TipoReanimou:
                    Fase2.Reanimacao.Receber(this, p, dados);
                    break;
                case Protocolo.TipoMarcador: // 0.17.0
                    Interface.Marcadores.Receber(this, p, dados);
                    break;
                case Protocolo.TipoPainelAliado: // 0.27.0
                    Interface.PainelGrupo.Receber(p, dados);
                    break;
                case Protocolo.TipoPedidoTp: // 0.29.0
                case Protocolo.TipoRespostaTp:
                    Fase2.TpAliado.Receber(this, p, dados);
                    break;
                case Protocolo.TipoGolpes:
                    p.UltimoCombate = Time.unscaledTime; // 0.29.0: golpe dado pelo convidado conta como combate (TP)
                    Fase3.InimigosCompartilhados.Receber(this, p, dados);
                    break;
                case Protocolo.TipoInimigoSpawn:
                case Protocolo.TipoInimigoMorte:
                case Protocolo.TipoInimigoSumiu:
                case Protocolo.TipoStatusInimigos:
                case Protocolo.TipoAndarLimpo:
                case Protocolo.TipoInimigoAjuste:
                    Fase3.InimigosCompartilhados.Receber(this, p, dados);
                    break;
                case Protocolo.TipoFxNovo:
                case Protocolo.TipoFxFim:
                case Protocolo.TipoFxNos:
                    Fase3.FxCompartilhados.Receber(this, p, dados);
                    break;
                case Protocolo.TipoSonsAliado:
                case Protocolo.TipoSonsFim:
                    Fase3.SonsAliados.Receber(this, p, dados);
                    break;
                case Protocolo.TipoInimigoSkill:
                    Fase3.InimigosCompartilhados.Receber(this, p, dados);
                    break;
                case Protocolo.TipoObjetoSpawn:
                case Protocolo.TipoObjetoUsado:
                    Fase3.SantuariosCompartilhados.Receber(this, p, dados);
                    break;
                case Protocolo.TipoBauSpawn:
                case Protocolo.TipoBauAberto:
                case Protocolo.TipoQuebrou:
                    Fase3.ObjetosCompartilhados.Receber(this, p, dados);
                    break;
                case Protocolo.TipoPortaFumaca:
                    Fase3.PortasCompartilhadas.Receber(this, p, dados);
                    break;
            }
        }

        private void TratarSnapshot(CSteamID de, byte[] dados, PlayerController jogadorLocal, int cenaLocal)
        {
            if (!Pares.TryGetValue(de.m_SteamID, out Par p) || !p.Compativel || dados.Length == 0) return;
            if (dados[0] == Protocolo.TipoTimer)
            {
                Fase2.RunCompartilhada.Receber(this, p, dados);
                return;
            }
            if (dados[0] == Protocolo.TipoReanimando) // 0.16.0, canal não confiável
            {
                Fase2.Reanimacao.Receber(this, p, dados);
                return;
            }
            if (dados[0] == Protocolo.TipoDanosVistos)
            {
                Fase3.DanosVisiveis.Receber(this, p, dados);
                return;
            }
            if (dados[0] == Protocolo.TipoGolpesVistos)
            {
                Fase3.GolpesVisiveis.Receber(this, p, dados);
                return;
            }
            if (dados[0] == Protocolo.TipoFxMovimento || dados[0] == Protocolo.TipoFxNos)
            {
                Fase3.FxCompartilhados.Receber(this, p, dados);
                return;
            }
            if (dados[0] == Protocolo.TipoInimigosEstado)
            {
                Fase3.InimigosCompartilhados.Receber(this, p, dados);
                return;
            }
            PlayerSnapshot s = PlayerSnapshot.Desserializar(dados);
            if (!Validacao.SnapshotValido(s)) return; // 0.21.1: NaN/infinito ou posição absurda (20 Hz: descarta sem log)
            if (p.UltimoPacote < 0f)
            {
                Plugin.Log.LogInfo($"[Sessão] Primeiro snapshot de {p.Nome} ({dados.Length} bytes): cena {s.CenaHash}, andar {s.Andar}. Sua cena: {cenaLocal}.");
            }
            if (s.CenaHash != p.CenaRemota)
            {
                p.CenaRemota = s.CenaHash;
                Plugin.Log.LogInfo($"[Sessão] {p.Nome} agora na cena {s.CenaHash} (andar {s.Andar}); você na cena {cenaLocal} → {(s.CenaHash != -1 && s.CenaHash == cenaLocal ? "visível" : "não visível")}.");
            }
            // 0.16.0: snapshot um pouco atrasado (fora de ordem no canal não confiável) não muda Morto: depois de uma
            // reanimação, um "caído" velho cobraria a queda de novo. Diferença grande (o outro PC recomeçou a contagem)
            // vale como nova.
            bool atrasado = Validacao.SequenciaAtrasada(s.Sequencia, p.SequenciaMorto);
            bool caiu = !atrasado && s.Morto && !p.Morto && p.UltimoPacote >= 0f; // 0.15.0: aviso e −10 s (no host) a cada queda
            p.UltimoPacote = Time.unscaledTime;
            if (!atrasado)
            {
                p.Morto = s.Morto;
                p.SequenciaMorto = s.Sequencia;
                if (p.VidaVista >= 0f && s.Vida < p.VidaVista - 0.001f) p.UltimoCombate = Time.unscaledTime; // 0.29.0: levou dano (TP)
                p.VidaVista = s.Vida;
            }
            p.MesmoAndar = s.CenaHash != -1 && s.CenaHash == cenaLocal;
            if (caiu && p.MesmoAndar) Fase2.JogadorCaido.AliadoCaiu(p);

            if (jogadorLocal == null)
            {
                DestruirAvatar(p);
                return;
            }
            if (p.Avatar == null)
            {
                if (!p.MesmoAndar) return;
                double inicio = Desempenho.Agora();
                p.Avatar = AvatarFactory.Criar(jogadorLocal, p.Nome);
                Desempenho.SomarDetalhe(Desempenho.Detalhe.CriarAvatar, inicio);
                if (p.Aparencia != null) AparenciaPersonagem.Aplicar(p.Avatar.ModeloCorpo, p.Aparencia);
            }

            bool visivel = p.Avatar.gameObject.activeSelf;
            if (p.MesmoAndar && !visivel)
            {
                p.Avatar.Reiniciar();             // evita interpolar de um andar para outro
                p.Avatar.gameObject.SetActive(true);
            }
            else if (!p.MesmoAndar && visivel)
            {
                p.Avatar.gameObject.SetActive(false);
            }
            if (p.MesmoAndar) p.Avatar.Receber(s);
        }

        /// <summary>Envia o snapshot local para todos os pares compatíveis (canal não confiável).</summary>
        private bool _jaEnviouSnapshot;

        public void EnviarSnapshot(byte[] pacote)
        {
            if (!_jaEnviouSnapshot)
            {
                _jaEnviouSnapshot = true;
                Plugin.Log.LogInfo($"[Sessão] Enviando seus snapshots ({pacote.Length} bytes, 20/s) para {Pares.Count} aliado(s).");
            }
            foreach (Par p in Pares.Values)
            {
                if (p.Compativel) SteamTransporte.Enviar(p.Id, pacote, confiavel: false, Protocolo.CanalSnapshots);
            }
        }

        /// <summary>Envia para todos os pares compatíveis (com ou sem a mesma DLC desde a 0.21.0).</summary>
        public void EnviarParaConvidados(byte[] pacote, bool confiavel)
        {
            foreach (Par p in Pares.Values)
            {
                if (p.Compativel) SteamTransporte.Enviar(p.Id, pacote, confiavel, confiavel ? Protocolo.CanalConfiavel : Protocolo.CanalSnapshots);
            }
        }

        /// <summary>Envia a todos os pares compatíveis que estão no mesmo andar (efeitos visuais).</summary>
        public void EnviarParaMesmoAndar(byte[] pacote, bool confiavel)
        {
            foreach (Par p in Pares.Values)
            {
                if (p.Compativel && p.MesmoAndar) SteamTransporte.Enviar(p.Id, pacote, confiavel, confiavel ? Protocolo.CanalConfiavel : Protocolo.CanalSnapshots);
            }
            if (Pares.Count == 0 && Simulada) Reinjetar(pacote);
        }

        /// <summary>Há um aliado no mesmo andar (ou, na recepção simulada, o eco valendo por aliados).</summary>
        public bool AlguemNoMesmoAndar
        {
            get
            {
                foreach (Par p in Pares.Values) if (p.Compativel && p.MesmoAndar) return true;
                return Simulada;
            }
        }

        // ---------------- Recepção simulada (0.31.1, só para o medidor) ----------------

        /// <summary>Quantos aliados o eco representa na recepção simulada: o grupo cheio (3 além deste PC).</summary>
        public const int AliadosSimulados = 3;
        private const int ReinjetadosMaximo = 2048;
        private readonly Dictionary<ulong, Par> _paresSimulados = new Dictionary<ulong, Par>();
        private readonly Queue<byte[]> _reinjetados = new Queue<byte[]>();

        /// <summary>
        /// Dono de sala sem ninguém, com o eco (F8) ligado: o medidor simula o coop sem um segundo jogador. O estado dos
        /// inimigos é montado e só contado (0.31.0, <c>InimigosCompartilhados</c>) e, desde a 0.31.1, os pacotes cosméticos
        /// que iriam aos aliados do mesmo andar (números de dano, golpes vistos, sons, efeitos) voltam pela recepção
        /// deste PC como se viessem de <see cref="AliadosSimulados"/> aliados. Nada sai pela rede, e com um par no lobby
        /// a simulação não existe.
        /// </summary>
        public bool Simulada
        {
            get
            {
                if (!SouHost || Pares.Count > 0) return false;
                CoopRuntime r = CoopRuntime.Instancia;
                return !ReferenceEquals(r, null) && r.EcoAtivo;
            }
        }

        /// <summary>O par com este SteamID: um jogador do lobby ou, na recepção simulada, um dos aliados de mentira.</summary>
        public Par ParDe(ulong id) => Pares.TryGetValue(id, out Par p) || _paresSimulados.TryGetValue(id, out p) ? p : null;

        private void Reinjetar(byte[] pacote)
        {
            if (pacote.Length == 0 || !Validacao.CosmeticoDeAliado(pacote[0]) || _reinjetados.Count >= ReinjetadosMaximo) return;
            _reinjetados.Enqueue(pacote);
        }

        /// <summary>
        /// Entrega os pacotes reinjetados no quadro seguinte ao do envio, uma vez por aliado simulado, pelo transporte
        /// (cópia dos bytes e medidor) e pelos mesmos tratadores da rede. Os aliados de mentira não entram em
        /// <see cref="Pares"/> (escala, painel e autoridade não os veem) e usam o fantasma do eco como avatar.
        /// </summary>
        private void ReceberSimulado()
        {
            CoopRuntime r = CoopRuntime.Instancia;
            bool simulada = Simulada;
            RemotePlayerAvatar eco = simulada && !ReferenceEquals(r, null) ? r.AvatarDoEco : null;
            if (!simulada)
            {
                if (_paresSimulados.Count > 0 || _reinjetados.Count > 0) EncerrarSimulacao();
                return;
            }
            if (_paresSimulados.Count == 0)
            {
                for (ulong k = 1; k <= AliadosSimulados; k++) // SteamIDs que não existem (o eco direto usa a chave 0)
                    _paresSimulados[k] = new Par { Id = new CSteamID(k), Nome = "Eco " + k, HelloRecebido = true, MeOuviu = true, Compativel = true };
            }
            foreach (Par p in _paresSimulados.Values)
            {
                p.Avatar = eco; // null durante a troca de cena: os tratadores descartam, como com um aliado fora do andar
                p.MesmoAndar = eco != null;
            }
            if (_reinjetados.Count == 0) return;
            Desempenho.RecepcaoSimulada(AliadosSimulados);
            Action<CSteamID, byte[]> tratar = TratarSimulado;
            while (_reinjetados.Count > 0)
            {
                byte[] pacote = _reinjetados.Dequeue();
                foreach (Par p in _paresSimulados.Values) SteamTransporte.ReceberSimulada(p.Id, pacote, tratar);
            }
        }

        private void TratarSimulado(CSteamID de, byte[] dados)
        {
            if (!_paresSimulados.TryGetValue(de.m_SteamID, out Par p)) return;
            switch (dados[0])
            {
                case Protocolo.TipoDanosVistos:
                    Fase3.DanosVisiveis.Receber(this, p, dados);
                    break;
                case Protocolo.TipoGolpesVistos:
                    Fase3.GolpesVisiveis.Receber(this, p, dados);
                    break;
                case Protocolo.TipoFxNovo:
                case Protocolo.TipoFxMovimento:
                case Protocolo.TipoFxNos:
                case Protocolo.TipoFxFim:
                    Fase3.FxCompartilhados.Receber(this, p, dados);
                    break;
                case Protocolo.TipoSonsAliado:
                case Protocolo.TipoSonsFim:
                    Fase3.SonsAliados.Receber(this, p, dados);
                    break;
            }
        }

        private void EncerrarSimulacao()
        {
            _reinjetados.Clear();
            foreach (Par p in _paresSimulados.Values)
            {
                p.Avatar = null; // o fantasma é do eco: quem o destrói é o CoopRuntime
                p.MesmoAndar = false;
                Fase3.DanosVisiveis.EsquecerCor(p.Id.m_SteamID); // a cor dos aliados de verdade não muda por causa do teste
            }
            _paresSimulados.Clear();
        }

        public void EnviarPara(Par p, byte[] pacote)
        {
            SteamTransporte.Enviar(p.Id, pacote, confiavel: true, Protocolo.CanalConfiavel);
        }

        public void Avisar(string texto) => Aviso?.Invoke(texto);

        /// <summary>Reenvia a aparência se ela mudou (troca de equipamento).</summary>
        public void EnviarAparenciaSeMudou(PlayerController jogadorLocal)
        {
            if (!EmSessao || jogadorLocal == null || jogadorLocal.Animator == null) return;
            Aparencia a = AparenciaPersonagem.Capturar(jogadorLocal.Animator.transform);
            if (a.IgualA(_ultimaAparenciaEnviada)) return;
            _ultimaAparenciaEnviada = a;
            byte[] pacote = a.Serializar();
            foreach (Par p in Pares.Values)
            {
                if (p.Compativel) SteamTransporte.Enviar(p.Id, pacote, confiavel: true, Protocolo.CanalConfiavel);
            }
        }

        public void DestruirAvatares()
        {
            foreach (Par p in Pares.Values) DestruirAvatar(p);
        }

        private static void DestruirAvatar(Par p)
        {
            if (p.Avatar != null) UnityEngine.Object.Destroy(p.Avatar.gameObject);
            p.Avatar = null;
        }

        private static string Vazio(string s) => string.IsNullOrEmpty(s) ? "(sem mod)" : Salas.LimparNome(s); // texto do lobby (não confiável)
    }

    /// <summary>
    /// Estado do SteamManager do jogo, lido por reflexão. Nunca usar SteamManager.Instance:
    /// aquele getter cria um SteamManager novo, e o jogo lança exceção se a SteamAPI iniciar duas vezes.
    /// </summary>
    internal static class SteamEstado
    {
        private static readonly Type Tipo = AccessTools.TypeByName("HellClock.Steam.SteamManager");
        private static readonly System.Reflection.FieldInfo CampoInstancia = AccessTools.Field(Tipo, "s_instance");
        private static readonly System.Reflection.FieldInfo CampoIniciado = AccessTools.Field(Tipo, "m_bInitialized");

        public static bool Pronto()
        {
            object instancia = CampoInstancia?.GetValue(null);
            return instancia != null && (UnityEngine.Object)instancia != null && (bool)CampoIniciado.GetValue(instancia);
        }
    }
}
