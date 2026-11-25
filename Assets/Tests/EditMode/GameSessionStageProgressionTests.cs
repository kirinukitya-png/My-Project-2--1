using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameSessionTests
    {
        private static GameSessionConfiguration ConfigurationWithStage(StageProgressConfig stage,
            EnemyDefinition enemy = null, ItemDefinition[] items = null, int maxBattleRounds = 3,
            ItemDefinition eventItemDefinition = null) =>
            new GameSessionConfiguration(stage, EconomyConfig(), ShopConfig(), StatConfig(),
                new StageRewardConfiguration(5, 2), new BattleConfig(maxBattleRounds), StartingEconomy(),
                Roster, Roster, Roster, items ?? new[] { BlockItem }, new[] { enemy ?? Enemy },
                EncounterPools(enemy ?? Enemy), 1, battleTargetDefinitions: new[]
                {
                    new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                        BattleTargetSelection.Uniform)
                }, eventItemDefinition: eventItemDefinition);

        private static GameSession ReachStageChoice(int choiceRound = 2, ulong seed = 21101,
            int eventChance = 0, ItemDefinition[] items = null)
        {
            var world = CreateStartedBattle(seed, enemyAttack: 0, enemyHp: 1,
                maxRounds: 3, items: items, eventChance: eventChance);
            BattleAdvanceResult battle = world.Session.AdvanceBattleToEnd();
            Assert.That(battle.Succeeded, Is.True, battle.Detail);
            StageSessionResult reward = world.Session.ResolveStageAfterBattle();
            Assert.That(reward.Succeeded, Is.True, reward.Detail);
            StageSessionResult advanced = world.Session.AdvanceToNextStage();
            Assert.That(advanced.Succeeded, Is.True, advanced.Detail);
            Assert.That(world.Session.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            return world.Session;
        }

        [Test]
        public void VictoryRewardReturnsShopReservationsAndNextEncounterUsesOnlyCurrentRules()
        {
            var world = CreateStartedBattle(21111, enemyAttack: 0, enemyHp: 1, maxRounds: 3);
            GameSession session = world.Session;
            Assert.That(session.AdvanceBattleToEnd().BattleResult.Outcome, Is.EqualTo(BattleOutcome.Victory));
            SimulationRandomStreamsState afterBattle = session.CurrentState.RandomState;

            StageSessionResult reward = session.ResolveStageAfterBattle();
            Assert.That(reward.Succeeded, Is.True, reward.Detail);
            Assert.That(reward.Reward.BaseGold, Is.EqualTo(3));
            Assert.That(reward.Reward.ExperienceAdded, Is.EqualTo(1));
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(session.CurrentState.Shop, Is.Null);
            Assert.That(session.CurrentState.Battle, Is.Null);
            Assert.That(session.CurrentState.CharacterPool.Entries.Values.All(x => x.ReservedCopies == 0), Is.True);
            Assert.That(session.ResolveStageAfterBattle().Status, Is.EqualTo(StageSessionStatus.Rejected));

            StageSessionResult next = session.AdvanceToNextStage();
            Assert.That(next.Succeeded, Is.True, next.Detail);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            Assert.That(session.CurrentState.RandomState.Shop, Is.EqualTo(afterBattle.Shop));
            Assert.That(session.CurrentState.RandomState.Encounter, Is.EqualTo(afterBattle.Encounter));
            Assert.That(session.CurrentState.RandomState.Event, Is.EqualTo(afterBattle.Event));
            Assert.That(session.CurrentState.RandomState.Battle, Is.EqualTo(afterBattle.Battle));
            GameSession restored = GameSession.Restore(world.Config, session.CaptureCheckpoint());
            Assert.That(restored.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
        }

        [Test]
        public void AutomaticNextEncounterGeneratesShopAndEncounterAtomically()
        {
            StageProgressConfig stage = new StageProgressConfig(12, 4, 3, 4, 0, 10, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            GameSession session = GameSession.Create(ConfigurationWithStage(stage), 21112);
            string offer = session.CurrentState.Shop.Offers.First(x => x.Kind == ShopOfferKind.Character &&
                x.Status == ShopOfferStatus.Available).OfferId;
            Assert.That(Execute(session, new BuyCharacterAction(new ActionId("11c-auto-buy"), offer,
                new GameActionLocation(CharacterLocationKind.Board, 0))).Succeeded, Is.True);
            Assert.That(Execute(session, new StartBattleAction(new ActionId("11c-auto-start"))).Succeeded, Is.True);
            Assert.That(session.AdvanceBattleToEnd().BattleResult.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(session.ResolveStageAfterBattle().Succeeded, Is.True);

            SimulationRandomStreamsState before = session.CurrentState.RandomState;
            StageSessionResult advanced = session.AdvanceToNextStage();
            Assert.That(advanced.Succeeded, Is.True, advanced.Detail);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(session.CurrentState.Stage.EncounterIndex, Is.EqualTo(1));
            Assert.That(session.CurrentState.Shop, Is.Not.Null);
            Assert.That(session.CurrentState.EnemyBoard, Is.Not.Null);
            Assert.That(session.CurrentState.Battle, Is.Null);
            Assert.That(session.CurrentState.RandomState.Shop, Is.Not.EqualTo(before.Shop));
            Assert.That(session.CurrentState.RandomState.Encounter, Is.Not.EqualTo(before.Encounter));
            Assert.That(session.CurrentState.RandomState.Event, Is.EqualTo(before.Event));
            Assert.That(session.CurrentState.RandomState.Battle, Is.EqualTo(before.Battle));
            GameSession restored = GameSession.Restore(ConfigurationWithStage(stage),
                session.CaptureCheckpoint());
            Assert.That(restored.Phase, Is.EqualTo(StageProgressPhase.Preparation));
        }

        [TestCase(StageChoice.SelectB, ShopKind.Item)]
        [TestCase(StageChoice.SelectD, ShopKind.Character)]
        public void EnemylessChoiceCompletesWithoutBattleThenAdvances(StageChoice choice, ShopKind expectedShop)
        {
            GameSession session = ReachStageChoice(seed: 21113 + (ulong)choice);
            Assert.That(Execute(session, new SelectStageChoiceAction(
                new ActionId("11c-choice-" + choice), choice)).Succeeded, Is.True);
            Assert.That(session.CurrentState.Shop.Kind, Is.EqualTo(expectedShop));
            Assert.That(session.CurrentState.Battle, Is.Null);

            if (choice == StageChoice.SelectB)
            {
                CharacterPool poolBefore = session.CurrentState.CharacterPool;
                Assert.That(Execute(session, new RerollShopAction(new ActionId("11c-item-reroll"),
                    ShopKind.Item)).Succeeded, Is.True);
                Assert.That(session.CurrentState.CharacterPool, Is.SameAs(poolBefore));
                ShopOffer itemOffer = session.CurrentState.Shop.Offers.First(x =>
                    x.Kind == ShopOfferKind.Item && x.Status == ShopOfferStatus.Available);
                Assert.That(Execute(session, new BuyItemAction(new ActionId("11c-item-buy"),
                    itemOffer.OfferId, 0)).Succeeded, Is.True);
                Assert.That(session.CurrentState.Hand.TryGetSlot(0, out HandSlotContent itemContent), Is.True);
                Assert.That(itemContent.Kind, Is.EqualTo(HandSlotContentKind.Item));
            }

            StageSessionResult resolved = session.ResolveEnemylessStage();
            Assert.That(resolved.Succeeded, Is.True, resolved.Detail);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(session.CurrentState.Battle, Is.Null);
            Assert.That(session.CurrentState.BattleResult, Is.Null);
            Assert.That(resolved.Reward.ExperienceAdded, Is.Zero);
            Assert.That(session.ResolveEnemylessStage().Status, Is.EqualTo(StageSessionStatus.Rejected));
            Assert.That(session.AdvanceToNextStage().Succeeded, Is.True);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(session.CurrentState.Shop, Is.Not.Null);
            Assert.That(session.CurrentState.Battle, Is.Null);
        }

        [Test]
        public void EventItemSelectionIsExplicitAtomicAndExistingEventActionsCanFinishIt()
        {
            ItemDefinition eventItem = new ItemDefinition("event-atk", "Event Attack", 0,
                string.Empty, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 1) });
            var world = CreateStartedBattle(21115, enemyAttack: 0, enemyHp: 1, maxRounds: 3,
                items: new[] { BlockItem, eventItem }, eventChance: 100);
            GameSession session = world.Session;
            Assert.That(session.AdvanceBattleToEnd().BattleResult.Outcome, Is.EqualTo(BattleOutcome.Victory));
            ActionExecutionState before = session.CurrentState;
            StageSessionResult required = session.ResolveStageAfterBattle();
            Assert.That(required.Status, Is.EqualTo(StageSessionStatus.NeedsEventItemSelection));
            Assert.That(session.CurrentState, Is.SameAs(before));
            Assert.That(required.EventStarted, Is.True);

            StageSessionResult invalid = session.ResolveStageAfterBattle("missing-event-item");
            Assert.That(invalid.Failure, Is.EqualTo(StageSessionFailure.InvalidEventItem));
            Assert.That(session.CurrentState, Is.SameAs(before));
            Pcg32State eventCheckpoint = before.RandomState.Event;

            StageSessionResult eventStarted = session.ResolveStageAfterBattle(eventItem.DefinitionId);
            Assert.That(eventStarted.Succeeded, Is.True, eventStarted.Detail);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            Assert.That(session.CurrentState.EventItem.DefinitionId, Is.EqualTo(eventItem.DefinitionId));
            Assert.That(session.CurrentState.Battle, Is.Null);
            Assert.That(session.CurrentState.RandomState.Event, Is.EqualTo(eventCheckpoint));
            Assert.That(session.GetObservation().Succeeded, Is.True);
            Assert.That(GameSession.Restore(world.Config, session.CaptureCheckpoint()).Phase,
                Is.EqualTo(StageProgressPhase.EventPending));

            GameActionTarget target = GameActionTarget.FromItemUseTarget(ItemUseTarget.BoardCharacter(0));
            Assert.That(Execute(session, new UseEventItemAction(new ActionId("11c-event-use"), target)).Succeeded, Is.True);
            Assert.That(session.CurrentState.EventItem.Status, Is.EqualTo(EventItemStatus.Consumed));
            Assert.That(Execute(session, new EndEventAction(new ActionId("11c-event-end"))).Succeeded, Is.True);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(session.AdvanceToNextStage().Succeeded, Is.True);
            Assert.That(session.CurrentState.EventItem, Is.Null);
        }

        [Test]
        public void UnresolvedAndDefeatAreStopsAndNeverReceiveVictoryReward()
        {
            var unresolved = CreateStartedBattle(21116, enemyAttack: 0, enemyHp: 10000, maxRounds: 1);
            Assert.That(unresolved.Session.AdvanceBattleToEnd().BattleResult.Outcome,
                Is.EqualTo(BattleOutcome.Unresolved));
            ActionExecutionState unresolvedBefore = unresolved.Session.CurrentState;
            StageSessionResult stopped = unresolved.Session.ResolveStageAfterBattle();
            Assert.That(stopped.Status, Is.EqualTo(StageSessionStatus.StoppedUnresolved));
            Assert.That(unresolved.Session.CurrentState, Is.SameAs(unresolvedBefore));
            Assert.That(unresolved.Session.AdvanceToNextStage().Status,
                Is.EqualTo(StageSessionStatus.StoppedUnresolved));

            var defeat = CreateStartedBattle(21117, enemyAttack: 1000, enemyHp: 10000, maxRounds: 3);
            Assert.That(defeat.Session.AdvanceBattleToEnd().BattleResult.Outcome,
                Is.EqualTo(BattleOutcome.Defeat));
            ActionExecutionState defeatBefore = defeat.Session.CurrentState;
            StageSessionResult gameOver = defeat.Session.ResolveStageAfterBattle();
            Assert.That(gameOver.Status, Is.EqualTo(StageSessionStatus.GameOver));
            Assert.That(defeat.Session.CurrentState, Is.SameAs(defeatBefore));
            Assert.That(defeat.Session.AdvanceToNextStage().Status, Is.EqualTo(StageSessionStatus.GameOver));
        }

        [Test]
        public void FinalVictoryAppliesRewardBeforeCompletedAndCannotApplyTwice()
        {
            StageProgressConfig finalStage = new StageProgressConfig(1, 1, 1, 1, 0, 10, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            GameSessionConfiguration config = ConfigurationWithStage(finalStage,
                enemy: new EnemyDefinition("final-enemy", "Final", 0, 0, 1,
                    null, null, null, "T001", null, null, null));
            GameSession session = GameSession.Create(config, 21118);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            Assert.That(Execute(session, new SelectStageChoiceAction(new ActionId("11c-final-choice"),
                StageChoice.SelectA)).Succeeded, Is.True);
            string offer = session.CurrentState.Shop.Offers.First(x => x.Kind == ShopOfferKind.Character &&
                x.Status == ShopOfferStatus.Available).OfferId;
            Assert.That(Execute(session, new BuyCharacterAction(new ActionId("11c-final-buy"), offer,
                new GameActionLocation(CharacterLocationKind.Board, 0))).Succeeded, Is.True);
            Assert.That(Execute(session, new StartBattleAction(new ActionId("11c-final-start"))).Succeeded, Is.True);
            Assert.That(session.AdvanceBattleToEnd().BattleResult.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.RewardPending));

            StageSessionResult completed = session.ResolveStageAfterBattle();
            Assert.That(completed.Succeeded, Is.True, completed.Detail);
            Assert.That(completed.Reward.IsFinalStageReward, Is.True);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Completed));
            ActionExecutionState after = session.CurrentState;
            Assert.That(session.ResolveStageAfterBattle().Status, Is.EqualTo(StageSessionStatus.Rejected));
            Assert.That(session.CurrentState, Is.SameAs(after));
            Assert.That(session.AdvanceToNextStage().Status, Is.EqualTo(StageSessionStatus.Completed));
            Assert.That(GameSession.Restore(config, session.CaptureCheckpoint()).Phase,
                Is.EqualTo(StageProgressPhase.Completed));
        }
    }
}
