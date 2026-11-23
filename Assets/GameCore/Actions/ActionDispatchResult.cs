using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Board;

namespace ProjectNST.GameCore.Actions
{
    public enum ActionDispatchStatus
    {
        Succeeded = 1,
        Rejected = 2,
        ConfigurationError = 3,
        InternalError = 4
    }

    public sealed class ActionDispatchResult
    {
        private readonly ReadOnlyCollection<string> _diagnostics;
        public bool HasActionIdentity { get; }
        public ActionId ActionId { get; }
        public GameActionKind? ActionKind { get; }
        public ActionDispatchStatus Status { get; }
        public ActionError Error { get; }
        /// <summary>On failure this is exactly the original input state; partial handler proposals are discarded.</summary>
        public ActionExecutionState NextState { get; }
        public IReadOnlyList<string> Diagnostics => _diagnostics;
        public bool Succeeded => Status == ActionDispatchStatus.Succeeded;

        internal ActionDispatchResult(ActionExecutionState nextState, ActionExecutionState inputState,
            GameAction action, ActionDispatchStatus status, ActionError error, IEnumerable<string> diagnostics = null)
        {
            NextState = status == ActionDispatchStatus.Succeeded ? nextState : inputState;
            HasActionIdentity = action != null && !string.IsNullOrWhiteSpace(action.ActionId.Value);
            ActionId = HasActionIdentity ? action.ActionId : default(ActionId);
            ActionKind = action == null ? (GameActionKind?)null : action.Kind;
            Status = status; Error = error;
            var copied = diagnostics == null ? new List<string>() : new List<string>(diagnostics);
            _diagnostics = copied.AsReadOnly();
        }
    }

    public sealed class ActionExecutionValidationResult
    {
        private readonly ReadOnlyCollection<string> _errors;
        public bool IsValid => _errors.Count == 0;
        public IReadOnlyList<string> Errors => _errors;
        internal ActionExecutionValidationResult(IEnumerable<string> errors)
        { _errors = new List<string>(errors).AsReadOnly(); }
    }

    /// <summary>Cross-state validation applied before a successful aggregate can be committed.</summary>
    public static class ActionExecutionStateValidator
    {
        public static ActionExecutionValidationResult Validate(ActionExecutionState state)
        {
            var errors = new List<string>();
            if (state == null)
            {
                errors.Add("ActionExecutionState is null.");
                return new ActionExecutionValidationResult(errors);
            }
            if (state.Stage == null) errors.Add("StageProgressState is missing.");
            if (state.Economy == null) errors.Add("EconomyState is missing.");
            if (state.CharacterPool == null) errors.Add("CharacterPool is missing.");
            if (state.Board == null) errors.Add("BoardState is missing.");
            if (state.Hand == null) errors.Add("HandState is missing.");
            if (state.Ownership == null) errors.Add("CharacterOwnershipState is missing.");
            if (state.ItemEffects == null) errors.Add("ItemEffectState is missing.");
            try { ActionExecutionState.ValidateRandomState(state.RandomState); }
            catch (ArgumentException ex) { errors.Add(ex.Message); }
            if (state.Board != null && state.Hand != null && state.Ownership != null)
            {
                BoardHandStateValidationResult boardHand =
                    BoardHandStateValidator.Validate(state.Board, state.Hand, state.Ownership);
                if (!boardHand.IsValid)
                    foreach (string error in boardHand.Errors) errors.Add(error);
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ActionId id in state.ExecutedActionIds)
                if (string.IsNullOrWhiteSpace(id.Value) || !ids.Add(id.Value))
                    errors.Add("Executed Action ID history is invalid or contains duplicates.");
            return new ActionExecutionValidationResult(errors);
        }
    }
}
