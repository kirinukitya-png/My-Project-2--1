using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class RandomStrategyTests
    {
        private const string Sid = RandomStrategy.StableId, Ver = RandomStrategy.StableVersion;
        private static StageProgressConfig Config(int choiceRound = 5, int eventChance = 0) =>
            new StageProgressConfig(25, 5, choiceRound, 4, eventChance, 0, eventChance, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
        private static EconomyRulesConfiguration EconConfig() => new EconomyRulesConfiguration(4, 2, 1,
            new[] { 1,2,3,4,5,6,7,8,9,10,11 }, 10, 1, 3, 3, 1, 0);
        private static EconomyState Econ(int gold = 20, int level = 1, int hp = 20) => new EconomyState(gold, 0, level, 12, 0, 1, 1, hp, 30);
        private static CharacterDefinition Def(string id) => new CharacterDefinition(id, id, 1, Array.Empty<string>(), Array.Empty<string>());
        private static CharacterInstance Inst(string id, CharacterDefinition definition) => new CharacterInstance(id, definition, 1, 1);
        private static ItemDefinition Item(string id, ItemEffectTarget target, params ItemEffectDefinition[] effects) =>
            new ItemDefinition(id, id, 0, id, target, ItemEffectDuration.Immediate, effects);
        private static StageProgressState Prep(StageProgressConfig c = null)
        {
            c = c ?? Config();
            StageProgressState initial = StageProgressRules.CreateInitialState(c);
            StageProgressState current = StageProgressRules.BeginCurrentEncounter(initial, c).NextState;
            if (current.Phase == StageProgressPhase.AwaitingStageChoice)
                current = StageSelectionRules.Select(current, c, StageChoice.SelectA).NextState;
            return current;
        }
        private static StageProgressState Choice(StageProgressConfig c = null)
        {
            c = c ?? Config(choiceRound: 1);
            return StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(c), c).NextState;
        }
        private static GameObservation Build(StageProgressState stage, EconomyState economy = null,
            BoardState board = null, HandState hand = null, CharacterInstance[] owned = null,
            CharacterDefinition[] definitions = null, ShopState shop = null, ItemDefinition[] items = null,
            EventItemState eventItem = null, EnemyBoardState enemyBoard = null, EnemyDefinition[] enemyDefs = null)
        {
            owned = owned ?? Array.Empty<CharacterInstance>(); definitions = definitions ?? Array.Empty<CharacterDefinition>();
            items = items ?? Array.Empty<ItemDefinition>(); enemyDefs = enemyDefs ?? Array.Empty<EnemyDefinition>();
            StageProgressConfig observationConfig = Config(stage.Phase == StageProgressPhase.AwaitingStageChoice ? 1 : 5);
            if (shop == null && stage.Phase == StageProgressPhase.Preparation)
            {
                ShopKind kind = stage.CurrentSelection.ShopMode == StageShopMode.Item ? ShopKind.Item : ShopKind.Character;
                shop = new ShopState(kind, Array.Empty<ShopOffer>(), 0, observationConfig.GetStageNumber(stage.EncounterIndex), observationConfig.GetRoundNumber(stage.EncounterIndex));
            }
            var result = ObservationBuilder.Build(new ObservationSource(stage, observationConfig,
                economy ?? Econ(), EconConfig(), board ?? BoardState.CreateStandard(), hand ?? HandState.CreateStandard(),
                new CharacterOwnershipState(owned), definitions, definitions, definitions, items, shop,
                new ItemEffectState(), eventItem, enemyDefs, enemyBoard ?? EnemyBoardState.CreateEmpty()));
            Assert.That(result.Succeeded, Is.True, string.Join(";", result.Errors));
            return result.Observation;
        }
        private static StrategyContext Context(StrategyRngState rng, string actionId = "0/a0", StrategyState state = null, long step = 0) =>
            new StrategyContext(Sid, Ver, 0, step, new ActionId(actionId), state ?? StrategyState.EmptyFor(Sid, Ver), rng);
        private static StrategyRngState Rng(ulong seed) => StrategyRandomSource.Create(seed, Sid, Ver).CaptureState();

        [Test]
        public void RandomStrategy_HasStableMetadataAndOnlyAvailableStageChoices()
        {
            var strategy = new RandomStrategy(); var obs = Build(Choice());
            Assert.That(strategy.StrategyId, Is.EqualTo("RandomStrategy")); Assert.That(strategy.Version, Is.EqualTo("4"));
            var candidates = strategy.GetCandidates(obs, new ActionId("choice"));
            Assert.That(candidates.Select(a => ((SelectStageChoiceAction)a).Choice), Is.EqualTo(new[] { StageChoice.SelectA, StageChoice.SelectB, StageChoice.SelectC, StageChoice.SelectD }));
            var context = Context(Rng(3), "choice"); var decision = strategy.ChooseAction(obs, context);
            Assert.That(decision.Action, Is.TypeOf<SelectStageChoiceAction>()); Assert.That(decision.Action.ActionId, Is.EqualTo(context.NextActionId));
            Assert.That(context.Accepts(decision), Is.True);
        }

        [Test]
        public void CandidateGeneration_FiltersPurchaseCostAndUsesOnlyAvailableOffersAndEmptySlots()
        {
            CharacterDefinition def = Def("c");
            var offers = new[] { new ShopOffer("z", ShopOfferKind.Character, "c", 2, 2),
                new ShopOffer("a", ShopOfferKind.Character, "c", 30, 2), ShopOffer.CreateEmpty("empty") };
            var shop = new ShopState(ShopKind.Character, offers, 3, 1, 1);
            var observation = Build(Prep(), Econ(5), shop: shop, definitions: new[] { def });
            var strategy = new RandomStrategy(); var candidates = strategy.GetCandidates(observation, new ActionId("buy"));
            var buys = candidates.OfType<BuyCharacterAction>().ToArray();
            Assert.That(buys.Length, Is.EqualTo(14)); // 5 Hand slots + 9 Board slots, same Definition not yet on Board.
            Assert.That(buys.All(x => x.OfferId == "z"), Is.True);

            var item = Item("i", ItemEffectTarget.Player, new ItemEffectDefinition(ItemEffectKind.Block, 1));
            var itemShop = new ShopState(ShopKind.Item, new[] { new ShopOffer("i0", ShopOfferKind.Item, "i", 2, null) }, 1, 1, 1);
            var choiceConfig = Config(choiceRound: 1);
            StageProgressState selectB = StageSelectionRules.Select(StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(choiceConfig), choiceConfig).NextState, choiceConfig, StageChoice.SelectB).NextState;
            var itemObs = Build(selectB, Econ(5), shop: itemShop, items: new[] { item });
            Assert.That(new RandomStrategy().GetCandidates(itemObs, new ActionId("i")).OfType<BuyItemAction>().Select(a => a.HandSlot), Is.EqualTo(new[] {0,1,2,3,4}));
            var poor = Build(Prep(), Econ(1), shop: shop, definitions: new[] { def });
            Assert.That(strategy.GetCandidates(poor, new ActionId("poor")).OfType<BuyCharacterAction>(), Is.Empty);
        }

        [Test]
        public void CandidateGeneration_CreatesMovesSalesAndLegalItemTargetsWithoutDuplicates()
        {
            CharacterDefinition a = Def("a"), b = Def("b");
            CharacterInstance ia = Inst("ia", a), ib = Inst("ib", b);
            BoardState board = BoardState.CreateStandard(); Assert.That(board.TryWithCharacterAt(0, "ia", out board), Is.True);
            HandState hand = HandState.CreateStandard(); Assert.That(hand.TryWithSlot(0, HandSlotContent.Character("ib"), out hand), Is.True);
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Item("buff"), out hand), Is.True);
            var buff = Item("buff", ItemEffectTarget.OneAlly, new ItemEffectDefinition(ItemEffectKind.Attack, 2));
            var stage = Prep(); var obs = Build(stage, board: board, hand: hand, owned: new[] { ia, ib }, definitions: new[] { a, b }, items: new[] { buff });
            var actions = new RandomStrategy().GetCandidates(obs, new ActionId("moves"));
            Assert.That(actions.OfType<MoveOrSwapBoardAction>().Any(x => x.SourceSlot == 0 && x.TargetSlot == 1), Is.True);
            Assert.That(actions.OfType<MoveOrSwapHandAction>().Any(x => x.SourceSlot == 0 && x.TargetSlot == 1), Is.True);
            Assert.That(actions.OfType<MoveBoardToHandAction>().Any(x => x.BoardSlot == 0 && x.HandSlot == 2), Is.True);
            Assert.That(actions.OfType<MoveHandToBoardAction>().Any(x => x.HandSlot == 0 && x.BoardSlot == 1), Is.True);
            Assert.That(actions.OfType<MoveHandToBoardAction>().Any(x => x.HandSlot == 1), Is.False);
            Assert.That(actions.OfType<SellCharacterAction>().Select(x => x.InstanceId), Is.EquivalentTo(new[] {"ia", "ib"}));
            Assert.That(actions.OfType<SellItemAction>().Select(x => x.HandSlot), Is.EqualTo(new[] {1}));
            Assert.That(actions.OfType<UseHandItemAction>().Single().Target.SlotIndex, Is.EqualTo(0));
            Assert.That(actions.Select(SignatureForTest).Distinct().Count(), Is.EqualTo(actions.Count));
        }

        [Test]
        public void CandidateGeneration_RespectsBoardDuplicateDefinitionAndAcceptsPreparedEmptyFormation()
        {
            CharacterDefinition a = Def("a"); CharacterInstance ia = Inst("ia", a);
            BoardState board = BoardState.CreateStandard(); Assert.That(board.TryWithCharacterAt(0, "ia", out board), Is.True);
            HandState hand = HandState.CreateStandard();
            var offers = new[] { new ShopOffer("same", ShopOfferKind.Character, "a", 1, 1) };
            var shop = new ShopState(ShopKind.Character, offers, 1, 1, 1);
            var obs = Build(Prep(), board: board, owned: new[] {ia}, definitions: new[] {a}, shop: shop);
            var actions = new RandomStrategy(new RandomStrategySettings(shopRerollGoldCost: 1, normalExperienceGoldCost: 4)).GetCandidates(obs, new ActionId("dup"));
            Assert.That(actions.OfType<BuyCharacterAction>().Any(a1 => a1.Placement.Kind == CharacterLocationKind.Board), Is.False);
            Assert.That(actions.OfType<BuyCharacterAction>().Any(a1 => a1.Placement.Kind == CharacterLocationKind.Hand), Is.True);
            Assert.That(actions.OfType<StartBattleAction>().Count(), Is.EqualTo(1)); // Empty Formation is explicitly prepared and is allowed by existing rules.
            Assert.That(actions.OfType<RerollShopAction>().Count(), Is.EqualTo(1));
            Assert.That(actions.OfType<BuyExperienceAction>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void MoveHandToBoardCandidatesValidateFinalDefinitionSetForMovesAndSwaps()
        {
            CharacterDefinition a = Def("canonical-a"), b = Def("canonical-b");
            CharacterInstance boardA = Inst("board-a", a), boardB = Inst("board-b", b);
            CharacterInstance handAStar2 = new CharacterInstance("hand-a-star2", a, 1, 2);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, boardA.InstanceId, out board), Is.True);
            Assert.That(board.TryWithCharacterAt(1, boardB.InstanceId, out board), Is.True);
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Character(handAStar2.InstanceId), out hand), Is.True);
            var ownership = new CharacterOwnershipState(new[] { boardA, boardB, handAStar2 });
            GameObservation observation = Build(Prep(), board: board, hand: hand,
                owned: new[] { boardA, boardB, handAStar2 }, definitions: new[] { a, b });
            IReadOnlyList<GameAction> candidates = new RandomStrategy().GetCandidates(observation,
                new ActionId("final-board-duplicates"));
            MoveHandToBoardAction[] moves = candidates.OfType<MoveHandToBoardAction>().ToArray();

            Assert.That(moves.Any(x => x.HandSlot == 0 && x.BoardSlot == 2), Is.False,
                "An empty-slot move must not create a duplicate Definition ID, regardless of Star or Instance ID.");
            Assert.That(moves.Any(x => x.HandSlot == 0 && x.BoardSlot == 1), Is.False,
                "Swapping with a different Definition would leave two canonical-a characters on Board.");
            Assert.That(moves.Any(x => x.HandSlot == 0 && x.BoardSlot == 0), Is.True,
                "Swapping with the same Definition removes the Board copy before adding the Hand copy.");

            BoardHandOperationResult legalSwap = BoardHandRules.MoveHandToBoard(board, hand, ownership,
                0, 0, BoardHandOperationContext.Preparation);
            Assert.That(legalSwap.Succeeded, Is.True, legalSwap.FailureReason.ToString());
            Assert.That(BoardHandStateValidator.Validate(legalSwap.NextBoard, legalSwap.NextHand, ownership).IsValid, Is.True);
        }

        [Test]
        public void MoveHandToBoardCandidateForUniqueDefinitionMatchesRules()
        {
            CharacterDefinition a = Def("unique-a"), b = Def("unique-b");
            CharacterInstance boardA = Inst("unique-board", a), handB = Inst("unique-hand", b);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, boardA.InstanceId, out board), Is.True);
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Character(handB.InstanceId), out hand), Is.True);
            var ownership = new CharacterOwnershipState(new[] { boardA, handB });
            GameObservation observation = Build(Prep(), board: board, hand: hand,
                owned: new[] { boardA, handB }, definitions: new[] { a, b });

            MoveHandToBoardAction candidate = new RandomStrategy().GetCandidates(observation,
                new ActionId("unique-move")).OfType<MoveHandToBoardAction>()
                .Single(x => x.HandSlot == 0 && x.BoardSlot == 2);
            BoardHandOperationResult result = BoardHandRules.MoveHandToBoard(board, hand, ownership,
                candidate.HandSlot, candidate.BoardSlot, BoardHandOperationContext.Preparation);
            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.NextBoard.TryGetCharacterIdAt(2, out string moved), Is.True);
            Assert.That(moved, Is.EqualTo(handB.InstanceId));
        }

        [Test]
        public void StartBattleCandidateRequiresEnemyChoiceLevelCapacityAndPositiveHP()
        {
            var strategy = new RandomStrategy();
            StageProgressConfig choiceConfig = Config(choiceRound: 1);
            StageProgressState choice = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(choiceConfig), choiceConfig).NextState;
            StageProgressState selectB = StageSelectionRules.Select(choice, choiceConfig, StageChoice.SelectB).NextState;
            Assert.That(strategy.GetCandidates(Build(selectB), new ActionId("no-enemy")).OfType<StartBattleAction>(), Is.Empty);

            CharacterDefinition a = Def("a"), b = Def("b");
            CharacterInstance ia = Inst("ia", a), ib = Inst("ib", b);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, ia.InstanceId, out board), Is.True);
            Assert.That(board.TryWithCharacterAt(1, ib.InstanceId, out board), Is.True);
            Assert.That(strategy.GetCandidates(Build(Prep(), Econ(level: 1), board, owned: new[] {ia, ib}, definitions: new[] {a,b}),
                new ActionId("over-level")).OfType<StartBattleAction>(), Is.Empty);
            Assert.That(strategy.GetCandidates(Build(Prep(), Econ(level: 3, hp: 0), board, owned: new[] {ia, ib}, definitions: new[] {a,b}),
                new ActionId("zero-hp")).OfType<StartBattleAction>(), Is.Empty);
        }
        [Test]
        public void EventCandidates_UsePublicBoardHandTargetsAndEndOnlyWhenNoneEligible()
        {
            var cfg = Config(eventChance: 100);
            StageProgressState prep = Prep(cfg);
            StageProgressState battle = StageProgressRules.BeginBattle(prep).NextState;
            StageProgressState pending = StageProgressRules.ResolveBattle(battle, cfg, true).NextState;
            StageProgressState ev = StageProgressRules.CompletePendingReward(pending, cfg, new SimulationRandomStreams(91).Event.CaptureState()).NextState;
            CharacterDefinition d = Def("c"); CharacterInstance c = Inst("i", d);
            BoardState board = BoardState.CreateStandard(); Assert.That(board.TryWithCharacterAt(2, "i", out board), Is.True);
            var item = Item("event", ItemEffectTarget.OneAlly, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            var eventState = new EventItemState("event");
            var obs = Build(ev, board: board, owned: new[] {c}, definitions: new[] {d}, items: new[] {item}, eventItem: eventState);
            var strategy = new RandomStrategy(); var acts = strategy.GetCandidates(obs, new ActionId("evt"));
            Assert.That(acts.OfType<UseEventItemAction>().Count(), Is.EqualTo(1)); Assert.That(acts.OfType<EndEventAction>(), Is.Empty);
            var availableDecision = strategy.ChooseAction(obs, Context(Rng(91), "event-use"));
            Assert.That(availableDecision.Action, Is.TypeOf<UseEventItemAction>());
            var empty = Build(ev, items: new[] {item}, eventItem: eventState);
            Assert.That(strategy.GetCandidates(empty, new ActionId("end")).Single(), Is.TypeOf<EndEventAction>());
        }

        [Test]
        public void ConsumedEventGeneratesOnlyEndEventAndRandomStrategySelectsItWithoutRng()
        {
            var cfg = Config(eventChance: 100);
            StageProgressState prep = Prep(cfg);
            StageProgressState battle = StageProgressRules.BeginBattle(prep).NextState;
            StageProgressState pending = StageProgressRules.ResolveBattle(battle, cfg, true).NextState;
            StageProgressState ev = StageProgressRules.CompletePendingReward(pending, cfg,
                new SimulationRandomStreams(91).Event.CaptureState()).NextState;
            var item = Item("event", ItemEffectTarget.OneAlly, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            GameObservation observation = Build(ev, items: new[] { item },
                eventItem: new EventItemState("event", EventItemStatus.Consumed));
            var strategy = new RandomStrategy();
            var candidates = strategy.GetCandidates(observation, new ActionId("end-consumed"));
            Assert.That(candidates, Has.Count.EqualTo(1));
            Assert.That(candidates[0], Is.TypeOf<EndEventAction>());

            StrategyRngState before = Rng(8877);
            StrategyContext context = Context(before, "end-consumed");
            StrategyDecision decision = strategy.ChooseAction(observation, context);
            Assert.That(decision.Action, Is.TypeOf<EndEventAction>());
            Assert.That(decision.StrategyRngAdvanced, Is.False);
            Assert.That(decision.NextStrategyRngState, Is.EqualTo(before));
            StrategyDecision replay = strategy.ChooseAction(observation, Context(before, "end-consumed"));
            Assert.That(replay.Action, Is.TypeOf<EndEventAction>());
            Assert.That(replay.NextStrategyRngState, Is.EqualTo(decision.NextStrategyRngState));
        }

        [Test]
        public void EndEventIsNotGeneratedOutsideEventPhaseOrForEndedWithoutUseState()
        {
            var item = Item("event", ItemEffectTarget.OneAlly, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            var strategy = new RandomStrategy();
            GameObservation wrongPhase = Build(Prep(), items: new[] { item },
                eventItem: new EventItemState("event", EventItemStatus.Consumed));
            Assert.That(strategy.GetCandidates(wrongPhase, new ActionId("wrong-phase")).OfType<EndEventAction>(), Is.Empty);

            var cfg = Config(eventChance: 100);
            StageProgressState prep = Prep(cfg);
            StageProgressState pending = StageProgressRules.ResolveBattle(
                StageProgressRules.BeginBattle(prep).NextState, cfg, true).NextState;
            StageProgressState ev = StageProgressRules.CompletePendingReward(pending, cfg,
                new SimulationRandomStreams(91).Event.CaptureState()).NextState;
            GameObservation invalidLifecycle = Build(ev, items: new[] { item },
                eventItem: new EventItemState("event", EventItemStatus.EndedWithoutUse));
            Assert.That(strategy.GetCandidates(invalidLifecycle, new ActionId("ended-state")), Is.Empty);
        }
        [Test]
        public void EndingConsumedEventAdvancesToNextEncounterAndExposesStageChoices()
        {
            var cfg = new StageProgressConfig(2, 2, 2, 2, 100, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(cfg), cfg).NextState;
            StageProgressState battle = StageProgressRules.BeginBattle(prep).NextState;
            StageProgressState reward = StageProgressRules.ResolveBattle(battle, cfg, true).NextState;
            StageProgressState ev = StageProgressRules.CompletePendingReward(reward, cfg,
                new SimulationRandomStreams(191).Event.CaptureState()).NextState;
            var eventItem = new EventItemState("event", EventItemStatus.Consumed);
            ItemDefinition item = Item("event", ItemEffectTarget.OneAlly,
                new ItemEffectDefinition(ItemEffectKind.Star, 2));
            GameObservation eventObservation = Build(ev, items: new[] { item }, eventItem: eventItem);
            var eventDecision = new RandomStrategy().ChooseAction(eventObservation,
                Context(Rng(191), "event-end", step: 0));
            Assert.That(eventDecision.Action, Is.TypeOf<EndEventAction>());

            StageProgressState completedEvent = StageProgressRules.CompleteEvent(ev, false).NextState;
            StageProgressState nextEncounter = StageProgressRules.BeginCurrentEncounter(completedEvent, cfg).NextState;
            Assert.That(nextEncounter.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            GameObservation choiceObservation = Build(nextEncounter);
            var choices = new RandomStrategy().GetCandidates(choiceObservation, new ActionId("next-choice"));
            Assert.That(choices.OfType<SelectStageChoiceAction>().Select(x => x.Choice),
                Is.EqualTo(new[] { StageChoice.SelectA, StageChoice.SelectB, StageChoice.SelectC, StageChoice.SelectD }));
        }
        [Test]
        public void UniformSelection_IsReproducibleAndCheckpointRestoresSameAction()
        {
            var obs = Build(Choice()); var strategy = new RandomStrategy(); var initial = Rng(8842);
            var a = strategy.ChooseAction(obs, Context(initial, "same", step: 4));
            var b = strategy.ChooseAction(obs, Context(initial, "same", step: 4));
            Assert.That(a.Action.Kind, Is.EqualTo(b.Action.Kind)); Assert.That(SignatureForTest(a.Action), Is.EqualTo(SignatureForTest(b.Action)));
            Assert.That(a.NextStrategyRngState, Is.EqualTo(b.NextStrategyRngState));
            Assert.That(a.StrategyRngAdvanced, Is.True);
            var resumed = strategy.ChooseAction(obs, Context(initial, "same", step: 4));
            Assert.That(SignatureForTest(a.Action), Is.EqualTo(SignatureForTest(resumed.Action)));
        }

        [Test]
        public void ZeroOrOneCandidateDoesNotConsumeRngAndOtherGameStreamsRemainIndependent()
        {
            var strategy = new RandomStrategy();
            GameObservation noChoice = Build(StageProgressRules.CreateInitialState(Config()));
            StrategyRngState before = Rng(42);
            var none = strategy.ChooseAction(noChoice, Context(before, "none"));
            Assert.That(none.HasAction, Is.False);
            Assert.That(none.NextStrategyRngState, Is.EqualTo(before));
            Assert.That(none.StrategyRngAdvanced, Is.False);

            StageProgressConfig selectConfig = Config(choiceRound: 1);
            StageProgressState choice = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(selectConfig), selectConfig).NextState;
            StageProgressState selectB = StageSelectionRules.Select(choice, selectConfig, StageChoice.SelectB).NextState;
            var emptyItemShop = new ShopState(ShopKind.Item, Array.Empty<ShopOffer>(), 0, 1, 1);
            var emptyPreparation = Build(selectB, shop: emptyItemShop);
            var stalled = strategy.ChooseAction(emptyPreparation, Context(before, "empty-prep"));
            Assert.That(stalled.HasAction, Is.False); Assert.That(stalled.NextStrategyRngState, Is.EqualTo(before));

            var eventConfig = Config(eventChance: 100);
            StageProgressState prep = Prep(eventConfig);
            StageProgressState battle = StageProgressRules.BeginBattle(prep).NextState;
            StageProgressState reward = StageProgressRules.ResolveBattle(battle, eventConfig, true).NextState;
            StageProgressState eventStage = StageProgressRules.CompletePendingReward(reward, eventConfig,
                new SimulationRandomStreams(9).Event.CaptureState()).NextState;
            CharacterDefinition def = Def("event-char");
            CharacterInstance character = Inst("event-instance", def);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, character.InstanceId, out board), Is.True);
            var item = Item("event-item", ItemEffectTarget.OneAlly, new ItemEffectDefinition(ItemEffectKind.Attack, 1));
            var oneObs = Build(eventStage, board: board, owned: new[] { character }, definitions: new[] { def },
                items: new[] { item }, eventItem: new EventItemState("event-item"));
            var one = strategy.ChooseAction(oneObs, Context(before, "one"));
            Assert.That(one.Action, Is.TypeOf<UseEventItemAction>());
            Assert.That(one.NextStrategyRngState, Is.EqualTo(before));
            Assert.That(one.StrategyRngAdvanced, Is.False);

            var streams = new SimulationRandomStreams(88); var old = streams.CaptureState();
            var choiceObs = Build(Choice()); strategy.ChooseAction(choiceObs, Context(before));
            var current = streams.CaptureState();
            Assert.That(current.Shop, Is.EqualTo(old.Shop)); Assert.That(current.Stats, Is.EqualTo(old.Stats));
            Assert.That(current.Encounter, Is.EqualTo(old.Encounter)); Assert.That(current.Battle, Is.EqualTo(old.Battle));
            Assert.That(current.Event, Is.EqualTo(old.Event)); Assert.That(current.Roster, Is.EqualTo(old.Roster)); Assert.That(current.Economy, Is.EqualTo(old.Economy));
        }
        [Test]
        public void StrategyStateCountsPreparationSuppressesImmediateReverseAndPrioritizesBattle()
        {
            var strategy = new RandomStrategy(new RandomStrategySettings(preparationDecisionLimit: 1));
            CharacterDefinition d = Def("c"); CharacterInstance c = Inst("i", d);
            BoardState board = BoardState.CreateStandard(); Assert.That(board.TryWithCharacterAt(0, "i", out board), Is.True);
            var enemy = new EnemyDefinition("e", "e", 1, 0, 2, null, null, null, "T001", new[] {"T001"}, null, null);
            var formationSlots = Enumerable.Range(0, 6).Select(i => i == 0 ? new EnemyFormationSlotDefinition("e", 0, 0) : new EnemyFormationSlotDefinition(null, 0, 0)).ToArray();
            var formation = new EnemyFormationDefinition("f", formationSlots);
            EnemyBoardState eb = EnemyFormationRules.CreateBoard(formation, new[] { enemy }, 0).Board;
            var obs = Build(Prep(), board: board, owned: new[] {c}, definitions: new[] {d}, enemyBoard: eb, enemyDefs: new[] {enemy});
            var context = Context(Rng(1), "step0"); var first = strategy.ChooseAction(obs, context);
            Assert.That(first.NextState.SchemaVersion, Is.EqualTo(RandomStrategy.StateSchemaVersion));
            Assert.That(first.NextState.Payload, Does.StartWith("rg1|1|"));
            Assert.That(first.Action, Is.TypeOf<StartBattleAction>());
            Assert.That(first.StrategyRngAdvanced, Is.False);
            var after = Context(first.NextStrategyRngState, "step1", first.NextState, 1);
            var second = strategy.ChooseAction(obs, after);
            Assert.That(second.Action, Is.Not.TypeOf<StartBattleAction>()); // A rejected unchanged observation does not immediately repeat StartBattle.

            // Move an occupied Board slot, then ensure its immediate reverse is not proposed.
            var movementState = new StrategyState(Sid, Ver, 1, "rg1|1|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("6:0:1")));
            BoardState moved = BoardState.CreateStandard(); Assert.That(moved.TryWithCharacterAt(1, "i", out moved), Is.True);
            var sparse = Build(Prep(), board: moved, owned: new[] {c}, definitions: new[] {d});
            var reverseContext = Context(Rng(5), "reverse", movementState);
            var reverseDecision = strategy.ChooseAction(sparse, reverseContext);
            Assert.That(reverseDecision.Action, Is.Not.TypeOf<MoveOrSwapBoardAction>());
        }

        [TestCase(StageChoice.SelectB)]
        [TestCase(StageChoice.SelectD)]
        public void EnemylessPreparationReturnsNoActionAtPolicyLimitWithoutConsumingRng(StageChoice choice)
        {
            StageProgressConfig config = Config(choiceRound: 1);
            StageProgressState selected = StageSelectionRules.Select(Choice(config), config, choice).NextState;
            ShopState shop;
            ItemDefinition[] items;
            CharacterDefinition[] definitions;
            if (choice == StageChoice.SelectB)
            {
                var item = Item("limit-item", ItemEffectTarget.Player,
                    new ItemEffectDefinition(ItemEffectKind.Block, 1));
                shop = new ShopState(ShopKind.Item,
                    new[] { new ShopOffer("limit-offer", ShopOfferKind.Item, item.DefinitionId, 1, null) },
                    1, 1, 1);
                items = new[] { item };
                definitions = Array.Empty<CharacterDefinition>();
            }
            else
            {
                CharacterDefinition definition = Def("limit-character");
                shop = new ShopState(ShopKind.Character,
                    new[] { new ShopOffer("limit-offer", ShopOfferKind.Character, definition.DefinitionId, 1, 1) },
                    1, 1, 1);
                items = Array.Empty<ItemDefinition>();
                definitions = new[] { definition };
            }
            GameObservation observation = Build(selected, Econ(10), definitions: definitions,
                shop: shop, items: items);
            var strategy = new RandomStrategy(new RandomStrategySettings(preparationDecisionLimit: 1));
            Assert.That(strategy.GetCandidates(observation, new ActionId("enemyless-candidates")), Is.Not.Empty,
                "The no-op must be caused by the policy limit, not by an empty candidate set.");

            StrategyRngState checkpoint = Rng(13013);
            var state = new StrategyState(Sid, Ver, 1, "rg1|0|" + Convert.ToBase64String(Array.Empty<byte>()));
            StrategyDecision decision = strategy.ChooseAction(observation, Context(checkpoint,
                "enemyless-limit", state));

            Assert.That(decision.HasAction, Is.False);
            Assert.That(decision.Diagnostic, Does.Contain("enemyless selection"));
            Assert.That(decision.NextState.Payload, Does.StartWith("rg1|1|"));
            Assert.That(decision.NextStrategyRngState, Is.EqualTo(checkpoint));
            Assert.That(decision.StrategyRngAdvanced, Is.False);
        }

        [Test]
        public void DecisionLimitRandomlySelectsOnlyBoardOverflowResolutionAndThenStartsBattle()
        {
            CharacterDefinition[] definitions = Enumerable.Range(0, 7).Select(i => Def("limit-overflow-" + i)).ToArray();
            CharacterInstance[] owned = definitions.Select((definition, i) => Inst("limit-instance-" + i, definition)).ToArray();
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, owned[0].InstanceId, out board), Is.True);
            Assert.That(board.TryWithCharacterAt(1, owned[1].InstanceId, out board), Is.True);
            HandState hand = HandState.CreateStandard();
            for (int i = 0; i < hand.SlotCount; i++)
                Assert.That(hand.TryWithSlot(i, HandSlotContent.Character(owned[i + 2].InstanceId), out hand), Is.True);
            var strategy = new RandomStrategy(new RandomStrategySettings(preparationDecisionLimit: 1));
            GameObservation over = Build(Prep(), Econ(gold: 0, level: 1), board, hand, owned, definitions);
            StrategyRngState checkpoint = Rng(13131);
            var initialState = new StrategyState(Sid, Ver, 1, "rg1|0|" + Convert.ToBase64String(Array.Empty<byte>()));
            var firstContext = Context(checkpoint, "overflow-1", initialState);
            StrategyDecision first = strategy.ChooseAction(over, firstContext);
            StrategyDecision replay = strategy.ChooseAction(over, Context(checkpoint, "overflow-1", initialState));
            Assert.That(first.Action, Is.TypeOf<SellCharacterAction>());
            string sold = ((SellCharacterAction)first.Action).InstanceId;
            Assert.That(owned.Take(2).Any(instance => instance.InstanceId == sold), Is.True,
                "Selling a Hand character would not reduce Board/Level excess.");
            Assert.That(first.Action.Kind, Is.EqualTo(replay.Action.Kind));
            Assert.That(((SellCharacterAction)replay.Action).InstanceId, Is.EqualTo(sold));
            Assert.That(first.NextStrategyRngState, Is.EqualTo(replay.NextStrategyRngState));

            Assert.That(board.TryFindInstance(sold, out int slot), Is.True);
            Assert.That(board.TryWithCharacterAt(slot, null, out BoardState nextBoard), Is.True);
            CharacterInstance[] nextOwned = owned.Where(instance => instance.InstanceId != sold).ToArray();
            GameObservation ready = Build(Prep(), Econ(gold: 0, level: 1), nextBoard, hand, nextOwned, definitions);
            StrategyDecision next = strategy.ChooseAction(ready, Context(first.NextStrategyRngState,
                "overflow-ready", first.NextState, step: 1));
            Assert.That(next.Action, Is.TypeOf<StartBattleAction>(),
                "After one legal sale reduces Board to Level, the random policy must use legal StartBattle at its cap.");
        }

        [Test]
        public void DecisionLimitPrefersAnEmptyHandTransferBeforeSaleAndDoesNotForceIllegalBattle()
        {
            CharacterDefinition a = Def("transfer-a"), b = Def("transfer-b");
            CharacterInstance ia = Inst("transfer-ia", a), ib = Inst("transfer-ib", b);
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, ia.InstanceId, out board), Is.True);
            Assert.That(board.TryWithCharacterAt(1, ib.InstanceId, out board), Is.True);
            GameObservation observation = Build(Prep(), Econ(level: 1), board,
                HandState.CreateStandard(), new[] { ia, ib }, new[] { a, b });
            var strategy = new RandomStrategy(new RandomStrategySettings(preparationDecisionLimit: 1));
            StrategyDecision decision = strategy.ChooseAction(observation,
                Context(Rng(13132), "overflow-transfer",
                    new StrategyState(Sid, Ver, 1, "rg1|0|" + Convert.ToBase64String(Array.Empty<byte>()))));
            Assert.That(decision.Action, Is.TypeOf<MoveBoardToHandAction>());
            Assert.That(decision.Action, Is.Not.TypeOf<StartBattleAction>());
        }
        [Test]
        public void InvalidPayloadResetsSafelyAndAutomaticPhasesReturnNoActionWithoutRandomness()
        {
            var strategy = new RandomStrategy(); var malformed = new StrategyState(Sid, Ver, 1, "rg1|bad|!bad!");
            var obs = Build(Choice()); var decision = strategy.ChooseAction(obs, Context(Rng(8), "choice", malformed));
            Assert.That(decision.HasAction, Is.True); Assert.That(decision.NextState.Payload, Does.StartWith("rg1|0|"));
            var completed = StageProgressRules.CreateInitialState(Config());
            // Initial waiting phase is not a decision phase; the Strategy returns a no-op and preserves RNG.
            var noDecision = strategy.ChooseAction(Build(completed), Context(Rng(8), "idle"));
            Assert.That(noDecision.HasAction, Is.False); Assert.That(noDecision.StrategyRngAdvanced, Is.False);
        }

        [Test]
        public void StrategyActionCanBeDispatchedAndRejectionDoesNotMutateInputOrCommitCheckpoints()
        {
            StageProgressState stage = Choice();
            GameObservation observation = Build(stage);
            StrategyRngState checkpoint = Rng(765);
            StrategyContext context = Context(checkpoint, "dispatch-choice");
            StrategyDecision decision = new RandomStrategy().ChooseAction(observation, context);
            Assert.That(decision.Action.ActionId, Is.EqualTo(context.NextActionId));
            Assert.That(context.Accepts(decision), Is.True);

            var streams = new SimulationRandomStreams(20UL);
            var before = streams.CaptureState();
            var input = new ActionExecutionState(stage, Econ(), new CharacterPool(Array.Empty<CharacterPoolEntry>()),
                new CharacterInstanceFactory(), BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), new ItemEffectState(), before);
            var dispatcher = new ActionDispatcher(new IActionHandler[] { new RejectChoiceHandler() });
            var dispatched = dispatcher.Dispatch(input, decision.Action, new ActionExecutionContext());
            Assert.That(dispatched.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(dispatched.Error.Code, Is.EqualTo(ActionErrorCode.InvalidAction));
            Assert.That(input.Stage, Is.SameAs(stage));
            Assert.That(input.RandomState, Is.EqualTo(before));
            Assert.That(input.HasExecuted(context.NextActionId), Is.False);
            Assert.That(decision.NextStrategyRngState, Is.Not.EqualTo(checkpoint));
            Assert.That(context.StrategyRandomState, Is.EqualTo(checkpoint));
        }

        private sealed class RejectChoiceHandler : IActionHandler
        {
            public GameActionKind Kind => GameActionKind.SelectStageChoice;
            public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context) =>
                ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Deliberate test rejection.");
        }
        private static string SignatureForTest(GameAction a)
        {
            switch (a)
            {
                case SelectStageChoiceAction x: return "1:" + (int)x.Choice;
                case BuyCharacterAction x: return "2:" + x.OfferId + ":" + (int)x.Placement.Kind + ":" + x.Placement.SlotIndex;
                case BuyItemAction x: return "3:" + x.OfferId + ":" + x.HandSlot;
                case MoveOrSwapBoardAction x: return "6:" + x.SourceSlot + ":" + x.TargetSlot;
                case MoveOrSwapHandAction x: return "7:" + x.SourceSlot + ":" + x.TargetSlot;
                case MoveBoardToHandAction x: return "8:" + x.BoardSlot + ":" + x.HandSlot;
                case MoveHandToBoardAction x: return "9:" + x.HandSlot + ":" + x.BoardSlot;
                case SellCharacterAction x: return "10:" + x.InstanceId;
                case SellItemAction x: return "11:" + x.HandSlot;
                case UseHandItemAction x: return "12:" + x.HandSlot + ":" + x.Target.Kind + ":" + x.Target.SlotIndex;
                case UseEventItemAction x: return "13:" + x.Target.Kind + ":" + x.Target.SlotIndex;
                default: return ((int)a.Kind).ToString();
            }
        }
    }
}














