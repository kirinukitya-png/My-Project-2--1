using System;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Strategy
{
    /// <summary>
    /// Opaque, serializable, immutable state owned by one strategy in one trial.
    /// Payload format is defined by the strategy; this core type never parses or mutates it.
    /// </summary>
    [Serializable]
    public sealed class StrategyState
    {
        public string StrategyId { get; }
        public string StrategyVersion { get; }
        public int SchemaVersion { get; }
        public string Payload { get; }

        public StrategyState(string strategyId, string strategyVersion, int schemaVersion, string payload)
        {
            if (string.IsNullOrWhiteSpace(strategyId)) throw new ArgumentException("Strategy ID is required.", nameof(strategyId));
            if (string.IsNullOrWhiteSpace(strategyVersion)) throw new ArgumentException("Strategy version is required.", nameof(strategyVersion));
            if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            StrategyId = strategyId;
            StrategyVersion = strategyVersion;
            SchemaVersion = schemaVersion;
            Payload = payload;
        }

        public static StrategyState EmptyFor(string strategyId, string strategyVersion) =>
            new StrategyState(strategyId, strategyVersion, 1, string.Empty);
    }

    /// <summary>
    /// Public strategy metadata and caller-issued replay identity for one trial decision.
    /// Contains no game state, hidden stock, or game RNG checkpoint.
    /// </summary>
    [Serializable]
    public sealed class StrategyContext
    {
        public string StrategyId { get; }
        public string StrategyVersion { get; }
        public long TrialIndex { get; }
        public long DecisionStep { get; }
        public ActionId NextActionId { get; }
        public StrategyState State { get; }
        /// <summary>Strategy-only checkpoint. Null preserves the Stage 10-E non-random contract.</summary>
        public StrategyRngState StrategyRandomState { get; }

        public StrategyContext(string strategyId, string strategyVersion, long trialIndex,
            long decisionStep, ActionId nextActionId, StrategyState state,
            StrategyRngState strategyRandomState = null)
        {
            if (string.IsNullOrWhiteSpace(strategyId)) throw new ArgumentException("Strategy ID is required.", nameof(strategyId));
            if (string.IsNullOrWhiteSpace(strategyVersion)) throw new ArgumentException("Strategy version is required.", nameof(strategyVersion));
            if (trialIndex < 0) throw new ArgumentOutOfRangeException(nameof(trialIndex));
            if (decisionStep < 0) throw new ArgumentOutOfRangeException(nameof(decisionStep));
            if (string.IsNullOrWhiteSpace(nextActionId.Value)) throw new ArgumentException("Next Action ID is required.", nameof(nextActionId));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!string.Equals(state.StrategyId, strategyId, StringComparison.Ordinal) ||
                !string.Equals(state.StrategyVersion, strategyVersion, StringComparison.Ordinal))
                throw new ArgumentException("StrategyState identity/version must match the context.", nameof(state));
            ValidateNextRandomState(strategyRandomState, trialIndex, strategyId, strategyVersion);
            StrategyId = strategyId;
            StrategyVersion = strategyVersion;
            TrialIndex = trialIndex;
            DecisionStep = decisionStep;
            NextActionId = nextActionId;
            State = state;
            StrategyRandomState = strategyRandomState;
        }

        /// <summary>Checks that this context belongs to the supplied strategy implementation.</summary>
        public bool IsFor(IPlayerStrategy strategy) => strategy != null &&
            string.Equals(strategy.StrategyId, StrategyId, StringComparison.Ordinal) &&
            string.Equals(strategy.Version, StrategyVersion, StringComparison.Ordinal);

        /// <summary>Returns a decision-local mutable copy; the context checkpoint remains unchanged.</summary>
        public StrategyRandomSource CreateStrategyRandomSource()
        {
            if (StrategyRandomState == null)
                throw new InvalidOperationException("No Strategy RNG checkpoint was supplied to this context.");
            return StrategyRandomSource.FromState(StrategyRandomState, StrategyRandomState.TrialSeed,
                StrategyId, StrategyVersion);
        }

        /// <summary>Creates a proposed decision and enforces the caller-issued Action ID.</summary>
        public StrategyDecision Decide(GameAction action, StrategyState nextState, string diagnostic = null)
            => DecideWithRng(action, nextState, StrategyRandomState, diagnostic);

        /// <summary>Returns a proposed action and explicit Strategy RNG checkpoint; neither is committed here.</summary>
        public StrategyDecision DecideWithRng(GameAction action, StrategyState nextState,
            StrategyRngState nextRandomState, string diagnostic = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (action.ActionId != NextActionId)
                throw new ArgumentException("Action must use the Action ID issued in this context.", nameof(action));
            ValidateNextState(nextState);
            ValidateNextRandomState(nextRandomState, TrialIndex, StrategyId, StrategyVersion);
            ValidateRandomTransition(StrategyRandomState, nextRandomState);
            return new StrategyDecision(action, nextState, nextRandomState, diagnostic, StrategyRandomState);
        }

        /// <summary>Represents that no player choice is required; it does not advance game state.</summary>
        public StrategyDecision NoAction(StrategyState nextState = null, string diagnostic = null)
            => NoActionWithRng(nextState, StrategyRandomState, diagnostic);

        /// <summary>Returns a no-action decision with an explicit proposed Strategy RNG checkpoint.</summary>
        public StrategyDecision NoActionWithRng(StrategyState nextState = null,
            StrategyRngState nextRandomState = null, string diagnostic = null)
        {
            nextState = nextState ?? State;
            ValidateNextState(nextState);
            ValidateNextRandomState(nextRandomState, TrialIndex, StrategyId, StrategyVersion);
            ValidateRandomTransition(StrategyRandomState, nextRandomState);
            return new StrategyDecision(null, nextState, nextRandomState, diagnostic, StrategyRandomState);
        }

        public bool Accepts(StrategyDecision decision)
        {
            return decision != null && decision.NextState != null &&
                string.Equals(decision.NextState.StrategyId, StrategyId, StringComparison.Ordinal) &&
                string.Equals(decision.NextState.StrategyVersion, StrategyVersion, StringComparison.Ordinal) &&
                (!decision.HasAction || decision.Action.ActionId == NextActionId) &&
                IsValidRandomState(decision.NextStrategyRngState, TrialIndex, StrategyId, StrategyVersion) &&
                HasCompatibleRandomState(StrategyRandomState, decision.NextStrategyRngState);
        }

        private static bool IsValidRandomState(StrategyRngState state, long trialIndex,
            string strategyId, string strategyVersion) => state == null ||
            (string.Equals(state.StrategyId, strategyId, StringComparison.Ordinal) &&
             string.Equals(state.StrategyVersion, strategyVersion, StringComparison.Ordinal));

        private static bool HasCompatibleRandomState(StrategyRngState current, StrategyRngState next) =>
            current == null ? next == null : next != null && current.TrialSeed == next.TrialSeed &&
                current.CheckpointVersion == next.CheckpointVersion && current.AlgorithmVersion == next.AlgorithmVersion &&
                string.Equals(current.AlgorithmId, next.AlgorithmId, StringComparison.Ordinal);

        private static void ValidateNextRandomState(StrategyRngState state, long trialIndex,
            string strategyId, string strategyVersion)
        {
            if (state == null) return;
            if (trialIndex < 0 || !string.Equals(state.StrategyId, strategyId, StringComparison.Ordinal) ||
                !string.Equals(state.StrategyVersion, strategyVersion, StringComparison.Ordinal))
                throw new ArgumentException("Strategy RNG checkpoint belongs to a different strategy context.", nameof(state));
            StrategyRandomSource.FromState(state, state.TrialSeed, strategyId, strategyVersion);
        }

        private static void ValidateRandomTransition(StrategyRngState current, StrategyRngState next)
        {
            if (current == null ? next != null : next == null || current.TrialSeed != next.TrialSeed)
                throw new ArgumentException("A decision must preserve the context's Strategy RNG identity.", nameof(next));
        }

        private void ValidateNextState(StrategyState nextState)
        {
            if (nextState == null) throw new ArgumentNullException(nameof(nextState));
            if (!string.Equals(nextState.StrategyId, StrategyId, StringComparison.Ordinal) ||
                !string.Equals(nextState.StrategyVersion, StrategyVersion, StringComparison.Ordinal))
                throw new ArgumentException("Next StrategyState identity/version must match the context.", nameof(nextState));
        }
    }

    /// <summary>
    /// One proposed action at most plus tentative strategy-state and RNG checkpoints. The caller
    /// adopts both checkpoints together when it accepts this decision, independently of whether
    /// the Dispatcher accepts the GameAction. Producing this DTO never executes the action.
    /// </summary>
    [Serializable]
    public sealed class StrategyDecision
    {
        public GameAction Action { get; }
        public StrategyState NextState { get; }
        public StrategyRngState NextStrategyRngState { get; }
        public bool StrategyRngAdvanced => NextStrategyRngState != null &&
            !NextStrategyRngState.Equals(_previousStrategyRngState);
        public string Diagnostic { get; }
        public bool HasAction => Action != null;
        private readonly StrategyRngState _previousStrategyRngState;

        internal StrategyDecision(GameAction action, StrategyState nextState,
            StrategyRngState nextStrategyRngState, string diagnostic,
            StrategyRngState previousStrategyRngState = null)
        {
            Action = action;
            NextState = nextState ?? throw new ArgumentNullException(nameof(nextState));
            NextStrategyRngState = nextStrategyRngState;
            _previousStrategyRngState = previousStrategyRngState;
            Diagnostic = diagnostic ?? string.Empty;
        }
    }

    /// <summary>
    /// Selects a declaration from a detached public observation. Implementations must keep
    /// trial-specific progress in StrategyState, not mutable fields shared across trials.
    /// </summary>
    public interface IPlayerStrategy
    {
        string StrategyId { get; }
        string Version { get; }
        StrategyDecision ChooseAction(GameObservation observation, StrategyContext context);
    }
}



