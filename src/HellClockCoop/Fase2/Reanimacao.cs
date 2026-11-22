using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HellClock.Damage;
using HellClock.Manager;
using HellClock.Player;
using HellClock.Skill;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase2
{
    /// <summary>
    /// Reanimação do aliado caído (0.16.0, entrega 3 de 4 da etapa "aliados na tela e reanimação"). Decisões do autor
    /// em 22/11/2026:
    /// - Automática: quem está de pé fica a até 2 m do aliado caído, parado e sem usar skill (a poção não conta), e a
    ///   reanimação corre; não há tecla.
    /// - Duração: SegundosParaReanimar (padrão 3) vezes a ordem da reanimação no andar (3, 6, 9 s…), para o grupo não
    ///   virar imortal sem cobrar de novo o relógio (os −10 s da queda já cobram).
    /// - Golpe recebido por quem reanima congela o avanço por 1 s; enquanto congelado, o progresso cai 1 s por segundo.
    ///   Mover, usar skill, sair do raio ou trocar de alvo zera.
    /// - O reanimado volta com VidaAoReanimar (padrão 30%; a volta no próximo andar continua com VidaAoVoltarDoChao).
    /// Rede: quem reanima conta o progresso (só ele sabe se andou, usou skill ou levou golpe) e manda Reanimando a 5 Hz
    /// para o andar; ao terminar manda Reanimou. Cada um é dono da própria vida: o caído levanta na máquina dele, se a
    /// mensagem é do andar dele e quem a mandou está perto (Validacao.ReanimouValido). Todos contam a reanimação do andar
    /// (a próxima demora mais) e todos esquecem a queda já vista do reanimado: a próxima mostra o aviso de novo e o host
    /// volta a tirar o tempo.
    /// O modo eco não reanima nem é reanimado (o fantasma repete você, que está caído): o teste é com dois PCs.
    /// </summary>
    internal static class Reanimacao
    {
        internal const float RaioMetros = 2f;
        private const float CongelaSegundos = 1f;     // golpe: avanço congelado por 1 s
        private const float DecaiPorSegundo = 1f;     // progresso que cai, em segundos, por segundo congelado
        private const float IntervaloEnvio = 0.2f;    // Reanimando a 5 Hz
        private const float ProgressoVelho = 0.6f;    // Reanimando sem renovação some da tela depois disso
        private const float EsperaAposTerminar = 3f;  // o mesmo alvo não recomeça até o snapshot dele dizer "de pé"

        internal enum Estado : byte { Enchendo = 0, Interrompido = 1, FiqueParado = 2 }

        internal static ConfigEntry<float> SegundosBase, VidaAoReanimar;

        /// <summary>Progresso visto na tela para um caído (o seu, se é você quem reanima, ou o recebido de outro).</summary>
        internal struct Progresso
        {
            public string Quem;      // nome de quem reanima ("Você" quando é o jogador local)
            public float Fracao;
            public Estado Estado;
        }

        private struct Recebido
        {
            public string Quem;
            public float Fracao;
            public Estado Estado;
            public float Em;
        }

        private static Par _alvo;
        private static float _progresso, _congeladoAte, _proximoEnvio, _esperaAte;
        private static ulong _ultimoTerminado;
        private static Estado _estado;
        private static bool _usouSkill;
        private static int _noAndar;               // reanimações concluídas neste andar (o grupo todo)
        private static long _andarDaConta = -1;
        private static PlayerController _registrado, _registradoSkills;
        private static readonly Dictionary<ulong, Recebido> Recebidos = new Dictionary<ulong, Recebido>();

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;
        private static DungeonProgressManager Dpm => DungeonProgressManager.Instance;

        internal static void Configurar(ConfigFile config)
        {
            SegundosBase = config.Bind("Balanceamento", "SegundosParaReanimar", 3f,
                "Segundos parado ao lado do aliado caído para reanimá-lo (1 a 15). No mesmo andar, cada reanimação " +
                "seguinte demora mais: 1×, 2×, 3× esse tempo. Padrão 3.");
            VidaAoReanimar = config.Bind("Balanceamento", "VidaAoReanimar", 0.3f,
                "Fração da vida (0,05 a 1) com que o aliado reanimado levanta. Vale o valor de quem levanta. Padrão 0,3.");
        }

        internal static float FracaoAoReanimar => Mathf.Clamp(Validacao.Finito(VidaAoReanimar.Value) ? VidaAoReanimar.Value : 0.3f, 0.05f, 1f);

        /// <summary>Duração da próxima reanimação neste andar (3, 6, 9 s… com o padrão).</summary>
        internal static float DuracaoAtual => Mathf.Clamp(Validacao.Finito(SegundosBase.Value) ? SegundosBase.Value : 3f, 1f, 15f) * (_noAndar + 1);

        private static bool EmRun => Dpm != null && Dpm.RunStarted.Value && !Dpm.EndingExecuted.Value;

        // ---------------- Laço (CoopRuntime, depois do JogadorCaido) ----------------

        internal static void Atualizar(PlayerController local)
        {
            ContarAndar();
            Registrar(local);
            if (!PodeReanimar(local))
            {
                Zerar(null);
                return;
            }
            Par alvo = CaidoMaisPerto(local);
            if (alvo == null)
            {
                Zerar(null);
                return;
            }
            if (!ReferenceEquals(alvo, _alvo)) Zerar(alvo);

            float dt = Time.unscaledDeltaTime;
            bool parado = !local.MovementController.IsMoving && !_usouSkill;
            _usouSkill = false;
            if (!parado)
            {
                _progresso = 0f;
                _estado = Estado.FiqueParado;
            }
            else if (Time.unscaledTime < _congeladoAte)
            {
                _progresso = Mathf.Max(0f, _progresso - DecaiPorSegundo * dt);
                _estado = Estado.Interrompido;
            }
            else
            {
                _progresso += dt;
                _estado = Estado.Enchendo;
            }

            if (_progresso >= DuracaoAtual)
            {
                Terminar(alvo);
                return;
            }
            if (Time.unscaledTime >= _proximoEnvio)
            {
                _proximoEnvio = Time.unscaledTime + IntervaloEnvio;
                Sessao.EnviarParaMesmoAndar(new Reanimando
                {
                    Alvo = alvo.Id.m_SteamID,
                    IndiceAndar = RunCompartilhada.AndarAtual,
                    Estado = (byte)_estado,
                    Fracao = Mathf.Clamp01(_progresso / DuracaoAtual),
                }.Serializar(), confiavel: false);
            }
        }

        private static bool PodeReanimar(PlayerController local) =>
            EmRun && !JogadorCaido.Caido && Sessao != null && Sessao.EmSessao &&
            local != null && local.LifeController != null && !local.LifeController.IsDead && local.MovementController != null;

        /// <summary>O aliado caído mais perto, no seu andar e na run, a até RaioMetros (no plano do chão).</summary>
        private static Par CaidoMaisPerto(PlayerController local)
        {
            Vector3 eu = local.transform.position;
            Par melhor = null;
            float menor = RaioMetros;
            foreach (Par p in Sessao.Pares.Values)
            {
                if (!JogadorCaido.CaidoNaRun(p) || p.Avatar == null || !p.Avatar.gameObject.activeInHierarchy) continue;
                if (p.Id.m_SteamID == _ultimoTerminado && Time.unscaledTime < _esperaAte) continue;
                float d = DistanciaChao(eu, p.Avatar.transform.position);
                if (d <= menor) { menor = d; melhor = p; }
            }
            return melhor;
        }

        private static float DistanciaChao(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static void Terminar(Par alvo)
        {
            uint andar = RunCompartilhada.AndarAtual;
            byte ordem = (byte)Mathf.Min(_noAndar + 1, 255);
            Plugin.Log.LogInfo($"[Reanimação] Você levantou {alvo.Nome} ({DuracaoAtual:0} s; reanimação {ordem} do andar {andar}).");
            Sessao.EnviarParaMesmoAndar(new Reanimou { Alvo = alvo.Id.m_SteamID, IndiceAndar = andar, Ordem = ordem }.Serializar(), confiavel: true);
            _ultimoTerminado = alvo.Id.m_SteamID;
            _esperaAte = Time.unscaledTime + EsperaAposTerminar;
            Contou(alvo.Id.m_SteamID, ordem);
            JogadorCaido.EsquecerQueda(alvo.Id.m_SteamID);
            Balanceamento.Metricas.Reanimei();
            Interface.CaidoNaTela.AvisarReanimacao($"Você levantou {alvo.Nome}");
            Zerar(null);
        }

        private static void Zerar(Par alvo)
        {
            _alvo = alvo;
            _progresso = 0f;
            _congeladoAte = 0f;
            _estado = Estado.Enchendo;
            _usouSkill = false;
        }

        /// <summary>Andar novo (ou fim da run): a conta das reanimações recomeça e os progressos recebidos somem.</summary>
        private static void ContarAndar()
        {
            long andar = EmRun ? RunCompartilhada.AndarAtual : -1;
            if (andar == _andarDaConta) return;
            _andarDaConta = andar;
            _noAndar = 0;
            _ultimoTerminado = 0;
            Recebidos.Clear();
            Zerar(null);
        }

        /// <summary>
        /// Uma reanimação terminou no andar (sua ou de outro): a próxima demora mais. Fica o maior valor visto, sem pular
        /// mais de um (Reanimou repetido não conta de novo; mensagem perdida é recuperada na seguinte). True = nova.
        /// </summary>
        private static bool Contou(ulong alvo, byte ordem)
        {
            Recebidos.Remove(alvo);
            int novo = Validacao.OrdemReanimacao(ordem, _noAndar);
            if (novo <= _noAndar) return false;
            _noAndar = novo;
            return true;
        }

        /// <summary>Golpes e skills do jogador local (registra uma vez por personagem).</summary>
        private static void Registrar(PlayerController local)
        {
            if (local == null) return;
            if (!ReferenceEquals(local, _registrado) && local.LifeController != null)
            {
                _registrado = local;
                local.LifeController.OnHurt.Register((DamageInstance d) =>
                {
                    if (d != null && d.LifeDamageValue > 0f) _congeladoAte = Time.unscaledTime + CongelaSegundos;
                });
            }
            // As skills à parte: o controlador pode aparecer depois do primeiro quadro do personagem.
            if (!ReferenceEquals(local, _registradoSkills) && local.SkillsController is PlayerSkillsController skills)
            {
                _registradoSkills = local;
                skills.OnSuccessExecuteSkill.Register((ActiveSkill s) =>
                {
                    if (s != null && !ReferenceEquals(s, skills.UsePotionSkill)) _usouSkill = true;
                });
            }
        }

        // ---------------- Rede ----------------

        internal static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            if (!de.Compativel || !de.DlcOk || !de.MesmoAndar || de.Morto || !EmRun) return;
            ContarAndar();
            byte tipo = dados.Length > 0 ? dados[0] : (byte)0;
            if (tipo == Protocolo.TipoReanimando)
            {
                Reanimando m = Reanimando.Desserializar(dados);
                if (!Validacao.ReanimandoValido(m.IndiceAndar, m.Estado, m.Fracao, RunCompartilhada.AndarAtual)) return;
                // Só de você ou de um aliado caído na run: alvo inventado não ocupa memória.
                bool alvoValido = m.Alvo == sessao.Eu.m_SteamID ? JogadorCaido.Caido
                    : m.Alvo != de.Id.m_SteamID && sessao.Pares.TryGetValue(m.Alvo, out Par caido) && JogadorCaido.CaidoNaRun(caido);
                if (!alvoValido) return;
                Recebidos[m.Alvo] = new Recebido { Quem = de.Nome, Fracao = m.Fracao, Estado = (Estado)m.Estado, Em = Time.unscaledTime };
                return;
            }

            Reanimou r = Reanimou.Desserializar(dados);
            if (r.IndiceAndar != RunCompartilhada.AndarAtual || r.Alvo == de.Id.m_SteamID) return; // ninguém se levanta sozinho
            if (r.Alvo == sessao.Eu.m_SteamID)
            {
                PlayerController local = CoopRuntime.JogadorLocal();
                if (!JogadorCaido.Caido || local == null) return;
                float d = de.Avatar != null ? DistanciaChao(local.transform.position, de.Avatar.transform.position) : 3.4e38f;
                if (!Validacao.ReanimouValido(r.IndiceAndar, RunCompartilhada.AndarAtual, d, RaioMetros))
                {
                    Plugin.Log.LogWarning($"[Reanimação] {de.Nome} mandou te levantar de longe ({d:0.#} m); ignorado.");
                    return;
                }
                Plugin.Log.LogInfo($"[Reanimação] {de.Nome} te levantou: você volta com {FracaoAoReanimar:P0} da vida.");
                Contou(r.Alvo, r.Ordem);
                JogadorCaido.Reanimado();
                Interface.CaidoNaTela.AvisarReanimacao($"{de.Nome} te levantou");
                return;
            }
            // Outro aliado. O snapshot "de pé" dele pode chegar antes desta mensagem: basta ele jogar a run. Todos
            // esquecem a queda já vista (a próxima avisa de novo e o host volta a cobrar); como o alvo nunca é quem manda, um caído não
            // reabre a própria cobrança alternando Morto no snapshot.
            if (!sessao.Pares.TryGetValue(r.Alvo, out Par alvo) || !JogadorCaido.NaRun(alvo)) return;
            JogadorCaido.EsquecerQueda(r.Alvo);
            if (!Contou(r.Alvo, r.Ordem)) return; // repetida
            Plugin.Log.LogInfo($"[Reanimação] {de.Nome} levantou {alvo.Nome}.");
            Interface.CaidoNaTela.AvisarReanimacao($"{de.Nome} levantou {alvo.Nome}");
        }

        // ---------------- Tela ----------------

        /// <summary>Progresso da reanimação de um caído (SteamID), se alguém está ao lado dele agora.</summary>
        internal static bool ProgressoDe(ulong caido, out Progresso p)
        {
            p = default;
            if (_alvo != null && _alvo.Id.m_SteamID == caido)
            {
                p = new Progresso { Quem = "Você", Fracao = Mathf.Clamp01(_progresso / DuracaoAtual), Estado = _estado };
                return true;
            }
            if (Recebidos.TryGetValue(caido, out Recebido r) && Time.unscaledTime - r.Em < ProgressoVelho)
            {
                p = new Progresso { Quem = r.Quem, Fracao = r.Fracao, Estado = r.Estado };
                return true;
            }
            return false;
        }

        /// <summary>Progresso de quem está te reanimando (você caído).</summary>
        internal static bool MeuProgresso(out Progresso p)
        {
            p = default;
            return JogadorCaido.Caido && Sessao != null && Sessao.EmSessao && ProgressoDe(Sessao.Eu.m_SteamID, out p);
        }
    }
}
