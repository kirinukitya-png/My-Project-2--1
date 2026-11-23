namespace ProjectNST.GameCore.Synergy
{
    /// <summary>A character participating in a board synergy.</summary>
    public sealed class SynergyParticipant
    {
        public string DefinitionId { get; }
        public string InstanceId { get; }
        public int BoardSlotIndex { get; }

        internal SynergyParticipant(string definitionId, string instanceId, int boardSlotIndex)
        {
            DefinitionId = definitionId;
            InstanceId = instanceId;
            BoardSlotIndex = boardSlotIndex;
        }
    }
}
