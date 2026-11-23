using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Tests
{
    public sealed class MergeRulesTests
    {
        [Test]
        public void ThreeOneStarCopiesMergeIntoTwoStars()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 4),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 2));
            Pcg32State random = new Pcg32RandomSource(42, 7).CaptureState();
            CharacterMergeResult result = Run(world, 1, Stats(), random);
            AssertSuccess(result, world);
            Assert.That(result.StarBefore, Is.EqualTo(1));
            Assert.That(result.StarAfter, Is.EqualTo(2));
            Assert.That(result.SurvivingInstanceId, Is.EqualTo(world.Characters[0].InstanceId));
            Assert.That(result.NextOwnership.OwnedCount, Is.EqualTo(1));
        }

        [Test]
        public void ThreeTwoStarCopiesMergeIntoThreeStars()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 1),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1));
            world = SetStars(world, 2);
            CharacterMergeResult result = Run(world, 2, Stats(), new Pcg32RandomSource(4, 9).CaptureState());
            AssertSuccess(result, world);
            Assert.That(result.StarAfter, Is.EqualTo(3));
            Assert.That(result.NextOwnership.TryGet(result.SurvivingInstanceId, out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.Star, Is.EqualTo(3));
        }

        [Test]
        public void ThreeStarCopiesCannotMerge()
        {
            World world = SetStars(MakeWorld(new Placement(0, Place.Hand, 0),
                new Placement(0, Place.Hand, 1), new Placement(0, Place.Hand, 2)), 3);
            Pcg32State before = new Pcg32RandomSource(5, 1).CaptureState();
            AssertFailure(Run(world, 3, Stats(), before), CharacterMergeFailureReason.MaximumStarReached, world, before);
        }

        [Test]
        public void DifferentDefinitionsCannotMerge()
        {
            World world = MakeWorld(new Placement(0, Place.Hand, 0),
                new Placement(0, Place.Hand, 1), new Placement(1, Place.Hand, 2));
            Pcg32State before = new Pcg32RandomSource(6, 1).CaptureState();
            AssertFailure(Run(world, 1, Stats(), before), CharacterMergeFailureReason.InsufficientCandidates, world, before);
        }

        [Test]
        public void DifferentStarsCannotMergeTogether()
        {
            World world = MakeWorld(new Placement(0, Place.Hand, 0),
                new Placement(0, Place.Hand, 1), new Placement(0, Place.Hand, 2));
            world.Characters[2].TrySetStar(2, world.Definitions[0], Stats(), new Pcg32RandomSource(8, 2));
            world = RebuildOwnership(world);
            Pcg32State before = new Pcg32RandomSource(9, 1).CaptureState();
            AssertFailure(Run(world, 1, Stats(), before), CharacterMergeFailureReason.InsufficientCandidates, world, before);
        }

        [Test]
        public void RepeatedInstanceIdInInvalidInputIsRejectedAndNotCountedTwice()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 0),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1));
            BoardState invalidBoard = new BoardState(3, 3, new[]
            {
                world.Characters[0].InstanceId, world.Characters[0].InstanceId, null, null, null, null, null, null, null
            });
            Pcg32State before = new Pcg32RandomSource(2, 3).CaptureState();
            CharacterMergeResult result = MergeRules.TryMerge(invalidBoard, world.Hand, world.Ownership,
                world.Definitions[0], 1, Stats(), before);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CharacterMergeFailureReason.InvalidInputState));
            Assert.That(result.NextBoard, Is.SameAs(invalidBoard));
            Assert.That(result.ConsumedInstanceIds, Is.Empty);
            AssertStateEqual(before, result.NextStatsRandomState);
        }

        [Test]
        public void BoardCandidateIsPreferredOverEarlierHandSlot()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 7),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1));
            CharacterMergeResult result = Run(world, 1, Stats(), new Pcg32RandomSource(12, 2).CaptureState());
            AssertSuccess(result, world);
            Assert.That(result.SurvivingInstanceId, Is.EqualTo(world.Characters[0].InstanceId));
            AssertBoard(result.NextBoard, 7, world.Characters[0].InstanceId);
        }

        [Test]
        public void HandCandidatesUseAscendingSlotOrderWhenBoardIsEmpty()
        {
            World world = MakeWorld(new Placement(0, Place.Hand, 4),
                new Placement(0, Place.Hand, 1), new Placement(0, Place.Hand, 3));
            CharacterMergeResult result = Run(world, 1, Stats(), new Pcg32RandomSource(13, 2).CaptureState());
            AssertSuccess(result, world);
            Assert.That(result.SurvivingInstanceId, Is.EqualTo(world.Characters[1].InstanceId));
            CollectionAssert.AreEqual(new[] { world.Characters[2].InstanceId, world.Characters[0].InstanceId },
                result.ConsumedInstanceIds);
        }

        [Test]
        public void SurplusCandidatesAreConsumedInOrderAndOnlyTwoAreRemoved()
        {
            World world = MakeWorld(new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1),
                new Placement(0, Place.Hand, 2), new Placement(0, Place.Hand, 4));
            CharacterMergeResult result = Run(world, 1, Stats(), new Pcg32RandomSource(14, 3).CaptureState());
            AssertSuccess(result, world);
            CollectionAssert.AreEqual(new[] { world.Characters[1].InstanceId, world.Characters[2].InstanceId },
                result.ConsumedInstanceIds);
            Assert.That(result.NextOwnership.Contains(world.Characters[3].InstanceId), Is.True);
            AssertHandHas(result.NextHand, 4, world.Characters[3].InstanceId);
        }

        [Test]
        public void MergeDoesNotChainIntoAnotherAvailableTier()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 0), new Placement(0, Place.Hand, 0),
                new Placement(0, Place.Hand, 1), new Placement(0, Place.Hand, 2),
                new Placement(0, Place.Hand, 3), new Placement(0, Place.Hand, 4));
            foreach (CharacterInstance character in world.Characters.Skip(3))
                Assert.That(character.TrySetStar(2, world.Definitions[0], Stats(), new Pcg32RandomSource(15, 1)), Is.True);
            world = RebuildOwnership(world);

            CharacterMergeResult result = Run(world, 1, Stats(), new Pcg32RandomSource(16, 1).CaptureState());
            AssertSuccess(result, world);
            Assert.That(result.NextOwnership.OwnedCount, Is.EqualTo(4));
            Assert.That(result.NextOwnership.GetOwnedInstances().Count(view => view.Star == 2), Is.EqualTo(4));
            Assert.That(result.StarAfter, Is.EqualTo(2));
        }

        [Test]
        public void SurvivorIdentityRunCostAndPlacementArePreserved()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 2),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1));
            int runCost = world.Characters[0].RunCost;
            string id = world.Characters[0].InstanceId;
            CharacterMergeResult result = Run(world, 1, Stats(), new Pcg32RandomSource(17, 4).CaptureState());
            AssertSuccess(result, world);
            Assert.That(result.NextOwnership.TryGet(id, out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.InstanceId, Is.EqualTo(id));
            Assert.That(survivor.DefinitionId, Is.EqualTo(world.Definitions[0].DefinitionId));
            Assert.That(survivor.RunCost, Is.EqualTo(runCost));
            AssertBoard(result.NextBoard, 2, id);
        }

        [Test]
        public void ConsumedInstancesAreRemovedFromOwnershipAndTheirSlotsCleared()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 4),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 2));
            CharacterMergeResult result = Run(world, 1, Stats(), new Pcg32RandomSource(18, 2).CaptureState());
            AssertSuccess(result, world);
            Assert.That(result.ConsumedInstanceIds.Count, Is.EqualTo(2));
            foreach (string id in result.ConsumedInstanceIds)
            {
                Assert.That(result.NextOwnership.Contains(id), Is.False);
                Assert.That(result.NextBoard.TryFindInstance(id, out _), Is.False);
                Assert.That(result.NextHand.TryFindCharacter(id, out _), Is.False);
            }
            Assert.That(result.NextBoard.InstanceIds[4], Is.EqualTo(result.SurvivingInstanceId));
            Assert.That(result.NextHand.Slots[0].Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Assert.That(result.NextHand.Slots[2].Kind, Is.EqualTo(HandSlotContentKind.Empty));
        }

        [Test]
        public void StatsAreRerolledUsingSurvivorRunCostAndOnlyStatsCheckpointAdvances()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 0),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1));
            CharacterStatDefinition stats = new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(1,
                    new[] { new CharacterStatOption(1, 2, 1) },
                    new[] { new CharacterStatOption(30, 40, 1) },
                    new[] { new CharacterStatOption(50, 60, 1) })
            });
            var streams = new SimulationRandomStreams(123UL);
            var before = streams.CaptureState();
            CharacterMergeResult result = Run(world, 1, stats, before.Stats);
            AssertSuccess(result, world);
            Assert.That(result.NextOwnership.TryGet(result.SurvivingInstanceId, out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.Attack, Is.EqualTo(30));
            Assert.That(survivor.Defense, Is.EqualTo(40));
            AssertStateEqual(before.Shop, streams.Shop.CaptureState());
            AssertStateEqual(before.Stats, streams.Stats.CaptureState());
            AssertStateEqual(before.Battle, streams.Battle.CaptureState());
            AssertStateEqual(before.Event, streams.Event.CaptureState());
            Assert.That(result.NextStatsRandomState.State, Is.Not.EqualTo(before.Stats.State));
        }

        [Test]
        public void FailurePreservesAllInputStatesAndStatsRandomCheckpoint()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 0), new Placement(0, Place.Hand, 0));
            Pcg32State before = new Pcg32RandomSource(21, 4).CaptureState();
            CharacterMergeResult result = Run(world, 1, Stats(), before);
            AssertFailure(result, CharacterMergeFailureReason.InsufficientCandidates, world, before);
        }

        [Test]
        public void InvalidStatsConfigurationFailsWithoutAdvancingRandomState()
        {
            World world = MakeWorld(new Placement(0, Place.Board, 0),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1));
            var invalid = new CharacterStatDefinition(null);
            Pcg32State before = new Pcg32RandomSource(22, 7).CaptureState();
            CharacterMergeResult result = Run(world, 1, invalid, before);
            AssertFailure(result, CharacterMergeFailureReason.StatsRerollFailed, world, before);
        }

        [Test]
        public void SameSeedAndInputsProduceSameMergeAndNextStatsState()
        {
            World first = MakeWorld(new Placement(0, Place.Board, 0),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1));
            World second = MakeWorld(new Placement(0, Place.Board, 0),
                new Placement(0, Place.Hand, 0), new Placement(0, Place.Hand, 1));
            Pcg32State stateA = new Pcg32RandomSource(777, 98).CaptureState();
            Pcg32State stateB = new Pcg32RandomSource(777, 98).CaptureState();
            CharacterMergeResult a = Run(first, 1, Stats(), stateA);
            CharacterMergeResult b = Run(second, 1, Stats(), stateB);
            Assert.That(a.Succeeded, Is.True);
            Assert.That(a.SurvivingInstanceId, Is.EqualTo(b.SurvivingInstanceId));
            Assert.That(a.NextOwnership.TryGet(a.SurvivingInstanceId, out CharacterInstanceView va), Is.True);
            Assert.That(b.NextOwnership.TryGet(b.SurvivingInstanceId, out CharacterInstanceView vb), Is.True);
            Assert.That((va.Attack, va.Defense), Is.EqualTo((vb.Attack, vb.Defense)));
            AssertStateEqual(a.NextStatsRandomState, b.NextStatsRandomState);
        }

        [Test]
        public void CandidatesWithDifferentRunCostStillMatchByDefinitionAndStar()
        {
            CharacterDefinition definition = Definition("hero");
            var factory = new CharacterInstanceFactory("COST");
            CharacterInstance boardCopy = factory.Create(definition, 1);
            CharacterInstance handCopyA = factory.Create(definition, 2);
            CharacterInstance handCopyB = factory.Create(definition, 3);
            BoardState board = BoardState.CreateStandard();
            board.TryWithCharacterAt(0, boardCopy.InstanceId, out board);
            HandState hand = HandState.CreateStandard();
            hand.TryWithSlot(0, HandSlotContent.Character(handCopyA.InstanceId), out hand);
            hand.TryWithSlot(1, HandSlotContent.Character(handCopyB.InstanceId), out hand);
            CharacterOwnershipState owner = new CharacterOwnershipState(new[] { boardCopy, handCopyA, handCopyB });
            CharacterStatDefinition stats = new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(1, null, new[] { new CharacterStatOption(10, 11, 1) }, null),
                new CharacterCostStats(2, null, new[] { new CharacterStatOption(20, 21, 1) }, null),
                new CharacterCostStats(3, null, new[] { new CharacterStatOption(30, 31, 1) }, null)
            });
            CharacterMergeResult result = MergeRules.TryMerge(board, hand, owner, definition, 1,
                stats, new Pcg32RandomSource(25, 4).CaptureState());
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextOwnership.TryGet(boardCopy.InstanceId, out CharacterInstanceView survivor), Is.True);
            Assert.That(survivor.RunCost, Is.EqualTo(1));
            Assert.That(survivor.Attack, Is.EqualTo(10));
        }

        private static CharacterMergeResult Run(World world, int star, CharacterStatDefinition stats, Pcg32State random)
        {
            return MergeRules.TryMerge(world.Board, world.Hand, world.Ownership,
                world.Definitions[0], star, stats, random);
        }

        private static CharacterStatDefinition Stats()
        {
            return new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(1,
                    new[] { new CharacterStatOption(10, 11, 1) },
                    new[] { new CharacterStatOption(20, 21, 1) },
                    new[] { new CharacterStatOption(30, 31, 1) })
            });
        }

        private static CharacterDefinition Definition(string id) => new CharacterDefinition(id, id, 1, null, null);

        private static World MakeWorld(params Placement[] placements)
        {
            CharacterDefinition[] definitions = { Definition("hero"), Definition("mage") };
            var factory = new CharacterInstanceFactory("MERGE");
            var characters = new CharacterInstance[placements.Length];
            var boardIds = new string[9];
            var handSlots = Enumerable.Range(0, 5).Select(_ => HandSlotContent.Empty()).ToArray();
            for (int i = 0; i < placements.Length; i++)
            {
                Placement placement = placements[i];
                characters[i] = factory.Create(definitions[placement.DefinitionIndex], 1);
                if (placement.Place == Place.Board) boardIds[placement.Slot] = characters[i].InstanceId;
                else handSlots[placement.Slot] = HandSlotContent.Character(characters[i].InstanceId);
            }
            return new World(boardIds, handSlots, characters, definitions);
        }

        private static World SetStars(World world, int star)
        {
            CharacterStatDefinition stats = Stats();
            for (int i = 0; i < world.Characters.Length; i++)
                Assert.That(world.Characters[i].TrySetStar(star, world.Definitions[0], stats,
                    new Pcg32RandomSource((ulong)(30 + i), 1)), Is.True);
            return RebuildOwnership(world);
        }

        private static World RebuildOwnership(World world)
        {
            return new World(world.Board.InstanceIds.ToArray(), world.Hand.Slots.ToArray(),
                world.Characters, world.Definitions);
        }

        private static void AssertSuccess(CharacterMergeResult result, World world)
        {
            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.FailureReason, Is.EqualTo(CharacterMergeFailureReason.None));
            Assert.That(result.ConsumedInstanceIds.Count, Is.EqualTo(2));
            Assert.That(BoardHandStateValidator.Validate(result.NextBoard, result.NextHand,
                result.NextOwnership).IsValid, Is.True);
        }

        private static void AssertFailure(CharacterMergeResult result, CharacterMergeFailureReason reason,
            World world, Pcg32State random)
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(reason));
            Assert.That(result.NextBoard, Is.SameAs(world.Board));
            Assert.That(result.NextHand, Is.SameAs(world.Hand));
            Assert.That(result.NextOwnership, Is.SameAs(world.Ownership));
            Assert.That(result.ConsumedInstanceIds, Is.Empty);
            AssertStateEqual(random, result.NextStatsRandomState);
        }

        private static void AssertBoard(BoardState board, int slot, string expected)
        { Assert.That(board.TryGetCharacterIdAt(slot, out string actual), Is.True); Assert.That(actual, Is.EqualTo(expected)); }
        private static void AssertHandHas(HandState hand, int slot, string expected)
        { Assert.That(hand.Slots[slot].CharacterInstanceId, Is.EqualTo(expected)); }

        private static void AssertStateEqual(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        private enum Place { Board, Hand }
        private readonly struct Placement
        {
            public int DefinitionIndex { get; }
            public Place Place { get; }
            public int Slot { get; }
            public Placement(int definitionIndex, Place place, int slot)
            { DefinitionIndex = definitionIndex; Place = place; Slot = slot; }
        }
        private sealed class World
        {
            public BoardState Board { get; }
            public HandState Hand { get; }
            public CharacterOwnershipState Ownership { get; }
            public CharacterInstance[] Characters { get; set; }
            public CharacterDefinition[] Definitions { get; }
            public World(string[] board, HandSlotContent[] hand, CharacterInstance[] characters,
                CharacterDefinition[] definitions)
            {
                Board = new BoardState(3, 3, board);
                Hand = new HandState(hand);
                Characters = characters;
                Definitions = definitions;
                Ownership = new CharacterOwnershipState(characters);
            }
        }
    }
}
