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
        private static GameObservation SynergyObservationFor(string tags, string layout, int star = 1,
            string exclude = "", int attributes = 0, bool handC = false, int level = 9)
        {
            var members = UnitMoveBoardFixture.Board(layout, star);
            var full = UnitMoveBoardFixture.Definitions(tags, members)
                .Select(d => new CharacterDefinition(d.DefinitionId, d.CharacterName, 1,
                    Enumerable.Range(0, attributes).Select(i => "Attribute" + i).ToArray(), d.Units)).ToArray();
            var selected = full.Where(d => !exclude.Contains(d.DefinitionId)).ToArray();
            var ids = new string[9];
            foreach (var member in members) ids[member.SlotIndex] = member.DefinitionId;
            var owned = members.Select(m => new CharacterInstance(m.DefinitionId,
                selected.Single(d => d.DefinitionId == m.DefinitionId), 1, m.Star)).ToList();
            var hand = HandState.CreateStandard();
            if (handC)
            {
                owned.Add(new CharacterInstance("C", selected.Single(d => d.DefinitionId == "C"), 1));
                Assert.That(hand.TryWithSlot(0, HandSlotContent.Character("C"), out hand), Is.True);
            }
            var config = StageConfig();
            var prep = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(config), config).NextState;
            var result = ObservationBuilder.Build(Source(prep,
                economy: new EconomyState(12, 0, level, 12, 12, 0, 1, 25, 30),
                board: new BoardState(3, 3, ids), hand: hand, ownership: new CharacterOwnershipState(owned),
                definitions: full, selected: selected, full: full,
                shop: new ShopState(ShopKind.Character, Array.Empty<ShopOffer>(), 0, 1, 1),
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty()));
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            return result.Observation;
        }

        [TestCase("A", "AB", "AB.......", 1, "", 0, false, 2, 8, 10)]
        [TestCase("B", "ABC", "ABC......", 1, "", 0, false, 4, 8, 12)]
        [TestCase("C", "AB,CD", "ABC..D...", 1, "", 0, false, 4, 16, 20)]
        [TestCase("D", "AB,AC", "AB.C.....", 1, "", 0, false, 4, 8, 12)]
        [TestCase("E", "ABC,ADE", "ABCD..E..", 1, "", 0, false, 8, 8, 16)]
        [TestCase("F", "ABC,AD", "ABCD.....", 1, "", 0, false, 6, 8, 14)]
        [TestCase("G", "AB,AC,AD,AE", ".B.CAD.E.", 3, "", 0, false, 8, 24, 32)]
        [TestCase("H", "AB,AC,AD", "...BAC.D.", 1, "", 0, false, 6, 8, 14)]
        [TestCase("I", "AB,AC,AD", "...BAC.D.", 2, "", 0, false, 6, 16, 22)]
        [TestCase("J", "AB,AC,AD", "...BAC.D.", 3, "", 0, false, 6, 24, 30)]
        [TestCase("K", "ABC", "AB......C", 1, "", 0, false, 2, 0, 2)]
        [TestCase("L", "ABC", "AB.......", 1, "", 0, true, 2, 0, 2)]
        [TestCase("M", "ABC", "AB.......", 1, "C", 0, false, 0, 0, 0)]
        [TestCase("N", "ABCDEF", "ABCDE....", 1, "", 0, false, 8, 0, 8)]
        [TestCase("O", "ABCDEF", "ABCDEF...", 1, "", 0, false, 10, 8, 18)]
        [TestCase("T", "", "A.......B", 1, "", 2, false, 0, 0, 10)]
        [TestCase("U", "ABC,ABD,ABE", "AB.......", 1, "CDE", 0, false, 0, 0, 0)]
        [TestCase("Empty", "", ".........", 1, "", 0, false, 0, 0, 0)]
        [TestCase("Diagonal", "AB", "A...B....", 1, "", 0, false, 0, 0, 0)]
        [TestCase("Other member bridge", "AB", "AXB......", 1, "", 0, false, 0, 0, 0)]
        [TestCase("Two components", "ABCD", "AB.....CD", 1, "", 0, false, 2, 0, 2)]
        [TestCase("7 members", "ABCDEFG", "ABCDEFG..", 1, "", 0, false, 12, 8, 20)]
        [TestCase("8 members", "ABCDEFGH", "ABCDEFGH.", 1, "", 0, false, 14, 8, 22)]
        [TestCase("9 members", "ABCDEFGHI", "ABCDEFGHI", 1, "", 0, false, 16, 8, 24)]
        [TestCase("Over physical capacity", "ABCDEFGHIJ", "ABCDEFGHI", 1, "", 0, false, 0, 0, 0)]
        public void SynergyScoresApprovedComponents(string example, string tags, string layout, int star,
            string exclude, int attributes, bool handC, int progress, int activation, int total)
        {
            var observation = SynergyObservationFor(tags, layout, star, exclude, attributes, handC);
            var evaluator = new SynergyValueEvaluator(observation);
            var result = evaluator.EvaluateCurrent(true);
            var light = evaluator.EvaluateCurrent();
            Assert.That(result.EstablishedAttributeCount, Is.EqualTo(observation.Synergy.AttributeBlockBonus), example);
            Assert.That(result.EstablishedUnitCount, Is.EqualTo(observation.Synergy.Units.Count), example);
            Assert.That(result.Value.AttributeValue, Is.EqualTo(5 * attributes), example);
            Assert.That(result.Value.UnitProgressValue, Is.EqualTo(progress), example);
            Assert.That(result.Value.UnitActivationValue, Is.EqualTo(activation), example);
            Assert.That(result.Value.TotalSynergyValue, Is.EqualTo(total), example);
            Assert.That(light.Value, Is.EqualTo(result.Value));
            Assert.That(light.Details, Is.Null);
            Assert.That(result.Details.UnitProgress.Sum(x => x.ProgressValue), Is.EqualTo(progress));
            Assert.That(result.Details.Round.UnusedUnits.Count, Is.EqualTo(result.CompetitionBlockedCount));
            if (example == "U") Assert.That(result.Details.TagsWithoutPublicRequirements,
                Is.EqualTo(new[] { "ABC", "ABD", "ABE" }));
        }

        [TestCase("A.......B")]
        [TestCase("A...B...C")]
        [TestCase("A.B...C.D")]
        public void AttributeCountDoesNotScaleWithMembers(string layout)
        {
            var observation = SynergyObservationFor("", layout, attributes: 1);
            var result = new SynergyValueEvaluator(observation).EvaluateCurrent(true);
            Assert.That(result.Value.AttributeValue, Is.EqualTo(5));
            Assert.That(result.Details.EstablishedAttributes, Is.EqualTo(new[] { "Attribute0" }));
        }

        [Test]
        public void SynergyProgressRemainsEligibleBelowRequiredLevel()
        {
            var evaluation = new SynergyValueEvaluator(SynergyObservationFor("ABCDEF", "AB.......", level: 2)).EvaluateCurrent(true);
            Assert.That(evaluation.EligibleUnitCount, Is.EqualTo(1));
            Assert.That(evaluation.Details.UnitProgress.Single().LargestConnectedCount, Is.EqualTo(2));
            Assert.That(evaluation.Value.UnitProgressValue, Is.EqualTo(2));
        }

        [Test]
        public void SynergyPlacementPUsesInverseDeltasAndSharesUnchangedBaseline()
        {
            var evaluator = new SynergyValueEvaluator(SynergyObservationFor("AB,BC,CD", "BCDA....."));
            var before = evaluator.CreateBaseline(true);
            var after = UnitMoveBoardFixture.Board("ABC..D...");
            var change = before.Compare(after);
            Assert.That(change.Before.Value.TotalSynergyValue, Is.EqualTo(14));
            Assert.That(change.After.Value.TotalSynergyValue, Is.EqualTo(22));
            Assert.That(change.After.Details.Round.Activations.Select(a => a.Unit.UnitName), Is.EqualTo(new[] { "AB", "CD" }));
            Assert.That(change.Delta.UnitProgressValue, Is.Zero);
            Assert.That(change.Delta.UnitActivationValue, Is.EqualTo(8));
            Assert.That(evaluator.CreateBaseline(after).Compare(UnitMoveBoardFixture.Board("BCDA....."))
                .Delta.TotalSynergyValue, Is.EqualTo(-8));
            Assert.That(before.Compare(UnitMoveBoardFixture.Board("BCDA.....").Reverse().ToArray()).After.Details,
                Is.SameAs(before.Before.Details));
            Assert.That(evaluator.Evaluate(after, true).Value, Is.EqualTo(change.After.Value));
        }

        [Test]
        public void SynergyPlacementQValuesBothActivationSizesEqually()
        {
            var evaluator = new SynergyValueEvaluator(SynergyObservationFor("ABC,AD", "ABCD....."));
            var change = evaluator.CreateBaseline(true).Compare(UnitMoveBoardFixture.Board("DAB..C..."));
            Assert.That(change.Before.Details.Round.Activations.Single().Unit.UnitName, Is.EqualTo("ABC"));
            Assert.That(change.After.Details.Round.Activations.Single().Unit.UnitName, Is.EqualTo("AD"));
            Assert.That(change.After.Value.TotalSynergyValue, Is.EqualTo(14));
            Assert.That(change.Delta.TotalSynergyValue, Is.Zero);
        }

        [Test]
        public void SynergyStarAndRemovalDeltasTelescope()
        {
            var evaluator = new SynergyValueEvaluator(SynergyObservationFor("AB,AC,AD", "...BAC.D."));
            var baseline = evaluator.CreateBaseline();
            var two = UnitMoveBoardFixture.Board("...BAC.D.", 2);
            var three = UnitMoveBoardFixture.Board("...BAC.D.", 3);
            var first = baseline.Compare(two);
            var second = evaluator.CreateBaseline(two).Compare(three);
            Assert.That(first.Delta.TotalSynergyValue, Is.EqualTo(8));
            Assert.That(second.Delta.TotalSynergyValue, Is.EqualTo(8));
            Assert.That(first.Delta.TotalSynergyValue + second.Delta.TotalSynergyValue,
                Is.EqualTo(baseline.Compare(three).Delta.TotalSynergyValue));
            var removal = baseline.Compare(UnitMoveBoardFixture.Board("...B.C.D."));
            Assert.That(removal.After.Value.TotalSynergyValue, Is.Zero);
            Assert.That(removal.Delta.TotalSynergyValue, Is.EqualTo(-14));
        }

        [Test]
        public void SynergySettingsAreIndependentAndValidateInputs()
        {
            var observation = SynergyObservationFor("AB", "AB.......", attributes: 1);
            Assert.That(new SynergyValueEvaluator(observation, new SynergyValueSettings(1, 3, 4))
                .EvaluateCurrent().Value.TotalSynergyValue, Is.EqualTo(8));
            var zero = new SynergyValueEvaluator(observation, new SynergyValueSettings(0, 0, 0)).EvaluateCurrent();
            Assert.That(zero.Value.TotalSynergyValue, Is.Zero);
            Assert.That(zero.ActivatableUnitCount, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SynergyValueSettings(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SynergyValueSettings(progressCoefficient: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SynergyValueSettings(activationCoefficient: -1));
            var evaluator = new SynergyValueEvaluator(observation);
            Assert.Throws<ArgumentNullException>(() => evaluator.Evaluate(null));
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(new UnitBoardMember[1]));
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(UnitMoveBoardFixture.Board("AC.......")));
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(new[] {
                new UnitBoardMember(0, "A", 1), new UnitBoardMember(0, "B", 1) }));
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(new[] {
                new UnitBoardMember(0, "A", 1), new UnitBoardMember(1, "A", 1) }));
        }
    }

    public sealed partial class HeuristicStrategyTests
    {
        [TestCase(false, 4, 2, 1, 22)]
        [TestCase(true, 4, 3, 2, 30)]
        [TestCase(false, 0, 2, 1, 10)]
        public void SynergyAppliesImmediateAndChainedPurchasePreview(bool chain, int targetSlot, int finalStar,
            int stages, int expected)
        {
            var definitions = UnitMoveBoardFixture.Definitions("AB,AC,AD", UnitMoveBoardFixture.Board("...BAC.D."));
            var a = definitions.Single(x => x.DefinitionId == "A");
            var rng = new Pcg32RandomSource(3, 9);
            var stats = Stats(new CharacterStatOption(2, 2, 1));
            var owned = new List<CharacterInstance>();
            var board = BoardState.CreateStandard();
            foreach (var member in UnitMoveBoardFixture.Board("...BAC.D.", chain ? 2 : 1))
            {
                var character = Inst(member.DefinitionId, definitions.Single(d => d.DefinitionId == member.DefinitionId), member.Star);
                if (chain && member.DefinitionId == "A")
                {
                    character = Inst("A", a);
                    Assert.That(character.TrySetTemporaryStar(2, a, stats, rng), Is.True);
                }
                owned.Add(character);
                board = Put(board, member.SlotIndex, character.InstanceId);
            }
            var hand = HandState.CreateStandard();
            int[] stars = chain ? new[] { 1, 1, 2 } : new[] { 1 };
            for (int i = 0; i < stars.Length; i++)
            {
                var material = Inst("material-" + i, a, stars[i]);
                owned.Add(material);
                hand = Put(hand, i, HandSlotContent.Character(material.InstanceId));
            }
            var observation = Build(Prep(), Econ(level: 5), board, hand, owned.ToArray(), definitions,
                CharacterShop("A"), characterStatSettings: stats);
            var evaluator = new SynergyValueEvaluator(observation);
            var baseline = evaluator.CreateBaseline(true);
            var previews = new CharacterPurchasePreview(observation);
            var randomBefore = rng.CaptureState();
            var preview = previews.Preview(new BuyCharacterAction(new ActionId("synergy-purchase"), "offer",
                new GameActionLocation(CharacterLocationKind.Board, targetSlot)));
            var hypothetical = previews.PreviewHypothetical(observation.SelectedRoster.Single(x => x.DefinitionId == "A"),
                1, 1, 20, new CharacterLocation(CharacterLocationKind.Board, targetSlot));
            Assert.That(preview.Succeeded, Is.True, preview.Failure.ToString());
            Assert.That(preview.MergeStageCount, Is.EqualTo(stages));
            Assert.That(preview.FinalCharacter.Star, Is.EqualTo(finalStar));
            Assert.That(preview.FinalLocation.Value.SlotIndex, Is.EqualTo(targetSlot));
            var result = baseline.ComparePurchasePreview(preview);
            Assert.That(result.After.Value.TotalSynergyValue, Is.EqualTo(expected));
            Assert.That(baseline.ComparePurchasePreview(hypothetical).After.Value, Is.EqualTo(result.After.Value));
            Assert.That(rng.CaptureState(), Is.EqualTo(randomBefore));
            Assert.That(evaluator.EvaluateCurrent().Value, Is.EqualTo(baseline.Before.Value));
            Assert.That(observation.Board.Slots[4].Character.Star, Is.EqualTo(chain ? 2 : 1));
        }

        [Test]
        public void SynergySameDefinitionExchangeReadsStar()
        {
            var defs = UnitMoveBoardFixture.Definitions("AB,AC,AD", UnitMoveBoardFixture.Board("...BAC.D."));
            var board = BoardState.CreateStandard();
            var members = UnitMoveBoardFixture.Board("...BAC.D.");
            foreach (var m in members) board = Put(board, m.SlotIndex, m.DefinitionId);
            var owned = defs.Select(d => V12Fighter(d.DefinitionId, d, 2, 3)).ToList();
            var handMember = Inst("reserve-A", defs.Single(d => d.DefinitionId == "A"), 2, 3);
            owned.Add(handMember);
            var hand = Put(HandState.CreateStandard(), 0, HandSlotContent.Character(handMember.InstanceId));
            var before = Build(Prep(), Econ(gold: 20, level: 5), board, hand, owned.ToArray(), defs);
            var afterBoard = Put(board, 4, handMember.InstanceId);
            var afterHand = Put(hand, 0, HandSlotContent.Character("A"));
            var after = Build(Prep(), Econ(gold: 50, level: 8), afterBoard, afterHand, owned.ToArray(), defs);
            var evaluator = new SynergyValueEvaluator(before);
            var change = evaluator.CreateBaseline().Compare(UnitMoveBoardFixture.Board("...BAC.D.", 2));
            Assert.That(change.Delta.TotalSynergyValue, Is.EqualTo(8));
            Assert.That(change.After.Value, Is.EqualTo(new SynergyValueEvaluator(after).EvaluateCurrent().Value));
        }

        [Test]
        public void SynergyIgnoresGoldXpRunCostAndRolledStats()
        {
            var a = Def("A", new[] { "Shared" }, new[] { "AB" });
            var b = Def("B", new[] { "Shared" }, new[] { "AB" });
            var board = Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b");
            var first = V12Fighter("a", a, 2, 1);
            var second = V12Fighter("b", b, 2, 1);
            var stronger = Inst("a", a, runCost: 3);
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(3,
                new[] { new CharacterStatOption(9, 8, 1) }, null, null) });
            var rng = new Pcg32RandomSource(14, 2);
            Assert.That(stronger.EnsureStats(a, stats, rng), Is.True);
            var before = Build(Prep(), Econ(gold: 8), board, owned: new[] { first, second }, defs: new[] { a, b });
            var after = Build(Prep(), new EconomyState(40, 2, 3, 12, 0, 1, 1, 20, 30),
                board, owned: new[] { stronger, second }, defs: new[] { a, b });
            Assert.That(after.Player.XP, Is.Not.EqualTo(before.Player.XP));
            Assert.That(after.Board.Slots[0].Character.RunCost, Is.Not.EqualTo(before.Board.Slots[0].Character.RunCost));
            Assert.That(after.Board.Slots[0].Character.Attack, Is.Not.EqualTo(before.Board.Slots[0].Character.Attack));
            Assert.That(after.Board.Slots[0].Character.Defense, Is.Not.EqualTo(before.Board.Slots[0].Character.Defense));
            Assert.That(new SynergyValueEvaluator(before).EvaluateCurrent().Value,
                Is.EqualTo(new SynergyValueEvaluator(after).EvaluateCurrent().Value));
        }

        [Test]
        public void SynergyHandOnlyPurchaseReusesBaselineAndRejectsForeignPreview()
        {
            var a = Def("A", units: new[] { "AB" });
            var b = Def("B", units: new[] { "AB" });
            var first = Inst("a", a, 1);
            var second = Inst("b", b, 1);
            var board = Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b");
            GameObservation Observation() => Build(Prep(), board: board, owned: new[] { first, second },
                defs: new[] { a, b }, shop: CharacterShop("A"));
            var observation = Observation();
            var baseline = new SynergyValueEvaluator(observation).CreateBaseline(true);
            var action = new BuyCharacterAction(new ActionId("hand"), "offer", new GameActionLocation(CharacterLocationKind.Hand, 0));
            var preview = new CharacterPurchasePreview(observation).Preview(action);
            Assert.That(baseline.ComparePurchasePreview(preview).Delta.TotalSynergyValue, Is.Zero);
            Assert.That(baseline.ComparePurchasePreview(preview).After.Details, Is.SameAs(baseline.Before.Details));
            Assert.Throws<ArgumentException>(() => baseline.ComparePurchasePreview(new CharacterPurchasePreview(Observation()).Preview(action)));
            var higherStar = baseline.Compare(UnitMoveBoardFixture.Board("AB.......", 2));
            Assert.That(higherStar.Delta.UnitActivationValue, Is.Zero);
        }

        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(13)]
        [TestCase(14)]
        public void StandaloneSynergyEvaluationLeavesStrategyDecisionUntouched(int version)
        {
            var observation = V14Observation(gold: 20, level: 9);
            HeuristicStrategy Create() => version switch
            {
                7 => new HeuristicStrategy(),
                8 => HeuristicStrategy.CreateV8(),
                9 => HeuristicStrategy.CreateV9(),
                10 => HeuristicStrategy.CreateV10(),
                11 => HeuristicStrategy.CreateV11(),
                12 => HeuristicStrategy.CreateV12(),
                13 => HeuristicStrategy.CreateV13(),
                _ => HeuristicStrategy.CreateV14()
            };
            var before = Create();
            var after = Create();
            var rng = StrategyRandomSource.Create(123, before.StrategyId, before.Version).CaptureState();
            var expected = before.ChooseAction(observation, Context(before, rng: rng));
            new SynergyValueEvaluator(observation).CreateBaseline(true);
            var actual = after.ChooseAction(observation, Context(after, rng: rng));
            Assert.That(SimulationStateFingerprint.Create(actual.Action), Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
            Assert.That(actual.StrategyRngAdvanced, Is.EqualTo(expected.StrategyRngAdvanced));
        }
    }
}
