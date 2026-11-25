using System;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Economy
{
    /// <summary>Pure Gold, XP, interest, and stage-gold calculations for GameCore.</summary>
    public static class EconomyRules
    {
        public static int GetExperiencePurchaseGoldCost(EconomyRulesConfiguration configuration,
            bool useSelectBPrice)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            return useSelectBPrice ? configuration.SelectBExperiencePurchaseGoldCost
                : configuration.NormalExperiencePurchaseGoldCost;
        }

        /// <summary>Reports purchase availability using the same non-random guards as the transaction rule.</summary>
        public static bool CanPurchaseExperience(EconomyState state,
            EconomyRulesConfiguration configuration, bool useSelectBPrice)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            int price = GetExperiencePurchaseGoldCost(configuration, useSelectBPrice);
            if (state.Level >= state.MaximumLevel || !HasValidExperienceCurve(state, configuration) ||
                state.Gold < price) return false;
            return TryApplyExperience(state, configuration, configuration.ExperiencePerPurchase,
                out _, out _, out _, out _);
        }

        /// <summary>Expected interest without drawing or advancing an RNG stream.</summary>
        public static decimal CalculateExpectedInterestGold(int goldForInterest,
            EconomyRulesConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (goldForInterest < 0) throw new ArgumentOutOfRangeException(nameof(goldForInterest));
            long guaranteed = Math.Min(
                ((long)goldForInterest / configuration.GoldPerInterestThreshold) * configuration.InterestGoldPerThreshold,
                configuration.MaximumInterestGold);
            int remainder = goldForInterest % configuration.GoldPerInterestThreshold;
            if (guaranteed >= configuration.MaximumInterestGold ||
                configuration.InterestGoldPerThreshold == 0 || remainder == 0)
                return guaranteed;
            int extra = Math.Min(configuration.InterestGoldPerThreshold,
                configuration.MaximumInterestGold - (int)guaranteed);
            return guaranteed + (decimal)remainder * extra / configuration.GoldPerInterestThreshold;
        }

        /// <summary>
        /// SelectB discount is supplied by the caller. Shop lock, stage-selection phase,
        /// and other action-availability rules belong to GameSession.
        /// </summary>
        public static EconomyActionResult TryPurchaseExperience(EconomyState state,
            EconomyRulesConfiguration configuration, bool useSelectBPrice)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            int price = GetExperiencePurchaseGoldCost(configuration, useSelectBPrice);
            if (state.Level >= state.MaximumLevel)
                return Failure(EconomyActionFailureReason.MaximumLevelReached, state);
            if (!HasValidExperienceCurve(state, configuration))
                return Failure(EconomyActionFailureReason.ExperienceCurveMismatch, state);
            if (state.Gold < price)
                return Failure(EconomyActionFailureReason.InsufficientGold, state);

            if (!TryApplyExperience(state, configuration, configuration.ExperiencePerPurchase,
                    out int nextXP, out int nextLevel, out int levelsGained, out EconomyActionFailureReason failure))
                return Failure(failure, state);

            EconomyState nextState = CopyWith(state, state.Gold - price, nextXP, nextLevel);
            return new EconomyActionResult(true, EconomyActionFailureReason.None, nextState,
                -price, configuration.ExperiencePerPurchase, levelsGained);
        }

        /// <summary>
        /// Proposes an XP grant using the same level-up curve as XP purchases. At maximum
        /// Level, additional grants are ignored, matching the existing ShopManager behavior.
        /// </summary>
        public static EconomyActionResult TryAddExperience(EconomyState state,
            EconomyRulesConfiguration configuration, int amount)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (amount < 0) return Failure(EconomyActionFailureReason.InvalidAmount, state);
            if (state.Level >= state.MaximumLevel)
                return new EconomyActionResult(true, EconomyActionFailureReason.None, state, 0, 0, 0);
            if (!HasValidExperienceCurve(state, configuration))
                return Failure(EconomyActionFailureReason.ExperienceCurveMismatch, state);
            if (!TryApplyExperience(state, configuration, amount,
                    out int nextXP, out int nextLevel, out int levelsGained, out EconomyActionFailureReason failure))
                return Failure(failure, state);

            EconomyState nextState = CopyWith(state, state.Gold, nextXP, nextLevel);
            return new EconomyActionResult(true, EconomyActionFailureReason.None,
                nextState, 0, amount, levelsGained);
        }

        /// <summary>Proposes a non-negative Gold grant with overflow protection.</summary>
        public static EconomyActionResult TryAddGold(EconomyState state, int amount)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (amount < 0) return Failure(EconomyActionFailureReason.InvalidAmount, state);
            long total = (long)state.Gold + amount;
            if (total > int.MaxValue) return Failure(EconomyActionFailureReason.GoldOverflow, state);
            EconomyState next = CopyWith(state, (int)total, state.XP, state.Level);
            return new EconomyActionResult(true, EconomyActionFailureReason.None, next, amount, 0, 0);
        }

        /// <summary>Proposes a non-negative Gold expense; insufficient funds leave state unchanged.</summary>
        public static EconomyActionResult TrySpendGold(EconomyState state, int amount)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (amount < 0) return Failure(EconomyActionFailureReason.InvalidAmount, state);
            if (state.Gold < amount) return Failure(EconomyActionFailureReason.InsufficientGold, state);
            EconomyState next = CopyWith(state, state.Gold - amount, state.XP, state.Level);
            return new EconomyActionResult(true, EconomyActionFailureReason.None, next, -amount, 0, 0);
        }

        /// <summary>Proposes a health grant, capped at the run's configured initial maximum HP.</summary>
        public static EconomyActionResult TryApplyHealing(EconomyState state, int amount)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (amount < 0) return Failure(EconomyActionFailureReason.InvalidAmount, state);
            if (!state.HasHealth) return Failure(EconomyActionFailureReason.HealthUnavailable, state);

            int nextHP = (int)Math.Min(state.InitialHP, (long)state.CurrentHP + amount);
            EconomyState next = state.WithHealth(nextHP);
            return new EconomyActionResult(true, EconomyActionFailureReason.None, next,
                0, 0, 0, nextHP - state.CurrentHP);
        }

        /// <summary>Calculates interest from the explicitly supplied pre-reward Gold value.</summary>
        public static InterestCalculationResult CalculateInterest(int goldForInterest,
            EconomyRulesConfiguration configuration, Pcg32RandomSource economyRandom)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (economyRandom == null) throw new ArgumentNullException(nameof(economyRandom));
            Pcg32State originalState = economyRandom.CaptureState();
            if (goldForInterest < 0)
                return new InterestCalculationResult(false, EconomyActionFailureReason.InvalidAmount, 0, 0, originalState);

            long guaranteedLong = Math.Min(
                ((long)goldForInterest / configuration.GoldPerInterestThreshold) * configuration.InterestGoldPerThreshold,
                configuration.MaximumInterestGold);
            int guaranteed = (int)guaranteedLong;
            int randomBonus = 0;

            int remainder = goldForInterest % configuration.GoldPerInterestThreshold;
            bool needsRandomDraw = guaranteed < configuration.MaximumInterestGold &&
                configuration.InterestGoldPerThreshold > 0 && remainder > 0;
            Pcg32State nextState = originalState;
            if (needsRandomDraw)
            {
                Pcg32RandomSource transactionRandom = Pcg32RandomSource.FromState(originalState);
                if (transactionRandom.NextUnitDouble() < (double)remainder / configuration.GoldPerInterestThreshold)
                    randomBonus = Math.Min(configuration.InterestGoldPerThreshold,
                        configuration.MaximumInterestGold - guaranteed);
                nextState = transactionRandom.CaptureState();
            }

            return new InterestCalculationResult(true, EconomyActionFailureReason.None,
                guaranteed, randomBonus, nextState);
        }

        /// <summary>
        /// Calculates a stage reward without applying it. Interest uses Gold before the base
        /// reward, matching ShopManager. A zero base reward produces no interest or hand bonus.
        /// </summary>
        public static StageGoldRewardCalculationResult CalculateStageGoldReward(
            int baseGoldReward, int goldBeforeReward, int emptyHandSlotCount,
            EconomyRulesConfiguration configuration, Pcg32RandomSource economyRandom)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (economyRandom == null) throw new ArgumentNullException(nameof(economyRandom));
            Pcg32State originalState = economyRandom.CaptureState();
            if (baseGoldReward < 0 || goldBeforeReward < 0 || emptyHandSlotCount < 0)
                return StageFailure(EconomyActionFailureReason.InvalidAmount, baseGoldReward, originalState);

            int guaranteed = 0;
            int randomBonus = 0;
            int emptyHandBonus = 0;
            Pcg32State nextRandomState = originalState;
            if (baseGoldReward > 0)
            {
                InterestCalculationResult interest = CalculateInterest(goldBeforeReward, configuration, economyRandom);
                if (!interest.Succeeded)
                    return StageFailure(interest.FailureReason, baseGoldReward, originalState);
                guaranteed = interest.GuaranteedInterest;
                randomBonus = interest.RandomBonusInterest;
                nextRandomState = interest.NextRandomState;

                long handBonus = (long)(emptyHandSlotCount / configuration.EmptyHandSlotsPerBonusGold) *
                    configuration.BonusGoldPerEmptyHandSlots;
                if (handBonus > int.MaxValue)
                    return StageFailure(EconomyActionFailureReason.GoldOverflow, baseGoldReward, originalState);
                emptyHandBonus = (int)handBonus;
            }

            long rewardTotal = (long)baseGoldReward + guaranteed + randomBonus + emptyHandBonus;
            if (rewardTotal > int.MaxValue || (long)goldBeforeReward + rewardTotal > int.MaxValue)
                return StageFailure(EconomyActionFailureReason.GoldOverflow, baseGoldReward, originalState);

            return new StageGoldRewardCalculationResult(true, EconomyActionFailureReason.None,
                baseGoldReward, guaranteed, randomBonus, emptyHandBonus, (int)rewardTotal, nextRandomState);
        }

        /// <summary>Calculates the configured standard-stage base reward without applying it.</summary>
        public static StageGoldRewardCalculationResult CalculateStageGoldReward(int goldBeforeReward,
            int emptyHandSlotCount, EconomyRulesConfiguration configuration, Pcg32RandomSource economyRandom)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            return CalculateStageGoldReward(configuration.BaseStageGoldReward, goldBeforeReward,
                emptyHandSlotCount, configuration, economyRandom);
        }

        private static bool TryApplyExperience(EconomyState state, EconomyRulesConfiguration configuration,
            int amount, out int nextXP, out int nextLevel, out int levelsGained,
            out EconomyActionFailureReason failure)
        {
            nextXP = state.XP;
            nextLevel = state.Level;
            levelsGained = 0;
            failure = EconomyActionFailureReason.None;
            if (amount < 0)
            {
                failure = EconomyActionFailureReason.InvalidAmount;
                return false;
            }
            if (state.Level >= state.MaximumLevel)
            {
                failure = EconomyActionFailureReason.MaximumLevelReached;
                return false;
            }
            if (!HasValidExperienceCurve(state, configuration))
            {
                failure = EconomyActionFailureReason.ExperienceCurveMismatch;
                return false;
            }

            long xp = (long)state.XP + amount;
            while (nextLevel < state.MaximumLevel)
            {
                int needed = configuration.GetRequiredExperience(nextLevel);
                if (needed < 1)
                {
                    failure = EconomyActionFailureReason.ExperienceCurveMismatch;
                    return false;
                }
                if (xp < needed) break;
                xp -= needed;
                nextLevel++;
                levelsGained++;
            }

            if (nextLevel >= state.MaximumLevel)
                nextXP = 0;
            else if (xp > int.MaxValue)
            {
                failure = EconomyActionFailureReason.ExperienceOverflow;
                return false;
            }
            else nextXP = (int)xp;
            return true;
        }

        private static bool HasValidExperienceCurve(EconomyState state,
            EconomyRulesConfiguration configuration)
        {
            return configuration.ExperienceToNextLevel.Count == state.MaximumLevel - 1;
        }

        private static EconomyState CopyWith(EconomyState state, int gold, int xp, int level)
        {
            return state.HasHealth
                ? new EconomyState(gold, xp, level, state.MaximumLevel, state.InitialGold,
                    state.InitialXP, state.InitialLevel, state.CurrentHP, state.MaximumHP)
                : new EconomyState(gold, xp, level, state.MaximumLevel,
                    state.InitialGold, state.InitialXP, state.InitialLevel);
        }

        private static EconomyActionResult Failure(EconomyActionFailureReason reason, EconomyState state)
        {
            return new EconomyActionResult(false, reason, state, 0, 0, 0);
        }

        private static StageGoldRewardCalculationResult StageFailure(
            EconomyActionFailureReason reason, int baseGoldReward, Pcg32State randomState)
        {
            return new StageGoldRewardCalculationResult(false, reason, baseGoldReward,
                0, 0, 0, 0, randomState);
        }
    }
}
