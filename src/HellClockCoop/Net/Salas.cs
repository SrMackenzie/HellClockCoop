using System.Globalization;
using System.Text;

namespace HellClockCoop.Net
{
    /// <summary>Uma sala pública da aba Salas, com os dados do lobby já filtrados (Salas.Ler*).</summary>
    internal sealed class SalaListada
    {
        public ulong Lobby;
        public string Host;
        public string Lugar;            // LugarMenu, LugarHub ou LugarRun
        public int Ato, Andar, Nivel;   // 0 = desconhecido
        public int Jogadores, Vagas;
        public int Dungeon;             // DungeonConfig.HashID da run do host (0.19.0); 0 = fora da run ou desconhecido
        public long Recorde = -1;       // nosso recorde de andar nessa masmorra (maxFloorReached); -1 = sem save carregado
    }

    /// <summary>
    /// Regras puras da lista de salas (0.18.0): leitura dos dados do lobby, que vêm de qualquer jogador do mundo (não
    /// confiáveis), texto das linhas e validação da expulsão. Sem Unity nem Steam: testadas no tools/codectest.
    /// </summary>
    internal static class Salas
    {
        public const string LugarMenu = "menu", LugarHub = "hub", LugarRun = "run";
        public const int NomeMaximo = 32, AtoMaximo = 20, AndarMaximo = 999, NivelMaximo = 999;

        /// <summary>Intervalo mínimo entre duas buscas (o botão "Atualizar lista" não martela a Steam).</summary>
        public const float EsperaEntreBuscas = 3f;

        /// <summary>Lista mais velha que isto é buscada de novo ao abrir a aba.</summary>
        public const float ListaVelhaApos = 30f;

        /// <summary>
        /// Nome vindo da Steam ou do lobby: sem caracteres de controle, sem formatação invisível (U+202E inverte o texto,
        /// U+200B...), sem &lt; &gt; (rich text) e cortado sem partir emoji.
        /// </summary>
        public static string LimparNome(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsControl(c) || c == '<' || c == '>' || char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
                sb.Append(c);
                if (sb.Length >= NomeMaximo) break;
            }
            if (sb.Length > 0 && char.IsHighSurrogate(sb[sb.Length - 1])) sb.Length--; // corte no meio de um par substituto
            string r = sb.ToString().Trim();
            return r.Length == 0 ? "?" : r;
        }

        /// <summary>Inteiro do lobby dentro da faixa; qualquer outra coisa vira 0 (desconhecido).</summary>
        public static int LerInteiro(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 6 || !int.TryParse(s, out int v)) return 0;
            return v >= 0 && v <= max ? v : 0;
        }

        /// <summary>Lugar do lobby: só os três valores conhecidos; o resto vira "" (desconhecido).</summary>
        public static string LerLugar(string s) => s == LugarMenu || s == LugarHub || s == LugarRun ? s : "";

        /// <summary>Número do ato pelo nome do DungeonConfig ("Act02_DungeonConfig" → 2); 0 se o nome não segue o padrão.</summary>
        public static int AtoDoNome(string nome)
        {
            if (string.IsNullOrEmpty(nome) || nome.Length < 4) return 0;
            if (char.ToLowerInvariant(nome[0]) != 'a' || char.ToLowerInvariant(nome[1]) != 'c' || char.ToLowerInvariant(nome[2]) != 't') return 0;
            int v = 0, i = 3;
            for (; i < nome.Length && i < 6 && nome[i] >= '0' && nome[i] <= '9'; i++) v = v * 10 + (nome[i] - '0');
            return i > 3 && v <= AtoMaximo ? v : 0;
        }

        public static string TextoLugar(string lugar, int ato, int andar)
        {
            switch (lugar)
            {
                case LugarMenu: return "no menu";
                case LugarHub: return "no hub";
                case LugarRun:
                    if (ato > 0 && andar > 0) return $"Ato {ato}, andar {andar}";
                    return andar > 0 ? $"em run, andar {andar}" : "em run";
                default: return "lugar desconhecido";
            }
        }

        /// <summary>Linha da aba Salas: "Host  -  lugar" à esquerda e "Nv 34  2/4" à direita.</summary>
        public static string TextoSala(SalaListada s) => s.Host + "  -  " + TextoLugar(s.Lugar, s.Ato, s.Andar);

        public static string ValorSala(SalaListada s) =>
            SalaTravada(s) ? TextoRecorde(s.Recorde) : (s.Nivel > 0 ? "Nv " + s.Nivel + "  " : "") + s.Jogadores + "/" + s.Vagas;

        /// <summary>Hash do DungeonConfig vindo do lobby: qualquer int (pode ser negativo); lixo ou vazio vira 0.</summary>
        public static int LerHash(string s) => !string.IsNullOrEmpty(s) && s.Length <= 11 && int.TryParse(s, out int v) ? v : 0;

        /// <summary>
        /// Trava de progresso (0.19.0): o recorde do nosso save na masmorra do host (maxFloorReached, que o jogo conta a partir
        /// de 1, como o andar do lobby) alcança o andar dele. Andar 0 (fora da run) não trava; recorde -1 (sem leitura) trava.
        /// </summary>
        public static bool Liberado(int andarDoHost, long recorde) => andarDoHost <= 0 || recorde >= andarDoHost;

        /// <summary>A sala está numa run aonde o nosso save não chegou. Sem save carregado (menu) ou sem masmorra, não trava.</summary>
        public static bool SalaTravada(SalaListada s) =>
            s != null && s.Lugar == LugarRun && s.Dungeon != 0 && s.Recorde >= 0 && !Liberado(s.Andar, s.Recorde);

        public static string TextoRecorde(long recorde) => recorde <= 0 ? "você não começou essa masmorra" : "você chegou ao andar " + recorde;

        /// <summary>Aviso do convidado que não acompanha a run do host.</summary>
        public static string TextoForaDaRun(int ato, int andar, long recorde) =>
            $"O seu save não chegou aonde o host está ({TextoLugar(LugarRun, ato, andar)}; {TextoRecorde(recorde)}). " +
            "Você fica no hub e acompanha a próxima run liberada.";

        /// <summary>
        /// A sala aparece na lista: mesma versão do mod e do protocolo, mesma DLC Cursed War (a run só é dividida assim),
        /// com vaga e que não seja a nossa nem uma de onde fomos expulsos. A Steam já filtra; aqui confere de novo.
        /// </summary>
        public static bool SalaVisivel(string mod, string proto, string dlc, string meuMod, string meuProto, bool minhaCursedWar,
                                       int jogadores, int vagas, bool nossa, bool expulso) =>
            !nossa && !expulso && mod == meuMod && proto == meuProto && dlc == (minhaCursedWar ? "1" : "0")
            && vagas > 0 && jogadores > 0 && jogadores < vagas;

        /// <summary>Expulsão só vale vinda do dono do lobby (o host), e nunca de nós mesmos.</summary>
        public static bool ExpulsaoValida(ulong de, ulong dono, ulong eu) => de != 0 && de == dono && de != eu;

        /// <summary>Pode buscar agora: nunca buscou (ultima &lt; 0) ou já passou a espera.</summary>
        public static bool PodeBuscar(float agora, float ultima) => ultima < 0f || agora - ultima >= EsperaEntreBuscas;
    }
}
