using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HellClockCoop.Avatar
{
    /// <summary>
    /// O AnimatorController do Pajeú tem scripts presos aos estados (StateMachineBehaviour:
    /// BlockLayerMotions, PlayLasso). Eles não são componentes, então a remoção de lógica do clone
    /// não os alcança; no clone procuram o PlayerController/Movement360LayerBehaviour, não acham e
    /// lançam NullReferenceException a cada troca de estado (visto no Player.log da 0.2.0).
    /// Este guarda patcheia os callbacks de todo StateMachineBehaviour do jogo para não rodar
    /// em Animators de avatares do mod.
    /// </summary>
    internal static class AvatarAnimatorGuard
    {
        private static readonly HashSet<Animator> AnimatorsDeAvatar = new HashSet<Animator>();

        private static readonly string[] Callbacks =
        {
            "OnStateEnter", "OnStateExit", "OnStateUpdate", "OnStateMove", "OnStateIK",
            "OnStateMachineEnter", "OnStateMachineExit",
        };

        public static void Registrar(IEnumerable<Animator> animators)
        {
            foreach (Animator a in animators) AnimatorsDeAvatar.Add(a);
        }

        public static void Remover(IEnumerable<Animator> animators)
        {
            foreach (Animator a in animators) AnimatorsDeAvatar.Remove(a);
            AnimatorsDeAvatar.RemoveWhere(a => a == null); // limpa entradas destruídas
        }

        /// <summary>Patcheia os StateMachineBehaviours dos assemblies do jogo. Retorna quantos métodos.</summary>
        public static int Aplicar(Harmony harmony)
        {
            var prefixo = new HarmonyMethod(typeof(AvatarAnimatorGuard).GetMethod(nameof(Prefixo), BindingFlags.NonPublic | BindingFlags.Static));
            int total = 0;
            foreach (string nomeAssembly in new[] { "HellClock", "Assembly-CSharp", "K10" })
            {
                Assembly asm;
                try { asm = Assembly.Load(nomeAssembly); }
                catch (Exception) { continue; }
                foreach (Type t in AccessTools.GetTypesFromAssembly(asm))
                {
                    if (!typeof(StateMachineBehaviour).IsAssignableFrom(t) || t.IsAbstract) continue;
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        ParameterInfo[] p = m.GetParameters();
                        if (Array.IndexOf(Callbacks, m.Name) < 0 || p.Length == 0 || p[0].ParameterType != typeof(Animator) || p[0].Name != "animator") continue;
                        harmony.Patch(m, prefix: prefixo);
                        Plugin.Log.LogInfo($"[Avatar] Guarda de animação em {t.FullName}.{m.Name}");
                        total++;
                    }
                }
            }
            return total;
        }

        private static bool Prefixo(Animator animator) => !AnimatorsDeAvatar.Contains(animator);
    }
}
