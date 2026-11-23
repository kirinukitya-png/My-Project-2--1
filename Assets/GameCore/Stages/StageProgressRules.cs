using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Stages
{
    /// <summary>Pure stage/encounter transitions. Callers adopt returned state and RNG atomically.</summary>
    public static class StageProgressRules
    {
        public static StageProgressState CreateInitialState(StageProgressConfig config)
        {
            if (config == null) throw new System.ArgumentNullException(nameof(config));
            return new StageProgressState(0, config.InitialEventChancePercent,
                StageProgressPhase.AwaitingEncounterStart, null, null);
        }

        public static StageProgressResult BeginCurrentEncounter(StageProgressState state,
            StageProgressConfig config)
        {
            if (state == null || config == null) return Failure(state, StageProgressFailure.InvalidState);
            if (state.Phase != StageProgressPhase.AwaitingEncounterStart ||
                state.EncounterIndex < 0 || state.EncounterIndex >= config.TotalEncounterCount)
                return Failure(state, StageProgressFailure.InvalidState);

            if (config.GetRoundNumber(state.EncounterIndex) == config.ChoiceRoundNumber)
            {
                StageProgressState choiceState = state.With(state.EncounterIndex,
                    state.EventChancePercent, StageProgressPhase.AwaitingStageChoice, null, null, false);
                return Success(choiceState, null, null, false, false, false, default(Pcg32State));
            }

            StageProgressState preparation = StageSelectionRules.CreatePreparationState(
                state, config, StageChoice.SelectA);
            return Success(preparation, preparation.CurrentSelection, null,
                false, false, false, default(Pcg32State));
        }

        public static StageProgressResult BeginBattle(StageProgressState state)
        {
            if (state == null) return Failure(null, StageProgressFailure.InvalidState);
            if (state.Phase != StageProgressPhase.Preparation)
                return Failure(state, StageProgressFailure.NotAwaitingBattle);
            if (state.CurrentSelection == null)
                return Failure(state, StageProgressFailure.InvalidState);
            if (state.CurrentSelection.EnemyCategory == StageEnemyCategory.None)
                return Failure(state, StageProgressFailure.NoEnemyEncounterRequired);

            StageProgressState next = state.With(state.EncounterIndex, state.EventChancePercent,
                StageProgressPhase.Battle, state.CurrentSelection, null);
            return Success(next, next.CurrentSelection, null, false, false, false, default(Pcg32State));
        }

        public static StageProgressResult ResolveBattle(StageProgressState state,
            StageProgressConfig config, bool won)
        {
            if (state == null || config == null) return Failure(state, StageProgressFailure.InvalidState);
            if (state.Phase != StageProgressPhase.Battle)
                return Failure(state, StageProgressFailure.NotAwaitingBattle);
            return won ? CreateRewardPending(state, config) : CreateGameOver(state);
        }

        /// <summary>Routes a selected no-enemy encounter to the same reward-pending path as a win.</summary>
        public static StageProgressResult ResolveNoEnemySelectionAsVictory(
            StageProgressState state, StageProgressConfig config)
        {
            if (state == null || config == null) return Failure(state, StageProgressFailure.InvalidState);
            if (state.Phase != StageProgressPhase.Preparation || state.CurrentSelection == null ||
                state.CurrentSelection.EnemyCategory != StageEnemyCategory.None)
                return Failure(state, StageProgressFailure.NoEnemyEncounterRequired);
            return CreateRewardPending(state, config);
        }

        /// <summary>
        /// Acknowledges that Stage 7-C has applied the pending reward, then advances or completes.
        /// The input Event RNG checkpoint is copied; the caller's RNG is never mutated.
        /// </summary>
        public static StageProgressResult CompletePendingReward(StageProgressState state,
            StageProgressConfig config, Pcg32State eventRandomState)
        {
            if (state == null || config == null) return Failure(state, StageProgressFailure.InvalidState);
            if (state.Phase != StageProgressPhase.RewardPending || state.PendingReward == null)
                return Failure(state, StageProgressFailure.NotAwaitingReward);

            if (state.PendingReward.IsFinalVictory)
            {
                StageProgressState completed = state.With(state.EncounterIndex,
                    state.EventChancePercent, StageProgressPhase.Completed,
                    state.CurrentSelection, null);
                return Success(completed, completed.CurrentSelection, null,
                    false, true, false, eventRandomState);
            }

            int nextIndex = state.EncounterIndex + 1;
            if (nextIndex >= config.TotalEncounterCount)
                return Failure(state, StageProgressFailure.InvalidState);

            int chance = state.EventChancePercent;
            bool eventStarted;
            Pcg32State nextRandomState = eventRandomState;
            bool randomConsumed = false;
            if (chance <= 0)
            {
                eventStarted = false;
            }
            else if (chance >= 100)
            {
                eventStarted = true;
            }
            else
            {
                Pcg32RandomSource eventRandom;
                try
                {
                    eventRandom = Pcg32RandomSource.FromState(eventRandomState);
                }
                catch (System.ArgumentException)
                {
                    return Failure(state, StageProgressFailure.InvalidState);
                }
                eventStarted = eventRandom.NextInt(0, 100) < chance;
                nextRandomState = eventRandom.CaptureState();
                randomConsumed = true;
            }

            int nextChance = eventStarted
                ? 0
                : System.Math.Min(config.MaximumEventChancePercent,
                    chance + config.EventChanceIncreasePercent);
            StageProgressPhase phase = eventStarted
                ? StageProgressPhase.EventPending
                : StageProgressPhase.AwaitingEncounterStart;
            StageProgressState next = state.With(nextIndex, nextChance, phase, null, null, false);
            return Success(next, null, null, eventStarted, true, randomConsumed, nextRandomState);
        }

        /// <summary>Completes an event only after the caller confirms no usable target remains.</summary>
        public static StageProgressResult CompleteEvent(StageProgressState state,
            bool usableItemTargetExists)
        {
            if (state == null) return Failure(null, StageProgressFailure.InvalidState);
            if (state.Phase != StageProgressPhase.EventPending)
                return Failure(state, StageProgressFailure.NotAwaitingEvent);
            if (usableItemTargetExists)
                return Failure(state, StageProgressFailure.EventItemTargetStillAvailable);

            StageProgressState next = state.With(state.EncounterIndex, state.EventChancePercent,
                StageProgressPhase.AwaitingEncounterStart, null, null, false);
            return Success(next, null, null, false, false, false, default(Pcg32State));
        }

        private static StageProgressResult CreateRewardPending(StageProgressState state,
            StageProgressConfig config)
        {
            if (state.CurrentSelection == null) return Failure(state, StageProgressFailure.InvalidState);
            int stage = config.GetStageNumber(state.EncounterIndex);
            int round = config.GetRoundNumber(state.EncounterIndex);
            bool isFinal = state.EncounterIndex == config.TotalEncounterCount - 1;
            bool isBoss = round == config.BossRoundNumber;
            StageRewardPending reward = new StageRewardPending(state.EncounterIndex,
                stage, round, state.CurrentSelection.Choice, isBoss, isFinal);
            StageProgressState next = state.With(state.EncounterIndex, state.EventChancePercent,
                StageProgressPhase.RewardPending, state.CurrentSelection, reward);
            return Success(next, next.CurrentSelection, reward, false, false, false, default(Pcg32State));
        }

        private static StageProgressResult CreateGameOver(StageProgressState state)
        {
            StageProgressState next = state.With(state.EncounterIndex, state.EventChancePercent,
                StageProgressPhase.GameOver, state.CurrentSelection, null);
            return Success(next, next.CurrentSelection, null, false, false, false, default(Pcg32State));
        }

        private static StageProgressResult Success(StageProgressState next,
            StageSelectionOutcome selection, StageRewardPending reward,
            bool eventStarted, bool hasRng, bool rngConsumed, Pcg32State rng)
        {
            return new StageProgressResult(true, StageProgressFailure.None,
                next, selection, reward, eventStarted, hasRng, rngConsumed, rng);
        }

        private static StageProgressResult Failure(StageProgressState state,
            StageProgressFailure failure)
        {
            return new StageProgressResult(false, failure, state, null, null,
                false, false, false, default(Pcg32State));
        }
    }
}
