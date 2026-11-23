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
    public sealed class BattleTurnRulesTests
    {
        private static readonly Pcg32State Rng = new Pcg32RandomSource(301, 11).CaptureState();

        private static StageProgressConfig StageConfig() => new StageProgressConfig(
            25, 5, 5, 4, 0, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static StageProgressState Stage()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            return StageProgressRules.BeginBattle(state).NextState;
        }

        private static CharacterDefinition Def(string id, params string[] units) =>
            new CharacterDefinition(id, id, 1, new[] { "Attr" }, units);

        private static CharacterStatDefinition Stats() => new CharacterStatDefinition(new[]
        {
            new CharacterCostStats(1,
                new[] { new CharacterStatOption(6, 2, 1) },
                new[] { new CharacterStatOption(12, 4, 1) },
                new[] { new CharacterStatOption(18, 6, 1) })
        });

        private static CharacterInstance Character(CharacterDefinition definition, string id, int star = 1)
        {
            var instance = new CharacterInstance(id, definition, 1, star);
            Assert.That(instance.EnsureStats(definition, Stats(), new Pcg32RandomSource(71, 5)), Is.True);
            return instance;
        }

        private static BoardState Board(params (int Slot, string Id)[] placements)
        {
            BoardState state = BoardState.CreateStandard();
            foreach ((int slot, string id) in placements)
            {
                Assert.That(state.TryWithCharacterAt(slot, id, out BoardState next), Is.True);
                state = next;
            }
            return state;
        }

        private static EnemyDefinition EnemyDef(string id = "E", int defense = 4, int hp = 12) =>
            new EnemyDefinition(id, id, 7, defense, hp, null, null, null,
                "T001", new[] { "T001" }, null, null);

        private static EnemyInstance Enemy(EnemyDefinition definition, int slot,
            int gap = 0, int cooldown = 0, int healthBonus = 0) 
        {
            Assert.That(EnemyInstanceFactory.TryCreate(definition, 0, slot, 0, 0,
                healthBonus, gap, 0, cooldown, out EnemyInstance instance,
                out EnemyInstanceFailure failure), Is.True, failure.ToString());
            return instance;
        }

        private static EnemyBoardState EnemyBoard(params EnemyInstance[] instances)
        {
            Assert.That(EnemyBoardState.TryCreate(2, 3, instances, out EnemyBoardState state,
                out EnemyBoardFailure failure), Is.True, failure.ToString());
            return state;
        }

        private static BattleState CreateBattle(CharacterInstance[] allies = null,
            CharacterDefinition[] definitions = null, BoardState board = null,
            EnemyBoardState enemyBoard = null, EnemyDefinition[] enemyDefinitions = null,
            BattleConfig config = null, CharacterDefinition[] selectedRoster = null,
            CharacterDefinition[] fullRoster = null)
        {
            allies = allies ?? Array.Empty<CharacterInstance>();
            definitions = definitions ?? Array.Empty<CharacterDefinition>();
            board = board ?? BoardState.CreateStandard();
            enemyBoard = enemyBoard ?? EnemyBoard();
            enemyDefinitions = enemyDefinitions ?? Array.Empty<EnemyDefinition>();
            IReadOnlyList<CharacterDefinition> selected = selectedRoster ?? definitions;
            IReadOnlyList<CharacterDefinition> full = fullRoster ?? definitions;
            BattleInitializationResult result = BattleInitializationRules.Initialize(
                new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30), Stage(), board,
                HandState.CreateStandard(), new CharacterOwnershipState(allies), definitions,
                enemyBoard, enemyDefinitions, new ItemEffectState(), selected, full, Rng, config);
            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            return result.State;
        }

        private static BattleState BeginFirstRound(BattleState state)
        {
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.BattleStartRequired));
            Assert.That(request.RequiresActionResolution, Is.True);
            state = BattleTurnRules.ResolveAction(request.NextState,
                BattleActionResolution.BoundaryCompleted(request.ActionRequestId)).NextState;
            request = BattleTurnRules.RequestNext(state);
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
            Assert.That(request.NextState.CurrentRound, Is.EqualTo(1));
            return request.NextState;
        }

        private static BattleState BeginEnemyTurn(BattleState state)
        {
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
            state = request.NextState;
            request = BattleTurnRules.RequestNext(state);
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
            Assert.That(request.RequiresActionResolution, Is.True);
            return BattleTurnRules.ResolveAction(request.NextState,
                BattleActionResolution.BoundaryCompleted(request.ActionRequestId)).NextState;
        }

        private static BattleState CompleteEnemyTurnAndRound(BattleState state,
            BattleScheduleEvent roundEvent = BattleScheduleEvent.RoundCompleted)
        {
            BattleScheduleResult turn;
            do
            {
                turn = BattleTurnRules.RequestNext(state);
                state = turn.NextState;
            }
            while (turn.Event == BattleScheduleEvent.EnemyWaiting ||
                   turn.Event == BattleScheduleEvent.EnemySkippedDefeated);
            Assert.That(turn.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnCompleted));
            BattleScheduleResult round = BattleTurnRules.RequestNext(turn.NextState);
            Assert.That(round.Event, Is.EqualTo(roundEvent));
            return round.NextState;
        }

        private static BattleScheduleResult RequestEnemy(BattleState state)
        {
            BattleState current = BeginFirstRound(state);
            current = BeginEnemyTurn(current);
            return BattleTurnRules.RequestNext(current);
        }

        private static BattleState ResolveEnemy(BattleScheduleResult request)
        {
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
            return BattleTurnRules.ResolveAction(request.NextState,
                BattleActionResolution.ActionCompleted(request.ActionRequestId)).NextState;
        }

        [Test]
        public void Schedule_StartsAtRoundOne_AlliesActBeforeEnemiesInSlotOrder()
        {
            CharacterDefinition[] defs = { Def("A"), Def("B") };
            CharacterInstance[] chars = { Character(defs[0], "IA"), Character(defs[1], "IB") };
            EnemyDefinition enemyDef = EnemyDef();
            BattleState state = CreateBattle(chars, defs, Board((6, "IA"), (2, "IB")),
                EnemyBoard(Enemy(enemyDef, 3)), new[] { enemyDef });
            state = BeginFirstRound(state);
            Assert.That(state.Phase, Is.EqualTo(BattlePhase.PlayerTurn));

            BattleScheduleResult first = BattleTurnRules.RequestNext(state);
            Assert.That(first.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
            Assert.That(first.ActorInstanceId, Is.EqualTo("IB"));
            Assert.That(first.NextState.NextActorIndex, Is.Zero);
            BattleScheduleResult repeated = BattleTurnRules.RequestNext(first.NextState);
            Assert.That(repeated.ActionRequestId, Is.EqualTo(first.ActionRequestId));
            Assert.That(repeated.NextState.NextActorIndex, Is.Zero);

            state = BattleTurnRules.ResolveAction(first.NextState,
                BattleActionResolution.ActionCompleted(first.ActionRequestId)).NextState;
            BattleScheduleResult second = BattleTurnRules.RequestNext(state);
            Assert.That(second.ActorInstanceId, Is.EqualTo("IA"));
            state = BattleTurnRules.ResolveAction(second.NextState,
                BattleActionResolution.ActionCompleted(second.ActionRequestId)).NextState;
            state = BeginEnemyTurn(state);
            Assert.That(state.Phase, Is.EqualTo(BattlePhase.EnemyTurn));
            Assert.That(BattleTurnRules.RequestNext(state).Event,
                Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
        }

        [Test]
        public void Schedule_SkipsEmptyBoardSlotsAndHandCharacters()
        {
            CharacterDefinition[] defs = { Def("A"), Def("H") };
            CharacterInstance[] chars = { Character(defs[0], "IA"), Character(defs[1], "IH") };
            BoardState board = Board((8, "IA"));
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Character("IH"), out hand), Is.True);
            BattleInitializationResult init = BattleInitializationRules.Initialize(
                new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30), Stage(), board, hand,
                new CharacterOwnershipState(chars), defs, EnemyBoard(), Array.Empty<EnemyDefinition>(),
                new ItemEffectState(), defs, defs, Rng);
            Assert.That(init.Succeeded, Is.True, init.Failure.ToString());
            BattleState state = BeginFirstRound(init.State);
            BattleScheduleResult action = BattleTurnRules.RequestNext(state);
            Assert.That(action.ActorInstanceId, Is.EqualTo("IA"));
            state = BattleTurnRules.ResolveAction(action.NextState,
                BattleActionResolution.ActionCompleted(action.ActionRequestId)).NextState;
            Assert.That(BattleTurnRules.RequestNext(state).Event,
                Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
        }

        [Test]
        public void Schedule_WithZeroAlliesAndZeroEnemies_CanCompleteRoundsWithoutActions()
        {
            BattleState state = BeginFirstRound(CreateBattle());
            state = BeginEnemyTurn(state);
            state = CompleteEnemyTurnAndRound(state);
            Assert.That(state.CurrentRound, Is.EqualTo(2));
            Assert.That(state.Phase, Is.EqualTo(BattlePhase.RoundStart));
            BattleScheduleResult roundStarted = BattleTurnRules.RequestNext(state);
            Assert.That(roundStarted.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 3)]
        public void EnemyGap_DeterminesFirstActionRound(int gap, int expectedRound)
        {
            EnemyDefinition definition = EnemyDef();
            BattleState state = CreateBattle(enemyBoard: EnemyBoard(Enemy(definition, 0, gap)),
                enemyDefinitions: new[] { definition });
            state = BeginFirstRound(state);
            state = BeginEnemyTurn(state);
            BattleScheduleResult result = BattleTurnRules.RequestNext(state);
            for (int round = 1; round <= expectedRound; round++)
            {
                if (round < expectedRound)
                {
                    Assert.That(result.Event, Is.EqualTo(BattleScheduleEvent.EnemyWaiting));
                    Assert.That(result.NextState.Enemies[0].Gap, Is.EqualTo(Math.Max(0, gap - round)));
                    state = CompleteEnemyTurnAndRound(result.NextState);
                    state = BattleTurnRules.RequestNext(state).NextState;
                    state = BeginEnemyTurn(state);
                    result = BattleTurnRules.RequestNext(state);
                }
                else
                {
                    Assert.That(state.CurrentRound, Is.EqualTo(expectedRound));
                    Assert.That(result.Event, Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
                }
            }
        }

        [Test]
        public void EnemyCooldownZero_AllowsAnActionEachRound()
        {
            EnemyDefinition definition = EnemyDef();
            BattleState state = CreateBattle(enemyBoard: EnemyBoard(Enemy(definition, 0, cooldown: 0)),
                enemyDefinitions: new[] { definition });
            BattleScheduleResult action = RequestEnemy(state);
            state = ResolveEnemy(action);
            Assert.That(state.Enemies[0].NextActionRound, Is.EqualTo(2));
            state = CompleteEnemyTurnAndRound(state);
            state = BattleTurnRules.RequestNext(state).NextState;
            state = BeginEnemyTurn(state);
            Assert.That(BattleTurnRules.RequestNext(state).Event,
                Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
        }

        [Test]
        public void EnemyCooldownTwo_ActsAgainOnRoundFourAndDoesNotChangeWhileWaiting()
        {
            EnemyDefinition definition = EnemyDef();
            BattleState state = CreateBattle(enemyBoard: EnemyBoard(Enemy(definition, 0, cooldown: 2)),
                enemyDefinitions: new[] { definition });
            state = ResolveEnemy(RequestEnemy(state));
            Assert.That(state.Enemies[0].NextActionRound, Is.EqualTo(4));
            Assert.That(state.Enemies[0].Cooldown, Is.EqualTo(2));

            for (int round = 2; round <= 3; round++)
            {
                state = CompleteEnemyTurnAndRound(state);
                state = BattleTurnRules.RequestNext(state).NextState;
                state = BeginEnemyTurn(state);
                BattleScheduleResult wait = BattleTurnRules.RequestNext(state);
                Assert.That(wait.Event, Is.EqualTo(BattleScheduleEvent.EnemyWaiting));
                Assert.That(wait.NextState.Enemies[0].Cooldown, Is.EqualTo(2));
                state = wait.NextState;
            }
            state = CompleteEnemyTurnAndRound(state);
            state = BattleTurnRules.RequestNext(state).NextState;
            state = BeginEnemyTurn(state);
            Assert.That(state.CurrentRound, Is.EqualTo(4));
            Assert.That(BattleTurnRules.RequestNext(state).Event,
                Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
        }

        [Test]
        public void EnemyWait_DecrementsDisplayGapWithoutConsumingBattleRandom()
        {
            EnemyDefinition definition = EnemyDef();
            BattleState state = CreateBattle(enemyBoard: EnemyBoard(Enemy(definition, 0, gap: 2)),
                enemyDefinitions: new[] { definition });
            BattleState original = state;
            BattleScheduleResult wait = RequestEnemy(state);
            Assert.That(wait.Event, Is.EqualTo(BattleScheduleEvent.EnemyWaiting));
            Assert.That(wait.NextState.Enemies[0].Gap, Is.EqualTo(1));
            Assert.That(wait.NextState.Enemies[0].Cooldown, Is.Zero);
            Assert.That(wait.NextState.BattleRandomState, Is.EqualTo(Rng));
            Assert.That(original.Enemies[0].Gap, Is.EqualTo(2));
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void AllyStun_SkipsAndDecrementsOnceBeforeUnitMoveScheduling(int stunRounds, bool shouldSkip)
        {
            CharacterDefinition definition = Def("A");
            CharacterInstance character = Character(definition, "IA");
            BattleState state = CreateBattle(new[] { character }, new[] { definition }, Board((0, "IA")));
            BattleUnitState ally = state.Allies[0];
            var stunned = new BattleUnitState(ally.InstanceId, ally.DefinitionId, ally.Side,
                ally.SlotIndex, ally.Attack, ally.Defense, ally.HasHealth, ally.MaxHP, ally.CurrentHP,
                ally.Gap, ally.Cooldown, ally.NextActionRound, stunRounds, ally.Star,
                ally.UnitTags, ally.AttributeTags, ally.TargetIds, ally.NextTargetIndex,
                ally.CipEffectIds, ally.AeEffectIds, ally.PigEffectIds,
                ally.TemporaryAttackBonus, ally.TemporaryDefenseBonus, ally.TemporaryDefenseReduction);
            Assert.That(state.TryWithAllyState(stunned, out state), Is.True);
            state = BeginFirstRound(state);

            for (int i = 0; i < stunRounds; i++)
            {
                BattleScheduleResult skip = BattleTurnRules.RequestNext(state);
                Assert.That(skip.Event, Is.EqualTo(BattleScheduleEvent.AllySkippedByStun));
                Assert.That(skip.NextState.Allies[0].StunRounds, Is.EqualTo(stunRounds - i - 1));
                state = skip.NextState;
                if (i < stunRounds - 1)
                {
                    state = CompleteEnemyTurnAndRound(BeginEnemyTurn(state));
                    state = BattleTurnRules.RequestNext(state).NextState;
                }
            }
            if (shouldSkip)
                Assert.That(BattleTurnRules.RequestNext(state).Event,
                    Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
            else
                Assert.That(BattleTurnRules.RequestNext(state).Event,
                    Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
        }

        [Test]
        public void AllyStun_DecrementsBeforeAnyMoveIsRecordedAndNeverGoesBelowZero()
        {
            CharacterDefinition a = Def("A", "X"), b = Def("B", "X");
            CharacterInstance ia = Character(a, "IA"), ib = Character(b, "IB");
            BattleState state = CreateBattle(new[] { ia, ib }, new[] { a, b }, Board((0, "IA"), (1, "IB")),
                selectedRoster: new[] { a, b }, fullRoster: new[] { a, b });
            BattleUnitState ally = state.Allies[0];
            var stunned = new BattleUnitState(ally.InstanceId, ally.DefinitionId, ally.Side,
                ally.SlotIndex, ally.Attack, ally.Defense, ally.HasHealth, ally.MaxHP, ally.CurrentHP,
                ally.Gap, ally.Cooldown, ally.NextActionRound, 1, ally.Star,
                ally.UnitTags, ally.AttributeTags, ally.TargetIds, ally.NextTargetIndex,
                ally.CipEffectIds, ally.AeEffectIds, ally.PigEffectIds);
            Assert.That(state.TryWithAllyState(stunned, out state), Is.True);
            state = BeginFirstRound(state);
            BattleScheduleResult skip = BattleTurnRules.RequestNext(state);
            Assert.That(skip.Event, Is.EqualTo(BattleScheduleEvent.AllySkippedByStun));
            Assert.That(skip.NextState.ParticipationCounts.Count, Is.Zero);
            Assert.That(skip.NextState.UsedUnitMoveIds, Is.Empty);
            Assert.That(skip.NextState.Allies[0].StunRounds, Is.Zero);
        }

        private static BattleState MultiMoveBattle(int star)
        {
            CharacterDefinition a = Def("A", "X", "Y");
            CharacterDefinition b = Def("B", "X");
            CharacterDefinition c = Def("C", "Y");
            CharacterInstance ia = Character(a, "IA", star);
            CharacterInstance ib = Character(b, "IB");
            CharacterInstance ic = Character(c, "IC");
            return CreateBattle(new[] { ia, ib, ic }, new[] { a, b, c },
                Board((0, "IA"), (1, "IB"), (3, "IC")),
                selectedRoster: new[] { a, b, c }, fullRoster: new[] { a, b, c });
        }

        private static BattleScheduleResult RequestAlly(BattleState state)
        {
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
            return request;
        }

        [TestCase(1, 1, false)]
        [TestCase(2, 2, false)]
        [TestCase(3, 2, true)]
        public void UnitMoveParticipation_RespectsStarLimitAndPreservesDetectorPriority(
            int star, int completedMoves, bool canStillParticipate)
        {
            BattleState state = BeginFirstRound(MultiMoveBattle(star));
            Assert.That(state.UnitMoves.Count, Is.EqualTo(2));
            Assert.That(state.UnitMoves[0].UnitName, Is.EqualTo("X"));
            Assert.That(state.UnitMoves[1].UnitName, Is.EqualTo("Y"));
            Assert.That(BattleTurnRules.GetParticipationLimit(state.Allies[0]), Is.EqualTo(star));

            for (int i = 0; i < completedMoves; i++)
            {
                BattleScheduleResult action = RequestAlly(state);
                UnitMoveState move = state.UnitMoves[i];
                state = BattleTurnRules.ResolveAction(action.NextState,
                    BattleActionResolution.UnitMoveCompleted(action.ActionRequestId, move.MoveId,
                        move.ParticipantInstanceIds, endsActorTurn: false)).NextState;
                if (i < completedMoves - 1) state = BattleTurnRules.RequestNext(state).NextState;
            }
            Assert.That(BattleTurnRules.GetParticipationCount(state, "IA"), Is.EqualTo(completedMoves));
            Assert.That(BattleTurnRules.CanParticipateInUnitMove(state, "IA"), Is.EqualTo(canStillParticipate));
        }

        [Test]
        public void UnitMoveCannotBeRecordedTwiceAndFailedResolutionCanBeRetried()
        {
            BattleState state = BeginFirstRound(MultiMoveBattle(2));
            BattleScheduleResult first = RequestAlly(state);
            BattleScheduleResult failed = BattleTurnRules.ResolveAction(first.NextState,
                new BattleActionResolution(first.ActionRequestId, false,
                    BattleActionResolutionKind.UnitMoveCompleted, "X",
                    first.NextState.UnitMoves[0].ParticipantInstanceIds, false));
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(failed.NextState, Is.SameAs(first.NextState));

            UnitMoveState move = first.NextState.UnitMoves[0];
            state = BattleTurnRules.ResolveAction(first.NextState,
                BattleActionResolution.UnitMoveCompleted(first.ActionRequestId, move.MoveId,
                    move.ParticipantInstanceIds, false)).NextState;
            BattleScheduleResult sameActor = RequestAlly(state);
            BattleScheduleResult duplicate = BattleTurnRules.ResolveAction(sameActor.NextState,
                BattleActionResolution.UnitMoveCompleted(sameActor.ActionRequestId, move.MoveId,
                    move.ParticipantInstanceIds, false));
            Assert.That(duplicate.Succeeded, Is.False);
            Assert.That(duplicate.Failure, Is.EqualTo(BattleScheduleFailure.UnitMoveAlreadyUsed));
            Assert.That(duplicate.NextState, Is.SameAs(sameActor.NextState));
        }

        [Test]
        public void PassAfterMoveEndsActorWithoutInventingAnAttack()
        {
            BattleState state = BeginFirstRound(MultiMoveBattle(2));
            BattleScheduleResult first = RequestAlly(state);
            UnitMoveState move = state.UnitMoves[0];
            state = BattleTurnRules.ResolveAction(first.NextState,
                BattleActionResolution.UnitMoveCompleted(first.ActionRequestId, move.MoveId,
                    move.ParticipantInstanceIds, false)).NextState;
            BattleScheduleResult again = RequestAlly(state);
            state = BattleTurnRules.ResolveAction(again.NextState,
                BattleActionResolution.PassAfterUnitMove(again.ActionRequestId)).NextState;
            Assert.That(state.NextActorIndex, Is.EqualTo(1));
            Assert.That(state.ParticipationCounts["IA"], Is.EqualTo(1));
        }

        [Test]
        public void RoundEnd_DecreasesLivingEnemyDefenseIncludingWaitersButNotDeadEnemiesOnce()
        {
            EnemyDefinition definition = EnemyDef(defense: 1, hp: 5);
            BattleState state = CreateBattle(enemyBoard: EnemyBoard(
                Enemy(definition, 0, gap: 2), Enemy(definition, 4)),
                enemyDefinitions: new[] { definition });
            BattleUnitState dead = state.Enemies[1];
            var deadState = new BattleUnitState(dead.InstanceId, dead.DefinitionId, dead.Side,
                dead.SlotIndex, dead.Attack, dead.Defense, true, dead.MaxHP, 0,
                dead.Gap, dead.Cooldown, dead.NextActionRound, dead.StunRounds, dead.Star,
                dead.UnitTags, dead.AttributeTags, dead.TargetIds, dead.NextTargetIndex,
                dead.CipEffectIds, dead.AeEffectIds, dead.PigEffectIds);
            Assert.That(state.TryWithEnemyState(deadState, out state), Is.True);
            state = BeginFirstRound(state);
            state = BeginEnemyTurn(state);
            BattleScheduleResult wait = BattleTurnRules.RequestNext(state);
            Assert.That(wait.Event, Is.EqualTo(BattleScheduleEvent.EnemyWaiting));
            state = CompleteEnemyTurnAndRound(wait.NextState);
            Assert.That(state.Enemies[0].Defense, Is.Zero);
            Assert.That(state.Enemies[1].Defense, Is.EqualTo(1));
            Assert.That(state.Enemies[1].CurrentHP, Is.Zero);
            Assert.That(state.Phase, Is.EqualTo(BattlePhase.RoundStart));
            BattleScheduleResult repeated = BattleTurnRules.RequestNext(state);
            Assert.That(repeated.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
            Assert.That(repeated.NextState.Enemies[0].Defense, Is.Zero);
        }

        [Test]
        public void EnemyTurnStartIsAnExplicitHookBeforeEnemyAction()
        {
            EnemyDefinition definition = EnemyDef();
            BattleState state = BeginFirstRound(CreateBattle(enemyBoard: EnemyBoard(Enemy(definition, 0)),
                enemyDefinitions: new[] { definition }));
            BattleScheduleResult allyDone = BattleTurnRules.RequestNext(state);
            BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(allyDone.NextState);
            Assert.That(enemyStart.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
            Assert.That(enemyStart.RequiresActionResolution, Is.True);
            Assert.That(enemyStart.NextState.Phase, Is.EqualTo(BattlePhase.EnemyTurnStart));
            Assert.That(enemyStart.NextState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
            state = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            Assert.That(BattleTurnRules.RequestNext(state).Event,
                Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
        }

        private static BattleState CompleteEmptyRound(BattleState state, BattleScheduleEvent expected)
        {
            if (state.Phase == BattlePhase.RoundStart)
            {
                BattleScheduleResult started = BattleTurnRules.RequestNext(state);
                Assert.That(started.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
                state = started.NextState;
            }
            else
            {
                state = BeginFirstRound(state);
            }
            state = BeginEnemyTurn(state);
            return CompleteEnemyTurnAndRound(state, expected);
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(100)]
        public void RoundLimit_IsDetectedOnlyAfterTheFinalRoundIsProcessed(int maxRounds)
        {
            BattleState state = CreateBattle(config: new BattleConfig(maxRounds));
            for (int round = 1; round < maxRounds; round++)
            {
                state = CompleteEmptyRound(state, BattleScheduleEvent.RoundCompleted);
                Assert.That(state.CurrentRound, Is.EqualTo(round + 1));
            }
            if (state.Phase == BattlePhase.RoundStart)
            {
                BattleScheduleResult started = BattleTurnRules.RequestNext(state);
                Assert.That(started.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
                state = started.NextState;
            }
            else
            {
                state = BeginFirstRound(state);
            }
            Assert.That(state.CurrentRound, Is.EqualTo(maxRounds));
            state = BeginEnemyTurn(state);
            BattleState ended = CompleteEnemyTurnAndRound(state,
                BattleScheduleEvent.RoundLimitReached);
            Assert.That(ended.CurrentRound, Is.EqualTo(maxRounds));
        }

        [Test]
        public void RoundLimit100_AllowsTheFinalEnemyActionThenSignalsUndecidedPending()
        {
            EnemyDefinition definition = EnemyDef();
            BattleState state = CreateBattle(enemyBoard: EnemyBoard(Enemy(definition, 0)),
                enemyDefinitions: new[] { definition }, config: new BattleConfig(100));
            state = BeginFirstRound(state);
            for (int round = 1; round <= 100; round++)
            {
                state = BeginEnemyTurn(state);
                BattleScheduleResult action = BattleTurnRules.RequestNext(state);
                Assert.That(action.Event, Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
                state = ResolveEnemy(action);
                BattleScheduleEvent expected = round == 100
                    ? BattleScheduleEvent.RoundLimitReached : BattleScheduleEvent.RoundCompleted;
                state = CompleteEnemyTurnAndRound(state, expected);
                if (round < 100) state = BattleTurnRules.RequestNext(state).NextState;
            }
            Assert.That(state.CurrentRound, Is.EqualTo(100));
            Assert.That(state.Phase, Is.EqualTo(BattlePhase.RoundLimitReached));
            Assert.That(state.Status, Is.EqualTo(BattleExecutionStatus.InProgress));
            Assert.That(state.Outcome, Is.EqualTo(BattleOutcome.Pending));
        }

        [Test]
        public void Schedule_RejectedAndDuplicateCompletionsPreserveTheCurrentPendingRequest()
        {
            CharacterDefinition definition = Def("A");
            CharacterInstance character = Character(definition, "IA");
            BattleState state = BeginFirstRound(CreateBattle(new[] { character },
                new[] { definition }, Board((0, "IA"))));
            BattleScheduleResult request = RequestAlly(state);
            BattleScheduleResult rejected = BattleTurnRules.ResolveAction(request.NextState,
                BattleActionResolution.ActionCompleted(request.ActionRequestId, succeeded: false));
            Assert.That(rejected.Failure, Is.EqualTo(BattleScheduleFailure.ActionResolutionFailed));
            Assert.That(rejected.NextState, Is.SameAs(request.NextState));

            BattleState completed = BattleTurnRules.ResolveAction(request.NextState,
                BattleActionResolution.ActionCompleted(request.ActionRequestId)).NextState;
            BattleScheduleResult duplicate = BattleTurnRules.ResolveAction(completed,
                BattleActionResolution.ActionCompleted(request.ActionRequestId));
            Assert.That(duplicate.Failure, Is.EqualTo(BattleScheduleFailure.NoPendingAction));
            Assert.That(duplicate.NextState, Is.SameAs(completed));
            Assert.That(request.NextState.NextActorIndex, Is.Zero);
            Assert.That(completed.NextActorIndex, Is.EqualTo(1));
            Assert.That(completed.BattleRandomState, Is.EqualTo(Rng));
        }

        [Test]
        public void Schedule_UsesStableRequestIdsAndNeverConsumesRandomness()
        {
            BattleState initial = CreateBattle();
            BattleScheduleResult a = BattleTurnRules.RequestNext(initial);
            BattleScheduleResult b = BattleTurnRules.RequestNext(initial);
            Assert.That(a.ActionRequestId, Is.EqualTo(b.ActionRequestId));
            Assert.That(a.NextState.BattleRandomState, Is.EqualTo(Rng));
            Assert.That(a.NextState.Phase, Is.EqualTo(BattlePhase.BattleStart));
            Assert.That(initial.Phase, Is.EqualTo(BattlePhase.BattleStart));
            Assert.That(a.NextState.NextActionRequestSequence, Is.EqualTo(1));
            Assert.That(BattleTurnRules.ResolveAction(a.NextState,
                BattleActionResolution.BoundaryCompleted(a.ActionRequestId)).NextState.BattleRandomState,
                Is.EqualTo(Rng));
        }
    }
}
