using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleAttackKind { AllyNormal, EnemyNormal, AllyUnitMove }
    public enum BattleAttackFailure
    {
        None, InvalidState, NoPendingAction, StaleActionRequest, WrongActionKind,
        ActorMismatch, ActorStunned, ActorUnavailable, NoLivingEnemies,
        InvalidTargetDefinition, TargetSelectionFailed, ArithmeticOverflow,
        SchedulerRejected, PostActionEffectsPending, UnitMoveAvailable,
        NormalAttackSkippedAfterUnitMove, UnitMoveUnavailable
    }

    /// <summary>Atomic combat update and scheduler acknowledgement for one normal attack.</summary>
    public sealed class BattleAttackResult
    {
        private readonly string[] _targetIds;
        private readonly BattlePendingEffectTrigger[] _pendingTriggers;
        public bool Succeeded { get; }
        public BattleAttackFailure Failure { get; }
        public BattleAttackKind Kind { get; }
        public string ActionRequestId { get; }
        public string ActorInstanceId { get; }
        public string SelectedTargetId { get; }
        public IReadOnlyList<string> TargetInstanceIds { get; }
        public int Attack { get; }
        public long TotalDefense { get; }
        public int CalculatedDamage { get; }
        public int ActualEnemyHpLost { get; }
        public int BlockAbsorbed { get; }
        public int ActualPlayerHpLost { get; }
        public string DefeatedEnemyInstanceId { get; }
        public IReadOnlyList<BattlePendingEffectTrigger> PendingEffectTriggers { get; }
        public string UnitMoveId { get; }
        public IReadOnlyList<string> ParticipantInstanceIds { get; }
        public BattleState NextState { get; }
        public Pcg32State NextBattleRandomState { get; }

        internal BattleAttackResult(bool success, BattleAttackFailure failure, BattleAttackKind kind,
            BattleState state, Pcg32State nextRng, string requestId = null, string actorId = null,
            string selectedTargetId = null,
            IEnumerable<string> targetIds = null, int attack = 0, long totalDefense = 0,
            int damage = 0, int enemyHpLost = 0, int blockAbsorbed = 0,
            int playerHpLost = 0, string defeatedEnemyId = null,
            IEnumerable<BattlePendingEffectTrigger> pendingTriggers = null,
            string unitMoveId = null, IEnumerable<string> participantIds = null)
        {
            Succeeded = success; Failure = failure; Kind = kind; NextState = state;
            NextBattleRandomState = nextRng; ActionRequestId = requestId; ActorInstanceId = actorId;
            SelectedTargetId = selectedTargetId;
            _targetIds = targetIds == null ? Array.Empty<string>() : new List<string>(targetIds).ToArray();
            TargetInstanceIds = Array.AsReadOnly(_targetIds);
            Attack = attack; TotalDefense = totalDefense; CalculatedDamage = damage;
            ActualEnemyHpLost = enemyHpLost; BlockAbsorbed = blockAbsorbed;
            ActualPlayerHpLost = playerHpLost; DefeatedEnemyInstanceId = defeatedEnemyId;
            _pendingTriggers = pendingTriggers == null ? Array.Empty<BattlePendingEffectTrigger>() : new List<BattlePendingEffectTrigger>(pendingTriggers).ToArray();
            PendingEffectTriggers = Array.AsReadOnly(_pendingTriggers);
            UnitMoveId = unitMoveId;
            string[] participantCopy = participantIds == null
                ? Array.Empty<string>() : new List<string>(participantIds).ToArray();
            ParticipantInstanceIds = Array.AsReadOnly(participantCopy);
        }
    }
}
