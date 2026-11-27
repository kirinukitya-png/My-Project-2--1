using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameObservationTests
    {
        private static HandSynergySlot OptionHand(int slot, string id, int star = 1) =>
            new HandSynergySlot(slot, HandSlotContentKind.Character, id, star, "hand-" + slot);

        private static HandSynergyOptionState OptionState(int level, string board, params HandSynergySlot[] hand) =>
            new HandSynergyOptionState(level, UnitMoveBoardFixture.Board(board), hand);

        private static GameObservation OptionObservation(string tags, HandSynergyOptionState state,
            string attributes = "", string excluded = "", int attributeCount = 1)
        {
            var roster = UnitMoveBoardFixture.Definitions(tags, state.Board.Concat(state.Hand
                    .Where(h => h.Kind == HandSlotContentKind.Character)
                    .Select(h => new UnitBoardMember(0, h.DefinitionId, h.Star))))
                .Select(d => new CharacterDefinition(d.DefinitionId, d.CharacterName, 1,
                    attributes.Contains(d.DefinitionId) ? Enumerable.Range(0, attributeCount)
                        .Select(i => "Shared" + i).ToArray() : null, d.Units)).ToArray();
            var selected = roster.Where(d => !excluded.Contains(d.DefinitionId)).ToArray();
            var ids = new string[9];
            var owned = new List<CharacterInstance>();
            foreach (var m in state.Board)
            {
                ids[m.SlotIndex] = "board-" + m.SlotIndex;
                owned.Add(new CharacterInstance(ids[m.SlotIndex], roster.Single(d => d.DefinitionId == m.DefinitionId), 1, m.Star));
            }
            var hand = HandState.CreateStandard();
            foreach (var h in state.Hand.Where(h => h.Kind == HandSlotContentKind.Character))
            {
                string id = "hand-" + h.SlotIndex;
                owned.Add(new CharacterInstance(id, roster.Single(d => d.DefinitionId == h.DefinitionId), 1, h.Star));
                Assert.That(hand.TryWithSlot(h.SlotIndex, HandSlotContent.Character(id), out hand), Is.True);
            }
            var config = StageConfig();
            var prep = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(config), config).NextState;
            var built = ObservationBuilder.Build(Source(prep,
                economy: new EconomyState(12, 0, state.Level, 12, 12, 0, 1, 25, 30),
                board: new BoardState(3, 3, ids), hand: hand, ownership: new CharacterOwnershipState(owned),
                definitions: roster, selected: selected, full: roster,
                shop: new ShopState(ShopKind.Character, Array.Empty<ShopOffer>(), 0, 1, 1),
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty()));
            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            return built.Observation;
        }

        [TestCase("A", 3, "AB.......", "C", "ABC", "", "", 1, 10, 150, HandSynergyOptionStatus.Positive)]
        [TestCase("B", 3, "AB.......", "D", "ABC", "", "", 1, 0, 0, HandSynergyOptionStatus.NoImprovement)]
        [TestCase("C", 2, "A........", "B", "ABC", "", "", 1, 2, 30, HandSynergyOptionStatus.Positive)]
        [TestCase("D", 2, "A........", "B", "ABC", "", "C", 1, 0, 0, HandSynergyOptionStatus.NoImprovement)]
        [TestCase("E", 3, "A.B......", "C", "AC,BC", "", "", 1, 12, 180, HandSynergyOptionStatus.Positive)]
        [TestCase("F", 2, "A........", "B", "", "AB", "", 1, 5, 75, HandSynergyOptionStatus.Positive)]
        [TestCase("G", 4, "...BAC.D.", "A", "AB,AC,AD", "", "", 2, 8, 120, HandSynergyOptionStatus.Positive)]
        [TestCase("H", 3, "AB.......", "CD", "ABC,ADX,ADY,ADZ", "", "", 1, 10, 150, HandSynergyOptionStatus.Positive)]
        [TestCase("I", 2, "AB.......", "X", "AB", "AB", "", 1, -15, 0, HandSynergyOptionStatus.NoImprovement)]
        [TestCase("J", 2, "AB.......", "-", "AB", "", "", 1, null, 0, HandSynergyOptionStatus.NoLegalCandidate)]
        [TestCase("K", 3, "AB.......", "C", "ABC", "", "", 1, null, 0, HandSynergyOptionStatus.Disabled)]
        public void HandOptionUsesApprovedNumericalCases(string example, int level, string board, string hand,
            string tags, string attributes, string excluded, int handStar, int? delta, int cents, HandSynergyOptionStatus status)
        {
            var state = OptionState(level, board, hand.Select((id, slot) => id == '-'
                ? new HandSynergySlot(slot, HandSlotContentKind.Item) : OptionHand(slot, id.ToString(), handStar)).ToArray());
            var observation = OptionObservation(tags, state, attributes, excluded);
            var evaluator = new HandSynergyOptionEvaluator(observation,
                settings: new HandSynergyOptionSettings(example == "K" ? 0m : 0.15m));
            var result = evaluator.Evaluate(state, true);
            Assert.That(result.HandUnitOption, Is.EqualTo(cents / 100m));
            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.Details.BestDelta?.TotalSynergyValue, Is.EqualTo(delta));
            Assert.That(evaluator.Evaluate(state).HandUnitOption, Is.EqualTo(result.HandUnitOption));
            var exact = new HandSynergyOptionEvaluator(observation, settings: evaluator.Settings, useCache: false, useSafeSkips: false)
                .Evaluate(state, true);
            Assert.That(exact.Details.BestDelta, Is.EqualTo(result.Details.BestDelta));
            Assert.That(exact.HandUnitOption, Is.EqualTo(result.HandUnitOption));
            if (example == "G")
            {
                Assert.That(result.Details.LegalCandidates, Is.EqualTo(1));
                Assert.That(result.Details.BestBoardSlot, Is.EqualTo(4));
                Assert.That(result.Details.IsExchange, Is.True);
            }
            if (example == "H")
            {
                Assert.That(result.Details.BestHand.Value.DefinitionId, Is.EqualTo("C"));
                Assert.That(evaluator.Evaluate(OptionState(level, board, OptionHand(0, "D")), true)
                    .Details.BestDelta.Value.TotalSynergyValue, Is.EqualTo(6));
            }
            if (example == "K")
            {
                Assert.That(result.Details.LegalCandidates, Is.Null);
                Assert.That(result.Details.CurrentSynergy, Is.Null);
                Assert.That(evaluator.CacheStatistics, Is.EqualTo((0, 0, 0)));
                Assert.Throws<ArgumentException>(() => evaluator.Evaluate(OptionState(3, "Z........")));
            }
        }

        [Test]
        public void HandOptionAllowsAnUntaggedReplacementToRemoveCompetition()
        {
            var state = OptionState(5, "ABCDE....", OptionHand(0, "X"));
            var result = new HandSynergyOptionEvaluator(OptionObservation("ABC,AD,BE", state)).EvaluateCurrent(true);
            Assert.That(result.Details.BestBoardSlot, Is.EqualTo(2));
            Assert.That(result.Details.CurrentSynergy.Value.UnitProgressValue, Is.EqualTo(8));
            Assert.That(result.Details.CurrentSynergy.Value.UnitActivationValue, Is.EqualTo(8));
            Assert.That(result.Details.BestAfterSynergy.Value.UnitProgressValue, Is.EqualTo(6));
            Assert.That(result.Details.BestAfterSynergy.Value.UnitActivationValue, Is.EqualTo(16));
            Assert.That(result.HandUnitOption, Is.EqualTo(0.90m));
        }

        [Test]
        public void BoardMoveCanOpenAnOptionWhilePreservingCurrentSynergy()
        {
            // B and X share two attributes. Replacing X with C in the initial layout loses those
            // ten points, so the starting Board cannot already realize a positive exchange.
            var before = new HandSynergyOptionState(4, UnitMoveBoardFixture.Board("AB.X.....", 2),
                new[] { OptionHand(0, "C") });
            var after = new HandSynergyOptionState(4, UnitMoveBoardFixture.Board(".BAX.....", 2), before.Hand);
            var observation = OptionObservation("AB,AC", before, "BX", attributeCount: 2);
            var evaluator = new HandSynergyOptionEvaluator(observation);
            var change = evaluator.CreateBaseline(before, true).Compare(after);
            Assert.That(change.Before.Details.CurrentSynergy, Is.EqualTo(change.After.Details.CurrentSynergy));
            Assert.That(change.Before.HandUnitOption, Is.Zero);
            Assert.That(change.After.Details.BestBoardSlot, Is.EqualTo(5));
            Assert.That(change.Delta, Is.EqualTo(1.5m));
            Assert.That(evaluator.CreateBaseline(after).Compare(before).Delta, Is.EqualTo(-1.5m));
        }

        [Test]
        public void HandOptionRealizationAndLevelTransitionsUseEndpointDifferences()
        {
            var before = OptionState(3, "AB.......", OptionHand(0, "C"));
            var observation = OptionObservation("ABC", before);
            var evaluator = new HandSynergyOptionEvaluator(observation);
            var noHand = OptionState(3, "AB.......");
            var lower = OptionState(2, "AB.......", OptionHand(0, "C"));
            var over = OptionState(1, "AB.......", OptionHand(0, "C"));
            var deployed = OptionState(3, "ABC......");
            var purchase = evaluator.CreateBaseline(noHand).Compare(before);
            Assert.That(purchase.Delta, Is.EqualTo(1.5m));
            Assert.That(evaluator.CreateBaseline(before).Compare(noHand).Delta, Is.EqualTo(-1.5m));
            Assert.That(evaluator.Evaluate(lower).HandUnitOption, Is.Zero);
            Assert.That(evaluator.Evaluate(over, true).Status, Is.EqualTo(HandSynergyOptionStatus.OverLevel));
            var realized = evaluator.CreateBaseline(before, true).Compare(deployed);
            var synergy = new SynergyValueEvaluator(observation).CreateBaseline(before.Board).Compare(deployed.Board);
            Assert.That(synergy.Delta.TotalSynergyValue, Is.EqualTo(10m));
            Assert.That(realized.After.HandUnitOption, Is.Zero);
            Assert.That(realized.Delta, Is.EqualTo(-1.5m));
            Assert.That(synergy.Delta.TotalSynergyValue + realized.Delta, Is.EqualTo(8.5m));
            Assert.That(purchase.Delta + realized.Delta, Is.EqualTo(evaluator.CreateBaseline(noHand).Compare(deployed).Delta));
        }

        [Test]
        public void HandOptionWithRemainingReserveReevaluatesAndDoesNotStack()
        {
            var state = OptionState(5, "AB..X....", OptionHand(0, "C"), OptionHand(1, "Y"));
            var evaluator = new HandSynergyOptionEvaluator(OptionObservation("ABC,XY", state));
            var before = evaluator.CreateBaseline(state);
            Assert.That(before.Before.HandUnitOption, Is.EqualTo(1.5m));
            Assert.That(evaluator.CreateBaseline(OptionState(5, "AB..X....", OptionHand(0, "C"))).Compare(state).Delta, Is.Zero);
            var after = OptionState(5, "ABC.X....", OptionHand(1, "Y"));
            Assert.That(before.Compare(after).After.HandUnitOption, Is.EqualTo(1.5m));
        }

        [Test]
        public void HandOptionCacheSharesNumericKeysButUpdatesDiagnosticSlots()
        {
            var state = OptionState(3, "AB.......", OptionHand(3, "C"), OptionHand(4, "C"));
            var observation = OptionObservation("ABC", state);
            var evaluator = new HandSynergyOptionEvaluator(observation);
            Assert.That(evaluator.Evaluate(state).HandUnitOption, Is.EqualTo(1.5m));
            var first = evaluator.CacheStatistics;
            Assert.That(first.SynergyCacheHits, Is.GreaterThan(0));
            var moved = OptionState(3, "AB.......", OptionHand(0, "C"), OptionHand(1, "C"),
                new HandSynergySlot(2, HandSlotContentKind.Item), new HandSynergySlot(4, HandSlotContentKind.Item));
            Assert.That(evaluator.Evaluate(moved).HandUnitOption, Is.EqualTo(1.5m));
            Assert.That(evaluator.CacheStatistics.OptionCacheHits, Is.EqualTo(first.OptionCacheHits + 1));
            var detail = evaluator.Evaluate(moved, true).Details;
            Assert.That(detail.BestHand.Value.SlotIndex, Is.Zero);
            Assert.That(detail.BestHand.Value.InstanceId, Is.EqualTo("hand-0"));
            Assert.That(detail.BestBoardSlot, Is.EqualTo(2));
            Assert.That(detail.NewEvaluations, Is.Zero);
            Assert.That(detail.CacheReuses, Is.GreaterThan(0));
            Assert.That(evaluator.Evaluate(OptionState(2, "AB.......", OptionHand(0, "C"), OptionHand(1, "C")))
                .HandUnitOption, Is.Zero);
            Assert.That(evaluator.CacheStatistics.OptionCacheHits, Is.EqualTo(first.OptionCacheHits + 1));
        }

        [Test]
        public void HandOptionLegalityMatchesTransactionsAndGeneratedDeployments()
        {
            var state = OptionState(2, "AB.......", OptionHand(0, "A", 2), OptionHand(1, "C"));
            var observation = OptionObservation("AB,BC", state);
            var fromStrategy = new RandomStrategy().GetCandidates(observation, new ActionId("option"))
                .OfType<MoveHandToBoardAction>().Where(a => observation.Board.Slots[a.BoardSlot].IsOccupied)
                .Select(a => (a.HandSlot, a.BoardSlot)).ToArray();
            var board = new BoardState(3, 3, new[] { "a", "b", null, null, null, null, null, null, null });
            var hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Character("a2"), out hand), Is.True);
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Character("c"), out hand), Is.True);
            var aDef = new CharacterDefinition("A", "A", 1, null, null);
            var ownership = new CharacterOwnershipState(new[] { new CharacterInstance("a", aDef, 1),
                new CharacterInstance("a2", aDef, 1, 2),
                new CharacterInstance("b", new CharacterDefinition("B", "B", 1, null, null), 1),
                new CharacterInstance("c", new CharacterDefinition("C", "C", 1, null, null), 1) });
            var legal = (from h in new[] { 0, 1 } from b in new[] { 0, 1 }
                         where BoardHandRules.MoveHandToBoard(board, hand, ownership, h, b,
                             BoardHandOperationContext.Preparation).Succeeded
                         select (h, b)).ToArray();
            Assert.That(legal, Is.EqualTo(new[] { (0, 0), (1, 0), (1, 1) }));
            Assert.That(fromStrategy, Is.EquivalentTo(legal));
            Assert.That(new HandSynergyOptionEvaluator(observation).EvaluateCurrent(true).Details.LegalCandidates, Is.EqualTo(3));
        }

        [Test]
        public void HandOptionHandlesEmptyBoardsAndBoundsCandidatePairs()
        {
            var state = OptionState(1, ".........", Enumerable.Range(0, 5).Select(i => OptionHand(i, "A")).ToArray());
            var observation = OptionObservation("AB", state);
            var evaluator = new HandSynergyOptionEvaluator(observation);
            var result = evaluator.Evaluate(state, true);
            Assert.That(result.Details.LegalCandidates, Is.EqualTo(45));
            Assert.That(result.HandUnitOption, Is.Zero);
            Assert.That(result.Details.NewEvaluations, Is.EqualTo(9));
            Assert.That(result.Details.CacheReuses, Is.EqualTo(36));
            Assert.That(evaluator.Evaluate(OptionState(1, ".........")).Status, Is.EqualTo(HandSynergyOptionStatus.NoLegalCandidate));
        }

        [Test]
        public void HandOptionValidatesCoefficientsAndSnapshotsInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HandSynergyOptionSettings(-0.01m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HandSynergyOptionSettings(1m));
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionState(0, "........."));
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionHand(5, "A"));
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionHand(0, "A", 0));
            Assert.Throws<ArgumentException>(() => OptionState(2, "AA......."));
            Assert.Throws<ArgumentException>(() => OptionState(2, "A........", OptionHand(0, "B"), OptionHand(0, "B")));
            Assert.Throws<ArgumentException>(() => new HandSynergyOptionState(2,
                new[] { new UnitBoardMember(0, "A", 1), new UnitBoardMember(0, "B", 1) }, Array.Empty<HandSynergySlot>()));
            var source = new[] { OptionHand(2, "B") };
            var state = OptionState(2, "A........", source);
            var evaluator = new HandSynergyOptionEvaluator(OptionObservation("AB", state));
            source[0] = OptionHand(0, "A");
            Assert.That(evaluator.Evaluate(state, true).Details.BestHand.Value.SlotIndex, Is.EqualTo(2));
            Assert.Throws<ArgumentNullException>(() => evaluator.Evaluate(null));
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(OptionState(2, "A........", OptionHand(0, "Z"))));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(OptionState(13, "A........")));
        }
    }

    public sealed partial class HeuristicStrategyTests
    {
        [TestCase(0, false, 180)]
        [TestCase(0, true, 0)]
        [TestCase(1, false, 300)]
        [TestCase(1, true, 0)]
        [TestCase(2, false, 450)]
        [TestCase(2, true, 0)]
        public void HandOptionProjectsRealAndHypotheticalPurchasesIncludingMerges(int stages, bool destinationBoard, int cents)
        {
            string tags = stages == 2 ? "AB,AC,AD" : "AB,AC";
            var members = UnitMoveBoardFixture.Board(stages == 2 ? "B.C.D...." : "B.C......");
            var defs = UnitMoveBoardFixture.Definitions(tags, members);
            var a = defs.Single(d => d.DefinitionId == "A");
            var owned = members.Select(m => Inst(m.DefinitionId, defs.Single(d => d.DefinitionId == m.DefinitionId))).ToList();
            var board = BoardState.CreateStandard();
            foreach (var m in members) board = Put(board, m.SlotIndex, m.DefinitionId);
            var hand = HandState.CreateStandard();
            var stars = stages == 2 ? new[] { 1, 1, 2, 2 } : stages == 1 ? new[] { 1, 1 } : Array.Empty<int>();
            for (int i = 0; i < stars.Length; i++)
            {
                var material = Inst("material-" + i, a, stars[i]);
                owned.Add(material);
                hand = Put(hand, i, HandSlotContent.Character(material.InstanceId));
            }
            var obs = Build(Prep(), Econ(level: 5), board, hand, owned.ToArray(), defs, CharacterShop("A"));
            var evaluator = new HandSynergyOptionEvaluator(obs);
            var baseline = evaluator.CreateBaseline(true);
            var previews = new CharacterPurchasePreview(obs);
            var location = new CharacterLocation(destinationBoard ? CharacterLocationKind.Board : CharacterLocationKind.Hand,
                destinationBoard ? 1 : 4);
            var rng = new Pcg32RandomSource(25, 5);
            var checkpoint = rng.CaptureState();
            var action = new BuyCharacterAction(new ActionId("option-buy"), "offer", new GameActionLocation(location.Kind, location.SlotIndex));
            var actual = previews.Preview(action);
            var future = previews.PreviewHypothetical(obs.SelectedRoster.Single(d => d.DefinitionId == "A"), 1, 1, 20, location);
            Assert.That(actual.Succeeded, Is.True, actual.Failure.ToString());
            Assert.That(actual.MergeStageCount, Is.EqualTo(stages));
            Assert.That(actual.FinalCharacter.Star, Is.EqualTo(stages + 1));
            Assert.That(actual.FinalLocation.Value.Kind, Is.EqualTo(location.Kind));
            Assert.That(actual.FinalLocation.Value.SlotIndex, Is.EqualTo(!destinationBoard && stages > 0 ? 0 : location.SlotIndex));
            var compared = baseline.ComparePurchasePreview(actual);
            Assert.That(compared.After.HandUnitOption, Is.EqualTo(cents / 100m));
            Assert.That(baseline.ComparePurchasePreview(future).Delta, Is.EqualTo(compared.Delta));
            Assert.That(evaluator.EvaluateCurrent().HandUnitOption, Is.EqualTo(baseline.Before.HandUnitOption));
            Assert.That(rng.CaptureState(), Is.EqualTo(checkpoint));
        }

        [Test]
        public void HandOptionRejectsForeignFailedAndNonObservationPreviewBases()
        {
            var a = Def("A");
            GameObservation Observation() => Build(Prep(), defs: new[] { a }, shop: CharacterShop("A"));
            var obs = Observation();
            var evaluator = new HandSynergyOptionEvaluator(obs);
            var action = new BuyCharacterAction(new ActionId("valid"), "offer", new GameActionLocation(CharacterLocationKind.Hand, 0));
            var foreign = new CharacterPurchasePreview(Observation()).Preview(action);
            var own = new CharacterPurchasePreview(obs);
            var failed = own.Preview(new BuyCharacterAction(new ActionId("missing"), "missing", action.Placement));
            Assert.That(failed.Succeeded, Is.False);
            Assert.Throws<ArgumentException>(() => evaluator.CreateBaseline().ComparePurchasePreview(foreign));
            Assert.Throws<ArgumentException>(() => evaluator.CreateBaseline().ComparePurchasePreview(failed));
            var state = new HandSynergyOptionState(obs.Player.Level, Array.Empty<UnitBoardMember>(), Array.Empty<HandSynergySlot>());
            Assert.Throws<InvalidOperationException>(() => evaluator.CreateBaseline(state).ComparePurchasePreview(own.Preview(action)));
        }

        [Test]
        public void PurchaseThatOnlyRaisesBoardStarConsumesPartOfExistingOption()
        {
            var members = UnitMoveBoardFixture.Board("...BAC.D.");
            var definitions = UnitMoveBoardFixture.Definitions("AB,AC,AD", members);
            var a = definitions.Single(d => d.DefinitionId == "A");
            var owned = members.Select(m => Inst(m.DefinitionId,
                definitions.Single(d => d.DefinitionId == m.DefinitionId))).ToList();
            var board = BoardState.CreateStandard();
            foreach (var m in members) board = Put(board, m.SlotIndex, m.DefinitionId);
            owned.Add(Inst("material", a));
            owned.Add(Inst("replacement", a, 3));
            var hand = Put(Put(HandState.CreateStandard(), 0, HandSlotContent.Character("material")),
                1, HandSlotContent.Character("replacement"));
            var obs = Build(Prep(), Econ(level: 4), board, hand, owned.ToArray(), definitions, CharacterShop("A"));
            var preview = new CharacterPurchasePreview(obs).Preview(new BuyCharacterAction(new ActionId("same-slot"),
                "offer", new GameActionLocation(CharacterLocationKind.Hand, 2)));
            Assert.That(preview.FinalLocation.Value.SlotIndex, Is.EqualTo(4));
            Assert.That(preview.FinalCharacter.Star, Is.EqualTo(2));
            var change = new HandSynergyOptionEvaluator(obs).CreateBaseline(true).ComparePurchasePreview(preview);
            Assert.That(change.Before.HandUnitOption, Is.EqualTo(2.4m));
            Assert.That(change.After.HandUnitOption, Is.EqualTo(1.2m));
            Assert.That(change.After.Details.CurrentSynergy.Value.UnitActivationValue, Is.EqualTo(16m));
            Assert.That(change.Delta, Is.EqualTo(-1.2m));
        }

        [Test]
        public void HandOptionIgnoresGoldCostAndRolledStats()
        {
            var a = Def("A", units: new[] { "AB" });
            var b = Def("B", units: new[] { "AB" });
            var board = Put(BoardState.CreateStandard(), 0, "a");
            var hand = Put(HandState.CreateStandard(), 0, HandSlotContent.Character("b"));
            var first = V12Fighter("a", a, 2, 2);
            var second = V12Fighter("b", b, 2, 2);
            var stronger = Inst("b", b, runCost: 3);
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(3,
                new[] { new CharacterStatOption(9, 8, 1) }, null, null) });
            Assert.That(stronger.EnsureStats(b, stats, new Pcg32RandomSource(14, 2)), Is.True);
            var low = Build(Prep(), Econ(gold: 1), board, hand, new[] { first, second }, new[] { a, b });
            var high = Build(Prep(), Econ(gold: 40), board, hand, new[] { first, stronger }, new[] { a, b });
            Assert.That(high.Hand.Slots[0].Character.RunCost, Is.Not.EqualTo(low.Hand.Slots[0].Character.RunCost));
            Assert.That(high.Hand.Slots[0].Character.Attack, Is.Not.EqualTo(low.Hand.Slots[0].Character.Attack));
            Assert.That(high.Hand.Slots[0].Character.Defense, Is.Not.EqualTo(low.Hand.Slots[0].Character.Defense));
            Assert.That(new HandSynergyOptionEvaluator(low).EvaluateCurrent().HandUnitOption, Is.EqualTo(1.5m));
            Assert.That(new HandSynergyOptionEvaluator(high).EvaluateCurrent().HandUnitOption, Is.EqualTo(1.5m));
        }

        [TestCase(3, 0, -515)]
        [TestCase(3, 150, 685)]
        [TestCase(4, 0, -2915)]
        [TestCase(4, 150, -1715)]
        public void HandOptionCapacityExamplesKeepExistingTerms(int occupancy, int optionCents, int scoreCents)
        {
            decimal capacity = occupancy == 3 ? -2m : -5m;
            Assert.That(8m * (1.4m + capacity + optionCents / 100m) - 0.35m, Is.EqualTo(scoreCents / 100m));
        }

        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(13)]
        [TestCase(14)]
        public void StandaloneHandOptionLeavesExistingStrategiesUnchanged(int version)
        {
            var a = Def("A", units: new[] { "AB" });
            var b = Def("B", units: new[] { "AB" });
            var observation = Build(Prep(), board: Put(BoardState.CreateStandard(), 0, "a"),
                hand: Put(HandState.CreateStandard(), 0, HandSlotContent.Character("b")),
                owned: new[] { V12Fighter("a", a, 2, 2), V12Fighter("b", b, 2, 2) }, defs: new[] { a, b });
            HeuristicStrategy Create() => version switch
            {
                7 => new HeuristicStrategy(), 8 => HeuristicStrategy.CreateV8(), 9 => HeuristicStrategy.CreateV9(),
                10 => HeuristicStrategy.CreateV10(), 11 => HeuristicStrategy.CreateV11(), 12 => HeuristicStrategy.CreateV12(),
                13 => HeuristicStrategy.CreateV13(), _ => HeuristicStrategy.CreateV14()
            };
            var strategy = Create();
            var rng = StrategyRandomSource.Create(123, strategy.StrategyId, strategy.Version).CaptureState();
            var expected = strategy.ChooseAction(observation, Context(strategy, rng: rng));
            Assert.That(new HandSynergyOptionEvaluator(observation).EvaluateCurrent().HandUnitOption, Is.EqualTo(1.5m));
            var other = Create();
            var actual = other.ChooseAction(observation, Context(other, rng: rng));
            Assert.That(SimulationStateFingerprint.Create(actual.Action), Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
            Assert.That(actual.StrategyRngAdvanced, Is.EqualTo(expected.StrategyRngAdvanced));
        }
    }
}
