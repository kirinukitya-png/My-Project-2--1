using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Strategy
{
    public sealed partial class HeuristicStrategy
    {
        private bool _v15TraceDetails;

        public void ConfigureV15TraceDetails(bool enabled)
        {
            if (_v15 == null) throw new InvalidOperationException("Detailed v15 diagnostics require Heuristic v15.");
            _v15TraceDetails = enabled;
        }

        private static string TraceNumber(decimal? number) =>
            number?.ToString("G29", CultureInfo.InvariantCulture) ?? "Unavailable";

        private void AppendV15PublicState(StringBuilder text, GameObservation o, bool includeHeader = true)
        {
            var p = o.Player;
            var settings = _v15.Composition;
            var core = settings.FourComponentSettings;
            var rules = _v15.Preparation;
            var economy = rules.Economy;
            var growth = rules.Growth;
            if (includeHeader) text.AppendLine($"TraceFormat=v15-1 Detail={(_v15TraceDetails ? "SelectedAndCurrent" : "Numeric")} EvaluationMode=SixComponents Configuration={GetV15ConfigurationIdentity(_v15)}");
            text.AppendLine(FormattableString.Invariant($"PublicState gold={p.Gold} level={p.Level} xp={p.XP} hp={(p.HasHealth ? p.HP.ToString(CultureInfo.InvariantCulture) : "Unavailable")} enemy={o.Stage.EnemyCategory} boardSlots={o.Board.Slots.Count} effectiveCapacity={Math.Min(p.Level, o.Board.Slots.Count)} boardOccupied={BoardCount(o)} handOccupied={o.Hand.SlotCount - o.Hand.EmptySlotCount}"))
                .AppendLine(FormattableString.Invariant($"Settings compositionScale={rules.Shop.CompositionWeight} attribute={settings.Synergy.AttributeCoefficient} progress={settings.Synergy.ProgressCoefficient} activation={settings.Synergy.ActivationCoefficient} option={settings.HandOption.Coefficient} attack={core.AttackValue} defense={core.DefenseValue} star={core.CharacterStarValue} temporaryStats={core.IncludeTemporaryCharacterBonuses} reserve={core.CompositionHandReserveWeight} duplicate={core.CompositionHandDuplicateWeight} merge={core.MergeProgressOneStarValue}/{core.MergeProgressOneStarTwoStarValue}/{core.MergeProgressTwoStarTwoCopiesValue} handCapacity=0/{settings.OneEmptyHandSlotValue}/{settings.FullHandValue}"))
                .AppendLine(FormattableString.Invariant($"Rules holding={economy.HoldingModel}/{economy.ReserveRate}/{economy.SurplusRate}/{economy.ReserveGold} interestWeight={economy.InterestWeight} interest={o.EconomyRules?.GoldPerInterestThreshold}/{o.EconomyRules?.InterestGoldPerThreshold}/{o.EconomyRules?.MaximumInterestGold} growth={growth.CapacityPerSlot}/{growth.ProgressFraction}/{growth.OpportunityPerSlot} rerollDiscount={rules.Shop.FutureDiscount} xpGold={rules.ExperienceMinimumGold} rerollGold={rules.RerollMinimumGold} minimum={rules.RequiredBenefit(false, false)}/{rules.RequiredBenefit(true, false)}/{rules.RequiredBenefit(false, true)}/{rules.RequiredBenefit(true, true)} limit={rules.PreparationLimit}"));
            foreach (var slot in o.Board.Slots)
                text.AppendLine($"Board slot={slot.SlotIndex} {TraceCharacter(slot.Character)}");
            foreach (var slot in o.Hand.Slots)
                text.AppendLine($"Hand slot={slot.SlotIndex} kind={slot.Kind} " + (slot.Item == null
                    ? TraceCharacter(slot.Character)
                    : $"item={slot.Item.DefinitionId} effects={string.Join(",", slot.Item.Effects.Select(e => FormattableString.Invariant($"{e.Kind}:{e.Value}:{e.Duration}")))}"));
            if (o.Shop != null)
                foreach (var offer in o.Shop.Offers)
                    text.AppendLine(FormattableString.Invariant($"Offer id={offer.OfferId} kind={offer.Kind} definition={offer.DefinitionId} runCost={offer.RunCost} price={offer.DisplayPrice} status={offer.Status}"));
            else text.AppendLine("Shop=Unavailable");
            if (_v15TraceDetails)
                foreach (var unit in o.UnitRequirements)
                    text.AppendLine($"UnitRequirement name={unit.UnitName} definitions={string.Join(",", unit.RequiredDefinitionIds)}");
        }

        private static string TraceCharacter(CharacterObservation c) => c == null ? "character=None" :
            FormattableString.Invariant($"character={c.InstanceId} definition={c.DefinitionId} star={c.Star} runCost={c.RunCost} atk={c.Attack} def={c.Defense} attributes={string.Join(",", c.Attributes)} units={string.Join(",", c.Units)}");

        private static string TraceValue(IntegratedCompositionValue v) => FormattableString.Invariant(
            $"total={v.TotalCompositionValue} combat={v.BoardCombat} synergy={v.BoardSynergy} attribute={v.Synergy.AttributeValue} progress={v.Synergy.UnitProgressValue} activation={v.Synergy.UnitActivationValue} hand={v.HandReserve} merge={v.MergeProgress} capacity={v.Capacity} option={v.HandUnitOption}");

        private static string TraceSynergy(SynergyValue? v) => v.HasValue
            ? FormattableString.Invariant($"{v.Value.TotalSynergyValue}/{v.Value.AttributeValue}/{v.Value.UnitProgressValue}/{v.Value.UnitActivationValue}")
            : "Unavailable";

        private void PublishV15(GameObservation o, StrategyContext context, int decisions, List<V14Evaluation> trace,
            GameAction action, string reason, bool candidatesGenerated, V14DecisionValues values)
        {
            try
            {
                var text = new StringBuilder()
                    .AppendLine(FormattableString.Invariant($"Strategy={StrategyId} Version={Version} TrialIndex={context.TrialIndex} DecisionIndex={context.DecisionStep}"))
                    .AppendLine(FormattableString.Invariant($"Stage={o.Stage.StageNumber} Round={o.Stage.RoundNumber} EncounterIndex={o.Stage.EncounterIndex} Phase={o.Stage.Phase} Choice={o.Stage.Choice} EnemyCategory={o.Stage.EnemyCategory} PreparationDecisions={decisions}"))
                    .AppendLine("VisibleObservationFingerprint=" + GetV15ObservationFingerprint(o, _v15));
                AppendV15PublicState(text, o);
                var integrated = values?.ExistingIntegrated;
                text.AppendLine("CurrentComposition " + (integrated == null ? "Unavailable" : TraceValue(integrated.Baseline.Value)));
                var reroll = trace.FirstOrDefault(r => r.Action is RerollShopAction);
                text.AppendLine($"RerollGeneration={(reroll != null ? "Generated" : candidatesGenerated ? "NoCandidate" : "NotRun")} outcome={(reroll != null ? V14TraceOutcome(reroll, action, reason) : reason)}");
                var counts = integrated?.CacheStatistics ?? default;
                var sizes = integrated?.DiagnosticCounts ?? default;
                var plans = integrated?.MergePlanCounts ?? default;
                var purchases = values?.PurchaseCounts ?? default;
                var shop = reroll?.Shop;
                text.AppendLine(FormattableString.Invariant($"Work contexts={(integrated == null ? 0 : 1)} baselines={(integrated == null ? 0 : 1)} synergyRequests={counts.SynergyEvaluations + counts.SynergyCacheHits} synergyNew={counts.SynergyEvaluations} synergyReuse={counts.SynergyCacheHits} boardEntries={sizes.BoardEntries} optionRequests={sizes.OptionRequests} optionSearches={sizes.OptionSearches} optionReuse={counts.OptionCacheHits} optionEntries={sizes.OptionEntries} actualPreviews={purchases.Entries} actualPreviewReuse={purchases.Reuses} previewEntries={purchases.Entries} mergePlanNew={plans.Calculated} mergePlanReuse={plans.Reused} mergePlanEntries={plans.Entries} currentShop={(shop?.CurrentOpportunity.HasValue == true ? 1 : 0)} futureShop={(shop?.ExpectedFutureOpportunity.HasValue == true ? 1 : 0)} hypotheticalPreviews={shop?.HypotheticalPreviewCount ?? 0}"));
                text.AppendLine(FormattableString.Invariant($"OptionWork legal={sizes.LegalOptions} skipped={sizes.SafeSkips} virtualSynergyNew={sizes.VirtualSynergyNew} virtualSynergyReuse={sizes.LegalOptions-sizes.SafeSkips-sizes.VirtualSynergyNew}"));
                foreach (var candidate in trace)
                {
                    text.AppendLine($"Candidate generated={DescribeCandidate(candidate.Action)} signature={Signature(candidate.Action)} {FormatV14(candidate)} outcome={V14TraceOutcome(candidate, action, reason)}");
                    if (candidate.IntegratedComposition.HasValue)
                    {
                        var c = candidate.IntegratedComposition.Value;
                        decimal applied = candidate.Action is MoveOrSwapBoardAction
                            ? c.Delta.BoardSynergy + c.Delta.HandUnitOption : c.TotalDelta;
                        text.AppendLine("  Before " + TraceValue(c.Before.Value))
                            .AppendLine("  After " + TraceValue(c.After.Value))
                            .AppendLine("  Delta " + TraceValue(c.Delta))
                            .AppendLine(FormattableString.Invariant($"  Applied compositionContribution={applied * _v15.Preparation.Shop.CompositionWeight} optionStatus={c.Before.HandOption.Status}->{c.After.HandOption.Status} board={c.Before.BoardCount}->{c.After.BoardCount} hand={o.Hand.SlotCount-c.Before.EmptyHandSlots}->{o.Hand.SlotCount-c.After.EmptyHandSlots}"));
                    }
                    else text.AppendLine("  Composition=NotUsedOrUnavailable");
                    AppendV15CandidateTargets(text, o, candidate.Action);
                    AppendV15Purchase(text, candidate);
                }
                if (shop != null) AppendV15Shop(text, o, shop);
                else text.AppendLine("Shop current=Unavailable future=Unavailable");
                if (_v15TraceDetails && integrated != null)
                {
                    AppendV15Structure(text, integrated, integrated.Baseline, "Current", true, o);
                    var selected = trace.FirstOrDefault(r => ReferenceEquals(r.Action, action));
                    if (selected?.IntegratedComposition.HasValue == true)
                        AppendV15Structure(text, integrated, selected.IntegratedComposition.Value.After, "SelectedAfter", false, o);
                    else text.AppendLine("Detail SelectedAfter=NotCaptured");
                }
                else text.AppendLine("Detail Structure=NotCaptured");
                text.Append("FinalAction=").Append(action == null ? "NoAction" : DescribeCandidate(action))
                    .Append(" FinalReason=").AppendLine(reason);
                SendDecisionTrace(text.ToString());
            }
            catch (Exception error) { ReportDecisionTraceError(false, error); }
        }

        private static void AppendV15CandidateTargets(StringBuilder text, GameObservation o, GameAction action)
        {
            switch (action)
            {
                case SellCharacterAction sell:
                    text.AppendLine("  SellTarget " + TraceCharacter(o.Characters.FirstOrDefault(c => c.InstanceId == sell.InstanceId)));
                    break;
                case MoveOrSwapBoardAction board:
                    text.AppendLine($"  From slot={board.SourceSlot} {TraceCharacter(o.Board.Slots[board.SourceSlot].Character)}")
                        .AppendLine($"  To slot={board.TargetSlot} {TraceCharacter(o.Board.Slots[board.TargetSlot].Character)}");
                    break;
                case MoveHandToBoardAction deploy:
                    text.AppendLine($"  From hand={deploy.HandSlot} {TraceCharacter(o.Hand.Slots[deploy.HandSlot].Character)}")
                        .AppendLine($"  To board={deploy.BoardSlot} {TraceCharacter(o.Board.Slots[deploy.BoardSlot].Character)}");
                    break;
                case MoveBoardToHandAction retreat:
                    text.AppendLine($"  From board={retreat.BoardSlot} {TraceCharacter(o.Board.Slots[retreat.BoardSlot].Character)}")
                        .AppendLine($"  To hand={retreat.HandSlot} {TraceCharacter(o.Hand.Slots[retreat.HandSlot].Character)}");
                    break;
            }
        }

        private void AppendV15Purchase(StringBuilder text, V14Evaluation row)
        {
            var result = row.IntegratedPurchase;
            if (result == null) return;
            var p = result.Preview;
            text.AppendLine(FormattableString.Invariant($"  Purchase definition={p.DefinitionId} requested={p.RequestedLocation.Kind}:{p.RequestedLocation.SlotIndex} final={p.FinalLocation?.Kind}:{p.FinalLocation?.SlotIndex} finalStar={p.FinalCharacter?.Star} survivor={p.FinalCharacter?.InstanceId} stages={(p.Succeeded ? p.MergeStageCount.ToString(CultureInfo.InvariantCulture) : "Unavailable")} preview={p.Failure} evaluation={result.Failure}"));
            foreach (var merge in p.MergeStages)
                text.AppendLine($"  Merge star={merge.Survivor.Star} consumed={string.Join(",", merge.ConsumedInstanceIds)} consumesPurchase={merge.ConsumesPurchase}");
            var core = _v15.Composition.FourComponentSettings;
            foreach (var roll in result.StatRolls)
            {
                string power = roll.Stats.IsAvailable ? CharacterPowerEvaluator.Evaluate(roll.Stats.ExpectedAttack.Value,
                    roll.Stats.ExpectedDefense.Value, roll.Roll.TemporaryAttackBonus, roll.Roll.TemporaryDefenseBonus,
                    roll.Roll.Star, core.AttackValue, core.DefenseValue, core.CharacterStarValue,
                    core.IncludeTemporaryCharacterBonuses).ToString("R", CultureInfo.InvariantCulture) : "Unavailable";
                text.AppendLine(FormattableString.Invariant($"  PublicRoll cost={roll.Roll.RunCost} star={roll.Roll.Star} expectedPower={power} status={roll.Stats.Status}"));
            }
        }

        private static void AppendV15Shop(StringBuilder text, GameObservation o, Shop.ShopOpportunityResult shop)
        {
            var mass = shop.FutureMass;
            text.AppendLine(FormattableString.Invariant($"Shop current={TraceNumber(shop.CurrentOpportunity)} future={TraceNumber(shop.ExpectedFutureOpportunity)} discounted={TraceNumber(shop.DiscountedFutureOpportunity)} rerollEconomy={TraceNumber(shop.RerollEconomy?.TotalDelta)} rerollScore={TraceNumber(shop.Score)} gold={shop.BeforeGold} afterGold={shop.RemainingGold} price={shop.RerollPrice} discount={shop.FutureDiscount} slots={o.Shop?.SlotCount}"));
            text.AppendLine($"ShopMass positive={TraceNumber(mass?.Positive)} nonPositive={TraceNumber(mass?.NonPositive)} unaffordable={TraceNumber(mass?.InsufficientGold)} unreachable={TraceNumber(mass?.NoReachablePlacement)} unavailable={TraceNumber(mass?.Unavailable)}");
            if (shop.Distribution != null && shop.Distribution.UnavailableReason == null)
            {
                var known = new HashSet<string>(o.Characters.Select(c => c.DefinitionId).Concat(
                    o.Shop.Offers.Where(a => a.Kind == Shop.ShopOfferKind.Character).Select(a => a.DefinitionId)), StringComparer.Ordinal);
                var outcomes = shop.Distribution.Outcomes;
                text.AppendLine(FormattableString.Invariant($"Distribution knownMass={outcomes.Where(x => known.Contains(x.DefinitionId)).Sum(x => x.SlotProbability)} unknownMass={outcomes.Where(x => !known.Contains(x.DefinitionId)).Sum(x => x.SlotProbability)} costMass={string.Join(",", outcomes.GroupBy(x => x.Cost).OrderBy(g => g.Key).Select(g => g.Key + ":" + TraceNumber(g.Sum(x => x.SlotProbability))))}"));
            }
            if (shop.BestCurrent?.IntegratedDelta != null)
                text.AppendLine($"ShopBest offer={shop.BestCurrent.OfferId} definition={shop.BestCurrent.DefinitionId} " + TraceValue(shop.BestCurrent.IntegratedDelta.Value));
            foreach (var offer in shop.Details.Where(x => x.OfferId == null))
                text.AppendLine(FormattableString.Invariant($"FutureOffer definition={offer.DefinitionId} cost={offer.Cost} probability={offer.Probability} status={offer.Status} benefit={(offer.Status == Shop.ShopPurchaseOpportunityStatus.Positive ? TraceNumber(offer.Benefit) : "Unavailable")}"));
        }

        private static void AppendV15Structure(StringBuilder text, IntegratedCompositionValueEvaluator integrated,
            IntegratedCompositionEvaluation evaluation, string label, bool current, GameObservation o)
        {
            try
            {
                var counts = integrated.CacheStatistics;
                int searches = integrated.DiagnosticCounts.OptionSearches;
                var data = integrated.Describe(evaluation, current);
                var d = data.Option.Details;
                var plan = data.Synergy.Details;
                text.AppendLine($"Detail {label} staticRound=True attributes={string.Join(",", plan.EstablishedAttributes)}");
                foreach (string tag in plan.EstablishedAttributes)
                {
                    var slots = evaluation.DiagnosticState.Board.Where(m =>
                        o.SelectedRoster.First(x => x.DefinitionId == m.DefinitionId).Attributes.Contains(tag)).Select(m => m.SlotIndex);
                    text.AppendLine($"  Attribute tag={tag} slots={string.Join(",", slots)}");
                }
                foreach (var u in plan.UnitProgress)
                    text.AppendLine(FormattableString.Invariant($"  UnitProgress unit={u.UnitName} connected={u.LargestConnectedCount} value={u.ProgressValue}"));
                text.AppendLine($"  Established={string.Join(",", plan.Round.EstablishedUnits.Select(x => x.UnitName))} blocked={string.Join(",", plan.Round.UnusedUnits.Select(x => x.UnitName))}");
                foreach (var move in plan.Round.Activations)
                    text.AppendLine($"  Activation unit={move.Unit.UnitName} starter={move.InitiatorSlot} participants={string.Join(",", move.Unit.ParticipantSlots)}");
                foreach (var member in plan.Round.Members)
                    text.AppendLine($"  Participation slot={member.Slot} star={evaluation.DiagnosticState.Board.First(x => x.SlotIndex == member.Slot).Star} count={member.Count} limit={member.Limit}");
                text.AppendLine(FormattableString.Invariant($"  Option status={data.Option.Status} value={data.Option.HandUnitOption} coefficient={d.Coefficient} current={TraceSynergy(d.CurrentSynergy)} after={TraceSynergy(d.BestAfterSynergy)} rawDelta={TraceSynergy(d.BestDelta)} winner={d.BestHand?.DefinitionId} winnerStar={d.BestHand?.Star} handSlot={d.BestHand?.SlotIndex} boardSlot={d.BestBoardSlot} exchange={d.IsExchange} outgoing={d.ExchangedCharacter?.DefinitionId} outgoingStar={d.ExchangedCharacter?.Star} legal={d.LegalCandidates?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable"} new={d.NewEvaluations} skipped={d.SafeSkips} reused={d.CacheReuses}"));
                var core = data.ImmediateCore;
                text.AppendLine(core.HasValue && d.BestDelta.HasValue
                    ? FormattableString.Invariant($"  ImmediateWithoutOption combat={core.Value.BoardCombat} reserve={core.Value.HandReserve} merge={core.Value.MergeProgress} capacity={core.Value.Capacity} total={(decimal)(core.Value.BoardCombat + core.Value.HandReserve + core.Value.MergeProgress + core.Value.Capacity) + d.BestDelta.Value.TotalSynergyValue}")
                    : "  ImmediateWithoutOption=NotCaptured");
                text.AppendLine(FormattableString.Invariant($"  DiagnosticWork boardDetailRecomputations=1 optionSearches={integrated.DiagnosticCounts.OptionSearches-searches} additionalSynergy={integrated.CacheStatistics.SynergyEvaluations-counts.SynergyEvaluations} shopRecalculations=0"));
            }
            catch (Exception error)
            {
                text.AppendLine($"Detail {label}=Unavailable ({error.GetType().Name})");
            }
        }
    }
}
