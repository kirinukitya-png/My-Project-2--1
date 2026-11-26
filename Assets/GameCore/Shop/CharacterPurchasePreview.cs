using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Shop
{
    public enum CharacterPurchasePreviewFailure
    {
        None, ShopUnavailable, OfferUnavailable, InvalidOfferCost, InsufficientGold,
        InvalidSlot, SlotOccupied, DuplicateBoardDefinition
    }

    public enum CharacterPurchasePreviewKind { Unavailable, NormalPurchase, MergePurchase }

    public readonly struct PurchaseOccupancy
    {
        public int BoardCount { get; }
        public int EmptyBoardSlots { get; }
        public int Level { get; }
        public int OverLevelCount => Math.Max(0, BoardCount - Level);
        public int HandOccupied { get; }
        public int EmptyHandSlots { get; }

        internal PurchaseOccupancy(int board, int boardSlots, int level, int hand, int handSlots)
        {
            BoardCount = board;
            EmptyBoardSlots = boardSlots - board;
            Level = level;
            HandOccupied = hand;
            EmptyHandSlots = handSlots - hand;
        }
    }

    public readonly struct PurchaseStarCounts
    {
        public int OneStar { get; }
        public int TwoStar { get; }
        public int ThreeStar { get; }

        internal PurchaseStarCounts(int one, int two, int three)
        {
            OneStar = one;
            TwoStar = two;
            ThreeStar = three;
        }

        internal PurchaseStarCounts Change(int star, int amount) => new PurchaseStarCounts(
            OneStar + (star == 1 ? amount : 0), TwoStar + (star == 2 ? amount : 0),
            ThreeStar + (star == 3 ? amount : 0));
    }

    /// <summary>A Character requiring a stat roll. Its base ATK/DEF are unknown until that roll.</summary>
    public sealed class PurchaseStatRoll
    {
        public string InstanceId { get; }
        public bool IsNewCharacter => InstanceId == null;
        public string DefinitionId { get; }
        public int RunCost { get; }
        public int Star { get; }
        public int TemporaryAttackBonus { get; }
        public int TemporaryDefenseBonus { get; }
        public IReadOnlyList<string> Units { get; }
        public IReadOnlyList<string> Attributes { get; }

        internal PurchaseStatRoll(string definitionId, int runCost, IReadOnlyList<string> units,
            IReadOnlyList<string> attributes)
        {
            DefinitionId = definitionId;
            RunCost = runCost;
            Star = 1;
            Units = units ?? Array.Empty<string>();
            Attributes = attributes ?? Array.Empty<string>();
        }

        internal PurchaseStatRoll(CharacterObservation survivor, int star)
        {
            InstanceId = survivor.InstanceId;
            DefinitionId = survivor.DefinitionId;
            RunCost = survivor.RunCost;
            Star = star;
            TemporaryAttackBonus = survivor.TemporaryAttackBonus;
            TemporaryDefenseBonus = survivor.TemporaryDefenseBonus;
            Units = survivor.Units;
            Attributes = survivor.Attributes;
        }
    }

    public sealed class PurchaseMergePreviewStage
    {
        public PurchaseStatRoll Survivor { get; }
        public IReadOnlyList<string> ConsumedInstanceIds { get; }
        public bool ConsumesPurchase { get; }

        internal PurchaseMergePreviewStage(PurchaseStatRoll survivor, bool consumesPurchase, params string[] consumed)
        {
            Survivor = survivor;
            ConsumesPurchase = consumesPurchase;
            ConsumedInstanceIds = Array.AsReadOnly(consumed);
        }
    }

    public readonly struct PurchaseSlotChange
    {
        public CharacterLocation Location { get; }
        public string PreviousInstanceId { get; }
        /// <summary>Null when this slot will be empty. Items never occur in changed slots.</summary>
        public PurchaseStatRoll CharacterAfter { get; }

        internal PurchaseSlotChange(CharacterLocation location, string previousInstanceId, PurchaseStatRoll after)
        {
            Location = location;
            PreviousInstanceId = previousInstanceId;
            CharacterAfter = after;
        }
    }

    public sealed class CharacterPurchasePreviewResult
    {
        /// <summary>Whether the visible occupancy can be projected. The dispatcher validates the transaction.</summary>
        public bool Succeeded => Failure == CharacterPurchasePreviewFailure.None;
        public CharacterPurchasePreviewFailure Failure { get; }
        public CharacterPurchasePreviewKind Kind { get; }
        public string DefinitionId { get; }
        public CharacterLocation RequestedLocation { get; }
        public CharacterLocation? FinalLocation { get; }
        public PurchaseStatRoll FinalCharacter { get; }
        public IReadOnlyList<PurchaseMergePreviewStage> MergeStages { get; }
        public int MergeStageCount => MergeStages.Count;
        public IReadOnlyList<PurchaseSlotChange> SlotChanges { get; }
        public PurchaseOccupancy Before { get; }
        public PurchaseOccupancy After { get; }
        public PurchaseStarCounts StarsBefore { get; }
        public PurchaseStarCounts StarsAfter { get; }

        internal CharacterPurchasePreviewResult(CharacterPurchasePreviewFailure failure, string definitionId,
            CharacterLocation requested, PurchaseOccupancy before, PurchaseStarCounts stars,
            CharacterLocation? final = null, PurchaseStatRoll character = null,
            IReadOnlyList<PurchaseMergePreviewStage> stages = null, IReadOnlyList<PurchaseSlotChange> changes = null,
            PurchaseOccupancy? after = null, PurchaseStarCounts? starsAfter = null)
        {
            Failure = failure;
            DefinitionId = definitionId;
            RequestedLocation = requested;
            FinalLocation = final;
            FinalCharacter = character;
            MergeStages = stages ?? Array.Empty<PurchaseMergePreviewStage>();
            SlotChanges = changes ?? Array.Empty<PurchaseSlotChange>();
            Before = before;
            After = after ?? before;
            StarsBefore = stars;
            StarsAfter = starsAfter ?? stars;
            Kind = !Succeeded ? CharacterPurchasePreviewKind.Unavailable :
                MergeStages.Count == 0 ? CharacterPurchasePreviewKind.NormalPurchase : CharacterPurchasePreviewKind.MergePurchase;
        }
    }

    /// <summary>Reusable decision-local preview for a single immutable public observation.</summary>
    public sealed class CharacterPurchasePreview
    {
        private readonly GameObservation _observation;
        private readonly PurchaseOccupancy _before;
        private readonly Dictionary<string, MergePlan> _plans = new Dictionary<string, MergePlan>(StringComparer.Ordinal);

        public CharacterPurchasePreview(GameObservation observation)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            _before = new PurchaseOccupancy(observation.Board.Slots.Count(slot => slot.IsOccupied),
                observation.Board.Slots.Count, observation.Player.Level,
                observation.Hand.SlotCount - observation.Hand.EmptySlotCount, observation.Hand.SlotCount);
        }

        public CharacterPurchasePreviewResult Preview(BuyCharacterAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            CharacterLocation requested = action.Placement.ToCharacterLocation();
            ShopOfferObservation offer = _observation.Shop?.Offers.FirstOrDefault(o => o.OfferId == action.OfferId);
            if (!CanPreview)
                return Failure(CharacterPurchasePreviewFailure.ShopUnavailable, offer?.DefinitionId, requested);
            if (offer == null || offer.Kind != ShopOfferKind.Character || offer.Status != ShopOfferStatus.Available ||
                string.IsNullOrWhiteSpace(offer.DefinitionId))
                return Failure(CharacterPurchasePreviewFailure.OfferUnavailable, offer?.DefinitionId, requested);

            return PreviewCore(offer.DefinitionId, offer.CharacterCost.GetValueOrDefault(), offer.DisplayPrice,
                _observation.Player.Gold, requested, offer.Units, offer.Attributes);
        }

        /// <summary>Projects a public hypothetical offer without creating an executable action or changing the Shop.</summary>
        public CharacterPurchasePreviewResult PreviewHypothetical(RosterDefinitionObservation definition,
            int runCost, int price, int gold, CharacterLocation requested)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (runCost < 1 || runCost > 5) throw new ArgumentOutOfRangeException(nameof(runCost));
            if (price != runCost) throw new ArgumentOutOfRangeException(nameof(price));
            if (gold < 0) throw new ArgumentOutOfRangeException(nameof(gold));
            if (!CanPreview) return Failure(CharacterPurchasePreviewFailure.ShopUnavailable, definition.DefinitionId, requested);
            return PreviewCore(definition.DefinitionId, runCost, price, gold, requested, definition.Units, definition.Attributes);
        }

        private bool CanPreview => _observation.IsStrategyActionAllowed &&
            _observation.Stage.Phase == ObservationPhase.Preparation && _observation.Shop?.Kind == ObservationShopKind.Character;

        private CharacterPurchasePreviewResult Failure(CharacterPurchasePreviewFailure failure, string definitionId,
            CharacterLocation requested, PurchaseStarCounts stars = default) =>
            new CharacterPurchasePreviewResult(failure, definitionId, requested, _before, stars);

        private CharacterPurchasePreviewResult PreviewCore(string definitionId, int runCost, int price, int gold,
            CharacterLocation requested, IReadOnlyList<string> units, IReadOnlyList<string> attributes)
        {
            MergePlan plan = GetPlan(definitionId);
            CharacterPurchasePreviewResult Fail(CharacterPurchasePreviewFailure failure) =>
                Failure(failure, definitionId, requested, plan.Stars);
            if (runCost < 1 || price != runCost) return Fail(CharacterPurchasePreviewFailure.InvalidOfferCost);
            if (gold < price) return Fail(CharacterPurchasePreviewFailure.InsufficientGold);
            if (requested.Kind != CharacterLocationKind.Board && requested.Kind != CharacterLocationKind.Hand)
                return Fail(CharacterPurchasePreviewFailure.InvalidSlot);

            bool board = requested.Kind == CharacterLocationKind.Board;
            int max = board ? _observation.Board.Slots.Count : _observation.Hand.SlotCount;
            if (requested.SlotIndex < 0 || requested.SlotIndex >= max)
                return Fail(CharacterPurchasePreviewFailure.InvalidSlot);

            CharacterObservation target = CharacterAt(requested);
            if (plan.Stages.Count == 0)
            {
                if (target != null || (!board && _observation.Hand.Slots[requested.SlotIndex].Kind != HandSlotContentKind.Empty))
                    return Fail(CharacterPurchasePreviewFailure.SlotOccupied);
                if (board && plan.HasBoardCharacter)
                    return Fail(CharacterPurchasePreviewFailure.DuplicateBoardDefinition);
                return BuildResult(definitionId, requested, requested, new PurchaseStatRoll(definitionId, runCost, units, attributes), plan);
            }

            // In a merge, Hand placement is ignored. An initially empty Board destination moves
            // the final survivor, even when the survivor of the first step was a different Character.
            CharacterLocation final = board && target == null ? requested : LocationOf(plan.Survivor);
            if (final.Kind == CharacterLocationKind.Board &&
                plan.Survivor.LocationKind == ObservationLocationKind.Hand && plan.HasBoardCharacter)
                return Fail(CharacterPurchasePreviewFailure.DuplicateBoardDefinition);
            return BuildResult(definitionId, requested, final, plan.Stages[plan.Stages.Count - 1].Survivor, plan);
        }

        private MergePlan GetPlan(string definitionId)
        {
            if (_plans.TryGetValue(definitionId, out MergePlan plan)) return plan;

            // ObservationBuilder produces slot-ordered Board and Hand snapshots.
            var candidates = _observation.Board.Slots.Select(slot => slot.Character)
                .Concat(_observation.Hand.Slots.Select(slot => slot.Character))
                .Where(character => character != null && character.DefinitionId == definitionId).ToArray();
            var stars = new PurchaseStarCounts(candidates.Count(c => c.Star == 1),
                candidates.Count(c => c.Star == 2), candidates.Count(c => c.Star == 3));
            var first = candidates.Where(c => c.Star == 1).Take(2).ToArray();
            var stages = new List<PurchaseMergePreviewStage>(2);
            var removed = new List<CharacterObservation>(3);
            CharacterObservation survivor = null;
            CharacterObservation retainedUpgrade = null;
            if (first.Length == 2)
            {
                survivor = first[0];
                removed.Add(first[1]);
                stages.Add(new PurchaseMergePreviewStage(new PurchaseStatRoll(survivor, 2), true, first[1].InstanceId));
                var second = candidates.Where(c => c.Star == 2 || c == survivor).Take(3).ToArray();
                if (second.Length == 3)
                {
                    // Earlier existing two-stars can fill all three positions in the chain.
                    // The first-stage survivor then keeps its new Star in its original slot.
                    if (!second.Contains(survivor)) retainedUpgrade = survivor;
                    survivor = second[0];
                    removed.Add(second[1]);
                    removed.Add(second[2]);
                    stages.Add(new PurchaseMergePreviewStage(new PurchaseStatRoll(survivor, 3), false,
                        second[1].InstanceId, second[2].InstanceId));
                }
            }
            plan = new MergePlan(stars, candidates.Any(c => c.LocationKind == ObservationLocationKind.Board),
                survivor, retainedUpgrade, stages.AsReadOnly(), removed);
            _plans.Add(definitionId, plan);
            return plan;
        }

        private CharacterPurchasePreviewResult BuildResult(string definitionId, CharacterLocation requested,
            CharacterLocation final, PurchaseStatRoll character, MergePlan plan)
        {
            var changes = new List<PurchaseSlotChange>(plan.Removed.Count + 3);
            foreach (CharacterObservation removed in plan.Removed)
                changes.Add(new PurchaseSlotChange(LocationOf(removed), removed.InstanceId, null));
            if (plan.RetainedUpgrade != null)
                changes.Add(new PurchaseSlotChange(LocationOf(plan.RetainedUpgrade), plan.RetainedUpgrade.InstanceId,
                    plan.Stages[0].Survivor));
            if (plan.Survivor != null)
            {
                CharacterLocation source = LocationOf(plan.Survivor);
                if (source.Kind != final.Kind || source.SlotIndex != final.SlotIndex)
                    changes.Add(new PurchaseSlotChange(source, plan.Survivor.InstanceId, null));
            }
            changes.Add(new PurchaseSlotChange(final, CharacterAt(final)?.InstanceId, character));

            int board = _before.BoardCount;
            int hand = _before.HandOccupied;
            PurchaseStarCounts stars = plan.Stars;
            foreach (PurchaseSlotChange change in changes)
            {
                CharacterObservation previous = CharacterAt(change.Location);
                int occupiedDelta = (change.CharacterAfter == null ? 0 : 1) - (previous == null ? 0 : 1);
                if (change.Location.Kind == CharacterLocationKind.Board) board += occupiedDelta;
                else hand += occupiedDelta;
                if (previous != null) stars = stars.Change(previous.Star, -1);
                if (change.CharacterAfter != null) stars = stars.Change(change.CharacterAfter.Star, 1);
            }
            var after = new PurchaseOccupancy(board, _observation.Board.Slots.Count, _before.Level,
                hand, _observation.Hand.SlotCount);
            return new CharacterPurchasePreviewResult(CharacterPurchasePreviewFailure.None, definitionId,
                requested, _before, plan.Stars, final, character, plan.Stages, changes.AsReadOnly(), after, stars);
        }

        private CharacterObservation CharacterAt(CharacterLocation location) => location.Kind == CharacterLocationKind.Board
            ? _observation.Board.Slots[location.SlotIndex].Character
            : _observation.Hand.Slots[location.SlotIndex].Character;

        private static CharacterLocation LocationOf(CharacterObservation character) => new CharacterLocation(
            character.LocationKind == ObservationLocationKind.Board ? CharacterLocationKind.Board : CharacterLocationKind.Hand,
            character.SlotIndex);

        private sealed class MergePlan
        {
            public PurchaseStarCounts Stars { get; }
            public bool HasBoardCharacter { get; }
            public CharacterObservation Survivor { get; }
            public CharacterObservation RetainedUpgrade { get; }
            public IReadOnlyList<PurchaseMergePreviewStage> Stages { get; }
            public IReadOnlyList<CharacterObservation> Removed { get; }

            public MergePlan(PurchaseStarCounts stars, bool hasBoardCharacter, CharacterObservation survivor,
                CharacterObservation retainedUpgrade,
                IReadOnlyList<PurchaseMergePreviewStage> stages, IReadOnlyList<CharacterObservation> removed)
            {
                Stars = stars;
                HasBoardCharacter = hasBoardCharacter;
                Survivor = survivor;
                RetainedUpgrade = retainedUpgrade;
                Stages = stages;
                Removed = removed;
            }
        }
    }
}
