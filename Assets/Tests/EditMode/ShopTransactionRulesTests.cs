using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Tests
{
    public sealed class ShopTransactionRulesTests
    {
        [Test]
        public void CharacterPurchase_SpendsRunCostCommitsReservationAndReturnsForkedInstanceState()
        {
            var setup = CreateCharacterShop(2, 13UL);
            var factory = new CharacterInstanceFactory("T");
            var statsRandom = new Pcg32RandomSource(14UL, 15UL);
            Pcg32State originalRng = statsRandom.CaptureState();
            var gate = new PlacementGate(true);
            EconomyState economy = MakeEconomy(10);

            CharacterPurchaseResult result = ShopTransactionRules.TryPurchaseCharacter(setup.Shop,
                setup.Shop.Offers[0].OfferId, setup.Definitions, setup.Costs, economy, setup.Pool,
                factory, MakeStats(2), statsRandom, gate);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.PlacementValidationPassed, Is.True);
            Assert.That(gate.Calls, Is.EqualTo(1));
            Assert.That(result.PurchasedCharacter.RunCost, Is.EqualTo(2));
            Assert.That(result.PurchasedCharacter.DefinitionId, Is.EqualTo("hero"));
            Assert.That(result.PurchasedCharacter.InstanceId, Is.EqualTo("T1"));
            Assert.That(result.PurchasedCharacter.HasRolledStats, Is.True);
            Assert.That(result.NextEconomy.Gold, Is.EqualTo(8));
            Assert.That(result.NextShop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(result.NextPool.Entries["hero"].ReservedCopies, Is.Zero);
            Assert.That(result.NextPool.Entries["hero"].OwnedCopies, Is.EqualTo(1));
            Assert.That(result.NextPool.Entries["hero"].RemainingCopies, Is.EqualTo(14));
            Assert.That(result.NextInstanceFactory.LastIssuedNumber, Is.EqualTo(1));
            Assert.That(factory.LastIssuedNumber, Is.Zero, "The input factory is not mutated before GameSession applies the result.");
            Assert.That(statsRandom.CaptureState().State, Is.EqualTo(originalRng.State), "The input RNG is not mutated before GameSession applies the result.");
            Assert.That(result.NextStatsRandomState.State, Is.Not.EqualTo(originalRng.State));
        }

        [Test]
        public void CharacterPurchase_FailureCasesLeaveGoldOfferPoolFactoryAndStatsRngUnchanged()
        {
            var setup = CreateCharacterShop(2, 22UL);
            var factory = new CharacterInstanceFactory("F", 4);
            var statsRandom = new Pcg32RandomSource(23UL, 24UL);
            Pcg32State beforeRng = statsRandom.CaptureState();
            EconomyState poor = MakeEconomy(1);
            ShopState shop = setup.Shop;
            CharacterPool pool = setup.Pool;
            var neverCalled = new PlacementGate(true);

            CharacterPurchaseResult insufficient = ShopTransactionRules.TryPurchaseCharacter(shop,
                shop.Offers[0].OfferId, setup.Definitions, setup.Costs, poor, pool, factory,
                MakeStats(2), statsRandom, neverCalled);
            Assert.That(insufficient.Succeeded, Is.False);
            Assert.That(insufficient.FailureReason, Is.EqualTo(CharacterPurchaseFailureReason.InsufficientGold));
            Assert.That(insufficient.NextEconomy, Is.SameAs(poor));
            Assert.That(insufficient.NextShop, Is.SameAs(shop));
            Assert.That(insufficient.NextPool, Is.SameAs(pool));
            Assert.That(insufficient.NextInstanceFactory.LastIssuedNumber, Is.EqualTo(4));
            Assert.That(factory.LastIssuedNumber, Is.EqualTo(4));
            Assert.That(statsRandom.CaptureState().State, Is.EqualTo(beforeRng.State));
            Assert.That(neverCalled.Calls, Is.Zero);

            CharacterPurchaseResult rejected = ShopTransactionRules.TryPurchaseCharacter(shop,
                shop.Offers[0].OfferId, setup.Definitions, setup.Costs, MakeEconomy(10), pool, factory,
                MakeStats(2), statsRandom, new PlacementGate(false));
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.FailureReason, Is.EqualTo(CharacterPurchaseFailureReason.PlacementNotApproved));
            Assert.That(rejected.NextShop, Is.SameAs(shop));
            Assert.That(rejected.NextPool, Is.SameAs(pool));
            Assert.That(factory.LastIssuedNumber, Is.EqualTo(4));
            Assert.That(statsRandom.CaptureState().State, Is.EqualTo(beforeRng.State));

            CharacterPurchaseResult invalidStats = ShopTransactionRules.TryPurchaseCharacter(shop,
                shop.Offers[0].OfferId, setup.Definitions, setup.Costs, MakeEconomy(10), pool, factory,
                new CharacterStatDefinition(null), statsRandom, new PlacementGate(true));
            Assert.That(invalidStats.Succeeded, Is.False);
            Assert.That(invalidStats.FailureReason, Is.EqualTo(CharacterPurchaseFailureReason.InvalidStatsConfiguration));
            Assert.That(factory.LastIssuedNumber, Is.EqualTo(4));
            Assert.That(statsRandom.CaptureState().State, Is.EqualTo(beforeRng.State));
        }

        [Test]
        public void CharacterPurchase_CannotBuyOfferTwice()
        {
            var setup = CreateCharacterShop(2, 31UL);
            var factory = new CharacterInstanceFactory("D");
            var statsRandom = new Pcg32RandomSource(32UL, 33UL);
            CharacterPurchaseResult first = ShopTransactionRules.TryPurchaseCharacter(setup.Shop,
                setup.Shop.Offers[0].OfferId, setup.Definitions, setup.Costs, MakeEconomy(10), setup.Pool,
                factory, MakeStats(2), statsRandom, new PlacementGate(true));
            Pcg32RandomSource committedStats = Pcg32RandomSource.FromState(first.NextStatsRandomState);
            CharacterPurchaseResult second = ShopTransactionRules.TryPurchaseCharacter(first.NextShop,
                setup.Shop.Offers[0].OfferId, setup.Definitions, setup.Costs, first.NextEconomy, first.NextPool,
                first.NextInstanceFactory, MakeStats(2), committedStats, new PlacementGate(true));

            Assert.That(first.Succeeded, Is.True);
            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.FailureReason, Is.EqualTo(CharacterPurchaseFailureReason.InvalidOffer));
            Assert.That(second.NextEconomy, Is.SameAs(first.NextEconomy));
            Assert.That(second.NextPool, Is.SameAs(first.NextPool));
            Assert.That(second.NextInstanceFactory.LastIssuedNumber, Is.EqualTo(1));
            Assert.That(committedStats.CaptureState().State, Is.EqualTo(first.NextStatsRandomState.State));
        }

        [TestCase(1, 1)]
        [TestCase(2, 3)]
        [TestCase(3, 9)]
        public void CharacterSale_ReturnsCopiesAndGoldUsingRunCost(int star, int expectedCopies)
        {
            var definition = new CharacterDefinition("hero", "Hero", 5, null, null);
            var character = new CharacterInstance("owned-" + star, definition, 2, star);
            var owned = new List<CharacterInstance> { character };
            const int initialStock = 15;
            var pool = new CharacterPool(new[]
            {
                new CharacterPoolEntry("hero", initialStock - expectedCopies, initialStock, 0, expectedCopies)
            });
            EconomyState economy = MakeEconomy(4);

            CharacterSaleResult result = ShopTransactionRules.TrySellCharacter(character, owned, economy, pool);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ReturnedCopies, Is.EqualTo(expectedCopies));
            Assert.That(result.GoldGained, Is.EqualTo(2 * expectedCopies), "Sale value is based on RunCost, not CharacterDefinition.BaseCost.");
            Assert.That(result.NextEconomy.Gold, Is.EqualTo(4 + 2 * expectedCopies));
            Assert.That(result.NextOwnedCharacters, Is.Empty);
            Assert.That(result.NextPool.Entries["hero"].OwnedCopies, Is.Zero);
            Assert.That(result.NextPool.Entries["hero"].RemainingCopies, Is.EqualTo(initialStock));
            Assert.That(result.NextPool.Entries["hero"].RemainingCopies + result.NextPool.Entries["hero"].ReservedCopies +
                        result.NextPool.Entries["hero"].OwnedCopies, Is.EqualTo(initialStock));
            Assert.That(owned.Count, Is.EqualTo(1), "Input owner list remains unchanged.");
        }

        [Test]
        public void CharacterSale_RejectsNotOwnedAndDoubleSoldCharactersWithoutChangingState()
        {
            var definition = new CharacterDefinition("hero", "Hero", 1, null, null);
            var character = new CharacterInstance("owned", definition, 2, 2);
            var owned = new List<CharacterInstance> { character };
            var pool = new CharacterPool(new[] { new CharacterPoolEntry("hero", 12, 15, 0, 3) });
            EconomyState economy = MakeEconomy(7);

            CharacterSaleResult notOwned = ShopTransactionRules.TrySellCharacter(character,
                Array.Empty<CharacterInstance>(), economy, pool);
            Assert.That(notOwned.Succeeded, Is.False);
            Assert.That(notOwned.FailureReason, Is.EqualTo(CharacterSaleFailureReason.NotOwned));
            Assert.That(notOwned.NextEconomy, Is.SameAs(economy));
            Assert.That(notOwned.NextPool, Is.SameAs(pool));

            CharacterSaleResult sold = ShopTransactionRules.TrySellCharacter(character, owned, economy, pool);
            CharacterSaleResult twice = ShopTransactionRules.TrySellCharacter(character,
                sold.NextOwnedCharacters, sold.NextEconomy, sold.NextPool);
            Assert.That(sold.Succeeded, Is.True);
            Assert.That(twice.Succeeded, Is.False);
            Assert.That(twice.FailureReason, Is.EqualTo(CharacterSaleFailureReason.NotOwned));
            Assert.That(twice.NextEconomy.Gold, Is.EqualTo(sold.NextEconomy.Gold));
            Assert.That(twice.NextPool, Is.SameAs(sold.NextPool));
        }

        [Test]
        public void ItemShop_ShufflesUniqueCandidatesToTheSmallerOfSlotAndCandidateCounts()
        {
            var first = new ItemDefinition("I1", "Potion", 4);
            var second = new ItemDefinition("I2", "Stone", 7);
            var items = new[] { first, second, first };
            CharacterPool pool = CreateCharacterShop(1, 40UL).Pool;
            var rngA = new Pcg32RandomSource(41UL, 42UL);
            var rngB = new Pcg32RandomSource(41UL, 42UL);
            Pcg32State beforeA = rngA.CaptureState();

            ItemShopGenerationResult resultA = ItemShopRules.Generate(null, pool, items, 2, 4, 5, 0, rngA);
            ItemShopGenerationResult resultB = ItemShopRules.Generate(null, pool, items, 2, 4, 5, 0, rngB);

            Assert.That(resultA.NextShop.Kind, Is.EqualTo(ShopKind.Item));
            Assert.That(resultA.NextShop.SlotCount, Is.EqualTo(5));
            Assert.That(resultA.NextShop.Offers.Count, Is.EqualTo(2));
            Assert.That(resultA.NextShop.Offers[0].DefinitionId, Is.Not.EqualTo(resultA.NextShop.Offers[1].DefinitionId));
            Assert.That(resultA.NextShop.Offers[0].DisplayPrice, Is.EqualTo(PriceFor(items, resultA.NextShop.Offers[0].DefinitionId)));
            Assert.That(resultA.NextShop.Offers[1].DisplayPrice, Is.EqualTo(PriceFor(items, resultA.NextShop.Offers[1].DefinitionId)));
            CollectionAssert.AreEqual(OfferIds(resultA.NextShop), OfferIds(resultB.NextShop));
            Assert.That(resultA.NextCharacterPool, Is.SameAs(pool), "Items have no finite inventory and do not consume character stock.");
            Assert.That(rngA.CaptureState().State, Is.EqualTo(beforeA.State), "Generation returns a proposed RNG checkpoint instead of mutating the input.");
            Assert.That(resultA.NextShopRandomState.State, Is.EqualTo(resultB.NextShopRandomState.State));

            ItemShopGenerationResult fewerSlots = ItemShopRules.Generate(null, pool, new[] { first, second }, 1, 1, 1, 0,
                new Pcg32RandomSource(41UL, 42UL));
            Assert.That(fewerSlots.NextShop.Offers.Count, Is.EqualTo(1));
        }

        [Test]
        public void ItemPurchase_SpendsDefinitionPriceAndMarksOfferPurchasedWithoutFinitePool()
        {
            var items = new[] { new ItemDefinition("I1", "Potion", 4), new ItemDefinition("I2", "Stone", 7) };
            CharacterPool pool = CreateCharacterShop(1, 50UL).Pool;
            ItemShopGenerationResult generated = ItemShopRules.Generate(null, pool, items, 1, 2, 2, 0,
                new Pcg32RandomSource(51UL, 52UL));
            ShopOffer offer = generated.NextShop.Offers[0];
            EconomyState economy = MakeEconomy(10);

            ItemPurchaseResult bought = ItemShopRules.TryPurchaseItem(generated.NextShop, offer.OfferId, items, economy);
            ItemPurchaseResult insufficient = ItemShopRules.TryPurchaseItem(generated.NextShop, offer.OfferId, items, MakeEconomy(0));
            ItemPurchaseResult twice = ItemShopRules.TryPurchaseItem(bought.NextShop, offer.OfferId, items, bought.NextEconomy);

            Assert.That(bought.Succeeded, Is.True);
            Assert.That(bought.PurchasedItem.DefinitionId, Is.EqualTo(offer.DefinitionId));
            Assert.That(bought.NextEconomy.Gold, Is.EqualTo(10 - offer.DisplayPrice));
            Assert.That(bought.NextShop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(insufficient.Succeeded, Is.False);
            Assert.That(insufficient.FailureReason, Is.EqualTo(ItemPurchaseFailureReason.InsufficientGold));
            Assert.That(insufficient.NextEconomy.Gold, Is.Zero);
            Assert.That(insufficient.NextShop, Is.SameAs(generated.NextShop));
            Assert.That(twice.Succeeded, Is.False);
            Assert.That(twice.NextEconomy, Is.SameAs(bought.NextEconomy));
        }

        [Test]
        public void ItemShopTransition_ReturnsOnlyUnpurchasedCharacterOffers()
        {
            var setup = CreateCharacterShop(2, 60UL, slotCount: 2);
            ShopOffer first = setup.Shop.Offers[0];
            ShopOffer second = setup.Shop.Offers[1];
            var afterFirstPurchase = new CharacterPool(new[]
            {
                new CharacterPoolEntry("hero", 13, 15, 1, 1)
            });
            var currentShop = new ShopState(ShopKind.Character, new[]
            {
                new ShopOffer(first.OfferId, ShopOfferKind.Character, "hero", 2, 2, ShopOfferStatus.Purchased),
                second
            }, 2, 1, 1, false, 0);

            ItemShopGenerationResult itemShop = ItemShopRules.Generate(currentShop, afterFirstPurchase,
                new[] { new ItemDefinition("I", "Item", 1) }, 1, 1, 2, 1,
                new Pcg32RandomSource(61UL, 62UL));

            Assert.That(itemShop.NextCharacterPool.Entries["hero"].OwnedCopies, Is.EqualTo(1));
            Assert.That(itemShop.NextCharacterPool.Entries["hero"].ReservedCopies, Is.Zero);
            Assert.That(itemShop.NextCharacterPool.Entries["hero"].RemainingCopies, Is.EqualTo(14));
            Assert.That(itemShop.NextShop.Kind, Is.EqualTo(ShopKind.Item));
        }

        private static CharacterSetup CreateCharacterShop(int runCost, ulong seed, int slotCount = 1)
        {
            var definition = new CharacterDefinition("hero", "Hero", 5, null, null);
            var definitions = new List<CharacterDefinition> { definition };
            var costs = new AssignedCharacterCosts(new[] { new KeyValuePair<string, int>("hero", runCost) });
            var perCost = new int[5];
            var initialPool = new int[5];
            var weights = new int[5];
            perCost[runCost - 1] = 1;
            initialPool[runCost - 1] = 15;
            weights[runCost - 1] = 100;
            var weightRows = new List<IEnumerable<int>> { weights };
            var config = new CharacterShopConfiguration(perCost, initialPool, weightRows, new[] { slotCount });
            CharacterPool pool = CharacterShopRules.CreateInitialPool(definitions, costs, config);
            CharacterShopGenerationResult generated = CharacterShopRules.Generate(definitions, costs,
                pool, config, 1, 1, 0, SimulationRandomStreams.CreateStream(seed, RandomStreamId.Shop));
            return new CharacterSetup(definitions, costs, generated.Shop, generated.Pool);
        }

        private static CharacterStatDefinition MakeStats(int cost)
        {
            return new CharacterStatDefinition(new[]
            {
                new CharacterCostStats(cost, new[] { new CharacterStatOption(8, 3, 1) }, null, null)
            });
        }

        private static EconomyState MakeEconomy(int gold) => new EconomyState(gold, 0, 1, 8, 10, 0, 1);

        private static int PriceFor(IReadOnlyList<ItemDefinition> items, string definitionId)
        {
            foreach (ItemDefinition item in items)
                if (item.DefinitionId == definitionId) return item.PurchasePrice;
            throw new InvalidOperationException("Test item was not found.");
        }

        private static string[] OfferIds(ShopState shop)
        {
            var ids = new string[shop.Offers.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = shop.Offers[i].DefinitionId;
            return ids;
        }

        private sealed class PlacementGate : ICharacterPlacementApproval
        {
            private readonly bool _allow;
            public int Calls { get; private set; }
            public PlacementGate(bool allow) { _allow = allow; }
            public bool CanAcceptPurchasedCharacter(CharacterInstance character) { Calls++; return _allow; }
        }

        private sealed class CharacterSetup
        {
            public IReadOnlyList<CharacterDefinition> Definitions { get; }
            public AssignedCharacterCosts Costs { get; }
            public ShopState Shop { get; }
            public CharacterPool Pool { get; }
            public CharacterSetup(IReadOnlyList<CharacterDefinition> definitions, AssignedCharacterCosts costs,
                ShopState shop, CharacterPool pool)
            {
                Definitions = definitions;
                Costs = costs;
                Shop = shop;
                Pool = pool;
            }
        }
    }
}
