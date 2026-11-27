using System;

namespace ProjectNST.GameCore.Items
{
    public enum ItemEffectEvaluationStatus
    {
        Evaluated, InvalidPreview, Illegal, DifferentObservation, ProjectionUnavailable,
        PublicStatsUnavailable, UnsupportedItem, InvalidHealth, CompositionUnavailable, BattleUnavailable
    }

    public sealed class HpValueSettings
    {
        public decimal LowHpValue { get; }
        public decimal MiddleHpValue { get; }
        public decimal HighHpValue { get; }

        public HpValueSettings(decimal lowHpValue = 4m, decimal middleHpValue = 2m, decimal highHpValue = 1m)
        {
            if (lowHpValue < 0m || lowHpValue > decimal.MaxValue / (3m * int.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(lowHpValue));
            if (middleHpValue < 0m || middleHpValue > lowHpValue)
                throw new ArgumentOutOfRangeException(nameof(middleHpValue));
            if (highHpValue < 0m || highHpValue > middleHpValue)
                throw new ArgumentOutOfRangeException(nameof(highHpValue));

            LowHpValue = lowHpValue;
            MiddleHpValue = middleHpValue;
            HighHpValue = highHpValue;
        }
    }

    public abstract class ItemEffectValueResult
    {
        public ItemPreviewResult Preview { get; }
        public ItemEffectEvaluationStatus Status { get; }
        public ItemPreviewFailure? PreviewFailure => Preview?.Failure;
        public bool Succeeded => Status == ItemEffectEvaluationStatus.Evaluated;
        public abstract decimal? EffectValue { get; }
        public decimal? CapacityBefore => Capacity(Preview?.Before);
        public decimal? CapacityAfter => Capacity(Preview?.After);
        public decimal? CapacityDelta => CapacityAfter - CapacityBefore;
        public decimal? CapacityActionValue => PermanentItemEffectEvaluator.ActionScale * CapacityDelta;
        public decimal? CapacityDeltaIncludingPurchase => CapacityAfter - Capacity(Preview?.PurchaseOrigin ?? Preview?.Before);
        public int? GoldBefore => Preview?.Before?.Gold;
        public int? GoldAfter => Preview?.After?.Gold;
        public int? GoldDelta => Preview?.GoldDelta;
        public int? GoldDeltaIncludingPurchase => Preview?.GoldDeltaIncludingPurchase;

        internal ItemEffectValueResult(ItemPreviewResult preview, ItemEffectEvaluationStatus status)
        {
            Preview = preview;
            Status = status;
        }

        internal static decimal? Capacity(ItemPreviewState state) => state == null ? (decimal?)null :
            (decimal)Strategy.CompositionValueEvaluator.HandCapacityScore(state.Observation.Hand.SlotCount - state.HandOccupied);
    }

    public sealed class HpValueComparison : ItemEffectValueResult
    {
        public int? BeforeHp { get; }
        public int? AfterHp { get; }
        public int? MaximumHp { get; }
        public int? EffectiveHealing => Succeeded ? Math.Max(0, AfterHp.Value - BeforeHp.Value) : (int?)null;
        public decimal? BeforeValue { get; }
        public decimal? AfterValue { get; }
        public decimal? Delta => AfterValue - BeforeValue;
        public override decimal? EffectValue => Delta;

        internal HpValueComparison(ItemEffectEvaluationStatus status, int? before = null, int? after = null,
            int? maximum = null, decimal? beforeValue = null, decimal? afterValue = null,
            ItemPreviewResult preview = null) : base(preview, status)
        {
            BeforeHp = before;
            AfterHp = after;
            MaximumHp = maximum;
            BeforeValue = beforeValue;
            AfterValue = afterValue;
        }
    }

    /// <summary>Piecewise HP state value in Action score units.</summary>
    public sealed class HpValueEvaluator
    {
        public HpValueSettings Settings { get; }

        public HpValueEvaluator(HpValueSettings settings = null)
        {
            Settings = settings ?? new HpValueSettings();
        }

        public decimal Evaluate(int hp, int maximumHp)
        {
            if (maximumHp <= 0) throw new ArgumentOutOfRangeException(nameof(maximumHp));
            if (hp < 0 || hp > maximumHp) throw new ArgumentOutOfRangeException(nameof(hp));
            // Work in thirds of an HP and divide the state value once.
            decimal scaledHp = 3m * hp;
            return (Settings.LowHpValue * Math.Min(scaledHp, maximumHp) +
                Settings.MiddleHpValue * Math.Max(0m, Math.Min(scaledHp - maximumHp, maximumHp)) +
                Settings.HighHpValue * Math.Max(0m, scaledHp - 2m * maximumHp)) / 3m;
        }

        public HpValueComparison Compare(int beforeHp, int afterHp, int maximumHp) =>
            Compare(beforeHp, afterHp, maximumHp, null);

        public HpValueComparison CompareHealing(int hp, int amount, int maximumHp)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            decimal before = Evaluate(hp, maximumHp);
            int after = (int)Math.Min(maximumHp, (long)hp + amount);
            return new HpValueComparison(ItemEffectEvaluationStatus.Evaluated,
                hp, after, maximumHp, before, Evaluate(after, maximumHp));
        }

        public HpValueComparison EvaluateItem(ItemPreviewResult preview)
        {
            var status = PreviewStatus(preview);
            if (status == ItemEffectEvaluationStatus.Evaluated)
            {
                if (preview.Item.DefinitionId != "Item005")
                    status = ItemEffectEvaluationStatus.UnsupportedItem;
                else if (preview.Before.Observation.Player.HasHealth && preview.Before.MaximumHP > 0)
                    return Compare(preview.Before.HP, preview.After.HP, preview.Before.MaximumHP, preview);
                else
                    status = ItemEffectEvaluationStatus.InvalidHealth;
            }
            return new HpValueComparison(status, preview: preview);
        }

        private HpValueComparison Compare(int beforeHp, int afterHp, int maximumHp, ItemPreviewResult preview) =>
            new HpValueComparison(ItemEffectEvaluationStatus.Evaluated, beforeHp, afterHp, maximumHp,
                Evaluate(beforeHp, maximumHp), Evaluate(afterHp, maximumHp), preview);

        internal static ItemEffectEvaluationStatus PreviewStatus(ItemPreviewResult preview)
        {
            if (preview == null || !preview.Target.HasValue) return ItemEffectEvaluationStatus.InvalidPreview;
            if (preview.Failure == ItemPreviewFailure.DifferentObservation) return ItemEffectEvaluationStatus.DifferentObservation;
            if (preview.Failure == ItemPreviewFailure.HealthUnavailable) return ItemEffectEvaluationStatus.InvalidHealth;
            if (preview.Legality == ItemPreviewLegality.Illegal) return ItemEffectEvaluationStatus.Illegal;
            if (preview.Failure == ItemPreviewFailure.StatsUnavailable) return ItemEffectEvaluationStatus.PublicStatsUnavailable;
            if (preview.ProjectionStatus == ItemProjectionStatus.Unsupported) return ItemEffectEvaluationStatus.UnsupportedItem;
            return preview.Succeeded ? ItemEffectEvaluationStatus.Evaluated : ItemEffectEvaluationStatus.ProjectionUnavailable;
        }
    }
}
