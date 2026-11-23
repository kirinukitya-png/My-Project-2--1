using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BattleFinishRulesTests
    {
        private sealed class World
        {
            public BattleState Battle;
            public EconomyState Economy;
            public BoardState Board;
            public HandState Hand;
            public CharacterOwnershipState Ownership;
            public ItemEffectState Effects;
            public CharacterDefinition CharacterDefinition;
            public CharacterStatDefinition StatSettings;
        }

        private static Pcg32State Rng(ulong seed = 902) => new Pcg32RandomSource(seed, 71).CaptureState();
        private static StageProgressConfig StageConfig() => new StageProgressConfig(
            25, 5, 5, 4, 0, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static World Create(int enemyCount = 1, int playerHp = 20, int maxRounds = 100,
            int enemyAttack = 1, int enemyDefense = 0, int enemyHp = 20, int enemyGap = 0,
            string[] cip = null, string[] ae = null, string[] pig = null,
            bool withAlly = false, int allyAttack = 10, int allyDefense = 1,
            ItemEffectState itemEffects = null, EconomyState economyOverride = null)
        {
            var result = new World
            {
                Board = BoardState.CreateStandard(), Hand = HandState.CreateStandard(),
                Ownership = new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                Effects = itemEffects ?? new ItemEffectState()
            };
            CharacterDefinition[] roster = Array.Empty<CharacterDefinition>();
            CharacterInstance[] owned = Array.Empty<CharacterInstance>();
            if (withAlly)
            {
                result.CharacterDefinition = new CharacterDefinition("D1", "Hero", 1,
                    Array.Empty<string>(), Array.Empty<string>());
                result.StatSettings = new CharacterStatDefinition(new[]
                {
                    new CharacterCostStats(1,
                        new[] { new CharacterStatOption(allyAttack, allyDefense, 1) },
                        new[] { new CharacterStatOption(allyAttack, allyDefense, 1) },
                        new[] { new CharacterStatOption(allyAttack, allyDefense, 1) })
                });
                var instance = new CharacterInstanceFactory().Create(result.CharacterDefinition, 1);
                Assert.That(instance.EnsureStats(result.CharacterDefinition, result.StatSettings,
                    new Pcg32RandomSource(18, 7)), Is.True);
                owned = new[] { instance };
                roster = new[] { result.CharacterDefinition };
                result.Ownership = new CharacterOwnershipState(owned);
                Assert.That(result.Board.TryWithCharacterAt(0, instance.InstanceId, out BoardState placed), Is.True);
                result.Board = placed;
            }

            var enemyDefinition = new EnemyDefinition("E1", "Enemy", enemyAttack,
                enemyDefense, enemyHp, cip, ae, pig, "T001", new[] { "T001" }, null, null);
            EnemyBoardState enemyBoard = EnemyBoardState.CreateEmpty();
            for (int i = 0; i < enemyCount; i++)
            {
                Assert.That(EnemyInstanceFactory.TryCreate(enemyDefinition, 0, i,
                    0, 0, 0, enemyGap, 0, 0, out EnemyInstance enemy,
                    out EnemyInstanceFailure failure), Is.True, failure.ToString());
                EnemyBoardOperationResult placed = enemyBoard.TryPlace(enemy);
                Assert.That(placed.Succeeded, Is.True, placed.Failure.ToString());
                enemyBoard = placed.NextState;
            }

            EconomyState economy = economyOverride ?? new EconomyState(10, 0, 3, 12,
                10, 0, 1, playerHp, 30);
            StageProgressConfig stageConfig = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(stageConfig), stageConfig).NextState;
            StageProgressState stage = StageProgressRules.BeginBattle(prep).NextState;
            BattleInitializationResult initialized = BattleInitializationRules.Initialize(
                economy, stage, result.Board, result.Hand, result.Ownership, roster,
                enemyBoard, new[] { enemyDefinition }, result.Effects, roster, roster,
                Rng(), new BattleConfig(maxRounds));
            Assert.That(initialized.Succeeded, Is.True, initialized.Failure.ToString());
            result.Battle = initialized.State;
            result.Economy = economy;
            return result;
        }

        private static BattleState StartAndResolveCip(BattleState state,
            ref EconomyState economy, IReadOnlyDictionary<string, BattleEffectDefinition> definitions = null)
        {
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.BattleStartRequired));
            state = BattleTurnRules.ResolveAction(request.NextState,
                BattleActionResolution.BoundaryCompleted(request.ActionRequestId)).NextState;
            while (state.PendingEffectTriggers.Count > 0)
            {
                BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                    state, economy, state.PendingEffectTriggers[0].TriggerId,
                    definitions ?? new Dictionary<string, BattleEffectDefinition>());
                Assert.That(effect.Succeeded, Is.True, effect.Failure.ToString());
                state = effect.NextState;
                economy = effect.NextEconomyState;
            }
            return state;
        }

        private static BattleState EnterAllyAction(BattleState state)
        {
            BattleScheduleResult round = BattleTurnRules.RequestNext(state);
            Assert.That(round.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
            BattleScheduleResult action = BattleTurnRules.RequestNext(round.NextState);
            Assert.That(action.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
            return action.NextState;
        }

        private static BattleState EnterEnemyAction(BattleState state)
        {
            BattleScheduleResult round = BattleTurnRules.RequestNext(state);
            Assert.That(round.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
            BattleState cursor = round.NextState;
            BattleScheduleResult allyEnd = BattleTurnRules.RequestNext(cursor);
            Assert.That(allyEnd.Event, Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
            BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(allyEnd.NextState);
            Assert.That(enemyStart.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
            BattleState enemyTurn = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            BattleScheduleResult enemy = BattleTurnRules.RequestNext(enemyTurn);
            Assert.That(enemy.Event, Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
            return enemy.NextState;
        }

        [Test]
        public void InitialEmptyEnemyBoardFinishesAsVictoryAndKeepsRng()
        {
            World world = Create(enemyCount: 0);
            Pcg32State rng = world.Battle.BattleRandomState;
            BattleFinishResult result = BattleFinishRules.TryFinish(world.Battle, world.Economy,
                world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(result.Result.Reason, Is.EqualTo(BattleFinishReason.EnemiesDefeated));
            Assert.That(result.Result.LivingEnemyCount, Is.Zero);
            Assert.That(result.NextBattleState.Status, Is.EqualTo(BattleExecutionStatus.Completed));
            Assert.That(result.NextBattleState.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(result.NextBattleRandomState, Is.EqualTo(rng));
        }

        [Test]
        public void PlayerHealthDepletedDuringBattleFinishesAsDefeatBeforeEnemyVictory()
        {
            World world = Create(enemyCount: 1, playerHp: 1, enemyAttack: 1, enemyHp: 20);
            BattleState started = StartAndResolveCip(world.Battle, ref world.Economy);
            BattleState enemyAction = EnterEnemyAction(started);
            var targets = new Dictionary<string, BattleTargetDefinition>
            {
                { "T001", new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.Uniform) }
            };
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                enemyAction, enemyAction.PendingActionRequestId, targets);
            Assert.That(attack.Succeeded, Is.True, attack.Failure.ToString());
            Assert.That(attack.NextState.PlayerCurrentHP, Is.Zero);

            BattleFinishResult result = BattleFinishRules.TryFinish(attack.NextState, world.Economy,
                world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Result.Outcome, Is.EqualTo(BattleOutcome.Defeat));
            Assert.That(result.Result.Reason, Is.EqualTo(BattleFinishReason.PlayerHealthDepleted));
        }

        [Test]
        public void CannotFinishWhileCipTriggerIsWaiting()
        {
            World world = Create(enemyCount: 1, cip: new[] { "CIP" });
            BattleScheduleResult start = BattleTurnRules.RequestNext(world.Battle);
            BattleState afterCipQueued = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId)).NextState;
            BattleFinishResult result = BattleFinishRules.TryFinish(afterCipQueued, world.Economy,
                world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(BattleFinishFailure.PendingEffects));
            Assert.That(result.NextBattleState, Is.SameAs(afterCipQueued));
        }

        [Test]
        public void VictoryWaitsForPigThenCountsHpZeroEnemyAsDefeated()
        {
            World world = Create(enemyCount: 1, enemyHp: 1, maxRounds: 1, withAlly: true,
                allyAttack: 20, pig: new[] { "PIG" });
            BattleState state = StartAndResolveCip(world.Battle, ref world.Economy);
            BattleState actionState = EnterAllyAction(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveAllyNormalAttack(
                actionState, actionState.PendingActionRequestId);
            Assert.That(attack.Succeeded, Is.True);
            Assert.That(attack.DefeatedEnemyInstanceId, Is.Not.Null);
            Assert.That(attack.NextState.PendingEffectTriggers, Has.Count.EqualTo(1));

            BattleFinishResult blocked = BattleFinishRules.TryFinish(attack.NextState,
                world.Economy, world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(blocked.Failure, Is.EqualTo(BattleFinishFailure.PendingEffects));

            var definitions = new Dictionary<string, BattleEffectDefinition>
            {
                { "PIG", new BattleEffectDefinition("PIG", BattleEffectType.Gold, -1,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate) }
            };
            BattlePendingEffectTrigger trigger = attack.NextState.PendingEffectTriggers[0];
            BattleEffectResolutionResult pig = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, world.Economy, trigger.TriggerId, definitions);
            Assert.That(pig.Succeeded, Is.True);
            BattleFinishResult finished = BattleFinishRules.TryFinish(pig.NextState,
                pig.NextEconomyState, world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(finished.Succeeded, Is.True);
            Assert.That(finished.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(finished.Result.LivingEnemyCount, Is.Zero);
            Assert.That(finished.Result.DefeatedEnemyCount, Is.EqualTo(1));
            Assert.That(finished.NextEconomyState.Gold, Is.EqualTo(world.Economy.Gold - 1));
        }

        [Test]
        public void EnemyAttackAndAeResolveBeforeDefeatIsFinalized()
        {
            World world = Create(enemyCount: 1, playerHp: 2, maxRounds: 1, enemyAttack: 8,
                ae: new[] { "AE" });
            BattleState state = StartAndResolveCip(world.Battle, ref world.Economy);
            BattleState enemyAction = EnterEnemyAction(state);
            var targets = new Dictionary<string, BattleTargetDefinition>
            {
                { "T001", new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.Uniform) }
            };
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                enemyAction, enemyAction.PendingActionRequestId, targets);
            Assert.That(attack.Succeeded, Is.True);
            Assert.That(attack.NextState.PlayerCurrentHP, Is.Zero);
            Assert.That(attack.NextState.PendingEffectTriggers, Has.Count.EqualTo(1));
            Assert.That(BattleFinishRules.TryFinish(attack.NextState, world.Economy,
                world.Board, world.Hand, world.Ownership, world.Effects).Failure,
                Is.EqualTo(BattleFinishFailure.PendingEffects));

            var definitions = new Dictionary<string, BattleEffectDefinition>
            {
                { "AE", new BattleEffectDefinition("AE", BattleEffectType.Gold, -1,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate) }
            };
            BattlePendingEffectTrigger trigger = attack.NextState.PendingEffectTriggers[0];
            BattleEffectResolutionResult ae = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, world.Economy, trigger.TriggerId, definitions);
            BattleFinishResult finished = BattleFinishRules.TryFinish(ae.NextState,
                ae.NextEconomyState, world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(finished.Succeeded, Is.True);
            Assert.That(finished.Result.Outcome, Is.EqualTo(BattleOutcome.Defeat));
            Assert.That(finished.NextEconomyState.CurrentHP, Is.Zero);
            Assert.That(finished.NextEconomyState.Gold, Is.EqualTo(world.Economy.Gold - 1));
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(100)]
        public void MaxRoundWithLivingEnemyFinishesUnresolvedWithoutAdvancing(int maxRounds)
        {
            World world = Create(enemyCount: 1, maxRounds: maxRounds, enemyGap: maxRounds + 2);
            BattleState state = world.Battle;
            EconomyState economy = world.Economy;
            state = StartAndResolveCip(state, ref economy);
            for (int roundNumber = 1; roundNumber <= maxRounds; roundNumber++)
            {
                BattleScheduleResult round = BattleTurnRules.RequestNext(state);
                Assert.That(round.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
                BattleScheduleResult ally = BattleTurnRules.RequestNext(round.NextState);
                Assert.That(ally.Event, Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
                BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(ally.NextState);
                Assert.That(enemyStart.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
                state = BattleTurnRules.ResolveAction(enemyStart.NextState,
                    BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
                BattleScheduleResult enemyWait = BattleTurnRules.RequestNext(state);
                Assert.That(enemyWait.Event, Is.EqualTo(BattleScheduleEvent.EnemyWaiting));
                BattleScheduleResult enemyEnd = BattleTurnRules.RequestNext(enemyWait.NextState);
                Assert.That(enemyEnd.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnCompleted));
                BattleScheduleResult finishRound = BattleTurnRules.RequestNext(enemyEnd.NextState);
                state = finishRound.NextState;
                if (roundNumber < maxRounds)
                    Assert.That(finishRound.Event, Is.EqualTo(BattleScheduleEvent.RoundCompleted));
                else
                    Assert.That(finishRound.Event, Is.EqualTo(BattleScheduleEvent.RoundLimitReached));
            }
            Assert.That(state.CurrentRound, Is.EqualTo(maxRounds));
            BattleFinishResult result = BattleFinishRules.TryFinish(state, economy,
                world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Result.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(result.NextBattleState.CurrentRound, Is.EqualTo(maxRounds));
            Assert.That(BattleTurnRules.RequestNext(result.NextBattleState).Failure,
                Is.EqualTo(BattleScheduleFailure.BattleAlreadyCompleted));
        }

        [Test]
        public void LastRoundVictoryTakesPriorityOverUnresolved()
        {
            World world = Create(enemyCount: 1, enemyHp: 1, maxRounds: 1,
                enemyGap: 3, withAlly: true, allyAttack: 20);
            BattleState state = StartAndResolveCip(world.Battle, ref world.Economy);
            BattleState action = EnterAllyAction(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveAllyNormalAttack(
                action, action.PendingActionRequestId);
            BattleFinishResult finish = BattleFinishRules.TryFinish(attack.NextState,
                world.Economy, world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(finish.Succeeded, Is.True);
            Assert.That(finish.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
        }

        [Test]
        public void LastRoundDefeatTakesPriorityOverUnresolved()
        {
            World world = Create(enemyCount: 1, playerHp: 1, maxRounds: 1,
                enemyAttack: 5);
            BattleState state = StartAndResolveCip(world.Battle, ref world.Economy);
            BattleState enemyAction = EnterEnemyAction(state);
            var targets = new Dictionary<string, BattleTargetDefinition>
            {
                { "T001", new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.Uniform) }
            };
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                enemyAction, enemyAction.PendingActionRequestId, targets);
            BattleFinishResult finish = BattleFinishRules.TryFinish(attack.NextState,
                world.Economy, world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(finish.Succeeded, Is.True);
            Assert.That(finish.Result.Outcome, Is.EqualTo(BattleOutcome.Defeat));
        }

        [Test]
        public void FinishRequiresOutcomeBoundaryAndDoesNotChangeInputsOnFailure()
        {
            World world = Create(enemyCount: 1);
            BattleFinishResult result = BattleFinishRules.TryFinish(world.Battle,
                world.Economy, world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(BattleFinishFailure.OutcomeNotReached));
            Assert.That(result.NextBattleState, Is.SameAs(world.Battle));
            Assert.That(result.NextEconomyState, Is.SameAs(world.Economy));
            Assert.That(result.NextOwnership, Is.SameAs(world.Ownership));
            Assert.That(result.Result, Is.Null);
        }

        [Test]
        public void InvalidBoardOwnershipStateCannotPartiallyFinish()
        {
            World world = Create(enemyCount: 0);
            BoardState invalidBoard = BoardState.CreateStandard();
            Assert.That(invalidBoard.TryWithCharacterAt(0, "not-owned", out invalidBoard), Is.True);
            BattleFinishResult result = BattleFinishRules.TryFinish(world.Battle,
                world.Economy, invalidBoard, world.Hand, world.Ownership, world.Effects);
            Assert.That(result.Failure, Is.EqualTo(BattleFinishFailure.InvalidState));
            Assert.That(result.NextBoard, Is.SameAs(invalidBoard));
            Assert.That(result.NextBattleState, Is.SameAs(world.Battle));
            Assert.That(result.NextEconomyState, Is.SameAs(world.Economy));
        }

        [Test]
        public void FinishClearsBlockBonusesAndBattleItemStateOnEveryOutcome()
        {
            World world = Create(enemyCount: 0);
            HandState hand = HandState.CreateEmpty(5);
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Item("block-now"), out hand), Is.True);
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Item("block-battle"), out hand), Is.True);
            ItemDefinition permanent = new ItemDefinition("block-now", "Block", 0, "",
                ItemEffectTarget.Player, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Block, 2) });
            ItemDefinition temporary = new ItemDefinition("block-battle", "Block", 0, "",
                ItemEffectTarget.Player, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Block, 3) });
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            ItemEffectResult first = ItemEffectRules.TryUseHandItem(permanent, 0,
                ItemUseTarget.PlayerArea(0), world.Board, hand, world.Ownership,
                world.Economy, prep, world.Effects, null, null, Rng());
            Assert.That(first.Succeeded, Is.True);
            ItemEffectResult second = ItemEffectRules.TryUseHandItem(temporary, 1,
                ItemUseTarget.PlayerArea(0), first.NextBoard, first.NextHand,
                first.NextOwnership, first.NextEconomy, prep, first.NextEffectState,
                null, null, first.NextStatsRandomState);
            Assert.That(second.Succeeded, Is.True);
            Assert.That(second.NextEffectState.PermanentPlayerBlockBonus, Is.EqualTo(2));
            Assert.That(second.NextEffectState.TemporaryPlayerBlockBonus, Is.EqualTo(3));
            Assert.That(second.NextEconomy.CurrentHP, Is.EqualTo(world.Economy.CurrentHP));

            World withBonuses = Create(enemyCount: 0, itemEffects: second.NextEffectState,
                economyOverride: second.NextEconomy);
            Assert.That(withBonuses.Battle.PlayerBlock, Is.EqualTo(5));
            BattleFinishResult result = BattleFinishRules.TryFinish(withBonuses.Battle,
                withBonuses.Economy, withBonuses.Board, withBonuses.Hand,
                withBonuses.Ownership, withBonuses.Effects);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextBattleState.PlayerBlock, Is.Zero);
            Assert.That(result.NextBattleState.PendingPlayerBlockBonus, Is.Zero);
            Assert.That(result.NextBattleState.PermanentPlayerBlockBonus, Is.Zero);
            Assert.That(result.NextItemEffectState.PermanentPlayerBlockBonus, Is.Zero);
            Assert.That(result.NextItemEffectState.TemporaryPlayerBlockBonus, Is.Zero);
            Assert.That(result.NextItemEffectState.HasActiveBattleEffects, Is.False);
        }

        [Test]
        public void BattleDurationHpAndCharacterBonusesAreRestoredWithoutTouchingBaseStats()
        {
            World world = Create(enemyCount: 0, playerHp: 10, withAlly: true,
                allyAttack: 10, allyDefense: 5);
            CharacterInstanceView original = Get(world.Ownership, "C1");
            ItemEffectState emptyEffects = new ItemEffectState();
            HandState hand = HandState.CreateEmpty(5);
            hand.TryWithSlot(0, HandSlotContent.Item("temporary"), out hand);
            ItemDefinition item = new ItemDefinition("temporary", "Temp", 0, "",
                ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 3),
                    new ItemEffectDefinition(ItemEffectKind.Defense, 2) });
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            ItemEffectResult used = ItemEffectRules.TryUseHandItem(item, 0,
                ItemUseTarget.BoardCharacter(0), world.Board, hand, world.Ownership,
                world.Economy, prep, emptyEffects, new[] { world.CharacterDefinition },
                world.StatSettings, Rng());
            Assert.That(used.Succeeded, Is.True);
            Assert.That(Get(used.NextOwnership, "C1").Attack, Is.EqualTo(original.Attack));

            World buffed = Create(enemyCount: 0, playerHp: 10, withAlly: true,
                allyAttack: 10, allyDefense: 5, itemEffects: used.NextEffectState,
                economyOverride: used.NextEconomy);
            // Rebuild the battle using the actual ownership and Board snapshot containing the temporary item bonuses.
            BattleInitializationResult initialized = InitializeExistingCharacter(used.NextOwnership,
                used.NextBoard, used.NextEconomy, used.NextEffectState,
                buffed.Battle.BattleRandomState, 100);
            BattleFinishResult finish = BattleFinishRules.TryFinish(initialized.State,
                used.NextEconomy, used.NextBoard, used.NextHand, used.NextOwnership,
                used.NextEffectState);
            Assert.That(finish.Succeeded, Is.True);
            Assert.That(finish.NextOwnership.TryGet("C1", out CharacterInstanceView restored), Is.True);
            Assert.That(restored.Attack, Is.EqualTo(original.Attack));
            Assert.That(restored.Defense, Is.EqualTo(original.Defense));
            Assert.That(restored.TemporaryAttackBonus, Is.Zero);
            Assert.That(restored.TemporaryDefenseBonus, Is.Zero);
            Assert.That(finish.NextBattleState.Allies[0].Attack, Is.EqualTo(original.Attack));
            Assert.That(finish.NextBattleState.Allies[0].Defense, Is.EqualTo(original.Defense));
        }

        [Test]
        public void FinishRestoresUnitMoveTemporaryDefenseReductionOnTheDefeatedEnemy()
        {
            CharacterDefinition first = new CharacterDefinition("D1", "First", 1,
                Array.Empty<string>(), new[] { "U" });
            CharacterDefinition second = new CharacterDefinition("D2", "Second", 1,
                Array.Empty<string>(), new[] { "U" });
            CharacterDefinition finisher = new CharacterDefinition("D3", "Finisher", 1,
                Array.Empty<string>(), Array.Empty<string>());
            CharacterDefinition[] definitions = { first, second, finisher };
            var stats = new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(1,
                    new[] { new CharacterStatOption(1, 1, 1) },
                    new[] { new CharacterStatOption(1, 1, 1) },
                    new[] { new CharacterStatOption(1, 1, 1) }),
                new CharacterCostStats(2,
                    new[] { new CharacterStatOption(20, 1, 1) },
                    new[] { new CharacterStatOption(20, 1, 1) },
                    new[] { new CharacterStatOption(20, 1, 1) })
            });
            var factory = new CharacterInstanceFactory();
            CharacterInstance[] instances = new CharacterInstance[3];
            BoardState board = BoardState.CreateStandard();
            for (int i = 0; i < 3; i++)
            {
                instances[i] = factory.Create(definitions[i], i == 2 ? 2 : 1);
                Assert.That(instances[i].EnsureStats(definitions[i], stats,
                    new Pcg32RandomSource((ulong)(40 + i), 13)), Is.True);
                Assert.That(board.TryWithCharacterAt(i, instances[i].InstanceId,
                    out BoardState placed), Is.True);
                board = placed;
            }
            var ownership = new CharacterOwnershipState(instances);
            EnemyDefinition enemyDefinition = new EnemyDefinition("E1", "Enemy", 1, 10, 5,
                null, null, null, "T001", new[] { "T001" }, null, null);
            Assert.That(EnemyInstanceFactory.TryCreate(enemyDefinition, 0, 0, 0, 0, 0,
                0, 0, 0, out EnemyInstance enemy, out EnemyInstanceFailure enemyFailure),
                Is.True, enemyFailure.ToString());
            EnemyBoardState enemyBoard = EnemyBoardState.CreateEmpty();
            enemyBoard = enemyBoard.TryPlace(enemy).NextState;
            EconomyState economy = new EconomyState(10, 0, 3, 12, 10, 0, 1, 20, 30);
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            StageProgressState stage = StageProgressRules.BeginBattle(prep).NextState;
            BattleInitializationResult init = BattleInitializationRules.Initialize(economy,
                stage, board, HandState.CreateStandard(), ownership, definitions,
                enemyBoard, new[] { enemyDefinition }, new ItemEffectState(), definitions,
                definitions, Rng(), new BattleConfig(1));
            Assert.That(init.Succeeded, Is.True, init.Failure.ToString());
            Assert.That(init.State.Allies[2].Attack, Is.EqualTo(20));

            BattleScheduleResult start = BattleTurnRules.RequestNext(init.State);
            BattleState state = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId)).NextState;
            state = BattleTurnRules.RequestNext(state).NextState; // RoundStarted
            BattleScheduleResult firstAction = BattleTurnRules.RequestNext(state);
            Assert.That(firstAction.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
            BattleUnitMoveDecision decision = BattleUnitMoveRules.DecideCurrentAllyAction(
                firstAction.NextState, firstAction.ActionRequestId);
            Assert.That(decision.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.MoveAvailable));
            BattleAttackResult move = BattleUnitMoveRules.ResolveUnitMoveAttack(
                firstAction.NextState, firstAction.ActionRequestId, decision.SelectedMove.MoveId);
            Assert.That(move.Succeeded, Is.True, move.Failure.ToString());
            Assert.That(move.CalculatedDamage, Is.Zero);
            Assert.That(move.NextState.Enemies[0].Defense, Is.EqualTo(8));
            Assert.That(move.NextState.Enemies[0].TemporaryDefenseReduction, Is.EqualTo(2));

            state = move.NextState;
            BattleScheduleResult next;
            do { next = BattleTurnRules.RequestNext(state); state = next.NextState; }
            while (next.Event == BattleScheduleEvent.AllySkippedByParticipationLimit ||
                   next.Event == BattleScheduleEvent.AllySkippedByStun);
            Assert.That(next.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
            BattleAttackResult kill = BattleNormalAttackRules.ResolveAllyNormalAttack(
                state, next.ActionRequestId);
            Assert.That(kill.Succeeded, Is.True, kill.Failure.ToString());
            Assert.That(kill.CalculatedDamage, Is.GreaterThanOrEqualTo(5));
            Assert.That(kill.NextState.Enemies[0].CurrentHP, Is.Zero);
            Assert.That(kill.DefeatedEnemyInstanceId, Is.Not.Null);

            BattleFinishResult finish = BattleFinishRules.TryFinish(kill.NextState,
                economy, board, HandState.CreateStandard(), ownership, new ItemEffectState());
            Assert.That(finish.Succeeded, Is.True, finish.Failure.ToString());
            Assert.That(finish.NextBattleState.Enemies[0].Defense, Is.EqualTo(10));
            Assert.That(finish.NextBattleState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
        }

        [Test]
        public void FinishRevertsTemporaryPlayerHpButKeepsOutcomeFromCombatHp()
        {
            World initial = Create(enemyCount: 0, playerHp: 10);
            HandState hand = HandState.CreateEmpty(5);
            hand.TryWithSlot(0, HandSlotContent.Item("battle-heal"), out hand);
            ItemDefinition battleHeal = new ItemDefinition("battle-heal", "Heal", 0, "",
                ItemEffectTarget.Player, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Damage, -4) });
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            ItemEffectResult used = ItemEffectRules.TryUseHandItem(battleHeal, 0,
                ItemUseTarget.PlayerArea(0), initial.Board, hand, initial.Ownership,
                initial.Economy, prep, initial.Effects, null, null, Rng());
            Assert.That(used.Succeeded, Is.True);
            World battle = Create(enemyCount: 0, playerHp: 14, economyOverride: used.NextEconomy,
                itemEffects: used.NextEffectState);
            BattleFinishResult result = BattleFinishRules.TryFinish(battle.Battle,
                used.NextEconomy, battle.Board, used.NextHand, used.NextOwnership,
                used.NextEffectState);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(result.Result.PlayerHPAtOutcomeCheck, Is.EqualTo(14));
            Assert.That(result.Result.EndingPlayerHP, Is.EqualTo(10));
            Assert.That(result.NextEconomyState.CurrentHP, Is.EqualTo(10));
        }

        [Test]
        public void ConfirmedPersistentCipCommandIsReportedAndNotReappliedByFinish()
        {
            World world = Create(enemyCount: 1, enemyHp: 1, withAlly: true,
                allyAttack: 20, cip: new[] { "CIP" });
            BattleScheduleResult start = BattleTurnRules.RequestNext(world.Battle);
            BattleState queued = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId)).NextState;
            BattlePendingEffectTrigger trigger = queued.PendingEffectTriggers[0];
            var effects = new Dictionary<string, BattleEffectDefinition>
            {
                { "CIP", new BattleEffectDefinition("CIP", BattleEffectType.Attack, 2,
                    BattleEffectTarget.TriggerActor, BattleEffectDuration.Immediate) }
            };
            BattleEffectResolutionResult cip = BattleEffectRules.ResolveNextPendingTrigger(
                queued, world.Economy, trigger.TriggerId, effects);
            Assert.That(cip.Succeeded, Is.True);
            Assert.That(cip.PersistentCommands, Has.Count.EqualTo(1));
            Assert.That(cip.NextState.PersistentEffectCommands, Has.Count.EqualTo(1));
            Assert.That(cip.NextState.PersistentEffectCommands[0].TriggerId, Is.EqualTo(trigger.TriggerId));

            BattleState action = EnterAllyAction(cip.NextState);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveAllyNormalAttack(
                action, action.PendingActionRequestId);
            Assert.That(attack.Succeeded, Is.True);
            BattleFinishResult finish = BattleFinishRules.TryFinish(attack.NextState,
                cip.NextEconomyState, world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(finish.Succeeded, Is.True);
            Assert.That(finish.Result.ConfirmedPersistentEffects, Has.Count.EqualTo(1));
            Assert.That(finish.NextBattleState.PersistentEffectCommands, Has.Count.EqualTo(1));
        }

        [Test]
        public void BattleGoldEffectIsAlreadyInEconomyAndNotAppliedAgainAtFinish()
        {
            World world = Create(enemyCount: 1, enemyHp: 1, withAlly: true,
                allyAttack: 20, cip: new[] { "CIP" });
            BattleScheduleResult start = BattleTurnRules.RequestNext(world.Battle);
            BattleState queued = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId)).NextState;
            var definitions = new Dictionary<string, BattleEffectDefinition>
            {
                { "CIP", new BattleEffectDefinition("CIP", BattleEffectType.Gold, -1,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate) }
            };
            BattlePendingEffectTrigger trigger = queued.PendingEffectTriggers[0];
            BattleEffectResolutionResult cip = BattleEffectRules.ResolveNextPendingTrigger(
                queued, world.Economy, trigger.TriggerId, definitions);
            Assert.That(cip.Succeeded, Is.True);
            BattleState action = EnterAllyAction(cip.NextState);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveAllyNormalAttack(
                action, action.PendingActionRequestId);
            Assert.That(attack.Succeeded, Is.True);
            BattleFinishResult finish = BattleFinishRules.TryFinish(attack.NextState,
                cip.NextEconomyState,
                world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(finish.Succeeded, Is.True);
            Assert.That(finish.NextEconomyState.Gold, Is.EqualTo(world.Economy.Gold - 1));
        }

        [Test]
        public void BlockAbsorptionIsIncludedInBattleResult()
        {
            World baseWorld = Create(enemyCount: 1, enemyAttack: 8, playerHp: 2);
            HandState hand = HandState.CreateEmpty(5);
            hand.TryWithSlot(0, HandSlotContent.Item("block"), out hand);
            ItemDefinition block = new ItemDefinition("block", "Block", 0, "",
                ItemEffectTarget.Player, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Block, 5) });
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            ItemEffectResult used = ItemEffectRules.TryUseHandItem(block, 0,
                ItemUseTarget.PlayerArea(0), baseWorld.Board, hand, baseWorld.Ownership,
                baseWorld.Economy, prep, baseWorld.Effects, null, null, Rng());
            Assert.That(used.Succeeded, Is.True);
            World world = Create(enemyCount: 1, enemyAttack: 8, playerHp: 2,
                itemEffects: used.NextEffectState, economyOverride: used.NextEconomy);
            BattleState state = StartAndResolveCip(world.Battle, ref world.Economy);
            BattleState enemyAction = EnterEnemyAction(state);
            var targets = new Dictionary<string, BattleTargetDefinition>
            {
                { "T001", new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.Uniform) }
            };
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                enemyAction, enemyAction.PendingActionRequestId, targets);
            Assert.That(attack.Succeeded, Is.True);
            Assert.That(attack.BlockAbsorbed, Is.EqualTo(5));
            BattleFinishResult finish = BattleFinishRules.TryFinish(attack.NextState,
                world.Economy, world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(finish.Succeeded, Is.True);
            Assert.That(finish.Result.BlockAbsorbed, Is.EqualTo(5));
            Assert.That(finish.Result.Outcome, Is.EqualTo(BattleOutcome.Defeat));
        }

        [Test]
        public void FinishResetsStunMoveHistoryPendingEffectsAndBlock()
        {
            World world = Create(enemyCount: 0);
            BattleFinishResult result = BattleFinishRules.TryFinish(world.Battle, world.Economy,
                world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextBattleState.UsedUnitMoveIds, Is.Empty);
            Assert.That(result.NextBattleState.ParticipationCounts, Is.Empty);
            Assert.That(result.NextBattleState.PendingEffectTriggers, Is.Empty);
            Assert.That(result.NextBattleState.PendingActionKind, Is.EqualTo(BattleActionKind.None));
            Assert.That(result.NextBattleState.BattleOutcomeCheckPending, Is.False);
            Assert.That(result.NextBattleState.PlayerBlock, Is.Zero);
        }

        [Test]
        public void FinishedBattleRejectsDoubleFinishAndFurtherActions()
        {
            World world = Create(enemyCount: 0);
            BattleFinishResult first = BattleFinishRules.TryFinish(world.Battle, world.Economy,
                world.Board, world.Hand, world.Ownership, world.Effects);
            BattleFinishResult second = BattleFinishRules.TryFinish(first.NextBattleState,
                first.NextEconomyState, first.NextBoard, first.NextHand,
                first.NextOwnership, first.NextItemEffectState);
            Assert.That(second.Failure, Is.EqualTo(BattleFinishFailure.AlreadyFinished));
            Assert.That(second.Result, Is.Null);
            Assert.That(BattleTurnRules.RequestNext(first.NextBattleState).Failure,
                Is.EqualTo(BattleScheduleFailure.BattleAlreadyCompleted));
        }

        [Test]
        public void FinishedBattleRejectsPendingEffectResolution()
        {
            World world = Create(enemyCount: 0);
            BattleFinishResult finish = BattleFinishRules.TryFinish(world.Battle, world.Economy,
                world.Board, world.Hand, world.Ownership, world.Effects);
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                finish.NextBattleState, finish.NextEconomyState, "trigger",
                new Dictionary<string, BattleEffectDefinition>());
            Assert.That(effect.Succeeded, Is.False);
            Assert.That(effect.Failure, Is.EqualTo(BattleEffectFailure.BattleAlreadyCompleted));
            Assert.That(effect.NextState, Is.SameAs(finish.NextBattleState));
        }

        [Test]
        public void FinishFailureFromHealthConfigurationMismatchIsAtomic()
        {
            World world = Create(enemyCount: 0);
            EconomyState wrong = new EconomyState(10, 0, 3, 12, 10, 0, 1, 20, 25);
            BattleFinishResult result = BattleFinishRules.TryFinish(world.Battle, wrong,
                world.Board, world.Hand, world.Ownership, world.Effects);
            Assert.That(result.Failure, Is.EqualTo(BattleFinishFailure.InvalidState));
            Assert.That(result.NextBattleState, Is.SameAs(world.Battle));
            Assert.That(result.NextEconomyState, Is.SameAs(wrong));
            Assert.That(result.NextItemEffectState, Is.SameAs(world.Effects));
            Assert.That(result.NextBattleRandomState, Is.EqualTo(world.Battle.BattleRandomState));
        }

        [Test]
        public void SameCheckpointProducesEquivalentFinishedResult()
        {
            World first = Create(enemyCount: 0);
            World second = Create(enemyCount: 0);
            BattleFinishResult left = BattleFinishRules.TryFinish(first.Battle, first.Economy,
                first.Board, first.Hand, first.Ownership, first.Effects);
            BattleFinishResult right = BattleFinishRules.TryFinish(second.Battle, second.Economy,
                second.Board, second.Hand, second.Ownership, second.Effects);
            Assert.That(left.Result.Outcome, Is.EqualTo(right.Result.Outcome));
            Assert.That(left.Result.FinalRound, Is.EqualTo(right.Result.FinalRound));
            Assert.That(left.Result.BattleRandomCheckpoint, Is.EqualTo(right.Result.BattleRandomCheckpoint));
            Assert.That(left.NextBattleState.BattleRandomState, Is.EqualTo(right.NextBattleState.BattleRandomState));
            Assert.That(left.Result.ConfirmedPersistentEffects, Is.Empty);
        }

        private static BattleInitializationResult InitializeExistingCharacter(
            CharacterOwnershipState ownership, BoardState board, EconomyState economy,
            ItemEffectState effects, Pcg32State rng, int maxRounds)
        {
            CharacterDefinition definition = new CharacterDefinition("D1", "Hero", 1,
                Array.Empty<string>(), Array.Empty<string>());
            EnemyBoardState enemies = EnemyBoardState.CreateEmpty();
            EnemyDefinition enemyDefinition = new EnemyDefinition("E1", "Enemy", 1, 0, 1,
                null, null, null, "T001", new[] { "T001" }, null, null);
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            StageProgressState stage = StageProgressRules.BeginBattle(prep).NextState;
            return BattleInitializationRules.Initialize(economy, stage, board,
                HandState.CreateStandard(), ownership, new[] { definition }, enemies,
                new[] { enemyDefinition }, effects, new[] { definition }, new[] { definition },
                rng, new BattleConfig(maxRounds));
        }

        private static CharacterInstanceView Get(CharacterOwnershipState ownership, string id)
        {
            Assert.That(ownership.TryGet(id, out CharacterInstanceView view), Is.True);
            return view;
        }

    }
}
