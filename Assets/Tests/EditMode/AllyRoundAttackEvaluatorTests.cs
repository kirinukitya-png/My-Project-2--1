using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Synergy;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class AllyRoundAttackEvaluatorTests
    {
        private const decimal Tolerance = 0.000000000000000000000001m;

        private static AllyAttackMember Member(int slot, decimal power, int star = 1,
            AllyAttackStatsKind kind = AllyAttackStatsKind.Actual) =>
            new AllyAttackMember(new UnitBoardMember(slot, "def" + slot, star), power, kind);

        private static AllyRoundAttackPlan Normal(params int[] attacks) =>
            AllyRoundAttackPlan.FromEstablishedUnits(attacks.Select((attack, slot) => Member(slot, attack)).ToArray(),
                Array.Empty<EstablishedUnitSlots>());

        private static AllyRoundAttackPlan MoveThenNormal(int last) =>
            AllyRoundAttackPlan.FromEstablishedUnits(new[] { Member(3, last), Member(2, 3), Member(0, 3), Member(1, 3) },
                new[] { new EstablishedUnitSlots("ABC", new[] { 0, 1, 2 }) });

        private static AllyRoundAttackResult Against(AllyRoundAttackPlan plan, int hp, int defense) =>
            AllyRoundAttackEvaluator.Evaluate(plan, new[] { new AllyRoundEnemyInput(0, hp, defense) });

        [TestCase(0, 9, 9)]
        [TestCase(1, 6, 6)]
        [TestCase(2, 3, 5)]
        [TestCase(3, 0, 4)]
        [TestCase(5, 0, 2)]
        [TestCase(7, 0, 0)]
        public void NormalDamageUsesEachAttack(int defense, int even, int concentrated)
        {
            Assert.That(Against(Normal(3, 3, 3), 20, defense).ExpectedEffectiveDamage, Is.EqualTo(even));
            Assert.That(Against(Normal(7, 1, 1), 20, defense).ExpectedEffectiveDamage, Is.EqualTo(concentrated));
        }

        [TestCase(0, 9, 9, 0)]
        [TestCase(2, 3, 7, 0)]
        [TestCase(4, 0, 5, 0)]
        [TestCase(8, 0, 1, 0)]
        [TestCase(9, 0, 0, 3)]
        public void UnitMoveAppliesDefenseOnce(int defense, int normal, int combined, int reduction)
        {
            var plan = AllyRoundAttackPlan.FromEstablishedUnits(new[] { Member(0, 3), Member(1, 3), Member(2, 3) },
                new[] { new EstablishedUnitSlots("ABC", new[] { 0, 1, 2 }) });
            var result = Against(plan, 30, defense);
            Assert.That(Against(Normal(3, 3, 3), 30, defense).ExpectedEffectiveDamage, Is.EqualTo(normal));
            Assert.That(result.ExpectedUnitDamage, Is.EqualTo(combined));
            Assert.That(result.ExpectedDefenseReduction, Is.EqualTo(reduction));
        }

        [Test]
        public void ZeroDamageMoveChangesLaterAttackAndReportsRestoredDefense()
        {
            var plan = MoveThenNormal(8);
            Assert.That(plan.Actions.Select(action => (action.ActorSlot, action.Attack, action.IsUnitMove)),
                Is.EqualTo(new[] { (0, 9m, true), (3, 8m, false) }));
            var result = Against(plan, 20, 9);
            var enemy = result.Outcomes.Single().Enemies.Single();
            Assert.That(enemy.Defense, Is.EqualTo(6));
            Assert.That(enemy.InitialDefense, Is.EqualTo(9));
            Assert.That(enemy.TemporaryDefenseReduction, Is.EqualTo(3));
            Assert.That(enemy.HP, Is.EqualTo(18));
            Assert.That(result.ExpectedNormalDamage, Is.EqualTo(2));
            Assert.That(result.ExpectedDefenseReductionEvents, Is.EqualTo(1));

            var moved = AllyRoundAttackPlan.FromEstablishedUnits(
                new[] { Member(0, 8), Member(1, 3), Member(2, 3), Member(3, 3) },
                new[] { new EstablishedUnitSlots("ABC", new[] { 1, 2, 3 }) });
            Assert.That(moved.Actions.Select(action => action.IsUnitMove), Is.EqualTo(new[] { false, true }));
            Assert.That(Against(moved, 20, 9).ExpectedEffectiveDamage, Is.Zero);
        }

        [Test]
        public void ZeroDamageThenAnotherMoveUsesTemporaryDefenseAndStopsAtZero()
        {
            var plan = AllyRoundAttackPlan.FromEstablishedUnits(
                new[] { Member(0, 0, 3), Member(1, 0, 3), Member(2, 1) },
                new[] {
                    new EstablishedUnitSlots("A", new[] { 0, 1 }),
                    new EstablishedUnitSlots("B", new[] { 0, 1, 2 }),
                    new EstablishedUnitSlots("C", new[] { 0, 1 }) });
            var result = Against(plan, 20, 2);
            Assert.That(plan.Actions.Select(a => a.UnitName), Is.EqualTo(new[] { "B", "A", "C" }));
            Assert.That(result.ExpectedDefenseReduction, Is.EqualTo(2));
            Assert.That(result.ExpectedDefenseReductionEvents, Is.EqualTo(1));
            Assert.That(result.Outcomes.Single().Enemies.Single().Defense, Is.Zero);
        }

        [Test]
        public void OverkillStopsAttackQueue()
        {
            var result = Against(Normal(3, 3, 3), 2, 0);
            Assert.That(result.ExpectedEffectiveDamage, Is.EqualTo(2));
            Assert.That(result.ExpectedOverkill, Is.EqualTo(1));
            Assert.That(result.ExpectedDefeats, Is.EqualTo(1));
            Assert.That(result.ExpectedAttacks, Is.EqualTo(1));
            Assert.That(result.Outcomes.Single().SurvivingSlotMask, Is.Zero);
            Assert.That(result.CompletedActionLayers, Is.EqualTo(1));
        }

        [Test]
        public void LowHealthTargetsAreRemovedFromTheNextDraw()
        {
            var enemies = Enumerable.Range(0, 3).Select(slot => new AllyRoundEnemyInput(slot, 2, 0)).ToArray();
            var result = AllyRoundAttackEvaluator.Evaluate(Normal(3, 3), enemies);
            Assert.That(result.Outcomes.Select(o => o.SurvivingSlotMask), Is.EquivalentTo(new[] { 1, 2, 4 }));
            Assert.That(result.ExpectedEffectiveDamage, Is.EqualTo(4m).Within(Tolerance));
            Assert.That(result.ExpectedDefeats, Is.EqualTo(2m).Within(Tolerance));
            foreach (var enemy in result.EnemySummaries)
            {
                Assert.That(enemy.ExpectedHP, Is.EqualTo(2m / 3m).Within(Tolerance));
                Assert.That(enemy.SurvivalProbability, Is.EqualTo(1m / 3m).Within(Tolerance));
                Assert.That(enemy.DefeatProbability, Is.EqualTo(2m / 3m).Within(Tolerance));
            }
            foreach (var outcome in result.Outcomes)
                Assert.That(outcome.Probability, Is.EqualTo(1m / 3m).Within(Tolerance));
            Assert.That(AllyRoundAttackEvaluator.Evaluate(Normal(3, 3, 3), enemies).Outcomes.Single().SurvivingSlotMask, Is.Zero);
            var move = AllyRoundAttackPlan.FromEstablishedUnits(new[] { Member(0, 3), Member(1, 3), Member(2, 3) },
                new[] { new EstablishedUnitSlots("ABC", new[] { 0, 1, 2 }) });
            Assert.That(AllyRoundAttackEvaluator.Evaluate(move, enemies).ExpectedDefeats, Is.EqualTo(1m).Within(Tolerance));
        }

        [Test]
        public void MergedStatesPreserveCorrelatedSurvivalAndCumulativeDamage()
        {
            var inputs = new[] { new AllyRoundEnemyInput(0, 4, 0), new AllyRoundEnemyInput(3, 4, 0) };
            var plan = Normal(3, 3);
            var result = AllyRoundAttackEvaluator.Evaluate(plan, inputs);
            Assert.That(result.ExpectedEffectiveDamage, Is.EqualTo(5m));
            Assert.That(result.ExpectedDefeats, Is.EqualTo(0.5m));
            Assert.That(result.Outcomes.Select(o => o.Probability), Is.EqualTo(new[] { .25m, .5m, .25m }));
            Assert.That(result.EnemySummaries.Select(e => e.ExpectedHP), Is.EqualTo(new[] { 1.5m, 1.5m }));
            Assert.That(result.EnemySummaries.Select(e => e.SurvivalProbability), Is.EqualTo(new[] { .75m, .75m }));
            Assert.That(result.Outcomes.Single(o => o.SurvivingSlotMask == 9).Probability, Is.EqualTo(.5m));
            Assert.That(result.GeneratedBranches, Is.EqualTo(6));
            Assert.That(result.MergedBranches, Is.EqualTo(1));
            Assert.That(SimulationStateFingerprint.Create(AllyRoundAttackEvaluator.Evaluate(plan, inputs.Reverse().ToArray())),
                Is.EqualTo(SimulationStateFingerprint.Create(result)));
            Assert.That(inputs[0].Slot, Is.Zero);
            Assert.Throws<NotSupportedException>(() => ((IList<AllyRoundEnemyOutcome>)result.Outcomes).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<AllyRoundEnemyState>)result.Outcomes[0].Enemies).Clear());
        }

        [Test]
        public void EnemySlotsKeepDistinctDefense()
        {
            var result = AllyRoundAttackEvaluator.Evaluate(Normal(3),
                new[] { new AllyRoundEnemyInput(0, 10, 0), new AllyRoundEnemyInput(1, 10, 4) });
            Assert.That(result.ExpectedEffectiveDamage, Is.EqualTo(1.5m));
            Assert.That(result.ExpectedDefeats, Is.Zero);
            Assert.That(result.EnemySummaries.Select(e => e.ExpectedHP), Is.EqualTo(new[] { 8.5m, 10m }));
            Assert.That(result.Outcomes.All(o => o.SurvivingSlotMask == 3), Is.True);
        }

        [TestCase(1, 1, 5)]
        [TestCase(2, 2, 10)]
        [TestCase(3, 2, 10)]
        public void MoveOrderUsesTurnFlagsAndParticipationLimit(int star, int moves, int damage)
        {
            var plan = AllyRoundAttackPlan.FromEstablishedUnits(
                new[] { Member(0, 3, star), Member(1, 3), Member(2, 3), Member(3, 3), Member(6, 3) },
                new[] { new EstablishedUnitSlots("second", new[] { 0, 3, 6 }), new EstablishedUnitSlots("first", new[] { 0, 1, 2 }) });
            Assert.That(plan.Actions.Where(a => a.IsUnitMove).Select(a => (a.UnitName, a.ActorSlot, a.Attack)),
                Is.EqualTo(new[] { ("first", 0, 9m), ("second", 0, 9m) }.Take(moves)));
            Assert.That(plan.Actions.Where(a => !a.IsUnitMove).Select(a => a.ActorSlot),
                Is.EqualTo(star == 1 ? new[] { 3, 6 } : Array.Empty<int>()));
            Assert.That(plan.UnitPlan.Members[0].Limit, Is.EqualTo(star));
            Assert.That(plan.UnitPlan.Members[0].Count, Is.EqualTo(moves));
            Assert.That(Against(plan, 20, 4).ExpectedEffectiveDamage, Is.EqualTo(damage));
        }

        [Test]
        public void StateLimitNeverPublishesAPartialDistribution()
        {
            var enemies = new[] { new AllyRoundEnemyInput(0, 4, 0), new AllyRoundEnemyInput(3, 4, 0) };
            var plan = Normal(3, 3);
            var limited = AllyRoundAttackEvaluator.Evaluate(plan, enemies, 2);
            Assert.That(limited.Status, Is.EqualTo(AllyRoundAttackStatus.StateLimitExceeded));
            Assert.That(limited.LastCompletedProbabilityMass, Is.EqualTo(1m));
            Assert.That(limited.CompletedActionLayers, Is.EqualTo(1));
            Assert.That(limited.Outcomes, Is.Empty);
            Assert.That(limited.ExpectedEffectiveDamage, Is.Null);
            Assert.That(limited.ExpectedDefeats, Is.Null);
            Assert.That(SimulationStateFingerprint.Create(AllyRoundAttackEvaluator.Evaluate(plan, enemies.Reverse().ToArray(), 2)),
                Is.EqualTo(SimulationStateFingerprint.Create(limited)));
            Assert.That(AllyRoundAttackEvaluator.Evaluate(plan, enemies, 3).Outcomes.Count, Is.EqualTo(3));
        }

        [Test]
        public void SixEnemyLayerCountsAndDiagnosticsAreDeterministic()
        {
            var enemies = Enumerable.Range(0, 6).Select(slot => new AllyRoundEnemyInput(slot, 20, 0)).ToArray();
            var plan = Normal(3, 3, 3, 3);
            var detailed = AllyRoundAttackEvaluator.Evaluate(plan, enemies, includeDetails: true);
            var plain = AllyRoundAttackEvaluator.Evaluate(plan, enemies);
            Assert.That(detailed.Layers, Is.EqualTo(new[] { (1, 6, 6, 0), (6, 36, 21, 15),
                (21, 126, 56, 70), (56, 336, 126, 210) }));
            Assert.That(detailed.ExpectedEffectiveDamage, Is.EqualTo(12m).Within(Tolerance));
            Assert.That(detailed.ExpectedEffectiveDamage, Is.EqualTo(plain.ExpectedEffectiveDamage));
            Assert.That(detailed.Outcomes.Select(o => o.Probability), Is.EqualTo(plain.Outcomes.Select(o => o.Probability)));
            Assert.That(plain.Layers, Is.Empty);
        }

        [Test]
        public void MissingStatsAndEmptyStatesAreDistinctFromNumericZero()
        {
            var empty = Normal();
            Assert.That(Against(empty, 10, 0).ExpectedEffectiveDamage, Is.Zero);
            Assert.That(Against(empty, 0, 0).Status, Is.EqualTo(AllyRoundAttackStatus.NoEnemy));
            var missing = AllyRoundAttackPlan.FromEstablishedUnits(
                new[] { Member(0, 0, kind: AllyAttackStatsKind.Unavailable) }, Array.Empty<EstablishedUnitSlots>());
            Assert.That(Against(missing, 10, 0).Status, Is.EqualTo(AllyRoundAttackStatus.PublicStatsUnavailable));
            Assert.That(Against(missing, 10, 0).ExpectedEffectiveDamage, Is.Null);
        }

        [Test]
        public void MeanInputAndUnsupportedEffectsAreIdentified()
        {
            var mean = AllyRoundAttackPlan.FromEstablishedUnits(
                new[] { Member(0, 2.5m, kind: AllyAttackStatsKind.PublicMean) }, Array.Empty<EstablishedUnitSlots>());
            var result = AllyRoundAttackEvaluator.Evaluate(mean, new[] {
                new AllyRoundEnemyInput(0, 4, 2, AllyRoundEnemyEffects.OnDefeat | AllyRoundEnemyEffects.EnemyTurn) });
            Assert.That(result.ExpectedEffectiveDamage, Is.EqualTo(.5m));
            Assert.That(result.UsesPublicMeanStats, Is.True);
            Assert.That(result.MatchesRealRoundModel, Is.False);
            Assert.That(result.IgnoredEffects, Is.EqualTo(AllyRoundEnemyEffects.OnDefeat | AllyRoundEnemyEffects.EnemyTurn));
        }

        [Test]
        public void ValidationAndNegativeStatsMatchSupportedDomain()
        {
            Assert.That(Against(Normal(-1), 10, -3).ExpectedEffectiveDamage, Is.EqualTo(2));
            Assert.That(Against(Normal(1), -1, 0).Status, Is.EqualTo(AllyRoundAttackStatus.InvalidInput));
            Assert.That(AllyRoundAttackEvaluator.Evaluate(Normal(1), null).Status, Is.EqualTo(AllyRoundAttackStatus.InvalidInput));
            Assert.That(AllyRoundAttackEvaluator.Evaluate(Normal(1), new[] { new AllyRoundEnemyInput(6, 1, 0) }).Status,
                Is.EqualTo(AllyRoundAttackStatus.InvalidInput));
            Assert.That(AllyRoundAttackEvaluator.Evaluate(Normal(1), Array.Empty<AllyRoundEnemyInput>(), 0).Status,
                Is.EqualTo(AllyRoundAttackStatus.InvalidInput));
            Assert.That(AllyRoundAttackPlan.FromEstablishedUnits(new[] { Member(0, 1), Member(0, 2) },
                Array.Empty<EstablishedUnitSlots>()).Status, Is.EqualTo(AllyRoundAttackStatus.InvalidInput));
            Assert.That(AllyRoundAttackPlan.FromEstablishedUnits(new[] { Member(0, 1) },
                new[] { new EstablishedUnitSlots("invalid", new[] { 0, 1 }) }).Status,
                Is.EqualTo(AllyRoundAttackStatus.InvalidInput));
            Assert.Throws<ArgumentOutOfRangeException>(() => Member(0, 1, 4));
        }

        [Test]
        public void IndependentUnmergedTreeAgreesWithProductionEvaluator()
        {
            var plan = MoveThenNormal(8);
            var initial = new[] { (HP: 10, DEF: 9), (HP: 4, DEF: 0), (HP: 6, DEF: 3) };
            var actual = AllyRoundAttackEvaluator.Evaluate(plan, initial.Select((e, i) =>
                new AllyRoundEnemyInput(i, e.HP, e.DEF)).ToArray());
            var reference = new Dictionary<string, decimal>();
            string Key(IEnumerable<(int HP, int DEF)> state) => string.Join(";", state.Select(e => e.HP + ":" + e.DEF));
            void Walk((int HP, int DEF)[] state, int step, decimal probability)
            {
                int alive = state.Count(e => e.HP != 0);
                if (step == 2 || alive == 0)
                {
                    string key = Key(state);
                    reference.TryGetValue(key, out decimal mass);
                    reference[key] = mass + probability;
                    return;
                }
                for (int target = 0; target < state.Length; target++)
                {
                    if (state[target].HP == 0) continue;
                    var next = state.ToArray();
                    int hit = (step == 0 ? 9 : 8) - state[target].DEF;
                    next[target] = (Math.Max(0, state[target].HP - Math.Max(0, hit)),
                        step == 0 && hit <= 0 ? Math.Max(0, state[target].DEF - 3) : state[target].DEF);
                    Walk(next, step + 1, probability / alive);
                }
            }
            Walk(initial, 0, 1m);
            Assert.That(actual.Outcomes.Count, Is.EqualTo(reference.Count));
            foreach (var outcome in actual.Outcomes)
                Assert.That(outcome.Probability, Is.EqualTo(reference[Key(outcome.Enemies.Select(e => ((int)e.HP, e.Defense)))]).Within(Tolerance));
            Assert.That(actual.Outcomes.Sum(o => o.Probability), Is.EqualTo(1m).Within(Tolerance));
        }
    }

    public sealed partial class GameSessionTests
    {
        [Test]
        public void AllyAttackEvaluationCheckpointDoesNotChangeSessionOrRandomStreams()
        {
            var session = Create(seed: 1111);
            var offer = session.CurrentState.Shop.Offers.First(x => !x.IsEmpty);
            Assert.That(Execute(session, new BuyCharacterAction(new ActionId("attack-eval-fixture"),
                offer.OfferId, new GameActionLocation(CharacterLocationKind.Board, 0))).Succeeded, Is.True);
            var observation = session.GetObservation().Observation;
            string visible = SimulationStateFingerprint.Create(observation);
            string before = SimulationStateFingerprint.Create(session.CurrentState);
            var rng = session.CurrentState.RandomState;
            var strategy = HeuristicStrategy.CreateV15();
            var strategyRng = StrategyRandomSource.Create(1111, strategy.StrategyId, strategy.Version).CaptureState();
            var context = new StrategyContext(strategy.StrategyId, strategy.Version, 0, 0,
                new ActionId("attack-eval-choice"), StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), strategyRng);
            var expected = strategy.ChooseAction(observation, context);
            var members = observation.Board.Slots.Where(s => s.Character != null)
                .Select(s => AllyAttackMember.FromObservation(s.Character)).ToArray();
            var plan = AllyRoundAttackPlan.FromRequirements(members, observation.UnitRequirements);
            var result = AllyRoundAttackEvaluator.Evaluate(plan, observation.Enemies.Select(AllyRoundEnemyInput.FromObservation).ToArray());
            Assert.That(result.Status, Is.EqualTo(AllyRoundAttackStatus.Evaluated));
            Assert.That(result.ExpectedAttacks, Is.EqualTo(1m));
            var actual = strategy.ChooseAction(observation, context);
            Assert.That(SimulationStateFingerprint.Create(actual.Action), Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(visible));
            Assert.That(SimulationStateFingerprint.Create(session.CurrentState), Is.EqualTo(before));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(rng));
        }
    }
}
