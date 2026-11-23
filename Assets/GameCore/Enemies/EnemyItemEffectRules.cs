using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Enemies
{
    public enum EnemyItemUseFailure
    {
        None,
        InvalidInput,
        NoEnemyTargets,
        UnsupportedTarget,
        ItemUseRejected,
        EnemyPreparationRejected
    }

    /// <summary>Combined item-use proposal. Adopt its next states only when Succeeded is true.</summary>
    public sealed class EnemyItemUseResult
    {
        public bool Succeeded { get; }
        public EnemyItemUseFailure Failure { get; }
        public ItemEffectFailure ItemFailure { get; }
        public ItemEffectFailure PreparationItemFailure { get; }
        public ItemEffectResult ItemResult { get; }
        public EnemyPreparationFailure PreparationFailure { get; }
        public BoardState NextBoard { get; }
        public HandState NextHand { get; }
        public CharacterOwnershipState NextOwnership { get; }
        public EconomyState NextEconomy { get; }
        public StageProgressState NextStage { get; }
        public ItemEffectState NextItemEffectState { get; }
        public EnemyPreparationState NextEnemyPreparationState { get; }
        public string ConsumedItemDefinitionId { get; }
        public Pcg32State NextStatsRandomState { get; }
        public bool StatsRandomConsumed { get; }

        internal EnemyItemUseResult(bool succeeded, EnemyItemUseFailure failure,
            ItemEffectFailure itemFailure, ItemEffectFailure preparationItemFailure,
            ItemEffectResult itemResult, EnemyPreparationFailure preparationFailure,
            BoardState board, HandState hand, CharacterOwnershipState ownership,
            EconomyState economy, StageProgressState stage, ItemEffectState effectState,
            EnemyPreparationState enemyPreparation, string consumedItemId,
            Pcg32State nextStatsRandomState, bool statsRandomConsumed)
        {
            Succeeded = succeeded;
            Failure = failure;
            ItemFailure = itemFailure;
            PreparationItemFailure = preparationItemFailure;
            ItemResult = itemResult;
            PreparationFailure = preparationFailure;
            NextBoard = board;
            NextHand = hand;
            NextOwnership = ownership;
            NextEconomy = economy;
            NextStage = stage;
            NextItemEffectState = effectState;
            NextEnemyPreparationState = enemyPreparation;
            ConsumedItemDefinitionId = consumedItemId;
            NextStatsRandomState = nextStatsRandomState;
            StatsRandomConsumed = statsRandomConsumed;
        }
    }

    /// <summary>Uses Stage 7-E validation/consumption, then atomically accumulates supported enemy commands.</summary>
    public static class EnemyItemEffectRules
    {
        public static EnemyItemUseResult TryUseHandItem(ItemDefinition item, int handSlotIndex,
            EnemyFormationDefinition formation, EnemyPreparationState enemyPreparation,
            BoardState board, HandState hand, CharacterOwnershipState ownership,
            EconomyState economy, StageProgressState stage, ItemEffectState effectState,
            IReadOnlyList<CharacterDefinition> characterDefinitions,
            CharacterStatDefinition statSettings, Pcg32State statsRandomState)
        {
            if (item == null || formation == null || enemyPreparation == null || board == null ||
                hand == null || ownership == null || economy == null || stage == null || effectState == null)
                return Fail(EnemyItemUseFailure.InvalidInput, ItemEffectFailure.InvalidInput,
                    EnemyPreparationFailure.None, null, board, hand, ownership, economy, stage,
                    effectState, enemyPreparation, statsRandomState);
            if (formation.Slots == null || formation.Slots.Count != EnemyFormationDefinition.RequiredSlotCount)
                return Fail(EnemyItemUseFailure.InvalidInput, ItemEffectFailure.InvalidInput,
                    EnemyPreparationFailure.InvalidFormation, null, board, hand, ownership, economy,
                    stage, effectState, enemyPreparation, statsRandomState);

            int enemyCount = 0;
            foreach (EnemyFormationSlotDefinition slot in formation.Slots)
                if (slot != null && slot.HasEnemy) enemyCount++;
            if (enemyCount == 0)
                return Fail(EnemyItemUseFailure.NoEnemyTargets, ItemEffectFailure.InvalidTarget,
                    EnemyPreparationFailure.NoEnemyTargets, null, board, hand, ownership, economy,
                    stage, effectState, enemyPreparation, statsRandomState);
            if (item.Target != ItemEffectTarget.AllEnemies)
                return Fail(EnemyItemUseFailure.UnsupportedTarget, ItemEffectFailure.UnsupportedEffect,
                    EnemyPreparationFailure.UnsupportedCommand, null, board, hand, ownership, economy,
                    stage, effectState, enemyPreparation, statsRandomState);

            ItemEffectResult itemResult = ItemEffectRules.TryUseHandItem(item, handSlotIndex,
                ItemUseTarget.EnemyArea(enemyCount), board, hand, ownership, economy, stage,
                effectState, characterDefinitions, statSettings, statsRandomState);
            if (!itemResult.Succeeded)
                return Fail(EnemyItemUseFailure.ItemUseRejected, itemResult.Failure,
                    EnemyPreparationFailure.None, itemResult, board, hand, ownership, economy,
                    stage, effectState, enemyPreparation, statsRandomState);

            EnemyPreparationResult preparationResult = enemyPreparation.TryApplyAllEnemies(
                formation, itemResult.Commands);
            if (!preparationResult.Succeeded)
                return Fail(EnemyItemUseFailure.EnemyPreparationRejected, ItemEffectFailure.None,
                    preparationResult.Failure, itemResult, board, hand, ownership, economy,
                    stage, effectState, enemyPreparation, statsRandomState);

            // ItemEffectState retains battle-duration commands for expiry bookkeeping. Stage 9 must
            // not reapply these commands: their numeric changes have already been folded into the
            // enemy initial state by EnemyPreparationState/EnemyFormationRules.
            return new EnemyItemUseResult(true, EnemyItemUseFailure.None, ItemEffectFailure.None,
                ItemEffectFailure.None, itemResult, EnemyPreparationFailure.None,
                itemResult.NextBoard, itemResult.NextHand, itemResult.NextOwnership,
                itemResult.NextEconomy, itemResult.NextStage, itemResult.NextEffectState,
                preparationResult.NextState, itemResult.ConsumedItemDefinitionId,
                itemResult.NextStatsRandomState, itemResult.StatsRandomConsumed);
        }

        private static EnemyItemUseResult Fail(EnemyItemUseFailure failure,
            ItemEffectFailure itemFailure, EnemyPreparationFailure preparationFailure,
            ItemEffectResult itemResult, BoardState board, HandState hand,
            CharacterOwnershipState ownership, EconomyState economy, StageProgressState stage,
            ItemEffectState effectState, EnemyPreparationState enemyPreparation,
            Pcg32State statsRandomState)
        {
            return new EnemyItemUseResult(false, failure, itemFailure,
                itemResult != null && itemResult.Succeeded ? itemFailure : ItemEffectFailure.None,
                itemResult, preparationFailure, board, hand, ownership, economy, stage,
                effectState, enemyPreparation, null, statsRandomState, false);
        }
    }
}
