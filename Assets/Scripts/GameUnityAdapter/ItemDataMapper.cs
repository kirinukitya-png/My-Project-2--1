using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameUnityAdapter
{
    /// <summary>Copies ItemData into Unity-independent GameCore data; never changes the asset.</summary>
    public static class ItemDataMapper
    {
        public static ItemDefinition ToDefinition(ItemData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.itemId))
                throw new ArgumentException("ItemData requires a non-empty itemId.", nameof(source));
            if (string.IsNullOrWhiteSpace(source.itemName))
                throw new ArgumentException("ItemData requires a non-empty itemName.", nameof(source));
            if (source.effects == null || source.effectValues == null || source.effects.Length != source.effectValues.Length)
                throw new ArgumentException("ItemData effects and effectValues must both exist and have equal lengths.", nameof(source));

            var effects = new List<ItemEffectDefinition>(source.effects.Length);
            for (int i = 0; i < source.effects.Length; i++)
                effects.Add(new ItemEffectDefinition(ParseEffect(source.effects[i]), source.effectValues[i]));

            return new ItemDefinition(source.itemId, source.itemName, source.purchasePrice,
                source.description, ParseTarget(source.target), ParseDuration(source.duration), effects);
        }

        private static ItemEffectKind ParseEffect(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "Attack": return ItemEffectKind.Attack;
                case "Def": return ItemEffectKind.Defense;
                case "Star": return ItemEffectKind.Star;
                case "Damage": return ItemEffectKind.Damage;
                case "Block": return ItemEffectKind.Block;
                case "Gap": return ItemEffectKind.Gap;
                default: throw new ArgumentException("Unsupported ItemData effect: " + (value ?? "<null>"));
            }
        }

        private static ItemEffectTarget ParseTarget(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "味方":
                case "味方1体": return ItemEffectTarget.OneAlly;
                case "全ての味方":
                case "すべての味方": return ItemEffectTarget.AllAllies;
                case "敵":
                case "敵1体": return ItemEffectTarget.OneEnemy;
                case "全ての敵":
                case "すべての敵": return ItemEffectTarget.AllEnemies;
                case "プレイヤー": return ItemEffectTarget.Player;
                default: throw new ArgumentException("Unsupported ItemData target: " + (value ?? "<null>"));
            }
        }

        private static ItemEffectDuration ParseDuration(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "このバトル中": return ItemEffectDuration.ThisBattle;
                case "即時効果": return ItemEffectDuration.Immediate;
                default: throw new ArgumentException("Unsupported ItemData duration: " + (value ?? "<null>"));
            }
        }
    }
}
