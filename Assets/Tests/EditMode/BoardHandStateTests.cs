using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BoardHandStateTests
    {
        [Test]
        public void StandardBoard_IsNineEmptySlotsInThreeByThreeGrid()
        {
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.Columns, Is.EqualTo(3));
            Assert.That(board.Rows, Is.EqualTo(3));
            Assert.That(board.SlotCount, Is.EqualTo(9));
            for (int i = 0; i < board.SlotCount; i++)
            {
                Assert.That(board.TryGetCharacterIdAt(i, out string id), Is.True);
                Assert.That(id, Is.Null);
            }
        }

        [Test]
        public void Board_ValidatesDimensionsAndSlotCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardState.CreateEmpty(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardState.CreateEmpty(3, 0));
            Assert.Throws<ArgumentException>(() => new BoardState(3, 3, new string[8]));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardState.CreateEmpty(int.MaxValue, int.MaxValue));
        }

        [Test]
        public void Board_SafelyRejectsOutOfRangeReadsAndCopyWrites()
        {
            BoardState board = BoardState.CreateStandard();
            Assert.That(board.TryGetCharacterIdAt(-1, out string negative), Is.False);
            Assert.That(negative, Is.Null);
            Assert.That(board.TryGetCharacterIdAt(9, out string pastEnd), Is.False);
            Assert.That(board.TryWithCharacterAt(9, "C1", out BoardState unchanged), Is.False);
            Assert.That(unchanged, Is.SameAs(board));
            Assert.That(board.TryWithCharacterAt(0, "C1", out BoardState changed), Is.True);
            Assert.That(board.TryGetCharacterIdAt(0, out string original), Is.True);
            Assert.That(original, Is.Null);
            Assert.That(changed.TryGetCharacterIdAt(0, out string updated), Is.True);
            Assert.That(updated, Is.EqualTo("C1"));
        }

        [Test]
        public void Board_DefensiveCopiesInputAndDoesNotExposeMutableSlots()
        {
            var input = new List<string> { "C1", null };
            BoardState board = new BoardState(2, 1, input);
            input[0] = "changed";
            Assert.That(board.InstanceIds[0], Is.EqualTo("C1"));

            IList<string> exposed = board.InstanceIds as IList<string>;
            Assert.That(exposed, Is.Not.Null);
            Assert.That(exposed.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => exposed[0] = "changed-again");
        }

        [Test]
        public void Board_CanFindCharacterSlotAndCountDuplicateIds()
        {
            BoardState board = new BoardState(3, 1, new[] { "C1", "C2", "C1" });
            Assert.That(board.TryFindInstance("C2", out int index), Is.True);
            Assert.That(index, Is.EqualTo(1));
            Assert.That(board.CountOccurrences("C1"), Is.EqualTo(2));
            CollectionAssert.AreEquivalent(new[] { "C1" }, board.FindDuplicateInstanceIds());
        }

        [Test]
        public void StandardHand_HasFiveEmptySlots()
        {
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.SlotCount, Is.EqualTo(5));
            Assert.That(hand.EmptySlotCount, Is.EqualTo(5));
            for (int i = 0; i < hand.SlotCount; i++)
            {
                Assert.That(hand.TryGetSlot(i, out HandSlotContent slot), Is.True);
                Assert.That(slot.Kind, Is.EqualTo(HandSlotContentKind.Empty));
            }
        }

        [Test]
        public void Hand_CalculatesEmptySlotsAndRejectsOutOfRangeAccess()
        {
            HandState hand = HandState.CreateEmpty(3);
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Item("item-potion"), out HandState withItem), Is.True);
            Assert.That(withItem.EmptySlotCount, Is.EqualTo(2));
            Assert.That(withItem.TryGetSlot(-1, out HandSlotContent invalid), Is.False);
            Assert.That(invalid, Is.Null);
            Assert.That(withItem.TryGetSlot(3, out invalid), Is.False);
        }

        [Test]
        public void Hand_CharacterAndItemAreExclusiveImmutableSlotValues()
        {
            HandSlotContent character = HandSlotContent.Character("C1");
            HandSlotContent item = HandSlotContent.Item("item-potion");
            Assert.That(character.Kind, Is.EqualTo(HandSlotContentKind.Character));
            Assert.That(character.CharacterInstanceId, Is.EqualTo("C1"));
            Assert.That(character.ItemDefinitionId, Is.Null);
            Assert.That(item.Kind, Is.EqualTo(HandSlotContentKind.Item));
            Assert.That(item.ItemDefinitionId, Is.EqualTo("item-potion"));
            Assert.That(item.CharacterInstanceId, Is.Null);
            Assert.Throws<ArgumentException>(() => HandSlotContent.Character(" "));
            Assert.Throws<ArgumentException>(() => HandSlotContent.Item(""));
        }

        [Test]
        public void Hand_DefensiveCopiesSlotsAndDoesNotExposeMutableCollection()
        {
            var input = new List<HandSlotContent> { HandSlotContent.Character("C1") };
            HandState hand = new HandState(input);
            input[0] = HandSlotContent.Item("item-x");
            Assert.That(hand.Slots[0].Kind, Is.EqualTo(HandSlotContentKind.Character));
            IList<HandSlotContent> exposed = hand.Slots as IList<HandSlotContent>;
            Assert.That(exposed.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => exposed[0] = HandSlotContent.Empty());
        }

        [Test]
        public void Hand_DetectsDuplicateInstanceIds()
        {
            HandState hand = new HandState(new[]
            {
                HandSlotContent.Character("C1"), HandSlotContent.Character("C1"), HandSlotContent.Empty()
            });
            Assert.That(hand.CountOccurrences("C1"), Is.EqualTo(2));
            CollectionAssert.AreEquivalent(new[] { "C1" }, hand.FindDuplicateInstanceIds());
        }

        [Test]
        public void Ownership_UsesFactoryIdsAndAllowsSameDefinitionInstances()
        {
            var defs = Definitions();
            var factory = new CharacterInstanceFactory("T");
            CharacterInstance first = factory.Create(defs[0], 1);
            CharacterInstance second = factory.Create(defs[0], 1);
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { first, second });

            Assert.That(first.InstanceId, Is.Not.EqualTo(second.InstanceId));
            Assert.That(first.DefinitionId, Is.EqualTo(second.DefinitionId));
            Assert.That(ownership.OwnedCount, Is.EqualTo(2));
            Assert.That(ownership.TryGet(first.InstanceId, out CharacterInstanceView view), Is.True);
            Assert.That(view.InstanceId, Is.EqualTo(first.InstanceId));
            Assert.That(view.DefinitionId, Is.EqualTo(defs[0].DefinitionId));
        }

        [Test]
        public void Ownership_RejectsDuplicateInstanceIds()
        {
            CharacterDefinition definition = Definitions()[0];
            var duplicateA = new CharacterInstance("same-id", definition, 1);
            var duplicateB = new CharacterInstance("same-id", definition, 1);
            Assert.Throws<ArgumentException>(() => new CharacterOwnershipState(new[] { duplicateA, duplicateB }));
        }

        [Test]
        public void Ownership_DefensiveCopiesInputAndReturnsReadOnlyViews()
        {
            CharacterInstance instance = new CharacterInstanceFactory("OWN").Create(Definitions()[0], 1);
            var input = new List<CharacterInstance> { instance };
            CharacterOwnershipState ownership = new CharacterOwnershipState(input);
            input.Clear();
            Assert.That(ownership.OwnedCount, Is.EqualTo(1));

            IDictionary<string, CharacterInstanceView> views =
                ownership.Instances as IDictionary<string, CharacterInstanceView>;
            Assert.That(views, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => views.Add("other", ownership.Instances[instance.InstanceId]));
            IList<CharacterInstanceView> list = ownership.GetOwnedInstances() as IList<CharacterInstanceView>;
            Assert.That(list.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => list.Clear());
        }

        [Test]
        public void Ownership_CopyGenerationDoesNotModifyOriginalCollection()
        {
            var factory = new CharacterInstanceFactory("COPY");
            CharacterInstance first = factory.Create(Definitions()[0], 1);
            CharacterOwnershipState original = new CharacterOwnershipState(new[] { first });
            CharacterInstance second = factory.Create(Definitions()[1], 2);

            Assert.That(original.TryWithAdded(second, out CharacterOwnershipState added), Is.True);
            Assert.That(original.OwnedCount, Is.EqualTo(1));
            Assert.That(added.OwnedCount, Is.EqualTo(2));
            Assert.That(added.TryWithRemoved(first.InstanceId, out CharacterOwnershipState removed), Is.True);
            Assert.That(added.OwnedCount, Is.EqualTo(2));
            Assert.That(removed.OwnedCount, Is.EqualTo(1));
            Assert.That(removed.Contains(second.InstanceId), Is.True);
            Assert.That(added.TryWithAdded(second, out CharacterOwnershipState unchanged), Is.False);
            Assert.That(unchanged, Is.SameAs(added));
        }

        [Test]
        public void Ownership_ConstructorDetachesMutableCharacterInstance()
        {
            CharacterDefinition definition = Definitions()[0];
            CharacterStatDefinition settings = new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(1,
                    new[] { new CharacterStatOption(11, 21, 1) },
                    new[] { new CharacterStatOption(31, 41, 1) },
                    new[] { new CharacterStatOption(51, 61, 1) })
            });
            var random = new Pcg32RandomSource(12, 34);
            CharacterInstance source = new CharacterInstanceFactory("DETACH").Create(definition, 1);
            Assert.That(source.TrySetStar(2, definition, settings, random), Is.True);
            source.TemporaryAttackBonus = 7;
            source.TemporaryDefenseBonus = 8;
            Assert.That(source.TrySetTemporaryStar(3, definition, settings, random), Is.True);
            source.TemporaryAttackBonus = 17;

            CharacterOwnershipState beforeSnapshot = new CharacterOwnershipState(new[] { source });
            Assert.That(beforeSnapshot.TryGet(source.InstanceId, out CharacterInstanceView before), Is.True);
            Pcg32State rngBeforeCopy = random.CaptureState();
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { source });
            Pcg32State rngAfterCopy = random.CaptureState();

            Assert.That(ownership.TryGet(source.InstanceId, out CharacterInstanceView owned), Is.True);
            AssertViewEqual(before, owned);
            AssertPcgStateEqual(rngBeforeCopy, rngAfterCopy);

            Assert.That(source.TrySetStar(1, definition, settings, random), Is.True);
            source.TemporaryAttackBonus = -50;
            source.TemporaryDefenseBonus = -60;
            AssertViewEqual(before, GetView(ownership, source.InstanceId));
        }

        [Test]
        public void Ownership_InstancesWithSameDefinitionRemainIndependent()
        {
            CharacterDefinition definition = Definitions()[0];
            var first = new CharacterInstanceFactory("PAIR").Create(definition, 1);
            var second = new CharacterInstanceFactory("PAIR", 1).Create(definition, 1);
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { first, second });
            first.TemporaryAttackBonus = 9;
            first.TemporaryDefenseBonus = 4;

            Assert.That(GetView(ownership, first.InstanceId).TemporaryAttackBonus, Is.Zero);
            Assert.That(GetView(ownership, second.InstanceId).TemporaryAttackBonus, Is.Zero);
            Assert.That(GetView(ownership, second.InstanceId).TemporaryDefenseBonus, Is.Zero);
        }

        [Test]
        public void Ownership_ReplacingCreatesDetachedNextStateAndPreservesOldState()
        {
            CharacterDefinition definition = Definitions()[0];
            CharacterInstance source = new CharacterInstanceFactory("REPLACE").Create(definition, 1);
            CharacterOwnershipState original = new CharacterOwnershipState(new[] { source });
            CharacterInstance replacement = new CharacterInstance(source.InstanceId, definition, 1);
            replacement.TemporaryAttackBonus = 14;

            Assert.That(original.TryWithReplaced(replacement, out CharacterOwnershipState next), Is.True);
            replacement.TemporaryAttackBonus = 99;
            Assert.That(GetView(original, source.InstanceId).TemporaryAttackBonus, Is.Zero);
            Assert.That(GetView(next, source.InstanceId).TemporaryAttackBonus, Is.EqualTo(14));
        }

        [Test]
        public void Ownership_ReplacementCannotChangeDefinitionOrRunCostForAnInstanceId()
        {
            CharacterDefinition[] definitions = Definitions();
            CharacterInstance source = new CharacterInstanceFactory("IDENTITY").Create(definitions[0], 1);
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { source });
            var differentDefinition = new CharacterInstance(source.InstanceId, definitions[1], 1);
            var differentRunCost = new CharacterInstance(source.InstanceId, definitions[0], 2);

            Assert.That(ownership.TryWithReplaced(differentDefinition, out CharacterOwnershipState unchanged), Is.False);
            Assert.That(unchanged, Is.SameAs(ownership));
            Assert.That(ownership.TryWithReplaced(differentRunCost, out unchanged), Is.False);
            Assert.That(unchanged, Is.SameAs(ownership));
        }

        [Test]
        public void CharacterInstanceView_HasNoWritableProperties()
        {
            foreach (var property in typeof(CharacterInstanceView).GetProperties())
                Assert.That(property.SetMethod, Is.Null, property.Name + " must be read-only.");
        }

        [Test]
        public void Ownership_CopyPreservesIdentityRunCostStarAndStatsWithoutAdvancingRng()
        {
            CharacterDefinition definition = Definitions()[0];
            CharacterStatDefinition settings = new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(2,
                    new[] { new CharacterStatOption(10, 20, 1) },
                    new[] { new CharacterStatOption(30, 40, 1) },
                    new[] { new CharacterStatOption(50, 60, 1) })
            });
            var random = new Pcg32RandomSource(901, 27);
            CharacterInstance source = new CharacterInstanceFactory("COPYVAL").Create(definition, 2);
            Assert.That(source.TrySetStar(2, definition, settings, random), Is.True);
            Pcg32State before = random.CaptureState();

            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { source });
            AssertPcgStateEqual(before, random.CaptureState());
            Assert.That(ownership.TryGet(source.InstanceId, out CharacterInstanceView copy), Is.True);
            Assert.That(copy.InstanceId, Is.EqualTo(source.InstanceId));
            Assert.That(copy.DefinitionId, Is.EqualTo(source.DefinitionId));
            Assert.That(copy.RunCost, Is.EqualTo(source.RunCost));
            Assert.That(copy.Star, Is.EqualTo(source.Star));
            Assert.That(copy.Attack, Is.EqualTo(source.Attack));
            Assert.That(copy.Defense, Is.EqualTo(source.Defense));
            Assert.That(copy.HasRolledStats, Is.EqualTo(source.HasRolledStats));
        }

        private static CharacterInstanceView GetView(CharacterOwnershipState ownership, string id)
        {
            Assert.That(ownership.TryGet(id, out CharacterInstanceView view), Is.True);
            return view;
        }

        private static void AssertViewEqual(CharacterInstanceView expected, CharacterInstanceView actual)
        {
            Assert.That(actual.InstanceId, Is.EqualTo(expected.InstanceId));
            Assert.That(actual.DefinitionId, Is.EqualTo(expected.DefinitionId));
            Assert.That(actual.RunCost, Is.EqualTo(expected.RunCost));
            Assert.That(actual.Star, Is.EqualTo(expected.Star));
            Assert.That(actual.Attack, Is.EqualTo(expected.Attack));
            Assert.That(actual.Defense, Is.EqualTo(expected.Defense));
            Assert.That(actual.TemporaryAttackBonus, Is.EqualTo(expected.TemporaryAttackBonus));
            Assert.That(actual.TemporaryDefenseBonus, Is.EqualTo(expected.TemporaryDefenseBonus));
            Assert.That(actual.HasRolledStats, Is.EqualTo(expected.HasRolledStats));
            Assert.That(actual.HasTemporaryStar, Is.EqualTo(expected.HasTemporaryStar));
        }

        private static void AssertPcgStateEqual(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        [Test]
        public void Validator_AcceptsEveryOwnedInstanceInExactlyOneBoardOrHandSlot()
        {
            var instances = MakeInstances();
            CharacterOwnershipState ownership = new CharacterOwnershipState(instances.Take(2));
            BoardState board = BoardState.CreateStandard();
            board.TryWithCharacterAt(0, instances[0].InstanceId, out board);
            HandState hand = HandState.CreateStandard();
            hand.TryWithSlot(2, HandSlotContent.Character(instances[1].InstanceId), out hand);
            hand.TryWithSlot(4, HandSlotContent.Item("item-potion"), out hand);

            BoardHandStateValidationResult result = BoardHandStateValidator.Validate(board, hand, ownership);
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Errors));
            Assert.That(BoardHandStateValidator.TryFindLocation(board, hand, instances[0].InstanceId,
                out CharacterLocation boardLocation), Is.True);
            Assert.That(boardLocation.Kind, Is.EqualTo(CharacterLocationKind.Board));
            Assert.That(boardLocation.SlotIndex, Is.Zero);
            Assert.That(BoardHandStateValidator.TryFindLocation(board, hand, instances[1].InstanceId,
                out CharacterLocation handLocation), Is.True);
            Assert.That(handLocation.Kind, Is.EqualTo(CharacterLocationKind.Hand));
            Assert.That(handLocation.SlotIndex, Is.EqualTo(2));
        }

        [Test]
        public void Validator_DetectsSameInstanceRepeatedOnBoard()
        {
            CharacterInstance instance = MakeInstances()[0];
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { instance });
            BoardState board = new BoardState(3, 1, new[] { instance.InstanceId, instance.InstanceId, null });
            BoardHandStateValidationResult result = BoardHandStateValidator.Validate(board,
                HandState.CreateEmpty(0), ownership);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(error => error.Contains("more than one")), Is.True);
        }

        [Test]
        public void Validator_DetectsSameInstanceRepeatedInHand()
        {
            CharacterInstance instance = MakeInstances()[0];
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { instance });
            HandState hand = new HandState(new[]
            {
                HandSlotContent.Character(instance.InstanceId), HandSlotContent.Character(instance.InstanceId)
            });
            BoardHandStateValidationResult result = BoardHandStateValidator.Validate(
                BoardState.CreateEmpty(1, 1), hand, ownership);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(error => error.Contains("more than one")), Is.True);
        }

        [Test]
        public void Validator_DetectsSameInstanceAcrossBoardAndHand()
        {
            CharacterInstance instance = MakeInstances()[0];
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { instance });
            BoardState board = new BoardState(1, 1, new[] { instance.InstanceId });
            HandState hand = new HandState(new[] { HandSlotContent.Character(instance.InstanceId) });
            Assert.That(BoardHandStateValidator.Validate(board, hand, ownership).IsValid, Is.False);
            Assert.That(BoardHandStateValidator.TryFindLocation(board, hand, instance.InstanceId,
                out CharacterLocation ambiguous), Is.False);
        }

        [Test]
        public void Validator_DetectsReferencesToUnownedInstances()
        {
            BoardState board = new BoardState(1, 1, new[] { "not-owned" });
            BoardHandStateValidationResult result = BoardHandStateValidator.Validate(board,
                HandState.CreateEmpty(0), new CharacterOwnershipState(Array.Empty<CharacterInstance>()));
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(error => error.Contains("unowned")), Is.True);
        }

        [Test]
        public void Validator_DetectsOwnedInstancesNotPlacedAnywhere()
        {
            CharacterInstance instance = MakeInstances()[0];
            BoardHandStateValidationResult result = BoardHandStateValidator.Validate(
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(new[] { instance }));
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(error => error.Contains("not placed")), Is.True);
        }

        [Test]
        public void Validator_DetectsDuplicateDefinitionIdsOnBoardButAllowsCopiesAcrossContainers()
        {
            CharacterInstance[] instances = MakeInstances();
            CharacterOwnershipState ownership = new CharacterOwnershipState(instances.Take(2));
            BoardState duplicateBoard = new BoardState(2, 1,
                new[] { instances[0].InstanceId, instances[1].InstanceId });
            BoardHandStateValidationResult duplicate = BoardHandStateValidator.Validate(duplicateBoard,
                HandState.CreateEmpty(0), ownership);
            Assert.That(duplicate.IsValid, Is.False);
            Assert.That(duplicate.Errors.Any(error => error.Contains("duplicate DefinitionId")), Is.True);

            BoardState splitBoard = new BoardState(1, 1, new[] { instances[0].InstanceId });
            HandState hand = new HandState(new[] { HandSlotContent.Character(instances[1].InstanceId) });
            Assert.That(BoardHandStateValidator.Validate(splitBoard, hand, ownership).IsValid, Is.True);
        }

        [Test]
        public void Validator_RejectsNullInputsWithoutThrowing()
        {
            BoardHandStateValidationResult result = BoardHandStateValidator.Validate(null, null, null);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Count, Is.EqualTo(3));
        }

        private static CharacterDefinition[] Definitions()
        {
            return new[]
            {
                new CharacterDefinition("hero", "Hero", 1, null, null),
                new CharacterDefinition("mage", "Mage", 2, null, null)
            };
        }

        private static CharacterInstance[] MakeInstances()
        {
            var factory = new CharacterInstanceFactory("TEST");
            CharacterDefinition[] definitions = Definitions();
            return new[]
            {
                factory.Create(definitions[0], 1),
                factory.Create(definitions[0], 1),
                factory.Create(definitions[1], 2)
            };
        }
    }
}
