using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Observation
{
    /// <summary>Explicit read sources for building an observation. This input is never retained.</summary>
    public sealed class ObservationSource
    {
        public StageProgressState Stage { get; }
        public StageProgressConfig StageConfig { get; }
        public EconomyState Economy { get; }
        public EconomyRulesConfiguration EconomyConfig { get; }
        public ShopState Shop { get; }
        public BoardState Board { get; }
        public HandState Hand { get; }
        public CharacterOwnershipState Ownership { get; }
        public IReadOnlyList<CharacterDefinition> CharacterDefinitions { get; }
        public IReadOnlyList<CharacterDefinition> SelectedRoster { get; }
        public IReadOnlyList<CharacterDefinition> FullCharacterRoster { get; }
        public IReadOnlyList<ItemDefinition> ItemDefinitions { get; }
        public IReadOnlyList<EnemyDefinition> EnemyDefinitions { get; }
        public ItemEffectState ItemEffects { get; }
        public EventItemState EventItem { get; }
        public EnemyBoardState EnemyBoard { get; }
        public EnemyPreparationState EnemyPreparation { get; }
        public BattleState Battle { get; }
        public BattleResult BattleResult { get; }

        public ObservationSource(StageProgressState stage, StageProgressConfig stageConfig,
            EconomyState economy, EconomyRulesConfiguration economyConfig, BoardState board,
            HandState hand, CharacterOwnershipState ownership,
            IReadOnlyList<CharacterDefinition> characterDefinitions,
            IReadOnlyList<CharacterDefinition> selectedRoster,
            IReadOnlyList<CharacterDefinition> fullCharacterRoster,
            IReadOnlyList<ItemDefinition> itemDefinitions,
            ShopState shop = null, ItemEffectState itemEffects = null,
            EventItemState eventItem = null, IReadOnlyList<EnemyDefinition> enemyDefinitions = null,
            EnemyBoardState enemyBoard = null, EnemyPreparationState enemyPreparation = null,
            BattleState battle = null, BattleResult battleResult = null)
        {
            Stage=stage; StageConfig=stageConfig; Economy=economy; EconomyConfig=economyConfig;
            Board=board; Hand=hand; Ownership=ownership; CharacterDefinitions=characterDefinitions;
            SelectedRoster=selectedRoster; FullCharacterRoster=fullCharacterRoster;
            ItemDefinitions=itemDefinitions; Shop=shop; ItemEffects=itemEffects;
            EventItem=eventItem; EnemyDefinitions=enemyDefinitions; EnemyBoard=enemyBoard;
            EnemyPreparation=enemyPreparation; Battle=battle; BattleResult=battleResult;
        }
    }

    public sealed class ObservationBuildResult
    {
        private readonly string[] _errors;
        public bool Succeeded { get; }
        public GameObservation Observation { get; }
        public IReadOnlyList<string> Errors { get; }
        internal ObservationBuildResult(bool succeeded, GameObservation observation, IList<string> errors)
        {
            Succeeded=succeeded; Observation=observation;
            _errors=new string[errors.Count]; errors.CopyTo(_errors,0); Errors=Array.AsReadOnly(_errors);
        }
    }

    /// <summary>
    /// Creates a detached, public-only immutable view. It does not retain source state objects,
    /// invoke rules that consume randomness, or mutate any input.
    /// </summary>
    public static class ObservationBuilder
    {
        public static ObservationBuildResult Build(ObservationSource source)
        {
            var errors = new List<string>();
            if (source == null) return Failure("ObservationSource is missing.");
            if (source.Stage == null) errors.Add("StageProgressState is missing.");
            if (source.StageConfig == null) errors.Add("StageProgressConfig is missing.");
            if (source.Economy == null) errors.Add("EconomyState is missing.");
            if (source.EconomyConfig == null) errors.Add("EconomyRulesConfiguration is missing.");
            if (source.Board == null) errors.Add("BoardState is missing.");
            if (source.Hand == null) errors.Add("HandState is missing.");
            if (source.Ownership == null) errors.Add("CharacterOwnershipState is missing.");
            if (source.CharacterDefinitions == null) errors.Add("Character definitions are missing.");
            if (source.SelectedRoster == null) errors.Add("Selected roster is missing.");
            if (source.FullCharacterRoster == null) errors.Add("Full character roster is missing.");
            if (source.ItemDefinitions == null) errors.Add("Item definitions are missing.");
            if (errors.Count != 0) return Failure(errors);

            if (source.Board.Columns != BoardState.StandardColumns ||
                source.Board.Rows != BoardState.StandardRows ||
                source.Hand.SlotCount != HandState.StandardSlotCount)
                errors.Add("Observation requires the standard 3x3 battle board and 5-slot hand.");

            BoardHandStateValidationResult validation =
                BoardHandStateValidator.Validate(source.Board, source.Hand, source.Ownership);
            if (!validation.IsValid)
                foreach (string error in validation.Errors) errors.Add(error);

            if (source.Stage.Phase == StageProgressPhase.Battle && source.Battle == null)
                errors.Add("BattleState is required during the Battle phase.");
            if (source.Stage.Phase == StageProgressPhase.EventPending && source.EventItem == null)
                errors.Add("EventItemState is required during the EventPending phase.");
            if (source.Battle != null && source.Battle.Enemies.Count > 0 && source.EnemyDefinitions == null)
                errors.Add("Enemy definitions are required to describe battle enemies.");
            if (source.Battle != null && source.Stage.Phase != StageProgressPhase.Battle &&
                source.Stage.Phase != StageProgressPhase.RewardPending &&
                source.Stage.Phase != StageProgressPhase.Completed &&
                source.Stage.Phase != StageProgressPhase.GameOver)
                errors.Add("BattleState was supplied outside a phase that can publicly expose battle data.");
            if (source.BattleResult != null &&
                source.Stage.Phase != StageProgressPhase.RewardPending &&
                source.Stage.Phase != StageProgressPhase.Completed &&
                source.Stage.Phase != StageProgressPhase.GameOver)
                errors.Add("BattleResult is not publicly available in the current phase.");

            if (source.Stage.Phase == StageProgressPhase.Preparation)
            {
                if (source.Stage.CurrentSelection == null)
                    errors.Add("Preparation requires a selected stage outcome.");
                else if (source.Shop != null &&
                    (source.Shop.StageNumber != source.StageConfig.GetStageNumber(source.Stage.EncounterIndex) ||
                     source.Shop.RoundNumber != source.StageConfig.GetRoundNumber(source.Stage.EncounterIndex)))
                    errors.Add("Shop stage/round does not match the current StageProgressState.");
                else if (source.Stage.CurrentSelection.ShopMode == StageShopMode.Character &&
                         (source.Shop == null || source.Shop.Kind != ShopKind.Character))
                    errors.Add("The current Character ShopState is required during character preparation.");
                else if (source.Stage.CurrentSelection.ShopMode == StageShopMode.Item &&
                         (source.Shop == null || source.Shop.Kind != ShopKind.Item))
                    errors.Add("The current Item ShopState is required during item preparation.");

                if (source.Stage.CurrentSelection != null &&
                    source.Stage.CurrentSelection.EnemyCategory != StageEnemyCategory.None &&
                    (source.EnemyBoard == null || source.EnemyDefinitions == null))
                    errors.Add("Generated enemy board and definitions are required for an enemy preparation.");
            }

            if (source.Stage.Phase == StageProgressPhase.Preparation)
            {
                if (source.SelectedRoster == null || source.FullCharacterRoster == null)
                    errors.Add("Selected and full rosters are required to detect preparation synergies.");
            }

            if (errors.Count != 0) return Failure(errors);

            try
            {
                Dictionary<string, CharacterDefinition> characters =
                    BuildUniqueMap(source.CharacterDefinitions, d => d.DefinitionId, "character", errors);
                Dictionary<string, ItemDefinition> items =
                    BuildUniqueMap(source.ItemDefinitions, d => d.DefinitionId, "item", errors);
                Dictionary<string, EnemyDefinition> enemies =
                    BuildUniqueMap(source.EnemyDefinitions, d => d.DefinitionId, "enemy", errors);
                if (errors.Count != 0) return Failure(errors);

                Dictionary<string, CharacterInstanceView> owned = new Dictionary<string, CharacterInstanceView>(StringComparer.Ordinal);
                foreach (CharacterInstanceView instance in source.Ownership.GetOwnedInstances())
                {
                    if (!characters.ContainsKey(instance.DefinitionId))
                        errors.Add("Owned character references unknown DefinitionId '" + instance.DefinitionId + "'.");
                    if (owned.ContainsKey(instance.InstanceId))
                        errors.Add("Duplicate owned InstanceId '" + instance.InstanceId + "'.");
                    else
                        owned.Add(instance.InstanceId, instance);
                }
                if (errors.Count != 0) return Failure(errors);

                var characterDtos = new Dictionary<string, CharacterObservation>(StringComparer.Ordinal);
                var boardSlots = new List<BoardSlotObservation>(source.Board.SlotCount);
                for (int i = 0; i < source.Board.SlotCount; i++)
                {
                    source.Board.TryGetCharacterIdAt(i, out string id);
                    CharacterObservation dto = id == null ? null :
                        BuildCharacter(id, ObservationLocationKind.Board, i, owned, characters, characterDtos, errors);
                    boardSlots.Add(new BoardSlotObservation(i, dto));
                }

                var handSlots = new List<HandSlotObservation>(source.Hand.SlotCount);
                EventItemObservation eventItemDto = null;
                for (int i = 0; i < source.Hand.SlotCount; i++)
                {
                    source.Hand.TryGetSlot(i, out HandSlotContent content);
                    CharacterObservation character = null;
                    ItemObservation item = null;
                    if (content.Kind == HandSlotContentKind.Character)
                        character = BuildCharacter(content.CharacterInstanceId, ObservationLocationKind.Hand,
                            i, owned, characters, characterDtos, errors);
                    else if (content.Kind == HandSlotContentKind.Item)
                    {
                        if (!items.TryGetValue(content.ItemDefinitionId, out ItemDefinition itemDefinition))
                            errors.Add("Hand references unknown Item DefinitionId '" + content.ItemDefinitionId + "'.");
                        else item = new ItemObservation(itemDefinition);
                    }
                    handSlots.Add(new HandSlotObservation(i, content.Kind, character, item));
                }

                var allCharacters = new List<CharacterObservation>(characterDtos.Values);
                allCharacters.Sort((left, right) => string.CompareOrdinal(left.InstanceId, right.InstanceId));

                PlayerObservation player = new PlayerObservation(source.Economy,
                    GetRequiredXP(source.Economy, source.EconomyConfig), source.ItemEffects);
                StageObservation stage = new StageObservation(source.Stage, source.StageConfig);
                ShopObservation shop = BuildShop(source.Stage, source.Shop, characters, items, errors);

                SynergyObservation synergy = null;
                if (source.Stage.Phase == StageProgressPhase.Preparation)
                {
                    SynergyDetectionResult detected = SynergyDetector.Detect(source.Board, source.Hand,
                        source.Ownership, source.SelectedRoster, source.FullCharacterRoster);
                    if (!detected.IsValid)
                        foreach (string error in detected.Errors) errors.Add("Synergy: " + error);
                    else synergy = new SynergyObservation(detected);
                }

                List<EnemyObservation> enemyDtos = BuildEnemies(source, enemies, errors);
                BattleObservation battle = source.Battle == null ? null :
                    new BattleObservation(source.Battle, source.BattleResult);

                if (source.Stage.Phase == StageProgressPhase.EventPending && source.EventItem != null)
                {
                    if (!items.TryGetValue(source.EventItem.DefinitionId, out ItemDefinition eventDefinition))
                        errors.Add("Event references unknown Item DefinitionId '" + source.EventItem.DefinitionId + "'.");
                    else eventItemDto = new EventItemObservation(eventDefinition, source.EventItem);
                }

                if (errors.Count != 0) return Failure(errors);

                bool actionAllowed = source.Stage.Phase == StageProgressPhase.AwaitingStageChoice ||
                                     source.Stage.Phase == StageProgressPhase.Preparation ||
                                     source.Stage.Phase == StageProgressPhase.EventPending;
                return new ObservationBuildResult(true,
                    new GameObservation(player, stage, shop,
                        new BoardObservation(source.Board.Columns, source.Board.Rows, boardSlots),
                        new HandObservation(source.Hand.SlotCount, source.Hand.EmptySlotCount, handSlots),
                        allCharacters, eventItemDto, synergy, enemyDtos, battle, actionAllowed),
                    Array.Empty<string>());
            }
            catch (ArgumentException exception)
            {
                errors.Add("Observation source contains invalid data: " + exception.Message);
                return Failure(errors);
            }
            catch (OverflowException)
            {
                errors.Add("Observation data exceeds supported numeric limits.");
                return Failure(errors);
            }
        }

        private static int GetRequiredXP(EconomyState economy, EconomyRulesConfiguration config)
        {
            int index = economy.Level - 1;
            return index >= 0 && index < config.ExperienceToNextLevel.Count
                ? config.ExperienceToNextLevel[index] : 0;
        }

        private static CharacterObservation BuildCharacter(string id, ObservationLocationKind location,
            int slot, Dictionary<string, CharacterInstanceView> owned,
            Dictionary<string, CharacterDefinition> definitions,
            Dictionary<string, CharacterObservation> built, List<string> errors)
        {
            if (built.TryGetValue(id, out CharacterObservation existing))
            {
                errors.Add("Character InstanceId '" + id + "' appears in more than one location.");
                return existing;
            }
            if (!owned.TryGetValue(id, out CharacterInstanceView instance))
            {
                errors.Add("Slot references unowned Character InstanceId '" + id + "'.");
                return null;
            }
            if (!definitions.TryGetValue(instance.DefinitionId, out CharacterDefinition definition))
            {
                errors.Add("Character InstanceId '" + id + "' has no definition.");
                return null;
            }
            var result = new CharacterObservation(instance, definition, location, slot);
            built.Add(id, result);
            return result;
        }

        private static ShopObservation BuildShop(StageProgressState stage, ShopState shop,
            Dictionary<string, CharacterDefinition> characters, Dictionary<string, ItemDefinition> items,
            List<string> errors)
        {
            if (stage.Phase != StageProgressPhase.Preparation || shop == null) return null;
            var offers = new List<ShopOfferObservation>(shop.Offers.Count);
            for (int i = 0; i < shop.Offers.Count; i++)
            {
                ShopOffer offer = shop.Offers[i];
                string name = string.Empty;
                if (!offer.IsEmpty)
                {
                    if (offer.Kind == ShopOfferKind.Character)
                    {
                        if (!characters.TryGetValue(offer.DefinitionId, out CharacterDefinition definition))
                            errors.Add("Shop references unknown Character DefinitionId '" + offer.DefinitionId + "'.");
                        else name = definition.CharacterName;
                    }
                    else if (offer.Kind == ShopOfferKind.Item)
                    {
                        if (!items.TryGetValue(offer.DefinitionId, out ItemDefinition definition))
                            errors.Add("Shop references unknown Item DefinitionId '" + offer.DefinitionId + "'.");
                        else name = definition.DisplayName;
                    }
                }
                offers.Add(new ShopOfferObservation(offer, name, i));
            }
            return new ShopObservation(shop.Kind, shop.SlotCount, shop.StageNumber, shop.RoundNumber, shop.IsEventStage, offers);
        }

        private static List<EnemyObservation> BuildEnemies(ObservationSource source,
            Dictionary<string, EnemyDefinition> definitions, List<string> errors)
        {
            var result = new List<EnemyObservation>();
            if (source.Stage.Phase == StageProgressPhase.Battle ||
                source.Stage.Phase == StageProgressPhase.RewardPending ||
                source.Stage.Phase == StageProgressPhase.Completed ||
                source.Stage.Phase == StageProgressPhase.GameOver)
            {
                if (source.Battle == null) return result;
                foreach (BattleUnitState unit in source.Battle.Enemies)
                {
                    if (!definitions.TryGetValue(unit.DefinitionId, out EnemyDefinition definition))
                    {
                        errors.Add("Battle enemy references unknown DefinitionId '" + unit.DefinitionId + "'.");
                        continue;
                    }
                    result.Add(new EnemyObservation(unit, definition));
                }
                return result;
            }
            if (source.Stage.Phase != StageProgressPhase.Preparation ||
                source.Stage.CurrentSelection == null ||
                source.Stage.CurrentSelection.EnemyCategory == StageEnemyCategory.None ||
                source.EnemyBoard == null) return result;

            for (int i = 0; i < source.EnemyBoard.SlotCount; i++)
            {
                source.EnemyBoard.TryGetAt(i, out EnemyInstance instance);
                if (instance == null) continue;
                if (!definitions.TryGetValue(instance.DefinitionId, out EnemyDefinition definition))
                {
                    errors.Add("Enemy board references unknown DefinitionId '" + instance.DefinitionId + "'.");
                    continue;
                }
                result.Add(new EnemyObservation(instance, definition, source.EnemyPreparation));
            }
            return result;
        }

        private static Dictionary<string, T> BuildUniqueMap<T>(IReadOnlyList<T> values,
            Func<T, string> getId, string kind, List<string> errors) where T : class
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            if (values == null) return result;
            for (int i = 0; i < values.Count; i++)
            {
                T value = values[i];
                if (value == null) { errors.Add("Null " + kind + " definition at index " + i + "."); continue; }
                string id = getId(value);
                if (string.IsNullOrWhiteSpace(id)) { errors.Add(kind + " definition has an empty ID."); continue; }
                if (result.ContainsKey(id)) errors.Add("Duplicate " + kind + " DefinitionId '" + id + "'.");
                else result.Add(id, value);
            }
            return result;
        }

        private static ObservationBuildResult Failure(string error) =>
            Failure(new List<string> { error });

        private static ObservationBuildResult Failure(IList<string> errors) =>
            new ObservationBuildResult(false, null, new List<string>(errors));
    }
}






