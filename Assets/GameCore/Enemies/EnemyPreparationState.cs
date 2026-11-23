using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Enemies
{
    public enum EnemyPreparationFailure
    {
        None,
        InvalidFormation,
        NoEnemyTargets,
        UnsupportedCommand,
        ArithmeticOverflow
    }

    public sealed class EnemyPreparationResult
    {
        public bool Succeeded { get; }
        public EnemyPreparationFailure Failure { get; }
        public EnemyPreparationState NextState { get; }

        internal EnemyPreparationResult(bool succeeded, EnemyPreparationFailure failure,
            EnemyPreparationState nextState)
        {
            Succeeded = succeeded;
            Failure = failure;
            NextState = nextState;
        }
    }

    /// <summary>Immutable, six-slot pre-battle modifiers. Call ClearForNextStage at stage boundaries.</summary>
    public sealed class EnemyPreparationState
    {
        public const int SlotCount = EnemyInstanceFactory.SlotCount;
        private readonly ReadOnlyCollection<int> _attack;
        private readonly ReadOnlyCollection<int> _defense;
        private readonly ReadOnlyCollection<int> _health;
        private readonly ReadOnlyCollection<int> _gap;

        public IReadOnlyList<int> AttackBonuses => _attack;
        public IReadOnlyList<int> DefenseBonuses => _defense;
        public IReadOnlyList<int> HealthBonuses => _health;
        public IReadOnlyList<int> GapBonuses => _gap;

        public EnemyPreparationState() : this(null, null, null, null) { }

        public EnemyPreparationState(IEnumerable<int> attackBonuses, IEnumerable<int> defenseBonuses,
            IEnumerable<int> healthBonuses, IEnumerable<int> gapBonuses)
        {
            _attack = CopySix(attackBonuses, nameof(attackBonuses));
            _defense = CopySix(defenseBonuses, nameof(defenseBonuses));
            _health = CopySix(healthBonuses, nameof(healthBonuses));
            _gap = CopySix(gapBonuses, nameof(gapBonuses));
        }

        public int GetAttackBonus(int slotIndex) => At(_attack, slotIndex);
        public int GetDefenseBonus(int slotIndex) => At(_defense, slotIndex);
        public int GetHealthBonus(int slotIndex) => At(_health, slotIndex);
        public int GetGapBonus(int slotIndex) => At(_gap, slotIndex);

        public EnemyPreparationState ClearForNextStage() => new EnemyPreparationState();

        /// <summary>Applies an AllEnemies command to occupied formation slots only; inputs are unchanged.</summary>
        public EnemyPreparationResult TryApplyAllEnemies(EnemyFormationDefinition formation,
            IEnumerable<ItemEffectCommand> commands)
        {
            if (!HasSixSlots(formation))
                return Failure(EnemyPreparationFailure.InvalidFormation, this);
            if (commands == null)
                return Failure(EnemyPreparationFailure.UnsupportedCommand, this);

            bool anyTarget = false;
            for (int i = 0; i < formation.Slots.Count; i++)
                anyTarget |= formation.Slots[i].HasEnemy;
            if (!anyTarget) return Failure(EnemyPreparationFailure.NoEnemyTargets, this);

            int[] attack = ToArray(_attack);
            int[] defense = ToArray(_defense);
            int[] health = ToArray(_health);
            int[] gap = ToArray(_gap);

            foreach (ItemEffectCommand command in commands)
            {
                if (command == null || command.Target != ItemEffectTarget.AllEnemies || command.EnemySlotIndex != -1)
                    return Failure(EnemyPreparationFailure.UnsupportedCommand, this);
                int[] target;
                long delta;
                switch (command.Effect)
                {
                    case ItemEffectKind.Attack: target = attack; delta = command.Value; break;
                    case ItemEffectKind.Defense: target = defense; delta = command.Value; break;
                    case ItemEffectKind.Damage:
                        target = health;
                        delta = -(long)command.Value;
                        break;
                    // ItemEffectRules currently does not permit AllEnemies/Gap; reject instead of guessing.
                    default: return Failure(EnemyPreparationFailure.UnsupportedCommand, this);
                }

                for (int i = 0; i < formation.Slots.Count; i++)
                {
                    if (!formation.Slots[i].HasEnemy) continue;
                    long sum = (long)target[i] + delta;
                    if (sum < int.MinValue || sum > int.MaxValue)
                        return Failure(EnemyPreparationFailure.ArithmeticOverflow, this);
                    target[i] = (int)sum;
                }
            }

            return new EnemyPreparationResult(true, EnemyPreparationFailure.None,
                new EnemyPreparationState(attack, defense, health, gap));
        }

        private static EnemyPreparationResult Failure(EnemyPreparationFailure failure, EnemyPreparationState original) =>
            new EnemyPreparationResult(false, failure, original);

        private static bool HasSixSlots(EnemyFormationDefinition formation) =>
            formation != null && formation.Slots != null && formation.Slots.Count == SlotCount;

        private static ReadOnlyCollection<int> CopySix(IEnumerable<int> source, string parameter)
        {
            var copy = source == null ? new int[SlotCount] : new List<int>(source).ToArray();
            if (copy.Length != SlotCount)
                throw new ArgumentException("Enemy preparation arrays must contain exactly six values.", parameter);
            return Array.AsReadOnly(copy);
        }

        private static int[] ToArray(IReadOnlyList<int> source)
        {
            var copy = new int[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return copy;
        }

        private static int At(IReadOnlyList<int> values, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return values[slotIndex];
        }
    }
}
