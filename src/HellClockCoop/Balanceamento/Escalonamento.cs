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
    ///   próprias recompensas de tempo, então o tempo que entra no relógio único cresce com o grupo.
    ///
    /// Os valores ficam no .cfg (seção Balanceamento), um por número de jogadores: "2, 3, 4".
    /// </summary>
    internal static class Escalonamento
    {
        private const string Chave = "HellClockCoop.Escalonamento";

        private static ConfigEntry<bool> _ativo;
        private static ConfigEntry<string> _vida, _vidaChefe, _dano, _quantidade, _tempo;
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

        internal static float Fator(ConfigEntry<string> e, int jogadores)
        {
            if (jogadores <= 1 || e == null) return 1f;
            string texto = e.Value ?? "";
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
                                   $"dano ×{FatorDano:0.##}, quantidade ×{FatorQuantidade:0.##}, tempo ganho ×{FatorTempo:0.##}. Vale para os próximos inimigos e andares.");
                if (n > 1) Sessao?.Avisar($"Balanceamento para {n} jogadores: inimigos com {Mathf.RoundToInt((FatorVida(false) - 1f) * 100)}% mais vida a partir de agora.");
            }
        }

        /// <summary>Linha para o painel do coop.</summary>
        internal static string Resumo()
        {
            int n = Jogadores;
            if (n <= 1 || !Ligado) return null;
            return $"Balanceamento {n}J: vida ×{FatorVida(false):0.#} · chefes ×{FatorVida(true):0.#} · dano ×{FatorDano:0.#} · inimigos ×{FatorQuantidade:0.#} · tempo ×{FatorTempo:0.##}";
        }
    }
}
