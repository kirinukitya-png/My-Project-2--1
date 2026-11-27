using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Strategy;
using ProjectNST.GameUnityAdapter;
using ProjectNST.SimulationExport;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

public sealed class NSTCustomTrialTools : EditorWindow
{
    private const string ShopScene = "Assets/Scenes/shop.unity";
    private enum StrategySet { HeuristicV7, Random, Both, HeuristicV8, HeuristicV7AndV8, HeuristicV9, HeuristicV8AndV9, HeuristicV10, HeuristicV9AndV10, HeuristicV11, HeuristicV10AndV11, HeuristicV12, HeuristicV11AndV12, HeuristicV13, HeuristicV12AndV13, HeuristicV14, HeuristicV13AndV14, HeuristicV15, HeuristicV14AndV15, HeuristicV16, HeuristicV15AndV16 }
    private enum ProfileStrategySet { Heuristic, Random, Both }
    private enum ReplayScope { All, Abnormal, Rejected, Explicit }
    private StrategySet strategies = StrategySet.HeuristicV7;
    private ProfileStrategySet profileStrategies = ProfileStrategySet.Both;
    private ReplayScope replayScope = ReplayScope.Abnormal;
    private string seedText = "12345", indexText = "217, 221, 356, 418, 900, 906", replayText = "217, 221, 356, 418, 900, 906";
    private string originalCsv = "", originalSummary = "";
    private long startIndex;
    private int count = 100;
    private int v14V15Count = 20;
    private int v15V16Count = 20;
    private bool detailedV16DecisionTrace;
    private long profileStartIndex;
    private int profileCount = 20;
    private bool fullDiagnostics = true, strategyRerun = true, actionReplay = true;
    private bool writeActionHistory = true, writeDecisionDetails = true, writeFinalState = true, validatePool = true, measurePerformance = true;
    private bool captureHeuristicDecisionTrace;
    private bool detailedV15DecisionTrace;
    private bool detailedProfiling;
    private bool allocationProfiling;
    private static bool running;

    [MenuItem("Tools/NST/Diagnose Specific Trials")]
    private static void OpenDiagnose() => OpenWindow();
    [MenuItem("Tools/NST/Run Custom Trials")]
    private static void OpenCustom() => OpenWindow();
    private static void OpenWindow() { var w = GetWindow<NSTCustomTrialTools>("NST Trial Tools"); w.minSize = new Vector2(520, 570); }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("NST Trial Tools", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Strategy versions", "Heuristic v7–v16 / Random v" + RandomStrategy.StableVersion);
        strategies = (StrategySet)EditorGUILayout.EnumPopup("Strategy", strategies);
        seedText = EditorGUILayout.TextField("Base Seed", seedText);
        fullDiagnostics = EditorGUILayout.Toggle("Full diagnostics", fullDiagnostics);
        detailedProfiling = EditorGUILayout.Toggle("Enable detailed performance profiling", detailedProfiling);
        allocationProfiling = EditorGUILayout.Toggle("Enable allocation sampling", allocationProfiling);
        if (fullDiagnostics)
        {
            strategyRerun = EditorGUILayout.Toggle("Strategy rerun", strategyRerun);
            actionReplay = EditorGUILayout.Toggle("Action Replay", actionReplay);
            writeActionHistory = EditorGUILayout.Toggle("Action history output", writeActionHistory);
            writeDecisionDetails = EditorGUILayout.Toggle("Decision details output", writeDecisionDetails);
            writeFinalState = EditorGUILayout.Toggle("Final state output", writeFinalState);
            validatePool = EditorGUILayout.Toggle("Pool validation", validatePool);
            measurePerformance = EditorGUILayout.Toggle("Performance timing", measurePerformance);
            captureHeuristicDecisionTrace = EditorGUILayout.Toggle("Capture actual Heuristic decision candidates", captureHeuristicDecisionTrace);
            if (captureHeuristicDecisionTrace &&
                (strategies == StrategySet.HeuristicV15 || strategies == StrategySet.HeuristicV14AndV15))
                detailedV15DecisionTrace = EditorGUILayout.Toggle("Detailed v15 candidate trace", detailedV15DecisionTrace);
            if (captureHeuristicDecisionTrace &&
                (strategies == StrategySet.HeuristicV16 || strategies == StrategySet.HeuristicV15AndV16))
                detailedV16DecisionTrace = EditorGUILayout.Toggle("Detailed v16 candidate trace", detailedV16DecisionTrace);
            replayScope = (ReplayScope)EditorGUILayout.EnumPopup("Replay scope", replayScope);
            if (replayScope == ReplayScope.Explicit) replayText = EditorGUILayout.TextField("Replay indices", replayText);
        }
        originalCsv = EditorGUILayout.TextField("Original trials.csv (optional)", originalCsv);
        originalSummary = EditorGUILayout.TextField("Original batch summary (optional)", originalSummary);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Diagnose selected TrialIndex values", EditorStyles.boldLabel);
        indexText = EditorGUILayout.TextField("Indices / inclusive ranges", indexText);
        if (GUILayout.Button("Diagnose Specific Trials"))
        {
            if (TrialIndexSelectionParser.TryParse(indexText, out var selected, out string error)) Run(selected, true);
            else EditorUtility.DisplayDialog("NST", error, "OK");
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Custom range", EditorStyles.boldLabel);
        startIndex = EditorGUILayout.LongField("Starting TrialIndex", startIndex);
        bool v15Comparison = strategies == StrategySet.HeuristicV14AndV15;
        bool v16Comparison = strategies == StrategySet.HeuristicV15AndV16;
        int selectedCount = EditorGUILayout.IntField("Trial count", v16Comparison ? v15V16Count : v15Comparison ? v14V15Count : count);
        if (v16Comparison) v15V16Count = selectedCount;
        else if (v15Comparison) v14V15Count = selectedCount;
        else count = selectedCount;
        if (GUILayout.Button("Run Custom Trials"))
        {
            if (selectedCount < 1 || startIndex < 0 || startIndex > long.MaxValue - selectedCount + 1L)
                EditorUtility.DisplayDialog("NST", "Trial count must be positive and the TrialIndex range must be valid.", "OK");
            else Run(Enumerable.Range(0, selectedCount).Select(i => startIndex + i).ToArray(), false);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Performance profiling", EditorStyles.boldLabel);
        profileStrategies = (ProfileStrategySet)EditorGUILayout.EnumPopup("Profile Strategy", profileStrategies);
        profileStartIndex = EditorGUILayout.LongField("Profile Trial start Index", profileStartIndex);
        profileCount = EditorGUILayout.IntField("Profile Trial count", profileCount);
        EditorGUILayout.HelpBox("Runs 2 warm-ups per selected Strategy, then paired baseline/profile runs for the configured Trial range. Detailed diagnostics are not required.", MessageType.Info);
        if (GUILayout.Button("Run Performance Profile")) RunPerformanceProfile();
        EditorGUILayout.HelpBox("Cancel is checked between Trials. A currently running Trial completes before cancellation.", MessageType.Info);
    }

    private void Run(IReadOnlyList<long> indices, bool specific)
    {
        if (running) { Debug.LogWarning("NST simulation is already running."); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("NST", "Exit Play Mode first.", "OK"); return; }
        if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong baseSeed))
        { EditorUtility.DisplayDialog("NST", "Base Seed must be a UInt64 value.", "OK"); return; }
        string replayError = null;
        if (fullDiagnostics && replayScope == ReplayScope.Explicit && !TrialIndexSelectionParser.TryParse(replayText, out _, out replayError))
        { EditorUtility.DisplayDialog("NST", replayError, "OK"); return; }
        var selectedStrategies = strategies;
        bool v15Details = fullDiagnostics && captureHeuristicDecisionTrace && detailedV15DecisionTrace &&
            (selectedStrategies == StrategySet.HeuristicV15 || selectedStrategies == StrategySet.HeuristicV14AndV15);
        bool v16Details = fullDiagnostics && captureHeuristicDecisionTrace && detailedV16DecisionTrace;
        var flags = (Full: fullDiagnostics, Capture: captureHeuristicDecisionTrace, History: writeActionHistory,
            Decisions: writeDecisionDetails, Final: writeFinalState, Pool: validatePool, Timing: measurePerformance,
            Rerun: strategyRerun, Replay: actionReplay, Scope: replayScope);
        var replayIndices = ResolveReplayIndices(indices);
        string settings = FormattableString.Invariant(
            $"StrategySelection={selectedStrategies}\nRunMode={(specific ? "Specific" : "CustomRange")}\nStartingTrialIndex={indices[0]}\nTrialCountPerStrategy={indices.Count}\nTrialIndices={string.Join(",", indices.Select(i => i.ToString(CultureInfo.InvariantCulture)))}\nFullDiagnostics={flags.Full}\nActionHistoryOutput={flags.Full && flags.History}\nDecisionDetailsOutput={flags.Full && flags.Decisions}\nCaptureActualHeuristicDecisionCandidates={flags.Capture}\nCaptureEnabledBySettings={flags.Full && flags.Capture}\nPerformanceTiming={flags.Full && flags.Timing}\nPoolValidation={flags.Full && flags.Pool}\nFinalStateOutput={flags.Full && flags.Final}\nStrategyRerun={flags.Full && flags.Rerun}\nActionReplay={flags.Full && flags.Replay}\nReplayScope={flags.Scope}\nDetailedPerformanceProfiling=False\nAllocationSampling=False\n");
        if (selectedStrategies == StrategySet.HeuristicV15 || selectedStrategies == StrategySet.HeuristicV14AndV15)
            settings += $"DetailedV15CandidateTrace={v15Details}\n";
        bool hasV16 = selectedStrategies == StrategySet.HeuristicV16 || selectedStrategies == StrategySet.HeuristicV15AndV16;
        if (hasV16) settings += $"DetailedV16CandidateTrace={v16Details}\nV16BattleSummary=True\n";
        running = true;
        Scene scene = default(Scene), previous = SceneManager.GetActiveScene(); bool opened = false;
        var timer = Stopwatch.StartNew();
        try
        {
            scene = SceneManager.GetSceneByPath(ShopScene);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ShopScene) == null) throw new InvalidOperationException("Saved shop.unity is missing.");
                scene = EditorSceneManager.OpenScene(ShopScene, OpenSceneMode.Additive); opened = true;
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            GameSessionConfiguration session = NSTSimulationBatchMenu.BuildSessionConfiguration(scene);
            ShopManager shop = Find<ShopManager>(scene);
            string eventItem = ItemDataMapper.ToDefinition(NSTSimulationBatchMenu.ResolveEventItemData(shop)).DefinitionId;
            string roster = CompositionRosterDataReader.ComputeFingerprint(session.SelectedRoster.Select(x => x.DefinitionId));
            string checkpointId = CompositionRosterDataReader.CreateCheckpointConfigurationId(roster);
            var runnerConfig = new SimulationRunnerConfiguration(session, 100, 10, 100000, flags.Full, checkpointId, eventItem);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SimulationResults",
                specific ? "SpecificTrialDiagnostics" : "CustomTrials", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(output);
            string summaryPath = Path.Combine(output, "batch_execution_summary.txt");
            File.WriteAllText(summaryPath, FormattableString.Invariant($"Status=Running\nBaseSeed={baseSeed}\n") + settings, new UTF8Encoding(false));
            var factories = MakeFactories(session, selectedStrategies);
            string batchId = CompositionRosterDataReader.CreateBatchConfigurationId(roster, eventItem,
                factories.Select(factory => StrategyIdentity(factory, session)));
            var runner = new SimulationRunner(runnerConfig);
            var results = new List<Row>();
            int planned = checked(indices.Count * factories.Count), completed = 0; bool cancelled = false;
            var captures = new List<DecisionTraceCapture>();
            var battleSummary = hasV16 ? new V16BattleSummary() : null;
            var battleTrialRows = hasV16 ? new List<string>() : null;
            foreach (SimulationStrategyFactory factory in factories)
            {
                bool isV16 = factory.StrategyVersion == HeuristicStrategy.Version16;
                var trialConfig = isV16 ? new SimulationRunnerConfiguration(session, 100, 10, 100000,
                    flags.Full, checkpointId + "|" + StrategyIdentity(factory, session), eventItem) : runnerConfig;
                var trialRunner = isV16 ? new SimulationRunner(trialConfig) : runner;
                foreach (long index in indices)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("NST Simulation", factory.StrategyId + " / TrialIndex " + index + " / " + completed + "/" + planned, (float)completed / Math.Max(1, planned)))
                    { cancelled = true; break; }
                    var trialWatch = Stopwatch.StartNew();
                    ulong trialSeed = TrialSeedGenerator.CreateTrialSeed(baseSeed, checked((ulong)index));
                    SimulationTrialResult result;
                    var strategy = factory.Create();
                    var battleTrial = isV16 ? new V16BattleSummary() : null;
                    bool supported = strategy is HeuristicStrategy;
                    var capture = new DecisionTraceCapture(output, factory.StrategyId, factory.StrategyVersion,
                        index, trialSeed, flags.Capture, flags.Full, supported);
                    if (strategy is HeuristicStrategy heuristic)
                    {
                        if (factory.StrategyVersion == HeuristicStrategy.Version15)
                            heuristic.ConfigureV15TraceDetails(v15Details);
                        if (isV16) heuristic.ConfigureV16Diagnostics(v16Details, sample =>
                        {
                            battleTrial.Record(sample);
                            battleSummary.Record(sample);
                        });
                        capture.Register(heuristic.ConfigureDecisionTrace);
                    }
                    result = trialRunner.RunTrial(index, trialSeed, strategy);
                    trialWatch.Stop();
                    capture.Complete(result.DecisionCount);
                    captures.Add(capture);
                    if (isV16) battleTrialRows.AddRange(battleTrial.ToCsv("Trial", index, trialSeed));
                    var row = new Row(result, trialWatch.Elapsed.TotalSeconds); results.Add(row); completed++;
                    row.Original = CompareOriginal(result, batchId, roster);
                    if (flags.Full)
                    {
                        bool selected = specific || replayIndices.Contains(index) ||
                            (flags.Scope == ReplayScope.Abnormal && result.Status == SimulationTrialStatus.Stopped) ||
                            (flags.Scope == ReplayScope.Rejected && result.RejectedActionCount > 0);
                        WriteDetails(output, row, factory, trialConfig, session, batchId, roster, eventItem,
                            selected && flags.Rerun, selected && flags.Replay, flags.History,
                            flags.Decisions, flags.Final, flags.Pool, flags.Timing);
                    }
                }
                if (cancelled) break;
            }
            timer.Stop();
            Export(output, results, planned, completed, indices.Count, cancelled, baseSeed, batchId, roster, eventItem, timer.Elapsed, specific || flags.Full);
            if (hasV16)
                File.WriteAllLines(Path.Combine(output, "v16_battle_summary.csv"),
                    new[] { V16BattleSummary.CsvHeader }.Concat(battleTrialRows).Concat(battleSummary.ToCsv("Batch")),
                    new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(output, "decision_trace_summary.csv"),
                new[] { DecisionTraceCapture.CsvHeader }.Concat(captures.Select(x => x.ToCsv())), new UTF8Encoding(false));
            string traceSummary = DecisionTraceCapture.BatchSummary(captures, out bool traceWarning);
            File.AppendAllText(summaryPath, settings + traceSummary, new UTF8Encoding(false));
            string traceNotice = traceWarning ? "\nCandidate trace incomplete. See decision_trace_summary.csv." : string.Empty;
            if (traceWarning) Debug.LogWarning("NST candidate trace incomplete. See decision_trace_summary.csv: " + output);
            Debug.Log("NST " + (cancelled ? "Cancelled" : "Completed") + " " + completed + "/" + planned + " Trial/Strategy runs: " + output);
            EditorUtility.DisplayDialog("NST", (cancelled ? "Cancelled" : "Completed") + ": " + completed + " of " + planned + " runs.\n" + output + traceNotice, "OK");
        }
        catch (Exception e) { Debug.LogError("NST Trial tool failed.\n" + e); EditorUtility.DisplayDialog("NST", e.Message, "OK"); }
        finally { EditorUtility.ClearProgressBar(); if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); running = false; }
    }


    private void RunPerformanceProfile()
    {
        if (!detailedProfiling) { EditorUtility.DisplayDialog("NST", "Enable detailed performance profiling first.", "OK"); return; }
        if (running) { Debug.LogWarning("NST simulation is already running."); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("NST", "Exit Play Mode first.", "OK"); return; }
        if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong baseSeed) || profileCount < 1 || profileStartIndex < 0 || profileStartIndex > long.MaxValue-profileCount+1L)
        { EditorUtility.DisplayDialog("NST", "Seed or Trial range is invalid.", "OK"); return; }
        running=true; Scene scene=default(Scene), previous=SceneManager.GetActiveScene(); bool opened=false;
        string output=null; var wall=Stopwatch.StartNew();
        try
        {
            scene=SceneManager.GetSceneByPath(ShopScene);
            if(!scene.IsValid()||!scene.isLoaded){if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ShopScene)==null)throw new InvalidOperationException("Saved shop.unity is missing.");scene=EditorSceneManager.OpenScene(ShopScene,OpenSceneMode.Additive);opened=true;if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
            var configurationWatch=Stopwatch.StartNew();GameSessionConfiguration session=NSTSimulationBatchMenu.BuildSessionConfiguration(scene); ShopManager shop=Find<ShopManager>(scene);
            string eventItem=ItemDataMapper.ToDefinition(NSTSimulationBatchMenu.ResolveEventItemData(shop)).DefinitionId;
            string roster=CompositionRosterDataReader.ComputeFingerprint(session.SelectedRoster.Select(x=>x.DefinitionId));
            string checkpointId=CompositionRosterDataReader.CreateCheckpointConfigurationId(roster);
            configurationWatch.Stop();double configurationSeconds=configurationWatch.Elapsed.TotalSeconds;
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","SimulationResults","PerformanceProfiling",DateTime.Now.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(output);
            var chosen=new List<string>(); if(profileStrategies!=ProfileStrategySet.Random)chosen.Add(HeuristicStrategy.StableId);if(profileStrategies!=ProfileStrategySet.Heuristic)chosen.Add(RandomStrategy.StableId);
            string[] plannedIdentities=chosen.Select(id=>id==HeuristicStrategy.StableId
                ?HeuristicStrategy.StableId+"-v"+HeuristicStrategy.StableVersion
                :RandomStrategy.StableId+"-v"+RandomStrategy.StableVersion).ToArray();
            string batchId=CompositionRosterDataReader.CreateBatchConfigurationId(roster,eventItem,plannedIdentities);
            var trials=new List<string>(); var comparisons=new List<string>(); var profileResults=new List<SimulationTrialResult>(); var profileRuns=new List<ProfileRun>(); var sectionTotals=new Dictionary<string,SectionTotal>(StringComparer.Ordinal);var counters=new Dictionary<string,long>(StringComparer.Ordinal);
            int planned=chosen.Count*(2+profileCount*2),done=0;bool cancelled=false;long batchMemStart=GC.GetTotalMemory(false),observedPeakManaged=batchMemStart;int[] gcStart={GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};
            foreach(string sid in chosen)
            {
                // Warm-up is independent of measured TrialIndex values and excluded from all exports.
                for(int w=0;w<2;w++){if(EditorUtility.DisplayCancelableProgressBar("NST Performance Profile",sid+" warm-up "+(w+1)+"/2",(float)done/Math.Max(1,planned))){cancelled=true;break;}ulong ws=TrialSeedGenerator.CreateTrialSeed(baseSeed,checked((ulong)(profileStartIndex+profileCount+w)));var warmup=RunProfileOne(session,ws,profileStartIndex+profileCount+w,sid,checkpointId,eventItem,null);observedPeakManaged=Math.Max(observedPeakManaged,Math.Max(warmup.MemoryStart,warmup.MemoryEnd));done++;}
                if(cancelled)break;
                for(int i=0;i<profileCount;i++)
                {
                    long idx=profileStartIndex+i;
                    if(EditorUtility.DisplayCancelableProgressBar("NST Performance Profile",sid+" paired TrialIndex "+idx,(float)done/Math.Max(1,planned))){cancelled=true;break;}
                    ulong trialSeed=TrialSeedGenerator.CreateTrialSeed(baseSeed,checked((ulong)idx));
                    var collector=new ProjectNST.GameCore.Sessions.SimulationProfileCollector(true,allocationProfiling);
                    ProfileRun baselineRun,profiledRun;
                    // Alternate pair order to reduce systematic warm/cache order bias.
                    if((i&1)==0){baselineRun=RunProfileOne(session,trialSeed,idx,sid,checkpointId,eventItem,null);profiledRun=RunProfileOne(session,trialSeed,idx,sid,checkpointId,eventItem,collector);}
                    else{profiledRun=RunProfileOne(session,trialSeed,idx,sid,checkpointId,eventItem,collector);baselineRun=RunProfileOne(session,trialSeed,idx,sid,checkpointId,eventItem,null);}
                    observedPeakManaged=Math.Max(observedPeakManaged,Math.Max(Math.Max(baselineRun.MemoryStart,baselineRun.MemoryEnd),Math.Max(profiledRun.MemoryStart,profiledRun.MemoryEnd)));
                    trials.Add(TrialCsv(sid,baselineRun));trials.Add(TrialCsv(sid,profiledRun));profileRuns.Add(baselineRun);profileRuns.Add(profiledRun);profileResults.Add(baselineRun.Result);profileResults.Add(profiledRun.Result);
                    foreach(var sec in collector.Sections){string key=sid+"|"+sec.SectionId;if(!sectionTotals.TryGetValue(key,out var total))sectionTotals.Add(key,total=new SectionTotal());total.Add(sec);}
                    foreach(var kv in collector.Counters){string key=sid+"|"+kv.Key;counters.TryGetValue(key,out long old);counters[key]=old+kv.Value;}
                    comparisons.Add(CompareCsv(sid,idx,baselineRun,profiledRun));done+=2;
                }
                if(cancelled)break;
            }
            wall.Stop();int[] gcEnd={GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};long batchMemEnd=GC.GetTotalMemory(false);
            WriteProfileOutputs(output,session,chosen,baseSeed,profileStartIndex,profileCount,roster,batchId,wall.Elapsed,done,planned,cancelled,trials,comparisons,sectionTotals,counters,profileResults,profileRuns,batchMemStart,batchMemEnd,observedPeakManaged,configurationSeconds,gcStart,gcEnd);
            Debug.Log("NST profile "+(cancelled?"Cancelled":"Completed")+" "+done+"/"+planned+" "+output);
            EditorUtility.DisplayDialog("NST",(cancelled?"Cancelled":"Completed")+" performance profiling. Runs completed: "+done+".\n"+output,"OK");
        }
        catch(Exception e){Debug.LogError("NST performance profile failed.\n"+e);EditorUtility.DisplayDialog("NST",e.Message,"OK");}
        finally{EditorUtility.ClearProgressBar();if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);running=false;}
    }
    private sealed class ProfileRun{public SimulationTrialResult Result;public double Seconds;public string Fingerprint;public long? Allocated,CandidateCount,EvaluatedCount;public long[] Gc;public long MemoryStart,MemoryEnd,WorkingSet;public string Mode;}
    private sealed class SectionTotal{public long Calls;public double Inclusive,Exclusive,Max;public string Parent;public void Add(ProjectNST.GameCore.Sessions.SimulationProfileSection s){Parent=s.ParentSectionId;Calls+=s.CallCount;Inclusive+=s.InclusiveSeconds;Exclusive+=s.ExclusiveSeconds;Max=Math.Max(Max,s.MaxMicroseconds/1000000d);}}
    private ProfileRun RunProfileOne(GameSessionConfiguration session,ulong seed,long index,string sid,string checkpoint,string eventItem,ProjectNST.GameCore.Sessions.SimulationProfileCollector collector)
    {
        long? allocBefore=allocationProfiling?ReadAllocatedBytes():null;long memoryStart=GC.GetTotalMemory(false);long[] gc={GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};long working=ReadWorkingSet();var sw=Stopwatch.StartNew();SimulationTrialResult result;using(collector==null?default(ProjectNST.GameCore.Sessions.SimulationProfileCollector.Scope):collector.Measure("Trial.Total")){var config=new SimulationRunnerConfiguration(session,100,10,100000,false,checkpoint,eventItem,collector);var runner=new SimulationRunner(config);IPlayerStrategy strategy;using(collector==null?default(ProjectNST.GameCore.Sessions.SimulationProfileCollector.Scope):collector.Measure("Runner.StrategyInitialization"))strategy=sid==HeuristicStrategy.StableId?NSTSimulationBatchMenu.CreateHeuristicStrategy(session,null,collector):NSTSimulationBatchMenu.CreateRandomStrategy(session,collector);result=runner.RunTrial(index,seed,strategy);}sw.Stop();long? allocAfter=allocationProfiling?ReadAllocatedBytes():null;long memoryEnd=GC.GetTotalMemory(false);var profileCounters=collector==null?null:collector.Counters;long candidates=0,evaluated=0;if(profileCounters!=null){profileCounters.TryGetValue("Candidate.Generated.Total",out candidates);foreach(var pair in profileCounters.Where(x=>x.Key.StartsWith("Candidate.Evaluated.",StringComparison.Ordinal)))evaluated+=pair.Value;}long? allocatedDelta=null;if(allocBefore.HasValue&&allocAfter.HasValue&&allocAfter.Value>allocBefore.Value)allocatedDelta=allocAfter.Value-allocBefore.Value;return new ProfileRun{Result=result,Seconds=sw.Elapsed.TotalSeconds,Fingerprint=result.FinalCheckpoint==null?"Unavailable":SimulationStateFingerprint.Create(result.FinalCheckpoint),Allocated=allocatedDelta,Gc=new[]{GC.CollectionCount(0)-gc[0],GC.CollectionCount(1)-gc[1],GC.CollectionCount(2)-gc[2]},MemoryStart=memoryStart,MemoryEnd=memoryEnd,WorkingSet=working,Mode=collector==null?"Baseline":"Profiled",CandidateCount=collector==null?(long?)null:candidates,EvaluatedCount=collector==null?(long?)null:evaluated};
    }
    private static readonly System.Func<long> AllocatedBytesReader=CreateAllocationReader();
    private static System.Func<long> CreateAllocationReader(){try{var m=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);return m==null?null:(System.Func<long>)Delegate.CreateDelegate(typeof(System.Func<long>),m);}catch{return null;}}
    private static long? ReadAllocatedBytes(){if(AllocatedBytesReader==null)return null;try{long value=AllocatedBytesReader();return value>0?value:(long?)null;}catch{return null;}}
    private static long ReadWorkingSet(){try{using(var process=System.Diagnostics.Process.GetCurrentProcess()){long value=process.WorkingSet64;return value>0?value:-1;}}catch{return -1;}}
    private static string TrialCsv(string id,ProfileRun x){var r=x.Result;return Csv(id)+","+Csv(r.StrategyVersion)+","+Csv(x.Mode)+","+r.TrialIndex+","+r.TrialSeed+","+r.StopReasonId+","+r.StageNumber+","+r.DecisionCount+","+r.SuccessfulActionCount+","+r.RejectedActionCount+","+r.BattleCount+","+x.Seconds.ToString("R",CultureInfo.InvariantCulture)+","+(x.Allocated.HasValue?x.Allocated.Value.ToString(CultureInfo.InvariantCulture):"Unavailable")+","+x.MemoryStart+","+x.MemoryEnd+","+(x.WorkingSet<=0?"Unavailable":x.WorkingSet.ToString(CultureInfo.InvariantCulture))+","+string.Join(";",x.Gc)+","+(x.CandidateCount.HasValue?x.CandidateCount.Value.ToString(CultureInfo.InvariantCulture):"")+","+(x.EvaluatedCount.HasValue?x.EvaluatedCount.Value.ToString(CultureInfo.InvariantCulture):"");}
    private static string CompareCsv(string id,long index,ProfileRun a,ProfileRun b){if(a==null||b==null)return Csv(id)+","+index+",,,,,,,,,";var x=a.Result;var y=b.Result;bool action=x.ActionSequence==y.ActionSequence&&x.DecisionCount==y.DecisionCount&&x.SuccessfulActionCount==y.SuccessfulActionCount&&x.RejectedActionCount==y.RejectedActionCount;return Csv(id)+","+index+","+a.Seconds.ToString("R",CultureInfo.InvariantCulture)+","+b.Seconds.ToString("R",CultureInfo.InvariantCulture)+","+((b.Seconds-a.Seconds)/Math.Max(double.Epsilon,a.Seconds)*100).ToString("R",CultureInfo.InvariantCulture)+",Unavailable,"+action+","+(a.Fingerprint==b.Fingerprint)+","+Equals(x.FinalGameRandomState,y.FinalGameRandomState)+","+Equals(x.FinalStrategyRandomState,y.FinalStrategyRandomState)+","+(x.FinalStrategyState?.Payload==y.FinalStrategyState?.Payload);}
    private void WriteProfileOutputs(string output,GameSessionConfiguration session,List<string> ids,ulong seed,long start,int count,string roster,string batch,TimeSpan elapsed,int done,int planned,bool cancelled,List<string> trials,List<string> comparisons,Dictionary<string,SectionTotal> sections,Dictionary<string,long> counters,List<SimulationTrialResult> measuredResults,List<ProfileRun> profileRuns,long memStart,long memEnd,long memPeak,double configurationSeconds,int[] gcStart,int[] gcEnd)
    {
        File.WriteAllLines(Path.Combine(output,"profiling_trials.csv"),new[]{"StrategyId,StrategyVersion,Mode,TrialIndex,TrialSeed,StopReason,ReachedStage,DecisionCount,SuccessfulActionCount,RejectedActionCount,BattleCount,ElapsedSeconds,AllocatedBytes,ManagedMemoryStartBytes,ManagedMemoryEndBytes,ProcessWorkingSetBytes,GCCollectionCount,CandidateCount,EvaluatedCandidateCount"}.Concat(trials),new UTF8Encoding(false));
        var secLines=new List<string>{"StrategyId,StrategyVersion,SectionId,ParentSectionId,CallCount,InclusiveSeconds,ExclusiveSeconds,AverageMicroseconds,MaxMicroseconds,AllocatedBytes,AverageAllocatedBytes,PercentOfTotal"};foreach(var kv in sections){string[] key=kv.Key.Split(new[]{'|'},2);double strategyTotal=sections.Where(x=>x.Key.StartsWith(key[0]+"|",StringComparison.Ordinal)).Sum(x=>x.Value.Exclusive);secLines.Add(Csv(key[0])+","+Csv(key[0]==HeuristicStrategy.StableId?HeuristicStrategy.StableVersion:RandomStrategy.StableVersion)+","+Csv(key.Length>1?key[1]:kv.Key)+","+Csv(kv.Value.Parent)+","+kv.Value.Calls+","+kv.Value.Inclusive.ToString("R",CultureInfo.InvariantCulture)+","+kv.Value.Exclusive.ToString("R",CultureInfo.InvariantCulture)+","+(kv.Value.Calls==0?0:kv.Value.Exclusive*1000000/kv.Value.Calls).ToString("R",CultureInfo.InvariantCulture)+","+(kv.Value.Max*1000000).ToString("R",CultureInfo.InvariantCulture)+",,,"+(strategyTotal<=0?0:kv.Value.Exclusive*100/strategyTotal).ToString("R",CultureInfo.InvariantCulture));}File.WriteAllLines(Path.Combine(output,"profiling_sections.csv"),secLines,new UTF8Encoding(false));
        var act=new List<string>{"StrategyId,ActionKind,GeneratedCandidateCount,FilteredCandidateCount,EvaluatedCandidateCount,EvaluationSeconds,AverageEvaluationMicroseconds,ExecutedActionCount,ExecutionSeconds"};foreach(string strategyId in ids){var kinds=new HashSet<string>(StringComparer.Ordinal);foreach(string key in counters.Keys.Where(k=>k.StartsWith(strategyId+"|Candidate.Generated.",StringComparison.Ordinal)||k.StartsWith(strategyId+"|Candidate.Evaluated.",StringComparison.Ordinal)||k.StartsWith(strategyId+"|Candidate.Filtered.",StringComparison.Ordinal)||k.StartsWith(strategyId+"|Action.Executed.",StringComparison.Ordinal))){string raw=key.Substring(strategyId.Length+1);if(raw.EndsWith("Total",StringComparison.Ordinal)||raw.EndsWith("Calls",StringComparison.Ordinal))continue;int dot=raw.LastIndexOf('.');if(dot>=0)kinds.Add(raw.Substring(dot+1));}foreach(string kind in kinds.OrderBy(x=>x,StringComparer.Ordinal)){long Get(string suffix){counters.TryGetValue(strategyId+"|"+suffix+kind,out long value);return value;}string sectionKey=strategyId+"|Strategy.Evaluation."+kind;sections.TryGetValue(sectionKey,out var eval);string execKey=strategyId+"|Action.Execute."+kind;sections.TryGetValue(execKey,out var execute);long generated=Get("Candidate.Generated."),filtered=Get("Candidate.Filtered."),evaluated=Get("Candidate.Evaluated."),executed=Get("Action.Executed.");act.Add(Csv(strategyId)+","+Csv(kind)+","+generated+","+filtered+","+evaluated+","+(eval==null?"":eval.Exclusive.ToString("R",CultureInfo.InvariantCulture))+","+(eval==null||eval.Calls==0?"":(eval.Exclusive*1000000/eval.Calls).ToString("R",CultureInfo.InvariantCulture))+","+executed+","+(execute==null?"":execute.Exclusive.ToString("R",CultureInfo.InvariantCulture)));}}File.WriteAllLines(Path.Combine(output,"profiling_action_kinds.csv"),act,new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(output,"profiling_comparison.csv"),new[]{"StrategyId,TrialIndex,BaselineSeconds,ProfiledSeconds,OverheadPercent,ActionHistoryMatch,ActionCountsMatch,FinalStateMatch,GameRngMatch,StrategyRngMatch,StrategyStateMatch"}.Concat(comparisons),new UTF8Encoding(false));
        var summary=new StringBuilder().AppendLine("UnityVersion="+Application.unityVersion).AppendLine("Strategy="+string.Join(",",ids)).AppendLine("StrategyVersions="+string.Join("; ",ids.Select(id=>id==HeuristicStrategy.StableId?"Heuristic v"+HeuristicStrategy.StableVersion:"Random v"+RandomStrategy.StableVersion))).AppendLine("BaseSeed="+seed).AppendLine("TrialIndex="+start+".."+(start+count-1)).AppendLine("ConfiguredMeasuredTrialsPerMode="+count).AppendLine("ExecutedRuns="+done+"/"+planned+" Cancelled="+cancelled).AppendLine("MeasuredCompleted="+measuredResults.Count(x=>x.Status==SimulationTrialStatus.Completed)).AppendLine("MeasuredGameOver="+measuredResults.Count(x=>x.Status==SimulationTrialStatus.GameOver)).AppendLine("MeasuredStoppedOrError="+measuredResults.Count(x=>x.Status==SimulationTrialStatus.Stopped||x.Status==SimulationTrialStatus.Error)).AppendLine("MeasuredRejectedActions="+measuredResults.Sum(x=>x.RejectedActionCount)).AppendLine("RosterFingerprint="+roster).AppendLine("ConfigurationId="+batch).AppendLine("DetailedProfiling=true; AllocationSampling="+allocationProfiling).AppendLine("FullDiagnostics=false; StrategyRerun=false; ActionReplay=false; ActionHistory=false; DecisionDetails=false; ActualCandidateList=false; FinalStateDetails=false").AppendLine("Warmup=2 per Strategy; excluded").AppendLine("Order=warmups, then same-TrialIndex pairs; pair order alternates by Index parity").AppendLine("Timer=Stopwatch.GetTimestamp; frequency="+System.Diagnostics.Stopwatch.Frequency+" ticks/second").AppendLine("ConfigurationConstructionSeconds="+configurationSeconds.ToString("R",CultureInfo.InvariantCulture)).AppendLine("ElapsedSeconds="+elapsed.TotalSeconds.ToString("R",CultureInfo.InvariantCulture)).AppendLine("ManagedMemoryStartBytes="+memStart).AppendLine("ManagedMemoryEndBytes="+memEnd).AppendLine("ObservedPeakManagedMemoryBytes="+memPeak+" (sampled only at Trial boundaries)").AppendLine("GCCollections="+(gcEnd[0]-gcStart[0])+";"+(gcEnd[1]-gcStart[1])+";"+(gcEnd[2]-gcStart[2])).AppendLine("AllocatedBytesAPI="+(AllocatedBytesReader==null?"Unavailable":"GetAllocatedBytesForCurrentThread; usable per-trial deltas="+profileRuns.Count(x=>x.Allocated.HasValue)+"/"+profileRuns.Count+"; non-positive or invalid samples are Unavailable")).AppendLine("ProcessWorkingSet=Process.GetCurrentProcess().WorkingSet64; usable per-trial samples="+profileRuns.Count(x=>x.WorkingSet>0)+"/"+profileRuns.Count+"; non-positive or failed samples are Unavailable. Managed-memory values are GC.GetTotalMemory(false) snapshots, not allocation totals.").AppendLine("Nested section times provide inclusive/exclusive ticks; action-kind evaluation sections are instrumented only for Heuristic.").AppendLine("Internal state-copy/individual Rule validation sections are not separated by current APIs (Unavailable).");File.WriteAllText(Path.Combine(output,"profiling_summary.txt"),summary.ToString(),new UTF8Encoding(false));
    }

    private HashSet<long> ResolveReplayIndices(IReadOnlyList<long> indices)
    {
        var set = new HashSet<long>();
        if (!fullDiagnostics) return set;
        if (replayScope == ReplayScope.All) foreach (long i in indices) set.Add(i);
        if (replayScope == ReplayScope.Explicit && TrialIndexSelectionParser.TryParse(replayText, out var parsed, out _))
            foreach (long i in parsed.Where(indices.Contains)) set.Add(i);
        return set;
    }

    private List<SimulationStrategyFactory> MakeFactories(GameSessionConfiguration session, StrategySet strategies)
    {
        var list = new List<SimulationStrategyFactory>();
        bool includeRandom = strategies == StrategySet.Random || strategies == StrategySet.Both;
        bool includeV7 = strategies == StrategySet.HeuristicV7 || strategies == StrategySet.Both || strategies == StrategySet.HeuristicV7AndV8;
        bool includeV8 = strategies == StrategySet.HeuristicV8 || strategies == StrategySet.HeuristicV7AndV8 || strategies == StrategySet.HeuristicV8AndV9;
        bool includeV9 = strategies == StrategySet.HeuristicV9 || strategies == StrategySet.HeuristicV8AndV9;
        bool includeV10 = strategies == StrategySet.HeuristicV10 || strategies == StrategySet.HeuristicV9AndV10;
        bool includeV11 = strategies == StrategySet.HeuristicV11 || strategies == StrategySet.HeuristicV10AndV11 || strategies == StrategySet.HeuristicV11AndV12;
        bool includeV12 = strategies == StrategySet.HeuristicV12 || strategies == StrategySet.HeuristicV11AndV12 || strategies == StrategySet.HeuristicV12AndV13;
        bool includeV13 = strategies == StrategySet.HeuristicV13 || strategies == StrategySet.HeuristicV12AndV13 || strategies == StrategySet.HeuristicV13AndV14;
        bool includeV14 = strategies == StrategySet.HeuristicV14 || strategies == StrategySet.HeuristicV13AndV14 ||
            strategies == StrategySet.HeuristicV14AndV15;
        bool includeV15 = strategies == StrategySet.HeuristicV15 || strategies == StrategySet.HeuristicV14AndV15 ||
            strategies == StrategySet.HeuristicV15AndV16;
        bool includeV16 = strategies == StrategySet.HeuristicV16 || strategies == StrategySet.HeuristicV15AndV16;
        if (strategies == StrategySet.HeuristicV9AndV10) includeV9 = true;
        if (strategies == StrategySet.HeuristicV10AndV11) includeV10 = true;
        if (includeRandom) list.Add(new SimulationStrategyFactory(RandomStrategy.StableId, RandomStrategy.StableVersion, () => NSTSimulationBatchMenu.CreateRandomStrategy(session)));
        if (includeV7) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.StableVersion, () => NSTSimulationBatchMenu.CreateHeuristicStrategy(session)));
        if (includeV8) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version8, () => NSTSimulationBatchMenu.CreateHeuristicStrategyV8(session)));
        if (includeV9) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version9, () => NSTSimulationBatchMenu.CreateHeuristicStrategyV9(session)));
        if (includeV10) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version10, () => NSTSimulationBatchMenu.CreateHeuristicStrategyV10(session)));
        if (includeV11) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version11, () => NSTSimulationBatchMenu.CreateHeuristicStrategyV11(session)));
        if (includeV12) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version12,
            () => HeuristicStrategy.CreateV12(NSTSimulationBatchMenu.CreateHeuristicStrategyV11Settings(session))));
        if (includeV13) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version13,
            () => HeuristicStrategy.CreateV13(V13Settings(session))));
        if (includeV14) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version14,
            () => HeuristicStrategy.CreateV14(V14Settings(session))));
        if (includeV15) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version15,
            () => HeuristicStrategy.CreateV15(V15Settings(session))));
        if (includeV16) list.Add(new SimulationStrategyFactory(HeuristicStrategy.StableId, HeuristicStrategy.Version16,
            () => HeuristicStrategy.CreateV16(new HeuristicV16Settings(V15Settings(session)))));
        return list;
    }

    private static string StrategyIdentity(SimulationStrategyFactory factory, GameSessionConfiguration session)
    {
        string identity=factory.StrategyId+"-v"+factory.StrategyVersion;
        if(factory.StrategyId==HeuristicStrategy.StableId&&factory.StrategyVersion==HeuristicStrategy.Version9)
            identity+="-"+HeuristicStrategy.GetV9PurchaseConfigurationIdentity(NSTSimulationBatchMenu.CreateHeuristicStrategyV9Settings(session));
        if(factory.StrategyId==HeuristicStrategy.StableId&&factory.StrategyVersion==HeuristicStrategy.Version10)
            identity+="-"+HeuristicStrategy.GetV10PurchaseConfigurationIdentity(NSTSimulationBatchMenu.CreateHeuristicStrategyV10Settings(session));
        if(factory.StrategyId==HeuristicStrategy.StableId&&factory.StrategyVersion==HeuristicStrategy.Version11)
            identity+="-"+HeuristicStrategy.GetV11ConfigurationIdentity(NSTSimulationBatchMenu.CreateHeuristicStrategyV11Settings(session));
        if(factory.StrategyId==HeuristicStrategy.StableId&&factory.StrategyVersion==HeuristicStrategy.Version12)
            identity+="-"+HeuristicStrategy.GetV12ConfigurationIdentity(NSTSimulationBatchMenu.CreateHeuristicStrategyV11Settings(session));
        if(factory.StrategyId==HeuristicStrategy.StableId&&factory.StrategyVersion==HeuristicStrategy.Version13)
            identity+="-"+HeuristicStrategy.GetV13ConfigurationIdentity(V13Settings(session));
        if(factory.StrategyId==HeuristicStrategy.StableId&&factory.StrategyVersion==HeuristicStrategy.Version14)
            identity+="-"+HeuristicStrategy.GetV14ConfigurationIdentity(V14Settings(session));
        if(factory.StrategyId==HeuristicStrategy.StableId&&factory.StrategyVersion==HeuristicStrategy.Version15)
            identity+="-"+HeuristicStrategy.GetV15ConfigurationIdentity(V15Settings(session));
        if(factory.StrategyId==HeuristicStrategy.StableId&&factory.StrategyVersion==HeuristicStrategy.Version16)
            identity+="-"+HeuristicStrategy.GetV16ConfigurationIdentity(new HeuristicV16Settings(V15Settings(session)));
        return identity;
    }

    private static HeuristicV14Settings V14Settings(GameSessionConfiguration session) =>
        new HeuristicV14Settings(selectDHealAmount: session.StageConfig.SelectDHealAmount);

    private static HeuristicV15Settings V15Settings(GameSessionConfiguration session) =>
        new HeuristicV15Settings(preparation: V14Settings(session));

    private static HeuristicStrategySettings V13Settings(GameSessionConfiguration session) =>
        HeuristicStrategy.CreateV13Settings(session.ShopRerollGoldCost,
            session.EconomyConfig.NormalExperiencePurchaseGoldCost, session.EconomyConfig.SelectBExperiencePurchaseGoldCost,
            session.StageConfig.SelectDHealAmount);

    private void WriteDetails(string output, Row row, SimulationStrategyFactory factory, SimulationRunnerConfiguration config,
        GameSessionConfiguration session, string batchId, string roster, string eventItem, bool rerunEnabled, bool replayEnabled,
        bool actionHistoryEnabled, bool decisionsEnabled, bool finalStateEnabled, bool poolValidationEnabled, bool performanceEnabled)
    {
        SimulationTrialResult r = row.Result;
        string rerun = "Not selected", replayTextResult = "Not selected";
        if (rerunEnabled)
        {
            SimulationTrialResult rerunResult = new SimulationRunner(config).RunTrial(r.TrialIndex, r.TrialSeed, factory.Create());
            rerun = Compare(r, rerunResult);
        }
        if (replayEnabled)
        {
            SimulationReplayResult replay = new SimulationReplayVerifier().Verify(config, r, r.ExecutionHistory);
            replayTextResult = replay.Succeeded ? "Match" : replay.MismatchKind + " expected=" + replay.Expected + " actual=" + replay.Actual;
        }
        row.Rerun = rerun; row.Replay = replayTextResult;
        AssignedCharacterCosts costs = CharacterCostAssigner.Assign(session.SelectedRoster, session.CharacterShopConfig, new SimulationRandomStreams(r.TrialSeed).Roster);
        var b = new StringBuilder();
        b.AppendLine("Strategy=" + r.StrategyId + " v" + r.StrategyVersion).AppendLine("BaseSeed=" + seedText + " TrialIndex=" + r.TrialIndex)
            .AppendLine("TrialSeed=" + r.TrialSeed.ToString(CultureInfo.InvariantCulture)).AppendLine("TrialSeedAlgorithm=" + TrialSeedGenerator.AlgorithmId)
            .AppendLine("RosterFingerprint=" + roster).AppendLine("BatchConfigurationId=" + batchId).AppendLine("EventItemDefinitionId=" + eventItem)
            .AppendLine("Stop=" + r.StopReasonId + " Detail=" + r.Detail).AppendLine("Stage=" + r.StageNumber + " Round=" + r.RoundNumber + " Phase=" + r.FinalPhase)
            .AppendLine("HP=" + r.FinalHP + " Gold=" + r.FinalGold + " Level=" + r.FinalLevel + " XP=" + r.FinalXP)
            .AppendLine("Decisions=" + r.DecisionCount + " PhaseDecisions=" + r.PhaseDecisionCount + " Rejected=" + r.RejectedActionCount)
            .AppendLine("FinalStateFingerprint=" + (r.FinalCheckpoint == null ? "Unavailable" : SimulationStateFingerprint.Create(r.FinalCheckpoint)))
            .AppendLine("StrategyRerun=" + rerun).AppendLine("ActionReplay=" + replayTextResult)
            .AppendLine("Candidate scores/decision-time candidates=Unavailable; Strategy was not called a second time for logging.")
            .AppendLine(performanceEnabled ? "Detailed init/strategy/dispatch/battle timings=Unavailable in current Runner API." : "Performance details disabled.")
            .AppendLine("TrialElapsedSeconds=" + (performanceEnabled ? row.Seconds.ToString("R", CultureInfo.InvariantCulture) : "Unavailable"))
            .AppendLine("Roster Cost assignment:");
        foreach (CharacterDefinition d in session.SelectedRoster) b.AppendLine("  " + d.DefinitionId + " cost=" + costs.GetCost(d.DefinitionId));
        if (finalStateEnabled && r.FinalCheckpoint != null) b.AppendLine("Final state: " + SimulationStateFingerprint.Create(r.FinalCheckpoint) + " | " + r.FinalCheckpoint.Stage.Phase +
            " encounter=" + r.FinalCheckpoint.Stage.EncounterIndex + " hp=" + r.FinalCheckpoint.Economy.CurrentHP + " gold=" + r.FinalCheckpoint.Economy.Gold +
            " level=" + r.FinalCheckpoint.Economy.Level + " xp=" + r.FinalCheckpoint.Economy.XP + " board=" + r.FinalCheckpoint.Board.SlotCount + " hand=" + r.FinalCheckpoint.Hand.SlotCount);
        if (finalStateEnabled && r.FinalCheckpoint != null) AppendCapturedStateSummary(b, session, r.FinalCheckpoint);
        row.BoardCount = r.FinalCheckpoint == null ? "Unavailable" : r.FinalCheckpoint.Board.InstanceIds.Count(x => !string.IsNullOrWhiteSpace(x)).ToString(CultureInfo.InvariantCulture);
        row.HandUsed = r.FinalCheckpoint == null ? "Unavailable" : r.FinalCheckpoint.Hand.Slots.Count(x => x != null && !x.IsEmpty).ToString(CultureInfo.InvariantCulture);
        row.StartBattleLegal = ProbeStartBattle(session, r);
        b.AppendLine("Board occupied=" + row.BoardCount + " Hand used=" + row.HandUsed + " StartBattle legal probe=" + row.StartBattleLegal);
        b.AppendLine("Pool validation: " + (poolValidationEnabled ? ValidatePool(session, r.FinalCheckpoint) : "Disabled"));
        b.AppendLine("Original retained Batch comparison=" + row.Original);
        b.AppendLine("Captured execution records (decision-time candidate lists are unavailable through the current Strategy API):");
        foreach (SimulationExecutionRecord record in actionHistoryEnabled || decisionsEnabled ? r.ExecutionHistory : Array.Empty<SimulationExecutionRecord>())
            b.Append("#").Append(record.Sequence).Append(" decision=").Append(record.DecisionStep).Append(" event=").Append(record.Kind)
                .Append(" phase=").Append(record.PhaseBefore).Append(" action=").Append(DescribeAction(record.Action))
                .Append(" status=").Append(record.ActionStatus?.ToString() ?? record.OperationStatus)
                .Append(" rejection=").Append(record.ActionError.HasValue ? record.ActionError.Value.Code + ": " + record.ActionError.Value.Detail : "none")
                .Append(" before=").Append(record.BeforeFingerprint).Append(" after=").Append(record.AfterFingerprint).AppendLine()
                .Append("  before-state: ").AppendLine(record.BeforeStateSummary).Append("  after-state: ").AppendLine(record.AfterStateSummary)
                .Append("  strategy: ").AppendLine(record.StrategyDiagnostic ?? "Unavailable");
        string stem = Safe(r.StrategyId) + "_v" + Safe(r.StrategyVersion) + "_trial_" + r.TrialIndex.ToString(CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(output, stem + "_diagnostics.txt"), b.ToString(), new UTF8Encoding(false));
        if(actionHistoryEnabled) WriteHistory(Path.Combine(output, stem + "_actions.csv"), r.ExecutionHistory, false);
        if(decisionsEnabled) WriteHistory(Path.Combine(output, stem + "_decisions.csv"), r.ExecutionHistory, true);
    }

    private string ValidatePool(GameSessionConfiguration config, ActionExecutionState state)
    { if (state == null) return "Unavailable"; try { GameSession.Restore(config, state); return "Valid"; } catch (Exception e) { return "Invalid: " + e.GetType().Name + ": " + e.Message; } }

    private static string ProbeStartBattle(GameSessionConfiguration config, SimulationTrialResult result)
    {
        if (result.FinalCheckpoint == null || result.FinalPhase != ProjectNST.GameCore.Stages.StageProgressPhase.Preparation) return "false (not Preparation)";
        try
        {
            GameSession probe=GameSession.Restore(config,result.FinalCheckpoint);
            var action=new StartBattleAction(new ActionId("diagnostic-start-battle-probe:"+result.TrialIndex.ToString(CultureInfo.InvariantCulture)));
            ActionDispatchResult attempt=probe.ExecuteAction(action);
            return attempt.Succeeded ? "true" : "false ("+attempt.Error.Code+": "+attempt.Error.Detail+")";
        }
        catch(Exception e){return "Unavailable ("+e.GetType().Name+": "+e.Message+")";}
    }

    private static void AppendCapturedStateSummary(StringBuilder b, GameSessionConfiguration config, ActionExecutionState state)
    {
        var definitions=config.CharacterDefinitions.ToDictionary(x=>x.DefinitionId,StringComparer.Ordinal);
        b.AppendLine("Board slots:");
        for(int i=0;i<state.Board.SlotCount;i++)
        {
            string id=state.Board.InstanceIds[i];
            if(string.IsNullOrWhiteSpace(id)){b.AppendLine("  "+i+": Empty");continue;}
            if(!state.Ownership.TryGet(id,out CharacterInstanceView character)){b.AppendLine("  "+i+": instance="+id+" (unresolved)");continue;}
            definitions.TryGetValue(character.DefinitionId,out CharacterDefinition definition);
            b.Append("  ").Append(i).Append(": instance=").Append(id).Append(" definition=").Append(character.DefinitionId)
                .Append(" name=").Append(definition?.CharacterName??"Unavailable").Append(" cost=").Append(character.RunCost).Append(" star=").Append(character.Star)
                .Append(" ATK=").Append(character.Attack).Append(" DEF=").Append(character.Defense).Append(" poolCopies=").Append(character.PoolCopies)
                .Append(" units=").Append(definition==null?"Unavailable":string.Join("|",definition.Units)).Append(" attributes=").AppendLine(definition==null?"Unavailable":string.Join("|",definition.Attributes));
        }
        b.AppendLine("Hand slots:");
        for(int i=0;i<state.Hand.SlotCount;i++)
        {
            HandSlotContent slot=state.Hand.Slots[i];
            if(slot.IsEmpty)b.AppendLine("  "+i+": Empty");
            else if(slot.Kind==HandSlotContentKind.Item)b.AppendLine("  "+i+": Item definition="+slot.ItemDefinitionId);
            else if(state.Ownership.TryGet(slot.CharacterInstanceId,out CharacterInstanceView character))
            {
                definitions.TryGetValue(character.DefinitionId,out CharacterDefinition definition);
                b.Append("  ").Append(i).Append(": Character instance=").Append(character.InstanceId).Append(" definition=").Append(character.DefinitionId)
                    .Append(" name=").Append(definition?.CharacterName??"Unavailable").Append(" cost=").Append(character.RunCost).Append(" star=").Append(character.Star)
                    .Append(" ATK=").Append(character.Attack).Append(" DEF=").Append(character.Defense).Append(" poolCopies=").Append(character.PoolCopies)
                    .Append(" units=").Append(definition==null?"Unavailable":string.Join("|",definition.Units)).Append(" attributes=").AppendLine(definition==null?"Unavailable":string.Join("|",definition.Attributes));
            }
            else b.AppendLine("  "+i+": Character instance="+slot.CharacterInstanceId+" (unresolved)");
        }
        b.AppendLine("Shop offers:");
        foreach(ShopOffer offer in state.Shop.Offers)
            b.AppendLine("  "+offer.OfferId+" kind="+offer.Kind+" definition="+offer.DefinitionId+" price="+offer.DisplayPrice+" characterCost="+(offer.CharacterCost?.ToString(CultureInfo.InvariantCulture)??"n/a")+" status="+offer.Status);
    }

    private static string Compare(SimulationTrialResult a, SimulationTrialResult b)
    {
        if (a.TrialIndex != b.TrialIndex || a.TrialSeed != b.TrialSeed || a.StrategyId != b.StrategyId || a.StrategyVersion != b.StrategyVersion) return "Mismatch identity";
        if (a.StopReason != b.StopReason || a.Status != b.Status || a.DecisionCount != b.DecisionCount || a.ActionSequence != b.ActionSequence) return "Mismatch outcome/counters";
        if (a.FinalCheckpoint == null || b.FinalCheckpoint == null) return "Unavailable checkpoint";
        if (SimulationStateFingerprint.Create(a.FinalCheckpoint) != SimulationStateFingerprint.Create(b.FinalCheckpoint)) return "Mismatch final state";
        if (!Equals(a.FinalGameRandomState, b.FinalGameRandomState) || !Equals(a.FinalStrategyRandomState, b.FinalStrategyRandomState)) return "Mismatch RNG";
        if (a.FinalStrategyState?.Payload != b.FinalStrategyState?.Payload) return "Mismatch strategy state";
        return "Match";
    }

    private static void WriteHistory(string path, IReadOnlyList<SimulationExecutionRecord> records, bool decisions)
    {
        var b = new StringBuilder("Sequence,DecisionStep,Kind,Phase,ActionKind,ActionId,ActionParameters,Status,Reason,BeforeFingerprint,AfterFingerprint,Diagnostic\n");
        foreach (var x in records.Where(x => !decisions || x.Kind == SimulationExecutionEventKind.Action || x.Kind == SimulationExecutionEventKind.StrategyNoAction))
            b.Append(Csv(x.Sequence)).Append(',').Append(Csv(x.DecisionStep)).Append(',').Append(Csv(x.Kind)).Append(',').Append(Csv(x.PhaseBefore)).Append(',')
                .Append(Csv(x.Action?.Kind.ToString() ?? "NoAction/Unavailable")).Append(',').Append(Csv(x.Action?.ActionId.Value ?? "")).Append(',').Append(Csv(DescribeAction(x.Action)))
                .Append(',').Append(Csv(x.ActionStatus?.ToString() ?? x.OperationStatus)).Append(',')
                .Append(Csv(x.ActionError.HasValue ? x.ActionError.Value.Code + ":" + x.ActionError.Value.Detail : ""))
                .Append(',').Append(Csv(x.BeforeFingerprint)).Append(',').Append(Csv(x.AfterFingerprint)).Append(',').Append(Csv(x.StrategyDiagnostic)).Append('\n');
        File.WriteAllText(path, b.ToString(), new UTF8Encoding(false));
    }

    private static void Export(string output, List<Row> rows, int planned, int executed, int trialsPerStrategy, bool cancelled, ulong seed,
        string batchId, string roster, string eventItem, TimeSpan elapsed, bool specific)
    {
        var trials = new StringBuilder("TrialNumber,TrialSeed,StrategyId,StrategyVersion,StopReason,IsResolved,IsCompleted,FinalStage,FinalRound,FinalHP,FinalGold,FinalLevel,FinalXP,DecisionCount,SuccessfulActionCount,RejectedActionCount,BattleCount\n");
        foreach (var x in rows) { var r=x.Result; bool resolved=r.Status==SimulationTrialStatus.Completed||r.Status==SimulationTrialStatus.GameOver;
            trials.Append(Csv(r.TrialIndex)).Append(',').Append(Csv(r.TrialSeed)).Append(',').Append(Csv(r.StrategyId)).Append(',').Append(Csv(r.StrategyVersion)).Append(',').Append(Csv(r.StopReasonId)).Append(',').Append(Csv(resolved)).Append(',').Append(Csv(r.Status==SimulationTrialStatus.Completed)).Append(',').Append(Csv(r.StageNumber)).Append(',').Append(Csv(r.RoundNumber)).Append(',').Append(Csv(r.FinalHP)).Append(',').Append(Csv(r.FinalGold)).Append(',').Append(Csv(r.FinalLevel)).Append(',').Append(Csv(r.FinalXP)).Append(',').Append(Csv(r.DecisionCount)).Append(',').Append(Csv(r.SuccessfulActionCount)).Append(',').Append(Csv(r.RejectedActionCount)).Append(',').Append(Csv(r.BattleCount)).Append('\n'); }
        File.WriteAllText(Path.Combine(output,"trials.csv"),trials.ToString(),new UTF8Encoding(false));
        var stops=new StringBuilder("StrategyId,StrategyVersion,StopReason,Count,PercentageOfExecutedTrials\n");
        foreach(var g in rows.GroupBy(x=>new{x.Result.StrategyId,x.Result.StrategyVersion,x.Result.StopReasonId})) stops.Append(Csv(g.Key.StrategyId)).Append(',').Append(Csv(g.Key.StrategyVersion)).Append(',').Append(Csv(g.Key.StopReasonId)).Append(',').Append(Csv(g.Count())).Append(',').Append(Csv(rows.Count==0?0:(double)g.Count()/rows.Count)).Append('\n');
        File.WriteAllText(Path.Combine(output,"stop_reasons.csv"),stops.ToString(),new UTF8Encoding(false));
        var summary=new StringBuilder("StrategyId,StrategyVersion,Planned,Executed,Completed,GameOver,Stopped,WinRate,ResolvedRate,AverageStage,AverageDecisionCount,AverageBattleCount\n");
        foreach(var g in rows.GroupBy(x=>new{x.Result.StrategyId,x.Result.StrategyVersion})){int win=g.Count(x=>x.Result.Status==SimulationTrialStatus.Completed),loss=g.Count(x=>x.Result.Status==SimulationTrialStatus.GameOver),resolved=win+loss;summary.Append(Csv(g.Key.StrategyId)).Append(',').Append(Csv(g.Key.StrategyVersion)).Append(',').Append(Csv(trialsPerStrategy)).Append(',').Append(Csv(g.Count())).Append(',').Append(Csv(win)).Append(',').Append(Csv(loss)).Append(',').Append(Csv(g.Count()-resolved)).Append(',').Append(Csv(resolved==0?"Undefined":((double)win/resolved).ToString("R",CultureInfo.InvariantCulture))).Append(',').Append(Csv(g.Count()==0?0:(double)resolved/g.Count())).Append(',').Append(Csv(g.Average(x=>(double)x.Result.StageNumber))).Append(',').Append(Csv(g.Average(x=>(double)x.Result.DecisionCount))).Append(',').Append(Csv(g.Average(x=>(double)x.Result.BattleCount))).Append('\n');}
        File.WriteAllText(Path.Combine(output,"strategy_summary.csv"),summary.ToString(),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output,"strategy_comparison.csv"),BuildComparison(rows),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output,"performance_summary.csv"),"PlannedRuns,ExecutedRuns,ElapsedSeconds,AverageTrialSeconds,WorkingSet\n"+Csv(planned)+","+Csv(executed)+","+Csv(elapsed.TotalSeconds)+","+Csv(rows.Count==0?0:rows.Average(x=>x.Seconds))+",Unavailable\n",new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output,"batch_execution_summary.txt"),"Status="+(cancelled?"Cancelled":"Completed")+"\nPlanned="+planned+"\nExecuted="+executed+"\nBaseSeed="+seed+"\nTrialSeedAlgorithm="+TrialSeedGenerator.AlgorithmId+"\nBatchConfigurationId="+batchId+"\nRosterFingerprint="+roster+"\nEventItemDefinitionId="+eventItem+"\nElapsed="+elapsed+"\nReplay matched="+rows.Count(x=>x.Replay=="Match")+"\nReplay unchecked="+rows.Count(x=>x.Replay=="Not selected")+"\n");
        if(specific)
        {
            var d=new StringBuilder("StrategyId,StrategyVersion,TrialIndex,TrialSeed,Stage,Round,Phase,StopReason,Decisions,PhaseDecisions,Rejected,StartBattleLegal,StartBattleCandidate,BoardCount,Level,HandUsed,CandidateCount,MaxCandidateScore,StrategyRerun,ActionReplay,OriginalBatchMatch,DiagnosticsFile\n");
            foreach(var x in rows)d.Append(Csv(x.Result.StrategyId)).Append(',').Append(Csv(x.Result.StrategyVersion)).Append(',').Append(Csv(x.Result.TrialIndex)).Append(',').Append(Csv(x.Result.TrialSeed)).Append(',').Append(Csv(x.Result.StageNumber)).Append(',').Append(Csv(x.Result.RoundNumber)).Append(',').Append(Csv(x.Result.FinalPhase)).Append(',').Append(Csv(x.Result.StopReasonId)).Append(',').Append(Csv(x.Result.DecisionCount)).Append(',').Append(Csv(x.Result.PhaseDecisionCount)).Append(',').Append(Csv(x.Result.RejectedActionCount)).Append(',').Append(Csv(x.StartBattleLegal)).Append(",Unavailable,").Append(Csv(x.BoardCount)).Append(',').Append(Csv(x.Result.FinalLevel)).Append(',').Append(Csv(x.HandUsed)).Append(",Unavailable,Unavailable,").Append(Csv(x.Rerun)).Append(',').Append(Csv(x.Replay)).Append(',').Append(Csv(x.Original)).Append(',').Append(Csv(x.DetailFile)).Append('\n');
            File.WriteAllText(Path.Combine(output,"diagnosis_summary.csv"),d.ToString(),new UTF8Encoding(false));
            var replay=new StringBuilder("StrategyId,StrategyVersion,TrialIndex,StrategyRerun,ActionReplay,OriginalBatchMatch\n");
            foreach(var x in rows) replay.Append(Csv(x.Result.StrategyId)).Append(',').Append(Csv(x.Result.StrategyVersion)).Append(',').Append(Csv(x.Result.TrialIndex)).Append(',').Append(Csv(x.Rerun)).Append(',').Append(Csv(x.Replay)).Append(',').Append(Csv(x.Original)).Append('\n');
            File.WriteAllText(Path.Combine(output,"replay_summary.csv"),replay.ToString(),new UTF8Encoding(false));
            var rejected=rows.SelectMany(x=>x.Result.ExecutionHistory.Where(e=>e.ActionStatus==ActionDispatchStatus.Rejected).Select(e=>new{Row=x,Entry=e})).ToArray();
            var rd=new StringBuilder("StrategyId,StrategyVersion,TrialIndex,Sequence,DecisionStep,ActionKind,ActionId,ActionParameters,ReasonCode,ReasonMessage,BeforeFingerprint,AfterFingerprint\n");
            foreach(var x in rejected) rd.Append(Csv(x.Row.Result.StrategyId)).Append(',').Append(Csv(x.Row.Result.StrategyVersion)).Append(',').Append(Csv(x.Row.Result.TrialIndex)).Append(',').Append(Csv(x.Entry.Sequence)).Append(',').Append(Csv(x.Entry.DecisionStep)).Append(',').Append(Csv(x.Entry.Action?.Kind.ToString())).Append(',').Append(Csv(x.Entry.Action?.ActionId.Value)).Append(',').Append(Csv(DescribeAction(x.Entry.Action))).Append(',').Append(Csv(x.Entry.ActionError.HasValue?x.Entry.ActionError.Value.Code.ToString():"Unavailable")).Append(',').Append(Csv(x.Entry.ActionError.HasValue?x.Entry.ActionError.Value.Detail:"Unavailable")).Append(',').Append(Csv(x.Entry.BeforeFingerprint)).Append(',').Append(Csv(x.Entry.AfterFingerprint)).Append('\n');
            File.WriteAllText(Path.Combine(output,"rejection_details.csv"),rd.ToString(),new UTF8Encoding(false));
            var rs=new StringBuilder("StrategyId,StrategyVersion,ActionKind,ReasonCode,Count\n");
            foreach(var g in rejected.GroupBy(x=>new{x.Row.Result.StrategyId,x.Row.Result.StrategyVersion,Kind=x.Entry.Action?.Kind.ToString(),Code=x.Entry.ActionError.HasValue?x.Entry.ActionError.Value.Code.ToString():"Unavailable"})) rs.Append(Csv(g.Key.StrategyId)).Append(',').Append(Csv(g.Key.StrategyVersion)).Append(',').Append(Csv(g.Key.Kind)).Append(',').Append(Csv(g.Key.Code)).Append(',').Append(Csv(g.Count())).Append('\n');
            File.WriteAllText(Path.Combine(output,"rejection_summary.csv"),rs.ToString(),new UTF8Encoding(false));
        }
    }

    private static string BuildComparison(List<Row> rows)
    {
        const string header="StrategyAId,StrategyAVersion,StrategyBId,StrategyBVersion,WinRateDifferencePercentagePoints,ResolvedRateDifferencePercentagePoints,StoppedRateDifferencePercentagePoints,AverageStageDifference,CommonSeedCount,CommonSeedStrategyAWinRate,CommonSeedStrategyBWinRate,CommonSeedWinRateDifferencePercentagePoints,OnlyACompletedCount,OnlyBCompletedCount,BothCompletedCount,BothGameOverCount\n";
        var groups=rows.GroupBy(x=>new{x.Result.StrategyId,x.Result.StrategyVersion}).OrderBy(x=>x.Key.StrategyId,StringComparer.Ordinal).ThenBy(x=>x.Key.StrategyVersion,StringComparer.Ordinal).ToArray();
        if(groups.Length<2)return header;
        var a=groups[0].ToArray();var b=groups[1].ToArray();
        int aResolved=a.Count(x=>x.Result.Status==SimulationTrialStatus.Completed||x.Result.Status==SimulationTrialStatus.GameOver),bResolved=b.Count(x=>x.Result.Status==SimulationTrialStatus.Completed||x.Result.Status==SimulationTrialStatus.GameOver);
        int aWins=a.Count(x=>x.Result.Status==SimulationTrialStatus.Completed),bWins=b.Count(x=>x.Result.Status==SimulationTrialStatus.Completed);
        double? aWin=aResolved==0?(double?)null:(double)aWins/aResolved,bWin=bResolved==0?(double?)null:(double)bWins/bResolved;
        double? aResolvedRate=a.Length==0?(double?)null:(double)aResolved/a.Length,bResolvedRate=b.Length==0?(double?)null:(double)bResolved/b.Length;
        double? aStopped=a.Length==0?(double?)null:(double)(a.Length-aResolved)/a.Length,bStopped=b.Length==0?(double?)null:(double)(b.Length-bResolved)/b.Length;
        var bByIndex=b.ToDictionary(x=>x.Result.TrialIndex);int common=0,onlyA=0,onlyB=0,bothWin=0,bothLoss=0;double commonAWins=0,commonBWins=0;
        foreach(Row left in a){if(!bByIndex.TryGetValue(left.Result.TrialIndex,out Row right)||left.Result.TrialSeed!=right.Result.TrialSeed||!Resolved(left.Result)||!Resolved(right.Result))continue;common++;bool aw=left.Result.Status==SimulationTrialStatus.Completed,bw=right.Result.Status==SimulationTrialStatus.Completed;if(aw)commonAWins++;if(bw)commonBWins++;if(aw&&bw)bothWin++;else if(!aw&&!bw)bothLoss++;else if(aw)onlyA++;else onlyB++;}
        double? caw=common==0?(double?)null:commonAWins/common,cbw=common==0?(double?)null:commonBWins/common;
        var line=new StringBuilder();line.Append(Csv(groups[0].Key.StrategyId)).Append(',').Append(Csv(groups[0].Key.StrategyVersion)).Append(',').Append(Csv(groups[1].Key.StrategyId)).Append(',').Append(Csv(groups[1].Key.StrategyVersion)).Append(',')
            .Append(Csv(Diff(aWin,bWin,true))).Append(',').Append(Csv(Diff(aResolvedRate,bResolvedRate,true))).Append(',').Append(Csv(Diff(aStopped,bStopped,true))).Append(',')
            .Append(Csv(a.Length==0||b.Length==0?(double?)null:a.Average(x=>(double)x.Result.StageNumber)-b.Average(x=>(double)x.Result.StageNumber))).Append(',').Append(Csv(common)).Append(',').Append(Csv(caw)).Append(',').Append(Csv(cbw)).Append(',').Append(Csv(Diff(caw,cbw,true))).Append(',')
            .Append(Csv(onlyA)).Append(',').Append(Csv(onlyB)).Append(',').Append(Csv(bothWin)).Append(',').Append(Csv(bothLoss)).Append('\n');
        return header+line;
    }
    private static bool Resolved(SimulationTrialResult r)=>r.Status==SimulationTrialStatus.Completed||r.Status==SimulationTrialStatus.GameOver;
    private static double? Diff(double? left,double? right,bool percentage){return !left.HasValue||!right.HasValue?(double?)null:(left.Value-right.Value)*(percentage?100.0:1.0);}

    private string CompareOriginal(SimulationTrialResult result, string batchId, string roster)
    {
        if (string.IsNullOrWhiteSpace(originalCsv) && string.IsNullOrWhiteSpace(originalSummary)) return "Not provided";
        var outcomes = new List<string>();
        if (!string.IsNullOrWhiteSpace(originalCsv) && !File.Exists(originalCsv)) outcomes.Add("CSV unavailable: file missing");
        else if (!string.IsNullOrWhiteSpace(originalCsv))
        try
        {
            string[] lines=File.ReadAllLines(originalCsv); if(lines.Length<2)return "Unavailable: source CSV has no data";
            List<string> headers=ParseCsv(lines[0]); string[] keys={"TrialNumber","TrialSeed","StrategyId","StrategyVersion","StopReason"};
            int[] ix=keys.Select(k=>headers.IndexOf(k)).ToArray(); if(ix.Any(i=>i<0))return "Unavailable: required columns missing";
            foreach(string line in lines.Skip(1))
            {
                List<string> f=ParseCsv(line); if(f.Count<=ix.Max())continue;
                if(f[ix[0]]!=result.TrialIndex.ToString(CultureInfo.InvariantCulture)||f[ix[2]]!=result.StrategyId||f[ix[3]]!=result.StrategyVersion)continue;
                outcomes.Add(f[ix[1]]==result.TrialSeed.ToString(CultureInfo.InvariantCulture)&&f[ix[4]]==result.StopReasonId?"CSV match (seed/stop)":"CSV mismatch (seed/stop)");
                break;
            }
            if (!outcomes.Any(x => x.StartsWith("CSV ", StringComparison.Ordinal))) outcomes.Add("CSV unavailable: Trial row not found");
        }
        catch(Exception e){outcomes.Add("CSV unavailable: "+e.GetType().Name+": "+e.Message);}
        if (!string.IsNullOrWhiteSpace(originalSummary))
        {
            if(!File.Exists(originalSummary)) outcomes.Add("Summary unavailable: file missing");
            else
            {
                string summary=File.ReadAllText(originalSummary);
                string recordedBatch=ReadSummaryValue(summary,"BatchConfigurationId");
                string recordedRoster=ReadSummaryValue(summary,"RosterFingerprint");
                if(recordedBatch==null||recordedRoster==null) outcomes.Add("Summary unavailable: identity fields missing");
                else outcomes.Add(recordedBatch==batchId&&recordedRoster==roster?"Summary identity match":"Summary identity mismatch");
            }
        }
        return string.Join("; ",outcomes);
    }
    private static string ReadSummaryValue(string text,string key){foreach(string line in text.Split('\n')){string prefix=key+"=";if(line.StartsWith(prefix,StringComparison.Ordinal))return line.Substring(prefix.Length).Trim();}return null;}
    private static List<string> ParseCsv(string line)
    {
        var fields=new List<string>();var value=new StringBuilder();bool quoted=false;
        for(int i=0;i<line.Length;i++){char c=line[i];if(quoted){if(c=='"'&&i+1<line.Length&&line[i+1]=='"'){value.Append('"');i++;}else if(c=='"')quoted=false;else value.Append(c);}else if(c=='"')quoted=true;else if(c==','){fields.Add(value.ToString());value.Clear();}else value.Append(c);}fields.Add(value.ToString());return fields;
    }
    private static string DescribeAction(GameAction a)
    {
        if(a==null)return "NoAction";
        switch(a){case BuyCharacterAction x:return "BuyCharacter(offer="+x.OfferId+",placement="+x.Placement.Kind+":"+x.Placement.SlotIndex+")";
            case BuyItemAction x:return "BuyItem(offer="+x.OfferId+",hand="+x.HandSlot+")";
            case MoveOrSwapBoardAction x:return "MoveOrSwapBoard("+x.SourceSlot+"->"+x.TargetSlot+")";
            case MoveOrSwapHandAction x:return "MoveOrSwapHand("+x.SourceSlot+"->"+x.TargetSlot+")";
            case MoveBoardToHandAction x:return "MoveBoardToHand("+x.BoardSlot+"->"+x.HandSlot+")";
            case MoveHandToBoardAction x:return "MoveHandToBoard("+x.HandSlot+"->"+x.BoardSlot+")";
            case SellCharacterAction x:return "SellCharacter("+x.InstanceId+")";
            case SellItemAction x:return "SellItem(hand="+x.HandSlot+")";
            case UseEventItemAction x:return "UseEventItem("+x.Target.Kind+":"+x.Target.SlotIndex+")";
            case UseHandItemAction x:return "UseHandItem(hand="+x.HandSlot+",target="+x.Target.Kind+":"+x.Target.SlotIndex+")";
            case SelectStageChoiceAction x:return "SelectStageChoice("+x.Choice+")";
            case RerollShopAction x:return "RerollShop("+x.ShopKind+")";default:return a.Kind.ToString();}
    }

    private static T Find<T>(Scene s) where T:Component { foreach(var root in s.GetRootGameObjects()){var x=root.GetComponentInChildren<T>(true);if(x!=null)return x;}return null; }
    private static string Csv(object value){string s=Convert.ToString(value,CultureInfo.InvariantCulture)??"";return "\""+s.Replace("\"","\"\"")+"\"";}
    private static string Safe(string s){foreach(char c in Path.GetInvalidFileNameChars())s=s.Replace(c,'_');return s.Replace('/','_').Replace('\\','_');}
    private sealed class Row { public SimulationTrialResult Result; public double Seconds; public string Rerun="Not selected",Replay="Not selected",Original="Unavailable",DetailFile,BoardCount="Unavailable",HandUsed="Unavailable",StartBattleLegal="Unavailable"; public Row(SimulationTrialResult r,double s){Result=r;Seconds=s;DetailFile=Safe(r.StrategyId)+"_v"+Safe(r.StrategyVersion)+"_trial_"+r.TrialIndex+"_diagnostics.txt";} }
}
