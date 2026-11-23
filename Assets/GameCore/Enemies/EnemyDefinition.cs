using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProjectNST.GameCore.Enemies
{
    /// <summary>Immutable, Unity-independent copy of an EnemyData definition.</summary>
    public sealed class EnemyDefinition
    {
        private readonly ReadOnlyCollection<string> _cipEffectIds;
        private readonly ReadOnlyCollection<string> _aeEffectIds;
        private readonly ReadOnlyCollection<string> _pigEffectIds;
        private readonly ReadOnlyCollection<string> _targetIds;
        private readonly ReadOnlyCollection<int> _stages;
        private readonly ReadOnlyCollection<string> _categories;

        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int Health { get; }
        public IReadOnlyList<string> CipEffectIds => _cipEffectIds;
        public IReadOnlyList<string> AeEffectIds => _aeEffectIds;
        public IReadOnlyList<string> PigEffectIds => _pigEffectIds;
        public string Target { get; }
        public IReadOnlyList<string> TargetIds => _targetIds;
        public IReadOnlyList<int> Stages => _stages;
        public IReadOnlyList<string> Categories => _categories;

        public EnemyDefinition(string definitionId, string displayName, int attack, int defense, int health,
            IEnumerable<string> cipEffectIds, IEnumerable<string> aeEffectIds,
            IEnumerable<string> pigEffectIds, string target, IEnumerable<string> targetIds,
            IEnumerable<int> stages, IEnumerable<string> categories)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("Enemy definition ID is required.", nameof(definitionId));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Enemy display name is required.", nameof(displayName));
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("Enemy target data is required.", nameof(target));

            DefinitionId = definitionId;
            DisplayName = displayName;
            Attack = attack;
            Defense = defense;
            Health = health;
            Target = target;
            _cipEffectIds = CopyStrings(cipEffectIds, nameof(cipEffectIds));
            _aeEffectIds = CopyStrings(aeEffectIds, nameof(aeEffectIds));
            _pigEffectIds = CopyStrings(pigEffectIds, nameof(pigEffectIds));
            _targetIds = CopyStrings(targetIds, nameof(targetIds));
            _stages = CopyInts(stages, nameof(stages));
            _categories = CopyStrings(categories, nameof(categories));
        }

        private static ReadOnlyCollection<string> CopyStrings(IEnumerable<string> source, string parameter)
        {
            var copy = source == null ? new List<string>() : new List<string>(source);
            for (int i = 0; i < copy.Count; i++)
                if (string.IsNullOrWhiteSpace(copy[i]))
                    throw new ArgumentException("String collections cannot contain empty values.", parameter);
            return copy.AsReadOnly();
        }

        private static ReadOnlyCollection<int> CopyInts(IEnumerable<int> source, string parameter)
        {
            var copy = source == null ? new List<int>() : new List<int>(source);
            for (int i = 0; i < copy.Count; i++)
                if (copy[i] < 0)
                    throw new ArgumentOutOfRangeException(parameter, "Stage numbers cannot be negative.");
            return copy.AsReadOnly();
        }
    }
}
