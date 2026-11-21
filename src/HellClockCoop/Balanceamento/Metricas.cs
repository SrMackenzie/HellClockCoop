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
                string arquivo = Path.Combine(Pasta, $"run-{_inicioData:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(arquivo, Json(), new UTF8Encoding(false));
                Plugin.Log.LogInfo($"[Métricas] Resumo da run salvo em metricas/{Path.GetFileName(arquivo)} ({Andares.Count} andares, {_jogadoresMax} jogador(es), {_resultado}).");
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

        private static string Json()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append('{');
            Campo(sb, "formato", VersaoFormato.ToString(c)); sb.Append(',');
            Texto(sb, "mod", Plugin.PluginVersion); sb.Append(',');
            Texto(sb, "jogo", Application.version); sb.Append(',');
            Texto(sb, "instalacao", IdInstalacao()); sb.Append(',');
            Texto(sb, "papel", _papel); sb.Append(',');
            Campo(sb, "jogadores", _jogadoresMax.ToString(c)); sb.Append(',');
            Campo(sb, "dungeon", _dungeon.ToString(c)); sb.Append(',');
            Texto(sb, "dungeonNome", _dungeonNome); sb.Append(',');
            Campo(sb, "tier", _tier.ToString(c)); sb.Append(',');
            Campo(sb, "ascensao", _ascensao ? "true" : "false"); sb.Append(',');
            Campo(sb, "nivel", _nivel.ToString(c)); sb.Append(',');
            Texto(sb, "resultado", _resultado); sb.Append(',');
            Campo(sb, "duracao", (Time.unscaledTime - _inicio).ToString("0.0", c)); sb.Append(',');
            Texto(sb, "balanceamento", Escalonamento.Resumo() ?? "solo"); sb.Append(',');
            sb.Append("\"andares\":[");
            for (int i = 0; i < Andares.Count; i++)
            {
                Andar a = Andares[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Campo(sb, "indice", a.Indice.ToString(c)); sb.Append(',');
                Campo(sb, "definicao", a.Definicao.ToString(c)); sb.Append(',');
                Campo(sb, "chefe", a.Chefe ? "true" : "false"); sb.Append(',');
                Campo(sb, "jogadores", a.Jogadores.ToString(c)); sb.Append(',');
                Campo(sb, "segundos", Math.Max(0f, (a.Fim > 0f ? a.Fim : Time.unscaledTime) - a.Inicio).ToString("0.0", c)); sb.Append(',');
                Campo(sb, "mortos", a.Mortos.ToString(c)); sb.Append(',');
                Campo(sb, "danoCausado", a.DanoCausado.ToString("0", c)); sb.Append(',');
                Campo(sb, "danoRecebido", a.DanoRecebido.ToString("0", c)); sb.Append(',');
                Campo(sb, "mortes", a.Mortes.ToString(c)); sb.Append(',');
                Campo(sb, "relogioEntrada", a.RelogioEntrada.ToString("0.0", c)); sb.Append(',');
                Campo(sb, "relogioSaida", a.RelogioSaida.ToString("0.0", c));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void Campo(StringBuilder sb, string nome, string valor) => sb.Append('"').Append(nome).Append("\":").Append(valor);

        private static void Texto(StringBuilder sb, string nome, string valor)
        {
            sb.Append('"').Append(nome).Append("\":\"");
            foreach (char ch in valor ?? "")
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append(' ');
                else sb.Append(ch);
            }
            sb.Append('"');
        }
    }
}
