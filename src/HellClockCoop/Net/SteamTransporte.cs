using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Envio e recebimento pela API SteamNetworkingMessages (P2P pela Steam: sem abrir portas). A sessão com cada par
    /// abre sozinha no primeiro envio (ou ao aceitar o pedido dele). Desde a 0.20.0, com quem não é amigo na Steam a
    /// sessão abre sem ICE (só pelo relay da Steam: o IP de um não chega ao outro); com amigos, como antes.
    /// </summary>
    internal static class SteamTransporte
    {
        private static readonly IntPtr[] Buffer = new IntPtr[64];
        private static readonly HashSet<ulong> SessaoAjustada = new HashSet<ulong>(); // pares cuja sessão já abriu com o ICE certo
        private static readonly HashSet<ulong> SemRelay = new HashSet<ulong>();       // não amigos que conectaram fora do relay: recusados
        private static bool _semIceAtivo;   // o global está em 0 (sem ICE): falso se a Steam recusou o ajuste
        private static bool _avisouFalhaIce;

        public static long BytesEnviados { get; private set; }
        public static long BytesRecebidos { get; private set; }

        public static bool Enviar(CSteamID para, byte[] dados, bool confiavel, int canal)
        {
            var identidade = new SteamNetworkingIdentity();
            identidade.SetSteamID(para);
            int flags = (confiavel ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable | Constants.k_nSteamNetworkingSend_NoNagle)
                        | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            bool primeira = false;
            GCHandle h = default;
            try
            {
                if (!PodeAbrir(para, out primeira)) return false;
                h = GCHandle.Alloc(dados, GCHandleType.Pinned);
                EResult r = SteamNetworkingMessages.SendMessageToUser(ref identidade, h.AddrOfPinnedObject(), (uint)dados.Length, flags, canal);
                if (r == EResult.k_EResultOK)
                {
                    BytesEnviados += dados.Length;
                    if (dados.Length > 0) Desempenho.MensagemEnviada(dados[0], dados.Length); // por tipo, no resumo do medidor
                    return true;
                }
                return false;
            }
            finally
            {
                if (h.IsAllocated) h.Free();
                if (primeira) _semIceAtivo = DefinirIceGlobal(Validacao.IceDesligado);
            }
        }

        /// <summary>Entrega cada mensagem pendente do canal ao <paramref name="tratar"/>. Libera a memória nativa sempre.</summary>
        public static void Receber(int canal, Action<CSteamID, byte[]> tratar)
        {
            int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(canal, Buffer, Buffer.Length);
            if (n >= Buffer.Length) Desempenho.BufferCheio(); // no limite: o que não coube fica para o quadro seguinte (medidor, 0.30.1)
            for (int i = 0; i < n; i++)
            {
                CSteamID de;
                byte[] dados;
                try
                {
                    SteamNetworkingMessage_t msg = SteamNetworkingMessage_t.FromIntPtr(Buffer[i]);
                    dados = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, dados, 0, msg.m_cbSize);
                    Desempenho.MensagemRecebida(msg.m_cbSize);
                    de = msg.m_identityPeer.GetSteamID();
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(Buffer[i]);
                    Buffer[i] = IntPtr.Zero;
                }
                BytesRecebidos += dados.Length;
                try
                {
                    tratar(de, dados);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Rede] Mensagem inválida de {de} ({dados.Length} bytes): {e.Message}");
                }
            }
        }

        public static void Fechar(CSteamID com)
        {
            var identidade = new SteamNetworkingIdentity();
            identidade.SetSteamID(com);
            SteamNetworkingMessages.CloseSessionWithUser(ref identidade);
            SessaoAjustada.Remove(com.m_SteamID); // a próxima sessão com ele abre com o ICE ajustado de novo
        }

        public static bool Aceitar(SteamNetworkingIdentity identidade)
        {
            bool primeira = false;
            try
            {
                if (!PodeAbrir(identidade.GetSteamID(), out primeira)) return false;
                return SteamNetworkingMessages.AcceptSessionWithUser(ref identidade);
            }
            finally
            {
                if (primeira) _semIceAtivo = DefinirIceGlobal(Validacao.IceDesligado);
            }
        }

        /// <summary>
        /// Quando a Steam fica pronta (SessaoCoop.TentarInicializar, antes de qualquer lobby): global sem ICE desde já (um
        /// pedido de sessão que chegue antes do 1º envio também nasce só com relay).
        /// </summary>
        public static void Preparar() => _semIceAtivo = DefinirIceGlobal(Validacao.IceDesligado);

        /// <summary>
        /// A SteamNetworkingMessages copia o valor GLOBAL de P2P_Transport_ICE_Enable ao criar a sessão com o par. Na 1ª vez
        /// por par: amigo, -1 (padrão do usuário); outro, 0 (sem ICE, só relay). Depois de abrir, o global volta a 0 (quem
        /// chama), para que uma sessão reaberta sozinha (AutoRestartBrokenSession) nunca saia com ICE para quem não é amigo.
        /// Fail-closed: se a Steam não aceitou o 0, não abre sessão com quem não é amigo.
        /// </summary>
        private static bool PodeAbrir(CSteamID par, out bool primeira)
        {
            primeira = false;
            if (SemRelay.Contains(par.m_SteamID)) return false;
            if (SessaoAjustada.Contains(par.m_SteamID)) return true;
            bool amigo = EhAmigo(par);
            if (!amigo && !_semIceAtivo) _semIceAtivo = DefinirIceGlobal(Validacao.IceDesligado);
            if (!amigo && !_semIceAtivo)
            {
                Plugin.Log.LogWarning($"[Rede] Sessão com {par} recusada: a Steam não aceitou desligar o ICE, e sem isso o seu IP chegaria a quem não é seu amigo.");
                return false;
            }
            SessaoAjustada.Add(par.m_SteamID);
            primeira = true;
            Plugin.Log.LogInfo($"[Rede] Sessão com {par}: {(amigo ? "amigo na Steam, conexão como a Steam escolher" : "não é amigo, só pelo relay da Steam (sem IP)")}.");
            if (amigo && DefinirIceGlobal(Validacao.IceDoPar(true))) _semIceAtivo = false; // -1 até quem chamou voltar a 0
            return true;
        }

        private static bool EhAmigo(CSteamID par)
        {
            try { return SteamFriends.HasFriend(par, EFriendFlags.k_EFriendFlagImmediate); }
            catch (Exception) { return false; } // sem saber, trata como desconhecido (só relay)
        }

        /// <summary>
        /// Depois do 1º Hello do par (sessão já conectada): confere o caminho e registra no log (relay ou direta). Quem não
        /// é amigo e não está no relay é desconectado e recusado até sair do coop (Esquecer), e devolve falso. Sem
        /// conseguir ler a conexão, só registra (a barreira principal é o ICE em 0 antes de abrir a sessão).
        /// </summary>
        public static bool ConferirCaminho(CSteamID par)
        {
            try
            {
                var identidade = new SteamNetworkingIdentity();
                identidade.SetSteamID(par);
                ESteamNetworkingConnectionState estado = SteamNetworkingMessages.GetSessionConnectionInfo(ref identidade, out SteamNetConnectionInfo_t info, out _);
                if (estado != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                {
                    Plugin.Log.LogInfo($"[Rede] Conexão com {par} ainda não conferida (estado {estado}).");
                    return true;
                }
                bool relay = (info.m_nFlags & Constants.k_nSteamNetworkConnectionInfoFlags_Relayed) != 0;
                bool amigo = EhAmigo(par);
                Plugin.Log.LogInfo($"[Rede] Conexão com {par}: {(relay ? "pelo relay da Steam" : "direta")}{(amigo ? " (amigo)" : "")}.");
                if (Validacao.CaminhoAceito(amigo, relay)) return true;
                Plugin.Log.LogWarning($"[Rede] {par} não é amigo e a conexão saiu fora do relay; desconectado (o seu IP não deve chegar a ele).");
                SemRelay.Add(par.m_SteamID);
                Fechar(par);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Rede] Não deu para conferir a conexão com {par}: {e.Message}");
                return true;
            }
        }

        /// <summary>Ao sair do coop: esquece os pares recusados fora do relay (no próximo coop são conferidos de novo).</summary>
        public static void Esquecer() => SemRelay.Clear();

        private static bool DefinirIceGlobal(int valor)
        {
            GCHandle h = GCHandle.Alloc(valor, GCHandleType.Pinned);
            try
            {
                bool ok = SteamNetworkingUtils.SetConfigValue(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_P2P_Transport_ICE_Enable,
                    ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero,
                    ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, h.AddrOfPinnedObject());
                if (!ok && !_avisouFalhaIce)
                {
                    _avisouFalhaIce = true;
                    Plugin.Log.LogWarning($"[Rede] A Steam recusou ajustar P2P_Transport_ICE_Enable para {valor}.");
                }
                return ok;
            }
            catch (Exception e)
            {
                if (!_avisouFalhaIce) { _avisouFalhaIce = true; Plugin.Log.LogWarning($"[Rede] Falha ao ajustar P2P_Transport_ICE_Enable: {e.Message}"); }
                return false;
            }
            finally
            {
                h.Free();
            }
        }
    }
}
