using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Tests
{
    /// <summary>Multi-action integration and BattleManager compatibility scenarios for Stage 6-C.</summary>
    public sealed class SynergyIntegrationScenarioTests
    {
        private static readonly BoardHandOperationContext Prep = BoardHandOperationContext.Preparation;

        [Test]
        public void BoardMovesEstablishBreakAndReestablishAConnectedUnit()
        {
            CharacterDefinition a = Def("A", units: new[] { "Unit" });
            CharacterDefinition b = Def("B", units: new[] { "Unit" });
            CharacterDefinition c = Def("C", units: new[] { "Unit" });
            World w = MakeWorld(new[] { a, b, c }, null, new[] { "A", "B", "C" },
                new[] { a, b, c }, new[] { a, b, c });

            MoveHandToBoard(w, 0, 0);
            AssertUnitCount(w, 0);
            MoveHandToBoard(w, 1, 1);
            AssertUnitCount(w, 0);
            MoveHandToBoard(w, 2, 3);
            AssertUnit(w, "Unit", new[] { 0, 1, 3 });

            MoveBoard(w, 3, 8);
            AssertUnitCount(w, 0);
            MoveBoard(w, 8, 3);
            AssertUnit(w, "Unit", new[] { 0, 1, 3 });
        }

        [Test]
        public void MovingUnitMemberBetweenHandAndBoardChangesUnitMembership()
        {
            CharacterDefinition a = Def("A", units: new[] { "Unit" });
            CharacterDefinition b = Def("B", units: new[] { "Unit" });
            World w = MakeWorld(new[] { a, b }, null, new[] { "A", "B" }, new[] { a, b }, new[] { a, b });
            MoveHandToBoard(w, 0, 0);
            AssertUnitCount(w, 0);

            MoveHandToBoard(w, 1, 1);
            AssertUnit(w, "Unit", new[] { 0, 1 });

            MoveBoardToHand(w, 1, 1);
            AssertUnitCount(w, 0);
        }

        [Test]
        public void AttributeCountsChangeBySharedTypesAndIgnoreHandCharacters()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "X", "Y" });
            CharacterDefinition b = Def("B", attributes: new[] { "X" });
            CharacterDefinition c = Def("C", attributes: new[] { "X" });
            CharacterDefinition d = Def("D", attributes: new[] { "Y" });
            World w = MakeWorld(new[] { a, b, c, d }, null, new[] { "A", "B", "C", "D" },
                new[] { a, b, c, d }, new[] { a, b, c, d });

            MoveHandToBoard(w, 0, 0);
            AssertBlock(w, 0);
            MoveHandToBoard(w, 1, 1);
            AssertBlock(w, 1);
            MoveHandToBoard(w, 2, 3);
            AssertBlock(w, 1);
            MoveHandToBoard(w, 3, 4);
            AssertBlock(w, 2);

            MoveBoardToHand(w, 4, 0);
            AssertBlock(w, 1);
            AssertAttributeCount(w, "Y", 0);
        }

        [Test]
        public void OverlappingUnitsRemainIndependentAndPrioritizedAfterAPlacementChange()
        {
            CharacterDefinition a = Def("A", units: new[] { "UnitX", "UnitY" });
            CharacterDefinition b = Def("B", units: new[] { "UnitX", "UnitY" });
            CharacterDefinition c = Def("C", units: new[] { "UnitX" });
            CharacterDefinition d = Def("D", units: new[] { "UnitY" });
            var placements = new[] { new Placement("A", 0), new Placement("B", 1),
                new Placement("C", 3), new Placement("D", 2) };
            World w = MakeWorld(new[] { a, b, c, d }, placements, null,
                new[] { a, b, c, d }, new[] { a, b, c, d });

            SynergyDetectionResult initial = Detect(w);
            CollectionAssert.AreEqual(new[] { "UnitY", "UnitX" }, initial.EstablishedUnits.Select(x => x.UnitName));
            Assert.That(initial.EstablishedUnits[0].ParticipantCount, Is.EqualTo(3));
            Assert.That(initial.EstablishedUnits[1].ParticipantCount, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { "I-A", "I-B" }, initial.EstablishedUnits[0].ParticipantInstanceIds.Take(2));
            CollectionAssert.AreEqual(new[] { "I-A", "I-B" }, initial.EstablishedUnits[1].ParticipantInstanceIds.Take(2));

            MoveBoard(w, 3, 8);
            SynergyDetectionResult changed = Detect(w);
            CollectionAssert.AreEqual(new[] { "UnitY" }, changed.EstablishedUnits.Select(x => x.UnitName));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, changed.EstablishedUnits[0].ParticipantBoardSlotIndexes);
        }

        [Test]
        public void ShopPurchaseMoveAndSaleUpdateSynergiesPoolAndGoldAtomically()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Shared" }, units: new[] { "Unit" });
            CharacterDefinition b = Def("B", attributes: new[] { "Shared" }, units: new[] { "Unit" });
            World w = MakeShopWorld(new[] { a, b });

            BoardShopTransactionResult buyA = Buy(w, "OfferA", new CharacterLocation(CharacterLocationKind.Hand, 0));
            Assert.That(buyA.Succeeded, Is.True, buyA.FailureReason.ToString());
            w.Adopt(buyA);
            AssertIntegrity(w);
            AssertUnitCount(w, 0);
            string idA = HandId(w.Hand, 0);
            MoveHandToBoard(w, 0, 0);
            AssertUnitCount(w, 0);

            BoardShopTransactionResult buyB = Buy(w, "OfferB", new CharacterLocation(CharacterLocationKind.Hand, 0));
            Assert.That(buyB.Succeeded, Is.True, buyB.FailureReason.ToString());
            w.Adopt(buyB);
            AssertIntegrity(w);
            AssertUnitCount(w, 0);
            string idB = HandId(w.Hand, 0);
            MoveHandToBoard(w, 0, 1);
            AssertUnit(w, "Unit", new[] { 0, 1 });
            AssertBlock(w, 1);
            Assert.That(w.Ownership.TryGet(idA, out CharacterInstanceView ownedA), Is.True);
            Assert.That(w.Ownership.TryGet(idB, out CharacterInstanceView ownedB), Is.True);
            Assert.That(ownedA.DefinitionId, Is.EqualTo("A"));
            Assert.That(ownedB.DefinitionId, Is.EqualTo("B"));

            BoardShopTransactionResult sale = Sell(w, idB);
            Assert.That(sale.Succeeded, Is.True, sale.FailureReason.ToString());
            w.Adopt(sale);
            AssertIntegrity(w);
            Assert.That(w.Ownership.Contains(idB), Is.False);
            Assert.That(w.Ownership.Contains(idA), Is.True);
            AssertUnitCount(w, 0);
            AssertBlock(w, 0);
            Assert.That(w.Economy.Gold, Is.EqualTo(98));
            AssertPoolEntry(w, "A", remaining: 9, reserved: 0, owned: 1, initial: 10);
            AssertPoolEntry(w, "B", remaining: 10, reserved: 0, owned: 0, initial: 10);
        }

        [Test]
        public void BattleManagerCompatibilityCasesMatchRosterConnectivityAttributeAndPriorityRules()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage", "Mage" }, units: new[] { "Zulu", "Alpha", "Alpha", "Beta" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage" }, units: new[] { "Zulu", "Alpha", "Beta" });
            CharacterDefinition c = Def("C", units: new[] { "Zulu", "Beta" });
            CharacterDefinition d = Def("D", units: new[] { "Alpha" });
            World w = MakeWorld(new[] { a, b, c, d },
                new[] { new Placement("A", 0), new Placement("B", 1), new Placement("C", 3), new Placement("D", 4) },
                null, new[] { a, b, c, d }, new[] { a, b, c, d });

            SynergyDetectionResult result = Detect(w);
            Assert.That(result.EstablishedUnits.Count, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { "Beta", "Zulu", "Alpha" }, result.EstablishedUnits.Select(x => x.UnitName));
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, result.EstablishedUnits[0].ParticipantBoardSlotIndexes);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, result.EstablishedUnits[1].ParticipantBoardSlotIndexes);
            CollectionAssert.AreEqual(new[] { 0, 1, 4 }, result.EstablishedUnits[2].ParticipantBoardSlotIndexes);
            AssertBlock(w, 1);

            // Like ShopManager.IsUnitComplete, the selected membership count must equal the full-catalog count.
            CharacterDefinition unselected = Def("Unselected", units: new[] { "Zulu" });
            World incompleteRoster = MakeWorld(new[] { a, b, c, d },
                new[] { new Placement("A", 0), new Placement("B", 1), new Placement("C", 3), new Placement("D", 4) },
                null, new[] { a, b, c, d }, new[] { a, b, c, d, unselected });
            CollectionAssert.AreEqual(new[] { "Beta", "Alpha" }, Detect(incompleteRoster).EstablishedUnits.Select(x => x.UnitName));

            // A hand character is absent from the battle unit list and attribute count.
            World withHandMember = MakeWorld(new[] { a, b }, new[] { new Placement("A", 0) }, new[] { "B" },
                new[] { a, b }, new[] { a, b });
            AssertUnitCount(withHandMember, 0);
            AssertBlock(withHandMember, 0);
        }

        private static void MoveHandToBoard(World w, int handSlot, int boardSlot)
        {
            BoardHandOperationResult result = BoardHandRules.MoveHandToBoard(w.Board, w.Hand, w.Ownership,
                handSlot, boardSlot, Prep);
            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            w.Adopt(result);
            AssertIntegrity(w);
            Detect(w);
        }

        private static void MoveBoard(World w, int from, int to)
        {
            BoardHandOperationResult result = BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, from, to, Prep);
            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            w.Adopt(result);
            AssertIntegrity(w);
            Detect(w);
        }

        private static void MoveBoardToHand(World w, int boardSlot, int handSlot)
        {
            BoardHandOperationResult result = BoardHandRules.MoveBoardToHand(w.Board, w.Hand, w.Ownership,
                boardSlot, handSlot, Prep);
            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            w.Adopt(result);
            AssertIntegrity(w);
            Detect(w);
        }

        private static SynergyDetectionResult Detect(World w)
        {
            string[] boardBefore = BoardSignature(w.Board);
            string[] ownedBefore = OwnershipSignature(w.Ownership);
            Pcg32State randomBefore = w.RandomState;
            SynergyDetectionResult first = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            SynergyDetectionResult second = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            Assert.That(first.IsValid, Is.True, string.Join(" | ", first.Errors));
            AssertDetectionEqual(first, second);
            CollectionAssert.AreEqual(boardBefore, BoardSignature(w.Board));
            CollectionAssert.AreEqual(ownedBefore, OwnershipSignature(w.Ownership));
            AssertStateEqual(randomBefore, w.RandomState);
            return first;
        }

        private static void AssertUnit(World w, string name, int[] expectedSlots)
        {
            SynergyDetectionResult result = Detect(w);
            UnitSynergyResult unit = result.EstablishedUnits.Single(x => x.UnitName == name);
            CollectionAssert.AreEqual(expectedSlots, unit.ParticipantBoardSlotIndexes);
        }

        private static void AssertUnitCount(World w, int count) => Assert.That(Detect(w).EstablishedUnits.Count, Is.EqualTo(count));
        private static void AssertBlock(World w, int count) => Assert.That(Detect(w).AttributeBlockBonus, Is.EqualTo(count));

        private static void AssertAttributeCount(World w, string attribute, int count)
        {
            SynergyDetectionResult result = Detect(w);
            Assert.That(result.EstablishedAttributes.Count(x => x.AttributeName == attribute), Is.EqualTo(count));
        }

        private static World MakeWorld(CharacterDefinition[] definitions, Placement[] placements,
            string[] handDefinitionIds, CharacterDefinition[] selected, CharacterDefinition[] full)
        {
            var byId = definitions.ToDictionary(x => x.DefinitionId, StringComparer.Ordinal);
            var instances = definitions.ToDictionary(x => x.DefinitionId,
                x => new CharacterInstance("I-" + x.DefinitionId, x, 2), StringComparer.Ordinal);
            var boardIds = new string[BoardState.StandardColumns * BoardState.StandardRows];
            var handSlots = Enumerable.Range(0, HandState.StandardSlotCount)
                .Select(_ => HandSlotContent.Empty()).ToArray();
            var placed = new HashSet<string>(StringComparer.Ordinal);
            if (placements != null)
            {
                foreach (Placement placement in placements)
                {
                    boardIds[placement.Slot] = instances[placement.DefinitionId].InstanceId;
                    placed.Add(placement.DefinitionId);
                }
            }
            if (handDefinitionIds != null)
            {
                for (int i = 0; i < handDefinitionIds.Length; i++)
                {
                    handSlots[i] = HandSlotContent.Character(instances[handDefinitionIds[i]].InstanceId);
                    placed.Add(handDefinitionIds[i]);
                }
            }
            return new World
            {
                Definitions = definitions,
                Selected = selected,
                Full = full,
                DefinitionById = byId,
                Board = new BoardState(3, 3, boardIds),
                Hand = new HandState(handSlots),
                Ownership = new CharacterOwnershipState(placed.Select(id => instances[id])),
                RandomState = new Pcg32RandomSource(20261121UL, 661).CaptureState()
            };
        }

        private static World MakeShopWorld(CharacterDefinition[] definitions)
        {
            World w = MakeWorld(definitions, null, null, definitions, definitions);
            var offers = new List<ShopOffer>();
            var pools = new List<CharacterPoolEntry>();
            var costs = new List<KeyValuePair<string, int>>();
            foreach (CharacterDefinition definition in definitions)
            {
                string offerId = "Offer" + definition.DefinitionId;
                offers.Add(new ShopOffer(offerId, ShopOfferKind.Character, definition.DefinitionId, 2, 2));
                pools.Add(new CharacterPoolEntry(definition.DefinitionId, 9, 10, 1, 0));
                costs.Add(new KeyValuePair<string, int>(definition.DefinitionId, 2));
            }
            CharacterStatOption[] options = { new CharacterStatOption(10, 4, 1) };
            w.Shop = new ShopState(ShopKind.Character, offers, offers.Count, 1, 1);
            w.Pool = new CharacterPool(pools);
            w.Costs = new AssignedCharacterCosts(costs);
            w.Economy = new EconomyState(100, 0, 1, 12, 100, 0, 1);
            w.Factory = new CharacterInstanceFactory("N");
            w.Stats = new CharacterStatDefinition(new[] { new CharacterCostStats(2, options, options, options) });
            return w;
        }

        private static BoardShopTransactionResult Buy(World w, string offerId, CharacterLocation target) =>
            BoardShopTransactionRules.TryPurchaseCharacter(w.Shop, offerId, w.Definitions, w.Costs,
                w.Economy, w.Pool, w.Board, w.Hand, w.Ownership, w.Factory, w.Stats,
                w.RandomState, target, ownershipScopeMatchesPool: true);

        private static BoardShopTransactionResult Sell(World w, string instanceId) =>
            BoardShopTransactionRules.TrySellCharacter(instanceId, w.Economy, w.Shop, w.Pool,
                w.Board, w.Hand, w.Ownership, w.Factory, w.RandomState, ownershipScopeMatchesPool: true);

        private static void AssertIntegrity(World w)
        {
            BoardHandStateValidationResult placement = BoardHandStateValidator.Validate(w.Board, w.Hand, w.Ownership);
            Assert.That(placement.IsValid, Is.True, string.Join(" | ", placement.Errors));
            foreach (CharacterInstanceView instance in w.Ownership.GetOwnedInstances())
                Assert.That(w.DefinitionById.ContainsKey(instance.DefinitionId), Is.True);
            if (w.Pool == null) return;
            foreach (CharacterPoolEntry entry in w.Pool.Entries.Values)
            {
                Assert.That(entry.RemainingCopies + entry.ReservedCopies + entry.OwnedCopies,
                    Is.EqualTo(entry.InitialStockLimit));
                int ownedCopies = w.Ownership.GetOwnedInstances().Where(x => x.DefinitionId == entry.DefinitionId)
                    .Sum(x => x.PoolCopies);
                Assert.That(entry.OwnedCopies, Is.EqualTo(ownedCopies));
                int availableOffers = w.Shop.Offers.Count(x => x.DefinitionId == entry.DefinitionId && x.Status == ShopOfferStatus.Available);
                Assert.That(entry.ReservedCopies, Is.EqualTo(availableOffers));
            }
        }

        private static void AssertPoolEntry(World w, string id, int remaining, int reserved, int owned, int initial)
        {
            CharacterPoolEntry entry = w.Pool.Entries[id];
            Assert.That(entry.RemainingCopies, Is.EqualTo(remaining));
            Assert.That(entry.ReservedCopies, Is.EqualTo(reserved));
            Assert.That(entry.OwnedCopies, Is.EqualTo(owned));
            Assert.That(entry.InitialStockLimit, Is.EqualTo(initial));
        }

        private static string HandId(HandState hand, int slot)
        {
            hand.TryGetSlot(slot, out HandSlotContent content);
            return content.CharacterInstanceId;
        }

        private static CharacterDefinition Def(string id, string[] attributes = null, string[] units = null) =>
            new CharacterDefinition(id, id, 2, attributes, units);

        private static string[] BoardSignature(BoardState board)
        {
            var result = new string[board.SlotCount];
            for (int i = 0; i < result.Length; i++) board.TryGetCharacterIdAt(i, out result[i]);
            return result;
        }

        private static string[] OwnershipSignature(CharacterOwnershipState ownership) => ownership.GetOwnedInstances()
            .Select(x => x.InstanceId + ":" + x.DefinitionId + ":" + x.Star + ":" + x.Attack + ":" + x.Defense)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

        private static void AssertDetectionEqual(SynergyDetectionResult left, SynergyDetectionResult right)
        {
            Assert.That(right.IsValid, Is.EqualTo(left.IsValid));
            Assert.That(right.AttributeBlockBonus, Is.EqualTo(left.AttributeBlockBonus));
            CollectionAssert.AreEqual(left.EstablishedUnits.Select(x => x.UnitName), right.EstablishedUnits.Select(x => x.UnitName));
            for (int i = 0; i < left.EstablishedUnits.Count; i++)
            {
                CollectionAssert.AreEqual(left.EstablishedUnits[i].ParticipantInstanceIds, right.EstablishedUnits[i].ParticipantInstanceIds);
                CollectionAssert.AreEqual(left.EstablishedUnits[i].ParticipantBoardSlotIndexes, right.EstablishedUnits[i].ParticipantBoardSlotIndexes);
            }
            CollectionAssert.AreEqual(left.EstablishedAttributes.Select(x => x.AttributeName), right.EstablishedAttributes.Select(x => x.AttributeName));
        }

        private static void AssertStateEqual(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        private sealed class Placement
        {
            public string DefinitionId { get; }
            public int Slot { get; }
            public Placement(string definitionId, int slot) { DefinitionId = definitionId; Slot = slot; }
        }

        private sealed class World
        {
            public CharacterDefinition[] Definitions;
            public CharacterDefinition[] Selected;
            public CharacterDefinition[] Full;
            public Dictionary<string, CharacterDefinition> DefinitionById;
            public BoardState Board;
            public HandState Hand;
            public CharacterOwnershipState Ownership;
            public ShopState Shop;
            public CharacterPool Pool;
            public AssignedCharacterCosts Costs;
            public EconomyState Economy;
            public CharacterInstanceFactory Factory;
            public CharacterStatDefinition Stats;
            public Pcg32State RandomState;

            public void Adopt(BoardHandOperationResult result) { Board = result.NextBoard; Hand = result.NextHand; }
            public void Adopt(BoardShopTransactionResult result)
            {
                Shop = result.NextShop; Pool = result.NextPool; Economy = result.NextEconomy;
                Board = result.NextBoard; Hand = result.NextHand; Ownership = result.NextOwnership;
                Factory = result.NextInstanceFactory; RandomState = result.NextStatsRandomState;
            }
        }
    }
}
