using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>Assigns run-specific Cost tiers to a chosen roster using a dedicated Roster RNG.</summary>
    public static class CharacterCostAssigner
    {
        public static AssignedCharacterCosts Assign(IReadOnlyList<CharacterDefinition> definitions,
            CharacterShopConfiguration configuration, IRandomSource rosterRandom)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (rosterRandom == null) throw new ArgumentNullException(nameof(rosterRandom));

            var shuffled = new List<CharacterDefinition>(definitions.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < definitions.Count; i++)
            {
                CharacterDefinition definition = definitions[i];
                if (definition == null) throw new ArgumentException("Roster cannot contain null definitions.", nameof(definitions));
                if (!seenIds.Add(definition.DefinitionId)) throw new ArgumentException("Roster DefinitionIds must be unique.", nameof(definitions));
                shuffled.Add(definition);
            }

            int expectedCount = 0;
            for (int cost = 1; cost <= 5; cost++) expectedCount += configuration.GetCharactersPerCost(cost);
            if (expectedCount != shuffled.Count)
                throw new ArgumentException("Cost allocation counts must exactly match the selected roster size.", nameof(configuration));

            rosterRandom.Shuffle(shuffled);
            var assignments = new List<KeyValuePair<string, int>>(shuffled.Count);
            int index = 0;
            for (int cost = 1; cost <= 5; cost++)
                for (int count = 0; count < configuration.GetCharactersPerCost(cost); count++)
                    assignments.Add(new KeyValuePair<string, int>(shuffled[index++].DefinitionId, cost));

            return new AssignedCharacterCosts(assignments);
        }
    }
}
