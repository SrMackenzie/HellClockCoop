using System;
using System.IO;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Fase 2 — plano de um andar sorteado pelo host. O convidado não sorteia nada:
    /// substitui o bloco (LevelBlockInfo) e a LevelDefinition pelos do host.
    /// </summary>
    internal sealed class PlanoAndar
    {
        public uint IndiceAndar;
        public bool Primeiro;          // primeiro andar de uma run nova
        public int DungeonHash;        // DungeonConfig.HashID
        public int AndarEspecial;      // ESpecialFloor
        public int BlocoHash;          // LevelBlock.HashID
        public uint IndiceLocal;       // LevelBlockInfo.LocalLevelIndex
        public bool TemSementeBoss;
        public uint SementeBoss;       // LevelBlockInfo.BossSpawnSeed
        public int BossForcado;        // LevelBlockInfo.ForcedBossHashId
        public int DefinicaoHash;      // LevelDefinition.HashID
        public bool EhBoss;
        public uint SementeCena;       // sorteios da cena (DrawRandomSceneObject: vasos, caixas...); 0 = sem semente

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoPlanoAndar, w =>
        {
            w.Write(IndiceAndar); w.Write(Primeiro); w.Write(DungeonHash); w.Write(AndarEspecial);
            w.Write(BlocoHash); w.Write(IndiceLocal); w.Write(TemSementeBoss); w.Write(SementeBoss);
            w.Write(BossForcado); w.Write(DefinicaoHash); w.Write(EhBoss); w.Write(SementeCena);
        });

        public static PlanoAndar Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoPlanoAndar, r => new PlanoAndar
        {
            IndiceAndar = r.ReadUInt32(), Primeiro = r.ReadBoolean(), DungeonHash = r.ReadInt32(), AndarEspecial = r.ReadInt32(),
            BlocoHash = r.ReadInt32(), IndiceLocal = r.ReadUInt32(), TemSementeBoss = r.ReadBoolean(), SementeBoss = r.ReadUInt32(),
            BossForcado = r.ReadInt32(), DefinicaoHash = r.ReadInt32(), EhBoss = r.ReadBoolean(),
            SementeCena = r.ReadUInt32(),
        });

        public override string ToString() =>
            $"andar {IndiceAndar}{(Primeiro ? " (início da run)" : "")}, bloco {BlocoHash}/{IndiceLocal}, definição {DefinicaoHash}{(EhBoss ? ", boss" : "")}, cena {SementeCena:X8}";
    }

    /// <summary>Qual ponto de spawn do andar o host usou (LevelObjects._currentPlayerSpawnIndex).</summary>
    internal sealed class SpawnAndar
    {
        public uint IndiceAndar;
        public int IndiceSpawn;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoSpawnAndar, w => { w.Write(IndiceAndar); w.Write(IndiceSpawn); });
        public static SpawnAndar Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoSpawnAndar, r => new SpawnAndar { IndiceAndar = r.ReadUInt32(), IndiceSpawn = r.ReadInt32() });
    }

    /// <summary>Valor do Hell Clock do host (4 Hz, canal não confiável).</summary>
    internal sealed class TimerSync
    {
        public float Timer;
        public float TimerRelaxado;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoTimer, w => { w.Write(Timer); w.Write(TimerRelaxado); });
        public static TimerSync Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoTimer, r => new TimerSync { Timer = r.ReadSingle(), TimerRelaxado = r.ReadSingle() });
    }

    /// <summary>Convidado → host: tempo ganho (relíquias, drops de tempo) para somar ao Hell Clock do host.</summary>
    internal sealed class TimerBonus
    {
        public float Segundos;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoTimerBonus, w => w.Write(Segundos));
        public static TimerBonus Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoTimerBonus, r => new TimerBonus { Segundos = r.ReadSingle() });
    }

    /// <summary>Host → convidados: a run terminou (EGameOverSource).</summary>
    internal sealed class FimRun
    {
        public int Motivo;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoFimRun, w => w.Write(Motivo));
        public static FimRun Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoFimRun, r => new FimRun { Motivo = r.ReadInt32() });
    }

    /// <summary>
    /// Convidado → host (0.15.0): com o host caído, o portal do convidado vira este pedido; o host avança o grupo.
    /// IndiceAndar é o andar em que o convidado usou o portal (o host só aceita o andar atual dele).
    /// </summary>
    internal sealed class PedidoAvanco
    {
        public uint IndiceAndar;
        public uint Andares;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoPedidoAvanco, w => { w.Write(IndiceAndar); w.Write(Andares); });
        public static PedidoAvanco Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoPedidoAvanco, r => new PedidoAvanco { IndiceAndar = r.ReadUInt32(), Andares = r.ReadUInt32() });
    }

    /// <summary>
    /// Todos → todos no mesmo andar (0.16.0), a 5 Hz e no canal não confiável: quem está ao lado de um aliado caído
    /// conta o progresso da reanimação. Alvo é o SteamID do caído; Estado: 0 enchendo, 1 interrompido (golpe),
    /// 2 ao lado, mas se movendo ou usando skill ("fique parado"). Fracao de 0 a 1.
    /// </summary>
    internal sealed class Reanimando
    {
        public ulong Alvo;
        public uint IndiceAndar;
        public byte Estado;
        public float Fracao;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoReanimando, w => { w.Write(Alvo); w.Write(IndiceAndar); w.Write(Estado); w.Write(Fracao); });
        public static Reanimando Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoReanimando, r => new Reanimando { Alvo = r.ReadUInt64(), IndiceAndar = r.ReadUInt32(), Estado = r.ReadByte(), Fracao = r.ReadSingle() });
    }

    /// <summary>
    /// Todos → todos no mesmo andar (0.16.0): a reanimação terminou; o caído (Alvo) levanta na máquina dele. Ordem é a
    /// posição desta reanimação no andar (1 = primeira): cada PC fica com o maior valor visto, sem pular mais de um.
    /// </summary>
    internal sealed class Reanimou
    {
        public ulong Alvo;
        public uint IndiceAndar;
        public byte Ordem;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoReanimou, w => { w.Write(Alvo); w.Write(IndiceAndar); w.Write(Ordem); });
        public static Reanimou Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoReanimou, r => new Reanimou { Alvo = r.ReadUInt64(), IndiceAndar = r.ReadUInt32(), Ordem = r.ReadByte() });
    }

    /// <summary>
    /// Todos → todos no mesmo andar (0.17.0): marcador (ping) no ponto do chão, por Interface.Marcadores.Duracao. Cena
    /// (LevelDefinition.HashID) e IndiceAndar (4294967295 fora da run) amarram o ponto ao andar de quem marcou.
    /// </summary>
    internal sealed class Marcador
    {
        public int Cena;
        public uint IndiceAndar;
        public float X, Y, Z;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoMarcador, w => { w.Write(Cena); w.Write(IndiceAndar); w.Write(X); w.Write(Y); w.Write(Z); });
        public static Marcador Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoMarcador, r => new Marcador { Cena = r.ReadInt32(), IndiceAndar = r.ReadUInt32(), X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle() });
    }

    /// <summary>
    /// Convidado → todos os pares (0.19.0): Fora = o save do convidado não chegou à masmorra e ao andar da run do host; ele fica no
    /// hub, não acompanha e sai da escala do grupo. Fora = false quando volta a acompanhar (fim da run ou run liberada).
    /// </summary>
    internal sealed class ForaDaRun
    {
        public bool Fora;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoForaDaRun, w => w.Write(Fora));
        public static ForaDaRun Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoForaDaRun, r => new ForaDaRun { Fora = r.ReadBoolean() });
    }

    internal static class Mensagem
    {
        public static byte[] Escrever(byte tipo, Action<BinaryWriter> corpo)
        {
            using (var ms = new MemoryStream(64))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(tipo);
                corpo(w);
                return ms.ToArray();
            }
        }

        public static T Ler<T>(byte[] dados, byte tipo, Func<BinaryReader, T> corpo)
        {
            using (var r = new BinaryReader(new MemoryStream(dados)))
            {
                byte t = r.ReadByte();
                if (t != tipo) throw new FormatException($"Esperava mensagem {tipo}, veio {t}.");
                return corpo(r);
            }
        }
    }
}
