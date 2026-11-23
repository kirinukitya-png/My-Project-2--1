using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Board
{
    /// <summary>Immutable hand slots. Items are identified by the existing ItemDefinition ID.</summary>
    public sealed class HandState
    {
        public const int StandardSlotCount = 5;
        private readonly HandSlotContent[] _slots;
        private readonly IReadOnlyList<HandSlotContent> _readOnlySlots;

        public int SlotCount => _slots.Length;
        public IReadOnlyList<HandSlotContent> Slots => _readOnlySlots;
        public int EmptySlotCount
        {
            get
            {
                int count = 0;
                foreach (HandSlotContent slot in _slots)
                    if (slot.IsEmpty) count++;
                return count;
            }
        }

        public HandState(IEnumerable<HandSlotContent> slots)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            var copy = new List<HandSlotContent>(slots).ToArray();
            for (int i = 0; i < copy.Length; i++)
                copy[i] = copy[i] ?? HandSlotContent.Empty();
            _slots = copy;
            _readOnlySlots = Array.AsReadOnly(_slots);
        }

        public static HandState CreateStandard() => CreateEmpty(StandardSlotCount);

        public static HandState CreateEmpty(int slotCount)
        {
            if (slotCount < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            var slots = new HandSlotContent[slotCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = HandSlotContent.Empty();
            return new HandState(slots);
        }

        /// <summary>Returns false for an out-of-range index; returned contents are immutable values.</summary>
        public bool TryGetSlot(int slotIndex, out HandSlotContent content)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length)
            {
                content = null;
                return false;
            }
            content = _slots[slotIndex];
            return true;
        }

        public bool TryFindCharacter(string instanceId, out int slotIndex)
        {
            slotIndex = -1;
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Kind != HandSlotContentKind.Character ||
                    !string.Equals(_slots[i].CharacterInstanceId, instanceId, StringComparison.Ordinal)) continue;
                slotIndex = i;
                return true;
            }
            return false;
        }

        public int CountOccurrences(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return 0;
            int count = 0;
            foreach (HandSlotContent slot in _slots)
                if (slot.Kind == HandSlotContentKind.Character &&
                    string.Equals(slot.CharacterInstanceId, instanceId, StringComparison.Ordinal)) count++;
            return count;
        }

        /// <summary>Creates a copied structural state without applying placement rules.</summary>
        public bool TryWithSlot(int slotIndex, HandSlotContent content, out HandState nextState)
        {
            nextState = this;
            if (slotIndex < 0 || slotIndex >= _slots.Length || content == null) return false;
            var copy = (HandSlotContent[])_slots.Clone();
            copy[slotIndex] = content;
            nextState = new HandState(copy);
            return true;
        }

        public IReadOnlyList<string> FindDuplicateInstanceIds()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (HandSlotContent slot in _slots)
                if (slot.Kind == HandSlotContentKind.Character && !seen.Add(slot.CharacterInstanceId))
                    duplicates.Add(slot.CharacterInstanceId);
            return Array.AsReadOnly(new List<string>(duplicates).ToArray());
        }
    }
}
