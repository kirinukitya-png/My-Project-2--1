using System;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Characters
{
    /// <summary>Run-specific character state. Definition data is referenced by ID, never mutated.</summary>
    public sealed class CharacterInstance
    {
        private bool _hasTemporaryStar;
        private int _starBeforeTemporaryEffect;
        private int _attackBeforeTemporaryEffect;
        private int _defenseBeforeTemporaryEffect;
        private bool _hadRolledStatsBeforeTemporaryEffect;

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public int RunCost { get; }
        public int Star { get; private set; }
        /// <summary>Number of finite pool copies represented by this owned instance. Independent of Star effects.</summary>
        public int PoolCopies { get; private set; }
        public int Attack { get; private set; }
        public int Defense { get; private set; }
        public int TemporaryAttackBonus { get; set; }
        public int TemporaryDefenseBonus { get; set; }
        public bool HasRolledStats { get; private set; }
        public bool HasTemporaryStar => _hasTemporaryStar;

        public CharacterInstance(string instanceId, CharacterDefinition definition, int runCost, int star = 1,
            int poolCopies = -1)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("A unique InstanceId is required.", nameof(instanceId));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (runCost < 1) throw new ArgumentOutOfRangeException(nameof(runCost));
            ValidateStar(star);
            if (poolCopies < -1) throw new ArgumentOutOfRangeException(nameof(poolCopies));

            InstanceId = instanceId;
            DefinitionId = definition.DefinitionId;
            RunCost = runCost;
            Star = star;
            PoolCopies = poolCopies == -1 ? CopiesForStar(star) : poolCopies;
        }

        /// <summary>Creates a detached copy of this instance without consuming randomness.</summary>
        internal CharacterInstance CreateCopy()
        {
            return new CharacterInstance(this);
        }

        private CharacterInstance(CharacterInstance source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            InstanceId = source.InstanceId;
            DefinitionId = source.DefinitionId;
            RunCost = source.RunCost;
            Star = source.Star;
            PoolCopies = source.PoolCopies;
            Attack = source.Attack;
            Defense = source.Defense;
            TemporaryAttackBonus = source.TemporaryAttackBonus;
            TemporaryDefenseBonus = source.TemporaryDefenseBonus;
            HasRolledStats = source.HasRolledStats;
            _hasTemporaryStar = source._hasTemporaryStar;
            _starBeforeTemporaryEffect = source._starBeforeTemporaryEffect;
            _attackBeforeTemporaryEffect = source._attackBeforeTemporaryEffect;
            _defenseBeforeTemporaryEffect = source._defenseBeforeTemporaryEffect;
            _hadRolledStatsBeforeTemporaryEffect = source._hadRolledStatsBeforeTemporaryEffect;
        }

        /// <summary>Explicit equivalent of legacy lazy initialization; value getters stay side-effect free.</summary>
        public bool EnsureStats(CharacterDefinition definition, CharacterStatDefinition settings, IRandomSource random)
        {
            if (HasRolledStats) return true;
            return TryRollStats(definition, settings, random, Star);
        }

        /// <summary>Legacy TrySetStar behavior: every successful call rerolls the chosen star's stats.</summary>
        public bool TrySetStar(int newStar, CharacterDefinition definition,
            CharacterStatDefinition settings, IRandomSource random)
        {
            if (newStar < 1 || newStar > 3) return false;
            if (!TryRollStats(definition, settings, random, newStar)) return false;
            Star = newStar;
            return true;
        }

        /// <summary>Sets pool provenance when copies are consumed by a merge; Star changes alone never alter it.</summary>
        internal bool TrySetPoolCopies(int copies)
        {
            if (copies < 0) return false;
            PoolCopies = copies;
            return true;
        }

        public bool TrySetTemporaryStar(int newStar, CharacterDefinition definition,
            CharacterStatDefinition settings, IRandomSource random)
        {
            bool wasAlreadyTemporary = _hasTemporaryStar;
            if (!wasAlreadyTemporary)
            {
                _starBeforeTemporaryEffect = Star;
                _attackBeforeTemporaryEffect = Attack;
                _defenseBeforeTemporaryEffect = Defense;
                _hadRolledStatsBeforeTemporaryEffect = HasRolledStats;
            }

            if (!TrySetStar(newStar, definition, settings, random))
            {
                if (!wasAlreadyTemporary) _hasTemporaryStar = false;
                return false;
            }
            _hasTemporaryStar = true;
            return true;
        }

        public void RevertTemporaryStar()
        {
            if (!_hasTemporaryStar) return;
            Star = _starBeforeTemporaryEffect;
            Attack = _attackBeforeTemporaryEffect;
            Defense = _defenseBeforeTemporaryEffect;
            HasRolledStats = _hadRolledStatsBeforeTemporaryEffect;
            _hasTemporaryStar = false;
        }

        /// <summary>Applies an item stat delta to this detached working copy.</summary>
        internal bool TryApplyItemStatDelta(int attackDelta, int defenseDelta, bool temporary)
        {
            long nextAttack = temporary
                ? (long)TemporaryAttackBonus + attackDelta
                : (long)Attack + attackDelta;
            long nextDefense = temporary
                ? (long)TemporaryDefenseBonus + defenseDelta
                : (long)Defense + defenseDelta;
            if (nextAttack < int.MinValue || nextAttack > int.MaxValue ||
                nextDefense < int.MinValue || nextDefense > int.MaxValue)
                return false;

            if (temporary)
            {
                TemporaryAttackBonus = (int)nextAttack;
                TemporaryDefenseBonus = (int)nextDefense;
            }
            else
            {
                Attack = (int)nextAttack;
                Defense = (int)nextDefense;
            }
            return true;
        }

        /// <summary>Restores all battle-only item changes on this detached working copy.</summary>
        internal void ClearBattleOnlyItemEffects()
        {
            RevertTemporaryStar();
            TemporaryAttackBonus = 0;
            TemporaryDefenseBonus = 0;
        }
        private bool TryRollStats(CharacterDefinition definition, CharacterStatDefinition settings,
            IRandomSource random, int statStar)
        {
            if (definition == null || definition.DefinitionId != DefinitionId || settings == null || random == null)
                return false;
            if (!CharacterStatGenerator.TryGenerate(settings, RunCost, statStar, random,
                out int rolledAttack, out int rolledDefense)) return false;
            Attack = rolledAttack;
            Defense = rolledDefense;
            HasRolledStats = true;
            return true;
        }

        private static void ValidateStar(int star)
        {
            if (star < 1 || star > 3) throw new ArgumentOutOfRangeException(nameof(star));
        }

        private static int CopiesForStar(int star) => star == 1 ? 1 : star == 2 ? 3 : 9;
    }
}
