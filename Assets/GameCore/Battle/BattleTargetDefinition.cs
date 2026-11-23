using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleTargetFilter { AllBoard, FrontColumn, MiddleColumn, BackColumn, SameEnemyRow, RandomCells }
    public enum BattleTargetSelection { Uniform, Weighted, All }
    public enum BattleTargetPosition { Front, Middle, Back, SameEnemyRow, Else }

    public readonly struct BattleTargetWeightRule
    {
        public BattleTargetPosition Position { get; }
        public int Weight { get; }
        public BattleTargetWeightRule(BattleTargetPosition position, int weight)
        {
            if (!Enum.IsDefined(typeof(BattleTargetPosition), position)) throw new ArgumentOutOfRangeException(nameof(position));
            if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
            Position = position;
            Weight = weight;
        }
    }

    /// <summary>Unity-free interpretation of one TargetData asset, including optional cell-selection data.</summary>
    public sealed class BattleTargetDefinition
    {
        private readonly ReadOnlyCollection<BattleTargetWeightRule> _weights;
        private readonly ReadOnlyCollection<BattleTargetWeightRule> _cellWeights;
        public string TargetId { get; }
        public BattleTargetFilter Filter { get; }
        public BattleTargetSelection Selection { get; }
        public int TargetCount { get; }
        public IReadOnlyList<BattleTargetWeightRule> Weights => _weights;
        public BattleTargetSelection CellSelection { get; }
        public int CellCount { get; }
        public IReadOnlyList<BattleTargetWeightRule> CellWeights => _cellWeights;

        public BattleTargetDefinition(string targetId, BattleTargetFilter filter,
            BattleTargetSelection selection, int targetCount = 1,
            IEnumerable<BattleTargetWeightRule> weights = null,
            BattleTargetSelection cellSelection = BattleTargetSelection.Uniform,
            int cellCount = 0, IEnumerable<BattleTargetWeightRule> cellWeights = null)
        {
            if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("Target ID is required.", nameof(targetId));
            if (!Enum.IsDefined(typeof(BattleTargetFilter), filter)) throw new ArgumentOutOfRangeException(nameof(filter));
            if (!Enum.IsDefined(typeof(BattleTargetSelection), selection)) throw new ArgumentOutOfRangeException(nameof(selection));
            if (!Enum.IsDefined(typeof(BattleTargetSelection), cellSelection) || cellSelection == BattleTargetSelection.All)
                throw new ArgumentOutOfRangeException(nameof(cellSelection));
            if (targetCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetCount));
            if (selection == BattleTargetSelection.Weighted && !HasPositiveWeight(weights))
                throw new ArgumentException("Weighted selection requires at least one positive weight.", nameof(weights));
            if (filter == BattleTargetFilter.RandomCells)
            {
                if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
                if (cellSelection == BattleTargetSelection.Weighted && !HasPositiveWeight(cellWeights))
                    throw new ArgumentException("Weighted cell selection requires at least one positive weight.", nameof(cellWeights));
                if (selection != BattleTargetSelection.All)
                    throw new ArgumentException("Random-cell TargetData selects all occupants of the drawn cells.", nameof(selection));
            }
            else if (cellCount != 0 || (cellWeights != null && HasAny(cellWeights)))
                throw new ArgumentException("Cell selection data is valid only for RandomCells.", nameof(cellCount));

            TargetId = targetId;
            Filter = filter;
            Selection = selection;
            TargetCount = targetCount;
            _weights = Copy(weights, nameof(weights));
            CellSelection = cellSelection;
            CellCount = cellCount;
            _cellWeights = Copy(cellWeights, nameof(cellWeights));
        }

        private static ReadOnlyCollection<BattleTargetWeightRule> Copy(IEnumerable<BattleTargetWeightRule> source, string parameter)
        {
            var result = source == null ? new List<BattleTargetWeightRule>() : new List<BattleTargetWeightRule>(source);
            int elseCount = 0;
            var positions = new HashSet<BattleTargetPosition>();
            foreach (BattleTargetWeightRule rule in result)
            {
                if (!positions.Add(rule.Position)) throw new ArgumentException("Each weight position may appear once.", parameter);
                if (rule.Position == BattleTargetPosition.Else) elseCount++;
            }
            if (elseCount > 1) throw new ArgumentException("Only one Else weight is allowed.", parameter);
            return result.AsReadOnly();
        }

        private static bool HasPositiveWeight(IEnumerable<BattleTargetWeightRule> source)
        {
            if (source == null) return false;
            foreach (BattleTargetWeightRule rule in source) if (rule.Weight > 0) return true;
            return false;
        }

        private static bool HasAny(IEnumerable<BattleTargetWeightRule> source)
        {
            if (source == null) return false;
            foreach (BattleTargetWeightRule _ in source) return true;
            return false;
        }
    }
}
