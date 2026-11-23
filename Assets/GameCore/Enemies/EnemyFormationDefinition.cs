using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProjectNST.GameCore.Enemies
{
    /// <summary>One immutable slot in a saved six-slot enemy formation.</summary>
    public sealed class EnemyFormationSlotDefinition
    {
        public string EnemyDefinitionId { get; }
        public bool HasEnemy => EnemyDefinitionId != null;
        public int Gap { get; }
        public int Cooldown { get; }

        public EnemyFormationSlotDefinition(string enemyDefinitionId, int gap, int cooldown)
        {
            if (enemyDefinitionId != null && string.IsNullOrWhiteSpace(enemyDefinitionId))
                throw new ArgumentException("Enemy definition ID must be null or non-empty.", nameof(enemyDefinitionId));
            if (gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));
            if (cooldown < 0) throw new ArgumentOutOfRangeException(nameof(cooldown));
            EnemyDefinitionId = enemyDefinitionId;
            Gap = gap;
            Cooldown = cooldown;
        }
    }

    /// <summary>Immutable formation copied from EnemyFormationData.</summary>
    public sealed class EnemyFormationDefinition
    {
        public const int RequiredSlotCount = 6;
        private readonly ReadOnlyCollection<EnemyFormationSlotDefinition> _slots;

        public string FormationId { get; }
        public IReadOnlyList<EnemyFormationSlotDefinition> Slots => _slots;

        public EnemyFormationDefinition(string formationId,
            IEnumerable<EnemyFormationSlotDefinition> slots)
        {
            if (string.IsNullOrWhiteSpace(formationId))
                throw new ArgumentException("Formation ID is required.", nameof(formationId));
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            var copy = new List<EnemyFormationSlotDefinition>(slots);
            if (copy.Count != RequiredSlotCount)
                throw new ArgumentException("A formation must define exactly six slots.", nameof(slots));
            for (int i = 0; i < copy.Count; i++)
                if (copy[i] == null)
                    throw new ArgumentException("Formation slots cannot contain null entries.", nameof(slots));
            FormationId = formationId;
            _slots = copy.AsReadOnly();
        }
    }
}
