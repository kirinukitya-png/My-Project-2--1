using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Strategy
{
    public sealed class HeuristicV15Settings
    {
        public HeuristicV14Settings Preparation { get; }
        public IntegratedCompositionSettings Composition { get; }

        public HeuristicV15Settings(IntegratedCompositionSettings composition = null, HeuristicV14Settings preparation = null)
        {
            Composition = composition ?? new IntegratedCompositionSettings();
            Preparation = preparation ?? new HeuristicV14Settings();
        }
    }

    public sealed class HeuristicV15CandidateEvaluation
    {
        public GameAction Action { get; }
        public string Status { get; }
        public decimal? Score { get; }
        public decimal Minimum { get; }
        public bool Passes => Score.HasValue && Score.Value > Minimum;
        public IntegratedCompositionComparison? Composition { get; }
        public EconomyValueComparison? Economy { get; }
        public GrowthValueComparison? Growth { get; }
        public ShopOpportunityResult Shop { get; }

        internal HeuristicV15CandidateEvaluation(GameAction action, string status, decimal? score, decimal minimum,
            IntegratedCompositionComparison? composition, EconomyValueComparison? economy,
            GrowthValueComparison? growth, ShopOpportunityResult shop)
        {
            Action = action;
            Status = status;
            Score = score;
            Minimum = minimum;
            Composition = composition;
            Economy = economy;
            Growth = growth;
            Shop = shop;
        }
    }

    public sealed partial class HeuristicStrategy
    {
        public const string Version15 = "15";
        private HeuristicV15Settings _v15;
        private bool UsesV14Preparation => _version == Version14 || _version == Version15 || _version == Version16;

        public static HeuristicStrategy CreateV15(HeuristicV15Settings settings = null,
            Action<string> decisionDiagnosticSink = null, SimulationProfileCollector profiler = null)
        {
            settings ??= new HeuristicV15Settings();
            return new HeuristicStrategy(CreateV13Settings(selectDHealAmount: settings.Preparation.SelectDHealAmount),
                decisionDiagnosticSink, profiler, Version15)
            {
                _v15 = settings,
                _v14 = settings.Preparation,
                _v14Shop = new ShopOpportunityEvaluator(settings.Preparation.Shop)
            };
        }

        public static string GetV15ConfigurationIdentity(HeuristicV15Settings settings = null)
        {
            settings ??= new HeuristicV15Settings();
            var c = settings.Composition;
            var s = c.Synergy;
            var f = c.FourComponentSettings;
            return HashIdentity("integrated-v15-", InvariantFields(new object[]
            {
                Version15, "six-components-1", GetV14ConfigurationIdentity(settings.Preparation),
                s.AttributeCoefficient, s.ProgressCoefficient, s.ActivationCoefficient, c.HandOption.Coefficient,
                f.AttackValue, f.DefenseValue, f.CharacterStarValue, f.IncludeTemporaryCharacterBonuses,
                f.CompositionHandReserveWeight, f.CompositionHandDuplicateWeight, f.MergeProgressOneStarValue,
                f.MergeProgressOneStarTwoStarValue, f.MergeProgressTwoStarTwoCopiesValue,
                c.OneEmptyHandSlotValue, c.FullHandValue
            }));
        }

        public static string GetV15ObservationFingerprint(GameObservation observation, HeuristicV15Settings settings = null) =>
            HashIdentity("visible-v15-", InvariantFields(new object[]
                { GetV15ConfigurationIdentity(settings), GetV14ObservationFingerprint(observation) }));

        /// <summary>Inspects generated preparation candidates before history suppression and early exits.</summary>
        public IReadOnlyList<HeuristicV15CandidateEvaluation> InspectV15Preparation(GameObservation observation,
            ActionId actionId, bool useCache = true, bool includeDetails = false)
        {
            if (_v15 == null) throw new InvalidOperationException("Candidate inspection requires Heuristic v15.");
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (observation.Stage.Phase != ObservationPhase.Preparation)
                throw new ArgumentException("Preparation observation is required.", nameof(observation));
            var generated = V14Candidates(observation, actionId);
            var values = new V14DecisionValues(this, observation, useCache, includeDetails);
            bool enemyless = IsEnemylessPreparation(observation);
            return Array.AsReadOnly(generated.Select(a => EvaluateV14(observation, a, generated, values, enemyless))
                .OrderByDescending(r => r.Score).ThenBy(r => Signature(r.Action), StringComparer.Ordinal)
                .Select(r => new HeuristicV15CandidateEvaluation(r.Action, r.Status.ToString(), r.Score,
                    r.Minimum, r.IntegratedComposition, r.Economy, r.Growth, r.Shop)).ToArray());
        }
    }
}
