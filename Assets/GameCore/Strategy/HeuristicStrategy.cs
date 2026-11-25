using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Strategy
{
    /// <summary>Immutable initial heuristic weights. Values are tunable hypotheses, not balance claims.</summary>
    public sealed class HeuristicStrategySettings
    {
        public int PreparationDecisionLimit { get; }
        public int? ShopRerollGoldCost { get; }
        public int? NormalExperienceGoldCost { get; }
        public int? SelectBExperienceGoldCost { get; }
        public int RerollGoldMinimum { get; }
        public int ExperienceGoldMinimum { get; }
        public int GoldReserve { get; }
        public int SelectDHealAmount { get; }
        public double SelectAValue { get; }
        public double SelectBValue { get; }
        public double SelectCValue { get; }
        public double SelectDBaseValue { get; }
        public double SelectDHealValuePerHp { get; }
        public double CharacterPurchaseValue { get; }
        public double MergePotentialValue { get; }
        public double MergeChainPotentialValue { get; }
        public double ItemPurchaseValue { get; }
        public double GoldSpendPenaltyPerGold { get; }
        public double ExcessGoldValuePerGold { get; }
        public double ExperienceValue { get; }
        public double NearLevelUpBonus { get; }
        public double RerollValue { get; }
        public double StartBattleValue { get; }
        public double ForcedStartBattleValue { get; }
        public double BoardImprovementValue { get; }
        public double OverLevelReliefValue { get; }
        public double UnitPartialValue { get; }
        public double UnitEstablishedValue { get; }
        public double AttributeSharedValue { get; }
        public double AttackValue { get; }
        public double DefenseValue { get; }
        public double CharacterStarValue { get; }
        public double PurchasePowerReference { get; }
        public double ImmediateBoardPurchasePowerWeight { get; }
        public double HandPurchasePowerWeight { get; }
        public double MergeProgressOneStarValue { get; }
        public double MergeProgressOneStarTwoStarValue { get; }
        public double MergeProgressTwoStarTwoCopiesValue { get; }
        public double ChainMergeValue { get; }
        public double HandSlotReleaseValue { get; }
        public double BoardSlotReleaseValue { get; }
        public double HandCapacityPenaltyOneEmpty { get; }
        public double HandCapacityPenaltyNoEmpty { get; }
        public bool IncludeTemporaryCharacterBonuses { get; }
        public double ItemUseBaseValue { get; }
        public double ItemEffectValuePerPoint { get; }
        public double ItemStarUpgradeValue { get; }
        public double SellGoldValuePerGold { get; }
        public double CharacterSaleLossPenalty { get; }
        public double ItemSaleSpaceValue { get; }
        public double NoImprovementPenalty { get; }
        public double BoardFillValue { get; }
        public double HandDeploymentPriority { get; }
        public double EmptyBoardPurchasePriority { get; }
        public double StartBattleHandOpportunityPenalty { get; }
        public double StartBattlePurchaseOpportunityPenalty { get; }
        public double StartBattleMergeOpportunityPenalty { get; }
        public double StartBattleUnitReadinessValue { get; }
        public double PreparationUrgencyPerDecision { get; }

        public HeuristicStrategySettings(int preparationDecisionLimit = 30,
            int? shopRerollGoldCost = null, int? normalExperienceGoldCost = null,
            int? selectBExperienceGoldCost = null, int rerollGoldMinimum = 12,
            int experienceGoldMinimum = 8, int goldReserve = 8, int selectDHealAmount = 10,
            double selectAValue = 14, double selectBValue = 17, double selectCValue = 22,
            double selectDBaseValue = 0, double selectDHealValuePerHp = 3,
            double characterPurchaseValue = 24, double mergePotentialValue = 75,
            double mergeChainPotentialValue = 18, double itemPurchaseValue = 16,
            double goldSpendPenaltyPerGold = 0.35, double excessGoldValuePerGold = 0.08,
            double experienceValue = 12, double nearLevelUpBonus = 16,
            double rerollValue = 8, double startBattleValue = 30,
            double forcedStartBattleValue = 1000000, double boardImprovementValue = 8,
            double overLevelReliefValue = 100, double unitPartialValue = 2,
            double unitEstablishedValue = 8, double attributeSharedValue = 5,
            double attackValue = 1, double defenseValue = 0.6,
            double itemUseBaseValue = 12, double itemEffectValuePerPoint = 2,
            double itemStarUpgradeValue = 18, double sellGoldValuePerGold = 0.3,
            double characterSaleLossPenalty = 18, double itemSaleSpaceValue = 9,
            double noImprovementPenalty = -100, double boardFillValue = 42,
            double handDeploymentPriority = 250, double emptyBoardPurchasePriority = 100,
            double startBattleHandOpportunityPenalty = 90,
            double startBattlePurchaseOpportunityPenalty = 82,
            double startBattleMergeOpportunityPenalty = 38,
            double startBattleUnitReadinessValue = 5,
            double preparationUrgencyPerDecision = 0.75,
            double characterStarValue = 3, bool includeTemporaryCharacterBonuses = false,
            double purchasePowerReference = 3.5, double immediateBoardPurchasePowerWeight = 4.0,
            double handPurchasePowerWeight = 0.5,
            double mergeProgressOneStarValue = 8, double mergeProgressOneStarTwoStarValue = 2,
            double mergeProgressTwoStarTwoCopiesValue = 4, double chainMergeValue = 45,
            double handSlotReleaseValue = 4, double boardSlotReleaseValue = 2,
            double handCapacityPenaltyOneEmpty = 2, double handCapacityPenaltyNoEmpty = 5)
        {
            if (preparationDecisionLimit < 1) throw new ArgumentOutOfRangeException(nameof(preparationDecisionLimit));
            ValidateCost(shopRerollGoldCost, nameof(shopRerollGoldCost));
            ValidateCost(normalExperienceGoldCost, nameof(normalExperienceGoldCost));
            ValidateCost(selectBExperienceGoldCost, nameof(selectBExperienceGoldCost));
            if (rerollGoldMinimum < 0) throw new ArgumentOutOfRangeException(nameof(rerollGoldMinimum));
            if (experienceGoldMinimum < 0) throw new ArgumentOutOfRangeException(nameof(experienceGoldMinimum));
            if (goldReserve < 0) throw new ArgumentOutOfRangeException(nameof(goldReserve));
            if (selectDHealAmount < 0) throw new ArgumentOutOfRangeException(nameof(selectDHealAmount));
            PreparationDecisionLimit=preparationDecisionLimit; ShopRerollGoldCost=shopRerollGoldCost;
            NormalExperienceGoldCost=normalExperienceGoldCost; SelectBExperienceGoldCost=selectBExperienceGoldCost;
            RerollGoldMinimum=rerollGoldMinimum; ExperienceGoldMinimum=experienceGoldMinimum; GoldReserve=goldReserve; SelectDHealAmount=selectDHealAmount;
            SelectAValue=Finite(selectAValue,nameof(selectAValue)); SelectBValue=Finite(selectBValue,nameof(selectBValue));
            SelectCValue=Finite(selectCValue,nameof(selectCValue)); SelectDBaseValue=Finite(selectDBaseValue,nameof(selectDBaseValue));
            SelectDHealValuePerHp=NonNegative(selectDHealValuePerHp,nameof(selectDHealValuePerHp));
            CharacterPurchaseValue=Finite(characterPurchaseValue,nameof(characterPurchaseValue));
            MergePotentialValue=Finite(mergePotentialValue,nameof(mergePotentialValue));
            MergeChainPotentialValue=Finite(mergeChainPotentialValue,nameof(mergeChainPotentialValue));
            ItemPurchaseValue=Finite(itemPurchaseValue,nameof(itemPurchaseValue));
            GoldSpendPenaltyPerGold=NonNegative(goldSpendPenaltyPerGold,nameof(goldSpendPenaltyPerGold));
            ExcessGoldValuePerGold=NonNegative(excessGoldValuePerGold,nameof(excessGoldValuePerGold));
            ExperienceValue=Finite(experienceValue,nameof(experienceValue)); NearLevelUpBonus=Finite(nearLevelUpBonus,nameof(nearLevelUpBonus));
            RerollValue=Finite(rerollValue,nameof(rerollValue)); StartBattleValue=Finite(startBattleValue,nameof(startBattleValue));
            ForcedStartBattleValue=Finite(forcedStartBattleValue,nameof(forcedStartBattleValue));
            BoardImprovementValue=NonNegative(boardImprovementValue,nameof(boardImprovementValue));
            OverLevelReliefValue=NonNegative(overLevelReliefValue,nameof(overLevelReliefValue));
            UnitPartialValue=NonNegative(unitPartialValue,nameof(unitPartialValue));
            UnitEstablishedValue=NonNegative(unitEstablishedValue,nameof(unitEstablishedValue));
            AttributeSharedValue=NonNegative(attributeSharedValue,nameof(attributeSharedValue));
            AttackValue=NonNegative(attackValue,nameof(attackValue)); DefenseValue=NonNegative(defenseValue,nameof(defenseValue));
            CharacterStarValue=NonNegative(characterStarValue,nameof(characterStarValue));
            PurchasePowerReference=NonNegative(purchasePowerReference,nameof(purchasePowerReference));
            ImmediateBoardPurchasePowerWeight=NonNegative(immediateBoardPurchasePowerWeight,nameof(immediateBoardPurchasePowerWeight));
            HandPurchasePowerWeight=NonNegative(handPurchasePowerWeight,nameof(handPurchasePowerWeight));
            MergeProgressOneStarValue=NonNegative(mergeProgressOneStarValue,nameof(mergeProgressOneStarValue));
            MergeProgressOneStarTwoStarValue=NonNegative(mergeProgressOneStarTwoStarValue,nameof(mergeProgressOneStarTwoStarValue));
            MergeProgressTwoStarTwoCopiesValue=NonNegative(mergeProgressTwoStarTwoCopiesValue,nameof(mergeProgressTwoStarTwoCopiesValue));
            ChainMergeValue=NonNegative(chainMergeValue,nameof(chainMergeValue));
            HandSlotReleaseValue=NonNegative(handSlotReleaseValue,nameof(handSlotReleaseValue));
            BoardSlotReleaseValue=NonNegative(boardSlotReleaseValue,nameof(boardSlotReleaseValue));
            HandCapacityPenaltyOneEmpty=NonNegative(handCapacityPenaltyOneEmpty,nameof(handCapacityPenaltyOneEmpty));
            HandCapacityPenaltyNoEmpty=NonNegative(handCapacityPenaltyNoEmpty,nameof(handCapacityPenaltyNoEmpty));
            IncludeTemporaryCharacterBonuses=includeTemporaryCharacterBonuses;
            ItemUseBaseValue=Finite(itemUseBaseValue,nameof(itemUseBaseValue));
            ItemEffectValuePerPoint=NonNegative(itemEffectValuePerPoint,nameof(itemEffectValuePerPoint));
            ItemStarUpgradeValue=NonNegative(itemStarUpgradeValue,nameof(itemStarUpgradeValue));
            SellGoldValuePerGold=NonNegative(sellGoldValuePerGold,nameof(sellGoldValuePerGold));
            CharacterSaleLossPenalty=NonNegative(characterSaleLossPenalty,nameof(characterSaleLossPenalty));
            ItemSaleSpaceValue=NonNegative(itemSaleSpaceValue,nameof(itemSaleSpaceValue));
            NoImprovementPenalty=Finite(noImprovementPenalty,nameof(noImprovementPenalty));
            BoardFillValue=NonNegative(boardFillValue,nameof(boardFillValue));
            HandDeploymentPriority=NonNegative(handDeploymentPriority,nameof(handDeploymentPriority));
            EmptyBoardPurchasePriority=NonNegative(emptyBoardPurchasePriority,nameof(emptyBoardPurchasePriority));
            StartBattleHandOpportunityPenalty=NonNegative(startBattleHandOpportunityPenalty,nameof(startBattleHandOpportunityPenalty));
            StartBattlePurchaseOpportunityPenalty=NonNegative(startBattlePurchaseOpportunityPenalty,nameof(startBattlePurchaseOpportunityPenalty));
            StartBattleMergeOpportunityPenalty=NonNegative(startBattleMergeOpportunityPenalty,nameof(startBattleMergeOpportunityPenalty));
            StartBattleUnitReadinessValue=NonNegative(startBattleUnitReadinessValue,nameof(startBattleUnitReadinessValue));
            PreparationUrgencyPerDecision=NonNegative(preparationUrgencyPerDecision,nameof(preparationUrgencyPerDecision));
        }
        private static void ValidateCost(int? n,string name){if(n.HasValue&&n.Value<0)throw new ArgumentOutOfRangeException(name);}
        private static double Finite(double n,string name){if(double.IsNaN(n)||double.IsInfinity(n))throw new ArgumentOutOfRangeException(name);return n;}
        private static double NonNegative(double n,string name){Finite(n,name);if(n<0)throw new ArgumentOutOfRangeException(name);return n;}
    }

    [Serializable]
    public sealed class HeuristicActionEvaluation
    {
        public GameAction Action { get; }
        public double Score { get; }
        public string Reason { get; }
        internal HeuristicActionEvaluation(GameAction action,double score,string reason){Action=action;Score=score;Reason=reason??string.Empty;}
    }

    /// <summary>Deterministic public-observation policy; per-trial progress is in StrategyState only.</summary>
    public sealed class HeuristicStrategy : IPlayerStrategy
    {
        public const string StableId="HeuristicStrategy", StableVersion="7", Version8="8", Version9="9", Version10="10", Version11="11";
        public const int StateSchemaVersion=2;
        private const string Prefix="hs2|";
        private readonly HeuristicStrategySettings _settings;
        private readonly string _version;
        private readonly RandomStrategy _candidateSource;
        private readonly Action<string> _decisionDiagnosticSink;
        private readonly ProjectNST.GameCore.Sessions.SimulationProfileCollector _profiler;
        public string StrategyId=>StableId;
        public string Version=>_version;
        public HeuristicStrategySettings Settings=>_settings;

        public HeuristicStrategy(HeuristicStrategySettings settings=null, Action<string> decisionDiagnosticSink=null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler=null)
            : this(settings, decisionDiagnosticSink, profiler, StableVersion)
        {
        }

        /// <summary>Creates v8 with the candidate-only power defaults; supplied settings can isolate formula factors.</summary>
        public static HeuristicStrategy CreateV8(HeuristicStrategySettings settings=null,
            Action<string> decisionDiagnosticSink=null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler=null)
        {
            settings = settings ?? new HeuristicStrategySettings(characterStarValue:0.75,
                includeTemporaryCharacterBonuses:true);
            return new HeuristicStrategy(settings, decisionDiagnosticSink, profiler, Version8);
        }

        /// <summary>Creates v9: v8 behavior plus a public Star 1 Shop Stats adjustment for purchases.</summary>
        public static HeuristicStrategy CreateV9(HeuristicStrategySettings settings=null,
            Action<string> decisionDiagnosticSink=null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler=null)
        {
            settings = settings ?? new HeuristicStrategySettings(characterStarValue:0.75,
                includeTemporaryCharacterBonuses:true, purchasePowerReference:3.5,
                immediateBoardPurchasePowerWeight:4.0, handPurchasePowerWeight:0.5);
            return new HeuristicStrategy(settings, decisionDiagnosticSink, profiler, Version9);
        }

        public static string GetV9PurchaseConfigurationIdentity(HeuristicStrategySettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return "purchasePower-ref" + settings.PurchasePowerReference.ToString("R", CultureInfo.InvariantCulture) +
                "-board" + settings.ImmediateBoardPurchasePowerWeight.ToString("R", CultureInfo.InvariantCulture) +
                "-hand" + settings.HandPurchasePowerWeight.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Creates v10: v9 purchase evaluation plus public merge-progress and slot-value adjustments.</summary>
        public static HeuristicStrategy CreateV10(HeuristicStrategySettings settings=null,
            Action<string> decisionDiagnosticSink=null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler=null)
        {
            settings = settings ?? new HeuristicStrategySettings(characterStarValue:0.75,
                includeTemporaryCharacterBonuses:true, purchasePowerReference:3.5,
                immediateBoardPurchasePowerWeight:4.0, handPurchasePowerWeight:0.5);
            return new HeuristicStrategy(settings, decisionDiagnosticSink, profiler, Version10);
        }

        /// <summary>Creates v11: v10 purchase behavior plus non-duplicated sale and retention evaluation.</summary>
        public static HeuristicStrategy CreateV11(HeuristicStrategySettings settings=null,
            Action<string> decisionDiagnosticSink=null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler=null)
        {
            settings = settings ?? new HeuristicStrategySettings(characterStarValue:0.75,
                includeTemporaryCharacterBonuses:true, purchasePowerReference:3.5,
                immediateBoardPurchasePowerWeight:4.0, handPurchasePowerWeight:0.5);
            return new HeuristicStrategy(settings, decisionDiagnosticSink, profiler, Version11);
        }

        public static string GetV10PurchaseConfigurationIdentity(HeuristicStrategySettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return GetV9PurchaseConfigurationIdentity(settings) +
                "-merge" + settings.MergePotentialValue.ToString("R", CultureInfo.InvariantCulture) +
                "-chainBase" + settings.MergeChainPotentialValue.ToString("R", CultureInfo.InvariantCulture) +
                "-progress1" + settings.MergeProgressOneStarValue.ToString("R", CultureInfo.InvariantCulture) +
                "-progress2a" + settings.MergeProgressOneStarTwoStarValue.ToString("R", CultureInfo.InvariantCulture) +
                "-progress2b" + settings.MergeProgressTwoStarTwoCopiesValue.ToString("R", CultureInfo.InvariantCulture) +
                "-chain" + settings.ChainMergeValue.ToString("R", CultureInfo.InvariantCulture) +
                "-handSlot" + settings.HandSlotReleaseValue.ToString("R", CultureInfo.InvariantCulture) +
                "-boardSlot" + settings.BoardSlotReleaseValue.ToString("R", CultureInfo.InvariantCulture) +
                "-handPenalty1" + settings.HandCapacityPenaltyOneEmpty.ToString("R", CultureInfo.InvariantCulture) +
                "-handPenalty0" + settings.HandCapacityPenaltyNoEmpty.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string GetV11ConfigurationIdentity(HeuristicStrategySettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return GetV10PurchaseConfigurationIdentity(settings) +
                "-sellReserve" + settings.GoldReserve.ToString(CultureInfo.InvariantCulture) +
                "-sellGold" + settings.SellGoldValuePerGold.ToString("R", CultureInfo.InvariantCulture) +
                "-sellLoss" + settings.CharacterSaleLossPenalty.ToString("R", CultureInfo.InvariantCulture) +
                "-sellAtk" + settings.AttackValue.ToString("R", CultureInfo.InvariantCulture) +
                "-sellDef" + settings.DefenseValue.ToString("R", CultureInfo.InvariantCulture) +
                "-sellStar" + settings.CharacterStarValue.ToString("R", CultureInfo.InvariantCulture) +
                "-sellTemp" + settings.IncludeTemporaryCharacterBonuses.ToString() +
                "-sellAttribute" + settings.AttributeSharedValue.ToString("R", CultureInfo.InvariantCulture) +
                "-sellUnitPartial" + settings.UnitPartialValue.ToString("R", CultureInfo.InvariantCulture) +
                "-sellUnitEstablished" + settings.UnitEstablishedValue.ToString("R", CultureInfo.InvariantCulture);
        }

        private HeuristicStrategy(HeuristicStrategySettings settings, Action<string> decisionDiagnosticSink,
            ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler, string version)
        {
            _settings=settings??new HeuristicStrategySettings();
            _version=version;
            _decisionDiagnosticSink=decisionDiagnosticSink;
            _profiler=profiler;
            _candidateSource=new RandomStrategy(new RandomStrategySettings(_settings.PreparationDecisionLimit,
                _settings.ShopRerollGoldCost,_settings.NormalExperienceGoldCost,_settings.SelectBExperienceGoldCost), profiler);
        }

        public StrategyDecision ChooseAction(GameObservation observation,StrategyContext context)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            if(context==null)throw new ArgumentNullException(nameof(context));
            if(!context.IsFor(this))throw new ArgumentException("Context belongs to another Strategy.",nameof(context));
            _profiler?.Add("Strategy.ChooseActionCalls");
            Payload state=ReadState(context.State);
            bool preparation=observation.Stage.Phase==ObservationPhase.Preparation;
            int priorPreparationDecisions=state.Decisions;
            bool newEncounter=!state.HasEncounter || state.EncounterIndex!=observation.Stage.EncounterIndex;
            string resetReason=newEncounter
                ? (state.HasEncounter?"Encounter changed; reset preparation counter and previous-action filter.":"No stored Encounter identity; initialize this Encounter.")
                : "Same Encounter; preserve its preparation counter.";
            int decisions=preparation?(newEncounter?1:Increment(state.Decisions)):0;
            string previousAction=newEncounter?string.Empty:state.LastAction;
            List<string> excluded=_decisionDiagnosticSink==null?null:new List<string>();
            List<HeuristicActionEvaluation> generated=null;
            List<HeuristicActionEvaluation> evaluations=null;
            StrategyDecision decision;

            if(!observation.IsStrategyActionAllowed)
            {
                decision=context.NoAction(NewState(0,string.Empty,observation.Stage.EncounterIndex,observation.Stage.Phase),
                    "No player decision is required in this phase.");
                return PublishDecisionDiagnostic(decision,observation,context,state,priorPreparationDecisions,decisions,
                    resetReason,(IReadOnlyList<HeuristicActionEvaluation>)generated??Array.Empty<HeuristicActionEvaluation>(),
                    (IReadOnlyList<HeuristicActionEvaluation>)evaluations??Array.Empty<HeuristicActionEvaluation>(),(IReadOnlyList<string>)excluded??Array.Empty<string>(),false,"Action is not allowed in this phase.");
            }

            bool limitReached=preparation&&decisions>=_settings.PreparationDecisionLimit;
            // The Runner treats NoAction as normal completion for enemyless Preparation.
            if(limitReached&&IsEnemylessPreparation(observation))
            {
                decision=context.NoAction(NewState(decisions,string.Empty,observation.Stage.EncounterIndex,observation.Stage.Phase),
                    "Preparation decision limit reached in an enemyless stage; finish through the Runner's existing enemyless-stage path.");
                return PublishDecisionDiagnostic(decision,observation,context,state,priorPreparationDecisions,decisions,
                    resetReason,(IReadOnlyList<HeuristicActionEvaluation>)generated??Array.Empty<HeuristicActionEvaluation>(),
                    (IReadOnlyList<HeuristicActionEvaluation>)evaluations??Array.Empty<HeuristicActionEvaluation>(),(IReadOnlyList<string>)excluded??Array.Empty<string>(),false,"Enemyless Preparation reached its decision limit.");
            }

            Payload evaluationState=newEncounter
                ?new Payload(0,string.Empty,true,observation.Stage.EncounterIndex,observation.Stage.Phase,true)
                :state;
            evaluations=GetEvaluations(observation,context.NextActionId,evaluationState,decisions,out BoardEvaluationContext boardContext).ToList();
            Dictionary<GameActionKind,int> profileCountsBeforeFilter=_profiler==null?null:evaluations.GroupBy(x=>x.Action.Kind).ToDictionary(g=>g.Key,g=>g.Count());
            if(_decisionDiagnosticSink!=null)
            {
                generated=new List<HeuristicActionEvaluation>(evaluations);
                foreach(HeuristicActionEvaluation item in generated)
                {
                    string signature=Signature(item.Action);
                    string reason=null;
                    if(signature==previousAction)reason="Same action signature as previous Strategy action.";
                    else if(IsReverse(previousAction,item.Action))reason="Immediate reverse-operation filter.";
                    else if(IsImmediateBuyThenSell(previousAction,item.Action,observation))reason="Immediate buy-then-sell filter.";
                    else if(IsImmediateSellThenBuy(previousAction,item.Action))reason="Immediate sell-then-buy filter.";
                    if(reason!=null)excluded.Add(signature+" | "+reason);
                }
            }
            evaluations.RemoveAll(x=>Signature(x.Action)==previousAction || IsReverse(previousAction,x.Action));
            evaluations.RemoveAll(x=>IsImmediateBuyThenSell(previousAction,x.Action,observation) ||
                IsImmediateSellThenBuy(previousAction,x.Action));
            if(_profiler!=null&&profileCountsBeforeFilter!=null){_profiler.Add("Candidate.Filtered.Total",Math.Max(0,profileCountsBeforeFilter.Values.Sum()-evaluations.Count));foreach(var group in profileCountsBeforeFilter){int remaining=evaluations.Count(x=>x.Action.Kind==group.Key);_profiler.Add("Candidate.Filtered."+group.Key,Math.Max(0,group.Value-remaining));}}
            HeuristicActionEvaluation legalStart=evaluations.FirstOrDefault(x=>x.Action is StartBattleAction);
            bool startBattleFallback=false;
            string selectionReason;

            if(preparation && TryChooseOverLevelResolution(observation,evaluations,boardContext,out HeuristicActionEvaluation resolution))
            {
                selectionReason=resolution.Reason+" Score="+resolution.Score.ToString("R",CultureInfo.InvariantCulture)+".";
                decision=context.Decide(resolution.Action,NewState(decisions,Signature(resolution.Action),observation.Stage.EncounterIndex,observation.Stage.Phase),selectionReason);
                return PublishDecisionDiagnostic(decision,observation,context,state,priorPreparationDecisions,decisions,
                    resetReason,generated,evaluations,excluded,startBattleFallback,selectionReason);
            }

            if(limitReached)
            {
                if(legalStart!=null)
                {
                    startBattleFallback=true;
                    selectionReason="Preparation decision limit reached; prioritize the existing legal StartBattle candidate.";
                    decision=context.Decide(legalStart.Action,NewState(decisions,Signature(legalStart.Action),observation.Stage.EncounterIndex,observation.Stage.Phase),selectionReason);
                }
                else
                {
                    selectionReason=evaluations.Count==0?"Preparation decision limit reached with no generated legal action.":
                        "Preparation decision limit reached; StartBattle is not a legal candidate and no required repair is available.";
                    decision=context.NoAction(NewState(decisions,string.Empty,observation.Stage.EncounterIndex,observation.Stage.Phase),selectionReason);
                }
                return PublishDecisionDiagnostic(decision,observation,context,state,priorPreparationDecisions,decisions,
                    resetReason,generated,evaluations,excluded,startBattleFallback,selectionReason);
            }

            if(evaluations.Count==0)
            {
                selectionReason="No observable improvement or legal candidate is available.";
                decision=context.NoAction(NewState(decisions,string.Empty,observation.Stage.EncounterIndex,observation.Stage.Phase),selectionReason);
                return PublishDecisionDiagnostic(decision,observation,context,state,priorPreparationDecisions,decisions,
                    resetReason,generated,evaluations,excluded,false,selectionReason);
            }
            if(preparation&&evaluations[0].Score<=0)
            {
                if(legalStart!=null)
                {
                    startBattleFallback=true;
                    selectionReason="No positive-value preparation action remains; choose the existing legal StartBattle candidate.";
                    decision=context.Decide(legalStart.Action,NewState(decisions,Signature(legalStart.Action),observation.Stage.EncounterIndex,observation.Stage.Phase),selectionReason);
                }
                else
                {
                    selectionReason="No positive-value preparation action is visible; Runner may detect the stall.";
                    decision=context.NoAction(NewState(decisions,string.Empty,observation.Stage.EncounterIndex,observation.Stage.Phase),selectionReason);
                }
                return PublishDecisionDiagnostic(decision,observation,context,state,priorPreparationDecisions,decisions,
                    resetReason,generated,evaluations,excluded,startBattleFallback,selectionReason);
            }

            HeuristicActionEvaluation chosen=evaluations[0];
            selectionReason=chosen.Reason+" Score="+chosen.Score.ToString("R",CultureInfo.InvariantCulture)+".";
            decision=context.Decide(chosen.Action,NewState(decisions,Signature(chosen.Action),observation.Stage.EncounterIndex,observation.Stage.Phase),selectionReason);
            return PublishDecisionDiagnostic(decision,observation,context,state,priorPreparationDecisions,decisions,
                resetReason,generated,evaluations,excluded,false,selectionReason);
        }

        private StrategyDecision PublishDecisionDiagnostic(StrategyDecision decision,GameObservation observation,
            StrategyContext context,Payload before,int previousDecisions,int currentDecisions,string resetReason,
            IReadOnlyList<HeuristicActionEvaluation> generated,IReadOnlyList<HeuristicActionEvaluation> filtered,
            IReadOnlyList<string> excluded,bool startBattleFallback,string selectionReason)
        {
            if(_decisionDiagnosticSink==null)return decision;
            try
            {
                var text=new StringBuilder();
                text.AppendLine("Strategy="+StrategyId+" Version="+Version+" TrialIndex="+context.TrialIndex+" DecisionIndex="+context.DecisionStep)
                    .AppendLine("Stage="+observation.Stage.StageNumber+" Round="+observation.Stage.RoundNumber+" EncounterIndex="+observation.Stage.EncounterIndex+" Phase="+observation.Stage.Phase+" Choice="+(observation.Stage.Choice.HasValue?observation.Stage.Choice.Value.ToString():"None")+" EnemyCategory="+(observation.Stage.EnemyCategory.HasValue?observation.Stage.EnemyCategory.Value.ToString():"None"))
                    .AppendLine("PhaseDecisionCount=Unavailable (not present in StrategyContext); StrategyPreparationDecisionBefore="+previousDecisions+" After="+currentDecisions+" Limit="+_settings.PreparationDecisionLimit)
                    .AppendLine("VisibleObservationFingerprint="+CreateVisibleObservationFingerprint(observation,_settings.IncludeTemporaryCharacterBonuses,_version)+" (hash of Strategy-visible data only).")
                    .AppendLine("StrategyStateBefore="+(context.State?.Payload??"Unavailable"))
                    .AppendLine("PreviousEncounter="+(before.HasEncounter?before.EncounterIndex.ToString(CultureInfo.InvariantCulture):"Unavailable")+" CurrentEncounter="+observation.Stage.EncounterIndex+" PreviousPhase="+(before.HasIdentity?before.Phase.ToString():"Unavailable")+" CurrentPhase="+observation.Stage.Phase+" CounterReset="+(observation.Stage.Phase==ObservationPhase.Preparation&&(!before.HasEncounter||before.EncounterIndex!=observation.Stage.EncounterIndex))+" ResetReason="+resetReason+" PreviousAction="+(before.LastAction??""))
                    .AppendLine("GeneratedCandidateCount="+generated.Count+" FilteredCandidateCount="+filtered.Count+" ExcludedCandidateCount="+excluded.Count);
                foreach(HeuristicActionEvaluation item in generated)
                {
                    string signature=Signature(item.Action);
                    bool remains=filtered.Any(x=>Signature(x.Action)==signature);
                    string exclusion=excluded.FirstOrDefault(x=>x.StartsWith(signature+" | ",StringComparison.Ordinal));
                    text.AppendLine("Candidate generated="+DescribeCandidate(item.Action)+" score="+item.Score.ToString("R",CultureInfo.InvariantCulture)+" reason="+item.Reason+" filtered="+(remains?"yes":"no")+" exclusion="+(exclusion??"none"));
                }
                HeuristicActionEvaluation start=generated.FirstOrDefault(x=>x.Action is StartBattleAction);
                string startRemoval=excluded.FirstOrDefault(x=>x.StartsWith("15 | ",StringComparison.Ordinal));
                bool startEligible=observation.Stage.Phase==ObservationPhase.Preparation&&observation.Stage.HasSelection&&
                    observation.Stage.Choice!=StageChoice.SelectB&&observation.Stage.Choice!=StageChoice.SelectD&&
                    observation.Stage.EnemyCategory.HasValue&&observation.Stage.EnemyCategory.Value!=StageEnemyCategory.None&&
                    observation.Player.HasHealth&&observation.Player.HP>0&&
                    observation.Board.Slots.Count(x=>x.Character!=null)<=observation.Player.Level;
                text.AppendLine("StartBattle eligibilityFromObservation="+startEligible+" generated="+(start!=null)+" filteredOut="+(start!=null&&startRemoval!=null)+" score="+(start==null?"Unavailable":start.Score.ToString("R",CultureInfo.InvariantCulture))+" fallback="+startBattleFallback+" notSelectedReason="+(start==null?"Candidate was not generated.":startRemoval??(decision.Action is StartBattleAction?"Selected.":"Other action ranked first or policy returned NoAction.")));
                foreach(string item in excluded)text.AppendLine("Excluded="+item);
                text.AppendLine("FinalAction="+(decision.Action==null?"NoAction":DescribeCandidate(decision.Action))+" FinalReason="+selectionReason)
                    .AppendLine("StrategyStateAfter="+(decision.NextState?.Payload??"Unavailable"));
                _decisionDiagnosticSink(text.ToString());
            }
            catch { /* Diagnostic sinks must never affect Strategy output. */ }
            return decision;
        }

        private static string CreateVisibleObservationFingerprint(GameObservation observation,bool includeTemporaryBonuses,string version)
        {
            var snapshot=new StringBuilder();
            if(string.Equals(version,Version8,StringComparison.Ordinal))snapshot.Append("heuristic-power-v8|");
            else if(string.Equals(version,Version9,StringComparison.Ordinal))snapshot.Append("heuristic-purchase-power-v9|");
            else if(string.Equals(version,Version10,StringComparison.Ordinal))snapshot.Append("heuristic-purchase-merge-v10|");
            else if(string.Equals(version,Version11,StringComparison.Ordinal))snapshot.Append("heuristic-sale-v11|");
            snapshot.Append(observation.Stage.EncounterIndex).Append('|').Append(observation.Stage.StageNumber).Append('|').Append(observation.Stage.RoundNumber)
                .Append('|').Append(observation.Stage.Phase).Append('|').Append(observation.Stage.Choice).Append('|').Append(observation.Stage.EnemyCategory)
                .Append('|').Append(observation.Player.Gold).Append('|').Append(observation.Player.Level).Append('|').Append(observation.Player.XP)
                .Append('|').Append(observation.Player.HP).Append('|').Append(observation.Player.MaximumHP);
            if(string.Equals(version,Version11,StringComparison.Ordinal))snapshot.Append("|BoardColumns=").Append(observation.Board.Columns);
            foreach(BoardSlotObservation slot in observation.Board.Slots)
            {
                snapshot.Append("|B").Append(slot.SlotIndex).Append('=');
                if(slot.Character!=null)
                {
                    snapshot.Append(slot.Character.InstanceId).Append(':').Append(slot.Character.DefinitionId).Append(':').Append(slot.Character.Star).Append(':').Append(slot.Character.Attack).Append(':').Append(slot.Character.Defense);
                    if(string.Equals(version,Version10,StringComparison.Ordinal)||string.Equals(version,Version11,StringComparison.Ordinal))snapshot.Append(":RC").Append(slot.Character.RunCost);
                    if(string.Equals(version,Version11,StringComparison.Ordinal))AppendSaleFingerprintCharacter(snapshot,slot.Character);
                    if(includeTemporaryBonuses)snapshot.Append(":TB").Append(slot.Character.TemporaryAttackBonus).Append(':').Append(slot.Character.TemporaryDefenseBonus);
                }
            }
            foreach(HandSlotObservation slot in observation.Hand.Slots)
            {
                snapshot.Append("|H").Append(slot.SlotIndex).Append('=').Append(slot.Kind);
                if(slot.Character!=null)
                {
                    snapshot.Append(':').Append(slot.Character.InstanceId).Append(':').Append(slot.Character.DefinitionId);
                    if(string.Equals(version,Version10,StringComparison.Ordinal))snapshot.Append(":ST").Append(slot.Character.Star).Append(":RC").Append(slot.Character.RunCost).Append(":L").Append(slot.SlotIndex);
                    if(string.Equals(version,Version11,StringComparison.Ordinal))
                    {snapshot.Append(":L").Append(slot.SlotIndex);AppendSaleFingerprintCharacter(snapshot,slot.Character);}
                    if(includeTemporaryBonuses)snapshot.Append(":TB").Append(slot.Character.TemporaryAttackBonus).Append(':').Append(slot.Character.TemporaryDefenseBonus);
                }
                if(slot.Item!=null)snapshot.Append(':').Append(slot.Item.DefinitionId);
            }
            if(observation.Shop!=null)foreach(ShopOfferObservation offer in observation.Shop.Offers.OrderBy(x=>x.OfferIndex))
            {
                snapshot.Append("|O").Append(offer.OfferIndex).Append(':').Append(offer.OfferId).Append(':').Append(offer.DefinitionId).Append(':').Append(offer.DisplayPrice).Append(':').Append(offer.Status);
                if((string.Equals(version,Version9,StringComparison.Ordinal)||string.Equals(version,Version10,StringComparison.Ordinal)||string.Equals(version,Version11,StringComparison.Ordinal))&&offer.CharacterStats!=null)
                    snapshot.Append(":S").Append(offer.CharacterStats.IsAvailable).Append(':').Append(offer.CharacterStats.MinimumAttack)
                        .Append(':').Append(offer.CharacterStats.MaximumAttack).Append(':').Append(offer.CharacterStats.MinimumDefense)
                        .Append(':').Append(offer.CharacterStats.MaximumDefense);
                if(string.Equals(version,Version10,StringComparison.Ordinal)||string.Equals(version,Version11,StringComparison.Ordinal))snapshot.Append(":C").Append(offer.CharacterCost);
            }
            if(string.Equals(version,Version11,StringComparison.Ordinal)&&observation.Synergy!=null)
            {
                snapshot.Append("|Synergy=").Append(observation.Synergy.IsAvailable);
                foreach(SynergyUnitObservation unit in observation.Synergy.Units.OrderBy(x=>x.UnitName,StringComparer.Ordinal))
                    snapshot.Append("|SU:").Append(unit.UnitName).Append(':').Append(string.Join(",",unit.ParticipantInstanceIds.OrderBy(x=>x,StringComparer.Ordinal)));
            }
            using(SHA256 sha=SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(snapshot.ToString()))).Replace("-",string.Empty).ToLowerInvariant();
        }

        private static void AppendSaleFingerprintCharacter(StringBuilder snapshot,CharacterObservation character)
        {
            snapshot.Append(":S").Append(character.Star).Append(":RC").Append(character.RunCost)
                .Append(":A").Append(character.Attack).Append(":D").Append(character.Defense)
                .Append(":AT=").Append(string.Join(",",character.Attributes.OrderBy(x=>x,StringComparer.Ordinal)))
                .Append(":UN=").Append(string.Join(",",character.Units.OrderBy(x=>x,StringComparer.Ordinal)));
        }

        private static string DescribeCandidate(GameAction action)
        {
            switch(action)
            {
                case SelectStageChoiceAction x:return "SelectStageChoice("+x.Choice+")";
                case BuyCharacterAction x:return "BuyCharacter(offer="+x.OfferId+",placement="+x.Placement.Kind+":"+x.Placement.SlotIndex+")";
                case BuyItemAction x:return "BuyItem(offer="+x.OfferId+",hand="+x.HandSlot+")";
                case RerollShopAction x:return "RerollShop("+x.ShopKind+")";
                case MoveOrSwapBoardAction x:return "MoveOrSwapBoard("+x.SourceSlot+"->"+x.TargetSlot+")";
                case MoveOrSwapHandAction x:return "MoveOrSwapHand("+x.SourceSlot+"->"+x.TargetSlot+")";
                case MoveBoardToHandAction x:return "MoveBoardToHand("+x.BoardSlot+"->"+x.HandSlot+")";
                case MoveHandToBoardAction x:return "MoveHandToBoard("+x.HandSlot+"->"+x.BoardSlot+")";
                case SellCharacterAction x:return "SellCharacter("+x.InstanceId+")";
                case SellItemAction x:return "SellItem("+x.HandSlot+")";
                case UseHandItemAction x:return "UseHandItem("+x.HandSlot+","+x.Target.Kind+":"+x.Target.SlotIndex+")";
                case UseEventItemAction x:return "UseEventItem("+x.Target.Kind+":"+x.Target.SlotIndex+")";
                case StartBattleAction:return "StartBattle";
                case EndEventAction:return "EndEvent";
                case BuyExperienceAction:return "BuyExperience";
                default:return action==null?"NoAction":action.Kind.ToString();
            }
        }

        /// <summary>
        /// Resolves a Board/Level mismatch before ordinary heuristic ranking. Only actions already
        /// generated by RandomStrategy are considered, so this changes preference, not legality.
        /// </summary>
        private static bool IsEnemylessPreparation(GameObservation observation) =>
            observation.Stage.HasSelection && observation.Stage.EnemyCategory.HasValue &&
            observation.Stage.EnemyCategory.Value==StageEnemyCategory.None;

        private bool TryChooseOverLevelResolution(GameObservation observation,
            IReadOnlyList<HeuristicActionEvaluation> evaluations,BoardEvaluationContext boardContext,out HeuristicActionEvaluation chosen)
        {
            chosen=null;
            int boardCount=BoardCount(observation);
            int excess=boardCount-observation.Player.Level;
            if(excess<=0)return false;

            // First use an actually empty Hand slot. A Character-to-Character cross move is a
            // swap and does not reduce Board population, so it is deliberately excluded here.
            HeuristicActionEvaluation transfer=evaluations
                .Where(x=>x.Action is MoveBoardToHandAction move && IsEmptyHandSlot(observation,move.HandSlot))
                .OrderByDescending(x=>BoardTransferRetention(observation,(MoveBoardToHandAction)x.Action,boardContext))
                .ThenBy(x=>Signature(x.Action),StringComparer.Ordinal).FirstOrDefault();
            if(transfer!=null)
            {
                chosen=WithReason(transfer,"Board exceeds Level; move a Board character into an empty Hand slot to reduce the excess without selling it.");
                return true;
            }

            // If a single available XP purchase actually levels up, it reduces the excess without
            // discarding an owned character. Buying XP that does not change Level is not a remedy.
            bool oneXpLevels=observation.Player.Level<observation.Player.MaximumLevel &&
                observation.Player.RequiredXP>0 && observation.Player.XP+1>=observation.Player.RequiredXP;
            if(oneXpLevels)
            {
                HeuristicActionEvaluation experience=evaluations.FirstOrDefault(x=>x.Action is BuyExperienceAction);
                if(experience!=null)
                {
                    chosen=WithReason(experience,"Board exceeds Level; this affordable XP purchase immediately raises Level and reduces the excess.");
                    return true;
                }
            }

            // If no empty Hand slot or useful one-step level-up exists, sell only a Board occupant.
            // Select the least costly visible loss (including its synergy contribution), rather
            // than applying a blanket sale bonus. Once BoardCount <= Level, normal ranking resumes.
            HeuristicActionEvaluation sale=evaluations
                .Where(x=>x.Action is SellCharacterAction action &&
                    observation.Characters.Any(c=>c.InstanceId==action.InstanceId && c.LocationKind==ObservationLocationKind.Board))
                .OrderByDescending(x=>x.Score)
                .ThenBy(x=>Signature(x.Action),StringComparer.Ordinal).FirstOrDefault();
            if(sale!=null)
            {
                chosen=WithReason(sale,"Board exceeds Level and no empty Hand transfer or immediately effective level-up is available; sell the Board character with the smallest visible power/synergy loss.");
                return true;
            }
            return false;
        }

        private double BoardTransferRetention(GameObservation observation,MoveBoardToHandAction action,BoardEvaluationContext boardContext)
        {
            CharacterObservation[] before=boardContext.Board;
            if(action.BoardSlot<0||action.BoardSlot>=before.Length||before[action.BoardSlot]==null)return double.NegativeInfinity;
            HandSlotObservation target=observation.Hand.Slots.FirstOrDefault(x=>x.SlotIndex==action.HandSlot);
            if(target==null||target.Kind!=HandSlotContentKind.Empty)return double.NegativeInfinity;
            return boardContext.QualityAfterReplacement(action.BoardSlot,null)-boardContext.BaseQuality;
        }

        private static bool IsEmptyHandSlot(GameObservation observation,int slot)
        {
            HandSlotObservation hand=observation.Hand.Slots.FirstOrDefault(x=>x.SlotIndex==slot);
            return hand!=null&&hand.Kind==HandSlotContentKind.Empty;
        }

        private static HeuristicActionEvaluation WithReason(HeuristicActionEvaluation evaluation,string reason)
            =>new HeuristicActionEvaluation(evaluation.Action,evaluation.Score,reason);

        /// <summary>Candidate list is exactly the existing RandomStrategy public candidate generator; only ranking differs.</summary>
        public IReadOnlyList<HeuristicActionEvaluation> GetEvaluations(GameObservation observation,ActionId actionId)
            => GetEvaluations(observation,actionId,new Payload(0,string.Empty,false,-1,ObservationPhase.AwaitingStageChoice,false),0,out _);

        private IReadOnlyList<HeuristicActionEvaluation> GetEvaluations(GameObservation observation,ActionId actionId,Payload state,int decisions,
            out BoardEvaluationContext boardContext)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            boardContext=null;
            if(!observation.IsStrategyActionAllowed)return Array.AsReadOnly(Array.Empty<HeuristicActionEvaluation>());
            GameAction[] candidates;
            using(_profiler==null?default(ProjectNST.GameCore.Sessions.SimulationProfileCollector.Scope):_profiler.Measure("Strategy.CandidateGeneration"))
                candidates=_candidateSource.GetCandidates(observation,actionId).ToArray();
            if(_profiler!=null){_profiler.Add("Candidate.Generated.Total",candidates.Length);foreach(var group in candidates.GroupBy(x=>x.Kind))_profiler.Add("Candidate.Generated."+group.Key,group.LongCount());}
            Dictionary<string,MergeProgressCounts> mergeProgressCounts=null;
            bool usesV10PurchaseRules=string.Equals(_version,Version10,StringComparison.Ordinal)||string.Equals(_version,Version11,StringComparison.Ordinal);
            PurchaseMergeEstimate[] purchaseMergeEstimates=usesV10PurchaseRules
                ?BuildPurchaseMergeEstimates(observation,string.Equals(_version,Version11,StringComparison.Ordinal),out mergeProgressCounts):null;
            PurchasePowerEstimate[] purchasePowerEstimates=string.Equals(_version,Version9,StringComparison.Ordinal)
                ?BuildPurchasePowerEstimates(observation):usesV10PurchaseRules
                    ?BuildPurchasePowerEstimates(observation,true,purchaseMergeEstimates):null;
            var evaluated=new List<HeuristicActionEvaluation>(candidates.Length);
            foreach(GameAction candidate in candidates)
            {
                _profiler?.Add("Candidate.Evaluated."+candidate.Kind);
                using(_profiler==null?default(ProjectNST.GameCore.Sessions.SimulationProfileCollector.Scope):_profiler.Measure("Strategy.Evaluation."+candidate.Kind))
                {
                    if(boardContext==null&&IsBoardEvaluationAction(candidate))
                        boardContext=new BoardEvaluationContext(this,observation);
                    evaluated.Add(Evaluate(observation,candidate,candidates,state,decisions,boardContext,purchasePowerEstimates,purchaseMergeEstimates,mergeProgressCounts));
                }
            }
            return Array.AsReadOnly(evaluated.OrderByDescending(x=>x.Score)
                .ThenBy(x=>Signature(x.Action),StringComparer.Ordinal).ToArray());
        }

        private HeuristicActionEvaluation Evaluate(GameObservation o,GameAction a,GameAction[] candidates,Payload state,int decisions,BoardEvaluationContext boardContext,PurchasePowerEstimate[] purchasePowerEstimates,PurchaseMergeEstimate[] purchaseMergeEstimates,Dictionary<string,MergeProgressCounts> mergeProgressCounts)
        {
            double score;string reason;
            switch(a)
            {
                case SelectStageChoiceAction x: score=ChoiceScore(o,x.Choice,out reason);break;
                case BuyCharacterAction x:
                    score=CharacterBuyScore(o,x,out reason);score+=PurchasePlacementBonus(o,x);
                    if(string.Equals(_version,Version9,StringComparison.Ordinal))
                    {
                        double baseV8Score=score;
                        PurchasePowerEstimate estimate=FindPurchasePowerEstimate(o,x,purchasePowerEstimates);
                        double weight=0,adjustment=0;string skippedReason=null;
                        if(!estimate.Available)skippedReason="public Character Stats range unavailable";
                        else if(x.Placement.Kind==CharacterLocationKind.Board&&BoardCount(o)+1>o.Player.Level)skippedReason="purchase would leave Board above Level";
                        else if(estimate.ImmediateMerge)skippedReason="purchase enables immediate/chain merge; resulting Stats are rerolled";
                        else if(x.Placement.Kind==CharacterLocationKind.Board)weight=_settings.ImmediateBoardPurchasePowerWeight;
                        else if(x.Placement.Kind==CharacterLocationKind.Hand)weight=_settings.HandPurchasePowerWeight;
                        else skippedReason="placement kind has no configured purchase weight";
                        if(skippedReason==null)adjustment=weight*(estimate.Power-_settings.PurchasePowerReference);
                        score=baseV8Score+adjustment;
                        if(_decisionDiagnosticSink!=null)
                        {
                            if(estimate.Available)
                                reason="v8="+baseV8Score.ToString("R",CultureInfo.InvariantCulture)+"; public ATK range="+estimate.MinAttack+".."+estimate.MaxAttack+
                                    "; public DEF range="+estimate.MinDefense+".."+estimate.MaxDefense+"; EstimatedPower="+estimate.Power.ToString("R",CultureInfo.InvariantCulture)+
                                    "; ReferencePower="+_settings.PurchasePowerReference.ToString("R",CultureInfo.InvariantCulture)+"; Weight="+weight.ToString("R",CultureInfo.InvariantCulture)+
                                    "; Adjustment="+adjustment.ToString("R",CultureInfo.InvariantCulture)+"; Final="+score.ToString("R",CultureInfo.InvariantCulture)+
                                    (skippedReason==null?"":"; adjustment skipped: "+skippedReason)+". Public range midpoint estimate; no hidden weights or roll pairs used.";
                            else reason="v8="+baseV8Score.ToString("R",CultureInfo.InvariantCulture)+"; purchase-power adjustment=0; adjustment skipped: "+skippedReason+".";
                        }
                        else reason=estimate.Available
                            ?"Purchase score adds the configured public Stats range-midpoint adjustment to the unchanged v8 score."
                            :"Purchase score falls back to the unchanged v8 score because the public Stats range is unavailable.";
                    }
                    else if(string.Equals(_version,Version10,StringComparison.Ordinal)||string.Equals(_version,Version11,StringComparison.Ordinal))
                    {
                        PurchaseMergeEstimate merge=FindPurchaseMergeEstimate(o,x,purchaseMergeEstimates);
                        PurchasePowerEstimate power=FindPurchasePowerEstimate(o,x,purchasePowerEstimates);
                        double immediateAdjustment=merge.ImmediateMerge&&!merge.LegacyImmediateMerge?_settings.MergePotentialValue:0;
                        double chainAdjustment=merge.ChainMerge
                            ?(merge.LegacyChainMerge?_settings.ChainMergeValue-_settings.MergeChainPotentialValue:_settings.ChainMergeValue)
                            :0;
                        double progress=merge.ImmediateMerge?0:
                            (merge.OneStarCount==1?_settings.MergeProgressOneStarValue:0)+
                            (merge.TwoStarCount==1?_settings.MergeProgressOneStarTwoStarValue:0)+
                            (merge.TwoStarCount>=2?_settings.MergeProgressTwoStarTwoCopiesValue:0);
                        int boardDelta=merge.ImmediateMerge?merge.BoardDelta:x.Placement.Kind==CharacterLocationKind.Board?1:0;
                        int handDelta=merge.ImmediateMerge?merge.HandDelta:x.Placement.Kind==CharacterLocationKind.Hand?1:0;
                        if(merge.ImmediateMerge&&x.Placement.Kind==CharacterLocationKind.Board&&IsEmptyBoardSlot(o,x.Placement.SlotIndex)&&merge.FinalSurvivorLocation==CharacterLocationKind.Hand)
                        {boardDelta++;handDelta--;}
                        double boardSlotAdjustment=boardDelta>0?-boardDelta*_settings.BoardSlotReleaseValue:
                            boardDelta<0?Math.Min(Math.Max(0,BoardCount(o)-o.Player.Level),-boardDelta)*_settings.BoardSlotReleaseValue:0;
                        double slotAdjustment=boardSlotAdjustment+Math.Max(0,-handDelta)*_settings.HandSlotReleaseValue;
                        double handPenalty=0;
                        if(x.Placement.Kind==CharacterLocationKind.Hand)
                        {
                            int emptyAfter=o.Hand.EmptySlotCount-handDelta;
                            handPenalty=emptyAfter<=0?_settings.HandCapacityPenaltyNoEmpty:
                                emptyAfter==1?_settings.HandCapacityPenaltyOneEmpty:0;
                        }
                        double powerAdjustment=0;string powerSkip=null;
                        if(!power.Available)powerSkip="public Character Stats range unavailable";
                        else if(x.Placement.Kind==CharacterLocationKind.Board&&BoardCount(o)+1>o.Player.Level&&!merge.ImmediateMerge)
                            powerSkip="purchase would leave Board above Level";
                        else if(merge.ImmediateMerge)powerSkip="purchase enables actual Rule merge; resulting Stats are rerolled";
                        else if(x.Placement.Kind==CharacterLocationKind.Board)powerAdjustment=_settings.ImmediateBoardPurchasePowerWeight*(power.Power-_settings.PurchasePowerReference);
                        else if(x.Placement.Kind==CharacterLocationKind.Hand)powerAdjustment=_settings.HandPurchasePowerWeight*(power.Power-_settings.PurchasePowerReference);
                        score+=immediateAdjustment+chainAdjustment+progress+slotAdjustment-handPenalty+powerAdjustment;
                        reason="v9 purchase="+(score-immediateAdjustment-chainAdjustment-progress-slotAdjustment+handPenalty-powerAdjustment).ToString("R",CultureInfo.InvariantCulture)+
                            "; actual merge immediate="+merge.ImmediateMerge+" ★1="+merge.OneStarCount+" ★2="+merge.TwoStarCount+
                            "; legacy immediate bonus correction="+immediateAdjustment.ToString("R",CultureInfo.InvariantCulture)+
                            "; chain adjustment="+chainAdjustment.ToString("R",CultureInfo.InvariantCulture)+
                            "; merge progress="+progress.ToString("R",CultureInfo.InvariantCulture)+
                            "; Board/Hand occupied deltas="+boardDelta+"/"+handDelta+"; slot adjustment="+slotAdjustment.ToString("R",CultureInfo.InvariantCulture)+
                            "; Hand capacity penalty="+handPenalty.ToString("R",CultureInfo.InvariantCulture)+
                            "; public Stats adjustment="+powerAdjustment.ToString("R",CultureInfo.InvariantCulture)+
                            (powerSkip==null?"":"; Stats adjustment skipped: "+powerSkip)+".";
                    }
                    break;
                case BuyItemAction x: score=ItemBuyScore(o,x,out reason);break;
                case RerollShopAction x: score=RerollScore(o,x,out reason);break;
                case BuyExperienceAction: score=ExperienceScore(o,out reason);break;
                case MoveOrSwapBoardAction x: score=BoardMoveScore(o,x.SourceSlot,x.TargetSlot,boardContext,out reason);break;
                case MoveOrSwapHandAction: score=_settings.NoImprovementPenalty;reason="Hand slots have no public strategic ordering; avoid an unproductive rearrangement.";break;
                case MoveBoardToHandAction x: score=CrossMoveScore(o,x.BoardSlot,x.HandSlot,true,boardContext,out reason);break;
                case MoveHandToBoardAction x:
                    score=CrossMoveScore(o,x.BoardSlot,x.HandSlot,false,boardContext,out reason);
                    if(WouldDuplicateAfterHandMove(o,x.BoardSlot,x.HandSlot))
                    {score=_settings.NoImprovementPenalty;reason="Would create a duplicate Definition ID on Board; Dispatcher would reject the move.";}
                    else if(IsEmptyBoardSlot(o,x.BoardSlot)&&BoardCount(o)>=o.Player.Level)
                    {score=_settings.NoImprovementPenalty;reason="Would exceed the public Level deployment capacity; keep the character in Hand.";}
                    else if(IsEmptyBoardSlot(o,x.BoardSlot))
                    {score+=_settings.HandDeploymentPriority;reason+=" Deploys an available Hand character into unused Level capacity.";}
                    break;
                case SellCharacterAction x:
                    score=string.Equals(_version,Version11,StringComparison.Ordinal)
                        ?CharacterSellScoreV11(o,x.InstanceId,boardContext,mergeProgressCounts,out reason)
                        :CharacterSellScore(o,x.InstanceId,boardContext,out reason);
                    break;
                case SellItemAction x: score=o.Hand.EmptySlotCount==0?_settings.ItemSaleSpaceValue:_settings.NoImprovementPenalty;reason="Item sale gives no Gold; only a full Hand makes freeing its slot valuable.";break;
                case UseHandItemAction x: score=HandItemScore(o,x,out reason);break;
                case UseEventItemAction x: score=EventItemScore(o,x,out reason);break;
                case EndEventAction: score=_settings.StartBattleValue;reason="No eligible Event Item target; end the event.";break;
                case StartBattleAction: score=StartBattleScore(o,candidates,decisions,state.LastAction,out reason);break;
                default: score=_settings.NoImprovementPenalty;reason="Unsupported candidate.";break;
            }
            if(double.IsNaN(score)||double.IsInfinity(score)){score=_settings.NoImprovementPenalty;reason+=" Non-finite score replaced by fallback.";}
            return new HeuristicActionEvaluation(a,score,reason);
        }

        private struct PurchasePowerEstimate
        {
            public readonly bool Available,ImmediateMerge; public readonly int MinAttack,MaxAttack,MinDefense,MaxDefense; public readonly double Power;
            public PurchasePowerEstimate(bool available,int minAttack,int maxAttack,int minDefense,int maxDefense,double power,bool immediateMerge)
            {Available=available;MinAttack=minAttack;MaxAttack=maxAttack;MinDefense=minDefense;MaxDefense=maxDefense;Power=power;ImmediateMerge=immediateMerge;}
        }

        private static PurchasePowerEstimate[] BuildPurchasePowerEstimates(GameObservation observation,bool actualMergeRules=false,PurchaseMergeEstimate[] mergeEstimates=null)
        {
            if(observation.Shop==null)return Array.Empty<PurchasePowerEstimate>();
            int maxIndex=observation.Shop.Offers.Count==0?-1:observation.Shop.Offers.Max(x=>x.OfferIndex);
            if(maxIndex<0)return Array.Empty<PurchasePowerEstimate>();
            var estimates=new PurchasePowerEstimate[maxIndex+1];
            foreach(ShopOfferObservation offer in observation.Shop.Offers)
            {
                CharacterStatRangeObservation range=offer.CharacterStats;
                if(offer.Kind!=ShopOfferKind.Character||offer.OfferIndex<0||offer.OfferIndex>=estimates.Length)continue;
                bool immediateMerge=actualMergeRules
                    ?offer.OfferIndex>=0&&mergeEstimates!=null&&offer.OfferIndex<mergeEstimates.Length&&mergeEstimates[offer.OfferIndex].ImmediateMerge
                    :offer.CharacterCost.HasValue&&observation.Characters.Count(c=>
                        c.DefinitionId==offer.DefinitionId&&c.RunCost==offer.CharacterCost.Value&&c.Star==1)>=2;
                if(range==null||!range.IsAvailable)
                {estimates[offer.OfferIndex]=new PurchasePowerEstimate(false,0,0,0,0,0,immediateMerge);continue;}
                double atk=((double)range.MinimumAttack+range.MaximumAttack)/2.0;
                double def=((double)range.MinimumDefense+range.MaximumDefense)/2.0;
                estimates[offer.OfferIndex]=new PurchasePowerEstimate(true,range.MinimumAttack,range.MaximumAttack,
                    range.MinimumDefense,range.MaximumDefense,atk+0.6*def,immediateMerge);
            }
            return estimates;
        }

        private static PurchasePowerEstimate FindPurchasePowerEstimate(GameObservation observation,BuyCharacterAction action,PurchasePowerEstimate[] estimates)
        {
            if(observation.Shop==null||estimates==null)return default(PurchasePowerEstimate);
            ShopOfferObservation offer=Offer(observation,action.OfferId);
            if(offer==null||offer.OfferIndex<0||offer.OfferIndex>=estimates.Length)return default(PurchasePowerEstimate);
            return estimates[offer.OfferIndex];
        }

        private struct MergeCandidateObservation
        {
            public readonly int Star,RunCost,Slot;
            public readonly CharacterLocationKind Location;
            public MergeCandidateObservation(CharacterObservation character,CharacterLocationKind location,int slot)
            {Star=character.Star;RunCost=character.RunCost;Location=location;Slot=slot;}
        }

        private struct PurchaseMergeEstimate
        {
            public readonly int OneStarCount,TwoStarCount,BoardDelta,HandDelta;
            public readonly bool ImmediateMerge,ChainMerge,LegacyImmediateMerge,LegacyChainMerge;
            public readonly CharacterLocationKind FinalSurvivorLocation;
            public PurchaseMergeEstimate(int one,int two,int boardDelta,int handDelta,bool immediate,bool chain,
                bool legacyImmediate,bool legacyChain,CharacterLocationKind finalLocation)
            {OneStarCount=one;TwoStarCount=two;BoardDelta=boardDelta;HandDelta=handDelta;ImmediateMerge=immediate;ChainMerge=chain;
             LegacyImmediateMerge=legacyImmediate;LegacyChainMerge=legacyChain;FinalSurvivorLocation=finalLocation;}
        }

        private struct MergeProgressCounts
        {
            public readonly int OneStarCount,TwoStarCount;
            public MergeProgressCounts(int oneStarCount,int twoStarCount)
            {OneStarCount=oneStarCount;TwoStarCount=twoStarCount;}
        }

        private static PurchaseMergeEstimate[] BuildPurchaseMergeEstimates(GameObservation observation,bool includeProgressCounts,
            out Dictionary<string,MergeProgressCounts> progressCountsByDefinition)
        {
            progressCountsByDefinition=includeProgressCounts
                ?new Dictionary<string,MergeProgressCounts>(StringComparer.Ordinal):null;
            if(!includeProgressCounts)
            {
                if(observation.Shop==null){return Array.Empty<PurchaseMergeEstimate>();}
                int existingMaxIndex=observation.Shop.Offers.Count==0?-1:observation.Shop.Offers.Max(x=>x.OfferIndex);
                if(existingMaxIndex<0)return Array.Empty<PurchaseMergeEstimate>();
            }
            var candidatesByDefinition=new Dictionary<string,List<MergeCandidateObservation>>(StringComparer.Ordinal);
            foreach(BoardSlotObservation slot in observation.Board.Slots)
                if(slot.Character!=null)AddMergeObservation(candidatesByDefinition,slot.Character,CharacterLocationKind.Board,slot.SlotIndex);
            foreach(HandSlotObservation slot in observation.Hand.Slots)
                if(slot.Character!=null)AddMergeObservation(candidatesByDefinition,slot.Character,CharacterLocationKind.Hand,slot.SlotIndex);
            if(includeProgressCounts)
                foreach(KeyValuePair<string,List<MergeCandidateObservation>> pair in candidatesByDefinition)
                    progressCountsByDefinition.Add(pair.Key,new MergeProgressCounts(pair.Value.Count(x=>x.Star==1),pair.Value.Count(x=>x.Star==2)));
            if(observation.Shop==null)return Array.Empty<PurchaseMergeEstimate>();
            int maxIndex=observation.Shop.Offers.Count==0?-1:observation.Shop.Offers.Max(x=>x.OfferIndex);
            if(maxIndex<0)return Array.Empty<PurchaseMergeEstimate>();
            var estimates=new PurchaseMergeEstimate[maxIndex+1];
            foreach(ShopOfferObservation offer in observation.Shop.Offers)
            {
                if(offer.Kind!=ShopOfferKind.Character||offer.OfferIndex<0||offer.OfferIndex>=estimates.Length)continue;
                if(!candidatesByDefinition.TryGetValue(offer.DefinitionId,out List<MergeCandidateObservation> candidates))
                {estimates[offer.OfferIndex]=new PurchaseMergeEstimate(0,0,0,0,false,false,false,false,CharacterLocationKind.Board);continue;}
                int one=candidates.Count(x=>x.Star==1),two=candidates.Count(x=>x.Star==2);
                bool legacyImmediate=offer.CharacterCost.HasValue&&candidates.Count(x=>x.Star==1&&x.RunCost==offer.CharacterCost.Value)>=2;
                bool legacyChain=legacyImmediate&&offer.CharacterCost.HasValue&&candidates.Count(x=>x.Star==2&&x.RunCost==offer.CharacterCost.Value)>=2;
                if(one<2)
                {estimates[offer.OfferIndex]=new PurchaseMergeEstimate(one,two,0,0,false,false,legacyImmediate,legacyChain,CharacterLocationKind.Board);continue;}
                MergeCandidateObservation first=default(MergeCandidateObservation),second=default(MergeCandidateObservation);
                bool gotFirst=false,gotSecond=false;
                foreach(MergeCandidateObservation candidate in candidates)
                {
                    if(candidate.Star!=1)continue;
                    if(!gotFirst){first=candidate;gotFirst=true;}
                    else{second=candidate;gotSecond=true;break;}
                }
                if(!gotFirst||!gotSecond)
                {estimates[offer.OfferIndex]=new PurchaseMergeEstimate(one,two,0,0,false,false,legacyImmediate,legacyChain,CharacterLocationKind.Board);continue;}
                int boardDelta=second.Location==CharacterLocationKind.Board?-1:0;
                int handDelta=second.Location==CharacterLocationKind.Hand?-1:0;
                var stageTwo=new List<MergeCandidateObservation>(two+1);
                foreach(MergeCandidateObservation candidate in candidates)
                {
                    if(candidate.Location==first.Location&&candidate.Slot==first.Slot)stageTwo.Add(first);
                    else if(candidate.Location==second.Location&&candidate.Slot==second.Slot)continue;
                    else if(candidate.Star==2)stageTwo.Add(candidate);
                }
                bool chain=stageTwo.Count>=3;
                if(chain)for(int i=1;i<3;i++)
                {if(stageTwo[i].Location==CharacterLocationKind.Board)boardDelta--;else handDelta--;}
                CharacterLocationKind finalLocation=stageTwo[0].Location;
                estimates[offer.OfferIndex]=new PurchaseMergeEstimate(one,two,boardDelta,handDelta,true,chain,
                    legacyImmediate,legacyChain,finalLocation);
            }
            return estimates;
        }

        private static void AddMergeObservation(Dictionary<string,List<MergeCandidateObservation>> byDefinition,
            CharacterObservation character,CharacterLocationKind location,int slot)
        {
            if(!byDefinition.TryGetValue(character.DefinitionId,out List<MergeCandidateObservation> list))
                byDefinition.Add(character.DefinitionId,list=new List<MergeCandidateObservation>(3));
            list.Add(new MergeCandidateObservation(character,location,slot));
        }

        private static PurchaseMergeEstimate FindPurchaseMergeEstimate(GameObservation observation,BuyCharacterAction action,PurchaseMergeEstimate[] estimates)
        {
            if(observation.Shop==null||estimates==null)return default(PurchaseMergeEstimate);
            ShopOfferObservation offer=Offer(observation,action.OfferId);
            if(offer==null||offer.OfferIndex<0||offer.OfferIndex>=estimates.Length)return default(PurchaseMergeEstimate);
            return estimates[offer.OfferIndex];
        }


        private double ChoiceScore(GameObservation o,StageChoice c,out string reason)
        {
            switch(c){
                case StageChoice.SelectA:reason="Standard public reward path.";return _settings.SelectAValue;
                case StageChoice.SelectB:reason="Values the Item-shop opportunity without predicting its offers.";return _settings.SelectBValue;
                case StageChoice.SelectC:reason="Favors the larger public reward; no hidden encounter is predicted.";return _settings.SelectCValue;
                case StageChoice.SelectD:int heal=o.Player.HasHealth?Math.Min(_settings.SelectDHealAmount,Math.Max(0,o.Player.MaximumHP-o.Player.HP)):0;reason=heal>0?"Values effective visible HP recovery.":"No effective visible HP recovery.";return _settings.SelectDBaseValue+heal*_settings.SelectDHealValuePerHp;
                default:reason="Unknown choice.";return _settings.NoImprovementPenalty;
            }
        }
        private double CharacterBuyScore(GameObservation o,BuyCharacterAction a,out string reason)
        {
            ShopOfferObservation offer=Offer(o,a.OfferId);if(offer==null){reason="Offer is not visible.";return _settings.NoImprovementPenalty;}
            // Existing BoardShopTransactionRules merge on DefinitionId + RunCost + Star.
            // A missing public RunCost cannot safely support a merge estimate.
            var same=offer.CharacterCost.HasValue
                ?o.Characters.Where(c=>c.DefinitionId==offer.DefinitionId&&c.RunCost==offer.CharacterCost.Value).ToArray()
                :Array.Empty<CharacterObservation>();
            int one=same.Count(c=>c.Star==1),two=same.Count(c=>c.Star==2);
            double value=_settings.CharacterPurchaseValue;
            if(one>=2)value+=_settings.MergePotentialValue;
            if(one>=2&&two>=2)value+=_settings.MergeChainPotentialValue;
            int excess=Math.Max(0,o.Player.Gold-_settings.GoldReserve);
            value-=offer.DisplayPrice*_settings.GoldSpendPenaltyPerGold;
            value-=Math.Max(0,offer.DisplayPrice-excess)*_settings.GoldSpendPenaltyPerGold;
            value+=Math.Max(0,o.Player.Gold-offer.DisplayPrice-_settings.GoldReserve)*_settings.ExcessGoldValuePerGold;
            reason=one>=2?"Visible copies suggest a possible merge; actual eligibility remains with Dispatcher.":
                "Visible offer is affordable; shop Character stats and tags are not exposed in GameObservation.";
            return value;
        }
        private double PurchasePlacementBonus(GameObservation o,BuyCharacterAction a)
        {
            if(a.Placement.Kind!=CharacterLocationKind.Board)return 0;
            int count=BoardCount(o);
            ShopOfferObservation offer=Offer(o,a.OfferId);
            bool merge=offer!=null&&offer.CharacterCost.HasValue&&o.Characters.Count(c=>
                c.DefinitionId==offer.DefinitionId&&c.RunCost==offer.CharacterCost.Value&&c.Star==1)>=2;
            if(count==0)return _settings.EmptyBoardPurchasePriority;
            return count<o.Player.Level?_settings.BoardFillValue:(merge?_settings.MergePotentialValue:0);
        }
        private double ItemBuyScore(GameObservation o,BuyItemAction a,out string reason)
        {
            ShopOfferObservation offer=Offer(o,a.OfferId);if(offer==null){reason="Offer is not visible.";return _settings.NoImprovementPenalty;}
            reason="Uses generic Item-shop value because effect definitions are not exposed before purchase.";
            return _settings.ItemPurchaseValue-offer.DisplayPrice*_settings.GoldSpendPenaltyPerGold+
                Math.Max(0,o.Player.Gold-offer.DisplayPrice-_settings.GoldReserve)*_settings.ExcessGoldValuePerGold;
        }
        private double RerollScore(GameObservation o,RerollShopAction a,out string reason)
        {
            int cost=_settings.ShopRerollGoldCost??0;
            if(o.Player.Gold<_settings.RerollGoldMinimum){reason="Below configured reroll Gold threshold.";return _settings.NoImprovementPenalty;}
            reason="Reroll is affordable; future offers are not predicted.";
            return _settings.RerollValue-cost*_settings.GoldSpendPenaltyPerGold+
                Math.Max(0,o.Player.Gold-cost-_settings.GoldReserve)*_settings.ExcessGoldValuePerGold;
        }
        private double ExperienceScore(GameObservation o,out string reason)
        {
            int? cost=o.Stage.Choice==StageChoice.SelectB?_settings.SelectBExperienceGoldCost:_settings.NormalExperienceGoldCost;
            if(!cost.HasValue||o.Player.Gold<_settings.ExperienceGoldMinimum){reason="XP price is unknown or Gold is below configured threshold.";return _settings.NoImprovementPenalty;}
            int remaining=Math.Max(0,o.Player.RequiredXP-o.Player.XP);
            reason=remaining<=1?"Public XP is near a level-up threshold.":"Values public XP progress against its configured cost.";
            return _settings.ExperienceValue+(remaining<=1?_settings.NearLevelUpBonus:0)-cost.Value*_settings.GoldSpendPenaltyPerGold;
        }
        private double StartBattleScore(GameObservation o,GameAction[] candidates,int decisions,string lastAction,out string reason)
        {
            int count=BoardCount(o);
            if((o.Player.HasHealth&&o.Player.HP<=0)||count>o.Player.Level)
            {reason="StartBattle is not a legal preparation finish for the visible HP or Board/Level state.";return _settings.NoImprovementPenalty;}
            bool canDeploy=count<o.Player.Level&&candidates.Any(x=>x is MoveHandToBoardAction);
            bool canBuyToBoard=count<o.Player.Level&&candidates.OfType<BuyCharacterAction>().Any(x=>x.Placement.Kind==CharacterLocationKind.Board);
            bool canMerge=candidates.OfType<BuyCharacterAction>().Any(x=>HasMergePotential(o,x));
            double score=_settings.StartBattleValue+count*_settings.StartBattleUnitReadinessValue+
                Math.Min(decisions*_settings.PreparationUrgencyPerDecision,15);
            score+=Math.Min(Math.Max(0,o.Player.Gold),_settings.GoldReserve)*0.1;
            if(canDeploy)score-=_settings.StartBattleHandOpportunityPenalty;
            if(canBuyToBoard)score-=_settings.StartBattlePurchaseOpportunityPenalty;
            if(canMerge)score-=_settings.StartBattleMergeOpportunityPenalty;
            if(lastAction.StartsWith("2:",StringComparison.Ordinal))score+=3;
            if(lastAction.StartsWith("4:",StringComparison.Ordinal))score+=5;
            reason=canDeploy?"Delay battle to deploy a visible Hand character while below Level capacity.":
                canBuyToBoard?"Delay battle to fill unused Board capacity with an affordable visible character.":
                canMerge?"Delay battle while a visible purchase can complete a merge.":
                "No valuable public strengthening action remains; finish preparation using current Board readiness and decision urgency.";
            return score;
        }
        private double BoardMoveScore(GameObservation o,int from,int to,BoardEvaluationContext boardContext,out string reason)
        {
            CharacterObservation[] before=boardContext.Board;if(from>=before.Length||to>=before.Length){reason="Board slot is outside the observation.";return _settings.NoImprovementPenalty;}
            double delta=boardContext.QualityAfterSwap(from,to)-boardContext.BaseQuality;
            reason=delta>0?"Relocation improves visible power or synergy placement.":"No observable improvement; avoids arbitrary moves.";
            return delta>0?delta*_settings.BoardImprovementValue:_settings.NoImprovementPenalty;
        }
        private double CrossMoveScore(GameObservation o,int boardSlot,int handSlot,bool b2h,BoardEvaluationContext boardContext,out string reason)
        {
            CharacterObservation[] before=boardContext.Board;if(boardSlot>=before.Length){reason="Slot is outside the observation.";return _settings.NoImprovementPenalty;}
            HandSlotObservation h=o.Hand.Slots.FirstOrDefault(s=>s.SlotIndex==handSlot);
            CharacterObservation moving= h!=null&&h.Kind==HandSlotContentKind.Character?h.Character:null;
            if(!b2h&&moving==null){reason="Hand slot has no character.";return _settings.NoImprovementPenalty;}
            int beforeCount=boardContext.BaseCount;
            int afterCount=beforeCount-(before[boardSlot]!=null?1:0)+(moving!=null?1:0);
            double relief=Math.Max(0,beforeCount-o.Player.Level)-Math.Max(0,afterCount-o.Player.Level);
            double delta=(boardContext.QualityAfterReplacement(boardSlot,moving)-boardContext.BaseQuality)*_settings.BoardImprovementValue+relief*_settings.OverLevelReliefValue;
            reason=delta>0?"Transfer improves visible formation or reduces Board population above Level.":"No positive public placement value.";
            return delta>0?delta:_settings.NoImprovementPenalty;
        }
        private double CharacterSellScore(GameObservation o,string id,BoardEvaluationContext boardContext,out string reason)
        {
            CharacterObservation c=o.Characters.FirstOrDefault(x=>x.InstanceId==id);if(c==null){reason="Character is not publicly owned.";return _settings.NoImprovementPenalty;}
            int copies=c.Star==1?1:c.Star==2?3:c.Star==3?9:1;int proceeds=c.RunCost*copies;
            double lost=Power(c)*(c.LocationKind==ObservationLocationKind.Board?1:.35);
            if(c.LocationKind==ObservationLocationKind.Board)lost+=Math.Max(0,boardContext.BaseQuality-boardContext.QualityAfterReplacement(c.SlotIndex,null));
            double gold=Math.Min(proceeds,Math.Max(0,_settings.GoldReserve-o.Player.Gold))*_settings.SellGoldValuePerGold;
            if(o.Player.Gold<_settings.GoldReserve)gold+=proceeds*_settings.SellGoldValuePerGold*.25;
            reason="Weighs visible sale Gold against the character and synergy value lost.";
            return gold-_settings.CharacterSaleLossPenalty-lost;
        }

        private double CharacterSellScoreV11(GameObservation o,string id,BoardEvaluationContext boardContext,
            Dictionary<string,MergeProgressCounts> progressCountsByDefinition,out string reason)
        {
            CharacterObservation c=o.Characters.FirstOrDefault(x=>x.InstanceId==id);
            if(c==null){reason="Character is not publicly owned.";return _settings.NoImprovementPenalty;}
            int copies=c.Star==1?1:c.Star==2?3:c.Star==3?9:1;
            int proceeds=c.RunCost*copies;
            double combatPowerLoss=c.LocationKind==ObservationLocationKind.Board
                ?Math.Max(0,boardContext.BaseQuality-boardContext.QualityAfterReplacement(c.SlotIndex,null))
                :Power(c)*0.35;
            double mergeProgressLoss=0;
            if(progressCountsByDefinition!=null&&progressCountsByDefinition.TryGetValue(c.DefinitionId,out MergeProgressCounts counts))
            {
                double before=MergeProgressHoldingValue(counts.OneStarCount,counts.TwoStarCount);
                int onesAfter=counts.OneStarCount-(c.Star==1?1:0);
                int twosAfter=counts.TwoStarCount-(c.Star==2?1:0);
                double after=MergeProgressHoldingValue(onesAfter,twosAfter);
                mergeProgressLoss=Math.Max(0,before-after);
            }
            double handCapacityRelief=0;
            if(c.LocationKind==ObservationLocationKind.Hand)
                handCapacityRelief=o.Hand.EmptySlotCount==0?_settings.HandCapacityPenaltyNoEmpty:
                    o.Hand.EmptySlotCount==1?_settings.HandCapacityPenaltyOneEmpty:0;
            double gold=Math.Min(proceeds,Math.Max(0,_settings.GoldReserve-o.Player.Gold))*_settings.SellGoldValuePerGold;
            if(o.Player.Gold<_settings.GoldReserve)gold+=proceeds*_settings.SellGoldValuePerGold*0.25;
            reason="v11 sale: Gold="+gold.ToString("R",CultureInfo.InvariantCulture)+
                "; BoardQualityLoss="+(c.LocationKind==ObservationLocationKind.Board?combatPowerLoss.ToString("R",CultureInfo.InvariantCulture):"0")+
                "; HandPowerLoss="+(c.LocationKind==ObservationLocationKind.Hand?combatPowerLoss.ToString("R",CultureInfo.InvariantCulture):"0")+
                "; MergeProgressLoss="+mergeProgressLoss.ToString("R",CultureInfo.InvariantCulture)+
                "; HandCapacityRelief="+handCapacityRelief.ToString("R",CultureInfo.InvariantCulture)+
                "; SalePenalty="+_settings.CharacterSaleLossPenalty.ToString("R",CultureInfo.InvariantCulture)+".";
            return gold-_settings.CharacterSaleLossPenalty-combatPowerLoss-mergeProgressLoss+handCapacityRelief;
        }

        private double MergeProgressHoldingValue(int oneStarCount,int twoStarCount)
        {
            double oneStar=oneStarCount>=2?_settings.MergeProgressOneStarValue:0;
            double twoStar=twoStarCount<=0?0:twoStarCount==1?
                _settings.MergeProgressOneStarTwoStarValue:_settings.MergeProgressTwoStarTwoCopiesValue;
            return oneStar+twoStar;
        }
        private double HandItemScore(GameObservation o,UseHandItemAction a,out string reason)
        {var h=o.Hand.Slots.FirstOrDefault(s=>s.SlotIndex==a.HandSlot);double n=h?.Item==null?0:ItemUtility(o,h.Item,a.Target);reason=n>0?"Uses an eligible visible Item effect.":"No positive public effect.";return n>0?n:_settings.NoImprovementPenalty;}
        private double EventItemScore(GameObservation o,UseEventItemAction a,out string reason)
        {double n=o.EventItem?.Item==null?0:ItemUtility(o,o.EventItem.Item,a.Target);reason=n>0?"Uses the Event Item on a public eligible target.":"No positive public Event Item effect.";return n>0?n:_settings.NoImprovementPenalty;}
        private double ItemUtility(GameObservation o,ItemObservation item,GameActionTarget target)
        {
            double score=_settings.ItemUseBaseValue;CharacterObservation ally=null;EnemyObservation enemy=null;
            if(target.Kind==ItemUseTargetKind.BoardCharacter)ally=o.Board.Slots.FirstOrDefault(s=>s.SlotIndex==target.SlotIndex)?.Character;
            else if(target.Kind==ItemUseTargetKind.HandCharacter)ally=o.Hand.Slots.FirstOrDefault(s=>s.SlotIndex==target.SlotIndex)?.Character;
            else if(target.Kind==ItemUseTargetKind.EnemyCharacter)enemy=o.Enemies.FirstOrDefault(e=>e.SlotIndex==target.SlotIndex);
            foreach(var effect in item.Effects)switch(effect.Kind){
                case ItemEffectKind.Attack:case ItemEffectKind.Defense:score+=Math.Max(0,effect.Value)*_settings.ItemEffectValuePerPoint*(ally==null?Math.Max(1,o.Board.Slots.Count(s=>s.Character!=null)):1);if(effect.Duration==ItemEffectDuration.ThisBattle&&o.Enemies.Count==0)score-=_settings.ItemUseBaseValue;break;
                case ItemEffectKind.Star:if(ally!=null&&effect.Value>=1&&ally.Star<effect.Value)score+=_settings.ItemStarUpgradeValue;break;
                case ItemEffectKind.Damage:score+=Math.Max(0,effect.Value)*_settings.ItemEffectValuePerPoint*(enemy!=null?1:Math.Max(1,o.Enemies.Count));if(item.Target==ItemEffectTarget.Player)score-=Math.Abs(effect.Value)*_settings.ItemEffectValuePerPoint;break;
                case ItemEffectKind.Block:case ItemEffectKind.Gap:score+=Math.Max(0,effect.Value)*_settings.ItemEffectValuePerPoint;break;
            }
            return score;
        }
        private static bool IsBoardEvaluationAction(GameAction action) =>
            action is MoveOrSwapBoardAction || action is MoveBoardToHandAction ||
            action is MoveHandToBoardAction || action is SellCharacterAction;

        private sealed class BoardEvaluationContext
        {
            private readonly HeuristicStrategy _owner;
            private readonly GameObservation _observation;
            private CharacterObservation[] _board;
            private bool _initialized;
            private double _baseQuality;
            private int _baseCount;

            public CharacterObservation[] Board { get { EnsureInitialized(); return _board; } }
            public double BaseQuality { get { EnsureInitialized(); return _baseQuality; } }
            public int BaseCount { get { EnsureInitialized(); return _baseCount; } }

            public BoardEvaluationContext(HeuristicStrategy owner,GameObservation observation)
            { _owner=owner;_observation=observation; }

            public double QualityAfterReplacement(int slot,CharacterObservation replacement)
            {
                EnsureInitialized();CharacterObservation original=_board[slot];_board[slot]=replacement;
                try{return MeasureVirtualQuality();}
                finally{_board[slot]=original;}
            }

            public double QualityAfterSwap(int first,int second)
            {
                EnsureInitialized();CharacterObservation a=_board[first],b=_board[second];
                _board[first]=b;_board[second]=a;
                try{return MeasureVirtualQuality();}
                finally{_board[first]=a;_board[second]=b;}
            }

            private void EnsureInitialized()
            {
                if(_initialized)return;
                using(_owner._profiler==null?default(ProjectNST.GameCore.Sessions.SimulationProfileCollector.Scope):_owner._profiler.Measure("Strategy.BoardEvaluation.Snapshot"))
                {
                    _board=BoardCharacters(_observation.Board);
                    _baseCount=_board.Count(c=>c!=null);
                }
                using(_owner._profiler==null?default(ProjectNST.GameCore.Sessions.SimulationProfileCollector.Scope):_owner._profiler.Measure("Strategy.BoardEvaluation.BaseQuality"))
                    _baseQuality=_owner.Quality(_board,_observation.Board.Columns,_observation.Synergy);
                _initialized=true;
            }

            private double MeasureVirtualQuality()
            {
                using(_owner._profiler==null?default(ProjectNST.GameCore.Sessions.SimulationProfileCollector.Scope):_owner._profiler.Measure("Strategy.BoardEvaluation.VirtualQuality"))
                    return _owner.Quality(_board,_observation.Board.Columns,_observation.Synergy);
            }
        }

        private double Quality(CharacterObservation[] board,int cols,SynergyObservation synergy)
        {
            double score=board.Where(c=>c!=null).Sum(Power);
            var attrs=new Dictionary<string,List<int>>(StringComparer.Ordinal);var units=new Dictionary<string,List<int>>(StringComparer.Ordinal);
            for(int i=0;i<board.Length;i++)if(board[i]!=null){foreach(string t in board[i].Attributes.Distinct(StringComparer.Ordinal)){if(!attrs.TryGetValue(t,out var l))attrs[t]=l=new List<int>();l.Add(i);}foreach(string t in board[i].Units.Distinct(StringComparer.Ordinal)){if(!units.TryGetValue(t,out var l))units[t]=l=new List<int>();l.Add(i);}}
            score+=attrs.Values.Count(v=>v.Count>=2)*_settings.AttributeSharedValue;
            foreach(var p in units.OrderBy(p=>p.Key,StringComparer.Ordinal))score+=Math.Max(0,MaxComponent(p.Value,board,p.Key,cols)-1)*_settings.UnitPartialValue;
            if(synergy!=null&&synergy.IsAvailable)foreach(var u in synergy.Units){var slots=new List<int>();foreach(string id in u.ParticipantInstanceIds){int ix=Array.FindIndex(board,c=>c!=null&&c.InstanceId==id);if(ix<0){slots.Clear();break;}slots.Add(ix);}if(slots.Count>=2&&slots.Count==u.ParticipantInstanceIds.Count&&Connected(slots,cols))score+=_settings.UnitEstablishedValue;}
            return score;
        }
        private static int MaxComponent(List<int> slots,CharacterObservation[] board,string unit,int cols)
        {var left=new HashSet<int>(slots);int max=0;while(left.Count>0){int n=left.Min(),count=0;left.Remove(n);var q=new Queue<int>();q.Enqueue(n);while(q.Count>0){int x=q.Dequeue();count++;foreach(int y in Neighbors(x,cols,board.Length))if(left.Contains(y)&&board[y].Units.Contains(unit)){left.Remove(y);q.Enqueue(y);}}max=Math.Max(max,count);}return max;}
        private static bool Connected(List<int> slots,int cols)
        {var left=new HashSet<int>(slots);var q=new Queue<int>();int first=left.First();left.Remove(first);q.Enqueue(first);while(q.Count>0)foreach(int n in Neighbors(q.Dequeue(),cols,int.MaxValue))if(left.Remove(n))q.Enqueue(n);return left.Count==0;}
        private static IEnumerable<int> Neighbors(int x,int cols,int count){int col=x%cols;if(col>0)yield return x-1;if(col+1<cols&&x+1<count)yield return x+1;if(x-cols>=0)yield return x-cols;if(x+cols<count)yield return x+cols;}
        private static CharacterObservation[] BoardCharacters(BoardObservation b){var a=new CharacterObservation[b.Slots.Count];foreach(var s in b.Slots)if(s.SlotIndex>=0&&s.SlotIndex<a.Length)a[s.SlotIndex]=s.Character;return a;}
        private double Power(CharacterObservation c)=>CharacterPowerEvaluator.Evaluate(c==null?0:c.Attack,
            c==null?0:c.Defense,c==null?0:c.TemporaryAttackBonus,c==null?0:c.TemporaryDefenseBonus,
            c==null?0:c.Star,_settings.AttackValue,_settings.DefenseValue,_settings.CharacterStarValue,
            _settings.IncludeTemporaryCharacterBonuses);
        private static ShopOfferObservation Offer(GameObservation o,string id)=>o.Shop?.Offers.FirstOrDefault(x=>x.OfferId==id);
        private static int BoardCount(GameObservation o)=>o.Board.Slots.Count(s=>s.Character!=null);
        private static bool IsEmptyBoardSlot(GameObservation o,int slot)=>o.Board.Slots.Any(s=>s.SlotIndex==slot&&s.Character==null);
        private static bool WouldDuplicateAfterHandMove(GameObservation o,int boardSlot,int handSlot)
        {
            CharacterObservation moving=o.Hand.Slots.FirstOrDefault(s=>s.SlotIndex==handSlot&&s.Kind==HandSlotContentKind.Character)?.Character;
            return moving!=null&&o.Board.Slots.Any(s=>s.SlotIndex!=boardSlot&&s.Character!=null&&
                string.Equals(s.Character.DefinitionId,moving.DefinitionId,StringComparison.Ordinal));
        }
        private bool HasMergePotential(GameObservation o,BuyCharacterAction a)
        {var offer=Offer(o,a.OfferId);return offer!=null&&offer.CharacterCost.HasValue&&o.Characters.Count(c=>c.DefinitionId==offer.DefinitionId&&c.RunCost==offer.CharacterCost.Value&&c.Star==1)>=2;}
        private static bool IsImmediateBuyThenSell(string previous,GameAction next,GameObservation o)
        {
            if(!(next is SellCharacterAction sell))return false;
            string[] parts=(previous??string.Empty).Split(':');if(parts.Length<4||parts[0]!="2")return false;
            ShopOfferObservation bought=o.Shop?.Offers.FirstOrDefault(x=>x.OfferId==parts[1]&&x.Status==ShopOfferStatus.Purchased);
            CharacterObservation sold=o.Characters.FirstOrDefault(x=>x.InstanceId==sell.InstanceId);
            return bought!=null&&sold!=null&&sold.DefinitionId==bought.DefinitionId;
        }
        private static bool IsImmediateSellThenBuy(string previous,GameAction next)
            => previous!=null&&previous.StartsWith("10:",StringComparison.Ordinal)&&next is BuyCharacterAction;
        private static int Increment(int n)=>n==int.MaxValue?n:n+1;

        private StrategyState NewState(int decisions,string action,int encounterIndex,ObservationPhase phase)=>new StrategyState(StableId,_version,StateSchemaVersion,
            Prefix+decisions.ToString(CultureInfo.InvariantCulture)+"|"+encounterIndex.ToString(CultureInfo.InvariantCulture)+"|"+((int)phase).ToString(CultureInfo.InvariantCulture)+"|"+Convert.ToBase64String(Encoding.UTF8.GetBytes(action??string.Empty)));
        private static Payload ReadState(StrategyState state)
        {
            if(state==null||state.SchemaVersion!=StateSchemaVersion||!state.Payload.StartsWith(Prefix,StringComparison.Ordinal))return new Payload(0,string.Empty,false,-1,ObservationPhase.AwaitingStageChoice,false);
            try
            {
                var p=state.Payload.Substring(Prefix.Length).Split('|');
                if(p.Length!=4||!int.TryParse(p[0],NumberStyles.None,CultureInfo.InvariantCulture,out int n)||n<0||
                    !int.TryParse(p[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out int encounter)||encounter<0||
                    !int.TryParse(p[2],NumberStyles.Integer,CultureInfo.InvariantCulture,out int phase)||!Enum.IsDefined(typeof(ObservationPhase),phase))
                    return new Payload(0,string.Empty,false,-1,ObservationPhase.AwaitingStageChoice,false);
                return new Payload(n,Encoding.UTF8.GetString(Convert.FromBase64String(p[3])),true,encounter,(ObservationPhase)phase,true);
            }
            catch(FormatException){return new Payload(0,string.Empty,false,-1,ObservationPhase.AwaitingStageChoice,false);}
        }
        private static string Signature(GameAction a){switch(a){case SelectStageChoiceAction x:return "1:"+(int)x.Choice;case BuyCharacterAction x:return "2:"+x.OfferId+":"+(int)x.Placement.Kind+":"+x.Placement.SlotIndex;case BuyItemAction x:return "3:"+x.OfferId+":"+x.HandSlot;case RerollShopAction x:return "4:"+(int)x.ShopKind;case BuyExperienceAction:return "5";case MoveOrSwapBoardAction x:return "6:"+x.SourceSlot+":"+x.TargetSlot;case MoveOrSwapHandAction x:return "7:"+x.SourceSlot+":"+x.TargetSlot;case MoveBoardToHandAction x:return "8:"+x.BoardSlot+":"+x.HandSlot;case MoveHandToBoardAction x:return "9:"+x.HandSlot+":"+x.BoardSlot;case SellCharacterAction x:return "10:"+x.InstanceId;case SellItemAction x:return "11:"+x.HandSlot;case UseHandItemAction x:return "12:"+x.HandSlot+":"+Target(x.Target);case UseEventItemAction x:return "13:"+Target(x.Target);case EndEventAction:return "14";case StartBattleAction:return "15";default:return ((int)a.Kind).ToString(CultureInfo.InvariantCulture);}}
        private static string Target(GameActionTarget t)=>((int)t.Kind)+":"+t.SlotIndex+":"+t.EnemyCount+":"+(t.IsPresent?1:0);
        private static bool IsReverse(string previous,GameAction next){string[] p=(previous??string.Empty).Split(':');if(p.Length<3||!int.TryParse(p[0],out int kind))return false;if(next is MoveOrSwapBoardAction b&&kind==6)return b.SourceSlot==Parse(p[2])&&b.TargetSlot==Parse(p[1]);if(next is MoveOrSwapHandAction h&&kind==7)return h.SourceSlot==Parse(p[2])&&h.TargetSlot==Parse(p[1]);if(next is MoveBoardToHandAction b2h&&kind==9)return b2h.BoardSlot==Parse(p[2])&&b2h.HandSlot==Parse(p[1]);if(next is MoveHandToBoardAction h2b&&kind==8)return h2b.HandSlot==Parse(p[2])&&h2b.BoardSlot==Parse(p[1]);return false;}
        private static int Parse(string s)=>int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?n:-1;
        private sealed class Payload
        {
            public int Decisions{get;} public string LastAction{get;} public bool HasEncounter{get;} public int EncounterIndex{get;}
            public ObservationPhase Phase{get;} public bool HasIdentity{get;}
            public Payload(int n,string a,bool hasEncounter,int encounter,ObservationPhase phase,bool hasIdentity)
            {Decisions=n;LastAction=a;HasEncounter=hasEncounter;EncounterIndex=encounter;Phase=phase;HasIdentity=hasIdentity;}
        }
    }

    public static class CharacterPowerEvaluator
    {
        public static double Evaluate(int attack, int defense, int temporaryAttackBonus,
            int temporaryDefenseBonus, int star, double attackWeight, double defenseWeight,
            double starWeight, bool includeTemporaryBonuses)
        {
            double effectiveAttack = attack;
            double effectiveDefense = defense;
            if (includeTemporaryBonuses)
            {
                effectiveAttack += temporaryAttackBonus;
                effectiveDefense += temporaryDefenseBonus;
            }

            return effectiveAttack * attackWeight + effectiveDefense * defenseWeight +
                Math.Max(0, star - 1) * starWeight;
        }

        public static double Evaluate(CharacterObservation character, double attackWeight,
            double defenseWeight, double starWeight, bool includeTemporaryBonuses)
        {
            return character == null ? 0 : Evaluate(character.Attack, character.Defense,
                character.TemporaryAttackBonus, character.TemporaryDefenseBonus, character.Star,
                attackWeight, defenseWeight, starWeight, includeTemporaryBonuses);
        }
    }
}
