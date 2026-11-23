using System;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Stages
{
    public static class StageSelectionRules
    {
        public static StageProgressResult Select(StageProgressState state,
            StageProgressConfig config, StageChoice choice)
        {
            if (state == null || config == null)
                return Failure(state, StageProgressFailure.InvalidState);
            if (state.Phase != StageProgressPhase.AwaitingStageChoice)
                return Failure(state, StageProgressFailure.ChoiceNotAllowed);
            if (!Enum.IsDefined(typeof(StageChoice), choice))
                return Failure(state, StageProgressFailure.InvalidChoice);

            StageProgressState next = CreatePreparationState(state, config, choice);
            return Success(next, next.CurrentSelection, null, false, false, false, default(Pcg32State));
        }

        internal static StageSelectionOutcome CreateOutcome(StageProgressConfig config,
            int encounterIndex, StageChoice choice)
        {
            int round = config.GetRoundNumber(encounterIndex);
            StageEnemyCategory enemyCategory;
            StageShopMode shopMode = StageShopMode.Character;
            int xp = 0;
            int gold = 0;
            int xpCost = config.NormalExperiencePurchaseGoldCost;
            bool canReroll = true;
            StageSelectionEffect effect = new StageSelectionEffect(
                StageSelectionEffectKind.None, StageSelectionEffectTiming.None, 0, false);

            if (round == config.BossRoundNumber)
            {
                enemyCategory = StageEnemyCategory.Boss;
            }
            else
            {
                switch (choice)
                {
                    case StageChoice.SelectA:
                        enemyCategory = StageEnemyCategory.Normal;
                        xp = config.SelectAExperienceReward;
                        break;
                    case StageChoice.SelectB:
                        enemyCategory = StageEnemyCategory.None;
                        shopMode = StageShopMode.Item;
                        xp = config.SelectBExperienceReward;
                        gold = config.SelectBGoldReward;
                        xpCost = config.SelectBExperiencePurchaseGoldCost;
                        canReroll = config.SelectBCanReroll;
                        break;
                    case StageChoice.SelectC:
                        enemyCategory = StageEnemyCategory.Hard;
                        xp = config.SelectCExperienceReward;
                        gold = config.SelectCGoldReward;
                        break;
                    case StageChoice.SelectD:
                        enemyCategory = StageEnemyCategory.None;
                        xp = config.SelectDExperienceReward;
                        gold = config.SelectDGoldReward;
                        effect = new StageSelectionEffect(StageSelectionEffectKind.HealPlayer,
                            StageSelectionEffectTiming.AtSelection, config.SelectDHealAmount, true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(choice));
                }
            }

            return new StageSelectionOutcome(choice, enemyCategory, shopMode, xp, gold,
                xpCost, canReroll, effect);
        }

        internal static StageProgressState CreatePreparationState(StageProgressState state,
            StageProgressConfig config, StageChoice choice)
        {
            StageSelectionOutcome outcome = CreateOutcome(config, state.EncounterIndex, choice);
            return state.With(state.EncounterIndex, state.EventChancePercent,
                StageProgressPhase.Preparation, outcome, null, false);
        }

        private static StageProgressResult Success(StageProgressState state,
            StageSelectionOutcome selection, StageRewardPending reward, bool eventStarted,
            bool hasRng, bool rngConsumed, Pcg32State rng)
        {
            return new StageProgressResult(true, StageProgressFailure.None, state,
                selection, reward, eventStarted, hasRng, rngConsumed, rng);
        }

        private static StageProgressResult Failure(StageProgressState state, StageProgressFailure failure)
        {
            return new StageProgressResult(false, failure, state, null, null,
                false, false, false, default(Pcg32State));
        }
    }
}
