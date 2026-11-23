namespace ProjectNST.GameCore.Randomness
{
    /// <summary>
    /// Stable stream identifiers. Never renumber/reuse existing values; append new IDs
    /// when introducing additional random domains.
    /// </summary>
    public enum RandomStreamId : ulong
    {
        Shop = 0x01UL,
        Stats = 0x02UL,
        Encounter = 0x03UL,
        Battle = 0x04UL,
        Event = 0x05UL,
        Roster = 0x06UL,
        Economy = 0x07UL,
        Strategy = 0x08UL
    }
}
