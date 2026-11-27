using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Strategy
{
    public readonly struct CompositionValue
    {
        public double BoardCombat { get; }
        public double AttributeSynergy { get; }
        public double UnitPartialConnectivity { get; }
        public double CompletedUnitSynergy { get; }
        public double HandReserve { get; }
        public double OneStarMergeProgress { get; }
        public double TwoStarMergeProgress { get; }
        public double HandCapacity { get; }
        public double BoardCapacity => 0;
        public double BoardSynergy => AttributeSynergy + UnitPartialConnectivity + CompletedUnitSynergy;
        public double MergeProgress => OneStarMergeProgress + TwoStarMergeProgress;
        public double Capacity => HandCapacity + BoardCapacity;
        public double Total => BoardCombat + BoardSynergy + HandReserve + MergeProgress + Capacity;

        internal CompositionValue(double combat, double attribute, double partial, double completed,
            double reserve, double oneStar, double twoStar, double handCapacity)
        {
            BoardCombat = combat;
            AttributeSynergy = attribute;
            UnitPartialConnectivity = partial;
            CompletedUnitSynergy = completed;
            HandReserve = reserve;
            OneStarMergeProgress = oneStar;
            TwoStarMergeProgress = twoStar;
            HandCapacity = handCapacity;
        }

        internal static CompositionValue Difference(CompositionValue before, CompositionValue after) =>
            new CompositionValue(after.BoardCombat - before.BoardCombat,
                after.AttributeSynergy - before.AttributeSynergy,
                after.UnitPartialConnectivity - before.UnitPartialConnectivity,
                after.CompletedUnitSynergy - before.CompletedUnitSynergy,
                after.HandReserve - before.HandReserve,
                after.OneStarMergeProgress - before.OneStarMergeProgress,
                after.TwoStarMergeProgress - before.TwoStarMergeProgress,
                after.HandCapacity - before.HandCapacity);
    }

    public readonly struct CompositionEvaluation
    {
        public CompositionValue Value { get; }
        /// <summary>Board Combat and Synergy accumulated in legacy Board Quality addition order.</summary>
        public double BoardQuality { get; }
        public int BoardCount { get; }
        public int Level { get; }
        public int EmptyBoardSlots { get; }
        public int OverLevelCount => Math.Max(0, BoardCount - Level);
        public int EmptyHandSlots { get; }

        internal CompositionEvaluation(CompositionValue value, double boardQuality, int boardCount,
            int level, int emptyBoardSlots, int emptyHandSlots)
        {
            Value = value;
            BoardQuality = boardQuality;
            BoardCount = boardCount;
            Level = level;
            EmptyBoardSlots = emptyBoardSlots;
            EmptyHandSlots = emptyHandSlots;
        }
    }

    public readonly struct CompositionComparison
    {
        public CompositionEvaluation Before { get; }
        public CompositionEvaluation After { get; }
        public CompositionValue Delta => CompositionValue.Difference(Before.Value, After.Value);
        public double TotalDelta => After.Value.Total - Before.Value.Total;

        internal CompositionComparison(CompositionEvaluation before, CompositionEvaluation after)
        {
            Before = before;
            After = after;
        }
    }

    public enum PurchaseCompositionFailure { None, PreviewUnavailable, PublicStatsUnavailable }

    public readonly struct PurchaseRollStats
    {
        public PurchaseStatRoll Roll { get; }
        public PublicCharacterStats Stats { get; }

        internal PurchaseRollStats(PurchaseStatRoll roll, PublicCharacterStats stats)
        {
            Roll = roll;
            Stats = stats;
        }
    }

    public sealed class PurchaseCompositionEvaluation
    {
        public bool Succeeded => Comparison.HasValue;
        public PurchaseCompositionFailure Failure { get; }
        public CharacterPurchasePreviewResult Preview { get; }
        public IReadOnlyList<PurchaseRollStats> StatRolls { get; }
        public CompositionEvaluation Before { get; }
        public CompositionComparison? Comparison { get; }
        public CompositionEvaluation? After => Comparison?.After;
        public CompositionValue? Delta => Comparison?.Delta;
        public double? TotalDelta => Comparison?.TotalDelta;

        internal PurchaseCompositionEvaluation(PurchaseCompositionFailure failure, CharacterPurchasePreviewResult preview,
            CompositionEvaluation before, PurchaseRollStats[] rolls, CompositionComparison? comparison = null)
        {
            Failure = failure;
            Preview = preview;
            Before = before;
            StatRolls = Array.AsReadOnly(rolls);
            Comparison = comparison;
        }
    }

    /// <summary>
    /// Evaluates visible composition and compares independent edits against one observation.
    /// A context is intended for sequential use within one decision. Comparisons model slot
    /// contents only, leaving Action legality and execution to the existing rules.
    /// </summary>
    public sealed class CompositionValueEvaluator
    {
        internal const double FullHandValue = -7;
        internal const double OneEmptyHandSlotValue = -2;
        private readonly HeuristicStrategySettings _settings;
        private readonly double _handReserveWeight;
        private readonly CharacterObservation[] _board;
        private readonly (CharacterObservation Character, bool HasItem)[] _hand;
        private readonly Dictionary<string, (int OneStar, int TwoStar)> _mergeCounts =
            new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        private readonly int _columns;
        private readonly int _level;
        private readonly SynergyObservation _synergy;
        private readonly GameObservation _observation;
        private readonly double? _handDuplicateWeight;
        private readonly Dictionary<string, HandPowerGroup> _handGroups;
        private readonly bool _includeLegacySynergy;
        private CharacterPurchasePreview _purchasePreview;

        public CompositionEvaluation Baseline { get; }
        internal (int Calculated, int Reused, int Entries) MergePlanCounts => _purchasePreview?.PlanCounts ?? default;

        public CompositionValueEvaluator(GameObservation observation, double handReserveWeight = 0.35,
            HeuristicStrategySettings settings = null, double? handDuplicateWeight = null)
            : this(observation, handReserveWeight, settings, handDuplicateWeight, true)
        {
        }

        private readonly bool _includeTemporaryBonuses;

        internal CompositionValueEvaluator(GameObservation observation, double handReserveWeight,
            HeuristicStrategySettings settings, double? handDuplicateWeight, bool includeLegacySynergy,
            bool permanentStatsOnly = false)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (double.IsNaN(handReserveWeight) || double.IsInfinity(handReserveWeight) || handReserveWeight < 0)
                throw new ArgumentOutOfRangeException(nameof(handReserveWeight));
            if (handDuplicateWeight.HasValue && !(handDuplicateWeight.Value >= 0 && handDuplicateWeight.Value <= 1))
                throw new ArgumentOutOfRangeException(nameof(handDuplicateWeight));

            _settings = settings ?? new HeuristicStrategySettings(characterStarValue: 0.75,
                includeTemporaryCharacterBonuses: true);
            _observation = observation;
            _includeTemporaryBonuses = !permanentStatsOnly && _settings.IncludeTemporaryCharacterBonuses;
            _includeLegacySynergy = includeLegacySynergy;
            _handReserveWeight = handReserveWeight;
            _board = HeuristicStrategy.BoardCharacters(observation.Board);
            _hand = new (CharacterObservation, bool)[observation.Hand.SlotCount];
            _columns = observation.Board.Columns;
            _level = observation.Player.Level;
            _synergy = observation.Synergy;
            _handDuplicateWeight = handDuplicateWeight;
            if (handDuplicateWeight.HasValue)
                _handGroups = new Dictionary<string, HandPowerGroup>(StringComparer.Ordinal);

            foreach (CharacterObservation character in _board)
                CountMergeCharacter(character);
            foreach (HandSlotObservation slot in observation.Hand.Slots)
            {
                _hand[slot.SlotIndex] = (slot.Character, slot.Kind == HandSlotContentKind.Item);
                CountMergeCharacter(slot.Character);
                if (_handGroups != null && slot.Character != null)
                {
                    string id = slot.Character.DefinitionId;
                    _handGroups.TryGetValue(id, out var group);
                    group.Add(Power(slot.Character));
                    _handGroups[id] = group;
                }
            }
            var merge = EvaluateMergeProgress(null);
            double? reserve = null;
            if (_handGroups != null)
            {
                reserve = 0;
                foreach (var group in _handGroups.Values) reserve += ReserveValue(group);
            }
            Baseline = Measure(merge.OneStar, merge.TwoStar, projectedReserve: reserve);
        }

        /// <summary>Compares a public purchase projection without executing the transaction or rolling stats.</summary>
        public PurchaseCompositionEvaluation ComparePurchase(BuyCharacterAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _purchasePreview ??= new CharacterPurchasePreview(_observation);
            return ComparePurchasePreview(_purchasePreview.Preview(action));
        }

        public PurchaseCompositionEvaluation CompareHypotheticalPurchase(RosterDefinitionObservation definition,
            int runCost, int price, int gold, CharacterLocation placement)
        {
            _purchasePreview ??= new CharacterPurchasePreview(_observation);
            return ComparePurchasePreview(_purchasePreview.PreviewHypothetical(definition, runCost, price, gold, placement));
        }

        internal PurchaseCompositionEvaluation ComparePurchasePreview(CharacterPurchasePreviewResult preview)
        {
            if (!preview.Succeeded)
                return new PurchaseCompositionEvaluation(PurchaseCompositionFailure.PreviewUnavailable, preview,
                    Baseline, Array.Empty<PurchaseRollStats>());

            var rolls = new PurchaseRollStats[Math.Max(1, preview.MergeStageCount)];
            bool available = true;
            for (int i = 0; i < rolls.Length; i++)
            {
                PurchaseStatRoll roll = preview.MergeStageCount == 0 ? preview.FinalCharacter : preview.MergeStages[i].Survivor;
                PublicCharacterStats stats = _observation.CharacterStats.Get(roll);
                rolls[i] = new PurchaseRollStats(roll, stats);
                available &= stats.IsAvailable;
            }
            if (!available)
                return new PurchaseCompositionEvaluation(PurchaseCompositionFailure.PublicStatsUnavailable, preview, Baseline, rolls);

            CompositionEvaluation after = MeasurePurchase(preview, rolls);
            return new PurchaseCompositionEvaluation(PurchaseCompositionFailure.None, preview, Baseline, rolls,
                new CompositionComparison(Baseline, after));
        }

        private CompositionEvaluation MeasurePurchase(CharacterPurchasePreviewResult preview, PurchaseRollStats[] rolls)
        {
            CompositionValue before = Baseline.Value;
            double combatDelta = 0, reservePowerDelta = 0;
            bool boardGeometryChanged = false;
            bool handChanged = false;
            foreach (PurchaseSlotChange change in preview.SlotChanges)
            {
                int slot = change.Location.SlotIndex;
                bool onBoard = change.Location.Kind == CharacterLocationKind.Board;
                if (!onBoard && _handGroups != null)
                {
                    handChanged = true;
                    continue;
                }
                CharacterObservation old = onBoard ? _board[slot] : _hand[slot].Character;
                double newPower = change.CharacterAfter == null ? 0 : ExpectedPower(change.CharacterAfter, rolls);
                double difference = newPower - Power(old);
                if (onBoard)
                {
                    combatDelta += difference;
                    boardGeometryChanged |= old?.DefinitionId != change.CharacterAfter?.DefinitionId;
                }
                else reservePowerDelta += difference;
            }

            double combat = before.BoardCombat + combatDelta;
            double attribute = before.AttributeSynergy, partial = before.UnitPartialConnectivity;
            double completed = before.CompletedUnitSynergy;
            double quality = Baseline.BoardQuality + combatDelta;
            if (_includeLegacySynergy && boardGeometryChanged)
                quality = MeasureProjectedSynergy(preview, combat, out attribute, out partial, out completed);

            HeuristicStrategy.EvaluateMergeProgress(preview.StarsBefore.OneStar, preview.StarsBefore.TwoStar, _settings,
                out double oldOne, out double oldTwo);
            HeuristicStrategy.EvaluateMergeProgress(preview.StarsAfter.OneStar, preview.StarsAfter.TwoStar, _settings,
                out double newOne, out double newTwo);
            int emptyHand = preview.After.EmptyHandSlots;
            double reserve = _handGroups == null ? before.HandReserve + reservePowerDelta * _handReserveWeight :
                before.HandReserve + (handChanged ? HandGroupDelta(preview.DefinitionId, preview, rolls) : 0);
            var value = new CompositionValue(combat, attribute, partial, completed,
                reserve,
                before.OneStarMergeProgress + (newOne - oldOne), before.TwoStarMergeProgress + (newTwo - oldTwo),
                HandCapacityScore(emptyHand));
            return new CompositionEvaluation(value, quality, preview.After.BoardCount, _level,
                preview.After.EmptyBoardSlots, emptyHand);
        }

        private double ExpectedPower(PurchaseStatRoll roll, PurchaseRollStats[] rolls)
        {
            foreach (PurchaseRollStats entry in rolls)
                if (ReferenceEquals(entry.Roll, roll))
                    return CharacterPowerEvaluator.Evaluate(entry.Stats.ExpectedAttack.Value, entry.Stats.ExpectedDefense.Value,
                        roll.TemporaryAttackBonus, roll.TemporaryDefenseBonus, roll.Star, _settings.AttackValue,
                        _settings.DefenseValue, _settings.CharacterStarValue, _includeTemporaryBonuses);
            throw new InvalidOperationException("The projected slot refers to a stat roll missing from the purchase plan.");
        }

        private double MeasureProjectedSynergy(CharacterPurchasePreviewResult preview, double combat,
            out double attribute, out double partial, out double completed)
        {
            if (preview == null)
            {
                double current = HeuristicStrategy.AddBoardTagSynergy(_board, _columns, _settings, combat,
                    static character => character?.Attributes, static character => character?.Units, out attribute, out partial);
                return AddCompletedUnits(_board, static character => character?.DefinitionId, current, out completed);
            }
            var board = new (string DefinitionId, IReadOnlyList<string> Attributes, IReadOnlyList<string> Units)[_board.Length];
            for (int i = 0; i < _board.Length; i++)
            {
                CharacterObservation character = _board[i];
                if (character != null) board[i] = (character.DefinitionId, character.Attributes, character.Units);
            }
            foreach (PurchaseSlotChange change in preview.SlotChanges)
            {
                if (change.Location.Kind != CharacterLocationKind.Board) continue;
                var character = change.CharacterAfter;
                board[change.Location.SlotIndex] = character == null ? default :
                    (character.DefinitionId, character.Attributes, character.Units);
            }
            double quality = HeuristicStrategy.AddBoardTagSynergy(board, _columns, _settings, combat,
                static slot => slot.Attributes, static slot => slot.Units, out attribute, out partial);
            return AddCompletedUnits(board, static slot => slot.DefinitionId, quality, out completed);
        }

        private double AddCompletedUnits<T>(T[] board, Func<T, string> definitionAt, double quality, out double completed)
        {
            completed = 0;
            if (_observation.UnitRequirements.Count == 0) return quality;
            var memberSlots = new List<int>();
            foreach (UnitRequirementObservation unit in _observation.UnitRequirements)
            {
                memberSlots.Clear();
                foreach (string definitionId in unit.RequiredDefinitionIds)
                {
                    int slot = 0;
                    while (slot < board.Length && definitionAt(board[slot]) != definitionId) slot++;
                    if (slot == board.Length) break;
                    memberSlots.Add(slot);
                }
                if (memberSlots.Count != unit.RequiredMemberCount || !HeuristicStrategy.Connected(memberSlots, _columns))
                    continue;
                completed += _settings.UnitEstablishedValue;
                quality += _settings.UnitEstablishedValue;
            }
            return quality;
        }

        public CompositionComparison CompareBoardMoveOrSwap(int source, int destination)
        {
            CheckSlot(source, _board.Length, nameof(source));
            CheckSlot(destination, _board.Length, nameof(destination));
            CharacterObservation from = _board[source];
            CharacterObservation to = _board[destination];
            RequireCharacter(from);
            _board[source] = to;
            _board[destination] = from;
            try { return CompareCurrent(); }
            finally
            {
                _board[source] = from;
                _board[destination] = to;
            }
        }

        public CompositionComparison CompareHandMoveOrSwap(int source, int destination)
        {
            CheckSlot(source, _hand.Length, nameof(source));
            CheckSlot(destination, _hand.Length, nameof(destination));
            if (_hand[source].Character == null && !_hand[source].HasItem)
                throw new InvalidOperationException("The source Hand slot is empty.");
            // A Hand permutation preserves every input to the scoring terms.
            return new CompositionComparison(Baseline, Baseline);
        }

        public CompositionComparison CompareBoardToHand(int boardSlot, int handSlot) =>
            CompareTransfer(boardSlot, handSlot, true);

        public CompositionComparison CompareHandToBoard(int handSlot, int boardSlot) =>
            CompareTransfer(boardSlot, handSlot, false);

        public CompositionComparison CompareBoardRemoval(int slot)
        {
            CheckSlot(slot, _board.Length, nameof(slot));
            CharacterObservation removed = _board[slot];
            RequireCharacter(removed);
            _board[slot] = null;
            try { return CompareCurrent(removed); }
            finally { _board[slot] = removed; }
        }

        public CompositionComparison CompareHandRemoval(int slot)
        {
            CheckSlot(slot, _hand.Length, nameof(slot));
            var removed = _hand[slot];
            if (removed.Character == null && !removed.HasItem)
                throw new InvalidOperationException("The Hand slot is empty.");
            _hand[slot] = default;
            try { return CompareCurrent(removed.Character, boardChanged: false, handFrom: removed.Character?.DefinitionId); }
            finally { _hand[slot] = removed; }
        }

        private CompositionComparison CompareTransfer(int boardSlot, int handSlot, bool boardToHand)
        {
            CheckSlot(boardSlot, _board.Length, nameof(boardSlot));
            CheckSlot(handSlot, _hand.Length, nameof(handSlot));
            CharacterObservation board = _board[boardSlot];
            var hand = _hand[handSlot];
            if (hand.HasItem)
                throw new InvalidOperationException("An Item cannot be exchanged with the Board.");
            RequireCharacter(boardToHand ? board : hand.Character);
            _board[boardSlot] = hand.Character;
            _hand[handSlot] = (board, false);
            try
            {
                return CompareCurrent(synergyChanged: _handGroups == null || board?.DefinitionId != hand.Character?.DefinitionId,
                    handFrom: hand.Character?.DefinitionId, handTo: board?.DefinitionId);
            }
            finally
            {
                _board[boardSlot] = board;
                _hand[handSlot] = hand;
            }
        }

        private CompositionComparison CompareCurrent(CharacterObservation removed = null, bool boardChanged = true,
            string handFrom = null, string handTo = null, bool synergyChanged = true)
        {
            var merge = removed == null || removed.Star > 2
                ? (Baseline.Value.OneStarMergeProgress, Baseline.Value.TwoStarMergeProgress)
                : _handGroups == null ? EvaluateMergeProgress(removed) : MergeProgressAfterRemoval(removed);
            double? reserve = _handGroups == null ? (double?)null : Baseline.Value.HandReserve +
                HandGroupDelta(handFrom) + (handTo != handFrom ? HandGroupDelta(handTo) : 0);
            return new CompositionComparison(Baseline, Measure(merge.Item1, merge.Item2, boardChanged, reserve, synergyChanged));
        }

        private CompositionEvaluation Measure(double oneStar, double twoStar, bool boardChanged = true,
            double? projectedReserve = null, bool synergyChanged = true)
        {
            double combat = Baseline.Value.BoardCombat;
            double attribute = Baseline.Value.AttributeSynergy;
            double partial = Baseline.Value.UnitPartialConnectivity;
            double completed = Baseline.Value.CompletedUnitSynergy;
            double boardQuality = Baseline.BoardQuality;
            int boardCount = Baseline.BoardCount;
            if (boardChanged)
            {
                combat = 0;
                boardCount = 0;
                foreach (CharacterObservation character in _board)
                {
                    if (character == null) continue;
                    combat += Power(character);
                    boardCount++;
                }
                if (_includeLegacySynergy && synergyChanged)
                    boardQuality = _handGroups == null
                        ? HeuristicStrategy.AddBoardSynergy(_board, _columns, _synergy,
                            _settings, combat, out attribute, out partial, out completed)
                        : MeasureProjectedSynergy(null, combat, out attribute, out partial, out completed);
                else boardQuality += combat - Baseline.Value.BoardCombat;
            }

            double reserve = 0;
            int emptyHand = 0;
            foreach (var slot in _hand)
            {
                if (slot.Character != null)
                {
                    if (!projectedReserve.HasValue) reserve += Power(slot.Character);
                }
                else if (!slot.HasItem) emptyHand++;
            }
            double handCapacity = HandCapacityScore(emptyHand);
            var value = new CompositionValue(combat, attribute, partial, completed,
                projectedReserve ?? reserve * _handReserveWeight, oneStar, twoStar, handCapacity);
            return new CompositionEvaluation(value, boardQuality, boardCount, _level,
                _board.Length - boardCount, emptyHand);
        }

        internal static double HandCapacityScore(int emptyHand) =>
            emptyHand == 0 ? FullHandValue : emptyHand == 1 ? OneEmptyHandSlotValue : 0;

        private double HandGroupDelta(string definitionId, CharacterPurchasePreviewResult preview = null,
            PurchaseRollStats[] rolls = null)
        {
            if (definitionId == null) return 0;
            _handGroups.TryGetValue(definitionId, out var before);
            var after = new HandPowerGroup();
            for (int i = 0; i < _hand.Length; i++)
            {
                if (preview != null)
                {
                    bool changed = false;
                    foreach (var edit in preview.SlotChanges)
                    {
                        if (edit.Location.Kind != CharacterLocationKind.Hand || edit.Location.SlotIndex != i) continue;
                        if (edit.CharacterAfter?.DefinitionId == definitionId) after.Add(ExpectedPower(edit.CharacterAfter, rolls));
                        changed = true;
                        break;
                    }
                    if (changed) continue;
                }
                CharacterObservation character = _hand[i].Character;
                if (character?.DefinitionId == definitionId) after.Add(Power(character));
            }
            return ReserveValue(after) - ReserveValue(before);
        }

        private double ReserveValue(HandPowerGroup group) => group.Count == 0 ? 0 :
            _handReserveWeight * (group.Max + _handDuplicateWeight.Value * (group.Total - group.Max));

        private struct HandPowerGroup
        {
            public int Count;
            public double Max;
            public double Total;

            public void Add(double power)
            {
                Max = Count == 0 ? power : Math.Max(Max, power);
                Total += power;
                Count++;
            }
        }

        private double Power(CharacterObservation character) =>
            CharacterPowerEvaluator.Evaluate(character, _settings.AttackValue, _settings.DefenseValue,
                _settings.CharacterStarValue, _includeTemporaryBonuses);

        internal CompositionValue ProjectPermanentCharacterChange(Items.ItemCharacterChange change)
        {
            var old = change.Before;
            var baseline = Baseline.Value;
            double power = CharacterPowerEvaluator.Evaluate(change.Attack.Value, change.Defense.Value,
                0, 0, change.Star, _settings.AttackValue, _settings.DefenseValue, _settings.CharacterStarValue, false);
            double combat = baseline.BoardCombat;
            double reserve = baseline.HandReserve;
            if (old.LocationKind == ObservationLocationKind.Board)
                combat += power - Power(old);
            else if (_handGroups == null)
                reserve += _handReserveWeight * (power - Power(old));
            else
            {
                var projected = new HandPowerGroup();
                foreach (var slot in _hand)
                {
                    var character = slot.Character;
                    if (character?.DefinitionId == old.DefinitionId)
                        projected.Add(ReferenceEquals(character, old) ? power : Power(character));
                }
                reserve += ReserveValue(projected) - ReserveValue(_handGroups[old.DefinitionId]);
            }

            _mergeCounts.TryGetValue(old.DefinitionId, out var counts);
            HeuristicStrategy.EvaluateMergeProgress(counts.OneStar, counts.TwoStar, _settings,
                out double oldOne, out double oldTwo);
            HeuristicStrategy.EvaluateMergeProgress(
                counts.OneStar - (old.Star == 1 ? 1 : 0) + (change.Star == 1 ? 1 : 0),
                counts.TwoStar - (old.Star == 2 ? 1 : 0) + (change.Star == 2 ? 1 : 0),
                _settings, out double newOne, out double newTwo);
            return new CompositionValue(combat, 0, 0, 0, reserve,
                baseline.OneStarMergeProgress + (newOne - oldOne),
                baseline.TwoStarMergeProgress + (newTwo - oldTwo), baseline.Capacity);
        }

        private void CountMergeCharacter(CharacterObservation character)
        {
            if (character == null || (character.Star != 1 && character.Star != 2)) return;
            _mergeCounts.TryGetValue(character.DefinitionId, out var counts);
            if (character.Star == 1) counts.OneStar++;
            else counts.TwoStar++;
            _mergeCounts[character.DefinitionId] = counts;
        }

        private (double OneStar, double TwoStar) MergeProgressAfterRemoval(CharacterObservation removed)
        {
            var counts = _mergeCounts[removed.DefinitionId];
            HeuristicStrategy.EvaluateMergeProgress(counts.OneStar, counts.TwoStar, _settings, out double oldOne, out double oldTwo);
            HeuristicStrategy.EvaluateMergeProgress(counts.OneStar - (removed.Star == 1 ? 1 : 0),
                counts.TwoStar - (removed.Star == 2 ? 1 : 0), _settings, out double newOne, out double newTwo);
            return (Baseline.Value.OneStarMergeProgress + (newOne - oldOne),
                Baseline.Value.TwoStarMergeProgress + (newTwo - oldTwo));
        }

        private (double OneStar, double TwoStar) EvaluateMergeProgress(CharacterObservation removed)
        {
            double oneStar = 0;
            double twoStar = 0;
            foreach (var pair in _mergeCounts)
            {
                var counts = pair.Value;
                if (removed != null && string.Equals(pair.Key, removed.DefinitionId, StringComparison.Ordinal))
                {
                    if (removed.Star == 1) counts.OneStar--;
                    else if (removed.Star == 2) counts.TwoStar--;
                }
                HeuristicStrategy.EvaluateMergeProgress(counts.OneStar, counts.TwoStar, _settings,
                    out double ones, out double twos);
                oneStar += ones;
                twoStar += twos;
            }
            return (oneStar, twoStar);
        }

        private static void CheckSlot(int slot, int count, string name)
        {
            if (slot < 0 || slot >= count) throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireCharacter(CharacterObservation character)
        {
            if (character == null) throw new InvalidOperationException("The source slot must contain a Character.");
        }
    }
}
