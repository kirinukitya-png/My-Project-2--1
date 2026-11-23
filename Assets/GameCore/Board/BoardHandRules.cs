using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;

namespace ProjectNST.GameCore.Board
{
    /// <summary>Rules for moving characters between Board and Hand. No input state is mutated.</summary>
    public static class BoardHandRules
    {
        public static BoardHandOperationResult MoveBoardToHand(BoardState board, HandState hand,
            CharacterOwnershipState ownership, int boardSlot, int handSlot,
            BoardHandOperationContext context)
        {
            BoardHandOperationResult failure = Validate(board, hand, ownership, context);
            if (failure != null) return failure;
            if (!board.TryGetCharacterIdAt(boardSlot, out string boardId))
                return Fail(board, hand, BoardHandOperationFailureReason.SourceSlotOutOfRange);
            if (!hand.TryGetSlot(handSlot, out HandSlotContent handContent))
                return Fail(board, hand, BoardHandOperationFailureReason.DestinationSlotOutOfRange);
            if (boardId == null) return Fail(board, hand, BoardHandOperationFailureReason.SourceEmpty);
            if (handContent.Kind == HandSlotContentKind.Item)
                return Fail(board, hand, BoardHandOperationFailureReason.ItemBoardExchangeForbidden);

            string handId = handContent.Kind == HandSlotContentKind.Character
                ? handContent.CharacterInstanceId : null;
            board.TryWithCharacterAt(boardSlot, handId, out BoardState nextBoard);
            hand.TryWithSlot(handSlot, HandSlotContent.Character(boardId), out HandState nextHand);
            return SucceedOrFail(board, hand, nextBoard, nextHand, ownership);
        }

        public static BoardHandOperationResult MoveHandToBoard(BoardState board, HandState hand,
            CharacterOwnershipState ownership, int handSlot, int boardSlot,
            BoardHandOperationContext context)
        {
            BoardHandOperationResult failure = Validate(board, hand, ownership, context);
            if (failure != null) return failure;
            if (!hand.TryGetSlot(handSlot, out HandSlotContent handContent))
                return Fail(board, hand, BoardHandOperationFailureReason.SourceSlotOutOfRange);
            if (!board.TryGetCharacterIdAt(boardSlot, out string boardId))
                return Fail(board, hand, BoardHandOperationFailureReason.DestinationSlotOutOfRange);
            if (handContent.Kind == HandSlotContentKind.Empty)
                return Fail(board, hand, BoardHandOperationFailureReason.SourceEmpty);
            if (handContent.Kind == HandSlotContentKind.Item)
                return Fail(board, hand, BoardHandOperationFailureReason.ItemCannotBePlacedOnBoard);

            hand.TryWithSlot(handSlot,
                boardId == null ? HandSlotContent.Empty() : HandSlotContent.Character(boardId),
                out HandState nextHand);
            board.TryWithCharacterAt(boardSlot, handContent.CharacterInstanceId, out BoardState nextBoard);
            return SucceedOrFail(board, hand, nextBoard, nextHand, ownership);
        }

        private static BoardHandOperationResult Validate(BoardState board, HandState hand,
            CharacterOwnershipState ownership, BoardHandOperationContext context)
        {
            if (board == null || hand == null || ownership == null)
                return Fail(board, hand, BoardHandOperationFailureReason.InvalidInputState);
            if (context.IsBattleActive)
                return Fail(board, hand, BoardHandOperationFailureReason.BattleInProgress);
            BoardHandStateValidationResult validation = BoardHandStateValidator.Validate(board, hand, ownership);
            return validation.IsValid ? null : Fail(board, hand, BoardHandOperationFailureReason.InvalidInputState);
        }

        internal static BoardHandOperationResult SucceedOrFail(BoardState originalBoard, HandState originalHand,
            BoardState nextBoard, HandState nextHand, CharacterOwnershipState ownership)
        {
            BoardHandStateValidationResult validation = BoardHandStateValidator.Validate(nextBoard, nextHand, ownership);
            if (!validation.IsValid)
            {
                bool duplicateDefinition = false;
                foreach (string error in validation.Errors)
                    if (error.IndexOf("duplicate DefinitionId", StringComparison.OrdinalIgnoreCase) >= 0)
                        duplicateDefinition = true;
                return Fail(originalBoard, originalHand, duplicateDefinition
                    ? BoardHandOperationFailureReason.DuplicateDefinitionOnBoard
                    : BoardHandOperationFailureReason.InvalidResultState);
            }
            return new BoardHandOperationResult(true, BoardHandOperationFailureReason.None,
                nextBoard, nextHand, GetChanges(originalBoard, originalHand, nextBoard, nextHand));
        }

        internal static BoardHandOperationResult Fail(BoardState board, HandState hand,
            BoardHandOperationFailureReason reason)
        {
            return new BoardHandOperationResult(false, reason, board, hand,
                Array.Empty<BoardHandSlotChange>());
        }

        internal static List<BoardHandSlotChange> GetChanges(BoardState beforeBoard, HandState beforeHand,
            BoardState afterBoard, HandState afterHand)
        {
            var changes = new List<BoardHandSlotChange>();
            for (int i = 0; i < beforeBoard.SlotCount; i++)
            {
                beforeBoard.TryGetCharacterIdAt(i, out string before);
                afterBoard.TryGetCharacterIdAt(i, out string after);
                if (!string.Equals(before, after, StringComparison.Ordinal))
                    changes.Add(new BoardHandSlotChange(new CharacterLocation(CharacterLocationKind.Board, i),
                        before, after, null, null));
            }
            for (int i = 0; i < beforeHand.SlotCount; i++)
            {
                beforeHand.TryGetSlot(i, out HandSlotContent before);
                afterHand.TryGetSlot(i, out HandSlotContent after);
                if (!SameContent(before, after))
                    changes.Add(new BoardHandSlotChange(new CharacterLocation(CharacterLocationKind.Hand, i),
                        null, null, before, after));
            }
            return changes;
        }

        internal static bool SameContent(HandSlotContent left, HandSlotContent right)
        {
            return left != null && right != null && left.Kind == right.Kind &&
                string.Equals(left.CharacterInstanceId, right.CharacterInstanceId, StringComparison.Ordinal) &&
                string.Equals(left.ItemDefinitionId, right.ItemDefinitionId, StringComparison.Ordinal);
        }
    }
}
