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
        public CharacterStatDefinition CharacterStatSettings { get; }
        public CharacterShopConfiguration CharacterShopConfig { get; }
        public StageRewardConfiguration StageRewardConfig { get; }
        public int? ShopRerollGoldCost { get; }
        public IReadOnlyList<RosterDefinitionObservation> SelectedRosterObservation { get; }
        internal PublicCharacterStatsCatalog CachedCharacterStats { get; set; }
        internal SynergyDetector.RosterRules CachedRosterRules { get; set; }

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
            BattleState battle = null, BattleResult battleResult = null,
            CharacterStatDefinition characterStatSettings = null,
            CharacterShopConfiguration characterShopConfig = null,
            StageRewardConfiguration stageRewardConfig = null,
            int? shopRerollGoldCost = null,
            IReadOnlyList<RosterDefinitionObservation> selectedRosterObservation = null)
        {
            Stage=stage; StageConfig=stageConfig; Economy=economy; EconomyConfig=economyConfig;
            Board=board; Hand=hand; Ownership=ownership; CharacterDefinitions=characterDefinitions;
            SelectedRoster=selectedRoster; FullCharacterRoster=fullCharacterRoster;
            ItemDefinitions=itemDefinitions; Shop=shop; ItemEffects=itemEffects;
            EventItem=eventItem; EnemyDefinitions=enemyDefinitions; EnemyBoard=enemyBoard;
            EnemyPreparation=enemyPreparation; Battle=battle; BattleResult=battleResult;
            CharacterStatSettings=characterStatSettings; CharacterShopConfig=characterShopConfig;
            StageRewardConfig=stageRewardConfig; ShopRerollGoldCost=shopRerollGoldCost;
            SelectedRosterObservation=selectedRosterObservation;
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
        private static readonly Comparison<CharacterObservation> CharacterOrder = CompareCharacters;

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
                var boardSlots = new BoardSlotObservation[source.Board.SlotCount];
                for (int i = 0; i < source.Board.SlotCount; i++)
                {
                    source.Board.TryGetCharacterIdAt(i, out string id);
                    CharacterObservation dto = id == null ? null :
                        BuildCharacter(id, ObservationLocationKind.Board, i, owned, characters, characterDtos, errors);
                    boardSlots[i] = new BoardSlotObservation(i, dto);
                }

                var itemDtos = new Dictionary<string, ItemObservation>(StringComparer.Ordinal);
                var handSlots = new HandSlotObservation[source.Hand.SlotCount];
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
                        else item = GetItemObservation(itemDefinition, itemDtos);
                    }
                    handSlots[i] = new HandSlotObservation(i, content.Kind, character, item);
                }

                var allCharacters = new CharacterObservation[characterDtos.Count];
                int characterIndex = 0;
                foreach (CharacterObservation character in characterDtos.Values)
                    allCharacters[characterIndex++] = character;
                Array.Sort(allCharacters, CharacterOrder);

                PlayerObservation player = new PlayerObservation(source.Economy,
                    GetRequiredXP(source.Economy, source.EconomyConfig), source.ItemEffects);
                StageObservation stage = new StageObservation(source.Stage, source.StageConfig,
                    source.EconomyConfig, source.StageRewardConfig);
                ShopObservation shop = BuildShop(source.Stage, source.Shop, characters, items,
                    source.CharacterStatSettings, source.CharacterShopConfig, itemDtos, errors);
                StageSelectionOutcome selection = source.Stage.CurrentSelection;
                EconomyRulesObservation economyRules = new EconomyRulesObservation(source.Economy,
                    source.EconomyConfig, selection != null && selection.Choice == StageChoice.SelectB,
                    source.ShopRerollGoldCost, selection != null && selection.CanReroll && source.Shop != null);
                IReadOnlyList<RosterDefinitionObservation> roster = source.SelectedRosterObservation ??
                    BuildRosterSnapshot(source.SelectedRoster);
                SynergyDetector.RosterRules rosterRules = source.CachedRosterRules ??
                    SynergyDetector.BuildRosterRules(source.SelectedRoster, source.FullCharacterRoster);

                SynergyObservation synergy = null;
                if (source.Stage.Phase == StageProgressPhase.Preparation)
                {
                    SynergyDetectionResult detected = SynergyDetector.Detect(source.Board, source.Hand,
                        source.Ownership, source.SelectedRoster, source.FullCharacterRoster, rosterRules);
                    if (!detected.IsValid)
                        foreach (string error in detected.Errors) errors.Add("Synergy: " + error);
                    else synergy = new SynergyObservation(detected);
                }

                EnemyObservation[] enemyDtos = BuildEnemies(source, enemies, errors);
                BattleObservation battle = source.Battle == null ? null :
                    new BattleObservation(source.Battle, source.BattleResult);

                if (source.Stage.Phase == StageProgressPhase.EventPending && source.EventItem != null)
                {
                    if (!items.TryGetValue(source.EventItem.DefinitionId, out ItemDefinition eventDefinition))
                        errors.Add("Event references unknown Item DefinitionId '" + source.EventItem.DefinitionId + "'.");
                    else eventItemDto = new EventItemObservation(GetItemObservation(eventDefinition, itemDtos), source.EventItem);
                }

                if (errors.Count != 0) return Failure(errors);

                bool actionAllowed = source.Stage.Phase == StageProgressPhase.AwaitingStageChoice ||
                                     source.Stage.Phase == StageProgressPhase.Preparation ||
                                     source.Stage.Phase == StageProgressPhase.EventPending;
                return new ObservationBuildResult(true,
                    new GameObservation(player, stage, shop,
                        new BoardObservation(source.Board.Columns, source.Board.Rows, boardSlots),
                        new HandObservation(source.Hand.SlotCount, source.Hand.EmptySlotCount, handSlots),
                        allCharacters, eventItemDto, synergy, enemyDtos, battle, actionAllowed,
                        economyRules, roster,
                        source.CachedCharacterStats ?? new PublicCharacterStatsCatalog(source.CharacterStatSettings),
                        rosterRules.Requirements),
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

        private static ItemObservation GetItemObservation(ItemDefinition item,
            Dictionary<string, ItemObservation> itemDtos)
        {
            if (!itemDtos.TryGetValue(item.DefinitionId, out ItemObservation observation))
            { observation = new ItemObservation(item); itemDtos.Add(item.DefinitionId, observation); }
            return observation;
        }

        private static IReadOnlyList<RosterDefinitionObservation> BuildRosterSnapshot(
            IReadOnlyList<CharacterDefinition> selectedRoster)
        {
            var snapshot = new RosterDefinitionObservation[selectedRoster.Count];
            for (int i = 0; i < snapshot.Length; i++) snapshot[i] = new RosterDefinitionObservation(selectedRoster[i]);
            return Array.AsReadOnly(snapshot);
        }

        private static ShopObservation BuildShop(StageProgressState stage, ShopState shop,
            Dictionary<string, CharacterDefinition> characters, Dictionary<string, ItemDefinition> items,
            CharacterStatDefinition stats, CharacterShopConfiguration shopConfig,
            Dictionary<string, ItemObservation> itemDtos, List<string> errors)
        {
            if (stage.Phase != StageProgressPhase.Preparation || shop == null) return null;
            var offers = new ShopOfferObservation[shop.Offers.Count];
            for (int i = 0; i < shop.Offers.Count; i++)
            {
                ShopOffer offer = shop.Offers[i];
                CharacterDefinition character = null;
                ItemDefinition item = null;
                if (!offer.IsEmpty)
                {
                    if (offer.Kind == ShopOfferKind.Character)
                    {
                        if (!characters.TryGetValue(offer.DefinitionId, out character))
                            errors.Add("Shop references unknown Character DefinitionId '" + offer.DefinitionId + "'.");
                    }
                    else if (offer.Kind == ShopOfferKind.Item)
                    {
                        if (!items.TryGetValue(offer.DefinitionId, out item))
                            errors.Add("Shop references unknown Item DefinitionId '" + offer.DefinitionId + "'.");
                    }
                }
                offers[i] = new ShopOfferObservation(offer, character,
                    item == null ? null : GetItemObservation(item, itemDtos), stats, i);
            }
            ShopCostWeightObservation weights = shop.Kind == ShopKind.Character && shopConfig != null
                ? new ShopCostWeightObservation(shop.StageNumber, shopConfig) : null;
            return new ShopObservation(shop.Kind, shop.SlotCount, shop.StageNumber, shop.RoundNumber,
                shop.IsEventStage, offers, weights);
        }

        private static EnemyObservation[] BuildEnemies(ObservationSource source,
            Dictionary<string, EnemyDefinition> definitions, List<string> errors)
        {
            var result = new List<EnemyObservation>();
            if (source.Stage.Phase == StageProgressPhase.Battle ||
                source.Stage.Phase == StageProgressPhase.RewardPending ||
                source.Stage.Phase == StageProgressPhase.Completed ||
                source.Stage.Phase == StageProgressPhase.GameOver)
            {
                if (source.Battle == null) return result.ToArray();
                foreach (BattleUnitState unit in source.Battle.Enemies)
                {
                    if (!definitions.TryGetValue(unit.DefinitionId, out EnemyDefinition definition))
                    {
                        errors.Add("Battle enemy references unknown DefinitionId '" + unit.DefinitionId + "'.");
                        continue;
                    }
                    result.Add(new EnemyObservation(unit, definition));
                }
                return result.ToArray();
            }
            if (source.Stage.Phase != StageProgressPhase.Preparation ||
                source.Stage.CurrentSelection == null ||
                source.Stage.CurrentSelection.EnemyCategory == StageEnemyCategory.None ||
                source.EnemyBoard == null) return result.ToArray();

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
            return result.ToArray();
        }

        private static int CompareCharacters(CharacterObservation left, CharacterObservation right) =>
            string.CompareOrdinal(left.InstanceId, right.InstanceId);

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






