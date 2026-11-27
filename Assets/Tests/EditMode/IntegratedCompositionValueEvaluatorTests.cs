using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Strategy;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameObservationTests
    {
        [TestCase("AB,AC", "AB.C.....", 1, "", "", 20, 12)]
        [TestCase("AB,AC", "AB.C.....", 2, "", "", 20, 20)]
        [TestCase("ABC", "AB.......", 1, "", "C", 2, 0)]
        [TestCase("AB,BC,CD", "ABC..D...", 1, "", "", 30, 22)]
        [TestCase("AB,BC,CD", "BCDA.....", 1, "", "", 30, 14)]
        [TestCase("", "A.......B", 1, "AB", "", 10, 10)]
        [TestCase("", ".........", 1, "", "", 0, 0)]
        public void IntegratedSynergyUsesNewRulesAndKeepsFourCoreComponents(string tags, string board,
            int starA, string attributes, string excluded, int legacySynergy, int newSynergy)
        {
            var state = new HandSynergyOptionState(5, UnitMoveBoardFixture.Board(board, starA),
                Array.Empty<HandSynergySlot>());
            var observation = OptionObservation(tags, state, attributes, excluded, 2);
            var legacy = new CompositionValueEvaluator(observation, settings: HeuristicStrategy.CreateV13Settings(),
                handDuplicateWeight: 0.25).Baseline.Value;
            var integrated = new IntegratedCompositionValueEvaluator(observation,
                new IntegratedCompositionSettings(handOption: new HandSynergyOptionSettings(0m)));
            var value = integrated.Baseline.Value;
            Assert.That(legacy.BoardSynergy, Is.EqualTo(legacySynergy));
            Assert.That(value.BoardSynergy, Is.EqualTo(newSynergy));
            Assert.That(value.BoardCombat, Is.EqualTo((decimal)legacy.BoardCombat));
            Assert.That(value.HandReserve, Is.EqualTo((decimal)legacy.HandReserve));
            Assert.That(value.MergeProgress, Is.EqualTo((decimal)legacy.MergeProgress));
            Assert.That(value.Capacity, Is.EqualTo((decimal)legacy.Capacity));
            Assert.That(value.HandUnitOption, Is.Zero);
            Assert.That(integrated.CacheStatistics, Is.EqualTo((1, 0, 0)));
        }

        [TestCase(3, "AB.......", "C", 1, "ABC", "", 150)]
        [TestCase(3, "AB.......", "D", 1, "ABC", "", 0)]
        [TestCase(2, "A........", "B", 1, "ABC", "", 30)]
        [TestCase(2, "A........", "B", 1, "", "AB", 75)]
        [TestCase(4, "...BAC.D.", "A", 2, "AB,AC,AD", "", 120)]
        [TestCase(5, "ABCDE....", "X", 1, "ABC,AD,BE", "", 90)]
        public void IntegratedHandOptionMatchesStandalone(int level, string board, string hand, int star,
            string tags, string attributes, int cents)
        {
            var obs = OptionObservation(tags, OptionState(level, board, OptionHand(0, hand, star)), attributes);
            var independent = new HandSynergyOptionEvaluator(obs).EvaluateCurrent(true);
            var evaluator = new IntegratedCompositionValueEvaluator(obs, includeDetails: true);
            var value = evaluator.Baseline.Value;
            Assert.That(value.HandUnitOption, Is.EqualTo(cents / 100m));
            Assert.That(evaluator.Baseline.HandOption.Details.BestDelta, Is.EqualTo(independent.Details.BestDelta));
            Assert.That(new IntegratedCompositionValueEvaluator(obs, useCache: false).Baseline.Value, Is.EqualTo(value));
            Assert.That(evaluator.CacheStatistics.SynergyEvaluations,
                Is.EqualTo(1 + evaluator.Baseline.HandOption.Details.NewEvaluations));
        }

        [Test]
        public void IntegratedBoardMovesAndOptionRealizationHaveConsistentDeltas()
        {
            var state = new HandSynergyOptionState(4, UnitMoveBoardFixture.Board("AB.X.....", 2),
                new[] { OptionHand(0, "C") });
            var evaluator = new IntegratedCompositionValueEvaluator(OptionObservation("AB,AC", state, "BX", attributeCount: 2));
            var moved = evaluator.CompareBoardMoveOrSwap(0, 2);
            Assert.That(moved.Delta.BoardSynergy, Is.Zero);
            Assert.That(moved.Delta.HandUnitOption, Is.EqualTo(1.5m));
            Assert.That(moved.TotalDelta, Is.EqualTo(1.5m));
            var other = new HandSynergyOptionState(4, UnitMoveBoardFixture.Board(".BAX.....", 2), state.Hand);
            var reverse = new IntegratedCompositionValueEvaluator(OptionObservation("AB,AC", other, "BX", attributeCount: 2))
                .CompareBoardMoveOrSwap(2, 0);
            Assert.That(reverse.TotalDelta, Is.EqualTo(-moved.TotalDelta));
            var realized = new IntegratedCompositionValueEvaluator(OptionObservation("ABC",
                OptionState(3, "AB.......", OptionHand(0, "C")))).CompareHandToBoard(0, 2);
            Assert.That(realized.Delta.BoardSynergy, Is.EqualTo(10));
            Assert.That(realized.Delta.HandUnitOption, Is.EqualTo(-1.5m));
            Assert.That(realized.Delta.BoardSynergy + realized.Delta.HandUnitOption, Is.EqualTo(8.5m));
            Assert.That(realized.TotalDelta, Is.EqualTo(realized.Delta.TotalCompositionValue));
        }

        [Test]
        public void IntegratedSaleCanIncreaseActivationAndNestedMovesCanRepairLevel()
        {
            var obs = OptionObservation("ABC,AD,BE", OptionState(4, "ABCDE....", OptionHand(0, "X")));
            var evaluator = new IntegratedCompositionValueEvaluator(obs);
            Assert.That(evaluator.Baseline.OverLevelCount, Is.EqualTo(1));
            Assert.That(evaluator.Baseline.HandOption.Status, Is.EqualTo(HandSynergyOptionStatus.OverLevel));
            var sale = evaluator.CompareBoardRemoval(2);
            Assert.That(sale.Delta.Synergy.UnitProgressValue, Is.EqualTo(-2));
            Assert.That(sale.Delta.Synergy.UnitActivationValue, Is.EqualTo(8));
            Assert.That(sale.After.OverLevelCount, Is.Zero);
            var move = evaluator.CompareBoardToHand(2, 1);
            Assert.That(move.After.OverLevelCount, Is.Zero);
            Assert.That(move.After.Value.BoardSynergy, Is.EqualTo(sale.After.Value.BoardSynergy));
        }

        [Test]
        public void IntegratedSameDefinitionSwapSharesProjectedStateAndOnlyOptionChangesWithLevel()
        {
            var state = OptionState(4, "...BAC.D.", OptionHand(0, "A", 2));
            var evaluator = new IntegratedCompositionValueEvaluator(OptionObservation("AB,AC,AD", state));
            var swap = evaluator.CompareHandToBoard(0, 4);
            Assert.That(swap.Delta.BoardSynergy, Is.EqualTo(8m));
            Assert.That(swap.Delta.HandUnitOption, Is.EqualTo(-1.2m));
            Assert.That(evaluator.CompareBoardToHand(4, 0).After.Value, Is.EqualTo(swap.After.Value));
            var reverseState = new HandSynergyOptionState(4, UnitMoveBoardFixture.Board("...BAC.D.", 2),
                new[] { OptionHand(0, "A") });
            var reverse = new IntegratedCompositionValueEvaluator(OptionObservation("AB,AC,AD", reverseState))
                .CompareBoardToHand(4, 0);
            Assert.That(reverse.TotalDelta, Is.EqualTo(-swap.TotalDelta));
            Assert.Throws<ArgumentException>(() => evaluator.CompareHandToBoard(0, 5));
            var atLevel = new IntegratedCompositionValueEvaluator(OptionObservation("ABC",
                OptionState(2, "AB.......", OptionHand(0, "C"))));
            var level = atLevel.CompareLevel(3);
            Assert.That(level.Delta.BoardSynergy, Is.Zero);
            Assert.That(level.TotalDelta, Is.EqualTo(1.5m));
            Assert.Throws<ArgumentOutOfRangeException>(() => atLevel.CompareLevel(13));
        }

        [Test]
        public void IntegratedPositionOrderCanChangeActivationWithoutChangingEstablishedCount()
        {
            var a = new IntegratedCompositionValueEvaluator(OptionObservation("AB,BC,CD", OptionState(4, "ABC..D...")));
            var b = new IntegratedCompositionValueEvaluator(OptionObservation("AB,BC,CD", OptionState(4, "BCDA.....")));
            Assert.That(a.Baseline.Value.Synergy.UnitProgressValue, Is.EqualTo(b.Baseline.Value.Synergy.UnitProgressValue));
            Assert.That(a.Baseline.Value.BoardSynergy - b.Baseline.Value.BoardSynergy, Is.EqualTo(8m));
        }

        [Test]
        public void IntegratedPlacementCanTradeCurrentProgressForFutureOption()
        {
            var state = new HandSynergyOptionState(4, UnitMoveBoardFixture.Board("AB.X.....", 2),
                new[] { OptionHand(0, "C") });
            var evaluator = new IntegratedCompositionValueEvaluator(
                OptionObservation("AB,AC,AXZ", state, "BX", attributeCount: 2));
            var moved = evaluator.CompareBoardMoveOrSwap(0, 2);
            Assert.That(moved.Delta.BoardSynergy, Is.EqualTo(-2m));
            Assert.That(moved.Delta.HandUnitOption, Is.EqualTo(1.5m));
            Assert.That(moved.TotalDelta, Is.EqualTo(-0.5m));
        }

        [Test]
        public void HandStarCanChangeOptionWithoutChangingCurrentBoardSynergy()
        {
            var one = OptionState(4, "...BAC.D.", OptionHand(0, "A"));
            var three = OptionState(4, "...BAC.D.", OptionHand(0, "A", 3));
            var before = new IntegratedCompositionValueEvaluator(OptionObservation("AB,AC,AD", one)).Baseline;
            var after = new IntegratedCompositionValueEvaluator(OptionObservation("AB,AC,AD", three)).Baseline;
            Assert.That(after.Value.BoardSynergy, Is.EqualTo(before.Value.BoardSynergy));
            Assert.That(before.Value.HandUnitOption, Is.Zero);
            Assert.That(after.Value.HandUnitOption, Is.EqualTo(2.4m));
        }
    }

    public sealed partial class HeuristicStrategyTests
    {
        private static CompositionValueEvaluator LegacyIntegratedControl(GameObservation obs)
        {
            var rules = HeuristicStrategy.CreateV13Settings();
            return new CompositionValueEvaluator(obs, rules.CompositionHandReserveWeight, rules,
                rules.CompositionHandDuplicateWeight);
        }

        private static void AssertLegacyComponents(IntegratedCompositionValue actual, CompositionValue expected)
        {
            Assert.That(actual.BoardCombat, Is.EqualTo((decimal)expected.BoardCombat));
            Assert.That(actual.HandReserve, Is.EqualTo((decimal)expected.HandReserve));
            Assert.That(actual.MergeProgress, Is.EqualTo((decimal)expected.MergeProgress));
            Assert.That(actual.Capacity, Is.EqualTo((decimal)expected.Capacity));
        }

        [TestCase(3, false, -60, -515)]
        [TestCase(3, true, 90, 685)]
        [TestCase(4, false, -360, -2915)]
        [TestCase(4, true, -210, -1715)]
        public void IntegratedPurchaseUsesPublicPowerFourAndPreservesCapacity(int occupied, bool useful,
            int compositionCents, int scoreCents)
        {
            var a = Def("A", units: new[] { "ABC" });
            var b = Def("B", units: new[] { "ABC" });
            var c = Def("C", units: new[] { "ABC" });
            var d = Def("D");
            var filler = Def("X");
            var target = useful ? c : d;
            var board = Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b");
            var hand = HandState.CreateStandard();
            var owned = new List<CharacterInstance> { V12Fighter("a", a, 3, 1), V12Fighter("b", b, 3, 1) };
            for (int i = 0; i < occupied; i++)
            {
                var resident = Inst("filler-" + i, filler);
                owned.Add(resident);
                hand = Put(hand, i, HandSlotContent.Character(resident.InstanceId));
            }
            var defs = new[] { a, b, c, d, filler };
            var obs = Build(Prep(), Econ(gold: 40, level: 3), board, hand, owned.ToArray(), defs,
                CharacterShop(target.DefinitionId), characterStatSettings: Stats(new CharacterStatOption(4, 0, 1)));
            var action = new BuyCharacterAction(new ActionId("integrated-capacity"), "offer",
                new GameActionLocation(CharacterLocationKind.Hand, occupied));
            var evaluator = new IntegratedCompositionValueEvaluator(obs);
            var purchase = evaluator.ComparePurchase(action);
            Assert.That(purchase.Succeeded, Is.True, purchase.Failure.ToString());
            AssertLegacyComponents(purchase.After.Value.Value, LegacyIntegratedControl(obs).ComparePurchase(action).After.Value.Value);
            Assert.That(purchase.Delta.Value.HandReserve, Is.EqualTo(1.4m));
            Assert.That(purchase.TotalDelta, Is.EqualTo(compositionCents / 100m));
            Assert.That(8m * purchase.TotalDelta.Value - 0.35m, Is.EqualTo(scoreCents / 100m));
            Assert.That(purchase.Delta.Value.TotalCompositionValue, Is.EqualTo(purchase.TotalDelta));
            var purchased = V12Fighter("new", target, 4, 0);
            var afterObs = Build(Prep(), Econ(gold: 39, level: 3), board,
                Put(hand, occupied, HandSlotContent.Character("new")), owned.Append(purchased).ToArray(), defs);
            var sale = new IntegratedCompositionValueEvaluator(afterObs).CompareHandRemoval(occupied);
            Assert.That(sale.Before.Value, Is.EqualTo(purchase.After.Value.Value));
            Assert.That(sale.TotalDelta, Is.EqualTo(-purchase.TotalDelta));
            Assert.That(sale.After.Value, Is.EqualTo(evaluator.Baseline.Value));
        }

        [TestCase(0, 1, 1)]
        [TestCase(1, 4, 2)]
        [TestCase(2, 4, 3)]
        [TestCase(1, 0, 2)]
        public void IntegratedRealAndHypotheticalPurchasesUseTheSameFinalStructure(int mergeStages, int slot, int star)
        {
            var members = UnitMoveBoardFixture.Board(mergeStages == 0 ? "...B.C.D." : "...BAC.D.");
            var defs = UnitMoveBoardFixture.Definitions("AB,AC,AD", members);
            var a = defs.Single(d => d.DefinitionId == "A");
            var rng = new Pcg32RandomSource(3, 9);
            var stats = Stats(new CharacterStatOption(2, 2, 1));
            var owned = new List<CharacterInstance>();
            var board = BoardState.CreateStandard();
            foreach (var member in members)
            {
                var character = Inst(member.DefinitionId, defs.Single(d => d.DefinitionId == member.DefinitionId));
                if (mergeStages == 2 && member.DefinitionId == "A")
                    Assert.That(character.TrySetTemporaryStar(2, a, stats, rng), Is.True);
                owned.Add(character);
                board = Put(board, member.SlotIndex, character.InstanceId);
            }
            var hand = HandState.CreateStandard();
            int[] stars = mergeStages == 2 ? new[] { 1, 1, 2 } : mergeStages == 1 ? new[] { 1 } : Array.Empty<int>();
            for (int i = 0; i < stars.Length; i++)
            {
                var material = Inst("material-" + i, a, stars[i]);
                owned.Add(material);
                hand = Put(hand, i, HandSlotContent.Character(material.InstanceId));
            }
            var obs = Build(Prep(), Econ(level: 5), board, hand, owned.ToArray(), defs, CharacterShop("A"),
                characterStatSettings: stats);
            var beforeRng = rng.CaptureState();
            var evaluator = new IntegratedCompositionValueEvaluator(obs, includeDetails: true);
            var action = new BuyCharacterAction(new ActionId("integrated-buy"), "offer",
                new GameActionLocation(CharacterLocationKind.Board, slot));
            var real = evaluator.ComparePurchase(action);
            var future = evaluator.CompareHypotheticalPurchase(obs.SelectedRoster.Single(d => d.DefinitionId == "A"), 1, 1, 20,
                new CharacterLocation(CharacterLocationKind.Board, slot));
            Assert.That(real.Succeeded, Is.True, real.Failure.ToString());
            Assert.That(real.Preview.MergeStageCount, Is.EqualTo(mergeStages));
            Assert.That(real.Preview.FinalCharacter.Star, Is.EqualTo(star));
            Assert.That(real.Preview.FinalLocation.Value.SlotIndex, Is.EqualTo(slot));
            Assert.That(real.After.Value.Value, Is.EqualTo(future.After.Value.Value));
            Assert.That(real.Delta, Is.EqualTo(future.Delta));
            Assert.That(real.Delta.Value.TotalCompositionValue, Is.EqualTo(real.TotalDelta));
            if (slot == 4) Assert.That(real.Delta.Value.Synergy.UnitActivationValue, Is.EqualTo(8m));
            AssertLegacyComponents(real.After.Value.Value, LegacyIntegratedControl(obs).ComparePurchase(action).After.Value.Value);
            Assert.That(rng.CaptureState(), Is.EqualTo(beforeRng));
        }

        [Test]
        public void IntegratedPurchasePreservesUnavailableResultsAndRejectsForeignPreview()
        {
            var a = Def("A");
            var obs = Build(Prep(), defs: new[] { a }, shop: CharacterShop("A"));
            var other = Build(Prep(), defs: new[] { a }, shop: CharacterShop("A"));
            var evaluator = new IntegratedCompositionValueEvaluator(obs);
            var action = new BuyCharacterAction(new ActionId("option"), "offer", new GameActionLocation(CharacterLocationKind.Hand, 0));
            var result = evaluator.ComparePurchase(action);
            Assert.That(result.Failure, Is.EqualTo(PurchaseCompositionFailure.PublicStatsUnavailable));
            Assert.That(result.After, Is.Null);
            var failed = new CharacterPurchasePreview(obs).Preview(new BuyCharacterAction(new ActionId("missing"), "missing", action.Placement));
            Assert.That(evaluator.ComparePurchasePreview(failed).Failure, Is.EqualTo(PurchaseCompositionFailure.PreviewUnavailable));
            Assert.Throws<ArgumentException>(() => evaluator.ComparePurchasePreview(new CharacterPurchasePreview(other).Preview(action)));
        }

        [Test]
        public void IntegratedCacheKeepsItemOccupancyAndHandDiagnosticsSeparate()
        {
            var a = Def("A", units: new[] { "AB" });
            var b = Def("B", units: new[] { "AB" });
            var filler = V12Fillers();
            var hand = Put(Put(Put(HandState.CreateStandard(), 0, HandSlotContent.Character("b")), 1,
                HandSlotContent.Item(filler[0].DefinitionId)), 2, HandSlotContent.Item(filler[1].DefinitionId));
            var obs = Build(Prep(), board: Put(BoardState.CreateStandard(), 0, "a"), hand: hand,
                owned: new[] { V12Fighter("a", a, 2, 2), V12Fighter("b", b, 2, 2) }, defs: new[] { a, b }, items: filler);
            var evaluator = new IntegratedCompositionValueEvaluator(obs);
            var details = new IntegratedCompositionValueEvaluator(obs, includeDetails: true);
            Assert.That(evaluator.CompareHandItemAddition(3).TotalDelta, Is.EqualTo(-2m));
            Assert.That(evaluator.CacheStatistics.SynergyEvaluations,
                Is.EqualTo(1 + details.Baseline.HandOption.Details.NewEvaluations));
            Assert.That(evaluator.CacheStatistics.OptionCacheHits, Is.EqualTo(1));
            Assert.That(evaluator.CompareHandMoveOrSwap(0, 1).TotalDelta, Is.Zero);
            Assert.That(details.CompareHandMoveOrSwap(0, 1).After.HandOption.Details.BestHand.Value.SlotIndex, Is.EqualTo(1));
            Assert.That(details.CompareHandMoveOrSwap(1, 4).After.Value, Is.EqualTo(details.Baseline.Value));
            Assert.That(details.CompareHandRemoval(1).After.Value.HandUnitOption, Is.EqualTo(details.Baseline.Value.HandUnitOption));
            Assert.Throws<InvalidOperationException>(() => evaluator.CompareBoardToHand(0, 1));
        }

        [Test]
        public void IntegratedCombatUsesObservedStatsWithoutChangingStructuralValues()
        {
            var a = Def("A", units: new[] { "AB" });
            var b = Def("B", units: new[] { "AB" });
            var board = Put(BoardState.CreateStandard(), 0, "a");
            var hand = Put(HandState.CreateStandard(), 0, HandSlotContent.Character("b"));
            var reserve = V12Fighter("b", b, 2, 1);
            GameObservation Observation(int attack, int defense) => Build(Prep(), board: board, hand: hand,
                owned: new[] { V12Fighter("a", a, attack, defense), reserve }, defs: new[] { a, b });
            var before = new IntegratedCompositionValueEvaluator(Observation(2, 1)).Baseline.Value;
            var after = new IntegratedCompositionValueEvaluator(Observation(3, 2)).Baseline.Value;
            Assert.That(after.BoardCombat - before.BoardCombat, Is.EqualTo(1.6m));
            Assert.That(after.HandUnitOption, Is.EqualTo(before.HandUnitOption));
            Assert.That(after.BoardSynergy, Is.EqualTo(before.BoardSynergy));
        }

        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(13)]
        [TestCase(14)]
        public void IntegratedEvaluationLeavesLegacyScoresAndStrategyUnchanged(int version)
        {
            var a = Def("A", units: new[] { "AB" });
            var b = Def("B", units: new[] { "AB" });
            var obs = Build(Prep(), board: Put(BoardState.CreateStandard(), 0, "a"),
                hand: Put(HandState.CreateStandard(), 0, HandSlotContent.Character("b")),
                owned: new[] { V12Fighter("a", a, 2, 2), V12Fighter("b", b, 2, 2) }, defs: new[] { a, b });
            HeuristicStrategy Make() => version switch
            {
                7 => new HeuristicStrategy(), 8 => HeuristicStrategy.CreateV8(), 9 => HeuristicStrategy.CreateV9(),
                10 => HeuristicStrategy.CreateV10(), 11 => HeuristicStrategy.CreateV11(), 12 => HeuristicStrategy.CreateV12(),
                13 => HeuristicStrategy.CreateV13(), _ => HeuristicStrategy.CreateV14()
            };
            var strategy = Make();
            var rng = StrategyRandomSource.Create(123, strategy.StrategyId, strategy.Version).CaptureState();
            var observationFingerprint = SimulationStateFingerprint.Create(obs);
            var legacy = LegacyIntegratedControl(obs).Baseline.Value;
            var expected = strategy.ChooseAction(obs, Context(strategy, rng: rng));
            double score = V12Score<MoveHandToBoardAction>(strategy, obs, a2 => a2.BoardSlot == 1);
            new IntegratedCompositionValueEvaluator(obs).CompareHandToBoard(0, 1);
            var next = Make();
            var actual = next.ChooseAction(obs, Context(next, rng: rng));
            Assert.That((next.StrategyId, next.Version), Is.EqualTo((strategy.StrategyId, strategy.Version)));
            Assert.That(SimulationStateFingerprint.Create(actual.Action), Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
            Assert.That(actual.StrategyRngAdvanced, Is.EqualTo(expected.StrategyRngAdvanced));
            Assert.That(SimulationStateFingerprint.Create(obs), Is.EqualTo(observationFingerprint));
            Assert.That(LegacyIntegratedControl(obs).Baseline.Value, Is.EqualTo(legacy));
            Assert.That(V12Score<MoveHandToBoardAction>(next, obs, a2 => a2.BoardSlot == 1), Is.EqualTo(score));
        }
    }
}
