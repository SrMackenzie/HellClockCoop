using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using HellClock.Character;
using HellClock.Character.Enemy;
using HellClock.Damage;
using HellClock.Level;
using HellClock.Manager;
using HellClock.Player;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using UnityEngine;

namespace HellClockCoop.Balanceamento
{
    /// <summary>
    /// Resumo de balanceamento de cada run, gravado no fim em BepInEx/HellClockCoop/metricas/run-*.json.
    /// Só números: nada de nome da Steam, SteamID ou caminhos do computador. O identificador da instalação é
    /// aleatório (instalacao.txt) e serve só para separar as runs de quem joga muito das de quem joga pouco.
    /// O envio (opcional, com aceite) vem numa versão seguinte; o formato aqui é o mesmo que será enviado.
    /// </summary>
    internal static class Metricas
    {
        public const int VersaoFormato = 1;

        private sealed class Andar
        {
            public uint Indice;
            public int Definicao;
            public bool Chefe;
            public int Jogadores;
            public float Inicio, Fim;
            public int Mortos;
            public double DanoCausado, DanoRecebido;
            public int Mortes;
            public int Quedas, Reanimacoes, Reanimado; // 0.16.0: quedas suas, aliados que você levantou e vezes que te levantaram
            public float RelogioEntrada, RelogioSaida;
        }

        private static bool _ativa;
        private static DateTime _inicioData;
        private static float _inicio;
        private static string _papel = "solo";
        private static int _jogadoresMax = 1;
        private static int _dungeon;
        private static string _dungeonNome = "";
        private static int _nivel;
        private static int _tier;
        private static bool _ascensao;
        private static string _resultado = "?";
        private static readonly List<Andar> Andares = new List<Andar>();
        private static Andar _atual;

        private static PlayerController _jogadorRegistrado;
        private static EnemyManager _emRegistrado;

        private static string Pasta => Path.Combine(Paths.BepInExRootPath, "HellClockCoop", "metricas");

        public static void Atualizar(PlayerController jogador)
        {
            DungeonProgressManager dpm = DungeonProgressManager.Instance;
            if (dpm == null) return;
            Registrar(jogador);

            LevelInfo andar = LocalPlayerSampler.AndarAtual();
            bool emRun = dpm.RunStarted.Value || (andar != null && andar.LevelBlockInfo != null);
            if (emRun && !_ativa) Comecar(dpm, jogador);
            if (!_ativa) return;

            int n = Escalonamento.Jogadores;
            if (n > _jogadoresMax) _jogadoresMax = n;
            if (RunCompartilhada.Host) _papel = "host";
            else if (RunCompartilhada.Convidado) _papel = "convidado";

            if (andar != null && andar.LevelBlockInfo != null && (_atual == null || _atual.Indice != andar.LevelIndex))
            {
                FecharAndar();
                _atual = new Andar
                {
                    Indice = andar.LevelIndex,
                    Definicao = andar.LevelDefinition != null ? andar.LevelDefinition.HashID : 0,
                    Chefe = andar.IsBossLevel,
                    Jogadores = n,
                    Inicio = Time.unscaledTime,
                    RelogioEntrada = Relogio(),
                };
                Andares.Add(_atual);
            }
            if (_atual != null && n > _atual.Jogadores) _atual.Jogadores = n;

            if (!dpm.RunStarted.Value && andar != null && andar.LevelBlockInfo == null) Terminar(); // voltou ao hub
        }

        private static void Comecar(DungeonProgressManager dpm, PlayerController jogador)
        {
            _ativa = true;
            _inicioData = DateTime.Now;
            _inicio = Time.unscaledTime;
            _jogadoresMax = Escalonamento.Jogadores;
            _papel = RunCompartilhada.Host ? "host" : RunCompartilhada.Convidado ? "convidado" : "solo";
            _dungeon = dpm.CurrentDungeonConfig != null ? dpm.CurrentDungeonConfig.HashID : 0;
            _dungeonNome = dpm.CurrentDungeonConfig != null ? dpm.CurrentDungeonConfig.name : "";
            _resultado = "?";
            try
            {
                _nivel = jogador?.PlayerSheet?.Experience?.Level?.Value ?? 0;
                _tier = (int)GameDifficultyController.CurrentWorldTier;
                _ascensao = GameDifficultyController.IsOnAscension;
            }
            catch (Exception) { }
            Andares.Clear();
            _atual = null;
        }

        private static void Registrar(PlayerController jogador)
        {
            if (jogador != null && !ReferenceEquals(jogador, _jogadorRegistrado))
            {
                _jogadorRegistrado = jogador;
                jogador.OnDealDamage.Register((IDamageable alvo, DamageInstance d) => { if (_ativa && _atual != null && d != null) _atual.DanoCausado += d.Value; });
                jogador.LifeController.OnHurt.Register((DamageInstance d) => { if (_ativa && _atual != null && d != null) _atual.DanoRecebido += d.LifeDamageValue; });
                jogador.LifeController.OnDie.Register((DamageInstance d) => { if (_ativa && _atual != null) _atual.Mortes++; });
            }
            EnemyManager em = EnemyManager.Instance;
            if (em != null && !ReferenceEquals(em, _emRegistrado))
            {
                _emRegistrado = em;
                em.OnEnemyKilled.Register((IEnemy e, DamageInstance d) => { if (_ativa && _atual != null) _atual.Mortos++; });
            }
        }

        /// <summary>Você caiu no coop (JogadorCaido.Cair, 0.16.0). A morte final continua em "mortes".</summary>
        internal static void Caiu() { if (_ativa && _atual != null) _atual.Quedas++; }

        /// <summary>Você levantou um aliado caído (Reanimacao, 0.16.0).</summary>
        internal static void Reanimei() { if (_ativa && _atual != null) _atual.Reanimacoes++; }

        /// <summary>Um aliado te levantou (Reanimacao, 0.16.0).</summary>
        internal static void FuiReanimado() { if (_ativa && _atual != null) _atual.Reanimado++; }

        /// <summary>Postfix de DungeonProgressManager.FinishDungeon: guarda o motivo (vale o último antes de voltar ao hub).</summary>
        internal static void Motivo(EGameOverSource fonte) => _resultado = fonte.ToString();

        private static float Relogio()
        {
            try { return TimerManager.Instance != null ? TimerManager.Instance.CurrentTimer : 0f; }
            catch (Exception) { return 0f; }
        }

        private static void FecharAndar()
        {
            if (_atual == null || _atual.Fim > 0f) return;
            _atual.Fim = Time.unscaledTime;
            _atual.RelogioSaida = Relogio();
        }

        private static void Terminar()
        {
            FecharAndar();
            _ativa = false;
            if (Andares.Count == 0) return;
            try
            {
                Directory.CreateDirectory(Pasta);
                string carimbo = _inicioData.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string arquivo = Path.Combine(Pasta, $"run-{carimbo}.json");
                Doc doc = Documento();
                File.WriteAllText(arquivo, Serializar.Json(doc), new UTF8Encoding(false));
                Plugin.Log.LogInfo($"[Métricas] Resumo da run salvo em metricas/{Path.GetFileName(arquivo)} ({Andares.Count} andares, {_jogadoresMax} jogador(es), {_resultado}).");
                Envio.RunSalva(IdInstalacao() + "-" + carimbo, doc);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Métricas] Não deu para salvar o resumo: {(e.InnerException ?? e).Message}");
            }
            Andares.Clear();
            _atual = null;
        }

        private static string IdInstalacao()
        {
            try
            {
                string f = Path.Combine(Paths.BepInExRootPath, "HellClockCoop", "instalacao.txt");
                if (File.Exists(f)) return File.ReadAllText(f).Trim();
                string id = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, id);
                return id;
            }
            catch (Exception) { return "desconhecida"; }
        }

        /// <summary>O resumo da run como árvore simples (Doc = campos em ordem; listas; string, int, long, double, bool).</summary>
        private static Doc Documento()
        {
            var d = new Doc
            {
                { "formato", VersaoFormato },
                { "mod", Plugin.PluginVersion },
                { "jogo", Application.version },
                { "instalacao", IdInstalacao() },
                { "papel", _papel },
                { "jogadores", _jogadoresMax },
                { "dungeon", _dungeon },
                { "dungeonNome", _dungeonNome ?? "" },
                { "tier", _tier },
                { "ascensao", _ascensao },
                { "nivel", _nivel },
                { "resultado", _resultado },
                { "duracao", Math.Round(Time.unscaledTime - _inicio, 1) },
                { "balanceamento", Escalonamento.Resumo() ?? "solo" },
            };
            var andares = new List<object>();
            foreach (Andar a in Andares)
            {
                andares.Add(new Doc
                {
                    { "indice", (long)a.Indice },
                    { "definicao", a.Definicao },
                    { "chefe", a.Chefe },
                    { "jogadores", a.Jogadores },
                    { "segundos", Math.Round(Math.Max(0f, (a.Fim > 0f ? a.Fim : Time.unscaledTime) - a.Inicio), 1) },
                    { "mortos", a.Mortos },
                    { "danoCausado", Math.Round(a.DanoCausado) },
                    { "danoRecebido", Math.Round(a.DanoRecebido) },
                    { "mortes", a.Mortes },
                    { "quedas", a.Quedas },
                    { "reanimacoes", a.Reanimacoes },
                    { "reanimado", a.Reanimado },
                    { "relogioEntrada", Math.Round(a.RelogioEntrada, 1) },
                    { "relogioSaida", Math.Round(a.RelogioSaida, 1) },
                });
            }
            d.Add("andares", andares);
            return d;
        }
    }
}
