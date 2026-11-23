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
    public sealed class BattleUnitMoveRulesTests
    {
        private static Pcg32State Rng(ulong seed = 111) => new Pcg32RandomSource(seed, 17).CaptureState();
        private static StageProgressConfig StageConfig() => new StageProgressConfig(25, 5, 5, 4, 0, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
        private static StageProgressState Stage()
        {
            var config = StageConfig();
            return StageProgressRules.BeginBattle(StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState).NextState;
        }
        private static CharacterDefinition Def(string id, string[] units, string attribute = "A") =>
            new CharacterDefinition(id, id, 1, new[] { attribute }, units);
        private static CharacterInstance Character(CharacterDefinition definition, string id, int star)
        {
            var instance = new CharacterInstance(id, definition, 1, star);
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1,
                new[] { new CharacterStatOption(3, 1, 1) },
                new[] { new CharacterStatOption(6, 2, 1) },
                new[] { new CharacterStatOption(9, 3, 1) }) });
            Assert.That(instance.EnsureStats(definition, stats, new Pcg32RandomSource(8, 3)), Is.True);
            return instance;
        }
        private static EnemyDefinition EnemyDef(string id = "E", int defense = 10, int hp = 20, string[] pig = null) =>
            new EnemyDefinition(id, id, 1, defense, hp, null, null, pig, "T001", new[] { "T001" }, null, null);
        private static EnemyInstance Enemy(EnemyDefinition definition) =>
            EnemyInstanceFactory.TryCreate(definition, 0, 0, 0, 0, 0, 0, 0, 0,
                out EnemyInstance instance, out EnemyInstanceFailure failure)
                ? instance : throw new InvalidOperationException(failure.ToString());

        private static BattleState Create(CharacterDefinition[] definitions, CharacterInstance[] chars,
            (int slot, string id)[] placements, EnemyDefinition enemyDefinition, int enemyDefense = 10,
            int enemyHp = 20, int enemyGap = 0, ulong seed = 111)
        {
            BoardState board = BoardState.CreateStandard();
            foreach (var placement in placements)
            {
                Assert.That(board.TryWithCharacterAt(placement.slot, placement.id, out BoardState next), Is.True);
                board = next;
            }
            Assert.That(EnemyInstanceFactory.TryCreate(enemyDefinition, 0, 0, 0, 0, 0,
                enemyGap, 0, 0, out EnemyInstance enemy, out EnemyInstanceFailure failure), Is.True, failure.ToString());
            // The selected roster and full roster are the same immutable definitions for these focused scenarios.
            Assert.That(EnemyBoardState.TryCreate(2, 3, new[] { enemy }, out EnemyBoardState enemyBoard,
                out EnemyBoardFailure boardFailure), Is.True, boardFailure.ToString());
            var init = BattleInitializationRules.Initialize(
                new EconomyState(5, 0, Math.Max(1, placements.Length), 12, 5, 0, 1, 20, 30), Stage(), board,
                HandState.CreateStandard(), new CharacterOwnershipState(chars), definitions,
                enemyBoard, new[] { enemyDefinition }, new ItemEffectState(), definitions, definitions,
                Rng(seed), BattleConfig.Default);
            Assert.That(init.Succeeded, Is.True, init.Failure.ToString());
            return init.State;
        }

        private static BattleState RequestFirstAlly(BattleState state)
        {
            var start = BattleTurnRules.RequestNext(state);
            Assert.That(start.Event, Is.EqualTo(BattleScheduleEvent.BattleStartRequired));
            state = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId)).NextState;
            state = BattleTurnRules.RequestNext(state).NextState; // enter PlayerTurn
            return BattleTurnRules.RequestNext(state).NextState;
        }

        private static BattleScheduleResult RequestNextAction(BattleState state)
        {
            BattleScheduleResult result = BattleTurnRules.RequestNext(state);
            while (!result.RequiresActionResolution && result.Succeeded &&
                result.Event != BattleScheduleEvent.AllyTurnCompleted)
            {
                state = result.NextState;
                result = BattleTurnRules.RequestNext(state);
            }
            return result;
        }

        private static BattleState ReplaceAlly(BattleState state, BattleUnitState replacement)
        { Assert.That(state.TryWithAllyState(replacement, out BattleState next), Is.True); return next; }
        private static BattleUnitState CopyAlly(BattleUnitState source, int stun = 0) =>
            new BattleUnitState(source.InstanceId, source.DefinitionId, source.Side, source.SlotIndex,
                source.Attack, source.Defense, source.HasHealth, source.MaxHP, source.CurrentHP,
                source.Gap, source.Cooldown, source.NextActionRound, stun, source.Star,
                source.UnitTags, source.AttributeTags, source.TargetIds, source.NextTargetIndex,
                source.CipEffectIds, source.AeEffectIds, source.PigEffectIds,
                source.TemporaryAttackBonus, source.TemporaryDefenseBonus, source.TemporaryDefenseReduction);

        [Test]
        public void UnitMoveDetectionUsesEstablishedSynergyAndBoardConnectivityOnly()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" });
            CharacterDefinition b = Def("B", new[] { "Pair" });
            var state = Create(new[] { a, b }, new[] { Character(a, "IA", 1), Character(b, "IB", 1) },
                new[] { (0, "IA"), (4, "IB") }, EnemyDef());
            Assert.That(state.UnitMoves, Is.Empty, "Diagonal neighbors do not connect.");

            state = Create(new[] { a, b }, new[] { Character(a, "IA", 1), Character(b, "IB", 1) },
                new[] { (0, "IA"), (1, "IB") }, EnemyDef());
            Assert.That(state.UnitMoves.Count, Is.EqualTo(1));
            Assert.That(state.UnitMoves[0].ParticipantInstanceIds, Is.EqualTo(new[] { "IA", "IB" }));
        }

        [Test]
        public void UnitMoveSelectionKeepsDetectorPriorityAndAllowsOverlappingMovesInOrder()
        {
            CharacterDefinition a = Def("A", new[] { "Alpha", "Beta" });
            CharacterDefinition b = Def("B", new[] { "Alpha" });
            CharacterDefinition c = Def("C", new[] { "Beta" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b, c },
                new[] { Character(a, "IA", 2), Character(b, "IB", 2), Character(c, "IC", 2) },
                new[] { (0, "IA"), (1, "IB"), (3, "IC") }, EnemyDef()));
            var decision = BattleUnitMoveRules.DecideCurrentAllyAction(state, state.PendingActionRequestId);
            Assert.That(decision.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.MoveAvailable));
            Assert.That(decision.SelectedMove.UnitName, Is.EqualTo("Alpha"));
            CollectionAssert.AreEqual(new[] { "IA", "IB" }, decision.SelectedMove.ParticipantInstanceIds);
            var normalBypass = BattleNormalAttackRules.ResolveAllyNormalAttack(state, state.PendingActionRequestId);
            Assert.That(normalBypass.Failure, Is.EqualTo(BattleAttackFailure.UnitMoveAvailable));
            Assert.That(normalBypass.NextState, Is.SameAs(state));
        }

        [Test]
        public void LowerPriorityMoveCannotBypassTheEstablishedOrder()
        {
            CharacterDefinition a = Def("A", new[] { "Alpha", "Beta" });
            CharacterDefinition b = Def("B", new[] { "Alpha" });
            CharacterDefinition c = Def("C", new[] { "Beta" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b, c },
                new[] { Character(a, "IA", 2), Character(b, "IB", 2), Character(c, "IC", 2) },
                new[] { (0, "IA"), (1, "IB"), (3, "IC") }, EnemyDef(defense: 0)));
            string lowerPriorityMove = state.UnitMoves[1].MoveId;
            BattleState before = state;
            Pcg32State beforeRng = state.BattleRandomState;
            var rejected = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId, lowerPriorityMove);
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Failure, Is.EqualTo(BattleAttackFailure.UnitMoveUnavailable));
            Assert.That(rejected.NextState, Is.SameAs(before));
            Assert.That(rejected.NextBattleRandomState, Is.EqualTo(beforeRng));
        }

        [Test]
        public void UnitMoveAttackCombinesAttackAndTracksZeroDamageDefenseReduction()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 1), Character(b, "IB", 1) },
                new[] { (0, "IA"), (1, "IB") }, EnemyDef(defense: 12)));
            var requestId = state.PendingActionRequestId;
            var move = BattleUnitMoveRules.DecideCurrentAllyAction(state, requestId).SelectedMove;
            Pcg32State rng = state.BattleRandomState;
            BattleAttackResult result = BattleUnitMoveRules.ResolveUnitMoveAttack(state, requestId, move.MoveId);
            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.Kind, Is.EqualTo(BattleAttackKind.AllyUnitMove));
            Assert.That(result.Attack, Is.EqualTo(6));
            Assert.That(result.CalculatedDamage, Is.Zero);
            Assert.That(result.ActualEnemyHpLost, Is.Zero);
            Assert.That(result.NextState.Enemies[0].Defense, Is.EqualTo(10));
            Assert.That(result.NextState.Enemies[0].TemporaryDefenseReduction, Is.EqualTo(2));
            Assert.That(result.NextState.UsedUnitMoveIds, Is.EquivalentTo(new[] { move.MoveId }));
            Assert.That(result.NextState.ParticipationCounts["IA"], Is.EqualTo(1));
            Assert.That(result.NextState.ParticipationCounts["IB"], Is.EqualTo(1));
            Assert.That(result.NextBattleRandomState, Is.Not.EqualTo(rng));
        }

        [Test]
        public void UnitMoveDamageDoesNotReduceDefenseAndReportsOverkillAndPigWait()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 1), Character(b, "IB", 1) }, new[] { (0, "IA"), (1, "IB") },
                EnemyDef(defense: 0, hp: 1, pig: new[] { "PIG" })));
            var result = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId,
                state.UnitMoves[0].MoveId);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CalculatedDamage, Is.EqualTo(6));
            Assert.That(result.ActualEnemyHpLost, Is.EqualTo(1));
            Assert.That(result.NextState.Enemies[0].CurrentHP, Is.Zero);
            Assert.That(result.NextState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
            Assert.That(result.PendingEffectTriggers.Count, Is.EqualTo(1));
            Assert.That(BattleTurnRules.RequestNext(result.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.PostActionEffectsRequired));
        }

        [Test]
        public void UnitMoveIsRoundLimitedAndParticipatingActorSkipsNormalAttack()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 2), Character(b, "IB", 2) }, new[] { (0, "IA"), (1, "IB") }, EnemyDef()));
            var move = state.UnitMoves[0];
            var used = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId, move.MoveId);
            Assert.That(used.Succeeded, Is.True);
            var repeatRequest = RequestNextAction(used.NextState);
            Assert.That(repeatRequest.RequiresActionResolution, Is.True);
            var decision = BattleUnitMoveRules.DecideCurrentAllyAction(repeatRequest.NextState,
                repeatRequest.ActionRequestId);
            Assert.That(decision.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.SkipNormalAttackAfterMove));
            var normal = BattleNormalAttackRules.ResolveAllyNormalAttack(repeatRequest.NextState,
                repeatRequest.ActionRequestId);
            Assert.That(normal.Failure, Is.EqualTo(BattleAttackFailure.NormalAttackSkippedAfterUnitMove));
            Assert.That(normal.NextState, Is.SameAs(repeatRequest.NextState));
        }

        [Test]
        public void UnitMoveUnavailableFallsBackToNormalAttackWithoutConsumingRequest()
        {
            CharacterDefinition a = Def("A", Array.Empty<string>());
            BattleState state = RequestFirstAlly(Create(new[] { a }, new[] { Character(a, "IA", 1) },
                new[] { (0, "IA") }, EnemyDef(defense: 0)));
            var decision = BattleUnitMoveRules.DecideCurrentAllyAction(state, state.PendingActionRequestId);
            Assert.That(decision.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.NormalAttackRequired));
            Assert.That(decision.NextState, Is.SameAs(state));
            var attack = BattleNormalAttackRules.ResolveAllyNormalAttack(state, state.PendingActionRequestId);
            Assert.That(attack.Succeeded, Is.True, attack.Failure.ToString());
        }

        [Test]
        public void UnitMoveAttackConsumesExactlyOneBattleDrawAndDoesNotMutateInput()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 1), Character(b, "IB", 1) },
                new[] { (0, "IA"), (1, "IB") }, EnemyDef(defense: 0), seed: 715));
            Pcg32State before = state.BattleRandomState;
            var expected = Pcg32RandomSource.FromState(before);
            expected.NextInt(0, 1); // current setup has one living enemy
            BattleAttackResult result = BattleUnitMoveRules.ResolveUnitMoveAttack(state,
                state.PendingActionRequestId, state.UnitMoves[0].MoveId);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextBattleRandomState, Is.EqualTo(expected.CaptureState()));
            Assert.That(state.Enemies[0].CurrentHP, Is.EqualTo(20));
            Assert.That(state.Enemies[0].Defense, Is.Zero);
            Assert.That(state.ParticipationCounts, Is.Empty);
        }

        [Test]
        public void ZeroDefenseCannotBeReducedBelowZero()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState state = Create(new[] { a, b }, new[] { Character(a, "IA", 1), Character(b, "IB", 1) },
                new[] { (0, "IA"), (1, "IB") }, EnemyDef(defense: 0));
            var ally0 = CopyAlly(state.Allies[0]);
            var ally1 = CopyAlly(state.Allies[1]);
            ally0 = new BattleUnitState(ally0.InstanceId, ally0.DefinitionId, ally0.Side, ally0.SlotIndex,
                0, ally0.Defense, ally0.HasHealth, ally0.MaxHP, ally0.CurrentHP, ally0.Gap, ally0.Cooldown,
                ally0.NextActionRound, ally0.StunRounds, ally0.Star, ally0.UnitTags, ally0.AttributeTags,
                ally0.TargetIds, ally0.NextTargetIndex, ally0.CipEffectIds, ally0.AeEffectIds, ally0.PigEffectIds);
            ally1 = new BattleUnitState(ally1.InstanceId, ally1.DefinitionId, ally1.Side, ally1.SlotIndex,
                0, ally1.Defense, ally1.HasHealth, ally1.MaxHP, ally1.CurrentHP, ally1.Gap, ally1.Cooldown,
                ally1.NextActionRound, ally1.StunRounds, ally1.Star, ally1.UnitTags, ally1.AttributeTags,
                ally1.TargetIds, ally1.NextTargetIndex, ally1.CipEffectIds, ally1.AeEffectIds, ally1.PigEffectIds);
            state = ReplaceAlly(ReplaceAlly(state, ally0), ally1);
            state = RequestFirstAlly(state);
            var result = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId, state.UnitMoves[0].MoveId);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CalculatedDamage, Is.Zero);
            Assert.That(result.NextState.Enemies[0].Defense, Is.Zero);
            Assert.That(result.NextState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
        }

        [Test]
        public void StunnedActorSkipsButOtherMemberCanUseMoveContainingStunnedParticipant()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState state = Create(new[] { a, b }, new[] { Character(a, "IA", 1), Character(b, "IB", 1) },
                new[] { (0, "IA"), (1, "IB") }, EnemyDef());
            state = ReplaceAlly(state, CopyAlly(state.Allies[0], stun: 2));
            state = RequestFirstAlly(state); // processes IA's stun skip; one stun round remains
            Assert.That(state.Allies[0].StunRounds, Is.EqualTo(1));
            state = BattleTurnRules.RequestNext(state).NextState; // request IB, which can still initiate the shared move
            var decision = BattleUnitMoveRules.DecideCurrentAllyAction(state, state.PendingActionRequestId);
            Assert.That(decision.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.MoveAvailable));
            Assert.That(decision.SelectedMove.ParticipantInstanceIds, Is.EquivalentTo(new[] { "IA", "IB" }));
        }

        [Test]
        public void StarParticipationCapsAndRoundStartResetAreHonored()
        {
            CharacterDefinition a = Def("A", new[] { "X", "Y" }); CharacterDefinition b = Def("B", new[] { "X" });
            CharacterDefinition c = Def("C", new[] { "Y" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b, c },
                new[] { Character(a, "IA", 2), Character(b, "IB", 1), Character(c, "IC", 3) },
                new[] { (0, "IA"), (1, "IB"), (3, "IC") }, EnemyDef(defense: 0)));
            Assert.That(BattleTurnRules.GetParticipationLimit(state.Allies[0]), Is.EqualTo(2));
            Assert.That(BattleTurnRules.GetParticipationLimit(state.Allies[1]), Is.EqualTo(1));
            Assert.That(BattleTurnRules.GetParticipationLimit(state.Allies[2]), Is.EqualTo(3));
            BattleState afterFirst = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId,
                state.UnitMoves[0].MoveId).NextState;
            var next = RequestNextAction(afterFirst);
            var nextMove = BattleUnitMoveRules.DecideCurrentAllyAction(next.NextState, next.ActionRequestId);
            Assert.That(nextMove.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.MoveAvailable));
            Assert.That(nextMove.SelectedMove.MoveId, Is.EqualTo(state.UnitMoves[1].MoveId));
            BattleState afterSecond = BattleUnitMoveRules.ResolveUnitMoveAttack(next.NextState,
                next.ActionRequestId, nextMove.SelectedMove.MoveId).NextState;
            Assert.That(afterSecond.ParticipationCounts["IA"], Is.EqualTo(2));
            Assert.That(afterSecond.UsedUnitMoveIds.Count, Is.EqualTo(2));
        }

        [Test]
        public void RoundStartClearsMoveUsageAndParticipationCounts()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 1), Character(b, "IB", 1) }, new[] { (0, "IA"), (1, "IB") }, EnemyDef(defense: 0)));
            BattleState usedMove = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId,
                state.UnitMoves[0].MoveId).NextState;
            BattleState afterPlayer = usedMove;
            while (afterPlayer.NextActorIndex < afterPlayer.Allies.Count)
                afterPlayer = BattleTurnRules.RequestNext(afterPlayer).NextState;
            var allyEnd = BattleTurnRules.RequestNext(afterPlayer);
            Assert.That(allyEnd.Event, Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
            var enemyStart = BattleTurnRules.RequestNext(allyEnd.NextState);
            afterPlayer = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            var enemyRequest = BattleTurnRules.RequestNext(afterPlayer);
            afterPlayer = BattleTurnRules.ResolveAction(enemyRequest.NextState,
                BattleActionResolution.ActionCompleted(enemyRequest.ActionRequestId)).NextState;
            var enemyDone = BattleTurnRules.RequestNext(afterPlayer);
            var roundEnd = BattleTurnRules.RequestNext(enemyDone.NextState);
            Assert.That(roundEnd.Event, Is.EqualTo(BattleScheduleEvent.RoundCompleted));
            var roundStart = BattleTurnRules.RequestNext(roundEnd.NextState);
            Assert.That(roundStart.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
            Assert.That(roundStart.NextState.UsedUnitMoveIds, Is.Empty);
            Assert.That(roundStart.NextState.ParticipationCounts, Is.Empty);
        }

        [Test]
        public void ZeroDamageDefenseReductionStacksAndRestoresOnceBeforeEnemyTurn()
        {
            CharacterDefinition a = Def("A", new[] { "X", "Y" }); CharacterDefinition b = Def("B", new[] { "X" });
            CharacterDefinition c = Def("C", new[] { "Y" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b, c },
                new[] { Character(a, "IA", 2), Character(b, "IB", 1), Character(c, "IC", 1) },
                new[] { (0, "IA"), (1, "IB"), (3, "IC") }, EnemyDef(defense: 12)));
            var first = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId, state.UnitMoves[0].MoveId);
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.NextState.Enemies[0].Defense, Is.EqualTo(10));
            var secondRequest = RequestNextAction(first.NextState);
            var decision = BattleUnitMoveRules.DecideCurrentAllyAction(secondRequest.NextState, secondRequest.ActionRequestId);
            Assert.That(decision.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.MoveAvailable));
            var second = BattleUnitMoveRules.ResolveUnitMoveAttack(secondRequest.NextState,
                secondRequest.ActionRequestId, decision.SelectedMove.MoveId);
            Assert.That(second.Succeeded, Is.True);
            Assert.That(second.NextState.Enemies[0].Defense, Is.EqualTo(8));
            Assert.That(second.NextState.Enemies[0].TemporaryDefenseReduction, Is.EqualTo(4));

            BattleState s = second.NextState;
            while (s.NextActorIndex < s.Allies.Count)
            {
                var req = BattleTurnRules.RequestNext(s);
                if (req.Event == BattleScheduleEvent.AllyActionRequired)
                    s = BattleTurnRules.ResolveAction(req.NextState, BattleActionResolution.ActionCompleted(req.ActionRequestId)).NextState;
                else s = req.NextState;
            }
            var allyDone = BattleTurnRules.RequestNext(s);
            Assert.That(allyDone.Event, Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
            var enemyStart = BattleTurnRules.RequestNext(allyDone.NextState);
            Assert.That(enemyStart.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
            BattleState beforeRestore = enemyStart.NextState;
            var restored = BattleTurnRules.ResolveAction(beforeRestore,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId));
            Assert.That(restored.Succeeded, Is.True);
            Assert.That(restored.NextState.Enemies[0].Defense, Is.EqualTo(12));
            Assert.That(restored.NextState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
            var stale = BattleTurnRules.ResolveAction(restored.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId));
            Assert.That(stale.Succeeded, Is.False);
            Assert.That(stale.NextState, Is.SameAs(restored.NextState));
            var enemyAttack = BattleTurnRules.RequestNext(restored.NextState);
            Assert.That(enemyAttack.Event, Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
            BattleState afterEnemy = BattleTurnRules.ResolveAction(enemyAttack.NextState,
                BattleActionResolution.ActionCompleted(enemyAttack.ActionRequestId)).NextState;
            var enemyDone = BattleTurnRules.RequestNext(afterEnemy);
            var roundCompleted = BattleTurnRules.RequestNext(enemyDone.NextState);
            Assert.That(roundCompleted.Event, Is.EqualTo(BattleScheduleEvent.RoundCompleted));
            Assert.That(roundCompleted.NextState.Enemies[0].Defense, Is.EqualTo(11),
                "The permanent end-of-round Defense -1 follows temporary reduction restoration.");
            Assert.That(roundCompleted.NextState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
        }

        [Test]
        public void AttributeBlockIsInitializedOnceAndUnitMoveDoesNotReapplyIt()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }, "Shared");
            CharacterDefinition b = Def("B", new[] { "Pair" }, "Shared");
            BattleState state = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 1), Character(b, "IB", 1) }, new[] { (0, "IA"), (1, "IB") }, EnemyDef(defense: 0)));
            Assert.That(state.AttributeBlock, Is.EqualTo(1));
            Assert.That(state.PlayerBlock, Is.EqualTo(1));
            BattleState moved = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId,
                state.UnitMoves[0].MoveId).NextState;
            Assert.That(moved.AttributeBlock, Is.EqualTo(1));
            Assert.That(moved.PlayerBlock, Is.EqualTo(1));
        }

        [Test]
        public void InvalidRequestOrUnavailableMoveLeavesStateAndBattleRngUnchanged()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState state = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 1), Character(b, "IB", 1) }, new[] { (0, "IA"), (1, "IB") }, EnemyDef()));
            var stale = BattleUnitMoveRules.ResolveUnitMoveAttack(state, "wrong", state.UnitMoves[0].MoveId);
            Assert.That(stale.Succeeded, Is.False);
            Assert.That(stale.NextState, Is.SameAs(state));
            Assert.That(stale.NextBattleRandomState, Is.EqualTo(state.BattleRandomState));
            var wrongMove = BattleUnitMoveRules.ResolveUnitMoveAttack(state, state.PendingActionRequestId, "missing");
            Assert.That(wrongMove.Succeeded, Is.False);
            Assert.That(wrongMove.NextState, Is.SameAs(state));
            Assert.That(wrongMove.NextBattleRandomState, Is.EqualTo(state.BattleRandomState));
        }

        [Test]
        public void SameSeedAndStateReproduceMoveTargetDamageAndCheckpoint()
        {
            CharacterDefinition a = Def("A", new[] { "Pair" }); CharacterDefinition b = Def("B", new[] { "Pair" });
            BattleState first = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 1), Character(b, "IB", 1) }, new[] { (0, "IA"), (1, "IB") }, EnemyDef(), seed: 222));
            BattleState second = RequestFirstAlly(Create(new[] { a, b },
                new[] { Character(a, "IA", 1), Character(b, "IB", 1) }, new[] { (0, "IA"), (1, "IB") }, EnemyDef(), seed: 222));
            var r1 = BattleUnitMoveRules.ResolveUnitMoveAttack(first, first.PendingActionRequestId, first.UnitMoves[0].MoveId);
            var r2 = BattleUnitMoveRules.ResolveUnitMoveAttack(second, second.PendingActionRequestId, second.UnitMoves[0].MoveId);
            Assert.That(r1.SelectedTargetId, Is.EqualTo(r2.SelectedTargetId));
            Assert.That(r1.CalculatedDamage, Is.EqualTo(r2.CalculatedDamage));
            Assert.That(r1.NextBattleRandomState, Is.EqualTo(r2.NextBattleRandomState));
            Assert.That(r1.NextState.ParticipationCounts["IA"], Is.EqualTo(r2.NextState.ParticipationCounts["IA"]));
        }
    }
}
