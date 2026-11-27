using System;
using System.Collections.Generic;
using System.Globalization;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Battle
{
    /// <summary>Pure round and turn scheduler. Combat resolution and RNG are deliberately external.</summary>
    public static class BattleTurnRules
    {
        public static BattleScheduleResult RequestNext(BattleState state)
        {
            if (state == null) return Fail(BattleScheduleFailure.InvalidState, null);
            if (state.Status == BattleExecutionStatus.Completed || state.Phase == BattlePhase.Completed)
                return Fail(BattleScheduleFailure.BattleAlreadyCompleted, state);
            if (state.PendingEffectTriggers.Count > 0)
                return new BattleScheduleResult(true, BattleScheduleFailure.None,
                    BattleScheduleEvent.PostActionEffectsRequired, state,
                    pendingEffectTrigger: state.PendingEffectTriggers[0]);
            if (state.BattleOutcomeCheckPending)
                return Success(BattleScheduleEvent.BattleOutcomeCheckRequired, state);
            if (state.PendingActionKind != BattleActionKind.None)
                return PendingRequest(state);

            switch (state.Phase)
            {
                case BattlePhase.BattleStart:
                    return Issue(state, BattleActionKind.BattleStart, BattleScheduleEvent.BattleStartRequired, null);
                case BattlePhase.RoundStart:
                    return Success(BattleScheduleEvent.RoundStarted,
                        state.CopyWith(phase: BattlePhase.PlayerTurn, nextActorSide: BattleUnitSide.Ally,
                            nextActorIndex: 0, usedUnitMoveIds: Array.Empty<string>(),
                            participationCounts: new Dictionary<string, int>(StringComparer.Ordinal),
                            replaceRoundRecords: true));
                case BattlePhase.PlayerTurn:
                    return RequestAlly(state);
                case BattlePhase.EnemyTurnStart:
                    return Issue(state, BattleActionKind.EnemyTurnStart,
                        BattleScheduleEvent.EnemyTurnStartRequired, null);
                case BattlePhase.EnemyTurn:
                    return RequestEnemy(state);
                case BattlePhase.RoundEnd:
                    return CompleteRound(state);
                case BattlePhase.RoundLimitReached:
                    return Success(BattleScheduleEvent.RoundLimitReached, state);
                default:
                    return Fail(BattleScheduleFailure.InvalidState, state);
            }
        }

        public static BattleScheduleResult ResolveAction(BattleState state,
            BattleActionResolution resolution)
        {
            if (state == null || resolution == null)
                return Fail(BattleScheduleFailure.InvalidState, state);
            if (state.PendingActionKind == BattleActionKind.None)
                return Fail(BattleScheduleFailure.NoPendingAction, state);
            if (!string.Equals(state.PendingActionRequestId, resolution.RequestId, StringComparison.Ordinal))
                return Fail(BattleScheduleFailure.StaleActionRequest, state);
            if (!resolution.Succeeded)
                return Fail(BattleScheduleFailure.ActionResolutionFailed, state);

            switch (state.PendingActionKind)
            {
                case BattleActionKind.BattleStart:
                    if (resolution.Kind != BattleActionResolutionKind.BoundaryCompleted)
                        return Fail(BattleScheduleFailure.InvalidResolutionKind, state);
                    var cipTriggers = CreateBattleStartTriggers(state, resolution.RequestId);
                    BattleState afterStart = ClearPending(state,
                        phase: BattlePhase.RoundStart, nextActorSide: BattleUnitSide.Ally,
                        nextActorIndex: 0);
                    if (cipTriggers.Count > 0)
                        afterStart = afterStart.CopyWith(pendingEffectTriggers: cipTriggers,
                            replacePendingEffectTriggers: true);
                    return Success(BattleScheduleEvent.None, afterStart);

                case BattleActionKind.EnemyTurnStart:
                    if (resolution.Kind != BattleActionResolutionKind.BoundaryCompleted)
                        return Fail(BattleScheduleFailure.InvalidResolutionKind, state);
                    if (!TryRestoreTemporaryUnitMoveDefense(state, out BattleUnitState[] restoredEnemies))
                        return Fail(BattleScheduleFailure.ArithmeticOverflow, state);
                    return Success(BattleScheduleEvent.None, ClearPending(state,
                        enemies: restoredEnemies,
                        phase: BattlePhase.EnemyTurn, nextActorSide: BattleUnitSide.Enemy,
                        nextActorIndex: 0));

                case BattleActionKind.Ally:
                    return ResolveAllyAction(state, resolution);

                case BattleActionKind.Enemy:
                    return ResolveEnemyAction(state, resolution);

                default:
                    return Fail(BattleScheduleFailure.InvalidState, state);
            }
        }

        public static int GetParticipationLimit(BattleUnitState ally)
        {
            if (ally == null || ally.Side != BattleUnitSide.Ally)
                throw new ArgumentException("An ally BattleUnitState is required.", nameof(ally));
            return UnitMovePriority.ParticipationLimit(ally.Star);
        }

        public static int GetParticipationCount(BattleState state, string instanceId)
        {
            if (state == null || string.IsNullOrWhiteSpace(instanceId)) return 0;
            return state.ParticipationCounts.TryGetValue(instanceId, out int count) ? count : 0;
        }

        public static bool CanParticipateInUnitMove(BattleState state, string allyInstanceId)
        {
            if (state == null || string.IsNullOrWhiteSpace(allyInstanceId)) return false;
            foreach (BattleUnitState ally in state.Allies)
                if (string.Equals(ally.InstanceId, allyInstanceId, StringComparison.Ordinal))
                    return GetParticipationCount(state, allyInstanceId) < GetParticipationLimit(ally);
            return false;
        }

        private static BattleScheduleResult RequestAlly(BattleState state)
        {
            if (state.NextActorIndex >= state.Allies.Count)
                return Success(BattleScheduleEvent.AllyTurnCompleted,
                    state.CopyWith(phase: BattlePhase.EnemyTurnStart,
                        nextActorSide: BattleUnitSide.Enemy, nextActorIndex: 0));

            BattleUnitState ally = state.Allies[state.NextActorIndex];
            if (ally.StunRounds > 0)
            {
                BattleUnitState[] allies = CopyUnits(state.Allies);
                allies[state.NextActorIndex] = ally.WithScheduledState(stunRounds: ally.StunRounds - 1);
                return Success(BattleScheduleEvent.AllySkippedByStun,
                    state.CopyWith(allies: allies, nextActorIndex: state.NextActorIndex + 1));
            }

            if (GetParticipationCount(state, ally.InstanceId) >= GetParticipationLimit(ally))
                return Success(BattleScheduleEvent.AllySkippedByParticipationLimit,
                    state.CopyWith(nextActorIndex: state.NextActorIndex + 1));

            return Issue(state, BattleActionKind.Ally,
                BattleScheduleEvent.AllyActionRequired, ally.InstanceId);
        }

        private static BattleScheduleResult RequestEnemy(BattleState state)
        {
            if (state.NextActorIndex >= state.Enemies.Count)
                return Success(BattleScheduleEvent.EnemyTurnCompleted,
                    state.CopyWith(phase: BattlePhase.RoundEnd,
                        nextActorSide: BattleUnitSide.Enemy, nextActorIndex: state.Enemies.Count));

            BattleUnitState enemy = state.Enemies[state.NextActorIndex];
            if (enemy.CurrentHP <= 0)
                return Success(BattleScheduleEvent.EnemySkippedDefeated,
                    state.CopyWith(nextActorIndex: state.NextActorIndex + 1));

            if (state.CurrentRound < enemy.NextActionRound)
            {
                BattleUnitState[] enemies = CopyUnits(state.Enemies);
                enemies[state.NextActorIndex] = enemy.WithScheduledState(gap: Math.Max(0, enemy.Gap - 1));
                return Success(BattleScheduleEvent.EnemyWaiting,
                    state.CopyWith(enemies: enemies, nextActorIndex: state.NextActorIndex + 1));
            }

            return Issue(state, BattleActionKind.Enemy,
                BattleScheduleEvent.EnemyActionRequired, enemy.InstanceId);
        }

        private static BattleScheduleResult CompleteRound(BattleState state)
        {
            BattleUnitState[] enemies = CopyUnits(state.Enemies);
            for (int i = 0; i < enemies.Length; i++)
                if (enemies[i].CurrentHP > 0)
                    enemies[i] = enemies[i].WithScheduledState(defense: Math.Max(0, enemies[i].Defense - 1));

            if (state.CurrentRound >= state.MaxRounds)
                return Success(BattleScheduleEvent.RoundLimitReached,
                    state.CopyWith(enemies: enemies, phase: BattlePhase.RoundLimitReached,
                        nextActorSide: BattleUnitSide.Enemy, nextActorIndex: state.Enemies.Count));

            return Success(BattleScheduleEvent.RoundCompleted,
                state.CopyWith(enemies: enemies, currentRound: state.CurrentRound + 1,
                    phase: BattlePhase.RoundStart, nextActorSide: BattleUnitSide.Ally,
                    nextActorIndex: 0));
        }

        private static BattleScheduleResult ResolveAllyAction(BattleState state,
            BattleActionResolution resolution)
        {
            if (state.NextActorSide != BattleUnitSide.Ally || state.NextActorIndex >= state.Allies.Count)
                return Fail(BattleScheduleFailure.ActorMismatch, state);
            BattleUnitState actor = state.Allies[state.NextActorIndex];
            if (!string.Equals(actor.InstanceId, state.PendingActorInstanceId, StringComparison.Ordinal))
                return Fail(BattleScheduleFailure.ActorMismatch, state);

            BattleUnitState[] allies = null;
            string[] usedMoves = ToArray(state.UsedUnitMoveIds);
            var participation = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in state.ParticipationCounts)
                participation.Add(pair.Key, pair.Value);
            if (resolution.Kind == BattleActionResolutionKind.UnitMoveCompleted)
            {
                int moveIndex = FindMove(state, resolution.UnitMoveId);
                if (moveIndex < 0) return Fail(BattleScheduleFailure.InvalidUnitMove, state);
                UnitMoveState move = state.UnitMoves[moveIndex];
                if (Contains(usedMoves, move.MoveId))
                    return Fail(BattleScheduleFailure.UnitMoveAlreadyUsed, state);
                if (!MatchesParticipants(move, resolution.ParticipantInstanceIds) ||
                    !Contains(move.ParticipantInstanceIds, actor.InstanceId))
                    return Fail(BattleScheduleFailure.InvalidUnitMove, state);
                foreach (string participantId in move.ParticipantInstanceIds)
                    if (!CanParticipateInUnitMove(state, participantId))
                        return Fail(BattleScheduleFailure.ParticipantLimitReached, state);

                usedMoves = Append(usedMoves, move.MoveId);
                foreach (string participantId in move.ParticipantInstanceIds)
                    participation[participantId] = GetParticipationCount(state, participantId) + 1;
            }
            else if (resolution.Kind == BattleActionResolutionKind.PassAfterUnitMove)
            {
                if (GetParticipationCount(state, actor.InstanceId) == 0)
                    return Fail(BattleScheduleFailure.InvalidResolutionKind, state);
            }
            else if (resolution.Kind != BattleActionResolutionKind.ActionCompleted ||
                     !resolution.EndsActorTurn)
                return Fail(BattleScheduleFailure.InvalidResolutionKind, state);

            int nextActorIndex = resolution.EndsActorTurn ? state.NextActorIndex + 1 : state.NextActorIndex;
            if (resolution.Kind == BattleActionResolutionKind.PassAfterUnitMove)
                nextActorIndex = state.NextActorIndex + 1;
            return Success(BattleScheduleEvent.None, ClearPending(state, allies: allies,
                nextActorIndex: nextActorIndex, usedUnitMoveIds: usedMoves,
                participationCounts: participation, replaceRoundRecords: true));
        }

        private static BattleScheduleResult ResolveEnemyAction(BattleState state,
            BattleActionResolution resolution)
        {
            if (resolution.Kind != BattleActionResolutionKind.ActionCompleted || !resolution.EndsActorTurn ||
                state.NextActorSide != BattleUnitSide.Enemy || state.NextActorIndex >= state.Enemies.Count)
                return Fail(BattleScheduleFailure.InvalidResolutionKind, state);
            BattleUnitState actor = state.Enemies[state.NextActorIndex];
            if (!string.Equals(actor.InstanceId, state.PendingActorInstanceId, StringComparison.Ordinal) ||
                actor.CurrentHP <= 0 || state.CurrentRound < actor.NextActionRound)
                return Fail(BattleScheduleFailure.ActorMismatch, state);

            long nextActionRound = (long)state.CurrentRound + actor.Cooldown + 1L;
            if (nextActionRound > int.MaxValue)
                return Fail(BattleScheduleFailure.ArithmeticOverflow, state);
            BattleUnitState[] enemies = CopyUnits(state.Enemies);
            enemies[state.NextActorIndex] = actor.WithScheduledState(
                gap: actor.Cooldown, nextActionRound: (int)nextActionRound);
            return Success(BattleScheduleEvent.None, ClearPending(state, enemies: enemies,
                nextActorIndex: state.NextActorIndex + 1));
        }

        private static BattleScheduleResult Issue(BattleState state, BattleActionKind actionKind,
            BattleScheduleEvent scheduleEvent, string actorId)
        {
            if (state.NextActionRequestSequence == int.MaxValue)
                return Fail(BattleScheduleFailure.RequestSequenceExhausted, state);
            int sequence = state.NextActionRequestSequence + 1;
            string requestId = "battle:" + state.EncounterIndex.ToString(CultureInfo.InvariantCulture) +
                ":round:" + state.CurrentRound.ToString(CultureInfo.InvariantCulture) +
                ":request:" + sequence.ToString(CultureInfo.InvariantCulture);
            BattleState next = state.CopyWith(pendingActionRequestId: requestId,
                pendingActionKind: actionKind, pendingActorInstanceId: actorId,
                nextActionRequestSequence: sequence, replacePendingAction: true);
            return ActionRequired(scheduleEvent, next, actorId);
        }

        private static BattleScheduleResult PendingRequest(BattleState state)
        {
            BattleScheduleEvent scheduleEvent;
            switch (state.PendingActionKind)
            {
                case BattleActionKind.BattleStart: scheduleEvent = BattleScheduleEvent.BattleStartRequired; break;
                case BattleActionKind.Ally: scheduleEvent = BattleScheduleEvent.AllyActionRequired; break;
                case BattleActionKind.EnemyTurnStart: scheduleEvent = BattleScheduleEvent.EnemyTurnStartRequired; break;
                case BattleActionKind.Enemy: scheduleEvent = BattleScheduleEvent.EnemyActionRequired; break;
                default: return Fail(BattleScheduleFailure.InvalidState, state);
            }
            return ActionRequired(scheduleEvent, state, state.PendingActorInstanceId);
        }

        private static BattleScheduleResult ActionRequired(BattleScheduleEvent scheduleEvent,
            BattleState state, string actorId) => new BattleScheduleResult(true,
                BattleScheduleFailure.None, scheduleEvent, state, true,
                state.PendingActionKind, state.PendingActionRequestId, actorId);

        private static BattleState ClearPending(BattleState state,
            BattleUnitState[] allies = null, BattleUnitState[] enemies = null,
            BattlePhase? phase = null, BattleUnitSide? nextActorSide = null,
            int? nextActorIndex = null, IEnumerable<string> usedUnitMoveIds = null,
            IReadOnlyDictionary<string, int> participationCounts = null,
            bool replaceRoundRecords = false) =>
            state.CopyWith(allies: allies, enemies: enemies, phase: phase,
                nextActorSide: nextActorSide, nextActorIndex: nextActorIndex,
                usedUnitMoveIds: usedUnitMoveIds, participationCounts: participationCounts,
                replaceRoundRecords: replaceRoundRecords,
                pendingActionRequestId: null, pendingActionKind: BattleActionKind.None,
                pendingActorInstanceId: null, replacePendingAction: true);

        private static bool MatchesParticipants(UnitMoveState move, IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count != move.ParticipantInstanceIds.Count) return false;
            foreach (string expected in move.ParticipantInstanceIds)
                if (!Contains(ids, expected)) return false;
            return true;
        }

        private static int FindMove(BattleState state, string moveId)
        {
            for (int i = 0; i < state.UnitMoves.Count; i++)
                if (string.Equals(state.UnitMoves[i].MoveId, moveId, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static BattleUnitState[] CopyUnits(IReadOnlyList<BattleUnitState> units)
        {
            var copy = new BattleUnitState[units.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = units[i];
            return copy;
        }

        private static bool TryRestoreTemporaryUnitMoveDefense(BattleState state,
            out BattleUnitState[] enemies)
        {
            enemies = CopyUnits(state.Enemies);
            for (int i = 0; i < enemies.Length; i++)
            {
                BattleUnitState enemy = enemies[i];
                if (enemy.TemporaryDefenseReduction <= 0) continue;
                long restoredDefense = (long)enemy.Defense + enemy.TemporaryDefenseReduction;
                if (restoredDefense > int.MaxValue) return false;
                enemies[i] = enemy.WithScheduledState(defense: (int)restoredDefense,
                    temporaryDefenseReduction: 0);
            }
            return true;
        }

        private static List<BattlePendingEffectTrigger> CreateBattleStartTriggers(BattleState state,
            string requestId)
        {
            var triggers = new List<BattlePendingEffectTrigger>();
            foreach (BattleUnitState enemy in state.Enemies)
            {
                if (enemy.CurrentHP <= 0 || enemy.CipEffectIds.Count == 0) continue;
                string triggerId = "battle:" + state.EncounterIndex.ToString(CultureInfo.InvariantCulture) +
                    ":cip:" + enemy.InstanceId;
                triggers.Add(new BattlePendingEffectTrigger(triggerId, requestId,
                    BattleEffectTriggerKind.EnemyBattleStart, enemy.InstanceId, enemy.InstanceId,
                    enemy.CipEffectIds, Array.Empty<string>()));
            }
            return triggers;
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var copy = new string[values.Count];
            for (int i = 0; i < values.Count; i++) copy[i] = values[i];
            return copy;
        }

        private static string[] Append(string[] values, string value)
        {
            var copy = new string[values.Length + 1];
            Array.Copy(values, copy, values.Length);
            copy[values.Length] = value;
            return copy;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        private static BattleScheduleResult Success(BattleScheduleEvent scheduleEvent, BattleState state) =>
            new BattleScheduleResult(true, BattleScheduleFailure.None, scheduleEvent, state);

        private static BattleScheduleResult Fail(BattleScheduleFailure failure, BattleState state) =>
            new BattleScheduleResult(false, failure, scheduleEvent: BattleScheduleEvent.None, nextState: state);
    }
}
