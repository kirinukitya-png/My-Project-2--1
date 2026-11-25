using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ProjectNST.GameCore.Sessions
{
    public enum SimulationCsvFormulaInjectionPolicy
    {
        PrefixApostrophe = 0,
        None = 1
    }

    /// <summary>Controls byte encoding and spreadsheet-safe handling of text fields.</summary>
    public sealed class SimulationExportConfiguration
    {
        public bool IncludeUtf8Bom { get; }
        public SimulationCsvFormulaInjectionPolicy FormulaInjectionPolicy { get; }

        public SimulationExportConfiguration(bool includeUtf8Bom = false,
            SimulationCsvFormulaInjectionPolicy formulaInjectionPolicy = SimulationCsvFormulaInjectionPolicy.PrefixApostrophe)
        {
            if (!Enum.IsDefined(typeof(SimulationCsvFormulaInjectionPolicy), formulaInjectionPolicy))
                throw new ArgumentOutOfRangeException(nameof(formulaInjectionPolicy));
            IncludeUtf8Bom = includeUtf8Bom;
            FormulaInjectionPolicy = formulaInjectionPolicy;
        }
    }

    public enum SimulationExportStatus { Succeeded, Unavailable, InvalidInput }

    /// <summary>Result of converting one Batch table to deterministic CSV text and UTF-8 bytes.</summary>
    public sealed class SimulationExportResult
    {
        private readonly byte[] _utf8Bytes;
        public SimulationExportStatus Status { get; }
        public bool Succeeded => Status == SimulationExportStatus.Succeeded;
        public bool IsAvailable => Succeeded;
        public string CsvText { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public byte[] Utf8Bytes => _utf8Bytes == null ? Array.Empty<byte>() : (byte[])_utf8Bytes.Clone();

        private SimulationExportResult(SimulationExportStatus status, string csvText,
            string errorCode, string errorMessage, bool includeBom)
        {
            Status = status;
            CsvText = csvText;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            if (status != SimulationExportStatus.Succeeded) return;
            byte[] payload = new UTF8Encoding(false).GetBytes(csvText ?? string.Empty);
            if (includeBom)
            {
                _utf8Bytes = new byte[payload.Length + 3];
                _utf8Bytes[0] = 0xEF; _utf8Bytes[1] = 0xBB; _utf8Bytes[2] = 0xBF;
                Buffer.BlockCopy(payload, 0, _utf8Bytes, 3, payload.Length);
            }
            else _utf8Bytes = payload;
        }

        internal static SimulationExportResult Success(string csv, bool bom) =>
            new SimulationExportResult(SimulationExportStatus.Succeeded, csv, null, null, bom);
        internal static SimulationExportResult Unavailable(string code, string message) =>
            new SimulationExportResult(SimulationExportStatus.Unavailable, null, code, message, false);
        internal static SimulationExportResult Invalid(string code, string message) =>
            new SimulationExportResult(SimulationExportStatus.InvalidInput, null, code, message, false);
    }

    /// <summary>Pure conversion from immutable Batch result DTOs to stable RFC 4180 CSV.</summary>
    public static class SimulationCsvExporter
    {
        private static readonly string[] TrialHeaders =
        {
            "TrialNumber", "TrialSeed", "StrategyId", "StrategyVersion", "StopReason", "IsResolved", "IsCompleted",
            "FinalStage", "FinalRound", "FinalHP", "FinalGold", "FinalLevel", "FinalXP", "DecisionCount",
            "SuccessfulActionCount", "RejectedActionCount", "BattleCount", "VictoryCount", "DefeatCount", "UnresolvedCount"
        };
        private static readonly string[] SummaryHeaders =
        {
            "StrategyId", "StrategyVersion", "TotalTrials", "CompletedCount", "GameOverCount", "StoppedCount",
            "WinRate", "ResolvedRate", "StoppedRate", "CompletionRate", "GameOverRate", "AverageStage", "MedianStage",
            "MaxStage", "AverageHP", "AverageGold", "AverageLevel", "AverageXP", "AverageDecisionCount",
            "AverageSuccessfulActionCount", "AverageRejectedActionCount", "AverageBattleCount"
        };
        private static readonly string[] ComparisonHeaders =
        {
            "StrategyAId", "StrategyAVersion", "StrategyBId", "StrategyBVersion",
            "WinRateDifferencePercentagePoints", "ResolvedRateDifferencePercentagePoints",
            "StoppedRateDifferencePercentagePoints", "AverageStageDifference", "CommonSeedCount",
            "CommonSeedStrategyAWinRate", "CommonSeedStrategyBWinRate", "CommonSeedWinRateDifferencePercentagePoints",
            "OnlyACompletedCount", "OnlyBCompletedCount", "BothCompletedCount", "BothGameOverCount"
        };
        private static readonly string[] StopReasonHeaders =
        {
            "StrategyId", "StrategyVersion", "StopReason", "Count", "PercentageOfTotalTrials"
        };

        public static IReadOnlyList<string> TrialsHeaders => Array.AsReadOnly((string[])TrialHeaders.Clone());
        public static IReadOnlyList<string> StrategySummaryHeaders => Array.AsReadOnly((string[])SummaryHeaders.Clone());
        public static IReadOnlyList<string> StrategyComparisonHeaders => Array.AsReadOnly((string[])ComparisonHeaders.Clone());
        public static IReadOnlyList<string> StopReasonsHeaders => Array.AsReadOnly((string[])StopReasonHeaders.Clone());

        public static SimulationExportResult ExportTrialsCsv(SimulationBatchResult batch,
            SimulationExportConfiguration configuration = null)
        {
            if (batch == null) return SimulationExportResult.Invalid("null_batch", "Batch result is required.");
            if (!HasCompleteTrialRows(batch))
                return SimulationExportResult.Unavailable("trial_rows_unavailable", "The Batch result does not retain a complete per-trial row set.");
            configuration = configuration ?? new SimulationExportConfiguration();
            var csv = new CsvBuilder(configuration);
            csv.AddRow(TrialHeaders);
            foreach (SimulationBatchTrialResult row in batch.TrialResults
                .OrderBy(x => x.TrialIndex).ThenBy(x => x.StrategyId, StringComparer.Ordinal)
                .ThenBy(x => x.StrategyVersion, StringComparer.Ordinal))
            {
                bool hasMetrics = row.HasSimulationResult && row.HasMetricSample;
                bool? resolved = row.TrialStatus.HasValue
                    ? row.TrialStatus == SimulationTrialStatus.Completed || row.TrialStatus == SimulationTrialStatus.GameOver
                    : (bool?)null;
                bool? completed = row.TrialStatus.HasValue
                    ? row.TrialStatus == SimulationTrialStatus.Completed : (bool?)null;
                csv.AddRow(new[]
                {
                    Number(row.TrialIndex), Number(row.TrialSeed), Text(row.StrategyId), Text(row.StrategyVersion), Text(row.StopReasonId),
                    Boolean(resolved), Boolean(completed), hasMetrics ? Number(row.StageNumber) : CsvCell.Empty,
                    hasMetrics ? Number(row.RoundNumber) : CsvCell.Empty, hasMetrics ? Number(row.FinalHP) : CsvCell.Empty,
                    hasMetrics ? Number(row.FinalGold) : CsvCell.Empty, hasMetrics ? Number(row.FinalLevel) : CsvCell.Empty,
                    hasMetrics ? Number(row.FinalXP) : CsvCell.Empty, row.HasSimulationResult ? Number(row.DecisionCount) : CsvCell.Empty,
                    row.HasSimulationResult ? Number(row.SuccessfulActionCount) : CsvCell.Empty,
                    row.HasSimulationResult ? Number(row.RejectedActionCount) : CsvCell.Empty,
                    row.HasSimulationResult ? Number(row.BattleCount) : CsvCell.Empty,
                    row.HasSimulationResult ? Number(row.VictoryCount) : CsvCell.Empty,
                    row.HasSimulationResult ? Number(row.DefeatCount) : CsvCell.Empty,
                    row.HasSimulationResult ? Number(row.UnresolvedCount) : CsvCell.Empty
                });
            }
            return SimulationExportResult.Success(csv.ToString(), configuration.IncludeUtf8Bom);
        }

        public static SimulationExportResult ExportStrategySummaryCsv(SimulationBatchResult batch,
            SimulationExportConfiguration configuration = null)
        {
            if (batch == null) return SimulationExportResult.Invalid("null_batch", "Batch result is required.");
            if (batch.StrategySummaries == null)
                return SimulationExportResult.Unavailable("summaries_unavailable", "Strategy summaries are unavailable.");
            configuration = configuration ?? new SimulationExportConfiguration();
            var csv = new CsvBuilder(configuration);
            csv.AddRow(SummaryHeaders);
            foreach (StrategyBatchSummary row in batch.StrategySummaries
                .OrderBy(x => x.StrategyId, StringComparer.Ordinal).ThenBy(x => x.StrategyVersion, StringComparer.Ordinal))
            {
                csv.AddRow(new[]
                {
                    Text(row.StrategyId), Text(row.StrategyVersion), Number(row.TotalTrials), Number(row.CompletedCount),
                    Number(row.GameOverCount), Number(row.StoppedCount), NullableNumber(row.WinRate), Number(row.ResolvedRate),
                    Number(row.StoppedRate), Number(row.CompletionRate), Number(row.GameOverRate), NullableNumber(row.ReachedStage.Mean),
                    NullableNumber(row.ReachedStage.Median), NullableNumber(row.ReachedStage.Maximum), NullableNumber(row.FinalHP.Mean),
                    NullableNumber(row.FinalGold.Mean), NullableNumber(row.FinalLevel.Mean), NullableNumber(row.FinalXP.Mean),
                    NullableNumber(row.Decisions.Mean), NullableNumber(row.SuccessfulActions.Mean),
                    NullableNumber(row.RejectedActions.Mean), NullableNumber(row.Battles.Mean)
                });
            }
            return SimulationExportResult.Success(csv.ToString(), configuration.IncludeUtf8Bom);
        }

        public static SimulationExportResult ExportStrategyComparisonCsv(SimulationBatchResult batch,
            SimulationExportConfiguration configuration = null)
        {
            if (batch == null) return SimulationExportResult.Invalid("null_batch", "Batch result is required.");
            if (batch.Comparisons == null)
                return SimulationExportResult.Unavailable("comparisons_unavailable", "Strategy comparisons are unavailable.");
            configuration = configuration ?? new SimulationExportConfiguration();
            var csv = new CsvBuilder(configuration);
            csv.AddRow(ComparisonHeaders);
            foreach (StrategyComparisonResult row in batch.Comparisons
                .OrderBy(x => x.FirstStrategyId, StringComparer.Ordinal).ThenBy(x => x.FirstStrategyVersion, StringComparer.Ordinal)
                .ThenBy(x => x.SecondStrategyId, StringComparer.Ordinal).ThenBy(x => x.SecondStrategyVersion, StringComparer.Ordinal))
            {
                csv.AddRow(new[]
                {
                    Text(row.FirstStrategyId), Text(row.FirstStrategyVersion), Text(row.SecondStrategyId), Text(row.SecondStrategyVersion),
                    NullableNumber(row.WinRateDifferencePercentagePoints), NullableNumber(row.ResolvedRateDifferencePercentagePoints),
                    NullableNumber(row.StoppedRateDifferencePercentagePoints), NullableNumber(row.MeanReachedStageDifference),
                    Number(row.CommonResolvedTrialCount), NullableNumber(row.FirstCommonSeedWinRate),
                    NullableNumber(row.SecondCommonSeedWinRate), NullableNumber(row.CommonSeedWinRateDifferencePercentagePoints),
                    Number(row.FirstCompletedSecondGameOverCount), Number(row.FirstGameOverSecondCompletedCount),
                    Number(row.BothCompletedCount), Number(row.BothGameOverCount)
                });
            }
            return SimulationExportResult.Success(csv.ToString(), configuration.IncludeUtf8Bom);
        }

        public static SimulationExportResult ExportStopReasonsCsv(SimulationBatchResult batch,
            SimulationExportConfiguration configuration = null)
        {
            if (batch == null) return SimulationExportResult.Invalid("null_batch", "Batch result is required.");
            if (batch.StrategySummaries == null)
                return SimulationExportResult.Unavailable("summaries_unavailable", "Stop-reason summaries are unavailable.");
            configuration = configuration ?? new SimulationExportConfiguration();
            var csv = new CsvBuilder(configuration);
            csv.AddRow(StopReasonHeaders);
            foreach (StrategyBatchSummary summary in batch.StrategySummaries
                .OrderBy(x => x.StrategyId, StringComparer.Ordinal).ThenBy(x => x.StrategyVersion, StringComparer.Ordinal))
            {
                foreach (KeyValuePair<string, long> reason in summary.StopReasonCounts.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    double? percentage = summary.TotalTrials > 0
                        ? (double)reason.Value * 100.0 / summary.TotalTrials : (double?)null;
                    csv.AddRow(new[] { Text(summary.StrategyId), Text(summary.StrategyVersion), Text(reason.Key),
                        Number(reason.Value), NullableNumber(percentage) });
                }
            }
            return SimulationExportResult.Success(csv.ToString(), configuration.IncludeUtf8Bom);
        }

        private static bool HasCompleteTrialRows(SimulationBatchResult batch)
        {
            if (batch.StrategySummaries == null || batch.TrialResults == null) return false;
            long expected;
            try { expected = checked((long)batch.TrialCount * batch.StrategySummaries.Count); }
            catch (OverflowException) { return false; }
            if (expected != batch.TrialResults.Count) return false;
            var strategyKeys = new HashSet<string>(batch.StrategySummaries.Select(x =>
                x.StrategyId.Length.ToString(CultureInfo.InvariantCulture) + ":" + x.StrategyId +
                x.StrategyVersion.Length.ToString(CultureInfo.InvariantCulture) + ":" + x.StrategyVersion), StringComparer.Ordinal);
            var seeds = batch.TrialSeeds.ToDictionary(x => checked((long)x.TrialIndex), x => x.TrialSeed);
            if (seeds.Count != batch.TrialCount) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (SimulationBatchTrialResult row in batch.TrialResults)
            {
                if (row == null || row.TrialIndex < 0 || row.TrialIndex >= batch.TrialCount) return false;
                string strategyKey = row.StrategyId.Length.ToString(CultureInfo.InvariantCulture) + ":" + row.StrategyId +
                    row.StrategyVersion.Length.ToString(CultureInfo.InvariantCulture) + ":" + row.StrategyVersion;
                if (!strategyKeys.Contains(strategyKey) || !seeds.TryGetValue(row.TrialIndex, out ulong expectedSeed) ||
                    expectedSeed != row.TrialSeed) return false;
                string key = row.TrialIndex.ToString(CultureInfo.InvariantCulture) + "|" +
                    row.StrategyId.Length.ToString(CultureInfo.InvariantCulture) + ":" + row.StrategyId +
                    row.StrategyVersion.Length.ToString(CultureInfo.InvariantCulture) + ":" + row.StrategyVersion;
                if (!keys.Add(key)) return false;
            }
            return true;
        }

        private static CsvCell Number(long value) => CsvCell.Numeric(value.ToString(CultureInfo.InvariantCulture));
        private static CsvCell Number(ulong value) => CsvCell.Numeric(value.ToString(CultureInfo.InvariantCulture));
        private static CsvCell Number(int value) => CsvCell.Numeric(value.ToString(CultureInfo.InvariantCulture));
        private static CsvCell Number(double value) => !double.IsNaN(value) && !double.IsInfinity(value)
            ? CsvCell.Numeric(value.ToString("R", CultureInfo.InvariantCulture)) : CsvCell.Empty;
        private static CsvCell Boolean(bool? value) => value.HasValue ? CsvCell.Numeric(value.Value ? "true" : "false") : CsvCell.Empty;
        private static CsvCell NullableNumber(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? CsvCell.Numeric(value.Value.ToString("R", CultureInfo.InvariantCulture)) : CsvCell.Empty;
        private static CsvCell Text(string value) => CsvCell.String(value);

        private readonly struct CsvCell
        {
            public static CsvCell Empty => new CsvCell(null, false);
            public string Value { get; }
            public bool IsText { get; }
            private CsvCell(string value, bool isText) { Value = value; IsText = isText; }
            public static CsvCell String(string value) => new CsvCell(value, true);
            public static CsvCell Numeric(string value) => new CsvCell(value, false);
        }

        private sealed class CsvBuilder
        {
            private static readonly char[] CsvSpecialCharacters = { ',', '"', '\r', '\n' };
            private readonly StringBuilder _builder = new StringBuilder();
            private readonly SimulationExportConfiguration _configuration;
            public CsvBuilder(SimulationExportConfiguration configuration) { _configuration = configuration; }

            public void AddRow(IEnumerable<string> fields)
            {
                bool first = true;
                foreach (string field in fields)
                {
                    if (!first) _builder.Append(',');
                    first = false;
                    AppendField(CsvCell.String(field));
                }
                _builder.Append("\r\n");
            }

            public void AddRow(params CsvCell[] fields)
            {
                bool first = true;
                foreach (CsvCell field in fields)
                {
                    if (!first) _builder.Append(',');
                    first = false;
                    AppendField(field);
                }
                _builder.Append("\r\n");
            }

            private void AppendField(CsvCell cell)
            {
                string value = cell.Value;
                if (value == null) return;
                if (cell.IsText && _configuration.FormulaInjectionPolicy == SimulationCsvFormulaInjectionPolicy.PrefixApostrophe && IsFormulaLike(value))
                    value = "'" + value;
                bool quote = value.IndexOfAny(CsvSpecialCharacters) >= 0;
                if (quote) _builder.Append('"');
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '"') _builder.Append("\"\""); else _builder.Append(c);
                }
                if (quote) _builder.Append('"');
            }

            private static bool IsFormulaLike(string value)
            {
                int index = 0;
                while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
                return index < value.Length && (value[index] == '=' || value[index] == '+' ||
                    value[index] == '-' || value[index] == '@');
            }

            public override string ToString() => _builder.ToString();
        }
    }
}
