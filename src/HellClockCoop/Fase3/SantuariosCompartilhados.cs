using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HellClock.Blessings;
using HellClock.Interactables;
using HellClock.Level;
using HellClock.Pool;
using HellClock.Shrine;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Santuários e totens de bênção iguais para todos (sorteados pelo host em LevelObjects.SetupLevelObjects,
    /// que é pulado no convidado).
    ///
    /// Santuário comum (cura, status): quem usar ativa para todos; cada um recebe o efeito no próprio personagem.
    /// Santuário amaldiçoado: quem ativar começa o evento de sobrevivência no HOST (as ondas chegam ao convidado
    /// como inimigos replicados). Quando o host purifica, cada um ganha o próprio ponto de constelação.
    /// Totem de bênção: aparece no mesmo lugar, mas a escolha de bênção é individual (cada um compra a sua, com o
    /// próprio ouro) e só aparece para quem já liberou os totens no próprio save.
    /// </summary>
    internal static class SantuariosCompartilhados
    {
        private const float IntervaloVarredura = 1f;
        private const float ValidadePendente = 90f;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;
        private static bool Host => RunCompartilhada.Host;
        private static bool Convidado => RunCompartilhada.Convidado;

        private static readonly AccessTools.FieldRef<ShrineInteractable, ShrineDefinition> DefinicaoRef =
            AccessTools.FieldRefAccess<ShrineInteractable, ShrineDefinition>("ShrineDefinition");
        private static readonly AccessTools.FieldRef<CursedShrineInteractable, bool> PurificadoRef =
            AccessTools.FieldRefAccess<CursedShrineInteractable, bool>("_claimed");
        private static readonly AccessTools.FieldRef<CursedShrineInteractable, float> RaioRef =
            AccessTools.FieldRefAccess<CursedShrineInteractable, float>("_radius");
        private static readonly MethodInfo FimSobrevivencia = AccessTools.Method(typeof(CursedShrineInteractable), "OnSurvivalEnd");
        private static readonly PropertyInfo TotemLiberado = AccessTools.Property(typeof(LevelObjects), "TotemSpawnUnlocked");

        private static readonly Dictionary<ushort, Interactable> PorId = new Dictionary<ushort, Interactable>();
        private static readonly Dictionary<Interactable, ushort> IdDe = new Dictionary<Interactable, ushort>();
        private static readonly Dictionary<ushort, ObjetoSpawn> DoHost = new Dictionary<ushort, ObjetoSpawn>();
        private static readonly Dictionary<ushort, ObjetoSpawn> Pendentes = new Dictionary<ushort, ObjetoSpawn>();
        private static readonly Dictionary<ushort, float> RecebidoEm = new Dictionary<ushort, float>();
        private static ushort _proximoId = 1;
        private static long _andar = -1;
        private static float _proximaVarredura;

        /// <summary>Usando por ordem de outro jogador: não avisar de volta nem mexer no jogador local.</summary>
        internal static bool OrdemRemota { get; private set; }

        private static LevelInfo AndarDeRun()
        {
            LevelInfo a = LocalPlayerSampler.AndarAtual();
            return a != null && a.LevelBlockInfo != null ? a : null;
        }

        public static void Atualizar()
        {
            if (Sessao == null) return;
            ExpirarPendentes();
            LevelInfo andar = AndarDeRun();
            if (andar == null)
            {
                if (LocalPlayerSampler.AndarAtual() != null && _andar != -1) Encerrar();
                return;
            }
            if (andar.LevelIndex != _andar)
            {
                _andar = andar.LevelIndex;
                PorId.Clear();
                IdDe.Clear();
                DoHost.Clear();
                _proximaVarredura = 0f;
            }
            if (Convidado && Pendentes.Count > 0) CriarPendentes(andar);
            if (Time.unscaledTime < _proximaVarredura) return;
            _proximaVarredura = Time.unscaledTime + IntervaloVarredura;
            if (Host) Varrer(andar);
            else if (Convidado) EsconderLocais();
        }

        // ---------------- Host ----------------

        private static void Varrer(LevelInfo andar)
        {
            foreach (ShrineInteractable s in UnityEngine.Object.FindObjectsByType<ShrineInteractable>(FindObjectsSortMode.None))
            {
                if (s == null || IdDe.ContainsKey(s) || s.transform.parent == null) continue;
                ShrineDefinition def = DefinicaoRef(s);
                if (def == null) continue;
                var m = new ObjetoSpawn { Andar = andar.LevelIndex, Tipo = ObjetoSpawn.Santuario, DefinicaoHash = def.HashID, Posicao = s.transform.parent.position };
                if (s is CursedShrineInteractable c) m.Raio = RaioRef(c);
                Registrar(s, m);
            }
            foreach (BlessingTotemInteractable t in UnityEngine.Object.FindObjectsByType<BlessingTotemInteractable>(FindObjectsSortMode.None))
            {
                if (t == null || IdDe.ContainsKey(t) || t.transform.parent == null) continue;
                Registrar(t, new ObjetoSpawn { Andar = andar.LevelIndex, Tipo = ObjetoSpawn.Totem, Posicao = t.transform.parent.position });
            }
        }

        private static void Registrar(Interactable obj, ObjetoSpawn m)
        {
            m.Id = _proximoId++;
            if (_proximoId == 0) _proximoId = 1;
            m.Estado = EstadoDe(obj);
            PorId[m.Id] = obj;
            IdDe[obj] = m.Id;
            DoHost[m.Id] = m;
            if (Sessao.ComAliados) Sessao.EnviarParaConvidados(m.Serializar(), confiavel: true);
        }

        private static byte EstadoDe(Interactable obj)
        {
            if (obj is CursedShrineInteractable c && PurificadoRef(c)) return ObjetoSpawn.Concluido;
            return obj.CanBeInteractedWith ? ObjetoSpawn.Livre : ObjetoSpawn.Usado;
        }

        internal static void AoNovoConvidado(SessaoCoop sessao, Par p)
        {
            foreach (var kv in DoHost)
            {
                if (kv.Value.Tipo == ObjetoSpawn.Santuario && PorId.TryGetValue(kv.Key, out Interactable o) && o != null) kv.Value.Estado = EstadoDe(o);
                sessao.EnviarPara(p, kv.Value.Serializar());
            }
        }

        /// <summary>Postfix de CursedShrineInteractable.OnSurvivalEnd: o host purificou o santuário.</summary>
        internal static void Purificado(CursedShrineInteractable s, bool concluido)
        {
            if (!concluido || OrdemRemota || !Host || Sessao == null || !Sessao.ComAliados || !IdDe.TryGetValue(s, out ushort id)) return;
            Sessao.EnviarParaConvidados(new ObjetoUsado { Id = id, Estado = ObjetoSpawn.Concluido }.Serializar(), confiavel: true);
        }

        // ---------------- Convidado ----------------

        private static void CriarPendentes(LevelInfo andar)
        {
            List<ushort> remover = null;
            foreach (var kv in Pendentes)
            {
                if (kv.Value.Andar > andar.LevelIndex) continue;
                (remover ?? (remover = new List<ushort>())).Add(kv.Key);
                if (kv.Value.Andar == andar.LevelIndex) Criar(andar, kv.Value);
            }
            if (remover != null) foreach (ushort id in remover) { Pendentes.Remove(id); RecebidoEm.Remove(id); }
        }

        private static void Criar(LevelInfo andar, ObjetoSpawn s)
        {
            if (PorId.ContainsKey(s.Id) || LevelObjectPoolController.Instance == null) return;
            try
            {
                GameObject go;
                Interactable obj;
                if (s.Tipo == ObjetoSpawn.Totem)
                {
                    // Totem é progressão do save: só aparece para quem já liberou (sorteio de bênçãos próprio).
                    bool liberado = TotemLiberado == null || (bool)TotemLiberado.GetValue(null, null);
                    if (!liberado) { Plugin.Log.LogInfo("[Objetos] Totem do host ignorado: você ainda não liberou os totens de bênção."); return; }
                    go = LevelObjectPoolController.Instance.SpawnBlessingTotem(s.Posicao);
                    obj = go != null ? go.GetComponentInChildren<BlessingTotemInteractable>() : null;
                }
                else
                {
                    ShrineDefinition def = global::HellClock.Constants.Collections.SHRINE_DEFINITION_COLLECTION.Reference.GetElement(s.DefinicaoHash);
                    if (def == null) return;
                    go = LevelObjectPoolController.Instance.SpawnShrine(s.Posicao, def, s.Raio);
                    obj = go != null ? go.GetComponentInChildren<ShrineInteractable>() : null;
                    if (obj != null && s.Estado != ObjetoSpawn.Livre) MarcarUsado((ShrineInteractable)obj);
                }
                if (obj == null) return;
                PorId[s.Id] = obj;
                IdDe[obj] = s.Id;
                andar.LevelObjects?.LevelObjectsList?.Add(go);
                Plugin.Log.LogInfo($"[Objetos] {(s.Tipo == ObjetoSpawn.Totem ? "Totem" : "Santuário")} {s.Id} do host em {s.Posicao}{(s.Estado != ObjetoSpawn.Livre ? ", já usado" : "")}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Objetos] Falha ao criar o objeto {s.Id}: {(e.InnerException ?? e).Message}");
            }
        }

        /// <summary>Quem entra depois que o santuário já foi usado: só desliga (sem efeito, ele já foi dado na hora).</summary>
        private static void MarcarUsado(ShrineInteractable s)
        {
            s.SetBaseInteractionAvailable(false);
            if (s.HoverFeedback != null) s.HoverFeedback.enabled = false;
            if (s.ShrineNameText != null) s.ShrineNameText.alpha = 0f;
        }

        private static void EsconderLocais()
        {
            foreach (ShrineInteractable s in UnityEngine.Object.FindObjectsByType<ShrineInteractable>(FindObjectsSortMode.None)) Esconder(s);
            foreach (BlessingTotemInteractable t in UnityEngine.Object.FindObjectsByType<BlessingTotemInteractable>(FindObjectsSortMode.None)) Esconder(t);
        }

        private static void Esconder(Interactable o)
        {
            if (o == null || IdDe.ContainsKey(o)) return;
            GameObject raiz = o.transform.parent != null ? o.transform.parent.gameObject : o.gameObject;
            Plugin.Log.LogInfo($"[Objetos] '{raiz.name}' local escondido (santuários e totens são os do host).");
            raiz.SetActive(false);
        }

        private static void ExpirarPendentes()
        {
            if (RecebidoEm.Count == 0) return;
            List<ushort> velhos = null;
            foreach (var kv in RecebidoEm) if (Time.unscaledTime - kv.Value > ValidadePendente) (velhos ?? (velhos = new List<ushort>())).Add(kv.Key);
            if (velhos != null) foreach (ushort id in velhos) { RecebidoEm.Remove(id); Pendentes.Remove(id); }
        }

        // ---------------- Uso ----------------

        /// <summary>Prefixo de ShrineInteractable.ExecuteInteraction: o jogador local usou um santuário compartilhado.</summary>
        internal static void UsouSantuario(ShrineInteractable s)
        {
            if (OrdemRemota || Sessao == null || !Sessao.ComAliados || !s.CanBeInteractedWith || !IdDe.TryGetValue(s, out ushort id)) return;
            byte[] m = new ObjetoUsado { Id = id, Estado = ObjetoSpawn.Usado }.Serializar();
            if (Host) Sessao.EnviarParaConvidados(m, confiavel: true);
            else if (Convidado && Sessao.Host != null) Sessao.EnviarPara(Sessao.Host, m);
        }

        private static void UsarPorOrdem(ObjetoUsado u, Par de)
        {
            if (Pendentes.TryGetValue(u.Id, out ObjetoSpawn p)) { p.Estado = u.Estado; return; }
            if (!PorId.TryGetValue(u.Id, out Interactable o) || !(o is ShrineInteractable s) || s == null) return;
            OrdemRemota = true;
            try
            {
                if (u.Estado == ObjetoSpawn.Usado && s.CanBeInteractedWith)
                {
                    // Comum: efeito no jogador local. Amaldiçoado: no host começa a sobrevivência; no convidado só a animação.
                    s.ExecuteInteraction();
                    Plugin.Log.LogInfo($"[Objetos] {de.Nome} ativou o santuário {u.Id}.");
                }
                else if (u.Estado == ObjetoSpawn.Concluido && s is CursedShrineInteractable c && !PurificadoRef(c))
                {
                    FimSobrevivencia?.Invoke(c, new object[] { true }); // purifica e dá o ponto de constelação a este jogador
                    Plugin.Log.LogInfo($"[Objetos] Santuário amaldiçoado {u.Id} purificado pelo host.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Objetos] Santuário {u.Id}: {(e.InnerException ?? e).Message}");
            }
            finally
            {
                OrdemRemota = false;
            }
        }

        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            try
            {
                switch (dados[0])
                {
                    case Protocolo.TipoObjetoSpawn:
                        if (!Convidado || de != sessao.Host) return;
                        ObjetoSpawn s = ObjetoSpawn.Desserializar(dados);
                        if (!PorId.ContainsKey(s.Id)) { Pendentes[s.Id] = s; RecebidoEm[s.Id] = Time.unscaledTime; }
                        break;
                    case Protocolo.TipoObjetoUsado:
                        ObjetoUsado u = ObjetoUsado.Desserializar(dados);
                        // Purificação (Concluido) dá ponto de constelação no save de quem recebe: só o host decide.
                        if (!Validacao.ObjetoUsadoAceito(u.Estado, de == sessao.Host))
                        {
                            Plugin.Log.LogWarning($"[Objetos] Purificação do santuário {u.Id} vinda de {de.Nome} (não é o host); ignorada.");
                            return;
                        }
                        UsarPorOrdem(u, de);
                        if (Host)
                        {
                            foreach (Par p in sessao.Pares.Values) if (p != de && p.Compativel && p.DlcOk) sessao.EnviarPara(p, dados);
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Objetos] Mensagem {dados[0]} de {de.Nome} ignorada: {(e.InnerException ?? e).Message}");
            }
        }

        public static void Encerrar()
        {
            PorId.Clear();
            IdDe.Clear();
            DoHost.Clear();
            Pendentes.Clear();
            RecebidoEm.Clear();
            _andar = -1;
        }
    }
}
