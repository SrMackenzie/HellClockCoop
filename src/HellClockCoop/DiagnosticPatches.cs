using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using HellClock.Level;
using HellClock.Player;
using UnityEngine;

namespace HellClockCoop
{
    // Patches da Fase 0: só observam e escrevem no log. Não alteram nenhum comportamento
    // do jogo. Servem para confirmar, rodando o jogo, que os pontos de patch mapeados
    // no código decompilado são de fato chamados na ordem esperada.

    /// <summary>Alvo: PlayerManager.Setup(PlayerController) — chamado por BootstrapDungeon.AwakeCoroutine.</summary>
    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.Setup))]
    internal static class Diag_PlayerManager_Setup
    {
        private static void Postfix(PlayerController playerController)
        {
            if (playerController == null)
            {
                return;
            }
            Plugin.Log.LogInfo($"[Diag] Jogador local registrado no PlayerManager em {playerController.transform.position}.");
            SteamDiag.Logar();
        }
    }

    /// <summary>Alvo: TimerManager.StartTimer(float) — chamado por DungeonProgressManager.StartRun.</summary>
    [HarmonyPatch(typeof(TimerManager), nameof(TimerManager.StartTimer))]
    internal static class Diag_TimerManager_StartTimer
    {
        private static void Postfix(TimerManager __instance, float timeLimit)
        {
            Plugin.Log.LogInfo($"[Diag] Run iniciada. TimeLimit base = {timeLimit:0}s, Hell Clock agora = {__instance.CurrentTimer:0.0}s.");
        }
    }

    /// <summary>Alvo: EnemyManager.AddToAvailableTargets(ITarget) — a lista que os inimigos usam para escolher alvo.</summary>
    [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.AddToAvailableTargets))]
    internal static class Diag_EnemyManager_AddToAvailableTargets
    {
        private static readonly AccessTools.FieldRef<EnemyManager, List<ITarget>> AlvosRef =
            AccessTools.FieldRefAccess<EnemyManager, List<ITarget>>("_availableTargets");

        private static void Postfix(EnemyManager __instance, ITarget target)
        {
            int total = AlvosRef(__instance)?.Count ?? -1;
            string nome = target?.Transform != null ? target.Transform.name : "(sem transform)";
            Plugin.Log.LogInfo($"[Diag] Alvo disponível para inimigos: {nome} ({target?.ETargetType}). Total na lista: {total}.");
        }
    }

    /// <summary>Alvo: LevelChanger.ChangeLevel(LevelInfo, Action, Func&lt;Coroutine&gt;).</summary>
    [HarmonyPatch(typeof(LevelChanger), nameof(LevelChanger.ChangeLevel))]
    internal static class Diag_LevelChanger_ChangeLevel
    {
        private static void Prefix(LevelInfo levelInfo)
        {
            if (levelInfo?.LevelDefinition == null)
            {
                return;
            }
            Plugin.Log.LogInfo(
                $"[Diag] Carregando andar: índice {levelInfo.LevelIndex}, LevelDefinition '{levelInfo.LevelDefinition.name}' " +
                $"(HashID {levelInfo.LevelDefinition.HashID}), bloco {levelInfo.LevelBlockHashID}, boss={levelInfo.IsBossLevel}.");
        }
    }

    /// <summary>
    /// Lê o estado do SteamManager do jogo por reflexão, sem tocar em SteamManager.Instance:
    /// aquele getter cria um SteamManager novo se não houver um, e o jogo lança exceção
    /// se a SteamAPI for inicializada duas vezes.
    /// </summary>
    internal static class SteamDiag
    {
        public static void Logar()
        {
            try
            {
                if (!Net.SteamEstado.Pronto())
                {
                    Plugin.Log.LogWarning("[Diag] Steam ainda não inicializado pelo jogo.");
                    return;
                }
                string nome = Steamworks.SteamFriends.GetPersonaName();
                ulong id = Steamworks.SteamUser.GetSteamID().m_SteamID;
                Plugin.Log.LogInfo($"[Diag] Steam OK: {nome} ({id}). Steamworks.NET acessível para a Fase 1.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Diag] Não foi possível ler o estado do Steam: {e.Message}");
            }
        }
    }
}
