using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>Immutable inventory counts for one character definition.</summary>
    public sealed class CharacterPoolEntry
    {
        public string DefinitionId { get; }
        public int RemainingCopies { get; }
        public int InitialStockLimit { get; }
        public int ReservedCopies { get; }
        public int OwnedCopies { get; }

        public CharacterPoolEntry(string definitionId, int remainingCopies, int initialStockLimit, int reservedCopies = 0, int ownedCopies = 0)
        {
            if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("A non-empty DefinitionId is required.", nameof(definitionId));
            if (remainingCopies < 0) throw new ArgumentOutOfRangeException(nameof(remainingCopies));
            if (initialStockLimit < 0) throw new ArgumentOutOfRangeException(nameof(initialStockLimit));
            if (reservedCopies < 0) throw new ArgumentOutOfRangeException(nameof(reservedCopies));
            if (ownedCopies < 0) throw new ArgumentOutOfRangeException(nameof(ownedCopies));
            if ((long)remainingCopies + reservedCopies + ownedCopies != initialStockLimit)
                throw new ArgumentException("Available, reserved, and owned copies must equal the initial stock limit.");

            DefinitionId = definitionId;
            RemainingCopies = remainingCopies;
            InitialStockLimit = initialStockLimit;
            ReservedCopies = reservedCopies;
            OwnedCopies = ownedCopies;
        }
    }

    /// <summary>Read-only pool snapshot. Reservation/return transitions belong to future rules.</summary>
    public sealed class CharacterPool
    {
        private readonly Dictionary<string, CharacterPoolEntry> _entries;
        private readonly System.Collections.ObjectModel.ReadOnlyDictionary<string, CharacterPoolEntry> _readOnlyEntries;

        public IReadOnlyDictionary<string, CharacterPoolEntry> Entries => _readOnlyEntries;

        public CharacterPool(IEnumerable<CharacterPoolEntry> entries)
        {
            _entries = new Dictionary<string, CharacterPoolEntry>(StringComparer.Ordinal);
            if (entries != null)
            {
                foreach (CharacterPoolEntry entry in entries)
                {
                    if (entry == null) throw new ArgumentException("Pool entries cannot contain null values.", nameof(entries));
                    if (_entries.ContainsKey(entry.DefinitionId))
                        throw new ArgumentException("DefinitionId values must be unique in the pool.", nameof(entries));
                    _entries.Add(entry.DefinitionId, entry);
                }
            }
            _readOnlyEntries = new System.Collections.ObjectModel.ReadOnlyDictionary<string, CharacterPoolEntry>(_entries);
        }
    }
}
