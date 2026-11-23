using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Shop
{
    public enum BoardShopTransactionKind
    {
        None = 0,
        NormalPurchase = 1,
        MergePurchase = 2,
        Sale = 3
    }

    public enum BoardShopTransactionFailureReason
    {
        None = 0,
        InvalidInputState,
        InvalidOffer,
        MissingDefinition,
        CostMismatch,
        InsufficientGold,
        MissingReservation,
        InvalidStatsConfiguration,
        InvalidPlacement,
        PlacementOccupied,
        DuplicateDefinitionOnBoard,
        InstanceIdExhausted,
        PoolOwnershipMismatch,
        StatsRerollFailed,
        MergeValidationFailed,
        NotOwned,
        AmbiguousOwnedId,
        PoolEntryMissing,
        InsufficientOwnedCopies,
        GoldOverflow,
        InvalidRandomState,
        TemporaryStarCannotBeSold
    }

    public sealed class CharacterMergeStageSummary
    {
        public int StarBefore { get; }
        public int StarAfter { get; }
        public string SurvivingInstanceId { get; }
        public IReadOnlyList<string> ConsumedInstanceIds { get; }
        public bool ConsumedPurchasedCopy { get; }

        internal CharacterMergeStageSummary(int starBefore, int starAfter, string survivor,
            IEnumerable<string> consumedIds, bool consumedPurchasedCopy)
        {
            StarBefore = starBefore;
            StarAfter = starAfter;
            SurvivingInstanceId = survivor;
            ConsumedInstanceIds = Array.AsReadOnly(new List<string>(consumedIds ?? Array.Empty<string>()).ToArray());
            ConsumedPurchasedCopy = consumedPurchasedCopy;
        }
    }

    /// <summary>Atomic proposal for one shop purchase or character sale across all related states.</summary>
    public sealed class BoardShopTransactionResult
    {
        public bool Succeeded { get; }
        public BoardShopTransactionFailureReason FailureReason { get; }
        public BoardShopTransactionKind TransactionKind { get; }
        public EconomyState NextEconomy { get; }
        public ShopState NextShop { get; }
        public CharacterPool NextPool { get; }
        public BoardState NextBoard { get; }
        public HandState NextHand { get; }
        public CharacterOwnershipState NextOwnership { get; }
        public CharacterInstanceFactory NextInstanceFactory { get; }
        public Pcg32State NextStatsRandomState { get; }
        public int GoldDelta { get; }
        /// <summary>Gross count of copies transferred between pool buckets by this transaction.</summary>
        public int PoolCopiesDelta { get; }
        /// <summary>Signed per-bucket deltas; these three values conserve the pool total.</summary>
        public int PoolRemainingCopiesDelta { get; }
        public int PoolReservedCopiesDelta { get; }
        public int PoolOwnedCopiesDelta { get; }
        public bool PoolOwnershipConsistencyVerified { get; }
        public int MergeStageCount { get; }
        public IReadOnlyList<CharacterMergeStageSummary> MergeStages { get; }
        public string FinalSurvivingInstanceId { get; }

        internal BoardShopTransactionResult(bool succeeded, BoardShopTransactionFailureReason failureReason,
            BoardShopTransactionKind transactionKind, EconomyState economy, ShopState shop, CharacterPool pool,
            BoardState board, HandState hand, CharacterOwnershipState ownership, CharacterInstanceFactory factory,
            Pcg32State statsRandomState, int goldDelta, int poolCopiesDelta,
            int poolRemainingCopiesDelta, int poolReservedCopiesDelta, int poolOwnedCopiesDelta,
            bool poolOwnershipConsistencyVerified, IEnumerable<CharacterMergeStageSummary> mergeStages, string finalSurvivor)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            TransactionKind = transactionKind;
            NextEconomy = economy;
            NextShop = shop;
            NextPool = pool;
            NextBoard = board;
            NextHand = hand;
            NextOwnership = ownership;
            NextInstanceFactory = factory;
            NextStatsRandomState = statsRandomState;
            GoldDelta = goldDelta;
            PoolCopiesDelta = poolCopiesDelta;
            PoolRemainingCopiesDelta = poolRemainingCopiesDelta;
            PoolReservedCopiesDelta = poolReservedCopiesDelta;
            PoolOwnedCopiesDelta = poolOwnedCopiesDelta;
            PoolOwnershipConsistencyVerified = poolOwnershipConsistencyVerified;
            var stages = new List<CharacterMergeStageSummary>(mergeStages ?? Array.Empty<CharacterMergeStageSummary>());
            MergeStages = Array.AsReadOnly(stages.ToArray());
            MergeStageCount = stages.Count;
            FinalSurvivingInstanceId = finalSurvivor;
        }
    }
}
