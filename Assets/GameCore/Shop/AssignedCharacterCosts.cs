using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>Immutable per-run Cost assignment; never modifies CharacterDefinition.</summary>
    public sealed class AssignedCharacterCosts
    {
        private readonly ReadOnlyDictionary<string, int> _costs;
        public IReadOnlyDictionary<string, int> Costs => _costs;
        public int Count => _costs.Count;

        public AssignedCharacterCosts(IEnumerable<KeyValuePair<string, int>> costs)
        {
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            var copied = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in costs)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) throw new ArgumentException("DefinitionId cannot be empty.", nameof(costs));
                if (pair.Value < 1 || pair.Value > 5) throw new ArgumentOutOfRangeException(nameof(costs), "Assigned Cost must be between 1 and 5.");
                if (copied.ContainsKey(pair.Key)) throw new ArgumentException("DefinitionId values must be unique.", nameof(costs));
                copied.Add(pair.Key, pair.Value);
            }
            _costs = new ReadOnlyDictionary<string, int>(copied);
        }

        public int GetCost(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("DefinitionId cannot be empty.", nameof(definitionId));
            if (!_costs.TryGetValue(definitionId, out int cost)) throw new KeyNotFoundException("No Cost assignment exists for " + definitionId + ".");
            return cost;
        }
    }
}
