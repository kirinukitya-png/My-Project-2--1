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
    public sealed class TemporaryItemEffectEvaluatorTests
    {
        private static readonly ItemDefinition[] Items =
        {
            Item(1, 3, ItemEffectTarget.OneAlly, Effect(ItemEffectKind.Attack, 3)),
            Item(2, 2, ItemEffectTarget.AllEnemies, Effect(ItemEffectKind.Defense, -1)),
            Item(3, 3, ItemEffectTarget.OneAlly, Effect(ItemEffectKind.Attack, 2), Effect(ItemEffectKind.Defense, 2)),
            Item(4, 3, ItemEffectTarget.AllEnemies, Effect(ItemEffectKind.Damage, 2))
        };

        [TestCase("A", 1, 3, 20, 0, 0, 3, 6, 4, 4, 0, 3, 0, 2.40)]
        [TestCase("B", 1, 3, 5, 0, 0, 3, 5, 4, 0, 0, 2, 4, 3.52)]
        [TestCase("C", 2, 3, 20, 0, 0, 3, 4, 4, 4, 0, 1, 0, .80)]
        [TestCase("C stacked", 2, 3, 20, 0, 0, 4, 5, 4, 4, 0, 1, 0, .80)]
        [TestCase("D", 2, 3, 20, 4, 0, 0, 0, 4, 4, 0, 0, 0, 0)]
        [TestCase("E", 3, 3, 20, 0, 0, 3, 5, 4, 2, 0, 2, 2, 2.56)]
        [TestCase("F", 3, 3, 5, 0, 0, 3, 5, 4, 0, 0, 2, 4, 3.52)]
        [TestCase("G", 4, 3, 20, 0, 0, 3, 3, 4, 4, 2, 2, 0, 1.60)]
        [TestCase("H", 4, 3, 5, 0, 0, 3, 3, 4, 0, 2, 2, 4, 3.52)]
        [TestCase("I", 4, 1, 2, 0, 0, 1, 0, 4, 0, 2, 1, 4, 2.72)]
        [TestCase("J", 4, 5, 2, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0)]
        [TestCase("L", 3, 3, 20, 0, 3, 3, 5, 1, 0, 0, 2, 1, 2.08)]
        public void SingleEnemyFixtures(string label, int item, int attack, int hp, int defense, int block,
            int out0, int out1, int incoming0, int incoming1, int direct, int damage, int avoided, double score)
        {
            var observation = World(new[] { item }, attack: attack, hp: new[] { hp }, enemyDefense: defense,
                block: block, existingDefenseDown: label == "C stacked");
            var evaluator = new TemporaryItemEffectEvaluator(observation, includeDetails: true);
            var result = evaluator.Evaluate(Use(observation, item));
            AssertScore(result, out0, out1, incoming0, incoming1, direct, damage, avoided, (decimal)score);
            Assert.That(result.Before.Evaluation.Reference, Is.Null);
            if (item == 2)
                Assert.That(result.After.Enemies.Enemies.Single().InitialEnemy.Defense,
                    Is.EqualTo(defense - (label == "C stacked" ? 2 : 1)));
            if (label == "I" || label == "J")
            {
                Assert.That(result.After.InitialThreat, Is.Zero);
                Assert.That(result.After.PhysicalEnemyCount, Is.EqualTo(1));
                Assert.That(result.After.LivingEnemyCount, Is.Zero);
                Assert.That(result.BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.None));
                Assert.That(evaluator.RoundRequests, Is.EqualTo(1));
            }
            if (label == "L")
            {
                Assert.That(result.Before.BlockAbsorbed, Is.EqualTo(3));
                Assert.That(result.After.BlockAbsorbed, Is.EqualTo(2));
                Assert.That(result.After.RemainingBlock, Is.EqualTo(1));
            }
        }

        [Test]
        public void K_PartialDirectDefeatsPreserveFourPhysicalSlots()
        {
            var observation = World(new[] { 4 }, attack: 0, hp: new[] { 1, 2, 10, 0 });
            var result = new TemporaryItemEffectEvaluator(observation).Evaluate(Use(observation, 4));
            AssertScore(result, 0, 0, 12, 4, 5, 5, 8, 7.84m);
            Assert.That(result.Before.LivingEnemyCount, Is.EqualTo(3));
            Assert.That(result.After.LivingEnemyCount, Is.EqualTo(1));
            Assert.That(result.After.PhysicalEnemyCount, Is.EqualTo(4));
            Assert.That(result.DirectDefeats, Is.EqualTo(2));
            Assert.That(result.Before.InitialThreat, Is.EqualTo(12));
            Assert.That(result.After.InitialThreat, Is.EqualTo(4));
            Assert.That(result.After.Enemies.Enemies.Select(e => e.InitialEnemy.HP), Is.EqualTo(new[] { 0, 0, 8, 0 }));
        }

        [Test]
        public void M_StarTwoParticipantAttacksInBothUnitsAndTemporaryDefenseDropIsRecomputed()
        {
            var observation = World(new[] { 1 }, layout: "AB.C.....", units: "AB,AC", star: 2,
                enemyDefense: 7, gaps: new[] { 1 });
            var result = new TemporaryItemEffectEvaluator(observation, includeDetails: true).Evaluate(Use(observation, 1));
            AssertScore(result, 1, 4, 0, 0, 0, 3, 0, 2.40m);
            Assert.That(result.Before.Evaluation.Current.Attack.ExpectedUnitMoves, Is.EqualTo(2));
            Assert.That(result.Before.Evaluation.Current.Attack.ExpectedNormalAttacks, Is.Zero);
            Assert.That(result.Before.Evaluation.Current.Attack.ExpectedDefenseReduction, Is.EqualTo(2));
            Assert.That(result.After.Evaluation.Current.Attack.ExpectedDefenseReduction, Is.Zero);
            Assert.That(result.After.Enemies.Enemies.Single().InitialEnemy.Defense, Is.EqualTo(7));
        }

        [Test]
        public void N_SharedBlockUsesJointSurvivalAcrossBothAllyAttacks()
        {
            // Before: two attacks choose the same enemy in 2/4 branches (1 survivor, 1 incoming)
            // and different enemies in 2/4 (2 survivors, 5 incoming). After: both are killed.
            var observation = World(new[] { 4 }, attack: 2, layout: "AB.......", hp: new[] { 4, 4 }, block: 3);
            var result = new TemporaryItemEffectEvaluator(observation, includeDetails: true).Evaluate(Use(observation, 4));
            AssertScore(result, 4, 4, 3, 0, 4, 4, 3, 4.64m);
            Assert.That(result.Before.Evaluation.Current.Attack.Outcomes
                .Select(outcome => outcome.Probability).OrderBy(p => p), Is.EqualTo(new[] { .25m, .25m, .50m }));
            Assert.That(result.Before.Evaluation.Current.Defense.Counters.SurvivalMasks, Is.EqualTo(3));
            Assert.That(result.After.ExpectedDefeats, Is.EqualTo(2));
        }

        [Test]
        public void SettingsDisableRequestsOrOnlySuppressDefenseValue()
        {
            var observation = World(new[] { 3 });
            var preview = Use(observation, 3);
            var disabled = new TemporaryItemEffectEvaluator(observation, new TemporaryItemEffectSettings(0));
            Assert.That(disabled.Evaluate(preview).BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.Disabled));
            Assert.That(disabled.Evaluate(preview).EffectValue, Is.Zero);
            Assert.That(disabled.RoundRequests, Is.Zero);
            var betaZero = new TemporaryItemEffectEvaluator(observation, new TemporaryItemEffectSettings(beta: 0));
            Assert.That(betaZero.Evaluate(preview).EffectValue, Is.EqualTo(1.60m));
            Assert.That(betaZero.Evaluate(preview).AvoidedIncoming, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporaryItemEffectSettings(kappa: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporaryItemEffectSettings(beta: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporaryItemEffectSettings(attackStateLimit: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporaryItemEffectSettings(defenseStateLimit: 0));
        }

        [Test]
        public void PreviewTemporaryStatsApplyOnceAndPermanentStatsStayUnchanged()
        {
            var observation = World(new[] { 3 }, temporaryAttack: 2, temporaryDefense: 1);
            var preview = Use(observation, 3);
            var result = new TemporaryItemEffectEvaluator(observation).Evaluate(preview);
            AssertScore(result, 5, 7, 3, 1, 0, 2, 2, 2.56m);
            Assert.That(preview.After.Character.Attack, Is.EqualTo(3));
            Assert.That(preview.After.Character.Defense, Is.Zero);
            Assert.That(result.After.Board.Members.Single().EffectiveAttack, Is.EqualTo(7));
            Assert.That(result.After.Board.Members.Single().EffectiveDefense, Is.EqualTo(3));
        }

        [Test]
        public void EnemylessAndAlreadyZeroAreSuccessfulDistinctSkips()
        {
            var enemyless = World(new[] { 1 }, enemyless: true);
            var zero = World(new[] { 4 }, hp: new[] { 0 });
            var first = new TemporaryItemEffectEvaluator(enemyless);
            var second = new TemporaryItemEffectEvaluator(zero);
            var a = first.Evaluate(Use(enemyless, 1));
            var b = second.Evaluate(Use(zero, 4));
            Assert.That(a.BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.Enemyless));
            Assert.That(b.BattleSkipReason, Is.EqualTo(ItemBattleSkipReason.AllEnemiesAtZeroHp));
            AssertScore(a, 0, 0, 0, 0, 0, 0, 0, 0);
            AssertScore(b, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.That(first.RoundRequests + second.RoundRequests, Is.Zero);
        }

        [Test]
        public void MissingContextAndNeededTargetFailButDefeatedOrGapEnemiesDoNotNeedTarget()
        {
            var noData = World(new[] { 1 }, hp: Array.Empty<int>());
            AssertFailure(new TemporaryItemEffectEvaluator(noData).Evaluate(Use(noData, 1)),
                TemporaryItemBattleSide.Before, BattleValueStatus.EnemyContextUnavailable);
            var missingTarget = World(new[] { 1 }, missingTargetSlot: 0);
            AssertFailure(new TemporaryItemEffectEvaluator(missingTarget).Evaluate(Use(missingTarget, 1)),
                TemporaryItemBattleSide.Before, BattleValueStatus.TargetUnavailable);

            var mixed = World(new[] { 4 }, attack: 0, hp: new[] { 0, 4, 20 },
                gaps: new[] { 0, 0, 1 }, missingTargetSlot: 0);
            AssertScore(new TemporaryItemEffectEvaluator(mixed).Evaluate(Use(mixed, 4)),
                0, 0, 4, 4, 4, 4, 0, 3.20m);
            var slow = World(new[] { 4 }, attack: 0, hp: new[] { 2 }, gaps: new[] { 1 }, missingTargetSlot: 0);
            AssertScore(new TemporaryItemEffectEvaluator(slow).Evaluate(Use(slow, 4)),
                0, 0, 0, 0, 2, 2, 0, 1.60m);
        }

        [Test]
        public void StateLimitsReportFailedSideAndDoNotProducePartialEffectValue()
        {
            var settings = new TemporaryItemEffectSettings(attackStateLimit: 1);
            var beforeFailure = World(new[] { 4 }, hp: new[] { 2, 2 });
            var first = new TemporaryItemEffectEvaluator(beforeFailure, settings).Evaluate(Use(beforeFailure, 4));
            AssertFailure(first, TemporaryItemBattleSide.Before, BattleValueStatus.AttackStateLimitExceeded);
            Assert.That(first.BattleFailure.StateLimit, Is.EqualTo(1));

            var afterFailure = World(new[] { 1, 2 }, hp: new[] { 20, 20 }, enemyDefense: 4);
            var evaluator = new TemporaryItemEffectEvaluator(afterFailure, settings);
            var second = evaluator.Evaluate(Use(afterFailure, 1));
            AssertFailure(second, TemporaryItemBattleSide.After, BattleValueStatus.AttackStateLimitExceeded);
            Assert.That(second.Before.Out, Is.Zero);
            Assert.That(second.BattleFailure.StateLimit, Is.EqualTo(1));
            Assert.That(evaluator.Evaluate(Use(afterFailure, 2, 1)).EffectValue, Is.Zero);

            var defense = World(new[] { 3 }, layout: "AB.......", block: 3, differentDefenses: true);
            var third = new TemporaryItemEffectEvaluator(defense, new TemporaryItemEffectSettings(defenseStateLimit: 1))
                .Evaluate(Use(defense, 3));
            AssertFailure(third, TemporaryItemBattleSide.Before, BattleValueStatus.DefenseStateLimitExceeded);
            Assert.That(third.BattleFailure.StateLimit, Is.EqualTo(1));
        }

        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void OtherItemTypesAreUnsupported(int item)
        {
            var observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(item));
            var preview = ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Hand,
                Target(item == 5 ? ItemUseTargetKind.PlayerArea : ItemUseTargetKind.BoardCharacter, 0), 0);
            var result = new TemporaryItemEffectEvaluator(observation).Evaluate(preview);
            Assert.That(result.Status, Is.EqualTo(ItemEffectEvaluationStatus.UnsupportedItem));
            Assert.That(result.EffectValue, Is.Null);
        }

        [Test]
        public void InvalidAndForeignPreviewsStayInvalidEvenWhenDisabled()
        {
            var observation = World(new[] { 1 });
            var evaluator = new TemporaryItemEffectEvaluator(observation, new TemporaryItemEffectSettings(0));
            var illegal = evaluator.Evaluate(Use(observation, 1, target: 8));
            Assert.That(illegal.Status, Is.EqualTo(ItemEffectEvaluationStatus.Illegal));
            var foreign = evaluator.Evaluate(Use(World(new[] { 1 }), 1));
            Assert.That(foreign.Status, Is.EqualTo(ItemEffectEvaluationStatus.DifferentObservation));
            Assert.That(evaluator.RoundRequests, Is.Zero);
        }

        [TestCase(false, 16)]
        [TestCase(true, 40)]
        public void CapacityDoesNotEnterEffectValue(bool full, int capacity)
        {
            var observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(1,
                fullHand: full, extraItem: 1, extraReserve: !full, choice: StageChoice.SelectB));
            var result = new TemporaryItemEffectEvaluator(observation).Evaluate(Use(observation, 1));
            Assert.That(result.EffectValue, Is.Zero);
            Assert.That(result.CapacityActionValue, Is.EqualTo(capacity));
            Assert.That(result.Preview.ConsumedHandSlot, Is.Zero);
        }

        [Test]
        public void PurchasedUseKeepsGoldDifferenceAndSessionState()
        {
            var session = ItemEffectPreviewTests.Session(1, choice: StageChoice.SelectB);
            var observation = ItemEffectPreviewTests.Observe(session);
            var state = session.CaptureCheckpoint();
            var before = SimulationStateFingerprint.Create(observation);
            var strategyRandom = StrategyRandomSource.Create(123, "HeuristicStrategy", "16");
            var strategyRngBefore = strategyRandom.CaptureState();
            var purchase = ItemEffectPreview.PreviewPurchase(observation, observation.Shop.Offers.Single().OfferId, 4);
            var preview = ItemEffectPreview.PreviewUseAfterPurchase(observation, purchase,
                Target(ItemUseTargetKind.BoardCharacter, 0));
            var result = new TemporaryItemEffectEvaluator(observation).Evaluate(preview);
            Assert.That(result.EffectValue, Is.Zero);
            Assert.That(result.GoldDelta, Is.Zero);
            Assert.That(result.GoldDeltaIncludingPurchase, Is.EqualTo(-3));
            Assert.That(result.CapacityDeltaIncludingPurchase, Is.Zero);
            Assert.That(session.CaptureCheckpoint(), Is.SameAs(state));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(state.RandomState));
            Assert.That(strategyRandom.CaptureState(), Is.EqualTo(strategyRngBefore));
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(before));
        }

        [Test]
        public void CacheAndDetailsDoNotMixEnemyContextsOrDependOnOrder()
        {
            var observation = World(new[] { 2, 4, 1 }, layout: "AB.......", hp: new[] { 6, 20 });
            var previews = new[] { Use(observation, 2), Use(observation, 4, 1),
                Use(observation, 1, 2), Use(observation, 1, 2, 1) };
            var cache = new TemporaryItemEffectEvaluator(observation);
            var expected = previews.Select(p => cache.Evaluate(p)).ToArray();
            Assert.That(cache.RoundRequests, Is.EqualTo(5));
            var uncached = new TemporaryItemEffectEvaluator(observation, useCache: false, includeDetails: true);
            for (int i = previews.Length - 1; i >= 0; i--)
            {
                var result = uncached.Evaluate(previews[i]);
                Assert.That(result.EffectValue, Is.EqualTo(expected[i].EffectValue));
                Assert.That(result.After.Out, Is.EqualTo(expected[i].After.Out));
                Assert.That(cache.Evaluate(previews[i]).EffectValue, Is.EqualTo(expected[i].EffectValue));
            }
            Assert.That(expected[0].After.Enemies.Enemies.Select(e => e.InitialEnemy.Defense), Is.EqualTo(new[] { -1, -1 }));
            Assert.That(expected[1].After.Enemies.Enemies.Select(e => e.InitialEnemy.HP), Is.EqualTo(new[] { 4, 18 }));
            Assert.That(expected[1].After.Enemies.Enemies.Select(e => e.InitialEnemy.Defense), Is.EqualTo(new[] { 0, 0 }));
            Assert.That(BattleValueEvaluator.FromObservation(observation).EvaluateCurrent().Current.Attack.ExpectedEffectiveDamage,
                Is.EqualTo(expected[0].Before.Out));
            Assert.That(new IntegratedCompositionValueEvaluator(observation).Baseline.Value.BoardCombat, Is.EqualTo(6));
        }

        private static void AssertScore(TemporaryItemEffectEvaluation result, int out0, int out1, int incoming0,
            int incoming1, int direct, int damage, int avoided, decimal value)
        {
            Assert.That(result.Status, Is.EqualTo(ItemEffectEvaluationStatus.Evaluated), result.BattleFailure?.Status.ToString());
            Assert.That(result.Before.Out, Is.EqualTo(out0));
            Assert.That(result.After.Out, Is.EqualTo(out1));
            Assert.That(result.Before.Incoming, Is.EqualTo(incoming0));
            Assert.That(result.After.Incoming, Is.EqualTo(incoming1));
            Assert.That(result.DirectHpLoss, Is.EqualTo(direct));
            Assert.That(result.DamageDelta, Is.EqualTo(damage));
            Assert.That(result.AvoidedIncoming, Is.EqualTo(avoided));
            Assert.That(result.DamageValue, Is.EqualTo(.8m * damage));
            Assert.That(result.DefenseValue, Is.EqualTo(.48m * avoided));
            Assert.That(result.EffectValue, Is.EqualTo(value));
        }

        private static void AssertFailure(TemporaryItemEffectEvaluation result, TemporaryItemBattleSide side, BattleValueStatus failure)
        {
            Assert.That(result.Status, Is.EqualTo(ItemEffectEvaluationStatus.BattleUnavailable));
            Assert.That(result.FailedSide, Is.EqualTo(side));
            Assert.That(result.BattleFailure.Status, Is.EqualTo(failure));
            Assert.That(result.EffectValue, Is.Null);
        }

        private static ItemPreviewResult Use(GameObservation observation, int item, int handSlot = 0, int target = 0) =>
            ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Hand,
                item == 2 || item == 4 ? Target(ItemUseTargetKind.EnemyArea, -1) :
                    Target(ItemUseTargetKind.BoardCharacter, target), handSlot);

        private static GameActionTarget Target(ItemUseTargetKind kind, int slot) => new GameActionTarget(kind, slot, 0, true);

        internal static GameObservation World(int[] items, int attack = 3, int[] hp = null, int enemyDefense = 0,
            int block = 0, string layout = "A........", string units = "", int star = 1, int[] gaps = null,
            bool enemyless = false, bool existingDefenseDown = false, int missingTargetSlot = -1,
            int temporaryAttack = 0, int temporaryDefense = 0, bool differentDefenses = false)
        {
            hp ??= new[] { 20 };
            var members = UnitMoveBoardFixture.Board(layout, star);
            var roster = UnitMoveBoardFixture.Definitions(units, members);
            var config = new StageProgressConfig(4, 4, 1, 4, 0, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var pending = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(config), config).NextState;
            var stage = StageSelectionRules.Select(pending, config, enemyless ? StageChoice.SelectD : StageChoice.SelectA).NextState;
            var random = new SimulationRandomStreams(124);
            var stats = new CharacterStatDefinition(new[] { new CharacterCostStats(1,
                new[] { new CharacterStatOption(attack, 0, 1) }, new[] { new CharacterStatOption(attack, 0, 1) }, null) });
            var board = BoardState.CreateStandard();
            var owned = new List<CharacterInstance>();
            foreach (var member in members)
            {
                var definition = roster.Single(d => d.DefinitionId == member.DefinitionId);
                var instance = new CharacterInstance(member.DefinitionId, definition, 1, member.Star);
                Assert.That(instance.EnsureStats(definition, stats, random.Stats), Is.True);
                instance.TemporaryAttackBonus = temporaryAttack;
                instance.TemporaryDefenseBonus = differentDefenses && member.DefinitionId == "B" ? 4 : temporaryDefense;
                owned.Add(instance);
                Assert.That(board.TryWithCharacterAt(member.SlotIndex, instance.InstanceId, out board), Is.True);
            }
            var ownership = new CharacterOwnershipState(owned);
            var economy = new EconomyState(15, 0, 4, 5, 15, 0, 1, 25, 30);
            var effects = new ItemEffectState();
            var hand = HandState.CreateStandard();
            if (block != 0)
            {
                var blockItem = Item(9, 0, ItemEffectTarget.Player, Effect(ItemEffectKind.Block, block));
                Assert.That(hand.TryWithSlot(0, HandSlotContent.Item(blockItem.DefinitionId), out hand), Is.True);
                var applied = ItemEffectRules.TryUseHandItem(blockItem, 0, ItemUseTarget.PlayerArea(0), board, hand,
                    ownership, economy, stage, effects, roster, stats, random.Stats.CaptureState());
                Assert.That(applied.Succeeded, Is.True, applied.Failure.ToString());
                effects = applied.NextEffectState;
                hand = applied.NextHand;
            }
            var itemDefs = items.Select(i => Items[i - 1]).ToArray();
            for (int i = 0; i < itemDefs.Length; i++)
                Assert.That(hand.TryWithSlot(i, HandSlotContent.Item(itemDefs[i].DefinitionId), out hand), Is.True);

            var enemies = hp.Select((health, slot) =>
                new EnemyDefinition("enemy-" + slot, "Enemy " + slot, 4, enemyDefense, health, null, null, null,
                    "T001", null, null, null)).ToArray();
            var enemyBoard = EnemyBoardState.CreateEmpty();
            var preparation = new EnemyPreparationState();
            for (int i = 0; i < enemies.Length; i++)
            {
                if (i == missingTargetSlot)
                    enemies[i] = new EnemyDefinition("enemy-" + i, "Enemy " + i, 4, enemyDefense, hp[i], null,
                        null, null, "T002", new[] { "T002" }, null, null);
                Assert.That(EnemyInstanceFactory.TryCreate(enemies[i], stage.EncounterIndex, i, 0,
                    0, 0, gaps?[i] ?? 0, 0, 0, out var enemy, out _), Is.True);
                enemyBoard = enemyBoard.TryPlace(enemy).NextState;
            }
            if (existingDefenseDown)
            {
                var formation = new EnemyFormationDefinition("formation", Enumerable.Range(0, 6)
                    .Select(i => new EnemyFormationSlotDefinition(i < enemies.Length ? enemies[i].DefinitionId : null, 0, 0)));
                var applied = EnemyItemEffectRules.TryUseHandItem(Items[1], 0, formation, preparation, board, hand,
                    ownership, economy, stage, effects, roster, stats, random.Stats.CaptureState());
                Assert.That(applied.Succeeded, Is.True, applied.Failure.ToString());
                preparation = applied.NextEnemyPreparationState;
            }

            var built = ObservationBuilder.Build(new ObservationSource(stage, config, economy,
                new EconomyRulesConfiguration(4, 2, 1, new[] { 1, 2, 3, 4 }, 10, 1, 3, 3, 1, 0),
                board, hand, ownership, roster, roster, roster, itemDefs,
                new ShopState(ShopKind.Character, Array.Empty<ShopOffer>(), 0, 1, 1), effects,
                enemyDefinitions: enemies, enemyBoard: enemyBoard, enemyPreparation: preparation, characterStatSettings: stats,
                battleTargetDefinitions: enemies.Length == 1 && missingTargetSlot == 0 ? null :
                    new[] { new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard, BattleTargetSelection.Uniform) }));
            Assert.That(built.Succeeded, Is.True, string.Join(";", built.Errors));
            return built.Observation;
        }

        private static ItemDefinition Item(int number, int price, ItemEffectTarget target, params ItemEffectDefinition[] effects)
        {
            string id = "Item00" + number;
            return new ItemDefinition(id, id, price, string.Empty, target, ItemEffectDuration.ThisBattle, effects);
        }

        private static ItemEffectDefinition Effect(ItemEffectKind kind, int value) => new ItemEffectDefinition(kind, value);
    }
}
