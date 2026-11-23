using System;

namespace ProjectNST.GameCore.Stages
{
    /// <summary>Immutable, caller-supplied configuration for encounter progression.</summary>
    public sealed class StageProgressConfig
    {
        public int TotalEncounterCount { get; }
        public int RoundsPerStage { get; }
        public int ChoiceRoundNumber { get; }
        public int BossRoundNumber { get; }
        public int InitialEventChancePercent { get; }
        public int EventChanceIncreasePercent { get; }
        public int MaximumEventChancePercent { get; }
        public int SelectAExperienceReward { get; }
        public int SelectBExperienceReward { get; }
        public int SelectBGoldReward { get; }
        public int SelectCExperienceReward { get; }
        public int SelectCGoldReward { get; }
        public int SelectDExperienceReward { get; }
        public int SelectDGoldReward { get; }
        public int SelectDHealAmount { get; }
        public int NormalExperiencePurchaseGoldCost { get; }
        public int SelectBExperiencePurchaseGoldCost { get; }
        public bool SelectBCanReroll { get; }

        public int TotalStageCount => TotalEncounterCount / RoundsPerStage;

        public StageProgressConfig(int totalEncounterCount, int roundsPerStage, int choiceRoundNumber,
            int bossRoundNumber, int initialEventChancePercent, int eventChanceIncreasePercent,
            int maximumEventChancePercent, int selectAExperienceReward, int selectBExperienceReward,
            int selectBGoldReward, int selectCExperienceReward, int selectCGoldReward,
            int selectDExperienceReward, int selectDGoldReward, int selectDHealAmount,
            int normalExperiencePurchaseGoldCost, int selectBExperiencePurchaseGoldCost,
            bool selectBCanReroll)
        {
            if (totalEncounterCount <= 0) throw new ArgumentOutOfRangeException(nameof(totalEncounterCount));
            if (roundsPerStage <= 0 || totalEncounterCount % roundsPerStage != 0)
                throw new ArgumentOutOfRangeException(nameof(roundsPerStage));
            if (choiceRoundNumber < 1 || choiceRoundNumber > roundsPerStage)
                throw new ArgumentOutOfRangeException(nameof(choiceRoundNumber));
            if (bossRoundNumber < 1 || bossRoundNumber > roundsPerStage)
                throw new ArgumentOutOfRangeException(nameof(bossRoundNumber));
            ValidateChance(initialEventChancePercent, nameof(initialEventChancePercent));
            ValidateChance(eventChanceIncreasePercent, nameof(eventChanceIncreasePercent));
            ValidateChance(maximumEventChancePercent, nameof(maximumEventChancePercent));
            if (initialEventChancePercent > maximumEventChancePercent)
                throw new ArgumentException("Initial event chance cannot exceed its maximum.", nameof(initialEventChancePercent));
            ValidateNonNegative(selectAExperienceReward, nameof(selectAExperienceReward));
            ValidateNonNegative(selectBExperienceReward, nameof(selectBExperienceReward));
            ValidateNonNegative(selectBGoldReward, nameof(selectBGoldReward));
            ValidateNonNegative(selectCExperienceReward, nameof(selectCExperienceReward));
            ValidateNonNegative(selectCGoldReward, nameof(selectCGoldReward));
            ValidateNonNegative(selectDExperienceReward, nameof(selectDExperienceReward));
            ValidateNonNegative(selectDGoldReward, nameof(selectDGoldReward));
            ValidateNonNegative(selectDHealAmount, nameof(selectDHealAmount));
            ValidateNonNegative(normalExperiencePurchaseGoldCost, nameof(normalExperiencePurchaseGoldCost));
            ValidateNonNegative(selectBExperiencePurchaseGoldCost, nameof(selectBExperiencePurchaseGoldCost));

            TotalEncounterCount = totalEncounterCount;
            RoundsPerStage = roundsPerStage;
            ChoiceRoundNumber = choiceRoundNumber;
            BossRoundNumber = bossRoundNumber;
            InitialEventChancePercent = initialEventChancePercent;
            EventChanceIncreasePercent = eventChanceIncreasePercent;
            MaximumEventChancePercent = maximumEventChancePercent;
            SelectAExperienceReward = selectAExperienceReward;
            SelectBExperienceReward = selectBExperienceReward;
            SelectBGoldReward = selectBGoldReward;
            SelectCExperienceReward = selectCExperienceReward;
            SelectCGoldReward = selectCGoldReward;
            SelectDExperienceReward = selectDExperienceReward;
            SelectDGoldReward = selectDGoldReward;
            SelectDHealAmount = selectDHealAmount;
            NormalExperiencePurchaseGoldCost = normalExperiencePurchaseGoldCost;
            SelectBExperiencePurchaseGoldCost = selectBExperiencePurchaseGoldCost;
            SelectBCanReroll = selectBCanReroll;
        }

        public int GetStageNumber(int encounterIndex)
        {
            ValidateEncounterIndex(encounterIndex);
            return encounterIndex / RoundsPerStage + 1;
        }

        public int GetRoundNumber(int encounterIndex)
        {
            ValidateEncounterIndex(encounterIndex);
            return encounterIndex % RoundsPerStage + 1;
        }

        private void ValidateEncounterIndex(int encounterIndex)
        {
            if (encounterIndex < 0 || encounterIndex >= TotalEncounterCount)
                throw new ArgumentOutOfRangeException(nameof(encounterIndex));
        }

        private static void ValidateChance(int value, string parameterName)
        {
            if (value < 0 || value > 100) throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateNonNegative(int value, string parameterName)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
