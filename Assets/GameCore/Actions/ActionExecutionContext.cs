using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Actions
{
    /// <summary>Immutable definitions/configuration supplied to handlers; contains no run state.</summary>
    public sealed class ActionExecutionContext
    {
        public StageProgressConfig StageConfig { get; }
        public EconomyRulesConfiguration EconomyConfig { get; }
        public CharacterShopConfiguration CharacterShopConfig { get; }
        public AssignedCharacterCosts AssignedCharacterCosts { get; }
        public CharacterStatDefinition CharacterStatSettings { get; }
        public StageRewardConfiguration StageRewardConfig { get; }
        public BattleConfig BattleConfig { get; }
        /// <summary>Serialized ShopManager.rerollCost copied from the active Scene configuration.</summary>
        public int? ShopRerollGoldCost { get; }
        public IReadOnlyList<CharacterDefinition> CharacterDefinitions { get; }
        public IReadOnlyList<CharacterDefinition> SelectedRoster { get; }
        public IReadOnlyList<CharacterDefinition> FullCharacterRoster { get; }
        public IReadOnlyList<ItemDefinition> ItemDefinitions { get; }
        public ItemDefinition EventItemDefinition { get; }
        public IReadOnlyList<EnemyDefinition> EnemyDefinitions { get; }
        public IReadOnlyList<EncounterPoolDefinition> EncounterPools { get; }
        public IReadOnlyList<EnemyFormationDefinition> EnemyFormations { get; }
        public IReadOnlyList<BattleTargetDefinition> BattleTargetDefinitions { get; }
        public IReadOnlyList<BattleEffectDefinition> BattleEffectDefinitions { get; }

        public ActionExecutionContext(StageProgressConfig stageConfig = null,
            EconomyRulesConfiguration economyConfig = null,
            CharacterShopConfiguration characterShopConfig = null,
            AssignedCharacterCosts assignedCharacterCosts = null,
            CharacterStatDefinition characterStatSettings = null,
            StageRewardConfiguration stageRewardConfig = null,
            BattleConfig battleConfig = null,
            IEnumerable<CharacterDefinition> characterDefinitions = null,
            IEnumerable<CharacterDefinition> selectedRoster = null,
            IEnumerable<CharacterDefinition> fullCharacterRoster = null,
            IEnumerable<ItemDefinition> itemDefinitions = null,
            IEnumerable<EnemyDefinition> enemyDefinitions = null,
            IEnumerable<EncounterPoolDefinition> encounterPools = null,
            IEnumerable<EnemyFormationDefinition> enemyFormations = null,
            IEnumerable<BattleTargetDefinition> battleTargetDefinitions = null,
            IEnumerable<BattleEffectDefinition> battleEffectDefinitions = null,
            int? shopRerollGoldCost = null,
            ItemDefinition eventItemDefinition = null)
        {
            if (shopRerollGoldCost < 0) throw new ArgumentOutOfRangeException(nameof(shopRerollGoldCost));
            StageConfig = stageConfig; EconomyConfig = economyConfig;
            CharacterShopConfig = characterShopConfig; AssignedCharacterCosts = assignedCharacterCosts;
            CharacterStatSettings = characterStatSettings; StageRewardConfig = stageRewardConfig;
            BattleConfig = battleConfig;
            ShopRerollGoldCost = shopRerollGoldCost;
            CharacterDefinitions = Copy(characterDefinitions, nameof(characterDefinitions));
            SelectedRoster = Copy(selectedRoster, nameof(selectedRoster));
            FullCharacterRoster = Copy(fullCharacterRoster, nameof(fullCharacterRoster));
            ItemDefinitions = Copy(itemDefinitions, nameof(itemDefinitions));
            EventItemDefinition = eventItemDefinition;
            EnemyDefinitions = Copy(enemyDefinitions, nameof(enemyDefinitions));
            EncounterPools = Copy(encounterPools, nameof(encounterPools));
            EnemyFormations = Copy(enemyFormations, nameof(enemyFormations));
            BattleTargetDefinitions = Copy(battleTargetDefinitions, nameof(battleTargetDefinitions));
            BattleEffectDefinitions = Copy(battleEffectDefinitions, nameof(battleEffectDefinitions));
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source, string name) where T : class
        {
            if (source == null) return null; // Null means unavailable; an empty collection means known empty.
            var copied = new List<T>(source);
            if (copied.Exists(value => value == null))
                throw new ArgumentException("Context definition lists cannot contain null entries.", name);
            return new ReadOnlyCollection<T>(copied.ToArray());
        }
    }
}
