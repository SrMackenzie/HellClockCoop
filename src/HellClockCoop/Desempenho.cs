using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace HellClockCoop
{
    /// <summary>
    /// Medidor de travadas (0.9.7). Cronometra cada parte do mod a cada quadro; num quadro bem mais longo que o normal,
    /// escreve no log quanto dele foi do mod (e de qual parte), se teve coleta de lixo, rede, log e carregamento.
    /// A cada 5 min em jogo (e ao trocar de solo para coop), um resumo com os números. Só mede: não muda nada no jogo.
    /// O quadro medido vai de um <see cref="NovoQuadro"/> ao seguinte (começo do Update do mod).
    /// </summary>
    internal static class Desempenho
    {
        /// <summary>Partes do mod que não se sobrepõem: a soma é o tempo do mod no quadro.</summary>
        internal enum Parte { Menus, Rede, Run, Inimigos, Objetos, Santuarios, Portas, Alvos, Fx, Danos, Sons, Balanceamento, Personagem, Interface, Ganchos, Quantidade }

        /// <summary>Trechos suspeitos medidos por dentro das partes (não entram na soma).</summary>
        internal enum Detalhe { CriarAvatar, CarregarPrefabFx, CopiarFx, BuscarOuvintesEsc, MedirTextoMenu, CriarLinhaMenu, BuscarBotoesJogo, Quantidade }

        private static readonly string[] NomesParte =
            { "Menus", "Rede", "Run", "Inimigos", "Objetos", "Santuários", "Portas", "Alvos", "Efeitos", "Danos", "Sons", "Balanceamento", "Personagem", "Interface", "Ganchos" };
        private static readonly string[] NomesDetalhe =
            { "criar avatar", "carregar prefab de efeito", "copiar efeito", "buscar ouvintes do Esc", "medir texto do menu", "criar linha do menu", "buscar botões do jogo" };

        private const double LimiteMinimoMs = 50.0;   // abaixo disso não é travada perceptível
        private const double FatorTravada = 2.5;      // e precisa ser bem maior que o quadro normal
        private const float IntervaloResumo = 300f;
        private const float MinimoParaResumo = 30f;   // janela curta demais (troca de contexto logo no início) não vira resumo
        private const int LinhasPorJanela = 40;       // travadas escritas por janela de resumo; o resto só conta

        /// <summary>Verdadeiro enquanto o laço do mod (Update, LateUpdate, OnGUI) mede as próprias partes.</summary>
        internal static bool NoLaco;

        // Quadro em curso
        private static readonly double[] _ms = new double[(int)Parte.Quantidade];
        private static readonly double[] _detMs = new double[(int)Detalhe.Quantidade];
        private static readonly int[] _detN = new int[(int)Detalhe.Quantidade];
        private static int _mensagens, _bytes, _linhasLog;

        private static double _inicioQuadro = -1;
        private static double _normalMs = 16.7;       // média móvel dos quadros em jogo sem travada
        private static int _gcAnterior;
        private static int _cenaAnterior = -2;
        private static bool _semPersonagemAnterior = true;
        private static bool _logAssinado;

        // Janela do resumo (só quadros em jogo)
        private static float _inicioJanela = -1f, _proximoContexto;
        private static string _contexto;
        private static int _quadros, _travadas, _travadasGc, _travadasMod, _linhasEscritas, _suprimidas, _carregamentos, _coletas;
        private static double _somaQuadro, _piorQuadro, _somaMod, _piorMod;
        private static string _piorModParte;
        private static readonly double[] _somaParte = new double[(int)Parte.Quantidade];
        private static readonly double[] _piorParte = new double[(int)Parte.Quantidade];
        private static readonly double[] _piorDet = new double[(int)Detalhe.Quantidade];
        private static readonly int[] _totalDet = new int[(int)Detalhe.Quantidade];

        internal static double Agora() => Time.realtimeSinceStartupAsDouble;

        /// <summary>Soma o tempo desde <paramref name="inicio"/> na parte e devolve o instante atual (para encadear).</summary>
        internal static double Somar(Parte parte, double inicio)
        {
            double agora = Agora();
            _ms[(int)parte] += (agora - inicio) * 1000.0;
            return agora;
        }

        /// <summary>Início de um gancho (patch dentro do código do jogo); dentro do laço do mod já está contado (-1).</summary>
        internal static double InicioGancho() => NoLaco ? -1.0 : Agora();

        internal static void FimGancho(double inicio)
        {
            if (inicio >= 0) Somar(Parte.Ganchos, inicio);
        }

        internal static void SomarDetalhe(Detalhe d, double inicio)
        {
            _detMs[(int)d] += (Agora() - inicio) * 1000.0;
            _detN[(int)d]++;
        }

        internal static void MensagemRecebida(int bytes)
        {
            _mensagens++;
            _bytes += bytes;
        }

        /// <summary>Começo do Update do mod: fecha o quadro anterior e decide se ele foi uma travada.</summary>
        internal static void NovoQuadro(bool semPersonagem, int cena)
        {
            if (!_logAssinado && Plugin.Log != null)
            {
                _logAssinado = true;
                Plugin.Log.LogEvent += ContarLinha;
            }
            double agora = Agora();
            int gc = GC.CollectionCount(0);
            if (_inicioQuadro < 0)
            {
                _inicioQuadro = agora;
                _gcAnterior = gc;
                Zerar();
                return;
            }
            double quadroMs = (agora - _inicioQuadro) * 1000.0;
            _inicioQuadro = agora;
            int coletas = gc - _gcAnterior;
            _gcAnterior = gc;
            bool carregando = semPersonagem || _semPersonagemAnterior || cena != _cenaAnterior;
            _semPersonagemAnterior = semPersonagem;
            _cenaAnterior = cena;

            if (!carregando) Contabilizar(quadroMs, coletas, cena);
            else if (quadroMs >= LimiteMinimoMs) _carregamentos++;
            Zerar();

            if (!semPersonagem) AtualizarJanela();
            else if (_inicioJanela >= 0f)
            {
                Resumir("voltou ao menu"); // o tempo no menu não entra na conta
                _inicioJanela = -1f;
            }
        }

        /// <summary>Janela perdeu ou ganhou o foco (alt-tab): o quadro em curso não é travada, recomeça a contar.</summary>
        internal static void Recomecar() => _inicioQuadro = -1;

        private static void ContarLinha(object origem, LogEventArgs e) => _linhasLog++;

        private static void Zerar()
        {
            Array.Clear(_ms, 0, _ms.Length);
            Array.Clear(_detMs, 0, _detMs.Length);
            Array.Clear(_detN, 0, _detN.Length);
            _mensagens = _bytes = _linhasLog = 0;
        }

        private static void Contabilizar(double quadroMs, int coletas, int cena)
        {
            if (_inicioJanela < 0f) return; // a janela abre no primeiro quadro em jogo (AtualizarJanela)
            double mod = 0;
            int pior = 0;
            for (int i = 0; i < _ms.Length; i++)
            {
                mod += _ms[i];
                _somaParte[i] += _ms[i];
                if (_ms[i] > _piorParte[i]) _piorParte[i] = _ms[i];
                if (_ms[i] > _ms[pior]) pior = i;
            }
            for (int i = 0; i < _detMs.Length; i++)
            {
                _totalDet[i] += _detN[i];
                if (_detMs[i] > _piorDet[i]) _piorDet[i] = _detMs[i];
            }
            _quadros++;
            _somaQuadro += quadroMs;
            _somaMod += mod;
            _coletas += coletas;
            if (quadroMs > _piorQuadro) _piorQuadro = quadroMs;
            if (mod > _piorMod) { _piorMod = mod; _piorModParte = NomesParte[pior]; }

            bool travada = quadroMs >= LimiteMinimoMs && quadroMs >= FatorTravada * _normalMs;
            if (!travada)
            {
                _normalMs = _normalMs * 0.98 + quadroMs * 0.02;
                return;
            }
            // Devagar também na travada: com FPS baixo o tempo todo, o normal sobe em ~2 s e para de contar cada quadro;
            // uma travada isolada mexe pouco nele.
            _normalMs = _normalMs * 0.995 + quadroMs * 0.005;
            _travadas++;
            if (coletas > 0) _travadasGc++;
            if (mod >= quadroMs * 0.5) _travadasMod++;
            if (_linhasEscritas >= LinhasPorJanela) { _suprimidas++; return; }
            _linhasEscritas++;
            Plugin.Log.LogInfo($"[Desempenho] Travada de {quadroMs:0} ms (normal {_normalMs:0.0} ms) · mod {mod:0.0} ms{DescreverPartes()}" +
                               $" · coleta de lixo: {(coletas > 0 ? "sim (" + coletas + ")" : "não")} · rede: {_mensagens} msg, {_bytes / 1024f:0.0} KB" +
                               $" · log: {_linhasLog} linha(s) · {_contexto} · cena {cena}");
        }

        /// <summary>": Rede 12,3 (criar avatar 11,9 ×1); Efeitos 0,6" — as 3 maiores partes acima de 0,5 ms e os detalhes do quadro.</summary>
        private static string DescreverPartes()
        {
            var ordem = new List<int>();
            for (int i = 0; i < _ms.Length; i++) if (_ms[i] >= 0.5) ordem.Add(i);
            ordem.Sort((a, b) => _ms[b].CompareTo(_ms[a]));
            var pedacos = new List<string>();
            for (int k = 0; k < ordem.Count && k < 3; k++) pedacos.Add($"{NomesParte[ordem[k]]} {_ms[ordem[k]]:0.0}");
            var detalhes = new List<string>();
            for (int i = 0; i < _detMs.Length; i++) if (_detN[i] > 0) detalhes.Add($"{NomesDetalhe[i]} {_detMs[i]:0.0} ×{_detN[i]}");
            string texto = pedacos.Count > 0 ? ": " + string.Join("; ", pedacos.ToArray()) : "";
            if (detalhes.Count > 0) texto += " (" + string.Join(", ", detalhes.ToArray()) + ")";
            return texto;
        }

        /// <summary>Abre a janela no primeiro quadro em jogo; fecha a cada 5 min ou quando o contexto (solo, eco, coop) muda.</summary>
        private static void AtualizarJanela()
        {
            float agora = Time.unscaledTime;
            if (_inicioJanela < 0f)
            {
                _inicioJanela = agora;
                _contexto = Contexto();
                _proximoContexto = agora + 1f;
                return;
            }
            if (agora >= _proximoContexto)
            {
                _proximoContexto = agora + 1f;
                string atual = Contexto();
                if (atual != _contexto)
                {
                    Resumir("contexto mudou para " + atual);
                    _contexto = atual;
                    return;
                }
            }
            if (agora - _inicioJanela >= IntervaloResumo) Resumir(null);
        }

        private static string Contexto()
        {
            CoopRuntime r = CoopRuntime.Instancia;
            if (r == null) return "solo";
            Net.SessaoCoop s = r.Sessao;
            if (s.EmSessao)
            {
                int n = s.Pares.Count;
                return $"{(s.SouHost ? "host" : "convidado")} com {n} aliado(s)";
            }
            return r.EcoAtivo ? "solo com eco" : "solo";
        }

        /// <summary>Escreve o resumo da janela (se tiver tempo suficiente) e abre outra.</summary>
        internal static void Resumir(string motivo)
        {
            float agora = Time.unscaledTime;
            float duracao = _inicioJanela < 0f ? 0f : agora - _inicioJanela;
            if (duracao >= MinimoParaResumo && _quadros > 0)
            {
                double media = _somaQuadro / _quadros;
                var partes = new List<string>();
                for (int i = 0; i < _somaParte.Length; i++)
                {
                    if (_piorParte[i] >= 0.5) partes.Add($"{NomesParte[i]} {_somaParte[i] / _quadros:0.00}/{_piorParte[i]:0.0}");
                }
                var detalhes = new List<string>();
                for (int i = 0; i < _totalDet.Length; i++)
                {
                    if (_totalDet[i] > 0) detalhes.Add($"{NomesDetalhe[i]} {_totalDet[i]}× (pior {_piorDet[i]:0.0} ms)");
                }
                Plugin.Log.LogInfo(
                    $"[Desempenho] Resumo de {duracao / 60f:0.0} min em jogo ({_contexto}{(motivo != null ? "; " + motivo : "")}): " +
                    $"{_quadros} quadros, média {media:0.0} ms ({1000.0 / media:0} fps), pior {_piorQuadro:0} ms; " +
                    $"travadas: {_travadas} (com coleta de lixo {_travadasGc}, mod com metade ou mais do quadro {_travadasMod}" +
                    $"{(_suprimidas > 0 ? ", " + _suprimidas + " fora do log" : "")}); " +
                    $"mod: média {_somaMod / _quadros:0.00} ms/quadro, pior {_piorMod:0.0} ms{(_piorModParte != null ? " (" + _piorModParte + ")" : "")}; " +
                    $"partes (média/pior ms): {(partes.Count > 0 ? string.Join(" · ", partes.ToArray()) : "todas abaixo de 0,5 ms")}; " +
                    $"detalhes: {(detalhes.Count > 0 ? string.Join(" · ", detalhes.ToArray()) : "nenhum")}; " +
                    $"coletas de lixo: {_coletas}; memória gerenciada {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024 * 1024)} MB; " +
                    $"quadros longos em carregamento (fora da conta): {_carregamentos}.");
            }
            _inicioJanela = agora;
            _quadros = _travadas = _travadasGc = _travadasMod = _linhasEscritas = _suprimidas = _carregamentos = _coletas = 0;
            _somaQuadro = _piorQuadro = _somaMod = _piorMod = 0;
            _piorModParte = null;
            Array.Clear(_somaParte, 0, _somaParte.Length);
            Array.Clear(_piorParte, 0, _piorParte.Length);
            Array.Clear(_piorDet, 0, _piorDet.Length);
            Array.Clear(_totalDet, 0, _totalDet.Length);
        }
    }
}
