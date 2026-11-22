using System;
using System.Globalization;
using BepInEx.Configuration;
using HellClock.Character.Enemy;
using HellClock.Manager;
using HellClock.Stats;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Balanceamento
{
    /// <summary>
    /// Escalonamento por número de jogadores ("um pouco mais difícil que o solo", decisão do projeto).
    ///
    /// Usa as alavancas que o próprio jogo usa para dificuldade e Ascensão:
    /// - vida e dano de cada inimigo: modificador multiplicativo na ficha (EnemySheet) dentro de
    ///   EnemyManager.ApplySheetScaling, junto com as outras escalas do jogo (vale no host e nas réplicas);
    /// - quantidade de inimigos: EnemyGlobalModifiers[RegularEnemyQuantity] (lido ao montar o andar e nas ondas);
    /// - tempo ganho no Hell Clock: TimerManager.IncrementTimer no host. Com loot individual, cada jogador pega as
    ///   próprias recompensas de tempo, então o tempo que entra no relógio único cresce com o grupo;
    /// - XP, ouro, Soulstones e moedas de criação (decisão do autor): cada jogador recebe o valor de cada inimigo, igual
    ///   para todos, × fator por número de jogadores. Como a quantidade de inimigos já sobe, o padrão 1/quantidade
    ///   (0,83 / 0,71 / 0,63) deixa o ganho por andar perto do solo. Vale só em run com mais de um jogador e é aplicado
    ///   em cada PC, no próprio ganho (o drop de cada um sai do jogo dele).
    ///
    /// Os valores ficam no .cfg (seção Balanceamento), um por número de jogadores: "2, 3, 4".
    /// </summary>
    internal static class Escalonamento
    {
        private const string Chave = "HellClockCoop.Escalonamento";

        private static ConfigEntry<bool> _ativo;
        private static ConfigEntry<string> _vida, _vidaChefe, _dano, _quantidade, _tempo, _xp, _moedas;
        private static int _jogadoresAplicados = 1;
        private static float _proximaChecagem;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;

        internal static void Configurar(ConfigFile config)
        {
            const string s = "Balanceamento";
            _ativo = config.Bind(s, "Ativo", true, "Ajusta os inimigos e o Hell Clock ao número de jogadores (o host decide; os valores devem ser iguais para todos).");
            _vida = config.Bind(s, "VidaInimigos", "1.7, 2.4, 3.1", "Multiplicador da vida dos inimigos com 2, 3 e 4 jogadores.");
            _vidaChefe = config.Bind(s, "VidaChefes", "1.8, 2.6, 3.4", "Multiplicador da vida dos chefes com 2, 3 e 4 jogadores.");
            _dano = config.Bind(s, "DanoInimigos", "1.1, 1.2, 1.3", "Multiplicador do dano dos inimigos com 2, 3 e 4 jogadores.");
            _quantidade = config.Bind(s, "QuantidadeInimigos", "1.2, 1.4, 1.6", "Multiplicador da quantidade de inimigos por andar e nas ondas, com 2, 3 e 4 jogadores.");
            _tempo = config.Bind(s, "TempoGanho", "0.55, 0.4, 0.32", "Multiplicador do tempo ganho no Hell Clock com 2, 3 e 4 jogadores (cada um pega as próprias recompensas de tempo).");
            _xp = config.Bind(s, "XpGanho", "0.83, 0.71, 0.63", "Multiplicador do XP que cada jogador ganha por inimigo com 2, 3 e 4 jogadores (só em run).");
            _moedas = config.Bind(s, "OuroGanho", "0.83, 0.71, 0.63", "Multiplicador do ouro, das Soulstones e das moedas de criação que cada jogador ganha com 2, 3 e 4 jogadores (só em run; nas moedas é a chance de o drop sair).");
        }

        /// <summary>Jogadores no coop que dividem a run (o local + aliados compatíveis com a mesma DLC).</summary>
        internal static int Jogadores
        {
            get
            {
                if (Sessao == null || !Sessao.EmSessao) return 1;
                int n = 1;
                foreach (Par p in Sessao.Pares.Values) if (p.Compativel && p.DlcOk) n++;
                return Mathf.Clamp(n, 1, 4);
            }
        }

        private static bool Ligado => _ativo == null || _ativo.Value;

        internal static float Fator(ConfigEntry<string> e, int jogadores) => e == null ? 1f : FatorDaLista(e.Value, jogadores);

        /// <summary>Valor da lista "2, 3, 4" do .cfg para o número de jogadores; solo, vazio ou inválido = 1.</summary>
        internal static float FatorDaLista(string texto, int jogadores)
        {
            if (jogadores <= 1 || texto == null) return 1f;
            // "1.7, 2.4, 3.1" ou, com vírgula decimal, "1,7; 2,4; 3,1" (ou separado por /)
            bool outroSeparador = texto.IndexOf(';') >= 0 || texto.IndexOf('/') >= 0;
            string[] partes = outroSeparador
                ? texto.Split(new[] { ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
                : texto.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int i = Mathf.Min(jogadores - 2, partes.Length - 1);
            if (i < 0) return 1f;
            string num = partes[i].Trim().Replace(',', '.');
            return float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v > 0f ? v : 1f;
        }

        internal static float FatorVida(bool chefe) => Ligado ? Fator(chefe ? _vidaChefe : _vida, Jogadores) : 1f;
        internal static float FatorDano => Ligado ? Fator(_dano, Jogadores) : 1f;
        internal static float FatorQuantidade => Ligado ? Fator(_quantidade, Jogadores) : 1f;
        internal static float FatorTempo => Ligado ? Fator(_tempo, Jogadores) : 1f;
        internal static float FatorXp => Ligado ? Fator(_xp, Jogadores) : 1f;
        internal static float FatorOuro => Ligado ? Fator(_moedas, Jogadores) : 1f;

        /// <summary>
        /// valor × fator com arredondamento sorteado: a parte fracionária vira a chance de +1 (sorteio em [0, 1)).
        /// Assim a média fica exata, e um ganho pequeno (1 moeda, 3 de XP) não some nem fica inteiro sempre.
        /// </summary>
        internal static double Escalar(double valor, float fator, double sorteio)
        {
            if (!(valor > 0) || Mathf.Approximately(fator, 1f)) return valor;
            double v = valor * fator;
            double inteiro = Math.Floor(v);
            return sorteio < v - inteiro ? inteiro + 1 : inteiro;
        }

        private static bool EmRunMultiplayer =>
            Jogadores > 1 && DungeonProgressManager.Instance != null && DungeonProgressManager.Instance.RunStarted.Value;

        private static double Sorteio => UnityEngine.Random.value * 0.9999999;

        /// <summary>Prefixo de DropBalance.CalculateExperienceDrop: XP do inimigo antes do bônus de XP do jogador.</summary>
        internal static void AjustarXp(ref int xp)
        {
            if (xp <= 0 || !EmRunMultiplayer) return;
            xp = (int)Escalar(xp, FatorXp, Sorteio);
        }

        /// <summary>Postfix de GoldTreasureClass.RollGoldValue e SoulStoneTreasureClass.RollSoulStonesAmount (drop e bênção).</summary>
        internal static void AjustarOuro(ref double valor)
        {
            if (valor <= 0 || !EmRunMultiplayer) return;
            valor = Escalar(valor, FatorOuro, Sorteio);
        }

        /// <summary>Prefixo de CurrencyTreasureClass.RollLoot: a moeda sai sempre 1 por drop; o fator vira a chance de sair.</summary>
        internal static bool ManterMoeda() => !EmRunMultiplayer || Escalar(1, FatorOuro, Sorteio) >= 1;

        /// <summary>Prefixo de EnemyManager.ApplySheetScaling: vale para os inimigos do host e para as réplicas.</summary>
        internal static void AjustarFicha(EnemyController inimigo)
        {
            EnemySheet ficha = inimigo?.EnemySheet;
            if (ficha == null) return;
            bool emRun = DungeonProgressManager.Instance != null &&
                         (DungeonProgressManager.Instance.RunStarted.Value || LocalPlayerSampler.AndarAtual() == null);
            float vida = emRun ? FatorVida(inimigo.EnemyType == EEnemyType.Boss) : 1f;
            float dano = emRun ? FatorDano : 1f;
            // O inimigo volta do pool com a ficha de antes: sempre tira o nosso modificador e põe de novo se precisar.
            ficha.RemoveStatModifier(EStatDefinition.Life, Chave);
            ficha.RemoveStatModifier(EStatDefinition.BaseDamage, Chave);
            if (!Mathf.Approximately(vida, 1f)) ficha.AddStatModifier(EStatDefinition.Life, EModifierType.Multiplicative, vida, Chave);
            if (!Mathf.Approximately(dano, 1f)) ficha.AddStatModifier(EStatDefinition.BaseDamage, EModifierType.Multiplicative, dano, Chave);
        }

        /// <summary>Prefixo de TimerManager.IncrementTimer no host (o tempo dos convidados chega por TimerBonus e passa aqui).</summary>
        internal static void AjustarTempo(ref float segundos)
        {
            if (segundos <= 0f || RunCompartilhada.Convidado || Jogadores <= 1) return;
            segundos *= FatorTempo;
        }

        public static void Atualizar()
        {
            if (Time.unscaledTime < _proximaChecagem) return;
            _proximaChecagem = Time.unscaledTime + 1f;
            EnemyManager em = EnemyManager.Instance;
            if (em == null) return;
            int n = Jogadores;
            try
            {
                var quantidade = em.EnemyGlobalModifiers.GetModifier(EEnemyGlobalModifier.RegularEnemyQuantity);
                float q = FatorQuantidade;
                if (quantidade != null)
                {
                    if (Mathf.Approximately(q, 1f)) { if (quantidade.HasModifier(Chave)) quantidade.RemoveModifier(Chave); }
                    else quantidade.AddOrUpdateModifier(Chave, q, EModifierType.Multiplicative);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Balanceamento] Quantidade de inimigos: {(e.InnerException ?? e).Message}");
            }
            if (n != _jogadoresAplicados)
            {
                _jogadoresAplicados = n;
                Plugin.Log.LogInfo($"[Balanceamento] {n} jogador(es): vida ×{FatorVida(false):0.##} (chefes ×{FatorVida(true):0.##}), " +
                                   $"dano ×{FatorDano:0.##}, quantidade ×{FatorQuantidade:0.##}, tempo ganho ×{FatorTempo:0.##}, " +
                                   $"XP ×{FatorXp:0.##}, ouro/Soulstones/moedas ×{FatorOuro:0.##} (em run). Vale para os próximos inimigos e andares.");
                if (n > 1) Sessao?.Avisar($"Balanceamento para {n} jogadores: inimigos com {Mathf.RoundToInt((FatorVida(false) - 1f) * 100)}% mais vida a partir de agora.");
            }
        }

        /// <summary>Aba Ajustes do menu do coop: os multiplicadores deste .cfg, só para leitura (com 2 jogadores fora do coop).</summary>
        internal static System.Collections.Generic.List<string> LinhasDoMenu()
        {
            var l = new System.Collections.Generic.List<string>();
            if (!Ligado)
            {
                l.Add("Desligado neste .cfg ([Balanceamento] Ativo = false).");
                return l;
            }
            int agora = Jogadores, n = Mathf.Max(2, agora);
            l.Add(agora > 1 ? $"Agora, com {n} jogadores:" : "Com 2 jogadores (os valores de 3 e 4 estão no .cfg):");
            l.Add($"Vida dos inimigos x{Fator(_vida, n):0.##}   Chefes x{Fator(_vidaChefe, n):0.##}");
            l.Add($"Dano dos inimigos x{Fator(_dano, n):0.##}   Quantidade x{Fator(_quantidade, n):0.##}");
            l.Add($"Tempo ganho x{Fator(_tempo, n):0.##}   XP x{Fator(_xp, n):0.##}   Ouro x{Fator(_moedas, n):0.##}");
            l.Add("O host decide os inimigos; os valores devem ser iguais para todos e só mudam no arquivo de configuração.");
            return l;
        }

        /// <summary>Linha para o painel do coop.</summary>
        internal static string Resumo()
        {
            int n = Jogadores;
            if (n <= 1 || !Ligado) return null;
            return $"Balanceamento {n}J: vida ×{FatorVida(false):0.#} · chefes ×{FatorVida(true):0.#} · dano ×{FatorDano:0.#} · inimigos ×{FatorQuantidade:0.#} · tempo ×{FatorTempo:0.##} · XP ×{FatorXp:0.##} · ouro ×{FatorOuro:0.##}";
        }
    }
}
