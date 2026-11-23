using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    /// <summary>Resolves one whole pending enemy-effect trigger atomically; it never consumes RNG.</summary>
    public static class BattleEffectRules
    {
        public static BattleEffectResolutionResult ResolveNextPendingTrigger(BattleState state,
            EconomyState economy, string triggerId,
            IReadOnlyDictionary<string, BattleEffectDefinition> definitions)
        {
            if (state == null || economy == null || definitions == null)
                return Fail(BattleEffectFailure.InvalidState, triggerId, state, economy);
            if (state.Status == BattleExecutionStatus.Completed || state.Phase == BattlePhase.Completed)
                return Fail(BattleEffectFailure.BattleAlreadyCompleted, triggerId, state, economy);
            if (Contains(state.ProcessedEffectTriggerIds, triggerId))
                return Fail(BattleEffectFailure.AlreadyProcessed, triggerId, state, economy);
            if (string.IsNullOrWhiteSpace(triggerId))
                return Fail(BattleEffectFailure.TriggerMismatch, triggerId, state, economy);
            if (state.PendingEffectTriggers.Count == 0)
                return Fail(BattleEffectFailure.NoPendingEffect, triggerId, state, economy);

            BattlePendingEffectTrigger trigger = state.PendingEffectTriggers[0];
            if (!string.Equals(trigger.TriggerId, triggerId, StringComparison.Ordinal))
                return Fail(BattleEffectFailure.TriggerMismatch, triggerId, state, economy);
            if (Find(state.Enemies, trigger.SourceInstanceId) == null)
                return Fail(BattleEffectFailure.InvalidTriggerTarget, triggerId, state, economy);
            if (economy.HasHealth && (economy.MaximumHP != state.PlayerMaximumHP ||
                state.PlayerCurrentHP > economy.MaximumHP))
                return Fail(BattleEffectFailure.EconomyHealthUnavailable, triggerId, state, economy);

            BattleUnitState[] allies = Copy(state.Allies);
            BattleUnitState[] enemies = Copy(state.Enemies);
            EconomyState nextEconomy = SyncHealth(economy, state.PlayerCurrentHP);
            int currentHp = state.PlayerCurrentHP;
            int pendingHpDelta = state.PendingPlayerHpDelta;
            int block = state.PlayerBlock;
            int pendingBlockBonus = state.PendingPlayerBlockBonus;
            int permanentBlockBonus = state.PermanentPlayerBlockBonus;
            bool outcomeCheckPending = state.BattleOutcomeCheckPending;
            var appliedIds = new List<string>(trigger.EffectIds.Count);
            var persistentCommands = new List<BattlePersistentEffectCommand>();

            foreach (string effectId in trigger.EffectIds)
            {
                if (!definitions.TryGetValue(effectId, out BattleEffectDefinition definition) ||
                    definition == null || !string.Equals(definition.EffectId, effectId, StringComparison.Ordinal))
                    return Fail(BattleEffectFailure.MissingDefinition, triggerId, state, economy);
                if (!IsValidForTrigger(trigger, definition))
                    return Fail(BattleEffectFailure.InvalidDefinition, triggerId, state, economy);

                BattleEffectFailure failure = ApplyEffect(trigger, definition, allies, enemies,
                    state.PlayerMaximumHP, ref nextEconomy, ref currentHp, ref pendingHpDelta,
                    ref block, ref pendingBlockBonus,
                    ref permanentBlockBonus, ref outcomeCheckPending, persistentCommands);
                if (failure != BattleEffectFailure.None)
                    return Fail(failure, triggerId, state, economy);
                appliedIds.Add(effectId);
            }

            var remainingTriggers = new BattlePendingEffectTrigger[state.PendingEffectTriggers.Count - 1];
            for (int i = 1; i < state.PendingEffectTriggers.Count; i++)
                remainingTriggers[i - 1] = state.PendingEffectTriggers[i];
            var processedIds = new List<string>(state.ProcessedEffectTriggerIds.Count + 1);
            for (int i = 0; i < state.ProcessedEffectTriggerIds.Count; i++)
                processedIds.Add(state.ProcessedEffectTriggerIds[i]);
            processedIds.Add(trigger.TriggerId);

            var persistentLedger = new List<BattlePersistentEffectCommand>(
                state.PersistentEffectCommands.Count + persistentCommands.Count);
            for (int i = 0; i < state.PersistentEffectCommands.Count; i++)
                persistentLedger.Add(state.PersistentEffectCommands[i]);
            persistentLedger.AddRange(persistentCommands);

            BattleState nextState = state.CopyWith(allies: allies, enemies: enemies,
                playerCurrentHP: currentHp, playerBlock: block,
                pendingPlayerBlockBonus: pendingBlockBonus,
                permanentPlayerBlockBonus: permanentBlockBonus,
                pendingPlayerHpDelta: pendingHpDelta,
                battleOutcomeCheckPending: outcomeCheckPending,
                pendingEffectTriggers: remainingTriggers, replacePendingEffectTriggers: true,
                processedEffectTriggerIds: processedIds, replaceProcessedEffectTriggerIds: true,
                persistentEffectCommands: persistentLedger, replacePersistentEffectCommands: true);
            nextEconomy = SyncHealth(nextEconomy, currentHp);
            return new BattleEffectResolutionResult(true, BattleEffectFailure.None,
                trigger.TriggerId, appliedIds, persistentCommands, nextState, nextEconomy,
                state.BattleRandomState);
        }

        private static BattleEffectFailure ApplyEffect(BattlePendingEffectTrigger trigger,
            BattleEffectDefinition definition, BattleUnitState[] allies, BattleUnitState[] enemies,
            int playerMaximumHp, ref EconomyState economy, ref int playerHp,
            ref int pendingHpDelta, ref int block, ref int pendingBlockBonus,
            ref int permanentBlockBonus, ref bool outcomeCheckPending,
            List<BattlePersistentEffectCommand> persistentCommands)
        {
            if (definition.Target == BattleEffectTarget.Player)
                return ApplyPlayerEffect(definition, playerMaximumHp, ref economy, ref playerHp,
                    ref pendingHpDelta, ref block, ref pendingBlockBonus,
                    ref permanentBlockBonus, ref outcomeCheckPending);

            if (definition.Target == BattleEffectTarget.AttackTargets &&
                trigger.Kind == BattleEffectTriggerKind.EnemyBattleStart)
                return BattleEffectFailure.InvalidTriggerTarget;

            if (definition.Target == BattleEffectTarget.TriggerActor)
            {
                int allyIndex = IndexOf(allies, trigger.ActorInstanceId);
                if (allyIndex >= 0)
                    return ApplyUnitEffect(trigger, definition, allies, allyIndex, false, persistentCommands);
                int enemyIndex = IndexOf(enemies, trigger.ActorInstanceId);
                if (enemyIndex >= 0)
                    return ApplyUnitEffect(trigger, definition, enemies, enemyIndex, true, persistentCommands);
                return BattleEffectFailure.TargetNotFound;
            }

            // AttackTargets are captured by the attack/PIG trigger. Empty captured target sets
            // are valid and deliberately perform no effect, matching BattleManager's empty list.
            foreach (string targetId in trigger.TargetInstanceIds)
            {
                int allyIndex = IndexOf(allies, targetId);
                if (allyIndex >= 0)
                {
                    BattleEffectFailure failure = ApplyUnitEffect(trigger, definition, allies,
                        allyIndex, false, persistentCommands);
                    if (failure != BattleEffectFailure.None) return failure;
                    continue;
                }
                int enemyIndex = IndexOf(enemies, targetId);
                if (enemyIndex >= 0)
                {
                    BattleEffectFailure failure = ApplyUnitEffect(trigger, definition, enemies,
                        enemyIndex, true, persistentCommands);
                    if (failure != BattleEffectFailure.None) return failure;
                    continue;
                }
                return BattleEffectFailure.TargetNotFound;
            }
            return BattleEffectFailure.None;
        }

        private static BattleEffectFailure ApplyPlayerEffect(BattleEffectDefinition definition,
            int playerMaximumHp,
            ref EconomyState economy, ref int playerHp, ref int pendingHpDelta,
            ref int block, ref int pendingBlockBonus, ref int permanentBlockBonus,
            ref bool outcomeCheckPending)
        {
            switch (definition.Effect)
            {
                case BattleEffectType.Gold:
                {
                    long gold = (long)economy.Gold + definition.Value;
                    if (gold > int.MaxValue) return BattleEffectFailure.ArithmeticOverflow;
                    economy = economy.WithGold((int)Math.Max(0L, gold));
                    return BattleEffectFailure.None;
                }
                case BattleEffectType.Damage:
                {
                    if (!economy.HasHealth || playerMaximumHp < playerHp || economy.MaximumHP < playerHp)
                        return BattleEffectFailure.EconomyHealthUnavailable;
                    int nextHp = ClampToIntRange((long)playerHp - definition.Value, 0, playerMaximumHp);
                    int delta = nextHp - playerHp;
                    if (definition.Duration == BattleEffectDuration.Battle)
                    {
                        long pending = (long)pendingHpDelta + delta;
                        if (pending < int.MinValue || pending > int.MaxValue)
                            return BattleEffectFailure.ArithmeticOverflow;
                        pendingHpDelta = (int)pending;
                    }
                    playerHp = nextHp;
                    economy = economy.WithHealth(nextHp);
                    if (playerHp == 0) outcomeCheckPending = true;
                    return BattleEffectFailure.None;
                }
                case BattleEffectType.Block:
                {
                    long next = (long)block + definition.Value;
                    if (next > int.MaxValue) return BattleEffectFailure.ArithmeticOverflow;
                    if (definition.Duration == BattleEffectDuration.Immediate)
                    {
                        long permanent = (long)permanentBlockBonus + definition.Value;
                        if (permanent < int.MinValue || permanent > int.MaxValue)
                            return BattleEffectFailure.ArithmeticOverflow;
                        permanentBlockBonus = (int)permanent;
                    }
                    else
                    {
                        long pending = (long)pendingBlockBonus + definition.Value;
                        if (pending < int.MinValue || pending > int.MaxValue)
                            return BattleEffectFailure.ArithmeticOverflow;
                        pendingBlockBonus = (int)pending;
                    }
                    block = (int)Math.Max(0L, next);
                    return BattleEffectFailure.None;
                }
                default:
                    return BattleEffectFailure.InvalidDefinition;
            }
        }

        private static BattleEffectFailure ApplyUnitEffect(BattlePendingEffectTrigger trigger,
            BattleEffectDefinition definition, BattleUnitState[] units, int index,
            bool isEnemy, List<BattlePersistentEffectCommand> persistentCommands)
        {
            BattleUnitState target = units[index];
            int? attack = null, defense = null, gap = null, nextActionRound = null, stunRounds = null;
            int? temporaryAttack = null, temporaryDefense = null;
            switch (definition.Effect)
            {
                case BattleEffectType.Attack:
                    if (!TryAdd(target.Attack, definition.Value, out int newAttack)) return BattleEffectFailure.ArithmeticOverflow;
                    attack = newAttack;
                    if (definition.Duration == BattleEffectDuration.Battle && !isEnemy)
                    {
                        if (!TryAdd(target.TemporaryAttackBonus, definition.Value, out int nextTemporaryAttack))
                            return BattleEffectFailure.ArithmeticOverflow;
                        temporaryAttack = nextTemporaryAttack;
                    }
                    break;
                case BattleEffectType.Defense:
                    if (!TryAdd(target.Defense, definition.Value, out int newDefense)) return BattleEffectFailure.ArithmeticOverflow;
                    defense = newDefense;
                    if (definition.Duration == BattleEffectDuration.Battle && !isEnemy)
                    {
                        if (!TryAdd(target.TemporaryDefenseBonus, definition.Value, out int nextTemporaryDefense))
                            return BattleEffectFailure.ArithmeticOverflow;
                        temporaryDefense = nextTemporaryDefense;
                    }
                    break;
                case BattleEffectType.Gap:
                    if (isEnemy)
                    {
                        if (!TryAdd(target.Gap, definition.Value, out int rawGap)) return BattleEffectFailure.ArithmeticOverflow;
                        gap = Math.Max(0, rawGap);
                        long nextRound = (long)target.NextActionRound + Math.Max(0, definition.Value);
                        if (nextRound > int.MaxValue) return BattleEffectFailure.ArithmeticOverflow;
                        nextActionRound = (int)nextRound;
                    }
                    else if (definition.Duration == BattleEffectDuration.Battle)
                    {
                        long nextStun = (long)target.StunRounds + Math.Max(0, definition.Value);
                        if (nextStun > int.MaxValue) return BattleEffectFailure.ArithmeticOverflow;
                        stunRounds = (int)nextStun;
                    }
                    // Existing BattleManager logs and leaves an Immediate Gap effect on a player
                    // character unapplied. Keep that behavior rather than inventing a rule.
                    break;
                default:
                    return BattleEffectFailure.InvalidDefinition;
            }

            units[index] = target.WithEffectState(attack, defense, gap, nextActionRound,
                stunRounds, temporaryAttackBonus: temporaryAttack,
                temporaryDefenseBonus: temporaryDefense);
            bool immediatePersistentUnitChange = definition.Duration == BattleEffectDuration.Immediate &&
                (definition.Effect != BattleEffectType.Gap || isEnemy);
            if (immediatePersistentUnitChange)
                persistentCommands.Add(new BattlePersistentEffectCommand(trigger.TriggerId, definition.EffectId,
                    target.InstanceId, definition.Effect, definition.Value));
            return BattleEffectFailure.None;
        }

        private static bool IsValidForTrigger(BattlePendingEffectTrigger trigger,
            BattleEffectDefinition definition)
        {
            if (trigger.Kind == BattleEffectTriggerKind.EnemyBattleStart &&
                definition.Target == BattleEffectTarget.AttackTargets) return false;
            return true;
        }

        private static EconomyState SyncHealth(EconomyState economy, int hp) =>
            economy != null && economy.HasHealth && hp <= economy.MaximumHP && hp >= 0
                ? economy.WithHealth(hp) : economy;

        private static int ClampToIntRange(long value, int min, int max) =>
            (int)Math.Max(min, Math.Min((long)max, value));

        private static bool TryAdd(int left, int right, out int result)
        {
            long sum = (long)left + right;
            result = sum < int.MinValue || sum > int.MaxValue ? 0 : (int)sum;
            return sum >= int.MinValue && sum <= int.MaxValue;
        }

        private static int IndexOf(IReadOnlyList<BattleUnitState> units, string id)
        { for (int i = 0; i < units.Count; i++) if (string.Equals(units[i].InstanceId, id, StringComparison.Ordinal)) return i; return -1; }
        private static BattleUnitState Find(IReadOnlyList<BattleUnitState> units, string id)
        { int index = IndexOf(units, id); return index < 0 ? null : units[index]; }
        private static bool Contains(IReadOnlyList<string> values, string id)
        { if (string.IsNullOrWhiteSpace(id)) return false; for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], id, StringComparison.Ordinal)) return true; return false; }
        private static BattleUnitState[] Copy(IReadOnlyList<BattleUnitState> units)
        { var copy = new BattleUnitState[units.Count]; for (int i = 0; i < copy.Length; i++) copy[i] = units[i]; return copy; }

        private static BattleEffectResolutionResult Fail(BattleEffectFailure failure, string triggerId,
            BattleState state, EconomyState economy) => new BattleEffectResolutionResult(false,
                failure, triggerId, null, null, state, economy,
                state == null ? default : state.BattleRandomState);
    }
}
