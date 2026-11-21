namespace ProjectNST.GameCore.Randomness
{
    /// <summary>Reproduction metadata for one simulation trial.</summary>
    public readonly struct SimulationSeed
    {
        public ulong MasterSeed { get; }
        public ulong TrialIndex { get; }
        public ulong TrialSeed { get; }
        public string AlgorithmVersion => TrialSeedGenerator.AlgorithmId;

        public SimulationSeed(ulong masterSeed, ulong trialIndex)
        {
            MasterSeed = masterSeed;
            TrialIndex = trialIndex;
            TrialSeed = TrialSeedGenerator.CreateTrialSeed(masterSeed, trialIndex);
        }
    }

    /// <summary>Stable, framework-independent seed derivation. Algorithm version: SplitMix64-v1.</summary>
    public static class TrialSeedGenerator
    {
        public const string AlgorithmId = "SplitMix64-v1";
        private const ulong GoldenRatio = 0x9E3779B97F4A7C15UL;
        private const ulong TrialDomain = 0xD1B54A32D192ED03UL;

        public static SimulationSeed Create(ulong masterSeed, ulong trialIndex)
        {
            return new SimulationSeed(masterSeed, trialIndex);
        }

        /// <summary>
        /// Derives a trial seed. For a fixed MasterSeed, the mixing steps are bijective
        /// over ulong TrialIndex values, so distinct indices map to distinct trial seeds.
        /// </summary>
        public static ulong CreateTrialSeed(ulong masterSeed, ulong trialIndex)
        {
            ulong indexed = Mix64(unchecked(trialIndex + GoldenRatio));
            return Mix64(masterSeed ^ indexed ^ TrialDomain);
        }

        internal static ulong Mix64(ulong value)
        {
            value = unchecked(value + GoldenRatio);
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
