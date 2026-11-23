using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleFinishFailure
    {
        None, InvalidInput, AlreadyFinished, PendingAction, PendingEffects,
        OutcomeCheckNotRequested, OutcomeNotReached, InvalidState,
        CleanupOverflow, ItemEffectCleanupFailed
    }

    /// <summary>Atomic proposal for finishing a battle and cleaning battle-only state.</summary>
    public sealed class BattleFinishResult
    {
        public bool Succeeded { get; }
        public BattleFinishFailure Failure { get; }
        public BattleResult Result { get; }
        public BattleState NextBattleState { get; }
        public EconomyState NextEconomyState { get; }
        public BoardState NextBoard { get; }
        public HandState NextHand { get; }
        public CharacterOwnershipState NextOwnership { get; }
        public ItemEffectState NextItemEffectState { get; }
        public Pcg32State NextBattleRandomState { get; }

        internal BattleFinishResult(bool succeeded, BattleFinishFailure failure,
            BattleResult result, BattleState battle, EconomyState economy,
            BoardState board, HandState hand, CharacterOwnershipState ownership,
            ItemEffectState itemEffectState, Pcg32State randomState)
        {
            Succeeded = succeeded;
            Failure = failure;
            Result = result;
            NextBattleState = battle;
            NextEconomyState = economy;
            NextBoard = board;
            NextHand = hand;
            NextOwnership = ownership;
            NextItemEffectState = itemEffectState;
            NextBattleRandomState = randomState;
        }
    }
}
