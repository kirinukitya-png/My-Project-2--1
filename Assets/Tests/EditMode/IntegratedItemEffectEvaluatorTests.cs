using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class IntegratedItemEffectEvaluatorTests
    {
        private static readonly GameActionTarget BoardTarget = Target(ItemUseTargetKind.BoardCharacter, 0);
        private static readonly GameActionTarget PlayerTarget = Target(ItemUseTargetKind.PlayerArea, 0);
        private static readonly GameActionTarget EnemyTarget = Target(ItemUseTargetKind.EnemyArea, -1);

        [TestCase(1, 2.40)]
        [TestCase(2, .80)]
        [TestCase(3, 2.56)]
        [TestCase(4, 3.52)]
        [TestCase(5, 12)]
        [TestCase(6, 50.40)]
        [TestCase(7, 8.96)]
        public void OfficialItemsMatchStandaloneValuesAndComponentSums(int item, double expected)
        {
            GameObservation observation;
            if (item <= 4)
                observation = TemporaryItemEffectEvaluatorTests.World(new[] { item }, hp: new[] { item == 4 ? 5 : 20 });
            else if (item == 5)
                observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(5, hp: 8));
            else
                observation = PermanentItemEffectEvaluatorTests.World(item, enemyHp: 8, temporary: item == 7);
            var settings = new IntegratedItemEffectSettings(permanentBattle: new BattleValueSettings(item == 6 ? 0m : .10m));
            var target = item == 5 ? PlayerTarget : item == 2 || item == 4 ? EnemyTarget : BoardTarget;
            var preview = ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Hand, target, 0);
            var evaluator = new IntegratedItemEffectEvaluator(observation, settings);
            var result = evaluator.Evaluate(preview);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.EffectValue, Is.EqualTo((decimal)expected));
            decimal damage = item == 1 ? 2.40m : item == 2 ? .80m : item == 3 || item == 4 ? 1.60m : 0m;
            decimal defense = item == 3 ? .96m : item == 4 ? 1.92m : 0m;
            Assert.That(result.TemporaryDamageValue, Is.EqualTo(damage));
            Assert.That(result.TemporaryDefenseValue, Is.EqualTo(defense));
            Assert.That(result.HpHealingValue, Is.EqualTo(item == 5 ? 12m : 0m));
            Assert.That(result.PermanentCompositionValue, Is.EqualTo(item == 6 ? 50.40m : item == 7 ? 8m : 0m));
            Assert.That(result.PermanentBattleCorrectionValue, Is.EqualTo(item == 7 ? .96m : 0m));
            ItemEffectValueResult standalone = item <= 4 ? (ItemEffectValueResult)new TemporaryItemEffectEvaluator(observation).Evaluate(preview)
                : item == 5 ? new HpValueEvaluator().EvaluateItem(preview)
                : new PermanentItemEffectEvaluator(observation, battleSettings: settings.PermanentBattle).Evaluate(preview);
            Assert.That(result.EffectValue, Is.EqualTo(standalone.EffectValue));
            Assert.That(result.CapacityActionValue, Is.EqualTo(standalone.CapacityActionValue));
            Assert.That(result.ConsumedHandSlot, Is.Zero);
            if (item < 7) Assert.That(evaluator.ReferenceBattleEvaluations, Is.Zero);
            if (item == 5) Assert.That(evaluator.PermanentBattleRequests + evaluator.TemporaryBattleRequests, Is.Zero);
        }

        [TestCase(2, 3, 20, 4, false)]
        [TestCase(4, 5, 2, 0, false)]
        [TestCase(4, 3, 0, 0, false)]
        [TestCase(1, 3, 20, 0, true)]
        public void ZeroEffectIsAnEvaluatedCandidate(int item, int attack, int hp, int def, bool enemyless)
        {
            var observation = TemporaryItemEffectEvaluatorTests.World(new[] { item }, attack: attack,
                hp: new[] { hp }, enemyDefense: def, enemyless: enemyless);
            var result = new IntegratedItemEffectEvaluator(observation).EvaluateHandTargets(0);
            Assert.That(result.Status, Is.EqualTo(ItemTargetEvaluationStatus.AllEvaluated));
            Assert.That(result.ConfirmedMaximumEffect, Is.Zero);
            Assert.That(result.MaximumSuccessfulTargets, Has.Count.EqualTo(1));
        }

        [Test]
        public void TargetlessHealingAndEnemyAreaAreSingleCandidates()
        {
            var fullHp = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(5, hp: 30));
            var healing = new IntegratedItemEffectEvaluator(fullHp).EvaluateHandTargets(0);
            Assert.That(healing.LegalCount, Is.EqualTo(1));
            Assert.That(healing.ConfirmedMaximumEffect, Is.Zero);
            var foes = TemporaryItemEffectEvaluatorTests.World(new[] { 4 }, hp: new[] { 1, 2, 10, 0 });
            Assert.That(new IntegratedItemEffectEvaluator(foes).EvaluateHandTargets(0).LegalCount, Is.EqualTo(1));
        }

        [TestCase(1)]
        [TestCase(3)]
        public void MultipleBoardTargetsRetainAllTiesInSlotOrder(int item)
        {
            var observation = TemporaryItemEffectEvaluatorTests.World(new[] { item }, layout: "AB.......");
            var evaluator = new IntegratedItemEffectEvaluator(observation);
            var targets = evaluator.EvaluateHandTargets(0);
            Assert.That(targets.Status, Is.EqualTo(ItemTargetEvaluationStatus.AllEvaluated));
            Assert.That(targets.MaximumSuccessfulTargets.Select(x => x.Target.Value.SlotIndex), Is.EqualTo(new[] { 0, 1 }));
            var uncached = new IntegratedItemEffectEvaluator(observation, useCache: false, includeDetails: true);
            foreach (var row in targets.Targets.Reverse())
                Assert.That(uncached.Evaluate(row.Preview).EffectValue, Is.EqualTo(row.EffectValue));
            Assert.That(evaluator.TemporaryBattleRequests, Is.EqualTo(3));
        }

        [Test]
        public void EventPreservesNegativeMaximumAndBoardHandTargets()
        {
            var observation = PermanentItemEffectEvaluatorTests.World(6, reserveStars: new[] { 1, 2, 2 },
                eventPending: true, poorMean: true);
            var evaluator = new IntegratedItemEffectEvaluator(observation);
            var result = evaluator.EvaluateEventTargets();
            Assert.That(result.Status, Is.EqualTo(ItemTargetEvaluationStatus.AllEvaluated));
            Assert.That(result.LegalCount, Is.EqualTo(2));
            Assert.That(result.IllegalCount, Is.EqualTo(2));
            var legal = result.Targets.Where(x => x.Succeeded).ToArray();
            Assert.That(legal[0].EffectValue, Is.EqualTo(-30.80m));
            Assert.That(evaluator.EvaluateEvent(BoardTarget).EffectValue, Is.EqualTo(-30.80m));
            Assert.That(legal[1].Target.Value.Kind, Is.EqualTo(ItemUseTargetKind.HandCharacter));
            Assert.That(legal.All(x => x.EffectValue < 0 && x.CapacityDelta == 0 &&
                x.BattleSkipReason == ItemBattleSkipReason.Event), Is.True);
            Assert.That(result.ConfirmedMaximumEffect, Is.EqualTo(legal.Max(x => x.EffectValue)));
        }

        [Test]
        public void NonePartialAndIncompleteAreDistinct()
        {
            var partial = PermanentItemEffectEvaluatorTests.World(6, layout: "AB.......", cost: 5, missingStatsCost: 5);
            var mixed = new IntegratedItemEffectEvaluator(partial).EvaluateHandTargets(0);
            Assert.That(mixed.Status, Is.EqualTo(ItemTargetEvaluationStatus.PartiallyEvaluated));
            Assert.That(mixed.LegalCount, Is.EqualTo(2));
            Assert.That(mixed.UnavailableCount, Is.EqualTo(1));
            Assert.That(mixed.Targets[0].Status, Is.EqualTo(ItemEffectEvaluationStatus.PublicStatsUnavailable));
            Assert.That(mixed.Targets[0].Permanent, Is.Not.Null);
            Assert.That(mixed.MaximumSuccessfulEffect, Is.Not.Null);
            Assert.That(mixed.ConfirmedMaximumEffect, Is.Null);

            var unknownStats = PermanentItemEffectEvaluatorTests.World(6, missingStats: true);
            var none = new IntegratedItemEffectEvaluator(unknownStats).EvaluateHandTargets(0);
            Assert.That(none.Status, Is.EqualTo(ItemTargetEvaluationStatus.NoneEvaluated));
            Assert.That(none.MaximumSuccessfulEffect, Is.Null);

            var noTarget = PermanentItemEffectEvaluatorTests.World(6, star: 2);
            var evaluator = new IntegratedItemEffectEvaluator(noTarget);
            Assert.That(evaluator.EvaluateHandTargets(0).Status, Is.EqualTo(ItemTargetEvaluationStatus.NoLegalTargets));
            var incomplete = evaluator.EvaluateHandTargets(4);
            Assert.That(incomplete.Status, Is.EqualTo(ItemTargetEvaluationStatus.EnumerationIncomplete));
            Assert.That(incomplete.EnumerationFailure, Is.EqualTo(ItemPreviewFailure.ItemUnavailable));
        }

        [TestCase(false, 3, 16)]
        [TestCase(true, 4, 40)]
        public void PurchaseUseReportsAllThreeStates(bool fourOccupied, int occupied, int capacity)
        {
            var session = ItemEffectPreviewTests.Session(1, extraItem: 1,
                extraReserve: fourOccupied, choice: StageChoice.SelectB);
            var observation = ItemEffectPreviewTests.Observe(session);
            var checkpoint = session.CaptureCheckpoint();
            var strategyRandom = StrategyRandomSource.Create(91, "HeuristicStrategy", "16");
            var strategyCheckpoint = strategyRandom.CaptureState();
            string fingerprint = SimulationStateFingerprint.Create(observation);
            var evaluator = new IntegratedItemEffectEvaluator(observation);
            var offer = observation.Shop.Offers.Single();
            var purchase = evaluator.PreviewPurchase(offer.OfferId, 4);
            var direct = evaluator.EvaluateHand(0, BoardTarget);
            var used = evaluator.EvaluatePurchased(purchase, BoardTarget);
            Assert.That(evaluator.EvaluatePurchasedTargets(purchase).LegalCount, Is.EqualTo(1));
            Assert.That(used.HandOccupiedBeforePurchase, Is.EqualTo(occupied));
            Assert.That(used.HandOccupiedBefore, Is.EqualTo(occupied + 1));
            Assert.That(used.HandOccupiedAfter, Is.EqualTo(occupied));
            Assert.That(used.PurchaseHandOccupiedDelta, Is.EqualTo(1));
            Assert.That(used.HandOccupiedDelta, Is.EqualTo(-1));
            Assert.That(used.PurchaseGoldDelta, Is.EqualTo(-3));
            Assert.That(used.PurchasePrice, Is.EqualTo(3));
            Assert.That(used.GoldDelta, Is.Zero);
            Assert.That(used.GoldDeltaIncludingPurchase, Is.EqualTo(-3));
            Assert.That(used.PurchaseCapacityActionDelta, Is.EqualTo(-capacity));
            Assert.That(used.CapacityActionValue, Is.EqualTo(capacity));
            Assert.That(used.CapacityActionDeltaIncludingPurchase, Is.Zero);
            Assert.That(direct.PurchasePrice, Is.Null);
            Assert.That(direct.ConsumedHandSlot, Is.Zero);
            Assert.That(used.ConsumedHandSlot, Is.EqualTo(4));
            Assert.That(used.EffectValue, Is.Zero);
            Assert.That(session.CaptureCheckpoint(), Is.SameAs(checkpoint));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(checkpoint.RandomState));
            Assert.That(strategyRandom.CaptureState(), Is.EqualTo(strategyCheckpoint));
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(fingerprint));
        }

        [Test]
        public void HealingReportsTransactionValuesAndDoesNotInitializeBattle()
        {
            var observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(5, hp: 8, fullHand: true));
            var evaluator = new IntegratedItemEffectEvaluator(observation,
                new IntegratedItemEffectSettings(hp: new HpValueSettings(2, 1, .5m)));
            var result = evaluator.EvaluateHand(0, PlayerTarget);
            Assert.That(result.HpHealingValue, Is.EqualTo(6));
            Assert.That(result.EffectValue, Is.EqualTo(6));
            Assert.That(result.CapacityActionValue, Is.EqualTo(40));
            Assert.That(result.GoldDelta, Is.Zero);
            Assert.That(result.ConsumedHandItem.DefinitionId, Is.EqualTo("Item005"));
            Assert.That(evaluator.PermanentBattleRequests + evaluator.TemporaryBattleRequests, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KappaAndLambdaAreIndependent(bool disablePermanent)
        {
            var observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(1, extraItem: 7));
            var evaluator = new IntegratedItemEffectEvaluator(observation,
                new IntegratedItemEffectSettings(permanentBattle: new BattleValueSettings(disablePermanent ? 0m : .1m),
                    temporaryBattle: new TemporaryItemEffectSettings(disablePermanent ? .1m : 0m)));
            Assert.That(evaluator.EvaluateHand(0, BoardTarget).Succeeded, Is.True);
            Assert.That(evaluator.EvaluateHand(2, BoardTarget).Succeeded, Is.True);
            Assert.That(evaluator.TemporaryBattleRequests > 0, Is.EqualTo(disablePermanent));
            Assert.That(evaluator.PermanentBattleRequests > 0, Is.EqualTo(!disablePermanent));
            Assert.That(evaluator.ReferenceBattleEvaluations > 0, Is.EqualTo(!disablePermanent));
        }

        [Test]
        public void DefaultSettingsAndValidationAreDelegatedToStandaloneSettings()
        {
            var settings = new IntegratedItemEffectSettings();
            Assert.That(settings.TemporaryBattle.Kappa, Is.EqualTo(.10m));
            Assert.That(settings.PermanentBattle.BattleCorrectionWeight, Is.EqualTo(.10m));
            Assert.That(settings.TemporaryBattle.Beta, Is.EqualTo(.60m));
            Assert.That(settings.PermanentBattle.DefenseWeight, Is.EqualTo(.60m));
            Assert.That(settings.TemporaryBattle.AttackStateLimit, Is.EqualTo(256));
            Assert.That(settings.TemporaryBattle.DefenseStateLimit, Is.EqualTo(256));
            Assert.That(settings.PermanentBattle.AllyAttackMaxStates, Is.EqualTo(256));
            Assert.That(settings.PermanentBattle.EnemyBlockMaxStates, Is.EqualTo(256));
            Assert.That(settings.Composition.HandOption.Coefficient, Is.EqualTo(.15m));
            Assert.That(settings.Hp.LowHpValue, Is.EqualTo(4));
            Assert.That(settings.Hp.MiddleHpValue, Is.EqualTo(2));
            Assert.That(settings.Hp.HighHpValue, Is.EqualTo(1));
            Assert.Throws<ArgumentNullException>(() => new IntegratedItemEffectEvaluator(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporaryItemEffectSettings(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleValueSettings(-1));
        }

        [Test]
        public void InvalidForeignAndContradictoryDefinitionsKeepSeparateStatuses()
        {
            var observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(1, choice: StageChoice.SelectB));
            var evaluator = new IntegratedItemEffectEvaluator(observation);
            Assert.That(evaluator.EvaluateHand(0, Target(ItemUseTargetKind.BoardCharacter, 8)).Status,
                Is.EqualTo(ItemEffectEvaluationStatus.Illegal));
            var other = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(1, choice: StageChoice.SelectB));
            Assert.That(evaluator.Evaluate(ItemEffectPreview.PreviewUse(other, ItemPreviewSource.Hand, BoardTarget, 0)).Status,
                Is.EqualTo(ItemEffectEvaluationStatus.DifferentObservation));
            var foreign = ItemEffectPreview.PreviewPurchase(other, other.Shop.Offers.Single().OfferId, 4);
            Assert.That(evaluator.EvaluatePurchased(foreign, BoardTarget).Status, Is.EqualTo(ItemEffectEvaluationStatus.DifferentObservation));
            Assert.That(evaluator.EvaluatePurchasedTargets(foreign).EnumerationFailure, Is.EqualTo(ItemPreviewFailure.DifferentObservation));
            var contradicts = new ItemDefinition("Item005", "Wrong", 4, string.Empty, ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Attack, 1) });
            var malformed = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(5, custom: contradicts));
            var result = new IntegratedItemEffectEvaluator(malformed).EvaluateHand(0, BoardTarget);
            Assert.That(result.Status, Is.EqualTo(ItemEffectEvaluationStatus.UnsupportedItem));
            Assert.That(result.IntegrationFailure, Is.EqualTo(ItemEffectIntegrationFailure.DefinitionMismatch));
            Assert.That(result.EffectValue, Is.Null);
        }

        [Test]
        public void UnsupportedProjectionIsReportedAsIncompleteEnumeration()
        {
            var block = new ItemDefinition("block", "Block", 2, string.Empty, ItemEffectTarget.Player,
                ItemEffectDuration.ThisBattle, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 2) });
            var observation = ItemEffectPreviewTests.Observe(ItemEffectPreviewTests.Session(1, custom: block));
            var evaluator = new IntegratedItemEffectEvaluator(observation);
            Assert.That(evaluator.EvaluateHandTargets(0).EnumerationFailure, Is.EqualTo(ItemPreviewFailure.UnsupportedEffect));
            Assert.That(evaluator.EvaluateHand(0, PlayerTarget).Legality, Is.EqualTo(ItemPreviewLegality.Unknown));
        }

        [TestCase(BattleValueStatus.TargetUnavailable, TemporaryItemBattleSide.Before)]
        [TestCase(BattleValueStatus.AttackStateLimitExceeded, TemporaryItemBattleSide.Before)]
        [TestCase(BattleValueStatus.AttackStateLimitExceeded, TemporaryItemBattleSide.After)]
        [TestCase(BattleValueStatus.DefenseStateLimitExceeded, TemporaryItemBattleSide.Before)]
        [TestCase(BattleValueStatus.ReferenceUnavailable, TemporaryItemBattleSide.Before)]
        public void BattleFailuresRetainComponentSideAndStandaloneDetails(BattleValueStatus status, TemporaryItemBattleSide side)
        {
            GameObservation observation;
            IntegratedItemEffectSettings settings;
            if (status == BattleValueStatus.ReferenceUnavailable)
            {
                observation = PermanentItemEffectEvaluatorTests.World(7, layout: "AB.......", sharedAttribute: true,
                    actualTargetAll: true);
                settings = new IntegratedItemEffectSettings(permanentBattle: new BattleValueSettings(enemyBlockMaxStates: 1));
            }
            else
            {
                observation = status == BattleValueStatus.TargetUnavailable
                    ? TemporaryItemEffectEvaluatorTests.World(new[] { 1 }, missingTargetSlot: 0)
                    : status == BattleValueStatus.DefenseStateLimitExceeded
                        ? TemporaryItemEffectEvaluatorTests.World(new[] { 3 }, layout: "AB.......", block: 3, differentDefenses: true)
                        : side == TemporaryItemBattleSide.Before
                            ? TemporaryItemEffectEvaluatorTests.World(new[] { 4 }, hp: new[] { 2, 2 })
                            : TemporaryItemEffectEvaluatorTests.World(new[] { 1, 2 }, hp: new[] { 20, 20 }, enemyDefense: 4);
                settings = new IntegratedItemEffectSettings(temporaryBattle: new TemporaryItemEffectSettings(attackStateLimit: 1,
                    defenseStateLimit: 1));
            }
            var evaluator = new IntegratedItemEffectEvaluator(observation, settings);
            var result = evaluator.EvaluateHand(0,
                status == BattleValueStatus.AttackStateLimitExceeded && side == TemporaryItemBattleSide.Before ? EnemyTarget : BoardTarget);
            Assert.That(result.Status, Is.EqualTo(ItemEffectEvaluationStatus.BattleUnavailable));
            Assert.That(result.FailedBattleSide, Is.EqualTo(side));
            Assert.That(result.BattleFailure.Status, Is.EqualTo(status));
            Assert.That(result.EffectValue, Is.Null);
            if (status == BattleValueStatus.ReferenceUnavailable)
            {
                Assert.That(result.FailedComponent, Is.EqualTo(ItemEffectFailureComponent.PermanentBattle));
                Assert.That(result.PermanentCompositionValue, Is.EqualTo(8));
                Assert.That(result.PermanentBattleCorrectionValue, Is.Null);
            }
            else
            {
                Assert.That(result.FailedComponent, Is.EqualTo(ItemEffectFailureComponent.TemporaryBattle));
                Assert.That(result.TemporaryDamageValue, Is.Null);
            }
            if (status == BattleValueStatus.AttackStateLimitExceeded && side == TemporaryItemBattleSide.After)
                Assert.That(evaluator.EvaluateHand(1, EnemyTarget).EffectValue, Is.Zero);
        }

        [Test]
        public void RegisteredPreviewCachePreservesOriginalObservationAndEnemyContext()
        {
            var observation = TemporaryItemEffectEvaluatorTests.World(new[] { 2, 4 }, hp: new[] { 6, 20 });
            string fingerprint = SimulationStateFingerprint.Create(observation);
            var cached = new IntegratedItemEffectEvaluator(observation);
            var def = ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Hand, EnemyTarget, 0);
            var hp = ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Hand, EnemyTarget, 1);
            var a = cached.Evaluate(def);
            var b = cached.Evaluate(hp);
            int requests = cached.TemporaryBattleRequests;
            Assert.That(cached.Evaluate(def), Is.SameAs(a));
            Assert.That(cached.Evaluate(hp), Is.SameAs(b));
            Assert.That(cached.TemporaryBattleRequests, Is.EqualTo(requests));
            var fresh = new IntegratedItemEffectEvaluator(observation, useCache: false, includeDetails: true);
            Assert.That(fresh.Evaluate(hp).EffectValue, Is.EqualTo(b.EffectValue));
            Assert.That(fresh.Evaluate(def).EffectValue, Is.EqualTo(a.EffectValue));
            Assert.That(a.Temporary.After.Enemies.Enemies.First().InitialEnemy.Defense, Is.EqualTo(-1));
            Assert.That(b.Temporary.After.Enemies.Enemies.First().InitialEnemy.Defense, Is.Zero);
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(fingerprint));
        }

        private static GameActionTarget Target(ItemUseTargetKind kind, int slot) => new GameActionTarget(kind, slot, 0, true);
    }
}
