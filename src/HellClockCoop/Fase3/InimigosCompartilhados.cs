using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HellClock.Character;
using HellClock.Character.Enemy;
using HellClock.Damage;
using HellClock.Level;
using HellClock.Manager;
using HellClock.Player;
using HellClock.Skill;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;
using UnityEngine.AI;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Fase 3a — os inimigos são do host; o convidado vê réplicas e bate nelas.
    ///
    /// Host: dá um número (netId) a cada inimigo que nasce na run (EnemyManager.SpawnEnemy) e manda
    /// aos convidados a definição, o tipo e os afixos já sorteados. A 10 Hz manda posição, rotação,
    /// vida e animação dos inimigos perto de algum aliado. Morte e sumiço vão na hora. Aplica nos seus
    /// inimigos o dano que os convidados relatam e acorda inimigos quando um aliado chega perto.
    ///
    /// Convidado: cria a réplica pelo próprio EnemyManager.SpawnEnemy (liberado só para isso), sem
    /// sorteio de afixos, e desliga a IA (EnemyController.Update pulado). O dano que ele causa é
    /// relatado ao host; localmente a réplica nunca morre por conta própria: só quando o host manda.
    /// A morte local dispara o OnAfterEnemyKilled do jogo, então loot e XP do convidado saem dele.
    /// </summary>
    internal static class InimigosCompartilhados
    {
        private const float IntervaloEstado = 0.1f;      // 10 Hz
        private const float IntervaloGolpes = 0.05f;     // 20 Hz
        private const float RaioRelevancia = 45f;
        private const int MaximoPorTick = 160;
        private const int BytesPorPacote = 1100;
        private const int CriacoesPorQuadro = 25;
        private const float JanelaGolpeLocal = 0.5f;     // depois de um golpe local, só aceita vida menor do host

        public static SessaoCoop Sessao => RunCompartilhada.Sessao;
        private static bool Host => RunCompartilhada.Host;
        private static bool Convidado => RunCompartilhada.Convidado;
        private const float ValidadePendente = 90f;      // spawn recebido e nunca criado (run que não começou) expira

        /// <summary>
        /// Run em andamento no host. O primeiro andar nasce antes de RunStarted virar true (StartRun só
        /// liga a flag depois do ChangeLevel), por isso conta também "carregando um andar sorteado".
        /// </summary>
        private static bool HostEmRun
        {
            get
            {
                DungeonProgressManager d = DungeonProgressManager.Instance;
                if (d == null) return false;
                return d.RunStarted.Value || (RunCompartilhada.TemPlano && ClassSingleton<LevelChanger>.Instance.IsChangingLevel.Value);
            }
        }

        // Membros privados do jogo
        private static readonly AccessTools.FieldRef<EnemyController, bool> NasceuRef = AccessTools.FieldRefAccess<EnemyController, bool>("_spawned");
        private static readonly AccessTools.FieldRef<EnemyManager, bool> IgnorarLimpezaRef = AccessTools.FieldRefAccess<EnemyManager, bool>("_ignoreLevelClear");
        private static readonly AccessTools.FieldRef<EnemyManager, EventSlot> AndarLimpoRef = AccessTools.FieldRefAccess<EnemyManager, EventSlot>("_onAllEnemiesCleared");
        private static readonly Func<HellClock.AI.Enemy.EnemyAIController, BoolState> AgressivoDe = CriarLeitorAgressivo();

        private static Func<HellClock.AI.Enemy.EnemyAIController, BoolState> CriarLeitorAgressivo()
        {
            // EnemyAIController.IsAggressive é internal: delegate no getter (sem ele, a réplica só não fica agressiva).
            try
            {
                System.Reflection.MethodInfo get = AccessTools.PropertyGetter(typeof(HellClock.AI.Enemy.EnemyAIController), "IsAggressive");
                return get == null ? null : AccessTools.MethodDelegate<Func<HellClock.AI.Enemy.EnemyAIController, BoolState>>(get);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Inimigos] IsAggressive indisponível: {(e.InnerException ?? e).Message}");
                return null;
            }
        }
        private static readonly AccessTools.FieldRef<StatBasedValueController, DoubleState> VidaAtualRef = AccessTools.FieldRefAccess<StatBasedValueController, DoubleState>("_currentValue");

        // ================= Host =================

        private sealed class InimigoHost
        {
            public ushort Id;
            public EnemyController C;
            public InimigoSpawn Spawn;
            public readonly AnimatorFrame Animacao = new AnimatorFrame();
        }

        private static readonly Dictionary<EnemyController, InimigoHost> HostPorControle = new Dictionary<EnemyController, InimigoHost>();
        private static readonly Dictionary<ushort, InimigoHost> HostPorId = new Dictionary<ushort, InimigoHost>();
        private static ushort _proximoId = 1;
        private static float _proximoEstado;
        private static uint _sequencia;
        private static EnemyManager _emComLimpeza;
        private static readonly List<Vector3> PosicoesAliados = new List<Vector3>(3);
        private static readonly List<KeyValuePair<float, InimigoHost>> Relevantes = new List<KeyValuePair<float, InimigoHost>>(256);

        /// <summary>Andar da run em que o host está (ou carregando).</summary>
        private static uint AndarDoHost => LocalPlayerSampler.AndarAtual()?.LevelIndex ?? RunCompartilhada.AndarPlanejado;

        internal static void HostNasceu(EnemyController c, SpawnEnemyData data)
        {
            if (!Host || c == null || !HostEmRun) return;
            if (HostPorControle.ContainsKey(c)) HostEsquecer(c); // vindo do pool sem passar por Dispose
            ushort id = _proximoId++;
            if (_proximoId == 0) _proximoId = 1;
            EnemyTypeConfig cfg = data.EnemyTypeConfig;
            var afixos = new List<int>();
            if (cfg?.Affixes != null)
            {
                foreach (PassiveSkillDefinition a in cfg.Affixes)
                {
                    if (a != null) afixos.Add(a.HashID);
                }
            }
            var s = new InimigoSpawn
            {
                Id = id,
                Andar = AndarDoHost,
                DefinicaoHash = data.EnemyDefinition.HashID,
                Tipo = (byte)(cfg?.EEnemyType ?? EEnemyType.Regular),
                ArquetipoHash = cfg?.ChampionEnemyArchetypeDefinition != null ? cfg.ChampionEnemyArchetypeDefinition.HashID : 0,
                Afixos = afixos.ToArray(),
                Sobrevivencia = cfg != null && cfg.IsSurvivalEnemy,
                ParDeBoss = cfg != null && cfg.IsBossPair,
                BarraDeVida = data.InstantiateLifeView,
                EfeitoSpawn = (byte)data.SpawnBehaviourConfig.SpawnFX,
                OrigemSpawn = data.SpawnBehaviourConfig.SpawnOrigin,
                AtrasoSpawn = data.SpawnBehaviourConfig.DelayToSpawn,
                Posicao = c.transform.position,
                Yaw = c.transform.eulerAngles.y,
            };
            var h = new InimigoHost { Id = id, C = c, Spawn = s };
            HostPorControle[c] = h;
            HostPorId[id] = h;
            if (Sessao.ComAliados) Sessao.EnviarParaConvidados(s.Serializar(), confiavel: true);
        }

        /// <summary>Postfix de ActiveSkill.Execute no host: inimigo da run usou uma skill.</summary>
        internal static void HostUsouSkill(ActiveSkill skill, ITarget alvo)
        {
            if (!Host || skill == null || !(skill.SkillCaster is EnemyController c) || !HostPorControle.TryGetValue(c, out InimigoHost h)) return;
            if (!Sessao.ComAliados || skill.SkillDefinition == null) return;
            var m = new InimigoSkill { Id = h.Id, SkillHash = skill.SkillDefinition.HashID };
            m.TipoAlvo = AlvosAliados.Identificar(alvo, out m.SteamAlvo);
            m.PosicaoAlvo = alvo != null ? alvo.GetPosition() : c.transform.position + c.transform.forward * 3f;
            Sessao.EnviarParaConvidados(m.Serializar(), confiavel: true);
        }

        internal static void HostAjustou(EnemyController c, byte ajuste)
        {
            if (!Host || c == null || !HostPorControle.TryGetValue(c, out InimigoHost h)) return;
            h.Spawn.Ajustes |= ajuste;
            if (Sessao.ComAliados) Sessao.EnviarParaConvidados(new InimigoAjuste { Id = h.Id, Ajuste = ajuste }.Serializar(), confiavel: true);
        }

        internal static void HostMorreu(IEnemy inimigo)
        {
            if (!Host || !(inimigo is EnemyController c) || !HostPorControle.TryGetValue(c, out InimigoHost h)) return;
            HostEsquecer(c);
            bool semDrop = DropManager.Instance != null && !DropManager.Instance.IgnoreAllDrops.Free;
            if (Sessao.ComAliados) Sessao.EnviarParaConvidados(new InimigoMorte { Id = h.Id, SemDrop = semDrop }.Serializar(), confiavel: true);
        }

        internal static void HostDescartou(IEnemy inimigo)
        {
            if (!Host || !(inimigo is EnemyController c) || !HostPorControle.TryGetValue(c, out InimigoHost h)) return;
            HostEsquecer(c);
            // Na troca de andar o convidado descarta as réplicas sozinho; não precisa de 200 mensagens.
            if (ClassSingleton<LevelChanger>.Instance.IsChangingLevel.Value) return;
            if (Sessao.ComAliados) Sessao.EnviarParaConvidados(new InimigoSumiu { Id = h.Id }.Serializar(), confiavel: true);
        }

        private static void HostEsquecer(EnemyController c)
        {
            if (HostPorControle.TryGetValue(c, out InimigoHost h))
            {
                HostPorControle.Remove(c);
                HostPorId.Remove(h.Id);
            }
        }

        /// <summary>Quem entrou no meio da run recebe os inimigos vivos do andar atual.</summary>
        internal static void AoNovoConvidado(SessaoCoop sessao, Par p)
        {
            if (!HostEmRun) return;
            int n = 0;
            foreach (InimigoHost h in HostPorId.Values)
            {
                if (h.C == null || h.C.LifeController.IsDead) continue;
                AtualizarSpawn(h);
                sessao.EnviarPara(p, h.Spawn.Serializar());
                n++;
            }
            Plugin.Log.LogInfo($"[Inimigos] {n} inimigos enviados a {p.Nome} (entrada no meio da run).");
        }

        private static void AtualizarSpawn(InimigoHost h)
        {
            h.Spawn.Posicao = h.C.transform.position;
            h.Spawn.Yaw = h.C.transform.eulerAngles.y;
            h.Spawn.JaNasceu = NasceuRef(h.C);
            h.Spawn.Vida = h.C.LifeController.CurrentRatio;
        }

        private static bool EstaAgressivo(EnemyController c)
        {
            try { return AgressivoDe != null && c.EnemyAIController != null && (AgressivoDe(c.EnemyAIController)?.Value ?? false); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Réplica fica "agressiva" quando o inimigo do host fica: dispara o que o jogo registra nessa hora
        /// (no chefe, EnemyBossController.OnAggressive libera o dano, a barra de vida e a câmera do chefe).
        /// </summary>
        private static void TornarAgressivo(Replica r)
        {
            if (r.Agressivo || AgressivoDe == null || r.C == null || r.C.EnemyAIController == null) return;
            r.Agressivo = true;
            try { AgressivoDe(r.C.EnemyAIController)?.Setter(true); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Inimigos] Réplica {r.Id} agressiva falhou: {(e.InnerException ?? e).Message}"); }
        }

        /// <summary>Inimigos dormentes acordam também quando um aliado chega perto (no jogo, só o jogador local).</summary>
        internal static bool HostAcordarPorAliado(EnemyController c, Vector3 origem)
        {
            if (!Host || !HostEmRun || !Sessao.ComAliados || c.CharacterEffectsController == null || c.CharacterEffectsController.IsSpawning) return false;
            float limite = EnemyManager.Instance.SpawnDistanceToTrigger;
            foreach (Par p in Sessao.Pares.Values)
            {
                if (p.Avatar == null || !p.Avatar.gameObject.activeSelf || !p.MesmoAndar) continue;
                Vector3 d = p.Avatar.transform.position - origem;
                d.y = 0f;
                if (d.sqrMagnitude <= limite * limite)
                {
                    c.CharacterEffectsController.PlaySpawnFX();
                    return true;
                }
            }
            return false;
        }

        private static void AtualizarHost()
        {
            EnemyManager em = EnemyManager.Instance;
            if (em != null && em != _emComLimpeza)
            {
                _emComLimpeza = em;
                em.OnAllEnemiesCleared.Register(HostAndarLimpo);
            }
            if (!HostEmRun)
            {
                if (HostPorId.Count > 0) { HostPorId.Clear(); HostPorControle.Clear(); }
                return;
            }
            if (Time.unscaledTime < _proximoEstado || !Sessao.ComAliados) return;
            _proximoEstado = Time.unscaledTime + IntervaloEstado;

            PosicoesAliados.Clear();
            foreach (Par p in Sessao.Pares.Values)
            {
                if (p.Compativel && p.DlcOk && p.MesmoAndar && p.Avatar != null && p.Avatar.gameObject.activeSelf) PosicoesAliados.Add(p.Avatar.transform.position);
            }
            if (PosicoesAliados.Count == 0) return;

            Relevantes.Clear();
            List<EnemyController> perdidos = null;
            foreach (InimigoHost h in HostPorId.Values)
            {
                if (h.C == null || !h.C.gameObject.activeInHierarchy)
                {
                    (perdidos ?? (perdidos = new List<EnemyController>())).Add(h.C);
                    continue;
                }
                Vector3 pos = h.C.transform.position;
                float menor = 3.4e38f; // float.MaxValue foi removido do mscorlib do jogo
                foreach (Vector3 a in PosicoesAliados) menor = Mathf.Min(menor, (a - pos).sqrMagnitude);
                if (menor <= RaioRelevancia * RaioRelevancia) Relevantes.Add(new KeyValuePair<float, InimigoHost>(menor, h));
            }
            if (perdidos != null)
            {
                foreach (EnemyController c in perdidos)
                {
                    if (c == null) LimparNulos();
                    else HostDescartou(c);
                }
            }
            if (Relevantes.Count == 0) return;
            Relevantes.Sort((a, b) => a.Key.CompareTo(b.Key));

            var itens = new List<byte[]>(Mathf.Min(Relevantes.Count, MaximoPorTick));
            var item = new InimigosEstado.Item();
            using (var ms = new MemoryStream(64))
            using (var w = new BinaryWriter(ms))
            {
                for (int k = 0; k < Relevantes.Count && k < MaximoPorTick; k++)
                {
                    InimigoHost h = Relevantes[k].Value;
                    item.Id = h.Id;
                    item.Posicao = h.C.transform.position;
                    item.Yaw = h.C.transform.eulerAngles.y;
                    item.Vida = h.C.LifeController.CurrentRatio;
                    item.JaNasceu = NasceuRef(h.C);
                    item.Agressivo = EstaAgressivo(h.C);
                    Animator an = h.C.Animator;
                    if (an != null && an.isActiveAndEnabled && an.runtimeAnimatorController != null)
                    {
                        AnimatorSync.Ler(an, h.Animacao);
                        item.Animacao = h.Animacao;
                    }
                    else item.Animacao = null;
                    ms.SetLength(0);
                    InimigosEstado.EscreverItem(w, item);
                    w.Flush();
                    itens.Add(ms.ToArray());
                }
            }
            EnviarEmPacotes(itens);
        }

        private static void EnviarEmPacotes(List<byte[]> itens)
        {
            int i = 0;
            while (i < itens.Count)
            {
                using (var ms = new MemoryStream(BytesPorPacote + 64))
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(Protocolo.TipoInimigosEstado);
                    w.Write(_sequencia++);
                    long posContagem = ms.Position;
                    w.Write((byte)0);
                    int n = 0;
                    while (i < itens.Count && n < 255 && (n == 0 || ms.Length + itens[i].Length <= BytesPorPacote))
                    {
                        w.Write(itens[i]);
                        i++;
                        n++;
                    }
                    w.Flush();
                    ms.Position = posContagem;
                    ms.WriteByte((byte)n);
                    Sessao.EnviarParaConvidados(ms.ToArray(), confiavel: false);
                }
            }
        }

        private static void LimparNulos()
        {
            var mortos = new List<ushort>();
            foreach (var kv in HostPorId)
            {
                if (kv.Value.C == null) mortos.Add(kv.Key);
            }
            foreach (ushort id in mortos)
            {
                HostPorId.Remove(id);
                if (Sessao.ComAliados) Sessao.EnviarParaConvidados(new InimigoSumiu { Id = id }.Serializar(), confiavel: true);
            }
            var chaves = new List<EnemyController>();
            foreach (EnemyController c in HostPorControle.Keys) if (c == null) chaves.Add(c);
            foreach (EnemyController c in chaves) HostPorControle.Remove(c);
        }

        private static void HostAndarLimpo()
        {
            if (!Host || !HostEmRun || !Sessao.ComAliados) return;
            Sessao.EnviarParaConvidados(new AndarLimpo { Andar = AndarDoHost }.Serializar(), confiavel: true);
        }

        private static void HostAplicarGolpes(Par de, Golpes g)
        {
            foreach (Golpes.Item i in g.Itens)
            {
                if (!HostPorId.TryGetValue(i.Id, out InimigoHost h) || h.C == null || h.C.LifeController.IsDead || i.Valor <= 0f) continue;
                DamageInstance dano = DamageInstance.RentDummy(i.Valor, EDamageTypeDefinition.PHYSICAL, hideFromUI: true);
                try
                {
                    if (i.MascaraTipos != 0) dano.DamageTypesBitMask = i.MascaraTipos;
                    dano.TotalCrits = i.Criticos;
                    h.C.LifeController.Hurt(dano);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Inimigos] Golpe de {de.Nome} no inimigo {i.Id} falhou: {(e.InnerException ?? e).Message}");
                }
                finally
                {
                    dano.Dispose();
                }
            }
        }

        // ================= Convidado =================

        private sealed class Replica
        {
            public ushort Id;
            public EnemyController C;
            public Vector3 Posicao;
            public float Yaw;
            public float VidaHost = 1f;
            public bool Nasceu;
            public bool TocouSpawn;
            public float UltimoGolpe = -10f;
            public AnimatorFrame Animacao;
            public bool AnimacaoNova;
            public bool RootMotionOriginal;
            public float SkillAte;
            public ActiveSkill UltimaSkill;
            public bool Agressivo;
            public bool QuerAgressivo;
        }

        private static readonly Dictionary<ushort, Replica> Replicas = new Dictionary<ushort, Replica>();
        private static readonly Dictionary<LifeController, Replica> PorVida = new Dictionary<LifeController, Replica>();
        private static readonly HashSet<EnemyController> Controles = new HashSet<EnemyController>();
        private static readonly Dictionary<ushort, InimigoSpawn> Pendentes = new Dictionary<ushort, InimigoSpawn>();
        private static readonly Dictionary<ushort, float> RecebidoEm = new Dictionary<ushort, float>();
        private static readonly Dictionary<long, Golpes.Item> GolpesAcumulados = new Dictionary<long, Golpes.Item>();
        private static readonly object ChaveSemAfixoExtra = new object();
        private static readonly object ChaveSemDrop = new object();
        private static float _proximosGolpes;
        private static bool _limpezaDesligada;
        private static bool _avisouDefinicao;

        /// <summary>Libera o EnemyManager.SpawnEnemy bloqueado no convidado pela Fase 2.</summary>
        internal static bool CriandoReplica { get; private set; }

        /// <summary>Morte, descarte ou dano vindos do host: não relatar de volta.</summary>
        internal static bool AplicandoOrdem { get; private set; }

        internal static bool EhReplica(EnemyController c) => Controles.Count > 0 && Controles.Contains(c);

        /// <summary>netId de um inimigo (do host, no host; da réplica, no convidado). false se não for compartilhado.</summary>
        internal static bool IdDe(EnemyController c, out ushort id)
        {
            id = 0;
            if (c == null) return false;
            if (HostPorControle.TryGetValue(c, out InimigoHost h)) { id = h.Id; return true; }
            if (PorVida.TryGetValue(c.LifeController, out Replica r)) { id = r.Id; return true; }
            return false;
        }

        /// <summary>Inimigo compartilhado pelo netId (host ou réplica).</summary>
        internal static EnemyController PorId(ushort id)
        {
            if (HostPorId.TryGetValue(id, out InimigoHost h) && h.C != null) return h.C;
            if (Replicas.TryGetValue(id, out Replica r) && r.C != null) return r.C;
            return null;
        }

        private static void AtualizarConvidado()
        {
            EnemyManager em = EnemyManager.Instance;
            if (!Convidado || em == null)
            {
                if (Replicas.Count > 0 || Pendentes.Count > 0) LimparConvidado("fora do coop");
                DevolverLimpeza(em);
                return;
            }
            ExpirarPendentes();

            LevelInfo andar = LocalPlayerSampler.AndarAtual();
            if (andar == null) return; // carregando: o próprio jogo descarta as réplicas (DisposeEnemy)
            if (andar.LevelBlockInfo == null)
            {
                // Hub. Os spawns pendentes ficam: a run do host pode estar começando.
                if (Replicas.Count > 0) LimparReplicas("voltou ao hub");
                DevolverLimpeza(em);
                return;
            }
            // Com réplicas, a lista local zera por motivos do mod (inimigo longe, ordem do host).
            // O "andar limpo" do convidado passa a vir do host (AndarLimpo).
            IgnorarLimpezaRef(em) = true;
            _limpezaDesligada = true;

            if (Pendentes.Count > 0) CriarPendentes(em, andar.LevelIndex);

            float dt = Time.deltaTime;
            List<ushort> sumidos = null;
            foreach (Replica r in Replicas.Values)
            {
                if (r.C == null || !r.C.gameObject.activeInHierarchy || r.C.LifeController.IsDead)
                {
                    (sumidos ?? (sumidos = new List<ushort>())).Add(r.Id);
                    continue;
                }
                Mover(r, dt);
                if (r.AnimacaoNova && r.Animacao != null)
                {
                    r.AnimacaoNova = false;
                    // Enquanto a réplica executa a própria skill, a animação é dela (o golpe sai dos eventos).
                    // (no máximo 3 s: uma skill que nunca termina na réplica não pode congelar a animação)
                    bool usandoSkill = Time.unscaledTime < r.SkillAte || (r.UltimaSkill != null && r.UltimaSkill.IsUsing.Value && Time.unscaledTime < r.SkillAte + 3f);
                    if (!usandoSkill) AnimatorSync.Aplicar(r.C.Animator, r.Animacao);
                }
                AplicarVida(r);
                if (r.QuerAgressivo && !r.Agressivo) TornarAgressivo(r);
                // SetupSpawn deixa IsSpawning=true esperando o PlaySpawnFX; o host já tocou o dele.
                if (r.Nasceu && !r.TocouSpawn && !NasceuRef(r.C) && r.C.CharacterEffectsController != null)
                {
                    r.TocouSpawn = true;
                    r.C.CharacterEffectsController.PlaySpawnFX();
                }
            }
            if (sumidos != null)
            {
                foreach (ushort id in sumidos) Esquecer(id);
            }

            if (GolpesAcumulados.Count > 0 && Time.unscaledTime >= _proximosGolpes)
            {
                _proximosGolpes = Time.unscaledTime + IntervaloGolpes;
                var g = new Golpes();
                g.Itens.AddRange(GolpesAcumulados.Values);
                GolpesAcumulados.Clear();
                Par host = Sessao.Host;
                if (host != null) Sessao.EnviarPara(host, g.Serializar());
            }
        }

        private static void Mover(Replica r, float dt)
        {
            Transform t = r.C.transform;
            Vector3 atual = t.position;
            if ((r.Posicao - atual).sqrMagnitude > 36f) t.position = r.Posicao; // mais de 6 m: teleporta
            else t.position = Vector3.Lerp(atual, r.Posicao, 1f - Mathf.Exp(-12f * dt));
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, r.Yaw, 0f), 1f - Mathf.Exp(-14f * dt));
            NavMeshAgent agente = r.C.MovementController?.Agent;
            if (agente != null && agente.enabled && agente.isOnNavMesh) agente.nextPosition = t.position;
        }

        private static void AplicarVida(Replica r)
        {
            LifeController vida = r.C.LifeController;
            double max = vida.CurrentMaxValue.Value;
            if (max <= 0.0 || r.VidaHost <= 0f) return; // vida 0 só com a ordem de morte
            double alvo = Math.Max(max * r.VidaHost, max * 0.0005);
            double atual = vida.CurrentValue.Value;
            bool golpeRecente = Time.unscaledTime - r.UltimoGolpe < JanelaGolpeLocal;
            if (Math.Abs(alvo - atual) < max * 0.001) return;
            if (golpeRecente && alvo > atual) return; // o host ainda não aplicou o golpe local
            VidaAtualRef(vida).Value = alvo;
        }

        private static void CriarPendentes(EnemyManager em, uint andarAtual)
        {
            List<ushort> remover = null;
            int criados = 0;
            foreach (var kv in Pendentes)
            {
                InimigoSpawn s = kv.Value;
                if (s.Andar < andarAtual) { (remover ?? (remover = new List<ushort>())).Add(kv.Key); continue; }
                if (s.Andar != andarAtual || criados >= CriacoesPorQuadro) continue;
                (remover ?? (remover = new List<ushort>())).Add(kv.Key);
                if (CriarReplica(em, s)) criados++;
            }
            if (remover != null)
            {
                foreach (ushort id in remover) Pendentes.Remove(id);
            }
        }

        private static bool CriarReplica(EnemyManager em, InimigoSpawn s)
        {
            if (Replicas.ContainsKey(s.Id)) return false;
            EnemyDefinition def = null;
            ChampionEnemyArchetypeDefinition arquetipo = null;
            List<PassiveSkillDefinition> afixos = null;
            try
            {
                def = global::HellClock.Constants.Collections.ENEMY_DEFINITION_COLLECTION.Reference.GetElement(s.DefinicaoHash);
                if (s.ArquetipoHash != 0) arquetipo = global::HellClock.Constants.Collections.ELITE_ENEMY_ARCHETYPE_DEFINITION_COLLECTION.Reference.GetElement(s.ArquetipoHash);
                foreach (int h in s.Afixos)
                {
                    if (global::HellClock.Constants.Collections.SKILL_DEFINITION_COLLECTION.Reference.GetElement(h) is PassiveSkillDefinition p)
                    {
                        (afixos ?? (afixos = new List<PassiveSkillDefinition>())).Add(p);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Inimigos] Definição do inimigo {s.Id} não encontrada ({s.DefinicaoHash}): {(e.InnerException ?? e).Message}");
            }
            if (def == null)
            {
                if (!_avisouDefinicao)
                {
                    _avisouDefinicao = true;
                    Sessao.Avisar("Um inimigo do host não existe no seu jogo (versão ou DLC diferente?). Veja o log.");
                }
                return false;
            }

            var cfg = new EnemyTypeConfig((EEnemyType)s.Tipo, arquetipo, afixos, s.Sobrevivencia, s.ParDeBoss, ignoreChanceAffixes: true);
            SpawnEnemyData data = new SpawnEnemyData(def, s.Posicao, cfg, false)
                .WithoutSamplePosition()
                .WithSpawnBehaviourConfig(new SpawnBehaviourConfig((SpawnFX)s.EfeitoSpawn, s.OrigemSpawn, s.AtrasoSpawn));
            if (!s.BarraDeVida) data = data.WithoutLifeView();

            IEnemy criado = null;
            em.AdditionalEliteAffixesAmount.AddIncrement(ChaveSemAfixoExtra, -10000); // afixos extras já vêm do host
            CriandoReplica = true;
            try
            {
                criado = em.SpawnEnemy(data);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Inimigos] Falha ao criar a réplica {s.Id} ({def.name}): {(e.InnerException ?? e).Message}");
            }
            finally
            {
                CriandoReplica = false;
                em.AdditionalEliteAffixesAmount.RemoveIncrement(ChaveSemAfixoExtra);
            }
            if (!(criado is EnemyController c)) return false;

            var r = new Replica { Id = s.Id, C = c, Posicao = s.Posicao, Yaw = s.Yaw, VidaHost = s.Vida, Nasceu = s.JaNasceu };
            c.transform.SetPositionAndRotation(s.Posicao, Quaternion.Euler(0f, s.Yaw, 0f));
            NavMeshAgent agente = c.MovementController?.Agent;
            if (agente != null)
            {
                agente.updatePosition = false;
                agente.updateRotation = false;
            }
            Animator an = c.Animator;
            if (an != null)
            {
                r.RootMotionOriginal = an.applyRootMotion;
                an.applyRootMotion = false;
                // Eventos de animação ficam ligados: as skills repetidas pela réplica (3c) dependem deles
                // para acertar. Sem skill ativa, o evento não tem ouvinte e não faz nada.
            }
            AplicarAjustes(c, s.Ajustes);
            Replicas[s.Id] = r;
            PorVida[c.LifeController] = r;
            Controles.Add(c);
            AplicarVida(r);
            return true;
        }

        private static void AplicarAjustes(EnemyController c, byte ajustes)
        {
            if ((ajustes & InimigoAjuste.Invocacao) != 0) c.SetupSummon();
            else if ((ajustes & InimigoAjuste.Descartavel) != 0) c.SetupDisposable();
        }

        private static void Restaurar(Replica r)
        {
            if (r.C == null) return;
            NavMeshAgent agente = r.C.MovementController?.Agent;
            if (agente != null)
            {
                agente.updatePosition = true;
                agente.updateRotation = true;
            }
            Animator an = r.C.Animator;
            if (an != null)
            {
                an.applyRootMotion = r.RootMotionOriginal;
            }
        }

        /// <summary>Tira a réplica dos mapas e devolve o inimigo ao normal (para morrer ou voltar ao pool).</summary>
        private static Replica Esquecer(ushort id)
        {
            if (!Replicas.TryGetValue(id, out Replica r)) return null;
            Replicas.Remove(id);
            if (r.C != null)
            {
                PorVida.Remove(r.C.LifeController);
                Controles.Remove(r.C);
            }
            else
            {
                Controles.RemoveWhere(x => x == null);
            }
            Restaurar(r);
            return r;
        }

        /// <summary>DisposeEnemy no convidado (troca de andar, fim da run): só esquece a réplica.</summary>
        internal static void ConvidadoDescartou(IEnemy inimigo)
        {
            if (!(inimigo is EnemyController c) || !Controles.Contains(c)) return;
            Replica alvo = null;
            foreach (Replica r in Replicas.Values)
            {
                if (r.C == c) { alvo = r; break; }
            }
            if (alvo != null) Esquecer(alvo.Id);
        }

        private static void ConvidadoMatar(InimigoMorte m)
        {
            Pendentes.Remove(m.Id);
            Replica r = Esquecer(m.Id);
            if (r?.C == null || r.C.LifeController.IsDead) return;
            DropManager dm = DropManager.Instance;
            if (m.SemDrop && dm != null) dm.IgnoreAllDrops.Block(ChaveSemDrop);
            AplicandoOrdem = true;
            try
            {
                r.C.LifeController.Kill();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Inimigos] Morte da réplica {m.Id} falhou: {(e.InnerException ?? e).Message}");
            }
            finally
            {
                AplicandoOrdem = false;
                if (m.SemDrop && dm != null) dm.IgnoreAllDrops.Release(ChaveSemDrop);
            }
        }

        private static void ConvidadoSumir(ushort id)
        {
            Pendentes.Remove(id);
            Replica r = Esquecer(id);
            if (r?.C == null || EnemyManager.Instance == null) return;
            AplicandoOrdem = true;
            try { EnemyManager.Instance.DisposeEnemy(0.3f, r.C); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Inimigos] Descarte da réplica {id} falhou: {(e.InnerException ?? e).Message}"); }
            finally { AplicandoOrdem = false; }
        }

        /// <summary>Durante a skill comandada pelo host, a disponibilidade da skill é forçada (ActiveSkill.CurrentSkillAvailabilityStatus).</summary>
        internal static bool ForcandoSkill { get; private set; }

        private static void ConvidadoUsarSkill(InimigoSkill m)
        {
            if (!Replicas.TryGetValue(m.Id, out Replica r) || r.C == null || r.C.LifeController.IsDead) return;
            ActiveSkill skill = AcharSkill(r.C, m.SkillHash);
            if (skill == null) return;
            string tipo = skill.GetType().Name;
            if (tipo.IndexOf("SpawnEnemy", StringComparison.Ordinal) >= 0 || tipo.IndexOf("Summon", StringComparison.Ordinal) >= 0) return; // invocações vêm do host

            ITarget alvo = null;
            PlayerController local = CoopRuntime.JogadorLocal();
            if (m.TipoAlvo == InimigoSkill.AlvoConvidado && local != null && Sessao.Eu.m_SteamID == m.SteamAlvo) alvo = local.GetTarget();
            else if (m.TipoAlvo == InimigoSkill.AlvoHost) alvo = AlvosAliados.De(Sessao.Host);
            else if (m.TipoAlvo == InimigoSkill.AlvoConvidado && Sessao.Pares.TryGetValue(m.SteamAlvo, out Par outro)) alvo = AlvosAliados.De(outro);
            if (alvo == null) return;

            // A réplica olha para onde o host olhava quando usou a skill (a direção do golpe sai daqui).
            Vector3 dir = m.PosicaoAlvo - r.C.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) r.C.transform.rotation = Quaternion.LookRotation(dir);

            ForcandoSkill = true;
            try
            {
                if (skill.IsUsing.Value) skill.Interrupt();
                if (skill.Execute(alvo)) r.SkillAte = Time.unscaledTime + 0.4f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Inimigos] Skill {skill.SkillDefinition?.name} da réplica {m.Id} falhou: {(e.InnerException ?? e).Message}");
            }
            finally
            {
                ForcandoSkill = false;
            }
            r.UltimaSkill = skill;
        }

        private static ActiveSkill AcharSkill(EnemyController c, int hash)
        {
            if (c.SkillsController != null && c.SkillsController.GetSkill(hash) is ActiveSkill s) return s;
            var pool = c.EnemyAIController?.ActiveSkillPool;
            if (pool == null) return null;
            foreach (ActiveSkill a in pool.Values)
            {
                if (a?.SkillDefinition != null && a.SkillDefinition.HashID == hash) return a;
            }
            return null;
        }

        private static void ConvidadoAndarLimpo(AndarLimpo a)
        {
            LevelInfo andar = LocalPlayerSampler.AndarAtual();
            EnemyManager em = EnemyManager.Instance;
            if (andar == null || em == null || andar.LevelIndex != a.Andar) return;
            Plugin.Log.LogInfo($"[Inimigos] Host limpou o andar {a.Andar}.");
            try { AndarLimpoRef(em)?.Trigger(); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Inimigos] Andar limpo falhou: {(e.InnerException ?? e).Message}"); }
        }

        /// <summary>Prefixo de LifeController.TryHurt no convidado: relata o golpe e impede a morte local.</summary>
        internal static void ConvidadoGolpeou(LifeController vida, DamageInstance dano)
        {
            if (AplicandoOrdem || PorVida.Count == 0 || dano == null || !PorVida.TryGetValue(vida, out Replica r)) return;
            if (vida.IsDead || dano.Value <= 0.0) return;
            long chave = ((long)r.Id << 32) | (uint)dano.DamageTypesBitMask;
            GolpesAcumulados.TryGetValue(chave, out Golpes.Item g);
            g.Id = r.Id;
            g.MascaraTipos = dano.DamageTypesBitMask;
            g.Valor += (float)dano.Value;
            g.Criticos = (byte)Mathf.Min(255, g.Criticos + dano.TotalCrits);
            GolpesAcumulados[chave] = g;
            r.UltimoGolpe = Time.unscaledTime;
            double atual = vida.CurrentValue.Value;
            if (dano.Value >= atual)
            {
                // Quem mata é o host. Aqui o golpe fatal deixa um fio de vida até a ordem chegar.
                dano.Value = Math.Max(0.0, atual - Math.Max(0.001, vida.CurrentMaxValue.Value * 0.0005));
            }
        }

        private static void LimparReplicas(string motivo)
        {
            if (Replicas.Count > 0) Plugin.Log.LogInfo($"[Inimigos] {Replicas.Count} réplicas esquecidas ({motivo}).");
            foreach (ushort id in new List<ushort>(Replicas.Keys)) Esquecer(id);
            GolpesAcumulados.Clear();
            Controles.Clear();
            PorVida.Clear();
        }

        private static void LimparConvidado(string motivo)
        {
            LimparReplicas(motivo);
            Pendentes.Clear();
            RecebidoEm.Clear();
        }

        private static void DevolverLimpeza(EnemyManager em)
        {
            if (_limpezaDesligada && em != null) IgnorarLimpezaRef(em) = false;
            _limpezaDesligada = false;
        }

        private static void ExpirarPendentes()
        {
            if (RecebidoEm.Count == 0) return;
            List<ushort> velhos = null;
            float agora = Time.unscaledTime;
            foreach (var kv in RecebidoEm)
            {
                if (agora - kv.Value > ValidadePendente || !Pendentes.ContainsKey(kv.Key)) (velhos ?? (velhos = new List<ushort>())).Add(kv.Key);
            }
            if (velhos == null) return;
            foreach (ushort id in velhos)
            {
                RecebidoEm.Remove(id);
                Pendentes.Remove(id);
            }
        }

        // ================= Comum =================

        public static void Atualizar()
        {
            if (Sessao == null) return;
            if (Host) AtualizarHost();
            AtualizarConvidado();
        }

        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            try
            {
                switch (dados[0])
                {
                    case Protocolo.TipoInimigoSpawn:
                        if (!Convidado || de != sessao.Host) return;
                        InimigoSpawn s = InimigoSpawn.Desserializar(dados);
                        if (!Replicas.ContainsKey(s.Id)) { Pendentes[s.Id] = s; RecebidoEm[s.Id] = Time.unscaledTime; }
                        break;
                    case Protocolo.TipoInimigosEstado:
                        if (!Convidado || de != sessao.Host) return;
                        foreach (InimigosEstado.Item i in InimigosEstado.Desserializar(dados).Itens)
                        {
                            if (Replicas.TryGetValue(i.Id, out Replica r))
                            {
                                r.Posicao = i.Posicao;
                                r.Yaw = i.Yaw;
                                r.VidaHost = i.Vida;
                                r.Nasceu |= i.JaNasceu;
                                r.QuerAgressivo |= i.Agressivo;
                                if (i.Animacao != null) { r.Animacao = i.Animacao; r.AnimacaoNova = true; }
                            }
                            else if (Pendentes.TryGetValue(i.Id, out InimigoSpawn p))
                            {
                                p.Posicao = i.Posicao;
                                p.Yaw = i.Yaw;
                                p.Vida = i.Vida;
                                p.JaNasceu |= i.JaNasceu;
                            }
                        }
                        break;
                    case Protocolo.TipoInimigoMorte:
                        if (Convidado && de == sessao.Host) ConvidadoMatar(InimigoMorte.Desserializar(dados));
                        break;
                    case Protocolo.TipoInimigoSumiu:
                        if (Convidado && de == sessao.Host) ConvidadoSumir(InimigoSumiu.Desserializar(dados).Id);
                        break;
                    case Protocolo.TipoAndarLimpo:
                        if (Convidado && de == sessao.Host) ConvidadoAndarLimpo(AndarLimpo.Desserializar(dados));
                        break;
                    case Protocolo.TipoInimigoSkill:
                        if (Convidado && de == sessao.Host) ConvidadoUsarSkill(InimigoSkill.Desserializar(dados));
                        break;
                    case Protocolo.TipoInimigoAjuste:
                        if (!Convidado || de != sessao.Host) return;
                        InimigoAjuste aj = InimigoAjuste.Desserializar(dados);
                        if (Replicas.TryGetValue(aj.Id, out Replica ra) && ra.C != null) AplicarAjustes(ra.C, aj.Ajuste);
                        else if (Pendentes.TryGetValue(aj.Id, out InimigoSpawn pa)) pa.Ajustes |= aj.Ajuste;
                        break;
                    case Protocolo.TipoGolpes:
                        if (Host && HostEmRun) HostAplicarGolpes(de, Golpes.Desserializar(dados));
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Inimigos] Mensagem {dados[0]} de {de.Nome} ignorada: {(e.InnerException ?? e).Message}");
            }
        }

        /// <summary>Saiu da sessão: devolve tudo ao normal.</summary>
        public static void Encerrar()
        {
            LimparConvidado("fim da sessão");
            HostPorId.Clear();
            HostPorControle.Clear();
        }
    }
}
