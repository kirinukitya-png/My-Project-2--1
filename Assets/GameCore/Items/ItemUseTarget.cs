using System;

namespace ProjectNST.GameCore.Items
{
    public enum ItemUseTargetKind
    {
        BoardCharacter = 1,
        BoardArea = 2,
        HandCharacter = 3,
        EnemyCharacter = 4,
        EnemyArea = 5,
        PlayerArea = 6
    }

    /// <summary>Explicit target selected by the adapter; enemy details remain outside Stage 7-E.</summary>
    public sealed class ItemUseTarget
    {
        public ItemUseTargetKind Kind { get; }
        public int SlotIndex { get; }
        public int EnemyCount { get; }
        public bool IsPresent { get; }

        private ItemUseTarget(ItemUseTargetKind kind, int slotIndex, int enemyCount, bool isPresent)
        {
            Kind = kind;
            SlotIndex = slotIndex;
            EnemyCount = enemyCount;
            IsPresent = isPresent;
        }

        public static ItemUseTarget BoardCharacter(int slotIndex) => CreateSlot(ItemUseTargetKind.BoardCharacter, slotIndex);
        public static ItemUseTarget BoardArea(int slotIndex) => CreateSlot(ItemUseTargetKind.BoardArea, slotIndex);
        public static ItemUseTarget HandCharacter(int slotIndex) => CreateSlot(ItemUseTargetKind.HandCharacter, slotIndex);
        public static ItemUseTarget EnemyCharacter(int slotIndex, bool enemyPresent) =>
            CreateSlot(ItemUseTargetKind.EnemyCharacter, slotIndex, enemyPresent);
        public static ItemUseTarget PlayerArea(int boardSlotIndex) => CreateSlot(ItemUseTargetKind.PlayerArea, boardSlotIndex);

        public static ItemUseTarget EnemyArea(int enemyCount)
        {
            if (enemyCount < 0) throw new ArgumentOutOfRangeException(nameof(enemyCount));
            return new ItemUseTarget(ItemUseTargetKind.EnemyArea, -1, enemyCount, enemyCount > 0);
        }

        private static ItemUseTarget CreateSlot(ItemUseTargetKind kind, int slotIndex, bool isPresent = true)
        {
            if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return new ItemUseTarget(kind, slotIndex, 0, isPresent);
        }
    }
}
