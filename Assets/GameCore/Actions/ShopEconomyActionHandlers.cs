using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Actions
{
    /// <summary>Creates the Stage 10-D2 registrations. Other ActionKinds remain unsupported.</summary>
    public static class ShopEconomyActionHandlers
    {
        public static IReadOnlyList<IActionHandler> Create()
        {
            return new ReadOnlyCollection<IActionHandler>(new IActionHandler[]
            {
                new BuyCharacterActionHandler(),
                new BuyItemActionHandler(),
                new RerollShopActionHandler(),
                new BuyExperienceActionHandler()
            });
        }
    }

    public sealed class BuyCharacterActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.BuyCharacter;

        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action,
            ActionExecutionContext context)
        {
            if (!(action is BuyCharacterAction buy))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected BuyCharacterAction.");
            if (state.Shop == null)
                return ActionHandlerResult.ConfigurationError("The active ShopState is missing.");
            if (state.Stage.CurrentSelection == null)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidState, "No active stage selection is available.");
            if (state.Stage.CurrentSelection.ShopMode != StageShopMode.Character || state.Shop.Kind != ShopKind.Character)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Character purchase requires a Character shop.");
            if (context.SelectedRoster == null || context.AssignedCharacterCosts == null ||
                context.CharacterStatSettings == null)
                return ActionHandlerResult.ConfigurationError("Selected roster, assigned Costs, and CharacterStatDefinition are required.");
            if (context.SelectedRoster.Count == 0)
                return ActionHandlerResult.ConfigurationError("Selected roster is empty.");

            try
            {
                bool alreadyPurchased = IsOfferPurchased(state.Shop, buy.OfferId);
                BoardShopTransactionResult transaction = BoardShopTransactionRules.TryPurchaseCharacter(
                    state.Shop, buy.OfferId, context.SelectedRoster, context.AssignedCharacterCosts,
                    state.Economy, state.CharacterPool, state.Board, state.Hand, state.Ownership,
                    state.InstanceFactory, context.CharacterStatSettings, state.RandomState.Stats,
                    buy.Placement.ToCharacterLocation(), ownershipScopeMatchesPool: true);
                if (!transaction.Succeeded)
                    return ActionHandlerResult.Reject(Map(transaction.FailureReason, buy.Placement.Kind, alreadyPurchased),
                        "Character purchase rejected by BoardShopTransactionRules: " + transaction.FailureReason + ".");

                SimulationRandomStreamsState nextRandom = ReplaceStats(state.RandomState, transaction.NextStatsRandomState);
                ActionExecutionState next = state.With(economy: transaction.NextEconomy,
                    shop: transaction.NextShop, characterPool: transaction.NextPool,
                    instanceFactory: transaction.NextInstanceFactory, board: transaction.NextBoard,
                    hand: transaction.NextHand, ownership: transaction.NextOwnership,
                    randomState: nextRandom);
                return ActionHandlerResult.Success(next,
                    nextRandom.Stats.Equals(state.RandomState.Stats) ? RandomStreamMask.None : RandomStreamMask.Stats);
            }
            catch (Exception ex) when (IsRuleConfigurationException(ex))
            {
                return ActionHandlerResult.ConfigurationError("Character shop configuration or definitions are invalid: " + ex.Message);
            }
        }

        private static ActionErrorCode Map(BoardShopTransactionFailureReason reason,
            CharacterLocationKind placementKind, bool alreadyPurchased)
        {
            switch (reason)
            {
                case BoardShopTransactionFailureReason.InvalidOffer:
                    return alreadyPurchased ? ActionErrorCode.AlreadyPurchased : ActionErrorCode.UnknownOffer;
                case BoardShopTransactionFailureReason.InsufficientGold: return ActionErrorCode.InsufficientGold;
                case BoardShopTransactionFailureReason.MissingReservation:
                case BoardShopTransactionFailureReason.PoolEntryMissing: return ActionErrorCode.InsufficientStock;
                case BoardShopTransactionFailureReason.InvalidPlacement: return ActionErrorCode.InvalidSlot;
                case BoardShopTransactionFailureReason.PlacementOccupied:
                    return placementKind == CharacterLocationKind.Hand ? ActionErrorCode.HandFull : ActionErrorCode.InvalidSlot;
                case BoardShopTransactionFailureReason.DuplicateDefinitionOnBoard: return ActionErrorCode.BoardLimitExceeded;
                case BoardShopTransactionFailureReason.MissingDefinition:
                case BoardShopTransactionFailureReason.CostMismatch:
                case BoardShopTransactionFailureReason.InvalidStatsConfiguration: return ActionErrorCode.InvalidDefinition;
                default: return ActionErrorCode.InvalidState;
            }
        }

        private static bool IsOfferPurchased(ShopState shop, string offerId)
        {
            if (shop == null || string.IsNullOrWhiteSpace(offerId)) return false;
            foreach (ShopOffer offer in shop.Offers)
                if (offer.OfferId == offerId) return offer.Status == ShopOfferStatus.Purchased;
            return false;
        }

        private static bool IsRuleConfigurationException(Exception ex) =>
            ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is OverflowException;

        internal static SimulationRandomStreamsState ReplaceStats(SimulationRandomStreamsState state, Pcg32State stats)
        {
            return new SimulationRandomStreamsState(state.TrialSeed, state.Version,
                state.Shop, stats, state.Encounter, state.Battle, state.Event, state.Roster, state.Economy);
        }
    }

    public sealed class BuyItemActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.BuyItem;

        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action,
            ActionExecutionContext context)
        {
            if (!(action is BuyItemAction buy))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected BuyItemAction.");
            if (state.Shop == null)
                return ActionHandlerResult.ConfigurationError("The active ShopState is missing.");
            if (state.Stage.CurrentSelection == null)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidState, "No active stage selection is available.");
            if (state.Stage.CurrentSelection.ShopMode != StageShopMode.Item || state.Shop.Kind != ShopKind.Item)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Item purchase requires an Item shop.");
            if (context.ItemDefinitions == null)
                return ActionHandlerResult.ConfigurationError("ItemDefinitions are unavailable.");

            try
            {
                bool alreadyPurchased = IsOfferPurchased(state.Shop, buy.OfferId);
                ItemHandPurchaseResult transaction = ItemHandTransactionRules.TryPurchaseIntoHand(
                    state.Shop, buy.OfferId, context.ItemDefinitions, state.Economy, state.Hand, buy.HandSlot);
                if (!transaction.Succeeded)
                    return ActionHandlerResult.Reject(Map(transaction.Failure, alreadyPurchased),
                        "Item purchase rejected by ItemHandTransactionRules: " + transaction.Failure + ".");

                return ActionHandlerResult.Success(state.With(economy: transaction.NextEconomy,
                    shop: transaction.NextShop, hand: transaction.NextHand));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                return ActionHandlerResult.ConfigurationError("Item shop definitions are invalid: " + ex.Message);
            }
        }

        private static ActionErrorCode Map(ItemHandPurchaseFailure reason, bool alreadyPurchased)
        {
            switch (reason)
            {
                case ItemHandPurchaseFailure.InvalidOffer:
                    return alreadyPurchased ? ActionErrorCode.AlreadyPurchased : ActionErrorCode.UnknownOffer;
                case ItemHandPurchaseFailure.InsufficientGold: return ActionErrorCode.InsufficientGold;
                case ItemHandPurchaseFailure.InvalidHandSlot: return ActionErrorCode.InvalidSlot;
                case ItemHandPurchaseFailure.HandSlotOccupied: return ActionErrorCode.HandFull;
                case ItemHandPurchaseFailure.MissingDefinition:
                case ItemHandPurchaseFailure.PriceMismatch: return ActionErrorCode.InvalidDefinition;
                case ItemHandPurchaseFailure.ItemShopInactive: return ActionErrorCode.InvalidAction;
                default: return ActionErrorCode.InvalidState;
            }
        }

        private static bool IsOfferPurchased(ShopState shop, string offerId)
        {
            if (shop == null || string.IsNullOrWhiteSpace(offerId)) return false;
            foreach (ShopOffer offer in shop.Offers)
                if (offer.OfferId == offerId) return offer.Status == ShopOfferStatus.Purchased;
            return false;
        }
    }

    public sealed class RerollShopActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.RerollShop;

        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action,
            ActionExecutionContext context)
        {
            if (!(action is RerollShopAction reroll))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected RerollShopAction.");
            if (state.Shop == null)
                return ActionHandlerResult.ConfigurationError("The active ShopState is missing.");
            if (context.ShopRerollGoldCost == null)
                return ActionHandlerResult.ConfigurationError("Shop reroll Gold cost is unavailable in ActionExecutionContext.");
            if (state.Stage.CurrentSelection == null)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidState, "No active stage selection is available.");
            if (!state.Stage.CurrentSelection.CanReroll)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Reroll is disabled for the current stage selection.");
            ShopKind expectedShop = state.Stage.CurrentSelection.ShopMode == StageShopMode.Item
                ? ShopKind.Item : ShopKind.Character;
            if (reroll.ShopKind != expectedShop || state.Shop.Kind != expectedShop)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Requested shop kind does not match the active stage/shop.");
            if (state.Shop.GenerationIndex == int.MaxValue)
                return ActionHandlerResult.ConfigurationError("Shop generation index is exhausted.");

            EconomyActionResult spend = EconomyRules.TrySpendGold(state.Economy, context.ShopRerollGoldCost.Value);
            if (!spend.Succeeded)
                return ActionHandlerResult.Reject(Map(spend.FailureReason), "Cannot pay the configured reroll cost.");

            try
            {
                if (expectedShop == ShopKind.Character)
                {
                    if (context.SelectedRoster == null || context.AssignedCharacterCosts == null || context.CharacterShopConfig == null)
                        return ActionHandlerResult.ConfigurationError("Character shop definitions, Cost assignments, or configuration are unavailable.");

                    Pcg32RandomSource workingShopRandom = Pcg32RandomSource.FromState(state.RandomState.Shop);
                    CharacterShopGenerationResult generated = CharacterShopRules.Refresh(state.Shop,
                        context.SelectedRoster, context.AssignedCharacterCosts, state.CharacterPool,
                        context.CharacterShopConfig, workingShopRandom);
                    if (HasEmptyCharacterOffer(generated.Shop))
                        return ActionHandlerResult.Reject(ActionErrorCode.InsufficientStock,
                            "Character reroll could not fill every shop slot; original shop, Gold, stock, and RNG were retained.");

                    Pcg32State nextShopRandom = workingShopRandom.CaptureState();
                    SimulationRandomStreamsState nextRandom = ReplaceShop(state.RandomState, nextShopRandom);
                    ActionExecutionState next = state.With(economy: spend.NextState, shop: generated.Shop,
                        characterPool: generated.Pool, randomState: nextRandom);
                    return ActionHandlerResult.Success(next,
                        nextShopRandom.Equals(state.RandomState.Shop) ? RandomStreamMask.None : RandomStreamMask.Shop);
                }

                if (context.ItemDefinitions == null)
                    return ActionHandlerResult.ConfigurationError("ItemDefinitions are unavailable.");
                Pcg32RandomSource itemRandom = Pcg32RandomSource.FromState(state.RandomState.Shop);
                ItemShopGenerationResult itemGeneration = ItemShopRules.Generate(state.Shop,
                    state.CharacterPool, context.ItemDefinitions, state.Shop.StageNumber,
                    state.Shop.RoundNumber, state.Shop.SlotCount, state.Shop.GenerationIndex + 1,
                    itemRandom);
                if (!PoolEquals(state.CharacterPool, itemGeneration.NextCharacterPool))
                    return ActionHandlerResult.InternalError("ItemShopRules unexpectedly changed CharacterPool.");

                SimulationRandomStreamsState itemRandomState = ReplaceShop(state.RandomState,
                    itemGeneration.NextShopRandomState);
                ActionExecutionState itemNext = state.With(economy: spend.NextState,
                    shop: itemGeneration.NextShop, randomState: itemRandomState);
                return ActionHandlerResult.Success(itemNext,
                    itemGeneration.NextShopRandomState.Equals(state.RandomState.Shop)
                        ? RandomStreamMask.None : RandomStreamMask.Shop);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException ||
                ex is KeyNotFoundException || ex is OverflowException)
            {
                return ActionHandlerResult.ConfigurationError("Shop reroll failed before commit: " + ex.Message);
            }
        }

        private static bool HasEmptyCharacterOffer(ShopState shop)
        {
            foreach (ShopOffer offer in shop.Offers)
                if (offer.IsEmpty) return true;
            return false;
        }

        private static bool PoolEquals(CharacterPool left, CharacterPool right)
        {
            if (left == null || right == null || left.Entries.Count != right.Entries.Count) return false;
            foreach (KeyValuePair<string, CharacterPoolEntry> pair in left.Entries)
            {
                if (!right.Entries.TryGetValue(pair.Key, out CharacterPoolEntry entry)) return false;
                if (entry.RemainingCopies != pair.Value.RemainingCopies || entry.ReservedCopies != pair.Value.ReservedCopies ||
                    entry.OwnedCopies != pair.Value.OwnedCopies || entry.InitialStockLimit != pair.Value.InitialStockLimit) return false;
            }
            return true;
        }

        private static SimulationRandomStreamsState ReplaceShop(SimulationRandomStreamsState state, Pcg32State shop)
        {
            return new SimulationRandomStreamsState(state.TrialSeed, state.Version,
                shop, state.Stats, state.Encounter, state.Battle, state.Event, state.Roster, state.Economy);
        }

        private static ActionErrorCode Map(EconomyActionFailureReason reason) =>
            reason == EconomyActionFailureReason.InsufficientGold ? ActionErrorCode.InsufficientGold : ActionErrorCode.InvalidState;
    }

    public sealed class BuyExperienceActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.BuyExperience;

        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action,
            ActionExecutionContext context)
        {
            if (!(action is BuyExperienceAction))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected BuyExperienceAction.");
            if (state.Stage.CurrentSelection == null)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidState, "No active stage selection is available.");
            if (context.EconomyConfig == null)
                return ActionHandlerResult.ConfigurationError("EconomyRulesConfiguration is unavailable.");

            EconomyActionResult purchase = EconomyRules.TryPurchaseExperience(state.Economy,
                context.EconomyConfig, state.Stage.CurrentSelection.Choice == StageChoice.SelectB);
            if (!purchase.Succeeded)
            {
                ActionErrorCode code = purchase.FailureReason == EconomyActionFailureReason.InsufficientGold
                    ? ActionErrorCode.InsufficientGold
                    : purchase.FailureReason == EconomyActionFailureReason.ExperienceCurveMismatch
                        ? ActionErrorCode.ConfigurationError : ActionErrorCode.InvalidAction;
                if (code == ActionErrorCode.ConfigurationError)
                    return ActionHandlerResult.ConfigurationError("XP configuration does not match EconomyState.");
                return ActionHandlerResult.Reject(code, "XP purchase rejected by EconomyRules: " + purchase.FailureReason + ".");
            }

            return ActionHandlerResult.Success(state.With(economy: purchase.NextState));
        }
    }
}
