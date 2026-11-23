using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameUnityAdapter
{
    /// <summary>Converts serialized EncounterPoolData and EnemyFormationData without modifying assets.</summary>
    public static class EnemyEncounterDataMapper
    {
        public static EnemyFormationDefinition ToFormationDefinition(EnemyFormationData source,
            IReadOnlyDictionary<string, EnemyDefinition> enemyDefinitions)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (enemyDefinitions == null) throw new ArgumentNullException(nameof(enemyDefinitions));
            if (string.IsNullOrWhiteSpace(source.formationId))
                throw new ArgumentException("EnemyFormationData requires a non-empty formationId.", nameof(source));
            if (source.slots == null || source.slots.Length != EnemyFormationDefinition.RequiredSlotCount)
                throw new ArgumentException("EnemyFormationData must contain exactly six slots.", nameof(source));

            var slots = new List<EnemyFormationSlotDefinition>(EnemyFormationDefinition.RequiredSlotCount);
            for (int i = 0; i < EnemyFormationDefinition.RequiredSlotCount; i++)
            {
                EnemyFormationData.FormationSlot slot = source.slots[i];
                if (slot == null)
                {
                    slots.Add(new EnemyFormationSlotDefinition(null, 0, 0));
                    continue;
                }

                string enemyDefinitionId = null;
                if (slot.enemy != null)
                {
                    string enemyId = slot.enemy.enemyId;
                    if (string.IsNullOrWhiteSpace(enemyId) ||
                        !enemyDefinitions.TryGetValue(enemyId, out EnemyDefinition definition) ||
                        definition == null || !string.Equals(definition.DefinitionId, enemyId, StringComparison.Ordinal))
                        throw new ArgumentException("Formation " + source.formationId +
                            " references an unmapped or mismatched EnemyData ID.", nameof(source));
                    enemyDefinitionId = definition.DefinitionId;
                }
                slots.Add(new EnemyFormationSlotDefinition(enemyDefinitionId, slot.gap, slot.cooldown));
            }

            return new EnemyFormationDefinition(source.formationId, slots);
        }

        public static EncounterPoolDefinition ToEncounterPoolDefinition(EncounterPoolData source,
            IReadOnlyDictionary<string, EnemyFormationDefinition> formationDefinitions)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (formationDefinitions == null) throw new ArgumentNullException(nameof(formationDefinitions));
            if (string.IsNullOrWhiteSpace(source.encounterId))
                throw new ArgumentException("EncounterPoolData requires a non-empty encounterId.", nameof(source));
            StageEnemyCategory category = ParseCategory(source.category);

            var rounds = source.roundIndices == null
                ? Array.Empty<int>()
                : (int[])source.roundIndices.Clone();
            var formations = new List<EnemyFormationDefinition>();
            if (source.formations != null)
            {
                foreach (EnemyFormationData formation in source.formations)
                {
                    if (formation == null || string.IsNullOrWhiteSpace(formation.formationId) ||
                        !formationDefinitions.TryGetValue(formation.formationId, out EnemyFormationDefinition definition) ||
                        definition == null || !string.Equals(definition.FormationId, formation.formationId, StringComparison.Ordinal))
                        throw new ArgumentException("Encounter pool " + source.encounterId +
                            " contains an unmapped or invalid formation reference.", nameof(source));
                    // Add each serialized reference in order; repeated references are intentional weights.
                    formations.Add(definition);
                }
            }

            return new EncounterPoolDefinition(source.encounterId, rounds, category, formations);
        }

        private static StageEnemyCategory ParseCategory(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "normal": return StageEnemyCategory.Normal;
                case "hard": return StageEnemyCategory.Hard;
                case "boss": return StageEnemyCategory.Boss;
                default: throw new ArgumentException("Unsupported EncounterPoolData category: " + (value ?? "<null>"));
            }
        }
    }
}
