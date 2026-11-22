using System;
using System.Linq;
using HarmonyLib;

namespace HellClockCoop
{
    /// <summary>
    /// Catálogo de todos os métodos do jogo que o mod pretende patchear (docs/ARQUITETURA.md).
    /// Validado a cada inicialização: uma atualização do jogo que renomeie ou remova
    /// qualquer um deles aparece no log antes de causar comportamento estranho.
    /// </summary>
    internal static class PatchTargets
    {
        internal struct Alvo
        {
            public string Fase;
            public string Tipo;
            public string Metodo;
            public string Motivo;

            public Alvo(string fase, string tipo, string metodo, string motivo)
            {
                Fase = fase; Tipo = tipo; Metodo = metodo; Motivo = motivo;
            }
        }

        // Tipo = nome completo como aparece no HellClock.dll (namespace + classe).
        private static readonly Alvo[] Fixos =
        {
            // Fase 0 — diagnóstico (patches ativos nesta versão)
            new Alvo("0", "PlayerManager", "Setup", "Detectar o PlayerController local pronto"),
            new Alvo("0", "TimerManager", "StartTimer", "Início da run / valor do Hell Clock"),
            new Alvo("0", "EnemyManager", "AddToAvailableTargets", "Lista de alvos dos inimigos"),
            new Alvo("0", "HellClock.Level.LevelChanger", "ChangeLevel", "Troca de andar e LevelDefinition escolhida"),

            // Fase 1 — fantasma
            new Alvo("1", "HellClock.Steam.SteamManager", "Awake", "Steam inicializado pelo jogo (não inicializar de novo)"),
            new Alvo("1", "HellClock.Character.HellClockCharacterController", "UpdateAnimationParameters", "Amostrar animação do jogador local"),
            new Alvo("1", "BlockLayerMotions", "OnStateEnter", "StateMachineBehaviour neutralizado nos avatares (AvatarAnimatorGuard)"),
            new Alvo("1", "PlayLasso", "OnStateEnter", "StateMachineBehaviour neutralizado nos avatares (AvatarAnimatorGuard)"),

            // Fase 2 — mesma run
            new Alvo("2", "HellClock.Manager.DungeonProgressManager", "StartRunFromDungeon", "Host inicia a run; cliente segue"),
            new Alvo("2", "HellClock.Manager.DungeonProgressManager", "AdvanceLevel", "Avanço de andar comandado pelo host; com o host caído, o portal do convidado vira pedido (0.15.0)"),
            new Alvo("2", "HellClock.Player.PlayerController", "Handle_OnDie", "Jogador caído: a morte do jogador local vira queda; a run só acaba quando todos caem (0.15.0)"),
            new Alvo("2", "HellClock.Character.StatBasedValueController", "Refill", "Subir de nível caído não devolve a vida (0.15.0)"),
            new Alvo("2", "HellClock.Level.LevelBlock", "GenerateNextLevel", "Forçar no cliente a LevelDefinition sorteada pelo host"),
            new Alvo("2", "HellClock.Level.LevelObjects", "SetupEnemies", "Cliente não gera inimigos próprios"),
            new Alvo("2", "HellClock.Level.LevelObjects", "SetupLevelObjects", "Totens, santuários e portais vêm do host"),
            new Alvo("2", "TimerManager", "Update", "Cliente não decrementa o timer; recebe do host. Host caído: o relógio continua (0.15.0)"),
            new Alvo("2", "TimerManager", "IncrementTimer", "Bônus de tempo do cliente vira pedido ao host"),
            new Alvo("2", "HellClock.Manager.TimeManager", "PauseTime", "Sem pausa de timeScale em coop"),
            new Alvo("2", "HellClock.Manager.TimeManager", "PauseGame", "Sem pausa de jogo em coop"),
            new Alvo("2", "HellClock.Manager.DungeonProgressManager", "StartRunFromDungeonCoroutine", "Convidado não inicia run sozinho"),
            new Alvo("2", "HellClock.Manager.DungeonProgressManager", "StartRunFromSpecialLevelBlock", "Convidado não inicia run sozinho"),
            new Alvo("2", "HellClock.Manager.DungeonProgressManager", "InternalAdvanceLevel", "Convidado avança até o andar do host"),
            new Alvo("2", "HellClock.Level.DungeonConfig", "GetLevelBlockInfoFromLevel", "Bloco do andar vem do host"),
            new Alvo("2", "HellClock.Level.AscensionDungeonConfig", "GetLevelBlockInfoFromLevel", "Bloco do andar vem do host (Ascensão)"),
            new Alvo("2", "HellClock.Level.DynamicDungeonConfig", "GetLevelBlockInfoFromLevel", "Bloco do andar vem do host (dinâmico)"),
            new Alvo("2", "HellClock.Level.LevelObjects", "SetupPlayerSpawn", "Mesmo ponto de spawn do host; no andar novo, o caído volta (0.15.0)"),
            new Alvo("2", "HellClock.Manager.SurvivalEventManager", "StartSurvival", "Sem ondas de sobrevivência no convidado (Fase 2)"),

            // Fase 3 — combate
            new Alvo("2", "EnemyManager", "SpawnEnemy", "Convidado sem inimigos próprios (Fase 2); réplicas e numeração no host (Fase 3a); boneco de treino do hub para os números dos aliados"),
            new Alvo("3", "EnemyManager", "Handle_EnemyDied", "Morte no host vira ordem de morte da réplica"),
            new Alvo("3", "EnemyManager", "DisposeEnemy", "Descarte sem morte (host avisa; convidado esquece a réplica)"),
            new Alvo("3", "EnemyManager", "RemoveEnemy", "Lê _ignoreLevelClear: andar limpo do convidado vem do host"),
            new Alvo("3", "HellClock.Character.Enemy.EnemyController", "Update", "Réplica sem IA"),
            new Alvo("3", "HellClock.Character.Enemy.EnemyController", "CheckForSpawn", "Aliados também acordam inimigos no host"),
            new Alvo("3", "HellClock.Character.Enemy.EnemyController", "SetupSummon", "Invocações sem XP/loot também na réplica"),
            new Alvo("3", "HellClock.Character.Enemy.EnemyController", "SetupDisposable", "Descartáveis sem XP também na réplica"),
            new Alvo("3", "HellClock.Character.Enemy.EnemyController", "SetExperienceMultiplier", "XP das ondas de sobrevivência também na réplica (0.10.1)"),
            new Alvo("3", "HellClock.Character.Enemy.EnemyController", "SetInstanceTreasureClassOverride", "Tesouro próprio (sobrevivência, temporadas) também na réplica (0.10.1)"),
            new Alvo("3", "HellClock.Character.LifeController", "TryHurt", "Golpe do convidado na réplica vira relato ao host"),
            new Alvo("3", "HellClock.Status.StatusController", "AddStatus", "Status de controle do convidado na réplica vai ao host (0.9.0)"),
            new Alvo("3", "HellClock.Chests.ChestInteractable", "ExecuteInteraction", "Baú aberto abre para todos"),
            new Alvo("3", "HellClock.Player.PlayerController", "InteractWith", "Abrir baú por ordem remota sem mexer no jogador local"),
            new Alvo("3", "HellClock.Character.Enemy.BreakableController", "Handle_OnDie", "Vaso/caixa quebrado quebra para todos"),
            new Alvo("3", "HellClock.Level.DrawRandomSceneObject", "OnEnable", "Sorteio da cena (vasos, caixas) com a semente do host (0.9.6)"),
            new Alvo("3", "SmokeDoor", "Start", "Estado inicial da barreira de sangue não vira aviso (0.9.5)"),
            new Alvo("3", "SmokeDoor", "Close", "Barreira de sangue fechada no host fecha para todos (0.9.5)"),
            new Alvo("3", "SmokeDoor", "Open", "Barreira de sangue aberta no host abre para todos (0.9.5)"),
            new Alvo("3", "HellClock.Pool.LevelObjectPoolController", "SpawnChest", "Convidado cria a cópia do baú do host"),
            new Alvo("3", "HellClock.Skill.ActiveSkill", "Execute", "Skill de inimigo do host repetida na réplica (3c)"),
            new Alvo("3", "HellClock.Skill.ActiveSkill", "get_CurrentSkillAvailabilityStatus", "Skill comandada pelo host sempre disponível na réplica"),
            new Alvo("3", "HellClock.Damage.DamageViewManager", "SpawnDamageView", "Números de dano dos aliados"),
            new Alvo("3", "HellClock.VFX.GameplayFx", "CreateFx", "Efeitos das skills do jogador aparecem para os outros (3b)"),
            new Alvo("3", "EnemyManager", "AddToAvailableTargets", "Avatares dos aliados como alvo dos inimigos no host (3c)"),
            new Alvo("3", "HellClock.Shrine.ShrineInteractable", "ExecuteInteraction", "Santuário usado vale para todos"),
            new Alvo("3", "HellClock.Shrine.CursedShrineInteractable", "OnSurvivalEnd", "Santuário amaldiçoado purificado pelo host"),
            new Alvo("3", "HellClock.Pool.LevelObjectPoolController", "SpawnShrine", "Convidado cria a cópia do santuário do host"),
            new Alvo("3", "HellClock.Pool.LevelObjectPoolController", "SpawnBlessingTotem", "Convidado cria o próprio totem no lugar do totem do host"),
            new Alvo("3", "HellClock.AI.Enemy.EnemyAIController", "get_IsAggressive", "Réplica agressiva como no host (chefe: dano, barra, câmera)"),
            new Alvo("3", "HellClock.Manager.DungeonProgressManager", "FinishDungeon", "Fim da run (timer, todos caídos) decidido pelo host"),
            new Alvo("3c", "EnemyManager", "TryToGetTargetForEnemy", "Já escolhe o alvo mais próximo entre _availableTargets"),
            new Alvo("3c", "HellClock.AI.Enemy.EnemyAIController", "SetCurrentTarget", "Observar/replicar o alvo escolhido"),
            new Alvo("3c", "HellClock.Damage.DamageDealer", "PerformDamage", "Origem do dano (crit, modificadores)"),

            // Balanceamento (trilha permanente)
            new Alvo("3", "EnemyManager", "ApplySheetScaling", "Vida e dano dos inimigos por número de jogadores"),
            new Alvo("3", "TimerManager", "IncrementTimer", "Tempo ganho no Hell Clock por número de jogadores"),
            new Alvo("3", "HellClock.Drop.DropBalance", "CalculateExperienceDrop", "XP de cada inimigo por número de jogadores (em run)"),
            new Alvo("3", "HellClock.Drop.GoldTreasureClass", "RollGoldValue", "Ouro de drop e bênção por número de jogadores (em run)"),
            new Alvo("3", "HellClock.Drop.SoulStoneTreasureClass", "RollSoulStonesAmount", "Soulstones de drop e bênção por número de jogadores (em run)"),
            new Alvo("3", "HellClock.Drop.CurrencyTreasureClass", "RollLoot", "Chance de a moeda de criação sair, por número de jogadores (em run)"),

            // Fase 4 — loot
            new Alvo("4", "DropManager", "SetupPlayerController", "Drops instanciados por jogador (namespace global, não HellClock.Drop)"),

            // Fase 5 — polimento (interface; fora das fases ativas: se sumir, só este patch cai e o menu segue sem segurar os eixos)
            new Alvo("5", "HellClock.GameInput.InputHandler", "Update", "Zerar movimento e mira com um painel do mod aberto (menu do coop, Novidades)"),

            // Etapa 8 — sons e golpes dos aliados (0.11.0). Fase "8": só cosmético; se faltar, avisa e o resto do mod roda.
            // Os métodos de contexto das skills (ContextosDeSom.Lista) entram no fim da lista, em Todos.
            new Alvo("8", "HellClock.Audio.AudioController", "PlaySfx", "Som num ponto numa skill do jogador local vai aos aliados"),
            new Alvo("8", "HellClock.Audio.AudioController", "PlayAndAttachSfx", "Som preso ao jogador local ou a um efeito dele vai aos aliados"),
            new Alvo("8", "HellClock.VFX.CharacterEffectsController", "CreateDamageFX", "Sangue, som de dor e efeito do golpe do jogador local vão aos aliados"),
            new Alvo("8", "HellClock.Damage.DamageDealer", "PerformDamageWrapper", "Sons do golpe (dor, morte) não vão como som de skill"),
        };

        internal static readonly Alvo[] Todos = Fixos
            .Concat(Fase3.ContextosDeSom.Lista.Select(c => new Alvo("8", c.Tipo, c.Metodo, c.Corrotina ? "Contexto de som da skill (corrotina: MoveNext)" : "Contexto de som da skill")))
            .ToArray();

        /// <summary>Fases com patches ativos nesta versão; se faltar qualquer alvo delas, nada é patcheado.</summary>
        private static readonly string[] FasesAtivas = { "0", "1", "2", "3" };

        /// <summary>Retorna quantos alvos das fases ativas estão faltando.</summary>
        public static int ValidarTodos()
        {
            int faltandoFase0 = 0;
            int ok = 0;
            foreach (Alvo alvo in Todos)
            {
                Type tipo = AccessTools.TypeByName(alvo.Tipo);
                bool existe = tipo != null && AccessTools.GetDeclaredMethods(tipo).Any(m => m.Name == alvo.Metodo);
                if (existe)
                {
                    ok++;
                    continue;
                }
                string onde = tipo == null ? "classe não encontrada" : "método não encontrado";
                Plugin.Log.LogWarning($"[Validação] Fase {alvo.Fase}: {alvo.Tipo}.{alvo.Metodo} — {onde} ({alvo.Motivo})");
                if (System.Array.IndexOf(FasesAtivas, alvo.Fase) >= 0)
                {
                    faltandoFase0++;
                }
            }
            Plugin.Log.LogInfo($"[Validação] {ok}/{Todos.Length} alvos de patch encontrados no HellClock.dll.");
            return faltandoFase0;
        }
    }
}
