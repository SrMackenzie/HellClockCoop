using System;
using System.Collections.Generic;
using System.Text;
using HellClock.Player;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Avatar
{
    /// <summary>
    /// Cria o clone visual do jogador para representar um aliado.
    /// Técnica (a mesma do BootstrapDungeon): instanciar dentro de um pai DESATIVADO, remover
    /// todos os componentes de lógica e só então ativar. Como o clone nunca foi ativado,
    /// nenhum Awake/OnEnable/OnDestroy do PlayerController roda nele (a Unity só chama
    /// OnDestroy em objetos que já estiveram ativos), então nada toca em singletons ou no save.
    /// </summary>
    internal static class AvatarFactory
    {
        private static readonly HashSet<Type> Mantidos = new HashSet<Type>
        {
            typeof(Transform), typeof(RectTransform), typeof(Animator),
            typeof(SkinnedMeshRenderer), typeof(MeshRenderer), typeof(MeshFilter),
            // (LODGroup não existe no build do jogo: o managed stripping removeu)
        };

        public static RemotePlayerAvatar Criar(PlayerController modelo, string nome)
        {
            var raiz = new GameObject($"HellClockCoop_Avatar_{nome}");
            raiz.SetActive(false);

            double inicio = Desempenho.Agora(); // 0.31.3: os dois trechos pesados da criação, por dentro do "criar avatar"
            GameObject clone = UnityEngine.Object.Instantiate(modelo.gameObject, raiz.transform);
            clone.name = "Modelo";
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            inicio = Desempenho.SomarDetalheEncadeado(Desempenho.Detalhe.CopiarPersonagem, inicio);

            int efeitos = RemoverEfeitosDoModelo(modelo.transform, clone.transform);
            int removidos = RemoverLogica(clone);
            Desempenho.SomarDetalhe(Desempenho.Detalhe.LimparClone, inicio);

            // Localiza no clone os três Animators equivalentes aos do jogador (corpo, chicote, lança).
            Animator[] originais = { modelo.Animator, modelo.LashAnimator, modelo.SpearAnimator };
            var animatorsClone = new Animator[originais.Length];
            for (int i = 0; i < originais.Length; i++)
            {
                if (originais[i] == null) continue;
                string caminho = CaminhoRelativo(modelo.transform, originais[i].transform);
                Transform t = caminho.Length == 0 ? clone.transform : clone.transform.Find(caminho);
                animatorsClone[i] = t != null ? t.GetComponent<Animator>() : null;
            }

            foreach (Animator a in clone.GetComponentsInChildren<Animator>(true))
            {
                a.fireEvents = false;            // eventos de animação chamariam lógica de ataque/som
                a.applyRootMotion = false;       // a posição vem da rede
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.keepAnimatorStateOnDisable = true;
            }

            // Registrar ANTES de ativar: a ativação já dispara OnStateEnter dos StateMachineBehaviours.
            Animator[] todos = clone.GetComponentsInChildren<Animator>(true);
            AvatarAnimatorGuard.Registrar(todos);

            RemotePlayerAvatar avatar = raiz.AddComponent<RemotePlayerAvatar>();
            Transform[] ossos = animatorsClone[0] != null ? BuscarOssosTronco(animatorsClone[0].transform) : new Transform[PlayerSnapshot.OssosTronco.Length];
            avatar.Inicializar(clone.transform, animatorsClone, ossos, todos);
            raiz.SetActive(true);

            int setas = 0;
            foreach (SpriteRenderer sr in clone.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!EhIconeDoMinimapa(sr)) continue;
                sr.color = CorSetaAliado;
                sr.sortingOrder -= 1; // a seta do próprio jogador fica por cima
                setas++;
            }

            int panos = 0;
            foreach (Component c in clone.GetComponentsInChildren<Component>(true))
            {
                if (c != null && c.GetType().FullName == "MagicaCloth2.MagicaCloth") panos++;
            }
            Plugin.Log.LogInfo($"[Avatar] '{raiz.name}' criado: {efeitos} efeito(s) de skill do jogador fora do clone, {removidos} componentes de lógica removidos, " +
                               $"{clone.GetComponentsInChildren<Renderer>(true).Length} renderers, {panos} panos com física e {setas} seta(s) de minimapa mantidos.");
            return avatar;
        }

        /// <summary>
        /// Física de tecido (MagicaCloth 2): capa, gibão, mangas, relógio. É simulação pura, sem lógica do
        /// jogo, e roda sozinha a partir do movimento dos ossos, então o clone a mantém.
        /// O DynamicClothController do jogo (que só liga/desliga panos durante algumas skills) sai.
        /// </summary>
        private static readonly HashSet<string> FisicaDeTecido = new HashSet<string>
        {
            "MagicaCloth2.MagicaCloth", "MagicaCloth2.MagicaPlaneCollider",
            "MagicaCloth2.MagicaCapsuleCollider", "MagicaCloth2.MagicaSphereCollider",
        };

        /// <summary>Cor da seta do aliado no minimapa (o próprio jogador continua com a seta original por cima).</summary>
        public static readonly Color CorSetaAliado = new Color(0.35f, 1f, 0.35f, 1f);

        private static int _layerIconeMinimapa = -2;
        private static int LayerIconeMinimapa => _layerIconeMinimapa != -2 ? _layerIconeMinimapa : (_layerIconeMinimapa = LayerMask.NameToLayer("MinimapIcon"));

        /// <summary>
        /// Ícone do minimapa: o Pajeú carrega um SpriteRenderer na layer "MinimapIcon" com a seta do jogador
        /// (IconType.Player é o único tipo que não é girado para a câmera: gira com o personagem).
        /// O clone o mantém, tingido de verde, como seta de direção do aliado.
        /// </summary>
        private static bool EhIconeDoMinimapa(Component c) => c is SpriteRenderer && LayerIconeMinimapa >= 0 && c.gameObject.layer == LayerIconeMinimapa;

        private static bool Manter(Component c) => Mantidos.Contains(c.GetType()) || FisicaDeTecido.Contains(c.GetType().FullName) || EhIconeDoMinimapa(c);

        /// <summary>
        /// Tira do clone os efeitos de skill presos no jogador na hora da cópia (adagas do Reflexos, orbes): sem a
        /// lógica eles ficariam parados no avatar, ao lado da cópia animada que o FxCompartilhados já manda.
        /// O Instantiate preserva a ordem dos filhos, então original e clone são percorridos juntos (de trás para frente,
        /// para a remoção não deslocar os índices que faltam).
        /// </summary>
        private static int RemoverEfeitosDoModelo(Transform original, Transform copia)
        {
            int n = 0;
            for (int i = Mathf.Min(original.childCount, copia.childCount) - 1; i >= 0; i--)
            {
                Transform o = original.GetChild(i), c = copia.GetChild(i);
                if (Fase3.FxCompartilhados.EhEfeitoDoJogador(o.gameObject))
                {
                    UnityEngine.Object.DestroyImmediate(c.gameObject); // o clone nunca foi ativado: nenhum OnDestroy roda
                    n++;
                }
                else n += RemoverEfeitosDoModelo(o, c);
            }
            return n;
        }

        private static int RemoverLogica(GameObject clone)
        {
            int total = 0;
            // Várias passadas: um componente com [RequireComponent] só sai depois de quem depende dele.
            for (int passada = 0; passada < 6; passada++)
            {
                int nesta = 0;
                // Ordem: scripts (MonoBehaviour), depois outros Behaviours (Canvas, Light...), depois o resto.
                // Assim quem tem [RequireComponent] sai antes de quem é requerido (ex.: CanvasScaler antes de Canvas).
                for (int categoria = 0; categoria < 3; categoria++)
                {
                    foreach (Component c in clone.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || Manter(c)) continue;
                        int cat = c is MonoBehaviour ? 0 : c is Behaviour ? 1 : 2;
                        if (cat != categoria) continue;
                        if (c is ParticleSystemRenderer) continue; // sai junto/depois do ParticleSystem
                        UnityEngine.Object.DestroyImmediate(c);
                        if (c == null) nesta++; // se outro componente depende dele, sai numa próxima passada
                    }
                }
                foreach (ParticleSystemRenderer r in clone.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    if (r.GetComponent<ParticleSystem>() == null)
                    {
                        UnityEngine.Object.DestroyImmediate(r);
                        nesta++;
                    }
                }
                total += nesta;
                if (nesta == 0) break;
            }
            return total;
        }

        /// <summary>Localiza os ossos de PlayerSnapshot.OssosTronco abaixo de <paramref name="raiz"/> (null se não houver).</summary>
        public static Transform[] BuscarOssosTronco(Transform raiz)
        {
            var ossos = new Transform[PlayerSnapshot.OssosTronco.Length];
            for (int i = 0; i < ossos.Length; i++)
            {
                ossos[i] = BuscarPorNome(raiz, PlayerSnapshot.OssosTronco[i]);
            }
            return ossos;
        }

        private static Transform BuscarPorNome(Transform t, string nome)
        {
            if (t.name == nome) return t;
            foreach (Transform filho in t)
            {
                Transform achado = BuscarPorNome(filho, nome);
                if (achado != null) return achado;
            }
            return null;
        }

        internal static string CaminhoRelativo(Transform raiz, Transform alvo)
        {
            if (alvo == raiz) return string.Empty;
            var partes = new List<string>();
            for (Transform t = alvo; t != null && t != raiz; t = t.parent)
            {
                partes.Add(t.name);
            }
            partes.Reverse();
            return string.Join("/", partes);
        }

        /// <summary>Texto com a hierarquia e os componentes do jogador, para decidir o que clonar.</summary>
        public static string DescreverHierarquia(Transform raiz, int profundidadeMaxima = 8)
        {
            var sb = new StringBuilder();
            Descrever(raiz, 0, profundidadeMaxima, sb);
            return sb.ToString();
        }

        private static void Descrever(Transform t, int nivel, int max, StringBuilder sb)
        {
            sb.Append(' ', nivel * 2).Append(t.gameObject.activeSelf ? "+ " : "- ").Append(t.name).Append("  [");
            Component[] comps = t.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(comps[i] == null ? "(script ausente)" : comps[i].GetType().Name);
            }
            sb.Append("]  layer=").Append(LayerMask.LayerToName(t.gameObject.layer)).AppendLine();
            if (nivel >= max)
            {
                if (t.childCount > 0) sb.Append(' ', (nivel + 1) * 2).Append("... ").Append(t.childCount).AppendLine(" filhos");
                return;
            }
            foreach (Transform filho in t)
            {
                Descrever(filho, nivel + 1, max, sb);
            }
        }
    }
}
