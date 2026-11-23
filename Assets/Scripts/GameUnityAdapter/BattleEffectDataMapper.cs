using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Battle;

namespace ProjectNST.GameUnityAdapter
{
    /// <summary>Read-only mapping of EffectData assets into immutable GameCore effect definitions.</summary>
    public static class BattleEffectDataMapper
    {
        public static BattleEffectDefinition ToDefinition(EffectData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.effectId))
                throw new ArgumentException("EffectData requires a non-empty effectId.", nameof(source));

            BattleEffectType effect;
            switch ((source.effect ?? string.Empty).Trim())
            {
                case "Attack": effect = BattleEffectType.Attack; break;
                case "Def": effect = BattleEffectType.Defense; break;
                case "Gap": effect = BattleEffectType.Gap; break;
                case "Gold": effect = BattleEffectType.Gold; break;
                case "Damage": effect = BattleEffectType.Damage; break;
                case "Block": effect = BattleEffectType.Block; break;
                default: throw new ArgumentException("Unsupported EffectData effect: " + source.effect, nameof(source));
            }

            BattleEffectTarget target;
            switch ((source.target ?? string.Empty).Trim())
            {
                case "攻撃者": target = BattleEffectTarget.TriggerActor; break;
                case "攻撃対象": target = BattleEffectTarget.AttackTargets; break;
                case "プレイヤー": target = BattleEffectTarget.Player; break;
                default: throw new ArgumentException("Unsupported EffectData target: " + source.target, nameof(source));
            }

            BattleEffectDuration duration;
            switch ((source.duration ?? string.Empty).Trim())
            {
                case "このバトル中": duration = BattleEffectDuration.Battle; break;
                case "即時効果": duration = BattleEffectDuration.Immediate; break;
                default: throw new ArgumentException("Unsupported EffectData duration: " + source.duration, nameof(source));
            }

            return new BattleEffectDefinition(source.effectId.Trim(), effect,
                source.effectValue, target, duration);
        }

        public static IReadOnlyDictionary<string, BattleEffectDefinition> ToDefinitionMap(
            IEnumerable<EffectData> assets)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            var result = new Dictionary<string, BattleEffectDefinition>(StringComparer.Ordinal);
            foreach (EffectData asset in assets)
            {
                BattleEffectDefinition definition = ToDefinition(asset);
                if (result.ContainsKey(definition.EffectId))
                    throw new ArgumentException("Duplicate EffectData ID: " + definition.EffectId, nameof(assets));
                result.Add(definition.EffectId, definition);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, BattleEffectDefinition>(result);
        }
    }
}
