using System;
using System.Collections.Generic;
using System.Text;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Strategy
{
    /// <summary>Immutable, versioned checkpoint for the Strategy-only PCG stream.</summary>
    [Serializable]
    public sealed class StrategyRngState : IEquatable<StrategyRngState>
    {
        public const int CurrentCheckpointVersion = 1;
        public string AlgorithmId { get; }
        public int AlgorithmVersion { get; }
        public int CheckpointVersion { get; }
        public ulong TrialSeed { get; }
        public string StrategyId { get; }
        public string StrategyVersion { get; }
        public Pcg32State State { get; }

        public StrategyRngState(ulong trialSeed, string strategyId, string strategyVersion,
            Pcg32State state)
            : this(Pcg32RandomSource.AlgorithmId, Pcg32State.CurrentAlgorithmVersion,
                CurrentCheckpointVersion, trialSeed, strategyId, strategyVersion, state)
        {
        }

        public StrategyRngState(string algorithmId, int algorithmVersion, int checkpointVersion,
            ulong trialSeed, string strategyId, string strategyVersion, Pcg32State state)
        {
            if (!string.Equals(algorithmId, Pcg32RandomSource.AlgorithmId, StringComparison.Ordinal))
                throw new ArgumentException("Unsupported Strategy RNG algorithm.", nameof(algorithmId));
            if (algorithmVersion != Pcg32State.CurrentAlgorithmVersion)
                throw new ArgumentOutOfRangeException(nameof(algorithmVersion));
            if (checkpointVersion != CurrentCheckpointVersion)
                throw new ArgumentOutOfRangeException(nameof(checkpointVersion));
            if (string.IsNullOrWhiteSpace(strategyId)) throw new ArgumentException("Strategy ID is required.", nameof(strategyId));
            if (string.IsNullOrWhiteSpace(strategyVersion)) throw new ArgumentException("Strategy version is required.", nameof(strategyVersion));
            Pcg32RandomSource.ValidateState(state);
            AlgorithmId = algorithmId;
            AlgorithmVersion = algorithmVersion;
            CheckpointVersion = checkpointVersion;
            TrialSeed = trialSeed;
            StrategyId = strategyId;
            StrategyVersion = strategyVersion;
            State = state;
        }

        public bool Equals(StrategyRngState other) => other != null &&
            AlgorithmVersion == other.AlgorithmVersion && CheckpointVersion == other.CheckpointVersion &&
            TrialSeed == other.TrialSeed && State.AlgorithmVersion == other.State.AlgorithmVersion &&
            State.State == other.State.State && State.Increment == other.State.Increment &&
            string.Equals(AlgorithmId, other.AlgorithmId, StringComparison.Ordinal) &&
            string.Equals(StrategyId, other.StrategyId, StringComparison.Ordinal) &&
            string.Equals(StrategyVersion, other.StrategyVersion, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as StrategyRngState);
        public override int GetHashCode() => unchecked((int)(TrialSeed ^ State.State ^ State.Increment));
    }

    /// <summary>
    /// Mutable, decision-local random source. Recreate it from StrategyRngState for each call;
    /// only the returned checkpoint can be adopted by the caller.
    /// </summary>
    public sealed class StrategyRandomSource
    {
        private readonly Pcg32RandomSource _random;
        private readonly ulong _trialSeed;
        private readonly string _strategyId;
        private readonly string _strategyVersion;

        private StrategyRandomSource(ulong trialSeed, string strategyId, string strategyVersion,
            Pcg32RandomSource random)
        {
            _trialSeed = trialSeed;
            _strategyId = strategyId;
            _strategyVersion = strategyVersion;
            _random = random;
        }

        public static StrategyRandomSource Create(ulong trialSeed, string strategyId,
            string strategyVersion)
        {
            ValidateIdentity(strategyId, strategyVersion);
            ulong discriminator = StableIdentityHash(strategyId, strategyVersion);
            Pcg32RandomSource random = SimulationRandomStreams.CreateStream(trialSeed,
                RandomStreamId.Strategy, discriminator);
            return new StrategyRandomSource(trialSeed, strategyId, strategyVersion, random);
        }

        public static StrategyRandomSource FromState(StrategyRngState checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            ValidateCheckpoint(checkpoint, checkpoint.TrialSeed, checkpoint.StrategyId, checkpoint.StrategyVersion);
            return new StrategyRandomSource(checkpoint.TrialSeed, checkpoint.StrategyId,
                checkpoint.StrategyVersion, Pcg32RandomSource.FromState(checkpoint.State));
        }

        public static StrategyRandomSource FromState(StrategyRngState checkpoint,
            ulong trialSeed, string strategyId, string strategyVersion)
        {
            ValidateCheckpoint(checkpoint, trialSeed, strategyId, strategyVersion);
            return new StrategyRandomSource(trialSeed, strategyId, strategyVersion,
                Pcg32RandomSource.FromState(checkpoint.State));
        }

        public int NextInt(int minInclusive, int maxExclusive) => _random.NextInt(minInclusive, maxExclusive);
        public double NextUnitDouble() => _random.NextUnitDouble();
        public void Shuffle<T>(IList<T> list) => _random.Shuffle(list);

        public StrategyRngState CaptureState() => new StrategyRngState(_trialSeed,
            _strategyId, _strategyVersion, _random.CaptureState());

        private static void ValidateCheckpoint(StrategyRngState checkpoint, ulong trialSeed,
            string strategyId, string strategyVersion)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            ValidateIdentity(strategyId, strategyVersion);
            if (checkpoint.TrialSeed != trialSeed ||
                !string.Equals(checkpoint.StrategyId, strategyId, StringComparison.Ordinal) ||
                !string.Equals(checkpoint.StrategyVersion, strategyVersion, StringComparison.Ordinal))
                throw new ArgumentException("Strategy RNG checkpoint belongs to a different trial or Strategy version.", nameof(checkpoint));
            if (checkpoint.CheckpointVersion != StrategyRngState.CurrentCheckpointVersion ||
                checkpoint.AlgorithmVersion != Pcg32State.CurrentAlgorithmVersion ||
                !string.Equals(checkpoint.AlgorithmId, Pcg32RandomSource.AlgorithmId, StringComparison.Ordinal))
                throw new ArgumentException("Unsupported Strategy RNG checkpoint version.", nameof(checkpoint));
            Pcg32RandomSource.ValidateState(checkpoint.State);
        }

        private static void ValidateIdentity(string strategyId, string strategyVersion)
        {
            if (string.IsNullOrWhiteSpace(strategyId)) throw new ArgumentException("Strategy ID is required.", nameof(strategyId));
            if (string.IsNullOrWhiteSpace(strategyVersion)) throw new ArgumentException("Strategy version is required.", nameof(strategyVersion));
        }

        private static ulong StableIdentityHash(string strategyId, string strategyVersion)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            byte[] id = Encoding.UTF8.GetBytes(strategyId);
            byte[] version = Encoding.UTF8.GetBytes(strategyVersion);
            unchecked
            {
                for (int i = 0; i < id.Length; i++) hash = (hash ^ id[i]) * prime;
                hash = (hash ^ 0xFF) * prime;
                for (int i = 0; i < version.Length; i++) hash = (hash ^ version[i]) * prime;
            }
            return TrialSeedGenerator.Mix64(hash);
        }
    }
}
