using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleEffectFailure
    {
        None, InvalidState, BattleAlreadyCompleted, NoPendingEffect, TriggerMismatch, AlreadyProcessed,
        MissingDefinition, InvalidDefinition, InvalidTriggerTarget, TargetNotFound,
        EconomyHealthUnavailable, ArithmeticOverflow
    }

    public sealed class BattleEffectResolutionResult
    {
        private readonly string[] _appliedEffectIds;
        private readonly BattlePersistentEffectCommand[] _persistentCommands;
        public bool Succeeded { get; }
        public BattleEffectFailure Failure { get; }
        public string TriggerId { get; }
        public IReadOnlyList<string> AppliedEffectIds { get; }
        public IReadOnlyList<BattlePersistentEffectCommand> PersistentCommands { get; }
        public BattleState NextState { get; }
        public EconomyState NextEconomyState { get; }
        public Pcg32State NextBattleRandomState { get; }

        internal BattleEffectResolutionResult(bool succeeded, BattleEffectFailure failure,
            string triggerId, IEnumerable<string> effectIds, IEnumerable<BattlePersistentEffectCommand> commands,
            BattleState battle, EconomyState economy, Pcg32State rng)
        {
            Succeeded = succeeded; Failure = failure; TriggerId = triggerId;
            _appliedEffectIds = effectIds == null ? Array.Empty<string>() : new List<string>(effectIds).ToArray();
            _persistentCommands = commands == null ? Array.Empty<BattlePersistentEffectCommand>() : new List<BattlePersistentEffectCommand>(commands).ToArray();
            AppliedEffectIds = Array.AsReadOnly(_appliedEffectIds);
            PersistentCommands = Array.AsReadOnly(_persistentCommands);
            NextState = battle; NextEconomyState = economy; NextBattleRandomState = rng;
        }
    }
}
