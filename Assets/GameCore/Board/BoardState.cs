using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Board
{
    /// <summary>Immutable fixed-grid board containing only CharacterInstance IDs.</summary>
    public sealed class BoardState
    {
        private readonly string[] _instanceIds;
        private readonly IReadOnlyList<string> _readOnlyInstanceIds;

        public const int StandardColumns = 3;
        public const int StandardRows = 3;
        public int Columns { get; }
        public int Rows { get; }
        public int SlotCount => _instanceIds.Length;
        public IReadOnlyList<string> InstanceIds => _readOnlyInstanceIds;

        public BoardState(int columns, int rows, IEnumerable<string> instanceIds)
        {
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
            if (instanceIds == null) throw new ArgumentNullException(nameof(instanceIds));

            long expectedCount = (long)columns * rows;
            if (expectedCount > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(columns), "Board dimensions exceed the supported slot count.");

            var copy = new List<string>(instanceIds).ToArray();
            if (copy.Length != (int)expectedCount)
                throw new ArgumentException("Slot count must equal columns multiplied by rows.", nameof(instanceIds));
            for (int i = 0; i < copy.Length; i++)
                if (copy[i] != null && string.IsNullOrWhiteSpace(copy[i]))
                    throw new ArgumentException("Occupied slots require a non-empty CharacterInstance ID.", nameof(instanceIds));

            Columns = columns;
            Rows = rows;
            _instanceIds = copy;
            _readOnlyInstanceIds = Array.AsReadOnly(_instanceIds);
        }

        public static BoardState CreateStandard()
        {
            return CreateEmpty(StandardColumns, StandardRows);
        }

        public static BoardState CreateEmpty(int columns, int rows)
        {
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
            long count = (long)columns * rows;
            if (count > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(columns), "Board dimensions exceed the supported slot count.");
            return new BoardState(columns, rows, new string[(int)count]);
        }

        /// <summary>Returns false only when the slot index is outside the board. A null ID means empty.</summary>
        public bool TryGetCharacterIdAt(int slotIndex, out string instanceId)
        {
            if (!IsValidIndex(slotIndex))
            {
                instanceId = null;
                return false;
            }
            instanceId = _instanceIds[slotIndex];
            return true;
        }

        public bool TryFindInstance(string instanceId, out int slotIndex)
        {
            slotIndex = -1;
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            for (int i = 0; i < _instanceIds.Length; i++)
            {
                if (!string.Equals(_instanceIds[i], instanceId, StringComparison.Ordinal)) continue;
                slotIndex = i;
                return true;
            }
            return false;
        }

        public int CountOccurrences(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return 0;
            int count = 0;
            for (int i = 0; i < _instanceIds.Length; i++)
                if (string.Equals(_instanceIds[i], instanceId, StringComparison.Ordinal)) count++;
            return count;
        }

        /// <summary>Creates a copied state. This structural API intentionally does not enforce gameplay rules.</summary>
        public bool TryWithCharacterAt(int slotIndex, string instanceId, out BoardState nextState)
        {
            nextState = this;
            if (!IsValidIndex(slotIndex) || (instanceId != null && string.IsNullOrWhiteSpace(instanceId))) return false;
            var copy = (string[])_instanceIds.Clone();
            copy[slotIndex] = instanceId;
            nextState = new BoardState(Columns, Rows, copy);
            return true;
        }

        public IReadOnlyList<string> FindDuplicateInstanceIds()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in _instanceIds)
                if (id != null && !seen.Add(id)) duplicates.Add(id);
            return Array.AsReadOnly(new List<string>(duplicates).ToArray());
        }

        private bool IsValidIndex(int slotIndex) => slotIndex >= 0 && slotIndex < _instanceIds.Length;
    }
}
