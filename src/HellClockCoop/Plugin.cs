// Hell Clock Coop — mod de fã, sem fins lucrativos.
// Fase 0: plugin "hello world" que prova o carregamento pelo BepInEx 5,
// faz backup do save, checa a versão do jogo e valida os alvos de patch.

using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HellClockCoop
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Hell Clock.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "br.srmackenzie.hellclockcoop";
        public const string PluginName = "Hell Clock Coop";
        public const string PluginVersion = "0.9.1";

        /// <summary>Versão do jogo contra a qual os patches foram mapeados (Application.version).</summary>
        public const string VersaoMapeada = "2.1.1.1";

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;

        private ConfigEntry<bool> _fazerBackupDoSave;
        private ConfigEntry<int> _backupsMantidos;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} {PluginVersion} carregado. Olá, Pajeú!");
            LogDaUnity.Ativar(); // erros da Unity também no LogOutput.log (o listener do BepInEx não funciona aqui)
            MigrarConfigAntiga(); // antes de qualquer Config.Bind

            _fazerBackupDoSave = Config.Bind("Seguranca", "BackupDoSaveAoIniciar", true,
                "Copia a pasta de save inteira antes de o jogo carregar qualquer coisa.");
            _backupsMantidos = Config.Bind("Seguranca", "BackupsMantidos", 15,
                "Quantos backups antigos manter; os mais velhos são apagados.");
            // Quem lê esta opção é o atualizador (BepInEx\patchers\HellClockCoop.Atualizador.dll), antes do plugin.
            Config.Bind("Atualizacao", "VerificarAoIniciar", true,
                "Ao abrir o jogo, procura a versão nova do coop no GitHub (até 5 s) e instala sozinha, conferindo a assinatura. " +
                "Com false, o mod fica na versão instalada.");
            AplicarConsole(Config.Bind("Diagnostico", "MostrarConsole", false,
                "Abre a janela preta de console do BepInEx junto com o jogo (útil para acompanhar o log ao vivo). " +
                "O console abre antes do mod carregar, então a mudança só vale a partir da próxima abertura do jogo. " +
                "O log continua gravado em BepInEx\\LogOutput.log nos dois casos.").Value);

            if (_fazerBackupDoSave.Value)
            {
                SaveBackup.Executar(_backupsMantidos.Value);
            }

            ChecarVersao();

            // A validação roda antes de aplicar qualquer patch: se um alvo sumiu numa
            // atualização do jogo, o log mostra exatamente qual.
            int faltando = PatchTargets.ValidarTodos();

            HarmonyInstance = new Harmony(PluginGuid);
            if (faltando == 0)
            {
                // Classe a classe: se um patch quebrar numa atualização do jogo, só ele cai (com o nome no log).
                int ok = 0, falhas = 0;
                foreach (System.Type t in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
                {
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                    try
                    {
                        HarmonyInstance.CreateClassProcessor(t).Patch();
                        ok++;
                    }
                    catch (System.Exception e)
                    {
                        falhas++;
                        Log.LogError($"[Patch] Falha em {t.Name}: {(e.InnerException ?? e).Message}");
                    }
                }
                Log.LogInfo($"Patches das Fases 0–3: {ok} aplicados, {falhas} com falha.");
            }
            else
            {
                Log.LogError($"{faltando} alvo(s) de patch não encontrados. Nenhum patch foi aplicado; o jogo roda sem modificações.");
            }

            // Scripts de estado do Animator (StateMachineBehaviour) não rodam nos clones de aliados.
            int guardas = Avatar.AvatarAnimatorGuard.Aplicar(HarmonyInstance);
            Log.LogInfo($"Guarda de animação dos avatares: {guardas} método(s).");

            // Fase 1: laço do mod (coop pela Steam, modo eco, diagnóstico). Não depende de patch:
            // só lê o PlayerController local e usa o Steam que o próprio jogo inicializa.
            CoopRuntime.Criar(Config);
            Log.LogInfo($"Fase 1 pronta: {CoopRuntime.TeclaConvidar.Value} cria/convida, {CoopRuntime.TeclaSair.Value} sai, {CoopRuntime.TeclaEco.Value} modo eco.");
        }

        /// <summary>
        /// O GUID mudou na 0.7.2, e com ele o nome do arquivo de configuração. Se ainda não existe o arquivo do
        /// GUID atual, copia o de um GUID anterior (outro "*.hellclockcoop.cfg" da pasta) para ninguém perder os
        /// ajustes. O arquivo antigo fica onde está.
        /// </summary>
        private void MigrarConfigAntiga()
        {
            try
            {
                string atual = Config.ConfigFilePath;
                if (File.Exists(atual)) return;
                foreach (string antigo in Directory.GetFiles(Paths.ConfigPath, "*.hellclockcoop.cfg"))
                {
                    if (string.Equals(Path.GetFullPath(antigo), Path.GetFullPath(atual), StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(antigo, atual);
                    Config.Reload();
                    Log.LogInfo($"Configuração de {Path.GetFileName(antigo)} copiada para {Path.GetFileName(atual)}.");
                    return;
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"Não deu para copiar a configuração antiga: {e.Message}. Valem os valores padrão.");
            }
        }

        /// <summary>
        /// Leva a opção [Diagnostico] MostrarConsole para o [Logging.Console] Enabled do BepInEx.cfg. O console abre
        /// antes do plugin, então vale na próxima abertura. A entrada é a do próprio BepInEx (ConsoleManager é
        /// interno; o campo é público), e o arquivo é gravado pelo CoreConfig, que preserva os comentários.
        /// O atualizador não troca o BepInEx.cfg; por isso a opção vive no plugin, e não só no pacote.
        /// </summary>
        private static void AplicarConsole(bool mostrar)
        {
            try
            {
                Type gerente = typeof(Paths).Assembly.GetType("BepInEx.ConsoleManager");
                var entrada = gerente?.GetField("ConfigConsoleEnabled")?.GetValue(null) as ConfigEntry<bool>;
                if (entrada == null)
                {
                    Log.LogWarning("Console do BepInEx: opção [Logging.Console] Enabled não encontrada; MostrarConsole ignorado.");
                    return;
                }
                if (entrada.Value == mostrar) return;
                entrada.Value = mostrar;
                entrada.ConfigFile.Save(); // grava o BepInEx.cfg mesmo se o CoreConfig não salvar sozinho
                Log.LogInfo($"Console do BepInEx {(mostrar ? "ligado" : "desligado")} a partir da próxima abertura do jogo.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Console do BepInEx: não deu para gravar a opção ({e.Message}).");
            }
        }

        private static void ChecarVersao()
        {
            string versao = Application.version;
            Log.LogInfo($"Versão do jogo: {versao} | Unity {Application.unityVersion} | Save em: {Application.persistentDataPath}");
            if (versao != VersaoMapeada)
            {
                Log.LogWarning($"O mod foi mapeado para a versão {VersaoMapeada}, mas o jogo está na {versao}. Os patches podem ter quebrado; confira a validação abaixo.");
            }
        }

        private void OnDestroy()
        {
            HarmonyInstance?.UnpatchSelf();
        }
    }

    /// <summary>Backup completo da pasta de save (regra do projeto: backup antes de qualquer teste).</summary>
    internal static class SaveBackup
    {
        public static void Executar(int manter)
        {
            try
            {
                string origem = Application.persistentDataPath;
                if (!Directory.Exists(origem))
                {
                    Plugin.Log.LogWarning($"Pasta de save não encontrada ({origem}); nada para copiar.");
                    return;
                }

                string raiz = Path.Combine(Paths.BepInExRootPath, "HellClockCoop", "save-backups");
                string destino = Path.Combine(raiz, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                int arquivos = CopiarPasta(origem, destino);
                Plugin.Log.LogInfo($"Backup do save: {arquivos} arquivo(s) copiados para {destino}");

                LimparAntigos(raiz, manter);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Falha no backup do save: {e}");
            }
        }

        private static int CopiarPasta(string origem, string destino)
        {
            Directory.CreateDirectory(destino);
            int total = 0;
            foreach (string arquivo in Directory.GetFiles(origem))
            {
                File.Copy(arquivo, Path.Combine(destino, Path.GetFileName(arquivo)), overwrite: true);
                total++;
            }
            foreach (string sub in Directory.GetDirectories(origem))
            {
                total += CopiarPasta(sub, Path.Combine(destino, Path.GetFileName(sub)));
            }
            return total;
        }

        private static void LimparAntigos(string raiz, int manter)
        {
            manter = Math.Max(1, manter); // .cfg com 0 ou negativo apagaria até o backup que acabou de ser feito
            string[] pastas = Directory.GetDirectories(raiz);
            Array.Sort(pastas, StringComparer.Ordinal); // nomes por data: ordem alfabética = cronológica
            for (int i = 0; i < pastas.Length - manter; i++)
            {
                Directory.Delete(pastas[i], recursive: true);
            }
        }
    }
}
