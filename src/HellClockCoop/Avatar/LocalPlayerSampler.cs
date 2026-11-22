using HellClock.Level;
using HellClock.Player;
using HellClockCoop.Net;
using UnityEngine;

namespace HellClockCoop.Avatar
{
    /// <summary>Tira snapshots do PlayerController local (o único do jogo).</summary>
    internal sealed class LocalPlayerSampler
    {
        /// <summary>Posição de espera usada pelo jogo antes do primeiro warp (visto no teste da Fase 0).</summary>
        private const float PosicaoDeEspera = 99990f;

        private ushort _sequencia;
        private PlayerController _jogadorDosOssos;
        private Transform[] _ossos;

        /// <summary>Andar carregado agora, ou null durante a troca de andar.</summary>
        public static LevelInfo AndarAtual()
        {
            LevelChanger trocador = ClassSingleton<LevelChanger>.Instance;
            return trocador.IsChangingLevel.Value ? null : trocador.HACK_CurrentLevel;
        }

        /// <summary>HashID da LevelDefinition atual (0 = hub Quixeramobim); -1 enquanto carrega.</summary>
        public static int CenaAtual()
        {
            LevelInfo andar = AndarAtual();
            return andar?.LevelDefinition != null ? andar.LevelDefinition.HashID : -1;
        }

        /// <summary>Retorna false se o jogador ainda não está numa posição válida do mundo.</summary>
        public bool TentarAmostrar(PlayerController jogador, PlayerSnapshot destino)
        {
            if (jogador == null || jogador.Animator == null)
            {
                return false;
            }
            Vector3 pos = jogador.transform.position;
            if (Mathf.Abs(pos.x) > PosicaoDeEspera || Mathf.Abs(pos.z) > PosicaoDeEspera)
            {
                return false;
            }

            destino.Sequencia = _sequencia++;
            destino.Tempo = Time.unscaledTime;
            destino.CenaHash = CenaAtual();
            destino.Andar = AndarAtual()?.LevelIndex ?? 0;
            destino.Morto = jogador.LifeController != null && jogador.LifeController.IsDead;
            destino.Vida = jogador.LifeController == null ? 1f : destino.Morto ? 0f : jogador.LifeController.CurrentRatio;
            destino.Posicao = pos;
            destino.Rotacao = jogador.transform.rotation;
            // Rotação do modelo relativa à raiz (identidade se o Animator estiver na própria raiz).
            destino.RotacaoModelo = Quaternion.Inverse(jogador.transform.rotation) * jogador.Animator.transform.rotation;

            // Pose final do tronco (depois do IK dos Rigs de mira). LateUpdate roda após a avaliação da animação.
            if (_jogadorDosOssos != jogador)
            {
                _jogadorDosOssos = jogador;
                _ossos = AvatarFactory.BuscarOssosTronco(jogador.Animator.transform);
            }
            destino.MascaraOssos = 0;
            for (int i = 0; i < _ossos.Length; i++)
            {
                if (_ossos[i] == null) continue;
                destino.MascaraOssos |= (byte)(1 << i);
                destino.RotacoesOssos[i] = _ossos[i].localRotation;
            }

            Animator[] animators = { jogador.Animator, jogador.LashAnimator, jogador.SpearAnimator };
            for (int i = 0; i < PlayerSnapshot.QuantidadeAnimators; i++)
            {
                Animator a = animators[i];
                bool ativo = a != null && a.isActiveAndEnabled && a.runtimeAnimatorController != null;
                destino.AnimatorAtivo[i] = ativo;
                if (ativo)
                {
                    AnimatorSync.Ler(a, destino.Animators[i]);
                }
            }
            return true;
        }
    }
}
