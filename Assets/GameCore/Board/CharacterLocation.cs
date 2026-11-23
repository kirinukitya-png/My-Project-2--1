namespace ProjectNST.GameCore.Board
{
    public enum CharacterLocationKind
    {
        Board = 0,
        Hand = 1
    }

    /// <summary>Read-only, derived location; it is not stored on CharacterInstance or in ownership state.</summary>
    public readonly struct CharacterLocation
    {
        public CharacterLocationKind Kind { get; }
        public int SlotIndex { get; }

        public CharacterLocation(CharacterLocationKind kind, int slotIndex)
        {
            Kind = kind;
            SlotIndex = slotIndex;
        }
    }
}
