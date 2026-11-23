using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Stages
{
    public enum StageRewardFailure
    {
        None,
        InvalidInput,
        RewardNotPending,
        SelectionNotReady,
        SelectionEffectAlreadyApplied,
        SelectionEffectNotApplied,
        HealthUnavailable,
        EconomyRuleFailed,
        ProgressionRuleFailed
    }

    /// <summary>Atomic proposal containing both next states and all applied reward details.</summary>
    public sealed class StageRewardResult
    {
        public bool Succeeded { get; }
        public StageRewardFailure Failure { get; }
        public EconomyActionFailureReason EconomyFailure { get; }
        public StageProgressFailure ProgressionFailure { get; }
        public StageProgressState NextStageState { get; }
        public EconomyState NextEconomyState { get; }
        public int BaseGold { get; }
        public int GuaranteedInterestGold { get; }
        public int RandomBonusInterestGold { get; }
        public int EmptyHandSlotGold { get; }
        public int TotalGoldAdded { get; }
        public int ExperienceAdded { get; }
        public int LevelsGained { get; }
        public bool IsBossReward { get; }
        public bool IsFinalStageReward { get; }
        public bool IsSelectionEffect { get; }
        public bool SelectionEffectApplied { get; }
        public bool EventStarted { get; }
        public int GoldBefore { get; }
        public int GoldAfter { get; }
        public int XPBefore { get; }
        public int XPAfter { get; }
        public int LevelBefore { get; }
        public int LevelAfter { get; }
        public int HPBefore { get; }
        public int HPAfter { get; }
        public bool HasNextEconomyRandomState { get; }
        public Pcg32State NextEconomyRandomState { get; }
        public bool EconomyRandomConsumed { get; }
        public bool HasNextEventRandomState { get; }
        public Pcg32State NextEventRandomState { get; }
        public bool EventRandomConsumed { get; }

        internal StageRewardResult(bool succeeded, StageRewardFailure failure,
            EconomyActionFailureReason economyFailure, StageProgressFailure progressionFailure,
            StageProgressState nextStageState, EconomyState nextEconomyState,
            int baseGold, int guaranteedInterestGold, int randomBonusInterestGold,
            int emptyHandSlotGold, int totalGoldAdded, int experienceAdded, int levelsGained,
            bool isBossReward, bool isFinalStageReward, bool isSelectionEffect,
            bool selectionEffectApplied, int goldBefore, int goldAfter, int xpBefore,
            int xpAfter, int levelBefore, int levelAfter, int hpBefore, int hpAfter,
            bool hasNextEconomyRandomState, Pcg32State nextEconomyRandomState,
            bool economyRandomConsumed, bool hasNextEventRandomState,
            Pcg32State nextEventRandomState, bool eventRandomConsumed, bool eventStarted)
        {
            Succeeded = succeeded;
            Failure = failure;
            EconomyFailure = economyFailure;
            ProgressionFailure = progressionFailure;
            NextStageState = nextStageState;
            NextEconomyState = nextEconomyState;
            BaseGold = baseGold;
            GuaranteedInterestGold = guaranteedInterestGold;
            RandomBonusInterestGold = randomBonusInterestGold;
            EmptyHandSlotGold = emptyHandSlotGold;
            TotalGoldAdded = totalGoldAdded;
            ExperienceAdded = experienceAdded;
            LevelsGained = levelsGained;
            IsBossReward = isBossReward;
            IsFinalStageReward = isFinalStageReward;
            IsSelectionEffect = isSelectionEffect;
            SelectionEffectApplied = selectionEffectApplied;
            GoldBefore = goldBefore;
            GoldAfter = goldAfter;
            XPBefore = xpBefore;
            XPAfter = xpAfter;
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
            HPBefore = hpBefore;
            HPAfter = hpAfter;
            HasNextEconomyRandomState = hasNextEconomyRandomState;
            NextEconomyRandomState = nextEconomyRandomState;
            EconomyRandomConsumed = economyRandomConsumed;
            HasNextEventRandomState = hasNextEventRandomState;
            NextEventRandomState = nextEventRandomState;
            EventRandomConsumed = eventRandomConsumed;
            EventStarted = eventStarted;
        }
    }
}
