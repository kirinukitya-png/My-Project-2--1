using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
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
    public sealed class PlayerStrategyTests
    {
        private const string Sid = "example", Version = "1";
        private static StageProgressConfig Config(int total=25, int choice=5, int eventChance=0) =>
            new StageProgressConfig(total,5,choice,4,eventChance,0,eventChance,1,0,0,3,6,0,0,10,4,2,true);
        private static EconomyRulesConfiguration EconConfig() => new EconomyRulesConfiguration(4,2,1,
            new[]{1,2,3,4,5,6,7,8,9,10,11},10,1,3,3,1,0);
        private static EconomyState Economy() => new EconomyState(20,0,1,12,20,0,1,20,30);
        private static ObservationBuildResult Build(StageProgressState stage, StageProgressConfig cfg,
            ShopState shop=null, EventItemState eventItem=null, ItemDefinition[] items=null, BattleState battle=null) =>
            ObservationBuilder.Build(new ObservationSource(stage,cfg,Economy(),EconConfig(),BoardState.CreateStandard(),
                HandState.CreateStandard(),new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                Array.Empty<CharacterDefinition>(),Array.Empty<CharacterDefinition>(),Array.Empty<CharacterDefinition>(),
                items??Array.Empty<ItemDefinition>(),shop,new ItemEffectState(),eventItem,Array.Empty<EnemyDefinition>(),
                EnemyBoardState.CreateEmpty(),null,battle));
        private static StrategyContext Ctx(string action="t0/a0",long trial=0,long step=0,StrategyState state=null) =>
            new StrategyContext(Sid,Version,trial,step,new ActionId(action),state??StrategyState.EmptyFor(Sid,Version));

        private sealed class TestStrategy : IPlayerStrategy
        {
            private readonly Func<ActionId,GameAction> _factory;
            public string StrategyId=>Sid; public string Version=>PlayerStrategyTests.Version;
            public TestStrategy(Func<ActionId,GameAction> factory){_factory=factory;}
            public StrategyDecision ChooseAction(GameObservation observation,StrategyContext context)
            {
                if(!observation.IsStrategyActionAllowed) return context.NoAction();
                return context.Decide(_factory(context.NextActionId),context.State);
            }
        }

        [Test] public void StateAndContext_AreImmutableTrialLocalAndCheckpointable()
        {
            var state=new StrategyState(Sid,Version,1,"payload");
            var a=Ctx("trial0/step0",0,0,state); var b=Ctx("trial1/step0",1,0,state);
            var next=new StrategyState(Sid,Version,1,"next");
            var d=a.NoAction(next);
            Assert.That(a.State.Payload,Is.EqualTo("payload")); Assert.That(b.State.Payload,Is.EqualTo("payload"));
            Assert.That(d.NextState.Payload,Is.EqualTo("next")); Assert.That(d.NextState,Is.Not.SameAs(state));
            Assert.That(typeof(StrategyState).IsSerializable,Is.True);
            Assert.Throws<ArgumentException>(()=>new StrategyState("",Version,1,""));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new StrategyState(Sid,Version,0,""));
            Assert.Throws<ArgumentNullException>(()=>new StrategyState(Sid,Version,1,null));
            Assert.Throws<ArgumentException>(()=>new StrategyContext(Sid,Version,0,0,new ActionId("x"),StrategyState.EmptyFor("other",Version)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>Ctx("x",-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>Ctx("x",0,-1));
        }

        [Test] public void ContextIssuesDeterministicActionIdentityAndRejectsMismatchedDecision()
        {
            var a=Ctx("trial2/action8",2,8); var replay=Ctx("trial2/action8",2,8);
            var other=Ctx("trial3/action8",3,8);
            Assert.That(a.NextActionId,Is.EqualTo(replay.NextActionId));
            Assert.That(a.NextActionId,Is.Not.EqualTo(other.NextActionId));
            Assert.That(a.IsFor(new TestStrategy(id=>new BuyExperienceAction(id))),Is.True);
            Assert.That(a.IsFor(new OtherStrategy()),Is.False);
            var decision=a.Decide(new BuyExperienceAction(a.NextActionId),a.State,"buy xp");
            Assert.That(a.Accepts(decision),Is.True);
            Assert.Throws<ArgumentException>(()=>a.Decide(new BuyExperienceAction(new ActionId("wrong")),a.State));
            Assert.Throws<ArgumentException>(()=>a.Decide(new BuyExperienceAction(a.NextActionId),StrategyState.EmptyFor("other",Version)));
            Assert.That(a.NoAction().HasAction,Is.False);
        }

        [Test] public void ExistingFifteenActionTypesCanBeReturnedAsDeclarations()
        {
            GameAction[] actions={
                new SelectStageChoiceAction(new ActionId("0"),StageChoice.SelectA),
                new BuyCharacterAction(new ActionId("1"),"offer",new GameActionLocation(CharacterLocationKind.Hand,0)),
                new BuyItemAction(new ActionId("2"),"offer",0),new RerollShopAction(new ActionId("3"),ShopKind.Character),
                new BuyExperienceAction(new ActionId("4")),new MoveOrSwapBoardAction(new ActionId("5"),0,1),
                new MoveOrSwapHandAction(new ActionId("6"),0,1),new MoveBoardToHandAction(new ActionId("7"),0,0),
                new MoveHandToBoardAction(new ActionId("8"),0,0),new SellCharacterAction(new ActionId("9"),"i"),
                new SellItemAction(new ActionId("10"),0),new UseHandItemAction(new ActionId("11"),0,
                    GameActionTarget.FromItemUseTarget(ItemUseTarget.PlayerArea(0))),
                new UseEventItemAction(new ActionId("12"),GameActionTarget.FromItemUseTarget(ItemUseTarget.PlayerArea(0))),
                new EndEventAction(new ActionId("13")),new StartBattleAction(new ActionId("14"))};
            Assert.That(actions,Has.Length.EqualTo(15));
            for(int i=0;i<actions.Length;i++){
                var ctx=Ctx(i.ToString(),0,i); var d=ctx.Decide(actions[i],ctx.State);
                Assert.That(d.Action.Kind,Is.EqualTo((GameActionKind)(i+1)));
                Assert.That(GameActionStructuralValidator.Validate(d.Action,out _),Is.True);
            }
        }

        [Test] public void ContextDoesNotExposeGameStateOrRngCheckpoints()
        {
            var forbidden=new HashSet<Type>{typeof(ActionExecutionState),typeof(CharacterPool),typeof(CharacterOwnershipState),typeof(SimulationRandomStreamsState)};
            foreach(var property in typeof(StrategyContext).GetProperties()) Assert.That(forbidden.Contains(property.PropertyType),Is.False,property.Name);
        }

        [Test] public void StrategyReturnsDeclarationWithoutExecutingOrConsumingGameRng()
        {
            StageProgressConfig cfg=Config(); var streams=new SimulationRandomStreams(193UL);
            var before=streams.CaptureState();
            StageProgressState prep=StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(cfg),cfg).NextState;
            var shop=new ShopState(ShopKind.Character,Array.Empty<ShopOffer>(),5,1,1);
            var built=Build(prep,cfg,shop);
            Assert.That(built.Succeeded,Is.True,string.Join(";",built.Errors));
            var strategy=new TestStrategy(id=>new BuyExperienceAction(id)); var context=Ctx();
            var decision=strategy.ChooseAction(built.Observation,context);
            Assert.That(decision.Action.ActionId,Is.EqualTo(context.NextActionId));
            Assert.That(built.Observation.Player.Gold,Is.EqualTo(20));
            var after=streams.CaptureState();
            Assert.That(after.Shop,Is.EqualTo(before.Shop)); Assert.That(after.Stats,Is.EqualTo(before.Stats));
            Assert.That(after.Encounter,Is.EqualTo(before.Encounter)); Assert.That(after.Battle,Is.EqualTo(before.Battle));
            Assert.That(after.Event,Is.EqualTo(before.Event)); Assert.That(after.Roster,Is.EqualTo(before.Roster));
            Assert.That(after.Economy,Is.EqualTo(before.Economy));
        }

        [Test] public void ObservationActionFlagCoversChoicePreparationEventAndAutomaticPhases()
        {
            StageProgressConfig cfg=Config(); StageProgressState initial=StageProgressRules.CreateInitialState(cfg);
            Assert.That(Build(initial,cfg).Observation.IsStrategyActionAllowed,Is.False);
            StageProgressConfig choiceCfg=Config(choice:1);
            var choice=StageProgressRules.BeginCurrentEncounter(initial,choiceCfg).NextState;
            Assert.That(Build(choice,choiceCfg).Observation.IsStrategyActionAllowed,Is.True);
            StageProgressState prep=StageProgressRules.BeginCurrentEncounter(initial,cfg).NextState;
            var shop=new ShopState(ShopKind.Character,Array.Empty<ShopOffer>(),5,1,1);
            Assert.That(Build(prep,cfg,shop).Observation.IsStrategyActionAllowed,Is.True);

            StageProgressConfig eventCfg=Config(eventChance:100);
            var ep=StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(eventCfg),eventCfg).NextState;
            var eb=StageProgressRules.BeginBattle(ep).NextState;
            var reward=StageProgressRules.ResolveBattle(eb,eventCfg,true).NextState;
            var ev=StageProgressRules.CompletePendingReward(reward,eventCfg,new SimulationRandomStreams(8UL).Event.CaptureState()).NextState;
            var item=new ItemDefinition("gift","Gift",0);
            var eventObs=Build(ev,eventCfg,eventItem:new EventItemState("gift"),items:new[]{item});
            Assert.That(eventObs.Succeeded,Is.True,string.Join(";",eventObs.Errors));
            Assert.That(eventObs.Observation.IsStrategyActionAllowed,Is.True);

            var over=StageProgressRules.ResolveBattle(StageProgressRules.BeginBattle(prep).NextState,cfg,false).NextState;
            Assert.That(Build(over,cfg).Observation.IsStrategyActionAllowed,Is.False);
            var finalCfg=Config(total:5); var state=StageProgressRules.CreateInitialState(finalCfg);
            var rewardRng=new SimulationRandomStreams(3UL).Economy.CaptureState();
            for(int i=0;i<5;i++){
                var p=StageProgressRules.BeginCurrentEncounter(state,finalCfg).NextState;
                if(p.Phase==StageProgressPhase.AwaitingStageChoice)p=StageSelectionRules.Select(p,finalCfg,StageChoice.SelectA).NextState;
                var b=StageProgressRules.BeginBattle(p).NextState;
                state=StageProgressRules.CompletePendingReward(StageProgressRules.ResolveBattle(b,finalCfg,true).NextState,finalCfg,rewardRng).NextState;
            }
            Assert.That(state.Phase,Is.EqualTo(StageProgressPhase.Completed));
            Assert.That(Build(state,finalCfg).Observation.IsStrategyActionAllowed,Is.False);

            var battleStage=StageProgressRules.BeginBattle(prep).NextState;
            var initialized=BattleInitializationRules.Initialize(Economy(),battleStage,BoardState.CreateStandard(),HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()),Array.Empty<CharacterDefinition>(),EnemyBoardState.CreateEmpty(),
                Array.Empty<EnemyDefinition>(),new ItemEffectState(),Array.Empty<CharacterDefinition>(),Array.Empty<CharacterDefinition>(),
                new SimulationRandomStreams(12UL).Battle.CaptureState());
            Assert.That(initialized.Succeeded,Is.True,initialized.Failure.ToString());
            Assert.That(Build(battleStage,cfg,battle:initialized.State).Observation.IsStrategyActionAllowed,Is.False);
        }

        [Test] public void DispatcherCanReceiveStrategyActionAndOnlyDispatcherAdoptsResult()
        {
            StageProgressConfig cfg=Config(); var prep=StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(cfg),cfg).NextState;
            var built=Build(prep,cfg,new ShopState(ShopKind.Character,Array.Empty<ShopOffer>(),5,1,1));
            var context=Ctx(); var decision=new TestStrategy(id=>new BuyExperienceAction(id)).ChooseAction(built.Observation,context);
            var streams=new SimulationRandomStreams(22UL);
            var state=new ActionExecutionState(prep,Economy(),new CharacterPool(Array.Empty<CharacterPoolEntry>()),
                new CharacterInstanceFactory(),BoardState.CreateStandard(),HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()),new ItemEffectState(),streams.CaptureState());
            var result=new ActionDispatcher(new IActionHandler[]{new AcceptingHandler()}).Dispatch(state,decision.Action,new ActionExecutionContext());
            Assert.That(result.Succeeded,Is.True); Assert.That(result.ActionId,Is.EqualTo(context.NextActionId));
            Assert.That(state.HasExecuted(context.NextActionId),Is.False);
        }
        private sealed class OtherStrategy:IPlayerStrategy{
            public string StrategyId=>"other"; public string Version=>"1";
            public StrategyDecision ChooseAction(GameObservation observation,StrategyContext context)=>context.NoAction();
        }
        private sealed class AcceptingHandler:IActionHandler{
            public GameActionKind Kind=>GameActionKind.BuyExperience;
            public ActionHandlerResult Handle(ActionExecutionState state,GameAction action,ActionExecutionContext context)=>ActionHandlerResult.Success(state);
        }
    }
}


