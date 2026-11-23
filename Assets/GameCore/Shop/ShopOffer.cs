using System;

namespace ProjectNST.GameCore.Shop
{
    public enum ShopOfferKind
    {
        Character = 0,
        Item = 1,
        Empty = 2
    }

    public enum ShopOfferStatus
    {
        Available = 0,
        Purchased = 1
    }

    /// <summary>An immutable candidate shown in the shop. It does not create a CharacterInstance.</summary>
    public sealed class ShopOffer
    {
        public string OfferId { get; }
        public ShopOfferKind Kind { get; }
        public string DefinitionId { get; }
        public int DisplayPrice { get; }
        public int? CharacterCost { get; }
        public ShopOfferStatus Status { get; }
        public bool IsEmpty => Kind == ShopOfferKind.Empty;

        public ShopOffer(string offerId, ShopOfferKind kind, string definitionId,
            int displayPrice, int? characterCost, ShopOfferStatus status = ShopOfferStatus.Available)
        {
            if (string.IsNullOrWhiteSpace(offerId))
                throw new ArgumentException("A non-empty OfferId is required.", nameof(offerId));
            if (!Enum.IsDefined(typeof(ShopOfferKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind != ShopOfferKind.Empty && string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("A non-empty DefinitionId is required.", nameof(definitionId));
            if (displayPrice < 0)
                throw new ArgumentOutOfRangeException(nameof(displayPrice));
            if (!Enum.IsDefined(typeof(ShopOfferStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));

            if (kind == ShopOfferKind.Character)
            {
                if (!characterCost.HasValue || characterCost.Value < 1)
                    throw new ArgumentOutOfRangeException(nameof(characterCost), "Character offers require a positive cost tier.");
            }
            else if (kind == ShopOfferKind.Item && characterCost.HasValue)
            {
                throw new ArgumentException("Item offers do not have a character cost tier.", nameof(characterCost));
            }
            else if (kind == ShopOfferKind.Empty && (displayPrice != 0 || characterCost.HasValue || !string.IsNullOrEmpty(definitionId) || status != ShopOfferStatus.Available))
            {
                throw new ArgumentException("An empty offer has no definition, price, cost tier, or purchased state.");
            }

            OfferId = offerId;
            Kind = kind;
            DefinitionId = definitionId;
            DisplayPrice = displayPrice;
            CharacterCost = characterCost;
            Status = status;
        }

        public static ShopOffer CreateEmpty(string offerId)
        {
            return new ShopOffer(offerId, ShopOfferKind.Empty, string.Empty, 0, null);
        }
    }
}
