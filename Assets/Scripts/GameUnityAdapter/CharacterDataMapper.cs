using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;

namespace ProjectNST.GameUnityAdapter
{
    /// <summary>Copies Unity CharacterData into GameCore. Never writes to the ScriptableObject.</summary>
    public static class CharacterDataMapper
    {
        public static CharacterDefinition ToDefinition(CharacterData source, string stableDefinitionId)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new CharacterDefinition(stableDefinitionId, source.characterName, source.cost,
                CopyStrings(source.attributes), CopyStrings(source.units));
        }

        private static IEnumerable<string> CopyStrings(string[] source)
        {
            return source == null ? Array.Empty<string>() : (string[])source.Clone();
        }
    }
}
