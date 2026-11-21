using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;
using UnityEngine;

namespace ProjectNST.GameCore.Tests
{
    public sealed class CharacterMigrationTests
    {
        [Test]
        public void DefinitionCopiesTagsAndRejectsEmptyId()
        {
            string[] attributes = { "Mage" };
            string[] units = { "North" };
            var definition = new CharacterDefinition("asset-guid-a", "Hero", 2, attributes, units);
            attributes[0] = "Changed";
            units[0] = "Changed";
            Assert.AreEqual("Mage", definition.Attributes[0]);
            Assert.AreEqual("North", definition.Units[0]);
            Assert.Throws<ArgumentException>(() => new CharacterDefinition(" ", "Hero", 1, null, null));
        }

        [Test]
        public void SameDefinitionCanCreateIndependentInstancesWithRunCost()
        {
            var definition = new CharacterDefinition("guid", "Hero", 5, null, null);
            var factory = new CharacterInstanceFactory("C");
            var first = factory.Create(definition, 2);
            var second = factory.Create(definition, 4);
            Assert.AreEqual(first.DefinitionId, second.DefinitionId);
            Assert.AreNotEqual(first.InstanceId, second.InstanceId);
            Assert.AreEqual(2, first.RunCost);
            Assert.AreEqual(4, second.RunCost);
            Assert.AreEqual("C1", first.InstanceId);
            Assert.AreEqual("C2", second.InstanceId);
            Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterInstance("C3", definition, 1, 4));
            Assert.Throws<ArgumentException>(() => new CharacterInstance("", definition, 1));
        }

        [Test]
        public void WeightedGenerationSelectsAttackDefensePairWithOneDraw()
        {
            var stats = MakeStats(1,
                new CharacterStatOption(10, 20, 1), new CharacterStatOption(30, 40, 3));
            var random = new SequenceRandom(0, 1, 3);
            Assert.IsTrue(CharacterStatGenerator.TryGenerate(stats, 1, 1, random, out int atk0, out int def0));
            Assert.IsTrue(CharacterStatGenerator.TryGenerate(stats, 1, 1, random, out int atk1, out int def1));
            Assert.IsTrue(CharacterStatGenerator.TryGenerate(stats, 1, 1, random, out int atk3, out int def3));
            Assert.AreEqual((10, 20), (atk0, def0));
            Assert.AreEqual((30, 40), (atk1, def1));
            Assert.AreEqual((30, 40), (atk3, def3));
            Assert.AreEqual(3, random.DrawCount);
        }

        [Test]
        public void WeightedDistributionTracksConfiguredWeights()
        {
            var stats = MakeStats(1,
                new CharacterStatOption(1, 0, 1), new CharacterStatOption(2, 0, 3));
            var rng = new Pcg32RandomSource(742, 19);
            int first = 0;
            const int samples = 8000;
            for (int i = 0; i < samples; i++)
            {
                Assert.IsTrue(CharacterStatGenerator.TryGenerate(stats, 1, 1, rng, out int attack, out _));
                if (attack == 1) first++;
            }
            Assert.That((double)first / samples, Is.InRange(0.22, 0.28));
        }

        [Test]
        public void InvalidConfigurationFailsWithoutChangingCurrentInstanceStats()
        {
            var definition = new CharacterDefinition("guid", "Hero", 1, null, null);
            var invalidStats = MakeStats(1, new CharacterStatOption(3, 2, -1));
            var instance = new CharacterInstance("C1", definition, 1);
            Assert.IsFalse(instance.EnsureStats(definition, invalidStats, new SequenceRandom(0)));
            Assert.IsFalse(instance.HasRolledStats);
            Assert.AreEqual(0, instance.Attack);
            Assert.IsFalse(CharacterStatGenerator.TryGenerate(MakeStats(1), 1, 1,
                new SequenceRandom(0), out _, out _));
            Assert.IsFalse(CharacterStatGenerator.TryGenerate(MakeStats(1, new CharacterStatOption(1, 1, 0)),
                1, 1, new SequenceRandom(0), out _, out _));
        }

        [Test]
        public void SamePcgSeedProducesSameStatsAndOtherStreamsDoNotAffectStats()
        {
            var stats = MakeStats(2,
                new CharacterStatOption(2, 2, 1), new CharacterStatOption(3, 1, 2));
            var streamSetA = new SimulationRandomStreams(123456UL);
            var streamSetB = new SimulationRandomStreams(123456UL);
            for (int i = 0; i < 20; i++) streamSetA.Shop.NextInt(0, 100);
            Assert.IsTrue(CharacterStatGenerator.TryGenerate(stats, 2, 1, streamSetA.Stats, out int aAtk, out int aDef));
            Assert.IsTrue(CharacterStatGenerator.TryGenerate(stats, 2, 1, streamSetB.Stats, out int bAtk, out int bDef));
            Assert.AreEqual((aAtk, aDef), (bAtk, bDef));
        }

        [Test]
        public void StarChangesRerollAndTemporaryStarRevertsLikeLegacyInstance()
        {
            var definition = new CharacterDefinition("guid", "Hero", 1, null, null);
            var stats = new CharacterStatDefinition(new[] {
                new CharacterCostStats(1, new[] { new CharacterStatOption(2, 1, 1) },
                    new[] { new CharacterStatOption(4, 2, 1) }, new[] { new CharacterStatOption(7, 3, 1) }) });
            var instance = new CharacterInstance("C1", definition, 1);
            Assert.IsTrue(instance.EnsureStats(definition, stats, new SequenceRandom(0)));
            Assert.AreEqual((2, 1), (instance.Attack, instance.Defense));
            Assert.IsTrue(instance.TrySetStar(2, definition, stats, new SequenceRandom(0)));
            Assert.AreEqual((4, 2), (instance.Attack, instance.Defense));
            Assert.IsTrue(instance.TrySetTemporaryStar(3, definition, stats, new SequenceRandom(0)));
            Assert.AreEqual(3, instance.Star);
            instance.RevertTemporaryStar();
            Assert.AreEqual(2, instance.Star);
            Assert.AreEqual((4, 2), (instance.Attack, instance.Defense));
        }

        [Test]
        public void UnityCharacterMapperCopiesWithoutChangingScriptableObject()
        {
            Type dataType = Type.GetType("CharacterData, Assembly-CSharp");
            Type mapperType = Type.GetType("ProjectNST.GameUnityAdapter.CharacterDataMapper, Assembly-CSharp");
            Assert.NotNull(dataType);
            Assert.NotNull(mapperType);
            ScriptableObject source = ScriptableObject.CreateInstance(dataType);
            try
            {
                Set(dataType, source, "characterName", "Hero");
                Set(dataType, source, "cost", 4);
                string[] attributes = { "Mage" };
                string[] units = { "North" };
                Set(dataType, source, "attributes", attributes);
                Set(dataType, source, "units", units);
                object mapped = mapperType.GetMethod("ToDefinition").Invoke(null, new object[] { source, "stable-guid" });
                attributes[0] = "Mutated";
                units[0] = "Mutated";
                Assert.AreEqual(4, Get<int>(dataType, source, "cost"));
                Assert.AreEqual("Mage", ((CharacterDefinition)mapped).Attributes[0]);
                Assert.AreEqual("North", ((CharacterDefinition)mapped).Units[0]);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void UnityStatMapperCopiesCandidateSettingsWithoutMutatingAsset()
        {
            Type settingsType = Type.GetType("CharacterStatSettings, Assembly-CSharp");
            Type mapperType = Type.GetType("ProjectNST.GameUnityAdapter.CharacterStatSettingsMapper, Assembly-CSharp");
            Assert.NotNull(settingsType);
            Assert.NotNull(mapperType);
            ScriptableObject source = ScriptableObject.CreateInstance(settingsType);
            try
            {
                Type costType = settingsType.GetNestedType("CostStats");
                Type optionType = settingsType.GetNestedType("StarStats");
                object cost = Activator.CreateInstance(costType);
                Set(costType, cost, "cost", 1);
                Array star1 = Array.CreateInstance(optionType, 1);
                object option = Activator.CreateInstance(optionType);
                Set(optionType, option, "attack", 5);
                Set(optionType, option, "defense", 2);
                Set(optionType, option, "weight", 1);
                star1.SetValue(option, 0);
                Set(costType, cost, "star1Options", star1);
                Array costs = Array.CreateInstance(costType, 1);
                costs.SetValue(cost, 0);
                Set(settingsType, source, "costs", costs);

                var mapped = (CharacterStatDefinition)mapperType.GetMethod("ToDefinition").Invoke(null, new object[] { source });
                Assert.AreEqual(5, Get<int>(optionType, option, "attack"), "Mapper must not change the ScriptableObject.");
                Set(optionType, option, "attack", 999);
                Assert.IsTrue(CharacterStatGenerator.TryGenerate(mapped, 1, 1, new SequenceRandom(0), out int atk, out int def));
                Assert.AreEqual((5, 2), (atk, def));
                Assert.AreEqual(999, Get<int>(optionType, option, "attack"));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private static CharacterStatDefinition MakeStats(int cost, params CharacterStatOption[] star1)
        {
            return new CharacterStatDefinition(new[] { new CharacterCostStats(cost, star1, null, null) });
        }

        private static void Set(Type type, object target, string field, object value) =>
            type.GetField(field).SetValue(target, value);

        private static T Get<T>(Type type, object target, string field) =>
            (T)type.GetField(field).GetValue(target);

        private sealed class SequenceRandom : IRandomSource
        {
            private readonly Queue<int> _values;
            public int DrawCount { get; private set; }
            public SequenceRandom(params int[] values) { _values = new Queue<int>(values); }
            public int NextInt(int minInclusive, int maxExclusive)
            {
                DrawCount++;
                int value = _values.Count == 0 ? minInclusive : _values.Dequeue();
                if (value < minInclusive || value >= maxExclusive) throw new InvalidOperationException("Test draw outside range.");
                return value;
            }
            public double NextUnitDouble() => 0.5;
            public void Shuffle<T>(IList<T> list) { }
        }
    }
}
