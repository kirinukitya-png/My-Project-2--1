using ProjectNST.GameCore.Battle;

namespace ProjectNST.GameCore.Sessions
{
    public enum BattleAdvanceStatus
    {
        Succeeded,
        Rejected,
        InternalError
    }

    public enum BattleAdvanceFailure
    {
        None,
        InvalidPhase,
        BattleStateMissing,
        BattleAlreadyFinished,
        RuleRejected,
        InternalError,
        SafetyLimitReached,
        InvalidConfiguration
    }

    /// <summary>Immutable summary of a GameSession battle-advance request.</summary>
    public sealed class BattleAdvanceResult
    {
        public BattleAdvanceStatus Status { get; }
        public BattleAdvanceFailure Failure { get; }
        public string Detail { get; }
        public int RoundsAdvanced { get; }
        public bool BattleInProgress { get; }
        public bool BattleFinished { get; }
        public BattleResult BattleResult { get; }
        public bool Succeeded => Status == BattleAdvanceStatus.Succeeded;

        private BattleAdvanceResult(BattleAdvanceStatus status, BattleAdvanceFailure failure,
            string detail, int roundsAdvanced, bool battleInProgress, BattleResult battleResult)
        {
            Status = status;
            Failure = failure;
            Detail = detail ?? string.Empty;
            RoundsAdvanced = roundsAdvanced;
            BattleInProgress = status == BattleAdvanceStatus.Succeeded && battleInProgress;
            BattleResult = battleResult;
            BattleFinished = status == BattleAdvanceStatus.Succeeded && battleResult != null;
        }

        internal static BattleAdvanceResult Success(int roundsAdvanced, bool battleInProgress,
            BattleResult result) => new BattleAdvanceResult(BattleAdvanceStatus.Succeeded,
                BattleAdvanceFailure.None, string.Empty, roundsAdvanced, battleInProgress, result);

        internal static BattleAdvanceResult Rejected(BattleAdvanceFailure failure, string detail,
            int roundsAdvanced = 0, BattleResult result = null) => new BattleAdvanceResult(
                BattleAdvanceStatus.Rejected, failure, detail, roundsAdvanced, false, result);

        internal static BattleAdvanceResult InternalError(string detail) => new BattleAdvanceResult(
            BattleAdvanceStatus.InternalError, BattleAdvanceFailure.InternalError, detail, 0, false, null);
    }
}
