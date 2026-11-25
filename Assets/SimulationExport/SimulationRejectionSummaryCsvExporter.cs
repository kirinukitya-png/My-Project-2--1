using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Sessions;

namespace ProjectNST.SimulationExport
{
    /// <summary>Exports the final consecutive rejection streaks from stopped simulation trials.</summary>
    public static class SimulationRejectionSummaryCsvExporter
    {
        private const string Header = "StrategyId,StrategyVersion,TrialNumber,Stage,Round,Phase,StopReason,RejectedActionKind,RejectionReasonCode,RejectionCount";

        public static string Export(IEnumerable<SimulationTrialResult> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var rows = new List<RejectionRow>();
            foreach (SimulationTrialResult result in results)
            {
                if (result == null || result.StopReason != SimulationStopReason.ConsecutiveRejectionLimitExceeded)
                    continue;

                SimulationExecutionRecord[] trailing = GetTrailingRejections(result);
                var groups = new List<Tuple<GameActionKind, string, int>>();
                foreach (SimulationExecutionRecord record in trailing)
                {
                    string reason = record.ActionError.HasValue
                        ? record.ActionError.Value.Code.ToString() : "<none>";
                    int index = groups.FindIndex(x => x.Item1 == record.Action.Kind &&
                        string.Equals(x.Item2, reason, StringComparison.Ordinal));
                    if (index < 0)
                    {
                        int count = trailing.Count(candidate => candidate.Action.Kind == record.Action.Kind &&
                            string.Equals(candidate.ActionError.HasValue
                                ? candidate.ActionError.Value.Code.ToString() : "<none>", reason,
                                StringComparison.Ordinal));
                        groups.Add(Tuple.Create(record.Action.Kind, reason, count));
                    }
                }

                foreach (Tuple<GameActionKind, string, int> group in groups)
                    rows.Add(new RejectionRow(result.StrategyId, result.StrategyVersion,
                        result.TrialIndex, result.StageNumber, result.RoundNumber,
                        result.FinalPhase.HasValue ? result.FinalPhase.Value.ToString() : "<none>",
                        result.StopReasonId, group.Item1.ToString(), group.Item2, group.Item3));
            }

            var csv = new StringBuilder();
            csv.AppendLine(Header);
            foreach (RejectionRow row in rows.OrderBy(x => x.StrategyId, StringComparer.Ordinal)
                .ThenBy(x => x.StrategyVersion, StringComparer.Ordinal).ThenBy(x => x.TrialNumber)
                .ThenBy(x => x.ActionKind, StringComparer.Ordinal).ThenBy(x => x.ReasonCode, StringComparer.Ordinal))
            {
                csv.Append(Csv(row.StrategyId)).Append(',').Append(Csv(row.StrategyVersion)).Append(',')
                    .Append(row.TrialNumber.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.Stage.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.Round.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(row.Phase)).Append(',')
                    .Append(Csv(row.StopReason)).Append(',').Append(Csv(row.ActionKind)).Append(',')
                    .Append(Csv(row.ReasonCode)).Append(',').AppendLine(row.Count.ToString(CultureInfo.InvariantCulture));
            }
            return csv.ToString();
        }

        private static SimulationExecutionRecord[] GetTrailingRejections(SimulationTrialResult result)
        {
            if (result.ConsecutiveRejectionCount <= 0) return Array.Empty<SimulationExecutionRecord>();
            SimulationExecutionRecord[] ordered = result.ExecutionHistory
                .Where(x => x != null && x.Kind != SimulationExecutionEventKind.TrialEnd)
                .OrderBy(x => x.Sequence).ToArray();
            var trailing = new List<SimulationExecutionRecord>(result.ConsecutiveRejectionCount);
            for (int i = ordered.Length - 1; i >= 0 && trailing.Count < result.ConsecutiveRejectionCount; i--)
            {
                SimulationExecutionRecord record = ordered[i];
                if (record.Kind != SimulationExecutionEventKind.Action ||
                    record.ActionStatus != ActionDispatchStatus.Rejected) break;
                trailing.Add(record);
            }
            trailing.Reverse();
            return trailing.ToArray();
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private sealed class RejectionRow
        {
            public string StrategyId { get; }
            public string StrategyVersion { get; }
            public long TrialNumber { get; }
            public int Stage { get; }
            public int Round { get; }
            public string Phase { get; }
            public string StopReason { get; }
            public string ActionKind { get; }
            public string ReasonCode { get; }
            public int Count { get; }

            public RejectionRow(string strategyId, string strategyVersion, long trialNumber,
                int stage, int round, string phase, string stopReason, string actionKind,
                string reasonCode, int count)
            {
                StrategyId = strategyId; StrategyVersion = strategyVersion; TrialNumber = trialNumber;
                Stage = stage; Round = round; Phase = phase; StopReason = stopReason;
                ActionKind = actionKind; ReasonCode = reasonCode; Count = count;
            }
        }
    }
}
