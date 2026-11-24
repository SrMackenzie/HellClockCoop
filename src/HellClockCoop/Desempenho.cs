using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace HellClockCoop
{
    /// <summary>
    /// Medidor de travadas (0.9.7; ampliado na 0.30.1). Cronometra cada parte do mod a cada quadro; num quadro bem mais
    /// longo que o normal, escreve no log quanto dele foi do mod (e de qual parte), se teve coleta de lixo, rede, chamadas
    /// à Steam, log e carregamento. A cada 5 min em jogo (e ao trocar de solo para coop), um resumo com os números.
    /// Só mede: não muda nada no jogo. O quadro medido vai de um <see cref="NovoQuadro"/> ao seguinte (começo do Update do mod).
    /// </summary>
    internal static class Desempenho
    {
        /// <summary>Partes do mod que não se sobrepõem: a soma é o tempo do mod no quadro.</summary>
        internal enum Parte { Menus, Rede, Run, Inimigos, Objetos, Santuarios, Portas, Alvos, Fx, Danos, Sons, Balanceamento, Personagem, Interface, Ganchos, Gui, Avatares, Medidor, Quantidade }

        /// <summary>Trechos suspeitos medidos por dentro das partes (não entram na soma). Os de gancho ficam por último.</summary>
        internal enum Detalhe { CriarAvatar, CarregarPrefabFx, CopiarFx, BuscarOuvintesEsc, MedirTextoMenu, CriarLinhaMenu, BuscarBotoesJogo, BuscarPecasHud, ChecarSpawn, TimerUpdate, ContextoSom, Quantidade }

        private static readonly string[] NomesParte =
            { "Menus", "Rede", "Run", "Inimigos", "Objetos", "Santuários", "Portas", "Alvos", "Efeitos", "Danos", "Sons", "Balanceamento", "Personagem", "Interface", "Ganchos", "OnGUI", "Avatares", "Medidor" };
        private static readonly string[] NomesDetalhe =
            { "criar avatar", "carregar prefab de efeito", "copiar efeito", "buscar ouvintes do Esc", "medir texto do menu", "criar linha do menu", "buscar botões do jogo",
              "buscar peças da HUD", "CheckForSpawn", "TimerManager.Update", "contexto de som" };

        private const int PrimeiroDetalheDeGancho = (int)Detalhe.ChecarSpawn; // chamados a cada quadro: na linha da travada só com tempo relevante
        private const double LimiteMinimoMs = 50.0;   // abaixo disso não é travada perceptível
        private const double FatorTravada = 2.5;      // e precisa ser bem maior que o quadro normal
        private const float IntervaloResumo = 300f;
        private const float MinimoParaResumo = 30f;   // janela curta demais (troca de contexto logo no início) não vira resumo
        private const int LinhasPorJanela = 40;       // travadas escritas por janela de resumo; o resto só conta
        private const double Mega = 1024.0 * 1024.0;

        /// <summary>Verdadeiro enquanto o laço do mod (Update, LateUpdate, OnGUI) mede as próprias partes.</summary>
        internal static bool NoLaco;

        // Quadro em curso
        private static readonly double[] _ms = new double[(int)Parte.Quantidade];
        private static readonly double[] _detMs = new double[(int)Detalhe.Quantidade];
        private static readonly int[] _detN = new int[(int)Detalhe.Quantidade];
        private static int _mensagens, _bytes, _enviadas, _bytesEnviados, _linhasLog;
        private static bool _bufferCheio;

        private static double _inicioQuadro = -1;
        private static double _normalMs = 16.7;       // média móvel dos quadros em jogo sem travada
        private static int _gcAnterior;
        private static int _cenaAnterior = -2;
        private static bool _semPersonagemAnterior = true;
        private static bool _logAssinado;
        private static bool _steamSemContador;        // a leitura das chamadas à Steam falhou uma vez: não tenta de novo

        // Janela do resumo (só quadros em jogo, salvo a rede e o piso de memória, que contam também no carregamento de andar)
        private static float _inicioJanela = -1f, _proximoContexto;
        private static string _contexto;
        private static int _quadros, _travadas, _travadasGc, _travadasMod, _linhasEscritas, _suprimidas, _carregamentos, _coletas;
        private static double _somaQuadro, _piorQuadro, _somaMod, _piorMod, _piorModCarregando;
        private static string _piorModParte, _piorModCarregandoParte;
        private static readonly double[] _somaParte = new double[(int)Parte.Quantidade];
        private static readonly double[] _piorParte = new double[(int)Parte.Quantidade];
        private static readonly double[] _somaDet = new double[(int)Detalhe.Quantidade];
        private static readonly double[] _piorDet = new double[(int)Detalhe.Quantidade];
        private static readonly int[] _totalDet = new int[(int)Detalhe.Quantidade];
        private static int _quadrosSteam, _piorSteam;
        private static long _somaSteam;
        private static int _pisos;
        private static long _pisoMinimo, _pisoMaximo, _pisoUltimo;
        private static readonly int[] _enviadasTipo = new int[256];
        private static readonly long[] _bytesTipo = new long[256];
        private static int _recebidasJanela, _quadrosBufferCheio;
        private static long _bytesRecebidosJanela;
        private static string[] _nomesTipo;

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

        /// <summary>
        /// Fim de um gancho chamado muitas vezes: soma em Ganchos e conta as chamadas e o tempo no detalhe. Um gancho
        /// disparado de dentro de outro (o som de um inimigo que o CheckForSpawn acorda) soma o próprio tempo duas vezes
        /// em Ganchos: é raro e de microssegundos, e não vale um estado a mais para evitar.
        /// </summary>
        internal static void FimGancho(double inicio, Detalhe d)
        {
            if (inicio < 0) return;
            double ms = (Agora() - inicio) * 1000.0;
            _ms[(int)Parte.Ganchos] += ms;
            _detMs[(int)d] += ms;
            _detN[(int)d]++;
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
            if (_inicioJanela < 0f) return;
            _recebidasJanela++;
            _bytesRecebidosJanela += bytes;
        }

        /// <summary>Uma mensagem entregue à Steam para um par (cada destinatário conta uma vez).</summary>
        internal static void MensagemEnviada(byte tipo, int bytes)
        {
            _enviadas++;
            _bytesEnviados += bytes;
            if (_inicioJanela < 0f) return;
            _enviadasTipo[tipo]++;
            _bytesTipo[tipo] += bytes;
        }

        /// <summary>A leitura de um canal chegou ao limite do buffer de recepção: pode ter ficado mensagem para o quadro seguinte.</summary>
        internal static void BufferCheio() => _bufferCheio = true;

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
            int steam = ChamadasSteam(); // lido a cada quadro: o contador zera a cada leitura
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
            // Cena -1: o andar ainda está carregando (o personagem já existe). Sem isso, cada quadro longo do carregamento
            // depois do primeiro virava "travada" (58 das 69 linhas do log de 23/11/2026).
            bool carregando = semPersonagem || _semPersonagemAnterior || cena == -1 || cena != _cenaAnterior;
            _semPersonagemAnterior = semPersonagem;
            _cenaAnterior = cena;

            if (_inicioJanela >= 0f) // com a janela fechada (menu, carregamento do save) nada disto conta
            {
                if (coletas > 0) AnotarPiso();
                if (_bufferCheio) _quadrosBufferCheio++;
                // Os detalhes contam também no carregamento de andar: a busca das peças da HUD roda logo depois da troca de cena.
                for (int i = 0; i < _detMs.Length; i++)
                {
                    _totalDet[i] += _detN[i];
                    _somaDet[i] += _detMs[i];
                    if (_detMs[i] > _piorDet[i]) _piorDet[i] = _detMs[i];
                }
                if (carregando && quadroMs >= LimiteMinimoMs)
                {
                    _carregamentos++;
                    double mod = 0;
                    int pior = 0;
                    for (int i = 0; i < _ms.Length; i++)
                    {
                        mod += _ms[i];
                        if (_ms[i] > _ms[pior]) pior = i;
                    }
                    if (mod > _piorModCarregando) { _piorModCarregando = mod; _piorModCarregandoParte = NomesParte[pior]; }
                }
            }
            if (!carregando) Contabilizar(quadroMs, coletas, cena, steam);
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

        /// <summary>
        /// Chamadas à Steam (IPC com o cliente) desde a leitura anterior, do jogo e do mod juntos; -1 sem a Steam pronta.
        /// SteamUtils.GetIPCCallCount devolve a contagem desde a última vez em que foi chamado.
        /// </summary>
        private static int ChamadasSteam()
        {
            if (_steamSemContador) return -1;
            try
            {
                CoopRuntime r = CoopRuntime.Instancia;
                if (r == null || r.Sessao.EstadoAtual == Net.SessaoCoop.Estado.AguardandoSteam) return -1;
                return (int)Steamworks.SteamUtils.GetIPCCallCount();
            }
            catch (Exception e)
            {
                _steamSemContador = true;
                Plugin.Log.LogWarning($"[Desempenho] Sem a contagem de chamadas à Steam (o resto do medidor segue): {e.Message}");
                return -1;
            }
        }

        /// <summary>Memória gerenciada no primeiro quadro depois de uma coleta de lixo: o piso (o que sobrou vivo).</summary>
        private static void AnotarPiso()
        {
            long usado = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            if (_pisos == 0 || usado < _pisoMinimo) _pisoMinimo = usado;
            if (_pisos == 0 || usado > _pisoMaximo) _pisoMaximo = usado;
            _pisoUltimo = usado;
            _pisos++;
        }

        private static void Zerar()
        {
            Array.Clear(_ms, 0, _ms.Length);
            Array.Clear(_detMs, 0, _detMs.Length);
            Array.Clear(_detN, 0, _detN.Length);
            _mensagens = _bytes = _enviadas = _bytesEnviados = _linhasLog = 0;
            _bufferCheio = false;
        }

        private static void Contabilizar(double quadroMs, int coletas, int cena, int steam)
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
            _quadros++;
            _somaQuadro += quadroMs;
            _somaMod += mod;
            _coletas += coletas;
            if (quadroMs > _piorQuadro) _piorQuadro = quadroMs;
            if (mod > _piorMod) { _piorMod = mod; _piorModParte = NomesParte[pior]; }
            if (steam >= 0)
            {
                _quadrosSteam++;
                _somaSteam += steam;
                if (steam > _piorSteam) _piorSteam = steam;
            }

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
                               $" · coleta de lixo: {(coletas > 0 ? "sim (" + coletas + ")" : "não")}" +
                               $" · rede: recebeu {_mensagens} msg, {_bytes / 1024f:0.0} KB; enviou {_enviadas} msg, {_bytesEnviados / 1024f:0.0} KB{(_bufferCheio ? "; buffer de recepção cheio" : "")}" +
                               $"{(steam >= 0 ? " · Steam: " + steam + " chamada(s)" : "")}" +
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
            for (int i = 0; i < _detMs.Length; i++)
            {
                if (_detN[i] > 0 && (i < PrimeiroDetalheDeGancho || _detMs[i] >= 0.5)) detalhes.Add($"{NomesDetalhe[i]} {_detMs[i]:0.0} ×{_detN[i]}");
            }
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
                return $"{(s.SouHost ? "host" : "convidado")} com {n} aliado(s){(r.EcoAtivo ? " e eco" : "")}"; // o eco muda a carga: janela própria
            }
            return r.EcoAtivo ? "solo com eco" : "solo";
        }

        /// <summary>Nome da mensagem pelo 1º byte do pacote, lido das constantes Tipo* do Protocolo (uma vez).</summary>
        private static string NomeDoTipo(int tipo)
        {
            if (_nomesTipo == null)
            {
                var nomes = new string[256];
                try
                {
                    foreach (FieldInfo f in typeof(Net.Protocolo).GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (f.IsLiteral && f.FieldType == typeof(byte) && f.Name.StartsWith("Tipo", StringComparison.Ordinal))
                            nomes[(byte)f.GetValue(null)] = f.Name.Substring(4);
                    }
                }
                catch (Exception)
                {
                    // sem os nomes, o resumo sai com o número do tipo
                }
                _nomesTipo = nomes;
            }
            return _nomesTipo[tipo] ?? "tipo " + tipo;
        }

        /// <summary>"Rede no período": o que foi enviado por tipo de mensagem (maiores primeiro), o recebido e o buffer cheio.</summary>
        private static void ResumirRede(float duracao)
        {
            var tipos = new List<int>();
            long mensagens = 0, bytes = 0;
            for (int i = 0; i < _enviadasTipo.Length; i++)
            {
                if (_enviadasTipo[i] == 0) continue;
                tipos.Add(i);
                mensagens += _enviadasTipo[i];
                bytes += _bytesTipo[i];
            }
            if (mensagens == 0 && _recebidasJanela == 0 && _quadrosBufferCheio == 0) return; // solo: sem linha de rede
            tipos.Sort((a, b) => _bytesTipo[b].CompareTo(_bytesTipo[a]));
            var pedacos = new List<string>();
            foreach (int t in tipos) pedacos.Add($"{NomeDoTipo(t)} {_enviadasTipo[t]} msg/{_bytesTipo[t] / 1024.0:0.0} KB");
            Plugin.Log.LogInfo(
                $"[Desempenho] Rede no período ({_contexto}): enviou {mensagens} msg, {bytes / 1024.0:0.0} KB ({bytes / 1024.0 / duracao:0.0} KB/s)" +
                $"{(pedacos.Count > 0 ? ": " + string.Join(" · ", pedacos.ToArray()) : "")}; " +
                $"recebeu {_recebidasJanela} msg, {_bytesRecebidosJanela / 1024.0:0.0} KB ({_bytesRecebidosJanela / 1024.0 / duracao:0.0} KB/s); " +
                $"buffer de recepção (64 msg) cheio em {_quadrosBufferCheio} quadro(s).");
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
                    if (_totalDet[i] > 0) detalhes.Add($"{NomesDetalhe[i]} {_totalDet[i]}× (total {_somaDet[i]:0.0} ms, pior quadro {_piorDet[i]:0.0} ms)");
                }
                Plugin.Log.LogInfo(
                    $"[Desempenho] Resumo de {duracao / 60f:0.0} min em jogo ({_contexto}{(motivo != null ? "; " + motivo : "")}): " +
                    $"{_quadros} quadros, média {media:0.0} ms ({1000.0 / media:0} fps), pior {_piorQuadro:0} ms; " +
                    $"travadas: {_travadas} (com coleta de lixo {_travadasGc}, mod com metade ou mais do quadro {_travadasMod}" +
                    $"{(_suprimidas > 0 ? ", " + _suprimidas + " fora do log" : "")}); " +
                    $"mod: média {_somaMod / _quadros:0.00} ms/quadro, pior {_piorMod:0.0} ms{(_piorModParte != null ? " (" + _piorModParte + ")" : "")}; " +
                    $"partes (média/pior ms): {(partes.Count > 0 ? string.Join(" · ", partes.ToArray()) : "todas abaixo de 0,5 ms")}; " +
                    $"detalhes: {(detalhes.Count > 0 ? string.Join(" · ", detalhes.ToArray()) : "nenhum")}; " +
                    $"chamadas à Steam por quadro (jogo e mod): {(_quadrosSteam > 0 ? $"média {(double)_somaSteam / _quadrosSteam:0.0}, pior {_piorSteam}" : "sem leitura")}; " +
                    $"coletas de lixo: {_coletas}; memória gerenciada {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / Mega:0.0} MB agora, " +
                    $"piso depois de coleta: {(_pisos > 0 ? $"mínimo {_pisoMinimo / Mega:0.0}, máximo {_pisoMaximo / Mega:0.0}, último {_pisoUltimo / Mega:0.0} MB em {_pisos} leitura(s)" : "sem coleta no período")}; " +
                    $"quadros longos em carregamento (fora da conta): {_carregamentos}{(_carregamentos > 0 ? $" (mod no pior: {_piorModCarregando:0.0} ms, {_piorModCarregandoParte})" : "")}.");
                ResumirRede(duracao);
            }
            _inicioJanela = agora;
            _quadros = _travadas = _travadasGc = _travadasMod = _linhasEscritas = _suprimidas = _carregamentos = _coletas = 0;
            _somaQuadro = _piorQuadro = _somaMod = _piorMod = _piorModCarregando = 0;
            _piorModParte = _piorModCarregandoParte = null;
            _quadrosSteam = _piorSteam = _pisos = _recebidasJanela = _quadrosBufferCheio = 0;
            _somaSteam = _pisoMinimo = _pisoMaximo = _pisoUltimo = _bytesRecebidosJanela = 0;
            Array.Clear(_somaParte, 0, _somaParte.Length);
            Array.Clear(_piorParte, 0, _piorParte.Length);
            Array.Clear(_somaDet, 0, _somaDet.Length);
            Array.Clear(_piorDet, 0, _piorDet.Length);
            Array.Clear(_totalDet, 0, _totalDet.Length);
            Array.Clear(_enviadasTipo, 0, _enviadasTipo.Length);
            Array.Clear(_bytesTipo, 0, _bytesTipo.Length);
        }
    }
}
