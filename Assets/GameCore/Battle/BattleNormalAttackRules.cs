using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public static class BattleNormalAttackRules
    {
        public static BattleAttackResult ResolveAllyNormalAttack(BattleState state, string requestId)
        {
            if (state == null) return Fail(BattleAttackFailure.InvalidState, BattleAttackKind.AllyNormal, null);
            BattleAttackFailure validation = ValidatePending(state, requestId, BattleActionKind.Ally);
            if (validation != BattleAttackFailure.None) return Fail(validation, BattleAttackKind.AllyNormal, state);
            if (state.PendingEffectTriggers.Count > 0)
                return Fail(BattleAttackFailure.PostActionEffectsPending, BattleAttackKind.AllyNormal, state);
            if (state.NextActorIndex >= state.Allies.Count) return Fail(BattleAttackFailure.ActorUnavailable, BattleAttackKind.AllyNormal, state);

            BattleUnitState actor = state.Allies[state.NextActorIndex];
            if (actor.StunRounds > 0) return Fail(BattleAttackFailure.ActorStunned, BattleAttackKind.AllyNormal, state);
            if (BattleTurnRules.GetParticipationCount(state, actor.InstanceId) > 0)
                return Fail(BattleAttackFailure.NormalAttackSkippedAfterUnitMove, BattleAttackKind.AllyNormal, state);
            if (BattleUnitMoveRules.GetAvailableMoves(state, actor.InstanceId).Count > 0)
                return Fail(BattleAttackFailure.UnitMoveAvailable, BattleAttackKind.AllyNormal, state);
            var candidates = new List<BattleUnitState>();
            foreach (BattleUnitState enemy in state.Enemies)
                if (enemy.CurrentHP > 0) candidates.Add(enemy);
            if (candidates.Count == 0) return Fail(BattleAttackFailure.NoLivingEnemies, BattleAttackKind.AllyNormal, state);

            Pcg32RandomSource random;
            try { random = Pcg32RandomSource.FromState(state.BattleRandomState); }
            catch (ArgumentException) { return Fail(BattleAttackFailure.InvalidState, BattleAttackKind.AllyNormal, state); }
            int selectedIndex;
            try { selectedIndex = random.NextInt(0, candidates.Count); }
            catch (ArgumentException) { return Fail(BattleAttackFailure.InvalidState, BattleAttackKind.AllyNormal, state); }
            BattleUnitState target = candidates[selectedIndex];
            BattleDamageResult damage = BattleDamageRules.Calculate(actor.Attack, target.Defense);
            if (!damage.Succeeded) return Fail(BattleAttackFailure.ArithmeticOverflow, BattleAttackKind.AllyNormal, state);

            int enemyHpLost = Math.Min(target.CurrentHP, damage.Damage);
            int nextHp = Math.Max(0, target.CurrentHP - damage.Damage);
            string defeatedId = nextHp == 0 ? target.InstanceId : null;
            BattleUnitState[] enemies = Copy(state.Enemies);
            int targetIndex = IndexOf(enemies, target.InstanceId);
            enemies[targetIndex] = target.WithCombatState(currentHP: nextHp);
            BattleState combatState = state.CopyWith(enemies: enemies,
                battleRandomState: random.CaptureState(),
                battleOutcomeCheckPending: state.BattleOutcomeCheckPending ||
                    (defeatedId != null && AreAllEnemiesDefeated(enemies)));
            BattleScheduleResult scheduled = BattleTurnRules.ResolveAction(combatState,
                BattleActionResolution.ActionCompleted(requestId));
            if (!scheduled.Succeeded) return Fail(BattleAttackFailure.SchedulerRejected, BattleAttackKind.AllyNormal, state);

            var triggers = new List<BattlePendingEffectTrigger>();
            if (defeatedId != null && target.PigEffectIds.Count > 0)
                triggers.Add(new BattlePendingEffectTrigger(requestId + ":PIG", requestId,
                    BattleEffectTriggerKind.EnemyDefeated, target.InstanceId, actor.InstanceId,
                    target.PigEffectIds, new[] { target.InstanceId }));
            BattleState next = WithPendingEffects(scheduled.NextState, triggers);
            return Success(BattleAttackKind.AllyNormal, next, requestId, actor.InstanceId,
                null, new[] { target.InstanceId }, actor.Attack, target.Defense, damage.Damage,
                enemyHpLost, 0, 0, defeatedId, triggers);
        }

        public static BattleAttackResult ResolveEnemyNormalAttack(BattleState state, string requestId,
            IReadOnlyDictionary<string, BattleTargetDefinition> targetDefinitions,
            int allyColumns = 3, int enemyColumns = 2, int boardSlotCount = 9)
        {
            if (state == null) return Fail(BattleAttackFailure.InvalidState, BattleAttackKind.EnemyNormal, null);
            BattleAttackFailure validation = ValidatePending(state, requestId, BattleActionKind.Enemy);
            if (validation != BattleAttackFailure.None) return Fail(validation, BattleAttackKind.EnemyNormal, state);
            if (state.PendingEffectTriggers.Count > 0) return Fail(BattleAttackFailure.PostActionEffectsPending, BattleAttackKind.EnemyNormal, state);
            if (targetDefinitions == null || state.NextActorIndex >= state.Enemies.Count)
                return Fail(BattleAttackFailure.InvalidState, BattleAttackKind.EnemyNormal, state);

            BattleUnitState actor = state.Enemies[state.NextActorIndex];
            if (actor.CurrentHP <= 0) return Fail(BattleAttackFailure.ActorUnavailable, BattleAttackKind.EnemyNormal, state);
            if (state.CurrentRound < actor.NextActionRound) return Fail(BattleAttackFailure.ActorUnavailable, BattleAttackKind.EnemyNormal, state);

            string[] targetIds = actor.TargetIds.Count == 0 ? new[] { "T001" } : ToArray(actor.TargetIds);
            int targetIndex = actor.NextTargetIndex % targetIds.Length;
            string targetId = targetIds[targetIndex];
            if (!targetDefinitions.TryGetValue(targetId, out BattleTargetDefinition targetDefinition) ||
                targetDefinition == null || !string.Equals(targetDefinition.TargetId, targetId, StringComparison.Ordinal))
                return Fail(BattleAttackFailure.InvalidTargetDefinition, BattleAttackKind.EnemyNormal, state);

            BattleTargetSelectionResult selected = BattleTargetRules.SelectTargets(targetDefinition,
                state.Allies, actor, state.BattleRandomState, allyColumns, enemyColumns, boardSlotCount);
            if (!selected.Succeeded) return Fail(BattleAttackFailure.TargetSelectionFailed, BattleAttackKind.EnemyNormal, state);

            long totalDefense = 0;
            foreach (string id in selected.TargetInstanceIds)
            {
                BattleUnitState ally = Find(state.Allies, id);
                if (ally == null) return Fail(BattleAttackFailure.InvalidState, BattleAttackKind.EnemyNormal, state);
                totalDefense = checked(totalDefense + ally.Defense);
            }
            BattleDamageResult damage = BattleDamageRules.Calculate(actor.Attack, totalDefense);
            if (!damage.Succeeded) return Fail(BattleAttackFailure.ArithmeticOverflow, BattleAttackKind.EnemyNormal, state);

            int absorbed = Math.Min(state.PlayerBlock, damage.Damage);
            int hpDamage = damage.Damage - absorbed;
            int playerHpLost = Math.Min(state.PlayerCurrentHP, hpDamage);
            int nextPlayerHp = Math.Max(0, state.PlayerCurrentHP - hpDamage);
            int nextBlock = Math.Max(0, state.PlayerBlock - absorbed);
            if (long.MaxValue - state.TotalBlockAbsorbed < absorbed)
                return Fail(BattleAttackFailure.ArithmeticOverflow, BattleAttackKind.EnemyNormal, state);
            long totalBlockAbsorbed = state.TotalBlockAbsorbed + absorbed;
            int nextTargetIndex = (targetIndex + 1) % targetIds.Length;
            BattleUnitState[] enemies = Copy(state.Enemies);
            int actorIndex = IndexOf(enemies, actor.InstanceId);
            enemies[actorIndex] = actor.WithCombatState(nextTargetIndex: nextTargetIndex);
            BattleState combatState = state.CopyWith(enemies: enemies,
                playerCurrentHP: nextPlayerHp, playerBlock: nextBlock,
                totalBlockAbsorbed: totalBlockAbsorbed,
                battleOutcomeCheckPending: state.BattleOutcomeCheckPending || nextPlayerHp == 0,
                battleRandomState: selected.NextBattleRandomState);
            BattleScheduleResult scheduled = BattleTurnRules.ResolveAction(combatState,
                BattleActionResolution.ActionCompleted(requestId));
            if (!scheduled.Succeeded) return Fail(BattleAttackFailure.SchedulerRejected, BattleAttackKind.EnemyNormal, state);

            var triggers = new List<BattlePendingEffectTrigger>();
            if (actor.AeEffectIds.Count > 0)
                triggers.Add(new BattlePendingEffectTrigger(requestId + ":AE", requestId,
                    BattleEffectTriggerKind.EnemyAttackAftermath, actor.InstanceId, actor.InstanceId,
                    actor.AeEffectIds, selected.TargetInstanceIds));
            BattleState next = WithPendingEffects(scheduled.NextState, triggers);
            return Success(BattleAttackKind.EnemyNormal, next, requestId, actor.InstanceId,
                targetId, selected.TargetInstanceIds, actor.Attack, totalDefense, damage.Damage,
                0, absorbed, playerHpLost, null, triggers);
        }

        private static BattleAttackFailure ValidatePending(BattleState state, string requestId, BattleActionKind expected)
        {
            if (state.PendingActionKind == BattleActionKind.None) return BattleAttackFailure.NoPendingAction;
            if (!string.Equals(state.PendingActionRequestId, requestId, StringComparison.Ordinal)) return BattleAttackFailure.StaleActionRequest;
            if (state.PendingActionKind != expected) return BattleAttackFailure.WrongActionKind;
            if (state.PendingActorInstanceId == null) return BattleAttackFailure.ActorMismatch;
            return BattleAttackFailure.None;
        }

        private static BattleState WithPendingEffects(BattleState state, List<BattlePendingEffectTrigger> triggers) =>
            triggers.Count == 0 ? state : state.CopyWith(pendingEffectTriggers: triggers, replacePendingEffectTriggers: true);

        private static BattleUnitState Find(IReadOnlyList<BattleUnitState> units, string id)
        {
            foreach (BattleUnitState unit in units)
                if (string.Equals(unit.InstanceId, id, StringComparison.Ordinal)) return unit;
            return null;
        }

        private static int IndexOf(IReadOnlyList<BattleUnitState> units, string id)
        {
            for (int i = 0; i < units.Count; i++)
                if (string.Equals(units[i].InstanceId, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static BattleUnitState[] Copy(IReadOnlyList<BattleUnitState> units)
        {
            var copy = new BattleUnitState[units.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = units[i];
            return copy;
        }

        private static bool AreAllEnemiesDefeated(IReadOnlyList<BattleUnitState> enemies)
        {
            if (enemies.Count == 0) return false;
            foreach (BattleUnitState enemy in enemies)
                if (enemy.CurrentHP > 0) return false;
            return true;
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var copy = new string[values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return copy;
        }

        private static BattleAttackResult Success(BattleAttackKind kind, BattleState next,
            string requestId, string actorId, string selectedTargetId,
            IEnumerable<string> targetIds, int attack,
            long defense, int damage, int enemyHpLost, int absorbed, int playerHpLost,
            string defeatedId, IEnumerable<BattlePendingEffectTrigger> triggers) =>
            new BattleAttackResult(true, BattleAttackFailure.None, kind, next,
                next.BattleRandomState, requestId, actorId, selectedTargetId, targetIds, attack, defense,
                damage, enemyHpLost, absorbed, playerHpLost, defeatedId, triggers);

        private static BattleAttackResult Fail(BattleAttackFailure failure, BattleAttackKind kind, BattleState state) =>
            new BattleAttackResult(false, failure, kind, state,
                state == null ? default : state.BattleRandomState);
    }
}
