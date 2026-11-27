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
    public sealed class ItemTransactionValueEvaluatorTests
    {
        private static readonly GameActionTarget Ally = new GameActionTarget(ItemUseTargetKind.BoardCharacter, 0, 0, true);
        private static readonly GameActionTarget Player = new GameActionTarget(ItemUseTargetKind.PlayerArea, 0, 0, true);
        private static readonly GameActionTarget Enemies = new GameActionTarget(ItemUseTargetKind.EnemyArea, -1, 0, true);

        private static readonly ItemDefinition[] Items =
        {
            Define(1, 3, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle, Effect(ItemEffectKind.Attack, 3)),
            Define(2, 2, ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle, Effect(ItemEffectKind.Defense, -1)),
            Define(3, 3, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle, Effect(ItemEffectKind.Attack, 2), Effect(ItemEffectKind.Defense, 2)),
            Define(4, 3, ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle, Effect(ItemEffectKind.Damage, 2)),
            Define(5, 4, ItemEffectTarget.Player, ItemEffectDuration.Immediate, Effect(ItemEffectKind.Damage, -4)),
            Define(6, 10, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate, Effect(ItemEffectKind.Star, 2)),
            Define(7, 5, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate, Effect(ItemEffectKind.Attack, 1))
        };

        [TestCase(1, 2.95, false)]
        [TestCase(2, 2.80, true)]
        [TestCase(3, 3.45, false)]
        [TestCase(4, 3.45, true)]
        public void EnemylessShopBuysFutureOptionAndSeparatesLegalZeroFromNoTarget(int item, double score, bool noTarget)
        {
            foreach (var occupied in new[] { 0, 3, 4 })
            {
                var observation = World(offer: item, occupied: occupied);
                var purchase = Evaluator(observation).EvaluatePurchase("offer", occupied, 2);
                decimal capacity = occupied == 0 ? 0 : occupied == 3 ? -16 : -40;
                Assert.That(purchase.Hold.Score, Is.EqualTo((decimal)score + capacity));
                Assert.That(purchase.Hold.Economy.Value.Eligibility, Is.EqualTo(InterestRewardEligibility.Ineligible));
                Assert.That(purchase.Hold.Economy.Value.WeightedInterestDelta, Is.Zero);
                Assert.That(purchase.Hold.Item.FutureWindow, Is.EqualTo(ItemFutureWindow.FutureEncounterAvailable));
                Assert.That(purchase.Targets.Status, Is.EqualTo(noTarget
                    ? ItemTargetEvaluationStatus.NoLegalTargets : ItemTargetEvaluationStatus.AllEvaluated));
                Assert.That(purchase.ImmediateUseExclusion, Is.EqualTo(noTarget
                    ? ItemCandidateExclusion.NoLegalUseTarget : ItemCandidateExclusion.NonPositiveEffect));
            }
        }

        [TestCase(null, ItemFutureWindow.FutureEncounterUnknown)]
        [TestCase(1, ItemFutureWindow.FinalEncounter)]
        public void FutureWindowOffKeepsCurrentValueAndRemovesFutureValue(int? total, ItemFutureWindow expected)
        {
            var observation = World(offer: 1, held: new[] { 5 }, hp: 5);
            var evaluator = new ItemTransactionValueEvaluator(observation, total);
            Assert.That(evaluator.Inventory.FutureWindow, Is.EqualTo(expected));
            Assert.That(evaluator.Inventory.EvaluateHand(0).Value, Is.EqualTo(8));
            Assert.That(evaluator.EvaluatePurchase("offer", 1, 2).Hold.Score, Is.EqualTo(-1.05m));
            Assert.That(evaluator.EvaluatePurchase("offer", 1, 2).Hold.Item.FutureValue, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemInventoryValueEvaluator(observation, 0));
        }

        [TestCase(5, 0, 16, 8, 6.60, 14.60, 13.80)]
        [TestCase(5, 3, 16, 8, -9.40, 14.60, 12.20)]
        [TestCase(5, 4, 16, 8, -33.40, 14.60, 9.80)]
        [TestCase(15, 0, 8, 4, 2.60, 6.60, 6.20)]
        [TestCase(15, 3, 8, 4, -13.40, 6.60, 4.60)]
        [TestCase(15, 4, 8, 4, -37.40, 6.60, 2.20)]
        [TestCase(25, 0, 4, 2, .60, 2.60, 2.40)]
        [TestCase(25, 3, 4, 2, -15.40, 2.60, .80)]
        [TestCase(25, 4, 4, 2, -39.40, 2.60, -1.60)]
        [TestCase(30, 0, 0, 2, .60, -1.40, -1.20)]
        [TestCase(30, 3, 0, 2, -15.40, -1.40, -2.80)]
        [TestCase(30, 4, 0, 2, -39.40, -1.40, -5.20)]
        public void TeaPurchaseReportsEachStage(int hp, int occupied, int w, int holding, double hold, double total, double discounted)
        {
            var result = Evaluator(World(offer: 5, occupied: occupied, gold: 12, hp: hp))
                .EvaluatePurchase("offer", occupied, 2);
            Assert.That(result.Hold.Item.Value, Is.EqualTo(holding));
            Assert.That(result.Hold.Score, Is.EqualTo((decimal)hold));
            Assert.That(result.Hold.EconomyDelta, Is.EqualTo(-1.40m));
            Assert.That(result.UseAfterPurchase.EffectValue, Is.EqualTo(w));
            Assert.That(result.UseAfterPurchase.GoldDelta, Is.Zero);
            Assert.That(result.TotalGoldDelta, Is.EqualTo(-4));
            Assert.That(result.Hold.OccupancyDelta, Is.EqualTo(1));
            Assert.That(result.UseAfterPurchase.OccupancyDelta, Is.EqualTo(-1));
            Assert.That(result.Hold.CapacityDelta, Is.EqualTo(-result.UseAfterPurchase.CapacityDelta));
            Assert.That(result.Hold.HoldingDelta, Is.EqualTo(-result.UseAfterPurchase.HoldingDelta));
            Assert.That(result.TotalCapacityDelta, Is.Zero);
            Assert.That(result.TotalHoldingDelta, Is.Zero);
            Assert.That(result.TotalScore, Is.EqualTo((decimal)total));
            Assert.That(result.DiscountedScore, Is.EqualTo((decimal)discounted));
            Assert.That(result.GuaranteesUse, Is.False);
            if (hp == 25 && occupied == 0)
            {
                Assert.That(result.DiscountedExceedsThreshold, Is.True);
                Assert.That(result.ImmediateUseExclusion, Is.EqualTo(ItemCandidateExclusion.ThresholdNotExceeded));
            }
            if (hp == 30)
                Assert.That(result.ImmediateUseExclusion, Is.EqualTo(ItemCandidateExclusion.NonPositiveEffect));
        }

        [TestCase(5, 16, 8)]
        [TestCase(15, 8, 4)]
        [TestCase(25, 4, 2)]
        [TestCase(30, 0, -2)]
        public void TeaUseAndSellSameSlotSeparateEffectAndDemand(int hp, int effect, int baseUse)
        {
            foreach (int occupied in new[] { 3, 4, 5 })
            {
                var evaluator = Evaluator(World(held: new[] { 5 }, occupied: occupied, hp: hp, gold: 12));
                var use = evaluator.EvaluateUse(0, Player);
                decimal released = occupied == 3 ? 0m : occupied == 4 ? 16m : 40m;
                Assert.That(use.Score, Is.EqualTo(baseUse + released));
                Assert.That(use.IsCandidate, Is.EqualTo(effect > 0 && use.Score > 2m));
                foreach (SpaceDemand demand in Enum.GetValues(typeof(SpaceDemand)))
                {
                    var sell = evaluator.EvaluateSell(0, demand);
                    Assert.That(sell.Score, Is.EqualTo(use.Score - effect));
                    Assert.That(sell.GoldDelta, Is.Zero);
                    Assert.That(sell.IsCandidate, Is.EqualTo(occupied == 5 && demand == SpaceDemand.ConfirmedDemand));
                    Assert.That(sell.Demand, Is.EqualTo(demand));
                }
            }
            Assert.That(ItemTransactionValueEvaluator.KeepScore, Is.Zero);
        }

        [TestCase(20, 2.40, -1.60)]
        [TestCase(5, 3.52, -.48)]
        [TestCase(0, 0, -4)]
        public void TemporaryUseRetainsRawScoreButNeverAddsFutureCarry(int hp, double effect, double baseUse)
        {
            foreach (var occupied in new[] { 3, 4, 5 })
            {
                var evaluator = Evaluator(World(held: new[] { 1 }, occupied: occupied, enemyHp: hp == 0 ? null : (int?)hp));
                var use = evaluator.EvaluateUse(0, Ally);
                Assert.That(use.EffectValue, Is.EqualTo((decimal)effect));
                Assert.That(use.Item.Value, Is.EqualTo(4));
                Assert.That(use.Score, Is.EqualTo((decimal)baseUse + (occupied == 3 ? 0 : occupied == 4 ? 16 : 40)));
                if (hp == 0) Assert.That(use.Exclusion, Is.EqualTo(ItemCandidateExclusion.NonPositiveEffect));
            }
        }

        [TestCase(2, 20, 4, 3, 0, 3.50)]
        [TestCase(2, 20, 0, 3, .80, 3.50)]
        [TestCase(4, 2, 0, 5, 0, 4.50)]
        [TestCase(4, 20, 0, 3, 1.60, 4.50)]
        [TestCase(4, 5, 0, 3, 3.52, 4.50)]
        [TestCase(4, 0, 0, 3, 0, 4.50)]
        public void EnemyAreaHoldingUsesActualA4Effect(int item, int hp, int defense, int attack, double effect, double holding)
        {
            var observation = TemporaryItemEffectEvaluatorTests.World(new[] { item }, attack: attack,
                hp: new[] { hp }, enemyDefense: defense);
            var evaluator = Evaluator(observation);
            var value = evaluator.Inventory.EvaluateHand(0);
            Assert.That(value.Value, Is.EqualTo((decimal)holding));
            Assert.That(value.BestEffect, Is.EqualTo((decimal)effect));
            Assert.That(value.LegalTargetCount, Is.EqualTo(1));
            Assert.That(evaluator.EvaluateUse(0, Enemies).EffectValue, Is.EqualTo((decimal)effect));
        }

        [Test]
        public void MultiEnemyAndStackedDefenseUseExistingProjection()
        {
            var multiple = TemporaryItemEffectEvaluatorTests.World(new[] { 4 }, attack: 0, hp: new[] { 1, 2, 10, 0 });
            Assert.That(Evaluator(multiple).Inventory.EvaluateHand(0).BestEffect, Is.EqualTo(7.84m));
            var stacked = TemporaryItemEffectEvaluatorTests.World(new[] { 2 }, existingDefenseDown: true);
            var result = Evaluator(stacked).EvaluateUse(0, Enemies);
            Assert.That(result.EffectValue, Is.EqualTo(.80m));
            Assert.That(result.Effect.Temporary.After.Enemies.Enemies.Single().InitialEnemy.Defense, Is.EqualTo(-2));
        }

        [TestCase(6, 1, 3, 50.4, 24, 4.50, 46.90, 42.66)]
        [TestCase(6, 1, 4, 50.4, 24, -19.50, 46.90, 40.26)]
        [TestCase(6, 5, 3, 74.4, 24, 4.50, 70.90, 64.26)]
        [TestCase(6, 5, 4, 74.4, 24, -19.50, 70.90, 61.86)]
        [TestCase(7, 1, 3, 8, 4, -13.75, 6.25, 4.25)]
        [TestCase(7, 1, 4, 8, 4, -37.75, 6.25, 1.85)]
        public void PermanentPurchasesUseRealEnemylessA4Observation(int item, int cost, int occupied, double effect,
            int holding, double hold, double total, double discounted)
        {
            var purchase = Evaluator(World(offer: item, occupied: occupied, cost: cost))
                .EvaluatePurchase("offer", occupied, 2);
            Assert.That(purchase.Hold.Item.LegalTargetCount, Is.EqualTo(1));
            Assert.That(purchase.Hold.Item.Value, Is.EqualTo(holding));
            Assert.That(purchase.UseAfterPurchase.EffectValue, Is.EqualTo((decimal)effect));
            Assert.That(purchase.Hold.Score, Is.EqualTo((decimal)hold));
            Assert.That(purchase.TotalScore, Is.EqualTo((decimal)total));
            Assert.That(purchase.DiscountedScore, Is.EqualTo((decimal)discounted));
        }

        [Test]
        public void NegativeStarEffectCanHavePositiveRawScoreButNeverQualifies()
        {
            var evaluator = Evaluator(World(held: new[] { 6 }, occupied: 5, poorMean: true, reserveStars: new[] { 1, 2, 2, 3 }));
            var use = evaluator.EvaluateUse(0, Ally);
            Assert.That(use.Item.Value, Is.EqualTo(3));
            Assert.That(use.EffectValue, Is.EqualTo(-30.80m));
            Assert.That(use.Score, Is.EqualTo(6.20m));
            Assert.That(use.Exclusion, Is.EqualTo(ItemCandidateExclusion.NonPositiveEffect));
            Assert.That(evaluator.EvaluateSell(0, SpaceDemand.ConfirmedDemand).Score, Is.EqualTo(37));
        }

        [TestCase(1, 8, -8)]
        [TestCase(2, 10, -2)]
        [TestCase(3, 12, -2)]
        public void DuplicateTeaRemovalUsesMarginalValue(int copies, int aggregate, int difference)
        {
            var evaluator = Evaluator(World(held: Enumerable.Repeat(5, copies).ToArray(), hp: 5));
            Assert.That(evaluator.Inventory.EvaluateInventory().Value, Is.EqualTo(aggregate));
            Assert.That(evaluator.EvaluateUse(0, Player).HoldingDelta, Is.EqualTo(difference));
            Assert.That(evaluator.EvaluateSell(copies - 1, SpaceDemand.NoDemand).HoldingDelta, Is.EqualTo(difference));
        }

        [TestCase(1, 3, 1.5)]
        [TestCase(3, 1, 1)]
        public void MixedBuffGroupPromotesNextMaximumAndOtherGroupsAreIndependent(int held, int offered, double increase)
        {
            var evaluator = Evaluator(World(held: new[] { held, 2 }, offer: offered));
            var buy = evaluator.EvaluatePurchase("offer", 2, 2);
            Assert.That(buy.Hold.HoldingDelta, Is.EqualTo((decimal)increase));
            Assert.That(buy.Hold.After.Value, Is.EqualTo(9));
            Assert.That(buy.Hold.After.EvaluatedGroups["Item001"], Is.EqualTo(5.50m));
            var mixed = Evaluator(World(held: new[] { 1, 3, 2 }));
            Assert.That(mixed.EvaluateSell(1, SpaceDemand.NoDemand).HoldingDelta, Is.EqualTo(-1.50m));
            Assert.That(mixed.EvaluateSell(0, SpaceDemand.NoDemand).HoldingDelta, Is.EqualTo(-1m));
        }

        [Test]
        public void InventoryContextChangesOnlyAfterNewObservation()
        {
            var session = ItemEffectPreviewTests.Session(5, extraItem: 5, hp: 25);
            var before = ItemEffectPreviewTests.Observe(session);
            var evaluator = Evaluator(before);
            var use = evaluator.EvaluateUse(0, Player);
            Assert.That(use.Before.Value, Is.EqualTo(2.50m));
            Assert.That(use.After.Value, Is.EqualTo(2m));
            Assert.That(use.After.Items.Single().BestEffect, Is.EqualTo(4));
            ItemEffectPreviewTests.Execute(session, new UseHandItemAction(new ActionId("heal"), 0, Player));
            var next = ItemEffectPreviewTests.Observe(session);
            Assert.That(Evaluator(next).Inventory.EvaluateHand(2).BestEffect, Is.EqualTo(1));
        }

        [TestCase(0, false, false)]
        [TestCase(1, true, false)]
        [TestCase(2, true, true)]
        public void ActionBudgetSeparatesRawScoresFromCandidates(int remaining, bool hold, bool immediate)
        {
            var result = Evaluator(World(offer: 5, hp: 5)).EvaluatePurchase("offer", 0, remaining);
            Assert.That(result.Hold.Score, Is.EqualTo(6.60m));
            Assert.That(result.Hold.IsCandidate, Is.EqualTo(hold));
            Assert.That(result.ImmediateUseEligible, Is.EqualTo(immediate));
        }

        [TestCase(2, false, 1, false)]
        [TestCase(2, false, .999, true)]
        [TestCase(1, true, 2, false)]
        [TestCase(1, true, 1.999, true)]
        public void UseRequiresStrictThreshold(int hpMissing, bool enemyless, double futureValue, bool candidate)
        {
            var settings = new ItemInventoryValueSettings(currentEffectRate: 0, futureValues: Future(5, (decimal)futureValue));
            var observation = World(held: new[] { 5 }, hp: enemyless ? 26 : 30 - hpMissing, enemyHp: enemyless ? null : (int?)20);
            var use = Evaluator(observation, new ItemTransactionValueSettings(inventory: settings)).EvaluateUse(0, Player);
            Assert.That(use.Score, Is.EqualTo((enemyless ? 4m : hpMissing) - (decimal)futureValue));
            Assert.That(use.IsCandidate, Is.EqualTo(candidate));
        }

        [Test]
        public void PurchaseLegalityAndEconomyBoundary()
        {
            var poor = Evaluator(World(offer: 6, gold: 9));
            Assert.That(poor.EvaluatePurchase("offer", 0, 2).Hold.Failure, Is.EqualTo(ItemPreviewFailure.InsufficientGold));
            Assert.That(poor.EvaluatePurchase("invalid", 0, 2).Hold.Score, Is.Null);
            var enough = Evaluator(World(offer: 6, gold: 12));
            Assert.That(enough.EvaluatePurchase("offer", 0, 2).Hold.EconomyDelta, Is.EqualTo(-5.60m));
            var full = Evaluator(World(offer: 5, occupied: 5));
            Assert.That(full.EvaluatePurchase("offer", 4, 2).Hold.Failure, Is.EqualTo(ItemPreviewFailure.HandSlotUnavailable));
            Assert.Throws<ArgumentOutOfRangeException>(() => enough.EvaluatePurchase("offer", 0, -1));
        }

        [Test]
        public void UnavailableHoldingsPropagateWithoutErasingKnownTransactionParts()
        {
            var observation = World(held: new[] { 6, 5 }, offer: 7, missingStats: true, hp: 5);
            var evaluator = Evaluator(observation);
            var buy = evaluator.EvaluatePurchase("offer", 2, 2);
            Assert.That(buy.Hold.Status, Is.EqualTo(ItemTransactionStatus.InventoryUnavailable));
            Assert.That(buy.Hold.Before.UnavailableItems.Single().HandSlot, Is.Zero);
            Assert.That(buy.Hold.EconomyDelta, Is.EqualTo(-1.75m));
            Assert.That(buy.Hold.Score, Is.Null);
            Assert.That(evaluator.EvaluateUse(1, Player).EffectValue, Is.EqualTo(16));
            var sell = evaluator.EvaluateSell(0, SpaceDemand.UnknownDemand);
            Assert.That(sell.After.Value, Is.EqualTo(8));
            Assert.That(sell.Score, Is.Null);
            Assert.That(sell.Item.Targets.Status, Is.EqualTo(ItemTargetEvaluationStatus.NoneEvaluated));
        }

        [Test]
        public void PartialAndBattleLimitFailuresStayUnavailable()
        {
            var partial = PermanentItemEffectEvaluatorTests.World(6, layout: "AB.......", cost: 5, missingStatsCost: 5);
            var holding = Evaluator(partial).Inventory.EvaluateHand(0);
            Assert.That(holding.Targets.Status, Is.EqualTo(ItemTargetEvaluationStatus.PartiallyEvaluated));
            Assert.That(holding.Value, Is.Null);

            var defense = TemporaryItemEffectEvaluatorTests.World(new[] { 3 }, layout: "AB.......", block: 3, differentDefenses: true);
            var attack = TemporaryItemEffectEvaluatorTests.World(new[] { 1 }, hp: new[] { 20, 20 });
            foreach (var observation in new[] { defense, attack })
            {
                var settings = new ItemTransactionValueSettings(effects: new IntegratedItemEffectSettings(
                    temporaryBattle: new TemporaryItemEffectSettings(attackStateLimit: 1, defenseStateLimit: 1)));
                var result = Evaluator(observation, settings).EvaluateUse(0, Ally);
                Assert.That(result.Status, Is.EqualTo(ItemTransactionStatus.EffectUnavailable));
                Assert.That(result.Effect.BattleFailure.StateLimit, Is.EqualTo(1));
                Assert.That(result.Score, Is.Null);
            }
        }

        [Test]
        public void ForeignUnsupportedContradictoryAndNonPreparationAreDistinct()
        {
            var observation = World(offer: 5, held: new[] { 5 });
            var evaluator = Evaluator(observation);
            var other = World(offer: 5, held: new[] { 5 });
            var foreign = ItemEffectPreview.PreviewPurchase(other, "offer", 1);
            Assert.That(evaluator.EvaluatePurchase(foreign, 2).Hold.Status, Is.EqualTo(ItemTransactionStatus.DifferentObservation));
            Assert.That(evaluator.EvaluateUse(ItemEffectPreview.PreviewUse(other, ItemPreviewSource.Hand, Player, 0)).Status,
                Is.EqualTo(ItemTransactionStatus.DifferentObservation));
            var pending = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(6, eventPending: true));
            var eventPreview = ItemEffectPreview.PreviewUse(pending, ItemPreviewSource.Event, Ally);
            Assert.That(Evaluator(pending).EvaluateUse(eventPreview).Failure, Is.EqualTo(ItemPreviewFailure.InvalidSource));
            Assert.That(Evaluator(pending).Inventory.EvaluateHand(0).Targets.Status,
                Is.EqualTo(ItemTargetEvaluationStatus.EnumerationIncomplete));

            foreach (string id in new[] { "unknown", "Item005" })
            {
                var custom = new ItemDefinition(id, "Other", 1, "", ItemEffectTarget.OneAlly,
                    ItemEffectDuration.Immediate, new[] { Effect(ItemEffectKind.Attack, 1) });
                var unsupported = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(5, custom: custom));
                var expected = id == "Item005" ? ItemHoldingStatus.DefinitionMismatch : ItemHoldingStatus.UnsupportedItem;
                Assert.That(Evaluator(unsupported).Inventory.EvaluateHand(0).Status, Is.EqualTo(expected));
            }
        }

        [TestCase(0, 0, 0, 0, -1.40)]
        [TestCase(.50, .25, .90, 8, 13.80)]
        [TestCase(1, 1, 1, 16, 14.60)]
        public void SettingsAffectHoldingGroupingAndDiscount(double alpha, double rho, double discount, int value, double twoStep)
        {
            var settings = new ItemInventoryValueSettings((decimal)alpha, duplicateRate: (decimal)rho, futureValues: Future(5, 0));
            var transaction = new ItemTransactionValueSettings(inventory: settings, immediateUseDiscount: (decimal)discount);
            var purchase = Evaluator(World(offer: 5, hp: 5), transaction).EvaluatePurchase("offer", 0, 2);
            Assert.That(purchase.Hold.Item.Value, Is.EqualTo(value));
            Assert.That(purchase.DiscountedScore, Is.EqualTo((decimal)twoStep));
            var existing = Evaluator(World(held: new[] { 5, 5 }, hp: 5), transaction).Inventory.EvaluateInventory();
            Assert.That(existing.Value, Is.EqualTo(value * (1m + (decimal)rho)));
        }

        [Test]
        public void SettingsRejectInvalidInputsAndAllowCapAndFutureOverrides()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemInventoryValueSettings(currentEffectRate: 1.1m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemInventoryValueSettings(maximumItemValue: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemInventoryValueSettings(duplicateRate: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemInventoryValueSettings(futureValues: Future(5, -1)));
            Assert.Throws<ArgumentException>(() => new ItemInventoryValueSettings(futureValues: Future(9, 1)));
            Assert.Throws<ArgumentException>(() => new ItemInventoryValueSettings(futureValues: Future(5, 1).Concat(Future(5, 2))));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemTransactionValueSettings(immediateUseDiscount: 1.1m));
            var small = new ItemTransactionValueSettings(inventory: new ItemInventoryValueSettings(maximumItemValue: 5));
            Assert.That(Evaluator(World(held: new[] { 5 }, hp: 5), small).Inventory.EvaluateHand(0).Value, Is.EqualTo(5));
        }

        [Test]
        public void DecisionCacheAndDiagnosticsDoNotMutateRealState()
        {
            var session = ItemEffectPreviewTests.Session(5, choice: StageChoice.SelectB, hp: 5);
            var observation = ItemEffectPreviewTests.Observe(session);
            var checkpoint = session.CaptureCheckpoint();
            var random = StrategyRandomSource.Create(99, "HeuristicStrategy", "16");
            var randomCheckpoint = random.CaptureState();
            string fingerprint = SimulationStateFingerprint.Create(observation);
            var cached = Evaluator(observation);
            var fresh = new ItemTransactionValueEvaluator(observation, 2, useCache: false, includeDetails: true);
            var preview = ItemEffectPreview.PreviewPurchase(observation, observation.Shop.Offers.Single().OfferId, 4);
            var first = cached.EvaluatePurchase(preview, 2);
            Assert.That(cached.EvaluateSell(0, SpaceDemand.UnknownDemand).Score, Is.EqualTo(fresh.EvaluateSell(0, SpaceDemand.UnknownDemand).Score));
            Assert.That(fresh.EvaluatePurchase(preview, 2).DiscountedScore, Is.EqualTo(first.DiscountedScore));
            Assert.That(cached.EvaluatePurchase(preview, 2).Hold.Before, Is.SameAs(first.Hold.Before));
            Assert.That(cached.EvaluatePurchase(preview, 1).ImmediateUseEligible, Is.False);
            Assert.That(cached.EvaluatePurchase(preview, 2).ImmediateUseEligible, Is.True);
            Assert.That(session.CaptureCheckpoint(), Is.SameAs(checkpoint));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(checkpoint.RandomState));
            Assert.That(random.CaptureState(), Is.EqualTo(randomCheckpoint));
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(fingerprint));
            Assert.That(SimulationTrialCheckpoint.CurrentSchemaVersion, Is.EqualTo(2));
        }

        private static ItemTransactionValueEvaluator Evaluator(GameObservation observation, ItemTransactionValueSettings settings = null) =>
            new ItemTransactionValueEvaluator(observation, 2, settings);

        private static KeyValuePair<string, decimal>[] Future(int item, decimal value) =>
            new[] { new KeyValuePair<string, decimal>("Item00" + item, value) };

        private static GameObservation World(int offer = 0, int[] held = null, int occupied = 0, int hp = 25, int gold = 30,
            int cost = 1, int? enemyHp = null, bool missingStats = false, bool poorMean = false, int[] reserveStars = null)
        {
            held ??= Array.Empty<int>();
            occupied = Math.Max(occupied, held.Length);
            var definitions = new[] { new CharacterDefinition("A", "A", cost, null, null),
                new CharacterDefinition("B", "B", 1, null, null) };
            var stats = new CharacterStatDefinition(new[] { 1, 5 }.Select(tier => new CharacterCostStats(tier,
                new[] { new CharacterStatOption(3, enemyHp.HasValue ? 0 : 1, 1) }, missingStats ? null :
                poorMean ? new[] { new CharacterStatOption(0, 0, 1) } :
                new[] { new CharacterStatOption(tier == 1 ? 5 : 8, 2, 1), new CharacterStatOption(tier == 1 ? 7 : 10, 3, 3) },
                new[] { new CharacterStatOption(10, 4, 1) })));
            var config = new StageProgressConfig(4, 4, 1, 4, 0, 0, 100, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var stage = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(config), config).NextState;
            stage = StageSelectionRules.Select(stage, config, enemyHp.HasValue ? StageChoice.SelectA : StageChoice.SelectB).NextState;
            var rand = new SimulationRandomStreams(192);
            var owned = new List<CharacterInstance>();
            CharacterInstance Own(int index, int star)
            {
                var character = new CharacterInstance("own-" + owned.Count, definitions[index], index == 0 ? cost : 1, star);
                Assert.That(character.EnsureStats(definitions[index], stats, rand.Stats), Is.True);
                owned.Add(character);
                return character;
            }

            var board = BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0, Own(0, 1).InstanceId, out board), Is.True);
            var hand = HandState.CreateStandard();
            for (int slot = 0; slot < held.Length; slot++)
                Assert.That(hand.TryWithSlot(slot, HandSlotContent.Item(Items[held[slot] - 1].DefinitionId), out hand), Is.True);
            for (int slot = held.Length; slot < occupied; slot++)
            {
                var character = reserveStars == null ? Own(1, 1) : Own(0, reserveStars[slot - held.Length]);
                Assert.That(hand.TryWithSlot(slot, HandSlotContent.Character(character.InstanceId), out hand), Is.True);
            }
            var enemyBoard = EnemyBoardState.CreateEmpty();
            var enemies = Array.Empty<EnemyDefinition>();
            if (enemyHp.HasValue)
            {
                var enemy = new EnemyDefinition("enemy", "Enemy", 4, 0, enemyHp.Value, null, null, null, "T001", null, null, null);
                enemies = new[] { enemy };
                Assert.That(EnemyInstanceFactory.TryCreate(enemy, 0, 0, 0, 0, 0, 0, 0, 0, out var instance, out _), Is.True);
                enemyBoard = enemyBoard.TryPlace(instance).NextState;
            }
            var offers = offer == 0 ? Array.Empty<ShopOffer>() :
                new[] { new ShopOffer("offer", ShopOfferKind.Item, Items[offer - 1].DefinitionId, Items[offer - 1].PurchasePrice, null) };
            var built = ObservationBuilder.Build(new ObservationSource(stage, config,
                new EconomyState(gold, 0, 4, 5, gold, 0, 1, hp, 30),
                new EconomyRulesConfiguration(4, 2, 1, new[] { 1, 2, 3, 4 }, 10, 1, 3, 3, 1, 0),
                board, hand, new CharacterOwnershipState(owned), definitions, definitions, definitions, Items,
                new ShopState(enemyHp.HasValue ? ShopKind.Character : ShopKind.Item, offers, offers.Length, 1, 1),
                new ItemEffectState(), enemyDefinitions: enemies, enemyBoard: enemyBoard, characterStatSettings: stats,
                battleTargetDefinitions: new[] { new BattleTargetDefinition("T001", BattleTargetFilter.AllBoard, BattleTargetSelection.Uniform) }));
            Assert.That(built.Succeeded, Is.True, string.Join("; ", built.Errors));
            return built.Observation;
        }

        private static ItemDefinition Define(int number, int price, ItemEffectTarget target, ItemEffectDuration duration,
            params ItemEffectDefinition[] effects)
        {
            string id = "Item00" + number;
            return new ItemDefinition(id, id, price, "", target, duration, effects);
        }

        private static ItemEffectDefinition Effect(ItemEffectKind kind, int value) => new ItemEffectDefinition(kind, value);
    }
}
