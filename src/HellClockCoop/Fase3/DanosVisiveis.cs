using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using HellClock.Character.Enemy;
using HellClock.Damage;
using HellClock.Player;
using HellClock.Skill;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Números de dano dos aliados. Postfix em DamageViewManager.SpawnDamageView: número de dano do jogador
    /// local num inimigo compartilhado vai para os outros (netId, valor, críticos), que mostram o mesmo número
    /// no inimigo deles. No boneco de treino do hub, que não é compartilhado, vai o IdBoneco e cada um mostra no
    /// próprio boneco. No modo eco, o número se repete no mesmo inimigo, com o atraso do fantasma.
    /// O dano de verdade não passa por aqui (vai pelos Golpes); no host os golpes do convidado são aplicados
    /// sem número, para não aparecer duas vezes.
    /// </summary>
    internal static class DanosVisiveis
    {
        private const float Intervalo = 0.05f;

        private static readonly List<DanosVistos.Item> Pendentes = new List<DanosVistos.Item>();
        private static readonly Queue<KeyValuePair<float, Action>> FilaEco = new Queue<KeyValuePair<float, Action>>();
        private static float _proximoEnvio;
        private static bool _reproduzindo;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;

        // ---- Visual dos números dos aliados: cor própria por aliado e um pouco menores ----

        private static ConfigEntry<bool> _mostrar;
        private static ConfigEntry<float> _tamanho;
        private static ConfigEntry<float> _opacidade;

        /// <summary>[Interface] MostrarDanoDosAliados (interruptor do menu do coop).</summary>
        internal static ConfigEntry<bool> MostrarDano => _mostrar;

        /// <summary>[Interface] TamanhoDanoDosAliados (deslizante da aba Ajustes; o uso limita a 0,3–2).</summary>
        internal static ConfigEntry<float> TamanhoDano => _tamanho;

        /// <summary>[Interface] OpacidadeDanoDosAliados (0.26.0, deslizante da aba Ajustes; o uso limita a 0,1–1).</summary>
        internal static ConfigEntry<float> OpacidadeDano => _opacidade;

        internal const float OpacidadeMinima = 0.1f; // abaixo disso o número some; para esconder há o interruptor

        /// <summary>(normal, crítico) por aliado, na ordem em que aparecem. O 1º é verde, como a seta no minimapa.</summary>
        private static readonly Color[,] Paleta =
        {
            { new Color(0.45f, 1f, 0.45f), new Color(0.80f, 1f, 0.15f) },   // verde / verde-limão
            { new Color(0.40f, 0.85f, 1f), new Color(0.15f, 0.55f, 1f) },   // ciano / azul
            { new Color(0.85f, 0.55f, 1f), new Color(1f, 0.30f, 0.85f) },   // lilás / magenta
        };

        private static readonly Dictionary<ulong, int> Cores = new Dictionary<ulong, int>();

        private static readonly AccessTools.FieldRef<DamageViewManager, Color> CorNormalRef = AccessTools.FieldRefAccess<DamageViewManager, Color>("_normalDamageColor");
        private static readonly AccessTools.FieldRef<DamageViewManager, Color> CorCriticoRef = AccessTools.FieldRefAccess<DamageViewManager, Color>("_criticalDamageColor");
        private static readonly AccessTools.FieldRef<DamageViewManager, Vector2> EscalaInicialRef = AccessTools.FieldRefAccess<DamageViewManager, Vector2>("_randomStartScale");
        private static readonly AccessTools.FieldRef<DamageViewManager, Vector2> EscalaFinalRef = AccessTools.FieldRefAccess<DamageViewManager, Vector2>("_randomFinalScale");

        internal static void Configurar(ConfigFile config)
        {
            _mostrar = config.Bind("Interface", "MostrarDanoDosAliados", true, "Mostra os números de dano dos aliados (com a cor de cada aliado).");
            _tamanho = config.Bind("Interface", "TamanhoDanoDosAliados", 0.8f, "Tamanho dos números de dano dos aliados em relação aos seus (1 = igual).");
            _opacidade = config.Bind("Interface", "OpacidadeDanoDosAliados", 0.5f, "Opacidade dos números de dano dos aliados (0,1 a 1; 1 = opaco).");
        }

        /// <summary>Índice de cor do aliado (o eco usa a chave 0). Estável durante a sessão.</summary>
        private static int CorDe(ulong chave)
        {
            if (!Cores.TryGetValue(chave, out int i))
            {
                int aliados = 0;
                foreach (ulong k in Cores.Keys) if (k != 0) aliados++;
                i = chave == 0 ? 0 : aliados % Paleta.GetLength(0); // eco = verde; aliados: verde, ciano, lilás
                Cores[chave] = i;
            }
            return i;
        }

        /// <summary>Cor (a normal dos números de dano) do aliado; o eco usa a chave 0. Usada pelo Marcador (0.17.0).</summary>
        internal static Color CorDoAliado(ulong chave) => Paleta[CorDe(chave), 0];

        /// <summary>Fim da recepção simulada: o aliado de mentira não ocupa mais uma cor da paleta.</summary>
        internal static void EsquecerCor(ulong chave) => Cores.Remove(chave);

        internal static void NumeroCriado(Collider colisor, Transform alvo, DamageInstance dano)
        {
            if (_reproduzindo || dano == null || dano.HideFromUI || dano.Ignored || alvo == null) return;
            PlayerController local = CoopRuntime.JogadorLocal();
            if (local == null || !DoJogador(dano.Attacker, local)) return;
            Fase2.TpAliado.CausouDano(); // 0.29.0: causar dano conta como combate para o TP
            float valor = (float)dano.Value;
            byte criticos = (byte)Mathf.Clamp(dano.TotalCrits, 0, 255);

            if (FxCompartilhados.AvatarEco != null)
            {
                FilaEco.Enqueue(new KeyValuePair<float, Action>(Time.unscaledTime + CoopRuntime.AtrasoEco + 0.1f, () =>
                {
                    if (alvo != null) Mostrar(colisor, alvo, valor, criticos, 0);
                }));
            }
            if (Sessao == null || !Sessao.AlguemNoMesmoAndar || Pendentes.Count >= 255) return;
            EnemyController c = alvo.GetComponent<EnemyController>();
            if (c == null) return;
            if (!InimigosCompartilhados.IdDe(c, out ushort id))
            {
                if (!(c is DummyEnemyController)) return;
                id = DanosVistos.IdBoneco;
            }
            Pendentes.Add(new DanosVistos.Item { Id = id, Valor = valor, Criticos = criticos });
        }

        // ---- Boneco de treino do hub: cada PC cria o seu (não é compartilhado); o número vai pelo IdBoneco ----

        private static DummyEnemyController _boneco;

        /// <summary>Postfix de EnemyManager.SpawnEnemy: o boneco de treino deste PC.</summary>
        internal static void BonecoNasceu(DummyEnemyController boneco)
        {
            _boneco = boneco;
            Plugin.Log.LogInfo("[Dano] Boneco de treino registrado (números dos aliados nele).");
        }

        /// <summary>Inimigo compartilhado pelo netId, ou o boneco de treino deste PC (também usado pelos golpes vistos).</summary>
        internal static EnemyController AlvoDe(ushort id)
        {
            if (id != DanosVistos.IdBoneco) return InimigosCompartilhados.PorId(id);
            return _boneco != null ? _boneco : null; // null do Unity: boneco destruído na troca de cena
        }

        private static bool DoJogador(IAttacker atacante, PlayerController local)
        {
            if (atacante == null) return false;
            if (ReferenceEquals(atacante, local)) return true;
            try { return atacante is ISkillCaster sc && ReferenceEquals(sc.SourceOrSelf, local); }
            catch (Exception) { return false; }
        }

        private static void Mostrar(Collider colisor, Transform alvo, float valor, byte criticos, ulong chave)
        {
            if (_mostrar != null && !_mostrar.Value) return;
            double inicio = Desempenho.Agora();
            DamageViewManager dv = DamageViewManager.Instance;
            int cor = CorDe(chave);
            float tam = _tamanho != null ? Mathf.Clamp(_tamanho.Value, 0.3f, 2f) : 0.8f;
            // 0.26.0: o jogo põe a FontColor (com alfa) no Text.color; o sumiço vai à parte, no CanvasGroup.alpha (IL do DamageView).
            float alfa = _opacidade != null ? Mathf.Clamp(_opacidade.Value, OpacidadeMinima, 1f) : 0.5f;
            // O jogo escolhe cor e escala dentro do SpawnDamageView: troca os campos só durante esta chamada.
            Color normal = CorNormalRef(dv), critico = CorCriticoRef(dv);
            Vector2 escIni = EscalaInicialRef(dv), escFim = EscalaFinalRef(dv);
            DamageInstance d = DamageInstance.RentDummy(valor, EDamageTypeDefinition.PHYSICAL, hideFromUI: false);
            _reproduzindo = true;
            try
            {
                d.TotalCrits = criticos;
                Color n = Paleta[cor, 0], c = Paleta[cor, 1];
                n.a = alfa;
                c.a = alfa;
                CorNormalRef(dv) = n;
                CorCriticoRef(dv) = c;
                EscalaInicialRef(dv) = escIni * tam;
                EscalaFinalRef(dv) = escFim * tam;
                dv.SpawnDamageView(colisor, alvo, alvo.position, d);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Dano] Número do aliado falhou: {(e.InnerException ?? e).Message}");
            }
            finally
            {
                CorNormalRef(dv) = normal;
                CorCriticoRef(dv) = critico;
                EscalaInicialRef(dv) = escIni;
                EscalaFinalRef(dv) = escFim;
                _reproduzindo = false;
                d.Dispose();
                if (chave != 0) Desempenho.Aplicou(Desempenho.Cosmetico.Numero, inicio); // o eco (chave 0) fica fora da conta da recepção
            }
        }

        public static void Atualizar()
        {
            float agora = Time.unscaledTime;
            if (FxCompartilhados.AvatarEco == null) FilaEco.Clear();
            while (FilaEco.Count > 0 && FilaEco.Peek().Key <= agora)
            {
                try { FilaEco.Dequeue().Value(); }
                catch (Exception e) { Plugin.Log.LogWarning($"[Dano] {(e.InnerException ?? e).Message}"); }
            }
            FilaRede.Processar(agora, MostrarRecebido); // no mesmo quadro em que chegou (a leitura da rede roda antes), até o teto
            if (Pendentes.Count == 0 || agora < _proximoEnvio) return;
            _proximoEnvio = agora + Intervalo;
            var m = new DanosVistos();
            m.Itens.AddRange(Pendentes);
            Pendentes.Clear();
            Sessao?.EnviarParaMesmoAndar(m.Serializar(), confiavel: false);
        }

        // ---- Números que chegam dos aliados: teto por quadro (0.31.1) ----

        private struct NumeroRecebido
        {
            public Par De;
            public DanosVistos.Item Item;
        }

        private static readonly FilaComTeto<NumeroRecebido> FilaRede =
            new FilaComTeto<NumeroRecebido>(Desempenho.Cosmetico.Numero, Validacao.NumerosDanoPorQuadroMaximo);
        private static readonly Action<NumeroRecebido> MostrarRecebido = MostrarDoAliado;

        /// <summary>
        /// Até a 0.31.0 cada número era criado dentro da leitura da rede, sem limite (até 255 por mensagem, 20 mensagens por
        /// segundo por aliado). Agora entra na fila e sai até o teto do quadro; o que atrasar demais é descartado.
        /// </summary>
        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            if (!de.MesmoAndar) return; // 0.21.1: números de dano só de quem está no mesmo andar (os ids de inimigo são do andar)
            if (_mostrar != null && !_mostrar.Value) return; // números dos aliados desligados no menu: nem lê a mensagem
            try
            {
                float agora = Time.unscaledTime;
                foreach (DanosVistos.Item i in DanosVistos.Desserializar(dados).Itens)
                    FilaRede.Por(agora, new NumeroRecebido { De = de, Item = i }, instantaneo: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Dano] Mensagem de {de.Nome} ignorada: {(e.InnerException ?? e).Message}");
            }
        }

        private static void MostrarDoAliado(NumeroRecebido n)
        {
            if (!n.De.MesmoAndar) return; // saiu do andar enquanto o número esperava
            try
            {
                EnemyController c = AlvoDe(n.Item.Id);
                if (c == null || !c.gameObject.activeInHierarchy) return;
                Mostrar(c.CharacterCollider, c.Transform, n.Item.Valor, n.Item.Criticos, n.De.Id.m_SteamID);
            }
            catch (Exception e) // um erro aqui não pode derrubar o resto do laço do mod
            {
                if (Time.unscaledTime < _proximoAvisoErro) return;
                _proximoAvisoErro = Time.unscaledTime + 5f;
                Plugin.Log.LogWarning($"[Dano] Número de {n.De.Nome} ignorado: {(e.InnerException ?? e).Message}");
            }
        }

        private static float _proximoAvisoErro;

        /// <summary>Saiu da sessão: o que esperava na fila não aparece mais.</summary>
        public static void Encerrar() => FilaRede.Limpar();
    }
}
