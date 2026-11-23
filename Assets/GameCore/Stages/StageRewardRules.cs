using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Stages
{
    /// <summary>Pure selection-effect and victory-reward rules. Both next states are returned atomically.</summary>
    public static class StageRewardRules
    {
        public static StageRewardResult ApplySelectionEffect(StageProgressState stageState,
            EconomyState economyState)
        {
            if (stageState == null || economyState == null)
                return Failure(stageState, economyState, StageRewardFailure.InvalidInput);
            if (stageState.Phase != StageProgressPhase.Preparation || stageState.CurrentSelection == null)
                return Failure(stageState, economyState, StageRewardFailure.SelectionNotReady);

            StageSelectionEffect effect = stageState.CurrentSelection.SelectionEffect;
            if (effect == null || effect.Kind == StageSelectionEffectKind.None)
                return CreateSuccess(stageState, economyState, economyState, 0, 0, 0, 0, 0, 0,
                    false, false, false, false, false, default(Pcg32State), false,
                    false, default(Pcg32State), false, false);
            if (stageState.SelectionEffectApplied)
                return Failure(stageState, economyState, StageRewardFailure.SelectionEffectAlreadyApplied);
            if (effect.Kind != StageSelectionEffectKind.HealPlayer ||
                effect.Timing != StageSelectionEffectTiming.AtSelection)
                return Failure(stageState, economyState, StageRewardFailure.InvalidInput);

            EconomyActionResult healing = EconomyRules.TryApplyHealing(economyState, effect.Amount);
            if (!healing.Succeeded)
            {
                StageRewardFailure failure = healing.FailureReason == EconomyActionFailureReason.HealthUnavailable
                    ? StageRewardFailure.HealthUnavailable
                    : StageRewardFailure.EconomyRuleFailed;
                return Failure(stageState, economyState, failure, healing.FailureReason);
            }

            StageProgressState nextStage = stageState.With(stageState.EncounterIndex,
                stageState.EventChancePercent, stageState.Phase, stageState.CurrentSelection,
                stageState.PendingReward, true);
            return CreateSuccess(nextStage, economyState, healing.NextState, 0, 0, 0, 0, 0, 0,
                false, false, true, true, false, default(Pcg32State), false,
                false, default(Pcg32State), false, false);
        }

        /// <summary>
        /// Applies a pending victory reward and progression together. The input RNG checkpoints
        /// are copied; the caller adopts both next checkpoints only when this result succeeds.
        /// </summary>
        public static StageRewardResult ApplyVictoryReward(StageProgressState stageState,
            StageProgressConfig stageConfiguration, EconomyState economyState,
            EconomyRulesConfiguration economyConfiguration, StageRewardConfiguration rewardConfiguration,
            HandState handState, Pcg32State economyRandomState, Pcg32State eventRandomState)
        {
            if (stageState == null || stageConfiguration == null || economyState == null ||
                economyConfiguration == null || rewardConfiguration == null || handState == null)
                return Failure(stageState, economyState, StageRewardFailure.InvalidInput);
            if (stageState.Phase != StageProgressPhase.RewardPending || stageState.PendingReward == null ||
                stageState.CurrentSelection == null)
                return Failure(stageState, economyState, StageRewardFailure.RewardNotPending);
            if (stageState.CurrentSelection.SelectionEffect != null &&
                stageState.CurrentSelection.SelectionEffect.Kind != StageSelectionEffectKind.None &&
                !stageState.SelectionEffectApplied)
                return Failure(stageState, economyState, StageRewardFailure.SelectionEffectNotApplied);

            bool isBoss = stageState.PendingReward.IsBossReward;
            bool isFinal = stageState.PendingReward.IsFinalVictory;
            int baseGold = ResolveBaseGold(stageState, economyConfiguration, rewardConfiguration);
            int experience = isBoss
                ? rewardConfiguration.BossExperienceReward
                : stageState.CurrentSelection.ExperienceReward;

            Pcg32RandomSource economyRandom;
            try
            {
                economyRandom = Pcg32RandomSource.FromState(economyRandomState);
            }
            catch (System.ArgumentException)
            {
                return Failure(stageState, economyState, StageRewardFailure.InvalidInput);
            }

            StageGoldRewardCalculationResult goldCalculation = EconomyRules.CalculateStageGoldReward(
                baseGold, economyState.Gold, handState.EmptySlotCount, economyConfiguration, economyRandom);
            if (!goldCalculation.Succeeded)
                return Failure(stageState, economyState, StageRewardFailure.EconomyRuleFailed,
                    goldCalculation.FailureReason);

            EconomyActionResult xpResult = EconomyRules.TryAddExperience(
                economyState, economyConfiguration, experience);
            if (!xpResult.Succeeded)
                return Failure(stageState, economyState, StageRewardFailure.EconomyRuleFailed,
                    xpResult.FailureReason);

            EconomyActionResult goldResult = EconomyRules.TryAddGold(
                xpResult.NextState, goldCalculation.TotalGoldReward);
            if (!goldResult.Succeeded)
                return Failure(stageState, economyState, StageRewardFailure.EconomyRuleFailed,
                    goldResult.FailureReason);

            StageProgressResult progression = StageProgressRules.CompletePendingReward(
                stageState, stageConfiguration, eventRandomState);
            if (!progression.Succeeded)
                return Failure(stageState, economyState, StageRewardFailure.ProgressionRuleFailed,
                    EconomyActionFailureReason.None, progression.Failure);

            bool economyRandomConsumed = !StateEquals(economyRandomState, goldCalculation.NextRandomState);
            return CreateSuccess(progression.NextState, economyState, goldResult.NextState,
                goldCalculation.BaseGoldReward, goldCalculation.GuaranteedInterest,
                goldCalculation.RandomBonusInterest, goldCalculation.EmptyHandSlotBonus,
                goldCalculation.TotalGoldReward, xpResult.ExperienceAdded,
                isBoss, isFinal, false, false, true, goldCalculation.NextRandomState,
                economyRandomConsumed, progression.HasNextEventRandomState,
                progression.NextEventRandomState, progression.EventRandomConsumed,
                progression.EventStarted);
        }

        private static int ResolveBaseGold(StageProgressState state,
            EconomyRulesConfiguration economyConfiguration, StageRewardConfiguration rewardConfiguration)
        {
            if (state.PendingReward.IsBossReward) return rewardConfiguration.BossGoldReward;
            switch (state.PendingReward.Choice)
            {
                case StageChoice.SelectA: return economyConfiguration.BaseStageGoldReward;
                case StageChoice.SelectB:
                case StageChoice.SelectC:
                case StageChoice.SelectD: return state.CurrentSelection.GoldReward;
                default: return -1;
            }
        }

        private static StageRewardResult CreateSuccess(StageProgressState stageState,
            EconomyState economyBefore, EconomyState economyAfter,
            int baseGold, int guaranteedInterest, int randomInterest,
            int emptyHandGold, int totalGold, int experience, bool isBoss, bool isFinal,
            bool isSelectionEffect, bool selectionEffectApplied,
            bool hasEconomyRng, Pcg32State economyRng, bool economyRngConsumed,
            bool hasEventRng, Pcg32State eventRng, bool eventRngConsumed, bool eventStarted)
        {
            return new StageRewardResult(true, StageRewardFailure.None,
                EconomyActionFailureReason.None, StageProgressFailure.None, stageState, economyAfter,
                baseGold, guaranteedInterest, randomInterest, emptyHandGold, totalGold,
                experience, economyAfter.Level - economyBefore.Level, isBoss, isFinal,
                isSelectionEffect, selectionEffectApplied, economyBefore.Gold, economyAfter.Gold,
                economyBefore.XP, economyAfter.XP, economyBefore.Level, economyAfter.Level,
                economyBefore.HasHealth ? economyBefore.CurrentHP : -1,
                economyAfter.HasHealth ? economyAfter.CurrentHP : -1,
                hasEconomyRng, economyRng, economyRngConsumed,
                hasEventRng, eventRng, eventRngConsumed, eventStarted);
        }

        private static StageRewardResult Failure(StageProgressState stageState,
            EconomyState economyState, StageRewardFailure failure,
            EconomyActionFailureReason economyFailure = EconomyActionFailureReason.None,
            StageProgressFailure progressFailure = StageProgressFailure.None)
        {
            return new StageRewardResult(false, failure, economyFailure, progressFailure,
                stageState, economyState, 0, 0, 0, 0, 0, 0, 0, false, false,
                false, false, economyState != null ? economyState.Gold : 0,
                economyState != null ? economyState.Gold : 0,
                economyState != null ? economyState.XP : 0,
                economyState != null ? economyState.XP : 0,
                economyState != null ? economyState.Level : 0,
                economyState != null ? economyState.Level : 0,
                economyState != null && economyState.HasHealth ? economyState.CurrentHP : -1,
                economyState != null && economyState.HasHealth ? economyState.CurrentHP : -1,
                false, default(Pcg32State), false, false, default(Pcg32State), false, false);
        }

        private static bool StateEquals(Pcg32State left, Pcg32State right)
        {
            return left.AlgorithmVersion == right.AlgorithmVersion &&
                left.State == right.State && left.Increment == right.Increment;
        }
    }
}
