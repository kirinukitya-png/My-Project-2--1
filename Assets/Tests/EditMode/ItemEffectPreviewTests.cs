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
    public sealed class ItemEffectPreviewTests
    {
        private static readonly CharacterDefinition Ally = new CharacterDefinition("ally", "Ally", 1,
            Array.Empty<string>(), Array.Empty<string>());
        private static readonly GameActionTarget BoardTarget = Target(ItemUseTargetKind.BoardCharacter, 0);
        private static readonly GameActionTarget ReserveTarget = Target(ItemUseTargetKind.HandCharacter, 1);
        private static readonly GameActionTarget PlayerTarget = Target(ItemUseTargetKind.PlayerArea, 0);
        private static readonly GameActionTarget EnemyTarget = Target(ItemUseTargetKind.EnemyArea, -1);
        private static readonly ItemDefinition[] Items =
        {
            Item(1, 3, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle, Effect(ItemEffectKind.Attack, 3)),
            Item(2, 2, ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle, Effect(ItemEffectKind.Defense, -1)),
            Item(3, 3, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle,
                Effect(ItemEffectKind.Attack, 2), Effect(ItemEffectKind.Defense, 2)),
            Item(4, 3, ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle, Effect(ItemEffectKind.Damage, 2)),
            Item(5, 4, ItemEffectTarget.Player, ItemEffectDuration.Immediate, Effect(ItemEffectKind.Damage, -4)),
            Item(6, 10, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate, Effect(ItemEffectKind.Star, 2)),
            Item(7, 5, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate, Effect(ItemEffectKind.Attack, 1))
        };

        [TestCase(1, 3, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle, 3)]
        [TestCase(2, 2, ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle, -1)]
        [TestCase(3, 3, ItemEffectTarget.OneAlly, ItemEffectDuration.ThisBattle, 2)]
        [TestCase(4, 3, ItemEffectTarget.AllEnemies, ItemEffectDuration.ThisBattle, 2)]
        [TestCase(5, 4, ItemEffectTarget.Player, ItemEffectDuration.Immediate, -4)]
        [TestCase(6, 10, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate, 2)]
        [TestCase(7, 5, ItemEffectTarget.OneAlly, ItemEffectDuration.Immediate, 1)]
        public void OfficialItemsMatchSessionExecution(int number, int price, ItemEffectTarget targetKind,
            ItemEffectDuration duration, int effectValue)
        {
            var session = Session(number);
            var before = Observe(session);
            var target = targetKind == ItemEffectTarget.AllEnemies ? EnemyTarget :
                targetKind == ItemEffectTarget.Player ? PlayerTarget : BoardTarget;
            var result = Preview(before, target);

            Assert.That(result.Legality, Is.EqualTo(ItemPreviewLegality.Legal));
            Assert.That(result.ProjectionStatus, Is.EqualTo(ItemProjectionStatus.Projected));
            Assert.That(result.Item.PurchasePrice, Is.EqualTo(price));
            Assert.That(result.Item.Target, Is.EqualTo(targetKind));
            Assert.That(result.Item.Duration, Is.EqualTo(duration));
            Assert.That(result.Item.Effects[0].Value, Is.EqualTo(effectValue));
            Assert.That(result.ConsumedHandItem, Is.SameAs(before.Hand.Slots[0].Item));
            Assert.That(result.ConsumedHandSlot, Is.EqualTo(0));
            Assert.That(result.GoldDelta, Is.Zero);
            Assert.That(result.ItemBlockDelta, Is.Zero);

            var state = session.CurrentState;
            var dispatched = Execute(session, new UseHandItemAction(new ActionId("use"), 0, target));
            var after = Observe(session);
            AssertParity(result, after);
            Assert.That(session.CurrentState.RandomState.Stats.Equals(state.RandomState.Stats), Is.EqualTo(number != 6));
            Assert.That(session.CurrentState.RandomState.Shop, Is.EqualTo(state.RandomState.Shop));
            Assert.That(session.CurrentState.RandomState.Event, Is.EqualTo(state.RandomState.Event));
            Assert.That(dispatched.NextState.CharacterPool, Is.SameAs(state.CharacterPool));
            Assert.That(dispatched.NextState.Ownership.GetOwnedInstances().Select(x => x.PoolCopies),
                Is.EqualTo(state.Ownership.GetOwnedInstances().Select(x => x.PoolCopies)));

            if (number == 1 || number == 3)
            {
                Assert.That(result.After.Character.Attack, Is.EqualTo(3));
                Assert.That(result.After.Character.Defense, Is.EqualTo(1));
                Assert.That(result.After.Character.TemporaryAttack, Is.EqualTo(number == 1 ? 7 : 6));
                Assert.That(result.After.Character.TemporaryDefense, Is.EqualTo(number == 1 ? 1 : 3));
                Assert.That(result.After.Character.EffectiveAttack, Is.EqualTo(number == 1 ? 10 : 9));
            }
            if (number == 6)
            {
                Assert.That(result.After.Character.Attack, Is.EqualTo(6.5));
                Assert.That(result.After.Character.Defense, Is.EqualTo(2.75));
                Assert.That(result.After.Character.StatsKind, Is.EqualTo(ItemPreviewStatsKind.PublicMean));
                Assert.That(result.After.Character.RequiresStatsReroll, Is.True);
                Assert.That(result.After.Character.PreservesOwnershipAndPoolCopies, Is.True);
                Assert.That(result.After.Character.TemporaryAttack, Is.EqualTo(4));
                Assert.That(result.After.Character.TemporaryDefense, Is.EqualTo(1));
            }
        }

        [TestCase(1, 30, 5)]
        [TestCase(25, 30, 29)]
        [TestCase(29, 30, 30)]
        [TestCase(30, 30, 30)]
        [TestCase(18, 20, 20)]
        public void HealingDistinguishesZeroEffectFromIllegal(int hp, int maxHp, int expected)
        {
            var session = Session(5, hp: hp, maxHp: maxHp);
            var result = Preview(Observe(session), PlayerTarget);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IsZeroEffect, Is.EqualTo(hp == expected));
            Assert.That(result.EffectiveHealing, Is.EqualTo(expected - hp));
            Execute(session, new UseHandItemAction(new ActionId("heal"), 0, PlayerTarget));
            AssertParity(result, Observe(session));
        }

        [Test]
        public void DamageUsesAdjustedHpAndRetainsZeroHpSlots()
        {
            var session = Session(4, extraItem: 4, enemyHp: new[] { 3, 4, 12, 1 });
            var initial = Preview(Observe(session), EnemyTarget);
            Execute(session, new UseHandItemAction(new ActionId("first"), 0, EnemyTarget));
            AssertParity(initial, Observe(session));

            var result = Preview(Observe(session), EnemyTarget, 2);
            Assert.That(result.After.Enemies.Select(e => e.Before.HP), Is.EqualTo(new[] { 1, 2, 10, 0 }));
            Assert.That(result.After.Enemies.Select(e => e.HP), Is.EqualTo(new[] { 0, 0, 8, 0 }));
            Assert.That(result.After.Enemies.Select(e => e.DirectHpLoss), Is.EqualTo(new[] { 1, 2, 2, 0 }));
            Assert.That(result.After.Enemies.Select(e => e.BecameZeroHp), Is.EqualTo(new[] { true, true, false, false }));
            Assert.That(result.After.LivingEnemySlots, Is.EqualTo(new[] { 2 }));
            Execute(session, new UseHandItemAction(new ActionId("second"), 2, EnemyTarget));
            AssertParity(result, Observe(session));
        }

        [Test]
        public void NegativeDefenseStacksWithoutClamping()
        {
            var session = Session(2, extraItem: 2);
            var first = Preview(Observe(session), EnemyTarget);
            Assert.That(first.After.Enemies.Single().Defense, Is.EqualTo(-1));
            Execute(session, new UseHandItemAction(new ActionId("first"), 0, EnemyTarget));
            var second = Preview(Observe(session), EnemyTarget, 2);
            Assert.That(second.After.Enemies.Single().Before.Defense, Is.EqualTo(-1));
            Assert.That(second.After.Enemies.Single().Defense, Is.EqualTo(-2));
            Execute(session, new UseHandItemAction(new ActionId("second"), 2, EnemyTarget));
            AssertParity(second, Observe(session));
        }

        [Test]
        public void AllZeroHpStillHasPhysicalTargetsUnlikeEnemyless()
        {
            var session = Session(4, extraItem: 4, enemyHp: new[] { 1 });
            var first = Preview(Observe(session), EnemyTarget);
            Assert.That(first.After.AllPhysicalEnemiesHaveZeroHp, Is.True);
            Assert.That(first.After.IsEnemylessEncounter, Is.False);
            Execute(session, new UseHandItemAction(new ActionId("zero"), 0, EnemyTarget));
            var second = Preview(Observe(session), EnemyTarget, 2);
            Assert.That(second.IsZeroEffect, Is.True);
            Execute(session, new UseHandItemAction(new ActionId("still-physical"), 2, EnemyTarget));
            AssertParity(second, Observe(session));

            var enemyless = Observe(Session(4, choice: StageChoice.SelectB));
            var missing = Preview(enemyless, EnemyTarget);
            Assert.That(missing.Before.IsEnemylessEncounter, Is.True);
            Assert.That(missing.Before.AllPhysicalEnemiesHaveZeroHp, Is.False);
            Assert.That(missing.Legality, Is.EqualTo(ItemPreviewLegality.Illegal));

            var emptyFormation = Observe(Session(4, enemyHp: Array.Empty<int>()));
            var empty = Preview(emptyFormation, EnemyTarget);
            Assert.That(empty.Before.IsEnemylessEncounter, Is.False);
            Assert.That(empty.Failure, Is.EqualTo(ItemPreviewFailure.InvalidTarget));
        }

        [Test]
        public void EnemylessPreparationDoesNotEraseTemporaryStats()
        {
            var session = Session(1, choice: StageChoice.SelectB);
            var result = Preview(Observe(session), BoardTarget);
            Assert.That(result.After.IsEnemylessEncounter, Is.True);
            Assert.That(result.After.Character.TemporaryAttack, Is.EqualTo(7));
            Execute(session, new UseHandItemAction(new ActionId("no-enemy"), 0, BoardTarget));
            AssertParity(result, Observe(session));
        }

        [TestCase(2)]
        [TestCase(3)]
        public void StarRejectsIneligibleTarget(int star)
        {
            var observation = Observe(Session(6, star: star));
            Assert.That(Preview(observation, BoardTarget).Failure, Is.EqualTo(ItemPreviewFailure.InvalidTarget));
            Assert.That(ItemEffectPreview.EnumerateUseTargets(observation, ItemPreviewSource.Hand, 0), Is.Empty);
        }

        [Test]
        public void StarWithMissingStatsPreservesKnownProjectionAndLegality()
        {
            var observation = Observe(Session(6, missingStarStats: true));
            var result = Preview(observation, BoardTarget);
            Assert.That(result.Legality, Is.EqualTo(ItemPreviewLegality.Legal));
            Assert.That(result.ProjectionStatus, Is.EqualTo(ItemProjectionStatus.Unavailable));
            Assert.That(result.Failure, Is.EqualTo(ItemPreviewFailure.StatsUnavailable));
            Assert.That(result.After.Character.Star, Is.EqualTo(2));
            Assert.That(result.After.Character.EffectiveAttack, Is.Null);
            Assert.That(result.After.Character.RequiresStatsReroll, Is.True);
            Assert.That(ItemEffectPreview.EnumerateUseTargets(observation, ItemPreviewSource.Hand, 0), Has.Count.EqualTo(1));
        }

        [Test]
        public void PermanentStarReplacesKeyBonusWithoutMergingOrMoving()
        {
            var session = Session(7, extraItem: 6, extraReserve: true);
            Execute(session, new UseHandItemAction(new ActionId("key"), 0, BoardTarget));
            var before = Observe(session);
            Assert.That(before.Board.Slots[0].Character.Attack, Is.EqualTo(4));
            var result = Preview(before, BoardTarget, 2);
            Assert.That(result.After.Character.Attack, Is.EqualTo(6.5));
            Execute(session, new UseHandItemAction(new ActionId("star"), 2, BoardTarget));
            var after = Observe(session);
            AssertParity(result, after);
            Assert.That(after.Characters, Has.Count.EqualTo(3));
            Assert.That(after.Hand.Slots[1].Character.Star, Is.EqualTo(1));
            Assert.That(after.Hand.Slots[3].Character.Star, Is.EqualTo(1));
            Assert.That(new[] { 5, 7 }, Does.Contain(after.Board.Slots[0].Character.Attack));
            Assert.That(after.Player.Gold, Is.EqualTo(before.Player.Gold));
            string id = after.Board.Slots[0].Character.InstanceId;
            Execute(session, new SellCharacterAction(new ActionId("sell-star"), id));
            Assert.That(Observe(session).Player.Gold - after.Player.Gold, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EventUsesBoardOrHandWithoutConsumingHandItem(bool useReserve)
        {
            var session = Session(1, eventPending: true);
            var before = Observe(session);
            var candidates = ItemEffectPreview.EnumerateUseTargets(before, ItemPreviewSource.Event);
            Assert.That(candidates.Select(x => x.Target.Value.Kind),
                Is.EqualTo(new[] { ItemUseTargetKind.BoardCharacter, ItemUseTargetKind.HandCharacter }));
            var result = candidates[useReserve ? 1 : 0];
            Assert.That(result.After.EventStatus, Is.EqualTo(EventItemStatus.Consumed));
            Assert.That(result.ConsumedHandSlot, Is.Null);
            Assert.That(result.After.HandOccupied, Is.EqualTo(result.Before.HandOccupied));
            Assert.That(result.After.GetHandItem(0), Is.SameAs(before.Hand.Slots[0].Item));
            Execute(session, new UseEventItemAction(new ActionId("event"), result.Target.Value));
            var after = Observe(session);
            AssertParity(result, after);
            Assert.That(ItemEffectPreview.PreviewUse(after, ItemPreviewSource.Event, BoardTarget).Failure,
                Is.EqualTo(ItemPreviewFailure.ItemUnavailable));
        }

        [Test]
        public void SourcePhaseEmptySlotAndTargetChecksAreDistinct()
        {
            var hand = Observe(Session(6));
            var pending = Observe(Session(6, eventPending: true));
            Assert.That(Preview(hand, ReserveTarget).Failure, Is.EqualTo(ItemPreviewFailure.InvalidTarget));
            Assert.That(Preview(hand, Target(ItemUseTargetKind.BoardCharacter, 1)).Failure,
                Is.EqualTo(ItemPreviewFailure.InvalidTarget));
            Assert.That(Preview(hand, BoardTarget, 1).Failure, Is.EqualTo(ItemPreviewFailure.ItemUnavailable));
            Assert.That(Preview(pending, BoardTarget).Failure, Is.EqualTo(ItemPreviewFailure.InvalidPhase));
            Assert.That(ItemEffectPreview.PreviewUse(hand, ItemPreviewSource.Event, BoardTarget).Failure,
                Is.EqualTo(ItemPreviewFailure.InvalidPhase));
            Assert.That(ItemEffectPreview.PreviewUse(pending, ItemPreviewSource.Event, BoardTarget, 0).Failure,
                Is.EqualTo(ItemPreviewFailure.InvalidSource));
            Assert.That(ItemEffectPreview.PreviewUse(pending, ItemPreviewSource.Event,
                Target(ItemUseTargetKind.HandCharacter, 0)).Failure, Is.EqualTo(ItemPreviewFailure.InvalidTarget));
            Assert.That(ItemEffectPreview.PreviewUse(hand, ItemPreviewSource.Purchased, BoardTarget, 0).Failure,
                Is.EqualTo(ItemPreviewFailure.InvalidSource));
        }

        [Test]
        public void PurchaseAndUseMatchSeparateTransactionsAndCountPriceOnce()
        {
            var session = Session(1, choice: StageChoice.SelectB);
            var observation = Observe(session);
            var offer = observation.Shop.Offers.Single();
            long lastIssuedNumber = session.CurrentState.InstanceFactory.LastIssuedNumber;
            var buy = ItemEffectPreview.PreviewPurchase(observation, offer.OfferId, 3);
            Assert.That(buy.GoldDelta, Is.EqualTo(-3));
            Assert.That(buy.After.GetOfferStatus(offer), Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(buy.After.HandOccupied, Is.EqualTo(buy.Before.HandOccupied + 1));
            Assert.That(buy.After.Character, Is.Null);
            Assert.That(buy.After.GetHandItem(3), Is.SameAs(offer.Item));
            Assert.That(ItemEffectPreview.EnumeratePurchasedUseTargets(observation, buy), Has.Count.EqualTo(1));

            var use = ItemEffectPreview.PreviewUseAfterPurchase(observation, buy, BoardTarget);
            Assert.That(use.Before, Is.SameAs(buy.After));
            Assert.That(use.PurchaseOrigin, Is.SameAs(buy.Before));
            Assert.That(use.GoldDelta, Is.Zero);
            Assert.That(use.GoldDeltaIncludingPurchase, Is.EqualTo(-3));
            Assert.That(use.After.HandOccupied, Is.EqualTo(buy.Before.HandOccupied));
            Assert.That(use.After.GetHandItem(3), Is.Null);
            Assert.That(ItemEffectPreview.PreviewUseAfterPurchase(Observe(session), buy, BoardTarget).Failure,
                Is.EqualTo(ItemPreviewFailure.DifferentObservation));
            Assert.That(ItemEffectPreview.PreviewUseAfterPurchase(observation, use, BoardTarget).Failure,
                Is.EqualTo(ItemPreviewFailure.InvalidPurchase));
            Assert.That(ItemEffectPreview.PreviewUseAfterPurchase(observation, buy, ReserveTarget).Failure,
                Is.EqualTo(ItemPreviewFailure.InvalidTarget));

            Execute(session, new BuyItemAction(new ActionId("buy"), offer.OfferId, 3));
            AssertParity(buy, Observe(session));
            Execute(session, new UseHandItemAction(new ActionId("use-buy"), 3, BoardTarget));
            AssertParity(use, Observe(session));
            Assert.That(session.CurrentState.InstanceFactory.LastIssuedNumber, Is.EqualTo(lastIssuedNumber));
        }

        [Test]
        public void PurchaseRejectsWrongShopOfferOccupiedFullAndUnaffordable()
        {
            var observation = Observe(Session(1, choice: StageChoice.SelectB, gold: 2));
            string offer = observation.Shop.Offers.Single().OfferId;
            Assert.That(ItemEffectPreview.PreviewPurchase(observation, offer, 3).Failure,
                Is.EqualTo(ItemPreviewFailure.InsufficientGold));
            Assert.That(ItemEffectPreview.PreviewPurchase(observation, offer, 0).Failure,
                Is.EqualTo(ItemPreviewFailure.HandSlotUnavailable));
            Assert.That(ItemEffectPreview.PreviewPurchase(observation, "unknown", 3).Failure,
                Is.EqualTo(ItemPreviewFailure.InvalidOffer));
            var characterShop = Observe(Session(1));
            Assert.That(ItemEffectPreview.PreviewPurchase(characterShop, characterShop.Shop.Offers[0].OfferId, 3).Failure,
                Is.EqualTo(ItemPreviewFailure.InvalidShop));
            var full = Observe(Session(1, choice: StageChoice.SelectB, fullHand: true));
            Assert.That(full.Hand.EmptySlotCount, Is.Zero);
            Assert.That(ItemEffectPreview.PreviewPurchase(full, full.Shop.Offers[0].OfferId, 4).Failure,
                Is.EqualTo(ItemPreviewFailure.HandSlotUnavailable));
        }

        [Test]
        public void UnsupportedEffectDoesNotBecomeAZeroEffect()
        {
            var custom = new ItemDefinition("block", "block", 1, "", ItemEffectTarget.Player,
                ItemEffectDuration.ThisBattle, new[] { Effect(ItemEffectKind.Block, 2) });
            var result = Preview(Observe(Session(1, custom: custom)), PlayerTarget);
            Assert.That(result.Legality, Is.EqualTo(ItemPreviewLegality.Unknown));
            Assert.That(result.ProjectionStatus, Is.EqualTo(ItemProjectionStatus.Unsupported));
            Assert.That(result.IsZeroEffect, Is.Null);
        }

        [Test]
        public void InvalidTargetEffectPairMatchesFormalRejection()
        {
            var custom = new ItemDefinition("invalid", "invalid", 1, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { Effect(ItemEffectKind.Attack, 1) });
            var session = Session(1, custom: custom);
            var state = session.CurrentState;
            var result = Preview(Observe(session), PlayerTarget);
            Assert.That(result.Legality, Is.EqualTo(ItemPreviewLegality.Illegal));
            Assert.That(result.ProjectionStatus, Is.EqualTo(ItemProjectionStatus.NotApplicable));
            Assert.That(result.Failure, Is.EqualTo(ItemPreviewFailure.InvalidDefinition));
            Assert.That(session.ExecuteAction(new UseHandItemAction(new ActionId("invalid"), 0, PlayerTarget))
                .Succeeded, Is.False);
            Assert.That(session.CurrentState, Is.SameAs(state));
        }

        [Test]
        public void RepeatedAndFailedPreviewsLeaveObservationSessionAndStrategyRandomUnchanged()
        {
            var session = Session(6);
            var observation = Observe(session);
            var state = session.CaptureCheckpoint();
            string observationFingerprint = SimulationStateFingerprint.Create(observation);
            string sessionFingerprint = SimulationStateFingerprint.Create(state);
            var random = StrategyRandomSource.Create(91, "HeuristicStrategy", "16");
            var strategyRng = random.CaptureState();

            var first = Preview(observation, BoardTarget);
            Assert.That(Preview(observation, BoardTarget).After.Character.Attack, Is.EqualTo(first.After.Character.Attack));
            Assert.That(Preview(observation, ReserveTarget).Succeeded, Is.False);
            Assert.That(observation.Hand.Slots[0].Item.DefinitionId, Is.EqualTo("Item006"));
            Assert.That(observation.Board.Slots[0].Character.Star, Is.EqualTo(1));
            Assert.That(session.CaptureCheckpoint(), Is.SameAs(state));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(state.RandomState));
            Assert.That(random.CaptureState(), Is.EqualTo(strategyRng));
            Assert.That(SimulationStateFingerprint.Create(session.CurrentState), Is.EqualTo(sessionFingerprint));
            Assert.That(SimulationStateFingerprint.Create(observation), Is.EqualTo(observationFingerprint));
            Assert.That(HeuristicStrategy.StableVersion, Is.EqualTo("7"));
            Assert.That(SimulationTrialCheckpoint.CurrentSchemaVersion, Is.EqualTo(2));
        }

        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(13)]
        [TestCase(14)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(4)]
        public void ExistingStrategyDecisionAndIdentityAreUnaffectedByPreview(int version)
        {
            IPlayerStrategy strategy;
            switch (version)
            {
                case 4: strategy = new RandomStrategy(); break;
                case 7: strategy = new HeuristicStrategy(); break;
                case 8: strategy = HeuristicStrategy.CreateV8(); break;
                case 9: strategy = HeuristicStrategy.CreateV9(); break;
                case 10: strategy = HeuristicStrategy.CreateV10(); break;
                case 11: strategy = HeuristicStrategy.CreateV11(); break;
                case 12: strategy = HeuristicStrategy.CreateV12(); break;
                case 13: strategy = HeuristicStrategy.CreateV13(); break;
                case 14: strategy = HeuristicStrategy.CreateV14(); break;
                case 15: strategy = HeuristicStrategy.CreateV15(); break;
                default: strategy = HeuristicStrategy.CreateV16(); break;
            }
            var session = Session(6, extraItem: 1);
            var observation = Observe(session);
            var random = StrategyRandomSource.Create(91, strategy.StrategyId, strategy.Version).CaptureState();
            var context = new StrategyContext(strategy.StrategyId, strategy.Version, 0, 0, new ActionId("decision"),
                StrategyState.EmptyFor(strategy.StrategyId, strategy.Version), random);
            string fingerprint = HeuristicStrategy.GetV16ObservationFingerprint(observation);
            string configuration = HeuristicStrategy.GetV16ConfigurationIdentity();
            var before = strategy.ChooseAction(observation, context);
            var preview = Preview(observation, BoardTarget);
            Assert.That(preview.Succeeded, Is.True);
            Assert.That(new PermanentItemEffectEvaluator(observation).Evaluate(preview).Succeeded, Is.True);
            var temporary = new TemporaryItemEffectEvaluator(observation).Evaluate(Preview(observation, BoardTarget, 2));
            Assert.That(temporary.Succeeded, Is.True);
            var integrated = new IntegratedItemEffectEvaluator(observation).Evaluate(Preview(observation, BoardTarget, 2));
            Assert.That(integrated.EffectValue, Is.EqualTo(temporary.EffectValue));
            var transaction = new ItemTransactionValueEvaluator(observation, totalEncounterCount: 4)
                .EvaluateSell(2, SpaceDemand.UnknownDemand);
            Assert.That(transaction.Status, Is.EqualTo(ItemTransactionStatus.Evaluated));
            var after = strategy.ChooseAction(Observe(session), context);
            Assert.That(strategy.Version, Is.EqualTo(version.ToString()));
            Assert.That(SimulationStateFingerprint.Create(after.Action),
                Is.EqualTo(SimulationStateFingerprint.Create(before.Action)));
            Assert.That(after.NextState.Payload, Is.EqualTo(before.NextState.Payload));
            Assert.That(SimulationStateFingerprint.Create(after.NextStrategyRngState),
                Is.EqualTo(SimulationStateFingerprint.Create(before.NextStrategyRngState)));
            Assert.That(HeuristicStrategy.GetV16ObservationFingerprint(Observe(session)), Is.EqualTo(fingerprint));
            Assert.That(HeuristicStrategy.GetV16ConfigurationIdentity(), Is.EqualTo(configuration));
            var eventSession = Session(1, eventPending: true);
            var pending = Observe(eventSession);
            var originalEvent = strategy.ChooseAction(pending, context);
            var evaluation = new EventItemValueEvaluator(pending).Evaluate();
            Assert.That(evaluation.MustUse, Is.True);
            var repeatedEvent = strategy.ChooseAction(Observe(eventSession), context);
            Assert.That(SimulationStateFingerprint.Create(repeatedEvent.Action),
                Is.EqualTo(SimulationStateFingerprint.Create(originalEvent.Action)));
            Assert.That(repeatedEvent.NextState.Payload, Is.EqualTo(originalEvent.NextState.Payload));
            Assert.That(SimulationStateFingerprint.Create(repeatedEvent.NextStrategyRngState),
                Is.EqualTo(SimulationStateFingerprint.Create(originalEvent.NextStrategyRngState)));
            var choice = StageChoiceValueEvaluatorTests.Observe(4, 30, 7, 1, 0);
            var originalChoice = strategy.ChooseAction(choice, context);
            var values = new StageChoiceValueEvaluator(choice, 25).Evaluate();
            Assert.That(values.RequiresLegacyFallback, Is.False);
            var repeatedChoice = strategy.ChooseAction(choice, context);
            Assert.That(SimulationStateFingerprint.Create(repeatedChoice.Action),
                Is.EqualTo(SimulationStateFingerprint.Create(originalChoice.Action)));
            Assert.That(repeatedChoice.NextState.Payload, Is.EqualTo(originalChoice.NextState.Payload));
            Assert.That(SimulationStateFingerprint.Create(repeatedChoice.NextStrategyRngState),
                Is.EqualTo(SimulationStateFingerprint.Create(originalChoice.NextStrategyRngState)));
        }

        private static ItemPreviewResult Preview(GameObservation observation, GameActionTarget target, int slot = 0) =>
            ItemEffectPreview.PreviewUse(observation, ItemPreviewSource.Hand, target, slot);

        private static void AssertParity(ItemPreviewResult result, GameObservation actual)
        {
            Assert.That(actual.Player.Gold, Is.EqualTo(result.After.Gold));
            Assert.That(actual.Player.HP, Is.EqualTo(result.After.HP));
            Assert.That(actual.Hand.SlotCount - actual.Hand.EmptySlotCount, Is.EqualTo(result.After.HandOccupied));
            Assert.That(actual.Characters.Count, Is.EqualTo(result.Before.Observation.Characters.Count));
            Assert.That(actual.EventItem?.Status, Is.EqualTo(result.After.EventStatus));
            if (result.After.ChangedHandItemSlot.HasValue)
                Assert.That(actual.Hand.Slots[result.After.ChangedHandItemSlot.Value].Item?.DefinitionId,
                    Is.EqualTo(result.After.HandItemAfter?.DefinitionId));
            if (result.After.PurchasedOfferId != null)
                Assert.That(actual.Shop.Offers.Single(x => x.OfferId == result.After.PurchasedOfferId).Status,
                    Is.EqualTo(ShopOfferStatus.Purchased));

            var character = result.After.Character;
            if (character != null)
            {
                var ally = actual.Characters.Single(x => x.InstanceId == character.Before.InstanceId);
                Assert.That(ally.DefinitionId, Is.EqualTo(character.Before.DefinitionId));
                Assert.That(ally.RunCost, Is.EqualTo(character.Before.RunCost));
                Assert.That(ally.Star, Is.EqualTo(character.Star));
                Assert.That(ally.SlotIndex, Is.EqualTo(character.Before.SlotIndex));
                Assert.That(ally.LocationKind, Is.EqualTo(character.Before.LocationKind));
                Assert.That(ally.TemporaryAttackBonus, Is.EqualTo(character.TemporaryAttack));
                Assert.That(ally.TemporaryDefenseBonus, Is.EqualTo(character.TemporaryDefense));
                if (!character.RequiresStatsReroll)
                {
                    Assert.That(ally.Attack, Is.EqualTo(character.Attack));
                    Assert.That(ally.Defense, Is.EqualTo(character.Defense));
                }
            }

            Assert.That(actual.Enemies.Count, Is.EqualTo(result.Before.Observation.Enemies.Count));
            Assert.That(actual.Enemies.Where(x => x.HP > 0).Select(x => x.SlotIndex),
                Is.EqualTo(result.After.LivingEnemySlots));
            foreach (var change in result.After.Enemies)
            {
                var enemy = actual.Enemies.Single(x => x.SlotIndex == change.Before.SlotIndex);
                Assert.That(enemy.InstanceId, Is.EqualTo(change.Before.InstanceId));
                Assert.That(enemy.DefinitionId, Is.EqualTo(change.Before.DefinitionId));
                Assert.That(enemy.HP, Is.EqualTo(change.HP));
                Assert.That(enemy.MaximumHP, Is.EqualTo(change.MaximumHP));
                Assert.That(enemy.Defense, Is.EqualTo(change.Defense));
                Assert.That(enemy.SlotBonusDefense, Is.EqualTo(change.PreparationDefenseBonus));
                Assert.That(enemy.SlotBonusHP, Is.EqualTo(change.PreparationHpBonus));
                Assert.That(enemy.Attack, Is.EqualTo(change.Before.Attack));
                Assert.That(enemy.Gap, Is.EqualTo(change.Before.Gap));
                Assert.That(enemy.Cooldown, Is.EqualTo(change.Before.Cooldown));
                Assert.That(SimulationStateFingerprint.Create(enemy.GetInitialTarget()),
                    Is.EqualTo(SimulationStateFingerprint.Create(change.Before.GetInitialTarget())));
            }
        }

        internal static GameSession Session(int item, int extraItem = 0, int hp = 25, int maxHp = 30,
            int gold = 15, int star = 1, bool missingStarStats = false, bool eventPending = false,
            StageChoice choice = StageChoice.SelectA, int[] enemyHp = null, bool fullHand = false,
            bool extraReserve = false, ItemDefinition custom = null, int reserveStar = 1)
        {
            var enemies = (enemyHp ?? new[] { 10 }).Select((health, index) =>
                new EnemyDefinition("enemy" + index, "Enemy", 4, 0, health,
                    null, null, null, "T001", null, null, null)).ToArray();
            var formation = new EnemyFormationDefinition("formation", Enumerable.Range(0, 6)
                .Select(index => index < enemies.Length
                    ? new EnemyFormationSlotDefinition(enemies[index].DefinitionId, 2, 1)
                    : new EnemyFormationSlotDefinition(null, 0, 0)));
            var stage = new StageProgressConfig(4, 4, eventPending ? 2 : 1, 4, 100, 0, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var stats = new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(1, new[] { new CharacterStatOption(3, 1, 1) },
                    missingStarStats ? null : new[] { new CharacterStatOption(5, 2, 1), new CharacterStatOption(7, 3, 3) },
                    new[] { new CharacterStatOption(10, 4, 1) })
            });
            var firstItem = custom ?? Items[item - 1];
            var definitions = new List<ItemDefinition> { firstItem };
            if (extraItem != 0 && extraItem != item) definitions.Add(Items[extraItem - 1]);
            var config = new GameSessionConfiguration(stage,
                new EconomyRulesConfiguration(4, 2, 1, new[] { 1, 2 }, 10, 1, 3, 3, 1, 0),
                new CharacterShopConfiguration(new[] { 1, 0, 0, 0, 0 }, new[] { 15, 0, 0, 0, 0 },
                    new[] { new[] { 100, 0, 0, 0, 0 } }, new[] { 1 }),
                stats, new StageRewardConfiguration(5, 2), new BattleConfig(5),
                new EconomyState(gold, 0, 1, 3, gold, 0, 1, maxHp, maxHp),
                new[] { Ally }, new[] { Ally }, new[] { Ally }, definitions, enemies,
                new[] { new EncounterPoolDefinition("normal", new[] { 0 }, StageEnemyCategory.Normal, new[] { formation }) },
                1, battleTargetDefinitions: new[] { new BattleTargetDefinition("T001",
                    BattleTargetFilter.AllBoard, BattleTargetSelection.Uniform) }, eventItemDefinition: Items[5]);
            var session = GameSession.Create(config, 19);
            if (!eventPending) Execute(session, new SelectStageChoiceAction(new ActionId("choice"), choice));
            var state = session.CurrentState;

            CharacterInstance CreateAlly(string id, int rank)
            {
                var ally = new CharacterInstance(id, Ally, 1, rank, poolCopies: 1);
                Assert.That(ally.EnsureStats(Ally, stats, new SimulationRandomStreams(99).Stats), Is.True);
                return ally;
            }

            var active = CreateAlly("fixture-board", star);
            active.TemporaryAttackBonus = 4;
            active.TemporaryDefenseBonus = 1;
            var reserve = CreateAlly("fixture-hand", reserveStar);
            var owned = new List<CharacterInstance> { active, reserve };
            Assert.That(state.Board.TryWithCharacterAt(0, active.InstanceId, out var board), Is.True);
            Assert.That(state.Hand.TryWithSlot(0, HandSlotContent.Item(firstItem.DefinitionId), out var hand), Is.True);
            Assert.That(hand.TryWithSlot(1, HandSlotContent.Character(reserve.InstanceId), out hand), Is.True);
            if (extraReserve)
            {
                var material = CreateAlly("fixture-material", 1);
                owned.Add(material);
                Assert.That(hand.TryWithSlot(3, HandSlotContent.Character(material.InstanceId), out hand), Is.True);
            }
            if (extraItem != 0)
                Assert.That(hand.TryWithSlot(2, HandSlotContent.Item(Items[extraItem - 1].DefinitionId), out hand), Is.True);
            if (fullHand)
                for (int index = 2; index < hand.SlotCount; index++)
                    Assert.That(hand.TryWithSlot(index, HandSlotContent.Item(firstItem.DefinitionId), out hand), Is.True);

            var poolEntry = state.CharacterPool.Entries[Ally.DefinitionId];
            var pool = new CharacterPool(new[] { new CharacterPoolEntry(Ally.DefinitionId,
                poolEntry.RemainingCopies - owned.Count, poolEntry.InitialStockLimit, poolEntry.ReservedCopies, owned.Count) });
            state = state.With(board: board, hand: hand, characterPool: pool,
                ownership: new CharacterOwnershipState(owned),
                economy: new EconomyState(gold, 0, 1, 3, gold, 0, 1, hp, maxHp));
            if (eventPending)
            {
                var battle = StageProgressRules.BeginBattle(state.Stage).NextState;
                var won = StageProgressRules.ResolveBattle(battle, stage, true).NextState;
                var next = StageProgressRules.CompletePendingReward(won, stage, state.RandomState.Event).NextState;
                state = new ActionExecutionState(next, state.Economy, state.CharacterPool, state.InstanceFactory,
                    state.Board, state.Hand, state.Ownership, state.ItemEffects, state.RandomState,
                    eventItem: new EventItemState("Item006"));
                var entry = state.CharacterPool.Entries[Ally.DefinitionId];
                state = state.With(characterPool: new CharacterPool(new[] { new CharacterPoolEntry(Ally.DefinitionId,
                    entry.RemainingCopies + entry.ReservedCopies, entry.InitialStockLimit, 0, entry.OwnedCopies) }));
            }
            return GameSession.Restore(config, state);
        }

        internal static GameObservation Observe(GameSession session)
        {
            var result = session.GetObservation();
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            return result.Observation;
        }

        internal static ActionDispatchResult Execute(GameSession session, GameAction action)
        {
            var result = session.ExecuteAction(action);
            Assert.That(result.Succeeded, Is.True, result.Error.Detail);
            return result;
        }

        private static GameActionTarget Target(ItemUseTargetKind kind, int slot) =>
            new GameActionTarget(kind, slot, 0, true);

        private static ItemEffectDefinition Effect(ItemEffectKind kind, int value) => new ItemEffectDefinition(kind, value);

        private static ItemDefinition Item(int id, int price, ItemEffectTarget target, ItemEffectDuration duration,
            params ItemEffectDefinition[] effects) => new ItemDefinition("Item00" + id, "Item00" + id,
                price, string.Empty, target, duration, effects);
    }
}
