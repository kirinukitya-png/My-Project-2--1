using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Battle
{
    public sealed class BattleValueSettings
    {
        public decimal BattleCorrectionWeight { get; }
        public decimal DefenseWeight { get; }
        public int ReferenceEnemyATK { get; }
        public int ReferenceEnemyDEF { get; }
        public int ReferenceEnemyGap { get; }
        public BattleTargetDefinition ReferenceEnemyTarget { get; }
        public int AllyAttackMaxStates { get; }
        public int EnemyBlockMaxStates { get; }
        public string EvaluationMode => "static-first-round-exact-v1";

        public BattleValueSettings(decimal battleCorrectionWeight = .10m, decimal defenseWeight = .60m,
            int referenceEnemyATK = 4, int referenceEnemyDEF = 0, int referenceEnemyGap = 0,
            BattleTargetDefinition referenceEnemyTarget = null, int allyAttackMaxStates = 256, int enemyBlockMaxStates = 256)
        {
            if (battleCorrectionWeight < 0m) throw new ArgumentOutOfRangeException(nameof(battleCorrectionWeight));
            if (defenseWeight < 0m) throw new ArgumentOutOfRangeException(nameof(defenseWeight));
            if (referenceEnemyGap < 0 || referenceEnemyGap == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(referenceEnemyGap));
            if (allyAttackMaxStates < 1 || allyAttackMaxStates > AllyRoundAttackEvaluator.MaximumStateLimit)
                throw new ArgumentOutOfRangeException(nameof(allyAttackMaxStates));
            if (enemyBlockMaxStates < 1 || enemyBlockMaxStates > EnemyRoundDefenseEvaluator.MaximumBlockStateLimit)
                throw new ArgumentOutOfRangeException(nameof(enemyBlockMaxStates));
            BattleCorrectionWeight = battleCorrectionWeight;
            DefenseWeight = defenseWeight;
            ReferenceEnemyATK = referenceEnemyATK;
            ReferenceEnemyDEF = referenceEnemyDEF;
            ReferenceEnemyGap = referenceEnemyGap;
            ReferenceEnemyTarget = referenceEnemyTarget ??
                new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard, BattleTargetSelection.Uniform);
            AllyAttackMaxStates = allyAttackMaxStates;
            EnemyBlockMaxStates = enemyBlockMaxStates;
        }
    }

    public sealed class EnemyEvaluationContext
    {
        public IReadOnlyList<EnemyDefenseAttacker> Enemies { get; }
        public IReadOnlyList<EnemyObservation> PublicEnemies { get; }
        public bool IsAvailable { get; }
        public int? PlayerHP { get; }
        public int Columns => 2;
        public int Rows => 3;
        public bool HasAppliedEnemyModifiers => PublicEnemies.Any(e => e.SlotBonusAttack != 0 ||
            e.SlotBonusDefense != 0 || e.SlotBonusHP != 0 || e.SlotBonusGap != 0);
        public bool HasEffectsBeforeOrDuringAllyTurn => Enemies.Any(e => e.InitialEnemy.HP > 0 &&
            (e.InitialEnemy.Effects & (AllyRoundEnemyEffects.BattleStart | AllyRoundEnemyEffects.OnDefeat)) != 0);
        public bool HasFirstEnemyTurnDamageModifiers => PublicEnemies.Any(e =>
            e.HP > 0 && e.Gap == 0 && e.AeEffects.Any(id => id == "AE001" || id == "AE007" || id == "AE008"));
        public bool HasLaterRoundModifiers => PublicEnemies.Any(e => e.HP > 0 && e.AeEffects.Any(id =>
            id == "AE001" || id == "AE003" || id == "AE004" || id == "AE005" ||
            id == "AE006" || id == "AE007" || id == "AE008" || id == "AE009"));
        public bool HasGoldAftermath => PublicEnemies.Any(e => e.HP > 0 &&
            e.AeEffects.Any(id => id == "AE002" || id == "AE010"));

        public EnemyEvaluationContext(IEnumerable<EnemyDefenseAttacker> enemies, int? playerHP = null)
            : this(enemies, playerHP, null)
        {
        }

        private EnemyEvaluationContext(IEnumerable<EnemyDefenseAttacker> enemies, int? playerHP,
            IReadOnlyList<EnemyObservation> publicEnemies)
        {
            IsAvailable = enemies != null;
            Enemies = Array.AsReadOnly(enemies?.OrderBy(e => e.InitialEnemy.Slot).ToArray() ?? Array.Empty<EnemyDefenseAttacker>());
            PlayerHP = playerHP;
            PublicEnemies = publicEnemies ?? Array.AsReadOnly(Array.Empty<EnemyObservation>());
        }

        public static EnemyEvaluationContext FromObservation(GameObservation observation)
        {
            if (observation?.Stage.Phase != ObservationPhase.Preparation)
                return new EnemyEvaluationContext(null);
            int? hp = observation.Player.HasHealth ? (int?)observation.Player.HP : null;
            bool known = observation.Enemies.Count != 0 ||
                observation.Stage.EnemyCategory == Stages.StageEnemyCategory.None;
            return new EnemyEvaluationContext(known ? observation.Enemies.Select(EnemyDefenseAttacker.FromObservation) : null,
                hp, observation.Enemies);
        }
    }

    public sealed class BattleBoardMember
    {
        public UnitBoardMember Member { get; }
        public decimal EffectiveAttack { get; }
        public decimal EffectiveDefense { get; }
        public AllyAttackStatsKind StatsKind { get; }
        public IReadOnlyList<string> Attributes { get; }

        public BattleBoardMember(UnitBoardMember member, decimal effectiveAttack, decimal effectiveDefense,
            AllyAttackStatsKind statsKind = AllyAttackStatsKind.Actual, IEnumerable<string> attributes = null)
        {
            Member = member;
            EffectiveAttack = effectiveAttack;
            EffectiveDefense = effectiveDefense;
            StatsKind = statsKind;
            Attributes = Array.AsReadOnly((attributes ?? Array.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.Ordinal).ToArray());
        }

        public static BattleBoardMember FromObservation(CharacterObservation character, int boardSlot)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            return new BattleBoardMember(new UnitBoardMember(boardSlot, character.DefinitionId, character.Star),
                (decimal)character.Attack + character.TemporaryAttackBonus,
                (decimal)character.Defense + character.TemporaryDefenseBonus, attributes: character.Attributes);
        }

        private static decimal ToDecimal(double value) => (decimal)value;

        internal static BattleBoardMember FromRoll(PurchaseStatRoll roll, int slot, PublicCharacterStats stats)
        {
            var member = new UnitBoardMember(slot, roll.DefinitionId, roll.Star);
            return stats.IsAvailable
                ? new BattleBoardMember(member, ToDecimal(stats.ExpectedAttack.Value) + roll.TemporaryAttackBonus,
                    ToDecimal(stats.ExpectedDefense.Value) + roll.TemporaryDefenseBonus, AllyAttackStatsKind.PublicMean, roll.Attributes)
                : new BattleBoardMember(member, 0m, 0m, AllyAttackStatsKind.Unavailable, roll.Attributes);
        }

        internal BattleBoardMember At(int slot) => new BattleBoardMember(
            new UnitBoardMember(slot, Member.DefinitionId, Member.Star), EffectiveAttack, EffectiveDefense, StatsKind, Attributes);
    }

    public sealed class BattleBoardInput
    {
        public IReadOnlyList<BattleBoardMember> Members { get; }
        public IReadOnlyList<UnitRequirementObservation> UnitRequirements { get; }
        public long? ItemBlock { get; }
        public int Columns { get; }
        public int Rows { get; }

        public BattleBoardInput(IEnumerable<BattleBoardMember> members, IEnumerable<UnitRequirementObservation> requirements,
            long? itemBlock = 0, int columns = 3, int rows = 3)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            Members = Array.AsReadOnly(members.OrderBy(m => m?.Member.SlotIndex ?? -1).ToArray());
            UnitRequirements = Array.AsReadOnly(requirements.OrderBy(r => r?.UnitName, StringComparer.Ordinal).ToArray());
            ItemBlock = itemBlock;
            Columns = columns;
            Rows = rows;
        }

        public static BattleBoardInput FromObservation(GameObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            return new BattleBoardInput(observation.Board.Slots.Where(s => s.Character != null)
                .Select(s => BattleBoardMember.FromObservation(s.Character, s.SlotIndex)), observation.UnitRequirements,
                (long)observation.Player.PermanentPlayerBlockBonus + observation.Player.TemporaryPlayerBlockBonus,
                observation.Board.Columns, observation.Board.Rows);
        }

        /// <summary>Projects the final placement supplied by the caller or a successful existing preview.</summary>
        public BattleBoardInput WithSlot(int slot, BattleBoardMember after)
        {
            ValidateSlot(slot);
            var result = Members.Where(m => m.Member.SlotIndex != slot);
            if (after != null) result = result.Append(after.At(slot));
            return new BattleBoardInput(result, UnitRequirements, ItemBlock, Columns, Rows);
        }

        /// <summary>Applies a projected Board swap after candidate legality has been checked by the caller.</summary>
        public BattleBoardInput WithBoardSwap(int source, int destination)
        {
            ValidateSlot(source);
            ValidateSlot(destination);
            var members = Members.Select(m => m.Member.SlotIndex == source ? m.At(destination) :
                m.Member.SlotIndex == destination ? m.At(source) : m);
            return new BattleBoardInput(members, UnitRequirements, ItemBlock, Columns, Rows);
        }

        private void ValidateSlot(int slot)
        {
            if (slot < 0 || slot >= 9) throw new ArgumentOutOfRangeException(nameof(slot));
        }

        internal int AttributeBlock => Members.SelectMany(member => member.Attributes)
            .GroupBy(attribute => attribute, StringComparer.Ordinal).Count(group => SynergyDetector.IsSharedAttribute(group.Count()));

        internal string CacheKey(int initialBlock)
        {
            var fields = new List<string> { initialBlock.ToString(CultureInfo.InvariantCulture) };
            foreach (var m in Members)
            {
                fields.Add(FormattableString.Invariant($"{m.Member.SlotIndex}/{m.Member.Star}/{(int)m.StatsKind}/") +
                    m.EffectiveAttack.ToString("G29", CultureInfo.InvariantCulture) + "/" +
                    m.EffectiveDefense.ToString("G29", CultureInfo.InvariantCulture));
                fields.Add(Token(m.Member.DefinitionId));
            }
            fields.Add("units");
            foreach (var requirement in UnitRequirements)
            {
                fields.Add(Token(requirement.UnitName));
                fields.Add(requirement.RequiredMemberCount.ToString(CultureInfo.InvariantCulture));
                foreach (var id in requirement.RequiredDefinitionIds) fields.Add(Token(id));
            }
            return string.Join("|", fields);
        }

        private static string Token(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    }
}
