namespace ProjectNST.GameCore.Battle
{
    /// <summary>Run-level change to apply atomically alongside a successful effect result.</summary>
    public sealed class BattlePersistentEffectCommand
    {
        public string TriggerId { get; }
        public string EffectId { get; }
        public string TargetInstanceId { get; }
        public BattleEffectType Effect { get; }
        public int Value { get; }

        internal BattlePersistentEffectCommand(string triggerId, string effectId, string targetInstanceId,
            BattleEffectType effect, int value)
        { TriggerId = triggerId; EffectId = effectId; TargetInstanceId = targetInstanceId; Effect = effect; Value = value; }
    }
}
