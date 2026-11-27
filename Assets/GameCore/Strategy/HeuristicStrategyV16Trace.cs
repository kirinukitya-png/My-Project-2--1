using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Strategy
{
    public enum V16BattleUse { NonPreparation, Forced, Disabled, NoEnemy, NotRequired, Used, ZeroDelta, Fallback }

    public readonly struct HeuristicV16DecisionTelemetry
    {
        public int Stage { get; }
        public int Encounter { get; }
        public int Enemies { get; }
        public int BoardCount { get; }
        public int? AttackLength { get; }
        public bool ShopEvaluated { get; }
        public V16BattleUse Use { get; }
        public bool Comparable { get; }
        public bool ActionChanged { get; }
        public bool KindChanged { get; }
        public bool RerollChanged { get; }
        public BattleValueDecisionFailure Failure { get; }
        public int Requests { get; }
        public int Evaluations { get; }
        public int Hits { get; }
        public int CacheLookups { get; }
        public int AttackPeak { get; }
        public int DefensePeak { get; }
        public int AttackLimitHits { get; }
        public int DefenseLimitHits { get; }
        public int PreviewCount { get; }
        public int CandidateCount { get; }

        internal HeuristicV16DecisionTelemetry(GameObservation observation, V16BattleUse use,
            int board, int? attackLength, int previewCount, int candidateCount, bool shop,
            bool comparable, GameAction original, GameAction chosen, bool changed,
            BattleValueDecisionFailure failure, BattleValueEvaluator evaluator)
        {
            Stage = observation.Stage.StageNumber;
            Encounter = observation.Stage.EncounterIndex;
            Enemies = observation.Enemies.Count(e => e.HP > 0);
            BoardCount = board;
            Use = use; AttackLength = attackLength; ShopEvaluated = shop; Comparable = comparable;
            ActionChanged = changed;
            KindChanged = changed && original?.Kind != chosen?.Kind;
            RerollChanged = changed && ((original is RerollShopAction) != (chosen is RerollShopAction));
            Failure = failure;
            Requests = evaluator?.Requests ?? 0;
            Evaluations = evaluator?.Evaluations ?? 0;
            Hits = evaluator?.CacheHits ?? 0;
            CacheLookups = evaluator?.CacheLookups ?? 0;
            AttackPeak = evaluator?.AttackPeakStates ?? 0;
            DefensePeak = evaluator?.DefensePeakStates ?? 0;
            AttackLimitHits = evaluator?.AttackLimitHits ?? 0;
            DefenseLimitHits = evaluator?.DefenseLimitHits ?? 0;
            PreviewCount = previewCount;
            CandidateCount = candidateCount;
        }
    }

    public sealed partial class HeuristicStrategy
    {
        private bool _v16DetailedTrace;
        private Action<HeuristicV16DecisionTelemetry> _v16Telemetry;

        public void ConfigureV16Diagnostics(bool detailed = false, Action<HeuristicV16DecisionTelemetry> telemetry = null)
        {
            if (_v16 == null) throw new InvalidOperationException("Diagnostics require Heuristic v16.");
            _v16DetailedTrace = detailed;
            _v16Telemetry = telemetry;
        }

        private void SendV16Telemetry(HeuristicV16DecisionTelemetry sample)
        {
            try { _v16Telemetry?.Invoke(sample); }
            catch (Exception error) { ReportDecisionTraceError(false, error); }
        }

        private static HeuristicV16DecisionTelemetry ObserveV16(GameObservation o, V16DecisionBattle battle,
            IReadOnlyList<V14Evaluation> rows, bool compared, GameAction original, GameAction chosen, int generated = 0)
        {
            var shop = rows?.FirstOrDefault(r => r.Shop != null)?.Shop;
            V16BattleUse use;
            if (o.Stage.Phase != ObservationPhase.Preparation) use = V16BattleUse.NonPreparation;
            else if (!compared) use = V16BattleUse.Forced;
            else if (battle.Status == "Disabled") use = V16BattleUse.Disabled;
            else if (battle.Status == "NoEnemy") use = V16BattleUse.NoEnemy;
            else if (battle.RequiresFallback) use = V16BattleUse.Fallback;
            else if (battle.Baseline == null) use = V16BattleUse.NotRequired;
            else use = rows.Any(r => (r.CorrectedScore.HasValue && r.CorrectedScore != r.Score) ||
                (r.CorrectedShop != null && (r.CorrectedShop.CurrentOpportunity != r.Shop.CurrentOpportunity ||
                    r.CorrectedShop.ExpectedFutureOpportunity != r.Shop.ExpectedFutureOpportunity)))
                ? V16BattleUse.Used : V16BattleUse.ZeroDelta;
            bool comparable = use == V16BattleUse.Used || use == V16BattleUse.ZeroDelta;
            bool changed = comparable && !ReferenceEquals(original, chosen);
            return new HeuristicV16DecisionTelemetry(o, use, BoardCount(o),
                battle?.Baseline?.Current?.Attack?.PlannedActions, shop?.HypotheticalPreviewCount ?? 0,
                generated, shop?.ExpectedFutureOpportunity.HasValue == true, comparable, original, chosen,
                changed, battle?.Failures.FirstOrDefault(), battle?.Evaluator);
        }

        private static string V16Action(GameAction action, bool available) =>
            !available ? "Unavailable" : action == null ? "NoAction" : DescribeCandidate(action);

        private void AppendV16Trace(StringBuilder b, GameObservation o, StrategyContext context,
            IReadOnlyList<V14Evaluation> rows, GameAction chosen, V14DecisionValues values,
            HeuristicV16DecisionTelemetry telemetry, GameAction original, bool compared,
            GameAction originalTop, GameAction correctedTop)
        {
            var battle = values?.Battle;
            var eval = battle?.Evaluator;
            b.AppendLine(FormattableString.Invariant($"Decision stage={o.Stage.StageNumber} encounter={o.Stage.EncounterIndex} round={o.Stage.RoundNumber} phase={o.Stage.Phase} actionId={context.NextActionId} configuration={GetV16ConfigurationIdentity(_v16)} mode={_v16.Battle.EvaluationMode} category={telemetry.Use}"));
            AppendV15PublicState(b, o, includeHeader: false);
            foreach (var slot in o.Board.Slots.Where(slot => slot.Character != null))
            {
                var c = slot.Character;
                b.AppendLine(FormattableString.Invariant($"BoardStats slot={slot.SlotIndex} star={c.Star} source=Actual tempAtk={c.TemporaryAttackBonus} tempDef={c.TemporaryDefenseBonus} effectiveAtk={(long)c.Attack + c.TemporaryAttackBonus} effectiveDef={(long)c.Defense + c.TemporaryDefenseBonus}"));
            }
            b.AppendLine(FormattableString.Invariant($"ItemBlock permanent={o.Player.PermanentPlayerBlockBonus} temporary={o.Player.TemporaryPlayerBlockBonus}"));
            if (eval != null)
                b.AppendLine($"Precision firstRoundOnly=True potentialDamage=True appliedEnemyModifiers={eval.Context.HasAppliedEnemyModifiers} unhandledAllyRound={eval.Context.HasEffectsBeforeOrDuringAllyTurn} unhandledEnemyRound={eval.Context.HasFirstEnemyTurnDamageModifiers} unhandledLaterRounds={eval.Context.HasLaterRoundModifiers} unhandledGold={eval.Context.HasGoldAftermath}");
            b.AppendLine($"BaselineComposition {(values?.ExistingIntegrated == null ? "Unavailable" : TraceValue(values.ExistingIntegrated.Baseline.Value))}");
            foreach (var e in o.Enemies)
            {
                var t = e.GetInitialTarget();
                b.AppendLine(FormattableString.Invariant($"Enemy slot={e.SlotIndex} definition={e.DefinitionId} hp={e.HP}/{e.MaximumHP} eligible={e.HP > 0} atk={e.Attack} def={e.Defense} gap={e.Gap} cooldown={e.Cooldown} target={t.TargetId} targetState={t.Status} bonuses={e.SlotBonusAttack}/{e.SlotBonusDefense}/{e.SlotBonusHP}/{e.SlotBonusGap} CIP={string.Join(",",e.CipEffects)} AE={string.Join(",",e.AeEffects)} PIG={string.Join(",",e.PigEffects)}"));
                var rule = t.Definition;
                if (rule != null)
                    b.AppendLine($"  Target filter={rule.Filter} characters={rule.Selection}/{rule.TargetCount} cells={rule.CellSelection}/{rule.CellCount} weights={string.Join(",", rule.Weights.Select(w => $"{w.Position}:{w.Weight}"))} cellWeights={string.Join(",", rule.CellWeights.Select(w => $"{w.Position}:{w.Weight}"))}");
            }
            var s = _v16.Battle;
            b.AppendLine(FormattableString.Invariant($"ReferenceSettings immortal=True atk={s.ReferenceEnemyATK} def={s.ReferenceEnemyDEF} gap={s.ReferenceEnemyGap} target={s.ReferenceEnemyTarget.TargetId} attackLimit={s.AllyAttackMaxStates} blockLimit={s.EnemyBlockMaxStates}"));
            b.AppendLine($"Selection originalTop={V16Action(originalTop, compared)} correctedTop={V16Action(correctedTop, compared && battle?.RequiresFallback != true)} originalFinal={V16Action(original, compared)} correctedFinal={V16Action(chosen, telemetry.Comparable)} changed={telemetry.ActionChanged} kindChanged={telemetry.KindChanged} placementChanged={telemetry.ActionChanged && !telemetry.KindChanged} fromStart={telemetry.ActionChanged && original is StartBattleAction} toStart={telemetry.ActionChanged && chosen is StartBattleAction}");
            var counts = values?.ExistingIntegrated?.CacheStatistics ?? default;
            var sizes = values?.ExistingIntegrated?.DiagnosticCounts ?? default;
            b.AppendLine(FormattableString.Invariant($"Work plans={eval?.PlanBuilds ?? 0} attack={eval?.AttackEvaluations ?? 0} defense={eval?.DefenseEvaluations ?? 0} target={eval?.TargetEvaluations ?? 0} reference={eval?.ReferenceEvaluations ?? 0} attackBranches={eval?.AttackBranches ?? 0} attackMerges={eval?.AttackMerges ?? 0} defenseBranches={eval?.DefenseBranches ?? 0} defenseMerges={eval?.DefenseMerges ?? 0} attackLimits={eval?.AttackLimitHits ?? 0} defenseLimits={eval?.DefenseLimitHits ?? 0} averages={eval?.PublicMeanEvaluations ?? 0} ignoredEffects={eval?.EffectEvaluations ?? 0} cachedBoards={eval?.CachedBoards ?? 0} synergyNew={counts.SynergyEvaluations} synergyReuse={counts.SynergyCacheHits} synergyEntries={sizes.BoardEntries} options={sizes.OptionSearches} optionEntries={sizes.OptionEntries} actualPreviews={values?.PurchaseCounts.Entries ?? 0} hypotheticalPreviews={telemetry.PreviewCount} rows={rows.Count}"));
            AppendV16Evaluation(b, "Current", battle?.Baseline, _v16DetailedTrace);
            if (_v16DetailedTrace)
                AppendV16Evaluation(b, "SelectedAfter", rows.FirstOrDefault(r => ReferenceEquals(r.Action, chosen))?.BattleComparison?.After, true);
        }

        private static void AppendV16Evaluation(StringBuilder b, string label, BattleValueEvaluation eval, bool details)
        {
            b.AppendLine($"{label} status={eval?.Status.ToString() ?? "NotEvaluated"} attrBlock={TraceNumber(eval?.AttributeBlock)} initialBlock={TraceNumber(eval?.InitialBlock)} meanStats={eval?.UsesPublicMeanStats} ignoredEffects={eval?.IgnoredEffects}");
            if (eval == null) return;
            if (details && eval.RetainedBoard != null)
                foreach (var m in eval.RetainedBoard.Members)
                    b.AppendLine(FormattableString.Invariant($"{label} Member slot={m.Member.SlotIndex} definition={m.Member.DefinitionId} star={m.Member.Star} atk={m.EffectiveAttack} def={m.EffectiveDefense} stats={m.StatsKind}"));
            AppendV16Round(b, label, eval.Current, details);
            AppendV16Round(b, label + "Reference", eval.Reference, false);
        }

        private static void AppendV16Round(StringBuilder b, string label, BattleValueRound round, bool details)
        {
            var a = round?.Attack;
            var d = round?.Defense;
            b.AppendLine($"{label}Round B={TraceNumber(round?.Value)} attack={a?.Status} defense={d?.Status} out={TraceNumber(a?.ExpectedEffectiveDamage)} normalCount={TraceNumber(a?.ExpectedNormalAttacks)} moveCount={TraceNumber(a?.ExpectedUnitMoves)} normalDamage={TraceNumber(a?.ExpectedNormalDamage)} unitDamage={TraceNumber(a?.ExpectedUnitDamage)} overkill={TraceNumber(a?.ExpectedOverkill)} kills={TraceNumber(a?.ExpectedDefeats)} defReduction={TraceNumber(a?.ExpectedDefenseReduction)} threat={TraceNumber(d?.InitialThreat)} surviving={TraceNumber(d?.SurvivingThreat)} avoided={TraceNumber(d?.AvoidedThreat)} rawIncoming={TraceNumber(d?.ExpectedDamageBeforeBlock)} block={TraceNumber(d?.ExpectedAbsorbed)} incoming={TraceNumber(d?.Incoming)} remainingBlock={TraceNumber(d?.ExpectedRemainingBlock)}");
            if (a != null) b.AppendLine(FormattableString.Invariant($"{label}AttackWork completed={a.CompletedActionLayers} planned={a.PlannedActions} peak={a.PeakStates} generated={a.GeneratedBranches} merged={a.MergedBranches} terminals={a.TerminalStates} mass={TraceNumber(a.LastCompletedProbabilityMass)}"));
            if (!details || a == null) return;
            var plan = round.RetainedPlan;
            if (plan != null)
            {
                foreach (var m in plan.UnitPlan.Members)
                    b.AppendLine($"{label} PlanMember slot={m.Slot} participation={m.Count}/{m.Limit} normal={m.NormalAttack}");
                foreach (var action in plan.Actions)
                    b.AppendLine($"{label} Attack order={action.Order} slot={action.ActorSlot} unit={action.UnitName ?? "Normal"} participants={string.Join(",",action.ParticipantSlots)} atk={TraceNumber(action.Attack)} probability={TraceNumber(a.ActionExecutionProbabilities.Count > action.Order ? a.ActionExecutionProbabilities[action.Order] : (decimal?)null)}");
            }
            foreach (var e in a.EnemySummaries)
                b.AppendLine($"{label} EnemyOutcome slot={e.Slot} hp={TraceNumber(e.ExpectedHP)} survival={TraceNumber(e.SurvivalProbability)} defeated={TraceNumber(e.DefeatProbability)}");
            foreach (var group in a.Outcomes.GroupBy(x => x.SurvivingSlotMask).OrderBy(g => g.Key))
                b.AppendLine($"{label} Survival mask={group.Key} probability={TraceNumber(group.Sum(x => x.Probability))}");
            if (d == null) return;
            b.AppendLine($"{label} DefenseWork masks={d.Counters.EligibleMasks} peak={d.Counters.PeakBlockStates} generated={d.Counters.GeneratedBlockBranches} merged={d.Counters.MergedBlockBranches}");
            int eligible = d.Enemies.Where(e => e.AttackProbability > 0m).Aggregate(0, (mask, e) => mask | (1 << e.Slot));
            foreach (var group in a.Outcomes.GroupBy(x => x.SurvivingSlotMask & eligible).OrderBy(g => g.Key))
                b.AppendLine($"{label} Eligible mask={group.Key} probability={TraceNumber(group.Sum(x => x.Probability))}");
            if (d.Enemies.Any(e => e.TargetSetCount > 0 && e.Targets.Count == 0))
                b.AppendLine($"{label} TargetDetails=NotRetained");
            foreach (var e in d.Enemies)
            {
                b.AppendLine($"{label} Attacker slot={e.Slot} target={e.TargetId} attackProbability={TraceNumber(e.AttackProbability)} incoming={TraceNumber(e.ExpectedIncoming)} block={TraceNumber(e.ExpectedAbsorbed)} targetSets={e.TargetSetCount}");
                foreach (var t in e.Targets)
                    b.AppendLine($"{label} Target mask={t.Target.SlotMask} probability={TraceNumber(t.Target.Probability)} def={TraceNumber(t.TotalDefense)} damage={TraceNumber(t.Damage)}");
                foreach (var hit in e.DamageDistribution)
                    b.AppendLine($"{label} Damage amount={TraceNumber(hit.Damage)} probability={TraceNumber(hit.Probability)}");
            }
            foreach (var block in d.RemainingBlockDistribution)
                b.AppendLine($"{label} Block amount={TraceNumber(block.Block)} probability={TraceNumber(block.Probability)}");
        }

        private void AppendV16CandidateTrace(StringBuilder b, V14Evaluation raw, HeuristicV16CandidateEvaluation row,
            bool fallback, GameAction chosen, string reason)
        {
            b.AppendLine($"  {FormatV14(raw)} selected={ReferenceEquals(chosen, row.Action)} outcome={(ReferenceEquals(chosen, row.Action) ? "Selected" : !row.UsedScore.HasValue ? row.Status : row.UsedScore <= 0m ? "NonPositive" : row.UsedScore <= row.Minimum ? "BelowMinimum" : "OtherActionWon")} fallback={fallback}");
            if (row.Composition.HasValue)
            {
                var c = row.Composition.Value;
                b.AppendLine("  Before " + TraceValue(c.Before.Value)).AppendLine("  After " + TraceValue(c.After.Value))
                    .AppendLine("  Delta " + TraceValue(c.Delta));
            }
            var cmp = raw.BattleComparison;
            b.AppendLine($"  Battle before={TraceNumber(cmp?.Before?.Current?.Value)}/{TraceNumber(cmp?.Before?.Reference?.Value)}/{TraceNumber(cmp?.Before?.RawCorrection)}/{TraceNumber(cmp?.Before?.Correction)} after={TraceNumber(cmp?.After?.Current?.Value)}/{TraceNumber(cmp?.After?.Reference?.Value)}/{TraceNumber(cmp?.After?.RawCorrection)}/{TraceNumber(cmp?.After?.Correction)} deltaR={TraceNumber(cmp?.DeltaR)} applied={TraceNumber(row.BattleContribution)}");
            if (raw.Action is RerollShopAction)
            {
                AppendV16ShopTrace(b, "Original", row.OriginalShop);
                AppendV16ShopTrace(b, "Corrected", row.CorrectedShop);
                AppendV16ShopTrace(b, "Used", fallback ? row.OriginalShop : row.CorrectedShop ?? row.OriginalShop);
            }
        }

        private void AppendV16ShopTrace(StringBuilder b, string label, ShopOpportunityResult shop)
        {
            var mass = shop?.FutureMass;
            b.AppendLine($"  Shop{label} current={TraceNumber(shop?.CurrentOpportunity)} future={TraceNumber(shop?.ExpectedFutureOpportunity)} score={TraceNumber(shop?.Score)} discount={TraceNumber(shop?.FutureDiscount)} economy={TraceNumber(shop?.RerollEconomy?.TotalDelta)} best={shop?.BestCurrent?.OfferId}/{shop?.BestCurrent?.Placement} goldAfter={shop?.RemainingGold} positive={TraceNumber(mass?.Positive)} nonPositive={TraceNumber(mass?.NonPositive)} unaffordable={TraceNumber(mass?.InsufficientGold)} unreachable={TraceNumber(mass?.NoReachablePlacement)} unknown={TraceNumber(mass?.Unavailable)}");
            if (shop?.CdfValues.HasValue == true)
                b.AppendLine($"  Shop{label} Work offers={shop.CurrentOffers} currentPlacements={shop.CurrentPlacements} futurePlacements={shop.FuturePlacements} formationEntries={shop.FormationEntries} cdfValues={shop.CdfValues}");
            if (_v16DetailedTrace && shop?.Distribution != null)
            {
                foreach (var placement in shop.CurrentPlacementDetails ?? Array.Empty<(string Id, decimal Original, decimal? Contribution)>())
                    b.AppendLine($"  Shop{label} Placement id={placement.Id} original={TraceNumber(placement.Original)} contribution={TraceNumber(placement.Contribution)} revised={TraceNumber(placement.Original + placement.Contribution)}");
                foreach (var outcome in shop.Distribution.Outcomes)
                    b.AppendLine($"  Shop{label} Outcome definition={outcome.DefinitionId} cost={outcome.Cost} probability={TraceNumber(outcome.SlotProbability)}");
                foreach (var offer in shop.Details)
                    b.AppendLine($"  Shop{label} BestPlacement definition={offer.DefinitionId} offer={offer.OfferId} placement={offer.Placement} status={offer.Status} benefit={TraceNumber(offer.Benefit)}");
            }
        }
    }
}
