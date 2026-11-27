using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;
using ProjectNST.SimulationExport;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class HeuristicStrategyTests
    {
        private static EnemyDefinition V16Enemy(int attack = 6, int defense = 2, int hp = 20, string target = "T001") =>
            new EnemyDefinition("v16", "v16", attack, defense, hp, null, null, null, target, new[] { target }, null, null);

        private static HeuristicV16Inspection V16Inspect(GameObservation observation, HeuristicStrategy strategy = null,
            bool cache = true) => (strategy ?? HeuristicStrategy.CreateV16()).InspectV16Preparation(observation,
                new ActionId("v16"), cache);

        private static void V16Equivalent(GameObservation observation, HeuristicStrategy v16)
        {
            var v15 = HeuristicStrategy.CreateV15();
            var before = v15.GetEvaluations(observation, new ActionId("compare"));
            var after = v16.GetEvaluations(observation, new ActionId("compare"));
            Assert.That(after.Select(r => (SimulationStateFingerprint.Create(r.Action), r.Score)),
                Is.EqualTo(before.Select(r => (SimulationStateFingerprint.Create(r.Action), r.Score))));
            var a = v15.ChooseAction(observation, Context(v15));
            var b = v16.ChooseAction(observation, Context(v16));
            Assert.That(SimulationStateFingerprint.Create(b.Action), Is.EqualTo(SimulationStateFingerprint.Create(a.Action)));
            Assert.That(b.NextState.Payload, Is.EqualTo(a.NextState.Payload));
            Assert.That(b.NextStrategyRngState, Is.EqualTo(a.NextStrategyRngState));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void V16ZeroWeightOrEnemylessPreservesAllV15Scores(bool enemyless)
        {
            var a = Def("a");
            var b = Def("b");
            var board = Put(BoardState.CreateStandard(), 0, "a");
            var hand = Put(HandState.CreateStandard(), 0, HandSlotContent.Character("b"));
            var obs = V14Observation(gold: 40, level: 1, choice: enemyless ? StageChoice.SelectD : StageChoice.SelectA,
                board: board, hand: hand, defs: new[] { a, b }, owned: new[] { V12Fighter("a", a, 2, 2), V12Fighter("b", b, 3, 2) },
                offers: new[] { OfferV14("b", "b") }, stats: Stats(new CharacterStatOption(3, 2, 1)),
                battleEnemy: enemyless ? null : V16Enemy());
            var strategy = HeuristicStrategy.CreateV16(new HeuristicV16Settings(battle: new BattleValueSettings(enemyless ? .10m : 0m)));
            V16Equivalent(obs, strategy);
            var result = V16Inspect(obs, strategy);
            Assert.That(result.Evaluations, Is.Zero);
            Assert.That(result.RequiresFallback, Is.False);
            Assert.That(result.Candidates.All(row => row.UsedScore == row.OriginalScore), Is.True);
        }

        [Test]
        public void V16SelectsDefensePlacementWithZeroLegacyScore()
        {
            var a = Def("a");
            var b = Def("b");
            var c = Def("c");
            var board = Put(Put(Put(BoardState.CreateStandard(), 1, "a"), 5, "b"), 0, "c");
            var obs = V14Observation(gold: 0, level: 3, board: board, defs: new[] { a, b, c },
                owned: new[] { V12Fighter("a", a, 1, 6), V12Fighter("b", b, 1, 1), V12Fighter("c", c, 1, 1) },
                battleEnemy: V16Enemy(8, 0, target: "T005"),
                battleTargets: new[] { EnemyTargetProbabilityEvaluatorTests.Definition(5) });
            var strategy = HeuristicStrategy.CreateV16();
            var result = V16Inspect(obs, strategy);
            var row = result.Candidates.Single(r => r.Action is MoveOrSwapBoardAction move && move.SourceSlot == 1 && move.TargetSlot == 2);
            Assert.That(row.OriginalScore, Is.Zero);
            Assert.That(row.DeltaCorrection, Is.EqualTo(.36m));
            Assert.That(row.UsedScore, Is.EqualTo(2.88m));
            Assert.That(strategy.ChooseAction(obs, Context(strategy)).Action, Is.TypeOf<MoveOrSwapBoardAction>());
            var uncached = V16Inspect(obs, strategy, cache: false);
            Assert.That(uncached.Candidates.Select(r => r.UsedScore), Is.EqualTo(result.Candidates.Select(r => r.UsedScore)));
        }

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(3, true)]
        public void V16PurchaseUsesSameLegacyAndFinalBoardForMerge(int materials, bool toHand)
        {
            var a = Def("a");
            var options = new[] { new CharacterStatOption(3, 2, 1) };
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1, options,
                new[] { new CharacterStatOption(6, 2, 1) }, new[] { new CharacterStatOption(10, 2, 1) }) });
            var hand = HandState.CreateStandard();
            var board = BoardState.CreateStandard();
            var owned = new System.Collections.Generic.List<CharacterInstance>();
            if (materials != 0)
            {
                board = Put(board, 0, "a");
                owned.Add(V12Fighter("a", a, 3, 2));
                for (int i = 0; i < materials; i++)
                {
                    var material = Inst("m" + i, a, i == 0 ? 1 : 2);
                    hand = Put(hand, i, HandSlotContent.Character(material.InstanceId));
                    owned.Add(material);
                }
            }
            var obs = V14Observation(gold: 40, board: board, hand: hand, defs: new[] { a }, owned: owned.ToArray(),
                offers: new[] { OfferV14("offer", "a") }, stats: stats, battleEnemy: V16Enemy());
            var kind = toHand ? CharacterLocationKind.Hand : CharacterLocationKind.Board;
            var row = V16Inspect(obs).Candidates.First(r => r.Action is BuyCharacterAction buy &&
                buy.Placement.Kind == kind && r.Status == "Scored");
            var old = HeuristicStrategy.CreateV15().InspectV15Preparation(obs, new ActionId("v16"))
                .Single(r => SimulationStateFingerprint.Create(r.Action) == SimulationStateFingerprint.Create(row.Action));
            Assert.That(row.OriginalScore, Is.EqualTo(old.Score));
            Assert.That(row.UsedScore, Is.EqualTo(row.OriginalScore + row.BattleContribution));
            Assert.That(row.DeltaCorrection, Is.EqualTo(materials == 0 && toHand ? 0m : materials > 0 ? 0m : -.2m));
            var eval = BattleValueEvaluator.FromObservation(obs);
            var baseline = eval.EvaluateCurrent();
            var real = eval.ComparePurchase(baseline, (BuyCharacterAction)row.Action);
            var spec = eval.CompareHypotheticalPurchase(baseline, obs.SelectedRoster[0], 1, 1, 39,
                ((BuyCharacterAction)row.Action).Placement.ToCharacterLocation());
            Assert.That(spec.DeltaCorrection, Is.EqualTo(real.DeltaCorrection));
            Assert.That(row.DeltaCorrection, Is.EqualTo(real.DeltaCorrection));
        }

        [Test]
        public void V16LateFutureOfferFailureRestoresAllOriginalValues()
        {
            var a = Def("a");
            var b = Def("b");
            var z = Def("z");
            var zero = new[] { new CharacterStatOption(0, 2, 1) };
            var positive = new[] { new CharacterStatOption(3, 2, 1) };
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1, zero, zero, zero),
                new CharacterCostStats(2, positive, positive, positive) });
            var obs = V14Observation(gold: 40, level: 3, board: Put(BoardState.CreateStandard(), 0, "a"),
                defs: new[] { a, b, z }, owned: new[] { V12Fighter("a", a, 0, 2) },
                offers: new[] { OfferV14("b", "b") }, stats: stats, quotas: new[] { 2, 1, 0, 0, 0 },
                weights: new[] { 1, 1, 0, 0, 0 }, battleEnemy: V16Enemy(defense: 0), enemyCount: 2);
            var strategy = HeuristicStrategy.CreateV16(new HeuristicV16Settings(battle: new BattleValueSettings(allyAttackMaxStates: 1)));
            var result = V16Inspect(obs, strategy);
            Assert.That(result.RequiresFallback, Is.True);
            Assert.That(result.Failures.Single().Source, Is.EqualTo(BattleValueCandidateSource.FutureShop));
            Assert.That(result.Failures.Single().CandidateId, Does.StartWith("z/"));
            Assert.That(result.Failures.Single().Status, Is.EqualTo(BattleValueStatus.AttackStateLimitExceeded));
            var reroll = result.Candidates.Single(r => r.Action is RerollShopAction);
            var old = HeuristicStrategy.CreateV15().InspectV15Preparation(obs, new ActionId("v16")).Single(r => r.Action is RerollShopAction);
            Assert.That(reroll.CorrectedShop, Is.Null);
            Assert.That(reroll.OriginalShop.CurrentOpportunity, Is.EqualTo(old.Shop.CurrentOpportunity));
            Assert.That(reroll.OriginalShop.ExpectedFutureOpportunity, Is.EqualTo(old.Shop.ExpectedFutureOpportunity));
            Assert.That(reroll.UsedScore, Is.EqualTo(old.Score));
            V16Equivalent(obs, strategy);
        }

        [Test]
        public void V16MissingTargetFallsBackButForcedEmptyBoardDoesNotEvaluateBattle()
        {
            var a = Def("a");
            var obs = V14Observation(gold: 40, defs: new[] { a }, offers: new[] { OfferV14("buy", "a") },
                stats: Stats(new CharacterStatOption(4, 2, 1)), battleEnemy: V16Enemy(),
                battleTargets: Array.Empty<BattleTargetDefinition>());
            string trace = null;
            var strategy = HeuristicStrategy.CreateV16(decisionDiagnosticSink: value => trace = value);
            var inspected = V16Inspect(obs, strategy);
            Assert.That(inspected.RequiresFallback, Is.True);
            Assert.That(inspected.Failures.Single().Status, Is.EqualTo(BattleValueStatus.TargetUnavailable));
            Assert.That(strategy.ChooseAction(obs, Context(strategy)).Action, Is.TypeOf<BuyCharacterAction>());
            Assert.That(trace, Does.Contain("EmptyBoardPriority").And.Contain("evaluations=0"));
        }

        [Test]
        public void V16ActualMergeAddsOnlyNegativePointEight()
        {
            var a = Def("a");
            var obs = V14Observation(gold: 20, board: Put(BoardState.CreateStandard(), 0, "a"),
                hand: Put(HandState.CreateStandard(), 0, HandSlotContent.Character("m")),
                defs: new[] { a }, owned: new[] { V12Fighter("a", a, 1, 2), Inst("m", a) },
                offers: new[] { OfferV14("a", "a") }, stats: Stats(new CharacterStatOption(3, 2, 1)),
                battleEnemy: V16Enemy());
            var row = V16Inspect(obs).Candidates.First(r => r.Action is BuyCharacterAction buy &&
                buy.Placement.Kind == CharacterLocationKind.Hand && r.Status == "Scored");
            Assert.That(row.DeltaCorrection, Is.EqualTo(-.1m));
            Assert.That(row.BattleContribution, Is.EqualTo(-.8m));
            Assert.That(row.UsedScore, Is.EqualTo(row.OriginalScore - .8m));
        }

        [Test]
        public void V16UnitMoveAddsCurrentEnemyAdvantageToExistingSynergy()
        {
            var members = UnitMoveBoardFixture.Board("AB......C");
            var defs = UnitMoveBoardFixture.Definitions("ABC", members);
            var board = BoardState.CreateStandard();
            foreach (var m in members) board = Put(board, m.SlotIndex, m.DefinitionId);
            var obs = V14Observation(gold: 0, level: 3, board: board, defs: defs,
                owned: defs.Select(d => V12Fighter(d.DefinitionId, d, 3, 0)).ToArray(),
                battleEnemy: V16Enemy(6, 4));
            var row = V16Inspect(obs).Candidates.Single(r =>
                r.Action is MoveOrSwapBoardAction move && move.SourceSlot == 8 && move.TargetSlot == 2);
            Assert.That(row.Composition.Value.Delta.BoardSynergy, Is.EqualTo(10m));
            Assert.That(row.Composition.Value.Delta.HandUnitOption, Is.Zero);
            Assert.That(row.BattleContribution, Is.EqualTo(4m));
            Assert.That(row.OriginalScore, Is.EqualTo(80m));
            Assert.That(row.UsedScore, Is.EqualTo(84m));
        }

        [Test]
        public void V16PreemptiveKillContributesThroughIncomingOnly()
        {
            var a = Def("a");
            var obs = V14Observation(gold: 6, defs: new[] { a }, offers: new[] { OfferV14("a", "a") },
                stats: Stats(new CharacterStatOption(2, 0, 1)), battleEnemy: V16Enemy(5, 0, 2));
            var result = V16Inspect(obs);
            var row = result.Candidates.First(r => r.Action is BuyCharacterAction buy && buy.Placement.Kind == CharacterLocationKind.Board);
            Assert.That(result.Baseline.Current.Defense.Incoming, Is.EqualTo(5m));
            Assert.That(row.DeltaCorrection, Is.EqualTo(.3m));
            Assert.That(row.BattleContribution, Is.EqualTo(2.4m));
            var evaluator = BattleValueEvaluator.FromObservation(obs);
            var value = evaluator.ComparePurchase(evaluator.EvaluateCurrent(), (BuyCharacterAction)row.Action).After.Current;
            Assert.That(value.Attack.ExpectedEffectiveDamage, Is.EqualTo(2m));
            Assert.That(value.Attack.EnemySummaries.Single().DefeatProbability, Is.EqualTo(1m));
            Assert.That(value.Defense.InitialThreat, Is.EqualTo(5m));
            Assert.That(value.Defense.Incoming, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void V16AttackLimitAtBaselineOrActualCandidateUsesV15(bool baselineFails)
        {
            var a = Def("a");
            var b = Def("b");
            var obs = V14Observation(gold: 40, board: Put(BoardState.CreateStandard(), 0, "a"),
                defs: new[] { a, b }, owned: new[] { V12Fighter("a", a, baselineFails ? 3 : 0, 2) },
                offers: new[] { OfferV14("b", "b") }, stats: Stats(new CharacterStatOption(3, 2, 1)),
                battleEnemy: V16Enemy(defense: 0), enemyCount: 2);
            var strategy = HeuristicStrategy.CreateV16(new HeuristicV16Settings(battle: new BattleValueSettings(allyAttackMaxStates: 1)));
            var result = V16Inspect(obs, strategy);
            Assert.That(result.Failures.Single().Source, Is.EqualTo(baselineFails ?
                BattleValueCandidateSource.CurrentBoard : BattleValueCandidateSource.Action));
            Assert.That(result.Failures.Single().Status, Is.EqualTo(BattleValueStatus.AttackStateLimitExceeded));
            V16Equivalent(obs, strategy);
        }

        [TestCase(1, BattleValueStatus.DefenseStateLimitExceeded)]
        [TestCase(5, BattleValueStatus.ReferenceUnavailable)]
        public void V16DefenseAndReferenceLimitsUseV15(int target, BattleValueStatus failure)
        {
            var a = Def("a", new[] { "x" });
            var b = Def("b", new[] { "x" });
            var obs = V14Observation(gold: 40,
                board: Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b"),
                defs: new[] { a, b }, owned: new[] { V12Fighter("a", a, 0, 0), V12Fighter("b", b, 0, 4) },
                battleEnemy: V16Enemy(4, 0, target: $"T{target:000}"),
                battleTargets: new[] { EnemyTargetProbabilityEvaluatorTests.Definition(target) });
            var strategy = HeuristicStrategy.CreateV16(new HeuristicV16Settings(battle: new BattleValueSettings(enemyBlockMaxStates: 1)));
            Assert.That(V16Inspect(obs, strategy).Failures.Single().Status, Is.EqualTo(failure));
            V16Equivalent(obs, strategy);
        }

        [Test]
        public void V16IdentityIncludesBattleSettingsAndKeepsLegacyIdentity()
        {
            var identity = HeuristicStrategy.GetV16ConfigurationIdentity();
            var zero = new HeuristicV16Settings(battle: new BattleValueSettings(0m));
            var otherTarget = new HeuristicV16Settings(battle: new BattleValueSettings(
                referenceEnemyTarget: EnemyTargetProbabilityEvaluatorTests.Definition(2)));
            Assert.That(identity, Does.StartWith("integrated-v16-"));
            Assert.That(HeuristicStrategy.GetV16ConfigurationIdentity(zero), Is.Not.EqualTo(identity));
            Assert.That(HeuristicStrategy.GetV16ConfigurationIdentity(otherTarget), Is.Not.EqualTo(identity));
            foreach (var changed in new[] { new BattleValueSettings(defenseWeight: .50m),
                new BattleValueSettings(referenceEnemyATK: 5), new BattleValueSettings(referenceEnemyDEF: 1),
                new BattleValueSettings(referenceEnemyGap: 1), new BattleValueSettings(allyAttackMaxStates: 128),
                new BattleValueSettings(enemyBlockMaxStates: 512) })
                Assert.That(HeuristicStrategy.GetV16ConfigurationIdentity(new HeuristicV16Settings(battle: changed)),
                    Is.Not.EqualTo(identity));
            Assert.That(HeuristicStrategy.CreateV16().Version, Is.EqualTo("16"));
            Assert.That(new HeuristicStrategy().Version, Is.EqualTo("7"));
        }

        [Test]
        public void V16ShopKeepsTwoIndependentMaximaAndPositiveMasses()
        {
            var a = Def("a");
            var b = Def("b");
            var c = Def("c");
            var obs = V14Observation(gold: 40, level: 1, board: Put(BoardState.CreateStandard(), 0, "c"),
                defs: new[] { a, b, c }, owned: new[] { V12Fighter("c", c, 1, 1) },
                offers: new[] { OfferV14("a", "a"), OfferV14("b", "b") },
                stats: Stats(new CharacterStatOption(3, 2, 1)), battleEnemy: V16Enemy());
            var settings = new HeuristicV15Settings();
            var shop = new ShopOpportunityEvaluator(settings.Preparation.Shop);
            var composition = new IntegratedCompositionValueEvaluator(obs, settings.Composition);
            var economy = new EconomyValueEvaluator(settings.Preparation.Economy, EconomyInterestObservation.FromObservation(obs));
            var baseline = economy.CreateBaseline(obs.Player.Gold);
            var method = typeof(ShopOpportunityEvaluator).GetMethod("EvaluateWithBattle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var original = shop.Evaluate(obs, true);
            Assert.That(original.BestCurrent.OfferId, Is.EqualTo("a"));
            var result = ((ShopOpportunityResult Original, ShopOpportunityResult Corrected))method.Invoke(shop,
                new object[] { obs, composition, economy, baseline, (Func<BuyCharacterAction, IntegratedPurchaseEvaluation>)composition.ComparePurchase,
                    true, (Func<CharacterPurchasePreviewResult, BattleValueCandidateSource, string, decimal?>)
                    ((preview, source, id) => preview.DefinitionId == "b" ? 100m : -100m) });
            Assert.That(result.Original.BestCurrent.OfferId, Is.EqualTo("a"));
            Assert.That(result.Corrected.BestCurrent.OfferId, Is.EqualTo("b"));
            Assert.That(result.Original.CurrentOpportunity, Is.EqualTo(original.CurrentOpportunity));
            Assert.That(result.Original.ExpectedFutureOpportunity, Is.EqualTo(original.ExpectedFutureOpportunity));
            Assert.That(result.Corrected.FutureMass.Value.NonPositive, Is.GreaterThan(result.Original.FutureMass.Value.NonPositive));
            var mass = result.Corrected.FutureMass.Value;
            Assert.That(mass.Positive + mass.NonPositive + mass.InsufficientGold + mass.NoReachablePlacement + mass.Unavailable,
                Is.EqualTo(1m).Within(.000000000000000000000001m));
            Assert.That(ShopOpportunityMath.ExpectedMaximum(new[] { (2m, .5m), (6m, .5m) }, 2), Is.EqualTo(5m));
            Assert.That(ShopOpportunityMath.ExpectedMaximum(new[] { (8m, .5m), (4m, .5m) }, 2), Is.EqualTo(7m));
            Assert.That(ShopOpportunityMath.Score(5m, 4m, -.35m, .70m), Is.EqualTo(-.85m));
            Assert.That(ShopOpportunityMath.Score(7m, 3m, -.35m, .70m), Is.EqualTo(1.55m));

            var failed = ((ShopOpportunityResult Original, ShopOpportunityResult Corrected))method.Invoke(shop,
                new object[] { obs, composition, economy, baseline, (Func<BuyCharacterAction, IntegratedPurchaseEvaluation>)composition.ComparePurchase,
                    false, (Func<CharacterPurchasePreviewResult, BattleValueCandidateSource, string, decimal?>)
                    ((preview, source, id) => source == BattleValueCandidateSource.CurrentShop && preview.DefinitionId == "b"
                        ? (decimal?)null : 100m) });
            Assert.That(failed.Corrected, Is.Null);
            Assert.That(failed.Original.Score, Is.EqualTo(original.Score));
            Assert.That(failed.Original.ExpectedFutureOpportunity, Is.EqualTo(original.ExpectedFutureOpportunity));
        }

        [Test]
        public void V16RerollUsesCorrectedCurrentAndFutureWithoutAdditionalActionDelta()
        {
            var a = Def("a");
            var b = Def("b");
            var obs = V14Observation(gold: 40, level: 2, board: Put(BoardState.CreateStandard(), 0, "a"),
                defs: new[] { a, b }, owned: new[] { V12Fighter("a", a, 2, 1) },
                offers: new[] { OfferV14("b", "b") }, stats: Stats(new CharacterStatOption(4, 2, 1)),
                battleEnemy: V16Enemy());
            var result = V16Inspect(obs);
            var row = result.Candidates.Single(r => r.Action is RerollShopAction);
            Assert.That(result.RequiresFallback, Is.False);
            Assert.That(row.CorrectedShop, Is.Not.Null);
            Assert.That(row.CorrectedShop.CurrentOpportunity, Is.Not.EqualTo(row.OriginalShop.CurrentOpportunity));
            Assert.That(row.CorrectedShop.ExpectedFutureOpportunity, Is.Not.EqualTo(row.OriginalShop.ExpectedFutureOpportunity));
            Assert.That(row.UsedScore, Is.EqualTo(.70m * row.CorrectedShop.ExpectedFutureOpportunity -
                row.CorrectedShop.CurrentOpportunity + row.Economy.Value.TotalDelta));
            Assert.That(row.BattleContribution, Is.Null);
            var placement = row.CorrectedShop.BestCurrent.Placement.Value;
            var purchase = result.Candidates.Single(r => r.Action is BuyCharacterAction buy && buy.OfferId == "b" &&
                buy.Placement.Kind == placement.Kind && buy.Placement.SlotIndex == placement.SlotIndex);
            Assert.That(purchase.UsedScore, Is.EqualTo(row.CorrectedShop.CurrentOpportunity),
                $"placement={placement} buyOriginal={purchase.OriginalScore} buyDelta={purchase.DeltaCorrection} shopOriginal={row.OriginalShop.CurrentOpportunity} correctedBest={row.CorrectedShop.BestCurrent.OfferId}");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void V16TraceUsesRetainedScoringAndWritesSameDecision(bool detailed)
        {
            var a = Def("a");
            var b = Def("b");
            var o = V14Observation(gold: 24, level: 3, board: Put(BoardState.CreateStandard(), 0, "a"),
                defs: new[] { a, b }, owned: new[] { V12Fighter("a", a, 2, 1) },
                offers: new[] { OfferV14("b", "b") }, stats: Stats(new CharacterStatOption(4, 2, 1)),
                battleEnemy: V16Enemy());
            var off = HeuristicStrategy.CreateV16();
            var expected = off.ChooseAction(o, Context(off));
            var original = V16Inspect(o, off);
            string trace = null;
            var on = HeuristicStrategy.CreateV16(decisionDiagnosticSink: text => trace = text);
            HeuristicV16DecisionTelemetry sample = default;
            on.ConfigureV16Diagnostics(detailed, value => sample = value);
            var actual = on.ChooseAction(o, Context(on));
            var inspected = V16Inspect(o, on);
            Assert.That(SimulationStateFingerprint.Create(actual.Action), Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
            Assert.That(inspected.Candidates.Select(c => (c.OriginalScore, c.CorrectedScore, c.UsedScore)),
                Is.EqualTo(original.Candidates.Select(c => (c.OriginalScore, c.CorrectedScore, c.UsedScore))));
            Assert.That(sample.Use, Is.EqualTo(V16BattleUse.Used));
            Assert.That(trace, Does.Contain("TraceFormat=v16-1").And.Contain("ShopCorrected").And.Contain("CurrentRound")
                .And.Contain("enemy=").And.Contain("Target filter=").And.Contain("Selection originalTop=")
                .And.Contain("  Delta total="));
            Assert.That(trace.Contains("Current Attack order="), Is.EqualTo(detailed));
            Assert.That(trace.Contains("Current Target mask="), Is.EqualTo(detailed));
            Assert.That(trace.Contains("ShopCorrected Work offers="), Is.EqualTo(detailed));
            Assert.That(trace, Does.Not.Contain("TraceFormat=v15-1"));
        }

        [Test]
        public void V16TraceWriteFailureDoesNotChangeActionOrTelemetry()
        {
            var a = Def("a");
            var o = V14Observation(gold: 0, level: 1, board: Put(BoardState.CreateStandard(), 0, "a"),
                defs: new[] { a }, owned: new[] { V12Fighter("a", a, 2, 1) }, battleEnemy: V16Enemy());
            var off = HeuristicStrategy.CreateV16();
            var expected = off.ChooseAction(o, Context(off));
            var on = HeuristicStrategy.CreateV16();
            var capture = new DecisionTraceCapture(System.IO.Path.GetTempPath(), on.StrategyId, on.Version,
                919016, 1002, true, true, true, (_, _) => throw new System.IO.IOException("test disk failure"));
            int observed = 0;
            on.ConfigureV16Diagnostics(true, _ => observed++);
            capture.Register(on.ConfigureDecisionTrace);
            var actual = on.ChooseAction(o, Context(on));
            capture.Complete(1);
            Assert.That(capture.Generated, Is.EqualTo(1));
            Assert.That(capture.WriteFailures, Is.EqualTo(1));
            Assert.That(capture.Written, Is.Zero);
            Assert.That(observed, Is.EqualTo(1));
            Assert.That(SimulationStateFingerprint.Create(actual.Action), Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
        }

        [Test]
        public void V16TrialAndBatchCountersUseDecisionDenominators()
        {
            var a = Def("a");
            var cases = new[]
            {
                V14Observation(gold: 0, board: Put(BoardState.CreateStandard(), 0, "a"), defs: new[] { a },
                    owned: new[] { V12Fighter("a", a, 3, 1) }, battleEnemy: V16Enemy()),
                V14Observation(gold: 0, board: Put(BoardState.CreateStandard(), 0, "a"), defs: new[] { a },
                    owned: new[] { V12Fighter("a", a, 3, 1) }, battleEnemy: V16Enemy(), battleTargets: Array.Empty<BattleTargetDefinition>())
            };
            var batch = new V16BattleSummary();
            var strategy = HeuristicStrategy.CreateV16();
            var trials = new[] { new V16BattleSummary(), new V16BattleSummary() };
            for (int i = 0; i < cases.Length; i++)
            {
                var trial = trials[i];
                strategy.ConfigureV16Diagnostics(false, d => { trial.Record(d); batch.Record(d); });
                strategy.ChooseAction(cases[i], Context(strategy));
            }
            string[] header = V16BattleSummary.CsvHeader.Split(',');
            string[] line = batch.ToCsv("Batch").First().Split(',');
            Assert.That(line.Length, Is.EqualTo(header.Length));
            foreach (string metric in new[] { "Decisions", "Targeted", "Fallback", "Requests", "Hits" })
            {
                int index = Array.IndexOf(header, metric);
                Assert.That(trials.Sum(t => long.Parse(t.ToCsv("Trial").First().Split(',')[index])),
                    Is.EqualTo(long.Parse(line[index])));
            }
            Assert.That(line[Array.IndexOf(header, "Targeted")], Is.EqualTo("2"));
            Assert.That(line[Array.IndexOf(header, "FallbackRate")], Is.EqualTo("0.5"));
            Assert.That(batch.ToCsv("Batch").Any(x => x.Contains(",EnemyCount,1,")), Is.True);
        }

        [Test]
        public void V16MissingPublicPurchaseStatsRemainLegacyUnknownAndBlockComesFromPublishedIntegers()
        {
            var a = Def("a");
            var b = Def("b");
            var z = Def("z");
            var o = V14Observation(gold: 40, level: 3, board: Put(BoardState.CreateStandard(), 0, "a"),
                defs: new[] { a, b, z }, owned: new[] { V12Fighter("a", a, 2, 2) },
                offers: new[] { OfferV14("b", "b") }, stats: Stats(new CharacterStatOption(3, 2, 1)),
                quotas: new[] { 2, 1, 0, 0, 0 }, weights: new[] { 1, 1, 0, 0, 0 },
                battleEnemy: V16Enemy());
            Assert.That(o.CharacterStats.Get(2, 1).IsAvailable, Is.False);
            Assert.That(BattleBoardInput.FromObservation(o).ItemBlock,
                Is.EqualTo((long)o.Player.PermanentPlayerBlockBonus + o.Player.TemporaryPlayerBlockBonus));
            var result = V16Inspect(o);
            var reroll = result.Candidates.Single(r => r.Action is RerollShopAction);
            Assert.That(result.RequiresFallback, Is.False);
            Assert.That(reroll.OriginalShop.FutureMass.Value.Unavailable, Is.EqualTo(.5m));
            Assert.That(reroll.CorrectedShop.FutureMass.Value.Unavailable, Is.EqualTo(.5m));
        }
    }

    public sealed partial class GameSessionTests
    {
        [TestCase(256, false)]
        [TestCase(256, true)]
        [TestCase(128, false)]
        public void V16CheckpointReplayAndTraceRerunKeepBothRandomStreams(int limit, bool zeroWeight)
        {
            var session = ConfigurationWithStage(OneEncounterConfig());
            var settings = new HeuristicV16Settings(battle: new BattleValueSettings(
                zeroWeight ? 0m : .10m, allyAttackMaxStates: limit));
            string id = HeuristicStrategy.GetV16ConfigurationIdentity(settings);
            var config = new SimulationRunnerConfiguration(session, 1, 3, 200, recordExecutionHistory: true,
                checkpointConfigurationId: id);
            var runner = new SimulationRunner(config);
            var original = runner.RunTrial(35, 88235, HeuristicStrategy.CreateV16(settings));
            var checkpoint = runner.CreateCheckpoint(original);
            Assert.That(checkpoint.SchemaVersion, Is.EqualTo(2));
            var expanded = new SimulationRunner(new SimulationRunnerConfiguration(session, 10, 3, 200, checkpointConfigurationId: id));
            Assert.That(expanded.RestoreTrial(checkpoint, HeuristicStrategy.CreateV16(settings)).CurrentState.RandomState,
                Is.EqualTo(checkpoint.GameRandomState));
            int traces = 0;
            var traced = HeuristicStrategy.CreateV16(settings, _ => traces++);
            traced.ConfigureV16Diagnostics(true);
            var rerun = runner.RunTrial(35, 88235, traced);
            Assert.That(traces, Is.GreaterThan(0));
            Assert.That(rerun.ExecutionHistory.Select(x => (x.Kind, x.AfterFingerprint)),
                Is.EqualTo(original.ExecutionHistory.Select(x => (x.Kind, x.AfterFingerprint))));
            Assert.That(rerun.FinalStrategyState.Payload, Is.EqualTo(original.FinalStrategyState.Payload));
            Assert.That(rerun.FinalStrategyRandomState, Is.EqualTo(original.FinalStrategyRandomState));
            Assert.That(new SimulationReplayVerifier().Verify(config, original, original.ExecutionHistory).Succeeded, Is.True);
            var zero = new HeuristicV16Settings(battle: new BattleValueSettings(zeroWeight ? .10m : 0m));
            var mismatch = new SimulationRunner(new SimulationRunnerConfiguration(session, 10, 3, 200,
                checkpointConfigurationId: HeuristicStrategy.GetV16ConfigurationIdentity(zero)));
            Assert.Throws<ArgumentException>(() => mismatch.RestoreTrial(checkpoint, HeuristicStrategy.CreateV16(zero)));
        }
    }
}
