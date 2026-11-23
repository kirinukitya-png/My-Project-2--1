using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Enemies;

namespace ProjectNST.GameUnityAdapter
{
    /// <summary>Copies EnemyData assets into Unity-independent definitions and validates resource IDs.</summary>
    public static class EnemyDataMapper
    {
        public static EnemyDefinition ToDefinition(EnemyData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.enemyId))
                throw new ArgumentException("EnemyData requires a non-empty enemyId.", nameof(source));
            string name = string.IsNullOrWhiteSpace(source.enemyName) ? source.name : source.enemyName;
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("EnemyData requires a non-empty enemyName.", nameof(source));

            HashSet<string> knownTargets = new HashSet<string>(
                UnityEngine.Resources.LoadAll<TargetData>("TargetData")
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.targetId))
                    .Select(x => x.targetId), StringComparer.Ordinal);
            HashSet<string> knownEffects = new HashSet<string>(
                UnityEngine.Resources.LoadAll<EffectData>("EffectData")
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.effectId))
                    .Select(x => x.effectId), StringComparer.Ordinal);
            if (knownTargets.Count == 0)
                throw new InvalidOperationException("No TargetData assets were found in Resources/TargetData.");
            if (knownEffects.Count == 0)
                throw new InvalidOperationException("No EffectData assets were found in Resources/EffectData.");

            string rawTarget = source.target;
            string[] targetIds = ParseIds(rawTarget, "target", allowEmptyFallback: true);
            if (targetIds.Length == 0) targetIds = new[] { "T001" };
            ValidateReferences(targetIds, knownTargets, "TargetData", source.enemyId);
            string[] cip = ParseIds(source.cip, "cip", allowEmptyFallback: true);
            string[] ae = ParseIds(source.ae, "ae", allowEmptyFallback: true);
            string[] pig = ParseIds(source.pig, "pig", allowEmptyFallback: true);
            ValidateReferences(cip, knownEffects, "EffectData", source.enemyId);
            ValidateReferences(ae, knownEffects, "EffectData", source.enemyId);
            ValidateReferences(pig, knownEffects, "EffectData", source.enemyId);

            return new EnemyDefinition(source.enemyId, name, source.attack, source.defense, source.health,
                cip, ae, pig, string.IsNullOrWhiteSpace(rawTarget) ? "T001" : rawTarget,
                targetIds, source.stages == null ? Array.Empty<int>() : (int[])source.stages.Clone(),
                CopyStrings(source.categories));
        }

        private static string[] ParseIds(string raw, string field, bool allowEmptyFallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            string[] values = raw.Split(',');
            var result = new List<string>(values.Length);
            foreach (string value in values)
            {
                string id = value.Trim();
                if (id.Length == 0)
                {
                    if (allowEmptyFallback) continue;
                    throw new ArgumentException("EnemyData " + field + " contains an empty ID.");
                }
                result.Add(id);
            }
            return result.ToArray();
        }

        private static void ValidateReferences(IEnumerable<string> ids, HashSet<string> known,
            string resourceType, string enemyId)
        {
            foreach (string id in ids)
                if (!known.Contains(id))
                    throw new ArgumentException("Enemy " + enemyId + " references missing " + resourceType + " ID '" + id + "'.");
        }

        private static string[] CopyStrings(string[] source)
        {
            if (source == null) return Array.Empty<string>();
            var copy = (string[])source.Clone();
            for (int i = 0; i < copy.Length; i++)
                if (string.IsNullOrWhiteSpace(copy[i]))
                    throw new ArgumentException("EnemyData categories cannot contain empty values.", nameof(source));
            return copy;
        }
    }
}
