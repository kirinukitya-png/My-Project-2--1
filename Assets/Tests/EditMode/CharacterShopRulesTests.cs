using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Tests
{
    public sealed class CharacterShopRulesTests
    {
        private static readonly int[][] SceneWeights =
        {
            new[] { 80, 15, 3, 2, 0 },
            new[] { 20, 50, 25, 3, 2 },
            new[] { 10, 20, 50, 15, 5 },
            new[] { 0, 5, 30, 50, 15 },
            new[] { 0, 5, 15, 30, 50 }
        };

        [Test]
        public void CostAssignment_UsesRosterRngAndKeepsDefinitionsUnchanged()
        {
            List<CharacterDefinition> definitions = CreateDefinitions(30);
            var config = CreateConfiguration(new[] { 6, 6, 6, 6, 6 }, new[] { 15, 15, 15, 15, 15 }, new[] { 3, 3, 3, 3, 3 });
            var first = CharacterCostAssigner.Assign(definitions, config, new SimulationRandomStreams(120UL).Roster);
            var second = CharacterCostAssigner.Assign(definitions, config, new SimulationRandomStreams(120UL).Roster);
            var actual = new int[5];

            foreach (CharacterDefinition definition in definitions)
            {
                actual[first.GetCost(definition.DefinitionId) - 1]++;
                Assert.That(definition.BaseCost, Is.EqualTo(1));
                Assert.That(first.GetCost(definition.DefinitionId), Is.EqualTo(second.GetCost(definition.DefinitionId)));
            }
            CollectionAssert.AreEqual(new[] { 6, 6, 6, 6, 6 }, actual);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)first.Costs).Clear());
        }

        [Test]
        public void CostAssignment_RejectsAllocationThatDoesNotCoverRosterExactly()
        {
            var definitions = CreateDefinitions(29);
            var config = CreateConfiguration(new[] { 6, 6, 6, 6, 6 }, new[] { 15, 15, 15, 15, 15 }, new[] { 3, 3, 3, 3, 3 });
            Assert.Throws<ArgumentException>(() => CharacterCostAssigner.Assign(definitions, config, new Pcg32RandomSource(1UL, 3UL)));
        }

        [Test]
        public void InitialPool_UsesRunCostTierStockAndMaintainsTotalCopies()
        {
            List<CharacterDefinition> definitions = CreateDefinitions(30);
            var config = CreateConfiguration(new[] { 6, 6, 6, 6, 6 }, new[] { 15, 15, 15, 15, 15 }, new[] { 3, 3, 3, 3, 3 });
            AssignedCharacterCosts costs = CharacterCostAssigner.Assign(definitions, config, new SimulationRandomStreams(8UL).Roster);
            CharacterPool pool = CharacterShopRules.CreateInitialPool(definitions, costs, config);

            Assert.That(pool.Entries.Count, Is.EqualTo(30));
            foreach (CharacterPoolEntry entry in pool.Entries.Values)
            {
                Assert.That(entry.RemainingCopies, Is.EqualTo(15));
                Assert.That(entry.ReservedCopies, Is.Zero);
                Assert.That(entry.OwnedCopies, Is.Zero);
                Assert.That(entry.RemainingCopies + entry.ReservedCopies + entry.OwnedCopies, Is.EqualTo(entry.InitialStockLimit));
            }
        }

        [Test]
        public void CostBandDraws_ReflectAllFiveConfiguredStageTables()
        {
            int trialsPerStage = 5000;
            List<CharacterDefinition> definitions = CreateDefinitions(5);
            var config = CreateConfiguration(new[] { 1, 1, 1, 1, 1 }, new[] { 100, 100, 100, 100, 100 }, new[] { 1, 1, 1, 1, 1 });
            AssignedCharacterCosts costs = FixedCosts(definitions, new[] { 1, 2, 3, 4, 5 });

            for (int stage = 1; stage <= 5; stage++)
            {
                var counts = new int[5];
                for (int trial = 0; trial < trialsPerStage; trial++)
                {
                    CharacterPool pool = CharacterShopRules.CreateInitialPool(definitions, costs, config);
                    var result = CharacterShopRules.Generate(definitions, costs, pool, config,
                        stage, 1, 0, SimulationRandomStreams.CreateStream((ulong)(trial + 10000 * stage), RandomStreamId.Shop));
                    counts[costs.GetCost(result.Shop.Offers[0].DefinitionId) - 1]++;
                }

                for (int cost = 0; cost < 5; cost++)
                {
                    double observed = counts[cost] / (double)trialsPerStage;
                    double expected = SceneWeights[stage - 1][cost] / 100.0;
                    Assert.That(observed, Is.EqualTo(expected).Within(0.025), "Stage " + stage + ", Cost " + (cost + 1));
                }
            }
        }

        [Test]
        public void CostBandDraw_ExcludesEmptyTiersAndRenormalizesRemainingWeights()
        {
            List<CharacterDefinition> definitions = CreateDefinitions(5);
            var config = CreateConfiguration(new[] { 1, 1, 1, 1, 1 }, new[] { 15, 15, 15, 15, 15 }, new[] { 1, 1, 1, 1, 1 });
            AssignedCharacterCosts costs = FixedCosts(definitions, new[] { 1, 2, 3, 4, 5 });
            int cost1 = 0;
            int cost4 = 0;
            const int trials = 8000;

            for (int trial = 0; trial < trials; trial++)
            {
                CharacterPool pool = CreatePool(definitions, costs, new[] { 0, 15, 0, 15, 0 }, new[] { 15, 0, 15, 0, 15 });
                var result = CharacterShopRules.Generate(definitions, costs, pool, config, 1, 1, 0,
                    SimulationRandomStreams.CreateStream((ulong)(trial + 50000), RandomStreamId.Shop));
                int cost = costs.GetCost(result.Shop.Offers[0].DefinitionId);
                Assert.That(cost, Is.EqualTo(2).Or.EqualTo(4));
                if (cost == 2) cost1++;
                else cost4++;
            }

            Assert.That(cost1 / (double)trials, Is.EqualTo(15.0 / 17.0).Within(0.025));
            Assert.That(cost4 / (double)trials, Is.EqualTo(2.0 / 17.0).Within(0.025));
        }

        [Test]
        public void CharacterDraw_IsWeightedByAvailableCopiesAndNeverSelectsZeroStock()
        {
            var definitions = CreateDefinitions(2);
            var config = CreateConfiguration(new[] { 2, 0, 0, 0, 0 }, new[] { 1, 0, 0, 0, 0 }, new[] { 1, 0, 0, 0, 0 });
            AssignedCharacterCosts costs = FixedCosts(definitions, new[] { 1, 1 });
            int oneCopyDraws = 0;
            const int trials = 8000;

            for (int trial = 0; trial < trials; trial++)
            {
                var pool = new CharacterPool(new[]
                {
                    new CharacterPoolEntry("C1", 1, 1),
                    new CharacterPoolEntry("C2", 3, 3)
                });
                var result = CharacterShopRules.Generate(definitions, costs, pool, config, 1, 1, 0,
                    SimulationRandomStreams.CreateStream((ulong)(trial + 90000), RandomStreamId.Shop));
                string selectedId = result.Shop.Offers[0].DefinitionId;
                Assert.That(selectedId, Is.EqualTo("C1").Or.EqualTo("C2"));
                if (selectedId == "C1") oneCopyDraws++;
            }
            Assert.That(oneCopyDraws / (double)trials, Is.EqualTo(0.25).Within(0.025));

            var zeroStock = new CharacterPool(new[]
            {
                new CharacterPoolEntry("C1", 0, 1, 0, 1),
                new CharacterPoolEntry("C2", 3, 3)
            });
            var fixedRng = SimulationRandomStreams.CreateStream(15UL, RandomStreamId.Shop);
            var zeroStockResult = CharacterShopRules.Generate(definitions, costs, zeroStock, config, 1, 1, 0, fixedRng);
            Assert.That(zeroStockResult.Shop.Offers[0].DefinitionId, Is.EqualTo("C2"));
        }

        [Test]
        public void AllEligibleCostBandsEmpty_CreatesAnEmptyOfferWithoutRedrawing()
        {
            List<CharacterDefinition> definitions = CreateDefinitions(5);
            var config = CreateConfiguration(new[] { 1, 1, 1, 1, 1 }, new[] { 15, 15, 15, 15, 15 }, new[] { 3, 3, 3, 3, 3 });
            AssignedCharacterCosts costs = FixedCosts(definitions, new[] { 1, 2, 3, 4, 5 });
            CharacterPool pool = CreatePool(definitions, costs, new[] { 0, 0, 0, 0, 0 }, new[] { 15, 15, 15, 15, 15 });
            var result = CharacterShopRules.Generate(definitions, costs, pool, config, 1, 1, 0,
                SimulationRandomStreams.CreateStream(14UL, RandomStreamId.Shop));

            Assert.That(result.Shop.SlotCount, Is.EqualTo(3));
            Assert.That(result.Shop.Offers.Count, Is.EqualTo(3));
            foreach (ShopOffer offer in result.Shop.Offers) Assert.That(offer.IsEmpty, Is.True);
            Assert.That(result.Pool.Entries["C1"].ReservedCopies, Is.Zero);
        }

        [Test]
        public void Generate_ReservesStockAndRefreshReturnsOnlyAvailableOffersOnce()
        {
            var definitions = CreateDefinitions(1);
            var config = CreateConfiguration(new[] { 1, 0, 0, 0, 0 }, new[] { 2, 0, 0, 0, 0 },
                new[] { 2, 2, 2, 2, 2 }, new[]
                {
                    new[] { 100, 0, 0, 0, 0 }, new[] { 100, 0, 0, 0, 0 },
                    new[] { 100, 0, 0, 0, 0 }, new[] { 100, 0, 0, 0, 0 },
                    new[] { 100, 0, 0, 0, 0 }
                });
            AssignedCharacterCosts costs = FixedCosts(definitions, new[] { 1 });
            CharacterPool initialPool = CharacterShopRules.CreateInitialPool(definitions, costs, config);
            var first = CharacterShopRules.Generate(definitions, costs, initialPool, config, 1, 3, 0,
                SimulationRandomStreams.CreateStream(71UL, RandomStreamId.Shop));

            Assert.That(first.Shop.Offers.Count, Is.EqualTo(2));
            Assert.That(first.Pool.Entries["C1"].RemainingCopies, Is.Zero);
            Assert.That(first.Pool.Entries["C1"].ReservedCopies, Is.EqualTo(2));

            ShopOffer firstOffer = first.Shop.Offers[0];
            ShopOffer secondOffer = first.Shop.Offers[1];
            var afterPurchasePool = new CharacterPool(new[] { new CharacterPoolEntry("C1", 0, 2, 1, 1) });
            var purchasedShop = new ShopState(ShopKind.Character,
                new[]
                {
                    new ShopOffer(firstOffer.OfferId, ShopOfferKind.Character, "C1", 1, 1, ShopOfferStatus.Purchased),
                    secondOffer
                }, 2, 1, 3, false, 0);

            var refreshed = CharacterShopRules.Refresh(purchasedShop, definitions, costs, afterPurchasePool, config,
                SimulationRandomStreams.CreateStream(72UL, RandomStreamId.Shop));

            Assert.That(refreshed.Shop.GenerationIndex, Is.EqualTo(1));
            Assert.That(refreshed.Shop.SlotCount, Is.EqualTo(2));
            Assert.That(refreshed.Shop.Offers.Count, Is.EqualTo(2));
            Assert.That(refreshed.Pool.Entries["C1"].OwnedCopies, Is.EqualTo(1));
            Assert.That(refreshed.Pool.Entries["C1"].RemainingCopies + refreshed.Pool.Entries["C1"].ReservedCopies +
                        refreshed.Pool.Entries["C1"].OwnedCopies, Is.EqualTo(2));
            Assert.That(afterPurchasePool.Entries["C1"].ReservedCopies, Is.EqualTo(1), "Input pool must remain immutable.");

            var twiceRefreshed = CharacterShopRules.Refresh(refreshed.Shop, definitions, costs, refreshed.Pool, config,
                SimulationRandomStreams.CreateStream(73UL, RandomStreamId.Shop));
            Assert.That(twiceRefreshed.Shop.GenerationIndex, Is.EqualTo(2));
            Assert.That(twiceRefreshed.Pool.Entries["C1"].OwnedCopies, Is.EqualTo(1));
            Assert.That(twiceRefreshed.Pool.Entries["C1"].RemainingCopies + twiceRefreshed.Pool.Entries["C1"].ReservedCopies +
                        twiceRefreshed.Pool.Entries["C1"].OwnedCopies, Is.EqualTo(2));
        }

        [Test]
        public void ShopAndRosterRandomStreams_RemainIndependent()
        {
            List<CharacterDefinition> definitions = CreateDefinitions(30);
            var config = CreateConfiguration(new[] { 6, 6, 6, 6, 6 }, new[] { 15, 15, 15, 15, 15 }, new[] { 3, 3, 3, 3, 3 });
            var streamSetA = new SimulationRandomStreams(8282UL);
            var streamSetB = new SimulationRandomStreams(8282UL);
            AssignedCharacterCosts before = CharacterCostAssigner.Assign(definitions, config, streamSetA.Roster);
            AssignedCharacterCosts after = CharacterCostAssigner.Assign(definitions, config, streamSetB.Roster);
            AssignedCharacterCosts secondExpected = CharacterCostAssigner.Assign(definitions, config, streamSetB.Roster);
            foreach (CharacterDefinition definition in definitions)
                Assert.That(before.GetCost(definition.DefinitionId), Is.EqualTo(after.GetCost(definition.DefinitionId)));

            CharacterPool pool = CharacterShopRules.CreateInitialPool(definitions, before, config);
            for (int i = 0; i < 50; i++)
                CharacterShopRules.Generate(definitions, before, pool, config, 1, 1, i, streamSetA.Shop);
            AssignedCharacterCosts afterShopUse = CharacterCostAssigner.Assign(definitions, config, streamSetA.Roster);
            foreach (CharacterDefinition definition in definitions)
                Assert.That(afterShopUse.GetCost(definition.DefinitionId), Is.EqualTo(secondExpected.GetCost(definition.DefinitionId)));
        }

        private static List<CharacterDefinition> CreateDefinitions(int count)
        {
            var definitions = new List<CharacterDefinition>(count);
            for (int i = 1; i <= count; i++)
                definitions.Add(new CharacterDefinition("C" + i, "Character " + i, 1, null, null));
            return definitions;
        }

        private static AssignedCharacterCosts FixedCosts(IReadOnlyList<CharacterDefinition> definitions, int[] costByIndex)
        {
            var pairs = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < definitions.Count; i++) pairs.Add(new KeyValuePair<string, int>(definitions[i].DefinitionId, costByIndex[i]));
            return new AssignedCharacterCosts(pairs);
        }

        private static CharacterPool CreatePool(IReadOnlyList<CharacterDefinition> definitions,
            AssignedCharacterCosts costs, int[] remainingByCost, int[] ownedByCost)
        {
            var entries = new List<CharacterPoolEntry>();
            foreach (CharacterDefinition definition in definitions)
            {
                int cost = costs.GetCost(definition.DefinitionId) - 1;
                int remaining = remainingByCost[cost];
                int owned = ownedByCost[cost];
                entries.Add(new CharacterPoolEntry(definition.DefinitionId, remaining, remaining + owned, 0, owned));
            }
            return new CharacterPool(entries);
        }

        private static CharacterShopConfiguration CreateConfiguration(int[] rosterCounts,
            int[] poolCounts, int[] slots, int[][] weights = null)
        {
            var rows = new List<IEnumerable<int>>();
            foreach (int[] row in weights ?? SceneWeights) rows.Add(row);
            return new CharacterShopConfiguration(rosterCounts, poolCounts, rows, slots);
        }
    }
}
