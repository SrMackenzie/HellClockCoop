using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HellClock.GameInput;
using HellClock.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HellClockCoop.Interface
{
    /// <summary>
    /// Segura a entrada do jogo enquanto um painel do mod (menu do coop, Novidades e, desde a 0.13.1, a JanelaAceite) está
    /// aberto, para o mouse, o teclado e o controle operarem o painel sem mexer no personagem nem abrir a pausa.
    /// - Semáforo do próprio jogo: InputHandler.DisableInput (K10 Semaphore), o mesmo que o console de cheats do jogo usa.
    ///   Travado, GetActionDown/State/Up e os eventos de ação do InputHandler devolvem falso: clique de mover, skills,
    ///   poção, pausa (PauseGame), Back e as janelas do jogo.
    /// - O semáforo não segura os eixos (WASD e analógico, lidos por GetAxisRaw): o patch em InputHandler.Update zera
    ///   MovementDirection e AimDirection enquanto travado.
    /// - Desliga o módulo de entrada da UI do jogo (EventSystem.currentInputModule).
    /// - O AndroidBackButtonListener da tela de saves lê o Esc por fora do semáforo (0.13.0): o patch no Update dele não o
    ///   deixa rodar enquanto um painel pede a trava (0.30.3; antes era procurado e desligado a cada abertura, 70 a 90 ms em jogo).
    /// - O painel chama Pedir() a cada quadro em que está aberto; o runtime chama Atualizar() no começo do Update, que
    ///   libera tudo só dois quadros depois do último pedido (o Esc ou o clique que fechou não chega ao jogo).
    /// - Nunca usa InputHandler.Instance: o getter cria um InputHandler se não houver (como o PlayerController.Instance);
    ///   lê o campo estático _instance.
    /// </summary>
    internal static class BloqueioEntrada
    {
        private static readonly object Dono = new object(); // chave do mod no semáforo do jogo
        private static readonly List<BaseInputModule> Desligados = new List<BaseInputModule>();
        private static global::Semaphore _travado;
        private static int _ultimoPedido = -10;
        private static FieldInfo _campoInstancia;
        private static bool _campoProcurado, _avisouSemJogo;
        private static bool _leve; // trava leve do TP (0.29.0): sem log e sem segurar o Esc da tela de saves

        /// <summary>O semáforo do jogo está travado pelo mod: mouse, teclado e controle não chegam ao personagem.</summary>
        internal static bool SeguraOJogo => _travado != null;

        /// <summary>
        /// Um painel do mod pediu a trava completa e ela ainda não foi liberada: o ouvinte do Esc da tela de saves não roda
        /// (BloqueioEntrada_AndroidBackButtonListener_Update). Vale também sem o InputHandler do jogo (telas de abertura).
        /// </summary>
        internal static bool SeguraOEsc => DeveSegurarEsc(_leve, _ultimoPedido, Time.frameCount);

        // ---------- Regras puras (testadas no tools/codectest) ----------

        /// <summary>Libera só quando passou mais de um quadro desde o último pedido.</summary>
        internal static bool DeveLiberar(int ultimoPedido, int quadro) => quadro - ultimoPedido > 1;

        /// <summary>O Esc da tela de saves fica seguro pelo mesmo tempo da trava completa; a trava leve não o segura.</summary>
        internal static bool DeveSegurarEsc(bool leve, int ultimoPedido, int quadro) => !leve && !DeveLiberar(ultimoPedido, quadro);

        // ---------- Uso ----------

        /// <summary>Painel aberto neste quadro: trava o jogo (idempotente).</summary>
        internal static void Pedir()
        {
            _leve = false;
            _ultimoPedido = Time.frameCount;
            DesligarModulo();
            Travar();
        }

        /// <summary>
        /// Trava leve (0.29.0, TP até o aliado): mouse sobre o símbolo do painel de grupo ou escolha do aliado com a tecla. Só o
        /// semáforo e o módulo de entrada da UI, sem segurar o ouvinte do Esc da tela de saves e sem log (o mouse passa pelo
        /// símbolo muitas vezes). Um Pedir() no mesmo período continua completo.
        /// </summary>
        internal static void PedirLeve()
        {
            if (_travado == null || _ultimoPedido < Time.frameCount - 1) _leve = true; // começo da trava: leve até um Pedir()
            _ultimoPedido = Time.frameCount;
            DesligarModulo();
            Travar();
        }

        /// <summary>No começo do Update do runtime: libera quando nenhum painel pediu no quadro anterior.</summary>
        internal static void Atualizar()
        {
            if (!DeveLiberar(_ultimoPedido, Time.frameCount)) return;
            if (_travado != null)
            {
                Soltar();
                if (!_leve) Plugin.Log.LogInfo("[Entrada] Entrada do jogo liberada.");
            }
            if (Desligados.Count == 0) return;
            foreach (BaseInputModule modulo in Desligados)
            {
                if (modulo != null) modulo.enabled = true;
            }
            Desligados.Clear();
        }

        private static void DesligarModulo()
        {
            EventSystem es = EventSystem.current;
            BaseInputModule modulo = es != null ? es.currentInputModule : null;
            if (modulo != null && modulo.enabled)
            {
                modulo.enabled = false;
                Desligados.Add(modulo);
            }
        }

        private static void Travar()
        {
            InputHandler entrada = InstanciaDoJogo();
            global::Semaphore semaforo = null;
            try
            {
                if (entrada != null) semaforo = entrada.DisableInput;
            }
            catch (System.Exception e)
            {
                if (!_avisouSemJogo) Plugin.Log.LogWarning($"[Entrada] Semáforo do jogo indisponível: {e.Message}");
                _avisouSemJogo = true;
            }
            if (semaforo == _travado) return;
            Soltar(); // troca de cena com um InputHandler novo: solta o antigo e trava o atual
            if (semaforo == null) return;
            semaforo.BlockButDoNotIncrease(Dono);
            _travado = semaforo;
            if (!_leve) Plugin.Log.LogInfo("[Entrada] Entrada do jogo bloqueada (painel do mod aberto).");
        }

        private static void Soltar()
        {
            if (_travado == null) return;
            try { _travado.Release(Dono); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[Entrada] Falha ao liberar o semáforo do jogo: {e.Message}"); }
            _travado = null;
        }

        /// <summary>InputHandler do jogo sem criar um novo (lê o campo estático _instance).</summary>
        private static InputHandler InstanciaDoJogo()
        {
            if (!_campoProcurado)
            {
                _campoProcurado = true;
                _campoInstancia = AccessTools.Field(typeof(InputHandler), "_instance");
                if (_campoInstancia == null) Plugin.Log.LogWarning("[Entrada] InputHandler._instance não encontrado: o painel não segura a entrada do jogo.");
            }
            if (_campoInstancia == null) return null;
            var entrada = _campoInstancia.GetValue(null) as InputHandler;
            return entrada != null ? entrada : null; // comparação da Unity: objeto destruído vira null
        }
    }

    /// <summary>
    /// Alvo: HellClock.GameInput.InputHandler.Update() — lê os eixos (GetAxisRaw), que o semáforo DisableInput não segura.
    /// Com um painel do mod aberto, zera o movimento e a mira depois da leitura.
    /// </summary>
    [HarmonyPatch(typeof(InputHandler), "Update")]
    internal static class BloqueioEntrada_InputHandler_Update
    {
        private static bool _avisou;

        private static void Postfix(InputHandler __instance)
        {
            if (!BloqueioEntrada.SeguraOJogo) return;
            try
            {
                __instance.MovementDirection.Setter(Vector2.zero);
                __instance.AimDirection.Setter(Vector2.zero);
            }
            catch (System.Exception e)
            {
                if (!_avisou) Plugin.Log.LogWarning($"[Entrada] Não deu para zerar o movimento: {e.Message}");
                _avisou = true;
            }
        }
    }

    /// <summary>
    /// Alvo: HellClock.UI.AndroidBackButtonListener.Update() — na tela de saves, lê o Esc direto (Input.GetKeyDown(Escape), sem
    /// passar pelo semáforo) e pede "Você realmente quer fechar o jogo?" (SaveSelectionScreen.ShowCloseGamePopup). Com um painel
    /// do mod aberto (menu do coop, Novidades, janela do aceite) o método não roda, e o Esc só fecha o painel (0.30.3). Vale para
    /// o ouvinte que nascer depois de o painel abrir (a janela do aceite abre antes de a cena dele carregar).
    /// </summary>
    [HarmonyPatch(typeof(AndroidBackButtonListener), "Update")]
    internal static class BloqueioEntrada_AndroidBackButtonListener_Update
    {
        private static bool Prefix() => !BloqueioEntrada.SeguraOEsc;
    }
}
