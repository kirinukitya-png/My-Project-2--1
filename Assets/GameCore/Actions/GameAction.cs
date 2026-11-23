using System;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Actions
{
    /// <summary>Caller-issued, stable identity for one decision event. Never generated from RNG.</summary>
    [Serializable]
    public readonly struct ActionId : IEquatable<ActionId>
    {
        public string Value { get; }
        public ActionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Action ID is required.", nameof(value));
            Value = value;
        }
        public bool Equals(ActionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ActionId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(ActionId left, ActionId right) => left.Equals(right);
        public static bool operator !=(ActionId left, ActionId right) => !left.Equals(right);
    }

    /// <summary>Numeric values are persisted in replay records; append new values, never renumber.</summary>
    public enum GameActionKind
    {
        SelectStageChoice = 1, BuyCharacter = 2, BuyItem = 3, RerollShop = 4,
        BuyExperience = 5, MoveOrSwapBoard = 6, MoveOrSwapHand = 7,
        MoveBoardToHand = 8, MoveHandToBoard = 9, SellCharacter = 10,
        SellItem = 11, UseHandItem = 12, UseEventItem = 13, EndEvent = 14, StartBattle = 15
    }

    /// <summary>Stable, serializable location payload independent from mutable Board/Hand state.</summary>
    [Serializable]
    public readonly struct GameActionLocation
    {
        public CharacterLocationKind Kind { get; }
        public int SlotIndex { get; }
        public GameActionLocation(CharacterLocationKind kind, int slotIndex)
        {
            if (!Enum.IsDefined(typeof(CharacterLocationKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            Kind = kind; SlotIndex = slotIndex;
        }
        public CharacterLocation ToCharacterLocation() => new CharacterLocation(Kind, SlotIndex);
    }

    /// <summary>Replay-safe copy of an explicit item target; range/occupancy legality is state-dependent.</summary>
    [Serializable]
    public readonly struct GameActionTarget
    {
        public ItemUseTargetKind Kind { get; }
        public int SlotIndex { get; }
        public int EnemyCount { get; }
        public bool IsPresent { get; }

        public GameActionTarget(ItemUseTargetKind kind, int slotIndex, int enemyCount, bool isPresent)
        {
            if (!Enum.IsDefined(typeof(ItemUseTargetKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind == ItemUseTargetKind.EnemyArea)
            {
                if (slotIndex != -1) throw new ArgumentOutOfRangeException(nameof(slotIndex), "EnemyArea has no slot index.");
                if (enemyCount < 0) throw new ArgumentOutOfRangeException(nameof(enemyCount));
            }
            else
            {
                if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex));
                if (enemyCount != 0) throw new ArgumentOutOfRangeException(nameof(enemyCount));
            }
            Kind = kind; SlotIndex = slotIndex; EnemyCount = enemyCount; IsPresent = isPresent;
        }

        public static GameActionTarget FromItemUseTarget(ItemUseTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new GameActionTarget(target.Kind, target.SlotIndex, target.EnemyCount, target.IsPresent);
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(ItemUseTargetKind), Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
            if (Kind == ItemUseTargetKind.EnemyArea)
            {
                if (SlotIndex != -1 || EnemyCount < 0) throw new ArgumentOutOfRangeException(nameof(SlotIndex));
            }
            else if (SlotIndex < 0 || EnemyCount != 0)
                throw new ArgumentOutOfRangeException(nameof(SlotIndex));
        }

        public ItemUseTarget ToItemUseTarget()
        {
            Validate();
            switch (Kind)
            {
                case ItemUseTargetKind.BoardCharacter: return ItemUseTarget.BoardCharacter(SlotIndex);
                case ItemUseTargetKind.BoardArea: return ItemUseTarget.BoardArea(SlotIndex);
                case ItemUseTargetKind.HandCharacter: return ItemUseTarget.HandCharacter(SlotIndex);
                case ItemUseTargetKind.EnemyCharacter: return ItemUseTarget.EnemyCharacter(SlotIndex, IsPresent);
                case ItemUseTargetKind.EnemyArea: return ItemUseTarget.EnemyArea(EnemyCount);
                case ItemUseTargetKind.PlayerArea: return ItemUseTarget.PlayerArea(SlotIndex);
                default: throw new InvalidOperationException("Unknown item target kind.");
            }
        }
    }

    [Serializable]
    public abstract class GameAction
    {
        public ActionId ActionId { get; }
        public abstract GameActionKind Kind { get; }
        protected GameAction(ActionId actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId.Value)) throw new ArgumentException("Action ID is required.", nameof(actionId));
            ActionId = actionId;
        }
        protected static string RequireId(string value, string name) => GameActionValidation.RequireId(value, name);
        protected static int RequireSlot(int value, string name) => GameActionValidation.RequireSlot(value, name);
    }

    [Serializable]
    public sealed class SelectStageChoiceAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.SelectStageChoice;
        public StageChoice Choice { get; }
        public SelectStageChoiceAction(ActionId actionId, StageChoice choice) : base(actionId)
        { RequireEnum(choice, nameof(choice)); Choice = choice; }
        private static void RequireEnum(StageChoice value, string name)
        { if (!Enum.IsDefined(typeof(StageChoice), value)) throw new ArgumentOutOfRangeException(name); }
    }

    [Serializable]
    public sealed class BuyCharacterAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.BuyCharacter;
        public string OfferId { get; }
        public GameActionLocation Placement { get; }
        public BuyCharacterAction(ActionId actionId, string offerId, GameActionLocation placement) : base(actionId)
        { OfferId = RequireId(offerId, nameof(offerId)); Placement = placement; }
    }

    [Serializable]
    public sealed class BuyItemAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.BuyItem;
        public string OfferId { get; }
        public int HandSlot { get; }
        public BuyItemAction(ActionId actionId, string offerId, int handSlot) : base(actionId)
        { OfferId = RequireId(offerId, nameof(offerId)); HandSlot = RequireSlot(handSlot, nameof(handSlot)); }
    }

    [Serializable]
    public sealed class RerollShopAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.RerollShop;
        public ShopKind ShopKind { get; }
        public RerollShopAction(ActionId actionId, ShopKind shopKind) : base(actionId)
        {
            if (!Enum.IsDefined(typeof(ShopKind), shopKind) || shopKind == ShopKind.None)
                throw new ArgumentOutOfRangeException(nameof(shopKind));
            ShopKind = shopKind;
        }
    }

    [Serializable]
    public sealed class BuyExperienceAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.BuyExperience;
        public BuyExperienceAction(ActionId actionId) : base(actionId) { }
    }

    [Serializable]
    public sealed class MoveOrSwapBoardAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.MoveOrSwapBoard;
        public int SourceSlot { get; }
        public int TargetSlot { get; }
        public MoveOrSwapBoardAction(ActionId actionId, int sourceSlot, int targetSlot) : base(actionId)
        { SourceSlot = RequireSlot(sourceSlot, nameof(sourceSlot)); TargetSlot = RequireSlot(targetSlot, nameof(targetSlot)); }
    }

    [Serializable]
    public sealed class MoveOrSwapHandAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.MoveOrSwapHand;
        public int SourceSlot { get; }
        public int TargetSlot { get; }
        public MoveOrSwapHandAction(ActionId actionId, int sourceSlot, int targetSlot) : base(actionId)
        { SourceSlot = RequireSlot(sourceSlot, nameof(sourceSlot)); TargetSlot = RequireSlot(targetSlot, nameof(targetSlot)); }
    }

    [Serializable]
    public sealed class MoveBoardToHandAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.MoveBoardToHand;
        public int BoardSlot { get; }
        public int HandSlot { get; }
        public MoveBoardToHandAction(ActionId actionId, int boardSlot, int handSlot) : base(actionId)
        { BoardSlot = RequireSlot(boardSlot, nameof(boardSlot)); HandSlot = RequireSlot(handSlot, nameof(handSlot)); }
    }

    [Serializable]
    public sealed class MoveHandToBoardAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.MoveHandToBoard;
        public int HandSlot { get; }
        public int BoardSlot { get; }
        public MoveHandToBoardAction(ActionId actionId, int handSlot, int boardSlot) : base(actionId)
        { HandSlot = RequireSlot(handSlot, nameof(handSlot)); BoardSlot = RequireSlot(boardSlot, nameof(boardSlot)); }
    }

    [Serializable]
    public sealed class SellCharacterAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.SellCharacter;
        public string InstanceId { get; }
        public SellCharacterAction(ActionId actionId, string instanceId) : base(actionId)
        { InstanceId = RequireId(instanceId, nameof(instanceId)); }
    }

    [Serializable]
    public sealed class SellItemAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.SellItem;
        public int HandSlot { get; }
        public SellItemAction(ActionId actionId, int handSlot) : base(actionId)
        { HandSlot = RequireSlot(handSlot, nameof(handSlot)); }
    }

    [Serializable]
    public sealed class UseHandItemAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.UseHandItem;
        public int HandSlot { get; }
        public GameActionTarget Target { get; }
        public UseHandItemAction(ActionId actionId, int handSlot, GameActionTarget target) : base(actionId)
        { HandSlot = RequireSlot(handSlot, nameof(handSlot)); target.Validate(); Target = target; }
    }

    [Serializable]
    public sealed class UseEventItemAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.UseEventItem;
        public GameActionTarget Target { get; }
        public UseEventItemAction(ActionId actionId, GameActionTarget target) : base(actionId)
        { target.Validate(); Target = target; }
    }

    [Serializable]
    public sealed class EndEventAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.EndEvent;
        public EndEventAction(ActionId actionId) : base(actionId) { }
    }

    [Serializable]
    public sealed class StartBattleAction : GameAction
    {
        public override GameActionKind Kind => GameActionKind.StartBattle;
        public StartBattleAction(ActionId actionId) : base(actionId) { }
    }

    internal static class GameActionValidation
    {
        internal static string RequireId(string value, string name)
        { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Required ID is missing.", name); return value; }
        internal static int RequireSlot(int value, string name)
        { if (value < 0) throw new ArgumentOutOfRangeException(name); return value; }
        internal static void RequireEnum<T>(T value, string name) where T : struct
        { if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(name); }
    }

    internal static class GameActionConstructorHelpers
    {
        internal static string RequireId(string value, string name) => GameActionValidation.RequireId(value, name);
        internal static int RequireSlot(int value, string name) => GameActionValidation.RequireSlot(value, name);
    }
}

