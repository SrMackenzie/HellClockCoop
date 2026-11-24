using System.Collections.Generic;

namespace HellClockCoop.Net
{
    /// <summary>
    /// Painel de grupo (0.27.0): o que o painel à esquerda mostra de um jogador além da vida (que já vem no PlayerSnapshot).
    /// Todos → todos os pares compatíveis, confiável, só quando muda (e a cada poucos segundos, para quem chegou depois).
    /// - Skills: o HashID da SkillDefinition de cada slot da barra, na ordem dos slots (0 = slot vazio). Sem recarga: a opção A
    ///   do autor mostra só o que o aliado tem equipado (recarga dos aliados daria vantagem ao grupo).
    /// - Status: até 4, os mais recentes, com o HashID da StatusDefinition e os acúmulos. Sem duração (mesmo motivo).
    /// Quem recebe resolve os ícones pelas coleções do jogo; hash desconhecido só não aparece.
    /// </summary>
    internal sealed class PainelAliado
    {
        public const int MaximoSkills = 8;
        public const int MaximoStatus = 4;

        internal struct Status
        {
            public int Hash;
            public byte Acumulos;
        }

        public readonly List<int> Skills = new List<int>();
        public readonly List<Status> Efeitos = new List<Status>();

        public byte[] Serializar() => Mensagem.Escrever(Protocolo.TipoPainelAliado, w =>
        {
            int n = Skills.Count < MaximoSkills ? Skills.Count : MaximoSkills;
            w.Write((byte)n);
            for (int i = 0; i < n; i++) w.Write(Skills[i]);
            int s = Efeitos.Count < MaximoStatus ? Efeitos.Count : MaximoStatus;
            w.Write((byte)s);
            for (int i = 0; i < s; i++) { w.Write(Efeitos[i].Hash); w.Write(Efeitos[i].Acumulos); }
        });

        /// <summary>Mais skills ou status que o teto derrubam a mensagem inteira.</summary>
        public static PainelAliado Desserializar(byte[] d) => Mensagem.Ler(d, Protocolo.TipoPainelAliado, r =>
        {
            var m = new PainelAliado();
            int n = r.ReadByte();
            if (n > MaximoSkills) throw new System.FormatException($"{n} skills no painel (máximo {MaximoSkills}).");
            for (int i = 0; i < n; i++) m.Skills.Add(r.ReadInt32());
            int s = r.ReadByte();
            if (s > MaximoStatus) throw new System.FormatException($"{s} status no painel (máximo {MaximoStatus}).");
            for (int i = 0; i < s; i++) m.Efeitos.Add(new Status { Hash = r.ReadInt32(), Acumulos = r.ReadByte() });
            return m;
        });

        /// <summary>Mesmo conteúdo (para só reenviar quando muda).</summary>
        public bool IgualA(PainelAliado o)
        {
            if (o == null || o.Skills.Count != Skills.Count || o.Efeitos.Count != Efeitos.Count) return false;
            for (int i = 0; i < Skills.Count; i++) if (Skills[i] != o.Skills[i]) return false;
            for (int i = 0; i < Efeitos.Count; i++) if (Efeitos[i].Hash != o.Efeitos[i].Hash || Efeitos[i].Acumulos != o.Efeitos[i].Acumulos) return false;
            return true;
        }
    }
}
