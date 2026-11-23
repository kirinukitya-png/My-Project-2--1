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
    public sealed class EnemyFormationRulesTests
    {
        private static StageProgressConfig Config() => new StageProgressConfig(
            25, 5, 5, 4, 0, 0, 0, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static EnemyDefinition Definition(string id, int attack = 5, int defense = 3, int hp = 5) =>
            new EnemyDefinition(id, id, attack, defense, hp, null, null, null, "T001", null, null, null);

        private static EnemyFormationDefinition Formation(params EnemyFormationSlotDefinition[] slots) =>
            new EnemyFormationDefinition("F", slots);

        private static EnemyFormationSlotDefinition[] EmptySlots()
        {
            var slots = new EnemyFormationSlotDefinition[6];
            for (int i = 0; i < slots.Length; i++) slots[i] = new EnemyFormationSlotDefinition(null, 0, 0);
            return slots;
        }

        private static EnemyFormationDefinition TwoEnemies()
        {
            var slots = EmptySlots();
            slots[0] = new EnemyFormationSlotDefinition("E1", 2, 3);
            slots[3] = new EnemyFormationSlotDefinition("E1", 0, 0);
            return Formation(slots);
        }

        private static StageProgressState PreparationAt(int index, StageChoice choice = StageChoice.SelectA)
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
                    new Pcg32RandomSource((ulong)(i + 1), 2).CaptureState()).NextState;
                if (state.Phase == StageProgressPhase.EventPending)
                    state = StageProgressRules.CompleteEvent(state, false).NextState;
            }
            state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                state = StageSelectionRules.Select(state, config, choice).NextState;
            return state;
        }

        [Test]
        public void FormationCreatesSixSlotsWithExactStatsPositionAndDeterministicIds()
        {
            var slots = EmptySlots();
            slots[0] = new EnemyFormationSlotDefinition("E1", 2, 4);
            slots[2] = new EnemyFormationSlotDefinition("E2", 1, 2);
            slots[4] = new EnemyFormationSlotDefinition("E1", 0, 0);
            var preparation = new EnemyPreparationState(
                new[] { -7, 0, 1, 0, 0, 0 },
                new[] { -4, 0, 2, 0, 0, 0 },
                new[] { -9, 0, 0, 0, -2, 0 },
                new[] { -3, 0, 0, 0, 0, 0 });
            EnemyFormationDefinition formation = Formation(slots);
            EnemyDefinition e1 = Definition("E1", 5, 3, 5);
            EnemyDefinition e2 = Definition("E2", 8, 2, 4);

            EnemyFormationResult first = EnemyFormationRules.CreateBoard(formation,
                new[] { e1, e2 }, 12, preparation);
            EnemyFormationResult second = EnemyFormationRules.CreateBoard(formation,
                new[] { e1, e2 }, 12, preparation);
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.Board.Columns, Is.EqualTo(2));
            Assert.That(first.Board.Rows, Is.EqualTo(3));
            Assert.That(first.Board.SlotCount, Is.EqualTo(6));
            Assert.That(first.Board.TryGetAt(0, out EnemyInstance at0), Is.True);
            Assert.That(at0.DefinitionId, Is.EqualTo("E1"));
            Assert.That(at0.Attack, Is.EqualTo(-2));
            Assert.That(at0.Defense, Is.EqualTo(-1));
            Assert.That(at0.MaxHealth, Is.EqualTo(0));
            Assert.That(at0.CurrentHealth, Is.EqualTo(0));
            Assert.That(at0.Gap, Is.EqualTo(0));
            Assert.That(at0.Cooldown, Is.EqualTo(4));
            Assert.That(at0.InitialActionRound, Is.EqualTo(1));
            Assert.That(first.Board.TryGetAt(1, out EnemyInstance empty1), Is.True);
            Assert.That(empty1, Is.Null);
            Assert.That(first.Board.TryGetAt(2, out EnemyInstance at2), Is.True);
            Assert.That(at2.DefinitionId, Is.EqualTo("E2"));
            Assert.That(at2.Attack, Is.EqualTo(9));
            Assert.That(at2.Defense, Is.EqualTo(4));
            Assert.That(at2.MaxHealth, Is.EqualTo(4));
            Assert.That(at2.Gap, Is.EqualTo(1));
            Assert.That(at2.Cooldown, Is.EqualTo(2));
            Assert.That(first.Board.TryGetAt(4, out EnemyInstance at4), Is.True);
            Assert.That(at4.DefinitionId, Is.EqualTo("E1"));
            Assert.That(at4.InstanceId, Is.EqualTo("enemy:encounter:12:slot:4"));
            Assert.That(at0.InstanceId, Is.EqualTo("enemy:encounter:12:slot:0"));
            Assert.That(second.Board.TryGetAt(0, out EnemyInstance repeated), Is.True);
            Assert.That(repeated.InstanceId, Is.EqualTo(at0.InstanceId));
            Assert.That(repeated.Attack, Is.EqualTo(at0.Attack));
        }

        [Test]
        public void FormationRejectsMissingDefinitionsAndReturnsNoPartialBoard()
        {
            var slots = EmptySlots();
            slots[5] = new EnemyFormationSlotDefinition("Missing", 0, 0);
            EnemyFormationResult result = EnemyFormationRules.CreateBoard(Formation(slots),
                new[] { Definition("E1") }, 0);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(EnemyFormationFailure.MissingEnemyDefinition));
            Assert.That(result.Board, Is.Null);
        }

        [Test]
        public void PreparationStateCopiesCollectionsAndClearsAllSlotBonusesForNextStage()
        {
            int[] defense = { -1, -1, 2, 0, 0, 0 };
            var state = new EnemyPreparationState(null, defense, null, null);
            defense[0] = 100;
            Assert.That(state.GetDefenseBonus(0), Is.EqualTo(-1));
            Assert.That(state.GetDefenseBonus(2), Is.EqualTo(2));
            Assert.Throws<NotSupportedException>(() => ((IList<int>)state.DefenseBonuses)[0] = 99);
            Assert.Throws<ArgumentOutOfRangeException>(() => state.GetGapBonus(6));
            EnemyPreparationState cleared = state.ClearForNextStage();
            for (int i = 0; i < 6; i++)
            {
                Assert.That(cleared.GetAttackBonus(i), Is.Zero);
                Assert.That(cleared.GetDefenseBonus(i), Is.Zero);
                Assert.That(cleared.GetHealthBonus(i), Is.Zero);
                Assert.That(cleared.GetGapBonus(i), Is.Zero);
            }
        }

        [Test]
        public void Item002AppliesDefenseMinusOneToOccupiedSlotsAndConsumesItemAtomically()
        {
            EnemyFormationDefinition formation = TwoEnemies();
            ItemDefinition item = new ItemDefinition("Item002", "", 2, "",
                ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Defense, -1) });
            World world = World.WithItem(item.DefinitionId);
            Pcg32State random = world.Random;
            EnemyItemUseResult first = Use(item, formation, new EnemyPreparationState(), world, new ItemEffectState(), random);
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.NextEnemyPreparationState.GetDefenseBonus(0), Is.EqualTo(-1));
            Assert.That(first.NextEnemyPreparationState.GetDefenseBonus(3), Is.EqualTo(-1));
            Assert.That(first.NextEnemyPreparationState.GetDefenseBonus(1), Is.Zero);
            Assert.That(first.NextHand.TryGetSlot(0, out HandSlotContent consumed), Is.True);
            Assert.That(consumed.Kind, Is.EqualTo(HandSlotContentKind.Empty));
            Assert.That(first.NextItemEffectState.EnemyCommands.Count, Is.EqualTo(1));
            Assert.That(first.StatsRandomConsumed, Is.False);
            AssertStateEqual(random, first.NextStatsRandomState);

            World secondWorld = World.WithItem(item.DefinitionId, first.NextBoard, first.NextOwnership,
                first.NextEconomy, first.NextStage, first.NextHand);
            secondWorld = secondWorld.WithItemAt(0, item.DefinitionId);
            EnemyItemUseResult second = Use(item, formation, first.NextEnemyPreparationState,
                secondWorld, first.NextItemEffectState, random);
            Assert.That(second.Succeeded, Is.True);
            Assert.That(second.NextEnemyPreparationState.GetDefenseBonus(0), Is.EqualTo(-2));
            Assert.That(second.NextEnemyPreparationState.GetDefenseBonus(3), Is.EqualTo(-2));
        }

        [Test]
        public void Item004DamageAccumulatesClampsHpAtZeroAndKeepsZeroHpEnemyInBoard()
        {
            EnemyFormationDefinition formation = TwoEnemies();
            ItemDefinition item = new ItemDefinition("Item004", "", 3, "",
                ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Damage, 2) });
            World world = World.WithItem(item.DefinitionId);
            EnemyItemUseResult first = Use(item, formation, new EnemyPreparationState(), world,
                new ItemEffectState(), world.Random);
            World twiceWorld = World.WithItem(item.DefinitionId, first.NextBoard, first.NextOwnership,
                first.NextEconomy, first.NextStage, first.NextHand).WithItemAt(0, item.DefinitionId);
            EnemyItemUseResult second = Use(item, formation, first.NextEnemyPreparationState,
                twiceWorld, first.NextItemEffectState, world.Random);
            Assert.That(second.Succeeded, Is.True);
            Assert.That(second.NextEnemyPreparationState.GetHealthBonus(0), Is.EqualTo(-4));
            EnemyFormationResult created = EnemyFormationRules.CreateBoard(formation,
                new[] { Definition("E1", hp: 3) }, 2, second.NextEnemyPreparationState);
            Assert.That(created.Succeeded, Is.True);
            Assert.That(created.Board.TryGetAt(0, out EnemyInstance zeroHp), Is.True);
            Assert.That(zeroHp, Is.Not.Null);
            Assert.That(zeroHp.MaxHealth, Is.Zero);
            Assert.That(zeroHp.CurrentHealth, Is.Zero);
            Assert.That(created.Board.TryGetAt(3, out EnemyInstance secondZeroHp), Is.True);
            Assert.That(secondZeroHp, Is.Not.Null);
        }

        [Test]
        public void EnemyItemUseRejectsNoEnemiesAndOverflowWithoutConsumingHandOrChangingModifiers()
        {
            var emptyFormation = Formation(EmptySlots());
            ItemDefinition item = new ItemDefinition("DefUp", "", 0, "",
                ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Defense, 1) });
            World world = World.WithItem(item.DefinitionId);
            EnemyPreparationState initial = new EnemyPreparationState(null,
                new[] { int.MaxValue, 0, 0, 0, 0, 0 }, null, null);
            EnemyItemUseResult noEnemies = Use(item, emptyFormation, initial, world,
                new ItemEffectState(), world.Random);
            Assert.That(noEnemies.Succeeded, Is.False);
            Assert.That(noEnemies.Failure, Is.EqualTo(EnemyItemUseFailure.NoEnemyTargets));
            Assert.That(noEnemies.NextHand, Is.SameAs(world.Hand));
            Assert.That(noEnemies.NextEnemyPreparationState, Is.SameAs(initial));

            EnemyFormationDefinition occupied = TwoEnemies();
            EnemyItemUseResult overflow = Use(item, occupied, initial, world,
                world.Effects, world.Random);
            Assert.That(overflow.Succeeded, Is.False);
            Assert.That(overflow.Failure, Is.EqualTo(EnemyItemUseFailure.EnemyPreparationRejected));
            Assert.That(overflow.PreparationFailure, Is.EqualTo(EnemyPreparationFailure.ArithmeticOverflow));
            Assert.That(overflow.NextHand, Is.SameAs(world.Hand));
            Assert.That(overflow.NextItemEffectState, Is.SameAs(world.Effects));
            Assert.That(overflow.NextEnemyPreparationState, Is.SameAs(initial));
            AssertStateEqual(world.Random, overflow.NextStatsRandomState);
            Assert.That(overflow.ItemResult.Succeeded, Is.True, "The intermediate proposal is not committed when the combined result fails.");
        }

        [Test]
        public void EncounterSetupCombinesSelectionFormationAndNoEnemyState()
        {
            EnemyFormationDefinition normal = Formation(new[]
            {
                new EnemyFormationSlotDefinition("E1", 0, 0), EmptySlots()[1], EmptySlots()[2],
                EmptySlots()[3], EmptySlots()[4], EmptySlots()[5]
            });
            EnemyFormationDefinition hard = Formation(new[]
            {
                new EnemyFormationSlotDefinition("E1", 2, 1), EmptySlots()[1], EmptySlots()[2],
                EmptySlots()[3], EmptySlots()[4], EmptySlots()[5]
            });
            EnemyFormationDefinition boss = Formation(new[]
            {
                new EnemyFormationSlotDefinition("E1", 1, 2), EmptySlots()[1], EmptySlots()[2],
                EmptySlots()[3], EmptySlots()[4], EmptySlots()[5]
            });
            var pools = new[]
            {
                new EncounterPoolDefinition("N", new[] { 0 }, StageEnemyCategory.Normal, new[] { normal }),
                new EncounterPoolDefinition("H", new[] { 4 }, StageEnemyCategory.Hard, new[] { hard }),
                new EncounterPoolDefinition("B", new[] { 3 }, StageEnemyCategory.Boss, new[] { boss })
            };
            Pcg32State rng = new Pcg32RandomSource(8, 4).CaptureState();
            var definitions = new[] { Definition("E1") };
            EnemyEncounterSetupResult a = EnemyEncounterSetupRules.Generate(
                PreparationAt(0), pools, definitions, new EnemyPreparationState(), rng);
            EnemyEncounterSetupResult c = EnemyEncounterSetupRules.Generate(
                PreparationAt(4, StageChoice.SelectC), pools, definitions, new EnemyPreparationState(), rng);
            EnemyEncounterSetupResult bossResult = EnemyEncounterSetupRules.Generate(
                PreparationAt(3), pools, definitions, new EnemyPreparationState(), rng);
            Assert.That(a.Succeeded && a.HasEncounter, Is.True);
            Assert.That(a.Board.TryGetAt(0, out EnemyInstance aEnemy), Is.True);
            Assert.That(aEnemy.InitialActionRound, Is.EqualTo(1));
            Assert.That(c.EncounterResult.Category, Is.EqualTo(StageEnemyCategory.Hard));
            Assert.That(bossResult.EncounterResult.Category, Is.EqualTo(StageEnemyCategory.Boss));

            foreach (StageChoice noEnemyChoice in new[] { StageChoice.SelectB, StageChoice.SelectD })
            {
                EnemyEncounterSetupResult none = EnemyEncounterSetupRules.Generate(
                    PreparationAt(4, noEnemyChoice), pools, null, new EnemyPreparationState(), rng);
                Assert.That(none.Succeeded, Is.True);
                Assert.That(none.HasEncounter, Is.False);
                Assert.That(none.Board.SlotCount, Is.EqualTo(6));
                Assert.That(none.EncounterRandomConsumed, Is.False);
                AssertStateEqual(rng, none.NextEncounterRandomState);
            }

            var eventConfig = new StageProgressConfig(25, 5, 5, 4, 100, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState eventState = StageProgressRules.CreateInitialState(eventConfig);
            eventState = StageProgressRules.BeginCurrentEncounter(eventState, eventConfig).NextState;
            eventState = StageProgressRules.BeginBattle(eventState).NextState;
            eventState = StageProgressRules.ResolveBattle(eventState, eventConfig, true).NextState;
            eventState = StageProgressRules.CompletePendingReward(eventState, eventConfig, rng).NextState;
            Assert.That(eventState.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            EnemyEncounterSetupResult eventWithoutEnemies = EnemyEncounterSetupRules.Generate(
                eventState, null, null, new EnemyPreparationState(), rng);
            Assert.That(eventWithoutEnemies.Succeeded, Is.True);
            Assert.That(eventWithoutEnemies.HasEncounter, Is.False);
            Assert.That(eventWithoutEnemies.Board.SlotCount, Is.EqualTo(6));
            Assert.That(eventWithoutEnemies.EncounterRandomConsumed, Is.False);
            AssertStateEqual(rng, eventWithoutEnemies.NextEncounterRandomState);
        }

        [Test]
        public void EncounterSetupPropagatesNoCandidatesAndRollsBackRngWhenMaterializationFails()
        {
            Pcg32State rng = new Pcg32RandomSource(99, 10).CaptureState();
            EnemyEncounterSetupResult missingCandidates = EnemyEncounterSetupRules.Generate(
                PreparationAt(0), Array.Empty<EncounterPoolDefinition>(), new[] { Definition("E1") },
                new EnemyPreparationState(), rng);
            Assert.That(missingCandidates.Succeeded, Is.False);
            Assert.That(missingCandidates.EncounterResult.Failure, Is.EqualTo(EncounterGenerationFailure.NoCandidates));
            AssertStateEqual(rng, missingCandidates.NextEncounterRandomState);

            EnemyFormationDefinition bad = Formation(new[]
            {
                new EnemyFormationSlotDefinition("Missing", 0, 0), EmptySlots()[1], EmptySlots()[2],
                EmptySlots()[3], EmptySlots()[4], EmptySlots()[5]
            });
            var pool = new[] { new EncounterPoolDefinition("N", new[] { 0 }, StageEnemyCategory.Normal, new[] { bad }) };
            EnemyEncounterSetupResult failedFormation = EnemyEncounterSetupRules.Generate(
                PreparationAt(0), pool, new[] { Definition("E1") }, new EnemyPreparationState(), rng);
            Assert.That(failedFormation.Succeeded, Is.False);
            Assert.That(failedFormation.Failure, Is.EqualTo(EnemyEncounterSetupFailure.FormationGenerationFailed));
            Assert.That(failedFormation.EncounterResult.RandomConsumed, Is.True);
            Assert.That(failedFormation.EncounterRandomConsumed, Is.False);
            AssertStateEqual(rng, failedFormation.NextEncounterRandomState);
        }

        [Test]
        public void P026IsAValidOneFormationHardPoolAtIndexThreeButThatCombinationIsNotReachable()
        {
            // Saved shop.unity references P026. Its serialized rounds are {3}, category Hard,
            // and its single Formation is valid. Round index 3 is the configured Boss round,
            // so StageSelectionRules cannot produce a reachable Hard selection there.
            Assert.That(Config().GetRoundNumber(3), Is.EqualTo(4));
            StageProgressState preparation = PreparationAt(3);
            Assert.That(preparation.CurrentSelection.EnemyCategory, Is.EqualTo(StageEnemyCategory.Boss));
            Assert.That(preparation.CurrentSelection.EnemyCategory, Is.Not.EqualTo(StageEnemyCategory.Hard));
        }

        private static EnemyItemUseResult Use(ItemDefinition item, EnemyFormationDefinition formation,
            EnemyPreparationState preparation, World world, ItemEffectState effects, Pcg32State random) =>
            EnemyItemEffectRules.TryUseHandItem(item, 0, formation, preparation,
                world.Board, world.Hand, world.Ownership, world.Economy, world.Stage,
                effects, Array.Empty<CharacterDefinition>(), null, random);

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
            public EconomyState Economy;
            public StageProgressState Stage;
            public ItemEffectState Effects;
            public Pcg32State Random;

            public static World WithItem(string itemId, BoardState board = null,
                CharacterOwnershipState ownership = null, EconomyState economy = null,
                StageProgressState stage = null, HandState hand = null)
            {
                if (hand == null) hand = HandState.CreateEmpty(5);
                hand.TryWithSlot(0, HandSlotContent.Item(itemId), out HandState withItem);
                return new World
                {
                    Board = board ?? BoardState.CreateStandard(),
                    Hand = withItem,
                    Ownership = ownership ?? new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                    Economy = economy ?? new EconomyState(0, 0, 1, 12, 0, 0, 1),
                    Stage = stage ?? PreparationAt(0),
                    Effects = new ItemEffectState(),
                    Random = new Pcg32RandomSource(101, 7).CaptureState()
                };
            }

            public World WithItemAt(int slotIndex, string itemId)
            {
                Hand.TryWithSlot(slotIndex, HandSlotContent.Item(itemId), out HandState next);
                Hand = next;
                return this;
            }
        }
    }
}
