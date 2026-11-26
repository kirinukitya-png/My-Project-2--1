using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class GrowthValueEvaluatorTests
    {
        private static GrowthValueEvaluator Evaluator(decimal opportunity = 0, decimal fraction = .5m,
            decimal capacity = 12) => new GrowthValueEvaluator(
                new GrowthValueSettings(capacity, fraction, opportunity), new ExperienceCurve(Enumerable.Range(1, 11)));

        private static GrowthPlacement Placement(int slots, int occupied, params string[] hand) =>
            new GrowthPlacement(slots, Enumerable.Range(0, occupied).Select(i => "board" + i), hand);

        [TestCase(1, 12, 12)]
        [TestCase(2, 3, 9)]
        [TestCase(3, 2, 8)]
        [TestCase(4, 1.5, 7.5)]
        [TestCase(5, 1.2, 7.2)]
        [TestCase(6, 1, 7)]
        [TestCase(7, 0.8571428571428571, 6.857142857142857)]
        [TestCase(8, 0.75, 6.75)]
        [TestCase(9, 0, 0)]
        [TestCase(10, 0, 0)]
        [TestCase(11, 0, 0)]
        public void SceneGrowthAtEveryPurchasableLevel(int level, double fromZero, double beforeLevelUp)
        {
            var evaluator = Evaluator();
            var placement = Placement(9, 0);
            Assert.That(evaluator.CreateBaseline(level, 0, placement).CompareExperienceGain(1).TotalDelta,
                Is.EqualTo((decimal)fromZero).Within(.000000000001m));
            Assert.That(evaluator.CreateBaseline(level, level - 1, placement).CompareExperienceGain(1).TotalDelta,
                Is.EqualTo((decimal)beforeLevelUp).Within(.000000000001m));
        }

        [TestCase("A", 1, 0, 1, 0, 7, 4, 1200, -308, 892)]
        [TestCase("B", 1, 0, 1, 1, 7, 4, 1600, -308, 1292)]
        [TestCase("C", 4, 0, 4, 0, 20, 4, 150, -168, -18)]
        [TestCase("D", 4, 3, 4, 1, 20, 4, 1150, -168, 982)]
        [TestCase("E", 4, 3, 2, 1, 20, 4, 750, -168, 582)]
        [TestCase("F", 8, 7, 8, 1, 20, 4, 1075, -168, 907)]
        [TestCase("G", 9, 8, 9, 0, 40, 4, 0, -140, -140)]
        [TestCase("H", 10, 0, 9, 0, 40, 4, 0, -140, -140)]
        [TestCase("J", 4, 3, 5, 0, 4, 4, 750, -308, 442)]
        [TestCase("K", 4, 0, 4, 0, 3, 2, 150, -140, 10)]
        [TestCase("L", 4, 3, 4, -1, 20, 4, 750, -168, 582)]
        public void RepresentativeScores(string example, int level, int xp, int occupied, int hand, int gold,
            int price, int growthHundredths, int economyHundredths, int totalHundredths)
        {
            var placement = Placement(9, occupied, hand < 0 ? new[] { "board0" } :
                Enumerable.Range(0, hand).Select(i => "hand" + i).ToArray());
            var growth = Evaluator(4).CreateBaseline(level, xp, placement).CompareExperienceGain(1);
            var withoutOpportunity = Evaluator().CreateBaseline(level, xp, placement).CompareExperienceGain(1);
            var economy = new EconomyValueEvaluator(EconomyValueSettings.TwoSegment(.70m, .35m, 8, .70m),
                new EconomyInterestContext(10, 1, 3, example == "K" ? InterestRewardEligibility.Ineligible :
                    InterestRewardEligibility.Eligible)).Compare(gold, gold - price);
            Assert.That(growth.TotalDelta, Is.EqualTo(growthHundredths / 100m));
            Assert.That(economy.TotalDelta, Is.EqualTo(economyHundredths / 100m));
            Assert.That(growth.TotalDelta + economy.TotalDelta, Is.EqualTo(totalHundredths / 100m));
            Assert.That(withoutOpportunity.TotalDelta, Is.EqualTo(growth.TotalDelta - growth.DeltaOpportunity));
        }

        [Test]
        public void MaximumLevelPreventsPurchaseButIgnoresNaturalExperience()
        {
            var config = Rules(Enumerable.Range(1, 11).ToArray());
            var state = new EconomyState(20, 0, 12, 12, 20, 0, 12);
            Assert.That(EconomyRules.TryPurchaseExperience(state, config, false).Succeeded, Is.False);
            Assert.That(EconomyRules.TryAddExperience(state, config, 10).NextState, Is.SameAs(state));
            var growth = Evaluator().CreateBaseline(12, 0, Placement(9, 9)).CompareExperienceGain(10);
            Assert.That(growth.After.ProgressValue, Is.Zero);
            Assert.That(growth.TotalDelta, Is.Zero);
        }

        [Test]
        public void MultipleLevelUpsConsumeThresholdsAndValueOnlyRemainingProgress()
        {
            var result = Evaluator().CreateBaseline(3, 2, Placement(9, 3)).CompareExperienceGain(6);
            Assert.That((result.After.Level, result.After.XP), Is.EqualTo((5, 1)));
            Assert.That(result.DeltaCapacity, Is.EqualTo(24m));
            Assert.That(result.DeltaProgress, Is.EqualTo(-2.8m).Within(.000000000001m));
            Assert.That(result.TotalDelta, Is.EqualTo(21.2m));
        }

        [TestCase(3, 0, 0, 3, 0)]
        [TestCase(3, 0, 1, 3, 1)]
        [TestCase(3, 2, 1, 4, 0)]
        [TestCase(3, 2, 2, 4, 1)]
        [TestCase(3, 2, 6, 5, 1)]
        [TestCase(8, 7, 10, 10, 0)]
        [TestCase(11, 10, 1, 12, 0)]
        [TestCase(11, 10, int.MaxValue, 12, 0)]
        public void PreviewAndEconomyUseTheSameExperienceProgress(int level, int xp, int gain, int next, int remainder)
        {
            var config = Rules(Enumerable.Range(1, 11).ToArray(), gain);
            var state = new EconomyState(20, xp, level, 12, 20, xp, level);
            var preview = config.ExperienceCurve.Advance(level, xp, gain);
            var grant = EconomyRules.TryAddExperience(state, config, gain);
            var purchase = EconomyRules.TryPurchaseExperience(state, config, false);
            Assert.That((preview.AfterLevel, preview.AfterXP, preview.LevelsGained), Is.EqualTo((next, remainder, next - level)));
            Assert.That((grant.NextState.Level, grant.NextState.XP), Is.EqualTo((next, remainder)));
            Assert.That((purchase.NextState.Level, purchase.NextState.XP), Is.EqualTo((next, remainder)));
        }

        [Test]
        public void ObservationSharesAnImmutableNonSceneCurve()
        {
            var source = new[] { 2, 3, 5 };
            var rules = Rules(source, 4);
            source[1] = 99;
            var first = Observe(rules);
            var second = Observe(rules);
            Assert.That(first.EconomyRules.ExperienceCurve, Is.SameAs(second.EconomyRules.ExperienceCurve));
            Assert.That(first.EconomyRules.ExperienceCurve.Thresholds, Is.SameAs(rules.ExperienceToNextLevel));
            Assert.Throws<NotSupportedException>(() => ((IList<int>)rules.ExperienceToNextLevel)[1] = 99);
            Assert.That(first.Player.RequiredXP, Is.EqualTo(2));
            var result = GrowthValueEvaluator.CreateBaseline(new GrowthValueSettings(12, .5m, 4), first)
                .CompareExperienceGain(first.EconomyRules.ExperiencePerPurchase);
            Assert.That((result.After.Level, result.After.XP), Is.EqualTo((2, 2)));
            Assert.That(result.After.ProgressValue, Is.EqualTo(4m).Within(.000000000001m));
        }

        [Test]
        public void PublishedHandCountsCharactersButNotItems()
        {
            var definition = new CharacterDefinition("hand", "Hand", 1, Array.Empty<string>(), Array.Empty<string>());
            var instance = new CharacterInstance("unit", definition, 1);
            var item = new ItemDefinition("item", "Item", 0, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 1) });
            Assert.That(HandState.CreateStandard().TryWithSlot(0, HandSlotContent.Character("unit"), out var hand), Is.True);
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Item("item"), out var mixedHand), Is.True);
            var rules = Rules(new[] { 2, 3, 5 });
            var observation = Observe(rules, mixedHand, new[] { definition }, new[] { instance }, new[] { item });
            Assert.That(GrowthPlacement.FromObservation(observation).EligibleHandDefinitions, Is.EqualTo(1));
        }

        [Test]
        public void OpportunityDeduplicatesAndAccountsForExistingSpaceAndPhysicalCapacity()
        {
            var evaluator = Evaluator(4);
            var baseline = evaluator.CreateBaseline(4, 3, Placement(6, 3, "board0", "a", "a", "b", "c"));
            Assert.That(baseline.Before.UsableOpportunityCount, Is.EqualTo(1));
            Assert.That(baseline.CompareExperienceGain(6).DeltaOpportunity, Is.EqualTo(8m));
            Assert.That(evaluator.CreateBaseline(4, 3, Placement(6, 3, "a")).CompareExperienceGain(1).DeltaOpportunity, Is.Zero);
            Assert.That(evaluator.CreateBaseline(4, 3, Placement(6, 5, "a")).CompareExperienceGain(1).DeltaOpportunity, Is.Zero);
            Assert.That(evaluator.Evaluate(8, 0, Placement(6, 6, "a")).OpportunityValue, Is.Zero);
            Assert.That(evaluator.Evaluate(8, 0, Placement(6, 5, "a")).ProgressValue, Is.Zero);
            Assert.That(evaluator.Evaluate(4, 3, Placement(6, 3, "board0")).OpportunityValue, Is.Zero);
        }

        [Test]
        public void PlacementOrSaleConsumesAnUnusedOpportunity()
        {
            var baseline = Evaluator(4).CreateBaseline(5, 0, Placement(9, 4, "a"));
            var deployed = baseline.ComparePlacement(Placement(9, 5));
            var sold = baseline.ComparePlacement(Placement(9, 4));
            Assert.That(deployed.DeltaOpportunity, Is.EqualTo(-4m));
            Assert.That(sold.TotalDelta, Is.EqualTo(-4m));
            Assert.That(baseline.ComparePlacement(Placement(9, 4, "a")).TotalDelta, Is.Zero);
        }

        [Test]
        public void GrowthIsMonotoneAndDifferencesComposeForFixedPlacement()
        {
            var evaluator = Evaluator(4);
            var placement = Placement(9, 3, "a", "b");
            var before = evaluator.CreateBaseline(3, 2, placement);
            var middle = before.CompareExperienceGain(2);
            var end = evaluator.CreateBaseline(middle.After.Level, middle.After.XP, placement).CompareExperienceGain(4);
            Assert.That(middle.TotalDelta + end.TotalDelta, Is.EqualTo(before.CompareExperienceGain(6).TotalDelta));
            Assert.That(before.CompareExperienceGain(0).TotalDelta, Is.Zero);
            decimal previous = before.Before.TotalValue;
            for (int xp = 0; xp <= 70; xp++)
            {
                decimal value = before.CompareExperienceGain(xp).After.TotalValue;
                Assert.That(value, Is.GreaterThanOrEqualTo(previous));
                previous = value;
            }
        }

        [Test]
        public void ProgressEndpointsZeroCapacityAndMaximumBelowPhysicalLimit()
        {
            var placement = Placement(9, 4, "a");
            Assert.That(Evaluator(fraction: 0).CreateBaseline(4, 0, placement).CompareExperienceGain(1).TotalDelta, Is.Zero);
            Assert.That(Evaluator(fraction: 0).CreateBaseline(4, 3, placement).CompareExperienceGain(1).TotalDelta, Is.EqualTo(12m));
            Assert.That(Evaluator(fraction: 1).CreateBaseline(4, 3, placement).CompareExperienceGain(1).TotalDelta, Is.EqualTo(3m));
            Assert.That(Evaluator(fraction: 1).CreateBaseline(4, 0, placement).CompareExperienceGain(1).TotalDelta, Is.EqualTo(3m));
            var zero = Evaluator(4, capacity: 0).CreateBaseline(4, 3, placement).CompareExperienceGain(1);
            Assert.That((zero.DeltaCapacity, zero.DeltaProgress, zero.DeltaOpportunity), Is.EqualTo((0m, 0m, 4m)));
            var capped = new GrowthValueEvaluator(new GrowthValueSettings(12, .5m, 0), new ExperienceCurve(new[] { 2 }));
            Assert.That(capped.Evaluate(2, 0, placement).ProgressValue, Is.Zero);
        }

        [Test]
        public void InvalidInputsAndScoreOverflowAreRejected()
        {
            var evaluator = Evaluator();
            var placement = Placement(9, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExperienceCurve(new[] { 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GrowthValueSettings(-1, .5m, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GrowthValueSettings(12, -.1m, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GrowthValueSettings(12, 1.1m, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GrowthValueSettings(12, .5m, -4));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(0, 0, placement));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(13, 0, placement));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(4, -1, placement));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(4, 4, placement));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(12, 1, placement));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Curve.Advance(4, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Placement(-1, 0));
            Assert.Throws<ArgumentException>(() => Placement(4, 5));
            Assert.Throws<OverflowException>(() => Evaluator(capacity: decimal.MaxValue).Evaluate(2, 0, placement));
            var mismatch = new EconomyState(20, 0, 1, 3, 20, 0, 1);
            Assert.That(EconomyRules.TryPurchaseExperience(mismatch, Rules(new[] { 2 }), false).Succeeded, Is.False);
        }

        private static EconomyRulesConfiguration Rules(int[] curve, int purchaseAmount = 1) =>
            new EconomyRulesConfiguration(4, 2, purchaseAmount, curve, 10, 1, 3, 3, 1, 0);

        private static GameObservation Observe(EconomyRulesConfiguration rules, HandState hand = null,
            CharacterDefinition[] definitions = null, CharacterInstance[] owned = null, ItemDefinition[] items = null)
        {
            var stage = new StageProgressConfig(5, 5, 5, 4, 0, 0, 0, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var source = new ObservationSource(StageProgressRules.CreateInitialState(stage), stage,
                new EconomyState(10, 0, 1, rules.ExperienceCurve.MaximumLevel, 10, 0, 1), rules,
                BoardState.CreateStandard(), hand ?? HandState.CreateStandard(),
                new CharacterOwnershipState(owned ?? Array.Empty<CharacterInstance>()),
                definitions ?? Array.Empty<CharacterDefinition>(), Array.Empty<CharacterDefinition>(),
                Array.Empty<CharacterDefinition>(), items ?? Array.Empty<ItemDefinition>());
            var result = ObservationBuilder.Build(source);
            Assert.That(result.Succeeded, Is.True, string.Join(", ", result.Errors));
            return result.Observation;
        }
    }
}
