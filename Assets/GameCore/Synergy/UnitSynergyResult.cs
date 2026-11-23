using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Synergy
{
    /// <summary>Read-only result for one established Unit group.</summary>
    public sealed class UnitSynergyResult
    {
        private readonly SynergyParticipant[] _participants;
        public string UnitName { get; }
        public IReadOnlyList<SynergyParticipant> Participants { get; }
        public IReadOnlyList<string> ParticipantDefinitionIds { get; }
        public IReadOnlyList<string> ParticipantInstanceIds { get; }
        public IReadOnlyList<int> ParticipantBoardSlotIndexes { get; }
        public int ParticipantCount => _participants.Length;
        public bool IsEstablished => true;

        internal UnitSynergyResult(string unitName, IList<SynergyParticipant> participants)
        {
            UnitName = unitName;
            _participants = new SynergyParticipant[participants.Count];
            var definitionIds = new string[participants.Count];
            var instanceIds = new string[participants.Count];
            var slotIndexes = new int[participants.Count];
            for (int i = 0; i < participants.Count; i++)
            {
                SynergyParticipant participant = participants[i];
                _participants[i] = participant;
                definitionIds[i] = participant.DefinitionId;
                instanceIds[i] = participant.InstanceId;
                slotIndexes[i] = participant.BoardSlotIndex;
            }

            Participants = Array.AsReadOnly(_participants);
            ParticipantDefinitionIds = Array.AsReadOnly(definitionIds);
            ParticipantInstanceIds = Array.AsReadOnly(instanceIds);
            ParticipantBoardSlotIndexes = Array.AsReadOnly(slotIndexes);
        }
    }
}
