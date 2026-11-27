using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Items
{
    public enum ItemFutureWindow { FutureEncounterAvailable, FinalEncounter, FutureEncounterUnknown }
    public enum ItemHoldingStatus
    {
        Evaluated, InvalidSlot, InvalidPurchase, DifferentObservation, UnsupportedItem, DefinitionMismatch, EffectUnavailable
    }

    public sealed class ItemInventoryValueSettings
    {
        private static readonly string[] SupportedIds =
            { "Item001", "Item002", "Item003", "Item004", "Item005", "Item006", "Item007" };

        public decimal CurrentEffectRate { get; }
        public decimal MaximumItemValue { get; }
        public decimal DuplicateRate { get; }
        public IReadOnlyDictionary<string, decimal> FutureValues { get; }

        public ItemInventoryValueSettings(decimal currentEffectRate = .50m, decimal maximumItemValue = 24m,
            decimal duplicateRate = .25m, IEnumerable<KeyValuePair<string, decimal>> futureValues = null)
        {
            if (currentEffectRate < 0 || currentEffectRate > 1)
                throw new ArgumentOutOfRangeException(nameof(currentEffectRate));
            if (duplicateRate < 0 || duplicateRate > 1)
                throw new ArgumentOutOfRangeException(nameof(duplicateRate));
            if (maximumItemValue < 0 || maximumItemValue > decimal.MaxValue / 16m)
                throw new ArgumentOutOfRangeException(nameof(maximumItemValue));
            var values = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["Item001"] = 4m, ["Item002"] = 3.50m, ["Item003"] = 4.50m, ["Item004"] = 4.50m,
                ["Item005"] = 2m, ["Item006"] = 3m, ["Item007"] = 2m
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in futureValues ?? Enumerable.Empty<KeyValuePair<string, decimal>>())
            {
                if (!IsSupported(entry.Key) || !seen.Add(entry.Key))
                    throw new ArgumentException("Future values must use distinct supported Item IDs.", nameof(futureValues));
                if (entry.Value < 0 || entry.Value > decimal.MaxValue / 16m)
                    throw new ArgumentOutOfRangeException(nameof(futureValues));
                values[entry.Key] = entry.Value;
            }
            CurrentEffectRate = currentEffectRate;
            MaximumItemValue = maximumItemValue;
            DuplicateRate = duplicateRate;
            FutureValues = new ReadOnlyDictionary<string, decimal>(values);
        }

        internal static bool IsSupported(string id) => Array.IndexOf(SupportedIds, id) >= 0;
    }

    public sealed class ItemHoldingValue
    {
        public int HandSlot { get; }
        public ItemObservation Item { get; }
        public string Group => Item?.DefinitionId == "Item003" ? "Item001" : Item?.DefinitionId;
        public ItemHoldingStatus Status { get; }
        public ItemTargetEvaluations Targets { get; }
        public ItemFutureWindow FutureWindow { get; }
        public int? LegalTargetCount => Targets?.LegalCount;
        public decimal? BestEffect => Targets?.ConfirmedMaximumEffect;
        public decimal? CurrentValue { get; }
        public decimal? FutureValue { get; }
        public decimal? Value { get; }

        internal ItemHoldingValue(int slot, ItemObservation item, ItemHoldingStatus status,
            ItemFutureWindow futureWindow, ItemTargetEvaluations targets = null, decimal? current = null,
            decimal? future = null, decimal? value = null)
        {
            HandSlot = slot;
            Item = item;
            Status = status;
            FutureWindow = futureWindow;
            Targets = targets;
            CurrentValue = current;
            FutureValue = future;
            Value = value;
        }
    }

    public sealed class ItemInventoryValue
    {
        public IReadOnlyList<ItemHoldingValue> Items { get; }
        public IReadOnlyList<ItemHoldingValue> UnavailableItems { get; }
        public IReadOnlyDictionary<string, decimal> EvaluatedGroups { get; }
        public decimal? Value { get; }

        internal ItemInventoryValue(IEnumerable<ItemHoldingValue> items, decimal duplicateRate)
        {
            var entries = items.OrderBy(item => item.HandSlot).ToArray();
            Items = Array.AsReadOnly(entries);
            UnavailableItems = Array.AsReadOnly(entries.Where(item => !item.Value.HasValue).ToArray());
            var groups = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var group in entries.GroupBy(item => item.Group, StringComparer.Ordinal))
            {
                if (group.Any(item => !item.Value.HasValue)) continue;
                var ordered = group.Select(item => item.Value.Value).OrderByDescending(value => value).ToArray();
                groups.Add(group.Key, ordered[0] + duplicateRate * ordered.Skip(1).Sum());
            }
            EvaluatedGroups = new ReadOnlyDictionary<string, decimal>(groups);
            if (UnavailableItems.Count == 0) Value = groups.Values.Sum();
        }
    }

    /// <summary>Scores inventory changes in a fixed decision context. Retained Items are not reevaluated after use.</summary>
    public sealed class ItemInventoryValueEvaluator
    {
        internal GameObservation Observation { get; }
        internal IntegratedItemEffectEvaluator Effects { get; }
        public ItemInventoryValueSettings Settings { get; }
        public ItemFutureWindow FutureWindow { get; }
        public int? TotalEncounterCount { get; }

        private readonly Dictionary<int, ItemHoldingValue> _held;
        private readonly Dictionary<ItemPreviewResult, ItemHoldingValue> _purchased;
        private readonly bool _useCache;
        private ItemInventoryValue _baseline;

        public ItemInventoryValueEvaluator(GameObservation observation, int? totalEncounterCount = null,
            ItemInventoryValueSettings settings = null, IntegratedItemEffectSettings effectSettings = null,
            bool useCache = true, bool includeDetails = false)
        {
            Observation = observation ?? throw new ArgumentNullException(nameof(observation));
            var stage = observation.Stage;
            if (stage.EncounterIndex < 0 || stage.StageNumber <= 0 || stage.RoundNumber <= 0)
                throw new ArgumentException("Public encounter coordinates are invalid.", nameof(observation));
            if (totalEncounterCount.HasValue && totalEncounterCount.Value <= stage.EncounterIndex)
                throw new ArgumentOutOfRangeException(nameof(totalEncounterCount));
            Settings = settings ?? new ItemInventoryValueSettings();
            TotalEncounterCount = totalEncounterCount;
            FutureWindow = !totalEncounterCount.HasValue ? ItemFutureWindow.FutureEncounterUnknown :
                stage.EncounterIndex == totalEncounterCount.Value - 1 ? ItemFutureWindow.FinalEncounter :
                ItemFutureWindow.FutureEncounterAvailable;
            Effects = new IntegratedItemEffectEvaluator(observation, effectSettings, useCache, includeDetails);
            _useCache = useCache;
            if (useCache)
            {
                _held = new Dictionary<int, ItemHoldingValue>();
                _purchased = new Dictionary<ItemPreviewResult, ItemHoldingValue>();
            }
        }

        public ItemHoldingValue EvaluateHand(int handSlot)
        {
            if (_held != null && _held.TryGetValue(handSlot, out var cached)) return cached;
            var slot = Observation.Hand.Slots.FirstOrDefault(entry => entry.SlotIndex == handSlot);
            if (slot?.Kind != HandSlotContentKind.Item)
                return Failed(handSlot, null, ItemHoldingStatus.InvalidSlot);
            var result = Evaluate(handSlot, slot.Item, () => Effects.EvaluateHandTargets(handSlot));
            _held?.Add(handSlot, result);
            return result;
        }

        public ItemHoldingValue EvaluatePurchased(ItemPreviewResult purchase)
        {
            int slot = purchase?.HandSlot ?? -1;
            if (purchase?.Before != null && !ReferenceEquals(purchase.Before.Observation, Observation))
                return Failed(slot, purchase.Item, ItemHoldingStatus.DifferentObservation);
            if (purchase?.Succeeded != true || purchase.Source != ItemPreviewSource.Purchased || purchase.Target.HasValue)
                return Failed(slot, purchase?.Item, ItemHoldingStatus.InvalidPurchase);

            if (_purchased != null && _purchased.TryGetValue(purchase, out var cached)) return cached;
            var result = Evaluate(slot, purchase.Item, () => Effects.EvaluatePurchasedTargets(purchase));
            _purchased?.Add(purchase, result);
            return result;
        }

        public ItemInventoryValue EvaluateInventory()
        {
            if (_baseline != null) return _baseline;
            var result = new ItemInventoryValue(Observation.Hand.Slots.Where(slot => slot.Kind == HandSlotContentKind.Item)
                .Select(slot => EvaluateHand(slot.SlotIndex)), Settings.DuplicateRate);
            if (_useCache) _baseline = result;
            return result;
        }

        internal ItemInventoryValue Add(ItemInventoryValue inventory, ItemHoldingValue item) =>
            new ItemInventoryValue(inventory.Items.Append(item), Settings.DuplicateRate);

        internal ItemInventoryValue Remove(ItemInventoryValue inventory, int handSlot) =>
            new ItemInventoryValue(inventory.Items.Where(item => item.HandSlot != handSlot), Settings.DuplicateRate);

        private ItemHoldingValue Evaluate(int slot, ItemObservation item, Func<ItemTargetEvaluations> evaluateTargets)
        {
            if (item == null || !ItemInventoryValueSettings.IsSupported(item.DefinitionId))
                return Failed(slot, item, ItemHoldingStatus.UnsupportedItem);
            if (!IntegratedItemEffectEvaluator.MatchesDefinition(item))
                return Failed(slot, item, ItemHoldingStatus.DefinitionMismatch);

            var targets = evaluateTargets();
            decimal future = FutureWindow == ItemFutureWindow.FutureEncounterAvailable
                ? Settings.FutureValues[item.DefinitionId] : 0m;
            if (!targets.AllLegalTargetsEvaluated)
                return new ItemHoldingValue(slot, item, ItemHoldingStatus.EffectUnavailable, FutureWindow, targets, future: future);
            decimal current = Settings.CurrentEffectRate * Math.Max(0m, targets.ConfirmedMaximumEffect ?? 0m);
            return new ItemHoldingValue(slot, item, ItemHoldingStatus.Evaluated, FutureWindow, targets, current, future,
                Math.Min(Settings.MaximumItemValue, Math.Max(current, future)));
        }

        private ItemHoldingValue Failed(int slot, ItemObservation item, ItemHoldingStatus status) =>
            new ItemHoldingValue(slot, item, status, FutureWindow);
    }
}
