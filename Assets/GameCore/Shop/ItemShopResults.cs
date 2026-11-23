using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>Next item shop, character pool, and Shop RNG checkpoint to apply together.</summary>
    public sealed class ItemShopGenerationResult
    {
        public ShopState NextShop { get; }
        public CharacterPool NextCharacterPool { get; }
        public Pcg32State NextShopRandomState { get; }

        internal ItemShopGenerationResult(ShopState nextShop, CharacterPool nextCharacterPool, Pcg32State nextShopRandomState)
        {
            NextShop = nextShop;
            NextCharacterPool = nextCharacterPool;
            NextShopRandomState = nextShopRandomState;
        }
    }

    public enum ItemPurchaseFailureReason
    {
        None = 0,
        InvalidOffer = 1,
        MissingDefinition = 2,
        PriceMismatch = 3,
        InsufficientGold = 4
    }

    /// <summary>One acquired item value and next Shop/Economy values; item effects are out of scope.</summary>
    public sealed class ItemPurchaseResult
    {
        public bool Succeeded { get; }
        public ItemPurchaseFailureReason FailureReason { get; }
        public EconomyState NextEconomy { get; }
        public ShopState NextShop { get; }
        public ItemDefinition PurchasedItem { get; }

        internal ItemPurchaseResult(bool succeeded, ItemPurchaseFailureReason failureReason,
            EconomyState nextEconomy, ShopState nextShop, ItemDefinition purchasedItem)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            NextEconomy = nextEconomy;
            NextShop = nextShop;
            PurchasedItem = purchasedItem;
        }
    }
}
