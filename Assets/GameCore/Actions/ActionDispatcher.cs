using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Actions
{
    public static class GameActionStructuralValidator
    {
        public static bool Validate(GameAction action, out ActionError error)
        {
            if (action == null)
            { error = new ActionError(ActionErrorCode.InvalidAction, "Action is null."); return false; }
            if (string.IsNullOrWhiteSpace(action.ActionId.Value))
            { error = new ActionError(ActionErrorCode.InvalidAction, "Action ID is missing."); return false; }

            bool valid = false;
            switch (action.Kind)
            {
                case GameActionKind.SelectStageChoice:
                    valid = action is SelectStageChoiceAction choice && Enum.IsDefined(typeof(StageChoice), choice.Choice);
                    break;
                case GameActionKind.BuyCharacter:
                    valid = action is BuyCharacterAction character && !string.IsNullOrWhiteSpace(character.OfferId) &&
                        Enum.IsDefined(typeof(CharacterLocationKind), character.Placement.Kind) && character.Placement.SlotIndex >= 0;
                    break;
                case GameActionKind.BuyItem:
                    valid = action is BuyItemAction item && !string.IsNullOrWhiteSpace(item.OfferId) && item.HandSlot >= 0;
                    break;
                case GameActionKind.RerollShop:
                    valid = action is RerollShopAction reroll && Enum.IsDefined(typeof(ShopKind), reroll.ShopKind) && reroll.ShopKind != ShopKind.None;
                    break;
                case GameActionKind.BuyExperience: valid = action is BuyExperienceAction; break;
                case GameActionKind.MoveOrSwapBoard:
                    valid = action is MoveOrSwapBoardAction board && board.SourceSlot >= 0 && board.TargetSlot >= 0;
                    break;
                case GameActionKind.MoveOrSwapHand:
                    valid = action is MoveOrSwapHandAction hand && hand.SourceSlot >= 0 && hand.TargetSlot >= 0;
                    break;
                case GameActionKind.MoveBoardToHand:
                    valid = action is MoveBoardToHandAction boardHand && boardHand.BoardSlot >= 0 && boardHand.HandSlot >= 0;
                    break;
                case GameActionKind.MoveHandToBoard:
                    valid = action is MoveHandToBoardAction handBoard && handBoard.HandSlot >= 0 && handBoard.BoardSlot >= 0;
                    break;
                case GameActionKind.SellCharacter:
                    valid = action is SellCharacterAction sale && !string.IsNullOrWhiteSpace(sale.InstanceId);
                    break;
                case GameActionKind.SellItem:
                    valid = action is SellItemAction itemSale && itemSale.HandSlot >= 0;
                    break;
                case GameActionKind.UseHandItem:
                    if (action is UseHandItemAction useHand && useHand.HandSlot >= 0)
                        valid = ValidateTarget(useHand.Target);
                    break;
                case GameActionKind.UseEventItem:
                    if (action is UseEventItemAction useEvent) valid = ValidateTarget(useEvent.Target);
                    break;
                case GameActionKind.EndEvent: valid = action is EndEventAction; break;
                case GameActionKind.StartBattle: valid = action is StartBattleAction; break;
                default: valid = false; break;
            }
            error = valid ? ActionError.None : new ActionError(ActionErrorCode.InvalidAction,
                "Action payload does not match its ActionKind or is structurally invalid.");
            return valid;
        }

        private static bool ValidateTarget(GameActionTarget target)
        {
            try { target.Validate(); return true; }
            catch (ArgumentException) { return false; }
        }
    }

    public static class ActionPhaseRules
    {
        public static bool IsAllowed(GameActionKind kind, StageProgressPhase phase)
        {
            switch (kind)
            {
                case GameActionKind.SelectStageChoice: return phase == StageProgressPhase.AwaitingStageChoice;
                case GameActionKind.UseEventItem:
                case GameActionKind.EndEvent: return phase == StageProgressPhase.EventPending;
                case GameActionKind.StartBattle:
                case GameActionKind.BuyCharacter:
                case GameActionKind.BuyItem:
                case GameActionKind.RerollShop:
                case GameActionKind.BuyExperience:
                case GameActionKind.MoveOrSwapBoard:
                case GameActionKind.MoveOrSwapHand:
                case GameActionKind.MoveBoardToHand:
                case GameActionKind.MoveHandToBoard:
                case GameActionKind.SellCharacter:
                case GameActionKind.SellItem:
                case GameActionKind.UseHandItem:
                    return phase == StageProgressPhase.Preparation;
                default: return false;
            }
        }
    }

    /// <summary>Single common dispatch entry. Individual rules are supplied by later stage handlers.</summary>
    public sealed class ActionDispatcher
    {
        private readonly Dictionary<GameActionKind, IActionHandler> _handlers;

        public ActionDispatcher(IEnumerable<IActionHandler> handlers = null)
        {
            _handlers = new Dictionary<GameActionKind, IActionHandler>();
            if (handlers == null) return;
            foreach (IActionHandler handler in handlers)
            {
                if (handler == null) throw new ArgumentException("Handler registrations cannot contain null.", nameof(handlers));
                if (!Enum.IsDefined(typeof(GameActionKind), handler.Kind))
                    throw new ArgumentException("Handler has an unknown ActionKind.", nameof(handlers));
                if (_handlers.ContainsKey(handler.Kind))
                    throw new ArgumentException("Only one handler may be registered for each ActionKind.", nameof(handlers));
                _handlers.Add(handler.Kind, handler);
            }
        }

        public ActionDispatchResult Dispatch(ActionExecutionState state, GameAction action,
            ActionExecutionContext context)
        {
            if (state == null) return Result(null, action, ActionDispatchStatus.Rejected,
                ActionErrorCode.InvalidState, "ActionExecutionState is null.");
            if (action == null) return Result(state, null, ActionDispatchStatus.Rejected,
                ActionErrorCode.InvalidAction, "GameAction is null.");
            if (context == null) return Result(state, action, ActionDispatchStatus.ConfigurationError,
                ActionErrorCode.ConfigurationError, "ActionExecutionContext is null.");
            if (!GameActionStructuralValidator.Validate(action, out ActionError structuralError))
                return Result(state, action, ActionDispatchStatus.Rejected, structuralError.Code, structuralError.Detail);
            if (!ActionPhaseRules.IsAllowed(action.Kind, state.Stage.Phase))
                return Result(state, action, ActionDispatchStatus.Rejected, ActionErrorCode.InvalidPhase,
                    "Action is not allowed in the current StageProgressPhase.");
            if (state.HasExecuted(action.ActionId))
                return Result(state, action, ActionDispatchStatus.Rejected, ActionErrorCode.DuplicateAction,
                    "This Action ID has already been successfully applied.");
            if (!_handlers.TryGetValue(action.Kind, out IActionHandler handler))
                return Result(state, action, ActionDispatchStatus.Rejected, ActionErrorCode.UnsupportedAction,
                    "No handler is registered for this ActionKind.");

            ActionHandlerResult handlerResult;
            try
            {
                handlerResult = handler.Handle(state, action, context);
            }
            catch (ActionExecutionConfigurationException ex)
            {
                return Result(state, action, ActionDispatchStatus.ConfigurationError,
                    ActionErrorCode.ConfigurationError, ex.Message);
            }
            catch (Exception ex)
            {
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, ex.Message);
            }

            if (handlerResult == null)
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, "Handler returned a null result.");
            if (handlerResult.Status == ActionHandlerResultStatus.Rejected)
                return Result(state, action, ActionDispatchStatus.Rejected,
                    handlerResult.Error.Code, handlerResult.Error.Detail);
            if (handlerResult.Status == ActionHandlerResultStatus.ConfigurationError)
                return Result(state, action, ActionDispatchStatus.ConfigurationError,
                    ActionErrorCode.ConfigurationError, handlerResult.Error.Detail);
            if (handlerResult.Status == ActionHandlerResultStatus.InternalError)
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, handlerResult.Error.Detail);
            if (handlerResult.Status != ActionHandlerResultStatus.Succeeded || handlerResult.NextState == null)
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, "Handler returned an invalid success result.");

            ActionExecutionState proposed = handlerResult.NextState;
            if (proposed.RandomState.TrialSeed != state.RandomState.TrialSeed ||
                proposed.RandomState.Version != state.RandomState.Version)
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, "Handler changed RNG TrialSeed or checkpoint version.");
            RandomStreamMask changedStreams = GetChangedRandomStreams(state.RandomState, proposed.RandomState);
            if (changedStreams != handlerResult.RandomStreamsChanged)
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, "Handler-declared RNG stream changes do not match its checkpoint.");
            if (!state.HasSameActionHistory(proposed))
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, "Handler must not modify executed Action ID history.");
            if (proposed.HasExecuted(action.ActionId))
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, "Handler recorded an Action ID; only the Dispatcher may do so.");

            ActionExecutionValidationResult validation = ActionExecutionStateValidator.Validate(proposed);
            if (!validation.IsValid)
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, "Handler proposed an invalid aggregate: " + string.Join("; ", validation.Errors));

            ActionExecutionState accepted;
            try { accepted = proposed.WithRecordedAction(action.ActionId); }
            catch (Exception ex)
            {
                return Result(state, action, ActionDispatchStatus.InternalError,
                    ActionErrorCode.InternalError, "Could not record successful Action ID: " + ex.Message);
            }
            return new ActionDispatchResult(accepted, state, action, ActionDispatchStatus.Succeeded, ActionError.None);
        }

        private static ActionDispatchResult Result(ActionExecutionState state, GameAction action,
            ActionDispatchStatus status, ActionErrorCode code, string detail)
        {
            return new ActionDispatchResult(null, state, action, status, new ActionError(code, detail));
        }

        private static RandomStreamMask GetChangedRandomStreams(SimulationRandomStreamsState before,
            SimulationRandomStreamsState after)
        {
            RandomStreamMask changed = RandomStreamMask.None;
            if (!before.Shop.Equals(after.Shop)) changed |= RandomStreamMask.Shop;
            if (!before.Stats.Equals(after.Stats)) changed |= RandomStreamMask.Stats;
            if (!before.Encounter.Equals(after.Encounter)) changed |= RandomStreamMask.Encounter;
            if (!before.Battle.Equals(after.Battle)) changed |= RandomStreamMask.Battle;
            if (!before.Event.Equals(after.Event)) changed |= RandomStreamMask.Event;
            if (!before.Roster.Equals(after.Roster)) changed |= RandomStreamMask.Roster;
            if (!before.Economy.Equals(after.Economy)) changed |= RandomStreamMask.Economy;
            return changed;
        }
    }
}
