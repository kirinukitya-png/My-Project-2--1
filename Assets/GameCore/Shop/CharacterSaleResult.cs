using System.Collections.Generic;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;

namespace ProjectNST.GameCore.Shop
{
    public enum CharacterSaleFailureReason
    {
        None = 0,
        InvalidTarget = 1,
        NotOwned = 2,
        AmbiguousOwnedId = 3,
        PoolEntryMissing = 4,
        InsufficientOwnedCopies = 5,
        GoldOverflow = 6
    }

    /// <summary>Proposed owner, pool and economy values for one validated sale.</summary>
    public sealed class CharacterSaleResult
    {
        public bool Succeeded { get; }
        public CharacterSaleFailureReason FailureReason { get; }
        public EconomyState NextEconomy { get; }
        public CharacterPool NextPool { get; }
        public IReadOnlyList<CharacterInstance> NextOwnedCharacters { get; }
        public int ReturnedCopies { get; }
        public int GoldGained { get; }

        internal CharacterSaleResult(bool succeeded, CharacterSaleFailureReason failureReason,
            EconomyState nextEconomy, CharacterPool nextPool, IReadOnlyList<CharacterInstance> nextOwnedCharacters,
            int returnedCopies, int goldGained)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            NextEconomy = nextEconomy;
            NextPool = nextPool;
            NextOwnedCharacters = nextOwnedCharacters;
            ReturnedCopies = returnedCopies;
            GoldGained = goldGained;
        }
    }
}
