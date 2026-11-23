using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed class StrategyRandomTests
    {
        private static StrategyRngState Initial(ulong seed=101, string id="strategy.a", string version="1") =>
            StrategyRandomSource.Create(seed,id,version).CaptureState();
        private static StrategyContext Context(StrategyRngState rng, string actionId="trial/action0",
            long trial=0, long step=0, StrategyState strategyState=null) =>
            new StrategyContext(rng.StrategyId,rng.StrategyVersion,trial,step,new ActionId(actionId),
                strategyState??StrategyState.EmptyFor(rng.StrategyId,rng.StrategyVersion),rng);

        [Test]
        public void StrategyRng_UsesStableSeedAndIdentitySeparatedStreamEight()
        {
            StrategyRngState a=Initial(1234,"alpha","1"), repeat=Initial(1234,"alpha","1");
            StrategyRngState otherSeed=Initial(1235,"alpha","1");
            StrategyRngState otherId=Initial(1234,"beta","1");
            StrategyRngState otherVersion=Initial(1234,"alpha","2");
            Assert.That(a.AlgorithmId,Is.EqualTo(Pcg32RandomSource.AlgorithmId));
            Assert.That(a.AlgorithmVersion,Is.EqualTo(Pcg32State.CurrentAlgorithmVersion));
            Assert.That(a.CheckpointVersion,Is.EqualTo(StrategyRngState.CurrentCheckpointVersion));
            Assert.That(typeof(StrategyRngState).IsSerializable,Is.True);
            Assert.That(a.Equals(repeat),Is.True);
            Assert.That(a.Equals(otherSeed),Is.False);
            Assert.That(a.Equals(otherId),Is.False);
            Assert.That(a.Equals(otherVersion),Is.False);
            Assert.That((ulong)RandomStreamId.Strategy,Is.EqualTo(8UL));
            var r1=StrategyRandomSource.FromState(a); var r2=StrategyRandomSource.FromState(repeat);
            for(int i=0;i<12;i++) Assert.That(r1.NextUIntForTest(),Is.EqualTo(r2.NextUIntForTest()));
            Assert.That(StrategyRandomSource.FromState(a).NextInt(0,100000),
                Is.Not.EqualTo(StrategyRandomSource.FromState(otherSeed).NextInt(0,100000)));
        }

        [Test]
        public void StrategyRng_CheckpointRestoresExactSequenceAndTrialLocalCopies()
        {
            StrategyRngState initial=Initial();
            StrategyRandomSource first=StrategyRandomSource.FromState(initial);
            first.NextInt(0,10); first.NextUnitDouble();
            StrategyRngState checkpoint=first.CaptureState();
            int[] expected=new int[8];
            for(int i=0;i<expected.Length;i++) expected[i]=first.NextInt(-10,100);
            StrategyRandomSource resumed=StrategyRandomSource.FromState(checkpoint,101,"strategy.a","1");
            for(int i=0;i<expected.Length;i++) Assert.That(resumed.NextInt(-10,100),Is.EqualTo(expected[i]));
            Assert.That(initial.Equals(Initial()),Is.True);
        }

        [Test]
        public void StrategyRng_RejectsMalformedOrMismatchedCheckpoint()
        {
            Assert.Throws<ArgumentException>(()=>new StrategyRngState("System.Random",1,1,1,"s","1",
                new Pcg32State(1,4,5)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new StrategyRngState(Pcg32RandomSource.AlgorithmId,1,2,1,"s","1",
                new Pcg32State(1,4,5)));
            Assert.Throws<ArgumentException>(()=>new StrategyRngState(1,"s","1",new Pcg32State(1,4,4)));
            StrategyRngState state=Initial();
            Assert.Throws<ArgumentException>(()=>StrategyRandomSource.FromState(state,102,"strategy.a","1"));
            Assert.Throws<ArgumentException>(()=>StrategyRandomSource.FromState(state,101,"strategy.b","1"));
            Assert.Throws<ArgumentException>(()=>new StrategyContext("strategy.a","1",0,0,
                new ActionId("x"),StrategyState.EmptyFor("strategy.a","1"),Initial(101,"wrong","1")));
        }

        [Test]
        public void StrategyRng_NonConsumptionOneDrawAndMultipleDrawsHaveExplicitCheckpoints()
        {
            StrategyRngState initial=Initial();
            StrategyRandomSource noDraw=StrategyRandomSource.FromState(initial);
            Assert.That(noDraw.CaptureState().Equals(initial),Is.True);
            StrategyRandomSource one=StrategyRandomSource.FromState(initial);
            one.NextInt(0,100);
            StrategyRngState afterOne=one.CaptureState();
            Assert.That(afterOne.Equals(initial),Is.False);
            StrategyRandomSource repeated=StrategyRandomSource.FromState(initial);
            repeated.NextInt(0,100);
            Assert.That(repeated.CaptureState().Equals(afterOne),Is.True);
            StrategyRandomSource many=StrategyRandomSource.FromState(initial);
            many.NextUnitDouble(); many.NextInt(0,3); many.NextInt(0,1000);
            Assert.That(many.CaptureState().Equals(afterOne),Is.False);
            var list=new List<int>{1,2,3,4};
            many.Shuffle(list);
            Assert.That(many.CaptureState().Equals(afterOne),Is.False);
        }

        [Test]
        public void StrategyContext_CreatesDecisionLocalSourceAndDecisionProposesItsCheckpoint()
        {
            StrategyRngState initial=Initial(); StrategyContext context=Context(initial);
            StrategyRandomSource local=context.CreateStrategyRandomSource();
            int chosen=local.NextInt(0,4);
            StrategyRngState proposed=local.CaptureState();
            var action=new SelectStageChoiceAction(context.NextActionId,(StageChoice)chosen);
            StrategyDecision decision=context.DecideWithRng(action,context.State,proposed,"sampled choice");
            Assert.That(context.StrategyRandomState.Equals(initial),Is.True);
            Assert.That(decision.NextStrategyRngState.Equals(proposed),Is.True);
            Assert.That(decision.StrategyRngAdvanced,Is.True);
            Assert.That(context.Accepts(decision),Is.True);
            StrategyDecision noDraw=context.Decide(new BuyExperienceAction(context.NextActionId),context.State);
            Assert.That(noDraw.NextStrategyRngState.Equals(initial),Is.True);
            Assert.That(noDraw.StrategyRngAdvanced,Is.False);
        }

        [Test]
        public void StrategyRng_DoesNotChangeAnyOfTheSevenGameStreams()
        {
            var gameStreams=new SimulationRandomStreams(777UL);
            SimulationRandomStreamsState before=gameStreams.CaptureState();
            StrategyRngState strategy=Initial(gameStreams.TrialSeed,"alpha","1");
            StrategyRandomSource local=StrategyRandomSource.FromState(strategy);
            for(int i=0;i<20;i++) local.NextInt(0,100);
            SimulationRandomStreamsState after=gameStreams.CaptureState();
            Assert.That(after.Shop,Is.EqualTo(before.Shop)); Assert.That(after.Stats,Is.EqualTo(before.Stats));
            Assert.That(after.Encounter,Is.EqualTo(before.Encounter)); Assert.That(after.Battle,Is.EqualTo(before.Battle));
            Assert.That(after.Event,Is.EqualTo(before.Event)); Assert.That(after.Roster,Is.EqualTo(before.Roster));
            Assert.That(after.Economy,Is.EqualTo(before.Economy));
        }

        [Test]
        public void StrategyDecision_NoActionCanProposeRandomCheckpointWithoutCommittingIt()
        {
            StrategyRngState initial=Initial(); StrategyContext context=Context(initial);
            StrategyRandomSource local=context.CreateStrategyRandomSource(); local.NextUnitDouble();
            StrategyRngState next=local.CaptureState();
            StrategyDecision decision=context.NoActionWithRng(context.State,next,"automatic phase");
            Assert.That(decision.HasAction,Is.False);
            Assert.That(decision.NextStrategyRngState.Equals(next),Is.True);
            Assert.That(context.StrategyRandomState.Equals(initial),Is.True);
            Assert.That(context.Accepts(decision),Is.True);
        }

        [Test]
        public void StrategyContext_RejectsRandomCheckpointFromDifferentTrialOrIdentity()
        {
            StrategyRngState initial=Initial(); StrategyContext context=Context(initial);
            StrategyRngState foreign=Initial(202,"strategy.a","1");
            Assert.Throws<ArgumentException>(()=>context.DecideWithRng(
                new BuyExperienceAction(context.NextActionId),context.State,foreign));
            Assert.Throws<ArgumentException>(()=>context.NoActionWithRng(context.State,
                Initial(101,"strategy.a","2")));
        }

        [Test]
        public void RejectedGameActionCanStillCommitTheAdoptedStrategyDecisionCheckpoint()
        {
            StageProgressConfig config = new StageProgressConfig(25, 5, 5, 4, 0, 0, 0,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            var gameStreams = new SimulationRandomStreams(505UL);
            SimulationRandomStreamsState gameRngBefore = gameStreams.CaptureState();
            var gameState = new ActionExecutionState(prep,
                new EconomyState(20, 0, 1, 12, 20, 0, 1, 20, 30),
                new CharacterPool(Array.Empty<CharacterPoolEntry>()), new CharacterInstanceFactory(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), new ItemEffectState(),
                gameRngBefore);

            StrategyRngState initial = Initial(505, "retry", "1");
            StrategyContext context = Context(initial, "trial/action0");
            StrategyRandomSource local = context.CreateStrategyRandomSource();
            local.NextInt(0, 100);
            StrategyRngState decisionRng = local.CaptureState();
            StrategyDecision decision = context.DecideWithRng(
                new BuyExperienceAction(context.NextActionId), context.State, decisionRng);

            // No handler is registered: Dispatcher rejects the action and leaves game state untouched.
            ActionDispatchResult rejected = new ActionDispatcher().Dispatch(gameState,
                decision.Action, new ActionExecutionContext());
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Error.Code, Is.EqualTo(ActionErrorCode.UnsupportedAction));
            Assert.That(rejected.NextState, Is.SameAs(gameState));
            Assert.That(gameState.Economy.Gold, Is.EqualTo(20));
            Assert.That(gameState.RandomState.Shop, Is.EqualTo(gameRngBefore.Shop));
            Assert.That(gameState.RandomState.Battle, Is.EqualTo(gameRngBefore.Battle));

            // Adoption belongs to the decision host, independently of GameAction success.
            StrategyRngState adopted = decision.NextStrategyRngState;
            Assert.That(adopted.Equals(decisionRng), Is.True);
            Assert.That(context.StrategyRandomState.Equals(initial), Is.True);
            Assert.That(decision.StrategyRngAdvanced, Is.True);
        }

        [Test]
        public void SuccessfulGameActionCarriesItsStrategyCheckpointWithoutChangingGameRng()
        {
            StageProgressConfig config = new StageProgressConfig(25, 5, 5, 4, 0, 0, 0,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            StageProgressState prep = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config).NextState;
            var gameStreams = new SimulationRandomStreams(606UL);
            SimulationRandomStreamsState gameRngBefore = gameStreams.CaptureState();
            var gameState = new ActionExecutionState(prep,
                new EconomyState(20, 0, 1, 12, 20, 0, 1, 20, 30),
                new CharacterPool(Array.Empty<CharacterPoolEntry>()), new CharacterInstanceFactory(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), new ItemEffectState(),
                gameRngBefore);
            StrategyRngState initial = Initial(606, "success", "1");
            StrategyContext context = Context(initial, "trial/action0");
            StrategyRandomSource local = context.CreateStrategyRandomSource();
            local.NextUnitDouble();
            StrategyDecision decision = context.DecideWithRng(
                new BuyExperienceAction(context.NextActionId), context.State, local.CaptureState());

            ActionDispatchResult accepted = new ActionDispatcher(new[] { new AcceptHandler() }).Dispatch(
                gameState, decision.Action, new ActionExecutionContext());
            Assert.That(accepted.Succeeded, Is.True);
            Assert.That(accepted.NextState.HasExecuted(context.NextActionId), Is.True);
            Assert.That(accepted.NextState.RandomState.Shop, Is.EqualTo(gameRngBefore.Shop));
            Assert.That(accepted.NextState.RandomState.Economy, Is.EqualTo(gameRngBefore.Economy));
            Assert.That(gameState.HasExecuted(context.NextActionId), Is.False);
            Assert.That(decision.NextStrategyRngState.Equals(initial), Is.False);
            Assert.That(context.StrategyRandomState.Equals(initial), Is.True);
        }

        [Test]
        public void ThrownStrategyOrInvalidDecisionLeavesInputCheckpointUnadopted()
        {
            StrategyRngState initial = Initial(88, "throws", "1");
            StrategyContext context = Context(initial);
            Assert.Throws<InvalidOperationException>(() =>
            {
                StrategyRandomSource local = context.CreateStrategyRandomSource();
                local.NextInt(0, 20);
                throw new InvalidOperationException("strategy failed after a local draw");
            });
            Assert.That(context.StrategyRandomState.Equals(initial), Is.True);
            Assert.Throws<ArgumentException>(() => context.DecideWithRng(
                new BuyExperienceAction(context.NextActionId), context.State,
                Initial(89, "throws", "1")));
            Assert.That(context.StrategyRandomState.Equals(initial), Is.True);
        }

        private sealed class AcceptHandler : IActionHandler
        {
            public GameActionKind Kind => GameActionKind.BuyExperience;
            public ActionHandlerResult Handle(ActionExecutionState state, GameAction action,
                ActionExecutionContext context) => ActionHandlerResult.Success(state);
        }

        [Test]
        public void LegacyStrategyContextRemainsCompatibleAndCannotInventRandomState()
        {
            var context=new StrategyContext("legacy","1",0,0,new ActionId("legacy/0"),
                StrategyState.EmptyFor("legacy","1"));
            Assert.That(context.StrategyRandomState,Is.Null);
            Assert.Throws<InvalidOperationException>(()=>context.CreateStrategyRandomSource());
            StrategyDecision decision=context.Decide(new BuyExperienceAction(context.NextActionId),context.State);
            Assert.That(decision.NextStrategyRngState,Is.Null);
            Assert.That(context.Accepts(decision),Is.True);
            Assert.Throws<ArgumentException>(()=>context.DecideWithRng(
                new BuyExperienceAction(context.NextActionId),context.State,Initial(4,"legacy","1")));
        }

        [Test]
        public void StrategyReplay_UsesSameCheckpointAndCallerActionIdIndependentOfRngDraws()
        {
            StrategyRngState initial=Initial(909,"random.choice","3");
            StrategyDecision Decide(StrategyRngState checkpoint)
            {
                StrategyContext context=Context(checkpoint,"trial9/action21",9,21);
                StrategyRandomSource rng=context.CreateStrategyRandomSource();
                StageChoice choice=(StageChoice)rng.NextInt(0,4);
                return context.DecideWithRng(new SelectStageChoiceAction(context.NextActionId,choice),
                    context.State,rng.CaptureState());
            }
            StrategyDecision a=Decide(initial); StrategyDecision b=Decide(initial);
            Assert.That(a.Action.ActionId,Is.EqualTo(new ActionId("trial9/action21")));
            Assert.That(a.Action.Kind,Is.EqualTo(b.Action.Kind));
            Assert.That(((SelectStageChoiceAction)a.Action).Choice,Is.EqualTo(((SelectStageChoiceAction)b.Action).Choice));
            Assert.That(a.NextStrategyRngState.Equals(b.NextStrategyRngState),Is.True);
            Assert.That(initial.Equals(Initial(909,"random.choice","3")),Is.True);
        }
    }

    internal static class StrategyRandomTestExtensions
    {
        // Obtains one unbiased integer without exposing or depending on PCG private methods.
        public static uint NextUIntForTest(this StrategyRandomSource source) =>
            unchecked((uint)source.NextInt(int.MinValue, int.MaxValue));
    }
}
