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
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class StrategyDispatcherIntegrationTests
    {
        private static readonly CharacterDefinition Hero = new CharacterDefinition("hero", "Hero", 1,
            new[] { "A" }, new[] { "U" });
        private static readonly CharacterDefinition Hero2 = new CharacterDefinition("hero2", "Hero 2", 1,
            new[] { "A" }, new[] { "U" });
        private static readonly EnemyDefinition Enemy = new EnemyDefinition("enemy", "Enemy", 1, 0, 5,
            null, null, null, "T001", null, null, null);
        private static readonly CharacterStatDefinition Stats = new CharacterStatDefinition(new[]
        {
            new CharacterCostStats(1, new[] { new CharacterStatOption(5, 1, 1) },
                new[] { new CharacterStatOption(6, 2, 1) }, new[] { new CharacterStatOption(7, 3, 1) })
        });
        private static readonly EconomyRulesConfiguration EconomyConfig = new EconomyRulesConfiguration(
            4, 2, 1, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 10, 1, 3, 3, 1, 0);

        private static StageProgressConfig Config(int choiceRound = 1, int eventChance = 0) =>
            new StageProgressConfig(20, 4, choiceRound, 4, eventChance, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static CharacterShopConfiguration ShopConfig => new CharacterShopConfiguration(
            new[] { 1, 0, 0, 0, 0 }, new[] { 10, 0, 0, 0, 0 },
            new[] { new[] { 100, 0, 0, 0, 0 } }, new[] { 1 });

        private static AssignedCharacterCosts Costs => new AssignedCharacterCosts(new[]
        {
            new KeyValuePair<string, int>(Hero.DefinitionId, 1)
        });

        private static ItemDefinition BlockItem => new ItemDefinition("block", "Block", 2, string.Empty,
            ItemEffectTarget.Player, ItemEffectDuration.ThisBattle,
            new[] { new ItemEffectDefinition(ItemEffectKind.Block, 3) });

        private static StageProgressState ChoicePending(StageProgressConfig config)
        {
            StageProgressState stage = StageProgressRules.CreateInitialState(config);
            return StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
        }

        private static IReadOnlyList<EncounterPoolDefinition> Pools(int roundIndex = 0)
        {
            var formation = new EnemyFormationDefinition("formation", new[]
            {
                new EnemyFormationSlotDefinition(Enemy.DefinitionId, 0, 0),
                new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0),
                new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0),
                new EnemyFormationSlotDefinition(null, 0, 0)
            });
            return new[]
            {
                new EncounterPoolDefinition("normal", new[] { roundIndex }, StageEnemyCategory.Normal, new[] { formation }),
                new EncounterPoolDefinition("hard", new[] { roundIndex }, StageEnemyCategory.Hard, new[] { formation })
            };
        }

        private static ActionExecutionState State(StageProgressState stage, ulong seed = 123,
            EconomyState economy = null, BoardState board = null, HandState hand = null,
            CharacterOwnershipState ownership = null, ShopState shop = null,
            CharacterPool pool = null, EventItemState eventItem = null,
            EnemyBoardState enemyBoard = null, ItemEffectState effects = null,
            IEnumerable<ActionId> history = null)
        {
            return new ActionExecutionState(stage, economy ?? new EconomyState(30, 0, 3, 12, 20, 0, 1, 20, 30),
                pool ?? new CharacterPool(new[] { new CharacterPoolEntry(Hero.DefinitionId, 10, 10) }),
                new CharacterInstanceFactory("I", 0),
                board ?? BoardState.CreateStandard(), hand ?? HandState.CreateStandard(),
                ownership ?? new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                effects ?? new ItemEffectState(), new SimulationRandomStreams(seed).CaptureState(),
                shop ?? new ShopState(ShopKind.Character, Array.Empty<ShopOffer>(), 0, 1, 1),
                eventItem, enemyBoard ?? EnemyBoardState.CreateEmpty(), executedActionIds: history);
        }

        private static ActionExecutionContext ActionContext(StageProgressConfig config,
            IReadOnlyList<CharacterDefinition> roster = null, IReadOnlyList<ItemDefinition> items = null,
            IReadOnlyList<EncounterPoolDefinition> pools = null, IReadOnlyList<EnemyDefinition> enemies = null,
            IReadOnlyList<CharacterDefinition> definitions = null) => new ActionExecutionContext(
                stageConfig: config, economyConfig: EconomyConfig, characterShopConfig: ShopConfig,
                assignedCharacterCosts: Costs, characterStatSettings: Stats,
                characterDefinitions: definitions ?? new[] { Hero, Hero2 },
                selectedRoster: roster ?? new[] { Hero }, fullCharacterRoster: roster ?? new[] { Hero },
                itemDefinitions: items ?? new[] { BlockItem }, encounterPools: pools ?? Pools(),
                enemyDefinitions: enemies ?? new[] { Enemy }, battleConfig: new BattleConfig(4));

        private static GameObservation Observe(ActionExecutionState state, StageProgressConfig config,
            IReadOnlyList<CharacterDefinition> roster = null, IReadOnlyList<ItemDefinition> items = null,
            IReadOnlyList<EnemyDefinition> enemies = null)
        {
            var result = ObservationBuilder.Build(new ObservationSource(state.Stage, config, state.Economy,
                EconomyConfig, state.Board, state.Hand, state.Ownership,
                new[] { Hero, Hero2 }, roster ?? new[] { Hero }, roster ?? new[] { Hero },
                items ?? new[] { BlockItem }, state.Shop, state.ItemEffects, state.EventItem,
                enemies ?? new[] { Enemy }, state.EnemyBoard, state.EnemyPreparation, state.Battle, state.BattleResult));
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            return result.Observation;
        }

        private static StrategyContext Context(IPlayerStrategy strategy, StrategyState state,
            StrategyRngState rng, int step, string id = null) => new StrategyContext(
                strategy.StrategyId, strategy.Version, 0, step,
                new ActionId(id ?? "trial0/action" + step), state, rng);

        private static ActionDispatcher Dispatcher => new ActionDispatcher(AllActionHandlers.Create());

        [Test]
        public void HeuristicSelectsChoiceThenDispatcherCreatesCharacterShopAndEncounter()
        {
            StageProgressConfig config = Config();
            ActionExecutionState initial = State(ChoicePending(config));
            var strategy = new HeuristicStrategy(new HeuristicStrategySettings(
                selectAValue: 1000, selectBValue: 0, selectCValue: 0, selectDBaseValue: 0,
                characterPurchaseValue: 1000000, startBattleValue: 1000));
            StrategyState policy = StrategyState.EmptyFor(strategy.StrategyId, strategy.Version);
            StrategyRngState policyRng = StrategyRandomSource.Create(17, strategy.StrategyId, strategy.Version).CaptureState();
            StrategyContext context = Context(strategy, policy, policyRng, 0);
            StrategyDecision decision = strategy.ChooseAction(Observe(initial, config), context);
            Assert.That(context.Accepts(decision), Is.True);
            Assert.That(decision.Action, Is.TypeOf<SelectStageChoiceAction>());
            Assert.That(((SelectStageChoiceAction)decision.Action).Choice, Is.EqualTo(StageChoice.SelectA));

            ActionDispatchResult dispatched = Dispatcher.Dispatch(initial, decision.Action, ActionContext(config));
            Assert.That(dispatched.Succeeded, Is.True, dispatched.Error.Detail);
            Assert.That(dispatched.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(dispatched.NextState.Shop.Kind, Is.EqualTo(ShopKind.Character));
            Assert.That(dispatched.NextState.Shop.Offers.Count, Is.EqualTo(1));
            Assert.That(dispatched.NextState.EnemyBoard.TryGetAt(0, out EnemyInstance selectedEnemy), Is.True);
            Assert.That(selectedEnemy, Is.Not.Null);
            Assert.That(dispatched.NextState.HasExecuted(decision.Action.ActionId), Is.True);
            Assert.That(decision.NextState.StrategyId, Is.EqualTo(strategy.StrategyId));
            Assert.That(decision.NextState.StrategyVersion, Is.EqualTo(strategy.Version));
            Assert.That(decision.NextStrategyRngState, Is.EqualTo(policyRng));

            StrategyContext buyContext = Context(strategy, decision.NextState,
                decision.NextStrategyRngState, 1);
            StrategyDecision buyDecision = strategy.ChooseAction(Observe(dispatched.NextState, config), buyContext);
            Assert.That(buyDecision.Action, Is.TypeOf<BuyCharacterAction>(), buyDecision.Diagnostic);
            ActionDispatchResult bought = Dispatcher.Dispatch(dispatched.NextState, buyDecision.Action,
                ActionContext(config));
            Assert.That(bought.Succeeded, Is.True, bought.Error.Detail);
            Assert.That(bought.NextState.Ownership.GetOwnedInstances().Count, Is.EqualTo(1));
            Assert.That(bought.NextState.Economy.Gold, Is.EqualTo(29));
            Assert.That(bought.NextState.HasExecuted(buyDecision.Action.ActionId), Is.True);

            StrategyContext battleContext = Context(strategy, buyDecision.NextState,
                buyDecision.NextStrategyRngState, 2);
            StrategyDecision battleDecision = strategy.ChooseAction(Observe(bought.NextState, config), battleContext);
            Assert.That(battleDecision.Action, Is.TypeOf<StartBattleAction>(), battleDecision.Diagnostic);
            ActionDispatchResult battle = Dispatcher.Dispatch(bought.NextState, battleDecision.Action,
                ActionContext(config));
            Assert.That(battle.Succeeded, Is.True, battle.Error.Detail);
            Assert.That(battle.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Battle));
            Assert.That(battle.NextState.Battle.Allies.Count, Is.EqualTo(1));
            Assert.That(battle.NextState.Battle.Enemies.Count, Is.EqualTo(1));
        }

        [Test]
        public void HeuristicDecisionIsDispatchedAndStartsBattleFromPreparation()
        {
            StageProgressConfig config = Config();
            StageProgressState stage = StageSelectionRules.Select(ChoicePending(config), config, StageChoice.SelectA).NextState;
            var hero = new CharacterInstance("hero-instance", Hero, 1);
            Assert.That(hero.EnsureStats(Hero, Stats, new Pcg32RandomSource(5, 1)), Is.True);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, hero.InstanceId, out board), Is.True);
            EnemyBoardState enemyBoard = EnemyEncounterSetupRules.Generate(stage, Pools(), new[] { Enemy },
                new EnemyPreparationState(), new SimulationRandomStreams(123).Encounter.CaptureState()).Board;
            ActionExecutionState state = State(stage, board: board,
                ownership: new CharacterOwnershipState(new[] { hero }), enemyBoard: enemyBoard,
                pool: new CharacterPool(new[] { new CharacterPoolEntry(Hero.DefinitionId, 9, 10, 0, 1) }));
            var strategy = new HeuristicStrategy();
            StrategyContext context = Context(strategy, StrategyState.EmptyFor(strategy.StrategyId, strategy.Version),
                StrategyRandomSource.Create(5, strategy.StrategyId, strategy.Version).CaptureState(), 0);
            StrategyDecision decision = strategy.ChooseAction(Observe(state, config), context);
            Assert.That(decision.Action, Is.TypeOf<StartBattleAction>(), decision.Diagnostic);
            ActionDispatchResult dispatched = Dispatcher.Dispatch(state, decision.Action, ActionContext(config));
            Assert.That(dispatched.Succeeded, Is.True, dispatched.Error.Detail);
            Assert.That(dispatched.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Battle));
            Assert.That(dispatched.NextState.Battle.Allies.Count, Is.EqualTo(1));
            Assert.That(dispatched.NextState.Battle.Enemies.Count, Is.EqualTo(1));
            Assert.That(dispatched.NextState.HasExecuted(decision.Action.ActionId), Is.True);
        }

        [Test]
        public void RandomStrategyChoiceUsesOnlyStrategyRngAndDispatcherOwnsGameRngChanges()
        {
            StageProgressConfig config = Config();
            ActionExecutionState state = State(ChoicePending(config), seed: 771);
            RandomStrategy strategy = new RandomStrategy();
            StrategyRngState strategyRng = StrategyRandomSource.Create(771, strategy.StrategyId, strategy.Version).CaptureState();
            StrategyContext context = Context(strategy, StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), strategyRng, 0);
            SimulationRandomStreamsState before = state.RandomState;
            StrategyDecision decision = strategy.ChooseAction(Observe(state, config), context);
            Assert.That(context.Accepts(decision), Is.True);
            Assert.That(decision.Action, Is.TypeOf<SelectStageChoiceAction>());
            Assert.That(decision.StrategyRngAdvanced, Is.True);
            Assert.That(state.RandomState, Is.EqualTo(before));
            ActionDispatchResult result = Dispatcher.Dispatch(state, decision.Action, ActionContext(config));
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(result.NextState.RandomState, Is.Not.EqualTo(before));
            Assert.That(result.NextState.RandomState.Shop, Is.Not.EqualTo(before.Shop));
            Assert.That(result.NextState.RandomState.Battle, Is.EqualTo(before.Battle));
            Assert.That(result.NextState.HasExecuted(decision.Action.ActionId), Is.True);
        }

        [Test]
        public void StrategyCheckpointCanBeAdoptedOnRejectedActionAndRejectedActionIdCanBeRetried()
        {
            StageProgressConfig config = Config();
            ActionExecutionState state = State(StageSelectionRules.Select(ChoicePending(config), config, StageChoice.SelectA).NextState);
            var strategy = new DeclaredActionStrategy();
            StrategyState beforePolicy = StrategyState.EmptyFor(strategy.StrategyId, strategy.Version);
            StrategyRngState beforePolicyRng = StrategyRandomSource.Create(88, strategy.StrategyId, strategy.Version).CaptureState();
            StrategyContext context = Context(strategy, beforePolicy, beforePolicyRng, 0);
            StrategyDecision decision = strategy.ChooseAction(Observe(state, config), context);
            Assert.That(context.Accepts(decision), Is.True);
            Assert.That(decision.NextState.Payload, Is.EqualTo("decided"));
            Assert.That(decision.StrategyRngAdvanced, Is.True);

            ActionDispatchResult rejected = Dispatcher.Dispatch(state, decision.Action, ActionContext(config));
            Assert.That(rejected.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(rejected.NextState, Is.SameAs(state));
            Assert.That(rejected.NextState.HasExecuted(decision.Action.ActionId), Is.False);
            Assert.That(rejected.NextState.RandomState, Is.EqualTo(state.RandomState));

            ActionDispatchResult retry = Dispatcher.Dispatch(state,
                new BuyExperienceAction(decision.Action.ActionId), ActionContext(config));
            Assert.That(retry.Succeeded, Is.True, retry.Error.Detail);
            Assert.That(retry.NextState.HasExecuted(decision.Action.ActionId), Is.True);
        }

        [Test]
        public void ObservationAndStrategyRemainDetachedFromDispatcherStateAndGameRng()
        {
            StageProgressConfig config = Config();
            var character = new CharacterInstance("a", Hero, 1);
            Assert.That(character.EnsureStats(Hero, Stats, new Pcg32RandomSource(11, 1)), Is.True);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, character.InstanceId, out board), Is.True);
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Character("b"), out hand), Is.True);
            var second = new CharacterInstance("b", Hero2, 1);
            Assert.That(second.EnsureStats(Hero2, Stats, new Pcg32RandomSource(12, 1)), Is.True);
            ActionExecutionState state = State(StageSelectionRules.Select(ChoicePending(config), config, StageChoice.SelectA).NextState,
                board: board, hand: hand, ownership: new CharacterOwnershipState(new[] { character, second }),
                pool: new CharacterPool(new[] { new CharacterPoolEntry(Hero.DefinitionId, 9, 10, 0, 1),
                    new CharacterPoolEntry(Hero2.DefinitionId, 9, 10, 0, 1) }));
            GameObservation observation = Observe(state, config, new[] { Hero, Hero2 });
            var strategy = new HeuristicStrategy(new HeuristicStrategySettings(startBattleValue: 0,
                forcedStartBattleValue: 0, boardImprovementValue: 80));
            StrategyContext context = Context(strategy, StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), null, 0);
            StrategyDecision decision = strategy.ChooseAction(observation, context);
            Assert.That(decision.Action, Is.TypeOf<MoveHandToBoardAction>(), decision.Diagnostic);
            Assert.That(state.Board.TryGetCharacterIdAt(1, out string beforeBoardId), Is.True);
            Assert.That(beforeBoardId, Is.Null);
            Assert.That(state.Hand.TryGetSlot(0, out HandSlotContent beforeHand), Is.True);
            Assert.That(beforeHand.Kind, Is.EqualTo(HandSlotContentKind.Character));
            Assert.That(state.RandomState, Is.EqualTo(new SimulationRandomStreams(123).CaptureState()));

            ActionDispatchResult result = Dispatcher.Dispatch(state, decision.Action, ActionContext(config,
                roster: new[] { Hero, Hero2 }, definitions: new[] { Hero, Hero2 }));
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(result.NextState.Board.TryGetCharacterIdAt(1, out string afterBoardId), Is.True);
            Assert.That(afterBoardId, Is.EqualTo("b"));
            Assert.That(result.NextState.Hand.TryGetSlot(0, out HandSlotContent afterHand), Is.True);
            Assert.That(afterHand.Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Assert.That(result.NextState.RandomState, Is.EqualTo(state.RandomState));
            Assert.That(state.Board.TryGetCharacterIdAt(1, out beforeBoardId), Is.True);
            Assert.That(beforeBoardId, Is.Null);
        }

        [Test]
        public void HeuristicUsesHandItemThroughDispatcherAndConsumesOnlyRuleOwnedStreams()
        {
            StageProgressConfig config = Config();
            StageProgressState stage = StageSelectionRules.Select(ChoicePending(config), config, StageChoice.SelectA).NextState;
            var hero = new CharacterInstance("hero-instance", Hero, 1);
            Assert.That(hero.EnsureStats(Hero, Stats, new Pcg32RandomSource(13, 1)), Is.True);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, hero.InstanceId, out board), Is.True);
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Item(BlockItem.DefinitionId), out hand), Is.True);
            ActionExecutionState state = State(stage, board: board, hand: hand,
                ownership: new CharacterOwnershipState(new[] { hero }),
                pool: new CharacterPool(new[] { new CharacterPoolEntry(Hero.DefinitionId, 9, 10, 0, 1) }));
            var strategy = new HeuristicStrategy(new HeuristicStrategySettings(itemUseBaseValue: 100));
            StrategyContext context = Context(strategy, StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), null, 0);
            StrategyDecision decision = strategy.ChooseAction(Observe(state, config), context);
            Assert.That(decision.Action, Is.TypeOf<UseHandItemAction>(), decision.Diagnostic);
            SimulationRandomStreamsState before = state.RandomState;
            ActionDispatchResult result = Dispatcher.Dispatch(state, decision.Action,
                ActionContext(config, items: new[] { BlockItem }));
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(result.NextState.Hand.TryGetSlot(0, out HandSlotContent usedSlot), Is.True);
            Assert.That(usedSlot.Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Assert.That(result.NextState.ItemEffects.TemporaryPlayerBlockBonus, Is.EqualTo(3));
            Assert.That(result.NextState.RandomState, Is.EqualTo(before));
        }

        [Test]
        public void SelectBItemOfferIsPurchasedAndUsedThroughStrategyAndDispatcherBeforeNoEnemyVictory()
        {
            StageProgressConfig config = Config();
            StageProgressState stage = StageSelectionRules.Select(ChoicePending(config), config, StageChoice.SelectB).NextState;
            var offer = new ShopOffer("block-offer", ShopOfferKind.Item, BlockItem.DefinitionId, 2, null);
            var itemShop = new ShopState(ShopKind.Item, new[] { offer }, 1, 1, 1);
            ActionExecutionState state = State(stage, shop: itemShop, seed: 34);
            var strategy = new HeuristicStrategy(new HeuristicStrategySettings(
                itemPurchaseValue: 1000, itemUseBaseValue: 1000, startBattleValue: 0));
            StrategyState policy = StrategyState.EmptyFor(strategy.StrategyId, strategy.Version);
            StrategyContext buyContext = Context(strategy, policy, null, 0);
            StrategyDecision buy = strategy.ChooseAction(Observe(state, config), buyContext);
            Assert.That(buy.Action, Is.TypeOf<BuyItemAction>(), buy.Diagnostic);
            SimulationRandomStreamsState beforePurchase = state.RandomState;
            ActionDispatchResult purchased = Dispatcher.Dispatch(state, buy.Action,
                ActionContext(config, items: new[] { BlockItem }));
            Assert.That(purchased.Succeeded, Is.True, purchased.Error.Detail);
            Assert.That(purchased.NextState.Economy.Gold, Is.EqualTo(28));
            Assert.That(purchased.NextState.Hand.TryGetSlot(0, out HandSlotContent purchasedItem), Is.True);
            Assert.That(purchasedItem.Kind, Is.EqualTo(HandSlotContentKind.Item));
            Assert.That(purchased.NextState.RandomState, Is.EqualTo(beforePurchase));

            StrategyContext useContext = Context(strategy, buy.NextState, null, 1);
            StrategyDecision use = strategy.ChooseAction(Observe(purchased.NextState, config,
                items: new[] { BlockItem }), useContext);
            Assert.That(use.Action, Is.TypeOf<UseHandItemAction>(), use.Diagnostic);
            ActionDispatchResult used = Dispatcher.Dispatch(purchased.NextState, use.Action,
                ActionContext(config, items: new[] { BlockItem }));
            Assert.That(used.Succeeded, Is.True, used.Error.Detail);
            Assert.That(used.NextState.Hand.TryGetSlot(0, out HandSlotContent usedItem), Is.True);
            Assert.That(usedItem.Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Assert.That(used.NextState.ItemEffects.TemporaryPlayerBlockBonus, Is.EqualTo(3));
            Assert.That(used.NextState.CharacterPool, Is.SameAs(state.CharacterPool));
            Assert.That(used.NextState.RandomState, Is.EqualTo(beforePurchase));

            StageProgressResult noEnemy = StageProgressRules.ResolveNoEnemySelectionAsVictory(used.NextState.Stage, config);
            Assert.That(noEnemy.Succeeded, Is.True, noEnemy.Failure.ToString());
            Assert.That(noEnemy.NextState.Phase, Is.EqualTo(StageProgressPhase.RewardPending));
        }

        [Test]
        public void HeuristicEndsTargetlessEventAndDispatcherCompletesEvent()
        {
            StageProgressConfig config = Config(eventChance: 100);
            StageProgressState stage = ChoicePending(config);
            stage = StageSelectionRules.Select(stage, config, StageChoice.SelectB).NextState;
            stage = StageProgressRules.ResolveNoEnemySelectionAsVictory(stage, config).NextState;
            stage = StageProgressRules.CompletePendingReward(stage, config,
                new SimulationRandomStreams(15).Event.CaptureState()).NextState;
            Assert.That(stage.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            var eventStar = new ItemDefinition("event-star", "Event Star", 0, string.Empty,
                ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Star, 2) });
            ActionExecutionState state = State(stage, eventItem: new EventItemState(eventStar.DefinitionId));
            var strategy = new HeuristicStrategy();
            StrategyContext context = Context(strategy, StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), null, 0);
            StrategyDecision decision = strategy.ChooseAction(Observe(state, config, items: new[] { eventStar }), context);
            Assert.That(decision.Action, Is.TypeOf<EndEventAction>());
            ActionDispatchResult result = Dispatcher.Dispatch(state, decision.Action,
                ActionContext(config, items: new[] { eventStar }));
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(result.NextState.Stage.Phase, Is.Not.EqualTo(StageProgressPhase.EventPending));
            Assert.That(result.NextState.EventItem.Status, Is.EqualTo(EventItemStatus.EndedWithoutUse));
            Assert.That(result.NextState.RandomState, Is.EqualTo(state.RandomState));
        }

        [Test]
        public void RejectedActionDoesNotAdoptGameStateButIsDistinctFromStrategyDecisionCheckpoint()
        {
            StageProgressConfig config = Config();
            ActionExecutionState state = State(StageSelectionRules.Select(ChoicePending(config), config, StageChoice.SelectA).NextState);
            var strategy = new DeclaredActionStrategy();
            StrategyRngState initialRng = StrategyRandomSource.Create(99, strategy.StrategyId, strategy.Version).CaptureState();
            var harness = new StrategyDispatchHarness(state, strategy,
                StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), initialRng);
            ActionDispatchResult result = harness.DecideAndDispatch(Observe(state, config), ActionContext(config));
            StrategyDecision decision = harness.LastDecision;
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.NextState, Is.SameAs(state));
            Assert.That(result.NextState.ExecutedActionIds, Is.Empty);
            Assert.That(result.NextState.RandomState, Is.EqualTo(state.RandomState));
            Assert.That(harness.GameState, Is.SameAs(state));
            Assert.That(harness.StrategyState.Payload, Is.EqualTo("decided"));
            Assert.That(harness.StrategyRandomState, Is.Not.EqualTo(initialRng));
            Assert.That(harness.DecisionStep, Is.EqualTo(1));
            Assert.That(decision.NextState.Payload, Is.EqualTo("decided"));
            Assert.That(decision.NextStrategyRngState, Is.Not.EqualTo(initialRng));
        }

        /// <summary>Test-only coordinator demonstrating separate policy and game-state adoption.</summary>
        private sealed class StrategyDispatchHarness
        {
            private readonly IPlayerStrategy _strategy;
            private readonly ActionDispatcher _dispatcher = Dispatcher;
            public ActionExecutionState GameState { get; private set; }
            public StrategyState StrategyState { get; private set; }
            public StrategyRngState StrategyRandomState { get; private set; }
            public int DecisionStep { get; private set; }
            public StrategyDecision LastDecision { get; private set; }

            public StrategyDispatchHarness(ActionExecutionState gameState, IPlayerStrategy strategy,
                StrategyState strategyState, StrategyRngState strategyRandomState)
            {
                GameState = gameState;
                _strategy = strategy;
                StrategyState = strategyState;
                StrategyRandomState = strategyRandomState;
            }

            public ActionDispatchResult DecideAndDispatch(GameObservation observation,
                ActionExecutionContext actionContext)
            {
                var context = new StrategyContext(_strategy.StrategyId, _strategy.Version, 0,
                    DecisionStep, new ActionId("trial0/action" + DecisionStep), StrategyState, StrategyRandomState);
                LastDecision = _strategy.ChooseAction(observation, context);
                if (!context.Accepts(LastDecision))
                    throw new InvalidOperationException("Strategy returned a decision that does not belong to its context.");

                // Accepted policy progress is independent of whether game rules accept the declared action.
                StrategyState = LastDecision.NextState;
                StrategyRandomState = LastDecision.NextStrategyRngState;
                DecisionStep++;
                if (!LastDecision.HasAction) return null;

                ActionDispatchResult result = _dispatcher.Dispatch(GameState, LastDecision.Action, actionContext);
                if (result.Succeeded) GameState = result.NextState;
                return result;
            }
        }

        private sealed class DeclaredActionStrategy : IPlayerStrategy
        {
            public string StrategyId => "TestDeclared";
            public string Version => "1";
            public StrategyDecision ChooseAction(GameObservation observation, StrategyContext context)
            {
                StrategyRandomSource rng = context.CreateStrategyRandomSource();
                rng.NextInt(0, 100);
                return context.DecideWithRng(new MoveOrSwapBoardAction(context.NextActionId, 0, 1),
                    new StrategyState(StrategyId, Version, 1, "decided"), rng.CaptureState());
            }
        }
    }
}
