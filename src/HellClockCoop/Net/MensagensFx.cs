using System;
using System.Collections.Generic;
using UnityEngine;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Fase 3b — efeitos visuais das skills do jogador (tiros, projéteis, áreas, auras).
    /// Todo efeito do jogo nasce em GameplayFx.CreateFx a partir de um prefab do Addressables;
    /// o GUID desse prefab é igual nos dois jogos. Quem lança manda o GUID e onde o efeito apareceu;
    /// os outros criam uma cópia só visual (sem scripts nem colisores) no avatar dele.
    /// </summary>
    internal sealed class FxNovo
    {
        public ushort Id;
        public string Guid = "";
        public const byte PaiMundo = 0, PaiJogador = 1, PaiInimigo = 2;

        public byte TipoPai;
        /// <summary>Caminho do osso a partir da raiz do jogador (o avatar tem a mesma hierarquia) ou do inimigo.</summary>
        public string Caminho = "";
        public ushort InimigoId;       // quando TipoPai = PaiInimigo (netId)
        /// <summary>Só no eco (não vai pela rede): o objeto pai de verdade, para prender a cópia no mesmo inimigo.</summary>
        [NonSerialized] public Transform PaiLocal;
        public Vector3 Posicao;        // mundo (solto) ou local (preso)
        public Quaternion Rotacao;
        public Vector3 Escala = Vector3.one;
        public bool LinhaNoMundo;      // LineRenderer.useWorldSpace
        public Vector3[] Linha = Array.Empty<Vector3>(); // pontos do primeiro LineRenderer (tiros)

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoFxNovo, w =>
        {
            w.Write(Id); w.Write(Guid ?? ""); w.Write(TipoPai); w.Write(Caminho ?? ""); w.Write(InimigoId);
            Bin.Escrever(w, Posicao);
            w.Write(Rotacao.x); w.Write(Rotacao.y); w.Write(Rotacao.z); w.Write(Rotacao.w);
            Bin.Escrever(w, Escala);
            w.Write(LinhaNoMundo);
            w.Write((byte)Linha.Length);
            foreach (Vector3 p in Linha) Bin.Escrever(w, p);
        });

        public static FxNovo Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoFxNovo, r =>
        {
            var f = new FxNovo { Id = r.ReadUInt16(), Guid = r.ReadString(), TipoPai = r.ReadByte(), Caminho = r.ReadString(), InimigoId = r.ReadUInt16(), Posicao = Bin.LerVetor(r) };
            f.Rotacao = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            f.Escala = Bin.LerVetor(r);
            f.LinhaNoMundo = r.ReadBoolean();
            f.Linha = new Vector3[r.ReadByte()];
            for (int i = 0; i < f.Linha.Length; i++) f.Linha[i] = Bin.LerVetor(r);
            return f;
        });
    }

    /// <summary>Posição dos efeitos soltos que se movem (projéteis), 20 Hz, canal não confiável.</summary>
    internal sealed class FxMovimento
    {
        internal struct Item
        {
            public ushort Id;
            public Vector3 Posicao;
            public Quaternion Rotacao;
        }

        public float Tempo;
        public readonly List<Item> Itens = new List<Item>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoFxMovimento, w =>
        {
            w.Write(Tempo);
            w.Write((byte)Itens.Count);
            foreach (Item i in Itens)
            {
                w.Write(i.Id);
                Bin.Escrever(w, i.Posicao);
                w.Write(i.Rotacao.x); w.Write(i.Rotacao.y); w.Write(i.Rotacao.z); w.Write(i.Rotacao.w);
            }
        });

        public static FxMovimento Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoFxMovimento, r =>
        {
            var m = new FxMovimento { Tempo = r.ReadSingle() };
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                m.Itens.Add(new Item
                {
                    Id = r.ReadUInt16(),
                    Posicao = Bin.LerVetor(r),
                    Rotacao = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                });
            }
            return m;
        });
    }

    /// <summary>O efeito acabou (voltou ao pool) em quem lançou.</summary>
    internal sealed class FxFim
    {
        public ushort Id;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoFxFim, w => w.Write(Id));
        public static FxFim Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoFxFim, r => new FxFim { Id = r.ReadUInt16() });
    }

    /// <summary>
    /// Fase 3c — o inimigo do host usou uma skill. A réplica no convidado usa a mesma skill, com o
    /// alvo traduzido: se o alvo era o avatar deste convidado, o alvo vira o jogador local de verdade
    /// (o dano é calculado aqui, com as defesas dele).
    /// </summary>
    internal sealed class InimigoSkill
    {
        public const byte AlvoNenhum = 0;
        public const byte AlvoHost = 1;
        public const byte AlvoConvidado = 2;

        public ushort Id;
        public int SkillHash;       // SkillDefinition.HashID
        public byte TipoAlvo;
        public ulong SteamAlvo;     // quando TipoAlvo = AlvoConvidado
        public Vector3 PosicaoAlvo;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoInimigoSkill, w =>
        {
            w.Write(Id); w.Write(SkillHash); w.Write(TipoAlvo); w.Write(SteamAlvo); Bin.Escrever(w, PosicaoAlvo);
        });

        public static InimigoSkill Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoInimigoSkill, r => new InimigoSkill
        {
            Id = r.ReadUInt16(), SkillHash = r.ReadInt32(), TipoAlvo = r.ReadByte(), SteamAlvo = r.ReadUInt64(), PosicaoAlvo = Bin.LerVetor(r),
        });
    }
}

namespace HellClockCoop.Net
{
    /// <summary>Números de dano de um jogador nos inimigos (todos → todos no mesmo andar, não confiável, 20 Hz).</summary>
    internal sealed class DanosVistos
    {
        /// <summary>Id 0 (o host nunca dá): o boneco de treino do hub, que cada PC cria para si (0.9.9).</summary>
        public const ushort IdBoneco = 0;

        internal struct Item
        {
            public ushort Id;      // netId do inimigo, ou IdBoneco
            public float Valor;
            public byte Criticos;
        }

        public readonly List<Item> Itens = new List<Item>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoDanosVistos, w =>
        {
            w.Write((byte)Itens.Count);
            foreach (Item i in Itens) { w.Write(i.Id); w.Write(i.Valor); w.Write(i.Criticos); }
        });

        public static DanosVistos Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoDanosVistos, r =>
        {
            var v = new DanosVistos();
            int n = r.ReadByte();
            for (int k = 0; k < n; k++) v.Itens.Add(new Item { Id = r.ReadUInt16(), Valor = r.ReadSingle(), Criticos = r.ReadByte() });
            return v;
        });
    }
}

namespace HellClockCoop.Net
{
    /// <summary>
    /// Partes internas de um efeito que os scripts do jogo mexem (facas que giram em volta do jogador,
    /// partes ligadas/desligadas). Caminho por índices de filho ("0/3/1"); vai só o que mudou, 20 Hz.
    /// </summary>
    internal sealed class FxNos
    {
        internal struct No
        {
            public string Caminho;
            public bool Ativo;
            public Vector3 Posicao;     // local
            public Quaternion Rotacao;  // local
            public Vector3 Escala;      // local
        }

        public ushort Id;
        public readonly List<No> Nos = new List<No>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoFxNos, w =>
        {
            w.Write(Id);
            w.Write((byte)Nos.Count);
            foreach (No n in Nos)
            {
                w.Write(n.Caminho ?? "");
                w.Write(n.Ativo);
                Bin.Escrever(w, n.Posicao);
                w.Write(n.Rotacao.x); w.Write(n.Rotacao.y); w.Write(n.Rotacao.z); w.Write(n.Rotacao.w);
                Bin.Escrever(w, n.Escala);
            }
        });

        public static FxNos Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoFxNos, r =>
        {
            var f = new FxNos { Id = r.ReadUInt16() };
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                f.Nos.Add(new No
                {
                    Caminho = r.ReadString(),
                    Ativo = r.ReadBoolean(),
                    Posicao = Bin.LerVetor(r),
                    Rotacao = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                    Escala = Bin.LerVetor(r),
                });
            }
            return f;
        });
    }
}
