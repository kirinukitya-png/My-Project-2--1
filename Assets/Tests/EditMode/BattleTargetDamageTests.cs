using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BattleTargetDamageTests
    {
        private static Pcg32State Rng(ulong seed = 111) => new Pcg32RandomSource(seed, 17).CaptureState();
        private static StageProgressConfig StageConfig() => new StageProgressConfig(
            25, 5, 5, 4, 0, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
        private static StageProgressState Stage()
        {
            var config = StageConfig();
            var stage = StageProgressRules.CreateInitialState(config);
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            return StageProgressRules.BeginBattle(stage).NextState;
        }
        private static CharacterDefinition Def(string id, int attack = 6, int defense = 2, string attr = "Attr") =>
            new CharacterDefinition(id, id, 1, new[] { attr }, Array.Empty<string>());
        private static CharacterInstance Character(CharacterDefinition definition, string id, int star = 1)
        {
            var instance = new CharacterInstance(id, definition, 1, star);
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1,
                new[] { new CharacterStatOption(6, 2, 1) },
                new[] { new CharacterStatOption(12, 4, 1) },
                new[] { new CharacterStatOption(18, 6, 1) }) });
            Assert.That(instance.EnsureStats(definition, stats, new Pcg32RandomSource(8, 3)), Is.True);
            return instance;
        }
        private static EnemyDefinition EnemyDef(string id = "E", int attack = 7, int defense = 4,
            int hp = 12, string target = "T001", string[] targetIds = null,
            string[] ae = null, string[] pig = null) => new EnemyDefinition(id, id, attack, defense, hp,
                null, ae, pig, target, targetIds ?? new[] { "T001" }, null, null);
        private static EnemyInstance Enemy(EnemyDefinition definition, int slot, int gap = 0, int cooldown = 0)
        {
            Assert.That(EnemyInstanceFactory.TryCreate(definition, 0, slot, 0, 0, 0,
                gap, 0, cooldown, out EnemyInstance instance, out EnemyInstanceFailure failure), Is.True, failure.ToString());
            return instance;
        }
        private static BoardState Board(params (int slot, string id)[] placements)
        {
            BoardState board = BoardState.CreateStandard();
            foreach (var p in placements)
            {
                Assert.That(board.TryWithCharacterAt(p.slot, p.id, out BoardState next), Is.True);
                board = next;
            }
            return board;
        }
        private static EnemyBoardState EnemyBoard(params EnemyInstance[] enemies)
        {
            Assert.That(EnemyBoardState.TryCreate(2, 3, enemies, out EnemyBoardState board,
                out EnemyBoardFailure failure), Is.True, failure.ToString());
            return board;
        }
        private static BattleState Create(CharacterDefinition[] definitions, CharacterInstance[] allies,
            (int slot, string id)[] placements, EnemyDefinition[] enemyDefinitions,
            EnemyInstance[] enemies, ItemEffectState effects = null)
        {
            var result = BattleInitializationRules.Initialize(
                new EconomyState(5, 0, Math.Max(1, placements.Length), 12, 5, 0, 1, 20, 30), Stage(), Board(placements),
                HandState.CreateStandard(), new CharacterOwnershipState(allies), definitions,
                EnemyBoard(enemies), enemyDefinitions, effects ?? new ItemEffectState(),
                definitions, definitions, Rng(), BattleConfig.Default);
            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            return result.State;
        }
        private static BattleState BeginRound(BattleState state)
        {
            var start = BattleTurnRules.RequestNext(state);
            Assert.That(start.Event, Is.EqualTo(BattleScheduleEvent.BattleStartRequired));
            state = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId)).NextState;
            return BattleTurnRules.RequestNext(state).NextState;
        }
        private static BattleScheduleResult RequestAlly(BattleState state)
        {
            state = BeginRound(state);
            return BattleTurnRules.RequestNext(state);
        }
        private static BattleScheduleResult RequestEnemy(BattleState state)
        {
            state = BeginRound(state);
            while (state.NextActorIndex < state.Allies.Count)
            {
                BattleScheduleResult ally = BattleTurnRules.RequestNext(state);
                Assert.That(ally.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
                state = BattleTurnRules.ResolveAction(ally.NextState,
                    BattleActionResolution.ActionCompleted(ally.ActionRequestId)).NextState;
            }
            Assert.That(BattleTurnRules.RequestNext(state).Event, Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
            state = BattleTurnRules.RequestNext(state).NextState;
            BattleScheduleResult start = BattleTurnRules.RequestNext(state);
            Assert.That(start.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
            state = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId)).NextState;
            return BattleTurnRules.RequestNext(state);
        }

        private static BattleTargetDefinition Target(string id, BattleTargetFilter filter,
            BattleTargetSelection selection = BattleTargetSelection.All, int count = 1,
            BattleTargetWeightRule[] weights = null, BattleTargetSelection cellMode = BattleTargetSelection.Uniform,
            int cellCount = 0, BattleTargetWeightRule[] cellWeights = null) =>
            new BattleTargetDefinition(id, filter, selection, count, weights, cellMode, cellCount, cellWeights);

        [TestCase(BattleTargetFilter.BackColumn, 0)]
        [TestCase(BattleTargetFilter.MiddleColumn, 1)]
        [TestCase(BattleTargetFilter.FrontColumn, 2)]
        public void TargetColumnFilters_UseBattleBoardColumns(BattleTargetFilter filter, int column)
        {
            var allies = new List<BattleUnitState>();
            for (int slot = 0; slot < 9; slot++) allies.Add(Ally("A" + slot, slot));
            var result = BattleTargetRules.SelectTargets(Target("T", filter), allies, null, Rng());
            Assert.That(result.Succeeded, Is.True);
            CollectionAssert.AreEqual(new[] { column, column + 3, column + 6 },
                SlotIndexes(result.TargetInstanceIds, allies));
        }

        [Test]
        public void TargetSameEnemyRow_MapsEnemyTwoByThreeRowToAllyThreeByThreeRow()
        {
            var allies = new List<BattleUnitState> { Ally("A0", 0), Ally("A4", 4), Ally("A6", 6), Ally("A8", 8) };
            BattleUnitState enemy = EnemyUnit("E", 4, 7, 0, 10, 1, 0);
            var result = BattleTargetRules.SelectTargets(Target("T", BattleTargetFilter.SameEnemyRow), allies, enemy, Rng());
            CollectionAssert.AreEqual(new[] { "A6", "A8" }, result.TargetInstanceIds);
        }

        [Test]
        public void TargetWeightedFrontAndBackWeightsAreRespectedByRuleDefinition()
        {
            var allies = new List<BattleUnitState> { Ally("Back", 0), Ally("Mid", 1), Ally("Front", 2) };
            var target = Target("T002", BattleTargetFilter.AllBoard, BattleTargetSelection.Weighted,
                1, new[] { new BattleTargetWeightRule(BattleTargetPosition.Front, 6),
                    new BattleTargetWeightRule(BattleTargetPosition.Middle, 2),
                    new BattleTargetWeightRule(BattleTargetPosition.Back, 1) });
            var first = BattleTargetRules.SelectTargets(target, allies, null, Rng(21));
            var second = BattleTargetRules.SelectTargets(target, allies, null, Rng(21));
            CollectionAssert.AreEqual(first.TargetInstanceIds, second.TargetInstanceIds);
            Assert.That(first.NextBattleRandomState, Is.EqualTo(second.NextBattleRandomState));
            Assert.That(first.TargetInstanceIds.Count, Is.EqualTo(1));
        }

        [Test]
        public void TargetCandidateZeroAndAllSelection_DoNotConsumeRandomness()
        {
            var allies = new List<BattleUnitState> { Ally("A0", 0) };
            Pcg32State rng = Rng(27);
            var empty = BattleTargetRules.SelectTargets(Target("none", BattleTargetFilter.FrontColumn), allies, null, rng);
            Assert.That(empty.Succeeded, Is.True);
            Assert.That(empty.TargetInstanceIds, Is.Empty);
            Assert.That(empty.NextBattleRandomState, Is.EqualTo(rng));
            var all = BattleTargetRules.SelectTargets(Target("all", BattleTargetFilter.AllBoard), allies, null, rng);
            Assert.That(all.NextBattleRandomState, Is.EqualTo(rng));
        }

        [Test]
        public void TargetUniformSingleCandidate_StillConsumesOneDrawLikeUnityRangeCall()
        {
            var allies = new List<BattleUnitState> { Ally("A0", 0) };
            Pcg32State before = Rng(32);
            var expectedRng = Pcg32RandomSource.FromState(before);
            expectedRng.NextInt(0, 1);
            var selected = BattleTargetRules.SelectTargets(Target("T001", BattleTargetFilter.AllBoard,
                BattleTargetSelection.Uniform, 1), allies, null, before);
            Assert.That(selected.TargetInstanceIds, Is.EquivalentTo(new[] { "A0" }));
            Assert.That(selected.NextBattleRandomState, Is.EqualTo(expectedRng.CaptureState()));
        }

        [Test]
        public void TargetT009SamplesEmptyCellsAndConsumesTwoCellDraws()
        {
            var allies = new List<BattleUnitState> { Ally("A0", 0) };
            Pcg32State before = Rng(81);
            var expectedRng = Pcg32RandomSource.FromState(before);
            expectedRng.NextInt(0, 9);
            expectedRng.NextInt(0, 8);
            var target = Target("T009", BattleTargetFilter.RandomCells, BattleTargetSelection.All,
                1, null, BattleTargetSelection.Uniform, 2);
            var result = BattleTargetRules.SelectTargets(target, allies, null, before);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextBattleRandomState, Is.EqualTo(expectedRng.CaptureState()));
        }

        [Test]
        public void TargetT010ZeroWeightBackCellsAreNeverSelected()
        {
            var allies = new List<BattleUnitState> { Ally("Back0", 0), Ally("Back1", 3),
                Ally("Back2", 6), Ally("Mid", 1), Ally("Front", 2) };
            var target = Target("T010", BattleTargetFilter.RandomCells, BattleTargetSelection.All,
                1, null, BattleTargetSelection.Weighted, 2, new[] {
                    new BattleTargetWeightRule(BattleTargetPosition.Front, 3),
                    new BattleTargetWeightRule(BattleTargetPosition.Middle, 1),
                    new BattleTargetWeightRule(BattleTargetPosition.Back, 0) });
            for (ulong seed = 1; seed < 20; seed++)
            {
                var result = BattleTargetRules.SelectTargets(target, allies, null, Rng(seed));
                Assert.That(result.Succeeded, Is.True);
                foreach (string id in result.TargetInstanceIds)
                    Assert.That(id.StartsWith("Back", StringComparison.Ordinal), Is.False);
            }
        }

        [Test]
        public void TargetDuplicateDrawsAreRemovedAndFailureKeepsCheckpoint()
        {
            var allies = new List<BattleUnitState> { Ally("A0", 0), Ally("A1", 1) };
            var two = Target("T", BattleTargetFilter.AllBoard, BattleTargetSelection.Uniform, 2);
            var result = BattleTargetRules.SelectTargets(two, allies, null, Rng(99));
            Assert.That(result.TargetInstanceIds.Count, Is.EqualTo(2));
            Assert.That(new HashSet<string>(result.TargetInstanceIds).Count, Is.EqualTo(2));
            Pcg32State invalid = new Pcg32State(999, 0, 2);
            var failed = BattleTargetRules.SelectTargets(two, allies, null, invalid);
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(failed.NextBattleRandomState, Is.EqualTo(invalid));
        }

        [Test]
        public void Damage_IsClampedAtZeroAndRejectsIntegerOverflow()
        {
            Assert.That(BattleDamageRules.Calculate(5, 5).Damage, Is.Zero);
            Assert.That(BattleDamageRules.Calculate(4, 8).Damage, Is.Zero);
            Assert.That(BattleDamageRules.Calculate(8, 3).Damage, Is.EqualTo(5));
            Assert.That(BattleDamageRules.Calculate(int.MaxValue, int.MinValue).Succeeded, Is.False);
        }

        [Test]
        public void AllyAttack_UpdatesEnemyHpAndRngAtomically_AndReportsDefeatPIGGate()
        {
            CharacterDefinition allyDef = Def("C1");
            CharacterInstance ally = Character(allyDef, "I1");
            EnemyDefinition enemyDef = EnemyDef(hp: 1, defense: 0, pig: new[] { "PIG-A" });
            BattleState state = Create(new[] { allyDef }, new[] { ally }, new[] { (0, "I1") },
                new[] { enemyDef }, new[] { Enemy(enemyDef, 0) });
            BattleScheduleResult request = RequestAlly(state);
            Pcg32State before = state.BattleRandomState;
            BattleAttackResult result = BattleNormalAttackRules.ResolveAllyNormalAttack(
                request.NextState, request.ActionRequestId);
            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.CalculatedDamage, Is.EqualTo(6));
            Assert.That(result.ActualEnemyHpLost, Is.EqualTo(1));
            Assert.That(result.DefeatedEnemyInstanceId, Is.EqualTo("enemy:encounter:0:slot:0"));
            Assert.That(result.NextState.Enemies[0].CurrentHP, Is.Zero);
            Assert.That(result.NextState.BattleRandomState, Is.Not.EqualTo(before));
            Assert.That(result.PendingEffectTriggers.Count, Is.EqualTo(1));
            Assert.That(BattleTurnRules.RequestNext(result.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.PostActionEffectsRequired));
        }

        [Test]
        public void AllyAttack_OverkillReportsActualHpLostAndExcludesDefeatedEnemyLater()
        {
            CharacterDefinition allyDef = Def("C1");
            CharacterInstance ally = Character(allyDef, "I1");
            EnemyDefinition first = EnemyDef("E1", hp: 1, defense: 0);
            EnemyDefinition second = EnemyDef("E2", hp: 1, defense: 0);
            BattleState state = Create(new[] { allyDef }, new[] { ally }, new[] { (0, "I1") },
                new[] { first, second }, new[] { Enemy(first, 0), Enemy(second, 1) });
            BattleScheduleResult request = RequestAlly(state);
            BattleAttackResult result = BattleNormalAttackRules.ResolveAllyNormalAttack(request.NextState, request.ActionRequestId);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CalculatedDamage, Is.EqualTo(6));
            Assert.That(result.ActualEnemyHpLost, Is.EqualTo(1));
            string defeated = result.DefeatedEnemyInstanceId;
            Assert.That(defeated, Is.Not.Null);
            Assert.That(result.NextState.Enemies[0].CurrentHP, Is.EqualTo(
                result.NextState.Enemies[0].InstanceId == defeated ? 0 : 1));
            Assert.That(result.NextState.Enemies[1].CurrentHP, Is.EqualTo(
                result.NextState.Enemies[1].InstanceId == defeated ? 0 : 1));
        }

        [Test]
        public void EnemyAttack_SumsTargetDefenseAndConsumesBlockBeforePlayerHp()
        {
            CharacterDefinition a = Def("C1", attr: "A");
            CharacterDefinition b = Def("C2", attr: "A");
            CharacterInstance ca = Character(a, "I1"); CharacterInstance cb = Character(b, "I2");
            EnemyDefinition enemyDef = EnemyDef(attack: 3, target: "T005", targetIds: new[] { "T005" });
            BattleState state = Create(new[] { a, b }, new[] { ca, cb },
                new[] { (0, "I1"), (2, "I2") }, new[] { enemyDef }, new[] { Enemy(enemyDef, 0) });
            Assert.That(state.PlayerBlock, Is.EqualTo(1));
            BattleScheduleResult request = RequestEnemy(state);
            var defs = new Dictionary<string, BattleTargetDefinition> {
                ["T005"] = Target("T005", BattleTargetFilter.FrontColumn) };
            BattleAttackResult result = BattleNormalAttackRules.ResolveEnemyNormalAttack(request.NextState,
                request.ActionRequestId, defs);
            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.TargetInstanceIds, Is.EquivalentTo(new[] { "I2" }));
            Assert.That(result.TotalDefense, Is.EqualTo(2));
            Assert.That(result.CalculatedDamage, Is.EqualTo(1));
            Assert.That(result.BlockAbsorbed, Is.EqualTo(1));
            Assert.That(result.ActualPlayerHpLost, Is.Zero);
            Assert.That(result.NextState.PlayerBlock, Is.Zero);
            Assert.That(result.NextState.PlayerCurrentHP, Is.EqualTo(20));
        }

        [Test]
        public void EnemyAttack_EmptyTargetListDirectlyDamagesPlayerAndQueuesAE()
        {
            EnemyDefinition enemyDef = EnemyDef(attack: 7, target: "T008", targetIds: new[] { "T008" }, ae: new[] { "AE-1" });
            BattleState state = Create(Array.Empty<CharacterDefinition>(), Array.Empty<CharacterInstance>(),
                Array.Empty<(int, string)>(), new[] { enemyDef }, new[] { Enemy(enemyDef, 0) });
            BattleScheduleResult request = RequestEnemy(state);
            var defs = new Dictionary<string, BattleTargetDefinition> {
                ["T008"] = Target("T008", BattleTargetFilter.SameEnemyRow) };
            BattleAttackResult result = BattleNormalAttackRules.ResolveEnemyNormalAttack(request.NextState,
                request.ActionRequestId, defs);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.TargetInstanceIds, Is.Empty);
            Assert.That(result.TotalDefense, Is.Zero);
            Assert.That(result.CalculatedDamage, Is.EqualTo(7));
            Assert.That(result.ActualPlayerHpLost, Is.EqualTo(7));
            Assert.That(result.NextState.PlayerCurrentHP, Is.EqualTo(13));
            Assert.That(result.PendingEffectTriggers[0].EffectIds, Is.EquivalentTo(new[] { "AE-1" }));
        }

        [Test]
        public void EnemyAttack_TargetIdsRotateOnlyAfterSuccess()
        {
            CharacterDefinition c = Def("C1"); CharacterInstance ally = Character(c, "I1");
            EnemyDefinition enemyDef = EnemyDef(target: "T005,T006", targetIds: new[] { "T005", "T006" });
            BattleState state = Create(new[] { c }, new[] { ally }, new[] { (0, "I1") },
                new[] { enemyDef }, new[] { Enemy(enemyDef, 0) });
            BattleScheduleResult request = RequestEnemy(state);
            var defs = new Dictionary<string, BattleTargetDefinition> {
                ["T005"] = Target("T005", BattleTargetFilter.FrontColumn),
                ["T006"] = Target("T006", BattleTargetFilter.MiddleColumn) };
            BattleAttackResult first = BattleNormalAttackRules.ResolveEnemyNormalAttack(request.NextState,
                request.ActionRequestId, defs);
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.SelectedTargetId, Is.EqualTo("T005"));
            Assert.That(first.NextState.Enemies[0].NextTargetIndex, Is.EqualTo(1));
            BattleAttackResult stale = BattleNormalAttackRules.ResolveEnemyNormalAttack(first.NextState,
                request.ActionRequestId, defs);
            Assert.That(stale.Succeeded, Is.False);
            Assert.That(stale.NextState, Is.SameAs(first.NextState));
            Assert.That(stale.NextBattleRandomState, Is.EqualTo(first.NextState.BattleRandomState));

            BattleState nextRound = BattleTurnRules.RequestNext(first.NextState).NextState;
            nextRound = BattleTurnRules.RequestNext(nextRound).NextState;
            nextRound = BattleTurnRules.RequestNext(nextRound).NextState;
            BattleScheduleResult allyAction = BattleTurnRules.RequestNext(nextRound);
            nextRound = BattleTurnRules.ResolveAction(allyAction.NextState,
                BattleActionResolution.ActionCompleted(allyAction.ActionRequestId)).NextState;
            nextRound = BattleTurnRules.RequestNext(nextRound).NextState;
            BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(nextRound);
            nextRound = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            BattleScheduleResult nextEnemyAction = BattleTurnRules.RequestNext(nextRound);
            BattleAttackResult second = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                nextEnemyAction.NextState, nextEnemyAction.ActionRequestId, defs);
            Assert.That(second.Succeeded, Is.True, second.Failure.ToString());
            Assert.That(second.SelectedTargetId, Is.EqualTo("T006"));
            Assert.That(second.NextState.Enemies[0].NextTargetIndex, Is.Zero);
        }

        [Test]
        public void AttackRejectsWrongStaleOrDuplicateRequestWithoutChangingStateOrRng()
        {
            CharacterDefinition c = Def("C1"); CharacterInstance ally = Character(c, "I1");
            EnemyDefinition enemyDef = EnemyDef();
            BattleState state = Create(new[] { c }, new[] { ally }, new[] { (0, "I1") },
                new[] { enemyDef }, new[] { Enemy(enemyDef, 0) });
            BattleScheduleResult request = RequestAlly(state);
            BattleAttackResult wrong = BattleNormalAttackRules.ResolveEnemyNormalAttack(request.NextState,
                request.ActionRequestId, new Dictionary<string, BattleTargetDefinition>());
            Assert.That(wrong.Failure, Is.EqualTo(BattleAttackFailure.WrongActionKind));
            BattleAttackResult stale = BattleNormalAttackRules.ResolveAllyNormalAttack(request.NextState, "old-id");
            Assert.That(stale.Failure, Is.EqualTo(BattleAttackFailure.StaleActionRequest));
            Assert.That(stale.NextState, Is.SameAs(request.NextState));
            BattleAttackResult success = BattleNormalAttackRules.ResolveAllyNormalAttack(request.NextState, request.ActionRequestId);
            Assert.That(success.Succeeded, Is.True);
            BattleAttackResult duplicate = BattleNormalAttackRules.ResolveAllyNormalAttack(success.NextState, request.ActionRequestId);
            Assert.That(duplicate.Succeeded, Is.False);
            Assert.That(duplicate.NextState, Is.SameAs(success.NextState));
        }

        private static int[] SlotIndexes(IReadOnlyList<string> ids, IReadOnlyList<BattleUnitState> allies)
        {
            var result = new int[ids.Count];
            for (int i = 0; i < ids.Count; i++)
                for (int j = 0; j < allies.Count; j++) if (allies[j].InstanceId == ids[i]) result[i] = allies[j].SlotIndex;
            Array.Sort(result); return result;
        }
        private static BattleUnitState Ally(string id, int slot) =>
            new BattleUnitState(id, id, BattleUnitSide.Ally, slot, 1, 1, false, 0, 0,
                0, 0, 1, 0, 1, null, null, null, 0, null, null, null);
        private static BattleUnitState EnemyUnit(string id, int slot, int attack, int defense, int hp, int nextRound, int cooldown) =>
            new BattleUnitState(id, id, BattleUnitSide.Enemy, slot, attack, defense, true,
                hp, hp, 0, cooldown, nextRound, 0, 0, null, null,
                new[] { "T001" }, 0, null, null, null);
    }
}
