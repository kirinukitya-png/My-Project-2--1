using System;

namespace ProjectNST.GameCore.Randomness
{
    /// <summary>Immutable checkpoint of every currently registered simulation stream.</summary>
    public readonly struct SimulationRandomStreamsState
    {
        public const int CurrentVersion = 2;
        public ulong TrialSeed { get; }
        public int Version { get; }
        public Pcg32State Shop { get; }
        public Pcg32State Stats { get; }
        public Pcg32State Encounter { get; }
        public Pcg32State Battle { get; }
        public Pcg32State Event { get; }
        public Pcg32State Roster { get; }
        public Pcg32State Economy { get; }

        public SimulationRandomStreamsState(ulong trialSeed, int version, Pcg32State shop, Pcg32State stats,
            Pcg32State encounter, Pcg32State battle, Pcg32State eventState)
            : this(trialSeed, version, shop, stats, encounter, battle, eventState, default(Pcg32State), default(Pcg32State))
        {
        }

        public SimulationRandomStreamsState(ulong trialSeed, int version, Pcg32State shop, Pcg32State stats,
            Pcg32State encounter, Pcg32State battle, Pcg32State eventState, Pcg32State roster, Pcg32State economy)
        {
            TrialSeed = trialSeed;
            Version = version;
            Shop = shop;
            Stats = stats;
            Encounter = encounter;
            Battle = battle;
            Event = eventState;
            Roster = roster;
            Economy = economy;
        }
    }

    /// <summary>Creates deterministic, domain-separated PCG32 streams for one trial.</summary>
    public sealed class SimulationRandomStreams
    {
        private const ulong SeedDomain = 0xA0761D6478BD642FUL;
        private const ulong SequenceDomain = 0xE7037ED1A0B428DBUL;
        private const ulong StreamMultiplier = 0x9E3779B97F4A7C15UL;

        public ulong TrialSeed { get; }
        public SimulationSeed? SeedMetadata { get; }
        public Pcg32RandomSource Shop { get; }
        public Pcg32RandomSource Stats { get; }
        public Pcg32RandomSource Encounter { get; }
        public Pcg32RandomSource Battle { get; }
        public Pcg32RandomSource Event { get; }
        public Pcg32RandomSource Roster { get; }
        public Pcg32RandomSource Economy { get; }

        public SimulationRandomStreams(ulong trialSeed)
        {
            TrialSeed = trialSeed;
            SeedMetadata = null;
            Shop = CreateStream(trialSeed, RandomStreamId.Shop);
            Stats = CreateStream(trialSeed, RandomStreamId.Stats);
            Encounter = CreateStream(trialSeed, RandomStreamId.Encounter);
            Battle = CreateStream(trialSeed, RandomStreamId.Battle);
            Event = CreateStream(trialSeed, RandomStreamId.Event);
            Roster = CreateStream(trialSeed, RandomStreamId.Roster);
            Economy = CreateStream(trialSeed, RandomStreamId.Economy);
        }

        public SimulationRandomStreams(SimulationSeed seed) : this(seed.TrialSeed)
        {
            SeedMetadata = seed;
        }

        public static Pcg32RandomSource CreateStream(ulong trialSeed, RandomStreamId streamId)
        {
            if (!Enum.IsDefined(typeof(RandomStreamId), streamId))
                throw new ArgumentOutOfRangeException(nameof(streamId), "Register a stable RandomStreamId before creating this stream.");

            ulong domain = unchecked((ulong)streamId * StreamMultiplier);
            ulong seed = TrialSeedGenerator.Mix64(trialSeed ^ domain ^ SeedDomain);
            ulong sequence = TrialSeedGenerator.Mix64(trialSeed ^ RotateLeft(domain, 23) ^ SequenceDomain);
            return new Pcg32RandomSource(seed, sequence);
        }

        /// <summary>
        /// Creates a deterministic substream within a registered stream domain. Existing
        /// two-argument stream creation remains byte-for-byte unchanged; Strategy uses this
        /// overload to separate each stable Strategy ID/version under stream ID 8.
        /// </summary>
        public static Pcg32RandomSource CreateStream(ulong trialSeed, RandomStreamId streamId,
            ulong discriminator)
        {
            if (!Enum.IsDefined(typeof(RandomStreamId), streamId))
                throw new ArgumentOutOfRangeException(nameof(streamId), "Register a stable RandomStreamId before creating this stream.");

            ulong baseDomain = unchecked((ulong)streamId * StreamMultiplier);
            ulong discriminatorDomain = TrialSeedGenerator.Mix64(discriminator ^ 0x8EBC6AF09C88C6E3UL);
            ulong domain = baseDomain ^ RotateLeft(discriminatorDomain, 17);
            ulong seed = TrialSeedGenerator.Mix64(trialSeed ^ domain ^ SeedDomain);
            ulong sequence = TrialSeedGenerator.Mix64(trialSeed ^ RotateLeft(domain, 23) ^ SequenceDomain);
            return new Pcg32RandomSource(seed, sequence);
        }

        public SimulationRandomStreamsState CaptureState()
        {
            return new SimulationRandomStreamsState(TrialSeed, SimulationRandomStreamsState.CurrentVersion,
                Shop.CaptureState(), Stats.CaptureState(), Encounter.CaptureState(), Battle.CaptureState(), Event.CaptureState(),
                Roster.CaptureState(), Economy.CaptureState());
        }

        public void RestoreState(SimulationRandomStreamsState state)
        {
            if (state.Version != SimulationRandomStreamsState.CurrentVersion)
                throw new ArgumentException("Unsupported random-stream checkpoint version.", nameof(state));
            if (state.TrialSeed != TrialSeed)
                throw new ArgumentException("Checkpoint TrialSeed does not match this stream set.", nameof(state));

            // Validate the entire checkpoint before mutating any stream.
            Pcg32RandomSource.ValidateState(state.Shop);
            Pcg32RandomSource.ValidateState(state.Stats);
            Pcg32RandomSource.ValidateState(state.Encounter);
            Pcg32RandomSource.ValidateState(state.Battle);
            Pcg32RandomSource.ValidateState(state.Event);
            Pcg32RandomSource.ValidateState(state.Roster);
            Pcg32RandomSource.ValidateState(state.Economy);

            Shop.RestoreState(state.Shop);
            Stats.RestoreState(state.Stats);
            Encounter.RestoreState(state.Encounter);
            Battle.RestoreState(state.Battle);
            Event.RestoreState(state.Event);
            Roster.RestoreState(state.Roster);
            Economy.RestoreState(state.Economy);
        }

        private static ulong RotateLeft(ulong value, int amount)
        {
            return (value << amount) | (value >> (64 - amount));
        }
    }
}
