using System;
using System.Collections.Generic;
using NUnit.Framework;
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
    public sealed class EnemyIntegrationScenarioTests
    {
        private static StageProgressConfig Config() => new StageProgressConfig(
            25, 5, 5, 4, 100, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static EnemyDefinition Def(string id, int atk = 5, int def = 3, int hp = 8) =>
            new EnemyDefinition(id, id, atk, def, hp, null, null, null, "T001", null, null, null);

        private static EnemyFormationDefinition Formation(string id, params EnemyFormationSlotDefinition[] slots) =>
            new EnemyFormationDefinition(id, slots);

        private static EnemyFormationSlotDefinition[] EmptySlots()
        {
            var slots = new EnemyFormationSlotDefinition[6];
            for (int i = 0; i < 6; i++) slots[i] = new EnemyFormationSlotDefinition(null, 0, 0);
            return slots;
        }

        private static EnemyFormationDefinition At(string id, string enemyId, int slot, int gap = 0, int cd = 0)
        {
            var slots = EmptySlots();
            slots[slot] = new EnemyFormationSlotDefinition(enemyId, gap, cd);
            return Formation(id, slots);
        }

        private static StageProgressState Prep(int index, StageChoice choice = StageChoice.SelectA)
        {
            StageProgressConfig config = Config();
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            for (int i = 0; i < index; i++)
            {
                state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
                if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                    state = StageSelectionRules.Select(state, config, StageChoice.SelectA).NextState;
                state = StageProgressRules.BeginBattle(state).NextState;
                state = StageProgressRules.ResolveBattle(state, config, true).NextState;
                state = StageProgressRules.CompletePendingReward(state, config,
                    new Pcg32RandomSource((ulong)i + 5, 17).CaptureState()).NextState;
                if (state.Phase == StageProgressPhase.EventPending)
                    state = StageProgressRules.CompleteEvent(state, false).NextState;
            }
            state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                state = StageSelectionRules.Select(state, config, choice).NextState;
            return state;
        }

        private static EncounterPoolDefinition[] Pools() => new[]
        {
            new EncounterPoolDefinition("N", new[] { 0 }, StageEnemyCategory.Normal, new[] { At("N-A", "E1", 0, 1, 2) }),
            new EncounterPoolDefinition("H", new[] { 4 }, StageEnemyCategory.Hard, new[] { At("H-C", "E1", 2, 3, 4) }),
            new EncounterPoolDefinition("B", new[] { 3 }, StageEnemyCategory.Boss, new[] { At("B-4", "E1", 4) })
        };

        private static ItemDefinition Item(string id, ItemEffectTarget target, ItemEffectKind kind, int value) =>
            new ItemDefinition(id, id, 0, string.Empty, target, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(kind, value) });

        private static HandState Hand(string itemId)
        {
            HandState empty = HandState.CreateEmpty(5);
            Assert.That(empty.TryWithSlot(0, HandSlotContent.Item(itemId), out HandState result), Is.True);
            return result;
        }

        private static EnemyItemUseResult Use(ItemDefinition item, EnemyFormationDefinition formation,
            EnemyPreparationState prep, HandState hand, Pcg32State stats, ItemEffectState effectState = null)
        {
            var economy = new EconomyState(0, 0, 1, 12, 0, 0, 1, 20, 30);
            return EnemyItemEffectRules.TryUseHandItem(item, 0, formation, prep,
                BoardState.CreateStandard(), hand,
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), economy,
                Prep(0), effectState ?? new ItemEffectState(), null, null, stats);
        }

        private static void Same(Pcg32State a, Pcg32State b)
        {
            Assert.That(b.AlgorithmVersion, Is.EqualTo(a.AlgorithmVersion));
            Assert.That(b.State, Is.EqualTo(a.State));
            Assert.That(b.Increment, Is.EqualTo(a.Increment));
        }

        [Test]
        public void ScenarioA_SelectASelectsNormalAndCreatesFormationInSlotOrder()
        {
            StageProgressState stage = Prep(0);
            Pcg32State rng = new Pcg32RandomSource(1001, 9).CaptureState();
            EnemyEncounterSetupResult result = EnemyEncounterSetupRules.Generate(stage, Pools(), new[] { Def("E1") }, new EnemyPreparationState(), rng);
            Assert.That(stage.CurrentSelection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Normal));
            Assert.That(result.Succeeded && result.HasEncounter, Is.True);
            Assert.That(result.EncounterResult.SelectedFormationId, Is.EqualTo("N-A"));
            Assert.That(result.Board.SlotCount, Is.EqualTo(6));
            Assert.That(result.Board.TryGetAt(0, out EnemyInstance enemy), Is.True);
            Assert.That(enemy.Gap, Is.EqualTo(1)); Assert.That(enemy.Cooldown, Is.EqualTo(2));
            Assert.That(result.Board.TryGetAt(1, out EnemyInstance empty), Is.True); Assert.That(empty, Is.Null);
            Assert.That(result.EncounterRandomConsumed, Is.True);
            Same(rng, new Pcg32RandomSource(1001, 9).CaptureState());
            Assert.That(result.NextEncounterRandomState.State, Is.Not.EqualTo(rng.State));
        }

        [Test]
        public void ScenarioB_SelectCUsesHardPoolWithoutDifficultyStatMultiplier()
        {
            EnemyEncounterSetupResult result = EnemyEncounterSetupRules.Generate(Prep(4, StageChoice.SelectC), Pools(),
                new[] { Def("E1", 7, 4, 11) }, new EnemyPreparationState(), new Pcg32RandomSource(2002, 10).CaptureState());
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.EncounterResult.Category, Is.EqualTo(StageEnemyCategory.Hard));
            Assert.That(result.EncounterResult.SelectedFormationId, Is.EqualTo("H-C"));
            Assert.That(result.Board.TryGetAt(2, out EnemyInstance enemy), Is.True);
            Assert.That(enemy.Attack, Is.EqualTo(7)); Assert.That(enemy.Defense, Is.EqualTo(4));
            Assert.That(enemy.MaxHealth, Is.EqualTo(11)); Assert.That(enemy.Gap, Is.EqualTo(3)); Assert.That(enemy.Cooldown, Is.EqualTo(4));
        }

        [Test]
        public void ScenarioC_AutomaticRoundFourUsesBossPoolWithoutBossStatBonus()
        {
            StageProgressState stage = Prep(3);
            Assert.That(stage.CurrentSelection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Boss));
            EnemyEncounterSetupResult result = EnemyEncounterSetupRules.Generate(stage, Pools(), new[] { Def("E1", 6, 2, 9) },
                new EnemyPreparationState(), new Pcg32RandomSource(3003, 11).CaptureState());
            Assert.That(result.Succeeded, Is.True); Assert.That(result.EncounterResult.Category, Is.EqualTo(StageEnemyCategory.Boss));
            Assert.That(result.EncounterResult.SelectedFormationId, Is.EqualTo("B-4"));
            Assert.That(result.Board.TryGetAt(4, out EnemyInstance boss), Is.True);
            Assert.That(boss.Attack, Is.EqualTo(6)); Assert.That(boss.Defense, Is.EqualTo(2)); Assert.That(boss.MaxHealth, Is.EqualTo(9));
        }

        [Test]
        public void ScenarioD_NoEnemyChoicesAreSuccessfulAndDifferentFromNoCandidatesError()
        {
            Pcg32State rng = new Pcg32RandomSource(4004, 12).CaptureState();
            foreach (StageChoice choice in new[] { StageChoice.SelectB, StageChoice.SelectD })
            {
                EnemyEncounterSetupResult result = EnemyEncounterSetupRules.Generate(Prep(4, choice), Pools(), null,
                    new EnemyPreparationState(), rng);
                Assert.That(result.Succeeded, Is.True, choice.ToString()); Assert.That(result.HasEncounter, Is.False);
                Assert.That(result.Board.SlotCount, Is.EqualTo(6)); Assert.That(result.EncounterRandomConsumed, Is.False);
                Same(rng, result.NextEncounterRandomState);
            }
            EnemyEncounterSetupResult missing = EnemyEncounterSetupRules.Generate(Prep(0), Array.Empty<EncounterPoolDefinition>(),
                new[] { Def("E1") }, new EnemyPreparationState(), rng);
            Assert.That(missing.Succeeded, Is.False); Assert.That(missing.EncounterResult.Failure, Is.EqualTo(EncounterGenerationFailure.NoCandidates));
            Assert.That(missing.Board, Is.Null); Same(rng, missing.NextEncounterRandomState);
        }

        [Test]
        public void ScenarioE_Item002ConsumesHandItemAndStacksDefenseModifiersOnlyOnOccupiedSlots()
        {
            EnemyFormationDefinition form = Formation("Two", new[] { new EnemyFormationSlotDefinition("E1", 0, 0), EmptySlots()[1],
                new EnemyFormationSlotDefinition("E1", 0, 0), EmptySlots()[3], EmptySlots()[4], EmptySlots()[5] });
            ItemDefinition item = Item("Item002", ItemEffectTarget.AllEnemies, ItemEffectKind.Defense, -1);
            Pcg32State stats = new Pcg32RandomSource(5005, 13).CaptureState();
            EnemyItemUseResult first = Use(item, form, new EnemyPreparationState(), Hand(item.DefinitionId), stats);
            Assert.That(first.Succeeded, Is.True); Assert.That(first.NextEnemyPreparationState.GetDefenseBonus(0), Is.EqualTo(-1));
            Assert.That(first.NextEnemyPreparationState.GetDefenseBonus(2), Is.EqualTo(-1)); Assert.That(first.NextEnemyPreparationState.GetDefenseBonus(1), Is.Zero);
            Assert.That(first.NextHand.TryGetSlot(0, out HandSlotContent used), Is.True); Assert.That(used.Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Same(stats, first.NextStatsRandomState);
            EnemyItemUseResult second = Use(item, form, first.NextEnemyPreparationState, Hand(item.DefinitionId), stats, first.NextItemEffectState);
            Assert.That(second.NextEnemyPreparationState.GetDefenseBonus(0), Is.EqualTo(-2));
            Assert.That(second.NextItemEffectState.EnemyCommands.Count, Is.EqualTo(2));
            EnemyPreparationState reset = second.NextEnemyPreparationState.ClearForNextStage();
            for (int i = 0; i < 6; i++) Assert.That(reset.GetDefenseBonus(i), Is.Zero);
        }

        [Test]
        public void ScenarioF_Item004DamageFloorsHealthAtZeroAndDoesNotRemoveEnemy()
        {
            EnemyFormationDefinition form = Formation("Two", new[] { new EnemyFormationSlotDefinition("E1", 0, 0), EmptySlots()[1],
                new EnemyFormationSlotDefinition("E1", 0, 0), EmptySlots()[3], EmptySlots()[4], EmptySlots()[5] });
            ItemDefinition item = Item("Item004", ItemEffectTarget.AllEnemies, ItemEffectKind.Damage, 2);
            EnemyPreparationState prep = new EnemyPreparationState(); ItemEffectState effectState = new ItemEffectState();
            Pcg32State stats = new Pcg32RandomSource(6006, 14).CaptureState();
            for (int i = 0; i < 4; i++)
            {
                EnemyItemUseResult used = Use(item, form, prep, Hand(item.DefinitionId), stats, effectState);
                Assert.That(used.Succeeded, Is.True); prep = used.NextEnemyPreparationState; effectState = used.NextItemEffectState;
            }
            Assert.That(effectState.EnemyCommands.Count, Is.EqualTo(4));
            EnemyFormationResult board = EnemyFormationRules.CreateBoard(form, new[] { Def("E1", hp: 5) }, 6, prep);
            Assert.That(board.Succeeded, Is.True); Assert.That(board.Board.TryGetAt(0, out EnemyInstance dead), Is.True);
            Assert.That(dead, Is.Not.Null); Assert.That(dead.MaxHealth, Is.Zero); Assert.That(dead.CurrentHealth, Is.Zero);
            Assert.That(board.Board.TryGetAt(2, out EnemyInstance stillStored), Is.True); Assert.That(stillStored, Is.Not.Null);
        }

        [Test]
        public void ScenarioG_RepeatedDefinitionCreatesDistinctIdsAndDuplicateInstanceIsRejected()
        {
            EnemyFormationDefinition form = Formation("Repeated", new[] { new EnemyFormationSlotDefinition("E1", 0, 0),
                new EnemyFormationSlotDefinition("E1", 1, 0), EmptySlots()[2], EmptySlots()[3],
                new EnemyFormationSlotDefinition("E1", 0, 1), EmptySlots()[5] });
            EnemyFormationResult result = EnemyFormationRules.CreateBoard(form, new[] { Def("E1") }, 20, new EnemyPreparationState());
            Assert.That(result.Succeeded, Is.True); result.Board.TryGetAt(0, out EnemyInstance a);
            result.Board.TryGetAt(1, out EnemyInstance b); result.Board.TryGetAt(4, out EnemyInstance c);
            Assert.That(a.DefinitionId, Is.EqualTo(b.DefinitionId));
            Assert.That(new HashSet<string> { a.InstanceId, b.InstanceId, c.InstanceId }.Count, Is.EqualTo(3));
            Assert.That(EnemyBoardState.TryCreate(2, 3, new[] { a, a }, out EnemyBoardState invalid, out EnemyBoardFailure failure), Is.False);
            Assert.That(invalid, Is.Null); Assert.That(failure, Is.EqualTo(EnemyBoardFailure.DuplicateInstanceId));
        }

        [Test]
        public void ScenarioH_NoCandidatesLeavesStagePreparationAndEncounterCheckpointUntouched()
        {
            StageProgressState stage = Prep(0); EnemyPreparationState prep = new EnemyPreparationState();
            Pcg32State rng = new Pcg32RandomSource(8008, 16).CaptureState();
            EnemyEncounterSetupResult result = EnemyEncounterSetupRules.Generate(stage, Array.Empty<EncounterPoolDefinition>(),
                new[] { Def("E1") }, prep, rng);
            Assert.That(result.Succeeded, Is.False); Assert.That(result.EncounterResult.Failure, Is.EqualTo(EncounterGenerationFailure.NoCandidates));
            Assert.That(result.Board, Is.Null); Assert.That(result.EncounterRandomConsumed, Is.False); Same(rng, result.NextEncounterRandomState);
            Assert.That(stage.Phase, Is.EqualTo(StageProgressPhase.Preparation)); Assert.That(stage.EncounterIndex, Is.Zero);
            Assert.That(prep.GetAttackBonus(0), Is.Zero);
        }

        [Test]
        public void ScenarioI_InvalidFormationDefinitionSlotAndItemFailuresDoNotCommitChanges()
        {
            EnemyFormationResult invalid = EnemyFormationRules.CreateBoard(null, new[] { Def("E1") }, 0);
            Assert.That(invalid.Succeeded, Is.False); Assert.That(invalid.Failure, Is.EqualTo(EnemyFormationFailure.InvalidInput));
            EnemyFormationResult missing = EnemyFormationRules.CreateBoard(At("Missing", "E404", 0), new[] { Def("E1") }, 0);
            Assert.That(missing.Succeeded, Is.False); Assert.That(missing.Board, Is.Null);
            Assert.That(EnemyInstanceFactory.TryCreate(Def("E1"), 0, 6, 0, 0, 0, 0, 0, 0,
                out EnemyInstance absent, out EnemyInstanceFailure slotFailure), Is.False);
            Assert.That(absent, Is.Null); Assert.That(slotFailure, Is.EqualTo(EnemyInstanceFailure.InvalidSlotIndex));

            EnemyFormationDefinition occupied = At("One", "E1", 0);
            EnemyFormationDefinition empty = Formation("Empty", EmptySlots());
            ItemDefinition item = Item("Item002", ItemEffectTarget.AllEnemies, ItemEffectKind.Defense, -1);
            HandState original = Hand(item.DefinitionId); EnemyPreparationState prep = new EnemyPreparationState();
            Pcg32State stats = new Pcg32RandomSource(9009, 17).CaptureState();
            EnemyItemUseResult noTarget = Use(item, empty, prep, original, stats);
            Assert.That(noTarget.Succeeded, Is.False); Assert.That(noTarget.Failure, Is.EqualTo(EnemyItemUseFailure.NoEnemyTargets));
            Assert.That(noTarget.NextHand, Is.SameAs(original)); Assert.That(noTarget.NextEnemyPreparationState, Is.SameAs(prep)); Same(stats, noTarget.NextStatsRandomState);
            HandState noItem = HandState.CreateEmpty(5);
            EnemyItemUseResult missingItem = Use(item, occupied, prep, noItem, stats);
            Assert.That(missingItem.Succeeded, Is.False); Assert.That(missingItem.ItemFailure, Is.EqualTo(ItemEffectFailure.ItemNotInHand));
            Assert.That(missingItem.NextHand, Is.SameAs(noItem)); Assert.That(missingItem.NextEnemyPreparationState, Is.SameAs(prep)); Same(stats, missingItem.NextStatsRandomState);

            ItemDefinition oneEnemy = Item("OneEnemy", ItemEffectTarget.OneEnemy, ItemEffectKind.Attack, 1);
            ItemEffectResult command = ItemEffectRules.TryUseHandItem(oneEnemy, 0, ItemUseTarget.EnemyCharacter(0, true),
                BoardState.CreateStandard(), Hand(oneEnemy.DefinitionId), new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                new EconomyState(0, 0, 1, 12, 0, 0, 1), Prep(0), new ItemEffectState(), null, null, stats);
            Assert.That(command.Succeeded, Is.True);
            EnemyPreparationResult rejected = prep.TryApplyAllEnemies(occupied, command.Commands);
            Assert.That(rejected.Succeeded, Is.False); Assert.That(rejected.Failure, Is.EqualTo(EnemyPreparationFailure.UnsupportedCommand));
            Assert.That(rejected.NextState, Is.SameAs(prep)); Same(stats, command.NextStatsRandomState);
        }

        [Test]
        public void ScenarioJ_SeededEncounterSelectionReplaysAndConsumesOnlyEncounterStream()
        {
            var pools = new[] { new EncounterPoolDefinition("Weighted", new[] { 0 }, StageEnemyCategory.Normal,
                new[] { At("F-A", "E1", 0), At("F-B", "E1", 1), At("F-C", "E1", 2) }) };
            StageProgressState stage = Prep(0); var firstStreams = new SimulationRandomStreams(123456UL);
            var secondStreams = new SimulationRandomStreams(123456UL); SimulationRandomStreamsState before = firstStreams.CaptureState();
            EnemyEncounterSetupResult first = EnemyEncounterSetupRules.Generate(stage, pools, new[] { Def("E1") },
                new EnemyPreparationState(), firstStreams.Encounter.CaptureState());
            EnemyEncounterSetupResult replay = EnemyEncounterSetupRules.Generate(stage, pools, new[] { Def("E1") },
                new EnemyPreparationState(), secondStreams.Encounter.CaptureState());
            Assert.That(first.Succeeded && replay.Succeeded, Is.True);
            Assert.That(first.EncounterResult.SelectedFormationId, Is.EqualTo(replay.EncounterResult.SelectedFormationId));
            Assert.That(first.Board.TryGetAt(0, out EnemyInstance first0), Is.True);
            Assert.That(replay.Board.TryGetAt(0, out EnemyInstance replay0), Is.True);
            Assert.That(first0?.InstanceId, Is.EqualTo(replay0?.InstanceId));
            Assert.That(first.EncounterResult.RandomConsumed, Is.True);
            Same(before.Shop, firstStreams.Shop.CaptureState()); Same(before.Stats, firstStreams.Stats.CaptureState());
            Same(before.Battle, firstStreams.Battle.CaptureState()); Same(before.Event, firstStreams.Event.CaptureState());
            Same(before.Roster, firstStreams.Roster.CaptureState()); Same(before.Economy, firstStreams.Economy.CaptureState());
            Same(before.Encounter, firstStreams.Encounter.CaptureState());

            foreach (ulong seed in new[] { 7UL, 999UL })
            {
                var expected = new Pcg32RandomSource(seed, 31); int index = expected.NextInt(0, 3);
                Pcg32State checkpoint = new Pcg32RandomSource(seed, 31).CaptureState();
                EnemyEncounterSetupResult result = EnemyEncounterSetupRules.Generate(stage, pools, new[] { Def("E1") },
                    new EnemyPreparationState(), checkpoint);
                Assert.That(result.EncounterResult.SelectedFormationId, Is.EqualTo("F-" + new[] { "A", "B", "C" }[index]));
                Same(expected.CaptureState(), result.NextEncounterRandomState);
            }

            EnemyFormationDefinition itemFormation = Formation("ItemReplay", new[] {
                new EnemyFormationSlotDefinition("E1", 0, 0), EmptySlots()[1],
                new EnemyFormationSlotDefinition("E1", 1, 0), EmptySlots()[3], EmptySlots()[4], EmptySlots()[5] });
            ItemDefinition defenseItem = Item("Item002", ItemEffectTarget.AllEnemies, ItemEffectKind.Defense, -1);
            Pcg32State itemStats = new Pcg32RandomSource(77, 33).CaptureState();
            EnemyItemUseResult useA = Use(defenseItem, itemFormation, new EnemyPreparationState(), Hand(defenseItem.DefinitionId), itemStats);
            EnemyItemUseResult useB = Use(defenseItem, itemFormation, new EnemyPreparationState(), Hand(defenseItem.DefinitionId), itemStats);
            EnemyFormationResult boardA = EnemyFormationRules.CreateBoard(itemFormation, new[] { Def("E1") }, 5, useA.NextEnemyPreparationState);
            EnemyFormationResult boardB = EnemyFormationRules.CreateBoard(itemFormation, new[] { Def("E1") }, 5, useB.NextEnemyPreparationState);
            Assert.That(boardA.Board.TryGetAt(0, out EnemyInstance a), Is.True);
            Assert.That(boardB.Board.TryGetAt(0, out EnemyInstance b), Is.True);
            Assert.That(a.InstanceId, Is.EqualTo(b.InstanceId)); Assert.That(a.Defense, Is.EqualTo(b.Defense));
            Assert.That(a.MaxHealth, Is.EqualTo(b.MaxHealth)); Same(itemStats, useA.NextStatsRandomState); Same(itemStats, useB.NextStatsRandomState);
        }
    }
}
