using System;
using NUnit.Framework;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Tests
{
    public sealed class EconomyRulesTests
    {
        [Test]
        public void ExperiencePurchase_UsesNormalPriceAndLevelsUpAtThreshold()
        {
            EconomyState state = MakeState(gold: 10);
            EconomyActionResult result = EconomyRules.TryPurchaseExperience(state, MakeConfig(), false);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Gold, Is.EqualTo(6));
            Assert.That(result.NextState.XP, Is.Zero);
            Assert.That(result.NextState.Level, Is.EqualTo(2));
            Assert.That(result.ExperienceAdded, Is.EqualTo(1));
            Assert.That(result.LevelsGained, Is.EqualTo(1));
            Assert.That(state.Gold, Is.EqualTo(10));
        }

        [Test]
        public void ExperiencePurchase_SelectBDiscountIsExplicitAndCostsTwoGold()
        {
            EconomyActionResult result = EconomyRules.TryPurchaseExperience(MakeState(gold: 10), MakeConfig(), true);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Gold, Is.EqualTo(8));
        }

        [Test]
        public void ExperiencePurchase_InsufficientGoldAndMaximumLevelPreserveInputState()
        {
            EconomyState poorState = MakeState(gold: 3);
            EconomyActionResult poor = EconomyRules.TryPurchaseExperience(poorState, MakeConfig(), false);
            Assert.That(poor.Succeeded, Is.False);
            Assert.That(poor.FailureReason, Is.EqualTo(EconomyActionFailureReason.InsufficientGold));
            Assert.That(poor.NextState, Is.SameAs(poorState));

            EconomyState maxState = MakeState(gold: 20, level: 12, xp: 0);
            EconomyActionResult max = EconomyRules.TryPurchaseExperience(maxState, MakeConfig(), false);
            Assert.That(max.Succeeded, Is.False);
            Assert.That(max.FailureReason, Is.EqualTo(EconomyActionFailureReason.MaximumLevelReached));
            Assert.That(max.NextState, Is.SameAs(maxState));
        }

        [Test]
        public void ExperiencePurchase_CarriesExcessXpAndCanGainSeveralLevels()
        {
            EconomyRulesConfiguration config = MakeConfig(experiencePerPurchase: 20);
            EconomyActionResult result = EconomyRules.TryPurchaseExperience(MakeState(gold: 4), config, false);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Level, Is.EqualTo(6));
            Assert.That(result.NextState.XP, Is.EqualTo(5));
            Assert.That(result.LevelsGained, Is.EqualTo(5));
        }

        [Test]
        public void ExperiencePurchase_StopsAtMaximumLevelAndClearsExcessXp()
        {
            EconomyRulesConfiguration config = MakeConfig(experiencePerPurchase: 1000);
            EconomyActionResult result = EconomyRules.TryPurchaseExperience(MakeState(gold: 4), config, false);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Level, Is.EqualTo(12));
            Assert.That(result.NextState.XP, Is.Zero);
            Assert.That(result.LevelsGained, Is.EqualTo(11));
        }

        [Test]
        public void ExperienceGrant_UsesTheSameLevelCurveAndIsIgnoredAtMaximumLevel()
        {
            EconomyActionResult result = EconomyRules.TryAddExperience(
                MakeState(gold: 10), MakeConfig(), 5);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Gold, Is.EqualTo(10));
            Assert.That(result.NextState.Level, Is.EqualTo(3));
            Assert.That(result.NextState.XP, Is.EqualTo(2));
            Assert.That(result.LevelsGained, Is.EqualTo(2));

            EconomyState maxState = MakeState(gold: 10, level: 12, xp: 0);
            EconomyActionResult ignored = EconomyRules.TryAddExperience(maxState, MakeConfig(), 5);
            Assert.That(ignored.Succeeded, Is.True);
            Assert.That(ignored.NextState, Is.SameAs(maxState));
            Assert.That(ignored.ExperienceAdded, Is.Zero);
        }

        [Test]
        public void ExperiencePurchase_RejectsCurveThatDoesNotMatchMaximumLevel()
        {
            EconomyState state = new EconomyState(10, 0, 1, 4, 10, 0, 1);
            EconomyActionResult result = EconomyRules.TryPurchaseExperience(state, MakeConfig(), false);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(EconomyActionFailureReason.ExperienceCurveMismatch));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [TestCase(0, 0)]
        [TestCase(10, 1)]
        [TestCase(30, 3)]
        [TestCase(39, 3)]
        public void InterestCalculation_FixedAmountsMatchSceneRulesWithoutRandomDraw(int gold, int expected)
        {
            var random = new Pcg32RandomSource(100UL, 200UL);
            Pcg32State before = random.CaptureState();
            InterestCalculationResult result = EconomyRules.CalculateInterest(gold, MakeConfig(), random);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.GuaranteedInterest, Is.EqualTo(expected));
            Assert.That(result.RandomBonusInterest, Is.Zero);
            AssertStateEqual(before, result.NextRandomState);
            AssertStateEqual(before, random.CaptureState());
        }

        [TestCase(5, 0, 1)]
        [TestCase(19, 1, 1)]
        [TestCase(25, 2, 1)]
        public void InterestCalculation_FractionalAmountsReturnGuaranteedAndBoundedRandomBonus(
            int gold, int expectedGuaranteed, int maximumRandomBonus)
        {
            var random = new Pcg32RandomSource(300UL, 400UL);
            Pcg32State before = random.CaptureState();
            InterestCalculationResult result = EconomyRules.CalculateInterest(gold, MakeConfig(), random);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.GuaranteedInterest, Is.EqualTo(expectedGuaranteed));
            Assert.That(result.RandomBonusInterest, Is.InRange(0, maximumRandomBonus));
            Assert.That(result.TotalInterest, Is.EqualTo(expectedGuaranteed + result.RandomBonusInterest));
            AssertStateEqual(before, random.CaptureState());

            Pcg32RandomSource expectedRandom = Pcg32RandomSource.FromState(before);
            expectedRandom.NextUnitDouble();
            AssertStateEqual(expectedRandom.CaptureState(), result.NextRandomState);

            Pcg32RandomSource replayRandom = Pcg32RandomSource.FromState(before);
            InterestCalculationResult replay = EconomyRules.CalculateInterest(gold, MakeConfig(), replayRandom);
            Assert.That(replay.RandomBonusInterest, Is.EqualTo(result.RandomBonusInterest));
            AssertStateEqual(result.NextRandomState, replay.NextRandomState);
            AssertStateEqual(before, replayRandom.CaptureState());
        }

        [Test]
        public void InterestCalculation_SameSeedReproducesAndOnlyConsumesEconomyStream()
        {
            var first = new SimulationRandomStreams(500UL);
            var second = new SimulationRandomStreams(500UL);
            EconomyRules.CalculateInterest(19, MakeConfig(), first.Economy);
            EconomyRules.CalculateInterest(19, MakeConfig(), second.Economy);

            Assert.That(first.Economy.NextInt(0, int.MaxValue), Is.EqualTo(second.Economy.NextInt(0, int.MaxValue)));
            Assert.That(first.Shop.NextInt(0, int.MaxValue), Is.EqualTo(second.Shop.NextInt(0, int.MaxValue)));
        }

        [Test]
        public void InterestCalculation_RandomBonusFrequencyTracksRemainderProbability()
        {
            AssertBonusFrequency(5, 0.50, 0.04);
            AssertBonusFrequency(19, 0.90, 0.025);
            AssertBonusFrequency(25, 0.50, 0.04);
        }

        [Test]
        public void InterestCalculation_DoesNotDrawWhenRemainderIsZeroOrCapIsReached()
        {
            var atWholeThreshold = new Pcg32RandomSource(600UL, 700UL);
            Pcg32State beforeWhole = atWholeThreshold.CaptureState();
            EconomyRules.CalculateInterest(10, MakeConfig(), atWholeThreshold);
            AssertStateEqual(beforeWhole, atWholeThreshold.CaptureState());

            var atCap = new Pcg32RandomSource(600UL, 700UL);
            Pcg32State beforeCap = atCap.CaptureState();
            EconomyRules.CalculateInterest(39, MakeConfig(), atCap);
            AssertStateEqual(beforeCap, atCap.CaptureState());
        }

        [Test]
        public void InterestCalculation_InvalidInputDoesNotAdvanceRandomState()
        {
            var random = new Pcg32RandomSource(800UL, 900UL);
            Pcg32State before = random.CaptureState();
            InterestCalculationResult result = EconomyRules.CalculateInterest(-1, MakeConfig(), random);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(EconomyActionFailureReason.InvalidAmount));
            AssertStateEqual(before, result.NextRandomState);
            AssertStateEqual(before, random.CaptureState());
        }

        [Test]
        public void StageGoldReward_UsesPreRewardGoldAndOnlyCalculates()
        {
            var randomForStage = new Pcg32RandomSource(1000UL, 2000UL);
            var randomForReference = new Pcg32RandomSource(1000UL, 2000UL);
            Pcg32State before = randomForStage.CaptureState();
            InterestCalculationResult expectedInterest = EconomyRules.CalculateInterest(5, MakeConfig(), randomForReference);
            StageGoldRewardCalculationResult result = EconomyRules.CalculateStageGoldReward(
                5, 0, MakeConfig(), randomForStage);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.BaseGoldReward, Is.EqualTo(3));
            Assert.That(result.GuaranteedInterest, Is.EqualTo(expectedInterest.GuaranteedInterest));
            Assert.That(result.RandomBonusInterest, Is.EqualTo(expectedInterest.RandomBonusInterest));
            Assert.That(result.TotalGoldReward, Is.EqualTo(3 + expectedInterest.TotalInterest));
            Assert.That(result.EmptyHandSlotBonus, Is.Zero);
            AssertStateEqual(before, randomForStage.CaptureState());
            AssertStateEqual(expectedInterest.NextRandomState, result.NextRandomState);
        }

        [Test]
        public void StageGoldReward_UsesConfigurableEmptyHandFormulaAndSkipsWhenBaseRewardIsZero()
        {
            EconomyRulesConfiguration config = MakeConfig(emptySlotsPerBonus: 2, goldPerEmptySlotBonus: 3);
            var random = new Pcg32RandomSource(1100UL, 2200UL);
            StageGoldRewardCalculationResult result = EconomyRules.CalculateStageGoldReward(
                10, 5, config, random);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.EmptyHandSlotBonus, Is.EqualTo(6));
            Assert.That(result.TotalGoldReward, Is.EqualTo(10)); // 3 base + 1 interest + 6 hand bonus

            Pcg32State before = random.CaptureState();
            StageGoldRewardCalculationResult noReward = EconomyRules.CalculateStageGoldReward(
                0, 19, 5, config, random);
            Assert.That(noReward.Succeeded, Is.True);
            Assert.That(noReward.TotalGoldReward, Is.Zero);
            AssertStateEqual(before, noReward.NextRandomState);
            AssertStateEqual(before, random.CaptureState());
        }

        [Test]
        public void StageGoldReward_RejectsOverflowWithoutConsumingRandomState()
        {
            var random = new Pcg32RandomSource(1200UL, 2400UL);
            Pcg32State before = random.CaptureState();
            StageGoldRewardCalculationResult result = EconomyRules.CalculateStageGoldReward(
                3, int.MaxValue, 0, MakeConfig(), random);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(EconomyActionFailureReason.GoldOverflow));
            AssertStateEqual(before, result.NextRandomState);
            AssertStateEqual(before, random.CaptureState());
        }

        [Test]
        public void GoldActions_AddSpendRejectNegativeInsufficientAndOverflowWithoutMutation()
        {
            EconomyState state = MakeState(gold: 5);
            EconomyActionResult added = EconomyRules.TryAddGold(state, 4);
            Assert.That(added.Succeeded, Is.True);
            Assert.That(added.NextState.Gold, Is.EqualTo(9));

            EconomyActionResult spent = EconomyRules.TrySpendGold(state, 5);
            Assert.That(spent.Succeeded, Is.True);
            Assert.That(spent.NextState.Gold, Is.Zero);

            AssertFailurePreservesState(EconomyRules.TryAddGold(state, -1), state,
                EconomyActionFailureReason.InvalidAmount);
            AssertFailurePreservesState(EconomyRules.TrySpendGold(state, -1), state,
                EconomyActionFailureReason.InvalidAmount);
            AssertFailurePreservesState(EconomyRules.TrySpendGold(state, 6), state,
                EconomyActionFailureReason.InsufficientGold);

            EconomyState nearLimit = MakeState(gold: int.MaxValue);
            AssertFailurePreservesState(EconomyRules.TryAddGold(nearLimit, 1), nearLimit,
                EconomyActionFailureReason.GoldOverflow);
            Assert.That(state.Gold, Is.EqualTo(5));
        }

        private static EconomyRulesConfiguration MakeConfig(int experiencePerPurchase = 1,
            int emptySlotsPerBonus = 1, int goldPerEmptySlotBonus = 0)
        {
            return new EconomyRulesConfiguration(4, 2, experiencePerPurchase,
                new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
                10, 1, 3, 3, emptySlotsPerBonus, goldPerEmptySlotBonus);
        }

        private static EconomyState MakeState(int gold = 10, int xp = 0, int level = 1)
        {
            return new EconomyState(gold, xp, level, 12, 7, 0, 1);
        }

        private static void AssertFailurePreservesState(EconomyActionResult result,
            EconomyState state, EconomyActionFailureReason reason)
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(reason));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        private static void AssertStateEqual(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        private static void AssertBonusFrequency(int gold, double expectedRate, double tolerance)
        {
            const int sampleCount = 5000;
            int bonusCount = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                var random = new Pcg32RandomSource((ulong)i + 1UL, 987654321UL);
                if (EconomyRules.CalculateInterest(gold, MakeConfig(), random).RandomBonusInterest > 0)
                    bonusCount++;
            }
            double observedRate = (double)bonusCount / sampleCount;
            Assert.That(observedRate, Is.EqualTo(expectedRate).Within(tolerance));
        }
    }
}
