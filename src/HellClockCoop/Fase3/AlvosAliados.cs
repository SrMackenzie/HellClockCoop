using System;
using System.Collections.Generic;
using HarmonyLib;
using HellClock.Character;
using HellClock.Character.Enemy;
using HellClock.Player;
using HellClock.Stats;
using HellClock.Status;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Alvo de inimigo que representa o avatar de um aliado (ITarget do jogo).
    /// No host, entra na lista de alvos do EnemyManager: os inimigos perseguem e atacam quem estiver
    /// mais perto, como fazem com os companheiros. O avatar não tem colisor, então o golpe não acerta
    /// nada no host; quem leva o dano é o convidado, quando a réplica repete a skill no jogo dele.
    /// No convidado, serve de alvo para a réplica que ataca o host ou outro convidado.
    /// </summary>
    internal sealed class AlvoAvatar : ITarget
    {
        public readonly ulong Steam;
        public readonly string Nome;
        public RemotePlayerAvatar Avatar;
        private readonly LifeController _vida;

        public AlvoAvatar(ulong steam, string nome, RemotePlayerAvatar avatar, LifeController vida)
        {
            Steam = steam; Nome = nome; Avatar = avatar; _vida = vida;
        }

        public Transform Transform => Avatar != null ? Avatar.transform : null;
        public LifeController LifeController => _vida;
        public StatusController StatusController => null;
        public ETargetType ETargetType => ETargetType.None;
        public bool Exists() => Avatar != null && Avatar.gameObject.activeInHierarchy;
        public bool CanBeTargeted() => Exists() && !_vida.IsDead;
        public bool IsInteractable() => false;
        public bool CanHold() => false;
        public Vector3 GetPosition() => Avatar != null ? Avatar.transform.position : Vector3.zero;
        public void BecomeSelected() { }
        public void BecomeUnselected() { }
        public string GetFullName() => Nome;
        public string GetShortName() => Nome;
        public string GetSubName() => "";
        public string GetDescription() => "";

        /// <summary>Morto: os inimigos largam o alvo (EnemyAIController testa LifeController.IsDead).</summary>
        public void DefinirMorto(bool morto)
        {
            if (morto == _vida.IsDead) return;
            if (morto) AlvosAliados.VidaAtualRef(_vida).Value = 0.0;
            else _vida.Refill();
        }
    }

    /// <summary>
    /// Alvo: EnemyManager.TryToGetTargetForEnemy(EnemyController) (privado) — com alguém caído, o inimigo escolhe o alvo vivo
    /// mais perto em vez do caído (AlvosAliados.EscolherAlvoVivo, 0.31.6).
    /// </summary>
    [HarmonyPatch(typeof(EnemyManager), "TryToGetTargetForEnemy")]
    internal static class Alvos_TryToGetTargetForEnemy
    {
        private static bool _avisou;

        private static bool Prefix(EnemyManager __instance, EnemyController enemy)
        {
            try { return !AlvosAliados.EscolherAlvoVivo(__instance, enemy); }
            catch (Exception e)
            {
                if (!_avisou) Plugin.Log.LogWarning($"[Alvos] Escolha de alvo vivo falhou; fica a do jogo: {(e.InnerException ?? e).Message}");
                _avisou = true; // roda por inimigo a cada intervalo: o aviso sai uma vez
                return true;
            }
        }
    }

    internal static class AlvosAliados
    {
        internal static readonly AccessTools.FieldRef<StatBasedValueController, DoubleState> VidaAtualRef =
            AccessTools.FieldRefAccess<StatBasedValueController, DoubleState>("_currentValue");

        private static readonly Dictionary<ulong, AlvoAvatar> Alvos = new Dictionary<ulong, AlvoAvatar>();
        private static readonly HashSet<ulong> Registrados = new HashSet<ulong>();
        private static EnemyManager _emDosRegistros;
        private static float _proximaChecagem;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;

        /// <summary>Alvo do avatar de um par (cria na primeira vez; null se ainda não há inimigo para copiar a ficha).</summary>
        internal static AlvoAvatar De(Par p)
        {
            if (p == null || p.Avatar == null) return null;
            if (Alvos.TryGetValue(p.Id.m_SteamID, out AlvoAvatar a))
            {
                a.Avatar = p.Avatar;
                return a;
            }
            LifeController vida = CriarVida();
            if (vida == null) return null;
            a = new AlvoAvatar(p.Id.m_SteamID, p.Nome, p.Avatar, vida);
            Alvos[p.Id.m_SteamID] = a;
            return a;
        }

        /// <summary>
        /// A ficha (CharacterSheet) do alvo é uma cópia da ficha base de um inimigo qualquer: o jogo só
        /// precisa de um LifeController vivo/morto. A ficha do jogador não serve (PlayerSheet liga pedras
        /// da alma, níveis etc.).
        /// </summary>
        private static LifeController CriarVida()
        {
            EnemyManager em = EnemyManager.Instance;
            if (em == null) return null;
            foreach (EnemyController e in em.Enemies)
            {
                if (e == null || e.BaseSheet == null) continue;
                try
                {
                    CharacterSheet ficha = UnityEngine.Object.Instantiate(e.BaseSheet);
                    ficha.Setup();
                    return new LifeController(ficha, new global::Semaphore(), null);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Alvos] Ficha do alvo não criada: {(ex.InnerException ?? ex).Message}");
                    return null;
                }
            }
            return null;
        }

        /// <summary>Qual jogador um alvo do jogo representa: (AlvoHost, 0), (AlvoConvidado, steam) ou (AlvoNenhum, 0).</summary>
        internal static byte Identificar(ITarget alvo, out ulong steam)
        {
            steam = 0;
            if (alvo == null) return InimigoSkill.AlvoNenhum;
            if (alvo is AlvoAvatar a)
            {
                steam = a.Steam;
                return InimigoSkill.AlvoConvidado;
            }
            PlayerController local = CoopRuntime.JogadorLocal();
            if (local != null && alvo.Transform == local.transform) return InimigoSkill.AlvoHost;
            return InimigoSkill.AlvoNenhum;
        }

        /// <summary>No host: põe/tira os avatares dos aliados na lista de alvos dos inimigos.</summary>
        public static void Atualizar()
        {
            if (Sessao == null || !RunCompartilhada.Host || Time.unscaledTime < _proximaChecagem) return;
            _proximaChecagem = Time.unscaledTime + 0.5f;
            EnemyManager em = EnemyManager.Instance;
            if (em == null) return;
            if (em != _emDosRegistros)
            {
                Registrados.Clear(); // EnemyManager novo, lista de alvos nova
                _emDosRegistros = em;
            }
            foreach (Par p in Sessao.Pares.Values)
            {
                ulong id = p.Id.m_SteamID;
                bool deveria = p.Compativel && p.MesmoAndar && p.Avatar != null && p.Avatar.gameObject.activeInHierarchy;
                AlvoAvatar a = deveria ? De(p) : (Alvos.TryGetValue(id, out AlvoAvatar x) ? x : null);
                if (a == null) continue;
                a.DefinirMorto(p.Morto);
                if (deveria && !Registrados.Contains(id))
                {
                    em.AddToAvailableTargets(a);
                    Registrados.Add(id);
                    Plugin.Log.LogInfo($"[Alvos] {p.Nome} agora pode ser atacado pelos inimigos.");
                }
                else if (!deveria && Registrados.Contains(id))
                {
                    em.RemoveFromAvailableTargets(a);
                    Registrados.Remove(id);
                }
            }
            // Quem saiu da sessão
            foreach (ulong id in new List<ulong>(Registrados))
            {
                if (Sessao.Pares.ContainsKey(id)) continue;
                if (Alvos.TryGetValue(id, out AlvoAvatar a)) em.RemoveFromAvailableTargets(a);
                Registrados.Remove(id);
                Alvos.Remove(id);
            }
        }

        private static readonly AccessTools.FieldRef<EnemyManager, List<ITarget>> DisponiveisRef =
            AccessTools.FieldRefAccess<EnemyManager, List<ITarget>>("_availableTargets");

        /// <summary>
        /// Prefixo de EnemyManager.TryToGetTargetForEnemy (0.31.6). O jogo escolhe o alvo mais perto da lista sem olhar se está
        /// morto: no solo o jogador morto acaba a run e o companheiro morto sai da lista. No coop o aliado caído (avatar com vida
        /// 0) e o host caído ficam nela, e o inimigo perto deles pega o caído, o UpdateAI larga (alvo morto) e no intervalo
        /// seguinte pega de novo: não ataca mais ninguém (chefe sem agro no host depois da queda do aliado). Mesma escolha do
        /// jogo, pulando quem está morto. True = escolheu aqui; false = roda a do jogo (fora do coop, no convidado e sem caído).
        /// </summary>
        internal static bool EscolherAlvoVivo(EnemyManager em, EnemyController inimigo)
        {
            if (Sessao == null || !Sessao.EmSessao || RunCompartilhada.Convidado || inimigo == null) return false;
            List<ITarget> alvos = DisponiveisRef(em);
            if (alvos == null) return false;
            bool algumMorto = false;
            foreach (ITarget t in alvos)
            {
                if (t?.LifeController != null && t.LifeController.IsDead) { algumMorto = true; break; }
            }
            if (!algumMorto) return false;
            HellClock.AI.Enemy.EnemyAIController ia = inimigo.EnemyAIController;
            if (ia == null) return false;
            Vector3 pos = inimigo.Position;
            float menor = 3.4e38f;
            ITarget melhor = null;
            foreach (ITarget t in alvos)
            {
                if (t == null || t.LifeController == null || t.LifeController.IsDead) continue;
                if (!ia.ForceAggressive && !ia.IsTargetInVisionRadius(t)) continue;
                float d = global::HellClock.Extensions.VectorExtensions.Distance2D(t.GetPosition(), pos);
                if (d < menor) { menor = d; melhor = t; }
            }
            if (melhor != null) ia.SetCurrentTarget(melhor);
            else ia.ResetCanReceiveNewTargetTimer();
            return true;
        }

        public static void Encerrar()
        {
            EnemyManager em = EnemyManager.Instance;
            if (em != null)
            {
                foreach (ulong id in Registrados)
                {
                    if (Alvos.TryGetValue(id, out AlvoAvatar a)) em.RemoveFromAvailableTargets(a);
                }
            }
            Registrados.Clear();
            Alvos.Clear();
        }
    }
}
