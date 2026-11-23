using System;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleDamageFailure { None, ArithmeticOverflow }

    public sealed class BattleDamageResult
    {
        public bool Succeeded { get; }
        public BattleDamageFailure Failure { get; }
        public int Damage { get; }
        internal BattleDamageResult(bool succeeded, BattleDamageFailure failure, int damage)
        { Succeeded = succeeded; Failure = failure; Damage = damage; }
    }

    /// <summary>Pure damage calculation shared by ally and enemy normal attacks.</summary>
    public static class BattleDamageRules
    {
        public static BattleDamageResult Calculate(int attack, long totalDefense)
        {
            long raw = (long)attack - totalDefense;
            if (raw > int.MaxValue) return new BattleDamageResult(false, BattleDamageFailure.ArithmeticOverflow, 0);
            return new BattleDamageResult(true, BattleDamageFailure.None, (int)Math.Max(0L, raw));
        }
    }
}
