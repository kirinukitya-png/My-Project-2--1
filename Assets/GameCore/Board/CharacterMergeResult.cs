using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Board
{
    public enum CharacterMergeFailureReason
    {
        None = 0,
        InvalidInputState,
        InvalidDefinitionId,
        InvalidStar,
        MaximumStarReached,
        InsufficientCandidates,
        InvalidStatsDefinition,
        InvalidRandomState,
        StatsRerollFailed,
        InvalidResultState
    }

    /// <summary>Immutable proposed result of one three-copy character upgrade.</summary>
    public sealed class CharacterMergeResult
    {
        public bool Succeeded { get; }
        public CharacterMergeFailureReason FailureReason { get; }
        public BoardState NextBoard { get; }
        public HandState NextHand { get; }
        public CharacterOwnershipState NextOwnership { get; }
        public string SurvivingInstanceId { get; }
        public IReadOnlyList<string> ConsumedInstanceIds { get; }
        public int StarBefore { get; }
        public int StarAfter { get; }
        public Pcg32State NextStatsRandomState { get; }

        internal CharacterMergeResult(bool succeeded, CharacterMergeFailureReason failureReason,
            BoardState nextBoard, HandState nextHand, CharacterOwnershipState nextOwnership,
            string survivingInstanceId, IEnumerable<string> consumedInstanceIds,
            int starBefore, int starAfter, Pcg32State nextStatsRandomState)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            NextBoard = nextBoard;
            NextHand = nextHand;
            NextOwnership = nextOwnership;
            SurvivingInstanceId = survivingInstanceId;
            ConsumedInstanceIds = Array.AsReadOnly(new List<string>(consumedInstanceIds).ToArray());
            StarBefore = starBefore;
            StarAfter = starAfter;
            NextStatsRandomState = nextStatsRandomState;
        }
    }
}
