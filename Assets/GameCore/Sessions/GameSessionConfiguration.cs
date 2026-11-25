using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Sessions
{
    /// <summary>
    /// Immutable run setup supplied by the application. It contains definitions/configuration,
    /// not mutable gameplay progress. Starting economy and grid sizes are explicit because the
    /// existing GameCore has no separate initial-player or board/hand configuration object.
    /// </summary>
    public sealed class GameSessionConfiguration
    {
        public StageProgressConfig StageConfig { get; }
        public EconomyRulesConfiguration EconomyConfig { get; }
        public CharacterShopConfiguration CharacterShopConfig { get; }
        public CharacterStatDefinition CharacterStatSettings { get; }
        public StageRewardConfiguration StageRewardConfig { get; }
        public BattleConfig BattleConfig { get; }
        public EconomyState StartingEconomy { get; }
        public int BoardColumns { get; }
        public int BoardRows { get; }
        public int HandSlotCount { get; }
        public string InstanceIdPrefix { get; }
        public int ShopRerollGoldCost { get; }
        public IReadOnlyList<CharacterDefinition> CharacterDefinitions { get; }
        public IReadOnlyList<CharacterDefinition> SelectedRoster { get; }
        public IReadOnlyList<RosterDefinitionObservation> SelectedRosterObservation { get; }
        public IReadOnlyList<CharacterDefinition> FullCharacterRoster { get; }
        public IReadOnlyList<ItemDefinition> ItemDefinitions { get; }
        public ItemDefinition EventItemDefinition { get; }
        public IReadOnlyList<ItemDefinition> ObservationItemDefinitions { get; }
        public IReadOnlyList<EnemyDefinition> EnemyDefinitions { get; }
        public IReadOnlyList<EncounterPoolDefinition> EncounterPools { get; }
        public IReadOnlyList<EnemyFormationDefinition> EnemyFormations { get; }
        public IReadOnlyList<BattleTargetDefinition> BattleTargetDefinitions { get; }
        public IReadOnlyList<BattleEffectDefinition> BattleEffectDefinitions { get; }

        public GameSessionConfiguration(StageProgressConfig stageConfig,
            EconomyRulesConfiguration economyConfig, CharacterShopConfiguration characterShopConfig,
            CharacterStatDefinition characterStatSettings, StageRewardConfiguration stageRewardConfig,
            BattleConfig battleConfig, EconomyState startingEconomy,
            IEnumerable<CharacterDefinition> characterDefinitions,
            IEnumerable<CharacterDefinition> selectedRoster,
            IEnumerable<CharacterDefinition> fullCharacterRoster,
            IEnumerable<ItemDefinition> itemDefinitions,
            IEnumerable<EnemyDefinition> enemyDefinitions,
            IEnumerable<EncounterPoolDefinition> encounterPools,
            int shopRerollGoldCost, int boardColumns = BoardState.StandardColumns,
            int boardRows = BoardState.StandardRows,
            int handSlotCount = HandState.StandardSlotCount,
            string instanceIdPrefix = "C",
            IEnumerable<EnemyFormationDefinition> enemyFormations = null,
            IEnumerable<BattleTargetDefinition> battleTargetDefinitions = null,
            IEnumerable<BattleEffectDefinition> battleEffectDefinitions = null,
            ItemDefinition eventItemDefinition = null)
        {
            StageConfig = stageConfig ?? throw new ArgumentNullException(nameof(stageConfig));
            EconomyConfig = economyConfig ?? throw new ArgumentNullException(nameof(economyConfig));
            CharacterShopConfig = characterShopConfig ?? throw new ArgumentNullException(nameof(characterShopConfig));
            CharacterStatSettings = characterStatSettings ?? throw new ArgumentNullException(nameof(characterStatSettings));
            StageRewardConfig = stageRewardConfig ?? throw new ArgumentNullException(nameof(stageRewardConfig));
            BattleConfig = battleConfig ?? throw new ArgumentNullException(nameof(battleConfig));
            StartingEconomy = startingEconomy ?? throw new ArgumentNullException(nameof(startingEconomy));
            if (!startingEconomy.HasHealth) throw new ArgumentException("Starting Economy must include HP settings.", nameof(startingEconomy));
            int configuredMaximumLevel = checked(economyConfig.ExperienceToNextLevel.Count + 1);
            if (startingEconomy.MaximumLevel != configuredMaximumLevel)
                throw new ArgumentException("Starting Economy maximum Level must match the XP configuration.", nameof(startingEconomy));
            if (shopRerollGoldCost < 0) throw new ArgumentOutOfRangeException(nameof(shopRerollGoldCost));
            if (boardColumns < 1) throw new ArgumentOutOfRangeException(nameof(boardColumns));
            if (boardRows < 1) throw new ArgumentOutOfRangeException(nameof(boardRows));
            if (handSlotCount < 0) throw new ArgumentOutOfRangeException(nameof(handSlotCount));
            if (string.IsNullOrWhiteSpace(instanceIdPrefix)) throw new ArgumentException("Instance ID prefix is required.", nameof(instanceIdPrefix));

            ShopRerollGoldCost = shopRerollGoldCost;
            BoardColumns = boardColumns;
            BoardRows = boardRows;
            HandSlotCount = handSlotCount;
            InstanceIdPrefix = instanceIdPrefix;
            CharacterDefinitions = Copy(characterDefinitions, nameof(characterDefinitions));
            SelectedRoster = Copy(selectedRoster, nameof(selectedRoster));
            SelectedRosterObservation = BuildRosterObservation(SelectedRoster);
            FullCharacterRoster = Copy(fullCharacterRoster, nameof(fullCharacterRoster));
            ItemDefinitions = Copy(itemDefinitions, nameof(itemDefinitions));
            EventItemDefinition = eventItemDefinition;
            var observationItems = new List<ItemDefinition>(ItemDefinitions);
            if (eventItemDefinition != null && !observationItems.Any(x => x.DefinitionId == eventItemDefinition.DefinitionId))
                observationItems.Add(eventItemDefinition);
            else if (eventItemDefinition != null && !observationItems.First(x => x.DefinitionId == eventItemDefinition.DefinitionId)
                .HasSameDefinitionData(eventItemDefinition))
                throw new ArgumentException("Event Item conflicts with an Item Definition using the same ID.", nameof(eventItemDefinition));
            ObservationItemDefinitions = new ReadOnlyCollection<ItemDefinition>(observationItems.ToArray());
            EnemyDefinitions = Copy(enemyDefinitions, nameof(enemyDefinitions));
            EncounterPools = Copy(encounterPools, nameof(encounterPools));
            EnemyFormations = Copy(enemyFormations ?? Array.Empty<EnemyFormationDefinition>(), nameof(enemyFormations));
            BattleTargetDefinitions = Copy(battleTargetDefinitions ?? Array.Empty<BattleTargetDefinition>(), nameof(battleTargetDefinitions));
            BattleEffectDefinitions = Copy(battleEffectDefinitions ?? Array.Empty<BattleEffectDefinition>(), nameof(battleEffectDefinitions));
        }

        internal ActionExecutionContext CreateActionContext(AssignedCharacterCosts costs) =>
            new ActionExecutionContext(StageConfig, EconomyConfig, CharacterShopConfig, costs,
                CharacterStatSettings, StageRewardConfig, BattleConfig,
                CharacterDefinitions, SelectedRoster, FullCharacterRoster,
                ItemDefinitions, EnemyDefinitions, EncounterPools, EnemyFormations,
                BattleTargetDefinitions, BattleEffectDefinitions, ShopRerollGoldCost,
                EventItemDefinition);

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source, string name) where T : class
        {
            if (source == null) throw new ArgumentNullException(name);
            var copy = new List<T>(source);
            if (copy.Exists(item => item == null)) throw new ArgumentException("Configuration lists cannot contain null entries.", name);
            return new ReadOnlyCollection<T>(copy.ToArray());
        }

        private static IReadOnlyList<RosterDefinitionObservation> BuildRosterObservation(
            IReadOnlyList<CharacterDefinition> roster)
        {
            var copy = new RosterDefinitionObservation[roster.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = new RosterDefinitionObservation(roster[i]);
            return new ReadOnlyCollection<RosterDefinitionObservation>(copy);
        }
    }
}
