using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HellClock.Level;
using HellClock.Player;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Barreiras de sangue (SmokeDoor do jogo) iguais para todos (0.9.5).
    ///
    /// No jogo, elas fecham em SurvivalEventManager.StartSurvival (andar de sobrevivência e santuário amaldiçoado,
    /// bloqueado no convidado desde a Fase 2) e em roteiros Fungus (SmokeRoom/OpenCloseSmokeDoor). Em vez de seguir
    /// cada caminho, o host avisa toda porta que abre ou fecha, e reenvia as fechadas a cada 2 s (quem chega depois).
    /// O convidado aplica na porta mais próxima da posição (as portas fazem parte do bloco do andar); se a porta estiver
    /// dentro de um SmokeRoom desligado (o roteiro que liga a sala só roda no host), fecha pela sala, como o jogo faz.
    /// Quando a barreira fecha no convidado, ele vai para perto do host, como o jogo faz com os companheiros (decisão do
    /// autor, 21/11/2026: ninguém fica de fora da luta). Se o host sair, o convidado reabre o que fechou por ordem dele.
    /// </summary>
    internal static class PortasCompartilhadas
    {
        private const float IntervaloReenvio = 2f;
        private const float DistanciaPorta = 1.5f;
        private const float DistanciaTeleporte = 3f;
        private const float AlcanceTeleporte = 60f;   // posição do host tem de estar perto da barreira
        private const float ValidadeTeleporte = 5f;
        private const float ValidadePendente = 90f;
        private const int MaximoPendentes = 64;
        private const int EstadoFechada = 2; // SmokeDoor.EState (privado): NONE, OPENED, CLOSED

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;
        private static bool Host => RunCompartilhada.Host;
        private static bool Convidado => RunCompartilhada.Convidado;

        private static readonly FieldInfo EstadoCampo = AccessTools.Field(typeof(SmokeDoor), "_state");
        private static readonly AccessTools.FieldRef<SmokeRoom, List<SmokeDoor>> PortasDaSalaRef =
            AccessTools.FieldRefAccess<SmokeRoom, List<SmokeDoor>>("_smokeDoors");

        // Host: portas fechadas do andar atual, para o reenvio.
        private static readonly Dictionary<SmokeDoor, bool> FechadasHost = new Dictionary<SmokeDoor, bool>();
        private static long _andarHost = -1;
        private static float _proximoReenvio;

        // Convidado: última ordem por porta (andar + posição), aplicada quando o andar estiver carregado.
        private sealed class Ordem { public PortaFumaca M; public float RecebidaEm; }
        private static readonly Dictionary<string, Ordem> Pendentes = new Dictionary<string, Ordem>();
        private static readonly HashSet<string> AvisadasSemPorta = new HashSet<string>();
        private static readonly HashSet<SmokeDoor> FechadasPorOrdem = new HashSet<SmokeDoor>();
        private static readonly Dictionary<SmokeDoor, SmokeRoom> SalaDaPorta = new Dictionary<SmokeDoor, SmokeRoom>();
        private static long _andarConvidado = -1;
        private static float _ultimoTeleporte = -100f;

        /// <summary>Dentro de SmokeDoor.Start: o estado inicial é o mesmo nos dois jogos, não é aviso.</summary>
        internal static bool NoStart;

        /// <summary>Abrindo/fechando por ordem do host: não avisar de volta.</summary>
        internal static bool OrdemRemota { get; private set; }

        private static LevelInfo AndarDeRun()
        {
            LevelInfo a = LocalPlayerSampler.AndarAtual();
            return a != null && a.LevelBlockInfo != null ? a : null;
        }

        internal static int Estado(SmokeDoor p)
        {
            try { return p == null || EstadoCampo == null ? 0 : Convert.ToInt32(EstadoCampo.GetValue(p)); }
            catch { return 0; }
        }

        private static string Chave(uint andar, Vector3 pos) =>
            andar + ":" + Mathf.RoundToInt(pos.x * 2f) + ":" + Mathf.RoundToInt(pos.y * 2f) + ":" + Mathf.RoundToInt(pos.z * 2f);

        // ---------------- Host ----------------

        private static void AndarDoHost(long indice)
        {
            if (indice == _andarHost) return;
            _andarHost = indice;
            FechadasHost.Clear();
        }

        /// <summary>Postfix de SmokeDoor.Open/Close no host. <paramref name="antes"/> = estado antes da chamada.</summary>
        internal static void Mudou(SmokeDoor porta, int antes, bool animar)
        {
            // Registra mesmo sem convidado conectado ainda (quem entrar depois recebe pelo reenvio); Enviar só sai com aliados.
            if (OrdemRemota || NoStart || Convidado || Sessao == null || porta == null) return;
            try
            {
                LevelInfo andar = AndarDeRun();
                if (andar == null) return;
                int depois = Estado(porta);
                if (depois == antes) return;
                AndarDoHost(andar.LevelIndex);
                bool fechada = depois == EstadoFechada;
                if (fechada) FechadasHost[porta] = animar; else FechadasHost.Remove(porta);
                Enviar(andar.LevelIndex, porta, fechada, animar);
                Plugin.Log.LogInfo($"[Portas] Barreira em {porta.transform.position} {(fechada ? "fechou" : "abriu")} no host (andar {andar.LevelIndex}).");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Portas] Aviso da barreira falhou: {(e.InnerException ?? e).Message}"); }
        }

        private static void Enviar(uint andar, SmokeDoor porta, bool fechada, bool animar)
        {
            if (!Sessao.ComAliados) return;
            PlayerController host = CoopRuntime.JogadorLocal();
            byte[] m = new PortaFumaca
            {
                Andar = andar, Posicao = porta.transform.position, Fechada = fechada, Animar = animar,
                PosicaoHost = host != null ? host.transform.position : porta.transform.position,
            }.Serializar();
            Sessao.EnviarParaConvidados(m, confiavel: true);
        }

        // ---------------- Laço ----------------

        public static void Atualizar()
        {
            if (Sessao == null) return;
            if (!Convidado && FechadasPorOrdem.Count > 0) { Reabrir("o host saiu do coop"); Pendentes.Clear(); }
            LevelInfo andar = AndarDeRun();
            if (!Convidado) AtualizarHost(andar);
            else if (Convidado) AtualizarConvidado(andar);
        }

        private static void AtualizarHost(LevelInfo andar)
        {
            AndarDoHost(andar != null ? andar.LevelIndex : -1);
            if (andar == null || FechadasHost.Count == 0 || Time.unscaledTime < _proximoReenvio) return;
            _proximoReenvio = Time.unscaledTime + IntervaloReenvio;
            List<SmokeDoor> sumiram = null;
            foreach (KeyValuePair<SmokeDoor, bool> kv in FechadasHost)
            {
                if (kv.Key == null || Estado(kv.Key) != EstadoFechada) { (sumiram ??= new List<SmokeDoor>()).Add(kv.Key); continue; }
                Enviar(andar.LevelIndex, kv.Key, true, kv.Value);
            }
            if (sumiram != null) foreach (SmokeDoor p in sumiram) FechadasHost.Remove(p);
        }

        private static void AtualizarConvidado(LevelInfo andar)
        {
            long indice = andar != null ? andar.LevelIndex : -1;
            if (indice != _andarConvidado) { _andarConvidado = indice; AvisadasSemPorta.Clear(); FechadasPorOrdem.Clear(); SalaDaPorta.Clear(); }
            if (Pendentes.Count == 0) return;
            List<string> feitas = null;
            SmokeDoor[] portas = null;   // uma busca só por quadro
            SmokeRoom[] salas = null;
            foreach (KeyValuePair<string, Ordem> kv in Pendentes)
            {
                Ordem o = kv.Value;
                bool vencida = Time.unscaledTime - o.RecebidaEm > ValidadePendente;
                if (!vencida && (andar == null || andar.LevelIndex != o.M.Andar)) continue;
                (feitas ??= new List<string>()).Add(kv.Key); // feita mesmo se der erro: não repete a cada quadro
                if (vencida) continue;
                try
                {
                    portas ??= UnityEngine.Object.FindObjectsByType<SmokeDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    Aplicar(kv.Key, o, portas, ref salas);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"[Portas] Ordem da barreira em {o.M.Posicao} falhou: {(e.InnerException ?? e).Message}"); }
            }
            if (feitas != null) foreach (string k in feitas) Pendentes.Remove(k);
        }

        private static void Aplicar(string chave, Ordem o, SmokeDoor[] portas, ref SmokeRoom[] salas)
        {
            PortaFumaca m = o.M;
            SmokeDoor porta = null;
            float melhor = DistanciaPorta * DistanciaPorta;
            foreach (SmokeDoor p in portas)
            {
                if (p == null) continue;
                float d = (p.transform.position - m.Posicao).sqrMagnitude;
                if (d <= melhor) { melhor = d; porta = p; }
            }
            if (porta == null)
            {
                if (!AvisadasSemPorta.Add(chave)) return; // reenvio: já avisou (e já teleportou)
                Plugin.Log.LogWarning($"[Portas] Barreira do host em {m.Posicao} (andar {m.Andar}) não existe neste jogo; só o teleporte vale.");
                if (m.Fechada) TeleportarParaHost(m, o.RecebidaEm); // sem barreira aqui, ao menos não fica fora da luta
                return;
            }
            if ((Estado(porta) == EstadoFechada) == m.Fechada) return; // já está assim (reenvio)
            OrdemRemota = true;
            try
            {
                if (m.Fechada)
                {
                    SmokeRoom sala = porta.gameObject.activeInHierarchy ? null : SalaDe(porta, ref salas);
                    if (sala != null)
                    {
                        sala.Close(m.Animar); // liga o que o roteiro do host ligou e fecha todas as portas da sala
                        foreach (SmokeDoor d in PortasDaSalaRef(sala) ?? new List<SmokeDoor>())
                            if (d != null) { FechadasPorOrdem.Add(d); SalaDaPorta[d] = sala; }
                    }
                    else porta.Close(m.Animar);
                    FechadasPorOrdem.Add(porta);
                    if (!porta.gameObject.activeInHierarchy)
                        Plugin.Log.LogWarning($"[Portas] Barreira em {m.Posicao} fechada, mas desligada neste jogo (fora de uma sala conhecida).");
                }
                else if (SalaDaPorta.TryGetValue(porta, out SmokeRoom sala) && sala != null)
                {
                    sala.Open(m.Animar); // desliga o que a sala ligou, como no host
                    foreach (SmokeDoor d in PortasDaSalaRef(sala) ?? new List<SmokeDoor>())
                        if (d != null) { FechadasPorOrdem.Remove(d); SalaDaPorta.Remove(d); }
                }
                else
                {
                    porta.Open(m.Animar);
                    FechadasPorOrdem.Remove(porta);
                }
                Plugin.Log.LogInfo($"[Portas] Barreira em {m.Posicao} {(m.Fechada ? "fechada" : "aberta")} pelo host.");
            }
            finally { OrdemRemota = false; }
            if (m.Fechada) TeleportarParaHost(m, o.RecebidaEm);
        }

        /// <summary>A sala (SmokeRoom) que lista a porta, para ligar o que o roteiro do host ligou.</summary>
        private static SmokeRoom SalaDe(SmokeDoor porta, ref SmokeRoom[] salas)
        {
            salas ??= UnityEngine.Object.FindObjectsByType<SmokeRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (SmokeRoom s in salas)
            {
                List<SmokeDoor> lista = s != null ? PortasDaSalaRef(s) : null;
                if (lista != null && lista.Contains(porta)) return s;
            }
            return null;
        }

        private static void TeleportarParaHost(PortaFumaca m, float recebidaEm)
        {
            if (Time.unscaledTime - recebidaEm > ValidadeTeleporte) return;
            if (Time.unscaledTime - _ultimoTeleporte < IntervaloReenvio) return; // várias portas fecham juntas
            if ((m.PosicaoHost - m.Posicao).sqrMagnitude > AlcanceTeleporte * AlcanceTeleporte) return; // posição absurda
            PlayerController local = CoopRuntime.JogadorLocal();
            if (local == null || local.LifeController == null || local.LifeController.IsDead) return;
            if ((local.transform.position - m.PosicaoHost).sqrMagnitude <= DistanciaTeleporte * DistanciaTeleporte) return;
            _ultimoTeleporte = Time.unscaledTime;
            try
            {
                local.MovementController.Warp(m.PosicaoHost);
                Plugin.Log.LogInfo("[Portas] Barreiras fecharam: você foi levado para perto do host.");
                Sessao?.Avisar("As barreiras fecharam: você foi levado para perto do host.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Portas] Levar para perto do host falhou: {(e.InnerException ?? e).Message}"); }
        }

        // ---------------- Rede ----------------

        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            try
            {
                if (!Convidado || de != sessao.Host) return; // só o host manda nas barreiras
                PortaFumaca m = PortaFumaca.Desserializar(dados);
                if (!Validacao.AndarValido(m.Andar, -1) || !Finito(m.Posicao) || !Finito(m.PosicaoHost))
                {
                    Plugin.Log.LogWarning($"[Portas] Ordem de barreira de {de.Nome} ignorada: valores inválidos.");
                    return;
                }
                string chave = Chave(m.Andar, m.Posicao);
                if (!Pendentes.ContainsKey(chave) && Pendentes.Count >= MaximoPendentes) return; // enxurrada: descarta
                Pendentes[chave] = new Ordem { M = m, RecebidaEm = Time.unscaledTime };
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Portas] Mensagem de {de.Nome} ignorada: {(e.InnerException ?? e).Message}");
            }
        }

        private static bool Finito(Vector3 v) => Validacao.Finito(v.x) && Validacao.Finito(v.y) && Validacao.Finito(v.z);

        /// <summary>Fim da sessão: o convidado não fica preso atrás das barreiras que o host fechou.</summary>
        public static void Encerrar()
        {
            Reabrir("fim do coop");
            FechadasHost.Clear();
            Pendentes.Clear();
            AvisadasSemPorta.Clear();
            FechadasPorOrdem.Clear();
            SalaDaPorta.Clear();
            _andarHost = -1;
            _andarConvidado = -1;
        }

        /// <summary>Reabre as barreiras fechadas por ordem do host (ele saiu, ou este PC saiu do coop).</summary>
        private static void Reabrir(string motivo)
        {
            if (FechadasPorOrdem.Count > 0)
            {
                OrdemRemota = true;
                try
                {
                    // Sem animação: a corrotina da animação não roda em objeto desligado e a barreira ficaria visível.
                    foreach (SmokeRoom sala in new HashSet<SmokeRoom>(SalaDaPorta.Values))
                    {
                        try { if (sala != null) sala.Open(false); }
                        catch (Exception e) { Plugin.Log.LogWarning($"[Portas] Reabrir sala falhou: {(e.InnerException ?? e).Message}"); }
                    }
                    foreach (SmokeDoor p in FechadasPorOrdem)
                    {
                        try { if (p != null && Estado(p) == EstadoFechada) p.Open(false); }
                        catch (Exception e) { Plugin.Log.LogWarning($"[Portas] Reabrir barreira falhou: {(e.InnerException ?? e).Message}"); }
                    }
                    Plugin.Log.LogInfo($"[Portas] Barreiras reabertas ({motivo}): {FechadasPorOrdem.Count} fechada(s) pelo host.");
                }
                finally { OrdemRemota = false; }
            }
            FechadasPorOrdem.Clear();
            SalaDaPorta.Clear();
        }
    }

    /// <summary>Alvo: SmokeDoor.Start (privado) — estado inicial da barreira; igual nos dois jogos, não vira aviso.</summary>
    [HarmonyPatch(typeof(SmokeDoor), "Start")]
    internal static class Portas_Start
    {
        private static void Prefix() => PortasCompartilhadas.NoStart = true;
        private static void Finalizer() => PortasCompartilhadas.NoStart = false;
    }

    /// <summary>Alvo: SmokeDoor.Close(bool animate) — barreira de sangue fechou (host avisa os convidados).</summary>
    [HarmonyPatch(typeof(SmokeDoor), nameof(SmokeDoor.Close))]
    internal static class Portas_Close
    {
        private static void Prefix(SmokeDoor __instance, out int __state) => __state = PortasCompartilhadas.Estado(__instance);
        private static void Postfix(SmokeDoor __instance, int __state, bool __0) => PortasCompartilhadas.Mudou(__instance, __state, __0);
    }

    /// <summary>Alvo: SmokeDoor.Open(bool animate) — barreira de sangue abriu (host avisa os convidados).</summary>
    [HarmonyPatch(typeof(SmokeDoor), nameof(SmokeDoor.Open))]
    internal static class Portas_Open
    {
        private static void Prefix(SmokeDoor __instance, out int __state) => __state = PortasCompartilhadas.Estado(__instance);
        private static void Postfix(SmokeDoor __instance, int __state, bool __0) => PortasCompartilhadas.Mudou(__instance, __state, __0);
    }
}
