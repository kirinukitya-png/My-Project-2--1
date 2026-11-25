using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameSessionTests
    {
        private static readonly CharacterDefinition[] Roster = Enumerable.Range(0, 30)
            .Select(i => new CharacterDefinition("ch" + i, "Character " + i, 1,
                Array.Empty<string>(), Array.Empty<string>())).ToArray();
        private static readonly EnemyDefinition Enemy = new EnemyDefinition("enemy", "Enemy", 4, 1, 6,
            null, null, null, "T001", null, null, null);
        private static readonly ItemDefinition BlockItem = new ItemDefinition("block", "Block", 2,
            string.Empty, ItemEffectTarget.Player, ItemEffectDuration.ThisBattle,
            new[] { new ItemEffectDefinition(ItemEffectKind.Block, 2) });

        private static EconomyRulesConfiguration EconomyConfig() => new EconomyRulesConfiguration(
            4, 2, 1, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 10, 1, 3, 3, 1, 0);

        private static StageProgressConfig StageConfig(int choiceRound = 2, int eventChance = 0) =>
            new StageProgressConfig(12, 4, choiceRound, 4, eventChance, 10, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static CharacterShopConfiguration ShopConfig() => new CharacterShopConfiguration(
            new[] { 6, 6, 6, 6, 6 }, new[] { 15, 15, 15, 15, 15 },
            new[] { new[] { 100, 0, 0, 0, 0 }, new[] { 20, 50, 25, 3, 2 },
                new[] { 10, 20, 50, 15, 5 } }, new[] { 3, 3, 3 });

        private static CharacterStatDefinition StatConfig() => new CharacterStatDefinition(
            Enumerable.Range(1, 5).Select(cost => new CharacterCostStats(cost,
                new[] { new CharacterStatOption(5 + cost, 1 + cost, 1) },
                new[] { new CharacterStatOption(7 + cost, 2 + cost, 1) },
                new[] { new CharacterStatOption(9 + cost, 3 + cost, 1) })));

        private static IReadOnlyList<EncounterPoolDefinition> EncounterPools(EnemyDefinition enemy = null)
        {
            enemy = enemy ?? Enemy;
            var result = new List<EncounterPoolDefinition>();
            foreach (StageEnemyCategory category in new[] { StageEnemyCategory.Normal, StageEnemyCategory.Hard, StageEnemyCategory.Boss })
            for (int round = 0; round < 12; round++)
            {
                var formation = new EnemyFormationDefinition("f-" + category + "-" + round, new[]
                {
                    new EnemyFormationSlotDefinition(enemy.DefinitionId, 0, 0),
                    new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0),
                    new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0),
                    new EnemyFormationSlotDefinition(null, 0, 0)
                });
                result.Add(new EncounterPoolDefinition("p-" + category + "-" + round,
                    new[] { round }, category, new[] { formation }));
            }
            return result;
        }

        private static EconomyState StartingEconomy() => new EconomyState(7, 0, 1, 12, 7, 0, 1, 30, 30);

        private static GameSessionConfiguration Configuration(int choiceRound = 2,
            IEnumerable<CharacterDefinition> characters = null, IEnumerable<CharacterDefinition> selected = null,
            IEnumerable<CharacterDefinition> fullRoster = null, int boardColumns = 3, int boardRows = 3,
            int handSlots = 5, int eventChance = 0, EnemyDefinition enemy = null,
            IEnumerable<BattleTargetDefinition> targets = null,
            IEnumerable<BattleEffectDefinition> effects = null, int maxBattleRounds = 5,
            IEnumerable<ItemDefinition> items = null)
        {
            CharacterDefinition[] definitions = (characters ?? Roster).ToArray();
            CharacterDefinition[] selectedRoster = (selected ?? Roster).ToArray();
            CharacterDefinition[] completeRoster = (fullRoster ?? Roster).ToArray();
            EnemyDefinition selectedEnemy = enemy ?? Enemy;
            BattleTargetDefinition[] targetDefinitions = (targets ?? new[]
            {
                new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.Uniform)
            }).ToArray();
            return new GameSessionConfiguration(StageConfig(choiceRound, eventChance), EconomyConfig(),
                ShopConfig(), StatConfig(), new StageRewardConfiguration(5, 2), new BattleConfig(maxBattleRounds),
                StartingEconomy(), definitions, selectedRoster, completeRoster, items ?? new[] { BlockItem },
                new[] { selectedEnemy }, EncounterPools(selectedEnemy), 1, boardColumns, boardRows, handSlots, "C",
                battleTargetDefinitions: targetDefinitions, battleEffectDefinitions: effects);
        }

        private static GameSession Create(int choiceRound = 2, ulong seed = 99, int eventChance = 0) =>
            GameSession.Create(Configuration(choiceRound: choiceRound, eventChance: eventChance), seed);

        private static ActionDispatchResult Execute(GameSession session, GameAction action)
        {
            ActionDispatchResult result = session.ExecuteAction(action);
            Assert.That(result, Is.Not.Null);
            return result;
        }

        [Test]
        public void CreateBuildsInitialPlayerBoardHandRosterStockShopAndEncounter()
        {
            GameSession session = Create(seed: 1001);
            ActionExecutionState state = session.CurrentState;
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(state.Economy.Gold, Is.EqualTo(7));
            Assert.That(state.Economy.CurrentHP, Is.EqualTo(30));
            Assert.That(state.Economy.MaximumHP, Is.EqualTo(30));
            Assert.That(state.Economy.Level, Is.EqualTo(1));
            Assert.That(state.Economy.XP, Is.EqualTo(0));
            Assert.That(state.Board.Columns, Is.EqualTo(3));
            Assert.That(state.Board.Rows, Is.EqualTo(3));
            Assert.That(state.Hand.SlotCount, Is.EqualTo(5));
            Assert.That(state.Ownership.GetOwnedInstances(), Is.Empty);
            Assert.That(state.CharacterPool.Entries.Count, Is.EqualTo(30));
            Assert.That(state.CharacterPool.Entries.Values.All(x => x.InitialStockLimit == 15 &&
                x.RemainingCopies + x.ReservedCopies + x.OwnedCopies == 15), Is.True);
            Assert.That(state.Shop.Kind, Is.EqualTo(ShopKind.Character));
            Assert.That(state.Shop.SlotCount, Is.EqualTo(3));
            Assert.That(state.Shop.Offers.Count, Is.EqualTo(3));
            Assert.That(state.EnemyBoard, Is.Not.Null);
            Assert.That(Enumerable.Range(0, state.EnemyBoard.SlotCount)
                .Count(i => state.EnemyBoard.TryGetAt(i, out EnemyInstance enemy) && enemy != null), Is.EqualTo(1));
            AssignedCharacterCosts assigned = CharacterCostAssigner.Assign(Roster, ShopConfig(),
                new SimulationRandomStreams(1001).Roster);
            Assert.That(assigned.Costs.Values.GroupBy(x => x).Select(group => group.Count()).OrderBy(x => x),
                Is.EqualTo(new[] { 6, 6, 6, 6, 6 }));
            Assert.That(state.RandomState.Version, Is.EqualTo(2));
        }

        [Test]
        public void InitialChoiceWaitDoesNotGenerateShopOrEncounterAheadOfSelection()
        {
            const ulong seed = 2002;
            GameSession session = Create(choiceRound: 1, seed: seed);
            ActionExecutionState state = session.CurrentState;
            SimulationRandomStreamsState initial = new SimulationRandomStreams(seed).CaptureState();
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            Assert.That(state.Shop, Is.Null);
            Assert.That(state.EnemyBoard, Is.Null);
            Assert.That(state.RandomState.Shop, Is.EqualTo(initial.Shop));
            Assert.That(state.RandomState.Encounter, Is.EqualTo(initial.Encounter));
            Assert.That(state.RandomState.Roster, Is.Not.EqualTo(initial.Roster));
            Assert.That(session.GetObservation().Succeeded, Is.True);
        }

        [Test]
        public void SameSeedRecreatesAllInitialCheckpointsAndOfferOrdering()
        {
            GameSession first = Create(seed: 3003);
            GameSession second = Create(seed: 3003);
            Assert.That(first.CurrentState.RandomState.Shop, Is.EqualTo(second.CurrentState.RandomState.Shop));
            Assert.That(first.CurrentState.RandomState.Roster, Is.EqualTo(second.CurrentState.RandomState.Roster));
            Assert.That(first.CurrentState.RandomState.Encounter, Is.EqualTo(second.CurrentState.RandomState.Encounter));
            Assert.That(first.CurrentState.Shop.Offers.Select(x => x.DefinitionId),
                Is.EqualTo(second.CurrentState.Shop.Offers.Select(x => x.DefinitionId)));
            Assert.That(first.CurrentState.CharacterPool.Entries.Select(x =>
                x.Key + ":" + x.Value.RemainingCopies + ":" + x.Value.ReservedCopies),
                Is.EquivalentTo(second.CurrentState.CharacterPool.Entries.Select(x =>
                    x.Key + ":" + x.Value.RemainingCopies + ":" + x.Value.ReservedCopies)));
        }

        [Test]
        public void InitialStateAndRngAreIndependentBetweenTrials()
        {
            GameSession first = Create(seed: 4004);
            GameSession second = Create(seed: 4005);
            ActionExecutionState untouched = second.CurrentState;
            ActionDispatchResult result = Execute(first, new BuyExperienceAction(new ActionId("trial-a/xp")));
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(first.CurrentState.Economy.XP, Is.EqualTo(0));
            Assert.That(first.CurrentState.Economy.Level, Is.EqualTo(2));
            Assert.That(second.CurrentState, Is.SameAs(untouched));
            Assert.That(second.CurrentState.Economy.XP, Is.EqualTo(0));
            Assert.That(first.CurrentState.RandomState.TrialSeed, Is.Not.EqualTo(second.CurrentState.RandomState.TrialSeed));
        }

        [Test]
        public void ConfigurationRejectsDuplicateDefinitionsAndRosterDistributionMismatch()
        {
            CharacterDefinition[] duplicate = Roster.Concat(new[] { Roster[0] }).ToArray();
            GameSessionConfiguration duplicateConfig = Configuration(characters: duplicate);
            Assert.Throws<ArgumentException>(() => GameSession.Create(duplicateConfig, 5));

            CharacterDefinition[] shortRoster = Roster.Take(29).ToArray();
            GameSessionConfiguration rosterConfig = Configuration(characters: Roster,
                selected: shortRoster, fullRoster: Roster);
            Assert.Throws<ArgumentException>(() => GameSession.Create(rosterConfig, 5));

            Assert.Throws<ArgumentOutOfRangeException>(() => Configuration(boardColumns: 0));
        }

        [Test]
        public void RestoreRejectsPoolThatDoesNotMatchConfiguredStockLimits()
        {
            GameSession session = Create(seed: 5005);
            CharacterPoolEntry[] entries = session.CurrentState.CharacterPool.Entries.Values.ToArray();
            CharacterPoolEntry first = entries[0];
            entries[0] = new CharacterPoolEntry(first.DefinitionId, first.RemainingCopies + 1,
                first.InitialStockLimit + 1, first.ReservedCopies, first.OwnedCopies);
            ActionExecutionState inconsistent = session.CurrentState.With(characterPool: new CharacterPool(entries));
            Assert.Throws<ArgumentException>(() => GameSession.Restore(Configuration(), inconsistent));
            Assert.That(session.CurrentState.CharacterPool.Entries[first.DefinitionId].InitialStockLimit, Is.EqualTo(15));
        }

        [Test]
        public void CurrentStateExposureIsReadOnlyAndFactoryIsReturnedAsFork()
        {
            GameSession session = Create();
            ActionExecutionState checkpoint = session.CurrentState;
            long nextId = checkpoint.InstanceFactory.LastIssuedNumber;
            checkpoint.InstanceFactory.Create(Roster[0], 1);
            Assert.That(session.CurrentState.InstanceFactory.LastIssuedNumber, Is.EqualTo(nextId));
            Assert.That(session.CurrentState, Is.SameAs(checkpoint));
            Assert.That(session.GetObservation().Succeeded, Is.True);
        }

        [Test]
        public void ExecuteActionAdoptsOnlySuccessfulDispatcherStateAndKeepsRejectedIdRetryable()
        {
            GameSession session = Create();
            ActionExecutionState before = session.CurrentState;
            ActionDispatchResult rejected = Execute(session, new SellCharacterAction(new ActionId("retry-sale"), "missing-instance"));
            Assert.That(rejected.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(rejected.NextState, Is.SameAs(before));
            Assert.That(session.CurrentState, Is.SameAs(before));
            Assert.That(session.CurrentState.HasExecuted(new ActionId("retry-sale")), Is.False);

            ActionDispatchResult retried = Execute(session, new BuyExperienceAction(new ActionId("retry-sale")));
            Assert.That(retried.Succeeded, Is.True, retried.Error.Detail);
            Assert.That(session.CurrentState, Is.SameAs(retried.NextState));
            Assert.That(session.CurrentState.HasExecuted(new ActionId("retry-sale")), Is.True);
            Assert.That(session.CurrentState.Economy.XP, Is.EqualTo(0));
            Assert.That(session.CurrentState.Economy.Level, Is.EqualTo(2));

            ActionExecutionState after = session.CurrentState;
            ActionDispatchResult duplicate = Execute(session, new BuyExperienceAction(new ActionId("retry-sale")));
            Assert.That(duplicate.Error.Code, Is.EqualTo(ActionErrorCode.DuplicateAction));
            Assert.That(session.CurrentState, Is.SameAs(after));
        }

        [Test]
        public void SessionUsesAllRegisteredHandlersWithoutDependingOnRegistrationOrder()
        {
            IReadOnlyList<IActionHandler> handlers = AllActionHandlers.Create();
            Assert.That(handlers.Count, Is.EqualTo(15));
            Assert.That(handlers.Select(x => x.Kind).Distinct().Count(), Is.EqualTo(15));
            Assert.That(handlers.Select(x => x.Kind), Is.EquivalentTo(Enum.GetValues(typeof(GameActionKind))));
            var dispatcher = new ActionDispatcher(handlers.Reverse());
            GameSession session = GameSession.Create(Configuration(), 77, dispatcher);
            ActionDispatchResult result = Execute(session, new BuyExperienceAction(new ActionId("order-test")));
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
        }

        [Test]
        public void StageChoiceActionCreatesOnlySelectedShopAndCurrentEncounter()
        {
            GameSession session = Create(choiceRound: 1, seed: 808);
            SimulationRandomStreamsState before = session.CurrentState.RandomState;
            ActionDispatchResult result = Execute(session,
                new SelectStageChoiceAction(new ActionId("choose-a"), StageChoice.SelectA));
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(session.CurrentState.Shop.Kind, Is.EqualTo(ShopKind.Character));
            Assert.That(session.CurrentState.Shop.Offers.Count, Is.EqualTo(3));
            Assert.That(session.CurrentState.EnemyBoard, Is.Not.Null);
            Assert.That(session.CurrentState.RandomState.Shop, Is.Not.EqualTo(before.Shop));
            Assert.That(session.CurrentState.RandomState.Encounter, Is.Not.EqualTo(before.Encounter));
            Assert.That(session.CurrentState.RandomState.Battle, Is.EqualTo(before.Battle));
        }

        [Test]
        public void CharacterPurchaseMoveSaleAndRerollFlowThroughSessionDispatcher()
        {
            GameSession session = Create(seed: 9009);
            string offerId = session.CurrentState.Shop.Offers.First(x => !x.IsEmpty).OfferId;
            ActionDispatchResult bought = Execute(session, new BuyCharacterAction(new ActionId("buy-char"),
                offerId, new GameActionLocation(CharacterLocationKind.Hand, 0)));
            Assert.That(bought.Succeeded, Is.True, bought.Error.Detail);
            Assert.That(session.CurrentState.Ownership.GetOwnedInstances().Count, Is.EqualTo(1));
            Assert.That(session.CurrentState.CharacterPool.Entries.Values.Sum(x => x.OwnedCopies), Is.EqualTo(1));
            Assert.That(session.CurrentState.Economy.Gold, Is.EqualTo(6));

            ActionDispatchResult moved = Execute(session, new MoveHandToBoardAction(new ActionId("move-to-board"), 0, 0));
            Assert.That(moved.Succeeded, Is.True, moved.Error.Detail);
            string instanceId = session.CurrentState.Board.InstanceIds[0];
            Assert.That(instanceId, Is.Not.Null);

            ActionDispatchResult movedBack = Execute(session, new MoveBoardToHandAction(new ActionId("move-to-hand"), 0, 0));
            Assert.That(movedBack.Succeeded, Is.True, movedBack.Error.Detail);
            ActionDispatchResult sold = Execute(session, new SellCharacterAction(new ActionId("sell-char"), instanceId));
            Assert.That(sold.Succeeded, Is.True, sold.Error.Detail);
            Assert.That(session.CurrentState.Ownership.GetOwnedInstances(), Is.Empty);
            Assert.That(session.CurrentState.CharacterPool.Entries.Values.Sum(x => x.OwnedCopies), Is.EqualTo(0));
            Assert.That(session.CurrentState.Economy.Gold, Is.EqualTo(7));

            Pcg32State beforeReroll = session.CurrentState.RandomState.Shop;
            ActionDispatchResult reroll = Execute(session, new RerollShopAction(new ActionId("reroll"), ShopKind.Character));
            Assert.That(reroll.Succeeded, Is.True, reroll.Error.Detail);
            Assert.That(session.CurrentState.RandomState.Shop, Is.Not.EqualTo(beforeReroll));
        }

        [Test]
        public void SelectBItemPurchaseUseAndSaleKeepCharacterPoolAndOtherStreamsStable()
        {
            GameSession session = Create(choiceRound: 1, seed: 1010);
            ActionDispatchResult choose = Execute(session,
                new SelectStageChoiceAction(new ActionId("choose-b"), StageChoice.SelectB));
            Assert.That(choose.Succeeded, Is.True, choose.Error.Detail);
            Assert.That(session.CurrentState.Shop.Kind, Is.EqualTo(ShopKind.Item));
            string itemOffer = session.CurrentState.Shop.Offers[0].OfferId;
            CharacterPool poolBefore = session.CurrentState.CharacterPool;
            SimulationRandomStreamsState randomBeforeBuy = session.CurrentState.RandomState;

            ActionDispatchResult buy = Execute(session, new BuyItemAction(new ActionId("buy-item"), itemOffer, 0));
            Assert.That(buy.Succeeded, Is.True, buy.Error.Detail);
            Assert.That(session.CurrentState.Economy.Gold, Is.EqualTo(5));
            Assert.That(session.CurrentState.CharacterPool, Is.SameAs(poolBefore));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(randomBeforeBuy));

            ActionDispatchResult use = Execute(session, new UseHandItemAction(new ActionId("use-item"), 0,
                GameActionTarget.FromItemUseTarget(ItemUseTarget.PlayerArea(0))));
            Assert.That(use.Succeeded, Is.True, use.Error.Detail);
            Assert.That(session.CurrentState.Hand.EmptySlotCount, Is.EqualTo(5));
            Assert.That(session.CurrentState.ItemEffects.TemporaryPlayerBlockBonus, Is.EqualTo(2));
            Assert.That(session.CurrentState.CharacterPool, Is.SameAs(poolBefore));

            GameSession sellSession = Create(choiceRound: 1, seed: 1010);
            Assert.That(Execute(sellSession, new SelectStageChoiceAction(new ActionId("sell-b"), StageChoice.SelectB)).Succeeded, Is.True);
            ActionExecutionState beforeSale = sellSession.CurrentState;
            ActionDispatchResult purchaseForSale = Execute(sellSession,
                new BuyItemAction(new ActionId("buy-for-sale"), beforeSale.Shop.Offers[0].OfferId, 0));
            Assert.That(purchaseForSale.Succeeded, Is.True, purchaseForSale.Error.Detail);
            int goldBeforeSale = sellSession.CurrentState.Economy.Gold;
            ActionDispatchResult sale = Execute(sellSession, new SellItemAction(new ActionId("sell-item"), 0));
            Assert.That(sale.Succeeded, Is.True, sale.Error.Detail);
            Assert.That(sellSession.CurrentState.Economy.Gold, Is.EqualTo(goldBeforeSale));
            Assert.That(sellSession.CurrentState.CharacterPool, Is.SameAs(beforeSale.CharacterPool));
        }

        [Test]
        public void StartBattleActionTransitionsPhaseButSessionDoesNotAdvanceBattleOrRewards()
        {
            GameSession session = Create(seed: 1111);
            string offer = session.CurrentState.Shop.Offers.First(x => !x.IsEmpty).OfferId;
            ActionDispatchResult bought = Execute(session, new BuyCharacterAction(new ActionId("battle-buy"),
                offer, new GameActionLocation(CharacterLocationKind.Board, 0)));
            Assert.That(bought.Succeeded, Is.True, bought.Error.Detail);
            ActionDispatchResult started = Execute(session, new StartBattleAction(new ActionId("start-battle")));
            Assert.That(started.Succeeded, Is.True, started.Error.Detail);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Battle));
            Assert.That(session.CurrentState.Battle, Is.Not.Null);
            Assert.That(session.CurrentState.BattleResult, Is.Null);
            BattlePhase battlePhase = session.CurrentState.Battle.Phase;
            int round = session.CurrentState.Battle.CurrentRound;
            ActionExecutionState beforeRejected = session.CurrentState;
            ActionDispatchResult rejected = Execute(session, new BuyExperienceAction(new ActionId("battle-xp")));
            Assert.That(rejected.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(session.CurrentState, Is.SameAs(beforeRejected));
            Assert.That(session.CurrentState.Battle.Phase, Is.EqualTo(battlePhase));
            Assert.That(session.CurrentState.Battle.CurrentRound, Is.EqualTo(round));
        }

        [Test]
        public void ObservationIsRepeatablePublicAndDetachedFromGameRngAndPool()
        {
            GameSession session = Create(seed: 1212);
            SimulationRandomStreamsState rng = session.CurrentState.RandomState;
            ObservationBuildResult first = session.GetObservation();
            ObservationBuildResult second = session.GetObservation();
            Assert.That(first.Succeeded, Is.True, string.Join(";", first.Errors));
            Assert.That(second.Succeeded, Is.True, string.Join(";", second.Errors));
            Assert.That(first.Observation.Stage.EncounterIndex, Is.EqualTo(second.Observation.Stage.EncounterIndex));
            Assert.That(first.Observation.Player.Gold, Is.EqualTo(second.Observation.Player.Gold));
            Assert.That(first.Observation.Shop.Offers.Count, Is.EqualTo(second.Observation.Shop.Offers.Count));
            Assert.That(first.Observation.GetType().GetProperty("RandomState"), Is.Null);
            Assert.That(first.Observation.GetType().GetProperty("CharacterPool"), Is.Null);
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(rng));
        }

        [Test]
        public void RestoredSessionObservationPublishesConfiguredDetailsWithoutAdvancingCheckpoint()
        {
            GameSession original = Create(seed: 1414);
            ActionExecutionState checkpoint = original.CaptureCheckpoint();
            GameSession restored = GameSession.Restore(Configuration(), checkpoint);
            SimulationRandomStreamsState before = restored.CurrentState.RandomState;

            ObservationBuildResult built = restored.GetObservation();

            Assert.That(built.Succeeded, Is.True, string.Join(";", built.Errors));
            Assert.That(built.Observation.SelectedRoster, Has.Count.EqualTo(30));
            Assert.That(built.Observation.Shop.CostWeights.Cost1Weight, Is.EqualTo(100));
            ShopOfferObservation characterOffer = built.Observation.Shop.Offers
                .First(offer => offer.Kind == ShopOfferKind.Character && offer.Status == ShopOfferStatus.Available);
            Assert.That(characterOffer.CharacterStats.IsAvailable, Is.True);
            Assert.That(built.Observation.EconomyRules.ExpectedInterestAtCurrentGold, Is.EqualTo(0.7m));
            Assert.That(restored.CurrentState.RandomState, Is.EqualTo(before));
            Assert.That(checkpoint.RandomState, Is.EqualTo(before));
        }

        [Test]
        public void RestorePreservesRngAndActionHistoryAndReplaysSameActionResult()
        {
            GameSession original = Create(seed: 1313);
            ActionExecutionState checkpoint = original.CaptureCheckpoint();
            GameSession restored = GameSession.Restore(Configuration(), checkpoint);
            ActionExecutionState sourceBefore = original.CurrentState;

            ActionDispatchResult first = Execute(original, new BuyExperienceAction(new ActionId("replay-xp")));
            ActionDispatchResult replay = Execute(restored, new BuyExperienceAction(new ActionId("replay-xp")));
            Assert.That(first.Succeeded, Is.True, first.Error.Detail);
            Assert.That(replay.Succeeded, Is.True, replay.Error.Detail);
            Assert.That(replay.NextState.Economy.Gold, Is.EqualTo(first.NextState.Economy.Gold));
            Assert.That(replay.NextState.Economy.XP, Is.EqualTo(first.NextState.Economy.XP));
            Assert.That(replay.NextState.RandomState, Is.EqualTo(first.NextState.RandomState));
            Assert.That(replay.NextState.ExecutedActionIds, Is.EqualTo(first.NextState.ExecutedActionIds));
            Assert.That(original.CurrentState, Is.SameAs(first.NextState));
            Assert.That(checkpoint.Economy.XP, Is.EqualTo(0));
            Assert.That(sourceBefore.Economy.XP, Is.EqualTo(0));
            Assert.That(sourceBefore, Is.Not.SameAs(original.CurrentState));
        }

        [Test]
        public void RestoreRejectsBattlePhaseWithoutBattleStateAndKeepsCheckpointImmutable()
        {
            GameSession session = Create();
            StageProgressResult begun = StageProgressRules.BeginBattle(session.CurrentState.Stage);
            Assert.That(begun.Succeeded, Is.True);
            ActionExecutionState inconsistent = session.CurrentState.With(stage: begun.NextState);
            Assert.Throws<ArgumentException>(() => GameSession.Restore(Configuration(), inconsistent));
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            Assert.That(session.CurrentState.Battle, Is.Null);
        }

        [Test]
        public void GameSessionHasNoStrategyStateOrStrategyRandomCheckpointFields()
        {
            Type type = typeof(GameSession);
            Assert.That(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(x => x.FieldType == typeof(StrategyState) || x.FieldType == typeof(StrategyRngState)), Is.False);
            Assert.That(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(x => x.PropertyType == typeof(StrategyState) || x.PropertyType == typeof(StrategyRngState)), Is.False);
        }

        [Test]
        public void EventPhaseCanBeRestoredAndEndedButIsNotAutomaticallyAdvanced()
        {
            GameSessionConfigForEvent(out GameSessionConfiguration configuration, out ActionExecutionState eventCheckpoint);
            GameSession session = GameSession.Restore(configuration, eventCheckpoint);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            ActionDispatchResult ended = Execute(session, new EndEventAction(new ActionId("end-event")));
            Assert.That(ended.Succeeded, Is.True, ended.Error.Detail);
            Assert.That(session.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(session.CurrentState.Stage.EncounterIndex, Is.EqualTo(eventCheckpoint.Stage.EncounterIndex));
        }

        private static void GameSessionConfigForEvent(out GameSessionConfiguration configuration,
            out ActionExecutionState checkpoint)
        {
            configuration = Configuration(choiceRound: 2, eventChance: 100);
            GameSession prep = GameSession.Create(configuration, 1515);
            StageProgressState battle = StageProgressRules.BeginBattle(prep.CurrentState.Stage).NextState;
            StageProgressState pending = StageProgressRules.ResolveBattle(battle, configuration.StageConfig, true).NextState;
            StageProgressState eventStage = StageProgressRules.CompletePendingReward(pending,
                configuration.StageConfig, new SimulationRandomStreams(prep.CurrentState.RandomState.TrialSeed).Event.CaptureState()).NextState;
            var eventItem = new EventItemState(BlockItem.DefinitionId);
            checkpoint = new ActionExecutionState(eventStage, prep.CurrentState.Economy,
                prep.CurrentState.CharacterPool, prep.CurrentState.InstanceFactory,
                prep.CurrentState.Board, prep.CurrentState.Hand, prep.CurrentState.Ownership,
                prep.CurrentState.ItemEffects, prep.CurrentState.RandomState,
                prep.CurrentState.Shop, eventItem, prep.CurrentState.EnemyBoard,
                prep.CurrentState.EnemyPreparation, null, null, prep.CurrentState.ExecutedActionIds);
        }
    }
}
