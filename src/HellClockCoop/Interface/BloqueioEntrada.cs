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
    /// - Desliga o módulo de entrada da UI do jogo (EventSystem.currentInputModule) e o
    ///   AndroidBackButtonListener da tela de saves, que lê o Esc por fora do semáforo (0.13.0).
    /// - O painel chama Pedir() a cada quadro em que está aberto; o runtime chama Atualizar() no começo do Update, que
    ///   libera tudo só dois quadros depois do último pedido (o Esc ou o clique que fechou não chega ao jogo).
    /// - Nunca usa InputHandler.Instance: o getter cria um InputHandler se não houver (como o PlayerController.Instance);
    ///   lê o campo estático _instance.
    /// </summary>
    internal static class BloqueioEntrada
    {
        private static readonly object Dono = new object(); // chave do mod no semáforo do jogo
        private static readonly List<BaseInputModule> Desligados = new List<BaseInputModule>();
        private static readonly List<Behaviour> OuvintesDoEsc = new List<Behaviour>();
        private static global::Semaphore _travado;
        private static int _ultimoPedido = -10;
        private static FieldInfo _campoInstancia;
        private static bool _campoProcurado, _avisouSemJogo;
        private static float _proximaBuscaOuvintes; // 0 = procurar já (na abertura)
        private const float IntervaloBuscaOuvintes = 0.5f;

        /// <summary>O semáforo do jogo está travado pelo mod: mouse, teclado e controle não chegam ao personagem.</summary>
        internal static bool SeguraOJogo => _travado != null;

        // ---------- Regra pura (testada no tools/codectest) ----------

        /// <summary>Libera só quando passou mais de um quadro desde o último pedido.</summary>
        internal static bool DeveLiberar(int ultimoPedido, int quadro) => quadro - ultimoPedido > 1;

        // ---------- Uso ----------

        /// <summary>Painel aberto neste quadro: trava o jogo (idempotente).</summary>
        internal static void Pedir()
        {
            _ultimoPedido = Time.frameCount;
            DesligarModulo();
            if (Time.unscaledTime >= _proximaBuscaOuvintes) DesligarOuvintesDoEsc();
            Travar();
        }

        /// <summary>No começo do Update do runtime: libera quando nenhum painel pediu no quadro anterior.</summary>
        internal static void Atualizar()
        {
            if (!DeveLiberar(_ultimoPedido, Time.frameCount)) return;
            if (_travado != null)
            {
                Soltar();
                Plugin.Log.LogInfo("[Entrada] Entrada do jogo liberada.");
            }
            _proximaBuscaOuvintes = 0f; // a próxima abertura procura de novo na hora
            if (OuvintesDoEsc.Count > 0)
            {
                foreach (Behaviour ouvinte in OuvintesDoEsc)
                {
                    if (ouvinte != null) ouvinte.enabled = true;
                }
                OuvintesDoEsc.Clear();
            }
            if (Desligados.Count == 0) return;
            foreach (BaseInputModule modulo in Desligados)
            {
                if (modulo != null) modulo.enabled = true;
            }
            Desligados.Clear();
        }

        /// <summary>
        /// O AndroidBackButtonListener da tela de saves lê o Esc direto (Input.GetKeyDown(Escape), sem passar pelo semáforo) e
        /// pede "Você realmente quer fechar o jogo?": fica desligado enquanto o painel está aberto e até a liberação (0.13.0).
        /// Procurado na abertura e de novo a cada 0,5 s enquanto travado (0.13.1): a janela do aceite abre nas telas de
        /// abertura, antes de a cena com o ouvinte carregar, e uma busca só na abertura não o achava.
        /// </summary>
        private static void DesligarOuvintesDoEsc()
        {
            _proximaBuscaOuvintes = Time.unscaledTime + IntervaloBuscaOuvintes;
            int novos = 0;
            foreach (AndroidBackButtonListener ouvinte in Resources.FindObjectsOfTypeAll<AndroidBackButtonListener>())
            {
                if (ouvinte == null || !ouvinte.gameObject.scene.IsValid() || !ouvinte.enabled) continue; // fora prefabs e os já desligados
                ouvinte.enabled = false;
                OuvintesDoEsc.Add(ouvinte);
                novos++;
            }
            if (novos > 0) Plugin.Log.LogInfo($"[Entrada] Ouvinte do Esc da tela de saves desligado ({novos}; {OuvintesDoEsc.Count} no total).");
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
            Plugin.Log.LogInfo("[Entrada] Entrada do jogo bloqueada (painel do mod aberto).");
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
}
