using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Tests
{
    internal static class UnitMoveBoardFixture
    {
        internal static UnitBoardMember[] Board(string layout, int starA = 1) => layout
            .Select((id, slot) => (id, slot)).Where(x => x.id != '.')
            .Select(x => new UnitBoardMember(x.slot, x.id.ToString(), x.id == 'A' ? starA : 1)).ToArray();

        internal static CharacterDefinition[] Definitions(string units, IEnumerable<UnitBoardMember> board) =>
            units.Where(char.IsLetter).Select(x => x.ToString()).Concat(board.Select(x => x.DefinitionId))
                .Distinct().Select(id => new CharacterDefinition(id, id, 1, null,
                    units.Split(',').Where(unit => unit.Contains(id)).ToArray())).ToArray();
    }

    public sealed partial class GameObservationTests
    {
        [TestCase(".........", "", 1, 0, 0, false, false)]
        [TestCase("A........", "", 1, 0, 0, false, false)]
        [TestCase("AB.......", "AB", 1, 1, 1, false, false)]
        [TestCase("ABC......", "ABC", 1, 1, 1, false, false)]
        [TestCase("ABC..D...", "AB,CD", 1, 2, 2, false, false)]
        [TestCase("AB.C.....", "AB,AC", 1, 2, 1, false, false)]
        [TestCase("ABCD..E..", "ABC,ADE", 1, 2, 1, false, false)]
        [TestCase("ABCD.....", "ABC,AD", 1, 2, 1, false, false)]
        [TestCase(".B.CAD.E.", "AB,AC,AD,AE", 3, 4, 3, false, false)]
        [TestCase("...BAC.D.", "AB,AC,AD", 1, 3, 1, false, false)]
        [TestCase("...BAC.D.", "AB,AC,AD", 2, 3, 2, false, false)]
        [TestCase("...BAC.D.", "AB,AC,AD", 3, 3, 3, false, false)]
        [TestCase("AB......C", "ABC", 1, 0, 0, false, false)]
        [TestCase("AB.......", "ABC", 1, 0, 0, false, true)]
        [TestCase("AB.......", "ABC", 1, 0, 0, true, false)]
        [TestCase("ABCDE....", "ABCDEF", 1, 0, 0, false, false)]
        [TestCase("ABCDEF...", "ABCDEF", 1, 1, 1, false, false)]
        [TestCase("ABCD.....", "ABCD", 1, 1, 1, false, false)]
        [TestCase("ABCDE....", "ABCDE", 1, 1, 1, false, false)]
        [TestCase("ABCDEFG..", "ABCDEFG", 1, 1, 1, false, false)]
        [TestCase("ABCDEFGH.", "ABCDEFGH", 1, 1, 1, false, false)]
        [TestCase("ABCDEFGHI", "ABCDEFGHI", 1, 1, 1, false, false)]
        [TestCase("ABCDEFGHI", "ABCDEFGHIJ", 1, 0, 0, false, false)]
        [TestCase("A...B....", "AB", 1, 0, 0, false, false)]
        [TestCase("AXB......", "AB", 1, 0, 0, false, false)]
        public void UnitPlannerUsesPublicRequirementsAndMatchesDetector(string layout, string tags, int star,
            int establishedCount, int activatedCount, bool missingRoster, bool memberInHand)
        {
            var board = UnitMoveBoardFixture.Board(layout, star);
            var definitions = UnitMoveBoardFixture.Definitions(tags, board);
            var selected = missingRoster ? definitions.Where(x => x.DefinitionId != "C").ToArray() : definitions;
            var instances = board.Select(x => new CharacterInstance(x.DefinitionId, definitions.Single(
                d => d.DefinitionId == x.DefinitionId), 1, x.Star)).ToList();
            var hand = HandState.CreateStandard();
            if (memberInHand)
            {
                instances.Add(new CharacterInstance("C", definitions.Single(x => x.DefinitionId == "C"), 1));
                Assert.That(hand.TryWithSlot(0, HandSlotContent.Character("C"), out hand), Is.True);
            }
            var config = StageConfig();
            var prep = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(config), config).NextState;
            var ids = new string[9];
            foreach (var member in board) ids[member.SlotIndex] = member.DefinitionId;
            var result = ObservationBuilder.Build(Source(prep,
                economy: new ProjectNST.GameCore.Economy.EconomyState(12, 0, Math.Max(1, board.Length), 12, 12, 0, 1, 25, 30),
                board: new BoardState(3, 3, ids), hand: hand, ownership: new CharacterOwnershipState(instances),
                definitions: definitions, selected: selected, full: definitions,
                shop: new ShopState(ShopKind.Character, Array.Empty<ShopOffer>(), 0, 1, 1),
                enemies: Array.Empty<EnemyDefinition>(), enemyBoard: EnemyBoardState.CreateEmpty()));
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            var observation = result.Observation;
            var established = UnitMoveRoundPlanner.DetectEstablishedUnits(board, observation.UnitRequirements);
            Assert.That(established.Select(u => u.UnitName), Is.EquivalentTo(observation.Synergy.Units.Select(u => u.UnitName)));
            var plan = UnitMoveRoundPlanner.Plan(board, established);
            Assert.That(plan.EstablishedCount, Is.EqualTo(establishedCount));
            Assert.That(plan.ActivationCount, Is.EqualTo(activatedCount));
            Assert.That(plan.CompetitionBlockedCount, Is.EqualTo(establishedCount - activatedCount));
            Assert.That(plan.Details, Is.Null);
        }
    }

    public sealed class UnitMoveRoundPlannerTests
    {
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        public void EstablishedInputKeepsPriorityAndStateIsLocal(int star, int expected)
        {
            var board = UnitMoveBoardFixture.Board("...BAC.DX", star)
                .Select(x => x.DefinitionId == "B" ? new UnitBoardMember(x.SlotIndex, x.DefinitionId, 3) : x).ToArray();
            var units = new[]
            {
                new EstablishedUnitSlots("A-late-slot", new[] { 4, 7 }),
                new EstablishedUnitSlots("z", new[] { 3, 4 }),
                new EstablishedUnitSlots("Z", new[] { 3, 4 })
            };
            var detailed = UnitMoveRoundPlanner.Plan(board, units, includeDetails: true);
            var lightweight = UnitMoveRoundPlanner.Plan(board.Reverse().ToArray(), units);
            Assert.That(lightweight.ActivationCount, Is.EqualTo(expected));
            Assert.That(detailed.ActivationCount, Is.EqualTo(expected));
            Assert.That(detailed.Details.Activations.Select(a => a.Unit.UnitName),
                Is.EqualTo(new[] { "Z", "z", "A-late-slot" }.Take(expected)));
            Assert.That(detailed.Details.Activations.Select(a => a.InitiatorSlot),
                Is.EqualTo(new[] { 3, 3, 4 }.Take(expected)));
            Assert.That(detailed.Details.Members.Single(x => x.Slot == 8).NormalAttack, Is.True);
            Assert.That(detailed.Details.Members.Single(x => x.Slot == 4).NormalAttack, Is.False);
            Assert.That(detailed.Details.UnusedUnits.Count, Is.EqualTo(3 - expected));
            Assert.That(units.Select(x => x.UnitName), Is.EqualTo(new[] { "A-late-slot", "z", "Z" }));
            Assert.That(board.Select(x => x.SlotIndex), Is.EqualTo(new[] { 3, 4, 5, 7, 8 }));
        }

        [Test]
        public void StarAndRemovalChangeOnlyTheApplicablePlan()
        {
            var pair = new EstablishedUnitSlots("AB", new[] { 0, 1 });
            var board = UnitMoveBoardFixture.Board("AB.......", 1);
            var upgraded = UnitMoveBoardFixture.Board("AB.......", 3);
            Assert.That(UnitMoveRoundPlanner.Plan(board, new[] { pair }).ActivationCount, Is.EqualTo(1));
            Assert.That(UnitMoveRoundPlanner.Plan(upgraded, new[] { pair }).ActivationCount, Is.EqualTo(1));
            Assert.That(UnitMoveRoundPlanner.Plan(upgraded.Skip(1).ToArray(), Array.Empty<EstablishedUnitSlots>())
                .ActivationCount, Is.Zero);
        }

        [Test]
        public void RejectsInvalidMembersAndEstablishedUnits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UnitBoardMember(9, "A", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UnitBoardMember(0, "A", 0));
            Assert.Throws<ArgumentException>(() => new EstablishedUnitSlots("AB", new[] { 1, 0 }));
            Assert.Throws<ArgumentException>(() => UnitMoveRoundPlanner.Plan(new UnitBoardMember[1], Array.Empty<EstablishedUnitSlots>()));
            var board = UnitMoveBoardFixture.Board("AB.......");
            var pair = new EstablishedUnitSlots("AB", new[] { 0, 1 });
            Assert.Throws<ArgumentException>(() => UnitMoveRoundPlanner.Plan(board.Concat(board).ToArray(), new[] { pair }));
            Assert.Throws<ArgumentException>(() => UnitMoveRoundPlanner.Plan(board, new[] { pair, pair }));
            Assert.Throws<ArgumentException>(() => UnitMoveRoundPlanner.Plan(board.Take(1).ToArray(), new[] { pair }));
        }
    }

    public sealed partial class BattleUnitMoveRulesTests
    {
        [TestCase("ABC..D...", "AB,CD", 1, "AB,CD")]
        [TestCase("AB.C.....", "AB,AC", 1, "AB")]
        [TestCase("...BAC.D.", "AB,AC,AD", 1, "AB")]
        [TestCase("...BAC.D.", "AB,AC,AD", 2, "AB,AC")]
        [TestCase("...BAC.D.", "AB,AC,AD", 3, "AB,AC,AD")]
        [TestCase("AB.C.....", "AB,AC", 2, "AB,AC")]
        [TestCase("ABC..D...", "AB,BC,CD", 1, "AB,CD")]
        [TestCase("BCDA.....", "AB,BC,CD", 1, "BC")]
        [TestCase("ABCD.....", "ABC,AD", 1, "ABC")]
        [TestCase("DAB..C...", "ABC,AD", 1, "AD")]
        public void StaticUnitPlannerMatchesOneDynamicAllyRound(string layout, string tags, int star, string sequence)
        {
            var board = UnitMoveBoardFixture.Board(layout, star);
            var definitions = UnitMoveBoardFixture.Definitions(tags, board);
            var battle = Create(definitions, board.Select(x => Character(
                definitions.Single(d => d.DefinitionId == x.DefinitionId), x.DefinitionId, x.Star)).ToArray(),
                board.Select(x => (x.SlotIndex, x.DefinitionId)).ToArray(), EnemyDef(defense: 0, hp: 1000));
            var rng = battle.BattleRandomState;
            var moves = battle.UnitMoves.Reverse().Select(u => new EstablishedUnitSlots(u.UnitName, u.ParticipantSlotIndexes)).ToArray();
            var plan = UnitMoveRoundPlanner.Plan(board, moves, true);
            Assert.That(battle.BattleRandomState, Is.EqualTo(rng));
            Assert.That(plan.Details.Activations.Select(x => x.Unit.UnitName), Is.EqualTo(sequence.Split(',')));
            Assert.That(plan.EstablishedCount, Is.EqualTo(tags.Split(',').Length));
            var slots = board.ToDictionary(x => x.DefinitionId, x => x.SlotIndex);
            var normal = new List<int>();
            var skipped = new List<int>();
            var executed = new List<(string, int, string)>();
            while (true)
            {
                var request = BattleTurnRules.RequestNext(battle);
                Assert.That(request.Succeeded, Is.True, request.Failure.ToString());
                if (request.Event == BattleScheduleEvent.AllyTurnCompleted) break;
                var prior = battle;
                battle = request.NextState;
                if (request.Event == BattleScheduleEvent.BattleStartRequired)
                    battle = BattleTurnRules.ResolveAction(battle, BattleActionResolution.BoundaryCompleted(request.ActionRequestId)).NextState;
                else if (request.Event == BattleScheduleEvent.AllySkippedByParticipationLimit)
                    skipped.Add(prior.Allies[prior.NextActorIndex].SlotIndex);
                else if (request.RequiresActionResolution)
                {
                    int slot = slots[request.ActorInstanceId];
                    var decision = BattleUnitMoveRules.DecideCurrentAllyAction(battle, request.ActionRequestId);
                    Assert.That(decision.Succeeded, Is.True);
                    if (decision.Kind == BattleUnitMoveDecisionKind.SkipNormalAttackAfterMove)
                    {
                        skipped.Add(slot);
                        battle = decision.NextState;
                        continue;
                    }
                    bool useMove = decision.Kind == BattleUnitMoveDecisionKind.MoveAvailable;
                    var attack = useMove
                        ? BattleUnitMoveRules.ResolveUnitMoveAttack(battle, request.ActionRequestId, decision.SelectedMove.MoveId)
                        : BattleNormalAttackRules.ResolveAllyNormalAttack(battle, request.ActionRequestId);
                    Assert.That(attack.Succeeded, Is.True, attack.Failure.ToString());
                    if (useMove)
                        executed.Add((attack.UnitMoveId, slot, string.Join(",", attack.ParticipantInstanceIds.Select(id => slots[id]))));
                    else normal.Add(slot);
                    battle = attack.NextState;
                }
            }
            Assert.That(executed, Is.EqualTo(plan.Details.Activations.Select(a =>
                (a.Unit.UnitName, a.InitiatorSlot, string.Join(",", a.Unit.ParticipantSlots)))));
            Assert.That(normal, Is.EqualTo(plan.Details.Members.Where(x => x.NormalAttack).Select(x => x.Slot)));
            Assert.That(skipped, Is.EqualTo(plan.Details.Members.Where(x => !x.NormalAttack).Select(x => x.Slot)));
            foreach (var member in plan.Details.Members)
            {
                var ally = battle.Allies.Single(x => x.SlotIndex == member.Slot);
                Assert.That(BattleTurnRules.GetParticipationCount(battle, ally.InstanceId), Is.EqualTo(member.Count));
                Assert.That(BattleTurnRules.GetParticipationLimit(ally), Is.EqualTo(member.Limit));
            }
        }
    }
}
