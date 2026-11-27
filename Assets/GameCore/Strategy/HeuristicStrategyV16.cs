using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Strategy
{
    public sealed class HeuristicV16Settings
    {
        public HeuristicV15Settings BaseSettings { get; }
        public BattleValueSettings Battle { get; }

        public HeuristicV16Settings(HeuristicV15Settings baseSettings = null, BattleValueSettings battle = null)
        {
            BaseSettings = baseSettings ?? new HeuristicV15Settings();
            Battle = battle ?? new BattleValueSettings();
        }
    }

    public sealed class HeuristicV16CandidateEvaluation
    {
        public GameAction Action { get; }
        public string Status { get; }
        public decimal? OriginalScore { get; }
        public decimal? CorrectedScore { get; }
        public decimal? UsedScore { get; }
        public decimal Minimum { get; }
        public IntegratedCompositionComparison? Composition { get; }
        public EconomyValueComparison? Economy { get; }
        public GrowthValueComparison? Growth { get; }
        public decimal? DeltaCorrection { get; }
        public decimal? BattleContribution => DeltaCorrection * 8m;
        public BattleValueStatus? BattleStatus { get; }
        public ShopOpportunityResult OriginalShop { get; }
        public ShopOpportunityResult CorrectedShop { get; }

        internal HeuristicV16CandidateEvaluation(GameAction action, string status, decimal? original,
            decimal? corrected, decimal? used, decimal minimum, IntegratedCompositionComparison? composition,
            EconomyValueComparison? economy, GrowthValueComparison? growth, decimal? delta,
            BattleValueStatus? battleStatus, ShopOpportunityResult originalShop, ShopOpportunityResult correctedShop)
        {
            Action = action; Status = status; OriginalScore = original; CorrectedScore = corrected; UsedScore = used;
            Minimum = minimum; Composition = composition; Economy = economy; Growth = growth; DeltaCorrection = delta;
            BattleStatus = battleStatus; OriginalShop = originalShop; CorrectedShop = correctedShop;
        }
    }

    public sealed class HeuristicV16Inspection
    {
        public IReadOnlyList<HeuristicV16CandidateEvaluation> Candidates { get; }
        public IReadOnlyList<BattleValueDecisionFailure> Failures { get; }
        public BattleValueEvaluation Baseline { get; }
        public string BattleStatus { get; }
        public bool RequiresFallback => Failures.Count != 0;
        public int Requests { get; }
        public int Evaluations { get; }
        public int CacheHits { get; }
        public int AttackPeak { get; }
        public int DefensePeak { get; }

        internal HeuristicV16Inspection(HeuristicV16CandidateEvaluation[] candidates,
            IReadOnlyList<BattleValueDecisionFailure> failures, BattleValueEvaluation baseline,
            BattleValueEvaluator evaluator, string battleStatus)
        {
            Candidates = Array.AsReadOnly(candidates);
            Failures = failures;
            Baseline = baseline;
            BattleStatus = battleStatus;
            Requests = evaluator?.Requests ?? 0;
            Evaluations = evaluator?.Evaluations ?? 0;
            CacheHits = evaluator?.CacheHits ?? 0;
            AttackPeak = evaluator?.AttackPeakStates ?? 0;
            DefensePeak = evaluator?.DefensePeakStates ?? 0;
        }
    }

    public sealed partial class HeuristicStrategy
    {
        public const string Version16 = "16";
        private HeuristicV16Settings _v16;

        public static HeuristicStrategy CreateV16(HeuristicV16Settings settings = null,
            Action<string> decisionDiagnosticSink = null, SimulationProfileCollector profiler = null)
        {
            settings ??= new HeuristicV16Settings();
            var previous = settings.BaseSettings;
            return new HeuristicStrategy(CreateV13Settings(selectDHealAmount: previous.Preparation.SelectDHealAmount),
                decisionDiagnosticSink, profiler, Version16)
            {
                _v16 = settings, _v15 = previous, _v14 = previous.Preparation,
                _v14Shop = new ShopOpportunityEvaluator(previous.Preparation.Shop)
            };
        }

        private static void AppendTargetIdentity(List<object> fields, BattleTargetDefinition definition)
        {
            if (definition == null) { fields.Add(null); return; }
            fields.AddRange(new object[] { definition.TargetId, definition.Filter, definition.Selection,
                definition.TargetCount, definition.CellSelection, definition.CellCount, definition.Weights.Count });
            foreach (var weight in definition.Weights) fields.AddRange(new object[] { weight.Position, weight.Weight });
            fields.Add(definition.CellWeights.Count);
            foreach (var weight in definition.CellWeights) fields.AddRange(new object[] { weight.Position, weight.Weight });
        }

        public static string GetV16ConfigurationIdentity(HeuristicV16Settings settings = null)
        {
            settings ??= new HeuristicV16Settings();
            var battle = settings.Battle;
            var fields = new List<object> { Version16, GetV15ConfigurationIdentity(settings.BaseSettings), battle.EvaluationMode,
                battle.BattleCorrectionWeight, battle.DefenseWeight, battle.ReferenceEnemyATK, battle.ReferenceEnemyDEF,
                battle.ReferenceEnemyGap, battle.AllyAttackMaxStates, battle.EnemyBlockMaxStates,
                "immortal-reference", "whole-decision-original", "decimal-from-public-double",
                "buy-sell-board-swap-transfer-scale8", "parallel-current-max-and-future-cdf-v1" };
            AppendTargetIdentity(fields, battle.ReferenceEnemyTarget);
            return HashIdentity("integrated-v16-", InvariantFields(fields));
        }

        public static string GetV16ObservationFingerprint(GameObservation observation, HeuristicV16Settings settings = null)
        {
            settings ??= new HeuristicV16Settings();
            var fields = new List<object> { GetV16ConfigurationIdentity(settings),
                GetV15ObservationFingerprint(observation, settings.BaseSettings), observation.Stage.EnemyCategory,
                observation.Player.PermanentPlayerBlockBonus, observation.Player.TemporaryPlayerBlockBonus };
            foreach (var enemy in observation.Enemies.OrderBy(e => e.SlotIndex))
            {
                var target = enemy.GetInitialTarget();
                fields.AddRange(new object[] { enemy.SlotIndex, enemy.HP, enemy.Attack, enemy.Defense, enemy.Gap,
                    target?.Status, target?.TargetId });
                AppendTargetIdentity(fields, target?.Definition);
            }
            return HashIdentity("visible-v16-", InvariantFields(fields));
        }

        public HeuristicV16Inspection InspectV16Preparation(GameObservation observation, ActionId actionId,
            bool useCache = true)
        {
            if (_v16 == null) throw new InvalidOperationException("Candidate inspection requires Heuristic v16.");
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (observation.Stage.Phase != ObservationPhase.Preparation)
                throw new ArgumentException("Preparation observation is required.", nameof(observation));
            var generated = V14Candidates(observation, actionId);
            var values = new V14DecisionValues(this, observation, useCache);
            var rows = generated.OrderBy(a => a is RerollShopAction).Select(a =>
            {
                var row = EvaluateV14(observation, a, generated, values, IsEnemylessPreparation(observation));
                values.Battle?.Correct(row);
                return row;
            }).ToArray();
            return MakeV16Inspection(rows, values.Battle);
        }

        private static HeuristicV16Inspection MakeV16Inspection(IEnumerable<V14Evaluation> rows, V16DecisionBattle battle)
        {
            bool useCorrection = battle?.RequiresFallback == false;
            return new HeuristicV16Inspection(rows.Select(row => new HeuristicV16CandidateEvaluation(row.Action,
                row.Status.ToString(), row.Score, row.CorrectedScore, useCorrection ? row.CorrectedScore ?? row.Score : row.Score,
                row.Minimum, row.IntegratedComposition, row.Economy, row.Growth, row.BattleDelta, row.BattleStatus,
                row.Shop, row.CorrectedShop)).ToArray(), battle?.Failures ?? Array.Empty<BattleValueDecisionFailure>(),
                battle?.Baseline, battle?.Evaluator, battle?.Status ?? "Deferred");
        }

        private void PublishV16(GameObservation o, StrategyContext context, IReadOnlyList<V14Evaluation> rows,
            GameAction action, string reason, V14DecisionValues values, HeuristicV16DecisionTelemetry telemetry,
            GameAction original, bool compared, GameAction originalTop, GameAction correctedTop)
        {
            try
            {
                var battle = values?.Battle;
                var result = MakeV16Inspection(rows, battle);
                var b = new StringBuilder()
                    .AppendLine(FormattableString.Invariant($"Strategy={StrategyId} Version={Version16} TrialIndex={context.TrialIndex} DecisionIndex={context.DecisionStep}"))
                    .AppendLine("TraceFormat=v16-1 Detail=" + (_v16DetailedTrace ? "SelectedAndCurrent" : "Numeric") + " VisibleObservationFingerprint=" + GetV16ObservationFingerprint(o, _v16))
                    .AppendLine(FormattableString.Invariant($"BattleStatus={battle?.Status ?? "Deferred"} lambda={_v16.Battle.BattleCorrectionWeight} beta={_v16.Battle.DefenseWeight} fallback={result.RequiresFallback}"))
                    .AppendLine($"Baseline B={TraceNumber(result.Baseline?.Current?.Value)} Ref={TraceNumber(result.Baseline?.Reference?.Value)} R={TraceNumber(result.Baseline?.RawCorrection)} Correction={TraceNumber(result.Baseline?.Correction)}")
                    .AppendLine(FormattableString.Invariant($"BattleWork requests={result.Requests} evaluations={result.Evaluations} hits={result.CacheHits} attackPeak={result.AttackPeak} defensePeak={result.DefensePeak} baselineReuses={battle?.BaselineReuses ?? 0} referenceReuses={battle?.Evaluator?.ReferenceCacheHits ?? 0} boardUnchanged={battle?.Unchanged ?? 0} skippedAfterFallback={battle?.SkippedAfterFailure ?? 0}"));
                foreach (var failure in result.Failures)
                    b.AppendLine($"Fallback source={failure.Source} candidate={failure.CandidateId} status={failure.Status} phase={failure.Phase} limit={failure.StateLimit} attackStatus={failure.AttackStatus} defenseStatus={failure.DefenseStatus} completed={failure.CompletedAttackLayers} attackPeak={failure.AttackPeakStates} defensePeak={failure.DefensePeakStates} originalRestored=True");
                AppendV16Trace(b, o, context, rows, action, values, telemetry, original, compared, originalTop, correctedTop);
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = result.Candidates[i];
                    b.AppendLine($"Candidate {DescribeCandidate(row.Action)} status={row.Status} original={TraceNumber(row.OriginalScore)} delta={TraceNumber(row.DeltaCorrection)} corrected={TraceNumber(row.CorrectedScore)} used={TraceNumber(row.UsedScore)} battleStatus={row.BattleStatus}");
                    AppendV16CandidateTrace(b, rows[i], row, result.RequiresFallback, action, reason);
                }
                b.Append("FinalAction=").Append(action == null ? "NoAction" : DescribeCandidate(action))
                    .Append(" FinalReason=").AppendLine(reason);
                SendDecisionTrace(b.ToString());
            }
            catch (Exception error) { ReportDecisionTraceError(false, error); }
        }

        private sealed class V16DecisionBattle
        {
            private readonly GameObservation _observation;
            private readonly BattleValueSettings _settings;
            private readonly bool _cache;
            private readonly string _skip;
            private readonly bool _retainComparisons, _details;
            private BattleValueComparison _actualComparison;
            private BattleValueDecision _decision;
            private BattleBoardInput _board;
            public BattleValueEvaluator Evaluator { get; private set; }
            public BattleValueEvaluation Baseline { get; private set; }
            public bool RequiresFallback => _decision?.RequiresFallback == true;
            public IReadOnlyList<BattleValueDecisionFailure> Failures => _decision?.Failures ?? Array.Empty<BattleValueDecisionFailure>();
            public string Status => _skip ?? (RequiresFallback ? "Fallback" : Baseline == null ? "NotRequired" : "Evaluated");
            public bool CanEvaluate => _skip == null && !RequiresFallback;
            public int BaselineReuses { get; private set; }
            public int Unchanged { get; private set; }
            public int SkippedAfterFailure { get; private set; }

            public V16DecisionBattle(GameObservation observation, BattleValueSettings settings, bool cache,
                bool retainComparisons, bool details)
            {
                _observation = observation;
                _settings = settings;
                _cache = cache;
                _retainComparisons = retainComparisons;
                _details = details;
                if (settings.BattleCorrectionWeight == 0m) _skip = "Disabled";
                else if ((observation.Enemies.Count > 0 && observation.Enemies.All(e => e.HP == 0)) ||
                    (observation.Enemies.Count == 0 && observation.Stage.EnemyCategory == Stages.StageEnemyCategory.None))
                    _skip = "NoEnemy";
            }

            private bool Ready()
            {
                if (!CanEvaluate)
                {
                    if (RequiresFallback) SkippedAfterFailure++;
                    return false;
                }
                if (Baseline != null) { BaselineReuses++; return true; }
                Evaluator = BattleValueEvaluator.FromObservation(_observation, _settings, _cache);
                Evaluator.RetainTraceInputs = _details;
                _decision = new BattleValueDecision(Evaluator);
                Baseline = Evaluator.EvaluateCurrent();
                _decision.Record(BattleValueCandidateSource.CurrentBoard, "current", Baseline);
                return !RequiresFallback;
            }

            private bool ReadyForAction()
            {
                if (!Ready()) return false;
                Evaluator.RetainCandidateDetails = _details;
                return true;
            }

            private decimal? Record(BattleValueComparison result, BattleValueCandidateSource source, string id)
            {
                _decision.Record(source, id, result.After);
                if (_retainComparisons && source == BattleValueCandidateSource.Action) _actualComparison = result;
                return result.DeltaCorrection;
            }

            public decimal? CorrectPurchase(CharacterPurchasePreviewResult preview, BattleValueCandidateSource source, string id)
            {
                if (!preview.SlotChanges.Any(change => change.Location.Kind == CharacterLocationKind.Board))
                {
                    Unchanged++;
                    return 0m;
                }
                bool ready = source == BattleValueCandidateSource.Action ? ReadyForAction() : Ready();
                return ready ? Record(Evaluator.ComparePurchasePreview(Baseline, preview), source, id) : (decimal?)null;
            }

            public void Correct(V14Evaluation row)
            {
                if (!row.Score.HasValue || row.Action is RerollShopAction) return;
                _actualComparison = null;
                decimal? delta;
                string id = Signature(row.Action);
                switch (row.Action)
                {
                    case BuyCharacterAction:
                        delta = CorrectPurchase(row.IntegratedPurchase.Preview, BattleValueCandidateSource.Action, id);
                        break;
                    case SellCharacterAction sell:
                        var member = _observation.Characters.First(c => c.InstanceId == sell.InstanceId);
                        if (member.LocationKind != ObservationLocationKind.Board) { Unchanged++; delta = 0m; }
                        else delta = ReadyForAction() ? Record(Evaluator.Compare(Baseline,
                            (_board ??= BattleBoardInput.FromObservation(_observation)).WithSlot(member.SlotIndex, null)),
                            BattleValueCandidateSource.Action, id) : (decimal?)null;
                        break;
                    case MoveOrSwapBoardAction move:
                        delta = ReadyForAction() ? Record(Evaluator.Compare(Baseline,
                            (_board ??= BattleBoardInput.FromObservation(_observation)).WithBoardSwap(move.SourceSlot, move.TargetSlot)),
                            BattleValueCandidateSource.Action, id) : (decimal?)null;
                        break;
                    case MoveBoardToHandAction transfer:
                        delta = ReadyForAction() ? Record(Evaluator.CompareTransfer(Baseline, transfer.BoardSlot, transfer.HandSlot),
                            BattleValueCandidateSource.Action, id) : (decimal?)null;
                        break;
                    case MoveHandToBoardAction deploy:
                        delta = ReadyForAction() ? Record(Evaluator.CompareTransfer(Baseline, deploy.BoardSlot, deploy.HandSlot),
                            BattleValueCandidateSource.Action, id) : (decimal?)null;
                        break;
                    default:
                        return;
                }
                if (Evaluator != null) Evaluator.RetainCandidateDetails = false;
                row.BattleDelta = delta ?? (_skip != null ? (decimal?)0m : null);
                row.BattleComparison = _actualComparison;
                _actualComparison = null;
                if (_skip == "Disabled") row.BattleStatus = BattleValueStatus.Disabled;
                else if (_skip == "NoEnemy") row.BattleStatus = BattleValueStatus.NoEnemy;
                else if (delta.HasValue) row.BattleStatus = BattleValueStatus.Evaluated;
                else if (RequiresFallback) row.BattleStatus = Failures.FirstOrDefault(f => f.Source == BattleValueCandidateSource.Action &&
                    f.CandidateId == id)?.Status ?? Baseline?.Status;
                if (row.BattleDelta.HasValue) row.CorrectedScore = row.Score + 8m * row.BattleDelta;
            }
        }
    }
}
