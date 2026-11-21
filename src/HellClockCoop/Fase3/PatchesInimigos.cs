using HarmonyLib;
using HellClock.Character;
using HellClock.Character.Enemy;
using HellClock.Damage;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    // Patches da Fase 3a. Cada um diz a classe e o método alvo; ver PatchTargets e docs/ARQUITETURA.md (seção 11).

    /// <summary>
    /// Alvo: EnemyManager.SpawnEnemy(SpawnEnemyData) — postfix (o prefixo da Fase 2 continua bloqueando
    /// os inimigos próprios do convidado). No host, numera o inimigo e avisa os convidados.
    /// </summary>
    [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.SpawnEnemy), typeof(SpawnEnemyData))]
    internal static class Inimigos_SpawnEnemy
    {
        private static void Postfix(SpawnEnemyData data, IEnemy __result)
        {
            if (__result is EnemyController c) InimigosCompartilhados.HostNasceu(c, data);
        }
    }

    /// <summary>Alvo: EnemyManager.Handle_EnemyDied(IEnemy, DamageInstance), privado — morte no host.</summary>
    [HarmonyPatch(typeof(EnemyManager), "Handle_EnemyDied")]
    internal static class Inimigos_EnemyDied
    {
        private static void Postfix(IEnemy enemy) => InimigosCompartilhados.HostMorreu(enemy);
    }

    /// <summary>Alvo: EnemyManager.DisposeEnemy(float, IEnemy) — descarte sem morte (host avisa; convidado esquece a réplica).</summary>
    [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.DisposeEnemy))]
    internal static class Inimigos_DisposeEnemy
    {
        private static void Prefix(IEnemy enemy)
        {
            InimigosCompartilhados.HostDescartou(enemy);
            InimigosCompartilhados.ConvidadoDescartou(enemy);
        }
    }

    /// <summary>Alvo: HellClock.Character.Enemy.EnemyController.Update — réplica sem IA, sem movimento próprio.</summary>
    [HarmonyPatch(typeof(EnemyController), "Update")]
    internal static class Inimigos_Update
    {
        private static bool Prefix(EnemyController __instance) => !InimigosCompartilhados.EhReplica(__instance);
    }

    /// <summary>Alvo: HellClock.Character.Enemy.EnemyController.CheckForSpawn, privado — no host, aliados também acordam inimigos.</summary>
    [HarmonyPatch(typeof(EnemyController), "CheckForSpawn")]
    internal static class Inimigos_CheckForSpawn
    {
        private static bool Prefix(EnemyController __instance, Vector3 ____spawnOrigin) =>
            !InimigosCompartilhados.HostAcordarPorAliado(__instance, ____spawnOrigin);
    }

    /// <summary>
    /// Alvo: HellClock.Character.LifeController.TryHurt(DamageInstance, bool) — por onde passa todo dano
    /// (Hurt, Kill, efeitos de status). No convidado, golpe em réplica vira relato ao host.
    /// </summary>
    [HarmonyPatch(typeof(LifeController), nameof(LifeController.TryHurt))]
    internal static class Inimigos_TryHurt
    {
        private static void Prefix(LifeController __instance, DamageInstance damage) => InimigosCompartilhados.ConvidadoGolpeou(__instance, damage);
    }

    /// <summary>
    /// Alvo: HellClock.Status.StatusController.AddStatus(StatusDefinition, ISkillCaster, IDamageable, float, float, int, int)
    /// — postfix. No convidado, status de controle posto numa réplica vira relato ao host. O postfix lê a duração
    /// depois que o AddStatus a reescreveu (resistência a controle e modificadores de quem aplicou).
    /// </summary>
    [HarmonyPatch(typeof(HellClock.Status.StatusController), nameof(HellClock.Status.StatusController.AddStatus))]
    internal static class Inimigos_AddStatus
    {
        private static void Postfix(HellClock.Status.StatusEffect __result, HellClock.Status.StatusDefinition statusDefinition,
            HellClock.Skill.ISkillCaster source, IDamageable target, float duration, float intensity, int sourceSkillID, int stackAmount) =>
            InimigosCompartilhados.ConvidadoAplicouStatus(__result, statusDefinition, source, target, duration, intensity, sourceSkillID, stackAmount);
    }

    /// <summary>Alvos: EnemyController.SetupSummon e SetupDisposable — chamados logo após o spawn (invocações, ondas).</summary>
    [HarmonyPatch(typeof(EnemyController), nameof(EnemyController.SetupSummon))]
    internal static class Inimigos_SetupSummon
    {
        private static void Postfix(EnemyController __instance) => InimigosCompartilhados.HostAjustou(__instance, Net.InimigoAjuste.Invocacao);
    }

    [HarmonyPatch(typeof(EnemyController), nameof(EnemyController.SetupDisposable))]
    internal static class Inimigos_SetupDisposable
    {
        private static void Postfix(EnemyController __instance) => InimigosCompartilhados.HostAjustou(__instance, Net.InimigoAjuste.Descartavel);
    }

    /// <summary>Alvo: HellClock.Chests.ChestInteractable.ExecuteInteraction — baú aberto pelo jogador local vira aviso.</summary>
    [HarmonyPatch(typeof(HellClock.Chests.ChestInteractable), nameof(HellClock.Chests.ChestInteractable.ExecuteInteraction))]
    internal static class Objetos_AbrirBau
    {
        private static void Postfix(HellClock.Chests.ChestInteractable __instance) => ObjetosCompartilhados.AbriuBau(__instance);
    }

    /// <summary>
    /// Alvo: HellClock.Player.PlayerController.InteractWith — ao abrir um baú por ordem de outro jogador,
    /// não parar o jogador local nem gravar a flag de interação no save dele.
    /// </summary>
    [HarmonyPatch(typeof(HellClock.Player.PlayerController), nameof(HellClock.Player.PlayerController.InteractWith))]
    internal static class Objetos_InteractWith
    {
        private static bool Prefix() => !(ObjetosCompartilhados.OrdemRemota || SantuariosCompartilhados.OrdemRemota);
    }

    /// <summary>Alvo: HellClock.Character.Enemy.BreakableController.Handle_OnDie (protegido) — quebrável quebrado.</summary>
    [HarmonyPatch(typeof(BreakableController), "Handle_OnDie")]
    internal static class Objetos_Quebrou
    {
        private static void Postfix(BreakableController __instance) => ObjetosCompartilhados.Quebrou(__instance);
    }

    /// <summary>
    /// Alvo: HellClock.Skill.ActiveSkill.Execute(ITarget) — postfix. No host, skill de inimigo da run vira
    /// InimigoSkill (as sobrescritas das subclasses chamam base.Execute, que é o que dispara o postfix).
    /// </summary>
    [HarmonyPatch(typeof(HellClock.Skill.ActiveSkill), nameof(HellClock.Skill.ActiveSkill.Execute), typeof(ITarget))]
    internal static class Inimigos_ExecuteSkill
    {
        private static void Postfix(HellClock.Skill.ActiveSkill __instance, ITarget target, bool __result)
        {
            if (__result) InimigosCompartilhados.HostUsouSkill(__instance, target);
        }
    }

    /// <summary>
    /// Alvo: getter de HellClock.Skill.ActiveSkill.CurrentSkillAvailabilityStatus — na skill comandada pelo
    /// host, a réplica não tem IA (não está "agressiva", sem recarga sincronizada): força AVAILABLE.
    /// </summary>
    [HarmonyPatch(typeof(HellClock.Skill.ActiveSkill), nameof(HellClock.Skill.ActiveSkill.CurrentSkillAvailabilityStatus), MethodType.Getter)]
    internal static class Inimigos_DisponibilidadeSkill
    {
        private static bool Prefix(ref HellClock.Skill.ESkillAvailabilityStatus __result)
        {
            if (!InimigosCompartilhados.ForcandoSkill) return true;
            __result = HellClock.Skill.ESkillAvailabilityStatus.AVAILABLE;
            return false;
        }
    }

    /// <summary>Alvo: HellClock.VFX.GameplayFx.CreateFx(ISkillCaster, Vector3?, Quaternion?, Skill) — efeitos das skills do jogador (3b).</summary>
    // Há também CreateFx<T> com os mesmos parâmetros: o alvo é escolhido à mão (não genérico, retorna GameObject).
    [HarmonyPatch]
    internal static class Fx_CreateFx
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            foreach (System.Reflection.MethodInfo m in typeof(HellClock.VFX.GameplayFx).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (m.Name == nameof(HellClock.VFX.GameplayFx.CreateFx) && !m.IsGenericMethodDefinition && m.ReturnType == typeof(GameObject)) return m;
            }
            return null;
        }

        private static void Postfix(HellClock.VFX.GameplayFx __instance, HellClock.Skill.ISkillCaster skillCaster, HellClock.Skill.Skill sourceSkill, GameObject __result)
        {
            FxCompartilhados.EfeitoCriado(__instance, skillCaster, sourceSkill, __result);
        }
    }

    /// <summary>Alvo: HellClock.Damage.DamageViewManager.SpawnDamageView — número de dano do jogador local vai para os aliados.</summary>
    [HarmonyPatch(typeof(DamageViewManager), nameof(DamageViewManager.SpawnDamageView))]
    internal static class Dano_SpawnDamageView
    {
        private static void Postfix(Collider targetCollider, Transform targetTransform, DamageInstance damage) =>
            DanosVisiveis.NumeroCriado(targetCollider, targetTransform, damage);
    }

    /// <summary>Alvo: HellClock.Shrine.ShrineInteractable.ExecuteInteraction — santuário usado pelo jogador local vale para todos.</summary>
    [HarmonyPatch(typeof(HellClock.Shrine.ShrineInteractable), nameof(HellClock.Shrine.ShrineInteractable.ExecuteInteraction))]
    internal static class Objetos_UsarSantuario
    {
        private static void Prefix(HellClock.Shrine.ShrineInteractable __instance) => SantuariosCompartilhados.UsouSantuario(__instance);
    }

    /// <summary>Alvo: HellClock.Shrine.CursedShrineInteractable.OnSurvivalEnd(bool), privado — host purificou o santuário amaldiçoado.</summary>
    [HarmonyPatch(typeof(HellClock.Shrine.CursedShrineInteractable), "OnSurvivalEnd")]
    internal static class Objetos_SantuarioPurificado
    {
        private static void Postfix(HellClock.Shrine.CursedShrineInteractable __instance, bool completed) => SantuariosCompartilhados.Purificado(__instance, completed);
    }
}
