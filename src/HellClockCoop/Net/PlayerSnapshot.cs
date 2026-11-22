using System;
using System.IO;
using UnityEngine;

namespace HellClockCoop.Net
{
    /// <summary>Estado de um Animator num instante: estado atual de cada layer e parâmetros.</summary>
    internal sealed class AnimatorFrame
    {
        public int[] StateHashes = Array.Empty<int>();
        public float[] NormalizedTimes = Array.Empty<float>();
        public float[] LayerWeights = Array.Empty<float>();   // pesos controlados por Movement360LayerBehaviour
        public float[] Floats = Array.Empty<float>();
        public int[] Ints = Array.Empty<int>();
        public bool[] Bools = Array.Empty<bool>();

        public void Escrever(BinaryWriter w)
        {
            w.Write((byte)StateHashes.Length);
            for (int l = 0; l < StateHashes.Length; l++)
            {
                w.Write(StateHashes[l]);
                w.Write(NormalizedTimes[l]);
                w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(LayerWeights[l]) * 255f));
            }
            w.Write((byte)Floats.Length);
            foreach (float f in Floats) w.Write(f);
            w.Write((byte)Ints.Length);
            foreach (int n in Ints) w.Write(n);
            w.Write((byte)Bools.Length);
            int bits = 0;
            for (int b = 0; b < Bools.Length && b < 32; b++)
            {
                if (Bools[b]) bits |= 1 << b;
            }
            w.Write(bits);
        }

        public static AnimatorFrame Ler(BinaryReader r, AnimatorFrame a = null)
        {
            a = a ?? new AnimatorFrame();
            int camadas = r.ReadByte();
            a.StateHashes = new int[camadas];
            a.NormalizedTimes = new float[camadas];
            a.LayerWeights = new float[camadas];
            for (int l = 0; l < camadas; l++)
            {
                a.StateHashes[l] = r.ReadInt32();
                a.NormalizedTimes[l] = r.ReadSingle();
                a.LayerWeights[l] = r.ReadByte() / 255f;
            }
            a.Floats = new float[r.ReadByte()];
            for (int k = 0; k < a.Floats.Length; k++) a.Floats[k] = r.ReadSingle();
            a.Ints = new int[r.ReadByte()];
            for (int k = 0; k < a.Ints.Length; k++) a.Ints[k] = r.ReadInt32();
            a.Bools = new bool[r.ReadByte()];
            int bits = r.ReadInt32();
            for (int k = 0; k < a.Bools.Length; k++) a.Bools[k] = k < 32 && (bits & (1 << k)) != 0;
            return a;
        }
    }

    /// <summary>
    /// Fotografia do jogador local enviada aos outros (Fase 1: posição, rotação e animação).
    /// Os três Animators do personagem: corpo, chicote (lash) e lança (spear).
    /// </summary>
    internal sealed class PlayerSnapshot
    {
        public const byte TipoMensagem = 1;
        public const byte VersaoFormato = 4; // 4: fração da vida (0.14.0)
        public const int QuantidadeAnimators = 3;

        /// <summary>
        /// Ossos do tronco cuja rotação final (já com o IK dos Rigs de mira aplicado) é enviada.
        /// O clone não tem RigBuilder; recebe a pose pronta.
        /// </summary>
        public static readonly string[] OssosTronco = { "Hip_jnt", "Spine_1_jnt", "Spine_2_jnt", "Spine_3_jnt", "Neck_jnt" };

        public ushort Sequencia;
        public float Tempo;                    // relógio do remetente (Time.unscaledTime)
        public int CenaHash;                   // HashID da LevelDefinition atual (0 = hub Quixeramobim)
        public uint Andar;                     // índice do andar na run
        public bool Morto;
        public float Vida = 1f;                // fração da vida máxima (0..1), para a barra sobre o aliado (0.14.0)
        public Vector3 Posicao;
        public Quaternion Rotacao;             // rotação da raiz do jogador
        public Quaternion RotacaoModelo;       // rotação local do objeto com o Animator do corpo
        public readonly bool[] AnimatorAtivo = new bool[QuantidadeAnimators];
        public readonly AnimatorFrame[] Animators = { new AnimatorFrame(), new AnimatorFrame(), new AnimatorFrame() };
        public readonly Quaternion[] RotacoesOssos = new Quaternion[OssosTronco.Length]; // locais
        public byte MascaraOssos;   // bit i = osso i encontrado no remetente

        public byte[] Serializar()
        {
            using (var ms = new MemoryStream(128))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(TipoMensagem);
                w.Write(VersaoFormato);
                w.Write(Sequencia);
                w.Write(Tempo);
                w.Write(CenaHash);
                w.Write(Andar);
                w.Write(Morto);
                w.Write((ushort)Mathf.RoundToInt(Mathf.Clamp01(Vida) * 65535f));
                Escrever(w, Posicao);
                Escrever(w, Rotacao);
                Escrever(w, RotacaoModelo);

                byte mascara = 0;
                for (int i = 0; i < QuantidadeAnimators; i++)
                {
                    if (AnimatorAtivo[i]) mascara |= (byte)(1 << i);
                }
                w.Write(mascara);

                for (int i = 0; i < QuantidadeAnimators; i++)
                {
                    if (AnimatorAtivo[i]) Animators[i].Escrever(w);
                }

                w.Write(MascaraOssos);
                for (int i = 0; i < OssosTronco.Length; i++)
                {
                    if ((MascaraOssos & (1 << i)) != 0) Escrever(w, RotacoesOssos[i]);
                }
                return ms.ToArray();
            }
        }

        public static PlayerSnapshot Desserializar(byte[] dados)
        {
            using (var r = new BinaryReader(new MemoryStream(dados)))
            {
                if (r.ReadByte() != TipoMensagem)
                {
                    throw new FormatException("Mensagem não é um PlayerSnapshot."); // InvalidDataException foi removida do System.dll do jogo
                }
                byte versao = r.ReadByte();
                if (versao != VersaoFormato)
                {
                    throw new FormatException($"PlayerSnapshot versão {versao}; esperada {VersaoFormato}. Os dois lados precisam da mesma versão do mod.");
                }
                var s = new PlayerSnapshot
                {
                    Sequencia = r.ReadUInt16(),
                    Tempo = r.ReadSingle(),
                    CenaHash = r.ReadInt32(),
                    Andar = r.ReadUInt32(),
                    Morto = r.ReadBoolean(),
                    Vida = r.ReadUInt16() / 65535f,
                    Posicao = LerVetor(r),
                    Rotacao = LerQuaternion(r),
                    RotacaoModelo = LerQuaternion(r),
                };
                byte mascara = r.ReadByte();
                for (int i = 0; i < QuantidadeAnimators; i++)
                {
                    s.AnimatorAtivo[i] = (mascara & (1 << i)) != 0;
                    if (s.AnimatorAtivo[i]) AnimatorFrame.Ler(r, s.Animators[i]);
                }

                s.MascaraOssos = r.ReadByte();
                for (int i = 0; i < OssosTronco.Length; i++)
                {
                    if ((s.MascaraOssos & (1 << i)) != 0) s.RotacoesOssos[i] = LerQuaternion(r);
                }
                return s;
            }
        }

        private static void Escrever(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static void Escrever(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        private static Vector3 LerVetor(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        private static Quaternion LerQuaternion(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
