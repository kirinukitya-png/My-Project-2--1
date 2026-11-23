using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Battle
{
    /// <summary>Immutable established UnitMove participants in detector priority order.</summary>
    public sealed class UnitMoveState
    {
        private readonly string[] _participantInstanceIds;
        private readonly int[] _participantSlotIndexes;
        private readonly int[] _participantStars;

        public string UnitName { get; }
        public string MoveId => UnitName;
        public IReadOnlyList<string> ParticipantInstanceIds { get; }
        public IReadOnlyList<int> ParticipantSlotIndexes { get; }
        public IReadOnlyList<int> ParticipantStars { get; }
        public int ParticipantCount => _participantInstanceIds.Length;

        public UnitMoveState(string unitName, IEnumerable<string> instanceIds,
            IEnumerable<int> slotIndexes, IEnumerable<int> stars)
        {
            if (string.IsNullOrWhiteSpace(unitName)) throw new ArgumentException("Unit name is required.", nameof(unitName));
            if (instanceIds == null || slotIndexes == null || stars == null) throw new ArgumentNullException("Participant collections are required.");
            _participantInstanceIds = new List<string>(instanceIds).ToArray();
            _participantSlotIndexes = new List<int>(slotIndexes).ToArray();
            _participantStars = new List<int>(stars).ToArray();
            if (_participantInstanceIds.Length < 2 || _participantSlotIndexes.Length != _participantInstanceIds.Length ||
                _participantStars.Length != _participantInstanceIds.Length)
                throw new ArgumentException("UnitMove participant collections must have matching lengths and at least two participants.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _participantInstanceIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(_participantInstanceIds[i]) || !ids.Add(_participantInstanceIds[i]))
                    throw new ArgumentException("Participant Instance IDs must be non-empty and unique.", nameof(instanceIds));
                if (_participantSlotIndexes[i] < 0) throw new ArgumentOutOfRangeException(nameof(slotIndexes));
                if (_participantStars[i] < 1 || _participantStars[i] > 3) throw new ArgumentOutOfRangeException(nameof(stars));
            }
            UnitName = unitName;
            ParticipantInstanceIds = Array.AsReadOnly(_participantInstanceIds);
            ParticipantSlotIndexes = Array.AsReadOnly(_participantSlotIndexes);
            ParticipantStars = Array.AsReadOnly(_participantStars);
        }
    }
}
