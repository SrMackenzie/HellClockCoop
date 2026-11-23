// Hell Clock Coop — atualizador automático (preloader patcher do BepInEx 5), mod de fã, sem fins lucrativos.
// Fica em BepInEx\patchers\HellClockCoop.Atualizador.dll. Não patcheia nada: usa o Initialize() do preloader, que
// roda antes do chainloader, para trocar o plugin enquanto ele ainda não foi carregado.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace HellClockCoop.Atualizador
{
    /// <summary>
    /// Ao abrir o jogo: consulta a última release pública do GitHub, confere a assinatura do manifesto e o SHA-256 de
    /// cada arquivo e troca só BepInEx\plugins\HellClockCoop\ (e este atualizador, que vale a partir da próxima abertura).
    /// Qualquer falha, ou mais de 5 s sem resposta: o jogo segue com a versão instalada. Desliga em
    /// [Atualizacao] VerificarAoIniciar = false, no .cfg do mod.
    /// </summary>
    public static class Atualizador
    {
        /// <summary>Igual ao PluginVersion do plugin (o empacotar.py confere).</summary>
        public const string Versao = "0.25.0";

        private const string Repositorio = "SrMackenzie/HellClockCoop";
        private const string ArquivoDeConfig = "br.srmackenzie.hellclockcoop.cfg";
        private const int PrazoMs = 5000;

        private static ManualLogSource _log;

        // --- contrato de patcher do BepInEx 5: sem DLL alvo, Patch nunca é chamado ---
        public static IEnumerable<string> TargetDLLs => new string[0];
        public static void Patch(AssemblyDefinition assembly) { }

        public static void Initialize()
        {
            _log = Logger.CreateLogSource("Atualizador");
            try
            {
                Executar();
            }
            catch (Exception e)
            {
                _log.LogError($"Erro inesperado; o jogo segue com a versão instalada. {e}");
            }
        }

        private static void Executar()
        {
            if (!VerificarAoIniciar())
            {
                _log.LogInfo("Verificação desligada ([Atualizacao] VerificarAoIniciar = false).");
                return;
            }
            string raiz = Paths.BepInExRootPath;
            // Jogo fechado no meio de uma troca anterior: põe de volta o que faltar antes de tudo.
            foreach (string restaurado in Instalacao.RecuperarTrocaInterrompida(raiz))
                _log.LogWarning($"Troca anterior interrompida: {restaurado} restaurado da versão guardada.");

            string plugin = Path.Combine(Path.Combine(Paths.PluginPath, "HellClockCoop"), "HellClockCoop.dll");
            if (!File.Exists(plugin))
            {
                _log.LogInfo("Plugin do coop não está instalado; nada para atualizar.");
                return;
            }
            string instalada = VersaoDoPlugin(plugin);
            if (instalada == null || !Version.TryParse(instalada, out _))
            {
                _log.LogWarning($"Não deu para ler a versão do plugin instalado; vale a do atualizador ({Versao}) como mínima.");
                instalada = null;
            }
            string mostrada = instalada ?? "instalada";

            _log.LogInfo($"Procurando versão nova em github.com/{Repositorio} (instalada: {instalada ?? "?"}, prazo {PrazoMs / 1000} s)...");
            var busca = new Busca(raiz, instalada);
            var thread = new Thread(busca.Rodar) { IsBackground = true, Name = "HellClockCoop.Atualizador" };
            thread.Start();
            if (!thread.Join(PrazoMs + 1000))
            {
                busca.Abandonada = true; // a thread não escreve em disco; nada do que ela trouxer depois é usado
                _log.LogWarning($"Sem resposta em {PrazoMs / 1000} s; o jogo segue com a versão {mostrada}.");
                return;
            }

            if (busca.Erro != null)
            {
                _log.LogWarning($"Não deu para verificar: {busca.Erro}. O jogo segue com a versão {mostrada}.");
                if (busca.AvisarNoPainel) GravarAviso($"Atualização do coop recusada ({busca.Erro}); segue a versão {mostrada}.");
                return;
            }
            if (busca.Manifesto == null)
            {
                _log.LogInfo(busca.Situacao);
                return;
            }

            string versaoNova = busca.Manifesto.VersaoTexto;
            if (Instalacao.Aplicar(raiz, busca.Manifesto, busca.Conteudo, VersaoDoPlugin, out string erro))
            {
                _log.LogInfo($"Atualizado da {mostrada} para a {versaoNova} ({busca.Conteudo.Count} arquivo(s)). Os arquivos substituídos ficaram em {Instalacao.PastaDeTrabalho(raiz)}\\anterior.");
                bool soAtualizador = busca.Conteudo.Count == 1 && busca.Conteudo.ContainsKey(Manifesto.DestinoAtualizador);
                if (soAtualizador) _log.LogInfo("Só o atualizador mudou; ele vale a partir da próxima abertura.");
                else GravarAviso($"Coop atualizado da versão {mostrada} para a {versaoNova}.");
            }
            else
            {
                _log.LogError($"A atualização para a {versaoNova} falhou e foi desfeita: {erro}. O jogo segue com a versão {mostrada}.");
                GravarAviso($"A atualização do coop para a {versaoNova} falhou; segue a versão {mostrada}. Detalhes no LogOutput.log.");
            }
        }

        /// <summary>Lê o .cfg do mod sem criá-lo (quem cria e migra o arquivo é o plugin). Padrão: ligado.</summary>
        private static bool VerificarAoIniciar()
        {
            string cfg = Path.Combine(Paths.ConfigPath, ArquivoDeConfig);
            if (!File.Exists(cfg)) return true;
            bool naSecao = false;
            foreach (string bruta in File.ReadAllLines(cfg))
            {
                string linha = bruta.Trim();
                if (linha.StartsWith("[", StringComparison.Ordinal)) { naSecao = linha == "[Atualizacao]"; continue; }
                if (!naSecao || linha.StartsWith("#", StringComparison.Ordinal)) continue;
                int igual = linha.IndexOf('=');
                if (igual > 0 && linha.Substring(0, igual).Trim() == "VerificarAoIniciar")
                    return !string.Equals(linha.Substring(igual + 1).Trim(), "false", StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        /// <summary>Versão do [BepInPlugin] da DLL, lida pelo Mono.Cecil sem carregar a DLL. Null se ilegível.</summary>
        internal static string VersaoDoPlugin(string caminho)
        {
            try
            {
                using (var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(caminho))))
                {
                    foreach (TypeDefinition t in asm.MainModule.Types)
                    {
                        foreach (CustomAttribute ca in t.CustomAttributes)
                        {
                            if (ca.AttributeType.FullName == "BepInEx.BepInPlugin" && ca.ConstructorArguments.Count == 3)
                                return ca.ConstructorArguments[2].Value as string;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // DLL corrompida ou não é .NET: trata como ilegível
            }
            return null;
        }

        /// <summary>Mensagem de uma linha que o plugin mostra no painel do coop ao abrir (e depois apaga).</summary>
        private static void GravarAviso(string texto)
        {
            try
            {
                string pasta = Instalacao.PastaDeTrabalho(Paths.BepInExRootPath);
                Directory.CreateDirectory(pasta);
                File.WriteAllText(Path.Combine(pasta, "aviso.txt"), texto, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                _log.LogWarning($"Não deu para gravar o aviso do painel: {e.Message}");
            }
        }

        /// <summary>Trabalho de rede, numa thread: tudo vai para a memória. Quem mexe em arquivo é a thread do preloader.</summary>
        private sealed class Busca
        {
            private readonly string _raiz;
            private readonly string _instalada;
            private readonly Stopwatch _relogio = new Stopwatch();

            public volatile bool Abandonada;
            public string Erro;
            public bool AvisarNoPainel;   // falha de segurança (assinatura/hash), não de rede
            public string Situacao;
            public Manifesto Manifesto;   // não nulo = há versão nova, com o conteúdo baixado e conferido
            public readonly Dictionary<string, byte[]> Conteudo = new Dictionary<string, byte[]>();

            public Busca(string raiz, string instalada)
            {
                _raiz = raiz;
                _instalada = instalada;
            }

            public void Rodar()
            {
                _relogio.Start();
                try
                {
                    string baseUltima = $"https://github.com/{Repositorio}/releases/latest/download/";
                    byte[] texto = Baixar(baseUltima + "manifesto.txt", 64 * 1024, out int status);
                    if (texto == null)
                    {
                        if (status == 404) { Erro = null; Situacao = "Nenhuma release com manifesto publicada; nada para atualizar."; }
                        return;
                    }
                    string versaoLida = Manifesto.VersaoSemVerificar(texto);
                    if (versaoLida == null) { Falhou("manifesto sem versão válida", seguranca: true); return; }

                    string baseVersao = $"https://github.com/{Repositorio}/releases/download/v{versaoLida}/";
                    byte[] assinatura = Baixar(baseVersao + "manifesto.txt.sig", 4 * 1024, out _);
                    if (assinatura == null) return;
                    Manifesto m = Manifesto.Ler(texto, assinatura, ChavePublica.Parametros(), out string erro);
                    if (m == null) { Falhou(erro, seguranca: true); return; }
                    if (m.VersaoTexto != versaoLida) { Falhou("versão do manifesto mudou entre leituras", seguranca: true); return; }

                    // Nunca rebaixa. Mesma versão do plugin com o atualizador diferente (troca anterior interrompida):
                    // baixa de novo tudo o que não confere com o manifesto.
                    // Plugin ilegível: a versão deste atualizador é o piso, e a mesma versão serve para reparar.
                    if (_instalada != null)
                    {
                        Version atual = new Version(_instalada);
                        if (m.Versao < atual || (m.Versao == atual && AtualizadorEmDia(m)))
                        {
                            Situacao = $"Em dia: versão {_instalada} (última publicada: {m.VersaoTexto}).";
                            return;
                        }
                    }
                    else if (m.Versao < new Version(Versao))
                    {
                        Situacao = $"Última publicada ({m.VersaoTexto}) é mais antiga que este atualizador ({Versao}); não rebaixa.";
                        return;
                    }

                    foreach (ArquivoDaRelease a in m.Arquivos)
                    {
                        string local = Path.Combine(_raiz, a.Destino.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(local) && Manifesto.Sha256(File.ReadAllBytes(local)) == a.Sha256) continue;
                        byte[] dados = Baixar(baseVersao + a.Nome, (int)a.Tamanho, out _);
                        if (dados == null) return;
                        if (dados.Length != a.Tamanho || Manifesto.Sha256(dados) != a.Sha256)
                        {
                            Falhou($"{a.Nome} baixado não confere com o manifesto", seguranca: true);
                            return;
                        }
                        Conteudo[a.Destino] = dados;
                    }
                    if (!Abandonada) Manifesto = m;
                }
                catch (Exception e)
                {
                    Falhou(e.GetType().Name + ": " + e.Message, seguranca: false);
                }
            }

            private bool AtualizadorEmDia(Manifesto m)
            {
                foreach (ArquivoDaRelease a in m.Arquivos)
                {
                    if (a.Destino != Manifesto.DestinoAtualizador) continue;
                    string local = Path.Combine(_raiz, a.Destino.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(local) && Manifesto.Sha256(File.ReadAllBytes(local)) == a.Sha256;
                }
                return true; // release sem atualizador: nada a completar
            }

            private byte[] Baixar(string url, int limite, out int status)
            {
                status = 0;
                int resta = PrazoMs - (int)_relogio.ElapsedMilliseconds;
                if (resta <= 0 || Abandonada) { Falhou("tempo esgotado", seguranca: false); return null; }
                byte[] dados = WinHttp.Baixar(url, resta, limite, "HellClockCoop-Atualizador/" + Versao, out status, out string erro);
                if (dados == null) Falhou($"{url.Substring(url.LastIndexOf('/') + 1)}: {erro}", seguranca: false);
                return dados;
            }

            private void Falhou(string motivo, bool seguranca)
            {
                if (Erro != null) return;
                Erro = motivo;
                AvisarNoPainel = seguranca;
            }
        }
    }
}
