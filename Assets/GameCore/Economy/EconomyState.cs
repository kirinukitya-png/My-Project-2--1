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
        public bool HasHealth { get; }
        public int CurrentHP { get; }
        public int MaximumHP { get; }
        public int InitialHP => MaximumHP;

        public EconomyState(int gold, int xp, int level, int maximumLevel,
            int initialGold, int initialXP, int initialLevel)
            : this(gold, xp, level, maximumLevel, initialGold, initialXP, initialLevel,
                0, 0, false)
        {
        }

        public EconomyState(int gold, int xp, int level, int maximumLevel,
            int initialGold, int initialXP, int initialLevel, int currentHP, int maximumHP)
            : this(gold, xp, level, maximumLevel, initialGold, initialXP, initialLevel,
                currentHP, maximumHP, true)
        {
        }

        private EconomyState(int gold, int xp, int level, int maximumLevel,
            int initialGold, int initialXP, int initialLevel, int currentHP, int maximumHP,
            bool hasHealth)
        {
            if (gold < 0) throw new ArgumentOutOfRangeException(nameof(gold));
            if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));
            if (maximumLevel < 1) throw new ArgumentOutOfRangeException(nameof(maximumLevel));
            if (level < 1 || level > maximumLevel) throw new ArgumentOutOfRangeException(nameof(level));
            if (initialGold < 0) throw new ArgumentOutOfRangeException(nameof(initialGold));
            if (initialXP < 0) throw new ArgumentOutOfRangeException(nameof(initialXP));
            if (initialLevel < 1 || initialLevel > maximumLevel) throw new ArgumentOutOfRangeException(nameof(initialLevel));
            if (hasHealth && maximumHP < 1) throw new ArgumentOutOfRangeException(nameof(maximumHP));
            if (hasHealth && (currentHP < 0 || currentHP > maximumHP))
                throw new ArgumentOutOfRangeException(nameof(currentHP));

            Gold = gold;
            XP = xp;
            Level = level;
            MaximumLevel = maximumLevel;
            InitialGold = initialGold;
            InitialXP = initialXP;
            InitialLevel = initialLevel;
            HasHealth = hasHealth;
            CurrentHP = hasHealth ? currentHP : 0;
            MaximumHP = hasHealth ? maximumHP : 0;
        }

        public EconomyState WithGold(int gold)
        {
            return HasHealth
                ? new EconomyState(gold, XP, Level, MaximumLevel, InitialGold, InitialXP,
                    InitialLevel, CurrentHP, MaximumHP)
                : new EconomyState(gold, XP, Level, MaximumLevel, InitialGold, InitialXP, InitialLevel);
        }

        public EconomyState WithHealth(int currentHP)
        {
            if (!HasHealth) throw new InvalidOperationException("Health values are not configured for this economy state.");
            return new EconomyState(Gold, XP, Level, MaximumLevel, InitialGold, InitialXP,
                InitialLevel, currentHP, MaximumHP);
        }
    }
}
