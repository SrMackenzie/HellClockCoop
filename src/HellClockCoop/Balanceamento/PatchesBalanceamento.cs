using HarmonyLib;
using HellClock.Character.Enemy;
using HellClock.Drop;

namespace HellClockCoop.Balanceamento
{
    /// <summary>Alvo: EnemyManager.ApplySheetScaling(EnemyController), privado — vida e dano por número de jogadores.</summary>
    [HarmonyPatch(typeof(EnemyManager), "ApplySheetScaling")]
    internal static class Balanceamento_FichaInimigo
    {
        private static void Prefix(EnemyController enemy) => Escalonamento.AjustarFicha(enemy);
    }

    /// <summary>Alvo: TimerManager.IncrementTimer(float) — tempo ganho no Hell Clock por número de jogadores (host).</summary>
    [HarmonyPatch(typeof(TimerManager), nameof(TimerManager.IncrementTimer))]
    internal static class Balanceamento_Tempo
    {
        private static void Prefix(ref float seconds) => Escalonamento.AjustarTempo(ref seconds);
    }

    /// <summary>Alvo: DropBalance.CalculateExperienceDrop(uint, int) — XP de cada inimigo (comum e chefe) por número de jogadores.</summary>
    [HarmonyPatch(typeof(DropBalance), "CalculateExperienceDrop")]
    internal static class Balanceamento_Xp
    {
        private static void Prefix(ref int baseExperience) => Escalonamento.AjustarXp(ref baseExperience);
    }

    /// <summary>Alvo: GoldTreasureClass.RollGoldValue(), privado — ouro de cada drop e da bênção de ouro.</summary>
    [HarmonyPatch(typeof(GoldTreasureClass), "RollGoldValue")]
    internal static class Balanceamento_Ouro
    {
        private static void Postfix(ref double __result) => Escalonamento.AjustarOuro(ref __result);
    }

    /// <summary>Alvo: SoulStoneTreasureClass.RollSoulStonesAmount(int), privado — Soulstones de cada drop e da bênção.</summary>
    [HarmonyPatch(typeof(SoulStoneTreasureClass), "RollSoulStonesAmount")]
    internal static class Balanceamento_Soulstones
    {
        private static void Postfix(ref double __result) => Escalonamento.AjustarOuro(ref __result);
    }

    /// <summary>Alvo: CurrencyTreasureClass.RollLoot(LootData) — moeda de criação (sempre 1): o fator vira a chance de sair.</summary>
    [HarmonyPatch(typeof(CurrencyTreasureClass), nameof(CurrencyTreasureClass.RollLoot))]
    internal static class Balanceamento_Moedas
    {
        private static bool Prefix() => Escalonamento.ManterMoeda();
    }
}
