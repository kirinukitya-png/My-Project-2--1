using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;
using ProjectNST.SimulationExport;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameSessionTests
    {
        private static StageProgressConfig OneEncounterConfig(int eventChance = 0) =>
            new StageProgressConfig(1, 1, 1, 1, eventChance, 0, eventChance,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static StageProgressConfig TwoEncounterConfig() =>
            new StageProgressConfig(2, 2, 1, 2, 0, 0, 0,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static SimulationRunner Runner(StageProgressConfig stage,
            int phaseLimit = 100, int rejectLimit = 10, long stepLimit = 1000,
            EnemyDefinition enemy = null, int maxBattleRounds = 3) =>
            new SimulationRunner(new SimulationRunnerConfiguration(
                ConfigurationWithStage(stage, enemy: enemy ?? new EnemyDefinition("run-enemy", "Run enemy", 0, 0, 1,
                    null, null, null, "T001", null, null, null), maxBattleRounds: maxBattleRounds),
                phaseLimit, rejectLimit, stepLimit));

        [Test]
        public void ScriptedTrialReachesCompletedAndReportsImmutableSummary()
        {
            SimulationTrialResult result = Runner(OneEncounterConfig()).RunTrial(3, 12345,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));

            Assert.That(result.Status, Is.EqualTo(SimulationTrialStatus.Completed),
                result.StopReason + ": " + result.Detail + " phase=" + result.FinalPhase);
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.Completed), result.Detail + " phase=" + result.FinalPhase);
            Assert.That(result.StopReasonId, Is.EqualTo("completed"));
            Assert.That(result.FinalPhase, Is.EqualTo(StageProgressPhase.Completed));
            Assert.That(result.SuccessfulActionCount, Is.EqualTo(3));
            Assert.That(result.DecisionCount, Is.EqualTo(3));
            Assert.That(result.FinalCheckpoint, Is.Not.Null);
            Assert.That(result.FinalGameRandomState, Is.Not.Null);
            Assert.That(result.FinalStrategyState.StrategyId, Is.EqualTo("runner-test"));
            Assert.That(result.NextActionId.Value, Is.EqualTo("trial:3:action:3"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RandomAndHeuristicStrategiesRunOneReproducibleTrial(bool heuristic)
        {
            IPlayerStrategy strategy = heuristic ? (IPlayerStrategy)new HeuristicStrategy() : new RandomStrategy();
            SimulationRunner runner = Runner(OneEncounterConfig(), stepLimit: 1000);
            SimulationTrialResult first = runner.RunTrial(5, 99881, strategy);
            SimulationTrialResult second = runner.RunTrial(5, 99881, strategy);

            Assert.That(first.FinalCheckpoint, Is.Not.Null);
            Assert.That(first.StopReason, Is.EqualTo(second.StopReason));
            Assert.That(first.DecisionCount, Is.EqualTo(second.DecisionCount));
            Assert.That(first.SuccessfulActionCount, Is.EqualTo(second.SuccessfulActionCount));
            Assert.That(first.RejectedActionCount, Is.EqualTo(second.RejectedActionCount));
            Assert.That(first.FinalGold, Is.EqualTo(second.FinalGold));
            Assert.That(first.FinalHP, Is.EqualTo(second.FinalHP));
            Assert.That(first.NextActionId, Is.EqualTo(second.NextActionId));
            Assert.That(first.FinalGameRandomState.Value.Shop, Is.EqualTo(second.FinalGameRandomState.Value.Shop));
            Assert.That(first.FinalGameRandomState.Value.Battle, Is.EqualTo(second.FinalGameRandomState.Value.Battle));
            Assert.That(first.FinalStrategyRandomState, Is.EqualTo(second.FinalStrategyRandomState));
        }

        [Test]
        public void NoActionResolvesEnemylessStageThenAdvancesInsteadOfEndingTrial()
        {
            SimulationTrialResult result = Runner(TwoEncounterConfig()).RunTrial(6, 12346,
                new ScriptedRunnerStrategy(RunnerBehavior.EnemylessThenStop));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.NoActionNotAllowed));
            Assert.That(result.EncounterIndex, Is.EqualTo(1));
            Assert.That(result.FinalPhase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(result.DecisionCount, Is.EqualTo(3));
            Assert.That(result.BattleCount, Is.Zero);
            Assert.That(result.VictoryCount, Is.Zero);
        }

        [Test]
        public void EventItemSelectionRequirementStopsWithPreRewardCheckpoint()
        {
            StageProgressConfig eventStage = new StageProgressConfig(2, 2, 1, 2, 100, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            SimulationTrialResult result = Runner(eventStage).RunTrial(13, 12353,
                new ScriptedRunnerStrategy(RunnerBehavior.EnemylessThenStop));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.EventItemSelectionRequired));
            Assert.That(result.FinalPhase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(result.FinalCheckpoint.Stage.CurrentSelection.Choice, Is.EqualTo(StageChoice.SelectB));
            Assert.That(result.FinalCheckpoint.RandomState.Event,
                Is.EqualTo(new SimulationRandomStreams(12353).CaptureState().Event));
        }

        [Test]
        public void ConfiguredEventItemIsSuppliedAutomaticallyAndRecordedInObservation()
        {
            var eventItem = new ItemDefinition("event-fixed", "Event", 0, "", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Star, 2) });
            var stage = new StageProgressConfig(2, 2, 1, 2, 100, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            GameSessionConfiguration sessionConfig = ConfigurationWithStage(stage, eventItemDefinition: eventItem);
            var runnerConfig = new SimulationRunnerConfiguration(sessionConfig,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 5, maxTrialSteps: 500,
                recordExecutionHistory: true, checkpointConfigurationId: "event-test-v1",
                eventItemDefinitionId: eventItem.DefinitionId);

            SimulationTrialResult result = new SimulationRunner(runnerConfig).RunTrial(21, 987654,
                new ScriptedRunnerStrategy(RunnerBehavior.EnemylessThenStop));

            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.NoActionNotAllowed), result.Detail);
            Assert.That(result.FinalPhase, Is.EqualTo(StageProgressPhase.EventPending));
            Assert.That(result.FinalCheckpoint.EventItem.DefinitionId, Is.EqualTo(eventItem.DefinitionId));
            Assert.That(result.ExecutionHistory.Any(x => x.ExternalEventItemDefinitionId == eventItem.DefinitionId), Is.True);
            Assert.That(result.FinalCheckpoint.RandomState.Event,
                Is.EqualTo(new SimulationRandomStreams(987654).CaptureState().Event),
                "A 100% event chance is deterministic and does not consume Event RNG.");
            Assert.That(result.FinalStrategyRandomState,
                Is.EqualTo(StrategyRandomSource.Create(987654, "runner-test", "1").CaptureState()));
            GameSession restored = GameSession.Restore(sessionConfig, result.FinalCheckpoint);
            ObservationBuildResult observation = restored.GetObservation();
            Assert.That(observation.Succeeded, Is.True, string.Join("; ", observation.Errors));
            Assert.That(observation.Observation.EventItem.Item.DefinitionId, Is.EqualTo(eventItem.DefinitionId));
            Assert.That(runnerConfig.CheckpointConfigurationId, Does.Contain(eventItem.DefinitionId));
        }

        [Test]
        public void EventItemConfigurationRejectsUnknownOrUnsupportedId()
        {
            GameSessionConfiguration sessionConfig = ConfigurationWithStage(OneEncounterConfig());
            Assert.Throws<ArgumentException>(() => new SimulationRunnerConfiguration(sessionConfig,
                eventItemDefinitionId: "missing"));
            var unsupported = new ItemDefinition("enemy-only", "Enemy", 0, "", ItemEffectTarget.AllEnemies,
                ItemEffectDuration.ThisBattle, new[] { new ItemEffectDefinition(ItemEffectKind.Damage, 2) });
            GameSessionConfiguration configWithUnsupported = ConfigurationWithStage(OneEncounterConfig(),
                eventItemDefinition: unsupported);
            Assert.Throws<ArgumentException>(() => new SimulationRunnerConfiguration(configWithUnsupported,
                eventItemDefinitionId: unsupported.DefinitionId));
        }

        [Test]
        public void AddingUnusedEventItemConfigurationDoesNotChangeNoEventTrialOrGameRng()
        {
            var eventItem = new ItemDefinition("event-unused", "Event", 0, "", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Star, 2) });
            var stage = new StageProgressConfig(2, 2, 1, 2, 0, 0, 0,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            GameSessionConfiguration plain = ConfigurationWithStage(stage);
            GameSessionConfiguration configured = ConfigurationWithStage(stage, eventItemDefinition: eventItem);
            SimulationTrialResult first = new SimulationRunner(new SimulationRunnerConfiguration(plain,
                maxTrialSteps: 500)).RunTrial(22, 987655, new ScriptedRunnerStrategy(RunnerBehavior.EnemylessThenStop));
            SimulationTrialResult second = new SimulationRunner(new SimulationRunnerConfiguration(configured,
                maxTrialSteps: 500, eventItemDefinitionId: eventItem.DefinitionId)).RunTrial(22, 987655,
                new ScriptedRunnerStrategy(RunnerBehavior.EnemylessThenStop));

            Assert.That(second.StopReason, Is.EqualTo(first.StopReason));
            Assert.That(SimulationStateFingerprint.Create(second.FinalCheckpoint),
                Is.EqualTo(SimulationStateFingerprint.Create(first.FinalCheckpoint)));
            Assert.That(second.FinalCheckpoint.RandomState, Is.EqualTo(first.FinalCheckpoint.RandomState));
        }

        [Test]
        public void DetailedStopDiagnosticsDoNotChangeTrialOutcomeOrRandomCheckpoints()
        {
            GameSessionConfiguration config = ConfigurationWithStage(OneEncounterConfig());
            SimulationTrialResult regular = new SimulationRunner(new SimulationRunnerConfiguration(config,
                recordExecutionHistory: false)).RunTrial(23, 987656,
                new ScriptedRunnerStrategy(RunnerBehavior.NoActionInPreparation));
            SimulationTrialResult detailed = new SimulationRunner(new SimulationRunnerConfiguration(config,
                recordExecutionHistory: true)).RunTrial(23, 987656,
                new ScriptedRunnerStrategy(RunnerBehavior.NoActionInPreparation));

            Assert.That(regular.StopReason, Is.EqualTo(detailed.StopReason));
            Assert.That(SimulationStateFingerprint.Create(regular.FinalCheckpoint),
                Is.EqualTo(SimulationStateFingerprint.Create(detailed.FinalCheckpoint)));
            Assert.That(regular.FinalCheckpoint.RandomState, Is.EqualTo(detailed.FinalCheckpoint.RandomState));
            Assert.That(regular.FinalStrategyRandomState, Is.EqualTo(detailed.FinalStrategyRandomState));
            Assert.That(detailed.ExecutionHistory.Count, Is.GreaterThan(0));
            Assert.That(detailed.ExecutionHistory.Last().AfterStateSummary, Does.Contain("board=["));
            Assert.That(detailed.ExecutionHistory.Last().AfterStateSummary, Does.Contain("hand=["));
        }

        [Test]
        public void UnresolvedBattleStopsWithoutRewardOrRetry()
        {
            var enemy = new EnemyDefinition("unresolved-run-enemy", "Unresolved", 0, 0, 100000,
                null, null, null, "T001", null, null, null);
            SimulationTrialResult result = Runner(OneEncounterConfig(), enemy: enemy,
                maxBattleRounds: 1).RunTrial(14, 12354,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.BattleUnresolved));
            Assert.That(result.Status, Is.EqualTo(SimulationTrialStatus.Stopped));
            Assert.That(result.FinalPhase, Is.EqualTo(StageProgressPhase.Battle));
            Assert.That(result.FinalCheckpoint.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(result.BattleCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedCount, Is.EqualTo(1));
        }

        [Test]
        public void DefeatStopsAsGameOverAndIsCountedSeparately()
        {
            var enemy = new EnemyDefinition("defeat-run-enemy", "Defeat", 10000, 0, 100000,
                null, null, null, "T001", null, null, null);
            SimulationTrialResult result = Runner(OneEncounterConfig(), enemy: enemy,
                maxBattleRounds: 3).RunTrial(15, 12355,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.GameOver));
            Assert.That(result.Status, Is.EqualTo(SimulationTrialStatus.GameOver));
            Assert.That(result.FinalPhase, Is.EqualTo(StageProgressPhase.GameOver));
            Assert.That(result.DefeatCount, Is.EqualTo(1));
            Assert.That(result.VictoryCount, Is.Zero);
        }

        [Test]
        public void ValidRejectedDecisionAdoptsStrategyRngButNotGameRng()
        {
            const long trialIndex = 16;
            const ulong seed = 12356;
            var enemy = new EnemyDefinition("run-enemy", "Run enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            GameSessionConfiguration config = ConfigurationWithStage(OneEncounterConfig(), enemy: enemy);
            SimulationTrialResult result = new SimulationRunner(new SimulationRunnerConfiguration(config,
                maxConsecutiveRejections: 1)).RunTrial(trialIndex, seed,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectWithStrategyRandom));
            GameSession expected = GameSession.Create(config, seed);
            expected.ExecuteAction(new SelectStageChoiceAction(new ActionId("trial:16:action:0"), StageChoice.SelectA));
            SimulationRandomStreamsState actualRandom = result.FinalCheckpoint.RandomState;
            SimulationRandomStreamsState expectedRandom = expected.CurrentState.RandomState;

            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.ConsecutiveRejectionLimitExceeded));
            Assert.That(actualRandom.Shop, Is.EqualTo(expectedRandom.Shop));
            Assert.That(actualRandom.Stats, Is.EqualTo(expectedRandom.Stats));
            Assert.That(actualRandom.Encounter, Is.EqualTo(expectedRandom.Encounter));
            Assert.That(actualRandom.Battle, Is.EqualTo(expectedRandom.Battle));
            Assert.That(actualRandom.Event, Is.EqualTo(expectedRandom.Event));
            Assert.That(actualRandom.Roster, Is.EqualTo(expectedRandom.Roster));
            Assert.That(actualRandom.Economy, Is.EqualTo(expectedRandom.Economy));
            Assert.That(result.FinalStrategyRandomState, Is.Not.EqualTo(
                StrategyRandomSource.Create(seed, "runner-test", "1").CaptureState()));
            Assert.That(result.FinalStrategyState.Payload, Is.EqualTo("2"));
        }

        [Test]
        public void NoActionInEnemyEncounterIsNotTreatedAsShopExit()
        {
            SimulationTrialResult result = Runner(OneEncounterConfig()).RunTrial(7, 12347,
                new ScriptedRunnerStrategy(RunnerBehavior.NoActionInPreparation));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.NoActionNotAllowed), result.Detail);
            Assert.That(result.FinalPhase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(result.FinalCheckpoint.Battle, Is.Null);
        }

        [Test]
        public void ConsecutiveRejectedActionsStopAtConfiguredLimitAndKeepSessionState()
        {
            SimulationTrialResult result = Runner(OneEncounterConfig(), rejectLimit: 2).RunTrial(8, 12348,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectPreparationAction));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.ConsecutiveRejectionLimitExceeded));
            Assert.That(result.RejectedActionCount, Is.EqualTo(2));
            Assert.That(result.SuccessfulActionCount, Is.EqualTo(1));
            Assert.That(result.FinalCheckpoint.Stage.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(result.FinalCheckpoint.ExecutedActionIds.Count, Is.EqualTo(1));
            Assert.That(result.LastActionError.HasValue, Is.True);
        }

        [Test]
        public void DecisionLimitIsNotAValidEnemylessExit()
        {
            SimulationTrialResult result = Runner(OneEncounterConfig(), phaseLimit: 2,
                rejectLimit: 10).RunTrial(9, 12349,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectPreparationAction));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.DecisionLimitExceeded));
            Assert.That(result.RejectedActionCount, Is.EqualTo(2));
            Assert.That(result.FinalPhase, Is.EqualTo(StageProgressPhase.Preparation));
        }

        [Test]
        public void StrategyExceptionAndInvalidDecisionDoNotAdoptStrategyCheckpoints()
        {
            var runner = Runner(OneEncounterConfig());
            SimulationTrialResult thrown = runner.RunTrial(10, 12350,
                new ScriptedRunnerStrategy(RunnerBehavior.Throw));
            SimulationTrialResult invalid = runner.RunTrial(10, 12350,
                new ScriptedRunnerStrategy(RunnerBehavior.ReturnNull));
            StrategyRngState initial = StrategyRandomSource.Create(12350, "runner-test", "1").CaptureState();

            Assert.That(thrown.StopReason, Is.EqualTo(SimulationStopReason.StrategyException));
            Assert.That(invalid.StopReason, Is.EqualTo(SimulationStopReason.InvalidStrategyDecision));
            Assert.That(thrown.DecisionCount, Is.EqualTo(1));
            Assert.That(invalid.DecisionCount, Is.EqualTo(1));
            Assert.That(thrown.FinalStrategyState.Payload, Is.Empty);
            Assert.That(invalid.FinalStrategyState.Payload, Is.Empty);
            Assert.That(thrown.FinalStrategyRandomState, Is.EqualTo(initial));
            Assert.That(invalid.FinalStrategyRandomState, Is.EqualTo(initial));
            Assert.That(thrown.FinalCheckpoint.ExecutedActionIds, Is.Empty);
            Assert.That(invalid.FinalCheckpoint.ExecutedActionIds, Is.Empty);
        }

        [Test]
        public void RejectedActionGetsFreshDeterministicIdOnRetry()
        {
            SimulationTrialResult first = Runner(OneEncounterConfig(), rejectLimit: 2).RunTrial(11, 12351,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectPreparationAction));
            SimulationTrialResult second = Runner(OneEncounterConfig(), rejectLimit: 2).RunTrial(11, 12351,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectPreparationAction));
            Assert.That(first.LastActionId.Value.Value, Is.EqualTo("trial:11:action:2"));
            Assert.That(first.NextActionId.Value, Is.EqualTo("trial:11:action:3"));
            Assert.That(first.LastActionId, Is.EqualTo(second.LastActionId));
            Assert.That(first.StopReason, Is.EqualTo(second.StopReason));
        }

        [Test]
        public void TrialStepLimitPreservesCurrentCheckpointAndIsNotNormalCompletion()
        {
            SimulationTrialResult result = Runner(OneEncounterConfig(), stepLimit: 1).RunTrial(12, 12352,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.TrialStepLimitExceeded));
            Assert.That(result.Status, Is.EqualTo(SimulationTrialStatus.Stopped));
            Assert.That(result.FinalCheckpoint, Is.Not.Null);
        }

        [Test]
        public void NegativeTrialIndexIsRejectedBeforeSessionCreation()
        {
            SimulationTrialResult result = Runner(OneEncounterConfig()).RunTrial(-1, 1,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));
            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.InvalidConfiguration));
            Assert.That(result.FinalCheckpoint, Is.Null);
        }

        [Test]
        public void RejectionDiagnosticsSelectsConsecutiveStopsAndGroupsLastRejectedKindsAndReasons()
        {
            var session = ConfigurationWithStage(OneEncounterConfig());
            var runner = new SimulationRunner(new SimulationRunnerConfiguration(session,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 10, maxTrialSteps: 100,
                recordExecutionHistory: true));
            SimulationTrialResult result = runner.RunTrial(41, 554433,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectDifferentActions));

            Assert.That(result.StopReason, Is.EqualTo(SimulationStopReason.ConsecutiveRejectionLimitExceeded));
            Assert.That(result.ConsecutiveRejectionCount, Is.EqualTo(10));
            var formatter = FindLoadedType("NSTSimulationRejectionDiagnostics");
            Assert.That(formatter, Is.Not.Null, "Editor diagnostic formatter must be loaded in EditMode.");
            MethodInfo include = formatter.GetMethod("IsDiagnosticStopReason", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(include, Is.Not.Null);
            Assert.That(include.Invoke(null, new object[] { SimulationStopReason.ConsecutiveRejectionLimitExceeded }), Is.EqualTo(true));
            Assert.That(include.Invoke(null, new object[] { SimulationStopReason.NoActionNotAllowed }), Is.EqualTo(true));

            MethodInfo trailing = formatter.GetMethod("GetTrailingRejectionRecords", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(trailing, Is.Not.Null);
            var trailingRecords = (IReadOnlyList<SimulationExecutionRecord>)trailing.Invoke(null,
                new object[] { result.ExecutionHistory, 10 });
            Assert.That(trailingRecords.Count, Is.EqualTo(10), "All ten rejected Actions immediately before the stop must be retained.");
            Assert.That(trailingRecords.All(x => x.ActionStatus == ActionDispatchStatus.Rejected), Is.True);
            Assert.That(trailingRecords.Select(x => x.Action.Kind).Distinct().Count(), Is.EqualTo(2));
            Assert.That(((BuyCharacterAction)trailingRecords[0].Action).OfferId, Is.EqualTo("missing-offer"));
            Assert.That(((RerollShopAction)trailingRecords[1].Action).ShopKind, Is.EqualTo(ShopKind.Item));

            string csv = SimulationRejectionSummaryCsvExporter.Export(new[] { result });
            Assert.That(csv, Does.StartWith("StrategyId,StrategyVersion,TrialNumber,Stage,Round,Phase,StopReason,RejectedActionKind,RejectionReasonCode,RejectionCount"));
            Assert.That(csv, Does.Contain("BuyCharacter"));
            Assert.That(csv, Does.Contain("UnknownOffer"));
            Assert.That(csv, Does.Contain("RerollShop"));
            Assert.That(csv, Does.Contain("InvalidAction"));
            string[] rows = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(rows.Length, Is.EqualTo(3),
                "The final rejection streak must be grouped as one row per ActionKind/reason pair.");
            Assert.That(rows[1], Does.EndWith(",5"));
            Assert.That(rows[2], Does.EndWith(",5"));
        }

        [Test]
        public void ExecutionHistoryDiagnosticsDoNotChangeTrialStateOrRandomCheckpoints()
        {
            var session = ConfigurationWithStage(OneEncounterConfig());
            var withoutHistory = new SimulationRunner(new SimulationRunnerConfiguration(session,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 10, maxTrialSteps: 100,
                recordExecutionHistory: false)).RunTrial(42, 554434,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectDifferentActions));
            var withHistory = new SimulationRunner(new SimulationRunnerConfiguration(session,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 10, maxTrialSteps: 100,
                recordExecutionHistory: true)).RunTrial(42, 554434,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectDifferentActions));

            Assert.That(withHistory.StopReason, Is.EqualTo(withoutHistory.StopReason));
            Assert.That(withHistory.TrialSeed, Is.EqualTo(withoutHistory.TrialSeed));
            Assert.That(SimulationStateFingerprint.Create(withHistory.FinalCheckpoint),
                Is.EqualTo(SimulationStateFingerprint.Create(withoutHistory.FinalCheckpoint)));
            Assert.That(withHistory.FinalGameRandomState, Is.EqualTo(withoutHistory.FinalGameRandomState));
            Assert.That(withHistory.FinalStrategyRandomState, Is.EqualTo(withoutHistory.FinalStrategyRandomState));
            Assert.That(withHistory.ActionSequence, Is.EqualTo(withoutHistory.ActionSequence));
            Assert.That(withHistory.SuccessfulActionCount, Is.EqualTo(withoutHistory.SuccessfulActionCount));
            Assert.That(withHistory.RejectedActionCount, Is.EqualTo(withoutHistory.RejectedActionCount));
        }

        private static Type FindLoadedType(string name)
        {
            Assembly editorAssembly = UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "Assembly-CSharp-Editor");
            return editorAssembly == null ? null : editorAssembly.GetType(name, false);
        }


        [Test]
        public void OptionalProfilerAggregatesNestedExclusiveTimeAndCounters()
        {
            var profiler = new SimulationProfileCollector();
            using (profiler.Measure("parent"))
            using (profiler.Measure("child")) { profiler.Add("actions", 2); }
            var parent = profiler.Sections.Single(x => x.SectionId == "parent");
            var child = profiler.Sections.Single(x => x.SectionId == "child");
            Assert.That(parent.CallCount, Is.EqualTo(1));
            Assert.That(child.CallCount, Is.EqualTo(1));
            Assert.That(parent.ExclusiveSeconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(parent.InclusiveSeconds, Is.GreaterThanOrEqualTo(parent.ExclusiveSeconds));
            Assert.That(profiler.Counters["actions"], Is.EqualTo(2));
        }

        [Test]
        public void ProfilingEnabledAndDisabledKeepTrialOutcomeAndRandomCheckpointsEqual()
        {
            StageProgressConfig stage = OneEncounterConfig();
            GameSessionConfiguration config = ConfigurationWithStage(stage);
            var plainRunner = new SimulationRunner(new SimulationRunnerConfiguration(config));
            var profile = new SimulationProfileCollector();
            var profiledRunner = new SimulationRunner(new SimulationRunnerConfiguration(config, profileCollector: profile));
            SimulationTrialResult plain = plainRunner.RunTrial(44, 991827, new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));
            SimulationTrialResult measured = profiledRunner.RunTrial(44, 991827, new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));
            Assert.That(measured.StopReason, Is.EqualTo(plain.StopReason));
            Assert.That(measured.DecisionCount, Is.EqualTo(plain.DecisionCount));
            Assert.That(measured.SuccessfulActionCount, Is.EqualTo(plain.SuccessfulActionCount));
            Assert.That(measured.RejectedActionCount, Is.EqualTo(plain.RejectedActionCount));
            Assert.That(SimulationStateFingerprint.Create(measured.FinalCheckpoint), Is.EqualTo(SimulationStateFingerprint.Create(plain.FinalCheckpoint)));
            Assert.That(measured.FinalGameRandomState, Is.EqualTo(plain.FinalGameRandomState));
            Assert.That(measured.FinalStrategyRandomState, Is.EqualTo(plain.FinalStrategyRandomState));
            Assert.That(measured.FinalStrategyState.Payload, Is.EqualTo(plain.FinalStrategyState.Payload));
            Assert.That(profile.Sections.Any(x => x.SectionId == "Runner.ObservationBuild"), Is.True);
            Assert.That(profile.Sections.Any(x => x.SectionId == "Action.Execute.BuyCharacter"), Is.True);
        }


        [Test]
        public void OptionalProfilerDoesNotChangeRandomOrHeuristicActionSequence()
        {
            GameSessionConfiguration config = ConfigurationWithStage(OneEncounterConfig());
            foreach (bool heuristic in new[] { false, true })
            {
                IPlayerStrategy Create() => heuristic ? (IPlayerStrategy)new HeuristicStrategy() : new RandomStrategy();
                var plainRunner = new SimulationRunner(new SimulationRunnerConfiguration(config, recordExecutionHistory: true));
                var profiler = new SimulationProfileCollector();
                var profiledRunner = new SimulationRunner(new SimulationRunnerConfiguration(config, recordExecutionHistory: true, profileCollector: profiler));
                SimulationTrialResult plain = plainRunner.RunTrial(57, 110028, Create());
                SimulationTrialResult profiled = profiledRunner.RunTrial(57, 110028, Create());
                var plainActions = plain.ExecutionHistory.Where(x => x.Kind == SimulationExecutionEventKind.Action).Select(x => x.Action.Kind + "|" + x.Action.ActionId.Value).ToArray();
                var profiledActions = profiled.ExecutionHistory.Where(x => x.Kind == SimulationExecutionEventKind.Action).Select(x => x.Action.Kind + "|" + x.Action.ActionId.Value).ToArray();
                Assert.That(profiledActions, Is.EqualTo(plainActions), heuristic ? "Heuristic action sequence changed with profiling." : "Random action sequence changed with profiling.");
                Assert.That(profiled.StopReason, Is.EqualTo(plain.StopReason));
                Assert.That(SimulationStateFingerprint.Create(profiled.FinalCheckpoint), Is.EqualTo(SimulationStateFingerprint.Create(plain.FinalCheckpoint)));
                Assert.That(profiled.FinalGameRandomState, Is.EqualTo(plain.FinalGameRandomState));
                Assert.That(profiled.FinalStrategyRandomState, Is.EqualTo(plain.FinalStrategyRandomState));
                Assert.That(profiled.FinalStrategyState.Payload, Is.EqualTo(plain.FinalStrategyState.Payload));
            }
        }

        private enum RunnerBehavior { CompleteNormal, EnemylessThenStop, NoActionInPreparation, RejectPreparationAction, RejectWithStrategyRandom, RejectDifferentActions, Throw, ReturnNull }

        private sealed class ScriptedRunnerStrategy : IPlayerStrategy
        {
            private readonly RunnerBehavior _behavior;
            public string StrategyId => "runner-test";
            public string Version => "1";
            public ScriptedRunnerStrategy(RunnerBehavior behavior) { _behavior = behavior; }

            public StrategyDecision ChooseAction(GameObservation observation, StrategyContext context)
            {
                if (_behavior == RunnerBehavior.Throw) throw new InvalidOperationException("scripted failure");
                if (_behavior == RunnerBehavior.ReturnNull) return null;
                int previous = context.State.Payload.Length == 0 ? 0 : int.Parse(context.State.Payload);
                StrategyState next = new StrategyState(StrategyId, Version, 1, (previous + 1).ToString());
                if (observation.Stage.Phase == ObservationPhase.AwaitingStageChoice)
                {
                    StageChoice choice = _behavior == RunnerBehavior.EnemylessThenStop
                        ? StageChoice.SelectB : StageChoice.SelectA;
                    return context.Decide(new SelectStageChoiceAction(context.NextActionId, choice), next);
                }
                if (observation.Stage.Phase == ObservationPhase.Preparation)
                {
                    if (_behavior == RunnerBehavior.RejectPreparationAction)
                        return context.Decide(new BuyCharacterAction(context.NextActionId, "missing-offer",
                            new GameActionLocation(CharacterLocationKind.Board, 0)), next);
                    if (_behavior == RunnerBehavior.RejectDifferentActions)
                        return previous % 2 == 1
                            ? context.Decide(new BuyCharacterAction(context.NextActionId, "missing-offer",
                                new GameActionLocation(CharacterLocationKind.Board, 0)), next)
                            : context.Decide(new RerollShopAction(context.NextActionId, ShopKind.Item), next);
                    if (_behavior == RunnerBehavior.RejectWithStrategyRandom)
                    {
                        StrategyRandomSource random = context.CreateStrategyRandomSource();
                        random.NextInt(0, 100);
                        return context.DecideWithRng(new BuyCharacterAction(context.NextActionId, "missing-offer",
                            new GameActionLocation(CharacterLocationKind.Board, 0)), next, random.CaptureState());
                    }
                    if (_behavior == RunnerBehavior.CompleteNormal)
                    {
                        if (observation.Board.Slots.Any(slot => slot.Character != null))
                            return context.Decide(new StartBattleAction(context.NextActionId), next);
                        var offer = observation.Shop.Offers.First(item => item.Kind == ShopOfferKind.Character &&
                            item.Status == ShopOfferStatus.Available);
                        return context.Decide(new BuyCharacterAction(context.NextActionId, offer.OfferId,
                            new GameActionLocation(CharacterLocationKind.Board, 0)), next);
                    }
                    return context.NoAction(next, "No further preparation action.");
                }
                return context.NoAction(next);
            }
        }
    }
}
