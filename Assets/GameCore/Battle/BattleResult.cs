using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleFinishReason { EnemiesDefeated, PlayerHealthDepleted, MaximumRoundsReached }

    /// <summary>Presentation-free immutable summary of one completed battle.</summary>
    public sealed class BattleResult
    {
        private readonly BattlePersistentEffectCommand[] _persistentEffects;
        private readonly ItemEffectCommand[] _expiredEnemyCommands;

        public BattleOutcome Outcome { get; }
        public int FinalRound { get; }
        public BattleFinishReason Reason { get; }
        public int StartingPlayerHP { get; }
        public int PlayerHPAtOutcomeCheck { get; }
        public int EndingPlayerHP { get; }
        public long BlockAbsorbed { get; }
        public int LivingEnemyCount { get; }
        public int DefeatedEnemyCount { get; }
        public Pcg32State BattleRandomCheckpoint { get; }
        public EconomyState EconomyAfterBattle { get; }
        /// <summary>Already emitted with successful effect-resolution results; finalization never reapplies them.</summary>
        public IReadOnlyList<BattlePersistentEffectCommand> ConfirmedPersistentEffects { get; }
        public IReadOnlyList<ItemEffectCommand> ExpiredEnemyCommands { get; }

        internal BattleResult(BattleOutcome outcome, int finalRound, BattleFinishReason reason,
            int startingPlayerHp, int outcomePlayerHp, int endingPlayerHp, long blockAbsorbed,
            int livingEnemies, int defeatedEnemies, Pcg32State randomCheckpoint,
            EconomyState economy, IEnumerable<BattlePersistentEffectCommand> persistentEffects,
            IEnumerable<ItemEffectCommand> expiredEnemyCommands)
        {
            Outcome = outcome;
            FinalRound = finalRound;
            Reason = reason;
            StartingPlayerHP = startingPlayerHp;
            PlayerHPAtOutcomeCheck = outcomePlayerHp;
            EndingPlayerHP = endingPlayerHp;
            BlockAbsorbed = blockAbsorbed;
            LivingEnemyCount = livingEnemies;
            DefeatedEnemyCount = defeatedEnemies;
            BattleRandomCheckpoint = randomCheckpoint;
            EconomyAfterBattle = economy;
            _persistentEffects = persistentEffects == null
                ? Array.Empty<BattlePersistentEffectCommand>()
                : new List<BattlePersistentEffectCommand>(persistentEffects).ToArray();
            _expiredEnemyCommands = expiredEnemyCommands == null
                ? Array.Empty<ItemEffectCommand>()
                : new List<ItemEffectCommand>(expiredEnemyCommands).ToArray();
            ConfirmedPersistentEffects = Array.AsReadOnly(_persistentEffects);
            ExpiredEnemyCommands = Array.AsReadOnly(_expiredEnemyCommands);
        }
    }
}
