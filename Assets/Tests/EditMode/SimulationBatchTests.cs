using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameSessionTests
    {
        private enum BatchBehavior { Complete, StartBattleWithoutBuying, NoAction, Throw }

        private static SimulationStrategyFactory BatchFactory(string id, string version,
            BatchBehavior behavior) => new SimulationStrategyFactory(id, version,
                () => new BatchTestStrategy(id, version, behavior));

        private static SimulationBatchConfiguration BatchConfiguration(int trials,
            params SimulationStrategyFactory[] factories)
        {
            var enemy = new EnemyDefinition("batch-enemy", "Batch enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            var runner = new SimulationRunnerConfiguration(
                ConfigurationWithStage(OneEncounterConfig(), enemy: enemy),
                maxDecisionsPerPhase: 30, maxConsecutiveRejections: 3, maxTrialSteps: 200);
            return new SimulationBatchConfiguration(5001, trials, factories, runner);
        }

        [Test]
        public void BatchConfigurationPreservesAnAlreadyNormalizedPoolAccountingIdentity()
        {
            const string identity="game-config|roster:test|event-item:Item006|pool-accounting:v2";
            var runner=new SimulationRunnerConfiguration(ConfigurationWithStage(OneEncounterConfig()));
            var batch=new SimulationBatchConfiguration(5001,1,
                new[]{BatchFactory("identity-test","1",BatchBehavior.Complete)},runner,
                SimulationBatchResultRetention.FullTrialResults,identity);
            Assert.That(batch.BatchConfigurationId,Is.EqualTo(identity));
            Assert.That(batch.BatchConfigurationId.Split(new[]{"pool-accounting:v2"},StringSplitOptions.None),Has.Length.EqualTo(2));
        }

        [Test]
        public void BatchRunsEachStrategyAgainstSameZeroBasedSeedListAndKeepsFullResults()
        {
            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(BatchConfiguration(3,
                BatchFactory("batch-a", "1", BatchBehavior.Complete),
                BatchFactory("batch-b", "1", BatchBehavior.Complete)));

            Assert.That(result.TrialCount, Is.EqualTo(3));
            Assert.That(result.TrialSeedAlgorithm, Is.EqualTo(TrialSeedGenerator.AlgorithmId));
            Assert.That(result.GameCoreRandomCheckpointVersion, Is.EqualTo(2));
            Assert.That(result.TrialSeeds.Select(x => x.TrialIndex), Is.EqualTo(new ulong[] { 0, 1, 2 }));
            Assert.That(result.TrialResults.Count, Is.EqualTo(6));
            Assert.That(result.StrategySummaries.Select(x => x.StrategyId), Is.EqualTo(new[] { "batch-a", "batch-b" }));
            foreach (ulong trialIndex in new ulong[] { 0, 1, 2 })
            {
                var rows = result.TrialResults.Where(x => (ulong)x.TrialIndex == trialIndex).ToArray();
                Assert.That(rows.Length, Is.EqualTo(2));
                Assert.That(rows[0].TrialSeed, Is.EqualTo(rows[1].TrialSeed));
                Assert.That(rows.All(x => x.FullResult != null), Is.True);
                Assert.That(rows.Select(x => x.FullResult.FinalCheckpoint).Distinct().Count(), Is.EqualTo(2));
            }
        }

        [Test]
        public void BatchProgressCallbacksCoverEveryTrialWithoutChangingResults()
        {
            SimulationBatchConfiguration configuration = BatchConfiguration(4,
                BatchFactory("progress", "1", BatchBehavior.Complete));
            SimulationBatchResult baseline = new SimulationBatchRunner().RunBatch(configuration);
            var progress = new System.Collections.Generic.List<SimulationBatchProgress>();
            SimulationBatchResult observed = new SimulationBatchRunner().RunBatch(configuration, progress.Add);

            Assert.That(progress.Count, Is.EqualTo(4));
            Assert.That(progress.Select(x => x.CompletedTrialCount), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(progress.All(x => x.TotalTrialCount == 4 && x.CurrentStrategyId == "progress"), Is.True);
            Assert.That(progress.Select(x => x.CurrentTrialIndex), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(progress.All(x => x.LastTrialElapsedSeconds >= 0), Is.True);
            Assert.That(observed.TrialSeeds.Select(x => x.TrialSeed), Is.EqualTo(baseline.TrialSeeds.Select(x => x.TrialSeed)));
            Assert.That(observed.TrialResults.Select(x => SimulationStateFingerprint.Create(x.FullResult.FinalCheckpoint)),
                Is.EqualTo(baseline.TrialResults.Select(x => SimulationStateFingerprint.Create(x.FullResult.FinalCheckpoint))));
            Assert.That(observed.TrialResults.Select(x => x.FullResult.FinalGameRandomState),
                Is.EqualTo(baseline.TrialResults.Select(x => x.FullResult.FinalGameRandomState)));
        }

        [Test]
        public void IncreasingBatchTrialCountPreservesTheExistingSeededPrefix()
        {
            SimulationStrategyFactory factory = BatchFactory("prefix", "1", BatchBehavior.Complete);
            SimulationBatchResult shorter = new SimulationBatchRunner().RunBatch(BatchConfiguration(3, factory));
            SimulationBatchResult longer = new SimulationBatchRunner().RunBatch(BatchConfiguration(5, factory));

            for (int i = 0; i < shorter.TrialCount; i++)
            {
                SimulationBatchTrialResult before = shorter.TrialResults.Single(x => x.TrialIndex == i);
                SimulationBatchTrialResult after = longer.TrialResults.Single(x => x.TrialIndex == i);
                Assert.That(after.TrialSeed, Is.EqualTo(before.TrialSeed));
                Assert.That(after.FullResult.Status, Is.EqualTo(before.FullResult.Status));
                Assert.That(after.FullResult.DecisionCount, Is.EqualTo(before.FullResult.DecisionCount));
                Assert.That(SimulationStateFingerprint.Create(after.FullResult.FinalCheckpoint),
                    Is.EqualTo(SimulationStateFingerprint.Create(before.FullResult.FinalCheckpoint)));
            }
        }

        [Test]
        public void BatchRunsRandomAndHeuristicStrategiesFromFreshInstancesOnSharedSeeds()
        {
            var config = BatchConfiguration(2,
                new SimulationStrategyFactory(RandomStrategy.StableId, RandomStrategy.StableVersion,
                    () => new RandomStrategy()),
                new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.StableVersion,
                    () => new HeuristicStrategy()));

            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(config);

            Assert.That(result.TrialResults.Count, Is.EqualTo(4));
            Assert.That(result.StrategySummaries.Select(x => x.StrategyId),
                Is.EqualTo(new[] { HeuristicStrategy.StableId, RandomStrategy.StableId }));
            Assert.That(result.TrialResults.All(x => x.HasSimulationResult), Is.True);
            foreach (ulong trialIndex in new ulong[] { 0, 1 })
            {
                var rows = result.TrialResults.Where(x => (ulong)x.TrialIndex == trialIndex).ToArray();
                Assert.That(rows.Length, Is.EqualTo(2));
                Assert.That(rows[0].TrialSeed, Is.EqualTo(rows[1].TrialSeed));
                Assert.That(rows.All(x => x.FullResult != null), Is.True);
            }
        }

        [Test]
        public void StrategySummariesCountTerminalAndStoppedTrialsWithoutConflation()
        {
            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(BatchConfiguration(2,
                BatchFactory("complete", "1", BatchBehavior.Complete),
                BatchFactory("stop", "1", BatchBehavior.Throw)));
            StrategyBatchSummary complete = result.StrategySummaries.Single(x => x.StrategyId == "complete");
            StrategyBatchSummary stop = result.StrategySummaries.Single(x => x.StrategyId == "stop");

            Assert.That(complete.TotalTrials, Is.EqualTo(2));
            Assert.That(complete.CompletedCount + complete.GameOverCount + complete.StoppedCount,
                Is.EqualTo(complete.TotalTrials));
            Assert.That(complete.ResolvedRate + complete.StoppedRate, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(complete.WinRate, Is.EqualTo(1.0));
            Assert.That(complete.CompletionRate, Is.EqualTo(1.0));
            Assert.That(complete.GameOverRate, Is.Zero);
            Assert.That(stop.CompletedCount, Is.Zero);
            Assert.That(stop.GameOverCount, Is.Zero);
            Assert.That(stop.StoppedCount, Is.EqualTo(2));
            Assert.That(stop.WinRate, Is.Null);
            Assert.That(stop.HasWinRate, Is.False);
            Assert.That(stop.ResolvedRate, Is.Zero);
            Assert.That(stop.StoppedRate, Is.EqualTo(1.0));
            Assert.That(stop.StopReasonCounts["strategy_exception"], Is.EqualTo(2));
            Assert.That(stop.StopReasonCounts.Values.Sum(), Is.EqualTo(2));
        }

        [Test]
        public void SummaryOnlyRetentionKeepsAggregateRowsButDropsFullCheckpoints()
        {
            SimulationBatchConfiguration source = BatchConfiguration(1,
                BatchFactory("summary", "1", BatchBehavior.Complete));
            var config = new SimulationBatchConfiguration(source.BaseSeed, source.TrialCount,
                source.Strategies, source.RunnerConfiguration, SimulationBatchResultRetention.SummaryOnly);
            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(config);
            Assert.That(result.ResultRetention, Is.EqualTo(SimulationBatchResultRetention.SummaryOnly));
            Assert.That(result.TrialResults.Count, Is.EqualTo(1));
            Assert.That(result.TrialResults[0].FullResult, Is.Null);
            Assert.That(result.TrialResults[0].HasSimulationResult, Is.True);
            Assert.That(result.TrialResults[0].HasMetricSample, Is.True);
            Assert.That(result.StrategySummaries[0].ReachedStage.SampleCount, Is.EqualTo(1));
        }

        [Test]
        public void TrialSeedIsStableAcrossStrategyOrderAndUsesIndexStartingAtZero()
        {
            SimulationBatchConfiguration forward = BatchConfiguration(4,
                BatchFactory("zeta", "1", BatchBehavior.Complete),
                BatchFactory("alpha", "1", BatchBehavior.Complete));
            SimulationBatchConfiguration reverse = BatchConfiguration(4,
                BatchFactory("alpha", "1", BatchBehavior.Complete),
                BatchFactory("zeta", "1", BatchBehavior.Complete));
            SimulationBatchResult a = new SimulationBatchRunner().RunBatch(forward);
            SimulationBatchResult b = new SimulationBatchRunner().RunBatch(reverse);

            Assert.That(a.TrialSeeds.Select(x => x.TrialSeed), Is.EqualTo(b.TrialSeeds.Select(x => x.TrialSeed)));
            Assert.That(a.TrialSeeds.Select(x => x.TrialSeed).Distinct().Count(), Is.EqualTo(4));
            Assert.That(a.BatchConfigurationId, Is.EqualTo(b.BatchConfigurationId));
            Assert.That(a.TrialResults.Select(Signature), Is.EqualTo(b.TrialResults.Select(Signature)));
            for (int i = 0; i < 4; i++)
                Assert.That(a.TrialSeeds[i], Is.EqualTo(TrialSeedGenerator.Create(5001, (ulong)i)));
        }

        [Test]
        public void PairComparisonUsesCommonResolvedSeedsAndReportsZeroWhenNone()
        {
            SimulationBatchResult resolved = new SimulationBatchRunner().RunBatch(BatchConfiguration(3,
                BatchFactory("pair", "1", BatchBehavior.Complete),
                BatchFactory("pair", "2", BatchBehavior.Complete)));
            StrategyComparisonResult comparison = resolved.Comparisons.Single();
            Assert.That(comparison.CommonResolvedTrialCount, Is.EqualTo(3));
            Assert.That(comparison.BothCompletedCount, Is.EqualTo(3));
            Assert.That(comparison.BothGameOverCount, Is.Zero);
            Assert.That(comparison.FirstCommonSeedWinRate, Is.EqualTo(1.0));
            Assert.That(comparison.SecondCommonSeedWinRate, Is.EqualTo(1.0));
            Assert.That(comparison.CommonSeedWinRateDifferencePercentagePoints, Is.EqualTo(0.0));

            SimulationBatchResult stopped = new SimulationBatchRunner().RunBatch(BatchConfiguration(2,
                BatchFactory("pair", "1", BatchBehavior.Complete),
                BatchFactory("pair", "2", BatchBehavior.Throw)));
            StrategyComparisonResult noCommon = stopped.Comparisons.Single();
            Assert.That(noCommon.CommonResolvedTrialCount, Is.Zero);
            Assert.That(noCommon.FirstCommonSeedWinRate, Is.Null);
            Assert.That(noCommon.SecondCommonSeedWinRate, Is.Null);
            Assert.That(noCommon.CommonSeedWinRateDifferencePercentagePoints, Is.Null);
        }

        [Test]
        public void BatchFactoryFailureIsRecordedAndDoesNotStopOtherTrials()
        {
            var failing = new SimulationStrategyFactory("factory-error", "1",
                () => throw new InvalidOperationException("factory failed"));
            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(BatchConfiguration(2,
                failing, BatchFactory("working", "1", BatchBehavior.Complete)));
            StrategyBatchSummary failure = result.StrategySummaries.Single(x => x.StrategyId == "factory-error");
            StrategyBatchSummary working = result.StrategySummaries.Single(x => x.StrategyId == "working");
            Assert.That(failure.BatchExecutionFailureCount, Is.EqualTo(2));
            Assert.That(failure.StoppedCount, Is.EqualTo(2));
            Assert.That(failure.StopReasonCounts["batch_execution_error"], Is.EqualTo(2));
            Assert.That(working.CompletedCount, Is.EqualTo(2));
            Assert.That(result.TrialResults.All(x => x.HasSimulationResult ||
                x.ExecutionStatus == SimulationBatchExecutionStatus.StrategyFactoryFailure), Is.True);
        }

        [Test]
        public void ReusingMutableStrategyObjectAcrossTrialsIsRejected()
        {
            var shared = new BatchTestStrategy("shared", "1", BatchBehavior.Complete);
            var factory = new SimulationStrategyFactory("shared", "1", () => shared);
            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(BatchConfiguration(2, factory));
            Assert.That(result.TrialResults[0].HasSimulationResult, Is.True);
            Assert.That(result.TrialResults[1].HasSimulationResult, Is.False);
            Assert.That(result.TrialResults[1].ExecutionStatus, Is.EqualTo(SimulationBatchExecutionStatus.RunnerFailure));
            Assert.That(result.StrategySummaries[0].BatchExecutionFailureCount, Is.EqualTo(1));
        }

        [Test]
        public void BatchRejectsInvalidCountsEmptyAndDuplicateStrategyIdentities()
        {
            var runner = new SimulationRunnerConfiguration(ConfigurationWithStage(OneEncounterConfig()));
            var factory = BatchFactory("duplicate", "1", BatchBehavior.Throw);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationBatchConfiguration(1, 0,
                new[] { factory }, runner));
            Assert.Throws<ArgumentException>(() => new SimulationBatchConfiguration(1, 2,
                Array.Empty<SimulationStrategyFactory>(), runner));
            Assert.Throws<ArgumentException>(() => new SimulationBatchConfiguration(1, 2,
                new[] { factory, BatchFactory("duplicate", "1", BatchBehavior.Throw) }, runner));
            Assert.Throws<ArgumentNullException>(() => new SimulationBatchConfiguration(1, 2,
                new[] { factory }, null));
        }

        [Test]
        public void BatchPreflightRejectsInvalidSessionConfigurationBeforeAnyStrategyRuns()
        {
            CharacterDefinition[] shortRoster = Roster.Take(29).ToArray();
            GameSessionConfiguration invalid = ConfigurationWithStage(OneEncounterConfig());
            var invalidSession = new GameSessionConfiguration(invalid.StageConfig, invalid.EconomyConfig,
                invalid.CharacterShopConfig, invalid.CharacterStatSettings, invalid.StageRewardConfig,
                invalid.BattleConfig, invalid.StartingEconomy, invalid.CharacterDefinitions,
                shortRoster, invalid.FullCharacterRoster, invalid.ItemDefinitions,
                invalid.EnemyDefinitions, invalid.EncounterPools, invalid.ShopRerollGoldCost);
            var batch = new SimulationBatchConfiguration(5001, 1,
                new[] { BatchFactory("never-called", "1", BatchBehavior.Throw) },
                new SimulationRunnerConfiguration(invalidSession));
            Assert.Throws<ArgumentException>(() => new SimulationBatchRunner().RunBatch(batch));
        }

        [Test]
        public void SummaryAndResultCollectionsAreReadOnlyAndMetricsIncludeStoppedTrials()
        {
            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(BatchConfiguration(2,
                BatchFactory("complete", "1", BatchBehavior.Complete),
                BatchFactory("stopped", "1", BatchBehavior.NoAction)));
            StrategyBatchSummary summary = result.StrategySummaries.Single(x => x.StrategyId == "complete");
            Assert.That(summary.ReachedStage.Mean, Is.EqualTo(1.0));
            Assert.That(summary.ReachedStage.Median, Is.EqualTo(1.0));
            Assert.That(summary.ReachedStage.Maximum, Is.EqualTo(1.0));
            Assert.That(summary.FinalHP.SampleCount, Is.EqualTo(2));
            Assert.That(result.TrialResults.Count, Is.EqualTo(4));
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<SimulationBatchTrialResult>)result.TrialResults).Clear());
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IDictionary<string, long>)summary.StopReasonCounts).Clear());
        }

        private static string Signature(SimulationBatchTrialResult row) =>
            row.StrategyId + ":" + row.StrategyVersion + ":" + row.TrialIndex + ":" + row.TrialSeed +
            ":" + row.StopReasonId + ":" + row.FinalGold + ":" + row.FinalHP + ":" + row.DecisionCount;

        private sealed class BatchTestStrategy : IPlayerStrategy
        {
            private readonly BatchBehavior _behavior;
            public string StrategyId { get; }
            public string Version { get; }
            public BatchTestStrategy(string id, string version, BatchBehavior behavior)
            { StrategyId = id; Version = version; _behavior = behavior; }

            public StrategyDecision ChooseAction(GameObservation observation, StrategyContext context)
            {
                if (_behavior == BatchBehavior.Throw) throw new InvalidOperationException("batch strategy failure");
                StrategyState next = new StrategyState(StrategyId, Version, 1,
                    (context.State.Payload.Length == 0 ? 1 : int.Parse(context.State.Payload) + 1).ToString());
                if (observation.Stage.Phase == ObservationPhase.AwaitingStageChoice)
                    return context.Decide(new SelectStageChoiceAction(context.NextActionId, StageChoice.SelectA), next);
                if (observation.Stage.Phase == ObservationPhase.Preparation)
                {
                    if (_behavior == BatchBehavior.NoAction) return context.NoAction(next);
                    if (_behavior == BatchBehavior.StartBattleWithoutBuying)
                        return context.Decide(new StartBattleAction(context.NextActionId), next);
                    if (observation.Board.Slots.Any(slot => slot.Character != null))
                        return context.Decide(new StartBattleAction(context.NextActionId), next);
                    ShopOfferObservation offer = observation.Shop.Offers.First(x =>
                        x.Kind == ShopOfferKind.Character && x.Status == ShopOfferStatus.Available);
                    return context.Decide(new BuyCharacterAction(context.NextActionId, offer.OfferId,
                        new GameActionLocation(CharacterLocationKind.Board, 0)), next);
                }
                return context.NoAction(next);
            }
        }
    }
}
