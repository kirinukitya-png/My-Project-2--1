using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>Item display and purchase rules. Item offers have no finite pool or effect behavior.</summary>
    public static class ItemShopRules
    {
        public static ItemShopGenerationResult Generate(ShopState previousShop, CharacterPool characterPool,
            IReadOnlyList<ItemDefinition> definitions, int stageNumber, int roundNumber, int slotCount,
            int generationIndex, Pcg32RandomSource shopRandom)
        {
            if (characterPool == null) throw new ArgumentNullException(nameof(characterPool));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (shopRandom == null) throw new ArgumentNullException(nameof(shopRandom));
            if (stageNumber < 1) throw new ArgumentOutOfRangeException(nameof(stageNumber));
            if (roundNumber < 1) throw new ArgumentOutOfRangeException(nameof(roundNumber));
            if (slotCount < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            if (generationIndex < 0) throw new ArgumentOutOfRangeException(nameof(generationIndex));

            List<ItemDefinition> uniqueItems = CopyUniqueDefinitions(definitions);
            var nextRandom = Pcg32RandomSource.FromState(shopRandom.CaptureState());
            nextRandom.Shuffle(uniqueItems);

            CharacterPool nextPool = previousShop != null && previousShop.Kind == ShopKind.Character
                ? CharacterShopRules.ReturnUnpurchasedOffers(previousShop, characterPool)
                : characterPool;

            int count = Math.Min(slotCount, uniqueItems.Count);
            var offers = new List<ShopOffer>(count);
            for (int i = 0; i < count; i++)
            {
                ItemDefinition item = uniqueItems[i];
                string offerId = "item-s" + stageNumber + "-r" + roundNumber + "-g" + generationIndex + "-slot" + i;
                offers.Add(new ShopOffer(offerId, ShopOfferKind.Item, item.DefinitionId,
                    item.PurchasePrice, null, ShopOfferStatus.Available));
            }

            var nextShop = new ShopState(ShopKind.Item, offers, slotCount, stageNumber,
                roundNumber, false, generationIndex);
            return new ItemShopGenerationResult(nextShop, nextPool, nextRandom.CaptureState());
        }

        public static ItemPurchaseResult TryPurchaseItem(ShopState shop, string offerId,
            IReadOnlyList<ItemDefinition> definitions, EconomyState economy)
        {
            if (shop == null) throw new ArgumentNullException(nameof(shop));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            if (shop.Kind != ShopKind.Item || !TryFindOffer(shop, offerId, out ShopOffer offer) ||
                offer.Kind != ShopOfferKind.Item || offer.Status != ShopOfferStatus.Available)
                return ItemPurchaseFailure(ItemPurchaseFailureReason.InvalidOffer, economy, shop);

            ItemDefinition selected = null;
            foreach (ItemDefinition item in definitions)
            {
                if (item == null || item.DefinitionId != offer.DefinitionId) continue;
                if (selected != null && !selected.HasSameDefinitionData(item))
                    return ItemPurchaseFailure(ItemPurchaseFailureReason.MissingDefinition, economy, shop);
                selected = item;
            }
            if (selected == null) return ItemPurchaseFailure(ItemPurchaseFailureReason.MissingDefinition, economy, shop);
            if (offer.DisplayPrice != selected.PurchasePrice)
                return ItemPurchaseFailure(ItemPurchaseFailureReason.PriceMismatch, economy, shop);
            if (economy.Gold < selected.PurchasePrice)
                return ItemPurchaseFailure(ItemPurchaseFailureReason.InsufficientGold, economy, shop);

            ShopState nextShop = shop.WithReplacedOffer(new ShopOffer(offer.OfferId, offer.Kind,
                offer.DefinitionId, offer.DisplayPrice, null, ShopOfferStatus.Purchased));
            EconomyState nextEconomy = economy.WithGold(economy.Gold - selected.PurchasePrice);
            return new ItemPurchaseResult(true, ItemPurchaseFailureReason.None, nextEconomy, nextShop, selected);
        }

        private static List<ItemDefinition> CopyUniqueDefinitions(IReadOnlyList<ItemDefinition> definitions)
        {
            var unique = new List<ItemDefinition>(definitions.Count);
            var byId = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition item = definitions[i];
                if (item == null) continue;
                if (byId.TryGetValue(item.DefinitionId, out ItemDefinition existing))
                {
                    if (!existing.HasSameDefinitionData(item))
                        throw new ArgumentException("Conflicting ItemDefinitions share DefinitionId " + item.DefinitionId + ".", nameof(definitions));
                    continue;
                }
                byId.Add(item.DefinitionId, item);
                unique.Add(item);
            }
            return unique;
        }

        private static bool TryFindOffer(ShopState shop, string offerId, out ShopOffer offer)
        {
            offer = null;
            if (string.IsNullOrWhiteSpace(offerId)) return false;
            foreach (ShopOffer candidate in shop.Offers)
                if (candidate.OfferId == offerId) { offer = candidate; return true; }
            return false;
        }

        private static ItemPurchaseResult ItemPurchaseFailure(ItemPurchaseFailureReason reason,
            EconomyState economy, ShopState shop)
        {
            return new ItemPurchaseResult(false, reason, economy, shop, null);
        }
    }
}
