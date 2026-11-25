using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameUnityAdapter;
using ProjectNST.SimulationExport;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>Editor-only entry point for comparing the built-in strategies against saved game data.</summary>
public static class NSTSimulationBatchMenu
{
    private const string ShopScenePath = "Assets/Scenes/shop.unity";
    private const int StandardTrialCount = 100;
    private const int LargeTrialCount = 1000;
    private const ulong BaseSeed = 12345UL;
    private static bool _isRunning;
    private static bool _fullDiagnosticsRequested;
    private static bool _largeBatchRequested;

    [MenuItem("Tools/NST/Run 100 Trials with Full Diagnostics")]
    private static void Run100TrialsWithFullDiagnostics()
    {
        _fullDiagnosticsRequested = true;
        try { RunTrials(StandardTrialCount); }
        finally { _fullDiagnosticsRequested = false; }
    }

    [MenuItem("Tools/NST/Run 1000 Trials")]
    private static void Run1000Trials()
    {
        _largeBatchRequested = true;
        try { RunTrials(LargeTrialCount); }
        finally { _largeBatchRequested = false; }
    }

    [MenuItem("Tools/NST/Run 1000 Trials with Full Diagnostics")]
    private static void Run1000TrialsWithFullDiagnostics()
    {
        _largeBatchRequested = true;
        _fullDiagnosticsRequested = true;
        try { RunTrials(LargeTrialCount); }
        finally { _fullDiagnosticsRequested = false; _largeBatchRequested = false; }
    }

    [MenuItem("Tools/NST/Run 100 Trials")]
    private static void Run100Trials()
    {
        RunTrials(StandardTrialCount);
    }

    private static void RunTrials(int trialCount)
    {
        if (_isRunning)
        {
            UnityEngine.Debug.LogWarning("NST 100 Trial simulation is already running.");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("NST Simulation", "Play Modeを終了してから実行してください。", "OK");
            return;
        }

        _isRunning = true;
        Scene loadedScene = default(Scene);
        Scene previouslyActiveScene = SceneManager.GetActiveScene();
        bool openedScene = false;
        var timer = Stopwatch.StartNew();
        long memoryStart = GC.GetTotalMemory(false);
        long memoryObservedMax = memoryStart;
        long workingSetStart = TryGetWorkingSet();
        long workingSetObservedMax = workingSetStart;
        int gc0Start = GC.CollectionCount(0), gc1Start = GC.CollectionCount(1), gc2Start = GC.CollectionCount(2);
        try
        {
            var configurationTimer = Stopwatch.StartNew();
            loadedScene = SceneManager.GetSceneByPath(ShopScenePath);
            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ShopScenePath) == null)
                    throw new InvalidOperationException("Saved shop scene was not found at " + ShopScenePath + ".");
                loadedScene = EditorSceneManager.OpenScene(ShopScenePath, OpenSceneMode.Additive);
                openedScene = true;
                if (previouslyActiveScene.IsValid() && previouslyActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previouslyActiveScene);
            }

            ResolvedCompositionRoster roster = LoadCompositionRoster();
            GameSessionConfiguration sessionConfiguration = BuildSessionConfiguration(loadedScene, roster.Characters);
            ShopManager sceneShop = FindInScene<ShopManager>(loadedScene);
            string eventItemDefinitionId = ItemDataMapper.ToDefinition(ResolveEventItemData(sceneShop)).DefinitionId;
            string checkpointConfigurationId = CompositionRosterDataReader.CreateCheckpointConfigurationId(roster.Fingerprint);
            LogFixedRoster(roster, sessionConfiguration);
            SimulationRunnerConfiguration runnerConfiguration = new SimulationRunnerConfiguration(
                sessionConfiguration, maxDecisionsPerPhase: 100, maxConsecutiveRejections: 10,
                maxTrialSteps: 100000, recordExecutionHistory: _fullDiagnosticsRequested,
                checkpointConfigurationId: checkpointConfigurationId,
                eventItemDefinitionId: eventItemDefinitionId);
            var batchConfiguration = new SimulationBatchConfiguration(BaseSeed, trialCount,
                new[]
                {
                    new SimulationStrategyFactory(RandomStrategy.StableId, RandomStrategy.StableVersion,
                        () => CreateRandomStrategy(sessionConfiguration)),
                    new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.StableVersion,
                        () => CreateHeuristicStrategy(sessionConfiguration))
                }, runnerConfiguration, SimulationBatchResultRetention.FullTrialResults,
                CompositionRosterDataReader.CreateBatchConfigurationId(roster.Fingerprint, eventItemDefinitionId));
            configurationTimer.Stop();

            var trialDurations = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            var strategyExecutionSeconds = new Dictionary<string, double>(StringComparer.Ordinal);
            int progressInterval = Math.Max(1, trialCount / 100);
            SimulationBatchResult result = new SimulationBatchRunner().RunBatch(batchConfiguration, progress =>
            {
                if (!trialDurations.TryGetValue(progress.CurrentStrategyId, out List<double> durations))
                    trialDurations.Add(progress.CurrentStrategyId, durations = new List<double>(trialCount));
                durations.Add(progress.LastTrialElapsedSeconds);
                strategyExecutionSeconds[progress.CurrentStrategyId] =
                    strategyExecutionSeconds.TryGetValue(progress.CurrentStrategyId, out double previous)
                        ? previous + progress.LastTrialElapsedSeconds : progress.LastTrialElapsedSeconds;
                if (progress.CompletedTrialCount % progressInterval != 0 &&
                    progress.CompletedTrialCount != progress.TotalTrialCount) return;
                long managed = GC.GetTotalMemory(false);
                if (managed > memoryObservedMax) memoryObservedMax = managed;
                long workingSet = TryGetWorkingSet();
                if (workingSet > workingSetObservedMax) workingSetObservedMax = workingSet;
                double fraction = (double)progress.CompletedTrialCount / progress.TotalTrialCount;
                double elapsedSeconds = timer.Elapsed.TotalSeconds;
                double etaSeconds = fraction <= 0 ? 0 : elapsedSeconds * (1.0 - fraction) / fraction;
                EditorUtility.DisplayProgressBar("NST Simulation",
                    progress.CurrentStrategyId + " / Original Trial " + progress.CompletedTrialCount + "/" +
                    progress.TotalTrialCount + " (" + (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%)\n" +
                    "Elapsed " + TimeSpan.FromSeconds(elapsedSeconds) + " / ETA " + TimeSpan.FromSeconds(etaSeconds), (float)fraction);
            });
            VerifySharedSeedsAndCounts(result, trialCount);
            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SimulationResults"));
            if (_fullDiagnosticsRequested || _largeBatchRequested)
                outputDirectory = Path.Combine(outputDirectory,
                    _fullDiagnosticsRequested ? "LargeScaleFullDiagnostics" : "LargeScale",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(outputDirectory);
            BackupExistingCsvFiles(outputDirectory);
            TimeSpan fullDiagnosticElapsed = TimeSpan.Zero;
            var diagnosticTimings = new DiagnosticTimingTotals();
            if (_fullDiagnosticsRequested)
            {
                var diagnosticTimer = Stopwatch.StartNew();
                WriteFullDiagnostics(result, runnerConfiguration, roster.Fingerprint,
                    eventItemDefinitionId, outputDirectory, (done, total, strategy, phase) =>
                    {
                        long managed = GC.GetTotalMemory(false);
                        if (managed > memoryObservedMax) memoryObservedMax = managed;
                        long workingSet = TryGetWorkingSet();
                        if (workingSet > workingSetObservedMax) workingSetObservedMax = workingSet;
                        double fraction = (double)done / Math.Max(1, total);
                        double elapsed = timer.Elapsed.TotalSeconds;
                        double eta = fraction <= 0 ? 0 : diagnosticTimer.Elapsed.TotalSeconds * (1.0 - fraction) / fraction;
                        EditorUtility.DisplayProgressBar("NST Full Diagnostics", strategy + " / " + phase + " " +
                            done + "/" + total + " (" + (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) +
                            "%)\nElapsed " + TimeSpan.FromSeconds(elapsed) + " / ETA " + TimeSpan.FromSeconds(eta), (float)fraction);
                    }, diagnosticTimings);
                diagnosticTimer.Stop();
                fullDiagnosticElapsed = diagnosticTimer.Elapsed;
            }
            else
                WriteStoppedTrialDiagnostics(result, sessionConfiguration, eventItemDefinitionId,
                    checkpointConfigurationId, roster.Fingerprint, outputDirectory);
            EditorUtility.DisplayProgressBar("NST Simulation", "CSV Export\nWriting batch reports", 0.99f);
            var csvTimer = Stopwatch.StartNew();
            WriteCsv(Path.Combine(outputDirectory, "trials.csv"), SimulationCsvExporter.ExportTrialsCsv(result));
            WriteCsv(Path.Combine(outputDirectory, "strategy_summary.csv"), SimulationCsvExporter.ExportStrategySummaryCsv(result));
            WriteCsv(Path.Combine(outputDirectory, "strategy_comparison.csv"), SimulationCsvExporter.ExportStrategyComparisonCsv(result));
            WriteCsv(Path.Combine(outputDirectory, "stop_reasons.csv"), SimulationCsvExporter.ExportStopReasonsCsv(result));
            csvTimer.Stop();
            timer.Stop();

            string performance = BuildPerformanceSummary(result, configurationTimer.Elapsed,
                strategyExecutionSeconds, trialDurations, diagnosticTimings, timer.Elapsed, fullDiagnosticElapsed, csvTimer.Elapsed,
                memoryStart, memoryObservedMax, GC.GetTotalMemory(false),
                workingSetStart, workingSetObservedMax, TryGetWorkingSet(), gc0Start, gc1Start, gc2Start, outputDirectory);
            WriteCsvText(Path.Combine(outputDirectory, "performance_summary.csv"), performance);
            File.WriteAllText(Path.Combine(outputDirectory, "batch_execution_summary.txt"),
                BuildExecutionSummary(result, trialCount, _fullDiagnosticsRequested, timer.Elapsed,
                    configurationTimer.Elapsed, fullDiagnosticElapsed, csvTimer.Elapsed, memoryStart,
                    memoryObservedMax, GC.GetTotalMemory(false), workingSetStart, workingSetObservedMax, TryGetWorkingSet(),
                    gc0Start, gc1Start, gc2Start, roster.Fingerprint, eventItemDefinitionId, outputDirectory),
                new UTF8Encoding(false));

            LogBatch(result, timer.Elapsed, outputDirectory);
            if (_fullDiagnosticsRequested)
                UnityEngine.Debug.Log("NST Full Diagnostics additional verification time: " +
                    fullDiagnosticElapsed.ToString() + " (" + (result.TrialResults.Count * 2) +
                    " action-history replays plus " + (result.TrialResults.Count * 2) + " Strategy reruns).");
            EditorUtility.DisplayDialog("NST Simulation", trialCount + " Trial × " + batchConfiguration.Strategies.Count +
                " Strategy の実行とCSV保存が完了しました。\n" + outputDirectory, "OK");
        }
        catch (Exception exception)
        {
            timer.Stop();
            UnityEngine.Debug.LogError("NST simulation failed. No dummy configuration was substituted.\n" + exception);
            EditorUtility.DisplayDialog("NST Simulation", "実行できませんでした。Consoleのエラーを確認してください。\n" + exception.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (openedScene && loadedScene.IsValid() && loadedScene.isLoaded)
                EditorSceneManager.CloseScene(loadedScene, true);
            if (previouslyActiveScene.IsValid() && previouslyActiveScene.isLoaded)
                SceneManager.SetActiveScene(previouslyActiveScene);
            _isRunning = false;
        }
    }

    internal static GameSessionConfiguration BuildSessionConfiguration(Scene scene)
    {
        ResolvedCompositionRoster roster = LoadCompositionRoster();
        return BuildSessionConfiguration(scene, roster.Characters);
    }

    internal static GameSessionConfiguration BuildSessionConfiguration(Scene scene,
        IReadOnlyList<CharacterData> selectedAssets)
    {
        ShopManager shop = FindInScene<ShopManager>(scene);
        PlayerData player = FindInScene<PlayerData>(scene);
        BoardManager board = FindInScene<BoardManager>(scene);
        HandManager hand = FindInScene<HandManager>(scene);
        EnemyManager enemies = FindInScene<EnemyManager>(scene);
        BattleManager battle = FindInScene<BattleManager>(scene);
        if (shop == null || player == null || board == null || hand == null || enemies == null || battle == null)
            throw new InvalidOperationException("shop.unity must contain ShopManager, PlayerData, BoardManager, HandManager, EnemyManager, and BattleManager.");
        if (selectedAssets == null || selectedAssets.Count != CompositionRosterDataReader.ExpectedSlotCount ||
            selectedAssets.Any(x => x == null) || selectedAssets.Distinct().Count() != CompositionRosterDataReader.ExpectedSlotCount)
            throw new InvalidOperationException("CompositionScene 編成1から一意なCharacterDataを30人取得できません。");
        if (shop.fullCharacterRoster == null || shop.fullCharacterRoster.Length == 0 ||
            shop.fullCharacterRoster.Any(x => x == null))
            throw new InvalidOperationException("ShopManager.fullCharacterRoster is empty or contains missing assets.");
        if (selectedAssets.Any(x => !shop.fullCharacterRoster.Contains(x)))
            throw new InvalidOperationException("CompositionScene 編成1にShopManager.fullCharacterRoster外のCharacterDataがあります。");
        if (shop.stageCostAppearanceRates == null || shop.stageCostAppearanceRates.Length != 5 ||
            shop.shopSlotsByStage == null || shop.shopSlotsByStage.Length != 5 ||
            shop.charactersPerCost == null || shop.initialPoolCountsByCost == null)
            throw new InvalidOperationException("Saved ShopManager cost, pool, and slot settings must cover all five stages/cost tiers.");

        var characterMap = new Dictionary<CharacterData, CharacterDefinition>();
        foreach (CharacterData asset in shop.fullCharacterRoster.Concat(selectedAssets).Distinct())
            characterMap.Add(asset, CharacterDataAssetIdMapper.ToDefinition(asset));
        CharacterDefinition[] definitions = characterMap.Values.ToArray();
        CharacterDefinition[] selectedRoster = selectedAssets.Select(x => characterMap[x]).ToArray();
        CharacterDefinition[] fullRoster = shop.fullCharacterRoster.Select(x => characterMap[x]).ToArray();

        if (battle.characterStatSettings == null)
            throw new InvalidOperationException("BattleManager.characterStatSettings is not assigned in the saved shop scene.");
        CharacterStatDefinition statDefinition = CharacterStatSettingsMapper.ToDefinition(battle.characterStatSettings);

        var enemyAssets = AssetDatabase.FindAssets("t:EnemyData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<EnemyData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(asset => asset != null).ToArray();
        if (enemyAssets.Length == 0) throw new InvalidOperationException("No EnemyData assets were found.");
        EnemyDefinition[] enemyDefinitions = enemyAssets.Select(EnemyDataMapper.ToDefinition).ToArray();
        var enemyMap = enemyDefinitions.ToDictionary(x => x.DefinitionId, StringComparer.Ordinal);

        EncounterPoolData[] poolAssets = enemies.encounterPoolData;
        if (poolAssets == null || poolAssets.Length == 0)
            throw new InvalidOperationException("EnemyManager.encounterPoolData is not assigned in the saved shop scene.");
        EnemyFormationData[] formationAssets = poolAssets.Where(x => x != null)
            .SelectMany(x => x.formations ?? Array.Empty<EnemyFormationData>())
            .Where(x => x != null).GroupBy(x => x.formationId, StringComparer.Ordinal)
            .Select(group =>
            {
                if (group.Select(asset => AssetDatabase.GetAssetPath(asset)).Distinct(StringComparer.Ordinal).Count() > 1)
                    throw new InvalidOperationException("Different Formation assets share ID " + group.Key + ".");
                return group.First();
            }).ToArray();
        var formationMap = formationAssets.ToDictionary(x => x.formationId,
            x => EnemyEncounterDataMapper.ToFormationDefinition(x, enemyMap), StringComparer.Ordinal);
        EncounterPoolDefinition[] poolDefinitions = poolAssets.Select(x =>
            EnemyEncounterDataMapper.ToEncounterPoolDefinition(x, formationMap)).ToArray();

        ItemDefinition[] items = (shop.itemDataPool ?? Array.Empty<ItemData>())
            .Where(x => x != null).Select(ItemDataMapper.ToDefinition).ToArray();
        ItemDefinition eventItem = ItemDataMapper.ToDefinition(ResolveEventItemData(shop));
        BattleTargetDefinition[] targets = Resources.LoadAll<TargetData>("TargetData")
            .Select(TargetDataMapper.ToDefinition).ToArray();
        BattleEffectDefinition[] effects = Resources.LoadAll<EffectData>("EffectData")
            .Select(BattleEffectDataMapper.ToDefinition).ToArray();
        if (targets.Length == 0 || effects.Length == 0)
            throw new InvalidOperationException("TargetData or EffectData Resources are missing; the saved EnemyData references cannot be mapped.");

        int[][] stageWeights = shop.stageCostAppearanceRates.Select(rate => rate == null
            ? throw new InvalidOperationException("ShopManager has a missing stage cost-rate row.")
            : new[] { rate.cost1, rate.cost2, rate.cost3, rate.cost4, rate.cost5 }).ToArray();
        int[] levelThresholds = shop.experienceToNextLevel;
        if (levelThresholds == null || levelThresholds.Length == 0)
            throw new InvalidOperationException("ShopManager.experienceToNextLevel is empty.");

        // These progression values are fixed by the existing Unity implementation; the mutable
        // event chance starts at 10 and rises by 10 to 100 when an event does not occur.
        var stageConfig = new StageProgressConfig(
            totalEncounterCount: 25, roundsPerStage: 5, choiceRoundNumber: 5, bossRoundNumber: 4,
            initialEventChancePercent: 10, eventChanceIncreasePercent: 10, maximumEventChancePercent: 100,
            selectAExperienceReward: shop.selectAExperienceReward,
            selectBExperienceReward: shop.selectBExperienceReward, selectBGoldReward: shop.selectBGoldReward,
            selectCExperienceReward: shop.selectCExperienceReward, selectCGoldReward: shop.selectCGoldReward,
            selectDExperienceReward: shop.selectDExperienceReward, selectDGoldReward: shop.selectDGoldReward,
            selectDHealAmount: shop.selectDHealAmount,
            normalExperiencePurchaseGoldCost: shop.xpPurchaseGoldCost,
            selectBExperiencePurchaseGoldCost: shop.xpSalePurchaseGoldCost, selectBCanReroll: true);
        var economyConfig = new EconomyRulesConfiguration(
            shop.xpPurchaseGoldCost, shop.xpSalePurchaseGoldCost, shop.xpPerPurchase, levelThresholds,
            shop.goldPerInterest, shop.interestGoldPerThreshold, shop.maxInterestGold, shop.stageGoldReward,
            shop.emptyHandSlotsPerBonusGold, shop.bonusGoldPerEmptyHandSlots);
        var shopConfig = new CharacterShopConfiguration(shop.charactersPerCost, shop.initialPoolCountsByCost,
            stageWeights, shop.shopSlotsByStage);
        var startingEconomy = new EconomyState(player.gold, 0, shop.CurrentLevel, levelThresholds.Length + 1,
            player.gold, 0, shop.CurrentLevel, player.hp, Math.Max(player.hp, player.InitialHp));
        if (startingEconomy.MaximumHP < 1)
            throw new InvalidOperationException("PlayerData.hp must be positive in the saved shop scene.");

        // Unity's BattleManager has no round cap. GameCore requires a positive cap, so use the
        // largest representable limit; SimulationRunner's step limit remains the practical guard.
        var battleConfig = new BattleConfig(int.MaxValue);
        return new GameSessionConfiguration(stageConfig, economyConfig, shopConfig, statDefinition,
            new StageRewardConfiguration(shop.bossGoldReward, shop.bossExperienceReward), battleConfig,
            startingEconomy, definitions, selectedRoster, fullRoster, items, enemyDefinitions,
            poolDefinitions, shop.rerollCost, board.columns, board.rows, hand.slotCount, "C",
            formationAssets.Select(x => formationMap[x.formationId]), targets, effects,
            eventItemDefinition: eventItem);
    }

    internal static ItemData ResolveEventItemData(ShopManager shop)
    {
        if (shop == null) throw new ArgumentNullException(nameof(shop));
        if (shop.eventItemData != null) return shop.eventItemData;
        foreach (ItemData item in shop.itemDataPool ?? Array.Empty<ItemData>())
            if (item != null && string.Equals(item.itemId, shop.eventItemId, StringComparison.Ordinal))
                return item;
        throw new InvalidOperationException("ShopManager event Item could not be resolved from eventItemData or eventItemId/itemDataPool.");
    }

    private sealed class ResolvedCompositionRoster
    {
        public string FormationName;
        public string SourceKey;
        public string Fingerprint;
        public CharacterData[] Characters;
        public string[] AssetNames;
        public string[] DefinitionIds;
    }

    private static ResolvedCompositionRoster LoadCompositionRoster()
    {
        CompositionRosterReadResult read = CompositionRosterDataReader.ReadFromPlayerPrefs();
        if (!read.Succeeded || read.Document == null)
            throw new InvalidOperationException("編成1を読み込めません: " + read.Error);

        string[] names = read.Document.CharacterAssetNames.ToArray();
        var selectedNames = new HashSet<string>(names, StringComparer.Ordinal);
        CharacterData[] assets = AssetDatabase.FindAssets("t:CharacterData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<CharacterData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(asset => asset != null).ToArray();

        var idByAsset = new Dictionary<CharacterData, string>();
        var references = new List<CompositionRosterAssetReference>();
        foreach (CharacterData asset in assets)
        {
            string id = string.Empty;
            if (selectedNames.Contains(asset.name))
            {
                try
                {
                    id = CharacterDataAssetIdMapper.ToDefinition(asset).DefinitionId;
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("CharacterData AssetのGUID/Definition IDを取得できません: " +
                        asset.name + " (" + AssetDatabase.GetAssetPath(asset) + ")", exception);
                }
                idByAsset[asset] = id;
            }
            references.Add(new CompositionRosterAssetReference(asset.name, id));
        }

        CompositionRosterResolution resolution = CompositionRosterDataReader.ResolveDefinitionIds(
            read.Document, references);
        if (!resolution.Succeeded)
            throw new InvalidOperationException("編成1のCharacterData参照が無効です: " + resolution.Error);

        var assetById = idByAsset.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        string[] ids = resolution.DefinitionIds.ToArray();
        CharacterData[] orderedAssets = ids.Select(id =>
        {
            if (!assetById.TryGetValue(id, out CharacterData asset))
                throw new InvalidOperationException("Definition IDからCharacterData Assetを再取得できません: " + id);
            return asset;
        }).ToArray();

        return new ResolvedCompositionRoster
        {
            FormationName = read.Document.FormationName,
            SourceKey = read.SourceKey,
            Fingerprint = CompositionRosterDataReader.ComputeFingerprint(ids),
            Characters = orderedAssets,
            AssetNames = names,
            DefinitionIds = ids
        };
    }

    private static void LogFixedRoster(ResolvedCompositionRoster roster,
        GameSessionConfiguration configuration)
    {
        var log = new StringBuilder(4096);
        log.AppendLine("NST fixed simulation roster")
            .AppendLine("Formation: 1")
            .AppendLine("Formation name: " + (string.IsNullOrWhiteSpace(roster.FormationName) ? "(not saved)" : roster.FormationName))
            .AppendLine("Source: " + roster.SourceKey)
            .AppendLine("Roster fingerprint: " + roster.Fingerprint)
            .AppendLine("Fixed members (slot order; run Cost is assigned separately per Trial):");
        for (int i = 0; i < roster.Characters.Length; i++)
            log.Append(i.ToString("D2", CultureInfo.InvariantCulture)).Append("  ")
                .Append(roster.Characters[i].characterName).Append("  asset=").Append(roster.AssetNames[i])
                .Append("  definition=").AppendLine(roster.DefinitionIds[i]);

        ulong trialZeroSeed = TrialSeedGenerator.Create(BaseSeed, 0UL).TrialSeed;
        var streams = new SimulationRandomStreams(trialZeroSeed);
        AssignedCharacterCosts trialZeroCosts = CharacterCostAssigner.Assign(configuration.SelectedRoster,
            configuration.CharacterShopConfig, streams.Roster);
        log.AppendLine("Trial 0 run Cost preview (not fixed roster data): seed=" +
            trialZeroSeed.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < roster.DefinitionIds.Length; i++)
            log.Append(roster.AssetNames[i]).Append(" -> Cost ")
                .AppendLine(trialZeroCosts.GetCost(roster.DefinitionIds[i]).ToString(CultureInfo.InvariantCulture));
        UnityEngine.Debug.Log(log.ToString());
    }

    private static void BackupExistingCsvFiles(string outputDirectory)
    {
        string[] names = { "trials.csv", "strategy_summary.csv", "strategy_comparison.csv", "stop_reasons.csv",
            "performance_summary.csv", "batch_execution_summary.txt" };
        string[] existing = names.Select(name => Path.Combine(outputDirectory, name)).Where(File.Exists).ToArray();
        if (existing.Length == 0) return;
        string backup = Path.Combine(outputDirectory, "Backups", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backup);
        foreach (string file in existing) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true);
        UnityEngine.Debug.Log("Previous NST Trial CSV files backed up to: " + backup);
    }

    private static void WriteStoppedTrialDiagnostics(SimulationBatchResult batch,
        GameSessionConfiguration sessionConfiguration, string eventItemDefinitionId,
        string checkpointConfigurationId, string rosterFingerprint, string outputDirectory)
    {
        SimulationBatchTrialResult[] stopped = batch.TrialResults.Where(x => x.HasSimulationResult &&
            x.FullResult != null && (x.StopReason == SimulationStopReason.NoActionNotAllowed ||
            x.StopReason == SimulationStopReason.DecisionLimitExceeded ||
            x.StopReason == SimulationStopReason.EventItemSelectionRequired ||
            x.StopReason == SimulationStopReason.ConsecutiveRejectionLimitExceeded)).ToArray();
        string directory = Path.GetFullPath(Path.Combine(outputDirectory, "Diagnostics"));
        Directory.CreateDirectory(directory);
        var runner = new SimulationRunner(new SimulationRunnerConfiguration(sessionConfiguration,
            maxDecisionsPerPhase: 100, maxConsecutiveRejections: 10, maxTrialSteps: 100000,
            recordExecutionHistory: true, checkpointConfigurationId: checkpointConfigurationId,
            eventItemDefinitionId: eventItemDefinitionId));
        var diagnosticResults = new List<SimulationTrialResult>();
        int replayMismatchCount = 0, replayUnavailableCount = 0, replayFailureCount = 0;

        foreach (SimulationBatchTrialResult row in stopped)
        {
            SimulationTrialResult original = row.FullResult;
            IPlayerStrategy strategy = original.StrategyId == RandomStrategy.StableId
                ? (IPlayerStrategy)CreateRandomStrategy(sessionConfiguration)
                : original.StrategyId == HeuristicStrategy.StableId
                    ? CreateHeuristicStrategy(sessionConfiguration)
                    : null;
            if (strategy == null) continue;
            string path = Path.Combine(directory, SafeFilePart(original.StrategyId) + "_v" + SafeFilePart(original.StrategyVersion) + "_trial_" +
                original.TrialIndex.ToString(CultureInfo.InvariantCulture) + "_" + original.StopReasonId + ".txt");
            try
            {
            SimulationTrialResult diagnostic = runner.RunTrial(original.TrialIndex, original.TrialSeed, strategy);
            AssignedCharacterCosts diagnosticCosts = CharacterCostAssigner.Assign(
                sessionConfiguration.SelectedRoster, sessionConfiguration.CharacterShopConfig,
                new SimulationRandomStreams(original.TrialSeed).Roster);
            var text = new StringBuilder(8192);
            text.AppendLine("NST stopped Trial diagnostic (deterministic replay)");
            text.AppendLine("TrialIndex=" + original.TrialIndex.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("TrialSeed=" + original.TrialSeed.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("Strategy=" + original.StrategyId + " v" + original.StrategyVersion);
            text.AppendLine("SelectedRosterFingerprint=" + rosterFingerprint);
            text.AppendLine("BatchConfigurationId=" + batch.BatchConfigurationId);
            text.AppendLine("DeterministicCostAssignment (recomputed from same TrialSeed + Roster RNG):");
            foreach (CharacterDefinition definition in sessionConfiguration.SelectedRoster)
                text.Append("  ").Append(definition.DefinitionId).Append(" cost=")
                    .AppendLine(diagnosticCosts.GetCost(definition.DefinitionId).ToString(CultureInfo.InvariantCulture));
            text.AppendLine("StopReason=" + original.StopReasonId + " / " + original.StopReason);
            text.AppendLine("Stage=" + original.StageNumber + " Round=" + original.RoundNumber +
                " Phase=" + original.FinalPhase + " Decisions=" + original.DecisionCount +
                " PhaseDecisions=" + original.PhaseDecisionCount);
            text.AppendLine("Detail=" + original.Detail);
            bool comparisonMatched = AppendDiagnosticComparisons(text, batch, original, diagnostic,
                rosterFingerprint, sessionConfiguration.SelectedRoster, eventItemDefinitionId,
                out bool actionHistoryUnavailable);
            string replayStatus = !comparisonMatched ? "false" :
                actionHistoryUnavailable ? "Unavailable" : "true";
            // Keep successful diagnostic reruns in the grouped rejection report even when a
            // replay comparison differs; the mismatch is evidence, not a reason to drop it.
            diagnosticResults.Add(diagnostic);
            if (!comparisonMatched) replayMismatchCount++;
            else if (actionHistoryUnavailable) replayUnavailableCount++;
            text.AppendLine("Replay identical=" + replayStatus);
            text.AppendLine("Action totals: successful=" + original.SuccessfulActionCount +
                " rejected=" + original.RejectedActionCount + " actionSequence=" + original.ActionSequence);
            text.AppendLine("PreparationDecisionCount=" + original.PhaseDecisionCount +
                " ConsecutiveRejectionCount=" + original.ConsecutiveRejectionCount);
            string recomputedRosterFingerprint = CompositionRosterDataReader.ComputeFingerprint(
                sessionConfiguration.SelectedRoster.Select(x => x.DefinitionId));
            text.AppendLine("Roster fingerprint matches batch input=" +
                string.Equals(rosterFingerprint, recomputedRosterFingerprint, StringComparison.Ordinal));
            AppendStateSummary(text, original.FinalCheckpoint, sessionConfiguration.StageConfig);
            text.AppendLine("Last 20 Action records (consecutive rejection stop includes its full final rejection streak):");
            SimulationExecutionRecord[] entries = diagnostic.ExecutionHistory
                .Where(x => x.Kind == SimulationExecutionEventKind.Action).ToArray();
            foreach (SimulationExecutionRecord entry in entries.Skip(Math.Max(0, entries.Length - 20)))
            {
                text.Append("#").Append(entry.Sequence).Append(" step=").Append(entry.DecisionStep)
                    .Append(" phase=").Append(entry.PhaseBefore).Append("->").Append(entry.PhaseAfter)
                    .Append(" location=").Append(DescribeStageRound(entry.BeforeStateSummary, sessionConfiguration.StageConfig))
                    .Append(" ActionKind=").Append(entry.Action.Kind)
                    .Append(" ActionId=").Append(entry.Action.ActionId.Value)
                    .Append(" parameters=").Append(DescribeAction(entry.Action))
                    .Append(" stageRound=").Append(DescribeStageRound(entry.BeforeStateSummary, sessionConfiguration.StageConfig))
                    .Append(" status=").Append(entry.ActionStatus?.ToString() ?? entry.OperationStatus);
                if (entry.ActionError.HasValue) text.Append(" error=").Append(entry.ActionError.Value.Code).Append(": ").Append(entry.ActionError.Value.Detail);
                text.Append(" beforeFingerprint=").Append(entry.BeforeFingerprint)
                    .Append(" afterFingerprint=").Append(entry.AfterFingerprint);
                text.Append(" strategyState=").Append(DescribeStrategyState(entry.StrategyStateAfterDecision))
                    .Append(" strategyRng=").Append(DescribeStrategyRng(entry.StrategyRandomStateAfterDecision));
                if (!string.IsNullOrEmpty(entry.StrategyDiagnostic)) text.Append(" strategy=").Append(entry.StrategyDiagnostic);
                text.AppendLine().AppendLine("  before: ").AppendLine(entry.BeforeStateSummary)
                    .AppendLine("  after: ").AppendLine(entry.AfterStateSummary);
            }
            if (original.StopReason == SimulationStopReason.ConsecutiveRejectionLimitExceeded)
            {
                text.AppendLine("Last 10 rejected Actions (or all available in the trailing rejection streak):");
                IReadOnlyList<SimulationExecutionRecord> lastRejected =
                    NSTSimulationRejectionDiagnostics.GetTrailingRejectionRecords(
                        diagnostic.ExecutionHistory, original.ConsecutiveRejectionCount);
                foreach (SimulationExecutionRecord entry in lastRejected)
                    text.Append("#").Append(entry.Sequence).Append(" step=").Append(entry.DecisionStep)
                        .Append(" ").Append(entry.Action.Kind).Append(" ").Append(DescribeAction(entry.Action))
                        .Append(" ").Append(entry.ActionError.HasValue ? entry.ActionError.Value.Code.ToString() : "<no-code>")
                        .Append(": ").AppendLine(entry.ActionError.HasValue ? entry.ActionError.Value.Detail : "<no-message>");
                text.AppendLine("Trailing rejected Action records captured=" + lastRejected.Count);
            }
            foreach (SimulationExecutionRecord entry in diagnostic.ExecutionHistory
                .Where(x => x.Kind == SimulationExecutionEventKind.StrategyNoAction).TakeLast(5))
                text.AppendLine("StrategyNoAction step=" + entry.DecisionStep + " phase=" + entry.PhaseBefore +
                    " reason=" + entry.StrategyDiagnostic);
            text.AppendLine("Per-decision candidate count/rank: unavailable; ExecutionHistory stores selected Action and StrategyDiagnostic, not the full candidate list. Final-state candidate inspection below is read-only and uses no Strategy RNG.");
            AppendCandidateDiagnostics(text, sessionConfiguration, diagnostic, strategy);
            File.WriteAllText(path, text.ToString());
            UnityEngine.Debug.Log("NST stopped Trial diagnostic: " + path + " (replay identical=" + replayStatus + ")");
            if (!comparisonMatched) UnityEngine.Debug.LogError("Diagnostic rerun differed from the retained Trial result: " + path);
            }
            catch (Exception exception)
            {
                replayMismatchCount++;
                replayFailureCount++;
                File.WriteAllText(path, "Diagnostic rerun failed without changing the retained batch result.\n" +
                    "TrialIndex=" + original.TrialIndex.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "TrialSeed=" + original.TrialSeed.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "Strategy=" + original.StrategyId + " v" + original.StrategyVersion + "\n" +
                    "StopReason=" + original.StopReasonId + " / " + original.StopReason + "\n" +
                    "Detail=" + original.Detail + "\nException=" + exception);
                UnityEngine.Debug.LogError("NST stopped Trial diagnostic failed; batch output will continue. " + path + "\n" + exception);
            }
        }

        string rejectionSummary = SimulationRejectionSummaryCsvExporter.Export(diagnosticResults);
        File.WriteAllText(Path.Combine(directory, "rejection_summary.csv"), rejectionSummary);
        UnityEngine.Debug.Log("NST rejection summary CSV: " + Path.Combine(directory, "rejection_summary.csv") +
            " (diagnostic reruns included=" + diagnosticResults.Count + ", replay mismatches=" +
            (replayMismatchCount - replayFailureCount) + ", unavailable=" + replayUnavailableCount +
            ", execution failures=" + replayFailureCount + ")");
    }

    private static bool AppendDiagnosticComparisons(StringBuilder text, SimulationBatchResult batch,
        SimulationTrialResult original, SimulationTrialResult replay, string rosterFingerprint,
        IReadOnlyList<CharacterDefinition> selectedRoster, string eventItemDefinitionId,
        out bool actionHistoryUnavailable)
    {
        actionHistoryUnavailable = false;
        text.AppendLine("Field-by-field replay comparison (Unavailable is never counted as a match):");
        bool allMatch = true;
        Compare(text, "TrialIndex", original.TrialIndex, replay.TrialIndex, ref allMatch);
        Compare(text, "TrialSeed", original.TrialSeed, replay.TrialSeed, ref allMatch);
        Compare(text, "StrategyId", original.StrategyId, replay.StrategyId, ref allMatch);
        Compare(text, "StrategyVersion", original.StrategyVersion, replay.StrategyVersion, ref allMatch);

        string expectedBatchId = CompositionRosterDataReader.CreateBatchConfigurationId(
            rosterFingerprint, eventItemDefinitionId);
        Compare(text, "BatchConfigurationId (retained batch vs replay configuration)",
            batch == null ? null : batch.BatchConfigurationId, expectedBatchId, ref allMatch);
        string replayRosterFingerprint = selectedRoster == null ? null :
            CompositionRosterDataReader.ComputeFingerprint(selectedRoster.Select(x => x.DefinitionId));
        Compare(text, "RosterFingerprint (batch input vs replay input)", rosterFingerprint,
            replayRosterFingerprint, ref allMatch);

        Compare(text, "StopReason", original.StopReasonId, replay.StopReasonId, ref allMatch);
        Compare(text, "FinalPhase", NullableText(original.FinalPhase), NullableText(replay.FinalPhase), ref allMatch);
        Compare(text, "FinalStage", original.FinalCheckpoint == null ? null : original.StageNumber.ToString(CultureInfo.InvariantCulture),
            replay.FinalCheckpoint == null ? null : replay.StageNumber.ToString(CultureInfo.InvariantCulture), ref allMatch);
        Compare(text, "FinalRound", original.FinalCheckpoint == null ? null : original.RoundNumber.ToString(CultureInfo.InvariantCulture),
            replay.FinalCheckpoint == null ? null : replay.RoundNumber.ToString(CultureInfo.InvariantCulture), ref allMatch);
        Compare(text, "FinalStateFingerprint", Fingerprint(original.FinalCheckpoint),
            Fingerprint(replay.FinalCheckpoint), ref allMatch);
        Compare(text, "DecisionCount", original.DecisionCount, replay.DecisionCount, ref allMatch);
        Compare(text, "PhaseDecisionCount", original.PhaseDecisionCount, replay.PhaseDecisionCount, ref allMatch);
        Compare(text, "SuccessfulActionCount", original.SuccessfulActionCount, replay.SuccessfulActionCount, ref allMatch);
        Compare(text, "RejectedActionCount", original.RejectedActionCount, replay.RejectedActionCount, ref allMatch);
        Compare(text, "ActionSequence", original.ActionSequence, replay.ActionSequence, ref allMatch);
        if (original.ExecutionHistory == null || original.ExecutionHistory.Count == 0 ||
            replay.ExecutionHistory == null || replay.ExecutionHistory.Count == 0)
        {
            actionHistoryUnavailable = true;
            text.AppendLine("ActionSequenceDetails: UNAVAILABLE retained Trial history was not recorded; the diagnostic rerun has a separate history and cannot be compared action-by-action.");
        }
        else
            Compare(text, "ActionSequenceDetails", DescribeActionSequence(original),
                DescribeActionSequence(replay), ref allMatch);
        CompareGameCheckpoints(text, original.FinalGameRandomState, replay.FinalGameRandomState, ref allMatch);
        CompareExecutionCheckpoints(text, original.FinalCheckpoint, replay.FinalCheckpoint, ref allMatch);
        CompareStrategyRandom(text, original.FinalStrategyRandomState, replay.FinalStrategyRandomState, ref allMatch);
        CompareStrategyState(text, original.FinalStrategyState, replay.FinalStrategyState, ref allMatch);
        text.AppendLine("ReplayComparison=" + (!allMatch ? "MISMATCH" :
            actionHistoryUnavailable ? "UNAVAILABLE" : "MATCH"));
        return allMatch;
    }

    private sealed class DiagnosticTimingTotals
    {
        public double StrategyRerunSeconds;
        public double ActionReplaySeconds;
        public double DiagnosticCsvExportSeconds;
    }

    private static void WriteFullDiagnostics(SimulationBatchResult batch,
        SimulationRunnerConfiguration runnerConfiguration, string rosterFingerprint,
        string eventItemDefinitionId, string outputDirectory,
        Action<int, int, string, string> progress = null, DiagnosticTimingTotals timings = null)
    {
        if (timings == null) timings = new DiagnosticTimingTotals();
        var replayRows = new List<ReplayDiagnosticCsvRow>(batch.TrialResults.Count * 2);
        var originalRejectionStatus = new Dictionary<string, string>(StringComparer.Ordinal);
        var rerunRejectionSummary = new SimulationFullDiagnosticsCsvExporter.RejectionSummaryAccumulator();
        var rerunRejectionDetails = new StringBuilder();
        var runner = new SimulationRunner(runnerConfiguration);
        var verifier = new SimulationReplayVerifier();
        int actionReplayMatches = 0, actionReplayMismatches = 0, strategyReplayMatches = 0,
            strategyReplayMismatches = 0, unavailable = 0, replayFailures = 0;

        int completedVerificationSteps = 0;
        int totalVerificationSteps = batch.TrialResults.Count * 2;
        foreach (SimulationBatchTrialResult batchRow in batch.TrialResults.OrderBy(x => x.StrategyId, StringComparer.Ordinal)
            .ThenBy(x => x.TrialIndex))
        {
            SimulationTrialResult original = batchRow.FullResult;
            if (original == null)
            {
                replayFailures++;
                unavailable += 2;
                progress?.Invoke(++completedVerificationSteps, totalVerificationSteps, batchRow.StrategyId, "Strategy Rerun Unavailable");
                progress?.Invoke(++completedVerificationSteps, totalVerificationSteps, batchRow.StrategyId, "Action Replay Unavailable");
                replayRows.Add(UnavailableReplayRow(batchRow, "StrategyReexecution", "OriginalTrialUnavailable"));
                replayRows.Add(UnavailableReplayRow(batchRow, "ActionReplay", "OriginalTrialUnavailable"));
                continue;
            }

            string key = SimulationFullDiagnosticsCsvExporter.TrialKey(original);
            bool hasHistory = original.ExecutionHistory != null && original.ExecutionHistory.Count > 0;
            SimulationTrialResult strategyRerun = null;
            string strategyReplayError = null;
            if (hasHistory)
            {
                var verificationTimer = Stopwatch.StartNew();
                try
                {
                    IPlayerStrategy strategy = CreateStrategyFor(original.StrategyId, runnerConfiguration.SessionConfiguration);
                    if (strategy == null) strategyReplayError = "UnsupportedStrategy";
                    else strategyRerun = runner.RunTrial(original.TrialIndex, original.TrialSeed, strategy);
                }
                catch (Exception exception) { strategyReplayError = exception.GetType().Name + ": " + exception.Message; }
                verificationTimer.Stop();
                timings.StrategyRerunSeconds += verificationTimer.Elapsed.TotalSeconds;
                progress?.Invoke(++completedVerificationSteps, totalVerificationSteps,
                    original.StrategyId, "Strategy Rerun (" + verificationTimer.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "s)");
            }
            else
            {
                strategyReplayError = "OriginalExecutionHistoryUnavailable";
                progress?.Invoke(++completedVerificationSteps, totalVerificationSteps, original.StrategyId, "Strategy Rerun Unavailable");
            }

            List<DiagnosticFieldComparison> strategyFields = null;
            ReplayDiagnosticCsvRow strategyRow;
            try
            {
                strategyRow = strategyRerun == null
                    ? UnavailableReplayRow(batchRow, "StrategyReexecution", strategyReplayError)
                    : BuildStrategyReplayRow(batch, original, strategyRerun, rosterFingerprint,
                        runnerConfiguration, eventItemDefinitionId, out strategyFields);
            }
            catch (Exception exception)
            {
                strategyReplayError = exception.GetType().Name + ": " + exception.Message;
                strategyRow = UnavailableReplayRow(batchRow, "StrategyReexecution", strategyReplayError);
            }
            replayRows.Add(strategyRow);
            string rejectionReplayStatus = strategyRow.ReplayIdentical ?? "Unavailable";
            if (strategyRow.ReplayIdentical == "true") strategyReplayMatches++;
            else if (strategyRow.ReplayIdentical == "false") strategyReplayMismatches++;
            else unavailable++;
            if (strategyRerun != null)
            {
                rerunRejectionSummary.Add(strategyRerun, "DiagnosticStrategyRerunHistory", rejectionReplayStatus);
                AppendCsvBody(rerunRejectionDetails, SimulationFullDiagnosticsCsvExporter.ExportRejectionDetails(
                    new[] { strategyRerun }, runnerConfiguration.SessionConfiguration.StageConfig,
                    "DiagnosticStrategyRerunHistory", new Dictionary<string, string> { [key] = rejectionReplayStatus }));
            }
            originalRejectionStatus[key] = rejectionReplayStatus;

            SimulationReplayResult actionReplay = null;
            string actionReplayError = null;
            if (hasHistory)
            {
                var verificationTimer = Stopwatch.StartNew();
                try { actionReplay = verifier.Verify(runnerConfiguration, original, original.ExecutionHistory); }
                catch (Exception exception) { actionReplayError = exception.GetType().Name + ": " + exception.Message; }
                verificationTimer.Stop();
                timings.ActionReplaySeconds += verificationTimer.Elapsed.TotalSeconds;
                progress?.Invoke(++completedVerificationSteps, totalVerificationSteps,
                    original.StrategyId, "Action Replay (" + verificationTimer.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "s)");
            }
            else
            {
                actionReplayError = "OriginalExecutionHistoryUnavailable";
                progress?.Invoke(++completedVerificationSteps, totalVerificationSteps, original.StrategyId, "Action Replay Unavailable");
            }
            ReplayDiagnosticCsvRow actionRow;
            try
            {
                actionRow = actionReplay == null
                    ? UnavailableReplayRow(batchRow, "ActionReplay", actionReplayError)
                    : BuildActionReplayRow(original, actionReplay);
            }
            catch (Exception exception)
            {
                actionReplayError = exception.GetType().Name + ": " + exception.Message;
                actionRow = UnavailableReplayRow(batchRow, "ActionReplay", actionReplayError);
            }
            replayRows.Add(actionRow);
            if (actionRow.ReplayIdentical == "true") actionReplayMatches++;
            else if (actionRow.ReplayIdentical == "false") actionReplayMismatches++;
            else unavailable++;

            if (strategyRow.ReplayIdentical != "true" || actionRow.ReplayIdentical != "true")
            {
                string path = Path.Combine(outputDirectory, "Diagnostics", SafeFilePart(original.StrategyId) +
                    "_v" + SafeFilePart(original.StrategyVersion) + "_trial_" +
                    original.TrialIndex.ToString(CultureInfo.InvariantCulture) + "_replay-mismatch.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                try
                {
                    WriteReplayMismatchDetails(path, original, strategyRerun, strategyFields,
                        actionReplay, runnerConfiguration.SessionConfiguration.StageConfig,
                        strategyReplayError, actionReplayError);
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogError("Could not write replay mismatch details for " + key + ": " + exception);
                }
            }
        }

        progress?.Invoke(totalVerificationSteps, totalVerificationSteps,
            "All Strategies", "CSV Export");
        var exportTimer = Stopwatch.StartNew();
        SimulationTrialResult[] sourceResults = batch.TrialResults.Where(x => x.HasSimulationResult && x.FullResult != null)
            .Select(x => x.FullResult).ToArray();
        string rerunDetailCsv = SimulationFullDiagnosticsCsvExporter.ExportRejectionDetails(
            Array.Empty<SimulationTrialResult>(), runnerConfiguration.SessionConfiguration.StageConfig,
            "DiagnosticStrategyRerunHistory", null) + rerunRejectionDetails;
        string details = CombineCsv(
            SimulationFullDiagnosticsCsvExporter.ExportRejectionDetails(sourceResults,
                runnerConfiguration.SessionConfiguration.StageConfig, "OriginalTrialExecutionHistory", originalRejectionStatus),
            rerunDetailCsv);
        string summary = CombineCsv(
            SimulationFullDiagnosticsCsvExporter.ExportRejectionSummary(sourceResults,
                runnerConfiguration.SessionConfiguration.StageConfig, "OriginalTrialExecutionHistory", originalRejectionStatus),
            rerunRejectionSummary.ExportCsv());
        string replayCsv = SimulationFullDiagnosticsCsvExporter.ExportReplaySummary(replayRows);
        File.WriteAllText(Path.Combine(outputDirectory, "rejection_details.csv"), details, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "rejection_summary.csv"), summary, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "replay_summary.csv"), replayCsv, new UTF8Encoding(false));
        exportTimer.Stop();
        timings.DiagnosticCsvExportSeconds += exportTimer.Elapsed.TotalSeconds;
        UnityEngine.Debug.Log("NST Full Diagnostics: action-replay matched=" + actionReplayMatches +
            " mismatched=" + actionReplayMismatches + "; Strategy-rerun matched=" + strategyReplayMatches +
            " mismatched=" + strategyReplayMismatches + "; unavailable=" + unavailable +
            "; failures=" + replayFailures + "; rejection detail rows=" +
            (details.Split('\n').Length - 2).ToString(CultureInfo.InvariantCulture) +
            "; output=" + outputDirectory);
        long expectedOriginalRejections = sourceResults.Sum(x => x.RejectedActionCount);
        long recordedOriginalRejections = sourceResults.SelectMany(x => x.ExecutionHistory)
            .LongCount(x => x != null && x.Kind == SimulationExecutionEventKind.Action &&
                x.ActionStatus == ActionDispatchStatus.Rejected);
        if (expectedOriginalRejections != recordedOriginalRejections)
            UnityEngine.Debug.LogError("Original rejection count mismatch: trials.csv total=" + expectedOriginalRejections +
                "; OriginalTrialExecutionHistory details=" + recordedOriginalRejections + ". See rejection_details.csv.");
        else
            UnityEngine.Debug.Log("Original rejection detail reconciliation passed: " + recordedOriginalRejections +
                " rejected Actions from OriginalTrialExecutionHistory. Strategy reruns are separately labeled.");
    }

    private static string CombineCsv(string first, string second)
    {
        if (string.IsNullOrEmpty(first)) return second ?? string.Empty;
        if (string.IsNullOrEmpty(second)) return first;
        int newline = second.IndexOf('\n');
        return newline < 0 ? first : first.TrimEnd('\r', '\n') + "\n" + second.Substring(newline + 1);
    }

    private static void AppendCsvBody(StringBuilder target, string csv)
    {
        if (target == null || string.IsNullOrEmpty(csv)) return;
        int newline = csv.IndexOf('\n');
        if (newline >= 0 && newline + 1 < csv.Length) target.Append(csv.Substring(newline + 1));
    }

    private sealed class DiagnosticFieldComparison
    {
        public string Name;
        public string Original;
        public string Replay;
        public string Status;
    }

    private static ReplayDiagnosticCsvRow BuildStrategyReplayRow(SimulationBatchResult batch,
        SimulationTrialResult original, SimulationTrialResult replay, string rosterFingerprint,
        SimulationRunnerConfiguration runnerConfiguration, string eventItemDefinitionId,
        out List<DiagnosticFieldComparison> fields)
    {
        fields = new List<DiagnosticFieldComparison>();
        AddField(fields, "TrialIndex", original.TrialIndex, replay.TrialIndex);
        AddField(fields, "TrialSeed", original.TrialSeed, replay.TrialSeed);
        AddField(fields, "StrategyId", original.StrategyId, replay.StrategyId);
        AddField(fields, "StrategyVersion", original.StrategyVersion, replay.StrategyVersion);
        string expectedBatch = CompositionRosterDataReader.CreateBatchConfigurationId(rosterFingerprint,
            eventItemDefinitionId);
        AddField(fields, "BatchConfigurationId", batch.BatchConfigurationId, expectedBatch);
        string rosterReplay = CompositionRosterDataReader.ComputeFingerprint(
            runnerConfiguration.SessionConfiguration.SelectedRoster.Select(x => x.DefinitionId));
        AddField(fields, "RosterFingerprint", rosterFingerprint, rosterReplay);
        AddField(fields, "Outcome", original.Status, replay.Status);
        AddField(fields, "StopReason", original.StopReasonId, replay.StopReasonId);
        AddField(fields, "FinalPhase", original.FinalPhase, replay.FinalPhase);
        AddField(fields, "FinalStage", original.FinalCheckpoint == null ? (object)null : original.StageNumber,
            replay.FinalCheckpoint == null ? (object)null : replay.StageNumber);
        AddField(fields, "FinalRound", original.FinalCheckpoint == null ? (object)null : original.RoundNumber,
            replay.FinalCheckpoint == null ? (object)null : replay.RoundNumber);
        AddField(fields, "FinalStateFingerprint", StateFingerprintOrNull(original.FinalCheckpoint),
            StateFingerprintOrNull(replay.FinalCheckpoint));
        AddField(fields, "DecisionCount", original.DecisionCount, replay.DecisionCount);
        AddField(fields, "PhaseDecisionCount", original.PhaseDecisionCount, replay.PhaseDecisionCount);
        AddField(fields, "SuccessfulActionCount", original.SuccessfulActionCount, replay.SuccessfulActionCount);
        AddField(fields, "RejectedActionCount", original.RejectedActionCount, replay.RejectedActionCount);
        AddField(fields, "ActionSequence", original.ActionSequence, replay.ActionSequence);
        AddField(fields, "ActionHistory", ActionHistorySignature(original), ActionHistorySignature(replay));
        AddField(fields, "GameRngCheckpoint", RandomCheckpointSignature(original.FinalGameRandomState),
            RandomCheckpointSignature(replay.FinalGameRandomState));
        AddField(fields, "StrategyRngCheckpoint", StrategyRngSignature(original.FinalStrategyRandomState),
            StrategyRngSignature(replay.FinalStrategyRandomState));
        AddField(fields, "StrategyState", StrategyStateSignature(original.FinalStrategyState),
            StrategyStateSignature(replay.FinalStrategyState));
        AddField(fields, "GameSessionCheckpoint", StateFingerprintOrNull(original.FinalCheckpoint),
            StateFingerprintOrNull(replay.FinalCheckpoint));

        var different = fields.Where(x => x.Status == "MISMATCH").ToArray();
        var unavailable = fields.Where(x => x.Status == "UNAVAILABLE").ToArray();
        string overall = different.Length > 0 ? "false" : unavailable.Length > 0 ? "Unavailable" : "true";
        DiagnosticFieldComparison first = fields.FirstOrDefault(x => x.Status != "MATCH");
        return new ReplayDiagnosticCsvRow
        {
            StrategyId = original.StrategyId, StrategyVersion = original.StrategyVersion,
            TrialIndex = original.TrialIndex, TrialSeed = original.TrialSeed,
            OriginalOutcome = original.Status.ToString(), ReplayOutcome = replay.Status.ToString(),
            OriginalStopReason = original.StopReasonId, ReplayStopReason = replay.StopReasonId,
            DiagnosticReplaySucceeded = "true", ReplayIdentical = overall,
            MismatchCount = different.Length.ToString(CultureInfo.InvariantCulture),
            MismatchFields = string.Join(";", different.Select(x => x.Name).Concat(unavailable.Select(x => "Unavailable:" + x.Name))),
            FirstMismatchField = first == null ? string.Empty : first.Name,
            OriginalValue = first == null ? string.Empty : first.Original,
            ReplayValue = first == null ? string.Empty : first.Replay,
            OriginalFinalStateFingerprint = StateFingerprintOrNull(original.FinalCheckpoint) ?? "Unavailable",
            ReplayFinalStateFingerprint = StateFingerprintOrNull(replay.FinalCheckpoint) ?? "Unavailable",
            GameRngIdentical = EqualityStatus(original.FinalGameRandomState, replay.FinalGameRandomState),
            StrategyRngIdentical = EqualityStatus(original.FinalStrategyRandomState, replay.FinalStrategyRandomState),
            StrategyStateIdentical = EqualityStatus(StrategyStateSignature(original.FinalStrategyState),
                StrategyStateSignature(replay.FinalStrategyState)),
            ActionHistoryIdentical = EqualityStatus(ActionHistorySignature(original), ActionHistorySignature(replay)),
            CheckpointIdentical = EqualityStatus(StateFingerprintOrNull(original.FinalCheckpoint),
                StateFingerprintOrNull(replay.FinalCheckpoint)),
            VerificationType = "StrategyReexecution"
        };
    }

    private static void AddField(List<DiagnosticFieldComparison> fields, string name, object original, object replay)
    {
        string left = DiagnosticValue(original), right = DiagnosticValue(replay);
        string status = left == null || right == null ? "UNAVAILABLE" :
            string.Equals(left, right, StringComparison.Ordinal) ? "MATCH" : "MISMATCH";
        fields.Add(new DiagnosticFieldComparison { Name = name, Original = left ?? "Unavailable",
            Replay = right ?? "Unavailable", Status = status });
    }

    private static string DiagnosticValue(object value)
    {
        if (value == null) return null;
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static ReplayDiagnosticCsvRow BuildActionReplayRow(SimulationTrialResult original,
        SimulationReplayResult replay)
    {
        string originalFingerprint = StateFingerprintOrNull(original.FinalCheckpoint) ?? "Unavailable";
        bool finalComparisonAvailable = replay.Succeeded || replay.MismatchKind == "FinalState" ||
            replay.MismatchKind == "GameRandomCheckpoint" || replay.MismatchKind == "ActionIdHistory" ||
            replay.MismatchKind == "StrategyCheckpoint" || replay.MismatchKind == "RunnerProgress";
        string replayFingerprint = finalComparisonAvailable
            ? StateFingerprintOrNull(replay.ReplayFinalCheckpoint) ?? "Unavailable" : "Unavailable";
        string stop = original.StopReasonId;
        string replayIdentical = replay.Succeeded ? "true" : "false";
        return new ReplayDiagnosticCsvRow
        {
            StrategyId = original.StrategyId, StrategyVersion = original.StrategyVersion,
            TrialIndex = original.TrialIndex, TrialSeed = original.TrialSeed,
            OriginalOutcome = original.Status.ToString(), ReplayOutcome = replay.Succeeded ? original.Status.ToString() : "Unavailable",
            OriginalStopReason = stop, ReplayStopReason = replay.Succeeded ? stop : "Unavailable",
            DiagnosticReplaySucceeded = "true", ReplayIdentical = replayIdentical,
            MismatchCount = replay.Succeeded ? "0" : "Unavailable",
            MismatchFields = replay.Succeeded ? string.Empty : replay.MismatchKind,
            FirstMismatchField = replay.Succeeded ? string.Empty : replay.MismatchKind,
            OriginalValue = replay.Expected, ReplayValue = replay.Actual,
            OriginalFinalStateFingerprint = originalFingerprint, ReplayFinalStateFingerprint = replayFingerprint,
            GameRngIdentical = finalComparisonAvailable && original.FinalGameRandomState.HasValue &&
                replay.ReplayFinalCheckpoint != null
                    ? original.FinalGameRandomState.Value.Equals(replay.ReplayFinalCheckpoint.RandomState) ? "true" : "false"
                    : replay.MismatchKind == "GameRandomCheckpoint" ? "false" : "Unavailable",
            StrategyRngIdentical = "Unavailable", StrategyStateIdentical = "Unavailable",
            ActionHistoryIdentical = finalComparisonAvailable
                ? replay.ActionHistoryMatches ? "true" : "false"
                : "Unavailable",
            CheckpointIdentical = finalComparisonAvailable
                ? replay.FinalStateMatches ? "true" : "false"
                : "Unavailable",
            VerificationType = "ActionReplay"
        };
    }

    private static ReplayDiagnosticCsvRow UnavailableReplayRow(SimulationBatchTrialResult row,
        string type, string reason) => new ReplayDiagnosticCsvRow
    {
        StrategyId = row.StrategyId, StrategyVersion = row.StrategyVersion,
        TrialIndex = row.TrialIndex, TrialSeed = row.TrialSeed,
        OriginalOutcome = row.TrialStatus.ToString(), ReplayOutcome = "Unavailable",
        OriginalStopReason = row.StopReasonId, ReplayStopReason = "Unavailable",
        DiagnosticReplaySucceeded = "false", ReplayIdentical = "Unavailable",
        MismatchCount = "Unavailable", MismatchFields = "Unavailable",
        FirstMismatchField = reason ?? "Unavailable", OriginalValue = "Unavailable", ReplayValue = "Unavailable",
        OriginalFinalStateFingerprint = "Unavailable", ReplayFinalStateFingerprint = "Unavailable",
        GameRngIdentical = "Unavailable", StrategyRngIdentical = "Unavailable",
        StrategyStateIdentical = "Unavailable", ActionHistoryIdentical = "Unavailable",
        CheckpointIdentical = "Unavailable", VerificationType = type
    };

    private static string StateFingerprintOrNull(ActionExecutionState state) =>
        state == null ? null : SimulationStateFingerprint.Create(state);
    private static string RandomCheckpointSignature(SimulationRandomStreamsState? state) =>
        state.HasValue ? SimulationStateFingerprint.Create(state.Value) : null;
    private static string StrategyRngSignature(StrategyRngState state) =>
        state == null ? null : SimulationStateFingerprint.Create(state);
    private static string StrategyStateSignature(StrategyState state) =>
        state == null ? null : state.StrategyId + "|" + state.StrategyVersion + "|" + state.SchemaVersion + "|" + state.Payload;
    private static string ActionHistorySignature(SimulationTrialResult result) =>
        result == null || result.ExecutionHistory == null || result.ExecutionHistory.Count == 0 ? null :
        string.Join("\n", result.ExecutionHistory.Select(x => x.Kind + "|" + x.Sequence + "|" + x.DecisionStep + "|" +
            (x.Action == null ? string.Empty : x.Action.ActionId.Value + "|" + x.Action.Kind + "|" + DescribeAction(x.Action)) + "|" +
            x.ExternalEventItemDefinitionId + "|" + x.PhaseBefore + "|" + x.PhaseAfter + "|" +
            x.OperationStatus + "|" + (x.ActionStatus.HasValue ? x.ActionStatus.Value.ToString() : string.Empty) + "|" +
            (x.ActionError.HasValue ? x.ActionError.Value.Code + ":" + x.ActionError.Value.Detail : string.Empty) + "|" +
            x.BeforeFingerprint + "|" + x.AfterFingerprint + "|" +
            StrategyStateSignature(x.StrategyStateAfterDecision) + "|" + StrategyRngSignature(x.StrategyRandomStateAfterDecision) + "|" +
            (x.RunnerProgress == null ? string.Empty : SimulationStateFingerprint.Create(x.RunnerProgress))));

    private static string EqualityStatus(string left, string right) => left == null || right == null
        ? "Unavailable" : string.Equals(left, right, StringComparison.Ordinal) ? "true" : "false";

    private static string EqualityStatus(StrategyRngState left, StrategyRngState right) =>
        left == null || right == null ? "Unavailable" : left.Equals(right) ? "true" : "false";

    private static string EqualityStatus(SimulationRandomStreamsState? left, SimulationRandomStreamsState? right) =>
        !left.HasValue || !right.HasValue ? "Unavailable" : left.Value.Equals(right.Value) ? "true" : "false";

    private static IPlayerStrategy CreateStrategyFor(string strategyId, GameSessionConfiguration configuration) =>
        strategyId == RandomStrategy.StableId ? (IPlayerStrategy)CreateRandomStrategy(configuration) :
        strategyId == HeuristicStrategy.StableId ? CreateHeuristicStrategy(configuration) : null;

    private static void WriteReplayMismatchDetails(string path, SimulationTrialResult original,
        SimulationTrialResult rerun, List<DiagnosticFieldComparison> fields,
        SimulationReplayResult actionReplay, StageProgressConfig stageConfig,
        string strategyReplayError, string actionReplayError)
    {
        var text = new StringBuilder(4096);
        text.AppendLine("NST Full Diagnostics replay mismatch details")
            .AppendLine("Trial=" + original.TrialIndex + " Seed=" + original.TrialSeed)
            .AppendLine("Strategy=" + original.StrategyId + " v" + original.StrategyVersion)
            .AppendLine("OriginalOutcome=" + original.Status + " StopReason=" + original.StopReasonId)
            .AppendLine("ActionReplay=" + (actionReplay == null ? "Unavailable: " + actionReplayError :
                actionReplay.Succeeded ? "Match" : actionReplay.MismatchKind + " expected=" + actionReplay.Expected + " actual=" + actionReplay.Actual))
            .AppendLine("StrategyReexecution=" + (rerun == null ? "Unavailable: " + strategyReplayError :
                rerun.Status + "/" + rerun.StopReasonId));
        if (fields != null)
        {
            DiagnosticFieldComparison first = fields.FirstOrDefault(x => x.Status != "MATCH");
            text.AppendLine("Strategy rerun first mismatch=" + (first == null ? "none" : first.Name +
                " original=" + first.Original + " replay=" + first.Replay));
            foreach (DiagnosticFieldComparison field in fields.Where(x => x.Status != "MATCH"))
                text.AppendLine(field.Status + " " + field.Name + " original=" + field.Original + " replay=" + field.Replay);
            if (rerun != null)
            {
                string[] a = ActionHistoryLines(original), b = ActionHistoryLines(rerun);
                int firstDifferent = FirstDifferent(a, b);
                text.AppendLine("First differing execution record index=" + (firstDifferent < 0 ? "none" : firstDifferent.ToString(CultureInfo.InvariantCulture)));
                AppendHistoryWindow(text, "Original", original.ExecutionHistory, firstDifferent);
                AppendHistoryWindow(text, "StrategyRerun", rerun.ExecutionHistory, firstDifferent);
            }
        }
        if (actionReplay != null && !actionReplay.Succeeded && actionReplay.FirstMismatchSequence.HasValue)
            AppendHistoryWindow(text, "ActionReplay source record", original.ExecutionHistory,
                (int)actionReplay.FirstMismatchSequence.Value);
        if (actionReplay?.ReplayFinalCheckpoint != null)
        {
            text.AppendLine("ActionReplay state at its comparison point:");
            AppendStateSummary(text, actionReplay.ReplayFinalCheckpoint, stageConfig);
            bool replayReachedFinal = actionReplay.Succeeded || actionReplay.MismatchKind == "FinalState" ||
                actionReplay.MismatchKind == "GameRandomCheckpoint" || actionReplay.MismatchKind == "ActionIdHistory" ||
                actionReplay.MismatchKind == "StrategyCheckpoint" || actionReplay.MismatchKind == "RunnerProgress";
            if (replayReachedFinal)
                AppendCheckpointComponentComparisons(text, original.FinalCheckpoint, actionReplay.ReplayFinalCheckpoint);
            else text.AppendLine("Final checkpoint component comparison unavailable: action replay stopped before the final comparison.");
        }
        text.AppendLine("Original Game RNG=" + (original.FinalGameRandomState.HasValue ? DescribeGameRng(original.FinalGameRandomState.Value) : "Unavailable"))
            .AppendLine("Rerun Game RNG=" + (rerun == null || !rerun.FinalGameRandomState.HasValue ? "Unavailable" : DescribeGameRng(rerun.FinalGameRandomState.Value)))
            .AppendLine("Original Strategy RNG=" + DescribeStrategyRng(original.FinalStrategyRandomState))
            .AppendLine("Rerun Strategy RNG=" + DescribeStrategyRng(rerun?.FinalStrategyRandomState))
            .AppendLine("Original StrategyState=" + DescribeStrategyState(original.FinalStrategyState))
            .AppendLine("Rerun StrategyState=" + DescribeStrategyState(rerun?.FinalStrategyState))
            .AppendLine("Original checkpoint:");
        AppendStateSummary(text, original.FinalCheckpoint, stageConfig);
        text.AppendLine("Rerun checkpoint:");
        AppendStateSummary(text, rerun?.FinalCheckpoint, stageConfig);
        AppendCheckpointComponentComparisons(text, original.FinalCheckpoint, rerun?.FinalCheckpoint);
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static string[] ActionHistoryLines(SimulationTrialResult result) =>
        result.ExecutionHistory.Select(x => x.Kind + "|" + x.Sequence + "|" + x.DecisionStep + "|" +
            (x.Action == null ? "" : x.Action.ActionId.Value + ":" + x.Action.Kind + ":" + DescribeAction(x.Action)) + "|" +
            x.ExternalEventItemDefinitionId + "|" + x.PhaseBefore + "|" + x.PhaseAfter + "|" +
            x.OperationStatus + "|" + (x.ActionStatus.HasValue ? x.ActionStatus.Value.ToString() : "") + "|" +
            (x.ActionError.HasValue ? x.ActionError.Value.Code + ":" + x.ActionError.Value.Detail : "") + "|" +
            x.BeforeFingerprint + "|" + x.AfterFingerprint + "|" +
            StrategyStateSignature(x.StrategyStateAfterDecision) + "|" + StrategyRngSignature(x.StrategyRandomStateAfterDecision) + "|" +
            (x.RunnerProgress == null ? "" : SimulationStateFingerprint.Create(x.RunnerProgress))).ToArray();

    private static void AppendCheckpointComponentComparisons(StringBuilder text,
        ActionExecutionState original, ActionExecutionState replay)
    {
        text.AppendLine("Checkpoint component comparison:");
        if (original == null || replay == null)
        {
            text.AppendLine("UNAVAILABLE original=" + (original == null ? "null" : "available") +
                " replay=" + (replay == null ? "null" : "available"));
            return;
        }
        AppendComponent(text, "Stage", original.Stage, replay.Stage);
        AppendComponent(text, "Economy", original.Economy, replay.Economy);
        AppendComponent(text, "Shop", original.Shop, replay.Shop);
        AppendComponent(text, "CharacterPool", original.CharacterPool, replay.CharacterPool);
        AppendComponent(text, "InstanceFactory.LastIssuedNumber", original.InstanceFactory.LastIssuedNumber,
            replay.InstanceFactory.LastIssuedNumber);
        AppendComponent(text, "Board", original.Board, replay.Board);
        AppendComponent(text, "Hand", original.Hand, replay.Hand);
        AppendComponent(text, "Ownership", original.Ownership, replay.Ownership);
        AppendComponent(text, "ItemEffects", original.ItemEffects, replay.ItemEffects);
        AppendComponent(text, "EventItem", original.EventItem, replay.EventItem);
        AppendComponent(text, "EnemyBoard", original.EnemyBoard, replay.EnemyBoard);
        AppendComponent(text, "EnemyPreparation", original.EnemyPreparation, replay.EnemyPreparation);
        AppendComponent(text, "Battle", original.Battle, replay.Battle);
        AppendComponent(text, "BattleResult", original.BattleResult, replay.BattleResult);
        AppendComponent(text, "RandomState", original.RandomState, replay.RandomState);
        AppendComponent(text, "ExecutedActionIds", string.Join("|", original.ExecutedActionIds.Select(x => x.Value)),
            string.Join("|", replay.ExecutedActionIds.Select(x => x.Value)));
    }

    private static void AppendComponent(StringBuilder text, string name, object original, object replay)
    {
        string left = original == null ? "<null>" : SimulationStateFingerprint.Create(original);
        string right = replay == null ? "<null>" : SimulationStateFingerprint.Create(replay);
        text.Append(name).Append(string.Equals(left, right, StringComparison.Ordinal) ? " MATCH " : " MISMATCH ")
            .Append("original=").Append(left).Append(" replay=").AppendLine(right);
    }

    private static int FirstDifferent(string[] a, string[] b)
    {
        int count = Math.Min(a.Length, b.Length);
        for (int i = 0; i < count; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return i;
        return a.Length == b.Length ? -1 : count;
    }

    private static void AppendHistoryWindow(StringBuilder text, string label,
        IReadOnlyList<SimulationExecutionRecord> records, int center)
    {
        text.AppendLine(label + " execution history around mismatch:");
        if (records == null || records.Count == 0) { text.AppendLine("  Unavailable"); return; }
        if (center < 0) center = records.Count - 1;
        int start = Math.Max(0, center - 1), end = Math.Min(records.Count - 1, center + 1);
        for (int i = start; i <= end; i++)
        {
            SimulationExecutionRecord record = records[i];
            text.Append("  #").Append(record.Sequence).Append(" ").Append(record.Kind)
                .Append(" action=").Append(record.Action == null ? "<none>" : DescribeAction(record.Action))
                .Append(" result=").Append(record.OperationStatus)
                .Append(" before=").Append(record.BeforeFingerprint)
                .Append(" after=").Append(record.AfterFingerprint).AppendLine();
        }
    }

    private static string DescribeGameRng(SimulationRandomStreamsState state) =>
        "seed=" + state.TrialSeed + " version=" + state.Version + " Shop=" + Pcg(state.Shop) +
        " Stats=" + Pcg(state.Stats) + " Encounter=" + Pcg(state.Encounter) + " Battle=" + Pcg(state.Battle) +
        " Event=" + Pcg(state.Event) + " Roster=" + Pcg(state.Roster) + " Economy=" + Pcg(state.Economy);

    private static string Pcg(Pcg32State state) => state.AlgorithmVersion + ":" + state.State + ":" + state.Increment;

    private static string NullableText<T>(T? value) where T : struct =>
        value.HasValue ? value.Value.ToString() : null;

    private static string Fingerprint(ActionExecutionState state) =>
        state == null ? null : SimulationStateFingerprint.Create(state);

    private static string DescribeActionSequence(SimulationTrialResult result)
    {
        if (result == null || result.ExecutionHistory == null) return null;
        SimulationExecutionRecord[] actions = result.ExecutionHistory
            .Where(entry => entry.Kind == SimulationExecutionEventKind.Action).ToArray();
        return string.Join("|", actions.Select(entry => entry.Action.Kind + ":" + entry.Action.ActionId.Value + ":" +
            DescribeAction(entry.Action) + ":" + (entry.ActionStatus.HasValue ? entry.ActionStatus.Value.ToString() : "<no-status>") + ":" +
            (entry.ActionError.HasValue ? entry.ActionError.Value.Code + "/" + entry.ActionError.Value.Detail : "<no-error>")));
    }

    private static void Compare(StringBuilder text, string name, object expected, object actual,
        ref bool allMatch)
    {
        if (expected == null || actual == null)
        {
            text.Append(name).Append(": UNAVAILABLE expected=")
                .Append(expected == null ? "<unavailable>" : Convert.ToString(expected, CultureInfo.InvariantCulture))
                .Append(" actual=").AppendLine(actual == null
                    ? "<unavailable>" : Convert.ToString(actual, CultureInfo.InvariantCulture));
            allMatch = false;
            return;
        }
        bool match = Equals(expected, actual);
        text.Append(name).Append(match ? ": MATCH" : ": MISMATCH")
            .Append(" expected=").Append(Convert.ToString(expected, CultureInfo.InvariantCulture))
            .Append(" actual=").AppendLine(Convert.ToString(actual, CultureInfo.InvariantCulture));
        allMatch &= match;
    }

    private static void CompareGameCheckpoints(StringBuilder text,
        SimulationRandomStreamsState? expected, SimulationRandomStreamsState? actual, ref bool allMatch)
    {
        text.AppendLine("GameCore checkpoint fields:");
        if (!expected.HasValue || !actual.HasValue)
        {
            Compare(text, "GameCoreCheckpoint", expected.HasValue ? "available" : null,
                actual.HasValue ? "available" : null, ref allMatch);
            return;
        }
        SimulationRandomStreamsState a = expected.Value, b = actual.Value;
        Compare(text, "GameCoreCheckpoint.Version", a.Version, b.Version, ref allMatch);
        Compare(text, "GameCoreCheckpoint.TrialSeed", a.TrialSeed, b.TrialSeed, ref allMatch);
        ComparePcg(text, "Shop", a.Shop, b.Shop, ref allMatch);
        ComparePcg(text, "Stats", a.Stats, b.Stats, ref allMatch);
        ComparePcg(text, "Encounter", a.Encounter, b.Encounter, ref allMatch);
        ComparePcg(text, "Battle", a.Battle, b.Battle, ref allMatch);
        ComparePcg(text, "Event", a.Event, b.Event, ref allMatch);
        ComparePcg(text, "Roster", a.Roster, b.Roster, ref allMatch);
        ComparePcg(text, "Economy", a.Economy, b.Economy, ref allMatch);
    }

    private static void CompareExecutionCheckpoints(StringBuilder text, ActionExecutionState expected,
        ActionExecutionState actual, ref bool allMatch)
    {
        text.AppendLine("GameSession checkpoint component fingerprints:");
        if (expected == null || actual == null)
        {
            Compare(text, "GameSessionCheckpoint", expected == null ? null : "available",
                actual == null ? null : "available", ref allMatch);
            return;
        }
        Compare(text, "Checkpoint.Stage", SimulationStateFingerprint.Create(expected.Stage),
            SimulationStateFingerprint.Create(actual.Stage), ref allMatch);
        Compare(text, "Checkpoint.Economy", SimulationStateFingerprint.Create(expected.Economy),
            SimulationStateFingerprint.Create(actual.Economy), ref allMatch);
        CompareNullableComponent(text, "Checkpoint.Shop", expected.Shop, actual.Shop, ref allMatch);
        Compare(text, "Checkpoint.CharacterPool", SimulationStateFingerprint.Create(expected.CharacterPool),
            SimulationStateFingerprint.Create(actual.CharacterPool), ref allMatch);
        Compare(text, "Checkpoint.InstanceFactory.LastIssuedNumber", expected.InstanceFactory.LastIssuedNumber,
            actual.InstanceFactory.LastIssuedNumber, ref allMatch);
        Compare(text, "Checkpoint.Board", SimulationStateFingerprint.Create(expected.Board),
            SimulationStateFingerprint.Create(actual.Board), ref allMatch);
        Compare(text, "Checkpoint.Hand", SimulationStateFingerprint.Create(expected.Hand),
            SimulationStateFingerprint.Create(actual.Hand), ref allMatch);
        Compare(text, "Checkpoint.Ownership", SimulationStateFingerprint.Create(expected.Ownership),
            SimulationStateFingerprint.Create(actual.Ownership), ref allMatch);
        Compare(text, "Checkpoint.ItemEffects", SimulationStateFingerprint.Create(expected.ItemEffects),
            SimulationStateFingerprint.Create(actual.ItemEffects), ref allMatch);
        CompareNullableComponent(text, "Checkpoint.EventItem", expected.EventItem, actual.EventItem, ref allMatch);
        CompareNullableComponent(text, "Checkpoint.EnemyBoard", expected.EnemyBoard, actual.EnemyBoard, ref allMatch);
        CompareNullableComponent(text, "Checkpoint.EnemyPreparation", expected.EnemyPreparation, actual.EnemyPreparation, ref allMatch);
        CompareNullableComponent(text, "Checkpoint.Battle", expected.Battle, actual.Battle, ref allMatch);
        CompareNullableComponent(text, "Checkpoint.BattleResult", expected.BattleResult, actual.BattleResult, ref allMatch);
        Compare(text, "Checkpoint.ExecutedActionIds", string.Join("|", expected.ExecutedActionIds.Select(x => x.Value)),
            string.Join("|", actual.ExecutedActionIds.Select(x => x.Value)), ref allMatch);
    }

    private static void CompareNullableComponent(StringBuilder text, string name, object expected,
        object actual, ref bool allMatch)
    {
        string left = expected == null ? "<null>" : SimulationStateFingerprint.Create(expected);
        string right = actual == null ? "<null>" : SimulationStateFingerprint.Create(actual);
        Compare(text, name, left, right, ref allMatch);
    }

    private static void ComparePcg(StringBuilder text, string name, Pcg32State expected,
        Pcg32State actual, ref bool allMatch)
    {
        Compare(text, "GameRng." + name + ".AlgorithmVersion", expected.AlgorithmVersion,
            actual.AlgorithmVersion, ref allMatch);
        Compare(text, "GameRng." + name + ".State", expected.State, actual.State, ref allMatch);
        Compare(text, "GameRng." + name + ".Increment", expected.Increment, actual.Increment, ref allMatch);
    }

    private static void CompareStrategyRandom(StringBuilder text, StrategyRngState expected,
        StrategyRngState actual, ref bool allMatch)
    {
        if (expected == null || actual == null)
        {
            Compare(text, "StrategyRng", expected == null ? null : "available",
                actual == null ? null : "available", ref allMatch);
            return;
        }
        Compare(text, "StrategyRng.AlgorithmId", expected.AlgorithmId, actual.AlgorithmId, ref allMatch);
        Compare(text, "StrategyRng.AlgorithmVersion", expected.AlgorithmVersion, actual.AlgorithmVersion, ref allMatch);
        Compare(text, "StrategyRng.CheckpointVersion", expected.CheckpointVersion, actual.CheckpointVersion, ref allMatch);
        Compare(text, "StrategyRng.TrialSeed", expected.TrialSeed, actual.TrialSeed, ref allMatch);
        Compare(text, "StrategyRng.State", expected.State.State, actual.State.State, ref allMatch);
        Compare(text, "StrategyRng.Increment", expected.State.Increment, actual.State.Increment, ref allMatch);
    }

    private static void CompareStrategyState(StringBuilder text, StrategyState expected,
        StrategyState actual, ref bool allMatch)
    {
        if (expected == null || actual == null)
        {
            Compare(text, "StrategyState", expected == null ? null : "available",
                actual == null ? null : "available", ref allMatch);
            return;
        }
        Compare(text, "StrategyState.StrategyId", expected.StrategyId, actual.StrategyId, ref allMatch);
        Compare(text, "StrategyState.Version", expected.StrategyVersion, actual.StrategyVersion, ref allMatch);
        Compare(text, "StrategyState.SchemaVersion", expected.SchemaVersion, actual.SchemaVersion, ref allMatch);
        Compare(text, "StrategyState.Payload", expected.Payload, actual.Payload, ref allMatch);
    }

    private static bool StrategyStatesEqual(StrategyState left, StrategyState right) =>
        left != null && right != null && left.StrategyId == right.StrategyId &&
        left.StrategyVersion == right.StrategyVersion && left.SchemaVersion == right.SchemaVersion &&
        left.Payload == right.Payload;

    private static string DescribeStrategyState(StrategyState state) => state == null ? "<none>" :
        state.StrategyId + "/v" + state.StrategyVersion + "/schema" + state.SchemaVersion + "/payload=" + state.Payload;

    private static string DescribeStrategyRng(StrategyRngState state) => state == null ? "<none>" :
        state.AlgorithmId + "/v" + state.AlgorithmVersion + "/checkpoint" + state.CheckpointVersion +
        "/seed=" + state.TrialSeed.ToString(CultureInfo.InvariantCulture) + "/state=" +
        state.State.State.ToString(CultureInfo.InvariantCulture) + "/increment=" +
        state.State.Increment.ToString(CultureInfo.InvariantCulture);

    private static string SafeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value;
    }

    private static string DescribeStageRound(string summary, StageProgressConfig config)
    {
        const string marker = "encounter=";
        int start = summary == null ? -1 : summary.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "Stage/Round unavailable";
        start += marker.Length;
        int end = summary.IndexOfAny(new[] { ' ', ']', ',' }, start);
        string value = end < 0 ? summary.Substring(start) : summary.Substring(start, end - start);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            return "Stage/Round unavailable";
        try { return "Stage=" + config.GetStageNumber(index).ToString(CultureInfo.InvariantCulture) +
            " Round=" + config.GetRoundNumber(index).ToString(CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { return "Stage/Round unavailable (EncounterIndex=" + value + ")"; }
    }

    private static void AppendStateSummary(StringBuilder text, ActionExecutionState state,
        StageProgressConfig stageConfig)
    {
        if (state == null) { text.AppendLine("State unavailable."); return; }
        string stageRound;
        try { stageRound = "Stage=" + stageConfig.GetStageNumber(state.Stage.EncounterIndex) +
            " Round=" + stageConfig.GetRoundNumber(state.Stage.EncounterIndex); }
        catch (ArgumentOutOfRangeException) { stageRound = "Stage/Round unavailable"; }
        text.AppendLine(stageRound + " Phase=" + state.Stage.Phase + " EncounterIndex=" + state.Stage.EncounterIndex +
            " Choice=" + (state.Stage.CurrentSelection == null ? "<none>" : state.Stage.CurrentSelection.Choice.ToString()) +
            " EnemyCategory=" + (state.Stage.CurrentSelection == null ? "<none>" : state.Stage.CurrentSelection.EnemyCategory.ToString()) +
            " ShopMode=" + (state.Stage.CurrentSelection == null ? "<none>" : state.Stage.CurrentSelection.ShopMode.ToString()));
        if (state.EventItem != null) text.AppendLine("EventItem=" + state.EventItem.DefinitionId + " Status=" + state.EventItem.Status);
        text.AppendLine("Gold=" + state.Economy.Gold + " Level=" + state.Economy.Level + " XP=" + state.Economy.XP +
            " HP=" + state.Economy.CurrentHP + "/" + state.Economy.MaximumHP);
        var board = new List<string>();
        for (int i = 0; i < state.Board.SlotCount; i++)
        { state.Board.TryGetCharacterIdAt(i, out string id); board.Add(i + ":" + (id ?? "empty")); }
        text.AppendLine("Board=" + string.Join(",", board));
        var hand = new List<string>();
        for (int i = 0; i < state.Hand.SlotCount; i++)
        {
            state.Hand.TryGetSlot(i, out HandSlotContent item);
            string value = item.Kind == HandSlotContentKind.Character ? "C:" + item.CharacterInstanceId :
                item.Kind == HandSlotContentKind.Item ? "I:" + item.ItemDefinitionId : "empty";
            hand.Add(i + ":" + value);
        }
        text.AppendLine("Hand=" + string.Join(",", hand));
        if (state.Shop != null)
        {
            text.AppendLine("Shop=" + state.Shop.Kind + " Stage=" + state.Shop.StageNumber + " Round=" + state.Shop.RoundNumber);
            foreach (ShopOffer offer in state.Shop.Offers)
                text.Append("  OfferId=").Append(offer.OfferId).Append(" Kind=").Append(offer.Kind)
                    .Append(" DefinitionId=").Append(offer.DefinitionId).Append(" Cost=")
                    .Append(offer.CharacterCost.HasValue ? offer.CharacterCost.Value.ToString(CultureInfo.InvariantCulture) : "<none>")
                    .Append(" Price=").Append(offer.DisplayPrice).Append(" Status=").AppendLine(offer.Status.ToString());
        }
    }

    private static void AppendCandidateDiagnostics(StringBuilder text, GameSessionConfiguration config,
        SimulationTrialResult result, IPlayerStrategy strategy)
    {
        if (result.FinalCheckpoint == null) return;
        ActionExecutionState state = result.FinalCheckpoint;
        AppendPoolConsistencyDiagnostics(text, state);
        ObservationBuildResult build = ObservationBuilder.Build(new ObservationSource(state.Stage,
            config.StageConfig, state.Economy, config.EconomyConfig, state.Board, state.Hand,
            state.Ownership, config.CharacterDefinitions, config.SelectedRoster,
            config.FullCharacterRoster, config.ObservationItemDefinitions, state.Shop,
            state.ItemEffects, state.EventItem, config.EnemyDefinitions, state.EnemyBoard,
            state.EnemyPreparation, state.Battle, state.BattleResult));
        if (build == null || !build.Succeeded || build.Observation == null)
        { text.AppendLine("Candidate evaluation unavailable: " + (build == null ? "null observation" : string.Join("; ", build.Errors))); return; }
        var actionId = new ActionId("diagnostic:" + result.TrialIndex.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("StartBattle legal in current public candidates: " +
            (strategy is HeuristicStrategy heuristic
                ? heuristic.GetEvaluations(build.Observation, actionId).Any(x => x.Action is StartBattleAction)
                : strategy is RandomStrategy random && random.GetCandidates(build.Observation, actionId).Any(x => x is StartBattleAction)));
        if (strategy is HeuristicStrategy hs)
        {
            IReadOnlyList<HeuristicActionEvaluation> evaluations = hs.GetEvaluations(build.Observation, actionId);
            text.AppendLine("Current final-state Heuristic candidate count=" + evaluations.Count + " (not per-decision rank).");
            text.AppendLine("Current Heuristic public candidate evaluations (fresh final-state diagnostic enumeration):");
            foreach (HeuristicActionEvaluation evaluation in evaluations)
                text.AppendLine("  " + DescribeAction(evaluation.Action) + " score=" +
                    evaluation.Score.ToString("R", CultureInfo.InvariantCulture) + " reason=" + evaluation.Reason);
        }
        else if (strategy is RandomStrategy rs)
        {
            IReadOnlyList<GameAction> candidates = rs.GetCandidates(build.Observation, actionId);
            text.AppendLine("Current final-state Random candidate count=" + candidates.Count + ".");
            text.AppendLine("Current RandomStrategy public candidate set (final-state diagnostic enumeration):");
            foreach (GameAction candidate in candidates)
                text.AppendLine("  " + DescribeAction(candidate));
        }
    }

    private static void AppendPoolConsistencyDiagnostics(StringBuilder text, ActionExecutionState state)
    {
        var owned = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (CharacterInstanceView instance in state.Ownership.GetOwnedInstances())
        {
            int copies = instance.PoolCopies;
            owned[instance.DefinitionId] = checked((owned.TryGetValue(instance.DefinitionId, out int count) ? count : 0) + copies);
        }
        var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
        if (state.Shop != null && state.Shop.Kind == ShopKind.Character)
        {
            foreach (ShopOffer offer in state.Shop.Offers)
            {
                if (offer.Kind != ShopOfferKind.Character || offer.Status != ShopOfferStatus.Available) continue;
                reserved[offer.DefinitionId] = checked((reserved.TryGetValue(offer.DefinitionId, out int count) ? count : 0) + 1);
            }
        }

        text.AppendLine("Character Pool consistency (diagnostic only):");
        foreach (KeyValuePair<string, CharacterPoolEntry> pair in state.CharacterPool.Entries.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            int expectedOwned = owned.TryGetValue(pair.Key, out int ownedCount) ? ownedCount : 0;
            int expectedReserved = reserved.TryGetValue(pair.Key, out int reservedCount) ? reservedCount : 0;
            CharacterPoolEntry actual = pair.Value;
            bool consistent = actual.OwnedCopies == expectedOwned && actual.ReservedCopies == expectedReserved;
            text.Append("  ").Append(pair.Key).Append(" owned=").Append(actual.OwnedCopies).Append(" expected=").Append(expectedOwned)
                .Append(" reserved=").Append(actual.ReservedCopies).Append(" expected=").Append(expectedReserved)
                .Append(" remaining=").Append(actual.RemainingCopies).Append(" initial=").Append(actual.InitialStockLimit)
                .Append(" consistent=").AppendLine(consistent.ToString());
        }
    }

    private static string DescribeAction(GameAction action)
    {
        if (action == null) return "none";
        switch (action)
        {
            case SelectStageChoiceAction x: return "SelectStageChoice(" + x.Choice + ")";
            case BuyCharacterAction x: return "BuyCharacter(" + x.OfferId + "," + x.Placement.Kind + ":" + x.Placement.SlotIndex + ")";
            case BuyItemAction x: return "BuyItem(" + x.OfferId + ",hand=" + x.HandSlot + ")";
            case RerollShopAction x: return "RerollShop(" + x.ShopKind + ")";
            case MoveOrSwapBoardAction x: return "MoveOrSwapBoard(" + x.SourceSlot + "->" + x.TargetSlot + ")";
            case MoveOrSwapHandAction x: return "MoveOrSwapHand(" + x.SourceSlot + "->" + x.TargetSlot + ")";
            case MoveBoardToHandAction x: return "MoveBoardToHand(" + x.BoardSlot + "->" + x.HandSlot + ")";
            case MoveHandToBoardAction x: return "MoveHandToBoard(" + x.HandSlot + "->" + x.BoardSlot + ")";
            case SellCharacterAction x: return "SellCharacter(" + x.InstanceId + ")";
            case SellItemAction x: return "SellItem(hand=" + x.HandSlot + ")";
            case UseEventItemAction x: return "UseEventItem(" + x.Target.Kind + ":" + x.Target.SlotIndex + ")";
            case UseHandItemAction x: return "UseHandItem(hand=" + x.HandSlot + "," + x.Target.Kind + ":" + x.Target.SlotIndex + ")";
            default: return action.Kind.ToString();
        }
    }

    internal static RandomStrategy CreateRandomStrategy(GameSessionConfiguration sessionConfiguration, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler = null)
    {
        if (sessionConfiguration == null) throw new ArgumentNullException(nameof(sessionConfiguration));
        return new RandomStrategy(new RandomStrategySettings(
            preparationDecisionLimit: 30,
            shopRerollGoldCost: sessionConfiguration.ShopRerollGoldCost,
            normalExperienceGoldCost: sessionConfiguration.EconomyConfig.NormalExperiencePurchaseGoldCost,
            selectBExperienceGoldCost: sessionConfiguration.EconomyConfig.SelectBExperiencePurchaseGoldCost), profiler);
    }

    internal static HeuristicStrategy CreateHeuristicStrategy(GameSessionConfiguration sessionConfiguration, Action<string> decisionDiagnosticSink = null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler = null)
    {
        if (sessionConfiguration == null) throw new ArgumentNullException(nameof(sessionConfiguration));
        return new HeuristicStrategy(new HeuristicStrategySettings(
            shopRerollGoldCost: sessionConfiguration.ShopRerollGoldCost,
            normalExperienceGoldCost: sessionConfiguration.EconomyConfig.NormalExperiencePurchaseGoldCost,
            selectBExperienceGoldCost: sessionConfiguration.EconomyConfig.SelectBExperiencePurchaseGoldCost,
            selectDHealAmount: sessionConfiguration.StageConfig.SelectDHealAmount), decisionDiagnosticSink, profiler);
    }

    internal static HeuristicStrategy CreateHeuristicStrategyV8(GameSessionConfiguration sessionConfiguration, Action<string> decisionDiagnosticSink = null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler = null)
    {
        if (sessionConfiguration == null) throw new ArgumentNullException(nameof(sessionConfiguration));
        return HeuristicStrategy.CreateV8(new HeuristicStrategySettings(
            shopRerollGoldCost: sessionConfiguration.ShopRerollGoldCost,
            normalExperienceGoldCost: sessionConfiguration.EconomyConfig.NormalExperiencePurchaseGoldCost,
            selectBExperienceGoldCost: sessionConfiguration.EconomyConfig.SelectBExperiencePurchaseGoldCost,
            selectDHealAmount: sessionConfiguration.StageConfig.SelectDHealAmount,
            characterStarValue: 0.75, includeTemporaryCharacterBonuses: true), decisionDiagnosticSink, profiler);
    }

    internal static HeuristicStrategySettings CreateHeuristicStrategyV9Settings(GameSessionConfiguration sessionConfiguration)
    {
        if (sessionConfiguration == null) throw new ArgumentNullException(nameof(sessionConfiguration));
        return new HeuristicStrategySettings(
            shopRerollGoldCost: sessionConfiguration.ShopRerollGoldCost,
            normalExperienceGoldCost: sessionConfiguration.EconomyConfig.NormalExperiencePurchaseGoldCost,
            selectBExperienceGoldCost: sessionConfiguration.EconomyConfig.SelectBExperiencePurchaseGoldCost,
            selectDHealAmount: sessionConfiguration.StageConfig.SelectDHealAmount,
            characterStarValue: 0.75, includeTemporaryCharacterBonuses: true,
            purchasePowerReference: 3.5, immediateBoardPurchasePowerWeight: 4.0,
            handPurchasePowerWeight: 0.5);
    }

    internal static HeuristicStrategy CreateHeuristicStrategyV9(GameSessionConfiguration sessionConfiguration,
        Action<string> decisionDiagnosticSink = null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler = null)
    {
        return HeuristicStrategy.CreateV9(CreateHeuristicStrategyV9Settings(sessionConfiguration), decisionDiagnosticSink, profiler);
    }

    internal static HeuristicStrategySettings CreateHeuristicStrategyV10Settings(GameSessionConfiguration sessionConfiguration)
    {
        if (sessionConfiguration == null) throw new ArgumentNullException(nameof(sessionConfiguration));
        return new HeuristicStrategySettings(
            shopRerollGoldCost: sessionConfiguration.ShopRerollGoldCost,
            normalExperienceGoldCost: sessionConfiguration.EconomyConfig.NormalExperiencePurchaseGoldCost,
            selectBExperienceGoldCost: sessionConfiguration.EconomyConfig.SelectBExperiencePurchaseGoldCost,
            selectDHealAmount: sessionConfiguration.StageConfig.SelectDHealAmount,
            characterStarValue: 0.75, includeTemporaryCharacterBonuses: true,
            purchasePowerReference: 3.5, immediateBoardPurchasePowerWeight: 4.0,
            handPurchasePowerWeight: 0.5);
    }

    internal static HeuristicStrategy CreateHeuristicStrategyV10(GameSessionConfiguration sessionConfiguration,
        Action<string> decisionDiagnosticSink = null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler = null)
    {
        return HeuristicStrategy.CreateV10(CreateHeuristicStrategyV10Settings(sessionConfiguration), decisionDiagnosticSink, profiler);
    }

    internal static HeuristicStrategySettings CreateHeuristicStrategyV11Settings(GameSessionConfiguration sessionConfiguration)
    {
        if (sessionConfiguration == null) throw new ArgumentNullException(nameof(sessionConfiguration));
        return CreateHeuristicStrategyV10Settings(sessionConfiguration);
    }

    internal static HeuristicStrategy CreateHeuristicStrategyV11(GameSessionConfiguration sessionConfiguration,
        Action<string> decisionDiagnosticSink = null, ProjectNST.GameCore.Sessions.SimulationProfileCollector profiler = null)
    {
        return HeuristicStrategy.CreateV11(CreateHeuristicStrategyV11Settings(sessionConfiguration), decisionDiagnosticSink, profiler);
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    private static void VerifySharedSeedsAndCounts(SimulationBatchResult result, int trialCount)
    {
        if (result == null || result.TrialCount != trialCount || result.TrialResults.Count != trialCount * 2)
            throw new InvalidOperationException("Batch did not return exactly " + (trialCount * 2) + " retained trial rows.");
        string[] expectedStrategies = { RandomStrategy.StableId, HeuristicStrategy.StableId };
        foreach (string strategy in expectedStrategies)
            if (result.TrialResults.Count(x => x.StrategyId == strategy) != trialCount)
                throw new InvalidOperationException("Batch did not return " + trialCount + " rows for " + strategy + ".");
        if (result.TrialSeeds.Count != trialCount || result.TrialSeeds.Select(x => x.TrialIndex).Distinct().Count() != trialCount)
            throw new InvalidOperationException("Trial index/seed list is not " + trialCount + " unique zero-based trials.");
    }

    private static void WriteCsv(string path, SimulationExportResult export)
    {
        SimulationFileWriteResult write = SimulationCsvFileWriter.Write(path, export);
        if (!write.Succeeded)
            throw new IOException("CSV write failed for " + path + ": " + write.ErrorCode + " " + write.ErrorMessage);
    }

    private static void WriteCsvText(string path, string text) =>
        File.WriteAllText(path, text ?? string.Empty, new UTF8Encoding(false));

    private static string BuildPerformanceSummary(SimulationBatchResult result, TimeSpan configuration,
        IReadOnlyDictionary<string, double> strategySeconds,
        IReadOnlyDictionary<string, List<double>> trialDurations, DiagnosticTimingTotals diagnostics,
        TimeSpan overallElapsed, TimeSpan totalDiagnostics, TimeSpan csvExport,
        long managedStart, long managedMax, long managedEnd, long workingSetStart, long workingSetMax, long workingSetEnd,
        int gc0Start, int gc1Start, int gc2Start, string outputDirectory)
    {
        var csv = new StringBuilder().AppendLine("Metric,Value,Unit");
        void Add(string metric, object value, string unit = "") =>
            csv.Append('"').Append(metric).Append("\",\"")
                .Append(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "Unavailable")
                .Append("\",\"").Append(unit).AppendLine("\"");
        double originalSeconds = strategySeconds.Values.Sum();
        double[] samples = trialDurations.Values.SelectMany(x => x).OrderBy(x => x).ToArray();
        Add("ConfigurationConstructionSeconds", configuration.TotalSeconds, "seconds");
        Add("OriginalTrialExecutionSeconds", originalSeconds, "seconds");
        Add("StrategyRerunSeconds", _fullDiagnosticsRequested ? (object)diagnostics.StrategyRerunSeconds : "Unavailable", "seconds");
        Add("ActionReplaySeconds", _fullDiagnosticsRequested ? (object)diagnostics.ActionReplaySeconds : "Unavailable", "seconds");
        Add("DiagnosticCsvExportSeconds", _fullDiagnosticsRequested ? (object)diagnostics.DiagnosticCsvExportSeconds : "Unavailable", "seconds");
        Add("CsvOutputSeconds", csvExport.TotalSeconds, "seconds");
        Add("TotalElapsedSeconds", overallElapsed.TotalSeconds, "seconds; Stopwatch from menu start through standard CSV export");
        foreach (KeyValuePair<string, double> pair in strategySeconds.OrderBy(x => x.Key, StringComparer.Ordinal))
            Add("StrategyExecutionSeconds." + pair.Key, pair.Value, "seconds");
        Add("AverageTrialSeconds", samples.Length == 0 ? "Unavailable" : samples.Average(), "seconds");
        Add("MedianTrialSeconds", Percentile(samples, 0.50), "seconds");
        Add("P95TrialSeconds", Percentile(samples, 0.95), "seconds");
        Add("MaximumTrialSeconds", samples.Length == 0 ? "Unavailable" : samples[samples.Length - 1], "seconds");
        Add("TrialsPerSecond", originalSeconds <= 0 ? "Unavailable" : result.TrialResults.Count / originalSeconds, "trial records/second");
        Add("ManagedMemoryStart", managedStart, "bytes");
        Add("ManagedMemoryObservedMax", managedMax, "bytes; sampled, not exact peak");
        Add("ManagedMemoryEnd", managedEnd, "bytes");
        Add("ProcessWorkingSetStart", workingSetStart < 0 ? "Unavailable" : (object)workingSetStart, "bytes");
        Add("ProcessWorkingSetObservedMax", workingSetMax < 0 ? "Unavailable" : (object)workingSetMax, "bytes; sampled");
        Add("ProcessWorkingSetEnd", workingSetEnd < 0 ? "Unavailable" : (object)workingSetEnd, "bytes");
        Add("GCCollectionCountGen0", GC.CollectionCount(0) - gc0Start, "collections");
        Add("GCCollectionCountGen1", GC.CollectionCount(1) - gc1Start, "collections");
        Add("GCCollectionCountGen2", GC.CollectionCount(2) - gc2Start, "collections");
        Add("OutputBytesExcludingSelfReports", GetOutputBytes(outputDirectory, "performance_summary.csv", "batch_execution_summary.txt"), "bytes");
        return csv.ToString();
    }

    private static string BuildExecutionSummary(SimulationBatchResult result, int trialCount, bool fullDiagnostics,
        TimeSpan elapsed, TimeSpan configuration, TimeSpan diagnostics, TimeSpan csvExport,
        long managedStart, long managedMax, long managedEnd, long workingSetStart, long workingSetMax, long workingSetEnd,
        int gc0Start, int gc1Start, int gc2Start, string rosterFingerprint, string eventItemId,
        string outputDirectory)
    {
        var text = new StringBuilder();
        text.AppendLine("RunDate=" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture))
            .AppendLine("UnityVersion=" + Application.unityVersion)
            .AppendLine("Mode=" + (fullDiagnostics ? "FullDiagnostics" : "Standard"))
            .AppendLine("TrialCountPerStrategy=" + trialCount.ToString(CultureInfo.InvariantCulture))
            .AppendLine("BaseSeed=" + BaseSeed.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Strategies=" + string.Join(";", result.StrategySummaries.Select(x => x.StrategyId + "/v" + x.StrategyVersion)))
            .AppendLine("RosterFingerprint=" + rosterFingerprint)
            .AppendLine("EventItemDefinitionId=" + eventItemId)
            .AppendLine("BatchConfigurationId=" + result.BatchConfigurationId)
            .AppendLine("TrialSeedAlgorithm=" + result.TrialSeedAlgorithm)
            .AppendLine("ActualTrialRows=" + result.TrialResults.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Completed=" + result.StrategySummaries.Sum(x => x.CompletedCount).ToString(CultureInfo.InvariantCulture))
            .AppendLine("GameOver=" + result.StrategySummaries.Sum(x => x.GameOverCount).ToString(CultureInfo.InvariantCulture))
            .AppendLine("Stopped=" + result.StrategySummaries.Sum(x => x.StoppedCount).ToString(CultureInfo.InvariantCulture))
            .AppendLine("BatchExecutionFailures=" + result.StrategySummaries.Sum(x => x.BatchExecutionFailureCount).ToString(CultureInfo.InvariantCulture))
            .AppendLine("RejectedActions=" + result.TrialResults.Sum(x => (long)x.RejectedActionCount).ToString(CultureInfo.InvariantCulture))
            .AppendLine("ReplayVerificationRows=" + ReadReplayCounts(outputDirectory))
            .AppendLine("ConfigurationSeconds=" + configuration.TotalSeconds.ToString("R", CultureInfo.InvariantCulture))
            .AppendLine("TotalMeasuredSeconds=" + elapsed.TotalSeconds.ToString("R", CultureInfo.InvariantCulture))
            .AppendLine("DiagnosticsSeconds=" + diagnostics.TotalSeconds.ToString("R", CultureInfo.InvariantCulture))
            .AppendLine("CsvExportSeconds=" + csvExport.TotalSeconds.ToString("R", CultureInfo.InvariantCulture))
            .AppendLine("ManagedMemoryStartBytes=" + managedStart.ToString(CultureInfo.InvariantCulture))
            .AppendLine("ManagedMemoryObservedMaxBytes=" + managedMax.ToString(CultureInfo.InvariantCulture))
            .AppendLine("ManagedMemoryEndBytes=" + managedEnd.ToString(CultureInfo.InvariantCulture))
            .AppendLine("ProcessWorkingSetStartBytes=" + (workingSetStart < 0 ? "Unavailable" : workingSetStart.ToString(CultureInfo.InvariantCulture)))
            .AppendLine("ProcessWorkingSetObservedMaxBytes=" + (workingSetMax < 0 ? "Unavailable" : workingSetMax.ToString(CultureInfo.InvariantCulture)))
            .AppendLine("ProcessWorkingSetEndBytes=" + (workingSetEnd < 0 ? "Unavailable" : workingSetEnd.ToString(CultureInfo.InvariantCulture)))
            .AppendLine("GCCollections=" + (GC.CollectionCount(0) - gc0Start) + "/" + (GC.CollectionCount(1) - gc1Start) + "/" + (GC.CollectionCount(2) - gc2Start))
            .AppendLine("OutputBytesExcludingSelfReports=" + GetOutputBytes(outputDirectory, "performance_summary.csv", "batch_execution_summary.txt"))
            .AppendLine("CompletionState=Completed");
        return text.ToString();
    }

    private static string ReadReplayCounts(string outputDirectory)
    {
        string path = Path.Combine(outputDirectory, "replay_summary.csv");
        if (!File.Exists(path)) return "Unavailable";
        string[] rows = File.ReadAllLines(path).Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        int matching = rows.Count(x => x.Split(',').Length > 9 && x.Split(',')[9] == "\"true\"");
        int mismatching = rows.Count(x => x.Split(',').Length > 9 && x.Split(',')[9] == "\"false\"");
        int unavailable = rows.Length - matching - mismatching;
        return "rows=" + rows.Length + "; matched=" + matching + "; mismatched=" + mismatching + "; unavailable=" + unavailable;
    }

    private static long GetOutputBytes(string directory, params string[] excludedFileNames)
    {
        if (!Directory.Exists(directory)) return 0;
        var excluded = new HashSet<string>(excludedFileNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            if (!excluded.Contains(Path.GetFileName(path))) total += new FileInfo(path).Length;
        return total;
    }

    private static long TryGetWorkingSet()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().WorkingSet64; }
        catch { return -1; }
    }

    private static string Percentile(double[] sorted, double percentile)
    {
        if (sorted == null || sorted.Length == 0) return "Unavailable";
        int index = Math.Max(0, Math.Min(sorted.Length - 1, (int)Math.Ceiling(percentile * sorted.Length) - 1));
        return sorted[index].ToString("R", CultureInfo.InvariantCulture);
    }

    private static void LogBatch(SimulationBatchResult result, TimeSpan elapsed, string outputDirectory)
    {
        UnityEngine.Debug.Log("NST 100 Trial comparison finished. Elapsed: " + elapsed.ToString("c", CultureInfo.InvariantCulture));
        foreach (StrategyBatchSummary summary in result.StrategySummaries)
        {
            UnityEngine.Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} v{1}: trials={2}, Completed={3}, GameOver={4}, Stopped={5}, WinRate={6}, ResolvedRate={7:P2}, AverageStage={8}, AverageDecisions={9}, AverageBattles={10}, StopReasons=[{11}]",
                summary.StrategyId, summary.StrategyVersion, summary.TotalTrials, summary.CompletedCount,
                summary.GameOverCount, summary.StoppedCount,
                summary.WinRate.HasValue ? summary.WinRate.Value.ToString("P2", CultureInfo.InvariantCulture) : "未定義",
                summary.ResolvedRate,
                summary.ReachedStage.Mean.HasValue ? summary.ReachedStage.Mean.Value.ToString("F2", CultureInfo.InvariantCulture) : "未定義",
                summary.Decisions.Mean.HasValue ? summary.Decisions.Mean.Value.ToString("F2", CultureInfo.InvariantCulture) : "未定義",
                summary.Battles.Mean.HasValue ? summary.Battles.Mean.Value.ToString("F2", CultureInfo.InvariantCulture) : "未定義",
                string.Join(",", summary.StopReasonCounts.Where(x => x.Value > 0).Select(x => x.Key + "=" + x.Value))));
        }
        UnityEngine.Debug.Log("CSV output: " + outputDirectory);
    }
}
