using System;
using ProjectNST.GameCore.Characters;

namespace ProjectNST.GameCore.Board
{
    /// <summary>Structural Board move and swap rules, with cross-state integrity validation.</summary>
    public static class BoardRules
    {
        public static BoardHandOperationResult MoveOrSwap(BoardState board, HandState hand,
            CharacterOwnershipState ownership, int fromSlot, int toSlot,
            BoardHandOperationContext context)
        {
            BoardHandOperationResult failure = Validate(board, hand, ownership, context);
            if (failure != null) return failure;
            if (!board.TryGetCharacterIdAt(fromSlot, out string fromId))
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.SourceSlotOutOfRange);
            if (!board.TryGetCharacterIdAt(toSlot, out string toId))
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.DestinationSlotOutOfRange);
            if (fromSlot == toSlot)
                return BoardHandRules.SucceedOrFail(board, hand, board, hand, ownership);
            if (fromId == null)
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.SourceEmpty);

            board.TryWithCharacterAt(fromSlot, toId, out BoardState intermediate);
            intermediate.TryWithCharacterAt(toSlot, fromId, out BoardState nextBoard);
            return BoardHandRules.SucceedOrFail(board, hand, nextBoard, hand, ownership);
        }

        private static BoardHandOperationResult Validate(BoardState board, HandState hand,
            CharacterOwnershipState ownership, BoardHandOperationContext context)
        {
            if (board == null || hand == null || ownership == null)
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.InvalidInputState);
            if (context.IsBattleActive)
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.BattleInProgress);
            return BoardHandStateValidator.Validate(board, hand, ownership).IsValid
                ? null
                : BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.InvalidInputState);
        }
    }
}
