using System;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleEffectType { Attack, Defense, Gap, Gold, Damage, Block }
    public enum BattleEffectTarget { TriggerActor, AttackTargets, Player }
    public enum BattleEffectDuration { Battle, Immediate }

    /// <summary>Immutable GameCore copy of one enemy EffectData asset.</summary>
    public sealed class BattleEffectDefinition
    {
        public string EffectId { get; }
        public BattleEffectType Effect { get; }
        public int Value { get; }
        public BattleEffectTarget Target { get; }
        public BattleEffectDuration Duration { get; }

        public BattleEffectDefinition(string effectId, BattleEffectType effect, int value,
            BattleEffectTarget target, BattleEffectDuration duration)
        {
            if (string.IsNullOrWhiteSpace(effectId)) throw new ArgumentException("Effect ID is required.", nameof(effectId));
            if (!Enum.IsDefined(typeof(BattleEffectType), effect)) throw new ArgumentOutOfRangeException(nameof(effect));
            if (!Enum.IsDefined(typeof(BattleEffectTarget), target)) throw new ArgumentOutOfRangeException(nameof(target));
            if (!Enum.IsDefined(typeof(BattleEffectDuration), duration)) throw new ArgumentOutOfRangeException(nameof(duration));
            bool unitEffect = effect == BattleEffectType.Attack || effect == BattleEffectType.Defense || effect == BattleEffectType.Gap;
            if (unitEffect != (target != BattleEffectTarget.Player))
                throw new ArgumentException("Attack/Defense/Gap target a unit; Gold/Damage/Block target the player.");
            if (effect == BattleEffectType.Gold && duration != BattleEffectDuration.Immediate)
                throw new ArgumentException("Gold effects must be immediate, matching EffectData validation.");
            if (effect == BattleEffectType.Gap && target == BattleEffectTarget.AttackTargets &&
                duration == BattleEffectDuration.Immediate)
                throw new ArgumentException("Immediate Gap effects cannot target AttackTargets.");
            EffectId = effectId;
            Effect = effect;
            Value = value;
            Target = target;
            Duration = duration;
        }
    }
}
