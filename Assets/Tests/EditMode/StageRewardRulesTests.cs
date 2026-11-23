using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class StageRewardRulesTests
    {
        private static StageProgressConfig StageConfig(int initialChance = 0,
            int chanceIncrease = 0, int maxChance = 0, int bossRound = 4)
        {
            return new StageProgressConfig(25, 5, 5, bossRound, initialChance,
                chanceIncrease, maxChance, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
        }

        private static EconomyRulesConfiguration EconomyConfig(int baseGold = 3,
            int handBonusGold = 0)
        {
            return new EconomyRulesConfiguration(4, 2, 1,
                new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
                10, 1, 3, baseGold, 1, handBonusGold);
        }

        private static StageRewardConfiguration Rewards(int bossGold = 5, int bossXP = 2)
        {
            return new StageRewardConfiguration(bossGold, bossXP);
        }

        private static EconomyState Economy(int gold, int xp = 0, int level = 1,
            int hp = 20, int maxHp = 30)
        {
            return new EconomyState(gold, xp, level, 12, gold, 0, 1, hp, maxHp);
        }

        [Test]
        public void SelectA_AppliesConfiguredBaseGoldAndExperienceAndReportsLevelChange()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageRewardResult result = ApplyReward(CreatePending(StageChoice.SelectA, stageConfig),
                stageConfig, Economy(0), EconomyConfig());
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.BaseGold, Is.EqualTo(3));
            Assert.That(result.TotalGoldAdded, Is.EqualTo(3));
            Assert.That(result.ExperienceAdded, Is.EqualTo(1));
            Assert.That(result.NextEconomyState.Gold, Is.EqualTo(3));
            Assert.That(result.NextEconomyState.Level, Is.EqualTo(2));
            Assert.That(result.NextEconomyState.XP, Is.EqualTo(0));
            Assert.That(result.IsBossReward, Is.False);
            Assert.That(result.IsFinalStageReward, Is.False);
        }

        [Test]
        public void SelectC_UsesItsGoldAndExperienceInsteadOfAddingNormalGold()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageRewardResult result = ApplyReward(CreatePending(StageChoice.SelectC, stageConfig),
                stageConfig, Economy(0), EconomyConfig(baseGold: 3));
            Assert.That(result.BaseGold, Is.EqualTo(6));
            Assert.That(result.NextEconomyState.Gold, Is.EqualTo(6));
            Assert.That(result.ExperienceAdded, Is.EqualTo(3));
            Assert.That(result.NextEconomyState.Level, Is.EqualTo(3));
            Assert.That(result.GoldBefore, Is.EqualTo(0));
            Assert.That(result.GoldAfter, Is.EqualTo(6));
        }

        [Test]
        public void SelectBAndD_GrantNoVictoryGoldOrExperience()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageRewardResult selectB = ApplyReward(CreatePending(StageChoice.SelectB, stageConfig),
                stageConfig, Economy(29), EconomyConfig());
            AssertNoReward(selectB, 29);

            StageProgressState selectDPreparation = ChoicePreparation(StageChoice.SelectD, stageConfig);
            EconomyState beforeHeal = Economy(29, hp: 25, maxHp: 30);
            StageRewardResult healed = StageRewardRules.ApplySelectionEffect(selectDPreparation, beforeHeal);
            StageProgressState selectDPending = StageProgressRules.ResolveNoEnemySelectionAsVictory(
                healed.NextStageState, stageConfig).NextState;
            StageRewardResult selectD = ApplyReward(selectDPending, stageConfig,
                healed.NextEconomyState, EconomyConfig());
            AssertNoReward(selectD, 29);
            Assert.That(selectD.NextEconomyState.CurrentHP, Is.EqualTo(30));
        }

        [Test]
        public void InterestUsesGoldBeforeRewardAndRespectsCapAcrossRepresentativeBalances()
        {
            int[] balances = { 0, 9, 10, 19, 20, 29, 30, 39 };
            int[] guaranteed = { 0, 0, 1, 1, 2, 2, 3, 3 };
            StageProgressConfig stageConfig = StageConfig();
            for (int i = 0; i < balances.Length; i++)
            {
                StageRewardResult result = ApplyReward(CreatePending(StageChoice.SelectA, stageConfig),
                    stageConfig, Economy(balances[i]), EconomyConfig());
                Assert.That(result.GuaranteedInterestGold, Is.EqualTo(guaranteed[i]),
                    "balance=" + balances[i]);
                Assert.That(result.RandomBonusInterestGold, Is.InRange(0, 1),
                    "balance=" + balances[i]);
                Assert.That(result.TotalGoldAdded, Is.EqualTo(3 + guaranteed[i] + result.RandomBonusInterestGold));
                Assert.That(result.EconomyRandomConsumed,
                    Is.EqualTo(balances[i] % 10 != 0 && guaranteed[i] < 3), "balance=" + balances[i]);
            }
        }

        [Test]
        public void EmptyHandBonusIsCalculatedByEconomyRulesAndZeroRewardSkipsIt()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageRewardResult rewarded = ApplyReward(CreatePending(StageChoice.SelectA, stageConfig),
                stageConfig, Economy(0), EconomyConfig(handBonusGold: 2), HandState.CreateEmpty(3));
            Assert.That(rewarded.EmptyHandSlotGold, Is.EqualTo(6));
            Assert.That(rewarded.TotalGoldAdded, Is.EqualTo(9));

            StageRewardResult noBaseReward = ApplyReward(CreatePending(StageChoice.SelectB, stageConfig),
                stageConfig, Economy(19), EconomyConfig(handBonusGold: 2), HandState.CreateEmpty(5));
            Assert.That(noBaseReward.EmptyHandSlotGold, Is.Zero);
            Assert.That(noBaseReward.GuaranteedInterestGold, Is.Zero);
        }

        [Test]
        public void BossRewardOverridesChoiceRewardAndUsesBossXP()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState pending = CreatePending(StageChoice.SelectA, stageConfig, encounterIndex: 3);
            StageRewardResult result = ApplyReward(pending, stageConfig, Economy(10), EconomyConfig(),
                bossRewards: Rewards(5, 2));
            Assert.That(result.IsBossReward, Is.True);
            Assert.That(result.BaseGold, Is.EqualTo(5));
            Assert.That(result.BaseGold, Is.Not.EqualTo(3));
            Assert.That(result.ExperienceAdded, Is.EqualTo(2));
            Assert.That(result.GuaranteedInterestGold, Is.EqualTo(1));
        }

        [Test]
        public void SelectDHealingIsCappedAndCanOnlyBeAppliedOnce()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState choice = ChoicePreparation(StageChoice.SelectD, stageConfig);
            EconomyState before = Economy(12, hp: 25, maxHp: 30);
            StageRewardResult healed = StageRewardRules.ApplySelectionEffect(choice, before);
            Assert.That(healed.Succeeded, Is.True);
            Assert.That(healed.IsSelectionEffect, Is.True);
            Assert.That(healed.SelectionEffectApplied, Is.True);
            Assert.That(healed.HPBefore, Is.EqualTo(25));
            Assert.That(healed.HPAfter, Is.EqualTo(30));
            Assert.That(healed.NextStageState.SelectionEffectApplied, Is.True);
            Assert.That(healed.NextEconomyState.Gold, Is.EqualTo(before.Gold));

            StageRewardResult repeated = StageRewardRules.ApplySelectionEffect(
                healed.NextStageState, healed.NextEconomyState);
            Assert.That(repeated.Succeeded, Is.False);
            Assert.That(repeated.Failure, Is.EqualTo(StageRewardFailure.SelectionEffectAlreadyApplied));
            Assert.That(repeated.NextEconomyState.CurrentHP, Is.EqualTo(30));
        }

        [Test]
        public void SelectDHealingRequiresHealthStateAndFailureLeavesBothInputsUntouched()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState choice = ChoicePreparation(StageChoice.SelectD, stageConfig);
            EconomyState noHealth = new EconomyState(5, 0, 1, 12, 5, 0, 1);
            StageRewardResult result = StageRewardRules.ApplySelectionEffect(choice, noHealth);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(StageRewardFailure.HealthUnavailable));
            Assert.That(result.NextStageState, Is.SameAs(choice));
            Assert.That(result.NextEconomyState, Is.SameAs(noHealth));
            Assert.That(choice.SelectionEffectApplied, Is.False);
        }

        [Test]
        public void SelectAHasNoSelectionTimeHealing()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState preparation = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(stageConfig), stageConfig).NextState;
            EconomyState before = Economy(5, hp: 12, maxHp: 30);
            StageRewardResult result = StageRewardRules.ApplySelectionEffect(preparation, before);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IsSelectionEffect, Is.False);
            Assert.That(result.SelectionEffectApplied, Is.False);
            Assert.That(result.NextStageState.SelectionEffectApplied, Is.False);
            Assert.That(result.NextEconomyState.CurrentHP, Is.EqualTo(12));
        }

        [Test]
        public void FinalVictoryAppliesRegularRewardThenCompletesProgression()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState pending = CreatePending(StageChoice.SelectA, stageConfig, encounterIndex: 24);
            Assert.That(pending.Phase, Is.EqualTo(StageProgressPhase.RewardPending));
            Assert.That(pending.PendingReward.IsFinalVictory, Is.True);
            StageRewardResult result = ApplyReward(pending, stageConfig, Economy(0), EconomyConfig());
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IsFinalStageReward, Is.True);
            Assert.That(result.BaseGold, Is.EqualTo(3));
            Assert.That(result.ExperienceAdded, Is.EqualTo(1));
            Assert.That(result.NextStageState.Phase, Is.EqualTo(StageProgressPhase.Completed));
            Assert.That(result.EventRandomConsumed, Is.False);
            Assert.That(StageRewardRules.ApplyVictoryReward(result.NextStageState, stageConfig,
                result.NextEconomyState, EconomyConfig(), Rewards(), HandState.CreateStandard(),
                result.NextEconomyRandomState, result.NextEventRandomState).Succeeded, Is.False);
        }

        [Test]
        public void FinalBossVictoryUsesBossRewardAndStillCompletesAfterReward()
        {
            StageProgressConfig finalBossConfig = StageConfig(bossRound: 5);
            StageProgressState pending = CreatePending(StageChoice.SelectC, finalBossConfig, encounterIndex: 24);
            StageRewardResult result = ApplyReward(pending, finalBossConfig, Economy(0), EconomyConfig(),
                bossRewards: Rewards(5, 2));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IsBossReward, Is.True);
            Assert.That(result.IsFinalStageReward, Is.True);
            Assert.That(result.BaseGold, Is.EqualTo(5));
            Assert.That(result.ExperienceAdded, Is.EqualTo(2));
            Assert.That(result.NextStageState.Phase, Is.EqualTo(StageProgressPhase.Completed));
        }

        [Test]
        public void WrongPhaseAndLossCannotReceiveRewards()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(stageConfig), stageConfig).NextState;
            StageRewardResult wrongPhase = ApplyReward(prep, stageConfig, Economy(10), EconomyConfig());
            Assert.That(wrongPhase.Succeeded, Is.False);
            Assert.That(wrongPhase.NextStageState, Is.SameAs(prep));

            StageProgressState gameOver = StageProgressRules.ResolveBattle(
                StageProgressRules.BeginBattle(prep).NextState, stageConfig, false).NextState;
            StageRewardResult loss = ApplyReward(gameOver, stageConfig, Economy(10), EconomyConfig());
            Assert.That(loss.Succeeded, Is.False);
            Assert.That(loss.NextStageState.Phase, Is.EqualTo(StageProgressPhase.GameOver));
        }

        [Test]
        public void DSelectionEffectMustBeAppliedBeforeVictoryReward()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState preparation = ChoicePreparation(StageChoice.SelectD, stageConfig);
            StageProgressState pending = StageProgressRules.ResolveNoEnemySelectionAsVictory(
                preparation, stageConfig).NextState;
            StageRewardResult result = ApplyReward(pending, stageConfig, Economy(0), EconomyConfig());
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(StageRewardFailure.SelectionEffectNotApplied));
        }

        [Test]
        public void EconomyAndEventRandomStreamsRemainIndependentAndRepeatable()
        {
            StageProgressConfig stageConfig = StageConfig(50, 10, 100);
            StageProgressState pending = CreatePending(StageChoice.SelectA, stageConfig);
            SimulationRandomStreams streams = new SimulationRandomStreams(87231UL);
            SimulationRandomStreamsState checkpoint = streams.CaptureState();
            StageRewardResult first = StageRewardRules.ApplyVictoryReward(pending, stageConfig,
                Economy(19), EconomyConfig(), Rewards(), HandState.CreateStandard(),
                checkpoint.Economy, checkpoint.Event);
            StageRewardResult second = StageRewardRules.ApplyVictoryReward(pending, stageConfig,
                Economy(19), EconomyConfig(), Rewards(), HandState.CreateStandard(),
                checkpoint.Economy, checkpoint.Event);
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.TotalGoldAdded, Is.EqualTo(second.TotalGoldAdded));
            Assert.That(first.NextEconomyState.XP, Is.EqualTo(second.NextEconomyState.XP));
            Assert.That(first.EventStarted, Is.EqualTo(second.EventStarted));
            AssertStateEqual(first.NextEconomyRandomState, second.NextEconomyRandomState);
            AssertStateEqual(first.NextEventRandomState, second.NextEventRandomState);

            SimulationRandomStreamsState after = streams.CaptureState();
            AssertStateEqual(checkpoint.Economy, after.Economy);
            AssertStateEqual(checkpoint.Event, after.Event);
            AssertStateEqual(checkpoint.Shop, after.Shop);
            AssertStateEqual(checkpoint.Stats, after.Stats);
            AssertStateEqual(checkpoint.Encounter, after.Encounter);
            AssertStateEqual(checkpoint.Battle, after.Battle);
            AssertStateEqual(checkpoint.Roster, after.Roster);
        }

        [Test]
        public void FailedEventTransitionAfterTemporaryCalculationsReturnsOriginalStatesAndCheckpoints()
        {
            StageProgressConfig stageConfig = StageConfig(50, 10, 100);
            StageProgressState pending = CreatePending(StageChoice.SelectA, stageConfig);
            EconomyState economy = Economy(19);
            Pcg32State economyRng = new Pcg32RandomSource(41, 3).CaptureState();
            Pcg32State invalidEventRng = default(Pcg32State);
            StageRewardResult result = StageRewardRules.ApplyVictoryReward(pending, stageConfig,
                economy, EconomyConfig(), Rewards(), HandState.CreateStandard(),
                economyRng, invalidEventRng);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(StageRewardFailure.ProgressionRuleFailed));
            Assert.That(result.NextStageState, Is.SameAs(pending));
            Assert.That(result.NextEconomyState, Is.SameAs(economy));
            Assert.That(result.HasNextEconomyRandomState, Is.False);
            Assert.That(result.HasNextEventRandomState, Is.False);
        }

        [Test]
        public void EconomyOverflowFailsAtomicallyWithoutAdvancingRandomness()
        {
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState pending = CreatePending(StageChoice.SelectA, stageConfig);
            EconomyState economy = Economy(int.MaxValue);
            Pcg32State economyRng = new Pcg32RandomSource(9, 7).CaptureState();
            Pcg32State eventRng = new Pcg32RandomSource(8, 5).CaptureState();
            StageRewardResult result = StageRewardRules.ApplyVictoryReward(pending, stageConfig,
                economy, EconomyConfig(), Rewards(), HandState.CreateStandard(), economyRng, eventRng);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.EconomyFailure, Is.EqualTo(EconomyActionFailureReason.GoldOverflow));
            Assert.That(result.NextStageState, Is.SameAs(pending));
            Assert.That(result.NextEconomyState, Is.SameAs(economy));
            Assert.That(result.HasNextEconomyRandomState, Is.False);
        }

        [Test]
        public void LegacyEconomyStateAndEconomyRulesPreserveExistingApiAndNoHealthFlag()
        {
            EconomyState legacy = new EconomyState(8, 2, 2, 12, 8, 0, 1);
            Assert.That(legacy.HasHealth, Is.False);
            Assert.That(EconomyRules.TryAddGold(legacy, 3).NextState.HasHealth, Is.False);
            Assert.That(EconomyRules.TryAddExperience(legacy, EconomyConfig(), 1).NextState.HasHealth, Is.False);
            Assert.That(EconomyRules.TryApplyHealing(legacy, 5).FailureReason,
                Is.EqualTo(EconomyActionFailureReason.HealthUnavailable));
        }

        private static StageRewardResult ApplyReward(StageProgressState pending,
            StageProgressConfig stageConfig, EconomyState economy, EconomyRulesConfiguration economyConfig,
            HandState hand = null, StageRewardConfiguration bossRewards = null)
        {
            return StageRewardRules.ApplyVictoryReward(pending, stageConfig, economy,
                economyConfig, bossRewards ?? Rewards(), hand ?? HandState.CreateStandard(),
                new Pcg32RandomSource(124, 17).CaptureState(),
                new Pcg32RandomSource(871, 5).CaptureState());
        }

        private static StageProgressState CreatePending(StageChoice choice,
            StageProgressConfig config, int encounterIndex = 4)
        {
            StageProgressState state = ProgressTo(encounterIndex, config);
            StageProgressResult begin = StageProgressRules.BeginCurrentEncounter(state, config);
            state = begin.NextState;
            if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                state = StageSelectionRules.Select(state, config, choice).NextState;
            if (state.CurrentSelection.EnemyCategory == StageEnemyCategory.None)
            {
                return StageProgressRules.ResolveNoEnemySelectionAsVictory(state, config).NextState;
            }
            StageProgressState battle = StageProgressRules.BeginBattle(state).NextState;
            return StageProgressRules.ResolveBattle(battle, config, true).NextState;
        }

        private static StageProgressState ChoicePreparation(StageChoice choice, StageProgressConfig config)
        {
            StageProgressState state = ProgressTo(4, config);
            state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            return StageSelectionRules.Select(state, config, choice).NextState;
        }

        private static StageProgressState ProgressTo(int targetEncounterIndex, StageProgressConfig config)
        {
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            for (int i = 0; i < targetEncounterIndex; i++)
            {
                state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
                if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                    state = StageSelectionRules.Select(state, config, StageChoice.SelectA).NextState;
                state = StageProgressRules.BeginBattle(state).NextState;
                state = StageProgressRules.ResolveBattle(state, config, true).NextState;
                state = StageProgressRules.CompletePendingReward(state, config,
                    new Pcg32RandomSource((ulong)(i + 1), 2).CaptureState()).NextState;
                if (state.Phase == StageProgressPhase.EventPending)
                    state = StageProgressRules.CompleteEvent(state, false).NextState;
            }
            return state;
        }

        private static void AssertStateEqual(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        private static void AssertNoReward(StageRewardResult result, int gold)
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.BaseGold, Is.Zero);
            Assert.That(result.GuaranteedInterestGold, Is.Zero);
            Assert.That(result.RandomBonusInterestGold, Is.Zero);
            Assert.That(result.EmptyHandSlotGold, Is.Zero);
            Assert.That(result.ExperienceAdded, Is.Zero);
            Assert.That(result.NextEconomyState.Gold, Is.EqualTo(gold));
            Assert.That(result.EconomyRandomConsumed, Is.False);
        }
    }
}
