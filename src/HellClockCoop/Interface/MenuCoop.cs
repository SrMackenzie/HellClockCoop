using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HellClockCoop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Menu do coop. Desde a 0.12.0 é desenhado com a interface do próprio jogo (PainelNativo: janela central com as peças das Opções
    /// do jogo, com as abas Sessão, Salas (0.18.0: salas públicas do mundo todo), Ajustes e, desde a 0.13.0, Novidades); se a skin do
    /// jogo não puder ser montada, volta à gaveta
    /// desenhada no OnGUI (lista única, como na 0.9.3) e a Novidades à janela própria.
    /// - Abre e fecha pela tecla [Teclas] MenuDoCoop (F7), pelo botão View/Select do controle ou pelo botão "Coop" do menu do
    ///   jogo e da pausa (BotoesDoJogo, 0.13.0); fecha também por Esc, B e pelo [X]. A tecla da Novidades (F10) abre na aba
    ///   Novidades; aberta sozinha depois de atualizar, Esc, B e o [X] só fecham depois de 1,5 s.
    /// - Sessão: recado do atualizador, estado, ALIADOS (o host expulsa com confirmação), COOP (sala pública, criar e convidar, sair com
    ///   confirmação, modo eco). Salas: atualizar a lista e entrar numa sala pública.
    ///   Ajustes: nomes, números e tamanho do dano dos aliados, sons e volume dos aliados (gravados no .cfg do mod,
    ///   nunca nas opções do jogo) e balanceamento deste .cfg só para leitura. Novidades: a versão atual e as 3 anteriores.
    /// - Aberto, segura a entrada do jogo (BloqueioEntrada): mouse, teclado e controle operam o menu sem mexer no
    ///   personagem nem abrir a pausa. Em jogo não escurece a tela nem pausa: o jogo continua rodando.
    /// - Se o semáforo do jogo não travar, em jogo o menu responde só a teclado e controle (o clique moveria o personagem).
    /// </summary>
    internal static class MenuCoop
    {
        /// <summary>
        /// Ações do menu. As salas e o Expulsar são faixas: Sala0 + i é a sala i da lista (até 50, o limite da busca) e
        /// Expulsar0 + i o aliado i da seção Aliados (até 3).
        /// </summary>
        internal enum Acao
        {
            Nenhuma, CriarOuConvidar, Sair, Eco, Nomes, Vida, Seta, Dano, TamanhoDano, Sons, Volume, Novidades,
            SalaPublica, AtualizarSalas, NomeSala, OpacidadeDano, Sala0 = 100, Expulsar0 = 200, Desbloquear0 = 300,
        }
        internal enum Tipo { Secao, Info, Item, Interruptor, Deslizante }
        internal enum Aba { Sessao, Salas, Ajustes, Novidades }
        internal enum Enfase { Normal, Titulo, Rotulo } // cor da linha de informação (aba Novidades)

        internal struct Linha
        {
            public Tipo Tipo;
            public Acao Acao;
            public string Texto, Valor;
            public string Detalhe; // 0.21.0: segunda linha do item (aba Salas, visual A: lugar e motivo de indisponível)
            public bool Habilitada, Ligado;
            public float Fracao; // deslizante: posição 0..1
            public Enfase Enfase;
        }

        internal static readonly string[] NomesDasAbas = { "Sessão", "Salas", "Ajustes", "Novidades" };
        private const int SalasNoMenu = 50, AliadosNoMenu = SessaoCoop.MaximoJogadores - 1, BloqueadosNoMenu = 50;

        /// <summary>Índice da faixa (Sala0 + i, Expulsar0 + i) ou -1 se a ação não é dela.</summary>
        internal static int IndiceNaFaixa(Acao acao, Acao inicio, int tamanho)
        {
            int i = (int)acao - (int)inicio;
            return i >= 0 && i < tamanho ? i : -1;
        }

        private const float LarguraMin = 380f, LarguraMax = 720f; // 0.21.0: a lista de salas usa mais tela
        private const float Margem = 20f, AlturaItem = 48f, AlturaSecao = 34f, LadoBotao = 44f, LarguraValor = 120f, AlturaDetalhe = 22f;
        private const float EsperaConfirmarSaida = 3f;
        private const float PassoRoda = 60f; // unidades de 1080p por entalhe da roda do mouse

        private static readonly Color Ferrugem = new Color(0.55f, 0.14f, 0.1f, 1f);
        private static readonly Color Creme = new Color(0.96f, 0.9f, 0.82f);

        internal static ConfigEntry<string> Tecla;

        private const float EsperaFecharAutomatica = 1.5f; // Novidades aberta sozinha: Esc/B/[X] de quem pula a abertura não a fecham

        private static bool _aberto, _semPersonagem, _controle, _rolarParaFoco, _nativo, _mouseIniciado, _novidadesVistas;
        private static Aba _aba;
        private static Acao _foco, _arrastando;
        private static float _rolagem, _confirmarSairAte = -1f, _fecharLiberadoEm;
        private static List<Linha> _linhasNovidades; // texto fixo da DLL: montado uma vez
        private static float _alturaItem = AlturaItem, _ladoX = LadoBotao; // em 1080p; crescem em tela menor (alvo real ≥ 44 px)
        private static Vector2 _mouseAntes, _mouseAntesNativo;
        private static readonly List<Linha> Linhas = new List<Linha>();
        // Expulsar0 + i → AliadosMostrados[i] (do último Montar). Criada no primeiro Montar: no inicializador estático, o tipo
        // Par puxaria a Steamworks, que o tools/codectest não carrega.
        private static List<Par> AliadosMostrados;
        private static ulong _entrarAlvo; // entrar noutra sala já num coop: dois apertos, armado pelo lobby
        private static float _confirmarEntrarAte = -1f;
        private static ulong _expulsarAlvo; // Expulsar pede dois apertos, como o Sair; armado pelo SteamID (não pelo lugar na lista)
        private static readonly List<SalaListada> SalasMostradas = new List<SalaListada>(); // Sala0 + i → a sala desenhada, não a da busca nova
        private static float _confirmarExpulsarAte = -1f;
        // 0.23.0: Bloquear é a ação secundária (X, Delete ou clique direito) da linha do aliado e da sala; dois apertos, armado
        // pelo SteamID. _teclaBloquear é o que o jogador usou, para o texto do segundo aperto.
        private static ulong _bloquearAlvo;
        private static float _confirmarBloquearAte = -1f;
        private static string _teclaBloquear = "X";
        private static ulong _desbloquearAlvo; // Desbloquear (aba Ajustes): dois apertos, armado pelo SteamID
        private static float _confirmarDesbloquearAte = -1f;
        private static GUIStyle _titulo, _secao, _info, _item, _valor, _detalhe, _rodape, _botao, _invisivel;

        internal static bool Aberto => _aberto;

        internal static void Configurar(ConfigFile config)
        {
            Tecla = config.Bind("Teclas", "MenuDoCoop", "F7",
                "Abre e fecha o menu do coop (criar, convidar, sair, modo eco, ajustes). No controle: View/Select. Nomes do Input System da Unity: F1..F12, Insert, Home...");
        }

        // ---------- Regras puras (testadas no tools/codectest) ----------

        /// <summary>Próximo item selecionável na direção de delta, girando nas pontas; sem foco válido, começa da ponta. -1 se não há nenhum.</summary>
        internal static int ProximoFoco(int atual, int delta, bool[] selecionavel)
        {
            int n = selecionavel == null ? 0 : selecionavel.Length;
            if (n == 0) return -1;
            int passo = delta < 0 ? -1 : 1;
            int i = atual >= 0 && atual < n ? atual : (passo > 0 ? -1 : n);
            for (int k = 0; k < n; k++)
            {
                i = (((i + passo) % n) + n) % n;
                if (selecionavel[i]) return i;
            }
            return -1;
        }

        /// <summary>"Sair do coop" pede dois apertos: o primeiro arma até armadoAte; o segundo, dentro do prazo, sai.</summary>
        internal static bool ConfirmaSaida(float agora, float armadoAte) => armadoAte >= 0f && agora <= armadoAte;

        /// <summary>Aba vizinha na direção de delta, girando nas pontas (LB/RB e setas laterais).</summary>
        internal static int ProximaAba(int atual, int delta, int total)
        {
            if (total <= 0) return 0;
            int passo = delta < 0 ? -1 : 1;
            return (((atual + passo) % total) + total) % total;
        }

        /// <summary>Valor do deslizante um passo para o lado de delta, alinhado ao passo e preso entre min e max.</summary>
        internal static float AjustarNumero(float valor, int delta, float passo, float min, float max)
        {
            if (float.IsNaN(valor)) valor = min; // NaN no .cfg
            float alvo = valor + (delta < 0 ? -passo : delta > 0 ? passo : 0f);
            return Alinhar(alvo, passo, min, max);
        }

        /// <summary>Valor do deslizante pela posição do mouse (0 = início, 1 = fim da barra), alinhado ao passo.</summary>
        internal static float NumeroPelaFracao(float fracao, float passo, float min, float max)
        {
            if (float.IsNaN(fracao)) fracao = 0f;
            fracao = fracao < 0f ? 0f : fracao > 1f ? 1f : fracao;
            return Alinhar(min + fracao * (max - min), passo, min, max);
        }

        private static float Alinhar(float valor, float passo, float min, float max)
        {
            if (passo > 0f) valor = (float)(Math.Round(valor / passo) * passo);
            valor = (float)Math.Round(valor, 4); // sem 0,30000001 no .cfg
            return valor < min ? min : valor > max ? max : valor;
        }

        // ---------- Entrada (no Update do runtime, depois da Novidades) ----------

        internal static void Atualizar(CoopRuntime rt, bool semPersonagem)
        {
            _semPersonagem = semPersonagem;
            bool tecla = Teclas.Pressionou(Tecla.Value);
            bool view = Teclas.Controle(c => c.selectButton, KeyCode.JoystickButton6);
            if (!_aberto)
            {
                PainelNativo.Esconder();
                if ((tecla || view) && !Balanceamento.JanelaAceite.Aberta && !Novidades.Aberta)
                    Abrir(view ? "botão View" : "tecla " + Tecla.Value, view);
                return;
            }

            BloqueioEntrada.Pedir();
            if (Balanceamento.JanelaAceite.Aberta)
            {
                Fechar("janela do aceite");
                return;
            }
            bool editando = _editandoNome; // 0.25.0: as teclas vão para o campo do nome (Esc cancela a edição, não fecha)
            if (editando) tecla = false; // a tecla do menu pode ser uma letra no .cfg: digitada, não fecha; o View fecha
            bool liberado = !editando && FechamentoLiberado(Time.unscaledTime, _fecharLiberadoEm); // F7 e View fecham sempre
            bool esc = liberado && Teclas.Pressionou("Escape");
            bool b = liberado && Teclas.Controle(c => c.buttonEast, KeyCode.JoystickButton1);
            if (tecla || view || esc || b)
            {
                Fechar(view || b ? "controle" : "teclado");
                return;
            }

            if (editando) EditarNome(rt);
            Montar(rt);
            if (!editando) // sem navegação nem ações enquanto edita
            {
                if (Teclas.Pressionou("UpArrow")) Mover(-1, false);
                if (Teclas.Pressionou("DownArrow")) Mover(1, false);
                if (Teclas.Controle(c => c.dpad.up, null)) Mover(-1, true);
                if (Teclas.Controle(c => c.dpad.down, null)) Mover(1, true);
                if (Teclas.Pressionou("LeftArrow")) Lado(-1, false);
                if (Teclas.Pressionou("RightArrow")) Lado(1, false);
                if (Teclas.Controle(c => c.dpad.left, null)) Lado(-1, true);
                if (Teclas.Controle(c => c.dpad.right, null)) Lado(1, true);
                if (Teclas.Controle(c => c.leftShoulder, KeyCode.JoystickButton4)) TrocarAba(-1, true);
                if (Teclas.Controle(c => c.rightShoulder, KeyCode.JoystickButton5)) TrocarAba(1, true);
                if (Teclas.Pressionou("Enter") || Teclas.Pressionou("NumpadEnter"))
                {
                    _controle = false;
                    Executar(rt, _foco);
                }
                else if (Teclas.Controle(c => c.buttonSouth, KeyCode.JoystickButton0))
                {
                    _controle = true;
                    Executar(rt, _foco);
                }
                else if (Teclas.Pressionou("Delete"))
                {
                    _controle = false;
                    Secundaria(rt, _foco, "Delete");
                }
                else if (Teclas.Controle(c => c.buttonWest, KeyCode.JoystickButton2))
                {
                    _controle = true;
                    Secundaria(rt, _foco, "X");
                }
            }
            if (!_aberto || !_nativo) return; // a reserva (OnGUI) lê o mouse no Desenhar

            try
            {
                MouseNativo(rt);
                if (!_aberto) return;
                Montar(rt); // o que a entrada mudou aparece no mesmo quadro
                PainelNativo.Mostrar(Linhas, IndiceDoFoco(), (int)_aba, Rodape(), _semPersonagem, ref _rolarParaFoco);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Menu] Painel com a interface do jogo falhou; volta ao menu simples: {e}");
                PainelNativo.Desativar();
                _nativo = false;
                if (_aba == Aba.Novidades) _aba = Aba.Sessao; // a gaveta não tem a aba (nem o rodapé dela)
            }
        }

        private static void Abrir(string motivo, bool peloControle, Aba aba = Aba.Sessao) // 0.26.0: volta a abrir na Sessão (de 0.21.0 a 0.25.0, Salas)
        {
            _aberto = true;
            _controle = peloControle;
            _aba = aba;
            _foco = Acao.Nenhuma; // o Montar escolhe o primeiro item disponível
            _arrastando = Acao.Nenhuma;
            _rolagem = 0f;
            PainelNativo.ZerarRolagem(); // reabre do topo
            _confirmarSairAte = -1f;
            _confirmarExpulsarAte = -1f;
            _confirmarBloquearAte = -1f;
            _confirmarDesbloquearAte = -1f;
            _confirmarEntrarAte = -1f;
            _fecharLiberadoEm = 0f;
            _novidadesVistas = false;
            _mouseAntes = Vector2.zero; // ao reabrir, o mouse parado não puxa o foco
            _mouseIniciado = false;
            _nativo = PainelNativo.Preparar();
            if (!_nativo && _aba == Aba.Novidades) _aba = Aba.Sessao; // a reserva não tem a aba (quem chama confere antes)
            BloqueioEntrada.Pedir(); // já no quadro em que abre (pelo botão, no OnGUI, o Update só viria no seguinte)
            Plugin.Log.LogInfo($"[Menu] Menu do coop aberto ({motivo}; {(_nativo ? "interface do jogo" : "menu simples")}; aba {NomesDasAbas[(int)_aba]}).");
        }

        internal static void Fechar(string motivo)
        {
            if (!_aberto) return;
            _aberto = false; // a entrada do jogo volta dois quadros depois (BloqueioEntrada)
            _arrastando = Acao.Nenhuma;
            PararEdicaoDoNome();
            PainelNativo.Esconder();
            if (_novidadesVistas) Novidades.MarcarVista(); // a aba Novidades apareceu: não abre sozinha de novo nesta versão
            Plugin.Log.LogInfo($"[Menu] Menu do coop fechado ({motivo}).");
        }

        /// <summary>Botão "Coop" do menu do jogo ou da pausa (BotoesDoJogo), no Update do runtime.</summary>
        internal static void AbrirPeloJogo(string motivo)
        {
            if (_aberto || Balanceamento.JanelaAceite.Aberta || Novidades.Aberta) return;
            Abrir(motivo, false);
        }

        /// <summary>
        /// Tecla da Novidades (F10) ou abertura automática, com o painel nativo: abre o menu na aba Novidades, troca para ela ou,
        /// já nela, fecha (só pela tecla). Falso: sem o painel com a interface do jogo, a Novidades usa a janela antiga.
        /// </summary>
        internal static bool MostrarNovidades(string motivo, bool automatica)
        {
            switch (RespostaNovidades(_aberto, _aberto ? _nativo : PainelNativo.Preparar(), _aba == Aba.Novidades, automatica))
            {
                case RespostaDaNovidade.Abrir:
                    Abrir(motivo, false, Aba.Novidades);
                    if (!_nativo)
                    {
                        Fechar("painel indisponível; Novidades na janela antiga"); // a montagem falhou agora: a janela antiga
                        return false;
                    }
                    if (automatica) _fecharLiberadoEm = Time.unscaledTime + EsperaFecharAutomatica;
                    return true;
                case RespostaDaNovidade.TrocarAba:
                    DefinirAba(Aba.Novidades);
                    return true;
                case RespostaDaNovidade.Fechar:
                    Fechar("tecla " + Novidades.Tecla.Value);
                    return true;
                case RespostaDaNovidade.Ficar:
                    return true;
                default:
                    return false;
            }
        }

        internal enum RespostaDaNovidade { Abrir, TrocarAba, Fechar, Ficar, JanelaAntiga }

        /// <summary>
        /// O que a tecla da Novidades (ou a abertura automática) faz com o menu do coop. Sem o painel nativo (reserva): a janela
        /// antiga. Fechado: abre na aba. Aberto em outra aba: troca. Já na aba: a tecla fecha; a abertura automática não.
        /// </summary>
        internal static RespostaDaNovidade RespostaNovidades(bool aberto, bool nativo, bool naAbaNovidades, bool automatica)
        {
            if (!nativo) return RespostaDaNovidade.JanelaAntiga;
            if (!aberto) return RespostaDaNovidade.Abrir;
            if (!naAbaNovidades) return RespostaDaNovidade.TrocarAba;
            return automatica ? RespostaDaNovidade.Ficar : RespostaDaNovidade.Fechar;
        }

        /// <summary>Esc, B e o [X] só fecham depois da espera da abertura automática (liberadoEm 0 = sem espera).</summary>
        internal static bool FechamentoLiberado(float agora, float liberadoEm) => agora >= liberadoEm;

        private static void Mover(int delta, bool peloControle)
        {
            _controle = peloControle;
            var selecionavel = new bool[Linhas.Count];
            int atual = -1;
            for (int i = 0; i < Linhas.Count; i++)
            {
                selecionavel[i] = Selecionavel(Linhas[i]);
                if (selecionavel[i] && Linhas[i].Acao == _foco) atual = i;
            }
            int proximo = ProximoFoco(atual, delta, selecionavel);
            if (proximo >= 0) _foco = Linhas[proximo].Acao;
            else if (_nativo) PainelNativo.Rolar(delta < 0 ? -PassoRoda : PassoRoda); // aba só de leitura (Novidades): as setas rolam
            _rolarParaFoco = true;
        }

        /// <summary>Esquerda/direita: ajusta o deslizante focado; fora dele, troca de aba (só no painel com abas).</summary>
        private static void Lado(int delta, bool peloControle)
        {
            _controle = peloControle;
            int i = IndiceDoFoco();
            if (i >= 0 && Linhas[i].Tipo == Tipo.Deslizante) Ajustar(_foco, delta);
            else TrocarAba(delta, peloControle);
        }

        private static void TrocarAba(int delta, bool peloControle)
        {
            if (!_nativo) return; // a reserva mostra as duas abas numa lista só
            _controle = peloControle;
            DefinirAba((Aba)ProximaAba((int)_aba, delta, NomesDasAbas.Length));
        }

        // ---------- Nome da sala (0.25.0): edição na linha ----------

        private static bool _editandoNome;
        private static string _textoNome = "";
        private static Keyboard _tecladoOuvido; // onde o OnTexto está inscrito (o teclado pode trocar)
        // 0.26.0: no jogo o Keyboard.current é nulo (o teclado vem pelo Input legado/Rewired), então o onTextInput não
        // entrega nada, e o Input.inputString foi removido pelo stripping. A outra fonte são os eventos de tecla do IMGUI
        // (Event.character no OnGUI, com acento e maiúscula do sistema). A primeira fonte que entregar uma letra fica com a
        // edição inteira, para a mesma letra não entrar duas vezes.
        private enum FonteTexto { Nenhuma, InputSystem, Gui }
        private static FonteTexto _fonteTexto;
        private static int _letrasInputSystem, _letrasGui;

        /// <summary>Host: Enter ou clique na linha "Nome da sala". No controle, tenta o teclado de tela da Steam antes.</summary>
        private static void ComecarEdicaoDoNome(CoopRuntime rt)
        {
            if (_editandoNome || !rt.Sessao.SouHost) return;
            if (_controle)
            {
                if (rt.Sessao.PedirNomeNoTecladoDaSteam()) return; // o nome chega pelo callback da Steam
                rt.Sessao.Avisar("O teclado de tela da Steam só abre no Big Picture ou no Steam Deck: digite o nome no teclado.");
            }
            _textoNome = rt.Sessao.NomeDaSalaAtual() ?? "";
            _editandoNome = true;
            _letrasInputSystem = _letrasGui = 0;
            _fonteTexto = FonteTexto.Nenhuma;
            try
            {
                _tecladoOuvido = Keyboard.current;
                if (_tecladoOuvido != null) _tecladoOuvido.onTextInput += OnTexto;
                else Plugin.Log.LogInfo("[Menu] Nome da sala: sem teclado no Input System; letras pelos eventos do OnGUI.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Menu] Sem leitura de texto do teclado: {e.Message}");
            }
        }

        /// <summary>Sai da edição sem salvar (o Enter salva antes de chamar). Também ao fechar, trocar de aba ou perder a sala.</summary>
        private static void PararEdicaoDoNome()
        {
            if (!_editandoNome) return;
            _editandoNome = false;
            _textoNome = "";
            Plugin.Log.LogInfo($"[Menu] Edição do nome encerrada: {_letrasInputSystem} letra(s) pelo Input System, {_letrasGui} pelo OnGUI.");
            try { if (_tecladoOuvido != null) _tecladoOuvido.onTextInput -= OnTexto; }
            catch (Exception) { /* teclado desconectado */ }
            _tecladoOuvido = null;
        }

        /// <summary>Caracteres digitados (Input System). Fora o que o Salas.LimparTexto tiraria; o Enter salva já limpo.</summary>
        private static void OnTexto(char c)
        {
            if (!_editandoNome || _fonteTexto == FonteTexto.Gui) return;
            if (Salas.CaractereAceito(c)) _fonteTexto = FonteTexto.InputSystem; // só letra aceita decide a fonte (o Enter não)
            if (Acrescentar(c)) _letrasInputSystem++;
        }

        /// <summary>No OnGUI do runtime: letras dos eventos de tecla do IMGUI, se o Input System não entregou nenhuma.</summary>
        internal static void TextoDoGui(Event e)
        {
            if (!_editandoNome || _fonteTexto == FonteTexto.InputSystem || e == null || e.type != EventType.KeyDown) return;
            char c = e.character;
            if (c == '\0') return; // tecla sem letra (setas, Shift...): o caractere, se houver, vem num evento próprio
            if (Salas.CaractereAceito(c)) _fonteTexto = FonteTexto.Gui;
            if (Acrescentar(c)) _letrasGui++;
        }

        /// <summary>Uma letra no campo, se couber e for aceita. Falso quando ficou de fora.</summary>
        private static bool Acrescentar(char c)
        {
            if (!_editandoNome || _textoNome.Length >= Salas.NomeMaximo) return false;
            if (!Salas.CaractereAceito(c)) return false; // inclui \b, \n e \r (Backspace e Enter têm leitura própria)
            // Emoji chega em duas metades: sem lugar para as duas, nenhuma entra; metade de baixo solta é descartada.
            if (char.IsHighSurrogate(c) && _textoNome.Length >= Salas.NomeMaximo - 1) return false;
            if (char.IsLowSurrogate(c) && (_textoNome.Length == 0 || !char.IsHighSurrogate(_textoNome[_textoNome.Length - 1]))) return false;
            _textoNome += c;
            return true;
        }

        /// <summary>No Update, editando: Enter salva, Esc ou B cancela, Backspace apaga (sem partir emoji).</summary>
        private static void EditarNome(CoopRuntime rt)
        {
            if (!rt.Sessao.SouHost)
            {
                PararEdicaoDoNome(); // saiu do coop ou deixou de ser o host
                return;
            }
            if (Teclas.Pressionou("Enter") || Teclas.Pressionou("NumpadEnter"))
            {
                rt.Sessao.RenomearSala(_textoNome);
                PararEdicaoDoNome();
            }
            else if (Teclas.Pressionou("Escape") || Teclas.Controle(c => c.buttonEast, KeyCode.JoystickButton1)) PararEdicaoDoNome();
            else if (Teclas.Pressionou("Backspace") && _textoNome.Length > 0)
            {
                int corte = _textoNome.Length - 1;
                if (corte > 0 && char.IsLowSurrogate(_textoNome[corte]) && char.IsHighSurrogate(_textoNome[corte - 1])) corte--;
                _textoNome = _textoNome.Substring(0, corte);
            }
        }

        /// <summary>Linha "Nome da sala" da aba Sessão (em coop). Convidado vê o nome, sem editar.</summary>
        private static void AdicionarNomeDaSala(SessaoCoop s)
        {
            if (!s.EmSessao) return;
            if (_editandoNome)
            {
                bool cursor = Time.unscaledTime % 1f < 0.5f;
                Linhas.Add(new Linha
                {
                    Tipo = Tipo.Item, Acao = Acao.NomeSala, Habilitada = true,
                    Texto = "Nome da sala:  " + _textoNome + (cursor ? "_" : " "),
                    Valor = _textoNome.Length + "/" + Salas.NomeMaximo,
                    Detalhe = "Enter salva  ·  Esc cancela  ·  vazio volta a \"" + Salas.NomePadrao(Steamworks.SteamFriends.GetPersonaName()) + "\"",
                });
                return;
            }
            Linhas.Add(new Linha
            {
                Tipo = Tipo.Item, Acao = Acao.NomeSala, Habilitada = s.SouHost,
                Texto = "Nome da sala:  " + s.NomeDaSalaAtual(), Valor = s.SouHost ? "Renomear" : "só o host muda",
            });
        }

        private static void DefinirAba(Aba aba)
        {
            if (aba == _aba) return;
            PararEdicaoDoNome();
            _aba = aba;
            _foco = Acao.Nenhuma; // o Montar escolhe o primeiro item da aba
            _arrastando = Acao.Nenhuma;
            _rolagem = 0f;
            _confirmarSairAte = -1f;
            _confirmarExpulsarAte = -1f;
            _confirmarBloquearAte = -1f;
            _confirmarDesbloquearAte = -1f;
            _confirmarEntrarAte = -1f;
            PainelNativo.ZerarRolagem();
        }

        private static bool Selecionavel(Linha l) => l.Tipo != Tipo.Secao && l.Tipo != Tipo.Info && l.Habilitada;

        private static int IndiceDoFoco()
        {
            for (int i = 0; i < Linhas.Count; i++)
            {
                if (Linhas[i].Acao == _foco && Selecionavel(Linhas[i])) return i;
            }
            return -1;
        }

        private static void Executar(CoopRuntime rt, Acao acao, string tecla = null) // tecla: só para o texto do 2º aperto
        {
            bool habilitada = false;
            foreach (Linha l in Linhas)
            {
                if (l.Acao == acao && Selecionavel(l)) habilitada = true;
            }
            if (!habilitada) return;
            int sala = IndiceNaFaixa(acao, Acao.Sala0, SalasNoMenu);
            int aliado = IndiceNaFaixa(acao, Acao.Expulsar0, AliadosNoMenu);
            int bloqueado = IndiceNaFaixa(acao, Acao.Desbloquear0, BloqueadosNoMenu);
            if (aliado >= 0 && !rt.Sessao.SouHost) // convidado não expulsa: na linha do aliado, o Enter também bloqueia
            {
                Secundaria(rt, acao, tecla ?? (_controle ? "A" : "Enter"));
                return;
            }
            _confirmarBloquearAte = -1f;
            if (aliado < 0) _confirmarExpulsarAte = -1f;
            if (sala < 0) _confirmarEntrarAte = -1f;
            if (bloqueado < 0) _confirmarDesbloquearAte = -1f;
            if (bloqueado >= 0)
            {
                if (bloqueado < Bloqueados.Quantidade)
                {
                    // Dois apertos, armados pelo SteamID: depois de desbloquear, o foco cai no próximo da lista, e o hábito
                    // do segundo aperto (Sair, Expulsar, Bloquear) não pode desbloquear outra pessoa.
                    KeyValuePair<ulong, string> b = Bloqueados.Item(bloqueado);
                    if (_desbloquearAlvo == b.Key && ConfirmaSaida(Time.unscaledTime, _confirmarDesbloquearAte))
                    {
                        _confirmarDesbloquearAte = -1f;
                        rt.Sessao.Desbloquear(b.Key, b.Value);
                    }
                    else
                    {
                        _desbloquearAlvo = b.Key;
                        _confirmarDesbloquearAte = Time.unscaledTime + EsperaConfirmarSaida;
                    }
                }
            }
            else if (sala >= 0)
            {
                if (sala < SalasMostradas.Count)
                {
                    SalaListada alvo = SalasMostradas[sala];
                    // Já num coop, entrar noutra sala sai dele: pede o segundo aperto, como o "Sair do coop".
                    if (!rt.Sessao.EmSessao || (_entrarAlvo == alvo.Lobby && ConfirmaSaida(Time.unscaledTime, _confirmarEntrarAte)))
                    {
                        _confirmarEntrarAte = -1f;
                        rt.Sessao.EntrarNaSala(alvo);
                    }
                    else
                    {
                        _entrarAlvo = alvo.Lobby;
                        _confirmarEntrarAte = Time.unscaledTime + EsperaConfirmarSaida;
                    }
                }
            }
            else if (aliado >= 0 && AliadosMostrados != null && aliado < AliadosMostrados.Count)
            {
                Par alvo = AliadosMostrados[aliado];
                if (_expulsarAlvo == alvo.Id.m_SteamID && ConfirmaSaida(Time.unscaledTime, _confirmarExpulsarAte))
                {
                    _confirmarExpulsarAte = -1f;
                    rt.Sessao.Expulsar(alvo);
                }
                else
                {
                    _expulsarAlvo = alvo.Id.m_SteamID;
                    _confirmarExpulsarAte = Time.unscaledTime + EsperaConfirmarSaida;
                }
            }
            switch (acao)
            {
                case Acao.SalaPublica:
                    rt.Sessao.AlternarSalaPublica();
                    break;
                case Acao.AtualizarSalas:
                    rt.Sessao.BuscarSalas();
                    break;
                case Acao.NomeSala:
                    ComecarEdicaoDoNome(rt);
                    break;
                case Acao.CriarOuConvidar:
                    rt.Sessao.HospedarOuConvidar();
                    break;
                case Acao.Sair:
                    if (ConfirmaSaida(Time.unscaledTime, _confirmarSairAte))
                    {
                        _confirmarSairAte = -1f;
                        rt.Sessao.Sair("menu do coop");
                    }
                    else _confirmarSairAte = Time.unscaledTime + EsperaConfirmarSaida;
                    break;
                case Acao.Eco:
                    rt.AlternarEco();
                    break;
                case Acao.Nomes:
                    Inverter(CoopRuntime.MostrarNomes);
                    break;
                case Acao.Vida:
                    Inverter(AliadosNaTela.MostrarVida);
                    break;
                case Acao.Seta:
                    Inverter(AliadosNaTela.MostrarSeta);
                    break;
                case Acao.Dano:
                    Inverter(Fase3.DanosVisiveis.MostrarDano);
                    break;
                case Acao.Sons:
                    Inverter(Fase3.SonsAliados.EntradaLigado);
                    break;
                case Acao.Novidades:
                    Fechar("Novidades");
                    Novidades.AbrirPeloMenu();
                    break;
            }
            if (acao != Acao.Sair) _confirmarSairAte = -1f;
        }

        /// <summary>
        /// Ação secundária (0.23.0): bloquear o aliado da linha ou o host da sala, com dois apertos (armado pelo SteamID).
        /// Em qualquer outra linha não faz nada.
        /// </summary>
        private static void Secundaria(CoopRuntime rt, Acao acao, string tecla)
        {
            if (!AlvoDoBloqueio(acao, out ulong id, out string nome)) return;
            _confirmarSairAte = _confirmarExpulsarAte = _confirmarEntrarAte = -1f;
            if (id == 0)
            {
                rt.Sessao.Avisar("Essa sala não informa quem é o host: não dá para bloquear por aqui.");
                return;
            }
            if (_bloquearAlvo == id && ConfirmaSaida(Time.unscaledTime, _confirmarBloquearAte))
            {
                _confirmarBloquearAte = -1f;
                rt.Sessao.Bloquear(id, nome);
                return;
            }
            _bloquearAlvo = id;
            _teclaBloquear = tecla;
            _confirmarBloquearAte = Time.unscaledTime + EsperaConfirmarSaida;
        }

        /// <summary>Quem a linha bloqueia: o aliado (nome da Steam) ou o dono anunciado da sala (id 0 = desconhecido).</summary>
        private static bool AlvoDoBloqueio(Acao acao, out ulong id, out string nome)
        {
            id = 0;
            nome = null;
            if (IndiceDe(acao) < 0 || !Selecionavel(Linhas[IndiceDe(acao)])) return false;
            int sala = IndiceNaFaixa(acao, Acao.Sala0, SalasNoMenu);
            if (sala >= 0 && sala < SalasMostradas.Count)
            {
                id = SalasMostradas[sala].Dono;
                nome = SalasMostradas[sala].Host;
                return true;
            }
            int aliado = IndiceNaFaixa(acao, Acao.Expulsar0, AliadosNoMenu);
            if (aliado >= 0 && AliadosMostrados != null && aliado < AliadosMostrados.Count)
            {
                id = AliadosMostrados[aliado].Id.m_SteamID;
                nome = Steamworks.SteamFriends.GetFriendPersonaName(AliadosMostrados[aliado].Id);
                return true;
            }
            return false;
        }

        private static bool BloqueioArmado(ulong id) => id != 0 && _bloquearAlvo == id && ConfirmaSaida(Time.unscaledTime, _confirmarBloquearAte);

        private static string TextoConfirmarBloqueio(string nome) => $"Confirmar ({_teclaBloquear} de novo): bloquear {nome}?";

        private static void Inverter(ConfigEntry<bool> entrada)
        {
            if (entrada == null) return;
            entrada.Value = !entrada.Value; // o BepInEx grava o .cfg
            Plugin.Log.LogInfo($"[Menu] [{entrada.Definition.Section}] {entrada.Definition.Key} = {entrada.Value}.");
        }

        // ---------- Deslizantes (valores do .cfg do mod) ----------

        private static ConfigEntry<float> Numero(Acao acao, out float min, out float max, out float passo)
        {
            if (acao == Acao.TamanhoDano)
            {
                min = 0.3f; max = 2f; passo = 0.1f; // mesma faixa que DanosVisiveis aplica
                return Fase3.DanosVisiveis.TamanhoDano;
            }
            if (acao == Acao.OpacidadeDano)
            {
                min = Fase3.DanosVisiveis.OpacidadeMinima; max = 1f; passo = 0.05f; // mesma faixa que DanosVisiveis aplica
                return Fase3.DanosVisiveis.OpacidadeDano;
            }
            min = 0f; max = 1f; passo = 0.05f;
            return acao == Acao.Volume ? Fase3.SonsAliados.EntradaVolume : null;
        }

        private static void Ajustar(Acao acao, int delta)
        {
            ConfigEntry<float> e = Numero(acao, out float min, out float max, out float passo);
            if (e != null) Gravar(e, AjustarNumero(e.Value, delta, passo, min, max));
        }

        private static void AjustarPelaFracao(Acao acao, float fracao)
        {
            ConfigEntry<float> e = Numero(acao, out float min, out float max, out float passo);
            if (e != null) Gravar(e, NumeroPelaFracao(fracao, passo, min, max));
        }

        private static void Gravar(ConfigEntry<float> e, float valor)
        {
            if (Mathf.Approximately(e.Value, valor)) return;
            e.Value = valor; // o BepInEx grava o .cfg
            Plugin.Log.LogInfo($"[Menu] [{e.Definition.Section}] {e.Definition.Key} = {valor:0.##}.");
        }

        private static void AdicionarDeslizante(Acao acao, string texto, bool habilitada)
        {
            ConfigEntry<float> e = Numero(acao, out float min, out float max, out _);
            float v = e != null ? Mathf.Clamp(e.Value, min, max) : min;
            Linhas.Add(new Linha
            {
                Tipo = Tipo.Deslizante, Acao = acao, Texto = texto, Habilitada = habilitada && e != null,
                Valor = e == null ? "-" : Mathf.RoundToInt(v * 100f) + "%",
                Fracao = max > min ? (v - min) / (max - min) : 0f,
            });
        }

        // ---------- Mouse do painel nativo ----------

        private static void MouseNativo(CoopRuntime rt)
        {
            bool mouse = _semPersonagem || BloqueioEntrada.SeguraOJogo; // em jogo, só com o jogo travado (o clique vazaria)
            if (!mouse || !LerMouse(out Vector2 pos, out bool apertou, out bool segurando, out float roda, out bool direito))
            {
                _arrastando = Acao.Nenhuma;
                return;
            }

            bool mexeu = _mouseIniciado && (pos - _mouseAntesNativo).sqrMagnitude > 1f;
            _mouseAntesNativo = pos;
            _mouseIniciado = true; // o primeiro quadro só registra a posição
            // A ação sob o mouse vem do que está desenhado; vale só se ainda é selecionável na lista deste quadro.
            Acao sob = PainelNativo.AcaoSob(pos);
            int iSob = IndiceDe(sob);
            bool sobValida = iSob >= 0 && Selecionavel(Linhas[iSob]);
            if (mexeu && sobValida)
            {
                _foco = sob;
                _controle = false;
            }

            if (roda != 0f && PainelNativo.DentroDoPainel(pos)) PainelNativo.Rolar(roda > 0f ? -PassoRoda : PassoRoda);

            if (_arrastando != Acao.Nenhuma)
            {
                int i = IndiceDe(_arrastando);
                if (!segurando || i < 0 || !Selecionavel(Linhas[i])) _arrastando = Acao.Nenhuma;
                else if (PainelNativo.FracaoNoDeslizante(_arrastando, pos, out float f)) AjustarPelaFracao(_arrastando, f);
                return;
            }
            if (direito && sobValida) // 0.23.0: clique direito = ação secundária (bloquear)
            {
                _foco = sob;
                _controle = false;
                Secundaria(rt, sob, "clique direito");
                return;
            }
            if (!apertou) return;
            _controle = false;
            if (_editandoNome && sob != Acao.NomeSala) PararEdicaoDoNome(); // clique fora do campo cancela
            if (PainelNativo.FecharSob(pos) && FechamentoLiberado(Time.unscaledTime, _fecharLiberadoEm))
            {
                Fechar("botão X");
                return;
            }
            int aba = PainelNativo.AbaSob(pos);
            if (aba >= 0)
            {
                DefinirAba((Aba)aba);
                return;
            }
            if (!sobValida) return;
            _foco = sob;
            if (Linhas[iSob].Tipo != Tipo.Deslizante) Executar(rt, _foco, "clique");
            else if (PainelNativo.NaBarra(sob, pos) && PainelNativo.FracaoNoDeslizante(sob, pos, out float f))
            {
                _arrastando = sob; // só o clique na barra muda o valor; no rótulo, só foca
                AjustarPelaFracao(sob, f);
            }
        }

        private static int IndiceDe(Acao acao)
        {
            if (acao == Acao.Nenhuma) return -1; // seções e informações não têm ação
            for (int i = 0; i < Linhas.Count; i++) if (Linhas[i].Acao == acao) return i;
            return -1;
        }

        /// <summary>Mouse pelo Input System (o jogo usa), com o legado de reserva. Posição em pixels, origem embaixo à esquerda.</summary>
        private static bool LerMouse(out Vector2 pos, out bool apertou, out bool segurando, out float roda, out bool direito)
        {
            try
            {
                Mouse m = Mouse.current;
                if (m != null)
                {
                    pos = m.position.ReadValue();
                    apertou = m.leftButton.wasPressedThisFrame;
                    direito = m.rightButton.wasPressedThisFrame;
                    segurando = m.leftButton.isPressed;
                    roda = m.scroll.ReadValue().y;
                    return true;
                }
            }
            catch (Exception)
            {
                // Input System indisponível: tenta o legado.
            }
            try
            {
                pos = Input.mousePosition;
                apertou = Input.GetMouseButtonDown(0);
                direito = Input.GetMouseButtonDown(1);
                segurando = Input.GetMouseButton(0);
                roda = Input.mouseScrollDelta.y;
                return true;
            }
            catch (InvalidOperationException)
            {
                pos = Vector2.zero;
                apertou = segurando = direito = false;
                roda = 0f;
                return false;
            }
        }

        // ---------- Conteúdo ----------

        private static void Montar(CoopRuntime rt)
        {
            Linhas.Clear();
            // A reserva (OnGUI) mostra Sessão, Salas e Ajustes numa lista só (a Novidades fica na janela antiga); o painel nativo, só a aba escolhida.
            if (!_nativo || _aba == Aba.Sessao) MontarSessao(rt);
            if (!_nativo || _aba == Aba.Salas) MontarSalas(rt);
            if (!_nativo || _aba == Aba.Ajustes) MontarAjustes();
            if (_nativo && _aba == Aba.Novidades)
            {
                Linhas.AddRange(LinhasNovidades());
                _novidadesVistas = true;
            }

            // Foco num item que sumiu ou ficou indisponível (ex.: saiu do coop): vai para o primeiro disponível.
            if (IndiceDoFoco() >= 0) return;
            var selecionavel = new bool[Linhas.Count];
            for (int i = 0; i < Linhas.Count; i++) selecionavel[i] = Selecionavel(Linhas[i]);
            int primeiro = ProximoFoco(-1, 1, selecionavel);
            _foco = primeiro >= 0 ? Linhas[primeiro].Acao : Acao.Nenhuma;
        }

        private static void MontarSessao(CoopRuntime rt)
        {
            SessaoCoop s = rt.Sessao;
            bool conectando = s.EstadoAtual == SessaoCoop.Estado.CriandoLobby || s.EstadoAtual == SessaoCoop.Estado.EntrandoLobby;

            if (rt.AvisoAtualizacao != null) Adicionar(Tipo.Info, Acao.Nenhuma, rt.AvisoAtualizacao, null, false, Enfase.Rotulo);
            Adicionar(Tipo.Info, Acao.Nenhuma, Status(s), null, false);
            if (!_semPersonagem)
            {
                Adicionar(Tipo.Info, Acao.Nenhuma, BloqueioEntrada.SeguraOJogo
                    ? "O jogo continua rodando; seu personagem fica parado com o menu aberto."
                    : "O jogo continua rodando. Use teclado ou controle: o clique moveria o personagem.", null, false);
            }

            // 0.26.0: criar/convidar é o primeiro item da aba (e o foco ao abrir).
            if (s.EmSessao) Adicionar(Tipo.Item, Acao.CriarOuConvidar, "Convidar amigos (Steam)", null, true);
            else if (conectando) Adicionar(Tipo.Item, Acao.CriarOuConvidar, "Conectando...", null, false);
            else if (s.EstadoAtual == SessaoCoop.Estado.AguardandoSteam) Adicionar(Tipo.Item, Acao.CriarOuConvidar, "Esperando o Steam...", null, false);
            else Adicionar(Tipo.Item, Acao.CriarOuConvidar, "Criar coop e convidar amigos", null, true);

            if (AliadosMostrados == null) AliadosMostrados = new List<Par>();
            AliadosMostrados.Clear();
            if (s.EmSessao && s.Pares.Count > 0)
            {
                Adicionar(Tipo.Secao, Acao.Nenhuma, "Aliados", null, false);
                foreach (Par p in s.Pares.Values)
                {
                    Adicionar(Tipo.Info, Acao.Nenhuma, p.Nome + "  -  " + CoopRuntime.EstadoDoPar(p), null, false);
                    if (AliadosMostrados.Count >= AliadosNoMenu) continue;
                    var acao = (Acao)((int)Acao.Expulsar0 + AliadosMostrados.Count);
                    AliadosMostrados.Add(p);
                    bool armado = _expulsarAlvo == p.Id.m_SteamID && ConfirmaSaida(Time.unscaledTime, _confirmarExpulsarAte);
                    // O nome do Hello é escolhido pelo par: no botão, o nome da Steam ligado ao SteamID (ninguém se passa por outro).
                    string nome = Salas.LimparNome(Steamworks.SteamFriends.GetFriendPersonaName(p.Id));
                    // 0.23.0: host — Enter expulsa e a secundária bloqueia; convidado — a linha só bloqueia.
                    string texto = BloqueioArmado(p.Id.m_SteamID) ? TextoConfirmarBloqueio(nome)
                        : !s.SouHost ? $"Bloquear {nome}" : armado ? $"Confirmar: expulsar {nome}?" : $"Expulsar {nome}";
                    Adicionar(Tipo.Item, acao, texto, null, true);
                }
            }

            Adicionar(Tipo.Secao, Acao.Nenhuma, "Coop", null, false);
            // Sala pública: vale para o próximo coop criado; como host, muda a sala atual na hora. Convidado não muda a do host.
            bool podeMudarSala = !s.EmSessao || s.SouHost;
            Linhas.Add(new Linha
            {
                Tipo = Tipo.Interruptor, Acao = Acao.SalaPublica, Texto = "Sala pública (aparece na aba Salas)",
                Habilitada = SessaoCoop.SalaPublica != null && podeMudarSala,
                Ligado = SessaoCoop.SalaPublica != null && SessaoCoop.SalaPublica.Value, Valor = Estado(SessaoCoop.SalaPublica),
            });
            AdicionarNomeDaSala(s); // 0.25.0
            if (s.EmSessao)
            {
                bool armado = ConfirmaSaida(Time.unscaledTime, _confirmarSairAte);
                Adicionar(Tipo.Item, Acao.Sair, armado ? "Confirmar: sair do coop?" : "Sair do coop", null, true);
            }
            string eco = rt.EcoAtivo ? "LIGADO" : _semPersonagem ? "entre num save" : "DESLIGADO";
            Adicionar(Tipo.Item, Acao.Eco, "Modo eco (teste com um PC)", eco, rt.EcoAtivo || !_semPersonagem);
        }

        /// <summary>
        /// Aba Salas (0.18.0; 0.21.0 visual A da janela larga): botão de atualizar com a idade da lista e um item de 2 linhas
        /// por sala ("Host  ·  Cursed War" | "Nv 34  2/4"; embaixo o lugar e o motivo de indisponível). Busca sozinha ao
        /// abrir e a cada 30 s com a aba à vista.
        /// </summary>
        private static void MontarSalas(CoopRuntime rt)
        {
            SessaoCoop s = rt.Sessao;
            bool steam = s.EstadoAtual != SessaoCoop.Estado.AguardandoSteam;
            bool conectando = s.EstadoAtual == SessaoCoop.Estado.CriandoLobby || s.EstadoAtual == SessaoCoop.Estado.EntrandoLobby;
            if (steam && (s.UltimaBusca < 0f || Time.unscaledTime - s.UltimaBusca > Salas.ListaVelhaApos)) s.BuscarSalas();

            // 0.25.0: a sala em que você está (host ou convidado), sempre no topo, também privada ou cheia; sem ação de entrar.
            // Vem do estado da sessão: a busca da Steam só traz salas públicas com vaga, e a nossa fica de fora (SalaVisivel).
            if (s.EmSessao)
            {
                Adicionar(Tipo.Secao, Acao.Nenhuma, "Sua sala", null, false);
                Adicionar(Tipo.Info, Acao.Nenhuma, s.NomeDaSalaAtual() + "  ·  você está aqui", null, false, Enfase.Titulo);
                string tipo = !s.SouHost ? "" : SessaoCoop.SalaPublica == null || SessaoCoop.SalaPublica.Value ? "  ·  pública" : "  ·  privada (só amigos)";
                Adicionar(Tipo.Info, Acao.Nenhuma, $"Host {s.HostDaSalaAtual()}  ·  {s.JogadoresNaSala}/{SessaoCoop.MaximoJogadores} jogadores{tipo}", null, false);
            }

            Adicionar(Tipo.Secao, Acao.Nenhuma, s.UltimaBusca < 0f || s.BuscandoSalas ? "Salas públicas" : $"Salas públicas  ·  {s.SalasListadas.Count} encontrada(s)", null, false);
            string idade = s.UltimaBusca < 0f ? null : "há " + Mathf.FloorToInt(Time.unscaledTime - s.UltimaBusca) + " s";
            Adicionar(Tipo.Item, Acao.AtualizarSalas, s.BuscandoSalas ? "Buscando salas..." : "Atualizar lista", s.BuscandoSalas ? null : idade,
                steam && !s.BuscandoSalas);

            if (!steam) Adicionar(Tipo.Info, Acao.Nenhuma, "Esperando o Steam ficar pronto.", null, false);
            else if (s.FalhaNaBusca) Adicionar(Tipo.Info, Acao.Nenhuma, "A busca falhou (sem conexão com a Steam). Tente de novo.", null, false);
            else if (!s.BuscandoSalas && s.UltimaBusca >= 0f && s.SalasListadas.Count == 0)
                Adicionar(Tipo.Info, Acao.Nenhuma, $"Nenhuma sala pública aberta com o mod {Plugin.PluginVersion}. Crie a sua na aba Sessão.", null, false);

            SalasMostradas.Clear();
            bool algumaTravada = false;
            for (int i = 0; i < s.SalasListadas.Count && i < SalasNoMenu; i++)
            {
                SalaListada sala = s.SalasListadas[i];
                SalasMostradas.Add(sala);
                s.AtualizarTrava(sala); // recorde do save carregado agora e DLC deste PC; indisponível não entra
                bool travada = Salas.SalaIndisponivel(sala);
                if (travada) algumaTravada = true;
                bool armada = !travada && _entrarAlvo == sala.Lobby && ConfirmaSaida(Time.unscaledTime, _confirmarEntrarAte);
                Linhas.Add(new Linha
                {
                    Tipo = Tipo.Item, Acao = (Acao)((int)Acao.Sala0 + i), Habilitada = !conectando && !travada,
                    Texto = BloqueioArmado(sala.Dono) ? TextoConfirmarBloqueio(sala.Host)
                        : armada ? "Confirmar: sair do seu coop e entrar?" : Salas.TextoSala(sala),
                    Valor = Salas.ValorSala(sala), Detalhe = Salas.DetalheSala(sala),
                });
            }

            if (algumaTravada) Adicionar(Tipo.Info, Acao.Nenhuma, "Salas indisponíveis estão no Ato 4 sem você ter a DLC Cursed War, ou numa run aonde o seu save ainda não chegou (mesma masmorra e andar).", null, false);

            if (s.EmSessao && s.SalasListadas.Count > 0) Adicionar(Tipo.Info, Acao.Nenhuma, "Entrar numa sala tira você do coop atual (pede confirmação).", null, false);
            Adicionar(Tipo.Info, Acao.Nenhuma, "Só aparecem salas com vaga e a mesma versão do mod, com ou sem a DLC Cursed War (o Ato 4 só com a DLC). Dá para entrar com a run em andamento.", null, false);
        }

        private static void MontarAjustes()
        {
            Adicionar(Tipo.Secao, Acao.Nenhuma, "Aliados na tela", null, false);
            AdicionarInterruptor(Acao.Nomes, "Nomes dos aliados", CoopRuntime.MostrarNomes);
            AdicionarInterruptor(Acao.Vida, "Barra de vida dos aliados", AliadosNaTela.MostrarVida);
            AdicionarInterruptor(Acao.Seta, "Seta do aliado fora da tela", AliadosNaTela.MostrarSeta);
            AdicionarInterruptor(Acao.Dano, "Números de dano dos aliados", Fase3.DanosVisiveis.MostrarDano);
            bool danoLigado = Fase3.DanosVisiveis.MostrarDano == null || Fase3.DanosVisiveis.MostrarDano.Value;
            AdicionarDeslizante(Acao.TamanhoDano, "Tamanho do dano", danoLigado);
            AdicionarDeslizante(Acao.OpacidadeDano, "Opacidade dos danos dos aliados", danoLigado); // 0.26.0

            Adicionar(Tipo.Secao, Acao.Nenhuma, "Som", null, false);
            AdicionarInterruptor(Acao.Sons, "Sons das skills dos aliados", Fase3.SonsAliados.EntradaLigado);
            AdicionarDeslizante(Acao.Volume, "Volume dos sons", Fase3.SonsAliados.Ligado);

            Adicionar(Tipo.Secao, Acao.Nenhuma, "Balanceamento", null, false);
            foreach (string linha in Balanceamento.Escalonamento.LinhasDoMenu()) Adicionar(Tipo.Info, Acao.Nenhuma, linha, null, false);

            // 0.23.0: bloqueados deste PC (bloqueados.txt), na ordem em que foram bloqueados; Enter desbloqueia.
            int total = Bloqueados.Quantidade;
            Adicionar(Tipo.Secao, Acao.Nenhuma, total > 0 ? $"Bloqueados ({total})" : "Bloqueados", null, false);
            if (total == 0)
            {
                Adicionar(Tipo.Info, Acao.Nenhuma, "Nenhum bloqueado.", null, false);
                Adicionar(Tipo.Info, Acao.Nenhuma, "Bloqueie nas abas Sessão e Salas: X no controle, Delete ou clique direito, duas vezes.", null, false);
            }
            for (int i = 0; i < total && i < BloqueadosNoMenu; i++)
            {
                KeyValuePair<ulong, string> b = Bloqueados.Item(i);
                bool armado = _desbloquearAlvo == b.Key && ConfirmaSaida(Time.unscaledTime, _confirmarDesbloquearAte);
                Adicionar(Tipo.Item, (Acao)((int)Acao.Desbloquear0 + i), armado ? $"Confirmar: desbloquear {b.Value}?" : b.Value, armado ? null : "Desbloquear", true);
            }
            if (total > BloqueadosNoMenu)
                Adicionar(Tipo.Info, Acao.Nenhuma, $"E mais {total - BloqueadosNoMenu} no arquivo BepInEx/HellClockCoop/bloqueados.txt.", null, false);
            Adicionar(Tipo.Info, Acao.Nenhuma, "Quem você bloqueou na Steam também fica de fora (desbloqueie pela Steam).", null, false);

            if (_nativo) return; // no painel nativo, as novidades são a terceira aba
            Adicionar(Tipo.Secao, Acao.Nenhuma, "Mod", null, false);
            Adicionar(Tipo.Item, Acao.Novidades, "Novidades da " + Plugin.PluginVersion, null, true);
        }

        /// <summary>
        /// Aba Novidades: a versão atual e as 3 anteriores numa lista que rola (roda, setas ou direcional), com "O que testar" nas
        /// versões ainda não testadas no jogo. Texto fixo da DLL (NovidadesTexto): montado uma vez só.
        /// </summary>
        private static List<Linha> LinhasNovidades()
        {
            if (_linhasNovidades != null) return _linhasNovidades;
            var l = new List<Linha>();
            VersaoNovidade[] versoes = NovidadesTexto.Versoes;
            for (int i = 0; i < versoes.Length; i++)
            {
                VersaoNovidade v = versoes[i];
                l.Add(new Linha { Tipo = Tipo.Secao, Texto = "v" + v.V + (i == 0 ? "  ·  nova" : "") });
                l.Add(new Linha { Tipo = Tipo.Info, Texto = v.Data + (v.Testado ? "" : "  ·  ainda não testada no jogo") });
                l.Add(new Linha { Tipo = Tipo.Info, Texto = v.Titulo, Enfase = Enfase.Titulo });
                foreach (string item in v.Itens) l.Add(new Linha { Tipo = Tipo.Info, Texto = "•  " + item });
                if (!Novidades.MostraTestar(v)) continue;
                l.Add(new Linha { Tipo = Tipo.Info, Texto = "O QUE TESTAR", Enfase = Enfase.Rotulo });
                foreach (string t in v.Testar) l.Add(new Linha { Tipo = Tipo.Info, Texto = "›  " + t });
            }
            if (l.Count == 0) l.Add(new Linha { Tipo = Tipo.Info, Texto = "Sem novidades embutidas nesta versão." });
            _linhasNovidades = l;
            return l;
        }

        private static void Adicionar(Tipo tipo, Acao acao, string texto, string valor, bool habilitada, Enfase enfase = Enfase.Normal) =>
            Linhas.Add(new Linha { Tipo = tipo, Acao = acao, Texto = texto, Valor = valor, Habilitada = habilitada, Enfase = enfase });

        private static void AdicionarInterruptor(Acao acao, string texto, ConfigEntry<bool> entrada) =>
            Linhas.Add(new Linha
            {
                Tipo = Tipo.Interruptor, Acao = acao, Texto = texto, Habilitada = entrada != null,
                Ligado = entrada != null && entrada.Value, Valor = Estado(entrada),
            });

        private static string Estado(ConfigEntry<bool> entrada) => entrada == null ? "-" : entrada.Value ? "LIGADO" : "DESLIGADO";

        private static string Status(SessaoCoop s)
        {
            switch (s.EstadoAtual)
            {
                case SessaoCoop.Estado.AguardandoSteam: return "Esperando o Steam ficar pronto.";
                case SessaoCoop.Estado.CriandoLobby:
                case SessaoCoop.Estado.EntrandoLobby: return "Conectando...";
                case SessaoCoop.Estado.NoLobby: break;
                default: return "Sem coop. Crie um e convide amigos da Steam.";
            }
            string jogadores = $"{s.Pares.Count + 1}/{SessaoCoop.MaximoJogadores} jogadores";
            if (s.SouHost) return "Você é o host  -  " + jogadores + (s.ModoHost ? "  -  a run é sua" : "");
            Par host = s.Host;
            return "Convidado de " + (host != null ? host.Nome : "host") + "  -  " + jogadores + (s.ModoConvidado ? "  -  acompanhando a run do host" : "");
        }

        private static string Rodape()
        {
            if (_editandoNome) return "Digite o nome   Enter: salvar   Backspace: apagar   Esc: cancelar";
            if (_aba == Aba.Novidades)
                return _controle
                    ? "LB/RB: aba   Direcional: rolar   B ou View: fechar"
                    : $"Setas ou roda: rolar   Setas laterais: aba   Esc, {Tecla.Value} ou {Novidades.Tecla.Value}: fechar";
            // 0.23.0: na linha do aliado ou da sala, o rodapé ensina a ação secundária (bloquear).
            bool bloqueia = IndiceNaFaixa(_foco, Acao.Sala0, SalasNoMenu) >= 0 || IndiceNaFaixa(_foco, Acao.Expulsar0, AliadosNoMenu) >= 0;
            if (bloqueia)
                return _controle
                    ? "LB/RB: aba   A: confirmar   X: bloquear   B ou View: fechar"
                    : $"Enter: confirmar   Delete ou clique direito: bloquear   Esc ou {Tecla.Value}: fechar";
            return _controle
                ? "LB/RB: aba   Direcional: escolher e ajustar   A: confirmar   B ou View: fechar"
                : $"Setas: escolher, ajustar e trocar de aba   Enter: confirmar   Esc ou {Tecla.Value}: fechar";
        }

        // ---------- Reserva: desenho no OnGUI do runtime ----------

        /// <summary>
        /// Reserva do botão "Coop" do menu do jogo (BotoesDoJogo): desde a 0.13.0 só aparece se a cópia na lista de botões do
        /// jogo falhou. Botão "Menu do coop" abaixo do painel do canto: só no menu (sem personagem) e sem outra janela aberta.
        /// Desenhado com a peça de aba do jogo (PainelNativo) e um botão invisível por cima para o clique; sem ela, o botão simples.
        /// </summary>
        internal static void DesenharBotaoMenu(float y)
        {
            if (!BotoesDoJogo.MenuFalhou) return;
            if (_aberto || !_semPersonagem || Balanceamento.JanelaAceite.Aberta || Novidades.Aberta) return;
            Estilos();
            string texto = $"Menu do coop ({Tecla.Value})";
            if (PainelNativo.BotaoDoMenu(12f, y, texto, out Rect area))
            {
                if (GUI.Button(area, GUIContent.none, _invisivel)) Abrir("botão do painel", false);
                return;
            }
            if (PainelNativo.EsperandoJanela) return; // telas de abertura: sem botão até a interface do jogo existir
            if (GUI.Button(new Rect(12f, y, 240f, LadoBotao), new GUIContent(texto), _botao))
                Abrir("botão do painel", false);
        }

        /// <summary>Gaveta desenhada no OnGUI: só quando o painel com a interface do jogo não está disponível.</summary>
        internal static void Desenhar(CoopRuntime rt)
        {
            if (!_aberto || _nativo) return;
            Estilos();
            Montar(rt);

            // Tudo desenhado em 1080p e escalado, como a Novidades e a JanelaAceite.
            float escala = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            _alturaItem = Mathf.Max(AlturaItem, 44f / escala); // alvo de clique de 44 px reais também abaixo de 1080p
            _ladoX = Mathf.Max(LadoBotao, 44f / escala);
            Matrix4x4 matrizAntes = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(escala, escala, 1f));
            float telaL = Screen.width / escala, telaA = Screen.height / escala;
            Color corAntes = GUI.color;
            bool mouse = _semPersonagem || BloqueioEntrada.SeguraOJogo; // em jogo, só com o jogo travado (o clique vazaria)
            Event ev = Event.current;

            bool mouseMexeu = false;
            if (mouse && ev.type == EventType.Repaint && ev.mousePosition != _mouseAntes)
            {
                mouseMexeu = _mouseAntes != Vector2.zero; // o primeiro quadro só registra a posição
                _mouseAntes = ev.mousePosition;
            }

            float largura = Mathf.Min(Mathf.Clamp(telaL * 0.4f, LarguraMin, LarguraMax), telaL);
            if (_semPersonagem) Retangulo(new Rect(0f, 0f, telaL - largura, telaA), new Color(0f, 0f, 0f, 0.35f)); // em jogo: sem escurecer
            var caixa = new Rect(telaL - largura, 0f, largura, telaA);
            Retangulo(caixa, Ferrugem);
            Retangulo(new Rect(caixa.x + 2f, 0f, largura - 2f, telaA), new Color(0.07f, 0.06f, 0.06f, 0.97f));

            float x = caixa.x + 2f + Margem, interna = largura - 2f - 2f * Margem, y = Margem;

            // Cabeçalho: título e [X] (o estado da sessão é a primeira linha da lista).
            GUI.Label(new Rect(x, y, interna - _ladoX - 8f, _ladoX), new GUIContent("COOP"), _titulo);
            bool fechar = mouse && GUI.Button(new Rect(x + interna - _ladoX, y, _ladoX, _ladoX), new GUIContent("X"), _botao);
            y += _ladoX + 6f;
            Retangulo(new Rect(x, y + 6f, interna, 1f), new Color(0.55f, 0.14f, 0.1f, 0.6f));
            y += 14f;

            // Rodapé com as teclas do último dispositivo usado.
            var dica = new GUIContent(_controle
                ? "Direcional: escolher e ajustar  ·  A: confirmar  ·  B ou View: fechar"
                : $"Setas: escolher e ajustar  ·  Enter: confirmar  ·  Esc ou {Tecla.Value}: fechar");
            float aRodape = _rodape.CalcHeight(dica, interna);
            float yRodape = telaA - Margem - aRodape;
            Retangulo(new Rect(x, yRodape - 10f, interna, 1f), new Color(0.55f, 0.14f, 0.1f, 0.6f));
            GUI.Label(new Rect(x, yRodape, interna, aRodape), dica, _rodape);

            // Corpo: lista com recorte; rola sozinha até o item focado e pela roda do mouse.
            var area = new Rect(x, y, interna, Mathf.Max(_alturaItem, yRodape - 18f - y));
            float aCorpo = 0f, topoFoco = -1f, alturaFoco = 0f;
            foreach (Linha l in Linhas)
            {
                float h = Altura(l, interna);
                if (Selecionavel(l) && l.Acao == _foco) { topoFoco = aCorpo; alturaFoco = h; }
                aCorpo += h + Espaco(l);
            }
            if (_rolarParaFoco && topoFoco >= 0f)
            {
                if (topoFoco < _rolagem) _rolagem = topoFoco;
                else if (topoFoco + alturaFoco > _rolagem + area.height) _rolagem = topoFoco + alturaFoco - area.height;
                _rolarParaFoco = false;
            }
            if (mouse && ev.type == EventType.ScrollWheel && area.Contains(ev.mousePosition))
            {
                _rolagem += ev.delta.y * 20f;
                ev.Use();
            }
            _rolagem = Mathf.Clamp(_rolagem, 0f, Mathf.Max(0f, aCorpo - area.height));

            Acao clicada = Acao.Nenhuma, clicadaDireito = Acao.Nenhuma;
            int ladoClicado = 0;
            GUI.BeginGroup(area, GUIContent.none, GUIStyle.none); // sobrecarga que existe no jogo (stripping)
            float yl = -_rolagem;
            foreach (Linha l in Linhas)
            {
                float h = Altura(l, interna);
                var r = new Rect(0f, yl, interna, h);
                if (l.Tipo == Tipo.Secao)
                {
                    GUI.Label(new Rect(0f, yl + 10f, interna, h - 10f), new GUIContent(l.Texto.ToUpperInvariant()), _secao);
                }
                else if (l.Tipo == Tipo.Info)
                {
                    GUI.Label(new Rect(8f, yl, interna - 8f, h), new GUIContent(l.Texto), _info);
                }
                else
                {
                    bool focado = l.Acao == _foco && l.Habilitada;
                    if (focado)
                    {
                        Retangulo(r, new Color(0.55f, 0.14f, 0.1f, 0.35f));
                        Moldura(r, Creme, 2f);
                    }
                    else Retangulo(r, new Color(0.12f, 0.1f, 0.09f, 1f));
                    GUI.color = l.Habilitada ? corAntes : new Color(1f, 1f, 1f, 0.45f);
                    string valor = l.Tipo == Tipo.Deslizante ? "<  " + l.Valor + "  >" : l.Valor;
                    float lValor = valor != null ? LarguraValor : 0f;
                    float hTexto = string.IsNullOrEmpty(l.Detalhe) ? h : _alturaItem; // detalhe: 2ª linha embaixo
                    GUI.Label(new Rect(14f, yl, interna - 28f - lValor, hTexto), new GUIContent(l.Texto), _item);
                    if (hTexto < h) GUI.Label(new Rect(28f, yl + hTexto - 10f, interna - 42f, h - hTexto + 4f), new GUIContent(l.Detalhe), _detalhe);
                    if (valor != null) GUI.Label(new Rect(interna - 14f - lValor, yl, lValor, hTexto), new GUIContent(valor), _valor);
                    GUI.color = corAntes;
                    if (mouse && l.Habilitada)
                    {
                        if (mouseMexeu && r.Contains(ev.mousePosition)) { _foco = l.Acao; _controle = false; }
                        if (GUI.Button(r, GUIContent.none, _invisivel))
                        {
                            _foco = l.Acao;
                            _controle = false;
                            if (l.Tipo == Tipo.Deslizante) ladoClicado = ev.mousePosition.x < interna / 2f ? -1 : 1; // metade esquerda diminui
                            else if (ev.button == 1) clicadaDireito = l.Acao; // o GUI.Button também dispara no direito
                            else clicada = l.Acao;
                        }
                    }
                }
                yl += h + Espaco(l);
            }
            GUI.EndGroup();

            GUI.color = corAntes;
            GUI.matrix = matrizAntes;
            if (clicada != Acao.Nenhuma && _editandoNome && clicada != Acao.NomeSala) PararEdicaoDoNome(); // clique fora cancela
            if (clicada != Acao.Nenhuma) Executar(rt, clicada, "clique");
            if (clicadaDireito != Acao.Nenhuma) Secundaria(rt, clicadaDireito, "clique direito"); // 0.23.0
            if (ladoClicado != 0) Ajustar(_foco, ladoClicado);
            if (fechar) Fechar("botão X");
        }

        private static float Altura(Linha l, float largura)
        {
            if (l.Tipo == Tipo.Secao) return AlturaSecao;
            if (l.Tipo != Tipo.Info) return string.IsNullOrEmpty(l.Detalhe) ? _alturaItem : _alturaItem + AlturaDetalhe;
            return Mathf.Max(26f, _info.CalcHeight(new GUIContent(l.Texto), largura - 8f));
        }

        private static float Espaco(Linha l) => l.Tipo == Tipo.Secao || l.Tipo == Tipo.Info ? 2f : 6f;

        private static void Estilos()
        {
            if (_titulo != null) return;
            _titulo = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _titulo.normal.textColor = Creme;
            _secao = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            _secao.normal.textColor = new Color(0.92f, 0.5f, 0.38f);
            _info = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, alignment = TextAnchor.MiddleLeft, richText = false };
            _info.normal.textColor = new Color(0.86f, 0.84f, 0.8f);
            _item = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft }; // (setter de clipping removido pelo stripping; o label já recorta)
            _item.normal.textColor = Creme;
            _valor = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _valor.normal.textColor = new Color(0.92f, 0.5f, 0.38f);
            _detalhe = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, richText = false };
            _detalhe.normal.textColor = new Color(0.72f, 0.68f, 0.65f);
            _rodape = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _rodape.normal.textColor = new Color(0.62f, 0.6f, 0.57f);
            _botao = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            _invisivel = new GUIStyle(); // botão sem desenho por cima da linha (só recebe o clique)
        }

        private static void Moldura(Rect r, Color cor, float e)
        {
            Retangulo(new Rect(r.x, r.y, r.width, e), cor);
            Retangulo(new Rect(r.x, r.yMax - e, r.width, e), cor);
            Retangulo(new Rect(r.x, r.y, e, r.height), cor);
            Retangulo(new Rect(r.xMax - e, r.y, e, r.height), cor);
        }

        private static void Retangulo(Rect r, Color cor)
        {
            Color antes = GUI.color;
            GUI.color = cor;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = antes;
        }
    }
}
