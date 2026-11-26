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
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BoardShopTransactionRulesTests
    {
        [TestCase(0, "00000", CharacterLocationKind.Board, 8, 0)]
        [TestCase(0, "00000", CharacterLocationKind.Hand, 2, 0)]
        [TestCase(0, "22000", CharacterLocationKind.Hand, 2, 0)]
        [TestCase(0, "11000", CharacterLocationKind.Hand, 4, 1)]
        [TestCase(0, "11111", CharacterLocationKind.Hand, 4, 1)]
        [TestCase(0, "11000", CharacterLocationKind.Board, 8, 1)]
        [TestCase(1, "11000", CharacterLocationKind.Hand, 4, 1)]
        [TestCase(1, "11000", CharacterLocationKind.Board, 8, 1)]
        [TestCase(1, "11000", CharacterLocationKind.Board, 0, 1)]
        [TestCase(0, "21120", CharacterLocationKind.Hand, 4, 2)]
        [TestCase(2, "21100", CharacterLocationKind.Board, 8, 2)]
        [TestCase(0, "22211", CharacterLocationKind.Hand, 4, 2)]
        [TestCase(3, "21120", CharacterLocationKind.Hand, 0, 2)]
        public void PurchasePreviewMatchesRealTransaction(int boardStar, string handStars,
            CharacterLocationKind kind, int slot, int stages)
        {
            var board = new int[9];
            board[0] = boardStar;
            World world = MakeWorld(board, handStars.Select(c => c - '0').ToArray(), 0, 1);
            var observation = ObservePurchase(world);
            var context = new CharacterPurchasePreview(observation);
            var action = PreviewAction(kind, slot);
            var before = world.StatsState;
            var preview = context.Preview(action);
            var repeated = context.Preview(action);
            Assert.That(preview.MergeStageCount, Is.EqualTo(stages));
            Assert.That(preview.After.BoardCount, Is.GreaterThanOrEqualTo(preview.Before.BoardCount));
            Assert.That(repeated.SlotChanges.Select(x => (x.Location.Kind, x.Location.SlotIndex, x.CharacterAfter?.Star)),
                Is.EqualTo(preview.SlotChanges.Select(x => (x.Location.Kind, x.Location.SlotIndex, x.CharacterAfter?.Star))));
            Assert.That(repeated.FinalCharacter.InstanceId, Is.EqualTo(preview.FinalCharacter.InstanceId));
            Assert.That(world.StatsState, Is.EqualTo(before));
            Assert.That(world.Factory.LastIssuedNumber, Is.Zero);
            Assert.That(observation.Board.Slots.Select(x => x.Character?.InstanceId), Is.EqualTo(world.BoardIds));
            Assert.That(observation.Hand.Slots.Select(x => x.Character?.InstanceId), Is.EqualTo(world.HandIds));
            var rolls = stages == 0 ? new[] { preview.FinalCharacter } : preview.MergeStages.Select(s => s.Survivor).ToArray();
            Assert.That(rolls.Select(roll => observation.CharacterStats.Get(roll).Star),
                Is.EqualTo(stages == 0 ? new[] { 1 } : Enumerable.Range(2, stages).ToArray()));
            Assert.That(rolls.All(roll => observation.CharacterStats.Get(roll).IsAvailable), Is.True);
            AssertPreviewParity(world, preview, action.Placement.ToCharacterLocation());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PurchasePreviewDistinguishesFullHandTransactionsFromCandidates(bool fullBoard)
        {
            World world = MakeWorld(new int[9], new[] { 1, 1, 0, 0, 0 }, 0, 1);
            var item = new ItemDefinition("item", "Item", 0, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 1) });
            for (int i = 2; i < 5; i++)
                Assert.That(world.Hand.TryWithSlot(i, HandSlotContent.Item(item.DefinitionId), out world.Hand), Is.True);
            AddOtherBoardCharacters(world, fullBoard ? 9 : 1);
            var observation = ObservePurchase(world, item);
            var candidates = new RandomStrategy().GetCandidates(observation, new ActionId("preview-candidates"))
                .OfType<BuyCharacterAction>().ToArray();
            Assert.That(candidates.Any(c => c.Placement.Kind == CharacterLocationKind.Hand), Is.False);
            Assert.That(candidates.Length, Is.EqualTo(fullBoard ? 0 : 8));

            var context = new CharacterPurchasePreview(observation);
            var handAction = PreviewAction(CharacterLocationKind.Hand, 4);
            var preview = context.Preview(handAction);
            Assert.That(preview.Before.HandOccupied, Is.EqualTo(5));
            Assert.That(preview.After.HandOccupied, Is.EqualTo(4));
            Assert.That(preview.FinalLocation.Value.SlotIndex, Is.Zero);
            AssertPreviewParity(world, preview, handAction.Placement.ToCharacterLocation());
            if (!fullBoard)
            {
                var boardAction = candidates[0];
                var boardPreview = context.Preview(boardAction);
                Assert.That(boardPreview.After.HandOccupied, Is.EqualTo(3));
                Assert.That(boardPreview.Before.OverLevelCount, Is.Zero);
                Assert.That(boardPreview.After.OverLevelCount, Is.EqualTo(1));
                Assert.That(boardPreview.After.EmptyBoardSlots, Is.EqualTo(7));
                AssertPreviewParity(world, boardPreview, boardAction.Placement.ToCharacterLocation());
            }
        }

        [TestCase(2, "00000", CharacterLocationKind.Board, 1, CharacterPurchasePreviewFailure.DuplicateBoardDefinition)]
        [TestCase(3, "11000", CharacterLocationKind.Board, 1, CharacterPurchasePreviewFailure.DuplicateBoardDefinition)]
        [TestCase(1, "00000", CharacterLocationKind.Board, 0, CharacterPurchasePreviewFailure.SlotOccupied)]
        [TestCase(0, "20000", CharacterLocationKind.Hand, 0, CharacterPurchasePreviewFailure.SlotOccupied)]
        [TestCase(0, "00000", CharacterLocationKind.Hand, 5, CharacterPurchasePreviewFailure.InvalidSlot)]
        public void PurchasePreviewReportsVisiblePlacementFailures(int boardStar, string handStars,
            CharacterLocationKind kind, int slot, CharacterPurchasePreviewFailure failure)
        {
            var board = new int[9];
            board[0] = boardStar;
            World world = MakeWorld(board, handStars.Select(c => c - '0').ToArray(), 0, 1);
            var action = PreviewAction(kind, slot);
            var preview = new CharacterPurchasePreview(ObservePurchase(world)).Preview(action);
            Assert.That(preview.Failure, Is.EqualTo(failure));
            Assert.That(preview.FinalLocation, Is.Null);
            Assert.That(preview.SlotChanges, Is.Empty);
            Assert.That(preview.Before.BoardCount, Is.EqualTo(preview.After.BoardCount));
            Assert.That(Buy(world, action.Placement.ToCharacterLocation()).Succeeded, Is.False);
        }

        [Test]
        public void PurchasePreviewReportsAffordabilityOfferStateAndItemOccupancy()
        {
            World poor = MakeWorld(new int[9], new int[5], 0, 1, gold: 1);
            var action = PreviewAction(CharacterLocationKind.Hand, 0);
            Assert.That(new CharacterPurchasePreview(ObservePurchase(poor)).Preview(action).Failure,
                Is.EqualTo(CharacterPurchasePreviewFailure.InsufficientGold));
            World purchased = poor.WithShop(ShopOfferStatus.Purchased);
            Assert.That(new CharacterPurchasePreview(ObservePurchase(purchased)).Preview(action).Failure,
                Is.EqualTo(CharacterPurchasePreviewFailure.OfferUnavailable));
            World world = MakeWorld(new int[9], new int[5], 0, 1);
            var item = new ItemDefinition("item", "Item", 0, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 1) });
            Assert.That(world.Hand.TryWithSlot(0, HandSlotContent.Item(item.DefinitionId), out world.Hand), Is.True);
            Assert.That(new CharacterPurchasePreview(ObservePurchase(world, item)).Preview(action).Failure,
                Is.EqualTo(CharacterPurchasePreviewFailure.SlotOccupied));
            Assert.That(Buy(world, action.Placement.ToCharacterLocation()).Succeeded, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PurchasePreviewRecordsBothStatRollCostsWhenChainChangesSurvivor(bool temporaryStar)
        {
            World world = MakeWorld(new[] { 2,0,0,0,0,0,0,0,0 }, new[] { 2,1,1,0,0 }, 0, 1);
            var options = new[] { new CharacterStatOption(4, 3, 1) };
            world.Stats = new CharacterStatDefinition(new[] {
                new CharacterCostStats(1, options, new[] { new CharacterStatOption(13, 6, 1) }, options),
                new CharacterCostStats(2, options, options, options)
            });
            string firstSurvivor = world.HandIds[1];
            var differentCost = new CharacterInstance(firstSurvivor, world.Definitions[0], 1);
            differentCost.TemporaryAttackBonus = 9;
            Assert.That(world.Ownership.TryWithRemoved(firstSurvivor, out world.Ownership), Is.True);
            Assert.That(world.Ownership.TryWithAdded(differentCost, out world.Ownership), Is.True);

            string finalSurvivor = world.BoardIds[0];
            var original = new CharacterInstance(finalSurvivor, world.Definitions[0], 2);
            original.TemporaryAttackBonus = 2;
            original.TemporaryDefenseBonus = 3;
            Assert.That(world.Ownership.TryWithReplaced(original, out world.Ownership), Is.True);
            world.Pool = world.WithPoolOwnedCopies(CopiesInOwnership(world.Ownership)).Pool;
            var stage = PurchasePreparation(out _);
            var starItem = new ItemDefinition("star", "Star", 0, "", ItemEffectTarget.OneAlly,
                temporaryStar ? ItemEffectDuration.ThisBattle : ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Star, 2) });
            Assert.That(world.Hand.TryWithSlot(4, HandSlotContent.Item(starItem.DefinitionId), out world.Hand), Is.True);
            var raised = ItemEffectRules.TryUseHandItem(starItem, 4, ItemUseTarget.BoardCharacter(0),
                world.Board, world.Hand, world.Ownership, world.Economy, stage, new ItemEffectState(),
                world.Definitions, world.Stats, world.StatsState);
            Assert.That(raised.Succeeded, Is.True, raised.Failure.ToString());
            world.Ownership = raised.NextOwnership;
            world.Hand = raised.NextHand;
            var attackItem = new ItemDefinition("permanent", "Permanent", 0, "", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 30) });
            // Apply the permanent stat boost before the purchase, using the real Item rules.
            Assert.That(world.Hand.TryWithSlot(4, HandSlotContent.Item(attackItem.DefinitionId), out world.Hand), Is.True);
            var itemUse = ItemEffectRules.TryUseHandItem(attackItem, 4, ItemUseTarget.BoardCharacter(0),
                world.Board, world.Hand, world.Ownership, world.Economy, stage, raised.NextEffectState,
                world.Definitions, world.Stats, raised.NextStatsRandomState);
            Assert.That(itemUse.Succeeded, Is.True, itemUse.Failure.ToString());
            world.Ownership = itemUse.NextOwnership;
            world.Hand = itemUse.NextHand;

            var observation = ObservePurchase(world);
            var action = PreviewAction(CharacterLocationKind.Board, 8);
            var preview = new CharacterPurchasePreview(observation).Preview(action);
            Assert.That(observation.Board.Slots[0].Character.Attack, Is.EqualTo(34));
            Assert.That(preview.MergeStageCount, Is.EqualTo(2));
            Assert.That(preview.MergeStages[0].Survivor.InstanceId, Is.EqualTo(firstSurvivor));
            Assert.That(preview.MergeStages[0].Survivor.RunCost, Is.EqualTo(1));
            Assert.That(preview.MergeStages[0].Survivor.Star, Is.EqualTo(2));
            Assert.That(preview.MergeStages[0].Survivor.TemporaryAttackBonus, Is.EqualTo(9));
            Assert.That(preview.MergeStages[1].ConsumedInstanceIds, Does.Contain(firstSurvivor));
            Assert.That(preview.FinalCharacter.InstanceId, Is.EqualTo(finalSurvivor));
            Assert.That(preview.FinalCharacter.RunCost, Is.EqualTo(2));
            Assert.That(preview.FinalCharacter.TemporaryAttackBonus, Is.EqualTo(2));
            Assert.That(preview.FinalCharacter.TemporaryDefenseBonus, Is.EqualTo(3));
            Assert.That(observation.CharacterStats.Get(preview.MergeStages[0].Survivor).ExpectedAttack, Is.EqualTo(13));
            Assert.That(observation.CharacterStats.Get(preview.FinalCharacter).ExpectedAttack, Is.EqualTo(4));
            if (temporaryStar)
            {
                Assert.That(preview.Succeeded, Is.True);
                Assert.That(Buy(world, action.Placement.ToCharacterLocation()).FailureReason,
                    Is.EqualTo(BoardShopTransactionFailureReason.PoolOwnershipMismatch));
                return;
            }
            var actual = AssertPreviewParity(world, preview, action.Placement.ToCharacterLocation());
            Assert.That(actual.NextOwnership.TryGet(finalSurvivor, out var survivor), Is.True);
            Assert.That(survivor.Attack, Is.EqualTo(4));
        }

        private static BuyCharacterAction PreviewAction(CharacterLocationKind kind, int slot) =>
            new BuyCharacterAction(new ActionId("preview"), "offer", new GameActionLocation(kind, slot));

        private static StageProgressState PurchasePreparation(out StageProgressConfig config)
        {
            config = new StageProgressConfig(25,5,5,4,0,0,0,1,0,0,3,6,0,0,10,4,2,true);
            var stage = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(config), config).NextState;
            return stage.Phase == StageProgressPhase.AwaitingStageChoice
                ? StageSelectionRules.Select(stage, config, StageChoice.SelectA).NextState : stage;
        }

        private static GameObservation ObservePurchase(World world, params ItemDefinition[] items)
        {
            var stage = PurchasePreparation(out var config);
            var economy = new EconomyRulesConfiguration(4,2,1,
                new[]{1,2,3,4,5,6,7,8,9,10,11},10,1,3,3,1,0);
            var result = ObservationBuilder.Build(new ObservationSource(stage, config, world.Economy, economy,
                world.Board, world.Hand, world.Ownership, world.Definitions, world.Definitions, world.Definitions,
                items, world.Shop, new ItemEffectState(), null, Array.Empty<EnemyDefinition>(),
                EnemyBoardState.CreateEmpty(), characterStatSettings: world.Stats));
            Assert.That(result.Succeeded, Is.True, string.Join(";", result.Errors));
            return result.Observation;
        }

        private static void AddOtherBoardCharacters(World world, int count)
        {
            var definitions = world.Definitions.ToList();
            var pool = world.Pool.Entries.Values.ToList();
            for (int i = 0; i < count; i++)
            {
                var other = new CharacterDefinition("other-" + i, "Hero", 2, null, null);
                var instance = new CharacterInstance("other-instance-" + i, other, 2);
                definitions.Add(other);
                pool.Add(new CharacterPoolEntry(other.DefinitionId, 9, 10, 0, 1));
                Assert.That(world.Board.TryWithCharacterAt(i, instance.InstanceId, out world.Board), Is.True);
                Assert.That(world.Ownership.TryWithAdded(instance, out world.Ownership), Is.True);
            }
            world.Definitions = definitions;
            world.Pool = new CharacterPool(pool);
        }

        private static BoardShopTransactionResult AssertPreviewParity(World world, CharacterPurchasePreviewResult preview,
            CharacterLocation destination)
        {
            Assert.That(preview.Succeeded, Is.True, preview.Failure.ToString());
            BoardShopTransactionResult actual = Buy(world, destination);
            Assert.That(actual.Succeeded, Is.True, actual.FailureReason.ToString());
            Assert.That(preview.MergeStageCount, Is.EqualTo(actual.MergeStageCount));
            for (int i = 0; i < preview.MergeStageCount; i++)
            {
                Assert.That(preview.MergeStages[i].Survivor.InstanceId, Is.EqualTo(actual.MergeStages[i].SurvivingInstanceId));
                Assert.That(preview.MergeStages[i].Survivor.Star, Is.EqualTo(actual.MergeStages[i].StarAfter));
                Assert.That(preview.MergeStages[i].ConsumedInstanceIds, Is.EqualTo(actual.MergeStages[i].ConsumedInstanceIds));
                Assert.That(preview.MergeStages[i].ConsumesPurchase, Is.EqualTo(actual.MergeStages[i].ConsumedPurchasedCopy));
            }
            string finalId = actual.FinalSurvivingInstanceId;
            Assert.That(actual.NextOwnership.TryGet(finalId, out var survivor), Is.True);
            Assert.That(preview.FinalCharacter.Star, Is.EqualTo(survivor.Star));
            Assert.That(preview.FinalCharacter.RunCost, Is.EqualTo(survivor.RunCost));
            Assert.That(preview.FinalCharacter.TemporaryAttackBonus, Is.EqualTo(survivor.TemporaryAttackBonus));
            Assert.That(preview.FinalCharacter.TemporaryDefenseBonus, Is.EqualTo(survivor.TemporaryDefenseBonus));
            if (preview.FinalCharacter.IsNewCharacter) Assert.That(preview.FinalCharacter.InstanceId, Is.Null);
            else Assert.That(preview.FinalCharacter.InstanceId, Is.EqualTo(finalId));
            foreach (var kind in new[] { CharacterLocationKind.Board, CharacterLocationKind.Hand })
            {
                int size = kind == CharacterLocationKind.Board ? world.Board.SlotCount : world.Hand.SlotCount;
                for (int i = 0; i < size; i++)
                {
                    var changes = preview.SlotChanges.Where(c => c.Location.Kind == kind && c.Location.SlotIndex == i).ToArray();
                    string before = Occupant(world.Board, world.Hand, kind, i);
                    PurchaseStatRoll after = changes.Length == 0 ? null : changes.Single().CharacterAfter;
                    string expected = changes.Length == 0 ? before : after == null ? null : after.InstanceId ?? finalId;
                    Assert.That(Occupant(actual.NextBoard, actual.NextHand, kind, i), Is.EqualTo(expected), $"{kind} {i}");
                    if (changes.Length > 0) Assert.That(changes[0].PreviousInstanceId, Is.EqualTo(before));
                    if (after != null)
                    {
                        Assert.That(actual.NextOwnership.TryGet(expected, out var occupant), Is.True);
                        Assert.That(occupant.Star, Is.EqualTo(after.Star), $"{kind} {i}");
                    }
                }
            }
            var final = preview.FinalLocation.Value;
            Assert.That(Occupant(actual.NextBoard, actual.NextHand, final.Kind, final.SlotIndex), Is.EqualTo(finalId));
            Assert.That(preview.After.BoardCount, Is.EqualTo(actual.NextBoard.InstanceIds.Count(id => id != null)));
            Assert.That(preview.After.HandOccupied, Is.EqualTo(actual.NextHand.SlotCount - actual.NextHand.EmptySlotCount));
            var stars = actual.NextOwnership.GetOwnedInstances().Where(c => c.DefinitionId == preview.DefinitionId).ToArray();
            Assert.That(preview.StarsAfter.OneStar, Is.EqualTo(stars.Count(c => c.Star == 1)));
            Assert.That(preview.StarsAfter.TwoStar, Is.EqualTo(stars.Count(c => c.Star == 2)));
            Assert.That(preview.StarsAfter.ThreeStar, Is.EqualTo(stars.Count(c => c.Star == 3)));
            return actual;
        }

        private static string Occupant(BoardState board, HandState hand, CharacterLocationKind kind, int slot)
        {
            if (kind == CharacterLocationKind.Board) return board.InstanceIds[slot];
            var content = hand.Slots[slot];
            return content.Kind == HandSlotContentKind.Item ? "item:" + content.ItemDefinitionId : content.CharacterInstanceId;
        }

        [Test]
        public void NormalPurchaseIntoHandCreatesOwnedAndPlacedInstanceAndConsumesReservation()
        {
            World w = MakeWorld(new int[9], new[] { 0, 0, 0, 0, 0 }, 0, 1);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 2));
            AssertSuccess(r, w);
            Assert.That(r.TransactionKind, Is.EqualTo(BoardShopTransactionKind.NormalPurchase));
            Assert.That(r.NextEconomy.Gold, Is.EqualTo(98));
            Assert.That(r.NextInstanceFactory.LastIssuedNumber, Is.EqualTo(1));
            Assert.That(r.NextStatsRandomState.State, Is.Not.EqualTo(w.StatsState.State));
            Assert.That(r.NextOwnership.OwnedCount, Is.EqualTo(1));
            Assert.That(r.NextHand.TryGetSlot(2, out HandSlotContent content), Is.True);
            Assert.That(content.CharacterInstanceId, Is.EqualTo("T1"));
            AssertPool(r.NextPool, owned: 1, reserved: 0);
        }

        [Test]
        public void NormalPurchaseIntoBoardPlacesAtRequestedSlot()
        {
            World w = MakeWorld(new int[9], new[] { 0, 0, 0, 0, 0 }, 0, 1);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Board, 8));
            AssertSuccess(r, w);
            Assert.That(r.NextBoard.TryGetCharacterIdAt(8, out string id), Is.True);
            Assert.That(id, Is.EqualTo("T1"));
        }

        [Test]
        public void NormalPurchaseRejectsBoardDuplicateAndFullHandWithoutAdvancingFactoryOrStats()
        {
            World boardFull = MakeWorld(new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 }, new int[5], 0, 1);
            BoardShopTransactionResult duplicate = Buy(boardFull, new CharacterLocation(CharacterLocationKind.Board, 1));
            AssertFailureUnchanged(duplicate, boardFull, BoardShopTransactionFailureReason.DuplicateDefinitionOnBoard);

            World fullHand = MakeWorld(new int[9], new[] { 2, 2, 2, 2, 2 }, 0, 1);
            BoardShopTransactionResult full = Buy(fullHand, new CharacterLocation(CharacterLocationKind.Hand, 0));
            AssertFailureUnchanged(full, fullHand, BoardShopTransactionFailureReason.PlacementOccupied);
        }

        [Test]
        public void PurchaseRejectsInsufficientGoldPurchasedOfferAndInvalidSlotAtomically()
        {
            World w = MakeWorld(new int[9], new int[5], 0, 1, gold: 1);
            AssertFailureUnchanged(Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 0)), w,
                BoardShopTransactionFailureReason.InsufficientGold);

            World purchased = w.WithShop(ShopOfferStatus.Purchased);
            AssertFailureUnchanged(Buy(purchased, new CharacterLocation(CharacterLocationKind.Hand, 0)), purchased,
                BoardShopTransactionFailureReason.InvalidOffer);

            World rich = MakeWorld(new int[9], new int[5], 0, 1);
            AssertFailureUnchanged(Buy(rich, new CharacterLocation(CharacterLocationKind.Hand, 99)), rich,
                BoardShopTransactionFailureReason.InvalidPlacement);
        }

        [Test]
        public void PoolOwnershipCheckRunsOnlyWhenCallerConfirmsMatchingScopes()
        {
            World w = MakeWorld(new int[9], new[] { 1, 0, 0, 0, 0 }, 0, 1).WithPoolOwnedCopies(0);
            BoardShopTransactionResult unverifiable = BoardShopTransactionRules.TryPurchaseCharacter(w.Shop,
                "offer", w.Definitions, w.Costs, w.Economy, w.Pool, w.Board, w.Hand, w.Ownership,
                w.Factory, w.Stats, w.StatsState,
                new CharacterLocation(CharacterLocationKind.Hand, 2), ownershipScopeMatchesPool: false);
            Assert.That(unverifiable.Succeeded, Is.True);
            Assert.That(unverifiable.PoolOwnershipConsistencyVerified, Is.False);

            BoardShopTransactionResult mismatch = BoardShopTransactionRules.TryPurchaseCharacter(w.Shop,
                "offer", w.Definitions, w.Costs, w.Economy, w.Pool, w.Board, w.Hand, w.Ownership,
                w.Factory, w.Stats, w.StatsState,
                new CharacterLocation(CharacterLocationKind.Hand, 2), ownershipScopeMatchesPool: true);
            AssertFailureUnchanged(mismatch, w, BoardShopTransactionFailureReason.PoolOwnershipMismatch);
        }

        [Test]
        public void OneStepMergeUsesBoardFirstAndKeepsSurplusOneStarCandidate()
        {
            World w = MakeWorld(new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, new[] { 1, 1, 0, 0, 0 }, 0, 1);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 4));
            AssertSuccess(r, w);
            Assert.That(r.TransactionKind, Is.EqualTo(BoardShopTransactionKind.MergePurchase));
            Assert.That(r.MergeStageCount, Is.EqualTo(1));
            Assert.That(r.MergeStages[0].SurvivingInstanceId, Is.EqualTo(w.BoardIds[0]));
            Assert.That(r.MergeStages[0].ConsumedInstanceIds, Is.EquivalentTo(new[] { w.HandIds[0] }));
            Assert.That(r.NextOwnership.TryGet(w.BoardIds[0], out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.Star, Is.EqualTo(2));
            Assert.That(r.NextOwnership.Contains(w.HandIds[1]), Is.True, "The third existing candidate stays owned.");
            Assert.That(r.NextInstanceFactory.LastIssuedNumber, Is.EqualTo(0));
            Assert.That(r.NextBoard.TryGetCharacterIdAt(0, out string moved), Is.True);
            Assert.That(moved, Is.EqualTo(w.BoardIds[0]));
            AssertPool(r.NextPool, owned: 4, reserved: 0);
        }

        [Test]
        public void OneStepMergeWorksWithOnlyHandCandidatesAndFullHand()
        {
            World w = MakeWorld(new int[9], new[] { 1, 1, 1, 1, 1 }, 0, 1);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 4));
            AssertSuccess(r, w);
            Assert.That(r.MergeStageCount, Is.EqualTo(1));
            Assert.That(r.NextOwnership.OwnedCount, Is.EqualTo(4));
            Assert.That(r.NextOwnership.TryGet(r.FinalSurvivingInstanceId, out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.Star, Is.EqualTo(2));
            Assert.That(r.NextInstanceFactory.LastIssuedNumber, Is.Zero);
        }

        [Test]
        public void OneStarPurchaseDoesNotDirectlyMergeTwoExistingTwoStars()
        {
            World w = MakeWorld(new int[9], new[] { 2, 2, 0, 0, 0 }, 0, 1);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 2));
            AssertSuccess(r, w);
            Assert.That(r.TransactionKind, Is.EqualTo(BoardShopTransactionKind.NormalPurchase));
            Assert.That(r.MergeStageCount, Is.Zero);
            Assert.That(r.NextInstanceFactory.LastIssuedNumber, Is.EqualTo(1));
            Assert.That(r.NextOwnership.OwnedCount, Is.EqualTo(3));
        }

        [Test]
        public void PurchaseCanChainToThreeStarsAndSecondStageMayConsumeFirstStageSurvivor()
        {
            // Existing ★2: Hand[0]. Existing ★1: Hand[1..2]. After step one, the new ★2 is Hand[1].
            // Stage two order is Hand[0], Hand[1], Hand[3]; Hand[0] survives and consumes the new ★2.
            World w = MakeWorld(new int[9], new[] { 2, 1, 1, 2, 0 }, 0, 1);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 4));
            AssertSuccess(r, w);
            Assert.That(r.MergeStageCount, Is.EqualTo(2));
            Assert.That(r.MergeStages[0].SurvivingInstanceId, Is.EqualTo(w.HandIds[1]));
            Assert.That(r.MergeStages[1].SurvivingInstanceId, Is.EqualTo(w.HandIds[0]));
            Assert.That(r.MergeStages[1].ConsumedInstanceIds, Does.Contain(w.HandIds[1]));
            Assert.That(r.FinalSurvivingInstanceId, Is.EqualTo(w.HandIds[0]));
            Assert.That(r.NextOwnership.OwnedCount, Is.EqualTo(1));
            Assert.That(r.NextOwnership.TryGet(w.HandIds[0], out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.Star, Is.EqualTo(3));
            Assert.That(CopiesInOwnership(r.NextOwnership), Is.EqualTo(9));
            Assert.That(r.NextInstanceFactory.LastIssuedNumber, Is.Zero);
            AssertPool(r.NextPool, owned: 9, reserved: 0);
            Assert.That(r.NextHand.TryGetSlot(0, out HandSlotContent finalSlot), Is.True);
            Assert.That(finalSlot.CharacterInstanceId, Is.EqualTo(w.HandIds[0]));
        }

        [Test]
        public void ChainCanUseDifferentSecondStageSurvivorAndMoveThatFinalSurvivorToBoard()
        {
            // Board ★2 precedes Hand ★2; first stage survives in Hand and is consumed by stage two.
            World w = MakeWorld(new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 }, new[] { 2, 1, 1, 0, 0 }, 0, 1);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Board, 8));
            AssertSuccess(r, w);
            Assert.That(r.MergeStageCount, Is.EqualTo(2));
            Assert.That(r.MergeStages[0].SurvivingInstanceId, Is.EqualTo(w.HandIds[1]));
            Assert.That(r.MergeStages[1].SurvivingInstanceId, Is.EqualTo(w.BoardIds[0]));
            Assert.That(r.MergeStages[1].ConsumedInstanceIds, Does.Contain(w.HandIds[1]));
            Assert.That(r.NextBoard.TryGetCharacterIdAt(8, out string finalId), Is.True);
            Assert.That(finalId, Is.EqualTo(w.BoardIds[0]));
            Assert.That(r.NextOwnership.OwnedCount, Is.EqualTo(1));
            Assert.That(r.NextOwnership.TryGet(finalId, out CharacterInstanceView final), Is.True);
            Assert.That(final.Star, Is.EqualTo(3));
        }

        [Test]
        public void MergeIntoBoardWithOccupiedRequestedSlotKeepsFinalSurvivorAtItsSource()
        {
            World w = MakeWorld(new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, new[] { 1, 0, 0, 0, 0 }, 0, 1);
            CharacterDefinition otherDefinition = new CharacterDefinition("other", "Other", 1, null, null);
            CharacterInstance other = new CharacterInstance("O1", otherDefinition, 1);
            Assert.That(w.Ownership.TryWithAdded(other, out CharacterOwnershipState nextOwnership), Is.True);
            string[] boardIds = (string[])w.BoardIds.Clone();
            boardIds[8] = other.InstanceId;
            var otherPoolEntry = new CharacterPoolEntry("other", 9, 10, 0, 1);
            var poolEntries = new List<CharacterPoolEntry>(w.Pool.Entries.Values) { otherPoolEntry };
            World occupied = new World
            {
                Definitions = w.Definitions,
                Costs = w.Costs,
                Stats = w.Stats,
                StatsState = w.StatsState,
                Factory = w.Factory,
                Economy = w.Economy,
                Shop = w.Shop,
                Pool = new CharacterPool(poolEntries),
                Board = new BoardState(3, 3, boardIds),
                Hand = w.Hand,
                Ownership = nextOwnership,
                BoardIds = boardIds,
                HandIds = w.HandIds
            };

            BoardShopTransactionResult r = Buy(occupied, new CharacterLocation(CharacterLocationKind.Board, 8));
            AssertSuccess(r, occupied);
            Assert.That(r.MergeStageCount, Is.EqualTo(1));
            Assert.That(r.NextBoard.TryGetCharacterIdAt(0, out string survivorId), Is.True);
            Assert.That(survivorId, Is.EqualTo(w.BoardIds[0]));
            Assert.That(r.NextBoard.TryGetCharacterIdAt(8, out string stillThere), Is.True);
            Assert.That(stillThere, Is.EqualTo(other.InstanceId));
        }

        [Test]
        public void ChainPreservesExcessTwoStarsAndStatsCheckpointAdvancesByTwoStages()
        {
            World w = MakeWorld(new int[9], new[] { 2, 2, 2, 1, 1 }, 0, 1);
            Pcg32State before = w.StatsState;
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 4));
            AssertSuccess(r, w);
            Assert.That(r.MergeStageCount, Is.EqualTo(2));
            Assert.That(r.NextOwnership.OwnedCount, Is.EqualTo(2));
            Assert.That(CopiesInOwnership(r.NextOwnership), Is.EqualTo(12));
            Assert.That(r.NextOwnership.TryGet(r.FinalSurvivingInstanceId, out CharacterInstanceView final), Is.True);
            Assert.That(final.Star, Is.EqualTo(3));
            Assert.That(r.NextStatsRandomState.State, Is.Not.EqualTo(before.State));
            Assert.That(r.NextInstanceFactory.LastIssuedNumber, Is.Zero);
            CharacterInstance expected = new CharacterInstance("expected", w.Definitions[0], 2);
            Pcg32RandomSource expectedRandom = Pcg32RandomSource.FromState(before);
            Assert.That(expected.TrySetStar(2, w.Definitions[0], w.Stats, expectedRandom), Is.True);
            Assert.That(expected.TrySetStar(3, w.Definitions[0], w.Stats, expectedRandom), Is.True);
            Assert.That(r.NextStatsRandomState.State, Is.EqualTo(expectedRandom.CaptureState().State));
            AssertPool(r.NextPool, owned: 12, reserved: 0);
        }

        [Test]
        public void ChainStatsFailureRollsBackAllStatesAndCheckpoints()
        {
            World w = MakeWorld(new int[9], new[] { 2, 2, 1, 1, 0 }, 0, 1, includeStar3Stats: false);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 4));
            AssertFailureUnchanged(r, w, BoardShopTransactionFailureReason.MergeValidationFailed);
        }

        [Test]
        public void OneStageMergeAdvancesStatsButDoesNotIssueInstanceId()
        {
            World w = MakeWorld(new int[9], new[] { 1, 1, 0, 0, 0 }, 0, 1);
            BoardShopTransactionResult r = Buy(w, new CharacterLocation(CharacterLocationKind.Hand, 4));
            AssertSuccess(r, w);
            Assert.That(r.NextStatsRandomState.State, Is.Not.EqualTo(w.StatsState.State));
            Assert.That(r.NextInstanceFactory.LastIssuedNumber, Is.Zero);
        }

        [Test]
        public void SaleFromBoardReturnsStarsGoldCopiesAndClearsSlot()
        {
            World w = MakeWorld(new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0 }, new int[5], 0, 0);
            string id = w.BoardIds[0];
            BoardShopTransactionResult r = BoardShopTransactionRules.TrySellCharacter(id, w.Economy,
                w.Shop, w.Pool, w.Board, w.Hand, w.Ownership, w.Factory, w.StatsState, ownershipScopeMatchesPool: true);
            Assert.That(r.Succeeded, Is.True);
            Assert.That(r.GoldDelta, Is.EqualTo(18));
            Assert.That(r.PoolCopiesDelta, Is.EqualTo(9));
            Assert.That(r.NextBoard.TryGetCharacterIdAt(0, out string remaining), Is.True);
            Assert.That(remaining, Is.Null);
            Assert.That(r.NextOwnership.OwnedCount, Is.Zero);
            AssertPool(r.NextPool, owned: 0, reserved: 0);
        }

        [Test]
        public void SaleFromHandForOneAndTwoStarsUsesRunCostCopiesAndClearsSlot()
        {
            foreach (int star in new[] { 1, 2 })
            {
                World w = MakeWorld(new int[9], new[] { star, 0, 0, 0, 0 }, 0, 0);
                BoardShopTransactionResult r = BoardShopTransactionRules.TrySellCharacter(w.HandIds[0], w.Economy,
                    w.Shop, w.Pool, w.Board, w.Hand, w.Ownership, w.Factory, w.StatsState, ownershipScopeMatchesPool: true);
                Assert.That(r.Succeeded, Is.True);
                Assert.That(r.GoldDelta, Is.EqualTo(2 * Copies(star)));
                Assert.That(r.PoolCopiesDelta, Is.EqualTo(Copies(star)));
                Assert.That(r.NextHand.TryGetSlot(0, out HandSlotContent content), Is.True);
                Assert.That(content.IsEmpty, Is.True);
            }
        }

        [Test]
        public void SaleRejectsUnownedAndDuplicateSaleWithoutChangingInputs()
        {
            World w = MakeWorld(new int[9], new[] { 1, 0, 0, 0, 0 }, 0, 0);
            BoardShopTransactionResult bad = BoardShopTransactionRules.TrySellCharacter("missing", w.Economy,
                w.Shop, w.Pool, w.Board, w.Hand, w.Ownership, w.Factory, w.StatsState, ownershipScopeMatchesPool: true);
            AssertFailureUnchanged(bad, w, BoardShopTransactionFailureReason.NotOwned);
            BoardShopTransactionResult first = BoardShopTransactionRules.TrySellCharacter(w.HandIds[0], w.Economy,
                w.Shop, w.Pool, w.Board, w.Hand, w.Ownership, w.Factory, w.StatsState, ownershipScopeMatchesPool: true);
            Assert.That(first.Succeeded, Is.True);
            BoardShopTransactionResult second = BoardShopTransactionRules.TrySellCharacter(w.HandIds[0], first.NextEconomy,
                w.Shop, first.NextPool, first.NextBoard, first.NextHand, first.NextOwnership,
                first.NextInstanceFactory, first.NextStatsRandomState, ownershipScopeMatchesPool: true);
            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.FailureReason, Is.EqualTo(BoardShopTransactionFailureReason.NotOwned));
        }

        [Test]
        public void PurchaseAndMergeReplayExactlyFromSameInputs()
        {
            World first = MakeWorld(new int[9], new[] { 1, 1, 0, 0, 0 }, 0, 2, seed: 1337);
            World second = MakeWorld(new int[9], new[] { 1, 1, 0, 0, 0 }, 0, 2, seed: 1337);
            BoardShopTransactionResult a = Buy(first, new CharacterLocation(CharacterLocationKind.Hand, 4));
            BoardShopTransactionResult b = Buy(second, new CharacterLocation(CharacterLocationKind.Hand, 4));
            Assert.That(a.Succeeded, Is.True);
            Assert.That(a.FinalSurvivingInstanceId, Is.EqualTo(b.FinalSurvivingInstanceId));
            Assert.That(a.NextOwnership.TryGet(a.FinalSurvivingInstanceId, out CharacterInstanceView av), Is.True);
            Assert.That(b.NextOwnership.TryGet(b.FinalSurvivingInstanceId, out CharacterInstanceView bv), Is.True);
            Assert.That(av.Attack, Is.EqualTo(bv.Attack));
            Assert.That(av.Defense, Is.EqualTo(bv.Defense));
            Assert.That(a.NextStatsRandomState.State, Is.EqualTo(b.NextStatsRandomState.State));
        }

        private static BoardShopTransactionResult Buy(World w, CharacterLocation destination)
        {
            return BoardShopTransactionRules.TryPurchaseCharacter(w.Shop, "offer", w.Definitions, w.Costs,
                w.Economy, w.Pool, w.Board, w.Hand, w.Ownership, w.Factory, w.Stats,
                w.StatsState, destination, ownershipScopeMatchesPool: true);
        }

        private static void AssertSuccess(BoardShopTransactionResult result, World input)
        {
            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(BoardHandStateValidator.Validate(result.NextBoard, result.NextHand, result.NextOwnership).IsValid, Is.True);
            Assert.That(result.NextPool.Entries["hero"].RemainingCopies + result.NextPool.Entries["hero"].ReservedCopies +
                result.NextPool.Entries["hero"].OwnedCopies, Is.EqualTo(result.NextPool.Entries["hero"].InitialStockLimit));
            Assert.That(CopiesInOwnership(result.NextOwnership, "hero"), Is.EqualTo(result.NextPool.Entries["hero"].OwnedCopies));
            Assert.That(input.Factory.LastIssuedNumber, Is.Zero);
            Assert.That(input.StatsState.State, Is.Not.EqualTo(0UL));
        }

        private static void AssertFailureUnchanged(BoardShopTransactionResult result, World w,
            BoardShopTransactionFailureReason reason)
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(reason));
            Assert.That(result.NextEconomy, Is.SameAs(w.Economy));
            Assert.That(result.NextShop, Is.SameAs(w.Shop));
            Assert.That(result.NextPool, Is.SameAs(w.Pool));
            Assert.That(result.NextBoard, Is.SameAs(w.Board));
            Assert.That(result.NextHand, Is.SameAs(w.Hand));
            Assert.That(result.NextOwnership, Is.SameAs(w.Ownership));
            Assert.That(result.NextInstanceFactory, Is.SameAs(w.Factory));
            Assert.That(result.NextStatsRandomState.State, Is.EqualTo(w.StatsState.State));
            Assert.That(w.Factory.LastIssuedNumber, Is.Zero);
        }

        private static void AssertPool(CharacterPool pool, int owned, int reserved)
        {
            CharacterPoolEntry entry = pool.Entries["hero"];
            Assert.That(entry.OwnedCopies, Is.EqualTo(owned));
            Assert.That(entry.ReservedCopies, Is.EqualTo(reserved));
            Assert.That(entry.RemainingCopies + entry.ReservedCopies + entry.OwnedCopies, Is.EqualTo(entry.InitialStockLimit));
        }

        private static int CopiesInOwnership(CharacterOwnershipState ownership)
        {
            int copies = 0;
            foreach (CharacterInstanceView view in ownership.GetOwnedInstances()) copies += view.PoolCopies;
            return copies;
        }

        private static int CopiesInOwnership(CharacterOwnershipState ownership, string definitionId)
        {
            int copies = 0;
            foreach (CharacterInstanceView view in ownership.GetOwnedInstances())
                if (view.DefinitionId == definitionId) copies += view.PoolCopies;
            return copies;
        }

        private static int Copies(int star) => star == 1 ? 1 : star == 2 ? 3 : 9;

        private static World MakeWorld(int[] boardStars, int[] handStars, int extraOneStarCount,
            int reservedCopies, int gold = 100, bool includeStar3Stats = true, ulong seed = 92)
        {
            if (boardStars.Length != 9 || handStars.Length < 5) throw new ArgumentException("Invalid test board/hand dimensions.");
            var definition = new CharacterDefinition("hero", "Hero", 1, null, null);
            var definitions = new[] { definition };
            CharacterStatDefinition stats = MakeStats(includeStar3Stats);
            var setupRandom = new Pcg32RandomSource(seed + 100, 19);
            var factory = new CharacterInstanceFactory("E");
            var characters = new List<CharacterInstance>();
            var boardIds = new string[9];
            var handIds = new string[handStars.Length];
            int copies = 0;
            for (int i = 0; i < boardStars.Length; i++)
            {
                int star = boardStars[i];
                if (star == 0) continue;
                CharacterInstance c = factory.Create(definition, 2, star);
                c.TrySetStar(star, definition, stats, setupRandom);
                characters.Add(c); boardIds[i] = c.InstanceId; copies += Copies(star);
            }
            for (int i = 0; i < handStars.Length; i++)
            {
                int star = handStars[i];
                if (star == 0) continue;
                CharacterInstance c = factory.Create(definition, 2, star);
                c.TrySetStar(star, definition, stats, setupRandom);
                characters.Add(c); handIds[i] = c.InstanceId; copies += Copies(star);
            }
            for (int i = 0; i < extraOneStarCount; i++)
            {
                CharacterInstance c = factory.Create(definition, 2, 1);
                c.TrySetStar(1, definition, stats, setupRandom);
                characters.Add(c); copies++;
            }

            var handContent = new HandSlotContent[handStars.Length];
            for (int i = 0; i < handContent.Length; i++)
                handContent[i] = handIds[i] == null ? HandSlotContent.Empty() : HandSlotContent.Character(handIds[i]);
            int initialStock = Math.Max(30, copies + reservedCopies);
            var pool = new CharacterPool(new[] { new CharacterPoolEntry("hero", initialStock - copies - reservedCopies,
                initialStock, reservedCopies, copies) });
            var offer = new ShopOffer("offer", ShopOfferKind.Character, "hero", 2, 2);
            return new World
            {
                Definitions = definitions,
                Costs = new AssignedCharacterCosts(new[] { new KeyValuePair<string, int>("hero", 2) }),
                Stats = stats,
                StatsState = new Pcg32RandomSource(seed, 27).CaptureState(),
                Factory = new CharacterInstanceFactory("T"),
                Economy = new EconomyState(gold, 0, 1, 12, 100, 0, 1),
                Shop = new ShopState(ShopKind.Character, new[] { offer }, 1, 1, 1),
                Pool = pool,
                Board = new BoardState(3, 3, boardIds),
                Hand = new HandState(handContent),
                Ownership = new CharacterOwnershipState(characters),
                BoardIds = boardIds,
                HandIds = handIds
            };
        }

        private static CharacterStatDefinition MakeStats(bool includeStar3)
        {
            var options = new[] { new CharacterStatOption(10, 5, 1), new CharacterStatOption(15, 7, 1) };
            var star3 = includeStar3 ? options : Array.Empty<CharacterStatOption>();
            return new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(2, options, options, star3)
            });
        }

        private sealed class World
        {
            public IReadOnlyList<CharacterDefinition> Definitions;
            public AssignedCharacterCosts Costs;
            public CharacterStatDefinition Stats;
            public Pcg32State StatsState;
            public CharacterInstanceFactory Factory;
            public EconomyState Economy;
            public ShopState Shop;
            public CharacterPool Pool;
            public BoardState Board;
            public HandState Hand;
            public CharacterOwnershipState Ownership;
            public string[] BoardIds;
            public string[] HandIds;

            public World WithShop(ShopOfferStatus status)
            {
                return new World
                {
                    Definitions = Definitions, Costs = Costs, Stats = Stats, StatsState = StatsState, Factory = Factory,
                    Economy = Economy, Shop = Shop.WithReplacedOffer(new ShopOffer("offer", ShopOfferKind.Character,
                        "hero", 2, 2, status)), Pool = Pool, Board = Board, Hand = Hand, Ownership = Ownership,
                    BoardIds = BoardIds, HandIds = HandIds
                };
            }

            public World WithPoolOwnedCopies(int ownedCopies)
            {
                CharacterPoolEntry old = Pool.Entries["hero"];
                int remaining = old.InitialStockLimit - old.ReservedCopies - ownedCopies;
                return new World
                {
                    Definitions = Definitions, Costs = Costs, Stats = Stats, StatsState = StatsState, Factory = Factory,
                    Economy = Economy, Shop = Shop,
                    Pool = new CharacterPool(new[] { new CharacterPoolEntry("hero", remaining, old.InitialStockLimit,
                        old.ReservedCopies, ownedCopies) }),
                    Board = Board, Hand = Hand, Ownership = Ownership, BoardIds = BoardIds, HandIds = HandIds
                };
            }
        }
    }
}
