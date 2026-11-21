using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Randomness
{
    /// <summary>Immutable checkpoint for PCG-XSH-RR 32 v1.</summary>
    public readonly struct Pcg32State
    {
        public const int CurrentAlgorithmVersion = 1;

        public int AlgorithmVersion { get; }
        public ulong State { get; }
        public ulong Increment { get; }

        public Pcg32State(int algorithmVersion, ulong state, ulong increment)
        {
            AlgorithmVersion = algorithmVersion;
            State = state;
            Increment = increment;
        }
    }

    /// <summary>
    /// PCG-XSH-RR 32-bit generator. Its state is 64 bits and its sequence selector is
    /// encoded as an odd increment. The algorithm/API contract is version 1.
    /// </summary>
    public sealed class Pcg32RandomSource : IRestorableRandomSource
    {
        public const string AlgorithmId = "PCG-XSH-RR-32-v1";
        private const ulong Multiplier = 6364136223846793005UL;
        private ulong _state;
        private ulong _increment;

        public Pcg32RandomSource(ulong seed, ulong sequence)
        {
            _state = 0UL;
            _increment = unchecked((sequence << 1) | 1UL);
            NextUInt32();
            _state = unchecked(_state + seed);
            NextUInt32();
        }

        private Pcg32RandomSource(Pcg32State state)
        {
            ValidateState(state);
            _state = state.State;
            _increment = state.Increment;
        }

        public static Pcg32RandomSource FromState(Pcg32State state)
        {
            return new Pcg32RandomSource(state);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");

            uint bound = (uint)((long)maxExclusive - minInclusive);
            uint threshold = unchecked(0U - bound) % bound;
            uint value;
            do
            {
                value = NextUInt32();
            } while (value < threshold);

            return (int)((long)minInclusive + (value % bound));
        }

        public double NextUnitDouble()
        {
            // Construct a uniform 53-bit mantissa in [0, 1), as used by binary64 doubles.
            ulong bits = ((ulong)NextUInt32() << 21) | ((ulong)NextUInt32() >> 11);
            return bits * (1.0 / 9007199254740992.0);
        }

        public void Shuffle<T>(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(0, i + 1);
                if (j == i) continue;
                T temporary = list[i];
                list[i] = list[j];
                list[j] = temporary;
            }
        }

        public Pcg32State CaptureState()
        {
            return new Pcg32State(Pcg32State.CurrentAlgorithmVersion, _state, _increment);
        }

        public void RestoreState(Pcg32State state)
        {
            ValidateState(state);
            _state = state.State;
            _increment = state.Increment;
        }

        public static void ValidateState(Pcg32State state)
        {
            if (state.AlgorithmVersion != Pcg32State.CurrentAlgorithmVersion)
                throw new ArgumentException("Unsupported PCG32 algorithm version.", nameof(state));
            if ((state.Increment & 1UL) == 0UL)
                throw new ArgumentException("PCG32 increment must be odd.", nameof(state));
        }

        private uint NextUInt32()
        {
            ulong oldState = _state;
            _state = unchecked(oldState * Multiplier + _increment);
            uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rotation = (int)(oldState >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }
    }
}
