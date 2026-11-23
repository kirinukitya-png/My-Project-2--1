using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Actions
{
    /// <summary>Builds the complete Stage 10-D2 through D5 handler set.</summary>
    public static class AllActionHandlers
    {
        public static IReadOnlyList<IActionHandler> Create()
        {
            var handlers = new List<IActionHandler>();
            handlers.AddRange(ShopEconomyActionHandlers.Create());
            handlers.AddRange(BoardHandActionHandlers.Create());
            handlers.AddRange(StageItemEventActionHandlers.Create());
            handlers.Add(new StartBattleActionHandler());
            return new ReadOnlyCollection<IActionHandler>(handlers);
        }
    }

    /// <summary>Starts one battle by atomically proposing the stage transition and initial BattleState.</summary>
    public sealed class StartBattleActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.StartBattle;

        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action,
            ActionExecutionContext context)
        {
            if (!(action is StartBattleAction))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction,
                    "Expected StartBattleAction.");
            if (state == null || context == null)
                return ActionHandlerResult.ConfigurationError("Action state or context is unavailable.");
            if (state.Stage.Phase != StageProgressPhase.Preparation)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidPhase,
                    "Battle can only start during Preparation.");
            if (context.CharacterDefinitions == null || context.SelectedRoster == null ||
                context.FullCharacterRoster == null || context.EnemyDefinitions == null)
                return ActionHandlerResult.ConfigurationError(
                    "Character definitions, selected roster, full roster, and enemy definitions are required.");

            try
            {
                StageProgressResult begin = StageProgressRules.BeginBattle(state.Stage);
                if (!begin.Succeeded)
                    return ActionHandlerResult.Reject(MapStageFailure(begin.Failure),
                        "StageProgressRules rejected battle start: " + begin.Failure + ".");

                BattleStartFailure startFailure = BattleStartRules.Validate(state.Economy,
                    begin.NextState, state.Board, state.Hand, state.Ownership, state.Battle);
                if (startFailure != BattleStartFailure.None)
                    return MapStartFailure(startFailure);

                if (state.EnemyBoard == null)
                    return ActionHandlerResult.ConfigurationError(
                        "EnemyBoardState is unavailable for the selected combat encounter.");
                if (!TryCreatePreparedEnemyBoard(state, context, out EnemyBoardState preparedEnemyBoard,
                    out string enemyBoardError))
                    return ActionHandlerResult.ConfigurationError(enemyBoardError);

                // BattleConfig's public constructor rejects invalid limits. A missing optional
                // config intentionally follows BattleInitializationRules' established default.
                BattleInitializationResult initialized = BattleInitializationRules.Initialize(
                    state.Economy, begin.NextState, state.Board, state.Hand, state.Ownership,
                    context.CharacterDefinitions, preparedEnemyBoard, context.EnemyDefinitions,
                    state.ItemEffects, context.SelectedRoster, context.FullCharacterRoster,
                    state.RandomState.Battle, context.BattleConfig ?? BattleConfig.Default,
                    state.Battle);
                if (!initialized.Succeeded)
                    return MapInitializationFailure(initialized.Failure);

                SimulationRandomStreamsState nextRandom = ReplaceBattle(state.RandomState,
                    initialized.NextBattleRandomState);
                var nextState = new ActionExecutionState(begin.NextState, state.Economy,
                    state.CharacterPool, state.InstanceFactory, state.Board, state.Hand,
                    state.Ownership, state.ItemEffects, nextRandom, state.Shop, state.EventItem,
                    state.EnemyBoard, state.EnemyPreparation, initialized.State, null,
                    state.ExecutedActionIds);
                return ActionHandlerResult.Success(nextState, RandomStreamMask.None);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException ||
                ex is KeyNotFoundException || ex is OverflowException)
            {
                return ActionHandlerResult.ConfigurationError(
                    "Battle start failed before commit: " + ex.Message);
            }
        }

        private static bool TryCreatePreparedEnemyBoard(ActionExecutionState state,
            ActionExecutionContext context, out EnemyBoardState prepared, out string error)
        {
            prepared = null;
            error = null;
            EnemyBoardState board = state.EnemyBoard;
            if (board.Columns != EnemyBoardState.StandardColumns ||
                board.Rows != EnemyBoardState.StandardRows ||
                board.SlotCount != EnemyInstanceFactory.SlotCount)
            {
                error = "EnemyBoardState must use the standard 2x3 battle formation.";
                return false;
            }

            var slots = new List<EnemyFormationSlotDefinition>(EnemyInstanceFactory.SlotCount);
            for (int i = 0; i < EnemyInstanceFactory.SlotCount; i++)
            {
                if (!board.TryGetAt(i, out EnemyInstance enemy))
                {
                    error = "EnemyBoardState contains an invalid slot.";
                    return false;
                }
                slots.Add(enemy == null
                    ? new EnemyFormationSlotDefinition(null, 0, 0)
                    : new EnemyFormationSlotDefinition(enemy.DefinitionId, enemy.Gap, enemy.Cooldown));
            }

            var formation = new EnemyFormationDefinition(
                "battle-start-" + state.Stage.EncounterIndex, slots);
            EnemyFormationResult result = EnemyFormationRules.CreateBoard(formation,
                context.EnemyDefinitions, state.Stage.EncounterIndex,
                state.EnemyPreparation ?? new EnemyPreparationState());
            if (!result.Succeeded)
            {
                error = "Could not materialize prepared enemies: " + result.Failure + ".";
                return false;
            }

            // EnemyPreparation is the separate source of pre-battle modifiers. Keep the
            // aggregate's base EnemyBoard unchanged; BattleState receives this projected board.
            prepared = result.Board;
            return true;
        }

        private static ActionErrorCode MapStageFailure(StageProgressFailure failure) =>
            failure == StageProgressFailure.NoEnemyEncounterRequired
                ? ActionErrorCode.InvalidPhase
                : failure == StageProgressFailure.NotAwaitingBattle
                    ? ActionErrorCode.InvalidPhase
                    : ActionErrorCode.InvalidState;

        private static ActionHandlerResult MapStartFailure(BattleStartFailure failure)
        {
            switch (failure)
            {
                case BattleStartFailure.BoardExceedsLevel:
                    return ActionHandlerResult.Reject(ActionErrorCode.BoardLimitExceeded,
                        "Board character count exceeds Player Level.");
                case BattleStartFailure.BattleAlreadyInProgress:
                    return ActionHandlerResult.Reject(ActionErrorCode.BattleInProgress,
                        "A battle is already in progress.");
                case BattleStartFailure.NoEnemyEncounterRequired:
                case BattleStartFailure.StageNotReady:
                    return ActionHandlerResult.Reject(ActionErrorCode.InvalidPhase,
                        "The current selection does not permit a battle start.");
                case BattleStartFailure.InvalidDefinitions:
                case BattleStartFailure.MissingCharacterDefinition:
                case BattleStartFailure.MissingEnemyDefinition:
                    return ActionHandlerResult.ConfigurationError(
                        "Battle definitions are invalid or incomplete: " + failure + ".");
                default:
                    return ActionHandlerResult.Reject(ActionErrorCode.InvalidState,
                        "BattleStartRules rejected the state: " + failure + ".");
            }
        }

        private static ActionHandlerResult MapInitializationFailure(BattleStartFailure failure)
        {
            ActionHandlerResult mapped = MapStartFailure(failure);
            if (mapped.Status == ActionHandlerResultStatus.Rejected ||
                mapped.Status == ActionHandlerResultStatus.ConfigurationError)
                return mapped;
            return ActionHandlerResult.Reject(ActionErrorCode.InvalidState,
                "BattleInitializationRules rejected initialization: " + failure + ".");
        }

        private static SimulationRandomStreamsState ReplaceBattle(
            SimulationRandomStreamsState state, Pcg32State battle) =>
            new SimulationRandomStreamsState(state.TrialSeed, state.Version,
                state.Shop, state.Stats, state.Encounter, battle, state.Event,
                state.Roster, state.Economy);
    }
}
