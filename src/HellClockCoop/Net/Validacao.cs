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

        /// <summary>ESpecialFloor no jogo 2.1.1.1: None=0, DesertShortcut=1, LaboratoryShortcut=2, Sandbox=3.</summary>
        public const int AtalhoDeserto = 1, AtalhoLaboratorio = 2;

        /// <summary>
        /// PlanoAndar.AndarEspecial (0.21.1): nenhum ou um dos dois atalhos do jogo. O plano de atalho não passa pelas travas de
        /// progresso e de DLC (decisão da 0.19.0), então Sandbox (3) e valores fora do enum seriam uma porta para pular as duas.
        /// </summary>
        public static bool AndarEspecialValido(int especial) => especial == 0 || especial == AtalhoDeserto || especial == AtalhoLaboratorio;

        /// <summary>SpawnAndar (0.21.1): índice do ponto de nascimento dentro dos PlayerSpawnInfos do andar (0..total-1).</summary>
        public static bool SpawnValido(int indice, int total) => indice >= 0 && indice < total;

        /// <summary>EGameOverSource.ManualFinish e Completed no jogo 2.1.1.1.</summary>
        public const int MotivoManualFinish = 1, MotivoCompleted = 3;

        /// <summary>
        /// FimRun no convidado (0.20.0): Completed grava a dungeon como concluída no save. Só vale se o convidado está no
        /// último andar (DungeonProgressManager.IsLastLevel) de uma run que acompanha o host; senão vira ManualFinish
        /// (encerra a run sem concluir). Host que manda Completed no andar 1 não conclui a dungeon de ninguém.
        /// </summary>
        public static int MotivoFimRunAplicado(int motivo, bool ultimoAndar, bool seguindoHost) =>
            motivo == MotivoCompleted && !(ultimoAndar && seguindoHost) ? MotivoManualFinish : motivo;

        /// <summary>Teto de andares que um PlanoAndar faz o convidado avançar de uma vez com a run aberta (0.20.0).</summary>
        public const uint SaltoPlanoMaximo = 3;

        /// <summary>
        /// PlanoAndar com a run do convidado aberta (0.20.0): só avança na mesma run. Plano de atalho especial exige o mesmo
        /// atalho; plano comum exige a mesma masmorra, também saindo de um atalho (a run segue do andar especial para os
        /// comuns: AdvanceLevel zera o ESpecialFloor). Sem isto, o índice de um plano de outra masmorra avançaria a run
        /// atual do convidado.
        /// </summary>
        public static bool PlanoDaRunAberta(int dungeonPlano, int especialPlano, int dungeonAtual, int especialAtual) =>
            especialPlano != 0 ? especialPlano == especialAtual : dungeonPlano == dungeonAtual;

        /// <summary>
        /// Reconexão (0.20.0): com a run aberta no mesmo andar do plano do host, na mesma run e com o save já liberado ali
        /// (Salas.Liberado), o convidado volta a acompanhar (o Completed do host vale para ele no último andar).
        /// </summary>
        public static bool RetomaRunDoHost(uint indicePlano, uint atual, bool mesmaRun, bool liberado) =>
            indicePlano == atual && mesmaRun && liberado;

        /// <summary>Andares a avançar até o plano: 0 se o plano não está à frente; no máximo SaltoPlanoMaximo (o resto vem nos próximos planos).</summary>
        public static uint SaltoPlano(uint indicePlano, uint atual) =>
            indicePlano <= atual ? 0u : System.Math.Min(indicePlano - atual, SaltoPlanoMaximo);

        // Inimigos do host (0.20.0). O multiplicador de XP das ondas de sobrevivência (SurvivalEventManager._xpMultiplier)
        // e a quantidade de afixos (DungeonConfig._rareAffixesPerFloor) vêm de campos serializados do asset, sem valor no
        // IL da 2.1.1.1: tetos folgados, para recusar só o absurdo.
        public const float MultiplicadorXpMaximo = 10f;
        public const int AfixosMaximo = 32;
        public const int TipoInimigoMaximo = 6;       // EEnemyType: Regular=0 .. Unique=6
        public const float AtrasoSpawnMaximo = 30f;   // SpawnBehaviourConfig.DelayToSpawn
        public const byte AjustesConhecidos = 15;     // InimigoAjuste: Invocacao | Descartavel | Xp | Tesouro
        public const float CoordenadaMaxima = 100000f;

        public static bool PosicaoValida(float x, float y, float z) =>
            Finito(x) && Finito(y) && Finito(z)
            && System.Math.Abs(x) <= CoordenadaMaxima && System.Math.Abs(y) <= CoordenadaMaxima && System.Math.Abs(z) <= CoordenadaMaxima;

        public static bool FracaoValida(float v) => Finito(v) && v >= 0f && v <= 1f;

        /// <summary>Ajustes só com as marcas conhecidas; com a marca Xp, multiplicador finito de 0 a MultiplicadorXpMaximo.</summary>
        public static bool AjusteValido(byte ajustes, float multiplicadorXp) =>
            (ajustes & ~AjustesConhecidos) == 0
            && ((ajustes & 4) == 0 || (Finito(multiplicadorXp) && multiplicadorXp >= 0f && multiplicadorXp <= MultiplicadorXpMaximo));

        /// <summary>
        /// InimigoSpawn (0.20.0): tipo no enum, afixos até o teto, ajustes válidos, vida em fração de 0 a 1, atraso finito
        /// de 0 a AtrasoSpawnMaximo, giro finito e as duas posições (inimigo e origem do efeito) finitas e dentro do teto.
        /// </summary>
        public static bool InimigoSpawnValido(byte tipo, int afixos, byte ajustes, float multiplicadorXp, float vida, float atraso, float yaw,
                                              float x, float y, float z, float ox, float oy, float oz) =>
            tipo <= TipoInimigoMaximo && afixos >= 0 && afixos <= AfixosMaximo && AjusteValido(ajustes, multiplicadorXp)
            && FracaoValida(vida) && Finito(atraso) && atraso >= 0f && atraso <= AtrasoSpawnMaximo && Finito(yaw)
            && PosicaoValida(x, y, z) && PosicaoValida(ox, oy, oz);

        // Teto por andar do que o host manda criar no convidado. 0.21.1: genérico. 0.22.0: o convidado calcula o teto de cada
        // andar com os dados do próprio jogo para aquele andar (Fase3.TetoDoAndar, IL da 2.1.1.1) e o fator de quantidade do
        // mod; os genéricos abaixo viram o limite de cima e o valor de quando os dados não puderam ser lidos (-1).
        // Três contagens, porque as fontes são diferentes:
        //  - andar: geração do andar (packs, elites com lacaios, esquadrões, opcionais, par do chefe, único de Nightmare);
        //  - sobrevivência (marca do spawn): ondas do andar de sobrevivência ou evento (santuário/Miracles, config própria);
        //  - invocações (ajuste Invocacao/Descartavel): skills de inimigo, sem XP; dinâmicas, teto fixo a medir pelos logs.
        public const int InimigosPorAndarMaximo = 6000;
        public const int ChefesPorAndarMaximo = 16;   // EEnemyType Boss (3) e Unique (6)
        public const int BausPorAndarMaximo = 32;
        public const byte TipoBoss = 3, TipoUnico = 6;

        /// <summary>Folga sobre o fator de quantidade do mod: cobre os nós da árvore do host que aumentam a quantidade.</summary>
        public const float FolgaQuantidade = 1.5f;
        /// <summary>Folga fixa do andar: opcionais, goblin, par do chefe, único de Nightmare.</summary>
        public const int FolgaInimigosAndar = 40;
        /// <summary>Sobrevivência de evento (santuário, Miracles) em qualquer andar: config fora do andar; medir pelos logs.</summary>
        public const int SobrevivenciaEventoMaximo = 1500;
        /// <summary>Invocações e descartáveis por andar (skills de inimigo; sem XP no convidado); medir pelos logs.</summary>
        public const int InvocacoesPorAndarMaximo = 3000;
        /// <summary>Folga dos baús: santuário amaldiçoado e baús de evento.</summary>
        public const int FolgaBaus = 4;

        public static bool EhChefe(byte tipo) => tipo == TipoBoss || tipo == TipoUnico;

        /// <summary>
        /// Teto de inimigos de uma contagem, a partir do total que os dados do jogo permitem no andar (-1 = não lido: genérico).
        /// O total vezes o fator de quantidade do mod (nunca abaixo de 1) vezes a folga, mais a folga fixa; nunca acima do genérico.
        /// </summary>
        public static int TetoInimigos(int doJogo, float fatorQuantidade, int folgaFixa)
        {
            if (doJogo < 0 || !Finito(fatorQuantidade)) return InimigosPorAndarMaximo;
            // Round antes do Ceiling: 1.6f vira 1,6000000238 em double e arredondaria 240 para 241.
            double teto = System.Math.Ceiling(System.Math.Round(doJogo * (double)System.Math.Max(1f, fatorQuantidade) * FolgaQuantidade, 3)) + System.Math.Max(0, folgaFixa);
            return teto >= InimigosPorAndarMaximo ? InimigosPorAndarMaximo : (int)teto;
        }

        /// <summary>Ondas: o andar não é de sobrevivência (vale só o teto de evento). -1 continua sendo "dados não lidos".</summary>
        public const int SemOndas = -2;

        /// <summary>
        /// Teto da sobrevivência: o das ondas (do andar ou da última sobrevivência da run, que pode seguir por vários andares)
        /// e, no mínimo, o de evento; sem ondas no andar, o de evento; dados não lidos (-1), o genérico.
        /// </summary>
        public static int TetoSobrevivencia(int ondasDoJogo, float fatorQuantidade) =>
            ondasDoJogo == SemOndas ? SobrevivenciaEventoMaximo
            : ondasDoJogo < 0 ? InimigosPorAndarMaximo
            : System.Math.Max(SobrevivenciaEventoMaximo, TetoInimigos(ondasDoJogo, fatorQuantidade, FolgaInimigosAndar));

        public static int TetoBaus(int doJogo) =>
            doJogo < 0 ? BausPorAndarMaximo : System.Math.Min(BausPorAndarMaximo, doJogo + FolgaBaus);

        /// <summary>Inimigo do host cabe no andar: a contagem dele abaixo do teto dela, e chefe só até o teto de chefes.</summary>
        public static bool InimigoCabeNoAndar(int criadosDaContagem, int tetoDaContagem, int chefesNoAndar, byte tipo) =>
            criadosDaContagem < tetoDaContagem && (!EhChefe(tipo) || chefesNoAndar < ChefesPorAndarMaximo);

        public static bool BauCabeNoAndar(int bausNoAndar, int teto) => bausNoAndar < teto;

        public const byte ContagemAndar = 0, ContagemSobrevivencia = 1, ContagemInvocacoes = 2;

        /// <summary>
        /// Em qual contagem do teto por andar entra um spawn do host: invocação (InimigoAjuste 1; o SetupSummon tira XP e loot)
        /// sem ajuste de XP (4) e sem tesouro próprio é invocação (o tesouro vazio que o próprio SetupSummon põe, ajuste 8 com
        /// EmptyTreasureClass, não conta como próprio); com a marca de sobrevivência, sobrevivência; o resto, do andar. O
        /// descartável (2) fica na do andar: o SetupDisposable só tira o XP, o loot continua.
        /// </summary>
        public static byte ContagemDoSpawn(byte ajustes, bool tesouroProprio, bool sobrevivencia) =>
            (ajustes & 1) != 0 && (ajustes & 4) == 0 && ((ajustes & 8) == 0 || !tesouroProprio) ? ContagemInvocacoes
            : sobrevivencia ? ContagemSobrevivencia : ContagemAndar;

        /// <summary>
        /// XP acima de 1 (ajuste 4) e tesouro próprio (ajuste 8 com um tesouro que não seja o vazio) só em inimigo de
        /// sobrevivência ou chefe (Boss/Unique): são as únicas fontes no jogo (SurvivalEventManager, temporadas). O vazio
        /// (hash 0 ou EmptyTreasureClass, que o SetupSummon e o goblin põem) sempre passa. Sem isso, o host mandaria XP x10 e
        /// tesouro de chefe em inimigos comuns até o teto do andar.
        /// </summary>
        public static bool RecompensaPermitida(byte ajustes, float multiplicadorXp, bool tesouroProprio, bool sobrevivencia, byte tipo)
        {
            if (sobrevivencia || EhChefe(tipo)) return true;
            if ((ajustes & 4) != 0 && multiplicadorXp > 1f) return false;
            return (ajustes & 8) == 0 || !tesouroProprio;
        }

        /// <summary>
        /// AndarLimpo (0.21.1): no mesmo andar, só dispara de novo se o host criou inimigos depois do último disparo (o jogo
        /// dispara toda vez que a contagem volta a 0: ondas, chefe depois dos comuns). -1 = nenhum ainda neste andar/run.
        /// </summary>
        public static bool AndarLimpoNovo(uint andar, long ultimoDisparado, int criadosAgora, int criadosNoUltimo) =>
            (long)andar != ultimoDisparado || criadosAgora > criadosNoUltimo;

        /// <summary>
        /// BauAberto, Quebrou e ObjetoUsado (0.21.1): o convidado só obedece ao host (os dos outros chegam repassados por ele);
        /// o host aceita de quem está no mesmo andar (o id do objeto é do andar). Fora do coop da run, nada.
        /// </summary>
        public static bool OrdemDeObjetoAceita(bool souHost, bool souConvidado, bool doHost, bool mesmoAndar) =>
            souHost ? mesmoAndar : souConvidado && doHost;

        /// <summary>TimerBonus (0.21.1): só com a run do host aberta e de quem divide a run (compatível e dentro da escala).</summary>
        public static bool BonusDoParAceito(bool runAberta, bool compativel, bool foraDaEscala) => runAberta && compativel && !foraDaEscala;

        /// <summary>
        /// Snapshot (0.21.1): tempo, posição (dentro do teto), rotações, tempos e floats dos animators e ossos finitos. NaN no
        /// avatar ou no Animator do aliado quebra a interpolação e a física local.
        /// </summary>
        public static bool SnapshotValido(PlayerSnapshot s)
        {
            if (s == null || !Finito(s.Tempo) || !PosicaoValida(s.Posicao.x, s.Posicao.y, s.Posicao.z)) return false;
            if (!QuaternionFinito(s.Rotacao) || !QuaternionFinito(s.RotacaoModelo)) return false;
            for (int i = 0; i < PlayerSnapshot.QuantidadeAnimators; i++)
            {
                if (!s.AnimatorAtivo[i]) continue;
                AnimatorFrame f = s.Animators[i];
                if (!TodosFinitos(f.NormalizedTimes) || !TodosFinitos(f.LayerWeights) || !TodosFinitos(f.Floats)) return false;
            }
            for (int i = 0; i < s.RotacoesOssos.Length; i++)
            {
                if ((s.MascaraOssos & (1 << i)) != 0 && !QuaternionFinito(s.RotacoesOssos[i])) return false;
            }
            return true;
        }

        private static bool QuaternionFinito(UnityEngine.Quaternion q) => Finito(q.x) && Finito(q.y) && Finito(q.z) && Finito(q.w);

        private static bool TodosFinitos(float[] v)
        {
            if (v == null) return true;
            foreach (float x in v) if (!Finito(x)) return false;
            return true;
        }

        /// <summary>
        /// Host fixo da run (0.21.1): sem run aberta (0), o dono do lobby; com run, o host dela só enquanto ainda é o dono. Se o
        /// lobby mudou de dono no meio da run, ninguém manda (0) até ela acabar.
        /// </summary>
        public static ulong HostEfetivo(ulong hostDaRun, ulong donoDoLobby) => hostDaRun == 0 || hostDaRun == donoDoLobby ? donoDoLobby : 0UL;

        /// <summary>
        /// ExpulsoAviso (0.21.1): vale do host da sala (o da run aberta, ver SessaoCoop.IdDoHost) e só fala de um terceiro
        /// (nem de quem manda nem de quem recebe).
        /// </summary>
        public static bool AvisoExpulsaoValido(ulong de, ulong host, ulong expulso, ulong eu) =>
            de != 0 && de == host && expulso != 0 && expulso != de && expulso != eu;

        // Santuários e totens do host (0.20.0). O jogo cria no máximo 1 santuário amaldiçoado por andar
        // (LevelDefinition.GenerateCursedShrineSpawnInfo devolve um só) e dá o ponto de constelação 1 vez por andar
        // (DungeonConfig.ClaimShrineLevel, no save). Os comuns vêm de LevelDefinition._shrineRangeAmount (asset).
        public const int AmaldicoadosPorAndarMaximo = 2;
        public const int ObjetosPorAndarMaximo = 32;
        public const float RaioSantuarioMaximo = 200f;

        /// <summary>ObjetoSpawn: tipo conhecido (santuário 1, totem 2), raio finito e positivo até o teto, posição válida.</summary>
        public static bool ObjetoSpawnValido(byte tipo, float raio, float x, float y, float z) =>
            (tipo == ObjetoSpawn.Santuario || tipo == ObjetoSpawn.Totem)
            && Finito(raio) && raio > 0f && raio <= RaioSantuarioMaximo && PosicaoValida(x, y, z);

        /// <summary>Teto por andar do que o host manda criar no convidado (contagens do andar antes deste objeto).</summary>
        public static bool ObjetoCabeNoAndar(int objetosNoAndar, int amaldicoadosNoAndar, bool amaldicoado) =>
            objetosNoAndar < ObjetosPorAndarMaximo && (!amaldicoado || amaldicoadosNoAndar < AmaldicoadosPorAndarMaximo);

        /// <summary>
        /// Porta de entrada (0.20.0): de um par que ainda não mandou Hello compatível, só passam Hello, Tchau e Expulso.
        /// O resto (planos, inimigos, golpes...) de um desconhecido do lobby é descartado antes de chegar ao jogo.
        /// </summary>
        public static bool PortaDeEntrada(byte tipo, bool helloRecebido, bool compativel) =>
            tipo == Protocolo.TipoHello || tipo == Protocolo.TipoTchau || tipo == Protocolo.TipoExpulso || (helloRecebido && compativel);

        /// <summary>Teto de itens de uma mensagem Golpes (0.20.0): acima disso a mensagem inteira é recusada. Sem teto de dano.</summary>
        public const int ItensGolpesPorMensagemMaximo = 512;

        /// <summary>Golpes (0.20.0): do convidado compatível, no mesmo andar e até o teto de itens (0.21.0: DLC diferente vale).</summary>
        public static bool GolpesAceitos(int itens, bool compativel, bool mesmoAndar) =>
            compativel && mesmoAndar && itens <= ItensGolpesPorMensagemMaximo;

        /// <summary>P2P_Transport_ICE_Enable (Steamworks): -1 = padrão do usuário (ICE, pode expor o IP); 0 = sem ICE, só relay.</summary>
        public const int IcePadraoDoUsuario = -1, IceDesligado = 0;

        /// <summary>Relay obrigatório (0.20.0): com quem não é amigo na Steam, a sessão abre sem ICE (o IP não passa); amigos como antes.</summary>
        public static int IceDoPar(bool amigo) => amigo ? IcePadraoDoUsuario : IceDesligado;

        /// <summary>Conferência depois de conectar (0.20.0): com quem não é amigo, só vale a conexão pelo relay (flag Relayed).</summary>
        public static bool CaminhoAceito(bool amigo, bool relay) => amigo || relay;

        /// <summary>Teto de andares de um pedido de avanço (portais comuns avançam 1; o jogo ainda limita pelo dele).</summary>
        public const uint AvancoMaximo = 3;

        /// <summary>
        /// PedidoAvanco (0.15.0): só do andar em que o host está (pedido repetido ou atrasado chega com o andar velho
        /// e é recusado), com pelo menos 1 andar (o host limita a AvancoMaximo) e nunca no último andar (lá o avanço
        /// do jogo vira FinishDungeon(Completed), que gravaria a dungeon como concluída no save do host).
        /// </summary>
        public static bool PedidoAvancoValido(uint andarDoPedido, uint andares, uint andarDoHost, bool hostNoUltimoAndar) =>
            !hostNoUltimoAndar && andarDoPedido == andarDoHost && andares >= 1;

        /// <summary>Reanimando (0.16.0): do andar em que você está, estado 0 a 2 e fração finita de 0 a 1.</summary>
        public static bool ReanimandoValido(uint andarDaMensagem, byte estado, float fracao, uint andarLocal) =>
            andarDaMensagem == andarLocal && estado <= 2 && Finito(fracao) && fracao >= 0f && fracao <= 1f;

        /// <summary>
        /// Reanimou (0.16.0): o caído só levanta se a mensagem é do andar dele e quem reanimou está perto (pela posição
        /// do último snapshot, com folga para o atraso da rede): mensagem forjada de longe não levanta ninguém.
        /// </summary>
        public static bool ReanimouValido(uint andarDaMensagem, uint andarLocal, float distancia, float raio) =>
            andarDaMensagem == andarLocal && Finito(distancia) && distancia >= 0f && distancia <= raio + FolgaReanimarMetros;

        /// <summary>Marcador (0.17.0): alcance a partir de quem marca (o cursor além disso marca na borda do alcance).</summary>
        public const float AlcanceMarcadorMetros = 30f;

        /// <summary>Folga sobre o alcance para o atraso da rede (quem marcou andou depois do último snapshot).</summary>
        public const float FolgaMarcadorMetros = 10f;

        /// <summary>Teto de sanidade de cada coordenada do ponto marcado.</summary>
        public const float CoordenadaMaximaMarcador = 100000f;

        /// <summary>Desnível máximo entre o ponto e quem marcou (o ponto fica no plano dos pés de quem marca).</summary>
        public const float DesnivelMaximoMarcadorMetros = 10f;

        /// <summary>
        /// Marcador (0.17.0): só no mesmo andar (cena conhecida e índice iguais), com ponto finito e perto de quem
        /// marcou (pela posição do último snapshot: distância no chão e desnível): ponto forjado longe, no alto ou de
        /// outro andar não aparece.
        /// </summary>
        public static bool MarcadorValido(int cena, uint andar, float x, float y, float z, int cenaLocal, uint andarLocal, float distancia, float desnivel) =>
            cena != -1 && cena == cenaLocal && andar == andarLocal
            && Finito(x) && Finito(y) && Finito(z)
            && System.Math.Abs(x) <= CoordenadaMaximaMarcador && System.Math.Abs(y) <= CoordenadaMaximaMarcador && System.Math.Abs(z) <= CoordenadaMaximaMarcador
            && Finito(distancia) && distancia >= 0f && distancia <= AlcanceMarcadorMetros + FolgaMarcadorMetros
            && Finito(desnivel) && desnivel >= 0f && desnivel <= DesnivelMaximoMarcadorMetros;

        /// <summary>
        /// Reanimou (0.16.0): nova conta das reanimações do andar a partir da Ordem recebida. Fica o maior valor, sem
        /// pular mais de um de uma vez (Ordem forjada alta não infla a duração das próximas); 0 não muda nada.
        /// </summary>
        public static int OrdemReanimacao(byte ordem, int contaAtual) => System.Math.Max(contaAtual, System.Math.Min(ordem, contaAtual + 1));

        /// <summary>
        /// Snapshot (0.16.0): a sequência é igual ou até 64 atrás da última que decidiu Morto (-1 = nenhuma ainda). Mais
        /// que isso conta como contagem recomeçada no outro PC, e o pacote vale.
        /// </summary>
        public static bool SequenciaAtrasada(ushort sequencia, int ultima)
        {
            if (ultima < 0) return false;
            int dif = (short)(sequencia - (ushort)ultima);
            return dif <= 0 && dif >= -64;
        }

        /// <summary>Folga, em metros, sobre o raio de reanimação ao conferir a distância de quem reanimou (atraso do snapshot).</summary>
        public const float FolgaReanimarMetros = 2f;

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

        /// <summary>
        /// ObjetoUsado com Estado=Concluido (santuário amaldiçoado purificado) só vale vindo do host e, desde a 0.20.0, depois
        /// que o santuário foi ativado aqui (Usado): purificar sem a sobrevivência ter começado daria o ponto de graça.
        /// </summary>
        public static bool ObjetoUsadoAceito(byte estado, bool doHost, bool usadoAntes) =>
            estado != ObjetoSpawn.Concluido || (doHost && usadoAntes);

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

        // Sons e golpes vistos dos aliados (0.11.0): só cosméticos, mas cada item vira uma chamada ao FMOD ou um efeito.
        public const float JanelaSonsSegundos = 1f;
        public const int SonsPorJanelaMaximo = 40;        // sons aceitos de um mesmo aliado por segundo
        public const float DistanciaSomMaxima = 40f;      // do avatar de quem mandou até onde o som toca
        public const float DuracaoSomMaxima = 20f;        // som que dura é parado aqui se o fim não chegar
        public const int NomeParametroSomMaximo = 64;
        public const float DeslocamentoGolpeMaximo = 10f; // ponto do golpe até o centro do inimigo
        public const int GolpesVistosPorJanelaMaximo = 120; // golpes vistos aceitos de um mesmo aliado por segundo

        /// <summary>Teto de itens por aliado numa janela fixa de 1 s (como AceitarBonusNaJanela). Recusado não conta.</summary>
        public static bool AceitarNaJanela(JanelaBonus j, float agora, int maximo)
        {
            if (agora - j.Inicio >= JanelaSonsSegundos || agora < j.Inicio)
            {
                j.Inicio = agora;
                j.Quantidade = 0;
                j.Avisou = false;
            }
            if (j.Quantidade >= maximo) return false;
            j.Quantidade++;
            return true;
        }

        public static bool AceitarSomNaJanela(JanelaBonus j, float agora) => AceitarNaJanela(j, agora, SonsPorJanelaMaximo);

        /// <summary>GUID do evento não nulo e ponto finito a no máximo DistanciaSomMaxima do avatar de quem mandou.</summary>
        public static bool SomValido(int g1, int g2, int g3, int g4, float x, float y, float z, float ax, float ay, float az)
        {
            if ((g1 | g2 | g3 | g4) == 0 || !Finito(x) || !Finito(y) || !Finito(z)) return false;
            float dx = x - ax, dy = y - ay, dz = z - az;
            return dx * dx + dy * dy + dz * dz <= DistanciaSomMaxima * DistanciaSomMaxima;
        }

        public static bool ParametroSomValido(string nome, float valor) =>
            !string.IsNullOrEmpty(nome) && nome.Length <= NomeParametroSomMaximo && Finito(valor);

        /// <summary>Opções e tipos só com os bits que o jogo conhece, tipo principal entre os presentes, vetores finitos e perto do inimigo.</summary>
        public static bool GolpeVistoValido(byte opcoes, byte tipos, byte principal, float dx, float dy, float dz, float ox, float oy, float oz)
        {
            if ((opcoes & ~GolpesVistos.OpcoesValidas) != 0 || tipos == 0 || (tipos & ~GolpesVistos.TiposValidos) != 0) return false;
            if (principal > 3 || (tipos & (1 << principal)) == 0) return false;
            if (!Finito(dx) || !Finito(dy) || !Finito(dz) || !Finito(ox) || !Finito(oy) || !Finito(oz)) return false;
            return ox * ox + oy * oy + oz * oz <= DeslocamentoGolpeMaximo * DeslocamentoGolpeMaximo;
        }
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
