using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Sessions
{
    /// <summary>Safety limits for one simulation trial. These are runner policy, not game rules.</summary>
    public sealed class SimulationRunnerConfiguration
    {
        public GameSessionConfiguration SessionConfiguration { get; }
        public int MaxDecisionsPerPhase { get; }
        public int MaxConsecutiveRejections { get; }
        public long MaxTrialSteps { get; }
        public bool RecordExecutionHistory { get; }
        public string CheckpointConfigurationId { get; }
        public string EventItemDefinitionId { get; }
        public SimulationProfileCollector ProfileCollector { get; }

        public SimulationRunnerConfiguration(GameSessionConfiguration sessionConfiguration,
            int maxDecisionsPerPhase = 100, int maxConsecutiveRejections = 10,
            long maxTrialSteps = 100000, bool recordExecutionHistory = false,
            string checkpointConfigurationId = null, string eventItemDefinitionId = null,
            SimulationProfileCollector profileCollector = null)
        {
            SessionConfiguration = sessionConfiguration ?? throw new ArgumentNullException(nameof(sessionConfiguration));
            ProfileCollector = profileCollector;
            if (maxDecisionsPerPhase < 1) throw new ArgumentOutOfRangeException(nameof(maxDecisionsPerPhase));
            if (maxConsecutiveRejections < 1) throw new ArgumentOutOfRangeException(nameof(maxConsecutiveRejections));
            if (maxTrialSteps < 1) throw new ArgumentOutOfRangeException(nameof(maxTrialSteps));
            MaxDecisionsPerPhase = maxDecisionsPerPhase;
            MaxConsecutiveRejections = maxConsecutiveRejections;
            MaxTrialSteps = maxTrialSteps;
            RecordExecutionHistory = recordExecutionHistory;
            if (!string.IsNullOrWhiteSpace(eventItemDefinitionId))
            {
                ItemDefinition selectedEventItem = sessionConfiguration.ObservationItemDefinitions
                    .FirstOrDefault(x => x.DefinitionId == eventItemDefinitionId);
                if (selectedEventItem == null || selectedEventItem.Target != ItemEffectTarget.OneAlly)
                    throw new ArgumentException("Configured Event Item must exist in ItemDefinitions and target OneAlly.", nameof(eventItemDefinitionId));
                EventItemDefinitionId = eventItemDefinitionId;
            }
            string checkpointIdentity = string.IsNullOrWhiteSpace(checkpointConfigurationId)
                ? (EventItemDefinitionId == null ? null : "event-item:" + EventItemDefinitionId)
                : checkpointConfigurationId + (EventItemDefinitionId == null ? string.Empty : "|event-item:" + EventItemDefinitionId);
            CheckpointConfigurationId = checkpointIdentity == null
                ? null : checkpointIdentity + "|pool-accounting:v2";
        }
    }

    public enum SimulationStopReason
    {
        Completed,
        GameOver,
        BattleUnresolved,
        EventItemSelectionRequired,
        EnemylessStageExitRequired,
        DecisionLimitExceeded,
        ConsecutiveRejectionLimitExceeded,
        InvalidStrategyDecision,
        StrategyException,
        InvalidSessionState,
        SessionExecutionError,
        InvalidConfiguration,
        NoActionNotAllowed,
        TrialStepLimitExceeded
    }

    public enum SimulationTrialStatus { Completed, GameOver, Stopped, Error }

    /// <summary>Immutable summary and in-memory resume checkpoint for a single trial.</summary>
    public sealed class SimulationTrialResult
    {
        public long TrialIndex { get; }
        public ulong TrialSeed { get; }
        public string StrategyId { get; }
        public string StrategyVersion { get; }
        public SimulationTrialStatus Status { get; }
        public SimulationStopReason StopReason { get; }
        public string StopReasonId => SimulationStopReasonIds.Get(StopReason);
        public string Detail { get; }
        public int EncounterIndex { get; }
        public int StageNumber { get; }
        public int RoundNumber { get; }
        public StageProgressPhase? FinalPhase { get; }
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
        public long RunnerStepCount { get; }
        public ActionId? LastActionId { get; }
        public ActionId NextActionId { get; }
        public ActionError? LastActionError { get; }
        public System.Collections.Generic.IReadOnlyList<SimulationExecutionRecord> ExecutionHistory { get; }
        public long ActionSequence { get; }
        public string DecisionWindow { get; }
        public long PhaseDecisionCount { get; }
        public int ConsecutiveRejectionCount { get; }
        public ActionExecutionState FinalCheckpoint { get; }
        public SimulationRandomStreamsState? FinalGameRandomState => FinalCheckpoint == null
            ? (SimulationRandomStreamsState?)null : FinalCheckpoint.RandomState;
        public StrategyRngState FinalStrategyRandomState { get; }
        public StrategyState FinalStrategyState { get; }
        public bool IsNormalTerminal => Status == SimulationTrialStatus.Completed || Status == SimulationTrialStatus.GameOver;

        internal SimulationTrialResult(long trialIndex, ulong trialSeed, string strategyId,
            string strategyVersion, SimulationStopReason stopReason, string detail,
            ActionExecutionState checkpoint, StrategyRngState strategyRandomState,
            StrategyState strategyState, long decisions, long successfulActions,
            long rejectedActions, long battles, long victories, long defeats,
            long unresolved, long runnerSteps, ActionId? lastActionId, ActionId nextActionId,
            ActionError? lastActionError, StageProgressConfig stageConfig,
            long actionSequence = 0, string decisionWindow = null,
            long phaseDecisionCount = 0, int consecutiveRejections = 0,
            System.Collections.Generic.IEnumerable<SimulationExecutionRecord> executionHistory = null)
        {
            TrialIndex = trialIndex; TrialSeed = trialSeed;
            StrategyId = strategyId ?? string.Empty; StrategyVersion = strategyVersion ?? string.Empty;
            StopReason = stopReason;
            Detail = detail ?? string.Empty; FinalCheckpoint = checkpoint;
            FinalStrategyRandomState = strategyRandomState; FinalStrategyState = strategyState;
            DecisionCount = decisions; SuccessfulActionCount = successfulActions;
            RejectedActionCount = rejectedActions; BattleCount = battles;
            VictoryCount = victories; DefeatCount = defeats; UnresolvedCount = unresolved;
            RunnerStepCount = runnerSteps; LastActionId = lastActionId; NextActionId = nextActionId;
            LastActionError = lastActionError;
            ActionSequence = actionSequence; DecisionWindow = decisionWindow;
            PhaseDecisionCount = phaseDecisionCount; ConsecutiveRejectionCount = consecutiveRejections;
            ExecutionHistory = Array.AsReadOnly(new System.Collections.Generic.List<SimulationExecutionRecord>(executionHistory ?? Array.Empty<SimulationExecutionRecord>()).ToArray());

            if (checkpoint == null)
            {
                Status = stopReason == SimulationStopReason.InvalidConfiguration ||
                    stopReason == SimulationStopReason.InvalidSessionState ||
                    stopReason == SimulationStopReason.StrategyException ||
                    stopReason == SimulationStopReason.SessionExecutionError ||
                    stopReason == SimulationStopReason.InvalidStrategyDecision
                    ? SimulationTrialStatus.Error : SimulationTrialStatus.Stopped;
                EncounterIndex = StageNumber = RoundNumber = FinalHP = FinalGold = FinalLevel = FinalXP = 0;
                FinalPhase = null;
                return;
            }

            StageProgressState stage = checkpoint.Stage;
            EncounterIndex = stage.EncounterIndex;
            FinalPhase = stage.Phase;
            try
            {
                StageNumber = stageConfig.GetStageNumber(stage.EncounterIndex);
                RoundNumber = stageConfig.GetRoundNumber(stage.EncounterIndex);
            }
            catch (ArgumentOutOfRangeException)
            {
                StageNumber = stageConfig.TotalStageCount;
                RoundNumber = stageConfig.RoundsPerStage;
            }
            FinalHP = checkpoint.Economy.CurrentHP;
            FinalGold = checkpoint.Economy.Gold;
            FinalLevel = checkpoint.Economy.Level;
            FinalXP = checkpoint.Economy.XP;
            Status = stopReason == SimulationStopReason.Completed ? SimulationTrialStatus.Completed :
                stopReason == SimulationStopReason.GameOver ? SimulationTrialStatus.GameOver :
                stopReason == SimulationStopReason.StrategyException ||
                stopReason == SimulationStopReason.InvalidStrategyDecision ||
                stopReason == SimulationStopReason.InvalidSessionState ||
                stopReason == SimulationStopReason.SessionExecutionError
                    ? SimulationTrialStatus.Error : SimulationTrialStatus.Stopped;
        }
    }

    /// <summary>Stable identifiers intended for logs and later batch summaries.</summary>
    public static class SimulationStopReasonIds
    {
        public static string Get(SimulationStopReason reason)
        {
            switch (reason)
            {
                case SimulationStopReason.Completed: return "completed";
                case SimulationStopReason.GameOver: return "game_over";
                case SimulationStopReason.BattleUnresolved: return "battle_unresolved";
                case SimulationStopReason.EventItemSelectionRequired: return "event_item_selection_required";
                case SimulationStopReason.EnemylessStageExitRequired: return "enemyless_stage_exit_required";
                case SimulationStopReason.DecisionLimitExceeded: return "decision_limit_exceeded";
                case SimulationStopReason.ConsecutiveRejectionLimitExceeded: return "consecutive_rejection_limit_exceeded";
                case SimulationStopReason.InvalidStrategyDecision: return "invalid_strategy_decision";
                case SimulationStopReason.StrategyException: return "strategy_exception";
                case SimulationStopReason.InvalidSessionState: return "invalid_session_state";
                case SimulationStopReason.SessionExecutionError: return "session_execution_error";
                case SimulationStopReason.InvalidConfiguration: return "invalid_configuration";
                case SimulationStopReason.NoActionNotAllowed: return "no_action_not_allowed";
                case SimulationStopReason.TrialStepLimitExceeded: return "trial_step_limit_exceeded";
                default: return "unknown";
            }
        }
    }

    /// <summary>Executes one isolated trial. Game state remains owned by GameSession.</summary>
    public sealed class SimulationRunner
    {
        private readonly SimulationRunnerConfiguration _configuration;
        public SimulationRunner(SimulationRunnerConfiguration configuration) =>
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        public SimulationTrialResult RunTrial(long trialIndex, ulong trialSeed,
            IPlayerStrategy strategy, ActionDispatcher dispatcher = null)
            => RunTrialCore(trialIndex, trialSeed, strategy, dispatcher, null, null);

        public SimulationTrialCheckpoint CreateCheckpoint(SimulationTrialResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.FinalCheckpoint == null)
                throw new ArgumentException("The Trial result has no resumable GameSession checkpoint.", nameof(result));
            return new SimulationTrialCheckpoint(result, _configuration);
        }

        public GameSession RestoreTrial(SimulationTrialCheckpoint checkpoint,
            IPlayerStrategy strategy, ActionDispatcher dispatcher = null)
        {
            ValidateCheckpoint(checkpoint, strategy, externalEventItemDefinitionId: null,
                requireEventSelection: false);
            GameSession restored = GameSession.Restore(_configuration.SessionConfiguration,
                checkpoint.GameSessionState, dispatcher);
            if (restored == null || !ActionExecutionStateValidator.Validate(restored.CurrentState).IsValid)
                throw new ArgumentException("Checkpoint could not be restored as a valid GameSession.", nameof(checkpoint));
            return restored;
        }

        public SimulationTrialResult ResumeTrial(SimulationTrialCheckpoint checkpoint,
            IPlayerStrategy strategy, string externalEventItemDefinitionId = null,
            ActionDispatcher dispatcher = null)
        {
            ValidateCheckpoint(checkpoint, strategy, externalEventItemDefinitionId,
                requireEventSelection: checkpoint != null && checkpoint.StopReason == SimulationStopReason.EventItemSelectionRequired);
            return RunTrialCore(checkpoint.TrialIndex, checkpoint.TrialSeed, strategy,
                dispatcher, checkpoint, externalEventItemDefinitionId);
        }

        private void ValidateCheckpoint(SimulationTrialCheckpoint checkpoint, IPlayerStrategy strategy,
            string externalEventItemDefinitionId, bool requireEventSelection)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));
            if (checkpoint.SchemaVersion != SimulationTrialCheckpoint.CurrentSchemaVersion)
                throw new ArgumentException("Unsupported Trial checkpoint schema version.", nameof(checkpoint));
            if (!checkpoint.CanResume)
                throw new ArgumentException("This stop reason is not resumable.", nameof(checkpoint));
            bool explicitConfigurationIdMatches = !string.IsNullOrWhiteSpace(_configuration.CheckpointConfigurationId) &&
                string.Equals(checkpoint.ConfigurationId, _configuration.CheckpointConfigurationId, StringComparison.Ordinal);
            bool implicitConfigurationMatches = checkpoint.ConfigurationId == "in-memory-reference-bound" &&
                ReferenceEquals(checkpoint.SessionConfiguration, _configuration.SessionConfiguration);
            if (!explicitConfigurationIdMatches && !implicitConfigurationMatches)
                throw new ArgumentException("Checkpoint belongs to a different in-memory GameSessionConfiguration.", nameof(checkpoint));
            if (strategy.StrategyId != checkpoint.StrategyId || strategy.Version != checkpoint.StrategyVersion)
                throw new ArgumentException("Strategy ID or Version does not match the Trial checkpoint.", nameof(strategy));
            if (checkpoint.TrialIndex < 0 || checkpoint.GameSessionState.RandomState.TrialSeed != checkpoint.TrialSeed ||
                checkpoint.GameSessionState.RandomState.Version != SimulationRandomStreamsState.CurrentVersion)
                throw new ArgumentException("Trial Seed or GameCore RNG checkpoint is inconsistent.", nameof(checkpoint));
            if (checkpoint.StrategyState == null || checkpoint.StrategyState.StrategyId != checkpoint.StrategyId ||
                checkpoint.StrategyState.StrategyVersion != checkpoint.StrategyVersion)
                throw new ArgumentException("StrategyState identity does not match the checkpoint.", nameof(checkpoint));
            StrategyRandomSource.FromState(checkpoint.StrategyRandomState, checkpoint.TrialSeed,
                checkpoint.StrategyId, checkpoint.StrategyVersion);
            if (checkpoint.DecisionCount < 0 || checkpoint.ActionSequence < 0 ||
                checkpoint.PhaseDecisionCount < 0 || checkpoint.ConsecutiveRejectionCount < 0 ||
                checkpoint.RunnerStepCount < 0 || checkpoint.SuccessfulActionCount < 0 ||
                checkpoint.RejectedActionCount < 0 || checkpoint.BattleCount < 0 ||
                checkpoint.VictoryCount < 0 || checkpoint.DefeatCount < 0 || checkpoint.UnresolvedCount < 0 ||
                checkpoint.ActionSequence > checkpoint.DecisionCount ||
                checkpoint.NextActionId != MakeActionId(checkpoint.TrialIndex, checkpoint.ActionSequence))
                throw new ArgumentException("Runner progress counters or next Action ID are inconsistent.", nameof(checkpoint));

            string actionPrefix = "trial:" + checkpoint.TrialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":action:";
            foreach (ActionId recordedId in checkpoint.GameSessionState.ExecutedActionIds)
            {
                if (!recordedId.Value.StartsWith(actionPrefix, StringComparison.Ordinal) ||
                    !long.TryParse(recordedId.Value.Substring(actionPrefix.Length),
                        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                        out long sequence) || sequence < 0 || sequence >= checkpoint.ActionSequence)
                    throw new ArgumentException("Executed Action ID history is inconsistent with the Runner sequence.", nameof(checkpoint));
            }

            StageProgressState stage = checkpoint.GameSessionState.Stage;
            if (checkpoint.StopReason == SimulationStopReason.DecisionLimitExceeded ||
                checkpoint.StopReason == SimulationStopReason.ConsecutiveRejectionLimitExceeded)
            {
                string expectedWindow = ((int)stage.Phase).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    stage.EncounterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.Equals(checkpoint.DecisionWindow, expectedWindow, StringComparison.Ordinal))
                    throw new ArgumentException("Runner decision window does not match the checkpoint phase.", nameof(checkpoint));
            }
            if (checkpoint.StopReason == SimulationStopReason.EventItemSelectionRequired)
            {
                bool rewardPending = stage.Phase == StageProgressPhase.RewardPending &&
                    checkpoint.GameSessionState.BattleResult?.Outcome == BattleOutcome.Victory;
                bool enemylessPreparation = stage.Phase == StageProgressPhase.Preparation &&
                    stage.CurrentSelection != null && stage.CurrentSelection.EnemyCategory == StageEnemyCategory.None;
                if (!rewardPending && !enemylessPreparation)
                    throw new ArgumentException("Event-selection stop reason does not match the GameSession phase.", nameof(checkpoint));
                if (requireEventSelection)
                {
                    string eventItemId = string.IsNullOrWhiteSpace(externalEventItemDefinitionId)
                        ? _configuration.EventItemDefinitionId : externalEventItemDefinitionId;
                    if (string.IsNullOrWhiteSpace(eventItemId) ||
                        !_configuration.SessionConfiguration.ObservationItemDefinitions.Any(item =>
                            item.DefinitionId == eventItemId && item.Target == ItemEffectTarget.OneAlly))
                        throw new ArgumentException("A configured OneAlly Event Item ID is required.", nameof(externalEventItemDefinitionId));
                }
            }
            else if (checkpoint.StopReason == SimulationStopReason.DecisionLimitExceeded)
            {
                if (_configuration.MaxDecisionsPerPhase <= checkpoint.PhaseDecisionCount)
                    throw new ArgumentException("The decision limit must be increased beyond the checkpoint count.", nameof(checkpoint));
            }
            else if (checkpoint.StopReason == SimulationStopReason.ConsecutiveRejectionLimitExceeded)
            {
                if (_configuration.MaxConsecutiveRejections <= checkpoint.ConsecutiveRejectionCount)
                    throw new ArgumentException("The rejection limit must be increased beyond the checkpoint count.", nameof(checkpoint));
            }
            else if (checkpoint.StopReason == SimulationStopReason.TrialStepLimitExceeded)
            {
                if (_configuration.MaxTrialSteps <= checkpoint.RunnerStepCount)
                    throw new ArgumentException("The Trial step limit must be increased beyond the checkpoint count.", nameof(checkpoint));
            }
            else throw new ArgumentException("Unsupported resumable stop reason.", nameof(checkpoint));

            // Restore performs the canonical aggregate, ownership, dimensions, RNG and Action-ID validation.
            GameSession.Restore(_configuration.SessionConfiguration, checkpoint.GameSessionState);
        }

        private SimulationTrialResult RunTrialCore(long trialIndex, ulong trialSeed,
            IPlayerStrategy strategy, ActionDispatcher dispatcher,
            SimulationTrialCheckpoint resume, string externalEventItemDefinitionId)
        {
            string resolvedEventItemDefinitionId = string.IsNullOrWhiteSpace(externalEventItemDefinitionId)
                ? _configuration.EventItemDefinitionId : externalEventItemDefinitionId;
            if (trialIndex < 0)
                return Empty(trialIndex, trialSeed, strategy, SimulationStopReason.InvalidConfiguration,
                    "TrialIndex must be non-negative.");
            if (strategy == null)
                return Empty(trialIndex, trialSeed, null, SimulationStopReason.InvalidConfiguration,
                    "A PlayerStrategy is required.");
            if (string.IsNullOrWhiteSpace(strategy.StrategyId) || string.IsNullOrWhiteSpace(strategy.Version))
                return Empty(trialIndex, trialSeed, strategy, SimulationStopReason.InvalidConfiguration,
                    "Strategy ID and Version must be non-empty.");

            GameSession session;
            StrategyState strategyState;
            StrategyRngState strategyRng;
            try
            {
                if (resume == null)
                {
                    using (Measure("Runner.GameSessionInitialization"))
                        session = GameSession.Create(_configuration.SessionConfiguration, trialSeed, dispatcher);
                    strategyState = StrategyState.EmptyFor(strategy.StrategyId, strategy.Version);
                    strategyRng = StrategyRandomSource.Create(trialSeed, strategy.StrategyId, strategy.Version).CaptureState();
                }
                else
                {
                    using (Measure("Runner.GameSessionRestore"))
                        session = GameSession.Restore(_configuration.SessionConfiguration, resume.GameSessionState, dispatcher);
                    strategyState = resume.StrategyState;
                    strategyRng = resume.StrategyRandomState;
                }
            }
            catch (ArgumentException ex)
            {
                return new SimulationTrialResult(trialIndex, trialSeed, strategy.StrategyId,
                    strategy.Version, SimulationStopReason.InvalidConfiguration, ex.Message,
                    null, null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                    null, MakeActionId(trialIndex, 0), null,
                    _configuration.SessionConfiguration.StageConfig);
            }
            catch (Exception ex)
            {
                return new SimulationTrialResult(trialIndex, trialSeed, strategy.StrategyId,
                    strategy.Version, SimulationStopReason.InvalidSessionState, ex.Message,
                    null, null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                    null, new ActionId("trial:" + trialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":action:0"),
                    null, _configuration.SessionConfiguration.StageConfig);
            }

            long decisions = resume?.DecisionCount ?? 0;
            long successful = resume?.SuccessfulActionCount ?? 0;
            long rejected = resume?.RejectedActionCount ?? 0;
            long battles = resume?.BattleCount ?? 0;
            long victories = resume?.VictoryCount ?? 0, defeats = resume?.DefeatCount ?? 0;
            long unresolved = resume?.UnresolvedCount ?? 0, runnerSteps = resume?.RunnerStepCount ?? 0;
            long actionSequence = resume?.ActionSequence ?? 0;
            long phaseDecisionCount = resume?.PhaseDecisionCount ?? 0;
            string decisionWindow = resume?.DecisionWindow;
            ActionId? lastActionId = resume?.LastActionId;
            ActionError? lastActionError = resume?.LastActionError;
            int consecutiveRejections = resume?.ConsecutiveRejectionCount ?? 0;
            var history = (_configuration.RecordExecutionHistory || (resume != null && resume.ExecutionHistory.Count > 0))
                ? new System.Collections.Generic.List<SimulationExecutionRecord>(resume == null
                    ? Array.Empty<SimulationExecutionRecord>()
                    : resume.ExecutionHistory.Where(x => x.Kind != SimulationExecutionEventKind.TrialEnd))
                : null;
            SimulationStopReason stopReason = SimulationStopReason.TrialStepLimitExceeded;
            string detail = "Maximum runner step count reached.";

            if (resume != null && resume.StopReason == SimulationStopReason.EventItemSelectionRequired)
            {
                ActionExecutionState before = session.CaptureCheckpoint();
                StageSessionResult supplied = session.CurrentState.Stage.Phase == StageProgressPhase.RewardPending
                    ? session.ResolveStageAfterBattle(resolvedEventItemDefinitionId)
                    : session.ResolveEnemylessStage(resolvedEventItemDefinitionId);
                if (supplied == null || !supplied.StateChanged || supplied.Status != StageSessionStatus.Progressed)
                    throw new ArgumentException("External Event Item selection was rejected: " + (supplied?.Detail ?? "no result"), nameof(externalEventItemDefinitionId));
                Record(history, decisions, before.Stage.Phase == StageProgressPhase.RewardPending
                    ? SimulationExecutionEventKind.ResolveVictoryReward : SimulationExecutionEventKind.ResolveEnemylessStage,
                    before, session.CaptureCheckpoint(), operationStatus: "stage:" + supplied.Status + ":" + supplied.Failure,
                    externalEventItemDefinitionId: resolvedEventItemDefinitionId,
                    strategyState: strategyState, strategyRandomState: strategyRng);
            }

            while (runnerSteps < _configuration.MaxTrialSteps)
            {
                runnerSteps++;
                ActionExecutionState state = session.CurrentState;
                if (state == null || !ActionExecutionStateValidator.Validate(state).IsValid)
                { stopReason = SimulationStopReason.InvalidSessionState; detail = "GameSession exposed an invalid checkpoint."; break; }

                if (state.BattleResult != null && state.BattleResult.Outcome == BattleOutcome.Unresolved)
                { stopReason = SimulationStopReason.BattleUnresolved; detail = "Battle result is Unresolved; the battle was not retried or advanced."; unresolved++; break; }

                switch (state.Stage.Phase)
                {
                    case StageProgressPhase.Completed:
                        stopReason = SimulationStopReason.Completed; detail = "Run completed."; goto Finished;
                    case StageProgressPhase.GameOver:
                        stopReason = SimulationStopReason.GameOver; detail = "Run ended in GameOver."; goto Finished;
                    case StageProgressPhase.AwaitingEncounterStart:
                    {
                        ActionExecutionState before = session.CaptureCheckpoint();
                        StageSessionResult advance; using (Measure("Runner.StageProgression")) advance = session.AdvanceToNextStage();
                        Record(history, decisions, SimulationExecutionEventKind.AdvanceToNextStage,
                            before, session.CaptureCheckpoint(), operationStatus: StageOperationStatus(advance));
                        if (!HandleStageResult(advance, out stopReason, out detail)) goto Finished;
                        consecutiveRejections = 0;
                        continue;
                    }
                    case StageProgressPhase.Battle:
                    {
                        if (state.Battle == null)
                        { stopReason = SimulationStopReason.InvalidSessionState; detail = "Battle phase has no BattleState."; goto Finished; }
                        battles++;
                        ActionExecutionState before = session.CaptureCheckpoint();
                        BattleAdvanceResult battleResult; using (Measure("Runner.BattleProgress")) battleResult = session.AdvanceBattleToEnd();
                        Record(history, decisions, SimulationExecutionEventKind.BattleAdvance,
                            before, session.CaptureCheckpoint(), operationStatus: BattleOperationStatus(battleResult));
                        if (!battleResult.Succeeded)
                        { stopReason = SimulationStopReason.SessionExecutionError; detail = "Battle advance failed: " + battleResult.Failure + ": " + battleResult.Detail; goto Finished; }
                        if (battleResult.BattleResult == null)
                        { stopReason = SimulationStopReason.SessionExecutionError; detail = "AdvanceBattleToEnd returned success without a terminal BattleResult."; goto Finished; }
                        if (battleResult.BattleResult.Outcome == BattleOutcome.Victory) victories++;
                        else if (battleResult.BattleResult.Outcome == BattleOutcome.Defeat) defeats++;
                        else if (battleResult.BattleResult.Outcome == BattleOutcome.Unresolved)
                        { unresolved++; stopReason = SimulationStopReason.BattleUnresolved; detail = "Battle ended Unresolved; no reward or next Stage was applied."; goto Finished; }
                        else
                        { stopReason = SimulationStopReason.InvalidSessionState; detail = "Battle returned a non-terminal outcome."; goto Finished; }
                        consecutiveRejections = 0;
                        continue;
                    }
                    case StageProgressPhase.RewardPending:
                    {
                        ActionExecutionState before = session.CaptureCheckpoint();
                        StageSessionResult reward; using (Measure("Runner.RewardAndEvent")) reward = session.ResolveStageAfterBattle(_configuration.EventItemDefinitionId);
                        Record(history, decisions, SimulationExecutionEventKind.ResolveVictoryReward,
                            before, session.CaptureCheckpoint(),
                            externalEventItemDefinitionId: reward.EventStarted && reward.StateChanged ? _configuration.EventItemDefinitionId : null,
                            operationStatus: StageOperationStatus(reward));
                        if (reward.Status == StageSessionStatus.NeedsEventItemSelection)
                        { stopReason = SimulationStopReason.EventItemSelectionRequired; detail = reward.Detail; goto Finished; }
                        if (!HandleStageResult(reward, out stopReason, out detail)) goto Finished;
                        consecutiveRejections = 0;
                        continue;
                    }
                    case StageProgressPhase.AwaitingStageChoice:
                    case StageProgressPhase.Preparation:
                    case StageProgressPhase.EventPending:
                    {
                        if (!TryConsumeDecisionWindow(state.Stage.Phase, state.Stage.EncounterIndex,
                            ref decisionWindow, ref phaseDecisionCount,
                            out stopReason, out detail)) goto Finished;
                        if (phaseDecisionCount >= _configuration.MaxDecisionsPerPhase)
                        { stopReason = SimulationStopReason.DecisionLimitExceeded; detail = "Decision limit reached for " + state.Stage.Phase + " at EncounterIndex " + state.Stage.EncounterIndex + "."; goto Finished; }

                        ObservationBuildResult observationResult; using (Measure("Runner.ObservationBuild")) observationResult = session.GetObservation();
                        if (observationResult == null || !observationResult.Succeeded || observationResult.Observation == null)
                        { stopReason = SimulationStopReason.InvalidSessionState; detail = observationResult == null ? "ObservationBuilder returned null." : string.Join("; ", observationResult.Errors); goto Finished; }

                        ActionId proposedId = MakeActionId(trialIndex, actionSequence);
                        StrategyContext context;
                        try
                        {
                            context = new StrategyContext(strategy.StrategyId, strategy.Version,
                                trialIndex, decisions, proposedId, strategyState, strategyRng);
                        }
                        catch (Exception ex)
                        { stopReason = SimulationStopReason.InvalidStrategyDecision; detail = "Could not create StrategyContext: " + ex.Message; goto Finished; }

                        StrategyDecision decision;
                        decisions++;
                        phaseDecisionCount++;
                        try { using (Measure("Strategy.ChooseAction")) decision = strategy.ChooseAction(observationResult.Observation, context); }
                        catch (Exception ex)
                        { stopReason = SimulationStopReason.StrategyException; detail = ex.GetType().Name + ": " + ex.Message; goto Finished; }

                        if (!context.Accepts(decision))
                        { stopReason = SimulationStopReason.InvalidStrategyDecision; detail = "StrategyDecision failed StrategyContext validation; no proposed checkpoint or action was adopted."; goto Finished; }

                        // Stage 10 contract: a valid decision's private state and RNG proposal are
                        // accepted together, even when its GameAction is later rejected.
                        strategyState = decision.NextState;
                        strategyRng = decision.NextStrategyRngState;
                        if (!decision.HasAction)
                        {
                            Record(history, decisions - 1, SimulationExecutionEventKind.StrategyNoAction,
                                state, session.CaptureCheckpoint(), operationStatus: "no-action",
                                strategyState: strategyState, strategyRandomState: strategyRng,
                                strategyDiagnostic: decision.Diagnostic);
                            if (state.Stage.Phase == StageProgressPhase.Preparation &&
                                state.Stage.CurrentSelection != null &&
                                state.Stage.CurrentSelection.EnemyCategory == StageEnemyCategory.None)
                            {
                                ActionExecutionState before = session.CaptureCheckpoint();
                                StageSessionResult enemyless; using (Measure("Runner.EnemylessStageResolution")) enemyless = session.ResolveEnemylessStage(_configuration.EventItemDefinitionId);
                                Record(history, decisions - 1, SimulationExecutionEventKind.ResolveEnemylessStage,
                                    before, session.CaptureCheckpoint(),
                                    externalEventItemDefinitionId: enemyless.EventStarted && enemyless.StateChanged ? _configuration.EventItemDefinitionId : null,
                                    operationStatus: StageOperationStatus(enemyless));
                                if (enemyless.Status == StageSessionStatus.NeedsEventItemSelection)
                                { stopReason = SimulationStopReason.EventItemSelectionRequired; detail = enemyless.Detail; goto Finished; }
                                if (!HandleStageResult(enemyless, out stopReason, out detail))
                                { if (stopReason == SimulationStopReason.SessionExecutionError) stopReason = SimulationStopReason.EnemylessStageExitRequired; goto Finished; }
                                consecutiveRejections = 0;
                                continue;
                            }
                            stopReason = SimulationStopReason.NoActionNotAllowed;
                            detail = "NoAction is not an end-of-turn signal in this phase.";
                            goto Finished;
                        }

                        lastActionId = decision.Action.ActionId;
                        actionSequence = checked(actionSequence + 1);
                        ActionExecutionState actionBefore = session.CaptureCheckpoint();
                        ActionDispatchResult dispatch;
                        try { _configuration.ProfileCollector?.Add("Action.Executed." + decision.Action.Kind); using (Measure("Runner.ActionDispatchAndStateUpdate"))
                            using (Measure("Action.Execute." + decision.Action.Kind)) dispatch = session.ExecuteAction(decision.Action); }
                        catch (Exception ex)
                        { stopReason = SimulationStopReason.SessionExecutionError; detail = "Action dispatch threw: " + ex.Message; goto Finished; }
                        if (dispatch == null)
                        { stopReason = SimulationStopReason.SessionExecutionError; detail = "ActionDispatcher returned null."; goto Finished; }
                        Record(history, decisions - 1, SimulationExecutionEventKind.Action,
                            actionBefore, session.CaptureCheckpoint(), action: decision.Action,
                            actionStatus: dispatch.Status,
                            actionError: dispatch.Succeeded ? (ActionError?)null : dispatch.Error,
                            operationStatus: "action-status:" + dispatch.Status,
                            strategyState: strategyState, strategyRandomState: strategyRng,
                            strategyDiagnostic: decision.Diagnostic);
                        if (dispatch.Succeeded)
                        { successful++; consecutiveRejections = 0; lastActionError = null; continue; }

                        lastActionError = dispatch.Error;
                        if (dispatch.Status != ActionDispatchStatus.Rejected)
                        { stopReason = SimulationStopReason.SessionExecutionError; detail = "Action dispatch failed with " + dispatch.Status + ": " + dispatch.Error.Detail; goto Finished; }
                        rejected++;
                        consecutiveRejections++;
                        if (consecutiveRejections >= _configuration.MaxConsecutiveRejections)
                        { stopReason = SimulationStopReason.ConsecutiveRejectionLimitExceeded; detail = "Consecutive Action rejection limit reached. Last rejection: " + dispatch.Error.Code + ": " + dispatch.Error.Detail; goto Finished; }
                        continue;
                    }
                    default:
                        stopReason = SimulationStopReason.InvalidSessionState;
                        detail = "Unsupported StageProgressPhase " + state.Stage.Phase + ".";
                        goto Finished;
                }
            }

            if (runnerSteps >= _configuration.MaxTrialSteps)
            { stopReason = SimulationStopReason.TrialStepLimitExceeded; detail = "Maximum runner step count reached."; }

        Finished:
            ActionExecutionState final; using (Measure("Runner.TrialResultAndCheckpoint")) final = session.CaptureCheckpoint();
            ActionId nextActionId = MakeActionId(trialIndex, actionSequence);
            Record(history, decisions, SimulationExecutionEventKind.TrialEnd, final, final,
                operationStatus: "trial-end:" + stopReason,
                runnerProgress: new SimulationRunnerProgressSnapshot(stopReason, decisions,
                    actionSequence, runnerSteps, successful, rejected, battles, victories,
                    defeats, unresolved, phaseDecisionCount, consecutiveRejections));
            return new SimulationTrialResult(trialIndex, trialSeed, strategy.StrategyId,
                strategy.Version, stopReason, detail, final, strategyRng, strategyState,
                decisions, successful, rejected, battles, victories, defeats, unresolved,
                runnerSteps, lastActionId, nextActionId, lastActionError,
                _configuration.SessionConfiguration.StageConfig, actionSequence, decisionWindow,
                phaseDecisionCount, consecutiveRejections, history);
        }

        private SimulationProfileCollector.Scope Measure(string id) => _configuration.ProfileCollector == null ? default(SimulationProfileCollector.Scope) : _configuration.ProfileCollector.Measure(id);

        private static void Record(System.Collections.Generic.List<SimulationExecutionRecord> history,
            long decisionStep, SimulationExecutionEventKind kind, ActionExecutionState before,
            ActionExecutionState after, GameAction action = null,
            string externalEventItemDefinitionId = null, ActionDispatchStatus? actionStatus = null,
            ActionError? actionError = null, string operationStatus = null,
            StrategyState strategyState = null, StrategyRngState strategyRandomState = null,
            SimulationRunnerProgressSnapshot runnerProgress = null, string strategyDiagnostic = null)
        {
            if (history == null) return;
            history.Add(new SimulationExecutionRecord(history.Count, Math.Max(0, decisionStep), kind,
                SimulationStateFingerprint.Create(before), SimulationStateFingerprint.Create(after),
                action, externalEventItemDefinitionId, before.Stage.Phase, after.Stage.Phase,
                actionStatus, actionError, operationStatus, strategyState, strategyRandomState, runnerProgress,
                SummarizeState(before), SummarizeState(after), strategyDiagnostic,
                SimulationStateSnapshot.Capture(before), SimulationStateSnapshot.Capture(after)));
        }

        private static string SummarizeState(ActionExecutionState state)
        {
            var text = new System.Text.StringBuilder(256);
            text.Append("phase=").Append(state.Stage.Phase).Append(" encounter=").Append(state.Stage.EncounterIndex)
                .Append(" gold=").Append(state.Economy.Gold).Append(" level=").Append(state.Economy.Level)
                .Append(" xp=").Append(state.Economy.XP).Append(" hp=").Append(state.Economy.CurrentHP)
                .Append('/').Append(state.Economy.MaximumHP).Append(" board=[");
            for (int i = 0; i < state.Board.SlotCount; i++)
            {
                if (i != 0) text.Append(',');
                state.Board.TryGetCharacterIdAt(i, out string id);
                text.Append(i).Append(':').Append(id ?? "empty");
            }
            text.Append("] hand=[");
            for (int i = 0; i < state.Hand.SlotCount; i++)
            {
                if (i != 0) text.Append(',');
                state.Hand.TryGetSlot(i, out HandSlotContent content);
                text.Append(i).Append(':');
                if (content.Kind == HandSlotContentKind.Character) text.Append("C:").Append(content.CharacterInstanceId);
                else if (content.Kind == HandSlotContentKind.Item) text.Append("I:").Append(content.ItemDefinitionId);
                else text.Append("empty");
            }
            return text.Append(']').ToString();
        }

        private static string StageOperationStatus(StageSessionResult result) => result == null
            ? "stage:null" : "stage:" + result.Status + ":" + result.Failure;

        private static string BattleOperationStatus(BattleAdvanceResult result) => result == null
            ? "battle:null" : result.Succeeded
                ? "battle:" + (result.BattleResult == null ? "missing" : result.BattleResult.Outcome.ToString())
                : "battle-failed:" + result.Failure;

        private static bool HandleStageResult(StageSessionResult result,
            out SimulationStopReason reason, out string detail)
        {
            if (result == null)
            { reason = SimulationStopReason.SessionExecutionError; detail = "Stage progression returned null."; return false; }
            if (result.Status == StageSessionStatus.NeedsEventItemSelection)
            { reason = SimulationStopReason.EventItemSelectionRequired; detail = result.Detail; return false; }
            if (result.Status == StageSessionStatus.StoppedUnresolved)
            { reason = SimulationStopReason.BattleUnresolved; detail = result.Detail; return false; }
            if (result.Status == StageSessionStatus.Completed)
            { reason = SimulationStopReason.Completed; detail = result.Detail; return false; }
            if (result.Status == StageSessionStatus.GameOver)
            { reason = SimulationStopReason.GameOver; detail = result.Detail; return false; }
            if (result.Status != StageSessionStatus.Progressed || !result.StateChanged)
            { reason = SimulationStopReason.SessionExecutionError; detail = "Stage progression did not advance: " + result.Status + "/" + result.Failure + ": " + result.Detail; return false; }
            reason = default(SimulationStopReason); detail = string.Empty; return true;
        }

        private static bool TryConsumeDecisionWindow(StageProgressPhase phase, int encounterIndex,
            ref string currentKey, ref long count, out SimulationStopReason reason, out string detail)
        {
            string key = ((int)phase).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                encounterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(key, currentKey, StringComparison.Ordinal))
            { currentKey = key; count = 0; }
            reason = default(SimulationStopReason); detail = string.Empty; return true;
        }

        private static ActionId MakeActionId(long trialIndex, long actionSequence) =>
            new ActionId("trial:" + trialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":action:" + actionSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));

        private SimulationTrialResult Empty(long trialIndex, ulong seed, IPlayerStrategy strategy,
            SimulationStopReason reason, string detail) => new SimulationTrialResult(trialIndex,
                seed, strategy?.StrategyId, strategy?.Version, reason, detail, null, null, null,
                0, 0, 0, 0, 0, 0, 0, 0, null,
                trialIndex < 0 ? new ActionId("invalid-trial:action:0") : MakeActionId(trialIndex, 0),
                null, _configuration.SessionConfiguration.StageConfig);
    }
    /// <summary>Optional bounded in-memory timing/counter collector. It never participates in game decisions.</summary>
    public sealed class SimulationProfileCollector
    {
        private sealed class Aggregate { public long Calls, InclusiveTicks, ExclusiveTicks, MaxTicks; public string Parent; }
        private struct Frame { public string Id; public long Start; public long ChildTicks; }
        private readonly Dictionary<string, Aggregate> _sections = new Dictionary<string, Aggregate>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _counters = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly List<Frame> _stack = new List<Frame>(8);
        public bool Enabled { get; }
        public bool AllocationSamplingEnabled { get; }
        public SimulationProfileCollector(bool enabled = true, bool allocationSamplingEnabled = false)
        { Enabled = enabled; AllocationSamplingEnabled = allocationSamplingEnabled; }
        public Scope Measure(string sectionId)
        {
            if (!Enabled) return default(Scope);
            if (string.IsNullOrWhiteSpace(sectionId)) throw new ArgumentException("Section ID is required.", nameof(sectionId));
            _stack.Add(new Frame { Id = sectionId, Start = Stopwatch.GetTimestamp() });
            return new Scope(this, _stack.Count);
        }
        public void Add(string counterId, long amount = 1)
        {
            if (!Enabled) return;
            if (string.IsNullOrWhiteSpace(counterId)) throw new ArgumentException("Counter ID is required.", nameof(counterId));
            _counters.TryGetValue(counterId, out long value); _counters[counterId] = checked(value + amount);
        }
        private void End(int depth)
        {
            if (depth != _stack.Count) throw new InvalidOperationException("Profile scopes must be disposed in LIFO order.");
            Frame frame = _stack[_stack.Count - 1]; _stack.RemoveAt(_stack.Count - 1);
            long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - frame.Start);
            if (!_sections.TryGetValue(frame.Id, out Aggregate a)) _sections.Add(frame.Id, a = new Aggregate());
            if (_stack.Count > 0 && a.Parent == null) a.Parent = _stack[_stack.Count - 1].Id;
            a.Calls++; a.InclusiveTicks += elapsed; a.ExclusiveTicks += Math.Max(0, elapsed - frame.ChildTicks); a.MaxTicks = Math.Max(a.MaxTicks, elapsed);
            if (_stack.Count > 0) { Frame parent = _stack[_stack.Count - 1]; parent.ChildTicks += elapsed; _stack[_stack.Count - 1] = parent; }
        }
        public IReadOnlyList<SimulationProfileSection> Sections => _sections.Select(x => new SimulationProfileSection(x.Key, x.Value.Parent, x.Value.Calls,
            TicksToSeconds(x.Value.InclusiveTicks), TicksToSeconds(x.Value.ExclusiveTicks), TicksToMicroseconds(x.Value.MaxTicks))).OrderBy(x => x.SectionId, StringComparer.Ordinal).ToArray();
        public IReadOnlyDictionary<string, long> Counters => new Dictionary<string, long>(_counters, StringComparer.Ordinal);
        public static double TicksToSeconds(long ticks) => ticks / (double)Stopwatch.Frequency;
        public static double TicksToMicroseconds(long ticks) => ticks * 1000000d / Stopwatch.Frequency;
        public readonly struct Scope : IDisposable
        {
            private readonly SimulationProfileCollector _owner; private readonly int _depth;
            internal Scope(SimulationProfileCollector owner, int depth) { _owner = owner; _depth = depth; }
            public void Dispose() { if (_owner != null) _owner.End(_depth); }
        }
    }
    public sealed class SimulationProfileSection
    {
        public string SectionId { get; }
        public string ParentSectionId { get; }
        public long CallCount { get; }
        public double InclusiveSeconds { get; }
        public double ExclusiveSeconds { get; }
        public double MaxMicroseconds { get; }
        internal SimulationProfileSection(string id, string parent, long calls, double inclusive, double exclusive, double maxMicroseconds)
        { SectionId=id; ParentSectionId=parent; CallCount=calls; InclusiveSeconds=inclusive; ExclusiveSeconds=exclusive; MaxMicroseconds=maxMicroseconds; }
    }
}
