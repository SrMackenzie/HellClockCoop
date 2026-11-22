using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HellClock.Level;
using HellClock.Manager;
using HellClock.Player;
using HellClockCoop.Avatar;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase2
{
    /// <summary>
    /// Fase 2 — a run é do host; o convidado acompanha.
    ///
    /// Host: a cada andar, captura o que o jogo sorteou (LevelBlock.GenerateNextLevel, ponto único de
    /// sorteio da LevelDefinition) e o ponto de spawn usado, e envia aos convidados junto com uma semente
    /// para os sorteios da cena (vasos, caixas: ver ObjetosCompartilhados). Também envia o Hell Clock 4x
    /// por segundo e o fim da run.
    ///
    /// Convidado: não inicia nem avança runs por conta própria (portais e menus bloqueados). Ao receber
    /// um plano, inicia a run ou avança até o andar do host; nos pontos de sorteio, substitui o resultado
    /// pelo do host. Não gera inimigos nem objetos do andar (decisão do autor: andar vazio até a Fase 3).
    /// O timer local para de correr e passa a mostrar o do host.
    /// </summary>
    internal static class RunCompartilhada
    {
        private const float IntervaloTimer = 0.25f;

        public static SessaoCoop Sessao;

        // Estado do convidado
        private static readonly Dictionary<uint, PlanoAndar> Planos = new Dictionary<uint, PlanoAndar>();
        private static readonly Dictionary<uint, int> SpawnsDoHost = new Dictionary<uint, int>();
        private static PlanoAndar _planoPendente;
        private static PlanoAndar _entradaTardia;   // entrar numa run já em andamento
        private static bool _ordemDoHost;            // libera as entradas de run bloqueadas
        private static bool _avisouBloqueio;

        // Estado do host
        private static PlanoAndar _ultimoPlano;
        private static SpawnAndar _ultimoSpawn;
        private static readonly Dictionary<ulong, JanelaBonus> JanelasBonus = new Dictionary<ulong, JanelaBonus>(); // por SteamID
        private static float _proximoTimer;

        // Acesso a membros privados do jogo
        private static readonly MethodInfo InternalAdvanceLevel = AccessTools.Method(typeof(DungeonProgressManager), "InternalAdvanceLevel");
        private static readonly AccessTools.FieldRef<DungeonProgressManager, UIntState> IndiceAndarRef =
            AccessTools.FieldRefAccess<DungeonProgressManager, UIntState>("_currentLevelIndex");
        private static readonly AccessTools.FieldRef<TimerManager, FloatState> TimerRef = AccessTools.FieldRefAccess<TimerManager, FloatState>("_timer");
        private static readonly AccessTools.FieldRef<TimerManager, FloatState> TimerRelaxadoRef = AccessTools.FieldRefAccess<TimerManager, FloatState>("_relaxedTimer");
        internal static readonly AccessTools.FieldRef<LevelObjects, int> SpawnIndexRef = AccessTools.FieldRefAccess<LevelObjects, int>("_currentPlayerSpawnIndex");
        private static readonly MethodInfo SetupPlayerSpawn = AccessTools.Method(typeof(LevelObjects), "SetupPlayerSpawn");

        private static readonly System.Random Sorteador = new System.Random();

        /// <summary>
        /// Semente dos sorteios da cena do andar em carregamento (0 = sorteio normal do jogo). O host sorteia
        /// uma por andar e manda no PlanoAndar; os dois lados semeiam com ela o DrawRandomSceneObject.
        /// </summary>
        public static uint SementeCena { get; private set; }

        public static bool Convidado => Sessao != null && Sessao.ModoConvidado;
        public static bool Host => Sessao != null && Sessao.ModoHost;
        public static bool PermitidoAgora => _ordemDoHost;

        /// <summary>Índice do último andar que o host sorteou (andar sendo carregado ou jogado).</summary>
        public static uint AndarPlanejado => _ultimoPlano?.IndiceAndar ?? 0;
        public static bool TemPlano => _ultimoPlano != null;

        private static DungeonProgressManager Dpm => DungeonProgressManager.Instance;
        private static uint IndiceAtual => IndiceAndarRef(Dpm).Value;

        // ---------------- Laço ----------------

        public static void Atualizar(PlayerController jogador)
        {
            if (Sessao == null) return;
            if (Host) AtualizarHost();
            if (Convidado) AtualizarConvidado(jogador);
            else _planoPendente = null;
        }

        private static void AtualizarHost()
        {
            if (!Dpm.RunStarted.Value || Time.unscaledTime < _proximoTimer) return;
            _proximoTimer = Time.unscaledTime + IntervaloTimer;
            TimerManager tm = TimerManager.Instance;
            if (tm == null) return;
            var t = new TimerSync { Timer = TimerRef(tm).Value, TimerRelaxado = TimerRelaxadoRef(tm).Value };
            Sessao.EnviarParaConvidados(t.Serializar(), confiavel: false);
        }

        private static void AtualizarConvidado(PlayerController jogador)
        {
            PlanoAndar p = _planoPendente;
            if (p == null || jogador == null) return;
            // Só age com o jogo parado num andar/hub carregado (o LevelChanger lança exceção se já estiver trocando).
            if (ClassSingleton<LevelChanger>.Instance.IsChangingLevel.Value || Dpm.IsTransitioning || LocalPlayerSampler.CenaAtual() == -1) return;
            _planoPendente = null;

            if (!Dpm.RunStarted.Value)
            {
                _entradaTardia = p.Primeiro && p.IndiceAndar == 0 ? null : p;
                Plugin.Log.LogInfo($"[Run] Acompanhando a run do host: {p}{(_entradaTardia != null ? " (entrando no meio)" : "")}.");
                Sessao.Avisar("Indo para a run do host...");
                _ordemDoHost = true;
                try
                {
                    if (p.AndarEspecial != (int)ESpecialFloor.None)
                    {
                        Dpm.StartRunFromSpecialLevelBlock((ESpecialFloor)p.AndarEspecial);
                    }
                    else
                    {
                        DungeonConfig d = DungeonConfig.GetByID(p.DungeonHash);
                        if (d == null) { Plugin.Log.LogError($"[Run] Dungeon {p.DungeonHash} do host não existe neste jogo."); return; }
                        Dpm.StartRunFromDungeon(d);
                    }
                }
                finally { _ordemDoHost = false; }
                return;
            }

            uint atual = IndiceAtual;
            if (p.IndiceAndar > atual)
            {
                Plugin.Log.LogInfo($"[Run] Host avançou: {p} (você estava no andar {atual}).");
                _ordemDoHost = true;
                try { InternalAdvanceLevel.Invoke(Dpm, new object[] { p.IndiceAndar - atual }); }
                finally { _ordemDoHost = false; }
            }
            else if (p.Primeiro && p.IndiceAndar < atual)
            {
                Sessao.Avisar("O host começou uma run nova, mas você ainda está na anterior. Termine-a para acompanhar.");
            }
        }

        // ---------------- Mensagens ----------------

        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            switch (dados[0])
            {
                case Protocolo.TipoPlanoAndar:
                    if (!Convidado || de != sessao.Host) return;
                    PlanoAndar plano = PlanoAndar.Desserializar(dados);
                    if (!PlanoValido(plano)) return;
                    if (plano.Primeiro) { Planos.Clear(); SpawnsDoHost.Clear(); }
                    Planos[plano.IndiceAndar] = plano;
                    _planoPendente = plano;
                    Plugin.Log.LogInfo($"[Run] Plano do host: {plano}.");
                    break;

                case Protocolo.TipoSpawnAndar:
                    if (!Convidado || de != sessao.Host) return;
                    SpawnAndar sp = SpawnAndar.Desserializar(dados);
                    SpawnsDoHost[sp.IndiceAndar] = sp.IndiceSpawn;
                    ReposicionarSeJaChegou(sp);
                    break;

                case Protocolo.TipoTimer:
                    if (!Convidado || de != sessao.Host || !Dpm.RunStarted.Value) return;
                    TimerSync t = TimerSync.Desserializar(dados);
                    if (!Validacao.TimerValido(t)) return; // 4 Hz: descarta sem log
                    TimerManager tm = TimerManager.Instance;
                    if (tm == null) return;
                    TimerRef(tm).Setter(t.Timer);
                    TimerRelaxadoRef(tm).Setter(t.TimerRelaxado);
                    break;

                case Protocolo.TipoTimerBonus:
                    if (!Host) return;
                    TimerBonus b = TimerBonus.Desserializar(dados);
                    // Chave pelo SteamID: sair e voltar ao lobby cria outro Par, mas não zera o teto.
                    ulong steam = de.Id.m_SteamID;
                    if (!JanelasBonus.TryGetValue(steam, out JanelaBonus jb))
                        JanelasBonus[steam] = jb = new JanelaBonus { Inicio = Time.unscaledTime };
                    if (!Validacao.AceitarBonusNaJanela(jb, Time.unscaledTime, b.Segundos))
                    {
                        if (!jb.Avisou) // um aviso por janela, para o log não encher
                        {
                            jb.Avisou = true;
                            Plugin.Log.LogWarning($"[Run] Bônus de tempo de {de.Nome} recusado ({b.Segundos}s): inválido ou acima do teto " +
                                $"({Validacao.BonusPorJanelaMaximo:0}s e {Validacao.MensagensBonusPorJanela} bônus a cada {Validacao.JanelaBonusSegundos:0}s); " +
                                "os próximos recusados nesta janela não aparecem no log.");
                        }
                        return;
                    }
                    Plugin.Log.LogInfo($"[Run] {de.Nome} ganhou {b.Segundos:0.#}s de Hell Clock.");
                    TimerManager.Instance?.IncrementTimer(b.Segundos);
                    break;

                case Protocolo.TipoFimRun:
                    if (!Convidado || de != sessao.Host) return;
                    FimRun f = FimRun.Desserializar(dados);
                    if (!Validacao.MotivoFimRunValido(f.Motivo))
                    {
                        Plugin.Log.LogWarning($"[Run] Fim de run com motivo desconhecido ({f.Motivo}); ignorado.");
                        return;
                    }
                    Planos.Clear(); SpawnsDoHost.Clear(); _planoPendente = null; _entradaTardia = null; SementeCena = 0;
                    if (!Dpm.RunStarted.Value || Dpm.EndingExecuted.Value) return;
                    Plugin.Log.LogInfo($"[Run] Host encerrou a run ({(EGameOverSource)f.Motivo}).");
                    _ordemDoHost = true;
                    try { Dpm.FinishDungeon((EGameOverSource)f.Motivo); }
                    finally { _ordemDoHost = false; }
                    break;
            }
        }

        /// <summary>
        /// Descarta plano com andar além do fim da dungeon: o convidado avançaria até ele e o jogo gravaria isso
        /// no save dele (CurrentLevelChanged). Andar especial não tem DungeonConfig: vale o teto de sanidade.
        /// </summary>
        private static bool PlanoValido(PlanoAndar p)
        {
            int max = -1;
            if (p.AndarEspecial == (int)ESpecialFloor.None)
            {
                DungeonConfig d = DungeonConfig.GetByID(p.DungeonHash);
                if (d == null)
                {
                    Plugin.Log.LogWarning($"[Run] Plano do host ignorado: dungeon {p.DungeonHash} não existe neste jogo.");
                    return false;
                }
                max = (int)d.MaxLevelIndex;
            }
            if (Validacao.AndarValido(p.IndiceAndar, max)) return true;
            Plugin.Log.LogWarning(max >= 0
                ? $"[Run] Plano do host ignorado: andar {p.IndiceAndar} além do fim da dungeon ({max} andares, índices 0 a {max - 1})."
                : $"[Run] Plano do host ignorado: andar especial {p.IndiceAndar} acima do teto de sanidade ({Validacao.AndarMaximoSemDungeon}).");
            return false;
        }

        /// <summary>Host: manda o andar atual para quem acabou de chegar (entrada no meio da run).</summary>
        public static void AoNovoConvidado(SessaoCoop sessao, Par p)
        {
            if (!Dpm.RunStarted.Value || _ultimoPlano == null) return;
            sessao.EnviarPara(p, _ultimoPlano.Serializar());
            if (_ultimoSpawn != null) sessao.EnviarPara(p, _ultimoSpawn.Serializar());
        }

        // ---------------- Host: captura ----------------

        internal static void HostCapturouAndar(uint indice, LevelBlockInfo bloco, LevelInfo resultado)
        {
            SementeCena = 0; // fora do coop (ou sem plano) o sorteio da cena volta a ser o do jogo
            if (!Host || resultado == null || bloco?.LevelBlock == null || resultado.LevelDefinition == null) return;
            SementeCena = (uint)Sorteador.Next(1, 2147483647); // int.MaxValue não existe no mscorlib do jogo
            var p = new PlanoAndar
            {
                IndiceAndar = indice,
                Primeiro = !Dpm.RunStarted.Value,
                DungeonHash = Dpm.CurrentDungeonConfig != null ? Dpm.CurrentDungeonConfig.HashID : 0,
                AndarEspecial = (int)Dpm.CurrentESpecialFloor,
                BlocoHash = bloco.LevelBlock.HashID,
                IndiceLocal = bloco.LocalLevelIndex,
                TemSementeBoss = bloco.BossSpawnSeed.HasValue,
                SementeBoss = bloco.BossSpawnSeed.GetValueOrDefault(),
                BossForcado = bloco.ForcedBossHashId,
                DefinicaoHash = resultado.LevelDefinition.HashID,
                EhBoss = resultado.IsBossLevel,
                SementeCena = SementeCena,
            };
            _ultimoPlano = p;
            _ultimoSpawn = null;
            Plugin.Log.LogInfo($"[Run] Enviando plano aos convidados: {p}.");
            Sessao.EnviarParaConvidados(p.Serializar(), confiavel: true);
        }

        internal static void HostUsouSpawn(LevelInfo andar, int indiceSpawn)
        {
            if (!Host || andar == null || andar.LevelBlockInfo == null) return; // hub: LevelBlockInfo nulo
            _ultimoSpawn = new SpawnAndar { IndiceAndar = andar.LevelIndex, IndiceSpawn = indiceSpawn };
            Sessao.EnviarParaConvidados(_ultimoSpawn.Serializar(), confiavel: true);
        }

        internal static void HostTerminou(EGameOverSource motivo)
        {
            SementeCena = 0; // o hub e a próxima run solo sorteiam como no jogo
            _ultimoPlano = null;
            _ultimoSpawn = null;
            JanelasBonus.Clear(); // não guarda pares de runs passadas
            if (!Host) return;
            Plugin.Log.LogInfo($"[Run] Fim da run ({motivo}); avisando os convidados.");
            Sessao.EnviarParaConvidados(new FimRun { Motivo = (int)motivo }.Serializar(), confiavel: true);
        }

        // ---------------- Convidado: substituições ----------------

        /// <summary>Bloco do andar: usa o do host (na Ascensão, a ordem dos blocos vem do save de cada um).</summary>
        internal static LevelBlockInfo BlocoDoHost(uint indice)
        {
            if (!Convidado) return null;
            // Entrada no meio da run: o StartRun do jogo começa no andar 0; pula direto para o andar do host.
            if (_entradaTardia != null && Dpm.IsTransitioning && !Dpm.RunStarted.Value && indice == 0)
            {
                indice = _entradaTardia.IndiceAndar;
                IndiceAndarRef(Dpm).Setter(indice);
                _entradaTardia = null;
                Plugin.Log.LogInfo($"[Run] Entrando direto no andar {indice} do host.");
            }
            if (!Planos.TryGetValue(indice, out PlanoAndar p)) return null;
            LevelBlock bloco = global::HellClock.Constants.Collections.LEVEL_BLOCK_COLLECTION.Reference.GetElement(p.BlocoHash);
            if (bloco == null)
            {
                Plugin.Log.LogError($"[Run] Bloco {p.BlocoHash} do host não existe neste jogo.");
                return null;
            }
            return new LevelBlockInfo(bloco, p.IndiceLocal, p.TemSementeBoss ? p.SementeBoss : (uint?)null, p.BossForcado);
        }

        /// <summary>LevelDefinition do andar: troca a sorteada localmente pela do host, mantendo o resto.</summary>
        internal static LevelInfo AndarDoHost(uint indice, LevelBlockInfo bloco, LevelInfo sorteado)
        {
            SementeCena = 0;
            if (!Convidado || sorteado == null) return sorteado;
            if (!Planos.TryGetValue(indice, out PlanoAndar p))
            {
                Plugin.Log.LogWarning($"[Run] Andar {indice} sem plano do host: vasos e caixas vão ser sorteados por este PC e podem não bater com os do host.");
                return sorteado;
            }
            SementeCena = p.SementeCena;
            if (sorteado.LevelDefinition != null && sorteado.LevelDefinition.HashID == p.DefinicaoHash && sorteado.IsBossLevel == p.EhBoss) return sorteado;
            LevelDefinition def = global::HellClock.Constants.Collections.LEVEL_DEFINITION_COLLECTION.Reference.GetElement(p.DefinicaoHash);
            if (def == null)
            {
                Plugin.Log.LogError($"[Run] Andar {p.DefinicaoHash} do host não existe neste jogo (DLC?).");
                return sorteado;
            }
            Plugin.Log.LogInfo($"[Run] Andar {indice}: '{def.name}' (o sorteio local tinha dado '{sorteado.LevelDefinition?.name}').");
            return new LevelInfo(indice, bloco, sorteado.CharacterSheetBaseModifier, def, p.EhBoss, sorteado.SquadPool, sorteado.PreferLevelDefinitionSquadPool);
        }

        /// <summary>Ajusta o índice rotativo de spawn para cair no mesmo ponto do host.</summary>
        internal static void AjustarSpawn(LevelObjects objetos, LevelInfo andar)
        {
            if (!Convidado || andar?.LevelBlockInfo == null || !SpawnsDoHost.TryGetValue(andar.LevelIndex, out int alvo)) return;
            int total = andar.LevelDefinition.PlayerSpawnInfos.Count;
            if (total == 0) return;
            SpawnIndexRef(objetos) = (alvo - 1 + total) % total; // o jogo faz ++ antes de usar
        }

        /// <summary>O spawn do host chegou depois de o convidado já ter nascido no andar: reposiciona.</summary>
        private static void ReposicionarSeJaChegou(SpawnAndar sp)
        {
            LevelInfo andar = LocalPlayerSampler.AndarAtual();
            if (andar?.LevelObjects == null || andar.LevelBlockInfo == null || andar.LevelIndex != sp.IndiceAndar) return;
            if (SpawnIndexRef(andar.LevelObjects) == sp.IndiceSpawn) return;
            AjustarSpawn(andar.LevelObjects, andar);
            SetupPlayerSpawn.Invoke(andar.LevelObjects, new object[] { andar });
            Plugin.Log.LogInfo($"[Run] Reposicionado no spawn {sp.IndiceSpawn} do host (andar {sp.IndiceAndar}).");
        }

        /// <summary>
        /// Convidado morreu de verdade (PlayerController.PlayDeathAnimation → FinishDungeon(Defeat)): deixa a
        /// run dele acabar como no solo (resultados, volta ao hub). Quando o host passar de andar, o plano
        /// novo traz o convidado de volta para a run (entrada no meio da run).
        /// </summary>
        internal static bool ConvidadoMorreu(EGameOverSource fonte)
        {
            if (!Convidado || fonte != EGameOverSource.Defeat) return false;
            PlayerController local = CoopRuntime.JogadorLocal();
            if (local == null || !local.LifeController.IsDead) return false;
            SementeCena = 0; // a run dele acaba: o hub sorteia como no jogo
            Plugin.Log.LogInfo("[Run] Você morreu: sua run acaba; você volta para a run do host quando ele passar de andar.");
            Sessao.Avisar("Você morreu. Quando o host passar de andar, você volta para a run dele.");
            return true;
        }

        internal static bool BloquearAcaoDoConvidado(string acao)
        {
            if (!Convidado || _ordemDoHost) return false;
            if (!_avisouBloqueio || Time.frameCount % 600 == 0)
            {
                _avisouBloqueio = true;
                Sessao.Avisar($"No coop, só o host {acao}.");
            }
            Plugin.Log.LogInfo($"[Run] Bloqueado no convidado: {acao}.");
            return true;
        }

        internal static void ConvidadoGanhouTempo(float segundos)
        {
            Sessao.EnviarPara(Sessao.Host, new TimerBonus { Segundos = segundos }.Serializar());
        }

        internal static IEnumerator Vazio()
        {
            yield break;
        }
    }
}
