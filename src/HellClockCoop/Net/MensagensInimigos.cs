using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Fase 3a — inimigos do host replicados no convidado.
    /// Spawn, morte e sumiço vão pelo canal confiável; o estado (posição, vida, animação) vai a 10 Hz
    /// pelo canal não confiável, só dos inimigos perto de algum aliado. Os golpes do convidado vão ao
    /// host pelo canal confiável, somados por inimigo.
    /// </summary>
    internal sealed class InimigoSpawn
    {
        public ushort Id;
        public uint Andar;                 // índice do andar do host em que o inimigo nasceu
        public int DefinicaoHash;          // EnemyDefinition.HashID
        public byte Tipo;                  // EEnemyType
        public int ArquetipoHash;          // ChampionEnemyArchetypeDefinition.HashID (0 = nenhum)
        public int[] Afixos = Array.Empty<int>(); // PassiveSkillDefinition.HashID, já com os sorteados
        public bool Sobrevivencia, ParDeBoss, BarraDeVida;
        public byte EfeitoSpawn;           // SpawnFX
        public Vector3 OrigemSpawn;
        public float AtrasoSpawn;
        public Vector3 Posicao;
        public float Yaw;
        public bool JaNasceu;              // _spawned no host (para quem chega depois)
        public float Vida = 1f;            // fração da vida máxima
        public byte Ajustes;               // InimigoAjuste.* aplicados depois do spawn (invocação etc.)

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoInimigoSpawn, w =>
        {
            w.Write(Id); w.Write(Andar); w.Write(DefinicaoHash); w.Write(Tipo); w.Write(ArquetipoHash);
            w.Write((byte)Afixos.Length);
            foreach (int a in Afixos) w.Write(a);
            byte flags = (byte)((Sobrevivencia ? 1 : 0) | (ParDeBoss ? 2 : 0) | (BarraDeVida ? 4 : 0) | (JaNasceu ? 8 : 0));
            w.Write(flags);
            w.Write(EfeitoSpawn);
            Bin.Escrever(w, OrigemSpawn);
            w.Write(AtrasoSpawn);
            Bin.Escrever(w, Posicao);
            w.Write(Yaw);
            w.Write(Vida);
            w.Write(Ajustes);
        });

        public static InimigoSpawn Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoInimigoSpawn, r =>
        {
            var s = new InimigoSpawn
            {
                Id = r.ReadUInt16(), Andar = r.ReadUInt32(), DefinicaoHash = r.ReadInt32(), Tipo = r.ReadByte(), ArquetipoHash = r.ReadInt32(),
            };
            s.Afixos = new int[r.ReadByte()];
            for (int i = 0; i < s.Afixos.Length; i++) s.Afixos[i] = r.ReadInt32();
            byte f = r.ReadByte();
            s.Sobrevivencia = (f & 1) != 0; s.ParDeBoss = (f & 2) != 0; s.BarraDeVida = (f & 4) != 0; s.JaNasceu = (f & 8) != 0;
            s.EfeitoSpawn = r.ReadByte();
            s.OrigemSpawn = Bin.LerVetor(r);
            s.AtrasoSpawn = r.ReadSingle();
            s.Posicao = Bin.LerVetor(r);
            s.Yaw = r.ReadSingle();
            s.Vida = r.ReadSingle();
            s.Ajustes = r.ReadByte();
            return s;
        });
    }

    /// <summary>
    /// Ajuste feito pelo jogo logo depois do spawn. Invocação (SetupSummon): sem XP e sem loot.
    /// Descartável (SetupDisposable): sem XP. Sem isto o convidado ganharia loot de invocações.
    /// </summary>
    internal sealed class InimigoAjuste
    {
        public const byte Invocacao = 1;
        public const byte Descartavel = 2;

        public ushort Id;
        public byte Ajuste;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoInimigoAjuste, w => { w.Write(Id); w.Write(Ajuste); });
        public static InimigoAjuste Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoInimigoAjuste, r => new InimigoAjuste { Id = r.ReadUInt16(), Ajuste = r.ReadByte() });
    }

    /// <summary>Morte decidida pelo host. SemDrop = o host matou sem drops (limpeza dos opcionais).</summary>
    internal sealed class InimigoMorte
    {
        public ushort Id;
        public bool SemDrop;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoInimigoMorte, w => { w.Write(Id); w.Write(SemDrop); });
        public static InimigoMorte Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoInimigoMorte, r => new InimigoMorte { Id = r.ReadUInt16(), SemDrop = r.ReadBoolean() });
    }

    /// <summary>Inimigo descartado sem morrer (troca de andar, fim de onda, invocação que expira).</summary>
    internal sealed class InimigoSumiu
    {
        public ushort Id;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoInimigoSumiu, w => w.Write(Id));
        public static InimigoSumiu Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoInimigoSumiu, r => new InimigoSumiu { Id = r.ReadUInt16() });
    }

    /// <summary>Andar limpo no host: o convidado dispara o próprio OnAllEnemiesCleared (missões de "limpar andar").</summary>
    internal sealed class AndarLimpo
    {
        public uint Andar;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoAndarLimpo, w => w.Write(Andar));
        public static AndarLimpo Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoAndarLimpo, r => new AndarLimpo { Andar = r.ReadUInt32() });
    }

    /// <summary>Estado de vários inimigos num pacote (canal não confiável, até ~1100 bytes).</summary>
    internal sealed class InimigosEstado
    {
        internal sealed class Item
        {
            public ushort Id;
            public Vector3 Posicao;
            public float Yaw;
            public float Vida;
            public bool JaNasceu;
            public bool Agressivo;        // EnemyAIController.IsAggressive (chefe libera o dano e a barra)
            public AnimatorFrame Animacao; // null = sem Animator
        }

        public uint Sequencia;
        public readonly List<Item> Itens = new List<Item>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoInimigosEstado, w =>
        {
            w.Write(Sequencia);
            w.Write((byte)Itens.Count);
            foreach (Item i in Itens) EscreverItem(w, i);
        });

        public static void EscreverItem(BinaryWriter w, Item i)
        {
            w.Write(i.Id);
            Bin.Escrever(w, i.Posicao);
            w.Write((ushort)Mathf.RoundToInt(Mathf.Repeat(i.Yaw, 360f) / 360f * 65535f));
            w.Write((ushort)Mathf.RoundToInt(Mathf.Clamp01(i.Vida) * 65535f));
            w.Write((byte)((i.JaNasceu ? 1 : 0) | (i.Animacao != null ? 2 : 0) | (i.Agressivo ? 4 : 0)));
            i.Animacao?.Escrever(w);
        }

        public static InimigosEstado Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoInimigosEstado, r =>
        {
            var e = new InimigosEstado { Sequencia = r.ReadUInt32() };
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                var i = new Item
                {
                    Id = r.ReadUInt16(),
                    Posicao = Bin.LerVetor(r),
                    Yaw = r.ReadUInt16() / 65535f * 360f,
                    Vida = r.ReadUInt16() / 65535f,
                };
                byte f = r.ReadByte();
                i.JaNasceu = (f & 1) != 0;
                i.Agressivo = (f & 4) != 0;
                if ((f & 2) != 0) i.Animacao = AnimatorFrame.Ler(r);
                e.Itens.Add(i);
            }
            return e;
        });
    }

    /// <summary>Dano do convidado nas réplicas, somado por inimigo e tipo de dano (convidado → host).</summary>
    internal sealed class Golpes
    {
        internal struct Item
        {
            public ushort Id;
            public float Valor;
            public int MascaraTipos;  // DamageInstance.DamageTypesBitMask
            public byte Criticos;
        }

        public readonly List<Item> Itens = new List<Item>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoGolpes, w =>
        {
            w.Write((ushort)Itens.Count);
            foreach (Item i in Itens) { w.Write(i.Id); w.Write(i.Valor); w.Write((byte)i.MascaraTipos); w.Write(i.Criticos); }
        });

        public static Golpes Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoGolpes, r =>
        {
            var g = new Golpes();
            int n = r.ReadUInt16();
            for (int k = 0; k < n; k++)
            {
                g.Itens.Add(new Item { Id = r.ReadUInt16(), Valor = r.ReadSingle(), MascaraTipos = r.ReadByte(), Criticos = r.ReadByte() });
            }
            return g;
        });
    }

    internal static class Bin
    {
        public static void Escrever(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        public static Vector3 LerVetor(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}

namespace HellClockCoop.Net
{
    /// <summary>Fase 3a+ — baú do andar do host (o convidado cria uma cópia no mesmo lugar).</summary>
    internal sealed class BauSpawn
    {
        public ushort Id;
        public uint Andar;
        public int DefinicaoHash;   // ChestDefinition.HashID
        public Vector3 Posicao;
        public float Rotacao;
        public bool Aberto;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoBauSpawn, w =>
        {
            w.Write(Id); w.Write(Andar); w.Write(DefinicaoHash); Bin.Escrever(w, Posicao); w.Write(Rotacao); w.Write(Aberto);
        });

        public static BauSpawn Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoBauSpawn, r => new BauSpawn
        {
            Id = r.ReadUInt16(), Andar = r.ReadUInt32(), DefinicaoHash = r.ReadInt32(), Posicao = Bin.LerVetor(r), Rotacao = r.ReadSingle(), Aberto = r.ReadBoolean(),
        });
    }

    /// <summary>Alguém abriu o baú: abre para todos, cada um com o próprio loot (nos dois sentidos; o host repassa).</summary>
    internal sealed class BauAberto
    {
        public ushort Id;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoBauAberto, w => w.Write(Id));
        public static BauAberto Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoBauAberto, r => new BauAberto { Id = r.ReadUInt16() });
    }

    /// <summary>
    /// Vaso/caixa quebrado. Os quebráveis fazem parte do mapa (mesmo lugar nos dois jogos), então a
    /// identificação é pela posição. Nos dois sentidos; o host repassa aos outros convidados.
    /// </summary>
    internal sealed class Quebrou
    {
        public uint Andar;
        public Vector3 Posicao;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoQuebrou, w => { w.Write(Andar); Bin.Escrever(w, Posicao); });
        public static Quebrou Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoQuebrou, r => new Quebrou { Andar = r.ReadUInt32(), Posicao = Bin.LerVetor(r) });
    }
}

namespace HellClockCoop.Net
{
    /// <summary>Santuário (comum ou amaldiçoado) ou totem de bênção do andar do host.</summary>
    internal sealed class ObjetoSpawn
    {
        public const byte Santuario = 1, Totem = 2;
        public const byte Livre = 0, Usado = 1, Concluido = 2; // Concluido: santuário amaldiçoado purificado

        public ushort Id;
        public uint Andar;
        public byte Tipo;
        public int DefinicaoHash;   // ShrineDefinition.HashID (0 no totem)
        public Vector3 Posicao;
        public float Raio = 1f;
        public byte Estado;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoObjetoSpawn, w =>
        {
            w.Write(Id); w.Write(Andar); w.Write(Tipo); w.Write(DefinicaoHash); Bin.Escrever(w, Posicao); w.Write(Raio); w.Write(Estado);
        });

        public static ObjetoSpawn Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoObjetoSpawn, r => new ObjetoSpawn
        {
            Id = r.ReadUInt16(), Andar = r.ReadUInt32(), Tipo = r.ReadByte(), DefinicaoHash = r.ReadInt32(), Posicao = Bin.LerVetor(r), Raio = r.ReadSingle(), Estado = r.ReadByte(),
        });
    }

    /// <summary>Santuário usado (Estado = Usado) ou amaldiçoado purificado (Concluido). Nos dois sentidos; o host repassa.</summary>
    internal sealed class ObjetoUsado
    {
        public ushort Id;
        public byte Estado;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoObjetoUsado, w => { w.Write(Id); w.Write(Estado); });
        public static ObjetoUsado Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoObjetoUsado, r => new ObjetoUsado { Id = r.ReadUInt16(), Estado = r.ReadByte() });
    }
}
