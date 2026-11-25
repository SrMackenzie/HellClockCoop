using System;
using System.Collections.Generic;
using HarmonyLib;
using HellClock.Character.Enemy;
using HellClock.Damage;
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
    /// Etapa 8 — golpes dos aliados com sangue, som de dor e efeito do tipo de dano, como no solo.
    ///
    /// O jogo só cria esses efeitos no DamageDealer.PerformDamageWrapper, depois do Hurt. O golpe do aliado chega por
    /// outro caminho (no host, LifeController.Hurt direto com os Golpes do convidado; no convidado, a vida do host),
    /// então saía mudo e sem sangue. Quem bate: postfix em CharacterEffectsController.CreateDamageFX, num inimigo
    /// compartilhado (ou no boneco de treino), com o jogador local como atacante: vai o que o jogo passou ao método.
    /// Quem recebe chama o mesmo método no mesmo inimigo. Cada PC toca uma vez cada golpe: o próprio pelo jogo, o
    /// do aliado por aqui (o Hurt direto do host não cria efeito, conferido no IL do 2.1.1.1).
    /// </summary>
    internal static class GolpesVisiveis
    {
        private const float Intervalo = 0.05f;
        private const float AtrasoRede = 0.1f;

        private static readonly List<GolpesVistos.Item> Pendentes = new List<GolpesVistos.Item>();
        private static readonly Queue<KeyValuePair<float, Action>> FilaEco = new Queue<KeyValuePair<float, Action>>();
        // 0.31.1: fila com teto por quadro. Até a 0.31.0 cada mensagem (até 120 golpes) era reproduzida inteira num quadro só.
        private static readonly FilaComTeto<GolpeRecebido> FilaRede =
            new FilaComTeto<GolpeRecebido>(Desempenho.Cosmetico.Golpe, Validacao.GolpesVistosPorQuadroMaximo);
        private static readonly Action<GolpeRecebido> ReproduzirRecebido = ReproduzirDoAliado;

        private struct GolpeRecebido
        {
            public Par De;
            public GolpesVistos.Item Item;
        }
        private static readonly Dictionary<ulong, JanelaBonus> Janelas = new Dictionary<ulong, JanelaBonus>();
        private static float _proximoEnvio;
        private static bool _reproduzindo;

        private static SessaoCoop Sessao => RunCompartilhada.Sessao;

        private static AccessTools.FieldRef<CharacterEffectsController, IDamageable> _personagemRef;
        private static bool _personagemTentado;

        /// <summary>CharacterEffectsController._character (privado), criado na primeira vez: se sumir, os golpes vistos param e o jogo segue.</summary>
        private static IDamageable PersonagemDe(CharacterEffectsController cec)
        {
            if (!_personagemTentado)
            {
                _personagemTentado = true;
                try { _personagemRef = AccessTools.FieldRefAccess<CharacterEffectsController, IDamageable>("_character"); }
                catch (Exception e) { Plugin.Log.LogWarning($"[Golpes] CharacterEffectsController._character não encontrado: golpes dos aliados sem efeito ({e.Message})."); }
            }
            return _personagemRef != null ? _personagemRef(cec) : null;
        }

        private static bool DoJogador(IAttacker atacante, PlayerController local)
        {
            if (atacante == null || local == null) return false;
            if (ReferenceEquals(atacante, local)) return true;
            try { return atacante is ISkillCaster sc && ReferenceEquals(sc.SourceOrSelf, local); }
            catch (Exception) { return false; }
        }

        /// <summary>Postfix de CharacterEffectsController.CreateDamageFX (erro aqui não pode chegar ao golpe do jogo).</summary>
        internal static void GolpeCriado(CharacterEffectsController cec, DamageHitInfo hit, DamageInstance dano, EFxOnDamage opcoes)
        {
            if (_reproduzindo || cec == null || hit == null || dano == null || dano.DamageBases == null || dano.DamageBases.Count == 0) return;
            bool rede = Sessao != null && Sessao.AlguemNoMesmoAndar;
            if (!rede && FxCompartilhados.AvatarEco == null) return;
            try { Capturar(cec, hit, dano, opcoes, rede); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Golpes] Captura falhou: {(e.InnerException ?? e).Message}"); }
        }

        private static void Capturar(CharacterEffectsController cec, DamageHitInfo hit, DamageInstance dano, EFxOnDamage opcoes, bool rede)
        {
            if (!DoJogador(dano.Attacker, CoopRuntime.JogadorLocal())) return;
            if (!(PersonagemDe(cec) is EnemyController c) || c == null) return;

            byte tipos = 0, principal = 0;
            double maior = -1.0;
            foreach (DamageInstanceBase b in dano.DamageBases)
            {
                if (b == null) continue;
                int t = (int)b.EDamageTypeDefinition;
                if (t < 0 || t > 3) continue;
                tipos |= (byte)(1 << t);
                if (b.Value > maior) { maior = b.Value; principal = (byte)t; }
            }
            if (tipos == 0) return;
            Vector3 desloc = hit.HitPosition - c.Transform.position;
            if (desloc.sqrMagnitude > Validacao.DeslocamentoGolpeMaximo * Validacao.DeslocamentoGolpeMaximo) desloc = Vector3.zero; // sem ponto de golpe
            var item = new GolpesVistos.Item
            {
                Opcoes = (byte)((int)opcoes & GolpesVistos.OpcoesValidas), Tipos = tipos, Principal = principal,
                Critico = dano.IsCritical, Direcao = hit.HitDirection, Deslocamento = desloc,
            };

            if (FxCompartilhados.AvatarEco != null)
            {
                GolpesVistos.Item copia = item;
                FilaEco.Enqueue(new KeyValuePair<float, Action>(Time.unscaledTime + CoopRuntime.AtrasoEco + AtrasoRede, () => Reproduzir(c, copia)));
            }
            if (!rede || Pendentes.Count >= GolpesVistos.MaximoItens) return;
            if (!InimigosCompartilhados.IdDe(c, out ushort id))
            {
                if (!(c is DummyEnemyController)) return;
                id = DanosVistos.IdBoneco;
            }
            item.Id = id;
            Pendentes.Add(item);
        }

        private static void Reproduzir(EnemyController c, GolpesVistos.Item i)
        {
            if (c == null || !c.gameObject.activeInHierarchy || c.LifeController.IsDead) return;
            CharacterEffectsController cec = c.CharacterEffectsController;
            if (cec == null) return;
            int opcoes = i.Opcoes;
            if (!SonsAliados.Ligado) opcoes &= ~(int)EFxOnDamage.PlayDamageTypeHitSfx;
            DamageInstance d = DamageInstance.RentDummy(1.0, (EDamageTypeDefinition)i.Principal, hideFromUI: true);
            DamageHitInfo h = DamageHitInfo.Rent(i.Direcao, 0f, c.Transform.position + i.Deslocamento);
            _reproduzindo = true;
            try
            {
                d.TotalCrits = i.Critico ? 1 : 0;
                for (int t = 0; t <= 3; t++)
                {
                    if (t == i.Principal || (i.Tipos & (1 << t)) == 0) continue;
                    d.DamageBases.Add(DamageInstanceBase.Rent(0.01, 0.01, (EDamageTypeDefinition)t, 0));
                }
                cec.CreateDamageFX(h, d, (EFxOnDamage)opcoes, false);
            }
            finally
            {
                _reproduzindo = false;
                h.Dispose();
                d.Dispose();
            }
        }

        public static void Atualizar()
        {
            float agora = Time.unscaledTime;
            if (FxCompartilhados.AvatarEco == null) FilaEco.Clear();
            while (FilaEco.Count > 0 && FilaEco.Peek().Key <= agora) Executar(FilaEco.Dequeue().Value);
            FilaRede.Processar(agora, ReproduzirRecebido);
            if (Pendentes.Count == 0 || agora < _proximoEnvio) return;
            _proximoEnvio = agora + Intervalo;
            var m = new GolpesVistos();
            m.Itens.AddRange(Pendentes);
            Pendentes.Clear();
            Sessao?.EnviarParaMesmoAndar(m.Serializar(), confiavel: false);
        }

        private static void Executar(Action a)
        {
            try { a(); }
            catch (Exception e) { Avisar(0, $"[Golpes] {(e.InnerException ?? e).Message}"); }
        }

        public static void Receber(SessaoCoop sessao, Par de, byte[] dados)
        {
            try
            {
                if (!de.Compativel || !de.MesmoAndar || de.Avatar == null || !de.Avatar.gameObject.activeInHierarchy) return;
                ulong chave = de.Id.m_SteamID;
                if (!Janelas.TryGetValue(chave, out JanelaBonus j)) Janelas[chave] = j = new JanelaBonus();
                float agora = Time.unscaledTime;
                int recusados = 0;
                foreach (GolpesVistos.Item i in GolpesVistos.Desserializar(dados).Itens)
                {
                    if (!Validacao.GolpeVistoValido(i.Opcoes, i.Tipos, i.Principal, i.Direcao.x, i.Direcao.y, i.Direcao.z, i.Deslocamento.x, i.Deslocamento.y, i.Deslocamento.z)
                        || !Validacao.AceitarNaJanela(j, agora, Validacao.GolpesVistosPorJanelaMaximo))
                    {
                        recusados++;
                        continue;
                    }
                    FilaRede.Por(agora + AtrasoRede, new GolpeRecebido { De = de, Item = i }, instantaneo: true);
                }
                if (recusados > 0) Avisar(chave, $"[Golpes] {recusados} golpe(s) vistos de {de.Nome} recusado(s) (formato inválido ou acima de {Validacao.GolpesVistosPorJanelaMaximo}/s).");
            }
            catch (Exception e)
            {
                Avisar(de.Id.m_SteamID, $"[Golpes] Mensagem de {de.Nome} ignorada: {(e.InnerException ?? e).Message}");
            }
        }

        private static void ReproduzirDoAliado(GolpeRecebido g)
        {
            try
            {
                RemotePlayerAvatar av = g.De.Avatar;
                if (av == null || !g.De.MesmoAndar) return;
                EnemyController c = DanosVisiveis.AlvoDe(g.Item.Id);
                // Só inimigo perto do aliado (o mesmo alcance dos sons): golpe visto é cosmético, mas não em qualquer lugar do andar.
                if (c == null || (c.Transform.position - av.transform.position).sqrMagnitude > Validacao.DistanciaSomMaxima * Validacao.DistanciaSomMaxima) return;
                double inicio = Desempenho.Agora();
                try { Reproduzir(c, g.Item); }
                finally { Desempenho.Aplicou(Desempenho.Cosmetico.Golpe, inicio); }
            }
            catch (Exception e)
            {
                Avisar(0, $"[Golpes] {(e.InnerException ?? e).Message}");
            }
        }

        private static readonly Dictionary<ulong, float> ProximoAviso = new Dictionary<ulong, float>();

        /// <summary>Um aviso a cada 5 s por aliado: a mensagem é não confiável e chega dezenas de vezes por quadro.</summary>
        private static void Avisar(ulong chave, string texto)
        {
            float agora = Time.unscaledTime;
            if (ProximoAviso.TryGetValue(chave, out float t) && agora < t) return;
            ProximoAviso[chave] = agora + 5f;
            Plugin.Log.LogWarning(texto);
        }

        public static void Encerrar()
        {
            FilaRede.Limpar();
            Pendentes.Clear();
            Janelas.Clear();
            ProximoAviso.Clear();
        }
    }
}
