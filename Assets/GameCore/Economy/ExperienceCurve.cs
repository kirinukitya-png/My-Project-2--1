using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Economy
{
    public readonly struct ExperienceProgress
    {
        public int BeforeLevel { get; }
        public int BeforeXP { get; }
        public int AfterLevel { get; }
        public int AfterXP { get; }
        public int LevelsGained => AfterLevel - BeforeLevel;

        internal ExperienceProgress(int level, int xp, int afterLevel, int afterXP)
        {
            BeforeLevel = level;
            BeforeXP = xp;
            AfterLevel = afterLevel;
            AfterXP = afterXP;
        }
    }

    public sealed class ExperienceCurve
    {
        public IReadOnlyList<int> Thresholds { get; }
        public int MaximumLevel => Thresholds.Count + 1;

        public ExperienceCurve(IEnumerable<int> thresholds)
        {
            if (thresholds == null) throw new ArgumentNullException(nameof(thresholds));
            int[] copy = new List<int>(thresholds).ToArray();
            foreach (int required in copy)
                if (required < 1)
                    throw new ArgumentOutOfRangeException(nameof(thresholds), "Every level threshold must be positive.");
            Thresholds = Array.AsReadOnly(copy);
        }

        public int RequiredXP(int level)
        {
            if (level < 1 || level > MaximumLevel) throw new ArgumentOutOfRangeException(nameof(level));
            return level == MaximumLevel ? 0 : Thresholds[level - 1];
        }

        internal void ValidateProgress(int level, int xp)
        {
            int required = RequiredXP(level);
            if (xp < 0 || (required == 0 ? xp != 0 : xp >= required))
                throw new ArgumentOutOfRangeException(nameof(xp), "XP must be normalized for the current level.");
        }

        public ExperienceProgress Advance(int level, int xp, int amount)
        {
            ValidateProgress(level, xp);
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            return AdvanceCore(level, xp, amount);
        }

        // Existing economy transactions already validate their inputs and historically normalize unspent XP.
        internal ExperienceProgress AdvanceCore(int level, int xp, int amount)
        {
            long remaining = (long)xp + amount;
            int nextLevel = level;
            while (nextLevel < MaximumLevel && remaining >= Thresholds[nextLevel - 1])
            {
                remaining -= Thresholds[nextLevel - 1];
                nextLevel++;
            }
            return new ExperienceProgress(level, xp, nextLevel,
                nextLevel == MaximumLevel ? 0 : checked((int)remaining));
        }
    }
}
