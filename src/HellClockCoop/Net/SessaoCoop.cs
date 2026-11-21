using System;
using System.Collections.Generic;
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
        public bool Deluxe, CursedWar;
        public bool DlcOk;              // mesma Cursed War (decide se o convidado acompanha a run)
        public int CenaRemota = -2;   // -2 = ainda não visto (int.MinValue foi removido do mscorlib do jogo)
        public Aparencia Aparencia;
        public RemotePlayerAvatar Avatar;
    }

    /// <summary>
    /// Sessão coop sobre um lobby da Steam (amigos, até 4). Fase 1b: todos mandam snapshots para todos
    /// (malha completa, no máximo 3 pares); o dono do lobby é o host para as fases seguintes.
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

        public bool EmSessao => EstadoAtual == Estado.NoLobby;
        public bool SouHost => EmSessao && SteamMatchmaking.GetLobbyOwner(Lobby) == Eu;

        /// <summary>DLCs deste PC (lidas uma vez, quando o Steam fica pronto).</summary>
        public bool MinhaDeluxe { get; private set; }
        public bool MinhaCursedWar { get; private set; }

        /// <summary>O dono do lobby, se já trocou Hello conosco.</summary>
        public Par Host => EmSessao && !SouHost && Pares.TryGetValue(SteamMatchmaking.GetLobbyOwner(Lobby).m_SteamID, out Par h) ? h : null;

        /// <summary>Fase 2: este PC segue a run do host (andar, timer, início e fim).</summary>
        public bool ModoConvidado
        {
            get { Par h = Host; return h != null && h.Compativel && h.DlcOk; }
        }

        /// <summary>Fase 2: este PC é o host e há ao menos um convidado acompanhando a run.</summary>
        public bool ModoHost
        {
            get
            {
                if (!SouHost) return false;
                foreach (Par p in Pares.Values) if (p.Compativel && p.DlcOk) return true;
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
        private CallResult<LobbyCreated_t> _crLobbyCriado;

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
            _crLobbyCriado = CallResult<LobbyCreated_t>.Create(OnLobbyCriado);
            SteamNetworkingUtils.InitRelayNetworkAccess(); // adianta a conexão com os relays da Steam

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
            SteamAPICall_t chamada = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaximoJogadores);
            _crLobbyCriado.Set(chamada);
            Avisar("Criando o coop...");
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
            Pares.Clear();
            Fase3.InimigosCompartilhados.Encerrar();
            Fase3.ObjetosCompartilhados.Encerrar();
            Fase3.SantuariosCompartilhados.Encerrar();
            Fase3.PortasCompartilhadas.Encerrar();
            Fase3.FxCompartilhados.Encerrar();
            Fase3.AlvosAliados.Encerrar();
            Lobby = CSteamID.Nil;
            _ultimaAparenciaEnviada = null;
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
            Plugin.Log.LogInfo($"[Sessão] Coop criado (lobby {r.m_ulSteamIDLobby}).");
            // O LobbyEnter_t também chega para quem cria; lá o estado vira NoLobby.
            SteamFriends.ActivateGameOverlayInviteDialog(lobby);
        }

        private void OnConviteAceito(GameLobbyJoinRequested_t c)
        {
            Plugin.Log.LogInfo($"[Sessão] Convite aceito de {SteamFriends.GetFriendPersonaName(c.m_steamIDFriend)}.");
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
            EstadoAtual = Estado.NoLobby;
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobby.m_SteamID);
            string dono = SteamFriends.GetFriendPersonaName(SteamMatchmaking.GetLobbyOwner(lobby));
            Plugin.Log.LogInfo($"[Sessão] No coop (lobby {lobby.m_SteamID}, host {dono}, {(SouHost ? "você é o host" : "você é convidado")}).");
            Avisar(SouHost ? "Coop criado! Convide amigos pelo overlay (o menu do coop abre de novo)." : $"Você entrou no coop de {dono}.");
            AtualizarMembros();
        }

        private void OnMembrosMudaram(LobbyChatUpdate_t u)
        {
            if (u.m_ulSteamIDLobby != Lobby.m_SteamID) return;
            AtualizarMembros();
        }

        private void OnPedidoDeSessao(SteamNetworkingMessagesSessionRequest_t pedido)
        {
            CSteamID quem = pedido.m_identityRemote.GetSteamID();
            if (EmSessao && EhMembro(quem))
            {
                SteamTransporte.Aceitar(pedido.m_identityRemote);
            }
            else
            {
                Plugin.Log.LogInfo($"[Rede] Pedido de conexão ignorado: {quem} não está no seu coop.");
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
                presentes.Add(id.m_SteamID);
                if (!Pares.ContainsKey(id.m_SteamID))
                {
                    var par = new Par { Id = id, Nome = SteamFriends.GetFriendPersonaName(id) };
                    Pares[id.m_SteamID] = par;
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
            if (!EmSessao) return;

            SteamTransporte.Receber(Protocolo.CanalConfiavel, (de, dados) => TratarConfiavel(de, dados, jogadorLocal));
            SteamTransporte.Receber(Protocolo.CanalSnapshots, (de, dados) => TratarSnapshot(de, dados, jogadorLocal, cenaLocal));

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
            switch (dados[0])
            {
                case Protocolo.TipoHello:
                    Hello h = Hello.Desserializar(dados);
                    bool primeiroHello = !p.HelloRecebido;
                    p.HelloRecebido = true;
                    p.MeOuviu = h.JaTeOuvi;
                    if (!h.JaTeOuvi) EnviarHello(p);   // responde na hora: "também te ouvi"
                    if (!primeiroHello) break;
                    p.Nome = string.IsNullOrEmpty(h.Nome) ? p.Nome : h.Nome;
                    p.Compativel = h.Protocolo == Protocolo.Versao && h.VersaoMod == Plugin.PluginVersion;
                    p.Incompatibilidade = p.Compativel ? null : $"mod {h.VersaoMod} (você {Plugin.PluginVersion})";
                    p.Deluxe = h.Deluxe;
                    p.CursedWar = h.CursedWar;
                    p.DlcOk = h.CursedWar == MinhaCursedWar;
                    Plugin.Log.LogInfo($"[Sessão] Hello de {p.Nome}: mod {h.VersaoMod}, protocolo {h.Protocolo}, jogo {h.VersaoJogo}, " +
                                       $"Cursed War={(h.CursedWar ? "sim" : "não")}, Deluxe={(h.Deluxe ? "sim" : "não")} — {(p.Compativel ? "compatível" : "INCOMPATÍVEL")}{(p.DlcOk ? "" : ", DLC diferente")}.");
                    if (!p.Compativel) Avisar($"{p.Nome} usa outra versão do mod ({h.VersaoMod}). Ele não vai aparecer para você.");
                    else if (!p.DlcOk) Avisar($"{p.Nome} {(h.CursedWar ? "tem" : "não tem")} a DLC Cursed War e você {(MinhaCursedWar ? "tem" : "não tem")}: vocês se veem, mas não dividem a run.");
                    else if (h.Deluxe != MinhaDeluxe) Avisar($"Aviso: só um de vocês tem a Deluxe Edition (cosmética). A run funciona normalmente.");
                    if (p.Compativel && p.DlcOk && SouHost)
                    {
                        Fase2.RunCompartilhada.AoNovoConvidado(this, p);
                        Fase3.InimigosCompartilhados.AoNovoConvidado(this, p);
                        Fase3.ObjetosCompartilhados.AoNovoConvidado(this, p);
                        Fase3.SantuariosCompartilhados.AoNovoConvidado(this, p);
                    }
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
                case Protocolo.TipoPlanoAndar:
                case Protocolo.TipoSpawnAndar:
                case Protocolo.TipoTimerBonus:
                case Protocolo.TipoFimRun:
                    Fase2.RunCompartilhada.Receber(this, p, dados);
                    break;
                case Protocolo.TipoInimigoSpawn:
                case Protocolo.TipoInimigoMorte:
                case Protocolo.TipoInimigoSumiu:
                case Protocolo.TipoGolpes:
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
            if (dados[0] == Protocolo.TipoDanosVistos)
            {
                Fase3.DanosVisiveis.Receber(this, p, dados);
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
            if (p.UltimoPacote < 0f)
            {
                Plugin.Log.LogInfo($"[Sessão] Primeiro snapshot de {p.Nome} ({dados.Length} bytes): cena {s.CenaHash}, andar {s.Andar}. Sua cena: {cenaLocal}.");
            }
            if (s.CenaHash != p.CenaRemota)
            {
                p.CenaRemota = s.CenaHash;
                Plugin.Log.LogInfo($"[Sessão] {p.Nome} agora na cena {s.CenaHash} (andar {s.Andar}); você na cena {cenaLocal} → {(s.CenaHash != -1 && s.CenaHash == cenaLocal ? "visível" : "não visível")}.");
            }
            p.UltimoPacote = Time.unscaledTime;
            p.Morto = s.Morto;
            p.MesmoAndar = s.CenaHash != -1 && s.CenaHash == cenaLocal;

            if (jogadorLocal == null)
            {
                DestruirAvatar(p);
                return;
            }
            if (p.Avatar == null)
            {
                if (!p.MesmoAndar) return;
                p.Avatar = AvatarFactory.Criar(jogadorLocal, p.Nome);
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

        /// <summary>Envia para todos os convidados que acompanham a run (compatíveis e com a mesma DLC).</summary>
        public void EnviarParaConvidados(byte[] pacote, bool confiavel)
        {
            foreach (Par p in Pares.Values)
            {
                if (p.Compativel && p.DlcOk) SteamTransporte.Enviar(p.Id, pacote, confiavel, confiavel ? Protocolo.CanalConfiavel : Protocolo.CanalSnapshots);
            }
        }

        /// <summary>Envia a todos os pares compatíveis que estão no mesmo andar (efeitos visuais).</summary>
        public void EnviarParaMesmoAndar(byte[] pacote, bool confiavel)
        {
            foreach (Par p in Pares.Values)
            {
                if (p.Compativel && p.MesmoAndar) SteamTransporte.Enviar(p.Id, pacote, confiavel, confiavel ? Protocolo.CanalConfiavel : Protocolo.CanalSnapshots);
            }
        }

        public bool AlguemNoMesmoAndar
        {
            get
            {
                foreach (Par p in Pares.Values) if (p.Compativel && p.MesmoAndar) return true;
                return false;
            }
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

        private static string Vazio(string s) => string.IsNullOrEmpty(s) ? "(sem mod)" : s;
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
