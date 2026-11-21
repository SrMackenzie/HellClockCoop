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

        // Status de controle do convidado (0.9.0). A intensidade multiplica (ou divide, no modificador
        // multiplicativo abaixo de 1) o valor do status; o jogo usa 1 como padrão. Abaixo de 1, a divisão
        // transformaria uma lentidão (0,7 de velocidade / 0,1 = 7x) em reforço do inimigo: o mínimo é 1.
        // Valor fora do teto é reduzido ao teto, não recusado.
        public const float DuracaoStatusMaxima = 30f;
        public const float IntensidadeStatusMinima = 1f;
        public const float IntensidadeStatusMaxima = 10f;
        public const int PilhasStatusSemTeto = 10;            // status cujo MaxStacks é 0 ou menos (o jogo não limita)
        public const int ItensStatusPorMensagemMaximo = 512;  // acima disso a mensagem inteira é recusada
        public const int AvisosStatusPorConvidado = 20;       // linhas de aviso por convidado na sessão

        /// <summary>
        /// Pilhas aplicadas no host: 1 se o status não acumula pilhas (StatusDefinition.CanStack falso: Refresh e instâncias
        /// múltiplas; o jogo não soma pilhas num status que já existe, mas soma ao criar); senão até o MaxStacks do status;
        /// sem teto no jogo (0 ou menos), até PilhasStatusSemTeto.
        /// </summary>
        public static int PilhasStatusLimitadas(byte pilhas, int maxStacks, bool acumula)
        {
            int teto = !acumula ? 1 : maxStacks > 0 ? maxStacks : PilhasStatusSemTeto;
            return pilhas > teto ? teto : pilhas;
        }

        /// <summary>Duração finita e positiva (status sem duração não vai), intensidade finita e positiva, ao menos 1 pilha.</summary>
        public static bool StatusValido(float duracao, float intensidade, byte pilhas) =>
            Finito(duracao) && duracao > 0f && Finito(intensidade) && intensidade > 0f && pilhas > 0;

        public static float DuracaoStatusLimitada(float duracao) => duracao > DuracaoStatusMaxima ? DuracaoStatusMaxima : duracao;

        /// <summary>EStatDefinition no jogo 2.1.1.1 (MovementSpeed é o que o jogo usa em IsSlowed).</summary>
        public const int StatVelocidadeMovimento = 6, StatVelocidadeAtaque = 7;

        /// <summary>
        /// Modificador de um status que só reduz velocidade (lentidão, 0.9.1). EModifierType no jogo 2.1.1.1: Additive=0
        /// (reduz abaixo de 0), Multiplicative=1 e MultiplicativeAdditive=2 (fator: reduz de 0 até abaixo de 1, como o
        /// IsMultiplicativeBelowOne do jogo), BaseOverride=3 e AbsoluteOverride=4 (recusados: trocam o valor inteiro).
        /// </summary>
        public static bool ModificadorReduzVelocidade(int stat, int tipo, float valor) =>
            (stat == StatVelocidadeMovimento || stat == StatVelocidadeAtaque) && Finito(valor)
            && (tipo == 0 ? valor < 0f : (tipo == 1 || tipo == 2) && valor >= 0f && valor < 1f);

        public static float IntensidadeStatusLimitada(float intensidade) =>
            intensidade < IntensidadeStatusMinima ? IntensidadeStatusMinima : intensidade > IntensidadeStatusMaxima ? IntensidadeStatusMaxima : intensidade;
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
