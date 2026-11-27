using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Battle
{
    [Flags]
    public enum AllyRoundEnemyEffects { None = 0, BattleStart = 1, OnDefeat = 2, EnemyTurn = 4 }

    public readonly struct AllyRoundEnemyInput
    {
        public int Slot { get; }
        public int HP { get; }
        public int Defense { get; }
        public AllyRoundEnemyEffects Effects { get; }

        public AllyRoundEnemyInput(int slot, int hp, int defense, AllyRoundEnemyEffects effects = AllyRoundEnemyEffects.None)
        {
            Slot = slot;
            HP = hp;
            Defense = defense;
            Effects = effects;
        }

        public static AllyRoundEnemyInput FromObservation(EnemyObservation enemy)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            var effects = AllyRoundEnemyEffects.None;
            if (enemy.CipEffects.Count > 0) effects |= AllyRoundEnemyEffects.BattleStart;
            if (enemy.PigEffects.Count > 0) effects |= AllyRoundEnemyEffects.OnDefeat;
            if (enemy.AeEffects.Count > 0) effects |= AllyRoundEnemyEffects.EnemyTurn;
            return new AllyRoundEnemyInput(enemy.SlotIndex, enemy.HP, enemy.Defense, effects);
        }
    }

    public readonly struct AllyRoundEnemyState
    {
        public int Slot { get; }
        public decimal HP { get; }
        public int InitialDefense { get; }
        public int TemporaryDefenseReduction { get; }
        public int Defense => InitialDefense - TemporaryDefenseReduction;
        public bool IsAlive => HP > 0m;

        internal AllyRoundEnemyState(int slot, decimal hp, int defense, int reduction = 0)
        {
            Slot = slot;
            HP = hp;
            InitialDefense = defense;
            TemporaryDefenseReduction = reduction;
        }
    }

    public sealed class AllyRoundEnemyOutcome
    {
        public decimal Probability { get; }
        public IReadOnlyList<AllyRoundEnemyState> Enemies { get; }
        public int SurvivingSlotMask { get; }

        internal AllyRoundEnemyOutcome(decimal probability, AllyRoundEnemyState[] enemies)
        {
            Probability = probability;
            Enemies = Array.AsReadOnly(enemies);
            foreach (var enemy in enemies)
                if (enemy.IsAlive) SurvivingSlotMask |= 1 << enemy.Slot;
        }
    }

    public sealed class AllyRoundAttackResult
    {
        public AllyRoundAttackStatus Status { get; }
        public bool UsesPublicMeanStats { get; }
        public AllyRoundEnemyEffects IgnoredEffects { get; }
        public bool MatchesRealRoundModel => Status == AllyRoundAttackStatus.Evaluated && !UsesPublicMeanStats &&
            (IgnoredEffects & (AllyRoundEnemyEffects.BattleStart | AllyRoundEnemyEffects.OnDefeat)) == 0;
        public IReadOnlyList<AllyRoundEnemyOutcome> Outcomes { get; }
        public IReadOnlyList<(int Slot, decimal ExpectedHP, decimal SurvivalProbability, decimal DefeatProbability)> EnemySummaries { get; }
        public decimal? ExpectedEffectiveDamage { get; }
        public decimal? ExpectedDefeats { get; }
        public decimal? ExpectedNormalAttacks { get; }
        public decimal? ExpectedUnitMoves { get; }
        public decimal? ExpectedAttacks => ExpectedNormalAttacks + ExpectedUnitMoves;
        public decimal? ExpectedNormalDamage { get; }
        public decimal? ExpectedUnitDamage { get; }
        public decimal? ExpectedOverkill { get; }
        public decimal? ExpectedDefenseReduction { get; }
        public decimal? ExpectedDefenseReductionEvents { get; }
        public int CompletedActionLayers { get; }
        public int PeakStates { get; }
        public int LastCompletedStateCount { get; }
        public int GeneratedBranches { get; }
        public int MergedBranches { get; }
        public int TerminalStates { get; }
        public decimal? LastCompletedProbabilityMass { get; }
        public IReadOnlyList<(int Before, int Generated, int After, int Merged)> Layers { get; }
        public int PlannedActions { get; }
        public IReadOnlyList<decimal> ActionExecutionProbabilities { get; }
        internal IReadOnlyList<AllyRoundEnemyState> InitialEnemies { get; }

        internal AllyRoundAttackResult(AllyRoundAttackStatus status, AllyRoundAttackPlan plan = null,
            AllyRoundEnemyEffects effects = AllyRoundEnemyEffects.None, int layers = 0, int peak = 0,
            int lastStates = 0, int branches = 0, int merges = 0, decimal? mass = null,
            List<AllyRoundEnemyOutcome> outcomes = null, AllyRoundAttackTotals? totals = null,
            List<(int, int, int, int)> layerDetails = null, List<AllyRoundEnemyState> initialEnemies = null,
            List<decimal> executionProbabilities = null)
        {
            Status = status;
            UsesPublicMeanStats = plan?.UsesPublicMeanStats == true;
            IgnoredEffects = effects;
            CompletedActionLayers = layers;
            PeakStates = peak;
            LastCompletedStateCount = lastStates;
            GeneratedBranches = branches;
            MergedBranches = merges;
            LastCompletedProbabilityMass = mass;
            Layers = layerDetails == null ? Array.AsReadOnly(Array.Empty<(int, int, int, int)>()) : layerDetails.AsReadOnly();
            PlannedActions = plan?.Actions.Count ?? 0;
            ActionExecutionProbabilities = executionProbabilities == null ? Array.AsReadOnly(Array.Empty<decimal>()) :
                executionProbabilities.AsReadOnly();
            InitialEnemies = initialEnemies == null ? Array.AsReadOnly(Array.Empty<AllyRoundEnemyState>()) : initialEnemies.AsReadOnly();
            Outcomes = (outcomes ?? new List<AllyRoundEnemyOutcome>()).AsReadOnly();
            TerminalStates = Outcomes.Count(outcome => outcome.SurvivingSlotMask == 0);
            var summaries = new List<(int, decimal, decimal, decimal)>();
            if (totals.HasValue)
            {
                var values = totals.Value;
                ExpectedNormalAttacks = values.NormalAttacks;
                ExpectedUnitMoves = values.UnitMoves;
                ExpectedNormalDamage = values.NormalDamage;
                ExpectedUnitDamage = values.UnitDamage;
                ExpectedEffectiveDamage = values.NormalDamage + values.UnitDamage;
                ExpectedOverkill = values.Overkill;
                ExpectedDefenseReduction = values.DefenseReduction;
                ExpectedDefenseReductionEvents = values.DefenseReductionEvents;
                if (Outcomes.Count > 0)
                    for (int i = 0; i < Outcomes[0].Enemies.Count; i++)
                    {
                        decimal hp = 0m, survives = 0m, defeated = 0m;
                        foreach (var outcome in Outcomes)
                        {
                            var enemy = outcome.Enemies[i];
                            hp += outcome.Probability * enemy.HP;
                            if (enemy.IsAlive) survives += outcome.Probability;
                            else defeated += outcome.Probability;
                        }
                        summaries.Add((Outcomes[0].Enemies[i].Slot, hp, survives, defeated));
                    }
                ExpectedDefeats = summaries.Sum(enemy => enemy.Item4);
            }
            EnemySummaries = summaries.AsReadOnly();
        }
    }

    internal struct AllyRoundAttackTotals
    {
        internal decimal NormalAttacks, UnitMoves, NormalDamage, UnitDamage;
        internal decimal Overkill, DefenseReduction, DefenseReductionEvents;

        internal void Record(bool isUnitMove, decimal probability, decimal damage, decimal effective, int reduction)
        {
            if (isUnitMove)
            {
                UnitMoves += probability;
                UnitDamage += probability * effective;
            }
            else
            {
                NormalAttacks += probability;
                NormalDamage += probability * effective;
            }
            Overkill += probability * (damage - effective);
            DefenseReduction += probability * reduction;
            if (reduction > 0) DefenseReductionEvents += probability;
        }
    }

    /// <summary>Exact target branching for a static first ally turn, bounded after state merging.</summary>
    public static class AllyRoundAttackEvaluator
    {
        public const int DefaultMaxStates = 256;
        public const int MaximumStateLimit = 1024;

        public static AllyRoundAttackResult Evaluate(AllyRoundAttackPlan plan,
            IReadOnlyList<AllyRoundEnemyInput> enemies, int maxStates = DefaultMaxStates, bool includeDetails = false) =>
            EvaluateCore(plan, enemies, maxStates, includeDetails, false);

        internal static AllyRoundAttackResult EvaluateAgainstImmortalReference(AllyRoundAttackPlan plan,
            AllyRoundEnemyInput enemy, int maxStates, bool includeDetails) =>
            EvaluateCore(plan, new[] { enemy }, maxStates, includeDetails, true);

        private static AllyRoundAttackResult EvaluateCore(AllyRoundAttackPlan plan,
            IReadOnlyList<AllyRoundEnemyInput> enemies, int maxStates, bool includeDetails, bool immortal)
        {
            if (plan == null || enemies == null || enemies.Count > 6 || maxStates < 1 || maxStates > MaximumStateLimit)
                return new AllyRoundAttackResult(AllyRoundAttackStatus.InvalidInput);
            int seen = 0;
            var initial = new List<AllyRoundEnemyState>();
            var effects = AllyRoundEnemyEffects.None;
            foreach (var enemy in enemies.OrderBy(enemy => enemy.Slot))
            {
                if (enemy.Slot < 0 || enemy.Slot >= 6 || enemy.HP < 0 || (seen & (1 << enemy.Slot)) != 0)
                    return new AllyRoundAttackResult(AllyRoundAttackStatus.InvalidInput);
                seen |= 1 << enemy.Slot;
                if (enemy.HP == 0) continue;
                effects |= enemy.Effects;
                initial.Add(new AllyRoundEnemyState(enemy.Slot, enemy.HP, enemy.Defense));
            }
            if (initial.Count == 0)
                return new AllyRoundAttackResult(AllyRoundAttackStatus.NoEnemy, totals: new AllyRoundAttackTotals());
            if (plan.Status != AllyRoundAttackStatus.Evaluated)
                return new AllyRoundAttackResult(plan.Status, plan, effects);

            var current = NewLayer();
            current.Add(initial.ToArray(), 1m);
            var totals = new AllyRoundAttackTotals();
            var details = includeDetails ? new List<(int, int, int, int)>() : null;
            var executionProbabilities = includeDetails ? new List<decimal>() : null;
            int layers = 0, peak = 1, branches = 0, merges = 0;
            AllyRoundAttackResult Fail(AllyRoundAttackStatus status) => new AllyRoundAttackResult(status, plan,
                effects, layers, peak, current.Count, branches, merges, current.Values.Sum(), layerDetails: details,
                executionProbabilities: executionProbabilities);

            foreach (var action in plan.Actions)
            {
                var next = NewLayer();
                int beforeBranches = branches, beforeMerges = merges;
                bool executed = false;
                decimal executedMass = 0m;
                foreach (var entry in current)
                {
                    int alive = entry.Key.Count(enemy => enemy.IsAlive);
                    if (alive == 0)
                    {
                        if (!Add(next, entry.Key, entry.Value, maxStates, ref merges))
                            return Fail(AllyRoundAttackStatus.StateLimitExceeded);
                        continue;
                    }
                    executed = true;
                    if (includeDetails) executedMass += entry.Value;
                    decimal share = entry.Value / alive;
                    for (int i = 0; i < entry.Key.Length; i++)
                    {
                        var enemy = entry.Key[i];
                        if (!enemy.IsAlive) continue;
                        branches++;
                        decimal damage = Math.Max(0m, action.Attack - enemy.Defense);
                        if (damage > int.MaxValue)
                            return Fail(AllyRoundAttackStatus.InvalidCombatState);
                        decimal effective = immortal ? damage : Math.Min(enemy.HP, damage);
                        int reduction = action.IsUnitMove && damage == 0m
                            ? Math.Min(enemy.Defense, action.ParticipantSlots.Count) : 0;
                        // GameCore rejects a negative accumulated temporary reduction.
                        if (enemy.TemporaryDefenseReduction + reduction < 0)
                            return Fail(AllyRoundAttackStatus.InvalidCombatState);

                        var successor = (AllyRoundEnemyState[])entry.Key.Clone();
                        successor[i] = new AllyRoundEnemyState(enemy.Slot, immortal ? enemy.HP : enemy.HP - effective,
                            enemy.InitialDefense, enemy.TemporaryDefenseReduction + reduction);
                        if (!Add(next, successor, share, maxStates, ref merges))
                            return Fail(AllyRoundAttackStatus.StateLimitExceeded);
                        // Every branch contributes here, including branches merged into an existing state.
                        totals.Record(action.IsUnitMove, share, damage, effective, reduction);
                    }
                }
                if (!executed) break;
                details?.Add((current.Count, branches - beforeBranches, next.Count, merges - beforeMerges));
                executionProbabilities?.Add(executedMass);
                current = next;
                layers++;
                peak = Math.Max(peak, current.Count);
            }

            var largest = current.Aggregate((left, right) => left.Value >= right.Value ? left : right);
            decimal correction = 1m - current.Values.Sum();
            var outcomes = current.Select(entry => new AllyRoundEnemyOutcome(
                entry.Value + (ReferenceEquals(entry.Key, largest.Key) ? correction : 0m), entry.Key)).ToList();
            return new AllyRoundAttackResult(AllyRoundAttackStatus.Evaluated, plan, effects, layers, peak,
                current.Count, branches, merges, current.Values.Sum(), outcomes, totals, details, initial, executionProbabilities);
        }

        private static SortedDictionary<AllyRoundEnemyState[], decimal> NewLayer() =>
            new SortedDictionary<AllyRoundEnemyState[], decimal>(
                Comparer<AllyRoundEnemyState[]>.Create(CompareStates));

        private static bool Add(SortedDictionary<AllyRoundEnemyState[], decimal> layer,
            AllyRoundEnemyState[] state, decimal mass, int maxStates, ref int merges)
        {
            if (layer.TryGetValue(state, out decimal existing))
            {
                layer[state] = existing + mass;
                merges++;
                return true;
            }
            if (layer.Count == maxStates) return false;
            layer.Add(state, mass);
            return true;
        }

        private static int CompareStates(AllyRoundEnemyState[] left, AllyRoundEnemyState[] right)
        {
            // Slots and initial defenses are identical within a call; array positions preserve identity.
            for (int i = 0; i < left.Length; i++)
            {
                int comparison = left[i].HP.CompareTo(right[i].HP);
                if (comparison != 0) return comparison;
                comparison = left[i].TemporaryDefenseReduction.CompareTo(right[i].TemporaryDefenseReduction);
                if (comparison != 0) return comparison;
            }
            return 0;
        }
    }
}
