using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.SimulationExport
{
    public sealed class V16BattleSummary
    {
        private sealed class Counts
        {
            internal long Total, Preparation, Targeted, Used, ZeroDelta, Fallback, Compared, Changed, Reroll;
            internal long AttackFallback, DefenseFallback, ReferenceFallback, TargetFallback, StatsFallback, ItemFallback, OtherFallback;
            internal long NoEnemy, Disabled, Forced, NotRequired, Requests, Evaluations, Hits, AttackLimits, DefenseLimits, CacheLookups;
            internal long ActionFailure, CurrentShopFailure, FutureShopFailure, BaselineFailure, Previews;
            internal int AttackPeak, DefensePeak;

            internal void Add(HeuristicV16DecisionTelemetry d)
            {
                Total++;
                if (d.Use != V16BattleUse.NonPreparation) Preparation++;
                if (d.Use == V16BattleUse.Used) Used++;
                if (d.Use == V16BattleUse.ZeroDelta) ZeroDelta++;
                if (d.Comparable) Compared++;
                if (d.ActionChanged) Changed++;
                if (d.RerollChanged) Reroll++;
                if (d.Use == V16BattleUse.NoEnemy) NoEnemy++;
                if (d.Use == V16BattleUse.Disabled) Disabled++;
                if (d.Use == V16BattleUse.Forced) Forced++;
                if (d.Use == V16BattleUse.NotRequired) NotRequired++;
                if (d.Use == V16BattleUse.Used || d.Use == V16BattleUse.ZeroDelta || d.Use == V16BattleUse.Fallback) Targeted++;
                if (d.Use == V16BattleUse.Fallback)
                {
                    Fallback++;
                    var failure = d.Failure;
                    switch (failure?.Status)
                    {
                        case BattleValueStatus.AttackStateLimitExceeded: AttackFallback++; break;
                        case BattleValueStatus.DefenseStateLimitExceeded: DefenseFallback++; break;
                        case BattleValueStatus.ReferenceUnavailable: ReferenceFallback++; break;
                        case BattleValueStatus.TargetUnavailable:
                        case BattleValueStatus.UnsupportedTarget:
                        case BattleValueStatus.InvalidDefinition: TargetFallback++; break;
                        case BattleValueStatus.PublicStatsUnavailable: StatsFallback++; break;
                        case BattleValueStatus.ItemBlockUnavailable: ItemFallback++; break;
                        default: OtherFallback++; break;
                    }
                    switch (failure?.Source)
                    {
                        case BattleValueCandidateSource.CurrentBoard: BaselineFailure++; break;
                        case BattleValueCandidateSource.Action: ActionFailure++; break;
                        case BattleValueCandidateSource.CurrentShop: CurrentShopFailure++; break;
                        case BattleValueCandidateSource.FutureShop: FutureShopFailure++; break;
                    }
                }
                Requests += d.Requests; Evaluations += d.Evaluations; Hits += d.Hits; CacheLookups += d.CacheLookups; Previews += d.PreviewCount;
                AttackPeak = Math.Max(AttackPeak, d.AttackPeak); DefensePeak = Math.Max(DefensePeak, d.DefensePeak);
                AttackLimits += d.AttackLimitHits; DefenseLimits += d.DefenseLimitHits;
            }

            internal string Csv() => string.Join(",", Numbers().Select(n => n.ToString(CultureInfo.InvariantCulture))) +
                "," + Rate(Used + ZeroDelta, Targeted) + "," + Rate(Fallback, Targeted) + "," + Rate(Changed, Compared) +
                "," + Rate(Hits, CacheLookups);

            private IEnumerable<long> Numbers() => new[] { Total, Preparation, Targeted, Used, ZeroDelta, Fallback,
                Compared, Changed, Reroll, AttackFallback, DefenseFallback, ReferenceFallback, TargetFallback,
                StatsFallback, ItemFallback, OtherFallback, NoEnemy, Disabled, Forced, NotRequired, Requests,
                Evaluations, Hits, AttackPeak, DefensePeak, AttackLimits, DefenseLimits, BaselineFailure, ActionFailure,
                CurrentShopFailure, FutureShopFailure, Previews, CacheLookups };

            private static string Rate(long numerator, long denominator) => denominator == 0 ? "" :
                ((decimal)numerator / denominator).ToString("G29", CultureInfo.InvariantCulture);
        }

        public const string CsvHeader = "Scope,TrialIndex,TrialSeed,Dimension,Value,Decisions,Preparation,Targeted,Used,ZeroDelta,Fallback,Comparable,ActionChanged,RerollChanged,AttackLimitFallback,DefenseLimitFallback,ReferenceFallback,TargetFallback,StatsFallback,ItemBlockFallback,OtherFallback,NoEnemy,Disabled,Forced,NotRequired,Requests,Evaluations,Hits,AttackPeak,DefensePeak,AttackLimitHits,DefenseLimitHits,BaselineFailure,ActionFailure,CurrentShopFailure,FutureShopFailure,HypotheticalPreviews,CacheLookups,SuccessRate,FallbackRate,ActionChangeRate,CacheHitRate";

        private readonly Counts _total = new Counts();
        private readonly SortedDictionary<(string Dimension, int Value), Counts> _groups =
            new SortedDictionary<(string, int), Counts>(Comparer<(string, int)>.Create((a, b) =>
            {
                int result = StringComparer.Ordinal.Compare(a.Item1, b.Item1);
                return result != 0 ? result : a.Item2.CompareTo(b.Item2);
            }));

        public void Record(HeuristicV16DecisionTelemetry sample)
        {
            _total.Add(sample);
            if (sample.Use == V16BattleUse.NonPreparation) return;
            Add("Stage", sample.Stage, sample);
            Add("Encounter", sample.Encounter, sample);
            Add("EnemyCount", sample.Enemies, sample);
            Add("BoardCount", sample.BoardCount, sample);
            Add("AttackLength", sample.AttackLength ?? -1, sample);
            Add("ShopEvaluated", sample.ShopEvaluated ? 1 : 0, sample);
        }

        private void Add(string dimension, int value, HeuristicV16DecisionTelemetry sample)
        {
            if (!_groups.TryGetValue((dimension, value), out var counts))
                _groups.Add((dimension, value), counts = new Counts());
            counts.Add(sample);
        }

        public IEnumerable<string> ToCsv(string scope, long? trialIndex = null, ulong? trialSeed = null)
        {
            string prefix = FormattableString.Invariant($"{scope},{trialIndex},{trialSeed},");
            yield return prefix + "All,," + _total.Csv();
            foreach (var item in _groups)
                yield return prefix + item.Key.Dimension + "," +
                    (item.Key.Value < 0 ? "" : item.Key.Value.ToString(CultureInfo.InvariantCulture)) + "," + item.Value.Csv();
        }
    }
}
