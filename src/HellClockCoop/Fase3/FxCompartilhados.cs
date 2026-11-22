using System;
using System.Collections.Generic;
using HarmonyLib;
using HellClock.Character.Enemy;
using HellClock.Manager;
using HellClock.Player;
using HellClock.Skill;
using HellClock.VFX;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Fase 3b — um vê as skills do outro.
    ///
    /// Quem lança: postfix em GameplayFx.CreateFx (por onde nasce todo efeito de skill: tiros, projéteis,
    /// áreas, auras). Se o efeito é do jogador local, guarda o objeto e, no fim do quadro (quando a skill
    /// já posicionou, prendeu no osso ou desenhou a linha), manda o GUID do prefab e onde ele está.
    /// Efeitos soltos que se movem mandam a posição a 20 Hz; quando o objeto volta ao pool, manda o fim.
    ///
    /// Quem recebe: carrega o mesmo prefab pelo Addressables, instancia uma cópia desativada, tira todo
    /// script do jogo, colisor e som (sobra só o visual: partículas, malhas, linhas, luzes, animação),
    /// e põe no avatar de quem lançou. Dano não existe na cópia: o dano de verdade já vai pelos Golpes.
    /// No modo eco (F8), o fantasma local também recebe as cópias, para testar sem amigo.
    /// </summary>
    internal static class FxCompartilhados
    {
        private const float IntervaloMovimento = 0.05f;
        private const int MaximoAtivos = 96;
        private const int BytesPorPacote = 1100;
        private const float VidaSolto = 30f;
        private const float VidaPreso = 120f;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;

        /// <summary>Fantasma do modo eco (ou null): recebe as próprias cópias, deslocado.</summary>
        internal static RemotePlayerAvatar AvatarEco;

        private static readonly AccessTools.FieldRef<GameplayFx, AssetReferenceGameObject> PrefabRef =
            AccessTools.FieldRefAccess<GameplayFx, AssetReferenceGameObject>("_vfxPrefab");

        // ---------------- Quem lança ----------------

        private sealed class FxLocal
        {
            public ushort Id;
            public GameObject Go;
            public string Guid;
            public bool Anunciado;
            public bool Solto;
            public Vector3 UltimaPos;
            public Quaternion UltimaRot;
            // Partes internas mexidas por script (ver FxNos)
            public readonly List<Transform> Nos = new List<Transform>();
            public readonly List<string> Caminhos = new List<string>();
            public readonly List<FxNos.No> UltimoEstado = new List<FxNos.No>();
            public int Assinatura = -1;
            public float ProximaRevisao;
            public bool NosConfiavel; // o primeiro estado completo vai pelo canal confiável
        }

        private const int MaximoNos = 48;
        private const int ProfundidadeNos = 5;

        /// <summary>Lista as partes internas do efeito (sem a raiz e sem outros efeitos rastreados dentro dele).</summary>
        private static void MontarNos(FxLocal f)
        {
            f.Nos.Clear();
            f.Caminhos.Clear();
            f.UltimoEstado.Clear();
            Visitar(f, f.Go.transform, "", 0);
            f.Assinatura = Assinar(f.Go.transform, 0);
            f.NosConfiavel = true;
        }

        private static void Visitar(FxLocal f, Transform t, string prefixo, int nivel)
        {
            if (nivel >= ProfundidadeNos) return;
            for (int i = 0; i < t.childCount && f.Nos.Count < MaximoNos; i++)
            {
                Transform c = t.GetChild(i);
                if (Locais.ContainsKey(c.gameObject)) continue; // outro efeito: tem a própria transmissão
                string caminho = prefixo.Length == 0 ? i.ToString() : prefixo + "/" + i;
                f.Nos.Add(c);
                f.Caminhos.Add(caminho);
                f.UltimoEstado.Add(new FxNos.No { Caminho = null }); // força o primeiro envio
                Visitar(f, c, caminho, nivel + 1);
            }
        }

        private static int Assinar(Transform t, int nivel)
        {
            if (nivel >= ProfundidadeNos) return 0;
            int n = t.childCount;
            for (int i = 0; i < t.childCount; i++) n = n * 31 + Assinar(t.GetChild(i), nivel + 1);
            return n;
        }

        /// <summary>Partes que mudaram desde o último envio.</summary>
        private static FxNos NosAlterados(FxLocal f)
        {
            if (Time.unscaledTime >= f.ProximaRevisao)
            {
                f.ProximaRevisao = Time.unscaledTime + 0.5f;
                if (Assinar(f.Go.transform, 0) != f.Assinatura) MontarNos(f); // o script criou/removeu partes
            }
            FxNos m = null;
            for (int i = 0; i < f.Nos.Count; i++)
            {
                Transform t = f.Nos[i];
                if (t == null) continue;
                FxNos.No antes = f.UltimoEstado[i];
                bool ativo = t.gameObject.activeSelf;
                bool mudou = antes.Caminho == null || antes.Ativo != ativo
                    || (t.localPosition - antes.Posicao).sqrMagnitude > 0.000025f
                    || Quaternion.Angle(t.localRotation, antes.Rotacao) > 0.5f
                    || (t.localScale - antes.Escala).sqrMagnitude > 0.0001f;
                if (!mudou) continue;
                var agora = new FxNos.No { Caminho = f.Caminhos[i], Ativo = ativo, Posicao = t.localPosition, Rotacao = t.localRotation, Escala = t.localScale };
                f.UltimoEstado[i] = agora;
                (m ?? (m = new FxNos { Id = f.Id })).Nos.Add(agora);
            }
            return m;
        }

        private static readonly Dictionary<GameObject, FxLocal> Locais = new Dictionary<GameObject, FxLocal>();
        private static readonly List<FxLocal> ParaRemover = new List<FxLocal>();
        private static ushort _proximoId = 1;
        private static float _proximoMovimento;

        private static bool HaQuemVeja => (Sessao != null && Sessao.AlguemNoMesmoAndar) || AvatarEco != null;

        /// <summary>Efeito de skill do jogador local rastreado aqui (o AvatarFactory não o clona: a cópia animada vem daqui).</summary>
        internal static bool EhEfeitoDoJogador(GameObject go) => go != null && Locais.ContainsKey(go);

        /// <summary>Postfix de GameplayFx.CreateFx.</summary>
        internal static void EfeitoCriado(GameplayFx fx, ISkillCaster caster, Skill skill, GameObject go)
        {
            if (go == null) return; // rastreia sempre: quem chegar depois (ou o eco ligado depois) recebe os efeitos que já existem
            // Com sobrescrita de efeito, a chamada de dentro (o efeito de fato) é que registra.
            if (skill != null && skill.HasFxOverrides && skill.SkillFxOverrides != null && skill.SkillFxOverrides.HasOverride(fx)) return;
            PlayerController local = CoopRuntime.JogadorLocal(); // (PlayerController.Instance cria um se faltar)
            if (local == null || !DoJogador(caster, skill, local)) return;
            AssetReferenceGameObject prefab = PrefabRef(fx);
            if (prefab == null || string.IsNullOrEmpty(prefab.AssetGUID)) return;

            if (Locais.TryGetValue(go, out FxLocal antigo)) Terminar(antigo); // o pool reaproveitou o objeto
            if (Locais.Count >= MaximoAtivos) return;
            ushort id = _proximoId++;
            if (_proximoId == 0) _proximoId = 1;
            Locais[go] = new FxLocal { Id = id, Go = go, Guid = prefab.AssetGUID };
        }

        private static bool DoJogador(ISkillCaster caster, Skill skill, PlayerController local)
        {
            ISkillCaster c = caster ?? skill?.SkillCaster;
            if (c == null) return false;
            if (ReferenceEquals(c, local)) return true;
            try { return ReferenceEquals(c.SourceOrSelf, local); } // companheiros, invocações do jogador
            catch (Exception) { return false; }
        }

        private static int _vistasAntes;

        private static void AtualizarLocais()
        {
            if (Locais.Count == 0) return;
            PlayerController local = CoopRuntime.JogadorLocal();
            int vistas = (AvatarEco != null ? 1 : 0) + ContarVistas();
            if (vistas > _vistasAntes)
            {
                // Alguém passou a ver (eco ligado, aliado chegou ao andar): reanuncia o que já existe (orbes, auras).
                foreach (FxLocal f in Locais.Values) f.Anunciado = false;
            }
            _vistasAntes = vistas;
            bool enviarMovimento = Time.unscaledTime >= _proximoMovimento;
            if (enviarMovimento) _proximoMovimento = Time.unscaledTime + IntervaloMovimento;
            FxMovimento mov = null;

            ParaRemover.Clear();
            foreach (FxLocal f in Locais.Values)
            {
                if (f.Go == null || !f.Go.activeInHierarchy)
                {
                    ParaRemover.Add(f);
                    continue;
                }
                if (vistas == 0) continue;
                if (!f.Anunciado)
                {
                    Anunciar(f, local);
                    continue;
                }
                if (!enviarMovimento && !f.NosConfiavel) continue;
                bool confiavel = f.NosConfiavel;
                f.NosConfiavel = false;
                FxNos nos = NosAlterados(f);
                if (nos != null)
                {
                    for (int i = 0; i < nos.Nos.Count; i += 30) // ~40 bytes por parte
                    {
                        var parte = new FxNos { Id = nos.Id };
                        parte.Nos.AddRange(nos.Nos.GetRange(i, Mathf.Min(30, nos.Nos.Count - i)));
                        Sessao?.EnviarParaMesmoAndar(parte.Serializar(), confiavel);
                        if (AvatarEco != null) NaFilaDoEco(() => AplicarNos(Eco, parte));
                    }
                }
                if (!f.Solto || !enviarMovimento) continue;
                Transform t = f.Go.transform;
                if ((t.position - f.UltimaPos).sqrMagnitude < 0.0004f && Quaternion.Angle(t.rotation, f.UltimaRot) < 1f) continue;
                f.UltimaPos = t.position;
                f.UltimaRot = t.rotation;
                (mov ?? (mov = new FxMovimento { Tempo = Time.unscaledTime })).Itens.Add(new FxMovimento.Item { Id = f.Id, Posicao = t.position, Rotacao = t.rotation });
            }
            foreach (FxLocal f in ParaRemover) Terminar(f);

            if (mov != null)
            {
                // Pacotes de até ~1100 bytes (30 bytes por item).
                for (int i = 0; i < mov.Itens.Count; i += 36)
                {
                    var parte = new FxMovimento { Tempo = mov.Tempo };
                    parte.Itens.AddRange(mov.Itens.GetRange(i, Mathf.Min(36, mov.Itens.Count - i)));
                    Sessao?.EnviarParaMesmoAndar(parte.Serializar(), confiavel: false);
                    if (AvatarEco != null) NaFilaDoEco(() => AplicarMovimento(Eco, parte));
                }
            }
        }

        private static int ContarVistas()
        {
            if (Sessao == null) return 0;
            int n = 0;
            foreach (Par p in Sessao.Pares.Values) if (p.Compativel && p.MesmoAndar) n++;
            return n;
        }

        private static void Anunciar(FxLocal f, PlayerController local)
        {
            f.Anunciado = true;
            MontarNos(f);
            Transform t = f.Go.transform;
            var m = new FxNovo { Id = f.Id, Guid = f.Guid };
            string caminho = local != null && t.parent != null ? CaminhoNoJogador(local.transform, t.parent) : null;
            // Dentro de outro efeito (ex.: orbe no "holder" que gira): o script que move foi removido da cópia,
            // então vai solto, com a posição de mundo transmitida.
            if (caminho != null && DentroDeOutroEfeito(t.parent, local.transform)) caminho = null;
            EnemyController inimigo = caminho == null && t.parent != null ? t.parent.GetComponentInParent<EnemyController>() : null;
            if (caminho != null)
            {
                m.TipoPai = FxNovo.PaiJogador;
                m.Caminho = caminho;
                m.Posicao = t.localPosition;
                m.Rotacao = t.localRotation;
                m.Escala = t.localScale;
            }
            else if (inimigo != null && !DentroDeOutroEfeito(t.parent, inimigo.transform))
            {
                // Preso num inimigo (estacas cravadas, efeitos de status): a cópia vai presa no mesmo inimigo.
                m.TipoPai = FxNovo.PaiInimigo;
                m.Caminho = AvatarFactory.CaminhoRelativo(inimigo.transform, t.parent);
                InimigosCompartilhados.IdDe(inimigo, out m.InimigoId);
                m.PaiLocal = t.parent;
                m.Posicao = t.localPosition;
                m.Rotacao = t.localRotation;
                m.Escala = t.localScale;
            }
            else
            {
                f.Solto = true;
                m.TipoPai = FxNovo.PaiMundo;
                m.Posicao = t.position;
                m.Rotacao = t.rotation;
                m.Escala = t.lossyScale;
            }
            f.UltimaPos = t.position;
            f.UltimaRot = t.rotation;
            LineRenderer lr = f.Go.GetComponentInChildren<LineRenderer>();
            if (lr != null && lr.positionCount > 0 && lr.positionCount <= 32)
            {
                m.LinhaNoMundo = lr.useWorldSpace;
                m.Linha = new Vector3[lr.positionCount];
                lr.GetPositions(m.Linha);
            }
            if (m.TipoPai != FxNovo.PaiInimigo || m.InimigoId != 0) Sessao?.EnviarParaMesmoAndar(m.Serializar(), confiavel: true);
            if (AvatarEco != null) NaFilaDoEco(() => Criar(Eco, m));
        }

        private static bool DentroDeOutroEfeito(Transform pai, Transform raizJogador)
        {
            for (Transform x = pai; x != null && x != raizJogador; x = x.parent)
            {
                if (Locais.ContainsKey(x.gameObject)) return true;
            }
            return false;
        }

        /// <summary>Caminho de <paramref name="alvo"/> a partir da raiz do jogador; null se não estiver no jogador.</summary>
        private static string CaminhoNoJogador(Transform raiz, Transform alvo)
        {
            for (Transform x = alvo; x != null; x = x.parent)
            {
                if (x == raiz) return AvatarFactory.CaminhoRelativo(raiz, alvo);
            }
            return null;
        }

        private static void Terminar(FxLocal f)
        {
            Locais.Remove(f.Go);
            if (!f.Anunciado) return;
            byte[] b = new FxFim { Id = f.Id }.Serializar();
            Sessao?.EnviarParaMesmoAndar(b, confiavel: true);
            ushort id = f.Id;
            if (AvatarEco != null) NaFilaDoEco(() => Remover(Eco, id));
        }

        // ---------------- Quem recebe ----------------

        private sealed class Copia
        {
            public GameObject Go;
            public bool Solta;
            public readonly List<Amostra> Amostras = new List<Amostra>(8); // por hora de chegada
            public float Expira;
            public readonly Dictionary<string, Transform> Nos = new Dictionary<string, Transform>();
            public readonly Dictionary<Transform, FxNos.No> AlvoNos = new Dictionary<Transform, FxNos.No>();
        }

        /// <summary>Acha a parte pelo caminho de índices; se o script do remetente criou partes a mais, clona a primeira irmã.</summary>
        private static Transform AcharNo(Copia c, string caminho)
        {
            if (c.Nos.TryGetValue(caminho, out Transform achado) && achado != null) return achado;
            Transform t = c.Go.transform;
            foreach (string parte in caminho.Split('/'))
            {
                if (!int.TryParse(parte, out int i) || i < 0 || i > 255) return null;
                if (t.childCount == 0) return null;
                while (t.childCount <= i)
                {
                    Transform modelo = t.GetChild(0);
                    UnityEngine.Object.Instantiate(modelo.gameObject, t, false).name = modelo.name; // já é cópia limpa
                }
                t = t.GetChild(i);
            }
            c.Nos[caminho] = t;
            return t;
        }

        private static void AplicarNos(Remetente de, FxNos m)
        {
            if (!Copias.TryGetValue(de.Chave, out var dele) || !dele.TryGetValue(m.Id, out Copia c) || c.Go == null) return;
            foreach (FxNos.No n in m.Nos)
            {
                Transform t = AcharNo(c, n.Caminho);
                if (t == null) continue;
                if (t.gameObject.activeSelf != n.Ativo) t.gameObject.SetActive(n.Ativo);
                bool primeiro = !c.AlvoNos.ContainsKey(t);
                c.AlvoNos[t] = n;
                if (primeiro) { t.localPosition = n.Posicao; t.localRotation = n.Rotacao; t.localScale = n.Escala; }
            }
            c.Expira = Mathf.Max(c.Expira, Time.unscaledTime + VidaSolto);
        }

        private static void SuavizarNos(Copia c, float dt)
        {
            if (c.AlvoNos.Count == 0) return;
            float k = 1f - Mathf.Exp(-25f * dt);
            foreach (var kv in c.AlvoNos)
            {
                Transform t = kv.Key;
                if (t == null) continue;
                FxNos.No n = kv.Value;
                t.localPosition = Vector3.Lerp(t.localPosition, n.Posicao, k);
                t.localRotation = Quaternion.Slerp(t.localRotation, n.Rotacao, k);
                t.localScale = Vector3.Lerp(t.localScale, n.Escala, k);
            }
        }

        private struct Amostra
        {
            public float T;
            public Vector3 Pos;
            public Quaternion Rot;
        }

        /// <summary>Atraso de interpolação dos efeitos soltos (o mesmo do avatar).</summary>
        private const float AtrasoMovimento = 0.1f;

        /// <summary>Cópias por remetente (a chave do eco é 0).</summary>
        private static readonly Dictionary<ulong, Dictionary<ushort, Copia>> Copias = new Dictionary<ulong, Dictionary<ushort, Copia>>();
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> PrefabsRuins = new HashSet<string>();
        private static GameObject _estacionamento;
        private static int _copiasLogadas;
        private static readonly Remetente Eco = new Remetente { Chave = 0 };

        private sealed class Remetente
        {
            public ulong Chave;
            public RemotePlayerAvatar Avatar;
            public Vector3 Deslocamento;
        }

        private static Remetente DePar(Par p) => new Remetente { Chave = p.Id.m_SteamID, Avatar = p.Avatar };

        private static GameObject ObterPrefab(string guid)
        {
            if (Prefabs.TryGetValue(guid, out GameObject pf)) return pf;
            if (PrefabsRuins.Contains(guid)) return null;
            try
            {
                double inicio = Desempenho.Agora(); // carga síncrona: suspeita de travada (medidor, 0.9.7)
                AsyncOperationHandle<GameObject> h = Addressables.LoadAssetAsync<GameObject>(guid);
                if (!h.IsDone) h.WaitForCompletion();
                Desempenho.SomarDetalhe(Desempenho.Detalhe.CarregarPrefabFx, inicio);
                pf = h.Status == AsyncOperationStatus.Succeeded ? h.Result : null; // o handle fica aberto: o prefab é reusado
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Fx] Prefab {guid} não carregou: {(e.InnerException ?? e).Message}");
                pf = null;
            }
            if (pf == null) { PrefabsRuins.Add(guid); Plugin.Log.LogWarning($"[Fx] Prefab {guid} indisponível neste jogo."); }
            else Prefabs[guid] = pf;
            return pf;
        }

        private static void Criar(Remetente de, FxNovo m)
        {
            if (de.Avatar == null || !de.Avatar.gameObject.activeInHierarchy) return;
            GameObject prefab = ObterPrefab(m.Guid);
            if (prefab == null) return;
            if (_estacionamento == null)
            {
                _estacionamento = new GameObject("HellClockCoop_FxEstacionamento");
                _estacionamento.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_estacionamento);
            }
            GameObject go;
            try
            {
                double inicio = Desempenho.Agora();
                go = UnityEngine.Object.Instantiate(prefab, _estacionamento.transform);
                LimparParaVisual(go);
                Desempenho.SomarDetalhe(Desempenho.Detalhe.CopiarFx, inicio);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Fx] Cópia de {m.Guid} falhou: {(e.InnerException ?? e).Message}");
                PrefabsRuins.Add(m.Guid);
                return;
            }

            var c = new Copia { Go = go };
            Transform t = go.transform;
            Transform paiInimigo = null;
            if (m.TipoPai == FxNovo.PaiInimigo)
            {
                if (m.PaiLocal != null) paiInimigo = m.PaiLocal; // eco: o mesmo objeto
                else
                {
                    EnemyController e = InimigosCompartilhados.PorId(m.InimigoId);
                    if (e != null) paiInimigo = string.IsNullOrEmpty(m.Caminho) ? e.transform : (e.transform.Find(m.Caminho) ?? e.transform);
                }
                if (paiInimigo == null) { UnityEngine.Object.Destroy(go); return; } // o inimigo já sumiu
            }
            if (paiInimigo != null)
            {
                t.SetParent(paiInimigo, false);
                t.localPosition = m.Posicao;
                t.localRotation = m.Rotacao;
                t.localScale = m.Escala;
                c.Expira = Time.unscaledTime + VidaPreso;
            }
            else if (m.TipoPai == FxNovo.PaiJogador && de.Avatar.Modelo != null)
            {
                Transform osso = string.IsNullOrEmpty(m.Caminho) ? de.Avatar.Modelo : de.Avatar.Modelo.Find(m.Caminho);
                t.SetParent(osso != null ? osso : de.Avatar.Modelo, false);
                t.localPosition = m.Posicao;
                t.localRotation = m.Rotacao;
                t.localScale = m.Escala;
                c.Expira = Time.unscaledTime + VidaPreso;
            }
            else
            {
                Transform raizNivel = DungeonProgressManager.Instance != null ? DungeonProgressManager.Instance.CurrentLevelObjectsRoot : null;
                t.SetParent(raizNivel, false);
                t.SetPositionAndRotation(m.Posicao + de.Deslocamento, m.Rotacao);
                t.localScale = m.Escala; // sem pai com escala: lossy = local
                c.Solta = true;
                c.Amostras.Add(new Amostra { T = Time.unscaledTime, Pos = t.position, Rot = t.rotation });
                c.Expira = Time.unscaledTime + VidaSolto;
            }
            if (m.Linha.Length > 0)
            {
                LineRenderer lr = go.GetComponentInChildren<LineRenderer>(true);
                if (lr != null)
                {
                    lr.useWorldSpace = m.LinhaNoMundo;
                    lr.positionCount = m.Linha.Length;
                    if (m.LinhaNoMundo && de.Deslocamento != Vector3.zero)
                    {
                        var pts = new Vector3[m.Linha.Length];
                        for (int i = 0; i < pts.Length; i++) pts[i] = m.Linha[i] + de.Deslocamento;
                        lr.SetPositions(pts);
                    }
                    else lr.SetPositions(m.Linha);
                }
            }
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null) ps.Play(true);
            }

            if (_copiasLogadas < 8)
            {
                _copiasLogadas++;
                Plugin.Log.LogInfo($"[Fx] Cópia de '{prefab.name}' ({(c.Solta ? "solta" : (m.TipoPai == FxNovo.PaiInimigo ? "presa no inimigo " : "presa em ") + m.Caminho)}) para {(de.Chave == 0 ? "o eco" : "o aliado")}.");
            }
            if (!Copias.TryGetValue(de.Chave, out var dele)) Copias[de.Chave] = dele = new Dictionary<ushort, Copia>();
            if (dele.TryGetValue(m.Id, out Copia velha) && velha.Go != null) UnityEngine.Object.Destroy(velha.Go);
            dele[m.Id] = c;
        }

        private static void AplicarMovimento(Remetente de, FxMovimento m)
        {
            if (!Copias.TryGetValue(de.Chave, out var dele)) return;
            float agora = Time.unscaledTime;
            foreach (FxMovimento.Item i in m.Itens)
            {
                if (!dele.TryGetValue(i.Id, out Copia c) || c.Go == null || !c.Solta) continue;
                c.Amostras.Add(new Amostra { T = agora, Pos = i.Posicao + de.Deslocamento, Rot = i.Rotacao });
                if (c.Amostras.Count > 8) c.Amostras.RemoveAt(0);
                c.Expira = agora + VidaSolto;
            }
        }

        /// <summary>Interpola entre as amostras, AtrasoMovimento no passado (como o avatar).</summary>
        private static void Interpolar(Copia c, float agora)
        {
            List<Amostra> a = c.Amostras;
            if (a.Count == 0) return;
            float alvo = agora - AtrasoMovimento;
            Transform t = c.Go.transform;
            if (a.Count == 1 || alvo <= a[0].T)
            {
                t.SetPositionAndRotation(a[0].Pos, a[0].Rot);
                return;
            }
            for (int k = a.Count - 1; k > 0; k--)
            {
                if (alvo < a[k - 1].T) continue;
                Amostra x = a[k - 1], y = a[k];
                float u = y.T > x.T ? Mathf.Clamp01((alvo - x.T) / (y.T - x.T)) : 1f;
                t.SetPositionAndRotation(Vector3.Lerp(x.Pos, y.Pos, u), Quaternion.Slerp(x.Rot, y.Rot, u));
                while (k - 1 > 0) { a.RemoveAt(0); k--; } // amostras antigas não servem mais
                return;
            }
        }

        private static void Remover(Remetente de, ushort id)
        {
            if (!Copias.TryGetValue(de.Chave, out var dele) || !dele.TryGetValue(id, out Copia c)) return;
            dele.Remove(id);
            if (c.Go != null) UnityEngine.Object.Destroy(c.Go);
        }

        private static void AtualizarCopias()
        {
            if (Copias.Count == 0) return;
            float agora = Time.unscaledTime;
            List<ulong> semAvatar = null;
            foreach (var kv in Copias)
            {
                bool temAvatar = kv.Key == 0 ? AvatarEco != null : AvatarDe(kv.Key) != null;
                if (!temAvatar) { (semAvatar ?? (semAvatar = new List<ulong>())).Add(kv.Key); continue; }
                List<ushort> vencidas = null;
                foreach (var c in kv.Value)
                {
                    Copia x = c.Value;
                    if (x.Go == null || agora > x.Expira) { (vencidas ?? (vencidas = new List<ushort>())).Add(c.Key); continue; }
                    if (x.Solta) Interpolar(x, agora);
                    SuavizarNos(x, Time.deltaTime);
                }
                if (vencidas != null)
                {
                    foreach (ushort id in vencidas)
                    {
                        if (kv.Value.TryGetValue(id, out Copia x) && x.Go != null) UnityEngine.Object.Destroy(x.Go);
                        kv.Value.Remove(id);
                    }
                }
            }
            if (semAvatar != null)
            {
                foreach (ulong k in semAvatar) DestruirDe(k);
            }
        }

        private static RemotePlayerAvatar AvatarDe(ulong steam)
        {
            if (Sessao == null || !Sessao.Pares.TryGetValue(steam, out Par p)) return null;
            return p.Avatar != null && p.Avatar.gameObject.activeInHierarchy && p.MesmoAndar ? p.Avatar : null;
        }

        private static void DestruirDe(ulong chave)
        {
            if (!Copias.TryGetValue(chave, out var dele)) return;
            foreach (Copia c in dele.Values) if (c.Go != null) UnityEngine.Object.Destroy(c.Go);
            Copias.Remove(chave);
        }

        // ---------------- Limpeza da cópia ----------------

        private static readonly HashSet<string> TiposProibidos = new HashSet<string>
        {
            "Rigidbody", "AudioSource", "AudioListener", "AudioReverbZone", "AudioLowPassFilter", "AudioHighPassFilter",
            "Camera", "NavMeshAgent", "NavMeshObstacle", "CharacterController", "Cloth",
        };

        private static bool ManterNaCopia(Component c)
        {
            if (c is Transform) return true;
            if (c is Collider) return false;
            Type t = c.GetType();
            string ns = t.Namespace ?? "";
            if (!ns.StartsWith("UnityEngine", StringComparison.Ordinal)) return false; // scripts do jogo, FMOD, DOTween...
            if (TiposProibidos.Contains(t.Name) || t.Name.EndsWith("Joint", StringComparison.Ordinal)) return false;
            return true;
        }

        private static void LimparParaVisual(GameObject go)
        {
            for (int passada = 0; passada < 6; passada++)
            {
                int nesta = 0;
                for (int categoria = 0; categoria < 3; categoria++)
                {
                    foreach (Component c in go.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || ManterNaCopia(c)) continue;
                        int cat = c is MonoBehaviour ? 0 : c is Behaviour ? 1 : 2;
                        if (cat != categoria) continue;
                        UnityEngine.Object.DestroyImmediate(c);
                        if (c == null) nesta++;
                    }
                }
                if (nesta == 0) break;
            }
            Animator[] animators = go.GetComponentsInChildren<Animator>(true);
            foreach (Animator a in animators) a.fireEvents = false; // os receptores dos eventos foram removidos
            AvatarAnimatorGuard.Registrar(animators);
        }

        // ---------------- Atraso (o efeito aparece junto com o avatar, que mostra o passado) ----------------

        private static readonly Queue<KeyValuePair<float, Action>> FilaEco = new Queue<KeyValuePair<float, Action>>();
        private static readonly Queue<KeyValuePair<float, Action>> FilaRede = new Queue<KeyValuePair<float, Action>>();

        private static void NaFilaDoEco(Action a) => FilaEco.Enqueue(new KeyValuePair<float, Action>(Time.unscaledTime + CoopRuntime.AtrasoEco + AtrasoMovimento, a));

        private static void NaFilaDaRede(Action a) => FilaRede.Enqueue(new KeyValuePair<float, Action>(Time.unscaledTime + AtrasoMovimento, a));

        private static void ProcessarFilas()
        {
            float agora = Time.unscaledTime;
            if (AvatarEco == null) FilaEco.Clear();
            while (FilaEco.Count > 0 && FilaEco.Peek().Key <= agora) Executar(FilaEco.Dequeue().Value);
            while (FilaRede.Count > 0 && FilaRede.Peek().Key <= agora) Executar(FilaRede.Dequeue().Value);
        }

        private static void Executar(Action a)
        {
            try { a(); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Fx] {(e.InnerException ?? e).Message}"); }
        }

        // ---------------- Laço e rede ----------------

        public static void Atualizar()
        {
            try
            {
                if (AvatarEco != null) Eco.Avatar = AvatarEco;
                Eco.Deslocamento = AvatarEco != null ? AvatarEco.DeslocamentoMundo : Vector3.zero;
                AtualizarLocais();
                ProcessarFilas();
                AtualizarCopias();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Fx] Erro no laço: {(e.InnerException ?? e).Message}");
            }
        }

        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            try
            {
                Remetente r = DePar(de);
                switch (dados[0])
                {
                    case Protocolo.TipoFxNovo:
                        FxNovo n = FxNovo.Desserializar(dados);
                        NaFilaDaRede(() => { r.Avatar = AvatarDe(r.Chave); if (r.Avatar != null) Criar(r, n); });
                        break;
                    case Protocolo.TipoFxMovimento:
                        FxMovimento mv = FxMovimento.Desserializar(dados);
                        NaFilaDaRede(() => AplicarMovimento(r, mv));
                        break;
                    case Protocolo.TipoFxNos:
                        FxNos nos = FxNos.Desserializar(dados);
                        NaFilaDaRede(() => AplicarNos(r, nos));
                        break;
                    case Protocolo.TipoFxFim:
                        ushort fim = FxFim.Desserializar(dados).Id;
                        NaFilaDaRede(() => Remover(r, fim));
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Fx] Mensagem {dados[0]} de {de.Nome} ignorada: {(e.InnerException ?? e).Message}");
            }
        }

        public static void Encerrar()
        {
            foreach (ulong k in new List<ulong>(Copias.Keys)) if (k != 0) DestruirDe(k);
            FilaRede.Clear();
        }
    }
}
