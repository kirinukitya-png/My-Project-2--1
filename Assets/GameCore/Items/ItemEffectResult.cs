using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Items
{
    public enum ItemEffectFailure
    {
        None,
        InvalidInput,
        InvalidPhase,
        InvalidItem,
        ItemNotAvailable,
        ItemNotInHand,
        InvalidTarget,
        TargetMissing,
        UnsupportedEffect,
        InvalidEffectValue,
        CharacterNotEligible,
        MissingCharacterDefinition,
        StatsUnavailable,
        HealthUnavailable,
        InvalidState,
        ArithmeticOverflow,
        EventNotPending,
        EventItemUnavailable,
        EventItemMismatch,
        EligibleEventTargetExists,
        NoBattleEffectsToRestore
    }

    /// <summary>Atomic proposal for using or restoring item effects; inputs are never mutated.</summary>
    public sealed class ItemEffectResult
    {
        public bool Succeeded { get; }
        public ItemEffectFailure Failure { get; }
        public BoardState NextBoard { get; }
        public HandState NextHand { get; }
        public CharacterOwnershipState NextOwnership { get; }
        public EconomyState NextEconomy { get; }
        public ItemEffectState NextEffectState { get; }
        public StageProgressState NextStage { get; }
        public EventItemState NextEventItem { get; }
        public string ConsumedItemDefinitionId { get; }
        public IReadOnlyList<ItemEffectCommand> Commands { get; }
        public IReadOnlyList<ItemEffectCommand> ExpiredEnemyCommands { get; }
        public IReadOnlyList<string> RestoredCharacterIds { get; }
        public bool StatsRandomConsumed { get; }
        public Pcg32State NextStatsRandomState { get; }

        internal ItemEffectResult(bool succeeded, ItemEffectFailure failure,
            BoardState board, HandState hand, CharacterOwnershipState ownership,
            EconomyState economy, ItemEffectState effectState, StageProgressState stage,
            EventItemState eventItem, string consumedItemDefinitionId,
            IEnumerable<ItemEffectCommand> commands, IEnumerable<ItemEffectCommand> expiredCommands,
            IEnumerable<string> restoredCharacterIds, bool statsRandomConsumed,
            Pcg32State nextStatsRandomState)
        {
            Succeeded = succeeded;
            Failure = failure;
            NextBoard = board;
            NextHand = hand;
            NextOwnership = ownership;
            NextEconomy = economy;
            NextEffectState = effectState;
            NextStage = stage;
            NextEventItem = eventItem;
            ConsumedItemDefinitionId = consumedItemDefinitionId;
            Commands = Array.AsReadOnly(new List<ItemEffectCommand>(commands ?? System.Array.Empty<ItemEffectCommand>()).ToArray());
            ExpiredEnemyCommands = Array.AsReadOnly(new List<ItemEffectCommand>(expiredCommands ?? System.Array.Empty<ItemEffectCommand>()).ToArray());
            RestoredCharacterIds = Array.AsReadOnly(new List<string>(restoredCharacterIds ?? System.Array.Empty<string>()).ToArray());
            StatsRandomConsumed = statsRandomConsumed;
            NextStatsRandomState = nextStatsRandomState;
        }
    }
}
