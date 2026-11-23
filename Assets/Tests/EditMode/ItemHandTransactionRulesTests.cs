using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Tests
{
    public sealed class ItemHandTransactionRulesTests
    {
        private static ItemDefinition Item(string id = "Item001", int price = 3) =>
            new ItemDefinition(id, "Item", price);

        private static ShopState ItemShop(string id = "Item001", int price = 3, ShopOfferStatus status = ShopOfferStatus.Available) =>
            new ShopState(ShopKind.Item,
                new[] { new ShopOffer("offer-1", ShopOfferKind.Item, id, price, null, status) }, 1, 1, 1);

        private static EconomyState Economy(int gold = 10) =>
            new EconomyState(gold, 0, 1, 12, gold, 0, 1);

        [Test]
        public void PurchaseIntoEmptyHandSlotUpdatesEconomyShopAndHandTogether()
        {
            ShopState shop = ItemShop();
            EconomyState economy = Economy();
            HandState hand = HandState.CreateEmpty(2);

            ItemHandPurchaseResult result = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop, "offer-1", new[] { Item() }, economy, hand, 1);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextEconomy.Gold, Is.EqualTo(7));
            Assert.That(result.NextShop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.That(result.NextHand.TryGetSlot(1, out HandSlotContent slot), Is.True);
            Assert.That(slot.ItemDefinitionId, Is.EqualTo("Item001"));
            Assert.That(economy.Gold, Is.EqualTo(10));
            Assert.That(shop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Available));
            Assert.That(hand.EmptySlotCount, Is.EqualTo(2));
        }

        [TestCase(0, ItemHandPurchaseFailure.InsufficientGold)]
        [TestCase(1, ItemHandPurchaseFailure.HandSlotOccupied)]
        [TestCase(2, ItemHandPurchaseFailure.InvalidHandSlot)]
        public void InvalidPurchaseDestinationOrGoldLeavesInputsUnchanged(int slotIndex, ItemHandPurchaseFailure expected)
        {
            ShopState shop = ItemShop();
            EconomyState economy = Economy(slotIndex == 0 ? 2 : 10);
            HandState hand = slotIndex == 1
                ? new HandState(new[] { HandSlotContent.Item("Existing") })
                : HandState.CreateEmpty(1);

            ItemHandPurchaseResult result = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop, "offer-1", new[] { Item() }, economy, hand, slotIndex == 2 ? 5 : 0);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(expected));
            Assert.That(result.NextEconomy, Is.SameAs(economy));
            Assert.That(result.NextShop, Is.SameAs(shop));
            Assert.That(result.NextHand, Is.SameAs(hand));
        }

        [Test]
        public void PurchaseRejectsMissingOfferNonItemShopAndMismatchedDefinitionWithoutMutation()
        {
            ShopState shop = ItemShop();
            EconomyState economy = Economy();
            HandState hand = HandState.CreateStandard();

            ItemHandPurchaseResult missing = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop, "missing", new[] { Item() }, economy, hand, 0);
            ItemHandPurchaseResult wrongShop = ItemHandTransactionRules.TryPurchaseIntoHand(
                new ShopState(ShopKind.Character, new[] { new ShopOffer("c", ShopOfferKind.Character, "C", 1, 1) }, 1, 1, 1),
                "c", new[] { Item() }, economy, hand, 0);
            ItemHandPurchaseResult mismatch = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop, "offer-1", new[] { Item("Item001", 4) }, economy, hand, 0);

            Assert.That(missing.Failure, Is.EqualTo(ItemHandPurchaseFailure.InvalidOffer));
            Assert.That(wrongShop.Failure, Is.EqualTo(ItemHandPurchaseFailure.ItemShopInactive));
            Assert.That(mismatch.Failure, Is.EqualTo(ItemHandPurchaseFailure.PriceMismatch));
            Assert.That(economy.Gold, Is.EqualTo(10));
            Assert.That(shop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Available));
            Assert.That(hand.EmptySlotCount, Is.EqualTo(5));
        }

        [Test]
        public void DuplicateItemDefinitionsCanBePurchasedFromSeparateOffers()
        {
            var offers = new[]
            {
                new ShopOffer("a", ShopOfferKind.Item, "Item001", 3, null),
                new ShopOffer("b", ShopOfferKind.Item, "Item001", 3, null)
            };
            ShopState shop = new ShopState(ShopKind.Item, offers, 2, 1, 1);
            EconomyState economy = Economy();
            HandState hand = HandState.CreateEmpty(2);
            ItemDefinition item = Item();

            ItemHandPurchaseResult first = ItemHandTransactionRules.TryPurchaseIntoHand(shop, "a", new[] { item }, economy, hand, 0);
            ItemHandPurchaseResult second = ItemHandTransactionRules.TryPurchaseIntoHand(first.NextShop, "b", new[] { item }, first.NextEconomy, first.NextHand, 1);

            Assert.That(first.Succeeded && second.Succeeded, Is.True);
            Assert.That(second.NextEconomy.Gold, Is.EqualTo(4));
            Assert.That(second.NextHand.TryGetSlot(0, out HandSlotContent firstItem) && firstItem.ItemDefinitionId == "Item001", Is.True);
            Assert.That(second.NextHand.TryGetSlot(1, out HandSlotContent secondItem) && secondItem.ItemDefinitionId == "Item001", Is.True);
        }

        [Test]
        public void PurchasedOfferCannotBePurchasedAgain()
        {
            ItemHandPurchaseResult first = ItemHandTransactionRules.TryPurchaseIntoHand(
                ItemShop(), "offer-1", new[] { Item() }, Economy(), HandState.CreateEmpty(2), 0);

            ItemHandPurchaseResult second = ItemHandTransactionRules.TryPurchaseIntoHand(
                first.NextShop, "offer-1", new[] { Item() }, first.NextEconomy, first.NextHand, 1);

            Assert.That(first.Succeeded, Is.True);
            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Failure, Is.EqualTo(ItemHandPurchaseFailure.InvalidOffer));
            Assert.That(second.NextEconomy, Is.SameAs(first.NextEconomy));
            Assert.That(second.NextShop, Is.SameAs(first.NextShop));
            Assert.That(second.NextHand, Is.SameAs(first.NextHand));
        }

        [Test]
        public void IdenticalPurchaseInputsProduceIdenticalResultsWithoutRandomInput()
        {
            ShopState shop = ItemShop();
            EconomyState economy = Economy();
            HandState hand = HandState.CreateEmpty(2);
            ItemDefinition[] definitions = { Item() };

            ItemHandPurchaseResult first = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop, "offer-1", definitions, economy, hand, 1);
            ItemHandPurchaseResult second = ItemHandTransactionRules.TryPurchaseIntoHand(
                shop, "offer-1", definitions, economy, hand, 1);

            Assert.That(first.Succeeded, Is.EqualTo(second.Succeeded));
            Assert.That(first.NextEconomy.Gold, Is.EqualTo(second.NextEconomy.Gold));
            Assert.That(first.NextShop.Offers[0].Status, Is.EqualTo(second.NextShop.Offers[0].Status));
            Assert.That(first.NextHand.Slots[1].ItemDefinitionId, Is.EqualTo(second.NextHand.Slots[1].ItemDefinitionId));
        }

        [Test]
        public void SellingItemClearsHandForZeroGoldWithoutPoolOrEconomyChanges()
        {
            EconomyState economy = Economy(4);
            HandState hand = new HandState(new[] { HandSlotContent.Item("Item001") });

            ItemHandSaleResult result = ItemHandTransactionRules.TrySellFromHand(hand, economy, 0);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.GoldReturned, Is.Zero);
            Assert.That(result.NextEconomy, Is.SameAs(economy));
            Assert.That(result.NextHand.EmptySlotCount, Is.EqualTo(1));
            Assert.That(result.SoldItemDefinitionId, Is.EqualTo("Item001"));
            Assert.That(hand.EmptySlotCount, Is.Zero);
        }

        [Test]
        public void CannotSellCharacterAsItemOrUseOutOfRangeSlot()
        {
            EconomyState economy = Economy();
            ItemHandSaleResult character = ItemHandTransactionRules.TrySellFromHand(
                new HandState(new[] { HandSlotContent.Character("C1") }), economy, 0);
            ItemHandSaleResult invalid = ItemHandTransactionRules.TrySellFromHand(HandState.CreateStandard(), economy, 8);

            Assert.That(character.Failure, Is.EqualTo(ItemHandSaleFailure.SlotDoesNotContainItem));
            Assert.That(invalid.Failure, Is.EqualTo(ItemHandSaleFailure.InvalidHandSlot));
            Assert.That(character.NextEconomy, Is.SameAs(economy));
            Assert.That(invalid.NextEconomy, Is.SameAs(economy));
        }
    }
}
