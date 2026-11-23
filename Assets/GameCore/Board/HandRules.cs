using ProjectNST.GameCore.Characters;

namespace ProjectNST.GameCore.Board
{
    /// <summary>Hand moves and swaps for empty, Character, and Item slots.</summary>
    public static class HandRules
    {
        public static BoardHandOperationResult MoveOrSwap( BoardState board, HandState hand,
            CharacterOwnershipState ownership, int fromSlot, int toSlot,
            BoardHandOperationContext context)
        {
            if (board == null || hand == null || ownership == null)
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.InvalidInputState);
            if (context.IsBattleActive)
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.BattleInProgress);
            if (!BoardHandStateValidator.Validate(board, hand, ownership).IsValid)
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.InvalidInputState);
            if (!hand.TryGetSlot(fromSlot, out HandSlotContent fromContent))
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.SourceSlotOutOfRange);
            if (!hand.TryGetSlot(toSlot, out HandSlotContent toContent))
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.DestinationSlotOutOfRange);
            if (fromSlot == toSlot)
                return BoardHandRules.SucceedOrFail(board, hand, board, hand, ownership);
            if (fromContent.Kind == HandSlotContentKind.Empty)
                return BoardHandRules.Fail(board, hand, BoardHandOperationFailureReason.SourceEmpty);

            hand.TryWithSlot(fromSlot, toContent, out HandState intermediate);
            intermediate.TryWithSlot(toSlot, fromContent, out HandState nextHand);
            return BoardHandRules.SucceedOrFail(board, hand, board, nextHand, ownership);
        }
    }
}
