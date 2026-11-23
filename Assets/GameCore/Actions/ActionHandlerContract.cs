using System;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Actions
{
    public enum ActionHandlerResultStatus { Succeeded = 1, Rejected = 2, ConfigurationError = 3, InternalError = 4 }

    [Flags]
    public enum RandomStreamMask
    {
        None = 0,
        Shop = 1,
        Stats = 2,
        Encounter = 4,
        Battle = 8,
        Event = 16,
        Roster = 32,
        Economy = 64,
        All = Shop | Stats | Encounter | Battle | Event | Roster | Economy
    }

    /// <summary>Handler proposal. Only a successful proposal may carry a complete replacement state.</summary>
    public sealed class ActionHandlerResult
    {
        public ActionHandlerResultStatus Status { get; }
        public ActionExecutionState NextState { get; }
        public ActionError Error { get; }
        /// <summary>Streams the handler declares it advanced; Dispatcher verifies this against the returned checkpoint.</summary>
        public RandomStreamMask RandomStreamsChanged { get; }
        public bool Succeeded => Status == ActionHandlerResultStatus.Succeeded;

        private ActionHandlerResult(ActionHandlerResultStatus status, ActionExecutionState nextState,
            ActionError error, RandomStreamMask randomStreamsChanged = RandomStreamMask.None)
        { Status = status; NextState = nextState; Error = error; RandomStreamsChanged = randomStreamsChanged; }

        public static ActionHandlerResult Success(ActionExecutionState nextState,
            RandomStreamMask randomStreamsChanged = RandomStreamMask.None)
        {
            if (nextState == null) throw new ArgumentNullException(nameof(nextState));
            if ((randomStreamsChanged & ~RandomStreamMask.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(randomStreamsChanged));
            return new ActionHandlerResult(ActionHandlerResultStatus.Succeeded, nextState,
                ActionError.None, randomStreamsChanged);
        }
        public static ActionHandlerResult Reject(ActionErrorCode reason, string detail = null)
        {
            ValidateError(reason);
            if (reason == ActionErrorCode.ConfigurationError || reason == ActionErrorCode.InternalError)
                throw new ArgumentOutOfRangeException(nameof(reason));
            return new ActionHandlerResult(ActionHandlerResultStatus.Rejected, null, new ActionError(reason, detail));
        }
        public static ActionHandlerResult ConfigurationError(string detail = null) =>
            new ActionHandlerResult(ActionHandlerResultStatus.ConfigurationError, null,
                new ActionError(ActionErrorCode.ConfigurationError, detail));
        public static ActionHandlerResult InternalError(string detail = null) =>
            new ActionHandlerResult(ActionHandlerResultStatus.InternalError, null,
                new ActionError(ActionErrorCode.InternalError, detail));

        private static void ValidateError(ActionErrorCode code)
        {
            if (!Enum.IsDefined(typeof(ActionErrorCode), code) || code == ActionErrorCode.None)
                throw new ArgumentOutOfRangeException(nameof(code));
        }
    }

    public interface IActionHandler
    {
        GameActionKind Kind { get; }
        ActionHandlerResult Handle(ActionExecutionState state, GameAction action,
            ActionExecutionContext context);
    }

    /// <summary>Signal a missing or invalid immutable rule configuration from a handler.</summary>
    public sealed class ActionExecutionConfigurationException : Exception
    {
        public ActionExecutionConfigurationException(string message) : base(message) { }
        public ActionExecutionConfigurationException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
