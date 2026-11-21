using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Tests
{
    public sealed class ShopEconomyDataTests
    {
        [Test]
        public void DefinitionsAndOffers_KeepValidatedValuesWithoutCreatingInstances()
        {
            var character = new ShopOffer("offer-1", ShopOfferKind.Character, "char-1", 3, 2);
            var item = new ShopOffer("offer-2", ShopOfferKind.Item, "item-1", 5, null);
            var itemDefinition = new ItemDefinition("item-1", "Potion", 5);

            Assert.That(character.Status, Is.EqualTo(ShopOfferStatus.Available));
            Assert.That(character.CharacterCost, Is.EqualTo(2));
            Assert.That(item.CharacterCost, Is.Null);
            Assert.That(itemDefinition.PurchasePrice, Is.EqualTo(5));
            Assert.Throws<ArgumentException>(() => new ItemDefinition(" ", "Potion", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemDefinition("item", "Potion", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShopOffer("o", ShopOfferKind.Character, "c", 1, 0));
            Assert.Throws<ArgumentException>(() => new ShopOffer("o", ShopOfferKind.Item, "i", 1, 1));
        }

        [Test]
        public void ShopState_CopiesOffersAndEnforcesShopKindAndCapacity()
        {
            var available = new ShopOffer("o1", ShopOfferKind.Character, "c1", 2, 1);
            var purchased = new ShopOffer("o2", ShopOfferKind.Character, "c2", 2, 1, ShopOfferStatus.Purchased);
            var source = new List<ShopOffer> { available, purchased };
            var shop = new ShopState(ShopKind.Character, source, 3, 1, 2);
            source.Clear();

            Assert.That(shop.Offers.Count, Is.EqualTo(2));
            Assert.That(shop.SlotCount, Is.EqualTo(3));
            Assert.That(shop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Available));
            Assert.That(shop.Offers[1].Status, Is.EqualTo(ShopOfferStatus.Purchased));
            Assert.Throws<NotSupportedException>(() => ((IList<ShopOffer>)shop.Offers).Clear());
            Assert.Throws<ArgumentException>(() => new ShopState(ShopKind.None, new[] { available }, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => new ShopState(ShopKind.Character,
                new[] { available, new ShopOffer("o1", ShopOfferKind.Character, "c2", 1, 1) }, 2, 1, 1));
            Assert.Throws<ArgumentException>(() => new ShopState(ShopKind.Item, new[] { available }, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => new ShopState(ShopKind.Character, new[] { available }, 0, 1, 1));
        }

        [Test]
        public void EventShop_CanRepresentNoOffers()
        {
            var shop = new ShopState(ShopKind.None, null, 0, 1, 1, true);
            Assert.That(shop.IsEventStage, Is.True);
            Assert.That(shop.Offers, Is.Empty);
        }

        [Test]
        public void CharacterPool_PreservesCountsAndProtectsItsCollection()
        {
            var entry = new CharacterPoolEntry("c1", 7, 10, 3);
            var source = new List<CharacterPoolEntry> { entry };
            var pool = new CharacterPool(source);
            source.Clear();

            Assert.That(pool.Entries["c1"].RemainingCopies, Is.EqualTo(7));
            Assert.That(pool.Entries["c1"].InitialStockLimit, Is.EqualTo(10));
            Assert.That(pool.Entries["c1"].ReservedCopies, Is.EqualTo(3));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, CharacterPoolEntry>)pool.Entries).Clear());
            Assert.Throws<ArgumentException>(() => new CharacterPool(new[] { entry, entry }));
            Assert.Throws<ArgumentException>(() => new CharacterPoolEntry("c", 8, 10, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterPoolEntry("c", -1, 10));
            Assert.Throws<ArgumentException>(() => new CharacterPoolEntry("", 0, 0));
        }

        [Test]
        public void EconomyState_PreservesCurrentAndInitialValues()
        {
            var state = new EconomyState(12, 4, 3, 8, 5, 0, 1);
            Assert.That(state.Gold, Is.EqualTo(12));
            Assert.That(state.XP, Is.EqualTo(4));
            Assert.That(state.Level, Is.EqualTo(3));
            Assert.That(state.MaximumLevel, Is.EqualTo(8));
            Assert.That(state.InitialGold, Is.EqualTo(5));
            Assert.That(state.InitialXP, Is.Zero);
            Assert.That(state.InitialLevel, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyState(-1, 0, 1, 8, 0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyState(0, 0, 9, 8, 0, 0, 1));
        }

        [Test]
        public void NewStreams_AreStableSeparatedAndPartOfCheckpoints()
        {
            var first = new SimulationRandomStreams(777UL);
            var second = new SimulationRandomStreams(777UL);
            Assert.That(first.Roster.NextInt(0, int.MaxValue), Is.EqualTo(second.Roster.NextInt(0, int.MaxValue)));
            Assert.That(first.Economy.NextInt(0, int.MaxValue), Is.EqualTo(second.Economy.NextInt(0, int.MaxValue)));

            var battleExpected = SimulationRandomStreams.CreateStream(777UL, RandomStreamId.Battle);
            var rosterNoise = SimulationRandomStreams.CreateStream(777UL, RandomStreamId.Roster);
            for (int i = 0; i < 30; i++) rosterNoise.NextInt(0, int.MaxValue);
            var battleAfterNoise = SimulationRandomStreams.CreateStream(777UL, RandomStreamId.Battle);
            for (int i = 0; i < 20; i++) Assert.That(battleExpected.NextInt(0, int.MaxValue), Is.EqualTo(battleAfterNoise.NextInt(0, int.MaxValue)));

            first.Roster.NextUnitDouble();
            first.Economy.NextUnitDouble();
            SimulationRandomStreamsState checkpoint = first.CaptureState();
            int rosterNext = first.Roster.NextInt(0, int.MaxValue);
            int economyNext = first.Economy.NextInt(0, int.MaxValue);
            first.RestoreState(checkpoint);
            Assert.That(first.Roster.NextInt(0, int.MaxValue), Is.EqualTo(rosterNext));
            Assert.That(first.Economy.NextInt(0, int.MaxValue), Is.EqualTo(economyNext));
            Assert.That(checkpoint.Version, Is.EqualTo(SimulationRandomStreamsState.CurrentVersion));
            Assert.That((ulong)RandomStreamId.Shop, Is.EqualTo(1UL));
            Assert.That((ulong)RandomStreamId.Event, Is.EqualTo(5UL));
            Assert.That((ulong)RandomStreamId.Roster, Is.EqualTo(6UL));
            Assert.That((ulong)RandomStreamId.Economy, Is.EqualTo(7UL));
        }

        [Test]
        public void OldCheckpointVersion_IsRejectedAfterStreamSetExpansion()
        {
            var streams = new SimulationRandomStreams(303UL);
            SimulationRandomStreamsState oldVersion = new SimulationRandomStreamsState(303UL, 1,
                streams.Shop.CaptureState(), streams.Stats.CaptureState(), streams.Encounter.CaptureState(),
                streams.Battle.CaptureState(), streams.Event.CaptureState());
            Assert.Throws<ArgumentException>(() => streams.RestoreState(oldVersion));
        }
    }
}
