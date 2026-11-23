using System;

namespace ProjectNST.GameCore.Battle
{
    /// <summary>Immutable battle limits. The default safety limit is provisional and configurable.</summary>
    public sealed class BattleConfig
    {
        public const int DefaultMaximumRounds = 100;
        public static BattleConfig Default { get; } = new BattleConfig(DefaultMaximumRounds);

        public int MaxRounds { get; }

        public BattleConfig(int maxRounds)
        {
            if (maxRounds <= 0) throw new ArgumentOutOfRangeException(nameof(maxRounds));
            MaxRounds = maxRounds;
        }
    }
}
