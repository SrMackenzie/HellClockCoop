using System;
using System.Collections.Generic;
using System.IO;

namespace HellClockCoop.Atualizador
{
    /// <summary>
    /// Troca os arquivos da release na pasta BepInEx, guardando os anteriores e voltando atrás se algo falhar.
    /// Roda no preloader, antes de o chainloader carregar o plugin, que portanto não está em uso. O arquivo do próprio
    /// atualizador, já carregado, não fica travado pelo Mono (testado no preloader real em 21/11/2026: renomear e abrir
    /// para escrita funcionaram); ele só é movido, nunca escrito por cima, e a versão nova vale na próxima abertura.
    /// Pastas de trabalho: BepInEx\HellClockCoop\atualizador\novo (download conferido) e \anterior (os arquivos
    /// substituídos na última atualização; só a última).
    /// </summary>
    internal static class Instalacao
    {
        public static string PastaDeTrabalho(string raizBepInEx) => Path.Combine(Path.Combine(raizBepInEx, "HellClockCoop"), "atualizador");

        /// <param name="conteudo">destino → bytes já conferidos (só os arquivos que mudaram).</param>
        /// <param name="versaoDoPlugin">lê a versão do plugin instalado (BepInPlugin) para a validação final.</param>
        public static bool Aplicar(string raizBepInEx, Manifesto manifesto, IDictionary<string, byte[]> conteudo,
                                   Func<string, string> versaoDoPlugin, out string erro)
        {
            erro = null;
            string trabalho = PastaDeTrabalho(raizBepInEx);
            string novo = Path.Combine(trabalho, "novo");
            string anterior = Path.Combine(trabalho, "anterior");
            var trocados = new List<KeyValuePair<ArquivoDaRelease, bool>>(); // arquivo, havia versão anterior

            try
            {
                // 1) grava e confere tudo fora do lugar
                if (Directory.Exists(novo)) Directory.Delete(novo, true);
                foreach (ArquivoDaRelease a in manifesto.Arquivos)
                {
                    if (!conteudo.TryGetValue(a.Destino, out byte[] dados)) continue;
                    string caminho = Caminho(novo, a.Destino);
                    Directory.CreateDirectory(Path.GetDirectoryName(caminho));
                    File.WriteAllBytes(caminho, dados);
                    if (Manifesto.Sha256(File.ReadAllBytes(caminho)) != a.Sha256) throw new IOException("cópia conferida diferente: " + a.Nome);
                }

                // 2) guarda a versão atual e põe a nova no lugar (só movimentos dentro da pasta BepInEx)
                if (Directory.Exists(anterior)) Directory.Delete(anterior, true);
                foreach (ArquivoDaRelease a in manifesto.Arquivos)
                {
                    if (!conteudo.ContainsKey(a.Destino)) continue;
                    string alvo = Caminho(raizBepInEx, a.Destino);
                    bool havia = File.Exists(alvo);
                    if (havia)
                    {
                        string guarda = Caminho(anterior, a.Destino);
                        Directory.CreateDirectory(Path.GetDirectoryName(guarda));
                        File.Move(alvo, guarda);
                    }
                    trocados.Add(new KeyValuePair<ArquivoDaRelease, bool>(a, havia));
                    Directory.CreateDirectory(Path.GetDirectoryName(alvo));
                    File.Move(Caminho(novo, a.Destino), alvo);
                }

                // 3) validação no lugar definitivo
                foreach (ArquivoDaRelease a in manifesto.Arquivos)
                {
                    if (Manifesto.Sha256(File.ReadAllBytes(Caminho(raizBepInEx, a.Destino))) != a.Sha256)
                        throw new IOException("arquivo instalado não confere: " + a.Nome);
                }
                string versao = versaoDoPlugin(Caminho(raizBepInEx, Manifesto.DestinoPlugin));
                if (versao != manifesto.VersaoTexto)
                    throw new IOException($"o plugin instalado diz versão {versao ?? "ilegível"}, o manifesto diz {manifesto.VersaoTexto}");

                ApagarSemFalhar(novo);
                return true;
            }
            catch (Exception e)
            {
                erro = e.Message;
                string falhaNaVolta = Voltar(raizBepInEx, anterior, trocados);
                if (falhaNaVolta != null) erro += "; e não deu para voltar a versão anterior: " + falhaNaVolta;
                ApagarSemFalhar(novo);
                return false;
            }
        }

        /// <summary>Desfaz na ordem inversa. Null se voltou tudo; senão, o primeiro problema.</summary>
        private static string Voltar(string raiz, string anterior, List<KeyValuePair<ArquivoDaRelease, bool>> trocados)
        {
            string problema = null;
            for (int i = trocados.Count - 1; i >= 0; i--)
            {
                try
                {
                    string alvo = Caminho(raiz, trocados[i].Key.Destino);
                    if (File.Exists(alvo)) File.Delete(alvo);
                    if (trocados[i].Value) File.Move(Caminho(anterior, trocados[i].Key.Destino), alvo);
                }
                catch (Exception e)
                {
                    if (problema == null) problema = trocados[i].Key.Nome + ": " + e.Message;
                }
            }
            return problema;
        }

        /// <summary>
        /// Jogo fechado entre guardar a versão atual e pôr a nova no lugar: o destino ficou vazio, e a versão guardada
        /// está em \anterior. Devolve o que faltar ao lugar (nunca sobrescreve um arquivo que existe).
        /// Devolve os caminhos restaurados, relativos à pasta BepInEx.
        /// </summary>
        public static List<string> RecuperarTrocaInterrompida(string raizBepInEx)
        {
            var restaurados = new List<string>();
            string anterior = Path.Combine(PastaDeTrabalho(raizBepInEx), "anterior");
            if (!Directory.Exists(anterior)) return restaurados;
            foreach (string guardado in Directory.GetFiles(anterior, "*", SearchOption.AllDirectories))
            {
                string rel = guardado.Substring(anterior.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                if (!Manifesto.DestinoPermitido(rel)) continue;
                string alvo = Caminho(raizBepInEx, rel);
                if (File.Exists(alvo)) continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(alvo));
                    File.Copy(guardado, alvo);
                    restaurados.Add(rel);
                }
                catch (Exception)
                {
                    // segue com o resto; a busca da versão nova ainda pode reparar
                }
            }
            return restaurados;
        }

        private static string Caminho(string raiz, string destino) => Path.Combine(raiz, destino.Replace('/', Path.DirectorySeparatorChar));

        private static void ApagarSemFalhar(string pasta)
        {
            try { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
            catch (Exception) { /* sobra de download; a próxima atualização limpa */ }
        }
    }
}
