using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Items
{
    public sealed class IntegratedItemEffectSettings
    {
        public HpValueSettings Hp { get; }
        public IntegratedCompositionSettings Composition { get; }
        public BattleValueSettings PermanentBattle { get; }
        public TemporaryItemEffectSettings TemporaryBattle { get; }

        public IntegratedItemEffectSettings(HpValueSettings hp = null, IntegratedCompositionSettings composition = null,
            BattleValueSettings permanentBattle = null, TemporaryItemEffectSettings temporaryBattle = null)
        {
            Hp = hp ?? new HpValueSettings();
            Composition = composition ?? new IntegratedCompositionSettings();
            PermanentBattle = permanentBattle ?? new BattleValueSettings();
            TemporaryBattle = temporaryBattle ?? new TemporaryItemEffectSettings();
        }
    }

    public enum ItemEffectIntegrationFailure { None, PreviewRejected, DefinitionMismatch }
    public enum ItemEffectFailureComponent { None, TemporaryBattle, HpHealing, PermanentComposition, PermanentBattle }
    public enum ItemTargetEvaluationStatus { NoLegalTargets, AllEvaluated, PartiallyEvaluated, NoneEvaluated, EnumerationIncomplete }

    public sealed class IntegratedItemEffectResult : ItemEffectValueResult
    {
        public TemporaryItemEffectEvaluation Temporary { get; }
        public HpValueComparison Healing { get; }
        public PermanentItemEffectEvaluation Permanent { get; }
        public ItemEffectIntegrationFailure IntegrationFailure { get; }
        public ItemEffectFailureComponent FailedComponent { get; }
        public BattleValueDecisionFailure BattleFailure => Temporary?.BattleFailure ?? Permanent?.BattleFailure;
        public TemporaryItemBattleSide? FailedBattleSide => Temporary?.FailedSide ??
            (Permanent?.BattleFailure == null ? (TemporaryItemBattleSide?)null :
                Permanent.BattleFailure.Source == BattleValueCandidateSource.CurrentBoard
                    ? TemporaryItemBattleSide.Before : TemporaryItemBattleSide.After);
        public ItemBattleSkipReason BattleSkipReason =>
            Temporary?.BattleSkipReason ?? Permanent?.BattleSkipReason ?? ItemBattleSkipReason.None;
        public ItemPreviewLegality? Legality => Preview?.Legality;
        public ItemProjectionStatus? ProjectionStatus => Preview?.ProjectionStatus;
        public string ItemId => Preview?.Item?.DefinitionId;
        public ItemPreviewSource? Source => Preview?.Source;
        public GameActionTarget? Target => Preview?.Target;
        public int? ConsumedHandSlot => Preview?.ConsumedHandSlot;
        public ItemObservation ConsumedHandItem => Preview?.ConsumedHandItem;
        public int? HandOccupiedBefore => Preview?.Before?.HandOccupied;
        public int? HandOccupiedAfter => Preview?.After?.HandOccupied;
        public int? HandOccupiedDelta => HandOccupiedAfter - HandOccupiedBefore;
        public int? HandOccupiedBeforePurchase => Preview?.PurchaseOrigin?.HandOccupied;
        public int? PurchaseHandOccupiedDelta => HandOccupiedBefore - HandOccupiedBeforePurchase;
        public int? HandOccupiedDeltaIncludingPurchase => HandOccupiedAfter - (HandOccupiedBeforePurchase ?? HandOccupiedBefore);
        public int? PurchaseGoldDelta => GoldBefore - Preview?.PurchaseOrigin?.Gold;
        public int? PurchasePrice => -PurchaseGoldDelta;
        public decimal? CapacityActionDeltaIncludingPurchase => PermanentItemEffectEvaluator.ActionScale * CapacityDeltaIncludingPurchase;
        public decimal? PurchaseCapacityActionDelta => PermanentItemEffectEvaluator.ActionScale *
            (CapacityBefore - Capacity(Preview?.PurchaseOrigin));
        public decimal? TemporaryDamageValue => Temporary != null ? Temporary.DamageValue : InactiveComponent;
        public decimal? TemporaryDefenseValue => Temporary != null ? Temporary.DefenseValue : InactiveComponent;
        public decimal? HpHealingValue => Healing != null ? Healing.Delta : InactiveComponent;
        public decimal? PermanentCompositionValue => Permanent != null ? Permanent.CompositionActionValue : InactiveComponent;
        public decimal? PermanentBattleCorrectionValue => Permanent != null ? Permanent.BattleActionValue : InactiveComponent;
        public override decimal? EffectValue => Succeeded
            ? TemporaryDamageValue + TemporaryDefenseValue + HpHealingValue + PermanentCompositionValue + PermanentBattleCorrectionValue
            : null;

        private decimal? InactiveComponent => Temporary != null || Healing != null || Permanent != null ? 0m : (decimal?)null;

        internal IntegratedItemEffectResult(ItemPreviewResult preview, ItemEffectEvaluationStatus status,
            ItemEffectIntegrationFailure integrationFailure = ItemEffectIntegrationFailure.None,
            TemporaryItemEffectEvaluation temporary = null, HpValueComparison healing = null,
            PermanentItemEffectEvaluation permanent = null, ItemEffectFailureComponent failed = ItemEffectFailureComponent.None)
            : base(preview, status)
        {
            IntegrationFailure = integrationFailure;
            Temporary = temporary;
            Healing = healing;
            Permanent = permanent;
            FailedComponent = failed;
        }
    }

    public sealed class ItemTargetEvaluations
    {
        public IReadOnlyList<IntegratedItemEffectResult> Targets { get; }
        public ItemPreviewFailure EnumerationFailure { get; }
        public ItemTargetEvaluationStatus Status { get; }
        public bool EnumerationComplete => Status != ItemTargetEvaluationStatus.EnumerationIncomplete;
        public bool AllLegalTargetsEvaluated => Status == ItemTargetEvaluationStatus.AllEvaluated ||
            Status == ItemTargetEvaluationStatus.NoLegalTargets;
        public int LegalCount { get; }
        public int EvaluatedCount { get; }
        public int UnavailableCount => LegalCount - EvaluatedCount;
        public int IllegalCount { get; }
        public int UnknownCount { get; }
        public decimal? MaximumSuccessfulEffect { get; }
        public decimal? ConfirmedMaximumEffect => Status == ItemTargetEvaluationStatus.AllEvaluated ? MaximumSuccessfulEffect : null;
        public IReadOnlyList<IntegratedItemEffectResult> MaximumSuccessfulTargets { get; }

        internal ItemTargetEvaluations(IntegratedItemEffectResult[] targets, ItemPreviewFailure enumerationFailure)
        {
            Targets = Array.AsReadOnly(targets);
            var legal = targets.Where(target => target.Legality == ItemPreviewLegality.Legal).ToArray();
            var evaluated = legal.Where(target => target.Succeeded).ToArray();
            LegalCount = legal.Length;
            EvaluatedCount = evaluated.Length;
            IllegalCount = targets.Count(target => target.Legality == ItemPreviewLegality.Illegal);
            UnknownCount = targets.Count(target => target.Legality == ItemPreviewLegality.Unknown);
            EnumerationFailure = enumerationFailure != ItemPreviewFailure.None ? enumerationFailure :
                targets.FirstOrDefault(target => target.Legality == ItemPreviewLegality.Unknown)?.PreviewFailure ?? ItemPreviewFailure.None;
            Status = EnumerationFailure != ItemPreviewFailure.None || UnknownCount > 0
                ? ItemTargetEvaluationStatus.EnumerationIncomplete
                : LegalCount == 0 ? ItemTargetEvaluationStatus.NoLegalTargets
                : EvaluatedCount == LegalCount ? ItemTargetEvaluationStatus.AllEvaluated
                : EvaluatedCount == 0 ? ItemTargetEvaluationStatus.NoneEvaluated
                : ItemTargetEvaluationStatus.PartiallyEvaluated;
            MaximumSuccessfulEffect = evaluated.Select(target => target.EffectValue).Max();
            MaximumSuccessfulTargets = Array.AsReadOnly(evaluated.Where(target => target.EffectValue == MaximumSuccessfulEffect).ToArray());
        }
    }

    /// <summary>Routes public Item previews to existing evaluators within one decision.</summary>
    public sealed class IntegratedItemEffectEvaluator
    {
        private readonly GameObservation _observation;
        private readonly bool _useCache;
        private readonly bool _includeDetails;
        private readonly Dictionary<ItemPreviewResult, IntegratedItemEffectResult> _results;
        private TemporaryItemEffectEvaluator _temporary;
        private HpValueEvaluator _hp;
        private PermanentItemEffectEvaluator _permanent;

        public IntegratedItemEffectSettings Settings { get; }
        public int TemporaryBattleRequests => _temporary?.RoundRequests ?? 0;
        public int PermanentBattleRequests => _permanent?.BattleRequests ?? 0;
        public int ReferenceBattleEvaluations => _permanent?.ReferenceEvaluations ?? 0;

        public IntegratedItemEffectEvaluator(GameObservation observation, IntegratedItemEffectSettings settings = null,
            bool useCache = true, bool includeDetails = false)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            Settings = settings ?? new IntegratedItemEffectSettings();
            _useCache = useCache;
            _includeDetails = includeDetails;
            if (useCache) _results = new Dictionary<ItemPreviewResult, IntegratedItemEffectResult>();
        }

        public IntegratedItemEffectResult EvaluateHand(int handSlot, GameActionTarget target) =>
            Evaluate(ItemEffectPreview.PreviewUse(_observation, ItemPreviewSource.Hand, target, handSlot));

        public IntegratedItemEffectResult EvaluateEvent(GameActionTarget target) =>
            Evaluate(ItemEffectPreview.PreviewUse(_observation, ItemPreviewSource.Event, target));

        public ItemPreviewResult PreviewPurchase(string offerId, int handSlot) =>
            ItemEffectPreview.PreviewPurchase(_observation, offerId, handSlot);

        public IntegratedItemEffectResult EvaluatePurchased(ItemPreviewResult purchase, GameActionTarget target) =>
            Evaluate(ItemEffectPreview.PreviewUseAfterPurchase(_observation, purchase, target));

        public ItemTargetEvaluations EvaluateHandTargets(int handSlot) => EvaluateTargets(ItemPreviewSource.Hand, handSlot);
        public ItemTargetEvaluations EvaluateEventTargets() => EvaluateTargets(ItemPreviewSource.Event, null);

        public ItemTargetEvaluations EvaluatePurchasedTargets(ItemPreviewResult purchase)
        {
            var previews = ItemEffectPreview.EnumeratePurchasedUseTargets(_observation, purchase, out var failure);
            return Summarize(previews, failure);
        }

        private ItemTargetEvaluations EvaluateTargets(ItemPreviewSource source, int? handSlot)
        {
            var previews = ItemEffectPreview.EnumerateUseTargets(_observation, source, handSlot, out var failure);
            return Summarize(previews, failure);
        }

        private ItemTargetEvaluations Summarize(IReadOnlyList<ItemPreviewResult> previews, ItemPreviewFailure failure) =>
            new ItemTargetEvaluations(previews.Select(Evaluate).ToArray(), failure);

        public IntegratedItemEffectResult Evaluate(ItemPreviewResult preview)
        {
            var status = HpValueEvaluator.PreviewStatus(preview);
            if (preview?.Before != null && !ReferenceEquals(preview.Before.Observation, _observation))
                status = ItemEffectEvaluationStatus.DifferentObservation;
            if (status != ItemEffectEvaluationStatus.Evaluated && status != ItemEffectEvaluationStatus.PublicStatsUnavailable)
                return new IntegratedItemEffectResult(preview, status, ItemEffectIntegrationFailure.PreviewRejected);
            if (_results != null && _results.TryGetValue(preview, out var cached)) return cached;

            var result = Route(preview);
            if (result.Succeeded) _results?.Add(preview, result);
            return result;
        }

        private IntegratedItemEffectResult Route(ItemPreviewResult preview)
        {
            var item = preview.Item;
            if (!MatchesDefinition(item))
                return new IntegratedItemEffectResult(preview, ItemEffectEvaluationStatus.UnsupportedItem,
                    ItemEffectIntegrationFailure.DefinitionMismatch);

            if (item.DefinitionId == "Item005")
            {
                var hp = (_hp ??= new HpValueEvaluator(Settings.Hp)).EvaluateItem(preview);
                return new IntegratedItemEffectResult(preview, hp.Status, healing: hp,
                    failed: hp.Succeeded ? ItemEffectFailureComponent.None : ItemEffectFailureComponent.HpHealing);
            }
            if (item.DefinitionId == "Item006" || item.DefinitionId == "Item007")
            {
                var permanent = (_permanent ??= new PermanentItemEffectEvaluator(_observation, Settings.Composition,
                    Settings.PermanentBattle, _useCache, _includeDetails)).Evaluate(preview);
                var failed = permanent.Succeeded ? ItemEffectFailureComponent.None :
                    permanent.Composition.HasValue ? ItemEffectFailureComponent.PermanentBattle : ItemEffectFailureComponent.PermanentComposition;
                return new IntegratedItemEffectResult(preview, permanent.Status, permanent: permanent, failed: failed);
            }
            var temporary = (_temporary ??= new TemporaryItemEffectEvaluator(_observation, Settings.TemporaryBattle,
                _useCache, _includeDetails)).Evaluate(preview);
            return new IntegratedItemEffectResult(preview, temporary.Status, temporary: temporary,
                failed: temporary.Succeeded ? ItemEffectFailureComponent.None : ItemEffectFailureComponent.TemporaryBattle);
        }

        internal static bool MatchesDefinition(ItemObservation item)
        {
            bool temporary = item.Duration == ItemEffectDuration.ThisBattle;
            bool immediate = item.Duration == ItemEffectDuration.Immediate;
            bool ally = item.Target == ItemEffectTarget.OneAlly;
            bool enemies = item.Target == ItemEffectTarget.AllEnemies;
            bool Single(ItemEffectKind kind, Func<int, bool> validValue) =>
                item.Effects.Count == 1 && item.Effects[0].Kind == kind && validValue(item.Effects[0].Value);
            switch (item.DefinitionId)
            {
                case "Item001": return temporary && ally && Single(ItemEffectKind.Attack, value => value > 0);
                case "Item002": return temporary && enemies && Single(ItemEffectKind.Defense, value => value < 0);
                case "Item003": return temporary && ally && item.Effects.Count == 2 &&
                    item.Effects.Any(effect => effect.Kind == ItemEffectKind.Attack && effect.Value > 0) &&
                    item.Effects.Any(effect => effect.Kind == ItemEffectKind.Defense && effect.Value > 0);
                case "Item004": return temporary && enemies && Single(ItemEffectKind.Damage, value => value > 0);
                case "Item005": return immediate && item.Target == ItemEffectTarget.Player &&
                    Single(ItemEffectKind.Damage, value => value < 0);
                case "Item006": return immediate && ally && Single(ItemEffectKind.Star, value => value == 2);
                case "Item007": return immediate && ally && Single(ItemEffectKind.Attack, value => value > 0);
                default: return false;
            }
        }
    }
}
