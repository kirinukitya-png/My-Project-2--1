using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;

namespace ProjectNST.GameUnityAdapter
{
    /// <summary>Copies the active Unity stat table into an immutable GameCore definition.</summary>
    public static class CharacterStatSettingsMapper
    {
        public static CharacterStatDefinition ToDefinition(CharacterStatSettings source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copiedCosts = new List<CharacterCostStats>();
            if (source.costs != null)
            {
                foreach (CharacterStatSettings.CostStats cost in source.costs)
                {
                    if (cost == null) continue;
                    copiedCosts.Add(new CharacterCostStats(cost.cost,
                        CopyOptions(cost.star1Options), CopyOptions(cost.star2Options), CopyOptions(cost.star3Options)));
                }
            }
            return new CharacterStatDefinition(copiedCosts);
        }

        private static IEnumerable<CharacterStatOption> CopyOptions(CharacterStatSettings.StarStats[] source)
        {
            var copied = new List<CharacterStatOption>();
            if (source != null)
            {
                foreach (CharacterStatSettings.StarStats option in source)
                {
                    // Legacy weighted roll skips null and zero-weight entries.
                    if (option != null) copied.Add(new CharacterStatOption(option.attack, option.defense, option.weight));
                }
            }
            return copied;
        }
    }
}
