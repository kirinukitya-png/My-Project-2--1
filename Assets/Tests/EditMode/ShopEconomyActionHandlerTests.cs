using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class ShopEconomyActionHandlerTests
    {
        private static readonly CharacterDefinition Hero = new CharacterDefinition(
            "hero", "Hero", 1, Array.Empty<string>(), Array.Empty<string>());
        private static readonly AssignedCharacterCosts Costs = new AssignedCharacterCosts(
            new[] { new KeyValuePair<string, int>("hero", 1) });
        private static readonly CharacterStatDefinition Stats = new CharacterStatDefinition(new[]
        {
            new CharacterCostStats(1,
                new[] { new CharacterStatOption(2, 1, 1) },
                new[] { new CharacterStatOption(4, 2, 1) },
                new[] { new CharacterStatOption(8, 4, 1) })
        });
        private static readonly CharacterShopConfiguration CharacterShop = new CharacterShopConfiguration(
            new[] { 1, 0, 0, 0, 0 }, new[] { 10, 0, 0, 0, 0 },
            new[] { new[] { 100, 0, 0, 0, 0 } }, new[] { 1 });
        private static readonly EconomyRulesConfiguration EconomyConfig = new EconomyRulesConfiguration(
            4, 2, 1, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
            10, 1, 3, 3, 1, 0);
        private static readonly ItemDefinition[] Items =
        {
            new ItemDefinition("i1", "Item 1", 2),
            new ItemDefinition("i2", "Item 2", 2),
            new ItemDefinition("i3", "Item 3", 2)
        };

        private static StageProgressConfig StageConfig(bool selectBCanReroll = true) =>
            new StageProgressConfig(20, 4, 2, 4, 0, 10, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, selectBCanReroll);

        private static StageProgressState PreparationStage(StageChoice choice = StageChoice.SelectA,
            bool selectBCanReroll = true)
        {
            StageProgressConfig config = StageConfig(selectBCanReroll);
            SimulationRandomStreams streams = new SimulationRandomStreams(901UL);
            StageProgressState stage = StageProgressRules.CreateInitialState(config);
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            stage = StageProgressRules.BeginBattle(stage).NextState;
            stage = StageProgressRules.ResolveBattle(stage, config, true).NextState;
            stage = StageProgressRules.CompletePendingReward(stage, config, streams.Event.CaptureState()).NextState;
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            Assert.That(stage.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            return StageSelectionRules.Select(stage, config, choice).NextState;
        }

        private static ActionExecutionContext Context(StageChoice choice = StageChoice.SelectA,
            bool selectBCanReroll = true, IEnumerable<ItemDefinition> items = null,
            int? rerollCost = 1, IEnumerable<CharacterDefinition> selectedRoster = null,
            CharacterShopConfiguration characterShop = null)
        {
            return new ActionExecutionContext(
                economyConfig: EconomyConfig,
                characterShopConfig: characterShop ?? CharacterShop,
                assignedCharacterCosts: Costs,
                characterStatSettings: Stats,
                selectedRoster: selectedRoster ?? new[] { Hero },
                itemDefinitions: items ?? Items,
                shopRerollGoldCost: rerollCost);
        }

        private static CharacterPool ReservedPool(int remaining = 9, int limit = 10, int owned = 0, int reserved = 1) =>
            new CharacterPool(new[] { new CharacterPoolEntry("hero", remaining, limit, reserved, owned) });

        private static ShopState CharacterShopState(int slots = 1, int generation = 0, bool purchased = false)
        {
            var offers = new List<ShopOffer>();
            for (int i = 0; i < slots; i++)
                offers.Add(new ShopOffer("c-offer-" + i, ShopOfferKind.Character, "hero", 1, 1,
                    purchased && i == 0 ? ShopOfferStatus.Purchased : ShopOfferStatus.Available));
            return new ShopState(ShopKind.Character, offers, slots, 1, 2, false, generation);
        }

        private static ShopState ItemShopState(int slots = 2, int generation = 0, bool purchased = false)
        {
            var offers = new List<ShopOffer>();
            for (int i = 0; i < Math.Min(slots, Items.Length); i++)
                offers.Add(new ShopOffer("i-offer-" + i, ShopOfferKind.Item, Items[i].DefinitionId, 2, null,
                    purchased && i == 0 ? ShopOfferStatus.Purchased : ShopOfferStatus.Available));
            return new ShopState(ShopKind.Item, offers, slots, 1, 2, false, generation);
        }

        private static SimulationRandomStreams Streams(ulong seed = 321UL) => new SimulationRandomStreams(seed);

        private static ActionExecutionState State(StageChoice choice = StageChoice.SelectA,
            ShopState shop = null, CharacterPool pool = null, EconomyState economy = null,
            BoardState board = null, HandState hand = null, CharacterOwnershipState ownership = null,
            SimulationRandomStreams streams = null, CharacterInstanceFactory factory = null,
            bool selectBCanReroll = true)
        {
            streams = streams ?? Streams();
            board = board ?? BoardState.CreateStandard();
            hand = hand ?? HandState.CreateStandard();
            ownership = ownership ?? new CharacterOwnershipState(Array.Empty<CharacterInstance>());
            return new ActionExecutionState(PreparationStage(choice, selectBCanReroll),
                economy ?? new EconomyState(20, 0, 1, 12, 20, 0, 1, 20, 30),
                pool ?? new CharacterPool(Array.Empty<CharacterPoolEntry>()),
                factory ?? new CharacterInstanceFactory("U", 0), board, hand, ownership,
                new ItemEffectState(), streams.CaptureState(), shop);
        }

        private static ActionDispatcher Dispatcher() => new ActionDispatcher(ShopEconomyActionHandlers.Create());

        private static ActionExecutionState DispatchSuccess(ActionExecutionState state, GameAction action,
            ActionExecutionContext context = null)
        {
            ActionDispatchResult result = Dispatcher().Dispatch(state, action,
                context ?? Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            return result.NextState;
        }

        private static void AssertAllRandomStreamsEqual(SimulationRandomStreamsState a, SimulationRandomStreamsState b)
        {
            Assert.That(b.TrialSeed, Is.EqualTo(a.TrialSeed));
            Assert.That(b.Version, Is.EqualTo(a.Version));
            Assert.That(b.Shop, Is.EqualTo(a.Shop));
            Assert.That(b.Stats, Is.EqualTo(a.Stats));
            Assert.That(b.Encounter, Is.EqualTo(a.Encounter));
            Assert.That(b.Battle, Is.EqualTo(a.Battle));
            Assert.That(b.Event, Is.EqualTo(a.Event));
            Assert.That(b.Roster, Is.EqualTo(a.Roster));
            Assert.That(b.Economy, Is.EqualTo(a.Economy));
        }

        [Test]
        public void HandlerSetRegistersOnlyTheFourStage10D2Actions()
        {
            var kinds = ShopEconomyActionHandlers.Create().Select(handler => handler.Kind).ToArray();
            Assert.That(kinds, Is.EquivalentTo(new[] { GameActionKind.BuyCharacter, GameActionKind.BuyItem,
                GameActionKind.RerollShop, GameActionKind.BuyExperience }));
            ActionDispatchResult unsupported = new ActionDispatcher(ShopEconomyActionHandlers.Create()).Dispatch(
                State(), new StartBattleAction(new ActionId("unsupported")), Context());
            Assert.That(unsupported.Error.Code, Is.EqualTo(ActionErrorCode.UnsupportedAction));
        }

        [Test]
        public void ActionExecutionStateReturnsAnIsolatedFactoryCopy()
        {
            ActionExecutionState state = State(factory: new CharacterInstanceFactory("U", 7));
            CharacterInstanceFactory leaked = state.InstanceFactory;
            leaked.Create(Hero, 1);
            Assert.That(leaked.LastIssuedNumber, Is.EqualTo(8));
            Assert.That(state.InstanceFactory.LastIssuedNumber, Is.EqualTo(7));
        }

        [Test]
        public void BuyCharacterPlacesPurchasedInstanceAndAtomicallyAdoptsGoldPoolOwnershipFactoryAndStatsRng()
        {
            SimulationRandomStreams streams = Streams();
            ActionExecutionState before = State(shop: CharacterShopState(), pool: ReservedPool(), streams: streams,
                factory: new CharacterInstanceFactory("U", 5));
            ActionExecutionState after = DispatchSuccess(before,
                new BuyCharacterAction(new ActionId("buy-character"), "c-offer-0",
                    new GameActionLocation(CharacterLocationKind.Board, 0)));
            Assert.That(before.Economy.Gold, Is.EqualTo(20));
            Assert.That(after.Economy.Gold, Is.EqualTo(19));
            Assert.That(after.Shop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(after.CharacterPool.Entries["hero"].OwnedCopies, Is.EqualTo(1));
            Assert.That(after.CharacterPool.Entries["hero"].ReservedCopies, Is.Zero);
            Assert.That(after.Board.TryGetCharacterIdAt(0, out string id), Is.True);
            Assert.That(id, Is.EqualTo("U6"));
            Assert.That(after.Ownership.Contains(id), Is.True);
            Assert.That(after.InstanceFactory.LastIssuedNumber, Is.EqualTo(6));
            Assert.That(after.RandomState.Stats, Is.Not.EqualTo(before.RandomState.Stats));
            Assert.That(after.RandomState.Shop, Is.EqualTo(before.RandomState.Shop));
            Assert.That(after.HasExecuted(new ActionId("buy-character")), Is.True);
        }

        [Test]
        public void BuyCharacterCanPlaceDirectlyIntoAnEmptyHandSlot()
        {
            ActionExecutionState before = State(shop: CharacterShopState(), pool: ReservedPool());
            ActionExecutionState after = DispatchSuccess(before,
                new BuyCharacterAction(new ActionId("buy-hand"), "c-offer-0",
                    new GameActionLocation(CharacterLocationKind.Hand, 3)));
            Assert.That(after.Hand.TryGetSlot(3, out HandSlotContent content), Is.True);
            Assert.That(content.Kind, Is.EqualTo(HandSlotContentKind.Character));
            Assert.That(after.Board.TryGetCharacterIdAt(0, out string _), Is.True);
            Assert.That(after.Ownership.Contains(content.CharacterInstanceId), Is.True);
        }

        [Test]
        public void BuyCharacterRejectsInsufficientGoldWithoutChangingAnyStateOrCheckpoint()
        {
            SimulationRandomStreams streams = Streams();
            ActionExecutionState before = State(shop: CharacterShopState(), pool: ReservedPool(),
                economy: new EconomyState(0, 0, 1, 12, 0, 0, 1, 20, 30), streams: streams,
                factory: new CharacterInstanceFactory("U", 14));
            ActionDispatchResult result = Dispatcher().Dispatch(before,
                new BuyCharacterAction(new ActionId("broke"), "c-offer-0",
                    new GameActionLocation(CharacterLocationKind.Hand, 0)), Context());
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InsufficientGold));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(before.Economy.Gold, Is.Zero);
            Assert.That(before.Shop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Available));
            Assert.That(before.CharacterPool.Entries["hero"].ReservedCopies, Is.EqualTo(1));
            Assert.That(before.InstanceFactory.LastIssuedNumber, Is.EqualTo(14));
            AssertAllRandomStreamsEqual(before.RandomState, result.NextState.RandomState);
        }

        [Test]
        public void BuyCharacterRejectsInvalidSlotAndAlreadyPurchasedOffer()
        {
            ActionExecutionState before = State(shop: CharacterShopState(), pool: ReservedPool());
            ActionDispatchResult invalid = Dispatcher().Dispatch(before,
                new BuyCharacterAction(new ActionId("bad-slot"), "c-offer-0",
                    new GameActionLocation(CharacterLocationKind.Hand, 99)), Context());
            Assert.That(invalid.Error.Code, Is.EqualTo(ActionErrorCode.InvalidSlot));
            Assert.That(invalid.NextState, Is.SameAs(before));

            ActionExecutionState purchased = State(shop: CharacterShopState(purchased: true),
                pool: new CharacterPool(new[] { new CharacterPoolEntry("hero", 9, 10, 0, 1) }));
            ActionDispatchResult secondBuy = Dispatcher().Dispatch(purchased,
                new BuyCharacterAction(new ActionId("already-bought"), "c-offer-0",
                    new GameActionLocation(CharacterLocationKind.Hand, 0)), Context());
            Assert.That(secondBuy.Error.Code, Is.EqualTo(ActionErrorCode.AlreadyPurchased));
            Assert.That(secondBuy.NextState, Is.SameAs(purchased));
        }

        [Test]
        public void BuyCharacterRejectsOccupiedHandAndDuplicateDefinitionOnBoard()
        {
            HandState occupiedHand = HandState.CreateStandard();
            Assert.That(occupiedHand.TryWithSlot(0, HandSlotContent.Item("already-here"), out occupiedHand), Is.True);
            ActionExecutionState handState = State(shop: CharacterShopState(), pool: ReservedPool(), hand: occupiedHand);
            ActionDispatchResult handFailure = Dispatcher().Dispatch(handState,
                new BuyCharacterAction(new ActionId("hand-full"), "c-offer-0",
                    new GameActionLocation(CharacterLocationKind.Hand, 0)), Context());
            Assert.That(handFailure.Error.Code, Is.EqualTo(ActionErrorCode.HandFull));
            Assert.That(handFailure.NextState, Is.SameAs(handState));

            var placed = new CharacterInstance("existing", Hero, 1);
            var ownership = new CharacterOwnershipState(new[] { placed });
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, "existing", out board), Is.True);
            CharacterPool pool = new CharacterPool(new[] { new CharacterPoolEntry("hero", 8, 10, 1, 1) });
            ActionExecutionState boardState = State(shop: CharacterShopState(), pool: pool,
                board: board, ownership: ownership);
            ActionDispatchResult duplicate = Dispatcher().Dispatch(boardState,
                new BuyCharacterAction(new ActionId("duplicate-on-board"), "c-offer-0",
                    new GameActionLocation(CharacterLocationKind.Board, 1)), Context());
            Assert.That(duplicate.Error.Code, Is.EqualTo(ActionErrorCode.BoardLimitExceeded));
            Assert.That(duplicate.NextState, Is.SameAs(boardState));
        }

        [Test]
        public void BuyCharacterPerformsApprovedTwoStageMergeWithoutIssuingAnUnusedInstanceId()
        {
            var star1Board = new CharacterInstance("old-1", Hero, 1);
            var star1Hand = new CharacterInstance("old-2", Hero, 1);
            var star2Hand1 = new CharacterInstance("old-3", Hero, 1, 2);
            var star2Hand2 = new CharacterInstance("old-4", Hero, 1, 2);
            var ownership = new CharacterOwnershipState(new[] { star1Board, star1Hand, star2Hand1, star2Hand2 });
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, "old-1", out board), Is.True);
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Character("old-2"), out hand), Is.True);
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Character("old-3"), out hand), Is.True);
            Assert.That(hand.TryWithSlot(2, HandSlotContent.Character("old-4"), out hand), Is.True);
            var pool = new CharacterPool(new[] { new CharacterPoolEntry("hero", 21, 30, 1, 8) });
            ActionExecutionState before = State(shop: CharacterShopState(), pool: pool, board: board,
                hand: hand, ownership: ownership, factory: new CharacterInstanceFactory("U", 50));
            ActionExecutionState after = DispatchSuccess(before,
                new BuyCharacterAction(new ActionId("chain-merge"), "c-offer-0",
                    new GameActionLocation(CharacterLocationKind.Hand, 4)));
            Assert.That(after.Ownership.OwnedCount, Is.EqualTo(1));
            Assert.That(after.Ownership.TryGet("old-1", out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.Star, Is.EqualTo(3));
            Assert.That(after.Board.TryGetCharacterIdAt(0, out string survivorId), Is.True);
            Assert.That(survivorId, Is.EqualTo("old-1"));
            Assert.That(after.Hand.TryGetSlot(4, out HandSlotContent unused), Is.True);
            Assert.That(unused.IsEmpty, Is.True);
            Assert.That(after.CharacterPool.Entries["hero"].OwnedCopies, Is.EqualTo(9));
            Assert.That(after.CharacterPool.Entries["hero"].ReservedCopies, Is.Zero);
            Assert.That(after.InstanceFactory.LastIssuedNumber, Is.EqualTo(50));
            Assert.That(BoardHandStateValidator.Validate(after.Board, after.Hand, after.Ownership).IsValid, Is.True);
        }

        [Test]
        public void BuyItemConsumesOfferAndStoresDefinitionInHandWithoutTouchingCharacterPoolOrRng()
        {
            SimulationRandomStreams streams = Streams();
            var pool = ReservedPool();
            ActionExecutionState before = State(choice: StageChoice.SelectB, shop: ItemShopState(),
                pool: pool, streams: streams);
            ActionExecutionState after = DispatchSuccess(before,
                new BuyItemAction(new ActionId("buy-item"), "i-offer-0", 2),
                Context(StageChoice.SelectB));
            Assert.That(after.Economy.Gold, Is.EqualTo(before.Economy.Gold - 2));
            Assert.That(after.Shop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(after.Hand.TryGetSlot(2, out HandSlotContent slot), Is.True);
            Assert.That(slot.ItemDefinitionId, Is.EqualTo("i1"));
            Assert.That(after.CharacterPool, Is.SameAs(pool));
            AssertAllRandomStreamsEqual(before.RandomState, after.RandomState);
        }

        [Test]
        public void BuyItemRejectsInvalidOfferInsufficientGoldOccupiedAndOutOfRangeSlotsAtomically()
        {
            ActionExecutionState state = State(choice: StageChoice.SelectB, shop: ItemShopState());
            var handler = new ActionDispatcher(ShopEconomyActionHandlers.Create());
            ActionDispatchResult unknown = handler.Dispatch(state,
                new BuyItemAction(new ActionId("unknown-item"), "missing", 0), Context(StageChoice.SelectB));
            Assert.That(unknown.Error.Code, Is.EqualTo(ActionErrorCode.UnknownOffer));
            Assert.That(unknown.NextState, Is.SameAs(state));

            ActionExecutionState broke = State(choice: StageChoice.SelectB, shop: ItemShopState(),
                economy: new EconomyState(1, 0, 1, 12, 1, 0, 1, 20, 30));
            ActionDispatchResult insufficient = handler.Dispatch(broke,
                new BuyItemAction(new ActionId("poor-item"), "i-offer-0", 0), Context(StageChoice.SelectB));
            Assert.That(insufficient.Error.Code, Is.EqualTo(ActionErrorCode.InsufficientGold));
            Assert.That(insufficient.NextState, Is.SameAs(broke));

            HandState occupiedHand = HandState.CreateStandard();
            Assert.That(occupiedHand.TryWithSlot(0, HandSlotContent.Item("owned-item"), out occupiedHand), Is.True);
            ActionExecutionState occupied = State(choice: StageChoice.SelectB, shop: ItemShopState(), hand: occupiedHand);
            ActionDispatchResult full = handler.Dispatch(occupied,
                new BuyItemAction(new ActionId("occupied-item"), "i-offer-0", 0), Context(StageChoice.SelectB));
            Assert.That(full.Error.Code, Is.EqualTo(ActionErrorCode.HandFull));
            Assert.That(full.NextState, Is.SameAs(occupied));

            ActionDispatchResult badSlot = handler.Dispatch(state,
                new BuyItemAction(new ActionId("bad-item-slot"), "i-offer-0", 5), Context(StageChoice.SelectB));
            Assert.That(badSlot.Error.Code, Is.EqualTo(ActionErrorCode.InvalidSlot));
            Assert.That(badSlot.NextState, Is.SameAs(state));
        }

        [Test]
        public void BuyItemRejectsOutsideItemShopAndRepeatedOffer()
        {
            var handler = Dispatcher();
            ActionExecutionState wrongShop = State(choice: StageChoice.SelectA, shop: CharacterShopState());
            ActionDispatchResult rejected = handler.Dispatch(wrongShop,
                new BuyItemAction(new ActionId("wrong-shop"), "c-offer-0", 0), Context());
            Assert.That(rejected.Error.Code, Is.EqualTo(ActionErrorCode.InvalidAction));
            Assert.That(rejected.NextState, Is.SameAs(wrongShop));

            ActionExecutionState purchased = State(choice: StageChoice.SelectB,
                shop: ItemShopState(purchased: true));
            ActionDispatchResult repeat = handler.Dispatch(purchased,
                new BuyItemAction(new ActionId("repeat-item"), "i-offer-0", 0), Context(StageChoice.SelectB));
            Assert.That(repeat.Error.Code, Is.EqualTo(ActionErrorCode.AlreadyPurchased));
            Assert.That(repeat.NextState, Is.SameAs(purchased));
        }

        [Test]
        public void CharacterRerollSpendsConfiguredGoldReturnsAndReservesStockAndAdvancesOnlyShopRng()
        {
            SimulationRandomStreams streams = Streams(777UL);
            ActionExecutionState before = State(shop: CharacterShopState(), pool: ReservedPool(), streams: streams);
            ActionExecutionState after = DispatchSuccess(before,
                new RerollShopAction(new ActionId("reroll-character"), ShopKind.Character));
            Assert.That(after.Economy.Gold, Is.EqualTo(19));
            Assert.That(after.Shop.GenerationIndex, Is.EqualTo(1));
            Assert.That(after.Shop.Offers.Count, Is.EqualTo(1));
            Assert.That(after.Shop.Offers[0].Kind, Is.EqualTo(ShopOfferKind.Character));
            Assert.That(after.CharacterPool.Entries["hero"].ReservedCopies, Is.EqualTo(1));
            Assert.That(after.CharacterPool.Entries["hero"].RemainingCopies, Is.EqualTo(9));
            Assert.That(after.CharacterPool.Entries["hero"].OwnedCopies, Is.Zero);
            Assert.That(after.RandomState.Shop, Is.Not.EqualTo(before.RandomState.Shop));
            Assert.That(after.RandomState.Stats, Is.EqualTo(before.RandomState.Stats));
            Assert.That(after.RandomState.Economy, Is.EqualTo(before.RandomState.Economy));
            Assert.That(after.RandomState.Battle, Is.EqualTo(before.RandomState.Battle));
        }

        [Test]
        public void ItemRerollUsesUniqueItemDefinitionsAndDoesNotChangeCharacterPool()
        {
            SimulationRandomStreams streams = Streams(778UL);
            CharacterPool pool = ReservedPool();
            ActionExecutionState before = State(choice: StageChoice.SelectB, shop: ItemShopState(),
                pool: pool, streams: streams);
            ActionExecutionState after = DispatchSuccess(before,
                new RerollShopAction(new ActionId("reroll-item"), ShopKind.Item), Context(StageChoice.SelectB));
            Assert.That(after.Economy.Gold, Is.EqualTo(19));
            Assert.That(after.Shop.GenerationIndex, Is.EqualTo(1));
            Assert.That(after.Shop.Offers.Select(offer => offer.DefinitionId).Distinct().Count(),
                Is.EqualTo(after.Shop.Offers.Count));
            Assert.That(after.CharacterPool, Is.SameAs(pool));
            Assert.That(after.RandomState.Shop, Is.Not.EqualTo(before.RandomState.Shop));
            Assert.That(after.RandomState.Stats, Is.EqualTo(before.RandomState.Stats));
            Assert.That(after.RandomState.Economy, Is.EqualTo(before.RandomState.Economy));
        }

        [Test]
        public void RerollRejectsDisabledSelectBAndMismatchedShopWithoutSpendingOrRolling()
        {
            ActionExecutionState disabled = State(choice: StageChoice.SelectB, shop: ItemShopState(), selectBCanReroll: false);
            ActionDispatchResult blocked = Dispatcher().Dispatch(disabled,
                new RerollShopAction(new ActionId("reroll-disabled"), ShopKind.Item), Context(StageChoice.SelectB));
            Assert.That(blocked.Error.Code, Is.EqualTo(ActionErrorCode.InvalidAction));
            Assert.That(blocked.NextState, Is.SameAs(disabled));

            ActionExecutionState mismatch = State(choice: StageChoice.SelectB, shop: ItemShopState());
            ActionDispatchResult wrongKind = Dispatcher().Dispatch(mismatch,
                new RerollShopAction(new ActionId("reroll-wrong-kind"), ShopKind.Character), Context(StageChoice.SelectB));
            Assert.That(wrongKind.Error.Code, Is.EqualTo(ActionErrorCode.InvalidAction));
            Assert.That(wrongKind.NextState, Is.SameAs(mismatch));
        }

        [Test]
        public void RerollGoldShortageAndCharacterCandidateShortageRollbackAllOutputsAndRng()
        {
            SimulationRandomStreams streams = Streams(779UL);
            ActionExecutionState broke = State(shop: CharacterShopState(), pool: ReservedPool(),
                economy: new EconomyState(0, 0, 1, 12, 0, 0, 1, 20, 30), streams: streams);
            ActionDispatchResult insufficient = Dispatcher().Dispatch(broke,
                new RerollShopAction(new ActionId("reroll-poor"), ShopKind.Character), Context());
            Assert.That(insufficient.Error.Code, Is.EqualTo(ActionErrorCode.InsufficientGold));
            Assert.That(insufficient.NextState, Is.SameAs(broke));

            var shortPool = new CharacterPool(new[] { new CharacterPoolEntry("hero", 0, 1, 1, 0) });
            var twoSlotShop = new ShopState(ShopKind.Character,
                new[] { new ShopOffer("reserved", ShopOfferKind.Character, "hero", 1, 1) },
                2, 1, 2, false, 0);
            ActionExecutionState shortage = State(shop: twoSlotShop, pool: shortPool, streams: streams);
            ActionDispatchResult rolledBack = Dispatcher().Dispatch(shortage,
                new RerollShopAction(new ActionId("reroll-shortage"), ShopKind.Character), Context());
            Assert.That(rolledBack.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(rolledBack.Error.Code, Is.EqualTo(ActionErrorCode.InsufficientStock));
            Assert.That(rolledBack.NextState, Is.SameAs(shortage));
            Assert.That(shortage.Economy.Gold, Is.EqualTo(20));
            Assert.That(shortage.Shop.GenerationIndex, Is.Zero);
            Assert.That(shortage.CharacterPool.Entries["hero"].ReservedCopies, Is.EqualTo(1));
            AssertAllRandomStreamsEqual(shortage.RandomState, rolledBack.NextState.RandomState);
        }

        [Test]
        public void RerollRejectsInvalidDefinitionsAsConfigurationErrorWithoutAdoptingLocalChanges()
        {
            SimulationRandomStreams streams = Streams(780UL);
            ActionExecutionState before = State(choice: StageChoice.SelectB, shop: ItemShopState(), streams: streams);
            ActionExecutionContext context = Context(StageChoice.SelectB,
                items: new[] { new ItemDefinition("dup", "A", 1), new ItemDefinition("dup", "B", 1) });
            ActionDispatchResult result = Dispatcher().Dispatch(before,
                new RerollShopAction(new ActionId("bad-item-config"), ShopKind.Item), context);
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(result.NextState, Is.SameAs(before));
            Assert.That(before.Economy.Gold, Is.EqualTo(20));
            Assert.That(before.Shop.GenerationIndex, Is.Zero);
            AssertAllRandomStreamsEqual(before.RandomState, result.NextState.RandomState);
        }

        [TestCase(StageChoice.SelectA, 4)]
        [TestCase(StageChoice.SelectB, 2)]
        public void BuyExperienceUsesTheCurrentStageChoicePriceAndDoesNotConsumeRng(StageChoice choice, int price)
        {
            SimulationRandomStreams streams = Streams(781UL);
            ActionExecutionState before = State(choice: choice,
                economy: new EconomyState(10, 0, 1, 12, 10, 0, 1, 20, 30), streams: streams);
            ActionExecutionState after = DispatchSuccess(before,
                new BuyExperienceAction(new ActionId("xp-" + choice)), Context(choice));
            Assert.That(after.Economy.Gold, Is.EqualTo(10 - price));
            Assert.That(after.Economy.XP, Is.Zero);
            Assert.That(after.Economy.Level, Is.EqualTo(2));
            AssertAllRandomStreamsEqual(before.RandomState, after.RandomState);
        }

        [Test]
        public void BuyExperienceRejectsGoldShortageAndMaximumLevelWithoutChangingStateOrRng()
        {
            ActionExecutionState broke = State(choice: StageChoice.SelectA,
                economy: new EconomyState(3, 0, 1, 12, 3, 0, 1, 20, 30));
            ActionDispatchResult poor = Dispatcher().Dispatch(broke,
                new BuyExperienceAction(new ActionId("xp-poor")), Context());
            Assert.That(poor.Error.Code, Is.EqualTo(ActionErrorCode.InsufficientGold));
            Assert.That(poor.NextState, Is.SameAs(broke));

            ActionExecutionState maxed = State(choice: StageChoice.SelectB,
                economy: new EconomyState(10, 0, 12, 12, 10, 0, 12, 20, 30));
            ActionDispatchResult atMax = Dispatcher().Dispatch(maxed,
                new BuyExperienceAction(new ActionId("xp-max")), Context(StageChoice.SelectB));
            Assert.That(atMax.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(atMax.NextState, Is.SameAs(maxed));
            Assert.That(maxed.Economy.Gold, Is.EqualTo(10));
            Assert.That(maxed.Economy.Level, Is.EqualTo(12));
            AssertAllRandomStreamsEqual(maxed.RandomState, atMax.NextState.RandomState);
        }

        [Test]
        public void DispatcherDeduplicatesSuccessfulShopActionButAllowsRetryAfterFailure()
        {
            var dispatcher = Dispatcher();
            ActionExecutionState before = State(shop: CharacterShopState(), pool: ReservedPool(),
                economy: new EconomyState(0, 0, 1, 12, 0, 0, 1, 20, 30));
            var action = new BuyCharacterAction(new ActionId("retryable"), "c-offer-0",
                new GameActionLocation(CharacterLocationKind.Hand, 0));
            ActionDispatchResult rejected = dispatcher.Dispatch(before, action, Context());
            Assert.That(rejected.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(rejected.NextState.HasExecuted(action.ActionId), Is.False);

            ActionExecutionState funded = before.With(economy: new EconomyState(10, 0, 1, 12, 10, 0, 1, 20, 30));
            ActionDispatchResult succeeded = dispatcher.Dispatch(funded, action, Context());
            Assert.That(succeeded.Succeeded, Is.True, succeeded.Error.Detail);
            ActionDispatchResult duplicate = dispatcher.Dispatch(succeeded.NextState, action, Context());
            Assert.That(duplicate.Error.Code, Is.EqualTo(ActionErrorCode.DuplicateAction));
            Assert.That(duplicate.NextState, Is.SameAs(succeeded.NextState));
        }

        [Test]
        public void ShopAndEconomyActionsReplayToIdenticalStatesAndRandomCheckpoints()
        {
            ActionExecutionState Run(ulong seed)
            {
                SimulationRandomStreams streams = Streams(seed);
                ActionExecutionState state = State(choice: StageChoice.SelectB, shop: ItemShopState(), streams: streams);
                ActionDispatcher dispatcher = Dispatcher();
                state = dispatcher.Dispatch(state,
                    new RerollShopAction(new ActionId("replay-reroll"), ShopKind.Item), Context(StageChoice.SelectB)).NextState;
                string itemOffer = state.Shop.Offers[0].OfferId;
                state = dispatcher.Dispatch(state,
                    new BuyItemAction(new ActionId("replay-buy"), itemOffer, 0), Context(StageChoice.SelectB)).NextState;
                state = dispatcher.Dispatch(state,
                    new BuyExperienceAction(new ActionId("replay-xp")), Context(StageChoice.SelectB)).NextState;
                return state;
            }

            ActionExecutionState left = Run(782UL);
            ActionExecutionState right = Run(782UL);
            Assert.That(left.Economy.Gold, Is.EqualTo(right.Economy.Gold));
            Assert.That(left.Economy.XP, Is.EqualTo(right.Economy.XP));
            Assert.That(left.Economy.Level, Is.EqualTo(right.Economy.Level));
            Assert.That(left.Shop.GenerationIndex, Is.EqualTo(right.Shop.GenerationIndex));
            Assert.That(left.Shop.Offers.Select(o => o.DefinitionId), Is.EqualTo(right.Shop.Offers.Select(o => o.DefinitionId)));
            Assert.That(left.Hand.Slots.Select(s => s.ItemDefinitionId), Is.EqualTo(right.Hand.Slots.Select(s => s.ItemDefinitionId)));
            Assert.That(left.ExecutedActionIds, Is.EqualTo(right.ExecutedActionIds));
            AssertAllRandomStreamsEqual(left.RandomState, right.RandomState);
        }
    }
}
