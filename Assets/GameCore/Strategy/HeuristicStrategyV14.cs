using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Strategy
{
    public sealed class HeuristicV14Settings
    {
        public ShopOpportunitySettings Shop { get; }
        public EconomyValueSettings Economy => Shop.Economy;
        public GrowthValueSettings Growth { get; }
        public int SelectDHealAmount { get; }
        public int PreparationLimit => 30;
        public int ExperienceMinimumGold => 8;
        public int RerollMinimumGold => 12;

        public HeuristicV14Settings(EconomyValueSettings economy = null, GrowthValueSettings growth = null,
            decimal compositionWeight = 8m, decimal futureDiscount = .70m, int selectDHealAmount = 10)
        {
            if (selectDHealAmount < 0) throw new ArgumentOutOfRangeException(nameof(selectDHealAmount));
            Growth = growth ?? new GrowthValueSettings(12m, .50m, 0m);
            if (Growth.OpportunityPerSlot != 0)
                throw new ArgumentException("v14 requires M=0 across all preparation actions.", nameof(growth));
            Shop = new ShopOpportunitySettings(economy ?? EconomyValueSettings.TwoSegment(.70m, .35m, 8, .70m),
                compositionWeight, futureDiscount);
            SelectDHealAmount = selectDHealAmount;
        }

        public decimal RequiredBenefit(bool enemyless, bool reroll) => reroll
            ? (enemyless ? 4m : 2m)
            : (enemyless ? 2m : 1m);
    }

    public sealed partial class HeuristicStrategy
    {
        private HeuristicV14Settings _v14;
        private ShopOpportunityEvaluator _v14Shop;

        public static HeuristicStrategy CreateV14(HeuristicV14Settings settings = null,
            Action<string> decisionDiagnosticSink = null, SimulationProfileCollector profiler = null)
        {
            settings ??= new HeuristicV14Settings();
            return new HeuristicStrategy(CreateV13Settings(selectDHealAmount: settings.SelectDHealAmount),
                decisionDiagnosticSink, profiler, Version14)
            {
                _v14 = settings,
                _v14Shop = new ShopOpportunityEvaluator(settings.Shop)
            };
        }

        public static string GetV14ConfigurationIdentity(HeuristicV14Settings settings = null)
        {
            settings ??= new HeuristicV14Settings();
            var e = settings.Economy;
            var g = settings.Growth;
            object[] fields = { "v14-integrated-1", settings.Shop.CompositionWeight, e.HoldingModel, e.ReserveRate,
                e.SurplusRate, e.ReserveGold, e.InterestWeight, g.CapacityPerSlot, g.ProgressFraction,
                g.OpportunityPerSlot, settings.Shop.FutureDiscount, settings.ExperienceMinimumGold,
                settings.RerollMinimumGold, settings.RequiredBenefit(false, false), settings.RequiredBenefit(true, false),
                settings.RequiredBenefit(false, true), settings.RequiredBenefit(true, true),
                settings.PreparationLimit, settings.SelectDHealAmount };
            return HashIdentity("integrated-v14-", InvariantFields(fields) +
                "|conditional-interest|cost-quota-independent-slots|unavailable-current-exclude-future-zero" +
                "|xp-public-new-increment|empty-enemy-board-positive|overlevel-legacy|signature-reverse-trades" +
                "|no-item-buy-reroll|item-sale-full-no-positive-use|legacy-item-use-choice-event");
        }

        public static string GetV14ObservationFingerprint(GameObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            var e = observation.EconomyRules;
            var shop = observation.Shop;
            var stage = observation.Stage;
            var choice = stage.SelectedChoiceDetails;
            var weights = shop?.CostWeights;
            var snapshot = new StringBuilder("v14-public-1|")
                .Append(CreateVisibleObservationFingerprint(observation, true, Version13)).Append('|')
                .Append(InvariantFields(new object[] { observation.IsStrategyActionAllowed, observation.Player.HasHealth,
                    observation.Board.Slots.Count, e?.ExperiencePurchaseGoldCost, e?.ExperiencePerPurchase,
                    e?.MaximumLevel, e?.CanPurchaseExperience, e?.ShopRerollGoldCost, e?.GoldPerInterestThreshold,
                    e?.InterestGoldPerThreshold, e?.MaximumInterestGold, e?.BaseStageGoldReward, shop?.SlotCount,
                    stage.HasSelection, stage.ShopMode, choice?.PostBattleGold, choice?.PostBattleXP,
                    choice?.BossRewardOverridesChoice, weights?.Cost1Weight, weights?.Cost2Weight, weights?.Cost3Weight,
                    weights?.Cost4Weight, weights?.Cost5Weight }));
            if (e != null) snapshot.Append("|xp:").Append(InvariantFields(e.ExperienceCurve.Thresholds.Cast<object>()));
            if (weights != null) snapshot.Append("|counts:").Append(InvariantFields(weights.CharactersPerCost.Cast<object>()));
            foreach (var definition in observation.SelectedRoster.OrderBy(d => d.DefinitionId, StringComparer.Ordinal))
            {
                snapshot.Append("|roster:");
                AppendFingerprintString(snapshot, definition.DefinitionId);
                AppendFingerprintTags(snapshot, definition.Attributes, definition.Units);
            }
            foreach (var slot in observation.Hand.Slots.Where(x => x.Item != null).OrderBy(x => x.SlotIndex))
                AppendItem(snapshot, slot.Item, slot.SlotIndex);
            if (observation.EventItem?.Item != null) AppendItem(snapshot, observation.EventItem.Item, -1);
            return HashIdentity("visible-v14-", snapshot.ToString());
        }

        private static void AppendItem(StringBuilder text, ItemObservation item, int slot)
        {
            text.Append("|item:").Append(InvariantFields(new object[] { slot, item.DefinitionId, item.Target, item.Duration }));
            foreach (var effect in item.Effects)
                text.Append('|').Append(InvariantFields(new object[] { effect.Kind, effect.Value, effect.Duration }));
        }

        private static string InvariantFields(IEnumerable<object> values) => string.Join("|", values.Select(value =>
        {
            string text = value is decimal d ? d.ToString("G29", CultureInfo.InvariantCulture) :
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text;
        }));

        private enum V14Status
        {
            Scored, Finish, RepeatedOrReverse, Deferred, ItemPolicy, UnavailablePurchase,
            OverLevel, DuplicateDefinition, ExperienceUnavailable, LowGoldExperience, LowGoldReroll, UnavailableReroll, HandReorder
        }

        private sealed class V14Evaluation
        {
            public GameAction Action { get; }
            public V14Status Status { get; set; }
            public decimal? Score { get; set; }
            public decimal Minimum { get; set; }
            public bool Passes => Score.HasValue && Score.Value > Minimum;
            public bool IncreasesBoard { get; set; }
            public bool? GoldGatePassed { get; set; }
            public CompositionValue? Composition { get; set; }
            public IntegratedCompositionComparison? IntegratedComposition { get; set; }
            public IntegratedPurchaseEvaluation IntegratedPurchase { get; set; }
            public PurchaseCompositionFailure? PurchaseFailure { get; set; }
            public int? RunCost { get; set; }
            public int? Price { get; set; }
            public int? MergeStages { get; set; }
            public V14CapacityUse? CapacityUse { get; set; }
            public EconomyValueComparison? Economy { get; set; }
            public GrowthValueComparison? Growth { get; set; }
            public ShopOpportunityResult Shop { get; set; }
            public ShopOpportunityResult CorrectedShop { get; set; }
            public decimal? CorrectedScore { get; set; }
            public decimal? BattleDelta { get; set; }
            public ProjectNST.GameCore.Battle.BattleValueStatus? BattleStatus { get; set; }
            public ProjectNST.GameCore.Battle.BattleValueComparison BattleComparison { get; set; }
            public V14Evaluation(GameAction action, V14Status status = V14Status.Scored) { Action = action; Status = status; }
        }

        private sealed class V14DecisionValues
        {
            private readonly HeuristicStrategy _strategy;
            private readonly GameObservation _observation;
            private CompositionValueEvaluator _composition;
            private IntegratedCompositionValueEvaluator _integrated;
            private readonly bool _useCache, _includeDetails;
            private readonly bool _retainTraceStates;
            private int _purchaseHits;
            private EconomyValueEvaluator _economy;
            private EconomyValueEvaluator.Baseline _gold;
            private GrowthValueEvaluator.Baseline _growth;
            private ShopOpportunityResult _shop;
            public ShopOpportunityResult CorrectedShop { get; private set; }
            public V16DecisionBattle Battle { get; }
            private readonly Dictionary<(string, CharacterLocationKind, int), PurchaseCompositionEvaluation> _purchases =
                new Dictionary<(string, CharacterLocationKind, int), PurchaseCompositionEvaluation>();
            private Dictionary<(string, CharacterLocationKind, int), IntegratedPurchaseEvaluation> _integratedPurchases;

            public V14DecisionValues(HeuristicStrategy strategy, GameObservation observation,
                bool useCache = true, bool includeDetails = false, bool retainTraceStates = false)
            {
                _strategy = strategy;
                _observation = observation;
                _useCache = useCache;
                _includeDetails = includeDetails;
                _retainTraceStates = retainTraceStates;
                if (strategy._v16 != null)
                    Battle = new V16DecisionBattle(observation, strategy._v16.Battle, useCache,
                        strategy._decisionDiagnosticSink != null, strategy._decisionDiagnosticSink != null && strategy._v16DetailedTrace);
            }

            public CompositionValueEvaluator Composition => _composition ??= new CompositionValueEvaluator(_observation,
                _strategy._settings.CompositionHandReserveWeight, _strategy._settings,
                _strategy._settings.CompositionHandDuplicateWeight);
            public bool UsesIntegrated => _strategy._v15 != null;
            public IntegratedCompositionValueEvaluator Integrated => _integrated ??= new IntegratedCompositionValueEvaluator(
                _observation, _strategy._v15.Composition, _includeDetails, _useCache, _retainTraceStates);
            public IntegratedCompositionValueEvaluator ExistingIntegrated => _integrated;
            public (int Entries, int Reuses) PurchaseCounts => (_integratedPurchases?.Count ?? 0, _purchaseHits);
            public EconomyValueEvaluator Economy => _economy ??= new EconomyValueEvaluator(_strategy._v14.Economy,
                EconomyInterestObservation.FromObservation(_observation));
            public EconomyValueEvaluator.Baseline Gold => _gold ??= Economy.CreateBaseline(_observation.Player.Gold);
            public GrowthValueEvaluator.Baseline Growth => _growth ??=
                GrowthValueEvaluator.CreateBaseline(_strategy._v14.Growth, _observation);

            public PurchaseCompositionEvaluation Purchase(BuyCharacterAction action)
            {
                var key = (action.OfferId, action.Placement.Kind, action.Placement.SlotIndex);
                if (!_purchases.TryGetValue(key, out var result))
                {
                    result = Composition.ComparePurchase(action);
                    _purchases.Add(key, result);
                }
                return result;
            }

            public IntegratedPurchaseEvaluation IntegratedPurchase(BuyCharacterAction action)
            {
                _integratedPurchases ??= new Dictionary<(string, CharacterLocationKind, int), IntegratedPurchaseEvaluation>();
                var key = (action.OfferId, action.Placement.Kind, action.Placement.SlotIndex);
                if (!_integratedPurchases.TryGetValue(key, out var result))
                {
                    result = Integrated.ComparePurchase(action);
                    _integratedPurchases.Add(key, result);
                }
                else _purchaseHits++;
                return result;
            }

            public ShopOpportunityResult Shop()
            {
                if (_shop != null) return _shop;
                string path = UsesIntegrated ? "Strategy.V15" : "Strategy.V14";
                _strategy._profiler?.Add(path + ".RerollEvaluations");
                using (_strategy._profiler == null ? default(SimulationProfileCollector.Scope) :
                    _strategy._profiler.Measure(path + ".ShopOpportunity"))
                {
                    if (Battle?.CanEvaluate == true)
                    {
                        var result = _strategy._v14Shop.EvaluateWithBattle(_observation, Integrated, Economy, Gold,
                            IntegratedPurchase, _includeDetails || _retainTraceStates ||
                                (_strategy._decisionDiagnosticSink != null && _strategy._v16DetailedTrace),
                            (preview, source, id) => 8m * Battle.CorrectPurchase(preview, source, id));
                        CorrectedShop = result.Corrected;
                        return _shop = result.Original;
                    }
                    return _shop = UsesIntegrated
                        ? _strategy._v14Shop.EvaluateIntegrated(_observation, Integrated, Economy, Gold, IntegratedPurchase,
                            _includeDetails || _retainTraceStates)
                        : _strategy._v14Shop.EvaluateShared(_observation, Composition, Economy, Gold, Purchase);
                }
            }

            public void RecordIntegratedCounters()
            {
                var profiler = _strategy._profiler;
                if (_integrated == null || profiler == null) return;
                var counts = _integrated.CacheStatistics;
                profiler.Add("Strategy.V15.IntegratedContexts");
                profiler.Add("Strategy.V15.SynergyEvaluations", counts.SynergyEvaluations);
                profiler.Add("Strategy.V15.SynergyCacheHits", counts.SynergyCacheHits);
                profiler.Add("Strategy.V15.OptionCacheHits", counts.OptionCacheHits);
                profiler.Add("Strategy.V15.OptionSearches", _integrated.DiagnosticCounts.OptionSearches);
                if (_shop != null)
                    profiler.Add("Strategy.V15.HypotheticalPurchasePreviews", _shop.HypotheticalPreviewCount);
            }
        }

        private GameAction[] V14Candidates(GameObservation o, ActionId id)
        {
            var rules = o.EconomyRules;
            int price = rules.ExperiencePurchaseGoldCost;
            var source = new RandomStrategy(new RandomStrategySettings(_v14.PreparationLimit, rules.ShopRerollGoldCost,
                price, price), _profiler);
            return source.GetCandidates(o, id).ToArray();
        }

        private IReadOnlyList<HeuristicActionEvaluation> InspectV14Preparation(GameObservation o, ActionId id)
        {
            if (_v16 != null)
                return Array.AsReadOnly(InspectV16Preparation(o, id).Candidates.OrderByDescending(r => r.UsedScore)
                    .ThenBy(r => Signature(r.Action), StringComparer.Ordinal).Select(r => new HeuristicActionEvaluation(
                        r.Action, r.UsedScore.HasValue ? (double)r.UsedScore.Value :
                            (r.Status == nameof(V14Status.Finish) ? (double)r.Minimum : double.NegativeInfinity),
                        r.Status, isSelectable: r.UsedScore.HasValue || r.Status == nameof(V14Status.Finish))).ToArray());
            var candidates = V14Candidates(o, id);
            var values = new V14DecisionValues(this, o);
            bool enemyless = IsEnemylessPreparation(o);
            return Array.AsReadOnly(candidates.Select(a => EvaluateV14(o, a, candidates, values, enemyless))
                .OrderByDescending(r => r.Score).ThenBy(r => Signature(r.Action), StringComparer.Ordinal)
                .Select(r => new HeuristicActionEvaluation(r.Action, r.Score.HasValue ? (double)r.Score.Value :
                    (r.Status == V14Status.Finish ? (double)r.Minimum : double.NegativeInfinity),
                    FormatV14(r), isSelectable: r.Score.HasValue || r.Status == V14Status.Finish)).ToArray());
        }

        private StrategyDecision ChooseV14Preparation(GameObservation o, StrategyContext context)
        {
            var state = ReadState(context.State);
            bool sameEncounter = state.HasEncounter && state.EncounterIndex == o.Stage.EncounterIndex;
            int decisions = sameEncounter ? Increment(state.Decisions) : 1;
            string previous = sameEncounter ? state.LastAction : string.Empty;
            bool enemyless = IsEnemylessPreparation(o);
            bool limit = decisions >= _v14.PreparationLimit;
            var trace = _decisionDiagnosticSink == null ? null : new List<V14Evaluation>();
            V14DecisionValues values = null;
            var evaluations = new List<V14Evaluation>();
            GameAction originalChoice = null;
            V14Evaluation originalTop = null, correctedTop = null;
            bool compared = false;

            bool candidatesGenerated = false;
            int generatedCount = 0;
            StrategyDecision Finish(GameAction action, string reason)
            {
                var next = NewState(decisions, action == null ? string.Empty : Signature(action),
                    o.Stage.EncounterIndex, o.Stage.Phase);
                var decision = action == null ? context.NoAction(next, reason) : context.Decide(action, next, reason);
                if (_v16 != null && (_v16Telemetry != null || trace != null))
                {
                    var summary = ObserveV16(o, values?.Battle, evaluations, compared, originalChoice, action, generatedCount);
                    SendV16Telemetry(summary);
                    if (trace != null)
                        PublishV16(o, context, trace, action, reason, values, summary, originalChoice, compared,
                            originalTop?.Action, correctedTop?.Action);
                }
                if (trace != null)
                {
                    if (_v15 == null) PublishV14(o, context, decisions, trace, action, reason, candidatesGenerated);
                    else if (_v16 == null) PublishV15(o, context, decisions, trace, action, reason, candidatesGenerated, values);
                }
                return decision;
            }

            if (!o.IsStrategyActionAllowed) return Finish(null, "No player decision is required.");
            if (enemyless && limit) return Finish(null, "Preparation limit; ResolveEnemylessStage.");
            var generated = V14Candidates(o, context.NextActionId);
            candidatesGenerated = true;
            generatedCount = generated.Length;
            var candidates = new List<GameAction>();
            foreach (var a in generated)
            {
                if (Signature(a) == previous || IsReverse(previous, a) ||
                    IsImmediateBuyThenSell(previous, a, o) || IsImmediateSellThenBuy(previous, a))
                    trace?.Add(new V14Evaluation(a, V14Status.RepeatedOrReverse));
                else candidates.Add(a);
            }
            var start = candidates.FirstOrDefault(a => a is StartBattleAction);
            StrategyDecision End(string reason) => Finish(start, reason + (enemyless ? "; ResolveEnemylessStage." :
                start != null ? "; StartBattle." : "; no legal StartBattle."));
            void TraceDeferred()
            {
                if (trace == null) return;
                foreach (var a in candidates)
                    if (!trace.Any(x => ReferenceEquals(x.Action, a)))
                        trace.Add(new V14Evaluation(a, V14Status.Deferred));
            }

            if (BoardCount(o) > o.Player.Level)
            {
                var board = new BoardEvaluationContext(this, o);
                var merge = CollectRepairMergeCounts(o);
                var formation = CreateCompositionEvaluator(o);
                var repair = candidates.Where(a => a is SellCharacterAction || a is MoveBoardToHandAction || a is BuyExperienceAction)
                    .Select(a => Evaluate(o, a, generated, state, decisions, board, null, null, merge, formation)).ToArray();
                if (TryChooseOverLevelResolution(o, repair, board, out var chosen))
                {
                    TraceDeferred();
                    return Finish(chosen.Action, "LevelRepair: " + chosen.Reason);
                }
            }
            if (limit)
            {
                TraceDeferred();
                return End("Preparation limit");
            }

            values = new V14DecisionValues(this, o, retainTraceStates: trace != null && _v15 != null && _v15TraceDetails);
            void EvaluateCandidate(GameAction a, bool correct = true)
            {
                var result = EvaluateV14(o, a, generated, values, enemyless);
                if (correct) values.Battle?.Correct(result);
                evaluations.Add(result);
                trace?.Add(result);
            }
            decimal? UsedScore(V14Evaluation row) => values.Battle?.RequiresFallback == false
                ? row.CorrectedScore ?? row.Score : row.Score;
            V14Evaluation Best(Func<V14Evaluation, bool> eligible, bool original = false) => evaluations.Where(eligible)
                .OrderByDescending(x => original ? x.Score : UsedScore(x))
                .ThenBy(x => Signature(x.Action), StringComparer.Ordinal).FirstOrDefault();

            bool emptyPriority = !enemyless && BoardCount(o) == 0;
            if (emptyPriority)
            {
                foreach (var a in candidates.Where(IsV14BoardAddition))
                    EvaluateCandidate(a, correct: false);
                var addition = Best(x => x.IncreasesBoard && x.Score > 0, original: true);
                if (addition != null)
                {
                    TraceDeferred();
                    values.RecordIntegratedCounters();
                    return Finish(addition.Action, "EmptyBoardPriority: positive legal Board addition.");
                }
                if (values.Battle != null)
                    foreach (var row in evaluations) values.Battle.Correct(row);
            }
            bool deferReroll = values.Battle?.CanEvaluate == true;
            foreach (var a in candidates)
                if ((!emptyPriority || !IsV14BoardAddition(a)) && (!deferReroll || !(a is RerollShopAction)))
                    EvaluateCandidate(a);
            if (deferReroll)
                foreach (var a in candidates.OfType<RerollShopAction>()) EvaluateCandidate(a);
            var winner = Best(x => UsedScore(x) > x.Minimum);
            if (_v16 != null && (_v16Telemetry != null || trace != null))
            {
                compared = true;
                originalChoice = Best(x => x.Score > x.Minimum, original: true)?.Action ?? start;
                if (trace != null)
                {
                    originalTop = Best(x => x.Score.HasValue, original: true);
                    if (values.Battle?.RequiresFallback != true) correctedTop = Best(x => UsedScore(x).HasValue);
                }
            }
            values.RecordIntegratedCounters();
            return winner == null ? End("No preparation candidate strictly exceeds its minimum benefit") :
                Finish(winner.Action, "Highest raw score among eligible preparation candidates.");
        }

        private static bool IsV14BoardAddition(GameAction a) => a is MoveHandToBoardAction ||
            a is BuyCharacterAction buy && buy.Placement.Kind == CharacterLocationKind.Board;

        private V14Evaluation EvaluateV14(GameObservation o, GameAction a, IReadOnlyList<GameAction> generated,
            V14DecisionValues values, bool enemyless)
        {
            var r = new V14Evaluation(a) { Minimum = _v14.RequiredBenefit(enemyless, a is RerollShopAction) };
            decimal score = 0;
            void Shape(CompositionComparison change, bool synergyOnly = false)
            {
                r.Composition = change.Delta;
                r.IncreasesBoard = change.After.BoardCount > change.Before.BoardCount;
                score += _v14.Shop.CompositionWeight * (decimal)(synergyOnly ? change.Delta.BoardSynergy : change.TotalDelta);
            }
            void IntegratedShape(IntegratedCompositionComparison change, bool boardMove = false)
            {
                r.IntegratedComposition = change;
                r.IncreasesBoard = change.After.BoardCount > change.Before.BoardCount;
                score += _v14.Shop.CompositionWeight * (boardMove
                    ? change.Delta.BoardSynergy + change.Delta.HandUnitOption : change.TotalDelta);
            }
            void Funds(int change)
            {
                r.Economy = values.Gold.CompareGoldChange(change);
                score += r.Economy.Value.TotalDelta;
            }

            switch (a)
            {
                case StartBattleAction:
                    r.Status = V14Status.Finish;
                    break;
                case BuyCharacterAction buy:
                    var integratedPurchase = values.UsesIntegrated ? values.IntegratedPurchase(buy) : null;
                    r.IntegratedPurchase = integratedPurchase;
                    var purchase = values.UsesIntegrated ? null : values.Purchase(buy);
                    var preview = integratedPurchase?.Preview ?? purchase.Preview;
                    r.PurchaseFailure = integratedPurchase?.Failure ?? purchase.Failure;
                    var offer = Offer(o, buy.OfferId);
                    r.RunCost = offer?.RunCost;
                    r.Price = offer?.DisplayPrice;
                    if (preview.Succeeded) r.MergeStages = preview.MergeStageCount;
                    if (preview.After.OverLevelCount > preview.Before.OverLevelCount)
                        r.Status = V14Status.OverLevel;
                    else if (!(integratedPurchase?.Succeeded ?? purchase.Succeeded))
                        r.Status = V14Status.UnavailablePurchase;
                    else
                    {
                        if (integratedPurchase != null) IntegratedShape(integratedPurchase.Comparison.Value);
                        else Shape(purchase.Comparison.Value);
                        Funds(-Offer(o, buy.OfferId).DisplayPrice);
                    }
                    break;
                case SellCharacterAction sell:
                    var c = o.Characters.First(x => x.InstanceId == sell.InstanceId);
                    if (values.UsesIntegrated)
                        IntegratedShape(c.LocationKind == ObservationLocationKind.Board
                            ? values.Integrated.CompareBoardRemoval(c.SlotIndex) : values.Integrated.CompareHandRemoval(c.SlotIndex));
                    else Shape(c.LocationKind == ObservationLocationKind.Board ? values.Composition.CompareBoardRemoval(c.SlotIndex)
                        : values.Composition.CompareHandRemoval(c.SlotIndex));
                    r.RunCost = c.RunCost;
                    r.Price = checked(c.RunCost * (c.Star == 3 ? 9 : c.Star == 2 ? 3 : 1));
                    Funds(r.Price.Value);
                    break;
                case MoveOrSwapBoardAction board:
                    if (values.UsesIntegrated)
                        IntegratedShape(values.Integrated.CompareBoardMoveOrSwap(board.SourceSlot, board.TargetSlot), true);
                    else Shape(values.Composition.CompareBoardMoveOrSwap(board.SourceSlot, board.TargetSlot), true);
                    break;
                case MoveBoardToHandAction transfer:
                    if (values.UsesIntegrated)
                        IntegratedShape(values.Integrated.CompareBoardToHand(transfer.BoardSlot, transfer.HandSlot));
                    else Shape(values.Composition.CompareBoardToHand(transfer.BoardSlot, transfer.HandSlot));
                    break;
                case MoveHandToBoardAction deploy:
                    if (WouldDuplicateAfterHandMove(o, deploy.BoardSlot, deploy.HandSlot))
                        r.Status = V14Status.DuplicateDefinition;
                    else if (IsEmptyBoardSlot(o, deploy.BoardSlot) && BoardCount(o) >= o.Player.Level)
                        r.Status = V14Status.OverLevel;
                    else if (values.UsesIntegrated)
                        IntegratedShape(values.Integrated.CompareHandToBoard(deploy.HandSlot, deploy.BoardSlot));
                    else Shape(values.Composition.CompareHandToBoard(deploy.HandSlot, deploy.BoardSlot));
                    break;
                case MoveOrSwapHandAction:
                    r.Status = V14Status.HandReorder;
                    break;
                case BuyExperienceAction:
                    var rules = o.EconomyRules;
                    if (!rules.CanPurchaseExperience)
                    {
                        r.Status = V14Status.ExperienceUnavailable;
                        break;
                    }
                    var growth = values.Growth.CompareExperienceGain(rules.ExperiencePerPurchase);
                    r.Growth = growth;
                    Funds(-rules.ExperiencePurchaseGoldCost);
                    if (o.Player.Gold < _v14.ExperienceMinimumGold)
                        r.CapacityUse = GetNewV14CapacityUse(o, generated, values, growth, r.Economy.Value.After.Gold);
                    r.GoldGatePassed = !r.CapacityUse.HasValue || r.CapacityUse != V14CapacityUse.None;
                    if (r.GoldGatePassed == false) r.Status = V14Status.LowGoldExperience;
                    else score += growth.TotalDelta;
                    break;
                case RerollShopAction reroll:
                    if (reroll.ShopKind != ShopKind.Character) r.Status = V14Status.ItemPolicy;
                    else
                    {
                        r.GoldGatePassed = o.Player.Gold >= _v14.RerollMinimumGold;
                        if (r.GoldGatePassed == false) r.Status = V14Status.LowGoldReroll;
                        else
                        {
                            r.Shop = values.Shop();
                            r.CorrectedShop = values.CorrectedShop;
                            r.CorrectedScore = r.CorrectedShop?.Score;
                            r.Economy = r.Shop.RerollEconomy;
                            if (r.Shop.Score.HasValue) score = r.Shop.Score.Value;
                            else r.Status = V14Status.UnavailableReroll;
                        }
                    }
                    break;
                case UseHandItemAction use:
                    score = (decimal)HandItemScore(o, use, out _);
                    break;
                case SellItemAction item:
                    // A slot identifies the Item in this immutable observation, including identical Definitions in other slots.
                    if (o.Hand.EmptySlotCount != 0 || generated.OfType<UseHandItemAction>()
                        .Any(use => use.HandSlot == item.HandSlot && HandItemScore(o, use, out _) > 0))
                        r.Status = V14Status.ItemPolicy;
                    else
                    {
                        score = (decimal)_settings.ItemSaleSpaceValue;
                        Funds(0);
                    }
                    break;
                default:
                    r.Status = V14Status.ItemPolicy;
                    break;
            }
            if (r.Status == V14Status.Scored) r.Score = score;
            return r;
        }

        private enum V14CapacityUse { None, HandPlacement, PublicShopPurchase }

        private static V14CapacityUse GetNewV14CapacityUse(GameObservation o, IReadOnlyList<GameAction> generated,
            V14DecisionValues values, GrowthValueComparison growth, int remainingGold)
        {
            int beforeCapacity = growth.Before.EffectiveCapacity;
            int afterCapacity = growth.After.EffectiveCapacity;
            if (afterCapacity <= beforeCapacity) return V14CapacityUse.None;
            int occupied = BoardCount(o);
            foreach (var a in generated)
            {
                if (a is MoveHandToBoardAction move && occupied >= beforeCapacity && occupied < afterCapacity &&
                    IsEmptyBoardSlot(o, move.BoardSlot) && !WouldDuplicateAfterHandMove(o, move.BoardSlot, move.HandSlot))
                    return V14CapacityUse.HandPlacement;
                if (!(a is BuyCharacterAction buy) || Offer(o, buy.OfferId).DisplayPrice > remainingGold) continue;
                var integrated = values.UsesIntegrated ? values.IntegratedPurchase(buy) : null;
                var purchase = values.UsesIntegrated ? null : values.Purchase(buy);
                var preview = integrated?.Preview ?? purchase.Preview;
                if ((integrated?.Succeeded ?? purchase.Succeeded) && preview.After.BoardCount > occupied &&
                    preview.After.BoardCount > beforeCapacity && preview.After.BoardCount <= afterCapacity)
                    return V14CapacityUse.PublicShopPurchase;
            }
            return V14CapacityUse.None;
        }

        private static string FormatV14(V14Evaluation r)
        {
            var text = new StringBuilder(FormattableString.Invariant(
                $"status={r.Status} score={r.Score?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable"} minimum={(r.Status == V14Status.Scored || r.Status == V14Status.Finish ? r.Minimum.ToString(CultureInfo.InvariantCulture) : "Unavailable")} passes={(r.Score.HasValue ? r.Passes.ToString() : "Unavailable")} boardAddition={r.IncreasesBoard} goldGate={r.GoldGatePassed}"));
            if (r.Price.HasValue) text.Append(FormattableString.Invariant($" runCost={r.RunCost} price={r.Price} mergeStages={r.MergeStages?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable"}"));
            if (r.CapacityUse.HasValue) text.Append(" lowGoldXp=").Append(r.CapacityUse);
            if (r.PurchaseFailure.HasValue) text.Append(" purchase=").Append(r.PurchaseFailure);
            if (r.Composition.HasValue)
            {
                var c = r.Composition.Value;
                text.Append(FormattableString.Invariant($" composition={c.Total} combat={c.BoardCombat} synergy={c.BoardSynergy} hand={c.HandReserve} merge={c.MergeProgress} capacity={c.Capacity}"));
            }
            if (r.Economy.HasValue)
            {
                var e = r.Economy.Value;
                text.Append(FormattableString.Invariant($" gold={e.Before.Gold}->{e.After.Gold} economy={e.TotalDelta} holding={e.HoldingDelta} interest={e.WeightedInterestDelta} eligibility={e.Eligibility}"));
            }
            if (r.Growth.HasValue)
            {
                var g = r.Growth.Value;
                text.Append(FormattableString.Invariant($" growth={g.TotalDelta} level={g.Before.Level}->{g.After.Level} xp={g.Before.XP}->{g.After.XP} immediateLevelUp={g.After.Level > g.Before.Level} slots={g.After.EffectiveCapacity-g.Before.EffectiveCapacity} progress={g.DeltaProgress} opportunity={g.DeltaOpportunity}"));
            }
            if (r.Shop != null)
                text.Append(FormattableString.Invariant($" shopStatus={r.Shop.Status} current={r.Shop.CurrentOpportunity} future={r.Shop.ExpectedFutureOpportunity} discounted={r.Shop.DiscountedFutureOpportunity} unknownMass={r.Shop.FutureMass?.Unavailable} discount={r.Shop.FutureDiscount} hypotheticalPreviews={r.Shop.HypotheticalPreviewCount} reason={r.Shop.UnavailableReason}"));
            return text.ToString();
        }

        private static string V14TraceOutcome(V14Evaluation row, GameAction selected, string reason)
        {
            if (ReferenceEquals(row.Action, selected)) return "Selected";
            if (row.Status == V14Status.Deferred)
                return reason.StartsWith("LevelRepair", StringComparison.Ordinal) ? "DeferredLevelRepair" :
                    reason.StartsWith("EmptyBoardPriority", StringComparison.Ordinal) ? "DeferredEmptyBoard" : "DeferredLimit";
            if (row.Status != V14Status.Scored) return row.Status.ToString();
            if (reason.StartsWith("EmptyBoardPriority", StringComparison.Ordinal)) return "OtherEmptyBoardAddition";
            return row.Score <= 0 ? "NonPositive" : row.Passes ? "OtherActionWon" : "BelowMinimum";
        }

        private void PublishV14(GameObservation o, StrategyContext context, int decisions,
            List<V14Evaluation> trace, GameAction action, string reason, bool candidatesGenerated)
        {
            try
            {
                var b = new StringBuilder()
                    .AppendLine(FormattableString.Invariant($"Strategy={StrategyId} Version={Version} TrialIndex={context.TrialIndex} DecisionIndex={context.DecisionStep}"))
                    .AppendLine(FormattableString.Invariant($"Stage={o.Stage.StageNumber} Round={o.Stage.RoundNumber} EncounterIndex={o.Stage.EncounterIndex} Phase={o.Stage.Phase} Choice={o.Stage.Choice} EnemyCategory={o.Stage.EnemyCategory} PreparationDecisions={decisions}"))
                    .AppendLine("VisibleObservationFingerprint=" + (_v15 == null ? GetV14ObservationFingerprint(o) :
                        GetV15ObservationFingerprint(o, _v15)))
                    .AppendLine(FormattableString.Invariant($"PublicState gold={o.Player.Gold} level={o.Player.Level} xp={o.Player.XP} board={BoardCount(o)}/{o.Board.Slots.Count} handOccupied={o.Hand.SlotCount-o.Hand.EmptySlotCount}/{o.Hand.SlotCount} handCharacters={o.Hand.Slots.Count(x => x.Character != null)} handItems={o.Hand.Slots.Count(x => x.Item != null)}"))
                    .AppendLine("RerollGeneration=" + (!candidatesGenerated ? "NotRun" : trace.Any(x => x.Action is RerollShopAction) ? "Generated" : "NoCandidate"))
                    .AppendLine(FormattableString.Invariant($"ShopEvaluation calls={trace.Count(x => x.Shop != null)} expectedComputed={trace.Count(x => x.Shop?.ExpectedFutureOpportunity.HasValue == true)} currentComputed={trace.Count(x => x.Shop?.CurrentOpportunity.HasValue == true)} unavailable={trace.Count(x => x.Shop != null && !x.Shop.Score.HasValue)}"));
                foreach (var result in trace)
                    b.Append("Candidate generated=").Append(DescribeCandidate(result.Action)).Append(" signature=")
                        .Append(Signature(result.Action)).Append(' ').Append(FormatV14(result)).Append(" outcome=").AppendLine(V14TraceOutcome(result, action, reason));
                b.Append("FinalAction=").Append(action == null ? "NoAction" : DescribeCandidate(action))
                    .Append(" FinalReason=").AppendLine(reason);
                SendDecisionTrace(b.ToString());
            }
            catch (Exception error) { ReportDecisionTraceError(false, error); }
        }
    }
}
