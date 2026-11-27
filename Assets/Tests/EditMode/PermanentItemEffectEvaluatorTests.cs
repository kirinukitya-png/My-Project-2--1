using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class PermanentItemEffectEvaluatorTests
    {
        private static readonly GameActionTarget BoardTarget = Target(ItemUseTargetKind.BoardCharacter, 0);
        private static readonly GameActionTarget HandTarget = Target(ItemUseTargetKind.HandCharacter, 1);
        private static readonly GameActionTarget PlayerTarget = Target(ItemUseTargetKind.PlayerArea, 0);

        [TestCase(1, 4, 16, 38)]
        [TestCase(5, 4, 16, 30)]
        [TestCase(10, 4, 8, 20)]
        [TestCase(15, 4, 8, 15)]
        [TestCase(20, 4, 4, 10)]
        [TestCase(25, 4, 4, 5)]
        [TestCase(29, 1, 1, 1)]
        [TestCase(30, 0, 0, 0)]
        public void HealingTableUsesPreviewAndReusableHealingApi(int hp, int healing, int value, int tenHpValue)
        {
            var observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(5, hp: hp));
            var evaluator = new HpValueEvaluator();
            var result = evaluator.EvaluateItem(Use(observation, PlayerTarget));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.BeforeHp, Is.EqualTo(hp));
            Assert.That(result.AfterHp, Is.EqualTo(hp + healing));
            Assert.That(result.MaximumHp, Is.EqualTo(30));
            Assert.That(result.EffectiveHealing, Is.EqualTo(healing));
            Assert.That(result.EffectValue, Is.EqualTo(value));
            Assert.That(evaluator.CompareHealing(hp, 10, 30).Delta, Is.EqualTo(tenHpValue));
        }

        [TestCase(8, 12, 30, 12)]
        [TestCase(18, 22, 30, 6)]
        [TestCase(8, 18, 30, 24)]
        [TestCase(25, 30, 30, 5)]
        [TestCase(4, 8, 15, 10)]
        [TestCase(18, 22, 60, 12)]
        public void HpComparisonIsAntisymmetric(int before, int after, int max, int expected)
        {
            var evaluator = new HpValueEvaluator();
            Assert.That(evaluator.Compare(before, after, max).Delta, Is.EqualTo(expected));
            Assert.That(evaluator.Compare(after, before, max).Delta, Is.EqualTo(-expected));
        }

        [Test]
        public void HpValueIsMonotoneAndSplittingDoesNotChangeValue()
        {
            var evaluator = new HpValueEvaluator();
            Assert.That(evaluator.Compare(8, 12, 30).Delta + evaluator.Compare(12, 18, 30).Delta, Is.EqualTo(24));
            Assert.That(evaluator.Compare(25, 29, 30).Delta + evaluator.Compare(29, 30, 30).Delta, Is.EqualTo(5));
            Assert.That(evaluator.Evaluate(4, 10), Is.EqualTo(44m / 3m));
            Assert.That(evaluator.Evaluate(7, 10), Is.EqualTo(61m / 3m));
            for (int hp = 0; hp < 10; hp++)
                Assert.That(evaluator.Evaluate(hp + 1, 10), Is.GreaterThan(evaluator.Evaluate(hp, 10)));
            Assert.That(evaluator.Compare(3, 4, 10).Delta + evaluator.Compare(4, 7, 10).Delta,
                Is.EqualTo(evaluator.Compare(3, 7, 10).Delta));
        }

        [Test]
        public void HpRejectsInvalidInputs()
        {
            var evaluator = new HpValueEvaluator();
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(-1, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(31, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.CompareHealing(1, -1, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HpValueSettings(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HpValueSettings(1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HpValueSettings(4, 2, 3));
        }

        [Test]
        public void HealingFromPurchasedUseKeepsPriceAndCapacityOutsideEffectValue()
        {
            var observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(5,
                hp: 5, choice: StageChoice.SelectB, extraItem: 5, extraReserve: true));
            var purchase = ItemEffectPreview.PreviewPurchase(observation, observation.Shop.Offers.Single().OfferId, 4);
            var result = new HpValueEvaluator().EvaluateItem(
                ItemEffectPreview.PreviewUseAfterPurchase(observation, purchase, PlayerTarget));
            Assert.That(result.EffectValue, Is.EqualTo(16));
            Assert.That(result.CapacityActionValue, Is.EqualTo(40));
            Assert.That(result.GoldDelta, Is.Zero);
            Assert.That(result.GoldDeltaIncludingPurchase, Is.EqualTo(-4));
        }

        [TestCase(8, 1.2, 8.96)]
        [TestCase(7, -1, 7.2)]
        [TestCase(20, 0, 8)]
        public void KeyUsesActualBattleAndReferenceAndSeparatesTemporaryStats(int enemyHp, double expectedR, double expected)
        {
            var observation = World(7, enemyHp: enemyHp, temporary: true);
            var preview = Use(observation, BoardTarget);
            var result = new PermanentItemEffectEvaluator(observation).Evaluate(preview);
            Assert.That(result.Status, Is.EqualTo(ItemEffectEvaluationStatus.Evaluated), result.BattleFailure?.Status.ToString());
            Assert.That(result.Composition.Value.Before.Value.BoardCombat, Is.EqualTo(3.6m));
            Assert.That(result.Composition.Value.After.Value.BoardCombat, Is.EqualTo(4.6m));
            Assert.That(result.Composition.Value.Delta.BoardCombat, Is.EqualTo(1));
            Assert.That(result.Battle.Before.Reference.Attack.ExpectedEffectiveDamage, Is.EqualTo(7));
            Assert.That(result.Battle.After.Reference.Attack.ExpectedEffectiveDamage, Is.EqualTo(8));
            Assert.That(result.Battle.Before.Current.Attack.ExpectedEffectiveDamage, Is.EqualTo(7));
            Assert.That(result.Battle.Before.Current.Defense.Incoming, Is.EqualTo(enemyHp == 7 ? 0 : 2));
            Assert.That(result.Battle.DeltaR, Is.EqualTo((decimal)expectedR));
            Assert.That(result.EffectValue, Is.EqualTo((decimal)expected));
            Assert.That(new IntegratedCompositionValueEvaluator(observation).Baseline.Value.BoardCombat, Is.EqualTo(8.2m));
        }

        [TestCase(1, 5.30, 50.40)]
        [TestCase(5, 8.30, 74.40)]
        public void StarUsesFractionalPublicMeansAndExistingMergeRule(int cost, double combat, double score)
        {
            var observation = World(6, cost: cost);
            var result = new PermanentItemEffectEvaluator(observation,
                battleSettings: new BattleValueSettings(0)).Evaluate(Use(observation, BoardTarget));
            Assert.That(result.StatsKind, Is.EqualTo(ItemPreviewStatsKind.PublicMean));
            Assert.That(result.Preview.After.Character.Attack, Is.EqualTo(cost == 1 ? 6.5 : 9.5));
            Assert.That(result.Preview.After.Character.Defense, Is.EqualTo(2.75));
            Assert.That(result.Composition.Value.Delta.BoardCombat, Is.EqualTo((decimal)combat));
            Assert.That(result.Composition.Value.Delta.MergeProgress, Is.EqualTo(1));
            Assert.That(result.BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.Disabled));
            Assert.That(result.EffectValue, Is.EqualTo((decimal)score));
            Assert.That(result.GoldDelta, Is.Zero);
        }

        [Test]
        public void StarCanLoseMergeProgressAndHaveNegativeEffect()
        {
            var observation = World(6, reserveStars: new[] { 1, 2, 2 }, eventPending: true, poorMean: true);
            var preview = ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Event, BoardTarget);
            var result = new PermanentItemEffectEvaluator(observation).Evaluate(preview);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Composition.Value.Delta.MergeProgress, Is.EqualTo(-1));
            Assert.That(result.Composition.Value.Delta.BoardCombat, Is.EqualTo(-2.85m));
            Assert.That(result.EffectValue, Is.EqualTo(-30.8m));
            Assert.That(result.BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.Event));
            Assert.That(result.CapacityDelta, Is.Zero);
        }

        [Test]
        public void StarRaisesBoardActivationOrHandOptionUsingExistingOneMoveSearch()
        {
            var board = World(6, layout: "AB.C.....", units: "AB,AC");
            var result = new PermanentItemEffectEvaluator(board, battleSettings: new BattleValueSettings(0))
                .Evaluate(Use(board, BoardTarget));
            Assert.That(result.Composition.Value.Delta.Synergy.UnitActivationValue, Is.EqualTo(8));
            Assert.That(result.Composition.Value.Delta.HandUnitOption, Is.Zero);

            var hand = World(6, layout: ".B.C.....", units: "AB,AC", reserveStars: new[] { 1 }, eventPending: true);
            var held = new PermanentItemEffectEvaluator(hand)
                .Evaluate(ItemEffectPreview.PreviewUse(hand, ItemPreviewSource.Event, HandTarget));
            Assert.That(held.Composition.Value.Delta.BoardCombat, Is.Zero);
            Assert.That(held.Composition.Value.Delta.HandReserve, Is.EqualTo(1.855m));
            Assert.That(held.Composition.Value.Delta.HandUnitOption, Is.EqualTo(1.20m));
            Assert.That(held.Composition.Value.Delta.Synergy.TotalSynergyValue, Is.Zero);
        }

        [Test]
        public void PermanentKeyIsReplacedAndTemporaryStatsRemainInBattleProjection()
        {
            var session = ItemEffectPreviewTests.Session(7, extraItem: 6);
            var initial = ItemEffectPreviewTests.Observe(session);
            var key = new PermanentItemEffectEvaluator(initial).Evaluate(Use(initial, BoardTarget));
            Assert.That(key.Composition.Value.Delta.BoardCombat, Is.EqualTo(1));

            ItemEffectPreviewTests.Execute(session, new UseHandItemAction(new ActionId("key"), 0, BoardTarget));
            var next = ItemEffectPreviewTests.Observe(session);
            var preview = ItemEffectPreview.PreviewUse(next, ItemPreviewSource.Hand, BoardTarget, 2);
            var star = new PermanentItemEffectEvaluator(next).Evaluate(preview);
            Assert.That(star.Preview.After.Character.Attack, Is.EqualTo(6.5));
            Assert.That(star.Preview.After.Character.TemporaryAttack, Is.EqualTo(4));
            Assert.That(star.Preview.After.Character.TemporaryDefense, Is.EqualTo(1));
            Assert.That(star.Composition.Value.Delta.BoardCombat, Is.EqualTo(4.3m));
            Assert.That(star.Battle.After.UsesPublicMeanStats, Is.True);
            Assert.That(star.Battle.After.Reference.Attack.ExpectedEffectiveDamage, Is.EqualTo(10.5m));
        }

        [Test]
        public void BattlePreservesItemBlockOnBothSides()
        {
            var block = new ItemDefinition("block", "Block", 0, string.Empty, ItemEffectTarget.Player,
                ItemEffectDuration.ThisBattle, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 2) });
            var session = ItemEffectPreviewTests.Session(5, extraItem: 7, custom: block);
            ItemEffectPreviewTests.Execute(session, new UseHandItemAction(new ActionId("block"), 0, PlayerTarget));
            var observation = ItemEffectPreviewTests.Observe(session);
            var preview = ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Hand, BoardTarget, 2);
            var result = new PermanentItemEffectEvaluator(observation).Evaluate(preview);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Battle.Before.InitialBlock, Is.EqualTo(2));
            Assert.That(result.Battle.After.InitialBlock, Is.EqualTo(2));
            Assert.That(result.CompositionActionValue, Is.EqualTo(8));
        }

        [TestCase(false, 16)]
        [TestCase(true, 40)]
        public void CapacityIsReportedSeparately(bool full, int capacity)
        {
            var session = ItemEffectPreviewTests.Session(7, fullHand: full, extraItem: 7, extraReserve: !full);
            var observation = ItemEffectPreviewTests.Observe(session);
            var result = new PermanentItemEffectEvaluator(observation, battleSettings: new BattleValueSettings(0))
                .Evaluate(Use(observation, BoardTarget));
            Assert.That(result.CapacityActionValue, Is.EqualTo(capacity));
            Assert.That(result.EffectValue, Is.EqualTo(8));
        }

        [Test]
        public void PurchasedUseSeparatesPriceAndSameStarChangeMatchesEventComposition()
        {
            var boughtIn = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(7, choice: StageChoice.SelectB));
            var purchase = ItemEffectPreview.PreviewPurchase(boughtIn, boughtIn.Shop.Offers.Single().OfferId, 4);
            var result = new PermanentItemEffectEvaluator(boughtIn)
                .Evaluate(ItemEffectPreview.PreviewUseAfterPurchase(boughtIn, purchase, BoardTarget));
            Assert.That(result.EffectValue, Is.EqualTo(8));
            Assert.That(result.GoldDelta, Is.Zero);
            Assert.That(result.GoldDeltaIncludingPurchase, Is.EqualTo(-5));
            Assert.That(result.CapacityDeltaIncludingPurchase, Is.Zero);
            Assert.That(result.BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.Enemyless));

            var eventObservation = World(6, eventPending: true);
            var handObservation = World(6, choice: StageChoice.SelectD);
            var viaEvent = new PermanentItemEffectEvaluator(eventObservation)
                .Evaluate(ItemEffectPreview.PreviewUse(eventObservation, ItemPreviewSource.Event, BoardTarget));
            var viaHand = new PermanentItemEffectEvaluator(handObservation).Evaluate(Use(handObservation, BoardTarget));
            Assert.That(viaEvent.Composition.Value.Delta, Is.EqualTo(viaHand.Composition.Value.Delta));
            Assert.That(viaEvent.EffectValue, Is.EqualTo(viaHand.EffectValue));
        }

        [Test]
        public void ZeroHpAndDisabledAreSeparateAndDoNotRequestBattle()
        {
            var observation = World(7, enemyHp: 0);
            var evaluator = new PermanentItemEffectEvaluator(observation);
            Assert.That(evaluator.Evaluate(Use(observation, BoardTarget)).BattleSkipReason,
                Is.EqualTo(ItemBattleSkipReason.AllEnemiesAtZeroHp));
            Assert.That(evaluator.BattleRequests, Is.Zero);
            observation = World(7);
            evaluator = new PermanentItemEffectEvaluator(observation, battleSettings: new BattleValueSettings(0));
            Assert.That(evaluator.Evaluate(Use(observation, BoardTarget)).BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.Disabled));
            Assert.That(evaluator.BattleRequests, Is.Zero);
        }

        [TestCase(BattleValueStatus.AttackStateLimitExceeded)]
        [TestCase(BattleValueStatus.DefenseStateLimitExceeded)]
        [TestCase(BattleValueStatus.ReferenceUnavailable)]
        public void BattleFailureKeepsPartialCompositionButHasNoTotal(BattleValueStatus failure)
        {
            bool attackLimit = failure == BattleValueStatus.AttackStateLimitExceeded;
            var observation = World(7, layout: attackLimit ? "A........" : "AB.......",
                enemyCount: attackLimit ? 2 : 1, sharedAttribute: !attackLimit,
                actualTargetAll: failure == BattleValueStatus.ReferenceUnavailable);
            var evaluator = new PermanentItemEffectEvaluator(observation, battleSettings: new BattleValueSettings(
                allyAttackMaxStates: attackLimit ? 1 : 256, enemyBlockMaxStates: attackLimit ? 256 : 1));
            var result = evaluator.Evaluate(Use(observation, BoardTarget));
            Assert.That(result.Status, Is.EqualTo(ItemEffectEvaluationStatus.BattleUnavailable));
            Assert.That(result.BattleFailure.Status, Is.EqualTo(failure));
            Assert.That(result.BattleFailure.StateLimit, Is.EqualTo(1));
            Assert.That(result.CompositionActionValue, Is.EqualTo(8));
            Assert.That(result.EffectValue, Is.Null);
        }

        [Test]
        public void DistinguishesFailureStatusesAndNeedsNoStrategyFallback()
        {
            var observation = World(6, missingStats: true);
            var preview = Use(observation, BoardTarget);
            Assert.That(new PermanentItemEffectEvaluator(observation).Evaluate(preview).Status,
                Is.EqualTo(ItemEffectEvaluationStatus.PublicStatsUnavailable));
            Assert.That(new PermanentItemEffectEvaluator(World(6)).Evaluate(preview).Status,
                Is.EqualTo(ItemEffectEvaluationStatus.DifferentObservation));

            var ordinary = World(7);
            var evaluator = new PermanentItemEffectEvaluator(ordinary);
            Assert.That(evaluator.Evaluate(Use(ordinary, HandTarget)).Status, Is.EqualTo(ItemEffectEvaluationStatus.Illegal));
            Assert.That(evaluator.Evaluate(null).Status, Is.EqualTo(ItemEffectEvaluationStatus.InvalidPreview));
            var temporary = World(1);
            Assert.That(new PermanentItemEffectEvaluator(temporary).Evaluate(Use(temporary, BoardTarget)).Status,
                Is.EqualTo(ItemEffectEvaluationStatus.UnsupportedItem));
            Assert.That(new HpValueEvaluator().EvaluateItem(Use(temporary, BoardTarget)).Status,
                Is.EqualTo(ItemEffectEvaluationStatus.UnsupportedItem));

            var noEnemyData = World(7, enemyCount: 0);
            Assert.That(new PermanentItemEffectEvaluator(noEnemyData).Evaluate(Use(noEnemyData, BoardTarget)).BattleFailure.Status,
                Is.EqualTo(BattleValueStatus.EnemyContextUnavailable));
        }

        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(1, true)]
        public void StarTargetsRejectedByPreviewAreNotEvaluated(int star, bool temporary)
        {
            var observation = World(6, star: star, temporaryStar: temporary);
            var result = new PermanentItemEffectEvaluator(observation).Evaluate(Use(observation, BoardTarget));
            Assert.That(result.Status, Is.EqualTo(ItemEffectEvaluationStatus.Illegal));
            Assert.That(result.EffectValue, Is.Null);
        }

        [Test]
        public void CacheUsesTheSamePublicStateAndDoesNotChangeLegacyEvaluators()
        {
            var observation = World(7, enemyHp: 8, temporary: true);
            var preview = Use(observation, BoardTarget);
            string before = SimulationStateFingerprint.Create(observation);
            var legacy = new IntegratedCompositionValueEvaluator(observation).Baseline.Value;
            var legacyBattle = BattleValueEvaluator.FromObservation(observation).EvaluateCurrent().RawCorrection;
            var evaluator = new PermanentItemEffectEvaluator(observation);
            var result = evaluator.Evaluate(preview);
            var repeated = evaluator.Evaluate(preview);
            var uncached = new PermanentItemEffectEvaluator(observation, useCache: false).Evaluate(preview);
            Assert.That(evaluator.BattleRequests, Is.EqualTo(3));
            Assert.That(repeated.Composition.Value.Delta, Is.EqualTo(uncached.Composition.Value.Delta));
            Assert.That(result.EffectValue, Is.EqualTo(uncached.EffectValue));
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(before));
            Assert.That(new IntegratedCompositionValueEvaluator(observation).Baseline.Value, Is.EqualTo(legacy));
            Assert.That(BattleValueEvaluator.FromObservation(observation).EvaluateCurrent().RawCorrection, Is.EqualTo(legacyBattle));
        }

        private static ItemPreviewResult Use(GameObservation observation, GameActionTarget target) =>
            ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Hand, target, 0);

        private static GameActionTarget Target(ItemUseTargetKind kind, int slot) => new GameActionTarget(kind, slot, 0, true);

        internal static GameObservation World(int item, string layout = "A........", string units = "",
            int cost = 1, int star = 1, int[] reserveStars = null, bool temporary = false,
            bool temporaryStar = false, bool eventPending = false, bool missingStats = false, bool poorMean = false,
            int enemyHp = 20, int enemyCount = 1, bool sharedAttribute = false, bool actualTargetAll = false,
            StageChoice choice = StageChoice.SelectA, int missingStatsCost = 0,
            EventItemStatus eventStatus = EventItemStatus.Available, ItemDefinition eventDefinition = null,
            CharacterStatOption[] costOneStarTwo = null)
        {
            var members = UnitMoveBoardFixture.Board(layout, star);
            var roster = UnitMoveBoardFixture.Definitions(units, members)
                .Select(def => new CharacterDefinition(def.DefinitionId, def.CharacterName, 1,
                    sharedAttribute ? new[] { "shared" } : null, def.Units)).ToArray();
            var configuration = new StageProgressConfig(4, 4, eventPending ? 2 : 1, 4, 100, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var stage = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(configuration), configuration).NextState;
            var random = new SimulationRandomStreams(42);
            if (eventPending)
            {
                stage = StageProgressRules.BeginBattle(stage).NextState;
                stage = StageProgressRules.ResolveBattle(stage, configuration, true).NextState;
                stage = StageProgressRules.CompletePendingReward(stage, configuration, random.Event.CaptureState()).NextState;
            }
            else stage = StageSelectionRules.Select(stage, configuration, choice).NextState;

            var stats = new CharacterStatDefinition(new[] { 1, 5 }.Select(runCost => new CharacterCostStats(runCost,
                new[] { new CharacterStatOption(3, 1, 1) }, missingStats || missingStatsCost == runCost ? null : poorMean
                    ? new[] { new CharacterStatOption(0, 0, 1) }
                    : (runCost == 1 ? costOneStarTwo : null) ??
                        new[] { new CharacterStatOption(runCost == 1 ? 5 : 8, 2, 1),
                            new CharacterStatOption(runCost == 1 ? 7 : 10, 3, 3) },
                new[] { new CharacterStatOption(10, 4, 1) })));

            var owned = new List<CharacterInstance>();
            CharacterInstance Own(string definition, int rank)
            {
                var def = roster.Single(d => d.DefinitionId == definition);
                var character = new CharacterInstance("ally-" + owned.Count, def, definition == "A" ? cost : 1, rank);
                Assert.That(character.EnsureStats(def, stats, random.Stats), Is.True);
                if (temporaryStar && definition == "A")
                    Assert.That(character.TrySetTemporaryStar(rank, def, stats, random.Stats), Is.True);
                owned.Add(character);
                return character;
            }

            var board = BoardState.CreateStandard();
            foreach (var member in members)
            {
                var character = Own(member.DefinitionId, member.Star);
                if (temporary)
                {
                    character.TemporaryAttackBonus = 4;
                    character.TemporaryDefenseBonus = 1;
                }
                else if (sharedAttribute && member.DefinitionId == "B")
                    character.TemporaryDefenseBonus = 3;
                Assert.That(board.TryWithCharacterAt(member.SlotIndex, character.InstanceId, out board), Is.True);
            }
            string itemId = "Item00" + item;
            var itemDefinition = eventDefinition ?? new ItemDefinition(itemId, itemId, 3, string.Empty, ItemEffectTarget.OneAlly,
                item == 1 ? ItemEffectDuration.ThisBattle : ItemEffectDuration.Immediate,
                new[] { new ItemEffectDefinition(item == 6 ? ItemEffectKind.Star : ItemEffectKind.Attack,
                    item == 6 ? 2 : item == 7 ? 1 : 3) });
            var hand = HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0, HandSlotContent.Item(itemDefinition.DefinitionId), out hand), Is.True);
            int slot = 1;
            foreach (int rank in reserveStars ?? Array.Empty<int>())
                Assert.That(hand.TryWithSlot(slot++, HandSlotContent.Character(Own("A", rank).InstanceId), out hand), Is.True);

            string targetId = actualTargetAll ? "T005" : "T001";
            var enemy = new EnemyDefinition("enemy", "Enemy", 4, 0, enemyHp, null, null, null, targetId,
                new[] { targetId }, null, null);
            var enemyBoard = EnemyBoardState.CreateEmpty();
            for (int i = 0; i < enemyCount; i++)
            {
                Assert.That(EnemyInstanceFactory.TryCreate(enemy, stage.EncounterIndex, i, 0, 0, 0, 0, 0, 0,
                    out var instance, out _), Is.True);
                enemyBoard = enemyBoard.TryPlace(instance).NextState;
            }

            var built = ObservationBuilder.Build(new ObservationSource(stage, configuration,
                new EconomyState(20, 0, 4, 5, 20, 0, 1, 25, 30),
                new EconomyRulesConfiguration(4, 2, 1, new[] { 1, 2, 3, 4 }, 10, 1, 3, 3, 1, 0),
                board, hand, new CharacterOwnershipState(owned), roster, roster, roster, new[] { itemDefinition },
                eventPending ? null : new ShopState(ShopKind.Character, Array.Empty<ShopOffer>(), 0, 1, 1),
                new ItemEffectState(), eventPending ? new EventItemState(itemDefinition.DefinitionId, eventStatus) : null,
                enemyDefinitions: new[] { enemy }, enemyBoard: enemyBoard, characterStatSettings: stats,
                battleTargetDefinitions: new[] { new BattleTargetDefinition(targetId,
                    actualTargetAll ? BattleTargetFilter.FrontColumn : BattleTargetFilter.AllBoard,
                    actualTargetAll ? BattleTargetSelection.All : BattleTargetSelection.Uniform) }));
            Assert.That(built.Succeeded, Is.True, string.Join(";", built.Errors));
            return built.Observation;
        }
    }
}
