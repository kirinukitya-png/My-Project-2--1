using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Sessions;
using ProjectNST.SimulationExport;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameSessionTests
    {
        [Test]
        public void FullDiagnosticsRejectionDetailsIncludeEveryRejectedActionAndStateSnapshot()
        {
            GameSessionConfiguration session = ConfigurationWithStage(OneEncounterConfig());
            var runnerConfiguration = new SimulationRunnerConfiguration(session,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 3, maxTrialSteps: 200,
                recordExecutionHistory: true);
            SimulationTrialResult result = new SimulationRunner(runnerConfiguration).RunTrial(12, 99123,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectDifferentActions));

            SimulationExecutionRecord[] rejected = result.ExecutionHistory.Where(x =>
                x.Kind == SimulationExecutionEventKind.Action && x.ActionStatus == ProjectNST.GameCore.Actions.ActionDispatchStatus.Rejected).ToArray();
            Assert.That(result.RejectedActionCount, Is.EqualTo(rejected.Length));
            Assert.That(rejected, Is.Not.Empty);
            Assert.That(rejected.All(x => x.BeforeStateSnapshot != null && x.AfterStateSnapshot != null), Is.True);
            Assert.That(rejected.All(x => x.BeforeStateSnapshot.BoardCapacity > 0 && x.BeforeStateSnapshot.HandCapacity > 0), Is.True);

            var replayStatus = new System.Collections.Generic.Dictionary<string, string>
            {
                [SimulationFullDiagnosticsCsvExporter.TrialKey(result)] = "true"
            };
            string csv = SimulationFullDiagnosticsCsvExporter.ExportRejectionDetails(new[] { result },
                OneEncounterConfig(), "OriginalTrialExecutionHistory", replayStatus);
            string[] rows = csv.TrimEnd('\r', '\n').Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            Assert.That(rows.Length - 1, Is.EqualTo(result.RejectedActionCount));
            Assert.That(rows[0], Does.Contain("ActionParameters"));
            Assert.That(rows[0], Does.Contain("BeforeStateFingerprint"));
            Assert.That(rows[1], Does.Contain("OriginalTrialExecutionHistory"));
            Assert.That(rows[1], Does.Contain("BuyCharacter"));
        }

        [Test]
        public void FullDiagnosticsRejectionSummaryUsesDistinctTrialsAndExplicitDenominators()
        {
            GameSessionConfiguration session = ConfigurationWithStage(OneEncounterConfig());
            var runnerConfiguration = new SimulationRunnerConfiguration(session,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 3, maxTrialSteps: 200,
                recordExecutionHistory: true);
            SimulationTrialResult result = new SimulationRunner(runnerConfiguration).RunTrial(12, 99123,
                new ScriptedRunnerStrategy(RunnerBehavior.RejectDifferentActions));
            string csv = SimulationFullDiagnosticsCsvExporter.ExportRejectionSummary(new[] { result },
                OneEncounterConfig(), "OriginalTrialExecutionHistory", null);
            string[] lines = csv.TrimEnd('\r', '\n').Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            Assert.That(lines[0], Does.Contain("AffectedTrialCount"));
            Assert.That(lines[0], Does.Contain("RejectionRateDenominator"));
            Assert.That(lines.Length, Is.GreaterThan(1));
            Assert.That(lines.Skip(1).All(line => line.Contains(",\"1\",")), Is.True,
                "Each ActionKind/reason group affects this one Trial once, even when repeated.");
            Assert.That(lines.Skip(1).All(line => line.Contains("Unavailable")), Is.True,
                "Replay status without a comparison must remain explicitly unavailable.");
            long rejectionTotal = lines.Skip(1).Sum(line => long.Parse(line.Split(',')[4].Trim('"'),
                System.Globalization.CultureInfo.InvariantCulture));
            Assert.That(rejectionTotal, Is.EqualTo(result.RejectedActionCount),
                "The grouped rejection counts must reconcile with the source execution history.");
        }

        [Test]
        public void FullDiagnosticsSummaryAccumulatorMatchesWholeSetAggregation()
        {
            GameSessionConfiguration session = ConfigurationWithStage(OneEncounterConfig());
            var runnerConfiguration = new SimulationRunnerConfiguration(session,
                maxDecisionsPerPhase: 20, maxConsecutiveRejections: 3, maxTrialSteps: 200,
                recordExecutionHistory: true);
            var runner = new SimulationRunner(runnerConfiguration);
            SimulationTrialResult[] results =
            {
                runner.RunTrial(2, 1122, new ScriptedRunnerStrategy(RunnerBehavior.RejectDifferentActions)),
                runner.RunTrial(5, 5511, new ScriptedRunnerStrategy(RunnerBehavior.RejectDifferentActions))
            };
            var accumulator = new SimulationFullDiagnosticsCsvExporter.RejectionSummaryAccumulator();
            foreach (SimulationTrialResult result in results)
                accumulator.Add(result, "DiagnosticStrategyRerunHistory", "true");

            string expected = SimulationFullDiagnosticsCsvExporter.ExportRejectionSummary(results,
                OneEncounterConfig(), "DiagnosticStrategyRerunHistory",
                results.ToDictionary(SimulationFullDiagnosticsCsvExporter.TrialKey, _ => "true"));
            Assert.That(accumulator.ExportCsv(), Is.EqualTo(expected));
        }

        [Test]
        public void FullDiagnosticsReplayCsvDoesNotTreatUnavailableAsIdentical()
        {
            string csv = SimulationFullDiagnosticsCsvExporter.ExportReplaySummary(new[]
            {
                new ReplayDiagnosticCsvRow
                {
                    StrategyId = "test", StrategyVersion = "1", TrialIndex = 0, TrialSeed = 42,
                    OriginalOutcome = "Completed", ReplayOutcome = "Unavailable",
                    OriginalStopReason = "completed", ReplayStopReason = "Unavailable",
                    DiagnosticReplaySucceeded = "false", ReplayIdentical = "Unavailable",
                    MismatchCount = "Unavailable", MismatchFields = "Unavailable:FinalState",
                    FirstMismatchField = "FinalState", OriginalValue = "state-a", ReplayValue = "Unavailable",
                    OriginalFinalStateFingerprint = "state-a", ReplayFinalStateFingerprint = "Unavailable",
                    GameRngIdentical = "Unavailable", StrategyRngIdentical = "Unavailable",
                    StrategyStateIdentical = "Unavailable", ActionHistoryIdentical = "Unavailable",
                    CheckpointIdentical = "Unavailable", VerificationType = "StrategyReexecution"
                }
            });
            Assert.That(csv, Does.Contain("Unavailable:FinalState"));
            Assert.That(csv, Does.Contain("StrategyReexecution"));
            Assert.That(csv, Does.Contain("\"Unavailable\""));
        }

        [Test]
        public void FullDiagnosticsIncludesCompletedZeroRejectionTrialInActionReplayVerification()
        {
            GameSessionConfiguration session = ConfigurationWithStage(OneEncounterConfig());
            var runnerConfiguration = new SimulationRunnerConfiguration(session,
                maxDecisionsPerPhase: 100, maxConsecutiveRejections: 10, maxTrialSteps: 1000,
                recordExecutionHistory: true);
            SimulationTrialResult result = new SimulationRunner(runnerConfiguration).RunTrial(8, 81818,
                new ScriptedRunnerStrategy(RunnerBehavior.CompleteNormal));
            Assert.That(result.Status, Is.EqualTo(SimulationTrialStatus.Completed), result.Detail);
            Assert.That(result.RejectedActionCount, Is.Zero);

            SimulationReplayResult replay = new SimulationReplayVerifier().Verify(runnerConfiguration,
                result, result.ExecutionHistory);
            Assert.That(replay.Succeeded, Is.True, replay.MismatchKind + ": " + replay.Expected + " / " + replay.Actual);
            string details = SimulationFullDiagnosticsCsvExporter.ExportRejectionDetails(new[] { result },
                OneEncounterConfig(), "OriginalTrialExecutionHistory", null);
            Assert.That(details.Trim().Split('\n'), Has.Length.EqualTo(1),
                "A Completed Trial with zero rejections remains in replay verification but adds no rejection detail row.");
        }
    }
}
