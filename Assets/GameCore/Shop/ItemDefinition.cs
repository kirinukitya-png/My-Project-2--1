using System;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>Unity-independent item offer metadata. Item effects are intentionally out of scope.</summary>
    public sealed class ItemDefinition
    {
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int PurchasePrice { get; }

        public ItemDefinition(string definitionId, string displayName, int purchasePrice)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("A non-empty item DefinitionId is required.", nameof(definitionId));
            if (purchasePrice < 0)
                throw new ArgumentOutOfRangeException(nameof(purchasePrice), "Purchase price cannot be negative.");

            DefinitionId = definitionId;
            DisplayName = displayName ?? string.Empty;
            PurchasePrice = purchasePrice;
        }
    }
}
