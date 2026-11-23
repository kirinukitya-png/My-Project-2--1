using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        public double ItemUseBaseValue { get; }
        public double ItemEffectValuePerPoint { get; }
        public double ItemStarUpgradeValue { get; }
        public double SellGoldValuePerGold { get; }
        public double CharacterSaleLossPenalty { get; }
        public double ItemSaleSpaceValue { get; }
        public double NoImprovementPenalty { get; }

        public HeuristicStrategySettings(int preparationDecisionLimit = 30,
            int? shopRerollGoldCost = null, int? normalExperienceGoldCost = null,
            int? selectBExperienceGoldCost = null, int rerollGoldMinimum = 12,
            int experienceGoldMinimum = 8, int goldReserve = 8, int selectDHealAmount = 10,
            double selectAValue = 14, double selectBValue = 17, double selectCValue = 22,
            double selectDBaseValue = 0, double selectDHealValuePerHp = 3,
            double characterPurchaseValue = 24, double mergePotentialValue = 25,
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
            double noImprovementPenalty = -100)
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
            ItemUseBaseValue=Finite(itemUseBaseValue,nameof(itemUseBaseValue));
            ItemEffectValuePerPoint=NonNegative(itemEffectValuePerPoint,nameof(itemEffectValuePerPoint));
            ItemStarUpgradeValue=NonNegative(itemStarUpgradeValue,nameof(itemStarUpgradeValue));
            SellGoldValuePerGold=NonNegative(sellGoldValuePerGold,nameof(sellGoldValuePerGold));
            CharacterSaleLossPenalty=NonNegative(characterSaleLossPenalty,nameof(characterSaleLossPenalty));
            ItemSaleSpaceValue=NonNegative(itemSaleSpaceValue,nameof(itemSaleSpaceValue));
            NoImprovementPenalty=Finite(noImprovementPenalty,nameof(noImprovementPenalty));
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
        public const string StableId="HeuristicStrategy", StableVersion="1";
        public const int StateSchemaVersion=1;
        private const string Prefix="hs1|";
        private readonly HeuristicStrategySettings _settings;
        private readonly RandomStrategy _candidateSource;
        public string StrategyId=>StableId;
        public string Version=>StableVersion;
        public HeuristicStrategySettings Settings=>_settings;

        public HeuristicStrategy(HeuristicStrategySettings settings=null)
        {
            _settings=settings??new HeuristicStrategySettings();
            _candidateSource=new RandomStrategy(new RandomStrategySettings(_settings.PreparationDecisionLimit,
                _settings.ShopRerollGoldCost,_settings.NormalExperienceGoldCost,_settings.SelectBExperienceGoldCost));
        }

        public StrategyDecision ChooseAction(GameObservation observation,StrategyContext context)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            if(context==null)throw new ArgumentNullException(nameof(context));
            if(!context.IsFor(this))throw new ArgumentException("Context belongs to another Strategy.",nameof(context));
            Payload state=ReadState(context.State);
            int decisions=observation.Stage.Phase==ObservationPhase.Preparation?Increment(state.Decisions):0;
            if(!observation.IsStrategyActionAllowed)
                return context.NoAction(NewState(0,string.Empty),"No player decision is required in this phase.");
            var evaluations=GetEvaluations(observation,context.NextActionId).ToList();
            evaluations.RemoveAll(x=>Signature(x.Action)==state.LastAction || IsReverse(state.LastAction,x.Action));
            if(evaluations.Count==0)
                return context.NoAction(NewState(decisions,string.Empty),"No observable improvement or legal candidate is available.");
            bool limitReached=observation.Stage.Phase==ObservationPhase.Preparation&&decisions>=_settings.PreparationDecisionLimit;
            bool legalForcedBattle=limitReached&&evaluations.Any(x=>x.Action is StartBattleAction);
            if(observation.Stage.Phase==ObservationPhase.Preparation&&evaluations[0].Score<=0&&!legalForcedBattle)
                return context.NoAction(NewState(decisions,string.Empty),"No positive-value preparation action is visible; Runner may detect the stall.");
            HeuristicActionEvaluation chosen=evaluations[0];
            if(limitReached)
            {
                var start=evaluations.FirstOrDefault(x=>x.Action is StartBattleAction);
                if(start!=null)chosen=new HeuristicActionEvaluation(start.Action,_settings.ForcedStartBattleValue,
                    "Preparation decision limit reached; prioritize legal StartBattle.");
            }
            return context.Decide(chosen.Action,NewState(decisions,Signature(chosen.Action)),
                chosen.Reason+" Score="+chosen.Score.ToString("R",CultureInfo.InvariantCulture)+".");
        }

        /// <summary>Candidate list is exactly the existing RandomStrategy public candidate generator; only ranking differs.</summary>
        public IReadOnlyList<HeuristicActionEvaluation> GetEvaluations(GameObservation observation,ActionId actionId)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            if(!observation.IsStrategyActionAllowed)return Array.AsReadOnly(Array.Empty<HeuristicActionEvaluation>());
            return Array.AsReadOnly(_candidateSource.GetCandidates(observation,actionId)
                .Select(a=>Evaluate(observation,a)).OrderByDescending(x=>x.Score)
                .ThenBy(x=>Signature(x.Action),StringComparer.Ordinal).ToArray());
        }

        private HeuristicActionEvaluation Evaluate(GameObservation o,GameAction a)
        {
            double score;string reason;
            switch(a)
            {
                case SelectStageChoiceAction x: score=ChoiceScore(o,x.Choice,out reason);break;
                case BuyCharacterAction x: score=CharacterBuyScore(o,x,out reason);break;
                case BuyItemAction x: score=ItemBuyScore(o,x,out reason);break;
                case RerollShopAction x: score=RerollScore(o,x,out reason);break;
                case BuyExperienceAction: score=ExperienceScore(o,out reason);break;
                case MoveOrSwapBoardAction x: score=BoardMoveScore(o,x.SourceSlot,x.TargetSlot,out reason);break;
                case MoveOrSwapHandAction: score=_settings.NoImprovementPenalty;reason="Hand slots have no public strategic ordering; avoid an unproductive rearrangement.";break;
                case MoveBoardToHandAction x: score=CrossMoveScore(o,x.BoardSlot,x.HandSlot,true,out reason);break;
                case MoveHandToBoardAction x: score=CrossMoveScore(o,x.BoardSlot,x.HandSlot,false,out reason);break;
                case SellCharacterAction x: score=CharacterSellScore(o,x.InstanceId,out reason);break;
                case SellItemAction x: score=o.Hand.EmptySlotCount==0?_settings.ItemSaleSpaceValue:_settings.NoImprovementPenalty;reason="Item sale gives no Gold; only a full Hand makes freeing its slot valuable.";break;
                case UseHandItemAction x: score=HandItemScore(o,x,out reason);break;
                case UseEventItemAction x: score=EventItemScore(o,x,out reason);break;
                case EndEventAction: score=_settings.StartBattleValue;reason="No eligible Event Item target; end the event.";break;
                case StartBattleAction: score=StartBattleScore(o);reason="Finish preparation and start the available battle.";break;
                default: score=_settings.NoImprovementPenalty;reason="Unsupported candidate.";break;
            }
            if(double.IsNaN(score)||double.IsInfinity(score)){score=_settings.NoImprovementPenalty;reason+=" Non-finite score replaced by fallback.";}
            return new HeuristicActionEvaluation(a,score,reason);
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
        private double StartBattleScore(GameObservation o)
        {
            int count=o.Board.Slots.Count(s=>s.Character!=null);
            if((o.Player.HasHealth&&o.Player.HP<=0)||count>o.Player.Level)return _settings.NoImprovementPenalty;
            return _settings.StartBattleValue;
        }
        private double BoardMoveScore(GameObservation o,int from,int to,out string reason)
        {
            CharacterObservation[] before=BoardCharacters(o.Board);if(from>=before.Length||to>=before.Length){reason="Board slot is outside the observation.";return _settings.NoImprovementPenalty;}
            var after=(CharacterObservation[])before.Clone();CharacterObservation tmp=after[from];after[from]=after[to];after[to]=tmp;
            double delta=Quality(after,o.Board.Columns,o.Synergy)-Quality(before,o.Board.Columns,o.Synergy);
            reason=delta>0?"Relocation improves visible power or synergy placement.":"No observable improvement; avoids arbitrary moves.";
            return delta>0?delta*_settings.BoardImprovementValue:_settings.NoImprovementPenalty;
        }
        private double CrossMoveScore(GameObservation o,int boardSlot,int handSlot,bool b2h,out string reason)
        {
            var before=BoardCharacters(o.Board);if(boardSlot>=before.Length){reason="Slot is outside the observation.";return _settings.NoImprovementPenalty;}
            HandSlotObservation h=o.Hand.Slots.FirstOrDefault(s=>s.SlotIndex==handSlot);
            CharacterObservation moving= h!=null&&h.Kind==HandSlotContentKind.Character?h.Character:null;
            var after=(CharacterObservation[])before.Clone();
            if(b2h)after[boardSlot]=moving;
            else if(moving!=null)after[boardSlot]=moving;
            else {reason="Hand slot has no character.";return _settings.NoImprovementPenalty;}
            int beforeCount=before.Count(c=>c!=null),afterCount=after.Count(c=>c!=null);
            double relief=Math.Max(0,beforeCount-o.Player.Level)-Math.Max(0,afterCount-o.Player.Level);
            double delta=(Quality(after,o.Board.Columns,o.Synergy)-Quality(before,o.Board.Columns,o.Synergy))*_settings.BoardImprovementValue+relief*_settings.OverLevelReliefValue;
            reason=delta>0?"Transfer improves visible formation or reduces Board population above Level.":"No positive public placement value.";
            return delta>0?delta:_settings.NoImprovementPenalty;
        }
        private double CharacterSellScore(GameObservation o,string id,out string reason)
        {
            CharacterObservation c=o.Characters.FirstOrDefault(x=>x.InstanceId==id);if(c==null){reason="Character is not publicly owned.";return _settings.NoImprovementPenalty;}
            int copies=c.Star==1?1:c.Star==2?3:c.Star==3?9:1;int proceeds=c.RunCost*copies;
            double lost=Power(c)*(c.LocationKind==ObservationLocationKind.Board?1:.35);
            if(c.LocationKind==ObservationLocationKind.Board){var after=BoardCharacters(o.Board);after[c.SlotIndex]=null;lost+=Math.Max(0,Quality(o.Board,o.Synergy)-Quality(after,o.Board.Columns,o.Synergy));}
            double gold=Math.Min(proceeds,Math.Max(0,_settings.GoldReserve-o.Player.Gold))*_settings.SellGoldValuePerGold;
            if(o.Player.Gold<_settings.GoldReserve)gold+=proceeds*_settings.SellGoldValuePerGold*.25;
            reason="Weighs visible sale Gold against the character and synergy value lost.";
            return gold-_settings.CharacterSaleLossPenalty-lost;
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
        private double Quality(BoardObservation b,SynergyObservation s)=>Quality(BoardCharacters(b),b.Columns,s);
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
        private double Power(CharacterObservation c)=>c==null?0:c.Attack*_settings.AttackValue+c.Defense*_settings.DefenseValue+Math.Max(0,c.Star-1)*3;
        private static ShopOfferObservation Offer(GameObservation o,string id)=>o.Shop?.Offers.FirstOrDefault(x=>x.OfferId==id);
        private static int Increment(int n)=>n==int.MaxValue?n:n+1;

        private static StrategyState NewState(int decisions,string action)=>new StrategyState(StableId,StableVersion,StateSchemaVersion,
            Prefix+decisions.ToString(CultureInfo.InvariantCulture)+"|"+Convert.ToBase64String(Encoding.UTF8.GetBytes(action??string.Empty)));
        private static Payload ReadState(StrategyState state){if(state==null||state.SchemaVersion!=StateSchemaVersion||!state.Payload.StartsWith(Prefix,StringComparison.Ordinal))return new Payload(0,string.Empty);try{var p=state.Payload.Substring(Prefix.Length).Split('|');if(p.Length!=2||!int.TryParse(p[0],NumberStyles.None,CultureInfo.InvariantCulture,out int n)||n<0)return new Payload(0,string.Empty);return new Payload(n,Encoding.UTF8.GetString(Convert.FromBase64String(p[1])));}catch(FormatException){return new Payload(0,string.Empty);}}
        private static string Signature(GameAction a){switch(a){case SelectStageChoiceAction x:return "1:"+(int)x.Choice;case BuyCharacterAction x:return "2:"+x.OfferId+":"+(int)x.Placement.Kind+":"+x.Placement.SlotIndex;case BuyItemAction x:return "3:"+x.OfferId+":"+x.HandSlot;case RerollShopAction x:return "4:"+(int)x.ShopKind;case BuyExperienceAction:return "5";case MoveOrSwapBoardAction x:return "6:"+x.SourceSlot+":"+x.TargetSlot;case MoveOrSwapHandAction x:return "7:"+x.SourceSlot+":"+x.TargetSlot;case MoveBoardToHandAction x:return "8:"+x.BoardSlot+":"+x.HandSlot;case MoveHandToBoardAction x:return "9:"+x.HandSlot+":"+x.BoardSlot;case SellCharacterAction x:return "10:"+x.InstanceId;case SellItemAction x:return "11:"+x.HandSlot;case UseHandItemAction x:return "12:"+x.HandSlot+":"+Target(x.Target);case UseEventItemAction x:return "13:"+Target(x.Target);case EndEventAction:return "14";case StartBattleAction:return "15";default:return ((int)a.Kind).ToString(CultureInfo.InvariantCulture);}}
        private static string Target(GameActionTarget t)=>((int)t.Kind)+":"+t.SlotIndex+":"+t.EnemyCount+":"+(t.IsPresent?1:0);
        private static bool IsReverse(string previous,GameAction next){string[] p=(previous??string.Empty).Split(':');if(p.Length<3||!int.TryParse(p[0],out int kind))return false;if(next is MoveOrSwapBoardAction b&&kind==6)return b.SourceSlot==Parse(p[2])&&b.TargetSlot==Parse(p[1]);if(next is MoveOrSwapHandAction h&&kind==7)return h.SourceSlot==Parse(p[2])&&h.TargetSlot==Parse(p[1]);if(next is MoveBoardToHandAction b2h&&kind==9)return b2h.BoardSlot==Parse(p[2])&&b2h.HandSlot==Parse(p[1]);if(next is MoveHandToBoardAction h2b&&kind==8)return h2b.HandSlot==Parse(p[2])&&h2b.BoardSlot==Parse(p[1]);return false;}
        private static int Parse(string s)=>int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?n:-1;
        private sealed class Payload{public int Decisions{get;}public string LastAction{get;}public Payload(int n,string a){Decisions=n;LastAction=a;}}
    }
}
