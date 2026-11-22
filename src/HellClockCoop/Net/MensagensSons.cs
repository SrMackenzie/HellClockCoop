using System.Collections.Generic;
using UnityEngine;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Etapa 8 — sons das skills de um jogador (todos → todos no mesmo andar, confiável, por quadro).
    /// Cada item é um evento do FMOD tocado pelo jogo para o jogador local: só o GUID do evento (igual nos
    /// dois jogos), onde tocar e os parâmetros. Som que dura (não é de uma vez só) ganha um Id e termina
    /// com SonsFim.
    /// </summary>
    internal sealed class SonsAliado
    {
        public const byte ModoPonto = 0;    // posição de mundo
        public const byte ModoJogador = 1;  // preso no avatar de quem mandou
        public const byte ModoEfeito = 2;   // preso na cópia do efeito FxId (sem a cópia, toca na posição)
        public const byte ModoMaximo = ModoEfeito;

        public const int MaximoItens = 64;
        public const int MaximoParametros = 4;

        internal struct Parametro
        {
            public string Nome;
            public float Valor;
        }

        internal sealed class Item
        {
            public ushort Id;              // 0 = som de uma vez só
            public int G1, G2, G3, G4;     // FMOD.GUID (Data1..Data4)
            public byte Modo;
            public ushort FxId;            // quando Modo = ModoEfeito
            public Vector3 Posicao;        // mundo (em todos os modos: fallback e validação de distância)
            public readonly List<Parametro> Parametros = new List<Parametro>();
        }

        public readonly List<Item> Itens = new List<Item>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoSonsAliado, w =>
        {
            int n = Mathf.Min(Itens.Count, MaximoItens);
            w.Write((byte)n);
            for (int k = 0; k < n; k++)
            {
                Item i = Itens[k];
                w.Write(i.Id); w.Write(i.G1); w.Write(i.G2); w.Write(i.G3); w.Write(i.G4);
                w.Write(i.Modo); w.Write(i.FxId);
                Bin.Escrever(w, i.Posicao);
                int p = Mathf.Min(i.Parametros.Count, MaximoParametros);
                w.Write((byte)p);
                for (int j = 0; j < p; j++) { w.Write(i.Parametros[j].Nome ?? ""); w.Write(i.Parametros[j].Valor); }
            }
        });

        /// <summary>Formato inválido (itens ou parâmetros acima do teto, modo desconhecido) derruba a mensagem inteira.</summary>
        public static SonsAliado Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoSonsAliado, r =>
        {
            var m = new SonsAliado();
            int n = r.ReadByte();
            if (n > MaximoItens) throw new System.FormatException($"{n} sons (máximo {MaximoItens}).");
            for (int k = 0; k < n; k++)
            {
                var i = new Item { Id = r.ReadUInt16(), G1 = r.ReadInt32(), G2 = r.ReadInt32(), G3 = r.ReadInt32(), G4 = r.ReadInt32(), Modo = r.ReadByte(), FxId = r.ReadUInt16(), Posicao = Bin.LerVetor(r) };
                if (i.Modo > ModoMaximo) throw new System.FormatException($"Modo de som {i.Modo} desconhecido.");
                int p = r.ReadByte();
                if (p > MaximoParametros) throw new System.FormatException($"{p} parâmetros de som (máximo {MaximoParametros}).");
                for (int j = 0; j < p; j++) i.Parametros.Add(new Parametro { Nome = Bin.LerTexto(r, Validacao.NomeParametroSomMaximo * 4), Valor = r.ReadSingle() });
                m.Itens.Add(i);
            }
            return m;
        });
    }

    /// <summary>Sons que duravam acabaram em quem lançou (confiável).</summary>
    internal sealed class SonsFim
    {
        public readonly List<ushort> Ids = new List<ushort>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoSonsFim, w =>
        {
            int n = Mathf.Min(Ids.Count, 255);
            w.Write((byte)n);
            for (int k = 0; k < n; k++) w.Write(Ids[k]);
        });

        public static SonsFim Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoSonsFim, r =>
        {
            var m = new SonsFim();
            int n = r.ReadByte();
            for (int k = 0; k < n; k++) m.Ids.Add(r.ReadUInt16());
            return m;
        });
    }

    /// <summary>
    /// Etapa 8 — golpes de um jogador nos inimigos compartilhados (todos → todos no mesmo andar, não confiável,
    /// 20 Hz): o que o jogo passa ao CharacterEffectsController.CreateDamageFX (sangue, som de dor do tipo de
    /// dano, efeito do tipo e luz). Quem recebe repete no mesmo inimigo.
    /// </summary>
    internal sealed class GolpesVistos
    {
        public const int MaximoItens = 255;
        public const byte TiposValidos = 0x0F;   // PHYSICAL, FIRE, PLAGUE, LIGHTNING (bits 0..3)
        public const byte OpcoesValidas = 0x07;  // EFxOnDamage: ShowBlood, ShowDamageType, PlayDamageTypeHitSfx

        internal struct Item
        {
            public ushort Id;           // netId do inimigo, ou DanosVistos.IdBoneco
            public byte Opcoes;         // EFxOnDamage
            public byte Tipos;          // um bit por EDamageTypeDefinition presente no golpe
            public byte Principal;      // EDamageTypeDefinition da maior parte do dano
            public bool Critico;
            public Vector3 Direcao;     // DamageHitInfo.HitDirection
            public Vector3 Deslocamento; // DamageHitInfo.HitPosition - posição do inimigo
        }

        public readonly List<Item> Itens = new List<Item>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoGolpesVistos, w =>
        {
            int n = Mathf.Min(Itens.Count, MaximoItens);
            w.Write((byte)n);
            for (int k = 0; k < n; k++)
            {
                Item i = Itens[k];
                w.Write(i.Id); w.Write(i.Opcoes); w.Write(i.Tipos); w.Write(i.Principal); w.Write(i.Critico);
                Bin.Escrever(w, i.Direcao);
                Bin.Escrever(w, i.Deslocamento);
            }
        });

        public static GolpesVistos Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoGolpesVistos, r =>
        {
            var m = new GolpesVistos();
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                m.Itens.Add(new Item
                {
                    Id = r.ReadUInt16(), Opcoes = r.ReadByte(), Tipos = r.ReadByte(), Principal = r.ReadByte(), Critico = r.ReadBoolean(),
                    Direcao = Bin.LerVetor(r), Deslocamento = Bin.LerVetor(r),
                });
            }
            return m;
        });
    }
}
