using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Actions
{
    /// <summary>Stage 10-D3 handlers for Board/Hand movement and sale actions.</summary>
    public static class BoardHandActionHandlers
    {
        public static IReadOnlyList<IActionHandler> Create() =>
            new ReadOnlyCollection<IActionHandler>(new IActionHandler[]
            {
                new MoveOrSwapBoardActionHandler(), new MoveOrSwapHandActionHandler(),
                new MoveBoardToHandActionHandler(), new MoveHandToBoardActionHandler(),
                new SellCharacterActionHandler(), new SellItemActionHandler()
            });
    }

    public sealed class MoveOrSwapBoardActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.MoveOrSwapBoard;
        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is MoveOrSwapBoardAction move)) return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected MoveOrSwapBoardAction.");
            BoardHandOperationResult result = BoardRules.MoveOrSwap(state.Board, state.Hand, state.Ownership, move.SourceSlot, move.TargetSlot, BoardHandOperationContext.Preparation);
            if (!result.Succeeded) return ActionHandlerResult.Reject(Map(result.FailureReason), "Board move rejected by BoardRules: " + result.FailureReason + ".");
            return ActionHandlerResult.Success(state.With(board: result.NextBoard, hand: result.NextHand));
        }
        internal static ActionErrorCode Map(BoardHandOperationFailureReason reason)
        {
            switch (reason)
            {
                case BoardHandOperationFailureReason.BattleInProgress: return ActionErrorCode.BattleInProgress;
                case BoardHandOperationFailureReason.SourceSlotOutOfRange:
                case BoardHandOperationFailureReason.DestinationSlotOutOfRange: return ActionErrorCode.InvalidSlot;
                case BoardHandOperationFailureReason.SourceEmpty:
                case BoardHandOperationFailureReason.ItemCannotBePlacedOnBoard:
                case BoardHandOperationFailureReason.ItemBoardExchangeForbidden: return ActionErrorCode.InvalidTarget;
                case BoardHandOperationFailureReason.DuplicateDefinitionOnBoard: return ActionErrorCode.BoardLimitExceeded;
                default: return ActionErrorCode.InvalidState;
            }
        }
    }

    public sealed class MoveOrSwapHandActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.MoveOrSwapHand;
        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is MoveOrSwapHandAction move)) return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected MoveOrSwapHandAction.");
            BoardHandOperationResult result = HandRules.MoveOrSwap(state.Board, state.Hand, state.Ownership, move.SourceSlot, move.TargetSlot, BoardHandOperationContext.Preparation);
            if (!result.Succeeded) return ActionHandlerResult.Reject(MoveOrSwapBoardActionHandler.Map(result.FailureReason), "Hand move rejected by HandRules: " + result.FailureReason + ".");
            return ActionHandlerResult.Success(state.With(board: result.NextBoard, hand: result.NextHand));
        }
    }

    public sealed class MoveBoardToHandActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.MoveBoardToHand;
        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is MoveBoardToHandAction move)) return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected MoveBoardToHandAction.");
            BoardHandOperationResult result = BoardHandRules.MoveBoardToHand(state.Board, state.Hand, state.Ownership, move.BoardSlot, move.HandSlot, BoardHandOperationContext.Preparation);
            if (!result.Succeeded) return ActionHandlerResult.Reject(MoveOrSwapBoardActionHandler.Map(result.FailureReason), "Board-to-Hand move rejected by BoardHandRules: " + result.FailureReason + ".");
            return ActionHandlerResult.Success(state.With(board: result.NextBoard, hand: result.NextHand));
        }
    }

    public sealed class MoveHandToBoardActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.MoveHandToBoard;
        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is MoveHandToBoardAction move)) return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected MoveHandToBoardAction.");
            BoardHandOperationResult result = BoardHandRules.MoveHandToBoard(state.Board, state.Hand, state.Ownership, move.HandSlot, move.BoardSlot, BoardHandOperationContext.Preparation);
            if (!result.Succeeded) return ActionHandlerResult.Reject(MoveOrSwapBoardActionHandler.Map(result.FailureReason), "Hand-to-Board move rejected by BoardHandRules: " + result.FailureReason + ".");
            return ActionHandlerResult.Success(state.With(board: result.NextBoard, hand: result.NextHand));
        }
    }

    public sealed class SellCharacterActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.SellCharacter;
        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is SellCharacterAction sale)) return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected SellCharacterAction.");
            try
            {
                if (!state.Ownership.TryCreateOwnedInstanceCopy(sale.InstanceId, out CharacterInstance target))
                    return ActionHandlerResult.Reject(ActionErrorCode.UnknownInstance, "Character instance is not owned.");
                if (!BoardHandStateValidator.TryFindLocation(state.Board, state.Hand, sale.InstanceId, out CharacterLocation location))
                    return ActionHandlerResult.Reject(ActionErrorCode.InvalidState, "Owned character must occupy exactly one Board or Hand slot before sale.");
                IReadOnlyList<CharacterInstanceView> views = state.Ownership.GetOwnedInstances();
                var ownedCopies = new List<CharacterInstance>(views.Count);
                foreach (CharacterInstanceView view in views)
                {
                    if (!state.Ownership.TryCreateOwnedInstanceCopy(view.InstanceId, out CharacterInstance owned))
                        return ActionHandlerResult.Reject(ActionErrorCode.InvalidState, "Ownership changed while preparing the sale transaction.");
                    ownedCopies.Add(owned);
                }
                CharacterSaleResult result = ShopTransactionRules.TrySellCharacter(target, ownedCopies, state.Economy, state.CharacterPool);
                if (!result.Succeeded) return ActionHandlerResult.Reject(Map(result.FailureReason), "Character sale rejected by ShopTransactionRules: " + result.FailureReason + ".");
                BoardState nextBoard = state.Board;
                HandState nextHand = state.Hand;
                if (location.Kind == CharacterLocationKind.Board)
                {
                    if (!nextBoard.TryWithCharacterAt(location.SlotIndex, null, out nextBoard))
                        return ActionHandlerResult.Reject(ActionErrorCode.InvalidSlot, "Could not clear the Board slot.");
                }
                else if (!nextHand.TryWithSlot(location.SlotIndex, HandSlotContent.Empty(), out nextHand))
                    return ActionHandlerResult.Reject(ActionErrorCode.InvalidSlot, "Could not clear the Hand slot.");
                var nextOwnership = new CharacterOwnershipState(result.NextOwnedCharacters);
                ActionExecutionState next = state.With(economy: result.NextEconomy, characterPool: result.NextPool,
                    board: nextBoard, hand: nextHand, ownership: nextOwnership);
                ActionExecutionValidationResult validation = ActionExecutionStateValidator.Validate(next);
                if (!validation.IsValid) return ActionHandlerResult.InternalError("Character sale proposal violates state invariants: " + string.Join("; ", validation.Errors));
                return ActionHandlerResult.Success(next);
            }
            catch (Exception ex)
            {
                return ActionHandlerResult.InternalError("Character sale transaction failed without adopting changes: " + ex.Message);
            }
        }
        private static ActionErrorCode Map(CharacterSaleFailureReason reason)
        {
            switch (reason)
            {
                case CharacterSaleFailureReason.InvalidTarget:
                case CharacterSaleFailureReason.NotOwned: return ActionErrorCode.UnknownInstance;
                case CharacterSaleFailureReason.PoolEntryMissing:
                case CharacterSaleFailureReason.InsufficientOwnedCopies: return ActionErrorCode.InsufficientStock;
                default: return ActionErrorCode.InvalidState;
            }
        }
    }

    public sealed class SellItemActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.SellItem;
        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is SellItemAction sale)) return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected SellItemAction.");
            ItemHandSaleResult result = ItemHandTransactionRules.TrySellFromHand(state.Hand, state.Economy, sale.HandSlot);
            if (!result.Succeeded)
            {
                ActionErrorCode error = result.Failure == ItemHandSaleFailure.InvalidHandSlot ? ActionErrorCode.InvalidSlot :
                    result.Failure == ItemHandSaleFailure.SlotDoesNotContainItem ? ActionErrorCode.InvalidTarget : ActionErrorCode.InvalidState;
                return ActionHandlerResult.Reject(error, "Item sale rejected by ItemHandTransactionRules: " + result.Failure + ".");
            }
            return ActionHandlerResult.Success(state.With(economy: result.NextEconomy, hand: result.NextHand));
        }
    }
}