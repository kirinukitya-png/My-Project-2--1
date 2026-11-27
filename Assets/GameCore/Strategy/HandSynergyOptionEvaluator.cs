using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Synergy;
using BoardKey = System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int, int>>;

namespace ProjectNST.GameCore.Strategy
{
    public sealed class HandSynergyOptionSettings
    {
        public decimal Coefficient { get; }

        public HandSynergyOptionSettings(decimal coefficient = 0.15m)
        {
            if (coefficient < 0m || coefficient >= 1m)
                throw new ArgumentOutOfRangeException(nameof(coefficient));
            Coefficient = coefficient;
        }
    }

    public readonly struct HandSynergySlot
    {
        public int SlotIndex { get; }
        public HandSlotContentKind Kind { get; }
        public string DefinitionId { get; }
        public int Star { get; }
        public string InstanceId { get; }

        public HandSynergySlot(int slotIndex, HandSlotContentKind kind, string definitionId = null,
            int star = 0, string instanceId = null)
        {
            if (slotIndex < 0 || slotIndex >= HandState.StandardSlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            switch (kind)
            {
                case HandSlotContentKind.Character:
                    if (string.IsNullOrWhiteSpace(definitionId))
                        throw new ArgumentException("Character definition is required.", nameof(definitionId));
                    if (star < 1 || star > 3) throw new ArgumentOutOfRangeException(nameof(star));
                    break;
                case HandSlotContentKind.Empty:
                case HandSlotContentKind.Item:
                    if (definitionId != null || star != 0 || instanceId != null)
                        throw new ArgumentException("Only Character slots contain Character information.", nameof(kind));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            SlotIndex = slotIndex;
            Kind = kind;
            DefinitionId = definitionId;
            Star = star;
            InstanceId = instanceId;
        }
    }

    public sealed class HandSynergyOptionState
    {
        public int Level { get; }
        public IReadOnlyList<UnitBoardMember> Board { get; }
        public IReadOnlyList<HandSynergySlot> Hand { get; }

        /// <summary>Unlisted slots are empty. Both collections are snapshotted and ordered by slot.</summary>
        public HandSynergyOptionState(int level, IEnumerable<UnitBoardMember> board, IEnumerable<HandSynergySlot> hand)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (hand == null) throw new ArgumentNullException(nameof(hand));
            var boardSlots = board.OrderBy(x => x.SlotIndex).ToArray();
            var handSlots = hand.OrderBy(x => x.SlotIndex).ToArray();
            if (boardSlots.Any(x => x.DefinitionId == null) ||
                boardSlots.Select(x => x.SlotIndex).Distinct().Count() != boardSlots.Length ||
                boardSlots.Select(x => x.DefinitionId).Distinct(StringComparer.Ordinal).Count() != boardSlots.Length)
                throw new ArgumentException("Board members must have unique slots and definitions.", nameof(board));
            if (handSlots.Select(x => x.SlotIndex).Distinct().Count() != handSlots.Length)
                throw new ArgumentException("Hand slots must be unique.", nameof(hand));
            Level = level;
            Board = Array.AsReadOnly(boardSlots);
            Hand = Array.AsReadOnly(handSlots);
        }
    }

    public enum HandSynergyOptionStatus { Disabled, OverLevel, NoLegalCandidate, NoImprovement, Positive }

    public sealed class HandSynergyOptionDetails
    {
        public decimal Coefficient { get; }
        public SynergyValue? CurrentSynergy { get; }
        public SynergyValue? BestAfterSynergy { get; }
        public SynergyValue? BestDelta { get; }
        public HandSynergySlot? BestHand { get; }
        public int? BestBoardSlot { get; }
        public UnitBoardMember? ExchangedCharacter { get; }
        public bool? IsExchange => BestBoardSlot.HasValue ? ExchangedCharacter.HasValue : (bool?)null;
        public int? LegalCandidates { get; }
        public int NewEvaluations { get; }
        public int SafeSkips { get; }
        public int CacheReuses { get; }

        internal HandSynergyOptionDetails(decimal coefficient, SynergyValue? current = null,
            SynergyValue? best = null, SynergyValue? delta = null, HandSynergySlot? hand = null,
            int? boardSlot = null, UnitBoardMember? exchanged = null, int? legal = null,
            int evaluated = 0, int skipped = 0, int reused = 0)
        {
            Coefficient = coefficient;
            CurrentSynergy = current;
            BestAfterSynergy = best;
            BestDelta = delta;
            BestHand = hand;
            BestBoardSlot = boardSlot;
            ExchangedCharacter = exchanged;
            LegalCandidates = legal;
            NewEvaluations = evaluated;
            SafeSkips = skipped;
            CacheReuses = reused;
        }
    }

    public readonly struct HandSynergyOptionEvaluation
    {
        public decimal HandUnitOption { get; }
        public HandSynergyOptionStatus Status { get; }
        public HandSynergyOptionDetails Details { get; }

        internal HandSynergyOptionEvaluation(decimal value, HandSynergyOptionStatus status, HandSynergyOptionDetails details = null)
        {
            HandUnitOption = value;
            Status = status;
            Details = details;
        }
    }

    public readonly struct HandSynergyOptionComparison
    {
        public HandSynergyOptionEvaluation Before { get; }
        public HandSynergyOptionEvaluation After { get; }
        public decimal Delta => After.HandUnitOption - Before.HandUnitOption;

        internal HandSynergyOptionComparison(HandSynergyOptionEvaluation before, HandSynergyOptionEvaluation after)
        {
            Before = before;
            After = after;
        }
    }

    /// <summary>Evaluates one legal Hand deployment in a fixed public context. Use one instance per Decision.</summary>
    public sealed class HandSynergyOptionEvaluator
    {
        private const int BoardSlots = UnitMoveRoundPlanner.SlotCount;
        private const int HandSlots = HandState.StandardSlotCount;
        private readonly GameObservation _source;
        private readonly SynergyValueEvaluator _synergy;
        private readonly Dictionary<string, int> _definitionIndices;
        private readonly HashSet<string> _relevantDefinitions;
        private readonly Dictionary<BoardKey, SynergyValue> _boards;
        private readonly Dictionary<(BoardKey Board, (int, int, int, int, int) Hand, int Level),
            HandSynergyOptionEvaluation> _options;
        private readonly bool _useSafeSkips;
        private int _synergyEvaluations, _synergyCacheHits, _optionCacheHits;
        private int _optionRequests, _optionSearches, _legalOptions, _safeSkips, _virtualSynergyNew;
        internal (int BoardEntries, int OptionEntries, int OptionRequests, int OptionSearches,
            int LegalOptions, int SafeSkips, int VirtualSynergyNew) DiagnosticCounts =>
            (_boards?.Count ?? 0, _options?.Count ?? 0, _optionRequests, _optionSearches,
                _legalOptions, _safeSkips, _virtualSynergyNew);
        internal SynergyEvaluation DescribeBoard(HandSynergyOptionState state) => _synergy.Evaluate(state.Board, true);

        public HandSynergyOptionSettings Settings { get; }
        public (int SynergyEvaluations, int SynergyCacheHits, int OptionCacheHits) CacheStatistics =>
            (_synergyEvaluations, _synergyCacheHits, _optionCacheHits);

        public HandSynergyOptionEvaluator(GameObservation source, SynergyValueSettings synergySettings = null,
            HandSynergyOptionSettings settings = null, bool useCache = true, bool useSafeSkips = true)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (source.Hand.SlotCount != HandSlots)
                throw new ArgumentException("The standard five-slot Hand is required.", nameof(source));
            _synergy = new SynergyValueEvaluator(source, synergySettings);
            Settings = settings ?? new HandSynergyOptionSettings();
            _definitionIndices = source.SelectedRoster.Select((d, i) => (d.DefinitionId, Index: (i + 1) * 4))
                .ToDictionary(x => x.DefinitionId, x => x.Index, StringComparer.Ordinal);
            _relevantDefinitions = new HashSet<string>(source.UnitRequirements.SelectMany(r => r.RequiredDefinitionIds)
                .Concat(source.SelectedRoster.Where(d => d.Attributes.Any(a => !string.IsNullOrWhiteSpace(a)))
                    .Select(d => d.DefinitionId)), StringComparer.Ordinal);
            _useSafeSkips = useSafeSkips;
            if (useCache)
            {
                _boards = new Dictionary<BoardKey, SynergyValue>();
                if (Settings.Coefficient != 0)
                    _options = new Dictionary<(BoardKey, (int, int, int, int, int), int), HandSynergyOptionEvaluation>();
            }
        }

        public HandSynergyOptionEvaluation EvaluateCurrent(bool includeDetails = false) =>
            Evaluate(ReadCurrent(), includeDetails);

        public Baseline CreateBaseline(bool includeDetails = false) => new Baseline(this, ReadCurrent(), includeDetails, true);

        public Baseline CreateBaseline(HandSynergyOptionState state, bool includeDetails = false) =>
            new Baseline(this, state, includeDetails, false);

        public HandSynergyOptionEvaluation Evaluate(HandSynergyOptionState state, bool includeDetails = false) =>
            Evaluate(state, includeDetails, null);

        internal HandSynergyOptionEvaluation Evaluate(HandSynergyOptionState state, bool includeDetails, SynergyValue? knownBoard)
        {
            _optionRequests++;
            Validate(state);
            if (Settings.Coefficient == 0) return Empty(HandSynergyOptionStatus.Disabled, includeDetails);
            int capacity = Math.Min(state.Level, BoardSlots);
            if (state.Board.Count > capacity) return Empty(HandSynergyOptionStatus.OverLevel, includeDetails);
            if (!state.Hand.Any(h => h.Kind == HandSlotContentKind.Character))
                return Empty(HandSynergyOptionStatus.NoLegalCandidate, includeDetails, 0);

            var boardKey = _boards == null ? default : KeyOf(state.Board);
            var optionKey = (boardKey, _options == null ? default : HandKeyOf(state.Hand), state.Level);
            if (!includeDetails && _options != null && _options.TryGetValue(optionKey, out var cached))
            {
                _optionCacheHits++;
                return cached;
            }
            _optionSearches++;

            var slots = new UnitBoardMember?[BoardSlots];
            foreach (var member in state.Board) slots[member.SlotIndex] = member;
            var candidateBoard = new List<UnitBoardMember>(BoardSlots);
            SynergyValue current = default, best = default, bestDelta = default;
            HandSynergySlot bestHand = default;
            UnitBoardMember? bestOccupant = null;
            int bestSlot = -1, legal = 0, evaluated = 0, skipped = 0, reused = 0;
            foreach (var hand in state.Hand)
            {
                if (hand.Kind != HandSlotContentKind.Character) continue;
                int sameDefinitionSlot = -1;
                foreach (var member in state.Board)
                    if (member.DefinitionId == hand.DefinitionId) sameDefinitionSlot = member.SlotIndex;
                for (int slot = 0; slot < BoardSlots; slot++)
                {
                    var occupant = slots[slot];
                    if ((!occupant.HasValue && state.Board.Count >= capacity) ||
                        (sameDefinitionSlot >= 0 && sameDefinitionSlot != slot)) continue;
                    if (legal++ == 0) current = knownBoard ?? ReadSynergy(state.Board, boardKey, out _);
                    SynergyValue after;
                    if (_useSafeSkips && IsSameSynergy(hand, occupant))
                    {
                        skipped++;
                        after = current;
                    }
                    else
                    {
                        slots[slot] = new UnitBoardMember(slot, hand.DefinitionId, hand.Star);
                        candidateBoard.Clear();
                        foreach (var member in slots)
                            if (member.HasValue) candidateBoard.Add(member.Value);
                        after = ReadSynergy(candidateBoard, _boards == null ? default : KeyOf(candidateBoard), out bool reusedBoard);
                        slots[slot] = occupant;
                        if (reusedBoard) reused++;
                        else evaluated++;
                    }
                    var delta = new SynergyValue(after.AttributeValue - current.AttributeValue,
                        after.UnitProgressValue - current.UnitProgressValue,
                        after.UnitActivationValue - current.UnitActivationValue);
                    if (bestSlot >= 0 && delta.TotalSynergyValue <= bestDelta.TotalSynergyValue) continue;
                    best = after;
                    bestDelta = delta;
                    bestHand = hand;
                    bestSlot = slot;
                    bestOccupant = occupant;
                }
            }

            _legalOptions += legal;
            _safeSkips += skipped;
            _virtualSynergyNew += evaluated;
            if (legal == 0) return Empty(HandSynergyOptionStatus.NoLegalCandidate, includeDetails, 0);
            decimal value = Settings.Coefficient * Math.Max(0, bestDelta.TotalSynergyValue);
            var status = bestDelta.TotalSynergyValue > 0 ? HandSynergyOptionStatus.Positive : HandSynergyOptionStatus.NoImprovement;
            var result = new HandSynergyOptionEvaluation(value, status);
            if (_options != null) _options[optionKey] = result;
            return includeDetails ? new HandSynergyOptionEvaluation(value, status,
                new HandSynergyOptionDetails(Settings.Coefficient, current, best, bestDelta, bestHand,
                    bestSlot, bestOccupant, legal, evaluated, skipped, reused)) : result;
        }

        private void Validate(HandSynergyOptionState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Level > _source.Player.MaximumLevel)
                throw new ArgumentOutOfRangeException(nameof(state), "Level exceeds the public maximum.");
            foreach (var member in state.Board)
                if (!_definitionIndices.ContainsKey(member.DefinitionId))
                    throw new ArgumentException("Unknown Board definition in this context.", nameof(state));
            foreach (var member in state.Hand)
                if (member.Kind == HandSlotContentKind.Character && !_definitionIndices.ContainsKey(member.DefinitionId))
                    throw new ArgumentException("Unknown Hand definition in this context.", nameof(state));
        }

        private HandSynergyOptionEvaluation Empty(HandSynergyOptionStatus status, bool detail, int? legal = null) =>
            new HandSynergyOptionEvaluation(0m, status, detail
                ? new HandSynergyOptionDetails(Settings.Coefficient, legal: legal) : null);

        private bool IsSameSynergy(HandSynergySlot incoming, UnitBoardMember? occupant) =>
            (occupant.HasValue && occupant.Value.DefinitionId == incoming.DefinitionId && occupant.Value.Star == incoming.Star) ||
            (!_relevantDefinitions.Contains(incoming.DefinitionId) &&
                (!occupant.HasValue || !_relevantDefinitions.Contains(occupant.Value.DefinitionId)));

        private SynergyValue ReadSynergy(IReadOnlyList<UnitBoardMember> board, BoardKey key, out bool reused)
        {
            if (_boards != null && _boards.TryGetValue(key, out var cached))
            {
                reused = true;
                _synergyCacheHits++;
                return cached;
            }
            reused = false;
            _synergyEvaluations++;
            var value = _synergy.Evaluate(board).Value;
            _boards?.Add(key, value);
            return value;
        }

        private BoardKey KeyOf(IReadOnlyList<UnitBoardMember> board)
        {
            Span<int> key = stackalloc int[BoardSlots];
            key.Clear();
            foreach (var member in board) key[member.SlotIndex] = _definitionIndices[member.DefinitionId] + member.Star;
            return (key[0], key[1], key[2], key[3], key[4], key[5], key[6], key[7], key[8]);
        }

        private (int, int, int, int, int) HandKeyOf(IReadOnlyList<HandSynergySlot> hand)
        {
            var key = new int[HandSlots];
            int count = 0;
            foreach (var member in hand)
                if (member.Kind == HandSlotContentKind.Character)
                    key[count++] = _definitionIndices[member.DefinitionId] + member.Star;
            Array.Sort(key);
            return (key[0], key[1], key[2], key[3], key[4]);
        }

        internal SynergyValue EvaluateBoard(HandSynergyOptionState state) =>
            ReadSynergy(state.Board, _boards == null ? default : KeyOf(state.Board), out _);

        internal HandSynergyOptionState ReadCurrent() => new HandSynergyOptionState(_source.Player.Level,
            _source.Board.Slots.Where(s => s.IsOccupied)
                .Select(s => new UnitBoardMember(s.SlotIndex, s.Character.DefinitionId, s.Character.Star)),
            _source.Hand.Slots.Select(s => new HandSynergySlot(s.SlotIndex, s.Kind,
                s.Character?.DefinitionId, s.Character?.Star ?? 0, s.Character?.InstanceId)));

        public sealed class Baseline
        {
            private readonly HandSynergyOptionEvaluator _evaluator;
            private readonly HandSynergyOptionState _state;
            private readonly bool _fromObservation;
            public HandSynergyOptionEvaluation Before { get; }

            internal Baseline(HandSynergyOptionEvaluator evaluator, HandSynergyOptionState state,
                bool includeDetails, bool fromObservation)
            {
                _evaluator = evaluator;
                _state = state;
                _fromObservation = fromObservation;
                Before = evaluator.Evaluate(state, includeDetails);
            }

            public HandSynergyOptionComparison Compare(HandSynergyOptionState after) =>
                new HandSynergyOptionComparison(Before, _evaluator.Evaluate(after, Before.Details != null));

            public HandSynergyOptionComparison ComparePurchasePreview(CharacterPurchasePreviewResult preview)
            {
                if (preview == null) throw new ArgumentNullException(nameof(preview));
                if (!_fromObservation) throw new InvalidOperationException("A purchase preview requires an observation baseline.");
                if (!preview.Succeeded || !ReferenceEquals(preview.SourceObservation, _evaluator._source))
                    throw new ArgumentException("A successful preview from the baseline observation is required.", nameof(preview));
                return Compare(ProjectPurchase(_state, preview));
            }
        }

        internal static HandSynergyOptionState ProjectPurchase(HandSynergyOptionState state, CharacterPurchasePreviewResult preview)
        {
            var board = new UnitBoardMember?[BoardSlots];
            var hand = new HandSynergySlot?[HandSlots];
            foreach (var member in state.Board) board[member.SlotIndex] = member;
            foreach (var member in state.Hand) hand[member.SlotIndex] = member;
            foreach (var change in preview.SlotChanges)
            {
                int slot = change.Location.SlotIndex;
                var character = change.CharacterAfter;
                if (change.Location.Kind == CharacterLocationKind.Board)
                    board[slot] = character == null ? (UnitBoardMember?)null :
                        new UnitBoardMember(slot, character.DefinitionId, character.Star);
                else
                    hand[slot] = character == null ? (HandSynergySlot?)null :
                        new HandSynergySlot(slot, HandSlotContentKind.Character, character.DefinitionId,
                            character.Star, character.InstanceId);
            }
            return new HandSynergyOptionState(state.Level, board.Where(x => x.HasValue).Select(x => x.Value),
                hand.Where(x => x.HasValue).Select(x => x.Value));
        }
    }
}
