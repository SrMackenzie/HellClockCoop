using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HellClockCoop.Atualizador
{
    /// <summary>
    /// GET por HTTPS com a WinHTTP do Windows (P/Invoke). No preloader não há TLS utilizável pelo Mono (o UnityTls
    /// ainda não subiu e o TLS antigo do Mono não fala TLS 1.2), e o mod não chama PowerShell, curl nem programa externo.
    /// Segue redirecionamentos HTTPS → HTTPS (padrão da WinHTTP), que é como o GitHub entrega os arquivos de release.
    /// </summary>
    internal static class WinHttp
    {
        private const uint AcessoProxyAutomatico = 4;      // WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY (Windows 8.1+)
        private const uint AcessoProxyPadrao = 0;          // WINHTTP_ACCESS_TYPE_DEFAULT_PROXY
        private const uint FlagSeguro = 0x00800000;        // WINHTTP_FLAG_SECURE
        private const uint ConsultaStatus = 19 | 0x20000000; // WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER

        /// <summary>
        /// Corpo da resposta 200, ou null com o motivo em <paramref name="erro"/>. <paramref name="timeoutMs"/> vale para
        /// cada etapa da WinHTTP e também como prazo total do download (um servidor lento não segura a leitura).
        /// </summary>
        public static byte[] Baixar(string url, int timeoutMs, int limiteBytes, string agente, out int status, out string erro)
        {
            status = 0;
            erro = null;
            int fim = Environment.TickCount + Math.Max(timeoutMs, 1);
            var uri = new Uri(url);
            if (uri.Scheme != "https") { erro = "só HTTPS"; return null; }

            IntPtr sessao = IntPtr.Zero, conexao = IntPtr.Zero, pedido = IntPtr.Zero;
            try
            {
                sessao = WinHttpOpen(agente, AcessoProxyAutomatico, null, null, 0);
                if (sessao == IntPtr.Zero) sessao = WinHttpOpen(agente, AcessoProxyPadrao, null, null, 0);
                if (sessao == IntPtr.Zero) { erro = Falha("WinHttpOpen"); return null; }
                if (timeoutMs < 1) timeoutMs = 1;
                WinHttpSetTimeouts(sessao, timeoutMs, timeoutMs, timeoutMs, timeoutMs);

                conexao = WinHttpConnect(sessao, uri.Host, (ushort)uri.Port, 0);
                if (conexao == IntPtr.Zero) { erro = Falha("WinHttpConnect"); return null; }
                pedido = WinHttpOpenRequest(conexao, "GET", uri.PathAndQuery, null, null, IntPtr.Zero, FlagSeguro);
                if (pedido == IntPtr.Zero) { erro = Falha("WinHttpOpenRequest"); return null; }
                if (!WinHttpSendRequest(pedido, null, 0, IntPtr.Zero, 0, 0, UIntPtr.Zero)) { erro = Falha("envio"); return null; }
                if (!WinHttpReceiveResponse(pedido, IntPtr.Zero)) { erro = Falha("resposta"); return null; }

                uint codigo = 0, tamanho = 4;
                if (!WinHttpQueryHeaders(pedido, ConsultaStatus, null, ref codigo, ref tamanho, IntPtr.Zero)) { erro = Falha("status"); return null; }
                status = (int)codigo;
                if (codigo != 200) { erro = "HTTP " + codigo; return null; }

                var corpo = new MemoryStream();
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    if (Environment.TickCount - fim > 0) { erro = "leitura: tempo esgotado"; return null; }
                    if (!WinHttpReadData(pedido, buffer, (uint)buffer.Length, out uint lidos)) { erro = Falha("leitura"); return null; }
                    if (lidos == 0) break;
                    if (corpo.Length + lidos > limiteBytes) { erro = $"resposta maior que {limiteBytes} bytes"; return null; }
                    corpo.Write(buffer, 0, (int)lidos);
                }
                return corpo.ToArray();
            }
            catch (Exception e)
            {
                erro = e.GetType().Name + ": " + e.Message;
                return null;
            }
            finally
            {
                if (pedido != IntPtr.Zero) WinHttpCloseHandle(pedido);
                if (conexao != IntPtr.Zero) WinHttpCloseHandle(conexao);
                if (sessao != IntPtr.Zero) WinHttpCloseHandle(sessao);
            }
        }

        private static string Falha(string etapa)
        {
            int codigo = Marshal.GetLastWin32Error();
            return codigo == 12002 ? etapa + ": tempo esgotado" : $"{etapa}: erro {codigo}";
        }

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr WinHttpOpen(string agente, uint tipoAcesso, string proxy, string semProxy, uint flags);

        [DllImport("winhttp.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool WinHttpSetTimeouts(IntPtr h, int resolver, int conectar, int enviar, int receber);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr WinHttpConnect(IntPtr sessao, string servidor, ushort porta, uint reservado);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr WinHttpOpenRequest(IntPtr conexao, string verbo, string objeto, string versao, string referencia, IntPtr tiposAceitos, uint flags);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool WinHttpSendRequest(IntPtr pedido, string cabecalhos, uint tamanhoCabecalhos, IntPtr opcional, uint tamanhoOpcional, uint tamanhoTotal, UIntPtr contexto);

        [DllImport("winhttp.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool WinHttpReceiveResponse(IntPtr pedido, IntPtr reservado);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool WinHttpQueryHeaders(IntPtr pedido, uint nivel, string nome, ref uint valor, ref uint tamanho, IntPtr indice);

        [DllImport("winhttp.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool WinHttpReadData(IntPtr pedido, byte[] buffer, uint aLer, out uint lidos);

        [DllImport("winhttp.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool WinHttpCloseHandle(IntPtr h);
    }
}
