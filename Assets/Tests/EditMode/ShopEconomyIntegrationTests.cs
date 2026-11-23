using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Tests
{
    public sealed class ShopEconomyIntegrationTests
    {
        [Test]
        public void CharacterShop_AssignPurchaseRefreshAndSalePreserveInventoryAndGold()
        {
            ShopFixture fixture = CreateFixture(1001UL, 7UL);
            Assert.That(fixture.Shop.SlotCount, Is.EqualTo(3));
            Assert.That(fixture.Shop.Offers.Count, Is.EqualTo(3));
            Assert.That(fixture.Shop.Offers.All(o => o.Kind == ShopOfferKind.Character), Is.True);
            AssertPoolAndReservationInvariant(fixture.Shop, fixture.Pool);
            Assert.That(fixture.Costs.Costs.Values.OrderBy(cost => cost), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));

            ShopOffer selected = fixture.Shop.Offers[0];
            int runCost = fixture.Costs.GetCost(selected.DefinitionId);
            int reservedBeforePurchase = fixture.Pool.Entries[selected.DefinitionId].ReservedCopies;
            Assert.That(selected.CharacterCost, Is.EqualTo(runCost));
            Assert.That(selected.DisplayPrice, Is.EqualTo(runCost));

            EconomyState economy = MakeEconomy(20);
            CharacterInstanceFactory factory = new CharacterInstanceFactory("INT");
            Pcg32State statsBefore = fixture.Streams.Stats.CaptureState();
            var gate = new PlacementGate(true);
            CharacterPurchaseResult purchase = ShopTransactionRules.TryPurchaseCharacter(fixture.Shop,
                selected.OfferId, fixture.Definitions, fixture.Costs, economy, fixture.Pool,
                factory, MakeStats(), fixture.Streams.Stats, gate);

            Assert.That(purchase.Succeeded, Is.True);
            Assert.That(gate.Calls, Is.EqualTo(1));
            Assert.That(purchase.PurchasedCharacter.RunCost, Is.EqualTo(runCost));
            Assert.That(purchase.PurchasedCharacter.HasRolledStats, Is.True);
            Assert.That(purchase.NextEconomy.Gold, Is.EqualTo(20 - runCost));
            Assert.That(purchase.NextShop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(purchase.NextPool.Entries[selected.DefinitionId].ReservedCopies,
                Is.EqualTo(reservedBeforePurchase - 1),
                "Only the purchased Offer reservation moves from reserved to owned.");
            Assert.That(purchase.NextPool.Entries[selected.DefinitionId].OwnedCopies, Is.EqualTo(1));
            Assert.That(factory.LastIssuedNumber, Is.Zero, "Factory input remains unchanged until the caller adopts the proposed result.");
            AssertStateEqual(statsBefore, fixture.Streams.Stats.CaptureState());

            // The test explicitly adopts successful proposed values, as a future GameSession would.
            ShopState shop = purchase.NextShop;
            CharacterPool pool = purchase.NextPool;
            economy = purchase.NextEconomy;
            factory = purchase.NextInstanceFactory;
            fixture.Streams.Stats.RestoreState(purchase.NextStatsRandomState);
            var owned = new List<CharacterInstance> { purchase.PurchasedCharacter };
            AssertPoolAndReservationInvariant(shop, pool);

            CharacterPool returnedOffers = CharacterShopRules.ReturnUnpurchasedOffers(shop, pool);
            Assert.That(returnedOffers.Entries[selected.DefinitionId].OwnedCopies, Is.EqualTo(1));
            int selectedDefinitionOffers = shop.Offers.Count(candidate =>
                candidate.Kind == ShopOfferKind.Character &&
                candidate.Status == ShopOfferStatus.Available &&
                candidate.DefinitionId == selected.DefinitionId);
            Assert.That(returnedOffers.Entries[selected.DefinitionId].RemainingCopies,
                Is.EqualTo(pool.Entries[selected.DefinitionId].RemainingCopies + selectedDefinitionOffers),
                "The purchased copy is not returned with the still-open Offers.");
            Assert.That(returnedOffers.Entries[selected.DefinitionId].ReservedCopies, Is.Zero);
            foreach (CharacterPoolEntry entry in returnedOffers.Entries.Values)
            {
                Assert.That(entry.ReservedCopies, Is.Zero,
                    "Returning detached Offers releases all unpurchased reservations.");
                Assert.That(entry.RemainingCopies + entry.ReservedCopies + entry.OwnedCopies,
                    Is.EqualTo(entry.InitialStockLimit));
            }

            CharacterShopGenerationResult refreshed = CharacterShopRules.Refresh(shop,
                fixture.Definitions, fixture.Costs, pool, fixture.Configuration, fixture.Streams.Shop);
            shop = refreshed.Shop;
            pool = refreshed.Pool;
            Assert.That(shop.GenerationIndex, Is.EqualTo(1));
            Assert.That(shop.Offers.Count, Is.EqualTo(3));
            Assert.That(pool.Entries[selected.DefinitionId].OwnedCopies, Is.EqualTo(1));
            AssertPoolAndReservationInvariant(shop, pool);

            CharacterSaleResult sale = ShopTransactionRules.TrySellCharacter(
                purchase.PurchasedCharacter, owned, economy, pool);
            Assert.That(sale.Succeeded, Is.True);
            Assert.That(sale.ReturnedCopies, Is.EqualTo(1));
            Assert.That(sale.GoldGained, Is.EqualTo(runCost));
            Assert.That(sale.NextEconomy.Gold, Is.EqualTo(20));
            Assert.That(sale.NextOwnedCharacters, Is.Empty);
            Assert.That(sale.NextPool.Entries[selected.DefinitionId].OwnedCopies, Is.Zero);
            AssertPoolAndReservationInvariant(shop, sale.NextPool);

            Assert.That(fixture.Definitions.All(definition => definition.BaseCost == 5), Is.True,
                "Per-run Cost assignment and transactions do not modify definitions.");
        }

        [Test]
        public void ItemShopTransitionReturnsOnlyUnpurchasedOffersAndCanReturnToCharacterShop()
        {
            ShopFixture fixture = CreateFixture(2002UL, 3UL);
            EconomyState economy = MakeEconomy(20);
            var factory = new CharacterInstanceFactory("ITEM");
            ShopOffer selected = fixture.Shop.Offers[0];
            var gate = new PlacementGate(true);
            CharacterPurchaseResult characterBuy = ShopTransactionRules.TryPurchaseCharacter(fixture.Shop,
                selected.OfferId, fixture.Definitions, fixture.Costs, economy, fixture.Pool,
                factory, MakeStats(), fixture.Streams.Stats, gate);
            Assert.That(characterBuy.Succeeded, Is.True);

            ShopState characterShopAfterBuy = characterBuy.NextShop;
            CharacterPool poolAfterBuy = characterBuy.NextPool;
            economy = characterBuy.NextEconomy;
            factory = characterBuy.NextInstanceFactory;
            fixture.Streams.Stats.RestoreState(characterBuy.NextStatsRandomState);
            AssertPoolAndReservationInvariant(characterShopAfterBuy, poolAfterBuy);

            var items = new[]
            {
                new ItemDefinition("item-1", "Potion", 2),
                new ItemDefinition("item-2", "Stone", 3),
                new ItemDefinition("item-3", "Charm", 4),
                new ItemDefinition("item-4", "Token", 5),
                new ItemDefinition("item-1", "Potion", 2)
            };
            Pcg32State shopRandomBefore = fixture.Streams.Shop.CaptureState();
            ItemShopGenerationResult itemGeneration = ItemShopRules.Generate(characterShopAfterBuy,
                poolAfterBuy, items, 1, 1, 3, 0, fixture.Streams.Shop);

            Assert.That(itemGeneration.NextShop.Kind, Is.EqualTo(ShopKind.Item));
            Assert.That(itemGeneration.NextShop.Offers.Count, Is.EqualTo(3));
            Assert.That(itemGeneration.NextShop.Offers.Select(o => o.DefinitionId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(itemGeneration.NextShopRandomState.State, Is.Not.EqualTo(shopRandomBefore.State));
            AssertStateEqual(shopRandomBefore, fixture.Streams.Shop.CaptureState(),
                "Item shop generation returns its proposed Shop RNG state instead of mutating the input.");

            CharacterPool poolAfterItemTransition = itemGeneration.NextCharacterPool;
            Assert.That(poolAfterItemTransition.Entries[selected.DefinitionId].OwnedCopies, Is.EqualTo(1));
            Assert.That(poolAfterItemTransition.Entries[selected.DefinitionId].ReservedCopies, Is.Zero);
            int selectedDefinitionOffers = characterShopAfterBuy.Offers.Count(candidate =>
                candidate.Kind == ShopOfferKind.Character &&
                candidate.Status == ShopOfferStatus.Available &&
                candidate.DefinitionId == selected.DefinitionId);
            Assert.That(poolAfterItemTransition.Entries[selected.DefinitionId].RemainingCopies,
                Is.EqualTo(poolAfterBuy.Entries[selected.DefinitionId].RemainingCopies + selectedDefinitionOffers),
                "The purchased copy remains owned when the shop changes to Items.");
            AssertPoolAndReservationInvariant(itemGeneration.NextShop, poolAfterItemTransition);
            foreach (ShopOffer oldOffer in characterShopAfterBuy.Offers)
            {
                if (oldOffer.Status == ShopOfferStatus.Purchased) continue;
                Assert.That(poolAfterItemTransition.Entries[oldOffer.DefinitionId].ReservedCopies,
                    Is.Zero, "Every unpurchased character Offer reservation is returned.");
            }

            fixture.Streams.Shop.RestoreState(itemGeneration.NextShopRandomState);
            ShopOffer itemOffer = itemGeneration.NextShop.Offers[0];
            int goldBeforeItem = economy.Gold;
            CharacterPool poolBeforeItemPurchase = poolAfterItemTransition;
            ItemPurchaseResult itemBuy = ItemShopRules.TryPurchaseItem(itemGeneration.NextShop,
                itemOffer.OfferId, items, economy);
            Assert.That(itemBuy.Succeeded, Is.True);
            Assert.That(itemBuy.PurchasedItem.DefinitionId, Is.EqualTo(itemOffer.DefinitionId));
            Assert.That(itemBuy.NextEconomy.Gold, Is.EqualTo(goldBeforeItem - itemOffer.DisplayPrice));
            Assert.That(itemBuy.NextShop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(poolAfterItemTransition, Is.SameAs(poolBeforeItemPurchase),
                "Item purchase does not alter the character pool.");

            ItemPurchaseResult duplicateItemBuy = ItemShopRules.TryPurchaseItem(itemBuy.NextShop,
                itemOffer.OfferId, items, itemBuy.NextEconomy);
            Assert.That(duplicateItemBuy.Succeeded, Is.False);
            Assert.That(duplicateItemBuy.NextShop, Is.SameAs(itemBuy.NextShop));
            Assert.That(duplicateItemBuy.NextEconomy, Is.SameAs(itemBuy.NextEconomy));

            CharacterShopGenerationResult characterShopAgain = CharacterShopRules.Generate(
                fixture.Definitions, fixture.Costs, poolAfterItemTransition, fixture.Configuration,
                1, 2, 1, fixture.Streams.Shop);
            Assert.That(characterShopAgain.Shop.Kind, Is.EqualTo(ShopKind.Character));
            Assert.That(characterShopAgain.Shop.Offers.Count, Is.EqualTo(3));
            Assert.That(characterShopAgain.Pool.Entries[selected.DefinitionId].OwnedCopies, Is.EqualTo(1));
            AssertPoolAndReservationInvariant(characterShopAgain.Shop, characterShopAgain.Pool);

            CharacterSaleResult sale = ShopTransactionRules.TrySellCharacter(characterBuy.PurchasedCharacter,
                new[] { characterBuy.PurchasedCharacter }, itemBuy.NextEconomy, characterShopAgain.Pool);
            Assert.That(sale.Succeeded, Is.True);
            AssertPoolAndReservationInvariant(characterShopAgain.Shop, sale.NextPool);
        }

        [Test]
        public void FailedTransactionsAreAtomicAndExcessReservationsCannotBeReturned()
        {
            ShopFixture fixture = CreateFixture(3003UL, 5UL);
            ShopOffer offer = fixture.Shop.Offers[0];
            EconomyState insufficientGold = MakeEconomy(0);
            var factory = new CharacterInstanceFactory("FAIL");
            Pcg32State statsBefore = fixture.Streams.Stats.CaptureState();
            PlacementGate approval = new PlacementGate(true);

            CharacterPurchaseResult tooExpensive = ShopTransactionRules.TryPurchaseCharacter(fixture.Shop,
                offer.OfferId, fixture.Definitions, fixture.Costs, insufficientGold, fixture.Pool,
                factory, MakeStats(), fixture.Streams.Stats, approval);
            Assert.That(tooExpensive.Succeeded, Is.False);
            Assert.That(tooExpensive.FailureReason, Is.EqualTo(CharacterPurchaseFailureReason.InsufficientGold));
            Assert.That(tooExpensive.NextShop, Is.SameAs(fixture.Shop));
            Assert.That(tooExpensive.NextPool, Is.SameAs(fixture.Pool));
            Assert.That(tooExpensive.NextEconomy, Is.SameAs(insufficientGold));
            Assert.That(tooExpensive.NextInstanceFactory, Is.SameAs(factory));
            Assert.That(approval.Calls, Is.Zero);
            Assert.That(factory.LastIssuedNumber, Is.Zero);
            AssertStateEqual(statsBefore, fixture.Streams.Stats.CaptureState());

            EconomyState enoughGold = MakeEconomy(20);
            CharacterPurchaseResult placementDenied = ShopTransactionRules.TryPurchaseCharacter(fixture.Shop,
                offer.OfferId, fixture.Definitions, fixture.Costs, enoughGold, fixture.Pool,
                factory, MakeStats(), fixture.Streams.Stats, new PlacementGate(false));
            Assert.That(placementDenied.Succeeded, Is.False);
            Assert.That(placementDenied.FailureReason, Is.EqualTo(CharacterPurchaseFailureReason.PlacementNotApproved));
            Assert.That(placementDenied.NextEconomy, Is.SameAs(enoughGold));
            Assert.That(placementDenied.NextShop, Is.SameAs(fixture.Shop));
            Assert.That(placementDenied.NextPool, Is.SameAs(fixture.Pool));
            Assert.That(placementDenied.NextInstanceFactory, Is.SameAs(factory));
            Assert.That(factory.LastIssuedNumber, Is.Zero);
            AssertStateEqual(statsBefore, fixture.Streams.Stats.CaptureState());

            CharacterPurchaseResult invalidId = ShopTransactionRules.TryPurchaseCharacter(fixture.Shop,
                "missing-offer", fixture.Definitions, fixture.Costs, enoughGold, fixture.Pool,
                factory, MakeStats(), fixture.Streams.Stats, new PlacementGate(true));
            Assert.That(invalidId.Succeeded, Is.False);
            Assert.That(invalidId.FailureReason, Is.EqualTo(CharacterPurchaseFailureReason.InvalidOffer));
            Assert.That(invalidId.NextShop, Is.SameAs(fixture.Shop));
            Assert.That(invalidId.NextPool, Is.SameAs(fixture.Pool));
            Assert.That(invalidId.NextEconomy, Is.SameAs(enoughGold));
            Assert.That(invalidId.NextInstanceFactory, Is.SameAs(factory));
            AssertStateEqual(statsBefore, fixture.Streams.Stats.CaptureState());

            CharacterPurchaseResult successful = ShopTransactionRules.TryPurchaseCharacter(fixture.Shop,
                offer.OfferId, fixture.Definitions, fixture.Costs, enoughGold, fixture.Pool,
                factory, MakeStats(), fixture.Streams.Stats, new PlacementGate(true));
            Assert.That(successful.Succeeded, Is.True);
            CharacterInstanceFactory committedFactory = successful.NextInstanceFactory;
            fixture.Streams.Stats.RestoreState(successful.NextStatsRandomState);
            CharacterPurchaseResult duplicate = ShopTransactionRules.TryPurchaseCharacter(successful.NextShop,
                offer.OfferId, fixture.Definitions, fixture.Costs, successful.NextEconomy, successful.NextPool,
                committedFactory, MakeStats(), fixture.Streams.Stats, new PlacementGate(true));
            Assert.That(duplicate.Succeeded, Is.False);
            Assert.That(duplicate.FailureReason, Is.EqualTo(CharacterPurchaseFailureReason.InvalidOffer));
            Assert.That(duplicate.NextShop, Is.SameAs(successful.NextShop));
            Assert.That(duplicate.NextPool, Is.SameAs(successful.NextPool));
            Assert.That(duplicate.NextEconomy, Is.SameAs(successful.NextEconomy));
            Assert.That(duplicate.NextInstanceFactory, Is.SameAs(committedFactory));
            AssertStateEqual(successful.NextStatsRandomState, fixture.Streams.Stats.CaptureState());

            var ownerList = new List<CharacterInstance> { successful.PurchasedCharacter };
            CharacterSaleResult sold = ShopTransactionRules.TrySellCharacter(successful.PurchasedCharacter,
                ownerList, successful.NextEconomy, successful.NextPool);
            Assert.That(sold.Succeeded, Is.True);
            CharacterSaleResult doubleSale = ShopTransactionRules.TrySellCharacter(successful.PurchasedCharacter,
                sold.NextOwnedCharacters, sold.NextEconomy, sold.NextPool);
            Assert.That(doubleSale.Succeeded, Is.False);
            Assert.That(doubleSale.FailureReason, Is.EqualTo(CharacterSaleFailureReason.NotOwned));
            Assert.That(doubleSale.NextEconomy, Is.SameAs(sold.NextEconomy));
            Assert.That(doubleSale.NextPool, Is.SameAs(sold.NextPool));
            Assert.That(ownerList.Count, Is.EqualTo(1), "Sale proposals do not mutate the input owner collection.");

            Assert.Throws<ArgumentException>(() => new CharacterPoolEntry("over-cap", 15, 15, 1, 0));
            ShopOffer duplicateA = new ShopOffer("bad-a", ShopOfferKind.Character,
                offer.DefinitionId, offer.DisplayPrice, offer.CharacterCost);
            ShopOffer duplicateB = new ShopOffer("bad-b", ShopOfferKind.Character,
                offer.DefinitionId, offer.DisplayPrice, offer.CharacterCost);
            var mismatchedOffers = new ShopState(ShopKind.Character, new[] { duplicateA, duplicateB },
                2, 1, 1);
            var underReservedEntries = fixture.Pool.Entries.Values.Select(entry =>
                entry.DefinitionId == offer.DefinitionId
                    ? new CharacterPoolEntry(entry.DefinitionId, entry.InitialStockLimit - 1,
                        entry.InitialStockLimit, 1, 0)
                    : new CharacterPoolEntry(entry.DefinitionId, entry.InitialStockLimit,
                        entry.InitialStockLimit, 0, 0));
            var underReservedPool = new CharacterPool(underReservedEntries);
            Assert.Throws<InvalidOperationException>(() =>
                CharacterShopRules.ReturnUnpurchasedOffers(mismatchedOffers, underReservedPool));
            Assert.That(underReservedPool.Entries[offer.DefinitionId].RemainingCopies, Is.EqualTo(14));
            Assert.That(underReservedPool.Entries[offer.DefinitionId].ReservedCopies, Is.EqualTo(1));

            EconomyRulesConfiguration economyConfig = MakeEconomyConfig();
            EconomyState poorXPState = MakeEconomy(3);
            EconomyActionResult poorXP = EconomyRules.TryPurchaseExperience(poorXPState, economyConfig, false);
            Assert.That(poorXP.Succeeded, Is.False);
            Assert.That(poorXP.FailureReason, Is.EqualTo(EconomyActionFailureReason.InsufficientGold));
            Assert.That(poorXP.NextState, Is.SameAs(poorXPState));
            EconomyState maxLevelState = new EconomyState(20, 0, 12, 12, 20, 0, 1);
            EconomyActionResult maxXP = EconomyRules.TryPurchaseExperience(maxLevelState, economyConfig, false);
            Assert.That(maxXP.Succeeded, Is.False);
            Assert.That(maxXP.FailureReason, Is.EqualTo(EconomyActionFailureReason.MaximumLevelReached));
            Assert.That(maxXP.NextState, Is.SameAs(maxLevelState));
        }

        [Test]
        public void IntegratedShopEconomyFlow_ReplaysExactlyFromMasterSeedAndTrialIndex()
        {
            IntegratedOutcome first = RunIntegratedFlow(0xA112233445566778UL, 42UL);
            IntegratedOutcome replay = RunIntegratedFlow(0xA112233445566778UL, 42UL);

            CollectionAssert.AreEqual(first.CostAssignments, replay.CostAssignments);
            CollectionAssert.AreEqual(first.InitialOffers, replay.InitialOffers);
            CollectionAssert.AreEqual(first.ItemOffers, replay.ItemOffers);
            CollectionAssert.AreEqual(first.FinalOffers, replay.FinalOffers);
            CollectionAssert.AreEqual(first.FinalPool, replay.FinalPool);
            Assert.That(replay.PurchasedInstanceId, Is.EqualTo(first.PurchasedInstanceId));
            Assert.That(replay.PurchasedAttack, Is.EqualTo(first.PurchasedAttack));
            Assert.That(replay.PurchasedDefense, Is.EqualTo(first.PurchasedDefense));
            Assert.That(replay.PurchasedRunCost, Is.EqualTo(first.PurchasedRunCost));
            Assert.That(replay.PurchasedItemId, Is.EqualTo(first.PurchasedItemId));
            Assert.That(replay.InterestGuaranteed, Is.EqualTo(first.InterestGuaranteed));
            Assert.That(replay.InterestRandomBonus, Is.EqualTo(first.InterestRandomBonus));
            Assert.That(replay.TotalStageReward, Is.EqualTo(first.TotalStageReward));
            Assert.That(replay.FinalEconomy.Gold, Is.EqualTo(first.FinalEconomy.Gold));
            Assert.That(replay.FinalEconomy.XP, Is.EqualTo(first.FinalEconomy.XP));
            Assert.That(replay.FinalEconomy.Level, Is.EqualTo(first.FinalEconomy.Level));
            AssertRandomStatesEqual(first.RandomState, replay.RandomState);

            var noisyStreams = new SimulationRandomStreams(TrialSeedGenerator.Create(99UL, 11UL));
            var quietStreams = new SimulationRandomStreams(TrialSeedGenerator.Create(99UL, 11UL));
            for (int i = 0; i < 100; i++) noisyStreams.Shop.NextInt(0, 1000);
            Assert.That(noisyStreams.Roster.NextInt(0, 100000), Is.EqualTo(quietStreams.Roster.NextInt(0, 100000)));
            Assert.That(noisyStreams.Stats.NextInt(0, 100000), Is.EqualTo(quietStreams.Stats.NextInt(0, 100000)));
            Assert.That(noisyStreams.Economy.NextInt(0, 100000), Is.EqualTo(quietStreams.Economy.NextInt(0, 100000)));
            Assert.That(noisyStreams.Battle.NextInt(0, 100000), Is.EqualTo(quietStreams.Battle.NextInt(0, 100000)));
        }

        private static IntegratedOutcome RunIntegratedFlow(ulong masterSeed, ulong trialIndex)
        {
            ShopFixture fixture = CreateFixture(masterSeed, trialIndex);
            AssertPoolAndReservationInvariant(fixture.Shop, fixture.Pool);
            string[] assignments = fixture.Costs.Costs.OrderBy(pair => pair.Key)
                .Select(pair => pair.Key + ":" + pair.Value).ToArray();
            string[] initialOffers = DescribeOffers(fixture.Shop);

            EconomyState economy = MakeEconomy(20);
            var factory = new CharacterInstanceFactory("RUN");
            ShopOffer selected = fixture.Shop.Offers[0];
            CharacterPurchaseResult purchase = ShopTransactionRules.TryPurchaseCharacter(fixture.Shop,
                selected.OfferId, fixture.Definitions, fixture.Costs, economy, fixture.Pool,
                factory, MakeStats(), fixture.Streams.Stats, new PlacementGate(true));
            Assert.That(purchase.Succeeded, Is.True);
            ShopState shop = purchase.NextShop;
            CharacterPool pool = purchase.NextPool;
            economy = purchase.NextEconomy;
            factory = purchase.NextInstanceFactory;
            fixture.Streams.Stats.RestoreState(purchase.NextStatsRandomState);
            var owned = new List<CharacterInstance> { purchase.PurchasedCharacter };
            AssertPoolAndReservationInvariant(shop, pool);

            CharacterShopGenerationResult refresh = CharacterShopRules.Refresh(shop,
                fixture.Definitions, fixture.Costs, pool, fixture.Configuration, fixture.Streams.Shop);
            shop = refresh.Shop;
            pool = refresh.Pool;
            Assert.That(pool.Entries[purchase.PurchasedCharacter.DefinitionId].OwnedCopies, Is.EqualTo(1));
            AssertPoolAndReservationInvariant(shop, pool);

            CharacterSaleResult sale = ShopTransactionRules.TrySellCharacter(purchase.PurchasedCharacter,
                owned, economy, pool);
            Assert.That(sale.Succeeded, Is.True);
            economy = sale.NextEconomy;
            pool = sale.NextPool;
            owned = sale.NextOwnedCharacters.ToList();
            AssertPoolAndReservationInvariant(shop, pool);
            Assert.That(owned, Is.Empty);

            ItemDefinition[] itemDefinitions = CreateItems();
            ItemShopGenerationResult itemShop = ItemShopRules.Generate(shop, pool, itemDefinitions,
                1, 2, 3, 0, fixture.Streams.Shop);
            shop = itemShop.NextShop;
            pool = itemShop.NextCharacterPool;
            fixture.Streams.Shop.RestoreState(itemShop.NextShopRandomState);
            Assert.That(shop.Kind, Is.EqualTo(ShopKind.Item));
            Assert.That(shop.Offers.Select(o => o.DefinitionId).Distinct().Count(), Is.EqualTo(shop.Offers.Count));
            AssertPoolAndReservationInvariant(shop, pool);
            string[] itemOffers = DescribeOffers(shop);

            ItemPurchaseResult itemBuy = ItemShopRules.TryPurchaseItem(shop,
                shop.Offers[0].OfferId, itemDefinitions, economy);
            Assert.That(itemBuy.Succeeded, Is.True);
            Assert.That(itemBuy.NextShop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(itemShop.NextCharacterPool, Is.SameAs(pool));
            shop = itemBuy.NextShop;
            economy = itemBuy.NextEconomy;

            CharacterShopGenerationResult nextCharacterShop = CharacterShopRules.Generate(
                fixture.Definitions, fixture.Costs, pool, fixture.Configuration,
                1, 2, 1, fixture.Streams.Shop);
            shop = nextCharacterShop.Shop;
            pool = nextCharacterShop.Pool;
            AssertPoolAndReservationInvariant(shop, pool);
            string[] finalOffers = DescribeOffers(shop);

            EconomyRulesConfiguration economyConfig = MakeEconomyConfig();
            EconomyActionResult normalXP = EconomyRules.TryPurchaseExperience(economy, economyConfig, false);
            Assert.That(normalXP.Succeeded, Is.True);
            economy = normalXP.NextState;
            Assert.That(economy.Level, Is.EqualTo(2));
            Assert.That(economy.XP, Is.Zero);
            EconomyActionResult selectBXP = EconomyRules.TryPurchaseExperience(economy, economyConfig, true);
            Assert.That(selectBXP.Succeeded, Is.True);
            economy = selectBXP.NextState;
            Assert.That(economy.Level, Is.EqualTo(2));
            Assert.That(economy.XP, Is.EqualTo(1));

            int goldBeforeReward = economy.Gold;
            Pcg32State economyBeforeReward = fixture.Streams.Economy.CaptureState();
            Pcg32RandomSource expectedInterestRandom = Pcg32RandomSource.FromState(economyBeforeReward);
            InterestCalculationResult expectedInterest = EconomyRules.CalculateInterest(
                goldBeforeReward, economyConfig, expectedInterestRandom);
            StageGoldRewardCalculationResult reward = EconomyRules.CalculateStageGoldReward(
                economyConfig.BaseStageGoldReward, goldBeforeReward, 3, economyConfig, fixture.Streams.Economy);
            Assert.That(reward.Succeeded, Is.True);
            Assert.That(reward.GuaranteedInterest, Is.EqualTo(expectedInterest.GuaranteedInterest));
            Assert.That(reward.RandomBonusInterest, Is.EqualTo(expectedInterest.RandomBonusInterest));
            Assert.That(reward.EmptyHandSlotBonus, Is.EqualTo(1));
            Assert.That(reward.TotalGoldReward, Is.EqualTo(3 + expectedInterest.TotalInterest + 1));
            AssertStateEqual(economyBeforeReward, fixture.Streams.Economy.CaptureState());
            fixture.Streams.Economy.RestoreState(reward.NextRandomState);

            EconomyActionResult appliedReward = EconomyRules.TryAddGold(economy, reward.TotalGoldReward);
            Assert.That(appliedReward.Succeeded, Is.True);
            economy = appliedReward.NextState;
            Assert.That(economy.Gold, Is.EqualTo(goldBeforeReward + reward.TotalGoldReward));
            Assert.That(economy.Level, Is.EqualTo(2));
            Assert.That(economy.XP, Is.EqualTo(1));
            Assert.That(factory.LastIssuedNumber, Is.EqualTo(1));

            return new IntegratedOutcome(assignments, initialOffers, itemOffers, finalOffers,
                CapturePool(pool), purchase.PurchasedCharacter.InstanceId,
                purchase.PurchasedCharacter.Attack, purchase.PurchasedCharacter.Defense,
                purchase.PurchasedCharacter.RunCost, itemBuy.PurchasedItem.DefinitionId,
                reward.GuaranteedInterest, reward.RandomBonusInterest, reward.TotalGoldReward,
                economy, fixture.Streams.CaptureState());
        }

        private static ShopFixture CreateFixture(ulong masterSeed, ulong trialIndex)
        {
            var definitions = new List<CharacterDefinition>();
            for (int i = 1; i <= 5; i++)
                definitions.Add(new CharacterDefinition("char-" + i, "Character " + i, 5, null, null));

            var configuration = new CharacterShopConfiguration(
                new[] { 1, 1, 1, 1, 1 },
                new[] { 15, 15, 15, 15, 15 },
                new[] { new[] { 20, 20, 20, 20, 20 } },
                new[] { 3 });
            SimulationSeed seed = TrialSeedGenerator.Create(masterSeed, trialIndex);
            var streams = new SimulationRandomStreams(seed);
            AssignedCharacterCosts costs = CharacterCostAssigner.Assign(definitions, configuration, streams.Roster);
            CharacterPool initialPool = CharacterShopRules.CreateInitialPool(definitions, costs, configuration);
            CharacterShopGenerationResult generated = CharacterShopRules.Generate(definitions, costs,
                initialPool, configuration, 1, 1, 0, streams.Shop);
            return new ShopFixture(definitions, configuration, costs, generated.Shop, generated.Pool, streams);
        }

        private static CharacterStatDefinition MakeStats()
        {
            var rows = new List<CharacterCostStats>();
            for (int cost = 1; cost <= 5; cost++)
                rows.Add(new CharacterCostStats(cost,
                    new[] { new CharacterStatOption(20 + cost, 3 + cost, 1) }, null, null));
            return new CharacterStatDefinition(rows);
        }

        private static EconomyRulesConfiguration MakeEconomyConfig()
        {
            return new EconomyRulesConfiguration(4, 2, 1,
                new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
                10, 1, 3, 3, 2, 1);
        }

        private static EconomyState MakeEconomy(int gold)
        {
            return new EconomyState(gold, 0, 1, 12, gold, 0, 1);
        }

        private static ItemDefinition[] CreateItems()
        {
            return new[]
            {
                new ItemDefinition("item-1", "Potion", 2),
                new ItemDefinition("item-2", "Stone", 3),
                new ItemDefinition("item-3", "Charm", 4),
                new ItemDefinition("item-4", "Token", 5),
                new ItemDefinition("item-1", "Potion", 2)
            };
        }

        private static void AssertPoolAndReservationInvariant(ShopState shop, CharacterPool pool)
        {
            foreach (CharacterPoolEntry entry in pool.Entries.Values)
            {
                Assert.That(entry.RemainingCopies, Is.GreaterThanOrEqualTo(0));
                Assert.That(entry.ReservedCopies, Is.GreaterThanOrEqualTo(0));
                Assert.That(entry.OwnedCopies, Is.GreaterThanOrEqualTo(0));
                Assert.That(entry.RemainingCopies + entry.ReservedCopies + entry.OwnedCopies,
                    Is.EqualTo(entry.InitialStockLimit), "Pool inventory total must remain at its initial cap.");

                int offersReserved = shop.Kind == ShopKind.Character
                    ? shop.Offers.Count(offer => offer.Kind == ShopOfferKind.Character &&
                        offer.Status == ShopOfferStatus.Available && offer.DefinitionId == entry.DefinitionId)
                    : 0;
                Assert.That(entry.ReservedCopies, Is.EqualTo(offersReserved),
                    "CharacterPool reservations must match currently available character Offers.");
            }
        }

        private static string[] DescribeOffers(ShopState shop)
        {
            return shop.Offers.Select(offer => offer.OfferId + "|" + offer.Kind + "|" +
                offer.DefinitionId + "|" + offer.DisplayPrice + "|" + offer.Status).ToArray();
        }

        private static string[] CapturePool(CharacterPool pool)
        {
            return pool.Entries.Values.OrderBy(entry => entry.DefinitionId)
                .Select(entry => entry.DefinitionId + "|" + entry.RemainingCopies + "|" +
                    entry.ReservedCopies + "|" + entry.OwnedCopies + "|" + entry.InitialStockLimit).ToArray();
        }

        private static void AssertRandomStatesEqual(SimulationRandomStreamsState expected,
            SimulationRandomStreamsState actual)
        {
            Assert.That(actual.TrialSeed, Is.EqualTo(expected.TrialSeed));
            AssertStateEqual(expected.Shop, actual.Shop);
            AssertStateEqual(expected.Stats, actual.Stats);
            AssertStateEqual(expected.Encounter, actual.Encounter);
            AssertStateEqual(expected.Battle, actual.Battle);
            AssertStateEqual(expected.Event, actual.Event);
            AssertStateEqual(expected.Roster, actual.Roster);
            AssertStateEqual(expected.Economy, actual.Economy);
        }

        private static void AssertStateEqual(Pcg32State expected, Pcg32State actual, string message = null)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion), message);
            Assert.That(actual.State, Is.EqualTo(expected.State), message);
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment), message);
        }

        private sealed class PlacementGate : ICharacterPlacementApproval
        {
            private readonly bool _allowed;
            public int Calls { get; private set; }
            public PlacementGate(bool allowed) { _allowed = allowed; }
            public bool CanAcceptPurchasedCharacter(CharacterInstance character) { Calls++; return _allowed; }
        }

        private sealed class ShopFixture
        {
            public IReadOnlyList<CharacterDefinition> Definitions { get; }
            public CharacterShopConfiguration Configuration { get; }
            public AssignedCharacterCosts Costs { get; }
            public ShopState Shop { get; }
            public CharacterPool Pool { get; }
            public SimulationRandomStreams Streams { get; }

            public ShopFixture(IReadOnlyList<CharacterDefinition> definitions,
                CharacterShopConfiguration configuration, AssignedCharacterCosts costs,
                ShopState shop, CharacterPool pool, SimulationRandomStreams streams)
            {
                Definitions = definitions;
                Configuration = configuration;
                Costs = costs;
                Shop = shop;
                Pool = pool;
                Streams = streams;
            }
        }

        private sealed class IntegratedOutcome
        {
            public string[] CostAssignments { get; }
            public string[] InitialOffers { get; }
            public string[] ItemOffers { get; }
            public string[] FinalOffers { get; }
            public string[] FinalPool { get; }
            public string PurchasedInstanceId { get; }
            public int PurchasedAttack { get; }
            public int PurchasedDefense { get; }
            public int PurchasedRunCost { get; }
            public string PurchasedItemId { get; }
            public int InterestGuaranteed { get; }
            public int InterestRandomBonus { get; }
            public int TotalStageReward { get; }
            public EconomyState FinalEconomy { get; }
            public SimulationRandomStreamsState RandomState { get; }

            public IntegratedOutcome(string[] costAssignments, string[] initialOffers,
                string[] itemOffers, string[] finalOffers, string[] finalPool,
                string purchasedInstanceId, int purchasedAttack, int purchasedDefense,
                int purchasedRunCost, string purchasedItemId, int interestGuaranteed,
                int interestRandomBonus, int totalStageReward, EconomyState finalEconomy,
                SimulationRandomStreamsState randomState)
            {
                CostAssignments = costAssignments;
                InitialOffers = initialOffers;
                ItemOffers = itemOffers;
                FinalOffers = finalOffers;
                FinalPool = finalPool;
                PurchasedInstanceId = purchasedInstanceId;
                PurchasedAttack = purchasedAttack;
                PurchasedDefense = purchasedDefense;
                PurchasedRunCost = purchasedRunCost;
                PurchasedItemId = purchasedItemId;
                InterestGuaranteed = interestGuaranteed;
                InterestRandomBonus = interestRandomBonus;
                TotalStageReward = totalStageReward;
                FinalEconomy = finalEconomy;
                RandomState = randomState;
            }
        }
    }
}
