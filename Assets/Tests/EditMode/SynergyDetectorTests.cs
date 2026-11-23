using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class SynergyDetectorTests
    {
        [Test]
        public void TwoMemberUnitOnAdjacentSlotsIsEstablished()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            AssertUnit(result, "U", "A", "B");
        }

        [Test]
        public void ThreeMemberUnitOnLineIsEstablished()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            CharacterDefinition c = Def("C", units: new[] { "U" });
            AssertUnit(Detect(new[] { a, b, c }, new[] { 0, 1, 2 }, new[] { a, b, c }, new[] { a, b, c }), "U", "A", "B", "C");
        }

        [Test]
        public void LShapedUnitIsConnected()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            CharacterDefinition c = Def("C", units: new[] { "U" });
            AssertUnit(Detect(new[] { a, b, c }, new[] { 0, 1, 3 }, new[] { a, b, c }, new[] { a, b, c }), "U", "A", "B", "C");
        }

        [Test]
        public void RosterIncompleteAgainstFullCatalogDoesNotEstablishUnit()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            CharacterDefinition c = Def("C", units: new[] { "U" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b, c });
            AssertValid(result);
            Assert.That(result.EstablishedUnits, Is.Empty);
        }

        [Test]
        public void SelectedRosterMemberMissingFromBoardDoesNotEstablishUnit()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            CharacterDefinition c = Def("C", units: new[] { "U" });
            World w = MakeWorld(new[] { a, b, c }, new[] { 0, 1 }, new[] { a, b, c }, new[] { a, b, c }, handDefinitionIds: new[] { "C" });
            SynergyDetectionResult result = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            AssertValid(result);
            Assert.That(result.EstablishedUnits, Is.Empty);
        }

        [Test]
        public void DiagonalOnlyParticipantsAreNotAdjacent()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            AssertNoUnit(Detect(new[] { a, b }, new[] { 0, 4 }, new[] { a, b }, new[] { a, b }));
        }

        [Test]
        public void SeparatedParticipantsAreNotConnectedAcrossEmptySlots()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            AssertNoUnit(Detect(new[] { a, b }, new[] { 0, 2 }, new[] { a, b }, new[] { a, b }));
        }

        [Test]
        public void HandCharacterDoesNotParticipateInUnit()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            World w = MakeWorld(new[] { a, b }, new[] { 0 }, new[] { a, b }, new[] { a, b }, handDefinitionIds: new[] { "B" });
            SynergyDetectionResult result = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            AssertNoUnit(result);
        }

        [Test]
        public void DuplicateUnitTagOnOneDefinitionCountsOnce()
        {
            CharacterDefinition a = Def("A", units: new[] { "U", "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            AssertUnit(result, "U", "A", "B");
            Assert.That(result.EstablishedUnits[0].ParticipantCount, Is.EqualTo(2));
        }

        [Test]
        public void OneCharacterCanParticipateInMultipleUnits()
        {
            CharacterDefinition a = Def("A", units: new[] { "U1", "U2" });
            CharacterDefinition b = Def("B", units: new[] { "U1", "U2" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            Assert.That(result.EstablishedUnits.Count, Is.EqualTo(2));
            Assert.That(result.EstablishedUnits[0].ParticipantInstanceIds[0], Is.EqualTo("I-A"));
            Assert.That(result.EstablishedUnits[1].ParticipantInstanceIds[0], Is.EqualTo("I-A"));
        }

        [Test]
        public void UnitParticipantsAreReturnedInBoardSlotOrder()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 6, 3 }, new[] { a, b }, new[] { a, b });
            AssertValid(result);
            CollectionAssert.AreEqual(new[] { 3, 6 }, result.EstablishedUnits[0].ParticipantBoardSlotIndexes);
            CollectionAssert.AreEqual(new[] { "B", "A" }, result.EstablishedUnits[0].ParticipantDefinitionIds);
        }

        [Test]
        public void UnitOrderPrioritizesLargerParticipantGroups()
        {
            CharacterDefinition a = Def("A", units: new[] { "Pair", "Trio" });
            CharacterDefinition b = Def("B", units: new[] { "Pair", "Trio" });
            CharacterDefinition c = Def("C", units: new[] { "Trio" });
            World w = MakeWorld(new[] { a, b, c }, new[] { 0, 1, 2 }, new[] { a, b, c }, new[] { a, b, c });
            SynergyDetectionResult result = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            AssertValid(result);
            Assert.That(result.EstablishedUnits[0].UnitName, Is.EqualTo("Trio"));
            Assert.That(result.EstablishedUnits[1].UnitName, Is.EqualTo("Pair"));
        }

        [Test]
        public void UnitOrderBreaksEqualCountsBySlotSequenceBeforeName()
        {
            CharacterDefinition a = Def("A", units: new[] { "Zulu" });
            CharacterDefinition b = Def("B", units: new[] { "Zulu" });
            CharacterDefinition c = Def("C", units: new[] { "Alpha" });
            CharacterDefinition d = Def("D", units: new[] { "Alpha" });
            SynergyDetectionResult result = Detect(new[] { a, b, c, d }, new[] { 0, 1, 3, 4 },
                new[] { a, b, c, d }, new[] { a, b, c, d });
            AssertValid(result);
            Assert.That(result.EstablishedUnits[0].UnitName, Is.EqualTo("Zulu"));
            Assert.That(result.EstablishedUnits[1].UnitName, Is.EqualTo("Alpha"));
        }

        [Test]
        public void UnitOrderUsesOrdinalNameWhenParticipantSlotsAreEqual()
        {
            CharacterDefinition a = Def("A", units: new[] { "Zulu", "Alpha" });
            CharacterDefinition b = Def("B", units: new[] { "Zulu", "Alpha" });
            World w = MakeWorld(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            SynergyDetectionResult result = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            AssertValid(result);
            Assert.That(result.EstablishedUnits[0].UnitName, Is.EqualTo("Alpha"));
            Assert.That(result.EstablishedUnits[1].UnitName, Is.EqualTo("Zulu"));
        }

        [Test]
        public void AttributeSharedByTwoAddsOneBlock()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            AssertAttribute(result, "Mage", 2, 1);
        }

        [Test]
        public void AttributeParticipantsExposeAlignedIdsAndSlots()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 1, 7 }, new[] { a, b }, new[] { a, b });
            AssertAttribute(result, "Mage", 2, 1);
            CollectionAssert.AreEqual(new[] { "A", "B" }, result.EstablishedAttributes[0].ParticipantDefinitionIds);
            CollectionAssert.AreEqual(new[] { "I-A", "I-B" }, result.EstablishedAttributes[0].ParticipantInstanceIds);
            CollectionAssert.AreEqual(new[] { 1, 7 }, result.EstablishedAttributes[0].ParticipantBoardSlotIndexes);
        }

        [Test]
        public void ThreeCharactersSharingAttributeStillAddsOnlyOneBlock()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage" });
            CharacterDefinition c = Def("C", attributes: new[] { "Mage" });
            SynergyDetectionResult result = Detect(new[] { a, b, c }, new[] { 0, 1, 2 }, new[] { a, b, c }, new[] { a, b, c });
            AssertAttribute(result, "Mage", 3, 1);
        }

        [Test]
        public void MultipleSharedAttributesAddBlockByDistinctAttributeType()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage", "Leader" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage", "Leader" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 8 }, new[] { a, b }, new[] { a, b });
            AssertValid(result);
            Assert.That(result.EstablishedAttributes.Count, Is.EqualTo(2));
            Assert.That(result.AttributeBlockBonus, Is.EqualTo(2));
            Assert.That(result.EstablishedUnits, Is.Empty);
        }

        [Test]
        public void AttributesDoNotRequireAdjacency()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 8 }, new[] { a, b }, new[] { a, b });
            AssertAttribute(result, "Mage", 2, 1);
        }

        [Test]
        public void HandCharacterDoesNotCountForAttribute()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage" });
            World w = MakeWorld(new[] { a, b }, new[] { 0 }, new[] { a, b }, new[] { a, b }, handDefinitionIds: new[] { "B" });
            SynergyDetectionResult result = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            AssertValid(result);
            Assert.That(result.EstablishedAttributes, Is.Empty);
            Assert.That(result.AttributeBlockBonus, Is.Zero);
        }

        [Test]
        public void DuplicateAttributeOnOneDefinitionCountsOnce()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage", "Mage" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            AssertAttribute(result, "Mage", 2, 1);
        }

        [Test]
        public void BoardWithoutAttributesHasNoAttributeBonus()
        {
            CharacterDefinition a = Def("A");
            CharacterDefinition b = Def("B");
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            AssertValid(result);
            Assert.That(result.EstablishedAttributes, Is.Empty);
            Assert.That(result.AttributeBlockBonus, Is.Zero);
        }

        [Test]
        public void AttributeCanEstablishWhenUnitIsIncomplete()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage" }, units: new[] { "U" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage" }, units: new[] { "U" });
            CharacterDefinition c = Def("C", units: new[] { "U" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b, c });
            AssertValid(result);
            Assert.That(result.EstablishedUnits, Is.Empty);
            AssertAttribute(result, "Mage", 2, 1);
        }

        [Test]
        public void RosterDuplicateDefinitionIdsAreDeduplicated()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, a, b }, new[] { a, b, b });
            AssertUnit(result, "U", "A", "B");
        }

        [Test]
        public void DuplicateDefinitionIdsWithEquivalentTagSetsAreDeduplicated()
        {
            CharacterDefinition a = Def("A", attributes: new[] { "Mage", "Leader" }, units: new[] { "U", "V" });
            CharacterDefinition reorderedDuplicate = Def("A", attributes: new[] { "Leader", "Mage", "Mage" },
                units: new[] { "V", "U", "U" });
            CharacterDefinition b = Def("B", attributes: new[] { "Mage", "Leader" }, units: new[] { "U", "V" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b },
                new[] { a, reorderedDuplicate, b });
            AssertValid(result);
            Assert.That(result.EstablishedUnits.Count, Is.EqualTo(2));
            Assert.That(result.EstablishedAttributes.Count, Is.EqualTo(2));
        }

        [Test]
        public void BlankTagsAreIgnoredWithoutTrimmingValidTags()
        {
            CharacterDefinition a = Def("A", units: new[] { "", "  ", " U " });
            CharacterDefinition b = Def("B", units: new[] { " U " });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            AssertUnit(result, " U ", "A", "B");
        }

        [Test]
        public void UnitMembershipAboveBoardCapacityCannotEstablish()
        {
            var defs = new List<CharacterDefinition>();
            var positions = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                CharacterDefinition definition = Def("D" + i, units: new[] { "U" });
                defs.Add(definition);
                if (i < 9) positions.Add(i);
            }
            SynergyDetectionResult result = Detect(defs, positions, defs, defs);
            AssertValid(result);
            Assert.That(result.EstablishedUnits, Is.Empty);
        }

        [Test]
        public void OneMemberUnitDoesNotEstablish()
        {
            CharacterDefinition a = Def("A", units: new[] { "Solo" });
            CharacterDefinition b = Def("B");
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            AssertNoUnit(result);
        }

        [Test]
        public void NonParticipantBetweenUnitMembersCannotBridgeConnectivity()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            CharacterDefinition c = Def("C");
            SynergyDetectionResult result = Detect(new[] { a, b, c }, new[] { 0, 2, 1 },
                new[] { a, b, c }, new[] { a, b, c });
            AssertNoUnit(result);
        }

        [Test]
        public void NullRequiredInputReturnsInvalidResult()
        {
            SynergyDetectionResult result = SynergyDetector.Detect(null, HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                Array.Empty<CharacterDefinition>(), Array.Empty<CharacterDefinition>());
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
        }

        [Test]
        public void BoardDefinitionMissingFromRosterCatalogReturnsInvalidResult()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            World w = MakeWorld(new[] { a }, new[] { 0 }, new[] { a }, Array.Empty<CharacterDefinition>());
            SynergyDetectionResult result = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("unknown DefinitionId"));
            Assert.That(result.EstablishedUnits, Is.Empty);
        }

        [Test]
        public void UnownedBoardInstanceReturnsInvalidResult()
        {
            BoardState board = BoardState.CreateEmpty(3, 3);
            board.TryWithCharacterAt(0, "missing", out board);
            SynergyDetectionResult result = SynergyDetector.Detect(board, HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                Array.Empty<CharacterDefinition>(), Array.Empty<CharacterDefinition>());
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("unowned CharacterInstance"));
        }

        [Test]
        public void SelectedDefinitionAbsentFromFullRosterReturnsInvalidResult()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            World w = MakeWorld(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a });
            SynergyDetectionResult result = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("absent from the full roster"));
        }

        [Test]
        public void BoardWithWrongDimensionsIsRejected()
        {
            SynergyDetectionResult result = SynergyDetector.Detect(BoardState.CreateEmpty(4, 4),
                HandState.CreateStandard(), new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                Array.Empty<CharacterDefinition>(), Array.Empty<CharacterDefinition>());
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("standard 3x3"));
        }

        [Test]
        public void IdenticalInputIsDeterministicAndDoesNotChangeStatesOrRng()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" }, attributes: new[] { "Mage" });
            CharacterDefinition b = Def("B", units: new[] { "U" }, attributes: new[] { "Mage" });
            World w = MakeWorld(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            string[] boardBefore = CopyBoard(w.Board);
            string[] ownershipBefore = CopyOwnership(w.Ownership);
            var streams = new SimulationRandomStreams(1234UL);
            Pcg32State rngBefore = streams.Stats.CaptureState();
            SynergyDetectionResult first = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            SynergyDetectionResult second = SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
            AssertEquivalent(first, second);
            CollectionAssert.AreEqual(boardBefore, CopyBoard(w.Board));
            CollectionAssert.AreEqual(ownershipBefore, CopyOwnership(w.Ownership));
            AssertStateEqual(rngBefore, streams.Stats.CaptureState());
        }

        [Test]
        public void ResultCollectionsAreReadOnly()
        {
            CharacterDefinition a = Def("A", units: new[] { "U" });
            CharacterDefinition b = Def("B", units: new[] { "U" });
            SynergyDetectionResult result = Detect(new[] { a, b }, new[] { 0, 1 }, new[] { a, b }, new[] { a, b });
            Assert.Throws<NotSupportedException>(() => ((IList<UnitSynergyResult>)result.EstablishedUnits).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<SynergyParticipant>)result.EstablishedUnits[0].Participants).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<AttributeSynergyResult>)result.EstablishedAttributes).Clear());
        }

        private static CharacterDefinition Def(string id, string[] attributes = null, string[] units = null) =>
            new CharacterDefinition(id, id, 1, attributes, units);

        private static SynergyDetectionResult Detect(IList<CharacterDefinition> definitions,
            IList<int> boardSlots, IList<CharacterDefinition> selected, IList<CharacterDefinition> full,
            IList<string> handDefinitionIds = null)
        {
            World w = MakeWorld(definitions, boardSlots, selected, full, handDefinitionIds);
            return SynergyDetector.Detect(w.Board, w.Hand, w.Ownership, w.Selected, w.Full);
        }

        private static World MakeWorld(IList<CharacterDefinition> definitions, IList<int> boardSlots,
            IList<CharacterDefinition> selected, IList<CharacterDefinition> full,
            IList<string> handDefinitionIds = null)
        {
            var definitionById = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
            foreach (CharacterDefinition definition in definitions)
                definitionById[definition.DefinitionId] = definition;
            var instanceByDefinition = new Dictionary<string, CharacterInstance>(StringComparer.Ordinal);
            foreach (CharacterDefinition definition in definitions)
                if (!instanceByDefinition.ContainsKey(definition.DefinitionId))
                    instanceByDefinition.Add(definition.DefinitionId,
                        new CharacterInstance("I-" + definition.DefinitionId, definition, 1));

            var boardIds = new string[BoardState.StandardColumns * BoardState.StandardRows];
            var handContents = new HandSlotContent[HandState.StandardSlotCount];
            for (int i = 0; i < handContents.Length; i++) handContents[i] = HandSlotContent.Empty();
            var owned = new List<CharacterInstance>();
            var placedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < boardSlots.Count; i++)
            {
                string definitionId = definitions[i].DefinitionId;
                boardIds[boardSlots[i]] = "I-" + definitionId;
                placedIds.Add(definitionId);
            }
            if (handDefinitionIds != null)
            {
                for (int i = 0; i < handDefinitionIds.Count; i++)
                {
                    string definitionId = handDefinitionIds[i];
                    handContents[i] = HandSlotContent.Character("I-" + definitionId);
                    placedIds.Add(definitionId);
                }
            }
            foreach (string definitionId in placedIds) owned.Add(instanceByDefinition[definitionId]);

            return new World
            {
                Board = new BoardState(3, 3, boardIds),
                Hand = new HandState(handContents),
                Ownership = new CharacterOwnershipState(owned),
                Selected = selected,
                Full = full
            };
        }

        private static void AssertValid(SynergyDetectionResult result) => Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors));
        private static void AssertNoUnit(SynergyDetectionResult result)
        {
            AssertValid(result);
            Assert.That(result.EstablishedUnits, Is.Empty);
        }

        private static void AssertUnit(SynergyDetectionResult result, string unit, params string[] definitionIds)
        {
            AssertValid(result);
            Assert.That(result.EstablishedUnits.Count, Is.EqualTo(1));
            UnitSynergyResult found = result.EstablishedUnits[0];
            Assert.That(found.UnitName, Is.EqualTo(unit));
            Assert.That(found.IsEstablished, Is.True);
            Assert.That(found.ParticipantCount, Is.EqualTo(definitionIds.Length));
            CollectionAssert.AreEqual(definitionIds, found.ParticipantDefinitionIds);
            Assert.That(found.ParticipantInstanceIds.Count, Is.EqualTo(definitionIds.Length));
        }

        private static void AssertAttribute(SynergyDetectionResult result, string name, int count, int block)
        {
            AssertValid(result);
            Assert.That(result.EstablishedAttributes.Count, Is.EqualTo(1));
            Assert.That(result.EstablishedAttributes[0].AttributeName, Is.EqualTo(name));
            Assert.That(result.EstablishedAttributes[0].ParticipantCount, Is.EqualTo(count));
            Assert.That(result.EstablishedAttributes[0].BlockBonus, Is.EqualTo(block));
            Assert.That(result.AttributeBlockBonus, Is.EqualTo(block));
        }

        private static string[] CopyBoard(BoardState board)
        {
            var values = new string[board.SlotCount];
            for (int i = 0; i < values.Length; i++) board.TryGetCharacterIdAt(i, out values[i]);
            return values;
        }

        private static string[] CopyOwnership(CharacterOwnershipState ownership)
        {
            var values = new List<string>();
            foreach (CharacterInstanceView instance in ownership.GetOwnedInstances())
                values.Add(instance.InstanceId + ":" + instance.DefinitionId);
            values.Sort(StringComparer.Ordinal);
            return values.ToArray();
        }

        private static void AssertEquivalent(SynergyDetectionResult left, SynergyDetectionResult right)
        {
            Assert.That(left.IsValid, Is.EqualTo(right.IsValid));
            Assert.That(left.AttributeBlockBonus, Is.EqualTo(right.AttributeBlockBonus));
            Assert.That(left.EstablishedUnits.Count, Is.EqualTo(right.EstablishedUnits.Count));
            for (int i = 0; i < left.EstablishedUnits.Count; i++)
            {
                Assert.That(left.EstablishedUnits[i].UnitName, Is.EqualTo(right.EstablishedUnits[i].UnitName));
                CollectionAssert.AreEqual(left.EstablishedUnits[i].ParticipantInstanceIds, right.EstablishedUnits[i].ParticipantInstanceIds);
                CollectionAssert.AreEqual(left.EstablishedUnits[i].ParticipantBoardSlotIndexes, right.EstablishedUnits[i].ParticipantBoardSlotIndexes);
            }
            Assert.That(left.EstablishedAttributes.Count, Is.EqualTo(right.EstablishedAttributes.Count));
            for (int i = 0; i < left.EstablishedAttributes.Count; i++)
            {
                Assert.That(left.EstablishedAttributes[i].AttributeName, Is.EqualTo(right.EstablishedAttributes[i].AttributeName));
                CollectionAssert.AreEqual(left.EstablishedAttributes[i].ParticipantDefinitionIds,
                    right.EstablishedAttributes[i].ParticipantDefinitionIds);
                CollectionAssert.AreEqual(left.EstablishedAttributes[i].ParticipantInstanceIds,
                    right.EstablishedAttributes[i].ParticipantInstanceIds);
                CollectionAssert.AreEqual(left.EstablishedAttributes[i].ParticipantBoardSlotIndexes,
                    right.EstablishedAttributes[i].ParticipantBoardSlotIndexes);
            }
        }

        private static void AssertStateEqual(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        private sealed class World
        {
            public BoardState Board;
            public HandState Hand;
            public CharacterOwnershipState Ownership;
            public IEnumerable<CharacterDefinition> Selected;
            public IEnumerable<CharacterDefinition> Full;
        }
    }
}
