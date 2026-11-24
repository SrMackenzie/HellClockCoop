using System.Collections.Generic;
using HarmonyLib;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Avatar
{
    /// <summary>
    /// Lê e aplica o estado de um Animator de forma genérica (sem conhecer cada skill):
    /// o estado atual de cada layer + todos os parâmetros float/int/bool, na ordem do controller.
    /// Os dois lados usam o mesmo AnimatorController do jogo, então a ordem dos parâmetros bate.
    /// </summary>
    internal static class AnimatorSync
    {
        private sealed class Parametros
        {
            public int[] Floats;
            public int[] Ints;
            public int[] Bools;
        }

        // O build do jogo removeu AnimatorControllerParameter.type (managed stripping); o campo interno sobrou.
        private static readonly AccessTools.FieldRef<AnimatorControllerParameter, AnimatorControllerParameterType> TipoRef =
            AccessTools.FieldRefAccess<AnimatorControllerParameter, AnimatorControllerParameterType>("m_Type");

        private static readonly Dictionary<RuntimeAnimatorController, Parametros> Cache =
            new Dictionary<RuntimeAnimatorController, Parametros>();

        private static Parametros ObterParametros(Animator animator)
        {
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                return null;
            }
            if (Cache.TryGetValue(controller, out Parametros p))
            {
                return p;
            }
            var floats = new List<int>();
            var ints = new List<int>();
            var bools = new List<int>();
            foreach (AnimatorControllerParameter par in animator.parameters)
            {
                switch (TipoRef(par))
                {
                    case AnimatorControllerParameterType.Float: floats.Add(par.nameHash); break;
                    case AnimatorControllerParameterType.Int: ints.Add(par.nameHash); break;
                    case AnimatorControllerParameterType.Bool: bools.Add(par.nameHash); break;
                    // Triggers não são enviados: o efeito deles aparece na troca de estado.
                }
            }
            p = new Parametros { Floats = floats.ToArray(), Ints = ints.ToArray(), Bools = bools.ToArray() };
            Cache[controller] = p;
            return p;
        }

        public static void Ler(Animator animator, AnimatorFrame destino)
        {
            int camadas = animator.layerCount;
            if (destino.StateHashes.Length != camadas)
            {
                destino.StateHashes = new int[camadas];
                destino.NormalizedTimes = new float[camadas];
                destino.LayerWeights = new float[camadas];
            }
            for (int l = 0; l < camadas; l++)
            {
                // Durante uma transição, manda o estado de destino: o fantasma já vai direto para ele.
                AnimatorStateInfo info = animator.IsInTransition(l)
                    ? animator.GetNextAnimatorStateInfo(l)
                    : animator.GetCurrentAnimatorStateInfo(l);
                destino.StateHashes[l] = info.fullPathHash;
                destino.NormalizedTimes[l] = info.normalizedTime;
                destino.LayerWeights[l] = l == 0 ? 1f : animator.GetLayerWeight(l); // a base é sempre 1
            }

            Parametros p = ObterParametros(animator);
            if (p == null)
            {
                return;
            }
            if (destino.Floats.Length != p.Floats.Length) destino.Floats = new float[p.Floats.Length];
            if (destino.Ints.Length != p.Ints.Length) destino.Ints = new int[p.Ints.Length];
            if (destino.Bools.Length != p.Bools.Length) destino.Bools = new bool[p.Bools.Length];
            for (int i = 0; i < p.Floats.Length; i++) destino.Floats[i] = animator.GetFloat(p.Floats[i]);
            for (int i = 0; i < p.Ints.Length; i++) destino.Ints[i] = animator.GetInteger(p.Ints[i]);
            for (int i = 0; i < p.Bools.Length; i++) destino.Bools[i] = animator.GetBool(p.Bools[i]);
        }

        /// <summary>
        /// Aplica no Animator do fantasma. Troca de estado com CrossFade curto; se o mesmo estado
        /// estiver muito fora de tempo (>15%), ressincroniza. <paramref name="acertarTempo"/> falso pula esse acerto:
        /// para um quadro que ficou guardado (o tempo dele já passou), que só deve trocar estado e parâmetros.
        /// </summary>
        public static void Aplicar(Animator animator, AnimatorFrame origem, bool acertarTempo = true)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            {
                return;
            }
            Parametros p = ObterParametros(animator);
            if (p != null)
            {
                for (int i = 0; i < p.Floats.Length && i < origem.Floats.Length; i++) animator.SetFloat(p.Floats[i], origem.Floats[i]);
                for (int i = 0; i < p.Ints.Length && i < origem.Ints.Length; i++) animator.SetInteger(p.Ints[i], origem.Ints[i]);
                for (int i = 0; i < p.Bools.Length && i < origem.Bools.Length; i++) animator.SetBool(p.Bools[i], origem.Bools[i]);
            }

            int camadas = Mathf.Min(animator.layerCount, origem.StateHashes.Length);
            for (int l = 1; l < camadas && l < origem.LayerWeights.Length; l++)
            {
                // Sem isto as layers de ataque (braços/corpo inteiro) ficam com peso 0 no clone.
                animator.SetLayerWeight(l, origem.LayerWeights[l]);
            }
            for (int l = 0; l < camadas; l++)
            {
                int hash = origem.StateHashes[l];
                if (hash == 0 || !animator.HasState(l, hash))
                {
                    continue;
                }
                AnimatorStateInfo atual = animator.IsInTransition(l)
                    ? animator.GetNextAnimatorStateInfo(l)
                    : animator.GetCurrentAnimatorStateInfo(l);
                // normalizedTime cresce sem limite em estados em loop (2,3 = 3º ciclo, 30%):
                // compara só a fração, com volta no 1.
                float fracaoAlvo = Mathf.Repeat(origem.NormalizedTimes[l], 1f);
                if (atual.fullPathHash != hash)
                {
                    // CrossFade(hash, duração normalizada da transição, layer, offset normalizado no estado de destino)
                    animator.CrossFade(hash, 0.1f, l, fracaoAlvo);
                }
                else if (acertarTempo)
                {
                    float d = Mathf.Abs(Mathf.Repeat(atual.normalizedTime, 1f) - fracaoAlvo);
                    if (Mathf.Min(d, 1f - d) > 0.15f)
                    {
                        animator.Play(hash, l, fracaoAlvo);
                    }
                }
            }
        }
    }
}
