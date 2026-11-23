using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;

namespace ProjectNST.GameCore.Shop
{
    public enum ItemHandPurchaseFailure
    {
        None,
        InvalidInput,
        ItemShopInactive,
        InvalidOffer,
        MissingDefinition,
        PriceMismatch,
        InsufficientGold,
        InvalidHandSlot,
        HandSlotOccupied
    }

    /// <summary>Atomic proposal for buying one item directly into one empty Hand slot.</summary>
    public sealed class ItemHandPurchaseResult
    {
        public bool Succeeded { get; }
        public ItemHandPurchaseFailure Failure { get; }
        public EconomyState NextEconomy { get; }
        public ShopState NextShop { get; }
        public HandState NextHand { get; }
        public string PurchasedItemDefinitionId { get; }

        internal ItemHandPurchaseResult(bool succeeded, ItemHandPurchaseFailure failure,
            EconomyState economy, ShopState shop, HandState hand, string purchasedItemDefinitionId)
        {
            Succeeded = succeeded;
            Failure = failure;
            NextEconomy = economy;
            NextShop = shop;
            NextHand = hand;
            PurchasedItemDefinitionId = purchasedItemDefinitionId;
        }
    }

    public enum ItemHandSaleFailure
    {
        None,
        InvalidInput,
        InvalidHandSlot,
        SlotDoesNotContainItem
    }

    /// <summary>Item sale removes its Hand entry and returns zero Gold; there is no item pool.</summary>
    public sealed class ItemHandSaleResult
    {
        public bool Succeeded { get; }
        public ItemHandSaleFailure Failure { get; }
        public EconomyState NextEconomy { get; }
        public HandState NextHand { get; }
        public string SoldItemDefinitionId { get; }
        public int GoldReturned => 0;

        internal ItemHandSaleResult(bool succeeded, ItemHandSaleFailure failure,
            EconomyState economy, HandState hand, string soldItemDefinitionId)
        {
            Succeeded = succeeded;
            Failure = failure;
            NextEconomy = economy;
            NextHand = hand;
            SoldItemDefinitionId = soldItemDefinitionId;
        }
    }

    /// <summary>Combines the existing ItemShopRules with immutable HandSlot state transitions.</summary>
    public static class ItemHandTransactionRules
    {
        public static ItemHandPurchaseResult TryPurchaseIntoHand(ShopState shop, string offerId,
            IReadOnlyList<ItemDefinition> definitions, EconomyState economy,
            HandState hand, int handSlotIndex)
        {
            if (shop == null || definitions == null || economy == null || hand == null)
                return PurchaseFailure(ItemHandPurchaseFailure.InvalidInput, economy, shop, hand);
            if (shop.Kind != ShopKind.Item)
                return PurchaseFailure(ItemHandPurchaseFailure.ItemShopInactive, economy, shop, hand);

            // Reuse the canonical offer, price, definition, and Gold validation.
            ItemPurchaseResult purchase = ItemShopRules.TryPurchaseItem(shop, offerId, definitions, economy);
            if (!purchase.Succeeded)
                return PurchaseFailure(MapFailure(purchase.FailureReason), economy, shop, hand);

            if (!hand.TryGetSlot(handSlotIndex, out HandSlotContent destination))
                return PurchaseFailure(ItemHandPurchaseFailure.InvalidHandSlot, economy, shop, hand);
            if (!destination.IsEmpty)
                return PurchaseFailure(ItemHandPurchaseFailure.HandSlotOccupied, economy, shop, hand);
            if (!hand.TryWithSlot(handSlotIndex,
                    HandSlotContent.Item(purchase.PurchasedItem.DefinitionId), out HandState nextHand))
                return PurchaseFailure(ItemHandPurchaseFailure.InvalidHandSlot, economy, shop, hand);

            return new ItemHandPurchaseResult(true, ItemHandPurchaseFailure.None,
                purchase.NextEconomy, purchase.NextShop, nextHand,
                purchase.PurchasedItem.DefinitionId);
        }

        public static ItemHandSaleResult TrySellFromHand(HandState hand,
            EconomyState economy, int handSlotIndex)
        {
            if (hand == null || economy == null)
                return SaleFailure(ItemHandSaleFailure.InvalidInput, hand, economy);
            if (!hand.TryGetSlot(handSlotIndex, out HandSlotContent content))
                return SaleFailure(ItemHandSaleFailure.InvalidHandSlot, hand, economy);
            if (content.Kind != HandSlotContentKind.Item)
                return SaleFailure(ItemHandSaleFailure.SlotDoesNotContainItem, hand, economy);
            if (!hand.TryWithSlot(handSlotIndex, HandSlotContent.Empty(), out HandState nextHand))
                return SaleFailure(ItemHandSaleFailure.InvalidHandSlot, hand, economy);

            return new ItemHandSaleResult(true, ItemHandSaleFailure.None,
                economy, nextHand, content.ItemDefinitionId);
        }

        private static ItemHandPurchaseFailure MapFailure(ItemPurchaseFailureReason failure)
        {
            switch (failure)
            {
                case ItemPurchaseFailureReason.InvalidOffer: return ItemHandPurchaseFailure.InvalidOffer;
                case ItemPurchaseFailureReason.MissingDefinition: return ItemHandPurchaseFailure.MissingDefinition;
                case ItemPurchaseFailureReason.PriceMismatch: return ItemHandPurchaseFailure.PriceMismatch;
                case ItemPurchaseFailureReason.InsufficientGold: return ItemHandPurchaseFailure.InsufficientGold;
                default: return ItemHandPurchaseFailure.InvalidInput;
            }
        }

        private static ItemHandPurchaseResult PurchaseFailure(ItemHandPurchaseFailure failure,
            EconomyState economy, ShopState shop, HandState hand)
        {
            return new ItemHandPurchaseResult(false, failure, economy, shop, hand, null);
        }

        private static ItemHandSaleResult SaleFailure(ItemHandSaleFailure failure,
            HandState hand, EconomyState economy)
        {
            return new ItemHandSaleResult(false, failure, economy, hand, null);
        }
    }
}
