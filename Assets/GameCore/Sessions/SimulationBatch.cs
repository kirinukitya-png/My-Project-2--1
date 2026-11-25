using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Sessions
{
    public enum SimulationBatchResultRetention
    {
        FullTrialResults = 0,
        SummaryOnly = 1
    }

    /// <summary>A factory guarantees a distinct strategy object for each individual trial.</summary>
    public sealed class SimulationStrategyFactory
    {
        private readonly Func<IPlayerStrategy> _create;
        public string StrategyId { get; }
        public string StrategyVersion { get; }

        public SimulationStrategyFactory(string strategyId, string strategyVersion,
            Func<IPlayerStrategy> create)
        {
            if (string.IsNullOrWhiteSpace(strategyId)) throw new ArgumentException("Strategy ID is required.", nameof(strategyId));
            if (string.IsNullOrWhiteSpace(strategyVersion)) throw new ArgumentException("Strategy Version is required.", nameof(strategyVersion));
            StrategyId = strategyId;
            StrategyVersion = strategyVersion;
            _create = create ?? throw new ArgumentNullException(nameof(create));
        }

        public IPlayerStrategy Create()
        {
            IPlayerStrategy strategy = _create();
            if (strategy == null) throw new InvalidOperationException("Strategy factory returned null.");
            if (!string.Equals(strategy.StrategyId, StrategyId, StringComparison.Ordinal) ||
                !string.Equals(strategy.Version, StrategyVersion, StringComparison.Ordinal))
                throw new InvalidOperationException("Strategy factory returned an object with a different ID or Version.");
            return strategy;
        }
    }

    /// <summary>Immutable inputs for a sequential, reproducible collection of trials.</summary>
    public sealed class SimulationBatchConfiguration
    {
        private readonly ReadOnlyCollection<SimulationStrategyFactory> _strategies;
        public ulong BaseSeed { get; }
        public string BatchConfigurationId { get; }
        public int TrialCount { get; }
        public IReadOnlyList<SimulationStrategyFactory> Strategies => _strategies;
        public SimulationRunnerConfiguration RunnerConfiguration { get; }
        public SimulationBatchResultRetention ResultRetention { get; }
        public string TrialSeedAlgorithm => TrialSeedGenerator.AlgorithmId;
        public int GameCoreRandomCheckpointVersion => SimulationRandomStreamsState.CurrentVersion;

        public SimulationBatchConfiguration(ulong baseSeed, int trialCount,
            IEnumerable<SimulationStrategyFactory> strategies,
            SimulationRunnerConfiguration runnerConfiguration,
            SimulationBatchResultRetention resultRetention = SimulationBatchResultRetention.FullTrialResults,
            string batchConfigurationId = null)
        {
            if (trialCount < 1) throw new ArgumentOutOfRangeException(nameof(trialCount));
            if (runnerConfiguration == null) throw new ArgumentNullException(nameof(runnerConfiguration));
            if (!Enum.IsDefined(typeof(SimulationBatchResultRetention), resultRetention))
                throw new ArgumentOutOfRangeException(nameof(resultRetention));
            if (strategies == null) throw new ArgumentNullException(nameof(strategies));

            var copy = strategies.ToList();
            if (copy.Count == 0) throw new ArgumentException("At least one strategy factory is required.", nameof(strategies));
            if (copy.Any(x => x == null)) throw new ArgumentException("Strategy factories cannot contain null entries.", nameof(strategies));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (SimulationStrategyFactory strategy in copy)
            {
                string key = IdentityKey(strategy.StrategyId, strategy.StrategyVersion);
                if (!keys.Add(key)) throw new ArgumentException("Strategy ID and Version pairs must be unique.", nameof(strategies));
            }
            copy.Sort(CompareFactories);

            long resultCount = checked((long)trialCount * copy.Count);
            if (resultCount > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(trialCount), "The requested result count exceeds the in-memory result index limit.");

            BaseSeed = baseSeed;
            TrialCount = trialCount;
            _strategies = Array.AsReadOnly(copy.ToArray());
            RunnerConfiguration = runnerConfiguration;
            ResultRetention = resultRetention;
            string batchIdentity = string.IsNullOrWhiteSpace(batchConfigurationId)
                ? CreatePlanId(baseSeed, trialCount, resultRetention, runnerConfiguration, copy)
                : batchConfigurationId;
            const string accountingIdentity = "|pool-accounting:v2";
            BatchConfigurationId = batchIdentity.EndsWith(accountingIdentity, StringComparison.Ordinal)
                ? batchIdentity : batchIdentity + accountingIdentity;
        }

        internal static string IdentityKey(string id, string version) =>
            id.Length.ToString(CultureInfo.InvariantCulture) + ":" + id + version.Length.ToString(CultureInfo.InvariantCulture) + ":" + version;

        private static int CompareFactories(SimulationStrategyFactory left, SimulationStrategyFactory right)
        {
            int id = string.Compare(left.StrategyId, right.StrategyId, StringComparison.Ordinal);
            return id != 0 ? id : string.Compare(left.StrategyVersion, right.StrategyVersion, StringComparison.Ordinal);
        }

        private static string CreatePlanId(ulong seed, int trials,
            SimulationBatchResultRetention retention, SimulationRunnerConfiguration runner,
            IReadOnlyList<SimulationStrategyFactory> strategies)
        {
            var text = new StringBuilder();
            text.Append(seed.ToString("X16", CultureInfo.InvariantCulture)).Append('|')
                .Append(trials.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(((int)retention).ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(runner.MaxDecisionsPerPhase.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(runner.MaxConsecutiveRejections.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(runner.MaxTrialSteps.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(runner.RecordExecutionHistory ? "1" : "0").Append('|')
                .Append(runner.CheckpointConfigurationId ?? "in-memory-reference-bound").Append('|')
                .Append(TrialSeedGenerator.AlgorithmId).Append('|')
                .Append(SimulationRandomStreamsState.CurrentVersion);
            foreach (SimulationStrategyFactory strategy in strategies)
                text.Append('|').Append(strategy.StrategyId.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(strategy.StrategyId)
                    .Append(strategy.StrategyVersion.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(strategy.StrategyVersion);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return "batch-v1-" + hex;
            }
        }
    }

    public enum SimulationBatchExecutionStatus { Succeeded, StrategyFactoryFailure, RunnerFailure }

    /// <summary>Compact immutable per-strategy/per-trial row; optionally retains the full checkpoint.</summary>
    public sealed class SimulationBatchTrialResult
    {
        public long TrialIndex { get; }
        public ulong TrialSeed { get; }
        public string StrategyId { get; }
        public string StrategyVersion { get; }
        public SimulationBatchExecutionStatus ExecutionStatus { get; }
        public bool HasSimulationResult { get; }
        public SimulationTrialStatus? TrialStatus { get; }
        public SimulationStopReason? StopReason { get; }
        public string StopReasonId { get; }
        public string Detail { get; }
        public int StageNumber { get; }
        public int RoundNumber { get; }
        public int FinalHP { get; }
        public int FinalGold { get; }
        public int FinalLevel { get; }
        public int FinalXP { get; }
        public long DecisionCount { get; }
        public long SuccessfulActionCount { get; }
        public long RejectedActionCount { get; }
        public long BattleCount { get; }
        public long VictoryCount { get; }
        public long DefeatCount { get; }
        public long UnresolvedCount { get; }
        public SimulationTrialResult FullResult { get; }
        public bool HasMetricSample { get; }

        internal SimulationBatchTrialResult(long trialIndex, ulong trialSeed,
            SimulationStrategyFactory strategy, SimulationTrialResult result,
            SimulationBatchExecutionStatus executionStatus, string failureDetail,
            SimulationBatchResultRetention retention)
        {
            TrialIndex = trialIndex; TrialSeed = trialSeed;
            StrategyId = strategy.StrategyId; StrategyVersion = strategy.StrategyVersion;
            ExecutionStatus = executionStatus;
            HasSimulationResult = result != null;
            if (result == null)
            {
                TrialStatus = null; StopReason = null; StopReasonId = "batch_execution_error";
                Detail = failureDetail ?? string.Empty;
                StageNumber = RoundNumber = FinalHP = FinalGold = FinalLevel = FinalXP = 0;
                DecisionCount = SuccessfulActionCount = RejectedActionCount = 0;
                BattleCount = VictoryCount = DefeatCount = UnresolvedCount = 0;
                FullResult = null; HasMetricSample = false;
                return;
            }

            if (result.TrialIndex != trialIndex || result.TrialSeed != trialSeed ||
                !string.Equals(result.StrategyId, strategy.StrategyId, StringComparison.Ordinal) ||
                !string.Equals(result.StrategyVersion, strategy.StrategyVersion, StringComparison.Ordinal))
                throw new ArgumentException("SimulationTrialResult identity does not match its Batch row.", nameof(result));
            TrialStatus = result.Status; StopReason = result.StopReason; StopReasonId = result.StopReasonId;
            Detail = result.Detail; StageNumber = result.StageNumber; RoundNumber = result.RoundNumber;
            FinalHP = result.FinalHP; FinalGold = result.FinalGold; FinalLevel = result.FinalLevel; FinalXP = result.FinalXP;
            DecisionCount = result.DecisionCount; SuccessfulActionCount = result.SuccessfulActionCount;
            RejectedActionCount = result.RejectedActionCount; BattleCount = result.BattleCount;
            VictoryCount = result.VictoryCount; DefeatCount = result.DefeatCount; UnresolvedCount = result.UnresolvedCount;
            FullResult = retention == SimulationBatchResultRetention.FullTrialResults ? result : null;
            HasMetricSample = result.FinalCheckpoint != null;
        }
    }

    public sealed class SimulationMetricStatistics
    {
        public int SampleCount { get; }
        public double? Mean { get; }
        public double? Median { get; }
        public double? Maximum { get; }

        internal SimulationMetricStatistics(IEnumerable<long> source)
        {
            long[] values = source.OrderBy(x => x).ToArray();
            SampleCount = values.Length;
            if (values.Length == 0) return;
            decimal sum = 0m;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            Mean = (double)(sum / values.Length);
            Median = values.Length % 2 == 1
                ? values[values.Length / 2]
                : (double)(((decimal)values[values.Length / 2 - 1] + values[values.Length / 2]) / 2m);
            Maximum = values[values.Length - 1];
        }
    }

    public sealed class StrategyBatchSummary
    {
        private readonly ReadOnlyDictionary<string, long> _stopReasonCounts;
        public string StrategyId { get; }
        public string StrategyVersion { get; }
        public long TotalTrials { get; }
        public long CompletedCount { get; }
        public long GameOverCount { get; }
        public long StoppedCount { get; }
        public long BatchExecutionFailureCount { get; }
        public int MetricSampleCount { get; }
        public IReadOnlyDictionary<string, long> StopReasonCounts => _stopReasonCounts;
        public bool HasWinRate => WinRate.HasValue;
        public double? WinRate { get; }
        public double ResolvedRate { get; }
        public double StoppedRate { get; }
        public double CompletionRate { get; }
        public double GameOverRate { get; }
        public SimulationMetricStatistics ReachedStage { get; }
        public SimulationMetricStatistics FinalHP { get; }
        public SimulationMetricStatistics FinalGold { get; }
        public SimulationMetricStatistics FinalLevel { get; }
        public SimulationMetricStatistics FinalXP { get; }
        public SimulationMetricStatistics Decisions { get; }
        public SimulationMetricStatistics SuccessfulActions { get; }
        public SimulationMetricStatistics RejectedActions { get; }
        public SimulationMetricStatistics Battles { get; }
        public SimulationMetricStatistics Victories { get; }
        public SimulationMetricStatistics Defeats { get; }
        public SimulationMetricStatistics UnresolvedBattles { get; }

        internal StrategyBatchSummary(string strategyId, string strategyVersion,
            IReadOnlyList<SimulationBatchTrialResult> results, int totalTrials)
        {
            StrategyId = strategyId; StrategyVersion = strategyVersion; TotalTrials = totalTrials;
            CompletedCount = results.LongCount(x => x.TrialStatus == SimulationTrialStatus.Completed);
            GameOverCount = results.LongCount(x => x.TrialStatus == SimulationTrialStatus.GameOver);
            StoppedCount = TotalTrials - CompletedCount - GameOverCount;
            BatchExecutionFailureCount = results.LongCount(x => !x.HasSimulationResult);
            long resolved = CompletedCount + GameOverCount;
            ResolvedRate = (double)resolved / TotalTrials;
            StoppedRate = (double)StoppedCount / TotalTrials;
            CompletionRate = (double)CompletedCount / TotalTrials;
            GameOverRate = (double)GameOverCount / TotalTrials;
            long winLossCount = resolved;
            WinRate = winLossCount == 0 ? (double?)null : (double)CompletedCount / winLossCount;

            var reasons = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (SimulationStopReason reason in Enum.GetValues(typeof(SimulationStopReason)))
                reasons.Add(SimulationStopReasonIds.Get(reason), results.LongCount(x => x.StopReasonId == SimulationStopReasonIds.Get(reason)));
            reasons.Add("batch_execution_error", results.LongCount(x => x.StopReasonId == "batch_execution_error"));
            _stopReasonCounts = new ReadOnlyDictionary<string, long>(reasons);

            SimulationBatchTrialResult[] samples = results.Where(x => x.HasMetricSample).OrderBy(x => x.TrialIndex).ToArray();
            MetricSampleCount = samples.Length;
            ReachedStage = Stat(samples, x => x.StageNumber);
            FinalHP = Stat(samples, x => x.FinalHP); FinalGold = Stat(samples, x => x.FinalGold);
            FinalLevel = Stat(samples, x => x.FinalLevel); FinalXP = Stat(samples, x => x.FinalXP);
            Decisions = Stat(samples, x => x.DecisionCount); SuccessfulActions = Stat(samples, x => x.SuccessfulActionCount);
            RejectedActions = Stat(samples, x => x.RejectedActionCount); Battles = Stat(samples, x => x.BattleCount);
            Victories = Stat(samples, x => x.VictoryCount); Defeats = Stat(samples, x => x.DefeatCount);
            UnresolvedBattles = Stat(samples, x => x.UnresolvedCount);
        }

        private static SimulationMetricStatistics Stat(IEnumerable<SimulationBatchTrialResult> rows,
            Func<SimulationBatchTrialResult, long> selector) => new SimulationMetricStatistics(rows.Select(selector));
    }

    public sealed class StrategyComparisonResult
    {
        public string FirstStrategyId { get; }
        public string FirstStrategyVersion { get; }
        public string SecondStrategyId { get; }
        public string SecondStrategyVersion { get; }
        public double? WinRateDifferencePercentagePoints { get; }
        public double? ResolvedRateDifferencePercentagePoints { get; }
        public double? StoppedRateDifferencePercentagePoints { get; }
        public double? MeanReachedStageDifference { get; }
        public double? MeanFinalHPDifference { get; }
        public double? MeanDecisionCountDifference { get; }
        public long CommonResolvedTrialCount { get; }
        public double? FirstCommonSeedWinRate { get; }
        public double? SecondCommonSeedWinRate { get; }
        public double? CommonSeedWinRateDifferencePercentagePoints { get; }
        public long FirstCompletedSecondGameOverCount { get; }
        public long FirstGameOverSecondCompletedCount { get; }
        public long BothCompletedCount { get; }
        public long BothGameOverCount { get; }

        internal StrategyComparisonResult(StrategyBatchSummary first, StrategyBatchSummary second,
            IReadOnlyList<SimulationBatchTrialResult> firstRows,
            IReadOnlyList<SimulationBatchTrialResult> secondRows)
        {
            FirstStrategyId = first.StrategyId; FirstStrategyVersion = first.StrategyVersion;
            SecondStrategyId = second.StrategyId; SecondStrategyVersion = second.StrategyVersion;
            WinRateDifferencePercentagePoints = Difference(first.WinRate, second.WinRate, percentagePoints: true);
            ResolvedRateDifferencePercentagePoints = Difference(first.ResolvedRate, second.ResolvedRate, percentagePoints: true);
            StoppedRateDifferencePercentagePoints = Difference(first.StoppedRate, second.StoppedRate, percentagePoints: true);
            MeanReachedStageDifference = Difference(first.ReachedStage.Mean, second.ReachedStage.Mean);
            MeanFinalHPDifference = Difference(first.FinalHP.Mean, second.FinalHP.Mean);
            MeanDecisionCountDifference = Difference(first.Decisions.Mean, second.Decisions.Mean);

            var secondByTrial = secondRows.ToDictionary(x => x.TrialIndex);
            long common = 0, firstWinSecondLoss = 0, firstLossSecondWin = 0, bothWin = 0, bothLoss = 0;
            foreach (SimulationBatchTrialResult left in firstRows.OrderBy(x => x.TrialIndex))
            {
                if (!secondByTrial.TryGetValue(left.TrialIndex, out SimulationBatchTrialResult right) ||
                    left.TrialSeed != right.TrialSeed || !IsResolved(left) || !IsResolved(right)) continue;
                common++;
                bool leftCompleted = left.TrialStatus == SimulationTrialStatus.Completed;
                bool rightCompleted = right.TrialStatus == SimulationTrialStatus.Completed;
                if (leftCompleted && rightCompleted) bothWin++;
                else if (!leftCompleted && !rightCompleted) bothLoss++;
                else if (leftCompleted) firstWinSecondLoss++;
                else firstLossSecondWin++;
            }
            CommonResolvedTrialCount = common;
            FirstCompletedSecondGameOverCount = firstWinSecondLoss;
            FirstGameOverSecondCompletedCount = firstLossSecondWin;
            BothCompletedCount = bothWin; BothGameOverCount = bothLoss;
            FirstCommonSeedWinRate = common == 0 ? (double?)null : (double)(firstWinSecondLoss + bothWin) / common;
            SecondCommonSeedWinRate = common == 0 ? (double?)null : (double)(firstLossSecondWin + bothWin) / common;
            CommonSeedWinRateDifferencePercentagePoints = Difference(FirstCommonSeedWinRate,
                SecondCommonSeedWinRate, percentagePoints: true);
        }

        private static bool IsResolved(SimulationBatchTrialResult row) =>
            row.TrialStatus == SimulationTrialStatus.Completed || row.TrialStatus == SimulationTrialStatus.GameOver;

        private static double? Difference(double? left, double? right, bool percentagePoints = false) =>
            !left.HasValue || !right.HasValue ? (double?)null : (left.Value - right.Value) * (percentagePoints ? 100.0 : 1.0);
    }

    public sealed class SimulationBatchResult
    {
        private readonly ReadOnlyCollection<SimulationSeed> _seeds;
        private readonly ReadOnlyCollection<SimulationBatchTrialResult> _trials;
        private readonly ReadOnlyCollection<StrategyBatchSummary> _summaries;
        private readonly ReadOnlyCollection<StrategyComparisonResult> _comparisons;
        public ulong BaseSeed { get; }
        public string BatchConfigurationId { get; }
        public int TrialCount { get; }
        public string TrialSeedAlgorithm { get; }
        public int GameCoreRandomCheckpointVersion { get; }
        public SimulationBatchResultRetention ResultRetention { get; }
        public IReadOnlyList<SimulationSeed> TrialSeeds => _seeds;
        public IReadOnlyList<SimulationBatchTrialResult> TrialResults => _trials;
        public IReadOnlyList<StrategyBatchSummary> StrategySummaries => _summaries;
        public IReadOnlyList<StrategyComparisonResult> Comparisons => _comparisons;

        internal SimulationBatchResult(ulong baseSeed, string batchConfigurationId,
            int trialCount, int randomVersion,
            SimulationBatchResultRetention retention, IEnumerable<SimulationSeed> seeds,
            IEnumerable<SimulationBatchTrialResult> trials, IEnumerable<StrategyBatchSummary> summaries,
            IEnumerable<StrategyComparisonResult> comparisons)
        {
            BaseSeed = baseSeed; BatchConfigurationId = batchConfigurationId;
            TrialCount = trialCount; TrialSeedAlgorithm = TrialSeedGenerator.AlgorithmId;
            GameCoreRandomCheckpointVersion = randomVersion; ResultRetention = retention;
            _seeds = Array.AsReadOnly(seeds.ToArray());
            _trials = Array.AsReadOnly(trials.ToArray());
            _summaries = Array.AsReadOnly(summaries.ToArray());
            _comparisons = Array.AsReadOnly(comparisons.ToArray());
        }
    }

    /// <summary>Read-only notification emitted after each completed trial; does not affect any simulation state.</summary>
    public sealed class SimulationBatchProgress
    {
        public int CompletedTrialCount { get; }
        public int TotalTrialCount { get; }
        public string CurrentStrategyId { get; }
        public int CurrentTrialIndex { get; }
        public double LastTrialElapsedSeconds { get; }

        internal SimulationBatchProgress(int completed, int total, string strategyId, int trialIndex, double elapsed)
        { CompletedTrialCount = completed; TotalTrialCount = total; CurrentStrategyId = strategyId;
            CurrentTrialIndex = trialIndex; LastTrialElapsedSeconds = elapsed; }
    }

    /// <summary>Sequentially runs strategy-major batches with one shared deterministic seed list.</summary>
    public sealed class SimulationBatchRunner
    {
        public SimulationBatchResult RunBatch(SimulationBatchConfiguration configuration) =>
            RunBatch(configuration, null);

        public SimulationBatchResult RunBatch(SimulationBatchConfiguration configuration,
            Action<SimulationBatchProgress> progress)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            ValidateGameConfiguration(configuration);

            var seeds = new SimulationSeed[configuration.TrialCount];
            for (int i = 0; i < seeds.Length; i++) seeds[i] = TrialSeedGenerator.Create(configuration.BaseSeed, (ulong)i);

            int totalRecords = checked(configuration.TrialCount * configuration.Strategies.Count);
            var records = new List<SimulationBatchTrialResult>(totalRecords);
            int completedRecords = 0;
            foreach (SimulationStrategyFactory factory in configuration.Strategies)
            {
                var runner = new SimulationRunner(configuration.RunnerConfiguration);
                var seenInstances = new HashSet<IPlayerStrategy>(ReferenceComparer<IPlayerStrategy>.Instance);
                for (int i = 0; i < seeds.Length; i++)
                {
                    var trialTimer = System.Diagnostics.Stopwatch.StartNew();
                    IPlayerStrategy strategy = null;
                    SimulationTrialResult result = null;
                    SimulationBatchExecutionStatus status = SimulationBatchExecutionStatus.Succeeded;
                    string failure = string.Empty;
                    try
                    {
                        strategy = factory.Create();
                        if (!seenInstances.Add(strategy))
                            throw new InvalidOperationException("Strategy factory reused an instance across trials; each trial requires a fresh object.");
                        result = runner.RunTrial(i, seeds[i].TrialSeed, strategy);
                        if (result == null) throw new InvalidOperationException("SimulationRunner returned null.");
                    }
                    catch (Exception ex)
                    {
                        status = strategy == null ? SimulationBatchExecutionStatus.StrategyFactoryFailure :
                            SimulationBatchExecutionStatus.RunnerFailure;
                        failure = ex.GetType().Name + ": " + ex.Message;
                    }

                    records.Add(new SimulationBatchTrialResult(i, seeds[i].TrialSeed,
                        factory, result, status, failure, configuration.ResultRetention));
                    trialTimer.Stop();
                    completedRecords++;
                    progress?.Invoke(new SimulationBatchProgress(completedRecords, totalRecords,
                        factory.StrategyId, i, trialTimer.Elapsed.TotalSeconds));
                }
            }

            records.Sort(CompareRows);
            var summaries = new List<StrategyBatchSummary>(configuration.Strategies.Count);
            foreach (SimulationStrategyFactory strategy in configuration.Strategies)
            {
                SimulationBatchTrialResult[] rows = records.Where(x =>
                    x.StrategyId == strategy.StrategyId && x.StrategyVersion == strategy.StrategyVersion).ToArray();
                summaries.Add(new StrategyBatchSummary(strategy.StrategyId, strategy.StrategyVersion,
                    rows, configuration.TrialCount));
            }

            var comparisons = new List<StrategyComparisonResult>();
            for (int i = 0; i < summaries.Count; i++)
            for (int j = i + 1; j < summaries.Count; j++)
            {
                SimulationBatchTrialResult[] leftRows = records.Where(x => SameIdentity(x, summaries[i])).ToArray();
                SimulationBatchTrialResult[] rightRows = records.Where(x => SameIdentity(x, summaries[j])).ToArray();
                comparisons.Add(new StrategyComparisonResult(summaries[i], summaries[j], leftRows, rightRows));
            }

            return new SimulationBatchResult(configuration.BaseSeed, configuration.BatchConfigurationId,
                configuration.TrialCount,
                configuration.GameCoreRandomCheckpointVersion, configuration.ResultRetention,
                seeds, records, summaries, comparisons);
        }

        private static void ValidateGameConfiguration(SimulationBatchConfiguration configuration)
        {
            try
            {
                ulong seed = TrialSeedGenerator.CreateTrialSeed(configuration.BaseSeed, 0);
                GameSession.Create(configuration.RunnerConfiguration.SessionConfiguration, seed);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Batch GameSessionConfiguration failed preflight validation.", nameof(configuration), ex);
            }
        }

        private static bool SameIdentity(SimulationBatchTrialResult row, StrategyBatchSummary summary) =>
            string.Equals(row.StrategyId, summary.StrategyId, StringComparison.Ordinal) &&
            string.Equals(row.StrategyVersion, summary.StrategyVersion, StringComparison.Ordinal);

        private static int CompareRows(SimulationBatchTrialResult left, SimulationBatchTrialResult right)
        {
            int id = string.Compare(left.StrategyId, right.StrategyId, StringComparison.Ordinal);
            if (id != 0) return id;
            int version = string.Compare(left.StrategyVersion, right.StrategyVersion, StringComparison.Ordinal);
            return version != 0 ? version : left.TrialIndex.CompareTo(right.TrialIndex);
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
