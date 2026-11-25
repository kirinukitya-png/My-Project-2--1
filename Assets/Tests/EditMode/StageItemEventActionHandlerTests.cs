using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
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
    public sealed class StageItemEventActionHandlerTests
    {
        private static readonly CharacterDefinition Hero = new CharacterDefinition("hero", "Hero", 1,
            Array.Empty<string>(), Array.Empty<string>());
        private static readonly AssignedCharacterCosts Costs = new AssignedCharacterCosts(
            new[] { new KeyValuePair<string, int>("hero", 1) });
        private static readonly CharacterShopConfiguration ShopConfig = new CharacterShopConfiguration(
            new[] { 1, 0, 0, 0, 0 }, new[] { 10, 0, 0, 0, 0 },
            new[] { new[] { 100, 0, 0, 0, 0 } }, new[] { 2 });
        private static readonly CharacterStatDefinition Stats = new CharacterStatDefinition(new[]
        {
            new CharacterCostStats(1, new[] { new CharacterStatOption(3, 1, 1) },
                new[] { new CharacterStatOption(5, 2, 1) }, new[] { new CharacterStatOption(8, 4, 1) })
        });
        private static readonly ItemDefinition Heal = Item("heal", ItemEffectTarget.Player,
            ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Damage, -4));
        private static readonly ItemDefinition AllyAttack = Item("ally", ItemEffectTarget.OneAlly,
            ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Attack, 1));
        private static readonly ItemDefinition EnemyDefense = Item("enemy-def", ItemEffectTarget.AllEnemies,
            ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Defense, -1));

        private static ItemDefinition Item(string id, ItemEffectTarget target, ItemEffectDuration duration,
            params ItemEffectDefinition[] effects) => new ItemDefinition(id, id, 0, string.Empty, target, duration, effects);

        private static StageProgressConfig Config(int choiceRound = 3, int bossRound = 4, int initialEventChance = 0) =>
            new StageProgressConfig(20, 4, choiceRound, bossRound, initialEventChance, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static StageProgressState ChoicePending(StageProgressConfig config = null)
        {
            config = config ?? Config();
            StageProgressState stage = StageProgressRules.CreateInitialState(config);
            while (true)
            {
                stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
                if (stage.Phase == StageProgressPhase.AwaitingStageChoice) return stage;
                stage = StageProgressRules.BeginBattle(stage).NextState;
                stage = StageProgressRules.ResolveBattle(stage, config, true).NextState;
                stage = StageProgressRules.CompletePendingReward(stage, config,
                    new SimulationRandomStreams(25UL).Event.CaptureState()).NextState;
            }
        }

        private static StageProgressState EventPending(StageProgressConfig config = null)
        {
            config = config ?? Config(choiceRound: 3, bossRound: 4, initialEventChance: 100);
            StageProgressState stage = StageProgressRules.CreateInitialState(config);
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            stage = StageProgressRules.BeginBattle(stage).NextState;
            stage = StageProgressRules.ResolveBattle(stage, config, true).NextState;
            return StageProgressRules.CompletePendingReward(stage, config,
                new SimulationRandomStreams(54UL).Event.CaptureState()).NextState;
        }

        private static EnemyDefinition Enemy => new EnemyDefinition("e1", "Enemy", 4, 1, 8,
            null, null, null, "T001", null, null, null);
        private static EnemyFormationDefinition Formation(string id = "f1") => new EnemyFormationDefinition(id,
            new[] { new EnemyFormationSlotDefinition("e1", 0, 0), new EnemyFormationSlotDefinition(null, 0, 0),
                new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0),
                new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0) });

        private static IReadOnlyList<EncounterPoolDefinition> Pools(int roundIndex = 2) => new[]
        {
            new EncounterPoolDefinition("normal", new[] { roundIndex }, StageEnemyCategory.Normal, new[] { Formation() }),
            new EncounterPoolDefinition("hard", new[] { roundIndex }, StageEnemyCategory.Hard, new[] { Formation("fh") }),
            new EncounterPoolDefinition("boss", new[] { roundIndex }, StageEnemyCategory.Boss, new[] { Formation("fb") })
        };

        private static ActionExecutionContext Context(StageProgressConfig config = null,
            IReadOnlyList<ItemDefinition> items = null, IReadOnlyList<EncounterPoolDefinition> pools = null,
            IReadOnlyList<EnemyDefinition> enemies = null) => new ActionExecutionContext(
                stageConfig: config ?? Config(), characterShopConfig: ShopConfig,
                assignedCharacterCosts: Costs, selectedRoster: new[] { Hero },
                characterDefinitions: new[] { Hero }, characterStatSettings: Stats,
                itemDefinitions: items ?? new[] { Heal, AllyAttack, EnemyDefense },
                encounterPools: pools ?? Pools(), enemyDefinitions: enemies ?? new[] { Enemy });

        private static ActionExecutionState State(StageProgressState stage, ulong seed = 71UL,
            HandState hand = null, CharacterOwnershipState ownership = null,
            EconomyState economy = null, ShopState shop = null, CharacterPool pool = null,
            EventItemState eventItem = null, EnemyBoardState enemyBoard = null,
            EnemyPreparationState enemyPreparation = null, BoardState board = null)
        {
            SimulationRandomStreams streams = new SimulationRandomStreams(seed);
            return new ActionExecutionState(stage, economy ?? new EconomyState(10, 0, 1, 12, 10, 0, 1, 20, 30),
                pool ?? new CharacterPool(new[] { new CharacterPoolEntry("hero", 10, 10) }),
                new CharacterInstanceFactory("T", 0), board ?? BoardState.CreateStandard(), hand ?? HandState.CreateStandard(),
                ownership ?? new CharacterOwnershipState(Array.Empty<CharacterInstance>()), new ItemEffectState(),
                streams.CaptureState(), shop, eventItem, enemyBoard, enemyPreparation);
        }

        private static HandState ItemInHand(string id)
        {
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Item(id), out HandState next), Is.True);
            return next;
        }

        private static ActionDispatchResult Dispatch(ActionExecutionState state, GameAction action,
            ActionExecutionContext context = null) =>
            new ActionDispatcher(ShopEconomyActionHandlers.Create().Concat(BoardHandActionHandlers.Create())
                .Concat(StageItemEventActionHandlers.Create())).Dispatch(state, action, context ?? Context());

        [TestCase(StageChoice.SelectA, StageEnemyCategory.Normal, ShopKind.Character)]
        [TestCase(StageChoice.SelectB, StageEnemyCategory.None, ShopKind.Item)]
        [TestCase(StageChoice.SelectC, StageEnemyCategory.Hard, ShopKind.Character)]
        [TestCase(StageChoice.SelectD, StageEnemyCategory.None, ShopKind.Character)]
        public void SelectChoiceCreatesMatchingShopAndEncounter(StageChoice choice,
            StageEnemyCategory category, ShopKind shopKind)
        {
            ActionExecutionState before = State(ChoicePending());
            ActionDispatchResult result = Dispatch(before, new SelectStageChoiceAction(new ActionId("choice-" + choice), choice));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.Stage.CurrentSelection.EnemyCategory, Is.EqualTo(category));
            Assert.That(result.NextState.Shop.Kind, Is.EqualTo(shopKind));
            Assert.That(EnemyCount(result.NextState.EnemyBoard), Is.EqualTo(category == StageEnemyCategory.None ? 0 : 1));
            Assert.That(result.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(result.NextState.EventItem, Is.Null);
            Assert.That(result.NextState.RandomState.Shop, Is.Not.EqualTo(before.RandomState.Shop));
            Assert.That(result.NextState.RandomState.Encounter.Equals(before.RandomState.Encounter),
                Is.EqualTo(category == StageEnemyCategory.None));
            Assert.That(result.NextState.RandomState.Stats, Is.EqualTo(before.RandomState.Stats));
            Assert.That(result.NextState.RandomState.Battle, Is.EqualTo(before.RandomState.Battle));
            Assert.That(result.NextState.RandomState.Event, Is.EqualTo(before.RandomState.Event));
            Assert.That(result.NextState.RandomState.Roster, Is.EqualTo(before.RandomState.Roster));
            Assert.That(result.NextState.RandomState.Economy, Is.EqualTo(before.RandomState.Economy));
        }

        [Test]
        public void SelectChoiceBossRoundUsesBossPool()
        {
            StageProgressConfig config = Config(choiceRound: 1, bossRound: 1);
            StageProgressState pending = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            ActionDispatchResult result = Dispatch(State(pending),
                new SelectStageChoiceAction(new ActionId("boss-choice"), StageChoice.SelectC),
                Context(config, pools: Pools(0)));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.Stage.CurrentSelection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Boss));
            Assert.That(EnemyCount(result.NextState.EnemyBoard), Is.EqualTo(1));
        }

        [Test]
        public void SelectDHealsOnceAndCapsAtMaximum()
        {
            StageProgressState pending = ChoicePending();
            ActionExecutionState before = State(pending, economy: new EconomyState(10, 0, 1, 12, 25, 0, 1, 20, 30));
            ActionDispatchResult result = Dispatch(before,
                new SelectStageChoiceAction(new ActionId("select-d"), StageChoice.SelectD));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.Economy.CurrentHP, Is.EqualTo(30));
            Assert.That(result.NextState.Stage.SelectionEffectApplied, Is.True);
        }

        [Test]
        public void ChoiceFailureForMissingEncounterCandidateIsAtomic()
        {
            ActionExecutionState before = State(ChoicePending());
            ActionDispatchResult result = Dispatch(before,
                new SelectStageChoiceAction(new ActionId("no-encounter"), StageChoice.SelectA),
                Context(pools: Array.Empty<EncounterPoolDefinition>()));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(result.NextState.Stage.Phase, Is.EqualTo(before.Stage.Phase));
            Assert.That(result.NextState.Shop, Is.SameAs(before.Shop));
            Assert.That(result.NextState.RandomState.Shop, Is.EqualTo(before.RandomState.Shop));
            Assert.That(result.NextState.RandomState.Encounter, Is.EqualTo(before.RandomState.Encounter));
            Assert.That(result.NextState.ExecutedActionIds, Is.Empty);
        }

        [Test]
        public void SelectChoiceOutsideChoicePhaseIsRejected()
        {
            StageProgressConfig config = Config();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            ActionExecutionState before = State(prep);
            ActionDispatchResult result = Dispatch(before,
                new SelectStageChoiceAction(new ActionId("wrong-phase"), StageChoice.SelectA));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InvalidPhase));
            Assert.That(result.NextState, Is.SameAs(before));
        }

        [Test]
        public void HandItemUsesExistingRuleAndConsumesItemAtomically()
        {
            ActionExecutionState before = State(Preparation(), hand: ItemInHand(Heal.DefinitionId),
                economy: new EconomyState(10, 0, 1, 12, 20, 0, 1, 20, 30));
            var target = GameActionTarget.FromItemUseTarget(ItemUseTarget.PlayerArea(0));
            ActionDispatchResult result = Dispatch(before, new UseHandItemAction(new ActionId("use-heal"), 0, target));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.Economy.CurrentHP, Is.EqualTo(24));
            Assert.That(result.NextState.Hand.TryGetSlot(0, out HandSlotContent content) && content.IsEmpty, Is.True);
            Assert.That(result.NextState.RandomState.Stats, Is.EqualTo(before.RandomState.Stats));
        }

        [Test]
        public void EnemyWideHandItemUpdatesEnemyPreparationAndConsumesHandItem()
        {
            EnemyFormationResult generated = EnemyFormationRules.CreateBoard(Formation(), new[] { Enemy }, 2);
            ActionExecutionState before = State(Preparation(), hand: ItemInHand(EnemyDefense.DefinitionId),
                enemyBoard: generated.Board, enemyPreparation: new EnemyPreparationState());
            var target = GameActionTarget.FromItemUseTarget(ItemUseTarget.EnemyArea(1));
            ActionDispatchResult result = Dispatch(before, new UseHandItemAction(new ActionId("use-enemy"), 0, target));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.EnemyPreparation.GetDefenseBonus(0), Is.EqualTo(-1));
            Assert.That(result.NextState.EnemyPreparation.GetDefenseBonus(1), Is.EqualTo(0));
            Assert.That(result.NextState.Hand.TryGetSlot(0, out HandSlotContent content) && content.IsEmpty, Is.True);
        }

        [Test]
        public void HandItemWithoutActualEnemyTargetIsRejectedWithoutMutation()
        {
            ActionExecutionState before = State(Preparation(), hand: ItemInHand(EnemyDefense.DefinitionId),
                enemyBoard: EnemyBoardState.CreateEmpty());
            ActionDispatchResult result = Dispatch(before,
                new UseHandItemAction(new ActionId("no-enemies"), 0,
                    GameActionTarget.FromItemUseTarget(ItemUseTarget.EnemyArea(0))));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(result.NextState.EnemyPreparation, Is.SameAs(before.EnemyPreparation));
            Assert.That(result.NextState.RandomState, Is.EqualTo(before.RandomState));
        }

        [Test]
        public void EventItemUseThenEndEventTransitionsAndCannotBeUsedTwice()
        {
            CharacterInstance hero = new CharacterInstance("hero-1", Hero, 1, 1);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, hero.InstanceId, out board), Is.True);
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { hero });
            ActionExecutionState before = State(EventPending(), ownership: ownership, board: board,
                eventItem: new EventItemState(AllyAttack.DefinitionId));
            var target = GameActionTarget.FromItemUseTarget(ItemUseTarget.BoardCharacter(0));
            ActionDispatchResult used = Dispatch(before, new UseEventItemAction(new ActionId("event-use"), target));
            Assert.That(used.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), used.Error.Detail);
            Assert.That(used.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            Assert.That(used.NextState.EventItem.Status, Is.EqualTo(EventItemStatus.Consumed));
            Assert.That(used.NextState.Ownership.TryGet(hero.InstanceId, out CharacterInstanceView improved), Is.True);
            Assert.That(improved.Attack, Is.EqualTo(hero.Attack + 1));
            ActionDispatchResult ended = Dispatch(used.NextState, new EndEventAction(new ActionId("event-end")));
            Assert.That(ended.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), ended.Error.Detail);
            Assert.That(ended.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(ended.NextState.EventItem.Status, Is.EqualTo(EventItemStatus.Consumed));
            ActionDispatchResult repeated = Dispatch(ended.NextState, new UseEventItemAction(new ActionId("event-use-again"), target));
            Assert.That(repeated.Error.Code, Is.EqualTo(ActionErrorCode.InvalidPhase));
        }

        [Test]
        public void EndEventWithoutEligibleTargetUsesExistingRuleAndDoesNotConsumeRng()
        {
            ActionExecutionState before = State(EventPending(), eventItem: new EventItemState(AllyAttack.DefinitionId));
            ActionDispatchResult result = Dispatch(before, new EndEventAction(new ActionId("empty-event")));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.Stage.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(result.NextState.EventItem.Status, Is.EqualTo(EventItemStatus.EndedWithoutUse));
            Assert.That(result.NextState.RandomState, Is.EqualTo(before.RandomState));
        }

        [Test]
        public void EndEventRejectsSkippingAvailableEligibleItemTarget()
        {
            CharacterInstance hero = new CharacterInstance("hero-2", Hero, 1, 1);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, hero.InstanceId, out board), Is.True);
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { hero });
            ActionExecutionState state = State(EventPending(), ownership: ownership, board: board,
                eventItem: new EventItemState(AllyAttack.DefinitionId));
            ActionDispatchResult result = Dispatch(state, new EndEventAction(new ActionId("skip-eligible")));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [Test]
        public void EventItemUseOutsideEventPhaseIsRejected()
        {
            ActionExecutionState before = State(Preparation(), eventItem: new EventItemState(AllyAttack.DefinitionId));
            ActionDispatchResult result = Dispatch(before, new UseEventItemAction(new ActionId("wrong-event-phase"),
                GameActionTarget.FromItemUseTarget(ItemUseTarget.BoardCharacter(0))));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InvalidPhase));
            Assert.That(result.NextState, Is.SameAs(before));
        }

        [Test]
        public void EventStarItemAdvancesOnlyStatsStream()
        {
            CharacterStatDefinition settings = new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(1, new[] { new CharacterStatOption(10, 5, 1) },
                    new[] { new CharacterStatOption(20, 9, 1) }, new[] { new CharacterStatOption(30, 15, 1) })
            });
            CharacterInstance instance = new CharacterInstanceFactory().Create(Hero, 1);
            var setupRandom = new Pcg32RandomSource(4, 1);
            Assert.That(instance.TrySetStar(1, Hero, settings, setupRandom), Is.True);
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { instance });
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, instance.InstanceId, out board), Is.True);
            ItemDefinition starItem = Item("event-star", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            ActionExecutionState before = State(EventPending(), ownership: ownership, board: board,
                eventItem: new EventItemState(starItem.DefinitionId));
            ActionExecutionContext context = Context(items: new[] { starItem });
            GameActionTarget target = GameActionTarget.FromItemUseTarget(ItemUseTarget.BoardCharacter(0));
            ActionDispatchResult result = Dispatch(before,
                new UseEventItemAction(new ActionId("event-star"), target), context);
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            Assert.That(result.NextState.RandomState.Stats, Is.Not.EqualTo(before.RandomState.Stats));
            Assert.That(result.NextState.RandomState.Shop, Is.EqualTo(before.RandomState.Shop));
            Assert.That(result.NextState.RandomState.Encounter, Is.EqualTo(before.RandomState.Encounter));
            Assert.That(result.NextState.RandomState.Battle, Is.EqualTo(before.RandomState.Battle));
            Assert.That(result.NextState.RandomState.Event, Is.EqualTo(before.RandomState.Event));
            Assert.That(result.NextState.RandomState.Roster, Is.EqualTo(before.RandomState.Roster));
            Assert.That(result.NextState.RandomState.Economy, Is.EqualTo(before.RandomState.Economy));
            Assert.That(result.NextState.Ownership.TryGet(instance.InstanceId, out CharacterInstanceView changed), Is.True);
            Assert.That(changed.Star, Is.EqualTo(2));
        }

        [Test]
        public void PermanentStarItemPreservesPoolCopiesAcrossPurchaseAndSale()
        {
            var starItem = Item("event-star-pool", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            CharacterInstance original = new CharacterInstance("pool-origin", Hero, 1);
            Assert.That(original.TrySetStar(1, Hero, Stats, new Pcg32RandomSource(8, 3)), Is.True);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, original.InstanceId, out board), Is.True);
            var ownership = new CharacterOwnershipState(new[] { original });
            Pcg32State beforeStats = new Pcg32RandomSource(91, 7).CaptureState();
            ItemEffectResult itemResult = ItemEffectRules.TryUseEventItem(starItem,
                new EventItemState(starItem.DefinitionId),
                ItemUseTarget.BoardCharacter(0), board, HandState.CreateStandard(), ownership,
                new EconomyState(10, 0, 1, 12, 10, 0, 1, 20, 30), EventPending(),
                new ItemEffectState(), new[] { Hero }, Stats, beforeStats);

            Assert.That(itemResult.Succeeded, Is.True, itemResult.Failure.ToString());
            Assert.That(itemResult.NextOwnership.TryGet(original.InstanceId, out CharacterInstanceView upgraded), Is.True);
            Assert.That(upgraded.Star, Is.EqualTo(2));
            Assert.That(upgraded.PoolCopies, Is.EqualTo(1), "A free Star increase must not consume finite shop stock.");

            CharacterPool pool = new CharacterPool(new[] { new CharacterPoolEntry("hero", 13, 15, 1, 1) });
            ShopState shop = new ShopState(ShopKind.Character,
                new[] { new ShopOffer("offer", ShopOfferKind.Character, "hero", 1, 1) }, 1, 1, 1);
            EconomyState economy = new EconomyState(10, 0, 1, 12, 10, 0, 1, 20, 30);
            CharacterInstanceFactory factory = new CharacterInstanceFactory("purchase", 0);
            BoardShopTransactionResult purchase = BoardShopTransactionRules.TryPurchaseCharacter(shop, "offer",
                new[] { Hero }, Costs, economy, pool, itemResult.NextBoard, itemResult.NextHand,
                itemResult.NextOwnership, factory, Stats, beforeStats,
                new CharacterLocation(CharacterLocationKind.Hand, 0), ownershipScopeMatchesPool: true);

            Assert.That(purchase.Succeeded, Is.True, purchase.FailureReason.ToString());
            Assert.That(purchase.NextPool.Entries["hero"].OwnedCopies, Is.EqualTo(2));
            Assert.That(purchase.NextOwnership.TryGet(original.InstanceId, out CharacterInstanceView stillUpgraded), Is.True);
            Assert.That(stillUpgraded.PoolCopies, Is.EqualTo(1));

            BoardShopTransactionResult sale = BoardShopTransactionRules.TrySellCharacter(original.InstanceId,
                purchase.NextEconomy, purchase.NextShop, purchase.NextPool, purchase.NextBoard,
                purchase.NextHand, purchase.NextOwnership, purchase.NextInstanceFactory,
                purchase.NextStatsRandomState, ownershipScopeMatchesPool: true);
            Assert.That(sale.Succeeded, Is.True, sale.FailureReason.ToString());
            Assert.That(sale.GoldDelta, Is.EqualTo(3), "Legacy sale Gold remains Star-based.");
            Assert.That(sale.PoolCopiesDelta, Is.EqualTo(1), "Only the copy actually acquired from the pool is returned.");
            Assert.That(sale.NextPool.Entries["hero"].OwnedCopies, Is.EqualTo(1));
            Assert.That(sale.NextPool.Entries["hero"].RemainingCopies, Is.EqualTo(14));
            Assert.That(sale.NextPool.Entries["hero"].RemainingCopies +
                sale.NextPool.Entries["hero"].ReservedCopies + sale.NextPool.Entries["hero"].OwnedCopies,
                Is.EqualTo(15));
        }

        [Test]
        public void MergeAfterFreeStarIncreaseConservesOriginalPoolCopyContributions()
        {
            CharacterInstance itemUpgraded = new CharacterInstance("item-upgraded", Hero, 1, 2, poolCopies: 1);
            CharacterInstance boughtA = new CharacterInstance("bought-a", Hero, 1, 2, poolCopies: 3);
            CharacterInstance boughtB = new CharacterInstance("bought-b", Hero, 1, 2, poolCopies: 3);
            var owned = new CharacterOwnershipState(new[] { itemUpgraded, boughtA, boughtB });
            BoardState board = BoardState.CreateStandard();
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Character(itemUpgraded.InstanceId), out hand), Is.True);
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Character(boughtA.InstanceId), out hand), Is.True);
            Assert.That(hand.TryWithSlot(2, HandSlotContent.Character(boughtB.InstanceId), out hand), Is.True);

            CharacterMergeResult result = MergeRules.TryMerge(board, hand, owned,
                Hero, 2, Stats, new Pcg32RandomSource(71, 9).CaptureState());

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.NextOwnership.TryGet(itemUpgraded.InstanceId, out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.Star, Is.EqualTo(3));
            Assert.That(survivor.PoolCopies, Is.EqualTo(7));
            Assert.That(result.NextOwnership.OwnedCount, Is.EqualTo(1));
            Assert.That(result.NextHand.TryGetSlot(0, out HandSlotContent survivingSlot), Is.True);
            Assert.That(survivingSlot.CharacterInstanceId, Is.EqualTo(itemUpgraded.InstanceId));
        }

        [Test]
        public void AllFourteenHandlersRegisterAndStartBattleRemainsUnsupported()
        {
            var handlers = ShopEconomyActionHandlers.Create().Concat(BoardHandActionHandlers.Create())
                .Concat(StageItemEventActionHandlers.Create()).ToArray();
            Assert.That(handlers.Select(h => h.Kind).Distinct().Count(), Is.EqualTo(14));
            ActionExecutionState state = State(Preparation());
            ActionDispatchResult unsupported = new ActionDispatcher(handlers).Dispatch(state,
                new StartBattleAction(new ActionId("start-battle")), Context());
            Assert.That(unsupported.Error.Code, Is.EqualTo(ActionErrorCode.UnsupportedAction));
            Assert.That(unsupported.NextState, Is.SameAs(state));
        }

        [Test]
        public void EventUseFailureKeepsItemOwnershipAndStatsCheckpoint()
        {
            ActionExecutionState before = State(EventPending(), eventItem: new EventItemState(AllyAttack.DefinitionId));
            ActionDispatchResult result = Dispatch(before, new UseEventItemAction(new ActionId("invalid-event-target"),
                GameActionTarget.FromItemUseTarget(ItemUseTarget.BoardCharacter(0))));
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(result.NextState.RandomState.Stats, Is.EqualTo(before.RandomState.Stats));
            Assert.That(result.NextState.EventItem.Status, Is.EqualTo(EventItemStatus.Available));
        }

        private static StageProgressState Preparation()
        {
            StageProgressConfig config = Config();
            StageProgressState stage = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            stage = StageProgressRules.BeginBattle(stage).NextState;
            stage = StageProgressRules.ResolveBattle(stage, config, true).NextState;
            stage = StageProgressRules.CompletePendingReward(stage, config,
                new SimulationRandomStreams(25UL).Event.CaptureState()).NextState;
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            return stage;
        }

        private static int EnemyCount(EnemyBoardState board)
        {
            int count = 0;
            for (int i = 0; i < board.SlotCount; i++)
                if (board.TryGetAt(i, out EnemyInstance enemy) && enemy != null) count++;
            return count;
        }
    }
}
