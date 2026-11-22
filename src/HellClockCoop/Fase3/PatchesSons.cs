using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using HellClock.Audio;
using HellClock.Damage;
using HellClock.Skill;
using HellClock.VFX;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    // Etapa 8 — sons das skills e golpes dos aliados. Os métodos de contexto (skills) são patcheados à mão em
    // SonsAliados.AplicarContextos; aqui ficam os dois métodos centrais do áudio e o efeito de golpe.
    // As versões com HcAudioEventReference só repassam para estas (conferido no IL do 2.1.1.1): patchear só estas
    // não conta o mesmo som duas vezes. A profundidade (_audio) garante que só a chamada de fora conta.

    /// <summary>Alvo: HellClock.Audio.AudioController.PlaySfx(EventReference, Vector3?, List&lt;AudioEventParameter&gt;, bool, bool) — som num ponto.</summary>
    [HarmonyPatch(typeof(AudioController), nameof(AudioController.PlaySfx),
        new[] { typeof(EventReference), typeof(Vector3?), typeof(List<AudioEventParameter>), typeof(bool), typeof(bool) })]
    internal static class Sons_PlaySfx
    {
        private static void Prefix(out bool __state, List<AudioEventParameter> audioEventParameters)
        {
            __state = SonsAliados.EntrarAudio();
            if (__state) SonsAliados.CopiarParametros(audioEventParameters); // o jogo esvazia a lista antes do postfix
        }

        private static void Postfix(bool __state, EventReference eventReference, Vector3? position, List<AudioEventParameter> audioEventParameters,
            bool isOneShot, EventInstance __result)
        {
            if (__state) SonsAliados.TocouNoPonto(eventReference, position, audioEventParameters, isOneShot, __result);
        }

        private static void Finalizer() => SonsAliados.SairAudio();
    }

    /// <summary>Alvo: HellClock.Audio.AudioController.PlayAndAttachSfx(EventReference, ISkillCaster, List&lt;AudioEventParameter&gt;, bool) — som preso a um personagem.</summary>
    [HarmonyPatch(typeof(AudioController), nameof(AudioController.PlayAndAttachSfx),
        new[] { typeof(EventReference), typeof(ISkillCaster), typeof(List<AudioEventParameter>), typeof(bool) })]
    internal static class Sons_PlayAndAttachNoLancador
    {
        private static void Prefix(out bool __state) => __state = SonsAliados.EntrarAudio();

        private static void Postfix(bool __state, EventReference eventReference, ISkillCaster skillCaster, List<AudioEventParameter> audioEventParameters, AttachedSfx __result)
        {
            if (__state) SonsAliados.TocouNoLancador(eventReference, skillCaster, audioEventParameters, __result);
        }

        private static void Finalizer() => SonsAliados.SairAudio();
    }

    /// <summary>Alvo: HellClock.Audio.AudioController.PlayAndAttachSfx(EventReference, GameObject, List&lt;AudioEventParameter&gt;, bool) — som preso a um objeto (efeito).</summary>
    [HarmonyPatch(typeof(AudioController), nameof(AudioController.PlayAndAttachSfx),
        new[] { typeof(EventReference), typeof(GameObject), typeof(List<AudioEventParameter>), typeof(bool) })]
    internal static class Sons_PlayAndAttachNoObjeto
    {
        private static void Prefix(out bool __state) => __state = SonsAliados.EntrarAudio();

        private static void Postfix(bool __state, EventReference eventReference, GameObject targetObject, List<AudioEventParameter> audioEventParameters, AttachedSfx __result)
        {
            if (__state) SonsAliados.TocouNoObjeto(eventReference, targetObject, audioEventParameters, __result);
        }

        private static void Finalizer() => SonsAliados.SairAudio();
    }

    /// <summary>
    /// Alvo: HellClock.Damage.DamageDealer.PerformDamageWrapper(DamageSourceInfo, IAttacker, IDamageable, DamageHitInfo, EFxOnDamage), privado —
    /// enquanto o jogo processa um golpe, nenhum som vira som de skill (a dor vai pelos golpes vistos; a morte, pela morte do inimigo).
    /// </summary>
    [HarmonyPatch(typeof(DamageDealer), "PerformDamageWrapper")]
    internal static class Sons_DentroDoGolpe
    {
        private static void Prefix() => SonsAliados.EntrarDano();

        private static void Finalizer() => SonsAliados.SairDano();
    }

    /// <summary>
    /// Alvo: HellClock.VFX.CharacterEffectsController.CreateDamageFX(DamageHitInfo, DamageInstance, EFxOnDamage, bool) — sangue,
    /// som de dor e efeito do tipo de dano de um golpe; só o DamageDealer.PerformDamageWrapper chama (golpe de verdade).
    /// </summary>
    [HarmonyPatch(typeof(CharacterEffectsController), nameof(CharacterEffectsController.CreateDamageFX))]
    internal static class Golpes_CreateDamageFX
    {
        private static void Postfix(CharacterEffectsController __instance, DamageHitInfo damageHitInfo, DamageInstance damageInstance, EFxOnDamage fxOnDamage)
        {
            double inicio = Desempenho.InicioGancho();
            try { GolpesVisiveis.GolpeCriado(__instance, damageHitInfo, damageInstance, fxOnDamage); }
            finally { Desempenho.FimGancho(inicio); }
        }
    }
}
