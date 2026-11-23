using System;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleStartFailure
    {
        None,
        InvalidInput,
        StageNotReady,
        NoEnemyEncounterRequired,
        BattleAlreadyInProgress,
        BoardExceedsLevel,
        PlayerHealthUnavailable,
        InvalidBoardHandOwnershipState,
        InvalidBattleRandomState,
        InvalidDefinitions,
        MissingCharacterDefinition,
        CharacterStatsUnavailable,
        InvalidSynergyRoster,
        InvalidEnemyBoard,
        MissingEnemyDefinition,
        InvalidCombatCounter
    }

    /// <summary>Shared battle-start gate for manual, auto and fast UI entry points.</summary>
    public static class BattleStartRules
    {
        public static BattleStartFailure Validate(EconomyState player,
            StageProgressState stage, BoardState board, HandState hand,
            CharacterOwnershipState ownership, BattleState existingBattle = null)
        {
            if (player == null || stage == null || board == null || hand == null || ownership == null)
                return BattleStartFailure.InvalidInput;
            if (existingBattle != null && existingBattle.Status == BattleExecutionStatus.InProgress)
                return BattleStartFailure.BattleAlreadyInProgress;
            if (stage.CurrentSelection != null &&
                stage.CurrentSelection.EnemyCategory == StageEnemyCategory.None)
                return BattleStartFailure.NoEnemyEncounterRequired;
            if (stage.Phase != StageProgressPhase.Battle || stage.CurrentSelection == null)
                return BattleStartFailure.StageNotReady;
            if (!player.HasHealth || player.CurrentHP <= 0)
                return BattleStartFailure.PlayerHealthUnavailable;
            if (board.Columns != BoardState.StandardColumns || board.Rows != BoardState.StandardRows)
                return BattleStartFailure.InvalidBoardHandOwnershipState;
            int boardCount = 0;
            for (int i = 0; i < board.SlotCount; i++)
            {
                board.TryGetCharacterIdAt(i, out string id);
                if (id != null) boardCount++;
            }
            if (boardCount > player.Level)
                return BattleStartFailure.BoardExceedsLevel;
            return BoardHandStateValidator.Validate(board, hand, ownership).IsValid
                ? BattleStartFailure.None
                : BattleStartFailure.InvalidBoardHandOwnershipState;
        }
    }
}
