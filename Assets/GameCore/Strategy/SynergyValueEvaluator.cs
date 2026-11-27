using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Strategy
{
    public sealed class SynergyValueSettings
    {
        public decimal AttributeCoefficient { get; }
        public decimal ProgressCoefficient { get; }
        public decimal ActivationCoefficient { get; }

        public SynergyValueSettings(decimal attributeCoefficient = 5m, decimal progressCoefficient = 2m,
            decimal activationCoefficient = 8m)
        {
            if (attributeCoefficient < 0) throw new ArgumentOutOfRangeException(nameof(attributeCoefficient));
            if (progressCoefficient < 0) throw new ArgumentOutOfRangeException(nameof(progressCoefficient));
            if (activationCoefficient < 0) throw new ArgumentOutOfRangeException(nameof(activationCoefficient));
            AttributeCoefficient = attributeCoefficient;
            ProgressCoefficient = progressCoefficient;
            ActivationCoefficient = activationCoefficient;
        }
    }

    public readonly struct SynergyValue
    {
        public decimal AttributeValue { get; }
        public decimal UnitProgressValue { get; }
        public decimal UnitActivationValue { get; }
        public decimal TotalSynergyValue => AttributeValue + UnitProgressValue + UnitActivationValue;

        internal SynergyValue(decimal attribute, decimal progress, decimal activation)
        {
            AttributeValue = attribute;
            UnitProgressValue = progress;
            UnitActivationValue = activation;
        }
    }

    public sealed class SynergyValueDetails
    {
        public IReadOnlyList<string> EstablishedAttributes { get; }
        public IReadOnlyList<(string UnitName, int LargestConnectedCount, decimal ProgressValue)> UnitProgress { get; }
        /// <summary>Tags observed on board that have no public requirement in this roster.</summary>
        public IReadOnlyList<string> TagsWithoutPublicRequirements { get; }
        public UnitMoveRoundDetails Round { get; }

        internal SynergyValueDetails(string[] attributes, List<(string, int, decimal)> progress,
            string[] excludedTags, UnitMoveRoundDetails round)
        {
            EstablishedAttributes = Array.AsReadOnly(attributes);
            UnitProgress = progress.AsReadOnly();
            TagsWithoutPublicRequirements = Array.AsReadOnly(excludedTags);
            Round = round;
        }
    }

    public readonly struct SynergyEvaluation
    {
        public SynergyValue Value { get; }
        public int EstablishedAttributeCount { get; }
        public int EligibleUnitCount { get; }
        public int EstablishedUnitCount { get; }
        public int ActivatableUnitCount { get; }
        public int CompetitionBlockedCount => EstablishedUnitCount - ActivatableUnitCount;
        public SynergyValueDetails Details { get; }

        internal SynergyEvaluation(SynergyValue value, int attributeCount, int eligibleUnits,
            UnitMoveRoundResult round, SynergyValueDetails details)
        {
            Value = value;
            EstablishedAttributeCount = attributeCount;
            EligibleUnitCount = eligibleUnits;
            EstablishedUnitCount = round.EstablishedCount;
            ActivatableUnitCount = round.ActivationCount;
            Details = details;
        }
    }

    public readonly struct SynergyComparison
    {
        public SynergyEvaluation Before { get; }
        public SynergyEvaluation After { get; }
        public SynergyValue Delta => new SynergyValue(
            After.Value.AttributeValue - Before.Value.AttributeValue,
            After.Value.UnitProgressValue - Before.Value.UnitProgressValue,
            After.Value.UnitActivationValue - Before.Value.UnitActivationValue);

        internal SynergyComparison(SynergyEvaluation before, SynergyEvaluation after)
        {
            Before = before;
            After = after;
        }
    }

    /// <summary>Enemy-independent board value in a fixed public roster and settings context.</summary>
    public sealed class SynergyValueEvaluator
    {
        private const int SlotCount = UnitMoveRoundPlanner.SlotCount;
        private static readonly int[] NeighborMasks = BuildNeighborMasks();
        private readonly GameObservation _observation;
        private readonly Dictionary<string, DefinitionInfo> _definitions;
        private readonly UnitRequirementObservation[] _requirements;
        private readonly string[] _attributeNames;
        private readonly HashSet<string> _requirementNames;

        public SynergyValueSettings Settings { get; }

        public SynergyValueEvaluator(GameObservation observation, SynergyValueSettings settings = null)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            if (observation.Board.Columns != BoardState.StandardColumns || observation.Board.Slots.Count != SlotCount)
                throw new ArgumentException("The standard 3x3 board is required.", nameof(observation));
            Settings = settings ?? new SynergyValueSettings();
            _requirements = observation.UnitRequirements.ToArray();
            _requirementNames = new HashSet<string>(_requirements.Select(r => r.UnitName), StringComparer.Ordinal);
            _attributeNames = observation.SelectedRoster.SelectMany(d => d.Attributes)
                .Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.Ordinal)
                .OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
            var attributeIndices = _attributeNames.Select((name, index) => (name, index))
                .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);
            var unitsByDefinition = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < _requirements.Length; i++)
                foreach (string id in _requirements[i].RequiredDefinitionIds)
                {
                    if (!unitsByDefinition.TryGetValue(id, out var entries))
                        unitsByDefinition.Add(id, entries = new List<int>());
                    entries.Add(i);
                }
            _definitions = observation.SelectedRoster.ToDictionary(d => d.DefinitionId, d => new DefinitionInfo(
                d.Attributes.Where(attributeIndices.ContainsKey).Distinct(StringComparer.Ordinal)
                    .Select(tag => attributeIndices[tag]).ToArray(),
                unitsByDefinition.TryGetValue(d.DefinitionId, out var units) ? units.ToArray() : Array.Empty<int>(),
                d.Units), StringComparer.Ordinal);
        }

        public SynergyEvaluation EvaluateCurrent(bool includeDetails = false) =>
            EvaluateSnapshot(ReadCurrentBoard(), includeDetails);

        public SynergyEvaluation Evaluate(IReadOnlyList<UnitBoardMember> board, bool includeDetails = false) =>
            EvaluateSnapshot(Snapshot(board), includeDetails);

        public Baseline CreateBaseline(bool includeDetails = false) =>
            new Baseline(this, ReadCurrentBoard(), includeDetails, _observation);

        public Baseline CreateBaseline(IReadOnlyList<UnitBoardMember> board, bool includeDetails = false) =>
            new Baseline(this, Snapshot(board), includeDetails, null);

        private SynergyEvaluation EvaluateSnapshot(UnitBoardMember[] board, bool includeDetails)
        {
            var attributeCounts = new int[_attributeNames.Length];
            var unitMasks = new int[_requirements.Length];
            var excluded = includeDetails ? new SortedSet<string>(StringComparer.Ordinal) : null;
            foreach (var member in board)
            {
                var definition = _definitions[member.DefinitionId];
                foreach (int attribute in definition.Attributes) attributeCounts[attribute]++;
                foreach (int unit in definition.Requirements) unitMasks[unit] |= 1 << member.SlotIndex;
                if (excluded != null)
                    foreach (string tag in definition.UnitTags)
                        if (!string.IsNullOrWhiteSpace(tag) && !_requirementNames.Contains(tag)) excluded.Add(tag);
            }

            int attributeCount = 0;
            var attributes = includeDetails ? new List<string>() : null;
            for (int i = 0; i < attributeCounts.Length; i++)
                if (SynergyDetector.IsSharedAttribute(attributeCounts[i]))
                {
                    attributeCount++;
                    attributes?.Add(_attributeNames[i]);
                }

            int progressSteps = 0;
            var established = new List<EstablishedUnitSlots>();
            var progress = includeDetails ? new List<(string, int, decimal)>(_requirements.Length) : null;
            for (int i = 0; i < _requirements.Length; i++)
            {
                var requirement = _requirements[i];
                int connected = LargestComponent(unitMasks[i]);
                int steps = Math.Max(0, connected - 1);
                progressSteps += steps;
                if (connected == requirement.RequiredMemberCount)
                    established.Add(new EstablishedUnitSlots(requirement.UnitName, Slots(unitMasks[i])));
                if (progress != null)
                    progress.Add((requirement.UnitName, connected, steps * Settings.ProgressCoefficient));
            }

            var round = UnitMoveRoundPlanner.Plan(board, established, includeDetails);
            var value = new SynergyValue(attributeCount * Settings.AttributeCoefficient,
                progressSteps * Settings.ProgressCoefficient, round.ActivationCount * Settings.ActivationCoefficient);
            return new SynergyEvaluation(value, attributeCount, _requirements.Length, round, includeDetails
                ? new SynergyValueDetails(attributes.ToArray(), progress, excluded.ToArray(), round.Details) : null);
        }

        private UnitBoardMember[] ReadCurrentBoard() => Snapshot(_observation.Board.Slots
            .Where(slot => slot.IsOccupied).Select(slot => new UnitBoardMember(slot.SlotIndex,
                slot.Character.DefinitionId, slot.Character.Star)).ToArray());

        private UnitBoardMember[] Snapshot(IReadOnlyList<UnitBoardMember> board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            var members = new UnitBoardMember[board.Count];
            var definitions = new HashSet<string>(StringComparer.Ordinal);
            int occupied = 0;
            for (int i = 0; i < board.Count; i++)
            {
                var member = board[i];
                int slot = 1 << member.SlotIndex;
                if (member.DefinitionId == null || !_definitions.ContainsKey(member.DefinitionId) ||
                    (occupied & slot) != 0 || !definitions.Add(member.DefinitionId))
                    throw new ArgumentException("Board must have unique slots and known, unique definitions.", nameof(board));
                occupied |= slot;
                members[i] = member;
            }
            Array.Sort(members, (a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
            return members;
        }

        private static int[] BuildNeighborMasks()
        {
            var masks = new int[SlotCount];
            for (int a = 0; a < SlotCount; a++)
                for (int b = 0; b < SlotCount; b++)
                    if (SynergyDetector.AreOrthogonalNeighbors(a, b, BoardState.StandardColumns))
                        masks[a] |= 1 << b;
            return masks;
        }

        private static IEnumerable<int> Slots(int mask)
        {
            for (int slot = 0; slot < SlotCount; slot++)
                if ((mask & (1 << slot)) != 0) yield return slot;
        }

        private static int LargestComponent(int remaining)
        {
            int largest = 0;
            while (remaining != 0)
            {
                int frontier = remaining & -remaining;
                remaining ^= frontier;
                int size = 0;
                while (frontier != 0)
                {
                    int adjacent = 0;
                    for (int slot = 0; slot < SlotCount; slot++)
                        if ((frontier & (1 << slot)) != 0)
                        {
                            size++;
                            adjacent |= NeighborMasks[slot];
                        }
                    frontier = adjacent & remaining;
                    remaining ^= frontier;
                }
                largest = Math.Max(largest, size);
            }
            return largest;
        }

        private sealed class DefinitionInfo
        {
            internal readonly int[] Attributes;
            internal readonly int[] Requirements;
            internal readonly IReadOnlyList<string> UnitTags;

            internal DefinitionInfo(int[] attributes, int[] requirements, IReadOnlyList<string> unitTags)
            {
                Attributes = attributes;
                Requirements = requirements;
                UnitTags = unitTags;
            }
        }

        public sealed class Baseline
        {
            private readonly SynergyValueEvaluator _evaluator;
            private readonly UnitBoardMember[] _board;
            private readonly GameObservation _source;
            public SynergyEvaluation Before { get; }

            internal Baseline(SynergyValueEvaluator evaluator, UnitBoardMember[] board, bool includeDetails,
                GameObservation source)
            {
                _evaluator = evaluator;
                _board = board;
                _source = source;
                Before = evaluator.EvaluateSnapshot(board, includeDetails);
            }

            public SynergyComparison Compare(IReadOnlyList<UnitBoardMember> after)
            {
                var snapshot = _evaluator.Snapshot(after);
                // Snapshots are normalized by slot, so unchanged boards can reuse the entire baseline.
                bool same = snapshot.Length == _board.Length;
                for (int i = 0; same && i < snapshot.Length; i++)
                    same = snapshot[i].SlotIndex == _board[i].SlotIndex &&
                        snapshot[i].DefinitionId == _board[i].DefinitionId && snapshot[i].Star == _board[i].Star;
                return new SynergyComparison(Before, same ? Before :
                    _evaluator.EvaluateSnapshot(snapshot, Before.Details != null));
            }

            /// <summary>Applies a successful preview computed from this baseline's observation.</summary>
            public SynergyComparison ComparePurchasePreview(CharacterPurchasePreviewResult preview)
            {
                if (_source == null)
                    throw new InvalidOperationException("A purchase preview requires an observation baseline.");
                if (preview == null) throw new ArgumentNullException(nameof(preview));
                if (!preview.Succeeded)
                    throw new ArgumentException("A successful purchase preview is required.", nameof(preview));
                if (!ReferenceEquals(preview.SourceObservation, _source))
                    throw new ArgumentException("Purchase preview must come from the baseline observation.", nameof(preview));

                UnitBoardMember?[] slots = null;
                foreach (var change in preview.SlotChanges)
                {
                    if (change.Location.Kind != CharacterLocationKind.Board) continue;
                    if (slots == null)
                    {
                        slots = new UnitBoardMember?[SlotCount];
                        foreach (var current in _board) slots[current.SlotIndex] = current;
                    }
                    int slot = change.Location.SlotIndex;
                    var member = change.CharacterAfter;
                    slots[slot] = member == null ? (UnitBoardMember?)null :
                        new UnitBoardMember(slot, member.DefinitionId, member.Star);
                }
                return slots != null ? Compare(slots.Where(member => member.HasValue).Select(member => member.Value).ToArray())
                    : new SynergyComparison(Before, Before);
            }
        }
    }
}
