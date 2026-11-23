using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleUnitMoveDecisionKind
    {
        MoveAvailable,
        NormalAttackRequired,
        SkipNormalAttackAfterMove,
        InvalidAction,
        PostActionEffectsPending
    }

    /// <summary>Decision made for the current ally request, without changing it unless a post-move pass is required.</summary>
    public sealed class BattleUnitMoveDecision
    {
        public BattleUnitMoveDecisionKind Kind { get; }
        public UnitMoveState SelectedMove { get; }
        public BattleState NextState { get; }
        public BattleScheduleFailure ScheduleFailure { get; }
        public bool Succeeded => Kind != BattleUnitMoveDecisionKind.InvalidAction &&
            Kind != BattleUnitMoveDecisionKind.PostActionEffectsPending && ScheduleFailure == BattleScheduleFailure.None;

        internal BattleUnitMoveDecision(BattleUnitMoveDecisionKind kind, UnitMoveState selectedMove,
            BattleState nextState, BattleScheduleFailure failure = BattleScheduleFailure.None)
        { Kind = kind; SelectedMove = selectedMove; NextState = nextState; ScheduleFailure = failure; }
    }

    /// <summary>Pure UnitMove selection and attack resolution built on the established Stage 6 synergy snapshot.</summary>
    public static class BattleUnitMoveRules
    {
        public static IReadOnlyList<UnitMoveState> GetAvailableMoves(BattleState state, string actorInstanceId)
        {
            var available = new List<UnitMoveState>();
            if (state == null || state.Phase != BattlePhase.PlayerTurn ||
                state.NextActorSide != BattleUnitSide.Ally || state.NextActorIndex >= state.Allies.Count ||
                string.IsNullOrWhiteSpace(actorInstanceId)) return available.AsReadOnly();
            BattleUnitState actor = state.Allies[state.NextActorIndex];
            if (!string.Equals(actor.InstanceId, actorInstanceId, StringComparison.Ordinal) || actor.StunRounds > 0 ||
                BattleTurnRules.GetParticipationCount(state, actorInstanceId) >= BattleTurnRules.GetParticipationLimit(actor))
                return available.AsReadOnly();

            foreach (UnitMoveState move in state.UnitMoves)
            {
                if (Contains(state.UsedUnitMoveIds, move.MoveId) || !Contains(move.ParticipantInstanceIds, actorInstanceId)) continue;
                bool allAvailable = true;
                foreach (string participantId in move.ParticipantInstanceIds)
                {
                    if (!BattleTurnRules.CanParticipateInUnitMove(state, participantId))
                    { allAvailable = false; break; }
                }
                if (allAvailable) available.Add(move);
            }
            return available.AsReadOnly();
        }

        /// <summary>Returns the highest priority eligible move, normal fallback, or a pass after previous participation.</summary>
        public static BattleUnitMoveDecision DecideCurrentAllyAction(BattleState state, string requestId)
        {
            BattleScheduleFailure validation = ValidateAllyRequest(state, requestId, out BattleUnitState actor);
            if (validation != BattleScheduleFailure.None)
                return new BattleUnitMoveDecision(BattleUnitMoveDecisionKind.InvalidAction, null, state, validation);
            if (state.PendingEffectTriggers.Count > 0)
                return new BattleUnitMoveDecision(BattleUnitMoveDecisionKind.PostActionEffectsPending, null, state);

            IReadOnlyList<UnitMoveState> moves = GetAvailableMoves(state, actor.InstanceId);
            if (moves.Count > 0)
                return new BattleUnitMoveDecision(BattleUnitMoveDecisionKind.MoveAvailable, moves[0], state);
            if (BattleTurnRules.GetParticipationCount(state, actor.InstanceId) == 0)
                return new BattleUnitMoveDecision(BattleUnitMoveDecisionKind.NormalAttackRequired, null, state);

            BattleScheduleResult pass = BattleTurnRules.ResolveAction(state,
                BattleActionResolution.PassAfterUnitMove(requestId));
            return pass.Succeeded
                ? new BattleUnitMoveDecision(BattleUnitMoveDecisionKind.SkipNormalAttackAfterMove, null, pass.NextState)
                : new BattleUnitMoveDecision(BattleUnitMoveDecisionKind.InvalidAction, null, state, pass.Failure);
        }

        public static BattleAttackResult ResolveUnitMoveAttack(BattleState state, string requestId, string moveId)
        {
            if (state == null) return Fail(BattleAttackFailure.InvalidState, null);
            BattleAttackFailure validation = ValidatePending(state, requestId);
            if (validation != BattleAttackFailure.None) return Fail(validation, state);
            if (state.PendingEffectTriggers.Count > 0) return Fail(BattleAttackFailure.PostActionEffectsPending, state);
            BattleUnitState actor = state.Allies[state.NextActorIndex];
            IReadOnlyList<UnitMoveState> available = GetAvailableMoves(state, actor.InstanceId);
            if (available.Count == 0 || !string.Equals(available[0].MoveId, moveId, StringComparison.Ordinal))
                return Fail(BattleAttackFailure.UnitMoveUnavailable, state);
            UnitMoveState move = available[0];

            long attackTotal = 0;
            foreach (string participantId in move.ParticipantInstanceIds)
            {
                BattleUnitState participant = Find(state.Allies, participantId);
                if (participant == null) return Fail(BattleAttackFailure.ActorUnavailable, state);
                attackTotal += participant.Attack;
            }
            if (attackTotal < int.MinValue || attackTotal > int.MaxValue)
                return Fail(BattleAttackFailure.ArithmeticOverflow, state);

            var candidates = new List<BattleUnitState>();
            foreach (BattleUnitState enemy in state.Enemies)
                if (enemy.CurrentHP > 0) candidates.Add(enemy);
            if (candidates.Count == 0) return Fail(BattleAttackFailure.NoLivingEnemies, state);

            Pcg32RandomSource random;
            try { random = Pcg32RandomSource.FromState(state.BattleRandomState); }
            catch (ArgumentException) { return Fail(BattleAttackFailure.InvalidState, state); }
            int selectedIndex;
            try { selectedIndex = random.NextInt(0, candidates.Count); }
            catch (ArgumentException) { return Fail(BattleAttackFailure.InvalidState, state); }
            BattleUnitState target = candidates[selectedIndex];
            BattleDamageResult damage = BattleDamageRules.Calculate((int)attackTotal, target.Defense);
            if (!damage.Succeeded) return Fail(BattleAttackFailure.ArithmeticOverflow, state);

            int actualLoss = Math.Min(target.CurrentHP, damage.Damage);
            int nextHp = Math.Max(0, target.CurrentHP - damage.Damage);
            int actualDefenseReduction = damage.Damage == 0
                ? Math.Min(target.Defense, move.ParticipantCount) : 0;
            long nextTemporaryReduction = (long)target.TemporaryDefenseReduction + actualDefenseReduction;
            if (nextTemporaryReduction > int.MaxValue)
                return Fail(BattleAttackFailure.ArithmeticOverflow, state);
            int nextDefense = target.Defense - actualDefenseReduction;
            BattleUnitState updatedTarget = target.WithScheduledState(defense: nextDefense,
                temporaryDefenseReduction: (int)nextTemporaryReduction).WithCombatState(currentHP: nextHp);
            var enemies = Copy(state.Enemies);
            enemies[IndexOf(enemies, target.InstanceId)] = updatedTarget;

            BattleState combatState = state.CopyWith(enemies: enemies,
                battleRandomState: random.CaptureState(),
                battleOutcomeCheckPending: state.BattleOutcomeCheckPending ||
                    (nextHp == 0 && AreAllEnemiesDefeated(enemies)));
            BattleScheduleResult scheduled = BattleTurnRules.ResolveAction(combatState,
                BattleActionResolution.UnitMoveCompleted(requestId, move.MoveId,
                    move.ParticipantInstanceIds, endsActorTurn: false));
            if (!scheduled.Succeeded) return Fail(BattleAttackFailure.SchedulerRejected, state);

            var triggers = new List<BattlePendingEffectTrigger>();
            string defeatedId = nextHp == 0 ? target.InstanceId : null;
            if (defeatedId != null && target.PigEffectIds.Count > 0)
                triggers.Add(new BattlePendingEffectTrigger(requestId + ":PIG", requestId,
                    BattleEffectTriggerKind.EnemyDefeated, target.InstanceId, actor.InstanceId,
                    target.PigEffectIds, new[] { target.InstanceId }));
            BattleState next = triggers.Count == 0 ? scheduled.NextState :
                scheduled.NextState.CopyWith(pendingEffectTriggers: triggers, replacePendingEffectTriggers: true);
            return new BattleAttackResult(true, BattleAttackFailure.None, BattleAttackKind.AllyUnitMove,
                next, next.BattleRandomState, requestId, actor.InstanceId, target.InstanceId,
                new[] { target.InstanceId }, (int)attackTotal, target.Defense, damage.Damage,
                actualLoss, 0, 0, defeatedId, triggers, move.MoveId, move.ParticipantInstanceIds);
        }

        private static BattleAttackFailure ValidatePending(BattleState state, string requestId)
        {
            BattleScheduleFailure validation = ValidateAllyRequest(state, requestId, out _);
            if (validation == BattleScheduleFailure.NoPendingAction) return BattleAttackFailure.NoPendingAction;
            if (validation == BattleScheduleFailure.StaleActionRequest) return BattleAttackFailure.StaleActionRequest;
            if (validation != BattleScheduleFailure.None) return BattleAttackFailure.ActorMismatch;
            return BattleAttackFailure.None;
        }

        private static BattleScheduleFailure ValidateAllyRequest(BattleState state, string requestId,
            out BattleUnitState actor)
        {
            actor = null;
            if (state == null) return BattleScheduleFailure.InvalidState;
            if (state.PendingActionKind == BattleActionKind.None) return BattleScheduleFailure.NoPendingAction;
            if (!string.Equals(state.PendingActionRequestId, requestId, StringComparison.Ordinal))
                return BattleScheduleFailure.StaleActionRequest;
            if (state.PendingActionKind != BattleActionKind.Ally || state.NextActorSide != BattleUnitSide.Ally ||
                state.NextActorIndex >= state.Allies.Count) return BattleScheduleFailure.ActorMismatch;
            actor = state.Allies[state.NextActorIndex];
            if (!string.Equals(actor.InstanceId, state.PendingActorInstanceId, StringComparison.Ordinal))
                return BattleScheduleFailure.ActorMismatch;
            if (actor.StunRounds > 0) return BattleScheduleFailure.ActorMismatch;
            return BattleScheduleFailure.None;
        }

        private static BattleAttackResult Fail(BattleAttackFailure failure, BattleState state) =>
            new BattleAttackResult(false, failure, BattleAttackKind.AllyUnitMove, state,
                state == null ? default : state.BattleRandomState);

        private static BattleUnitState Find(IReadOnlyList<BattleUnitState> units, string id)
        { foreach (BattleUnitState unit in units) if (string.Equals(unit.InstanceId, id, StringComparison.Ordinal)) return unit; return null; }
        private static int IndexOf(IReadOnlyList<BattleUnitState> units, string id)
        { for (int i = 0; i < units.Count; i++) if (string.Equals(units[i].InstanceId, id, StringComparison.Ordinal)) return i; return -1; }
        private static BattleUnitState[] Copy(IReadOnlyList<BattleUnitState> units)
        { var copy = new BattleUnitState[units.Count]; for (int i = 0; i < copy.Length; i++) copy[i] = units[i]; return copy; }
        private static bool AreAllEnemiesDefeated(IReadOnlyList<BattleUnitState> enemies)
        {
            if (enemies.Count == 0) return false;
            foreach (BattleUnitState enemy in enemies)
                if (enemy.CurrentHP > 0) return false;
            return true;
        }
        private static bool Contains(IReadOnlyList<string> values, string value)
        { for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], value, StringComparison.Ordinal)) return true; return false; }
    }
}
