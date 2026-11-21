using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Tests
{
    public sealed class RandomnessTests
    {
        [Test]
        public void NextInt_UsesInclusiveMinimumAndExclusiveMaximum()
        {
            var rng = new Pcg32RandomSource(12UL, 34UL);
            bool sawMinimum = false;
            bool sawBelowMaximum = false;
            for (int i = 0; i < 2000; i++)
            {
                int value = rng.NextInt(-4, 5);
                Assert.That(value, Is.InRange(-4, 4));
                sawMinimum |= value == -4;
                sawBelowMaximum |= value == 4;
            }
            Assert.That(sawMinimum, Is.True);
            Assert.That(sawBelowMaximum, Is.True);
        }

        [Test]
        public void NextInt_SupportsFullSignedRangeWithoutOverflow()
        {
            var rng = new Pcg32RandomSource(1UL, 2UL);
            for (int i = 0; i < 1000; i++)
                Assert.That(rng.NextInt(int.MinValue, int.MaxValue), Is.InRange(int.MinValue, int.MaxValue - 1));
        }

        [TestCase(0, 0)]
        [TestCase(3, 3)]
        [TestCase(5, -5)]
        public void NextInt_ThrowsForInvalidRange(int min, int max)
        {
            var rng = new Pcg32RandomSource(1UL, 2UL);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(min, max));
        }

        [Test]
        public void NextUnitDouble_IsWithinZeroInclusiveOneExclusive()
        {
            var rng = new Pcg32RandomSource(567UL, 89UL);
            for (int i = 0; i < 10000; i++)
            {
                double value = rng.NextUnitDouble();
                Assert.That(value, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(value, Is.LessThan(1.0));
            }
        }

        [Test]
        public void Shuffle_PreservesAllElementsAndHandlesSmallLists()
        {
            var rng = new Pcg32RandomSource(456UL, 789UL);
            var values = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            rng.Shuffle(values);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, values);
            Assert.Throws<ArgumentNullException>(() => rng.Shuffle<int>(null));
            rng.Shuffle(new List<int>());
            var single = new List<int> { 42 };
            rng.Shuffle(single);
            CollectionAssert.AreEqual(new[] { 42 }, single);
        }

        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var first = new Pcg32RandomSource(0x12345678UL, 0xABCDEFUL);
            var second = new Pcg32RandomSource(0x12345678UL, 0xABCDEFUL);
            for (int i = 0; i < 1000; i++) Assert.That(first.NextInt(-100, 100), Is.EqualTo(second.NextInt(-100, 100)));
        }

        [Test]
        public void DifferentSeeds_UsuallyProduceDifferentSequences()
        {
            var first = new Pcg32RandomSource(100UL, 200UL);
            var second = new Pcg32RandomSource(101UL, 200UL);
            bool differs = false;
            for (int i = 0; i < 32; i++) differs |= first.NextInt(0, int.MaxValue) != second.NextInt(0, int.MaxValue);
            Assert.That(differs, Is.True);
        }

        [Test]
        public void TrialSeed_IsRepeatableAndChangesWithIndex()
        {
            SimulationSeed seed = TrialSeedGenerator.Create(77UL, 12UL);
            Assert.That(seed.MasterSeed, Is.EqualTo(77UL));
            Assert.That(seed.TrialIndex, Is.EqualTo(12UL));
            Assert.That(seed.AlgorithmVersion, Is.EqualTo(TrialSeedGenerator.AlgorithmId));
            Assert.That(seed.TrialSeed, Is.EqualTo(TrialSeedGenerator.CreateTrialSeed(77UL, 12UL)));
            Assert.That(seed.TrialSeed, Is.EqualTo(TrialSeedGenerator.CreateTrialSeed(77UL, 12UL)));
            Assert.That(seed.TrialSeed, Is.Not.EqualTo(TrialSeedGenerator.CreateTrialSeed(77UL, 13UL)));
            Assert.That(new SimulationRandomStreams(seed).SeedMetadata.Value.TrialSeed, Is.EqualTo(seed.TrialSeed));
        }

        [Test]
        public void Streams_AreStableIndependentAndCreationOrderDoesNotMatter()
        {
            var first = new SimulationRandomStreams(9001UL);
            var second = new SimulationRandomStreams(9001UL);
            AssertStreamEqual(first.Shop, second.Shop);

            var battleBeforeShop = SimulationRandomStreams.CreateStream(9001UL, RandomStreamId.Battle);
            var shopConsumedSeparately = SimulationRandomStreams.CreateStream(9001UL, RandomStreamId.Shop);
            for (int i = 0; i < 500; i++) shopConsumedSeparately.NextInt(0, int.MaxValue);
            var battleAfterShop = SimulationRandomStreams.CreateStream(9001UL, RandomStreamId.Battle);
            AssertStreamEqual(battleBeforeShop, battleAfterShop);

            var reversedBattle = SimulationRandomStreams.CreateStream(9001UL, RandomStreamId.Battle);
            var reversedShop = SimulationRandomStreams.CreateStream(9001UL, RandomStreamId.Shop);
            AssertStreamEqual(reversedBattle, SimulationRandomStreams.CreateStream(9001UL, RandomStreamId.Battle));
            AssertStreamEqual(reversedShop, SimulationRandomStreams.CreateStream(9001UL, RandomStreamId.Shop));
        }

        [Test]
        public void IndividualStreamState_CanBeRestored()
        {
            var rng = new Pcg32RandomSource(23UL, 98UL);
            rng.NextInt(0, 100);
            Pcg32State checkpoint = rng.CaptureState();
            int[] expected = new int[20];
            for (int i = 0; i < expected.Length; i++) expected[i] = rng.NextInt(-500, 500);
            rng.RestoreState(checkpoint);
            for (int i = 0; i < expected.Length; i++) Assert.That(rng.NextInt(-500, 500), Is.EqualTo(expected[i]));
        }

        [Test]
        public void State_CanBeRestoredIntoAnotherGeneratorIncludingSequence()
        {
            var source = new Pcg32RandomSource(111UL, 222UL);
            source.NextUnitDouble();
            Pcg32State checkpoint = source.CaptureState();
            int[] expected = new int[12];
            for (int i = 0; i < expected.Length; i++) expected[i] = source.NextInt(0, 100000);

            var restored = new Pcg32RandomSource(999UL, 888UL);
            restored.RestoreState(checkpoint);
            for (int i = 0; i < expected.Length; i++) Assert.That(restored.NextInt(0, 100000), Is.EqualTo(expected[i]));
        }

        [Test]
        public void AggregateState_RestoresEachStreamIndependently()
        {
            var streams = new SimulationRandomStreams(10101UL);
            streams.Shop.NextUnitDouble();
            streams.Battle.NextInt(0, 15);
            SimulationRandomStreamsState checkpoint = streams.CaptureState();
            int expectedBattle = streams.Battle.NextInt(0, int.MaxValue);
            int expectedShop = streams.Shop.NextInt(0, int.MaxValue);
            streams.RestoreState(checkpoint);
            Assert.That(streams.Battle.NextInt(0, int.MaxValue), Is.EqualTo(expectedBattle));
            Assert.That(streams.Shop.NextInt(0, int.MaxValue), Is.EqualTo(expectedShop));
        }

        [Test]
        public void InvalidCheckpoint_IsRejectedWithoutPartialRestore()
        {
            var streams = new SimulationRandomStreams(303UL);
            Pcg32State checkpointShop = streams.Shop.CaptureState();
            streams.Shop.NextInt(0, 1000);
            Pcg32State beforeReject = streams.Shop.CaptureState();
            SimulationRandomStreamsState invalid = new SimulationRandomStreamsState(303UL, 1,
                checkpointShop, streams.Stats.CaptureState(), streams.Encounter.CaptureState(),
                streams.Battle.CaptureState(), new Pcg32State(1, 123UL, 2UL));
            Assert.Throws<ArgumentException>(() => streams.RestoreState(invalid));
            Assert.That(streams.Shop.CaptureState().State, Is.EqualTo(beforeReject.State));
        }

        [Test]
        public void InvalidStateVersionOrEvenIncrement_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => Pcg32RandomSource.FromState(new Pcg32State(99, 1UL, 3UL)));
            Assert.Throws<ArgumentException>(() => Pcg32RandomSource.FromState(new Pcg32State(1, 1UL, 2UL)));
        }

        private static void AssertStreamEqual(Pcg32RandomSource first, Pcg32RandomSource second)
        {
            for (int i = 0; i < 100; i++) Assert.That(first.NextUnitDouble(), Is.EqualTo(second.NextUnitDouble()));
        }
    }
}
