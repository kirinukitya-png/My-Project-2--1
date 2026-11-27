using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class EventItemValueEvaluatorTests
    {
        private static readonly GameActionTarget BoardTarget = Target(0);
        private static readonly GameActionTarget HandTarget = new GameActionTarget(ItemUseTargetKind.HandCharacter, 1, 0, true);

        [TestCase(1, 6.5, 50.40)]
        [TestCase(5, 9.5, 74.40)]
        public void BoardStarUsesPublicMeansAndNoFinancialOrBattleTerms(int cost, double attack, double score)
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, cost: cost, eventPending: true);
            var evaluator = new EventItemValueEvaluator(observation);
            var result = evaluator.Evaluate();
            var target = result.ConfirmedBestTargets.Single();
            var gain = target.Effect.Permanent.Composition.Value.Delta;
            Assert.That(result.Status, Is.EqualTo(EventItemEvaluationStatus.AllEvaluated));
            Assert.That(result.MustUse, Is.True);
            Assert.That(result.CanEnd, Is.False);
            Assert.That(result.RequiresLegacyFallback, Is.False);
            Assert.That(target.Effect.Preview.After.Character.Attack, Is.EqualTo(attack));
            Assert.That(target.Effect.Preview.After.Character.Defense, Is.EqualTo(2.75));
            Assert.That(target.Effect.Preview.After.Character.PreservesOwnershipAndPoolCopies, Is.True);
            Assert.That(target.BeforeStar, Is.EqualTo(1));
            Assert.That(target.AfterStar, Is.EqualTo(2));
            Assert.That(gain.MergeProgress, Is.EqualTo(1));
            Assert.That(target.Effect.GoldDelta, Is.Zero);
            Assert.That(target.Effect.CapacityActionValue, Is.Zero);
            Assert.That(target.Effect.HandOccupiedDelta, Is.Zero);
            Assert.That(target.Effect.PurchasePrice, Is.Null);
            Assert.That(target.Effect.ConsumedHandSlot, Is.Null);
            Assert.That(target.Effect.PermanentBattleCorrectionValue, Is.Zero);
            Assert.That(target.Effect.BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.Event));
            Assert.That(target.Score, Is.EqualTo((decimal)score));
            Assert.That(target.Score, Is.EqualTo(target.Effect.PermanentCompositionValue));
            Assert.That(evaluator.EvaluateTarget(BoardTarget).Score, Is.EqualTo(result.ConfirmedBestEffect));
            Assert.That(evaluator.PermanentBattleRequests + evaluator.TemporaryBattleRequests + evaluator.ReferenceBattleEvaluations, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BestTargetCanBeBoardOrHandUsingRealComposition(bool handWins)
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, layout: handWins ? "B........" : "A........",
                units: "AB", cost: handWins ? 5 : 1, reserveStars: new[] { 1 }, eventPending: true,
                costOneStarTwo: handWins ? new[] { new CharacterStatOption(0, 0, 1) } : null);
            var result = new EventItemValueEvaluator(observation).Evaluate();
            Assert.That(result.Targets, Has.Count.EqualTo(2));
            Assert.That(result.ConfirmedBestTargets.Single().Target.Value.Kind,
                Is.EqualTo(handWins ? ItemUseTargetKind.HandCharacter : ItemUseTargetKind.BoardCharacter));
            var hand = result.Targets.Single(target => target.Target == null ? false : target.Target.Value.Kind == ItemUseTargetKind.HandCharacter);
            Assert.That(hand.Effect.Permanent.Composition.Value.Delta.HandReserve, Is.GreaterThan(0));
            Assert.That(hand.InstanceId, Is.EqualTo(observation.Hand.Slots[1].Character.InstanceId));
            Assert.That(hand.DefinitionId, Is.EqualTo(observation.Hand.Slots[1].Character.DefinitionId));
        }

        [Test]
        public void NegativeEventPreservesMergeLossAndStillRequiresUse()
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, reserveStars: new[] { 1, 2, 2 },
                eventPending: true, poorMean: true);
            var result = new EventItemValueEvaluator(observation).Evaluate();
            Assert.That(result.Targets[0].Score, Is.EqualTo(-30.80m));
            Assert.That(result.Targets[0].Effect.Permanent.Composition.Value.Delta.MergeProgress, Is.EqualTo(-1));
            Assert.That(result.Targets.Where(target => target.Effect.Succeeded).All(target => target.Score < 0), Is.True);
            Assert.That(result.ConfirmedBestEffect, Is.LessThan(0));
            Assert.That(result.MustUse, Is.True);
            Assert.That(result.CanEnd, Is.False);
        }

        [Test]
        public void ActualZeroStillRequiresUse()
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, eventPending: true,
                costOneStarTwo: new[] { new CharacterStatOption(1, 1, 3), new CharacterStatOption(2, 1, 1) });
            var result = new EventItemValueEvaluator(observation).Evaluate();
            Assert.That(result.ConfirmedBestTargets, Has.Count.EqualTo(1));
            Assert.That(result.ConfirmedBestEffect, Is.Zero);
            Assert.That(result.MustUse, Is.True);
            Assert.That(result.CanEnd, Is.False);
        }

        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(2, true)]
        public void NoEligibleStarCanEndWithoutBattle(int star, bool temporaryStar)
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, star: star, temporaryStar: temporaryStar,
                eventPending: true);
            AssertEndWithoutEvaluation(observation);
        }

        [Test]
        public void EmptyBoardAndHandCharactersCanEnd()
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, layout: ".........", eventPending: true);
            AssertEndWithoutEvaluation(observation);
        }

        [Test]
        public void FormalEndAcceptsAnUnconsumedItemWithNoEligibleTarget()
        {
            var session = ItemEffectPreviewTests.Session(1, star: 2, reserveStar: 3, eventPending: true);
            AssertEndWithoutEvaluation(ItemEffectPreviewTests.Observe(session));
            var random = session.CurrentState.RandomState;
            ItemEffectPreviewTests.Execute(session, new EndEventAction(new ActionId("end-without-target")));
            Assert.That(session.CurrentState.Stage.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(session.CurrentState.EventItem.Status, Is.EqualTo(EventItemStatus.EndedWithoutUse));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(random));
        }

        [Test]
        public void SyntheticTemporaryStarOneCannotEndBecauseFormalEndChecksRankAlone()
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, temporaryStar: true, eventPending: true);
            var result = new EventItemValueEvaluator(observation).Evaluate();
            Assert.That(result.Status, Is.EqualTo(EventItemEvaluationStatus.InconsistentState));
            Assert.That(result.Failure, Is.EqualTo(EventItemEvaluationFailure.EndBlockedByTemporaryStar));
            Assert.That(result.TargetEvaluations.LegalCount, Is.Zero);
            Assert.That(result.MustUse, Is.False);
            Assert.That(result.CanEnd, Is.False);
            Assert.That(result.RequiresLegacyFallback, Is.True);
        }

        [Test]
        public void ActualUseAndEndValidateStateAndPoolWithoutEvaluatorApplyingEither()
        {
            var session = ItemEffectPreviewTests.Session(1, eventPending: true);
            var before = ItemEffectPreviewTests.Observe(session);
            var checkpoint = session.CaptureCheckpoint();
            var random = StrategyRandomSource.Create(71, "HeuristicStrategy", "16");
            var strategyRng = random.CaptureState();
            string fingerprint = SimulationStateFingerprint.Create(before);
            string stateFingerprint = SimulationStateFingerprint.Create(checkpoint);
            var evaluator = new EventItemValueEvaluator(before);
            Assert.That(evaluator.Evaluate().CanEnd, Is.False);
            Assert.That(evaluator.EvaluateTarget(HandTarget).AfterStar, Is.EqualTo(2));
            Assert.That(session.CaptureCheckpoint(), Is.SameAs(checkpoint));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(checkpoint.RandomState));
            Assert.That(random.CaptureState(), Is.EqualTo(strategyRng));
            Assert.That(SimulationStateFingerprint.Create(session.CurrentState), Is.EqualTo(stateFingerprint));
            Assert.That(SimulationStateFingerprint.Create(before), Is.EqualTo(fingerprint));
            Assert.That(session.ExecuteAction(new EndEventAction(new ActionId("early-end"))).Succeeded, Is.False);

            ItemEffectPreviewTests.Execute(session, new UseEventItemAction(new ActionId("use"), BoardTarget));
            var consumed = ItemEffectPreviewTests.Observe(session);
            var consumedEvaluator = new EventItemValueEvaluator(consumed);
            var result = consumedEvaluator.Evaluate();
            Assert.That(result.Status, Is.EqualTo(EventItemEvaluationStatus.Consumed));
            Assert.That(result.CanEnd, Is.True);
            Assert.That(result.Targets, Is.Empty);
            Assert.That(consumedEvaluator.PermanentBattleRequests, Is.Zero);
            Assert.That(session.CurrentState.Ownership.GetOwnedInstances().Select(x => x.PoolCopies),
                Is.EqualTo(checkpoint.Ownership.GetOwnedInstances().Select(x => x.PoolCopies)));
            Assert.That(session.CurrentState.RandomState.Event, Is.EqualTo(checkpoint.RandomState.Event));
            Assert.That(session.CurrentState.RandomState.Stats, Is.Not.EqualTo(checkpoint.RandomState.Stats));
            Assert.That(session.CurrentState.Stage.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            ItemEffectPreviewTests.Execute(session, new EndEventAction(new ActionId("end")));
            Assert.That(session.CurrentState.Stage.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
        }

        [Test]
        public void RealPartialAndAllUnavailableDoNotRankAcrossMissingStats()
        {
            var partial = PermanentItemEffectEvaluatorTests.World(6, layout: "AB.......", cost: 5, missingStatsCost: 5, eventPending: true);
            var result = new EventItemValueEvaluator(partial).Evaluate();
            Assert.That(result.Status, Is.EqualTo(EventItemEvaluationStatus.PartiallyEvaluated));
            Assert.That(result.MaximumSuccessfulEffect, Is.EqualTo(50.4m));
            Assert.That(result.Targets[0].Effect.Status, Is.EqualTo(ItemEffectEvaluationStatus.PublicStatsUnavailable));
            AssertUnavailableWithLegalTargets(result);

            var missing = PermanentItemEffectEvaluatorTests.World(6, missingStats: true, eventPending: true);
            result = new EventItemValueEvaluator(missing).Evaluate();
            Assert.That(result.Status, Is.EqualTo(EventItemEvaluationStatus.NoneEvaluated));
            Assert.That(result.MaximumSuccessfulEffect, Is.Null);
            AssertUnavailableWithLegalTargets(result);
        }

        [Test]
        public void UnsupportedMismatchEndedOutsideAndForeignHaveDistinctReasons()
        {
            var outside = PermanentItemEffectEvaluatorTests.World(6);
            Assert.That(new EventItemValueEvaluator(outside).Evaluate().Status, Is.EqualTo(EventItemEvaluationStatus.OutsideEvent));
            var custom = new ItemDefinition("Item006", "Wrong", 10, "", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 1) });
            var mismatch = PermanentItemEffectEvaluatorTests.World(6, eventPending: true, eventDefinition: custom);
            AssertRejectedEvent(mismatch, EventItemEvaluationStatus.DefinitionMismatch);
            var unknown = PermanentItemEffectEvaluatorTests.World(7, eventPending: true);
            AssertRejectedEvent(unknown, EventItemEvaluationStatus.UnsupportedItem);
            var ended = PermanentItemEffectEvaluatorTests.World(6, eventPending: true, eventStatus: EventItemStatus.EndedWithoutUse);
            AssertRejectedEvent(ended, EventItemEvaluationStatus.InconsistentState);
            var actual = PermanentItemEffectEvaluatorTests.World(6, eventPending: true);
            var evaluator = new EventItemValueEvaluator(actual);
            var foreign = ItemEffectPreview.PreviewUse(PermanentItemEffectEvaluatorTests.World(6, eventPending: true),
                ItemPreviewSource.Event, BoardTarget);
            Assert.That(evaluator.EvaluatePreview(foreign).Effect.Status, Is.EqualTo(ItemEffectEvaluationStatus.DifferentObservation));
        }

        [Test]
        public void CacheAndDetailsKeepValuesAndTargetsAcrossEvaluationOrder()
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, layout: "AB.......", eventPending: true);
            var cached = new EventItemValueEvaluator(observation);
            var uncached = new EventItemValueEvaluator(observation, useCache: false, includeDetails: true);
            var a = cached.Evaluate();
            foreach (var target in a.Targets.Reverse())
                Assert.That(uncached.EvaluateTarget(target.Target.Value).Score, Is.EqualTo(target.Score));
            var b = uncached.Evaluate();
            Assert.That(b.ConfirmedBestTargets.Select(target => target.InstanceId), Is.EqualTo(a.ConfirmedBestTargets.Select(target => target.InstanceId)));
            Assert.That(b.ConfirmedBestEffect, Is.EqualTo(a.ConfirmedBestEffect));
            Assert.That(cached.Evaluate(), Is.SameAs(a));
            Assert.That(uncached.PermanentBattleRequests, Is.Zero);
        }

        [TestCase(-8, -2, -12, -2, "1")]
        [TestCase(16, 28, 28, 28, "1,2")]
        [TestCase(-8, 0, -12, 0, "1")]
        public void SyntheticScoresTestComparisonWithoutImplyingCharacterValues(int a, int b, int c, int best, string winners)
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, layout: "ABC......", eventPending: true);
            var targets = SyntheticScores(observation, a, b, c);
            foreach (var ordered in new[] { targets, targets.Reverse().ToArray() })
            {
                var result = DescribeSynthetic(observation, ordered);
                Assert.That(result.ConfirmedBestEffect, Is.EqualTo(best));
                Assert.That(string.Join(",", result.ConfirmedBestTargets.Select(x => x.Target.Value.SlotIndex).OrderBy(x => x)),
                    Is.EqualTo(winners));
                Assert.That(result.MustUse, Is.True);
                Assert.That(result.CanEnd, Is.False);
            }
        }

        [Test]
        public void SyntheticPartialAndIncompleteKeepDiagnosticsAndNeverAllowEnd()
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, layout: "ABC......", eventPending: true);
            var result = DescribeSynthetic(observation, SyntheticScores(observation, 16, null, 18));
            Assert.That(result.MaximumSuccessfulEffect, Is.EqualTo(18));
            AssertUnavailableWithLegalTargets(result);
            var incomplete = DescribeSynthetic(observation, Array.Empty<IntegratedItemEffectResult>(), ItemPreviewFailure.UnsupportedEffect);
            Assert.That(incomplete.Status, Is.EqualTo(EventItemEvaluationStatus.EnumerationIncomplete));
            Assert.That(incomplete.HasLegalTargets, Is.Null);
            Assert.That(incomplete.CanEnd, Is.False);
            Assert.That(incomplete.RequiresLegacyFallback, Is.True);
        }

        // Reuse the real A4 aggregation and Event classifier with injected numbers only in these comparison tests.
        private static IntegratedItemEffectResult[] SyntheticScores(GameObservation observation, params int?[] scores) =>
            scores.Select((score, slot) =>
            {
                var preview = ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Event, Target(slot));
                HpValueComparison healing = null;
                if (score.HasValue)
                {
                    var magnitude = Math.Abs(score.Value);
                    healing = new HpValueEvaluator(new HpValueSettings(magnitude, magnitude, magnitude))
                        .Compare(score.Value < 0 ? 2 : 1, score.Value < 0 ? 1 : 2, 3);
                }
                return (IntegratedItemEffectResult)Activator.CreateInstance(typeof(IntegratedItemEffectResult),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new object[]
                    {
                        preview, score.HasValue ? ItemEffectEvaluationStatus.Evaluated : ItemEffectEvaluationStatus.PublicStatsUnavailable,
                        ItemEffectIntegrationFailure.None, null, healing, null, ItemEffectFailureComponent.None
                    }, null);
            }).ToArray();

        private static EventItemEvaluation DescribeSynthetic(GameObservation observation, IntegratedItemEffectResult[] results,
            ItemPreviewFailure failure = ItemPreviewFailure.None)
        {
            var aggregate = (ItemTargetEvaluations)Activator.CreateInstance(typeof(ItemTargetEvaluations),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { results, failure }, null);
            return (EventItemEvaluation)typeof(EventItemValueEvaluator).GetMethod("DescribeTargets", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { observation.EventItem, aggregate });
        }

        private static void AssertEndWithoutEvaluation(GameObservation observation)
        {
            var evaluator = new EventItemValueEvaluator(observation);
            var result = evaluator.Evaluate();
            Assert.That(result.Status, Is.EqualTo(EventItemEvaluationStatus.NoLegalTargets));
            Assert.That(result.CanEnd, Is.True);
            Assert.That(result.HasLegalTargets, Is.False);
            Assert.That(result.ConfirmedBestEffect, Is.Null);
            Assert.That(result.RequiresLegacyFallback, Is.False);
            Assert.That(evaluator.PermanentBattleRequests, Is.Zero);
        }

        private static void AssertUnavailableWithLegalTargets(EventItemEvaluation result)
        {
            Assert.That(result.MustUse, Is.True);
            Assert.That(result.CanEnd, Is.False);
            Assert.That(result.ConfirmedBestEffect, Is.Null);
            Assert.That(result.ConfirmedBestTargets, Is.Empty);
            Assert.That(result.RequiresLegacyFallback, Is.True);
        }

        private static void AssertRejectedEvent(GameObservation observation, EventItemEvaluationStatus status)
        {
            var result = new EventItemValueEvaluator(observation).Evaluate();
            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.CanEnd, Is.False);
            Assert.That(result.RequiresLegacyFallback, Is.True);
        }

        private static GameActionTarget Target(int slot) => new GameActionTarget(ItemUseTargetKind.BoardCharacter, slot, 0, true);
    }
}
