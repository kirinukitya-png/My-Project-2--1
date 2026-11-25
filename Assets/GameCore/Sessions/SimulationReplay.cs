using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Sessions
{
    public enum SimulationExecutionEventKind
    {
        Action = 0,
        StrategyNoAction = 1,
        BattleAdvance = 2,
        ResolveVictoryReward = 3,
        ResolveEnemylessStage = 4,
        AdvanceToNextStage = 5,
        TrialEnd = 6
    }

    public sealed class SimulationRunnerProgressSnapshot
    {
        public SimulationStopReason StopReason { get; }
        public long DecisionCount { get; }
        public long ActionSequence { get; }
        public long RunnerStepCount { get; }
        public long SuccessfulActionCount { get; }
        public long RejectedActionCount { get; }
        public long BattleCount { get; }
        public long VictoryCount { get; }
        public long DefeatCount { get; }
        public long UnresolvedCount { get; }
        public long PhaseDecisionCount { get; }
        public int ConsecutiveRejectionCount { get; }

        public SimulationRunnerProgressSnapshot(SimulationStopReason stopReason,
            long decisions, long actionSequence, long runnerSteps, long successfulActions,
            long rejectedActions, long battles, long victories, long defeats, long unresolved,
            long phaseDecisionCount, int consecutiveRejections)
        {
            StopReason = stopReason; DecisionCount = decisions; ActionSequence = actionSequence;
            RunnerStepCount = runnerSteps; SuccessfulActionCount = successfulActions;
            RejectedActionCount = rejectedActions; BattleCount = battles; VictoryCount = victories;
            DefeatCount = defeats; UnresolvedCount = unresolved; PhaseDecisionCount = phaseDecisionCount;
            ConsecutiveRejectionCount = consecutiveRejections;
        }

        public bool Matches(SimulationTrialResult result) => result != null && StopReason == result.StopReason &&
            DecisionCount == result.DecisionCount && ActionSequence == result.ActionSequence &&
            RunnerStepCount == result.RunnerStepCount && SuccessfulActionCount == result.SuccessfulActionCount &&
            RejectedActionCount == result.RejectedActionCount && BattleCount == result.BattleCount &&
            VictoryCount == result.VictoryCount && DefeatCount == result.DefeatCount &&
            UnresolvedCount == result.UnresolvedCount && PhaseDecisionCount == result.PhaseDecisionCount &&
            ConsecutiveRejectionCount == result.ConsecutiveRejectionCount;
    }

    /// <summary>Immutable public-state counters captured with an execution record for diagnostics.</summary>
    public sealed class SimulationStateSnapshot
    {
        public int EncounterIndex { get; }
        public StageProgressPhase Phase { get; }
        public int Gold { get; }
        public int Level { get; }
        public int XP { get; }
        public int HP { get; }
        public int BoardCharacterCount { get; }
        public int HandCharacterCount { get; }
        public int HandItemCount { get; }
        public int BoardCapacity { get; }
        public int HandCapacity { get; }

        private SimulationStateSnapshot(ActionExecutionState state)
        {
            EncounterIndex = state.Stage.EncounterIndex;
            Phase = state.Stage.Phase;
            Gold = state.Economy.Gold;
            Level = state.Economy.Level;
            XP = state.Economy.XP;
            HP = state.Economy.CurrentHP;
            BoardCapacity = state.Board.SlotCount;
            HandCapacity = state.Hand.SlotCount;
            BoardCharacterCount = Enumerable.Range(0, state.Board.SlotCount)
                .Count(slot => state.Board.TryGetCharacterIdAt(slot, out string id) && id != null);
            int handCharacters = 0;
            int handItems = 0;
            for (int slot = 0; slot < state.Hand.SlotCount; slot++)
            {
                state.Hand.TryGetSlot(slot, out HandSlotContent content);
                if (content.Kind == HandSlotContentKind.Character) handCharacters++;
                else if (content.Kind == HandSlotContentKind.Item) handItems++;
            }
            HandCharacterCount = handCharacters;
            HandItemCount = handItems;
        }

        internal static SimulationStateSnapshot Capture(ActionExecutionState state) =>
            state == null ? null : new SimulationStateSnapshot(state);
    }

    /// <summary>Immutable in-memory event sufficient to reapply one runner/session transition.</summary>
    public sealed class SimulationExecutionRecord
    {
        public long Sequence { get; }
        public long DecisionStep { get; }
        public SimulationExecutionEventKind Kind { get; }
        public GameAction Action { get; }
        public string ExternalEventItemDefinitionId { get; }
        public StageProgressPhase? PhaseBefore { get; }
        public StageProgressPhase? PhaseAfter { get; }
        public ActionDispatchStatus? ActionStatus { get; }
        public ActionError? ActionError { get; }
        public string OperationStatus { get; }
        public string BeforeFingerprint { get; }
        public string AfterFingerprint { get; }
        public StrategyState StrategyStateAfterDecision { get; }
        public StrategyRngState StrategyRandomStateAfterDecision { get; }
        public SimulationRunnerProgressSnapshot RunnerProgress { get; }
        public string BeforeStateSummary { get; }
        public string AfterStateSummary { get; }
        public string StrategyDiagnostic { get; }
        public SimulationStateSnapshot BeforeStateSnapshot { get; }
        public SimulationStateSnapshot AfterStateSnapshot { get; }

        public SimulationExecutionRecord(long sequence, long decisionStep,
            SimulationExecutionEventKind kind, string beforeFingerprint, string afterFingerprint,
            GameAction action = null, string externalEventItemDefinitionId = null,
            StageProgressPhase? phaseBefore = null, StageProgressPhase? phaseAfter = null,
            ActionDispatchStatus? actionStatus = null, ActionError? actionError = null,
            string operationStatus = null, StrategyState strategyStateAfterDecision = null,
            StrategyRngState strategyRandomStateAfterDecision = null,
            SimulationRunnerProgressSnapshot runnerProgress = null,
            string beforeStateSummary = null, string afterStateSummary = null,
            string strategyDiagnostic = null,
            SimulationStateSnapshot beforeStateSnapshot = null,
            SimulationStateSnapshot afterStateSnapshot = null)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (decisionStep < 0) throw new ArgumentOutOfRangeException(nameof(decisionStep));
            if (!Enum.IsDefined(typeof(SimulationExecutionEventKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(beforeFingerprint) || string.IsNullOrWhiteSpace(afterFingerprint))
                throw new ArgumentException("Before and after fingerprints are required.");
            if ((kind == SimulationExecutionEventKind.Action) != (action != null))
                throw new ArgumentException("Action event kind and Action payload must agree.", nameof(action));
            Sequence = sequence; DecisionStep = decisionStep; Kind = kind; Action = action;
            ExternalEventItemDefinitionId = externalEventItemDefinitionId;
            PhaseBefore = phaseBefore; PhaseAfter = phaseAfter; ActionStatus = actionStatus;
            ActionError = actionError; OperationStatus = operationStatus ?? string.Empty;
            BeforeFingerprint = beforeFingerprint; AfterFingerprint = afterFingerprint;
            StrategyStateAfterDecision = strategyStateAfterDecision;
            StrategyRandomStateAfterDecision = strategyRandomStateAfterDecision;
            RunnerProgress = runnerProgress;
            BeforeStateSummary = beforeStateSummary ?? string.Empty;
            AfterStateSummary = afterStateSummary ?? string.Empty;
            BeforeStateSnapshot = beforeStateSnapshot;
            AfterStateSnapshot = afterStateSnapshot;
            StrategyDiagnostic = strategyDiagnostic ?? string.Empty;
        }
    }

    /// <summary>Stable SHA-256 fingerprint over public immutable state properties; no runtime HashCode is used.</summary>
    public static class SimulationStateFingerprint
    {
        public static string Create(object value)
        {
            var text = new StringBuilder(4096);
            AppendValue(text, value, new List<object>());
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))
                    .Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static void AppendValue(StringBuilder text, object value, List<object> seen)
        {
            if (value == null) { text.Append("null;"); return; }
            Type type = value.GetType();
            text.Append(type.FullName).Append(':');
            if (value is string s) { text.Append(s.Length).Append('#').Append(s).Append(';'); return; }
            if (type.IsEnum) { text.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture)).Append(';'); return; }
            if (type.IsPrimitive || value is decimal)
            { text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';'); return; }
            if (!type.IsValueType)
            {
                if (seen.Any(existing => ReferenceEquals(existing, value))) { text.Append("ref;"); return; }
                seen.Add(value);
            }
            if (value is IDictionary dictionary)
            {
                var entries = new List<Tuple<string, object>>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    var keyText = new StringBuilder();
                    AppendValue(keyText, entry.Key, new List<object>());
                    entries.Add(Tuple.Create(keyText.ToString(), entry.Value));
                }
                foreach (var entry in entries.OrderBy(x => x.Item1, StringComparer.Ordinal))
                { text.Append(entry.Item1); AppendValue(text, entry.Item2, seen); }
                text.Append("enddict;"); return;
            }
            Type dictionaryInterface = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                 i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
            if (dictionaryInterface != null && value is IEnumerable genericDictionary)
            {
                var entries = new List<Tuple<string, object>>();
                foreach (object pair in genericDictionary)
                {
                    Type pairType = pair.GetType();
                    object key = pairType.GetProperty("Key").GetValue(pair, null);
                    object entryValue = pairType.GetProperty("Value").GetValue(pair, null);
                    var keyText = new StringBuilder();
                    AppendValue(keyText, key, new List<object>());
                    entries.Add(Tuple.Create(keyText.ToString(), entryValue));
                }
                foreach (var entry in entries.OrderBy(x => x.Item1, StringComparer.Ordinal))
                { text.Append(entry.Item1); AppendValue(text, entry.Item2, seen); }
                text.Append("enddict;"); return;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable) AppendValue(text, item, seen);
                text.Append("endenumerable;"); return;
            }
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
            foreach (PropertyInfo property in properties)
            {
                text.Append(property.Name).Append('=');
                AppendValue(text, property.GetValue(value, null), seen);
            }
            text.Append("endobject;");
        }
    }

    public sealed class SimulationReplayResult
    {
        public bool Succeeded { get; }
        public long? FirstMismatchSequence { get; }
        public string MismatchKind { get; }
        public string Expected { get; }
        public string Actual { get; }
        public bool FinalStateMatches { get; }
        public bool GameRandomCheckpointMatches { get; }
        public bool StrategyCheckpointMatches { get; }
        public bool RunnerProgressMatches { get; }
        public bool ActionHistoryMatches { get; }
        public ActionExecutionState ReplayFinalCheckpoint { get; }
        internal SimulationReplayResult(bool succeeded, long? sequence, string kind,
            string expected, string actual, bool stateMatches, bool rngMatches, bool actionHistoryMatches,
            bool strategyCheckpointMatches = true, bool runnerProgressMatches = true,
            ActionExecutionState replayFinalCheckpoint = null)
        {
            Succeeded = succeeded; FirstMismatchSequence = sequence; MismatchKind = kind ?? string.Empty;
            Expected = expected ?? string.Empty; Actual = actual ?? string.Empty;
            FinalStateMatches = stateMatches; GameRandomCheckpointMatches = rngMatches;
            StrategyCheckpointMatches = strategyCheckpointMatches;
            RunnerProgressMatches = runnerProgressMatches;
            ActionHistoryMatches = actionHistoryMatches;
            ReplayFinalCheckpoint = replayFinalCheckpoint;
        }
    }

    /// <summary>Reapplies recorded actions and automatic Session transitions without asking a Strategy to decide again.</summary>
    public sealed class SimulationReplayVerifier
    {
        public SimulationReplayResult Verify(SimulationRunnerConfiguration configuration,
            SimulationTrialResult expected, IReadOnlyList<SimulationExecutionRecord> history,
            ActionDispatcher dispatcher = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (expected.FinalCheckpoint == null)
                return Mismatch(0, "MissingFinalCheckpoint", "captured GameSession", "null");
            GameSession session;
            try { session = GameSession.Create(configuration.SessionConfiguration, expected.TrialSeed, dispatcher); }
            catch (Exception ex) { return Mismatch(0, "InitialState", "valid initial session", ex.GetType().Name + ": " + ex.Message); }

            for (int i = 0; i < history.Count; i++)
            {
                SimulationExecutionRecord record = history[i];
                if (record == null || record.Sequence != i)
                    return Mismatch(i, "HistorySequence", i.ToString(CultureInfo.InvariantCulture), record?.Sequence.ToString(CultureInfo.InvariantCulture) ?? "null", session.CurrentState);
                string before = SimulationStateFingerprint.Create(session.CurrentState);
                if (!string.Equals(before, record.BeforeFingerprint, StringComparison.Ordinal))
                    return Mismatch(i, "BeforeStateFingerprint", record.BeforeFingerprint, before, session.CurrentState);
                string operation = ReplayOne(session, record);
                if (!string.Equals(operation, record.OperationStatus, StringComparison.Ordinal))
                    return Mismatch(i, "OperationResult", record.OperationStatus, operation, session.CurrentState);
                string after = SimulationStateFingerprint.Create(session.CurrentState);
                if (!string.Equals(after, record.AfterFingerprint, StringComparison.Ordinal))
                    return Mismatch(i, "AfterStateFingerprint", record.AfterFingerprint, after, session.CurrentState);
            }

            string final = SimulationStateFingerprint.Create(session.CurrentState);
            string wanted = SimulationStateFingerprint.Create(expected.FinalCheckpoint);
            bool stateMatches = string.Equals(final, wanted, StringComparison.Ordinal);
            bool rngMatches = session.CurrentState.RandomState.Equals(expected.FinalCheckpoint.RandomState);
            bool actionsMatch = session.CurrentState.ExecutedActionIds.SequenceEqual(expected.FinalCheckpoint.ExecutedActionIds);
            bool strategyMatches = StrategyStateEquals(history, expected.FinalStrategyState) &&
                Equals(history.LastOrDefault(x => x.StrategyRandomStateAfterDecision != null)?.StrategyRandomStateAfterDecision,
                    expected.FinalStrategyRandomState);
            SimulationExecutionRecord endRecord = history.LastOrDefault(x => x.Kind == SimulationExecutionEventKind.TrialEnd);
            bool progressMatches = endRecord?.RunnerProgress != null && endRecord.RunnerProgress.Matches(expected);
            if (!stateMatches) return new SimulationReplayResult(false, history.Count, "FinalState", wanted, final, false, rngMatches, actionsMatch, strategyMatches, progressMatches, session.CurrentState);
            if (!rngMatches) return new SimulationReplayResult(false, history.Count, "GameRandomCheckpoint", wanted, final, true, false, actionsMatch, strategyMatches, progressMatches, session.CurrentState);
            if (!actionsMatch) return new SimulationReplayResult(false, history.Count, "ActionIdHistory", string.Join("|", expected.FinalCheckpoint.ExecutedActionIds), string.Join("|", session.CurrentState.ExecutedActionIds), true, true, false, strategyMatches, progressMatches, session.CurrentState);
            if (!strategyMatches) return new SimulationReplayResult(false, history.Count, "StrategyCheckpoint", "recorded final Strategy state", "mismatch", true, true, actionsMatch, false, progressMatches, session.CurrentState);
            if (!progressMatches) return new SimulationReplayResult(false, history.Count, "RunnerProgress", "recorded counters", "mismatch", true, true, actionsMatch, true, false, session.CurrentState);
            return new SimulationReplayResult(true, null, string.Empty, string.Empty, string.Empty, true, true, true, true, true, session.CurrentState);
        }

        private static bool StrategyStateEquals(IReadOnlyList<SimulationExecutionRecord> records, StrategyState expected)
        {
            SimulationExecutionRecord last = records.LastOrDefault(x => x.StrategyStateAfterDecision != null);
            if (last == null) return expected == null || expected.Payload.Length == 0;
            StrategyState actual = last.StrategyStateAfterDecision;
            return actual.StrategyId == expected?.StrategyId && actual.StrategyVersion == expected?.StrategyVersion &&
                actual.SchemaVersion == expected?.SchemaVersion && actual.Payload == expected?.Payload;
        }

        private static string ReplayOne(GameSession session, SimulationExecutionRecord record)
        {
            switch (record.Kind)
            {
                case SimulationExecutionEventKind.Action:
                {
                    ActionDispatchResult result = session.ExecuteAction(record.Action);
                    if (record.ActionStatus != result.Status) return "action-status:" + result.Status;
                    if (record.ActionError.HasValue && record.ActionError.Value.Code != result.Error.Code)
                        return "action-error:" + result.Error.Code;
                    return "action-status:" + result.Status;
                }
                case SimulationExecutionEventKind.StrategyNoAction:
                    return "no-action";
                case SimulationExecutionEventKind.BattleAdvance:
                {
                    BattleAdvanceResult result = session.AdvanceBattleToEnd();
                    return result.Succeeded ? "battle:" + (result.BattleResult == null ? "missing" : result.BattleResult.Outcome.ToString()) : "battle-failed:" + result.Failure;
                }
                case SimulationExecutionEventKind.ResolveVictoryReward:
                {
                    StageSessionResult result = session.ResolveStageAfterBattle(record.ExternalEventItemDefinitionId);
                    return "stage:" + result.Status + ":" + result.Failure;
                }
                case SimulationExecutionEventKind.ResolveEnemylessStage:
                {
                    StageSessionResult result = session.ResolveEnemylessStage(record.ExternalEventItemDefinitionId);
                    return "stage:" + result.Status + ":" + result.Failure;
                }
                case SimulationExecutionEventKind.AdvanceToNextStage:
                {
                    StageSessionResult result = session.AdvanceToNextStage();
                    return "stage:" + result.Status + ":" + result.Failure;
                }
                case SimulationExecutionEventKind.TrialEnd:
                    return record.RunnerProgress == null ? "trial-end:missing-progress" :
                        "trial-end:" + record.RunnerProgress.StopReason;
                default: return "unknown-event";
            }
        }

        private static SimulationReplayResult Mismatch(long sequence, string kind, string expected, string actual,
            ActionExecutionState replayState = null) =>
            new SimulationReplayResult(false, sequence, kind, expected, actual, false, false, false,
                replayFinalCheckpoint: replayState);
    }
}
