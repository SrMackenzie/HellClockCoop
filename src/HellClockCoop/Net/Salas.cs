using System.Globalization;
using System.Text;

namespace HellClockCoop.Net
{
    /// <summary>Uma sala pública da aba Salas, com os dados do lobby já filtrados (Salas.Ler*).</summary>
    internal sealed class SalaListada
    {
        public ulong Lobby;
        public string Host;
        public string Nome;             // 0.25.0: nome da sala (hcc_sala), já limpo; padrão "Sala de <host>"
        public string Lugar;           // LugarMenu, LugarHub ou LugarRun
        public int Ato, Andar, Nivel;   // 0 = desconhecido
        public int Jogadores, Vagas;
        public int Dungeon;             // DungeonConfig.HashID da run do host (0.19.0); 0 = fora da run ou desconhecido
        public long Recorde = -1;       // nosso recorde de andar nessa masmorra (maxFloorReached); -1 = sem save carregado
        public bool CursedWar;          // 0.21.0: o host anuncia a DLC Cursed War (hcc_dlc); só marca na lista
        public bool SemDlc;             // 0.21.0: sala no Ato 4 e este PC sem a Cursed War (SessaoCoop.AtualizarTrava)
        public ulong Dono;              // 0.23.0: SteamID64 que o host anuncia (hcc_dono), para bloquear pela lista; 0 = desconhecido
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
            string r = LimparTexto(s);
            return r.Length == 0 ? "?" : r;
        }

        /// <summary>
        /// Caractere que pode ir para a tela num nome: fora controle, formatação invisível, &lt; &gt; (rich text), \ (o
        /// TextMeshPro lê "\n" e "‮" escritos como texto) e os separadores U+2028/U+2029 (quebram linha no TMP).
        /// 0.25.0: o \ e os separadores entraram (forjavam uma linha "Host ..." falsa); vale também para a digitação.
        /// </summary>
        public static bool CaractereAceito(char c)
        {
            if (char.IsControl(c) || c == '<' || c == '>' || c == '\\') return false;
            UnicodeCategory cat = char.GetUnicodeCategory(c);
            return cat != UnicodeCategory.Format && cat != UnicodeCategory.LineSeparator && cat != UnicodeCategory.ParagraphSeparator;
        }

        /// <summary>A limpeza do LimparNome, mas vazio continua vazio (nome da sala: vazio = padrão).</summary>
        public static string LimparTexto(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (!CaractereAceito(c)) continue;
                sb.Append(c);
                if (sb.Length >= NomeMaximo) break;
            }
            if (sb.Length > 0 && char.IsHighSurrogate(sb[sb.Length - 1])) sb.Length--; // corte no meio de um par substituto
            return sb.ToString().Trim();
        }

        // ---------- Nome da sala (0.25.0) ----------

        /// <summary>"Sala de &lt;nome na Steam&gt;", limpo e no limite de 32.</summary>
        public static string NomePadrao(string persona) => LimparNome("Sala de " + LimparNome(persona));

        /// <summary>Nome digitado pelo host: limpo; vazio (ou só lixo) vira null, que quer dizer "use o padrão".</summary>
        public static string NomeDigitado(string s)
        {
            string r = LimparTexto(s);
            return r.Length == 0 ? null : r;
        }

        /// <summary>Nome da sala lido do lobby (não confiável): limpo; vazio cai no padrão com o nome do host.</summary>
        public static string NomeDaSalaListada(string doLobby, string host) => NomeDigitado(doLobby) ?? NomePadrao(host);

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

        /// <summary>Linha 1 da sala: o nome da sala (0.25.0; antes, o host) e a marca da Cursed War.</summary>
        public static string TextoSala(SalaListada s)
        {
            string nome = s.Nome ?? NomePadrao(s.Host);
            return s.CursedWar ? nome + "  ·  Cursed War" : nome;
        }

        /// <summary>Linha 2 da sala: o host (0.25.0), o lugar e, se indisponível, o motivo (DLC antes do recorde).</summary>
        public static string DetalheSala(SalaListada s)
        {
            string lugar = "Host " + s.Host + "  ·  " + TextoLugar(s.Lugar, s.Ato, s.Andar);
            if (s.SemDlc) return lugar + "  ·  " + TextoSemDlc;
            return SalaTravada(s) ? lugar + "  ·  " + TextoRecorde(s.Recorde) : lugar;
        }

        /// <summary>À direita: "Nv 34  2/4", ou "indisponível" (o motivo vai na linha 2).</summary>
        public static string ValorSala(SalaListada s) =>
            SalaIndisponivel(s) ? "indisponível" : (s.Nivel > 0 ? "Nv " + s.Nivel + "  " : "") + s.Jogadores + "/" + s.Vagas;

        /// <summary>SteamID do dono anunciado no lobby (0.23.0): só conta de jogador (Bloqueados.IdValido); o resto vira 0.</summary>
        public static ulong LerSteamId(string s) =>
            !string.IsNullOrEmpty(s) && s.Length <= 20 && ulong.TryParse(s, out ulong v) && Bloqueados.IdValido(v) ? v : 0ul;

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
        /// A sala aparece na lista: mesma versão do mod e do protocolo, com vaga e que não seja a nossa nem uma de onde fomos
        /// expulsos. A Steam já filtra; aqui confere de novo. Desde a 0.21.0 a DLC não esconde a sala (só o Ato 4 trava).
        /// </summary>
        public static bool SalaVisivel(string mod, string proto, string meuMod, string meuProto,
                                       int jogadores, int vagas, bool nossa, bool expulso) =>
            !nossa && !expulso && mod == meuMod && proto == meuProto && vagas > 0 && jogadores > 0 && jogadores < vagas;

        // ---------- DLC Cursed War (0.21.0) ----------

        /// <summary>
        /// Ato da Cursed War: no IL da 2.1.1.1 a DLC libera o Ato 4 (GameConfig.MaxActEnabled = 4; EDungeon Act04, Act04Endgame,
        /// Act04Abyss, Act04Oblivion e Act04Void). O resto (atos 1 a 3, Ascensão) é jogo base e não trava ninguém.
        /// </summary>
        public const int AtoDaDlc = 4;

        public const string TextoSemDlc = "precisa da DLC Cursed War";

        /// <summary>A run é do Ato 4 e este PC não tem a DLC: não entra pela lista nem acompanha o host.</summary>
        public static bool PrecisaDaDlc(bool atoDaDlc, bool minhaCursedWar) => atoDaDlc && !minhaCursedWar;

        /// <summary>Indisponível na lista: falta a DLC ou o save não chegou lá.</summary>
        public static bool SalaIndisponivel(SalaListada s) => s != null && (s.SemDlc || SalaTravada(s));

        /// <summary>Aviso do convidado sem a DLC quando o host vai para o Ato 4.</summary>
        public static string TextoForaDaRunDlc(int ato, int andar) =>
            $"O host foi para uma run da DLC Cursed War ({TextoLugar(LugarRun, ato, andar)}), que você não tem. Você fica no hub e acompanha a próxima run do jogo base.";

        /// <summary>Aviso do host quando um par avisa que não acompanha (ForaDaRun): pela DLC (0.21.0) ou pelo save (0.19.0).</summary>
        public static string TextoParFora(string nome, bool semDlc) => semDlc
            ? $"{nome} não tem a DLC Cursed War: fica no hub, fora da escala do grupo."
            : $"{nome} ainda não chegou aqui na campanha: fica no hub, fora da escala do grupo.";

        /// <summary>Aviso no Hello quando só um dos dois tem a Cursed War.</summary>
        public static string TextoDlcDiferente(string nome, bool eleTem) =>
            $"{nome} {(eleTem ? "tem" : "não tem")} a DLC Cursed War e você {(eleTem ? "não tem" : "tem")}: vocês jogam juntos os atos 1 a 3; o Ato 4 é só para quem tem a DLC.";

        /// <summary>Expulsão só vale vinda do dono do lobby (o host), e nunca de nós mesmos.</summary>
        public static bool ExpulsaoValida(ulong de, ulong dono, ulong eu) => de != 0 && de == dono && de != eu;

        /// <summary>Pode buscar agora: nunca buscou (ultima &lt; 0) ou já passou a espera.</summary>
        public static bool PodeBuscar(float agora, float ultima) => ultima < 0f || agora - ultima >= EsperaEntreBuscas;
    }
}
