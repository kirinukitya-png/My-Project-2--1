using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    /// <summary>End-to-end rule-chain tests. These deliberately compose existing rules instead of adding a BattleRunner.</summary>
    public sealed class BattleIntegrationScenarioTests
    {
        private sealed class AllySpec
        {
            public string Id; public int Attack; public int Defense;
            public string[] Units = Array.Empty<string>();
            public string[] Attributes = Array.Empty<string>();
            public AllySpec(string id, int attack = 0, int defense = 0,
                string[] units = null, string[] attributes = null)
            { Id = id; Attack = attack; Defense = defense; Units = units ?? Array.Empty<string>(); Attributes = attributes ?? Array.Empty<string>(); }
        }

        private sealed class EnemySpec
        {
            public string Id; public int Attack; public int Defense; public int Hp;
            public int Gap; public int Cooldown; public string Target = "T001";
            public string[] TargetIds = { "T001" };
            public string[] Cip = Array.Empty<string>(); public string[] Ae = Array.Empty<string>(); public string[] Pig = Array.Empty<string>();
            public EnemySpec(string id, int attack = 0, int defense = 0, int hp = 10,
                int gap = 0, int cooldown = 0, string[] cip = null, string[] ae = null, string[] pig = null,
                string[] targetIds = null)
            { Id = id; Attack = attack; Defense = defense; Hp = hp; Gap = gap; Cooldown = cooldown;
              Cip = cip ?? Array.Empty<string>(); Ae = ae ?? Array.Empty<string>(); Pig = pig ?? Array.Empty<string>();
              TargetIds = targetIds ?? new[] { "T001" }; }
        }

        private sealed class World
        {
            public BattleState Battle; public EconomyState Economy; public BoardState Board; public HandState Hand;
            public CharacterOwnershipState Ownership; public ItemEffectState ItemEffects;
            public CharacterDefinition[] Definitions; public CharacterStatDefinition Stats;
            public Dictionary<string, BattleTargetDefinition> Targets;
            public Dictionary<string, BattleEffectDefinition> Effects;
        }

        private sealed class RunResult
        {
            public BattleFinishResult Finish; public EconomyState Economy; public BattleState BeforeFinish;
            public int UnitMoves; public int AllyAttacks; public int EnemyAttacks; public int EnemyWaits;
            public readonly List<string> ActionIds = new List<string>();
            public readonly List<string> EnemyActionOrder = new List<string>();
        }

        private static Pcg32State Rng(ulong seed = 1251) => new Pcg32RandomSource(seed, 77).CaptureState();
        private static StageProgressConfig StageConfig() => new StageProgressConfig(
            25, 5, 5, 4, 0, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static World Create(AllySpec[] allies, EnemySpec[] enemies, int playerHp = 20,
            int maxHp = 30, int block = 0, int level = 12, int maxRounds = 8, ulong seed = 1251,
            ItemEffectState itemEffects = null)
        {
            var world = new World { Board = BoardState.CreateStandard(), Hand = HandState.CreateStandard(),
                ItemEffects = itemEffects ?? new ItemEffectState(), Targets = new Dictionary<string, BattleTargetDefinition>(),
                Effects = new Dictionary<string, BattleEffectDefinition>() };
            world.Targets.Add("T001", new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                BattleTargetSelection.Uniform));
            world.Targets.Add("T009", new BattleTargetDefinition("T009", BattleTargetFilter.RandomCells,
                BattleTargetSelection.All, cellSelection: BattleTargetSelection.Uniform, cellCount: 2));
            world.Targets.Add("T010", new BattleTargetDefinition("T010", BattleTargetFilter.RandomCells,
                BattleTargetSelection.All, cellSelection: BattleTargetSelection.Weighted, cellCount: 2,
                cellWeights: new[] { new BattleTargetWeightRule(BattleTargetPosition.Front, 3),
                    new BattleTargetWeightRule(BattleTargetPosition.Middle, 1),
                    new BattleTargetWeightRule(BattleTargetPosition.Back, 0) }));
            world.Targets.Add("TALL", new BattleTargetDefinition("TALL", BattleTargetFilter.AllBoard,
                BattleTargetSelection.All));
            world.Targets.Add("TBACK", new BattleTargetDefinition("TBACK", BattleTargetFilter.BackColumn,
                BattleTargetSelection.Uniform));

            world.Definitions = new CharacterDefinition[allies.Length];
            var instances = new CharacterInstance[allies.Length];
            var factory = new CharacterInstanceFactory();
            for (int i = 0; i < allies.Length; i++)
            {
                AllySpec spec = allies[i];
                world.Definitions[i] = new CharacterDefinition(spec.Id, spec.Id, 1,
                    spec.Attributes, spec.Units);
                world.Stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1,
                    new[] { new CharacterStatOption(spec.Attack, spec.Defense, 1) },
                    new[] { new CharacterStatOption(spec.Attack, spec.Defense, 1) },
                    new[] { new CharacterStatOption(spec.Attack, spec.Defense, 1) }) });
                instances[i] = factory.Create(world.Definitions[i], 1);
                Assert.That(instances[i].EnsureStats(world.Definitions[i], world.Stats,
                    new Pcg32RandomSource((ulong)(100 + i), 19)), Is.True);
                Assert.That(world.Board.TryWithCharacterAt(i, instances[i].InstanceId, out BoardState next), Is.True);
                world.Board = next;
            }
            world.Ownership = new CharacterOwnershipState(instances);
            world.Economy = new EconomyState(10, 0, level, 12, 10, 0, level, playerHp, maxHp);

            EnemyDefinition[] enemyDefinitions = new EnemyDefinition[enemies.Length];
            EnemyBoardState enemyBoard = EnemyBoardState.CreateEmpty();
            for (int i = 0; i < enemies.Length; i++)
            {
                EnemySpec spec = enemies[i];
                enemyDefinitions[i] = new EnemyDefinition(spec.Id, spec.Id, spec.Attack,
                    spec.Defense, spec.Hp, spec.Cip, spec.Ae, spec.Pig, spec.Target,
                    spec.TargetIds, null, null);
                Assert.That(EnemyInstanceFactory.TryCreate(enemyDefinitions[i], 0, i, 0, 0, 0,
                    spec.Gap, 0, spec.Cooldown, out EnemyInstance instance, out EnemyInstanceFailure failure),
                    Is.True, failure.ToString());
                EnemyBoardOperationResult placement = enemyBoard.TryPlace(instance);
                Assert.That(placement.Succeeded, Is.True, placement.Failure.ToString());
                enemyBoard = placement.NextState;
                foreach (string id in spec.Cip) AddEffect(world, new BattleEffectDefinition(id,
                    BattleEffectType.Gold, 3, BattleEffectTarget.Player, BattleEffectDuration.Immediate));
                foreach (string id in spec.Ae) AddEffect(world, new BattleEffectDefinition(id,
                    BattleEffectType.Gold, -1, BattleEffectTarget.Player, BattleEffectDuration.Immediate));
                foreach (string id in spec.Pig) AddEffect(world, new BattleEffectDefinition(id,
                    BattleEffectType.Gold, -2, BattleEffectTarget.Player, BattleEffectDuration.Immediate));
            }

            StageProgressConfig stageConfig = StageConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(stageConfig), stageConfig).NextState;
            StageProgressState stage = StageProgressRules.BeginBattle(prep).NextState;
            BattleInitializationResult initialized = BattleInitializationRules.Initialize(world.Economy,
                stage, world.Board, world.Hand, world.Ownership, world.Definitions,
                enemyBoard, enemyDefinitions, world.ItemEffects, world.Definitions, world.Definitions,
                Rng(seed), new BattleConfig(maxRounds));
            Assert.That(initialized.Succeeded, Is.True, initialized.Failure.ToString());
            world.Battle = initialized.State;
            return world;
        }

        private static void AddEffect(World world, BattleEffectDefinition definition)
        { if (!world.Effects.ContainsKey(definition.EffectId)) world.Effects.Add(definition.EffectId, definition); }

        private static BattleState Start(World world)
        {
            BattleScheduleResult start = BattleTurnRules.RequestNext(world.Battle);
            Assert.That(start.Event, Is.EqualTo(BattleScheduleEvent.BattleStartRequired));
            BattleScheduleResult started = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId));
            Assert.That(started.Succeeded, Is.True);
            return ResolveEffects(started.NextState, ref world.Economy, world.Effects);
        }

        private static BattleState ResolveEffects(BattleState state, ref EconomyState economy,
            IReadOnlyDictionary<string, BattleEffectDefinition> definitions)
        {
            while (state.PendingEffectTriggers.Count > 0)
            {
                BattlePendingEffectTrigger trigger = state.PendingEffectTriggers[0];
                BattleEffectResolutionResult result = BattleEffectRules.ResolveNextPendingTrigger(
                    state, economy, trigger.TriggerId, definitions);
                Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
                state = result.NextState;
                economy = result.NextEconomyState;
            }
            return state;
        }

        private static RunResult Run(World world)
        {
            BattleState state = Start(world);
            var run = new RunResult();
            for (int guard = 0; guard < 1000; guard++)
            {
                BattleScheduleResult request = BattleTurnRules.RequestNext(state);
                Assert.That(request.Succeeded, Is.True, request.Failure.ToString());
                state = request.NextState;
                switch (request.Event)
                {
                    case BattleScheduleEvent.RoundStarted: break;
                    case BattleScheduleEvent.AllySkippedByStun:
                    case BattleScheduleEvent.AllySkippedByParticipationLimit:
                    case BattleScheduleEvent.EnemyWaiting:
                    case BattleScheduleEvent.EnemySkippedDefeated:
                        if (request.Event == BattleScheduleEvent.EnemyWaiting) run.EnemyWaits++;
                        break;
                    case BattleScheduleEvent.AllyActionRequired:
                    {
                        run.ActionIds.Add(request.ActionRequestId);
                        BattleUnitMoveDecision decision = BattleUnitMoveRules.DecideCurrentAllyAction(
                            state, request.ActionRequestId);
                        Assert.That(decision.Succeeded, Is.True, decision.ScheduleFailure.ToString());
                        if (decision.Kind == BattleUnitMoveDecisionKind.MoveAvailable)
                        {
                            BattleAttackResult move = BattleUnitMoveRules.ResolveUnitMoveAttack(
                                state, request.ActionRequestId, decision.SelectedMove.MoveId);
                            Assert.That(move.Succeeded, Is.True, move.Failure.ToString());
                            run.UnitMoves++;
                            state = move.NextState;
                        }
                        else if (decision.Kind == BattleUnitMoveDecisionKind.SkipNormalAttackAfterMove)
                            state = decision.NextState;
                        else
                        {
                            BattleAttackResult attack = BattleNormalAttackRules.ResolveAllyNormalAttack(
                                state, request.ActionRequestId);
                            Assert.That(attack.Succeeded, Is.True, attack.Failure.ToString());
                            run.AllyAttacks++;
                            state = attack.NextState;
                        }
                        state = ResolveEffects(state, ref world.Economy, world.Effects);
                        break;
                    }
                    case BattleScheduleEvent.AllyTurnCompleted: break;
                    case BattleScheduleEvent.EnemyTurnStartRequired:
                    {
                        BattleScheduleResult boundary = BattleTurnRules.ResolveAction(state,
                            BattleActionResolution.BoundaryCompleted(request.ActionRequestId));
                        Assert.That(boundary.Succeeded, Is.True, boundary.Failure.ToString());
                        state = boundary.NextState;
                        break;
                    }
                    case BattleScheduleEvent.EnemyActionRequired:
                    {
                        run.ActionIds.Add(request.ActionRequestId);
                        run.EnemyActionOrder.Add(state.CurrentRound + ":" + state.Enemies[state.NextActorIndex].SlotIndex);
                        BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                            state, request.ActionRequestId, world.Targets);
                        Assert.That(attack.Succeeded, Is.True, attack.Failure.ToString());
                        run.EnemyAttacks++;
                        state = ResolveEffects(attack.NextState, ref world.Economy, world.Effects);
                        break;
                    }
                    case BattleScheduleEvent.EnemyTurnCompleted:
                    case BattleScheduleEvent.RoundCompleted: break;
                    case BattleScheduleEvent.BattleOutcomeCheckRequired:
                    case BattleScheduleEvent.RoundLimitReached:
                        run.BeforeFinish = state;
                        run.Finish = BattleFinishRules.TryFinish(state, world.Economy, world.Board,
                            world.Hand, world.Ownership, world.ItemEffects);
                        Assert.That(run.Finish.Succeeded, Is.True, run.Finish.Failure.ToString());
                        run.Economy = run.Finish.NextEconomyState;
                        return run;
                    case BattleScheduleEvent.PostActionEffectsRequired:
                        state = ResolveEffects(state, ref world.Economy, world.Effects);
                        break;
                    default: Assert.Fail("Unexpected scheduler event: " + request.Event); break;
                }
            }
            Assert.Fail("Battle did not terminate within the safety guard.");
            return run;
        }

        private static World OneAllyOneEnemy(int allyAttack = 20, int allyDefense = 1,
            int enemyAttack = 0, int enemyDefense = 0, int enemyHp = 1, int playerHp = 20,
            int maxRounds = 4, int enemyGap = 0, int cooldown = 0, string[] units = null,
            string[] attributes = null, string[] cip = null, string[] ae = null, string[] pig = null,
            string[] targetIds = null, ulong seed = 1251) => Create(
                new[] { new AllySpec("A", allyAttack, allyDefense, units, attributes) },
                new[] { new EnemySpec("E", enemyAttack, enemyDefense, enemyHp, enemyGap,
                    cooldown, cip, ae, pig, targetIds) }, playerHp, 30, 0, 12, maxRounds, seed);

        [Test]
        public void ScenarioA_NormalAttackWinsFromInitializationToResultAndIsRepeatable()
        {
            World a = OneAllyOneEnemy(); World b = OneAllyOneEnemy();
            Pcg32State before = a.Battle.BattleRandomState;
            RunResult resultA = Run(a); RunResult resultB = Run(b);
            Assert.That(resultA.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(resultA.EnemyAttacks, Is.Zero);
            Assert.That(resultA.Finish.Result.BattleRandomCheckpoint, Is.EqualTo(resultB.Finish.Result.BattleRandomCheckpoint));
            Assert.That(resultA.Finish.Result.BattleRandomCheckpoint, Is.Not.EqualTo(before));
            Assert.That(a.Battle.Phase, Is.EqualTo(BattlePhase.BattleStart)); // original input snapshot is unchanged
            Assert.That(a.Battle.Enemies[0].CurrentHP, Is.EqualTo(1));
        }

        [Test]
        public void ScenarioB_EnemyAttackDefeatsPlayerAndDoesNotAdvanceAnotherRound()
        {
            RunResult result = Run(OneAllyOneEnemy(allyAttack: 0, enemyAttack: 20,
                enemyHp: 50, playerHp: 5, maxRounds: 6));
            Assert.That(result.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Defeat));
            Assert.That(result.Finish.Result.FinalRound, Is.EqualTo(1));
            Assert.That(result.Finish.Result.EndingPlayerHP, Is.Zero);
            Assert.That(result.EnemyAttacks, Is.EqualTo(1));
        }

        [Test]
        public void ScenarioC_AttributeBlockIsInitialOneUseAndNotReappliedNextRound()
        {
            World world = Create(new[] { new AllySpec("A", 0, 1, attributes: new[] { "Fire" }),
                    new AllySpec("B", 0, 1, attributes: new[] { "Fire" }) },
                new[] { new EnemySpec("E", 2, 0, 100) }, playerHp: 20, maxRounds: 2);
            Assert.That(world.Battle.AttributeBlock, Is.EqualTo(1));
            Assert.That(world.Battle.PlayerBlock, Is.EqualTo(1));
            RunResult result = Run(world);
            Assert.That(result.EnemyAttacks, Is.EqualTo(2));
            Assert.That(result.Finish.Result.BlockAbsorbed, Is.EqualTo(1));
            Assert.That(result.Finish.Result.EndingPlayerHP, Is.EqualTo(19));
            Assert.That(result.Finish.NextBattleState.PlayerBlock, Is.Zero);
        }

        [Test]
        public void ScenarioD_UnitMovePrecedesNormalAttacksAndPIGCompletesBeforeVictory()
        {
            World world = Create(new[] { new AllySpec("A", 2, 0, units: new[] { "Pair" }),
                    new AllySpec("B", 2, 0, units: new[] { "Pair" }) },
                new[] { new EnemySpec("E", 0, 0, 1, pig: new[] { "PIG" }) });
            RunResult result = Run(world);
            Assert.That(result.UnitMoves, Is.EqualTo(1));
            Assert.That(result.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(result.Finish.Result.ConfirmedPersistentEffects, Is.Empty);
            Assert.That(result.Finish.NextBattleState.ParticipationCounts, Is.Empty);
        }

        [Test]
        public void ScenarioD_UnitMoveParticipantsSkipTheirNormalAttacksForThatRound()
        {
            RunResult result = Run(Create(new[] { new AllySpec("A", 1, 0, units: new[] { "Pair" }),
                    new AllySpec("B", 1, 0, units: new[] { "Pair" }) },
                new[] { new EnemySpec("E", 0, 0, 100) }, maxRounds: 1));
            Assert.That(result.UnitMoves, Is.EqualTo(1));
            Assert.That(result.AllyAttacks, Is.Zero);
            Assert.That(result.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
        }

        [Test]
        public void ScenarioE_UnitMoveDefenseReductionRestoresAtEnemyStartThenRoundEndPersistsMinusOne()
        {
            World world = Create(new[] { new AllySpec("A", 1, 0, units: new[] { "Pair" }),
                    new AllySpec("B", 1, 0, units: new[] { "Pair" }), new AllySpec("C", 0, 0) },
                new[] { new EnemySpec("E", 0, 10, 100) }, maxRounds: 1);
            BattleState state = Start(world);
            BattleScheduleResult round = BattleTurnRules.RequestNext(state); state = round.NextState;
            BattleScheduleResult actor = BattleTurnRules.RequestNext(state);
            BattleUnitMoveDecision decision = BattleUnitMoveRules.DecideCurrentAllyAction(actor.NextState, actor.ActionRequestId);
            Assert.That(decision.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.MoveAvailable));
            BattleAttackResult move = BattleUnitMoveRules.ResolveUnitMoveAttack(actor.NextState,
                actor.ActionRequestId, decision.SelectedMove.MoveId);
            Assert.That(move.CalculatedDamage, Is.Zero);
            Assert.That(move.NextState.Enemies[0].Defense, Is.EqualTo(8));
            state = move.NextState;
            // Skip participants after their move and let the untagged ally finish its normal action.
            for (int i = 0; i < 2; i++) state = BattleTurnRules.RequestNext(state).NextState;
            BattleScheduleResult third = BattleTurnRules.RequestNext(state);
            Assert.That(third.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
            BattleAttackResult last = BattleNormalAttackRules.ResolveAllyNormalAttack(third.NextState, third.ActionRequestId);
            Assert.That(last.Succeeded, Is.True);
            state = last.NextState;
            BattleScheduleResult alliesDone = BattleTurnRules.RequestNext(state);
            Assert.That(alliesDone.Event, Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
            BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(alliesDone.NextState);
            Assert.That(enemyStart.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
            state = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            Assert.That(state.Enemies[0].Defense, Is.EqualTo(10));
            RunResult result = ContinueFromEnemyTurn(state, world);
            Assert.That(result.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(result.Finish.NextBattleState.Enemies[0].Defense, Is.EqualTo(9));
            Assert.That(result.Finish.NextBattleState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
        }

        private static RunResult ContinueFromEnemyTurn(BattleState state, World world)
        {
            // Continue through the real scheduler without re-running the start boundary.
            for (int guard = 0; guard < 100; guard++)
            {
                BattleScheduleResult request = BattleTurnRules.RequestNext(state); state = request.NextState;
                if (request.Event == BattleScheduleEvent.EnemyActionRequired)
                {
                    BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(state,
                        request.ActionRequestId, world.Targets); Assert.That(attack.Succeeded, Is.True);
                    state = ResolveEffects(attack.NextState, ref world.Economy, world.Effects);
                }
                else if (request.Event == BattleScheduleEvent.EnemyTurnCompleted || request.Event == BattleScheduleEvent.RoundCompleted) { }
                else if (request.Event == BattleScheduleEvent.RoundLimitReached || request.Event == BattleScheduleEvent.BattleOutcomeCheckRequired)
                {
                    BattleFinishResult finish = BattleFinishRules.TryFinish(state, world.Economy,
                        world.Board, world.Hand, world.Ownership, world.ItemEffects);
                    return new RunResult { Finish = finish, Economy = finish.NextEconomyState };
                }
                else Assert.That(request.Event, Is.Not.EqualTo(BattleScheduleEvent.AllyActionRequired));
            }
            Assert.Fail("Enemy turn did not reach a terminal boundary."); return null;
        }

        [Test]
        public void ScenarioF_CipAeAndPigAreSerializedAndPersistentGoldIsNotReapplied()
        {
            World world = OneAllyOneEnemy(allyAttack: 1, enemyAttack: 0, enemyHp: 2,
                maxRounds: 4, cip: new[] { "CIP" }, ae: new[] { "AE" }, pig: new[] { "PIG" });
            RunResult result = Run(world);
            Assert.That(result.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(result.EnemyAttacks, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.Finish.Result.ConfirmedPersistentEffects, Is.Empty);
            Assert.That(result.Economy.Gold, Is.EqualTo(10 + 3 - result.EnemyAttacks - 2));
            Assert.That(result.Finish.NextBattleState.ProcessedEffectTriggerIds.Count, Is.EqualTo(2 + result.EnemyAttacks));
            Assert.That(result.Finish.NextBattleState.ProcessedEffectTriggerIds[0], Does.StartWith("battle:0:cip:"));
            Assert.That(result.Finish.NextBattleState.ProcessedEffectTriggerIds[1], Does.EndWith(":AE"));
            Assert.That(result.Finish.NextBattleState.ProcessedEffectTriggerIds[2], Does.EndWith(":PIG"));
        }

        [Test]
        public void ScenarioG_T009AndT010IncludeEmptyCellsAndRepeatFromSameCheckpoint()
        {
            World world = Create(new[] { new AllySpec("A", 0, 1), new AllySpec("B", 0, 1) },
                new[] { new EnemySpec("E", 3, 0, 20, targetIds: new[] { "T009", "T010" }) }, seed: 99);
            BattleState state = Start(world);
            foreach (string id in new[] { "T009", "T010" })
            {
                BattleTargetSelectionResult a = BattleTargetRules.SelectTargets(world.Targets[id], state.Allies,
                    state.Enemies[0], state.BattleRandomState);
                BattleTargetSelectionResult b = BattleTargetRules.SelectTargets(world.Targets[id], state.Allies,
                    state.Enemies[0], state.BattleRandomState);
                Assert.That(a.Succeeded, Is.True); Assert.That(a.TargetInstanceIds, Is.EqualTo(b.TargetInstanceIds));
                Assert.That(a.NextBattleRandomState, Is.EqualTo(b.NextBattleRandomState));
                Assert.That(a.TargetInstanceIds.Count, Is.LessThanOrEqualTo(2));
                Assert.That(new HashSet<string>(a.TargetInstanceIds).Count, Is.EqualTo(a.TargetInstanceIds.Count));
            }

            World directWorld = null;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                World candidate = Create(new[] { new AllySpec("Sparse", 0, 0) },
                    new[] { new EnemySpec("CellEnemy", 5, 0, 100, targetIds: new[] { "T009" }) }, seed: seed);
                BattleTargetSelectionResult selection = BattleTargetRules.SelectTargets(
                    candidate.Targets["T009"], candidate.Battle.Allies, candidate.Battle.Enemies[0],
                    candidate.Battle.BattleRandomState);
                if (selection.TargetInstanceIds.Count == 0) { directWorld = candidate; break; }
            }
            Assert.That(directWorld, Is.Not.Null, "A deterministic seed should draw only empty T009 cells.");
            BattleState directState = Start(directWorld);
            directState = BattleTurnRules.RequestNext(directState).NextState; // RoundStarted
            BattleScheduleResult allyRequest = BattleTurnRules.RequestNext(directState);
            BattleAttackResult allyAction = BattleNormalAttackRules.ResolveAllyNormalAttack(
                allyRequest.NextState, allyRequest.ActionRequestId);
            directState = allyAction.NextState;
            BattleScheduleResult alliesDone = BattleTurnRules.RequestNext(directState);
            BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(alliesDone.NextState);
            directState = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            BattleScheduleResult enemyRequest = BattleTurnRules.RequestNext(directState);
            BattleAttackResult directHit = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                enemyRequest.NextState, enemyRequest.ActionRequestId, directWorld.Targets);
            Assert.That(directHit.Succeeded, Is.True, directHit.Failure.ToString());
            Assert.That(directHit.TargetInstanceIds, Is.Empty);
            Assert.That(directHit.ActualPlayerHpLost, Is.EqualTo(5));
        }

        [Test]
        public void ScenarioH_BattleCleanupKeepsPermanentCharacterBonusAndResetsBothBlockBonuses()
        {
            World world = OneAllyOneEnemy(enemyHp: 100, maxRounds: 1);
            CharacterInstanceView view; Assert.That(world.Ownership.TryGet("C1", out view), Is.True);
            HandState hand = world.Hand.TryWithSlot(0, HandSlotContent.Item("perma"), out HandState first) ? first : world.Hand;
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Item("temp"), out HandState second), Is.True);
            Assert.That(hand.TryGetSlot(0, out HandSlotContent firstItem), Is.True);
            Assert.That(firstItem.ItemDefinitionId, Is.EqualTo("perma"));
            ItemEffectState effects = new ItemEffectState();
            StageProgressState stage = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(StageConfig()), StageConfig()).NextState;
            ItemDefinition permanent = new ItemDefinition("perma", "Permanent", 0, "",
                ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 2) });
            ItemDefinition temporary = new ItemDefinition("temp", "Temporary", 0, "",
                ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 4) });
            ItemEffectResult p = ItemEffectRules.TryUseHandItem(permanent, 0,
                ItemUseTarget.BoardCharacter(0), world.Board, second, world.Ownership, world.Economy,
                stage, effects, world.Definitions, world.Stats, Rng());
            Assert.That(p.Succeeded, Is.True, "permanent item: " + p.Failure);
            ItemEffectResult t = ItemEffectRules.TryUseHandItem(temporary, 1,
                ItemUseTarget.BoardCharacter(0), p.NextBoard, p.NextHand, p.NextOwnership, p.NextEconomy,
                stage, p.NextEffectState, world.Definitions, world.Stats, Rng());
            Assert.That(t.Succeeded, Is.True, "temporary item: " + t.Failure);
            BattleInitializationResult initialized = BattleInitializationRules.Initialize(t.NextEconomy,
                StageProgressRules.BeginBattle(stage).NextState, t.NextBoard, t.NextHand, t.NextOwnership,
                world.Definitions, EnemyBoardState.CreateEmpty(), Array.Empty<EnemyDefinition>(), t.NextEffectState,
                world.Definitions, world.Definitions, Rng(), new BattleConfig(1));
            Assert.That(initialized.Succeeded, Is.True, initialized.Failure.ToString());
            BattleFinishResult finish = BattleFinishRules.TryFinish(initialized.State, t.NextEconomy,
                t.NextBoard, t.NextHand, t.NextOwnership, t.NextEffectState);
            Assert.That(finish.Succeeded, Is.True);
            Assert.That(finish.NextOwnership.TryGet("C1", out CharacterInstanceView cleaned), Is.True);
            Assert.That(cleaned.Attack, Is.EqualTo(view.Attack + 2));
            Assert.That(cleaned.TemporaryAttackBonus, Is.Zero);
            Assert.That(finish.NextItemEffectState.PermanentPlayerBlockBonus, Is.Zero);
            Assert.That(finish.NextItemEffectState.TemporaryPlayerBlockBonus, Is.Zero);
            Assert.That(BattleFinishRules.TryFinish(finish.NextBattleState, finish.NextEconomyState,
                finish.NextBoard, finish.NextHand, finish.NextOwnership, finish.NextItemEffectState).Succeeded, Is.False);
        }

        [Test]
        public void ScenarioI_MaxRoundProducesUnresolvedAfterLastRoundProcessing()
        {
            RunResult result = Run(OneAllyOneEnemy(allyAttack: 0, enemyAttack: 0,
                enemyDefense: 100, enemyHp: 10, maxRounds: 1));
            Assert.That(result.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(result.Finish.Result.Reason, Is.EqualTo(BattleFinishReason.MaximumRoundsReached));
            Assert.That(result.Finish.Result.FinalRound, Is.EqualTo(1));
        }

        [Test]
        public void ScenarioJ_BadActionAndEffectAreAtomicAndValidReplayIsRejected()
        {
            World world = OneAllyOneEnemy(allyAttack: 0, enemyHp: 100);
            BattleState state = Start(world);
            BattleScheduleResult round = BattleTurnRules.RequestNext(state);
            BattleScheduleResult request = BattleTurnRules.RequestNext(round.NextState);
            BattleAttackResult invalid = BattleNormalAttackRules.ResolveAllyNormalAttack(
                request.NextState, "stale-request");
            Assert.That(invalid.Succeeded, Is.False); Assert.That(invalid.NextState, Is.SameAs(request.NextState));
            Assert.That(invalid.NextBattleRandomState, Is.EqualTo(request.NextState.BattleRandomState));
            BattleAttackResult action = BattleNormalAttackRules.ResolveAllyNormalAttack(
                request.NextState, request.ActionRequestId);
            Assert.That(action.Succeeded, Is.True);
            BattleAttackResult twice = BattleNormalAttackRules.ResolveAllyNormalAttack(
                action.NextState, request.ActionRequestId);
            Assert.That(twice.Succeeded, Is.False);
            Assert.That(twice.NextState, Is.SameAs(action.NextState));

            World invalidTargetWorld = OneAllyOneEnemy(allyAttack: 0, enemyHp: 100, targetIds: new[] { "UNKNOWN" });
            BattleState targetState = Start(invalidTargetWorld);
            targetState = BattleTurnRules.RequestNext(targetState).NextState;
            BattleScheduleResult targetAlly = BattleTurnRules.RequestNext(targetState);
            BattleAttackResult targetAllyAction = BattleNormalAttackRules.ResolveAllyNormalAttack(
                targetAlly.NextState, targetAlly.ActionRequestId);
            BattleScheduleResult targetAlliesDone = BattleTurnRules.RequestNext(targetAllyAction.NextState);
            BattleScheduleResult targetEnemyStart = BattleTurnRules.RequestNext(targetAlliesDone.NextState);
            targetState = BattleTurnRules.ResolveAction(targetEnemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(targetEnemyStart.ActionRequestId)).NextState;
            BattleScheduleResult targetRequest = BattleTurnRules.RequestNext(targetState);
            Pcg32State targetCheckpoint = targetRequest.NextState.BattleRandomState;
            BattleAttackResult invalidTarget = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                targetRequest.NextState, targetRequest.ActionRequestId, invalidTargetWorld.Targets);
            Assert.That(invalidTarget.Succeeded, Is.False);
            Assert.That(invalidTarget.NextState, Is.SameAs(targetRequest.NextState));
            Assert.That(invalidTarget.NextBattleRandomState, Is.EqualTo(targetCheckpoint));

            World badEffectWorld = OneAllyOneEnemy(cip: new[] { "MISSING" });
            BattleScheduleResult startRequest = BattleTurnRules.RequestNext(badEffectWorld.Battle);
            BattleState waiting = BattleTurnRules.ResolveAction(startRequest.NextState,
                BattleActionResolution.BoundaryCompleted(startRequest.ActionRequestId)).NextState;
            Pcg32State beforeEffect = waiting.BattleRandomState;
            BattleEffectResolutionResult badEffect = BattleEffectRules.ResolveNextPendingTrigger(
                waiting, badEffectWorld.Economy, waiting.PendingEffectTriggers[0].TriggerId,
                new Dictionary<string, BattleEffectDefinition>());
            Assert.That(badEffect.Succeeded, Is.False);
            Assert.That(badEffect.NextState, Is.SameAs(waiting));
            Assert.That(badEffect.NextEconomyState, Is.SameAs(badEffectWorld.Economy));
            Assert.That(badEffect.NextBattleRandomState, Is.EqualTo(beforeEffect));
        }

        [Test]
        public void ScenarioK_LevelThreeRejectsFourBoardCharactersAndAllowsThree()
        {
            World four = Create(new[] { new AllySpec("A"), new AllySpec("B"), new AllySpec("C"), new AllySpec("D") },
                new[] { new EnemySpec("E") }, level: 4);
            StageProgressState stage = StageProgressRules.BeginBattle(StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(StageConfig()), StageConfig()).NextState).NextState;
            EconomyState levelThree = four.Economy.WithGold(four.Economy.Gold);
            levelThree = new EconomyState(levelThree.Gold, levelThree.XP, 3, 12, 10, 0, 3, 20, 30);
            Assert.That(BattleStartRules.Validate(levelThree, stage, four.Board, four.Hand,
                four.Ownership), Is.EqualTo(BattleStartFailure.BoardExceedsLevel));
            World three = Create(new[] { new AllySpec("A"), new AllySpec("B"), new AllySpec("C") },
                new[] { new EnemySpec("E") }, level: 3);
            Assert.That(BattleStartRules.Validate(three.Economy, stage, three.Board, three.Hand,
                three.Ownership), Is.EqualTo(BattleStartFailure.None));
            Assert.That(four.Board.SlotCount, Is.EqualTo(9));
        }

        [Test]
        public void ScenarioL_InitialBoundaryVictoryDefeatAndHpZeroEnemyAreDistinct()
        {
            World victory = Create(Array.Empty<AllySpec>(), Array.Empty<EnemySpec>());
            Assert.That(BattleFinishRules.TryFinish(victory.Battle, victory.Economy, victory.Board,
                victory.Hand, victory.Ownership, victory.ItemEffects).Result.Outcome, Is.EqualTo(BattleOutcome.Victory));
            RunResult defeat = Run(OneAllyOneEnemy(allyAttack: 0, enemyAttack: 20,
                enemyHp: 50, playerHp: 5, maxRounds: 6));
            Assert.That(defeat.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Defeat));
            World hpZeroEnemy = Create(Array.Empty<AllySpec>(), new[] { new EnemySpec("E", hp: 0) });
            Assert.That(hpZeroEnemy.Battle.Enemies, Is.Empty);
            Assert.That(BattleFinishRules.TryFinish(hpZeroEnemy.Battle, hpZeroEnemy.Economy,
                hpZeroEnemy.Board, hpZeroEnemy.Hand, hpZeroEnemy.Ownership, hpZeroEnemy.ItemEffects).Result.Outcome,
                Is.EqualTo(BattleOutcome.Victory));
            World waiting = OneAllyOneEnemy(cip: new[] { "CIP" });
            BattleScheduleResult start = BattleTurnRules.RequestNext(waiting.Battle);
            BattleState pendingCip = BattleTurnRules.ResolveAction(start.NextState,
                BattleActionResolution.BoundaryCompleted(start.ActionRequestId)).NextState;
            Assert.That(BattleFinishRules.TryFinish(pendingCip, waiting.Economy, waiting.Board,
                waiting.Hand, waiting.Ownership, waiting.ItemEffects).Failure,
                Is.EqualTo(BattleFinishFailure.PendingEffects));
        }

        [Test]
        public void ScenarioM_EnemyGapCooldownAndSlotOrderAreAppliedByScheduler()
        {
            World world = Create(new[] { new AllySpec("A", 0, 0) }, new[] {
                new EnemySpec("E0", 0, 100, 100, gap: 0, cooldown: 2),
                new EnemySpec("E1", 0, 100, 100, gap: 1, cooldown: 0) }, maxRounds: 4);
            RunResult result = Run(world);
            Assert.That(result.EnemyAttacks, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.EnemyWaits, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.Finish.Result.Outcome, Is.EqualTo(BattleOutcome.Unresolved));
            Assert.That(result.ActionIds.Count, Is.EqualTo(new HashSet<string>(result.ActionIds).Count));
            Assert.That(result.EnemyActionOrder[0], Is.EqualTo("1:0"));
            Assert.That(result.EnemyActionOrder[1], Is.EqualTo("2:1"));
        }

        [Test]
        public void ScenarioN_AeGoldChangesAccumulateAndFinishDoesNotApplyThemAgain()
        {
            World world = OneAllyOneEnemy(allyAttack: 0, enemyAttack: 0, enemyHp: 50,
                maxRounds: 2, ae: new[] { "AE" });
            world.Effects["AE"] = new BattleEffectDefinition("AE", BattleEffectType.Gold, -1,
                BattleEffectTarget.Player, BattleEffectDuration.Immediate);
            RunResult result = Run(world);
            Assert.That(result.EnemyAttacks, Is.EqualTo(2));
            Assert.That(result.Economy.Gold, Is.EqualTo(8));
            Assert.That(result.Finish.NextEconomyState.Gold, Is.EqualTo(8));
            world.Economy = world.Economy.WithGold(0);
            RunResult lowerBound = Run(world);
            Assert.That(lowerBound.Economy.Gold, Is.Zero);
        }

        [Test]
        public void ScenarioO_StunnedUnitMoveMemberIsSkippedAsActorAndParticipationIsBounded()
        {
            World world = Create(new[] { new AllySpec("A", 1, 0, units: new[] { "Pair" }),
                    new AllySpec("B", 1, 0, units: new[] { "Pair" }) },
                new[] { new EnemySpec("E", 0, 0, 100, ae: new[] { "STUN" }, targetIds: new[] { "TBACK" }) }, maxRounds: 2);
            world.Effects["STUN"] = new BattleEffectDefinition("STUN", BattleEffectType.Gap, 1,
                BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle);
            BattleState state = Start(world);
            BattleScheduleResult round = BattleTurnRules.RequestNext(state); state = round.NextState;
            BattleScheduleResult ally = BattleTurnRules.RequestNext(state);
            BattleUnitMoveDecision moveDecision = BattleUnitMoveRules.DecideCurrentAllyAction(
                ally.NextState, ally.ActionRequestId);
            Assert.That(moveDecision.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.MoveAvailable));
            BattleAttackResult move = BattleUnitMoveRules.ResolveUnitMoveAttack(ally.NextState,
                ally.ActionRequestId, moveDecision.SelectedMove.MoveId);
            Assert.That(move.Succeeded, Is.True);
            state = move.NextState;
            BattleScheduleResult moveActorSkip = BattleTurnRules.RequestNext(state);
            Assert.That(moveActorSkip.Event, Is.EqualTo(BattleScheduleEvent.AllySkippedByParticipationLimit));
            BattleScheduleResult participantSkip = BattleTurnRules.RequestNext(moveActorSkip.NextState);
            Assert.That(participantSkip.Event, Is.EqualTo(BattleScheduleEvent.AllySkippedByParticipationLimit));
            state = participantSkip.NextState;
            BattleScheduleResult allyDone = BattleTurnRules.RequestNext(state);
            BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(allyDone.NextState);
            state = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            BattleScheduleResult enemy = BattleTurnRules.RequestNext(state);
            BattleAttackResult enemyHit = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                enemy.NextState, enemy.ActionRequestId, world.Targets);
            state = ResolveEffects(enemyHit.NextState, ref world.Economy, world.Effects);
            Assert.That(state.Allies[0].StunRounds, Is.EqualTo(1));
            Assert.That(state.Allies[1].StunRounds, Is.Zero);
            Assert.That(state.PendingEffectTriggers, Is.Empty);
            Assert.That(state.BattleRandomState, Is.EqualTo(enemyHit.NextBattleRandomState));
            BattleScheduleResult enemyDone = BattleTurnRules.RequestNext(state);
            Assert.That(enemyDone.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnCompleted));
            BattleScheduleResult roundDone = BattleTurnRules.RequestNext(enemyDone.NextState);
            Assert.That(roundDone.Event, Is.EqualTo(BattleScheduleEvent.RoundCompleted));
            BattleScheduleResult nextRound = BattleTurnRules.RequestNext(roundDone.NextState);
            Assert.That(nextRound.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
            BattleScheduleResult stunnedFirst = BattleTurnRules.RequestNext(nextRound.NextState);
            Assert.That(stunnedFirst.Event, Is.EqualTo(BattleScheduleEvent.AllySkippedByStun));
            Assert.That(stunnedFirst.NextState.Allies[0].StunRounds, Is.Zero);
            BattleScheduleResult otherActor = BattleTurnRules.RequestNext(stunnedFirst.NextState);
            Assert.That(otherActor.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
            BattleUnitMoveDecision moveAfterStun = BattleUnitMoveRules.DecideCurrentAllyAction(
                otherActor.NextState, otherActor.ActionRequestId);
            Assert.That(moveAfterStun.Kind, Is.EqualTo(BattleUnitMoveDecisionKind.MoveAvailable));
            Assert.That(moveAfterStun.SelectedMove.ParticipantInstanceIds, Does.Contain(state.Allies[0].InstanceId));
        }
    }
}

