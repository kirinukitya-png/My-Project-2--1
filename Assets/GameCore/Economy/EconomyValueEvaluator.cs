using System;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Economy
{
    public enum GoldHoldingModel { Linear, TwoSegment }
    public enum InterestRewardEligibility { Unknown, Ineligible, Eligible }

    public sealed class EconomyValueSettings
    {
        public GoldHoldingModel HoldingModel { get; }
        public decimal ReserveRate { get; }
        public decimal SurplusRate { get; }
        public int ReserveGold { get; }
        public decimal InterestWeight { get; }

        public EconomyValueSettings(GoldHoldingModel holdingModel, decimal reserveRate,
            decimal surplusRate, int reserveGold, decimal interestWeight)
        {
            if (holdingModel != GoldHoldingModel.Linear && holdingModel != GoldHoldingModel.TwoSegment)
                throw new ArgumentOutOfRangeException(nameof(holdingModel));
            if (surplusRate <= 0) throw new ArgumentOutOfRangeException(nameof(surplusRate));
            if (reserveRate < surplusRate) throw new ArgumentOutOfRangeException(nameof(reserveRate));
            if (reserveGold < 0) throw new ArgumentOutOfRangeException(nameof(reserveGold));
            if (interestWeight < 0) throw new ArgumentOutOfRangeException(nameof(interestWeight));
            if (holdingModel == GoldHoldingModel.Linear && (reserveGold != 0 || reserveRate != surplusRate))
                throw new ArgumentException("Linear holding value requires a single rate and no reserve boundary.");
            HoldingModel = holdingModel;
            ReserveRate = reserveRate;
            SurplusRate = surplusRate;
            ReserveGold = reserveGold;
            InterestWeight = interestWeight;
        }

        public static EconomyValueSettings Linear(decimal rate, decimal interestWeight = 0) =>
            new EconomyValueSettings(GoldHoldingModel.Linear, rate, rate, 0, interestWeight);

        public static EconomyValueSettings TwoSegment(decimal reserveRate, decimal surplusRate,
            int reserveGold, decimal interestWeight = 0) =>
            new EconomyValueSettings(GoldHoldingModel.TwoSegment, reserveRate, surplusRate, reserveGold, interestWeight);
    }

    public sealed class EconomyInterestContext
    {
        public int GoldPerThreshold { get; }
        public int InterestPerThreshold { get; }
        public int MaximumInterestGold { get; }
        public InterestRewardEligibility Eligibility { get; }

        public EconomyInterestContext(int goldPerThreshold, int interestPerThreshold,
            int maximumInterestGold, InterestRewardEligibility eligibility)
        {
            if (goldPerThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(goldPerThreshold));
            if (interestPerThreshold < 0) throw new ArgumentOutOfRangeException(nameof(interestPerThreshold));
            if (maximumInterestGold < 0) throw new ArgumentOutOfRangeException(nameof(maximumInterestGold));
            if (!Enum.IsDefined(typeof(InterestRewardEligibility), eligibility))
                throw new ArgumentOutOfRangeException(nameof(eligibility));
            GoldPerThreshold = goldPerThreshold;
            InterestPerThreshold = interestPerThreshold;
            MaximumInterestGold = maximumInterestGold;
            Eligibility = eligibility;
        }
    }

    public static class EconomyInterestObservation
    {
        public static EconomyInterestContext FromObservation(GameObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            var rules = observation.EconomyRules ??
                throw new ArgumentException("Public economy rules are required.", nameof(observation));
            return new EconomyInterestContext(rules.GoldPerInterestThreshold, rules.InterestGoldPerThreshold,
                rules.MaximumInterestGold, GetEligibility(observation.Stage));
        }

        private static InterestRewardEligibility GetEligibility(StageObservation stage)
        {
            switch (stage.Phase)
            {
                case ObservationPhase.EventPending:
                case ObservationPhase.Completed:
                case ObservationPhase.GameOver:
                    return InterestRewardEligibility.Ineligible;
                case ObservationPhase.Preparation:
                case ObservationPhase.Battle:
                case ObservationPhase.RewardPending:
                    int? baseGold = stage.SelectedChoiceDetails?.PostBattleGold;
                    if (baseGold.HasValue)
                        return baseGold.Value > 0 ? InterestRewardEligibility.Eligible : InterestRewardEligibility.Ineligible;
                    break;
            }
            return InterestRewardEligibility.Unknown;
        }
    }

    public readonly struct EconomyValue
    {
        public int Gold { get; }
        public decimal HoldingValue { get; }
        public decimal ExpectedInterestGold { get; }
        public decimal WeightedInterestValue { get; }
        public decimal TotalValue { get; }

        internal EconomyValue(int gold, decimal holdingValue, decimal expectedInterestGold, decimal weightedInterestValue)
        {
            Gold = gold;
            HoldingValue = holdingValue;
            ExpectedInterestGold = expectedInterestGold;
            WeightedInterestValue = weightedInterestValue;
            TotalValue = holdingValue + weightedInterestValue;
        }
    }

    public readonly struct EconomyValueComparison
    {
        public EconomyValue Before { get; }
        public EconomyValue After { get; }
        public InterestRewardEligibility Eligibility { get; }
        public decimal HoldingDelta => After.HoldingValue - Before.HoldingValue;
        public decimal ExpectedInterestGoldDelta => After.ExpectedInterestGold - Before.ExpectedInterestGold;
        public decimal WeightedInterestDelta => After.WeightedInterestValue - Before.WeightedInterestValue;
        public decimal TotalDelta => After.TotalValue - Before.TotalValue;

        internal EconomyValueComparison(EconomyValue before, EconomyValue after, InterestRewardEligibility eligibility)
        {
            Before = before;
            After = after;
            Eligibility = eligibility;
        }
    }

    public sealed class EconomyValueEvaluator
    {
        public EconomyValueSettings Settings { get; }
        public EconomyInterestContext Interest { get; }

        public EconomyValueEvaluator(EconomyValueSettings settings, EconomyInterestContext interest)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Interest = interest ?? throw new ArgumentNullException(nameof(interest));
        }

        public EconomyValue Evaluate(int gold)
        {
            if (gold < 0) throw new ArgumentOutOfRangeException(nameof(gold));

            int reserve = Math.Min(gold, Settings.ReserveGold);
            decimal holding = reserve * Settings.ReserveRate + (gold - reserve) * Settings.SurplusRate;
            decimal expectedInterest = Interest.Eligibility == InterestRewardEligibility.Eligible
                ? EconomyRules.CalculateExpectedInterestGold(gold, Interest.GoldPerThreshold,
                    Interest.InterestPerThreshold, Interest.MaximumInterestGold)
                : 0m;
            return new EconomyValue(gold, holding, expectedInterest, Settings.InterestWeight * expectedInterest);
        }

        public EconomyValueComparison Compare(int beforeGold, int afterGold) =>
            CreateBaseline(beforeGold).Compare(afterGold);

        public Baseline CreateBaseline(int beforeGold) => new Baseline(this, Evaluate(beforeGold));

        /// <summary>Reuses the before value and the same immutable reward context for each candidate.</summary>
        public sealed class Baseline
        {
            private readonly EconomyValueEvaluator _evaluator;
            public EconomyValue Before { get; }

            internal Baseline(EconomyValueEvaluator evaluator, EconomyValue before)
            {
                _evaluator = evaluator;
                Before = before;
            }

            public EconomyValueComparison Compare(int afterGold) =>
                new EconomyValueComparison(Before, _evaluator.Evaluate(afterGold), _evaluator.Interest.Eligibility);

            public EconomyValueComparison CompareGoldChange(int goldChange) => Compare(checked(Before.Gold + goldChange));
        }
    }
}
