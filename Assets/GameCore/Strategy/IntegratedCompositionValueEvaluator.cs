using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Strategy
{
    public sealed class IntegratedCompositionSettings
    {
        public SynergyValueSettings Synergy { get; }
        public HandSynergyOptionSettings HandOption { get; }
        public HeuristicStrategySettings FourComponentSettings { get; } = HeuristicStrategy.CreateV13Settings();
        public double OneEmptyHandSlotValue => CompositionValueEvaluator.OneEmptyHandSlotValue;
        public double FullHandValue => CompositionValueEvaluator.FullHandValue;

        public IntegratedCompositionSettings(SynergyValueSettings synergy = null, HandSynergyOptionSettings handOption = null)
        {
            Synergy = synergy ?? new SynergyValueSettings();
            HandOption = handOption ?? new HandSynergyOptionSettings();
        }
    }

    public readonly struct IntegratedCompositionValue
    {
        public decimal BoardCombat { get; }
        public SynergyValue Synergy { get; }
        public decimal BoardSynergy => Synergy.TotalSynergyValue;
        public decimal HandReserve { get; }
        public decimal MergeProgress { get; }
        public decimal Capacity { get; }
        public decimal HandUnitOption { get; }
        public decimal TotalCompositionValue => BoardCombat + BoardSynergy + HandReserve + MergeProgress + Capacity + HandUnitOption;

        internal IntegratedCompositionValue(decimal combat, SynergyValue synergy, decimal reserve,
            decimal merge, decimal capacity, decimal handOption)
        {
            BoardCombat = combat;
            Synergy = synergy;
            HandReserve = reserve;
            MergeProgress = merge;
            Capacity = capacity;
            HandUnitOption = handOption;
        }

        internal static IntegratedCompositionValue Difference(IntegratedCompositionValue before, IntegratedCompositionValue after) =>
            new IntegratedCompositionValue(after.BoardCombat - before.BoardCombat,
                new SynergyValue(after.Synergy.AttributeValue - before.Synergy.AttributeValue,
                    after.Synergy.UnitProgressValue - before.Synergy.UnitProgressValue,
                    after.Synergy.UnitActivationValue - before.Synergy.UnitActivationValue),
                after.HandReserve - before.HandReserve, after.MergeProgress - before.MergeProgress,
                after.Capacity - before.Capacity, after.HandUnitOption - before.HandUnitOption);
    }

    public readonly struct IntegratedCompositionEvaluation
    {
        public IntegratedCompositionValue Value { get; }
        public HandSynergyOptionEvaluation HandOption { get; }
        public int BoardCount { get; }
        public int Level { get; }
        public int EmptyBoardSlots { get; }
        public int EmptyHandSlots { get; }
        public int OverLevelCount => Math.Max(0, BoardCount - Level);
        internal HandSynergyOptionState DiagnosticState { get; }

        internal IntegratedCompositionEvaluation(IntegratedCompositionValue value, HandSynergyOptionEvaluation handOption,
            int boardCount, int level, int emptyBoardSlots, int emptyHandSlots, HandSynergyOptionState diagnosticState)
        {
            Value = value;
            HandOption = handOption;
            BoardCount = boardCount;
            Level = level;
            EmptyBoardSlots = emptyBoardSlots;
            EmptyHandSlots = emptyHandSlots;
            DiagnosticState = diagnosticState;
        }
    }

    public readonly struct IntegratedCompositionComparison
    {
        public IntegratedCompositionEvaluation Before { get; }
        public IntegratedCompositionEvaluation After { get; }
        public IntegratedCompositionValue Delta => IntegratedCompositionValue.Difference(Before.Value, After.Value);
        public decimal TotalDelta => After.Value.TotalCompositionValue - Before.Value.TotalCompositionValue;

        internal IntegratedCompositionComparison(IntegratedCompositionEvaluation before, IntegratedCompositionEvaluation after)
        {
            Before = before;
            After = after;
        }
    }

    public sealed class IntegratedPurchaseEvaluation
    {
        public bool Succeeded => Comparison.HasValue;
        public PurchaseCompositionFailure Failure { get; }
        public CharacterPurchasePreviewResult Preview { get; }
        public IReadOnlyList<PurchaseRollStats> StatRolls { get; }
        public IntegratedCompositionEvaluation Before { get; }
        public IntegratedCompositionComparison? Comparison { get; }
        public IntegratedCompositionEvaluation? After => Comparison?.After;
        public IntegratedCompositionValue? Delta => Comparison?.Delta;
        public decimal? TotalDelta => Comparison?.TotalDelta;

        internal IntegratedPurchaseEvaluation(PurchaseCompositionEvaluation purchase,
            IntegratedCompositionEvaluation before, IntegratedCompositionComparison? comparison)
        {
            Failure = purchase.Failure;
            Preview = purchase.Preview;
            StatRolls = purchase.StatRolls;
            Before = before;
            Comparison = comparison;
        }
    }

    /// <summary>Six-component composition for one observation. Compare independent edits against its baseline.</summary>
    public sealed class IntegratedCompositionValueEvaluator
    {
        private readonly GameObservation _observation;
        private readonly CompositionValueEvaluator _fourComponents;
        private readonly HandSynergyOptionEvaluator _structure;
        private readonly HandSynergyOptionState _current;
        private readonly bool _includeDetails;
        private readonly bool _retainDiagnosticStates;
        private const int BoardSlots = UnitMoveRoundPlanner.SlotCount;
        private const int HandSlots = HandState.StandardSlotCount;

        public IntegratedCompositionSettings Settings { get; }
        public IntegratedCompositionEvaluation Baseline { get; }
        public (int SynergyEvaluations, int SynergyCacheHits, int OptionCacheHits) CacheStatistics => _structure.CacheStatistics;

        public IntegratedCompositionValueEvaluator(GameObservation observation, IntegratedCompositionSettings settings = null,
            bool includeDetails = false, bool useCache = true)
            : this(observation, settings, includeDetails, useCache, false)
        {
        }

        internal IntegratedCompositionValueEvaluator(GameObservation observation, IntegratedCompositionSettings settings,
            bool includeDetails, bool useCache, bool retainDiagnosticStates, bool permanentStatsOnly = false)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            Settings = settings ?? new IntegratedCompositionSettings();
            _includeDetails = includeDetails;
            _retainDiagnosticStates = retainDiagnosticStates;
            var core = Settings.FourComponentSettings;
            _fourComponents = new CompositionValueEvaluator(observation, core.CompositionHandReserveWeight,
                core, core.CompositionHandDuplicateWeight, includeLegacySynergy: false, permanentStatsOnly: permanentStatsOnly);
            _structure = new HandSynergyOptionEvaluator(observation, Settings.Synergy, Settings.HandOption, useCache);
            _current = _structure.ReadCurrent();
            Baseline = Evaluate(_fourComponents.Baseline.Value, _current);
        }

        public IntegratedPurchaseEvaluation ComparePurchase(BuyCharacterAction action) =>
            ComparePurchase(_fourComponents.ComparePurchase(action));

        public IntegratedPurchaseEvaluation CompareHypotheticalPurchase(RosterDefinitionObservation definition,
            int runCost, int price, int gold, CharacterLocation placement) =>
            ComparePurchase(_fourComponents.CompareHypotheticalPurchase(definition, runCost, price, gold, placement));

        public IntegratedPurchaseEvaluation ComparePurchasePreview(CharacterPurchasePreviewResult preview)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            if (!ReferenceEquals(preview.SourceObservation, _observation))
                throw new ArgumentException("The preview must belong to this evaluator's observation.", nameof(preview));
            return ComparePurchase(_fourComponents.ComparePurchasePreview(preview));
        }

        private IntegratedPurchaseEvaluation ComparePurchase(PurchaseCompositionEvaluation purchase)
        {
            if (!purchase.Succeeded) return new IntegratedPurchaseEvaluation(purchase, Baseline, null);
            var state = HandSynergyOptionEvaluator.ProjectPurchase(_current, purchase.Preview);
            return new IntegratedPurchaseEvaluation(purchase, Baseline, Compare(purchase.After.Value.Value, state));
        }

        public IntegratedCompositionComparison CompareBoardRemoval(int slot) =>
            CompareEdit(_fourComponents.CompareBoardRemoval(slot), (board, hand) => board[slot] = null);

        public IntegratedCompositionComparison CompareHandRemoval(int slot) =>
            CompareEdit(_fourComponents.CompareHandRemoval(slot), (board, hand) => hand[slot] = null);

        public IntegratedCompositionComparison CompareBoardMoveOrSwap(int source, int destination) =>
            CompareEdit(_fourComponents.CompareBoardMoveOrSwap(source, destination), (board, hand) =>
            {
                var from = board[source];
                board[source] = AtBoard(board[destination], source);
                board[destination] = AtBoard(from, destination);
            });

        public IntegratedCompositionComparison CompareHandMoveOrSwap(int source, int destination) =>
            CompareEdit(_fourComponents.CompareHandMoveOrSwap(source, destination), (board, hand) =>
            {
                var from = hand[source];
                hand[source] = AtHand(hand[destination], source);
                hand[destination] = AtHand(from, destination);
            });

        public IntegratedCompositionComparison CompareBoardToHand(int boardSlot, int handSlot) =>
            CompareTransfer(_fourComponents.CompareBoardToHand(boardSlot, handSlot), boardSlot, handSlot);

        public IntegratedCompositionComparison CompareHandToBoard(int handSlot, int boardSlot) =>
            CompareTransfer(_fourComponents.CompareHandToBoard(handSlot, boardSlot), boardSlot, handSlot);

        private IntegratedCompositionComparison CompareTransfer(CompositionComparison core, int boardSlot, int handSlot) =>
            CompareEdit(core, (board, hand) =>
            {
                var fromBoard = board[boardSlot];
                var fromHand = hand[handSlot];
                board[boardSlot] = fromHand?.Kind == HandSlotContentKind.Character
                    ? new UnitBoardMember(boardSlot, fromHand.Value.DefinitionId, fromHand.Value.Star)
                    : (UnitBoardMember?)null;
                hand[handSlot] = fromBoard.HasValue
                    ? new HandSynergySlot(handSlot, HandSlotContentKind.Character, fromBoard.Value.DefinitionId,
                        fromBoard.Value.Star, _observation.Board.Slots[boardSlot].Character.InstanceId)
                    : (HandSynergySlot?)null;
            });

        public IntegratedCompositionComparison CompareLevel(int level) =>
            Compare(_fourComponents.Baseline.Value, new HandSynergyOptionState(level, _current.Board, _current.Hand));

        internal IntegratedCompositionComparison ComparePermanentItemEffect(Items.ItemCharacterChange change)
        {
            var core = _fourComponents.ProjectPermanentCharacterChange(change);
            var old = change.Before;
            if (old.Star == change.Star) return Compare(core, _current);
            var board = _current.Board;
            var hand = _current.Hand;
            if (old.LocationKind == ObservationLocationKind.Board)
                return Compare(core, new HandSynergyOptionState(_current.Level,
                    board.Select(member => member.SlotIndex == old.SlotIndex
                        ? new UnitBoardMember(old.SlotIndex, old.DefinitionId, change.Star) : member), hand));
            return Compare(core, new HandSynergyOptionState(_current.Level, board,
                hand.Select(member => member.SlotIndex == old.SlotIndex
                    ? new HandSynergySlot(old.SlotIndex, member.Kind, old.DefinitionId, change.Star, old.InstanceId)
                    : member)));
        }

        public IntegratedCompositionComparison CompareHandItemAddition(int emptySlot)
        {
            var item = new HandSynergySlot(emptySlot, HandSlotContentKind.Item);
            if (_current.Hand.Any(x => x.SlotIndex == emptySlot && x.Kind != HandSlotContentKind.Empty))
                throw new InvalidOperationException("The destination Hand slot is occupied.");
            var state = new HandSynergyOptionState(_current.Level, _current.Board,
                _current.Hand.Where(x => x.SlotIndex != emptySlot).Append(item));
            return Compare(_fourComponents.Baseline.Value, state);
        }

        private IntegratedCompositionComparison CompareEdit(CompositionComparison core,
            Action<UnitBoardMember?[], HandSynergySlot?[]> edit)
        {
            var board = new UnitBoardMember?[BoardSlots];
            var hand = new HandSynergySlot?[HandSlots];
            foreach (var member in _current.Board) board[member.SlotIndex] = member;
            foreach (var member in _current.Hand) hand[member.SlotIndex] = member;
            edit(board, hand);
            return Compare(core.After.Value, new HandSynergyOptionState(_current.Level,
                board.Where(x => x.HasValue).Select(x => x.Value), hand.Where(x => x.HasValue).Select(x => x.Value)));
        }

        private IntegratedCompositionComparison Compare(CompositionValue core, HandSynergyOptionState state) =>
            new IntegratedCompositionComparison(Baseline, Evaluate(core, state));

        private IntegratedCompositionEvaluation Evaluate(CompositionValue core, HandSynergyOptionState state)
        {
            var synergy = _structure.EvaluateBoard(state);
            var option = _structure.Evaluate(state, _includeDetails, synergy);
            int emptyHand = HandSlots - state.Hand.Count(x => x.Kind != HandSlotContentKind.Empty);
            var value = new IntegratedCompositionValue((decimal)core.BoardCombat, synergy, (decimal)core.HandReserve,
                (decimal)core.MergeProgress, (decimal)CompositionValueEvaluator.HandCapacityScore(emptyHand), option.HandUnitOption);
            return new IntegratedCompositionEvaluation(value, option, state.Board.Count, state.Level,
                BoardSlots - state.Board.Count, emptyHand, _retainDiagnosticStates ? state : null);
        }

        internal (SynergyEvaluation Synergy, HandSynergyOptionEvaluation Option, CompositionValue? ImmediateCore)
            Describe(IntegratedCompositionEvaluation evaluation, bool current)
        {
            var state = evaluation.DiagnosticState ?? throw new InvalidOperationException("Diagnostic state was not retained.");
            var option = _structure.Evaluate(state, true, evaluation.Value.Synergy);
            CompositionValue? immediate = null;
            if (current && option.Details.BestHand.HasValue && option.Details.BestBoardSlot.HasValue)
                immediate = _fourComponents.CompareHandToBoard(option.Details.BestHand.Value.SlotIndex,
                    option.Details.BestBoardSlot.Value).Delta;
            return (_structure.DescribeBoard(state), option, immediate);
        }

        internal (int BoardEntries, int OptionEntries, int OptionRequests, int OptionSearches,
            int LegalOptions, int SafeSkips, int VirtualSynergyNew) DiagnosticCounts => _structure.DiagnosticCounts;

        internal (int Calculated, int Reused, int Entries) MergePlanCounts => _fourComponents.MergePlanCounts;

        private static UnitBoardMember? AtBoard(UnitBoardMember? member, int slot) => member.HasValue
            ? new UnitBoardMember(slot, member.Value.DefinitionId, member.Value.Star) : (UnitBoardMember?)null;

        private static HandSynergySlot? AtHand(HandSynergySlot? member, int slot) => member.HasValue
            ? new HandSynergySlot(slot, member.Value.Kind, member.Value.DefinitionId, member.Value.Star, member.Value.InstanceId)
            : (HandSynergySlot?)null;
    }
}
