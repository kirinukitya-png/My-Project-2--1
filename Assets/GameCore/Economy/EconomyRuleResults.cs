using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Economy
{
    public enum EconomyActionFailureReason
    {
        None = 0,
        InvalidAmount = 1,
        InsufficientGold = 2,
        MaximumLevelReached = 3,
        ExperienceCurveMismatch = 4,
        GoldOverflow = 5,
        ExperienceOverflow = 6,
        HealthUnavailable = 7
    }

    /// <summary>Proposed result of a pure economy action.</summary>
    public sealed class EconomyActionResult
    {
        public bool Succeeded { get; }
        public EconomyActionFailureReason FailureReason { get; }
        public EconomyState NextState { get; }
        public int GoldDelta { get; }
        public int ExperienceAdded { get; }
        public int LevelsGained { get; }
        public int HealthDelta { get; }

        internal EconomyActionResult(bool succeeded, EconomyActionFailureReason failureReason,
            EconomyState nextState, int goldDelta, int experienceAdded, int levelsGained)
            : this(succeeded, failureReason, nextState, goldDelta, experienceAdded, levelsGained, 0)
        {
        }

        internal EconomyActionResult(bool succeeded, EconomyActionFailureReason failureReason,
            EconomyState nextState, int goldDelta, int experienceAdded, int levelsGained,
            int healthDelta)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            NextState = nextState;
            GoldDelta = goldDelta;
            ExperienceAdded = experienceAdded;
            LevelsGained = levelsGained;
            HealthDelta = healthDelta;
        }
    }

    public sealed class InterestCalculationResult
    {
        public bool Succeeded { get; }
        public EconomyActionFailureReason FailureReason { get; }
        public int GuaranteedInterest { get; }
        public int RandomBonusInterest { get; }
        public int TotalInterest => GuaranteedInterest + RandomBonusInterest;
        public Pcg32State NextRandomState { get; }

        internal InterestCalculationResult(bool succeeded, EconomyActionFailureReason failureReason,
            int guaranteedInterest, int randomBonusInterest, Pcg32State nextRandomState)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            GuaranteedInterest = guaranteedInterest;
            RandomBonusInterest = randomBonusInterest;
            NextRandomState = nextRandomState;
        }
    }

    /// <summary>Calculation only: callers decide separately whether and when to apply this reward.</summary>
    public sealed class StageGoldRewardCalculationResult
    {
        public bool Succeeded { get; }
        public EconomyActionFailureReason FailureReason { get; }
        public int BaseGoldReward { get; }
        public int GuaranteedInterest { get; }
        public int RandomBonusInterest { get; }
        public int TotalInterest => GuaranteedInterest + RandomBonusInterest;
        public int EmptyHandSlotBonus { get; }
        public int TotalGoldReward { get; }
        public Pcg32State NextRandomState { get; }

        internal StageGoldRewardCalculationResult(bool succeeded, EconomyActionFailureReason failureReason,
            int baseGoldReward, int guaranteedInterest, int randomBonusInterest,
            int emptyHandSlotBonus, int totalGoldReward, Pcg32State nextRandomState)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            BaseGoldReward = baseGoldReward;
            GuaranteedInterest = guaranteedInterest;
            RandomBonusInterest = randomBonusInterest;
            EmptyHandSlotBonus = emptyHandSlotBonus;
            TotalGoldReward = totalGoldReward;
            NextRandomState = nextRandomState;
        }
    }
}
