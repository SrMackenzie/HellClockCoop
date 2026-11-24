using System;
using DG.Tweening;
using HarmonyLib;
using HellClock.Player;
using HellClock.Skill;
using HellClock.VFX;
using UnityEngine;

namespace HellClockCoop.Fase2
{
    /// <summary>
    /// Efeito do TP até o aliado no seu personagem (0.30.0, opção A "raio + aura" do autor). Só no PC de quem teleporta: os
    /// aliados só o veem chegar (nada vai pela rede, e os efeitos criados aqui não entram no FxCompartilhados).
    /// - Canalizando: contorno (fresnel adicional dos materiais do personagem) na cor do aliado, pulsando cada vez mais rápido
    ///   nos 2 s (como o jogo pulsa a intensidade, 1 ↔ 0,25). Cor e intensidade de cada material são guardadas e voltam no fim
    ///   (cancelar também restaura).
    /// - Teleporte: DisappearFx do TeleportConfig do jogo + LerpDissolve(1, 0,2 s, InQuad); 0,2 s depois o TpAliado faz o
    ///   Warp e aqui vem o ReappearFx + LerpDissolve(0, 0,35 s, Linear). Sem o TeleportConfig (outra versão do jogo), só o
    ///   dissolver.
    /// Fatos do IL (2.1.1.1): CharacterEffectsController.AllMaterials, static int AdditionalFresnelColor/Intensity,
    /// LerpDissolve(float, float, Ease); TeleportSkillDefinition.TeleportConfig (DisappearFx, ReappearFx: GameplayFx); o jogo
    /// chama CreateFx(caster, posição, null, null) no Teleport.ExecuteTeleport.
    /// Tudo em try/catch: erro aqui desliga só o efeito (log uma vez) e o teleporte segue.
    /// </summary>
    internal static class TpEfeito
    {
        internal const float DuracaoSumir = 0.2f;
        private const float DuracaoVoltar = 0.35f;
        private const float FrequenciaInicial = 1.5f, FrequenciaFinal = 6.5f; // pulsos por segundo; 8 ciclos inteiros em 2 s: termina no máximo
        private const float IntensidadeMinima = 0.25f, IntensidadeMaxima = 1f;
        private const float DoisPi = 6.2831855f;           // Mathf.PI não existe no Unity do jogo (stripped)

        private static bool _desligado;
        private static int _idCor = -1, _idIntensidade = -1;
        private static bool _idsLidos;

        // Contorno: materiais com os valores de antes.
        private static CharacterEffectsController _cec;
        private static Material[] _materiais;
        private static Color[] _coresAntes;
        private static float[] _intensidadesAntes;

        private static bool _sumido;                     // dissolvido, esperando o Warp
        private static CharacterEffectsController _cecSumido;
        private static Tween _tweenSumir;                // o LerpDissolve do jogo não mata o anterior: matamos antes de voltar

        private static bool _configBuscada;
        private static TeleportConfig _config;

        /// <summary>Contorno na cor do aliado; t = segundos de canalização (0..duração), o pulso acelera com ele.</summary>
        internal static void Pulsar(PlayerController local, Color cor, float t, float duracao)
        {
            if (_desligado || local == null) return;
            try
            {
                if (_materiais != null && !ReferenceEquals(_cec, local.CharacterEffectsController)) Restaurar(); // outro personagem
                if (_materiais == null && !Guardar(local)) return;
                float d = Mathf.Max(0.01f, duracao);
                float s = Mathf.Clamp(t, 0f, d);
                // Fase com a frequência subindo em linha reta de FrequenciaInicial a FrequenciaFinal (integral da frequência).
                float fase = DoisPi * (FrequenciaInicial * s + (FrequenciaFinal - FrequenciaInicial) * s * s / (2f * d));
                float intensidade = Mathf.Lerp(IntensidadeMinima, IntensidadeMaxima, 0.5f + 0.5f * Mathf.Cos(fase));
                cor.a = 1f;
                foreach (Material m in _materiais)
                {
                    if (m == null) continue;
                    m.SetColor(_idCor, cor);
                    m.SetFloat(_idIntensidade, intensidade);
                }
            }
            catch (Exception e) { Desligar("contorno", e); }
        }

        /// <summary>Devolve cor e intensidade do contorno de antes (fim da canalização, cancelamento, troca de personagem).</summary>
        internal static void Restaurar()
        {
            if (_materiais == null) return;
            try
            {
                for (int i = 0; i < _materiais.Length; i++)
                {
                    Material m = _materiais[i];
                    if (m == null) continue;
                    m.SetColor(_idCor, _coresAntes[i]);
                    m.SetFloat(_idIntensidade, _intensidadesAntes[i]);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[TP] Contorno não restaurado: {(e.InnerException ?? e).Message}"); }
            finally
            {
                _materiais = null;
                _coresAntes = null;
                _intensidadesAntes = null;
                _cec = null;
            }
        }

        private static bool Guardar(PlayerController local)
        {
            if (!LerIds()) return false;
            CharacterEffectsController cec = local.CharacterEffectsController;
            if (cec == null) return false;
            var lista = Materiais(cec);
            if (lista == null || lista.Count == 0) return false;
            var mats = new Material[lista.Count];
            var cores = new Color[lista.Count];
            var intens = new float[lista.Count];
            for (int i = 0; i < lista.Count; i++)
            {
                Material m = lista[i];
                mats[i] = m;
                if (m == null) continue;
                cores[i] = m.HasProperty(_idCor) ? m.GetColor(_idCor) : Color.black;
                intens[i] = m.HasProperty(_idIntensidade) ? m.GetFloat(_idIntensidade) : 0f;
            }
            _cec = cec;
            _materiais = mats;
            _coresAntes = cores;
            _intensidadesAntes = intens;
            return true;
        }

        /// <summary>Ids das propriedades do shader, dos estáticos do jogo (privados ou não: por reflexão, uma vez).</summary>
        private static bool LerIds()
        {
            if (_idsLidos) return _idCor != -1 && _idIntensidade != -1;
            _idsLidos = true;
            object c = AccessTools.Field(typeof(CharacterEffectsController), "AdditionalFresnelColor")?.GetValue(null);
            object i = AccessTools.Field(typeof(CharacterEffectsController), "AdditionalFresnelIntensity")?.GetValue(null);
            if (c is int ic && i is int ii)
            {
                _idCor = ic;
                _idIntensidade = ii;
                return true;
            }
            Plugin.Log.LogWarning("[TP] Propriedades do contorno não encontradas no jogo: teleporte sem contorno.");
            return false;
        }

        private static System.Reflection.MethodInfo _getMateriais;

        /// <summary>CharacterEffectsController.AllMaterials (não público): os materiais próprios do personagem.</summary>
        private static System.Collections.Generic.IReadOnlyList<Material> Materiais(CharacterEffectsController cec)
        {
            if (_getMateriais == null) _getMateriais = AccessTools.PropertyGetter(typeof(CharacterEffectsController), "AllMaterials");
            if (_getMateriais == null) throw new InvalidOperationException("CharacterEffectsController.AllMaterials não existe");
            return _getMateriais.Invoke(cec, null) as System.Collections.Generic.IReadOnlyList<Material>;
        }

        /// <summary>Some: DisappearFx onde você está e dissolve em 0,2 s. O Warp vem depois (TpAliado), com Reaparecer.</summary>
        internal static void Sumir(PlayerController local)
        {
            Restaurar();
            if (_desligado || local == null) return;
            try
            {
                CriarFx(Config()?.DisappearFx, local, local.transform.position);
                CharacterEffectsController cec = local.CharacterEffectsController;
                if (cec != null)
                {
                    _tweenSumir = cec.LerpDissolve(1f, DuracaoSumir, Ease.InQuad);
                    _sumido = true;
                    _cecSumido = cec;
                }
            }
            catch (Exception e) { Desligar("sumir", e); }
        }

        /// <summary>Reaparece no destino (depois do Warp): ReappearFx e o dissolver volta em 0,35 s.</summary>
        internal static void Reaparecer(PlayerController local, Vector3 destino)
        {
            if (local == null) { Desfazer(); return; }
            if (!_desligado)
            {
                try { CriarFx(Config()?.ReappearFx, local, destino); }
                catch (Exception e) { Desligar("reaparecer", e); }
            }
            Voltar(DuracaoVoltar, Ease.Linear);
        }

        /// <summary>Teleporte perdido depois de sumir: o personagem volta a aparecer onde está.</summary>
        internal static void Desfazer() => Voltar(0.1f, Ease.Linear);

        private static void Voltar(float duracao, Ease ease)
        {
            if (!_sumido) return;
            _sumido = false;
            CharacterEffectsController cec = _cecSumido;
            Tween sumir = _tweenSumir;
            _tweenSumir = null;
            if (sumir != null && sumir.IsActive()) sumir.Kill(); // pausa ou timeScale baixo: o sumir ainda andando terminaria por último
            _cecSumido = null;
            if (cec == null) return; // personagem destruído (troca de cena): o novo nasce visível
            try { cec.LerpDissolve(0f, duracao, ease); }
            catch (Exception e) { Plugin.Log.LogWarning($"[TP] Dissolver não voltou: {(e.InnerException ?? e).Message}"); }
        }

        /// <summary>Efeito do jogo só neste PC: o FxCompartilhados ignora o que nasce aqui (não vai para os aliados).</summary>
        private static void CriarFx(GameplayFx fx, PlayerController local, Vector3 posicao)
        {
            if (fx == null) return;
            Fase3.FxCompartilhados.IgnorarCriados = true;
            try { Fase3.FxCompartilhados.MarcarSoLocal(fx.CreateFx(local, posicao, null, null)); }
            finally { Fase3.FxCompartilhados.IgnorarCriados = false; }
        }

        /// <summary>TeleportConfig da primeira TeleportSkillDefinition da coleção de skills que tem os dois efeitos (uma vez).</summary>
        private static TeleportConfig Config()
        {
            if (_configBuscada) return _config;
            _configBuscada = true;
            try
            {
                foreach (SkillDefinition d in global::HellClock.Constants.Collections.SKILL_DEFINITION_COLLECTION.Reference)
                {
                    if (!(d is TeleportSkillDefinition t)) continue;
                    TeleportConfig c = t.TeleportConfig;
                    if (c == null || c.DisappearFx == null || c.ReappearFx == null) continue;
                    _config = c;
                    Plugin.Log.LogInfo($"[TP] Efeitos do teleporte: os da skill {t.name}.");
                    return _config;
                }
                Plugin.Log.LogWarning("[TP] Nenhuma skill de teleporte com efeitos no jogo: teleporte só com o dissolver.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[TP] Efeitos do teleporte não lidos: {(e.InnerException ?? e).Message}"); }
            return _config;
        }

        private static void Desligar(string parte, Exception e)
        {
            _desligado = true;
            Plugin.Log.LogWarning($"[TP] Efeito visual do teleporte desligado ({parte}): {(e.InnerException ?? e).Message}");
            Restaurar();
            Voltar(0.1f, Ease.Linear);
        }
    }
}
