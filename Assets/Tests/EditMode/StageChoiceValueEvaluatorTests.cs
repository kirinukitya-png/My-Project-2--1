using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class StageChoiceValueEvaluatorTests
    {
        [TestCase(1,1,30,7,1,0,2.2,1,0,0,.43375,1.4,-6.67875,0,StageChoice.SelectB)]
        [TestCase(2,1,30,7,2,0,8.7,2,1,0,1.48375,1.4,-1.42875,0,StageChoice.SelectA)]
        [TestCase(3,2,25,12,3,2,12.6,3,1,1,4.3025,.70,-8.06,5,StageChoice.SelectD)]
        [TestCase(4,2,15,12,4,1,13.8,4,0,1,-2.5725,.70,-15.435,15,StageChoice.SelectD)]
        [TestCase(5,3,5,20,5,4,27.2,5,1,2,3.0458333333,.70,-2,30,StageChoice.SelectD)]
        [TestCase(6,3,30,30,4,2,8.8,4,0,0,-12.5,.70,-24.7875,0,StageChoice.SelectB)]
        [TestCase(7,3,30,30,4,2,24.8,4,1,2,.70,.70,4.0125,0,StageChoice.SelectC)]
        [TestCase(8,3,30,2,3,2,8.8,3,0,0,-7.52,6.60,-23.3475,0,StageChoice.SelectB)]
        [TestCase(9,4,25,30,6,2,27.9,6,1,1,-.0083333333,.30,-7.8125,5,StageChoice.SelectD)]
        [TestCase(10,4,25,30,6,2,27.9,6,1,1,-.0083333333,.30,-7.8125,5,StageChoice.SelectD)]
        [TestCase(11,5,30,40,12,0,55,9,2,2,-2,0,-6,0,StageChoice.SelectB)]
        [TestCase(12,5,30,30,7,6,30.2,7,1,1,-2.8,0,-16.2,0,StageChoice.SelectB)]
        [TestCase(13,5,5,30,7,6,30.2,7,1,1,-5.1333333333,0,-29.7,0,StageChoice.SelectD)]
        [TestCase(14,3,30,30,4,3,17.4,4,0,0,.60,.70,-12.1125,0,StageChoice.SelectB)]
        [TestCase(14,3,30,30,4,3,17.4,4,2,2,5.20,.70,-3.1125,0,StageChoice.SelectA)]
        [TestCase(15,3,25,12,4,3,19.3,3,1,1,2.4108333333,.70,-11.16,5,StageChoice.SelectD)]
        public void D1ArithmeticUsesSyntheticBoardStrength(int fixture, int stage, int hp, int gold, int level, int xp,
            double power, int count, int units, int block, double a, double b, double c, double d, StageChoice winner)
        {
            var observation = Observe(stage * 5 - 1, hp, gold, level, xp, extraHand: fixture == 10 ? 5 : 0);
            var result = Synthetic(observation, power, count, units, block);
            Assert.That(result.RequiresLegacyFallback, Is.False);
            var expected = new[] { a, b, c, d };
            for (int i = 0; i < 4; i++)
                Assert.That(result.Choices[i].Score.Value, Is.EqualTo((decimal)expected[i]).Within(.000000001m));
            Assert.That(result.ConfirmedBest.First().Choice, Is.EqualTo(winner));
            Assert.That(result.ConfirmedBest.Count, Is.EqualTo(fixture == 11 || fixture == 12 ? 2 : 1));
        }

        [TestCase(7,1.40,.245,2.45,1.40,4.85875)]
        [TestCase(8,1.05,.28,2.10,1.40,4.6225)]
        [TestCase(9,1.05,.315,2.10,1.40,4.64875)]
        [TestCase(29,1.05,1.015,2.10,.70,5.17375)]
        [TestCase(30,1.05,1.05,2.10,.70,5.20)]
        [TestCase(31,1.05,1.05,2.10,.70,5.20)]
        public void InterestUsesPreRewardGoldAndOnlyHoldingValue(int gold, double aGold, double interest,
            double cGold, double discount, double score)
        {
            var observation = Observe(14, 30, gold, 4, 3);
            var result = Synthetic(observation, 24.8, 4, 1, 2);
            var a = result.Choices[0];
            var c = result.Choices[2];
            Assert.That(a.GoldReward, Is.EqualTo(3));
            Assert.That(observation.Stage.ChoiceDetails[0].ChoiceGoldReward, Is.Zero);
            Assert.That(a.GoldValue, Is.EqualTo((decimal)aGold));
            Assert.That(c.GoldValue, Is.EqualTo((decimal)cGold));
            Assert.That(a.InterestValue, Is.EqualTo((decimal)interest));
            Assert.That(c.InterestValue, Is.EqualTo((decimal)interest));
            Assert.That(a.AssumedPreRewardGold, Is.EqualTo(gold));
            Assert.That(a.InterestOutcomes.Sum(branch => branch.Probability * branch.Gold),
                Is.EqualTo(observation.EconomyRules.ExpectedInterestAtCurrentGold));
            Assert.That(a.InterestOutcomes.Sum(branch => branch.Probability), Is.EqualTo(1m));
            Assert.That(a.Score, Is.EqualTo((decimal)score));
            Assert.That(result.Choices[1].DiscountValue, Is.EqualTo((decimal)discount));
            Assert.That(result.Choices[1].InterestValue, Is.Zero);
        }

        [TestCase(0,false,false,0)]
        [TestCase(1,false,false,0)]
        [TestCase(2,true,false,6.60)]
        [TestCase(3,true,false,6.60)]
        [TestCase(4,true,true,1.40)]
        public void DiscountPurchaseUsesItsOwnAffordability(int gold, bool discounted, bool ordinary, double score)
        {
            var row = new StageChoiceValueEvaluator(Observe(4, 30, gold, 3, 2), 25).Evaluate().Choices[1];
            Assert.That(row.DiscountPrice.Value.CanBuy, Is.EqualTo(discounted));
            Assert.That(row.NormalPrice.Value.CanBuy, Is.EqualTo(ordinary));
            Assert.That(row.DiscountValue, Is.EqualTo((decimal)score));
        }

        [TestCase(1,38)]
        [TestCase(5,30)]
        [TestCase(10,20)]
        [TestCase(15,15)]
        [TestCase(20,10)]
        [TestCase(25,5)]
        [TestCase(29,1)]
        [TestCase(30,0)]
        public void HealingUsesExistingHpValue(int hp, int expected)
        {
            var d = new StageChoiceValueEvaluator(Observe(4, hp, 7, 1, 0), 25).Evaluate().Choices[3];
            Assert.That(d.HealingValue, Is.EqualTo(expected));
            Assert.That(d.Healing.EffectiveHealing, Is.EqualTo(Math.Min(10, 30 - hp)));
        }

        [Test]
        public void NonstandardMaximumAndCustomHpSettingsAreHonored()
        {
            var observation = Observe(4, 4, 7, 1, 0, maximumHp: 15);
            var settings = new StageChoiceValueSettings(hp: new HpValueSettings(2, 1, .5m));
            var d = new StageChoiceValueEvaluator(observation, 25, settings).Evaluate().Choices[3];
            Assert.That(d.Healing.MaximumHp, Is.EqualTo(15));
            Assert.That(d.HealingValue, Is.EqualTo(9m));
        }

        [TestCase(1,0,12,24,3,0)]
        [TestCase(4,2,1.50,10.20,5,1)]
        [TestCase(9,8,0,0,10,2)]
        [TestCase(11,10,0,0,12,0)]
        [TestCase(12,0,0,0,12,0)]
        public void GrowthRespectsLevelCurveCarryAndPhysicalCapacity(int level, int xp, double a, double c,
            int afterLevel, int afterXp)
        {
            var result = new StageChoiceValueEvaluator(Observe(4, 30, 12, level, xp), 25).Evaluate();
            Assert.That(result.Choices[0].ExperienceValue, Is.EqualTo((decimal)a));
            Assert.That(result.Choices[2].ExperienceValue, Is.EqualTo((decimal)c));
            Assert.That(result.Choices[2].Experience.Value.After.Level, Is.EqualTo(afterLevel));
            Assert.That(result.Choices[2].Experience.Value.After.XP, Is.EqualTo(afterXp));
        }

        [Test]
        public void RealPublicBoardUsesActivatableUnitsAndAttributeBlock()
        {
            var observation = Observe(14, 30, 30, 4, 3, layout: "AB.C.....", units: "AB,AC", shared: true, star: 1);
            Assert.That(observation.Synergy, Is.Null);
            var row = new StageChoiceValueEvaluator(observation, 25).Evaluate().Choices[0];
            Assert.That(row.Board.BoardCombat, Is.EqualTo(10.8m));
            Assert.That(row.Board.Synergy.Value.EstablishedUnitCount, Is.EqualTo(2));
            Assert.That(row.Board.ActivatableUnitCount, Is.EqualTo(1));
            Assert.That(row.Board.AttributeBlock, Is.EqualTo(1));
            Assert.That(row.Board.MissingSlots, Is.EqualTo(1));
            Assert.That(row.Q, Is.EqualTo(11.8m));
            Assert.That(new StageChoiceValueEvaluator(Observe(14, 30, 30, 1, 0, layout: "AB.C....."), 25)
                .Evaluate().Choices[0].Board.MissingSlots, Is.Zero);
            var full = new StageChoiceValueEvaluator(Observe(14, 30, 30, 3, 0, layout: "AB.C....."), 25).Evaluate().Choices[0];
            var shortOne = new StageChoiceValueEvaluator(Observe(14, 30, 30, 4, 0, layout: "AB.C....."), 25).Evaluate().Choices[0];
            Assert.That(shortOne.Q, Is.EqualTo(full.Q - 2));
        }

        [Test]
        public void RealBoardRetainsTemporaryStatsAndIgnoresUnplayedHandAndItems()
        {
            var a = new StageChoiceValueEvaluator(Observe(14, 25, 12, 4, 3, layout: "ABC......", temporaryAttack: 3), 25).Evaluate();
            var b = new StageChoiceValueEvaluator(Observe(14, 25, 12, 4, 3, layout: "ABC......", temporaryAttack: 3, extraHand: 5), 25).Evaluate();
            Assert.That(a.Choices[0].Board.BoardCombat, Is.EqualTo(19.8m));
            Assert.That(b.Choices.Select(row => row.Score), Is.EqualTo(a.Choices.Select(row => row.Score)));
            Assert.That(b.Choices.All(row => row.ShopStatus == ChoiceValueStatus.IntentionallyOmitted), Is.True);
        }

        [Test]
        public void TerminalLexicographicComparisonPrecedesScoresEvenWithZeroRisk()
        {
            var settings = new StageChoiceValueSettings(normalBaseRisk: 0, hardBaseRisk: 0, normalDeficitWeight: 0, hardDeficitWeight: 0);
            var obs = Observe(4, 30, 7, 1, 0);
            var terminal = new StageChoiceValueEvaluator(obs, 5, settings).Evaluate();
            Assert.That(terminal.Choices.All(row => row.Score == 0m), Is.True);
            Assert.That(terminal.ConfirmedBest.Select(row => row.Choice), Is.EqualTo(new[] { StageChoice.SelectB, StageChoice.SelectD }));
            Assert.That(terminal.Choices[1].DiscountValue, Is.GreaterThan(0));
            Assert.That(terminal.Choices[0].WeightedRewardValue, Is.Zero);
            Assert.That(new StageChoiceValueEvaluator(obs, 6, settings).Evaluate().Choices[0].IsFinalEncounter, Is.False);
            Assert.That(new StageChoiceValueEvaluator(obs, 4, settings).Evaluate().Failure, Is.EqualTo(ChoiceValueFailure.InvalidEncounterCount));
            Assert.That(new StageChoiceValueEvaluator(obs, null, settings).Evaluate().Failure, Is.EqualTo(ChoiceValueFailure.MissingEncounterCount));
        }

        [Test]
        public void TerminalSuppressesUnavailableReferenceComponentsOnly()
        {
            var observation = Observe(24, 5, 30, 7, 6);
            var evaluator = new StageChoiceValueEvaluator(observation, 25,
                new StageChoiceValueSettings(economy: EconomyValueSettings.TwoSegment(decimal.MaxValue, decimal.MaxValue, 8, 0)));
            var result = evaluator.Evaluate();
            Assert.That(result.Choices[0].RewardFailure, Is.EqualTo(ChoiceValueFailure.InvalidNumericInput));
            Assert.That(result.Choices[1].DiscountFailure, Is.EqualTo(ChoiceValueFailure.InvalidNumericInput));
            Assert.That(result.ConfirmedBest.Single().Choice, Is.EqualTo(StageChoice.SelectD));
            Assert.That(result.RequiresLegacyFallback, Is.False);
        }

        [Test]
        public void RankingHandlesAllNegativePartialAndMissingLowerTier()
        {
            var a = Candidate(StageChoice.SelectA, -8);
            var b = Candidate(StageChoice.SelectB, -2);
            var c = Candidate(StageChoice.SelectC, -2);
            Assert.That(Rank(c, b, a).ConfirmedBest.Select(row => row.Choice), Is.EqualTo(new[] { StageChoice.SelectB, StageChoice.SelectC }));
            var invalid = Candidate(StageChoice.SelectD, null);
            Assert.That(Rank(a, invalid).RequiresLegacyFallback, Is.True);
            Assert.That(Rank(a, invalid).ReferenceMaximum, Is.EqualTo(-8));
            Assert.That(Rank(invalid).ConfirmedBestScore, Is.Null);
            var low = Candidate(StageChoice.SelectA, null, ChoiceTerminalClass.CombatVictoryRequired);
            var high = Candidate(StageChoice.SelectB, 0, ChoiceTerminalClass.EnemylessCompletionOnSuccessfulResolution);
            Assert.That(Rank(low, high).ConfirmedBest.Single().Choice, Is.EqualTo(StageChoice.SelectB));
            Assert.That(Rank(Candidate(StageChoice.SelectA,-2,ChoiceTerminalClass.CombatVictoryRequired),
                Candidate(StageChoice.SelectC,-4,ChoiceTerminalClass.CombatVictoryRequired)).ConfirmedBest.Single().Choice,
                Is.EqualTo(StageChoice.SelectA));
        }

        [TestCase(0,-2)]
        [TestCase(.50,2.8)]
        [TestCase(.75,5.2)]
        [TestCase(1,7.6)]
        public void RewardWeightChangesOnlyRewardAggregation(double weight, double expected)
        {
            var result = Synthetic(Observe(14, 30, 30, 4, 3), 24.8, 4, 1, 2,
                new StageChoiceValueSettings(rewardWeight: (decimal)weight));
            Assert.That(result.Choices[0].Risk, Is.EqualTo(2));
            Assert.That(result.Choices[0].Score, Is.EqualTo((decimal)expected));
        }

        [Test]
        public void ExistingSettingsAreReusableAndInvalidSettingsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StageChoiceValueSettings(rewardWeight: 1.01m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StageChoiceValueSettings(stageStrengthSlope: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StageChoiceValueSettings(hardBaseRisk: -1));
            var settings = new StageChoiceValueSettings(economy: EconomyValueSettings.Linear(1),
                growth: new GrowthValueSettings(6, .50m, 0), activatableUnitWeight: 3, attributeBlockWeight: 2,
                missingBoardSlotPenalty: 4);
            var result = Synthetic(Observe(4, 30, 7, 2, 0), 8.7, 1, 1, 1, settings);
            Assert.That(result.Choices[0].Q, Is.EqualTo(9.7m));
            Assert.That(result.Choices[0].GoldValue, Is.EqualTo(3));
            Assert.That(result.Choices[0].ExperienceValue, Is.EqualTo(1.5m));
        }

        [Test]
        public void BossOverlapRequiresFallbackAndDoesNotApplyBDiscount()
        {
            var evaluated = new StageChoiceValueEvaluator(Observe(4, 30, 7, 1, 0, bossOverlap: true), 25).Evaluate();
            Assert.That(evaluated.RequiresLegacyFallback, Is.True);
            Assert.That(evaluated.Failure, Is.EqualTo(ChoiceValueFailure.BossOverride));
            Assert.That(evaluated.Choices.All(row => row.Status == ChoiceValueStatus.UnsupportedChoice && row.DiscountValue == null), Is.True);
            var preparation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(1));
            Assert.That(new StageChoiceValueEvaluator(preparation, 25).Evaluate().Failure, Is.EqualTo(ChoiceValueFailure.OutsideChoicePhase));
        }

        [Test]
        public void NormalPriceOverrideDoesNotRequireAnotherChoiceAndOptionsRemainPure()
        {
            var observation = Observe(4, 30, 4, 3, 2, layout: "A........");
            SetChoices(observation, StageChoice.SelectB);
            string fingerprint = SimulationStateFingerprint.Create(observation);
            var cached = new StageChoiceValueEvaluator(observation, 25, normalExperiencePurchasePrice: 5);
            var first = cached.Evaluate();
            var b = first.Choices.Single();
            Assert.That(b.NormalPrice.Value.Price, Is.EqualTo(5));
            Assert.That(b.NormalPrice.Value.CanBuy, Is.False);
            Assert.That(b.PurchaseExperience.Value.TotalDelta, Is.EqualTo(8));
            Assert.That(b.DiscountValue, Is.EqualTo(6.60m));
            Assert.That(cached.Evaluate(), Is.SameAs(first));
            var uncached = new StageChoiceValueEvaluator(observation, 25, normalExperiencePurchasePrice: 5, useCache: false, includeDetails: true);
            Assert.That(uncached.Evaluate().Choices.Select(row => row.Score),
                Is.EqualTo(first.Choices.Select(row => row.Score)));
            Assert.That(cached.BattleEvaluationRequests + uncached.BattleEvaluationRequests, Is.Zero);
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(fingerprint));
        }

        [Test]
        public void InvalidAvailabilityAndNoLegalChoicesHaveDistinctReasons()
        {
            var obs = Observe(4, 30, 4, 3, 2);
            SetChoices(obs, StageChoice.SelectA, StageChoice.SelectC);
            var terminal = new StageChoiceValueEvaluator(obs, 5).Evaluate();
            Assert.That(terminal.ConfirmedBest.Single().Choice, Is.EqualTo(StageChoice.SelectA));
            Assert.That(terminal.Choices.All(row => row.TerminalClass == ChoiceTerminalClass.CombatVictoryRequired), Is.True);
            typeof(StageObservation).GetField("<AvailableChoices>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(obs.Stage, new[] { StageChoice.SelectB });
            Assert.That(new StageChoiceValueEvaluator(obs, 5).Evaluate().Failure, Is.EqualTo(ChoiceValueFailure.InconsistentChoiceDetails));
            SetChoices(obs);
            Assert.That(new StageChoiceValueEvaluator(obs, 5).Evaluate().Failure, Is.EqualTo(ChoiceValueFailure.NoLegalChoices));
        }

        [Test]
        public void PartialMissingGrowthAndReversedChoiceDetailsAreDiagnosed()
        {
            var obs = Observe(4, 30, 4, 3, 2);
            var expected = new StageChoiceValueEvaluator(obs, 25).Evaluate();
            typeof(StageObservation).GetField("<ChoiceDetails>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(obs.Stage, obs.Stage.ChoiceDetails.Reverse().ToArray());
            var reversed = new StageChoiceValueEvaluator(obs, 25).Evaluate();
            Assert.That(reversed.Choices.Select(row => row.Score), Is.EqualTo(expected.Choices.Select(row => row.Score)));
            typeof(EconomyRulesObservation).GetField("<ExperienceCurve>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(obs.EconomyRules, null);
            var missing = new StageChoiceValueEvaluator(obs, 25).Evaluate();
            Assert.That(missing.RequiresLegacyFallback, Is.True);
            Assert.That(missing.Choices[0].RewardFailure, Is.EqualTo(ChoiceValueFailure.MissingGrowth));
            Assert.That(missing.Choices[0].ShopStatus, Is.EqualTo(ChoiceValueStatus.IntentionallyOmitted));
            Assert.That(missing.ConfirmedBest, Is.Empty);
            Assert.That(new StageChoiceValueEvaluator(obs, 5).Evaluate().ConfirmedBest, Has.Count.EqualTo(2));
        }

        private static StageChoiceEvaluation Synthetic(GameObservation observation, double power, int count, int units, int block,
            StageChoiceValueSettings settings = null)
        {
            var board = (ChoiceBoardStrength)Construct(typeof(ChoiceBoardStrength), (decimal)power, count,
                Math.Max(0, Math.Min(observation.Player.Level, 9) - count), units, block, null);
            return (StageChoiceEvaluation)typeof(StageChoiceValueEvaluator).GetMethod("EvaluateCore",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(new StageChoiceValueEvaluator(observation, 25, settings), new object[] { board });
        }

        private static StageChoiceValue Candidate(StageChoice choice, decimal? score, ChoiceTerminalClass? terminal = null)
        {
            var row = (StageChoiceValue)Construct(typeof(StageChoiceValue), choice, terminal.HasValue, null);
            typeof(StageChoiceValue).GetProperty(nameof(StageChoiceValue.Score)).SetValue(row, score);
            typeof(StageChoiceValue).GetProperty(nameof(StageChoiceValue.TerminalClass)).SetValue(row, terminal);
            if (!score.HasValue)
                typeof(StageChoiceValue).GetProperty(nameof(StageChoiceValue.Failure)).SetValue(row, ChoiceValueFailure.MissingGrowth);
            return row;
        }

        private static StageChoiceEvaluation Rank(params StageChoiceValue[] rows) =>
            (StageChoiceEvaluation)Construct(typeof(StageChoiceEvaluation), rows, ChoiceValueFailure.None);

        private static object Construct(Type type, params object[] arguments) =>
            Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null);

        internal static StageProgressConfig Stage(bool bossOverlap = false) =>
            new StageProgressConfig(25, 5, 5, bossOverlap ? 5 : 4, 10, 10, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        internal static StageProgressState Pending(int encounter, StageProgressConfig config) =>
            StageProgressRules.BeginCurrentEncounter((StageProgressState)Construct(typeof(StageProgressState),
                encounter, 10, StageProgressPhase.AwaitingEncounterStart, null, null, false), config).NextState;

        private static void SetChoices(GameObservation observation, params StageChoice[] choices)
        {
            var type = typeof(StageObservation);
            var details = observation.Stage.ChoiceDetails.Where(row => choices.Contains(row.Choice)).ToArray();
            type.GetField("<AvailableChoices>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(observation.Stage, choices);
            type.GetField("<ChoiceDetails>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(observation.Stage, details);
        }

        internal static GameObservation Observe(int index, int hp, int gold, int level, int xp, string layout = ".........",
            string units = "", bool shared = false, int star = 1, int extraHand = 0, bool bossOverlap = false,
            int maximumHp = 30, int temporaryAttack = 0)
        {
            var members = UnitMoveBoardFixture.Board(layout, star);
            var roster = UnitMoveBoardFixture.Definitions(units, members).Select(def =>
                new CharacterDefinition(def.DefinitionId, def.CharacterName, 1, shared ? new[] { "Shared" } : null, def.Units))
                .Append(new CharacterDefinition("Reserve", "Reserve", 1, null, null)).ToArray();
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1,
                new[] { new CharacterStatOption(3, 1, 1) }, new[] { new CharacterStatOption(3, 1, 1) }, null) });
            var rng = new SimulationRandomStreams(19).Stats;
            var owned = new List<CharacterInstance>();
            CharacterInstance Own(CharacterDefinition definition, int rank)
            {
                var character = new CharacterInstance("owned-" + owned.Count, definition, 1, rank);
                Assert.That(character.EnsureStats(definition, stats, rng), Is.True);
                owned.Add(character);
                return character;
            }
            var board = BoardState.CreateStandard();
            foreach (var member in members)
            {
                var fighter = Own(roster.Single(def => def.DefinitionId == member.DefinitionId), member.Star);
                fighter.TemporaryAttackBonus = temporaryAttack;
                Assert.That(board.TryWithCharacterAt(member.SlotIndex, fighter.InstanceId, out board), Is.True);
            }
            var item = new ItemDefinition("Item005","Tea",4,"", ItemEffectTarget.Player, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Damage, -4) });
            var hand = HandState.CreateStandard();
            for (int i = 0; i < extraHand; i++)
            {
                var content = i == 0 ? HandSlotContent.Item(item.DefinitionId) : HandSlotContent.Character(Own(roster.Last(), 1).InstanceId);
                Assert.That(hand.TryWithSlot(i, content, out hand), Is.True);
            }
            var config = Stage(bossOverlap);
            var result = ObservationBuilder.Build(new ObservationSource(Pending(index, config), config,
                new EconomyState(gold, xp, level, 12, gold, 0, 1, hp, maximumHp),
                new EconomyRulesConfiguration(4, 2, 1, Enumerable.Range(1, 11).ToArray(), 10, 1, 3, 3, 1, 0),
                board, hand, new CharacterOwnershipState(owned), roster, roster, roster, new[] { item },
                null, new ItemEffectState(), characterStatSettings: stats, stageRewardConfig: new StageRewardConfiguration(5, 2)));
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            return result.Observation;
        }
    }

    public sealed partial class GameSessionTests
    {
        [TestCase(StageChoice.SelectB)]
        [TestCase(StageChoice.SelectD)]
        public void FinalEnemylessChoiceCompletesThroughRealSessionResolution(StageChoice choice)
        {
            var stageConfig = StageChoiceValueEvaluatorTests.Stage();
            var configuration = ConfigurationWithStage(stageConfig);
            var original = GameSession.Create(configuration, 101).CurrentState;
            var pool = new CharacterPool(original.CharacterPool.Entries.Values.Select(entry => new CharacterPoolEntry(
                entry.DefinitionId, entry.RemainingCopies + entry.ReservedCopies, entry.InitialStockLimit)));
            var checkpoint = new ActionExecutionState(StageChoiceValueEvaluatorTests.Pending(24, stageConfig),
                original.Economy, pool, original.InstanceFactory, original.Board, original.Hand, original.Ownership,
                original.ItemEffects, original.RandomState);
            var session = GameSession.Restore(configuration, checkpoint);
            var observation = session.GetObservation().Observation;
            var evaluator = new StageChoiceValueEvaluator(observation, 25);
            Assert.That(evaluator.Evaluate().ConfirmedBest.Select(row => row.Choice), Does.Contain(choice));
            Assert.That(session.CaptureCheckpoint(), Is.SameAs(checkpoint));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(checkpoint.RandomState));
            Assert.That(session.ExecuteAction(new SelectStageChoiceAction(new ActionId("final-choice"), choice)).Succeeded, Is.True);
            Assert.That(session.CurrentState.Battle, Is.Null);
            Assert.That(session.ResolveEnemylessStage().Status, Is.EqualTo(StageSessionStatus.Completed));
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Completed));
        }
    }
}
