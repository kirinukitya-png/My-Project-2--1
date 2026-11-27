using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class EnemyTargetProbabilityEvaluatorTests
    {
        private const decimal Tolerance = 0.000000000000000000000001m;

        private static BattleTargetWeightRule W(BattleTargetPosition position, int weight) =>
            new BattleTargetWeightRule(position, weight);

        internal static BattleTargetDefinition Definition(int target)
        {
            string id = $"T{target:000}";
            switch (target)
            {
                case 1: return new BattleTargetDefinition(id, BattleTargetFilter.AllBoard, BattleTargetSelection.Uniform);
                case 2: return new BattleTargetDefinition(id, BattleTargetFilter.AllBoard, BattleTargetSelection.Weighted,
                    weights: new[] { W(BattleTargetPosition.Front, 6), W(BattleTargetPosition.Middle, 2), W(BattleTargetPosition.Back, 1) });
                case 3: return new BattleTargetDefinition(id, BattleTargetFilter.AllBoard, BattleTargetSelection.Weighted,
                    weights: new[] { W(BattleTargetPosition.Back, 3), W(BattleTargetPosition.Else, 1) });
                case 4: return new BattleTargetDefinition(id, BattleTargetFilter.AllBoard, BattleTargetSelection.Weighted,
                    weights: new[] { W(BattleTargetPosition.SameEnemyRow, 3), W(BattleTargetPosition.Else, 1) });
                case 5: return new BattleTargetDefinition(id, BattleTargetFilter.FrontColumn, BattleTargetSelection.All);
                case 6: return new BattleTargetDefinition(id, BattleTargetFilter.MiddleColumn, BattleTargetSelection.All);
                case 7: return new BattleTargetDefinition(id, BattleTargetFilter.BackColumn, BattleTargetSelection.All);
                case 8: return new BattleTargetDefinition(id, BattleTargetFilter.SameEnemyRow, BattleTargetSelection.All);
                case 9: return new BattleTargetDefinition(id, BattleTargetFilter.RandomCells, BattleTargetSelection.All, cellCount: 2);
                case 10: return new BattleTargetDefinition(id, BattleTargetFilter.RandomCells, BattleTargetSelection.All,
                    cellCount: 2, cellSelection: BattleTargetSelection.Weighted, cellWeights: new[] {
                        W(BattleTargetPosition.Front, 3), W(BattleTargetPosition.Middle, 1), W(BattleTargetPosition.Back, 0) });
                default: throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        private static EnemyTargetObservation Resolve(BattleTargetDefinition definition) =>
            EnemyTargetObservation.Resolve(new[] { definition.TargetId }, new[] { definition });

        private static IReadOnlyList<EnemyTargetOutcome> Evaluate(int target, int enemySlot, params int[] occupied) =>
            EnemyTargetProbabilityEvaluator.Evaluate(Resolve(Definition(target)), enemySlot, occupied).Outcomes;

        private static void Check(IReadOnlyList<EnemyTargetOutcome> actual, int denominator,
            params (int Mask, int Numerator)[] expected)
        {
            Assert.That(actual.Select(o => o.SlotMask), Is.EqualTo(expected.Select(e => e.Mask)));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(actual[i].Probability, Is.EqualTo((decimal)expected[i].Numerator / denominator).Within(Tolerance));
        }

        [TestCase(1, 3, 1, 1, 1)]
        [TestCase(2, 9, 1, 2, 6)]
        [TestCase(3, 5, 3, 1, 1)]
        public void IndividualTargetsUseFormalWeights(int id, int denominator, int back, int middle, int front) =>
            Check(Evaluate(id, 0, 0, 1, 2), denominator, (1, back), (2, middle), (4, front));

        [Test]
        public void SameRowWeightsFollowEnemySlot()
        {
            Check(Evaluate(4, 0, 3, 1, 5), 5, (2, 3), (8, 1), (32, 1));
            Check(Evaluate(4, 2, 3, 1, 5), 7, (2, 1), (8, 3), (32, 3));
        }

        [TestCase(5, 4)]
        [TestCase(6, 146)]
        [TestCase(7, 1)]
        [TestCase(8, 7)]
        public void AllTargetsUseFormalRanges(int id, int mask) =>
            Check(Evaluate(id, 0, 0, 1, 2, 4, 7), 1, (mask, 1));

        [Test]
        public void UniformCellDrawCombinesEmptyAndReversedPaths() =>
            Check(Evaluate(9, 0, 0, 1, 2), 36, (0, 15), (1, 6), (2, 6), (3, 1), (4, 6), (5, 1), (6, 1));

        [Test]
        public void WeightedCellDrawCombinesEmptyAndReversedPaths() =>
            Check(Evaluate(10, 0, 0, 1, 2), 99, (0, 38), (2, 13), (4, 43), (6, 5));

        [Test]
        public void EmptyTargetIsEvaluatedAndEnemyRowCanChange()
        {
            Check(Evaluate(5, 0, 0, 1), 1, (0, 1));
            Check(Evaluate(8, 2, 0, 1, 2, 4, 7), 1, (16, 1));
            Check(Evaluate(10, 0, 0, 3, 6), 1, (0, 1));
            Check(Evaluate(1, 0, 5), 1, (32, 1));
        }

        [Test]
        public void WeightedDrawStopsWhenCandidatesOrPositiveWeightRunOut()
        {
            var rule = new BattleTargetDefinition("limited", BattleTargetFilter.AllBoard,
                BattleTargetSelection.Weighted, 4, new[] { W(BattleTargetPosition.Front, 1), W(BattleTargetPosition.Else, 0) });
            var target = Resolve(rule);
            Check(EnemyTargetProbabilityEvaluator.Evaluate(target, 0, new[] { 0, 1, 2 }).Outcomes, 1, (4, 1));
            Check(EnemyTargetProbabilityEvaluator.Evaluate(target, 0, new[] { 0 }).Outcomes, 1, (0, 1));
            Check(EnemyTargetProbabilityEvaluator.Evaluate(target, 0, new[] { 2 }).Outcomes, 1, (4, 1));
        }

        [Test]
        public void ResolutionDistinguishesDefaultMissingUnknownAndInvalid()
        {
            var t001 = Definition(1);
            Assert.That(EnemyTargetObservation.Resolve(Array.Empty<string>(), new[] { t001 }).Definition, Is.SameAs(t001));
            var missing = EnemyTargetObservation.Resolve(Array.Empty<string>(), null);
            Assert.That(missing.Status, Is.EqualTo(EnemyTargetStatus.Unavailable));
            Assert.That(EnemyTargetProbabilityEvaluator.Evaluate(missing, 0, Array.Empty<int>()).Outcomes, Is.Empty);
            Assert.That(EnemyTargetObservation.Resolve(new[] { "unknown" }, new[] { t001 }).Status,
                Is.EqualTo(EnemyTargetStatus.UnsupportedTarget));
            Assert.That(EnemyTargetObservation.Resolve(new[] { "T001" }, new[] { t001, t001 }).Status,
                Is.EqualTo(EnemyTargetStatus.InvalidDefinition));
            Assert.That(EnemyTargetProbabilityEvaluator.Evaluate(Resolve(t001), 0, new[] { 2, 2 }).Status,
                Is.EqualTo(EnemyTargetProbabilityStatus.InvalidInput));
        }

        [Test]
        public void InputAndPublishedCollectionsAreImmutable()
        {
            var rule = Resolve(Definition(2));
            var occupied = new[] { 2, 0, 1 };
            var result = EnemyTargetProbabilityEvaluator.Evaluate(rule, 0, occupied);
            Assert.That(occupied, Is.EqualTo(new[] { 2, 0, 1 }));
            Assert.Throws<NotSupportedException>(() => ((IList<BattleTargetWeightRule>)rule.Definition.Weights).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<EnemyTargetOutcome>)result.Outcomes).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<int>)result.Outcomes[0].Slots).Clear());
        }

        [Test]
        public void All512OccupancyMasksPreserveDistributionInvariants()
        {
            for (int id = 1; id <= 10; id++)
            {
                var target = Resolve(Definition(id));
                for (int mask = 0; mask < 512; mask++)
                {
                    var slots = Enumerable.Range(0, 9).Where(s => (mask & (1 << s)) != 0).ToArray();
                    var first = EnemyTargetProbabilityEvaluator.Evaluate(target, 3, slots);
                    var repeat = EnemyTargetProbabilityEvaluator.Evaluate(target, 3, slots.Reverse());
                    Assert.That(first.Status, Is.EqualTo(EnemyTargetProbabilityStatus.Evaluated));
                    int previous = -1;
                    decimal total = 0m;
                    for (int i = 0; i < first.Outcomes.Count; i++)
                    {
                        var outcome = first.Outcomes[i];
                        Assert.That(outcome.SlotMask, Is.GreaterThan(previous));
                        Assert.That(outcome.SlotMask & ~mask, Is.Zero);
                        Assert.That(outcome.Probability, Is.GreaterThan(0m));
                        Assert.That((outcome.SlotMask, outcome.Probability),
                            Is.EqualTo((repeat.Outcomes[i].SlotMask, repeat.Outcomes[i].Probability)));
                        Assert.That(outcome.Slots.Count, Is.LessThanOrEqualTo(id >= 9 ? 2 : id <= 4 ? 1 : 3));
                        previous = outcome.SlotMask;
                        total += outcome.Probability;
                    }
                    Assert.That(first.Outcomes.Count, Is.EqualTo(repeat.Outcomes.Count));
                    Assert.That(total, Is.EqualTo(1m).Within(Tolerance));
                    if (mask == 0) Check(first.Outcomes, 1, (0, 1));
                }
            }
        }
    }

    public sealed partial class GameSessionTests
    {
        [Test]
        public void TargetObservationCheckpointPreservesLegacyDecisionsAndRandomStreams()
        {
            var config = Configuration();
            var missing = Configuration(targets: Array.Empty<BattleTargetDefinition>());
            var session = GameSession.Create(config, 441);
            var state = session.CurrentState;
            string before = SimulationStateFingerprint.Create(state);
            var visible = session.GetObservation().Observation;
            var restored = GameSession.Restore(config, state).GetObservation().Observation;
            var legacy = GameSession.Restore(missing, state).GetObservation().Observation;
            Assert.That(visible.Enemies[0].GetInitialTarget().Status, Is.EqualTo(EnemyTargetStatus.Available));
            Assert.That(restored.Enemies[0].GetInitialTarget().TargetId, Is.EqualTo("T001"));
            Assert.That(legacy.Enemies[0].GetInitialTarget().Status, Is.EqualTo(EnemyTargetStatus.Unavailable));
            Assert.That(SimulationStateFingerprint.Create(visible), Is.EqualTo(SimulationStateFingerprint.Create(legacy)));
            Assert.That(HeuristicStrategy.GetV14ObservationFingerprint(visible),
                Is.EqualTo(HeuristicStrategy.GetV14ObservationFingerprint(legacy)));
            Assert.That(HeuristicStrategy.GetV15ObservationFingerprint(visible),
                Is.EqualTo(HeuristicStrategy.GetV15ObservationFingerprint(legacy)));

            IPlayerStrategy[] strategies = {
                new HeuristicStrategy(), HeuristicStrategy.CreateV8(), HeuristicStrategy.CreateV9(),
                HeuristicStrategy.CreateV10(), HeuristicStrategy.CreateV11(), HeuristicStrategy.CreateV12(),
                HeuristicStrategy.CreateV13(), HeuristicStrategy.CreateV14(), HeuristicStrategy.CreateV15(), new RandomStrategy()
            };
            foreach (var strategy in strategies)
            {
                var rng = StrategyRandomSource.Create(441, strategy.StrategyId, strategy.Version).CaptureState();
                var context = new StrategyContext(strategy.StrategyId, strategy.Version, 0, 0, new ActionId("target"),
                    StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), rng);
                var a = strategy.ChooseAction(legacy, context);
                var b = strategy.ChooseAction(visible, context);
                Assert.That(SimulationStateFingerprint.Create(b.Action), Is.EqualTo(SimulationStateFingerprint.Create(a.Action)));
                Assert.That(b.NextState.Payload, Is.EqualTo(a.NextState.Payload));
                Assert.That(b.NextStrategyRngState, Is.EqualTo(a.NextStrategyRngState));
            }

            var enemy = visible.Enemies[0];
            EnemyTargetProbabilityEvaluator.Evaluate(enemy.GetInitialTarget(), enemy.SlotIndex,
                visible.Board.Slots.Where(s => s.Character != null).Select(s => s.SlotIndex));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(state.RandomState));
            Assert.That(SimulationStateFingerprint.Create(session.CurrentState), Is.EqualTo(before));
        }
    }
}
