using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>Copied, Unity-independent shop settings exported from the run configuration.</summary>
    public sealed class CharacterShopConfiguration
    {
        private readonly int[] _charactersPerCost;
        private readonly int[] _initialPoolCountsByCost;
        private readonly int[][] _stageCostWeights;
        private readonly int[] _shopSlotsByStage;

        public IReadOnlyList<int> CharactersPerCost { get; }

        public int StageCount => _stageCostWeights.Length;

        public CharacterShopConfiguration(IEnumerable<int> charactersPerCost,
            IEnumerable<int> initialPoolCountsByCost,
            IEnumerable<IEnumerable<int>> stageCostWeights,
            IEnumerable<int> shopSlotsByStage)
        {
            _charactersPerCost = CopyFiveCounts(charactersPerCost, nameof(charactersPerCost));
            CharactersPerCost = Array.AsReadOnly(_charactersPerCost);
            _initialPoolCountsByCost = CopyFiveCounts(initialPoolCountsByCost, nameof(initialPoolCountsByCost));
            _stageCostWeights = CopyStageWeights(stageCostWeights);
            _shopSlotsByStage = CopyCounts(shopSlotsByStage, nameof(shopSlotsByStage));
            if (_stageCostWeights.Length == 0 || _shopSlotsByStage.Length != _stageCostWeights.Length)
                throw new ArgumentException("Shop slot and cost-weight settings must have the same non-zero stage count.");
            for (int i = 0; i < _shopSlotsByStage.Length; i++)
                if (_shopSlotsByStage[i] < 0) throw new ArgumentOutOfRangeException(nameof(shopSlotsByStage));
        }

        public int GetCharactersPerCost(int cost) => _charactersPerCost[CostIndex(cost)];
        public int GetInitialPoolCount(int cost) => _initialPoolCountsByCost[CostIndex(cost)];
        public int GetCostWeight(int stageNumber, int cost) => _stageCostWeights[StageIndex(stageNumber)][CostIndex(cost)];
        public int GetShopSlotCount(int stageNumber) => _shopSlotsByStage[StageIndex(stageNumber)];

        private int StageIndex(int stageNumber)
        {
            if (stageNumber < 1) throw new ArgumentOutOfRangeException(nameof(stageNumber));
            return Math.Min(stageNumber - 1, _stageCostWeights.Length - 1);
        }

        private static int CostIndex(int cost)
        {
            if (cost < 1 || cost > 5) throw new ArgumentOutOfRangeException(nameof(cost));
            return cost - 1;
        }

        private static int[] CopyFiveCounts(IEnumerable<int> values, string parameterName)
        {
            int[] copied = CopyCounts(values, parameterName);
            if (copied.Length != 5) throw new ArgumentException("Exactly five Cost tiers are required.", parameterName);
            for (int i = 0; i < copied.Length; i++)
                if (copied[i] < 0) throw new ArgumentOutOfRangeException(parameterName);
            return copied;
        }

        private static int[] CopyCounts(IEnumerable<int> values, string parameterName)
        {
            if (values == null) throw new ArgumentNullException(parameterName);
            return new List<int>(values).ToArray();
        }

        private static int[][] CopyStageWeights(IEnumerable<IEnumerable<int>> weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            var rows = new List<int[]>();
            foreach (IEnumerable<int> row in weights)
            {
                int[] copied = CopyCounts(row, nameof(weights));
                if (copied.Length != 5) throw new ArgumentException("Each stage must define five Cost weights.", nameof(weights));
                long sum = 0;
                for (int i = 0; i < copied.Length; i++)
                {
                    if (copied[i] < 0) throw new ArgumentOutOfRangeException(nameof(weights));
                    sum += copied[i];
                }
                if (sum > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(weights), "A stage's total weight must fit in a signed integer.");
                rows.Add(copied);
            }
            return rows.ToArray();
        }
    }
}
