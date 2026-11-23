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
        private static ulong _abandonouHost;         // SteamID do host cuja run você abandonou caído (0 = nenhum)
        private static bool _semLiberar;             // 0.19.0: o save não chegou à run do host; espera a próxima
        private static bool _hostSabeQueEstouFora;   // 0.19.0: já mandou ForaDaRun { Fora = true } aos pares
        private static bool _seguindoHost;           // 0.19.0: a run aberta foi iniciada por ordem do host (até o FimRun)

        // Estado do host
        private static PlanoAndar _ultimoPlano;
        private static SpawnAndar _ultimoSpawn;
        private static readonly Dictionary<ulong, JanelaBonus> JanelasBonus = new Dictionary<ulong, JanelaBonus>(); // por SteamID
        private static float _proximoTimer;
        private static float _proximoPedidoAvanco;
        private static long _andarAvancadoPorPedido = -1; // host: andar já avançado por PedidoAvanco (-1 = nenhum)
        private static float _proximoLogPedido;
        private static readonly MethodInfo EhUltimoAndar = AccessTools.Method(typeof(DungeonProgressManager), "IsLastLevel");

        /// <summary>Log de pedido ignorado ou recusado: no máximo um a cada 5 s (canal confiável, sem limite de envio).</summary>
        private static void LogPedido(bool aviso, string texto)
        {
            if (Time.unscaledTime < _proximoLogPedido) return;
            _proximoLogPedido = Time.unscaledTime + 5f;
            if (aviso) Plugin.Log.LogWarning(texto); else Plugin.Log.LogInfo(texto);
        }

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
        internal static uint AndarAtual => IndiceAtual;

        /// <summary>
        /// Lugar deste PC para a lista de salas (0.18.0): falso fora da run (hub). Na run, o nome do DungeonConfig (o ato sai
        /// dele: Salas.AtoDoNome) e o andar contado a partir de 1. Desde a 0.19.0 também o HashID da masmorra (trava de
        /// progresso de quem entra); 0 num atalho especial (ESpecialFloor), que não tem trava.
        /// </summary>
        internal static bool LugarDaRun(out string dungeon, out int dungeonHash, out int andar)
        {
            dungeon = "";
            dungeonHash = 0;
            andar = 0;
            try
            {
                DungeonProgressManager dpm = Dpm;
                if (dpm == null || !dpm.RunStarted.Value) return false;
                DungeonConfig d = dpm.CurrentDungeonConfig;
                dungeon = d != null ? d.name : "";
                bool especial = dpm.CurrentESpecialFloor != ESpecialFloor.None; // a run especial não grava _currentDungeonConfig
                dungeonHash = d != null && !especial ? d.HashID : 0;
                andar = (int)Math.Min(IndiceAtual + 1u, (uint)Salas.AndarMaximo);
                return true;
            }
            catch (Exception)
            {
                return false; // jogo trocando de cena: fica como hub até a próxima leitura
            }
        }

        /// <summary>
        /// Recorde de andar do save carregado na masmorra (PlayerDungeonData.maxFloorReached, contado a partir de 1;
        /// 0 = nunca entrou). -1 sem personagem (menu) ou se a leitura falhar. Só lê: GetDungeonDataByID não grava
        /// registro novo (conferido no IL da 2.1.1.1).
        /// </summary>
        internal static long RecordeNoSave(int dungeonHash)
        {
            // Esta guarda não sai: CurrentPlayerData passa por PlayerSaveManager.Instance, que cria o singleton se não
            // existir. Com personagem carregado, o jogo já o criou.
            if (CoopRuntime.JogadorLocal() == null) return -1;
            try
            {
                PlayerDungeonData dd = PlayerSaveManager.CurrentPlayerData?.GetDungeonDataByID(dungeonHash);
                return dd != null ? dd.maxFloorReached : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // ---------------- Laço ----------------

        public static void Atualizar(PlayerController jogador)
        {
            if (!Convidado)
            {
                _abandonouHost = 0; // fora da sessão (saiu ou caiu): ao voltar, acompanha a run do host
                _semLiberar = false;
                _hostSabeQueEstouFora = false; // o host esquece o par que saiu (o Par é outro na volta)
                _seguindoHost = false;
            }
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
                // 0.19.0: só acompanha se o próprio save já chegou a esta masmorra e a este andar (decisão do autor: o coop
                // não dá progresso de campanha). Atalho especial não tem DungeonConfig nem trava.
                if (p.AndarEspecial == (int)ESpecialFloor.None)
                {
                    long recorde = RecordeNoSave(p.DungeonHash);
                    int andar = (int)Math.Min(p.IndiceAndar + 1u, (uint)Salas.AndarMaximo);
                    if (!Salas.Liberado(andar, recorde))
                    {
                        DungeonConfig dc = DungeonConfig.GetByID(p.DungeonHash);
                        _semLiberar = true;
                        _entradaTardia = null;
                        Plugin.Log.LogInfo($"[Run] Não acompanha a run do host: {p} (recorde do save nessa masmorra: {recorde}).");
                        Sessao.Avisar(Salas.TextoForaDaRun(Salas.AtoDoNome(dc != null ? dc.name : ""), andar, recorde));
                        AvisarHostForaDaRun(true);
                        return;
                    }
                }
                AvisarHostForaDaRun(false);
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
                    _seguindoHost = true; // a run é a do host: os avanços seguintes não passam pela trava
                }
                finally { _ordemDoHost = false; }
                return;
            }

            uint atual = IndiceAtual;
            // 0.19.0: run própria aberta ao entrar no coop (não foi o host quem a iniciou): avançar até o andar do host
            // também é entrada e passa pela trava, inclusive se a masmorra for outra.
            if (p.IndiceAndar > atual && !_seguindoHost && p.AndarEspecial == (int)ESpecialFloor.None)
            {
                DungeonConfig minha = Dpm.CurrentDungeonConfig;
                long recorde = RecordeNoSave(p.DungeonHash);
                int andar = (int)Math.Min(p.IndiceAndar + 1u, (uint)Salas.AndarMaximo);
                if (minha == null || minha.HashID != p.DungeonHash || !Salas.Liberado(andar, recorde))
                {
                    DungeonConfig dc = DungeonConfig.GetByID(p.DungeonHash);
                    _semLiberar = true;
                    Plugin.Log.LogInfo($"[Run] Não acompanha a run do host (run própria aberta): {p} (recorde do save nessa masmorra: {recorde}).");
                    Sessao.Avisar(minha != null && minha.HashID != p.DungeonHash
                        ? "Você está numa run sua, em outra masmorra. Termine-a para acompanhar o host."
                        : Salas.TextoForaDaRun(Salas.AtoDoNome(dc != null ? dc.name : ""), andar, recorde));
                    AvisarHostForaDaRun(true);
                    return;
                }
            }
            if (p.IndiceAndar > atual)
            {
                // 0.20.0: com a run aberta, o plano só avança a mesma run (o PlanoValido conferiu o índice contra a masmorra
                // do plano, não contra a sua) e no máximo SaltoPlanoMaximo andares por vez.
                DungeonConfig aberta = Dpm.CurrentDungeonConfig;
                if (!Validacao.PlanoDaRunAberta(p.DungeonHash, p.AndarEspecial, aberta != null ? aberta.HashID : 0, (int)Dpm.CurrentESpecialFloor))
                {
                    Plugin.Log.LogWarning($"[Run] Plano do host de outra run ignorado com a sua aberta: {p}.");
                    if (p.Primeiro) Sessao.Avisar("O host começou uma run nova, mas você ainda está na anterior. Termine-a para acompanhar.");
                    return;
                }
                uint salto = Validacao.SaltoPlano(p.IndiceAndar, atual);
                if (salto < p.IndiceAndar - atual)
                    Plugin.Log.LogWarning($"[Run] Plano do host {p.IndiceAndar - atual} andares à frente; avança só {salto} agora (o resto no próximo andar do host).");
                _seguindoHost = true;
                Plugin.Log.LogInfo($"[Run] Host avançou: {p} (você estava no andar {atual}).");
                _ordemDoHost = true;
                try { InternalAdvanceLevel.Invoke(Dpm, new object[] { salto }); }
                finally { _ordemDoHost = false; }
            }
            else if (!_seguindoHost && p.IndiceAndar == atual)
            {
                // 0.20.0: reconexão com a run aberta no mesmo andar do host (a queda da sessão zera _seguindoHost): volta a
                // acompanhar se é a mesma run e o save já chegou aqui, senão o Completed do host não concluiria a sua.
                DungeonConfig aberta = Dpm.CurrentDungeonConfig;
                bool mesma = Validacao.PlanoDaRunAberta(p.DungeonHash, p.AndarEspecial, aberta != null ? aberta.HashID : 0, (int)Dpm.CurrentESpecialFloor);
                bool liberado = p.AndarEspecial != (int)ESpecialFloor.None
                                || Salas.Liberado((int)Math.Min(p.IndiceAndar + 1u, (uint)Salas.AndarMaximo), RecordeNoSave(p.DungeonHash));
                if (Validacao.RetomaRunDoHost(p.IndiceAndar, atual, mesma, liberado))
                {
                    _seguindoHost = true;
                    Plugin.Log.LogInfo($"[Run] Voltou a acompanhar a run do host no mesmo andar: {p}.");
                }
            }
            else if (p.Primeiro && p.IndiceAndar < atual)
            {
                Sessao.Avisar("O host começou uma run nova, mas você ainda está na anterior. Termine-a para acompanhar.");
            }
        }

        /// <summary>
        /// Convidado (0.19.0): conta a todos os pares (o host e os outros convidados, que também escalam XP, ouro e
        /// réplicas pelo número de jogadores) que não acompanha a run (Fora) ou que voltou a acompanhar. "Voltou" só vai
        /// depois de um "fora" já mandado.
        /// </summary>
        private static void AvisarHostForaDaRun(bool fora)
        {
            if (!fora && !_hostSabeQueEstouFora) return;
            if (Sessao == null || !Sessao.EmSessao) return;
            Sessao.EnviarParaConvidados(new ForaDaRun { Fora = fora }.Serializar(), confiavel: true);
            _hostSabeQueEstouFora = fora;
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
                    if (plano.Primeiro) { _abandonouHost = 0; _semLiberar = false; } // run nova do host: confere de novo
                    else if (_abandonouHost != 0 && _abandonouHost == de.Id.m_SteamID) return; // você saiu desta run
                    else if (_semLiberar) return; // 0.19.0: o save não chegou a esta run; espera a próxima
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

                case Protocolo.TipoPedidoAvanco:
                    if (!Host || !de.Compativel || !de.DlcOk) return; // só quem divide a run
                    PedidoAvanco pa = PedidoAvanco.Desserializar(dados);
                    if (!JogadorCaido.Caido || !Dpm.RunStarted.Value || Dpm.EndingExecuted.Value || Dpm.IsTransitioning ||
                        ClassSingleton<LevelChanger>.Instance.IsChangingLevel.Value || _andarAvancadoPorPedido == IndiceAtual)
                    {
                        LogPedido(false, $"[Run] Pedido de avanço de {de.Nome} ignorado: você não está caído, o andar está trocando ou já foi pedido.");
                        return;
                    }
                    // Último andar: a saída é o portal do fim da dungeon (não passa por aqui); um avanço nele gravaria a
                    // dungeon como concluída no save do host.
                    if (EhUltimoAndar == null) // jogo mudou: sem saber se é o último andar, recusa (protege o save)
                    {
                        LogPedido(true, "[Run] Pedido de avanço recusado: DungeonProgressManager.IsLastLevel não existe nesta versão do jogo.");
                        return;
                    }
                    bool ultimo = (bool)EhUltimoAndar.Invoke(Dpm, null);
                    if (!de.MesmoAndar || !Validacao.PedidoAvancoValido(pa.IndiceAndar, pa.Andares, IndiceAtual, ultimo))
                    {
                        LogPedido(true, $"[Run] Pedido de avanço de {de.Nome} recusado (andar {pa.IndiceAndar}, {pa.Andares} andar(es); você no andar {IndiceAtual}{(ultimo ? ", o último" : "")}).");
                        return;
                    }
                    uint andares = Math.Min(pa.Andares, Validacao.AvancoMaximo);
                    Plugin.Log.LogInfo($"[Run] Você está caído: {de.Nome} leva o grupo ({andares} andar(es)).");
                    Sessao.Avisar($"{de.Nome} levou o grupo para o próximo andar.");
                    _andarAvancadoPorPedido = IndiceAtual; // um avanço por andar (no Endless Nightmare a troca é assíncrona)
                    JogadorCaido.AvancoPedido(); // até a troca começar, os aliados não contam como fora da run
                    Dpm.AdvanceLevel(andares, false); // o jogo ainda limita pelo máximo dele
                    break;

                case Protocolo.TipoForaDaRun: // 0.19.0: cada par só fala de si; todos tiram da escala quem está fora
                    if (!de.Compativel || !de.DlcOk) return;
                    bool fora = ForaDaRun.Desserializar(dados).Fora;
                    if (de.SemLiberar == fora) return;
                    de.SemLiberar = fora;
                    if (!Host || Time.unscaledTime < de.ProximoAvisoFora) return; // aviso e log: só no host, 1 a cada 30 s por par
                    de.ProximoAvisoFora = Time.unscaledTime + 30f;
                    Plugin.Log.LogInfo($"[Run] {de.Nome} {(fora ? "não acompanha a run (o save dele não chegou aqui); fora da escala do grupo" : "voltou a acompanhar")}.");
                    if (fora) Sessao.Avisar($"{de.Nome} ainda não chegou aqui na campanha: fica no hub, fora da escala do grupo.");
                    break;

                case Protocolo.TipoFimRun:
                    if (!Convidado || de != sessao.Host) return;
                    FimRun f = FimRun.Desserializar(dados);
                    if (!Validacao.MotivoFimRunValido(f.Motivo))
                    {
                        Plugin.Log.LogWarning($"[Run] Fim de run com motivo desconhecido ({f.Motivo}); ignorado.");
                        return;
                    }
                    bool seguia = _seguindoHost;
                    Planos.Clear(); SpawnsDoHost.Clear(); _planoPendente = null; _entradaTardia = null; SementeCena = 0;
                    _abandonouHost = 0; // a run que você abandonou acabou
                    _semLiberar = false; // 0.19.0: a run que você não acompanhou acabou; volta a contar na escala
                    _seguindoHost = false;
                    AvisarHostForaDaRun(false);
                    JogadorCaido.FimDaRunNoHost(); // o convidado também esquece as quedas já avisadas
                    if (!Dpm.RunStarted.Value || Dpm.EndingExecuted.Value) return;
                    // 0.20.0: Completed (dungeon concluída no save) só no último andar de uma run que acompanha o host.
                    bool ultimoAndar = false;
                    if (f.Motivo == Validacao.MotivoCompleted && EhUltimoAndar != null)
                    {
                        try { ultimoAndar = (bool)EhUltimoAndar.Invoke(Dpm, null); }
                        catch (Exception) { } // sem saber, não conclui (encerra sem concluir)
                    }
                    int motivo = Validacao.MotivoFimRunAplicado(f.Motivo, ultimoAndar, seguia);
                    if (motivo != f.Motivo) Plugin.Log.LogWarning($"[Run] Host mandou {(EGameOverSource)f.Motivo}, mas você não está no último andar de uma run que acompanha; a sua encerra como {(EGameOverSource)motivo}.");
                    Plugin.Log.LogInfo($"[Run] Host encerrou a run ({(EGameOverSource)motivo}).");
                    _ordemDoHost = true;
                    try { Dpm.FinishDungeon((EGameOverSource)motivo); }
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

        /// <summary>Convidado fora da run (0.19.0): conta a quem chegou depois (o aviso só foi a quem já estava).</summary>
        public static void AoNovoPar(SessaoCoop sessao, Par p)
        {
            if (Convidado && _hostSabeQueEstouFora) sessao.EnviarPara(p, new ForaDaRun { Fora = true }.Serializar());
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
            _andarAvancadoPorPedido = -1;
            JogadorCaido.FimDaRunNoHost();
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
        /// Reserva: desde a 0.15.0 o convidado que morre fica caído (JogadorCaido) e a morte do jogo não roda. Se ela
        /// rodar mesmo assim (PlayDeathAnimation → FinishDungeon(Defeat) com o jogador morto), deixa a run dele acabar
        /// como no solo, em vez de travar; o plano do próximo andar do host o traz de volta (entrada no meio da run).
        /// </summary>
        internal static bool ConvidadoMorreu(EGameOverSource fonte)
        {
            if (!Convidado || _ordemDoHost || fonte != EGameOverSource.Defeat) return false; // fim mandado pelo host: não é esta reserva
            PlayerController local = CoopRuntime.JogadorLocal();
            if (local == null || !local.LifeController.IsDead) return false;
            SementeCena = 0; // a run dele acaba: o hub sorteia como no jogo
            Plugin.Log.LogWarning("[Run] A morte do jogo rodou no convidado (era para ficar caído): sua run acaba; você volta quando o host passar de andar.");
            Sessao.Avisar("Sua run acabou. Quando o host passar de andar, você volta para a run dele.");
            return true;
        }

        /// <summary>
        /// Convidado caído que abandona a run pelo menu (Esc → Terminar run): só a run dele acaba, como no
        /// solo; o host e os outros continuam. Até o host encerrar a run (FimRun) ou começar outra, os planos dela são
        /// ignorados, para ele não ser puxado de volta no próximo andar. De pé, o "Terminar run" do convidado segue
        /// bloqueado (o fim da run é do host). Decisão do autor em 22/11/2026.
        /// </summary>
        internal static bool ConvidadoAbandonou(EGameOverSource fonte)
        {
            if (!Convidado || _ordemDoHost || fonte != EGameOverSource.ManualFinish || !JogadorCaido.Caido) return false;
            _abandonouHost = Sessao.Host != null ? Sessao.Host.Id.m_SteamID : 0;
            Planos.Clear(); SpawnsDoHost.Clear(); _planoPendente = null; _entradaTardia = null; SementeCena = 0;
            Plugin.Log.LogInfo("[Run] Você abandonou a run caído; o grupo continua sem você.");
            Sessao.Avisar("Você saiu da run. O grupo continua; você volta na próxima run do host.");
            return true;
        }

        /// <summary>
        /// Convidado no portal com o host caído (0.15.0): pede ao host que avance o grupo. True = pedido enviado (o
        /// avanço local não roda; o plano do host traz o convidado).
        /// </summary>
        internal static bool PedirAvancoComHostCaido(uint andares)
        {
            if (!Convidado || _ordemDoHost || !Dpm.RunStarted.Value) return false;
            Par host = Sessao.Host;
            if (host == null || !host.Morto || !host.MesmoAndar) return false;
            if (Time.unscaledTime < _proximoPedidoAvanco) return true; // cliques repetidos no portal
            _proximoPedidoAvanco = Time.unscaledTime + 2f;
            Sessao.EnviarPara(host, new PedidoAvanco { IndiceAndar = IndiceAtual, Andares = andares }.Serializar());
            Plugin.Log.LogInfo($"[Run] O host está caído: pedido para avançar {andares} andar(es) do andar {IndiceAtual}.");
            Sessao.Avisar("O host está caído: pedido enviado para levar o grupo ao próximo andar.");
            return true;
        }

        /// <summary>Host: tira segundos do Hell Clock (queda de um jogador). Devolve quanto tirou.</summary>
        internal static float PerderTempo(float segundos)
        {
            segundos = Validacao.Finito(segundos) ? Mathf.Clamp(segundos, 0f, 60f) : 0f;
            TimerManager tm = TimerManager.Instance;
            if (segundos <= 0f || tm == null || !Dpm.RunStarted.Value) return 0f;
            FloatState relogio = tm.RelaxedModeEnabled ? TimerRelaxadoRef(tm) : TimerRef(tm);
            if (relogio.Value <= 0f) return 0f; // modo relaxado pode estar negativo: subir a 0 viraria bônus
            relogio.Setter(Mathf.Max(0f, relogio.Value - segundos)); // zerado, o próprio jogo encerra a run por tempo
            return segundos;
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
