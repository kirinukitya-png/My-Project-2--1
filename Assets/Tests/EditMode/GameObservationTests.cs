using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class GameObservationTests
    {
        private static StageProgressConfig StageConfig() =>
            new StageProgressConfig(25, 5, 5, 4, 0, 0, 0, 1, 0, 0,
                3, 6, 0, 0, 10, 4, 2, true);

        private static EconomyRulesConfiguration EconomyConfig() =>
            new EconomyRulesConfiguration(4, 2, 1, new[] { 1,2,3,4,5,6,7,8,9,10,11 },
                10, 1, 3, 3, 1, 0);

        private static ObservationSource Source(StageProgressState stage = null,
            EconomyState economy = null, BoardState board = null, HandState hand = null,
            CharacterOwnershipState ownership = null,
            IReadOnlyList<CharacterDefinition> definitions = null,
            IReadOnlyList<CharacterDefinition> selected = null,
            IReadOnlyList<CharacterDefinition> full = null,
            IReadOnlyList<ItemDefinition> items = null,
            ShopState shop = null, EventItemState eventItem = null, IReadOnlyList<EnemyDefinition> enemies = null,
            EnemyBoardState enemyBoard = null, BattleState battle = null, BattleResult result = null,
            CharacterStatDefinition stats = null, CharacterShopConfiguration characterShop = null,
            StageRewardConfiguration rewards = null, int? rerollCost = null,
            StageProgressConfig stageConfig = null)
        {
            StageProgressConfig effectiveStageConfig = stageConfig ?? StageConfig();
            return new ObservationSource(stage ?? StageProgressRules.CreateInitialState(effectiveStageConfig),
                effectiveStageConfig, economy ?? new EconomyState(12, 0, 1, 12, 12, 0, 1, 25, 30),
                EconomyConfig(), board ?? BoardState.CreateStandard(),
                hand ?? HandState.CreateStandard(), ownership ?? new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                definitions ?? Array.Empty<CharacterDefinition>(), selected ?? Array.Empty<CharacterDefinition>(),
                full ?? Array.Empty<CharacterDefinition>(), items ?? Array.Empty<ItemDefinition>(),
                shop, new ItemEffectState(), eventItem, enemies, enemyBoard, null, battle, result,
                stats, characterShop, rewards, rerollCost);
        }

        private static StageProgressState ChoiceState(StageProgressConfig config)
        {
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            for (int i = 0; i < config.RoundsPerStage - 1; i++)
            {
                state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
                state = StageProgressRules.BeginBattle(state).NextState;
                state = StageProgressRules.ResolveBattle(state, config, true).NextState;
                state = StageProgressRules.CompletePendingReward(state, config,
                    new SimulationRandomStreams((ulong)i + 1).Event.CaptureState()).NextState;
            }
            return StageProgressRules.BeginCurrentEncounter(state, config).NextState;
        }

        private static CharacterStatDefinition TestStats() => new CharacterStatDefinition(new[]
        {
            new CharacterCostStats(2,
                new[] { new CharacterStatOption(5, 1, 2), new CharacterStatOption(8, 4, 1), new CharacterStatOption(99, 99, 0) },
                Array.Empty<CharacterStatOption>(), Array.Empty<CharacterStatOption>())
        });

        private static CharacterShopConfiguration TestCharacterShop() => new CharacterShopConfiguration(
            new[] { 6, 6, 6, 6, 6 }, new[] { 15, 15, 15, 15, 15 },
            new[] { new[] { 85, 15, 0, 0, 0 } }, new[] { 3 });

        [Test]
        public void Build_InitialStateCopiesPublicPlayerAndStageValuesWithoutRng()
        {
            var streams = new SimulationRandomStreams(123UL);
            SimulationRandomStreamsState before = streams.CaptureState();
            ObservationBuildResult first = ObservationBuilder.Build(Source(economy: new EconomyState(17, 2, 3, 12, 12, 0, 1, 21, 30)));
            ObservationBuildResult second = ObservationBuilder.Build(Source(economy: new EconomyState(17, 2, 3, 12, 12, 0, 1, 21, 30)));

            Assert.That(first.Succeeded, Is.True, string.Join("; ", first.Errors));
            Assert.That(first.Observation.Player.Gold, Is.EqualTo(17));
            Assert.That(first.Observation.Player.HP, Is.EqualTo(21));
            Assert.That(first.Observation.Player.RequiredXP, Is.EqualTo(3));
            Assert.That(first.Observation.Stage.Phase, Is.EqualTo(ObservationPhase.AwaitingEncounterStart));
            Assert.That(first.Observation.Board.Slots, Has.Count.EqualTo(9));
            Assert.That(first.Observation.Hand.Slots, Has.Count.EqualTo(5));
            Assert.That(first.Observation.IsStrategyActionAllowed, Is.False);
            Assert.That(second.Succeeded, Is.True);
            Assert.That(second.Observation.Player.Gold, Is.EqualTo(first.Observation.Player.Gold));
            Assert.That(second.Observation.Stage.EncounterIndex, Is.EqualTo(first.Observation.Stage.EncounterIndex));
            Assert.That(streams.CaptureState().Shop, Is.EqualTo(before.Shop));
            Assert.That(streams.CaptureState().Battle, Is.EqualTo(before.Battle));
            SimulationRandomStreamsState after = streams.CaptureState();
            Assert.That(after.Encounter, Is.EqualTo(before.Encounter));
            Assert.That(after.Stats, Is.EqualTo(before.Stats));
            Assert.That(after.Event, Is.EqualTo(before.Event));
            Assert.That(after.Roster, Is.EqualTo(before.Roster));
            Assert.That(after.Economy, Is.EqualTo(before.Economy));
        }

        [Test]
        public void Build_PreparationIncludesBoardCharacterAndCurrentShopOnly()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            var definition = new CharacterDefinition("char-1", "Fighter", 1, new[] { "Fire" }, new[] { "Guard" });
            var instance = new CharacterInstance("C1", definition, 2);
            var ownership = new CharacterOwnershipState(new[] { instance });
            var ids = new string[9]; ids[0] = "C1";
            var board = new BoardState(3, 3, ids);
            var shop = new ShopState(ShopKind.Character,
                new[] { new ShopOffer("offer-s1-r1-g0-slot0", ShopOfferKind.Character,
                    "char-1", 2, 2) }, 1, 1, 1);
            ObservationBuildResult built = ObservationBuilder.Build(Source(prep, board: board,
                ownership: ownership, definitions: new[] { definition }, selected: new[] { definition },
                full: new[] { definition }, shop: shop, enemies: Array.Empty<EnemyDefinition>(),
                enemyBoard: EnemyBoardState.CreateEmpty()));

            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            Assert.That(built.Observation.Board.Slots[0].Character.DisplayName, Is.EqualTo("Fighter"));
            Assert.That(built.Observation.Board.Slots[0].Character.InstanceId, Is.EqualTo("C1"));
            Assert.That(built.Observation.Board.Slots[0].Character.Units[0], Is.EqualTo("Guard"));
            Assert.That(built.Observation.Shop.Offers[0].OfferId, Is.EqualTo(shop.Offers[0].OfferId));
            Assert.That(built.Observation.Shop.Offers[0].OfferIndex, Is.EqualTo(0));
            Assert.That(built.Observation.Shop.Offers[0].OfferSlotIndex, Is.EqualTo(0));
            Assert.That(built.Observation.Shop.Offers[0].DisplayName, Is.EqualTo("Fighter"));
            Assert.That(built.Observation.Synergy.IsAvailable, Is.True);
        }

        [Test]
        public void Build_HandItemIncludesDefinitionEffectsAndKeepsSlotOrder()
        {
            ItemDefinition item = new ItemDefinition("item-1", "Potion", 4, "Attack +2",
                ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 2) });
            var slots = new[] { HandSlotContent.Item("item-1"), HandSlotContent.Empty(),
                HandSlotContent.Empty(), HandSlotContent.Empty(), HandSlotContent.Empty() };
            var hand = new HandState(slots);
            ObservationBuildResult built = ObservationBuilder.Build(Source(hand: hand, items: new[] { item }));

            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            Assert.That(built.Observation.Hand.Slots[0].Kind, Is.EqualTo(HandSlotContentKind.Item));
            Assert.That(built.Observation.Hand.Slots[0].Item.DefinitionId, Is.EqualTo("item-1"));
            Assert.That(built.Observation.Hand.Slots[0].Item.Effects[0].Value, Is.EqualTo(2));
            Assert.That(built.Observation.Hand.Slots[1].Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Assert.Throws<NotSupportedException>(() => ((IList<HandSlotObservation>)built.Observation.Hand.Slots)[0] = null);
        }

        [Test]
        public void Build_HidesShopOutsidePreparationAndDoesNotExposePoolOrRandomState()
        {
            var shop = new ShopState(ShopKind.Item,
                new[] { new ShopOffer("item-s1-r1-g0-slot0", ShopOfferKind.Item, "it", 3, null) },
                1, 1, 1);
            ItemDefinition item = new ItemDefinition("it", "Visible item", 3);
            ObservationBuildResult built = ObservationBuilder.Build(Source(shop: shop, items: new[] { item }));

            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            Assert.That(built.Observation.Shop, Is.Null);
            Assert.That(built.Observation.GetType().GetProperty("RandomState"), Is.Null);
            Assert.That(built.Observation.GetType().GetProperty("CharacterPool"), Is.Null);
        }

        [Test]
        public void Build_RejectsInvalidBoardWithoutReturningPartialObservation()
        {
            ObservationBuildResult built = ObservationBuilder.Build(Source(board: BoardState.CreateEmpty(2, 3)));
            Assert.That(built.Succeeded, Is.False);
            Assert.That(built.Observation, Is.Null);
            Assert.That(built.Errors, Is.Not.Empty);
        }

        [Test]
        public void Build_ChoicePhaseExposesChoicesButNotAnUnconfirmedShop()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            Pcg32State eventState = new SimulationRandomStreams(88UL).Event.CaptureState();
            for (int encounter = 0; encounter < 4; encounter++)
            {
                StageProgressResult prep = StageProgressRules.BeginCurrentEncounter(state, config);
                StageProgressResult battle = StageProgressRules.BeginBattle(prep.NextState);
                StageProgressResult reward = StageProgressRules.ResolveBattle(battle.NextState, config, true);
                StageProgressResult next = StageProgressRules.CompletePendingReward(reward.NextState, config, eventState);
                Assert.That(next.Succeeded, Is.True);
                state = next.NextState;
            }
            state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            var unconfirmed = new ShopState(ShopKind.Character,
                new[] { new ShopOffer("x", ShopOfferKind.Character, "c", 1, 1) }, 1, 1, 5);
            ObservationBuildResult built = ObservationBuilder.Build(Source(state, shop: unconfirmed,
                definitions: Array.Empty<CharacterDefinition>()));

            Assert.That(state.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            Assert.That(built.Observation.Stage.AvailableChoices, Has.Count.EqualTo(4));
            Assert.That(built.Observation.Shop, Is.Null);
            Assert.That(built.Observation.IsStrategyActionAllowed, Is.True);
        }
        [Test]
        public void Build_EventPhasePublishesOnlyTheCurrentEventItemAndItsStatus()
        {
            var config = new StageProgressConfig(25, 5, 5, 4, 100, 10, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            StageProgressState battle = StageProgressRules.BeginBattle(prep).NextState;
            StageProgressState reward = StageProgressRules.ResolveBattle(battle, config, true).NextState;
            var streams = new SimulationRandomStreams(5UL);
            StageProgressState eventState = StageProgressRules.CompletePendingReward(
                reward, config, streams.Event.CaptureState()).NextState;
            var eventItem = new EventItemState("event-item");
            ItemDefinition item = new ItemDefinition("event-item", "Event Gift", 0);
            ObservationBuildResult built = ObservationBuilder.Build(Source(eventState,
                items: new[] { item }, eventItem: eventItem));

            Assert.That(eventState.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            Assert.That(built.Observation.EventItem.Item.DisplayName, Is.EqualTo("Event Gift"));
            Assert.That(built.Observation.EventItem.Status, Is.EqualTo(EventItemStatus.Available));
            Assert.That(built.Observation.Shop, Is.Null);
            Assert.That(built.Observation.IsStrategyActionAllowed, Is.True);
        }

        [Test]
        public void Build_DoesNotRetainGameCoreStateReferences()
        {
            var stateTypes = new HashSet<Type>
            {
                typeof(StageProgressState), typeof(EconomyState), typeof(ShopState),
                typeof(BoardState), typeof(HandState), typeof(CharacterOwnershipState),
                typeof(BattleState), typeof(ItemEffectState), typeof(CharacterDefinition),
                typeof(ItemDefinition), typeof(EnemyBoardState)
            };
            foreach (var property in typeof(GameObservation).GetProperties())
                Assert.That(stateTypes.Contains(property.PropertyType), Is.False,
                    "Observation leaks GameCore state through " + property.Name);
        }

        [Test]
        public void Build_CharacterAndTagDataRemainStableAfterSourceInstanceMutation()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            var definition = new CharacterDefinition("char-2", "Mage", 1,
                new[] { "Arcane" }, new[] { "Scholar" });
            var instance = new CharacterInstance("C9", definition, 1);
            instance.TemporaryAttackBonus = 3;
            var owner = new CharacterOwnershipState(new[] { instance });
            var ids = new string[9]; ids[8] = "C9";
            var shop = new ShopState(ShopKind.Character,
                new[] { new ShopOffer("a", ShopOfferKind.Character, "char-2", 1, 1) }, 1, 1, 1);
            var source = Source(prep, board: new BoardState(3, 3, ids), ownership: owner,
                definitions: new[] { definition }, selected: new[] { definition },
                full: new[] { definition }, shop: shop,
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty());
            ObservationBuildResult built = ObservationBuilder.Build(source);
            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));

            instance.TemporaryAttackBonus = 99;
            Assert.That(built.Observation.Board.Slots[8].Character.TemporaryAttackBonus, Is.EqualTo(3));
            Assert.That(built.Observation.Board.Slots[8].Character.Attributes[0], Is.EqualTo("Arcane"));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)built.Observation.Board.Slots[8].Character.Units)[0] = "changed");
        }

        [Test]
        public void Build_ReusesImmutableDefinitionTagsButKeepsOlderObservationDetached()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            var definition = new CharacterDefinition("shared-tags", "Guard", 1,
                new[] { "Earth" }, new[] { "Sentinel" });
            var instance = new CharacterInstance("shared-instance", definition, 1);
            var firstOwnership = new CharacterOwnershipState(new[] { instance });
            var ids = new string[9]; ids[0] = instance.InstanceId;
            var board = new BoardState(3, 3, ids);
            var shop = new ShopState(ShopKind.Character,
                new[] { new ShopOffer("shared-offer", ShopOfferKind.Character,
                    definition.DefinitionId, 1, 1) }, 1, 1, 1);
            ObservationSource firstSource = Source(prep, board: board, ownership: firstOwnership,
                definitions: new[] { definition }, selected: new[] { definition },
                full: new[] { definition }, shop: shop,
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty());

            ObservationBuildResult first = ObservationBuilder.Build(firstSource);
            Assert.That(first.Succeeded, Is.True, string.Join("; ", first.Errors));
            CharacterObservation oldCharacter = first.Observation.Board.Slots[0].Character;
            Assert.That(oldCharacter.Units, Is.SameAs(definition.Units));
            Assert.That(oldCharacter.Attributes, Is.SameAs(definition.Attributes));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)oldCharacter.Units)[0] = "mutated");

            instance.TemporaryAttackBonus = 7;
            var secondOwnership = new CharacterOwnershipState(new[] { instance });
            ObservationBuildResult second = ObservationBuilder.Build(Source(prep, board: board,
                ownership: secondOwnership, definitions: new[] { definition },
                selected: new[] { definition }, full: new[] { definition }, shop: shop,
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty()));

            Assert.That(second.Succeeded, Is.True, string.Join("; ", second.Errors));
            Assert.That(oldCharacter.TemporaryAttackBonus, Is.EqualTo(0),
                "A later source state must not mutate an already returned observation.");
            Assert.That(second.Observation.Board.Slots[0].Character.TemporaryAttackBonus, Is.EqualTo(7));
            Assert.That(oldCharacter.Units[0], Is.EqualTo("Sentinel"));
        }

        [Test]
        public void OwnershipState_ReusesDetachedImmutableViewsForItsLifetime()
        {
            var definition = new CharacterDefinition("view-cache", "Guard", 1,
                Array.Empty<string>(), Array.Empty<string>());
            var source = new CharacterInstance("view-cache-1", definition, 1);
            var ownership = new CharacterOwnershipState(new[] { source });

            Assert.That(ownership.TryGet(source.InstanceId, out CharacterInstanceView first), Is.True);
            Assert.That(ownership.TryGet(source.InstanceId, out CharacterInstanceView second), Is.True);
            Assert.That(second, Is.SameAs(first));
            Assert.That(ownership.Instances[source.InstanceId], Is.SameAs(first));
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, CharacterInstanceView>)ownership.Instances)[source.InstanceId] = null);

            source.TemporaryAttackBonus = 99;
            Assert.That(ownership.TryGet(source.InstanceId, out CharacterInstanceView afterSourceMutation), Is.True);
            Assert.That(afterSourceMutation, Is.SameAs(first));
            Assert.That(afterSourceMutation.TemporaryAttackBonus, Is.EqualTo(0));
        }

        [Test]
        public void Build_CharacterOfferExposesDefinitionTagsRunCostPriceAndPublishedStatRange()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            var definition = new CharacterDefinition("shop-char", "Shop Guard", 1,
                new[] { "Earth" }, new[] { "Sentinel" });
            var shop = new ShopState(ShopKind.Character,
                new[] { new ShopOffer("char-offer", ShopOfferKind.Character, "shop-char", 11, 2) },
                1, 1, 1);
            var built = ObservationBuilder.Build(Source(prep, definitions: new[] { definition },
                selected: new[] { definition }, full: new[] { definition }, shop: shop,
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty(),
                stats: TestStats(), characterShop: TestCharacterShop(), rerollCost: 2));

            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            ShopOfferObservation offer = built.Observation.Shop.Offers[0];
            Assert.That(offer.DefinitionId, Is.EqualTo("shop-char"));
            Assert.That(offer.DisplayName, Is.EqualTo("Shop Guard"));
            Assert.That(offer.RunCost, Is.EqualTo(2));
            Assert.That(offer.DisplayPrice, Is.EqualTo(11));
            Assert.That(offer.Units, Is.EqualTo(new[] { "Sentinel" }));
            Assert.That(offer.Attributes, Is.EqualTo(new[] { "Earth" }));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)offer.Units)[0] = "mutated");
            Assert.That(offer.CharacterStats.IsAvailable, Is.True);
            Assert.That(offer.CharacterStats.MinimumAttack, Is.EqualTo(5));
            Assert.That(offer.CharacterStats.MaximumAttack, Is.EqualTo(8));
            Assert.That(offer.CharacterStats.MinimumDefense, Is.EqualTo(1));
            Assert.That(offer.CharacterStats.MaximumDefense, Is.EqualTo(4));
            Assert.That(built.Observation.Shop.CostWeights.Cost1Weight, Is.EqualTo(85));
            Assert.That(built.Observation.Shop.CostWeights.Cost2Weight, Is.EqualTo(15));
            Assert.That(built.Observation.Shop.CostWeights.TotalConfiguredWeight, Is.EqualTo(100));
            Assert.That(typeof(ShopCostWeightObservation).GetProperty("PoolRemaining"), Is.Null);
        }

        [Test]
        public void Build_ItemOfferReusesPublicEffectViewAndDoesNotExposePoolInformation()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState choice = ChoiceState(config);
            StageProgressState itemPrep = StageSelectionRules.Select(choice, config, StageChoice.SelectB).NextState;
            var item = new ItemDefinition("item-public", "Shield Draught", 5, "Defense -1",
                ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Defense, -1) });
            var shop = new ShopState(ShopKind.Item,
                new[] { new ShopOffer("item-offer", ShopOfferKind.Item, "item-public", 5, null) },
                1, 1, 5);
            var built = ObservationBuilder.Build(Source(itemPrep, items: new[] { item }, shop: shop,
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty(),
                stageConfig: config));

            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            ItemObservation observed = built.Observation.Shop.Offers[0].Item;
            Assert.That(observed.DefinitionId, Is.EqualTo("item-public"));
            Assert.That(observed.PurchasePrice, Is.EqualTo(5));
            Assert.That(observed.Effects[0].Kind, Is.EqualTo(ItemEffectKind.Defense));
            Assert.That(observed.Effects[0].Value, Is.EqualTo(-1));
            Assert.That(observed.Effects[0].Target, Is.EqualTo(ItemEffectTarget.AllEnemies));
            Assert.That(observed.Effects[0].Duration, Is.EqualTo(ItemEffectDuration.ThisBattle));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ItemEffectObservation>)observed.Effects)[0] = null);
            Assert.That(built.Observation.Shop.CostWeights, Is.Null);
        }

        [Test]
        public void Build_EconomyRulesExposeCurrentChoiceCostsAndDeterministicInterestExpectation()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState choice = ChoiceState(config);
            StageProgressState selected = StageSelectionRules.Select(choice, config, StageChoice.SelectB).NextState;
            EconomyState economy = new EconomyState(19, 2, 3, 12, 12, 0, 1, 25, 30);
            SimulationRandomStreams streams = new SimulationRandomStreams(919UL);
            SimulationRandomStreamsState before = streams.CaptureState();
            var built = ObservationBuilder.Build(Source(selected, economy: economy,
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty(),
                rerollCost: 2, stageConfig: config,
                shop: new ShopState(ShopKind.Item, Array.Empty<ShopOffer>(), 3, 1, 5)));

            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            EconomyRulesObservation view = built.Observation.EconomyRules;
            Assert.That(view.ExperiencePurchaseGoldCost, Is.EqualTo(2));
            Assert.That(view.ExperiencePerPurchase, Is.EqualTo(1));
            Assert.That(view.MaximumLevel, Is.EqualTo(12));
            Assert.That(view.CanPurchaseExperience, Is.True);
            Assert.That(view.ShopRerollGoldCost, Is.EqualTo(2));
            Assert.That(view.CanRerollShop, Is.True);
            Assert.That(view.GoldPerInterestThreshold, Is.EqualTo(10));
            Assert.That(view.MaximumInterestGold, Is.EqualTo(3));
            Assert.That(view.ExpectedInterestAtCurrentGold, Is.EqualTo(1.9m));
            Assert.That(streams.CaptureState().Economy, Is.EqualTo(before.Economy));
            Assert.That(streams.CaptureState().Stats, Is.EqualTo(before.Stats));
        }

        [Test]
        public void ExpectedInterestUsesOnlyTheRemainingCapNearAndAtMaximum()
        {
            EconomyRulesConfiguration config = EconomyConfig();

            Assert.That(EconomyRules.CalculateExpectedInterestGold(29, config), Is.EqualTo(2.9m));
            Assert.That(EconomyRules.CalculateExpectedInterestGold(30, config), Is.EqualTo(3m));
            Assert.That(EconomyRules.CalculateExpectedInterestGold(39, config), Is.EqualTo(3m));
        }

        [Test]
        public void Build_ChoiceDetailsSeparateImmediateEffectsFromRewardAndMarkBossOverride()
        {
            var normalConfig = new StageProgressConfig(25, 5, 5, 4, 0, 0, 0,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState normalChoice = ChoiceState(normalConfig);
            var normalBuilt = ObservationBuilder.Build(Source(normalChoice,
                rewards: new StageRewardConfiguration(5, 2), stageConfig: normalConfig));

            Assert.That(normalBuilt.Succeeded, Is.True, string.Join("; ", normalBuilt.Errors));
            Assert.That(normalBuilt.Observation.Stage.ChoiceDetails, Has.Count.EqualTo(4));
            StageChoiceObservation selectD = normalBuilt.Observation.Stage.ChoiceDetails[3];
            Assert.That(selectD.Choice, Is.EqualTo(StageChoice.SelectD));
            Assert.That(selectD.IsAvailable, Is.True);
            Assert.That(selectD.SelectionEffect, Is.EqualTo(StageSelectionEffectKind.HealPlayer));
            Assert.That(selectD.SelectionEffectTiming, Is.EqualTo(StageSelectionEffectTiming.AtSelection));
            Assert.That(selectD.SelectionEffectAmount, Is.EqualTo(10));
            Assert.That(selectD.SelectionEffectCapsAtMaximum, Is.True);
            Assert.That(selectD.PostBattleGold, Is.EqualTo(0));
            Assert.That(selectD.PostBattleXP, Is.EqualTo(0));
            Assert.That(selectD.BossRewardOverridesChoice, Is.False);
            Assert.That(normalBuilt.Observation.Enemies, Is.Empty);
            Assert.That(normalBuilt.Observation.Stage.SelectedChoiceDetails, Is.Null);

            var bossConfig = new StageProgressConfig(25, 5, 5, 5, 0, 0, 0,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState bossChoice = ChoiceState(bossConfig);
            var bossBuilt = ObservationBuilder.Build(Source(bossChoice,
                rewards: new StageRewardConfiguration(5, 2), stageConfig: bossConfig));
            Assert.That(bossBuilt.Succeeded, Is.True, string.Join("; ", bossBuilt.Errors));
            StageChoiceObservation bossSelectD = bossBuilt.Observation.Stage.ChoiceDetails[3];
            Assert.That(bossSelectD.SelectionEffect, Is.EqualTo(StageSelectionEffectKind.None));
            Assert.That(bossSelectD.PostBattleGold, Is.EqualTo(5));
            Assert.That(bossSelectD.PostBattleXP, Is.EqualTo(2));
            Assert.That(bossSelectD.BossRewardOverridesChoice, Is.True);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<StageChoiceObservation>)bossBuilt.Observation.Stage.ChoiceDetails).Clear());
        }

        [Test]
        public void Build_SelectedRosterIsOrderedImmutableAndOmitsUnassignedRunCost()
        {
            var first = new CharacterDefinition("roster-a", "A", 1, new[] { "Wind" }, new[] { "Swift" });
            var second = new CharacterDefinition("roster-b", "B", 5, new[] { "Stone" }, new[] { "Guard" });
            var built = ObservationBuilder.Build(Source(selected: new[] { first, second },
                full: new[] { first, second }));

            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            Assert.That(built.Observation.SelectedRoster, Has.Count.EqualTo(2));
            Assert.That(built.Observation.SelectedRoster[0].DefinitionId, Is.EqualTo("roster-a"));
            Assert.That(built.Observation.SelectedRoster[1].Attributes[0], Is.EqualTo("Stone"));
            Assert.That(built.Observation.SelectedRoster[1].Units[0], Is.EqualTo("Guard"));
            Assert.That(typeof(RosterDefinitionObservation).GetProperty("RunCost"), Is.Null);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<RosterDefinitionObservation>)built.Observation.SelectedRoster)[0] = null);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)built.Observation.SelectedRoster[0].Units)[0] = "changed");
        }
    }
}

