using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Characters
{
    /// <summary>Unity-independent, copied character definition.</summary>
    public sealed class CharacterDefinition
    {
        private readonly string[] _attributes;
        private readonly string[] _units;

        public string DefinitionId { get; }
        public string CharacterName { get; }
        public int BaseCost { get; }
        public IReadOnlyList<string> Attributes { get; }
        public IReadOnlyList<string> Units { get; }

        public CharacterDefinition(string definitionId, string characterName, int baseCost,
            IEnumerable<string> attributes, IEnumerable<string> units)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("A stable DefinitionId is required.", nameof(definitionId));
            if (baseCost < 1)
                throw new ArgumentOutOfRangeException(nameof(baseCost), "BaseCost must be at least 1.");

            DefinitionId = definitionId;
            CharacterName = characterName ?? string.Empty;
            BaseCost = baseCost;
            _attributes = Copy(attributes);
            _units = Copy(units);
            Attributes = Array.AsReadOnly(_attributes);
            Units = Array.AsReadOnly(_units);
        }

        private static string[] Copy(IEnumerable<string> values)
        {
            if (values == null) return Array.Empty<string>();
            return new List<string>(values).ToArray();
        }
    }
}
