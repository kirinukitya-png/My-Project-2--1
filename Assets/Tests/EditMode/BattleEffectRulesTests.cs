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
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BattleEffectRulesTests
    {
        private static Pcg32State Rng(ulong seed = 41) => new Pcg32RandomSource(seed, 17).CaptureState();
        private static StageProgressConfig StageConfig() => new StageProgressConfig(
            25, 5, 5, 4, 0, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static StageProgressState Stage()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState stage = StageProgressRules.CreateInitialState(config);
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            return StageProgressRules.BeginBattle(stage).NextState;
        }

        private static CharacterDefinition Definition(string id, string[] units = null) =>
            new CharacterDefinition(id, id, 1, new[] { "A" }, units ?? Array.Empty<string>());

        private static CharacterInstance Character(CharacterDefinition definition, string instanceId, int attack = 5, int defense = 1)
        {
            var character = new CharacterInstance(instanceId, definition, 1, 1);
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1,
                new[] { new CharacterStatOption(attack, defense, 1) },
                new[] { new CharacterStatOption(attack, defense, 1) },
                new[] { new CharacterStatOption(attack, defense, 1) }) });
            Assert.That(character.EnsureStats(definition, stats, new Pcg32RandomSource(8, 3)), Is.True);
            return character;
        }

        private static EnemyDefinition EnemyDefinition(string id = "E", int attack = 1, int defense = 0,
            int hp = 10, string[] cip = null, string[] ae = null, string[] pig = null) =>
            new EnemyDefinition(id, id, attack, defense, hp, cip, ae, pig, "T001",
                new[] { "T001" }, null, null);

        private static BattleState Create(EnemyDefinition enemyDefinition, CharacterDefinition[] defs = null,
            CharacterInstance[] characters = null, (int slot, string id)[] placements = null,
            int enemyGap = 0, ulong seed = 41, int enemyCopies = 1,
            int playerHp = 20, int playerMaximumHp = 30)
        {
            defs ??= Array.Empty<CharacterDefinition>();
            characters ??= Array.Empty<CharacterInstance>();
            placements ??= Array.Empty<(int, string)>();
            BoardState board = BoardState.CreateStandard();
            foreach (var placement in placements)
            {
                Assert.That(board.TryWithCharacterAt(placement.slot, placement.id, out BoardState next), Is.True);
                board = next;
            }
            var enemies = new List<EnemyInstance>();
            for (int slot = 0; slot < enemyCopies; slot++)
            {
                Assert.That(EnemyInstanceFactory.TryCreate(enemyDefinition, 0, slot, 0, 0, 0,
                    enemyGap, 0, 0, out EnemyInstance enemy, out EnemyInstanceFailure enemyFailure),
                    Is.True, enemyFailure.ToString());
                enemies.Add(enemy);
            }
            Assert.That(EnemyBoardState.TryCreate(2, 3, enemies, out EnemyBoardState enemyBoard,
                out EnemyBoardFailure boardFailure), Is.True, boardFailure.ToString());
            var initialized = BattleInitializationRules.Initialize(
                new EconomyState(5, 0, 3, 12, 5, 0, 1, playerHp, playerMaximumHp), Stage(), board,
                HandState.CreateStandard(), new CharacterOwnershipState(characters), defs,
                enemyBoard, new[] { enemyDefinition }, new ItemEffectState(), defs, defs,
                Rng(seed), BattleConfig.Default);
            Assert.That(initialized.Succeeded, Is.True, initialized.Failure.ToString());
            return initialized.State;
        }

        private static BattleState CompleteBattleStart(BattleState state)
        {
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.BattleStartRequired));
            BattleScheduleResult started = BattleTurnRules.ResolveAction(request.NextState,
                BattleActionResolution.BoundaryCompleted(request.ActionRequestId));
            Assert.That(started.Succeeded, Is.True);
            return started.NextState;
        }

        private static BattleState EnterPlayerTurn(BattleState state)
        {
            state = CompleteBattleStart(state);
            Assert.That(state.PendingEffectTriggers, Is.Empty);
            BattleScheduleResult round = BattleTurnRules.RequestNext(state);
            Assert.That(round.Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
            return round.NextState;
        }

        private static BattleState EnterFirstEnemyTurn(BattleState state)
        {
            state = EnterPlayerTurn(state);
            while (true)
            {
                BattleScheduleResult ally = BattleTurnRules.RequestNext(state);
                if (ally.Event == BattleScheduleEvent.AllyTurnCompleted)
                { state = ally.NextState; break; }
                Assert.That(ally.Event, Is.EqualTo(BattleScheduleEvent.AllyActionRequired));
                state = BattleTurnRules.ResolveAction(ally.NextState,
                    BattleActionResolution.ActionCompleted(ally.ActionRequestId)).NextState;
            }
            BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(state);
            Assert.That(enemyStart.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
            state = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            return state;
        }

        private static Dictionary<string, BattleEffectDefinition> Effects(params BattleEffectDefinition[] values)
        {
            var result = new Dictionary<string, BattleEffectDefinition>(StringComparer.Ordinal);
            foreach (BattleEffectDefinition value in values) result.Add(value.EffectId, value);
            return result;
        }

        [Test]
        public void CipRunsInEnemySlotAndEffectOrderBeforeRoundOneWithoutUsingRng()
        {
            EnemyDefinition def = EnemyDefinition(cip: new[] { "C1", "C2" });
            BattleState state = Create(def);
            state = CompleteBattleStart(state);
            Assert.That(state.PendingEffectTriggers.Count, Is.EqualTo(1));
            BattlePendingEffectTrigger trigger = state.PendingEffectTriggers[0];
            Assert.That(trigger.Kind, Is.EqualTo(BattleEffectTriggerKind.EnemyBattleStart));
            Assert.That(trigger.EffectIds, Is.EqualTo(new[] { "C1", "C2" }));
            Pcg32State before = state.BattleRandomState;

            BattleEffectResolutionResult result = BattleEffectRules.ResolveNextPendingTrigger(state,
                new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30), trigger.TriggerId,
                Effects(new BattleEffectDefinition("C1", BattleEffectType.Attack, 1,
                    BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle),
                    new BattleEffectDefinition("C2", BattleEffectType.Attack, 2,
                    BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)));

            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.NextState.Enemies[0].Attack, Is.EqualTo(def.Attack + 3));
            Assert.That(result.AppliedEffectIds, Is.EqualTo(new[] { "C1", "C2" }));
            Assert.That(result.NextBattleRandomState, Is.EqualTo(before));
            Assert.That(result.NextState.PendingEffectTriggers, Is.Empty);
            Assert.That(BattleTurnRules.RequestNext(result.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.RoundStarted));
            Assert.That(BattleEffectRules.ResolveNextPendingTrigger(result.NextState,
                result.NextEconomyState, trigger.TriggerId, Effects()).Failure,
                Is.EqualTo(BattleEffectFailure.AlreadyProcessed));
        }

        [Test]
        public void CipUnsetDoesNotCreatePendingTriggerOrConsumeBattleRng()
        {
            BattleState state = CompleteBattleStart(Create(EnemyDefinition()));
            Assert.That(state.PendingEffectTriggers, Is.Empty);
            Assert.That(state.BattleRandomState, Is.EqualTo(Rng()));
            Assert.That(BattleTurnRules.RequestNext(state).Event, Is.EqualTo(BattleScheduleEvent.RoundStarted));
        }

        [Test]
        public void CipQueueUsesEnemySlotOrderAndPreservesPerEnemyEffectOrder()
        {
            EnemyDefinition definition = EnemyDefinition(cip: new[] { "C1", "C2" });
            BattleState state = CompleteBattleStart(Create(definition, enemyCopies: 2));
            Assert.That(state.PendingEffectTriggers.Count, Is.EqualTo(2));
            for (int i = 0; i < state.Enemies.Count; i++)
            {
                Assert.That(state.PendingEffectTriggers[i].SourceInstanceId,
                    Is.EqualTo(state.Enemies[i].InstanceId));
                Assert.That(state.PendingEffectTriggers[i].EffectIds,
                    Is.EqualTo(new[] { "C1", "C2" }));
            }
        }

        [Test]
        public void AeRunsAfterZeroDamageAndBlocksSchedulingUntilResolved()
        {
            CharacterDefinition allyDefinition = Definition("C");
            CharacterInstance allyInstance = Character(allyDefinition, "CI", attack: 1, defense: 0);
            EnemyDefinition enemyDefinition = EnemyDefinition(attack: 0, defense: 0, hp: 10, ae: new[] { "AE004" });
            BattleState state = EnterFirstEnemyTurn(Create(enemyDefinition, new[] { allyDefinition },
                new[] { allyInstance }, new[] { (0, "CI") }));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            Assert.That(request.Event, Is.EqualTo(BattleScheduleEvent.EnemyActionRequired));
            var targets = new Dictionary<string, BattleTargetDefinition>
            {
                ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.Uniform, 1)
            };
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId, targets);
            Assert.That(attack.Succeeded, Is.True, attack.Failure.ToString());
            Assert.That(attack.CalculatedDamage, Is.EqualTo(0));
            Assert.That(attack.NextState.PendingEffectTriggers.Count, Is.EqualTo(1));
            Assert.That(BattleTurnRules.RequestNext(attack.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.PostActionEffectsRequired));

            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("AE004", BattleEffectType.Attack, 1,
                    BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(effect.NextState.Enemies[0].Attack, Is.EqualTo(1));
            Assert.That(effect.NextState.PendingEffectTriggers, Is.Empty);
            Assert.That(BattleTurnRules.RequestNext(attack.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.PostActionEffectsRequired));
        }

        [Test]
        public void AeStillRunsAfterPlayerHpReachesZeroAndBeforeOutcomeCheck()
        {
            CharacterDefinition allyDefinition = Definition("C");
            CharacterInstance ally = Character(allyDefinition, "CI", attack: 1, defense: 0);
            BattleState state = EnterFirstEnemyTurn(Create(
                EnemyDefinition(attack: 5, ae: new[] { "AE002" }), new[] { allyDefinition },
                new[] { ally }, new[] { (0, "CI") }, playerHp: 1, playerMaximumHp: 30));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId,
                new Dictionary<string, BattleTargetDefinition>
                { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All) });
            Assert.That(attack.Succeeded, Is.True);
            Assert.That(attack.NextState.PlayerCurrentHP, Is.Zero);
            Assert.That(attack.NextState.BattleOutcomeCheckPending, Is.True);
            Assert.That(BattleTurnRules.RequestNext(attack.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.PostActionEffectsRequired));
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 1, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("AE002", BattleEffectType.Gold, -1,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(effect.NextEconomyState.Gold, Is.EqualTo(4));
            Assert.That(BattleTurnRules.RequestNext(effect.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.BattleOutcomeCheckRequired));
        }

        [Test]
        public void AeAttackTargetsReuseTheOriginalSelectionAndApplyToEveryCapturedAlly()
        {
            CharacterDefinition a = Definition("A");
            CharacterDefinition b = Definition("B");
            EnemyDefinition enemyDefinition = EnemyDefinition(attack: 0, ae: new[] { "AE001" });
            BattleState state = EnterFirstEnemyTurn(Create(enemyDefinition,
                new[] { a, b }, new[] { Character(a, "IA", defense: 3), Character(b, "IB", defense: 2) },
                new[] { (0, "IA"), (1, "IB") }));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            var targets = new Dictionary<string, BattleTargetDefinition>
            {
                ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All)
            };
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId, targets);
            Assert.That(attack.Succeeded, Is.True);
            string[] captured = new List<string>(attack.TargetInstanceIds).ToArray();
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("AE001", BattleEffectType.Defense, -1,
                    BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(attack.TargetInstanceIds, Is.EqualTo(captured));
            foreach (string id in captured)
            {
                int idx = effect.NextState.Allies[0].InstanceId == id ? 0 : 1;
                Assert.That(effect.NextState.Allies[idx].Defense,
                    Is.EqualTo(state.Allies[idx].Defense - 1));
            }
        }

        [TestCase("AE003", BattleEffectType.Gap, 1)]
        [TestCase("AE005", BattleEffectType.Attack, -1)]
        [TestCase("AE008", BattleEffectType.Defense, -2)]
        [TestCase("AE009", BattleEffectType.Gap, 2)]
        public void AeAllyEffectsChangeOnlyTheirSpecifiedBattleStat(string id,
            BattleEffectType effectType, int value)
        {
            CharacterDefinition allyDefinition = Definition("A");
            CharacterInstance ally = Character(allyDefinition, "IA", attack: 5, defense: 4);
            BattleState state = EnterFirstEnemyTurn(Create(
                EnemyDefinition(attack: 0, ae: new[] { id }), new[] { allyDefinition },
                new[] { ally }, new[] { (0, "IA") }));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            var targetMap = new Dictionary<string, BattleTargetDefinition>
            { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                BattleTargetSelection.All) };
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId, targetMap);
            BattleEffectResolutionResult result = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition(id, effectType, value,
                    BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)));

            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.NextBattleRandomState, Is.EqualTo(attack.NextBattleRandomState));
            Assert.That(result.NextState.Allies[0].Attack,
                Is.EqualTo(attack.NextState.Allies[0].Attack + (effectType == BattleEffectType.Attack ? value : 0)));
            Assert.That(result.NextState.Allies[0].Defense,
                Is.EqualTo(attack.NextState.Allies[0].Defense + (effectType == BattleEffectType.Defense ? value : 0)));
            Assert.That(result.NextState.Allies[0].StunRounds,
                Is.EqualTo(effectType == BattleEffectType.Gap ? value : 0));
        }

        [Test]
        public void Ae006AddsDefenseToItsTriggerEnemyWithoutRemovingTemporaryDefenseReduction()
        {
            CharacterDefinition allyDefinition = Definition("A");
            CharacterInstance ally = Character(allyDefinition, "IA", attack: 1, defense: 0);
            EnemyDefinition enemyDefinition = EnemyDefinition(defense: 5, attack: 0, ae: new[] { "AE006" });
            BattleState state = EnterFirstEnemyTurn(Create(enemyDefinition,
                new[] { allyDefinition }, new[] { ally }, new[] { (0, "IA") }));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId,
                new Dictionary<string, BattleTargetDefinition>
                { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All) });
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("AE006", BattleEffectType.Defense, 2,
                    BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(effect.NextState.Enemies[0].Defense, Is.EqualTo(7));
            Assert.That(effect.NextState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
        }

        [Test]
        public void AttackAndDefenseEffectsHaveNoArtificialLowerBound()
        {
            CharacterDefinition allyDefinition = Definition("A");
            CharacterInstance ally = Character(allyDefinition, "IA", attack: 1, defense: 1);
            BattleState state = EnterFirstEnemyTurn(Create(
                EnemyDefinition(attack: 0, ae: new[] { "LOW" }), new[] { allyDefinition },
                new[] { ally }, new[] { (0, "IA") }));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId,
                new Dictionary<string, BattleTargetDefinition>
                { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All) });
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("LOW", BattleEffectType.Attack, -100,
                    BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(effect.NextState.Allies[0].Attack, Is.EqualTo(-99));
            Assert.That(effect.NextState.Allies[0].TemporaryAttackBonus, Is.EqualTo(-100));
        }

        [Test]
        public void CipCannotUseAttackTargetListBecauseLegacyCipHasNoAttackTargets()
        {
            BattleState state = CompleteBattleStart(Create(EnemyDefinition(cip: new[] { "BAD" })));
            BattleEffectResolutionResult result = BattleEffectRules.ResolveNextPendingTrigger(
                state, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                state.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("BAD", BattleEffectType.Attack, 1,
                    BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(BattleEffectFailure.InvalidDefinition));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [TestCase("AE002", -1, 0)]
        [TestCase("AE010", -3, 0)]
        public void AeGoldEffectsClampGoldAtZero(string id, int value, int expected)
        {
            BattleState state = EnterFirstEnemyTurn(Create(EnemyDefinition(ae: new[] { id })));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            var targets = new Dictionary<string, BattleTargetDefinition>
            {
                ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All)
            };
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId, targets);
            var lowGold = new EconomyState(1, 0, 3, 12, 5, 0, 1, 20, 30);
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, lowGold, attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition(id, BattleEffectType.Gold, value,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(effect.NextEconomyState.Gold, Is.EqualTo(expected));
        }

        [Test]
        public void AeBlockReductionClampsCurrentBlockAndTracksTemporaryBonus()
        {
            BattleState state = EnterFirstEnemyTurn(Create(EnemyDefinition(ae: new[] { "AE007" })));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId,
                new Dictionary<string, BattleTargetDefinition>
                { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All) });
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("AE007", BattleEffectType.Block, -100,
                    BattleEffectTarget.Player, BattleEffectDuration.Battle)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(effect.NextState.PlayerBlock, Is.Zero);
            Assert.That(effect.NextState.PendingPlayerBlockBonus, Is.EqualTo(-100));
        }

        [Test]
        public void EmptyAeAttackTargetSetIsAValidNoOpAndStillConsumesTheTrigger()
        {
            BattleState state = EnterFirstEnemyTurn(Create(EnemyDefinition(attack: 0, ae: new[] { "AE001" })));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId,
                new Dictionary<string, BattleTargetDefinition>
                { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All) });
            Assert.That(attack.Succeeded, Is.True);
            Assert.That(attack.TargetInstanceIds, Is.Empty);
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("AE001", BattleEffectType.Defense, -1,
                    BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(effect.NextState.Allies, Is.Empty);
            Assert.That(effect.NextState.PendingEffectTriggers, Is.Empty);
        }

        [Test]
        public void PigFromNormalAttackRunsBeforeLastEnemyOutcomeCheck()
        {
            CharacterDefinition allyDefinition = Definition("A");
            CharacterInstance ally = Character(allyDefinition, "IA", attack: 50);
            EnemyDefinition enemyDefinition = EnemyDefinition(hp: 1, pig: new[] { "PIG" });
            BattleState state = EnterPlayerTurn(Create(enemyDefinition, new[] { allyDefinition },
                new[] { ally }, new[] { (0, "IA") }));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveAllyNormalAttack(
                request.NextState, request.ActionRequestId);
            Assert.That(attack.Succeeded, Is.True);
            Assert.That(attack.NextState.Enemies[0].CurrentHP, Is.Zero);
            Assert.That(attack.NextState.BattleOutcomeCheckPending, Is.True);
            Assert.That(BattleTurnRules.RequestNext(attack.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.PostActionEffectsRequired));

            BattleEffectResolutionResult pig = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                attack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("PIG", BattleEffectType.Gold, -1,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate)));
            Assert.That(pig.Succeeded, Is.True);
            Assert.That(pig.NextEconomyState.Gold, Is.EqualTo(4));
            Assert.That(pig.NextState.Enemies[0].CurrentHP, Is.Zero);
            Assert.That(pig.NextState.PendingEffectTriggers, Is.Empty);
            Assert.That(BattleTurnRules.RequestNext(pig.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.BattleOutcomeCheckRequired));
        }

        [Test]
        public void PigFromUnitMoveAlsoCreatesPendingOutcomeCheck()
        {
            CharacterDefinition a = Definition("A", new[] { "Pair" });
            CharacterDefinition b = Definition("B", new[] { "Pair" });
            EnemyDefinition enemyDefinition = EnemyDefinition(defense: 0, hp: 1, pig: new[] { "PIG" });
            BattleState state = Create(enemyDefinition, new[] { a, b },
                new[] { Character(a, "IA", attack: 4), Character(b, "IB", attack: 4) },
                new[] { (0, "IA"), (1, "IB") });
            state = EnterPlayerTurn(state);
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            UnitMoveState move = BattleUnitMoveRules.GetAvailableMoves(request.NextState,
                request.ActorInstanceId)[0];
            BattleAttackResult attack = BattleUnitMoveRules.ResolveUnitMoveAttack(
                request.NextState, request.ActionRequestId, move.MoveId);
            Assert.That(attack.Succeeded, Is.True, attack.Failure.ToString());
            Assert.That(attack.DefeatedEnemyInstanceId, Is.Not.Null);
            Assert.That(attack.NextState.PendingEffectTriggers.Count, Is.EqualTo(1));
            Assert.That(attack.NextState.BattleOutcomeCheckPending, Is.True);
        }

        [Test]
        public void FailedLaterEffectRollsBackEarlierGoldChangeAndKeepsTrigger()
        {
            BattleState state = EnterFirstEnemyTurn(Create(EnemyDefinition(ae: new[] { "GOLD", "MISSING" })));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId,
                new Dictionary<string, BattleTargetDefinition>
                { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All) });
            EconomyState economy = new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30);
            BattlePendingEffectTrigger trigger = attack.NextState.PendingEffectTriggers[0];
            BattleEffectResolutionResult failed = BattleEffectRules.ResolveNextPendingTrigger(
                attack.NextState, economy, trigger.TriggerId,
                Effects(new BattleEffectDefinition("GOLD", BattleEffectType.Gold, -1,
                    BattleEffectTarget.Player, BattleEffectDuration.Immediate)));
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(failed.Failure, Is.EqualTo(BattleEffectFailure.MissingDefinition));
            Assert.That(failed.NextState, Is.SameAs(attack.NextState));
            Assert.That(failed.NextEconomyState, Is.SameAs(economy));
            Assert.That(failed.NextBattleRandomState, Is.EqualTo(attack.NextState.BattleRandomState));
            Assert.That(failed.NextState.PendingEffectTriggers[0].TriggerId, Is.EqualTo(trigger.TriggerId));
            Assert.That(BattleTurnRules.RequestNext(failed.NextState).Event,
                Is.EqualTo(BattleScheduleEvent.PostActionEffectsRequired));
        }

        [Test]
        public void WrongTriggerAndAlreadyProcessedTriggerAreRejectedWithoutMutation()
        {
            BattleState state = CompleteBattleStart(Create(EnemyDefinition(cip: new[] { "C" })));
            BattlePendingEffectTrigger trigger = state.PendingEffectTriggers[0];
            EconomyState economy = new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30);
            BattleEffectResolutionResult mismatch = BattleEffectRules.ResolveNextPendingTrigger(
                state, economy, "other", Effects(new BattleEffectDefinition("C", BattleEffectType.Attack,
                    1, BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)));
            Assert.That(mismatch.Failure, Is.EqualTo(BattleEffectFailure.TriggerMismatch));
            Assert.That(mismatch.NextState, Is.SameAs(state));
            BattleEffectResolutionResult applied = BattleEffectRules.ResolveNextPendingTrigger(
                state, economy, trigger.TriggerId, Effects(new BattleEffectDefinition("C", BattleEffectType.Attack,
                    1, BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)));
            BattleEffectResolutionResult duplicate = BattleEffectRules.ResolveNextPendingTrigger(
                applied.NextState, applied.NextEconomyState, trigger.TriggerId,
                Effects(new BattleEffectDefinition("C", BattleEffectType.Attack, 1,
                    BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)));
            Assert.That(duplicate.Failure, Is.EqualTo(BattleEffectFailure.AlreadyProcessed));
            Assert.That(duplicate.NextState, Is.SameAs(applied.NextState));
        }

        [Test]
        public void BattleDurationAllyAttackTracksTemporaryBonusWhileImmediateIsPersistentCommand()
        {
            CharacterDefinition characterDefinition = Definition("A");
            CharacterInstance character = Character(characterDefinition, "IA");
            EnemyDefinition temporaryEnemy = EnemyDefinition(attack: 0, ae: new[] { "TMP" });
            BattleState state = EnterFirstEnemyTurn(Create(temporaryEnemy,
                new[] { characterDefinition }, new[] { character }, new[] { (0, "IA") }));
            BattleScheduleResult request = BattleTurnRules.RequestNext(state);
            var targetMap = new Dictionary<string, BattleTargetDefinition>
            { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                BattleTargetSelection.All) };
            BattleAttackResult enemyAttack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                request.NextState, request.ActionRequestId, targetMap);
            BattlePendingEffectTrigger trigger = enemyAttack.NextState.PendingEffectTriggers[0];
            EconomyState economy = new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30);
            BattleEffectResolutionResult temporary = BattleEffectRules.ResolveNextPendingTrigger(
                enemyAttack.NextState, economy, trigger.TriggerId,
                Effects(new BattleEffectDefinition("TMP", BattleEffectType.Attack, -2,
                    BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)));
            Assert.That(temporary.Succeeded, Is.True);
            Assert.That(temporary.NextState.Allies[0].TemporaryAttackBonus, Is.EqualTo(-2));
            Assert.That(temporary.NextState.Allies[0].Attack, Is.EqualTo(enemyAttack.NextState.Allies[0].Attack - 2));

            BattleState immediateState = EnterFirstEnemyTurn(Create(
                EnemyDefinition(attack: 0, ae: new[] { "PERM" }), new[] { characterDefinition },
                new[] { character }, new[] { (0, "IA") }));
            BattleScheduleResult immediateRequest = BattleTurnRules.RequestNext(immediateState);
            BattleAttackResult immediateAttack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                immediateRequest.NextState, immediateRequest.ActionRequestId, targetMap);
            BattleEffectResolutionResult immediate = BattleEffectRules.ResolveNextPendingTrigger(
                immediateAttack.NextState, economy, immediateAttack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("PERM", BattleEffectType.Defense, 2,
                    BattleEffectTarget.AttackTargets, BattleEffectDuration.Immediate)));
            Assert.That(immediate.Succeeded, Is.True);
            Assert.That(immediate.PersistentCommands.Count, Is.EqualTo(1));
            Assert.That(immediate.PersistentCommands[0].TargetInstanceId, Is.EqualTo("IA"));
        }

        [Test]
        public void PlayerHpDamageClampsAndDefersBattleOnlyRollbackAmount()
        {
            BattleState state = CompleteBattleStart(Create(EnemyDefinition(cip: new[] { "DMG" })));
            BattlePendingEffectTrigger trigger = state.PendingEffectTriggers[0];
            EconomyState economy = new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30);
            BattleEffectResolutionResult result = BattleEffectRules.ResolveNextPendingTrigger(
                state, economy, trigger.TriggerId,
                Effects(new BattleEffectDefinition("DMG", BattleEffectType.Damage, 50,
                    BattleEffectTarget.Player, BattleEffectDuration.Battle)));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.PlayerCurrentHP, Is.Zero);
            Assert.That(result.NextEconomyState.CurrentHP, Is.Zero);
            Assert.That(result.NextState.PendingPlayerHpDelta, Is.EqualTo(-20));
            Assert.That(result.NextState.BattleOutcomeCheckPending, Is.True);
        }

        [Test]
        public void EnemyDefenseEffectDoesNotEraseUnitMoveReductionTracking()
        {
            CharacterDefinition a = Definition("A", new[] { "Pair" });
            CharacterDefinition b = Definition("B", new[] { "Pair" });
            BattleState state = CompleteBattleStart(Create(EnemyDefinition(hp: 10, defense: 5,
                    cip: new[] { "DEF" }), new[] { a, b },
                new[] { Character(a, "IA"), Character(b, "IB") }, new[] { (0, "IA"), (1, "IB") }));
            BattlePendingEffectTrigger trigger = state.PendingEffectTriggers[0];
            BattleEffectResolutionResult result = BattleEffectRules.ResolveNextPendingTrigger(state,
                new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30), trigger.TriggerId,
                Effects(new BattleEffectDefinition("DEF", BattleEffectType.Defense, 2,
                    BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.Enemies[0].Defense, Is.EqualTo(7));
            Assert.That(result.NextState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
        }

        [Test]
        public void UnitMoveDefenseReductionRestoresBeforeEnemyAeDefenseBonus()
        {
            CharacterDefinition a = Definition("A", new[] { "Pair" });
            CharacterDefinition b = Definition("B", new[] { "Pair" });
            EnemyDefinition enemyDefinition = EnemyDefinition(attack: 0, defense: 5, hp: 100,
                ae: new[] { "AE006" });
            BattleState state = EnterPlayerTurn(Create(enemyDefinition, new[] { a, b },
                new[] { Character(a, "IA", attack: 0), Character(b, "IB", attack: 0) },
                new[] { (0, "IA"), (1, "IB") }));

            BattleScheduleResult firstAlly = BattleTurnRules.RequestNext(state);
            UnitMoveState move = BattleUnitMoveRules.GetAvailableMoves(firstAlly.NextState,
                firstAlly.ActorInstanceId)[0];
            BattleAttackResult moveAttack = BattleUnitMoveRules.ResolveUnitMoveAttack(
                firstAlly.NextState, firstAlly.ActionRequestId, move.MoveId);
            Assert.That(moveAttack.Succeeded, Is.True);
            Assert.That(moveAttack.NextState.Enemies[0].Defense, Is.EqualTo(3));
            Assert.That(moveAttack.NextState.Enemies[0].TemporaryDefenseReduction, Is.EqualTo(2));

            state = moveAttack.NextState;
            BattleScheduleResult allyEnd = BattleTurnRules.RequestNext(state);
            while (allyEnd.Event != BattleScheduleEvent.AllyTurnCompleted)
            {
                if (allyEnd.Event == BattleScheduleEvent.AllyActionRequired)
                    state = BattleTurnRules.ResolveAction(allyEnd.NextState,
                        BattleActionResolution.ActionCompleted(allyEnd.ActionRequestId)).NextState;
                else
                {
                    Assert.That(allyEnd.Event, Is.EqualTo(BattleScheduleEvent.AllySkippedByParticipationLimit));
                    state = allyEnd.NextState;
                }
                allyEnd = BattleTurnRules.RequestNext(state);
            }
            Assert.That(allyEnd.Event, Is.EqualTo(BattleScheduleEvent.AllyTurnCompleted));
            BattleScheduleResult enemyStart = BattleTurnRules.RequestNext(allyEnd.NextState);
            Assert.That(enemyStart.Event, Is.EqualTo(BattleScheduleEvent.EnemyTurnStartRequired));
            state = BattleTurnRules.ResolveAction(enemyStart.NextState,
                BattleActionResolution.BoundaryCompleted(enemyStart.ActionRequestId)).NextState;
            Assert.That(state.Enemies[0].Defense, Is.EqualTo(5));
            Assert.That(state.Enemies[0].TemporaryDefenseReduction, Is.Zero);

            BattleScheduleResult enemy = BattleTurnRules.RequestNext(state);
            BattleAttackResult enemyAttack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                enemy.NextState, enemy.ActionRequestId,
                new Dictionary<string, BattleTargetDefinition>
                { ["T001"] = new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard,
                    BattleTargetSelection.All) });
            Assert.That(enemyAttack.Succeeded, Is.True);
            BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                enemyAttack.NextState, new EconomyState(5, 0, 3, 12, 5, 0, 1, 20, 30),
                enemyAttack.NextState.PendingEffectTriggers[0].TriggerId,
                Effects(new BattleEffectDefinition("AE006", BattleEffectType.Defense, 2,
                    BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)));
            Assert.That(effect.Succeeded, Is.True);
            Assert.That(effect.NextState.Enemies[0].Defense, Is.EqualTo(7));
            Assert.That(effect.NextState.Enemies[0].TemporaryDefenseReduction, Is.Zero);
        }
    }
}
