using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleUnitSide { Ally, Enemy }

    /// <summary>Immutable, Unity-independent combat snapshot for one board occupant.</summary>
    public sealed class BattleUnitState
    {
        private readonly string[] _unitTags;
        private readonly string[] _attributeTags;
        private readonly string[] _targetIds;
        private readonly string[] _cipEffectIds;
        private readonly string[] _aeEffectIds;
        private readonly string[] _pigEffectIds;

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public BattleUnitSide Side { get; }
        public int SlotIndex { get; }
        public int Attack { get; }
        public int Defense { get; }
        public bool HasHealth { get; }
        public int MaxHP { get; }
        public int CurrentHP { get; }
        public int Gap { get; }
        public int Cooldown { get; }
        public int NextActionRound { get; }
        public int StunRounds { get; }
        public int Star { get; }
        public IReadOnlyList<string> UnitTags { get; }
        public IReadOnlyList<string> AttributeTags { get; }
        public IReadOnlyList<string> TargetIds { get; }
        public int NextTargetIndex { get; }
        public IReadOnlyList<string> CipEffectIds { get; }
        public IReadOnlyList<string> AeEffectIds { get; }
        public IReadOnlyList<string> PigEffectIds { get; }
        public int TemporaryAttackBonus { get; }
        public int TemporaryDefenseBonus { get; }
        public int TemporaryDefenseReduction { get; }

        public BattleUnitState(string instanceId, string definitionId, BattleUnitSide side,
            int slotIndex, int attack, int defense, bool hasHealth, int maxHP, int currentHP,
            int gap, int cooldown, int nextActionRound, int stunRounds, int star,
            IEnumerable<string> unitTags, IEnumerable<string> attributeTags,
            IEnumerable<string> targetIds, int nextTargetIndex,
            IEnumerable<string> cipEffectIds, IEnumerable<string> aeEffectIds,
            IEnumerable<string> pigEffectIds, int temporaryAttackBonus = 0,
            int temporaryDefenseBonus = 0, int temporaryDefenseReduction = 0)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("Instance ID is required.", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("Definition ID is required.", nameof(definitionId));
            if (!Enum.IsDefined(typeof(BattleUnitSide), side)) throw new ArgumentOutOfRangeException(nameof(side));
            if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            if (hasHealth && (maxHP < 0 || currentHP < 0 || currentHP > maxHP)) throw new ArgumentOutOfRangeException(nameof(currentHP));
            if (!hasHealth && (maxHP != 0 || currentHP != 0)) throw new ArgumentException("Units without health must use zero HP values.");
            if (gap < 0 || cooldown < 0 || nextActionRound < 1 || stunRounds < 0 || nextTargetIndex < 0 || temporaryDefenseReduction < 0)
                throw new ArgumentOutOfRangeException("Combat counters must be non-negative and nextActionRound must be positive.");
            if (side == BattleUnitSide.Ally && (star < 1 || star > 3)) throw new ArgumentOutOfRangeException(nameof(star));
            if (side == BattleUnitSide.Enemy && star != 0) throw new ArgumentException("Enemy units do not have a Star value.", nameof(star));

            InstanceId = instanceId;
            DefinitionId = definitionId;
            Side = side;
            SlotIndex = slotIndex;
            Attack = attack;
            Defense = defense;
            HasHealth = hasHealth;
            MaxHP = maxHP;
            CurrentHP = currentHP;
            Gap = gap;
            Cooldown = cooldown;
            NextActionRound = nextActionRound;
            StunRounds = stunRounds;
            Star = star;
            _unitTags = Copy(unitTags, nameof(unitTags));
            _attributeTags = Copy(attributeTags, nameof(attributeTags));
            _targetIds = Copy(targetIds, nameof(targetIds));
            _cipEffectIds = Copy(cipEffectIds, nameof(cipEffectIds));
            _aeEffectIds = Copy(aeEffectIds, nameof(aeEffectIds));
            _pigEffectIds = Copy(pigEffectIds, nameof(pigEffectIds));
            UnitTags = Array.AsReadOnly(_unitTags);
            AttributeTags = Array.AsReadOnly(_attributeTags);
            TargetIds = Array.AsReadOnly(_targetIds);
            CipEffectIds = Array.AsReadOnly(_cipEffectIds);
            AeEffectIds = Array.AsReadOnly(_aeEffectIds);
            PigEffectIds = Array.AsReadOnly(_pigEffectIds);
            if (nextTargetIndex > _targetIds.Length)
                throw new ArgumentOutOfRangeException(nameof(nextTargetIndex));
            NextTargetIndex = nextTargetIndex;
            TemporaryAttackBonus = temporaryAttackBonus;
            TemporaryDefenseBonus = temporaryDefenseBonus;
            TemporaryDefenseReduction = temporaryDefenseReduction;
        }

        private static string[] Copy(IEnumerable<string> values, string name)
        {
            var copy = values == null ? new List<string>() : new List<string>(values);
            if (copy.Exists(string.IsNullOrWhiteSpace)) throw new ArgumentException("Tags and IDs cannot be empty.", name);
            return copy.ToArray();
        }

        internal BattleUnitState WithScheduledState(int? defense = null, int? gap = null,
            int? nextActionRound = null, int? stunRounds = null,
            int? temporaryDefenseReduction = null)
        {
            return new BattleUnitState(InstanceId, DefinitionId, Side, SlotIndex, Attack,
                defense ?? Defense, HasHealth, MaxHP, CurrentHP, gap ?? Gap, Cooldown,
                nextActionRound ?? NextActionRound, stunRounds ?? StunRounds, Star,
                _unitTags, _attributeTags, _targetIds, NextTargetIndex,
                _cipEffectIds, _aeEffectIds, _pigEffectIds, TemporaryAttackBonus,
                TemporaryDefenseBonus, temporaryDefenseReduction ?? TemporaryDefenseReduction);
        }

        internal BattleUnitState WithCombatState(int? currentHP = null, int? nextTargetIndex = null)
        {
            return new BattleUnitState(InstanceId, DefinitionId, Side, SlotIndex, Attack,
                Defense, HasHealth, MaxHP, currentHP ?? CurrentHP, Gap, Cooldown,
                NextActionRound, StunRounds, Star, _unitTags, _attributeTags,
                _targetIds, nextTargetIndex ?? NextTargetIndex, _cipEffectIds,
                _aeEffectIds, _pigEffectIds, TemporaryAttackBonus,
                TemporaryDefenseBonus, TemporaryDefenseReduction);
        }

        internal BattleUnitState WithEffectState(int? attack = null, int? defense = null,
            int? gap = null, int? nextActionRound = null, int? stunRounds = null,
            int? currentHP = null, int? temporaryDefenseReduction = null,
            int? temporaryAttackBonus = null, int? temporaryDefenseBonus = null)
        {
            return new BattleUnitState(InstanceId, DefinitionId, Side, SlotIndex,
                attack ?? Attack, defense ?? Defense, HasHealth, MaxHP, currentHP ?? CurrentHP,
                gap ?? Gap, Cooldown, nextActionRound ?? NextActionRound,
                stunRounds ?? StunRounds, Star, _unitTags, _attributeTags, _targetIds,
                NextTargetIndex, _cipEffectIds, _aeEffectIds, _pigEffectIds,
                temporaryAttackBonus ?? TemporaryAttackBonus,
                temporaryDefenseBonus ?? TemporaryDefenseBonus,
                temporaryDefenseReduction ?? TemporaryDefenseReduction);
        }

        /// <summary>Returns a result snapshot with battle-only modifiers and turn counters cleared.</summary>
        internal bool TryWithBattleCleanup(out BattleUnitState cleaned)
        {
            cleaned = this;
            long attack = (long)Attack - TemporaryAttackBonus;
            long defense = (long)Defense - TemporaryDefenseBonus + TemporaryDefenseReduction;
            if (attack < int.MinValue || attack > int.MaxValue ||
                defense < int.MinValue || defense > int.MaxValue) return false;

            cleaned = new BattleUnitState(InstanceId, DefinitionId, Side, SlotIndex,
                (int)attack, (int)defense, HasHealth, MaxHP, CurrentHP,
                0, Cooldown, 1, 0, Star, _unitTags, _attributeTags,
                _targetIds, 0, _cipEffectIds, _aeEffectIds, _pigEffectIds);
            return true;
        }
    }
}
