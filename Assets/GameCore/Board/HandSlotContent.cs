using System;

namespace ProjectNST.GameCore.Board
{
    public enum HandSlotContentKind
    {
        Empty = 0,
        Character = 1,
        Item = 2
    }

    /// <summary>Immutable discriminated value: a slot contains at most one character ID or Item definition ID.</summary>
    public sealed class HandSlotContent
    {
        public HandSlotContentKind Kind { get; }
        public string CharacterInstanceId { get; }
        public string ItemDefinitionId { get; }
        public bool IsEmpty => Kind == HandSlotContentKind.Empty;

        private HandSlotContent(HandSlotContentKind kind, string characterInstanceId, string itemDefinitionId)
        {
            Kind = kind;
            CharacterInstanceId = characterInstanceId;
            ItemDefinitionId = itemDefinitionId;
        }

        public static HandSlotContent Empty() => new HandSlotContent(HandSlotContentKind.Empty, null, null);

        public static HandSlotContent Character(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("A non-empty CharacterInstance ID is required.", nameof(instanceId));
            return new HandSlotContent(HandSlotContentKind.Character, instanceId, null);
        }

        public static HandSlotContent Item(string itemDefinitionId)
        {
            if (string.IsNullOrWhiteSpace(itemDefinitionId))
                throw new ArgumentException("A non-empty Item Definition ID is required.", nameof(itemDefinitionId));
            return new HandSlotContent(HandSlotContentKind.Item, null, itemDefinitionId);
        }
    }
}
