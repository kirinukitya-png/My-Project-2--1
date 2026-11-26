using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectNST.GameCore.Shop
{
    public readonly struct ShopCostProbability
    {
        public string DefinitionId { get; }
        public int Cost { get; }
        public decimal RunCostProbability { get; }
        public decimal SlotProbability { get; }

        internal ShopCostProbability(string id, int cost, decimal runCostProbability, decimal slotProbability)
        {
            DefinitionId = id;
            Cost = cost;
            RunCostProbability = runCostProbability;
            SlotProbability = slotProbability;
        }
    }

    /// <summary>Public marginal probabilities. Actual Shop draws are dependent and use hidden stock.</summary>
    public sealed class ShopCostDistribution
    {
        public string UnavailableReason { get; }
        public IReadOnlyList<int> KnownPerCost { get; }
        public int KnownCount => KnownPerCost.Sum();
        public int UnknownCount { get; }
        public IReadOnlyList<ShopCostProbability> Outcomes { get; }
        public decimal ProbabilityTotal { get; }

        private ShopCostDistribution(string reason, int[] known, int unknown, List<ShopCostProbability> outcomes)
        {
            UnavailableReason = reason;
            KnownPerCost = Array.AsReadOnly(known);
            UnknownCount = unknown;
            Outcomes = outcomes.AsReadOnly();
            ProbabilityTotal = outcomes.Sum(x => x.SlotProbability);
        }

        public static ShopCostDistribution Estimate(IEnumerable<string> roster, IReadOnlyList<int> counts,
            IReadOnlyList<int> weights, IEnumerable<(string DefinitionId, int RunCost)> observed)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (counts == null) throw new ArgumentNullException(nameof(counts));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (observed == null) throw new ArgumentNullException(nameof(observed));
            if (counts.Count != 5 || counts.Any(x => x < 0)) throw new ArgumentException("Invalid Cost counts.", nameof(counts));
            if (weights.Count != 5 || weights.Any(x => x < 0)) throw new ArgumentException("Invalid Cost weights.", nameof(weights));

            var ids = new SortedSet<string>(StringComparer.Ordinal);
            var known = new Dictionary<string, int>(StringComparer.Ordinal);
            var knownCounts = new int[5];
            var outcomes = new List<ShopCostProbability>();
            ShopCostDistribution Result(string reason = null) =>
                new ShopCostDistribution(reason, knownCounts, ids.Count - known.Count, outcomes);
            foreach (string id in roster)
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) return Result("Invalid or duplicate public Roster Definition.");
            if (counts.Sum(x => (long)x) != ids.Count) return Result("Configured Cost counts do not match the public Roster size.");

            foreach (var (id, cost) in observed)
            {
                if (!ids.Contains(id) || cost < 1 || cost > 5) return Result("An observed Definition or RunCost is invalid.");
                if (known.TryGetValue(id, out int previous))
                {
                    if (previous != cost) return Result("Conflicting observed RunCosts for one Definition.");
                    continue;
                }
                known.Add(id, cost);
                if (++knownCounts[cost - 1] > counts[cost - 1]) return Result("Known Cost count exceeds its configured size.");
            }

            int unknown = ids.Count - known.Count;
            long totalWeight = 0;
            for (int i = 0; i < 5; i++)
                if (counts[i] > 0) totalWeight += weights[i];
            if (totalWeight == 0) return Result("No Cost has both positive configured weight and Roster count.");

            for (int i = 0; i < 5; i++)
            {
                if (counts[i] == 0 || weights[i] == 0) continue;
                decimal perDefinition = (decimal)weights[i] / totalWeight / counts[i];
                foreach (string id in ids)
                {
                    decimal runCostProbability = known.TryGetValue(id, out int cost)
                        ? (cost == i + 1 ? 1m : 0m)
                        : (decimal)(counts[i] - knownCounts[i]) / unknown;
                    if (runCostProbability > 0)
                        outcomes.Add(new ShopCostProbability(id, i + 1, runCostProbability,
                            perDefinition * runCostProbability));
                }
            }
            return Result();
        }
    }

    public static class ShopOpportunityMath
    {
        public const decimal ProbabilityTolerance = 0.00000000000000000001m;

        /// <summary>Expected best nonnegative benefit using independent draws from public marginal probabilities.</summary>
        public static decimal ExpectedMaximum(IEnumerable<(decimal Benefit, decimal Probability)> outcomes, int slots)
        {
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));
            if (slots < 0) throw new ArgumentOutOfRangeException(nameof(slots));
            var groups = outcomes.Select(x =>
            {
                if (x.Probability < 0 || x.Probability > 1) throw new ArgumentOutOfRangeException(nameof(outcomes));
                return (Benefit: Math.Max(0m, x.Benefit), x.Probability);
            }).Where(x => x.Probability > 0).GroupBy(x => x.Benefit)
                .OrderBy(g => g.Key)
                .Select(g => (Benefit: g.Key, Probability: g.OrderBy(x => x.Probability).Sum(x => x.Probability))).ToArray();
            decimal total = groups.Sum(g => g.Probability);
            if (Math.Abs(total - 1m) > ProbabilityTolerance)
                throw new ArgumentException("Slot probabilities must sum to one.", nameof(outcomes));
            if (slots == 0) return 0m;

            decimal cumulative = 0m, previous = 0m, result = 0m;
            for (int i = 0; i < groups.Length; i++)
            {
                cumulative += groups[i].Probability;
                // Snap only the endpoint to one after validating the rounding tolerance.
                decimal next = i == groups.Length - 1 ? 1m : Pow(Math.Min(1m, cumulative), slots);
                result += groups[i].Benefit * (next - previous);
                previous = next;
            }
            return result;
        }

        public static decimal Score(decimal future, decimal current, decimal economyDelta, decimal discount)
        {
            if (future < 0) throw new ArgumentOutOfRangeException(nameof(future));
            if (current < 0) throw new ArgumentOutOfRangeException(nameof(current));
            if (discount < 0 || discount > 1) throw new ArgumentOutOfRangeException(nameof(discount));
            return discount * future - current + economyDelta;
        }

        private static decimal Pow(decimal value, int exponent)
        {
            decimal result = 1m;
            while (exponent > 0)
            {
                if ((exponent & 1) != 0) result *= value;
                exponent >>= 1;
                if (exponent > 0) value *= value;
            }
            return result;
        }
    }
}
