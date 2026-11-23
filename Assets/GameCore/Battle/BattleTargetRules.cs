using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleTargetFailure { None, InvalidInput, InvalidDefinition, InvalidRandomState, ArithmeticOverflow }

    public sealed class BattleTargetSelectionResult
    {
        private readonly string[] _targetIds;
        public bool Succeeded { get; }
        public BattleTargetFailure Failure { get; }
        public IReadOnlyList<string> TargetInstanceIds { get; }
        public Pcg32State NextBattleRandomState { get; }
        internal BattleTargetSelectionResult(bool success, BattleTargetFailure failure,
            IEnumerable<string> targetIds, Pcg32State nextState)
        {
            Succeeded = success; Failure = failure;
            _targetIds = targetIds == null ? Array.Empty<string>() : new List<string>(targetIds).ToArray();
            TargetInstanceIds = Array.AsReadOnly(_targetIds);
            NextBattleRandomState = nextState;
        }
    }

    /// <summary>Deterministic, Unity-free implementation of the saved T001-T010 targeting data.</summary>
    public static class BattleTargetRules
    {
        public static BattleTargetSelectionResult SelectTargets(BattleTargetDefinition definition,
            IReadOnlyList<BattleUnitState> allies, BattleUnitState enemy,
            Pcg32State randomState, int allyColumns = 3, int enemyColumns = 2,
            int boardSlotCount = 9)
        {
            if (definition == null || allies == null || allyColumns <= 0 || enemyColumns <= 0 ||
                boardSlotCount <= 0 || boardSlotCount % allyColumns != 0)
                return Fail(BattleTargetFailure.InvalidInput, randomState);
            try { Pcg32RandomSource.ValidateState(randomState); }
            catch (ArgumentException) { return Fail(BattleTargetFailure.InvalidRandomState, randomState); }
            if (NeedsEnemy(definition) && (enemy == null || enemy.Side != BattleUnitSide.Enemy ||
                enemy.SlotIndex >= (long)enemyColumns * 3L))
                return Fail(BattleTargetFailure.InvalidInput, randomState);

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenSlots = new HashSet<int>();
            foreach (BattleUnitState ally in allies)
            {
                if (ally == null || ally.Side != BattleUnitSide.Ally || ally.SlotIndex >= boardSlotCount ||
                    !seenIds.Add(ally.InstanceId) || !seenSlots.Add(ally.SlotIndex))
                    return Fail(BattleTargetFailure.InvalidInput, randomState);
            }

            var rng = Pcg32RandomSource.FromState(randomState);
            try
            {
                if (definition.Filter == BattleTargetFilter.RandomCells)
                    return SelectFromCells(definition, allies, enemy, rng, allyColumns, enemyColumns, boardSlotCount);

                List<BattleUnitState> candidates = GetCandidates(definition.Filter, allies, enemy, allyColumns, enemyColumns);
                List<BattleUnitState> selected = SelectUnits(definition, candidates, enemy, rng, allyColumns, enemyColumns);
                var ids = new List<string>(selected.Count);
                foreach (BattleUnitState unit in selected) ids.Add(unit.InstanceId);
                return Success(ids, rng.CaptureState());
            }
            catch (OverflowException)
            {
                return Fail(BattleTargetFailure.ArithmeticOverflow, randomState);
            }
            catch (ArgumentException)
            {
                return Fail(BattleTargetFailure.InvalidRandomState, randomState);
            }
        }

        private static BattleTargetSelectionResult SelectFromCells(BattleTargetDefinition definition,
            IReadOnlyList<BattleUnitState> allies, BattleUnitState enemy, Pcg32RandomSource rng,
            int allyColumns, int enemyColumns, int boardSlotCount)
        {
            var cells = new List<int>(boardSlotCount);
            for (int i = 0; i < boardSlotCount; i++) cells.Add(i); // Empty cells remain eligible.
            var selectedCells = new HashSet<int>();
            int draws = Math.Min(definition.CellCount, cells.Count);
            for (int i = 0; i < draws; i++)
            {
                int index = ChooseCell(definition, cells, enemy, rng, allyColumns, enemyColumns);
                if (index < 0) break;
                selectedCells.Add(cells[index]);
                cells.RemoveAt(index);
            }
            var selected = new List<string>();
            foreach (BattleUnitState ally in allies)
                if (selectedCells.Contains(ally.SlotIndex)) selected.Add(ally.InstanceId);
            return Success(selected, rng.CaptureState());
        }

        private static List<BattleUnitState> GetCandidates(BattleTargetFilter filter,
            IReadOnlyList<BattleUnitState> allies, BattleUnitState enemy,
            int allyColumns, int enemyColumns)
        {
            var candidates = new List<BattleUnitState>();
            int enemyRow = enemy == null ? -1 : enemy.SlotIndex / enemyColumns;
            foreach (BattleUnitState ally in allies)
            {
                int column = ally.SlotIndex % allyColumns;
                int row = ally.SlotIndex / allyColumns;
                bool include = filter == BattleTargetFilter.AllBoard ||
                    (filter == BattleTargetFilter.BackColumn && column == 0) ||
                    (filter == BattleTargetFilter.MiddleColumn && column == allyColumns / 2) ||
                    (filter == BattleTargetFilter.FrontColumn && column == allyColumns - 1) ||
                    (filter == BattleTargetFilter.SameEnemyRow && row == enemyRow);
                if (include) candidates.Add(ally);
            }
            return candidates;
        }

        private static List<BattleUnitState> SelectUnits(BattleTargetDefinition definition,
            List<BattleUnitState> candidates, BattleUnitState enemy, Pcg32RandomSource rng,
            int allyColumns, int enemyColumns)
        {
            if (definition.Selection == BattleTargetSelection.All) return candidates;
            var selected = new List<BattleUnitState>();
            int draws = Math.Min(definition.TargetCount, candidates.Count);
            for (int draw = 0; draw < draws; draw++)
            {
                int index = definition.Selection == BattleTargetSelection.Uniform
                    ? rng.NextInt(0, candidates.Count)
                    : ChooseWeightedUnit(definition.Weights, candidates, enemy, rng, allyColumns, enemyColumns);
                if (index < 0) break;
                selected.Add(candidates[index]);
                candidates.RemoveAt(index);
            }
            return selected;
        }

        private static int ChooseCell(BattleTargetDefinition definition, List<int> cells,
            BattleUnitState enemy, Pcg32RandomSource rng, int allyColumns, int enemyColumns)
        {
            if (definition.CellSelection == BattleTargetSelection.Uniform)
                return rng.NextInt(0, cells.Count);
            return ChooseWeightedIndex(definition.CellWeights, cells,
                cell => PositionWeight(definition.CellWeights, cell, enemy, allyColumns, enemyColumns), rng);
        }

        private static int ChooseWeightedUnit(IReadOnlyList<BattleTargetWeightRule> rules,
            List<BattleUnitState> candidates, BattleUnitState enemy, Pcg32RandomSource rng,
            int allyColumns, int enemyColumns) => ChooseWeightedIndex(rules, candidates,
                unit => PositionWeight(rules, unit.SlotIndex, enemy, allyColumns, enemyColumns), rng);

        private static int ChooseWeightedIndex<T>(IReadOnlyList<BattleTargetWeightRule> rules,
            List<T> values, Func<T, int> getWeight, Pcg32RandomSource rng)
        {
            long total = 0;
            var weights = new int[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                int weight = getWeight(values[i]);
                if (weight < 0) throw new ArgumentOutOfRangeException(nameof(rules));
                weights[i] = weight;
                total = checked(total + weight);
            }
            if (total <= 0) return -1;
            double roll = rng.NextUnitDouble() * total;
            int lastPositive = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;
                lastPositive = i;
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return lastPositive;
        }

        private static int PositionWeight(IReadOnlyList<BattleTargetWeightRule> rules,
            int slotIndex, BattleUnitState enemy, int allyColumns, int enemyColumns)
        {
            int column = slotIndex % allyColumns;
            int row = slotIndex / allyColumns;
            int enemyRow = enemy == null ? -1 : enemy.SlotIndex / enemyColumns;
            int defaultWeight = 1;
            foreach (BattleTargetWeightRule rule in rules)
            {
                if (rule.Position == BattleTargetPosition.Else) { defaultWeight = rule.Weight; continue; }
                bool matches = (rule.Position == BattleTargetPosition.Back && column == 0) ||
                    (rule.Position == BattleTargetPosition.Middle && column == allyColumns / 2) ||
                    (rule.Position == BattleTargetPosition.Front && column == allyColumns - 1) ||
                    (rule.Position == BattleTargetPosition.SameEnemyRow && row == enemyRow);
                if (matches) return rule.Weight;
            }
            return defaultWeight;
        }

        private static bool NeedsEnemy(BattleTargetDefinition definition)
        {
            if (definition.Filter == BattleTargetFilter.SameEnemyRow) return true;
            foreach (BattleTargetWeightRule rule in definition.Weights)
                if (rule.Position == BattleTargetPosition.SameEnemyRow) return true;
            foreach (BattleTargetWeightRule rule in definition.CellWeights)
                if (rule.Position == BattleTargetPosition.SameEnemyRow) return true;
            return false;
        }

        private static BattleTargetSelectionResult Success(IEnumerable<string> ids, Pcg32State state) =>
            new BattleTargetSelectionResult(true, BattleTargetFailure.None, ids, state);
        private static BattleTargetSelectionResult Fail(BattleTargetFailure failure, Pcg32State state) =>
            new BattleTargetSelectionResult(false, failure, null, state);
    }
}
