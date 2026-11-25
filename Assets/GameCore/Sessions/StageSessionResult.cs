using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Sessions
{
    public enum StageSessionStatus
    {
        Progressed,
        NeedsEventItemSelection,
        StoppedUnresolved,
        GameOver,
        Completed,
        Rejected,
        InternalError
    }

    public enum StageSessionFailure
    {
        None,
        InvalidPhase,
        MissingBattleResult,
        BattleOutcomeMismatch,
        EventItemRequired,
        InvalidEventItem,
        ActiveEventMustEndFirst,
        RuleRejected,
        ConfigurationError,
        InvalidNextState
    }

    /// <summary>Immutable result for an explicit stage progression request.</summary>
    public sealed class StageSessionResult
    {
        public StageSessionStatus Status { get; }
        public StageSessionFailure Failure { get; }
        public string Detail { get; }
        public StageRewardResult Reward { get; }
        public bool EventStarted { get; }
        public bool StateChanged { get; }
        public StageProgressPhase Phase { get; }
        public bool Succeeded => Status == StageSessionStatus.Progressed ||
            Status == StageSessionStatus.StoppedUnresolved ||
            Status == StageSessionStatus.GameOver || Status == StageSessionStatus.Completed;

        private StageSessionResult(StageSessionStatus status, StageSessionFailure failure,
            string detail, StageRewardResult reward, bool eventStarted, bool stateChanged,
            StageProgressPhase phase)
        {
            Status = status;
            Failure = failure;
            Detail = detail ?? string.Empty;
            Reward = reward;
            EventStarted = eventStarted;
            StateChanged = stateChanged;
            Phase = phase;
        }

        internal static StageSessionResult Create(StageSessionStatus status,
            StageSessionFailure failure, string detail, StageRewardResult reward,
            bool eventStarted, bool stateChanged, StageProgressPhase phase) =>
            new StageSessionResult(status, failure, detail, reward, eventStarted, stateChanged, phase);
    }
}
