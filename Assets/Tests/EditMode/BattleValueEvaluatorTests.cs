using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Strategy;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BattleValueEvaluatorTests
    {
        private static BattleBoardMember Member(int slot, decimal attack, decimal defense, params string[] tags) =>
            new BattleBoardMember(new UnitBoardMember(slot, "m" + slot, 1), attack, defense, attributes: tags);

        private static BattleBoardInput Board(long? block, params BattleBoardMember[] members) =>
            new BattleBoardInput(members, Array.Empty<UnitRequirementObservation>(), block);

        private static EnemyDefenseAttacker Enemy(int slot, int hp, int atk, int def = 0, int gap = 0, int target = 1)
        {
            var rule = EnemyTargetProbabilityEvaluatorTests.Definition(target);
            return new EnemyDefenseAttacker(new AllyRoundEnemyInput(slot, hp, def), atk, gap,
                EnemyTargetObservation.Resolve(new[] { rule.TargetId }, new[] { rule }));
        }

        private static BattleValueEvaluator Evaluator(params EnemyDefenseAttacker[] enemies) =>
            new BattleValueEvaluator(new EnemyEvaluationContext(enemies));

        [TestCase(5, 5, 8)]
        [TestCase(5, 0, 5)]
        [TestCase(0, -2, -1.2)]
        public void BasicValueIncludesSignedDefenseContribution(int attack, int defense, double expected)
        {
            var enemy = Enemy(0, 20, attack == 0 ? 4 : 8);
            var result = Evaluator(enemy).Evaluate(Board(0, Member(0, attack, defense)));
            Assert.That(result.Status, Is.EqualTo(BattleValueStatus.Evaluated));
            Assert.That(result.Current.Value, Is.EqualTo((decimal)expected));
        }

        [Test]
        public void PreemptiveKillCountsOnlyOnce()
        {
            var result = Evaluator(Enemy(0, 2, 5)).Evaluate(Board(0, Member(0, 2, 0)));
            Assert.That(result.Current.Attack.ExpectedEffectiveDamage, Is.EqualTo(2m));
            Assert.That(result.Current.Attack.ExpectedDefeats, Is.EqualTo(1m));
            Assert.That(result.Current.Defense.InitialThreat, Is.EqualTo(5m));
            Assert.That(result.Current.Defense.SurvivingThreat, Is.Zero);
            Assert.That(result.Current.Defense.AvoidedThreat, Is.EqualTo(5m));
            Assert.That(result.Current.Defense.Incoming, Is.Zero);
            Assert.That(result.Current.Value, Is.EqualTo(5m));
        }

        [TestCase(0, 2, 4.2)]
        [TestCase(1, 1, 4.8)]
        [TestCase(2, 0, 5.4)]
        [TestCase(5, 0, 5.4)]
        public void ReferenceUsesSameBoardAndBlock(int block, int incoming, double value)
        {
            var result = Evaluator(Enemy(0, 20, 6, 2)).EvaluateReference(Board(block, Member(0, 3, 2)));
            Assert.That(result.Value, Is.EqualTo((decimal)value));
            Assert.That(result.Defense.InitialThreat, Is.EqualTo(4m));
            Assert.That(result.Defense.Incoming, Is.EqualTo(incoming));
            Assert.That(result.Attack.ExpectedEffectiveDamage, Is.EqualTo(3m));
        }

        [Test]
        public void ReferenceRemainsAliveEvenForTotalAttackBeyondAnIntHp()
        {
            var board = Board(0, Member(0, int.MaxValue, 0), Member(1, int.MaxValue, 0));
            var result = Evaluator(Enemy(0, 20, 6)).EvaluateReference(board);
            Assert.That(result.Attack.ExpectedEffectiveDamage, Is.EqualTo(2m * int.MaxValue));
            Assert.That(result.Attack.ExpectedDefeats, Is.Zero);
            Assert.That(result.Defense.ExpectedAttacks, Is.EqualTo(1m));
        }

        [Test]
        public void ReferenceDifferenceAndComparisonsRemainSignedAndAdditive()
        {
            var e = Evaluator(Enemy(0, 20, 6, 2));
            var a = e.CreateBaseline(Board(0, Member(0, 1, 2)));
            var b = e.Evaluate(Board(0, Member(0, 3, 2)));
            var c = e.Evaluate(Board(0, Member(0, 4, 3)));
            Assert.That(a.Current.Value, Is.EqualTo(1.2m));
            Assert.That(a.Reference.Value, Is.EqualTo(2.2m));
            Assert.That(b.Current.Value, Is.EqualTo(2.2m));
            Assert.That(b.Reference.Value, Is.EqualTo(4.2m));
            Assert.That(b.RawCorrection, Is.EqualTo(-2m));
            Assert.That(b.Correction, Is.EqualTo(-.2m));
            var comparison = e.Compare(a, b);
            Assert.That(comparison.DeltaR, Is.EqualTo(-1m));
            Assert.That(comparison.DeltaCorrection, Is.EqualTo(-.1m));
            Assert.That(comparison.ActionContribution, Is.EqualTo(-.8m));
            Assert.That(e.Compare(a, a).DeltaCorrection, Is.Zero);
            Assert.That(e.Compare(b, a).DeltaCorrection, Is.EqualTo(.1m));
            Assert.That(comparison.DeltaCorrection + e.Compare(b, c).DeltaCorrection, Is.EqualTo(e.Compare(a, c).DeltaCorrection));
            var different = Evaluator(Enemy(0, 20, 6)).Evaluate(Board(0, Member(0, 3, 2)));
            Assert.That(different.Correction, Is.Zero);
            Assert.That(e.Compare(a, different).Status, Is.EqualTo(BattleValueStatus.DifferentContext));
        }

        [Test]
        public void PositionChangesOnlyEnemyDependentDefense()
        {
            var e = Evaluator(Enemy(0, 20, 8, target: 5));
            var a = Board(0, Member(2, 1, 6), Member(5, 1, 1), Member(0, 1, 1));
            var b = a.WithBoardSwap(2, 1);
            var comparison = e.Compare(e.Evaluate(b), e.Evaluate(a));
            Assert.That(comparison.Before.Current.Defense.Incoming, Is.EqualTo(7m));
            Assert.That(comparison.After.Current.Defense.Incoming, Is.EqualTo(1m));
            Assert.That(comparison.After.Reference.Value, Is.EqualTo(comparison.Before.Reference.Value));
            Assert.That(comparison.DeltaR, Is.EqualTo(3.6m));
            Assert.That(comparison.ActionContribution, Is.EqualTo(2.88m));
        }

        [Test]
        public void AttributeCountFollowsFinalBoardAndCombinesWithItemBlock()
        {
            var e = Evaluator(Enemy(0, 20, 6));
            var a = Board(2, Member(0, 1, 0, "x", "y", "y"));
            var b = a.WithSlot(1, Member(1, 1, 0, "x"));
            var c = b.WithSlot(2, Member(2, 1, 0, "x", "y"));
            Assert.That(e.Evaluate(a).AttributeBlock, Is.Zero);
            Assert.That(e.Evaluate(b).AttributeBlock, Is.EqualTo(1));
            Assert.That(e.Evaluate(b).InitialBlock, Is.EqualTo(3));
            Assert.That(e.Evaluate(c).InitialBlock, Is.EqualTo(4));
            Assert.That(e.Evaluate(c.WithBoardSwap(0, 7)).AttributeBlock, Is.EqualTo(2));
            Assert.That(e.Evaluate(c.WithSlot(0, null)).AttributeBlock, Is.EqualTo(1));
            Assert.That(e.Evaluate(b).Current.Defense.Incoming, Is.EqualTo(3m));
            Assert.That(e.Evaluate(b).Reference.Defense.Incoming, Is.EqualTo(1m));
            Assert.That(e.Evaluate(Board(-5, Member(0, 1, 0))).InitialBlock, Is.Zero);
            Assert.That(e.Evaluate(Board(null)).Status, Is.EqualTo(BattleValueStatus.ItemBlockUnavailable));
        }

        [Test]
        public void CorrelatedBattleUsesExactIncomingAndDifferentReference()
        {
            var e = Evaluator(Enemy(0, 4, 4), Enemy(1, 4, 4));
            var result = e.Evaluate(Board(5, Member(0, 3, 0), Member(1, 3, 0)));
            Assert.That(result.Current.Attack.ExpectedEffectiveDamage, Is.EqualTo(5m));
            Assert.That(result.Current.Defense.SurvivingThreat, Is.EqualTo(6m));
            Assert.That(result.Current.Defense.Incoming, Is.EqualTo(1.5m));
            Assert.That(result.Current.Value, Is.EqualTo(8.9m));
            Assert.That(result.Reference.Value, Is.EqualTo(8.4m));
            Assert.That(result.RawCorrection, Is.EqualTo(.5m));
            Assert.That(result.Correction, Is.EqualTo(.05m));
        }

        [Test]
        public void GapSuppressesThreatButRetainsAllyDamage()
        {
            var result = Evaluator(Enemy(0, 20, 6, gap: 1)).Evaluate(Board(0, Member(0, 3, 2)));
            Assert.That(result.Current.Value, Is.EqualTo(3m));
            Assert.That(result.Current.Defense.InitialThreat, Is.Zero);
            Assert.That(result.Current.Defense.Incoming, Is.Zero);
            Assert.That(result.Correction, Is.EqualTo(-.12m));
        }

        [Test]
        public void DisabledAndNoEnemySkipAllEvaluationAndCaching()
        {
            var disabled = new BattleValueEvaluator(new EnemyEvaluationContext(null), new BattleValueSettings(0m));
            var noEnemy = Evaluator(Enemy(0, 0, 10));
            foreach (var evaluator in new[] { disabled, noEnemy })
            {
                var first = evaluator.Evaluate(Board(null));
                var second = evaluator.Evaluate(Board(10, Member(0, 1, 1)));
                Assert.That(first.Status, Is.EqualTo(evaluator == disabled ? BattleValueStatus.Disabled : BattleValueStatus.NoEnemy));
                Assert.That(evaluator.Compare(first, second).DeltaCorrection, Is.Zero);
                Assert.That(evaluator.CachedBoards, Is.Zero);
                Assert.That(evaluator.PlanBuilds, Is.Zero);
                Assert.That(evaluator.ReferenceEvaluations, Is.Zero);
                var decision = new BattleValueDecision(evaluator);
                decision.Record(BattleValueCandidateSource.CurrentBoard, "base", first);
                Assert.That(decision.RequiresFallback, Is.False);
            }
            var unknown = new BattleValueEvaluator(new EnemyEvaluationContext(null));
            Assert.That(unknown.Evaluate(Board(0)).Status, Is.EqualTo(BattleValueStatus.EnemyContextUnavailable));
        }

        [Test]
        public void DecisionFallbackDoesNotDependOnCandidateOrder()
        {
            var context = new EnemyEvaluationContext(new[] { Enemy(0, 20, 4), Enemy(1, 20, 4) });
            foreach (bool reversed in new[] { false, true })
            {
                var e = new BattleValueEvaluator(context, new BattleValueSettings(allyAttackMaxStates: 1));
                var decision = new BattleValueDecision(e);
                decision.Record(BattleValueCandidateSource.CurrentBoard, "base", e.Evaluate(Board(0)));
                var candidates = new[] { "A", "B", "C" };
                foreach (string id in reversed ? candidates.Reverse() : candidates)
                    decision.Record(BattleValueCandidateSource.Action, id, e.Evaluate(Board(0, Member(0, id == "B" ? 1 : 0, 0))));
                decision.Record(BattleValueCandidateSource.FutureShop, "legacy", null, legacyEvaluable: false);
                Assert.That(decision.RequiresFallback, Is.True);
                Assert.That(decision.LegacyUnavailableCount, Is.EqualTo(1));
                Assert.That(decision.Failures.Single().CandidateId, Is.EqualTo("B"));
                Assert.That(decision.Failures.Single().Status, Is.EqualTo(BattleValueStatus.AttackStateLimitExceeded));
                Assert.That(decision.Failures.Single().StateLimit, Is.EqualTo(1));
                Assert.That(e.LimitHits, Is.EqualTo(1));
            }
        }

        [TestCase(1, false)]
        [TestCase(5, true)]
        public void DefenseOrReferenceFailureInvalidatesEntireDecision(int target, bool reference)
        {
            var e = new BattleValueEvaluator(new EnemyEvaluationContext(new[] { Enemy(0, 20, 4, target: target) }),
                new BattleValueSettings(enemyBlockMaxStates: 1));
            var result = e.Evaluate(Board(3, Member(0, 0, 0), Member(1, 0, 4)));
            var decision = new BattleValueDecision(e);
            decision.Record(BattleValueCandidateSource.FutureShop, "offer", result);
            Assert.That(result.Status, Is.EqualTo(reference ? BattleValueStatus.ReferenceUnavailable : BattleValueStatus.DefenseStateLimitExceeded));
            Assert.That(result.Correction, Is.Null);
            Assert.That(decision.Failures.Single().Phase, Is.EqualTo(reference ? BattleValuePhase.Reference : BattleValuePhase.Defense));
            Assert.That(decision.Failures.Single().StateLimit, Is.EqualTo(1));
        }

        [Test]
        public void MissingTargetAndStatsRemainUnavailable()
        {
            var missingTarget = new EnemyDefenseAttacker(new AllyRoundEnemyInput(0, 20, 0), 4, 0, null);
            var e = Evaluator(missingTarget);
            var result = e.Evaluate(Board(0));
            Assert.That(result.Status, Is.EqualTo(BattleValueStatus.TargetUnavailable));
            var decision = new BattleValueDecision(e);
            decision.Record(BattleValueCandidateSource.CurrentBoard, "base", result);
            Assert.That(decision.RequiresFallback, Is.True);
            var unrecognized = EnemyTargetObservation.Resolve(new[] { "?" }, new[] { EnemyTargetProbabilityEvaluatorTests.Definition(1) });
            Assert.That(Evaluator(new EnemyDefenseAttacker(missingTarget.InitialEnemy, 4, 0, unrecognized))
                .Evaluate(Board(0)).Status, Is.EqualTo(BattleValueStatus.UnsupportedTarget));
            var unavailable = new BattleBoardMember(new UnitBoardMember(0, "m", 1), 0, 0, AllyAttackStatsKind.Unavailable);
            Assert.That(Evaluator(Enemy(0, 20, 4)).Evaluate(Board(0, unavailable)).Status,
                Is.EqualTo(BattleValueStatus.PublicStatsUnavailable));
        }

        [Test]
        public void CacheSharesIdenticalBoardButDistinguishesStatsAndOrigins()
        {
            var context = new EnemyEvaluationContext(new[] { Enemy(0, 20, 6, 2) });
            var e = new BattleValueEvaluator(context);
            var uncached = new BattleValueEvaluator(context, useCache: false, includeDetails: true);
            var board = Board(0, Member(1, 1, 1), Member(0, 3, 2));
            var differentOrder = Board(0, board.Members.Reverse().ToArray());
            var mean = board.WithSlot(0, new BattleBoardMember(new UnitBoardMember(0, "m0", 1), 3, 2, AllyAttackStatsKind.PublicMean));
            var upgraded = board.WithSlot(0, new BattleBoardMember(new UnitBoardMember(0, "m0", 2), 3.5m, 2));
            upgraded = upgraded.WithSlot(0, new BattleBoardMember(new UnitBoardMember(0, "m0", 2), 3.5m, 2, AllyAttackStatsKind.PublicMean));
            var reference = e.EvaluateReference(board);
            var first = e.Evaluate(board);
            Assert.That(first.Reference, Is.SameAs(reference));
            Assert.That(e.Evaluate(differentOrder), Is.SameAs(first));
            Assert.That(e.Evaluate(mean).UsesPublicMeanStats, Is.True);
            Assert.That(e.Evaluate(upgraded).UsesPublicMeanStats, Is.True);
            Assert.That(uncached.Evaluate(upgraded).Correction, Is.EqualTo(e.Evaluate(upgraded).Correction));
            Assert.That(uncached.Evaluate(mean).Correction, Is.EqualTo(first.Correction));
            Assert.That(e.Requests, Is.EqualTo(5));
            Assert.That(e.Evaluations, Is.EqualTo(3));
            Assert.That(e.CacheHits, Is.EqualTo(2));
            Assert.That(e.ReferenceCacheHits, Is.EqualTo(1));
            Assert.That(e.PlanBuilds, Is.EqualTo(3));
        }

        [Test]
        public void InvalidSettingsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleValueSettings(-1m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleValueSettings(defenseWeight: -1m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleValueSettings(allyAttackMaxStates: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleValueSettings(enemyBlockMaxStates: 4097));
        }

        [Test]
        public void DefaultAttackLimitKeepsFiveLayersButRejectsSixthForSixEnemies()
        {
            var enemies = Enumerable.Range(0, 6).Select(slot => Enemy(slot, 20, 4)).ToArray();
            var e = Evaluator(enemies);
            var firstFive = Board(0, Enumerable.Range(0, 5).Select(slot => Member(slot, 3, 0)).ToArray());
            var first = e.Evaluate(firstFive);
            Assert.That(first.Status, Is.EqualTo(BattleValueStatus.Evaluated));
            Assert.That(first.Current.Attack.LastCompletedStateCount, Is.EqualTo(252));
            Assert.That(first.Current.Defense.Counters.EligibleMasks, Is.EqualTo(1));
            var capped = e.Evaluate(firstFive.WithSlot(5, Member(5, 3, 0)));
            Assert.That(capped.Status, Is.EqualTo(BattleValueStatus.AttackStateLimitExceeded));
            Assert.That(capped.Current.Attack.CompletedActionLayers, Is.EqualTo(5));
            Assert.That(capped.Correction, Is.Null);
            Assert.That(e.LimitHits, Is.EqualTo(1));
        }
    }
    public sealed partial class HeuristicStrategyTests
    {
        private static EnemyDefinition BattleValueEnemy(int defense) =>
            new EnemyDefinition("bv-enemy", "BV enemy", 6, defense, 20, null, null, null, "T001", null, null, null);

        [TestCase(0, false, 1)]
        [TestCase(0, true, 0)]
        [TestCase(1, false, 4)]
        [TestCase(3, false, 8)]
        public void BattleValuePurchasePreviewUsesFinalBoardAndMatchesHypothetical(int materials, bool toHand, int expectedOut)
        {
            var a = Def("A");
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1,
                new[] { new CharacterStatOption(3, 1, 1) }, new[] { new CharacterStatOption(6, 2, 1) },
                new[] { new CharacterStatOption(10, 3, 1) }) });
            var board = BoardState.CreateStandard();
            var hand = HandState.CreateStandard();
            var owned = new List<CharacterInstance>();
            if (materials > 0)
            {
                owned.Add(V12Fighter("board", a, 3, 1));
                board = Put(board, 0, "board");
                for (int i = 0; i < materials; i++)
                {
                    var material = Inst("hand" + i, a, i == 0 ? 1 : 2);
                    owned.Add(material);
                    hand = Put(hand, i, HandSlotContent.Character(material.InstanceId));
                }
            }
            var obs = V14Observation(board: board, hand: hand, defs: new[] { a }, owned: owned.ToArray(),
                offers: new[] { OfferV14("buy", "A") }, stats: stats, battleEnemy: BattleValueEnemy(2));
            var e = BattleValueEvaluator.FromObservation(obs);
            var baseline = e.EvaluateCurrent();
            var kind = toHand || materials > 0 ? CharacterLocationKind.Hand : CharacterLocationKind.Board;
            var actual = e.ComparePurchase(baseline, new BuyCharacterAction(new ActionId("buy"), "buy", new GameActionLocation(kind, 0)));
            var hypothetical = e.CompareHypotheticalPurchase(baseline, obs.SelectedRoster[0], 1, 1, 20,
                new CharacterLocation(kind, 0));
            Assert.That(actual.Status, Is.EqualTo(BattleValueStatus.Evaluated));
            Assert.That(actual.After.Current.Attack.ExpectedEffectiveDamage, Is.EqualTo(toHand ? 0 : expectedOut));
            Assert.That(hypothetical.After, Is.SameAs(actual.After));
            if (toHand)
            {
                Assert.That(actual.DeltaCorrection, Is.Zero);
                Assert.That(e.Requests, Is.EqualTo(3));
                Assert.That(e.Evaluations, Is.EqualTo(1));
                Assert.That(e.CacheHits, Is.EqualTo(2));
            }
            else Assert.That(actual.After.UsesPublicMeanStats, Is.True);
        }

        [TestCase(1, 1, 5)]
        [TestCase(2, 2, 10)]
        [TestCase(3, 2, 10)]
        public void BattleValueUsesPlannedMovesAndStarLimits(int star, int moves, int damage)
        {
            var members = UnitMoveBoardFixture.Board("ABCD..E..");
            var defs = UnitMoveBoardFixture.Definitions("ABC,ADE", members);
            var obs = V14Observation(defs: defs, battleEnemy: BattleValueEnemy(4));
            var roster = members.Select(m => new BattleBoardMember(
                new UnitBoardMember(m.SlotIndex, m.DefinitionId, m.DefinitionId == "A" ? star : 1), 3m, 0m)).ToArray();
            var e = BattleValueEvaluator.FromObservation(obs);
            var result = e.Evaluate(new BattleBoardInput(roster, obs.UnitRequirements));
            Assert.That(result.Current.Attack.ExpectedUnitMoves, Is.EqualTo(moves));
            Assert.That(result.Current.Attack.ExpectedEffectiveDamage, Is.EqualTo(damage));
            Assert.That(result.Reference.Attack.ExpectedEffectiveDamage, Is.EqualTo(star == 1 ? 15m : 18m));
        }

        [Test]
        public void BattleValueRetainsSameRoundDefenseReduction()
        {
            var members = UnitMoveBoardFixture.Board("ABCD.....");
            var defs = UnitMoveBoardFixture.Definitions("ABC", members);
            var obs = V14Observation(defs: defs, battleEnemy: BattleValueEnemy(9));
            var roster = members.Select(m => new BattleBoardMember(m, m.DefinitionId == "D" ? 8m : 3m, 0m)).ToArray();
            var result = BattleValueEvaluator.FromObservation(obs).Evaluate(new BattleBoardInput(roster, obs.UnitRequirements));
            Assert.That(result.Current.Attack.ExpectedDefenseReduction, Is.EqualTo(3m));
            Assert.That(result.Current.Attack.ExpectedNormalDamage, Is.EqualTo(2m));
            Assert.That(result.Reference.Attack.ExpectedEffectiveDamage, Is.EqualTo(17m));
        }

        [Test]
        public void BattleValueTransferUsesObservedReserveStatsAndAttributes()
        {
            var a = Def("A", new[] { "x" });
            var b = Def("B");
            var c = Def("C", new[] { "x" });
            var board = Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b");
            var hand = Put(HandState.CreateStandard(), 0, HandSlotContent.Character("c"));
            var reserve = V12Fighter("c", c, 4, 1);
            reserve.TemporaryAttackBonus = 2;
            var obs = V14Observation(board: board, hand: hand, defs: new[] { a, b, c },
                owned: new[] { V12Fighter("a", a, 1, 2), V12Fighter("b", b, 2, 1), reserve },
                battleEnemy: BattleValueEnemy(0));
            var e = BattleValueEvaluator.FromObservation(obs);
            var baseline = e.EvaluateCurrent();
            var transfer = e.CompareTransfer(baseline, 1, 0).After;
            Assert.That(baseline.InitialBlock, Is.Zero);
            Assert.That(transfer.InitialBlock, Is.EqualTo(1));
            Assert.That(transfer.Current.Attack.ExpectedEffectiveDamage, Is.EqualTo(7m));
            Assert.That(transfer.UsesPublicMeanStats, Is.False);
            Assert.That(e.CompareTransfer(baseline, 1, 1).After.Current.Attack.ExpectedEffectiveDamage, Is.EqualTo(1m));
        }
    }

    public sealed partial class GameSessionTests
    {
        [Test]
        public void BattleValueCheckpointLeavesStateAndLegacyChoiceUnchanged()
        {
            var session = Create(seed: 1111);
            var observation = session.GetObservation().Observation;
            var stateBefore = SimulationStateFingerprint.Create(session.CurrentState);
            var visibleBefore = SimulationStateFingerprint.Create(observation);
            var rngBefore = session.CurrentState.RandomState;
            var strategy = HeuristicStrategy.CreateV15();
            var strategyRng = StrategyRandomSource.Create(1111, strategy.StrategyId, strategy.Version).CaptureState();
            var context = new StrategyContext(strategy.StrategyId, strategy.Version, 0, 0, new ActionId("battle-value"),
                StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), strategyRng);
            var first = strategy.ChooseAction(observation, context);
            var value = BattleValueEvaluator.FromObservation(observation).EvaluateCurrent();
            Assert.That(value.Status, Is.EqualTo(BattleValueStatus.Evaluated));
            var last = strategy.ChooseAction(observation, context);
            Assert.That(SimulationStateFingerprint.Create(first.Action), Is.EqualTo(SimulationStateFingerprint.Create(last.Action)));
            Assert.That(first.NextState.Payload, Is.EqualTo(last.NextState.Payload));
            Assert.That(first.NextStrategyRngState, Is.EqualTo(last.NextStrategyRngState));
            Assert.That(SimulationStateFingerprint.Create(session.CurrentState), Is.EqualTo(stateBefore));
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(visibleBefore));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(rngBefore));
        }
    }
}
