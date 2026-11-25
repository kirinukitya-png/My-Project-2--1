using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameSessionTests
    {
        [Test]
        public void EventSelectionCheckpointRestoresAndResumesWithCallerSuppliedItem()
        {
            var eventStage = new StageProgressConfig(2, 2, 1, 2, 100, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var enemy = new EnemyDefinition("resume-enemy", "Resume enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            var eventItem = new ItemDefinition("resume-event", "Resume event", 0, string.Empty,
                ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 1) });
            GameSessionConfiguration sessionConfiguration = ConfigurationWithStage(eventStage,
                enemy: enemy, items: new[] { BlockItem, eventItem });
            var runnerConfiguration = new SimulationRunnerConfiguration(sessionConfiguration,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 3, maxTrialSteps: 200,
                recordExecutionHistory: true);
            var runner = new SimulationRunner(runnerConfiguration);
            var strategy = new ScriptedRunnerStrategy(RunnerBehavior.EnemylessThenStop);

            SimulationTrialResult stopped = runner.RunTrial(31, 88231, strategy);
            Assert.That(stopped.StopReason, Is.EqualTo(SimulationStopReason.EventItemSelectionRequired));
            SimulationTrialCheckpoint checkpoint = runner.CreateCheckpoint(stopped);
            GameSession restored = runner.RestoreTrial(checkpoint, strategy);
            Assert.That(restored.CurrentState.RandomState, Is.EqualTo(stopped.FinalCheckpoint.RandomState));
            Assert.That(restored.CurrentState.Stage, Is.EqualTo(stopped.FinalCheckpoint.Stage));
            Assert.That(checkpoint.DecisionCount, Is.EqualTo(stopped.DecisionCount));
            Assert.That(checkpoint.NextActionId, Is.EqualTo(stopped.NextActionId));

            string itemId = eventItem.DefinitionId;
            SimulationTrialResult resumed = runner.ResumeTrial(checkpoint, strategy, itemId);
            Assert.That(resumed.StopReason, Is.EqualTo(SimulationStopReason.NoActionNotAllowed));
            Assert.That(resumed.FinalPhase, Is.EqualTo(StageProgressPhase.EventPending));
            Assert.That(resumed.FinalCheckpoint.EventItem, Is.Not.Null);
            Assert.That(resumed.FinalCheckpoint.EventItem.DefinitionId, Is.EqualTo(itemId));
            Assert.That(resumed.DecisionCount, Is.GreaterThanOrEqualTo(stopped.DecisionCount));
            Assert.That(resumed.FinalCheckpoint.RandomState.TrialSeed, Is.EqualTo(stopped.TrialSeed));
            SimulationReplayResult replay = new SimulationReplayVerifier().Verify(runnerConfiguration,
                resumed, resumed.ExecutionHistory);
            Assert.That(replay.Succeeded, Is.True, replay.MismatchKind + " " + replay.Expected + " / " + replay.Actual);
        }

        [Test]
        public void InvalidExternalEventItemDoesNotAlterCheckpointOrResume()
        {
            var eventStage = new StageProgressConfig(2, 2, 1, 2, 100, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var enemy = new EnemyDefinition("resume-invalid-enemy", "Resume enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            var eventItem = new ItemDefinition("resume-invalid-event", "Resume event", 0, string.Empty,
                ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 1) });
            GameSessionConfiguration config = ConfigurationWithStage(eventStage,
                enemy: enemy, items: new[] { BlockItem, eventItem });
            var runner = new SimulationRunner(new SimulationRunnerConfiguration(config, 20, 3, 200));
            var strategy = new ScriptedRunnerStrategy(RunnerBehavior.EnemylessThenStop);
            SimulationTrialCheckpoint checkpoint = runner.CreateCheckpoint(runner.RunTrial(32, 88232, strategy));
            var before = checkpoint.GameSessionState;

            Assert.Throws<ArgumentException>(() => runner.ResumeTrial(checkpoint, strategy, "unknown-item"));
            Assert.That(checkpoint.GameSessionState, Is.SameAs(before));
            Assert.That(checkpoint.GameSessionState.RandomState, Is.EqualTo(before.RandomState));
            Assert.That(checkpoint.GameSessionState.Stage, Is.EqualTo(before.Stage));
        }

        [Test]
        public void DecisionLimitCheckpointResumesWithoutResettingRunnerOrActionProgress()
        {
            var enemy = new EnemyDefinition("resume-limit-enemy", "Resume enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            GameSessionConfiguration config = ConfigurationWithStage(OneEncounterConfig(), enemy: enemy);
            var limited = new SimulationRunner(new SimulationRunnerConfiguration(config, 1, 3, 200));
            var strategy = new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal);
            SimulationTrialResult stopped = limited.RunTrial(33, 88233, strategy);
            Assert.That(stopped.StopReason, Is.EqualTo(SimulationStopReason.DecisionLimitExceeded));
            Assert.That(stopped.FinalCheckpoint.Board.TryGetCharacterIdAt(0, out string placedId), Is.True);
            Assert.That(placedId, Is.Not.Empty);

            SimulationTrialCheckpoint checkpoint = limited.CreateCheckpoint(stopped);
            var expandedRunner = new SimulationRunner(new SimulationRunnerConfiguration(config, 10, 3, 200));
            SimulationTrialResult resumed = expandedRunner.ResumeTrial(checkpoint,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));
            SimulationTrialResult uninterrupted = expandedRunner.RunTrial(33, 88233,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));

            Assert.That(resumed.StopReason, Is.EqualTo(uninterrupted.StopReason));
            Assert.That(resumed.FinalCheckpoint.Economy.Gold, Is.EqualTo(uninterrupted.FinalCheckpoint.Economy.Gold));
            Assert.That(resumed.FinalCheckpoint.RandomState, Is.EqualTo(uninterrupted.FinalCheckpoint.RandomState));
            Assert.That(resumed.FinalStrategyRandomState, Is.EqualTo(uninterrupted.FinalStrategyRandomState));
            Assert.That(resumed.FinalStrategyState.Payload, Is.EqualTo(uninterrupted.FinalStrategyState.Payload));
            Assert.That(resumed.NextActionId, Is.EqualTo(uninterrupted.NextActionId));
            Assert.That(resumed.DecisionCount, Is.EqualTo(uninterrupted.DecisionCount));
            Assert.That(resumed.SuccessfulActionCount, Is.EqualTo(uninterrupted.SuccessfulActionCount));
        }

        [Test]
        public void CheckpointRejectsDifferentStrategyAndSessionConfiguration()
        {
            var enemy = new EnemyDefinition("resume-mismatch-enemy", "Resume enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            GameSessionConfiguration config = ConfigurationWithStage(OneEncounterConfig(), enemy: enemy);
            var runner = new SimulationRunner(new SimulationRunnerConfiguration(config, 1, 3, 200));
            var strategy = new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal);
            SimulationTrialCheckpoint checkpoint = runner.CreateCheckpoint(runner.RunTrial(34, 88234, strategy));
            Assert.Throws<ArgumentException>(() => runner.ResumeTrial(checkpoint,
                new BatchTestStrategy("different", "1", BatchBehavior.Complete)));

            var otherRunner = new SimulationRunner(new SimulationRunnerConfiguration(
                ConfigurationWithStage(OneEncounterConfig(), enemy: enemy), 10, 3, 200));
            Assert.Throws<ArgumentException>(() => otherRunner.ResumeTrial(checkpoint, strategy));
        }

        [Test]
        public void RecordedAutomaticBattleAndRewardsReplayWithoutRerunningStrategy()
        {
            var enemy = new EnemyDefinition("replay-enemy", "Replay enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            GameSessionConfiguration config = ConfigurationWithStage(OneEncounterConfig(), enemy: enemy);
            var runner = new SimulationRunner(new SimulationRunnerConfiguration(config,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 3, maxTrialSteps: 200,
                recordExecutionHistory: true));
            SimulationTrialResult trial = runner.RunTrial(35, 88235,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));

            Assert.That(trial.StopReason, Is.EqualTo(SimulationStopReason.Completed));
            Assert.That(trial.ExecutionHistory.Any(x => x.Kind == SimulationExecutionEventKind.BattleAdvance), Is.True);
            Assert.That(trial.ExecutionHistory.Any(x => x.Kind == SimulationExecutionEventKind.ResolveVictoryReward), Is.True);
            SimulationReplayResult replay = new SimulationReplayVerifier().Verify(
                new SimulationRunnerConfiguration(config, 20, 3, 200, recordExecutionHistory: true),
                trial, trial.ExecutionHistory);
            Assert.That(replay.Succeeded, Is.True, replay.MismatchKind + " " + replay.Expected + " / " + replay.Actual);
            Assert.That(replay.FinalStateMatches, Is.True);
            Assert.That(replay.GameRandomCheckpointMatches, Is.True);
            Assert.That(replay.StrategyCheckpointMatches, Is.True);
            Assert.That(replay.ActionHistoryMatches, Is.True);

            SimulationExecutionRecord first = trial.ExecutionHistory[0];
            var altered = trial.ExecutionHistory.ToArray();
            altered[0] = new SimulationExecutionRecord(first.Sequence, first.DecisionStep, first.Kind,
                first.BeforeFingerprint, first.AfterFingerprint,
                new SelectStageChoiceAction(first.Action.ActionId, StageChoice.SelectC),
                phaseBefore: first.PhaseBefore, phaseAfter: first.PhaseAfter,
                actionStatus: first.ActionStatus, actionError: first.ActionError,
                operationStatus: first.OperationStatus,
                strategyStateAfterDecision: first.StrategyStateAfterDecision,
                strategyRandomStateAfterDecision: first.StrategyRandomStateAfterDecision);
            SimulationReplayResult mismatch = new SimulationReplayVerifier().Verify(
                new SimulationRunnerConfiguration(config, 20, 3, 200, recordExecutionHistory: true), trial, altered);
            Assert.That(mismatch.Succeeded, Is.False);
            Assert.That(mismatch.FirstMismatchSequence, Is.EqualTo(0));
        }
    }
}
