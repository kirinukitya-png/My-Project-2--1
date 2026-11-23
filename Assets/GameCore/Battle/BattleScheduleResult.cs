namespace ProjectNST.GameCore.Battle
{
    public enum BattleScheduleEvent
    {
        None,
        BattleStartRequired,
        RoundStarted,
        AllyActionRequired,
        AllySkippedByStun,
        AllySkippedByParticipationLimit,
        AllyTurnCompleted,
        EnemyTurnStartRequired,
        EnemyActionRequired,
        EnemyWaiting,
        EnemySkippedDefeated,
        EnemyTurnCompleted,
        PostActionEffectsRequired,
        BattleOutcomeCheckRequired,
        RoundCompleted,
        RoundLimitReached
    }

    public enum BattleScheduleFailure
    {
        None,
        InvalidState,
        BattleAlreadyCompleted,
        RequestSequenceExhausted,
        NoPendingAction,
        StaleActionRequest,
        ActionResolutionFailed,
        InvalidResolutionKind,
        ActorMismatch,
        InvalidUnitMove,
        UnitMoveAlreadyUsed,
        ParticipantLimitReached,
        ArithmeticOverflow
    }

    /// <summary>One deterministic scheduler result; action requests do not advance their actor cursor.</summary>
    public sealed class BattleScheduleResult
    {
        public bool Succeeded { get; }
        public BattleScheduleFailure Failure { get; }
        public BattleScheduleEvent Event { get; }
        public BattleState NextState { get; }
        public bool RequiresActionResolution { get; }
        public BattleActionKind ActionKind { get; }
        public string ActionRequestId { get; }
        public string ActorInstanceId { get; }
        public BattlePendingEffectTrigger PendingEffectTrigger { get; }

        internal BattleScheduleResult(bool succeeded, BattleScheduleFailure failure,
            BattleScheduleEvent scheduleEvent, BattleState nextState,
            bool requiresActionResolution = false, BattleActionKind actionKind = BattleActionKind.None,
            string requestId = null, string actorInstanceId = null,
            BattlePendingEffectTrigger pendingEffectTrigger = null)
        {
            Succeeded = succeeded;
            Failure = failure;
            Event = scheduleEvent;
            NextState = nextState;
            RequiresActionResolution = requiresActionResolution;
            ActionKind = actionKind;
            ActionRequestId = requestId;
            ActorInstanceId = actorInstanceId;
            PendingEffectTrigger = pendingEffectTrigger;
        }
    }
}
