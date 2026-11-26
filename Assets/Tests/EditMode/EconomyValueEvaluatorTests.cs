using System;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class EconomyValueEvaluatorTests
    {
        private static EconomyValueEvaluator Evaluator(decimal weight = .70m,
            InterestRewardEligibility eligibility = InterestRewardEligibility.Eligible) =>
            new EconomyValueEvaluator(EconomyValueSettings.TwoSegment(.70m, .35m, 8, weight),
                new EconomyInterestContext(10, 1, 3, eligibility));

        [TestCase(0, 0, 0)]
        [TestCase(3, 210, 231)]
        [TestCase(8, 560, 616)]
        [TestCase(10, 630, 700)]
        [TestCase(20, 980, 1120)]
        [TestCase(29, 1295, 1498)]
        [TestCase(30, 1330, 1540)]
        [TestCase(31, 1365, 1575)]
        [TestCase(40, 1680, 1890)]
        [TestCase(100, 3780, 3990)]
        public void SceneValues(int gold, int holdingHundredths, int totalHundredths)
        {
            var result = Evaluator().Evaluate(gold);
            Assert.That(result.HoldingValue, Is.EqualTo(holdingHundredths / 100m));
            Assert.That(result.TotalValue, Is.EqualTo(totalHundredths / 100m));
            Assert.That(result.ExpectedInterestGold, Is.EqualTo(Math.Min(gold, 30) / 10m));
            Assert.That(result.WeightedInterestValue, Is.EqualTo(result.ExpectedInterestGold * .70m));
        }

        [TestCase(8, 7, -70, -77)]
        [TestCase(9, 8, -35, -42)]
        [TestCase(10, 6, -210, -238)]
        [TestCase(12, 11, -35, -42)]
        [TestCase(12, 8, -140, -168)]
        [TestCase(31, 30, -35, -35)]
        [TestCase(30, 29, -35, -42)]
        [TestCase(32, 27, -175, -196)]
        [TestCase(40, 5, -1330, -1505)]
        [TestCase(3, 30, 1120, 1309)]
        [TestCase(0, 45, 1855, 2065)]
        public void SceneDeltasAndTheirReverse(int before, int after, int holdingHundredths, int totalHundredths)
        {
            var evaluator = Evaluator();
            var result = evaluator.Compare(before, after);
            var reverse = evaluator.Compare(after, before);
            Assert.That(result.HoldingDelta, Is.EqualTo(holdingHundredths / 100m));
            Assert.That(result.TotalDelta, Is.EqualTo(totalHundredths / 100m));
            Assert.That(Evaluator(0).Compare(before, after).TotalDelta, Is.EqualTo(result.HoldingDelta));
            Assert.That(result.ExpectedInterestGoldDelta, Is.EqualTo((Math.Min(after, 30) - Math.Min(before, 30)) / 10m));
            Assert.That(result.WeightedInterestDelta, Is.EqualTo(.70m * result.ExpectedInterestGoldDelta));
            Assert.That(reverse.TotalDelta, Is.EqualTo(-result.TotalDelta));
            Assert.That(reverse.HoldingDelta, Is.EqualTo(-result.HoldingDelta));
        }

        [Test]
        public void IncreasingValueAndPathIndependenceAcrossBoundaries()
        {
            var evaluator = Evaluator();
            Assert.That(evaluator.Evaluate(0).TotalValue, Is.Zero);
            Assert.That(evaluator.Compare(8, 8).TotalDelta, Is.Zero);
            Assert.That(evaluator.Compare(7, 8).HoldingDelta, Is.EqualTo(.70m));
            Assert.That(evaluator.Compare(8, 9).HoldingDelta, Is.EqualTo(.35m));
            foreach (int threshold in new[] { 10, 20 })
            {
                Assert.That(evaluator.Compare(threshold - 1, threshold).ExpectedInterestGoldDelta, Is.EqualTo(.1m));
                Assert.That(evaluator.Compare(threshold, threshold + 1).ExpectedInterestGoldDelta, Is.EqualTo(.1m));
            }
            Assert.That(evaluator.Compare(30, 31).ExpectedInterestGoldDelta, Is.Zero);
            Assert.That(evaluator.Compare(30, 31).HoldingDelta, Is.EqualTo(.35m));
            var sum = evaluator.Compare(40, 30).TotalDelta + evaluator.Compare(30, 8).TotalDelta
                + evaluator.Compare(8, 5).TotalDelta;
            Assert.That(sum, Is.EqualTo(evaluator.Compare(40, 5).TotalDelta));
            Assert.That(sum, Is.Negative);
            Assert.That(evaluator.Compare(5, 40).TotalDelta, Is.Positive);
        }

        [Test]
        public void DegenerateTwoSegmentSettingsMatchTheCorrespondingLinearModels()
        {
            var interest = Evaluator().Interest;
            var linear = new EconomyValueEvaluator(EconomyValueSettings.Linear(.35m), interest);
            var zeroReserve = new EconomyValueEvaluator(EconomyValueSettings.TwoSegment(.70m, .35m, 0), interest);
            var equalRates = new EconomyValueEvaluator(EconomyValueSettings.TwoSegment(.35m, .35m, 8), interest);
            foreach (int gold in new[] { 0, 3, 8, 12, 100 })
            {
                Assert.That(linear.Evaluate(gold).TotalValue, Is.EqualTo(.35m * gold));
                Assert.That(zeroReserve.Evaluate(gold).TotalValue, Is.EqualTo(linear.Evaluate(gold).TotalValue));
                Assert.That(equalRates.Evaluate(gold).TotalValue, Is.EqualTo(linear.Evaluate(gold).TotalValue));
            }
        }

        [Test]
        public void ZeroWeightPreservesExpectedGoldWithoutAddingScore()
        {
            var value = Evaluator(0).Evaluate(10);
            Assert.That(value.ExpectedInterestGold, Is.EqualTo(1m));
            Assert.That(value.WeightedInterestValue, Is.Zero);
            Assert.That(value.TotalValue, Is.EqualTo(value.HoldingValue));
        }

        [TestCase(InterestRewardEligibility.Ineligible)]
        [TestCase(InterestRewardEligibility.Unknown)]
        public void UnavailableInterestDoesNotAddScore(InterestRewardEligibility eligibility)
        {
            var result = Evaluator(eligibility: eligibility).Compare(8, 9);
            Assert.That(result.Eligibility, Is.EqualTo(eligibility));
            Assert.That(result.After.ExpectedInterestGold, Is.Zero);
            Assert.That(result.WeightedInterestDelta, Is.Zero);
            Assert.That(result.TotalDelta, Is.EqualTo(.35m));
        }

        [TestCase(10, 1, 3)]
        [TestCase(7, 2, 5)]
        [TestCase(7, 0, 5)]
        [TestCase(7, 2, 0)]
        public void InterestUsesThePublicRulesAtEveryCandidateBalance(int threshold, int rate, int cap)
        {
            var rules = Rules(threshold, rate, cap);
            var config = Config();
            var context = Context(Begin(config), config, rules);
            Assert.That(context.GoldPerThreshold, Is.EqualTo(threshold));
            Assert.That(context.InterestPerThreshold, Is.EqualTo(rate));
            Assert.That(context.MaximumInterestGold, Is.EqualTo(cap));
            var evaluator = new EconomyValueEvaluator(EconomyValueSettings.Linear(.35m, .70m), context);
            foreach (int gold in new[] { 0, 6, 7, 14, 15, 20, 21, 22, 29, 30, 31, 100, int.MaxValue })
                Assert.That(evaluator.Evaluate(gold).ExpectedInterestGold,
                    Is.EqualTo(EconomyRules.CalculateExpectedInterestGold(gold, rules)));
        }

        [TestCase(StageChoice.SelectA, 0, InterestRewardEligibility.Eligible)]
        [TestCase(StageChoice.SelectC, 0, InterestRewardEligibility.Eligible)]
        [TestCase(StageChoice.SelectB, 0, InterestRewardEligibility.Ineligible)]
        [TestCase(StageChoice.SelectD, 0, InterestRewardEligibility.Ineligible)]
        [TestCase(StageChoice.SelectB, 2, InterestRewardEligibility.Eligible)]
        [TestCase(StageChoice.SelectD, 2, InterestRewardEligibility.Eligible)]
        public void SelectedStageUsesBaseRewardInsteadOfChoiceName(StageChoice choice, int enemylessGold,
            InterestRewardEligibility expected)
        {
            var config = Config(enemylessGold);
            var stage = StageSelectionRules.Select(ChoiceStage(config), config, choice).NextState;
            Assert.That(Context(stage, config).Eligibility, Is.EqualTo(expected));
        }

        [TestCase(5, InterestRewardEligibility.Eligible)]
        [TestCase(0, InterestRewardEligibility.Ineligible)]
        public void BossUsesOverrideBeforeAnyRewardIsPending(int bossGold, InterestRewardEligibility expected)
        {
            var config = Config();
            var boss = Begin(config);
            for (int i = 1; i < 4; i++)
                boss = StageProgressRules.BeginCurrentEncounter(CompleteWin(boss, config), config).NextState;
            Assert.That(Context(boss, config, Rules(baseGold: bossGold == 0 ? 3 : 0), bossGold).Eligibility,
                Is.EqualTo(expected));
        }

        [Test]
        public void UnselectedEncounterHasUnknownEligibility()
        {
            var config = Config();
            Assert.That(Context(ChoiceStage(config), config).Eligibility, Is.EqualTo(InterestRewardEligibility.Unknown));
        }

        [Test]
        public void DefeatAndFinalRewardDoNotCarryInterest()
        {
            var config = Config();
            var lost = StageProgressRules.ResolveBattle(StageProgressRules.BeginBattle(Begin(config)).NextState, config, false).NextState;
            var final = StageSelectionRules.Select(ChoiceStage(config), config, StageChoice.SelectC).NextState;
            var completed = CompleteWin(final, config);
            Assert.That(Context(lost, config, playerHp: 0).Eligibility, Is.EqualTo(InterestRewardEligibility.Ineligible));
            Assert.That(Context(completed, config).Eligibility, Is.EqualTo(InterestRewardEligibility.Ineligible));
        }

        [Test]
        public void BaselineReusesBeforeAndAppliesTheSameCostAcrossActions()
        {
            var evaluator = Evaluator();
            var baseline = evaluator.CreateBaseline(10);
            // Synthetic equal costs for Character, XP, reroll and Item. Actual prices come from their rules.
            foreach (int cost in new[] { 4, 4, 4, 4 })
            {
                var result = baseline.CompareGoldChange(-cost);
                Assert.That(result.Before, Is.EqualTo(baseline.Before));
                Assert.That(result.TotalDelta, Is.EqualTo(-2.38m));
                Assert.That(evaluator.CreateBaseline(6).CompareGoldChange(cost).TotalDelta, Is.EqualTo(-result.TotalDelta));
            }
            Assert.That(baseline.Compare(9).TotalDelta, Is.EqualTo(evaluator.Compare(10, 9).TotalDelta));
        }

        [Test]
        public void InvalidSettingsAndInputsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EconomyValueSettings.Linear(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EconomyValueSettings.TwoSegment(.35m, .70m, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => EconomyValueSettings.TwoSegment(.70m, .35m, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => EconomyValueSettings.Linear(.35m, -.70m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyValueSettings((GoldHoldingModel)99, 1, 1, 0, 0));
            Assert.Throws<ArgumentException>(() => new EconomyValueSettings(GoldHoldingModel.Linear, 1, 1, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyInterestContext(0, 1, 3, InterestRewardEligibility.Eligible));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyInterestContext(10, -1, 3, InterestRewardEligibility.Eligible));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyInterestContext(10, 1, -3, InterestRewardEligibility.Eligible));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyInterestContext(10, 1, 3, (InterestRewardEligibility)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => Evaluator().Evaluate(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Evaluator().CreateBaseline(2).CompareGoldChange(-3));
        }

        [Test]
        public void GoldAndScoreOverflowAreRejected()
        {
            var evaluator = Evaluator();
            Assert.Throws<OverflowException>(() => evaluator.CreateBaseline(int.MaxValue).CompareGoldChange(1));
            var largeHolding = new EconomyValueEvaluator(EconomyValueSettings.Linear(decimal.MaxValue), evaluator.Interest);
            Assert.Throws<OverflowException>(() => largeHolding.Evaluate(2));
            var largeInterest = new EconomyValueEvaluator(EconomyValueSettings.Linear(1, decimal.MaxValue), evaluator.Interest);
            Assert.Throws<OverflowException>(() => largeInterest.Evaluate(20));
            Assert.Throws<OverflowException>(() => largeInterest.Evaluate(10));
        }

        private static StageProgressConfig Config(int enemylessGold = 0) =>
            new StageProgressConfig(5, 5, 5, 4, 0, 0, 0, 1, 0, enemylessGold, 3, 6, 0,
                enemylessGold, 10, 4, 2, true);

        private static EconomyRulesConfiguration Rules(int threshold = 10, int rate = 1, int cap = 3,
            int baseGold = 3) =>
            new EconomyRulesConfiguration(4, 2, 1, new[] { 1 }, threshold, rate, cap, baseGold, 1, 0);

        private static StageProgressState Begin(StageProgressConfig config) =>
            StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(config), config).NextState;

        private static StageProgressState CompleteWin(StageProgressState preparation, StageProgressConfig config)
        {
            var battle = StageProgressRules.BeginBattle(preparation).NextState;
            var pending = StageProgressRules.ResolveBattle(battle, config, true).NextState;
            return StageProgressRules.CompletePendingReward(pending, config, default).NextState;
        }

        private static StageProgressState ChoiceStage(StageProgressConfig config)
        {
            var stage = Begin(config);
            for (int i = 1; i <= 4; i++)
                stage = StageProgressRules.BeginCurrentEncounter(CompleteWin(stage, config), config).NextState;
            return stage;
        }

        private static EconomyInterestContext Context(StageProgressState stage, StageProgressConfig config,
            EconomyRulesConfiguration rules = null, int bossGold = 5, int playerHp = 30)
        {
            ShopState shop = null;
            if (stage.Phase == StageProgressPhase.Preparation)
            {
                var kind = stage.CurrentSelection.ShopMode == StageShopMode.Item ? ShopKind.Item : ShopKind.Character;
                shop = new ShopState(kind, Array.Empty<ShopOffer>(), 0, config.GetStageNumber(stage.EncounterIndex),
                    config.GetRoundNumber(stage.EncounterIndex));
            }

            var source = new ObservationSource(stage, config, new EconomyState(12, 0, 1, 2, 12, 0, 1, playerHp, 30),
                rules ?? Rules(), BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), Array.Empty<CharacterDefinition>(),
                Array.Empty<CharacterDefinition>(), Array.Empty<CharacterDefinition>(), Array.Empty<ItemDefinition>(),
                shop: shop, itemEffects: new ItemEffectState(), enemyDefinitions: Array.Empty<EnemyDefinition>(),
                enemyBoard: EnemyBoardState.CreateEmpty(), stageRewardConfig: new StageRewardConfiguration(bossGold, 2));
            var result = ObservationBuilder.Build(source);
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            return EconomyInterestObservation.FromObservation(result.Observation);
        }
    }
}
