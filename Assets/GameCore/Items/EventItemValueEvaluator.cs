using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Items
{
    public enum EventItemEvaluationStatus
    {
        OutsideEvent, MissingEventItem, Consumed, NoLegalTargets, AllEvaluated,
        PartiallyEvaluated, NoneEvaluated, EnumerationIncomplete, UnsupportedItem,
        DefinitionMismatch, InconsistentState
    }

    public enum EventItemEvaluationFailure
    {
        None, MissingItem, UnsupportedItem, DefinitionMismatch, InvalidItemState,
        EndBlockedByTemporaryStar, TargetEvaluationUnavailable, TargetEnumerationIncomplete
    }

    public sealed class EventItemTargetValue
    {
        public IntegratedItemEffectResult Effect { get; }
        public GameActionTarget? Target => Effect.Target;
        public CharacterObservation Character { get; }
        public string InstanceId => Character?.InstanceId;
        public string DefinitionId => Character?.DefinitionId;
        public int? BeforeStar => Character?.Star;
        public int? AfterStar => Effect.Preview?.After?.Character?.Star;
        public decimal? Score => Effect.EffectValue;

        internal EventItemTargetValue(IntegratedItemEffectResult effect)
        {
            Effect = effect;
            Character = effect.Preview?.After?.Character?.Before;
            if (Character != null) return;
            if (effect.Preview?.Before?.Observation is GameObservation observation && effect.Target.HasValue)
            {
                var target = effect.Target.Value;
                if (target.Kind == ItemUseTargetKind.BoardCharacter)
                    Character = observation.Board.Slots.FirstOrDefault(slot => slot.SlotIndex == target.SlotIndex)?.Character;
                else if (target.Kind == ItemUseTargetKind.HandCharacter)
                    Character = observation.Hand.Slots.FirstOrDefault(slot => slot.SlotIndex == target.SlotIndex)?.Character;
            }
        }
    }

    public sealed class EventItemEvaluation
    {
        public EventItemEvaluationStatus Status { get; }
        public EventItemEvaluationFailure Failure { get; }
        public EventItemObservation EventItem { get; }
        public ItemTargetEvaluations TargetEvaluations { get; }
        public IReadOnlyList<EventItemTargetValue> Targets { get; }
        public IReadOnlyList<EventItemTargetValue> ConfirmedBestTargets { get; }
        public decimal? MaximumSuccessfulEffect => TargetEvaluations?.MaximumSuccessfulEffect;
        public decimal? ConfirmedBestEffect => Status == EventItemEvaluationStatus.AllEvaluated
            ? TargetEvaluations.ConfirmedMaximumEffect : null;
        public bool? HasLegalTargets { get; }
        public bool MustUse => HasLegalTargets == true;
        public bool CanEnd => Status == EventItemEvaluationStatus.Consumed || Status == EventItemEvaluationStatus.NoLegalTargets;
        public bool RequiresLegacyFallback => Failure != EventItemEvaluationFailure.None;

        internal EventItemEvaluation(EventItemEvaluationStatus status, EventItemObservation item,
            EventItemEvaluationFailure failure = EventItemEvaluationFailure.None, ItemTargetEvaluations targets = null)
        {
            Status = status;
            Failure = failure;
            EventItem = item;
            TargetEvaluations = targets;
            Targets = Array.AsReadOnly(targets?.Targets.Select(target => new EventItemTargetValue(target)).ToArray()
                ?? Array.Empty<EventItemTargetValue>());
            HasLegalTargets = targets?.LegalCount > 0 ? true :
                status == EventItemEvaluationStatus.NoLegalTargets ? false : (bool?)null;
            ConfirmedBestTargets = Array.AsReadOnly(ConfirmedBestEffect.HasValue
                ? Targets.Where(target => target.Score == ConfirmedBestEffect && target.Effect.Legality == ItemPreviewLegality.Legal)
                    .ToArray()
                : Array.Empty<EventItemTargetValue>());
        }
    }

    /// <summary>Evaluates a single public Event. The caller owns Action creation and any legacy fallback.</summary>
    public sealed class EventItemValueEvaluator
    {
        private const string OfficialItemId = "Item006";
        private readonly GameObservation _observation;
        private readonly IntegratedItemEffectSettings _settings;
        private readonly bool _useCache;
        private readonly bool _includeDetails;
        private IntegratedItemEffectEvaluator _effects;
        private EventItemEvaluation _cached;

        public int PermanentBattleRequests => _effects?.PermanentBattleRequests ?? 0;
        public int TemporaryBattleRequests => _effects?.TemporaryBattleRequests ?? 0;
        public int ReferenceBattleEvaluations => _effects?.ReferenceBattleEvaluations ?? 0;

        public EventItemValueEvaluator(GameObservation observation, IntegratedItemEffectSettings settings = null,
            bool useCache = true, bool includeDetails = false)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            _settings = settings;
            _useCache = useCache;
            _includeDetails = includeDetails;
        }

        public EventItemEvaluation Evaluate()
        {
            if (_cached != null) return _cached;
            var state = CheckEvent();
            var result = state ?? DescribeTargets(_observation.EventItem, Effects.EvaluateEventTargets());
            if (_useCache) _cached = result;
            return result;
        }

        public EventItemTargetValue EvaluateTarget(GameActionTarget target)
        {
            var cached = _cached?.Targets.FirstOrDefault(row => row.Target.HasValue && row.Target.Value.Equals(target));
            if (cached != null) return cached;
            return CheckEvent() == null ? new EventItemTargetValue(Effects.EvaluateEvent(target)) :
                EvaluatePreview(ItemEffectPreview.PreviewUse(_observation, ItemPreviewSource.Event, target));
        }

        public EventItemTargetValue EvaluatePreview(ItemPreviewResult preview)
        {
            if (preview?.Before != null && !ReferenceEquals(preview.Before.Observation, _observation))
                return Reject(preview, ItemEffectEvaluationStatus.DifferentObservation);
            if (preview?.Source != ItemPreviewSource.Event || !preview.Target.HasValue)
                return Reject(preview, ItemEffectEvaluationStatus.InvalidPreview);
            var state = CheckEvent();
            if (state == null) return new EventItemTargetValue(Effects.Evaluate(preview));
            return Reject(preview, state.Status == EventItemEvaluationStatus.UnsupportedItem ||
                state.Status == EventItemEvaluationStatus.DefinitionMismatch
                ? ItemEffectEvaluationStatus.UnsupportedItem : ItemEffectEvaluationStatus.Illegal);
        }

        private IntegratedItemEffectEvaluator Effects => _effects ??=
            new IntegratedItemEffectEvaluator(_observation, _settings, _useCache, _includeDetails);

        private static EventItemTargetValue Reject(ItemPreviewResult preview, ItemEffectEvaluationStatus status) =>
            new EventItemTargetValue(new IntegratedItemEffectResult(preview, status, ItemEffectIntegrationFailure.PreviewRejected));

        private EventItemEvaluation CheckEvent()
        {
            var item = _observation.EventItem;
            if (_observation.Stage.Phase != ObservationPhase.EventPending)
                return new EventItemEvaluation(EventItemEvaluationStatus.OutsideEvent, item);
            if (item == null)
                return new EventItemEvaluation(EventItemEvaluationStatus.MissingEventItem, null, EventItemEvaluationFailure.MissingItem);
            // The formal handler closes Consumed before looking up the definition.
            if (item.Status == EventItemStatus.Consumed)
                return new EventItemEvaluation(EventItemEvaluationStatus.Consumed, item);
            if (item.Status != EventItemStatus.Available)
                return new EventItemEvaluation(EventItemEvaluationStatus.InconsistentState, item, EventItemEvaluationFailure.InvalidItemState);
            if (item.Item == null)
                return new EventItemEvaluation(EventItemEvaluationStatus.MissingEventItem, item, EventItemEvaluationFailure.MissingItem);
            if (item.Item.DefinitionId != OfficialItemId)
                return new EventItemEvaluation(EventItemEvaluationStatus.UnsupportedItem, item, EventItemEvaluationFailure.UnsupportedItem);
            if (!IntegratedItemEffectEvaluator.MatchesDefinition(item.Item))
                return new EventItemEvaluation(EventItemEvaluationStatus.DefinitionMismatch, item, EventItemEvaluationFailure.DefinitionMismatch);
            return null;
        }

        private static EventItemEvaluation DescribeTargets(EventItemObservation item, ItemTargetEvaluations targets)
        {
            switch (targets.Status)
            {
                case ItemTargetEvaluationStatus.NoLegalTargets:
                    // A synthetic temporary Star 1 is rejected for permanent use, but the legacy EndEvent
                    // predicate sees it as eligible by rank alone. Never claim EndEvent is legal in that state.
                    if (targets.Targets.Any(target => new EventItemTargetValue(target).Character is CharacterObservation character &&
                        character.Star < item.Item.Effects[0].Value && character.IsStarTemporary()))
                        return new EventItemEvaluation(EventItemEvaluationStatus.InconsistentState, item,
                            EventItemEvaluationFailure.EndBlockedByTemporaryStar, targets);
                    return new EventItemEvaluation(EventItemEvaluationStatus.NoLegalTargets, item, targets: targets);
                case ItemTargetEvaluationStatus.AllEvaluated:
                    return new EventItemEvaluation(EventItemEvaluationStatus.AllEvaluated, item, targets: targets);
                case ItemTargetEvaluationStatus.PartiallyEvaluated:
                    return new EventItemEvaluation(EventItemEvaluationStatus.PartiallyEvaluated, item,
                        EventItemEvaluationFailure.TargetEvaluationUnavailable, targets);
                case ItemTargetEvaluationStatus.NoneEvaluated:
                    return new EventItemEvaluation(EventItemEvaluationStatus.NoneEvaluated, item,
                        EventItemEvaluationFailure.TargetEvaluationUnavailable, targets);
                default:
                    return new EventItemEvaluation(EventItemEvaluationStatus.EnumerationIncomplete, item,
                        EventItemEvaluationFailure.TargetEnumerationIncomplete, targets);
            }
        }
    }
}
