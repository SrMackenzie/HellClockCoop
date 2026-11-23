using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Jogadores bloqueados (0.23.0, etapa 3b da lista de salas). Lista só deste PC, em BepInEx/HellClockCoop/bloqueados.txt:
    /// uma linha por jogador, "SteamID64[TAB]último nome". Nunca vai para métricas, log ou rede (o log cita só o nome).
    /// Quem está bloqueado some da aba Salas; o host o expulsa sempre que ele entra; o convidado só recebe um aviso.
    /// O bloqueio da própria Steam (Ignorado) também conta, mas fica fora deste arquivo (SessaoCoop.EstaBloqueado).
    /// As regras puras (Ler, Formatar, IdValido) não tocam Unity, Steam nem disco: testadas no tools/codectest.
    /// </summary>
    internal static class Bloqueados
    {
        /// <summary>Teto de jogadores na lista (o arquivo é editável à mão; o resto é ignorado ao ler).</summary>
        public const int Maximo = 500;

        private const string Cabecalho =
            "# Hell Clock Coop - jogadores bloqueados (só neste PC; nunca é enviado).\n" +
            "# Uma linha por jogador: SteamID64, TAB e o último nome visto. Desbloqueie pelo menu do coop (aba Ajustes).\n";

        // Ordem de inserção, para a aba Ajustes mostrar na ordem em que foram bloqueados.
        private static readonly List<KeyValuePair<ulong, string>> Lista = new List<KeyValuePair<ulong, string>>();
        private static bool _carregado;

        private static string Arquivo => Path.Combine(Paths.BepInExRootPath, "HellClockCoop", "bloqueados.txt");

        // ---------- Regras puras ----------

        /// <summary>SteamID64 de conta individual (universo público 1, tipo 1): o resto não é jogador.</summary>
        public static bool IdValido(ulong id) => (id >> 56) == 1ul && ((id >> 52) & 0xFul) == 1ul && (id & 0xFFFFFFFFul) != 0ul;

        /// <summary>Lê o texto do arquivo: ignora comentários, linhas ruins e repetidas; no máximo Maximo jogadores.</summary>
        public static List<KeyValuePair<ulong, string>> Ler(string texto)
        {
            var r = new List<KeyValuePair<ulong, string>>();
            if (string.IsNullOrEmpty(texto)) return r;
            var vistos = new HashSet<ulong>();
            foreach (string bruta in texto.Split('\n'))
            {
                string linha = bruta.Trim();
                if (linha.Length == 0 || linha[0] == '#') continue;
                int tab = linha.IndexOf('\t');
                string id = (tab < 0 ? linha : linha.Substring(0, tab)).Trim();
                if (id.Length == 0 || id.Length > 20 || !ulong.TryParse(id, out ulong v) || !IdValido(v) || !vistos.Add(v)) continue;
                string nome = Salas.LimparNome(tab < 0 ? null : linha.Substring(tab + 1));
                r.Add(new KeyValuePair<ulong, string>(v, nome));
                if (r.Count >= Maximo) break;
            }
            return r;
        }

        /// <summary>Texto do arquivo: cabeçalho e uma linha por jogador (o nome já sai limpo, sem TAB nem quebra).</summary>
        public static string Formatar(List<KeyValuePair<ulong, string>> lista)
        {
            var sb = new StringBuilder(Cabecalho);
            if (lista == null) return sb.ToString();
            foreach (KeyValuePair<ulong, string> kv in lista) sb.Append(kv.Key).Append('\t').Append(Salas.LimparNome(kv.Value)).Append('\n');
            return sb.ToString();
        }

        // ---------- Lista deste PC ----------

        public static int Quantidade
        {
            get { Carregar(); return Lista.Count; }
        }

        /// <summary>Jogador i da lista (ordem em que foi bloqueado).</summary>
        public static KeyValuePair<ulong, string> Item(int i)
        {
            Carregar();
            return Lista[i];
        }

        public static bool Contem(ulong id)
        {
            Carregar();
            return IndiceDe(id) >= 0;
        }

        /// <summary>Põe na lista (ou atualiza o nome) e grava. False se o id não é de jogador ou a lista está cheia.</summary>
        public static bool Bloquear(ulong id, string nome)
        {
            Carregar();
            if (!IdValido(id)) return false;
            int i = IndiceDe(id);
            if (i >= 0) Lista[i] = new KeyValuePair<ulong, string>(id, Salas.LimparNome(nome));
            else if (Lista.Count >= Maximo) return false;
            else Lista.Add(new KeyValuePair<ulong, string>(id, Salas.LimparNome(nome)));
            Gravar();
            return true;
        }

        public static bool Desbloquear(ulong id)
        {
            Carregar();
            int i = IndiceDe(id);
            if (i < 0) return false;
            Lista.RemoveAt(i);
            Gravar();
            return true;
        }

        private static int IndiceDe(ulong id)
        {
            for (int i = 0; i < Lista.Count; i++) if (Lista[i].Key == id) return i;
            return -1;
        }

        /// <summary>Arquivo maior que isto (editado à mão) não é lido: 500 linhas cabem com folga.</summary>
        private const long TamanhoMaximo = 64 * 1024;

        // Leitura falhou (arquivo travado por outro programa, grande demais...): não grava por cima, para não perder a lista.
        private static bool _leituraFalhou;

        private static string Temporario => Arquivo + ".tmp";

        private static void Carregar()
        {
            if (_carregado) return;
            _carregado = true;
            try
            {
                // Uma troca interrompida (queda entre apagar e renomear) deixa só o .tmp: ele é a lista mais nova.
                string origem = File.Exists(Arquivo) ? Arquivo : File.Exists(Temporario) ? Temporario : null;
                if (origem != null)
                {
                    if (new FileInfo(origem).Length > TamanhoMaximo)
                    {
                        _leituraFalhou = true;
                        Plugin.Log.LogWarning("[Bloqueio] bloqueados.txt é grande demais; fica sem uso nesta sessão (nada é gravado por cima).");
                        return;
                    }
                    Lista.AddRange(Ler(File.ReadAllText(origem, Encoding.UTF8)));
                }
                if (Lista.Count > 0) Plugin.Log.LogInfo($"[Bloqueio] {Lista.Count} jogador(es) bloqueado(s) neste PC.");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _leituraFalhou = true;
                Plugin.Log.LogWarning($"[Bloqueio] Não foi possível ler bloqueados.txt ({e.GetType().Name}); bloqueios novos valem só até fechar o jogo.");
            }
        }

        /// <summary>Grava num temporário e troca, para uma queda no meio não apagar a lista (o Carregar recupera o .tmp).</summary>
        private static void Gravar()
        {
            if (_leituraFalhou) return; // o arquivo do disco tem bloqueios que não estão na memória
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                File.WriteAllText(Temporario, Formatar(Lista), new UTF8Encoding(false));
                if (File.Exists(Arquivo)) File.Delete(Arquivo);
                File.Move(Temporario, Arquivo);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Plugin.Log.LogWarning($"[Bloqueio] Não foi possível gravar bloqueados.txt ({e.GetType().Name}); vale só até fechar o jogo.");
            }
        }
    }
}
