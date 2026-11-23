using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class StartBattleActionHandlerTests
    {
        private static readonly CharacterDefinition Hero = new CharacterDefinition(
            "hero", "Hero", 1, new[] { "A" }, new[] { "U" });
        private static readonly CharacterDefinition Hero2 = new CharacterDefinition(
            "hero2", "Hero2", 1, new[] { "A" }, new[] { "U" });
        private static readonly CharacterDefinition Hero3 = new CharacterDefinition(
            "hero3", "Hero3", 1, Array.Empty<string>(), Array.Empty<string>());
        private static readonly EnemyDefinition Enemy = new EnemyDefinition(
            "enemy", "Enemy", 5, 3, 10, null, null, null, "T001", null, null, null);
        private static readonly CharacterStatDefinition Stats = new CharacterStatDefinition(new[]
        {
            new CharacterCostStats(1,
                new[] { new CharacterStatOption(7, 2, 1) },
                new[] { new CharacterStatOption(9, 3, 1) },
                new[] { new CharacterStatOption(11, 4, 1) })
        });
        private static readonly StageProgressConfig StageConfig = new StageProgressConfig(
            20, 4, 2, 4, 0, 10, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
        private static readonly ItemDefinition EnemyDefenseItem = new ItemDefinition(
            "enemy-defense", "Enemy Defense", 0, string.Empty, ItemEffectTarget.AllEnemies,
            ItemEffectDuration.ThisBattle,
            new[] { new ItemEffectDefinition(ItemEffectKind.Defense, -1) });
        private static readonly ItemDefinition AllyAttackItem = new ItemDefinition(
            "ally-attack", "Ally Attack", 0, string.Empty, ItemEffectTarget.OneAlly,
            ItemEffectDuration.ThisBattle,
            new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 2) });
        private static readonly CharacterShopConfiguration CharacterShopConfig = new CharacterShopConfiguration(
            new[] { 1, 0, 0, 0, 0 }, new[] { 10, 0, 0, 0, 0 },
            new[] { new[] { 100, 0, 0, 0, 0 } }, new[] { 1 });
        private static readonly AssignedCharacterCosts AssignedCosts = new AssignedCharacterCosts(
            new[] { new KeyValuePair<string, int>(Hero3.DefinitionId, 1) });
        private static readonly EconomyRulesConfiguration EconomyConfig = new EconomyRulesConfiguration(
            4, 2, 1, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
            10, 1, 3, 3, 1, 0);

        private static readonly IReadOnlyList<EnemyDefinition> Enemies = new[] { Enemy };
        private static readonly IReadOnlyList<CharacterDefinition> Characters = new[] { Hero, Hero2, Hero3 };

        private static StageProgressState Preparation(StageChoice choice = StageChoice.SelectA,
            StageProgressConfig config = null)
        {
            config = config ?? StageConfig;
            StageProgressState stage = StageProgressRules.CreateInitialState(config);
            if (config.GetRoundNumber(0) == config.ChoiceRoundNumber)
            {
                stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
                return StageSelectionRules.Select(stage, config, choice).NextState;
            }
            return StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
        }

        private static EnemyBoardState EnemyBoard(int count = 1)
        {
            var instances = new List<EnemyInstance>();
            for (int slot = 0; slot < count; slot++)
            {
                Assert.That(EnemyInstanceFactory.TryCreate(Enemy, 0, slot, 0, 0, 0,
                    0, 0, 0, out EnemyInstance instance, out EnemyInstanceFailure failure), Is.True, failure.ToString());
                instances.Add(instance);
            }
            Assert.That(EnemyBoardState.TryCreate(2, 3, instances, out EnemyBoardState board,
                out EnemyBoardFailure boardFailure), Is.True, boardFailure.ToString());
            return board;
        }

        private static IReadOnlyList<EncounterPoolDefinition> NormalPools() => new[]
        {
            new EncounterPoolDefinition("normal-stage-1", new[] { 0 }, StageEnemyCategory.Normal,
                new[] { new EnemyFormationDefinition("normal-formation", new[]
                {
                    new EnemyFormationSlotDefinition(Enemy.DefinitionId, 0, 0),
                    new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0),
                    new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0),
                    new EnemyFormationSlotDefinition(null, 0, 0)
                }) })
        };

        private static CharacterInstance MakeCharacter(CharacterDefinition definition,
            Pcg32RandomSource statsRandom, string instanceId)
        {
            var instance = new CharacterInstance(instanceId, definition, 1);
            Assert.That(instance.EnsureStats(definition, Stats, statsRandom), Is.True);
            return instance;
        }

        private static ActionExecutionState State(StageProgressState stage = null,
            EconomyState economy = null, BoardState board = null, HandState hand = null,
            CharacterOwnershipState ownership = null, EnemyBoardState enemyBoard = null,
            EnemyPreparationState enemyPreparation = null, ItemEffectState itemEffects = null,
            SimulationRandomStreamsState? randomState = null, BattleState battle = null,
            IEnumerable<ActionId> history = null, CharacterPool pool = null)
        {
            var streams = new SimulationRandomStreams(321UL);
            return new ActionExecutionState(stage ?? Preparation(),
                economy ?? new EconomyState(10, 0, 3, 12, 10, 0, 1, 20, 30),
                pool ?? new CharacterPool(Array.Empty<CharacterPoolEntry>()), new CharacterInstanceFactory(),
                board ?? BoardState.CreateStandard(), hand ?? HandState.CreateStandard(),
                ownership ?? new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                itemEffects ?? new ItemEffectState(), randomState ?? streams.CaptureState(),
                enemyBoard: enemyBoard ?? EnemyBoard(), enemyPreparation: enemyPreparation ?? new EnemyPreparationState(),
                battle: battle, executedActionIds: history);
        }

        private static ActionExecutionContext Context(BattleConfig battleConfig = null,
            IReadOnlyList<CharacterDefinition> selected = null,
            IReadOnlyList<CharacterDefinition> fullRoster = null,
            IReadOnlyList<CharacterDefinition> characterDefinitions = null,
            IReadOnlyList<EnemyDefinition> enemies = null,
            IReadOnlyList<ItemDefinition> items = null, StageProgressConfig stageConfig = null,
            IReadOnlyList<EncounterPoolDefinition> pools = null,
            CharacterShopConfiguration shopConfig = null,
            AssignedCharacterCosts assignedCosts = null) => new ActionExecutionContext(
                stageConfig: stageConfig ?? StageConfig, economyConfig: EconomyConfig,
                battleConfig: battleConfig,
                characterShopConfig: shopConfig, assignedCharacterCosts: assignedCosts,
                characterStatSettings: Stats,
                characterDefinitions: characterDefinitions ?? Characters,
                selectedRoster: selected ?? new[] { Hero },
                fullCharacterRoster: fullRoster ?? new[] { Hero },
                enemyDefinitions: enemies ?? Enemies,
                encounterPools: pools ?? NormalPools(),
                itemDefinitions: items ?? new[] { EnemyDefenseItem, AllyAttackItem });

        private static ActionDispatcher Dispatcher(IEnumerable<IActionHandler> handlers = null) =>
            new ActionDispatcher(handlers ?? AllActionHandlers.Create());

        private static ActionDispatchResult Start(ActionExecutionState state,
            ActionExecutionContext context = null, string id = "start") =>
            Dispatcher().Dispatch(state, new StartBattleAction(new ActionId(id)), context ?? Context());

        private static BoardState BoardWith(params CharacterInstance[] instances)
        {
            BoardState board = BoardState.CreateStandard();
            for (int i = 0; i < instances.Length; i++)
                Assert.That(board.TryWithCharacterAt(i, instances[i].InstanceId, out board), Is.True);
            return board;
        }

        private static CharacterOwnershipState Ownership(params CharacterInstance[] instances) =>
            new CharacterOwnershipState(instances);

        private static SimulationRandomStreamsState ChangeBattleSeed(ulong seed) =>
            new SimulationRandomStreams(seed).CaptureState();

        [Test]
        public void AllActionHandlersRegistersExactlyFifteenKindsAndAllowsOrderIndependentDispatch()
        {
            IReadOnlyList<IActionHandler> handlers = AllActionHandlers.Create();
            Assert.That(handlers.Count, Is.EqualTo(15));
            Assert.That(handlers.Select(h => h.Kind).Distinct().Count(), Is.EqualTo(15));
            Assert.That(handlers.Select(h => h.Kind), Is.EquivalentTo(Enum.GetValues(typeof(GameActionKind))));
            Assert.Throws<ArgumentException>(() => new ActionDispatcher(handlers.Concat(
                new[] { new StartBattleActionHandler() })));

            var reversed = handlers.Reverse().ToArray();
            ActionExecutionState before = State();
            ActionDispatchResult result = Dispatcher(reversed).Dispatch(before,
                new StartBattleAction(new ActionId("reverse-start")), Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Battle));
        }

        [Test]
        public void StartBattleAtomicallyTransitionsStageAndCreatesInitialBattleWithoutConsumingRng()
        {
            ActionExecutionState before = State();
            var battleRngBefore = before.RandomState;
            ActionDispatchResult result = Start(before);
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Battle));
            Assert.That(result.NextState.Battle, Is.Not.Null);
            Assert.That(result.NextState.Battle.Status, Is.EqualTo(BattleExecutionStatus.InProgress));
            Assert.That(result.NextState.Battle.Allies, Is.Empty);
            Assert.That(result.NextState.Battle.Enemies.Count, Is.EqualTo(1));
            Assert.That(result.NextState.RandomState.Battle, Is.EqualTo(battleRngBefore.Battle));
            Assert.That(result.NextState.RandomState.Shop, Is.EqualTo(battleRngBefore.Shop));
            Assert.That(result.NextState.RandomState.Stats, Is.EqualTo(battleRngBefore.Stats));
            Assert.That(result.NextState.RandomState.Encounter, Is.EqualTo(battleRngBefore.Encounter));
            Assert.That(result.NextState.RandomState.Event, Is.EqualTo(battleRngBefore.Event));
            Assert.That(result.NextState.RandomState.Roster, Is.EqualTo(battleRngBefore.Roster));
            Assert.That(result.NextState.RandomState.Economy, Is.EqualTo(battleRngBefore.Economy));
            Assert.That(before.Stage.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(before.Battle, Is.Null);
            Assert.That(result.NextState.HasExecuted(new ActionId("start")), Is.True);
        }

        [Test]
        public void StartBattleIncludesOnlyBoardCharactersInSlotOrderAndUsesUnitAndAttributeSynergy()
        {
            var streams = new SimulationRandomStreams(99UL);
            CharacterInstance a = MakeCharacter(Hero, streams.Stats, "a");
            CharacterInstance b = MakeCharacter(Hero2, streams.Stats, "b");
            BoardState board = BoardWith(a, b);
            var ownership = Ownership(a, b);
            ActionExecutionState before = State(board: board, ownership: ownership,
                hand: HandState.CreateStandard());
            var context = Context(selected: new[] { Hero, Hero2 }, fullRoster: new[] { Hero, Hero2 });

            ActionDispatchResult result = Start(before, context);
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            BattleState battle = result.NextState.Battle;
            Assert.That(battle.Allies.Select(x => x.InstanceId), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(battle.UnitMoves.Count, Is.EqualTo(1));
            Assert.That(battle.UnitMoves[0].UnitName, Is.EqualTo("U"));
            Assert.That(battle.AttributeBlock, Is.EqualTo(1));
            Assert.That(battle.PlayerBlock, Is.EqualTo(1));
        }

        [Test]
        public void StartBattleAppliesEnemyPreparationModifiersWhileKeepingAggregateBaseBoardAndModifierState()
        {
            int[] defense = { -1, 0, 0, 0, 0, 0 };
            int[] health = { -3, 0, 0, 0, 0, 0 };
            var prep = new EnemyPreparationState(null, defense, health, null);
            ActionExecutionState before = State(enemyBoard: EnemyBoard(), enemyPreparation: prep);
            ActionDispatchResult result = Start(before);
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(result.NextState.Battle.Enemies.Count, Is.EqualTo(1));
            Assert.That(result.NextState.Battle.Enemies[0].Defense, Is.EqualTo(2));
            Assert.That(result.NextState.Battle.Enemies[0].MaxHP, Is.EqualTo(7));
            Assert.That(result.NextState.EnemyPreparation, Is.SameAs(prep));
            Assert.That(result.NextState.EnemyBoard, Is.SameAs(before.EnemyBoard));
            Assert.That(result.NextState.EnemyBoard.TryGetAt(0, out EnemyInstance original), Is.True);
            Assert.That(original.Defense, Is.EqualTo(3));
            Assert.That(original.CurrentHealth, Is.EqualTo(10));
        }

        [Test]
        public void StartBattleExcludesEnemiesReducedToZeroHealthByPreparationEffects()
        {
            int[] health = { -10, 0, 0, 0, 0, 0 };
            ActionExecutionState before = State(enemyBoard: EnemyBoard(),
                enemyPreparation: new EnemyPreparationState(null, null, health, null));
            ActionDispatchResult result = Start(before);
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(result.NextState.Battle.Enemies, Is.Empty);
            Assert.That(result.NextState.EnemyBoard.TryGetAt(0, out EnemyInstance baseEnemy), Is.True);
            Assert.That(baseEnemy.CurrentHealth, Is.EqualTo(10));
        }

        [Test]
        public void StartBattleRejectsBoardOverLevelButBoardMovementRemainsAllowed()
        {
            var streams = new SimulationRandomStreams(12UL);
            CharacterInstance a = MakeCharacter(Hero, streams.Stats, "a");
            CharacterInstance b = MakeCharacter(Hero2, streams.Stats, "b");
            BoardState board = BoardWith(a, b);
            ActionExecutionState tooMany = State(board: board, ownership: Ownership(a, b),
                economy: new EconomyState(10, 0, 1, 12, 10, 0, 1, 20, 30));
            ActionDispatchResult rejected = Start(tooMany);
            Assert.That(rejected.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(rejected.Error.Code, Is.EqualTo(ActionErrorCode.BoardLimitExceeded));
            Assert.That(rejected.NextState, Is.SameAs(tooMany));
            Assert.That(tooMany.HasExecuted(new ActionId("start")), Is.False);

            ActionDispatchResult moved = Dispatcher().Dispatch(tooMany,
                new MoveOrSwapBoardAction(new ActionId("move-over-level"), 0, 4), Context());
            Assert.That(moved.Succeeded, Is.True, moved.Error.Detail);
            Assert.That(moved.NextState.Board.TryGetCharacterIdAt(4, out string movedId), Is.True);
            Assert.That(movedId, Is.EqualTo("a"));
        }

        [TestCase(StageChoice.SelectB)]
        [TestCase(StageChoice.SelectD)]
        public void StartBattleRejectsNoEnemyChoicesAndDoesNotAutoResolveVictory(StageChoice choice)
        {
            var choiceConfig = new StageProgressConfig(20, 4, 1, 4, 0, 10, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            ActionExecutionState before = State(stage: Preparation(choice, choiceConfig), enemyBoard: EnemyBoard(0));
            ActionDispatchResult result = Dispatcher().Dispatch(before,
                new StartBattleAction(new ActionId("no-enemy-start")), Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InvalidPhase));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(before.Stage.Phase, Is.EqualTo(StageProgressPhase.Preparation));
        }

        [Test]
        public void SelectAOrCWithMaterializedEmptyFormationIsNotRejectedSolelyForEmptyEnemyBoard()
        {
            ActionExecutionState before = State(enemyBoard: EnemyBoard(0));
            ActionDispatchResult result = Start(before);
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(result.NextState.Battle.Enemies, Is.Empty);
            Assert.That(result.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Battle));
        }

        [Test]
        public void MissingEnemyBoardIsConfigurationErrorAndDoesNotAdvanceAnyStateOrRandomStream()
        {
            var raw = State();
            var before = new ActionExecutionState(raw.Stage, raw.Economy, raw.CharacterPool,
                raw.InstanceFactory, raw.Board, raw.Hand, raw.Ownership, raw.ItemEffects,
                raw.RandomState, raw.Shop, raw.EventItem, null, raw.EnemyPreparation,
                raw.Battle, raw.BattleResult, raw.ExecutedActionIds);
            ActionDispatchResult result = Start(before);
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(result.NextState.RandomState.Battle, Is.EqualTo(before.RandomState.Battle));
            Assert.That(result.NextState.ExecutedActionIds, Is.Empty);
        }

        [Test]
        public void StartBattleRejectsPlayerAtZeroHealthWithoutChangingInputs()
        {
            ActionExecutionState before = State(economy: new EconomyState(10, 0, 3, 12,
                10, 0, 1, 0, 30));
            ActionDispatchResult result = Start(before);
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InvalidState));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(result.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Preparation));
        }

        [Test]
        public void StartBattleRejectsAnAlreadyRunningBattleEvenIfPreparationWasReintroduced()
        {
            ActionDispatchResult first = Start(State(), id: "first-start");
            Assert.That(first.Succeeded, Is.True, first.Error.Detail);
            ActionExecutionState inconsistentRetry = State(stage: Preparation(),
                randomState: first.NextState.RandomState, battle: first.NextState.Battle);
            ActionDispatchResult second = Start(inconsistentRetry, id: "second-start");
            Assert.That(second.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(second.Error.Code, Is.EqualTo(ActionErrorCode.BattleInProgress));
            Assert.That(second.NextState, Is.SameAs(inconsistentRetry));
        }

        [Test]
        public void StartBattleRejectsInvalidPhaseBeforeCallingHandlerAndDoesNotRecordAction()
        {
            StageProgressState initial = StageProgressRules.CreateInitialState(StageConfig);
            ActionExecutionState before = State(stage: initial);
            ActionDispatchResult result = Start(before);
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InvalidPhase));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(result.NextState.ExecutedActionIds, Is.Empty);
        }

        [Test]
        public void InvalidDefinitionsFailAtomicallyAndFailedActionIdCanBeRetried()
        {
            ActionExecutionState before = State();
            ActionId id = new ActionId("retry-start");
            ActionExecutionContext missingEnemies = Context(enemies: null);
            // Null has the established meaning “unavailable” at the context boundary.
            missingEnemies = new ActionExecutionContext(stageConfig: StageConfig,
                characterDefinitions: Characters, selectedRoster: new[] { Hero },
                fullCharacterRoster: new[] { Hero }, enemyDefinitions: null);
            var dispatcher = Dispatcher();
            ActionDispatchResult failure = dispatcher.Dispatch(before, new StartBattleAction(id), missingEnemies);
            Assert.That(failure.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(failure.NextState, Is.SameAs(before));
            Assert.That(before.HasExecuted(id), Is.False);
            ActionDispatchResult retry = dispatcher.Dispatch(before, new StartBattleAction(id), Context());
            Assert.That(retry.Succeeded, Is.True, retry.Error.Detail);
            Assert.That(retry.NextState.HasExecuted(id), Is.True);
        }

        [Test]
        public void BattleLimitedAllyItemEffectIsIncludedAndItemEffectStateIsPreserved()
        {
            var streams = new SimulationRandomStreams(14UL);
            CharacterInstance hero = MakeCharacter(Hero3, streams.Stats, "hero-instance");
            BoardState board = BoardWith(hero);
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Item(AllyAttackItem.DefinitionId), out hand), Is.True);
            ActionExecutionState before = State(board: board, hand: hand, ownership: Ownership(hero));
            var dispatcher = Dispatcher();
            var use = new UseHandItemAction(new ActionId("use-ally-item"), 0,
                GameActionTarget.FromItemUseTarget(ItemUseTarget.BoardCharacter(0)));
            ActionExecutionContext itemContext = Context(selected: new[] { Hero3 },
                fullRoster: new[] { Hero3 }, characterDefinitions: new[] { Hero3 });
            ActionDispatchResult used = dispatcher.Dispatch(before, use, itemContext);
            Assert.That(used.Succeeded, Is.True, used.Error.Detail);
            Assert.That(used.NextState.Ownership.TryGet("hero-instance", out CharacterInstanceView buffed), Is.True);
            Assert.That(buffed.TemporaryAttackBonus, Is.EqualTo(2));

            ActionDispatchResult started = dispatcher.Dispatch(used.NextState,
                new StartBattleAction(new ActionId("start-after-item")), itemContext);
            Assert.That(started.Succeeded, Is.True, started.Error.Detail);
            Assert.That(started.NextState.Battle.Allies.Single().Attack, Is.EqualTo(buffed.Attack + 2));
            Assert.That(started.NextState.ItemEffects, Is.SameAs(used.NextState.ItemEffects));
            Assert.That(started.NextState.ItemEffects.HasActiveBattleEffects, Is.True);
        }

        [Test]
        public void AllEnemiesItemModifierIsAppliedToBattleAndItemEffectStateIsKeptForLaterBattleRules()
        {
            var streams = new SimulationRandomStreams(15UL);
            CharacterInstance hero = MakeCharacter(Hero3, streams.Stats, "hero-instance");
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Item(EnemyDefenseItem.DefinitionId), out hand), Is.True);
            ActionExecutionState before = State(hand: hand, ownership: Ownership(hero),
                board: BoardWith(hero), enemyBoard: EnemyBoard());
            var dispatcher = Dispatcher();
            ActionExecutionContext itemContext = Context(selected: new[] { Hero3 },
                fullRoster: new[] { Hero3 }, characterDefinitions: new[] { Hero3 });
            ActionDispatchResult used = dispatcher.Dispatch(before,
                new UseHandItemAction(new ActionId("use-enemy-item"), 0,
                    GameActionTarget.FromItemUseTarget(ItemUseTarget.EnemyArea(1))), itemContext);
            Assert.That(used.Succeeded, Is.True, used.Error.Detail);
            Assert.That(used.NextState.EnemyPreparation.GetDefenseBonus(0), Is.EqualTo(-1));
            ActionDispatchResult started = dispatcher.Dispatch(used.NextState,
                new StartBattleAction(new ActionId("start-after-enemy-item")), itemContext);
            Assert.That(started.Succeeded, Is.True, started.Error.Detail);
            Assert.That(started.NextState.Battle.Enemies.Single().Defense, Is.EqualTo(2));
            Assert.That(started.NextState.ItemEffects.EnemyCommands.Count, Is.EqualTo(1));
        }

        [Test]
        public void EmptyAndInvalidBattleDefinitionConfigurationAreReportedWithoutPartialCommit()
        {
            ActionExecutionState before = State();
            ActionExecutionContext emptyEnemyDefinitions = Context(enemies: Array.Empty<EnemyDefinition>());
            ActionDispatchResult missing = Start(before, emptyEnemyDefinitions);
            Assert.That(missing.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(missing.NextState, Is.SameAs(before));
            Assert.That(before.RandomState.Battle, Is.EqualTo(missing.NextState.RandomState.Battle));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleConfig(0));
        }

        [Test]
        public void ReplayingSameInitialStateAndActionProducesIdenticalBattleAndHistory()
        {
            ActionExecutionState firstInput = State(randomState: ChangeBattleSeed(777UL));
            ActionExecutionState secondInput = State(randomState: ChangeBattleSeed(777UL));
            ActionDispatchResult first = Start(firstInput, id: "replay-start");
            ActionDispatchResult second = Start(secondInput, id: "replay-start");
            Assert.That(first.Succeeded, Is.True, first.Error.Detail);
            Assert.That(second.Succeeded, Is.True, second.Error.Detail);
            Assert.That(first.NextState.Stage.Phase, Is.EqualTo(second.NextState.Stage.Phase));
            Assert.That(first.NextState.Battle.Enemies.Select(e => e.InstanceId),
                Is.EqualTo(second.NextState.Battle.Enemies.Select(e => e.InstanceId)));
            Assert.That(first.NextState.Battle.BattleRandomState, Is.EqualTo(second.NextState.Battle.BattleRandomState));
            Assert.That(first.NextState.ExecutedActionIds, Is.EqualTo(second.NextState.ExecutedActionIds));
        }

        [Test]
        public void DuplicateActionIdIsRejectedWithoutChangingAnyCheckpointOrSuccessfulHistory()
        {
            ActionExecutionState before = State(history: new[] { new ActionId("already-used") });
            ActionDispatchResult result = Start(before, id: "already-used");
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.DuplicateAction));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(result.NextState.ExecutedActionIds.Select(x => x.Value), Is.EqualTo(new[] { "already-used" }));
            Assert.That(result.NextState.RandomState.Battle, Is.EqualTo(before.RandomState.Battle));
        }

        [Test]
        public void MissingBattleConfigUsesExistingBattleInitializationDefault()
        {
            ActionExecutionState before = State();
            ActionDispatchResult result = Start(before, Context(battleConfig: null));
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(result.NextState.Battle.MaxRounds, Is.EqualTo(BattleConfig.Default.MaxRounds));
        }

        [Test]
        public void StageChoicePurchasePlacementXpItemUseAndStartBattleRunThroughOneDispatcher()
        {
            var choiceConfig = new StageProgressConfig(20, 4, 1, 4, 0, 10, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState choice = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(choiceConfig), choiceConfig).NextState;
            Assert.That(choice.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));

            HandState initialHand = HandState.CreateStandard();
            Assert.That(initialHand.TryWithSlot(4, HandSlotContent.Item(AllyAttackItem.DefinitionId),
                out initialHand), Is.True);
            var pool = new CharacterPool(new[]
            {
                new CharacterPoolEntry(Hero3.DefinitionId, 10, 10)
            });
            ActionExecutionState state = State(stage: choice, hand: initialHand, pool: pool,
                economy: new EconomyState(10, 0, 3, 12, 10, 0, 1, 20, 30));
            ActionExecutionContext context = Context(selected: new[] { Hero3 },
                fullRoster: new[] { Hero3 }, characterDefinitions: new[] { Hero3 },
                stageConfig: choiceConfig, pools: NormalPools(), shopConfig: CharacterShopConfig,
                assignedCosts: AssignedCosts);
            ActionDispatcher dispatcher = Dispatcher();

            ActionDispatchResult selected = dispatcher.Dispatch(state,
                new SelectStageChoiceAction(new ActionId("flow-choice"), StageChoice.SelectA), context);
            Assert.That(selected.Succeeded, Is.True, selected.Error.Detail);
            Assert.That(selected.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(selected.NextState.Shop.Offers.Count, Is.EqualTo(1));
            Assert.That(selected.NextState.EnemyBoard.TryGetAt(0, out EnemyInstance selectedEnemy), Is.True);
            Assert.That(selectedEnemy, Is.Not.Null);

            string offerId = selected.NextState.Shop.Offers[0].OfferId;
            ActionDispatchResult purchased = dispatcher.Dispatch(selected.NextState,
                new BuyCharacterAction(new ActionId("flow-buy"), offerId,
                    new GameActionLocation(CharacterLocationKind.Hand, 0)), context);
            Assert.That(purchased.Succeeded, Is.True, purchased.Error.Detail);
            Assert.That(purchased.NextState.Economy.Gold, Is.EqualTo(9));
            Assert.That(purchased.NextState.Hand.TryGetSlot(0, out HandSlotContent bought), Is.True);
            Assert.That(bought.Kind, Is.EqualTo(HandSlotContentKind.Character));

            ActionDispatchResult placed = dispatcher.Dispatch(purchased.NextState,
                new MoveHandToBoardAction(new ActionId("flow-place"), 0, 0), context);
            Assert.That(placed.Succeeded, Is.True, placed.Error.Detail);
            ActionDispatchResult xp = dispatcher.Dispatch(placed.NextState,
                new BuyExperienceAction(new ActionId("flow-xp")), context);
            Assert.That(xp.Succeeded, Is.True, xp.Error.Detail);
            Assert.That(xp.NextState.Economy.Gold, Is.EqualTo(5));
            Assert.That(xp.NextState.Economy.XP, Is.EqualTo(1));

            ActionDispatchResult item = dispatcher.Dispatch(xp.NextState,
                new UseHandItemAction(new ActionId("flow-use-item"), 4,
                    GameActionTarget.FromItemUseTarget(ItemUseTarget.BoardCharacter(0))), context);
            Assert.That(item.Succeeded, Is.True, item.Error.Detail);
            ActionDispatchResult battle = dispatcher.Dispatch(item.NextState,
                new StartBattleAction(new ActionId("flow-start-battle")), context);
            Assert.That(battle.Succeeded, Is.True, battle.Error.Detail);
            Assert.That(battle.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Battle));
            Assert.That(battle.NextState.Battle.Allies.Single().Attack, Is.EqualTo(9));
            Assert.That(battle.NextState.Battle.Enemies.Single().DefinitionId, Is.EqualTo(Enemy.DefinitionId));
            Assert.That(battle.NextState.ExecutedActionIds.Select(x => x.Value), Is.EqualTo(new[]
            {
                "flow-choice", "flow-buy", "flow-place", "flow-xp", "flow-use-item", "flow-start-battle"
            }));
            Assert.That(battle.NextState.CharacterPool.Entries[Hero3.DefinitionId].OwnedCopies, Is.EqualTo(1));
            Assert.That(battle.NextState.Board.TryGetCharacterIdAt(0, out string boardId), Is.True);
            Assert.That(battle.NextState.Ownership.Contains(boardId), Is.True);
        }

        [Test]
        public void MalformedEnemyBoardAndMissingCharacterDefinitionsDoNotPartiallyAdvanceStage()
        {
            var state = State();
            var wrongSizeBoard = EnemyBoardState.CreateEmpty(3, 2);
            ActionExecutionState wrongBoard = new ActionExecutionState(state.Stage, state.Economy,
                state.CharacterPool, state.InstanceFactory, state.Board, state.Hand, state.Ownership,
                state.ItemEffects, state.RandomState, enemyBoard: wrongSizeBoard);
            ActionDispatchResult badBoard = Start(wrongBoard);
            Assert.That(badBoard.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(badBoard.NextState, Is.SameAs(wrongBoard));

            var streams = new SimulationRandomStreams(65UL);
            CharacterInstance hero = MakeCharacter(Hero3, streams.Stats, "needs-definition");
            ActionExecutionState withCharacter = State(board: BoardWith(hero), ownership: Ownership(hero));
            ActionDispatchResult noCharacters = Start(withCharacter,
                Context(characterDefinitions: Array.Empty<CharacterDefinition>(),
                    selected: new[] { Hero3 }, fullRoster: new[] { Hero3 }));
            Assert.That(noCharacters.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(noCharacters.NextState, Is.SameAs(withCharacter));
            Assert.That(withCharacter.Stage.Phase, Is.EqualTo(StageProgressPhase.Preparation));
        }
    }
}
