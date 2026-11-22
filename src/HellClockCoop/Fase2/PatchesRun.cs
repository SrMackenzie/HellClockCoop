using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HellClock.Level;
using HellClock.Manager;
using HellClock.Character.Enemy;
using UnityEngine;

namespace HellClockCoop.Fase2
{
    // Patches da Fase 2. Todos checam o modo em tempo de execução (Host/Convidado);
    // fora de uma sessão coop o jogo roda exatamente como sem o mod.

    /// <summary>Alvo: HellClock.Level.LevelBlock.GenerateNextLevel — único ponto onde a LevelDefinition é sorteada.</summary>
    [HarmonyPatch(typeof(LevelBlock), nameof(LevelBlock.GenerateNextLevel))]
    internal static class Run_GenerateNextLevel
    {
        private static void Postfix(uint levelIndex, LevelBlockInfo levelBlockInfo, ref LevelInfo __result)
        {
            if (RunCompartilhada.Convidado) __result = RunCompartilhada.AndarDoHost(levelIndex, levelBlockInfo, __result);
            else RunCompartilhada.HostCapturouAndar(levelIndex, levelBlockInfo, __result);
        }
    }

    /// <summary>Alvos: DungeonConfig.GetLevelBlockInfoFromLevel e as sobrescritas em AscensionDungeonConfig e DynamicDungeonConfig.</summary>
    [HarmonyPatch]
    internal static class Run_GetLevelBlockInfoFromLevel
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DungeonConfig), nameof(DungeonConfig.GetLevelBlockInfoFromLevel));
            yield return AccessTools.Method(typeof(AscensionDungeonConfig), nameof(DungeonConfig.GetLevelBlockInfoFromLevel));
            yield return AccessTools.Method(typeof(DynamicDungeonConfig), nameof(DungeonConfig.GetLevelBlockInfoFromLevel));
        }

        private static void Postfix(uint levelIndex, ref LevelBlockInfo __result)
        {
            if (!RunCompartilhada.Convidado) return;
            LevelBlockInfo doHost = RunCompartilhada.BlocoDoHost(levelIndex);
            if (doHost != null) __result = doHost;
        }
    }

    /// <summary>
    /// Alvo: HellClock.Level.LevelObjects.SetupPlayerSpawn (privado) — índice rotativo do ponto de spawn; no andar novo,
    /// o jogador caído volta (0.15.0).
    /// </summary>
    [HarmonyPatch(typeof(LevelObjects), "SetupPlayerSpawn")]
    internal static class Run_SetupPlayerSpawn
    {
        private static void Prefix(LevelObjects __instance, LevelInfo levelInfo) => RunCompartilhada.AjustarSpawn(__instance, levelInfo);

        private static void Postfix(LevelObjects __instance, LevelInfo levelInfo)
        {
            RunCompartilhada.HostUsouSpawn(levelInfo, RunCompartilhada.SpawnIndexRef(__instance));
            JogadorCaido.NovoAndar(levelInfo);
        }
    }

    /// <summary>Alvo: HellClock.Level.LevelObjects.SetupEnemies — convidado com andar vazio até a Fase 3.</summary>
    [HarmonyPatch(typeof(LevelObjects), "SetupEnemies")]
    internal static class Run_SetupEnemies
    {
        private static bool Prefix(LevelInfo levelInfo) => !(RunCompartilhada.Convidado && levelInfo?.LevelBlockInfo != null);
    }

    /// <summary>Alvo: HellClock.Level.LevelObjects.SetupLevelObjects — baús, santuários, totens e portais são do host.</summary>
    [HarmonyPatch(typeof(LevelObjects), "SetupLevelObjects")]
    internal static class Run_SetupLevelObjects
    {
        private static bool Prefix(LevelObjects __instance, LevelInfo levelInfo)
        {
            if (!(RunCompartilhada.Convidado && levelInfo?.LevelBlockInfo != null)) return true;
            Fase3.ObjetosCompartilhados.ConvidadoNovoAndar(__instance); // baús do host entram nas listas do andar
            return false;
        }
    }

    /// <summary>
    /// Alvo: EnemyManager.SpawnEnemy(SpawnEnemyData) — ponto único por onde passam todos os geradores
    /// (andar, sobrevivência, temporadas Nightmare/Miracles, invocações). No convidado, durante a run,
    /// não nasce inimigo próprio (andar vazio até a Fase 3, que vai liberar as réplicas do host).
    /// Os chamadores das temporadas já tratam o retorno nulo.
    /// </summary>
    [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.SpawnEnemy), typeof(SpawnEnemyData))]
    internal static class Run_SpawnEnemy
    {
        private static bool Prefix(ref IEnemy __result)
        {
            if (!(RunCompartilhada.Convidado && DungeonProgressManager.Instance.RunStarted.Value)) return true;
            if (Fase3.InimigosCompartilhados.CriandoReplica) return true; // réplica de um inimigo do host
            __result = null;
            return false;
        }
    }

    /// <summary>Alvo: HellClock.Manager.SurvivalEventManager.StartSurvival — o laço de ondas não aceita inimigo nulo.</summary>
    [HarmonyPatch(typeof(SurvivalEventManager), nameof(SurvivalEventManager.StartSurvival))]
    internal static class Run_StartSurvival
    {
        private static bool Prefix() => !(RunCompartilhada.Convidado && DungeonProgressManager.Instance.RunStarted.Value);
    }

    /// <summary>Alvo: DungeonProgressManager.StartRunFromDungeon(DungeonConfig) — a sobrecarga por EDungeon chama esta.</summary>
    [HarmonyPatch(typeof(DungeonProgressManager), nameof(DungeonProgressManager.StartRunFromDungeon), typeof(DungeonConfig))]
    internal static class Run_StartRunFromDungeon
    {
        private static bool Prefix() => !RunCompartilhada.BloquearAcaoDoConvidado("inicia a run");
    }

    /// <summary>Alvo: DungeonProgressManager.StartRunFromDungeonCoroutine.</summary>
    [HarmonyPatch(typeof(DungeonProgressManager), nameof(DungeonProgressManager.StartRunFromDungeonCoroutine))]
    internal static class Run_StartRunFromDungeonCoroutine
    {
        private static bool Prefix(ref IEnumerator __result)
        {
            if (!RunCompartilhada.BloquearAcaoDoConvidado("inicia a run")) return true;
            __result = RunCompartilhada.Vazio();
            return false;
        }
    }

    /// <summary>Alvo: DungeonProgressManager.StartRunFromSpecialLevelBlock.</summary>
    [HarmonyPatch(typeof(DungeonProgressManager), nameof(DungeonProgressManager.StartRunFromSpecialLevelBlock))]
    internal static class Run_StartRunFromSpecialLevelBlock
    {
        private static bool Prefix() => !RunCompartilhada.BloquearAcaoDoConvidado("inicia a run");
    }

    /// <summary>
    /// Alvo: DungeonProgressManager.AdvanceLevel — portais (inclusive vermelhos) do convidado. Com o host caído
    /// (0.15.0), o portal do convidado vira PedidoAvanco ao host.
    /// </summary>
    [HarmonyPatch(typeof(DungeonProgressManager), nameof(DungeonProgressManager.AdvanceLevel))]
    internal static class Run_AdvanceLevel
    {
        private static bool Prefix(uint levelsToAdvance)
        {
            if (RunCompartilhada.PedirAvancoComHostCaido(levelsToAdvance)) return false;
            return !RunCompartilhada.BloquearAcaoDoConvidado("avança de andar");
        }
    }

    /// <summary>Alvo: DungeonProgressManager.FinishDungeon — o fim da run é do host.</summary>
    [HarmonyPatch(typeof(DungeonProgressManager), nameof(DungeonProgressManager.FinishDungeon))]
    internal static class Run_FinishDungeon
    {
        private static bool Prefix(EGameOverSource source, ref Coroutine __result)
        {
            if (RunCompartilhada.ConvidadoMorreu(source)) return true; // a run DELE acaba; volta no próximo andar do host
            if (!RunCompartilhada.BloquearAcaoDoConvidado("encerra a run")) return true;
            __result = null;
            return false;
        }

        private static void Postfix(EGameOverSource source)
        {
            Balanceamento.Metricas.Motivo(source);
            // Caído no fim da run: libera as ações; a volta ao hub fica com o jogo. No convidado, só no fim mandado pelo
            // host (o postfix roda mesmo com o prefixo bloqueando).
            if (!RunCompartilhada.Convidado || RunCompartilhada.PermitidoAgora) JogadorCaido.FimDaRun();
            if (!RunCompartilhada.Convidado) RunCompartilhada.HostTerminou(source);
        }
    }

    /// <summary>
    /// Alvo: TimerManager.Update — no convidado o Hell Clock não corre; mostra o do host. No host caído (0.15.0) o
    /// relógio continua: o Update do jogo para quando o jogador local está morto (IsDead), e o caído fica com vida 0.
    /// Mesmas condições do jogo (PauseTimer livre, run em andamento, sem o truque de tempo infinito, sem fim já
    /// executado), sem o teste de morto; o próprio UpdateDefaultMode encerra a run quando o tempo zera.
    /// </summary>
    [HarmonyPatch(typeof(TimerManager), nameof(TimerManager.Update))]
    internal static class Run_TimerUpdate
    {
        private static readonly MethodInfo ModoPadrao = AccessTools.Method(typeof(TimerManager), "UpdateDefaultMode");
        private static readonly MethodInfo ModoRelaxado = AccessTools.Method(typeof(TimerManager), "UpdateRelaxedMode");

        private static bool Prefix(TimerManager __instance)
        {
            DungeonProgressManager dpm = DungeonProgressManager.Instance;
            if (RunCompartilhada.Convidado && dpm.RunStarted.Value) return false;
            if (!JogadorCaido.Caido) return true;
            if (__instance.PauseTimer.Free && dpm.RunStarted.Value && !HellClock.Cheat.CheatInputs.CheatInfiniteTimer.Value && !dpm.EndingExecuted.Value)
                (__instance.RelaxedModeEnabled ? ModoRelaxado : ModoPadrao).Invoke(__instance, null);
            return false;
        }
    }

    /// <summary>Alvo: TimerManager.IncrementTimer — tempo ganho pelo convidado vai para o Hell Clock do host.</summary>
    [HarmonyPatch(typeof(TimerManager), nameof(TimerManager.IncrementTimer))]
    internal static class Run_IncrementTimer
    {
        private static bool Prefix(TimerManager __instance, float seconds, ref float __result)
        {
            if (!(RunCompartilhada.Convidado && DungeonProgressManager.Instance.RunStarted.Value)) return true;
            RunCompartilhada.ConvidadoGanhouTempo(seconds);
            __result = __instance.CurrentTimer;
            return false;
        }
    }

    /// <summary>
    /// Alvos: HellClock.Manager.TimeManager.PauseTime e PauseGame. Com aliados no coop, abrir janelas,
    /// popups ou o menu não congela o tempo (congelaria o mundo só de um lado). O carregamento da
    /// primeira cena (chave LevelChanger) continua pausando.
    /// </summary>
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.PauseTime))]
    internal static class Run_PauseTime
    {
        private static bool Prefix(object key) => !(RunCompartilhada.Sessao != null && RunCompartilhada.Sessao.ComAliados && !(key is LevelChanger));
    }

    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.PauseGame))]
    internal static class Run_PauseGame
    {
        private static bool Prefix() => !(RunCompartilhada.Sessao != null && RunCompartilhada.Sessao.ComAliados);
    }
}
