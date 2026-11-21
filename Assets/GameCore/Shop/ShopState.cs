using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Shop
{
    public enum ShopKind
    {
        None = 0,
        Character = 1,
        Item = 2
    }

    /// <summary>Immutable shop snapshot. State transitions are owned by the future GameSession.</summary>
    public sealed class ShopState
    {
        private readonly ShopOffer[] _offers;

        public ShopKind Kind { get; }
        public IReadOnlyList<ShopOffer> Offers { get; }
        public int SlotCount { get; }
        public int StageNumber { get; }
        public int RoundNumber { get; }
        public bool IsEventStage { get; }

        public ShopState(ShopKind kind, IEnumerable<ShopOffer> offers, int slotCount,
            int stageNumber, int roundNumber, bool isEventStage = false)
        {
            if (!Enum.IsDefined(typeof(ShopKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (slotCount < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            if (stageNumber < 1) throw new ArgumentOutOfRangeException(nameof(stageNumber));
            if (roundNumber < 1) throw new ArgumentOutOfRangeException(nameof(roundNumber));

            _offers = CopyAndValidateOffers(kind, offers, slotCount);
            Kind = kind;
            Offers = Array.AsReadOnly(_offers);
            SlotCount = slotCount;
            StageNumber = stageNumber;
            RoundNumber = roundNumber;
            IsEventStage = isEventStage;
        }

        private static ShopOffer[] CopyAndValidateOffers(ShopKind kind,
            IEnumerable<ShopOffer> offers, int slotCount)
        {
            var copied = new List<ShopOffer>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (offers != null)
            {
                foreach (ShopOffer offer in offers)
                {
                    if (offer == null) throw new ArgumentException("Shop offers cannot contain null entries.", nameof(offers));
                    if (kind == ShopKind.None)
                        throw new ArgumentException("A shop with Kind.None cannot contain offers.", nameof(offers));
                    if (kind == ShopKind.Character && offer.Kind != ShopOfferKind.Character ||
                        kind == ShopKind.Item && offer.Kind != ShopOfferKind.Item)
                        throw new ArgumentException("Offer kind does not match the shop kind.", nameof(offers));
                    if (!ids.Add(offer.OfferId))
                        throw new ArgumentException("OfferId values must be unique within a ShopState.", nameof(offers));
                    copied.Add(offer);
                }
            }
            if (copied.Count > slotCount)
                throw new ArgumentException("Offer count cannot exceed the shop slot count.", nameof(offers));
            return copied.ToArray();
        }
    }
}
