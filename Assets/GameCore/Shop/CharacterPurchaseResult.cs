using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Shop
{
    public enum CharacterPurchaseFailureReason
    {
        None = 0,
        InvalidOffer = 1,
        MissingDefinition = 2,
        CostMismatch = 3,
        InsufficientGold = 4,
        MissingReservation = 5,
        InvalidStatsConfiguration = 6,
        PlacementNotApproved = 7,
        InstanceIdExhausted = 8
    }

    /// <summary>Proposed atomic result; callers apply all Next* values together only after success.</summary>
    public sealed class CharacterPurchaseResult
    {
        public bool Succeeded { get; }
        public CharacterPurchaseFailureReason FailureReason { get; }
        public EconomyState NextEconomy { get; }
        public ShopState NextShop { get; }
        public CharacterPool NextPool { get; }
        public CharacterInstance PurchasedCharacter { get; }
        public CharacterInstanceFactory NextInstanceFactory { get; }
        public Pcg32State NextStatsRandomState { get; }
        public bool PlacementValidationPassed { get; }

        internal CharacterPurchaseResult(bool succeeded, CharacterPurchaseFailureReason failureReason,
            EconomyState nextEconomy, ShopState nextShop, CharacterPool nextPool,
            CharacterInstance purchasedCharacter, CharacterInstanceFactory nextInstanceFactory,
            Pcg32State nextStatsRandomState)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            NextEconomy = nextEconomy;
            NextShop = nextShop;
            NextPool = nextPool;
            PurchasedCharacter = purchasedCharacter;
            NextInstanceFactory = nextInstanceFactory;
            NextStatsRandomState = nextStatsRandomState;
            PlacementValidationPassed = succeeded;
        }
    }
}
