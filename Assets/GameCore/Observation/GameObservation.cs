using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Observation
{
    public enum ObservationLocationKind { Board, Hand }
    public enum ObservationShopKind { None, Character, Item }
    public enum ObservationPhase
    {
        AwaitingEncounterStart, AwaitingStageChoice, Preparation, Battle,
        RewardPending, EventPending, Completed, GameOver
    }

    public sealed class CharacterObservation
    {
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int RunCost { get; }
        public int Star { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int TemporaryAttackBonus { get; }
        public int TemporaryDefenseBonus { get; }
        public IReadOnlyList<string> Units { get; }
        public IReadOnlyList<string> Attributes { get; }
        public ObservationLocationKind LocationKind { get; }
        public int SlotIndex { get; }
        private readonly bool _temporaryStar;

        // Use an accessor to preserve the existing property-based observation fingerprint.
        public bool IsStarTemporary() => _temporaryStar;

        internal CharacterObservation(CharacterInstanceView instance, CharacterDefinition definition,
            ObservationLocationKind locationKind, int slotIndex)
        {
            InstanceId = instance.InstanceId; DefinitionId = instance.DefinitionId;
            DisplayName = definition.CharacterName; RunCost = instance.RunCost; Star = instance.Star;
            Attack = instance.Attack; Defense = instance.Defense;
            TemporaryAttackBonus = instance.TemporaryAttackBonus;
            TemporaryDefenseBonus = instance.TemporaryDefenseBonus;
            _temporaryStar = instance.HasTemporaryStar;
            // CharacterDefinition owns defensive copies wrapped as read-only collections.
            // Sharing those immutable definition values avoids two arrays and two wrappers
            // per observed character without sharing mutable instance state.
            Units = definition.Units; Attributes = definition.Attributes;
            LocationKind = locationKind; SlotIndex = slotIndex;
        }
    }

    public sealed class BoardSlotObservation
    {
        public int SlotIndex { get; }
        public CharacterObservation Character { get; }
        internal BoardSlotObservation(int slotIndex, CharacterObservation character)
        { SlotIndex = slotIndex; Character = character; }
        public bool IsOccupied => Character != null;
    }

    public sealed class BoardObservation
    {
        public int Columns { get; }
        public int Rows { get; }
        public IReadOnlyList<BoardSlotObservation> Slots { get; }
        internal BoardObservation(int columns, int rows, BoardSlotObservation[] slots)
        { Columns = columns; Rows = rows; Slots = Array.AsReadOnly(slots); }
    }

    public sealed class ItemEffectObservation
    {
        public ItemEffectKind Kind { get; }
        public int Value { get; }
        public ItemEffectTarget Target { get; }
        public ItemEffectDuration Duration { get; }
        internal ItemEffectObservation(ItemEffectDefinition effect, ItemEffectTarget target, ItemEffectDuration duration)
        { Kind = effect.Kind; Value = effect.Value; Target = target; Duration = duration; }
    }

    public sealed class ItemObservation
    {
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int PurchasePrice { get; }
        public string Description { get; }
        public ItemEffectTarget Target { get; }
        public ItemEffectDuration Duration { get; }
        public IReadOnlyList<ItemEffectObservation> Effects { get; }
        internal ItemObservation(ItemDefinition item)
        {
            DefinitionId = item.DefinitionId; DisplayName = item.DisplayName;
            PurchasePrice = item.PurchasePrice; Description = item.Description;
            Target = item.Target; Duration = item.Duration;
            var effects = new ItemEffectObservation[item.Effects.Count];
            for (int i = 0; i < effects.Length; i++)
                effects[i] = new ItemEffectObservation(item.Effects[i], item.Target, item.Duration);
            Effects = Array.AsReadOnly(effects);
        }
    }

    public sealed class HandSlotObservation
    {
        public int SlotIndex { get; }
        public HandSlotContentKind Kind { get; }
        public CharacterObservation Character { get; }
        public ItemObservation Item { get; }
        internal HandSlotObservation(int slotIndex, HandSlotContentKind kind,
            CharacterObservation character, ItemObservation item)
        { SlotIndex = slotIndex; Kind = kind; Character = character; Item = item; }
    }

    public sealed class HandObservation
    {
        public int SlotCount { get; }
        public int EmptySlotCount { get; }
        public IReadOnlyList<HandSlotObservation> Slots { get; }
        internal HandObservation(int slotCount, int emptySlotCount, HandSlotObservation[] slots)
        { SlotCount = slotCount; EmptySlotCount = emptySlotCount; Slots = Array.AsReadOnly(slots); }
    }

    public sealed class PlayerObservation
    {
        public bool HasHealth { get; }
        public int HP { get; }
        public int MaximumHP { get; }
        public int Gold { get; }
        public int Level { get; }
        public int XP { get; }
        public int RequiredXP { get; }
        public int MaximumLevel { get; }
        public int PermanentPlayerBlockBonus { get; }
        public int TemporaryPlayerBlockBonus { get; }
        public int TemporaryPlayerHPDelta { get; }
        internal PlayerObservation(EconomyState economy, int requiredXP, ItemEffectState effects)
        {
            HasHealth = economy.HasHealth; HP = economy.CurrentHP; MaximumHP = economy.MaximumHP;
            Gold = economy.Gold; Level = economy.Level; XP = economy.XP;
            RequiredXP = requiredXP; MaximumLevel = economy.MaximumLevel;
            PermanentPlayerBlockBonus = effects == null ? 0 : effects.PermanentPlayerBlockBonus;
            TemporaryPlayerBlockBonus = effects == null ? 0 : effects.TemporaryPlayerBlockBonus;
            TemporaryPlayerHPDelta = effects == null ? 0 : effects.TemporaryPlayerHpDelta;
        }
    }

    /// <summary>Public, rule-derived Economy choices; contains no random checkpoint.</summary>
    public sealed class EconomyRulesObservation
    {
        public int ExperiencePurchaseGoldCost { get; }
        public int ExperiencePerPurchase { get; }
        public ExperienceCurve ExperienceCurve { get; }
        public int MaximumLevel { get; }
        public bool CanPurchaseExperience { get; }
        public int? ShopRerollGoldCost { get; }
        public bool CanRerollShop { get; }
        public int GoldPerInterestThreshold { get; }
        public int InterestGoldPerThreshold { get; }
        public int MaximumInterestGold { get; }
        public decimal ExpectedInterestAtCurrentGold { get; }
        public int BaseStageGoldReward { get; }
        public int EmptyHandSlotsPerBonusGold { get; }
        public int BonusGoldPerEmptyHandSlots { get; }

        internal EconomyRulesObservation(EconomyState state, EconomyRulesConfiguration config,
            bool selectB, int? rerollCost, bool canReroll)
        {
            ExperiencePurchaseGoldCost = EconomyRules.GetExperiencePurchaseGoldCost(config, selectB);
            ExperiencePerPurchase = config.ExperiencePerPurchase;
            ExperienceCurve = config.ExperienceCurve;
            MaximumLevel = state.MaximumLevel;
            CanPurchaseExperience = EconomyRules.CanPurchaseExperience(state, config, selectB);
            ShopRerollGoldCost = rerollCost;
            CanRerollShop = canReroll && rerollCost.HasValue && state.Gold >= rerollCost.Value;
            GoldPerInterestThreshold = config.GoldPerInterestThreshold;
            InterestGoldPerThreshold = config.InterestGoldPerThreshold;
            MaximumInterestGold = config.MaximumInterestGold;
            ExpectedInterestAtCurrentGold = EconomyRules.CalculateExpectedInterestGold(state.Gold, config);
            BaseStageGoldReward = config.BaseStageGoldReward;
            EmptyHandSlotsPerBonusGold = config.EmptyHandSlotsPerBonusGold;
            BonusGoldPerEmptyHandSlots = config.BonusGoldPerEmptyHandSlots;
        }
    }

    public sealed class CharacterStatRangeObservation
    {
        public bool IsAvailable { get; }
        public int MinimumAttack { get; }
        public int MaximumAttack { get; }
        public int MinimumDefense { get; }
        public int MaximumDefense { get; }
        internal CharacterStatRangeObservation(bool available, int minAttack, int maxAttack,
            int minDefense, int maxDefense)
        { IsAvailable = available; MinimumAttack = minAttack; MaximumAttack = maxAttack;
          MinimumDefense = minDefense; MaximumDefense = maxDefense; }
    }

    /// <summary>Stable per-cost weights shown by the shop UI, not pool-conditioned odds.</summary>
    public sealed class ShopCostWeightObservation
    {
        public int StageNumber { get; }
        public IReadOnlyList<int> CharactersPerCost { get; }
        public int Cost1Weight { get; }
        public int Cost2Weight { get; }
        public int Cost3Weight { get; }
        public int Cost4Weight { get; }
        public int Cost5Weight { get; }
        public int TotalConfiguredWeight { get; }
        internal ShopCostWeightObservation(int stage, CharacterShopConfiguration config)
        {
            CharactersPerCost = config.CharactersPerCost;
            StageNumber = stage; Cost1Weight = config.GetCostWeight(stage, 1);
            Cost2Weight = config.GetCostWeight(stage, 2); Cost3Weight = config.GetCostWeight(stage, 3);
            Cost4Weight = config.GetCostWeight(stage, 4); Cost5Weight = config.GetCostWeight(stage, 5);
            TotalConfiguredWeight = checked(Cost1Weight + Cost2Weight + Cost3Weight + Cost4Weight + Cost5Weight);
        }
    }

    public sealed class RosterDefinitionObservation
    {
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> Units { get; }
        public IReadOnlyList<string> Attributes { get; }
        internal RosterDefinitionObservation(CharacterDefinition definition)
        { DefinitionId = definition.DefinitionId; DisplayName = definition.CharacterName;
          Units = definition.Units; Attributes = definition.Attributes; }
    }

    public enum PublicCharacterStatsStatus { MissingConfiguration, NoPositiveWeight, InvalidWeights, Available }

    public readonly struct PublicCharacterStats
    {
        public int RunCost { get; }
        public int Star { get; }
        public PublicCharacterStatsStatus Status { get; }
        public bool IsAvailable => Status == PublicCharacterStatsStatus.Available;
        public int? MinimumAttack { get; }
        public int? MaximumAttack { get; }
        public int? MinimumDefense { get; }
        public int? MaximumDefense { get; }
        public double? ExpectedAttack { get; }
        public double? ExpectedDefense { get; }

        internal PublicCharacterStats(int cost, int star, PublicCharacterStatsStatus status,
            int? minAttack = null, int? maxAttack = null, int? minDefense = null, int? maxDefense = null,
            double? expectedAttack = null, double? expectedDefense = null)
        {
            RunCost = cost;
            Star = star;
            Status = status;
            MinimumAttack = minAttack;
            MaximumAttack = maxAttack;
            MinimumDefense = minDefense;
            MaximumDefense = maxDefense;
            ExpectedAttack = expectedAttack;
            ExpectedDefense = expectedDefense;
        }
    }

    public sealed class PublicCharacterStatsCatalog
    {
        private readonly Dictionary<(int Cost, int Star), PublicCharacterStats> _entries =
            new Dictionary<(int, int), PublicCharacterStats>();
        internal IEnumerable<PublicCharacterStats> Entries => _entries.Values;

        internal PublicCharacterStatsCatalog(CharacterStatDefinition definition)
        {
            if (definition == null) return;
            var costs = new HashSet<int>();
            foreach (int cost in definition.ConfiguredCosts)
            {
                if (cost <= 0 || !costs.Add(cost)) continue;
                for (int star = 1; star <= 3; star++)
                    if (definition.TryGetOptions(cost, star, out var options))
                        _entries.Add((cost, star), Summarize(cost, star, options));
            }
        }

        public PublicCharacterStats Get(int runCost, int star) =>
            _entries.TryGetValue((runCost, star), out var stats) ? stats :
                new PublicCharacterStats(runCost, star, PublicCharacterStatsStatus.MissingConfiguration);

        public PublicCharacterStats Get(PurchaseStatRoll roll)
        {
            if (roll == null) throw new ArgumentNullException(nameof(roll));
            return Get(roll.RunCost, roll.Star);
        }

        private static PublicCharacterStats Summarize(int cost, int star, IReadOnlyList<CharacterStatOption> options)
        {
            long weight = 0, attack = 0, defense = 0;
            int minAttack = int.MaxValue, minDefense = int.MaxValue;
            int maxAttack = int.MinValue, maxDefense = int.MinValue;
            foreach (CharacterStatOption option in options)
            {
                // Match the generator's valid draw interval, before accumulating products.
                if (option.Weight < 0 || weight + option.Weight > int.MaxValue)
                    return new PublicCharacterStats(cost, star, PublicCharacterStatsStatus.InvalidWeights);
                if (option.Weight == 0) continue;
                weight += option.Weight;
                attack += (long)option.Attack * option.Weight;
                defense += (long)option.Defense * option.Weight;
                minAttack = Math.Min(minAttack, option.Attack);
                maxAttack = Math.Max(maxAttack, option.Attack);
                minDefense = Math.Min(minDefense, option.Defense);
                maxDefense = Math.Max(maxDefense, option.Defense);
            }
            if (weight == 0) return new PublicCharacterStats(cost, star, PublicCharacterStatsStatus.NoPositiveWeight);
            return new PublicCharacterStats(cost, star, PublicCharacterStatsStatus.Available,
                minAttack, maxAttack, minDefense, maxDefense, (double)attack / weight, (double)defense / weight);
        }
    }

    public sealed class UnitRequirementObservation
    {
        public string UnitName { get; }
        public IReadOnlyList<string> RequiredDefinitionIds { get; }
        public int RequiredMemberCount => RequiredDefinitionIds.Count;

        internal UnitRequirementObservation(string unitName, string[] definitionIds)
        {
            UnitName = unitName;
            RequiredDefinitionIds = Array.AsReadOnly(definitionIds);
        }
    }

    public sealed class StageChoiceObservation
    {
        public StageChoice Choice { get; }
        public bool IsAvailable { get; }
        public StageEnemyCategory EnemyCategory { get; }
        public StageShopMode ShopMode { get; }
        public int ChoiceGoldReward { get; }
        public int ChoiceXPReward { get; }
        public StageSelectionEffectKind SelectionEffect { get; }
        public StageSelectionEffectTiming SelectionEffectTiming { get; }
        public int SelectionEffectAmount { get; }
        public bool SelectionEffectCapsAtMaximum { get; }
        public int ExperiencePurchaseGoldCost { get; }
        public bool CanReroll { get; }
        public int? PostBattleGold { get; }
        public int? PostBattleXP { get; }
        public bool BossRewardOverridesChoice { get; }
        public int? BossGoldReward { get; }
        public int? BossXPReward { get; }

        internal StageChoiceObservation(StageChoice choice, StageSelectionOutcome outcome,
            bool available, bool bossRewardOverrides, EconomyRulesConfiguration economy,
            StageRewardConfiguration rewards)
        {
            Choice = choice; IsAvailable = available; EnemyCategory = outcome.EnemyCategory;
            ShopMode = outcome.ShopMode; ChoiceGoldReward = outcome.GoldReward;
            ChoiceXPReward = outcome.ExperienceReward;
            SelectionEffect = outcome.SelectionEffect == null ? StageSelectionEffectKind.None : outcome.SelectionEffect.Kind;
            SelectionEffectTiming = outcome.SelectionEffect == null ? StageSelectionEffectTiming.None : outcome.SelectionEffect.Timing;
            SelectionEffectAmount = outcome.SelectionEffect == null ? 0 : outcome.SelectionEffect.Amount;
            SelectionEffectCapsAtMaximum = outcome.SelectionEffect != null && outcome.SelectionEffect.CapAtInitialMaximum;
            ExperiencePurchaseGoldCost = EconomyRules.GetExperiencePurchaseGoldCost(economy,
                choice == StageChoice.SelectB);
            CanReroll = outcome.CanReroll;
            BossRewardOverridesChoice = bossRewardOverrides;
            BossGoldReward = rewards == null ? (int?)null : rewards.BossGoldReward;
            BossXPReward = rewards == null ? (int?)null : rewards.BossExperienceReward;
            PostBattleGold = bossRewardOverrides ? (rewards == null ? (int?)null : rewards.BossGoldReward) :
                (choice == StageChoice.SelectA ? economy.BaseStageGoldReward : outcome.GoldReward);
            PostBattleXP = bossRewardOverrides ? (rewards == null ? (int?)null : rewards.BossExperienceReward) : outcome.ExperienceReward;
        }
    }

    public sealed class StageObservation
    {
        public int EncounterIndex { get; }
        public int StageNumber { get; }
        public int RoundNumber { get; }
        public ObservationPhase Phase { get; }
        public bool HasSelection { get; }
        public StageChoice? Choice { get; }
        public StageEnemyCategory? EnemyCategory { get; }
        public StageShopMode? ShopMode { get; }
        public bool CanReroll { get; }
        public bool SelectionEffectApplied { get; }
        public bool HasPendingReward { get; }
        public bool IsBossReward { get; }
        public bool IsFinalVictory { get; }
        public IReadOnlyList<StageChoice> AvailableChoices { get; }
        public IReadOnlyList<StageChoiceObservation> ChoiceDetails { get; }
        public StageChoiceObservation SelectedChoiceDetails { get; }
        internal StageObservation(StageProgressState state, StageProgressConfig config,
            EconomyRulesConfiguration economy, StageRewardConfiguration rewards)
        {
            EncounterIndex = state.EncounterIndex;
            StageNumber = config.GetStageNumber(state.EncounterIndex);
            RoundNumber = config.GetRoundNumber(state.EncounterIndex);
            Phase = (ObservationPhase)state.Phase;
            HasSelection = state.CurrentSelection != null;
            Choice = state.CurrentSelection == null ? (StageChoice?)null : state.CurrentSelection.Choice;
            EnemyCategory = state.CurrentSelection == null ? (StageEnemyCategory?)null : state.CurrentSelection.EnemyCategory;
            ShopMode = state.CurrentSelection == null ? (StageShopMode?)null : state.CurrentSelection.ShopMode;
            CanReroll = state.CurrentSelection != null && state.CurrentSelection.CanReroll;
            SelectionEffectApplied = state.SelectionEffectApplied;
            HasPendingReward = state.PendingReward != null;
            IsBossReward = state.PendingReward != null && state.PendingReward.IsBossReward;
            IsFinalVictory = state.PendingReward != null && state.PendingReward.IsFinalVictory;
            AvailableChoices = state.Phase == StageProgressPhase.AwaitingStageChoice
                ? Array.AsReadOnly(new[] { StageChoice.SelectA, StageChoice.SelectB, StageChoice.SelectC, StageChoice.SelectD })
                : Array.AsReadOnly(Array.Empty<StageChoice>());
            bool isChoicePhase = state.Phase == StageProgressPhase.AwaitingStageChoice;
            bool boss = RoundNumber == config.BossRoundNumber;
            if (isChoicePhase)
            {
                var choices = new StageChoiceObservation[4];
                for (int i = 0; i < choices.Length; i++)
                {
                    StageChoice choice = (StageChoice)i;
                    choices[i] = new StageChoiceObservation(choice,
                        StageSelectionRules.CreateOutcome(config, state.EncounterIndex, choice), true, boss, economy, rewards);
                }
                ChoiceDetails = Array.AsReadOnly(choices);
            }
            else ChoiceDetails = Array.AsReadOnly(Array.Empty<StageChoiceObservation>());
            if (state.CurrentSelection != null)
                SelectedChoiceDetails = new StageChoiceObservation(state.CurrentSelection.Choice,
                    state.CurrentSelection, false, boss, economy, rewards);
        }
    }

    public sealed class ShopOfferObservation
    {
        public string OfferId { get; }
        public ShopOfferKind Kind { get; }
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int DisplayPrice { get; }
        public int? CharacterCost { get; }
        public ShopOfferStatus Status { get; }
        public int OfferIndex { get; }
        public int OfferSlotIndex => OfferIndex;
        public int? RunCost => Kind == ShopOfferKind.Character ? CharacterCost : null;
        public IReadOnlyList<string> Units { get; }
        public IReadOnlyList<string> Attributes { get; }
        public CharacterStatRangeObservation CharacterStats { get; }
        public ItemObservation Item { get; }
        internal ShopOfferObservation(ShopOffer offer, CharacterDefinition character,
            ItemObservation item, CharacterStatDefinition stats, int offerIndex)
        {
            OfferId = offer.OfferId; Kind = offer.Kind; DefinitionId = offer.DefinitionId;
            DisplayName = character != null ? character.CharacterName : item != null ? item.DisplayName : string.Empty;
            DisplayPrice = offer.DisplayPrice; CharacterCost = offer.CharacterCost; Status = offer.Status; OfferIndex = offerIndex;
            if (character != null)
            {
                Units = character.Units; Attributes = character.Attributes;
                CharacterStats = BuildStatRange(stats, offer.CharacterCost ?? 0);
            }
            Item = item;
        }

        private static CharacterStatRangeObservation BuildStatRange(CharacterStatDefinition stats, int cost)
        {
            if (stats == null || !stats.TryGetOptions(cost, 1, out IReadOnlyList<CharacterStatOption> options))
                return new CharacterStatRangeObservation(false, 0, 0, 0, 0);
            int minA = int.MaxValue, maxA = int.MinValue, minD = int.MaxValue, maxD = int.MinValue;
            for (int i = 0; i < options.Count; i++)
            {
                CharacterStatOption option = options[i];
                if (option.Weight <= 0) continue;
                minA = Math.Min(minA, option.Attack); maxA = Math.Max(maxA, option.Attack);
                minD = Math.Min(minD, option.Defense); maxD = Math.Max(maxD, option.Defense);
            }
            return minA == int.MaxValue
                ? new CharacterStatRangeObservation(false, 0, 0, 0, 0)
                : new CharacterStatRangeObservation(true, minA, maxA, minD, maxD);
        }
    }

    public sealed class ShopObservation
    {
        public ObservationShopKind Kind { get; }
        public int SlotCount { get; }
        public int StageNumber { get; }
        public int RoundNumber { get; }
        public bool IsEventStage { get; }
                public IReadOnlyList<ShopOfferObservation> Offers { get; }
        public ShopCostWeightObservation CostWeights { get; }
        internal ShopObservation(ShopKind kind, int slotCount, int stage, int round, bool isEventStage,
            ShopOfferObservation[] offers, ShopCostWeightObservation costWeights)
        { Kind = (ObservationShopKind)kind; SlotCount = slotCount; StageNumber = stage; RoundNumber = round; IsEventStage = isEventStage;
          Offers = Array.AsReadOnly(offers); CostWeights = costWeights; }
    }

    public sealed class EventItemObservation
    {
        public ItemObservation Item { get; }
        public EventItemStatus Status { get; }
        public bool IsAvailable { get; }
        internal EventItemObservation(ItemObservation item, EventItemState state)
        { Item = item; Status = state.Status; IsAvailable = state.IsAvailable; }
    }

    public sealed class SynergyUnitObservation
    {
        public string UnitName { get; }
        public IReadOnlyList<string> ParticipantInstanceIds { get; }
        public IReadOnlyList<int> ParticipantSlots { get; }
        internal SynergyUnitObservation(UnitSynergyResult source)
        { UnitName = source.UnitName; ParticipantInstanceIds = Copy(source.ParticipantInstanceIds); ParticipantSlots = Copy(source.ParticipantBoardSlotIndexes); }
        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> src)
        { var a = new T[src.Count]; for (int i=0;i<a.Length;i++) a[i]=src[i]; return Array.AsReadOnly(a); }
    }

    public sealed class SynergyAttributeObservation
    {
        public string AttributeName { get; }
        public IReadOnlyList<string> ParticipantInstanceIds { get; }
        public IReadOnlyList<int> ParticipantSlots { get; }
        public int BlockBonus { get; }
        internal SynergyAttributeObservation(AttributeSynergyResult source)
        { AttributeName = source.AttributeName; BlockBonus = source.BlockBonus;
          ParticipantInstanceIds = Copy(source.ParticipantInstanceIds); ParticipantSlots = Copy(source.ParticipantBoardSlotIndexes); }
        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> src)
        { var a = new T[src.Count]; for (int i=0;i<a.Length;i++) a[i]=src[i]; return Array.AsReadOnly(a); }
    }

    public sealed class SynergyObservation
    {
        public bool IsAvailable { get; }
        public int AttributeBlockBonus { get; }
        public IReadOnlyList<SynergyUnitObservation> Units { get; }
        public IReadOnlyList<SynergyAttributeObservation> Attributes { get; }
        internal SynergyObservation(SynergyDetectionResult result)
        {
            IsAvailable = result != null && result.IsValid;
            AttributeBlockBonus = IsAvailable ? result.AttributeBlockBonus : 0;
            var units = new SynergyUnitObservation[IsAvailable ? result.EstablishedUnits.Count : 0];
            for (int i=0;i<units.Length;i++) units[i] = new SynergyUnitObservation(result.EstablishedUnits[i]);
            Units = Array.AsReadOnly(units);
            var attrs = new SynergyAttributeObservation[IsAvailable ? result.EstablishedAttributes.Count : 0];
            for (int i=0;i<attrs.Length;i++) attrs[i] = new SynergyAttributeObservation(result.EstablishedAttributes[i]);
            Attributes = Array.AsReadOnly(attrs);
        }
    }

    public sealed class EnemyObservation
    {
        public int SlotIndex { get; }
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int HP { get; }
        public int MaximumHP { get; }
        public int Gap { get; }
        public int Cooldown { get; }
        public int SlotBonusAttack { get; }
        public int SlotBonusDefense { get; }
        public int SlotBonusHP { get; }
        public int SlotBonusGap { get; }
        public IReadOnlyList<string> CipEffects { get; }
        public IReadOnlyList<string> AeEffects { get; }
        public IReadOnlyList<string> PigEffects { get; }
        private readonly EnemyTargetObservation _initialTarget;

        // An explicit accessor preserves the existing property-based observation fingerprints.
        public EnemyTargetObservation GetInitialTarget() => _initialTarget;

        internal EnemyObservation(BattleUnitState unit, EnemyDefinition definition, EnemyTargetObservation initialTarget)
        {
            _initialTarget = initialTarget;
            SlotIndex = unit.SlotIndex; InstanceId = unit.InstanceId; DefinitionId = unit.DefinitionId;
            DisplayName = definition.DisplayName;
            Attack = unit.Attack; Defense = unit.Defense; HP = unit.CurrentHP; MaximumHP = unit.MaxHP;
            Gap = unit.Gap; Cooldown = unit.Cooldown;
            SlotBonusAttack = unit.TemporaryAttackBonus; SlotBonusDefense = unit.TemporaryDefenseBonus;
            SlotBonusHP = 0; SlotBonusGap = 0;
            // BattleUnitState exposes immutable read-only collections backed by private copies.
            CipEffects = unit.CipEffectIds; AeEffects = unit.AeEffectIds; PigEffects = unit.PigEffectIds;
        }

        internal EnemyObservation(EnemyInstance instance, EnemyDefinition definition, EnemyPreparationState prep,
            EnemyTargetObservation initialTarget)
        {
            _initialTarget = initialTarget;
            SlotIndex = instance.SlotIndex; InstanceId = instance.InstanceId; DefinitionId = instance.DefinitionId;
            DisplayName = definition.DisplayName;
            SlotBonusAttack = prep == null ? 0 : prep.GetAttackBonus(SlotIndex);
            SlotBonusDefense = prep == null ? 0 : prep.GetDefenseBonus(SlotIndex);
            SlotBonusHP = prep == null ? 0 : prep.GetHealthBonus(SlotIndex);
            SlotBonusGap = prep == null ? 0 : prep.GetGapBonus(SlotIndex);
            Attack = checked(instance.Attack + SlotBonusAttack); Defense = checked(instance.Defense + SlotBonusDefense);
            MaximumHP = Math.Max(0, checked(instance.MaxHealth + SlotBonusHP)); HP = MaximumHP;
            Gap = Math.Max(0, checked(instance.Gap + SlotBonusGap)); Cooldown = instance.Cooldown;
            CipEffects = definition.CipEffectIds; AeEffects = definition.AeEffectIds; PigEffects = definition.PigEffectIds;
        }
    }

    public sealed class BattleUnitObservation
    {
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public int SlotIndex { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int HP { get; }
        public int MaximumHP { get; }
        public bool HasHealth { get; }
        public int Gap { get; }
        public int Cooldown { get; }
        public int Star { get; }
        public IReadOnlyList<string> UnitTags { get; }
        public IReadOnlyList<string> AttributeTags { get; }
        public IReadOnlyList<string> PublicEffectIds { get; }
        internal BattleUnitObservation(BattleUnitState unit)
        {
            InstanceId=unit.InstanceId; DefinitionId=unit.DefinitionId; SlotIndex=unit.SlotIndex;
            Attack=unit.Attack; Defense=unit.Defense; HasHealth=unit.HasHealth;
            HP=unit.CurrentHP; MaximumHP=unit.MaxHP; Gap=unit.Gap; Cooldown=unit.Cooldown; Star=unit.Star;
            // BattleUnitState is immutable and defensively owns these read-only lists.
            UnitTags=unit.UnitTags; AttributeTags=unit.AttributeTags;
            var e=new List<string>(); e.AddRange(unit.CipEffectIds); e.AddRange(unit.AeEffectIds); e.AddRange(unit.PigEffectIds);
            PublicEffectIds=Array.AsReadOnly(e.ToArray());
        }
        private static IReadOnlyList<string> Copy(IReadOnlyList<string> src)
        { var a=new string[src.Count]; for(int i=0;i<a.Length;i++) a[i]=src[i]; return Array.AsReadOnly(a); }
    }

    public sealed class BattleObservation
    {
        public bool IsInProgress { get; }
        public int CurrentRound { get; }
        public BattlePhase Phase { get; }
        public int PlayerHP { get; }
        public int PlayerMaximumHP { get; }
        public int PlayerBlock { get; }
        public BattleOutcome Outcome { get; }
        public bool HasResult { get; }
        public int? FinalRound { get; }
        public BattleFinishReason? FinishReason { get; }
        public int? DefeatedEnemyCount { get; }
        public IReadOnlyList<BattleUnitObservation> Allies { get; }
        public IReadOnlyList<BattleUnitObservation> Enemies { get; }
        internal BattleObservation(BattleState battle, BattleResult result)
        {
            IsInProgress=battle != null && battle.Status==BattleExecutionStatus.InProgress; CurrentRound=battle == null ? (result == null ? 0 : result.FinalRound) : battle.CurrentRound;
            Phase=battle == null ? BattlePhase.Completed : battle.Phase; PlayerHP=battle == null ? (result == null ? 0 : result.EndingPlayerHP) : battle.PlayerCurrentHP; PlayerMaximumHP=battle == null ? PlayerHP : battle.PlayerMaximumHP;
            PlayerBlock=battle == null ? 0 : battle.PlayerBlock; Outcome=battle == null ? (result == null ? BattleOutcome.Pending : result.Outcome) : battle.Outcome; HasResult=result!=null;
            FinalRound=result==null?(int?)null:result.FinalRound;
            FinishReason=result==null?(BattleFinishReason?)null:result.Reason;
            DefeatedEnemyCount=result==null?(int?)null:result.DefeatedEnemyCount;
            Allies=battle == null ? Array.AsReadOnly(Array.Empty<BattleUnitObservation>()) : Copy(battle.Allies); Enemies=battle == null ? Array.AsReadOnly(Array.Empty<BattleUnitObservation>()) : Copy(battle.Enemies);
        }
        private static IReadOnlyList<BattleUnitObservation> Copy(IReadOnlyList<BattleUnitState> src)
        { var a=new BattleUnitObservation[src.Count]; for(int i=0;i<a.Length;i++) a[i]=new BattleUnitObservation(src[i]); return Array.AsReadOnly(a); }
    }

    public sealed class GameObservation
    {
        public PlayerObservation Player { get; }
        public StageObservation Stage { get; }
        public ShopObservation Shop { get; }
        public BoardObservation Board { get; }
        public HandObservation Hand { get; }
        public IReadOnlyList<CharacterObservation> Characters { get; }
        public EventItemObservation EventItem { get; }
        public SynergyObservation Synergy { get; }
        public IReadOnlyList<EnemyObservation> Enemies { get; }
        public BattleObservation Battle { get; }
        public bool IsStrategyActionAllowed { get; }
        public EconomyRulesObservation EconomyRules { get; }
        public IReadOnlyList<RosterDefinitionObservation> SelectedRoster { get; }
        public PublicCharacterStatsCatalog CharacterStats { get; }
        public IReadOnlyList<UnitRequirementObservation> UnitRequirements { get; }

        internal GameObservation(PlayerObservation player, StageObservation stage, ShopObservation shop,
            BoardObservation board, HandObservation hand, CharacterObservation[] characters,
            EventItemObservation eventItem, SynergyObservation synergy, EnemyObservation[] enemies,
            BattleObservation battle, bool actionAllowed, EconomyRulesObservation economyRules,
            IReadOnlyList<RosterDefinitionObservation> selectedRoster,
            PublicCharacterStatsCatalog characterStats, IReadOnlyList<UnitRequirementObservation> unitRequirements)
        {
            Player=player; Stage=stage; Shop=shop; Board=board; Hand=hand;
            Characters=Array.AsReadOnly(characters); EventItem=eventItem; Synergy=synergy;
            Enemies=Array.AsReadOnly(enemies); Battle=battle; IsStrategyActionAllowed=actionAllowed;
            EconomyRules=economyRules; SelectedRoster=selectedRoster;
            CharacterStats = characterStats;
            UnitRequirements = unitRequirements;
        }
    }
}






