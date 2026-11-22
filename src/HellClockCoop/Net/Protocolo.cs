using System;
using System.Collections.Generic;
using System.IO;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Mensagens do mod. Todo pacote começa com 1 byte de tipo.
    /// Canal 0: confiável (controle, eventos). Canal 1: não confiável (snapshots a 20 Hz).
    /// </summary>
    internal static class Protocolo
    {
        /// <summary>Muda sempre que o formato de qualquer mensagem mudar. Pares com versões diferentes não conversam.</summary>
        public const int Versao = 14; // 14: sons das skills (55, 56) e golpes vistos (57) dos aliados (0.11.0)

        public const int CanalConfiavel = 0;
        public const int CanalSnapshots = 1;

        public const byte TipoSnapshot = PlayerSnapshot.TipoMensagem; // 1
        public const byte TipoHello = 10;
        public const byte TipoAparencia = 11;
        public const byte TipoTchau = 12;

        // Fase 2 — run compartilhada (host → convidados, salvo TimerBonus)
        public const byte TipoPlanoAndar = 20;
        public const byte TipoSpawnAndar = 21;
        public const byte TipoTimer = 22;       // canal não confiável
        public const byte TipoTimerBonus = 23;  // convidado → host
        public const byte TipoFimRun = 24;

        // Fase 3a — inimigos do host (host → convidados, salvo Golpes)
        public const byte TipoInimigoSpawn = 30;
        public const byte TipoInimigoMorte = 31;
        public const byte TipoInimigoSumiu = 32;
        public const byte TipoInimigosEstado = 33; // canal não confiável
        public const byte TipoGolpes = 34;         // convidado → host
        public const byte TipoAndarLimpo = 35;
        public const byte TipoInimigoAjuste = 36;

        // Baús e quebráveis
        public const byte TipoBauSpawn = 40;   // host → convidados
        public const byte TipoBauAberto = 41;  // nos dois sentidos (host repassa)
        public const byte TipoQuebrou = 42;    // nos dois sentidos (host repassa)
        public const byte TipoObjetoSpawn = 43; // santuários e totens, host → convidados
        public const byte TipoObjetoUsado = 44; // nos dois sentidos (host repassa)
        public const byte TipoPortaFumaca = 45; // barreira de sangue, host → convidados (0.9.5)

        // Fase 3b/3c — efeitos das skills (todos → todos) e skills dos inimigos (host → convidados)
        public const byte TipoFxNovo = 50;
        public const byte TipoFxMovimento = 51; // canal não confiável
        public const byte TipoFxFim = 52;
        public const byte TipoDanosVistos = 53; // canal não confiável
        public const byte TipoFxNos = 54;       // canal não confiável
        public const byte TipoInimigoSkill = 37;
        public const byte TipoStatusInimigos = 38; // convidado → host (0.9.0)

        // Etapa 8 — sons das skills e golpes dos aliados (0.11.0)
        public const byte TipoSonsAliado = 55;   // todos → todos no mesmo andar
        public const byte TipoSonsFim = 56;
        public const byte TipoGolpesVistos = 57; // canal não confiável

        /// <summary>Chaves gravadas nos dados do lobby da Steam.</summary>
        public const string ChaveLobbyMod = "hcc_mod";
        public const string ChaveLobbyProtocolo = "hcc_proto";
        public const string ChaveLobbyJogo = "hcc_game";
        public const string ChaveLobbyNome = "hcc_host";
    }

    /// <summary>Apresentação: cada par manda ao entrar no lobby e quando alguém novo entra.</summary>
    internal sealed class Hello
    {
        public int Protocolo;
        public string VersaoMod;
        public string VersaoJogo;
        public string Nome;
        /// <summary>Confirma que o remetente já recebeu um Hello do destinatário (aperto de mão de duas vias).</summary>
        public bool JaTeOuvi;
        /// <summary>DLCs instaladas (Constants.DlcConfig). Cursed War muda o conteúdo das runs.</summary>
        public bool Deluxe;
        public bool CursedWar;

        public byte[] Serializar()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Net.Protocolo.TipoHello);
                w.Write(Protocolo);
                w.Write(VersaoMod ?? "");
                w.Write(VersaoJogo ?? "");
                w.Write(Nome ?? "");
                w.Write(JaTeOuvi);
                w.Write(Deluxe);
                w.Write(CursedWar);
                return ms.ToArray();
            }
        }

        public static Hello Desserializar(byte[] dados)
        {
            using (var r = new BinaryReader(new MemoryStream(dados)))
            {
                r.ReadByte();
                return new Hello { Protocolo = r.ReadInt32(), VersaoMod = Bin.LerTexto(r, Bin.TextoCurto), VersaoJogo = Bin.LerTexto(r, Bin.TextoCurto), Nome = Bin.LerTexto(r, Bin.TextoNome), JaTeOuvi = r.ReadBoolean(), Deluxe = r.ReadBoolean(), CursedWar = r.ReadBoolean() };
            }
        }
    }

    /// <summary>
    /// Aparência do personagem: quais peças de roupa estão ativas
    /// (Bota/Calca/Chapeu/Gibao *_lvl_01..05 e afins, sob Pajeu_RIG_360/Mesh).
    /// Identifica cada nó pelo caminho relativo a "Mesh".
    /// </summary>
    internal sealed class Aparencia
    {
        public readonly List<KeyValuePair<string, bool>> Nos = new List<KeyValuePair<string, bool>>();

        public byte[] Serializar()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Protocolo.TipoAparencia);
                w.Write((ushort)Nos.Count);
                foreach (KeyValuePair<string, bool> n in Nos)
                {
                    w.Write(n.Key);
                    w.Write(n.Value);
                }
                return ms.ToArray();
            }
        }

        public static Aparencia Desserializar(byte[] dados)
        {
            using (var r = new BinaryReader(new MemoryStream(dados)))
            {
                r.ReadByte();
                var a = new Aparencia();
                int n = r.ReadUInt16();
                for (int i = 0; i < n; i++)
                {
                    a.Nos.Add(new KeyValuePair<string, bool>(Bin.LerTexto(r, Bin.TextoCaminho), r.ReadBoolean()));
                }
                return a;
            }
        }

        public bool IgualA(Aparencia outra)
        {
            if (outra == null || outra.Nos.Count != Nos.Count) return false;
            for (int i = 0; i < Nos.Count; i++)
            {
                if (Nos[i].Key != outra.Nos[i].Key || Nos[i].Value != outra.Nos[i].Value) return false;
            }
            return true;
        }
    }
}
