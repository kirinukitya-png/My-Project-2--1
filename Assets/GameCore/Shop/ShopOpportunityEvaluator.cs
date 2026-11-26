using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Shop
{
    public enum ShopOpportunityStatus
    {
        Evaluated, ItemShopUnsupported, RerollUnavailable, InsufficientGold,
        PublicConfigurationUnavailable, DistributionUnavailable, CurrentOfferUnavailable
    }

    public enum ShopPurchaseOpportunityStatus
    {
        Positive, NonPositive, InsufficientGold, NoReachablePlacement, Unavailable
    }

    public sealed class ShopOpportunitySettings
    {
        public EconomyValueSettings Economy { get; }
        public decimal CompositionWeight { get; }
        public decimal FutureDiscount { get; }

        public ShopOpportunitySettings(EconomyValueSettings economy, decimal compositionWeight = 8m,
            decimal futureDiscount = .70m)
        {
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            if (compositionWeight < 0) throw new ArgumentOutOfRangeException(nameof(compositionWeight));
            if (futureDiscount < 0 || futureDiscount > 1) throw new ArgumentOutOfRangeException(nameof(futureDiscount));
            CompositionWeight = compositionWeight;
            FutureDiscount = futureDiscount;
        }
    }

    public sealed class ShopPurchaseOpportunity
    {
        public string OfferId { get; }
        public string DefinitionId { get; }
        public int Cost { get; }
        public CharacterLocation? Placement { get; }
        public ShopPurchaseOpportunityStatus Status { get; }
        public string UnavailableReason { get; }
        public decimal Benefit { get; }
        public decimal Probability { get; }

        internal ShopPurchaseOpportunity(string offerId, string definitionId, int cost,
            ShopPurchaseOpportunityStatus status, decimal benefit, CharacterLocation? placement,
            decimal probability, string unavailableReason)
        {
            OfferId = offerId;
            DefinitionId = definitionId;
            Cost = cost;
            Status = status;
            Benefit = benefit;
            Placement = placement;
            Probability = probability;
            UnavailableReason = unavailableReason;
        }
    }

    public readonly struct ShopOpportunityMass
    {
        public decimal Positive { get; }
        public decimal NonPositive { get; }
        public decimal InsufficientGold { get; }
        public decimal NoReachablePlacement { get; }
        public decimal Unavailable { get; }

        internal ShopOpportunityMass(decimal[] mass)
        {
            Positive = mass[(int)ShopPurchaseOpportunityStatus.Positive];
            NonPositive = mass[(int)ShopPurchaseOpportunityStatus.NonPositive];
            InsufficientGold = mass[(int)ShopPurchaseOpportunityStatus.InsufficientGold];
            NoReachablePlacement = mass[(int)ShopPurchaseOpportunityStatus.NoReachablePlacement];
            Unavailable = mass[(int)ShopPurchaseOpportunityStatus.Unavailable];
        }
    }

    public sealed class ShopOpportunityResult
    {
        public ShopOpportunityStatus Status { get; }
        public string UnavailableReason { get; }
        public ObservationShopKind? ShopKind { get; }
        public int BeforeGold { get; }
        public int? RerollPrice { get; }
        public int? RemainingGold { get; }
        public ShopPurchaseOpportunity BestCurrent { get; }
        public decimal? CurrentOpportunity { get; }
        public decimal? ExpectedFutureOpportunity { get; }
        public decimal FutureDiscount { get; }
        public decimal? DiscountedFutureOpportunity => FutureDiscount * ExpectedFutureOpportunity;
        public EconomyValueComparison? RerollEconomy { get; }
        public decimal? Score { get; }
        /// <summary>Each future slot is approximated as an independent draw from public marginal probabilities.</summary>
        public bool UsesIndependentSlotApproximation => ExpectedFutureOpportunity.HasValue && Distribution != null;
        public ShopCostDistribution Distribution { get; }
        public ShopOpportunityMass? FutureMass { get; }
        public int HypotheticalPreviewCount { get; }
        public IReadOnlyList<ShopPurchaseOpportunity> Details { get; }

        internal ShopOpportunityResult(ShopOpportunityStatus status, string reason, ObservationShopKind? shopKind,
            int gold, int? price, int? remaining, decimal discount, EconomyValueComparison? economy,
            ShopPurchaseOpportunity best, decimal? current, decimal? future, ShopCostDistribution distribution,
            ShopOpportunityMass? futureMass, int previews, List<ShopPurchaseOpportunity> details)
        {
            Status = status;
            UnavailableReason = reason;
            ShopKind = shopKind;
            BeforeGold = gold;
            RerollPrice = price;
            RemainingGold = remaining;
            FutureDiscount = discount;
            RerollEconomy = economy;
            BestCurrent = best;
            CurrentOpportunity = current;
            ExpectedFutureOpportunity = future;
            Distribution = distribution;
            FutureMass = futureMass;
            HypotheticalPreviewCount = previews;
            Details = details == null ? Array.Empty<ShopPurchaseOpportunity>() : details.AsReadOnly();
            if (status == ShopOpportunityStatus.Evaluated)
                Score = ShopOpportunityMath.Score(future.Value, current.Value, economy.Value.TotalDelta, discount);
        }
    }

    public sealed class ShopOpportunityEvaluator
    {
        public ShopOpportunitySettings Settings { get; }

        public ShopOpportunityEvaluator(ShopOpportunitySettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Evaluates one public decision. A score does not guarantee that the hidden Pool can fill the Shop.</summary>
        public ShopOpportunityResult Evaluate(GameObservation observation, bool includeDetails = false)
            => EvaluateShared(observation, null, null, null, null, includeDetails);

        internal ShopOpportunityResult EvaluateShared(GameObservation observation, CompositionValueEvaluator sharedComposition,
            EconomyValueEvaluator sharedEconomy, EconomyValueEvaluator.Baseline sharedBefore,
            Func<BuyCharacterAction, PurchaseCompositionEvaluation> currentPurchase, bool includeDetails = false)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            var shop = observation.Shop;
            int gold = observation.Player.Gold;
            int? price = observation.EconomyRules?.ShopRerollGoldCost;
            int? remaining = null;
            var details = includeDetails ? new List<ShopPurchaseOpportunity>() : null;
            EconomyValueComparison? rerollEconomy = null;
            ShopPurchaseOpportunity best = null;
            decimal? current = null;
            decimal? future = null;
            ShopCostDistribution distribution = null;
            ShopOpportunityMass? futureMass = null;
            int previews = 0;

            ShopOpportunityResult Result(ShopOpportunityStatus status, string reason = null) =>
                new ShopOpportunityResult(status, reason, shop?.Kind, gold, price, remaining, Settings.FutureDiscount,
                    rerollEconomy, best, current, future, distribution, futureMass, previews, details);

            if (shop?.Kind == ObservationShopKind.Item)
                return Result(ShopOpportunityStatus.ItemShopUnsupported, "Item purchase opportunity has no Character valuation.");
            if (shop?.Kind != ObservationShopKind.Character || !observation.IsStrategyActionAllowed ||
                observation.Stage.Phase != ObservationPhase.Preparation || !observation.Stage.HasSelection ||
                observation.Stage.ShopMode != StageShopMode.Character || !observation.Stage.CanReroll)
                return Result(ShopOpportunityStatus.RerollUnavailable, "The public stage or Shop does not allow Character reroll.");
            if (!price.HasValue || shop.CostWeights == null)
                return Result(ShopOpportunityStatus.PublicConfigurationUnavailable, "Reroll price or public Cost settings are missing.");
            if (gold < 0 || price.Value < 0 || shop.SlotCount < 0)
                throw new ArgumentOutOfRangeException(nameof(observation), "Gold, reroll price and slot count must be nonnegative.");
            if (gold < price.Value)
                return Result(ShopOpportunityStatus.InsufficientGold, "The reroll price is unaffordable.");

            remaining = checked(gold - price.Value);
            var economy = sharedEconomy ?? new EconomyValueEvaluator(Settings.Economy, EconomyInterestObservation.FromObservation(observation));
            var beforeGold = sharedBefore ?? economy.CreateBaseline(gold);
            var afterGold = economy.CreateBaseline(remaining.Value);
            rerollEconomy = beforeGold.Compare(remaining.Value);
            var weights = shop.CostWeights;
            var visibleOffers = shop.Offers.Where(o => o.Kind == ShopOfferKind.Character).ToArray();
            distribution = ShopCostDistribution.Estimate(observation.SelectedRoster.Select(d => d.DefinitionId),
                weights.CharactersPerCost,
                new[] { weights.Cost1Weight, weights.Cost2Weight, weights.Cost3Weight, weights.Cost4Weight, weights.Cost5Weight },
                observation.Characters.Select(c => (c.DefinitionId, c.RunCost)).Concat(
                    visibleOffers.Select(o => (o.DefinitionId, o.CharacterCost.GetValueOrDefault()))));
            if (distribution.UnavailableReason != null)
                return Result(ShopOpportunityStatus.DistributionUnavailable, distribution.UnavailableReason);

            var roster = observation.SelectedRoster.ToDictionary(d => d.DefinitionId, StringComparer.Ordinal);
            var boardIds = new HashSet<string>(observation.Board.Slots.Where(s => s.IsOccupied)
                .Select(s => s.Character.DefinitionId), StringComparer.Ordinal);
            var boardSlots = observation.Board.Slots.Where(s => !s.IsOccupied).OrderBy(s => s.SlotIndex)
                .Select(s => new CharacterLocation(CharacterLocationKind.Board, s.SlotIndex)).ToArray();
            // All empty Hand slots are equivalent to the formation evaluator and merge ignores Hand destination.
            var handSlot = observation.Hand.Slots.Where(s => s.Kind == HandSlotContentKind.Empty)
                .OrderBy(s => s.SlotIndex).FirstOrDefault();
            var formationSettings = HeuristicStrategy.CreateV13Settings();
            var composition = sharedComposition ?? new CompositionValueEvaluator(observation, formationSettings.CompositionHandReserveWeight,
                formationSettings, formationSettings.CompositionHandDuplicateWeight);
            var formationCache = new Dictionary<(string, int, CharacterLocationKind, int), PurchaseCompositionEvaluation>();
            var fundsCache = new Dictionary<(int, int), decimal>();

            ShopPurchaseOpportunity EvaluateOffer(string offerId, string id, int cost, int offerPrice,
                EconomyValueEvaluator.Baseline funds, decimal probability)
            {
                ShopPurchaseOpportunity Entry(ShopPurchaseOpportunityStatus status, decimal benefit = 0m,
                    CharacterLocation? placement = null, string reason = null) =>
                    new ShopPurchaseOpportunity(offerId, id, cost, status, benefit, placement, probability, reason);
                if (offerPrice != cost)
                    return Entry(ShopPurchaseOpportunityStatus.Unavailable, reason: "Character price does not equal its public RunCost.");
                if (funds.Before.Gold < offerPrice) return Entry(ShopPurchaseOpportunityStatus.InsufficientGold);

                IEnumerable<CharacterLocation> placements = boardIds.Contains(id)
                    ? Array.Empty<CharacterLocation>() : boardSlots;
                if (handSlot != null)
                    placements = placements.Append(new CharacterLocation(CharacterLocationKind.Hand, handSlot.SlotIndex));

                CharacterLocation? bestPlacement = null;
                decimal bestBenefit = 0m;
                bool reachable = false;
                string unavailableReason = null;
                foreach (var placement in placements)
                {
                    var key = (id, cost, placement.Kind, placement.SlotIndex);
                    if (!formationCache.TryGetValue(key, out var formation))
                    {
                        if (offerId == null)
                        {
                            previews++;
                            formation = composition.CompareHypotheticalPurchase(roster[id], cost, offerPrice,
                                funds.Before.Gold, placement);
                        }
                        else
                        {
                            var action = new BuyCharacterAction(new ActionId("shop-opportunity"), offerId,
                                new GameActionLocation(placement.Kind, placement.SlotIndex));
                            formation = currentPurchase == null ? composition.ComparePurchase(action) : currentPurchase(action);
                        }
                        formationCache.Add(key, formation);
                    }
                    var preview = formation.Preview;
                    if (!preview.Succeeded)
                    {
                        if (preview.Failure != CharacterPurchasePreviewFailure.SlotOccupied &&
                            preview.Failure != CharacterPurchasePreviewFailure.DuplicateBoardDefinition)
                            unavailableReason = "PreviewUnavailable: " + preview.Failure;
                        continue;
                    }
                    if (preview.After.OverLevelCount > preview.Before.OverLevelCount) continue;
                    reachable = true;
                    if (!formation.TotalDelta.HasValue)
                    {
                        unavailableReason = formation.Failure.ToString();
                        continue;
                    }
                    var fundsKey = (funds.Before.Gold, offerPrice);
                    if (!fundsCache.TryGetValue(fundsKey, out decimal purchaseDelta))
                    {
                        purchaseDelta = funds.CompareGoldChange(-offerPrice).TotalDelta;
                        fundsCache.Add(fundsKey, purchaseDelta);
                    }
                    decimal benefit = Settings.CompositionWeight * (decimal)formation.TotalDelta.Value + purchaseDelta;
                    if (benefit > bestBenefit)
                    {
                        bestBenefit = benefit;
                        bestPlacement = placement;
                    }
                }
                if (unavailableReason != null)
                    return Entry(ShopPurchaseOpportunityStatus.Unavailable, reason: unavailableReason);
                if (!reachable) return Entry(ShopPurchaseOpportunityStatus.NoReachablePlacement);
                return bestPlacement.HasValue ? Entry(ShopPurchaseOpportunityStatus.Positive, bestBenefit, bestPlacement)
                    : Entry(ShopPurchaseOpportunityStatus.NonPositive);
            }

            string currentFailure = null;
            foreach (var offer in visibleOffers.Where(o => o.Status == ShopOfferStatus.Available)
                .OrderBy(o => o.OfferId, StringComparer.Ordinal))
            {
                var candidate = EvaluateOffer(offer.OfferId, offer.DefinitionId, offer.CharacterCost.Value,
                    offer.DisplayPrice, beforeGold, 0m);
                details?.Add(candidate);
                if (candidate.Status == ShopPurchaseOpportunityStatus.Unavailable)
                    currentFailure ??= offer.OfferId + ": " + candidate.UnavailableReason;
                if (candidate.Benefit > (best?.Benefit ?? 0m)) best = candidate;
            }
            if (currentFailure != null) return Result(ShopOpportunityStatus.CurrentOfferUnavailable, currentFailure);
            current = best?.Benefit ?? 0m;

            var outcomes = new List<(decimal Benefit, decimal Probability)>(distribution.Outcomes.Count);
            var mass = new decimal[5];
            if (shop.SlotCount > 0)
            {
                foreach (var outcome in distribution.Outcomes)
                {
                    var candidate = EvaluateOffer(null, outcome.DefinitionId, outcome.Cost, outcome.Cost,
                        afterGold, outcome.SlotProbability);
                    outcomes.Add((candidate.Benefit, candidate.Probability));
                    mass[(int)candidate.Status] += candidate.Probability;
                    details?.Add(candidate);
                }
                future = ShopOpportunityMath.ExpectedMaximum(outcomes, shop.SlotCount);
            }
            else future = 0m;
            futureMass = new ShopOpportunityMass(mass);
            return Result(ShopOpportunityStatus.Evaluated);
        }
    }
}
