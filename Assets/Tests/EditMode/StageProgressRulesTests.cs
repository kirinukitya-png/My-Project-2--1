using NUnit.Framework;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class StageProgressRulesTests
    {
        private static StageProgressConfig Config(int initialChance = 0, int chanceIncrease = 0,
            int maximumChance = 0)
        {
            return new StageProgressConfig(25, 5, 5, 4, initialChance,
                chanceIncrease, maximumChance, 1, 0, 0, 3, 6, 0, 0, 10,
                4, 2, true);
        }

        [Test]
        public void InitialState_UsesSuppliedConfigurationAndStartsAtFirstEncounter()
        {
            StageProgressConfig config = Config(10, 10, 100);
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            Assert.That(state.EncounterIndex, Is.EqualTo(0));
            Assert.That(config.GetStageNumber(state.EncounterIndex), Is.EqualTo(1));
            Assert.That(config.GetRoundNumber(state.EncounterIndex), Is.EqualTo(1));
            Assert.That(state.EventChancePercent, Is.EqualTo(10));
            Assert.That(state.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(config.TotalStageCount, Is.EqualTo(5));
        }

        [Test]
        public void Config_RejectsInvalidCountsAndChanceValues()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new StageProgressConfig(
                25, 0, 5, 4, 10, 10, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Config(101, 0, 100));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new StageProgressConfig(
                25, 5, 5, 4, 10, 10, 100, -1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true));
        }

        [Test]
        public void RoundsOneToFour_AutomaticallySelectA_AndRoundFourIsBoss()
        {
            StageProgressConfig config = Config();
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            StageProgressResult result = StageProgressRules.BeginCurrentEncounter(state, config);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(result.Selection.Choice, Is.EqualTo(StageChoice.SelectA));
            Assert.That(result.Selection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Normal));
            Assert.That(result.Selection.ExperienceReward, Is.EqualTo(1));

            state = ProgressToEncounter(3, config);
            result = StageProgressRules.BeginCurrentEncounter(state, config);
            Assert.That(result.Selection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Boss));
        }

        [Test]
        public void RoundFive_WaitsForChoiceAndCannotStartBattleYet()
        {
            StageProgressConfig config = Config();
            StageProgressState state = ProgressToEncounter(4, config);
            StageProgressResult result = StageProgressRules.BeginCurrentEncounter(state, config);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            Assert.That(result.Selection, Is.Null);
            StageProgressResult invalid = StageProgressRules.BeginBattle(result.NextState);
            Assert.That(invalid.Succeeded, Is.False);
            Assert.That(invalid.NextState, Is.SameAs(result.NextState));
        }

        [Test]
        public void ChoiceA_ReturnsNormalCharacterShopAndExperienceMetadata()
        {
            StageProgressResult result = SelectAtRoundFive(StageChoice.SelectA, Config());
            Assert.That(result.Selection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Normal));
            Assert.That(result.Selection.ShopMode, Is.EqualTo(StageShopMode.Character));
            Assert.That(result.Selection.ExperienceReward, Is.EqualTo(1));
            Assert.That(result.Selection.GoldReward, Is.EqualTo(0));
            Assert.That(result.Selection.SelectionEffect.Timing, Is.EqualTo(StageSelectionEffectTiming.None));
        }

        [Test]
        public void ChoiceB_ReturnsNoEnemyItemShopDiscountAndRerollMetadata()
        {
            StageProgressResult result = SelectAtRoundFive(StageChoice.SelectB, Config());
            Assert.That(result.Selection.EnemyCategory, Is.EqualTo(StageEnemyCategory.None));
            Assert.That(result.Selection.ShopMode, Is.EqualTo(StageShopMode.Item));
            Assert.That(result.Selection.ExperiencePurchaseGoldCost, Is.EqualTo(2));
            Assert.That(result.Selection.CanReroll, Is.True);
        }

        [Test]
        public void ChoiceC_ReturnsHardCharacterShopAndRewardMetadata()
        {
            StageProgressResult result = SelectAtRoundFive(StageChoice.SelectC, Config());
            Assert.That(result.Selection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Hard));
            Assert.That(result.Selection.ShopMode, Is.EqualTo(StageShopMode.Character));
            Assert.That(result.Selection.GoldReward, Is.EqualTo(6));
            Assert.That(result.Selection.ExperienceReward, Is.EqualTo(3));
        }

        [Test]
        public void ChoiceD_ReturnsNoEnemyAndSelectionTimeHealMetadata()
        {
            StageProgressResult result = SelectAtRoundFive(StageChoice.SelectD, Config());
            Assert.That(result.Selection.EnemyCategory, Is.EqualTo(StageEnemyCategory.None));
            Assert.That(result.Selection.ShopMode, Is.EqualTo(StageShopMode.Character));
            Assert.That(result.Selection.SelectionEffect.Kind, Is.EqualTo(StageSelectionEffectKind.HealPlayer));
            Assert.That(result.Selection.SelectionEffect.Timing, Is.EqualTo(StageSelectionEffectTiming.AtSelection));
            Assert.That(result.Selection.SelectionEffect.Amount, Is.EqualTo(10));
            Assert.That(result.Selection.SelectionEffect.CapAtInitialMaximum, Is.True);
        }

        [Test]
        public void InvalidSelectionOrderAndUndefinedChoice_LeaveStateUntouched()
        {
            StageProgressConfig config = Config();
            StageProgressState initial = StageProgressRules.CreateInitialState(config);
            StageProgressResult wrongPhase = StageSelectionRules.Select(initial, config, StageChoice.SelectB);
            Assert.That(wrongPhase.Succeeded, Is.False);
            Assert.That(wrongPhase.NextState, Is.SameAs(initial));

            StageProgressState pending = StageProgressRules.BeginCurrentEncounter(
                ProgressToEncounter(4, config), config).NextState;
            StageProgressResult invalidChoice = StageSelectionRules.Select(pending, config, (StageChoice)99);
            Assert.That(invalidChoice.Succeeded, Is.False);
            Assert.That(invalidChoice.NextState, Is.SameAs(pending));
        }

        [Test]
        public void NoEnemySelection_TransitionsToRewardPendingWithoutBattle()
        {
            StageProgressResult selection = SelectAtRoundFive(StageChoice.SelectB, Config());
            StageProgressResult result = StageProgressRules.ResolveNoEnemySelectionAsVictory(
                selection.NextState, Config());
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Phase, Is.EqualTo(StageProgressPhase.RewardPending));
            Assert.That(result.Reward.Choice, Is.EqualTo(StageChoice.SelectB));
            Assert.That(result.Reward.IsBossReward, Is.False);
            Assert.That(StageProgressRules.BeginBattle(selection.NextState).Failure,
                Is.EqualTo(StageProgressFailure.NoEnemyEncounterRequired));
        }

        [Test]
        public void NoEnemyVictoryRoute_RejectsEncounterWithEnemies()
        {
            StageProgressState state = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(Config()), Config()).NextState;
            StageProgressResult result = StageProgressRules.ResolveNoEnemySelectionAsVictory(state, Config());
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [Test]
        public void Win_CreatesRewardPendingAndCarriesBossAndFinalFlags()
        {
            StageProgressConfig config = Config();
            StageProgressState boss = ProgressToEncounter(3, config);
            StageProgressState bossBattle = StageProgressRules.BeginBattle(
                StageProgressRules.BeginCurrentEncounter(boss, config).NextState).NextState;
            StageProgressResult result = StageProgressRules.ResolveBattle(bossBattle, config, true);
            Assert.That(result.NextState.Phase, Is.EqualTo(StageProgressPhase.RewardPending));
            Assert.That(result.Reward.IsBossReward, Is.True);
            Assert.That(result.Reward.IsFinalVictory, Is.False);

            StageProgressState final = ProgressToEncounter(24, config);
            StageProgressState finalChoice = StageProgressRules.BeginCurrentEncounter(final, config).NextState;
            StageProgressState finalPreparation = StageSelectionRules.Select(
                finalChoice, config, StageChoice.SelectA).NextState;
            StageProgressState finalBattle = StageProgressRules.BeginBattle(finalPreparation).NextState;
            StageProgressResult finalResult = StageProgressRules.ResolveBattle(finalBattle, config, true);
            Assert.That(finalResult.Reward.IsFinalVictory, Is.True);
            Assert.That(finalResult.NextState.Phase, Is.EqualTo(StageProgressPhase.RewardPending));
        }

        [Test]
        public void Loss_TransitionsToGameOverWithoutReward()
        {
            StageProgressConfig config = Config();
            StageProgressState preparation = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            StageProgressState battle = StageProgressRules.BeginBattle(preparation).NextState;
            StageProgressResult result = StageProgressRules.ResolveBattle(battle, config, false);
            Assert.That(result.NextState.Phase, Is.EqualTo(StageProgressPhase.GameOver));
            Assert.That(result.Reward, Is.Null);
        }

        [Test]
        public void VictoryCannotBeResolvedTwiceOrAdvancedBeforeRewardApplication()
        {
            StageProgressConfig config = Config();
            StageProgressState preparation = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            StageProgressState battle = StageProgressRules.BeginBattle(preparation).NextState;
            StageProgressState pending = StageProgressRules.ResolveBattle(battle, config, true).NextState;
            Assert.That(StageProgressRules.ResolveBattle(pending, config, true).Succeeded, Is.False);
            Assert.That(StageProgressRules.BeginCurrentEncounter(pending, config).Succeeded, Is.False);
            Assert.That(StageProgressRules.CompletePendingReward(battle, config,
                new Pcg32RandomSource(1, 2).CaptureState()).Succeeded, Is.False);
        }

        [Test]
        public void EventAtHundredPercentStartsDeterministicallyAndResetsChance()
        {
            StageProgressConfig config = Config(100, 10, 100);
            StageProgressState pendingReward = WinCurrentEncounter(config);
            Pcg32State rng = new Pcg32RandomSource(51, 7).CaptureState();
            StageProgressResult result = StageProgressRules.CompletePendingReward(pendingReward, config, rng);
            Assert.That(result.EventStarted, Is.True);
            Assert.That(result.NextState.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            Assert.That(result.NextState.EventChancePercent, Is.EqualTo(0));
            Assert.That(result.EventRandomConsumed, Is.False);
            AssertStateEqual(rng, result.NextEventRandomState);
        }

        [Test]
        public void ZeroPercentEventRollDoesNotConsumeRngAndRaisesChance()
        {
            StageProgressConfig config = Config(0, 10, 100);
            StageProgressState pendingReward = WinCurrentEncounter(config);
            Pcg32State rng = new Pcg32RandomSource(51, 7).CaptureState();
            StageProgressResult result = StageProgressRules.CompletePendingReward(pendingReward, config, rng);
            Assert.That(result.EventStarted, Is.False);
            Assert.That(result.NextState.EventChancePercent, Is.EqualTo(10));
            Assert.That(result.EventRandomConsumed, Is.False);
            AssertStateEqual(rng, result.NextEventRandomState);
        }

        [Test]
        public void EventTargetBlocksExitButNoTargetAllowsOneTimeResume()
        {
            StageProgressConfig config = Config(100, 10, 100);
            StageProgressResult rolled = StageProgressRules.CompletePendingReward(
                WinCurrentEncounter(config), config, new Pcg32RandomSource(9, 11).CaptureState());
            StageProgressState eventState = rolled.NextState;
            StageProgressResult blocked = StageProgressRules.CompleteEvent(eventState, true);
            Assert.That(blocked.Succeeded, Is.False);
            Assert.That(blocked.NextState, Is.SameAs(eventState));
            StageProgressResult resumed = StageProgressRules.CompleteEvent(eventState, false);
            Assert.That(resumed.NextState.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(resumed.NextState.EncounterIndex, Is.EqualTo(eventState.EncounterIndex));
            Assert.That(StageProgressRules.CompleteEvent(resumed.NextState, false).Succeeded, Is.False);
        }

        [Test]
        public void RandomEventRoll_IsDeterministicAndOnlyConsumesEventCheckpoint()
        {
            StageProgressConfig config = Config(37, 10, 100);
            StageProgressState pending = WinCurrentEncounter(config);
            SimulationRandomStreams streams = new SimulationRandomStreams(123456UL);
            SimulationRandomStreamsState before = streams.CaptureState();
            StageProgressResult first = StageProgressRules.CompletePendingReward(
                pending, config, before.Event);
            StageProgressResult second = StageProgressRules.CompletePendingReward(
                pending, config, before.Event);
            Assert.That(first.EventStarted, Is.EqualTo(second.EventStarted));
            AssertStateEqual(first.NextEventRandomState, second.NextEventRandomState);
            Assert.That(first.EventRandomConsumed, Is.True);
            SimulationRandomStreamsState after = streams.CaptureState();
            AssertStateEqual(before.Shop, after.Shop);
            AssertStateEqual(before.Stats, after.Stats);
            AssertStateEqual(before.Encounter, after.Encounter);
            AssertStateEqual(before.Battle, after.Battle);
            AssertStateEqual(before.Event, after.Event);
            AssertStateEqual(before.Roster, after.Roster);
            AssertStateEqual(before.Economy, after.Economy);
        }

        [Test]
        public void InvalidEventCheckpoint_FailsWithoutChangingProgression()
        {
            StageProgressConfig config = Config(50, 10, 100);
            StageProgressState pending = WinCurrentEncounter(config);
            Pcg32State invalid = default(Pcg32State);
            StageProgressResult result = StageProgressRules.CompletePendingReward(pending, config, invalid);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.NextState, Is.SameAs(pending));
            Assert.That(result.HasNextEventRandomState, Is.False);
        }

        [Test]
        public void EventChanceIncreasesAfterMissAndCapsAtMaximum()
        {
            StageProgressConfig config = Config(99, 10, 100);
            StageProgressState pending = WinCurrentEncounter(config);
            Pcg32State missState = FindEventRollState(99);
            StageProgressResult result = StageProgressRules.CompletePendingReward(pending, config, missState);
            Assert.That(result.EventStarted, Is.False);
            Assert.That(result.NextState.EventChancePercent, Is.EqualTo(100));
        }

        [Test]
        public void FinalRewardApplication_CompletesWithoutEventRollOrFurtherProgress()
        {
            StageProgressConfig config = Config(100, 10, 100);
            StageProgressState finalPending = WinFinalEncounter(config);
            Pcg32State rng = new Pcg32RandomSource(4, 8).CaptureState();
            StageProgressResult completed = StageProgressRules.CompletePendingReward(finalPending, config, rng);
            Assert.That(completed.NextState.Phase, Is.EqualTo(StageProgressPhase.Completed));
            Assert.That(completed.EventStarted, Is.False);
            Assert.That(completed.EventRandomConsumed, Is.False);
            AssertStateEqual(rng, completed.NextEventRandomState);
            Assert.That(StageProgressRules.CompletePendingReward(completed.NextState, config, rng).Succeeded, Is.False);
            Assert.That(StageProgressRules.BeginCurrentEncounter(completed.NextState, config).Succeeded, Is.False);
        }

        [Test]
        public void FullRun_CompletesAllTwentyFiveEncounterIndices()
        {
            StageProgressConfig config = Config();
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            int rewards = 0;
            while (state.Phase != StageProgressPhase.Completed)
            {
                StageProgressResult begin = StageProgressRules.BeginCurrentEncounter(state, config);
                Assert.That(begin.Succeeded, Is.True);
                state = begin.NextState;
                if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                    state = StageSelectionRules.Select(state, config, StageChoice.SelectA).NextState;
                state = StageProgressRules.BeginBattle(state).NextState;
                StageProgressResult victory = StageProgressRules.ResolveBattle(state, config, true);
                Assert.That(victory.Succeeded, Is.True);
                state = victory.NextState;
                rewards++;
                StageProgressResult applied = StageProgressRules.CompletePendingReward(
                    state, config, new Pcg32RandomSource(77, 2).CaptureState());
                Assert.That(applied.Succeeded, Is.True);
                state = applied.NextState;
                if (state.Phase == StageProgressPhase.EventPending)
                    state = StageProgressRules.CompleteEvent(state, false).NextState;
            }
            Assert.That(rewards, Is.EqualTo(25));
            Assert.That(state.EncounterIndex, Is.EqualTo(24));
        }

        private static StageProgressResult SelectAtRoundFive(StageChoice choice, StageProgressConfig config)
        {
            StageProgressState atRoundFive = ProgressToEncounter(4, config);
            StageProgressState awaitingChoice = StageProgressRules.BeginCurrentEncounter(atRoundFive, config).NextState;
            return StageSelectionRules.Select(awaitingChoice, config, choice);
        }

        private static StageProgressState ProgressToEncounter(int targetIndex, StageProgressConfig config)
        {
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            for (int index = 0; index < targetIndex; index++)
            {
                StageProgressResult begin = StageProgressRules.BeginCurrentEncounter(state, config);
                state = begin.NextState;
                if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                    state = StageSelectionRules.Select(state, config, StageChoice.SelectA).NextState;
                state = StageProgressRules.BeginBattle(state).NextState;
                StageProgressState pending = StageProgressRules.ResolveBattle(state, config, true).NextState;
                state = StageProgressRules.CompletePendingReward(pending, config,
                    new Pcg32RandomSource((ulong)(index + 1), 3).CaptureState()).NextState;
                if (state.Phase == StageProgressPhase.EventPending)
                    state = StageProgressRules.CompleteEvent(state, false).NextState;
            }
            return state;
        }

        private static StageProgressState WinCurrentEncounter(StageProgressConfig config)
        {
            StageProgressState preparation = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            StageProgressState battle = StageProgressRules.BeginBattle(preparation).NextState;
            return StageProgressRules.ResolveBattle(battle, config, true).NextState;
        }

        private static StageProgressState WinFinalEncounter(StageProgressConfig config)
        {
            StageProgressState state = ProgressToEncounter(config.TotalEncounterCount - 1, config);
            state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            state = StageSelectionRules.Select(state, config, StageChoice.SelectA).NextState;
            state = StageProgressRules.BeginBattle(state).NextState;
            return StageProgressRules.ResolveBattle(state, config, true).NextState;
        }

        private static void AssertStateEqual(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        private static Pcg32State FindEventRollState(int expectedRoll)
        {
            for (ulong seed = 0; seed < 10000; seed++)
            {
                Pcg32RandomSource candidate = new Pcg32RandomSource(seed, 2);
                Pcg32State state = candidate.CaptureState();
                if (candidate.NextInt(0, 100) == expectedRoll) return state;
            }
            Assert.Fail("Could not find a deterministic PCG state for the requested event roll.");
            return default(Pcg32State);
        }
    }
}
