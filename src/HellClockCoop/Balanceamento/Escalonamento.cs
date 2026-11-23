using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
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
    /// Todos os valores são do autor e fixos no código, um por número de jogadores (2, 3, 4): XP e ouro desde a 0.16.3;
    /// vida, chefes, dano, quantidade e tempo desde a 0.22.0, sem liga/desliga (decisão do autor: quem usa o mod usa os
    /// critérios do mod; ninguém, nem o host, muda o balanceamento pelo .cfg).
    /// </summary>
    internal static class Escalonamento
    {
        private const string Chave = "HellClockCoop.Escalonamento";

        // Valores do autor, fora do .cfg, com 2, 3 e 4 jogadores. O balanceamento é calibrado com as métricas; um valor
        // mudado por jogador sujaria os dados e daria a um host o poder de mexer no jogo dos outros.
        // 0.22.0: vida, chefes, dano, quantidade e tempo saíram do .cfg com os valores padrão que tinham lá.
        internal static readonly float[] VidaGrupo = { 1.7f, 2.4f, 3.1f };
        internal static readonly float[] VidaChefeGrupo = { 1.8f, 2.6f, 3.4f };
        internal static readonly float[] DanoGrupo = { 1.1f, 1.2f, 1.3f };
        internal static readonly float[] QuantidadeGrupo = { 1.2f, 1.4f, 1.6f };
        internal static readonly float[] TempoGrupo = { 0.55f, 0.4f, 0.32f }; // cada um pega as próprias recompensas de tempo
        // XP e ouro/Soulstones/moedas de criação de cada jogador (0.16.3): 1/quantidade, para o ganho por andar ficar perto do solo.
        internal static readonly float[] XpGrupo = { 0.83f, 0.71f, 0.63f };
        internal static readonly float[] OuroGrupo = { 0.83f, 0.71f, 0.63f };

        /// <summary>Chaves que ficavam na seção [Balanceamento] do .cfg até a 0.21.x (apagadas do arquivo na 0.22.0).</summary>
        internal static readonly string[] ChavesAntigas = { "Ativo", "VidaInimigos", "VidaChefes", "DanoInimigos", "QuantidadeInimigos", "TempoGanho" };
        private static int _jogadoresAplicados = 1;
        private static float _proximaChecagem;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;

        /// <summary>
        /// 0.22.0: apaga do .cfg a seção [Balanceamento] antiga (os valores agora são do autor, no código), para ninguém
        /// achar que editar o arquivo muda alguma coisa. Sem a API (BepInEx diferente), só avisa no log.
        /// </summary>
        internal static void Configurar(ConfigFile config)
        {
            try
            {
                var orfas = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config, null) as Dictionary<ConfigDefinition, string>;
                if (orfas == null) return;
                int apagadas = 0;
                foreach (string chave in ChavesAntigas)
                {
                    if (orfas.Remove(new ConfigDefinition("Balanceamento", chave))) apagadas++;
                }
                if (apagadas == 0) return;
                config.Save();
                Plugin.Log.LogInfo($"[Balanceamento] {apagadas} opção(ões) antiga(s) de [Balanceamento] apagada(s) do .cfg: os valores agora são fixos no mod.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Balanceamento] Não deu para apagar a seção antiga do .cfg ({(e.InnerException ?? e).Message}); ela é ignorada.");
            }
        }

        /// <summary>
        /// Jogadores no coop que dividem a run (o local + aliados compatíveis; desde a 0.21.0 com ou sem a mesma DLC). Desde a 0.19.0 sem o
        /// convidado que avisou não acompanhar (o save dele não chegou à run: ForaDaRun), a não ser que ele esteja no
        /// mesmo andar (um ForaDaRun falso não tira da escala quem joga a run).
        /// </summary>
        internal static int Jogadores
        {
            get
            {
                if (Sessao == null || !Sessao.EmSessao) return 1;
                int n = 1;
                foreach (Par p in Sessao.Pares.Values) if (p.Compativel && !ForaDaEscala(p)) n++;
                return Mathf.Clamp(n, 1, 4);
            }
        }

        /// <summary>
        /// 0.19.0: par que avisou não acompanhar a run e não está no mesmo andar. Não conta na escala e, no host, os golpes
        /// e status dele são ignorados (sem isso, ForaDaRun falso + cena falsa tiraria da escala quem ainda bate).
        /// 0.21.0: também o par sem a Cursed War fora do nosso andar numa run do Ato 4, sem esperar o ForaDaRun (que não
        /// chega se ele está no menu ou carregando). Mentir que tem a DLC só o põe na escala (grupo mais difícil).
        /// </summary>
        internal static bool ForaDaEscala(Par p) =>
            !p.MesmoAndar && (p.SemLiberar || (!p.CursedWar && Fase2.RunCompartilhada.RunAtualDaDlc()));

        internal static float FatorVida(bool chefe) => FatorFixo(chefe ? VidaChefeGrupo : VidaGrupo, Jogadores);
        internal static float FatorDano => FatorFixo(DanoGrupo, Jogadores);
        internal static float FatorQuantidade => FatorFixo(QuantidadeGrupo, Jogadores);
        internal static float FatorTempo => FatorFixo(TempoGrupo, Jogadores);
        internal static float FatorXp => FatorFixo(XpGrupo, Jogadores);
        internal static float FatorOuro => FatorFixo(OuroGrupo, Jogadores);

        /// <summary>Valor fixo do autor para o número de jogadores (2, 3, 4; além usa o último); solo = 1.</summary>
        internal static float FatorFixo(float[] lista, int jogadores) =>
            jogadores <= 1 || lista == null || lista.Length == 0 ? 1f : lista[Mathf.Min(jogadores - 2, lista.Length - 1)];

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

        /// <summary>Aba Ajustes do menu do coop: os valores do autor, só para leitura (com 2 jogadores fora do coop).</summary>
        internal static List<string> LinhasDoMenu()
        {
            var l = new List<string>();
            int agora = Jogadores, n = Mathf.Max(2, agora);
            l.Add(agora > 1 ? $"Agora, com {n} jogadores:" : "Com 2 jogadores (3 e 4 jogadores têm outros valores):");
            l.Add($"Vida dos inimigos x{FatorFixo(VidaGrupo, n):0.##}   Chefes x{FatorFixo(VidaChefeGrupo, n):0.##}");
            l.Add($"Dano dos inimigos x{FatorFixo(DanoGrupo, n):0.##}   Quantidade x{FatorFixo(QuantidadeGrupo, n):0.##}");
            l.Add($"Tempo ganho x{FatorFixo(TempoGrupo, n):0.##}   XP x{FatorFixo(XpGrupo, n):0.##}   Ouro x{FatorFixo(OuroGrupo, n):0.##}");
            l.Add("Valores fixos do mod, iguais para todos os jogadores.");
            return l;
        }

        /// <summary>Linha para o painel do coop.</summary>
        internal static string Resumo()
        {
            int n = Jogadores;
            if (n <= 1) return null;
            return $"Balanceamento {n}J: vida ×{FatorVida(false):0.#} · chefes ×{FatorVida(true):0.#} · dano ×{FatorDano:0.#} · inimigos ×{FatorQuantidade:0.#} · tempo ×{FatorTempo:0.##} · XP ×{FatorXp:0.##} · ouro ×{FatorOuro:0.##}";
        }
    }
}
