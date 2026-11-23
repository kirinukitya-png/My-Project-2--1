using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    /// <summary>Cross-rule Stage 7 scenarios. State transitions are explicitly adopted by the test.</summary>
    public sealed class Stage7IntegrationScenarioTests
    {
        [Test]
        public void NormalSelectA_BattleVictoryRewardAndProgressionAreAppliedOnce()
        {
            StageProgressConfig stageConfig = StageConfig();
            EconomyRulesConfiguration economyConfig = EconomyConfig();
            SimulationRandomStreams streams = new SimulationRandomStreams(1701UL);
            StageProgressState stage = ChoiceState(stageConfig, 4, StageChoice.SelectA);
            Assert.That(stage.CurrentSelection.ExperienceReward, Is.EqualTo(1));
            StageProgressResult battle = StageProgressRules.BeginBattle(stage);
            Assert.That(battle.Succeeded, Is.True);
            stage = StageProgressRules.ResolveBattle(battle.NextState, stageConfig, true).NextState;
            Assert.That(stage.Phase, Is.EqualTo(StageProgressPhase.RewardPending));

            SimulationRandomStreamsState before = streams.CaptureState();
            StageRewardResult reward = StageRewardRules.ApplyVictoryReward(stage, stageConfig,
                Economy(19), economyConfig, Rewards(), HandState.CreateStandard(),
                before.Economy, before.Event);
            Assert.That(reward.Succeeded, Is.True);
            Assert.That(reward.BaseGold, Is.EqualTo(3));
            Assert.That(reward.ExperienceAdded, Is.EqualTo(1));
            Assert.That(reward.GuaranteedInterestGold, Is.EqualTo(1));
            Assert.That(reward.TotalGoldAdded, Is.InRange(4, 5));
            Assert.That(reward.NextEconomyState.Gold, Is.EqualTo(19 + reward.TotalGoldAdded));
            Assert.That(reward.NextEconomyState.Level, Is.EqualTo(2));
            Assert.That(reward.NextStageState.EncounterIndex, Is.EqualTo(5));
            Assert.That(reward.NextStageState.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));

            StageRewardResult duplicate = StageRewardRules.ApplyVictoryReward(reward.NextStageState,
                stageConfig, reward.NextEconomyState, economyConfig, Rewards(), HandState.CreateStandard(),
                reward.NextEconomyRandomState, reward.NextEventRandomState);
            Assert.That(duplicate.Succeeded, Is.False);
            Assert.That(duplicate.NextEconomyState, Is.SameAs(reward.NextEconomyState));
            Assert.That(duplicate.NextStageState, Is.SameAs(reward.NextStageState));
        }

        [Test]
        public void SelectB_ItemShopPurchaseUseAndNoEnemyRewardComposeWithoutTouchingCharacterPool()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState stage = ChoiceState(config, 4, StageChoice.SelectB);
            Assert.That(stage.CurrentSelection.ShopMode, Is.EqualTo(StageShopMode.Item));
            Assert.That(stage.CurrentSelection.CanReroll, Is.True);

            CharacterPool pool = new CharacterPool(new[] { new CharacterPoolEntry("D1", 10, 10) });
            ItemDefinition[] items =
            {
                Item("Item001", 3, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                    new ItemEffectDefinition(ItemEffectKind.Attack, 3)),
                Item("Item003", 4, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                    new ItemEffectDefinition(ItemEffectKind.Attack, 2),
                    new ItemEffectDefinition(ItemEffectKind.Defense, 2)),
                Item("Item007", 2, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate,
                    new ItemEffectDefinition(ItemEffectKind.Attack, 1))
            };
            Pcg32RandomSource shopRandom = new Pcg32RandomSource(712, 17);
            ItemShopGenerationResult generated = ItemShopRules.Generate(null, pool, items,
                1, 5, 3, 0, shopRandom);
            Assert.That(generated.NextShop.Offers.Select(x => x.DefinitionId).Distinct().Count(),
                Is.EqualTo(generated.NextShop.Offers.Count));
            Assert.That(generated.NextCharacterPool, Is.SameAs(pool));

            ItemShopGenerationResult rerolled = ItemShopRules.Generate(generated.NextShop,
                generated.NextCharacterPool, items, 1, 5, 3, 1,
                Pcg32RandomSource.FromState(generated.NextShopRandomState));
            Assert.That(rerolled.NextShop.Offers.Select(x => x.DefinitionId).Distinct().Count(),
                Is.EqualTo(rerolled.NextShop.Offers.Count));
            Assert.That(rerolled.NextCharacterPool, Is.SameAs(pool));

            World world = MakeWorld(stage, Economy(10));
            string offerId = rerolled.NextShop.Offers[0].OfferId;
            ItemHandPurchaseResult purchase = ItemHandTransactionRules.TryPurchaseIntoHand(
                rerolled.NextShop, offerId, items, world.Economy, world.Hand, 0);
            Assert.That(purchase.Succeeded, Is.True);
            Assert.That(purchase.NextEconomy.Gold, Is.EqualTo(10 - rerolled.NextShop.Offers[0].DisplayPrice));
            ShopOffer purchasedOffer = purchase.NextShop.Offers.Single(x => x.OfferId == offerId);
            Assert.That(purchasedOffer.Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(pool, Is.SameAs(generated.NextCharacterPool));

            ItemDefinition boughtItem = items.Single(x => x.DefinitionId == purchase.PurchasedItemDefinitionId);
            ItemEffectResult used = ItemEffectRules.TryUseHandItem(boughtItem, 0,
                ItemUseTarget.BoardCharacter(0), world.Board, purchase.NextHand, world.Ownership,
                purchase.NextEconomy, stage, new ItemEffectState(), new[] { world.Definition },
                world.Settings, world.StatsRandomState);
            Assert.That(used.Succeeded, Is.True);
            Assert.That(used.NextHand.Slots[0].IsEmpty, Is.True);
            Assert.That(used.ConsumedItemDefinitionId, Is.EqualTo(boughtItem.DefinitionId));

            StageProgressResult noEnemyVictory = StageProgressRules.ResolveNoEnemySelectionAsVictory(
                used.NextStage, config);
            Assert.That(noEnemyVictory.Succeeded, Is.True);
            SimulationRandomStreams streams = new SimulationRandomStreams(44UL);
            SimulationRandomStreamsState checkpoint = streams.CaptureState();
            StageRewardResult reward = StageRewardRules.ApplyVictoryReward(noEnemyVictory.NextState,
                config, used.NextEconomy, EconomyConfig(), Rewards(), used.NextHand,
                checkpoint.Economy, checkpoint.Event);
            Assert.That(reward.Succeeded, Is.True);
            Assert.That(reward.BaseGold, Is.Zero);
            Assert.That(reward.TotalGoldAdded, Is.Zero);
            Assert.That(reward.ExperienceAdded, Is.Zero);
            Assert.That(reward.EconomyRandomConsumed, Is.False);
            Assert.That(pool.Entries["D1"].RemainingCopies, Is.EqualTo(10));
            Assert.That(pool.Entries["D1"].OwnedCopies, Is.Zero);
        }

        [Test]
        public void SelectCRewardAndBossRewardUseDistinctConfiguredAmountsAndPreRewardInterest()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState selectC = ChoiceState(config, 4, StageChoice.SelectC);
            StageProgressState cPending = StageProgressRules.ResolveBattle(
                StageProgressRules.BeginBattle(selectC).NextState, config, true).NextState;
            SimulationRandomStreams streams = new SimulationRandomStreams(818UL);
            SimulationRandomStreamsState checkpoint = streams.CaptureState();
            StageRewardResult cReward = StageRewardRules.ApplyVictoryReward(cPending, config,
                Economy(10), EconomyConfig(), Rewards(), HandState.CreateStandard(),
                checkpoint.Economy, checkpoint.Event);
            Assert.That(cReward.Succeeded, Is.True);
            Assert.That(cReward.BaseGold, Is.EqualTo(6));
            Assert.That(cReward.ExperienceAdded, Is.EqualTo(3));
            Assert.That(cReward.GuaranteedInterestGold, Is.EqualTo(1));
            Assert.That(cReward.GoldBefore, Is.EqualTo(10));

            StageProgressState bossPreparation = BeginAt(config, 3); // round 4 is the configured boss
            StageProgressState bossPending = StageProgressRules.ResolveBattle(
                StageProgressRules.BeginBattle(bossPreparation).NextState, config, true).NextState;
            Assert.That(bossPending.PendingReward.IsBossReward, Is.True);
            StageRewardResult boss = StageRewardRules.ApplyVictoryReward(bossPending, config,
                Economy(10), EconomyConfig(), Rewards(5, 2), HandState.CreateStandard(),
                checkpoint.Economy, checkpoint.Event);
            Assert.That(boss.Succeeded, Is.True);
            Assert.That(boss.BaseGold, Is.EqualTo(5));
            Assert.That(boss.ExperienceAdded, Is.EqualTo(2));
            Assert.That(boss.TotalGoldAdded, Is.InRange(6, 7));
            Assert.That(boss.BaseGold, Is.Not.EqualTo(6));
        }

        [Test]
        public void SelectDHealingIsAppliedOnceBeforeZeroRewardAndNeverExceedsMaximumHP()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState selected = ChoiceState(config, 4, StageChoice.SelectD);
            EconomyState damaged = Economy(25, hp: 25, maxHp: 30);
            StageRewardResult healing = StageRewardRules.ApplySelectionEffect(selected, damaged);
            Assert.That(healing.Succeeded, Is.True);
            Assert.That(healing.NextEconomyState.CurrentHP, Is.EqualTo(30));
            StageRewardResult twice = StageRewardRules.ApplySelectionEffect(
                healing.NextStageState, healing.NextEconomyState);
            Assert.That(twice.Succeeded, Is.False);
            Assert.That(twice.NextEconomyState.CurrentHP, Is.EqualTo(30));

            StageProgressState pending = StageProgressRules.ResolveNoEnemySelectionAsVictory(
                healing.NextStageState, config).NextState;
            Pcg32State rng = new Pcg32RandomSource(35, 7).CaptureState();
            StageRewardResult reward = StageRewardRules.ApplyVictoryReward(pending, config,
                healing.NextEconomyState, EconomyConfig(), Rewards(), HandState.CreateStandard(), rng, rng);
            Assert.That(reward.Succeeded, Is.True);
            Assert.That(reward.BaseGold, Is.Zero);
            Assert.That(reward.ExperienceAdded, Is.Zero);
            Assert.That(reward.GuaranteedInterestGold, Is.Zero);
            Assert.That(reward.RandomBonusInterestGold, Is.Zero);
            Assert.That(reward.EconomyRandomConsumed, Is.False);
            Assert.That(reward.NextEconomyState.CurrentHP, Is.EqualTo(30));
        }

        [Test]
        public void BattleTemporaryAndImmediateItemEffectsRestoreOnlyTemporaryCharacterStats()
        {
            StageProgressState stage = PreparationState(StageConfig(), 0, StageChoice.SelectA);
            World world = MakeWorld(stage, Economy(10));
            ItemEffectState effects = new ItemEffectState();
            HandState hand = PutItem(world.Hand, 0, "Item001");
            ItemEffectResult temporary = Use(world, "Item001", ItemEffectTarget.OneAlly,
                ItemEffectDuration.ThisBattle, new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 3) },
                hand, world.Ownership, world.Economy, stage, effects, world.StatsRandomState);
            Assert.That(temporary.Succeeded, Is.True);
            world = world.With(temporary.NextHand, temporary.NextOwnership, temporary.NextEconomy,
                temporary.NextEffectState);

            hand = PutItem(world.Hand, 0, "Item007");
            ItemEffectResult permanent = Use(world, "Item007", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 1) },
                hand, world.Ownership, world.Economy, stage, world.Effects, temporary.NextStatsRandomState);
            Assert.That(permanent.Succeeded, Is.True);
            CharacterInstanceView beforeCleanup = View(permanent.NextOwnership, "C1");
            Assert.That(beforeCleanup.Attack, Is.EqualTo(11));
            Assert.That(beforeCleanup.TemporaryAttackBonus, Is.EqualTo(3));
            Assert.That(permanent.NextHand.Slots[0].IsEmpty, Is.True);

            ItemEffectResult restored = ItemEffectRules.RestoreBattleEffects(permanent.NextBoard,
                permanent.NextHand, permanent.NextOwnership, permanent.NextEconomy,
                permanent.NextEffectState, permanent.NextStatsRandomState);
            Assert.That(restored.Succeeded, Is.True);
            CharacterInstanceView afterCleanup = View(restored.NextOwnership, "C1");
            Assert.That(afterCleanup.Attack, Is.EqualTo(11));
            Assert.That(afterCleanup.TemporaryAttackBonus, Is.Zero);
            Assert.That(ItemEffectRules.RestoreBattleEffects(restored.NextBoard, restored.NextHand,
                restored.NextOwnership, restored.NextEconomy, restored.NextEffectState,
                permanent.NextStatsRandomState).Failure, Is.EqualTo(ItemEffectFailure.NoBattleEffectsToRestore));

            World triple = MakeWorld(stage, Economy(10));
            ItemEffectResult twoStats = Use(triple, "Item003", ItemEffectTarget.OneAlly,
                ItemEffectDuration.ThisBattle, new[]
                {
                    new ItemEffectDefinition(ItemEffectKind.Attack, 2),
                    new ItemEffectDefinition(ItemEffectKind.Defense, 2)
                }, PutItem(triple.Hand, 0, "Item003"), triple.Ownership, triple.Economy,
                stage, new ItemEffectState(), triple.StatsRandomState);
            Assert.That(twoStats.Succeeded, Is.True);
            Assert.That(View(twoStats.NextOwnership, "C1").TemporaryAttackBonus, Is.EqualTo(2));
            Assert.That(View(twoStats.NextOwnership, "C1").TemporaryDefenseBonus, Is.EqualTo(2));
            ItemEffectResult twoStatsRestore = ItemEffectRules.RestoreBattleEffects(twoStats.NextBoard,
                twoStats.NextHand, twoStats.NextOwnership, twoStats.NextEconomy,
                twoStats.NextEffectState, twoStats.NextStatsRandomState);
            Assert.That(View(twoStatsRestore.NextOwnership, "C1").TemporaryAttackBonus, Is.Zero);
            Assert.That(View(twoStatsRestore.NextOwnership, "C1").TemporaryDefenseBonus, Is.Zero);
        }

        [Test]
        public void EventItem006RaisesStarConsumesStatsRandomAndNoEligibleTargetCanEndEvent()
        {
            StageProgressConfig config = StageConfig(total: 10, initialChance: 100, maxChance: 100);
            StageProgressState preparation = PreparationState(config, 0, StageChoice.SelectA);
            StageProgressState pending = StageProgressRules.ResolveBattle(
                StageProgressRules.BeginBattle(preparation).NextState, config, true).NextState;
            Pcg32State eventRng = new Pcg32RandomSource(19, 2).CaptureState();
            StageRewardResult reward = StageRewardRules.ApplyVictoryReward(pending, config,
                Economy(0), EconomyConfig(), Rewards(), HandState.CreateStandard(),
                new Pcg32RandomSource(5, 4).CaptureState(), eventRng);
            Assert.That(reward.Succeeded, Is.True);
            Assert.That(reward.EventStarted, Is.True);
            Assert.That(reward.NextStageState.Phase, Is.EqualTo(StageProgressPhase.EventPending));

            World world = MakeWorld(reward.NextStageState, reward.NextEconomyState);
            ItemDefinition starItem = Item("Item006", 0, ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            EventItemState eventItem = new EventItemState(starItem.DefinitionId);
            ItemEffectResult used = ItemEffectRules.TryUseEventItem(starItem, eventItem,
                ItemUseTarget.BoardCharacter(0), world.Board, world.Hand, world.Ownership,
                world.Economy, world.Stage, new ItemEffectState(), new[] { world.Definition },
                world.Settings, world.StatsRandomState);
            Assert.That(used.Succeeded, Is.True);
            Assert.That(View(used.NextOwnership, "C1").Star, Is.EqualTo(2));
            Assert.That(used.StatsRandomConsumed, Is.True);
            Assert.That(used.NextEventItem.Status, Is.EqualTo(EventItemStatus.Consumed));
            Assert.That(used.NextStage.Phase, Is.EqualTo(StageProgressPhase.EventPending));

            World empty = MakeEmptyWorld(reward.NextStageState, reward.NextEconomyState);
            ItemEffectResult ended = ItemEffectRules.EndEventWithoutEligibleTarget(starItem,
                eventItem, empty.Board, empty.Hand, empty.Ownership, empty.Economy, empty.Stage,
                new ItemEffectState(), empty.StatsRandomState);
            Assert.That(ended.Succeeded, Is.True);
            Assert.That(ended.NextEventItem.Status, Is.EqualTo(EventItemStatus.EndedWithoutUse));
            Assert.That(ended.StatsRandomConsumed, Is.False);
            Assert.That(ended.NextStage.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
        }

        [Test]
        public void EnemyItemsEmitCommandsWithoutApplyingEnemyState()
        {
            StageProgressState stage = PreparationState(StageConfig(), 0, StageChoice.SelectA);
            World world = MakeWorld(stage, Economy(10));
            ItemEffectResult defense = Use(world, "Item002", ItemEffectTarget.AllEnemies,
                ItemEffectDuration.ThisBattle, new[] { new ItemEffectDefinition(ItemEffectKind.Defense, -1) },
                PutItem(world.Hand, 0, "Item002"), world.Ownership, world.Economy, stage,
                world.Effects, world.StatsRandomState, ItemUseTarget.EnemyArea(2));
            Assert.That(defense.Succeeded, Is.True);
            Assert.That(defense.Commands.Count, Is.EqualTo(1));
            Assert.That(defense.Commands[0].Effect, Is.EqualTo(ItemEffectKind.Defense));
            Assert.That(defense.Commands[0].Value, Is.EqualTo(-1));
            Assert.That(View(defense.NextOwnership, "C1").Attack, Is.EqualTo(10));
            Assert.That(View(defense.NextOwnership, "C1").Defense, Is.EqualTo(5));

            ItemEffectResult damage = Use(world, "Item004", ItemEffectTarget.AllEnemies,
                ItemEffectDuration.ThisBattle, new[] { new ItemEffectDefinition(ItemEffectKind.Damage, 2) },
                PutItem(defense.NextHand, 0, "Item004"), defense.NextOwnership,
                defense.NextEconomy, stage, defense.NextEffectState, defense.NextStatsRandomState,
                ItemUseTarget.EnemyArea(2));
            Assert.That(damage.Succeeded, Is.True);
            Assert.That(damage.Commands.Count, Is.EqualTo(1));
            Assert.That(damage.Commands[0].Effect, Is.EqualTo(ItemEffectKind.Damage));
            Assert.That(damage.Commands[0].Value, Is.EqualTo(2));
            Assert.That(View(damage.NextOwnership, "C1").Attack, Is.EqualTo(10));
            Assert.That(View(damage.NextOwnership, "C1").Defense, Is.EqualTo(5));
            Assert.That(damage.NextEffectState.EnemyCommands.Count, Is.EqualTo(2));
        }

        [Test]
        public void FinalVictoryAppliesRewardBeforeCompletedAndCannotBeAppliedTwice()
        {
            StageProgressConfig config = StageConfig(total: 5, bossRound: 5);
            StageProgressState choice = ChoiceState(config, 4, StageChoice.SelectC);
            Assert.That(choice.CurrentSelection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Boss));
            StageProgressState pending = StageProgressRules.ResolveBattle(
                StageProgressRules.BeginBattle(choice).NextState, config, true).NextState;
            Assert.That(pending.Phase, Is.EqualTo(StageProgressPhase.RewardPending));
            Assert.That(pending.PendingReward.IsFinalVictory, Is.True);
            Assert.That(pending.Phase, Is.Not.EqualTo(StageProgressPhase.Completed));

            Pcg32State economyRng = new Pcg32RandomSource(51, 7).CaptureState();
            Pcg32State eventRng = new Pcg32RandomSource(52, 9).CaptureState();
            StageRewardResult reward = StageRewardRules.ApplyVictoryReward(pending, config,
                Economy(0), EconomyConfig(), Rewards(5, 2), HandState.CreateStandard(),
                economyRng, eventRng);
            Assert.That(reward.Succeeded, Is.True);
            Assert.That(reward.BaseGold, Is.EqualTo(5));
            Assert.That(reward.ExperienceAdded, Is.EqualTo(2));
            Assert.That(reward.NextStageState.Phase, Is.EqualTo(StageProgressPhase.Completed));
            Assert.That(reward.NextEconomyState.Gold, Is.EqualTo(reward.TotalGoldAdded));

            StageRewardResult second = StageRewardRules.ApplyVictoryReward(reward.NextStageState,
                config, reward.NextEconomyState, EconomyConfig(), Rewards(5, 2),
                HandState.CreateStandard(), reward.NextEconomyRandomState, reward.NextEventRandomState);
            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.NextEconomyState, Is.SameAs(reward.NextEconomyState));
            Assert.That(second.NextStageState, Is.SameAs(reward.NextStageState));
        }

        [Test]
        public void FailedItemAndRewardOperationsLeaveAllInputStatesAndRandomCheckpointsUnchanged()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState stage = PreparationState(config, 0, StageChoice.SelectA);
            World world = MakeWorld(stage, Economy(1));
            ItemDefinition item = Item("expensive", 4, ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Attack, 1));
            ShopState shop = new ShopState(ShopKind.Item,
                new[] { new ShopOffer("offer", ShopOfferKind.Item, "expensive", 4, null) }, 1, 1, 1);
            Pcg32State shopCheckpoint = new Pcg32RandomSource(21, 3).CaptureState();
            ItemHandPurchaseResult poor = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop, "offer", new[] { item }, world.Economy, world.Hand, 0);
            Assert.That(poor.Succeeded, Is.False);
            Assert.That(poor.NextEconomy, Is.SameAs(world.Economy));
            Assert.That(poor.NextShop, Is.SameAs(shop));
            Assert.That(poor.NextHand, Is.SameAs(world.Hand));
            AssertState(shopCheckpoint, new Pcg32RandomSource(21, 3).CaptureState());

            HandState full = new HandState(Enumerable.Range(0, 5)
                .Select(_ => HandSlotContent.Item("occupied")));
            EconomyState enough = Economy(10);
            ItemHandPurchaseResult handFull = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop, "offer", new[] { item }, enough, full, 0);
            Assert.That(handFull.Succeeded, Is.False);
            Assert.That(handFull.NextEconomy, Is.SameAs(enough));
            Assert.That(handFull.NextShop, Is.SameAs(shop));
            Assert.That(handFull.NextHand, Is.SameAs(full));

            World withItem = MakeWorld(stage, enough);
            HandState withItemHand = PutItem(withItem.Hand, 0, "expensive");
            ItemDefinition allyItem = Item("ally-target", 0, ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Attack, 1));
            ItemEffectResult invalidTarget = ItemEffectRules.TryUseHandItem(allyItem, 0,
                ItemUseTarget.EnemyArea(1), withItem.Board, withItemHand, withItem.Ownership,
                enough, stage, new ItemEffectState(), new[] { withItem.Definition },
                withItem.Settings, withItem.StatsRandomState);
            Assert.That(invalidTarget.Succeeded, Is.False);
            Assert.That(invalidTarget.NextHand, Is.SameAs(withItemHand));
            Assert.That(invalidTarget.NextOwnership, Is.SameAs(withItem.Ownership));
            Assert.That(invalidTarget.NextEconomy, Is.SameAs(enough));
            AssertState(withItem.StatsRandomState, invalidTarget.NextStatsRandomState);

            ItemDefinition star = Item("star", 0, ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            World starTwo = MakeWorld(stage, enough, star: 2);
            EventItemState available = new EventItemState("star");
            StageProgressState eventStage = MakeEventPending();
            Pcg32State stats = starTwo.StatsRandomState;
            ItemEffectResult ineligible = ItemEffectRules.TryUseEventItem(star, available,
                ItemUseTarget.BoardCharacter(0), starTwo.Board, starTwo.Hand, starTwo.Ownership,
                enough, eventStage, new ItemEffectState(), new[] { starTwo.Definition },
                starTwo.Settings, stats);
            Assert.That(ineligible.Succeeded, Is.False);
            Assert.That(ineligible.NextHand, Is.SameAs(starTwo.Hand));
            Assert.That(ineligible.NextOwnership, Is.SameAs(starTwo.Ownership));
            Assert.That(ineligible.NextEventItem, Is.SameAs(available));
            Assert.That(ineligible.StatsRandomConsumed, Is.False);
            AssertState(stats, ineligible.NextStatsRandomState);

            StageRewardResult wrongPhase = StageRewardRules.ApplyVictoryReward(stage, config,
                enough, EconomyConfig(), Rewards(), HandState.CreateStandard(), stats, stats);
            Assert.That(wrongPhase.Succeeded, Is.False);
            Assert.That(wrongPhase.NextStageState, Is.SameAs(stage));
            Assert.That(wrongPhase.NextEconomyState, Is.SameAs(enough));
            Assert.That(wrongPhase.HasNextEconomyRandomState, Is.False);
            Assert.That(wrongPhase.HasNextEventRandomState, Is.False);
        }

        [Test]
        public void IdenticalScenarioReplaysWithSameRngCheckpointsAndUnrelatedStreamsUntouched()
        {
            ReplayResult first = RunDeterministicReplay(90210UL, false);
            ReplayResult second = RunDeterministicReplay(90210UL, false);
            Assert.That(first.ShopOfferIds, Is.EqualTo(second.ShopOfferIds));
            Assert.That(first.Gold, Is.EqualTo(second.Gold));
            Assert.That(first.Attack, Is.EqualTo(second.Attack));
            Assert.That(first.ShopState, Is.EqualTo(second.ShopState));
            AssertState(first.ShopRng, second.ShopRng);
            AssertState(first.EconomyRng, second.EconomyRng);
            AssertState(first.StatsRng, second.StatsRng);
            AssertState(first.EventRng, second.EventRng);

            ReplayResult extraShopDraw = RunDeterministicReplay(90210UL, true);
            Assert.That(StateEquals(first.ShopRng, extraShopDraw.ShopRng), Is.False);
            AssertState(first.EconomyRng, extraShopDraw.EconomyRng);
            AssertState(first.StatsRng, extraShopDraw.StatsRng);
            AssertState(first.EventRng, extraShopDraw.EventRng);

            ReplayResult differentSeed = RunDeterministicReplay(90211UL, false);
            Assert.That(StateEquals(first.ShopRng, differentSeed.ShopRng), Is.False);
            Assert.That(StateEquals(first.EconomyRng, differentSeed.EconomyRng), Is.False);
            Assert.That(StateEquals(first.StatsRng, differentSeed.StatsRng), Is.False);
            Assert.That(StateEquals(first.EventRng, differentSeed.EventRng), Is.False);
        }

        private static ReplayResult RunDeterministicReplay(ulong seed, bool extraShopDraw)
        {
            SimulationRandomStreams streams = new SimulationRandomStreams(seed);
            if (extraShopDraw) streams.Shop.NextInt(0, 1000);
            ItemDefinition[] items =
            {
                Item("i1", 1, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                    new ItemEffectDefinition(ItemEffectKind.Attack, 1)),
                Item("i2", 2, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                    new ItemEffectDefinition(ItemEffectKind.Attack, 2)),
                Item("i3", 3, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                    new ItemEffectDefinition(ItemEffectKind.Attack, 3))
            };
            CharacterPool pool = new CharacterPool(new[] { new CharacterPoolEntry("D1", 5, 5) });
            ItemShopGenerationResult shop = ItemShopRules.Generate(null, pool, items,
                1, 1, 2, 0, streams.Shop);
            ItemHandPurchaseResult purchased = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop.NextShop, shop.NextShop.Offers[0].OfferId, items, Economy(10),
                HandState.CreateStandard(), 0);
            StageProgressState stage = PreparationState(StageConfig(), 0, StageChoice.SelectA);
            World world = MakeWorld(stage, purchased.NextEconomy);
            ItemDefinition definition = items.Single(x => x.DefinitionId == purchased.PurchasedItemDefinitionId);
            ItemEffectResult effect = ItemEffectRules.TryUseHandItem(definition, 0,
                ItemUseTarget.BoardCharacter(0), world.Board, purchased.NextHand, world.Ownership,
                purchased.NextEconomy, stage, new ItemEffectState(), new[] { world.Definition },
                world.Settings, streams.Stats.CaptureState());
            StageProgressState pending = StageProgressRules.ResolveBattle(
                StageProgressRules.BeginBattle(stage).NextState, StageConfig(), true).NextState;
            StageRewardResult reward = StageRewardRules.ApplyVictoryReward(pending, StageConfig(),
                effect.NextEconomy, EconomyConfig(), Rewards(), effect.NextHand,
                streams.Economy.CaptureState(), streams.Event.CaptureState());
            Assert.That(purchased.Succeeded && effect.Succeeded && reward.Succeeded, Is.True);
            return new ReplayResult(string.Join(",", shop.NextShop.Offers.Select(x => x.DefinitionId)),
                reward.NextEconomyState.Gold, View(effect.NextOwnership, "C1").TemporaryAttackBonus,
                shop.NextShop.Offers[0].Status, shop.NextShopRandomState,
                reward.NextEconomyRandomState, effect.NextStatsRandomState, reward.NextEventRandomState);
        }

        private static StageProgressConfig StageConfig(int total = 10, int initialChance = 0,
            int maxChance = 0, int bossRound = 4) => new StageProgressConfig(total, 5, 5,
                bossRound, initialChance, 0, maxChance, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static EconomyRulesConfiguration EconomyConfig() => new EconomyRulesConfiguration(
            4, 2, 1, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 10, 1, 3, 3, 1, 0);

        private static StageRewardConfiguration Rewards(int gold = 5, int xp = 2) =>
            new StageRewardConfiguration(gold, xp);

        private static EconomyState Economy(int gold, int xp = 0, int level = 1,
            int hp = 20, int maxHp = 30) => new EconomyState(gold, xp, level, 12,
                gold, 0, 1, hp, maxHp);

        private static StageProgressState ChoiceState(StageProgressConfig config, int index,
            StageChoice choice)
        {
            StageProgressState state = BeginAt(config, index);
            if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                state = StageSelectionRules.Select(state, config, choice).NextState;
            return state;
        }

        private static StageProgressState PreparationState(StageProgressConfig config, int index,
            StageChoice choice)
        {
            StageProgressState state = ChoiceState(config, index, choice);
            if (state.Phase == StageProgressPhase.AwaitingEncounterStart)
                state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            return state;
        }

        private static StageProgressState BeginAt(StageProgressConfig config, int index)
        {
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            for (int i = 0; i < index; i++)
            {
                state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
                if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                    state = StageSelectionRules.Select(state, config, StageChoice.SelectA).NextState;
                StageProgressResult battle = StageProgressRules.BeginBattle(state);
                state = StageProgressRules.ResolveBattle(battle.NextState, config, true).NextState;
                state = StageProgressRules.CompletePendingReward(state, config,
                    new Pcg32RandomSource((ulong)(i + 1), 3).CaptureState()).NextState;
                if (state.Phase == StageProgressPhase.EventPending)
                    state = StageProgressRules.CompleteEvent(state, false).NextState;
            }
            return StageProgressRules.BeginCurrentEncounter(state, config).NextState;
        }

        private static World MakeWorld(StageProgressState stage, EconomyState economy, int star = 1)
        {
            CharacterDefinition definition = new CharacterDefinition("D1", "Hero", 1, null, null);
            CharacterStatDefinition settings = Stats();
            CharacterInstance instance = new CharacterInstanceFactory().Create(definition, 1);
            var initRng = new Pcg32RandomSource(3, 5);
            Assert.That(instance.TrySetStar(star, definition, settings, initRng), Is.True);
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { instance });
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, instance.InstanceId, out board), Is.True);
            return new World(definition, settings, board, HandState.CreateStandard(), ownership,
                economy, stage, new Pcg32RandomSource(88, 13).CaptureState(), new ItemEffectState());
        }

        private static World MakeEmptyWorld(StageProgressState stage, EconomyState economy) =>
            new World(new CharacterDefinition("D1", "Hero", 1, null, null), Stats(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), economy, stage,
                new Pcg32RandomSource(89, 15).CaptureState(), new ItemEffectState());

        private static CharacterStatDefinition Stats() => new CharacterStatDefinition(new[]
        {
            new CharacterCostStats(1,
                new[] { new CharacterStatOption(10, 5, 1) },
                new[] { new CharacterStatOption(20, 9, 1) },
                new[] { new CharacterStatOption(30, 15, 1) })
        });

        private static ItemDefinition Item(string id, int price, ItemEffectTarget target,
            ItemEffectDuration duration, params ItemEffectDefinition[] effects) =>
            new ItemDefinition(id, id, price, string.Empty, target, duration, effects);

        private static HandState PutItem(HandState hand, int slot, string id)
        {
            Assert.That(hand.TryWithSlot(slot, HandSlotContent.Item(id), out HandState next), Is.True);
            return next;
        }

        private static ItemEffectResult Use(World world, string id, ItemEffectTarget target,
            ItemEffectDuration duration, ItemEffectDefinition[] definitions, HandState hand,
            CharacterOwnershipState ownership, EconomyState economy, StageProgressState stage,
            ItemEffectState effectState, Pcg32State statsState, ItemUseTarget useTarget = null)
        {
            return ItemEffectRules.TryUseHandItem(Item(id, 0, target, duration, definitions), 0,
                useTarget ?? ItemUseTarget.BoardCharacter(0), world.Board, hand, ownership, economy, stage,
                effectState, new[] { world.Definition }, world.Settings, statsState);
        }

        private static CharacterInstanceView View(CharacterOwnershipState ownership, string id)
        {
            Assert.That(ownership.TryGet(id, out CharacterInstanceView view), Is.True);
            return view;
        }

        private static StageProgressState MakeEventPending()
        {
            StageProgressConfig config = StageConfig(total: 10, initialChance: 100, maxChance: 100);
            StageProgressState prep = PreparationState(config, 0, StageChoice.SelectA);
            StageProgressState pending = StageProgressRules.ResolveBattle(
                StageProgressRules.BeginBattle(prep).NextState, config, true).NextState;
            StageRewardResult reward = StageRewardRules.ApplyVictoryReward(pending, config,
                Economy(0), EconomyConfig(), Rewards(), HandState.CreateStandard(),
                new Pcg32RandomSource(1, 2).CaptureState(), new Pcg32RandomSource(3, 4).CaptureState());
            Assert.That(reward.Succeeded, Is.True);
            return reward.NextStageState;
        }

        private static void AssertState(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        private static bool StateEquals(Pcg32State left, Pcg32State right) =>
            left.AlgorithmVersion == right.AlgorithmVersion && left.State == right.State &&
            left.Increment == right.Increment;

        private sealed class World
        {
            public CharacterDefinition Definition { get; }
            public CharacterStatDefinition Settings { get; }
            public BoardState Board { get; }
            public HandState Hand { get; }
            public CharacterOwnershipState Ownership { get; }
            public EconomyState Economy { get; }
            public StageProgressState Stage { get; }
            public Pcg32State StatsRandomState { get; }
            public ItemEffectState Effects { get; }

            public World(CharacterDefinition definition, CharacterStatDefinition settings,
                BoardState board, HandState hand, CharacterOwnershipState ownership,
                EconomyState economy, StageProgressState stage, Pcg32State statsRandomState,
                ItemEffectState effects)
            {
                Definition = definition; Settings = settings; Board = board; Hand = hand;
                Ownership = ownership; Economy = economy; Stage = stage;
                StatsRandomState = statsRandomState; Effects = effects;
            }

            public World With(HandState hand, CharacterOwnershipState ownership,
                EconomyState economy, ItemEffectState effects) => new World(Definition, Settings,
                    Board, hand, ownership, economy, Stage, StatsRandomState, effects);
        }

        private sealed class ReplayResult
        {
            public string ShopOfferIds { get; }
            public int Gold { get; }
            public int Attack { get; }
            public ShopOfferStatus ShopState { get; }
            public Pcg32State ShopRng { get; }
            public Pcg32State EconomyRng { get; }
            public Pcg32State StatsRng { get; }
            public Pcg32State EventRng { get; }

            public ReplayResult(string offerIds, int gold, int attack, ShopOfferStatus shopState,
                Pcg32State shopRng, Pcg32State economyRng, Pcg32State statsRng, Pcg32State eventRng)
            {
                ShopOfferIds = offerIds; Gold = gold; Attack = attack; ShopState = shopState;
                ShopRng = shopRng; EconomyRng = economyRng; StatsRng = statsRng; EventRng = eventRng;
            }
        }
    }
}
