using System;

namespace ProjectNST.GameCore.Economy
{
    /// <summary>Immutable current and initial economy values; contains no economy rules.</summary>
    public sealed class EconomyState
    {
        public int Gold { get; }
        public int XP { get; }
        public int Level { get; }
        public int MaximumLevel { get; }
        public int InitialGold { get; }
        public int InitialXP { get; }
        public int InitialLevel { get; }

        public EconomyState(int gold, int xp, int level, int maximumLevel,
            int initialGold, int initialXP, int initialLevel)
        {
            if (gold < 0) throw new ArgumentOutOfRangeException(nameof(gold));
            if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));
            if (maximumLevel < 1) throw new ArgumentOutOfRangeException(nameof(maximumLevel));
            if (level < 1 || level > maximumLevel) throw new ArgumentOutOfRangeException(nameof(level));
            if (initialGold < 0) throw new ArgumentOutOfRangeException(nameof(initialGold));
            if (initialXP < 0) throw new ArgumentOutOfRangeException(nameof(initialXP));
            if (initialLevel < 1 || initialLevel > maximumLevel) throw new ArgumentOutOfRangeException(nameof(initialLevel));

            Gold = gold;
            XP = xp;
            Level = level;
            MaximumLevel = maximumLevel;
            InitialGold = initialGold;
            InitialXP = initialXP;
            InitialLevel = initialLevel;
        }
    }
}
