using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HellClock.Player;
using HellClockCoop.Avatar;
using HellClockCoop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HellClockCoop
{
    /// <summary>
    /// Laço principal do mod (objeto próprio, DontDestroyOnLoad).
    /// - Sessão coop pela Steam, pelo menu do coop (F7 ou View/Select no controle): snapshots a 20 Hz para cada aliado.
    /// - Modo eco (F8): o mesmo caminho em loopback, com 1 s de atraso, para testar com um PC só.
    /// - F9: salva a hierarquia do personagem (diagnóstico).
    /// </summary>
    internal sealed class CoopRuntime : MonoBehaviour
    {
        private const float IntervaloSnapshot = 1f / 20f;
        private const float IntervaloAparencia = 1f;
        internal const float AtrasoEco = 1.0f;
        private static readonly Vector3 DeslocamentoEco = new Vector3(2.5f, 0f, 0f);

        public static CoopRuntime Instancia { get; private set; }

        internal static ConfigEntry<string> TeclaEco, TeclaHierarquia;
        internal static ConfigEntry<bool> MostrarNomes;

        private readonly SessaoCoop _sessao = new SessaoCoop();
        private readonly LocalPlayerSampler _amostrador = new LocalPlayerSampler();
        private readonly PlayerSnapshot _snapshotLocal = new PlayerSnapshot();
        private readonly Queue<KeyValuePair<float, byte[]>> _filaEco = new Queue<KeyValuePair<float, byte[]>>();
        private readonly List<KeyValuePair<float, string>> _avisos = new List<KeyValuePair<float, string>>();

        private float _proximaAmostra;
        private float _proximaAparencia;
        private bool _ecoAtivo;
        private RemotePlayerAvatar _avatarEco;
        private bool _hierarquiaRegistrada;
        private float _dicaAte = -1f;
        private string _avisoAtualizacao;
        private int _bytesUltimo;
        private GUIStyle _estiloCaixa, _estiloNome;

        public static void Criar(ConfigFile config)
        {
            if (Instancia != null) return;
            TeclaEco = config.Bind("Teclas", "ModoEco", "F8", "Fantasma local que repete você (teste com um PC só).");
            TeclaHierarquia = config.Bind("Teclas", "SalvarHierarquia", "F9", "Diagnóstico: salva a estrutura do personagem.");
            MostrarNomes = config.Bind("Interface", "MostrarNomesDosAliados", true, "Mostra o nome da Steam sobre a cabeça dos aliados.");
            Fase3.DanosVisiveis.Configurar(config);
            Fase3.SonsAliados.Configurar(config);
            Balanceamento.Escalonamento.Configurar(config);
            Balanceamento.Envio.Configurar(config);
            Interface.Novidades.Configurar(config);
            Interface.MenuCoop.Configurar(config);

            var go = new GameObject("HellClockCoop_Runtime");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            Instancia = go.AddComponent<CoopRuntime>();
            Instancia._sessao.Aviso += Instancia.Avisar;
            Instancia._avisoAtualizacao = LerAvisoDoAtualizador();
            Fase2.RunCompartilhada.Sessao = Instancia._sessao;
        }

        private void Update()
        {
            bool semPersonagem = JogadorLocal() == null;
            try
            {
                Desempenho.NovoQuadro(semPersonagem, LocalPlayerSampler.CenaAtual()); // fecha o quadro anterior (medidor de travadas)
            }
            catch (Exception e)
            {
                if (_errosMedidor++ < 3) Plugin.Log.LogWarning($"[Desempenho] Erro no medidor (os menus seguem): {e.Message}");
            }
            double inicio = Desempenho.Agora();
            Desempenho.NoLaco = true;
            try
            {
                AtualizarMenus(semPersonagem);
            }
            finally
            {
                Desempenho.NoLaco = false;
                Desempenho.Somar(Desempenho.Parte.Menus, inicio);
            }
        }

        private int _errosMedidor;

        private void OnApplicationFocus(bool foco) => Desempenho.Recomecar(); // alt-tab não vira travada
        private void OnApplicationPause(bool pausa) => Desempenho.Recomecar();

        private void AtualizarMenus(bool semPersonagem)
        {
            Interface.BloqueioEntrada.Atualizar(); // primeiro: libera a entrada do jogo se nenhum painel pediu no quadro anterior
            _sessao.TentarInicializar();
            Balanceamento.JanelaAceite.Atualizar(semPersonagem);
            Interface.Novidades.Atualizar(semPersonagem); // depois do aceite: abre só com ele fechado
            if (Interface.Novidades.Aberta)
            {
                Interface.MenuCoop.Fechar("Novidades aberta"); // F10 ou o item do menu: a Novidades fica sozinha na tela
                return; // aberta, as teclas do coop não disparam por trás
            }
            Interface.MenuCoop.Atualizar(this, semPersonagem);
            if (Interface.MenuCoop.Aberto) return;

            if (Teclas.Pressionou(TeclaEco.Value)) AlternarEco();
            if (Teclas.Pressionou(TeclaHierarquia.Value)) RegistrarHierarquia(forcar: true);
        }

        private void LateUpdate()
        {
            Desempenho.NoLaco = true;
            try
            {
                AtualizarLaco();
            }
            finally
            {
                Desempenho.NoLaco = false;
            }
        }

        private void AtualizarLaco()
        {
            double t = Desempenho.Agora(); // cada parte soma o próprio tempo no medidor de travadas
            PlayerController jogador = JogadorLocal();
            int cena = LocalPlayerSampler.CenaAtual();

            try
            {
                _sessao.Atualizar(jogador, cena);
                t = Desempenho.Somar(Desempenho.Parte.Rede, t);
                Fase2.RunCompartilhada.Atualizar(jogador);
                t = Desempenho.Somar(Desempenho.Parte.Run, t);
                Fase3.InimigosCompartilhados.Atualizar();
                t = Desempenho.Somar(Desempenho.Parte.Inimigos, t);
                Fase3.ObjetosCompartilhados.Atualizar();
                t = Desempenho.Somar(Desempenho.Parte.Objetos, t);
                Fase3.SantuariosCompartilhados.Atualizar();
                t = Desempenho.Somar(Desempenho.Parte.Santuarios, t);
                Fase3.PortasCompartilhadas.Atualizar();
                t = Desempenho.Somar(Desempenho.Parte.Portas, t);
                Fase3.AlvosAliados.Atualizar();
                t = Desempenho.Somar(Desempenho.Parte.Alvos, t);
                Fase3.FxCompartilhados.AvatarEco = _avatarEco;
                Fase3.FxCompartilhados.Atualizar();
                t = Desempenho.Somar(Desempenho.Parte.Fx, t);
                Fase3.DanosVisiveis.Atualizar();
                t = Desempenho.Somar(Desempenho.Parte.Danos, t);
                Fase3.SonsAliados.Atualizar();
                Fase3.GolpesVisiveis.Atualizar();
                t = Desempenho.Somar(Desempenho.Parte.Sons, t);
                Balanceamento.Escalonamento.Atualizar();
                Balanceamento.Metricas.Atualizar(jogador);
                Balanceamento.Envio.Atualizar();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Sessão] Erro no laço de rede: {e}");
            }
            t = Desempenho.Somar(Desempenho.Parte.Balanceamento, t); // (com erro, o resto do quadro cai aqui)
            try
            {
                AtualizarPersonagem(jogador);
            }
            finally
            {
                Desempenho.Somar(Desempenho.Parte.Personagem, t);
            }
        }

        private void AtualizarPersonagem(PlayerController jogador)
        {
            if (jogador == null)
            {
                // O eco continua ligado: o fantasma volta quando o personagem reaparecer (próximo save, hub ou run).
                if (_avatarEco != null) Destroy(_avatarEco.gameObject);
                _avatarEco = null;
                _filaEco.Clear();
                _sessao.DestruirAvatares();
                return;
            }
            // Troca de andar ou de cena destrói o fantasma junto com a cena (o personagem continua): recria.
            if (_ecoAtivo && _avatarEco == null) CriarFantasmaEco(jogador, "recriado depois da troca de cena");
            if (!_hierarquiaRegistrada) RegistrarHierarquia(forcar: false);
            if (_dicaAte < 0f) _dicaAte = Time.unscaledTime + 20f;

            try
            {
                AmostrarEEnviar(jogador);
            }
            catch (Exception e)
            {
                if (_errosAmostra++ < 5) Plugin.Log.LogError($"[Sessão] Erro ao amostrar/enviar o personagem: {e}");
            }
        }

        private int _errosAmostra;

        private void AmostrarEEnviar(PlayerController jogador)
        {
            float agora = Time.unscaledTime;
            bool precisaAmostrar = _ecoAtivo || (_sessao.EmSessao && _sessao.Pares.Count > 0);
            if (precisaAmostrar && agora >= _proximaAmostra)
            {
                _proximaAmostra = agora + IntervaloSnapshot;
                if (_amostrador.TentarAmostrar(jogador, _snapshotLocal))
                {
                    byte[] pacote = _snapshotLocal.Serializar();
                    _bytesUltimo = pacote.Length;
                    if (_sessao.EmSessao) _sessao.EnviarSnapshot(pacote);
                    if (_ecoAtivo) _filaEco.Enqueue(new KeyValuePair<float, byte[]>(agora + AtrasoEco, pacote));
                }
            }
            if (_sessao.EmSessao && agora >= _proximaAparencia)
            {
                _proximaAparencia = agora + IntervaloAparencia;
                _sessao.EnviarAparenciaSeMudou(jogador);
            }

            while (_filaEco.Count > 0 && _filaEco.Peek().Key <= agora)
            {
                byte[] pacote = _filaEco.Dequeue().Value;
                if (_avatarEco != null) _avatarEco.Receber(PlayerSnapshot.Desserializar(pacote));
            }
        }

        private void OnApplicationQuit()
        {
            Desempenho.Resumir("jogo fechado");
            if (_sessao.EmSessao) _sessao.Sair("jogo fechado");
        }

        internal SessaoCoop Sessao => _sessao;
        internal bool EcoAtivo => _ecoAtivo;

        // ---------- Modo eco ----------

        internal void AlternarEco()
        {
            if (_ecoAtivo)
            {
                DesligarEco("tecla " + TeclaEco.Value);
                return;
            }
            PlayerController jogador = JogadorLocal();
            if (jogador == null)
            {
                Avisar("Entre num save (hub ou run) antes de ligar o modo eco.");
                return;
            }
            if (CriarFantasmaEco(jogador, "ligado")) Avisar($"Modo eco ligado ({TeclaEco.Value} ou o menu do coop desligam; continua ligado ao trocar de andar).");
        }

        /// <summary>Cria o fantasma do eco; com erro, desliga o eco (sem tentar de novo a cada quadro).</summary>
        private bool CriarFantasmaEco(PlayerController jogador, string motivo)
        {
            try
            {
                double inicio = Desempenho.Agora();
                _avatarEco = AvatarFactory.Criar(jogador, "Eco");
                Desempenho.SomarDetalhe(Desempenho.Detalhe.CriarAvatar, inicio);
                _avatarEco.DeslocamentoMundo = DeslocamentoEco;
                _ecoAtivo = true;
                _filaEco.Clear();
                Plugin.Log.LogInfo($"[Eco] Fantasma {motivo}.");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Eco] Falha ao criar o fantasma: {e}");
                DesligarEco("erro");
                return false;
            }
        }

        private void DesligarEco(string motivo)
        {
            _ecoAtivo = false;
            _filaEco.Clear();
            if (_avatarEco != null)
            {
                Plugin.Log.LogInfo($"[Eco] Desligado ({motivo}). Snapshots recebidos: {_avatarEco.Recebidos}, fora de ordem: {_avatarEco.ForaDeOrdem}.");
                Destroy(_avatarEco.gameObject);
            }
            else Plugin.Log.LogInfo($"[Eco] Desligado ({motivo}), sem fantasma em cena.");
            _avatarEco = null;
        }

        // ---------- Diagnóstico ----------

        private void RegistrarHierarquia(bool forcar)
        {
            PlayerController jogador = JogadorLocal();
            if (jogador == null) return;
            _hierarquiaRegistrada = true;
            try
            {
                string pasta = Path.Combine(Paths.BepInExRootPath, "HellClockCoop");
                Directory.CreateDirectory(pasta);
                string arquivo = Path.Combine(pasta, "hierarquia-jogador.txt");
                if (!forcar && File.Exists(arquivo)) return;
                string texto =
                    $"Hierarquia do jogador — Hell Clock {Application.version} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"Animator corpo: {Nome(jogador.Animator)} | chicote: {Nome(jogador.LashAnimator)} | lança: {Nome(jogador.SpearAnimator)}\n" +
                    "(+ ativo, - inativo)\n\n" +
                    AvatarFactory.DescreverHierarquia(jogador.transform);
                File.WriteAllText(arquivo, texto);
                Plugin.Log.LogInfo($"[Diag] Hierarquia do jogador salva em {arquivo}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Diag] Não foi possível salvar a hierarquia: {e.Message}");
            }
        }

        private static string Nome(Animator a) => a == null ? "(nenhum)" : $"{a.name} ({a.runtimeAnimatorController?.name}, {a.layerCount} layers, {a.parameterCount} parâmetros)";

        internal static PlayerController JogadorLocal()
        {
            PlayerController p = ClassSingleton<PlayerManager>.Instance.PlayerController;
            return p != null ? p : null; // comparação da Unity: objeto destruído vira null
        }

        // ---------- Interface ----------

        /// <summary>
        /// Recado que o atualizador (preloader) deixou nesta abertura: versão nova instalada ou atualização recusada.
        /// Lido uma vez e apagado; fica no painel até 20 s depois de entrar no save.
        /// </summary>
        private static string LerAvisoDoAtualizador()
        {
            string arquivo = Path.Combine(Path.Combine(Path.Combine(Paths.BepInExRootPath, "HellClockCoop"), "atualizador"), "aviso.txt");
            try
            {
                if (!File.Exists(arquivo)) return null;
                string texto = File.ReadAllText(arquivo).Trim();
                File.Delete(arquivo);
                if (texto.Length > 300) texto = texto.Substring(0, 300);
                return texto.Length > 0 ? texto : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Atualizador] Não deu para ler o aviso da atualização: {e.Message}");
                return null;
            }
        }

        /// <summary>Estado de um aliado em poucas palavras (painel do canto e menu do coop).</summary>
        internal static string EstadoDoPar(Par p)
        {
            if (!p.HelloRecebido) return "conectando...";
            if (!p.Compativel) return "versão incompatível: " + p.Incompatibilidade;
            if (!p.DlcOk) return "DLC Cursed War diferente (não divide a run)";
            if (p.UltimoPacote < 0f) return "aguardando o personagem";
            if (Time.unscaledTime - p.UltimoPacote > 5f) return "sem sinal";
            return p.MesmoAndar ? (p.Morto ? "no mesmo andar (caído)" : "no mesmo andar") : "em outro lugar";
        }

        private void Avisar(string texto)
        {
            _avisos.Add(new KeyValuePair<float, string>(Time.unscaledTime + 6f, texto));
            if (_avisos.Count > 5) _avisos.RemoveAt(0);
        }

        private void OnGUI()
        {
            double inicio = Desempenho.Agora();
            Desempenho.NoLaco = true;
            try
            {
                DesenharInterface();
            }
            finally
            {
                Desempenho.NoLaco = false;
                Desempenho.Somar(Desempenho.Parte.Interface, inicio);
            }
        }

        private void DesenharInterface()
        {
            if (_estiloCaixa == null)
            {
                _estiloCaixa = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true };
                // (o setter de GUIStyle.padding foi removido pelo stripping do jogo; ajusta o RectOffset existente)
                _estiloCaixa.padding.left = 8; _estiloCaixa.padding.right = 8;
                _estiloCaixa.padding.top = 6; _estiloCaixa.padding.bottom = 6;
                _estiloNome = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            }

            var linhas = new List<string>();
            if (_sessao.EmSessao)
            {
                string run = _sessao.ModoConvidado ? " · acompanhando a run do host" : _sessao.ModoHost ? " · a run é sua" : "";
                linhas.Add($"Coop — {(_sessao.SouHost ? "você é o host" : "convidado")}{run} · {_sessao.Pares.Count + 1}/{SessaoCoop.MaximoJogadores} jogadores · {Interface.MenuCoop.Tecla.Value}: menu do coop");
                foreach (Par p in _sessao.Pares.Values) linhas.Add($"  • {p.Nome}: {EstadoDoPar(p)}");
                string bal = Balanceamento.Escalonamento.Resumo();
                if (bal != null) linhas.Add("  " + bal);
            }
            else if (_sessao.EstadoAtual == SessaoCoop.Estado.CriandoLobby || _sessao.EstadoAtual == SessaoCoop.Estado.EntrandoLobby)
            {
                linhas.Add("Coop: conectando...");
            }
            else if (Time.unscaledTime < _dicaAte)
            {
                linhas.Add($"Hell Clock Coop {Plugin.PluginVersion}: {Interface.MenuCoop.Tecla.Value} abre o menu do coop para jogar com amigos");
            }
            if (_avisoAtualizacao != null && (_dicaAte < 0f || Time.unscaledTime < _dicaAte)) linhas.Add("» " + _avisoAtualizacao);
            if (_ecoAtivo) linhas.Add($"Modo eco ({TeclaEco.Value}): {_bytesUltimo} bytes por snapshot, 20/s");
            for (int i = _avisos.Count - 1; i >= 0; i--)
            {
                if (_avisos[i].Key < Time.unscaledTime) _avisos.RemoveAt(i);
            }
            foreach (KeyValuePair<float, string> a in _avisos) linhas.Add("» " + a.Value);

            float yBotao = 12f;
            if (linhas.Count > 0)
            {
                string texto = string.Join("\n", linhas);
                var conteudo = new GUIContent(texto);
                float altura = _estiloCaixa.CalcHeight(conteudo, 560f);
                GUI.Box(new Rect(12, 12, 560, altura), conteudo, _estiloCaixa);
                yBotao += altura + 8f;
            }
            Interface.MenuCoop.DesenharBotaoMenu(yBotao); // só no menu (sem personagem)

            if (MostrarNomes.Value && Camera.main != null)
            {
                foreach (Par p in _sessao.Pares.Values)
                {
                    if (p.Avatar == null || !p.Avatar.gameObject.activeInHierarchy) continue;
                    Vector3 tela = Camera.main.WorldToScreenPoint(p.Avatar.transform.position + Vector3.up * 2.4f);
                    if (tela.z <= 0f) continue;
                    GUI.color = p.Morto ? new Color(1f, 0.45f, 0.45f) : Color.white;
                    GUI.Label(new Rect(tela.x - 100f, Screen.height - tela.y - 12f, 200f, 24f), p.Nome, _estiloNome);
                    GUI.color = Color.white;
                }
            }

            Interface.MenuCoop.Desenhar(this);
            Interface.Novidades.Desenhar();
            Balanceamento.JanelaAceite.Desenhar(); // por último: fica por cima do painel
        }
    }

    /// <summary>Leitura de teclas pelo Input System novo (o jogo usa), com o legado de reserva.</summary>
    internal static class Teclas
    {
        private static readonly Dictionary<string, Key> Cache = new Dictionary<string, Key>();
        private static bool _legadoIndisponivel;
        private static int _caminhoInformado; // 1 = Input System, 2 = legado (o jogo lê o teclado pelo Rewired)

        /// <summary>Nomes do Input System que no Input legado (KeyCode) têm outro nome.</summary>
        private static string NomeLegado(string nome)
        {
            switch (nome.ToLowerInvariant())
            {
                case "enter": return "Return";
                case "numpadenter": return "KeypadEnter";
                default: return nome;
            }
        }

        private static void InformarCaminho(int caminho)
        {
            if (_caminhoInformado == caminho) return;
            _caminhoInformado = caminho;
            Plugin.Log.LogInfo(caminho == 1 ? "[Teclas] Teclado lido pelo Input System." : "[Teclas] Teclado lido pelo Input legado.");
        }

        public static bool Pressionou(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return false;
            try
            {
                Keyboard teclado = Keyboard.current;
                if (teclado != null)
                {
                    if (!Cache.TryGetValue(nome, out Key k))
                    {
                        k = Enum.TryParse(nome, true, out Key lida) ? lida : Key.None;
                        Cache[nome] = k;
                        if (k == Key.None) Plugin.Log.LogWarning($"[Teclas] Tecla '{nome}' desconhecida; ajuste no arquivo de configuração.");
                    }
                    InformarCaminho(1);
                    return k != Key.None && teclado[k].wasPressedThisFrame;
                }
            }
            catch (Exception)
            {
                // Input System indisponível: tenta o legado.
            }
            if (_legadoIndisponivel) return false;
            try
            {
                InformarCaminho(2);
                return Enum.TryParse(NomeLegado(nome), true, out KeyCode codigo) && Input.GetKeyDown(codigo);
            }
            catch (InvalidOperationException)
            {
                _legadoIndisponivel = true;
                return false;
            }
        }

        /// <summary>Botão do controle pelo Input System, com o Input legado de reserva (o jogo usa o Rewired).</summary>
        public static bool Controle(Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> botao, KeyCode? legado)
        {
            try
            {
                Gamepad controle = Gamepad.current;
                if (controle != null) return botao(controle).wasPressedThisFrame;
            }
            catch (Exception) { }
            if (legado == null || _legadoIndisponivel) return false;
            try { return Input.GetKeyDown(legado.Value); }
            catch (Exception) { return false; }
        }
    }
}
