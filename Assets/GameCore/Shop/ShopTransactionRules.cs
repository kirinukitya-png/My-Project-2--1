using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>Purchase and sale calculations. Every method returns proposed replacements and leaves inputs untouched.</summary>
    public static class ShopTransactionRules
    {
        public static CharacterPurchaseResult TryPurchaseCharacter(ShopState shop, string offerId,
            IReadOnlyList<CharacterDefinition> definitions, AssignedCharacterCosts assignedCosts,
            EconomyState economy, CharacterPool pool, CharacterInstanceFactory instanceFactory,
            CharacterStatDefinition statSettings, Pcg32RandomSource statsRandom,
            ICharacterPlacementApproval placementApproval)
        {
            if (shop == null) throw new ArgumentNullException(nameof(shop));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (assignedCosts == null) throw new ArgumentNullException(nameof(assignedCosts));
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (instanceFactory == null) throw new ArgumentNullException(nameof(instanceFactory));
            if (statsRandom == null) throw new ArgumentNullException(nameof(statsRandom));

            Pcg32State originalStatsState = statsRandom.CaptureState();
            if (shop.Kind != ShopKind.Character || !TryFindOffer(shop, offerId, out ShopOffer offer) ||
                offer.Kind != ShopOfferKind.Character || offer.Status != ShopOfferStatus.Available)
                return PurchaseFailure(CharacterPurchaseFailureReason.InvalidOffer, economy, shop, pool, instanceFactory, originalStatsState);

            CharacterDefinition definition = null;
            foreach (CharacterDefinition candidate in definitions)
            {
                if (candidate == null || candidate.DefinitionId != offer.DefinitionId) continue;
                if (definition != null)
                    return PurchaseFailure(CharacterPurchaseFailureReason.MissingDefinition, economy, shop, pool, instanceFactory, originalStatsState);
                definition = candidate;
            }
            if (definition == null || !assignedCosts.Costs.TryGetValue(offer.DefinitionId, out int runCost))
                return PurchaseFailure(CharacterPurchaseFailureReason.MissingDefinition, economy, shop, pool, instanceFactory, originalStatsState);
            if (offer.CharacterCost != runCost || offer.DisplayPrice != runCost)
                return PurchaseFailure(CharacterPurchaseFailureReason.CostMismatch, economy, shop, pool, instanceFactory, originalStatsState);
            if (economy.Gold < runCost)
                return PurchaseFailure(CharacterPurchaseFailureReason.InsufficientGold, economy, shop, pool, instanceFactory, originalStatsState);
            if (!pool.Entries.TryGetValue(offer.DefinitionId, out CharacterPoolEntry poolEntry) || poolEntry.ReservedCopies < 1)
                return PurchaseFailure(CharacterPurchaseFailureReason.MissingReservation, economy, shop, pool, instanceFactory, originalStatsState);
            if (statSettings == null || placementApproval == null)
                return PurchaseFailure(statSettings == null ? CharacterPurchaseFailureReason.InvalidStatsConfiguration : CharacterPurchaseFailureReason.PlacementNotApproved,
                    economy, shop, pool, instanceFactory, originalStatsState);
            if (instanceFactory.LastIssuedNumber == long.MaxValue)
                return PurchaseFailure(CharacterPurchaseFailureReason.InstanceIdExhausted, economy, shop, pool, instanceFactory, originalStatsState);

            // Use transaction-local copies. Failed stat generation or placement approval discards both.
            CharacterInstanceFactory nextFactory = instanceFactory.CreateFork();
            Pcg32RandomSource nextStatsRandom = Pcg32RandomSource.FromState(originalStatsState);
            CharacterInstance instance = nextFactory.Create(definition, runCost);
            if (!instance.EnsureStats(definition, statSettings, nextStatsRandom))
                return PurchaseFailure(CharacterPurchaseFailureReason.InvalidStatsConfiguration, economy, shop, pool, instanceFactory, originalStatsState);
            if (!placementApproval.CanAcceptPurchasedCharacter(instance))
                return PurchaseFailure(CharacterPurchaseFailureReason.PlacementNotApproved, economy, shop, pool, instanceFactory, originalStatsState);

            var nextPool = ReplacePoolEntry(pool, new CharacterPoolEntry(poolEntry.DefinitionId,
                poolEntry.RemainingCopies, poolEntry.InitialStockLimit,
                poolEntry.ReservedCopies - 1, checked(poolEntry.OwnedCopies + 1)));
            ShopState nextShop = shop.WithReplacedOffer(new ShopOffer(offer.OfferId, offer.Kind,
                offer.DefinitionId, offer.DisplayPrice, offer.CharacterCost, ShopOfferStatus.Purchased));
            EconomyState nextEconomy = economy.WithGold(economy.Gold - runCost);
            return new CharacterPurchaseResult(true, CharacterPurchaseFailureReason.None,
                nextEconomy, nextShop, nextPool, instance, nextFactory, nextStatsRandom.CaptureState());
        }

        public static CharacterSaleResult TrySellCharacter(CharacterInstance target,
            IReadOnlyList<CharacterInstance> ownedCharacters, EconomyState economy, CharacterPool pool)
        {
            if (ownedCharacters == null) throw new ArgumentNullException(nameof(ownedCharacters));
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (target == null)
                return SaleFailure(CharacterSaleFailureReason.InvalidTarget, economy, pool, ownedCharacters);

            int foundIndex = -1;
            for (int i = 0; i < ownedCharacters.Count; i++)
            {
                CharacterInstance candidate = ownedCharacters[i];
                if (candidate == null || candidate.InstanceId != target.InstanceId) continue;
                if (foundIndex >= 0)
                    return SaleFailure(CharacterSaleFailureReason.AmbiguousOwnedId, economy, pool, ownedCharacters);
                foundIndex = i;
                if (candidate.DefinitionId != target.DefinitionId || candidate.RunCost != target.RunCost ||
                    candidate.Star != target.Star || candidate.PoolCopies != target.PoolCopies)
                    return SaleFailure(CharacterSaleFailureReason.NotOwned, economy, pool, ownedCharacters);
            }
            if (foundIndex < 0) return SaleFailure(CharacterSaleFailureReason.NotOwned, economy, pool, ownedCharacters);
            if (!pool.Entries.TryGetValue(target.DefinitionId, out CharacterPoolEntry entry))
                return SaleFailure(CharacterSaleFailureReason.PoolEntryMissing, economy, pool, ownedCharacters);

            int returnedCopies = target.PoolCopies;
            int saleValueCopies = CopiesForStar(target.Star);
            if (entry.OwnedCopies < returnedCopies)
                return SaleFailure(CharacterSaleFailureReason.InsufficientOwnedCopies, economy, pool, ownedCharacters);

            long goldGainLong = (long)target.RunCost * saleValueCopies;
            long goldAfterLong = (long)economy.Gold + goldGainLong;
            if (goldGainLong > int.MaxValue || goldAfterLong > int.MaxValue)
                return SaleFailure(CharacterSaleFailureReason.GoldOverflow, economy, pool, ownedCharacters);

            var nextPool = ReplacePoolEntry(pool, new CharacterPoolEntry(entry.DefinitionId,
                checked(entry.RemainingCopies + returnedCopies), entry.InitialStockLimit,
                entry.ReservedCopies, entry.OwnedCopies - returnedCopies));
            var nextOwned = new List<CharacterInstance>(ownedCharacters.Count - 1);
            for (int i = 0; i < ownedCharacters.Count; i++)
                if (i != foundIndex) nextOwned.Add(ownedCharacters[i]);

            return new CharacterSaleResult(true, CharacterSaleFailureReason.None,
                economy.WithGold((int)goldAfterLong), nextPool, Array.AsReadOnly(nextOwned.ToArray()),
                returnedCopies, (int)goldGainLong);
        }

        private static int CopiesForStar(int star)
        {
            switch (star)
            {
                case 1: return 1;
                case 2: return 3;
                case 3: return 9;
                default: throw new ArgumentOutOfRangeException(nameof(star));
            }
        }

        private static bool TryFindOffer(ShopState shop, string offerId, out ShopOffer offer)
        {
            offer = null;
            if (string.IsNullOrWhiteSpace(offerId)) return false;
            foreach (ShopOffer candidate in shop.Offers)
                if (candidate.OfferId == offerId) { offer = candidate; return true; }
            return false;
        }

        private static CharacterPool ReplacePoolEntry(CharacterPool pool, CharacterPoolEntry replacement)
        {
            var entries = new List<CharacterPoolEntry>(pool.Entries.Count);
            foreach (CharacterPoolEntry entry in pool.Entries.Values)
                entries.Add(entry.DefinitionId == replacement.DefinitionId ? replacement : entry);
            return new CharacterPool(entries);
        }

        private static CharacterPurchaseResult PurchaseFailure(CharacterPurchaseFailureReason reason,
            EconomyState economy, ShopState shop, CharacterPool pool, CharacterInstanceFactory factory, Pcg32State statsState)
        {
            return new CharacterPurchaseResult(false, reason, economy, shop, pool, null, factory, statsState);
        }

        private static CharacterSaleResult SaleFailure(CharacterSaleFailureReason reason,
            EconomyState economy, CharacterPool pool, IReadOnlyList<CharacterInstance> ownedCharacters)
        {
            return new CharacterSaleResult(false, reason, economy, pool, ownedCharacters, 0, 0);
        }
    }
}
