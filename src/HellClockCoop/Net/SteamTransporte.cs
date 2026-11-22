using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Envio e recebimento pela API SteamNetworkingMessages (P2P com relay da Steam: sem abrir portas,
    /// sem expor IP). A sessão com cada par abre sozinha no primeiro envio.
    /// </summary>
    internal static class SteamTransporte
    {
        private static readonly IntPtr[] Buffer = new IntPtr[64];

        public static long BytesEnviados { get; private set; }
        public static long BytesRecebidos { get; private set; }

        public static bool Enviar(CSteamID para, byte[] dados, bool confiavel, int canal)
        {
            var identidade = new SteamNetworkingIdentity();
            identidade.SetSteamID(para);
            int flags = (confiavel ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable | Constants.k_nSteamNetworkingSend_NoNagle)
                        | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            GCHandle h = GCHandle.Alloc(dados, GCHandleType.Pinned);
            try
            {
                EResult r = SteamNetworkingMessages.SendMessageToUser(ref identidade, h.AddrOfPinnedObject(), (uint)dados.Length, flags, canal);
                if (r == EResult.k_EResultOK)
                {
                    BytesEnviados += dados.Length;
                    return true;
                }
                return false;
            }
            finally
            {
                h.Free();
            }
        }

        /// <summary>Entrega cada mensagem pendente do canal ao <paramref name="tratar"/>. Libera a memória nativa sempre.</summary>
        public static void Receber(int canal, Action<CSteamID, byte[]> tratar)
        {
            int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(canal, Buffer, Buffer.Length);
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
        }

        public static bool Aceitar(SteamNetworkingIdentity identidade)
        {
            return SteamNetworkingMessages.AcceptSessionWithUser(ref identidade);
        }
    }
}
