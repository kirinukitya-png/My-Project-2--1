using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
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
        private static (GameSession Session, GameSessionConfiguration Config) CreateStartedBattle(
            ulong seed, int enemyAttack, int enemyHp, int maxRounds,
            string[] targetIds = null, string[] pigEffectIds = null, string[] aeEffectIds = null,
            System.Collections.Generic.IEnumerable<BattleEffectDefinition> effects = null,
            System.Collections.Generic.IEnumerable<ItemDefinition> items = null,
            int eventChance = 0)
        {
            EnemyDefinition enemy = new EnemyDefinition("battle-enemy", "Battle Enemy",
                enemyAttack, 0, enemyHp, null, aeEffectIds, pigEffectIds, "T001", targetIds, null, null);
            GameSessionConfiguration config = Configuration(enemy: enemy, eventChance: eventChance,
                targets: new[] { new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.Uniform) },
                effects: effects, items: items, maxBattleRounds: maxRounds);
            GameSession session = GameSession.Create(config, seed);
            string offer = session.CurrentState.Shop.Offers.First(x => x.Kind == ShopOfferKind.Character &&
                x.Status == ShopOfferStatus.Available).OfferId;
            ActionDispatchResult purchase = Execute(session, new BuyCharacterAction(
                new ActionId("11b/buy/" + seed), offer,
                new GameActionLocation(CharacterLocationKind.Board, 0)));
            Assert.That(purchase.Succeeded, Is.True, purchase.Error.Detail);
            ActionDispatchResult started = Execute(session,
                new StartBattleAction(new ActionId("11b/start/" + seed)));
            Assert.That(started.Succeeded, Is.True, started.Error.Detail);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Battle));
            Assert.That(session.CurrentState.Battle, Is.Not.Null);
            return (session, config);
        }

        [Test]
        public void AdvanceBattleRound_UsesRulesAndCommitsVictoryWithoutApplyingRewards()
        {
            var world = CreateStartedBattle(20101, enemyAttack: 0, enemyHp: 1, maxRounds: 4);
            SimulationRandomStreamsState before = world.Session.CurrentState.RandomState;
            int goldBefore = world.Session.CurrentState.Economy.Gold;
            int actionCount = world.Session.CurrentState.ExecutedActionIds.Count;

            BattleAdvanceResult result = world.Session.AdvanceBattleRound();

            Assert.That(result.Succeeded, Is.True, result.Detail);
            Assert.That(result.BattleFinished, Is.True);
            Assert.That(result.RoundsAdvanced, Is.EqualTo(1));
            Assert.That(result.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(world.Session.CurrentState.Stage.Phase, Is.EqualTo(StageProgressPhase.RewardPending));
            Assert.That(world.Session.CurrentState.BattleResult, Is.SameAs(result.BattleResult));
            Assert.That(world.Session.CurrentState.Economy.Gold, Is.EqualTo(goldBefore));
            Assert.That(world.Session.CurrentState.ExecutedActionIds.Count, Is.EqualTo(actionCount));
            Assert.That(world.Session.CurrentState.RandomState.Shop, Is.EqualTo(before.Shop));
            Assert.That(world.Session.CurrentState.RandomState.Stats, Is.EqualTo(before.Stats));
            Assert.That(world.Session.CurrentState.RandomState.Encounter, Is.EqualTo(before.Encounter));
            Assert.That(world.Session.CurrentState.RandomState.Event, Is.EqualTo(before.Event));
            Assert.That(world.Session.CurrentState.RandomState.Roster, Is.EqualTo(before.Roster));
            Assert.That(world.Session.CurrentState.RandomState.Economy, Is.EqualTo(before.Economy));
            Assert.That(world.Session.CurrentState.RandomState.Battle, Is.Not.EqualTo(before.Battle));
        }

        [Test]
        public void AdvanceBattleRound_StopsAtOneRoundBoundaryAndContinuesFromCheckpoint()
        {
            var world = CreateStartedBattle(20102, enemyAttack: 0, enemyHp: 10000, maxRounds: 3);
            BattleAdvanceResult first = world.Session.AdvanceBattleRound();
            Assert.That(first.Succeeded, Is.True, first.Detail);
            Assert.That(first.BattleInProgress, Is.True);
            Assert.That(first.BattleFinished, Is.False);
            Assert.That(first.RoundsAdvanced, Is.EqualTo(1));
            Assert.That(world.Session.CurrentState.Battle.CurrentRound, Is.EqualTo(2));
            Assert.That(world.Session.CurrentState.Battle.Phase, Is.EqualTo(BattlePhase.RoundStart));

            BattleAdvanceResult second = world.Session.AdvanceBattleRound();
            Assert.That(second.Succeeded, Is.True, second.Detail);
            Assert.That(second.RoundsAdvanced, Is.EqualTo(1));
            Assert.That(second.BattleFinished, Is.False);
            Assert.That(world.Session.CurrentState.Battle.CurrentRound, Is.EqualTo(3));
        }

        [Test]
        public void AdvanceBattleToEnd_UsesBattleFinishRulesForDefeatAndUnresolved()
        {
            var defeat = CreateStartedBattle(20103, enemyAttack: 1000, enemyHp: 10000, maxRounds: 4);
            BattleAdvanceResult defeatResult = defeat.Session.AdvanceBattleToEnd();
            Assert.That(defeatResult.Succeeded, Is.True, defeatResult.Detail);
            Assert.That(defeatResult.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Defeat));
            Assert.That(defeat.Session.CurrentState.Stage.Phase, Is.EqualTo(StageProgressPhase.GameOver));
            Assert.That(defeatResult.BattleResult.EndingPlayerHP, Is.Zero);

            var unresolved = CreateStartedBattle(20104, enemyAttack: 0, enemyHp: 10000, maxRounds: 1);
            BattleAdvanceResult unresolvedResult = unresolved.Session.AdvanceBattleToEnd();
            Assert.That(unresolvedResult.Succeeded, Is.True, unresolvedResult.Detail);
            Assert.That(unresolvedResult.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(unresolved.Session.CurrentState.Battle.Status, Is.EqualTo(BattleExecutionStatus.Completed));
            Assert.That(unresolved.Session.CurrentState.Stage.Phase, Is.EqualTo(StageProgressPhase.Battle));
            Assert.That(unresolved.Session.CurrentState.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(unresolved.Session.AdvanceBattleToEnd().Failure,
                Is.EqualTo(BattleAdvanceFailure.BattleAlreadyFinished));
            GameSession unresolvedRestore = GameSession.Restore(unresolved.Config,
                unresolved.Session.CaptureCheckpoint());
            Assert.That(unresolvedRestore.CurrentState.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(unresolvedRestore.GetObservation().Succeeded, Is.True);
        }

        [Test]
        public void AdvanceBattleToEnd_IsDeterministicAndRestoresAtBattleStartAndFinish()
        {
            var first = CreateStartedBattle(20105, enemyAttack: 1, enemyHp: 1, maxRounds: 4);
            ActionExecutionState checkpoint = first.Session.CaptureCheckpoint();
            GameSession restored = GameSession.Restore(first.Config, checkpoint);
            SimulationRandomStreamsState before = checkpoint.RandomState;

            BattleAdvanceResult a = first.Session.AdvanceBattleToEnd();
            BattleAdvanceResult b = restored.AdvanceBattleToEnd();
            Assert.That(a.Succeeded, Is.True, a.Detail);
            Assert.That(b.Succeeded, Is.True, b.Detail);
            Assert.That(a.BattleResult.Outcome, Is.EqualTo(b.BattleResult.Outcome));
            Assert.That(a.BattleResult.FinalRound, Is.EqualTo(b.BattleResult.FinalRound));
            Assert.That(a.BattleResult.EndingPlayerHP, Is.EqualTo(b.BattleResult.EndingPlayerHP));
            Assert.That(first.Session.CurrentState.RandomState, Is.EqualTo(restored.CurrentState.RandomState));
            Assert.That(first.Session.CurrentState.RandomState.Battle,
                Is.EqualTo(a.BattleResult.BattleRandomCheckpoint));
            Assert.That(checkpoint.RandomState, Is.EqualTo(before));

            GameSession finishedRestore = GameSession.Restore(first.Config, first.Session.CaptureCheckpoint());
            Assert.That(finishedRestore.CurrentState.BattleResult.Outcome, Is.EqualTo(a.BattleResult.Outcome));
            Assert.That(finishedRestore.AdvanceBattleRound().Failure,
                Is.EqualTo(BattleAdvanceFailure.BattleAlreadyFinished));
        }

        [Test]
        public void FailedBattleProgressionIsAtomicAndOtherPhasesAreRejected()
        {
            GameSession preparation = Create(seed: 20106);
            ActionExecutionState prepBefore = preparation.CurrentState;
            Assert.That(preparation.AdvanceBattleRound().Failure, Is.EqualTo(BattleAdvanceFailure.InvalidPhase));
            Assert.That(preparation.CurrentState, Is.SameAs(prepBefore));

            var invalidTarget = CreateStartedBattle(20107, enemyAttack: 1, enemyHp: 10000,
                maxRounds: 2, targetIds: new[] { "T999" });
            ActionExecutionState before = invalidTarget.Session.CurrentState;
            BattleAdvanceResult rejected = invalidTarget.Session.AdvanceBattleToEnd();
            Assert.That(rejected.Status, Is.EqualTo(BattleAdvanceStatus.Rejected));
            Assert.That(rejected.Failure, Is.EqualTo(BattleAdvanceFailure.RuleRejected));
            Assert.That(invalidTarget.Session.CurrentState, Is.SameAs(before));
            Assert.That(invalidTarget.Session.CurrentState.RandomState, Is.EqualTo(before.RandomState));
            Assert.That(invalidTarget.Session.CurrentState.Economy, Is.SameAs(before.Economy));
        }

        [Test]
        public void PIGAndAEEffectsResolveBeforeBattleOutcomeAndFinishCleanup()
        {
            var effects = new[]
            {
                new BattleEffectDefinition("PIG-DMG", BattleEffectType.Damage, 3,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate),
                new BattleEffectDefinition("AE-DMG", BattleEffectType.Damage, 2,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate)
            };
            var pigEnemy = CreateStartedBattle(20108, enemyAttack: 0, enemyHp: 1,
                maxRounds: 3, pigEffectIds: new[] { "PIG-DMG" }, effects: effects);
            BattleAdvanceResult pig = pigEnemy.Session.AdvanceBattleToEnd();
            Assert.That(pig.Succeeded, Is.True, pig.Detail);
            Assert.That(pig.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(pig.BattleResult.EndingPlayerHP, Is.EqualTo(27));

            var aeEnemy = CreateStartedBattle(20109, enemyAttack: 1, enemyHp: 10000,
                maxRounds: 1, aeEffectIds: new[] { "AE-DMG" }, effects: effects);
            BattleAdvanceResult ae = aeEnemy.Session.AdvanceBattleToEnd();
            Assert.That(ae.Succeeded, Is.True, ae.Detail);
            Assert.That(ae.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(ae.BattleResult.EndingPlayerHP, Is.EqualTo(28));
        }

        [Test]
        public void BattleFinishRestoresTemporaryItemBuffAndKeepsImmediatePersistentBuff()
        {
            ItemDefinition persistent = new ItemDefinition("permanent-atk", "Permanent ATK", 0,
                string.Empty, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 3) });
            ItemDefinition temporary = new ItemDefinition("temporary-atk", "Temporary ATK", 0,
                string.Empty, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 2) });
            EnemyDefinition enemy = new EnemyDefinition("buff-enemy", "Buff Enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            GameSessionConfiguration config = Configuration(enemy: enemy,
                targets: new[] { new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.Uniform) }, maxBattleRounds: 3, items: new[] { BlockItem, persistent, temporary });
            GameSession session = GameSession.Create(config, 20110);
            string offer = session.CurrentState.Shop.Offers.First(x => x.Kind == ShopOfferKind.Character &&
                x.Status == ShopOfferStatus.Available).OfferId;
            Assert.That(Execute(session, new BuyCharacterAction(new ActionId("11b/buff-buy"), offer,
                new GameActionLocation(CharacterLocationKind.Board, 0))).Succeeded, Is.True);
            ActionExecutionState source = session.CurrentState;
            string instanceId = source.Ownership.GetOwnedInstances().Single().InstanceId;
            source.Hand.TryWithSlot(0, HandSlotContent.Item(persistent.DefinitionId), out HandState persistentHand);
            ItemEffectResult permanentResult = ItemEffectRules.TryUseHandItem(persistent, 0,
                ItemUseTarget.BoardCharacter(0), source.Board, persistentHand, source.Ownership,
                source.Economy, source.Stage, source.ItemEffects, config.CharacterDefinitions,
                config.CharacterStatSettings, source.RandomState.Stats);
            Assert.That(permanentResult.Succeeded, Is.True, permanentResult.Failure.ToString());

                permanentResult.NextHand.TryWithSlot(0, HandSlotContent.Item(temporary.DefinitionId), out HandState temporaryHand);
            ItemEffectResult temporaryResult = ItemEffectRules.TryUseHandItem(temporary, 0,
                ItemUseTarget.BoardCharacter(0), permanentResult.NextBoard, temporaryHand,
                permanentResult.NextOwnership, permanentResult.NextEconomy, permanentResult.NextStage,
                permanentResult.NextEffectState, config.CharacterDefinitions, config.CharacterStatSettings,
                permanentResult.NextStatsRandomState);
            Assert.That(temporaryResult.Succeeded, Is.True, temporaryResult.Failure.ToString());
            Assert.That(temporaryResult.NextEffectState.HasActiveBattleEffects, Is.True);
            temporaryResult.NextOwnership.TryGet(instanceId, out CharacterInstanceView buffed);
            // Temporary bonuses are tracked separately from the persistent Attack field.
            int persistentAttack = buffed.Attack;
            Assert.That(buffed.TemporaryAttackBonus, Is.EqualTo(2));

            SimulationRandomStreamsState random = new SimulationRandomStreamsState(source.RandomState.TrialSeed,
                source.RandomState.Version, source.RandomState.Shop, temporaryResult.NextStatsRandomState,
                source.RandomState.Encounter, source.RandomState.Battle, source.RandomState.Event,
                source.RandomState.Roster, source.RandomState.Economy);
            var checkpoint = new ActionExecutionState(temporaryResult.NextStage,
                temporaryResult.NextEconomy, source.CharacterPool, source.InstanceFactory,
                temporaryResult.NextBoard, temporaryResult.NextHand, temporaryResult.NextOwnership,
                temporaryResult.NextEffectState, random, source.Shop, source.EventItem,
                source.EnemyBoard, source.EnemyPreparation, executedActionIds: source.ExecutedActionIds);
            GameSession restored = GameSession.Restore(config, checkpoint);
            Assert.That(Execute(restored, new StartBattleAction(new ActionId("11b/buff-start"))).Succeeded, Is.True);
            BattleAdvanceResult finished = restored.AdvanceBattleToEnd();
            Assert.That(finished.Succeeded, Is.True, finished.Detail);
            Assert.That(finished.BattleResult.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(restored.CurrentState.ItemEffects.HasActiveBattleEffects, Is.False);
            Assert.That(restored.CurrentState.Ownership.TryGet(instanceId, out CharacterInstanceView after), Is.True);
            Assert.That(after.TemporaryAttackBonus, Is.Zero);
            Assert.That(after.Attack, Is.EqualTo(persistentAttack));
        }
    }
}
