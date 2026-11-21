using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HellClockCoop.Atualizador
{
    /// <summary>Um arquivo da release: caminho relativo à pasta BepInEx, tamanho e SHA-256.</summary>
    internal sealed class ArquivoDaRelease
    {
        public string Destino;   // ex.: plugins/HellClockCoop/HellClockCoop.dll
        public long Tamanho;
        public string Sha256;    // hexadecimal minúsculo

        /// <summary>Nome do arquivo avulso na release do GitHub (sem pastas).</summary>
        public string Nome => Destino.Substring(Destino.LastIndexOf('/') + 1);
    }

    /// <summary>
    /// manifesto.txt da release, assinado pelo autor (manifesto.txt.sig = RSA PKCS#1 v1.5 + SHA-256, em Base64).
    /// Formato (UTF-8, uma entrada por linha):
    /// <code>
    /// hellclockcoop-manifesto 1
    /// versao 0.8.0
    /// arquivo plugins/HellClockCoop/HellClockCoop.dll 176640 &lt;sha256&gt;
    /// </code>
    /// Só aceita destinos dentro de BepInEx\plugins\HellClockCoop\ e o próprio atualizador em BepInEx\patchers\.
    /// </summary>
    internal sealed class Manifesto
    {
        public const string Cabecalho = "hellclockcoop-manifesto 1";
        public const string DestinoPlugin = "plugins/HellClockCoop/HellClockCoop.dll";
        public const string DestinoAtualizador = "patchers/HellClockCoop.Atualizador.dll";
        public const long TamanhoMaximoArquivo = 16L * 1024 * 1024;

        private static readonly Regex VersaoValida = new Regex(@"^\d{1,4}\.\d{1,4}\.\d{1,4}$");
        private static readonly Regex NomeValido = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$");
        private static readonly Regex HashValido = new Regex(@"^[0-9a-f]{64}$");

        public string VersaoTexto;
        public Version Versao;
        public readonly List<ArquivoDaRelease> Arquivos = new List<ArquivoDaRelease>();

        /// <summary>
        /// Lê só a linha "versao" de um manifesto AINDA NÃO verificado, para montar o endereço da assinatura
        /// (a da mesma release). Nada além disso é usado antes de a assinatura conferir.
        /// </summary>
        public static string VersaoSemVerificar(byte[] texto)
        {
            foreach (string linha in Linhas(texto))
            {
                if (!linha.StartsWith("versao ", StringComparison.Ordinal)) continue;
                string v = linha.Substring(7);
                return VersaoValida.IsMatch(v) ? v : null;
            }
            return null;
        }

        /// <summary>Confere a assinatura e só então interpreta o manifesto. Null com o motivo em <paramref name="erro"/>.</summary>
        public static Manifesto Ler(byte[] texto, byte[] assinaturaBase64, RSAParameters chavePublica, out string erro)
        {
            erro = null;
            if (!AssinaturaConfere(texto, assinaturaBase64, chavePublica))
            {
                erro = "assinatura inválida";
                return null;
            }

            var m = new Manifesto();
            var destinos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool cabecalho = false;
            foreach (string linha in Linhas(texto))
            {
                if (linha.Length == 0) continue;
                if (!cabecalho)
                {
                    if (linha != Cabecalho) { erro = "formato de manifesto desconhecido"; return null; }
                    cabecalho = true;
                    continue;
                }
                string[] p = linha.Split(' ');
                if (p[0] == "versao" && p.Length == 2 && m.VersaoTexto == null && VersaoValida.IsMatch(p[1]))
                {
                    m.VersaoTexto = p[1];
                    m.Versao = new Version(p[1]);
                }
                else if (p[0] == "arquivo" && p.Length == 4)
                {
                    var a = new ArquivoDaRelease { Destino = p[1], Sha256 = p[3] };
                    if (!DestinoPermitido(a.Destino) || !long.TryParse(p[2], out a.Tamanho) || a.Tamanho <= 0
                        || a.Tamanho > TamanhoMaximoArquivo || !HashValido.IsMatch(a.Sha256)
                        || !destinos.Add(a.Destino) || !nomes.Add(a.Nome))
                    {
                        erro = "entrada de arquivo inválida: " + p[1];
                        return null;
                    }
                    m.Arquivos.Add(a);
                }
                else
                {
                    erro = "linha inválida no manifesto";
                    return null;
                }
            }
            if (m.VersaoTexto == null) { erro = "manifesto sem versão"; return null; }
            if (!destinos.Contains(DestinoPlugin)) { erro = "manifesto sem o plugin"; return null; }
            return m;
        }

        /// <summary>plugins/HellClockCoop/&lt;nome&gt; ou o próprio atualizador; nada de subpastas, "..", ou outro lugar.</summary>
        public static bool DestinoPermitido(string destino)
        {
            if (destino == DestinoAtualizador) return true;
            const string pasta = "plugins/HellClockCoop/";
            if (!destino.StartsWith(pasta, StringComparison.Ordinal)) return false;
            string nome = destino.Substring(pasta.Length);
            return NomeValido.IsMatch(nome) && nome.IndexOf("..", StringComparison.Ordinal) < 0 && !nome.EndsWith(".", StringComparison.Ordinal);
        }

        public static bool AssinaturaConfere(byte[] texto, byte[] assinaturaBase64, RSAParameters chavePublica)
        {
            try
            {
                byte[] assinatura = Convert.FromBase64String(Encoding.ASCII.GetString(assinaturaBase64).Trim());
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.ImportParameters(chavePublica);
                    return rsa.VerifyData(texto, CryptoConfig.MapNameToOID("SHA256"), assinatura);
                }
            }
            catch (Exception)
            {
                return false; // Base64 ou chave malformada: assinatura não confere
            }
        }

        public static string Sha256(byte[] dados)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(dados);
                var sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static IEnumerable<string> Linhas(byte[] texto)
        {
            using (var leitor = new StringReader(Encoding.UTF8.GetString(texto)))
            {
                string linha;
                while ((linha = leitor.ReadLine()) != null) yield return linha.TrimEnd('\r');
            }
        }
    }
}
