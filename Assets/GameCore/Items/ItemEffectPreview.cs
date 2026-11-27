using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Items
{
    public enum ItemPreviewLegality { Legal, Illegal, Unknown }
    public enum ItemProjectionStatus { Projected, NotApplicable, Unavailable, Unsupported }
    public enum ItemPreviewFailure
    {
        None, MissingObservation, InvalidSource, InvalidPhase, ItemUnavailable, InvalidDefinition,
        InvalidTarget, EnemyInformationUnavailable, HealthUnavailable, UnsupportedEffect,
        StatsUnavailable, ArithmeticOverflow, InvalidShop, InvalidOffer, InsufficientGold,
        HandSlotUnavailable, DifferentObservation, InvalidPurchase
    }

    public enum ItemPreviewSource { Hand, Event, Purchased }
    public enum ItemPreviewStatsKind { Deterministic, PublicMean, Unavailable }

    /// <summary>A single replacement of public character data. Identity and location come from Before.</summary>
    public sealed class ItemCharacterChange
    {
        public CharacterObservation Before { get; }
        public int Star { get; }
        public double? Attack { get; }
        public double? Defense { get; }
        public int TemporaryAttack { get; }
        public int TemporaryDefense { get; }
        public double? EffectiveAttack => Attack + TemporaryAttack;
        public double? EffectiveDefense => Defense + TemporaryDefense;
        public ItemPreviewStatsKind StatsKind { get; }
        public bool RequiresStatsReroll => Star != Before.Star;
        public bool PreservesOwnershipAndPoolCopies => true;

        internal ItemCharacterChange(CharacterObservation before, int star, double? attack,
            double? defense, int temporaryAttack, int temporaryDefense, ItemPreviewStatsKind statsKind)
        {
            Before = before;
            Star = star;
            Attack = attack;
            Defense = defense;
            TemporaryAttack = temporaryAttack;
            TemporaryDefense = temporaryDefense;
            StatsKind = statsKind;
        }
    }

    /// <summary>The physical slot remains present even when projected HP is zero.</summary>
    public sealed class ItemEnemyChange
    {
        public EnemyObservation Before { get; }
        public int HP { get; }
        public int MaximumHP => HP; // Preparation observations expose the adjusted starting HP as both values.
        public int Defense { get; }
        public int PreparationHpBonus { get; }
        public int PreparationDefenseBonus { get; }
        public int DirectHpLoss => Before.HP - HP;
        public int DefenseDelta => Defense - Before.Defense;
        public bool BecameZeroHp => Before.HP > 0 && HP == 0;

        internal ItemEnemyChange(EnemyObservation before, int hp, int defense,
            int preparationHpBonus, int preparationDefenseBonus)
        {
            Before = before;
            HP = hp;
            Defense = defense;
            PreparationHpBonus = preparationHpBonus;
            PreparationDefenseBonus = preparationDefenseBonus;
        }
    }

    /// <summary>An immutable overlay. Unchanged fields and slots retain their values in Observation.</summary>
    public sealed class ItemPreviewState
    {
        public GameObservation Observation { get; }
        public int Gold { get; }
        public int HP { get; }
        public int MaximumHP => Observation.Player.MaximumHP;
        public int HandOccupied { get; }
        public int? ChangedHandItemSlot { get; }
        public ItemObservation HandItemAfter { get; }
        public EventItemStatus? EventStatus { get; }
        public string PurchasedOfferId { get; }
        public ItemCharacterChange Character { get; }
        public IReadOnlyList<ItemEnemyChange> Enemies { get; }
        public IEnumerable<int> LivingEnemySlots => Observation.Enemies
            .Where(enemy => (Enemies.FirstOrDefault(change => change.Before.SlotIndex == enemy.SlotIndex)?.HP ?? enemy.HP) > 0)
            .Select(enemy => enemy.SlotIndex);
        public bool IsEnemylessEncounter => Observation.Stage.EnemyCategory == StageEnemyCategory.None;
        public bool AllPhysicalEnemiesHaveZeroHp => Observation.Enemies.Count > 0 && !LivingEnemySlots.Any();

        internal ItemPreviewState(GameObservation observation, int gold, int hp, int handOccupied,
            EventItemStatus? eventStatus, int? changedHandSlot = null, ItemObservation handItem = null,
            string offerId = null, ItemCharacterChange character = null, ItemEnemyChange[] enemies = null)
        {
            Observation = observation;
            Gold = gold;
            HP = hp;
            HandOccupied = handOccupied;
            EventStatus = eventStatus;
            ChangedHandItemSlot = changedHandSlot;
            HandItemAfter = handItem;
            PurchasedOfferId = offerId;
            Character = character;
            Enemies = Array.AsReadOnly(enemies ?? Array.Empty<ItemEnemyChange>());
        }

        public ItemObservation GetHandItem(int slot) => ChangedHandItemSlot == slot
            ? HandItemAfter : Observation.Hand.Slots.FirstOrDefault(entry => entry.SlotIndex == slot)?.Item;

        public ShopOfferStatus GetOfferStatus(ShopOfferObservation offer) =>
            PurchasedOfferId == offer.OfferId ? ShopOfferStatus.Purchased : offer.Status;
    }

    public sealed class ItemPreviewResult
    {
        public ItemPreviewLegality Legality { get; }
        public ItemProjectionStatus ProjectionStatus { get; }
        public ItemPreviewFailure Failure { get; }
        public bool Succeeded => ProjectionStatus == ItemProjectionStatus.Projected;
        public ItemPreviewSource Source { get; }
        public ItemObservation Item { get; }
        public GameActionTarget? Target { get; }
        public int? HandSlot { get; }
        public int? ConsumedHandSlot => Target.HasValue && Source != ItemPreviewSource.Event && After != null ? HandSlot : null;
        public ItemObservation ConsumedHandItem => ConsumedHandSlot.HasValue ? Item : null;
        public ItemPreviewState Before { get; }
        /// <summary>For missing Star Stats, contains only the deterministic portion of the projection.</summary>
        public ItemPreviewState After { get; }
        public ItemPreviewState PurchaseOrigin { get; }
        public int? GoldDelta => Succeeded ? After.Gold - Before.Gold : (int?)null;
        public int? GoldDeltaIncludingPurchase => Succeeded ? After.Gold - (PurchaseOrigin ?? Before).Gold : (int?)null;
        public int? EffectiveHealing => Succeeded ? Math.Max(0, After.HP - Before.HP) : (int?)null;
        public int? ItemBlockDelta => Succeeded ? 0 : (int?)null;
        public bool? IsZeroEffect { get; }

        internal ItemPreviewResult(ItemPreviewSource source, ItemObservation item, GameActionTarget? target,
            int? handSlot, ItemPreviewState before, ItemPreviewState after,
            ItemPreviewLegality legality, ItemProjectionStatus status, ItemPreviewFailure failure,
            ItemPreviewState purchaseOrigin = null, bool? zeroEffect = null)
        {
            Source = source;
            Item = item;
            Target = target;
            HandSlot = handSlot;
            Before = before;
            After = after;
            Legality = legality;
            ProjectionStatus = status;
            Failure = failure;
            PurchaseOrigin = purchaseOrigin;
            IsZeroEffect = zeroEffect;
        }
    }

    /// <summary>Projects current public Item effects without executing a transaction or drawing Stats.</summary>
    public static class ItemEffectPreview
    {
        public static ItemPreviewResult PreviewUse(GameObservation observation, ItemPreviewSource source,
            GameActionTarget target, int? handSlot = null)
        {
            var state = Baseline(observation);
            return Use(state, source, handSlot, target, null);
        }

        public static ItemPreviewResult PreviewPurchase(GameObservation observation, string offerId, int handSlot)
        {
            var before = Baseline(observation);
            ItemPreviewResult Fail(ItemPreviewFailure reason, bool unknown = false) =>
                Failure(ItemPreviewSource.Purchased, null, null, handSlot, before, reason,
                    unknown ? ItemPreviewLegality.Unknown : ItemPreviewLegality.Illegal);
            if (before == null) return Fail(ItemPreviewFailure.MissingObservation, true);
            if (observation.Stage.Phase != ObservationPhase.Preparation) return Fail(ItemPreviewFailure.InvalidPhase);
            if (observation.Stage.ShopMode != StageShopMode.Item ||
                observation.Shop.Kind != ObservationShopKind.Item) return Fail(ItemPreviewFailure.InvalidShop);
            var offer = observation.Shop.Offers.FirstOrDefault(entry => entry.OfferId == offerId);
            if (offer == null || offer.Kind != ShopOfferKind.Item || offer.Status != ShopOfferStatus.Available)
                return Fail(ItemPreviewFailure.InvalidOffer);
            var item = offer.Item;
            if (item == null) return Fail(ItemPreviewFailure.InvalidDefinition, true);
            if (item.DefinitionId != offer.DefinitionId || item.PurchasePrice != offer.DisplayPrice || offer.DisplayPrice < 0)
                return Fail(ItemPreviewFailure.InvalidDefinition);
            var slot = observation.Hand.Slots.FirstOrDefault(entry => entry.SlotIndex == handSlot);
            if (slot == null || slot.Kind != HandSlotContentKind.Empty) return Fail(ItemPreviewFailure.HandSlotUnavailable);
            if (before.Gold < offer.DisplayPrice) return Fail(ItemPreviewFailure.InsufficientGold);

            var after = new ItemPreviewState(observation, before.Gold - offer.DisplayPrice, before.HP,
                before.HandOccupied + 1, before.EventStatus, handSlot, item, offerId);
            return Success(ItemPreviewSource.Purchased, item, null, handSlot, before, after);
        }

        public static ItemPreviewResult PreviewUseAfterPurchase(GameObservation observation,
            ItemPreviewResult purchase, GameActionTarget target)
        {
            if (purchase?.Succeeded != true || purchase.Source != ItemPreviewSource.Purchased ||
                purchase.Target.HasValue)
                return Failure(ItemPreviewSource.Purchased, null, target, null,
                    Baseline(observation), ItemPreviewFailure.InvalidPurchase);
            if (!ReferenceEquals(observation, purchase.Before.Observation))
                return Failure(ItemPreviewSource.Purchased, purchase.Item, target, purchase.HandSlot,
                    Baseline(observation), ItemPreviewFailure.DifferentObservation);

            return Use(purchase.After, ItemPreviewSource.Purchased, purchase.HandSlot, target, purchase.Before);
        }

        public static IReadOnlyList<ItemPreviewResult> EnumerateUseTargets(GameObservation observation,
            ItemPreviewSource source, int? handSlot = null) =>
            LegalTargets(EnumerateUseTargets(observation, source, handSlot, out _));

        public static IReadOnlyList<ItemPreviewResult> EnumeratePurchasedUseTargets(GameObservation observation,
            ItemPreviewResult purchase) => LegalTargets(EnumeratePurchasedUseTargets(observation, purchase, out _));

        internal static IReadOnlyList<ItemPreviewResult> EnumerateUseTargets(GameObservation observation,
            ItemPreviewSource source, int? handSlot, out ItemPreviewFailure failure) =>
            Enumerate(Baseline(observation), source, handSlot, null, out failure);

        internal static IReadOnlyList<ItemPreviewResult> EnumeratePurchasedUseTargets(GameObservation observation,
            ItemPreviewResult purchase, out ItemPreviewFailure failure)
        {
            if (purchase?.Succeeded != true || purchase.Source != ItemPreviewSource.Purchased ||
                purchase.Target.HasValue)
            {
                failure = purchase != null && !purchase.Succeeded &&
                    purchase.Source == ItemPreviewSource.Purchased && !purchase.Target.HasValue
                    ? purchase.Failure : ItemPreviewFailure.InvalidPurchase;
                return Array.Empty<ItemPreviewResult>();
            }
            if (!ReferenceEquals(observation, purchase.Before.Observation))
            {
                failure = ItemPreviewFailure.DifferentObservation;
                return Array.Empty<ItemPreviewResult>();
            }

            return Enumerate(purchase.After, ItemPreviewSource.Purchased, purchase.HandSlot, purchase.Before, out failure);
        }

        private static IReadOnlyList<ItemPreviewResult> LegalTargets(IReadOnlyList<ItemPreviewResult> targets) =>
            Array.AsReadOnly(targets.Where(result => result.Legality == ItemPreviewLegality.Legal).ToArray());

        private static IReadOnlyList<ItemPreviewResult> Enumerate(ItemPreviewState state, ItemPreviewSource source,
            int? handSlot, ItemPreviewState purchaseOrigin, out ItemPreviewFailure failure)
        {
            failure = ValidateSource(state, source, handSlot, purchaseOrigin, out var item);
            if (failure != ItemPreviewFailure.None) return Array.Empty<ItemPreviewResult>();
            var observation = state.Observation;
            var targets = new List<GameActionTarget>();
            switch (item?.Target)
            {
                case ItemEffectTarget.OneAlly:
                    targets.AddRange(observation.Board.Slots.Where(slot => slot.IsOccupied)
                        .Select(slot => new GameActionTarget(ItemUseTargetKind.BoardCharacter, slot.SlotIndex, 0, true)));
                    if (source == ItemPreviewSource.Event)
                        targets.AddRange(observation.Hand.Slots.Where(slot => slot.Character != null)
                            .Select(slot => new GameActionTarget(ItemUseTargetKind.HandCharacter, slot.SlotIndex, 0, true)));
                    break;
                case ItemEffectTarget.AllEnemies:
                    targets.Add(new GameActionTarget(ItemUseTargetKind.EnemyArea, -1, observation.Enemies.Count, true));
                    break;
                case ItemEffectTarget.Player:
                    if (observation.Board.Slots.Count > 0)
                        targets.Add(new GameActionTarget(ItemUseTargetKind.PlayerArea, observation.Board.Slots[0].SlotIndex, 0, true));
                    break;
            }
            return Array.AsReadOnly(targets.Select(target => Use(state, source, handSlot, target, purchaseOrigin)).ToArray());
        }

        private static ItemPreviewResult Use(ItemPreviewState before, ItemPreviewSource source,
            int? handSlot, GameActionTarget target, ItemPreviewState purchaseOrigin)
        {
            ItemObservation item = null;
            ItemPreviewResult Fail(ItemPreviewFailure reason, ItemPreviewLegality legality = ItemPreviewLegality.Illegal) =>
                Failure(source, item, target, handSlot, before, reason, legality, purchaseOrigin);
            var failure = ValidateSource(before, source, handSlot, purchaseOrigin, out item);
            if (failure != ItemPreviewFailure.None)
                return Fail(failure, failure == ItemPreviewFailure.MissingObservation || failure == ItemPreviewFailure.UnsupportedEffect
                    ? ItemPreviewLegality.Unknown : ItemPreviewLegality.Illegal);
            bool isEvent = source == ItemPreviewSource.Event;

            var observation = before.Observation;

            ItemCharacterChange character = null;
            ItemEnemyChange[] enemies = null;
            int hp = before.HP;
            bool zeroEffect;
            try
            {
                switch (item.Target)
                {
                    case ItemEffectTarget.OneAlly:
                        var ally = FindAlly(observation, isEvent, target);
                        if (ally == null) return Fail(ItemPreviewFailure.InvalidTarget);
                        var star = item.Effects.FirstOrDefault(effect => effect.Kind == ItemEffectKind.Star);
                        if (star != null && !ItemEffectRules.CanChangeStar(ally.Star, ally.IsStarTemporary(),
                            star.Value, item.Duration == ItemEffectDuration.ThisBattle))
                            return Fail(ItemPreviewFailure.InvalidTarget);
                        character = ProjectAlly(observation, ally, item, star);
                        if (character.StatsKind == ItemPreviewStatsKind.Unavailable)
                            return new ItemPreviewResult(source, item, target, handSlot, before,
                                AfterUse(before, handSlot, isEvent, hp, character, null), ItemPreviewLegality.Legal,
                                ItemProjectionStatus.Unavailable, ItemPreviewFailure.StatsUnavailable, purchaseOrigin);
                        zeroEffect = character.Star == ally.Star && character.Attack == ally.Attack &&
                            character.Defense == ally.Defense && character.TemporaryAttack == ally.TemporaryAttackBonus &&
                            character.TemporaryDefense == ally.TemporaryDefenseBonus;
                        break;
                    case ItemEffectTarget.AllEnemies:
                        if (target.Kind != ItemUseTargetKind.EnemyArea || target.SlotIndex != -1 || before.IsEnemylessEncounter)
                            return Fail(ItemPreviewFailure.InvalidTarget);
                        if (observation.Enemies.Count == 0)
                            return observation.Stage.EnemyCategory.HasValue
                                ? Fail(ItemPreviewFailure.InvalidTarget)
                                : Fail(ItemPreviewFailure.EnemyInformationUnavailable, ItemPreviewLegality.Unknown);
                        // The dispatcher derives its own physical enemy count rather than trusting the payload.
                        enemies = observation.Enemies.Select(enemy => ProjectEnemy(enemy, item)).ToArray();
                        zeroEffect = enemies.All(enemy => enemy.DirectHpLoss == 0 && enemy.DefenseDelta == 0);
                        break;
                    default: // Player
                        if (target.Kind != ItemUseTargetKind.PlayerArea ||
                            !observation.Board.Slots.Any(slot => slot.SlotIndex == target.SlotIndex))
                            return Fail(ItemPreviewFailure.InvalidTarget);
                        if (!observation.Player.HasHealth) return Fail(ItemPreviewFailure.HealthUnavailable);
                        foreach (var effect in item.Effects)
                            hp = ItemEffectRules.ApplyPlayerHpEffect(hp, before.MaximumHP, effect.Value);
                        zeroEffect = hp == before.HP;
                        break;
                }
            }
            catch (OverflowException)
            {
                return Fail(ItemPreviewFailure.ArithmeticOverflow);
            }

            return Success(source, item, target, handSlot, before,
                AfterUse(before, handSlot, isEvent, hp, character, enemies), purchaseOrigin, zeroEffect);
        }

        private static ItemCharacterChange ProjectAlly(GameObservation observation, CharacterObservation ally,
            ItemObservation item, ItemEffectObservation star)
        {
            if (star != null)
            {
                var stats = observation.CharacterStats.Get(ally.RunCost, star.Value);
                return new ItemCharacterChange(ally, star.Value, stats.ExpectedAttack, stats.ExpectedDefense,
                    ally.TemporaryAttackBonus, ally.TemporaryDefenseBonus,
                    stats.IsAvailable ? ItemPreviewStatsKind.PublicMean : ItemPreviewStatsKind.Unavailable);
            }

            int attack = ally.Attack, defense = ally.Defense;
            int tempAttack = ally.TemporaryAttackBonus, tempDefense = ally.TemporaryDefenseBonus;
            foreach (var effect in item.Effects)
            {
                if (effect.Kind == ItemEffectKind.Attack)
                {
                    if (item.Duration == ItemEffectDuration.ThisBattle) tempAttack = checked(tempAttack + effect.Value);
                    else attack = checked(attack + effect.Value);
                }
                else
                {
                    if (item.Duration == ItemEffectDuration.ThisBattle) tempDefense = checked(tempDefense + effect.Value);
                    else defense = checked(defense + effect.Value);
                }
            }
            return new ItemCharacterChange(ally, ally.Star, attack, defense, tempAttack, tempDefense,
                ItemPreviewStatsKind.Deterministic);
        }

        private static ItemEnemyChange ProjectEnemy(EnemyObservation enemy, ItemObservation item)
        {
            int hpBonus = enemy.SlotBonusHP;
            int defBonus = enemy.SlotBonusDefense;
            foreach (var effect in item.Effects)
            {
                if (effect.Kind == ItemEffectKind.Defense) defBonus = checked(defBonus + effect.Value);
                else hpBonus = checked(hpBonus - effect.Value);
            }
            int defense = checked(enemy.Defense + (defBonus - enemy.SlotBonusDefense));
            int hp = Math.Max(0, checked(enemy.HP + (hpBonus - enemy.SlotBonusHP)));
            return new ItemEnemyChange(enemy, hp, defense, hpBonus, defBonus);
        }

        private static ItemPreviewFailure ValidateSource(ItemPreviewState state, ItemPreviewSource source,
            int? handSlot, ItemPreviewState purchaseOrigin, out ItemObservation item)
        {
            item = null;
            if (state == null) return ItemPreviewFailure.MissingObservation;
            bool isEvent = source == ItemPreviewSource.Event;
            if ((isEvent && handSlot.HasValue) ||
                (!isEvent && (!handSlot.HasValue || (source != ItemPreviewSource.Hand && purchaseOrigin == null))))
                return ItemPreviewFailure.InvalidSource;
            if (state.Observation.Stage.Phase != (isEvent ? ObservationPhase.EventPending : ObservationPhase.Preparation))
                return ItemPreviewFailure.InvalidPhase;
            if (isEvent && state.EventStatus != EventItemStatus.Available)
                return ItemPreviewFailure.ItemUnavailable;
            item = SourceItem(state, source, handSlot);
            if (item == null) return ItemPreviewFailure.ItemUnavailable;
            if (!ValidDefinition(item)) return ItemPreviewFailure.InvalidDefinition;
            if (isEvent && item.Target != ItemEffectTarget.OneAlly) return ItemPreviewFailure.InvalidTarget;
            return Supported(item) ? ItemPreviewFailure.None : ItemPreviewFailure.UnsupportedEffect;
        }

        private static bool ValidDefinition(ItemObservation item) =>
            !string.IsNullOrWhiteSpace(item.DefinitionId) && item.PurchasePrice >= 0 &&
            Enum.IsDefined(typeof(ItemEffectDuration), item.Duration) && item.Duration != ItemEffectDuration.Unspecified &&
            Enum.IsDefined(typeof(ItemEffectTarget), item.Target) && item.Target != ItemEffectTarget.Unspecified &&
            item.Effects.Count > 0 && item.Effects.All(effect => effect != null &&
                Enum.IsDefined(typeof(ItemEffectKind), effect.Kind) &&
                ItemEffectRules.IsSupported(item.Target, effect.Kind) &&
                (effect.Kind != ItemEffectKind.Star || (effect.Value >= 1 && effect.Value <= 3)));

        // Accept the effect shapes used by the seven public Items, without keying on IDs or magnitudes.
        private static bool Supported(ItemObservation item)
        {
            switch (item.Target)
            {
                case ItemEffectTarget.OneAlly:
                    if (item.Effects.Any(effect => effect.Kind == ItemEffectKind.Star))
                        return item.Effects.Count == 1 && item.Duration == ItemEffectDuration.Immediate;
                    return item.Effects.All(effect => effect.Kind == ItemEffectKind.Attack || effect.Kind == ItemEffectKind.Defense);
                case ItemEffectTarget.AllEnemies:
                    return item.Duration == ItemEffectDuration.ThisBattle && item.Effects.All(effect =>
                        effect.Kind == ItemEffectKind.Defense || (effect.Kind == ItemEffectKind.Damage && effect.Value >= 0));
                case ItemEffectTarget.Player:
                    return item.Duration == ItemEffectDuration.Immediate && item.Effects.All(effect =>
                        effect.Kind == ItemEffectKind.Damage && effect.Value <= 0);
                default:
                    return false;
            }
        }

        private static CharacterObservation FindAlly(GameObservation observation, bool isEvent, GameActionTarget target)
        {
            switch (target.Kind)
            {
                case ItemUseTargetKind.BoardCharacter:
                    return observation.Board.Slots.FirstOrDefault(slot => slot.SlotIndex == target.SlotIndex)?.Character;
                case ItemUseTargetKind.HandCharacter when isEvent:
                    return observation.Hand.Slots.FirstOrDefault(slot => slot.SlotIndex == target.SlotIndex)?.Character;
                default:
                    return null;
            }
        }

        private static ItemObservation SourceItem(ItemPreviewState state, ItemPreviewSource source, int? handSlot) =>
            source == ItemPreviewSource.Event ? state.Observation.EventItem?.Item :
            handSlot.HasValue ? state.GetHandItem(handSlot.Value) : null;

        private static ItemPreviewState Baseline(GameObservation observation) => observation == null ? null :
            new ItemPreviewState(observation, observation.Player.Gold, observation.Player.HP,
                observation.Hand.SlotCount - observation.Hand.EmptySlotCount, observation.EventItem?.Status);

        private static ItemPreviewState AfterUse(ItemPreviewState before, int? handSlot, bool isEvent, int hp,
            ItemCharacterChange character, ItemEnemyChange[] enemies) =>
            new ItemPreviewState(before.Observation, before.Gold, hp, before.HandOccupied - (isEvent ? 0 : 1),
                isEvent ? EventItemStatus.Consumed : before.EventStatus, handSlot, offerId: before.PurchasedOfferId,
                character: character, enemies: enemies);

        private static ItemPreviewResult Success(ItemPreviewSource source, ItemObservation item, GameActionTarget? target,
            int? handSlot, ItemPreviewState before, ItemPreviewState after, ItemPreviewState purchaseOrigin = null,
            bool? zeroEffect = null) =>
            new ItemPreviewResult(source, item, target, handSlot, before, after, ItemPreviewLegality.Legal,
                ItemProjectionStatus.Projected, ItemPreviewFailure.None, purchaseOrigin, zeroEffect);

        private static ItemPreviewResult Failure(ItemPreviewSource source, ItemObservation item, GameActionTarget? target,
            int? handSlot, ItemPreviewState before, ItemPreviewFailure failure,
            ItemPreviewLegality legality = ItemPreviewLegality.Illegal, ItemPreviewState purchaseOrigin = null) =>
            new ItemPreviewResult(source, item, target, handSlot, before, null, legality,
                failure == ItemPreviewFailure.UnsupportedEffect ? ItemProjectionStatus.Unsupported :
                legality == ItemPreviewLegality.Illegal ? ItemProjectionStatus.NotApplicable : ItemProjectionStatus.Unavailable,
                failure, purchaseOrigin);
    }
}
