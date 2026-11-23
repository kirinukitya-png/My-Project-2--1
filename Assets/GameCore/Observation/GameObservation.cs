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

        internal CharacterObservation(CharacterInstanceView instance, CharacterDefinition definition,
            ObservationLocationKind locationKind, int slotIndex)
        {
            InstanceId = instance.InstanceId; DefinitionId = instance.DefinitionId;
            DisplayName = definition.CharacterName; RunCost = instance.RunCost; Star = instance.Star;
            Attack = instance.Attack; Defense = instance.Defense;
            TemporaryAttackBonus = instance.TemporaryAttackBonus;
            TemporaryDefenseBonus = instance.TemporaryDefenseBonus;
            Units = Copy(definition.Units); Attributes = Copy(definition.Attributes);
            LocationKind = locationKind; SlotIndex = slotIndex;
        }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> values)
        {
            var copy = new string[values == null ? 0 : values.Count];
            if (values != null) for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return Array.AsReadOnly(copy);
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
        internal BoardObservation(int columns, int rows, IList<BoardSlotObservation> slots)
        { Columns = columns; Rows = rows; Slots = Freeze(slots); }
        private static IReadOnlyList<BoardSlotObservation> Freeze(IList<BoardSlotObservation> src)
        { var a = new BoardSlotObservation[src.Count]; src.CopyTo(a, 0); return Array.AsReadOnly(a); }
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
        internal HandObservation(int slotCount, int emptySlotCount, IList<HandSlotObservation> slots)
        { SlotCount = slotCount; EmptySlotCount = emptySlotCount; var a = new HandSlotObservation[slots.Count]; slots.CopyTo(a, 0); Slots = Array.AsReadOnly(a); }
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
        internal StageObservation(StageProgressState state, StageProgressConfig config)
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
        internal ShopOfferObservation(ShopOffer offer, string displayName, int offerIndex)
        { OfferId = offer.OfferId; Kind = offer.Kind; DefinitionId = offer.DefinitionId; DisplayName = displayName;
          DisplayPrice = offer.DisplayPrice; CharacterCost = offer.CharacterCost; Status = offer.Status; OfferIndex = offerIndex; }
    }

    public sealed class ShopObservation
    {
        public ObservationShopKind Kind { get; }
        public int SlotCount { get; }
        public int StageNumber { get; }
        public int RoundNumber { get; }
        public bool IsEventStage { get; }
                public IReadOnlyList<ShopOfferObservation> Offers { get; }
        internal ShopObservation(ShopKind kind, int slotCount, int stage, int round, bool isEventStage, IList<ShopOfferObservation> offers)
        { Kind = (ObservationShopKind)kind; SlotCount = slotCount; StageNumber = stage; RoundNumber = round; IsEventStage = isEventStage;
          var a = new ShopOfferObservation[offers.Count]; offers.CopyTo(a, 0); Offers = Array.AsReadOnly(a); }
    }

    public sealed class EventItemObservation
    {
        public ItemObservation Item { get; }
        public EventItemStatus Status { get; }
        public bool IsAvailable { get; }
        internal EventItemObservation(ItemDefinition item, EventItemState state)
        { Item = new ItemObservation(item); Status = state.Status; IsAvailable = state.IsAvailable; }
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
        internal EnemyObservation(BattleUnitState unit, EnemyDefinition definition)
        {
            SlotIndex = unit.SlotIndex; InstanceId = unit.InstanceId; DefinitionId = unit.DefinitionId;
            DisplayName = definition.DisplayName;
            Attack = unit.Attack; Defense = unit.Defense; HP = unit.CurrentHP; MaximumHP = unit.MaxHP;
            Gap = unit.Gap; Cooldown = unit.Cooldown;
            SlotBonusAttack = unit.TemporaryAttackBonus; SlotBonusDefense = unit.TemporaryDefenseBonus;
            SlotBonusHP = 0; SlotBonusGap = 0;
            CipEffects = Copy(unit.CipEffectIds); AeEffects = Copy(unit.AeEffectIds); PigEffects = Copy(unit.PigEffectIds);
        }

        internal EnemyObservation(EnemyInstance instance, EnemyDefinition definition, EnemyPreparationState prep)
        {
            SlotIndex = instance.SlotIndex; InstanceId = instance.InstanceId; DefinitionId = instance.DefinitionId;
            DisplayName = definition.DisplayName;
            SlotBonusAttack = prep == null ? 0 : prep.GetAttackBonus(SlotIndex);
            SlotBonusDefense = prep == null ? 0 : prep.GetDefenseBonus(SlotIndex);
            SlotBonusHP = prep == null ? 0 : prep.GetHealthBonus(SlotIndex);
            SlotBonusGap = prep == null ? 0 : prep.GetGapBonus(SlotIndex);
            Attack = checked(instance.Attack + SlotBonusAttack); Defense = checked(instance.Defense + SlotBonusDefense);
            MaximumHP = Math.Max(0, checked(instance.MaxHealth + SlotBonusHP)); HP = MaximumHP;
            Gap = Math.Max(0, checked(instance.Gap + SlotBonusGap)); Cooldown = instance.Cooldown;
            CipEffects = Copy(definition.CipEffectIds); AeEffects = Copy(definition.AeEffectIds); PigEffects = Copy(definition.PigEffectIds);
        }
        private static IReadOnlyList<string> Copy(IReadOnlyList<string> src)
        { var a = new string[src.Count]; for (int i=0;i<a.Length;i++) a[i]=src[i]; return Array.AsReadOnly(a); }
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
            UnitTags=Copy(unit.UnitTags); AttributeTags=Copy(unit.AttributeTags);
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

        internal GameObservation(PlayerObservation player, StageObservation stage, ShopObservation shop,
            BoardObservation board, HandObservation hand, IList<CharacterObservation> characters,
            EventItemObservation eventItem, SynergyObservation synergy, IList<EnemyObservation> enemies,
            BattleObservation battle, bool actionAllowed)
        {
            Player=player; Stage=stage; Shop=shop; Board=board; Hand=hand;
            Characters=Freeze(characters); EventItem=eventItem; Synergy=synergy;
            Enemies=Freeze(enemies); Battle=battle; IsStrategyActionAllowed=actionAllowed;
        }
        private static IReadOnlyList<T> Freeze<T>(IList<T> src)
        { var a=new T[src.Count]; src.CopyTo(a,0); return Array.AsReadOnly(a); }
    }
}






