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

        /// <summary>Verdadeiro enquanto o mod cria um efeito só deste PC (TpEfeito, 0.30.0): nem o efeito nem o som vão aos aliados.</summary>
        internal static bool IgnorarCriados;

        // Efeitos só deste PC e até quando valem (o som pode ser ativado depois do CreateFx; o pool reaproveita o objeto).
        private static readonly Dictionary<GameObject, float> SoLocais = new Dictionary<GameObject, float>();
        private static readonly List<GameObject> SoLocaisVencidos = new List<GameObject>();
        private const float DuracaoSoLocal = 3f;

        /// <summary>Marca um efeito criado pelo mod como só deste PC por DuracaoSoLocal (SonsAliados não captura o som dele).</summary>
        internal static void MarcarSoLocal(GameObject go)
        {
            if (go == null) return;
            float agora = Time.unscaledTime;
            SoLocaisVencidos.Clear();
            foreach (KeyValuePair<GameObject, float> kv in SoLocais) if (kv.Key == null || kv.Value < agora) SoLocaisVencidos.Add(kv.Key);
            foreach (GameObject g in SoLocaisVencidos) SoLocais.Remove(g);
            SoLocais[go] = agora + DuracaoSoLocal;
        }

        internal static bool SoLocal(GameObject go) =>
            SoLocais.Count > 0 && go != null && SoLocais.TryGetValue(go, out float ate) && Time.unscaledTime <= ate;

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

        /// <summary>Id do efeito rastreado que contém o objeto (ele ou um pai até 8 níveis acima): som preso a um efeito (etapa 8).</summary>
        internal static bool IdDoEfeito(GameObject go, out ushort id)
        {
            id = 0;
            Transform t = go != null ? go.transform : null;
            for (int n = 0; t != null && n < 8; n++, t = t.parent)
            {
                if (Locais.TryGetValue(t.gameObject, out FxLocal f))
                {
                    id = f.Id;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Cópia do efeito <paramref name="id"/> de quem mandou (0 = eco), ou null se ainda não carregou.</summary>
        internal static GameObject CopiaDe(ulong chave, ushort id) =>
            Copias.TryGetValue(chave, out var dele) && dele.TryGetValue(id, out Copia c) && c.Go != null ? c.Go : null;

        /// <summary>Postfix de GameplayFx.CreateFx.</summary>
        internal static void EfeitoCriado(GameplayFx fx, ISkillCaster caster, Skill skill, GameObject go)
        {
            if (go == null || IgnorarCriados) return; // rastreia sempre: quem chegar depois (ou o eco ligado depois) recebe os efeitos que já existem
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
            if (AvatarEco != null)
            {
                ObterModelo(m.Guid, out _); // como na rede: o modelo fica pronto durante o atraso da fila
                NaFilaDoEco(() => Criar(Eco, m, Time.unscaledTime));
            }
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
            if (!Copias.TryGetValue(de.Chave, out var dele) || !dele.TryGetValue(m.Id, out Copia c) || c.Go == null)
            {
                // A cópia espera o prefab: guarda para depois. Quem lança nunca manda mais que MaximoNos partes por efeito:
                // mensagem maior que isso não é guardada (seria aplicada de uma vez no quadro em que o prefab chega).
                Pendente p = _pendentes > 0 ? AcharPendente(de.Chave, m.Id) : null;
                if (p != null && p.Nos.Count < MaximoNosPendentes && m.Nos.Count <= MaximoNos) p.Nos.Add(m);
                return;
            }
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
        // 0.30.3: um modelo por prefab, já limpo (sem scripts, colisores e som), guardado inativo no estacionamento. A cópia
        // de cada efeito sai dele, sem repetir a limpeza; o prefab chega do Addressables sem segurar o quadro (Carregando).
        private static readonly Dictionary<string, GameObject> Modelos = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> PrefabsRuins = new HashSet<string>();
        private static readonly Dictionary<string, AsyncOperationHandle<GameObject>> Carregando = new Dictionary<string, AsyncOperationHandle<GameObject>>();
        private static readonly List<string> Chegaram = new List<string>();
        // Cópias à espera do prefab, por remetente e id (a busca é direta: o movimento chega com até 255 itens por mensagem).
        private static readonly Dictionary<ulong, Dictionary<ushort, Pendente>> Pendentes = new Dictionary<ulong, Dictionary<ushort, Pendente>>();
        private static readonly List<Pendente> Prontos = new List<Pendente>();
        private static readonly List<ulong> ChavesPendentes = new List<ulong>();
        private static int _pendentes;            // total, de todos os remetentes
        private const int MaximoCarregando = 64;  // prefabs a caminho ao mesmo tempo; acima disso o efeito novo é ignorado
        private const int MaximoPendentes = 96;   // cópias à espera do prefab (o mesmo teto dos efeitos ativos de quem lança)
        private const int MaximoPendentesPorPar = 32; // um par sozinho não ocupa as vagas dos outros
        private const int MaximoNosPendentes = 16; // mensagens de partes guardadas por cópia à espera
        private static GameObject _estacionamento;
        private static int _copiasLogadas;
        private static readonly Remetente Eco = new Remetente { Chave = 0 };

        /// <summary>
        /// Cópia à espera do prefab: o que chega para ela nesse meio-tempo (partes, posição, fim) fica guardado aqui e vale
        /// quando ela nasce. Sem isso, o primeiro estado das partes, que chega logo depois do anúncio, se perderia.
        /// </summary>
        private sealed class Pendente
        {
            public Remetente De;
            public FxNovo Novo;
            public readonly List<FxNos> Nos = new List<FxNos>();
            public bool SemVaga; // 0.31.1: o prefab já chegou; só espera vaga no teto de cópias por quadro
            public float Desde;  // quando devia ter nascido (o efeito solto atrasado demais é descartado)
            public int Ordem;    // ordem de chegada: os que esperam vaga nascem nela
        }

        private static int _copiasNoQuadro; // cópias criadas neste quadro (teto: Validacao.CopiasFxPorQuadroMaximo)
        private static double _msCopiasNoQuadro; // tempo gasto neste quadro em cópias e modelos (orçamento: Validacao.CopiasFxPorQuadroMs)

        /// <summary>Vagas de cópia já usadas neste quadro, pela contagem e pelo tempo (uma cópia pesada fecha o quadro).</summary>
        private static int VagasUsadas =>
            Validacao.VagasUsadas(_copiasNoQuadro, _msCopiasNoQuadro, Validacao.CopiasFxPorQuadroMaximo, Validacao.CopiasFxPorQuadroMs);

        private static void SomarTempoDeCopia(double inicio) => _msCopiasNoQuadro += (Desempenho.Agora() - inicio) * 1000.0;
        private static int _ordemPendente;
        private static readonly List<Pendente> SemVaga = new List<Pendente>();

        private sealed class Remetente
        {
            public ulong Chave;
            public RemotePlayerAvatar Avatar;
            public Vector3 Deslocamento;
        }

        private static Remetente DePar(Par p) => new Remetente { Chave = p.Id.m_SteamID, Avatar = p.Avatar };

        private static Transform Estacionamento()
        {
            if (_estacionamento == null)
            {
                _estacionamento = new GameObject("HellClockCoop_FxEstacionamento");
                _estacionamento.SetActive(false); // nada acorda aqui dentro: os scripts do jogo saem antes de a cópia ir para a cena
                UnityEngine.Object.DontDestroyOnLoad(_estacionamento);
            }
            return _estacionamento.transform;
        }

        /// <summary>
        /// Modelo limpo do efeito. Prefab ainda não pedido: pede ao Addressables sem esperar (até a 0.30.2 era
        /// WaitForCompletion, que segurava o quadro quando o aliado usava um efeito que este PC ainda não tinha carregado).
        /// Null com <paramref name="carregando"/> = true: o prefab está a caminho e a cópia deve esperar (Adiar).
        /// </summary>
        private static GameObject ObterModelo(string guid, out bool carregando)
        {
            carregando = false;
            if (PrefabsRuins.Contains(guid)) return null;
            if (Modelos.TryGetValue(guid, out GameObject modelo) && modelo != null) return modelo;
            if (Carregando.ContainsKey(guid)) { carregando = true; return null; }
            if (Carregando.Count >= MaximoCarregando) return null;
            AsyncOperationHandle<GameObject> h;
            try
            {
                double inicio = Desempenho.Agora();
                h = Addressables.LoadAssetAsync<GameObject>(guid);
                Desempenho.SomarDetalhe(Desempenho.Detalhe.CarregarPrefabFx, inicio);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Fx] Prefab {guid} não carregou: {(e.InnerException ?? e).Message}");
                PrefabsRuins.Add(guid);
                return null;
            }
            if (h.IsDone) return MontarModelo(guid, h); // já estava na memória (o jogo usa o mesmo efeito): sai na hora
            Carregando[guid] = h;
            carregando = true;
            return null;
        }

        /// <summary>Prefab chegou: faz o modelo limpo (uma vez por prefab). Null: prefab indisponível ou cópia com erro.</summary>
        private static GameObject MontarModelo(string guid, AsyncOperationHandle<GameObject> h)
        {
            GameObject prefab = null;
            try
            {
                prefab = h.Status == AsyncOperationStatus.Succeeded ? h.Result : null; // o handle fica aberto: o prefab é reusado
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Fx] Prefab {guid} não carregou: {(e.InnerException ?? e).Message}");
            }
            if (prefab == null)
            {
                PrefabsRuins.Add(guid);
                Plugin.Log.LogWarning($"[Fx] Prefab {guid} indisponível neste jogo.");
                return null;
            }
            GameObject modelo = null;
            try
            {
                double inicio = Desempenho.Agora();
                modelo = UnityEngine.Object.Instantiate(prefab, Estacionamento());
                modelo.name = prefab.name;
                LimparParaVisual(modelo);
                Desempenho.SomarDetalhe(Desempenho.Detalhe.CopiarFx, inicio);
                SomarTempoDeCopia(inicio); // o modelo de um prefab novo é o mais caro: a cópia dele fica para o quadro seguinte
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Fx] Cópia de {guid} falhou: {(e.InnerException ?? e).Message}");
                if (modelo != null) UnityEngine.Object.Destroy(modelo);
                PrefabsRuins.Add(guid);
                return null;
            }
            Modelos[guid] = modelo;
            return modelo;
        }

        /// <summary>No laço: prefabs que chegaram viram modelo e as cópias que esperavam por eles nascem, na ordem de chegada.</summary>
        private static void ConcluirCargas()
        {
            if (Carregando.Count == 0) return;
            Chegaram.Clear();
            foreach (KeyValuePair<string, AsyncOperationHandle<GameObject>> kv in Carregando) if (kv.Value.IsDone) Chegaram.Add(kv.Key);
            foreach (string guid in Chegaram)
            {
                AsyncOperationHandle<GameObject> h = Carregando[guid];
                Carregando.Remove(guid);
                bool pronto = MontarModelo(guid, h) != null;
                Prontos.Clear();
                foreach (Dictionary<ushort, Pendente> dele in Pendentes.Values)
                {
                    foreach (Pendente p in dele.Values) if (p.Novo.Guid == guid) Prontos.Add(p);
                }
                foreach (Pendente p in Prontos)
                {
                    TirarPendente(p.De.Chave, p.Novo.Id);
                    if (!pronto) continue;
                    try
                    {
                        if (p.De != Eco) p.De.Avatar = AvatarDe(p.De.Chave); // o aliado pode ter saído do andar enquanto o prefab vinha
                        Criar(p.De, p.Novo, Time.unscaledTime); // esperou o prefab, não o teto: nasce mesmo atrasada (como até a 0.31.0)
                        foreach (FxNos nos in p.Nos) AplicarNos(p.De, nos);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"[Fx] {(e.InnerException ?? e).Message}");
                    }
                }
            }
        }

        private static Pendente AcharPendente(ulong chave, ushort id) =>
            Pendentes.TryGetValue(chave, out Dictionary<ushort, Pendente> dele) && dele.TryGetValue(id, out Pendente p) ? p : null;

        private static void TirarPendente(ulong chave, ushort id)
        {
            if (!Pendentes.TryGetValue(chave, out Dictionary<ushort, Pendente> dele) || !dele.Remove(id)) return;
            _pendentes--;
            if (dele.Count == 0) Pendentes.Remove(chave);
        }

        private static void TirarPendentesDe(ulong chave)
        {
            if (!Pendentes.TryGetValue(chave, out Dictionary<ushort, Pendente> dele)) return;
            _pendentes -= dele.Count;
            Pendentes.Remove(chave);
        }

        /// <summary>
        /// Guarda a cópia que ainda não pode nascer: o prefab está a caminho ou (<paramref name="semVaga"/>, 0.31.1) o
        /// quadro já criou o teto de cópias. Enquanto espera, o que chega para ela (partes, posição, fim) vale como antes.
        /// </summary>
        private static void Adiar(Remetente de, FxNovo m, bool semVaga, float desde)
        {
            TirarPendente(de.Chave, m.Id); // o mesmo efeito anunciado de novo: vale o último anúncio
            bool cabe = _pendentes < MaximoPendentes;
            Dictionary<ushort, Pendente> dele = null;
            if (cabe && !Pendentes.TryGetValue(de.Chave, out dele)) Pendentes[de.Chave] = dele = new Dictionary<ushort, Pendente>();
            if (cabe && dele.Count >= MaximoPendentesPorPar) cabe = false;
            if (!cabe)
            {
                if (semVaga) Desempenho.Descartou(Desempenho.Cosmetico.CopiaFx, 1); // sem lugar na espera: esta cópia não nasce
                return;
            }
            dele[m.Id] = new Pendente { De = de, Novo = m, SemVaga = semVaga, Desde = desde, Ordem = _ordemPendente++ };
            _pendentes++;
        }

        /// <summary>
        /// No laço, antes das filas: as cópias que ficaram sem vaga nascem na ordem em que chegaram, até o teto do quadro.
        /// O efeito solto no mundo que esperou demais é descartado; o preso no jogador ou num inimigo (auras, estacas)
        /// só espera. O fim do efeito tira a cópia da espera antes de ela nascer (Remover).
        /// </summary>
        private static void CriarAdiadas()
        {
            if (_pendentes == 0) return;
            SemVaga.Clear();
            foreach (Dictionary<ushort, Pendente> dele in Pendentes.Values)
            {
                foreach (Pendente p in dele.Values) if (p.SemVaga) SemVaga.Add(p);
            }
            if (SemVaga.Count == 0) return;
            if (SemVaga.Count > 1) SemVaga.Sort((a, b) => a.Ordem.CompareTo(b.Ordem));
            float agora = Time.unscaledTime;
            foreach (Pendente p in SemVaga)
            {
                int decisao = Validacao.DecidirNaFila(p.Desde, agora, VagasUsadas, Validacao.CopiasFxPorQuadroMaximo, p.Novo.TipoPai == FxNovo.PaiMundo);
                if (decisao == Validacao.FilaEsperar)
                {
                    Desempenho.NoTeto(Desempenho.Cosmetico.CopiaFx);
                    break;
                }
                TirarPendente(p.De.Chave, p.Novo.Id);
                try
                {
                    if (p.De != Eco) p.De.Avatar = AvatarDe(p.De.Chave); // o aliado pode ter saído do andar enquanto a cópia esperava
                    Criar(p.De, p.Novo, p.Desde); // (a mesma decisão: cria, ou descarta e conta o efeito solto atrasado)
                    foreach (FxNos nos in p.Nos) AplicarNos(p.De, nos);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Fx] {(e.InnerException ?? e).Message}");
                }
            }
            SemVaga.Clear();
        }

        /// <summary>
        /// Cria a cópia do efeito. <paramref name="vence"/>: quando ela devia nascer (0.31.1). Com o teto de cópias do
        /// quadro já usado, fica na espera (Adiar) e nasce num quadro seguinte; efeito solto atrasado demais não nasce.
        /// </summary>
        private static void Criar(Remetente de, FxNovo m, float vence)
        {
            if (de.Avatar == null || !de.Avatar.gameObject.activeInHierarchy) return;
            GameObject prefab = ObterModelo(m.Guid, out bool carregando);
            if (prefab == null)
            {
                if (carregando) Adiar(de, m, semVaga: false, Time.unscaledTime);
                return;
            }
            // Quem lança nunca tem mais que MaximoAtivos efeitos vivos; até a 0.31.0 quem recebia não conferia.
            if (Copias.TryGetValue(de.Chave, out var vivas) && vivas.Count >= MaximoAtivos && !vivas.ContainsKey(m.Id)) return;
            int decisao = Validacao.DecidirNaFila(vence, Time.unscaledTime, VagasUsadas, Validacao.CopiasFxPorQuadroMaximo, m.TipoPai == FxNovo.PaiMundo);
            if (decisao == Validacao.FilaDescartar)
            {
                Desempenho.Descartou(Desempenho.Cosmetico.CopiaFx, 1);
                return;
            }
            if (decisao == Validacao.FilaEsperar)
            {
                Desempenho.NoTeto(Desempenho.Cosmetico.CopiaFx);
                Adiar(de, m, semVaga: true, vence);
                return;
            }
            _copiasNoQuadro++;
            GameObject go;
            double inicio = Desempenho.Agora();
            try
            {
                go = UnityEngine.Object.Instantiate(prefab, Estacionamento());
                Animator[] animators = go.GetComponentsInChildren<Animator>(true);
                foreach (Animator a in animators) a.fireEvents = false; // os receptores dos eventos saíram na limpeza do modelo
                AvatarAnimatorGuard.Registrar(animators);
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
                if (paiInimigo == null) // o inimigo já sumiu
                {
                    UnityEngine.Object.Destroy(go);
                    SomarTempoDeCopia(inicio);
                    if (de != Eco) Desempenho.Aplicou(Desempenho.Cosmetico.CopiaFx, inicio);
                    return;
                }
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
            SomarTempoDeCopia(inicio); // as do eco também gastam o orçamento do quadro
            if (de != Eco) Desempenho.Aplicou(Desempenho.Cosmetico.CopiaFx, inicio); // a cópia inteira: instanciar, prender e ligar as partículas (o eco fica fora da conta da recepção)
        }

        private static void AplicarMovimento(Remetente de, FxMovimento m)
        {
            Copias.TryGetValue(de.Chave, out var dele);
            if (dele == null && _pendentes == 0) return;
            float agora = Time.unscaledTime;
            foreach (FxMovimento.Item i in m.Itens)
            {
                if (dele == null || !dele.TryGetValue(i.Id, out Copia c) || c.Go == null)
                {
                    // A cópia espera o prefab: quando nascer, nasce na posição mais nova (só efeito solto recebe movimento).
                    Pendente p = _pendentes > 0 ? AcharPendente(de.Chave, i.Id) : null;
                    if (p != null && p.Novo.TipoPai == FxNovo.PaiMundo) { p.Novo.Posicao = i.Posicao; p.Novo.Rotacao = i.Rotacao; }
                    continue;
                }
                if (!c.Solta) continue;
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
            if (_pendentes > 0) TirarPendente(de.Chave, id); // acabou antes de o prefab chegar: a cópia nem nasce
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
            Par p = Sessao?.ParDe(steam); // um jogador do lobby ou, na recepção simulada, um aliado de mentira
            return p != null && p.Avatar != null && p.Avatar.gameObject.activeInHierarchy && p.MesmoAndar ? p.Avatar : null;
        }

        private static void DestruirDe(ulong chave)
        {
            TirarPendentesDe(chave);
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

        private static int _emissoresLogados;

        private static void LimparParaVisual(GameObject go)
        {
            // Etapa 8: o som dos efeitos vem pela rede (SonsAliados). Se algum prefab tiver um emissor próprio do FMOD,
            // ele é removido aqui junto com os scripts; o log diz quais, para saber se falta algum som.
            int emissores = 0;
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
            {
                if (c != null && c.GetType().Name == "StudioEventEmitter") emissores++;
            }
            if (emissores > 0 && _emissoresLogados < 8)
            {
                _emissoresLogados++;
                Plugin.Log.LogInfo($"[Fx] Cópia de '{go.name}' sem {emissores} emissor(es) de som próprios do FMOD (StudioEventEmitter).");
            }
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
            // (os Animators são preparados em cada cópia, no Criar: fireEvents não passa do modelo para a cópia)
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
                _copiasNoQuadro = 0;
                _msCopiasNoQuadro = 0;
                AtualizarLocais();
                ConcluirCargas(); // antes das filas: a cópia que vence agora já acha o modelo que chegou
                CriarAdiadas();   // antes das filas: as que esperavam vaga nascem antes das que vencem agora
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
            // 0.21.1: efeito novo ou que se move só de quem está no mesmo andar (o fim passa sempre: só remove a cópia).
            if (dados[0] != Protocolo.TipoFxFim && !de.MesmoAndar) return;
            try
            {
                Remetente r = DePar(de);
                switch (dados[0])
                {
                    case Protocolo.TipoFxNovo:
                        FxNovo n = FxNovo.Desserializar(dados);
                        if (AvatarDe(r.Chave) != null) ObterModelo(n.Guid, out _); // já pede o prefab (ele vem durante o atraso da fila), só de quem tem avatar no andar
                        float vence = Time.unscaledTime + AtrasoMovimento;
                        NaFilaDaRede(() => { r.Avatar = AvatarDe(r.Chave); if (r.Avatar != null) Criar(r, n, vence); });
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
            ChavesPendentes.Clear();
            foreach (ulong k in Pendentes.Keys) if (k != 0) ChavesPendentes.Add(k);
            foreach (ulong k in ChavesPendentes) TirarPendentesDe(k);
            FilaRede.Clear();
        }
    }
}
