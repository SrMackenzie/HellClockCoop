namespace HellClockCoop.Net
{
    /// <summary>
    /// Filtros dos valores que chegam pela rede, antes de virarem chamada ao jogo (e, por consequência, gravação
    /// do save local). Sem Unity nem tipos do jogo: testados no tools/codectest.
    /// </summary>
    internal static class Validacao
    {
        /// <summary>Teto de sanidade do índice de andar quando não há DungeonConfig para comparar (andar especial).</summary>
        public const uint AndarMaximoSemDungeon = 200;

        /// <summary>Teto de um único bônus de tempo pedido por convidado (relíquias e drops dão bem menos).</summary>
        public const float BonusTempoMaximo = 120f;

        /// <summary>EGameOverSource no jogo 2.1.1.1: Defeat=0, ManualFinish, Timer, Completed, ManualFinishWithPortal=4.</summary>
        public const int MotivoFimRunMaximo = 4;

        public static bool Finito(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>
        /// Índice do andar dentro do limite. <paramref name="maxIndice"/> = DungeonConfig.MaxLevelIndex da dungeon do
        /// plano, que é a QUANTIDADE de andares (o jogo usa IsLastLevel = índice + 1 >= MaxLevelIndex): índices válidos
        /// 0..MaxLevelIndex-1. Negativo quando não há dungeon (andar especial), e aí vale o teto de sanidade.
        /// </summary>
        public static bool AndarValido(uint indice, int maxIndice) =>
            maxIndice >= 0 ? indice < (uint)maxIndice : indice <= AndarMaximoSemDungeon;

        public static bool MotivoFimRunValido(int motivo) => motivo >= 0 && motivo <= MotivoFimRunMaximo;

        public static bool BonusTempoValido(float segundos) => Finito(segundos) && segundos > 0f && segundos <= BonusTempoMaximo;

        /// <summary>Janela do teto acumulado de bônus de tempo, por convidado.</summary>
        public const float JanelaBonusSegundos = 60f;

        /// <summary>Soma máxima de bônus de tempo aceitos de um mesmo convidado dentro de uma janela.</summary>
        public const float BonusPorJanelaMaximo = 120f;

        /// <summary>
        /// Quantidade máxima de bônus aceitos de um convidado numa janela (2 por segundo): bônus minúsculos nunca chegam
        /// ao teto de segundos, mas cada um vira uma linha no log do host.
        /// </summary>
        public const int MensagensBonusPorJanela = 120;

        /// <summary>
        /// Teto acumulado (0.8.5): cada bônus precisa ser válido (BonusTempoValido) e caber na janela fixa de 60 s do
        /// convidado: até 120 s somados e até 120 bônus. Vários bônus de 120 s seguidos deixariam o Hell Clock ilimitado
        /// para todos. A janela recomeça quando passa o tempo dela (ou se o relógio voltar); na virada, até o dobro pode
        /// passar em sequência, mas a média fica no teto. Recusado não conta.
        /// </summary>
        public static bool AceitarBonusNaJanela(JanelaBonus j, float agora, float segundos)
        {
            if (agora - j.Inicio >= JanelaBonusSegundos || agora < j.Inicio)
            {
                j.Inicio = agora;
                j.Soma = 0f;
                j.Quantidade = 0;
                j.Avisou = false;
            }
            if (!BonusTempoValido(segundos) || j.Quantidade >= MensagensBonusPorJanela || j.Soma + segundos > BonusPorJanelaMaximo) return false;
            j.Soma += segundos;
            j.Quantidade++;
            return true;
        }

        /// <summary>
        /// Dano de um golpe do convidado: finito e positivo. Barra NaN/infinito, que corromperiam a vida do inimigo no host.
        /// Não há teto de valor: o dano do convidado é confiável (sai do save dele, seção 2 da ARQUITETURA).
        /// </summary>
        public static bool DanoValido(float valor) => Finito(valor) && valor > 0f;

        public static bool TimerValido(TimerSync t) =>
            t != null && Finito(t.Timer) && t.Timer >= 0f && Finito(t.TimerRelaxado) && t.TimerRelaxado >= 0f;

        /// <summary>ObjetoUsado com Estado=Concluido (santuário amaldiçoado purificado) só vale vindo do host.</summary>
        public static bool ObjetoUsadoAceito(byte estado, bool doHost) => doHost || estado != ObjetoSpawn.Concluido;
    }

    /// <summary>Estado do teto acumulado de bônus de tempo de um convidado (ver Validacao.AceitarBonusNaJanela).</summary>
    internal sealed class JanelaBonus
    {
        public float Inicio;
        public float Soma;
        public int Quantidade;
        public bool Avisou;   // um aviso por janela, para o log não encher
    }
}
