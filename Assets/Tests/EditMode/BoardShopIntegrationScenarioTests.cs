using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Tests
{
    /// <summary>Multi-operation GameCore integration coverage. State adoption below is test-only.</summary>
    public sealed class BoardShopIntegrationScenarioTests
    {
        [Test]
        public void PreparationFlow_BuyMoveSwapReturnPurchaseAndSellPreservesInvariantsAfterEveryAction()
        {
            CharacterDefinition a = Definition("a");
            CharacterDefinition b = Definition("b");
            CharacterDefinition c = Definition("c");
            World w = EmptyWorld(new[] { a, b, c }, new[] { Offer("oa", "a"), Offer("ob", "b"), Offer("oc", "c") }, 100, 301);

            BoardShopTransactionResult buyA = Buy(w, "oa", new CharacterLocation(CharacterLocationKind.Hand, 0));
            Assert.That(buyA.Succeeded, Is.True, buyA.FailureReason.ToString());
            w.Adopt(buyA); AssertIntegrity(w);
            string idA = HandId(w.Hand, 0);

            BoardHandOperationResult handToBoard = BoardHandRules.MoveHandToBoard(w.Board, w.Hand, w.Ownership,
                0, 0, BoardHandOperationContext.Preparation);
            Assert.That(handToBoard.Succeeded, Is.True);
            w.Adopt(handToBoard); AssertIntegrity(w);

            BoardShopTransactionResult buyB = Buy(w, "ob", new CharacterLocation(CharacterLocationKind.Hand, 1));
            Assert.That(buyB.Succeeded, Is.True, buyB.FailureReason.ToString());
            w.Adopt(buyB); AssertIntegrity(w);
            string idB = HandId(w.Hand, 1);
            handToBoard = BoardHandRules.MoveHandToBoard(w.Board, w.Hand, w.Ownership,
                1, 1, BoardHandOperationContext.Preparation);
            Assert.That(handToBoard.Succeeded, Is.True);
            w.Adopt(handToBoard); AssertIntegrity(w);

            BoardHandOperationResult swap = BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership,
                0, 1, BoardHandOperationContext.Preparation);
            Assert.That(swap.Succeeded, Is.True);
            w.Adopt(swap); AssertIntegrity(w);
            AssertBoard(w.Board, 0, idB);
            AssertBoard(w.Board, 1, idA);

            BoardHandOperationResult boardToHand = BoardHandRules.MoveBoardToHand(w.Board, w.Hand,
                w.Ownership, 0, 0, BoardHandOperationContext.Preparation);
            Assert.That(boardToHand.Succeeded, Is.True);
            w.Adopt(boardToHand); AssertIntegrity(w);
            Assert.That(HandId(w.Hand, 0), Is.EqualTo(idB));

            BoardShopTransactionResult buyC = Buy(w, "oc", new CharacterLocation(CharacterLocationKind.Hand, 2));
            Assert.That(buyC.Succeeded, Is.True, buyC.FailureReason.ToString());
            w.Adopt(buyC); AssertIntegrity(w);
            string idC = HandId(w.Hand, 2);

            BoardShopTransactionResult sale = Sell(w, idB);
            Assert.That(sale.Succeeded, Is.True, sale.FailureReason.ToString());
            w.Adopt(sale); AssertIntegrity(w);

            Assert.That(w.Economy.Gold, Is.EqualTo(96));
            Assert.That(w.Ownership.Contains(idB), Is.False);
            Assert.That(w.Ownership.Contains(idA), Is.True);
            Assert.That(w.Ownership.Contains(idC), Is.True);
            AssertBoard(w.Board, 1, idA);
            Assert.That(HandId(w.Hand, 2), Is.EqualTo(idC));
            Assert.That(Entry(w.Pool, "a").OwnedCopies, Is.EqualTo(1));
            Assert.That(Entry(w.Pool, "b").OwnedCopies, Is.Zero);
            Assert.That(Entry(w.Pool, "c").OwnedCopies, Is.EqualTo(1));
        }

        [Test]
        public void PurchaseChainToThreeStars_ThenSaleReturnsAllNineCopiesAndGold()
        {
            CharacterDefinition hero = Definition("hero");
            CharacterInstance h1 = Instance("I1", hero, 2, 2);
            CharacterInstance h2 = Instance("I2", hero, 2, 2);
            CharacterInstance h3 = Instance("I3", hero, 2, 1);
            CharacterInstance h4 = Instance("I4", hero, 2, 1);
            var boardIds = EmptyBoardIds(); boardIds[0] = h1.InstanceId;
            var hand = HandWith((0, h2), (1, h3), (2, h4));
            World w = WorldWith(new[] { hero }, new[] { Offer("oh", "hero") }, 100, 512,
                new[] { h1, h2, h3, h4 }, boardIds, hand, reservedPerOffer: 1, stockPerDefinition: 20);

            BoardShopTransactionResult purchase = Buy(w, "oh", new CharacterLocation(CharacterLocationKind.Hand, 3));
            Assert.That(purchase.Succeeded, Is.True, purchase.FailureReason.ToString());
            Assert.That(purchase.MergeStageCount, Is.EqualTo(2));
            Assert.That(purchase.NextInstanceFactory.LastIssuedNumber, Is.EqualTo(w.Factory.LastIssuedNumber));
            Assert.That(purchase.PoolOwnershipConsistencyVerified, Is.True);
            Assert.That(Entry(purchase.NextPool, "hero").OwnedCopies, Is.EqualTo(9));
            Assert.That(purchase.MergeStages[0].StarBefore, Is.EqualTo(1));
            Assert.That(purchase.MergeStages[0].StarAfter, Is.EqualTo(2));
            Assert.That(purchase.MergeStages[1].StarBefore, Is.EqualTo(2));
            Assert.That(purchase.MergeStages[1].StarAfter, Is.EqualTo(3));
            Assert.That(purchase.MergeStages[0].ConsumedPurchasedCopy, Is.True);
            w.Adopt(purchase); AssertIntegrity(w);
            Assert.That(w.Ownership.OwnedCount, Is.EqualTo(1));
            Assert.That(w.Ownership.TryGet(purchase.FinalSurvivingInstanceId, out CharacterInstanceView star3), Is.True);
            Assert.That(star3.Star, Is.EqualTo(3));
            AssertBoard(w.Board, 0, purchase.FinalSurvivingInstanceId);
            Assert.That(w.Factory.LastIssuedNumber, Is.EqualTo(0));

            int goldBeforeSale = w.Economy.Gold;
            int remainingBeforeSale = Entry(w.Pool, "hero").RemainingCopies;
            BoardShopTransactionResult sale = Sell(w, purchase.FinalSurvivingInstanceId);
            Assert.That(sale.Succeeded, Is.True, sale.FailureReason.ToString());
            Assert.That(sale.GoldDelta, Is.EqualTo(star3.RunCost * 9));
            w.Adopt(sale); AssertIntegrity(w);
            Assert.That(OwnedCount(w.Ownership, "hero"), Is.Zero);
            Assert.That(Entry(w.Pool, "hero").OwnedCopies, Is.Zero);
            Assert.That(Entry(w.Pool, "hero").RemainingCopies, Is.EqualTo(remainingBeforeSale + 9));
            Assert.That(w.Economy.Gold, Is.EqualTo(goldBeforeSale + star3.RunCost * 9));
            Assert.That(w.Board.TryGetCharacterIdAt(0, out string empty), Is.True);
            Assert.That(empty, Is.Null);
        }

        [Test]
        public void FailedPurchaseAndSaleAttemptsAreAtomic_AndValidPurchaseCanBeRetried()
        {
            CharacterDefinition hero = Definition("hero");
            World poor = EmptyWorld(new[] { hero }, new[] { Offer("oh", "hero") }, 1, 610);
            Pcg32State rngBefore = poor.RandomState;
            BoardShopTransactionResult insufficient = Buy(poor, "oh", new CharacterLocation(CharacterLocationKind.Hand, 0));
            AssertFailureAtomic(insufficient, poor, BoardShopTransactionFailureReason.InsufficientGold, rngBefore);

            poor.Economy = new EconomyState(10, 0, 1, 12, 1, 0, 1);
            BoardShopTransactionResult badSlot = Buy(poor, "oh", new CharacterLocation(CharacterLocationKind.Hand, 88));
            AssertFailureAtomic(badSlot, poor, BoardShopTransactionFailureReason.InvalidPlacement, rngBefore);

            BoardShopTransactionResult missingSale = Sell(poor, "not-owned");
            AssertFailureAtomic(missingSale, poor, BoardShopTransactionFailureReason.NotOwned, rngBefore);

            BoardShopTransactionResult retry = Buy(poor, "oh", new CharacterLocation(CharacterLocationKind.Hand, 0));
            Assert.That(retry.Succeeded, Is.True, retry.FailureReason.ToString());
            poor.Adopt(retry); AssertIntegrity(poor);
            Assert.That(poor.Economy.Gold, Is.EqualTo(8));
            Assert.That(poor.Ownership.OwnedCount, Is.EqualTo(1));

            CharacterInstance existingTwoStar = Instance("I2", hero, 2, 2);
            string[] duplicateBoardIds = EmptyBoardIds(); duplicateBoardIds[0] = existingTwoStar.InstanceId;
            World duplicateBoard = WorldWith(new[] { hero }, new[] { Offer("oh", "hero") }, 10, 611,
                new[] { existingTwoStar }, duplicateBoardIds, HandState.CreateStandard(), 1, 20);
            BoardShopTransactionResult duplicate = Buy(duplicateBoard, "oh", new CharacterLocation(CharacterLocationKind.Board, 1));
            AssertFailureAtomic(duplicate, duplicateBoard, BoardShopTransactionFailureReason.DuplicateDefinitionOnBoard,
                duplicateBoard.RandomState);
        }

        [Test]
        public void ChainCandidateSelectionUsesBoardThenSlotOrder_AndKeepsSurplusCopies()
        {
            CharacterDefinition hero = Definition("hero");
            CharacterInstance boardOne = Instance("I1", hero, 2, 1);
            CharacterInstance handOne = Instance("I2", hero, 2, 1);
            CharacterInstance handTwoStarA = Instance("I3", hero, 2, 2);
            CharacterInstance handTwoStarB = Instance("I4", hero, 2, 2);
            CharacterInstance handTwoStarExtra = Instance("I5", hero, 2, 2);
            string[] boardIds = EmptyBoardIds(); boardIds[4] = boardOne.InstanceId;
            HandState hand = HandWith((0, handOne), (1, handTwoStarA), (2, handTwoStarB), (3, handTwoStarExtra));
            World w = WorldWith(new[] { hero }, new[] { Offer("oh", "hero") }, 100, 712,
                new[] { boardOne, handOne, handTwoStarA, handTwoStarB, handTwoStarExtra },
                boardIds, hand, 1, 20);

            BoardShopTransactionResult r = Buy(w, "oh", new CharacterLocation(CharacterLocationKind.Hand, 4));
            Assert.That(r.Succeeded, Is.True, r.FailureReason.ToString());
            Assert.That(r.MergeStageCount, Is.EqualTo(2));
            Assert.That(r.MergeStages[0].SurvivingInstanceId, Is.EqualTo(boardOne.InstanceId));
            Assert.That(r.MergeStages[0].ConsumedInstanceIds, Is.EquivalentTo(new[] { handOne.InstanceId }));
            Assert.That(r.MergeStages[1].SurvivingInstanceId, Is.EqualTo(boardOne.InstanceId));
            CollectionAssert.AreEqual(new[] { handTwoStarA.InstanceId, handTwoStarB.InstanceId }, r.MergeStages[1].ConsumedInstanceIds);
            w.Adopt(r); AssertIntegrity(w);
            AssertBoard(w.Board, 4, boardOne.InstanceId);
            Assert.That(w.Ownership.TryGet(boardOne.InstanceId, out CharacterInstanceView final), Is.True);
            Assert.That(final.Star, Is.EqualTo(3));
            Assert.That(w.Ownership.TryGet(handTwoStarExtra.InstanceId, out CharacterInstanceView extra), Is.True);
            Assert.That(extra.Star, Is.EqualTo(2));
            Assert.That(HandId(w.Hand, 3), Is.EqualTo(handTwoStarExtra.InstanceId));
        }

        [Test]
        public void FirstStageSurvivorCanBeConsumedBySecondStageBoardPriorityCandidate()
        {
            CharacterDefinition hero = Definition("hero");
            CharacterInstance boardTwo = Instance("I1", hero, 2, 2);
            CharacterInstance handTwo = Instance("I2", hero, 2, 2);
            CharacterInstance firstStageSurvivor = Instance("I3", hero, 2, 1);
            CharacterInstance firstStageConsumed = Instance("I4", hero, 2, 1);
            string[] boardIds = EmptyBoardIds(); boardIds[0] = boardTwo.InstanceId;
            HandState hand = HandWith((0, handTwo), (1, firstStageSurvivor), (2, firstStageConsumed));
            World w = WorldWith(new[] { hero }, new[] { Offer("oh", "hero") }, 100, 713,
                new[] { boardTwo, handTwo, firstStageSurvivor, firstStageConsumed }, boardIds, hand, 1, 20);

            BoardShopTransactionResult r = Buy(w, "oh", new CharacterLocation(CharacterLocationKind.Board, 8));
            Assert.That(r.Succeeded, Is.True, r.FailureReason.ToString());
            Assert.That(r.MergeStages[0].SurvivingInstanceId, Is.EqualTo(firstStageSurvivor.InstanceId));
            Assert.That(r.MergeStages[1].SurvivingInstanceId, Is.EqualTo(boardTwo.InstanceId));
            Assert.That(r.MergeStages[1].ConsumedInstanceIds, Does.Contain(firstStageSurvivor.InstanceId));
            w.Adopt(r); AssertIntegrity(w);
            AssertBoard(w.Board, 8, boardTwo.InstanceId);
            Assert.That(w.Ownership.TryGet(boardTwo.InstanceId, out CharacterInstanceView star3), Is.True);
            Assert.That(star3.Star, Is.EqualTo(3));
            Assert.That(w.Ownership.Contains(firstStageSurvivor.InstanceId), Is.False);
        }

        [Test]
        public void IdenticalSeedAndActionSequenceReplayAllStateAndHistory_DifferentSeedChangesRandomCheckpoint()
        {
            Replay first = RunReplay(8123);
            Replay repeat = RunReplay(8123);
            Replay differentSeed = RunReplay(8124);

            Assert.That(first.Signature, Is.EqualTo(repeat.Signature));
            Assert.That(first.AfterMergeRandom.State, Is.EqualTo(repeat.AfterMergeRandom.State));
            Assert.That(first.AfterMergeRandom.Increment, Is.EqualTo(repeat.AfterMergeRandom.Increment));
            Assert.That(first.FinalRandom.State, Is.EqualTo(repeat.FinalRandom.State));
            Assert.That(first.FinalRandom.Increment, Is.EqualTo(repeat.FinalRandom.Increment));
            Assert.That(first.AfterMergeRandom.State, Is.Not.EqualTo(differentSeed.AfterMergeRandom.State));
            Assert.That(first.ActionHistory, Is.EqualTo(repeat.ActionHistory));
        }

        private static Replay RunReplay(ulong seed)
        {
            CharacterDefinition hero = Definition("hero");
            CharacterInstance h1 = Instance("I1", hero, 2, 2);
            CharacterInstance h2 = Instance("I2", hero, 2, 2);
            CharacterInstance h3 = Instance("I3", hero, 2, 1);
            CharacterInstance h4 = Instance("I4", hero, 2, 1);
            string[] board = EmptyBoardIds(); board[0] = h1.InstanceId;
            World w = WorldWith(new[] { hero }, new[] { Offer("oh", "hero") }, 120, seed,
                new[] { h1, h2, h3, h4 }, board, HandWith((0, h2), (1, h3), (2, h4)), 1, 20);
            var history = new List<string>();

            BoardShopTransactionResult purchase = Buy(w, "oh", new CharacterLocation(CharacterLocationKind.Hand, 3));
            history.Add(Describe(purchase));
            Assert.That(purchase.Succeeded, Is.True);
            w.Adopt(purchase); AssertIntegrity(w);
            Pcg32State afterMerge = w.RandomState;

            BoardShopTransactionResult sale = Sell(w, purchase.FinalSurvivingInstanceId);
            history.Add(Describe(sale));
            Assert.That(sale.Succeeded, Is.True);
            w.Adopt(sale); AssertIntegrity(w);
            return new Replay(Serialize(w), afterMerge, w.RandomState, string.Join("|", history));
        }

        private static string Describe(BoardShopTransactionResult result)
        {
            var builder = new StringBuilder();
            builder.Append(result.Succeeded).Append(':').Append(result.FailureReason).Append(':')
                .Append(result.TransactionKind).Append(':').Append(result.FinalSurvivingInstanceId);
            foreach (CharacterMergeStageSummary stage in result.MergeStages)
            {
                builder.Append("/M").Append(stage.StarBefore).Append('>').Append(stage.StarAfter).Append(':')
                    .Append(stage.SurvivingInstanceId).Append(':').Append(stage.ConsumedPurchasedCopy);
                foreach (string consumed in stage.ConsumedInstanceIds) builder.Append(',').Append(consumed);
            }
            return builder.ToString();
        }

        private static string Serialize(World w)
        {
            var b = new StringBuilder();
            b.Append("gold=").Append(w.Economy.Gold).Append(";factory=").Append(w.Factory.LastIssuedNumber)
                .Append(";rng=").Append(w.RandomState.AlgorithmVersion).Append(',').Append(w.RandomState.State)
                .Append(',').Append(w.RandomState.Increment).Append(";board=");
            foreach (string id in w.Board.InstanceIds) b.Append(id ?? "-").Append(',');
            b.Append(";hand=");
            for (int i = 0; i < w.Hand.SlotCount; i++)
            {
                w.Hand.TryGetSlot(i, out HandSlotContent content);
                b.Append(content.Kind).Append(':').Append(content.CharacterInstanceId ?? "-").Append(':')
                    .Append(content.ItemDefinitionId ?? "-").Append(',');
            }
            b.Append(";shop=");
            foreach (ShopOffer offer in w.Shop.Offers)
                b.Append(offer.OfferId).Append(':').Append(offer.DefinitionId).Append(':').Append(offer.Status).Append(',');
            b.Append(";pool=");
            foreach (CharacterPoolEntry entry in w.Pool.Entries.Values.OrderBy(e => e.DefinitionId, StringComparer.Ordinal))
                b.Append(entry.DefinitionId).Append(':').Append(entry.RemainingCopies).Append(':')
                    .Append(entry.ReservedCopies).Append(':').Append(entry.OwnedCopies).Append(',');
            b.Append(";owned=");
            foreach (CharacterInstanceView instance in w.Ownership.GetOwnedInstances().OrderBy(i => i.InstanceId, StringComparer.Ordinal))
                b.Append(instance.InstanceId).Append(':').Append(instance.DefinitionId).Append(':').Append(instance.RunCost)
                    .Append(':').Append(instance.Star).Append(':').Append(instance.Attack).Append(':').Append(instance.Defense).Append(',');
            return b.ToString();
        }

        private static World EmptyWorld(CharacterDefinition[] definitions, ShopOffer[] offers, int gold, ulong seed)
        {
            var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ShopOffer offer in offers) reserved[offer.DefinitionId] = reserved.TryGetValue(offer.DefinitionId, out int n) ? n + 1 : 1;
            return WorldWith(definitions, offers, gold, seed, Array.Empty<CharacterInstance>(), EmptyBoardIds(),
                HandState.CreateStandard(), reserved, 30);
        }

        private static World WorldWith(CharacterDefinition[] definitions, ShopOffer[] offers, int gold, ulong seed,
            IReadOnlyList<CharacterInstance> owned, string[] boardIds, HandState hand, int reservedPerOffer, int stockPerDefinition)
        {
            var reservations = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ShopOffer offer in offers)
                reservations[offer.DefinitionId] = reservations.TryGetValue(offer.DefinitionId, out int n) ? n + 1 : 1;
            return WorldWith(definitions, offers, gold, seed, owned, boardIds, hand, reservations, stockPerDefinition);
        }

        private static World WorldWith(CharacterDefinition[] definitions, ShopOffer[] offers, int gold, ulong seed,
            IReadOnlyList<CharacterInstance> owned, string[] boardIds, HandState hand,
            Dictionary<string, int> reservations, int stockPerDefinition)
        {
            var poolEntries = new List<CharacterPoolEntry>();
            var costPairs = new List<KeyValuePair<string, int>>();
            foreach (CharacterDefinition definition in definitions)
            {
                int ownedCopies = 0;
                foreach (CharacterInstance instance in owned)
                    if (instance.DefinitionId == definition.DefinitionId) ownedCopies += instance.PoolCopies;
                reservations.TryGetValue(definition.DefinitionId, out int reserved);
                poolEntries.Add(new CharacterPoolEntry(definition.DefinitionId,
                    stockPerDefinition - ownedCopies - reserved, stockPerDefinition, reserved, ownedCopies));
                costPairs.Add(new KeyValuePair<string, int>(definition.DefinitionId, 2));
            }
            var stats = Stats();
            return new World
            {
                Definitions = definitions,
                Costs = new AssignedCharacterCosts(costPairs),
                Stats = stats,
                Shop = new ShopState(ShopKind.Character, offers, offers.Length, 1, 1),
                Pool = new CharacterPool(poolEntries),
                Economy = new EconomyState(gold, 0, 1, 12, gold, 0, 1),
                Board = new BoardState(3, 3, boardIds),
                Hand = hand,
                Ownership = new CharacterOwnershipState(owned),
                Factory = new CharacterInstanceFactory("N"),
                RandomState = new Pcg32RandomSource(seed, 771).CaptureState()
            };
        }

        private static BoardShopTransactionResult Buy(World w, string offerId, CharacterLocation destination)
        {
            return BoardShopTransactionRules.TryPurchaseCharacter(w.Shop, offerId, w.Definitions, w.Costs,
                w.Economy, w.Pool, w.Board, w.Hand, w.Ownership, w.Factory, w.Stats,
                w.RandomState, destination, ownershipScopeMatchesPool: true);
        }

        private static BoardShopTransactionResult Sell(World w, string instanceId)
        {
            return BoardShopTransactionRules.TrySellCharacter(instanceId, w.Economy, w.Shop, w.Pool,
                w.Board, w.Hand, w.Ownership, w.Factory, w.RandomState, ownershipScopeMatchesPool: true);
        }

        private static CharacterDefinition Definition(string id) => new CharacterDefinition(id, id, 2, null, null);
        private static ShopOffer Offer(string offerId, string definitionId) =>
            new ShopOffer(offerId, ShopOfferKind.Character, definitionId, 2, 2);

        private static CharacterInstance Instance(string id, CharacterDefinition definition, int cost, int star)
        {
            var instance = new CharacterInstance(id, definition, cost, star);
            Assert.That(instance.TrySetStar(star, definition, Stats(), new Pcg32RandomSource((ulong)(id[1] + star), 102)), Is.True);
            return instance;
        }

        private static CharacterStatDefinition Stats()
        {
            var options = new[]
            {
                new CharacterStatOption(10, 4, 1),
                new CharacterStatOption(14, 6, 1),
                new CharacterStatOption(18, 8, 1)
            };
            return new CharacterStatDefinition(new[] { new CharacterCostStats(2, options, options, options) });
        }

        private static string[] EmptyBoardIds() => new string[9];

        private static HandState HandWith(params (int slot, CharacterInstance instance)[] characters)
        {
            var slots = new HandSlotContent[HandState.StandardSlotCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = HandSlotContent.Empty();
            foreach (var pair in characters) slots[pair.slot] = HandSlotContent.Character(pair.instance.InstanceId);
            return new HandState(slots);
        }

        private static string HandId(HandState hand, int slot)
        {
            hand.TryGetSlot(slot, out HandSlotContent content);
            return content.CharacterInstanceId;
        }

        private static int Copies(int star) => star == 1 ? 1 : star == 2 ? 3 : 9;

        private static int OwnedCount(CharacterOwnershipState ownership, string definitionId) =>
            ownership.GetOwnedInstances().Count(i => i.DefinitionId == definitionId);

        private static CharacterPoolEntry Entry(CharacterPool pool, string definitionId) => pool.Entries[definitionId];

        private static void AssertBoard(BoardState board, int slot, string id)
        {
            Assert.That(board.TryGetCharacterIdAt(slot, out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(id));
        }

        private static void AssertIntegrity(World w)
        {
            BoardHandStateValidationResult validation = BoardHandStateValidator.Validate(w.Board, w.Hand, w.Ownership);
            Assert.That(validation.IsValid, Is.True, string.Join(" | ", validation.Errors));
            foreach (CharacterPoolEntry entry in w.Pool.Entries.Values)
            {
                Assert.That(entry.RemainingCopies + entry.ReservedCopies + entry.OwnedCopies,
                    Is.EqualTo(entry.InitialStockLimit), "Pool conservation for " + entry.DefinitionId);
                int ownedCopies = 0;
                foreach (CharacterInstanceView instance in w.Ownership.GetOwnedInstances())
                    if (instance.DefinitionId == entry.DefinitionId) ownedCopies += instance.PoolCopies;
                Assert.That(entry.OwnedCopies, Is.EqualTo(ownedCopies), "Ownership copy count for " + entry.DefinitionId);
            }
        }

        private static void AssertFailureAtomic(BoardShopTransactionResult r, World w,
            BoardShopTransactionFailureReason reason, Pcg32State randomBefore)
        {
            Assert.That(r.Succeeded, Is.False);
            Assert.That(r.FailureReason, Is.EqualTo(reason));
            Assert.That(r.NextEconomy, Is.SameAs(w.Economy));
            Assert.That(r.NextShop, Is.SameAs(w.Shop));
            Assert.That(r.NextPool, Is.SameAs(w.Pool));
            Assert.That(r.NextBoard, Is.SameAs(w.Board));
            Assert.That(r.NextHand, Is.SameAs(w.Hand));
            Assert.That(r.NextOwnership, Is.SameAs(w.Ownership));
            Assert.That(r.NextInstanceFactory, Is.SameAs(w.Factory));
            Assert.That(r.NextStatsRandomState.State, Is.EqualTo(randomBefore.State));
            Assert.That(r.NextStatsRandomState.Increment, Is.EqualTo(randomBefore.Increment));
        }

        private static void AssertIntegrity(BoardState board, HandState hand, CharacterOwnershipState ownership)
        {
            BoardHandStateValidationResult result = BoardHandStateValidator.Validate(board, hand, ownership);
            Assert.That(result.IsValid, Is.True, string.Join(" | ", result.Errors));
        }

        private sealed class World
        {
            public CharacterDefinition[] Definitions;
            public AssignedCharacterCosts Costs;
            public CharacterStatDefinition Stats;
            public ShopState Shop;
            public CharacterPool Pool;
            public EconomyState Economy;
            public BoardState Board;
            public HandState Hand;
            public CharacterOwnershipState Ownership;
            public CharacterInstanceFactory Factory;
            public Pcg32State RandomState;

            // Test-only state adoption; production still has no GameSession here.
            public void Adopt(BoardShopTransactionResult r)
            {
                Economy = r.NextEconomy; Shop = r.NextShop; Pool = r.NextPool;
                Board = r.NextBoard; Hand = r.NextHand; Ownership = r.NextOwnership;
                Factory = r.NextInstanceFactory; RandomState = r.NextStatsRandomState;
            }

            public void Adopt(BoardHandOperationResult r)
            {
                Board = r.NextBoard; Hand = r.NextHand;
            }
        }

        private sealed class Replay
        {
            public string Signature { get; }
            public Pcg32State AfterMergeRandom { get; }
            public Pcg32State FinalRandom { get; }
            public string ActionHistory { get; }
            public Replay(string signature, Pcg32State afterMergeRandom, Pcg32State finalRandom, string actionHistory)
            { Signature = signature; AfterMergeRandom = afterMergeRandom; FinalRandom = finalRandom; ActionHistory = actionHistory; }
        }
    }
}
