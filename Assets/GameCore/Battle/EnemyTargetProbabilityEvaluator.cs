using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Battle
{
    public enum EnemyTargetProbabilityStatus
    {
        Evaluated, Unavailable, UnsupportedTarget, InvalidDefinition, InvalidInput
    }

    public sealed class EnemyTargetOutcome
    {
        public int SlotMask { get; }
        public IReadOnlyList<int> Slots { get; }
        public decimal Probability { get; }

        internal EnemyTargetOutcome(int mask, decimal probability)
        {
            SlotMask = mask;
            Probability = probability;
            var slots = new List<int>();
            for (int slot = 0; slot < 9; slot++)
                if ((mask & (1 << slot)) != 0) slots.Add(slot);
            Slots = slots.AsReadOnly();
        }
    }

    public sealed class EnemyTargetProbabilityResult
    {
        public EnemyTargetProbabilityStatus Status { get; }
        public IReadOnlyList<EnemyTargetOutcome> Outcomes { get; }

        internal EnemyTargetProbabilityResult(EnemyTargetProbabilityStatus status,
            List<EnemyTargetOutcome> outcomes = null)
        {
            Status = status;
            Outcomes = outcomes == null ? Array.AsReadOnly(Array.Empty<EnemyTargetOutcome>()) : outcomes.AsReadOnly();
        }
    }

    /// <summary>Enumerates target sets on boards of up to nine cells without consuming RNG.</summary>
    public static class EnemyTargetProbabilityEvaluator
    {
        public static EnemyTargetProbabilityResult Evaluate(EnemyTargetObservation rule, int enemySlot,
            IEnumerable<int> occupiedSlots, int allyColumns = 3, int enemyColumns = 2, int boardSlotCount = 9)
        {
            if (rule == null || rule.Status == EnemyTargetStatus.Unavailable)
                return new EnemyTargetProbabilityResult(EnemyTargetProbabilityStatus.Unavailable);
            if (rule.Status == EnemyTargetStatus.UnsupportedTarget)
                return new EnemyTargetProbabilityResult(EnemyTargetProbabilityStatus.UnsupportedTarget);
            if (rule.Status != EnemyTargetStatus.Available || rule.Definition == null)
                return new EnemyTargetProbabilityResult(EnemyTargetProbabilityStatus.InvalidDefinition);
            if (occupiedSlots == null || allyColumns <= 0 || enemyColumns <= 0 ||
                boardSlotCount <= 0 || boardSlotCount > 9 || boardSlotCount % allyColumns != 0 ||
                enemySlot < 0 || enemySlot >= 3L * enemyColumns)
                return new EnemyTargetProbabilityResult(EnemyTargetProbabilityStatus.InvalidInput);

            int occupied = 0;
            foreach (int slot in occupiedSlots)
            {
                if (slot < 0 || slot >= boardSlotCount || (occupied & (1 << slot)) != 0)
                    return new EnemyTargetProbabilityResult(EnemyTargetProbabilityStatus.InvalidInput);
                occupied |= 1 << slot;
            }

            var definition = rule.Definition;
            int enemyRow = enemySlot / enemyColumns;
            bool cells = definition.Filter == BattleTargetFilter.RandomCells;
            var selection = cells ? definition.CellSelection : definition.Selection;
            int candidates = 0;
            var weights = new int[boardSlotCount];
            for (int slot = 0; slot < boardSlotCount; slot++)
            {
                int bit = 1 << slot;
                if (!cells && ((occupied & bit) == 0 ||
                    !Matches(definition.Filter, slot, allyColumns, enemyRow))) continue;
                candidates |= bit;
                weights[slot] = selection == BattleTargetSelection.Weighted
                    ? Weight(cells ? definition.CellWeights : definition.Weights, slot, allyColumns, enemyRow)
                    : 1;
            }
            if (selection == BattleTargetSelection.All)
                return new EnemyTargetProbabilityResult(EnemyTargetProbabilityStatus.Evaluated,
                    new List<EnemyTargetOutcome> { new EnemyTargetOutcome(candidates, 1m) });

            return Enumerate(candidates, occupied, weights,
                Math.Min(boardSlotCount, cells ? definition.CellCount : definition.TargetCount));
        }

        private static EnemyTargetProbabilityResult Enumerate(int candidates, int occupied, int[] weights, int draws)
        {
            int size = 1 << weights.Length;
            var current = new decimal[size];
            var next = new decimal[size];
            var finalized = new decimal[size];
            current[0] = 1m;
            for (int draw = 0; draw < draws; draw++)
            {
                for (int mask = 0; mask < size; mask++)
                {
                    decimal mass = current[mask];
                    if (mass == 0m) continue;
                    int remaining = candidates & ~mask;
                    long total = 0;
                    for (int slot = 0; slot < weights.Length; slot++)
                        if ((remaining & (1 << slot)) != 0) total += weights[slot];
                    if (total == 0)
                    {
                        finalized[mask & occupied] += mass;
                        continue;
                    }
                    for (int slot = 0; slot < weights.Length; slot++)
                    {
                        int bit = 1 << slot;
                        if ((remaining & bit) != 0 && weights[slot] > 0)
                            next[mask | bit] += mass * weights[slot] / total;
                    }
                }
                var previous = current;
                current = next;
                next = previous;
                Array.Clear(next, 0, size);
            }
            for (int mask = 0; mask < size; mask++)
                finalized[mask & occupied] += current[mask];

            // Correct only decimal round-off and keep the adjustment off tiny paths.
            int largest = 0;
            decimal sum = 0m;
            for (int mask = 0; mask < size; mask++)
            {
                sum += finalized[mask];
                if (finalized[mask] > finalized[largest]) largest = mask;
            }
            finalized[largest] += 1m - sum;
            var outcomes = new List<EnemyTargetOutcome>();
            for (int mask = 0; mask < size; mask++)
                if (finalized[mask] > 0m) outcomes.Add(new EnemyTargetOutcome(mask, finalized[mask]));
            return new EnemyTargetProbabilityResult(EnemyTargetProbabilityStatus.Evaluated, outcomes);
        }

        private static bool Matches(BattleTargetFilter filter, int slot, int columns, int enemyRow)
        {
            int column = slot % columns;
            return filter == BattleTargetFilter.AllBoard ||
                (filter == BattleTargetFilter.BackColumn && column == 0) ||
                (filter == BattleTargetFilter.MiddleColumn && column == columns / 2) ||
                (filter == BattleTargetFilter.FrontColumn && column == columns - 1) ||
                (filter == BattleTargetFilter.SameEnemyRow && slot / columns == enemyRow);
        }

        private static int Weight(IReadOnlyList<BattleTargetWeightRule> rules, int slot, int columns, int enemyRow)
        {
            int column = slot % columns;
            int fallback = 1;
            foreach (var rule in rules)
            {
                if (rule.Position == BattleTargetPosition.Else)
                    fallback = rule.Weight;
                else if ((rule.Position == BattleTargetPosition.Back && column == 0) ||
                    (rule.Position == BattleTargetPosition.Middle && column == columns / 2) ||
                    (rule.Position == BattleTargetPosition.Front && column == columns - 1) ||
                    (rule.Position == BattleTargetPosition.SameEnemyRow && slot / columns == enemyRow))
                    return rule.Weight;
            }
            return fallback;
        }
    }
}
