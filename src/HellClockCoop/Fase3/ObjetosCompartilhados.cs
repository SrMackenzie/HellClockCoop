using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HellClock.Character.Enemy;
using HellClock.Chests;
using HellClock.Level;
using HellClock.Manager;
using HellClock.Pool;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Baús e quebráveis (vasos, caixas) iguais para todos.
    ///
    /// Baús: são sorteados pelo host (LevelObjects.SetupLevelObjects, pulado no convidado). O host varre
    /// os baús ativos do andar 1x por segundo, numera e manda definição, posição e rotação; o convidado
    /// cria a cópia pelo pool do jogo e esconde qualquer outro baú que apareça no andar dele. Quem abrir
    /// abre para todos e cada um recebe o próprio loot (progressão individual).
    ///
    /// Quebráveis: vêm na cena do andar, mas parte deles sai de um DrawRandomSceneObject, que sorteia em
    /// cada PC qual objeto aparece naquele ponto (conferido no IL da 2.1.1.1). No coop, esse sorteio é
    /// semeado com a semente do andar que o host manda no PlanoAndar e com a posição do ponto, e dá o
    /// mesmo nos dois jogos. Quem quebrar avisa a posição; os outros quebram o quebrável mais próximo
    /// dali (até 0,75 m), com loot próprio. Não achou: fica no log, com a distância do mais próximo.
    /// </summary>
    internal static class ObjetosCompartilhados
    {
        private const float IntervaloVarredura = 1f;
        private const float DistanciaQuebravel = 0.75f;
        private const float ValidadePendente = 90f;
        private const int AvisosNaoAchouPorAndar = 5;

        // Estado do gerador do jogo (K10Random), guardado e devolvido em volta do sorteio semeado.
        private static readonly FieldInfo K10Estado = AccessTools.Field(typeof(K10Random), "_random");
        private static readonly FieldInfo K10UsaSemente = AccessTools.Field(typeof(K10Random), "_useSeed");
        private static readonly List<EstadoK10> SorteiosAbertos = new List<EstadoK10>(); // pilha prefixo → finalizer
        private static bool _avisouSemK10;
        private static int _sorteiosSemeados;
        private static int _naoAchadosNoAndar;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;
        private static bool Host => RunCompartilhada.Host;
        private static bool Convidado => RunCompartilhada.Convidado;

        private static readonly AccessTools.FieldRef<ChestInteractable, ChestDefinition> DefinicaoBauRef =
            AccessTools.FieldRefAccess<ChestInteractable, ChestDefinition>("_chestDefinition");
        private static readonly AccessTools.FieldRef<ChestInteractable, GameObject> IconeBauRef =
            AccessTools.FieldRefAccess<ChestInteractable, GameObject>("_minimapIcon");
        private static readonly AccessTools.FieldRef<ChestInteractable, Animator> AnimatorBauRef =
            AccessTools.FieldRefAccess<ChestInteractable, Animator>("_chestAnimator");

        private static readonly Dictionary<ushort, ChestInteractable> BausPorId = new Dictionary<ushort, ChestInteractable>();
        private static readonly Dictionary<ChestInteractable, ushort> IdPorBau = new Dictionary<ChestInteractable, ushort>();
        private static readonly Dictionary<ushort, BauSpawn> SpawnsDoHost = new Dictionary<ushort, BauSpawn>(); // host: para quem chega depois
        private static readonly Dictionary<ushort, BauSpawn> Pendentes = new Dictionary<ushort, BauSpawn>();
        private static readonly Dictionary<ushort, float> RecebidoEm = new Dictionary<ushort, float>();
        private static ushort _proximoId = 1;
        private static long _andarDosBaus = -1;
        private static float _proximaVarredura;

        /// <summary>Abrindo/quebrando por ordem de outro jogador: não avisar de volta nem mexer no jogador local.</summary>
        internal static bool OrdemRemota { get; private set; }

        private static LevelInfo AndarDeRun()
        {
            LevelInfo a = LocalPlayerSampler.AndarAtual();
            return a != null && a.LevelBlockInfo != null && DungeonProgressManager.Instance != null ? a : null;
        }

        // ---------------- Laço ----------------

        public static void Atualizar()
        {
            if (Sessao == null) return;
            if (Convidado) ExpirarPendentes();
            LevelInfo andar = AndarDeRun();
            if (andar == null)
            {
                if (LocalPlayerSampler.AndarAtual() != null && _andarDosBaus != -1) Encerrar(); // voltou ao hub
                return;
            }
            if (andar.LevelIndex != _andarDosBaus)
            {
                // Andar novo: os baús do anterior já foram desativados pelo jogo (LevelObjects.Deactivate).
                _andarDosBaus = andar.LevelIndex;
                BausPorId.Clear();
                IdPorBau.Clear();
                SpawnsDoHost.Clear();
                _proximaVarredura = 0f;
                LogarQuebraveis(andar);
            }
            if (Convidado && Pendentes.Count > 0) CriarPendentes(andar);
            if (Time.unscaledTime < _proximaVarredura) return;
            _proximaVarredura = Time.unscaledTime + IntervaloVarredura;
            if (Host) VarrerHost(andar);
            else if (Convidado) EsconderBausLocais();
        }

        // ---------------- Baús: host ----------------

        private static void VarrerHost(LevelInfo andar)
        {
            foreach (ChestInteractable bau in UnityEngine.Object.FindObjectsByType<ChestInteractable>(FindObjectsSortMode.None))
            {
                if (bau == null || IdPorBau.ContainsKey(bau)) continue;
                ChestDefinition def = DefinicaoBauRef(bau);
                if (def == null || bau.transform.parent == null) continue;
                ushort id = _proximoId++;
                if (_proximoId == 0) _proximoId = 1;
                Transform raiz = bau.transform.parent;
                var s = new BauSpawn
                {
                    Id = id, Andar = andar.LevelIndex, DefinicaoHash = def.HashID,
                    Posicao = raiz.position, Rotacao = raiz.eulerAngles.y, Aberto = !bau.CanBeInteractedWith,
                };
                BausPorId[id] = bau;
                IdPorBau[bau] = id;
                SpawnsDoHost[id] = s;
                if (Sessao.ComAliados) Sessao.EnviarParaConvidados(s.Serializar(), confiavel: true);
            }
        }

        internal static void AoNovoConvidado(SessaoCoop sessao, Par p)
        {
            foreach (var kv in SpawnsDoHost)
            {
                if (BausPorId.TryGetValue(kv.Key, out ChestInteractable b) && b != null) kv.Value.Aberto = !b.CanBeInteractedWith;
                sessao.EnviarPara(p, kv.Value.Serializar());
            }
        }

        // ---------------- Baús: convidado ----------------

        /// <summary>
        /// Chamado no lugar do SetupLevelObjects pulado (Fase 2): limpa as listas que o jogo limparia,
        /// para os baús copiados entrarem nelas (e serem desativados na troca de andar).
        /// </summary>
        internal static void ConvidadoNovoAndar(LevelObjects objetos)
        {
            try
            {
                objetos.LevelObjectsList?.Clear();
                if (objetos.Chests == null) AccessTools.PropertySetter(typeof(LevelObjects), nameof(LevelObjects.Chests)).Invoke(objetos, new object[] { new List<ChestInteractable>() });
                else objetos.Chests.Clear();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Objetos] Não deu para preparar as listas do andar: {(e.InnerException ?? e).Message}");
            }
        }

        private static void CriarPendentes(LevelInfo andar)
        {
            List<ushort> remover = null;
            foreach (var kv in Pendentes)
            {
                if (kv.Value.Andar < andar.LevelIndex) { (remover ?? (remover = new List<ushort>())).Add(kv.Key); continue; }
                if (kv.Value.Andar != andar.LevelIndex) continue;
                (remover ?? (remover = new List<ushort>())).Add(kv.Key);
                CriarBau(andar, kv.Value);
            }
            if (remover != null)
            {
                foreach (ushort id in remover) { Pendentes.Remove(id); RecebidoEm.Remove(id); }
            }
        }

        private static void CriarBau(LevelInfo andar, BauSpawn s)
        {
            if (BausPorId.ContainsKey(s.Id)) return;
            try
            {
                ChestDefinition def = global::HellClock.Constants.Collections.CHEST_DEFINITION_COLLECTION.Reference.GetElement(s.DefinicaoHash);
                if (def == null || LevelObjectPoolController.Instance == null) return;
                GameObject go = LevelObjectPoolController.Instance.SpawnChest(new ChestSpawnConfig(s.Posicao, s.Rotacao), def);
                ChestInteractable bau = go != null ? go.GetComponentInChildren<ChestInteractable>() : null;
                if (bau == null) return;
                BausPorId[s.Id] = bau;
                IdPorBau[bau] = s.Id;
                andar.LevelObjects?.LevelObjectsList?.Add(go);
                andar.LevelObjects?.Chests?.Add(bau);
                if (s.Aberto) MarcarAberto(bau);
                Plugin.Log.LogInfo($"[Objetos] Baú {s.Id} do host ({def.name}) em {s.Posicao}{(s.Aberto ? ", já aberto" : "")}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Objetos] Falha ao criar o baú {s.Id}: {(e.InnerException ?? e).Message}");
            }
        }

        /// <summary>Baú que só existe no convidado (sorteio local ou sobra de outro andar): some.</summary>
        private static void EsconderBausLocais()
        {
            foreach (ChestInteractable bau in UnityEngine.Object.FindObjectsByType<ChestInteractable>(FindObjectsSortMode.None))
            {
                if (bau == null || IdPorBau.ContainsKey(bau)) continue;
                GameObject raiz = bau.transform.parent != null ? bau.transform.parent.gameObject : bau.gameObject;
                Plugin.Log.LogInfo($"[Objetos] Baú local '{raiz.name}' em {raiz.transform.position} escondido (os baús são os do host).");
                raiz.SetActive(false);
            }
        }

        private static void MarcarAberto(ChestInteractable bau)
        {
            bau.SetBaseInteractionAvailable(false);
            if (bau.HoverFeedback != null) bau.HoverFeedback.enabled = false;
            GameObject icone = IconeBauRef(bau);
            if (icone != null) icone.SetActive(false);
            Animator an = AnimatorBauRef(bau);
            if (an != null) an.SetTrigger("Open");
        }

        private static void ExpirarPendentes()
        {
            if (RecebidoEm.Count == 0) return;
            List<ushort> velhos = null;
            foreach (var kv in RecebidoEm)
            {
                if (Time.unscaledTime - kv.Value > ValidadePendente) (velhos ?? (velhos = new List<ushort>())).Add(kv.Key);
            }
            if (velhos == null) return;
            foreach (ushort id in velhos) { RecebidoEm.Remove(id); Pendentes.Remove(id); }
        }

        // ---------------- Baús: abrir ----------------

        /// <summary>Postfix de ChestInteractable.ExecuteInteraction: o jogador local abriu um baú.</summary>
        internal static void AbriuBau(ChestInteractable bau)
        {
            if (OrdemRemota || Sessao == null || !Sessao.ComAliados || !IdPorBau.TryGetValue(bau, out ushort id)) return;
            byte[] m = new BauAberto { Id = id }.Serializar();
            if (Host) Sessao.EnviarParaConvidados(m, confiavel: true);
            else if (Convidado && Sessao.Host != null) Sessao.EnviarPara(Sessao.Host, m);
        }

        private static void AbrirPorOrdem(ushort id, Par de)
        {
            if (Pendentes.TryGetValue(id, out BauSpawn p)) { p.Aberto = true; return; }
            if (!BausPorId.TryGetValue(id, out ChestInteractable bau) || bau == null || !bau.CanBeInteractedWith) return;
            OrdemRemota = true;
            try { bau.ExecuteInteraction(); } // loot próprio deste jogador
            catch (Exception e) { Plugin.Log.LogWarning($"[Objetos] Abrir o baú {id} falhou: {(e.InnerException ?? e).Message}"); }
            finally { OrdemRemota = false; }
            Plugin.Log.LogInfo($"[Objetos] {de.Nome} abriu o baú {id}.");
        }

        // ---------------- Quebráveis ----------------

        /// <summary>Postfix de BreakableController.Handle_OnDie: o jogador local quebrou algo.</summary>
        internal static void Quebrou(BreakableController b)
        {
            if (OrdemRemota || Sessao == null || !Sessao.ComAliados || b == null) return;
            LevelInfo andar = AndarDeRun();
            if (andar == null) return;
            byte[] m = new Quebrou { Andar = andar.LevelIndex, Posicao = b.transform.position }.Serializar();
            if (Host) Sessao.EnviarParaConvidados(m, confiavel: true);
            else if (Convidado && Sessao.Host != null) Sessao.EnviarPara(Sessao.Host, m);
        }

        private static void QuebrarPorOrdem(Quebrou q, Par de)
        {
            LevelInfo andar = AndarDeRun();
            if (andar == null || andar.LevelIndex != q.Andar) return; // o outro está em outro andar (ou no hub)
            float limite = DistanciaQuebravel * DistanciaQuebravel;
            BreakableController alvo = null;
            float melhor = limite;
            float maisPerto = 3.4e38f; // qualquer um, vivo ou quebrado; float.MaxValue não existe no mscorlib do jogo
            bool jaQuebrado = false;
            int vivos = 0;
            foreach (BreakableController b in BreakableController.AllBreakables)
            {
                if (b == null || b.LifeController == null) continue;
                float d = (b.transform.position - q.Posicao).sqrMagnitude;
                if (d < maisPerto) maisPerto = d;
                if (b.LifeController.IsDead)
                {
                    if (d <= limite) jaQuebrado = true;
                    continue;
                }
                vivos++;
                if (d <= melhor) { melhor = d; alvo = b; }
            }
            if (alvo == null)
            {
                if (jaQuebrado) return; // já quebrado aqui (os dois quebraram o mesmo ao mesmo tempo)
                if (++_naoAchadosNoAndar > AvisosNaoAchouPorAndar) return;
                string perto = maisPerto < 3.4e38f ? $"o mais próximo está a {Mathf.Sqrt(maisPerto):0.00} m" : "nenhum quebrável no andar";
                Plugin.Log.LogWarning($"[Objetos] {de.Nome} quebrou algo em {q.Posicao} no andar {q.Andar}, mas aqui não há quebrável inteiro a até " +
                    $"{DistanciaQuebravel} m ({perto}; {vivos} inteiros no andar).{(_naoAchadosNoAndar == AvisosNaoAchouPorAndar ? " Os próximos deste andar não aparecem no log." : "")}");
                return;
            }
            OrdemRemota = true;
            try { alvo.Execute(); } // LifeController.Kill → quebra e rola o loot deste jogador
            catch (Exception e) { Plugin.Log.LogWarning($"[Objetos] Quebrar em {q.Posicao} falhou: {(e.InnerException ?? e).Message}"); }
            finally { OrdemRemota = false; }
        }

        // ---------------- Quebráveis: sorteio da cena ----------------

        /// <summary>
        /// Prefixo de DrawRandomSceneObject.OnEnable: com a semente do andar, semeia o K10Random (o gerador
        /// que o sorteio usa) com semente + posição do ponto. Sempre empilha uma entrada (null = não semeou),
        /// que o finalizer desempilha: um sorteio pode ativar outro ponto dentro dele.
        /// </summary>
        internal static void SemearSorteio(DrawRandomSceneObject ponto)
        {
            EstadoK10 antes = null;
            try
            {
                uint semente = RunCompartilhada.SementeCena;
                // Só com o coop ativo: fora dele o jogo sorteia como sem o mod, mesmo com semente sobrando.
                if (semente == 0 || ponto == null || !(RunCompartilhada.Host || RunCompartilhada.Convidado)) return;
                if (K10Estado == null || K10UsaSemente == null)
                {
                    if (!_avisouSemK10)
                    {
                        _avisouSemK10 = true;
                        Plugin.Log.LogWarning("[Objetos] K10Random mudou nesta versão do jogo: vasos e caixas continuam sorteados por PC e podem não bater.");
                    }
                    return;
                }
                antes = new EstadoK10 { Gerador = K10Estado.GetValue(null), UsavaSemente = (bool)K10UsaSemente.GetValue(null) };
                K10Random.SetRandomSeed(SementeDoPonto(semente, ponto.transform.position));
                _sorteiosSemeados++;
            }
            finally { SorteiosAbertos.Add(antes); }
        }

        /// <summary>Finalizer de DrawRandomSceneObject.OnEnable: devolve o K10Random como estava antes do prefixo.</summary>
        internal static void RestaurarSorteio()
        {
            if (SorteiosAbertos.Count == 0) return;
            EstadoK10 e = SorteiosAbertos[SorteiosAbertos.Count - 1];
            SorteiosAbertos.RemoveAt(SorteiosAbertos.Count - 1);
            if (e == null) return;
            K10Estado.SetValue(null, e.Gerador);
            K10UsaSemente.SetValue(null, e.UsavaSemente);
        }

        private sealed class EstadoK10
        {
            public object Gerador;     // Unity.Mathematics.Random (struct, guardado em caixa)
            public bool UsavaSemente;
        }

        /// <summary>Semente de um ponto da cena: igual nos dois PCs (mesma cena, mesma posição, em centímetros).</summary>
        private static uint SementeDoPonto(uint semente, Vector3 p)
        {
            uint h = Misturar(semente ^ (uint)Mathf.RoundToInt(p.x * 100f));
            h = Misturar(h ^ (uint)Mathf.RoundToInt(p.y * 100f));
            h = Misturar(h ^ (uint)Mathf.RoundToInt(p.z * 100f));
            return h == 0 ? 1u : h; // semente 0 trava o gerador do Unity.Mathematics
        }

        private static uint Misturar(uint h)
        {
            unchecked
            {
                h ^= h >> 16; h *= 0x85EBCA6B;
                h ^= h >> 13; h *= 0xC2B2AE35;
                h ^= h >> 16;
            }
            return h;
        }

        /// <summary>
        /// Ao entrar no andar: quantos quebráveis há e uma assinatura das posições. Os dois PCs devem
        /// mostrar o mesmo número e a mesma assinatura; se não, o sorteio da cena não bateu.
        /// </summary>
        private static void LogarQuebraveis(LevelInfo andar)
        {
            int total = 0;
            uint assinatura = 0;
            foreach (BreakableController b in BreakableController.AllBreakables)
            {
                if (b == null) continue;
                total++;
                assinatura ^= SementeDoPonto(0x9E3779B9, b.transform.position); // XOR: não depende da ordem da lista
            }
            Plugin.Log.LogInfo($"[Objetos] Andar {andar.LevelIndex}: {total} quebráveis (assinatura {assinatura:X8}); " +
                $"{_sorteiosSemeados} sorteios da cena com a semente {RunCompartilhada.SementeCena:X8}. Compare com o log do outro PC.");
            _sorteiosSemeados = 0;
            _naoAchadosNoAndar = 0;
        }

        // ---------------- Rede ----------------

        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            try
            {
                switch (dados[0])
                {
                    case Protocolo.TipoBauSpawn:
                        if (!Convidado || de != sessao.Host) return;
                        BauSpawn s = BauSpawn.Desserializar(dados);
                        if (!BausPorId.ContainsKey(s.Id)) { Pendentes[s.Id] = s; RecebidoEm[s.Id] = Time.unscaledTime; }
                        break;
                    case Protocolo.TipoBauAberto:
                        ushort id = BauAberto.Desserializar(dados).Id;
                        AbrirPorOrdem(id, de);
                        if (Host) Repassar(sessao, de, dados);
                        break;
                    case Protocolo.TipoQuebrou:
                        QuebrarPorOrdem(Net.Quebrou.Desserializar(dados), de);
                        if (Host) Repassar(sessao, de, dados);
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Objetos] Mensagem {dados[0]} de {de.Nome} ignorada: {(e.InnerException ?? e).Message}");
            }
        }

        /// <summary>Host repassa o que um convidado fez para os demais convidados.</summary>
        private static void Repassar(SessaoCoop sessao, Par de, byte[] dados)
        {
            foreach (Par p in sessao.Pares.Values)
            {
                if (p != de && p.Compativel) sessao.EnviarPara(p, dados);
            }
        }

        public static void Encerrar()
        {
            BausPorId.Clear();
            IdPorBau.Clear();
            SpawnsDoHost.Clear();
            Pendentes.Clear();
            RecebidoEm.Clear();
            _andarDosBaus = -1;
        }
    }
}
