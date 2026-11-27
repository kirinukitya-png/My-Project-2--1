using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Synergy
{
    public readonly struct UnitBoardMember
    {
        public int SlotIndex { get; }
        public string DefinitionId { get; }
        public int Star { get; }

        public UnitBoardMember(int slotIndex, string definitionId, int star)
        {
            if (slotIndex < 0 || slotIndex >= UnitMoveRoundPlanner.SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            if (string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("Definition ID is required.", nameof(definitionId));
            if (star < 1 || star > 3) throw new ArgumentOutOfRangeException(nameof(star));
            SlotIndex = slotIndex;
            DefinitionId = definitionId;
            Star = star;
        }
    }

    public sealed class EstablishedUnitSlots
    {
        public string UnitName { get; }
        public IReadOnlyList<int> ParticipantSlots { get; }

        public EstablishedUnitSlots(string unitName, IEnumerable<int> participantSlots)
        {
            if (string.IsNullOrWhiteSpace(unitName))
                throw new ArgumentException("Unit name is required.", nameof(unitName));
            if (participantSlots == null) throw new ArgumentNullException(nameof(participantSlots));
            int[] slots = participantSlots.ToArray();
            if (slots.Length < 2 || slots.Length > UnitMoveRoundPlanner.SlotCount)
                throw new ArgumentException("A Unit must have two to nine participants.", nameof(participantSlots));
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] < 0 || slots[i] >= UnitMoveRoundPlanner.SlotCount ||
                    (i > 0 && slots[i - 1] >= slots[i]))
                    throw new ArgumentException("Participant slots must be in range and strictly ascending.", nameof(participantSlots));
            UnitName = unitName;
            ParticipantSlots = Array.AsReadOnly(slots);
        }
    }

    public sealed class UnitMoveRoundDetails
    {
        public IReadOnlyList<EstablishedUnitSlots> EstablishedUnits { get; }
        public IReadOnlyList<(EstablishedUnitSlots Unit, int InitiatorSlot)> Activations { get; }
        public IReadOnlyList<EstablishedUnitSlots> UnusedUnits { get; }
        public IReadOnlyList<(int Slot, int Count, int Limit, bool NormalAttack)> Members { get; }

        internal UnitMoveRoundDetails(EstablishedUnitSlots[] established,
            List<(EstablishedUnitSlots, int)> activations, List<EstablishedUnitSlots> unused,
            List<(int, int, int, bool)> members)
        {
            EstablishedUnits = Array.AsReadOnly(established);
            Activations = activations.AsReadOnly();
            UnusedUnits = unused.AsReadOnly();
            Members = members.AsReadOnly();
        }
    }

    public readonly struct UnitMoveRoundResult
    {
        public int EstablishedCount { get; }
        public int ActivationCount { get; }
        public int CompetitionBlockedCount => EstablishedCount - ActivationCount;
        public UnitMoveRoundDetails Details { get; }

        internal UnitMoveRoundResult(int established, int activated, UnitMoveRoundDetails details)
        {
            EstablishedCount = established;
            ActivationCount = activated;
            Details = details;
        }
    }

    internal static class UnitMovePriority
    {
        internal static int ParticipationLimit(int star) => Math.Max(1, Math.Min(3, star));

        internal static int Compare(string leftName, IReadOnlyList<int> left, string rightName, IReadOnlyList<int> right)
        {
            int result = right.Count.CompareTo(left.Count);
            if (result != 0) return result;
            for (int i = 0; i < left.Count; i++)
            {
                result = left[i].CompareTo(right[i]);
                if (result != 0) return result;
            }
            return string.CompareOrdinal(leftName, rightName);
        }
    }

    /// <summary>Plans one ally round with every member able to act and no enemy defeat or changing effects.</summary>
    public static class UnitMoveRoundPlanner
    {
        internal const int SlotCount = BoardState.StandardColumns * BoardState.StandardRows;

        /// <summary>Unlisted slots are empty. Requirements are the public selected-roster Unit requirements.</summary>
        public static IReadOnlyList<EstablishedUnitSlots> DetectEstablishedUnits(
            IReadOnlyList<UnitBoardMember> board, IReadOnlyList<UnitRequirementObservation> requirements)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            var limits = new int[SlotCount];
            var positions = ReadBoard(board, limits);
            var established = new List<EstablishedUnitSlots>();
            var slots = new List<int>(SlotCount);
            foreach (var requirement in requirements)
            {
                if (requirement == null) throw new ArgumentException("A Unit requirement is null.", nameof(requirements));
                slots.Clear();
                foreach (string definition in requirement.RequiredDefinitionIds)
                {
                    if (!positions.TryGetValue(definition, out int slot)) break;
                    slots.Add(slot);
                }
                if (slots.Count == requirement.RequiredMemberCount &&
                    SynergyDetector.AreConnected(slots, BoardState.StandardColumns))
                {
                    slots.Sort();
                    established.Add(new EstablishedUnitSlots(requirement.UnitName, slots));
                }
            }
            return established.AsReadOnly();
        }

        /// <summary>Accepts established Units in any order and leaves both inputs unchanged.</summary>
        public static UnitMoveRoundResult Plan(IReadOnlyList<UnitBoardMember> board,
            IReadOnlyList<EstablishedUnitSlots> establishedUnits, bool includeDetails = false)
        {
            if (establishedUnits == null) throw new ArgumentNullException(nameof(establishedUnits));
            var limits = new int[SlotCount];
            ReadBoard(board, limits);
            if (establishedUnits.Count == 0 && !includeDetails) return default;
            var units = establishedUnits.ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var bySlot = new List<int>[SlotCount];
            foreach (var unit in units)
            {
                if (unit == null || !names.Add(unit.UnitName))
                    throw new ArgumentException("Established Units must be non-null and uniquely named.", nameof(establishedUnits));
                foreach (int slot in unit.ParticipantSlots)
                    if (limits[slot] == 0)
                        throw new ArgumentException("An established Unit references an empty slot.", nameof(establishedUnits));
            }
            if (units.Length > 1) Array.Sort(units, (a, b) =>
                UnitMovePriority.Compare(a.UnitName, a.ParticipantSlots, b.UnitName, b.ParticipantSlots));
            for (int i = 0; i < units.Length; i++)
                foreach (int slot in units[i].ParticipantSlots)
                    (bySlot[slot] ??= new List<int>()).Add(i);

            var counts = new int[SlotCount];
            var used = new bool[units.Length];
            var activations = includeDetails ? new List<(EstablishedUnitSlots, int)>() : null;
            var normalAttack = includeDetails ? new bool[SlotCount] : null;
            int activated = 0;
            for (int actor = 0; actor < SlotCount; actor++)
            {
                if (counts[actor] >= limits[actor]) continue;
                // During this turn, a skipped move cannot regain eligibility as participation only increases.
                if (bySlot[actor] != null)
                    foreach (int i in bySlot[actor])
                    {
                        if (counts[actor] >= limits[actor]) break;
                        if (used[i] || !HasRemainingCapacity(units[i].ParticipantSlots, counts, limits)) continue;
                        used[i] = true;
                        activated++;
                        foreach (int slot in units[i].ParticipantSlots) counts[slot]++;
                        activations?.Add((units[i], actor));
                    }
                if (normalAttack != null) normalAttack[actor] = counts[actor] == 0;
            }

            return new UnitMoveRoundResult(units.Length, activated, includeDetails
                ? BuildDetails(units, used, activations, counts, limits, normalAttack) : null);
        }

        private static bool HasRemainingCapacity(IReadOnlyList<int> slots, int[] counts, int[] limits)
        {
            foreach (int slot in slots)
                if (counts[slot] >= limits[slot]) return false;
            return true;
        }

        private static UnitMoveRoundDetails BuildDetails(EstablishedUnitSlots[] units, bool[] used,
            List<(EstablishedUnitSlots, int)> activations, int[] counts, int[] limits, bool[] normalAttack)
        {
            var unused = new List<EstablishedUnitSlots>();
            for (int i = 0; i < units.Length; i++)
                if (!used[i]) unused.Add(units[i]);
            var members = new List<(int, int, int, bool)>();
            for (int i = 0; i < SlotCount; i++)
                if (limits[i] != 0) members.Add((i, counts[i], limits[i], normalAttack[i]));
            return new UnitMoveRoundDetails(units, activations, unused, members);
        }

        private static Dictionary<string, int> ReadBoard(IReadOnlyList<UnitBoardMember> board, int[] limits)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var member in board)
            {
                if (member.DefinitionId == null || limits[member.SlotIndex] != 0 ||
                    !positions.TryAdd(member.DefinitionId, member.SlotIndex))
                    throw new ArgumentException("Board members must be initialized and use unique slots and definitions.", nameof(board));
                limits[member.SlotIndex] = UnitMovePriority.ParticipationLimit(member.Star);
            }
            return positions;
        }
    }
}
