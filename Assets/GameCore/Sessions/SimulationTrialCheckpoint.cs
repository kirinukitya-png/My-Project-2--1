using System;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Strategy;
using System.Collections.Generic;
using System.Linq;

namespace ProjectNST.GameCore.Sessions
{
    /// <summary>
    /// In-memory, immutable continuation point for one trial. The GameSession checkpoint and
    /// runner/strategy counters are captured together so they cannot be resumed independently.
    /// </summary>
    public sealed class SimulationTrialCheckpoint
    {
        // Version 2 adds explicit pool-copy provenance to owned CharacterInstances.
        // Older checkpoints cannot reconstruct this value after item-based Star changes.
        public const int CurrentSchemaVersion = 2;
        public int SchemaVersion { get; }
        public long TrialIndex { get; }
        public ulong TrialSeed { get; }
        public string StrategyId { get; }
        public string StrategyVersion { get; }
        public ActionExecutionState GameSessionState { get; }
        public SimulationRandomStreamsState GameRandomState => GameSessionState.RandomState;
        public StrategyState StrategyState { get; }
        public StrategyRngState StrategyRandomState { get; }
        public long DecisionCount { get; }
        public long ActionSequence { get; }
        public ActionId NextActionId { get; }
        public ActionId? LastActionId { get; }
        public ActionError? LastActionError { get; }
        public string DecisionWindow { get; }
        public long PhaseDecisionCount { get; }
        public int ConsecutiveRejectionCount { get; }
        public long RunnerStepCount { get; }
        public long SuccessfulActionCount { get; }
        public long RejectedActionCount { get; }
        public long BattleCount { get; }
        public long VictoryCount { get; }
        public long DefeatCount { get; }
        public long UnresolvedCount { get; }
        public SimulationStopReason StopReason { get; }
        public bool CanResume { get; }
        public string ConfigurationId { get; }
        public IReadOnlyList<SimulationExecutionRecord> ExecutionHistory { get; }
        internal GameSessionConfiguration SessionConfiguration { get; }

        internal SimulationTrialCheckpoint(SimulationTrialResult result,
            SimulationRunnerConfiguration configuration)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (result.FinalCheckpoint == null || result.FinalStrategyState == null || result.FinalStrategyRandomState == null)
                throw new ArgumentException("A checkpoint requires a captured GameSession and Strategy state.", nameof(result));
            SchemaVersion = CurrentSchemaVersion;
            TrialIndex = result.TrialIndex; TrialSeed = result.TrialSeed;
            StrategyId = result.StrategyId; StrategyVersion = result.StrategyVersion;
            GameSessionState = result.FinalCheckpoint; StrategyState = result.FinalStrategyState;
            StrategyRandomState = result.FinalStrategyRandomState;
            DecisionCount = result.DecisionCount; ActionSequence = result.ActionSequence;
            NextActionId = result.NextActionId; DecisionWindow = result.DecisionWindow;
            LastActionId = result.LastActionId; LastActionError = result.LastActionError;
            PhaseDecisionCount = result.PhaseDecisionCount;
            ConsecutiveRejectionCount = result.ConsecutiveRejectionCount;
            RunnerStepCount = result.RunnerStepCount; SuccessfulActionCount = result.SuccessfulActionCount;
            RejectedActionCount = result.RejectedActionCount; BattleCount = result.BattleCount;
            VictoryCount = result.VictoryCount; DefeatCount = result.DefeatCount;
            UnresolvedCount = result.UnresolvedCount; StopReason = result.StopReason;
            SessionConfiguration = configuration.SessionConfiguration;
            ConfigurationId = configuration.CheckpointConfigurationId ?? "in-memory-reference-bound";
            ExecutionHistory = Array.AsReadOnly(result.ExecutionHistory.ToArray());
            CanResume = IsPotentiallyResumable(result.StopReason);
        }

        internal static bool IsPotentiallyResumable(SimulationStopReason reason) =>
            reason == SimulationStopReason.EventItemSelectionRequired ||
            reason == SimulationStopReason.DecisionLimitExceeded ||
            reason == SimulationStopReason.ConsecutiveRejectionLimitExceeded ||
            reason == SimulationStopReason.TrialStepLimitExceeded;
    }
}
