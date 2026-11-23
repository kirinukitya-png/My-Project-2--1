using System;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class HeuristicStrategyTests
    {
        private static readonly string Sid=HeuristicStrategy.StableId, Ver=HeuristicStrategy.StableVersion;
        private static StageProgressConfig Config(int choice=5,int eventChance=0) =>
            new StageProgressConfig(25,5,choice,4,eventChance,0,eventChance,1,0,0,3,6,0,0,10,4,2,true);
        private static EconomyRulesConfiguration EconomyConfig()=>new EconomyRulesConfiguration(4,2,1,
            new[]{1,2,3,4,5,6,7,8,9,10,11},10,1,3,3,1,0);
        private static EconomyState Econ(int gold=20,int level=3,int hp=20)=>new EconomyState(gold,0,level,12,0,1,1,hp,30);
        private static CharacterDefinition Def(string id,string[] attrs=null,string[] units=null)=>new CharacterDefinition(id,id,1,attrs??Array.Empty<string>(),units??Array.Empty<string>());
        private static CharacterInstance Inst(string id,CharacterDefinition d,int star=1,int runCost=1)=>new CharacterInstance(id,d,runCost,star);
        private static StageProgressState Choice(){var c=Config(1);return StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(c),c).NextState;}
        private static StageProgressState Prep(StageProgressConfig cfg=null)
        {cfg=cfg??Config();var s=StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(cfg),cfg).NextState;if(s.Phase==StageProgressPhase.AwaitingStageChoice)s=StageSelectionRules.Select(s,cfg,StageChoice.SelectA).NextState;return s;}
        private static GameObservation Build(StageProgressState stage,EconomyState economy=null,BoardState board=null,
            HandState hand=null,CharacterInstance[] owned=null,CharacterDefinition[] defs=null,ShopState shop=null,
            ItemDefinition[] items=null,EventItemState eventItem=null)
        {
            defs=defs??Array.Empty<CharacterDefinition>();owned=owned??Array.Empty<CharacterInstance>();items=items??Array.Empty<ItemDefinition>();
            StageProgressConfig cfg=Config(stage.Phase==StageProgressPhase.AwaitingStageChoice?1:5);
            if(shop==null&&stage.Phase==StageProgressPhase.Preparation)
                shop=new ShopState(stage.CurrentSelection.ShopMode==StageShopMode.Item?ShopKind.Item:ShopKind.Character,
                    Array.Empty<ShopOffer>(),0,cfg.GetStageNumber(stage.EncounterIndex),cfg.GetRoundNumber(stage.EncounterIndex));
            var result=ObservationBuilder.Build(new ObservationSource(stage,cfg,economy??Econ(),EconomyConfig(),
                board??BoardState.CreateStandard(),hand??HandState.CreateStandard(),new CharacterOwnershipState(owned),
                defs,defs,defs,items,shop,new ItemEffectState(),eventItem,Array.Empty<EnemyDefinition>(),EnemyBoardState.CreateEmpty()));
            Assert.That(result.Succeeded,Is.True,string.Join(";",result.Errors));return result.Observation;
        }
        private static StrategyContext Context(HeuristicStrategy strategy,string id="h/action0",long step=0,StrategyState state=null,StrategyRngState rng=null)=>
            new StrategyContext(strategy.StrategyId,strategy.Version,0,step,new ActionId(id),state??StrategyState.EmptyFor(strategy.StrategyId,strategy.Version),rng);
        private static BoardState Put(BoardState b,int slot,string id){Assert.That(b.TryWithCharacterAt(slot,id,out BoardState next),Is.True);return next;}
        private static HandState Put(HandState h,int slot,HandSlotContent content){Assert.That(h.TryWithSlot(slot,content,out HandState next),Is.True);return next;}
        private static ShopState CharacterShop(string def,int price=1)=>new ShopState(ShopKind.Character,
            new[]{new ShopOffer("offer",ShopOfferKind.Character,def,price,1)},1,1,1);

        [Test] public void MetadataSettingsAndValidationAreStable()
        {
            var s=new HeuristicStrategy();Assert.That(s.StrategyId,Is.EqualTo("HeuristicStrategy"));Assert.That(s.Version,Is.EqualTo("1"));
            Assert.That(s.Settings.PreparationDecisionLimit,Is.EqualTo(30));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(preparationDecisionLimit:0));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(shopRerollGoldCost:-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(selectAValue:double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(attackValue:double.PositiveInfinity));
        }

        [Test] public void ChoiceScoringUsesEffectiveHealingAndPrefersPublicHighRewardWhenHealthy()
        {
            var low=new HeuristicStrategy();var lowObs=Build(Choice(),Econ(hp:20));
            Assert.That(low.GetEvaluations(lowObs,new ActionId("low")).First().Action,Is.TypeOf<SelectStageChoiceAction>());
            Assert.That(((SelectStageChoiceAction)low.GetEvaluations(lowObs,new ActionId("low2")).First().Action).Choice,Is.EqualTo(StageChoice.SelectD));
            var healthy=new HeuristicStrategy();var healthyObs=Build(Choice(),Econ(hp:30));
            Assert.That(((SelectStageChoiceAction)healthy.GetEvaluations(healthyObs,new ActionId("ok")).First().Action).Choice,Is.EqualTo(StageChoice.SelectC));
        }

        [Test] public void ChoiceScoringOnlyRanksChoicesPresentInObservation()
        {
            var cfg=Config(1);var st=StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(cfg),cfg).NextState;
            st=StageSelectionRules.Select(st,cfg,StageChoice.SelectA).NextState;
            var obs=Build(st);var actions=new HeuristicStrategy().GetEvaluations(obs,new ActionId("selected"));
            Assert.That(actions.Any(x=>x.Action is SelectStageChoiceAction),Is.False);
        }

        [Test] public void CharacterPurchasePrioritizesVisiblePossibleMergeWithoutAssumingHiddenStats()
        {
            var d=Def("duplicate");var a=Inst("a",d);var b=Inst("b",d);
            BoardState board=Put(BoardState.CreateStandard(),0,"a");HandState hand=Put(HandState.CreateStandard(),0,HandSlotContent.Character("b"));
            var obs=Build(Prep(),Econ(),board,hand,new[]{a,b},new[]{d},CharacterShop("duplicate"));
            var eval=new HeuristicStrategy().GetEvaluations(obs,new ActionId("buy"));
            Assert.That(eval[0].Action,Is.TypeOf<BuyCharacterAction>());Assert.That(eval[0].Reason,Does.Contain("possible merge"));
            Assert.That(eval[0].Reason,Does.Not.Contain("guaranteed"));
        }

        [Test] public void CharacterPurchaseMergeEstimateRequiresMatchingRunCost()
        {
            var d=Def("same");var a=Inst("a",d,1,2);var b=Inst("b",d,1,2);
            var board=Put(BoardState.CreateStandard(),0,"a");var hand=Put(HandState.CreateStandard(),0,HandSlotContent.Character("b"));
            var obs=Build(Prep(),Econ(),board,hand,new[]{a,b},new[]{d},CharacterShop("same",1));
            var candidate=new HeuristicStrategy().GetEvaluations(obs,new ActionId("cost-mismatch")).First(x=>x.Action is BuyCharacterAction);
            Assert.That(candidate.Reason,Does.Not.Contain("possible merge"));
        }

        [Test] public void CharacterPurchaseIsNotGeneratedWhenUnaffordableOrCapacityMissing()
        {
            var d=Def("costly");var offer=CharacterShop("costly",5);
            var poor=Build(Prep(),Econ(gold:2),shop:offer,defs:new[]{d});
            Assert.That(new HeuristicStrategy().GetEvaluations(poor,new ActionId("poor")).Any(x=>x.Action is BuyCharacterAction),Is.False);
        }

        [Test] public void CharacterPurchaseIsNotGeneratedWhenBoardAndHandHaveNoEmptySlot()
        {
            var defs=Enumerable.Range(0,9).Select(i=>Def("c"+i)).Concat(new[]{Def("new")}).ToArray();
            var owned=defs.Take(9).Select((d,i)=>Inst("i"+i,d)).ToArray();
            var board=BoardState.CreateStandard();for(int i=0;i<9;i++)board=Put(board,i,"i"+i);
            var item=new ItemDefinition("item","Item",0,"",ItemEffectTarget.Player,ItemEffectDuration.Immediate,
                new[]{new ItemEffectDefinition(ItemEffectKind.Block,1)});
            var hand=HandState.CreateStandard();for(int i=0;i<5;i++)hand=Put(hand,i,HandSlotContent.Item("item"));
            var obs=Build(Prep(),Econ(),board,hand,owned,defs,CharacterShop("new"),new[]{item});
            Assert.That(new HeuristicStrategy().GetEvaluations(obs,new ActionId("full")).Any(x=>x.Action is BuyCharacterAction),Is.False);
        }

        [Test] public void AttributeSharingImprovementMovesHandCharacterOntoBoard()
        {
            var a=Def("a",new[]{"Fire"});var b=Def("b",new[]{"Fire"});var ia=Inst("ia",a);var ib=Inst("ib",b);
            var board=Put(BoardState.CreateStandard(),0,"ia");var hand=Put(HandState.CreateStandard(),0,HandSlotContent.Character("ib"));
            var obs=Build(Prep(),Econ(),board,hand,new[]{ia,ib},new[]{a,b});
            var decision=new HeuristicStrategy().ChooseAction(obs,Context(new HeuristicStrategy(),"attr"));
            Assert.That(decision.Action,Is.TypeOf<MoveHandToBoardAction>());
        }

        [Test] public void UnitAdjacencyUsesOrthogonalConnectionsAndNotDiagonalContacts()
        {
            var a=Def("a",units:new[]{"U"});var b=Def("b",units:new[]{"U"});var c=Def("c",units:new[]{"U"});
            var ia=Inst("ia",a);var ib=Inst("ib",b);var ic=Inst("ic",c);
            var board=Put(Put(BoardState.CreateStandard(),0,"ia"),4,"ib");
            var hand=Put(HandState.CreateStandard(),0,HandSlotContent.Character("ic"));
            var obs=Build(Prep(),Econ(),board,hand,new[]{ia,ib,ic},new[]{a,b,c});
            var strategy=new HeuristicStrategy();
            var scores=strategy.GetEvaluations(obs,new ActionId("diag"));
            double adjacent=scores.Where(x=>x.Action is MoveHandToBoardAction m&&m.BoardSlot==1).Single().Score;
            double diagonal=scores.Where(x=>x.Action is MoveHandToBoardAction m&&m.BoardSlot==8).Single().Score;
            Assert.That(adjacent,Is.GreaterThan(diagonal));
            board=Put(BoardState.CreateStandard(),0,"ia");hand=Put(HandState.CreateStandard(),0,HandSlotContent.Character("ib"));
            obs=Build(Prep(),Econ(),board,hand,new[]{ia,ib},new[]{a,b});
            var adjacencyFocused=new HeuristicStrategy(new HeuristicStrategySettings(startBattleValue:5));
            Assert.That(adjacencyFocused.GetEvaluations(obs,new ActionId("orth")).First().Action,Is.TypeOf<MoveHandToBoardAction>());
        }

        [Test] public void BoardOverLevelIsRelievedBeforeBattleAndBattleCandidateIsNotForced()
        {
            var a=Def("a");var b=Def("b");var ia=Inst("ia",a);var ib=Inst("ib",b);
            var board=Put(Put(BoardState.CreateStandard(),0,"ia"),1,"ib");
            var obs=Build(Prep(),Econ(level:1),board,HandState.CreateStandard(),new[]{ia,ib},new[]{a,b});
            var evaluations=new HeuristicStrategy().GetEvaluations(obs,new ActionId("over"));
            Assert.That(evaluations.Any(x=>x.Action is StartBattleAction),Is.False);
            Assert.That(evaluations[0].Action,Is.TypeOf<MoveBoardToHandAction>());
        }

        [Test] public void RerollRequiresExplicitCostAndGoldThreshold()
        {
            var obs=Build(Prep(),Econ(gold:30),shop:new ShopState(ShopKind.Character,Array.Empty<ShopOffer>(),1,1,1));
            var disabled=new HeuristicStrategy();Assert.That(disabled.GetEvaluations(obs,new ActionId("off")).Any(x=>x.Action is RerollShopAction),Is.False);
            var enabled=new HeuristicStrategy(new HeuristicStrategySettings(shopRerollGoldCost:1,rerollGoldMinimum:10,rerollValue:200));
            Assert.That(enabled.ChooseAction(obs,Context(enabled,"on")).Action,Is.TypeOf<RerollShopAction>());
            var guarded=new HeuristicStrategy(new HeuristicStrategySettings(shopRerollGoldCost:1,rerollGoldMinimum:40,rerollValue:200));
            Assert.That(guarded.ChooseAction(obs,Context(guarded,"guard")).Action,Is.TypeOf<StartBattleAction>());
        }

        [Test] public void RerollDoesNotEstimateTheContentsOfFutureOffers()
        {
            var shop=new ShopState(ShopKind.Character,Array.Empty<ShopOffer>(),1,1,1);
            var obs=Build(Prep(),Econ(gold:30),shop:shop);
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(shopRerollGoldCost:1,rerollGoldMinimum:10));
            var reroll=strategy.GetEvaluations(obs,new ActionId("reroll" )).Single(x=>x.Action is RerollShopAction);
            Assert.That(reroll.Reason,Does.Contain("not predicted"));
            Assert.That(reroll.Score,Is.LessThan(30));
        }

        [Test] public void ExperienceUsesExplicitCostAndRewardsNearLevelUp()
        {
            var obs=Build(Prep(),Econ(gold:30),shop:new ShopState(ShopKind.Character,Array.Empty<ShopOffer>(),1,1,1));
            var unknown=new HeuristicStrategy();Assert.That(unknown.GetEvaluations(obs,new ActionId("unk")).Any(x=>x.Action is BuyExperienceAction),Is.False);
            var configured=new HeuristicStrategy(new HeuristicStrategySettings(normalExperienceGoldCost:4,experienceValue:60));
            Assert.That(configured.ChooseAction(obs,Context(configured,"xp")).Action,Is.TypeOf<BuyExperienceAction>());
        }

        [Test] public void ItemUseIsValuedByVisibleEffectAndTarget()
        {
            var d=Def("ally");var ally=Inst("ally1",d);var board=Put(BoardState.CreateStandard(),0,"ally1");
            var item=new ItemDefinition("block","Block",0,"",ItemEffectTarget.Player,ItemEffectDuration.ThisBattle,
                new[]{new ItemEffectDefinition(ItemEffectKind.Block,4)});
            var hand=Put(HandState.CreateStandard(),0,HandSlotContent.Item("block"));
            var obs=Build(Prep(),Econ(),board,hand,new[]{ally},new[]{d},items:new[]{item});
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(itemUseBaseValue:100));
            Assert.That(strategy.ChooseAction(obs,Context(strategy,"use")).Action,Is.TypeOf<UseHandItemAction>());
        }

        [Test] public void ItemSaleOnlyHasValueWhenHandHasNoEmptySlotAndNeverAddsGoldValue()
        {
            var item=new ItemDefinition("i","Item",0,"",ItemEffectTarget.Player,ItemEffectDuration.Immediate,
                new[]{new ItemEffectDefinition(ItemEffectKind.Block,1)});
            var hand=Put(HandState.CreateStandard(),0,HandSlotContent.Item("i"));
            var obs=Build(Prep(),Econ(),hand:hand,items:new[]{item});
            var sell=new HeuristicStrategy().GetEvaluations(obs,new ActionId("sell")).Single(x=>x.Action is SellItemAction);
            Assert.That(sell.Score,Is.EqualTo(new HeuristicStrategySettings().NoImprovementPenalty));
            hand=HandState.CreateStandard();for(int i=0;i<hand.SlotCount;i++)hand=Put(hand,i,HandSlotContent.Item("i"));
            obs=Build(Prep(),Econ(),hand:hand,items:new[]{item});
            sell=new HeuristicStrategy().GetEvaluations(obs,new ActionId("sell-full")).Single(x=>x.Action is SellItemAction action&&action.HandSlot==0);
            Assert.That(sell.Score,Is.EqualTo(new HeuristicStrategySettings().ItemSaleSpaceValue));
            Assert.That(sell.Reason,Does.Contain("no Gold"));
        }

        [Test] public void CharacterSaleIsPenalizedWhenItWouldSacrificeBoardValue()
        {
            var d=Def("fighter");var instance=Inst("fighter1",d);var board=Put(BoardState.CreateStandard(),0,"fighter1");
            var obs=Build(Prep(),Econ(gold:0),board,HandState.CreateStandard(),new[]{instance},new[]{d});
            var sale=new HeuristicStrategy().GetEvaluations(obs,new ActionId("sale")).Single(x=>x.Action is SellCharacterAction);
            Assert.That(sale.Score,Is.LessThan(0));
        }

        [Test] public void EventItemWithoutEligibleTargetEndsEventInsteadOfStalling()
        {
            var cfg=Config(1,100);var p=Prep(cfg);var b=StageProgressRules.BeginBattle(p).NextState;
            var pending=StageProgressRules.ResolveBattle(b,cfg,true).NextState;
            var ev=StageProgressRules.CompletePendingReward(pending,cfg,new SimulationRandomStreams(7).Event.CaptureState()).NextState;
            var item=new ItemDefinition("star","Star",0,"",ItemEffectTarget.OneAlly,ItemEffectDuration.Immediate,
                new[]{new ItemEffectDefinition(ItemEffectKind.Star,2)});
            var obs=Build(ev,items:new[]{item},eventItem:new EventItemState("star"));
            var strategy=new HeuristicStrategy();Assert.That(strategy.ChooseAction(obs,Context(strategy,"event")).Action,Is.TypeOf<EndEventAction>());
        }

        [Test] public void ForcedBattleOccursAtPreparationDecisionLimitOnlyWhenItIsLegal()
        {
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(preparationDecisionLimit:2));var obs=Build(Prep());
            var state=new StrategyState(strategy.StrategyId,strategy.Version,1,"hs1|1|");
            var decision=strategy.ChooseAction(obs,Context(strategy,"limit",state:state));
            Assert.That(decision.Action,Is.TypeOf<StartBattleAction>());
            var x=Def("x");var y=Def("y");var ix=Inst("x",x);var iy=Inst("y",y);
            var over=Build(Prep(),Econ(level:1),board:Put(Put(BoardState.CreateStandard(),0,"x"),1,"y"),
                owned:new[]{ix,iy},defs:new[]{x,y});
            Assert.That(over.IsStrategyActionAllowed,Is.True);
            var cannotStart=strategy.ChooseAction(over,Context(strategy,"no-start",state:state));
            Assert.That(cannotStart.Action,Is.Not.TypeOf<StartBattleAction>());
        }

        [Test] public void SameObservationAndCheckpointReturnSameActionAndDoNotConsumeAnyRng()
        {
            var strategy=new HeuristicStrategy();var obs=Build(Choice());var rng=StrategyRandomSource.Create(123,Sid,Ver).CaptureState();
            var c1=Context(strategy,"stable",rng:rng);var c2=Context(strategy,"stable",rng:rng);
            var a=strategy.ChooseAction(obs,c1);var b=strategy.ChooseAction(obs,c2);
            Assert.That(a.Action.Kind,Is.EqualTo(b.Action.Kind));Assert.That(((SelectStageChoiceAction)a.Action).Choice,Is.EqualTo(((SelectStageChoiceAction)b.Action).Choice));
            Assert.That(a.Action.ActionId,Is.EqualTo(b.Action.ActionId));Assert.That(a.NextStrategyRngState,Is.EqualTo(rng));Assert.That(a.StrategyRngAdvanced,Is.False);
            Assert.That(c1.Accepts(a),Is.True);Assert.That(c2.Accepts(b),Is.True);
        }

        [Test] public void EqualScoresUseStableTieBreakAndChangingActionIdDoesNotChangeChoice()
        {
            var obs=Build(Choice());var strategy=new HeuristicStrategy(new HeuristicStrategySettings(
                selectAValue:10,selectBValue:10,selectCValue:10,selectDBaseValue:10,selectDHealValuePerHp:0));
            var a=strategy.GetEvaluations(obs,new ActionId("first")).First().Action;
            var b=strategy.GetEvaluations(obs,new ActionId("second")).First().Action;
            Assert.That(((SelectStageChoiceAction)a).Choice,Is.EqualTo(StageChoice.SelectA));
            Assert.That(((SelectStageChoiceAction)b).Choice,Is.EqualTo(StageChoice.SelectA));
        }

        [Test] public void MeaninglessBoardSwapDoesNotOutrankReadyBattle()
        {
            var a=Def("a");var b=Def("b");var ia=Inst("ia",a);var ib=Inst("ib",b);
            var board=Put(Put(BoardState.CreateStandard(),0,"ia"),1,"ib");
            var obs=Build(Prep(),Econ(),board,HandState.CreateStandard(),new[]{ia,ib},new[]{a,b});
            Assert.That(new HeuristicStrategy().ChooseAction(obs,Context(new HeuristicStrategy(),"swap")).Action,Is.TypeOf<StartBattleAction>());
        }

        [Test] public void PreparationLimitDoesNotForceBattleWhenNoLegalStartCandidateExists()
        {
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(preparationDecisionLimit:1));
            var d=Def("a");var a=Inst("a",d);var b=Def("b");var ib=Inst("b",b);
            var board=Put(Put(BoardState.CreateStandard(),0,"a"),1,"b");
            var obs=Build(Prep(),Econ(level:1),board,HandState.CreateStandard(),new[]{a,ib},new[]{d,b});
            var state=new StrategyState(strategy.StrategyId,strategy.Version,1,"hs1|0|");
            var decision=strategy.ChooseAction(obs,Context(strategy,"no-legal",state:state));
            Assert.That(decision.Action,Is.Not.TypeOf<StartBattleAction>());
        }

        [Test] public void NoPositivePreparationImprovementReturnsNoActionInsteadOfMovementLoop()
        {
            var strategy=new HeuristicStrategy();var d=Def("a");var a=Inst("a",d);
            var board=Put(BoardState.CreateStandard(),0,"a");
            var obs=Build(Prep(),Econ(hp:0),board,HandState.CreateStandard(),new[]{a},new[]{d});
            var decision=strategy.ChooseAction(obs,Context(strategy,"stalled"));
            Assert.That(decision.HasAction,Is.False);
            Assert.That(decision.Diagnostic,Does.Contain("Runner may detect the stall"));
        }
    }
}
