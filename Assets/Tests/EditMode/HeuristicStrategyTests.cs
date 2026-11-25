using System;
using System.Collections.Generic;
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
using ProjectNST.GameCore.Sessions;
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
            ItemDefinition[] items=null,EventItemState eventItem=null,CharacterStatDefinition characterStatSettings=null)
        {
            defs=defs??Array.Empty<CharacterDefinition>();owned=owned??Array.Empty<CharacterInstance>();items=items??Array.Empty<ItemDefinition>();
            StageProgressConfig cfg=Config(stage.Phase==StageProgressPhase.AwaitingStageChoice?1:5);
            if(shop==null&&stage.Phase==StageProgressPhase.Preparation)
                shop=new ShopState(stage.CurrentSelection.ShopMode==StageShopMode.Item?ShopKind.Item:ShopKind.Character,
                    Array.Empty<ShopOffer>(),0,cfg.GetStageNumber(stage.EncounterIndex),cfg.GetRoundNumber(stage.EncounterIndex));
            var result=ObservationBuilder.Build(new ObservationSource(stage,cfg,economy??Econ(),EconomyConfig(),
                board??BoardState.CreateStandard(),hand??HandState.CreateStandard(),new CharacterOwnershipState(owned),
                defs,defs,defs,items,shop,new ItemEffectState(),eventItem,Array.Empty<EnemyDefinition>(),EnemyBoardState.CreateEmpty(),
                characterStatSettings:characterStatSettings));
            Assert.That(result.Succeeded,Is.True,string.Join(";",result.Errors));return result.Observation;
        }
        private static StrategyContext Context(HeuristicStrategy strategy,string id="h/action0",long step=0,StrategyState state=null,StrategyRngState rng=null)=>
            new StrategyContext(strategy.StrategyId,strategy.Version,0,step,new ActionId(id),state??StrategyState.EmptyFor(strategy.StrategyId,strategy.Version),rng);
        private static BoardState Put(BoardState b,int slot,string id){Assert.That(b.TryWithCharacterAt(slot,id,out BoardState next),Is.True);return next;}
        private static HandState Put(HandState h,int slot,HandSlotContent content){Assert.That(h.TryWithSlot(slot,content,out HandState next),Is.True);return next;}
        private static ShopState CharacterShop(string def,int price=1)=>new ShopState(ShopKind.Character,
            new[]{new ShopOffer("offer",ShopOfferKind.Character,def,price,1)},1,1,1);
        private static CharacterStatDefinition Stats(params CharacterStatOption[] options)=>new CharacterStatDefinition(new[]{
            new CharacterCostStats(1,options,options,options)});

        [Test] public void MetadataSettingsAndValidationAreStable()
        {
            var s=new HeuristicStrategy();Assert.That(s.StrategyId,Is.EqualTo("HeuristicStrategy"));Assert.That(s.Version,Is.EqualTo("7"));
            Assert.That(s.Settings.PreparationDecisionLimit,Is.EqualTo(30));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(preparationDecisionLimit:0));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(shopRerollGoldCost:-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(selectAValue:double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(attackValue:double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(purchasePowerReference:-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HeuristicStrategySettings(handPurchasePowerWeight:double.NaN));
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

        [Test]
        public void V9PurchasePowerUsesPublicRangeMidpointsAndPlacementWeights()
        {
            var stats=Stats(new CharacterStatOption(2,1,1),new CharacterStatOption(3,2,1),
                new CharacterStatOption(99,99,0));
            var obs=Build(Prep(),Econ(gold:7,level:1),shop:CharacterShop("ranged"),defs:new[]{Def("ranged")},characterStatSettings:stats);
            var v8=HeuristicStrategy.CreateV8().GetEvaluations(obs,new ActionId("range-v8"))
                .Where(x=>x.Action is BuyCharacterAction).ToArray();
            var v9=HeuristicStrategy.CreateV9().GetEvaluations(obs,new ActionId("range-v9"))
                .Where(x=>x.Action is BuyCharacterAction).ToArray();
            double boardV8=v8.First(x=>((BuyCharacterAction)x.Action).Placement.Kind==CharacterLocationKind.Board&&((BuyCharacterAction)x.Action).Placement.SlotIndex==0).Score;
            double boardV9=v9.First(x=>((BuyCharacterAction)x.Action).Placement.Kind==CharacterLocationKind.Board&&((BuyCharacterAction)x.Action).Placement.SlotIndex==0).Score;
            double handV8=v8.First(x=>((BuyCharacterAction)x.Action).Placement.Kind==CharacterLocationKind.Hand&&((BuyCharacterAction)x.Action).Placement.SlotIndex==0).Score;
            double handV9=v9.First(x=>((BuyCharacterAction)x.Action).Placement.Kind==CharacterLocationKind.Hand&&((BuyCharacterAction)x.Action).Placement.SlotIndex==0).Score;
            // ATK midpoint 2.5 + 0.6 * DEF midpoint 1.5 = 3.4; zero-weight extrema are excluded.
            Assert.That(boardV9,Is.EqualTo(boardV8+4.0*(3.4-3.5)).Within(1e-12));
            Assert.That(handV9,Is.EqualTo(handV8+0.5*(3.4-3.5)).Within(1e-12));
            Assert.That(boardV8,Is.EqualTo(123.3).Within(1e-12));
            Assert.That(handV8,Is.EqualTo(23.3).Within(1e-12));
            string trace=null;var diagnostic=HeuristicStrategy.CreateV9(null,value=>trace=value);
            diagnostic.ChooseAction(obs,Context(diagnostic,"range-trace"));
            Assert.That(trace,Does.Contain("public ATK range=2..3"));
            Assert.That(trace,Does.Contain("public DEF range=1..2"));
            Assert.That(trace,Does.Contain("EstimatedPower=3.4"));
            Assert.That(trace,Does.Contain("Adjustment=-0.4"));
        }

        [Test]
        public void V9PurchasePowerIsZeroAtReferenceAboveBelowAndWithoutPublicStats()
        {
            var definition=Def("power");var shop=CharacterShop("power");var economy=Econ(gold:7,level:1);
            GameObservation equal=Build(Prep(),economy,shop:shop,defs:new[]{definition},
                characterStatSettings:Stats(new CharacterStatOption(3,0,1),new CharacterStatOption(4,0,1)));
            GameObservation high=Build(Prep(),economy,shop:shop,defs:new[]{definition},
                characterStatSettings:Stats(new CharacterStatOption(8,4,1),new CharacterStatOption(6,2,1)));
            GameObservation missing=Build(Prep(),economy,shop:shop,defs:new[]{definition});
            double Score(GameObservation o,HeuristicStrategy strategy,string id)=>strategy.GetEvaluations(o,new ActionId(id)
                ).First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==CharacterLocationKind.Board&&b.Placement.SlotIndex==0).Score;
            double baseScore=Score(equal,HeuristicStrategy.CreateV8(),"eq-v8");
            Assert.That(Score(equal,HeuristicStrategy.CreateV9(),"eq-v9"),Is.EqualTo(baseScore).Within(1e-12));
            Assert.That(Score(high,HeuristicStrategy.CreateV9(),"high-v9"),Is.GreaterThan(Score(high,HeuristicStrategy.CreateV8(),"high-v8")));
            Assert.That(Score(missing,HeuristicStrategy.CreateV9(),"missing-v9"),Is.EqualTo(Score(missing,HeuristicStrategy.CreateV8(),"missing-v8")));
        }

        [Test]
        public void V9PurchasePowerIsSkippedWhenBoardWillRemainOverLevelOrPurchaseMerges()
        {
            var incoming=Def("incoming");var resident=Def("resident");var owner=Inst("resident-i",resident);
            var overBoard=Put(BoardState.CreateStandard(),0,owner.InstanceId);
            var over=Build(Prep(),Econ(gold:7,level:1),overBoard,owned:new[]{owner},defs:new[]{incoming,resident},
                shop:CharacterShop("incoming"),characterStatSettings:Stats(new CharacterStatOption(8,4,1)));
            double overV8=HeuristicStrategy.CreateV8().GetEvaluations(over,new ActionId("over-v8"))
                .First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==CharacterLocationKind.Board&&b.Placement.SlotIndex==1).Score;
            double overV9=HeuristicStrategy.CreateV9().GetEvaluations(over,new ActionId("over-v9"))
                .First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==CharacterLocationKind.Board&&b.Placement.SlotIndex==1).Score;
            Assert.That(overV9,Is.EqualTo(overV8));

            var d=Def("merge-power");var first=Inst("m1",d);var second=Inst("m2",d);
            var third=Inst("m3",d,2);var fourth=Inst("m4",d,2);
            HandState hand=HandState.CreateStandard();hand=Put(hand,0,HandSlotContent.Character(second.InstanceId));
            hand=Put(hand,1,HandSlotContent.Character(third.InstanceId));hand=Put(hand,2,HandSlotContent.Character(fourth.InstanceId));
            var merge=Build(Prep(),Econ(gold:7,level:1),Put(BoardState.CreateStandard(),0,first.InstanceId),hand,
                new[]{first,second,third,fourth},new[]{d},CharacterShop("merge-power"),characterStatSettings:Stats(new CharacterStatOption(8,4,1)));
            double mergeV8=HeuristicStrategy.CreateV8().GetEvaluations(merge,new ActionId("merge-v8"))
                .First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==CharacterLocationKind.Hand&&b.Placement.SlotIndex==3).Score;
            double mergeV9=HeuristicStrategy.CreateV9().GetEvaluations(merge,new ActionId("merge-v9"))
                .First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==CharacterLocationKind.Hand&&b.Placement.SlotIndex==3).Score;
            Assert.That(mergeV9,Is.EqualTo(mergeV8));
        }

        [Test]
        public void V9LeavesV7V8AndNonPurchaseEvaluationsAndCandidatesUnchanged()
        {
            var d=Def("v9-regression");var stats=Stats(new CharacterStatOption(8,4,1));
            var obs=Build(Prep(),Econ(gold:7,level:1),shop:CharacterShop("v9-regression"),defs:new[]{d},characterStatSettings:stats);
            var noStats=Build(Prep(),Econ(gold:7,level:1),shop:CharacterShop("v9-regression"),defs:new[]{d});
            var v7=new HeuristicStrategy();var v8=HeuristicStrategy.CreateV8();var v9=HeuristicStrategy.CreateV9();
            var e7=v7.GetEvaluations(obs,new ActionId("same-candidates"));var e8=v8.GetEvaluations(obs,new ActionId("same-candidates"));var e9=v9.GetEvaluations(obs,new ActionId("same-candidates"));
            Assert.That(e7.Select(x=>x.Action.ActionId.Value).OrderBy(x=>x,StringComparer.Ordinal),
                Is.EqualTo(e9.Select(x=>x.Action.ActionId.Value).OrderBy(x=>x,StringComparer.Ordinal)));
            Assert.That(e8.Select(x=>x.Action.ActionId.Value).OrderBy(x=>x,StringComparer.Ordinal),
                Is.EqualTo(e9.Select(x=>x.Action.ActionId.Value).OrderBy(x=>x,StringComparer.Ordinal)));
            Assert.That(e7.Where(x=>!(x.Action is BuyCharacterAction)).Select(x=>x.Score),
                Is.EqualTo(e9.Where(x=>!(x.Action is BuyCharacterAction)).Select(x=>x.Score)));
            Assert.That(e8.Where(x=>!(x.Action is BuyCharacterAction)).Select(x=>x.Score),
                Is.EqualTo(e9.Where(x=>!(x.Action is BuyCharacterAction)).Select(x=>x.Score)));
            double PurchaseScore(HeuristicStrategy s,GameObservation o,string id)=>s.GetEvaluations(o,new ActionId(id))
                .First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==CharacterLocationKind.Board&&b.Placement.SlotIndex==0).Score;
            Assert.That(PurchaseScore(v7,obs,"v7-stats"),Is.EqualTo(PurchaseScore(v7,noStats,"v7-no-stats")));
            Assert.That(PurchaseScore(v8,obs,"v8-stats"),Is.EqualTo(PurchaseScore(v8,noStats,"v8-no-stats")));
            Assert.That(v7.Version,Is.EqualTo("7"));Assert.That(v8.Version,Is.EqualTo("8"));Assert.That(v9.Version,Is.EqualTo("9"));
            Assert.That(HeuristicStrategy.StateSchemaVersion,Is.EqualTo(2));
            Assert.That(v9.Settings.PurchasePowerReference,Is.EqualTo(3.5));
            Assert.That(v9.Settings.ImmediateBoardPurchasePowerWeight,Is.EqualTo(4.0));
            Assert.That(v9.Settings.HandPurchasePowerWeight,Is.EqualTo(0.5));
            Assert.That(HeuristicStrategy.GetV9PurchaseConfigurationIdentity(v9.Settings),Does.Contain("ref3.5-board4-hand0.5"));
            Assert.Throws<ArgumentException>(()=>new StrategyContext(v9.StrategyId,v9.Version,0,0,
                new ActionId("cross-version"),StrategyState.EmptyFor(v9.StrategyId,v8.Version)));
            var context=Context(v9,"same-action",3);
            Assert.That(v9.ChooseAction(obs,context).Action.Kind,Is.EqualTo(v9.ChooseAction(obs,context).Action.Kind));
        }

        [Test]
        public void V10MergeProgressUsesDefinitionAndActualStarCountsAcrossBoardAndHand()
        {
            var settings=new HeuristicStrategySettings(handSlotReleaseValue:0,boardSlotReleaseValue:0,
                handCapacityPenaltyOneEmpty:0,handCapacityPenaltyNoEmpty:0);
            var d=Def("v10-progress");var other=Def("v10-other");
            double Delta(CharacterInstance[] residents,CharacterDefinition[] definitions,string suffix)
            {
                HandState hand=HandState.CreateStandard();BoardState board=BoardState.CreateStandard();
                for(int i=0;i<residents.Length;i++)hand=Put(hand,i,HandSlotContent.Character(residents[i].InstanceId));
                GameObservation observation=Build(Prep(),Econ(),board,hand,residents,definitions,CharacterShop("v10-progress"));
                HeuristicStrategy v9=HeuristicStrategy.CreateV9(settings),v10=HeuristicStrategy.CreateV10(settings);
                double Score(HeuristicStrategy strategy,string id)=>strategy.GetEvaluations(observation,new ActionId(id))
                    .First(x=>x.Action is BuyCharacterAction buy&&buy.Placement.Kind==CharacterLocationKind.Hand).Score;
                return Score(v10,suffix+"10-score")-Score(v9,suffix+"9-score");
            }
            Assert.That(Delta(Array.Empty<CharacterInstance>(),new[]{d},"none"),Is.EqualTo(0).Within(1e-12));
            Assert.That(Delta(new[]{Inst("one",d)},new[]{d},"one"),Is.EqualTo(8).Within(1e-12));
            Assert.That(Delta(new[]{Inst("two-a",d,2)},new[]{d},"two-a"),Is.EqualTo(2).Within(1e-12));
            Assert.That(Delta(new[]{Inst("two-a",d,2),Inst("two-b",d,2)},new[]{d},"two-b"),Is.EqualTo(4).Within(1e-12));
            Assert.That(Delta(new[]{Inst("three",d,3)},new[]{d},"three"),Is.EqualTo(0).Within(1e-12));
            Assert.That(Delta(new[]{Inst("wrong",other,1)},new[]{d,other},"wrong"),Is.EqualTo(0).Within(1e-12));
        }

        [Test]
        public void V10ImmediateMergeDoesNotDoubleCountProgressAndChainIs45Total()
        {
            var settings=new HeuristicStrategySettings(handSlotReleaseValue:0,boardSlotReleaseValue:0,
                handCapacityPenaltyOneEmpty:0,handCapacityPenaltyNoEmpty:0);
            var d=Def("v10-chain");
            double Delta(CharacterInstance[] residents,string suffix)
            {
                HandState hand=HandState.CreateStandard();
                for(int i=0;i<residents.Length;i++)hand=Put(hand,i,HandSlotContent.Character(residents[i].InstanceId));
                GameObservation observation=Build(Prep(),Econ(),hand:hand,owned:residents,defs:new[]{d},shop:CharacterShop("v10-chain"));
                HeuristicStrategy old=HeuristicStrategy.CreateV9(settings),next=HeuristicStrategy.CreateV10(settings);
                double Score(HeuristicStrategy strategy,string id)=>strategy.GetEvaluations(observation,new ActionId(id))
                    .First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==CharacterLocationKind.Hand).Score;
                return Score(next,suffix+"10")-Score(old,suffix+"9");
            }
            Assert.That(Delta(new[]{Inst("m1",d),Inst("m2",d)},"immediate"),Is.EqualTo(0).Within(1e-12));
            Assert.That(Delta(new[]{Inst("c1",d),Inst("c2",d),Inst("c3",d,2),Inst("c4",d,2)},"chain"),Is.EqualTo(27).Within(1e-12),
                "v9's 18 chain points are increased to a total of 45, not 18+45.");
        }

        [Test]
        public void V10UsesDefinitionOnlyForImmediateMergeAndSuppressesRerolledStatsBonus()
        {
            var d=Def("v10-run-cost");var a=Inst("rc-a",d,1,2);var b=Inst("rc-b",d,1,2);
            HandState hand=Put(Put(HandState.CreateStandard(),0,HandSlotContent.Character(a.InstanceId)),1,HandSlotContent.Character(b.InstanceId));
            var stats=Stats(new CharacterStatOption(9,9,1));
            GameObservation observation=Build(Prep(),Econ(),hand:hand,owned:new[]{a,b},defs:new[]{d},shop:CharacterShop("v10-run-cost",1),characterStatSettings:stats);
            double Score(HeuristicStrategy strategy,string id)=>strategy.GetEvaluations(observation,new ActionId(id))
                .First(x=>x.Action is BuyCharacterAction buy&&buy.Placement.Kind==CharacterLocationKind.Hand).Score;
            Assert.That(Score(HeuristicStrategy.CreateV10(),"v10-actual-merge")-Score(HeuristicStrategy.CreateV9(),"v9-cost-legacy"),
                Is.EqualTo(75+4-0.5*(9+0.6*9-3.5)).Within(1e-12),
                "The immediate merge releases one Hand slot (+4); the rerolled Stats bonus remains suppressed.");
        }

        [Test]
        public void V10AccountsForPostMergeHandAndBoardSlotChangesAndHandCapacity()
        {
            var d=Def("v10-slots");var first=Inst("slot-board",d);var second=Inst("slot-hand",d);
            BoardState board=Put(BoardState.CreateStandard(),0,first.InstanceId);
            HandState hand=Put(HandState.CreateStandard(),0,HandSlotContent.Character(second.InstanceId));
            GameObservation merge=Build(Prep(),Econ(),board,hand,new[]{first,second},new[]{d},CharacterShop("v10-slots"));
            double Score(HeuristicStrategy strategy,GameObservation observation,string id,CharacterLocationKind placement)
                =>strategy.GetEvaluations(observation,new ActionId(id)).First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==placement).Score;
            Assert.That(Score(HeuristicStrategy.CreateV10(),merge,"merge-hand-slot-v10",CharacterLocationKind.Hand)-
                Score(HeuristicStrategy.CreateV9(),merge,"merge-hand-slot-v9",CharacterLocationKind.Hand),Is.EqualTo(4).Within(1e-12),
                "Consuming a Hand copy frees one slot after the purchase merge.");

            var itemContents=Enumerable.Range(0,4).Select(i=>HandSlotContent.Item("item"+i)).ToArray();
            var itemDefs=Enumerable.Range(0,4).Select(i=>new ItemDefinition("item"+i,"Item",0,"",ItemEffectTarget.Player,
                ItemEffectDuration.Immediate,new[]{new ItemEffectDefinition(ItemEffectKind.Block,1)})).ToArray();
            HandState oneEmpty=HandState.CreateStandard();for(int i=0;i<4;i++)oneEmpty=Put(oneEmpty,i,itemContents[i]);
            GameObservation one=Build(Prep(),Econ(),hand:oneEmpty,defs:new[]{Def("capacity")},shop:CharacterShop("capacity"),items:itemDefs);
            HandState twoEmpty=HandState.CreateStandard();for(int i=0;i<3;i++)twoEmpty=Put(twoEmpty,i,itemContents[i]);
            GameObservation two=Build(Prep(),Econ(),hand:twoEmpty,defs:new[]{Def("capacity")},shop:CharacterShop("capacity"),items:itemDefs);
            Assert.That(Score(HeuristicStrategy.CreateV10(),one,"one-empty-v10",CharacterLocationKind.Hand)-
                Score(HeuristicStrategy.CreateV9(),one,"one-empty-v9",CharacterLocationKind.Hand),Is.EqualTo(-5).Within(1e-12), "Only the no-empty capacity penalty applies; a normal Hand purchase has no slot-release adjustment.");
            Assert.That(Score(HeuristicStrategy.CreateV10(),two,"two-empty-v10",CharacterLocationKind.Hand)-
                Score(HeuristicStrategy.CreateV9(),two,"two-empty-v9",CharacterLocationKind.Hand),Is.EqualTo(-2).Within(1e-12), "Only the one-empty capacity penalty applies.");
            GameObservation ample=Build(Prep(),Econ(),hand:HandState.CreateStandard(),defs:new[]{Def("capacity")},
                shop:CharacterShop("capacity"),items:itemDefs);
            Assert.That(Score(HeuristicStrategy.CreateV10(),ample,"ample-v10",CharacterLocationKind.Hand)-
                Score(HeuristicStrategy.CreateV9(),ample,"ample-v9",CharacterLocationKind.Hand),Is.EqualTo(0).Within(1e-12),
                "A normal Hand purchase that leaves at least two empty slots has no slot-release bonus or capacity penalty.");
        }

        [Test]
        public void V10MergeSlotPreviewFollowsBoardThenHandSurvivorAndChainConsumptionOrder()
        {
            var d=Def("v10-slot-order");var statsOnly=new HeuristicStrategySettings();
            double Difference(GameObservation observation,CharacterLocationKind location,string suffix)
            {
                HeuristicStrategy v9=HeuristicStrategy.CreateV9(statsOnly),v10=HeuristicStrategy.CreateV10(statsOnly);
                double Score(HeuristicStrategy strategy,string id)=>strategy.GetEvaluations(observation,new ActionId(id))
                    .First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==location).Score;
                return Score(v10,suffix+"-10")-Score(v9,suffix+"-9");
            }
            var handFirst=Inst("hand-survivor",d);var handSecond=Inst("hand-consumed",d);
            HandState bothHand=Put(Put(HandState.CreateStandard(),0,HandSlotContent.Character(handFirst.InstanceId)),1,HandSlotContent.Character(handSecond.InstanceId));
            GameObservation movedSurvivor=Build(Prep(),Econ(),hand:bothHand,owned:new[]{handFirst,handSecond},defs:new[]{d},shop:CharacterShop("v10-slot-order"));
            Assert.That(Difference(movedSurvivor,CharacterLocationKind.Board,"hand-to-board-survivor"),Is.EqualTo(6).Within(1e-12),
                "The Hand merge frees two slots while the final Hand survivor moves into the requested empty Board slot (+8 - 2).");

            var first=Inst("ordered-chain-star1-a",d);var second=Inst("ordered-chain-star1-b",d);
            var boardTwo=Inst("ordered-chain-star2-board",d,2);var handTwo=Inst("ordered-chain-star2-hand",d,2);
            BoardState chainBoard=Put(BoardState.CreateStandard(),0,boardTwo.InstanceId);
            HandState chainHand=HandState.CreateStandard();
            chainHand=Put(chainHand,0,HandSlotContent.Character(first.InstanceId));
            chainHand=Put(chainHand,1,HandSlotContent.Character(second.InstanceId));
            chainHand=Put(chainHand,2,HandSlotContent.Character(handTwo.InstanceId));
            GameObservation chain=Build(Prep(),Econ(),chainBoard,chainHand,new[]{first,second,boardTwo,handTwo},new[]{d},CharacterShop("v10-slot-order"));
            Assert.That(Difference(chain,CharacterLocationKind.Hand,"chain-release"),Is.EqualTo(39).Within(1e-12),
                "The chain adds 27 over v9's 18, and the actual merge frees three Hand slots (+12); it does not count the purchased copy as an extra slot.");
        }

        [Test]
        public void V11BoardSaleUsesBoardQualityOnceAndKeepsSynergyLoss()
        {
            var settings=new HeuristicStrategySettings(includeTemporaryCharacterBonuses:true);
            double SaleScore(HeuristicStrategy strategy,GameObservation observation,string instanceId)=>
                strategy.GetEvaluations(observation,new ActionId("v11-board-sale"))
                    .Single(x=>x.Action is SellCharacterAction sale&&sale.InstanceId==instanceId).Score;

            CharacterDefinition loneDefinition=Def("v11-lone-board");
            CharacterInstance lone=Inst("v11-lone-board-instance",loneDefinition);
            lone.TemporaryAttackBonus=4;
            GameObservation loneObservation=Build(Prep(),board:Put(BoardState.CreateStandard(),0,lone.InstanceId),
                owned:new[]{lone},defs:new[]{loneDefinition});
            double v10Lone=SaleScore(HeuristicStrategy.CreateV10(settings),loneObservation,lone.InstanceId);
            double v11Lone=SaleScore(HeuristicStrategy.CreateV11(settings),loneObservation,lone.InstanceId);
            Assert.That(v11Lone-v10Lone,Is.EqualTo(4).Within(1e-12),
                "The configured temporary +4 Attack is included in Power; v10 deducted it twice and v11 deducts the BoardQuality loss once.");

            CharacterDefinition targetDefinition=Def("v11-unit-target",units:new[]{"Guard"});
            CharacterDefinition allyDefinition=Def("v11-unit-ally",units:new[]{"Guard"});
            CharacterInstance target=Inst("v11-unit-target-instance",targetDefinition);
            CharacterInstance ally=Inst("v11-unit-ally-instance",allyDefinition);
            BoardState board=Put(Put(BoardState.CreateStandard(),0,target.InstanceId),1,ally.InstanceId);
            GameObservation synergyObservation=Build(Prep(),board:board,owned:new[]{target,ally},defs:new[]{targetDefinition,allyDefinition});
            Assert.That(synergyObservation.Synergy.Units.Count,Is.EqualTo(1));
            double synergyV11=SaleScore(HeuristicStrategy.CreateV11(settings),synergyObservation,target.InstanceId);
            Assert.That(synergyV11,Is.EqualTo(-28).Within(1e-12),
                "The ★1 target has zero Power before its first Star upgrade; the quality loss is partial Unit value 2 plus established Unit value 8, plus the existing sale penalty 18.");
        }

        [Test]
        public void V11SaleSubtractsHeldMergeProgressByDefinitionAndObservedStarAcrossBoardAndHand()
        {
            var settings=new HeuristicStrategySettings(sellGoldValuePerGold:0,characterSaleLossPenalty:0,
                attackValue:0,defenseValue:0,characterStarValue:0,unitPartialValue:0,unitEstablishedValue:0,
                attributeSharedValue:0,handCapacityPenaltyOneEmpty:0,handCapacityPenaltyNoEmpty:0);
            double Delta(CharacterInstance[] residents,CharacterDefinition[] definitions,string soldId,bool boardFirst=false)
            {
                BoardState board=BoardState.CreateStandard();HandState hand=HandState.CreateStandard();int handSlot=0;
                for(int i=0;i<residents.Length;i++)
                {
                    if(boardFirst&&i==0)board=Put(board,0,residents[i].InstanceId);
                    else hand=Put(hand,handSlot++,HandSlotContent.Character(residents[i].InstanceId));
                }
                GameObservation observation=Build(Prep(),board:board,hand:hand,owned:residents,defs:definitions);
                HeuristicStrategy v10=HeuristicStrategy.CreateV10(settings),v11=HeuristicStrategy.CreateV11(settings);
                double Score(HeuristicStrategy strategy)=>strategy.GetEvaluations(observation,new ActionId("v11-progress-sale"))
                    .Single(x=>x.Action is SellCharacterAction sale&&sale.InstanceId==soldId).Score;
                return Score(v11)-Score(v10);
            }

            CharacterDefinition d=Def("v11-progress"),other=Def("v11-progress-other");
            CharacterInstance one=Inst("v11-one-star-one",d);
            Assert.That(Delta(new[]{one},new[]{d},one.InstanceId),Is.EqualTo(0).Within(1e-12),"One ★1 has no held progress value.");
            CharacterInstance oneA=Inst("v11-one-star-a",d),oneB=Inst("v11-one-star-b",d);
            Assert.That(Delta(new[]{oneA,oneB},new[]{d},oneA.InstanceId),Is.EqualTo(-8).Within(1e-12),"Selling from two ★1 loses the held value 8.");
            CharacterInstance two=Inst("v11-two-star-one",d,2);
            Assert.That(Delta(new[]{two},new[]{d},two.InstanceId),Is.EqualTo(-2).Within(1e-12),"One ★2 has held progress value 2.");
            CharacterInstance twoA=Inst("v11-two-star-a",d,2),twoB=Inst("v11-two-star-b",d,2);
            Assert.That(Delta(new[]{twoA,twoB},new[]{d},twoA.InstanceId),Is.EqualTo(-2).Within(1e-12),"The held value falls from 4 to 2 when selling one of two ★2.");
            CharacterInstance three=Inst("v11-three-star",d,3);
            Assert.That(Delta(new[]{three},new[]{d},three.InstanceId),Is.EqualTo(0).Within(1e-12),"★3 does not receive additional merge progress value.");
            CharacterInstance itemStar=Inst("v11-item-star-upgrade",d,2),otherTwo=Inst("v11-item-star-peer",d,2);
            Assert.That(Delta(new[]{itemStar,otherTwo},new[]{d},itemStar.InstanceId),Is.EqualTo(-2).Within(1e-12),
                "An Item-upgraded character is counted at its observed ★2.");
            CharacterInstance mixedOne=Inst("v11-mixed-one",d),mixedTwo=Inst("v11-mixed-two",d,2);
            Assert.That(Delta(new[]{mixedOne,mixedTwo},new[]{d},mixedOne.InstanceId),Is.EqualTo(0).Within(1e-12),
                "A lone ★1 does not gain cross-star value from a separate ★2.");
            CharacterInstance unique=Inst("v11-unique-definition",d),different=Inst("v11-different-definition",other);
            Assert.That(Delta(new[]{unique,different},new[]{d,other},unique.InstanceId),Is.EqualTo(0).Within(1e-12),
                "Progress is grouped by Definition ID.");
            CharacterInstance poolCopies= new CharacterInstance("v11-poolcopies-three",d,1,1,3);
            Assert.That(Delta(new[]{poolCopies},new[]{d},poolCopies.InstanceId),Is.EqualTo(0).Within(1e-12),
                "PoolCopies do not inflate the count of owned ★1 instances.");
            CharacterInstance boardOne=Inst("v11-distributed-board",d),handOne=Inst("v11-distributed-hand",d);
            Assert.That(Delta(new[]{boardOne,handOne},new[]{d},handOne.InstanceId,true),Is.EqualTo(-8).Within(1e-12),
                "Merge progress aggregates actual Board and Hand instances.");
        }

        [Test]
        public void V11HandSaleCapacityReliefMatchesPreSaleEmptySlotsWithoutChangingItemSale()
        {
            var settings=new HeuristicStrategySettings(sellGoldValuePerGold:0,characterSaleLossPenalty:0,
                attackValue:0,defenseValue:0,characterStarValue:0,handCapacityPenaltyOneEmpty:2,handCapacityPenaltyNoEmpty:5);
            var d=Def("v11-capacity-sale");
            var character=Inst("v11-capacity-character",d);
            var itemContents=Enumerable.Range(0,4).Select(i=>HandSlotContent.Item("v11-item-"+i)).ToArray();
            var itemDefinitions=Enumerable.Range(0,4).Select(i=>new ItemDefinition("v11-item-"+i,"Item",0,"",
                ItemEffectTarget.Player,ItemEffectDuration.Immediate,new[]{new ItemEffectDefinition(ItemEffectKind.Block,1)})).ToArray();
            double Delta(int occupiedItems)
            {
                HandState hand=HandState.CreateStandard();
                for(int i=0;i<occupiedItems;i++)hand=Put(hand,i,itemContents[i]);
                hand=Put(hand,occupiedItems,HandSlotContent.Character(character.InstanceId));
                GameObservation observation=Build(Prep(),hand:hand,owned:new[]{character},defs:new[]{d},items:itemDefinitions);
                double Score(HeuristicStrategy strategy,string actionId)=>strategy.GetEvaluations(observation,new ActionId(actionId))
                    .Single(x=>x.Action is SellCharacterAction sale&&sale.InstanceId==character.InstanceId).Score;
                return Score(HeuristicStrategy.CreateV11(settings),"v11-capacity-sale")-
                    Score(HeuristicStrategy.CreateV10(settings),"v10-capacity-sale");
            }
            Assert.That(Delta(4),Is.EqualTo(5).Within(1e-12),"A full Hand receives only the configured 5-point relief.");
            Assert.That(Delta(3),Is.EqualTo(2).Within(1e-12),"One pre-sale empty slot receives only the configured 2-point relief.");
            Assert.That(Delta(2),Is.EqualTo(0).Within(1e-12),"Two or more pre-sale empty slots receive no relief.");

            HandState full=HandState.CreateStandard();for(int i=0;i<4;i++)full=Put(full,i,itemContents[i]);
            full=Put(full,4,HandSlotContent.Character(character.InstanceId));
            GameObservation fullObservation=Build(Prep(),hand:full,owned:new[]{character},defs:new[]{d},items:itemDefinitions);
            HeuristicActionEvaluation itemV10=HeuristicStrategy.CreateV10(settings).GetEvaluations(fullObservation,new ActionId("v10-item-sale"))
                .First(x=>x.Action is SellItemAction);
            HeuristicActionEvaluation itemV11=HeuristicStrategy.CreateV11(settings).GetEvaluations(fullObservation,new ActionId("v11-item-sale"))
                .First(x=>x.Action is SellItemAction);
            Assert.That(itemV11.Score,Is.EqualTo(itemV10.Score),"SellItem evaluation is unchanged in v11.");
        }

        [Test]
        public void V11StillPrioritizesLegalBoardOverLevelRepairSales()
        {
            var definitions=Enumerable.Range(0,7).Select(i=>Def("v11-over-"+i)).ToArray();
            var instances=definitions.Select((d,i)=>Inst("v11-over-instance-"+i,d)).ToArray();
            BoardState board=Put(Put(BoardState.CreateStandard(),0,instances[0].InstanceId),1,instances[1].InstanceId);
            HandState hand=HandState.CreateStandard();for(int i=2;i<7;i++)hand=Put(hand,i-2,HandSlotContent.Character(instances[i].InstanceId));
            GameObservation observation=Build(Prep(),Econ(gold:7,level:1),board,hand,instances,definitions);
            HeuristicStrategy strategy=HeuristicStrategy.CreateV11(new HeuristicStrategySettings(normalExperienceGoldCost:40));
            StrategyDecision decision=strategy.ChooseAction(observation,Context(strategy,"v11-over-level"));
            Assert.That(decision.Action,Is.TypeOf<SellCharacterAction>());
            Assert.That(new[]{instances[0].InstanceId,instances[1].InstanceId},Does.Contain(((SellCharacterAction)decision.Action).InstanceId));
            Assert.That(observation.Board.Slots.Count(x=>x.Character!=null),Is.EqualTo(2),"Scoring remains observational; the Action is only a declaration.");
        }

        [Test]
        public void V11KeepsPurchaseCandidatesAndLegacyVersionsAndUsesVersionedIdentityWithoutRng()
        {
            var definition=Def("v11-identity");var stats=Stats(new CharacterStatOption(8,4,1));
            GameObservation observation=Build(Prep(),Econ(gold:7,level:1),shop:CharacterShop("v11-identity"),
                defs:new[]{definition},characterStatSettings:stats);
            HeuristicStrategySettings settings=new HeuristicStrategySettings(characterStarValue:0.75,
                includeTemporaryCharacterBonuses:true,purchasePowerReference:3.5,
                immediateBoardPurchasePowerWeight:4.0,handPurchasePowerWeight:0.5);
            HeuristicStrategy v7=new HeuristicStrategy();HeuristicStrategy v8=HeuristicStrategy.CreateV8();
            HeuristicStrategy v9=HeuristicStrategy.CreateV9(settings);HeuristicStrategy v10=HeuristicStrategy.CreateV10(settings);
            HeuristicStrategy v11=HeuristicStrategy.CreateV11(settings);
            Assert.That(v11.Version,Is.EqualTo("11"));Assert.That(HeuristicStrategy.StateSchemaVersion,Is.EqualTo(2));
            foreach(HeuristicStrategy old in new[]{v7,v8,v9,v10})
            {
                Assert.That(old.Version,Is.Not.EqualTo(v11.Version));
                var oldKinds=old.GetEvaluations(observation,new ActionId("v11-old-"+old.Version)).Select(evaluation=>evaluation.Action.Kind).OrderBy(kind=>kind).ToArray();
                var newKinds=v11.GetEvaluations(observation,new ActionId("v11-new-"+old.Version)).Select(evaluation=>evaluation.Action.Kind).OrderBy(kind=>kind).ToArray();
                Assert.That(oldKinds,Is.EqualTo(newKinds));
            }
            double Purchase(HeuristicStrategy strategy)=>strategy.GetEvaluations(observation,new ActionId("v11-purchase-equal"))
                .First(x=>x.Action is BuyCharacterAction).Score;
            Assert.That(Purchase(v11),Is.EqualTo(Purchase(v10)).Within(1e-12),"v11 retains v10 purchase scoring.");
            Assert.That(HeuristicStrategy.GetV11ConfigurationIdentity(settings),Is.Not.EqualTo(
                HeuristicStrategy.GetV11ConfigurationIdentity(new HeuristicStrategySettings(characterSaleLossPenalty:19,
                    characterStarValue:0.75,includeTemporaryCharacterBonuses:true,purchasePowerReference:3.5,
                    immediateBoardPurchasePowerWeight:4.0,handPurchasePowerWeight:0.5))));

            var rng=new StrategyRngState(0,v11.StrategyId,v11.Version,new Pcg32State(1,123,5));
            StrategyDecision decision=v11.ChooseAction(observation,Context(v11,"v11-no-rng",rng:rng));
            Assert.That(decision.NextStrategyRngState,Is.EqualTo(rng));
        }

        [Test]
        public void V10UsesOwnedInstancesRatherThanPoolCopiesAndIncludesItemStarUpgrades()
        {
            var d=Def("v10-pool-copies");
            var oneCopy=Inst("pool-copy-one",d,1,1);var inflatedCopies=new CharacterInstance("pool-copy-three",d,1,1,3);
            var freeStar=Inst("item006-star2",d,2,1);
            double Delta(CharacterInstance[] residents,string id)
            {
                HandState hand=HandState.CreateStandard();for(int i=0;i<residents.Length;i++)hand=Put(hand,i,HandSlotContent.Character(residents[i].InstanceId));
                GameObservation observation=Build(Prep(),Econ(),hand:hand,owned:residents,defs:new[]{d},shop:CharacterShop("v10-pool-copies"));
                HeuristicStrategy v9=HeuristicStrategy.CreateV9(),v10=HeuristicStrategy.CreateV10();
                double Score(HeuristicStrategy strategy,string suffix)=>strategy.GetEvaluations(observation,new ActionId(suffix))
                    .First(x=>x.Action is BuyCharacterAction b&&b.Placement.Kind==CharacterLocationKind.Hand).Score;
                return Score(v10,id+"-10")-Score(v9,id+"-9");
            }
            Assert.That(Delta(new[]{inflatedCopies},"pool-is-not-count"),Is.EqualTo(8).Within(1e-12),
                "One owned ★1 contributes one merge-progress instance even if it represents three PoolCopies.");
            Assert.That(Delta(new[]{oneCopy,freeStar},"item006-upgrade"),Is.EqualTo(10).Within(1e-12),
                "The observed ★2 contributes to ★3 progress; the free star change does not need extra PoolCopies.");
        }

        [Test]
        public void V10KeepsLegacyVersionsAndCandidatesAndHasVersionedSettingsIdentity()
        {
            var d=Def("v10-regression");var stats=Stats(new CharacterStatOption(8,4,1));
            var obs=Build(Prep(),Econ(gold:7,level:1),shop:CharacterShop("v10-regression"),defs:new[]{d},characterStatSettings:stats);
            var v7=new HeuristicStrategy();var v8=HeuristicStrategy.CreateV8();var v9=HeuristicStrategy.CreateV9();var v10=HeuristicStrategy.CreateV10();
            var before7=v7.GetEvaluations(obs,new ActionId("v7a"));var after7=new HeuristicStrategy().GetEvaluations(obs,new ActionId("v7b"));
            var before8=v8.GetEvaluations(obs,new ActionId("v8a"));var after8=HeuristicStrategy.CreateV8().GetEvaluations(obs,new ActionId("v8b"));
            var before9=v9.GetEvaluations(obs,new ActionId("v9a"));var after9=HeuristicStrategy.CreateV9().GetEvaluations(obs,new ActionId("v9b"));
            Assert.That(before7.Select(x=>x.Score),Is.EqualTo(after7.Select(x=>x.Score)));
            Assert.That(before8.Select(x=>x.Score),Is.EqualTo(after8.Select(x=>x.Score)));
            Assert.That(before9.Select(x=>x.Score),Is.EqualTo(after9.Select(x=>x.Score)));
            Assert.That(v10.Version,Is.EqualTo("10"));Assert.That(HeuristicStrategy.StateSchemaVersion,Is.EqualTo(2));
            Assert.That(before9.Select(x=>x.Action.Kind).OrderBy(x=>x),Is.EqualTo(v10.GetEvaluations(obs,new ActionId("v10-candidates")).Select(x=>x.Action.Kind).OrderBy(x=>x)));
            string identity=HeuristicStrategy.GetV10PurchaseConfigurationIdentity(v10.Settings);
            Assert.That(identity,Does.Contain("-progress18-progress2a2-progress2b4-chain45-handSlot4-boardSlot2-handPenalty12-handPenalty05"));
            Assert.That(HeuristicStrategy.GetV10PurchaseConfigurationIdentity(new HeuristicStrategySettings(chainMergeValue:46)),
                Is.Not.EqualTo(identity));
            Assert.Throws<ArgumentException>(()=>new StrategyContext(v10.StrategyId,v10.Version,0,0,new ActionId("v10-state"),
                StrategyState.EmptyFor(v10.StrategyId,v9.Version)));
        }

        [Test]
        public void V10RetainsPublicStatsAdjustmentForNonMergeAndDoesNotAdvanceStrategyRng()
        {
            var definition=Def("v10-stats");var stats=Stats(new CharacterStatOption(2,1,1),new CharacterStatOption(3,2,1));
            GameObservation withStats=Build(Prep(),Econ(),shop:CharacterShop("v10-stats"),defs:new[]{definition},characterStatSettings:stats);
            GameObservation withoutStats=Build(Prep(),Econ(),shop:CharacterShop("v10-stats"),defs:new[]{definition});
            HeuristicStrategy strategy=HeuristicStrategy.CreateV10();
            double Score(GameObservation observation,string id)=>strategy.GetEvaluations(observation,new ActionId(id))
                .First(x=>x.Action is BuyCharacterAction buy&&buy.Placement.Kind==CharacterLocationKind.Hand).Score;
            Assert.That(Score(withStats,"v10-stats-present")-Score(withoutStats,"v10-stats-absent"),
                Is.EqualTo(0.5*(3.4-3.5)).Within(1e-12));
            var rng=new StrategyRngState(0,strategy.StrategyId,strategy.Version,new Pcg32State(1,123,5));
            StrategyDecision first=strategy.ChooseAction(withStats,Context(strategy,"v10-choice-a",rng:rng));
            StrategyDecision repeated=strategy.ChooseAction(withStats,Context(strategy,"v10-choice-a",rng:rng));
            Assert.That(first.Action.Kind,Is.EqualTo(repeated.Action.Kind));
            Assert.That(first.NextState.Payload,Is.EqualTo(repeated.NextState.Payload));
            Assert.That(first.NextStrategyRngState,Is.EqualTo(rng));
            Assert.That(first.StrategyRngAdvanced,Is.False);
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

        [Test] public void OverLevelBoardUsesEmptyHandSlotBeforeOrdinaryHeuristicScoring()
        {
            var a=Def("board-a");var b=Def("board-b");var ia=Inst("board-a1",a);var ib=Inst("board-b1",b);
            var board=Put(Put(BoardState.CreateStandard(),0,ia.InstanceId),1,ib.InstanceId);
            var hand=HandState.CreateStandard();
            var obs=Build(Prep(),Econ(level:1),board,hand,new[]{ia,ib},new[]{a,b});
            var strategy=new HeuristicStrategy();
            var decision=strategy.ChooseAction(obs,Context(strategy,"relief-empty-hand"));
            Assert.That(decision.Action,Is.TypeOf<MoveBoardToHandAction>());
            Assert.That(decision.Diagnostic,Does.Contain("empty Hand slot"));
        }

        [Test] public void OverLevelBoardWithFullHandSellsOnlyBoardCharacterWithLeastVisibleLoss()
        {
            var definitions=Enumerable.Range(0,14).Select(i=>Def("over-"+i)).ToArray();
            var instances=definitions.Select((d,i)=>Inst("over-instance-"+i,d)).ToArray();
            var board=BoardState.CreateStandard();for(int i=0;i<9;i++)board=Put(board,i,instances[i].InstanceId);
            var hand=HandState.CreateStandard();for(int i=0;i<5;i++)hand=Put(hand,i,HandSlotContent.Character(instances[i+9].InstanceId));
            var observation=Build(Prep(),Econ(gold:7,level:6),board,hand,instances,definitions);
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(normalExperienceGoldCost:4));

            var decision=strategy.ChooseAction(observation,Context(strategy,"over-full-hand"));
            Assert.That(decision.Action,Is.TypeOf<SellCharacterAction>());
            string sold=((SellCharacterAction)decision.Action).InstanceId;
            Assert.That(instances.Take(9).Any(x=>x.InstanceId==sold),Is.True,
                "Selling a Hand character would not reduce the Board/Level excess.");
            Assert.That(decision.Diagnostic,Does.Contain("smallest visible power/synergy loss"));
        }

        [Test] public void OverLevelBoardPrefersOnlyAnXpPurchaseThatImmediatelyRaisesLevel()
        {
            var definitions=Enumerable.Range(0,9).Select(i=>Def("xp-over-"+i)).ToArray();
            var instances=definitions.Select((d,i)=>Inst("xp-over-instance-"+i,d)).ToArray();
            var board=BoardState.CreateStandard();for(int i=0;i<4;i++)board=Put(board,i,instances[i].InstanceId);
            var hand=HandState.CreateStandard();for(int i=4;i<9;i++)hand=Put(hand,i-4,HandSlotContent.Character(instances[i].InstanceId));
            var economy=new EconomyState(7,1,2,12,0,1,1,20,30);
            var observation=Build(Prep(),economy,board,hand,instances,definitions);
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(normalExperienceGoldCost:4));
            var decision=strategy.ChooseAction(observation,Context(strategy,"xp-relief"));
            Assert.That(decision.Action,Is.TypeOf<BuyExperienceAction>());
            Assert.That(decision.Diagnostic,Does.Contain("immediately raises Level"));
        }

        [Test] public void Trial13KBoardNineLevelSixHandFiveIsReducedOnlyUntilBattleIsLegal()
        {
            var definitions=Enumerable.Range(0,14).Select(i=>Def("trial13k-"+i)).ToArray();
            var all=definitions.Select((d,i)=>Inst("trial13k-instance-"+i,d)).ToArray();
            BoardState board=BoardState.CreateStandard();for(int i=0;i<9;i++)board=Put(board,i,all[i].InstanceId);
            HandState hand=HandState.CreateStandard();for(int i=0;i<5;i++)hand=Put(hand,i,HandSlotContent.Character(all[i+9].InstanceId));
            var economy=new EconomyState(7,3,6,12,0,1,1,22,30);
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(normalExperienceGoldCost:4));
            int boardCount=9;
            for(int step=0;step<3;step++)
            {
                var observation=Build(Prep(),economy,board,hand,all,definitions);
                var decision=strategy.ChooseAction(observation,Context(strategy,"trial13k-"+step,step:step));
                Assert.That(decision.Action,Is.TypeOf<SellCharacterAction>(),"Full Hand and insufficient XP require one Board sale per excess unit.");
                var sale=(SellCharacterAction)decision.Action;
                CharacterInstance instance=all.Single(x=>x.InstanceId==sale.InstanceId);
                Assert.That(board.TryFindInstance(instance.InstanceId,out int slot),Is.True);
                Assert.That(board.TryWithCharacterAt(slot,null,out BoardState nextBoard),Is.True);
                board=nextBoard;all=all.Where(x=>x.InstanceId!=instance.InstanceId).ToArray();boardCount--;
                Assert.That(boardCount,Is.EqualTo(9-step-1));
            }
            var ready=Build(Prep(),economy,board,hand,all,definitions);
            Assert.That(boardCount,Is.EqualTo(6));
            Assert.That(strategy.ChooseAction(ready,Context(strategy,"trial13k-ready",step:3)).Action,
                Is.TypeOf<StartBattleAction>(),"Once the Board is within Level, normal ranking should start battle instead of selling more.");
        }

        [Test] public void BoardAtOrBelowLevelDoesNotEnterCleanupModeAndChoiceIsDeterministic()
        {
            var a=Def("at-level-a");var b=Def("at-level-b");var ia=Inst("at-level-ia",a);var ib=Inst("at-level-ib",b);
            var board=Put(Put(BoardState.CreateStandard(),0,ia.InstanceId),1,ib.InstanceId);
            var obs=Build(Prep(),Econ(level:2),board:board,owned:new[]{ia,ib},defs:new[]{a,b});
            var strategy=new HeuristicStrategy();var first=strategy.ChooseAction(obs,Context(strategy,"at-level-1"));
            var second=strategy.ChooseAction(obs,Context(strategy,"at-level-2"));
            Assert.That(first.Action,Is.TypeOf<StartBattleAction>());
            Assert.That(second.Action.Kind,Is.EqualTo(first.Action.Kind));
            Assert.That(second.NextState.Payload,Is.EqualTo(first.NextState.Payload));
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

        [Test] public void ConsumedEventRequiresEndEventAndHeuristicSelectsItEvenWithEligibleTargets()
        {
            var cfg=Config(1,100);var prep=Prep(cfg);var battle=StageProgressRules.BeginBattle(prep).NextState;
            var pending=StageProgressRules.ResolveBattle(battle,cfg,true).NextState;
            var ev=StageProgressRules.CompletePendingReward(pending,cfg,new SimulationRandomStreams(17).Event.CaptureState()).NextState;
            var d=Def("event-hero");var hero=Inst("event-hero-instance",d);
            var board=Put(BoardState.CreateStandard(),0,hero.InstanceId);
            var item=new ItemDefinition("star","Star",0,"",ItemEffectTarget.OneAlly,ItemEffectDuration.Immediate,
                new[]{new ItemEffectDefinition(ItemEffectKind.Star,2)});
var available=Build(ev,board:board,owned:new[]{hero},defs:new[]{d},items:new[]{item},
                eventItem:new EventItemState(item.DefinitionId,EventItemStatus.Available));
            var availableStrategy=new HeuristicStrategy();
            Assert.That(availableStrategy.ChooseAction(available,Context(availableStrategy,"available-event")).Action,
                Is.TypeOf<UseEventItemAction>());
            var obs=Build(ev,board:board,owned:new[]{hero},defs:new[]{d},items:new[]{item},
                eventItem:new EventItemState(item.DefinitionId,EventItemStatus.Consumed));
            var strategy=new HeuristicStrategy();
            var evaluations=strategy.GetEvaluations(obs,new ActionId("consumed-event"));
            Assert.That(evaluations,Has.Count.EqualTo(1));
            Assert.That(evaluations[0].Action,Is.TypeOf<EndEventAction>());
            var decision=strategy.ChooseAction(obs,Context(strategy,"consumed-event"));
            Assert.That(decision.Action,Is.TypeOf<EndEventAction>());
        }
        [Test] public void ForcedBattleOccursAtPreparationDecisionLimitOnlyWhenItIsLegal()
        {
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(preparationDecisionLimit:2));var obs=Build(Prep());
            var state=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,"hs2|1|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Empty)));
            var decision=strategy.ChooseAction(obs,Context(strategy,"limit",state:state));
            Assert.That(decision.Action,Is.TypeOf<StartBattleAction>());
            var x=Def("x");var y=Def("y");var ix=Inst("x",x);var iy=Inst("y",y);
            var over=Build(Prep(),Econ(level:1),board:Put(Put(BoardState.CreateStandard(),0,"x"),1,"y"),
                owned:new[]{ix,iy},defs:new[]{x,y});
            Assert.That(over.IsStrategyActionAllowed,Is.True);
            var cannotStart=strategy.ChooseAction(over,Context(strategy,"no-start",state:state));
            Assert.That(cannotStart.Action,Is.Not.TypeOf<StartBattleAction>());
        }

        private static StageProgressState EnemylessPreparation(StageChoice choice)
        {
            var cfg=Config(1);
            var state=StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(cfg),cfg).NextState;
            return StageSelectionRules.Select(state,cfg,choice).NextState;
        }

        [TestCase(StageChoice.SelectB)]
        [TestCase(StageChoice.SelectD)]
        public void EnemylessPreparationReturnsNoActionAtHeuristicLimitEvenWhenBoardNeedsCleanup(StageChoice choice)
        {
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(preparationDecisionLimit:2));
            var a=Def("enemyless-a");var b=Def("enemyless-b");var ia=Inst("enemyless-ia",a);var ib=Inst("enemyless-ib",b);
            var board=Put(Put(BoardState.CreateStandard(),0,ia.InstanceId),1,ib.InstanceId);
            var observation=Build(EnemylessPreparation(choice),Econ(level:1),board:board,owned:new[]{ia,ib},defs:new[]{a,b});
            var state=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,"hs2|1|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Empty)));
            var decision=strategy.ChooseAction(observation,Context(strategy,"enemyless-limit",state:state));
            Assert.That(decision.HasAction,Is.False);
            Assert.That(decision.Diagnostic,Does.Contain("enemyless stage"));
        }

        [Test] public void NewEncounterResetsPreparationLimitAndDoesNotFilterStartBattleFromPreviousEncounter()
        {
            var cfg=Config(5,0);
            var prep0=StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(cfg),cfg).NextState;
            var battle=StageProgressRules.BeginBattle(prep0).NextState;
            var pending=StageProgressRules.ResolveBattle(battle,cfg,true).NextState;
            var advanced=StageProgressRules.CompletePendingReward(pending,cfg,default(Pcg32State));
            Assert.That(advanced.Succeeded,Is.True);
            var prep1=StageProgressRules.BeginCurrentEncounter(advanced.NextState,cfg).NextState;
            Assert.That(prep1.EncounterIndex,Is.EqualTo(1));

            var definition=Def("next-encounter");var instance=Inst("next-encounter-1",definition);
            var board=Put(BoardState.CreateStandard(),0,instance.InstanceId);
            var observation=Build(prep1,Econ(gold:0,level:1),board:board,owned:new[]{instance},defs:new[]{definition});
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(preparationDecisionLimit:1));
            // Previous encounter ended with StartBattle and reached its decision cap.
            var prior=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,
                "hs2|30|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("15")));
            string trace=null;
            var tracedStrategy=new HeuristicStrategy(new HeuristicStrategySettings(preparationDecisionLimit:1),value=>trace=value);
            var decision=tracedStrategy.ChooseAction(observation,Context(tracedStrategy,"next-encounter",state:prior));

            Assert.That(decision.Action,Is.TypeOf<StartBattleAction>());
            Assert.That(decision.NextState.Payload,Does.StartWith("hs2|1|1|"));
            Assert.That(trace,Does.Contain("CounterReset=True"));
            Assert.That(trace,Does.Contain("StartBattle eligibilityFromObservation=True generated=True filteredOut=False"));
            Assert.That(trace,Does.Contain("fallback=True"));
        }

        [Test] public void DecisionDiagnosticsAreNonInvasiveAndRecordActualCandidateEvaluation()
        {
            var definition=Def("diagnostic");var instance=Inst("diagnostic-1",definition);
            var observation=Build(Prep(),Econ(gold:0,level:1),board:Put(BoardState.CreateStandard(),0,instance.InstanceId),
                owned:new[]{instance},defs:new[]{definition});
            string trace=null;
            var plain=new HeuristicStrategy();var traced=new HeuristicStrategy(null,value=>trace=value);
            var plainResult=plain.ChooseAction(observation,Context(plain,"diagnostic-action"));
            var tracedResult=traced.ChooseAction(observation,Context(traced,"diagnostic-action"));
            Assert.That(tracedResult.Action.Kind,Is.EqualTo(plainResult.Action.Kind));
            Assert.That(tracedResult.NextState.Payload,Is.EqualTo(plainResult.NextState.Payload));
            Assert.That(tracedResult.NextStrategyRngState,Is.EqualTo(plainResult.NextStrategyRngState));
            Assert.That(trace,Does.Contain("VisibleObservationFingerprint="));
            Assert.That(trace,Does.Contain("GeneratedCandidateCount="));
            Assert.That(trace,Does.Contain("Candidate generated=StartBattle"));
            Assert.That(trace,Does.Contain("FinalAction="));
        }

        [Test] public void LegalStartBattleIsFallbackWhenEveryPreparationScoreIsNonPositive()
        {
            var definition=Def("fallback-ready");var instance=Inst("fallback-ready-1",definition);
            var board=Put(BoardState.CreateStandard(),0,instance.InstanceId);
            var observation=Build(Prep(),Econ(gold:0,level:1),board:board,owned:new[]{instance},defs:new[]{definition});
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(startBattleValue:-1000));
            var evaluations=strategy.GetEvaluations(observation,new ActionId("negative-start"));
            Assert.That(evaluations.Single(x=>x.Action is StartBattleAction).Score,Is.LessThanOrEqualTo(0));
            var decision=strategy.ChooseAction(observation,Context(strategy,"negative-start-choice"));
            Assert.That(decision.Action,Is.TypeOf<StartBattleAction>());
            Assert.That(decision.Diagnostic,Does.Contain("existing legal StartBattle"));
        }

        [Test] public void AtPreparationLimitOverLevelRepairPrecedesBattleThenBattlePrecedesNormalActions()
        {
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(preparationDecisionLimit:2));
            var a=Def("repair-a");var b=Def("repair-b");var ia=Inst("repair-ia",a);var ib=Inst("repair-ib",b);
            var board=Put(Put(BoardState.CreateStandard(),0,ia.InstanceId),1,ib.InstanceId);
            var observation=Build(Prep(),Econ(gold:0,level:1),board:board,owned:new[]{ia,ib},defs:new[]{a,b});
            var prior=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,"hs2|1|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Empty)));
            var repair=strategy.ChooseAction(observation,Context(strategy,"repair-at-limit",state:prior));
            Assert.That(repair.Action,Is.TypeOf<MoveBoardToHandAction>());
            var move=(MoveBoardToHandAction)repair.Action;
            BoardState nextBoard=BoardState.CreateStandard();
            int remainingSlot=move.BoardSlot==0?1:0;
            CharacterInstance remaining=move.BoardSlot==0?ib:ia;
            Assert.That(nextBoard.TryWithCharacterAt(remainingSlot,remaining.InstanceId,out nextBoard),Is.True);
            HandState nextHand=Put(HandState.CreateStandard(),move.HandSlot,HandSlotContent.Character(
                move.BoardSlot==0?ia.InstanceId:ib.InstanceId));
            var nextObservation=Build(Prep(),Econ(gold:0,level:1),board:nextBoard,hand:nextHand,
                owned:new[]{ia,ib},defs:new[]{a,b});
            var finish=strategy.ChooseAction(nextObservation,Context(strategy,"battle-after-repair",state:repair.NextState));
            Assert.That(finish.Action,Is.TypeOf<StartBattleAction>());
            Assert.That(finish.Diagnostic,Does.Contain("Preparation decision limit reached"));
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
            var state=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,"hs2|0|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Empty)));
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

        [Test] public void EmptyBoardWithAffordableCharacterBuysForBoardBeforeStartingBattle()
        {
            var strategy=new HeuristicStrategy();var d=Def("first");
            var obs=Build(Prep(),Econ(gold:7,level:1),shop:CharacterShop("first",1),defs:new[]{d});
            var evaluations=strategy.GetEvaluations(obs,new ActionId("empty-buy"));
            Assert.That(evaluations[0].Action,Is.TypeOf<BuyCharacterAction>());
            Assert.That(((BuyCharacterAction)evaluations[0].Action).Placement.Kind,Is.EqualTo(CharacterLocationKind.Board));
            Assert.That(evaluations.Single(x=>x.Action is StartBattleAction).Score,Is.LessThan(evaluations[0].Score));
        }

        [Test] public void EmptyBoardDeploysHandCharacterBeforeConsideringShopOrBattle()
        {
            var strategy=new HeuristicStrategy();var d=Def("hand");var other=Def("another");var i=Inst("hand1",d);
            var obs=Build(Prep(),Econ(gold:7,level:1),hand:Put(HandState.CreateStandard(),0,HandSlotContent.Character("hand1")),
                owned:new[]{i},defs:new[]{d,other},shop:CharacterShop("another",1));
            var decision=strategy.ChooseAction(obs,Context(strategy,"empty-hand"));
            Assert.That(decision.Action,Is.TypeOf<MoveHandToBoardAction>());
            Assert.That(decision.Diagnostic,Does.Contain("Deploys an available Hand character"));
        }

        [Test] public void UnderLevelBoardPrioritizesAvailableHandDeployment()
        {
            var strategy=new HeuristicStrategy();var a=Def("board");var b=Def("reserve");var ia=Inst("ia",a);var ib=Inst("ib",b);
            var obs=Build(Prep(),Econ(level:2),Put(BoardState.CreateStandard(),0,"ia"),
                Put(HandState.CreateStandard(),0,HandSlotContent.Character("ib")),new[]{ia,ib},new[]{a,b});
            Assert.That(strategy.ChooseAction(obs,Context(strategy,"under-level")).Action,Is.TypeOf<MoveHandToBoardAction>());
        }

        [Test] public void FullLevelBoardWithoutStrengtheningOptionsStartsBattle()
        {
            var strategy=new HeuristicStrategy();var d=Def("ready");var i=Inst("ready1",d);
            var obs=Build(Prep(),Econ(gold:0,level:1),Put(BoardState.CreateStandard(),0,"ready1"),
                HandState.CreateStandard(),new[]{i},new[]{d});
            Assert.That(strategy.ChooseAction(obs,Context(strategy,"ready")).Action,Is.TypeOf<StartBattleAction>());
        }

        [Test] public void EmptyBoardWithNoAffordablePurchaseOrHandCharacterStartsBattle()
        {
            var strategy=new HeuristicStrategy();var d=Def("costly");
            var obs=Build(Prep(),Econ(gold:0,level:1),shop:CharacterShop("costly",5),defs:new[]{d});
            Assert.That(strategy.GetEvaluations(obs,new ActionId("cannot-buy")).Any(x=>x.Action is BuyCharacterAction),Is.False);
            Assert.That(strategy.ChooseAction(obs,Context(strategy,"no-strengthening")).Action,Is.TypeOf<StartBattleAction>());
        }

        [Test] public void OverLevelBoardPrefersLegalReliefAndNeverAddsAnotherUnit()
        {
            var strategy=new HeuristicStrategy();var a=Def("a");var b=Def("b");var c=Def("c");
            var ia=Inst("ia",a);var ib=Inst("ib",b);var ic=Inst("ic",c);
            var board=Put(Put(BoardState.CreateStandard(),0,"ia"),1,"ib");
            var obs=Build(Prep(),Econ(level:1),board,Put(HandState.CreateStandard(),0,HandSlotContent.Character("ic")),
                new[]{ia,ib,ic},new[]{a,b,c});
            var decision=strategy.ChooseAction(obs,Context(strategy,"over-level-adjust"));
            Assert.That(decision.Action,Is.TypeOf<MoveBoardToHandAction>());
        }

        [Test] public void ImmediatelyPurchasedCharacterIsNotSoldOnNextDecision()
        {
            var strategy=new HeuristicStrategy();var d=Def("bought");var i=Inst("bought1",d);
            var board=Put(BoardState.CreateStandard(),0,"bought1");
            var shop=new ShopState(ShopKind.Character,new[]{new ShopOffer("offer",ShopOfferKind.Character,
                "bought",1,1,ShopOfferStatus.Purchased)},1,1,1);
            var obs=Build(Prep(),Econ(gold:0,level:1),board,HandState.CreateStandard(),new[]{i},new[]{d},shop);
            string payload="hs2|1|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("2:offer:0:0"));
            var state=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,payload);
            var decision=strategy.ChooseAction(obs,Context(strategy,"no-flip",state:state));
            Assert.That(decision.Action,Is.TypeOf<StartBattleAction>());
        }

        [Test] public void ImmediateCharacterBuybackIsSuppressedAfterSale()
        {
            var strategy=new HeuristicStrategy();var d=Def("kept");var next=Def("replacement");var i=Inst("kept1",d);
            var board=Put(BoardState.CreateStandard(),0,"kept1");var shop=CharacterShop("replacement",1);
            var obs=Build(Prep(),Econ(gold:20,level:1),board,HandState.CreateStandard(),new[]{i},new[]{d,next},shop);
            string payload="hs2|1|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("10:just-sold"));
            var state=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,payload);
            var decision=strategy.ChooseAction(obs,Context(strategy,"no-buyback",state:state));
            Assert.That(decision.Action,Is.Not.TypeOf<BuyCharacterAction>());
        }

        [Test] public void CrossBoardHandMoveDoesNotImmediatelyReverse()
        {
            var strategy=new HeuristicStrategy();var a=Def("a");var b=Def("b");var ia=Inst("ia",a);var ib=Inst("ib",b);
            var board=Put(BoardState.CreateStandard(),0,"ia");var hand=Put(HandState.CreateStandard(),0,HandSlotContent.Character("ib"));
            var obs=Build(Prep(),Econ(level:2),board,hand,new[]{ia,ib},new[]{a,b});
            string payload="hs2|1|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("9:0:1"));
            var state=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,payload);
            var decision=strategy.ChooseAction(obs,Context(strategy,"no-reverse",state:state));
            Assert.That(decision.Action,Is.Not.TypeOf<MoveBoardToHandAction>());
        }

        [Test] public void DecisionLimitForcesOnlyAvailableLegalBattleFinish()
        {
            var strategy=new HeuristicStrategy(new HeuristicStrategySettings(preparationDecisionLimit:2));
            var obs=Build(Prep(),Econ(gold:0,level:1),board:BoardState.CreateStandard());
            var state=new StrategyState(strategy.StrategyId,strategy.Version,HeuristicStrategy.StateSchemaVersion,"hs2|1|0|"+((int)ObservationPhase.Preparation).ToString()+"|"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Empty)));
            Assert.That(strategy.ChooseAction(obs,Context(strategy,"limit-finish",state:state)).Action,Is.TypeOf<StartBattleAction>());
        }

        [Test] public void HeuristicDecisionIsDeterministicAndDoesNotConsumeStrategyRng()
        {
            var strategy=new HeuristicStrategy();var d=Def("first");
            var obs=Build(Prep(),Econ(gold:7,level:1),shop:CharacterShop("first",1),defs:new[]{d});
            var rng=StrategyRandomSource.Create(12345,Sid,Ver).CaptureState();
            var first=strategy.ChooseAction(obs,Context(strategy,"deterministic",rng:rng));
            var second=strategy.ChooseAction(obs,Context(strategy,"deterministic",rng:rng));
            Assert.That(first.Action.Kind,Is.EqualTo(second.Action.Kind));
            Assert.That(first.NextStrategyRngState,Is.EqualTo(rng));
            Assert.That(first.StrategyRngAdvanced,Is.False);
        }

        [Test] public void HeuristicCandidateGenerationDoesNotChangeRandomStrategy()
        {
            var d=Def("first");var obs=Build(Prep(),Econ(gold:7,level:1),shop:CharacterShop("first",1),defs:new[]{d});
            var id=new ActionId("same-candidates");
            var before=new RandomStrategy().GetCandidates(obs,id).Select(x=>x.Kind+":"+x.ActionId.Value).ToArray();
            _=new HeuristicStrategy().ChooseAction(obs,Context(new HeuristicStrategy(),"heuristic-only"));
            var after=new RandomStrategy().GetCandidates(obs,id).Select(x=>x.Kind+":"+x.ActionId.Value).ToArray();
            Assert.That(after,Is.EqualTo(before));
        }

        [Test] public void BoardRelatedEvaluationPreservesLegacyScoresAndReusesBaseBoardQuality()
        {
            var definitions=new[]
            {
                Def("board-a",new[]{"Fire"},new[]{"UnitA"}),
                Def("board-b",new[]{"Fire"},new[]{"UnitA"}),
                Def("board-c",new[]{"Water"},new[]{"UnitA","UnitB"}),
                Def("board-d",new[]{"Water"},new[]{"UnitB"}),
                Def("hand-a",new[]{"Fire"},new[]{"UnitB"}),
                Def("hand-b",new[]{"Air"},new[]{"UnitA"})
            };
            var instances=definitions.Select((d,i)=>Inst("eval-"+i,d,i==2?2:1,i%3+1)).ToArray();
            BoardState board=BoardState.CreateStandard();
            board=Put(Put(Put(Put(board,0,"eval-0"),1,"eval-1"),4,"eval-2"),8,"eval-3");
            HandState hand=HandState.CreateStandard();
            hand=Put(Put(hand,0,HandSlotContent.Character("eval-4")),1,HandSlotContent.Character("eval-5"));
            var observation=Build(Prep(),Econ(level:5),board,hand,instances,definitions);
            var settings=new HeuristicStrategySettings();
            var profiler=new SimulationProfileCollector();
            var strategy=new HeuristicStrategy(settings,null,profiler);
            IReadOnlyList<HeuristicActionEvaluation> actual=strategy.GetEvaluations(observation,new ActionId("board-ref"));
            var boardActions=actual.Where(x=>x.Action is MoveOrSwapBoardAction||x.Action is MoveBoardToHandAction||
                x.Action is MoveHandToBoardAction||x.Action is SellCharacterAction).ToArray();

            Assert.That(boardActions.Any(x=>x.Action is MoveOrSwapBoardAction),Is.True);
            Assert.That(boardActions.Any(x=>x.Action is MoveBoardToHandAction),Is.True);
            Assert.That(boardActions.Any(x=>x.Action is MoveHandToBoardAction),Is.True);
            Assert.That(boardActions.Any(x=>x.Action is SellCharacterAction),Is.True);
            foreach(HeuristicActionEvaluation evaluation in boardActions)
            {
                Tuple<double,string> expected=LegacyBoardEvaluation(observation,evaluation.Action,settings);
                Assert.That(evaluation.Score,Is.EqualTo(expected.Item1),"Legacy score for "+LegacyBoardSignature(evaluation.Action));
                Assert.That(evaluation.Reason,Is.EqualTo(expected.Item2),"Legacy reason for "+LegacyBoardSignature(evaluation.Action));
            }
            string[] expectedOrder=boardActions.OrderByDescending(x=>LegacyBoardEvaluation(observation,x.Action,settings).Item1)
                .ThenBy(x=>LegacyBoardSignature(x.Action),StringComparer.Ordinal).Select(x=>LegacyBoardSignature(x.Action)).ToArray();
            Assert.That(boardActions.Select(x=>LegacyBoardSignature(x.Action)).ToArray(),Is.EqualTo(expectedOrder),
                "The relative order of Board-related evaluations must remain unchanged.");

            var sections=profiler.Sections.ToDictionary(x=>x.SectionId,StringComparer.Ordinal);
            Assert.That(sections["Strategy.BoardEvaluation.BaseQuality"].CallCount,Is.EqualTo(1),
                "The unchanged Board quality is calculated once per GetEvaluations call.");
            int expectedVirtualCalls=boardActions.Count(x=>x.Action is MoveOrSwapBoardAction||x.Action is MoveBoardToHandAction||
                x.Action is MoveHandToBoardAction||x.Action is SellCharacterAction sale&&
                observation.Characters.Any(c=>c.InstanceId==sale.InstanceId&&c.LocationKind==ObservationLocationKind.Board));
            Assert.That(sections["Strategy.BoardEvaluation.VirtualQuality"].CallCount,Is.EqualTo(expectedVirtualCalls),
                "Each Board-changing candidate requires one virtual quality calculation, with no repeated baseline calculation.");
        }

        private static Tuple<double,string> LegacyBoardEvaluation(GameObservation o,GameAction action,HeuristicStrategySettings settings)
        {
            CharacterObservation[] before=LegacyBoardCharacters(o.Board);
            double baseQuality=LegacyBoardQuality(before,o.Board.Columns,o.Synergy,settings);
            if(action is MoveOrSwapBoardAction move)
            {
                if(move.SourceSlot>=before.Length||move.TargetSlot>=before.Length)
                    return Tuple.Create(settings.NoImprovementPenalty,"Board slot is outside the observation.");
                var after=(CharacterObservation[])before.Clone();CharacterObservation tmp=after[move.SourceSlot];after[move.SourceSlot]=after[move.TargetSlot];after[move.TargetSlot]=tmp;
                double delta=LegacyBoardQuality(after,o.Board.Columns,o.Synergy,settings)-baseQuality;
                return Tuple.Create(delta>0?delta*settings.BoardImprovementValue:settings.NoImprovementPenalty,
                    delta>0?"Relocation improves visible power or synergy placement.":"No observable improvement; avoids arbitrary moves.");
            }
            if(action is MoveBoardToHandAction b2h)return LegacyCrossEvaluation(o,before,baseQuality,b2h.BoardSlot,b2h.HandSlot,true,settings);
            if(action is MoveHandToBoardAction h2b)return LegacyCrossEvaluation(o,before,baseQuality,h2b.BoardSlot,h2b.HandSlot,false,settings);
            if(action is SellCharacterAction sale)
            {
                CharacterObservation c=o.Characters.FirstOrDefault(x=>x.InstanceId==sale.InstanceId);
                if(c==null)return Tuple.Create(settings.NoImprovementPenalty,"Character is not publicly owned.");
                int copies=c.Star==1?1:c.Star==2?3:c.Star==3?9:1;int proceeds=c.RunCost*copies;
                double lost=(c.Attack*settings.AttackValue+c.Defense*settings.DefenseValue+Math.Max(0,c.Star-1)*3)*
                    (c.LocationKind==ObservationLocationKind.Board?1:.35);
                if(c.LocationKind==ObservationLocationKind.Board)
                {var after=LegacyBoardCharacters(o.Board);after[c.SlotIndex]=null;lost+=Math.Max(0,baseQuality-LegacyBoardQuality(after,o.Board.Columns,o.Synergy,settings));}
                double gold=Math.Min(proceeds,Math.Max(0,settings.GoldReserve-o.Player.Gold))*settings.SellGoldValuePerGold;
                if(o.Player.Gold<settings.GoldReserve)gold+=proceeds*settings.SellGoldValuePerGold*.25;
                return Tuple.Create(gold-settings.CharacterSaleLossPenalty-lost,"Weighs visible sale Gold against the character and synergy value lost.");
            }
            Assert.Fail("Unexpected Board evaluation action: "+action.Kind);
            return null;
        }

        private static Tuple<double,string> LegacyCrossEvaluation(GameObservation o,CharacterObservation[] before,double baseQuality,
            int boardSlot,int handSlot,bool boardToHand,HeuristicStrategySettings settings)
        {
            if(boardSlot>=before.Length)return Tuple.Create(settings.NoImprovementPenalty,"Slot is outside the observation.");
            HandSlotObservation hand=o.Hand.Slots.FirstOrDefault(x=>x.SlotIndex==handSlot);
            CharacterObservation moving=hand!=null&&hand.Kind==HandSlotContentKind.Character?hand.Character:null;
            var after=(CharacterObservation[])before.Clone();
            if(boardToHand)after[boardSlot]=moving;
            else if(moving!=null)after[boardSlot]=moving;
            else return Tuple.Create(settings.NoImprovementPenalty,"Hand slot has no character.");
            int beforeCount=before.Count(c=>c!=null),afterCount=after.Count(c=>c!=null);
            double relief=Math.Max(0,beforeCount-o.Player.Level)-Math.Max(0,afterCount-o.Player.Level);
            double delta=(LegacyBoardQuality(after,o.Board.Columns,o.Synergy,settings)-baseQuality)*settings.BoardImprovementValue+
                relief*settings.OverLevelReliefValue;
            string reason=delta>0?"Transfer improves visible formation or reduces Board population above Level.":"No positive public placement value.";
            double score=delta>0?delta:settings.NoImprovementPenalty;
            if(!boardToHand)
            {
                bool duplicate=moving!=null&&o.Board.Slots.Any(x=>x.SlotIndex!=boardSlot&&x.Character!=null&&
                    string.Equals(x.Character.DefinitionId,moving.DefinitionId,StringComparison.Ordinal));
                if(duplicate){score=settings.NoImprovementPenalty;reason="Would create a duplicate Definition ID on Board; Dispatcher would reject the move.";}
                else if(o.Board.Slots.Any(x=>x.SlotIndex==boardSlot&&x.Character==null)&&beforeCount>=o.Player.Level)
                {score=settings.NoImprovementPenalty;reason="Would exceed the public Level deployment capacity; keep the character in Hand.";}
                else if(o.Board.Slots.Any(x=>x.SlotIndex==boardSlot&&x.Character==null))
                {score+=settings.HandDeploymentPriority;reason+=" Deploys an available Hand character into unused Level capacity.";}
            }
            return Tuple.Create(score,reason);
        }

        private static CharacterObservation[] LegacyBoardCharacters(BoardObservation board)
        {var result=new CharacterObservation[board.Slots.Count];foreach(BoardSlotObservation slot in board.Slots)if(slot.SlotIndex>=0&&slot.SlotIndex<result.Length)result[slot.SlotIndex]=slot.Character;return result;}
        private static double LegacyBoardQuality(CharacterObservation[] board,int columns,SynergyObservation synergy,HeuristicStrategySettings settings)
        {
            double score=board.Where(c=>c!=null).Sum(c=>c.Attack*settings.AttackValue+c.Defense*settings.DefenseValue+Math.Max(0,c.Star-1)*3);
            var attrs=new Dictionary<string,List<int>>(StringComparer.Ordinal);var units=new Dictionary<string,List<int>>(StringComparer.Ordinal);
            for(int i=0;i<board.Length;i++)if(board[i]!=null)
            {foreach(string tag in board[i].Attributes.Distinct(StringComparer.Ordinal)){if(!attrs.TryGetValue(tag,out var list))attrs[tag]=list=new List<int>();list.Add(i);}
             foreach(string tag in board[i].Units.Distinct(StringComparer.Ordinal)){if(!units.TryGetValue(tag,out var list))units[tag]=list=new List<int>();list.Add(i);}}
            score+=attrs.Values.Count(v=>v.Count>=2)*settings.AttributeSharedValue;
            foreach(var pair in units.OrderBy(x=>x.Key,StringComparer.Ordinal))score+=Math.Max(0,LegacyMaxComponent(pair.Value,board,pair.Key,columns)-1)*settings.UnitPartialValue;
            if(synergy!=null&&synergy.IsAvailable)foreach(SynergyUnitObservation unit in synergy.Units)
            {var slots=new List<int>();foreach(string id in unit.ParticipantInstanceIds){int slot=Array.FindIndex(board,c=>c!=null&&c.InstanceId==id);if(slot<0){slots.Clear();break;}slots.Add(slot);}if(slots.Count>=2&&slots.Count==unit.ParticipantInstanceIds.Count&&LegacyConnected(slots,columns))score+=settings.UnitEstablishedValue;}
            return score;
        }
        private static int LegacyMaxComponent(List<int> slots,CharacterObservation[] board,string unit,int columns)
        {
            var remaining=new HashSet<int>(slots);int max=0;
            while(remaining.Count>0){int first=remaining.Min(),count=0;remaining.Remove(first);var queue=new Queue<int>();queue.Enqueue(first);
                while(queue.Count>0){int current=queue.Dequeue();count++;foreach(int next in LegacyNeighbors(current,columns,board.Length))if(remaining.Contains(next)&&board[next].Units.Contains(unit)){remaining.Remove(next);queue.Enqueue(next);}}
                max=Math.Max(max,count);}
            return max;
        }
        private static bool LegacyConnected(List<int> slots,int columns)
        {
            var remaining=new HashSet<int>(slots);var queue=new Queue<int>();int first=remaining.First();remaining.Remove(first);queue.Enqueue(first);
            while(queue.Count>0)foreach(int next in LegacyNeighbors(queue.Dequeue(),columns,int.MaxValue))if(remaining.Remove(next))queue.Enqueue(next);
            return remaining.Count==0;
        }
        private static IEnumerable<int> LegacyNeighbors(int slot,int columns,int count)
        {int col=slot%columns;if(col>0)yield return slot-1;if(col+1<columns&&slot+1<count)yield return slot+1;if(slot-columns>=0)yield return slot-columns;if(slot+columns<count)yield return slot+columns;}
        private static string LegacyBoardSignature(GameAction action)
        {if(action is MoveOrSwapBoardAction b)return "6:"+b.SourceSlot+":"+b.TargetSlot;if(action is MoveBoardToHandAction b2h)return "8:"+b2h.BoardSlot+":"+b2h.HandSlot;if(action is MoveHandToBoardAction h2b)return "9:"+h2b.HandSlot+":"+h2b.BoardSlot;if(action is SellCharacterAction sale)return "10:"+sale.InstanceId;return ((int)action.Kind).ToString();}

        [Test] public void HeuristicDoesNotEvaluateMoveHandToBoardThatDuplicatesDefinition()
        {
            var definition=Def("canonical");
            var boardCharacter=Inst("board-copy",definition,1);
            var handCharacter=Inst("hand-copy-star2",definition,2);
            var board=BoardState.CreateStandard();
            Assert.That(board.TryWithCharacterAt(0,boardCharacter.InstanceId,out board),Is.True);
            var hand=HandState.CreateStandard();
            Assert.That(hand.TryWithSlot(0,HandSlotContent.Character(handCharacter.InstanceId),out hand),Is.True);
            var observation=Build(Prep(),Econ(gold:0,level:3),board:board,hand:hand,
                owned:new[]{boardCharacter,handCharacter},defs:new[]{definition});
            var strategy=new HeuristicStrategy();
            var evaluations=strategy.GetEvaluations(observation,new ActionId("no-duplicate-evaluation"));

            Assert.That(evaluations.Any(x=>x.Action is MoveHandToBoardAction move && move.HandSlot==0 && move.BoardSlot==1),Is.False,
                "Heuristic shares RandomStrategy candidate generation and must not score a duplicate-producing move.");
            Assert.That(evaluations.Any(x=>x.Action is MoveHandToBoardAction move && move.HandSlot==0 && move.BoardSlot==0),Is.True,
                "A same-Definition swap remains available because the Board copy leaves in the same operation.");
        }

        [Test]
        public void CharacterPowerEvaluatorSeparatesStarAndTemporaryBonusChanges()
        {
            const int attack = 3, defense = 2, temporaryAttack = 2, temporaryDefense = 1, star = 2;
            double legacy = CharacterPowerEvaluator.Evaluate(attack, defense, temporaryAttack, temporaryDefense,
                star, 1.0, 0.6, 3.0, false);
            double starOnly = CharacterPowerEvaluator.Evaluate(attack, defense, temporaryAttack, temporaryDefense,
                star, 1.0, 0.6, 0.75, false);
            double bonusOnly = CharacterPowerEvaluator.Evaluate(attack, defense, temporaryAttack, temporaryDefense,
                star, 1.0, 0.6, 3.0, true);
            double v8 = CharacterPowerEvaluator.Evaluate(attack, defense, temporaryAttack, temporaryDefense,
                star, 1.0, 0.6, 0.75, true);

            Assert.That(legacy, Is.EqualTo(7.2).Within(1e-12));
            Assert.That(starOnly, Is.EqualTo(4.95).Within(1e-12));
            Assert.That(bonusOnly, Is.EqualTo(9.8).Within(1e-12));
            Assert.That(v8, Is.EqualTo(7.55).Within(1e-12));
        }

        [Test]
        public void CharacterPowerEvaluatorUsesV8StarTiersAndDoesNotOverflowAtIntBoundaries()
        {
            double one = CharacterPowerEvaluator.Evaluate(1, 2, 0, 0, 1, 1.0, 0.6, 0.75, true);
            double two = CharacterPowerEvaluator.Evaluate(1, 2, 0, 0, 2, 1.0, 0.6, 0.75, true);
            double three = CharacterPowerEvaluator.Evaluate(1, 2, 0, 0, 3, 1.0, 0.6, 0.75, true);
            double large = CharacterPowerEvaluator.Evaluate(int.MaxValue, int.MaxValue, int.MaxValue,
                int.MaxValue, 3, 1.0, 0.6, 0.75, true);
            Assert.That(one, Is.EqualTo(2.2).Within(1e-12));
            Assert.That(two, Is.EqualTo(2.95).Within(1e-12));
            Assert.That(three, Is.EqualTo(3.7).Within(1e-12));
            Assert.That(double.IsNaN(large) || double.IsInfinity(large), Is.False);
        }

        [Test]
        public void V8DiagnosticFingerprintTracksTemporaryBonusesWhileV7FingerprintRemainsLegacy()
        {
            CharacterDefinition definition = Def("fingerprint");
            CharacterInstance plain = Inst("fingerprint-instance", definition);
            BoardState plainBoard = Put(BoardState.CreateStandard(), 0, plain.InstanceId);
            GameObservation plainObservation = Build(Prep(), board:plainBoard, owned:new[]{plain}, defs:new[]{definition});

            CharacterInstance boosted = Inst("fingerprint-instance", definition);
            boosted.TemporaryAttackBonus = 2;
            BoardState boostedBoard = Put(BoardState.CreateStandard(), 0, boosted.InstanceId);
            GameObservation boostedObservation = Build(Prep(), board:boostedBoard, owned:new[]{boosted}, defs:new[]{definition});

            string Fingerprint(HeuristicStrategy strategy, GameObservation observation, string actionId)
            {
                string diagnostic = null;
                var traced = strategy.Version == "8"
                    ? HeuristicStrategy.CreateV8(strategy.Settings, value => diagnostic = value)
                    : strategy.Version == "9"
                        ? HeuristicStrategy.CreateV9(strategy.Settings, value => diagnostic = value)
                        : strategy.Version == "10"
                            ? HeuristicStrategy.CreateV10(strategy.Settings, value => diagnostic = value)
                        : strategy.Version == "11"
                            ? HeuristicStrategy.CreateV11(strategy.Settings, value => diagnostic = value)
                            : new HeuristicStrategy(strategy.Settings, value => diagnostic = value);
                traced.ChooseAction(observation, Context(traced, actionId));
                return diagnostic.Split(new[]{Environment.NewLine}, StringSplitOptions.None)
                    .Single(line => line.StartsWith("VisibleObservationFingerprint=", StringComparison.Ordinal));
            }

            Assert.That(Fingerprint(HeuristicStrategy.CreateV8(), plainObservation, "v8-plain"),
                Is.Not.EqualTo(Fingerprint(HeuristicStrategy.CreateV8(), boostedObservation, "v8-boosted")));
            Assert.That(Fingerprint(new HeuristicStrategy(), plainObservation, "v7-plain"),
                Is.EqualTo(Fingerprint(new HeuristicStrategy(), boostedObservation, "v7-boosted")));

            var statsA=Stats(new CharacterStatOption(2,1,1));var statsB=Stats(new CharacterStatOption(4,2,1));
            GameObservation shopA=Build(Prep(),shop:CharacterShop("fingerprint-offer"),defs:new[]{Def("fingerprint-offer")},characterStatSettings:statsA);
            GameObservation shopB=Build(Prep(),shop:CharacterShop("fingerprint-offer"),defs:new[]{Def("fingerprint-offer")},characterStatSettings:statsB);
            Assert.That(Fingerprint(HeuristicStrategy.CreateV9(),shopA,"v9-stat-a"),
                Is.Not.EqualTo(Fingerprint(HeuristicStrategy.CreateV9(),shopB,"v9-stat-b")));
            Assert.That(Fingerprint(HeuristicStrategy.CreateV8(),shopA,"v8-stat-a"),
                Is.EqualTo(Fingerprint(HeuristicStrategy.CreateV8(),shopB,"v8-stat-b")));

            CharacterDefinition handDefinition=Def("v10-fingerprint");
            CharacterInstance handOne=Inst("same-hand-instance",handDefinition,1,1);
            CharacterInstance handTwo=Inst("same-hand-instance",handDefinition,2,2);
            GameObservation handObsOne=Build(Prep(),hand:Put(HandState.CreateStandard(),0,HandSlotContent.Character(handOne.InstanceId)),
                owned:new[]{handOne},defs:new[]{handDefinition},shop:CharacterShop("v10-fingerprint"));
            GameObservation handObsTwo=Build(Prep(),hand:Put(HandState.CreateStandard(),0,HandSlotContent.Character(handTwo.InstanceId)),
                owned:new[]{handTwo},defs:new[]{handDefinition},shop:CharacterShop("v10-fingerprint"));
            Assert.That(Fingerprint(HeuristicStrategy.CreateV10(),handObsOne,"v10-hand-one"),
                Is.Not.EqualTo(Fingerprint(HeuristicStrategy.CreateV10(),handObsTwo,"v10-hand-two")));
            Assert.That(Fingerprint(HeuristicStrategy.CreateV9(),handObsOne,"v9-hand-one"),
                Is.EqualTo(Fingerprint(HeuristicStrategy.CreateV9(),handObsTwo,"v9-hand-two")),
                "Adding v10 fingerprint data must not alter the legacy v9 fingerprint.");

            CharacterDefinition withoutUnit=Def("v11-fingerprint-unit",units:Array.Empty<string>());
            CharacterDefinition withUnit=Def("v11-fingerprint-unit",units:new[]{"Sentinel"});
            CharacterInstance plainTagInstance=Inst("v11-fingerprint-instance",withoutUnit);
            CharacterInstance taggedInstance=Inst("v11-fingerprint-instance",withUnit);
            GameObservation withoutUnitObservation=Build(Prep(),board:Put(BoardState.CreateStandard(),0,plainTagInstance.InstanceId),
                owned:new[]{plainTagInstance},defs:new[]{withoutUnit});
            GameObservation withUnitObservation=Build(Prep(),board:Put(BoardState.CreateStandard(),0,taggedInstance.InstanceId),
                owned:new[]{taggedInstance},defs:new[]{withUnit});
            Assert.That(Fingerprint(HeuristicStrategy.CreateV11(),withoutUnitObservation,"v11-tag-plain"),
                Is.Not.EqualTo(Fingerprint(HeuristicStrategy.CreateV11(),withUnitObservation,"v11-tagged")),
                "v11 Board sale value depends on public Unit tags; v11 fingerprints include them.");
            Assert.That(Fingerprint(HeuristicStrategy.CreateV10(),withoutUnitObservation,"v10-tag-plain"),
                Is.EqualTo(Fingerprint(HeuristicStrategy.CreateV10(),withUnitObservation,"v10-tagged")),
                "The v11 fingerprint extension leaves the legacy v10 fingerprint unchanged.");
        }

        [Test]
        public void HeuristicV7AndV8HaveDistinctIdentitiesButShareStateSchema()
        {
            var v7 = new HeuristicStrategy();
            var v8 = HeuristicStrategy.CreateV8();
            Assert.That(v7.Version, Is.EqualTo("7"));
            Assert.That(v8.Version, Is.EqualTo("8"));
            Assert.That(v7.StrategyId, Is.EqualTo(v8.StrategyId));
            Assert.That(HeuristicStrategy.StateSchemaVersion, Is.EqualTo(2));
            Assert.That(v8.Settings.CharacterStarValue, Is.EqualTo(0.75));
            Assert.That(v8.Settings.IncludeTemporaryCharacterBonuses, Is.True);
            double persistentAlreadyInBase = CharacterPowerEvaluator.Evaluate(5, 3, 0, 0, 2, 1.0, 0.6, 0.75, true);
            double temporaryAppliedOnce = CharacterPowerEvaluator.Evaluate(3, 2, 2, 1, 2, 1.0, 0.6, 0.75, true);
            Assert.That(persistentAlreadyInBase, Is.EqualTo(temporaryAppliedOnce).Within(1e-12),
                "Persistent deltas are already in Attack/Defense; only temporary deltas are added.");
            Assert.Throws<ArgumentException>(() => new StrategyContext(v8.StrategyId, v8.Version, 0, 0,
                new ActionId("wrong-version"), StrategyState.EmptyFor(v8.StrategyId, v7.Version)));
        }

    }
}
