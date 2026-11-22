using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using HellClock.Audio;
using HellClock.Player;
using HellClock.Skill;
using HellClock.VFX;
using HellClockCoop.Avatar;
using HellClockCoop.Fase2;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Fase3
{
    /// <summary>
    /// Etapa 8 — um ouve as skills do outro.
    ///
    /// Todo som do jogo passa por dois métodos do AudioController: PlaySfx (num ponto) e PlayAndAttachSfx (preso a
    /// um personagem ou a um objeto). Quem lança: prefixo nesses dois. O som é do jogador local quando
    /// (a) vai preso a ele (ou a algo dele: SourceOrSelf), o que cobre o som de ativação preso, skills contínuas e
    /// os eventos de animação; ou (b) toca dentro de um método de skill do jogador local (contador de contexto,
    /// ligado por prefixo/finalizador nos métodos de skill que chamam o AudioController, nos Execute/ExecuteRaw de
    /// toda ActiveSkill e no GameplaySfxConfig.ActivateSfx, que toca o som de cada efeito). A lista de métodos sai
    /// do IL do jogo ao iniciar, então skills novas de uma atualização entram sozinhas.
    ///
    /// Vai só o GUID do evento do FMOD (igual nos dois jogos), onde tocar e os parâmetros; som que dura ganha um
    /// Id e um fim. Quem recebe toca pelo mesmo AudioController, 0,1 s depois (junto com o avatar e os efeitos),
    /// preso ao avatar ou à cópia do efeito, com o volume do .cfg. A cópia dos efeitos e o avatar continuam sem
    /// nenhum som próprio: quem lançou ouve só o som do próprio jogo, uma vez. No eco (F8), os próprios sons se
    /// repetem no fantasma, com o atraso dele.
    /// </summary>
    internal static class SonsAliados
    {
        private static SessaoCoop Sessao => RunCompartilhada.Sessao;

        private static bool HaQuemOuca => (Sessao != null && Sessao.AlguemNoMesmoAndar) || FxCompartilhados.AvatarEco != null;

        // ---------------- Configuração ----------------

        private static ConfigEntry<bool> _ligado;
        private static ConfigEntry<float> _volume;

        internal static bool Ligado => _ligado == null || _ligado.Value;

        internal static void Configurar(ConfigFile config)
        {
            _ligado = config.Bind("Audio", "SonsDosAliados", true, "Toca os sons das skills e dos golpes dos aliados (e do fantasma do modo eco).");
            _volume = config.Bind("Audio", "VolumeDosAliados", 0.8f, "Volume dos sons das skills dos aliados em relação aos seus (0 a 1).");
        }

        private static float Volume => _volume != null ? Mathf.Clamp01(_volume.Value) : 0.8f;

        // ---------------- Contexto: métodos de skill do jogador local ----------------

        private static int _contexto;      // > 0: dentro de um método de skill do jogador local
        private static int _audio;         // profundidade nos métodos do AudioController (só o de fora conta)
        private static bool _reproduzindo; // tocando um som que veio de fora: não captura
        private static int _dano;          // > 0: dentro do DamageDealer.PerformDamageWrapper (dor, morte: vão pelos golpes vistos e pela réplica)
        private static bool _avisouContexto;

        /// <summary>Como achar quem lança a partir de um método patcheado.</summary>
        private sealed class Resolvedor
        {
            public int[] Parametros = Array.Empty<int>();
            public FieldInfo[] Campos = Array.Empty<FieldInfo>();
            public bool Instancia;
            public bool Util => Instancia || Parametros.Length > 0 || Campos.Length > 0;
        }

        private static readonly Dictionary<MethodBase, Resolvedor> Resolvedores = new Dictionary<MethodBase, Resolvedor>();

        private static bool Candidato(Type t) =>
            typeof(ISkillCaster).IsAssignableFrom(t) || typeof(Skill).IsAssignableFrom(t)
            || t == typeof(HellClock.Damage.IAttacker) || t == typeof(SingleBulletRequest);

        private static Resolvedor Montar(MethodBase m)
        {
            var r = new Resolvedor();
            var ps = new List<int>();
            ParameterInfo[] parametros = m.GetParameters();
            for (int i = 0; i < parametros.Length; i++)
            {
                Type t = parametros[i].ParameterType;
                if (t.IsByRef) t = t.GetElementType();
                if (t != null && Candidato(t)) ps.Add(i);
            }
            r.Parametros = ps.ToArray();
            Type tipo = m.DeclaringType;
            if (!m.IsStatic && tipo != null)
            {
                r.Instancia = typeof(Skill).IsAssignableFrom(tipo) || typeof(ISkillCaster).IsAssignableFrom(tipo);
                if (!r.Instancia)
                {
                    // Máquina de estados de corrotina (ou lambda): o "this" de fora e os parâmetros capturados.
                    var campos = new List<FieldInfo>();
                    foreach (FieldInfo f in tipo.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (Candidato(f.FieldType)) campos.Add(f);
                    }
                    r.Campos = campos.ToArray();
                }
            }
            return r;
        }

        private static ISkillCaster ParaLancador(object v)
        {
            switch (v)
            {
                case ISkillCaster sc: return sc;
                case Skill s: return s.SkillCaster;
                case SingleBulletRequest req: return req.SkillCaster;
                default: return null;
            }
        }

        private static ISkillCaster Lancador(MethodBase m, object instancia, object[] args)
        {
            if (!Resolvedores.TryGetValue(m, out Resolvedor r)) Resolvedores[m] = r = Montar(m);
            if (args != null)
            {
                foreach (int i in r.Parametros)
                {
                    if (i < args.Length && ParaLancador(args[i]) is ISkillCaster c) return c;
                }
            }
            if (instancia == null) return null;
            if (r.Instancia) return ParaLancador(instancia);
            foreach (FieldInfo f in r.Campos)
            {
                if (ParaLancador(f.GetValue(instancia)) is ISkillCaster c) return c;
            }
            return null;
        }

        private static bool DoJogador(ISkillCaster c, PlayerController local)
        {
            if (c == null || local == null) return false;
            if (ReferenceEquals(c, local)) return true;
            try { return ReferenceEquals(c.SourceOrSelf, local); } // companheiros, invocações do jogador
            catch (Exception) { return false; }
        }

        /// <summary>Prefixo dos métodos de contexto. Verdadeiro quando entrou (o finalizador sai).</summary>
        internal static bool EntrarContexto(MethodBase m, object instancia, object[] args)
        {
            if (_reproduzindo || m == null || !HaQuemOuca) return false;
            double inicio = Desempenho.InicioGancho();
            try
            {
                if (!DoJogador(Lancador(m, instancia, args), CoopRuntime.JogadorLocal())) return false;
                _contexto++;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                Desempenho.FimGancho(inicio);
            }
        }

        internal static void SairContexto(bool entrou)
        {
            if (entrou && _contexto > 0) _contexto--;
        }

        internal static bool EntrarAudio() => _audio++ == 0;

        /// <summary>
        /// Prefixo/finalizador de DamageDealer.PerformDamageWrapper. O golpe roda dentro de métodos de skill (tiro, facas,
        /// corrotinas): sem isto, o som de dor e o de morte do inimigo iriam como som de skill e tocariam duas vezes em quem
        /// recebe (a dor já vai pelos GolpesVistos; a morte, pela própria morte do inimigo em cada PC).
        /// </summary>
        internal static void EntrarDano() => _dano++;

        internal static void SairDano() { if (_dano > 0) _dano--; }

        private static readonly List<SonsAliado.Parametro> ParametrosCopiados = new List<SonsAliado.Parametro>();
        private static bool _temCopia;

        /// <summary>
        /// Prefixo de PlaySfx: o jogo devolve a lista de parâmetros ao pool (que a esvazia) antes de sair, quando
        /// manageAudioEventParameters é verdadeiro; o postfix leria a lista vazia.
        /// </summary>
        internal static void CopiarParametros(List<AudioEventParameter> parametros)
        {
            ParametrosCopiados.Clear();
            _temCopia = false;
            if (_reproduzindo || _contexto <= 0 || _dano > 0 || parametros == null) return;
            foreach (AudioEventParameter p in parametros)
            {
                if (ParametrosCopiados.Count >= SonsAliado.MaximoParametros) break;
                if (Validacao.ParametroSomValido(p.Name, p.Value)) ParametrosCopiados.Add(new SonsAliado.Parametro { Nome = p.Name, Valor = p.Value });
            }
            _temCopia = true;
        }

        internal static void SairAudio()
        {
            if (_audio > 0) _audio--;
            if (_audio == 0) _temCopia = false; // cópia que o postfix não usou não passa para o próximo som
        }


        /// <summary>MoveNext da máquina de estados criada pelo método de corrotina (tipo aninhado "&lt;Metodo&gt;d__N").</summary>
        private static MethodInfo MoveNextDe(Type tipo, string metodo)
        {
            string prefixo = "<" + metodo + ">d__";
            foreach (Type t in tipo.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (t.Name.StartsWith(prefixo, StringComparison.Ordinal)) return AccessTools.Method(t, "MoveNext");
            }
            return null;
        }

        /// <summary>
        /// Patch dos métodos de contexto, um a um (um que falhe não derruba os outros).
        /// Alvo: os métodos de ContextosDeSom.Lista (prefixo e finalizador que ligam o contador de contexto).
        /// </summary>
        internal static int AplicarContextos(Harmony harmony)
        {
            var prefixo = new HarmonyMethod(typeof(SonsAliados).GetMethod(nameof(PrefixoContexto), BindingFlags.NonPublic | BindingFlags.Static));
            var finalizador = new HarmonyMethod(typeof(SonsAliados).GetMethod(nameof(FinalizadorContexto), BindingFlags.NonPublic | BindingFlags.Static));
            var alvos = new List<MethodBase>();
            int semLancador = 0, ausentes = 0;
            foreach (var c in ContextosDeSom.Lista)
            {
                Type t = AccessTools.TypeByName(c.Tipo);
                var achados = new List<MethodBase>();
                if (t != null && c.Corrotina)
                {
                    MethodInfo mn = MoveNextDe(t, c.Metodo);
                    if (mn != null) achados.Add(mn);
                }
                else if (t != null)
                {
                    foreach (MethodInfo m in AccessTools.GetDeclaredMethods(t)) if (m.Name == c.Metodo && !m.IsAbstract) achados.Add(m);
                }
                if (achados.Count == 0) { ausentes++; Plugin.Log.LogWarning($"[Sons] {c.Tipo}.{c.Metodo} não encontrado: esse som não vai aos aliados."); }
                foreach (MethodBase m in achados)
                {
                    if (Montar(m).Util) alvos.Add(m);
                    else { semLancador++; Plugin.Log.LogWarning($"[Sons] {c.Tipo}.{c.Metodo}: lançador não encontrado no método."); }
                }
            }
            int ok = 0, falhas = 0;
            foreach (MethodBase m in alvos)
            {
                try
                {
                    harmony.Patch(m, prefix: prefixo, finalizer: finalizador);
                    ok++;
                }
                catch (Exception e)
                {
                    falhas++;
                    Plugin.Log.LogWarning($"[Sons] Contexto em {m.DeclaringType?.FullName}.{m.Name} falhou: {(e.InnerException ?? e).Message}");
                }
            }
            Plugin.Log.LogInfo($"[Sons] Contexto de som em {ok} método(s) de skill ({falhas} com falha, {ausentes} ausente(s), {semLancador} sem lançador).");
            return ok;
        }

        private static void PrefixoContexto(MethodBase __originalMethod, object __instance, object[] __args, out bool __state) =>
            __state = EntrarContexto(__originalMethod, __instance, __args);

        private static void FinalizadorContexto(bool __state) => SairContexto(__state);

        // ---------------- Quem lança: captura ----------------

        private sealed class Pendente
        {
            public SonsAliado.Item Item;
            public GameObject Objeto; // ModoEfeito: o objeto em que o som foi preso (vira FxId no envio)
        }

        private sealed class Acompanhado
        {
            public ushort Id;
            public EventInstance Instancia;
            public AttachedSfx Preso;
            public float Inicio;
        }

        private static readonly List<Pendente> Pendentes = new List<Pendente>();
        private static readonly List<Acompanhado> Acompanhados = new List<Acompanhado>();
        private static readonly SonsFim FinsPendentes = new SonsFim();
        private static ushort _proximoId = 1;

        private const int PendentesMaximo = 256;
        private const int AcompanhadosMaximo = 64;

        private static ushort NovoId()
        {
            ushort id = _proximoId++;
            if (_proximoId == 0) _proximoId = 1;
            return id;
        }

        private static SonsAliado.Item NovoItem(EventReference ev, byte modo, Vector3 posicao, List<AudioEventParameter> parametros)
        {
            var i = new SonsAliado.Item { G1 = ev.Guid.Data1, G2 = ev.Guid.Data2, G3 = ev.Guid.Data3, G4 = ev.Guid.Data4, Modo = modo, Posicao = posicao };
            if (_temCopia)
            {
                i.Parametros.AddRange(ParametrosCopiados);
                _temCopia = false;
            }
            else if (parametros != null)
            {
                foreach (AudioEventParameter p in parametros)
                {
                    if (i.Parametros.Count >= SonsAliado.MaximoParametros) break;
                    if (!Validacao.ParametroSomValido(p.Name, p.Value)) continue;
                    i.Parametros.Add(new SonsAliado.Parametro { Nome = p.Name, Valor = p.Value });
                }
            }
            return i;
        }

        private static void Guardar(SonsAliado.Item i, GameObject objeto, EventInstance instancia, AttachedSfx preso, bool dura)
        {
            if (Pendentes.Count >= PendentesMaximo) return;
            if (dura)
            {
                if (Acompanhados.Count >= AcompanhadosMaximo) return; // sem como mandar o fim: não vai (quem recebe recusaria)
                i.Id = NovoId();
                Acompanhados.Add(new Acompanhado { Id = i.Id, Instancia = instancia, Preso = preso, Inicio = Time.unscaledTime });
            }
            Pendentes.Add(new Pendente { Item = i, Objeto = objeto });
        }

        /// <summary>Postfix de AudioController.PlaySfx (o de fora): som num ponto, dentro de uma skill do jogador local.</summary>
        internal static void TocouNoPonto(EventReference ev, Vector3? posicao, List<AudioEventParameter> parametros, bool umaVez, EventInstance instancia)
        {
            if (_reproduzindo || _contexto <= 0 || _dano > 0 || ev.IsNull || !HaQuemOuca) return;
            double inicio = Desempenho.InicioGancho();
            try
            {
                PlayerController local = CoopRuntime.JogadorLocal();
                if (local == null) return;
                Vector3 p = posicao ?? local.transform.position;
                Guardar(NovoItem(ev, SonsAliado.ModoPonto, p, parametros), null, instancia, null, !umaVez && instancia.isValid());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Sons] Captura falhou: {(e.InnerException ?? e).Message}");
            }
            finally
            {
                Desempenho.FimGancho(inicio);
            }
        }

        /// <summary>Postfix de AudioController.PlayAndAttachSfx(EventReference, ISkillCaster, …): som preso ao jogador local (ou a algo dele).</summary>
        internal static void TocouNoLancador(EventReference ev, ISkillCaster lancador, List<AudioEventParameter> parametros, AttachedSfx preso)
        {
            if (_reproduzindo || _dano > 0 || ev.IsNull || lancador == null || preso == null || !HaQuemOuca) return;
            double inicio = Desempenho.InicioGancho();
            try
            {
                PlayerController local = CoopRuntime.JogadorLocal();
                if (!DoJogador(lancador, local)) return;
                bool proprio = ReferenceEquals(lancador, local);
                Vector3 p = proprio ? local.transform.position : lancador.Position;
                Guardar(NovoItem(ev, proprio ? SonsAliado.ModoJogador : SonsAliado.ModoPonto, p, parametros), null, default, preso, !UmaVez(preso));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Sons] Captura falhou: {(e.InnerException ?? e).Message}");
            }
            finally
            {
                Desempenho.FimGancho(inicio);
            }
        }

        /// <summary>Postfix de AudioController.PlayAndAttachSfx(EventReference, GameObject, …): som preso a um efeito, dentro de uma skill do jogador local.</summary>
        internal static void TocouNoObjeto(EventReference ev, GameObject alvo, List<AudioEventParameter> parametros, AttachedSfx preso)
        {
            if (_reproduzindo || _contexto <= 0 || _dano > 0 || ev.IsNull || alvo == null || preso == null || !HaQuemOuca) return;
            double inicio = Desempenho.InicioGancho();
            try
            {
                PlayerController local = CoopRuntime.JogadorLocal();
                if (local == null) return;
                bool noJogador = alvo.transform.IsChildOf(local.transform);
                SonsAliado.Item i = NovoItem(ev, noJogador ? SonsAliado.ModoJogador : SonsAliado.ModoEfeito, alvo.transform.position, parametros);
                Guardar(i, noJogador ? null : alvo, default, preso, !UmaVez(preso));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Sons] Captura falhou: {(e.InnerException ?? e).Message}");
            }
            finally
            {
                Desempenho.FimGancho(inicio);
            }
        }

        // Criado na primeira vez, não na inicialização da classe: a lista de contextos é lida pelo PatchTargets ao iniciar, e um
        // campo renomeado numa atualização do jogo não pode derrubar a validação.
        private static AccessTools.FieldRef<AttachedSfx, bool> _umaVezRef;
        private static bool _umaVezTentado;

        private static bool UmaVez(AttachedSfx preso)
        {
            if (!_umaVezTentado)
            {
                _umaVezTentado = true;
                try { _umaVezRef = AccessTools.FieldRefAccess<AttachedSfx, bool>("_isOneShot"); }
                catch (Exception e) { Plugin.Log.LogWarning($"[Sons] AttachedSfx._isOneShot não encontrado: todo som preso será acompanhado ({e.Message})."); }
            }
            if (_umaVezRef == null) return false; // na dúvida, acompanha (o fim chega quando parar)
            try { return _umaVezRef(preso); }
            catch (Exception) { return false; }
        }

        private static bool Acabou(Acompanhado a, float agora)
        {
            if (agora - a.Inicio > Validacao.DuracaoSomMaxima) return true;
            try
            {
                if (a.Preso != null) return a.Preso.Stopped || !a.Preso.EventInstance.isValid();
                if (!a.Instancia.isValid()) return true;
                a.Instancia.getPlaybackState(out PLAYBACK_STATE estado);
                return estado == PLAYBACK_STATE.STOPPED;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void EnviarPendentes()
        {
            float agora = Time.unscaledTime;
            for (int k = Acompanhados.Count - 1; k >= 0; k--)
            {
                if (!Acabou(Acompanhados[k], agora)) continue;
                FinsPendentes.Ids.Add(Acompanhados[k].Id);
                Acompanhados.RemoveAt(k);
            }
            bool rede = Sessao != null && Sessao.AlguemNoMesmoAndar;
            RemotePlayerAvatar eco = FxCompartilhados.AvatarEco;
            if (Pendentes.Count > 0)
            {
                var m = new SonsAliado();
                foreach (Pendente p in Pendentes)
                {
                    if (p.Item.Modo == SonsAliado.ModoEfeito)
                    {
                        if (p.Objeto != null && FxCompartilhados.IdDoEfeito(p.Objeto, out ushort fx))
                        {
                            p.Item.FxId = fx;
                            p.Item.Posicao = p.Objeto.transform.position;
                        }
                        else
                        {
                            p.Item.Modo = SonsAliado.ModoPonto; // efeito que não vai pela rede: toca onde está
                        }
                    }
                    m.Itens.Add(p.Item);
                    if (m.Itens.Count >= SonsAliado.MaximoItens)
                    {
                        Despachar(m, rede, eco);
                        m = new SonsAliado();
                    }
                }
                if (m.Itens.Count > 0) Despachar(m, rede, eco);
                Pendentes.Clear();
            }
            if (FinsPendentes.Ids.Count > 0)
            {
                if (rede) Sessao.EnviarParaMesmoAndar(FinsPendentes.Serializar(), confiavel: true);
                if (eco != null)
                {
                    var fins = new List<ushort>(FinsPendentes.Ids);
                    NaFilaDoEco(() => { foreach (ushort id in fins) Parar(0, id); });
                }
                FinsPendentes.Ids.Clear();
            }
        }

        private static void Despachar(SonsAliado m, bool rede, RemotePlayerAvatar eco)
        {
            if (rede) Sessao.EnviarParaMesmoAndar(m.Serializar(), confiavel: true);
            if (eco != null)
            {
                var itens = new List<SonsAliado.Item>(m.Itens);
                NaFilaDoEco(() =>
                {
                    RemotePlayerAvatar a = FxCompartilhados.AvatarEco;
                    if (a == null) return;
                    foreach (SonsAliado.Item i in itens) Tocar(0, a, a.DeslocamentoMundo, i);
                });
            }
        }

        // ---------------- Quem recebe ----------------

        private sealed class Ativo
        {
            public EventInstance Instancia;
            public AttachedSfx Preso;
            public float Expira;
            public IntPtr UltimaInstancia;  // o AudioController recria a instância do som preso ao voltar do corte por distância
            public List<SonsAliado.Parametro> Parametros;
        }

        private sealed class Remetente
        {
            public readonly JanelaBonus Janela = new JanelaBonus();
            public readonly Dictionary<ushort, Ativo> Ativos = new Dictionary<ushort, Ativo>();
            public int Recusados;
            public float ProximoAvisoErro;
            public float ProximoAvisoRecusa;
        }

        private const int AtivosPorRemetenteMaximo = 64; // o mesmo teto de quem lança (AcompanhadosMaximo)
        private const int CurtosMaximo = 128;
        private const float VidaCurto = 10f;
        private const float IntervaloAvisos = 5f;

        /// <summary>
        /// Sons de uma vez só presos ao avatar ou a uma cópia: o AudioController guarda o AttachedSfx até alguém chamar Stop
        /// (o objeto do avatar não avisa), então quem recebe para cada um quando o FMOD termina, ou em 10 s.
        /// </summary>
        private static readonly List<Ativo> Curtos = new List<Ativo>();

        // ---- O evento existe neste jogo e pode tocar de um aliado? (GUID → classificação, por consulta ao FMOD) ----

        private const byte EventoRecusado = 0, EventoDeUmaVez = 1, EventoQueDura = 2;
        private const int DescricoesMaximo = 1024;
        private static readonly Dictionary<(int, int, int, int), byte> Descricoes = new Dictionary<(int, int, int, int), byte>();

        /// <summary>
        /// Só evento 3D que não é snapshot: um evento 2D (música, interface, voz) tocaria em volume cheio em qualquer lugar
        /// do mapa, e um snapshot mexe na mixagem inteira. GUID inexistente também é recusado.
        /// </summary>
        private static byte Classificar(SonsAliado.Item i)
        {
            var chave = (i.G1, i.G2, i.G3, i.G4);
            if (Descricoes.TryGetValue(chave, out byte c)) return c;
            c = EventoRecusado;
            try
            {
                var guid = new FMOD.GUID { Data1 = i.G1, Data2 = i.G2, Data3 = i.G3, Data4 = i.G4 };
                if (RuntimeManager.StudioSystem.getEventByID(guid, out EventDescription d) == FMOD.RESULT.OK && d.isValid()
                    && d.is3D(out bool tresD) == FMOD.RESULT.OK && tresD
                    && d.isSnapshot(out bool snapshot) == FMOD.RESULT.OK && !snapshot
                    && d.isOneshot(out bool umaVez) == FMOD.RESULT.OK)
                {
                    c = umaVez ? EventoDeUmaVez : EventoQueDura;
                }
            }
            catch (Exception)
            {
                // FMOD indisponível: recusa
            }
            if (Descricoes.Count >= DescricoesMaximo) Descricoes.Clear();
            Descricoes[chave] = c;
            return c;
        }

        private static float _proximoAvisoExecutar;

        private static readonly Dictionary<ulong, Remetente> Remetentes = new Dictionary<ulong, Remetente>();

        private static Remetente RemetenteDe(ulong chave)
        {
            if (!Remetentes.TryGetValue(chave, out Remetente r)) Remetentes[chave] = r = new Remetente();
            return r;
        }

        private static RemotePlayerAvatar AvatarDe(ulong steam)
        {
            if (Sessao == null || !Sessao.Pares.TryGetValue(steam, out Par p)) return null;
            return p.Avatar != null && p.Avatar.gameObject.activeInHierarchy && p.MesmoAndar ? p.Avatar : null;
        }

        private static void Tocar(ulong chave, RemotePlayerAvatar avatar, Vector3 deslocamento, SonsAliado.Item i)
        {
            if (!Ligado || avatar == null) return;
            AudioController ac = AudioController.Instance;
            if (ac == null) return;
            if (!Aceitavel(i)) return;
            Remetente r = RemetenteDe(chave);
            if (i.Id != 0 && !r.Ativos.ContainsKey(i.Id) && r.Ativos.Count >= AtivosPorRemetenteMaximo) return;
            var ev = new EventReference { Guid = new FMOD.GUID { Data1 = i.G1, Data2 = i.G2, Data3 = i.G3, Data4 = i.G4 } };
            Vector3 pos = i.Posicao + deslocamento;
            EventInstance inst = default;
            AttachedSfx preso = null;
            _reproduzindo = true;
            try
            {
                GameObject alvo = null;
                if (i.Modo == SonsAliado.ModoJogador) alvo = avatar.gameObject;
                else if (i.Modo == SonsAliado.ModoEfeito) alvo = FxCompartilhados.CopiaDe(chave, i.FxId);
                if (alvo != null)
                {
                    preso = ac.PlayAndAttachSfx(ev, alvo, null, false);
                    if (preso != null) inst = preso.EventInstance;
                }
                else
                {
                    inst = ac.PlaySfx(ev, pos, null, false, i.Id == 0);
                }
                if (!inst.isValid())
                {
                    if (preso != null) preso.Stop(true); // o AudioController guardaria a peça sem som até a próxima cena
                    return;
                }
                Aplicar(inst, i.Parametros);
            }
            finally
            {
                _reproduzindo = false;
            }
            var a = new Ativo { Instancia = inst, Preso = preso, UltimaInstancia = inst.handle, Parametros = i.Parametros };
            if (i.Id == 0)
            {
                if (preso == null) return; // de uma vez só, num ponto: o FMOD libera sozinho
                if (Curtos.Count >= CurtosMaximo) { PararAtivo(Curtos[0]); Curtos.RemoveAt(0); }
                a.Expira = Time.unscaledTime + VidaCurto;
                Curtos.Add(a);
                return;
            }
            if (r.Ativos.TryGetValue(i.Id, out Ativo antigo)) PararAtivo(antigo);
            a.Expira = Time.unscaledTime + Validacao.DuracaoSomMaxima;
            r.Ativos[i.Id] = a;
        }

        /// <summary>Evento 3D deste jogo, não snapshot; som que dura só com Id (senão nada o pararia).</summary>
        private static bool Aceitavel(SonsAliado.Item i)
        {
            byte c = Classificar(i);
            return c == EventoDeUmaVez || (c == EventoQueDura && i.Id != 0);
        }

        private static void Aplicar(EventInstance inst, List<SonsAliado.Parametro> parametros)
        {
            inst.setVolume(Volume);
            if (parametros == null) return;
            foreach (SonsAliado.Parametro p in parametros) inst.setParameterByName(p.Nome, p.Valor, false);
        }

        /// <summary>Som preso que o AudioController recriou (voltou do corte por distância): volume e parâmetros de novo.</summary>
        private static void Reaplicar(Ativo a)
        {
            if (a.Preso == null) return;
            EventInstance atual = a.Preso.EventInstance;
            if (atual.handle == a.UltimaInstancia || !atual.isValid()) return;
            a.UltimaInstancia = atual.handle;
            Aplicar(atual, a.Parametros);
        }

        private static bool CurtoAcabou(Ativo a, float agora)
        {
            if (agora > a.Expira || a.Preso == null || a.Preso.Stopped) return true;
            EventInstance atual = a.Preso.EventInstance;
            if (!atual.isValid()) return false; // cortado pela distância: o AudioController pode religar
            atual.getPlaybackState(out PLAYBACK_STATE estado);
            return estado == PLAYBACK_STATE.STOPPED;
        }

        private static void AtualizarCurtos()
        {
            if (Curtos.Count == 0) return;
            float agora = Time.unscaledTime;
            for (int k = Curtos.Count - 1; k >= 0; k--)
            {
                Ativo a = Curtos[k];
                bool acabou;
                try { acabou = CurtoAcabou(a, agora); }
                catch (Exception) { acabou = true; }
                if (acabou)
                {
                    PararAtivo(a);
                    Curtos.RemoveAt(k);
                }
                else
                {
                    try { Reaplicar(a); }
                    catch (Exception) { }
                }
            }
        }

        private static void PararAtivo(Ativo a)
        {
            try
            {
                if (a.Preso != null) a.Preso.Stop(false);
                else if (a.Instancia.isValid())
                {
                    a.Instancia.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                    a.Instancia.release();
                }
            }
            catch (Exception)
            {
                // instância já liberada pelo FMOD
            }
        }

        private static void Parar(ulong chave, ushort id)
        {
            if (!Remetentes.TryGetValue(chave, out Remetente r) || !r.Ativos.TryGetValue(id, out Ativo a)) return;
            r.Ativos.Remove(id);
            PararAtivo(a);
        }

        private static void PararDe(ulong chave)
        {
            if (!Remetentes.TryGetValue(chave, out Remetente r)) return;
            foreach (Ativo a in r.Ativos.Values) PararAtivo(a);
            r.Ativos.Clear();
        }

        private static void AtualizarAtivos()
        {
            if (Remetentes.Count == 0) return;
            float agora = Time.unscaledTime;
            List<ushort> vencidos = null;
            foreach (var kv in Remetentes)
            {
                bool temAvatar = kv.Key == 0 ? FxCompartilhados.AvatarEco != null : AvatarDe(kv.Key) != null;
                if (!temAvatar)
                {
                    foreach (Ativo a in kv.Value.Ativos.Values) PararAtivo(a);
                    kv.Value.Ativos.Clear();
                    continue;
                }
                vencidos?.Clear();
                foreach (var a in kv.Value.Ativos)
                {
                    if (agora > a.Value.Expira) (vencidos ?? (vencidos = new List<ushort>())).Add(a.Key);
                    else
                    {
                        try { Reaplicar(a.Value); }
                        catch (Exception) { }
                    }
                }
                if (vencidos == null) continue;
                foreach (ushort id in vencidos)
                {
                    PararAtivo(kv.Value.Ativos[id]);
                    kv.Value.Ativos.Remove(id);
                }
            }
        }

        // ---------------- Atraso (o som sai junto com o avatar, que mostra o passado) ----------------

        private const float AtrasoRede = 0.1f;
        private static readonly Queue<KeyValuePair<float, Action>> FilaEco = new Queue<KeyValuePair<float, Action>>();
        private static readonly Queue<KeyValuePair<float, Action>> FilaRede = new Queue<KeyValuePair<float, Action>>();

        private static void NaFilaDoEco(Action a) => FilaEco.Enqueue(new KeyValuePair<float, Action>(Time.unscaledTime + CoopRuntime.AtrasoEco + AtrasoRede, a));

        private static void NaFilaDaRede(Action a) => FilaRede.Enqueue(new KeyValuePair<float, Action>(Time.unscaledTime + AtrasoRede, a));

        private static void ProcessarFilas()
        {
            float agora = Time.unscaledTime;
            if (FxCompartilhados.AvatarEco == null)
            {
                FilaEco.Clear();
                PararDe(0);
            }
            while (FilaEco.Count > 0 && FilaEco.Peek().Key <= agora) Executar(FilaEco.Dequeue().Value);
            while (FilaRede.Count > 0 && FilaRede.Peek().Key <= agora) Executar(FilaRede.Dequeue().Value);
        }

        private static void Executar(Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                if (Time.unscaledTime < _proximoAvisoExecutar) return; // um aviso a cada 5 s, para o log não encher
                _proximoAvisoExecutar = Time.unscaledTime + IntervaloAvisos;
                Plugin.Log.LogWarning($"[Sons] {(e.InnerException ?? e).Message}");
            }
        }

        // ---------------- Laço e rede ----------------

        public static void Atualizar()
        {
            try
            {
                if (_contexto != 0 || _audio != 0 || _dano != 0)
                {
                    // Fora de qualquer método do jogo os três são 0; se não, um finalizador não rodou.
                    if (!_avisouContexto) Plugin.Log.LogWarning($"[Sons] Contexto desalinhado ({_contexto}, {_audio}, {_dano}); zerado.");
                    _avisouContexto = true;
                    _contexto = 0;
                    _audio = 0;
                    _dano = 0;
                }
                _temCopia = false;
                EnviarPendentes();
                ProcessarFilas();
                AtualizarAtivos();
                AtualizarCurtos();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Sons] Erro no laço: {(e.InnerException ?? e).Message}");
            }
        }

        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            try
            {
                if (!de.Compativel) return;
                ulong chave = de.Id.m_SteamID;
                if (dados[0] == Protocolo.TipoSonsFim)
                {
                    SonsFim f = SonsFim.Desserializar(dados);
                    NaFilaDaRede(() => { foreach (ushort id in f.Ids) Parar(chave, id); });
                    return;
                }
                if (dados[0] != Protocolo.TipoSonsAliado) return;
                SonsAliado m = SonsAliado.Desserializar(dados);
                RemotePlayerAvatar avatar = AvatarDe(chave);
                if (avatar == null) return;
                Remetente r = RemetenteDe(chave);
                float agora = Time.unscaledTime;
                Vector3 a = avatar.transform.position;
                var aceitos = new List<SonsAliado.Item>(m.Itens.Count);
                foreach (SonsAliado.Item i in m.Itens)
                {
                    if (!Validacao.SomValido(i.G1, i.G2, i.G3, i.G4, i.Posicao.x, i.Posicao.y, i.Posicao.z, a.x, a.y, a.z)
                        || !Aceitavel(i) || !Validacao.AceitarSomNaJanela(r.Janela, agora))
                    {
                        r.Recusados++;
                        continue;
                    }
                    i.Parametros.RemoveAll(p => !Validacao.ParametroSomValido(p.Nome, p.Valor));
                    aceitos.Add(i);
                }
                if (r.Recusados > 0 && agora >= r.ProximoAvisoRecusa)
                {
                    r.ProximoAvisoRecusa = agora + IntervaloAvisos;
                    Plugin.Log.LogWarning($"[Sons] {r.Recusados} som(ns) de {de.Nome} recusado(s) (fora do alcance do avatar, evento que não é 3D deste jogo, som que dura sem fim ou acima de {Validacao.SonsPorJanelaMaximo}/s).");
                    r.Recusados = 0;
                }
                if (aceitos.Count == 0) return;
                NaFilaDaRede(() =>
                {
                    RemotePlayerAvatar av = AvatarDe(chave);
                    if (av == null) return;
                    foreach (SonsAliado.Item i in aceitos) Tocar(chave, av, Vector3.zero, i);
                });
            }
            catch (Exception e)
            {
                // Mensagem malformada: um aviso a cada 5 s por aliado (o transporte entrega dezenas por quadro).
                Remetente r = RemetenteDe(de.Id.m_SteamID);
                if (Time.unscaledTime < r.ProximoAvisoErro) return;
                r.ProximoAvisoErro = Time.unscaledTime + IntervaloAvisos;
                Plugin.Log.LogWarning($"[Sons] Mensagem {dados[0]} de {de.Nome} ignorada: {(e.InnerException ?? e).Message}");
            }
        }

        public static void Encerrar()
        {
            foreach (ulong k in new List<ulong>(Remetentes.Keys)) if (k != 0) PararDe(k);
            foreach (ulong k in new List<ulong>(Remetentes.Keys)) if (k != 0) Remetentes.Remove(k);
            foreach (Ativo a in Curtos) PararAtivo(a);
            Curtos.Clear();
            FilaRede.Clear();
            Pendentes.Clear();
            Acompanhados.Clear();
            FinsPendentes.Ids.Clear();
        }
    }

    /// <summary>
    /// Lista dos métodos de contexto de som (só texto, sem tipos do jogo): o PatchTargets lê ao iniciar, antes de qualquer
    /// patch, e uma classe com campos de tipos do jogo poderia falhar ao carregar numa atualização.
    /// </summary>
    internal static class ContextosDeSom
    {
        /// <summary>
        /// Métodos de contexto: os que chamam o AudioController dentro das skills, achados no IL do 2.1.1.1
        /// (quem chama AudioController.PlaySfx/PlayAndAttachSfx no namespace HellClock.Skill), mais os Execute/ExecuteRaw
        /// (que chamam os disparos e o ProjectileSpawnerConfig, sem lançador próprio). Corrotina: o método que a cria
        /// (o patch vai no MoveNext da máquina de estados). Todos também em PatchTargets. Skills de inimigo entram na
        /// lista, mas o prefixo só conta quando o lançador é o jogador local.
        /// </summary>
        internal static readonly (string Tipo, string Metodo, bool Corrotina)[] Lista =
        {
            ("HellClock.VFX.GameplaySfxConfig", "ActivateSfx", false),          // som de cada efeito (GameplayFx.CreateFx)
            ("HellClock.Skill.ActiveSkill", "Execute", false),                  // ActiveSkillDefinition.Sfx
            ("HellClock.Skill.ActiveSkill", "ExecuteRaw", false),
            ("HellClock.Skill.ContinuousSkill", "ExecuteStartup", false),
            ("HellClock.Skill.SkillMeleeAttack", "Execute", false),             // CustomActivationSfx
            ("HellClock.Skill.SkillAddStatus", "Execute", false),
            ("HellClock.Skill.SkillShadowDash", "Execute", false),
            ("HellClock.Skill.SkillReflexes", "Execute", false),
            ("HellClock.Skill.SkillReflexes", "PlayHitSfx", false),
            ("HellClock.Skill.SkillReflexes", "RegenKnife", false),
            ("HellClock.Skill.SkillReflexes", "ThrowKnife", false),
            ("HellClock.Skill.SkillReflexes", "MarkTarget", true),
            ("HellClock.Skill.SkillSecondWind", "SecondWindCoroutine", true),
            ("HellClock.Skill.SkillSixShooter", "SetIsReloading", false),
            ("HellClock.Skill.SkillChargedAttack", "ChargeCoroutine", true),
            ("HellClock.Skill.SkillCorpseExplosion", "ExplosionCoroutine", true),
            ("HellClock.Skill.SkillHomingProjectileAttack", "FireAllProjectiles", false),
            ("HellClock.Skill.SkillParabolaProjectileAttack", "FireProjectiles", false),
            ("HellClock.Skill.SkillParabolaProjectileAttack", "FireProjectileFromWeaponOrigin", false),
            ("HellClock.Skill.SkillProjectileAttack", "FireProjectileFromWeaponOrigin", false),
            ("HellClock.Skill.SkillSpinningProjectileAttack", "FireProjectileFromWeaponOrigin", false),
            ("HellClock.Skill.SkillSlamAndSpawnDamageAreas", "JumpCoroutine", true),
            ("HellClock.Skill.SkillTargetTeleport", "TeleportCoroutine", true),
            ("HellClock.Skill.ParabolaProjectileConfig", "FireProjectile", false), // dono: parâmetro IAttacker
            ("HellClock.Skill.RangedAttack", "FireSingleBullet", false),           // SingleBulletRequest.SkillCaster
            ("HellClock.Skill.RangedAttack", "FireBulletCoroutine", true),         // campo skillCaster
            ("HellClock.Skill.Teleport", "ExecuteTeleport", true),                 // campo skillCaster
        };
    }
}
