using System;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class ItemEffectRulesTests
    {
        [Test]
        public void BattleAttackAndDefenseEffectsAreTemporaryAndConsumedAtomically()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "buff");
            ItemDefinition item = Item("buff", ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                new ItemEffectDefinition(ItemEffectKind.Attack, 2),
                new ItemEffectDefinition(ItemEffectKind.Defense, 2));

            ItemEffectResult used = UseHand(world, item, ItemUseTarget.BoardCharacter(0));

            Assert.That(used.Succeeded, Is.True);
            Assert.That(used.ConsumedItemDefinitionId, Is.EqualTo("buff"));
            Assert.That(used.NextHand.Slots[0].IsEmpty, Is.True);
            CharacterInstanceView changed = Get(used.NextOwnership, "C1");
            Assert.That(changed.Attack, Is.EqualTo(10));
            Assert.That(changed.Defense, Is.EqualTo(5));
            Assert.That(changed.TemporaryAttackBonus, Is.EqualTo(2));
            Assert.That(changed.TemporaryDefenseBonus, Is.EqualTo(2));
            Assert.That(world.Ownership.TryGet("C1", out CharacterInstanceView original), Is.True);
            Assert.That(original.TemporaryAttackBonus, Is.Zero);

            ItemEffectResult restored = ItemEffectRules.RestoreBattleEffects(used.NextBoard,
                used.NextHand, used.NextOwnership, used.NextEconomy, used.NextEffectState,
                used.NextStatsRandomState);
            Assert.That(restored.Succeeded, Is.True);
            Assert.That(Get(restored.NextOwnership, "C1").TemporaryAttackBonus, Is.Zero);
            Assert.That(Get(restored.NextOwnership, "C1").TemporaryDefenseBonus, Is.Zero);
            Assert.That(Get(restored.NextOwnership, "C1").Attack, Is.EqualTo(10));
            Assert.That(restored.RestoredCharacterIds, Does.Contain("C1"));
            Assert.That(restored.NextEffectState.HasActiveBattleEffects, Is.False);
        }

        [Test]
        public void Item001BattleAttackPlusThreeIsTrackedSeparatelyFromBaseAttack()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "Item001");
            ItemDefinition item = Item("Item001", ItemEffectTarget.OneAlly,
                ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Attack, 3));

            ItemEffectResult used = UseHand(world, item, ItemUseTarget.BoardCharacter(0));

            Assert.That(used.Succeeded, Is.True);
            Assert.That(Get(used.NextOwnership, "C1").Attack, Is.EqualTo(10));
            Assert.That(Get(used.NextOwnership, "C1").TemporaryAttackBonus, Is.EqualTo(3));
        }

        [Test]
        public void ImmediateAttackPersistsThroughBattleRestoration()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "attack");
            ItemDefinition item = Item("attack", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Attack, 1));

            ItemEffectResult used = UseHand(world, item, ItemUseTarget.BoardCharacter(0));

            Assert.That(used.Succeeded, Is.True);
            Assert.That(Get(used.NextOwnership, "C1").Attack, Is.EqualTo(11));
            Assert.That(used.NextEffectState.HasActiveBattleEffects, Is.False);
            Assert.That(used.NextHand.Slots[0].IsEmpty, Is.True);
        }

        [Test]
        public void Item006StyleImmediateStarChangeUsesStatsRandomAndRejectsAlreadyStarTwo()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "star");
            ItemDefinition item = Item("star", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            ItemEffectResult used = UseHand(world, item, ItemUseTarget.BoardCharacter(0));

            Assert.That(used.Succeeded, Is.True);
            Assert.That(used.StatsRandomConsumed, Is.True);
            Assert.That(Get(used.NextOwnership, "C1").Star, Is.EqualTo(2));
            Assert.That(Get(used.NextOwnership, "C1").Attack, Is.EqualTo(20));
            Assert.That(used.NextEffectState.HasActiveBattleEffects, Is.False);

            World alreadyStarTwo = MakeWorld(withHandItem: true, star: 2, handItemId: "star");
            ItemEffectResult rejected = UseHand(alreadyStarTwo, item, ItemUseTarget.BoardCharacter(0));
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Failure, Is.EqualTo(ItemEffectFailure.CharacterNotEligible));
            Assert.That(rejected.NextHand, Is.SameAs(alreadyStarTwo.Hand));
            Assert.That(rejected.NextOwnership, Is.SameAs(alreadyStarTwo.Ownership));
            Assert.That(rejected.StatsRandomConsumed, Is.False);
            AssertStateEqual(rejected.NextStatsRandomState, alreadyStarTwo.UseRandomState);
        }

        [Test]
        public void ImmediatePlayerDamageNegativeValueHealsAndRespectsMaximumHp()
        {
            World world = MakeWorld(withHandItem: true, currentHp: 18, maximumHp: 20, handItemId: "heal");
            ItemDefinition item = Item("heal", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Damage, -4));
            ItemEffectResult used = UseHand(world, item, ItemUseTarget.PlayerArea(0));

            Assert.That(used.Succeeded, Is.True);
            Assert.That(used.NextEconomy.CurrentHP, Is.EqualTo(20));
            Assert.That(used.NextEffectState.HasActiveBattleEffects, Is.False);
            Assert.That(used.NextHand.Slots[0].IsEmpty, Is.True);
        }

        [Test]
        public void TemporaryPlayerHpAndBlockAreRestoredButPermanentBlockRemains()
        {
            World world = MakeWorld(withHandItem: true, currentHp: 10, maximumHp: 20, handItemId: "temp-hp");
            ItemDefinition battleHp = Item("temp-hp", ItemEffectTarget.Player,
                ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Damage, -4));
            ItemEffectResult hp = UseHand(world, battleHp, ItemUseTarget.PlayerArea(0));
            Assert.That(hp.Succeeded, Is.True);
            Assert.That(hp.NextEconomy.CurrentHP, Is.EqualTo(14));
            ItemEffectResult hpRestored = ItemEffectRules.RestoreBattleEffects(hp.NextBoard, hp.NextHand,
                hp.NextOwnership, hp.NextEconomy, hp.NextEffectState, hp.NextStatsRandomState);
            Assert.That(hpRestored.NextEconomy.CurrentHP, Is.EqualTo(10));

            World blockWorld = MakeWorld(withHandItem: true, handItemId: "block-now");
            ItemDefinition permanent = Item("block-now", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Block, 2));
            ItemEffectResult permanentResult = UseHand(blockWorld, permanent, ItemUseTarget.PlayerArea(0));
            ItemDefinition temporary = Item("block-battle", ItemEffectTarget.Player,
                ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Block, 3));
            World nextWorld = blockWorld.WithItem(permanentResult.NextHand, "block-battle");
            ItemEffectResult temporaryResult = UseHand(nextWorld, temporary, ItemUseTarget.PlayerArea(0),
                permanentResult.NextEconomy, permanentResult.NextEffectState);
            Assert.That(temporaryResult.NextEffectState.PermanentPlayerBlockBonus, Is.EqualTo(2));
            Assert.That(temporaryResult.NextEffectState.TemporaryPlayerBlockBonus, Is.EqualTo(3));
            ItemEffectResult blockRestored = ItemEffectRules.RestoreBattleEffects(temporaryResult.NextBoard,
                temporaryResult.NextHand, temporaryResult.NextOwnership, temporaryResult.NextEconomy,
                temporaryResult.NextEffectState, temporaryResult.NextStatsRandomState);
            Assert.That(blockRestored.NextEffectState.PermanentPlayerBlockBonus, Is.EqualTo(2));
            Assert.That(blockRestored.NextEffectState.TemporaryPlayerBlockBonus, Is.Zero);
        }

        [Test]
        public void EnemyItemsReturnBattleScopedCommandsWithoutMutatingEnemyState()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "def-down");
            ItemDefinition defense = Item("def-down", ItemEffectTarget.AllEnemies,
                ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Defense, -1));
            ItemEffectResult defResult = UseHand(world, defense, ItemUseTarget.EnemyArea(2));
            Assert.That(defResult.Succeeded, Is.True);
            Assert.That(defResult.Commands.Count, Is.EqualTo(1));
            Assert.That(defResult.Commands[0].Effect, Is.EqualTo(ItemEffectKind.Defense));
            Assert.That(defResult.Commands[0].Value, Is.EqualTo(-1));
            Assert.That(defResult.Commands[0].Target, Is.EqualTo(ItemEffectTarget.AllEnemies));
            Assert.That(defResult.Commands[0].Duration, Is.EqualTo(ItemEffectDuration.ThisBattle));
            Assert.That(defResult.NextEffectState.EnemyCommands.Count, Is.EqualTo(1));

            World damageWorld = MakeWorld(withHandItem: true, handItemId: "enemy-hit");
            ItemDefinition damage = Item("enemy-hit", ItemEffectTarget.AllEnemies,
                ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Damage, 2));
            ItemEffectResult damageResult = UseHand(damageWorld, damage, ItemUseTarget.EnemyArea(2));
            Assert.That(damageResult.Succeeded, Is.True);
            Assert.That(damageResult.Commands[0].Effect, Is.EqualTo(ItemEffectKind.Damage));
            Assert.That(damageResult.Commands[0].Value, Is.EqualTo(2));
            ItemEffectResult cleared = ItemEffectRules.RestoreBattleEffects(damageResult.NextBoard,
                damageResult.NextHand, damageResult.NextOwnership, damageResult.NextEconomy,
                damageResult.NextEffectState, damageResult.NextStatsRandomState);
            Assert.That(cleared.ExpiredEnemyCommands.Count, Is.EqualTo(1));
            Assert.That(cleared.NextEffectState.EnemyCommands, Is.Empty);
        }

        [Test]
        public void BattleOnlyCharacterEffectCanBeRestoredAfterCharacterMovesToHand()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "battle-attack");
            ItemDefinition item = Item("battle-attack", ItemEffectTarget.OneAlly,
                ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Attack, 3));
            ItemEffectResult used = UseHand(world, item, ItemUseTarget.BoardCharacter(0));
            BoardHandOperationResult moved = BoardHandRules.MoveBoardToHand(used.NextBoard,
                used.NextHand, used.NextOwnership, 0, 1, BoardHandOperationContext.Preparation);
            Assert.That(moved.Succeeded, Is.True);

            ItemEffectResult restored = ItemEffectRules.RestoreBattleEffects(moved.NextBoard,
                moved.NextHand, used.NextOwnership, used.NextEconomy, used.NextEffectState,
                used.NextStatsRandomState);
            Assert.That(restored.Succeeded, Is.True);
            Assert.That(restored.RestoredCharacterIds, Does.Contain("C1"));
            Assert.That(Get(restored.NextOwnership, "C1").TemporaryAttackBonus, Is.Zero);
            Assert.That(restored.NextHand.Slots[1].CharacterInstanceId, Is.EqualTo("C1"));
            Assert.That(ItemEffectRules.RestoreBattleEffects(restored.NextBoard, restored.NextHand,
                restored.NextOwnership, restored.NextEconomy, restored.NextEffectState,
                restored.NextStatsRandomState).Failure, Is.EqualTo(ItemEffectFailure.NoBattleEffectsToRestore));
        }

        [Test]
        public void TemporaryStarIsRestoredByInstanceIdAfterMovingToHand()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "temp-star");
            ItemDefinition item = Item("temp-star", ItemEffectTarget.OneAlly,
                ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            ItemEffectResult used = UseHand(world, item, ItemUseTarget.BoardCharacter(0));
            Assert.That(used.Succeeded, Is.True);
            Assert.That(Get(used.NextOwnership, "C1").Star, Is.EqualTo(2));
            Assert.That(Get(used.NextOwnership, "C1").HasTemporaryStar, Is.True);

            BoardHandOperationResult moved = BoardHandRules.MoveBoardToHand(used.NextBoard,
                used.NextHand, used.NextOwnership, 0, 1, BoardHandOperationContext.Preparation);
            Assert.That(moved.Succeeded, Is.True);
            ItemEffectResult restored = ItemEffectRules.RestoreBattleEffects(moved.NextBoard,
                moved.NextHand, used.NextOwnership, used.NextEconomy, used.NextEffectState,
                used.NextStatsRandomState);

            Assert.That(restored.Succeeded, Is.True);
            Assert.That(Get(restored.NextOwnership, "C1").Star, Is.EqualTo(1));
            Assert.That(Get(restored.NextOwnership, "C1").Attack, Is.EqualTo(10));
            Assert.That(Get(restored.NextOwnership, "C1").HasTemporaryStar, Is.False);
        }

        [Test]
        public void EventItemCanTargetHandCharacterAndCannotBeConsumedTwice()
        {
            World world = MakeWorld(withHandItem: false, characterOnBoard: false);
            EventItemState eventItem = new EventItemState("event-star");
            ItemDefinition item = Item("event-star", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            StageProgressState eventStage = CreateEventState();
            ItemEffectResult failedTarget = ItemEffectRules.TryUseEventItem(item, eventItem,
                ItemUseTarget.BoardCharacter(0), world.Board, world.Hand, world.Ownership,
                world.Economy, eventStage, new ItemEffectState(), new[] { world.Definition },
                world.Settings, world.UseRandomState);
            Assert.That(failedTarget.Succeeded, Is.False);
            Assert.That(failedTarget.NextEventItem, Is.SameAs(eventItem));
            Assert.That(failedTarget.NextOwnership, Is.SameAs(world.Ownership));

            ItemEffectResult used = ItemEffectRules.TryUseEventItem(item, eventItem,
                ItemUseTarget.HandCharacter(0), world.Board, world.Hand, world.Ownership,
                world.Economy, eventStage, new ItemEffectState(), new[] { world.Definition },
                world.Settings, world.UseRandomState);
            Assert.That(used.Succeeded, Is.True);
            Assert.That(used.NextEventItem.Status, Is.EqualTo(EventItemStatus.Consumed));
            Assert.That(used.NextHand.Slots[0].CharacterInstanceId, Is.EqualTo("C1"));
            Assert.That(Get(used.NextOwnership, "C1").Star, Is.EqualTo(2));

            ItemEffectResult twice = ItemEffectRules.TryUseEventItem(item, used.NextEventItem,
                ItemUseTarget.HandCharacter(0), used.NextBoard, used.NextHand, used.NextOwnership,
                used.NextEconomy, used.NextStage, used.NextEffectState, new[] { world.Definition },
                world.Settings, used.NextStatsRandomState);
            Assert.That(twice.Succeeded, Is.False);
            Assert.That(twice.Failure, Is.EqualTo(ItemEffectFailure.EventItemUnavailable));
            Assert.That(twice.NextOwnership, Is.SameAs(used.NextOwnership));
            Assert.That(twice.NextEventItem, Is.SameAs(used.NextEventItem));
        }

        [Test]
        public void EventEndsWithoutConsumingItemWhenNoCharacterIsEligible()
        {
            World world = MakeWorld(withHandItem: false, star: 2);
            EventItemState eventItem = new EventItemState("event-star");
            ItemDefinition item = Item("event-star", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            StageProgressState eventStage = CreateEventState();

            ItemEffectResult ended = ItemEffectRules.EndEventWithoutEligibleTarget(item, eventItem,
                world.Board, world.Hand, world.Ownership, world.Economy, eventStage,
                new ItemEffectState(), world.UseRandomState);

            Assert.That(ended.Succeeded, Is.True);
            Assert.That(ended.NextStage.Phase, Is.EqualTo(StageProgressPhase.AwaitingEncounterStart));
            Assert.That(ended.NextEventItem.Status, Is.EqualTo(EventItemStatus.EndedWithoutUse));
            Assert.That(ended.ConsumedItemDefinitionId, Is.Null);
            Assert.That(ended.NextOwnership, Is.SameAs(world.Ownership));

            ItemEffectResult stillEligible = ItemEffectRules.EndEventWithoutEligibleTarget(
                Item("other", ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate,
                    new ItemEffectDefinition(ItemEffectKind.Attack, 1)),
                new EventItemState("other"), world.Board, world.Hand, world.Ownership,
                world.Economy, eventStage, new ItemEffectState(), world.UseRandomState);
            Assert.That(stillEligible.Succeeded, Is.False);
            Assert.That(stillEligible.Failure, Is.EqualTo(ItemEffectFailure.EligibleEventTargetExists));
        }

        [Test]
        public void InvalidTargetDurationAndPhaseDoNotConsumeItemOrChangeState()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "ally");
            ItemDefinition ally = Item("ally", ItemEffectTarget.OneAlly,
                ItemEffectDuration.ThisBattle, new ItemEffectDefinition(ItemEffectKind.Attack, 1));
            World emptyWorld = MakeWorld(withHandItem: true, characterOnBoard: false, handItemId: "ally");
            ItemEffectResult missing = UseHand(emptyWorld, ally, ItemUseTarget.BoardCharacter(0));
            Assert.That(missing.Succeeded, Is.False);
            Assert.That(missing.NextHand, Is.SameAs(emptyWorld.Hand));
            Assert.That(missing.NextOwnership, Is.SameAs(emptyWorld.Ownership));

            ItemDefinition unspecified = new ItemDefinition("bad", "Bad", 0, "",
                ItemEffectTarget.OneAlly, ItemEffectDuration.Unspecified,
                new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 1) });
            World badItemWorld = MakeWorld(withHandItem: true, handItemId: "bad");
            ItemEffectResult invalidDuration = UseHand(badItemWorld, unspecified, ItemUseTarget.BoardCharacter(0));
            Assert.That(invalidDuration.Failure, Is.EqualTo(ItemEffectFailure.InvalidItem));

            StageProgressConfig config = ProgressConfig();
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            StageProgressState battle = StageProgressRules.BeginBattle(prep).NextState;
            ItemEffectResult wrongPhase = ItemEffectRules.TryUseHandItem(ally, 0,
                ItemUseTarget.BoardCharacter(0), world.Board, world.Hand, world.Ownership,
                world.Economy, battle, new ItemEffectState(), new[] { world.Definition },
                world.Settings, world.UseRandomState);
            Assert.That(wrongPhase.Failure, Is.EqualTo(ItemEffectFailure.InvalidPhase));
            Assert.That(wrongPhase.NextHand, Is.SameAs(world.Hand));
            Assert.That(wrongPhase.NextOwnership, Is.SameAs(world.Ownership));
            AssertStateEqual(wrongPhase.NextStatsRandomState, world.UseRandomState);
        }

        [Test]
        public void FailedStarUseDoesNotConsumeRandomAndImmediateEffectIsNotRestored()
        {
            World world = MakeWorld(withHandItem: true, handItemId: "star");
            ItemDefinition star = Item("star", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            ItemDefinition missingStatsItem = Item("star", ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new ItemEffectDefinition(ItemEffectKind.Star, 2));
            ItemEffectResult failed = ItemEffectRules.TryUseHandItem(missingStatsItem, 0,
                ItemUseTarget.BoardCharacter(0), world.Board, world.Hand, world.Ownership,
                world.Economy, world.Stage, new ItemEffectState(), new[] { world.Definition },
                null, world.UseRandomState);
            Assert.That(failed.Failure, Is.EqualTo(ItemEffectFailure.StatsUnavailable));
            Assert.That(failed.NextHand, Is.SameAs(world.Hand));
            AssertStateEqual(failed.NextStatsRandomState, world.UseRandomState);

            ItemEffectResult successful = UseHand(world, star, ItemUseTarget.BoardCharacter(0));
            Assert.That(successful.Succeeded, Is.True);
            Assert.That(successful.NextEffectState.HasActiveBattleEffects, Is.False);
            Assert.That(successful.StatsRandomConsumed, Is.True);
        }

        private static ItemEffectResult UseHand(World world, ItemDefinition item, ItemUseTarget target,
            EconomyState economy = null, ItemEffectState effectState = null)
        {
            return ItemEffectRules.TryUseHandItem(item, 0, target, world.Board, world.Hand,
                world.Ownership, economy ?? world.Economy, world.Stage,
                effectState ?? new ItemEffectState(), new[] { world.Definition }, world.Settings,
                world.UseRandomState);
        }

        private static ItemDefinition Item(string id, ItemEffectTarget target,
            ItemEffectDuration duration, params ItemEffectDefinition[] effects) =>
            new ItemDefinition(id, id, 0, string.Empty, target, duration, effects);

        private static CharacterInstanceView Get(CharacterOwnershipState state, string id)
        {
            Assert.That(state.TryGet(id, out CharacterInstanceView view), Is.True);
            return view;
        }

        private static World MakeWorld(bool withHandItem, bool characterOnBoard = true,
            int star = 1, int currentHp = 10, int maximumHp = 20, string handItemId = "Item")
        {
            CharacterDefinition definition = new CharacterDefinition("D1", "Hero", 1, null, null);
            CharacterStatDefinition settings = new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(1,
                    new[] { new CharacterStatOption(10, 5, 1) },
                    new[] { new CharacterStatOption(20, 9, 1) },
                    new[] { new CharacterStatOption(30, 15, 1) })
            });
            CharacterInstance instance = new CharacterInstanceFactory().Create(definition, 1);
            var setupRandom = new Pcg32RandomSource(10, 4);
            Assert.That(instance.TrySetStar(star, definition, settings, setupRandom), Is.True);
            CharacterOwnershipState ownership = new CharacterOwnershipState(new[] { instance });

            BoardState board = BoardState.CreateStandard();
            HandState hand = HandState.CreateEmpty(5);
            if (characterOnBoard)
                board.TryWithCharacterAt(0, instance.InstanceId, out board);
            else
                hand.TryWithSlot(withHandItem ? 1 : 0, HandSlotContent.Character(instance.InstanceId), out hand);
            if (withHandItem) hand.TryWithSlot(0, HandSlotContent.Item(handItemId), out hand);

            EconomyState economy = new EconomyState(10, 0, 1, 12, 10, 0, 1, currentHp, maximumHp);
            StageProgressConfig config = ProgressConfig();
            StageProgressState stage = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            return new World(definition, settings, board, hand, ownership, economy, stage,
                new Pcg32RandomSource(777, 9).CaptureState());
        }

        private static StageProgressConfig ProgressConfig(bool events = false) =>
            new StageProgressConfig(events ? 2 : 25, events ? 2 : 5, events ? 2 : 5,
                events ? 2 : 4, events ? 100 : 0, 0, events ? 100 : 0,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static StageProgressState CreateEventState()
        {
            StageProgressConfig config = ProgressConfig(events: true);
            StageProgressState initial = StageProgressRules.CreateInitialState(config);
            StageProgressState preparation = StageProgressRules.BeginCurrentEncounter(initial, config).NextState;
            StageProgressState battle = StageProgressRules.BeginBattle(preparation).NextState;
            StageProgressState pending = StageProgressRules.ResolveBattle(battle, config, true).NextState;
            return StageProgressRules.CompletePendingReward(pending, config, default).NextState;
        }

        private static void AssertStateEqual(Pcg32State left, Pcg32State right)
        {
            Assert.That(left.AlgorithmVersion, Is.EqualTo(right.AlgorithmVersion));
            Assert.That(left.State, Is.EqualTo(right.State));
            Assert.That(left.Increment, Is.EqualTo(right.Increment));
        }

        private sealed class World
        {
            public CharacterDefinition Definition { get; }
            public CharacterStatDefinition Settings { get; }
            public BoardState Board { get; }
            public HandState Hand { get; }
            public CharacterOwnershipState Ownership { get; }
            public EconomyState Economy { get; }
            public StageProgressState Stage { get; }
            public Pcg32State UseRandomState { get; }

            public World(CharacterDefinition definition, CharacterStatDefinition settings,
                BoardState board, HandState hand, CharacterOwnershipState ownership,
                EconomyState economy, StageProgressState stage, Pcg32State useRandomState)
            {
                Definition = definition;
                Settings = settings;
                Board = board;
                Hand = hand;
                Ownership = ownership;
                Economy = economy;
                Stage = stage;
                UseRandomState = useRandomState;
            }

            public World WithHand(HandState hand) => new World(Definition, Settings, Board,
                hand, Ownership, Economy, Stage, UseRandomState);

            public World WithItem(HandState hand, string itemId)
            {
                hand.TryWithSlot(0, HandSlotContent.Item(itemId), out HandState withItem);
                return WithHand(withItem);
            }
        }
    }
}
