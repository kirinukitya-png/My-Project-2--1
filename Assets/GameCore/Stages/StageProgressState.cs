using System;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Stages
{
    public enum StageProgressPhase
    {
        AwaitingEncounterStart,
        AwaitingStageChoice,
        Preparation,
        Battle,
        RewardPending,
        EventPending,
        Completed,
        GameOver
    }

    public enum StageChoice { SelectA, SelectB, SelectC, SelectD }
    public enum StageEnemyCategory { Normal, Hard, Boss, None }
    public enum StageShopMode { Character, Item }
    public enum StageSelectionEffectTiming { None, AtSelection }
    public enum StageSelectionEffectKind { None, HealPlayer }

    /// <summary>Describes an effect for a later stage to apply; this type does not apply it.</summary>
    public sealed class StageSelectionEffect
    {
        public StageSelectionEffectKind Kind { get; }
        public StageSelectionEffectTiming Timing { get; }
        public int Amount { get; }
        public bool CapAtInitialMaximum { get; }

        internal StageSelectionEffect(StageSelectionEffectKind kind, StageSelectionEffectTiming timing,
            int amount, bool capAtInitialMaximum)
        {
            Kind = kind;
            Timing = timing;
            Amount = amount;
            CapAtInitialMaximum = capAtInitialMaximum;
        }
    }

    /// <summary>Metadata produced at encounter selection for Stage 7-C and adapters.</summary>
    public sealed class StageSelectionOutcome
    {
        public StageChoice Choice { get; }
        public StageEnemyCategory EnemyCategory { get; }
        public StageShopMode ShopMode { get; }
        public int ExperienceReward { get; }
        public int GoldReward { get; }
        public int ExperiencePurchaseGoldCost { get; }
        public bool CanReroll { get; }
        public StageSelectionEffect SelectionEffect { get; }

        internal StageSelectionOutcome(StageChoice choice, StageEnemyCategory enemyCategory,
            StageShopMode shopMode, int experienceReward, int goldReward,
            int experiencePurchaseGoldCost, bool canReroll, StageSelectionEffect selectionEffect)
        {
            Choice = choice;
            EnemyCategory = enemyCategory;
            ShopMode = shopMode;
            ExperienceReward = experienceReward;
            GoldReward = goldReward;
            ExperiencePurchaseGoldCost = experiencePurchaseGoldCost;
            CanReroll = canReroll;
            SelectionEffect = selectionEffect;
        }
    }

    /// <summary>Information about one victory whose reward must be applied before progression.</summary>
    public sealed class StageRewardPending
    {
        public int EncounterIndex { get; }
        public int StageNumber { get; }
        public int RoundNumber { get; }
        public StageChoice Choice { get; }
        public bool IsBossReward { get; }
        public bool IsFinalVictory { get; }

        internal StageRewardPending(int encounterIndex, int stageNumber, int roundNumber,
            StageChoice choice, bool isBossReward, bool isFinalVictory)
        {
            EncounterIndex = encounterIndex;
            StageNumber = stageNumber;
            RoundNumber = roundNumber;
            Choice = choice;
            IsBossReward = isBossReward;
            IsFinalVictory = isFinalVictory;
        }
    }

    /// <summary>Immutable progression state. Only rules in this assembly can create transitions.</summary>
    public sealed class StageProgressState
    {
        public int EncounterIndex { get; }
        public int EventChancePercent { get; }
        public StageProgressPhase Phase { get; }
        public StageSelectionOutcome CurrentSelection { get; }
        public StageRewardPending PendingReward { get; }
        public bool SelectionEffectApplied { get; }
        public bool IsEventPending => Phase == StageProgressPhase.EventPending;
        public bool IsCompleted => Phase == StageProgressPhase.Completed;
        public bool IsGameOver => Phase == StageProgressPhase.GameOver;

        internal StageProgressState(int encounterIndex, int eventChancePercent,
            StageProgressPhase phase, StageSelectionOutcome currentSelection,
            StageRewardPending pendingReward, bool selectionEffectApplied = false)
        {
            EncounterIndex = encounterIndex;
            EventChancePercent = eventChancePercent;
            Phase = phase;
            CurrentSelection = currentSelection;
            PendingReward = pendingReward;
            SelectionEffectApplied = selectionEffectApplied;
        }

        internal StageProgressState With(int encounterIndex, int eventChancePercent,
            StageProgressPhase phase, StageSelectionOutcome currentSelection,
            StageRewardPending pendingReward)
        {
            return new StageProgressState(encounterIndex, eventChancePercent, phase,
                currentSelection, pendingReward, SelectionEffectApplied);
        }

        internal StageProgressState With(int encounterIndex, int eventChancePercent,
            StageProgressPhase phase, StageSelectionOutcome currentSelection,
            StageRewardPending pendingReward, bool selectionEffectApplied)
        {
            return new StageProgressState(encounterIndex, eventChancePercent, phase,
                currentSelection, pendingReward, selectionEffectApplied);
        }
    }

    public enum StageProgressFailure
    {
        None,
        InvalidState,
        InvalidChoice,
        ChoiceNotAllowed,
        NotAwaitingBattle,
        NotAwaitingReward,
        NotAwaitingEvent,
        EventItemTargetStillAvailable,
        NoEnemyEncounterRequired
    }

    /// <summary>Immutable result returned by progression and selection rules.</summary>
    public sealed class StageProgressResult
    {
        public bool Succeeded { get; }
        public StageProgressFailure Failure { get; }
        public StageProgressState NextState { get; }
        public StageSelectionOutcome Selection { get; }
        public StageRewardPending Reward { get; }
        public bool EventStarted { get; }
        public bool HasNextEventRandomState { get; }
        public bool EventRandomConsumed { get; }
        public Pcg32State NextEventRandomState { get; }

        internal StageProgressResult(bool succeeded, StageProgressFailure failure,
            StageProgressState nextState, StageSelectionOutcome selection,
            StageRewardPending reward, bool eventStarted, bool hasNextEventRandomState,
            bool eventRandomConsumed, Pcg32State nextEventRandomState)
        {
            Succeeded = succeeded;
            Failure = failure;
            NextState = nextState;
            Selection = selection;
            Reward = reward;
            EventStarted = eventStarted;
            HasNextEventRandomState = hasNextEventRandomState;
            EventRandomConsumed = eventRandomConsumed;
            NextEventRandomState = nextEventRandomState;
        }
    }
}
