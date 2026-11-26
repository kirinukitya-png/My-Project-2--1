using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Economy
{
    /// <summary>Immutable copy of the economy values configured for a simulation run.</summary>
    public sealed class EconomyRulesConfiguration
    {
        public ExperienceCurve ExperienceCurve { get; }

        public int NormalExperiencePurchaseGoldCost { get; }
        public int SelectBExperiencePurchaseGoldCost { get; }
        public int ExperiencePerPurchase { get; }
        public IReadOnlyList<int> ExperienceToNextLevel { get; }
        public int GoldPerInterestThreshold { get; }
        public int InterestGoldPerThreshold { get; }
        public int MaximumInterestGold { get; }
        public int BaseStageGoldReward { get; }
        public int EmptyHandSlotsPerBonusGold { get; }
        public int BonusGoldPerEmptyHandSlots { get; }

        public EconomyRulesConfiguration(int normalExperiencePurchaseGoldCost,
            int selectBExperiencePurchaseGoldCost, int experiencePerPurchase,
            IEnumerable<int> experienceToNextLevel, int goldPerInterestThreshold,
            int interestGoldPerThreshold, int maximumInterestGold, int baseStageGoldReward,
            int emptyHandSlotsPerBonusGold, int bonusGoldPerEmptyHandSlots)
        {
            NormalExperiencePurchaseGoldCost = RequireNonNegative(normalExperiencePurchaseGoldCost, nameof(normalExperiencePurchaseGoldCost));
            SelectBExperiencePurchaseGoldCost = RequireNonNegative(selectBExperiencePurchaseGoldCost, nameof(selectBExperiencePurchaseGoldCost));
            ExperiencePerPurchase = RequireNonNegative(experiencePerPurchase, nameof(experiencePerPurchase));
            if (experienceToNextLevel == null) throw new ArgumentNullException(nameof(experienceToNextLevel));
            ExperienceCurve = new ExperienceCurve(experienceToNextLevel);
            ExperienceToNextLevel = ExperienceCurve.Thresholds;
            if (goldPerInterestThreshold < 1) throw new ArgumentOutOfRangeException(nameof(goldPerInterestThreshold));
            GoldPerInterestThreshold = goldPerInterestThreshold;
            InterestGoldPerThreshold = RequireNonNegative(interestGoldPerThreshold, nameof(interestGoldPerThreshold));
            MaximumInterestGold = RequireNonNegative(maximumInterestGold, nameof(maximumInterestGold));
            BaseStageGoldReward = RequireNonNegative(baseStageGoldReward, nameof(baseStageGoldReward));
            if (emptyHandSlotsPerBonusGold < 1) throw new ArgumentOutOfRangeException(nameof(emptyHandSlotsPerBonusGold));
            EmptyHandSlotsPerBonusGold = emptyHandSlotsPerBonusGold;
            BonusGoldPerEmptyHandSlots = RequireNonNegative(bonusGoldPerEmptyHandSlots, nameof(bonusGoldPerEmptyHandSlots));
        }

        private static int RequireNonNegative(int value, string parameterName)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }
}
