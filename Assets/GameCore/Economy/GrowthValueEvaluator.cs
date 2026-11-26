using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Economy
{
    public sealed class GrowthValueSettings
    {
        public decimal CapacityPerSlot { get; }
        public decimal ProgressFraction { get; }
        public decimal OpportunityPerSlot { get; }

        public GrowthValueSettings(decimal capacityPerSlot, decimal progressFraction, decimal opportunityPerSlot)
        {
            if (capacityPerSlot < 0) throw new ArgumentOutOfRangeException(nameof(capacityPerSlot));
            if (progressFraction < 0 || progressFraction > 1) throw new ArgumentOutOfRangeException(nameof(progressFraction));
            if (opportunityPerSlot < 0) throw new ArgumentOutOfRangeException(nameof(opportunityPerSlot));
            CapacityPerSlot = capacityPerSlot;
            ProgressFraction = progressFraction;
            OpportunityPerSlot = opportunityPerSlot;
        }
    }

    /// <summary>Immutable summary of the Board and Hand for one observed or hypothetical formation.</summary>
    public sealed class GrowthPlacement
    {
        public int PhysicalSlots { get; }
        public int BoardCount { get; }
        public int EligibleHandDefinitions { get; }

        public GrowthPlacement(int physicalSlots, IEnumerable<string> boardDefinitions, IEnumerable<string> handDefinitions)
        {
            if (physicalSlots < 0) throw new ArgumentOutOfRangeException(nameof(physicalSlots));
            if (boardDefinitions == null) throw new ArgumentNullException(nameof(boardDefinitions));
            if (handDefinitions == null) throw new ArgumentNullException(nameof(handDefinitions));

            var board = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in boardDefinitions)
            {
                if (string.IsNullOrEmpty(id) || !board.Add(id))
                    throw new ArgumentException("The Board requires distinct Character definitions.", nameof(boardDefinitions));
            }
            if (board.Count > physicalSlots) throw new ArgumentException("Too many Board occupants.", nameof(boardDefinitions));

            var hand = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in handDefinitions)
            {
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("Expected a Character definition.", nameof(handDefinitions));
                if (!board.Contains(id)) hand.Add(id);
            }

            PhysicalSlots = physicalSlots;
            BoardCount = board.Count;
            EligibleHandDefinitions = BoardCount < PhysicalSlots ? hand.Count : 0;
        }

        public static GrowthPlacement FromObservation(GameObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            return new GrowthPlacement(observation.Board.Slots.Count,
                observation.Board.Slots.Where(s => s.Character != null).Select(s => s.Character.DefinitionId),
                observation.Hand.Slots.Where(s => s.Kind == HandSlotContentKind.Character)
                    .Select(s => s.Character.DefinitionId));
        }
    }

    public readonly struct GrowthValue
    {
        public int Level { get; }
        public int XP { get; }
        public int EffectiveCapacity { get; }
        public int UsableOpportunityCount { get; }
        public decimal CapacityValue { get; }
        public decimal ProgressValue { get; }
        public decimal OpportunityValue { get; }
        public decimal TotalValue { get; }

        internal GrowthValue(int level, int xp, int capacity, int opportunities, decimal capacityValue,
            decimal progressValue, decimal opportunityValue)
        {
            Level = level;
            XP = xp;
            EffectiveCapacity = capacity;
            UsableOpportunityCount = opportunities;
            CapacityValue = capacityValue;
            ProgressValue = progressValue;
            OpportunityValue = opportunityValue;
            TotalValue = capacityValue + progressValue + opportunityValue;
        }
    }

    public readonly struct GrowthValueComparison
    {
        public GrowthValue Before { get; }
        public GrowthValue After { get; }
        public decimal DeltaCapacity => After.CapacityValue - Before.CapacityValue;
        public decimal DeltaProgress => After.ProgressValue - Before.ProgressValue;
        public decimal DeltaOpportunity => After.OpportunityValue - Before.OpportunityValue;
        public decimal TotalDelta => After.TotalValue - Before.TotalValue;

        internal GrowthValueComparison(GrowthValue before, GrowthValue after)
        {
            Before = before;
            After = after;
        }
    }

    public sealed class GrowthValueEvaluator
    {
        public GrowthValueSettings Settings { get; }
        public ExperienceCurve Curve { get; }

        public GrowthValueEvaluator(GrowthValueSettings settings, ExperienceCurve curve)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));
        }

        public static Baseline CreateBaseline(GrowthValueSettings settings, GameObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            var evaluator = new GrowthValueEvaluator(settings, observation.EconomyRules.ExperienceCurve);
            if (evaluator.Curve.MaximumLevel != observation.Player.MaximumLevel ||
                evaluator.Curve.RequiredXP(observation.Player.Level) != observation.Player.RequiredXP)
                throw new ArgumentException("Public XP thresholds do not match the player state.", nameof(observation));
            return evaluator.CreateBaseline(observation.Player.Level, observation.Player.XP,
                GrowthPlacement.FromObservation(observation));
        }

        public GrowthValue Evaluate(int level, int xp, GrowthPlacement placement)
        {
            if (placement == null) throw new ArgumentNullException(nameof(placement));
            Curve.ValidateProgress(level, xp);
            int capacity = Math.Min(level, placement.PhysicalSlots);
            int opportunities = Math.Min(placement.EligibleHandDefinitions, Math.Max(0, capacity - placement.BoardCount));
            decimal progress = level < Curve.MaximumLevel && level < placement.PhysicalSlots
                ? Settings.ProgressFraction * Settings.CapacityPerSlot * ((decimal)xp / Curve.RequiredXP(level))
                : 0m;
            return new GrowthValue(level, xp, capacity, opportunities, capacity * Settings.CapacityPerSlot,
                progress, opportunities * Settings.OpportunityPerSlot);
        }

        public Baseline CreateBaseline(int level, int xp, GrowthPlacement placement) =>
            new Baseline(this, placement, Evaluate(level, xp, placement));

        public sealed class Baseline
        {
            private readonly GrowthValueEvaluator _evaluator;
            private readonly GrowthPlacement _placement;
            public GrowthValue Before { get; }

            internal Baseline(GrowthValueEvaluator evaluator, GrowthPlacement placement, GrowthValue before)
            {
                _evaluator = evaluator;
                _placement = placement;
                Before = before;
            }

            public GrowthValueComparison CompareExperienceGain(int amount)
            {
                var result = _evaluator.Curve.Advance(Before.Level, Before.XP, amount);
                return new GrowthValueComparison(Before, _evaluator.Evaluate(result.AfterLevel, result.AfterXP, _placement));
            }

            public GrowthValueComparison ComparePlacement(GrowthPlacement after)
            {
                if (after == null) throw new ArgumentNullException(nameof(after));
                if (after.PhysicalSlots != _placement.PhysicalSlots)
                    throw new ArgumentException("The physical Board must remain the same.", nameof(after));
                return new GrowthValueComparison(Before, _evaluator.Evaluate(Before.Level, Before.XP, after));
            }
        }
    }
}
