using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BoardHandActionHandlerTests
    {
        private static readonly CharacterDefinition Hero = Def("hero");
        private static readonly CharacterDefinition Mage = Def("mage");
        private static readonly CharacterDefinition Rogue = Def("rogue");
        private static readonly CharacterDefinition Archer = Def("archer");
        private static CharacterDefinition Def(string id) => new CharacterDefinition(id, id, 1, Array.Empty<string>(), Array.Empty<string>());
        private static CharacterInstance Unit(string id, CharacterDefinition def = null, int star = 1, int cost = 2) => new CharacterInstance(id, def ?? Hero, cost, star);

        private static StageProgressState PreparationStage()
        {
            var config = new StageProgressConfig(20, 4, 2, 4, 0, 10, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var streams = new SimulationRandomStreams(77UL);
            StageProgressState stage = StageProgressRules.CreateInitialState(config);
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            stage = StageProgressRules.BeginBattle(stage).NextState;
            stage = StageProgressRules.ResolveBattle(stage, config, true).NextState;
            stage = StageProgressRules.CompletePendingReward(stage, config, streams.Event.CaptureState()).NextState;
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            return StageSelectionRules.Select(stage, config, StageChoice.SelectA).NextState;
        }

        private static CharacterPool PoolFor(IEnumerable<CharacterInstance> owned)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal) { ["hero"] = 0, ["mage"] = 0, ["rogue"] = 0, ["archer"] = 0 };
            foreach (CharacterInstance instance in owned) counts[instance.DefinitionId] += instance.PoolCopies;
            var entries = new List<CharacterPoolEntry>();
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value > 15) throw new ArgumentException("Test pool exceeds its initial limit.");
                entries.Add(new CharacterPoolEntry(pair.Key, 15 - pair.Value, 15, 0, pair.Value));
            }
            return new CharacterPool(entries);
        }

        private static ActionExecutionState State(IEnumerable<CharacterInstance> owned,
            BoardState board = null, HandState hand = null, EconomyState economy = null,
            CharacterPool pool = null, StageProgressState stage = null, ulong seed = 77UL)
        {
            CharacterInstance[] characters = owned.ToArray();
            return new ActionExecutionState(stage ?? PreparationStage(),
                economy ?? new EconomyState(10, 0, 1, 12, 10, 0, 1, 20, 30),
                pool ?? PoolFor(characters), new CharacterInstanceFactory("U", 5),
                board ?? BoardState.CreateStandard(), hand ?? HandState.CreateStandard(),
                new CharacterOwnershipState(characters), new ItemEffectState(),
                new SimulationRandomStreams(seed).CaptureState());
        }

        private static ActionDispatcher Dispatcher() =>
            new ActionDispatcher(ShopEconomyActionHandlers.Create().Concat(BoardHandActionHandlers.Create()));

        private static ActionExecutionState Succeed(ActionExecutionState state, GameAction action)
        {
            ActionDispatchResult result = Dispatcher().Dispatch(state, action, new ActionExecutionContext());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), result.Error.Detail);
            return result.NextState;
        }

        private static ActionDispatchResult Dispatch(ActionExecutionState state, GameAction action) =>
            Dispatcher().Dispatch(state, action, new ActionExecutionContext());

        private static BoardState PutBoard(BoardState board, int slot, string id)
        {
            Assert.That(board.TryWithCharacterAt(slot, id, out BoardState next), Is.True);
            return next;
        }

        private static HandState PutCharacter(HandState hand, int slot, string id)
        {
            Assert.That(hand.TryWithSlot(slot, HandSlotContent.Character(id), out HandState next), Is.True);
            return next;
        }

        private static HandState PutItem(HandState hand, int slot, string id)
        {
            Assert.That(hand.TryWithSlot(slot, HandSlotContent.Item(id), out HandState next), Is.True);
            return next;
        }

        private static void SameRandomState(SimulationRandomStreamsState left, SimulationRandomStreamsState right)
        {
            Assert.That(right.TrialSeed, Is.EqualTo(left.TrialSeed));
            Assert.That(right.Version, Is.EqualTo(left.Version));
            Assert.That(right.Shop, Is.EqualTo(left.Shop));
            Assert.That(right.Stats, Is.EqualTo(left.Stats));
            Assert.That(right.Encounter, Is.EqualTo(left.Encounter));
            Assert.That(right.Battle, Is.EqualTo(left.Battle));
            Assert.That(right.Event, Is.EqualTo(left.Event));
            Assert.That(right.Roster, Is.EqualTo(left.Roster));
            Assert.That(right.Economy, Is.EqualTo(left.Economy));
        }

        [Test]
        public void SixD3HandlersCoexistWithD2AndOtherActionsRemainUnsupported()
        {
            Assert.That(BoardHandActionHandlers.Create().Select(h => h.Kind), Is.EquivalentTo(new[]
            {
                GameActionKind.MoveOrSwapBoard, GameActionKind.MoveOrSwapHand, GameActionKind.MoveBoardToHand,
                GameActionKind.MoveHandToBoard, GameActionKind.SellCharacter, GameActionKind.SellItem
            }));
            var combined = ShopEconomyActionHandlers.Create().Concat(BoardHandActionHandlers.Create()).ToArray();
            Assert.That(combined.Length, Is.EqualTo(10));
            Assert.That(combined.Select(h => h.Kind).Distinct().Count(), Is.EqualTo(10));
            var unsupported = Dispatch(State(Array.Empty<CharacterInstance>()), new StartBattleAction(new ActionId("not-yet")));
            Assert.That(unsupported.Error.Code, Is.EqualTo(ActionErrorCode.UnsupportedAction));
        }

        [Test]
        public void BoardMoveToEmptySlotAllowsCountAboveLevelAndDoesNotConsumeRng()
        {
            CharacterInstance[] units = { Unit("h", Hero), Unit("m", Mage), Unit("r", Rogue), Unit("a", Archer) };
            BoardState board = PutBoard(PutBoard(PutBoard(PutBoard(BoardState.CreateStandard(), 0, "h"), 1, "m"), 2, "r"), 3, "a");
            ActionExecutionState before = State(units, board);
            ActionExecutionState after = Succeed(before, new MoveOrSwapBoardAction(new ActionId("move-board"), 0, 8));
            after.Board.TryGetCharacterIdAt(0, out string source);
            after.Board.TryGetCharacterIdAt(8, out string destination);
            Assert.That(source, Is.Null);
            Assert.That(destination, Is.EqualTo("h"));
            Assert.That(after.Ownership, Is.SameAs(before.Ownership));
            Assert.That(after.CharacterPool, Is.SameAs(before.CharacterPool));
            Assert.That(after.Economy, Is.SameAs(before.Economy));
            SameRandomState(before.RandomState, after.RandomState);
            Assert.That(BoardHandStateValidator.Validate(after.Board, after.Hand, after.Ownership).IsValid, Is.True);
        }

        [Test]
        public void BoardSwapAndSameSlotUseExistingRuleSemantics()
        {
            var owned = new[] { Unit("h", Hero), Unit("m", Mage) };
            BoardState board = PutBoard(PutBoard(BoardState.CreateStandard(), 0, "h"), 4, "m");
            ActionExecutionState state = State(owned, board);
            ActionExecutionState swapped = Succeed(state, new MoveOrSwapBoardAction(new ActionId("swap-board"), 0, 4));
            swapped.Board.TryGetCharacterIdAt(0, out string first);
            swapped.Board.TryGetCharacterIdAt(4, out string second);
            Assert.That(first, Is.EqualTo("m"));
            Assert.That(second, Is.EqualTo("h"));
            ActionExecutionState same = Succeed(swapped, new MoveOrSwapBoardAction(new ActionId("same-board"), 0, 0));
            Assert.That(same.Board, Is.SameAs(swapped.Board));
            SameRandomState(state.RandomState, same.RandomState);
        }

        [Test]
        public void BoardRejectsEmptySourceAndOutOfRangeAtomically()
        {
            var owned = new[] { Unit("h1", Hero) };
            BoardState board = PutBoard(BoardState.CreateStandard(), 0, "h1");
            ActionExecutionState state = State(owned, board);
            ActionDispatchResult range = Dispatch(state, new MoveOrSwapBoardAction(new ActionId("range-board"), 0, 9));
            Assert.That(range.Error.Code, Is.EqualTo(ActionErrorCode.InvalidSlot));
            Assert.That(range.NextState, Is.SameAs(state));
            ActionDispatchResult empty = Dispatch(state, new MoveOrSwapBoardAction(new ActionId("empty-board"), 8, 1));
            Assert.That(empty.Error.Code, Is.EqualTo(ActionErrorCode.InvalidTarget));
            Assert.That(empty.NextState, Is.SameAs(state));
        }

        [Test]
        public void HandMovesItemAndSwapsCharacterWithItem()
        {
            HandState hand = PutItem(PutCharacter(HandState.CreateStandard(), 0, "h"), 1, "potion");
            ActionExecutionState state = State(new[] { Unit("h") }, hand: hand);
            ActionExecutionState moved = Succeed(state, new MoveOrSwapHandAction(new ActionId("move-hand"), 0, 4));
            moved.Hand.TryGetSlot(0, out HandSlotContent empty);
            moved.Hand.TryGetSlot(4, out HandSlotContent character);
            Assert.That(empty.Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Assert.That(character.CharacterInstanceId, Is.EqualTo("h"));
            ActionExecutionState swapped = Succeed(moved, new MoveOrSwapHandAction(new ActionId("swap-hand"), 4, 1));
            swapped.Hand.TryGetSlot(4, out HandSlotContent item);
            swapped.Hand.TryGetSlot(1, out HandSlotContent character2);
            Assert.That(item.ItemDefinitionId, Is.EqualTo("potion"));
            Assert.That(character2.CharacterInstanceId, Is.EqualTo("h"));
            ActionExecutionState same = Succeed(swapped, new MoveOrSwapHandAction(new ActionId("same-hand"), 1, 1));
            Assert.That(same.Hand, Is.SameAs(swapped.Hand));
            SameRandomState(state.RandomState, same.RandomState);
        }

        [Test]
        public void HandRejectsEmptySourceAndInvalidSlot()
        {
            ActionExecutionState state = State(Array.Empty<CharacterInstance>());
            ActionDispatchResult empty = Dispatch(state, new MoveOrSwapHandAction(new ActionId("empty-hand"), 0, 1));
            Assert.That(empty.Error.Code, Is.EqualTo(ActionErrorCode.InvalidTarget));
            Assert.That(empty.NextState, Is.SameAs(state));
            ActionDispatchResult range = Dispatch(state, new MoveOrSwapHandAction(new ActionId("range-hand"), 5, 1));
            Assert.That(range.Error.Code, Is.EqualTo(ActionErrorCode.InvalidSlot));
            Assert.That(range.NextState, Is.SameAs(state));
            SameRandomState(state.RandomState, range.NextState.RandomState);
        }

        [Test]
        public void BoardHandRulesMoveAndSwapCharactersBothDirections()
        {
            var owned = new[] { Unit("h", Hero), Unit("m", Mage) };
            BoardState board = PutBoard(BoardState.CreateStandard(), 0, "h");
            HandState hand = PutCharacter(HandState.CreateStandard(), 2, "m");
            ActionExecutionState state = State(owned, board, hand);
            ActionExecutionState swapped = Succeed(state, new MoveBoardToHandAction(new ActionId("board-to-hand-swap"), 0, 2));
            swapped.Board.TryGetCharacterIdAt(0, out string boardId);
            swapped.Hand.TryGetSlot(2, out HandSlotContent handContent);
            Assert.That(boardId, Is.EqualTo("m"));
            Assert.That(handContent.CharacterInstanceId, Is.EqualTo("h"));
            ActionExecutionState moved = Succeed(swapped, new MoveHandToBoardAction(new ActionId("hand-to-board"), 2, 8));
            moved.Board.TryGetCharacterIdAt(8, out string movedId);
            moved.Hand.TryGetSlot(2, out HandSlotContent cleared);
            Assert.That(movedId, Is.EqualTo("h"));
            Assert.That(cleared.IsEmpty, Is.True);
            Assert.That(BoardHandStateValidator.Validate(moved.Board, moved.Hand, moved.Ownership).IsValid, Is.True);
            SameRandomState(state.RandomState, moved.RandomState);
        }

        [Test]
        public void BoardHandRulesMoveCharactersToEmptySlots()
        {
            var owned = new[] { Unit("h", Hero), Unit("m", Mage) };
            ActionExecutionState state = State(owned, PutBoard(BoardState.CreateStandard(), 0, "h"),
                PutCharacter(HandState.CreateStandard(), 1, "m"));
            ActionExecutionState toHand = Succeed(state, new MoveBoardToHandAction(new ActionId("empty-hand-move"), 0, 4));
            toHand.Board.TryGetCharacterIdAt(0, out string removed);
            toHand.Hand.TryGetSlot(4, out HandSlotContent placed);
            Assert.That(removed, Is.Null);
            Assert.That(placed.CharacterInstanceId, Is.EqualTo("h"));
            ActionExecutionState toBoard = Succeed(toHand, new MoveHandToBoardAction(new ActionId("empty-board-move"), 1, 8));
            toBoard.Hand.TryGetSlot(1, out HandSlotContent cleared);
            toBoard.Board.TryGetCharacterIdAt(8, out string placedBoard);
            Assert.That(cleared.IsEmpty, Is.True);
            Assert.That(placedBoard, Is.EqualTo("m"));
        }

        [Test]
        public void BoardHandRulesRejectItemExchangeAndItemPlacementAtomically()
        {
            ActionExecutionState state = State(new[] { Unit("h") },
                PutBoard(BoardState.CreateStandard(), 0, "h"),
                PutItem(HandState.CreateStandard(), 0, "potion"));
            ActionDispatchResult boardToItem = Dispatch(state, new MoveBoardToHandAction(new ActionId("board-item"), 0, 0));
            Assert.That(boardToItem.Error.Code, Is.EqualTo(ActionErrorCode.InvalidTarget));
            Assert.That(boardToItem.NextState, Is.SameAs(state));
            ActionDispatchResult itemToBoard = Dispatch(state, new MoveHandToBoardAction(new ActionId("item-board"), 0, 1));
            Assert.That(itemToBoard.Error.Code, Is.EqualTo(ActionErrorCode.InvalidTarget));
            Assert.That(itemToBoard.NextState, Is.SameAs(state));
            SameRandomState(state.RandomState, itemToBoard.NextState.RandomState);
        }

        [Test]
        public void HandToBoardRejectsDuplicateDefinitionOnFinalBoard()
        {
            var owned = new[] { Unit("h1"), Unit("h2") };
            ActionExecutionState state = State(owned, PutBoard(BoardState.CreateStandard(), 0, "h1"),
                PutCharacter(HandState.CreateStandard(), 0, "h2"));
            ActionDispatchResult result = Dispatch(state, new MoveHandToBoardAction(new ActionId("dup-final"), 0, 1));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.BoardLimitExceeded));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [TestCase(1, 1)]
        [TestCase(2, 3)]
        [TestCase(3, 9)]
        public void SellCharacterClearsBoardAndReturnsStarCopies(int star, int copies)
        {
            CharacterInstance unit = Unit("sale", Hero, star);
            ActionExecutionState before = State(new[] { unit }, PutBoard(BoardState.CreateStandard(), 6, "sale"));
            ActionExecutionState after = Succeed(before, new SellCharacterAction(new ActionId("sell-board-" + star), "sale"));
            after.Board.TryGetCharacterIdAt(6, out string slot);
            Assert.That(slot, Is.Null);
            Assert.That(after.Ownership.OwnedCount, Is.Zero);
            Assert.That(after.Economy.Gold, Is.EqualTo(10 + 2 * copies));
            Assert.That(after.CharacterPool.Entries["hero"].RemainingCopies, Is.EqualTo(15));
            Assert.That(after.CharacterPool.Entries["hero"].OwnedCopies, Is.Zero);
            Assert.That(after.CharacterPool.Entries["hero"].InitialStockLimit, Is.EqualTo(15));
            Assert.That(after.InstanceFactory.LastIssuedNumber, Is.EqualTo(before.InstanceFactory.LastIssuedNumber));
            SameRandomState(before.RandomState, after.RandomState);
            Assert.That(BoardHandStateValidator.Validate(after.Board, after.Hand, after.Ownership).IsValid, Is.True);
        }

        [Test]
        public void SellCharacterFromHandClearsSlotAndReturnsStock()
        {
            CharacterInstance unit = Unit("hand-sale", Hero, 2, 3);
            ActionExecutionState before = State(new[] { unit }, hand: PutCharacter(HandState.CreateStandard(), 3, "hand-sale"));
            ActionExecutionState after = Succeed(before, new SellCharacterAction(new ActionId("sell-from-hand"), "hand-sale"));
            after.Hand.TryGetSlot(3, out HandSlotContent slot);
            Assert.That(slot.IsEmpty, Is.True);
            Assert.That(after.Ownership.OwnedCount, Is.Zero);
            Assert.That(after.CharacterPool.Entries["hero"].RemainingCopies, Is.EqualTo(15));
            Assert.That(after.Economy.Gold, Is.EqualTo(19));
        }

        [Test]
        public void SellCharacterRejectsUnknownAndAlreadySoldInstance()
        {
            ActionExecutionState empty = State(Array.Empty<CharacterInstance>());
            ActionDispatchResult unknown = Dispatch(empty, new SellCharacterAction(new ActionId("unknown-sale"), "missing"));
            Assert.That(unknown.Error.Code, Is.EqualTo(ActionErrorCode.UnknownInstance));
            Assert.That(unknown.NextState, Is.SameAs(empty));
            CharacterInstance unit = Unit("sale");
            ActionExecutionState owned = State(new[] { unit }, PutBoard(BoardState.CreateStandard(), 0, "sale"));
            ActionExecutionState sold = Succeed(owned, new SellCharacterAction(new ActionId("first-sale"), "sale"));
            ActionDispatchResult second = Dispatch(sold, new SellCharacterAction(new ActionId("second-sale"), "sale"));
            Assert.That(second.Error.Code, Is.EqualTo(ActionErrorCode.UnknownInstance));
            Assert.That(second.NextState, Is.SameAs(sold));
        }

        [Test]
        public void SellItemRemovesItemForZeroGoldWithoutChangingCharacterPool()
        {
            ActionExecutionState before = State(Array.Empty<CharacterInstance>(),
                hand: PutItem(HandState.CreateStandard(), 4, "potion"),
                economy: new EconomyState(8, 0, 1, 12, 8, 0, 1, 20, 30));
            ActionExecutionState after = Succeed(before, new SellItemAction(new ActionId("sell-item"), 4));
            after.Hand.TryGetSlot(4, out HandSlotContent slot);
            Assert.That(slot.IsEmpty, Is.True);
            Assert.That(after.Economy.Gold, Is.EqualTo(8));
            Assert.That(after.CharacterPool, Is.SameAs(before.CharacterPool));
            SameRandomState(before.RandomState, after.RandomState);
        }

        [Test]
        public void SellItemRejectsCharacterEmptyAndOutOfRangeSlots()
        {
            ActionExecutionState state = State(new[] { Unit("h") }, hand: PutCharacter(HandState.CreateStandard(), 0, "h"));
            ActionDispatchResult character = Dispatch(state, new SellItemAction(new ActionId("sell-character-as-item"), 0));
            Assert.That(character.Error.Code, Is.EqualTo(ActionErrorCode.InvalidTarget));
            Assert.That(character.NextState, Is.SameAs(state));
            ActionDispatchResult empty = Dispatch(State(Array.Empty<CharacterInstance>()),
                new SellItemAction(new ActionId("sell-empty-item"), 1));
            Assert.That(empty.Error.Code, Is.EqualTo(ActionErrorCode.InvalidTarget));
            ActionDispatchResult range = Dispatch(state, new SellItemAction(new ActionId("sell-item-range"), 5));
            Assert.That(range.Error.Code, Is.EqualTo(ActionErrorCode.InvalidSlot));
            Assert.That(range.NextState, Is.SameAs(state));
        }

        [Test]
        public void DispatcherChecksPhaseDedupesSuccessAndAllowsRetryAfterFailure()
        {
            var dispatcher = Dispatcher();
            var retry = new MoveOrSwapBoardAction(new ActionId("retry-id"), 0, 1);
            ActionExecutionState empty = State(Array.Empty<CharacterInstance>());
            ActionDispatchResult failed = dispatcher.Dispatch(empty, retry, new ActionExecutionContext());
            Assert.That(failed.Error.Code, Is.EqualTo(ActionErrorCode.InvalidTarget));
            Assert.That(failed.NextState, Is.SameAs(empty));
            ActionExecutionState occupied = State(new[] { Unit("h") }, PutBoard(BoardState.CreateStandard(), 0, "h"));
            ActionDispatchResult success = dispatcher.Dispatch(occupied, retry, new ActionExecutionContext());
            Assert.That(success.Status, Is.EqualTo(ActionDispatchStatus.Succeeded), success.Error.Detail);
            ActionDispatchResult duplicate = dispatcher.Dispatch(success.NextState, retry, new ActionExecutionContext());
            Assert.That(duplicate.Error.Code, Is.EqualTo(ActionErrorCode.DuplicateAction));
            var config = new StageProgressConfig(20, 4, 2, 4, 0, 10, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            ActionExecutionState wrongPhase = State(Array.Empty<CharacterInstance>(), stage: StageProgressRules.CreateInitialState(config));
            ActionDispatchResult phase = dispatcher.Dispatch(wrongPhase,
                new MoveOrSwapHandAction(new ActionId("phase-id"), 0, 1), new ActionExecutionContext());
            Assert.That(phase.Error.Code, Is.EqualTo(ActionErrorCode.InvalidPhase));
            Assert.That(phase.NextState, Is.SameAs(wrongPhase));
        }

        [Test]
        public void SuccessfulMovementAndSalesAreDeterministicAndDoNotAdvanceAnyRng()
        {
            ActionExecutionState Run()
            {
                var owned = new[] { Unit("h", Hero), Unit("m", Mage) };
                ActionExecutionState state = State(owned, PutBoard(BoardState.CreateStandard(), 0, "h"),
                    PutCharacter(HandState.CreateStandard(), 1, "m"), seed: 808UL);
                state = Succeed(state, new MoveOrSwapBoardAction(new ActionId("det-move"), 0, 2));
                state = Succeed(state, new MoveOrSwapHandAction(new ActionId("det-hand"), 1, 4));
                state = Succeed(state, new MoveHandToBoardAction(new ActionId("det-place"), 4, 0));
                return Succeed(state, new SellCharacterAction(new ActionId("det-sale"), "h"));
            }
            ActionExecutionState first = Run(), second = Run();
            Assert.That(first.Economy.Gold, Is.EqualTo(second.Economy.Gold));
            Assert.That(first.Board.InstanceIds, Is.EqualTo(second.Board.InstanceIds));
            Assert.That(first.Hand.Slots.Select(s => s.Kind), Is.EqualTo(second.Hand.Slots.Select(s => s.Kind)));
            Assert.That(first.Ownership.OwnedCount, Is.EqualTo(second.Ownership.OwnedCount));
            Assert.That(first.ExecutedActionIds.Select(id => id.Value), Is.EqualTo(second.ExecutedActionIds.Select(id => id.Value)));
            SameRandomState(first.RandomState, second.RandomState);
            SameRandomState(new SimulationRandomStreams(808UL).CaptureState(), first.RandomState);
        }
    }
}
