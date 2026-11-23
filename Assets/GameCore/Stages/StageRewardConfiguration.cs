using System;

namespace ProjectNST.GameCore.Stages
{
    /// <summary>Reward values not already supplied by StageProgressConfig or EconomyRulesConfiguration.</summary>
    public sealed class StageRewardConfiguration
    {
        public int BossGoldReward { get; }
        public int BossExperienceReward { get; }

        public StageRewardConfiguration(int bossGoldReward, int bossExperienceReward)
        {
            if (bossGoldReward < 0) throw new ArgumentOutOfRangeException(nameof(bossGoldReward));
            if (bossExperienceReward < 0) throw new ArgumentOutOfRangeException(nameof(bossExperienceReward));
            BossGoldReward = bossGoldReward;
            BossExperienceReward = bossExperienceReward;
        }
    }
}
