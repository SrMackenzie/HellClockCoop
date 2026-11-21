using HarmonyLib;
using HellClock.Character.Enemy;

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
}
