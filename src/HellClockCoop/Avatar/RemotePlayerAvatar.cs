using System.Collections.Generic;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Avatar
{
    /// <summary>
    /// Representação visual de um aliado: recebe PlayerSnapshots e os reproduz com um pequeno
    /// atraso de interpolação (padrão em jogos em rede: troca ~100 ms de latência visual
    /// por movimento suave mesmo com pacotes chegando irregulares).
    /// </summary>
    [DefaultExecutionOrder(-500)] // pose aplicada cedo no LateUpdate, antes de quem lê os ossos
    internal sealed class RemotePlayerAvatar : MonoBehaviour
    {
        public float AtrasoInterpolacao = 0.1f;
        public Vector3 DeslocamentoMundo = Vector3.zero;

        private Transform _modelo;
        private Animator[] _animators;
        private Transform[] _ossos;
        private Animator[] _todosAnimators;
        private readonly List<PlayerSnapshot> _buffer = new List<PlayerSnapshot>();
        private float _offsetRelogio;          // tempo do remetente − tempo local
        private bool _temOffset;
        private ushort _ultimaSequencia;
        private bool _recebeuAlgum;

        /// <summary>Objeto com o Animator do corpo (Pajeu_RIG_360), onde fica a pasta "Mesh" das roupas.</summary>
        public Transform ModeloCorpo => _animators != null && _animators.Length > 0 && _animators[0] != null ? _animators[0].transform : _modelo;

        /// <summary>Clone da raiz do jogador (mesma hierarquia de ossos do PlayerController original).</summary>
        public Transform Modelo => _modelo;

        public int Recebidos { get; private set; }
        public int ForaDeOrdem { get; private set; }

        /// <summary>Fração da vida no último snapshot aceito (0..1), para a barra sobre o aliado (0.14.0).</summary>
        public float Vida { get; private set; } = 1f;

        public void Inicializar(Transform modelo, Animator[] animators, Transform[] ossosTronco, Animator[] todosAnimators)
        {
            _modelo = modelo;
            _animators = animators;
            _ossos = ossosTronco;
            _todosAnimators = todosAnimators;
        }

        private void OnDestroy()
        {
            if (_todosAnimators != null) AvatarAnimatorGuard.Remover(_todosAnimators);
        }

        /// <summary>Esquece o histórico (ex.: o aliado reapareceu após trocar de andar).</summary>
        public void Reiniciar()
        {
            _buffer.Clear();
            _temOffset = false;
            _recebeuAlgum = false;
        }

        public void Receber(PlayerSnapshot s)
        {
            // Descarta pacote velho (canal não confiável pode entregar fora de ordem).
            if (_recebeuAlgum && (short)(s.Sequencia - _ultimaSequencia) <= 0)
            {
                ForaDeOrdem++;
                return;
            }
            _recebeuAlgum = true;
            _ultimaSequencia = s.Sequencia;
            Recebidos++;
            Vida = s.Vida;

            float amostra = s.Tempo - Time.unscaledTime;
            // O menor offset visto é o mais próximo do real (o resto é atraso de rede); deriva devagar.
            _offsetRelogio = !_temOffset ? amostra : Mathf.Min(amostra, _offsetRelogio + 0.01f);
            _temOffset = true;

            _buffer.Add(s);
            if (_buffer.Count > 64)
            {
                _buffer.RemoveAt(0);
            }

            if (_buffer.Count == 1)
            {
                AplicarTransform(s, s, 0f);
            }
        }

        private void LateUpdate()
        {
            if (!_temOffset || _buffer.Count == 0 || _modelo == null)
            {
                return;
            }
            float tempoRender = Time.unscaledTime + _offsetRelogio - AtrasoInterpolacao;

            // Encontra o par de snapshots em volta do tempo de render.
            PlayerSnapshot a = _buffer[0], b = _buffer[0];
            for (int i = 0; i < _buffer.Count; i++)
            {
                if (_buffer[i].Tempo <= tempoRender)
                {
                    a = _buffer[i];
                    b = i + 1 < _buffer.Count ? _buffer[i + 1] : _buffer[i];
                }
            }
            float t = b.Tempo > a.Tempo ? Mathf.Clamp01((tempoRender - a.Tempo) / (b.Tempo - a.Tempo)) : 0f;
            AplicarTransform(a, b, t);

            // A animação usa o snapshot mais recente que já "passou": estados são discretos.
            PlayerSnapshot anim = t >= 0.5f ? b : a;
            for (int i = 0; i < _animators.Length; i++)
            {
                Animator alvo = _animators[i];
                if (alvo == null) continue;
                bool ativo = anim.AnimatorAtivo[i];
                if (i > 0 && alvo.gameObject.activeSelf != ativo)
                {
                    alvo.gameObject.SetActive(ativo);   // chicote/lança aparecem só quando usados
                }
                if (ativo)
                {
                    AnimatorSync.Aplicar(alvo, anim.Animators[i]);
                }
            }

            // Tronco: sobrescreve a pose do Animator do clone com a pose final do remetente (com IK de mira).
            // Este LateUpdate roda depois da avaliação da animação do frame, então a sobrescrita vale.
            for (int i = 0; i < _ossos.Length; i++)
            {
                if (_ossos[i] == null || (a.MascaraOssos & (1 << i)) == 0 || (b.MascaraOssos & (1 << i)) == 0) continue;
                _ossos[i].localRotation = Quaternion.Slerp(a.RotacoesOssos[i], b.RotacoesOssos[i], t);
            }

            // Mantém só o necessário no buffer.
            while (_buffer.Count > 2 && _buffer[1].Tempo < tempoRender - 0.5f)
            {
                _buffer.RemoveAt(0);
            }
        }

        private void AplicarTransform(PlayerSnapshot a, PlayerSnapshot b, float t)
        {
            transform.position = Vector3.Lerp(a.Posicao, b.Posicao, t) + DeslocamentoMundo;
            transform.rotation = Quaternion.Slerp(a.Rotacao, b.Rotacao, t);
            if (_animators.Length > 0 && _animators[0] != null)
            {
                _animators[0].transform.rotation = transform.rotation * Quaternion.Slerp(a.RotacaoModelo, b.RotacaoModelo, t);
            }
        }
    }
}
