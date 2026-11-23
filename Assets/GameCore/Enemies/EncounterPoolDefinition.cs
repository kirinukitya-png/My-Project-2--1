using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Enemies
{
    /// <summary>Immutable pool copied from EncounterPoolData. Formation duplicates are intentional weights.</summary>
    public sealed class EncounterPoolDefinition
    {
        private readonly ReadOnlyCollection<int> _roundIndices;
        private readonly ReadOnlyCollection<EnemyFormationDefinition> _formations;

        public string PoolId { get; }
        public IReadOnlyList<int> RoundIndices => _roundIndices;
        public StageEnemyCategory Category { get; }
        public IReadOnlyList<EnemyFormationDefinition> Formations => _formations;

        public EncounterPoolDefinition(string poolId, IEnumerable<int> roundIndices,
            StageEnemyCategory category, IEnumerable<EnemyFormationDefinition> formations)
        {
            if (string.IsNullOrWhiteSpace(poolId))
                throw new ArgumentException("Encounter pool ID is required.", nameof(poolId));
            if (category != StageEnemyCategory.Normal && category != StageEnemyCategory.Hard &&
                category != StageEnemyCategory.Boss)
                throw new ArgumentOutOfRangeException(nameof(category), "Pools must use Normal, Hard, or Boss category.");

            var rounds = roundIndices == null ? new List<int>() : new List<int>(roundIndices);
            for (int i = 0; i < rounds.Count; i++)
                if (rounds[i] < 0)
                    throw new ArgumentOutOfRangeException(nameof(roundIndices), "Round indices cannot be negative.");
            var candidates = formations == null
                ? new List<EnemyFormationDefinition>()
                : new List<EnemyFormationDefinition>(formations);
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i] == null)
                    throw new ArgumentException("Formation candidates cannot contain null entries.", nameof(formations));

            PoolId = poolId;
            Category = category;
            _roundIndices = rounds.AsReadOnly();
            _formations = candidates.AsReadOnly();
        }

        public bool ContainsRound(int roundIndex)
        {
            for (int i = 0; i < _roundIndices.Count; i++)
                if (_roundIndices[i] == roundIndex) return true;
            return false;
        }
    }
}
