using System;

namespace ProjectNST.GameCore.Actions
{
    /// <summary>Common result envelope. It proposes no state changes and performs no action.</summary>
    [Serializable]
    public sealed class ActionResult
    {
        public ActionId ActionId { get; }
        public ActionResultStatus Status { get; }
        public ActionError Error { get; }
        public bool Succeeded => Status == ActionResultStatus.Succeeded;
        public bool Rejected => Status == ActionResultStatus.Rejected;
        public bool HasError => Status == ActionResultStatus.Error;

        private ActionResult(ActionId actionId, ActionResultStatus status, ActionError error)
        {
            if (string.IsNullOrWhiteSpace(actionId.Value)) throw new ArgumentException("Action ID is required.", nameof(actionId));
            ActionId = actionId; Status = status; Error = error;
        }

        public static ActionResult Success(ActionId actionId) =>
            new ActionResult(actionId, ActionResultStatus.Succeeded, ActionError.None);

        public static ActionResult Reject(ActionId actionId, ActionErrorCode reason, string detail = null)
        {
            if (!Enum.IsDefined(typeof(ActionErrorCode), reason) || reason == ActionErrorCode.None)
                throw new ArgumentOutOfRangeException(nameof(reason));
            return new ActionResult(actionId, ActionResultStatus.Rejected, new ActionError(reason, detail));
        }

        public static ActionResult Fail(ActionId actionId, ActionErrorCode error, string detail = null)
        {
            if (error != ActionErrorCode.ConfigurationError && error != ActionErrorCode.InternalError)
                throw new ArgumentOutOfRangeException(nameof(error), "Failure must describe a configuration or internal error.");
            return new ActionResult(actionId, ActionResultStatus.Error, new ActionError(error, detail));
        }
    }

    public enum ActionResultStatus { Succeeded = 1, Rejected = 2, Error = 3 }

    /// <summary>Stable cross-rule error vocabulary. Detailed rule-specific failures remain in their native Results.</summary>
    public enum ActionErrorCode
    {
        None = 0,
        InvalidPhase = 1,
        InvalidAction = 2,
        InvalidTarget = 3,
        InvalidSlot = 4,
        UnknownOffer = 5,
        UnknownInstance = 6,
        InsufficientGold = 7,
        InsufficientStock = 8,
        BoardLimitExceeded = 9,
        HandFull = 10,
        ItemTargetInvalid = 11,
        AlreadyPurchased = 12,
        DuplicateAction = 13,
        BattleInProgress = 14,
        ConfigurationError = 15,
        InternalError = 16,
        InvalidDefinition = 17,
        InvalidState = 18,
        UnsupportedAction = 19
    }

    [Serializable]
    public readonly struct ActionError
    {
        public ActionErrorCode Code { get; }
        public string Detail { get; }
        public bool IsNone => Code == ActionErrorCode.None;
        public ActionError(ActionErrorCode code, string detail = null)
        {
            if (!Enum.IsDefined(typeof(ActionErrorCode), code)) throw new ArgumentOutOfRangeException(nameof(code));
            Code = code; Detail = detail;
        }
        public static ActionError None => new ActionError(ActionErrorCode.None);
    }
}
