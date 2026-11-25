using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.SimulationExport
{
    /// <summary>CSV exports for the opt-in full-trial diagnostics menu.</summary>
    public static class SimulationFullDiagnosticsCsvExporter
    {
        private const string RejectionDetailsHeader =
            "StrategyId,StrategyVersion,TrialIndex,TrialSeed,TrialOutcome,StopReason,Stage,Round,EncounterIndex,Phase,DecisionIndex,ActionSequence,ActionId,ActionKind,ActionParameters,RejectionCode,RejectionDetail,Gold,Level,XP,HP,BoardCharacterCount,HandCharacterCount,HandItemCount,BoardCapacity,HandCapacity,BeforeStateFingerprint,AfterStateFingerprint,DataSource,ReplayIdentical";
        private const string RejectionSummaryHeader =
            "StrategyId,StrategyVersion,ActionKind,RejectionCode,RejectionCount,AffectedTrialCount,RejectionRate,RejectionRateDenominator,ActionKindRejectionRate,ActionKindRateDenominator,RepresentativeTrialIndex,RepresentativeTrialSeed,ReplayIdentical,DataSource";
        private const string ReplaySummaryHeader =
            "StrategyId,StrategyVersion,TrialIndex,TrialSeed,OriginalOutcome,ReplayOutcome,OriginalStopReason,ReplayStopReason,DiagnosticReplaySucceeded,ReplayIdentical,MismatchCount,MismatchFields,FirstMismatchField,OriginalValue,ReplayValue,OriginalFinalStateFingerprint,ReplayFinalStateFingerprint,GameRngIdentical,StrategyRngIdentical,StrategyStateIdentical,ActionHistoryIdentical,CheckpointIdentical,VerificationType";

        public static string ExportRejectionDetails(IEnumerable<SimulationTrialResult> results,
            StageProgressConfig stageConfig, string dataSource,
            IReadOnlyDictionary<string, string> replayStatusByTrial)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (stageConfig == null) throw new ArgumentNullException(nameof(stageConfig));
            var csv = new StringBuilder().AppendLine(RejectionDetailsHeader);
            foreach (SimulationTrialResult result in results.Where(x => x != null)
                .OrderBy(x => x.StrategyId, StringComparer.Ordinal).ThenBy(x => x.StrategyVersion, StringComparer.Ordinal)
                .ThenBy(x => x.TrialIndex))
            {
                string replay = LookupReplay(replayStatusByTrial, result);
                foreach (SimulationExecutionRecord record in result.ExecutionHistory
                    .Where(x => x != null && x.Kind == SimulationExecutionEventKind.Action &&
                        x.ActionStatus == ActionDispatchStatus.Rejected).OrderBy(x => x.Sequence))
                {
                    SimulationStateSnapshot snapshot = record.BeforeStateSnapshot;
                    string stage = snapshot == null ? string.Empty : SafeStage(stageConfig, snapshot.EncounterIndex);
                    string round = snapshot == null ? string.Empty : SafeRound(stageConfig, snapshot.EncounterIndex);
                    string code = record.ActionError.HasValue
                        ? record.ActionError.Value.Code.ToString() : "Unavailable";
                    var fields = new[]
                    {
                        result.StrategyId, result.StrategyVersion, N(result.TrialIndex), N(result.TrialSeed),
                        result.Status.ToString(), result.StopReasonId, stage, round,
                        snapshot == null ? "" : N(snapshot.EncounterIndex),
                        snapshot == null ? (record.PhaseBefore?.ToString() ?? "Unavailable") : snapshot.Phase.ToString(),
                        N(record.DecisionStep), N(result.ExecutionHistory.LongCount(x => x != null &&
                            x.Kind == SimulationExecutionEventKind.Action && x.Sequence <= record.Sequence)),
                        record.Action?.ActionId.Value ?? "Unavailable",
                        record.Action?.Kind.ToString() ?? "Unavailable", DescribeParameters(record.Action), code,
                        record.ActionError.HasValue ? record.ActionError.Value.Detail ?? "" : "Unavailable",
                        snapshot == null ? "" : N(snapshot.Gold), snapshot == null ? "" : N(snapshot.Level),
                        snapshot == null ? "" : N(snapshot.XP), snapshot == null ? "" : N(snapshot.HP),
                        snapshot == null ? "" : N(snapshot.BoardCharacterCount),
                        snapshot == null ? "" : N(snapshot.HandCharacterCount), snapshot == null ? "" : N(snapshot.HandItemCount),
                        snapshot == null ? "" : N(snapshot.BoardCapacity), snapshot == null ? "" : N(snapshot.HandCapacity),
                        record.BeforeFingerprint, record.AfterFingerprint, dataSource ?? "Unavailable", replay
                    };
                    AppendRow(csv, fields);
                }
            }
            return csv.ToString();
        }

        public static string ExportRejectionSummary(IEnumerable<SimulationTrialResult> results,
            StageProgressConfig stageConfig, string dataSource,
            IReadOnlyDictionary<string, string> replayStatusByTrial)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (stageConfig == null) throw new ArgumentNullException(nameof(stageConfig));
            var accumulator = new RejectionSummaryAccumulator();
            foreach (SimulationTrialResult result in results.Where(x => x != null))
                accumulator.Add(result, dataSource, LookupReplay(replayStatusByTrial, result));
            return accumulator.ExportCsv();
        }

        public static string ExportReplaySummary(IEnumerable<ReplayDiagnosticCsvRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var csv = new StringBuilder().AppendLine(ReplaySummaryHeader);
            foreach (ReplayDiagnosticCsvRow row in rows.OrderBy(x => x.StrategyId, StringComparer.Ordinal)
                .ThenBy(x => x.StrategyVersion, StringComparer.Ordinal).ThenBy(x => x.TrialIndex)
                .ThenBy(x => x.VerificationType, StringComparer.Ordinal))
                AppendRow(csv, row.ToColumns());
            return csv.ToString();
        }

        public static string TrialKey(SimulationTrialResult result) =>
            result.StrategyId + "|" + result.StrategyVersion + "|" + result.TrialIndex.ToString(CultureInfo.InvariantCulture);

        private static string LookupReplay(IReadOnlyDictionary<string, string> statuses, SimulationTrialResult result) =>
            statuses != null && statuses.TryGetValue(TrialKey(result), out string status) ? status : "Unavailable";

        private static string DescribeParameters(GameAction action)
        {
            if (action == null) return "Unavailable";
            switch (action)
            {
                case SelectStageChoiceAction x: return "Choice=" + x.Choice;
                case BuyCharacterAction x: return "OfferId=" + x.OfferId + ";Placement=" + x.Placement.Kind + ":" + x.Placement.SlotIndex;
                case BuyItemAction x: return "OfferId=" + x.OfferId + ";HandSlot=" + x.HandSlot;
                case RerollShopAction x: return "ShopKind=" + x.ShopKind;
                case MoveOrSwapBoardAction x: return "SourceSlot=" + x.SourceSlot + ";TargetSlot=" + x.TargetSlot;
                case MoveOrSwapHandAction x: return "SourceSlot=" + x.SourceSlot + ";TargetSlot=" + x.TargetSlot;
                case MoveBoardToHandAction x: return "BoardSlot=" + x.BoardSlot + ";HandSlot=" + x.HandSlot;
                case MoveHandToBoardAction x: return "HandSlot=" + x.HandSlot + ";BoardSlot=" + x.BoardSlot;
                case SellCharacterAction x: return "InstanceId=" + x.InstanceId;
                case SellItemAction x: return "HandSlot=" + x.HandSlot;
                case UseHandItemAction x: return "HandSlot=" + x.HandSlot + ";Target=" + Target(x.Target);
                case UseEventItemAction x: return "Target=" + Target(x.Target);
                default: return "None";
            }
        }

        private static string Target(GameActionTarget target) =>
            target.Kind + ":" + target.SlotIndex.ToString(CultureInfo.InvariantCulture) +
            ";EnemyCount=" + target.EnemyCount.ToString(CultureInfo.InvariantCulture) +
            ";Present=" + target.IsPresent.ToString(CultureInfo.InvariantCulture);

        private static string SafeStage(StageProgressConfig config, int encounter) =>
            SafeNumber(() => config.GetStageNumber(encounter));
        private static string SafeRound(StageProgressConfig config, int encounter) =>
            SafeNumber(() => config.GetRoundNumber(encounter));
        private static string SafeNumber(Func<int> get) { try { return N(get()); } catch { return "Unavailable"; } }
        private static string Rate(long numerator, long denominator) => denominator == 0
            ? "Unavailable" : ((double)numerator / denominator).ToString("0.########", CultureInfo.InvariantCulture);
        private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string N(ulong value) => value.ToString(CultureInfo.InvariantCulture);
        private static void AppendRow(StringBuilder csv, IEnumerable<string> columns) =>
            csv.AppendLine(string.Join(",", columns.Select(Csv)));
        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        public sealed class RejectionSummaryAccumulator
        {
            private sealed class Group
            {
                public string StrategyId, Version, Kind, Code, Source, Replay;
                public long Count, RepresentativeIndex = long.MaxValue;
                public ulong RepresentativeSeed;
                public readonly HashSet<long> Trials = new HashSet<long>();
            }
            private readonly Dictionary<string, Group> _groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            private readonly Dictionary<string, long> _allAttempts = new Dictionary<string, long>(StringComparer.Ordinal);
            private readonly Dictionary<string, long> _kindAttempts = new Dictionary<string, long>(StringComparer.Ordinal);

            public void Add(SimulationTrialResult trial, string dataSource, string replayIdentical)
            {
                if (trial == null) throw new ArgumentNullException(nameof(trial));
                string source = dataSource ?? "Unavailable", replay = replayIdentical ?? "Unavailable";
                string baseKey = Key(trial.StrategyId, trial.StrategyVersion, source, replay);
                Increment(_allAttempts, baseKey, trial.SuccessfulActionCount + trial.RejectedActionCount);
                foreach (SimulationExecutionRecord record in trial.ExecutionHistory)
                {
                    if (record == null || record.Kind != SimulationExecutionEventKind.Action ||
                        record.Action == null || !record.ActionStatus.HasValue) continue;
                    ActionDispatchStatus status = record.ActionStatus.Value;
                    if (status != ActionDispatchStatus.Succeeded && status != ActionDispatchStatus.Rejected) continue;
                    string kind = record.Action.Kind.ToString();
                    string kindKey = Key(trial.StrategyId, trial.StrategyVersion, source, replay, kind);
                    Increment(_kindAttempts, kindKey, 1);
                    if (status != ActionDispatchStatus.Rejected) continue;
                    string code = record.ActionError.HasValue ? record.ActionError.Value.Code.ToString() : "Unavailable";
                    string groupKey = Key(trial.StrategyId, trial.StrategyVersion, kind, code, source, replay);
                    if (!_groups.TryGetValue(groupKey, out Group group))
                    {
                        group = new Group { StrategyId = trial.StrategyId, Version = trial.StrategyVersion,
                            Kind = kind, Code = code, Source = source, Replay = replay };
                        _groups.Add(groupKey, group);
                    }
                    group.Count++;
                    group.Trials.Add(trial.TrialIndex);
                    if (trial.TrialIndex < group.RepresentativeIndex)
                    { group.RepresentativeIndex = trial.TrialIndex; group.RepresentativeSeed = trial.TrialSeed; }
                }
            }

            public string ExportCsv()
            {
                var csv = new StringBuilder().AppendLine(RejectionSummaryHeader);
                foreach (Group group in _groups.Values.OrderBy(x => x.StrategyId, StringComparer.Ordinal)
                    .ThenBy(x => x.Version, StringComparer.Ordinal).ThenBy(x => x.Kind, StringComparer.Ordinal)
                    .ThenBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Source, StringComparer.Ordinal)
                    .ThenBy(x => x.Replay, StringComparer.Ordinal))
                {
                    string baseKey = Key(group.StrategyId, group.Version, group.Source, group.Replay);
                    string kindKey = Key(group.StrategyId, group.Version, group.Source, group.Replay, group.Kind);
                    long all = _allAttempts.TryGetValue(baseKey, out long allValue) ? allValue : 0;
                    long kind = _kindAttempts.TryGetValue(kindKey, out long kindValue) ? kindValue : 0;
                    AppendRow(csv, new[] { group.StrategyId, group.Version, group.Kind, group.Code,
                        N(group.Count), N(group.Trials.Count), Rate(group.Count, all), N(all),
                        kind == 0 ? "Unavailable" : Rate(group.Count, kind),
                        kind == 0 ? "Unavailable" : N(kind), N(group.RepresentativeIndex),
                        N(group.RepresentativeSeed), group.Replay, group.Source });
                }
                return csv.ToString();
            }

            private static void Increment(Dictionary<string, long> dictionary, string key, long amount)
            { dictionary[key] = dictionary.TryGetValue(key, out long value) ? value + amount : amount; }
            private static string Key(params string[] parts) => string.Join("\u001f", parts);
        }
    }

    public sealed class ReplayDiagnosticCsvRow
    {
        public string StrategyId { get; set; }
        public string StrategyVersion { get; set; }
        public long TrialIndex { get; set; }
        public ulong TrialSeed { get; set; }
        public string OriginalOutcome { get; set; }
        public string ReplayOutcome { get; set; }
        public string OriginalStopReason { get; set; }
        public string ReplayStopReason { get; set; }
        public string DiagnosticReplaySucceeded { get; set; }
        public string ReplayIdentical { get; set; }
        public string MismatchCount { get; set; }
        public string MismatchFields { get; set; }
        public string FirstMismatchField { get; set; }
        public string OriginalValue { get; set; }
        public string ReplayValue { get; set; }
        public string OriginalFinalStateFingerprint { get; set; }
        public string ReplayFinalStateFingerprint { get; set; }
        public string GameRngIdentical { get; set; }
        public string StrategyRngIdentical { get; set; }
        public string StrategyStateIdentical { get; set; }
        public string ActionHistoryIdentical { get; set; }
        public string CheckpointIdentical { get; set; }
        public string VerificationType { get; set; }

        internal string[] ToColumns() => new[]
        {
            StrategyId, StrategyVersion, TrialIndex.ToString(CultureInfo.InvariantCulture),
            TrialSeed.ToString(CultureInfo.InvariantCulture), OriginalOutcome, ReplayOutcome,
            OriginalStopReason, ReplayStopReason, DiagnosticReplaySucceeded, ReplayIdentical,
            MismatchCount, MismatchFields, FirstMismatchField, OriginalValue, ReplayValue,
            OriginalFinalStateFingerprint, ReplayFinalStateFingerprint, GameRngIdentical,
            StrategyRngIdentical, StrategyStateIdentical, ActionHistoryIdentical,
            CheckpointIdentical, VerificationType
        };
    }
}
