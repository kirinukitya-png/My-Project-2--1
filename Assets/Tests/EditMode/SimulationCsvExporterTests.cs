using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;
using ProjectNST.SimulationExport;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameSessionTests
    {
        [Test]
        public void CsvExportsAllFourTablesWithFixedHeadersAndDeterministicRowCounts()
        {
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(BatchConfiguration(2,
                BatchFactory("zeta", "1", BatchBehavior.Complete), BatchFactory("alpha", "1", BatchBehavior.Complete)));
            SimulationExportResult trials = SimulationCsvExporter.ExportTrialsCsv(batch);
            SimulationExportResult summaries = SimulationCsvExporter.ExportStrategySummaryCsv(batch);
            SimulationExportResult comparisons = SimulationCsvExporter.ExportStrategyComparisonCsv(batch);
            SimulationExportResult stops = SimulationCsvExporter.ExportStopReasonsCsv(batch);

            Assert.That(trials.Succeeded && summaries.Succeeded && comparisons.Succeeded && stops.Succeeded, Is.True);
            Assert.That(CountLines(trials.CsvText), Is.EqualTo(5));
            Assert.That(CountLines(summaries.CsvText), Is.EqualTo(3));
            Assert.That(CountLines(comparisons.CsvText), Is.EqualTo(2));
            Assert.That(CountLines(stops.CsvText), Is.EqualTo(1 + batch.StrategySummaries.Sum(x => x.StopReasonCounts.Count)));
            Assert.That(trials.CsvText.Split('\n')[0].TrimEnd('\r'), Is.EqualTo(
                "TrialNumber,TrialSeed,StrategyId,StrategyVersion,StopReason,IsResolved,IsCompleted,FinalStage,FinalRound,FinalHP,FinalGold,FinalLevel,FinalXP,DecisionCount,SuccessfulActionCount,RejectedActionCount,BattleCount,VictoryCount,DefeatCount,UnresolvedCount"));
            Assert.That(summaries.CsvText.Split('\n')[0].TrimEnd('\r').StartsWith("StrategyId,StrategyVersion,TotalTrials,CompletedCount,GameOverCount,StoppedCount,WinRate,"), Is.True);
            Assert.That(comparisons.CsvText.Split('\n')[0].TrimEnd('\r').StartsWith("StrategyAId,StrategyAVersion,StrategyBId,StrategyBVersion,"), Is.True);
            Assert.That(stops.CsvText.Split('\n')[0].TrimEnd('\r'), Is.EqualTo("StrategyId,StrategyVersion,StopReason,Count,PercentageOfTotalTrials"));
            Assert.That(stops.CsvText.Contains("alpha,1,completed,2,100"), Is.True);
            Assert.That(trials.CsvText.IndexOf("\n0,", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
            Assert.That(trials.CsvText.IndexOf("\n0,", StringComparison.Ordinal), Is.LessThan(trials.CsvText.IndexOf("\n1,", StringComparison.Ordinal)));
        }

        [Test]
        public void SummaryCsvUsesExistingWinRateAndLeavesUndefinedWinRateBlank()
        {
            SimulationBatchResult mixed = new SimulationBatchRunner().RunBatch(BatchConfiguration(2,
                BatchFactory("complete", "1", BatchBehavior.Complete), BatchFactory("stopped", "1", BatchBehavior.Throw)));
            string[] rows = SimulationCsvExporter.ExportStrategySummaryCsv(mixed).CsvText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            string complete = rows.Single(x => x.StartsWith("complete,", StringComparison.Ordinal));
            string stopped = rows.Single(x => x.StartsWith("stopped,", StringComparison.Ordinal));
            Assert.That(complete.Split(',')[6], Is.EqualTo("1"));
            Assert.That(complete.Split(',')[7], Is.EqualTo("1"));
            Assert.That(complete.Split(',')[8], Is.EqualTo("0"));
            Assert.That(stopped.Split(',')[6], Is.Empty);
            Assert.That(stopped.Split(',')[7], Is.EqualTo("0"));
            Assert.That(stopped.Split(',')[8], Is.EqualTo("1"));
        }

        [Test]
        public void SummaryWinRateUsesOnlyCompletedAndGameOverAndExportsGameOverOnlyAsZero()
        {
            SimulationBatchConfiguration source = BatchConfiguration(1,
                BatchFactory("only-loss", "1", BatchBehavior.StartBattleWithoutBuying));
            var enemy = new ProjectNST.GameCore.Enemies.EnemyDefinition("csv-defeat", "CSV defeat", 10000, 0, 100000,
                null, null, null, "T001", null, null, null);
            var lossRunner = new SimulationRunnerConfiguration(Configuration(enemy: enemy, maxBattleRounds: 3),
                maxDecisionsPerPhase: 30, maxConsecutiveRejections: 3, maxTrialSteps: 2000);
            var onlyLossConfig = new SimulationBatchConfiguration(source.BaseSeed, 1, source.Strategies, lossRunner);
            StrategyBatchSummary onlyLoss = new SimulationBatchRunner().RunBatch(onlyLossConfig).StrategySummaries.Single();
            Assert.That(onlyLoss.CompletedCount, Is.Zero);
            Assert.That(onlyLoss.GameOverCount, Is.EqualTo(1), string.Join(",", onlyLoss.StopReasonCounts.Select(x => x.Key + "=" + x.Value)));
            Assert.That(onlyLoss.WinRate, Is.EqualTo(0.0));
        }

        [Test]
        public void SummaryWinRateMixedOutcomesExcludeStoppedTrialsFromDenominator()
        {
            var fatalEnemy = new ProjectNST.GameCore.Enemies.EnemyDefinition("csv-fatal", "CSV fatal", 10000, 0, 1,
                null, null, null, "T001", null, null, null);
            var runner = new SimulationRunnerConfiguration(ConfigurationWithStage(OneEncounterConfig(), enemy: fatalEnemy,
                maxBattleRounds: 3), maxDecisionsPerPhase: 20, maxConsecutiveRejections: 3, maxTrialSteps: 1000);
            var factory = new SimulationStrategyFactory("mixed-outcomes", "1", () => new MixedOutcomeStrategy());
            var batch = new SimulationBatchConfiguration(5001, 3, new[] { factory }, runner);
            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(batch);
            StrategyBatchSummary summary = result.StrategySummaries.Single();

            string outcomes = string.Join(" | ", result.TrialResults.Select(x => x.TrialIndex + ":" + x.StopReasonId + ":" + x.StageNumber + ":" + x.Detail));
            Assert.That(summary.CompletedCount, Is.EqualTo(1), outcomes);
            Assert.That(summary.GameOverCount, Is.EqualTo(1));
            Assert.That(summary.StoppedCount, Is.EqualTo(1));
            Assert.That(summary.WinRate, Is.EqualTo(0.5));
            Assert.That(summary.ResolvedRate, Is.EqualTo(2.0 / 3.0).Within(1e-12));
            Assert.That(summary.StoppedRate, Is.EqualTo(1.0 / 3.0).Within(1e-12));
            string[] row = SimulationCsvExporter.ExportStrategySummaryCsv(result)
                .CsvText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(',');
            Assert.That(row[6], Is.EqualTo("0.5"));
            Assert.That(row[7], Is.EqualTo((2.0 / 3.0).ToString("R", CultureInfo.InvariantCulture)));
            Assert.That(row[8], Is.EqualTo((1.0 / 3.0).ToString("R", CultureInfo.InvariantCulture)));
        }

        [Test]
        public void TrialsCsvSummaryOnlyRetentionUsesCompactRowsAndDoesNotInventMissingMetrics()
        {
            SimulationBatchConfiguration source = BatchConfiguration(1, BatchFactory("kept", "1", BatchBehavior.Complete));
            var summaryOnly = new SimulationBatchConfiguration(source.BaseSeed, source.TrialCount, source.Strategies,
                source.RunnerConfiguration, SimulationBatchResultRetention.SummaryOnly);
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(summaryOnly);
            SimulationExportResult export = SimulationCsvExporter.ExportTrialsCsv(batch);
            Assert.That(export.Succeeded, Is.True);
            Assert.That(batch.TrialResults[0].FullResult, Is.Null);
            Assert.That(export.CsvText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(2));
            Assert.That(export.CsvText.Contains(",completed,true,true,"), Is.True);
        }

        [Test]
        public void TrialCsvLeavesMetricsBlankWhenBatchCouldNotCreateASimulationResult()
        {
            var failingFactory = new SimulationStrategyFactory("factory-error", "1", () => throw new InvalidOperationException("factory failure"));
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(BatchConfiguration(1, failingFactory));
            string row = SimulationCsvExporter.ExportTrialsCsv(batch).CsvText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[1];
            string[] fields = row.Split(',');
            Assert.That(fields[2], Is.EqualTo("factory-error"));
            Assert.That(fields[4], Is.EqualTo("batch_execution_error"));
            Assert.That(fields[5], Is.Empty);
            Assert.That(fields[7], Is.Empty);
            Assert.That(fields[13], Is.Empty);
        }

        [Test]
        public void CsvTextEscapesQuotesCommasNewlinesAndMitigatesFormulaLikeTextOnly()
        {
            string strategyId = " =SUM(\"x\",2)\r\n日本語";
            var factory = new SimulationStrategyFactory(strategyId, "v,\"1\"", () => new BatchTestStrategy(strategyId, "v,\"1\"", BatchBehavior.Complete));
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(BatchConfiguration(1, factory));
            string csv = SimulationCsvExporter.ExportTrialsCsv(batch).CsvText;
            Assert.That(csv.Contains("\"' =SUM("), Is.True);
            Assert.That(csv.Contains("\"\"x\"\""), Is.True);
            Assert.That(csv.Contains("\r\n日本語\""), Is.True);
            Assert.That(csv.Contains("\"v,\"\"1\"\"\""), Is.True);
            var rawText = new SimulationExportConfiguration(false, SimulationCsvFormulaInjectionPolicy.None);
            Assert.That(SimulationCsvExporter.ExportTrialsCsv(batch, rawText).CsvText.Contains("\" =SUM("), Is.True);
        }

        [Test]
        public void Utf8OutputSupportsOptionalBomAndJapaneseText()
        {
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(BatchConfiguration(1, BatchFactory("日本語", "版", BatchBehavior.Complete)));
            byte[] withoutBom = SimulationCsvExporter.ExportTrialsCsv(batch).Utf8Bytes;
            byte[] withBom = SimulationCsvExporter.ExportTrialsCsv(batch, new SimulationExportConfiguration(includeUtf8Bom: true)).Utf8Bytes;
            Assert.That(withoutBom.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(withBom.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(new UTF8Encoding(false).GetString(withBom, 3, withBom.Length - 3).Contains("日本語"), Is.True);
            withoutBom[0] = 0;
            Assert.That(SimulationCsvExporter.ExportTrialsCsv(batch).Utf8Bytes[0], Is.Not.Zero);
        }

        [Test]
        public void CsvOutputIsCultureIndependentAndDoesNotChangeBatchStateOrRandomCheckpoints()
        {
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(BatchConfiguration(2, BatchFactory("stable", "1", BatchBehavior.Complete)));
            string before = SimulationStateFingerprint.Create(batch.TrialResults[0].FullResult.FinalCheckpoint);
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                string french = SimulationCsvExporter.ExportStrategySummaryCsv(batch).CsvText;
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                string english = SimulationCsvExporter.ExportStrategySummaryCsv(batch).CsvText;
                Assert.That(french, Is.EqualTo(english));
                Assert.That(french.Contains(",1,1,"), Is.True);
                Assert.That(SimulationCsvExporter.ExportTrialsCsv(batch).CsvText, Is.EqualTo(SimulationCsvExporter.ExportTrialsCsv(batch).CsvText));
            }
            finally { CultureInfo.CurrentCulture = originalCulture; }
            Assert.That(SimulationStateFingerprint.Create(batch.TrialResults[0].FullResult.FinalCheckpoint), Is.EqualTo(before));
        }

        [Test]
        public void ComparisonCsvUsesExistingAminusBValuesAndEmptyComparisonIsHeaderOnly()
        {
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(BatchConfiguration(2,
                BatchFactory("a", "1", BatchBehavior.Complete), BatchFactory("b", "1", BatchBehavior.Complete)));
            string csv = SimulationCsvExporter.ExportStrategyComparisonCsv(batch).CsvText;
            Assert.That(csv.Contains("a,1,b,1,0,0,0,"), Is.True);
            Assert.That(csv.Contains(",2,1,1,0,0,0,2,0"), Is.True);
            Assert.That(SimulationCsvExporter.ExportStrategyComparisonCsv(batch).CsvText, Is.EqualTo(csv));
            SimulationBatchResult single = new SimulationBatchRunner().RunBatch(BatchConfiguration(1, BatchFactory("solo", "1", BatchBehavior.Complete)));
            Assert.That(SimulationCsvExporter.ExportStrategyComparisonCsv(single).CsvText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(1));
        }

        [Test]
        public void ComparisonCsvLeavesNegativePercentagePointDifferencesNumeric()
        {
            var enemy = new ProjectNST.GameCore.Enemies.EnemyDefinition("csv-easy", "CSV easy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            var runner = new SimulationRunnerConfiguration(ConfigurationWithStage(OneEncounterConfig(), enemy: enemy),
                maxDecisionsPerPhase: 30, maxConsecutiveRejections: 3, maxTrialSteps: 2000);
            var configuration = new SimulationBatchConfiguration(5001, 1, new[]
            {
                new SimulationStrategyFactory("runner-test", "1", () => new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal)),
                BatchFactory("zz-stopped", "1", BatchBehavior.Throw)
            }, runner);
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(configuration);
            Assert.That(batch.StrategySummaries.Single(x => x.StrategyId == "runner-test").CompletedCount, Is.EqualTo(1));
            Assert.That(batch.StrategySummaries.Single(x => x.StrategyId == "zz-stopped").StoppedCount, Is.EqualTo(1));
            string row = SimulationCsvExporter.ExportStrategyComparisonCsv(batch).CsvText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[1];
            Assert.That(row.Split(',')[6], Is.EqualTo("-100"), row);
        }

        [Test]
        public void ExportValidationReportsNullBatchAndInvalidConfiguration()
        {
            Assert.That(SimulationCsvExporter.ExportTrialsCsv(null).Status, Is.EqualTo(SimulationExportStatus.InvalidInput));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationExportConfiguration(false, (SimulationCsvFormulaInjectionPolicy)999));
        }

        [Test]
        public void FileWriterReportsSuccessAndFilesystemFailure()
        {
            SimulationBatchResult batch = new SimulationBatchRunner().RunBatch(BatchConfiguration(1, BatchFactory("writer", "1", BatchBehavior.Complete)));
            SimulationExportResult csv = SimulationCsvExporter.ExportTrialsCsv(batch);
            string path = Path.GetTempFileName();
            try
            {
                SimulationFileWriteResult success = SimulationCsvFileWriter.Write(path, csv);
                Assert.That(success.Succeeded, Is.True);
                Assert.That(File.ReadAllText(path, Encoding.UTF8), Is.EqualTo(csv.CsvText));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            string missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "out.csv");
            SimulationFileWriteResult failure = SimulationCsvFileWriter.Write(missingDirectory, csv);
            Assert.That(failure.Succeeded, Is.False);
            Assert.That(failure.ErrorCode, Is.EqualTo("file_write_failed"));
        }

        private static int CountLines(string csv) => csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length;

        private sealed class MixedOutcomeStrategy : IPlayerStrategy
        {
            public string StrategyId => "mixed-outcomes";
            public string Version => "1";

            public StrategyDecision ChooseAction(GameObservation observation, StrategyContext context)
            {
                if (context.TrialIndex == 2) throw new InvalidOperationException("intentional stopped trial");
                StrategyState next = new StrategyState(StrategyId, Version, 1, (context.DecisionStep + 1).ToString(CultureInfo.InvariantCulture));
                if (observation.Stage.Phase == ObservationPhase.AwaitingStageChoice)
                    return context.Decide(new SelectStageChoiceAction(context.NextActionId, StageChoice.SelectA), next);
                if (observation.Stage.Phase == ObservationPhase.Preparation)
                {
                    if (context.TrialIndex == 0 && !observation.Board.Slots.Any(x => x.Character != null))
                    {
                        ShopOfferObservation offer = observation.Shop.Offers.First(x =>
                            x.Kind == ShopOfferKind.Character && x.Status == ShopOfferStatus.Available);
                        return context.Decide(new BuyCharacterAction(context.NextActionId, offer.OfferId,
                            new GameActionLocation(CharacterLocationKind.Board, 0)), next);
                    }
                    return context.Decide(new StartBattleAction(context.NextActionId), next);
                }
                return context.NoAction(next);
            }
        }
    }
}
