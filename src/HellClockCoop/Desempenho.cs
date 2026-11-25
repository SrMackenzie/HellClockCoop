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

        /// <summary>Trechos suspeitos medidos por dentro das partes (não entram na soma). Os chamados o tempo todo (ganchos e o estado dos inimigos) ficam por último.</summary>
        internal enum Detalhe { CriarAvatar, CarregarPrefabFx, CopiarFx, MedirTextoMenu, CriarLinhaMenu, BuscarBotoesJogo, BuscarPecasHud, ChecarSpawn, TimerUpdate, ContextoSom, MontarEstado, Quantidade }

        private static readonly string[] NomesParte =
            { "Menus", "Rede", "Run", "Inimigos", "Objetos", "Santuários", "Portas", "Alvos", "Efeitos", "Danos", "Sons", "Balanceamento", "Personagem", "Interface", "Ganchos", "OnGUI", "Avatares", "Medidor" };
        private static readonly string[] NomesDetalhe =
            { "criar avatar", "carregar prefab de efeito", "copiar efeito", "medir texto do menu", "criar linha do menu", "buscar botões do jogo",
              "buscar peças da HUD", "CheckForSpawn", "TimerManager.Update", "contexto de som", "montar estado dos inimigos" };

        /// <summary>
        /// O que chega dos aliados e vira trabalho por item (0.31.1): contado e cronometrado na aplicação, com o que o teto
        /// por quadro segurou e o que foi descartado (por atraso ou fila cheia). O que o eco repete direto (sem passar pela recepção) não conta.
        /// </summary>
        internal enum Cosmetico { Numero, Golpe, Som, CopiaFx, Quantidade }

        private static readonly string[] NomesCosmetico = { "números de dano", "golpes vistos", "sons", "cópias de efeito" };

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
        private static readonly int[] _cosN = new int[(int)Cosmetico.Quantidade];
        private static readonly double[] _cosMs = new double[(int)Cosmetico.Quantidade];
        private static readonly bool[] _cosNoTeto = new bool[(int)Cosmetico.Quantidade];

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
        private static int _enviosEstado, _pacotesEstado;
        private static long _relevantesEstado, _itensEstado, _completosEstado, _comAnimacaoEstado, _bytesEstado;
        private static bool _estadoSimulado;
        private static string[] _nomesTipo;
        // Recepção (0.31.1): por tipo de mensagem (1º byte) e por item aplicado
        private static readonly int[] _recebidasTipo = new int[256];
        private static readonly long[] _bytesRecebidosTipo = new long[256];
        private static readonly double[] _msTipo = new double[256];
        private static readonly double[] _piorMsTipo = new double[256];
        private static readonly int[] _cosTotal = new int[(int)Cosmetico.Quantidade];
        private static readonly int[] _cosPiorN = new int[(int)Cosmetico.Quantidade];
        private static readonly int[] _cosDescartados = new int[(int)Cosmetico.Quantidade];
        private static readonly int[] _cosQuadrosNoTeto = new int[(int)Cosmetico.Quantidade];
        private static readonly double[] _cosSomaMs = new double[(int)Cosmetico.Quantidade];
        private static readonly double[] _cosPiorMs = new double[(int)Cosmetico.Quantidade];
        private static int _aliadosSimulados;

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

        internal static void MensagemRecebida(byte tipo, int bytes)
        {
            _mensagens++;
            _bytes += bytes;
            if (_inicioJanela < 0f) return;
            _recebidasJanela++;
            _bytesRecebidosJanela += bytes;
            _recebidasTipo[tipo]++;
            _bytesRecebidosTipo[tipo] += bytes;
        }

        /// <summary>Tempo do tratador de uma mensagem recebida (o que ele faz na hora; o que vai para uma fila conta em <see cref="Aplicou"/>).</summary>
        internal static void MensagemTratada(byte tipo, double inicio)
        {
            if (_inicioJanela < 0f) return;
            double ms = (Agora() - inicio) * 1000.0;
            _msTipo[tipo] += ms;
            if (ms > _piorMsTipo[tipo]) _piorMsTipo[tipo] = ms;
        }

        /// <summary>Um item de aliado aplicado no jogo: um número de dano, um golpe visto, um som, uma cópia de efeito.</summary>
        internal static void Aplicou(Cosmetico c, double inicio)
        {
            _cosN[(int)c]++;
            _cosMs[(int)c] += (Agora() - inicio) * 1000.0;
        }

        /// <summary>Itens que esperaram mais que o atraso máximo (ou não couberam na fila) e não foram aplicados.</summary>
        internal static void Descartou(Cosmetico c, int quantos)
        {
            if (_inicioJanela >= 0f) _cosDescartados[(int)c] += quantos;
        }

        /// <summary>O teto por quadro segurou itens deste tipo para o quadro seguinte.</summary>
        internal static void NoTeto(Cosmetico c) => _cosNoTeto[(int)c] = true;

        /// <summary>A recepção simulada entregou pacotes nesta janela (sala vazia, com o eco valendo por <paramref name="aliados"/> aliados).</summary>
        internal static void RecepcaoSimulada(int aliados) => _aliadosSimulados = aliados;

        /// <summary>Uma mensagem entregue à Steam para um par (cada destinatário conta uma vez).</summary>
        internal static void MensagemEnviada(byte tipo, int bytes)
        {
            _enviadas++;
            _bytesEnviados += bytes;
            if (_inicioJanela < 0f) return;
            _enviadasTipo[tipo]++;
            _bytesTipo[tipo] += bytes;
        }

        /// <summary>
        /// Um pacote de estado dos inimigos, contado uma vez (é o mesmo para cada convidado). <paramref name="simulado"/>:
        /// montado e não enviado, com o host sozinho na sala e o eco ligado (o eco vale como convidado), o que dá o
        /// tráfego por convidado sem um segundo jogador.
        /// </summary>
        internal static void PacoteDeEstado(int bytes, bool simulado)
        {
            if (_inicioJanela < 0f) return;
            _pacotesEstado++;
            _bytesEstado += bytes;
            _estadoSimulado |= simulado;
        }

        /// <summary>
        /// Um envio de estado dos inimigos (10 por segundo): quantos estavam perto de um aliado, quantos entraram no pacote
        /// (os outros não tinham mudança), quantos desses foram completos e quantos levaram a animação.
        /// </summary>
        internal static void EstadoDeInimigos(int relevantes, int itens, int completos, int comAnimacao)
        {
            if (_inicioJanela < 0f) return;
            _enviosEstado++;
            _relevantesEstado += relevantes;
            _itensEstado += itens;
            _completosEstado += completos;
            _comAnimacaoEstado += comAnimacao;
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
                for (int i = 0; i < _cosN.Length; i++)
                {
                    _cosTotal[i] += _cosN[i];
                    _cosSomaMs[i] += _cosMs[i];
                    if (_cosN[i] > _cosPiorN[i]) _cosPiorN[i] = _cosN[i];
                    if (_cosMs[i] > _cosPiorMs[i]) _cosPiorMs[i] = _cosMs[i];
                    if (_cosNoTeto[i]) _cosQuadrosNoTeto[i]++;
                }
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
            Array.Clear(_cosN, 0, _cosN.Length);
            Array.Clear(_cosMs, 0, _cosMs.Length);
            Array.Clear(_cosNoTeto, 0, _cosNoTeto.Length);
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
                               $"{DescreverAliados()}{(steam >= 0 ? " · Steam: " + steam + " chamada(s)" : "")}" +
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

        /// <summary>" · dos aliados: 40 números de dano (1,2 ms), 3 cópias de efeito (2,1 ms)" — o que foi aplicado no quadro da travada.</summary>
        private static string DescreverAliados()
        {
            List<string> pedacos = null;
            for (int i = 0; i < _cosN.Length; i++)
            {
                if (_cosN[i] > 0) (pedacos ?? (pedacos = new List<string>())).Add($"{_cosN[i]} {NomesCosmetico[i]} ({_cosMs[i]:0.0} ms)");
            }
            return pedacos == null ? "" : " · dos aliados: " + string.Join(", ", pedacos.ToArray());
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
            if (mensagens == 0 && _recebidasJanela == 0 && _quadrosBufferCheio == 0 && _enviosEstado == 0) return; // solo: sem linha de rede
            tipos.Sort((a, b) => _bytesTipo[b].CompareTo(_bytesTipo[a]));
            var pedacos = new List<string>();
            foreach (int t in tipos) pedacos.Add($"{NomeDoTipo(t)} {_enviadasTipo[t]} msg/{_bytesTipo[t] / 1024.0:0.0} KB");
            Plugin.Log.LogInfo(
                $"[Desempenho] Rede no período ({_contexto}): enviou {mensagens} msg, {bytes / 1024.0:0.0} KB ({bytes / 1024.0 / duracao:0.0} KB/s)" +
                $"{(pedacos.Count > 0 ? ": " + string.Join(" · ", pedacos.ToArray()) : "")}; " +
                $"recebeu {_recebidasJanela} msg, {_bytesRecebidosJanela / 1024.0:0.0} KB ({_bytesRecebidosJanela / 1024.0 / duracao:0.0} KB/s); " +
                $"buffer de recepção (64 msg) cheio em {_quadrosBufferCheio} quadro(s)" +
                (_enviosEstado > 0
                    ? $"; estado dos inimigos{(_estadoSimulado ? " (simulado: eco como convidado, montado e não enviado)" : "")}: {_enviosEstado} envio(s); " +
                      $"por envio, {(double)_relevantesEstado / _enviosEstado:0.0} inimigo(s) perto de aliado, {(double)_itensEstado / _enviosEstado:0.0} no pacote " +
                      $"({(double)_completosEstado / _enviosEstado:0.0} completo(s), {(double)_comAnimacaoEstado / _enviosEstado:0.0} com animação), " +
                      $"{(double)_pacotesEstado / _enviosEstado:0.00} pacote(s) e {(double)_bytesEstado / _enviosEstado:0} bytes por convidado " +
                      $"({_bytesEstado * 10.0 / _enviosEstado / 1024.0:0.0} KB/s a 10 envios por segundo)"
                    : "") + ".");
        }

        /// <summary>
        /// "Recepção no período" (0.31.1): o que chegou por tipo de mensagem, com o tempo do tratador, e o que foi aplicado
        /// dos aliados por item (números de dano, golpes vistos, sons, cópias de efeito), com o pior quadro, o que o teto
        /// por quadro segurou e o que foi descartado (por atraso ou fila cheia). Sozinho só sai na recepção simulada (sala vazia com o eco).
        /// </summary>
        private static void ResumirRecepcao()
        {
            var tipos = new List<int>();
            for (int i = 0; i < _recebidasTipo.Length; i++) if (_recebidasTipo[i] > 0) tipos.Add(i);
            tipos.Sort((a, b) => _msTipo[b].CompareTo(_msTipo[a]));
            var porTipo = new List<string>();
            foreach (int t in tipos)
                porTipo.Add($"{NomeDoTipo(t)} {_recebidasTipo[t]} msg/{_bytesRecebidosTipo[t] / 1024.0:0.0} KB/{_msTipo[t]:0.0} ms (pior {_piorMsTipo[t]:0.00})");
            var itens = new List<string>();
            for (int i = 0; i < _cosTotal.Length; i++)
            {
                if (_cosTotal[i] == 0 && _cosDescartados[i] == 0) continue;
                itens.Add($"{NomesCosmetico[i]} {_cosTotal[i]} ({_cosSomaMs[i]:0.0} ms no total, {(_cosTotal[i] > 0 ? _cosSomaMs[i] * 1000.0 / _cosTotal[i] : 0.0):0} µs cada; " +
                          $"pior quadro {_cosPiorN[i]} e {_cosPiorMs[i]:0.0} ms; no teto em {_cosQuadrosNoTeto[i]} quadro(s); {_cosDescartados[i]} descartado(s) por atraso ou fila cheia)");
            }
            if (porTipo.Count == 0 && itens.Count == 0) return;
            Plugin.Log.LogInfo(
                $"[Desempenho] Recepção no período ({_contexto}{(_aliadosSimulados > 0 ? $"; simulada: os pacotes deste PC voltam como se viessem de {_aliadosSimulados} aliado(s)" : "")}): " +
                $"por tipo (mensagens/KB/tempo do tratador): {(porTipo.Count > 0 ? string.Join(" · ", porTipo.ToArray()) : "nada recebido")}; " +
                $"aplicado dos aliados: {(itens.Count > 0 ? string.Join(" · ", itens.ToArray()) : "nada")}.");
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
                ResumirRecepcao();
            }
            _inicioJanela = agora;
            _quadros = _travadas = _travadasGc = _travadasMod = _linhasEscritas = _suprimidas = _carregamentos = _coletas = 0;
            _somaQuadro = _piorQuadro = _somaMod = _piorMod = _piorModCarregando = 0;
            _piorModParte = _piorModCarregandoParte = null;
            _quadrosSteam = _piorSteam = _pisos = _recebidasJanela = _quadrosBufferCheio = _enviosEstado = _pacotesEstado = 0;
            _somaSteam = _pisoMinimo = _pisoMaximo = _pisoUltimo = _bytesRecebidosJanela = 0;
            _relevantesEstado = _itensEstado = _completosEstado = _comAnimacaoEstado = _bytesEstado = 0;
            _estadoSimulado = false;
            Array.Clear(_somaParte, 0, _somaParte.Length);
            Array.Clear(_piorParte, 0, _piorParte.Length);
            Array.Clear(_somaDet, 0, _somaDet.Length);
            Array.Clear(_piorDet, 0, _piorDet.Length);
            Array.Clear(_totalDet, 0, _totalDet.Length);
            Array.Clear(_enviadasTipo, 0, _enviadasTipo.Length);
            Array.Clear(_bytesTipo, 0, _bytesTipo.Length);
            Array.Clear(_recebidasTipo, 0, _recebidasTipo.Length);
            Array.Clear(_bytesRecebidosTipo, 0, _bytesRecebidosTipo.Length);
            Array.Clear(_msTipo, 0, _msTipo.Length);
            Array.Clear(_piorMsTipo, 0, _piorMsTipo.Length);
            Array.Clear(_cosTotal, 0, _cosTotal.Length);
            Array.Clear(_cosPiorN, 0, _cosPiorN.Length);
            Array.Clear(_cosDescartados, 0, _cosDescartados.Length);
            Array.Clear(_cosQuadrosNoTeto, 0, _cosQuadrosNoTeto.Length);
            Array.Clear(_cosSomaMs, 0, _cosSomaMs.Length);
            Array.Clear(_cosPiorMs, 0, _cosPiorMs.Length);
            _aliadosSimulados = 0;
        }
    }
}
