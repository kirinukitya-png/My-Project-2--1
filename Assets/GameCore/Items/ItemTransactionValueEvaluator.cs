using System;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Items
{
    public enum SpaceDemand { ConfirmedDemand, NoDemand, UnknownDemand }
    public enum ItemTransactionStatus { Evaluated, Illegal, LegalityUnknown, DifferentObservation, EffectUnavailable, InventoryUnavailable }
    public enum ItemCandidateExclusion
    {
        None, TransactionUnavailable, NoActionSlots, RequiresTwoActions, NonPositiveEffect,
        ThresholdNotExceeded, HandNotFull, NoSpaceDemand, UnknownSpaceDemand, NoLegalUseTarget, UnconfirmedBestTarget
    }

    public sealed class ItemTransactionValueSettings
    {
        public ItemInventoryValueSettings Inventory { get; }
        public IntegratedItemEffectSettings Effects { get; }
        public EconomyValueSettings Economy { get; }
        public decimal ImmediateUseDiscount { get; }

        public ItemTransactionValueSettings(ItemInventoryValueSettings inventory = null,
            IntegratedItemEffectSettings effects = null, EconomyValueSettings economy = null, decimal immediateUseDiscount = .90m)
        {
            if (immediateUseDiscount < 0m || immediateUseDiscount > 1m)
                throw new ArgumentOutOfRangeException(nameof(immediateUseDiscount));
            Inventory = inventory ?? new ItemInventoryValueSettings();
            Effects = effects ?? new IntegratedItemEffectSettings();
            Economy = economy ?? new HeuristicV14Settings().Economy;
            ImmediateUseDiscount = immediateUseDiscount;
        }
    }

    public sealed class ItemTransactionValue
    {
        public ItemTransactionStatus Status { get; }
        public ItemPreviewLegality Legality { get; }
        public ItemPreviewFailure Failure { get; }
        public ItemCandidateExclusion Exclusion { get; }
        public bool IsCandidate => Exclusion == ItemCandidateExclusion.None;
        public decimal Threshold { get; }
        public bool ExceedsThreshold => Score > Threshold;
        public ItemFutureWindow FutureWindow { get; }
        public SpaceDemand? Demand { get; }
        public ItemHoldingValue Item { get; }
        public ItemPreviewResult PurchasePreview { get; }
        public IntegratedItemEffectResult Effect { get; }
        public ItemInventoryValue Before { get; }
        public ItemInventoryValue After { get; }
        public int? HandOccupiedBefore { get; }
        public int? HandOccupiedAfter { get; }
        public int? OccupancyDelta => HandOccupiedAfter - HandOccupiedBefore;
        public decimal? HoldingDelta => After?.Value - Before?.Value;
        public decimal? CapacityBefore { get; }
        public decimal? CapacityAfter { get; }
        public decimal? CapacityDelta => CapacityAfter - CapacityBefore;
        public EconomyValueComparison? Economy { get; }
        public decimal? EconomyDelta => Economy?.TotalDelta;
        public int? GoldDelta => Economy.HasValue ? Economy.Value.After.Gold - Economy.Value.Before.Gold : (int?)null;
        public decimal? EffectValue => Effect != null ? Effect.EffectValue : Before == null ? (decimal?)null : 0m;
        public decimal? Score => Status == ItemTransactionStatus.Evaluated
            ? EffectValue + HoldingDelta + CapacityDelta + EconomyDelta : null;

        internal ItemTransactionValue(ItemTransactionStatus status, ItemPreviewLegality legality, decimal threshold,
            ItemFutureWindow futureWindow, ItemPreviewFailure failure = ItemPreviewFailure.None,
            ItemCandidateExclusion exclusion = ItemCandidateExclusion.TransactionUnavailable,
            ItemHoldingValue item = null, ItemInventoryValue before = null, ItemInventoryValue after = null,
            EconomyValueComparison? economy = null, int? occupiedBefore = null, int? occupiedAfter = null,
            int handSlots = 0, IntegratedItemEffectResult effect = null, ItemPreviewResult purchase = null,
            SpaceDemand? demand = null)
        {
            Status = status;
            Legality = legality;
            Threshold = threshold;
            FutureWindow = futureWindow;
            Failure = failure;
            Item = item;
            Before = before;
            After = after;
            Economy = economy;
            HandOccupiedBefore = occupiedBefore;
            HandOccupiedAfter = occupiedAfter;
            CapacityBefore = Capacity(handSlots, occupiedBefore);
            CapacityAfter = Capacity(handSlots, occupiedAfter);
            Effect = effect;
            PurchasePreview = purchase;
            Demand = demand;
            Exclusion = status != ItemTransactionStatus.Evaluated ? ItemCandidateExclusion.TransactionUnavailable :
                exclusion != ItemCandidateExclusion.None ? exclusion :
                ExceedsThreshold ? ItemCandidateExclusion.None : ItemCandidateExclusion.ThresholdNotExceeded;
        }

        private static decimal? Capacity(int slots, int? occupied) => occupied.HasValue
            ? PermanentItemEffectEvaluator.ActionScale * (decimal)CompositionValueEvaluator.HandCapacityScore(slots - occupied.Value)
            : (decimal?)null;
    }

    public sealed class ItemPurchaseValue
    {
        public ItemTransactionValue Hold { get; }
        public ItemTransactionValue UseAfterPurchase { get; }
        public ItemTargetEvaluations Targets => Hold.Item?.Targets;
        public decimal? TotalScore => Hold.Score + UseAfterPurchase?.Score;
        public decimal? DiscountedScore { get; }
        public decimal? TotalHoldingDelta => UseAfterPurchase?.After?.Value - Hold.Before?.Value;
        public decimal? TotalCapacityDelta => UseAfterPurchase?.CapacityAfter - Hold.CapacityBefore;
        public int? TotalGoldDelta => Hold.GoldDelta + UseAfterPurchase?.GoldDelta;
        public bool DiscountedExceedsThreshold => DiscountedScore > Hold.Threshold;
        public ItemCandidateExclusion ImmediateUseExclusion { get; }
        public bool ImmediateUseEligible => ImmediateUseExclusion == ItemCandidateExclusion.None;
        public bool IsDiscountedPurchaseCandidate => ImmediateUseEligible && DiscountedExceedsThreshold;
        public bool GuaranteesUse => false;
        public int RemainingNormalActions { get; }

        internal ItemPurchaseValue(ItemTransactionValue hold, ItemTransactionValue use, decimal discount, int remaining)
        {
            Hold = hold;
            UseAfterPurchase = use;
            RemainingNormalActions = remaining;
            DiscountedScore = hold.Score + discount * use?.Score;
            if (hold.Legality != ItemPreviewLegality.Legal)
                ImmediateUseExclusion = ItemCandidateExclusion.TransactionUnavailable;
            else if (Targets?.Status == ItemTargetEvaluationStatus.NoLegalTargets)
                ImmediateUseExclusion = ItemCandidateExclusion.NoLegalUseTarget;
            else if (Targets != null && !Targets.ConfirmedMaximumEffect.HasValue)
                ImmediateUseExclusion = ItemCandidateExclusion.UnconfirmedBestTarget;
            else if (hold.Status != ItemTransactionStatus.Evaluated || use?.Status != ItemTransactionStatus.Evaluated)
                ImmediateUseExclusion = ItemCandidateExclusion.TransactionUnavailable;
            else if (remaining < 2)
                ImmediateUseExclusion = ItemCandidateExclusion.RequiresTwoActions;
            else
                ImmediateUseExclusion = use.Exclusion;
        }
    }

    /// <summary>Produces transaction scores and eligibility diagnostics without choosing or executing an Action.</summary>
    public sealed class ItemTransactionValueEvaluator
    {
        public const decimal KeepScore = 0m;
        public ItemInventoryValueEvaluator Inventory { get; }
        public ItemTransactionValueSettings Settings { get; }
        public decimal Threshold { get; }

        private GameObservation Observation => Inventory.Observation;
        private IntegratedItemEffectEvaluator Effects => Inventory.Effects;
        private readonly EconomyValueEvaluator _economy;

        public ItemTransactionValueEvaluator(GameObservation observation, int? totalEncounterCount = null,
            ItemTransactionValueSettings settings = null, bool useCache = true, bool includeDetails = false)
        {
            Settings = settings ?? new ItemTransactionValueSettings();
            Inventory = new ItemInventoryValueEvaluator(observation, totalEncounterCount, Settings.Inventory,
                Settings.Effects, useCache, includeDetails);
            _economy = new EconomyValueEvaluator(Settings.Economy, EconomyInterestObservation.FromObservation(observation));
            Threshold = observation.Stage.EnemyCategory == StageEnemyCategory.None ? 2m : 1m;
        }

        public ItemPurchaseValue EvaluatePurchase(string offerId, int handSlot, int remainingNormalActions) =>
            EvaluatePurchase(Effects.PreviewPurchase(offerId, handSlot), remainingNormalActions);

        public ItemPurchaseValue EvaluatePurchase(ItemPreviewResult purchase, int remainingNormalActions)
        {
            ValidateActions(remainingNormalActions);
            var item = Inventory.EvaluatePurchased(purchase);
            ItemTransactionValue hold;
            ItemTransactionValue use = null;
            if (item.Status == ItemHoldingStatus.DifferentObservation)
                hold = Rejected(ItemTransactionStatus.DifferentObservation, ItemPreviewLegality.Illegal,
                    ItemPreviewFailure.DifferentObservation, purchase: purchase);
            else if (item.Status == ItemHoldingStatus.InvalidPurchase)
                hold = Rejected(StatusFor(purchase?.Legality), purchase?.Legality == ItemPreviewLegality.Unknown
                    ? ItemPreviewLegality.Unknown : ItemPreviewLegality.Illegal,
                    purchase == null || purchase.Failure == ItemPreviewFailure.None ? ItemPreviewFailure.InvalidPurchase : purchase.Failure,
                    purchase: purchase);
            else
            {
                var before = Inventory.EvaluateInventory();
                var bought = Inventory.Add(before, item);
                hold = Compare(item, before, bought, purchase.Before, purchase.After,
                    remainingNormalActions == 0 ? ItemCandidateExclusion.NoActionSlots : ItemCandidateExclusion.None, purchase: purchase);
                // A tied target supplies identical numeric components for the transaction; the complete tie set is retained.
                var best = item.Targets?.ConfirmedMaximumEffect.HasValue == true
                    ? item.Targets.MaximumSuccessfulTargets.First() : null;
                if (best != null)
                    use = Compare(item, bought, before, best.Preview.Before, best.Preview.After,
                        UseExclusion(best, remainingNormalActions), effect: best, purchase: purchase);
            }
            return new ItemPurchaseValue(hold, use, Settings.ImmediateUseDiscount, remainingNormalActions);
        }

        public ItemTransactionValue EvaluateUse(int handSlot, GameActionTarget target, int remainingNormalActions = 1)
        {
            ValidateActions(remainingNormalActions);
            var held = Inventory.EvaluateHand(handSlot);
            var known = held.Targets?.Targets.FirstOrDefault(row => row.Target.HasValue && row.Target.Value.Equals(target));
            return EvaluateUse(known ?? Effects.EvaluateHand(handSlot, target), remainingNormalActions);
        }

        public ItemTransactionValue EvaluateUse(ItemPreviewResult preview, int remainingNormalActions = 1)
        {
            ValidateActions(remainingNormalActions);
            if (preview?.Before != null && !ReferenceEquals(preview.Before.Observation, Observation))
                return Rejected(ItemTransactionStatus.DifferentObservation, ItemPreviewLegality.Illegal, ItemPreviewFailure.DifferentObservation);
            if (preview?.Source != ItemPreviewSource.Hand || !preview.Target.HasValue)
                return Rejected(ItemTransactionStatus.Illegal, ItemPreviewLegality.Illegal, ItemPreviewFailure.InvalidSource);
            return EvaluateUse(Effects.Evaluate(preview), remainingNormalActions);
        }

        private ItemTransactionValue EvaluateUse(IntegratedItemEffectResult effect, int remaining)
        {
            var preview = effect.Preview;
            if (preview.Legality != ItemPreviewLegality.Legal)
                return Rejected(StatusFor(preview.Legality), preview.Legality, preview.Failure, effect: effect);
            var before = Inventory.EvaluateInventory();
            return Compare(Inventory.EvaluateHand(preview.HandSlot.Value), before, Inventory.Remove(before, preview.HandSlot.Value),
                preview.Before, preview.After, UseExclusion(effect, remaining), effect: effect);
        }

        public ItemTransactionValue EvaluateSell(int handSlot, SpaceDemand demand, int remainingNormalActions = 1)
        {
            ValidateActions(remainingNormalActions);
            if (!Enum.IsDefined(typeof(SpaceDemand), demand)) throw new ArgumentOutOfRangeException(nameof(demand));
            if (Observation.Stage.Phase != ObservationPhase.Preparation)
                return Rejected(ItemTransactionStatus.Illegal, ItemPreviewLegality.Illegal, ItemPreviewFailure.InvalidPhase, demand: demand);
            var item = Inventory.EvaluateHand(handSlot);
            if (item.Status == ItemHoldingStatus.InvalidSlot)
                return Rejected(ItemTransactionStatus.Illegal, ItemPreviewLegality.Illegal, ItemPreviewFailure.ItemUnavailable, demand: demand);
            var before = Inventory.EvaluateInventory();
            int occupied = Observation.Hand.SlotCount - Observation.Hand.EmptySlotCount;
            var exclusion = remainingNormalActions == 0 ? ItemCandidateExclusion.NoActionSlots :
                Observation.Hand.EmptySlotCount != 0 ? ItemCandidateExclusion.HandNotFull :
                demand == SpaceDemand.UnknownDemand ? ItemCandidateExclusion.UnknownSpaceDemand :
                demand == SpaceDemand.NoDemand ? ItemCandidateExclusion.NoSpaceDemand : ItemCandidateExclusion.None;
            return Create(item, before, Inventory.Remove(before, handSlot), _economy.Compare(Observation.Player.Gold, Observation.Player.Gold),
                occupied, occupied - 1, exclusion, demand: demand);
        }

        private ItemTransactionValue Compare(ItemHoldingValue item, ItemInventoryValue before, ItemInventoryValue after,
            ItemPreviewState s0, ItemPreviewState s1, ItemCandidateExclusion exclusion,
            IntegratedItemEffectResult effect = null, ItemPreviewResult purchase = null) =>
            Create(item, before, after, s1 == null ? (EconomyValueComparison?)null : _economy.Compare(s0.Gold, s1.Gold),
                s0.HandOccupied, s1?.HandOccupied, exclusion, effect, purchase);

        private ItemTransactionValue Create(ItemHoldingValue item, ItemInventoryValue before, ItemInventoryValue after,
            EconomyValueComparison? economy, int? occupiedBefore, int? occupiedAfter, ItemCandidateExclusion exclusion,
            IntegratedItemEffectResult effect = null, ItemPreviewResult purchase = null, SpaceDemand? demand = null)
        {
            var status = effect != null && !effect.Succeeded ? ItemTransactionStatus.EffectUnavailable :
                !before.Value.HasValue || !after.Value.HasValue ? ItemTransactionStatus.InventoryUnavailable : ItemTransactionStatus.Evaluated;
            return new ItemTransactionValue(status, ItemPreviewLegality.Legal, Threshold, Inventory.FutureWindow,
                effect?.PreviewFailure ?? ItemPreviewFailure.None, exclusion, item, before, after, economy,
                occupiedBefore, occupiedAfter, Observation.Hand.SlotCount, effect, purchase, demand);
        }

        private ItemTransactionValue Rejected(ItemTransactionStatus status, ItemPreviewLegality legality,
            ItemPreviewFailure failure, IntegratedItemEffectResult effect = null, ItemPreviewResult purchase = null, SpaceDemand? demand = null) =>
            new ItemTransactionValue(status, legality, Threshold, Inventory.FutureWindow, failure,
                effect: effect, purchase: purchase, demand: demand);

        private static ItemCandidateExclusion UseExclusion(IntegratedItemEffectResult effect, int remaining) =>
            remaining == 0 ? ItemCandidateExclusion.NoActionSlots :
            effect.EffectValue <= 0m ? ItemCandidateExclusion.NonPositiveEffect : ItemCandidateExclusion.None;

        private static ItemTransactionStatus StatusFor(ItemPreviewLegality? legality) =>
            legality == ItemPreviewLegality.Unknown ? ItemTransactionStatus.LegalityUnknown : ItemTransactionStatus.Illegal;

        private static void ValidateActions(int remainingNormalActions)
        {
            if (remainingNormalActions < 0) throw new ArgumentOutOfRangeException(nameof(remainingNormalActions));
        }
    }
}
