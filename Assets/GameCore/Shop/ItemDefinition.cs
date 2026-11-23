using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Shop
{
    public enum ItemEffectKind
    {
        Attack = 1,
        Defense = 2,
        Star = 3,
        Damage = 4,
        Block = 5,
        Gap = 6
    }

    public enum ItemEffectTarget
    {
        Unspecified = 0,
        OneAlly = 1,
        AllAllies = 2,
        OneEnemy = 3,
        AllEnemies = 4,
        Player = 5
    }

    public enum ItemEffectDuration
    {
        Unspecified = 0,
        ThisBattle = 1,
        Immediate = 2
    }

    /// <summary>One immutable effect code and value copied from ItemData. It does not apply the effect.</summary>
    public sealed class ItemEffectDefinition
    {
        public ItemEffectKind Kind { get; }
        public int Value { get; }

        public ItemEffectDefinition(ItemEffectKind kind, int value)
        {
            if (!Enum.IsDefined(typeof(ItemEffectKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            Value = value;
        }
    }

    /// <summary>Unity-independent item master data. Effects are descriptive only in Stage 7-D.</summary>
    public sealed class ItemDefinition
    {
        private readonly ItemEffectDefinition[] _effects;

        public string DefinitionId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int PurchasePrice { get; }
        public ItemEffectTarget Target { get; }
        public ItemEffectDuration Duration { get; }
        public IReadOnlyList<ItemEffectDefinition> Effects { get; }

        /// <summary>Compatibility constructor for existing Stage 4 definitions without effect metadata.</summary>
        public ItemDefinition(string definitionId, string displayName, int purchasePrice)
            : this(definitionId, displayName, purchasePrice, string.Empty,
                ItemEffectTarget.Unspecified, ItemEffectDuration.Unspecified,
                Array.Empty<ItemEffectDefinition>())
        {
        }

        public ItemDefinition(string definitionId, string displayName, int purchasePrice,
            string description, ItemEffectTarget target, ItemEffectDuration duration,
            IEnumerable<ItemEffectDefinition> effects)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("A non-empty item DefinitionId is required.", nameof(definitionId));
            if (purchasePrice < 0)
                throw new ArgumentOutOfRangeException(nameof(purchasePrice), "Purchase price cannot be negative.");
            if (!Enum.IsDefined(typeof(ItemEffectTarget), target))
                throw new ArgumentOutOfRangeException(nameof(target));
            if (!Enum.IsDefined(typeof(ItemEffectDuration), duration))
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (effects == null) throw new ArgumentNullException(nameof(effects));

            var copied = new List<ItemEffectDefinition>(effects);
            if (copied.Exists(effect => effect == null))
                throw new ArgumentException("Effect definitions cannot contain null entries.", nameof(effects));

            DefinitionId = definitionId;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            PurchasePrice = purchasePrice;
            Target = target;
            Duration = duration;
            _effects = copied.ToArray();
            Effects = Array.AsReadOnly(_effects);
        }

        /// <summary>Checks all immutable fields when duplicate assets share one Item ID.</summary>
        public bool HasSameDefinitionData(ItemDefinition other)
        {
            if (other == null || DefinitionId != other.DefinitionId || DisplayName != other.DisplayName ||
                Description != other.Description || PurchasePrice != other.PurchasePrice ||
                Target != other.Target || Duration != other.Duration || _effects.Length != other._effects.Length)
                return false;
            for (int i = 0; i < _effects.Length; i++)
                if (_effects[i].Kind != other._effects[i].Kind || _effects[i].Value != other._effects[i].Value)
                    return false;
            return true;
        }
    }
}
