using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HellClock.Damage;
using HellClock.Player;
using HellClockCoop.Avatar;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase2
{
    /// <summary>
    /// Teleporte até um aliado (0.29.0, etapa 2 depois do painel de grupo). Decisões do autor em 23/11/2026:
    /// - Vale só na run, no mesmo andar, com você e o aliado de pé, fora de combate (Validacao.TpForaDeCombateSegundos sem
    ///   dano recebido nem causado) e fora da recarga (Validacao.TpRecargaSegundos por jogador). Valores fixos no código.
    /// - Canaliza Validacao.TpCanalizaSegundos parado; mover, levar dano, causar dano ou o aliado ficar indisponível cancela.
    /// - Aciona: tecla do .cfg ([Teclas] TeleporteAliado, T). Com um aliado, vai direto; com dois ou mais, mostra 1/2/3 nas
    ///   linhas do painel e o número escolhe (Esc ou a tecla de novo cancela). Clique no símbolo do painel vai direto. No
    ///   controle: D-pad ← (com vários, ↑/↓ escolhe, A confirma, B cancela). Durante a escolha a entrada do jogo fica presa
    ///   (BloqueioEntrada), para o número e o A não usarem skill.
    /// - Host é a autoridade: o convidado canaliza e manda PedidoTp; o host confere (Validacao.MotivoTp, com o combate do
    ///   convidado visto pelos Golpes e pela vida do snapshot), guarda a recarga e responde RespostaTp; com "ok", o convidado
    ///   teleporta até o aliado. O host decide o próprio TP pela mesma regra, sem rede.
    /// - Balanceamento: o TP serve para reagrupar, não para fugir da luta; não muda a escala nem o tempo.
    /// - Modo eco (F8): o fantasma vale como aliado só para quem joga sozinho (fora de sessão, ou host de sala vazia), para testar
    ///   (sem rede, só a regra local).
    /// - Destino: a posição do avatar do aliado, levada ao chão andável mais próximo (NavMesh, até 2 m); sem chão, não teleporta.
    /// - Visual (0.30.0, TpEfeito): contorno na cor do aliado pulsando na canalização; no TP o personagem some (0,2 s), o Warp
    ///   vem depois e ele reaparece no destino. Só neste PC.
    /// </summary>
    internal static class TpAliado
    {
        internal const ulong ChaveEco = 0;               // como no painel de grupo
        private const float EsperaResposta = 3f;         // sem resposta do host: desiste (a mensagem é confiável)
        private const float DuracaoEscolha = 5f;
        private const float IntervaloRespostaHost = 0.5f; // pedidos repetidos de um par: uma resposta a cada meio segundo
        private const int MaximoNumerados = 3;
        private const float RaioNavMesh = 2f;             // o destino (posição do aliado) precisa ter chão andável por perto

        internal static ConfigEntry<string> Tecla;

        internal enum Situacao : byte { Oculto, Pronto, Recarga, Combate, Canalizando, Aguardando }

        private static ulong _alvo;                       // canalizando ou aguardando; 0 com _ativo falso
        private static bool _canalizando, _aguardando, _ativo;
        private static float _inicio, _pedidoEm, _recargaAte, _ultimoCombate = -100f;
        private static ulong _warpAlvo;                   // 0.30.0: TP autorizado, esperando o personagem sumir (TpEfeito)
        private static float _warpEm = -1f;
        private static bool _escolhendo;
        private static float _escolhaAte;
        private static int _realce;                       // controle: índice realçado na escolha
        private static PlayerController _registrado;
        private static RemotePlayerAvatar _eco;
        private static readonly List<ulong> Alvos = new List<ulong>(); // aliados do painel na ordem dele (sem você)

        // Host: último TP autorizado e última resposta de cada jogador (SteamID; o host com o próprio id).
        private static readonly Dictionary<ulong, float> UltimoTpHost = new Dictionary<ulong, float>();
        private static readonly Dictionary<ulong, float> UltimaRespostaHost = new Dictionary<ulong, float>();
        private static readonly List<ulong> Velhas = new List<ulong>();

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;

        internal static void Configurar(ConfigFile config)
        {
            Tecla = config.Bind("Teclas", "TeleporteAliado", "T",
                "Teleporta até um aliado do mesmo andar, fora de combate (recarga de 60 s; canaliza 2 s parado). Com dois ou " +
                "mais aliados, mostra 1/2/3 no painel de grupo e o número escolhe. No controle: D-pad esquerdo.");
        }

        // ---------------- Estado para o painel ----------------

        /// <summary>Ordem dos aliados no painel (o painel chama a cada quadro): os números da escolha seguem essa ordem.</summary>
        internal static void OrdemDoPainel(List<ulong> chaves)
        {
            Alvos.Clear();
            foreach (ulong c in chaves) Alvos.Add(c);
        }

        internal static bool Escolhendo => _escolhendo;

        /// <summary>Número (1..3) do aliado na escolha, 0 sem número; realçado = o do controle.</summary>
        internal static int NumeroNaEscolha(ulong chave, out bool realcado)
        {
            realcado = false;
            if (!_escolhendo) return 0;
            int n = 0;
            foreach (ulong c in Alvos)
            {
                if (n >= MaximoNumerados) break;
                if (!AlvoDisponivel(c)) continue;
                if (c == chave)
                {
                    realcado = n == _realce;
                    return n + 1;
                }
                n++;
            }
            return 0;
        }

        /// <summary>O que o símbolo de TP da linha do aliado mostra; segundos = recarga, ou o que falta da canalização.</summary>
        internal static Situacao SituacaoDe(ulong chave, out float segundos)
        {
            segundos = 0f;
            if (_ativo && chave == _alvo)
            {
                if (_aguardando) return Situacao.Aguardando;
                segundos = Mathf.Max(0f, Validacao.TpCanalizaSegundos - (Time.unscaledTime - _inicio));
                return Situacao.Canalizando;
            }
            if (!PodeTeleportar() || !AlvoDisponivel(chave)) return Situacao.Oculto;
            float recarga = _recargaAte - Time.unscaledTime;
            if (recarga > 0f)
            {
                segundos = recarga;
                return Situacao.Recarga;
            }
            return EmCombate ? Situacao.Combate : Situacao.Pronto;
        }

        // ---------------- Regras locais ----------------

        private static bool EmRun => Reanimacao.EmRun;

        private static bool EmCombate => Time.unscaledTime - _ultimoCombate < Validacao.TpForaDeCombateSegundos;

        /// <summary>Você pode teleportar agora (sem contar recarga e combate): na run, de pé, com personagem.</summary>
        private static bool PodeTeleportar()
        {
            PlayerController local = CoopRuntime.JogadorLocal();
            return EmRun && !JogadorCaido.Caido && local != null && local.LifeController != null && !local.LifeController.IsDead
                   && local.MovementController != null;
        }

        /// <summary>O aliado está no seu andar, de pé e visível (o eco também, com o modo eco ligado).</summary>
        private static bool AlvoDisponivel(ulong chave)
        {
            if (chave == ChaveEco) return EcoValeComoAlvo();
            SessaoCoop s = Sessao;
            return s != null && s.EmSessao && s.Pares.TryGetValue(chave, out Par p) && JogadorCaido.NaRun(p) && p.MesmoAndar && !p.Morto
                   && p.Avatar != null && p.Avatar.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// O eco só vale como alvo para quem joga sozinho (teste): fora de sessão, ou host de uma sala sem ninguém (0.29.1). Com
        /// alguém na sala, o TP até ele seria decidido sem o host (no convidado) e o eco pode estar deslocado até 60 m (além das
        /// paredes e do mapa).
        /// </summary>
        private static bool EcoValeComoAlvo()
        {
            SessaoCoop s = Sessao;
            bool sozinho = s == null || !s.EmSessao || (s.SouHost && s.Pares.Count == 0);
            return sozinho && _eco != null && _eco.gameObject.activeInHierarchy;
        }

        /// <summary>Dano causado pelo jogador local (DanosVisiveis.NumeroCriado): conta como combate.</summary>
        internal static void CausouDano()
        {
            _ultimoCombate = Time.unscaledTime;
            if (_canalizando) Cancelar("você atacou");
        }

        private static void Registrar(PlayerController local)
        {
            if (local == null || ReferenceEquals(local, _registrado) || local.LifeController == null) return;
            _registrado = local;
            local.LifeController.OnHurt.Register((DamageInstance d) =>
            {
                if (d == null || d.Value <= 0) return;
                _ultimoCombate = Time.unscaledTime;
                if (_canalizando) Cancelar("você levou dano");
            });
        }

        // ---------------- Entrada (Update do CoopRuntime, com os menus do coop fechados) ----------------

        /// <summary>Tecla, controle e clique no símbolo do painel.</summary>
        internal static void LerEntrada(PlayerController jogador, RemotePlayerAvatar eco)
        {
            _eco = eco;
            if (jogador == null || Time.timeScale <= 0f) // pausa do jogo
            {
                _escolhendo = false;
                return;
            }
            Registrar(jogador);

            if (_escolhendo)
            {
                Interface.BloqueioEntrada.PedirLeve(); // o número, o A e o Esc não chegam ao jogo
                LerEscolha();
                return;
            }

            // Clique no símbolo do painel: com o mouse sobre um símbolo que responde (pronto, ou o TP em andamento, que o clique
            // cancela), a entrada do jogo fica presa (o clique não move o personagem). Cinza (recarga, combate) não prende.
            if (Interface.PainelGrupo.SimboloTpSobMouse(out ulong clicado))
            {
                Situacao sit = SituacaoDe(clicado, out float _);
                if (sit == Situacao.Pronto || sit == Situacao.Canalizando || sit == Situacao.Aguardando)
                {
                    Interface.BloqueioEntrada.PedirLeve();
                    if (Teclas.Pressionou("Mouse0"))
                    {
                        if (_ativo) Cancelar("cancelado");
                        else Pedir(clicado);
                        return;
                    }
                }
            }

            bool tecla = Teclas.Pressionou(Tecla.Value);
            bool controle = !tecla && Teclas.Controle(c => c.dpad.left, null);
            if (!tecla && !controle) return;
            if (_ativo)
            {
                Cancelar("cancelado");
                return;
            }
            int quantos = 0;
            ulong unico = 0;
            foreach (ulong c in Alvos)
            {
                if (!AlvoDisponivel(c)) continue;
                quantos++;
                unico = c;
            }
            if (quantos == 0)
            {
                Avisar("Nenhum aliado de pé no seu andar para teleportar.");
                return;
            }
            if (quantos == 1)
            {
                Pedir(unico);
                return;
            }
            // Antes de abrir a escolha (que prende a entrada do jogo): as mesmas regras do Pedir, e o painel visível (os números).
            if (!PodeComecar()) return;
            if (!Interface.PainelGrupo.Visivel)
            {
                Avisar("Mostre o painel de grupo para escolher o aliado do teleporte.");
                return;
            }
            _escolhendo = true;
            _escolhaAte = Time.unscaledTime + DuracaoEscolha;
            _realce = 0;
            Interface.BloqueioEntrada.PedirLeve();
        }

        private static void LerEscolha()
        {
            if (Time.unscaledTime > _escolhaAte || Teclas.Pressionou("Escape") || Teclas.Pressionou(Tecla.Value)
                || Teclas.Controle(c => c.buttonEast, null) || Teclas.Controle(c => c.dpad.left, null))
            {
                _escolhendo = false;
                return;
            }
            int disponiveis = 0;
            foreach (ulong c in Alvos) if (AlvoDisponivel(c) && disponiveis < MaximoNumerados) disponiveis++;
            if (disponiveis == 0)
            {
                _escolhendo = false;
                return;
            }
            if (_realce >= disponiveis) _realce = disponiveis - 1;
            if (Teclas.Controle(c => c.dpad.down, null)) _realce = (_realce + 1) % disponiveis;
            if (Teclas.Controle(c => c.dpad.up, null)) _realce = (_realce + disponiveis - 1) % disponiveis;

            int escolhido = -1;
            for (int i = 0; i < disponiveis; i++)
            {
                if (Teclas.Pressionou("Digit" + (i + 1)) || Teclas.Pressionou("Numpad" + (i + 1))) escolhido = i;
            }
            if (escolhido < 0 && Teclas.Controle(c => c.buttonSouth, null)) escolhido = _realce;
            if (escolhido < 0) return;
            int n = 0;
            foreach (ulong c in Alvos)
            {
                if (!AlvoDisponivel(c)) continue;
                if (n == escolhido)
                {
                    _escolhendo = false;
                    Pedir(c);
                    return;
                }
                n++;
            }
        }

        /// <summary>Começa a canalizar até o aliado, se as regras locais deixam (o host confere de novo no fim).</summary>
        private static void Pedir(ulong alvo)
        {
            if (_ativo || !PodeComecar()) return;
            if (!AlvoDisponivel(alvo))
            {
                Avisar("Esse aliado não está de pé no seu andar.");
                return;
            }
            _alvo = alvo;
            _ativo = _canalizando = true;
            _aguardando = false;
            _inicio = Time.unscaledTime;
        }

        /// <summary>Regras locais para começar (antes da escolha e da canalização), com o aviso da recusa.</summary>
        private static bool PodeComecar()
        {
            if (!PodeTeleportar()) return false;
            float recarga = _recargaAte - Time.unscaledTime;
            if (recarga > 0f)
            {
                Avisar($"Teleporte em recarga ({Mathf.CeilToInt(recarga)} s).");
                return false;
            }
            if (EmCombate)
            {
                Avisar("Teleporte só fora de combate.");
                return false;
            }
            return true;
        }

        private static void Cancelar(string motivo)
        {
            if (!_ativo) return;
            bool canalizando = _canalizando;
            _ativo = _canalizando = _aguardando = false;
            _alvo = 0;
            TpEfeito.Restaurar();
            if (canalizando) Avisar($"Teleporte cancelado: {motivo}.");
        }

        private static void Avisar(string texto) => Interface.CaidoNaTela.AvisarTp(texto);

        // ---------------- Laço (LateUpdate do CoopRuntime, depois da reanimação) ----------------

        internal static void Atualizar(PlayerController local)
        {
            if (_warpEm >= 0f) AtualizarWarp();
            if (!_ativo) return;
            if (!PodeTeleportar()) { Cancelar("você não pode teleportar agora"); return; }
            if (_aguardando)
            {
                if (Time.unscaledTime - _pedidoEm > EsperaResposta)
                {
                    Plugin.Log.LogWarning("[TP] O host não respondeu ao pedido de teleporte.");
                    _ativo = _aguardando = false;
                    _alvo = 0;
                    TpEfeito.Restaurar();
                    Avisar("O host não respondeu ao teleporte.");
                }
                return;
            }
            if (!AlvoDisponivel(_alvo)) { Cancelar("o aliado ficou indisponível"); return; }
            if (local.MovementController.IsMoving) { Cancelar("você se moveu"); return; }
            // Contorno na cor do aliado, pulsando cada vez mais rápido (só neste PC); parado no máximo enquanto espera o host.
            TpEfeito.Pulsar(local, Fase3.DanosVisiveis.CorDoAliado(_alvo), Time.unscaledTime - _inicio, Validacao.TpCanalizaSegundos);
            if (Time.unscaledTime - _inicio < Validacao.TpCanalizaSegundos) return;

            _canalizando = false;
            SessaoCoop s = Sessao;
            if (_alvo == ChaveEco || s == null || !s.EmSessao || s.SouHost)
            {
                // Você é a autoridade (host) ou é o eco: a mesma regra do host, sem rede.
                ulong eu = s != null && s.EmSessao ? s.Eu.m_SteamID : 1;
                byte motivo = MotivoNoHost(eu, true, !JogadorCaido.Caido, RunCompartilhada.AndarAtual, _alvo, Time.unscaledTime - _ultimoCombate);
                Resolver(_alvo, motivo, motivo == Validacao.MotivoTpOk ? Validacao.TpRecargaSegundos : RecargaNoHost(eu));
                return;
            }
            Par host = s.Host;
            if (host == null) { Cancelar("sem host"); return; }
            _aguardando = true;
            _pedidoEm = Time.unscaledTime;
            s.EnviarPara(host, new PedidoTp { Alvo = _alvo, IndiceAndar = RunCompartilhada.AndarAtual }.Serializar());
            Plugin.Log.LogInfo("[TP] Pedido de teleporte enviado ao host.");
        }

        /// <summary>
        /// Resultado (do host pela rede, ou o seu próprio como host): com "ok", o personagem some (TpEfeito) e o Warp vem
        /// TpEfeito.DuracaoSumir depois (AtualizarWarp); senão explica a recusa.
        /// </summary>
        private static void Resolver(ulong alvo, byte motivo, float recarga)
        {
            _ativo = _canalizando = _aguardando = false;
            _alvo = 0;
            TpEfeito.Restaurar();
            if (motivo != Validacao.MotivoTpOk)
            {
                if (motivo == Validacao.MotivoTpRecarga) _recargaAte = Time.unscaledTime + recarga;
                Avisar(TextoRecusa(motivo, recarga));
                Plugin.Log.LogInfo($"[TP] Teleporte recusado (motivo {motivo}).");
                return;
            }
            _recargaAte = Time.unscaledTime + Validacao.TpRecargaSegundos;
            PlayerController local = CoopRuntime.JogadorLocal();
            if (!Destino(alvo, local, out Vector3 _, out string _)) return; // confere antes de sumir: sem destino, nem some
            TpEfeito.Sumir(local);
            _warpAlvo = alvo;
            _warpEm = Time.unscaledTime + TpEfeito.DuracaoSumir;
        }

        /// <summary>Warp depois de o personagem sumir; o destino é lido de novo (o aliado pode ter andado nesses 0,2 s).</summary>
        private static void AtualizarWarp()
        {
            if (Time.timeScale <= 0f || Time.unscaledTime < _warpEm) return; // pausa: o dissolver também para
            ulong alvo = _warpAlvo;
            _warpEm = -1f;
            _warpAlvo = 0;
            PlayerController local = CoopRuntime.JogadorLocal();
            if (!AlvoDisponivel(alvo)) Avisar("Teleporte perdido: o aliado ficou indisponível.");
            if (!PodeTeleportar() || !AlvoDisponivel(alvo) || !Destino(alvo, local, out Vector3 destino, out string nome))
            {
                TpEfeito.Desfazer();
                return;
            }
            try
            {
                local.MovementController.Warp(destino);
                Plugin.Log.LogInfo($"[TP] Você teleportou até {nome}.");
                TpEfeito.Reaparecer(local, destino);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[TP] Teleporte falhou: {(e.InnerException ?? e).Message}");
                TpEfeito.Desfazer();
            }
        }

        /// <summary>Posição do aliado levada ao chão andável (NavMesh, até RaioNavMesh); avisa e devolve falso se não der.</summary>
        private static bool Destino(ulong alvo, PlayerController local, out Vector3 destino, out string nome)
        {
            if (!PosicaoDe(alvo, out destino, out nome) || local == null || local.MovementController == null)
            {
                Avisar("Teleporte perdido: o aliado sumiu da tela.");
                return false;
            }
            if (!UnityEngine.AI.NavMesh.SamplePosition(destino, out UnityEngine.AI.NavMeshHit ponto, RaioNavMesh, -1)) // -1 = todas as áreas (NavMesh.AllAreas não existe no Unity do jogo)
            {
                Avisar("Teleporte perdido: o aliado está fora do chão andável.");
                Plugin.Log.LogWarning("[TP] Destino do teleporte fora da NavMesh; ignorado.");
                return false;
            }
            destino = ponto.position;
            return true;
        }

        private static bool PosicaoDe(ulong chave, out Vector3 posicao, out string nome)
        {
            posicao = default;
            nome = null;
            if (chave == ChaveEco)
            {
                if (_eco == null) return false;
                posicao = _eco.transform.position;
                nome = "Eco";
                return true;
            }
            SessaoCoop s = Sessao;
            if (s == null || !s.Pares.TryGetValue(chave, out Par p) || p.Avatar == null || !p.Avatar.gameObject.activeInHierarchy) return false;
            posicao = p.Avatar.transform.position;
            nome = p.Nome;
            return true;
        }

        private static string TextoRecusa(byte motivo, float recarga)
        {
            switch (motivo)
            {
                case Validacao.MotivoTpOutroAndar: return "Teleporte recusado: vocês não estão no mesmo andar.";
                case Validacao.MotivoTpVoceCaido: return "Teleporte recusado: você está caído.";
                case Validacao.MotivoTpAlvoIndisponivel: return "Teleporte recusado: o aliado não está de pé no andar.";
                case Validacao.MotivoTpEmCombate: return "Teleporte recusado: só fora de combate.";
                case Validacao.MotivoTpRecarga: return $"Teleporte em recarga ({Mathf.CeilToInt(recarga)} s).";
                default: return "Teleporte recusado.";
            }
        }

        // ---------------- Host ----------------

        /// <summary>Regra do host para quem pede (id): andar, de pé, alvo no andar e de pé, combate e recarga.</summary>
        private static byte MotivoNoHost(ulong quem, bool quemNoAndar, bool quemDePe, uint andarPedido, ulong alvo, float desdeCombate)
        {
            SessaoCoop s = Sessao;
            bool alvoNoAndar, alvoDePe;
            if (alvo == ChaveEco) { alvoNoAndar = alvoDePe = EcoValeComoAlvo(); }
            else if (s != null && s.EmSessao && alvo == s.Eu.m_SteamID) // o próprio host
            {
                alvoNoAndar = CoopRuntime.JogadorLocal() != null;
                alvoDePe = !JogadorCaido.Caido;
            }
            else
            {
                Par p = null;
                bool existe = s != null && s.Pares.TryGetValue(alvo, out p) && alvo != quem;
                alvoNoAndar = existe && JogadorCaido.NaRun(p) && p.MesmoAndar;
                alvoDePe = existe && !p.Morto;
            }
            float desdeTp = UltimoTpHost.TryGetValue(quem, out float t) ? Time.unscaledTime - t : 1e9f;
            byte motivo = Validacao.MotivoTp(andarPedido, RunCompartilhada.AndarAtual, quemNoAndar && EmRun, quemDePe, alvoNoAndar, alvoDePe, desdeCombate, desdeTp);
            if (motivo == Validacao.MotivoTpOk) UltimoTpHost[quem] = Time.unscaledTime;
            return motivo;
        }

        /// <summary>
        /// Host: descarta o que não decide mais nada (último TP há mais que a recarga, resposta há mais que o intervalo), para
        /// os dicionários não crescerem numa sala pública aberta por horas. Sair e voltar não zera a recarga (ela continua aqui).
        /// </summary>
        private static void Esquecer()
        {
            float agora = Time.unscaledTime;
            Velhas.Clear();
            foreach (KeyValuePair<ulong, float> kv in UltimoTpHost) if (agora - kv.Value > Validacao.TpRecargaSegundos) Velhas.Add(kv.Key);
            foreach (ulong k in Velhas) UltimoTpHost.Remove(k);
            Velhas.Clear();
            foreach (KeyValuePair<ulong, float> kv in UltimaRespostaHost) if (agora - kv.Value > IntervaloRespostaHost) Velhas.Add(kv.Key);
            foreach (ulong k in Velhas) UltimaRespostaHost.Remove(k);
        }

        private static float RecargaNoHost(ulong quem) =>
            Validacao.RecargaTpRestante(UltimoTpHost.TryGetValue(quem, out float t) ? Time.unscaledTime - t : 1e9f);

        // ---------------- Rede ----------------

        internal static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            if (!de.Compativel || dados.Length == 0) return;
            if (dados[0] == Protocolo.TipoPedidoTp)
            {
                if (!sessao.SouHost) return;
                PedidoTp m = PedidoTp.Desserializar(dados);
                ulong quem = de.Id.m_SteamID;
                if (UltimaRespostaHost.TryGetValue(quem, out float ultima) && Time.unscaledTime - ultima < IntervaloRespostaHost) return; // repetição
                Esquecer();
                UltimaRespostaHost[quem] = Time.unscaledTime;
                bool pedinteNoAndar = JogadorCaido.NaRun(de) && de.MesmoAndar;
                // O eco (chave 0) nunca vem pela rede: o convidado decide o dele sozinho, e só fora de sessão.
                byte motivo = m.Alvo == ChaveEco ? Validacao.MotivoTpAlvoIndisponivel
                    : MotivoNoHost(quem, pedinteNoAndar, !de.Morto, m.IndiceAndar, m.Alvo, Time.unscaledTime - de.UltimoCombate);
                float recarga = motivo == Validacao.MotivoTpOk ? Validacao.TpRecargaSegundos : RecargaNoHost(quem);
                sessao.EnviarPara(de, new RespostaTp { Alvo = m.Alvo, Motivo = motivo, Recarga = recarga }.Serializar());
                if (motivo == Validacao.MotivoTpOk) Plugin.Log.LogInfo($"[TP] Teleporte de {de.Nome} autorizado.");
                else Plugin.Log.LogDebug($"[TP] Pedido de {de.Nome} recusado, motivo {motivo}."); // até 2 por segundo por par: fora do log padrão
                return;
            }
            if (dados[0] != Protocolo.TipoRespostaTp || sessao.SouHost || de != sessao.Host) return; // só o host responde
            RespostaTp r = RespostaTp.Desserializar(dados);
            if (!_aguardando || r.Alvo != _alvo)
            {
                // Resposta velha (o pedido já expirou depois de EsperaResposta): a recarga do host vale assim mesmo, sem teleportar.
                if (r.Motivo == Validacao.MotivoTpOk) _recargaAte = Time.unscaledTime + Validacao.TpRecargaSegundos;
                return;
            }
            Resolver(r.Alvo, r.Motivo, Validacao.RecargaTpRecebida(r.Recarga));
        }
    }
}
