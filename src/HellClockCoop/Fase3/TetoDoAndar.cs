using System;
using System.Collections.Generic;
using HarmonyLib;
using HellClock.Character.Enemy;
using HellClock.Chests;
using HellClock.Extensions.HelperTypes;
using HellClock.Level;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Teto por andar do que o host manda criar no convidado (0.22.0), calculado no convidado com os dados do próprio jogo
    /// para aquele andar, no lugar dos genéricos da 0.21.1. O convidado passa pelo LevelObjects.SetupEnemies e
    /// SetupLevelObjects do andar (os dois pulados por ele, Fase2/PatchesRun) com o LevelInfo do andar do host; dali sai o
    /// máximo que a geração do jogo pode pôr no andar. Validacao.TetoInimigos aplica o fator de quantidade do mod e a folga.
    ///
    /// Geração do andar (IL da 2.1.1.1, LevelObjects.SetupEnemies / EnemiesPostProcessSetup / TryUseSquadPool):
    ///  - composição (LevelDefinition._enemySpawnCompositions, uma sorteada): cada ponto i recebe AmountPerSpawn[i] de cada
    ///    EnemyGroupSpawns (EnemyManager.SpawnRegularEnemyGroup cria um por unidade), mais até EliteEnemyRange.MaxValue elites;
    ///  - esquadrões (composição sem grupos, composition.SquadsPool ou LevelInfo.SquadPool): AggregatedSubsetSelector — cada
    ///    seletor sorteia de Min a Max entradas (vezes o modificador RegularEnemyQuantity no pool) mais as garantidas, e cada
    ///    esquadrão sorteia os inimigos do mesmo jeito; mais as elites e o chefe forçado;
    ///  - elite: campeão (ChampionEnemyDefinition.AmountRange) ou raro com lacaios (RareEnemyDefinition.MinionAmountRange); o
    ///    pack de elite é o maior desses na coleção de inimigos do jogo.
    /// Sobrevivência (SurvivalEventManager.StartWave): cada onda até floor(TotalToEndWave (ou o da onda) × densidade), com as
    /// elites dentro do total, mais o chefe. Baús (LevelDefinition.GenerateChestsSpawnInfo): min(_chestsRangeAmount.MaxValue,
    /// pontos de baú) mais os que vêm na cena do andar (o host varre todos os ChestInteractable).
    /// Opcionais, goblin, par do chefe e único de Nightmare entram na folga fixa (Validacao.FolgaInimigosAndar).
    /// Qualquer falha na leitura deixa -1, e o teto volta ao genérico (registrado no log).
    /// </summary>
    internal static class TetoDoAndar
    {
        private static long _andar = -1;
        private static int _inimigosDoJogo = -1, _ondasDoJogo = -1, _bausDoJogo = -1;
        private static int _packDeEliteMaximo = -1;
        // Ondas da última sobrevivência da run: ela pode seguir por vários andares (SurvivalSpawnConfig.MultiFloorCount,
        // SurvivalEventManager.OnChangeLevel) com a config do andar em que começou.
        private static int _ondasDaRun = Validacao.SemOndas;

        // Criado na primeira leitura, dentro do try: um erro aqui não pode quebrar o tipo inteiro (a criação das réplicas usa os tetos).
        private static AccessTools.FieldRef<LevelDefinition, IntValueRange> _faixaDeBausRef;

        /// <summary>Fator de quantidade usado no teto: o maior do mod (4 jogadores), porque o grupo pode crescer no andar.</summary>
        private static float FatorQuantidade
        {
            get
            {
                float f = 1f;
                foreach (float q in Balanceamento.Escalonamento.QuantidadeGrupo) f = Math.Max(f, q);
                return f;
            }
        }

        internal static int TetoAndar(long andar) =>
            Validacao.TetoInimigos(andar == _andar ? _inimigosDoJogo : -1, FatorQuantidade, Validacao.FolgaInimigosAndar);

        internal static int TetoSobrevivencia(long andar) =>
            Validacao.TetoSobrevivencia(andar == _andar ? _ondasDoJogo : -1, FatorQuantidade);

        internal static int TetoBaus(long andar) => Validacao.TetoBaus(andar == _andar ? _bausDoJogo : -1);

        /// <summary>Hub ou fim da sessão: nada calculado vale para a run seguinte.</summary>
        internal static void Esquecer()
        {
            _andar = -1;
            _inimigosDoJogo = _ondasDoJogo = _bausDoJogo = -1;
            _ondasDaRun = Validacao.SemOndas;
        }

        private static void NovoAndar(uint andar)
        {
            if (_andar == andar) return;
            _andar = andar;
            _inimigosDoJogo = _ondasDoJogo = _bausDoJogo = -1;
        }

        /// <summary>Prefixo do SetupEnemies no convidado: máximo de inimigos da geração do andar e das ondas.</summary>
        internal static void CalcularInimigos(LevelInfo info)
        {
            if (info == null) return;
            NovoAndar(info.LevelIndex);
            try
            {
                LevelDefinition def = info.LevelDefinition;
                if (def == null) throw new InvalidOperationException("andar sem LevelDefinition");
                // TryUseSquadPool repete o sorteio do pool ceil(q) vezes (q = RegularEnemyQuantity), e as garantidas contam em
                // cada uma: aqui, o maior q do mod com a folga da árvore do host.
                int repeticoes = (int)Math.Ceiling(FatorQuantidade * Validacao.FolgaQuantidade);
                int elite = PackDeEliteMaximo();
                int elitesMax = 0;
                if (def.EnemySpawnCompositions != null)
                {
                    foreach (var w in def.EnemySpawnCompositions)
                        if (w?.Value != null) elitesMax = Math.Max(elitesMax, w.Value.EliteEnemyRange?.MaxValue ?? 0);
                }
                // o pool do LevelInfo recebe as elites da composição sorteada (SetupEnemies)
                int geracao = info.SquadPool != null ? Esquadroes(info.SquadPool, repeticoes, elitesMax, elite) : 0;
                if (def.EnemySpawnCompositions != null)
                {
                    foreach (var w in def.EnemySpawnCompositions)
                    {
                        EnemySpawnComposition c = w?.Value;
                        if (c == null) continue;
                        int elites = Math.Max(0, c.EliteEnemyRange?.MaxValue ?? 0);
                        int grupos = 0;
                        if (c.EnemyGroupSpawns != null)
                        {
                            foreach (EnemyGroupSpawns g in c.EnemyGroupSpawns)
                            {
                                if (g.AmountPerSpawn == null) continue; // struct
                                foreach (int n in g.AmountPerSpawn) grupos += Math.Max(0, n);
                            }
                        }
                        geracao = Math.Max(geracao, grupos + elites * elite);
                        if (c.SquadsPool != null) geracao = Math.Max(geracao, Esquadroes(c.SquadsPool, repeticoes, elites, elite));
                    }
                }
                _inimigosDoJogo = geracao + 1; // + o chefe forçado do andar de chefe
                if (def.IsSurvivalFloor)
                {
                    _ondasDoJogo = Ondas(def.SurvivalSpawnConfig);
                    if (_ondasDoJogo >= 0) _ondasDaRun = _ondasDoJogo;
                }
                else _ondasDoJogo = _ondasDaRun; // sobrevivência que segue de um andar anterior, ou SemOndas
                Plugin.Log.LogInfo($"[Teto] Andar {info.LevelIndex} ({def.name}): até {_inimigosDoJogo} inimigos na geração do jogo" +
                    (_ondasDoJogo >= 0 ? $" e {_ondasDoJogo} nas ondas{(def.IsSurvivalFloor ? "" : " (sobrevivência de um andar anterior)")}" : "") +
                    $"; teto {TetoAndar(info.LevelIndex)} do andar e {TetoSobrevivencia(info.LevelIndex)} de sobrevivência.");
            }
            catch (Exception e)
            {
                _inimigosDoJogo = _ondasDoJogo = -1;
                Plugin.Log.LogWarning($"[Teto] Andar {info.LevelIndex}: dados de inimigos não lidos ({(e.InnerException ?? e).Message}); teto genérico.");
            }
        }

        /// <summary>Prefixo do SetupLevelObjects no convidado: máximo de baús sorteados mais os da cena do andar.</summary>
        internal static void CalcularBaus(LevelInfo info)
        {
            if (info == null) return;
            NovoAndar(info.LevelIndex);
            try
            {
                LevelDefinition def = info.LevelDefinition;
                if (def == null) throw new InvalidOperationException("andar sem LevelDefinition");
                if (_faixaDeBausRef == null) _faixaDeBausRef = AccessTools.FieldRefAccess<LevelDefinition, IntValueRange>("_chestsRangeAmount");
                IntValueRange faixa = _faixaDeBausRef(def);
                int pontos = def.ChestSpawnPoints?.Count ?? 0;
                int sorteados = Math.Min(Math.Max(0, faixa?.MaxValue ?? 0), pontos);
                int naCena = 0;
                foreach (ChestInteractable b in UnityEngine.Object.FindObjectsByType<ChestInteractable>(FindObjectsSortMode.None))
                {
                    if (!ObjetosCompartilhados.EhBauDoHost(b)) naCena++;
                }
                _bausDoJogo = sorteados + naCena;
                Plugin.Log.LogInfo($"[Teto] Andar {info.LevelIndex}: até {_bausDoJogo} baús ({sorteados} sorteados, {naCena} da cena); teto {TetoBaus(info.LevelIndex)}.");
            }
            catch (Exception e)
            {
                _bausDoJogo = -1;
                Plugin.Log.LogWarning($"[Teto] Andar {info.LevelIndex}: dados de baús não lidos ({(e.InnerException ?? e).Message}); teto genérico.");
            }
        }

        /// <summary>Máximo de esquadrões do pool (em todas as repetições) vezes o maior esquadrão, mais as elites.</summary>
        private static int Esquadroes(SquadPoolDefinition pool, int repeticoes, int elites, int packDeElite)
        {
            int maiorEsquadrao = 0;
            int esquadroes = Sorteio(pool, 1f, e =>
            {
                if (e is IAggregatedSubsetSelector esquadrao) maiorEsquadrao = Math.Max(maiorEsquadrao, Sorteio(esquadrao, 1f, null));
            });
            long total = (long)Math.Max(1, repeticoes) * esquadroes * maiorEsquadrao + (long)elites * packDeElite;
            return total > Validacao.InimigosPorAndarMaximo ? Validacao.InimigosPorAndarMaximo : (int)total;
        }

        /// <summary>
        /// Máximo de entradas que um AggregatedSubsetSelector sorteia (SubsetSelectorExtension.Roll): por seletor, as garantidas
        /// mais floor(Max × multiplicador). <paramref name="porElemento"/> vê cada elemento possível.
        /// </summary>
        private static int Sorteio(IAggregatedSubsetSelector sel, float multiplicador, Action<object> porElemento)
        {
            long total = 0;
            for (int i = 0; i < sel.Count; i++)
            {
                if (!(sel.GetEntryObject(i) is ISubsetSelector s)) continue;
                long garantidas = 0;
                for (int j = 0; j < s.EntriesCount; j++)
                {
                    if (!(s.GetEntryObject(j) is IWeightedSubsetEntry e) || !e.IsValid) continue;
                    garantidas += Math.Max(0, e.Guaranteed);
                    porElemento?.Invoke(e.ElementAsObject);
                }
                total += garantidas + (long)Math.Floor(Math.Max(0, s.Max) * (double)multiplicador);
            }
            return total > Validacao.InimigosPorAndarMaximo ? Validacao.InimigosPorAndarMaximo : (int)total;
        }

        /// <summary>Maior pack de elite do jogo: campeões (AmountRange) e raro com lacaios (1 + MinionAmountRange).</summary>
        private static int PackDeEliteMaximo()
        {
            if (_packDeEliteMaximo > 0) return _packDeEliteMaximo;
            int maior = 1;
            foreach (EnemyDefinition d in global::HellClock.Constants.Collections.ENEMY_DEFINITION_COLLECTION.Reference)
            {
                if (d is ChampionEnemyDefinition c) maior = Math.Max(maior, c.AmountRange?.MaxValue ?? 1);
                else if (d is RareEnemyDefinition r) maior = Math.Max(maior, 1 + (r.MinionAmountRange?.MaxValue ?? 0));
            }
            return _packDeEliteMaximo = maior;
        }

        /// <summary>Soma das ondas sem densidade (o fator de quantidade entra no teto) mais o chefe.</summary>
        private static int Ondas(SurvivalSpawnConfig cfg)
        {
            if (cfg?.Waves == null) return -1;
            long total = 1; // o chefe
            foreach (SurvivalSpawnWaveConfig onda in cfg.Waves)
            {
                int n = onda?.TotalToEndWaveOverride != null ? onda.TotalToEndWaveOverride.GetValue(cfg.TotalToEndWave) : cfg.TotalToEndWave;
                total += Math.Max(0, n);
            }
            return total > Validacao.InimigosPorAndarMaximo ? Validacao.InimigosPorAndarMaximo : (int)total;
        }
    }
}
