using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Board
{
    public enum BoardHandOperationFailureReason
    {
        None = 0,
        InvalidInputState,
        BattleInProgress,
        SourceSlotOutOfRange,
        DestinationSlotOutOfRange,
        SourceEmpty,
        ItemCannotBePlacedOnBoard,
        ItemBoardExchangeForbidden,
        DuplicateDefinitionOnBoard,
        InvalidResultState
    }

    /// <summary>Explicit phase context used to reject preparation moves during battle.</summary>
    public readonly struct BoardHandOperationContext
    {
        public bool IsBattleActive { get; }
        public BoardHandOperationContext(bool isBattleActive) => IsBattleActive = isBattleActive;
        public static BoardHandOperationContext Preparation => new BoardHandOperationContext(false);
        public static BoardHandOperationContext Battle => new BoardHandOperationContext(true);
    }

    /// <summary>Describes one changed Board or Hand slot in an immutable operation result.</summary>
    public sealed class BoardHandSlotChange
    {
        public CharacterLocation Location { get; }
        public string BeforeCharacterInstanceId { get; }
        public string AfterCharacterInstanceId { get; }
        public HandSlotContent BeforeHandContent { get; }
        public HandSlotContent AfterHandContent { get; }

        internal BoardHandSlotChange(CharacterLocation location, string beforeCharacterId,
            string afterCharacterId, HandSlotContent beforeHandContent, HandSlotContent afterHandContent)
        {
            Location = location;
            BeforeCharacterInstanceId = beforeCharacterId;
            AfterCharacterInstanceId = afterCharacterId;
            BeforeHandContent = beforeHandContent;
            AfterHandContent = afterHandContent;
        }
    }

    /// <summary>Proposed immutable Board/Hand states; failed operations retain the exact input states.</summary>
    public sealed class BoardHandOperationResult
    {
        public bool Succeeded { get; }
        public BoardHandOperationFailureReason FailureReason { get; }
        public BoardState NextBoard { get; }
        public HandState NextHand { get; }
        public IReadOnlyList<BoardHandSlotChange> ChangedSlots { get; }

        internal BoardHandOperationResult(bool succeeded, BoardHandOperationFailureReason failureReason,
            BoardState nextBoard, HandState nextHand, IEnumerable<BoardHandSlotChange> changedSlots)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            NextBoard = nextBoard;
            NextHand = nextHand;
            ChangedSlots = Array.AsReadOnly(new List<BoardHandSlotChange>(changedSlots).ToArray());
        }
    }
}
