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
            EnemyBoardState enemyBoard = null, BattleState battle = null, BattleResult result = null)
        {
            return new ObservationSource(stage ?? StageProgressRules.CreateInitialState(StageConfig()),
                StageConfig(), economy ?? new EconomyState(12, 0, 1, 12, 12, 0, 1, 25, 30),
                EconomyConfig(), board ?? BoardState.CreateStandard(),
                hand ?? HandState.CreateStandard(), ownership ?? new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                definitions ?? Array.Empty<CharacterDefinition>(), selected ?? Array.Empty<CharacterDefinition>(),
                full ?? Array.Empty<CharacterDefinition>(), items ?? Array.Empty<ItemDefinition>(),
                shop, new ItemEffectState(), eventItem, enemies, enemyBoard, null, battle, result);
        }

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
    }
}

