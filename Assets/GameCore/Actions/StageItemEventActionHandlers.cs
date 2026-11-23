using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Actions
{
    /// <summary>Stage 10-D4 registrations. StartBattle intentionally remains unsupported.</summary>
    public static class StageItemEventActionHandlers
    {
        public static IReadOnlyList<IActionHandler> Create() => Array.AsReadOnly<IActionHandler>(new IActionHandler[]
        {
            new SelectStageChoiceActionHandler(),
            new UseHandItemActionHandler(),
            new UseEventItemActionHandler(),
            new EndEventActionHandler()
        });
    }

    public sealed class SelectStageChoiceActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.SelectStageChoice;

        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is SelectStageChoiceAction select))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected SelectStageChoiceAction.");
            if (context.StageConfig == null)
                return ActionHandlerResult.ConfigurationError("StageProgressConfig is unavailable.");

            try
            {
                StageProgressResult selection = StageSelectionRules.Select(state.Stage, context.StageConfig, select.Choice);
                if (!selection.Succeeded)
                    return ActionHandlerResult.Reject(Map(selection.Failure), "StageSelectionRules rejected the choice: " + selection.Failure + ".");

                StageProgressState nextStage = selection.NextState;
                var streams = state.RandomState;
                var nextPool = state.CharacterPool;
                int stageNumber = context.StageConfig.GetStageNumber(nextStage.EncounterIndex);
                int roundNumber = context.StageConfig.GetRoundNumber(nextStage.EncounterIndex);
                ShopState nextShop;

                if (nextStage.CurrentSelection.ShopMode == StageShopMode.Character)
                {
                    if (context.SelectedRoster == null || context.AssignedCharacterCosts == null ||
                        context.CharacterShopConfig == null)
                        return ActionHandlerResult.ConfigurationError("Character shop roster, Cost assignment, or configuration is unavailable.");

                    CharacterPool available = state.Shop == null ? state.CharacterPool :
                        CharacterShopRules.ReturnUnpurchasedOffers(state.Shop, state.CharacterPool);
                    Pcg32RandomSource random = Pcg32RandomSource.FromState(streams.Shop);
                    CharacterShopGenerationResult generated = CharacterShopRules.Generate(context.SelectedRoster,
                        context.AssignedCharacterCosts, available, context.CharacterShopConfig,
                        stageNumber, roundNumber, 0, random);
                    nextShop = generated.Shop;
                    nextPool = generated.Pool;
                    streams = ReplaceShop(streams, random.CaptureState());
                }
                else if (nextStage.CurrentSelection.ShopMode == StageShopMode.Item)
                {
                    if (context.ItemDefinitions == null || context.CharacterShopConfig == null)
                        return ActionHandlerResult.ConfigurationError("Item definitions or shop slot configuration is unavailable.");
                    int slots = context.CharacterShopConfig.GetShopSlotCount(stageNumber);
                    ItemShopGenerationResult generated = ItemShopRules.Generate(state.Shop, state.CharacterPool,
                        context.ItemDefinitions, stageNumber, roundNumber, slots, 0,
                        Pcg32RandomSource.FromState(streams.Shop));
                    nextShop = generated.NextShop;
                    nextPool = generated.NextCharacterPool;
                    streams = ReplaceShop(streams, generated.NextShopRandomState);
                }
                else
                {
                    return ActionHandlerResult.ConfigurationError("The selected StageShopMode is not supported.");
                }

                if (nextStage.CurrentSelection.SelectionEffect != null &&
                    nextStage.CurrentSelection.SelectionEffect.Kind != StageSelectionEffectKind.None)
                {
                    StageRewardResult effect = StageRewardRules.ApplySelectionEffect(nextStage, state.Economy);
                    if (!effect.Succeeded)
                        return ActionHandlerResult.Reject(ActionErrorCode.InvalidState,
                            "Selection effect was rejected by StageRewardRules: " + effect.Failure + ".");
                    nextStage = effect.NextStageState;
                    var nextEconomy = effect.NextEconomyState;

                    EnemyEncounterSetupResult setup = GenerateEncounter(nextStage, context, state, streams);
                    if (!setup.Succeeded)
                        return MapEncounterFailure(setup);
                    streams = ReplaceEncounter(streams, setup.NextEncounterRandomState);
                    return ActionHandlerResult.Success(CreateNext(state, nextStage, nextEconomy,
                        nextPool, nextShop, setup.Board, new EnemyPreparationState(), streams), ChangedStreams(state.RandomState, streams));
                }

                EnemyEncounterSetupResult encounter = GenerateEncounter(nextStage, context, state, streams);
                if (!encounter.Succeeded)
                    return MapEncounterFailure(encounter);
                streams = ReplaceEncounter(streams, encounter.NextEncounterRandomState);
                return ActionHandlerResult.Success(CreateNext(state, nextStage, state.Economy,
                    nextPool, nextShop, encounter.Board, new EnemyPreparationState(), streams),
                    ChangedStreams(state.RandomState, streams));
            }
            catch (Exception ex) when (IsConfigurationException(ex))
            {
                return ActionHandlerResult.ConfigurationError("Stage selection setup failed before commit: " + ex.Message);
            }
        }

        private static EnemyEncounterSetupResult GenerateEncounter(StageProgressState stage,
            ActionExecutionContext context, ActionExecutionState state, SimulationRandomStreamsState streams)
        {
            if (stage.CurrentSelection.EnemyCategory != StageEnemyCategory.None &&
                (context.EncounterPools == null || context.EnemyDefinitions == null))
                throw new ActionExecutionConfigurationException("Encounter pools or EnemyDefinitions are unavailable.");
            return EnemyEncounterSetupRules.Generate(stage, context.EncounterPools,
                context.EnemyDefinitions, new EnemyPreparationState(), streams.Encounter);
        }

        private static ActionHandlerResult MapEncounterFailure(EnemyEncounterSetupResult setup)
        {
            if (setup.Failure == EnemyEncounterSetupFailure.EncounterGenerationFailed &&
                setup.EncounterResult != null && setup.EncounterResult.Failure == EncounterGenerationFailure.NoCandidates)
                return ActionHandlerResult.ConfigurationError("No encounter candidates exist for the selected round/category.");
            return ActionHandlerResult.ConfigurationError("Encounter setup failed before commit: " + setup.Failure + ".");
        }

        private static ActionExecutionState CreateNext(ActionExecutionState old, StageProgressState stage,
            ProjectNST.GameCore.Economy.EconomyState economy, CharacterPool pool, ShopState shop,
            EnemyBoardState enemyBoard, EnemyPreparationState enemyPreparation,
            SimulationRandomStreamsState random) => new ActionExecutionState(stage, economy, pool,
                old.InstanceFactory, old.Board, old.Hand, old.Ownership, old.ItemEffects, random,
                shop, null, enemyBoard, enemyPreparation, null, null, old.ExecutedActionIds);

        private static ActionErrorCode Map(StageProgressFailure failure) =>
            failure == StageProgressFailure.ChoiceNotAllowed ? ActionErrorCode.InvalidPhase :
            failure == StageProgressFailure.InvalidChoice ? ActionErrorCode.InvalidAction : ActionErrorCode.InvalidState;

        private static SimulationRandomStreamsState ReplaceShop(SimulationRandomStreamsState s, Pcg32State shop) =>
            new SimulationRandomStreamsState(s.TrialSeed, s.Version, shop, s.Stats, s.Encounter,
                s.Battle, s.Event, s.Roster, s.Economy);
        private static SimulationRandomStreamsState ReplaceEncounter(SimulationRandomStreamsState s, Pcg32State encounter) =>
            new SimulationRandomStreamsState(s.TrialSeed, s.Version, s.Shop, s.Stats, encounter,
                s.Battle, s.Event, s.Roster, s.Economy);
        private static RandomStreamMask ChangedStreams(SimulationRandomStreamsState a, SimulationRandomStreamsState b)
        {
            RandomStreamMask changed = RandomStreamMask.None;
            if (!a.Shop.Equals(b.Shop)) changed |= RandomStreamMask.Shop;
            if (!a.Encounter.Equals(b.Encounter)) changed |= RandomStreamMask.Encounter;
            return changed;
        }
        private static bool IsConfigurationException(Exception ex) => ex is ArgumentException ||
            ex is InvalidOperationException || ex is KeyNotFoundException || ex is OverflowException ||
            ex is ActionExecutionConfigurationException;
    }

    public sealed class UseHandItemActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.UseHandItem;

        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is UseHandItemAction use))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected UseHandItemAction.");
            if (context.ItemDefinitions == null)
                return ActionHandlerResult.ConfigurationError("ItemDefinitions are unavailable.");
            if (!state.Hand.TryGetSlot(use.HandSlot, out HandSlotContent content) ||
                content.Kind != HandSlotContentKind.Item)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidTarget, "The selected Hand slot does not contain an Item.");
            if (!TryFindItem(context.ItemDefinitions, content.ItemDefinitionId, out ItemDefinition item, out string error))
                return ActionHandlerResult.ConfigurationError(error);

            try
            {
                ItemUseTarget target = use.Target.ToItemUseTarget();
                EnemyPreparationState enemyPreparation = state.EnemyPreparation ?? new EnemyPreparationState();
                if (item.Target == ItemEffectTarget.AllEnemies)
                {
                    if (use.Target.Kind != ItemUseTargetKind.EnemyArea)
                        return ActionHandlerResult.Reject(ActionErrorCode.ItemTargetInvalid, "An enemy-wide Item requires an EnemyArea target payload.");
                    if (state.EnemyBoard == null || !TryCreateFormation(state.EnemyBoard, state.Stage.EncounterIndex,
                        out EnemyFormationDefinition formation, out int enemyCount) || enemyCount == 0)
                        return ActionHandlerResult.Reject(ActionErrorCode.ItemTargetInvalid, "The selected enemy-wide Item requires at least one generated enemy.");
                    EnemyItemUseResult enemyUse = EnemyItemEffectRules.TryUseHandItem(item, use.HandSlot,
                        formation, enemyPreparation, state.Board, state.Hand, state.Ownership,
                        state.Economy, state.Stage, state.ItemEffects, context.CharacterDefinitions,
                        context.CharacterStatSettings, state.RandomState.Stats);
                    if (!enemyUse.Succeeded)
                        return ActionHandlerResult.Reject(Map(enemyUse.ItemFailure), "EnemyItemEffectRules rejected Item use: " + enemyUse.Failure + ".");
                    var streams = ReplaceStats(state.RandomState, enemyUse.NextStatsRandomState);
                    ActionExecutionState next = state.With(board: enemyUse.NextBoard, hand: enemyUse.NextHand,
                        ownership: enemyUse.NextOwnership, economy: enemyUse.NextEconomy,
                        stage: enemyUse.NextStage, itemEffects: enemyUse.NextItemEffectState,
                        enemyPreparation: enemyUse.NextEnemyPreparationState, randomState: streams);
                    return ActionHandlerResult.Success(next, enemyUse.StatsRandomConsumed ? RandomStreamMask.Stats : RandomStreamMask.None);
                }

                if (item.Target == ItemEffectTarget.OneEnemy)
                {
                    if (use.Target.Kind != ItemUseTargetKind.EnemyCharacter)
                        return ActionHandlerResult.Reject(ActionErrorCode.ItemTargetInvalid, "A single-enemy Item requires an EnemyCharacter target payload.");
                    if (state.EnemyBoard == null || !state.EnemyBoard.TryGetAt(use.Target.SlotIndex, out EnemyInstance targetedEnemy) || targetedEnemy == null)
                        return ActionHandlerResult.Reject(ActionErrorCode.ItemTargetInvalid, "The selected enemy slot is empty.");
                    target = ItemUseTarget.EnemyCharacter(use.Target.SlotIndex, true);
                }
                ItemEffectResult result = ItemEffectRules.TryUseHandItem(item, use.HandSlot, target,
                    state.Board, state.Hand, state.Ownership, state.Economy, state.Stage,
                    state.ItemEffects, context.CharacterDefinitions, context.CharacterStatSettings,
                    state.RandomState.Stats);
                if (!result.Succeeded)
                    return ActionHandlerResult.Reject(Map(result.Failure), "ItemEffectRules rejected Item use: " + result.Failure + ".");
                var nextRandom = ReplaceStats(state.RandomState, result.NextStatsRandomState);
                return ActionHandlerResult.Success(state.With(board: result.NextBoard, hand: result.NextHand,
                    ownership: result.NextOwnership, economy: result.NextEconomy, stage: result.NextStage,
                    itemEffects: result.NextEffectState, randomState: nextRandom),
                    result.StatsRandomConsumed ? RandomStreamMask.Stats : RandomStreamMask.None);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException ||
                ex is KeyNotFoundException || ex is OverflowException)
            {
                return ActionHandlerResult.ConfigurationError("Item use failed before commit: " + ex.Message);
            }
        }

        internal static bool TryFindItem(IReadOnlyList<ItemDefinition> definitions, string id,
            out ItemDefinition item, out string error)
        {
            item = null; error = null;
            foreach (ItemDefinition candidate in definitions)
            {
                if (candidate == null || candidate.DefinitionId != id) continue;
                if (item != null && !item.HasSameDefinitionData(candidate))
                { error = "Conflicting ItemDefinitions share ID " + id + "."; return false; }
                item = candidate;
            }
            if (item == null) error = "ItemDefinition " + id + " is unavailable.";
            return item != null;
        }

        private static bool TryCreateFormation(EnemyBoardState board, int encounterIndex,
            out EnemyFormationDefinition formation, out int count)
        {
            formation = null; count = 0;
            if (board.SlotCount != EnemyInstanceFactory.SlotCount) return false;
            var slots = new List<EnemyFormationSlotDefinition>(board.SlotCount);
            for (int i = 0; i < board.SlotCount; i++)
            {
                if (!board.TryGetAt(i, out EnemyInstance enemy)) return false;
                slots.Add(enemy == null ? new EnemyFormationSlotDefinition(null, 0, 0) :
                    new EnemyFormationSlotDefinition(enemy.DefinitionId, enemy.Gap, enemy.Cooldown));
                if (enemy != null) count++;
            }
            formation = new EnemyFormationDefinition("action-state-" + encounterIndex, slots);
            return true;
        }

        internal static SimulationRandomStreamsState ReplaceStats(SimulationRandomStreamsState s, Pcg32State stats) =>
            new SimulationRandomStreamsState(s.TrialSeed, s.Version, s.Shop, stats, s.Encounter,
                s.Battle, s.Event, s.Roster, s.Economy);

        internal static ActionErrorCode Map(ItemEffectFailure failure) =>
            failure == ItemEffectFailure.InvalidPhase || failure == ItemEffectFailure.EventNotPending
                ? ActionErrorCode.InvalidPhase :
            failure == ItemEffectFailure.InvalidItem || failure == ItemEffectFailure.MissingCharacterDefinition ||
            failure == ItemEffectFailure.StatsUnavailable ? ActionErrorCode.InvalidDefinition :
            failure == ItemEffectFailure.InvalidTarget || failure == ItemEffectFailure.TargetMissing ||
            failure == ItemEffectFailure.CharacterNotEligible || failure == ItemEffectFailure.ItemNotInHand
                ? ActionErrorCode.ItemTargetInvalid : ActionErrorCode.InvalidState;
    }

    public sealed class UseEventItemActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.UseEventItem;
        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is UseEventItemAction use))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected UseEventItemAction.");
            if (state.EventItem == null)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidState, "No Event Item is active.");
            if (context.ItemDefinitions == null)
                return ActionHandlerResult.ConfigurationError("ItemDefinitions are unavailable.");
            if (!UseHandItemActionHandler.TryFindItem(context.ItemDefinitions, state.EventItem.DefinitionId,
                out ItemDefinition item, out string error))
                return ActionHandlerResult.ConfigurationError(error);
            if (item.Target != ItemEffectTarget.OneAlly)
                return ActionHandlerResult.Reject(ActionErrorCode.ItemTargetInvalid, "Event Item rules support a single ally target only.");

            ItemEffectResult result = ItemEffectRules.TryUseEventItem(item, state.EventItem,
                use.Target.ToItemUseTarget(), state.Board, state.Hand, state.Ownership,
                state.Economy, state.Stage, state.ItemEffects, context.CharacterDefinitions,
                context.CharacterStatSettings, state.RandomState.Stats);
            if (!result.Succeeded)
                return ActionHandlerResult.Reject(UseHandItemActionHandler.Map(result.Failure),
                    "ItemEffectRules rejected Event Item use: " + result.Failure + ".");
            var streams = UseHandItemActionHandler.ReplaceStats(state.RandomState, result.NextStatsRandomState);
            return ActionHandlerResult.Success(state.With(board: result.NextBoard, hand: result.NextHand,
                ownership: result.NextOwnership, economy: result.NextEconomy, stage: result.NextStage,
                itemEffects: result.NextEffectState, eventItem: result.NextEventItem, randomState: streams),
                result.StatsRandomConsumed ? RandomStreamMask.Stats : RandomStreamMask.None);
        }
    }

    public sealed class EndEventActionHandler : IActionHandler
    {
        public GameActionKind Kind => GameActionKind.EndEvent;
        public ActionHandlerResult Handle(ActionExecutionState state, GameAction action, ActionExecutionContext context)
        {
            if (!(action is EndEventAction))
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidAction, "Expected EndEventAction.");
            if (state.EventItem == null)
                return ActionHandlerResult.Reject(ActionErrorCode.InvalidState, "No Event Item is active.");

            if (state.EventItem.Status == EventItemStatus.Consumed)
            {
                StageProgressResult completed = StageProgressRules.CompleteEvent(state.Stage, false);
                if (!completed.Succeeded)
                    return ActionHandlerResult.Reject(ActionErrorCode.InvalidPhase, "Consumed Event Item could not complete this event: " + completed.Failure + ".");
                return ActionHandlerResult.Success(new ActionExecutionState(completed.NextState,
                    state.Economy, state.CharacterPool, state.InstanceFactory, state.Board, state.Hand,
                    state.Ownership, state.ItemEffects, state.RandomState, state.Shop,
                    state.EventItem, state.EnemyBoard, state.EnemyPreparation, state.Battle,
                    state.BattleResult, state.ExecutedActionIds));
            }

            if (context.ItemDefinitions == null)
                return ActionHandlerResult.ConfigurationError("ItemDefinitions are unavailable.");
            if (!UseHandItemActionHandler.TryFindItem(context.ItemDefinitions, state.EventItem.DefinitionId,
                out ItemDefinition item, out string error))
                return ActionHandlerResult.ConfigurationError(error);
            ItemEffectResult result = ItemEffectRules.EndEventWithoutEligibleTarget(item,
                state.EventItem, state.Board, state.Hand, state.Ownership, state.Economy,
                state.Stage, state.ItemEffects, state.RandomState.Stats);
            if (!result.Succeeded)
                return ActionHandlerResult.Reject(UseHandItemActionHandler.Map(result.Failure),
                    "ItemEffectRules rejected Event end: " + result.Failure + ".");
            return ActionHandlerResult.Success(state.With(stage: result.NextStage,
                eventItem: result.NextEventItem));
        }
    }
}
