using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public sealed class BattleInitializationResult
    {
        public bool Succeeded { get; }
        public BattleStartFailure Failure { get; }
        public BattleState State { get; }
        public Pcg32State NextBattleRandomState { get; }

        internal BattleInitializationResult(bool succeeded, BattleStartFailure failure,
            BattleState state, Pcg32State nextBattleRandomState)
        {
            Succeeded = succeeded;
            Failure = failure;
            State = state;
            NextBattleRandomState = nextBattleRandomState;
        }
    }
}
