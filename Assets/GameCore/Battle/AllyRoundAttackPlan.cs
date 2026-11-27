using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Battle
{
    public enum AllyAttackStatsKind { Actual, PublicMean, Unavailable }
    public enum AllyRoundAttackStatus { Evaluated, NoEnemy, PublicStatsUnavailable, StateLimitExceeded, InvalidInput, InvalidCombatState }

    public readonly struct AllyAttackMember
    {
        public UnitBoardMember Member { get; }
        public decimal EffectiveAttack { get; }
        public AllyAttackStatsKind StatsKind { get; }

        public AllyAttackMember(UnitBoardMember member, decimal effectiveAttack,
            AllyAttackStatsKind statsKind = AllyAttackStatsKind.Actual)
        {
            Member = member;
            EffectiveAttack = effectiveAttack;
            StatsKind = statsKind;
        }

        public static AllyAttackMember FromObservation(CharacterObservation character)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (character.LocationKind != ObservationLocationKind.Board)
                throw new ArgumentException("The character must occupy a Board slot.", nameof(character));
            return new AllyAttackMember(new UnitBoardMember(character.SlotIndex, character.DefinitionId, character.Star),
                (decimal)character.Attack + character.TemporaryAttackBonus);
        }
    }

    public sealed class AllyRoundAttackAction
    {
        public int Order { get; }
        public int ActorSlot { get; }
        public string UnitName { get; }
        public bool IsUnitMove => UnitName != null;
        public IReadOnlyList<int> ParticipantSlots { get; }
        public decimal Attack { get; }

        internal AllyRoundAttackAction(int order, int actorSlot, decimal attack, EstablishedUnitSlots unit = null)
        {
            Order = order;
            ActorSlot = actorSlot;
            UnitName = unit?.UnitName;
            ParticipantSlots = unit?.ParticipantSlots ?? Array.AsReadOnly(new[] { actorSlot });
            Attack = attack;
        }
    }

    public sealed class AllyRoundAttackPlan
    {
        public AllyRoundAttackStatus Status { get; }
        public IReadOnlyList<AllyRoundAttackAction> Actions { get; }
        public UnitMoveRoundDetails UnitPlan { get; }
        public bool UsesPublicMeanStats { get; }

        private AllyRoundAttackPlan(AllyRoundAttackStatus status, List<AllyRoundAttackAction> actions = null,
            UnitMoveRoundDetails unitPlan = null, bool usesPublicMeanStats = false)
        {
            Status = status;
            Actions = (actions ?? new List<AllyRoundAttackAction>()).AsReadOnly();
            UnitPlan = unitPlan;
            UsesPublicMeanStats = usesPublicMeanStats;
        }

        public static AllyRoundAttackPlan FromRequirements(IReadOnlyList<AllyAttackMember> board,
            IReadOnlyList<UnitRequirementObservation> requirements) => Build(board, members =>
                UnitMoveRoundPlanner.DetectEstablishedUnits(members, requirements));

        public static AllyRoundAttackPlan FromEstablishedUnits(IReadOnlyList<AllyAttackMember> board,
            IReadOnlyList<EstablishedUnitSlots> units) => Build(board, _ => units);

        private static AllyRoundAttackPlan Build(IReadOnlyList<AllyAttackMember> board,
            Func<UnitBoardMember[], IReadOnlyList<EstablishedUnitSlots>> established)
        {
            if (board == null || board.Count > UnitMoveRoundPlanner.SlotCount)
                return new AllyRoundAttackPlan(AllyRoundAttackStatus.InvalidInput);
            bool mean = false;
            bool missing = false;
            var attack = new decimal[UnitMoveRoundPlanner.SlotCount];
            var members = new UnitBoardMember[board.Count];
            for (int i = 0; i < board.Count; i++)
            {
                var entry = board[i];
                if (!Enum.IsDefined(typeof(AllyAttackStatsKind), entry.StatsKind) ||
                    entry.EffectiveAttack < int.MinValue || entry.EffectiveAttack > int.MaxValue ||
                    (entry.StatsKind == AllyAttackStatsKind.Actual && decimal.Truncate(entry.EffectiveAttack) != entry.EffectiveAttack))
                    return new AllyRoundAttackPlan(AllyRoundAttackStatus.InvalidInput);
                if (entry.Member.DefinitionId == null)
                    return new AllyRoundAttackPlan(AllyRoundAttackStatus.InvalidInput);
                members[i] = entry.Member;
                attack[entry.Member.SlotIndex] = entry.EffectiveAttack;
                mean |= entry.StatsKind == AllyAttackStatsKind.PublicMean;
                missing |= entry.StatsKind == AllyAttackStatsKind.Unavailable;
            }

            UnitMoveRoundDetails plan;
            try { plan = UnitMoveRoundPlanner.Plan(members, established(members), includeDetails: true).Details; }
            catch (ArgumentException) { return new AllyRoundAttackPlan(AllyRoundAttackStatus.InvalidInput); }
            if (missing) return new AllyRoundAttackPlan(AllyRoundAttackStatus.PublicStatsUnavailable);

            var actions = new List<AllyRoundAttackAction>();
            int activation = 0;
            foreach (var member in plan.Members)
            {
                while (activation < plan.Activations.Count && plan.Activations[activation].InitiatorSlot == member.Slot)
                {
                    var move = plan.Activations[activation++].Unit;
                    decimal sum = move.ParticipantSlots.Sum(slot => attack[slot]);
                    if (sum < int.MinValue || sum > int.MaxValue)
                        return new AllyRoundAttackPlan(AllyRoundAttackStatus.InvalidCombatState);
                    actions.Add(new AllyRoundAttackAction(actions.Count, member.Slot, sum, move));
                }
                // This flag was captured on the member's turn; final participation is diagnostic only.
                if (member.NormalAttack)
                    actions.Add(new AllyRoundAttackAction(actions.Count, member.Slot, attack[member.Slot]));
            }
            return new AllyRoundAttackPlan(AllyRoundAttackStatus.Evaluated, actions, plan, mean);
        }
    }
}
