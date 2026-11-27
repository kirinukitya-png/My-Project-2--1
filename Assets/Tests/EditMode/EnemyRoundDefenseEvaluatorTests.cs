using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Strategy;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class EnemyRoundDefenseEvaluatorTests
    {
        private const decimal Epsilon = 0.000000000000000000000001m;

        private static EnemyDefenseAttacker Enemy(int slot, int atk, int target = 1, int gap = 0, int hp = 4,
            int def = 0, AllyRoundEnemyEffects effects = AllyRoundEnemyEffects.None)
        {
            var rule = EnemyTargetProbabilityEvaluatorTests.Definition(target);
            return new EnemyDefenseAttacker(new AllyRoundEnemyInput(slot, hp, def, effects), atk, gap,
                EnemyTargetObservation.Resolve(new[] { rule.TargetId }, new[] { rule }));
        }

        private static AllyRoundAttackResult Attack(EnemyDefenseAttacker[] enemies, params int[] attacks) =>
            AttackWithLimit(enemies, AllyRoundAttackEvaluator.DefaultMaxStates, attacks);

        private static AllyRoundAttackResult AttackWithLimit(EnemyDefenseAttacker[] enemies, int limit, params int[] attacks) =>
            AllyRoundAttackEvaluator.Evaluate(AllyRoundAttackPlan.FromEstablishedUnits(attacks.Select((atk, slot) =>
                new AllyAttackMember(new UnitBoardMember(slot, "d" + slot, 1), atk)).ToArray(),
                Array.Empty<EstablishedUnitSlots>()), enemies.Select(e => e.InitialEnemy).ToArray(), limit);

        private static AllyDefenseMember[] Board(params int[] defs) =>
            defs.Select((def, slot) => new AllyDefenseMember(slot, def)).ToArray();

        private static EnemyRoundDefenseResult Defend(EnemyDefenseAttacker enemy, decimal block, params int[] defs) =>
            EnemyRoundDefenseEvaluator.Evaluate(Board(defs), new[] { enemy }, Attack(new[] { enemy }), block, includeDetails: true);

        private static void Close(decimal? actual, decimal expected) =>
            Assert.That(actual, Is.EqualTo(expected).Within(Epsilon));

        [TestCase(1, 0, 2)]
        [TestCase(2, 0, 4)]
        [TestCase(4, 2, 8)]
        [TestCase(6, 4, 12)]
        [TestCase(8, 6, 18)]
        public void SameDefenseSumProducesDifferentDamage(int attack, int balanced, int concentratedNumerator)
        {
            Close(Defend(Enemy(0, attack), 0m, 2, 2, 2).Incoming, balanced);
            Close(Defend(Enemy(0, attack), 0m, 6, 0, 0).Incoming, concentratedNumerator / 3m);
        }

        [TestCase(1, 4, 1)]
        [TestCase(2, 26, 9)]
        [TestCase(3, 24, 5)]
        [TestCase(9, 14, 3)]
        [TestCase(10, 122, 33)]
        public void UsesFormalSingleAndCellTargetProbabilities(int target, int numerator, int denominator)
        {
            var result = Defend(Enemy(0, 6, target), 0m, 0, 2, 4);
            Assert.That(result.Status, Is.EqualTo(EnemyRoundDefenseStatus.Evaluated));
            Close(result.Incoming, (decimal)numerator / denominator);
            Close(result.Enemies.Single().Targets.Sum(t => t.Target.Probability), 1m);
            if (target == 1) Assert.That(result.Enemies.Single().Targets.Select(t => t.Damage), Is.EqualTo(new[] { 6m, 4m, 2m }));
        }

        [Test]
        public void SameRowWeightsAndDeterministicRangesUseSelectedDefenseOnly()
        {
            var board = new[] { new AllyDefenseMember(3, 0), new AllyDefenseMember(1, 2), new AllyDefenseMember(5, 4) };
            var enemies = new[] { Enemy(0, 6, 4) };
            Close(EnemyRoundDefenseEvaluator.Evaluate(board, enemies, Attack(enemies), 0).Incoming, 4m);
            foreach (int target in new[] { 5, 6, 7, 8 })
            {
                int[] slots = target == 5 ? new[] { 2, 5, 1 } : target == 6 ? new[] { 1, 4, 2 } :
                    target == 7 ? new[] { 0, 3, 2 } : new[] { 0, 2, 3 };
                var people = slots.Zip(new[] { 2, 3, 99 }, (slot, def) => new AllyDefenseMember(slot, def)).ToArray();
                enemies = new[] { Enemy(0, 8, target) };
                Close(EnemyRoundDefenseEvaluator.Evaluate(people, enemies, Attack(enemies), 0).Incoming, 3m);
            }
        }

        [Test]
        public void EmptyTargetStillAttacks()
        {
            var result = Defend(Enemy(0, 5, 5), 0, 7, 8);
            Close(result.ExpectedAttacks, 1);
            Close(result.Incoming, 5);
            Assert.That(result.Enemies.Single().Targets.Single().Target.SlotMask, Is.Zero);
        }

        [TestCase(2, 5, 2, 0, 3)]
        [TestCase(5, 2, 2, 3, 0)]
        public void BlockSurplusAndShortage(int attack, int block, int absorbed, int incoming, int remaining)
        {
            var result = Defend(Enemy(0, attack), block);
            Close(result.ExpectedAbsorbed, absorbed);
            Close(result.Incoming, incoming);
            Close(result.ExpectedRemainingBlock, remaining);
        }

        [Test]
        public void BlockIsAppliedToEachRandomDamageOutcome()
        {
            var result = Defend(Enemy(0, 6), 3m, 0, 6);
            Close(result.ExpectedDamageBeforeBlock, 3m);
            Close(result.ExpectedAbsorbed, 1.5m);
            Close(result.Incoming, 1.5m);
            Close(result.ExpectedRemainingBlock, 1.5m);
            Assert.That(result.RemainingBlockDistribution, Is.EqualTo(new[] { (0m, .5m), (3m, .5m) }));
        }

        [Test]
        public void MultipleAttacksConsumeBlockInEnemySlotOrder()
        {
            var enemies = new[] { Enemy(3, 4), Enemy(0, 2) };
            var result = EnemyRoundDefenseEvaluator.Evaluate(Board(), enemies, Attack(enemies), 3m);
            Close(result.ExpectedDamageBeforeBlock, 6);
            Close(result.ExpectedAbsorbed, 3);
            Close(result.Incoming, 3);
            Assert.That(result.Enemies.Select(e => e.ExpectedAbsorbed), Is.EqualTo(new[] { 2m, 1m }));
            Assert.That(result.RemainingBlockDistribution, Is.EqualTo(new[] { (0m, 1m) }));
        }

        [Test]
        public void RandomThenFixedAttackPreservesRemainingBlockDistribution()
        {
            var enemies = new[] { Enemy(0, 4), Enemy(1, 2, 5) };
            var result = EnemyRoundDefenseEvaluator.Evaluate(Board(0, 4), enemies, Attack(enemies), 3m);
            Close(result.ExpectedDamageBeforeBlock, 4);
            Close(result.ExpectedAbsorbed, 2.5m);
            Close(result.Incoming, 1.5m);
            Close(result.ExpectedRemainingBlock, .5m);
            Assert.That(result.RemainingBlockDistribution, Is.EqualTo(new[] { (0m, .5m), (1m, .5m) }));
        }

        [Test]
        public void TwoRandomAttacksMatchIndependentUnmergedReference()
        {
            var enemies = new[] { Enemy(0, 4), Enemy(1, 4) };
            var attack = Attack(enemies);
            var board = Board(0, 4);
            var result = EnemyRoundDefenseEvaluator.Evaluate(board, enemies, attack, 3m);
            var paths = (from first in new[] { 0m, 4m } from second in new[] { 0m, 4m }
                let total = first + second
                select (Incoming: Math.Max(0m, total - 3m), Absorbed: Math.Min(3m, total), Block: Math.Max(0m, 3m - total))).ToArray();
            Close(result.ExpectedDamageBeforeBlock, 4);
            Close(result.ExpectedAbsorbed, paths.Average(p => p.Absorbed));
            Close(result.Incoming, paths.Average(p => p.Incoming));
            Close(result.ExpectedRemainingBlock, .75m);
            Assert.That(result.RemainingBlockDistribution,
                Is.EqualTo(paths.GroupBy(p => p.Block).OrderBy(g => g.Key).Select(g => (g.Key, g.Count() / 4m))));
            Assert.That(result.Counters.GeneratedBlockBranches, Is.EqualTo(6));
            Assert.That(result.Counters.MergedBlockBranches, Is.EqualTo(2));
            Assert.That(result.Counters.PeakBlockStates, Is.EqualTo(2));
            var detailed = EnemyRoundDefenseEvaluator.Evaluate(board.Reverse().ToArray(), enemies.Reverse().ToArray(),
                attack, 3m, playerHP: 1, includeDetails: true);
            Assert.That(detailed.Incoming, Is.EqualTo(result.Incoming));
            Assert.That(detailed.Incoming, Is.EqualTo(1.75m));
            Assert.That(detailed.RemainingPlayerHPEstimate, Is.Zero);
            Assert.That(detailed.Enemies[0].Targets.Count, Is.EqualTo(2));
            Assert.That(result.Enemies[0].Targets, Is.Empty);
        }

        [Test]
        public void GapAndInitialZeroHpAreExcludedBeforeTargetResolution()
        {
            var unknown = EnemyTargetObservation.Resolve(new[] { "unknown" }, new[] { EnemyTargetProbabilityEvaluatorTests.Definition(1) });
            var enemies = new[] { Enemy(0, 4), new EnemyDefenseAttacker(new AllyRoundEnemyInput(1, 4, 0), 6, 1, unknown),
                Enemy(2, 8, gap: 2), new EnemyDefenseAttacker(new AllyRoundEnemyInput(3, 0, 0), 10, 0, null) };
            var result = EnemyRoundDefenseEvaluator.Evaluate(Board(0), enemies, Attack(enemies), 0m);
            Close(result.InitialThreat, 4);
            Close(result.SurvivingThreat, 4);
            Close(result.ExpectedAttacks, 1);
            Close(result.Incoming, 4);
            Assert.That(result.Counters.TargetEvaluations, Is.EqualTo(1));
            Assert.That(result.Enemies.Select(e => e.AttackProbability), Is.EqualTo(new[] { 1m, 0m, 0m, 0m }));
        }

        [Test]
        public void KnownKillSkipsUnknownTargetAndReducesThreat()
        {
            var enemies = new[] { Enemy(0, 4, def: 6),
                new EnemyDefenseAttacker(new AllyRoundEnemyInput(1, 2, 0), 6, 0, null) };
            // With both enemies alive, a single hit is still equally likely to select either slot.
            var killed = new[] { Enemy(0, 4, def: 6), Enemy(1, 6, hp: 2) };
            var branching = EnemyRoundDefenseEvaluator.Evaluate(Board(0), killed, Attack(killed, 3), 0m);
            Close(branching.InitialThreat, 10);
            Close(branching.SurvivingThreat, 7);
            Close(branching.AvoidedThreat, 3);
            var allDefeated = EnemyRoundDefenseEvaluator.Evaluate(Board(0), enemies,
                Attack(enemies, 20, 20), 3m);
            Assert.That(allDefeated.Status, Is.EqualTo(EnemyRoundDefenseStatus.NoEligibleEnemyAttack));
            Close(allDefeated.AvoidedThreat, 10);
            Close(allDefeated.ExpectedRemainingBlock, 3);
        }

        [Test]
        public void EquallyLikelySurvivorsShareBlockPerJointOutcome()
        {
            var enemies = new[] { Enemy(0, 4, hp: 2), Enemy(1, 6, hp: 2) };
            var result = EnemyRoundDefenseEvaluator.Evaluate(Board(0), enemies, Attack(enemies, 3), 3m);
            Close(result.InitialThreat, 10);
            Close(result.SurvivingThreat, 5);
            Close(result.AvoidedThreat, 5);
            Close(result.ExpectedAbsorbed, 3);
            Close(result.Incoming, 2);
        }

        [Test]
        public void CorrelatedSurvivalMatchesReferenceAndDiffersFromIndependentMarginals()
        {
            var enemies = new[] { Enemy(0, 4), Enemy(1, 4) };
            var ally = Attack(enemies, 3, 3);
            var result = EnemyRoundDefenseEvaluator.Evaluate(Board(0), enemies, ally, 5m);
            Close(result.InitialThreat, 8);
            Close(result.SurvivingThreat, 6);
            Close(result.AvoidedThreat, 2);
            Close(result.ExpectedDamageBeforeBlock, 6);
            Close(result.ExpectedAbsorbed, 4.5m);
            Close(result.Incoming, 1.5m);
            Close(result.ExpectedRemainingBlock, .5m);
            Close(result.Incoming, ally.Outcomes.Sum(o => o.Probability * Math.Max(0m, o.Enemies.Count(e => e.IsAlive) * 4 - 5)));
            Assert.That(result.Incoming, Is.Not.EqualTo(.75m * .75m * 3m));
            Assert.That(result.Counters.SurvivalMasks, Is.EqualTo(3));
            var randomTargets = EnemyRoundDefenseEvaluator.Evaluate(Board(0, 4), enemies, ally, 5m);
            var paths = from state in ally.Outcomes
                        let n = state.Enemies.Count(e => e.IsAlive)
                        from hits in Enumerable.Range(0, 1 << n)
                        let total = ((hits & 1) + ((hits >> 1) & 1)) * 4m
                        select (Mass: state.Probability / (1 << n), Damage: total);
            Close(randomTargets.Incoming, paths.Sum(p => p.Mass * Math.Max(0m, p.Damage - 5m)));
            Close(randomTargets.ExpectedAbsorbed, paths.Sum(p => p.Mass * Math.Min(5m, p.Damage)));
            Close(randomTargets.ExpectedRemainingBlock, 2.375m);
        }

        [Test]
        public void IdenticalSurvivalAndDamageOutcomesAreAggregated()
        {
            var enemies = new[] { Enemy(0, 5), Enemy(1, 4, gap: 1) };
            var attack = Attack(enemies, 1);
            var result = EnemyRoundDefenseEvaluator.Evaluate(Board(3, 3, 0), enemies, attack, 0m, includeDetails: true);
            Assert.That(result.Counters.AttackOutcomes, Is.EqualTo(2));
            Assert.That(result.Counters.SurvivalMasks, Is.EqualTo(1));
            Assert.That(result.Counters.EligibleMasks, Is.EqualTo(1));
            Assert.That(result.Enemies[0].TargetSetCount, Is.EqualTo(3));
            Assert.That(result.Enemies[0].UniqueDamageCount, Is.EqualTo(2));
            Close(result.Enemies[0].DamageDistribution[0].Probability, 2m / 3m);
            Close(result.Enemies[0].DamageDistribution[1].Probability, 1m / 3m);
            Close(result.Incoming, 3m);
        }

        [Test]
        public void BoardPositionChangesDamageAtTheSameDefenseTotal()
        {
            var enemies = new[] { Enemy(0, 8, 5) };
            var attack = Attack(enemies);
            var first = new[] { new AllyDefenseMember(2, 6), new AllyDefenseMember(5, 1), new AllyDefenseMember(0, 1) };
            var second = new[] { new AllyDefenseMember(1, 6), new AllyDefenseMember(5, 1), new AllyDefenseMember(0, 1) };
            Close(EnemyRoundDefenseEvaluator.Evaluate(first, enemies, attack, 0).Incoming, 1);
            Close(EnemyRoundDefenseEvaluator.Evaluate(second, enemies, attack, 0).Incoming, 7);
        }

        [Test]
        public void SignedAndPublicMeanDefenseUseTheDamageClampOnly()
        {
            Close(Defend(Enemy(0, -1), 0, -3).Incoming, 2);
            Close(Defend(Enemy(0, 2), 0, 0, 4).Incoming, 1);
            var enemies = new[] { Enemy(0, 2) };
            var attack = Attack(enemies);
            var mean = new[] { new AllyDefenseMember(0, 2, AllyAttackStatsKind.PublicMean) };
            var result = EnemyRoundDefenseEvaluator.Evaluate(mean, enemies, attack, 0);
            Assert.That(result.UsesPublicMeanStats, Is.True);
            Close(result.Incoming, 0);
            var fractional = EnemyRoundDefenseEvaluator.Evaluate(
                new[] { new AllyDefenseMember(0, .5m, AllyAttackStatsKind.PublicMean) }, enemies, attack, 2m);
            Close(fractional.ExpectedRemainingBlock, .5m);
            Assert.That(fractional.RemainingBlockDistribution, Is.EqualTo(new[] { (.5m, 1m) }));
            Assert.That(Defend(Enemy(0, 2), 0, 0, 4).Enemies[0].Targets.Select(t => t.Target.Probability),
                Is.EqualTo(Defend(Enemy(0, 2), 0, 2, 2).Enemies[0].Targets.Select(t => t.Target.Probability)));
        }

        [Test]
        public void MissingRulesDoNotBecomeZeroDamage()
        {
            var t = EnemyTargetProbabilityEvaluatorTests.Definition(1);
            var rules = new[] { EnemyTargetObservation.Resolve(new[] { "missing" }, new[] { t }), null,
                EnemyTargetObservation.Resolve(new[] { t.TargetId }, new[] { t, t }) };
            var expected = new[] { EnemyRoundDefenseStatus.UnsupportedTarget, EnemyRoundDefenseStatus.TargetUnavailable,
                EnemyRoundDefenseStatus.InvalidTargetDefinition };
            for (int i = 0; i < rules.Length; i++)
            {
                var enemies = new[] { new EnemyDefenseAttacker(new AllyRoundEnemyInput(0, 4, 0), 5, 0, rules[i]) };
                var result = EnemyRoundDefenseEvaluator.Evaluate(Board(), enemies, Attack(enemies), 0);
                Assert.That(result.Status, Is.EqualTo(expected[i]));
                Assert.That(result.Incoming, Is.Null);
            }
        }

        [Test]
        public void RejectsUnfinishedAttackMismatchedInputsAndStateLimit()
        {
            var enemies = new[] { Enemy(0, 4), Enemy(1, 4) };
            var attack = AttackWithLimit(enemies, 1, 1);
            var failed = EnemyRoundDefenseEvaluator.Evaluate(Board(), enemies, attack, 0);
            Assert.That(failed.Status, Is.EqualTo(EnemyRoundDefenseStatus.AttackEvaluationUnavailable));
            Assert.That(failed.AttackStatus, Is.EqualTo(AllyRoundAttackStatus.StateLimitExceeded));
            Assert.That(failed.Incoming, Is.Null);
            Assert.That(EnemyRoundDefenseEvaluator.Evaluate(Board(), new[] { Enemy(0, 4, hp: 9), enemies[1] },
                Attack(enemies), 0).Status, Is.EqualTo(EnemyRoundDefenseStatus.InvalidInput));
            Assert.That(EnemyRoundDefenseEvaluator.Evaluate(Board(), new[] { enemies[0], enemies[0] },
                Attack(enemies), 0).Status, Is.EqualTo(EnemyRoundDefenseStatus.InvalidInput));
            Assert.That(EnemyRoundDefenseEvaluator.Evaluate(new[] { new AllyDefenseMember(9, 0) }, enemies,
                Attack(enemies), 0).Status, Is.EqualTo(EnemyRoundDefenseStatus.InvalidInput));
            var limited = EnemyRoundDefenseEvaluator.Evaluate(Board(0, 4), enemies, Attack(enemies), 3m, maxBlockStates: 1);
            Assert.That(limited.Status, Is.EqualTo(EnemyRoundDefenseStatus.StateLimitExceeded));
            Assert.That(limited.Incoming, Is.Null);
            Assert.That(limited.RemainingBlockDistribution, Is.Empty);
            Assert.That(EnemyRoundDefenseEvaluator.Evaluate(Board(0, 4), enemies, Attack(enemies),
                3m, maxBlockStates: 2).Status, Is.EqualTo(EnemyRoundDefenseStatus.Evaluated));
        }

        [Test]
        public void NoEnemyAndUnavailableStatsRemainDistinctAndCollectionsAreImmutable()
        {
            var none = Array.Empty<EnemyDefenseAttacker>();
            var result = EnemyRoundDefenseEvaluator.Evaluate(Board(), none, Attack(none), 2m);
            Assert.That(result.Status, Is.EqualTo(EnemyRoundDefenseStatus.NoEnemy));
            Close(result.Incoming, 0m);
            Close(result.ExpectedRemainingBlock, 2m);
            Assert.Throws<NotSupportedException>(() => ((IList<(decimal, decimal)>)result.RemainingBlockDistribution).Clear());
            var enemies = new[] { Enemy(0, 4, effects: AllyRoundEnemyEffects.BattleStart | AllyRoundEnemyEffects.EnemyTurn) };
            var board = new[] { new AllyDefenseMember(1, 0, AllyAttackStatsKind.Unavailable) };
            Assert.That(EnemyRoundDefenseEvaluator.Evaluate(board, enemies, Attack(enemies), 0).Status,
                Is.EqualTo(EnemyRoundDefenseStatus.PublicStatsUnavailable));
            var evaluated = Defend(enemies[0], 0m, 1);
            Assert.That(evaluated.IgnoredEffects, Is.EqualTo(AllyRoundEnemyEffects.BattleStart | AllyRoundEnemyEffects.EnemyTurn));
            Assert.Throws<NotSupportedException>(() => ((IList<EnemyDefenseTargetDetail>)evaluated.Enemies[0].Targets).Clear());
        }
    }

    public sealed partial class HeuristicStrategyTests
    {
        [Test]
        public void PublicDefenseInputAddsTemporaryBonusOnce()
        {
            var definition = Def("defense-observed");
            var character = V12Fighter("fighter", definition, 2, 3);
            character.TemporaryDefenseBonus = 2;
            var visible = V14Observation(board: Put(BoardState.CreateStandard(), 0, "fighter"),
                defs: new[] { definition }, owned: new[] { character });
            var member = AllyDefenseMember.FromObservation(visible.Board.Slots[0].Character);
            Assert.That(member.Defense, Is.EqualTo(5m));
            Assert.That(member.StatsKind, Is.EqualTo(AllyAttackStatsKind.Actual));
        }
    }

    public sealed partial class GameSessionTests
    {
        [Test]
        public void EnemyDefenseCheckpointEvaluationLeavesObservationAndBothRngStreamsUntouched()
        {
            var session = Create(seed: 1111);
            var offer = session.CurrentState.Shop.Offers.First(x => !x.IsEmpty);
            Assert.That(Execute(session, new BuyCharacterAction(new ActionId("defense-fixture"), offer.OfferId,
                new GameActionLocation(CharacterLocationKind.Board, 0))).Succeeded, Is.True);
            var observation = session.GetObservation().Observation;
            var state = session.CurrentState;
            string visible = SimulationStateFingerprint.Create(observation);
            string before = SimulationStateFingerprint.Create(state);
            var strategy = HeuristicStrategy.CreateV15();
            var rng = StrategyRandomSource.Create(1111, strategy.StrategyId, strategy.Version).CaptureState();
            var context = new StrategyContext(strategy.StrategyId, strategy.Version, 0, 0,
                new ActionId("defense-choice"), StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), rng);
            var expected = strategy.ChooseAction(observation, context);
            var allies = observation.Board.Slots.Where(s => s.Character != null).Select(s => s.Character).ToArray();
            var plan = AllyRoundAttackPlan.FromRequirements(allies.Select(AllyAttackMember.FromObservation).ToArray(),
                observation.UnitRequirements);
            var enemies = observation.Enemies.Select(EnemyDefenseAttacker.FromObservation).ToArray();
            var attack = AllyRoundAttackEvaluator.Evaluate(plan, enemies.Select(e => e.InitialEnemy).ToArray());
            var result = EnemyRoundDefenseEvaluator.Evaluate(allies.Select(AllyDefenseMember.FromObservation).ToArray(),
                enemies, attack, 0m);
            Assert.That(result.Incoming, Is.Not.Null);
            var actual = strategy.ChooseAction(observation, context);
            Assert.That(SimulationStateFingerprint.Create(actual.Action), Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(state.RandomState));
            Assert.That(SimulationStateFingerprint.Create(session.CurrentState), Is.EqualTo(before));
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(visible));
        }
    }
}
