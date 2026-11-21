using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Randomness
{
    /// <summary>Deterministic random values for simulation rules. No Unity dependencies.</summary>
    public interface IRandomSource
    {
        int NextInt(int minInclusive, int maxExclusive);
        double NextUnitDouble();
        void Shuffle<T>(IList<T> list);
    }

    /// <summary>Random source whose exact internal state can be checkpointed and restored.</summary>
    public interface IRestorableRandomSource : IRandomSource
    {
        Pcg32State CaptureState();
        void RestoreState(Pcg32State state);
    }
}
