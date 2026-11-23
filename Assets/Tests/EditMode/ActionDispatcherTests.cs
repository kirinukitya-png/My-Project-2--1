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
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class ActionDispatcherTests
    {
        private static ActionId Id(string value = "trial-1/1") => new ActionId(value);
        private static ActionExecutionContext Context() => new ActionExecutionContext();
        private static GameAction Action(string id = "trial-1/1") => new BuyExperienceAction(Id(id));
        private static StageProgressConfig Config(int choiceRound = 2, int initialEventChance = 0) =>
            new StageProgressConfig(20, 4, choiceRound, 4, initialEventChance, 10, 100,
                1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);

        private static StageProgressState PreparationStage()
        {
            StageProgressConfig config = Config();
            StageProgressResult begin = StageProgressRules.BeginCurrentEncounter(
                StageProgressRules.CreateInitialState(config), config);
            Assert.That(begin.Succeeded, Is.True);
            Assert.That(begin.NextState.Phase, Is.EqualTo(StageProgressPhase.Preparation));
            return begin.NextState;
        }

        private static ActionExecutionState State(StageProgressState stage = null,
            SimulationRandomStreams streams = null, IEnumerable<ActionId> history = null,
            EconomyState economy = null, ShopState shop = null)
        {
            streams = streams ?? new SimulationRandomStreams(1234UL);
            return new ActionExecutionState(stage ?? PreparationStage(),
                economy ?? new EconomyState(20, 0, 1, 12, 20, 0, 1, 20, 30),
                new CharacterPool(Array.Empty<CharacterPoolEntry>()), new CharacterInstanceFactory(), BoardState.CreateStandard(),
                HandState.CreateStandard(), new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                new ItemEffectState(), streams.CaptureState(), shop, executedActionIds: history);
        }

        [Test]
        public void DispatchRoutesToHandlerAndAdoptsCompleteNextStateOnlyOnSuccess()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience);
            ActionExecutionState before = State();
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(before, Action(), Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Succeeded));
            Assert.That(result.ActionId, Is.EqualTo(Id()));
            Assert.That(result.ActionKind, Is.EqualTo(GameActionKind.BuyExperience));
            Assert.That(result.NextState, Is.Not.SameAs(before));
            Assert.That(result.NextState.Economy.Gold, Is.EqualTo(21));
            Assert.That(before.Economy.Gold, Is.EqualTo(20));
            Assert.That(result.NextState.HasExecuted(Id()), Is.True);
            Assert.That(handler.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void HandlerRegistrationDispatchesByActionKind()
        {
            var buyXp = new FakeHandler(GameActionKind.BuyExperience);
            var sellItem = new FakeHandler(GameActionKind.SellItem);
            var dispatcher = new ActionDispatcher(new IActionHandler[] { buyXp, sellItem });
            var sell = new SellItemAction(Id("sell-1"), 0);
            ActionDispatchResult result = dispatcher.Dispatch(State(), sell, Context());
            Assert.That(result.Succeeded, Is.True);
            Assert.That(sellItem.CallCount, Is.EqualTo(1));
            Assert.That(buyXp.CallCount, Is.EqualTo(0));
        }

        [Test]
        public void MissingHandlerIsExplicitlyRejectedAsUnsupported()
        {
            ActionExecutionState state = State();
            ActionDispatchResult result = new ActionDispatcher().Dispatch(state, Action(), Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.UnsupportedAction));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [Test]
        public void NullActionStateAndContextAreHandledWithoutThrowing()
        {
            ActionExecutionState state = State();
            var dispatcher = new ActionDispatcher(new[] { new FakeHandler(GameActionKind.BuyExperience) });
            ActionDispatchResult noAction = dispatcher.Dispatch(state, null, Context());
            Assert.That(noAction.Error.Code, Is.EqualTo(ActionErrorCode.InvalidAction));
            Assert.That(noAction.NextState, Is.SameAs(state));
            ActionDispatchResult noState = dispatcher.Dispatch(null, Action(), Context());
            Assert.That(noState.Error.Code, Is.EqualTo(ActionErrorCode.InvalidState));
            Assert.That(noState.NextState, Is.Null);
            ActionDispatchResult noContext = dispatcher.Dispatch(state, Action(), null);
            Assert.That(noContext.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(noContext.Error.Code, Is.EqualTo(ActionErrorCode.ConfigurationError));
            Assert.That(noContext.NextState, Is.SameAs(state));
        }

        [Test]
        public void StructuralValidationRunsBeforePhaseHandlerAndRejectsMalformedActionKindPair()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience);
            var dispatcher = new ActionDispatcher(new[] { handler });
            var forged = new ForgedAction(Id(), GameActionKind.BuyExperience);
            ActionDispatchResult result = dispatcher.Dispatch(State(), forged, Context());
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InvalidAction));
            Assert.That(handler.CallCount, Is.Zero);
        }

        [Test]
        public void PreparationPhaseAllowsPreparationActionsAndBlocksChoiceAction()
        {
            ActionExecutionState state = State();
            var dispatcher = new ActionDispatcher(new[] { new FakeHandler(GameActionKind.BuyExperience) });
            var wrongPhase = new SelectStageChoiceAction(Id(), StageChoice.SelectA);
            ActionDispatchResult result = dispatcher.Dispatch(state, wrongPhase, Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InvalidPhase));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [Test]
        public void PhaseRejectionDoesNotCallHandlerOrChangeStateOrRandomCheckpoint()
        {
            var streams = new SimulationRandomStreams(987UL);
            var handler = new FakeHandler(GameActionKind.BuyExperience);
            ActionExecutionState state = State(streams: streams);
            SimulationRandomStreamsState checkpoint = streams.CaptureState();
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(state,
                new SelectStageChoiceAction(Id(), StageChoice.SelectB), Context());
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InvalidPhase));
            Assert.That(handler.CallCount, Is.Zero);
            Assert.That(result.NextState, Is.SameAs(state));
            AssertRandomStateEqual(checkpoint, result.NextState.RandomState);
        }

        [Test]
        public void PhaseTableMatchesCurrentStageProgressPhases()
        {
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.SelectStageChoice, StageProgressPhase.AwaitingStageChoice), Is.True);
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.SelectStageChoice, StageProgressPhase.Preparation), Is.False);
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.BuyItem, StageProgressPhase.Preparation), Is.True);
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.MoveOrSwapBoard, StageProgressPhase.Battle), Is.False);
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.UseHandItem, StageProgressPhase.Preparation), Is.True);
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.UseEventItem, StageProgressPhase.EventPending), Is.True);
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.EndEvent, StageProgressPhase.EventPending), Is.True);
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.StartBattle, StageProgressPhase.Preparation), Is.True);
            Assert.That(ActionPhaseRules.IsAllowed(GameActionKind.StartBattle, StageProgressPhase.RewardPending), Is.False);
        }

        [Test]
        public void SuccessfulActionIdIsRecordedAndDuplicateIsRejectedBeforeHandler()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience);
            var dispatcher = new ActionDispatcher(new[] { handler });
            ActionExecutionState first = State();
            ActionDispatchResult applied = dispatcher.Dispatch(first, Action("same-id"), Context());
            int callsAfterSuccess = handler.CallCount;
            ActionDispatchResult duplicate = dispatcher.Dispatch(applied.NextState,
                new BuyExperienceAction(Id("same-id")), Context());
            Assert.That(applied.NextState.ExecutedActionIds.Count, Is.EqualTo(1));
            Assert.That(duplicate.Error.Code, Is.EqualTo(ActionErrorCode.DuplicateAction));
            Assert.That(duplicate.NextState, Is.SameAs(applied.NextState));
            Assert.That(handler.CallCount, Is.EqualTo(callsAfterSuccess));
        }

        [Test]
        public void SameActionIdWithDifferentActionContentIsRejected()
        {
            var buyXp = new FakeHandler(GameActionKind.BuyExperience);
            var sellItem = new FakeHandler(GameActionKind.SellItem);
            var dispatcher = new ActionDispatcher(new IActionHandler[] { buyXp, sellItem });
            ActionExecutionState applied = dispatcher.Dispatch(State(), Action("collision"), Context()).NextState;
            ActionDispatchResult duplicateDifferentPayload = dispatcher.Dispatch(applied,
                new SellItemAction(Id("collision"), 4), Context());
            Assert.That(duplicateDifferentPayload.Error.Code, Is.EqualTo(ActionErrorCode.DuplicateAction));
            Assert.That(sellItem.CallCount, Is.Zero);
        }

        [Test]
        public void FailedActionIdIsNotRecordedAndCanBeRetried()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience) { Mode = HandlerMode.Reject };
            var dispatcher = new ActionDispatcher(new[] { handler });
            ActionExecutionState state = State();
            ActionDispatchResult failed = dispatcher.Dispatch(state, Action("retry-id"), Context());
            Assert.That(failed.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(failed.NextState, Is.SameAs(state));
            Assert.That(state.HasExecuted(Id("retry-id")), Is.False);
            handler.Mode = HandlerMode.Success;
            ActionDispatchResult retry = dispatcher.Dispatch(state, Action("retry-id"), Context());
            Assert.That(retry.Succeeded, Is.True);
            Assert.That(retry.NextState.HasExecuted(Id("retry-id")), Is.True);
        }

        [Test]
        public void ActionIdComparisonIsOrdinalAndCaseSensitive()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience);
            var dispatcher = new ActionDispatcher(new[] { handler });
            ActionExecutionState state = dispatcher.Dispatch(State(), Action("abc"), Context()).NextState;
            ActionDispatchResult upper = dispatcher.Dispatch(state, Action("ABC"), Context());
            Assert.That(upper.Succeeded, Is.True);
            Assert.That(upper.NextState.ExecutedActionIds.Count, Is.EqualTo(2));
        }

        [Test]
        public void StateCopiesAndFreezesActionHistoryInput()
        {
            var input = new List<ActionId> { Id("already") };
            ActionExecutionState state = State(history: input);
            input.Add(Id("late"));
            Assert.That(state.ExecutedActionIds.Count, Is.EqualTo(1));
            Assert.That(state.HasExecuted(Id("already")), Is.True);
            Assert.That(state.HasExecuted(Id("late")), Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<ActionId>)state.ExecutedActionIds).Add(Id("x")));
        }

        [Test]
        public void HistoryRejectsDuplicatesOrdinallyButPermitsDistinctCase()
        {
            Assert.Throws<ArgumentException>(() => State(history: new[] { Id("x"), Id("x") }));
            Assert.DoesNotThrow(() => State(history: new[] { Id("x"), Id("X") }));
        }

        [Test]
        public void HandlerRejectionDiscardsProposedStateAndLeavesInputsUnchanged()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience) { Mode = HandlerMode.RejectAfterProposal };
            ActionExecutionState state = State();
            SimulationRandomStreamsState beforeRng = state.RandomState;
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(state, Action(), Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.Rejected));
            Assert.That(result.NextState, Is.SameAs(state));
            Assert.That(state.Economy.Gold, Is.EqualTo(20));
            Assert.That(state.ExecutedActionIds.Count, Is.Zero);
            AssertRandomStateEqual(beforeRng, result.NextState.RandomState);
        }

        [Test]
        public void HandlerExceptionBecomesInternalErrorAndLeavesOriginalStateUnchanged()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience) { Mode = HandlerMode.ThrowInternal };
            ActionExecutionState state = State();
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(state, Action(), Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.InternalError));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InternalError));
            Assert.That(result.NextState, Is.SameAs(state));
            Assert.That(state.ExecutedActionIds.Count, Is.Zero);
        }

        [Test]
        public void ConfigurationExceptionIsReportedSeparately()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience) { Mode = HandlerMode.ThrowConfiguration };
            ActionExecutionState state = State();
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(state, Action(), Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.ConfigurationError));
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.ConfigurationError));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [Test]
        public void NullOrMalformedHandlerResultIsInternalError()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience) { Mode = HandlerMode.ReturnNull };
            ActionExecutionState state = State();
            ActionDispatchResult nullResult = new ActionDispatcher(new[] { handler }).Dispatch(state, Action(), Context());
            Assert.That(nullResult.Status, Is.EqualTo(ActionDispatchStatus.InternalError));
            Assert.That(nullResult.NextState, Is.SameAs(state));
            handler.Mode = HandlerMode.InvalidSuccess;
            ActionDispatchResult malformed = new ActionDispatcher(new[] { handler }).Dispatch(state, Action("bad-success"), Context());
            Assert.That(malformed.Status, Is.EqualTo(ActionDispatchStatus.InternalError));
            Assert.That(malformed.NextState, Is.SameAs(state));
        }

        [Test]
        public void HandlerCannotOwnActionHistoryAndDispatcherRejectsAttempt()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience) { Mode = HandlerMode.AppendHistory };
            ActionExecutionState state = State();
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(state, Action(), Context());
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InternalError));
            Assert.That(result.NextState, Is.SameAs(state));
            Assert.That(state.ExecutedActionIds.Count, Is.Zero);
        }

        [Test]
        public void HandlerCannotChangeTrialSeedOrCheckpointVersion()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience) { Mode = HandlerMode.ChangeTrialSeed };
            ActionExecutionState state = State();
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(state, Action(), Context());
            Assert.That(result.Error.Code, Is.EqualTo(ActionErrorCode.InternalError));
            Assert.That(result.NextState, Is.SameAs(state));
        }

        [Test]
        public void DispatcherAcceptsOnlyDeclaredRngStreamsAndPreservesAllOthers()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience)
            {
                Mode = HandlerMode.ChangeShopStream,
                DeclaredRandomStreams = RandomStreamMask.Shop
            };
            ActionExecutionState state = State();
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(state, Action(), Context());
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NextState.RandomState.Shop, Is.Not.EqualTo(state.RandomState.Shop));
            Assert.That(result.NextState.RandomState.Stats, Is.EqualTo(state.RandomState.Stats));
            Assert.That(result.NextState.RandomState.Encounter, Is.EqualTo(state.RandomState.Encounter));
            Assert.That(result.NextState.RandomState.Battle, Is.EqualTo(state.RandomState.Battle));
            Assert.That(result.NextState.RandomState.Event, Is.EqualTo(state.RandomState.Event));
            Assert.That(result.NextState.RandomState.Roster, Is.EqualTo(state.RandomState.Roster));
            Assert.That(result.NextState.RandomState.Economy, Is.EqualTo(state.RandomState.Economy));
        }

        [Test]
        public void UndeclaredRngStreamChangeIsRejectedWithoutAdoptingCheckpoint()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience) { Mode = HandlerMode.ChangeShopStream };
            ActionExecutionState state = State();
            ActionDispatchResult result = new ActionDispatcher(new[] { handler }).Dispatch(state, Action(), Context());
            Assert.That(result.Status, Is.EqualTo(ActionDispatchStatus.InternalError));
            Assert.That(result.NextState, Is.SameAs(state));
            AssertRandomStateEqual(state.RandomState, result.NextState.RandomState);
        }

        [Test]
        public void DispatcherRecordsSuccessfulActionAndCheckpointCanRebuildHistory()
        {
            var handler = new FakeHandler(GameActionKind.BuyExperience);
            var dispatcher = new ActionDispatcher(new[] { handler });
            ActionExecutionState first = dispatcher.Dispatch(State(), Action("replay-1"), Context()).NextState;
            var restored = new ActionExecutionState(first.Stage, first.Economy, first.CharacterPool, first.InstanceFactory,
                first.Board, first.Hand, first.Ownership, first.ItemEffects, first.RandomState,
                first.Shop, first.EventItem, first.EnemyBoard, first.EnemyPreparation,
                first.Battle, first.BattleResult, first.ExecutedActionIds);
            Assert.That(restored.HasExecuted(Id("replay-1")), Is.True);
            ActionDispatchResult replay = dispatcher.Dispatch(restored, Action("replay-1"), Context());
            Assert.That(replay.Error.Code, Is.EqualTo(ActionErrorCode.DuplicateAction));
        }

        [Test]
        public void ContextDefensivelyCopiesDefinitionListsAndDoesNotStoreRunState()
        {
            var definitions = new List<CharacterDefinition>
            {
                new CharacterDefinition("C1", "One", 1, Array.Empty<string>(), Array.Empty<string>())
            };
            var context = new ActionExecutionContext(characterDefinitions: definitions);
            definitions.Clear();
            Assert.That(context.CharacterDefinitions.Count, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() => ((IList<CharacterDefinition>)context.CharacterDefinitions).Clear());
            Assert.That(context.StageConfig, Is.Null);
            Assert.That(context.EconomyConfig, Is.Null);
        }

        [Test]
        public void StateRejectsInconsistentOwnershipAndInvalidRandomCheckpoint()
        {
            Assert.Throws<ArgumentException>(() => new ActionExecutionState(PreparationStage(),
                new EconomyState(1, 0, 1, 12, 1, 0, 1), new CharacterPool(Array.Empty<CharacterPoolEntry>()), new CharacterInstanceFactory(),
                BoardState.CreateStandard(), HandState.CreateStandard(),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()), new ItemEffectState(), default(SimulationRandomStreamsState)));
        }

        [Test]
        public void BattleStateBattleRngIsTheCanonicalBattleStreamAndMustMatchAggregateCheckpoint()
        {
            StageProgressConfig config = Config();
            var streams = new SimulationRandomStreams(7654UL);
            StageProgressState stage = StageProgressRules.CreateInitialState(config);
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            stage = StageProgressRules.BeginBattle(stage).NextState;
            stage = StageProgressRules.ResolveBattle(stage, config, true).NextState;
            stage = StageProgressRules.CompletePendingReward(stage, config, streams.Event.CaptureState()).NextState;
            stage = StageProgressRules.BeginCurrentEncounter(stage, config).NextState;
            stage = StageSelectionRules.Select(stage, config, StageChoice.SelectA).NextState;
            stage = StageProgressRules.BeginBattle(stage).NextState;

            var economy = new EconomyState(20, 0, 1, 12, 20, 0, 1, 20, 30);
            BoardState board = BoardState.CreateStandard();
            HandState hand = HandState.CreateStandard();
            var ownership = new CharacterOwnershipState(Array.Empty<CharacterInstance>());
            EnemyBoardState enemyBoard = EnemyBoardState.CreateEmpty();
            var effects = new ItemEffectState();
            BattleInitializationResult battle = BattleInitializationRules.Initialize(economy, stage,
                board, hand, ownership, Array.Empty<CharacterDefinition>(), enemyBoard,
                Array.Empty<EnemyDefinition>(), effects, Array.Empty<CharacterDefinition>(),
                Array.Empty<CharacterDefinition>(), streams.Battle.CaptureState());
            Assert.That(battle.Succeeded, Is.True, battle.Failure.ToString());
            ActionExecutionState aggregate = new ActionExecutionState(stage, economy,
                new CharacterPool(Array.Empty<CharacterPoolEntry>()), new CharacterInstanceFactory(), board, hand, ownership,
                effects, streams.CaptureState(), enemyBoard: enemyBoard, battle: battle.State);
            Assert.That(aggregate.Battle.BattleRandomState, Is.EqualTo(aggregate.RandomState.Battle));

            Pcg32RandomSource moved = Pcg32RandomSource.FromState(streams.Battle.CaptureState());
            moved.NextInt(0, 10);
            SimulationRandomStreamsState checkpoint = streams.CaptureState();
            var mismatched = new SimulationRandomStreamsState(checkpoint.TrialSeed, checkpoint.Version,
                checkpoint.Shop, checkpoint.Stats, checkpoint.Encounter, moved.CaptureState(),
                checkpoint.Event, checkpoint.Roster, checkpoint.Economy);
            Assert.Throws<ArgumentException>(() => new ActionExecutionState(stage, economy,
                new CharacterPool(Array.Empty<CharacterPoolEntry>()), new CharacterInstanceFactory(), board, hand, ownership,
                effects, mismatched, enemyBoard: enemyBoard, battle: battle.State));
        }

        [Test]
        public void DuplicateHandlerRegistrationsAndUnknownKindsAreRejectedAtSetup()
        {
            Assert.Throws<ArgumentException>(() => new ActionDispatcher(new IActionHandler[]
            { new FakeHandler(GameActionKind.BuyExperience), new FakeHandler(GameActionKind.BuyExperience) }));
            Assert.Throws<ArgumentException>(() => new ActionDispatcher(new[] { new FakeHandler((GameActionKind)99) }));
        }

        [Test]
        public void SameInitialStateAndActionSequenceProduceSameHistoryAndRngCheckpoint()
        {
            var dispatcher = new ActionDispatcher(new[] { new FakeHandler(GameActionKind.BuyExperience) });
            ActionExecutionState left = dispatcher.Dispatch(State(streams: new SimulationRandomStreams(9)), Action("a"), Context()).NextState;
            ActionExecutionState right = dispatcher.Dispatch(State(streams: new SimulationRandomStreams(9)), Action("a"), Context()).NextState;
            Assert.That(left.Economy.Gold, Is.EqualTo(right.Economy.Gold));
            Assert.That(left.ExecutedActionIds, Is.EqualTo(right.ExecutedActionIds));
            AssertRandomStateEqual(left.RandomState, right.RandomState);
        }

        [Test]
        public void CommonDispatchDoesNotConsumeAnyRandomStream()
        {
            var streams = new SimulationRandomStreams(0xABCDEFUL);
            SimulationRandomStreamsState before = streams.CaptureState();
            var dispatcher = new ActionDispatcher(new[] { new FakeHandler(GameActionKind.BuyExperience) });
            ActionDispatchResult result = dispatcher.Dispatch(State(streams: streams), Action(), Context());
            Assert.That(result.Succeeded, Is.True);
            AssertRandomStateEqual(before, result.NextState.RandomState);
            AssertRandomStateEqual(before, streams.CaptureState());
        }

        private static void AssertRandomStateEqual(SimulationRandomStreamsState expected, SimulationRandomStreamsState actual)
        {
            Assert.That(actual.TrialSeed, Is.EqualTo(expected.TrialSeed));
            Assert.That(actual.Version, Is.EqualTo(expected.Version));
            Assert.That(actual.Shop, Is.EqualTo(expected.Shop));
            Assert.That(actual.Stats, Is.EqualTo(expected.Stats));
            Assert.That(actual.Encounter, Is.EqualTo(expected.Encounter));
            Assert.That(actual.Battle, Is.EqualTo(expected.Battle));
            Assert.That(actual.Event, Is.EqualTo(expected.Event));
            Assert.That(actual.Roster, Is.EqualTo(expected.Roster));
            Assert.That(actual.Economy, Is.EqualTo(expected.Economy));
        }

        private enum HandlerMode { Success, Reject, RejectAfterProposal, ThrowInternal, ThrowConfiguration, ReturnNull, InvalidSuccess, AppendHistory, ChangeTrialSeed, ChangeShopStream }

        private sealed class FakeHandler : IActionHandler
        {
            public GameActionKind Kind { get; }
            public HandlerMode Mode { get; set; }
            public RandomStreamMask DeclaredRandomStreams { get; set; }
            public int CallCount { get; private set; }
            public FakeHandler(GameActionKind kind) { Kind = kind; }

            public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
            {
                CallCount++;
                switch (Mode)
                {
                    case HandlerMode.Reject: return ActionHandlerResult.Reject(ActionErrorCode.InsufficientGold);
                    case HandlerMode.RejectAfterProposal:
                        _ = state.With(economy: state.Economy.WithGold(state.Economy.Gold + 10));
                        return ActionHandlerResult.Reject(ActionErrorCode.InsufficientGold, "proposal discarded");
                    case HandlerMode.ThrowInternal: throw new InvalidOperationException("fake failure");
                    case HandlerMode.ThrowConfiguration: throw new ActionExecutionConfigurationException("missing config");
                    case HandlerMode.ReturnNull: return null;
                    case HandlerMode.InvalidSuccess:
                        return ActionHandlerResult.Success(new ActionExecutionState(state.Stage, state.Economy,
                            state.CharacterPool, state.InstanceFactory, state.Board, state.Hand, state.Ownership, state.ItemEffects,
                            state.RandomState, state.Shop, state.EventItem, state.EnemyBoard,
                            state.EnemyPreparation, state.Battle, state.BattleResult,
                            new[] { action.ActionId }));
                    case HandlerMode.AppendHistory:
                        return ActionHandlerResult.Success(new ActionExecutionState(state.Stage, state.Economy,
                            state.CharacterPool, state.InstanceFactory, state.Board, state.Hand, state.Ownership, state.ItemEffects,
                            state.RandomState, state.Shop, state.EventItem, state.EnemyBoard,
                            state.EnemyPreparation, state.Battle, state.BattleResult,
                            new[] { new ActionId("handler-forged") }));
                    case HandlerMode.ChangeTrialSeed:
                        var other = new SimulationRandomStreams(state.RandomState.TrialSeed + 1).CaptureState();
                        return ActionHandlerResult.Success(state.With(randomState: other));
                    case HandlerMode.ChangeShopStream:
                        Pcg32RandomSource shop = Pcg32RandomSource.FromState(state.RandomState.Shop);
                        shop.NextInt(0, 100);
                        var before = state.RandomState;
                        var updated = new SimulationRandomStreamsState(before.TrialSeed, before.Version,
                            shop.CaptureState(), before.Stats, before.Encounter, before.Battle,
                            before.Event, before.Roster, before.Economy);
                        return ActionHandlerResult.Success(state.With(randomState: updated), DeclaredRandomStreams);
                    default:
                        return ActionHandlerResult.Success(state.With(economy: state.Economy.WithGold(state.Economy.Gold + 1)));
                }
            }
        }

        private sealed class ForgedAction : GameAction
        {
            private readonly GameActionKind _kind;
            public ForgedAction(ActionId id, GameActionKind kind) : base(id) { _kind = kind; }
            public override GameActionKind Kind => _kind;
        }
    }
}
