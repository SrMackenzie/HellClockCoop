using System;
using System.Reflection;
using HarmonyLib;
using HellClock.Character;
using HellClock.Damage;
using HellClock.Level;
using HellClock.Manager;
using HellClock.Player;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase2
{
    /// <summary>
    /// Jogador caído (0.15.0, entrega 2 de 4 da etapa "aliados na tela e reanimação"). No coop, quem morre fica caído
    /// em vez de a run acabar; a run só acaba quando todos caem.
    /// - Cair: o PlayerController.Handle_OnDie do jogador local não roda a morte do jogo (PlayDeathAnimation: pausa,
    ///   esconde a HUD, câmera de morte e FinishDungeon(Defeat)). No lugar, a mesma receita da segunda chance do jogo
    ///   (SkillSecondWind): gatilho "dead" no Animator, ações bloqueadas (CanPerformAction), sem alvo e sem companheiros.
    ///   A vida fica em 0 (LifeController.IsDead): os inimigos largam o alvo e a regeneração do jogo não age em quem
    ///   está morto. O Hell Clock continua correndo: o TimerManager.Update do jogo para com o jogador local morto, e o
    ///   Run_TimerUpdate o faz correr no host caído. As quedas não sobem o contador de mortes do save; só a morte final.
    /// - Voltar: no próximo andar (LevelObjects.SetupPlayerSpawn), com VidaAoVoltar (50%, fixo no código) da vida
    ///   (ForceRegeneratePercentage, que passa por cima do teste de morto), gatilho "revive" e ações liberadas.
    /// - Quem decide o fim é o host: com ele caído e nenhum aliado de pé, roda a morte do jogo (que termina a run e
    ///   manda FimRun). O convidado caído espera o host; sem sessão, morre como no solo.
    /// - Cada queda tira SegundosPorQueda (10 s, fixo no código) do Hell Clock do host (o host aplica as dele e as dos convidados).
    /// - Modo eco (F8): o fantasma conta como aliado de pé (só teste); desligá-lo com você caído encerra a run.
    /// - Reanimação (0.16.0, Fase2/Reanimacao): um aliado parado ao seu lado te levanta antes do próximo andar.
    /// Decisões do autor em 22/11/2026: volta no próximo andar com 50%, −10 s por queda, a escala continua contando o
    /// grupo todo, e com o host caído o portal do convidado vira pedido ao host (PedidoAvanco).
    /// </summary>
    internal static class JogadorCaido
    {
        private const float SemSinalSegundos = 5f; // mesmo limite do "sem sinal" do canto do coop
        private const string NomeBloqueio = "HellClockCoop.Caido";

        // Valores do autor (0.16.3), fixos no código e fora do .cfg, como a reanimação desde a 0.16.1: o balanceamento é
        // calibrado com as métricas, e um valor mudado por jogador sujaria os dados.
        internal const float VidaAoVoltar = 0.5f;      // fração da vida com que o caído volta no próximo andar
        internal const float SegundosPorQueda = 10f;   // segundos que cada queda tira do Hell Clock (o host aplica)

        private static readonly object Chave = new object(); // dono do bloqueio de ações
        private static readonly MethodInfo HandleOnDie = AccessTools.Method(typeof(PlayerController), "Handle_OnDie");
        private static readonly MethodInfo EhUltimoAndar = AccessTools.Method(typeof(DungeonProgressManager), "IsLastLevel");
        private const float CarenciaTrocaSegundos = 3f;
        private const float HostSemSinalSegundos = 20f; // convidado caído desiste do host travado
        private static readonly System.Collections.Generic.Dictionary<ulong, long> QuedasNoAndar = new System.Collections.Generic.Dictionary<ulong, long>();

        /// <summary>Fim da run no host (ou solo): esquece as quedas cobradas.</summary>
        internal static void FimDaRunNoHost()
        {
            QuedasNoAndar.Clear();
            _andarLimpo = -1;
        }

        private static long _andarLimpo = -1; // host: último andar com OnAllEnemiesCleared nesta run (-1 = nenhum)
        private static bool _liberarMorte;
        private static float _carenciaAte;

        /// <summary>Host aceitou um PedidoAvanco: carência até a troca de andar começar (o AdvanceLevel pode esperar um quadro).</summary>
        internal static void AvancoPedido() => _carenciaAte = Time.unscaledTime + CarenciaTrocaSegundos;

        /// <summary>O jogador local está caído (vida 0, sem ações), esperando o próximo andar.</summary>
        public static bool Caido { get; private set; }

        /// <summary>Modo eco ligado (CoopRuntime, a cada quadro): o fantasma conta como aliado de pé.</summary>
        public static bool EcoAtivo;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;
        private static DungeonProgressManager Dpm => DungeonProgressManager.Instance;

        /// <summary>Em run com aliados (convidado, host com convidado ou eco): só aí a morte vira queda.</summary>
        private static bool EmRunCoop =>
            Dpm.RunStarted.Value && !Dpm.EndingExecuted.Value && (EcoAtivo || RunCompartilhada.Host || RunCompartilhada.Convidado);

        /// <summary>Algum aliado de pé na run (eco, ou par compatível com sinal, no andar ou carregando).</summary>
        internal static bool AlgumAliadoDePe()
        {
            if (EcoAtivo) return true;
            if (Sessao == null || !Sessao.EmSessao) return false;
            foreach (Par p in Sessao.Pares.Values)
            {
                if (DePe(p)) return true;
            }
            return false;
        }

        internal static bool NaRun(Par p) =>
            p.Compativel && p.UltimoPacote >= 0f && Time.unscaledTime - p.UltimoPacote < SemSinalSegundos &&
            (p.MesmoAndar || p.CenaRemota == -1); // -1: carregando o andar

        private static bool DePe(Par p) => NaRun(p) && !p.Morto;

        /// <summary>Aliado caído que joga a run (para a reanimação, 0.16.0).</summary>
        internal static bool CaidoNaRun(Par p) => NaRun(p) && p.Morto;

        /// <summary>
        /// O aliado foi reanimado (Reanimou, 0.16.0): a queda dele neste andar deixa de contar como já vista, para a
        /// próxima mostrar o aviso de novo e, no host, tirar o tempo de novo.
        /// </summary>
        internal static void EsquecerQueda(ulong steamId) => QuedasNoAndar.Remove(steamId);

        /// <summary>Um aliado te reanimou (Reanimacao, já validado): levanta com VidaAoReanimar.</summary>
        internal static void Reanimado()
        {
            PlayerController local = CoopRuntime.JogadorLocal();
            if (!Caido || local == null) return;
            Balanceamento.Metricas.FuiReanimado();
            Levantar(local, Reanimacao.FracaoAoReanimar, animar: true);
        }

        /// <summary>Quantos estão de pé e quantos jogam a run (você, os aliados na run e o eco).</summary>
        internal static void Contar(out int dePe, out int total)
        {
            total = 1;
            dePe = Caido ? 0 : 1;
            if (EcoAtivo) { total++; dePe++; }
            if (Sessao == null || !Sessao.EmSessao) return;
            foreach (Par p in Sessao.Pares.Values)
            {
                if (!NaRun(p)) continue;
                total++;
                if (!p.Morto) dePe++;
            }
        }

        /// <summary>Fração da vida na volta (VidaAoVoltar, valor do autor).</summary>
        internal static float FracaoAoVoltar => VidaAoVoltar;

        // ---------------- Patches ----------------

        /// <summary>Prefixo de PlayerController.Handle_OnDie: false = virou queda (a morte do jogo não roda).</summary>
        internal static bool AoMorrer(PlayerController pc)
        {
            if (_liberarMorte || pc == null || pc != CoopRuntime.JogadorLocal()) return true;
            if (Caido) return false; // já no chão: nada de morte do jogo por cima
            if (!EmRunCoop) return true;
            // Host (ou solo com eco) sem ninguém de pé: é o último a cair, a morte do jogo encerra a run (e manda FimRun).
            if (!RunCompartilhada.Convidado && !AlgumAliadoDePe()) return true;
            Cair(pc);
            return false;
        }

        /// <summary>Postfix de IsPlayerBlockedFromPause: caído numa run que ainda não acabou pode abrir o menu.</summary>
        internal static bool PodePausarCaido()
        {
            if (!Caido) return false;
            DungeonProgressManager dpm = Dpm;
            return dpm != null && dpm.RunStarted.Value && !dpm.EndingExecuted.Value;
        }

        /// <summary>Prefixo de StatBasedValueController.Refill: subir de nível caído não devolve a vida.</summary>
        internal static bool PodeEncher(StatBasedValueController valor)
        {
            if (!Caido) return true;
            PlayerController local = CoopRuntime.JogadorLocal();
            return local == null || !ReferenceEquals(local.LifeController, valor);
        }

        /// <summary>Postfix de LevelObjects.SetupPlayerSpawn: andar novo da run, o caído volta.</summary>
        internal static void NovoAndar(LevelInfo andar)
        {
            if (!Caido || andar == null || andar.LevelBlockInfo == null) return; // hub: LevelBlockInfo nulo
            PlayerController local = CoopRuntime.JogadorLocal();
            if (local == null) return;
            Plugin.Log.LogInfo($"[Caído] Andar {andar.LevelIndex}: você volta com {FracaoAoVoltar:P0} da vida.");
            Levantar(local, FracaoAoVoltar, animar: true);
        }

        /// <summary>
        /// Andar limpo no host (EnemyManager.OnAllEnemiesCleared). Só no último andar: a saída é o portal do fim da
        /// dungeon, que só o host usa; o host caído levanta para usá-lo (decisão do autor, 22/11/2026). Nos outros
        /// andares o convidado leva o grupo (PedidoAvanco).
        /// </summary>
        internal static void AndarLimpo()
        {
            _andarLimpo = RunCompartilhada.AndarAtual; // vale também para quem cair depois (Atualizar)
            if (!Caido || RunCompartilhada.Convidado || !Dpm.RunStarted.Value || Dpm.EndingExecuted.Value) return;
            if (EhUltimoAndar == null || !(bool)EhUltimoAndar.Invoke(Dpm, null)) return;
            PlayerController local = CoopRuntime.JogadorLocal();
            if (local == null) return;
            Plugin.Log.LogInfo($"[Caído] Último andar limpo: você levanta com {FracaoAoVoltar:P0} da vida para usar o portal final.");
            Levantar(local, FracaoAoVoltar, animar: true);
        }

        /// <summary>
        /// Postfix de FinishDungeon: a run acabou com você no chão (fim mandado pelo host, tempo zerado ou último andar
        /// vencido pelo grupo). Devolve a vida (sem animação) e libera as ações, para não chegar ao hub com 0 de vida.
        /// </summary>
        internal static void FimDaRun()
        {
            if (!Caido) return;
            PlayerController local = CoopRuntime.JogadorLocal();
            Plugin.Log.LogInfo("[Caído] A run acabou com você caído.");
            if (local != null) Levantar(local, 1f, animar: false);
            else Soltar(null);
        }

        // ---------------- Laço ----------------

        /// <summary>Laço do CoopRuntime: confere se o caído ficou sem ninguém de pé.</summary>
        internal static void Atualizar(PlayerController jogador)
        {
            if (!Caido) return;
            if (jogador == null || jogador.LifeController == null) // troca de cena sem personagem
            {
                Caido = false;
                Interface.CaidoNaTela.AoLevantar(); // a próxima queda volta a pôr o cinza
                return;
            }
            if (!jogador.LifeController.IsDead)
            {
                Plugin.Log.LogInfo("[Caído] A vida voltou por outro caminho; ações liberadas.");
                Soltar(jogador);
                return;
            }
            if (!Dpm.RunStarted.Value || Dpm.EndingExecuted.Value) return;
            // Trocando de andar (ex.: o convidado levou o grupo): os aliados aparecem em outra cena até este PC chegar;
            // a volta vem no SetupPlayerSpawn. Carência depois da troca para os snapshots do andar novo chegarem.
            if (Dpm.IsTransitioning || ClassSingleton<LevelChanger>.Instance.IsChangingLevel.Value || Avatar.LocalPlayerSampler.CenaAtual() == -1)
            {
                _carenciaAte = Time.unscaledTime + CarenciaTrocaSegundos;
                return;
            }
            if (Time.unscaledTime < _carenciaAte) return;
            // Host que caiu no último andar depois de ele já estar limpo (o evento do jogo dispara uma vez por andar).
            if (!RunCompartilhada.Convidado && _andarLimpo == RunCompartilhada.AndarAtual) { AndarLimpo(); if (!Caido) return; }
            // O convidado espera o host decidir (FimRun). Host, solo com eco ou convidado que perdeu a sessão: sem
            // ninguém de pé, a run acaba como no solo.
            if (RunCompartilhada.Convidado)
            {
                Par host = Sessao.Host;
                bool semSinal = host != null && host.UltimoPacote >= 0f && Time.unscaledTime - host.UltimoPacote > HostSemSinalSegundos;
                if (!semSinal) return;
                Plugin.Log.LogWarning($"[Caído] O host está sem sinal há mais de {HostSemSinalSegundos:0} s.");
            }
            else if (AlgumAliadoDePe()) return;
            MorrerDeVerdade(jogador);
        }

        /// <summary>Um aliado caiu (snapshot com Morto, na run): o host tira o tempo; todos veem o aviso.</summary>
        internal static void AliadoCaiu(Par p)
        {
            if (!Dpm.RunStarted.Value || Dpm.EndingExecuted.Value || !p.Compativel) return;
            // Uma queda por aliado e por andar (o caído só levanta no próximo): snapshot fora de ordem ou forjado
            // alternando Morto não tira tempo de novo nem repete o aviso.
            long andar = RunCompartilhada.AndarAtual;
            if (QuedasNoAndar.TryGetValue(p.Id.m_SteamID, out long ja) && ja == andar) return;
            QuedasNoAndar[p.Id.m_SteamID] = andar;
            float perdidos = RunCompartilhada.Host ? RunCompartilhada.PerderTempo(SegundosPorQueda) : 0f;
            Plugin.Log.LogInfo($"[Caído] {p.Nome} caiu{(perdidos > 0f ? $" (−{perdidos:0} s no Hell Clock)" : "")}.");
            Interface.CaidoNaTela.AvisarQueda(p.Nome, perdidos);
        }

        // ---------------- Estado ----------------

        private static void Cair(PlayerController pc)
        {
            Caido = true;
            try
            {
                pc.MovementController.StopNow();
                pc.SkillsController.ResetSkillsExecution();
                pc.CharacterSemaphores.CanPerformAction.Block(Chave, true, NomeBloqueio);
                Animator a = pc.Animator;
                if (a != null)
                {
                    a.SetTrigger("dead");
                    foreach (string g in new[] { "knockback", "interact", "attack", "revive" }) a.ResetTrigger(g);
                    a.SetBool("looping_attack", false);
                }
                pc.TargetController.ChangeTarget(null);
                pc.CompanionAIDirector?.DisableCompanions();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Parte da queda falhou (o personagem pode não deitar): {e}");
            }
            float perdidos = RunCompartilhada.Convidado ? 0f : RunCompartilhada.PerderTempo(SegundosPorQueda);
            Plugin.Log.LogInfo($"[Caído] Você caiu{(perdidos > 0f ? $" (−{perdidos:0} s no Hell Clock)" : "")}; volta no próximo andar ou se um aliado te reanimar.");
            Balanceamento.Metricas.Caiu();
            Interface.CaidoNaTela.AoCair();
        }

        private static void Levantar(PlayerController pc, float fracao, bool animar)
        {
            try
            {
                LifeController vida = pc.LifeController;
                vida.ClearDelayedDamage();
                if (vida.IsDead) vida.ForceRegeneratePercentage(fracao);
                if (animar && pc.Animator != null)
                {
                    pc.Animator.ResetTrigger("dead");
                    pc.Animator.SetTrigger("revive");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Erro ao devolver a vida: {e}");
            }
            Soltar(pc);
        }

        /// <summary>Sai do estado de caído: libera as ações e os companheiros.</summary>
        private static void Soltar(PlayerController pc)
        {
            Caido = false;
            Interface.CaidoNaTela.AoLevantar();
            if (pc == null) return;
            try
            {
                pc.CharacterSemaphores.CanPerformAction.Release(Chave);
                pc.CompanionAIDirector?.EnableCompanions();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Caído] Erro ao liberar as ações: {e}");
            }
        }

        /// <summary>Ninguém de pé: roda a morte do jogo (resultado da run, FinishDungeon(Defeat) e, no host, FimRun).</summary>
        private static void MorrerDeVerdade(PlayerController pc)
        {
            Plugin.Log.LogInfo("[Caído] Ninguém de pé: a run acaba.");
            Soltar(pc);
            _liberarMorte = true;
            try { HandleOnDie.Invoke(pc, new object[] { DamageInstance.Default }); }
            catch (Exception e) { Plugin.Log.LogError($"[Caído] A morte do jogo falhou; encerrando a run direto: {e}"); Dpm.FinishDungeon(EGameOverSource.Defeat); }
            finally { _liberarMorte = false; }
        }
    }

    /// <summary>Alvo: HellClock.Player.PlayerController.Handle_OnDie (privado) — no coop, a morte do jogador local vira queda.</summary>
    [HarmonyPatch(typeof(PlayerController), "Handle_OnDie")]
    internal static class Caido_HandleOnDie
    {
        private static bool Prefix(PlayerController __instance) => JogadorCaido.AoMorrer(__instance);
    }

    /// <summary>
    /// Alvo: HellClock.Player.PlayerController.IsPlayerBlockedFromPause (estático) — o jogo barra a pausa (Esc,
    /// PauseButton.TogglePause) com o jogador local morto, e o caído fica com vida 0. Caído numa run em andamento, o
    /// menu abre: dá para abandonar a run (convidado sai sozinho, host encerra para todos; Run_FinishDungeon). As ações
    /// de interface (IsPlayerBlockedFromUIActions, que chama este método) continuam barradas pelo CanPerformAction.
    /// </summary>
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.IsPlayerBlockedFromPause))]
    internal static class Caido_BloqueioPausa
    {
        private static void Postfix(ref bool __result)
        {
            if (__result && JogadorCaido.PodePausarCaido()) __result = false;
        }
    }

    /// <summary>Alvo: HellClock.Character.StatBasedValueController.Refill — subir de nível caído não devolve a vida.</summary>
    [HarmonyPatch(typeof(StatBasedValueController), nameof(StatBasedValueController.Refill))]
    internal static class Caido_Refill
    {
        private static bool Prefix(StatBasedValueController __instance) => JogadorCaido.PodeEncher(__instance);
    }
}
