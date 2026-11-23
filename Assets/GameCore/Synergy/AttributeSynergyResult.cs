using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Synergy
{
    /// <summary>Read-only result for one Attribute shared by board characters.</summary>
    public sealed class AttributeSynergyResult
    {
        private readonly SynergyParticipant[] _participants;
        public string AttributeName { get; }
        public IReadOnlyList<SynergyParticipant> Participants { get; }
        public IReadOnlyList<string> ParticipantDefinitionIds { get; }
        public IReadOnlyList<string> ParticipantInstanceIds { get; }
        public IReadOnlyList<int> ParticipantBoardSlotIndexes { get; }
        public int ParticipantCount => _participants.Length;
        public int BlockBonus => 1;

        internal AttributeSynergyResult(string attributeName, IList<SynergyParticipant> participants)
        {
            AttributeName = attributeName;
            _participants = new SynergyParticipant[participants.Count];
            var definitionIds = new string[participants.Count];
            var instanceIds = new string[participants.Count];
            var slotIndexes = new int[participants.Count];
            for (int i = 0; i < participants.Count; i++)
            {
                _participants[i] = participants[i];
                definitionIds[i] = participants[i].DefinitionId;
                instanceIds[i] = participants[i].InstanceId;
                slotIndexes[i] = participants[i].BoardSlotIndex;
            }
            Participants = Array.AsReadOnly(_participants);
            ParticipantDefinitionIds = Array.AsReadOnly(definitionIds);
            ParticipantInstanceIds = Array.AsReadOnly(instanceIds);
            ParticipantBoardSlotIndexes = Array.AsReadOnly(slotIndexes);
        }
    }
}
