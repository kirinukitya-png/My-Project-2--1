using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;

namespace ProjectNST.GameCore.Battle
{
    /// <summary>Determines a battle result and atomically clears battle-only state.</summary>
    public static class BattleFinishRules
    {
        public static BattleFinishResult TryFinish(BattleState state, EconomyState economy,
            BoardState board, HandState hand, CharacterOwnershipState ownership,
            ItemEffectState itemEffects)
        {
            if (state == null || economy == null || board == null || hand == null ||
                ownership == null || itemEffects == null)
                return Fail(BattleFinishFailure.InvalidInput, state, economy, board, hand, ownership, itemEffects);
            if (state.Status == BattleExecutionStatus.Completed || state.Phase == BattlePhase.Completed ||
                state.Outcome != BattleOutcome.Pending)
                return Fail(BattleFinishFailure.AlreadyFinished, state, economy, board, hand, ownership, itemEffects);
            if (state.PendingActionKind != BattleActionKind.None)
                return Fail(BattleFinishFailure.PendingAction, state, economy, board, hand, ownership, itemEffects);
            if (state.PendingEffectTriggers.Count > 0)
                return Fail(BattleFinishFailure.PendingEffects, state, economy, board, hand, ownership, itemEffects);
            if (!economy.HasHealth || economy.MaximumHP != state.PlayerMaximumHP ||
                !BoardHandStateValidator.Validate(board, hand, ownership).IsValid)
                return Fail(BattleFinishFailure.InvalidState, state, economy, board, hand, ownership, itemEffects);

            bool initialBoundary = state.Phase == BattlePhase.BattleStart;
            bool roundLimit = state.Phase == BattlePhase.RoundLimitReached;
            if (!initialBoundary && !roundLimit && !state.BattleOutcomeCheckPending)
                return Fail(BattleFinishFailure.OutcomeCheckNotRequested, state, economy, board, hand, ownership, itemEffects);

            int livingEnemies = CountLivingEnemies(state);
            int defeatedEnemies = state.Enemies.Count - livingEnemies;
            BattleOutcome outcome;
            BattleFinishReason reason;
            // Unity checks player HP before the empty-enemy victory branch on battle start.
            if (state.PlayerCurrentHP <= 0)
            {
                outcome = BattleOutcome.Defeat;
                reason = BattleFinishReason.PlayerHealthDepleted;
            }
            else if (livingEnemies == 0)
            {
                outcome = BattleOutcome.Victory;
                reason = BattleFinishReason.EnemiesDefeated;
            }
            else if (roundLimit)
            {
                outcome = BattleOutcome.Unresolved;
                reason = BattleFinishReason.MaximumRoundsReached;
            }
            else
                return Fail(BattleFinishFailure.OutcomeNotReached, state, economy, board, hand, ownership, itemEffects);

            // Synchronize combat HP first, then restore pre-battle Item HP changes and battle-duration
            // HP deltas. Outcome is intentionally based on the HP before this cleanup, as in FinishBattle.
            EconomyState alignedEconomy;
            try { alignedEconomy = economy.WithHealth(state.PlayerCurrentHP); }
            catch (ArgumentException)
            { return Fail(BattleFinishFailure.InvalidState, state, economy, board, hand, ownership, itemEffects); }

            CharacterOwnershipState nextOwnership = ownership;
            ItemEffectState restoredEffects = itemEffects;
            ItemEffectResult restoration = null;
            if (itemEffects.HasActiveBattleEffects)
            {
                restoration = ItemEffectRules.RestoreBattleEffects(board, hand, ownership,
                    alignedEconomy, itemEffects, state.BattleRandomState);
                if (!restoration.Succeeded)
                    return Fail(BattleFinishFailure.ItemEffectCleanupFailed,
                        state, economy, board, hand, ownership, itemEffects);
                nextOwnership = restoration.NextOwnership;
                alignedEconomy = restoration.NextEconomy;
                restoredEffects = restoration.NextEffectState;
            }

            long restoredBattleHp = (long)alignedEconomy.CurrentHP - state.PendingPlayerHpDelta;
            if (restoredBattleHp < 0) restoredBattleHp = 0;
            if (restoredBattleHp > alignedEconomy.MaximumHP) restoredBattleHp = alignedEconomy.MaximumHP;
            alignedEconomy = alignedEconomy.WithHealth((int)restoredBattleHp);
            restoredEffects = restoredEffects.AfterBattleAndResetPermanentBlock();

            if (!TryCleanupUnits(state.Allies, out BattleUnitState[] allies) ||
                !TryCleanupUnits(state.Enemies, out BattleUnitState[] enemies))
                return Fail(BattleFinishFailure.CleanupOverflow, state, economy, board, hand, ownership, itemEffects);

            BattleState finished;
            try
            {
                finished = state.CopyWith(allies: allies, enemies: enemies,
                    phase: BattlePhase.Completed, status: BattleExecutionStatus.Completed,
                    outcome: outcome, playerCurrentHP: (int)restoredBattleHp, playerBlock: 0,
                    pendingPlayerBlockBonus: 0, permanentPlayerBlockBonus: 0,
                    pendingPlayerHpDelta: 0, battleOutcomeCheckPending: false,
                    nextActorIndex: 0, nextActorSide: BattleUnitSide.Ally,
                    usedUnitMoveIds: Array.Empty<string>(), participationCounts: null,
                    replaceRoundRecords: true,
                    pendingActionRequestId: null, pendingActionKind: BattleActionKind.None,
                    pendingActorInstanceId: null, replacePendingAction: true,
                    pendingEffectTriggers: Array.Empty<BattlePendingEffectTrigger>(),
                    replacePendingEffectTriggers: true);
            }
            catch (ArgumentException)
            { return Fail(BattleFinishFailure.InvalidState, state, economy, board, hand, ownership, itemEffects); }

            IReadOnlyList<ItemEffectCommand> expiredCommands = restoration == null
                ? (IReadOnlyList<ItemEffectCommand>)Array.Empty<ItemEffectCommand>()
                : restoration.ExpiredEnemyCommands;
            var result = new BattleResult(outcome, state.CurrentRound, reason,
                state.PlayerStartingHP, state.PlayerCurrentHP, alignedEconomy.CurrentHP,
                state.TotalBlockAbsorbed, livingEnemies, defeatedEnemies,
                state.BattleRandomState, alignedEconomy, state.PersistentEffectCommands,
                expiredCommands);
            return new BattleFinishResult(true, BattleFinishFailure.None, result, finished,
                alignedEconomy, board, hand, nextOwnership, restoredEffects, state.BattleRandomState);
        }

        private static int CountLivingEnemies(BattleState state)
        {
            int count = 0;
            foreach (BattleUnitState enemy in state.Enemies)
                if (!enemy.HasHealth || enemy.CurrentHP > 0) count++;
            return count;
        }

        private static bool TryCleanupUnits(IReadOnlyList<BattleUnitState> source,
            out BattleUnitState[] cleaned)
        {
            cleaned = new BattleUnitState[source.Count];
            for (int i = 0; i < source.Count; i++)
                if (!source[i].TryWithBattleCleanup(out cleaned[i]))
                { cleaned = null; return false; }
            return true;
        }

        private static BattleFinishResult Fail(BattleFinishFailure failure,
            BattleState state, EconomyState economy, BoardState board, HandState hand,
            CharacterOwnershipState ownership, ItemEffectState itemEffects) =>
            new BattleFinishResult(false, failure, null, state, economy, board, hand,
                ownership, itemEffects, state == null ? default : state.BattleRandomState);
    }
}
