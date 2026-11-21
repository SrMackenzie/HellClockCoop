using System.Collections.Generic;
using BepInEx.Configuration;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Menu do coop (0.9.3): gaveta à direita da tela, no lugar das teclas F6/F7 das versões anteriores.
    /// - Abre e fecha pela tecla [Teclas] MenuDoCoop (F7), pelo botão View/Select do controle ou pelo botão
    ///   "Menu do coop" abaixo do painel do canto (só no menu); fecha também por Esc, B e pelo [X].
    /// - Lista única: ALIADOS (só leitura), COOP (criar e convidar, sair com confirmação, modo eco) e AJUSTES
    ///   (nomes e números de dano dos aliados, gravados no .cfg; Novidades).
    /// - Aberto, segura a entrada do jogo (BloqueioEntrada): mouse, teclado e controle operam o menu sem mexer no
    ///   personagem nem abrir a pausa. Em jogo não escurece a tela nem pausa: o jogo continua rodando.
    /// - Se o semáforo do jogo não travar, em jogo o menu responde só a teclado e controle (o clique moveria o personagem).
    /// </summary>
    internal static class MenuCoop
    {
        internal enum Acao { Nenhuma, CriarOuConvidar, Sair, Eco, Nomes, Dano, Novidades }
        private enum Tipo { Secao, Info, Item }

        private struct Linha
        {
            public Tipo Tipo;
            public Acao Acao;
            public string Texto, Valor;
            public bool Habilitada;
        }

        private const float LarguraMin = 380f, LarguraMax = 460f;
        private const float Margem = 20f, AlturaItem = 48f, AlturaSecao = 34f, LadoBotao = 44f, LarguraValor = 120f;
        private const float EsperaConfirmarSaida = 3f;

        private static readonly Color Ferrugem = new Color(0.55f, 0.14f, 0.1f, 1f);
        private static readonly Color Creme = new Color(0.96f, 0.9f, 0.82f);

        internal static ConfigEntry<string> Tecla;

        private static bool _aberto, _semPersonagem, _controle, _rolarParaFoco;
        private static Acao _foco;
        private static float _rolagem, _confirmarSairAte = -1f;
        private static float _alturaItem = AlturaItem, _ladoX = LadoBotao; // em 1080p; crescem em tela menor (alvo real ≥ 44 px)
        private static Vector2 _mouseAntes;
        private static readonly List<Linha> Linhas = new List<Linha>();
        private static GUIStyle _titulo, _status, _aviso, _secao, _info, _item, _valor, _rodape, _botao, _invisivel;

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

        // ---------- Entrada (no Update do runtime, depois da Novidades) ----------

        internal static void Atualizar(CoopRuntime rt, bool semPersonagem)
        {
            _semPersonagem = semPersonagem;
            bool tecla = Teclas.Pressionou(Tecla.Value);
            bool view = Teclas.Controle(c => c.selectButton, KeyCode.JoystickButton6);
            if (!_aberto)
            {
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
            bool esc = Teclas.Pressionou("Escape");
            bool b = Teclas.Controle(c => c.buttonEast, KeyCode.JoystickButton1);
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
        }

        private static void Abrir(string motivo, bool peloControle)
        {
            _aberto = true;
            _controle = peloControle;
            _foco = Acao.Nenhuma; // o Montar escolhe o primeiro item disponível
            _rolagem = 0f;
            _confirmarSairAte = -1f;
            _mouseAntes = Vector2.zero; // ao reabrir, o mouse parado não puxa o foco
            BloqueioEntrada.Pedir(); // já no quadro em que abre (pelo botão, no OnGUI, o Update só viria no seguinte)
            Plugin.Log.LogInfo($"[Menu] Menu do coop aberto ({motivo}).");
        }

        internal static void Fechar(string motivo)
        {
            if (!_aberto) return;
            _aberto = false; // a entrada do jogo volta dois quadros depois (BloqueioEntrada)
            Plugin.Log.LogInfo($"[Menu] Menu do coop fechado ({motivo}).");
        }

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
            _rolarParaFoco = true;
        }

        private static bool Selecionavel(Linha l) => l.Tipo == Tipo.Item && l.Habilitada;

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

        // ---------- Conteúdo ----------

        private static void Montar(CoopRuntime rt)
        {
            Linhas.Clear();
            SessaoCoop s = rt.Sessao;
            bool conectando = s.EstadoAtual == SessaoCoop.Estado.CriandoLobby || s.EstadoAtual == SessaoCoop.Estado.EntrandoLobby;

            if (s.EmSessao && s.Pares.Count > 0)
            {
                Adicionar(Tipo.Secao, Acao.Nenhuma, "ALIADOS", null, false);
                foreach (Par p in s.Pares.Values) Adicionar(Tipo.Info, Acao.Nenhuma, p.Nome + "  ·  " + CoopRuntime.EstadoDoPar(p), null, false);
            }

            Adicionar(Tipo.Secao, Acao.Nenhuma, "COOP", null, false);
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

            Adicionar(Tipo.Secao, Acao.Nenhuma, "AJUSTES", null, false);
            Adicionar(Tipo.Item, Acao.Nomes, "Nomes dos aliados", Estado(CoopRuntime.MostrarNomes), CoopRuntime.MostrarNomes != null);
            Adicionar(Tipo.Item, Acao.Dano, "Números de dano dos aliados", Estado(Fase3.DanosVisiveis.MostrarDano), Fase3.DanosVisiveis.MostrarDano != null);
            Adicionar(Tipo.Item, Acao.Novidades, "Novidades da " + Plugin.PluginVersion, null, true);

            // Foco num item que sumiu ou ficou indisponível (ex.: saiu do coop): vai para o primeiro disponível.
            bool focoOk = false;
            var selecionavel = new bool[Linhas.Count];
            for (int i = 0; i < Linhas.Count; i++)
            {
                selecionavel[i] = Selecionavel(Linhas[i]);
                if (selecionavel[i] && Linhas[i].Acao == _foco) focoOk = true;
            }
            if (focoOk) return;
            int primeiro = ProximoFoco(-1, 1, selecionavel);
            _foco = primeiro >= 0 ? Linhas[primeiro].Acao : Acao.Nenhuma;
        }

        private static void Adicionar(Tipo tipo, Acao acao, string texto, string valor, bool habilitada) =>
            Linhas.Add(new Linha { Tipo = tipo, Acao = acao, Texto = texto, Valor = valor, Habilitada = habilitada });

        private static string Estado(ConfigEntry<bool> entrada) => entrada == null ? "—" : entrada.Value ? "LIGADO" : "DESLIGADO";

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
            string texto;
            if (s.SouHost) texto = "Você é o host  ·  " + jogadores + (s.ModoHost ? "  ·  a run é sua" : "");
            else
            {
                Par host = s.Host;
                texto = "Convidado de " + (host != null ? host.Nome : "host") + "  ·  " + jogadores + (s.ModoConvidado ? "  ·  acompanhando a run do host" : "");
            }
            string bal = Balanceamento.Escalonamento.Resumo();
            return bal != null ? texto + "\n" + bal : texto;
        }

        // ---------- Desenho (no OnGUI do runtime) ----------

        /// <summary>Botão "Menu do coop" abaixo do painel do canto: só no menu (sem personagem) e sem outra janela aberta.</summary>
        internal static void DesenharBotaoMenu(float y)
        {
            if (_aberto || !_semPersonagem || Balanceamento.JanelaAceite.Aberta || Novidades.Aberta) return;
            Estilos();
            if (GUI.Button(new Rect(12f, y, 240f, LadoBotao), new GUIContent($"Menu do coop  ({Tecla.Value})"), _botao))
                Abrir("botão do painel", false);
        }

        internal static void Desenhar(CoopRuntime rt)
        {
            if (!_aberto) return;
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

            // Cabeçalho: título, [X] e estado da sessão.
            GUI.Label(new Rect(x, y, interna - _ladoX - 8f, _ladoX), new GUIContent("COOP"), _titulo);
            bool fechar = mouse && GUI.Button(new Rect(x + interna - _ladoX, y, _ladoX, _ladoX), new GUIContent("X"), _botao);
            y += _ladoX + 6f;
            var status = new GUIContent(Status(rt.Sessao));
            float aStatus = _status.CalcHeight(status, interna);
            GUI.Label(new Rect(x, y, interna, aStatus), status, _status);
            y += aStatus + 4f;
            if (!_semPersonagem)
            {
                var aviso = new GUIContent(BloqueioEntrada.SeguraOJogo
                    ? "O jogo continua rodando; seu personagem fica parado com o menu aberto."
                    : "O jogo continua rodando. Use teclado ou controle: o clique moveria o personagem.");
                float aAviso = _aviso.CalcHeight(aviso, interna);
                GUI.Label(new Rect(x, y, interna, aAviso), aviso, _aviso);
                y += aAviso + 4f;
            }
            Retangulo(new Rect(x, y + 6f, interna, 1f), new Color(0.55f, 0.14f, 0.1f, 0.6f));
            y += 14f;

            // Rodapé com as teclas do último dispositivo usado.
            var dica = new GUIContent(_controle
                ? "Direcional: escolher  ·  A: confirmar  ·  B ou View: fechar"
                : $"Setas: escolher  ·  Enter: confirmar  ·  Esc ou {Tecla.Value}: fechar");
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
                if (l.Tipo == Tipo.Item && l.Acao == _foco) { topoFoco = aCorpo; alturaFoco = h; }
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
            GUI.BeginGroup(area, GUIContent.none, GUIStyle.none); // sobrecarga que existe no jogo (stripping)
            float yl = -_rolagem;
            foreach (Linha l in Linhas)
            {
                float h = Altura(l, interna);
                var r = new Rect(0f, yl, interna, h);
                if (l.Tipo == Tipo.Secao)
                {
                    GUI.Label(new Rect(0f, yl + 10f, interna, h - 10f), new GUIContent(l.Texto), _secao);
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
                    float lValor = l.Valor != null ? LarguraValor : 0f;
                    GUI.Label(new Rect(14f, yl, interna - 28f - lValor, h), new GUIContent(l.Texto), _item);
                    if (l.Valor != null) GUI.Label(new Rect(interna - 14f - lValor, yl, lValor, h), new GUIContent(l.Valor), _valor);
                    GUI.color = corAntes;
                    if (mouse && l.Habilitada)
                    {
                        if (mouseMexeu && r.Contains(ev.mousePosition)) { _foco = l.Acao; _controle = false; }
                        if (GUI.Button(r, GUIContent.none, _invisivel))
                        {
                            _foco = l.Acao;
                            _controle = false;
                            clicada = l.Acao;
                        }
                    }
                }
                yl += h + Espaco(l);
            }
            GUI.EndGroup();

            GUI.color = corAntes;
            GUI.matrix = matrizAntes;
            if (clicada != Acao.Nenhuma) Executar(rt, clicada);
            if (fechar) Fechar("botão X");
        }

        private static float Altura(Linha l, float largura)
        {
            if (l.Tipo == Tipo.Secao) return AlturaSecao;
            if (l.Tipo == Tipo.Item) return _alturaItem;
            return Mathf.Max(26f, _info.CalcHeight(new GUIContent(l.Texto), largura - 8f));
        }

        private static float Espaco(Linha l) => l.Tipo == Tipo.Item ? 6f : 2f;

        private static void Estilos()
        {
            if (_titulo != null) return;
            _titulo = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _titulo.normal.textColor = Creme;
            _status = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _status.normal.textColor = new Color(0.86f, 0.84f, 0.8f);
            _aviso = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _aviso.normal.textColor = new Color(0.92f, 0.5f, 0.38f);
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
