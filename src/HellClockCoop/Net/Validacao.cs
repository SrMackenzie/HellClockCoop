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

        public static bool TimerValido(TimerSync t) =>
            t != null && Finito(t.Timer) && t.Timer >= 0f && Finito(t.TimerRelaxado) && t.TimerRelaxado >= 0f;

        /// <summary>ObjetoUsado com Estado=Concluido (santuário amaldiçoado purificado) só vale vindo do host.</summary>
        public static bool ObjetoUsadoAceito(byte estado, bool doHost) => doHost || estado != ObjetoSpawn.Concluido;
    }
}
