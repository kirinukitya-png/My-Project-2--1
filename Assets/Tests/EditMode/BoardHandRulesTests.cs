using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BoardHandRulesTests
    {
        private static readonly BoardHandOperationContext Prep = BoardHandOperationContext.Preparation;

        [Test]
        public void BoardMoveToEmptySlotMovesCharacterAndReportsBothSlots()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0));
            BoardHandOperationResult result = BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 4, Prep);
            AssertSuccess(result, w.Ownership);
            AssertBoard(result.NextBoard, 0, null);
            AssertBoard(result.NextBoard, 4, w.Characters[0].InstanceId);
            Assert.That(result.ChangedSlots.Count, Is.EqualTo(2));
        }

        [Test]
        public void BoardMoveToOccupiedSlotSwapsCharacters()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0), new Placement(1, Location.Board, 8));
            BoardHandOperationResult result = BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 8, Prep);
            AssertSuccess(result, w.Ownership);
            AssertBoard(result.NextBoard, 0, w.Characters[1].InstanceId);
            AssertBoard(result.NextBoard, 8, w.Characters[0].InstanceId);
        }

        [Test]
        public void BoardMoveToSameSlotIsSuccessfulNoOp()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 2));
            BoardHandOperationResult result = BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 2, 2, Prep);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextBoard, Is.SameAs(w.Board));
            Assert.That(result.NextHand, Is.SameAs(w.Hand));
            Assert.That(result.ChangedSlots, Is.Empty);
        }

        [Test]
        public void BoardMoveRejectsEmptySourceOutOfRangeAndBattlePhase()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0));
            AssertFailure(BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 1, 2, Prep),
                BoardHandOperationFailureReason.SourceEmpty, w);
            AssertFailure(BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, -1, 2, Prep),
                BoardHandOperationFailureReason.SourceSlotOutOfRange, w);
            AssertFailure(BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 9, Prep),
                BoardHandOperationFailureReason.DestinationSlotOutOfRange, w);
            AssertFailure(BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 2,
                    BoardHandOperationContext.Battle),
                BoardHandOperationFailureReason.BattleInProgress, w);
        }

        [Test]
        public void HandCharacterMovesToEmptySlot()
        {
            World w = MakeWorld(new Placement(0, Location.Hand, 0));
            BoardHandOperationResult result = HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 4, Prep);
            AssertSuccess(result, w.Ownership);
            AssertHandCharacter(result.NextHand, 0, null);
            AssertHandCharacter(result.NextHand, 4, w.Characters[0].InstanceId);
        }

        [Test]
        public void HandCharactersSwapAndSameSlotIsNoOp()
        {
            World w = MakeWorld(new Placement(0, Location.Hand, 0), new Placement(1, Location.Hand, 1));
            BoardHandOperationResult result = HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 1, Prep);
            AssertSuccess(result, w.Ownership);
            AssertHandCharacter(result.NextHand, 0, w.Characters[1].InstanceId);
            AssertHandCharacter(result.NextHand, 1, w.Characters[0].InstanceId);
            BoardHandOperationResult same = HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 0, Prep);
            Assert.That(same.Succeeded, Is.True);
            Assert.That(same.ChangedSlots, Is.Empty);
        }

        [Test]
        public void HandItemsMoveAndSwapWithOtherHandContents()
        {
            World w = MakeWorld(new Placement(0, Location.Hand, 0));
            w.Hand.TryWithSlot(1, HandSlotContent.Item("potion"), out w.Hand);
            BoardHandOperationResult move = HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 1, 3, Prep);
            AssertSuccess(move, w.Ownership);
            Assert.That(move.NextHand.Slots[1].Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Assert.That(move.NextHand.Slots[3].ItemDefinitionId, Is.EqualTo("potion"));

            BoardHandOperationResult swap = HandRules.MoveOrSwap(move.NextBoard, move.NextHand,
                w.Ownership, 0, 3, Prep);
            AssertSuccess(swap, w.Ownership);
            Assert.That(swap.NextHand.Slots[0].ItemDefinitionId, Is.EqualTo("potion"));
            AssertHandCharacter(swap.NextHand, 3, w.Characters[0].InstanceId);
        }

        [Test]
        public void HandRejectsEmptySourceOutOfRangeAndBattlePhase()
        {
            World w = MakeWorld(new Placement(0, Location.Hand, 0));
            AssertFailure(HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 1, 2, Prep),
                BoardHandOperationFailureReason.SourceEmpty, w);
            AssertFailure(HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, -1, 2, Prep),
                BoardHandOperationFailureReason.SourceSlotOutOfRange, w);
            AssertFailure(HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 5, Prep),
                BoardHandOperationFailureReason.DestinationSlotOutOfRange, w);
            AssertFailure(HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 2,
                    BoardHandOperationContext.Battle),
                BoardHandOperationFailureReason.BattleInProgress, w);
        }

        [Test]
        public void BoardToHandMovesIntoEmptySlot()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 3));
            BoardHandOperationResult result = BoardHandRules.MoveBoardToHand(w.Board, w.Hand,
                w.Ownership, 3, 1, Prep);
            AssertSuccess(result, w.Ownership);
            AssertBoard(result.NextBoard, 3, null);
            AssertHandCharacter(result.NextHand, 1, w.Characters[0].InstanceId);
        }

        [Test]
        public void BoardToHandSwapsCharactersEvenWhenHandIsFull()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0), new Placement(1, Location.Hand, 0));
            for (int i = 1; i < w.Hand.SlotCount; i++)
                w.Hand.TryWithSlot(i, HandSlotContent.Item("item-" + i), out w.Hand);
            BoardHandOperationResult result = BoardHandRules.MoveBoardToHand(w.Board, w.Hand,
                w.Ownership, 0, 0, Prep);
            AssertSuccess(result, w.Ownership);
            AssertBoard(result.NextBoard, 0, w.Characters[1].InstanceId);
            AssertHandCharacter(result.NextHand, 0, w.Characters[0].InstanceId);
        }

        [Test]
        public void BoardToHandRejectsItemTargetEvenWhenHandIsFull()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0));
            w.Hand.TryWithSlot(0, HandSlotContent.Item("potion"), out w.Hand);
            AssertFailure(BoardHandRules.MoveBoardToHand(w.Board, w.Hand, w.Ownership, 0, 0, Prep),
                BoardHandOperationFailureReason.ItemBoardExchangeForbidden, w);
            for (int i = 1; i < w.Hand.SlotCount; i++)
                w.Hand.TryWithSlot(i, HandSlotContent.Item("item-" + i), out w.Hand);
            Assert.That(w.Hand.EmptySlotCount, Is.Zero);
            AssertFailure(BoardHandRules.MoveBoardToHand(w.Board, w.Hand, w.Ownership, 0, 1, Prep),
                BoardHandOperationFailureReason.ItemBoardExchangeForbidden, w);
        }

        [Test]
        public void HandToBoardMovesCharacterIntoEmptySlot()
        {
            World w = MakeWorld(new Placement(0, Location.Hand, 0));
            BoardHandOperationResult result = BoardHandRules.MoveHandToBoard(w.Board, w.Hand,
                w.Ownership, 0, 6, Prep);
            AssertSuccess(result, w.Ownership);
            AssertHandCharacter(result.NextHand, 0, null);
            AssertBoard(result.NextBoard, 6, w.Characters[0].InstanceId);
        }

        [Test]
        public void HandToBoardSwapsDifferentCharacters()
        {
            World w = MakeWorld(new Placement(0, Location.Hand, 0), new Placement(1, Location.Board, 3));
            BoardHandOperationResult result = BoardHandRules.MoveHandToBoard(w.Board, w.Hand,
                w.Ownership, 0, 3, Prep);
            AssertSuccess(result, w.Ownership);
            AssertHandCharacter(result.NextHand, 0, w.Characters[1].InstanceId);
            AssertBoard(result.NextBoard, 3, w.Characters[0].InstanceId);
        }

        [Test]
        public void HandItemCannotBePlacedOnBoardOrExchangedWithBoardCharacter()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0));
            w.Hand.TryWithSlot(0, HandSlotContent.Item("potion"), out w.Hand);
            AssertFailure(BoardHandRules.MoveHandToBoard(w.Board, w.Hand, w.Ownership, 0, 0, Prep),
                BoardHandOperationFailureReason.ItemCannotBePlacedOnBoard, w);
        }

        [Test]
        public void DuplicateDefinitionIsRejectedByFinalBoardState()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0), new Placement(1, Location.Hand, 0));
            // These two instances use the same Definition ID; Board must not contain both.
            w.Characters = new[] { w.Characters[0], CloneWithSameDefinition(w.Characters[0], "duplicate") };
            w.Ownership = new CharacterOwnershipState(w.Characters);
            w.Hand.TryWithSlot(0, HandSlotContent.Character(w.Characters[1].InstanceId), out w.Hand);
            BoardHandOperationResult result = BoardHandRules.MoveHandToBoard(w.Board, w.Hand,
                w.Ownership, 0, 1, Prep);
            AssertFailure(result, BoardHandOperationFailureReason.DuplicateDefinitionOnBoard, w);
        }

        [Test]
        public void InvalidInputWithDuplicateInstanceOrUnownedReferenceIsRejected()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0));
            BoardState duplicate = new BoardState(3, 3,
                new[] { w.Characters[0].InstanceId, w.Characters[0].InstanceId, null, null, null, null, null, null, null });
            BoardHandOperationResult duplicateResult = BoardRules.MoveOrSwap(duplicate, w.Hand,
                w.Ownership, 0, 2, Prep);
            Assert.That(duplicateResult.Succeeded, Is.False);
            Assert.That(duplicateResult.FailureReason, Is.EqualTo(BoardHandOperationFailureReason.InvalidInputState));
            Assert.That(duplicateResult.NextBoard, Is.SameAs(duplicate));

            BoardState unowned = new BoardState(3, 3,
                new[] { "unowned", null, null, null, null, null, null, null, null });
            BoardHandOperationResult unownedResult = BoardRules.MoveOrSwap(unowned, w.Hand,
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), 0, 1, Prep);
            Assert.That(unownedResult.Succeeded, Is.False);
            Assert.That(unownedResult.FailureReason, Is.EqualTo(BoardHandOperationFailureReason.InvalidInputState));
        }

        [Test]
        public void CrossContainerOperationsRejectOutOfRangeSlots()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0));
            AssertFailure(BoardHandRules.MoveBoardToHand(w.Board, w.Hand, w.Ownership, 9, 0, Prep),
                BoardHandOperationFailureReason.SourceSlotOutOfRange, w);
            AssertFailure(BoardHandRules.MoveBoardToHand(w.Board, w.Hand, w.Ownership, 0, 5, Prep),
                BoardHandOperationFailureReason.DestinationSlotOutOfRange, w);
            AssertFailure(BoardHandRules.MoveHandToBoard(w.Board, w.Hand, w.Ownership, 5, 0, Prep),
                BoardHandOperationFailureReason.SourceSlotOutOfRange, w);
            AssertFailure(BoardHandRules.MoveHandToBoard(w.Board, w.Hand, w.Ownership, 0, 9, Prep),
                BoardHandOperationFailureReason.DestinationSlotOutOfRange, w);
        }

        [Test]
        public void FailedOperationsKeepInputReferencesAndContentsUnchanged()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0));
            string original = w.Board.InstanceIds[0];
            BoardHandOperationResult result = BoardRules.MoveOrSwap(w.Board, w.Hand,
                w.Ownership, 0, 9, Prep);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.NextBoard, Is.SameAs(w.Board));
            Assert.That(result.NextHand, Is.SameAs(w.Hand));
            Assert.That(w.Board.InstanceIds[0], Is.EqualTo(original));
            Assert.That(w.Ownership.OwnedCount, Is.EqualTo(1));
        }

        [Test]
        public void EverySuccessfulOperationPreservesOwnershipAndPlacementInvariants()
        {
            World w = MakeWorld(new Placement(0, Location.Board, 0), new Placement(1, Location.Hand, 0));
            BoardHandOperationResult[] results =
            {
                BoardRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 1, Prep),
                HandRules.MoveOrSwap(w.Board, w.Hand, w.Ownership, 0, 1, Prep),
                BoardHandRules.MoveBoardToHand(w.Board, w.Hand, w.Ownership, 0, 0, Prep),
                BoardHandRules.MoveHandToBoard(w.Board, w.Hand, w.Ownership, 0, 1, Prep)
            };
            foreach (BoardHandOperationResult result in results)
                if (result.Succeeded)
                    Assert.That(BoardHandStateValidator.Validate(result.NextBoard, result.NextHand,
                        w.Ownership).IsValid, Is.True);
            Assert.That(results.All(result => result.Succeeded), Is.True);
        }

        [Test]
        public void RepeatingSameOperationIsDeterministic()
        {
            World a = MakeWorld(new Placement(0, Location.Board, 2), new Placement(1, Location.Hand, 4));
            World b = MakeWorld(new Placement(0, Location.Board, 2), new Placement(1, Location.Hand, 4));
            BoardHandOperationResult first = BoardHandRules.MoveHandToBoard(a.Board, a.Hand,
                a.Ownership, 4, 2, Prep);
            BoardHandOperationResult second = BoardHandRules.MoveHandToBoard(b.Board, b.Hand,
                b.Ownership, 4, 2, Prep);
            Assert.That(first.Succeeded, Is.EqualTo(second.Succeeded));
            CollectionAssert.AreEqual(first.NextBoard.InstanceIds, second.NextBoard.InstanceIds);
            for (int i = 0; i < first.NextHand.SlotCount; i++)
                Assert.That(first.NextHand.Slots[i].CharacterInstanceId,
                    Is.EqualTo(second.NextHand.Slots[i].CharacterInstanceId));
        }

        private static CharacterInstance CloneWithSameDefinition(CharacterInstance instance, string id)
        {
            return new CharacterInstance(id, new CharacterDefinition(instance.DefinitionId,
                instance.DefinitionId, instance.RunCost, null, null), instance.RunCost);
        }

        private static void AssertSuccess(BoardHandOperationResult result, CharacterOwnershipState owner)
        {
            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.FailureReason, Is.EqualTo(BoardHandOperationFailureReason.None));
            Assert.That(BoardHandStateValidator.Validate(result.NextBoard, result.NextHand, owner).IsValid, Is.True);
        }

        private static void AssertFailure(BoardHandOperationResult result,
            BoardHandOperationFailureReason reason, World world)
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(reason));
            Assert.That(result.NextBoard, Is.SameAs(world.Board));
            Assert.That(result.NextHand, Is.SameAs(world.Hand));
            Assert.That(result.ChangedSlots, Is.Empty);
        }

        private static void AssertBoard(BoardState board, int slot, string id)
        {
            Assert.That(board.TryGetCharacterIdAt(slot, out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(id));
        }

        private static void AssertHandCharacter(HandState hand, int slot, string id)
        {
            Assert.That(hand.Slots[slot].Kind, Is.EqualTo(id == null
                ? HandSlotContentKind.Empty : HandSlotContentKind.Character));
            Assert.That(hand.Slots[slot].CharacterInstanceId, Is.EqualTo(id));
        }

        private static World MakeWorld(params Placement[] placements)
        {
            var definitions = new[]
            {
                new CharacterDefinition("hero", "Hero", 1, null, null),
                new CharacterDefinition("mage", "Mage", 2, null, null),
                new CharacterDefinition("rogue", "Rogue", 3, null, null)
            };
            var factory = new CharacterInstanceFactory("RULE");
            var characters = new CharacterInstance[placements.Length];
            var boardIds = new string[9];
            var handSlots = Enumerable.Range(0, 5).Select(_ => HandSlotContent.Empty()).ToArray();
            for (int i = 0; i < placements.Length; i++)
            {
                Placement placement = placements[i];
                characters[i] = factory.Create(definitions[placement.DefinitionIndex], placement.DefinitionIndex + 1);
                if (placement.Location == Location.Board) boardIds[placement.Slot] = characters[i].InstanceId;
                else handSlots[placement.Slot] = HandSlotContent.Character(characters[i].InstanceId);
            }
            return new World
            {
                Board = new BoardState(3, 3, boardIds),
                Hand = new HandState(handSlots),
                Ownership = new CharacterOwnershipState(characters),
                Characters = characters
            };
        }

        private enum Location { Board, Hand }
        private readonly struct Placement
        {
            public int DefinitionIndex { get; }
            public Location Location { get; }
            public int Slot { get; }
            public Placement(int definitionIndex, Location location, int slot)
            { DefinitionIndex = definitionIndex; Location = location; Slot = slot; }
        }
        private sealed class World
        {
            public BoardState Board;
            public HandState Hand;
            public CharacterOwnershipState Ownership;
            public CharacterInstance[] Characters;
        }
    }
}
