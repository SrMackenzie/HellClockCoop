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
    /// do jogo, com as abas Sessão, Ajustes e, desde a 0.13.0, Novidades); se a skin do jogo não puder ser montada, volta à gaveta
    /// desenhada no OnGUI (lista única, como na 0.9.3) e a Novidades à janela própria.
    /// - Abre e fecha pela tecla [Teclas] MenuDoCoop (F7), pelo botão View/Select do controle ou pelo botão "Coop" do menu do
    ///   jogo e da pausa (BotoesDoJogo, 0.13.0); fecha também por Esc, B e pelo [X]. A tecla da Novidades (F10) abre na aba
    ///   Novidades; aberta sozinha depois de atualizar, Esc, B e o [X] só fecham depois de 1,5 s.
    /// - Sessão: recado do atualizador, estado, ALIADOS (só leitura), COOP (criar e convidar, sair com confirmação, modo eco).
    ///   Ajustes: nomes, números e tamanho do dano dos aliados, sons e volume dos aliados (gravados no .cfg do mod,
    ///   nunca nas opções do jogo) e balanceamento deste .cfg só para leitura. Novidades: a versão atual e as 3 anteriores.
    /// - Aberto, segura a entrada do jogo (BloqueioEntrada): mouse, teclado e controle operam o menu sem mexer no
    ///   personagem nem abrir a pausa. Em jogo não escurece a tela nem pausa: o jogo continua rodando.
    /// - Se o semáforo do jogo não travar, em jogo o menu responde só a teclado e controle (o clique moveria o personagem).
    /// </summary>
    internal static class MenuCoop
    {
        internal enum Acao { Nenhuma, CriarOuConvidar, Sair, Eco, Nomes, Dano, TamanhoDano, Sons, Volume, Novidades }
        internal enum Tipo { Secao, Info, Item, Interruptor, Deslizante }
        internal enum Aba { Sessao, Ajustes, Novidades }
        internal enum Enfase { Normal, Titulo, Rotulo } // cor da linha de informação (aba Novidades)

        internal struct Linha
        {
            public Tipo Tipo;
            public Acao Acao;
            public string Texto, Valor;
            public bool Habilitada, Ligado;
            public float Fracao; // deslizante: posição 0..1
            public Enfase Enfase;
        }

        internal static readonly string[] NomesDasAbas = { "Sessão", "Ajustes", "Novidades" };

        private const float LarguraMin = 380f, LarguraMax = 460f;
        private const float Margem = 20f, AlturaItem = 48f, AlturaSecao = 34f, LadoBotao = 44f, LarguraValor = 120f;
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
        private static GUIStyle _titulo, _secao, _info, _item, _valor, _rodape, _botao, _invisivel;

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
            bool liberado = FechamentoLiberado(Time.unscaledTime, _fecharLiberadoEm); // F7 e View fecham sempre
            bool esc = liberado && Teclas.Pressionou("Escape");
            bool b = liberado && Teclas.Controle(c => c.buttonEast, KeyCode.JoystickButton1);
            if (tecla || view || esc || b)
            {
                Fechar(view || b ? "controle" : "teclado");
                return;
            }

            Montar(rt);
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

        private static void Abrir(string motivo, bool peloControle, Aba aba = Aba.Sessao)
        {
            _aberto = true;
            _controle = peloControle;
            _aba = aba;
            _foco = Acao.Nenhuma; // o Montar escolhe o primeiro item disponível
            _arrastando = Acao.Nenhuma;
            _rolagem = 0f;
            PainelNativo.ZerarRolagem(); // reabre do topo
            _confirmarSairAte = -1f;
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

        private static void DefinirAba(Aba aba)
        {
            if (aba == _aba) return;
            _aba = aba;
            _foco = Acao.Nenhuma; // o Montar escolhe o primeiro item da aba
            _arrastando = Acao.Nenhuma;
            _rolagem = 0f;
            _confirmarSairAte = -1f;
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

        private static void Executar(CoopRuntime rt, Acao acao)
        {
            bool habilitada = false;
            foreach (Linha l in Linhas)
            {
                if (l.Acao == acao && Selecionavel(l)) habilitada = true;
            }
            if (!habilitada) return;
            switch (acao)
            {
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
            if (!mouse || !LerMouse(out Vector2 pos, out bool apertou, out bool segurando, out float roda))
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
            if (!apertou) return;
            _controle = false;
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
            if (Linhas[iSob].Tipo != Tipo.Deslizante) Executar(rt, _foco);
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
        private static bool LerMouse(out Vector2 pos, out bool apertou, out bool segurando, out float roda)
        {
            try
            {
                Mouse m = Mouse.current;
                if (m != null)
                {
                    pos = m.position.ReadValue();
                    apertou = m.leftButton.wasPressedThisFrame;
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
                segurando = Input.GetMouseButton(0);
                roda = Input.mouseScrollDelta.y;
                return true;
            }
            catch (InvalidOperationException)
            {
                pos = Vector2.zero;
                apertou = segurando = false;
                roda = 0f;
                return false;
            }
        }

        // ---------- Conteúdo ----------

        private static void Montar(CoopRuntime rt)
        {
            Linhas.Clear();
            // A reserva (OnGUI) mostra Sessão e Ajustes numa lista só (a Novidades fica na janela antiga); o painel nativo, só a aba escolhida.
            if (!_nativo || _aba == Aba.Sessao) MontarSessao(rt);
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

            if (s.EmSessao && s.Pares.Count > 0)
            {
                Adicionar(Tipo.Secao, Acao.Nenhuma, "Aliados", null, false);
                foreach (Par p in s.Pares.Values) Adicionar(Tipo.Info, Acao.Nenhuma, p.Nome + "  -  " + CoopRuntime.EstadoDoPar(p), null, false);
            }

            Adicionar(Tipo.Secao, Acao.Nenhuma, "Coop", null, false);
            if (s.EmSessao) Adicionar(Tipo.Item, Acao.CriarOuConvidar, "Convidar amigos (Steam)", null, true);
            else if (conectando) Adicionar(Tipo.Item, Acao.CriarOuConvidar, "Conectando...", null, false);
            else if (s.EstadoAtual == SessaoCoop.Estado.AguardandoSteam) Adicionar(Tipo.Item, Acao.CriarOuConvidar, "Esperando o Steam...", null, false);
            else Adicionar(Tipo.Item, Acao.CriarOuConvidar, "Criar coop e convidar amigos", null, true);
            if (s.EmSessao)
            {
                bool armado = ConfirmaSaida(Time.unscaledTime, _confirmarSairAte);
                Adicionar(Tipo.Item, Acao.Sair, armado ? "Confirmar: sair do coop?" : "Sair do coop", null, true);
            }
            string eco = rt.EcoAtivo ? "LIGADO" : _semPersonagem ? "entre num save" : "DESLIGADO";
            Adicionar(Tipo.Item, Acao.Eco, "Modo eco (teste com um PC)", eco, rt.EcoAtivo || !_semPersonagem);
        }

        private static void MontarAjustes()
        {
            Adicionar(Tipo.Secao, Acao.Nenhuma, "Aliados na tela", null, false);
            AdicionarInterruptor(Acao.Nomes, "Nomes dos aliados", CoopRuntime.MostrarNomes);
            AdicionarInterruptor(Acao.Dano, "Números de dano dos aliados", Fase3.DanosVisiveis.MostrarDano);
            AdicionarDeslizante(Acao.TamanhoDano, "Tamanho do dano", Fase3.DanosVisiveis.MostrarDano == null || Fase3.DanosVisiveis.MostrarDano.Value);

            Adicionar(Tipo.Secao, Acao.Nenhuma, "Som", null, false);
            AdicionarInterruptor(Acao.Sons, "Sons das skills dos aliados", Fase3.SonsAliados.EntradaLigado);
            AdicionarDeslizante(Acao.Volume, "Volume dos sons", Fase3.SonsAliados.Ligado);

            Adicionar(Tipo.Secao, Acao.Nenhuma, "Balanceamento", null, false);
            foreach (string linha in Balanceamento.Escalonamento.LinhasDoMenu()) Adicionar(Tipo.Info, Acao.Nenhuma, linha, null, false);

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
            if (_aba == Aba.Novidades)
                return _controle
                    ? "LB/RB: aba   Direcional: rolar   B ou View: fechar"
                    : $"Setas ou roda: rolar   Setas laterais: aba   Esc, {Tecla.Value} ou {Novidades.Tecla.Value}: fechar";
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

            float largura = Mathf.Min(Mathf.Clamp(telaL * 0.3f, LarguraMin, LarguraMax), telaL);
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

            Acao clicada = Acao.Nenhuma;
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
                    GUI.Label(new Rect(14f, yl, interna - 28f - lValor, h), new GUIContent(l.Texto), _item);
                    if (valor != null) GUI.Label(new Rect(interna - 14f - lValor, yl, lValor, h), new GUIContent(valor), _valor);
                    GUI.color = corAntes;
                    if (mouse && l.Habilitada)
                    {
                        if (mouseMexeu && r.Contains(ev.mousePosition)) { _foco = l.Acao; _controle = false; }
                        if (GUI.Button(r, GUIContent.none, _invisivel))
                        {
                            _foco = l.Acao;
                            _controle = false;
                            if (l.Tipo == Tipo.Deslizante) ladoClicado = ev.mousePosition.x < interna / 2f ? -1 : 1; // metade esquerda diminui
                            else clicada = l.Acao;
                        }
                    }
                }
                yl += h + Espaco(l);
            }
            GUI.EndGroup();

            GUI.color = corAntes;
            GUI.matrix = matrizAntes;
            if (clicada != Acao.Nenhuma) Executar(rt, clicada);
            if (ladoClicado != 0) Ajustar(_foco, ladoClicado);
            if (fechar) Fechar("botão X");
        }

        private static float Altura(Linha l, float largura)
        {
            if (l.Tipo == Tipo.Secao) return AlturaSecao;
            if (l.Tipo != Tipo.Info) return _alturaItem;
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
