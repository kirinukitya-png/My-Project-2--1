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
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BattleInitializationTests
    {
        private static StageProgressConfig StageConfig(int choiceRound = 5, int total = 25) =>
            new StageProgressConfig(total, 5, choiceRound, 4, 0, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static StageProgressState BattleStage(int encounterIndex = 0,
            StageChoice choice = StageChoice.SelectA)
        {
            StageProgressConfig config = StageConfig();
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            for (int i = 0; i < encounterIndex; i++)
            {
                state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
                if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                    state = StageSelectionRules.Select(state, config, StageChoice.SelectA).NextState;
                state = StageProgressRules.BeginBattle(state).NextState;
                state = StageProgressRules.ResolveBattle(state, config, true).NextState;
                state = StageProgressRules.CompletePendingReward(state, config,
                    new Pcg32RandomSource(10, 20).CaptureState()).NextState;
                if (state.Phase == StageProgressPhase.EventPending)
                    state = StageProgressRules.CompleteEvent(state, false).NextState;
            }

            state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                state = StageSelectionRules.Select(state, config, choice).NextState;
            if (state.Phase == StageProgressPhase.Preparation &&
                state.CurrentSelection.EnemyCategory != StageEnemyCategory.None)
                state = StageProgressRules.BeginBattle(state).NextState;
            return state;
        }

        private static CharacterDefinition CharacterDef(string id, params string[] units) =>
            new CharacterDefinition(id, id, 1, new[] { "A" }, units);

        private static CharacterStatDefinition Stats() => new CharacterStatDefinition(new[]
        {
            new CharacterCostStats(1,
                new[] { new CharacterStatOption(7, 3, 1) },
                new[] { new CharacterStatOption(14, 6, 1) },
                new[] { new CharacterStatOption(21, 9, 1) })
        });

        private static CharacterInstance Instance(CharacterDefinition definition, string id,
            int attackBonus = 0, int defenseBonus = 0, int star = 1)
        {
            var instance = new CharacterInstance(id, definition, 1, star);
            Assert.That(instance.EnsureStats(definition, Stats(), new Pcg32RandomSource(3, 8)), Is.True);
            instance.TemporaryAttackBonus = attackBonus;
            instance.TemporaryDefenseBonus = defenseBonus;
            return instance;
        }

        private static BoardState Board(params (int Slot, string Id)[] placements)
        {
            BoardState board = BoardState.CreateStandard();
            foreach ((int slot, string id) in placements)
            {
                Assert.That(board.TryWithCharacterAt(slot, id, out BoardState next), Is.True);
                board = next;
            }
            return board;
        }

        private static EnemyDefinition EnemyDef(string id = "E1", int attack = 8,
            int defense = 4, int hp = 12) => new EnemyDefinition(id, id, attack, defense,
                hp, new[] { "CIP" }, new[] { "AE" }, new[] { "PIG" }, "T009", new[] { "T009", "T010" }, null, null);

        private static EnemyBoardState EnemyBoard(params EnemyInstance[] instances)
        {
            Assert.That(EnemyBoardState.TryCreate(2, 3, instances, out EnemyBoardState board,
                out EnemyBoardFailure failure), Is.True, failure.ToString());
            return board;
        }

        private static EnemyInstance Enemy(EnemyDefinition definition, int slot,
            int gap = 2, int cooldown = 3, int healthBonus = 0)
        {
            Assert.That(EnemyInstanceFactory.TryCreate(definition, 0, slot, 0, 0,
                healthBonus, gap, 0, cooldown, out EnemyInstance instance,
                out EnemyInstanceFailure failure), Is.True, failure.ToString());
            return instance;
        }

        private static EconomyState Player(int level = 3, int hp = 20) =>
            new EconomyState(10, 0, level, 12, 10, 0, 1, hp, 30);

        private static Pcg32State BattleRng() => new Pcg32RandomSource(77, 13).CaptureState();

        private static BattleInitializationResult Initialize(EconomyState player,
            StageProgressState stage, BoardState board, HandState hand,
            CharacterOwnershipState ownership, IReadOnlyList<CharacterDefinition> definitions,
            EnemyBoardState enemies, IReadOnlyList<EnemyDefinition> enemyDefinitions,
            ItemEffectState effects = null, IEnumerable<CharacterDefinition> selected = null,
            IEnumerable<CharacterDefinition> full = null, Pcg32State? random = null,
            BattleConfig config = null, BattleState existing = null)
        {
            IReadOnlyList<CharacterDefinition> chars = definitions ?? Array.Empty<CharacterDefinition>();
            return BattleInitializationRules.Initialize(player, stage, board, hand, ownership,
                chars, enemies, enemyDefinitions ?? Array.Empty<EnemyDefinition>(),
                effects ?? new ItemEffectState(), selected ?? chars, full ?? chars,
                random ?? BattleRng(), config, existing);
        }

        [Test]
        public void BattleStartRules_RejectsBoardCountOverLevelButAllowsEqualOrZero()
        {
            CharacterDefinition[] defs = { CharacterDef("A"), CharacterDef("B"), CharacterDef("C"), CharacterDef("D") };
            CharacterInstance[] instances = new CharacterInstance[4];
            for (int i = 0; i < instances.Length; i++) instances[i] = Instance(defs[i], "I" + i);
            var ownership = new CharacterOwnershipState(instances);
            HandState hand = HandState.CreateStandard();
            StageProgressState stage = BattleStage();

            Assert.That(BattleStartRules.Validate(Player(3), stage,
                Board((0, "I0"), (1, "I1"), (2, "I2"), (3, "I3")), hand, ownership),
                Is.EqualTo(BattleStartFailure.BoardExceedsLevel));
            Assert.That(BattleStartRules.Validate(Player(3), stage,
                Board((0, "I0"), (1, "I1"), (2, "I2")), hand,
                new CharacterOwnershipState(new[] { instances[0], instances[1], instances[2] })),
                Is.EqualTo(BattleStartFailure.None));

            var emptyOwnership = new CharacterOwnershipState(Array.Empty<CharacterInstance>());
            Assert.That(BattleStartRules.Validate(Player(3), stage, BoardState.CreateStandard(), hand,
                emptyOwnership), Is.EqualTo(BattleStartFailure.None));
        }

        [Test]
        public void Initialization_CreatesAlliesInSlotOrderAndExcludesHandCharacters()
        {
            CharacterDefinition a = CharacterDef("A"), b = CharacterDef("B");
            CharacterInstance ia = Instance(a, "IA", 2, -1), ib = Instance(b, "IB");
            CharacterInstance ih = Instance(CharacterDef("H"), "IH");
            BoardState board = Board((1, "IB"), (7, "IA"));
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Character("IH"), out hand), Is.True);
            var ownership = new CharacterOwnershipState(new[] { ia, ib, ih });
            var enemyDef = EnemyDef();
            EnemyBoardState enemyBoard = EnemyBoard(Enemy(enemyDef, 4));
            StageProgressState stage = BattleStage();

            BattleInitializationResult result = Initialize(Player(), stage, board, hand,
                ownership, new[] { a, b, CharacterDef("H") }, enemyBoard,
                new[] { enemyDef });

            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.State.Allies.Count, Is.EqualTo(2));
            Assert.That(result.State.Allies[0].InstanceId, Is.EqualTo("IB"));
            Assert.That(result.State.Allies[0].SlotIndex, Is.EqualTo(1));
            Assert.That(result.State.Allies[1].InstanceId, Is.EqualTo("IA"));
            Assert.That(result.State.Allies[1].SlotIndex, Is.EqualTo(7));
            Assert.That(result.State.Allies[1].Attack, Is.EqualTo(9));
            Assert.That(result.State.Allies[1].Defense, Is.EqualTo(2));
            Assert.That(result.State.Allies[1].Star, Is.EqualTo(1));
            Assert.That(result.State.Allies[1].HasHealth, Is.False);
            Assert.That(result.State.Allies[1].CurrentHP, Is.Zero);
            Assert.That(result.State.Allies[1].TemporaryAttackBonus, Is.EqualTo(2));
            Assert.That(result.State.Allies[1].TemporaryDefenseBonus, Is.EqualTo(-1));
            Assert.That(result.State.Allies[0].UnitTags, Is.Empty);
            Assert.That(result.State.BattleRandomState, Is.EqualTo(BattleRng()));
        }

        [Test]
        public void Initialization_ExcludesZeroHpEnemiesWithoutChangingEnemyBoard()
        {
            EnemyDefinition definition = EnemyDef(hp: 3);
            EnemyInstance dead = Enemy(definition, 0, healthBonus: -3);
            EnemyInstance alive = Enemy(definition, 4);
            EnemyBoardState board = EnemyBoard(dead, alive);
            BattleInitializationResult result = Initialize(Player(), BattleStage(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), null,
                board, new[] { definition });

            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.State.Enemies.Count, Is.EqualTo(1));
            Assert.That(result.State.Enemies[0].SlotIndex, Is.EqualTo(4));
            Assert.That(result.State.Enemies[0].InstanceId, Is.EqualTo(alive.InstanceId));
            Assert.That(result.State.Enemies[0].CurrentHP, Is.EqualTo(3));
            Assert.That(result.State.Enemies[0].MaxHP, Is.EqualTo(3));
            Assert.That(board.TryGetAt(0, out EnemyInstance stillPlaced), Is.True);
            Assert.That(stillPlaced.CurrentHealth, Is.Zero);
        }

        [Test]
        public void Initialization_CopiesEnemyTargetEffectsAndGapTiming()
        {
            EnemyDefinition definition = EnemyDef();
            EnemyInstance instance = Enemy(definition, 2, gap: 4, cooldown: 3);
            BattleInitializationResult result = Initialize(Player(), BattleStage(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), null,
                EnemyBoard(instance), new[] { definition });

            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            BattleUnitState enemy = result.State.Enemies[0];
            Assert.That(enemy.DefinitionId, Is.EqualTo("E1"));
            Assert.That(enemy.Attack, Is.EqualTo(8));
            Assert.That(enemy.Defense, Is.EqualTo(4));
            Assert.That(enemy.Gap, Is.EqualTo(4));
            Assert.That(enemy.Cooldown, Is.EqualTo(3));
            Assert.That(enemy.NextActionRound, Is.EqualTo(5));
            Assert.That(enemy.TargetIds, Is.EqualTo(new[] { "T009", "T010" }));
            Assert.That(enemy.NextTargetIndex, Is.Zero);
            Assert.That(enemy.CipEffectIds, Is.EqualTo(new[] { "CIP" }));
            Assert.That(enemy.AeEffectIds, Is.EqualTo(new[] { "AE" }));
            Assert.That(enemy.PigEffectIds, Is.EqualTo(new[] { "PIG" }));
            Assert.That(enemy.TemporaryDefenseReduction, Is.Zero);
            Assert.That(enemy.StunRounds, Is.Zero);
        }

        [Test]
        public void Initialization_UsesSynergyDetectorForUnitMovesAndAttributeBlock()
        {
            CharacterDefinition a = new CharacterDefinition("A", "A", 1, new[] { "Fire" }, new[] { "Pair" });
            CharacterDefinition b = new CharacterDefinition("B", "B", 1, new[] { "Fire" }, new[] { "Pair" });
            CharacterInstance ia = Instance(a, "IA", star: 2), ib = Instance(b, "IB", star: 3);
            BoardState board = Board((0, "IA"), (1, "IB"));
            var ownership = new CharacterOwnershipState(new[] { ia, ib });
            BattleInitializationResult result = Initialize(Player(), BattleStage(), board,
                HandState.CreateStandard(), ownership, new[] { a, b }, EnemyBoard(),
                Array.Empty<EnemyDefinition>(), selected: new[] { a, b }, full: new[] { a, b });

            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.State.UnitMoves.Count, Is.EqualTo(1));
            Assert.That(result.State.UnitMoves[0].UnitName, Is.EqualTo("Pair"));
            Assert.That(result.State.UnitMoves[0].ParticipantInstanceIds, Is.EqualTo(new[] { "IA", "IB" }));
            Assert.That(result.State.UnitMoves[0].ParticipantSlotIndexes, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(result.State.UnitMoves[0].ParticipantStars, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(result.State.AttributeBlock, Is.EqualTo(1));
            Assert.That(result.State.PlayerBlock, Is.EqualTo(1));
            Assert.That(result.State.UsedUnitMoveIds, Is.Empty);
            Assert.That(result.State.ParticipationCounts.Count, Is.Zero);
        }

        [Test]
        public void Initialization_AddsOneBlockForEachSharedAttributeKind()
        {
            CharacterDefinition[] defs =
            {
                new CharacterDefinition("A", "A", 1, new[] { "Fire" }, null),
                new CharacterDefinition("B", "B", 1, new[] { "Fire" }, null),
                new CharacterDefinition("C", "C", 1, new[] { "Water" }, null),
                new CharacterDefinition("D", "D", 1, new[] { "Water" }, null)
            };
            CharacterInstance[] instances = new CharacterInstance[defs.Length];
            (int Slot, string Id)[] placements = new (int, string)[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                instances[i] = Instance(defs[i], "I" + i);
                placements[i] = (i, "I" + i);
            }
            BattleInitializationResult result = Initialize(Player(level: 4), BattleStage(),
                Board(placements), HandState.CreateStandard(), new CharacterOwnershipState(instances),
                defs, EnemyBoard(), Array.Empty<EnemyDefinition>());

            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.State.AttributeBlock, Is.EqualTo(2));
            Assert.That(result.State.PlayerBlock, Is.EqualTo(2));
        }

        [Test]
        public void Initialization_AddsTemporaryAndPermanentPlayerBlockExactlyOnce()
        {
            StageProgressState preparation = BattleStage();
            // Rebuild a preparation-phase state; the selected encounter is the first, SelectA.
            StageProgressConfig config = StageConfig();
            preparation = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            HandState hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Item("temporary"), out hand), Is.True);
            ItemDefinition item = new ItemDefinition("temporary", "temporary", 0, "",
                ItemEffectTarget.Player, ItemEffectDuration.ThisBattle,
                new[] { new ItemEffectDefinition(ItemEffectKind.Block, 3) });
            ItemEffectResult itemResult = ItemEffectRules.TryUseHandItem(item, 0,
                ItemUseTarget.PlayerArea(0), BoardState.CreateStandard(), hand,
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), Player(), preparation,
                new ItemEffectState(), null, null, BattleRng());
            Assert.That(itemResult.Succeeded, Is.True, itemResult.Failure.ToString());

            // Add an immediate block effect using a second item through the same public rule API.
            HandState hand2 = HandState.CreateStandard();
            Assert.That(hand2.TryWithSlot(0, HandSlotContent.Item("permanent"), out hand2), Is.True);
            ItemDefinition permanent = new ItemDefinition("permanent", "permanent", 0, "",
                ItemEffectTarget.Player, ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(ItemEffectKind.Block, 2) });
            ItemEffectResult permanentResult = ItemEffectRules.TryUseHandItem(permanent, 0,
                ItemUseTarget.PlayerArea(0), BoardState.CreateStandard(), hand2,
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), Player(), preparation,
                itemResult.NextEffectState, null, null, BattleRng());
            Assert.That(permanentResult.Succeeded, Is.True, permanentResult.Failure.ToString());

            StageProgressState battle = StageProgressRules.BeginBattle(preparation).NextState;
            BattleInitializationResult result = Initialize(Player(), battle, BoardState.CreateStandard(),
                permanentResult.NextHand, permanentResult.NextOwnership, null, EnemyBoard(),
                Array.Empty<EnemyDefinition>(), permanentResult.NextEffectState);
            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());
            Assert.That(result.State.PendingPlayerBlockBonus, Is.EqualTo(3));
            Assert.That(result.State.PermanentPlayerBlockBonus, Is.EqualTo(2));
            Assert.That(result.State.PlayerBlock, Is.EqualTo(5));
        }

        [TestCase(1)]
        [TestCase(3)]
        public void BattleConfig_ControlsMaximumRoundAndOutcomeCanRepresentUndecided(int rounds)
        {
            var config = new BattleConfig(rounds);
            Assert.That(config.MaxRounds, Is.EqualTo(rounds));
            Assert.That(Enum.IsDefined(typeof(BattleOutcome), BattleOutcome.Undecided), Is.True);
        }

        [Test]
        public void BattleConfig_DefaultIsOneHundredAndRejectsNonPositiveValues()
        {
            Assert.That(BattleConfig.Default.MaxRounds, Is.EqualTo(100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleConfig(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleConfig(-1));
        }

        [Test]
        public void Initialization_FailureDoesNotReturnPartialStateOrAdvanceBattleRandom()
        {
            Pcg32State rng = BattleRng();
            BattleInitializationResult result = Initialize(Player(), BattleStage(),
                Board((0, "missing")), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), null,
                EnemyBoard(), Array.Empty<EnemyDefinition>(), random: rng);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.State, Is.Null);
            Assert.That(result.Failure, Is.EqualTo(BattleStartFailure.InvalidBoardHandOwnershipState));
            Assert.That(result.NextBattleRandomState, Is.EqualTo(rng));
        }

        [Test]
        public void StartRules_RejectsWrongPhaseAndActiveBattleForAllEntryModes()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState waiting = StageProgressRules.CreateInitialState(config);
            BoardState board = BoardState.CreateStandard();
            HandState hand = HandState.CreateStandard();
            var ownership = new CharacterOwnershipState(Array.Empty<CharacterInstance>());
            Assert.That(BattleStartRules.Validate(Player(), waiting, board, hand, ownership),
                Is.EqualTo(BattleStartFailure.StageNotReady));

            BattleState active = Initialize(Player(), BattleStage(), board, hand, ownership,
                null, EnemyBoard(), Array.Empty<EnemyDefinition>()).State;
            Assert.That(active, Is.Not.Null);
            Assert.That(BattleStartRules.Validate(Player(), BattleStage(), board, hand, ownership, active),
                Is.EqualTo(BattleStartFailure.BattleAlreadyInProgress));
        }

        [Test]
        public void StartRules_RejectsChoiceEventCompletedNoEnemyAndZeroPlayerHp()
        {
            StageProgressConfig config = StageConfig();
            StageProgressState choice = StageProgressRules.CreateInitialState(config);
            for (int i = 0; i < 4; i++)
            {
                choice = StageProgressRules.BeginCurrentEncounter(choice, config).NextState;
                if (choice.Phase == StageProgressPhase.AwaitingStageChoice)
                    choice = StageSelectionRules.Select(choice, config, StageChoice.SelectA).NextState;
                choice = StageProgressRules.BeginBattle(choice).NextState;
                choice = StageProgressRules.ResolveBattle(choice, config, true).NextState;
                choice = StageProgressRules.CompletePendingReward(choice, config, BattleRng()).NextState;
            }
            choice = StageProgressRules.BeginCurrentEncounter(choice, config).NextState;
            Assert.That(choice.Phase, Is.EqualTo(StageProgressPhase.AwaitingStageChoice));
            Assert.That(BattleStartRules.Validate(Player(), choice, BoardState.CreateStandard(),
                HandState.CreateStandard(), new CharacterOwnershipState(Array.Empty<CharacterInstance>())),
                Is.EqualTo(BattleStartFailure.StageNotReady));

            StageProgressConfig eventConfig = new StageProgressConfig(25, 5, 5, 4,
                100, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState eventState = StageProgressRules.CreateInitialState(eventConfig);
            eventState = StageProgressRules.BeginCurrentEncounter(eventState, eventConfig).NextState;
            eventState = StageProgressRules.BeginBattle(eventState).NextState;
            eventState = StageProgressRules.ResolveBattle(eventState, eventConfig, true).NextState;
            eventState = StageProgressRules.CompletePendingReward(eventState, eventConfig, BattleRng()).NextState;
            Assert.That(eventState.Phase, Is.EqualTo(StageProgressPhase.EventPending));
            Assert.That(BattleStartRules.Validate(Player(), eventState, BoardState.CreateStandard(),
                HandState.CreateStandard(), new CharacterOwnershipState(Array.Empty<CharacterInstance>())),
                Is.EqualTo(BattleStartFailure.StageNotReady));

            StageProgressState completed = StageProgressRules.CreateInitialState(config);
            for (int i = 0; i < config.TotalEncounterCount; i++)
            {
                completed = StageProgressRules.BeginCurrentEncounter(completed, config).NextState;
                if (completed.Phase == StageProgressPhase.AwaitingStageChoice)
                    completed = StageSelectionRules.Select(completed, config, StageChoice.SelectA).NextState;
                completed = StageProgressRules.BeginBattle(completed).NextState;
                completed = StageProgressRules.ResolveBattle(completed, config, true).NextState;
                completed = StageProgressRules.CompletePendingReward(completed, config, BattleRng()).NextState;
            }
            Assert.That(completed.Phase, Is.EqualTo(StageProgressPhase.Completed));
            Assert.That(BattleStartRules.Validate(Player(), completed, BoardState.CreateStandard(),
                HandState.CreateStandard(), new CharacterOwnershipState(Array.Empty<CharacterInstance>())),
                Is.EqualTo(BattleStartFailure.StageNotReady));

            BattleInitializationResult zeroHpPlayer = Initialize(Player(hp: 0), BattleStage(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), null,
                EnemyBoard(), Array.Empty<EnemyDefinition>());
            Assert.That(zeroHpPlayer.Succeeded, Is.False);
            Assert.That(zeroHpPlayer.Failure, Is.EqualTo(BattleStartFailure.PlayerHealthUnavailable));

            StageProgressState noEnemyPreparation = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(StageConfig(choiceRound: 1)),
                StageConfig(choiceRound: 1)).NextState;
            noEnemyPreparation = StageSelectionRules.Select(noEnemyPreparation,
                StageConfig(choiceRound: 1), StageChoice.SelectB).NextState;
            Assert.That(BattleStartRules.Validate(Player(), noEnemyPreparation, BoardState.CreateStandard(),
                HandState.CreateStandard(), new CharacterOwnershipState(Array.Empty<CharacterInstance>())),
                Is.EqualTo(BattleStartFailure.NoEnemyEncounterRequired));
        }

        [Test]
        public void Initialization_RejectsMissingEnemyDefinitionAndUnavailablePlayerHealth()
        {
            EnemyDefinition actual = EnemyDef();
            BattleInitializationResult missing = Initialize(Player(), BattleStage(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), null,
                EnemyBoard(Enemy(actual, 0)), Array.Empty<EnemyDefinition>());
            Assert.That(missing.Succeeded, Is.False);
            Assert.That(missing.Failure, Is.EqualTo(BattleStartFailure.MissingEnemyDefinition));

            var noHealth = new EconomyState(0, 0, 3, 12, 0, 0, 1);
            Assert.That(BattleStartRules.Validate(noHealth, BattleStage(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>())),
                Is.EqualTo(BattleStartFailure.PlayerHealthUnavailable));
        }

        [Test]
        public void Initialization_IsDeterministicAndPreservesInputSnapshots()
        {
            CharacterDefinition definition = CharacterDef("A");
            CharacterInstance instance = Instance(definition, "IA", 1, 2);
            var ownership = new CharacterOwnershipState(new[] { instance });
            BoardState board = Board((8, "IA"));
            EnemyDefinition enemyDefinition = EnemyDef();
            EnemyBoardState enemyBoard = EnemyBoard(Enemy(enemyDefinition, 5));
            EconomyState player = Player();
            HandState hand = HandState.CreateStandard();
            StageProgressState stage = BattleStage();

            BattleInitializationResult first = Initialize(player, stage, board, hand, ownership,
                new[] { definition }, enemyBoard, new[] { enemyDefinition });
            BattleInitializationResult second = Initialize(player, stage, board, hand, ownership,
                new[] { definition }, enemyBoard, new[] { enemyDefinition });

            Assert.That(first.Succeeded && second.Succeeded, Is.True);
            Assert.That(first.State.Allies[0].Attack, Is.EqualTo(second.State.Allies[0].Attack));
            Assert.That(first.State.Allies[0].Defense, Is.EqualTo(second.State.Allies[0].Defense));
            Assert.That(first.State.Enemies[0].InstanceId, Is.EqualTo(second.State.Enemies[0].InstanceId));
            Assert.That(first.State.BattleRandomState, Is.EqualTo(second.State.BattleRandomState));
            Assert.That(board.TryGetCharacterIdAt(8, out string boardId), Is.True);
            Assert.That(boardId, Is.EqualTo("IA"));
            Assert.That(ownership.TryGet("IA", out CharacterInstanceView view), Is.True);
            Assert.That(view.TemporaryAttackBonus, Is.EqualTo(1));
            Assert.That(hand.EmptySlotCount, Is.EqualTo(5));
            Assert.That(player.CurrentHP, Is.EqualTo(20));
        }

        [Test]
        public void BattleState_CollectionsAreDefensiveAndReadOnly()
        {
            CharacterDefinition def = CharacterDef("A");
            CharacterInstance instance = Instance(def, "IA");
            BattleInitializationResult result = Initialize(Player(), BattleStage(), Board((0, "IA")),
                HandState.CreateStandard(), new CharacterOwnershipState(new[] { instance }),
                new[] { def }, EnemyBoard(), Array.Empty<EnemyDefinition>());
            Assert.That(result.Succeeded, Is.True, result.Failure.ToString());

            var readOnlyAllies = result.State.Allies as IList<BattleUnitState>;
            Assert.That(readOnlyAllies, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => readOnlyAllies.Add(result.State.Allies[0]));
            var readOnlyCounts = result.State.ParticipationCounts as IDictionary<string, int>;
            Assert.That(readOnlyCounts, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => readOnlyCounts.Add("IA", 1));

            string[] targets = { "T001" };
            var unit = new BattleUnitState("enemy", "E", BattleUnitSide.Enemy,
                0, 1, 1, true, 1, 1, 0, 0, 1, 0, 0,
                null, null, targets, 0, null, null, null);
            targets[0] = "T002";
            Assert.That(unit.TargetIds[0], Is.EqualTo("T001"));
        }
    }
}
