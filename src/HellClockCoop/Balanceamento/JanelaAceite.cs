using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HellClockCoop.Balanceamento
{
    /// <summary>
    /// Janela do aceite das métricas: aparece no menu principal até o jogador responder; a resposta vai para o .cfg
    /// ([Metricas] EnviarResumoAnonimo e AceitePerguntado) e a pergunta não volta.
    /// - Só sem personagem carregado (menu, telas de abertura), para o clique não mover o personagem.
    /// - Aberta, segura a entrada do jogo pelo Interface.BloqueioEntrada (desde a 0.13.1, como o menu do coop): módulo de
    ///   entrada da UI (o clique e o Enter não chegam ao menu por trás), o ouvinte do Esc da tela de saves (que abria "fechar
    ///   o jogo?") e o semáforo do InputHandler; tudo volta dois quadros depois de fechar. O EventSystem fica ligado: o
    ///   InputHandler do jogo chama EventSystem.current.IsPointerOverGameObject() todo quadro e quebra se ele for nulo.
    /// - Enter = "Enviar resumos" (o padrão), mas só depois de 3 s sem apertar Enter: quem aperta Enter sem parar para
    ///   pular a abertura não aceita sem ver a janela. N = "Não enviar". Esc (ou B no controle) fecha sem responder
    ///   (pergunta de novo no próximo início). Os botões só respondem 1,5 s depois de a janela abrir.
    /// </summary>
    internal static class JanelaAceite
    {
        private const float EsperaEnter = 3f;
        private const float EsperaClique = 1.5f; // clique para pular a abertura não responde a janela
        private const float Largura = 640f;
        private const float Margem = 24f;
        private const float AlturaBotao = 48f;

        private static bool _adiada;
        private static bool _aberta;
        private static float _enterLiberadoEm;
        private static float _cliqueLiberadoEm;
        private static int _salvos;
        private static GUIStyle _titulo, _texto, _rodape, _botao;

        /// <summary>A janela das Novidades espera esta fechar (as duas não abrem uma por cima da outra).</summary>
        internal static bool Aberta => _aberta;

        /// <summary>No Update do runtime, antes de tudo que lê teclas.</summary>
        internal static void Atualizar(bool semPersonagem)
        {
            if (!semPersonagem || _adiada || !Envio.PrecisaPerguntar)
            {
                Fechar();
                return;
            }
            if (!_aberta)
            {
                _aberta = true;
                _salvos = Envio.ResumosSalvos();
                _enterLiberadoEm = Time.unscaledTime + EsperaEnter;
                _cliqueLiberadoEm = Time.unscaledTime + EsperaClique;
                Plugin.Log.LogInfo("[Métricas] Janela do aceite aberta no menu.");
            }

            // Segura a entrada do jogo como o menu do coop (0.13.1): módulo de UI, ouvinte do Esc da tela de saves e semáforo,
            // liberados dois quadros depois de fechar. Antes só desligava o módulo, e o Esc que adiava abria "fechar o jogo?".
            Interface.BloqueioEntrada.Pedir();

            if (Teclas.Pressionou("Escape") || BotaoVoltarDoControle())
            {
                _adiada = true;
                Fechar();
                Plugin.Log.LogInfo("[Métricas] Aceite adiado; a pergunta volta no próximo início.");
                return;
            }
            if (Teclas.Pressionou("N"))
            {
                Responder(false); // recusar tem tecla, como aceitar
                return;
            }
            if (Teclas.Pressionou("Enter") || Teclas.Pressionou("NumpadEnter"))
            {
                if (Time.unscaledTime >= _enterLiberadoEm) Responder(true);
                else _enterLiberadoEm = Time.unscaledTime + EsperaEnter; // Enter cedo demais: recomeça a espera
            }
        }

        /// <summary>Quem joga só com controle não fica preso: o botão "voltar" (B/Círculo) adia, como o Esc.</summary>
        private static bool BotaoVoltarDoControle()
        {
            try
            {
                Gamepad controle = Gamepad.current;
                if (controle != null) return controle.buttonEast.wasPressedThisFrame;
            }
            catch (System.Exception) { }
            try { return Input.GetKeyDown(KeyCode.JoystickButton1); } // Input legado (o jogo usa o Rewired)
            catch (System.Exception) { return false; }
        }

        private static void Responder(bool aceitou)
        {
            Envio.Responder(aceitou);
            Fechar();
            Fase2.RunCompartilhada.Sessao?.Avisar(aceitou
                ? "Obrigado! Os resumos das runs serão enviados. Para desligar: [Metricas] EnviarResumoAnonimo = false no .cfg do mod."
                : "Combinado: nada será enviado. Para mudar: [Metricas] EnviarResumoAnonimo = true no .cfg do mod.");
        }

        /// <summary>A entrada do jogo volta sozinha dois quadros depois do último pedido (BloqueioEntrada.Atualizar).</summary>
        private static void Fechar() => _aberta = false;

        /// <summary>No OnGUI do runtime, por último (por cima do painel do coop).</summary>
        internal static void Desenhar()
        {
            if (!_aberta) return;
            if (_titulo == null)
            {
                _titulo = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
                _titulo.normal.textColor = new Color(0.96f, 0.9f, 0.82f);
                _texto = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
                _texto.normal.textColor = new Color(0.86f, 0.84f, 0.8f);
                _rodape = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleCenter };
                _rodape.normal.textColor = new Color(0.62f, 0.6f, 0.57f);
                _botao = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
            }

            // Tudo desenhado em 1080p e escalado: legível em 1440p/4K sem estourar em telas menores.
            float escala = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            Matrix4x4 matrizAntes = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(escala, escala, 1f));
            float telaL = Screen.width / escala, telaA = Screen.height / escala;
            Color corAntes = GUI.color, fundoAntes = GUI.backgroundColor;

            Retangulo(new Rect(0f, 0f, telaL, telaA), new Color(0f, 0f, 0f, 0.72f));

            float largura = Mathf.Min(Largura, telaL - 32f);
            float interna = largura - 2f * Margem;
            var titulo = new GUIContent("Hell Clock Coop: ajudar no balanceamento?");
            var texto = new GUIContent(Texto());
            var rodape = new GUIContent("Enter: enviar resumos  ·  N: não enviar  ·  Esc: responder no próximo início");
            float aTitulo = _titulo.CalcHeight(titulo, interna);
            float aTexto = _texto.CalcHeight(texto, interna);
            float aRodape = _rodape.CalcHeight(rodape, interna);
            float altura = Margem + aTitulo + 12f + aTexto + 20f + AlturaBotao + 10f + aRodape + Margem;
            var caixa = new Rect(Mathf.Round((telaL - largura) / 2f), Mathf.Round(Mathf.Max(16f, (telaA - altura) / 2f)), largura, altura);

            Retangulo(caixa, new Color(0.55f, 0.14f, 0.1f, 1f));
            Retangulo(new Rect(caixa.x + 2f, caixa.y + 2f, caixa.width - 4f, caixa.height - 4f), new Color(0.07f, 0.06f, 0.06f, 0.98f));

            float y = caixa.y + Margem;
            GUI.Label(new Rect(caixa.x + Margem, y, interna, aTitulo), titulo, _titulo);
            y += aTitulo + 12f;
            GUI.Label(new Rect(caixa.x + Margem, y, interna, aTexto), texto, _texto);
            y += aTexto + 20f;

            float lBotao = (interna - 12f) / 2f;
            bool liberado = Time.unscaledTime >= _cliqueLiberadoEm;
            GUI.enabled = liberado;
            GUI.backgroundColor = new Color(0.85f, 0.3f, 0.22f); // o padrão (Enter) em destaque
            bool enviar = GUI.Button(new Rect(caixa.x + Margem, y, lBotao, AlturaBotao), new GUIContent("Enviar resumos"), _botao);
            GUI.backgroundColor = fundoAntes;
            bool naoEnviar = GUI.Button(new Rect(caixa.x + Margem + lBotao + 12f, y, lBotao, AlturaBotao), new GUIContent("Não enviar"), _botao);
            GUI.enabled = true;
            y += AlturaBotao + 10f;
            GUI.Label(new Rect(caixa.x + Margem, y, interna, aRodape), rodape, _rodape);

            GUI.color = corAntes;
            GUI.backgroundColor = fundoAntes;
            GUI.matrix = matrizAntes;

            if (!liberado) return;
            if (enviar) Responder(true);
            else if (naoEnviar) Responder(false);
        }

        private static string Texto()
        {
            string salvos = _salvos == 1 ? " Vai junto o resumo que já está salvo aqui."
                : _salvos > 1 ? $" Vão junto os {_salvos} resumos que já estão salvos aqui." : "";
            return "O mod guarda neste computador um resumo de cada run, solo ou coop, para calibrar a dificuldade do coop. " +
                   "Posso enviar esses resumos ao autor do mod?" + salvos + "\n\n" +
                   "<b>Vai:</b> só dados da run (tempo e inimigos por andar, dano causado e recebido, mortes, quedas e reanimações, nível do personagem, " +
                   "quantos jogadores e se você era solo, host ou convidado, dificuldade, dungeon, resultado, ajustes de " +
                   "balanceamento, versões do jogo e do mod, data e hora) e o id aleatório desta instalação " +
                   "(BepInEx/HellClockCoop/instalacao.txt), que serve para pedir a exclusão dos seus resumos por issue no GitHub do projeto.\n\n" +
                   "<b>Não vai:</b> nome ou ID da Steam, arquivos, caminhos do computador ou o log.\n\n" +
                   "<b>Para onde:</b> um banco Firebase (Google) só deste mod. O IP chega ao Google no envio, como em qualquer " +
                   "conexão, mas não é gravado no resumo. Os resumos ficam guardados até o fim da calibragem do balanceamento e depois são apagados.\n\n" +
                   "Para mudar depois: BepInEx/config/br.srmackenzie.hellclockcoop.cfg, seção [Metricas], " +
                   "EnviarResumoAnonimo = true ou false.";
        }

        private static void Retangulo(Rect r, Color cor)
        {
            Color antes = GUI.color;
            GUI.color = cor;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = antes; // GUI.color tinge tudo o que vem depois (texto e botões)
        }
    }
}
