using System;

namespace ProjectNST.GameCore.Enemies
{
    public enum EnemyInstanceFailure
    {
        None,
        InvalidDefinition,
        InvalidEncounterIndex,
        InvalidSlotIndex,
        ArithmeticOverflow
    }

    /// <summary>Immutable initial combat state for one enemy in one encounter.</summary>
    public sealed class EnemyInstance
    {
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public int SlotIndex { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int MaxHealth { get; }
        public int CurrentHealth { get; }
        public int Gap { get; }
        public int Cooldown { get; }
        public int InitialActionRound { get; }

        internal EnemyInstance(string instanceId, string definitionId, int slotIndex, int attack,
            int defense, int maxHealth, int gap, int cooldown, int initialActionRound)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            SlotIndex = slotIndex;
            Attack = attack;
            Defense = defense;
            MaxHealth = maxHealth;
            CurrentHealth = maxHealth;
            Gap = gap;
            Cooldown = cooldown;
            InitialActionRound = initialActionRound;
        }
    }

    /// <summary>Creates stable encounter-local IDs without counters, GUIDs, or random streams.</summary>
    public static class EnemyInstanceFactory
    {
        public const int SlotCount = 6;

        public static bool TryCreate(EnemyDefinition definition, int encounterIndex, int slotIndex,
            int slotAttackBonus, int slotDefenseBonus, int slotHealthBonus,
            int formationGap, int slotGapBonus, int formationCooldown,
            out EnemyInstance instance, out EnemyInstanceFailure failure)
        {
            instance = null;
            if (definition == null)
            {
                failure = EnemyInstanceFailure.InvalidDefinition;
                return false;
            }
            if (encounterIndex < 0)
            {
                failure = EnemyInstanceFailure.InvalidEncounterIndex;
                return false;
            }
            if (slotIndex < 0 || slotIndex >= SlotCount)
            {
                failure = EnemyInstanceFailure.InvalidSlotIndex;
                return false;
            }

            long attack = (long)definition.Attack + slotAttackBonus;
            long defense = (long)definition.Defense + slotDefenseBonus;
            long rawHealth = (long)definition.Health + slotHealthBonus;
            long rawGap = (long)formationGap + slotGapBonus;
            long gap = Math.Max(0L, rawGap);
            long cooldown = Math.Max(0L, formationCooldown);
            long initialActionRound = gap + 1L;
            if (!FitsInt(attack) || !FitsInt(defense) || !FitsInt(rawHealth) ||
                !FitsInt(gap) || !FitsInt(cooldown) || !FitsInt(initialActionRound))
            {
                failure = EnemyInstanceFailure.ArithmeticOverflow;
                return false;
            }

            int maxHealth = (int)Math.Max(0L, rawHealth);
            string id = CreateDeterministicId(encounterIndex, slotIndex);
            instance = new EnemyInstance(id, definition.DefinitionId, slotIndex,
                (int)attack, (int)defense, maxHealth, (int)gap, (int)cooldown,
                (int)initialActionRound);
            failure = EnemyInstanceFailure.None;
            return true;
        }

        public static string CreateDeterministicId(int encounterIndex, int slotIndex)
        {
            if (encounterIndex < 0) throw new ArgumentOutOfRangeException(nameof(encounterIndex));
            if (slotIndex < 0 || slotIndex >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return "enemy:encounter:" + encounterIndex + ":slot:" + slotIndex;
        }

        private static bool FitsInt(long value) => value >= int.MinValue && value <= int.MaxValue;
    }
}
