namespace ProjectNST.GameCore.Characters
{
    /// <summary>Immutable public read model for a CharacterInstance owned by CharacterOwnershipState.</summary>
    public sealed class CharacterInstanceView
    {
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public int RunCost { get; }
        public int Star { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int TemporaryAttackBonus { get; }
        public int TemporaryDefenseBonus { get; }
        public bool HasRolledStats { get; }
        public bool HasTemporaryStar { get; }

        internal CharacterInstanceView(CharacterInstance instance)
        {
            InstanceId = instance.InstanceId;
            DefinitionId = instance.DefinitionId;
            RunCost = instance.RunCost;
            Star = instance.Star;
            Attack = instance.Attack;
            Defense = instance.Defense;
            TemporaryAttackBonus = instance.TemporaryAttackBonus;
            TemporaryDefenseBonus = instance.TemporaryDefenseBonus;
            HasRolledStats = instance.HasRolledStats;
            HasTemporaryStar = instance.HasTemporaryStar;
        }
    }
}
