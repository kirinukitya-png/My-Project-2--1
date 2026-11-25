using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Sessions
{
    internal sealed class BattleSessionAdvanceCalculation
    {
        public bool Succeeded { get; }
        public BattleAdvanceFailure Failure { get; }
        public string Detail { get; }
        public int RoundsAdvanced { get; }
        public ActionExecutionState NextState { get; }

        private BattleSessionAdvanceCalculation(bool succeeded, BattleAdvanceFailure failure,
            string detail, int roundsAdvanced, ActionExecutionState nextState)
        {
            Succeeded = succeeded;
            Failure = failure;
            Detail = detail ?? string.Empty;
            RoundsAdvanced = roundsAdvanced;
            NextState = nextState;
        }

        public static BattleSessionAdvanceCalculation Success(ActionExecutionState state, int rounds) =>
            new BattleSessionAdvanceCalculation(true, BattleAdvanceFailure.None, string.Empty, rounds, state);
        public static BattleSessionAdvanceCalculation Fail(BattleAdvanceFailure failure, string detail, int rounds = 0) =>
            new BattleSessionAdvanceCalculation(false, failure, detail, rounds, null);
    }

    /// <summary>Stateless orchestration over existing BattleRules. The caller adopts one aggregate only on success.</summary>
    internal static class BattleSessionAdvancer
    {
        public static BattleSessionAdvanceCalculation Advance(ActionExecutionState initial,
            ActionExecutionContext context, StageProgressConfig stageConfig, bool toEnd)
        {
            if (initial == null || context == null || stageConfig == null || initial.Battle == null)
                return BattleSessionAdvanceCalculation.Fail(BattleAdvanceFailure.BattleStateMissing,
                    "Battle state or battle configuration is unavailable.");
            if (initial.Stage.Phase != StageProgressPhase.Battle)
                return BattleSessionAdvanceCalculation.Fail(BattleAdvanceFailure.InvalidPhase,
                    "StageProgressState is not in Battle phase.");
            if (initial.Battle.Status == BattleExecutionStatus.Completed ||
                initial.Battle.Phase == BattlePhase.Completed || initial.BattleResult != null)
                return BattleSessionAdvanceCalculation.Fail(BattleAdvanceFailure.BattleAlreadyFinished,
                    "Battle is already completed.");

            Dictionary<string, BattleTargetDefinition> targets;
            Dictionary<string, BattleEffectDefinition> effects;
            try
            {
                targets = Index(context.BattleTargetDefinitions, x => x.TargetId, "Target");
                effects = Index(context.BattleEffectDefinitions, x => x.EffectId, "Effect");
            }
            catch (ArgumentException ex)
            {
                return BattleSessionAdvanceCalculation.Fail(BattleAdvanceFailure.InvalidConfiguration, ex.Message);
            }

            ActionExecutionState working = initial;
            int initialRound = initial.Battle.CurrentRound;
            bool roundActive = initial.Battle.Phase != BattlePhase.BattleStart &&
                initial.Battle.Phase != BattlePhase.RoundStart &&
                initial.Battle.Phase != BattlePhase.RoundLimitReached &&
                initial.Battle.Phase != BattlePhase.Completed;
            int fullyCompletedRounds = 0;
            long stepLimit = CalculateStepLimit(initial.Battle, toEnd);

            for (long step = 0; step < stepLimit; step++)
            {
                BattleScheduleResult request = BattleTurnRules.RequestNext(working.Battle);
                if (!request.Succeeded)
                    return RuleFailure("BattleTurnRules.RequestNext", request.Failure.ToString(),
                        fullyCompletedRounds + (roundActive ? 1 : 0));
                working = AdoptBattle(working, request.NextState);

                switch (request.Event)
                {
                    case BattleScheduleEvent.RoundStarted:
                        roundActive = true;
                        break;

                    case BattleScheduleEvent.AllySkippedByStun:
                    case BattleScheduleEvent.AllySkippedByParticipationLimit:
                    case BattleScheduleEvent.EnemyWaiting:
                    case BattleScheduleEvent.EnemySkippedDefeated:
                    case BattleScheduleEvent.AllyTurnCompleted:
                    case BattleScheduleEvent.EnemyTurnCompleted:
                        break;

                    case BattleScheduleEvent.BattleStartRequired:
                    case BattleScheduleEvent.EnemyTurnStartRequired:
                    {
                        BattleScheduleResult boundary = BattleTurnRules.ResolveAction(working.Battle,
                            BattleActionResolution.BoundaryCompleted(request.ActionRequestId));
                        if (!boundary.Succeeded)
                            return RuleFailure("BattleTurnRules.ResolveAction(boundary)", boundary.Failure.ToString(),
                                fullyCompletedRounds + (roundActive ? 1 : 0));
                        working = AdoptBattle(working, boundary.NextState);
                        working = ResolvePendingEffects(working, effects, out string effectError);
                        if (effectError != null)
                            return RuleFailure("BattleEffectRules", effectError,
                                fullyCompletedRounds + (roundActive ? 1 : 0));
                        break;
                    }

                    case BattleScheduleEvent.AllyActionRequired:
                    {
                        BattleUnitMoveDecision decision = BattleUnitMoveRules.DecideCurrentAllyAction(
                            working.Battle, request.ActionRequestId);
                        if (!decision.Succeeded)
                            return RuleFailure("BattleUnitMoveRules.DecideCurrentAllyAction", decision.ScheduleFailure.ToString(),
                                fullyCompletedRounds + (roundActive ? 1 : 0));

                        if (decision.Kind == BattleUnitMoveDecisionKind.MoveAvailable)
                        {
                            BattleAttackResult move = BattleUnitMoveRules.ResolveUnitMoveAttack(
                                working.Battle, request.ActionRequestId, decision.SelectedMove.MoveId);
                            if (!move.Succeeded)
                                return RuleFailure("BattleUnitMoveRules.ResolveUnitMoveAttack", move.Failure.ToString(),
                                    fullyCompletedRounds + (roundActive ? 1 : 0));
                            working = AdoptBattle(working, move.NextState);
                        }
                        else if (decision.Kind == BattleUnitMoveDecisionKind.SkipNormalAttackAfterMove)
                        {
                            working = AdoptBattle(working, decision.NextState);
                        }
                        else
                        {
                            BattleAttackResult attack = BattleNormalAttackRules.ResolveAllyNormalAttack(
                                working.Battle, request.ActionRequestId);
                            if (!attack.Succeeded)
                                return RuleFailure("BattleNormalAttackRules.ResolveAllyNormalAttack", attack.Failure.ToString(),
                                    fullyCompletedRounds + (roundActive ? 1 : 0));
                            working = AdoptBattle(working, attack.NextState);
                        }

                        working = ResolvePendingEffects(working, effects, out string allyEffectError);
                        if (allyEffectError != null)
                            return RuleFailure("BattleEffectRules", allyEffectError,
                                fullyCompletedRounds + (roundActive ? 1 : 0));
                        break;
                    }

                    case BattleScheduleEvent.EnemyActionRequired:
                    {
                        BattleAttackResult attack = BattleNormalAttackRules.ResolveEnemyNormalAttack(
                            working.Battle, request.ActionRequestId, targets,
                            BoardState.StandardColumns, 2, BoardState.StandardColumns * BoardState.StandardRows);
                        if (!attack.Succeeded)
                            return RuleFailure("BattleNormalAttackRules.ResolveEnemyNormalAttack", attack.Failure.ToString(),
                                fullyCompletedRounds + (roundActive ? 1 : 0));
                        working = AdoptBattle(working, attack.NextState);
                        working = ResolvePendingEffects(working, effects, out string enemyEffectError);
                        if (enemyEffectError != null)
                            return RuleFailure("BattleEffectRules", enemyEffectError,
                                fullyCompletedRounds + (roundActive ? 1 : 0));
                        break;
                    }

                    case BattleScheduleEvent.PostActionEffectsRequired:
                    {
                        working = ResolvePendingEffects(working, effects, out string pendingError);
                        if (pendingError != null)
                            return RuleFailure("BattleEffectRules", pendingError,
                                fullyCompletedRounds + (roundActive ? 1 : 0));
                        break;
                    }

                    case BattleScheduleEvent.RoundCompleted:
                        fullyCompletedRounds++;
                        roundActive = false;
                        if (!toEnd)
                            return BattleSessionAdvanceCalculation.Success(working, fullyCompletedRounds);
                        break;

                    case BattleScheduleEvent.BattleOutcomeCheckRequired:
                    case BattleScheduleEvent.RoundLimitReached:
                        return FinishBattle(working, stageConfig,
                            fullyCompletedRounds + (roundActive ? 1 : 0));

                    case BattleScheduleEvent.None:
                        return BattleSessionAdvanceCalculation.Fail(BattleAdvanceFailure.RuleRejected,
                            "BattleTurnRules returned no progress event.",
                            fullyCompletedRounds + (roundActive ? 1 : 0));

                    default:
                        return BattleSessionAdvanceCalculation.Fail(BattleAdvanceFailure.RuleRejected,
                            "Unsupported BattleScheduleEvent: " + request.Event + ".",
                            fullyCompletedRounds + (roundActive ? 1 : 0));
                }
            }

            int progressed = Math.Max(fullyCompletedRounds,
                working.Battle.CurrentRound - initialRound + (roundActive ? 1 : 0));
            return BattleSessionAdvanceCalculation.Fail(BattleAdvanceFailure.SafetyLimitReached,
                "Battle progression exceeded a safety bound derived from BattleState.MaxRounds and participant counts.",
                Math.Max(0, progressed));
        }

        private static ActionExecutionState ResolvePendingEffects(ActionExecutionState state,
            IReadOnlyDictionary<string, BattleEffectDefinition> definitions, out string error)
        {
            error = null;
            while (state.Battle.PendingEffectTriggers.Count > 0)
            {
                BattlePendingEffectTrigger trigger = state.Battle.PendingEffectTriggers[0];
                BattleEffectResolutionResult effect = BattleEffectRules.ResolveNextPendingTrigger(
                    state.Battle, state.Economy, trigger.TriggerId, definitions);
                if (!effect.Succeeded)
                {
                    error = effect.Failure + " for trigger '" + trigger.TriggerId + "'.";
                    return state;
                }
                state = AdoptBattle(state, effect.NextState,
                    economy: effect.NextEconomyState);
            }
            return state;
        }

        private static BattleSessionAdvanceCalculation FinishBattle(ActionExecutionState state,
            StageProgressConfig stageConfig, int roundsAdvanced)
        {
            BattleFinishResult finish = BattleFinishRules.TryFinish(state.Battle, state.Economy,
                state.Board, state.Hand, state.Ownership, state.ItemEffects);
            if (!finish.Succeeded)
                return RuleFailure("BattleFinishRules.TryFinish", finish.Failure.ToString(), roundsAdvanced);

            StageProgressState nextStage = state.Stage;
            if (finish.Result.Outcome == BattleOutcome.Victory || finish.Result.Outcome == BattleOutcome.Defeat)
            {
                StageProgressResult resolved = StageProgressRules.ResolveBattle(state.Stage,
                    stageConfig, finish.Result.Outcome == BattleOutcome.Victory);
                if (!resolved.Succeeded)
                    return RuleFailure("StageProgressRules.ResolveBattle", resolved.Failure.ToString(), roundsAdvanced);
                nextStage = resolved.NextState;
            }
            // There is no StageProgressPhase for Unresolved. Keep Battle phase, but store the
            // completed BattleState and distinct Unresolved result for Stage 11-C to handle.

            SimulationRandomStreamsState nextRandom = ReplaceBattleRandom(state.RandomState,
                finish.NextBattleRandomState);
            ActionExecutionState next;
            try
            {
                next = new ActionExecutionState(nextStage, finish.NextEconomyState,
                    state.CharacterPool, state.InstanceFactory, finish.NextBoard, finish.NextHand,
                    finish.NextOwnership, finish.NextItemEffectState, nextRandom,
                    state.Shop, state.EventItem, state.EnemyBoard, state.EnemyPreparation,
                    finish.NextBattleState, finish.Result, state.ExecutedActionIds);
            }
            catch (ArgumentException ex)
            {
                return RuleFailure("ActionExecutionState", ex.Message, roundsAdvanced);
            }
            return BattleSessionAdvanceCalculation.Success(next, roundsAdvanced);
        }

        private static ActionExecutionState AdoptBattle(ActionExecutionState state,
            BattleState battle, EconomyState economy = null)
        {
            SimulationRandomStreamsState random = ReplaceBattleRandom(state.RandomState,
                battle.BattleRandomState);
            return state.With(battle: battle, economy: economy, randomState: random);
        }

        private static SimulationRandomStreamsState ReplaceBattleRandom(
            SimulationRandomStreamsState state, Pcg32State battle) =>
            new SimulationRandomStreamsState(state.TrialSeed, state.Version,
                state.Shop, state.Stats, state.Encounter, battle, state.Event,
                state.Roster, state.Economy);

        private static Dictionary<string, T> Index<T>(IReadOnlyList<T> definitions,
            Func<T, string> getId, string label)
            where T : class
        {
            var indexed = new Dictionary<string, T>(StringComparer.Ordinal);
            if (definitions == null) return indexed;
            foreach (T item in definitions)
            {
                if (item == null) throw new ArgumentException(label + " definitions cannot contain null values.");
                string id = getId(item);
                if (string.IsNullOrWhiteSpace(id) || indexed.ContainsKey(id))
                    throw new ArgumentException(label + " definitions must have unique non-empty IDs.");
                indexed.Add(id, item);
            }
            return indexed;
        }

        private static long CalculateStepLimit(BattleState battle, bool toEnd)
        {
            long allies = battle.Allies.Count;
            long enemies = battle.Enemies.Count;
            long moves = battle.UnitMoves.Count;
            long effectIds = 0;
            foreach (BattleUnitState enemy in battle.Enemies)
                effectIds += enemy.CipEffectIds.Count + enemy.AeEffectIds.Count + enemy.PigEffectIds.Count;
            long perRound = 64L + 32L * (allies + enemies) + 12L * moves + 4L * effectIds;
            if (!toEnd) return perRound;
            long remainingRounds = Math.Max(1, (long)battle.MaxRounds - battle.CurrentRound + 1L);
            return remainingRounds * perRound + 32L + 4L * enemies;
        }

        private static BattleSessionAdvanceCalculation RuleFailure(string rule, string detail, int rounds) =>
            BattleSessionAdvanceCalculation.Fail(BattleAdvanceFailure.RuleRejected,
                rule + " rejected progression: " + detail + ".", Math.Max(0, rounds));
    }
}
