using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Sessions
{
    /// <summary>
    /// Owns the single immutable ActionExecutionState for one game trial. Strategy decisions,
    /// StrategyState, Strategy RNG, and decision counters deliberately remain outside this type.
    /// </summary>
    public sealed class GameSession
    {
        private readonly object _gate = new object();
        private readonly GameSessionConfiguration _configuration;
        private readonly ActionExecutionContext _actionContext;
        private readonly ActionDispatcher _dispatcher;
        private ActionExecutionState _state;

        public ActionExecutionState CurrentState
        {
            get { lock (_gate) return _state; }
        }

        public StageProgressPhase Phase => CurrentState.Stage.Phase;

        private GameSession(GameSessionConfiguration configuration, ActionExecutionContext actionContext,
            ActionExecutionState state, ActionDispatcher dispatcher)
        {
            _configuration = configuration;
            _actionContext = actionContext;
            _state = state;
            _dispatcher = dispatcher ?? new ActionDispatcher(AllActionHandlers.Create());
        }

        /// <summary>Creates a complete initial state locally, then publishes the session only on success.</summary>
        public static GameSession Create(GameSessionConfiguration configuration, ulong trialSeed,
            ActionDispatcher dispatcher = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            ValidateDefinitions(configuration);

            var streams = new SimulationRandomStreams(trialSeed);
            AssignedCharacterCosts costs = CharacterCostAssigner.Assign(configuration.SelectedRoster,
                configuration.CharacterShopConfig, streams.Roster);
            CharacterPool pool = CharacterShopRules.CreateInitialPool(configuration.SelectedRoster,
                costs, configuration.CharacterShopConfig);
            EconomyState economy = configuration.StartingEconomy;
            StageProgressState stage = StageProgressRules.CreateInitialState(configuration.StageConfig);
            StageProgressResult begun = StageProgressRules.BeginCurrentEncounter(stage, configuration.StageConfig);
            if (!begun.Succeeded) throw new ArgumentException("Initial Stage could not begin: " + begun.Failure + ".", nameof(configuration));
            stage = begun.NextState;

            ShopState shop = null;
            EnemyBoardState enemyBoard = null;
            EnemyPreparationState enemyPreparation = null;
            if (stage.Phase == StageProgressPhase.Preparation)
            {
                int stageNumber = configuration.StageConfig.GetStageNumber(stage.EncounterIndex);
                int roundNumber = configuration.StageConfig.GetRoundNumber(stage.EncounterIndex);
                if (stage.CurrentSelection == null) throw new ArgumentException("Initial Preparation has no Stage selection.", nameof(configuration));

                if (stage.CurrentSelection.ShopMode == StageShopMode.Character)
                {
                    CharacterShopGenerationResult generated = CharacterShopRules.Generate(
                        configuration.SelectedRoster, costs, pool, configuration.CharacterShopConfig,
                        stageNumber, roundNumber, 0, streams.Shop);
                    shop = generated.Shop;
                    pool = generated.Pool;
                }
                else if (stage.CurrentSelection.ShopMode == StageShopMode.Item)
                {
                    int slots = configuration.CharacterShopConfig.GetShopSlotCount(stageNumber);
                    ItemShopGenerationResult generated = ItemShopRules.Generate(null, pool,
                        configuration.ItemDefinitions, stageNumber, roundNumber, slots, 0, streams.Shop);
                    shop = generated.NextShop;
                    pool = generated.NextCharacterPool;
                    streams.Shop.RestoreState(generated.NextShopRandomState);
                }
                else throw new ArgumentException("Initial Stage selected an unsupported shop mode.", nameof(configuration));

                if (stage.CurrentSelection.SelectionEffect != null &&
                    stage.CurrentSelection.SelectionEffect.Kind != StageSelectionEffectKind.None)
                {
                    StageRewardResult selectionEffect = StageRewardRules.ApplySelectionEffect(stage, economy);
                    if (!selectionEffect.Succeeded) throw new ArgumentException("Initial Stage selection effect failed: " + selectionEffect.Failure + ".", nameof(configuration));
                    stage = selectionEffect.NextStageState;
                    economy = selectionEffect.NextEconomyState;
                }

                EnemyEncounterSetupResult encounter = EnemyEncounterSetupRules.Generate(stage,
                    configuration.EncounterPools, configuration.EnemyDefinitions,
                    new EnemyPreparationState(), streams.Encounter.CaptureState());
                if (!encounter.Succeeded)
                    throw new ArgumentException("Initial encounter setup failed: " + encounter.Failure + ".", nameof(configuration));
                enemyBoard = encounter.Board;
                enemyPreparation = new EnemyPreparationState();
                if (encounter.EncounterRandomConsumed) streams.Encounter.RestoreState(encounter.NextEncounterRandomState);
            }

            var state = new ActionExecutionState(stage, economy, pool,
                new CharacterInstanceFactory(configuration.InstanceIdPrefix),
                BoardState.CreateEmpty(configuration.BoardColumns, configuration.BoardRows),
                HandState.CreateEmpty(configuration.HandSlotCount),
                new CharacterOwnershipState(Array.Empty<CharacterInstance>()),
                new ItemEffectState(), streams.CaptureState(), shop,
                enemyBoard: enemyBoard, enemyPreparation: enemyPreparation);
            ActionExecutionContext context = configuration.CreateActionContext(costs);
            ValidateState(configuration, context, state);
            return new GameSession(configuration, context, state, dispatcher);
        }

        /// <summary>Restores an in-memory checkpoint while preserving its RNG and Action ID history.</summary>
        public static GameSession Restore(GameSessionConfiguration configuration,
            ActionExecutionState checkpoint, ActionDispatcher dispatcher = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            ValidateDefinitions(configuration);

            // Assigned costs are immutable run data not stored in ActionExecutionState. Re-derive
            // them from the same TrialSeed and fresh Roster stream; keep the supplied checkpoint intact.
            var seedStreams = new SimulationRandomStreams(checkpoint.RandomState.TrialSeed);
            AssignedCharacterCosts costs = CharacterCostAssigner.Assign(configuration.SelectedRoster,
                configuration.CharacterShopConfig, seedStreams.Roster);
            ActionExecutionContext context = configuration.CreateActionContext(costs);
            ValidateState(configuration, context, checkpoint);
            return new GameSession(configuration, context, checkpoint, dispatcher);
        }

        /// <summary>Builds a detached public observation from the currently owned state.</summary>
        public ObservationBuildResult GetObservation()
        {
            lock (_gate)
            {
                return ObservationBuilder.Build(new ObservationSource(_state.Stage,
                    _configuration.StageConfig, _state.Economy, _configuration.EconomyConfig,
                    _state.Board, _state.Hand, _state.Ownership,
                    _configuration.CharacterDefinitions, _configuration.SelectedRoster,
                    _configuration.FullCharacterRoster, _configuration.ObservationItemDefinitions,
                    _state.Shop, _state.ItemEffects, _state.EventItem,
                    _configuration.EnemyDefinitions, _state.EnemyBoard,
                    _state.EnemyPreparation, _state.Battle, GetObservableBattleResult(_state),
                    _configuration.CharacterStatSettings, _configuration.CharacterShopConfig,
                    _configuration.StageRewardConfig, _configuration.ShopRerollGoldCost,
                    _configuration.SelectedRosterObservation)
                {
                    CachedCharacterStats = _configuration.PublicCharacterStats,
                    CachedRosterRules = _configuration.RosterRules
                });
            }
        }

        /// <summary>Delegates validation to the existing Dispatcher and adopts only successful NextState.</summary>
        public ActionDispatchResult ExecuteAction(GameAction action)
        {
            lock (_gate)
            {
                ActionDispatchResult result = _dispatcher.Dispatch(_state, action, _actionContext);
                if (result.Succeeded) _state = result.NextState;
                return result;
            }
        }

        /// <summary>Runs the current battle through one round boundary, or until the battle ends.</summary>
        public BattleAdvanceResult AdvanceBattleRound() => AdvanceBattle(advanceToEnd: false);

        /// <summary>Runs the current battle to its existing BattleRules terminal condition atomically.</summary>
        public BattleAdvanceResult AdvanceBattleToEnd() => AdvanceBattle(advanceToEnd: true);

        private BattleAdvanceResult AdvanceBattle(bool advanceToEnd)
        {
            lock (_gate)
            {
                if (_state.Battle != null && (_state.Battle.Status == BattleExecutionStatus.Completed ||
                    _state.Battle.Phase == BattlePhase.Completed || _state.BattleResult != null))
                    return BattleAdvanceResult.Rejected(BattleAdvanceFailure.BattleAlreadyFinished,
                        "The current Battle is already finished.");
                if (_state.Stage.Phase != StageProgressPhase.Battle)
                    return BattleAdvanceResult.Rejected(BattleAdvanceFailure.InvalidPhase,
                        "Battle advancement is only allowed during the Battle phase.");
                if (_state.Battle == null)
                    return BattleAdvanceResult.Rejected(BattleAdvanceFailure.BattleStateMissing,
                        "Battle phase has no BattleState.");

                try
                {
                    BattleSessionAdvanceCalculation calculation = BattleSessionAdvancer.Advance(
                        _state, _actionContext, _configuration.StageConfig, advanceToEnd);
                    if (!calculation.Succeeded)
                        return BattleAdvanceResult.Rejected(calculation.Failure, calculation.Detail,
                            calculation.RoundsAdvanced, _state.BattleResult);
                    ActionExecutionValidationResult validation = ActionExecutionStateValidator.Validate(calculation.NextState);
                    if (!validation.IsValid)
                        return BattleAdvanceResult.InternalError(
                            "Battle progression produced an invalid state: " + string.Join("; ", validation.Errors));
                    _state = calculation.NextState;
                    return BattleAdvanceResult.Success(calculation.RoundsAdvanced,
                        _state.Battle != null && _state.Battle.Status == BattleExecutionStatus.InProgress,
                        _state.BattleResult);
                }
                catch (Exception ex)
                {
                    return BattleAdvanceResult.InternalError("Battle progression failed without committing state: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Applies a Victory's existing reward and progression rules once. If the event roll
        /// starts an event, the caller must provide its already-selected Item Definition ID;
        /// this layer does not invent an event-item pool or draw rule.
        /// </summary>
        public StageSessionResult ResolveStageAfterBattle(string eventItemDefinitionId = null)
        {
            lock (_gate)
            {
                if (_state.BattleResult != null && _state.BattleResult.Outcome == BattleOutcome.Unresolved)
                    return StageSessionResult.Create(StageSessionStatus.StoppedUnresolved,
                        StageSessionFailure.None, "Unresolved battles require a later explicit policy.",
                        null, false, false, _state.Stage.Phase);
                if (_state.Stage.Phase == StageProgressPhase.GameOver && _state.BattleResult != null &&
                    _state.BattleResult.Outcome == BattleOutcome.Defeat)
                    return StageSessionResult.Create(StageSessionStatus.GameOver,
                        StageSessionFailure.None, "Defeat was already applied by BattleFinishRules.",
                        null, false, false, _state.Stage.Phase);
                if (_state.BattleResult == null)
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.MissingBattleResult, "No completed battle result is available.",
                        null, false, false, _state.Stage.Phase);
                if (_state.BattleResult.Outcome != BattleOutcome.Victory ||
                    _state.Stage.Phase != StageProgressPhase.RewardPending)
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.BattleOutcomeMismatch,
                        "Only a Victory in RewardPending can receive a stage reward.",
                        null, false, false, _state.Stage.Phase);

                return ApplyVictoryReward(_state.Stage, eventItemDefinitionId);
            }
        }

        /// <summary>Resolves SelectB/SelectD-style no-enemy preparation using the existing Stage and reward rules.</summary>
        public StageSessionResult ResolveEnemylessStage(string eventItemDefinitionId = null)
        {
            lock (_gate)
            {
                if (_state.Stage.Phase != StageProgressPhase.Preparation ||
                    _state.Stage.CurrentSelection == null ||
                    _state.Stage.CurrentSelection.EnemyCategory != StageEnemyCategory.None ||
                    _state.Battle != null || _state.BattleResult != null)
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.InvalidPhase,
                        "Enemyless resolution requires a no-enemy Preparation with no BattleState.",
                        null, false, false, _state.Stage.Phase);

                StageProgressResult noEnemy = StageProgressRules.ResolveNoEnemySelectionAsVictory(
                    _state.Stage, _configuration.StageConfig);
                if (!noEnemy.Succeeded)
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.RuleRejected,
                        "StageProgressRules rejected enemyless completion: " + noEnemy.Failure + ".",
                        null, false, false, _state.Stage.Phase);
                return ApplyVictoryReward(noEnemy.NextState, eventItemDefinitionId);
            }
        }

        /// <summary>
        /// Begins the next Encounter after reward/event completion. It generates only the current
        /// shop and encounter using their existing Rules and adopts all resulting checkpoints together.
        /// </summary>
        public StageSessionResult AdvanceToNextStage()
        {
            lock (_gate)
            {
                if (_state.Stage.Phase == StageProgressPhase.Completed)
                    return StageSessionResult.Create(StageSessionStatus.Completed,
                        StageSessionFailure.None, "The run is already complete.", null,
                        false, false, _state.Stage.Phase);
                if (_state.Stage.Phase == StageProgressPhase.GameOver)
                    return StageSessionResult.Create(StageSessionStatus.GameOver,
                        StageSessionFailure.None, "The run has already ended in GameOver.", null,
                        false, false, _state.Stage.Phase);
                if (_state.BattleResult != null && _state.BattleResult.Outcome == BattleOutcome.Unresolved)
                    return StageSessionResult.Create(StageSessionStatus.StoppedUnresolved,
                        StageSessionFailure.None, "An Unresolved battle cannot advance to another Encounter.",
                        null, false, false, _state.Stage.Phase);
                if (_state.Stage.Phase == StageProgressPhase.EventPending)
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.InvalidPhase, "The Event must be ended before the next Encounter begins.",
                        null, false, false, _state.Stage.Phase);
                if (_state.Stage.Phase != StageProgressPhase.AwaitingEncounterStart)
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.InvalidPhase,
                        "Next Encounter can begin only from AwaitingEncounterStart.",
                        null, false, false, _state.Stage.Phase);
                if (_state.EventItem != null && _state.EventItem.IsAvailable)
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.ActiveEventMustEndFirst,
                        "An available Event Item must be consumed or ended first.",
                        null, false, false, _state.Stage.Phase);

                try
                {
                    StageProgressResult begun = StageProgressRules.BeginCurrentEncounter(
                        _state.Stage, _configuration.StageConfig);
                    if (!begun.Succeeded)
                        return StageSessionResult.Create(StageSessionStatus.Rejected,
                            StageSessionFailure.RuleRejected,
                            "StageProgressRules rejected the next Encounter: " + begun.Failure + ".",
                            null, false, false, _state.Stage.Phase);

                    StageProgressState nextStage = begun.NextState;
                    CharacterPool nextPool = _state.CharacterPool;
                    SimulationRandomStreamsState nextRandom = _state.RandomState;
                    ShopState nextShop = null;
                    EnemyBoardState nextEnemyBoard = null;
                    EnemyPreparationState nextEnemyPreparation = null;
                    EconomyState nextEconomy = _state.Economy;

                    if (nextStage.Phase == StageProgressPhase.Preparation)
                    {
                        int stageNumber = _configuration.StageConfig.GetStageNumber(nextStage.EncounterIndex);
                        int roundNumber = _configuration.StageConfig.GetRoundNumber(nextStage.EncounterIndex);
                        Pcg32RandomSource shopRandom = Pcg32RandomSource.FromState(nextRandom.Shop);
                        if (nextStage.CurrentSelection.ShopMode == StageShopMode.Character)
                        {
                            nextPool = ReturnShopReservations(_state.Shop, nextPool);
                            CharacterShopGenerationResult generated = CharacterShopRules.Generate(
                                _configuration.SelectedRoster, _actionContext.AssignedCharacterCosts,
                                nextPool, _configuration.CharacterShopConfig, stageNumber,
                                roundNumber, 0, shopRandom);
                            nextShop = generated.Shop;
                            nextPool = generated.Pool;
                            nextRandom = ReplaceShopRandom(nextRandom, shopRandom.CaptureState());
                        }
                        else if (nextStage.CurrentSelection.ShopMode == StageShopMode.Item)
                        {
                            int slots = _configuration.CharacterShopConfig.GetShopSlotCount(stageNumber);
                            ItemShopGenerationResult generated = ItemShopRules.Generate(_state.Shop,
                                nextPool, _configuration.ItemDefinitions, stageNumber, roundNumber,
                                slots, 0, shopRandom);
                            nextShop = generated.NextShop;
                            nextPool = generated.NextCharacterPool;
                            nextRandom = ReplaceShopRandom(nextRandom, generated.NextShopRandomState);
                        }
                        else
                        {
                            return StageSessionResult.Create(StageSessionStatus.Rejected,
                                StageSessionFailure.ConfigurationError,
                                "Next Stage has an unsupported Shop mode.", null, false, false,
                                _state.Stage.Phase);
                        }

                        if (nextStage.CurrentSelection.SelectionEffect != null &&
                            nextStage.CurrentSelection.SelectionEffect.Kind != StageSelectionEffectKind.None)
                        {
                            StageRewardResult effect = StageRewardRules.ApplySelectionEffect(nextStage, nextEconomy);
                            if (!effect.Succeeded)
                                return StageSessionResult.Create(StageSessionStatus.Rejected,
                                    StageSessionFailure.RuleRejected,
                                    "Selection effect failed: " + effect.Failure + ".", null,
                                    false, false, _state.Stage.Phase);
                            nextStage = effect.NextStageState;
                            nextEconomy = effect.NextEconomyState;
                        }

                        EnemyEncounterSetupResult encounter = EnemyEncounterSetupRules.Generate(
                            nextStage, _configuration.EncounterPools, _configuration.EnemyDefinitions,
                            new EnemyPreparationState(), nextRandom.Encounter);
                        if (!encounter.Succeeded)
                            return StageSessionResult.Create(StageSessionStatus.Rejected,
                                StageSessionFailure.ConfigurationError,
                                "Next Encounter setup failed: " + encounter.Failure + ".",
                                null, false, false, _state.Stage.Phase);
                        nextEnemyBoard = encounter.Board;
                        nextEnemyPreparation = new EnemyPreparationState();
                        if (encounter.EncounterRandomConsumed)
                            nextRandom = ReplaceEncounterRandom(nextRandom, encounter.NextEncounterRandomState);
                    }
                    else
                    {
                        nextPool = ReturnShopReservations(_state.Shop, nextPool);
                    }

                    ActionExecutionState nextState = new ActionExecutionState(nextStage, nextEconomy,
                        nextPool, _state.InstanceFactory, _state.Board, _state.Hand, _state.Ownership,
                        _state.ItemEffects, nextRandom, nextShop, null, nextEnemyBoard,
                        nextEnemyPreparation, null, null, _state.ExecutedActionIds);
                    ActionExecutionValidationResult validation = ActionExecutionStateValidator.Validate(nextState);
                    if (!validation.IsValid)
                        return StageSessionResult.Create(StageSessionStatus.InternalError,
                            StageSessionFailure.InvalidNextState,
                            "Next Encounter state failed validation: " + string.Join("; ", validation.Errors),
                            null, false, false, _state.Stage.Phase);
                    ObservationBuildResult observation = BuildObservation(nextState);
                    if (!observation.Succeeded)
                        return StageSessionResult.Create(StageSessionStatus.InternalError,
                            StageSessionFailure.InvalidNextState,
                            "Next Encounter cannot produce an observation: " + string.Join("; ", observation.Errors),
                            null, false, false, _state.Stage.Phase);

                    _state = nextState;
                    return StageSessionResult.Create(nextStage.Phase == StageProgressPhase.Completed
                            ? StageSessionStatus.Completed : StageSessionStatus.Progressed,
                        StageSessionFailure.None, string.Empty, null, false, true, nextStage.Phase);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException ||
                    ex is KeyNotFoundException || ex is OverflowException)
                {
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.ConfigurationError,
                        "Next Encounter setup failed atomically: " + ex.Message,
                        null, false, false, _state.Stage.Phase);
                }
            }
        }

        private StageSessionResult ApplyVictoryReward(StageProgressState rewardPending,
            string eventItemDefinitionId)
        {
            StageRewardResult reward = StageRewardRules.ApplyVictoryReward(rewardPending,
                _configuration.StageConfig, _state.Economy, _configuration.EconomyConfig,
                _configuration.StageRewardConfig, _state.Hand, _state.RandomState.Economy,
                _state.RandomState.Event);
            if (!reward.Succeeded)
                return StageSessionResult.Create(StageSessionStatus.Rejected,
                    StageSessionFailure.RuleRejected,
                    "StageRewardRules rejected reward application: " + reward.Failure + ".",
                    reward, false, false, _state.Stage.Phase);

            EventItemState nextEventItem = null;
            if (reward.EventStarted)
            {
                if (string.IsNullOrWhiteSpace(eventItemDefinitionId))
                    return StageSessionResult.Create(StageSessionStatus.NeedsEventItemSelection,
                        StageSessionFailure.EventItemRequired,
                        "The existing rules rolled an Event, but no Event Item selection rule exists; provide a valid Item Definition ID and retry.",
                        reward, true, false, _state.Stage.Phase);
                ItemDefinition selected = null;
                foreach (ItemDefinition item in _configuration.ObservationItemDefinitions)
                    if (item.DefinitionId == eventItemDefinitionId) { selected = item; break; }
                if (selected == null || selected.Target != ItemEffectTarget.OneAlly)
                    return StageSessionResult.Create(StageSessionStatus.Rejected,
                        StageSessionFailure.InvalidEventItem,
                        "The supplied Event Item must exist in configuration and support the existing OneAlly Event Item rules.",
                        reward, true, false, _state.Stage.Phase);
                nextEventItem = new EventItemState(selected.DefinitionId);
            }

            CharacterPool pool = ReturnShopReservations(_state.Shop, _state.CharacterPool);
            SimulationRandomStreamsState random = _state.RandomState;
            if (reward.HasNextEconomyRandomState)
                random = ReplaceEconomyRandom(random, reward.NextEconomyRandomState);
            if (reward.HasNextEventRandomState)
                random = ReplaceEventRandom(random, reward.NextEventRandomState);

            bool finishedRun = reward.NextStageState.Phase == StageProgressPhase.Completed;
            ActionExecutionState nextState = new ActionExecutionState(reward.NextStageState,
                reward.NextEconomyState, pool, _state.InstanceFactory, _state.Board, _state.Hand,
                _state.Ownership, _state.ItemEffects, random,
                null, nextEventItem,
                finishedRun ? _state.EnemyBoard : null,
                finishedRun ? _state.EnemyPreparation : null,
                finishedRun ? _state.Battle : null,
                finishedRun ? _state.BattleResult : null, _state.ExecutedActionIds);
            ActionExecutionValidationResult validation = ActionExecutionStateValidator.Validate(nextState);
            if (!validation.IsValid)
                return StageSessionResult.Create(StageSessionStatus.InternalError,
                    StageSessionFailure.InvalidNextState,
                    "Reward application produced an invalid state: " + string.Join("; ", validation.Errors),
                    reward, reward.EventStarted, false, _state.Stage.Phase);
            ObservationBuildResult observation = BuildObservation(nextState);
            if (!observation.Succeeded)
                return StageSessionResult.Create(StageSessionStatus.InternalError,
                    StageSessionFailure.InvalidNextState,
                    "Reward state cannot produce an observation: " + string.Join("; ", observation.Errors),
                    reward, reward.EventStarted, false, _state.Stage.Phase);

            _state = nextState;
            StageSessionStatus status = nextState.Stage.Phase == StageProgressPhase.Completed
                ? StageSessionStatus.Completed
                : nextState.Stage.Phase == StageProgressPhase.GameOver
                    ? StageSessionStatus.GameOver : StageSessionStatus.Progressed;
            return StageSessionResult.Create(status, StageSessionFailure.None, string.Empty,
                reward, reward.EventStarted, true, nextState.Stage.Phase);
        }

        private ObservationBuildResult BuildObservation(ActionExecutionState state) =>
            ObservationBuilder.Build(new ObservationSource(state.Stage, _configuration.StageConfig,
                state.Economy, _configuration.EconomyConfig, state.Board, state.Hand,
                state.Ownership, _configuration.CharacterDefinitions, _configuration.SelectedRoster,
                _configuration.FullCharacterRoster, _configuration.ObservationItemDefinitions, state.Shop,
                state.ItemEffects, state.EventItem, _configuration.EnemyDefinitions,
                state.EnemyBoard, state.EnemyPreparation, state.Battle,
                GetObservableBattleResult(state), _configuration.CharacterStatSettings,
                _configuration.CharacterShopConfig, _configuration.StageRewardConfig,
                _configuration.ShopRerollGoldCost, _configuration.SelectedRosterObservation)
            {
                CachedCharacterStats = _configuration.PublicCharacterStats,
                CachedRosterRules = _configuration.RosterRules
            });

        private static CharacterPool ReturnShopReservations(ShopState shop, CharacterPool pool) =>
            shop != null && shop.Kind == ShopKind.Character
                ? CharacterShopRules.ReturnUnpurchasedOffers(shop, pool) : pool;

        private static SimulationRandomStreamsState ReplaceShopRandom(
            SimulationRandomStreamsState s, Pcg32State value) =>
            new SimulationRandomStreamsState(s.TrialSeed, s.Version, value, s.Stats,
                s.Encounter, s.Battle, s.Event, s.Roster, s.Economy);

        private static SimulationRandomStreamsState ReplaceEncounterRandom(
            SimulationRandomStreamsState s, Pcg32State value) =>
            new SimulationRandomStreamsState(s.TrialSeed, s.Version, s.Shop, s.Stats,
                value, s.Battle, s.Event, s.Roster, s.Economy);

        private static SimulationRandomStreamsState ReplaceEconomyRandom(
            SimulationRandomStreamsState s, Pcg32State value) =>
            new SimulationRandomStreamsState(s.TrialSeed, s.Version, s.Shop, s.Stats,
                s.Encounter, s.Battle, s.Event, s.Roster, value);

        private static SimulationRandomStreamsState ReplaceEventRandom(
            SimulationRandomStreamsState s, Pcg32State value) =>
            new SimulationRandomStreamsState(s.TrialSeed, s.Version, s.Shop, s.Stats,
                s.Encounter, s.Battle, value, s.Roster, s.Economy);

        /// <summary>Returns the immutable in-memory checkpoint; no file serialization is performed.</summary>
        public ActionExecutionState CaptureCheckpoint() => CurrentState;

        private static void ValidateDefinitions(GameSessionConfiguration configuration)
        {
            var definitions = UniqueById(configuration.CharacterDefinitions, x => x.DefinitionId, "Character");
            UniqueById(configuration.ItemDefinitions, x => x.DefinitionId, "Item");
            UniqueById(configuration.ObservationItemDefinitions, x => x.DefinitionId, "Observable Item");
            UniqueById(configuration.EnemyDefinitions, x => x.DefinitionId, "Enemy");
            UniqueById(configuration.EncounterPools, x => x.PoolId, "Encounter Pool");
            ValidateReferences(configuration.SelectedRoster, definitions, "Selected roster");
            ValidateReferences(configuration.FullCharacterRoster, definitions, "Full roster");

            int expectedRosterCount = 0;
            for (int cost = 1; cost <= 5; cost++)
                expectedRosterCount = checked(expectedRosterCount + configuration.CharacterShopConfig.GetCharactersPerCost(cost));
            if (configuration.SelectedRoster.Count != expectedRosterCount)
                throw new ArgumentException("Selected roster size must match the configured Cost distribution.", nameof(configuration));
            if (configuration.StartingEconomy.Gold != configuration.StartingEconomy.InitialGold ||
                configuration.StartingEconomy.XP != configuration.StartingEconomy.InitialXP ||
                configuration.StartingEconomy.Level != configuration.StartingEconomy.InitialLevel ||
                configuration.StartingEconomy.CurrentHP != configuration.StartingEconomy.InitialHP)
                throw new ArgumentException("Starting Economy must be at its configured initial values.", nameof(configuration));
        }

        private static Dictionary<string, T> UniqueById<T>(IReadOnlyList<T> items,
            Func<T, string> getId, string label) where T : class
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T item in items)
            {
                string id = getId(item);
                if (string.IsNullOrWhiteSpace(id) || result.ContainsKey(id))
                    throw new ArgumentException(label + " definitions must have unique non-empty IDs.");
                result.Add(id, item);
            }
            return result;
        }

        private static void ValidateReferences(IReadOnlyList<CharacterDefinition> roster,
            Dictionary<string, CharacterDefinition> definitions, string label)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterDefinition definition in roster)
            {
                if (!seen.Add(definition.DefinitionId)) throw new ArgumentException(label + " contains duplicate DefinitionIds.");
                if (!definitions.ContainsKey(definition.DefinitionId))
                    throw new ArgumentException(label + " references unknown DefinitionId '" + definition.DefinitionId + "'.");
            }
        }

        private static void ValidateState(GameSessionConfiguration configuration,
            ActionExecutionContext context, ActionExecutionState state)
        {
            ActionExecutionValidationResult validation = ActionExecutionStateValidator.Validate(state);
            if (!validation.IsValid) throw new ArgumentException("Checkpoint state is invalid: " + string.Join("; ", validation.Errors), nameof(state));
            if (state.Board.Columns != configuration.BoardColumns || state.Board.Rows != configuration.BoardRows ||
                state.Hand.SlotCount != configuration.HandSlotCount)
                throw new ArgumentException("Checkpoint Board/Hand dimensions do not match the session configuration.", nameof(state));

            ValidateCharacterPool(configuration, context, state);

            ValidateBattlePhaseAndResult(state);
            if (state.Stage.Phase == StageProgressPhase.Preparation)
            {
                if (state.Stage.CurrentSelection == null || state.Shop == null || state.EnemyBoard == null)
                    throw new ArgumentException("Preparation checkpoint must include its Stage selection, Shop, and Enemy board.", nameof(state));
                ShopKind expectedShop = state.Stage.CurrentSelection.ShopMode == StageShopMode.Item ? ShopKind.Item : ShopKind.Character;
                if (state.Shop.Kind != expectedShop)
                    throw new ArgumentException("Preparation Shop kind does not match the Stage selection.", nameof(state));
            }

            ObservationBuildResult observation = ObservationBuilder.Build(new ObservationSource(state.Stage,
                configuration.StageConfig, state.Economy, configuration.EconomyConfig,
                state.Board, state.Hand, state.Ownership, configuration.CharacterDefinitions,
                configuration.SelectedRoster, configuration.FullCharacterRoster,
                configuration.ObservationItemDefinitions, state.Shop, state.ItemEffects, state.EventItem,
                configuration.EnemyDefinitions, state.EnemyBoard, state.EnemyPreparation,
                state.Battle, GetObservableBattleResult(state), configuration.CharacterStatSettings,
                configuration.CharacterShopConfig, configuration.StageRewardConfig,
                configuration.ShopRerollGoldCost, configuration.SelectedRosterObservation)
            {
                CachedCharacterStats = configuration.PublicCharacterStats,
                CachedRosterRules = configuration.RosterRules
            });
            if (!observation.Succeeded)
                throw new ArgumentException("Checkpoint cannot produce a valid public observation: " + string.Join("; ", observation.Errors), nameof(state));
        }

        private static void ValidateCharacterPool(GameSessionConfiguration configuration,
            ActionExecutionContext context, ActionExecutionState state)
        {
            CharacterPool expected = CharacterShopRules.CreateInitialPool(configuration.SelectedRoster,
                context.AssignedCharacterCosts, configuration.CharacterShopConfig);
            if (state.CharacterPool.Entries.Count != expected.Entries.Count)
                throw new ArgumentException("Checkpoint Character Pool does not match the selected roster.", nameof(state));

            var expectedOwned = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CharacterInstanceView instance in state.Ownership.GetOwnedInstances())
            {
                int copies = instance.PoolCopies;
                if (copies < 0 || !expected.Entries.ContainsKey(instance.DefinitionId))
                    throw new ArgumentException("Checkpoint ownership contains invalid pool-copy provenance or a Definition outside the selected roster.", nameof(state));
                expectedOwned[instance.DefinitionId] = checked((expectedOwned.TryGetValue(instance.DefinitionId, out int previous) ? previous : 0) + copies);
            }

            var expectedReserved = new Dictionary<string, int>(StringComparer.Ordinal);
            if (state.Shop != null && state.Shop.Kind == ShopKind.Character)
            {
                foreach (ShopOffer offer in state.Shop.Offers)
                {
                    if (offer.Kind == ShopOfferKind.Character && offer.Status == ShopOfferStatus.Available)
                    {
                        if (!expected.Entries.ContainsKey(offer.DefinitionId))
                            throw new ArgumentException("Checkpoint Shop references a Definition outside the selected roster.", nameof(state));
                        expectedReserved[offer.DefinitionId] = checked((expectedReserved.TryGetValue(offer.DefinitionId, out int previous) ? previous : 0) + 1);
                    }
                }
            }

            foreach (KeyValuePair<string, CharacterPoolEntry> pair in expected.Entries)
            {
                if (!state.CharacterPool.Entries.TryGetValue(pair.Key, out CharacterPoolEntry actual) ||
                    actual.InitialStockLimit != pair.Value.InitialStockLimit)
                    throw new ArgumentException("Checkpoint Character Pool has an unknown Definition or mismatched stock limit.", nameof(state));
                int owned = expectedOwned.TryGetValue(pair.Key, out int ownedCount) ? ownedCount : 0;
                int reserved = expectedReserved.TryGetValue(pair.Key, out int reservedCount) ? reservedCount : 0;
                if (actual.OwnedCopies != owned || actual.ReservedCopies != reserved)
                    throw new ArgumentException("Checkpoint Character Pool owned/reserved counts do not match Ownership and Shop.", nameof(state));
            }
        }

        private static void ValidateBattlePhaseAndResult(ActionExecutionState state)
        {
            bool battlePhase = state.Stage.Phase == StageProgressPhase.Battle;
            if (battlePhase && state.Battle == null)
                throw new ArgumentException("Battle phase requires a BattleState.", nameof(state));

            if (state.Battle == null)
            {
                if (state.BattleResult != null && state.Stage.Phase != StageProgressPhase.RewardPending &&
                    state.Stage.Phase != StageProgressPhase.GameOver && state.Stage.Phase != StageProgressPhase.Completed)
                    throw new ArgumentException("A BattleResult without BattleState requires a terminal or reward-pending Stage phase.", nameof(state));
                return;
            }

            bool battleCompleted = state.Battle.Status == BattleExecutionStatus.Completed ||
                state.Battle.Phase == BattlePhase.Completed;
            if (!battleCompleted)
            {
                if (!battlePhase || state.BattleResult != null)
                    throw new ArgumentException("An active BattleState requires Battle phase and no BattleResult.", nameof(state));
                return;
            }

            if (state.BattleResult == null || state.BattleResult.Outcome != state.Battle.Outcome)
                throw new ArgumentException("A completed BattleState requires a matching BattleResult.", nameof(state));
            switch (state.BattleResult.Outcome)
            {
                case BattleOutcome.Victory:
                    if (state.Stage.Phase != StageProgressPhase.RewardPending &&
                        state.Stage.Phase != StageProgressPhase.Completed)
                        throw new ArgumentException("A Victory result requires RewardPending or Completed Stage phase.", nameof(state));
                    break;
                case BattleOutcome.Defeat:
                    if (state.Stage.Phase != StageProgressPhase.GameOver)
                        throw new ArgumentException("A Defeat result requires GameOver Stage phase.", nameof(state));
                    break;
                case BattleOutcome.Unresolved:
                    // StageProgressState has no Unresolved phase. Preserve the existing Battle phase
                    // and expose the distinct BattleResult until the later progression stage decides its handling.
                    if (!battlePhase)
                        throw new ArgumentException("Unresolved results remain in Battle phase because no dedicated Stage phase exists.", nameof(state));
                    break;
                default:
                    throw new ArgumentException("A completed Battle cannot have a pending outcome.", nameof(state));
            }
        }

        private static BattleResult GetObservableBattleResult(ActionExecutionState state)
        {
            // Unresolved is retained in Battle phase because StageProgress has no dedicated
            // terminal phase for it. Keep the result in the checkpoint/API, but do not expose
            // it through the current public Observation contract, which only allows terminal
            // results in RewardPending/Completed/GameOver.
            return state.Stage.Phase == StageProgressPhase.Battle && state.BattleResult != null &&
                state.BattleResult.Outcome == BattleOutcome.Unresolved ? null : state.BattleResult;
        }
    }
}
