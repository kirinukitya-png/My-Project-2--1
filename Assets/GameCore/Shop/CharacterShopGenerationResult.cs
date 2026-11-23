namespace ProjectNST.GameCore.Shop
{
    /// <summary>Proposed next shop and pool values. A future GameSession applies them as one state change.</summary>
    public sealed class CharacterShopGenerationResult
    {
        public ShopState Shop { get; }
        public CharacterPool Pool { get; }

        public CharacterShopGenerationResult(ShopState shop, CharacterPool pool)
        {
            Shop = shop ?? throw new System.ArgumentNullException(nameof(shop));
            Pool = pool ?? throw new System.ArgumentNullException(nameof(pool));
        }
    }
}
