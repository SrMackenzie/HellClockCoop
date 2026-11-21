using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Avatar
{
    /// <summary>
    /// Captura/aplica quais peças de roupa estão ativas. As variantes ficam em
    /// Pajeu_RIG_360/Mesh/{Bota,Calca,Chapeu,Gibao,...}/*_lvl_NN, ligadas por PlayerGearVisuals
    /// conforme o equipamento de cada jogador. Vai até 2 níveis abaixo de "Mesh".
    /// </summary>
    internal static class AparenciaPersonagem
    {
        private const string NomeRaiz = "Mesh";

        public static Aparencia Capturar(Transform modelo)
        {
            var a = new Aparencia();
            Transform mesh = modelo != null ? modelo.Find(NomeRaiz) : null;
            if (mesh == null) return a;
            foreach (Transform grupo in mesh)
            {
                a.Nos.Add(new System.Collections.Generic.KeyValuePair<string, bool>(grupo.name, grupo.gameObject.activeSelf));
                foreach (Transform variante in grupo)
                {
                    a.Nos.Add(new System.Collections.Generic.KeyValuePair<string, bool>(grupo.name + "/" + variante.name, variante.gameObject.activeSelf));
                }
            }
            return a;
        }

        public static void Aplicar(Transform modelo, Aparencia a)
        {
            Transform mesh = modelo != null ? modelo.Find(NomeRaiz) : null;
            if (mesh == null || a == null) return;
            foreach (var no in a.Nos)
            {
                Transform t = mesh.Find(no.Key);
                if (t != null && t.gameObject.activeSelf != no.Value)
                {
                    t.gameObject.SetActive(no.Value);
                }
            }
        }
    }
}
