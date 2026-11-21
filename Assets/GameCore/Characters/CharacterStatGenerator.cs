using System;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Characters
{
    /// <summary>Reproduces the existing one-roll weighted selection of an ATK/DEF pair.</summary>
    public static class CharacterStatGenerator
    {
        public static bool TryGenerate(CharacterStatDefinition definition, int runCost, int star,
            IRandomSource random, out int attack, out int defense)
        {
            attack = 0;
            defense = 0;
            if (definition == null || random == null) return false;
            if (!definition.TryGetOptions(runCost, star, out var options) || options == null || options.Count == 0)
                return false;

            long totalWeight = 0;
            for (int i = 0; i < options.Count; i++)
            {
                int weight = options[i].Weight;
                if (weight < 0) return false;
                if (weight > 0) totalWeight += weight;
            }
            if (totalWeight <= 0 || totalWeight > int.MaxValue) return false;

            // Match CharacterStatSettings: one integer draw across all positive weights.
            int roll = random.NextInt(0, (int)totalWeight);
            for (int i = 0; i < options.Count; i++)
            {
                CharacterStatOption option = options[i];
                if (option.Weight <= 0) continue;
                if (roll < option.Weight)
                {
                    attack = option.Attack;
                    defense = option.Defense;
                    return true;
                }
                roll -= option.Weight;
            }
            return false;
        }
    }
}
