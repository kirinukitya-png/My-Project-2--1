using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>
    /// Pure GameCore shop transitions. Inputs are never mutated; callers receive a proposed
    /// replacement ShopState and CharacterPool for GameSession to apply.
    /// </summary>
    public static class CharacterShopRules
    {
        public static CharacterPool CreateInitialPool(IReadOnlyList<CharacterDefinition> definitions,
            AssignedCharacterCosts assignedCosts, CharacterShopConfiguration configuration)
        {
            ValidateRoster(definitions, assignedCosts, configuration, null);
            var entries = new List<CharacterPoolEntry>(definitions.Count);
            foreach (CharacterDefinition definition in definitions)
            {
                int cost = assignedCosts.GetCost(definition.DefinitionId);
                int stock = configuration.GetInitialPoolCount(cost);
                entries.Add(new CharacterPoolEntry(definition.DefinitionId, stock, stock));
            }
            return new CharacterPool(entries);
        }

        public static CharacterShopGenerationResult Generate(IReadOnlyList<CharacterDefinition> definitions,
            AssignedCharacterCosts assignedCosts, CharacterPool pool, CharacterShopConfiguration configuration,
            int stageNumber, int roundNumber, int generationIndex, IRandomSource shopRandom)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (shopRandom == null) throw new ArgumentNullException(nameof(shopRandom));
            if (stageNumber < 1) throw new ArgumentOutOfRangeException(nameof(stageNumber));
            if (roundNumber < 1) throw new ArgumentOutOfRangeException(nameof(roundNumber));
            if (generationIndex < 0) throw new ArgumentOutOfRangeException(nameof(generationIndex));
            ValidateRoster(definitions, assignedCosts, configuration, pool);

            int slotCount = configuration.GetShopSlotCount(stageNumber);
            return GenerateUsingPool(definitions, assignedCosts, pool, configuration,
                stageNumber, roundNumber, generationIndex, slotCount, shopRandom);
        }

        public static CharacterShopGenerationResult Refresh(ShopState currentShop,
            IReadOnlyList<CharacterDefinition> definitions, AssignedCharacterCosts assignedCosts,
            CharacterPool pool, CharacterShopConfiguration configuration, IRandomSource shopRandom)
        {
            if (currentShop == null) throw new ArgumentNullException(nameof(currentShop));
            if (currentShop.Kind != ShopKind.Character) throw new ArgumentException("Only a character shop can be refreshed here.", nameof(currentShop));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (shopRandom == null) throw new ArgumentNullException(nameof(shopRandom));
            if (currentShop.GenerationIndex == int.MaxValue) throw new InvalidOperationException("Shop generation index is exhausted.");
            ValidateRoster(definitions, assignedCosts, configuration, pool);

            CharacterPool returnedPool = ReturnUnpurchasedOffers(currentShop, pool);
            return GenerateUsingPool(definitions, assignedCosts, returnedPool, configuration,
                currentShop.StageNumber, currentShop.RoundNumber, currentShop.GenerationIndex + 1,
                currentShop.SlotCount, shopRandom);
        }

        public static CharacterPool ReturnUnpurchasedOffers(ShopState currentShop, CharacterPool pool)
        {
            if (currentShop == null) throw new ArgumentNullException(nameof(currentShop));
            if (currentShop.Kind != ShopKind.Character) return pool ?? throw new ArgumentNullException(nameof(pool));
            if (pool == null) throw new ArgumentNullException(nameof(pool));

            var working = CopyEntries(pool);
            foreach (ShopOffer offer in currentShop.Offers)
            {
                if (offer.IsEmpty || offer.Status == ShopOfferStatus.Purchased) continue;

                CharacterPoolEntry entry = working[offer.DefinitionId];
                if (entry.ReservedCopies == 0)
                    throw new InvalidOperationException("An available offer has no reserved copy: " + offer.OfferId);
                working[offer.DefinitionId] = new CharacterPoolEntry(entry.DefinitionId,
                    checked(entry.RemainingCopies + 1), entry.InitialStockLimit,
                    entry.ReservedCopies - 1, entry.OwnedCopies);
            }
            return new CharacterPool(working.Values);
        }

        private static CharacterShopGenerationResult GenerateUsingPool(IReadOnlyList<CharacterDefinition> definitions,
            AssignedCharacterCosts assignedCosts, CharacterPool pool, CharacterShopConfiguration configuration,
            int stageNumber, int roundNumber, int generationIndex, int slotCount, IRandomSource random)
        {
            var entries = CopyEntries(pool);
            var offers = new List<ShopOffer>(slotCount);

            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                string offerId = CreateOfferId(stageNumber, roundNumber, generationIndex, slotIndex);
                int selectedCost = SelectCost(definitions, assignedCosts, entries, configuration, stageNumber, random);
                if (selectedCost == 0)
                {
                    offers.Add(ShopOffer.CreateEmpty(offerId));
                    continue;
                }

                CharacterDefinition selected = SelectCharacter(definitions, assignedCosts, entries, selectedCost, random);
                CharacterPoolEntry entry = entries[selected.DefinitionId];
                entries[selected.DefinitionId] = new CharacterPoolEntry(entry.DefinitionId,
                    entry.RemainingCopies - 1, entry.InitialStockLimit,
                    checked(entry.ReservedCopies + 1), entry.OwnedCopies);
                int cost = assignedCosts.GetCost(selected.DefinitionId);
                offers.Add(new ShopOffer(offerId, ShopOfferKind.Character, selected.DefinitionId,
                    cost, cost, ShopOfferStatus.Available));
            }

            var nextShop = new ShopState(ShopKind.Character, offers, slotCount,
                stageNumber, roundNumber, false, generationIndex);
            var nextPool = new CharacterPool(entries.Values);
            return new CharacterShopGenerationResult(nextShop, nextPool);
        }

        private static int SelectCost(IReadOnlyList<CharacterDefinition> definitions,
            AssignedCharacterCosts assignedCosts, Dictionary<string, CharacterPoolEntry> entries,
            CharacterShopConfiguration configuration, int stageNumber, IRandomSource random)
        {
            var eligible = new int[5];
            int totalWeight = 0;
            for (int cost = 1; cost <= 5; cost++)
            {
                int weight = configuration.GetCostWeight(stageNumber, cost);
                if (weight <= 0 || !HasAvailableCharacter(definitions, assignedCosts, entries, cost)) continue;
                eligible[cost - 1] = weight;
                totalWeight = checked(totalWeight + weight);
            }
            if (totalWeight == 0) return 0;

            // Drawing against the remaining integer weight is equivalent to normalizing
            // eligible Cost probabilities to 100%, without rounding fractional weights.
            int roll = random.NextInt(0, totalWeight);
            for (int cost = 1; cost <= 5; cost++)
            {
                int weight = eligible[cost - 1];
                if (weight == 0) continue;
                if (roll < weight) return cost;
                roll -= weight;
            }
            throw new InvalidOperationException("Cost weight selection failed despite a positive eligible total.");
        }

        private static CharacterDefinition SelectCharacter(IReadOnlyList<CharacterDefinition> definitions,
            AssignedCharacterCosts assignedCosts, Dictionary<string, CharacterPoolEntry> entries, int cost,
            IRandomSource random)
        {
            int totalCopies = 0;
            foreach (CharacterDefinition definition in definitions)
            {
                if (assignedCosts.GetCost(definition.DefinitionId) != cost) continue;
                totalCopies = checked(totalCopies + entries[definition.DefinitionId].RemainingCopies);
            }
            if (totalCopies <= 0) throw new InvalidOperationException("Selected Cost tier has no available character copies.");

            int roll = random.NextInt(0, totalCopies);
            foreach (CharacterDefinition definition in definitions)
            {
                if (assignedCosts.GetCost(definition.DefinitionId) != cost) continue;
                int copies = entries[definition.DefinitionId].RemainingCopies;
                if (roll < copies) return definition;
                roll -= copies;
            }
            throw new InvalidOperationException("Character copy selection failed despite positive inventory.");
        }

        private static bool HasAvailableCharacter(IReadOnlyList<CharacterDefinition> definitions,
            AssignedCharacterCosts assignedCosts, Dictionary<string, CharacterPoolEntry> entries, int cost)
        {
            foreach (CharacterDefinition definition in definitions)
                if (assignedCosts.GetCost(definition.DefinitionId) == cost && entries[definition.DefinitionId].RemainingCopies > 0)
                    return true;
            return false;
        }

        private static void ValidateRoster(IReadOnlyList<CharacterDefinition> definitions,
            AssignedCharacterCosts assignedCosts, CharacterShopConfiguration configuration, CharacterPool pool)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (assignedCosts == null) throw new ArgumentNullException(nameof(assignedCosts));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (assignedCosts.Count != definitions.Count) throw new ArgumentException("Every selected character must have exactly one Cost assignment.", nameof(assignedCosts));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var countsByCost = new int[5];
            foreach (CharacterDefinition definition in definitions)
            {
                if (definition == null) throw new ArgumentException("Roster cannot contain null definitions.", nameof(definitions));
                if (!ids.Add(definition.DefinitionId)) throw new ArgumentException("Roster DefinitionIds must be unique.", nameof(definitions));
                countsByCost[assignedCosts.GetCost(definition.DefinitionId) - 1]++;
                if (pool != null && !pool.Entries.ContainsKey(definition.DefinitionId))
                    throw new ArgumentException("Character pool is missing " + definition.DefinitionId + ".", nameof(pool));
            }
            for (int cost = 1; cost <= 5; cost++)
                if (countsByCost[cost - 1] != configuration.GetCharactersPerCost(cost))
                    throw new ArgumentException("Assigned Cost counts do not match the configured roster distribution.", nameof(assignedCosts));
            if (pool != null && pool.Entries.Count != definitions.Count)
                throw new ArgumentException("Character pool contains entries outside the selected roster.", nameof(pool));
        }

        private static Dictionary<string, CharacterPoolEntry> CopyEntries(CharacterPool pool)
        {
            var copy = new Dictionary<string, CharacterPoolEntry>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, CharacterPoolEntry> pair in pool.Entries) copy.Add(pair.Key, pair.Value);
            return copy;
        }

        private static string CreateOfferId(int stage, int round, int generation, int slot)
        {
            return "character-s" + stage + "-r" + round + "-g" + generation + "-slot" + slot;
        }
    }
}
