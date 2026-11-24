using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Fase 3a — inimigos do host replicados no convidado.
    /// Spawn, morte e sumiço vão pelo canal confiável; o estado (posição, vida, animação) vai a 10 Hz
    /// pelo canal não confiável, só dos inimigos perto de algum aliado e só o que mudou (protocolo 25). Os golpes
    /// do convidado vão ao host pelo canal confiável, somados por inimigo.
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
        public float MultiplicadorXp = 1f; // EnemyController.SetExperienceMultiplier (ondas de sobrevivência)
        public int TesouroHash;            // TreasureClass.HashID de SetInstanceTreasureClassOverride (0 = nenhum)

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
            w.Write(MultiplicadorXp);
            w.Write(TesouroHash);
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
            s.MultiplicadorXp = r.ReadSingle();
            s.TesouroHash = r.ReadInt32();
            return s;
        });
    }

    /// <summary>
    /// Ajuste feito pelo jogo logo depois do spawn. Invocação (SetupSummon): sem XP e sem loot.
    /// Descartável (SetupDisposable): sem XP. Sem isto o convidado ganharia loot de invocações.
    /// Xp (SetExperienceMultiplier): multiplicador das ondas de sobrevivência.
    /// Tesouro (SetInstanceTreasureClassOverride): tesouro próprio da onda ou do chefe da sobrevivência (o santuário de
    /// Miracles abre a sobrevivência com tesouro vazio) ou vazio no único de Nightmare. Sem isto o convidado sorteava o
    /// loot comum do andar.
    /// </summary>
    internal sealed class InimigoAjuste
    {
        public const byte Invocacao = 1;
        public const byte Descartavel = 2;
        public const byte Xp = 4;
        public const byte Tesouro = 8;

        public ushort Id;
        public byte Ajuste;
        public float MultiplicadorXp = 1f; // vale só com a marca Xp
        public int TesouroHash;            // vale só com a marca Tesouro

        /// <summary>Junta este ajuste no spawn (pendente no convidado, ou o guardado no host para quem chega depois).</summary>
        public void JuntarEm(InimigoSpawn s)
        {
            s.Ajustes |= Ajuste;
            if ((Ajuste & Xp) != 0) s.MultiplicadorXp = MultiplicadorXp;
            if ((Ajuste & Tesouro) != 0) s.TesouroHash = TesouroHash;
        }

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoInimigoAjuste, w => { w.Write(Id); w.Write(Ajuste); w.Write(MultiplicadorXp); w.Write(TesouroHash); });
        public static InimigoAjuste Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoInimigoAjuste, r =>
            new InimigoAjuste { Id = r.ReadUInt16(), Ajuste = r.ReadByte(), MultiplicadorXp = r.ReadSingle(), TesouroHash = r.ReadInt32() });
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

    /// <summary>
    /// Estado de vários inimigos num pacote (canal não confiável, até ~1100 bytes). Desde o protocolo 25 cada item traz
    /// só os campos que mudaram desde o último envio (a máscara do item diz quais); as marcas "nasceu" e "agressivo" vão
    /// em todo item. Quem decide o que vai é o <see cref="EstadoEnviado"/> de cada inimigo, no host.
    /// </summary>
    internal sealed class InimigosEstado
    {
        // Byte de marcas do item: duas marcas do inimigo e quais campos vêm a seguir, nesta ordem.
        public const byte MarcaNasceu = 1, MarcaAgressivo = 2;
        public const byte TemPosicao = 4, TemYaw = 8, TemVida = 16, TemAnimacao = 32;
        public const byte TodosOsCampos = TemPosicao | TemYaw | TemVida | TemAnimacao;

        /// <summary>Onde fica, no pacote, o byte com a quantidade de itens (depois do tipo e da sequência).</summary>
        public const int PosicaoDaContagem = 3;

        internal sealed class Item
        {
            public ushort Id;
            public byte Campos;            // Tem*: quais dos campos abaixo estão no pacote
            public Vector3 Posicao;
            public float Yaw;
            public float Vida;
            public bool JaNasceu;
            public bool Agressivo;        // EnemyAIController.IsAggressive (chefe libera o dano e a barra)
            public AnimatorFrame Animacao; // só com TemAnimacao (null = sem Animator ou não veio)
        }

        /// <summary>Cresce a cada pacote; o convidado descarta o que chega fora de ordem (Validacao.SequenciaAtrasada).</summary>
        public ushort Sequencia;
        public readonly List<Item> Itens = new List<Item>();

        public static ushort YawEm16(float yaw) => (ushort)Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 360f * 65535f);
        public static ushort VidaEm16(float vida) => (ushort)Mathf.RoundToInt(Mathf.Clamp01(vida) * 65535f);

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoInimigosEstado, w =>
        {
            w.Write(Sequencia);
            w.Write((byte)Itens.Count);
            foreach (Item i in Itens) EscreverItem(w, i);
        });

        public static void EscreverItem(BinaryWriter w, Item i)
        {
            byte campos = (byte)(i.Campos & TodosOsCampos);
            if (i.Animacao == null) campos = (byte)(campos & ~TemAnimacao);
            w.Write(i.Id);
            w.Write((byte)(campos | (i.JaNasceu ? MarcaNasceu : 0) | (i.Agressivo ? MarcaAgressivo : 0)));
            if ((campos & TemPosicao) != 0) Bin.Escrever(w, i.Posicao);
            if ((campos & TemYaw) != 0) w.Write(YawEm16(i.Yaw));
            if ((campos & TemVida) != 0) w.Write(VidaEm16(i.Vida));
            if ((campos & TemAnimacao) != 0) i.Animacao.Escrever(w);
        }

        public static InimigosEstado Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoInimigosEstado, r =>
        {
            var e = new InimigosEstado { Sequencia = r.ReadUInt16() };
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                var i = new Item { Id = r.ReadUInt16() };
                byte f = r.ReadByte();
                i.Campos = (byte)(f & TodosOsCampos);
                i.JaNasceu = (f & MarcaNasceu) != 0;
                i.Agressivo = (f & MarcaAgressivo) != 0;
                if ((f & TemPosicao) != 0) i.Posicao = Bin.LerVetor(r);
                if ((f & TemYaw) != 0) i.Yaw = r.ReadUInt16() / 65535f * 360f;
                if ((f & TemVida) != 0) i.Vida = r.ReadUInt16() / 65535f;
                if ((f & TemAnimacao) != 0) i.Animacao = AnimatorFrame.Ler(r);
                e.Itens.Add(i);
            }
            return e;
        });
    }

    /// <summary>
    /// O que o host já mandou de um inimigo (protocolo 25). A cada envio (10 por segundo) decide quais campos vão:
    /// o que mudou desde o último valor enviado vai neste envio e no seguinte (o canal não é confiável: a repetição
    /// cobre um pacote perdido), e o envio "completo" manda tudo (a cada segundo, e quando o inimigo volta a ficar perto
    /// de um aliado), para corrigir o que duas perdas seguidas deixaram para trás. Inimigo sem nada a mandar fica fora
    /// do pacote. Sem chamadas à Unity: testado no codectest.
    /// </summary>
    internal sealed class EstadoEnviado
    {
        public const float PosicaoMinima = 0.02f; // metros desde a última posição enviada
        public const int YawMinimo = 182;         // ~1 grau, na escala de 16 bits
        public const float PesoMinimo = 0.05f;    // peso de camada do Animator (0 a 1)
        public const float FloatMinimo = 0.02f;   // parâmetro float do Animator
        public const float RecuoDeTempo = 0.05f;  // tempo do estado menor que o do envio anterior: o estado recomeçou

        private bool _temBase, _temAnimacao, _repetirMarcas;
        private Vector3 _posicao;
        private ushort _yaw, _vida;
        private byte _marcas, _repetir;
        private readonly AnimatorFrame _animacao = new AnimatorFrame();
        private float[] _temposVistos = Array.Empty<float>(); // tempo de cada camada no envio anterior (foi ou não no pacote)

        /// <summary>
        /// Campos (InimigosEstado.Tem*) a mandar agora e, em <paramref name="enviar"/>, se o inimigo entra no pacote
        /// (falso = nada mudou). Guarda como enviado o que devolveu: só chamar quando o item for mesmo para o pacote.
        /// </summary>
        public byte Decidir(InimigosEstado.Item atual, bool completo, out bool enviar)
        {
            ushort yaw = InimigosEstado.YawEm16(atual.Yaw), vida = InimigosEstado.VidaEm16(atual.Vida);
            byte marcas = (byte)((atual.JaNasceu ? InimigosEstado.MarcaNasceu : 0) | (atual.Agressivo ? InimigosEstado.MarcaAgressivo : 0));
            bool temAnimacao = atual.Animacao != null;
            byte presentes = temAnimacao ? InimigosEstado.TodosOsCampos : (byte)(InimigosEstado.TodosOsCampos & ~InimigosEstado.TemAnimacao);

            byte mudou = 0;
            bool marcasMudaram = !_temBase || marcas != _marcas;
            if (!_temBase) mudou = presentes;
            else
            {
                if ((atual.Posicao - _posicao).sqrMagnitude > PosicaoMinima * PosicaoMinima) mudou |= InimigosEstado.TemPosicao;
                int giro = Math.Abs(yaw - _yaw);
                if (giro > 32768) giro = 65536 - giro;
                if (giro > YawMinimo) mudou |= InimigosEstado.TemYaw;
                if (vida != _vida) mudou |= InimigosEstado.TemVida;
                if (temAnimacao && (!_temAnimacao || AnimacaoMudou(_animacao, atual.Animacao, _temposVistos))) mudou |= InimigosEstado.TemAnimacao;
            }
            if (temAnimacao) _temposVistos = Copia(atual.Animacao.NormalizedTimes, _temposVistos);

            byte campos = (byte)((completo ? presentes : (byte)(mudou | _repetir)) & presentes);
            enviar = campos != 0 || marcasMudaram || _repetirMarcas;
            _repetir = mudou;
            _repetirMarcas = marcasMudaram;
            if (!enviar) return 0;

            _temBase = true;
            _marcas = marcas;
            if ((campos & InimigosEstado.TemPosicao) != 0) _posicao = atual.Posicao;
            if ((campos & InimigosEstado.TemYaw) != 0) _yaw = yaw;
            if ((campos & InimigosEstado.TemVida) != 0) _vida = vida;
            if ((campos & InimigosEstado.TemAnimacao) != 0)
            {
                Copiar(atual.Animacao, _animacao);
                _temAnimacao = true;
            }
            return campos;
        }

        /// <summary>
        /// A animação mudou de um jeito que o convidado precisa saber: outro estado, o mesmo estado recomeçado, peso de
        /// camada ou parâmetro diferente. O tempo do estado avançando sozinho não conta: a réplica toca o mesmo estado
        /// por conta própria, e o envio completo acerta o tempo. <paramref name="temposVistos"/>: o tempo de cada camada
        /// no envio anterior (o recomeço se vê contra ele, não contra o último tempo enviado, que pode ser bem mais antigo).
        /// </summary>
        public static bool AnimacaoMudou(AnimatorFrame enviada, AnimatorFrame atual, float[] temposVistos)
        {
            if (enviada.StateHashes.Length != atual.StateHashes.Length || enviada.Floats.Length != atual.Floats.Length
                || enviada.Ints.Length != atual.Ints.Length || enviada.Bools.Length != atual.Bools.Length) return true;
            for (int l = 0; l < atual.StateHashes.Length; l++)
            {
                if (atual.StateHashes[l] != enviada.StateHashes[l]) return true;
                if (l < temposVistos.Length && atual.NormalizedTimes[l] < temposVistos[l] - RecuoDeTempo) return true;
                if (Distante(atual.LayerWeights[l], enviada.LayerWeights[l], PesoMinimo)) return true;
                // Chegar a 0 ou a 1 vai sempre: uma camada de ataque com um resto de peso fica visível na réplica.
                float peso = atual.LayerWeights[l];
                if (peso != enviada.LayerWeights[l] && (peso <= 0f || peso >= 1f)) return true;
            }
            for (int k = 0; k < atual.Floats.Length; k++)
            {
                if (Distante(atual.Floats[k], enviada.Floats[k], FloatMinimo)) return true;
                if (atual.Floats[k] != enviada.Floats[k] && atual.Floats[k] == 0f) return true; // parou: o zero vai sempre
            }
            for (int k = 0; k < atual.Ints.Length; k++) if (atual.Ints[k] != enviada.Ints[k]) return true;
            for (int k = 0; k < atual.Bools.Length; k++) if (atual.Bools[k] != enviada.Bools[k]) return true;
            return false;
        }

        private static bool Distante(float a, float b, float minimo) => Math.Abs(a - b) > minimo;

        private static void Copiar(AnimatorFrame de, AnimatorFrame para)
        {
            para.StateHashes = Copia(de.StateHashes, para.StateHashes);
            para.NormalizedTimes = Copia(de.NormalizedTimes, para.NormalizedTimes);
            para.LayerWeights = Copia(de.LayerWeights, para.LayerWeights);
            para.Floats = Copia(de.Floats, para.Floats);
            para.Ints = Copia(de.Ints, para.Ints);
            para.Bools = Copia(de.Bools, para.Bools);
        }

        private static T[] Copia<T>(T[] de, T[] para)
        {
            if (para.Length != de.Length) para = new T[de.Length];
            Array.Copy(de, para, de.Length);
            return para;
        }
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

    /// <summary>
    /// Status de controle (lentidão, congelamento, atordoamento, enraizamento) que o convidado aplicou
    /// nas réplicas, juntados por inimigo e status (convidado → host, 0.9.0). A duração vai antes da
    /// resistência a controle do inimigo: quem aplica a resistência é o host.
    /// </summary>
    internal sealed class StatusInimigos
    {
        internal struct Item
        {
            public ushort Id;
            public int StatusHash;    // StatusDefinition.HashID
            public float Duracao;     // segundos
            public float Intensidade;
            public int SkillHash;     // SkillDefinition.HashID da skill de origem (0 se não houver)
            public byte Pilhas;
        }

        public readonly List<Item> Itens = new List<Item>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoStatusInimigos, w =>
        {
            w.Write((ushort)Itens.Count);
            foreach (Item i in Itens) { w.Write(i.Id); w.Write(i.StatusHash); w.Write(i.Duracao); w.Write(i.Intensidade); w.Write(i.SkillHash); w.Write(i.Pilhas); }
        });

        public static StatusInimigos Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoStatusInimigos, r =>
        {
            var s = new StatusInimigos();
            int n = r.ReadUInt16();
            for (int k = 0; k < n; k++)
            {
                s.Itens.Add(new Item { Id = r.ReadUInt16(), StatusHash = r.ReadInt32(), Duracao = r.ReadSingle(), Intensidade = r.ReadSingle(), SkillHash = r.ReadInt32(), Pilhas = r.ReadByte() });
            }
            return s;
        });
    }

    internal static class Bin
    {
        public static void Escrever(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        public static Vector3 LerVetor(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>
        /// Lê um texto do BinaryWriter.Write(string) (tamanho 7-bit + UTF-8) com teto em bytes. O ReadString do mscorlib
        /// do jogo reserva a memória pelo tamanho declarado antes de ler: um tamanho falso de 2 GB num pacote de 10 bytes
        /// alocaria isso tudo. Aqui o tamanho é conferido antes; acima do teto, FormatException (a mensagem é recusada).
        /// </summary>
        public static string LerTexto(BinaryReader r, int maxBytes)
        {
            int n = 0, deslocamento = 0;
            byte b;
            do
            {
                if (deslocamento >= 35) throw new FormatException("Tamanho de texto inválido.");
                b = r.ReadByte();
                n |= (b & 0x7F) << deslocamento;
                deslocamento += 7;
            }
            while ((b & 0x80) != 0);
            if (n < 0 || n > maxBytes) throw new FormatException($"Texto de {n} bytes (máximo {maxBytes}).");
            byte[] bytes = r.ReadBytes(n);
            if (bytes.Length != n) throw new EndOfStreamException();
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        // Tetos de texto por campo (bem acima do uso real).
        public const int TextoCurto = 64;    // GUID de prefab, nome de parâmetro de som, versões
        public const int TextoNome = 256;    // nome da Steam (até 32 caracteres)
        public const int TextoCaminho = 512; // caminho de osso ou de nó na hierarquia
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

    /// <summary>
    /// 0.9.5 — barreira de sangue (SmokeDoor do jogo) abriu ou fechou no host. Host → convidados. As portas fazem parte
    /// do bloco do andar, então a identificação é pela posição. Vale também como reenvio (a cada 2 s, para quem chega
    /// depois). PosicaoHost: onde o host está, para levar o convidado quando a barreira fecha nele.
    /// </summary>
    internal sealed class PortaFumaca
    {
        public uint Andar;
        public Vector3 Posicao;
        public bool Fechada;
        public bool Animar;       // argumento animate de SmokeDoor.Open/Close no host
        public Vector3 PosicaoHost;

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoPortaFumaca, w =>
        {
            w.Write(Andar); Bin.Escrever(w, Posicao); w.Write(Fechada); w.Write(Animar); Bin.Escrever(w, PosicaoHost);
        });

        public static PortaFumaca Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoPortaFumaca, r => new PortaFumaca
        {
            Andar = r.ReadUInt32(), Posicao = Bin.LerVetor(r), Fechada = r.ReadBoolean(), Animar = r.ReadBoolean(), PosicaoHost = Bin.LerVetor(r),
        });
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
