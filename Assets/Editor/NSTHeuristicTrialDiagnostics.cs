using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor-only, read-only diagnostic for the first encounter decisions of Trial 0.</summary>
public static class NSTHeuristicTrialDiagnostics
{
    private const ulong BaseSeed = 12345UL;
    private const ulong TrialIndex = 0UL;
    private const int MaxDecisionSteps = 100;
    private const string ShopScenePath = "Assets/Scenes/shop.unity";
    private static bool _isRunning;

    [MenuItem("Tools/NST/Diagnose Heuristic Trial")]
    private static void Diagnose()
    {
        if (_isRunning) { Debug.LogWarning("NST heuristic diagnostic is already running."); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("NST diagnostic requires Edit Mode.");
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("NST Diagnostic", "Play Modeを終了してから実行してください。", "OK");
            return;
        }

        _isRunning = true;
        Scene loaded = default(Scene);
        Scene activeBefore = SceneManager.GetActiveScene();
        bool opened = false;
        try
        {
            loaded = SceneManager.GetSceneByPath(ShopScenePath);
            if (!loaded.IsValid() || !loaded.isLoaded)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ShopScenePath) == null)
                    throw new InvalidOperationException("Saved shop.unity was not found: " + ShopScenePath);
                loaded = EditorSceneManager.OpenScene(ShopScenePath, OpenSceneMode.Additive);
                opened = true;
                if (activeBefore.IsValid() && activeBefore.isLoaded) SceneManager.SetActiveScene(activeBefore);
            }

            GameSessionConfiguration config = NSTSimulationBatchMenu.BuildSessionConfiguration(loaded);
            ulong trialSeed = TrialSeedGenerator.CreateTrialSeed(BaseSeed, TrialIndex);
            var runs = new List<Tuple<string, TraceRun, TraceRun>>();
            foreach (bool heuristic in new[] { true, false })
            {
                // Repeat the same harness with rendering disabled; strategy and state transitions are identical.
                TraceRun unlogged = RunOne(config, trialSeed, heuristic, false);
                TraceRun logged = RunOne(config, trialSeed, heuristic, true);
                runs.Add(Tuple.Create(heuristic ? "HeuristicStrategy" : "RandomStrategy", unlogged, logged));
            }
            TraceRun heuristicRun = runs[0].Item3;
            TraceRun randomRun = runs[1].Item3;
            string comparison = "\n[Strategy comparison on the same Trial Seed]\n" +
                "TrialSeed=" + trialSeed + "\n" +
                "Initial game state identical=" + (heuristicRun.InitialFingerprint == randomRun.InitialFingerprint) + "\n" +
                "Initial Observation identical=" + (heuristicRun.InitialObservationFingerprint == randomRun.InitialObservationFingerprint) + "\n" +
                "Initial candidate signatures identical=" + (heuristicRun.InitialCandidateSet == randomRun.InitialCandidateSet) + "\n" +
                "Heuristic first action=" + heuristicRun.FirstAction + "\n" +
                "Random first action=" + randomRun.FirstAction + "\n" +
                "Battle-start formation/Board fingerprint identical=" + (heuristicRun.BattleStartFingerprint == randomRun.BattleStartFingerprint) + "\n" +
                "Heuristic battle result=" + heuristicRun.BattleOutcome + "\n" +
                "Random battle result=" + randomRun.BattleOutcome + "\n" +
                "Heuristic Gold at battle start=" + heuristicRun.BattleGold + "\n" +
                "Random Gold at battle start=" + randomRun.BattleGold;
            var outputs = new List<string>();
            foreach (Tuple<string, TraceRun, TraceRun> item in runs)
            {
                TraceRun unlogged = item.Item2, logged = item.Item3;
                bool unchanged = string.Equals(unlogged.CanonicalTrace, logged.CanonicalTrace, StringComparison.Ordinal);
                logged.Log.AppendLine("\n[Instrumentation parity]");
                logged.Log.AppendLine("Unlogged and logged runs identical: " + unchanged);
                logged.Log.AppendLine("Canonical trace SHA-256: " + SimulationStateFingerprint.Create(logged.CanonicalTrace));
                logged.Log.AppendLine("Initial game-state fingerprint: " + logged.InitialFingerprint);
                logged.Log.AppendLine("Final game-state fingerprint: " + logged.FinalFingerprint);
                logged.Log.AppendLine("Result: " + logged.TerminalSummary);
                logged.Log.AppendLine(comparison);
                string text = logged.Log.ToString();
                string file = item.Item1 == "HeuristicStrategy" ? "heuristic_trial_0.txt" : "random_trial_0.txt";
                outputs.Add(WriteDiagnostic(file, text));
                Debug.Log(text);
                if (!unchanged) Debug.LogError(item.Item1 + " diagnostic instrumentation changed the observed trace.");
            }

            Debug.Log("NST Trial 0 diagnostic files: " + string.Join(" | ", outputs));
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("NST Diagnostic", "Heuristic / Random の初回戦闘診断を保存しました。\n" + string.Join("\n", outputs), "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError("NST Heuristic Trial diagnostic failed. No dummy configuration was substituted.\n" + ex);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("NST Diagnostic", "診断を完了できませんでした。Consoleを確認してください。\n" + ex.Message, "OK");
        }
        finally
        {
            if (opened && loaded.IsValid() && loaded.isLoaded) EditorSceneManager.CloseScene(loaded, true);
            if (activeBefore.IsValid() && activeBefore.isLoaded) SceneManager.SetActiveScene(activeBefore);
            _isRunning = false;
        }
    }

    /// <summary>Command-line entry point used to execute this same Editor diagnostic in batch mode.</summary>
    public static void RunBatchModeDiagnostic() => Diagnose();

    private static TraceRun RunOne(GameSessionConfiguration config, ulong seed, bool heuristic, bool renderLog)
    {
        IPlayerStrategy strategy = heuristic
            ? (IPlayerStrategy)NSTSimulationBatchMenu.CreateHeuristicStrategy(config)
            : NSTSimulationBatchMenu.CreateRandomStrategy(config);
        var session = GameSession.Create(config, seed);
        StrategyState strategyState = StrategyState.EmptyFor(strategy.StrategyId, strategy.Version);
        StrategyRngState strategyRng = StrategyRandomSource.Create(seed, strategy.StrategyId, strategy.Version).CaptureState();
        long decisions = 0, actionSequence = 0;
        var log = new StringBuilder();
        var canonical = new StringBuilder();
        string initialFingerprint = SimulationStateFingerprint.Create(session.CurrentState);
        string initialObservationFingerprint = string.Empty, initialCandidateSet = string.Empty;
        string firstAction = "none", battleStartFingerprint = string.Empty, battleOutcome = "not reached";
        int battleGold = -1;
        bool battleStarted = false;
        string terminal = "No battle reached within diagnostic limit.";
        if (renderLog)
        {
            log.AppendLine("NST first-encounter diagnostic");
            log.AppendLine("Strategy: " + strategy.StrategyId + " v" + strategy.Version);
            log.AppendLine("BaseSeed=" + BaseSeed + ", TrialIndex=" + TrialIndex + ", TrialSeed=" + seed.ToString(CultureInfo.InvariantCulture));
            log.AppendLine("Configuration: saved shop.unity via NSTSimulationBatchMenu.BuildSessionConfiguration; same strategy settings as 100-Trial menu.");
            log.AppendLine("Initial state fingerprint: " + initialFingerprint);
        }
        canonical.AppendLine("seed=" + seed + ";initial=" + initialFingerprint);

        for (int step = 0; step < MaxDecisionSteps; step++)
        {
            ActionExecutionState state = session.CurrentState;
            if (state.Stage.Phase == StageProgressPhase.Battle)
            {
                battleStarted = true;
                string beforeBattle = SimulationStateFingerprint.Create(state);
                battleStartFingerprint = SimulationStateFingerprint.Create(new object[] { state.Board, state.EnemyBoard, state.Ownership, state.Economy });
                battleGold = state.Economy.Gold;
                LogBattleStart(log, state, strategy, session, renderLog);
                BattleAdvanceResult battle = session.AdvanceBattleToEnd();
                string afterBattle = SimulationStateFingerprint.Create(session.CurrentState);
                canonical.AppendLine("battle:" + beforeBattle + ":" + afterBattle + ":" + battle.Status + ":" + battle.RoundsAdvanced + ":" + (battle.BattleResult == null ? "none" : battle.BattleResult.Outcome.ToString()));
                if (renderLog)
                {
                    log.AppendLine("\n[Battle result]");
                    log.AppendLine("Status=" + battle.Status + ", Failure=" + battle.Failure + ", Rounds=" + battle.RoundsAdvanced);
                    if (battle.BattleResult != null)
                    {
                        log.AppendLine("Outcome=" + battle.BattleResult.Outcome + ", FinishReason=" + battle.BattleResult.Reason +
                            ", FinalRound=" + battle.BattleResult.FinalRound + ", PlayerHP=" + battle.BattleResult.StartingPlayerHP + " -> " + battle.BattleResult.EndingPlayerHP);
                        log.AppendLine("Defeated enemies=" + battle.BattleResult.DefeatedEnemyCount + ", living enemies=" + battle.BattleResult.LivingEnemyCount);
                    }
                    log.AppendLine("Post-battle phase=" + session.CurrentState.Stage.Phase + ", GameOver=" + session.CurrentState.Stage.IsGameOver);
                    log.AppendLine("Battle start fingerprint=" + beforeBattle);
                    log.AppendLine("Battle end fingerprint=" + afterBattle);
                }
                terminal = battle.BattleResult == null
                    ? "Battle advance " + battle.Status + ": " + battle.Failure + " " + battle.Detail
                    : battle.BattleResult.Outcome + " / " + battle.BattleResult.Reason + " / stage=" + session.CurrentState.Stage.Phase;
                battleOutcome = terminal;
                break;
            }
            if (state.Stage.Phase == StageProgressPhase.GameOver || state.Stage.Phase == StageProgressPhase.Completed)
            { terminal = "Reached " + state.Stage.Phase + " before battle."; break; }
            if (state.Stage.Phase == StageProgressPhase.AwaitingEncounterStart)
            {
                StageSessionResult advance = session.AdvanceToNextStage();
                canonical.AppendLine("advance:" + SimulationStateFingerprint.Create(session.CurrentState) + ":" + advance.Status + ":" + advance.Failure);
                if (renderLog) log.AppendLine("Auto stage advance: " + advance.Status + "/" + advance.Failure + " " + advance.Detail);
                if (!advance.StateChanged) { terminal = "Stage advance rejected: " + advance.Failure; break; }
                continue;
            }

            ObservationBuildResult built = session.GetObservation();
            if (built == null || !built.Succeeded || built.Observation == null)
            { terminal = "Observation error: " + (built == null ? "null" : string.Join("; ", built.Errors)); break; }
            GameObservation observation = built.Observation;
            var actionId = new ActionId("trial:" + TrialIndex.ToString(CultureInfo.InvariantCulture) + ":action:" + actionSequence.ToString(CultureInfo.InvariantCulture));
            var context = new StrategyContext(strategy.StrategyId, strategy.Version, (long)TrialIndex,
                decisions, actionId, strategyState, strategyRng);
            string before = SimulationStateFingerprint.Create(state);
            StrategyDecision decision = strategy.ChooseAction(observation, context);
            if (!context.Accepts(decision)) { terminal = "Strategy returned invalid decision."; break; }

            if (decisions == 0) initialObservationFingerprint = SimulationStateFingerprint.Create(observation);
            string candidateSet = GetCandidateLog(observation, actionId, decision.Action, strategy, context.State,
                renderLog ? log : null);
            if (decisions == 0) initialCandidateSet = string.Join("\n", candidateSet.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x, StringComparer.Ordinal));
            if (decisions == 0) firstAction = decision.Action == null ? "<NoAction>" : DescribeAction(decision.Action);
            if (renderLog)
            {
                if (decisions == 0) LogObservation(log, observation, actionId);
                log.AppendLine("\n[Decision " + decisions + "]");
                log.AppendLine("Selected=" + (decision.Action == null ? "<NoAction>" : DescribeAction(decision.Action)));
                log.AppendLine("Selection diagnostic=" + decision.Diagnostic);
                log.AppendLine("StrategyState=" + SafeState(strategyState) + " -> " + SafeState(decision.NextState));
                log.AppendLine("Strategy RNG fingerprint=" + Fingerprint(strategyRng) + " -> " + Fingerprint(decision.NextStrategyRngState) +
                    "; consumed=" + (strategyRng == null ? "n/a" : !strategyRng.Equals(decision.NextStrategyRngState)));
            }
            strategyState = decision.NextState;
            strategyRng = decision.NextStrategyRngState;
            decisions++;
            if (!decision.HasAction)
            {
                canonical.AppendLine("decision:" + before + ":none:" + SafeState(strategyState) + ":" + Fingerprint(strategyRng));
                if (state.Stage.Phase == StageProgressPhase.Preparation && state.Stage.CurrentSelection != null &&
                    state.Stage.CurrentSelection.EnemyCategory == StageEnemyCategory.None)
                {
                    StageSessionResult empty = session.ResolveEnemylessStage(config.EventItemDefinition?.DefinitionId);
                    canonical.AppendLine("enemyless:" + empty.Status + ":" + SimulationStateFingerprint.Create(session.CurrentState));
                    if (renderLog) log.AppendLine("Enemyless auto-resolution=" + empty.Status + "/" + empty.Failure);
                    continue;
                }
                terminal = "Strategy returned NoAction in " + state.Stage.Phase + ": " + decision.Diagnostic;
                break;
            }

            actionSequence++;
            ActionDispatchResult dispatch = session.ExecuteAction(decision.Action);
            string afterAction = SimulationStateFingerprint.Create(session.CurrentState);
            canonical.AppendLine("decision:" + before + ":" + DescribeAction(decision.Action) + ":" + dispatch.Status + ":" +
                (dispatch.Error.IsNone ? "" : dispatch.Error.Code + "/" + dispatch.Error.Detail) + ":" + afterAction + ":" +
                SafeState(strategyState) + ":" + Fingerprint(strategyRng) + ":game-rng=" + SimulationStateFingerprint.Create(session.CurrentState.RandomState));
            if (renderLog)
            {
                log.AppendLine("Dispatcher=" + dispatch.Status + (dispatch.Error.IsNone ? string.Empty : ", Error=" + dispatch.Error.Code + ": " + dispatch.Error.Detail));
                log.AppendLine("Action state fingerprint=" + before + " -> " + afterAction);
                LogPostAction(log, session);
            }
            if (!dispatch.Succeeded)
            { terminal = "Action rejected: " + dispatch.Error.Code + ": " + dispatch.Error.Detail; break; }
        }

        string finalFingerprint = SimulationStateFingerprint.Create(session.CurrentState);
        var result = new TraceRun(log, canonical.ToString(), initialFingerprint, finalFingerprint, terminal, battleStarted,
            initialObservationFingerprint, initialCandidateSet, firstAction, battleStartFingerprint, battleOutcome, battleGold);
        return result;
    }

    private static void LogObservation(StringBuilder log, GameObservation o, ActionId actionId)
    {
        log.AppendLine("\n[Initial public observation]");
        log.AppendLine("Stage=" + o.Stage.StageNumber + "-" + o.Stage.RoundNumber + ", EncounterIndex=" + o.Stage.EncounterIndex +
            ", Phase=" + o.Stage.Phase + ", Choice=" + (o.Stage.Choice.HasValue ? o.Stage.Choice.Value.ToString() : "none"));
        log.AppendLine("Player HP=" + o.Player.HP + "/" + o.Player.MaximumHP + ", Gold=" + o.Player.Gold + ", Level=" + o.Player.Level + ", XP=" + o.Player.XP + "/" + o.Player.RequiredXP);
        log.AppendLine("Shop=" + (o.Shop == null ? "none" : o.Shop.Kind + " slots=" + o.Shop.SlotCount));
        if (o.Shop != null) foreach (ShopOfferObservation offer in o.Shop.Offers)
            log.AppendLine("  Offer " + offer.OfferId + " | " + offer.Kind + " | " + offer.DefinitionId + " | " + offer.DisplayName +
                " | cost=" + (offer.CharacterCost.HasValue ? offer.CharacterCost.Value.ToString(CultureInfo.InvariantCulture) : "n/a") +
                " | price=" + offer.DisplayPrice + " | " + offer.Status);
        log.AppendLine("Board " + o.Board.Columns + "x" + o.Board.Rows + ":");
        foreach (BoardSlotObservation slot in o.Board.Slots)
            log.AppendLine("  " + slot.SlotIndex + ": " + (slot.Character == null ? "empty" : slot.Character.InstanceId + " " + slot.Character.DefinitionId + "/" + slot.Character.DisplayName + "★" + slot.Character.Star + " ATK" + slot.Character.Attack + " DEF" + slot.Character.Defense));
        log.AppendLine("Hand " + o.Hand.SlotCount + " slots, empty=" + o.Hand.EmptySlotCount + ":");
        foreach (HandSlotObservation slot in o.Hand.Slots)
            log.AppendLine("  " + slot.SlotIndex + ": " + (slot.Character != null ? "Character " + slot.Character.DefinitionId + "★" + slot.Character.Star : slot.Item != null ? "Item " + slot.Item.DefinitionId + "/" + slot.Item.DisplayName : "empty"));
        log.AppendLine("Synergy available=" + o.Synergy.IsAvailable + ", AttributeBlockBonus=" + o.Synergy.AttributeBlockBonus);
        foreach (SynergyUnitObservation unit in o.Synergy.Units) log.AppendLine("  Unit " + unit.UnitName + ": slots=" + string.Join(",", unit.ParticipantSlots));
        foreach (SynergyAttributeObservation attribute in o.Synergy.Attributes) log.AppendLine("  Attribute " + attribute.AttributeName + ": count=" + attribute.ParticipantSlots.Count + ", block=" + attribute.BlockBonus);
        var candidateCheck = new RandomStrategy();
        bool canStart = candidateCheck.GetCandidates(o, actionId).Any(a => a is StartBattleAction);
        log.AppendLine("Battle start candidate available=" + canStart + "; strategy-action allowed=" + o.IsStrategyActionAllowed);
        if (o.Board.Slots.All(s => s.Character == null))
            log.AppendLine("Board empty: initial GameSession initializes an empty Board; it is not a rendered/hidden unit. Check available offers and Strategy evaluation for why no purchase was selected.");
    }

    private static string GetCandidateLog(GameObservation o, ActionId id, GameAction selected,
        IPlayerStrategy strategy, StrategyState state, StringBuilder log)
    {
        var details = new StringBuilder();
        if (log != null) log.AppendLine("\n[Action candidates evaluated]");
        if (strategy is HeuristicStrategy heuristic)
        {
            if (log != null) log.AppendLine("Tie-break policy: score descending, then action signature ordinal ascending.");
            string previous = ReadLastHeuristicSignature(state);
            IReadOnlyList<HeuristicActionEvaluation> evaluations = heuristic.GetEvaluations(o, id);
            int preparationDecision = o.Stage.Phase == ObservationPhase.Preparation
                ? ReadHeuristicDecisionCount(state) + 1 : 0;
            bool forcedStart = o.Stage.Phase == ObservationPhase.Preparation &&
                preparationDecision >= heuristic.Settings.PreparationDecisionLimit &&
                evaluations.Any(x => x.Action is StartBattleAction);
            if (evaluations.Count == 0) details.AppendLine("<none>");
            if (evaluations.Count == 0 && log != null) log.AppendLine("No raw candidates. Shared candidate generator found none for this phase/observation.");
            for (int i = 0; i < evaluations.Count; i++)
            {
                HeuristicActionEvaluation item = evaluations[i];
                string signature = HeuristicSignature(item.Action);
                string excluded = signature == previous ? "excluded: repeated previous action" : IsReverseSignature(previous, item.Action) ? "excluded: immediate reverse" : "eligible";
                bool chosen = selected != null && selected.ActionId == item.Action.ActionId && signature == HeuristicSignature(selected);
                string row = "#" + i + " " + DescribeAction(item.Action) + " | score=" + item.Score.ToString("R", CultureInfo.InvariantCulture) +
                    " | " + excluded + (chosen ? " | SELECTED" : string.Empty) + " | reason=" + item.Reason;
                details.AppendLine(signature);
                if (log != null) log.AppendLine("  " + row);
            }
            if (evaluations.Count > 0 && evaluations[0].Score <= 0 && !forcedStart && log != null)
                log.AppendLine("NoAction guard: top score is not positive; ChooseAction returns NoAction unless the preparation decision limit forces a legal StartBattle.");
            if (forcedStart && log != null)
                log.AppendLine("Forced-start guard is active at preparation decision " + preparationDecision + "; legal StartBattle receives ForcedStartBattleValue=" + heuristic.Settings.ForcedStartBattleValue + ".");
            if (selected != null && evaluations.Count > 0 && selected is StartBattleAction && log != null)
                log.AppendLine("StartBattle evaluation: score=" + evaluations.FirstOrDefault(x => x.Action is StartBattleAction)?.Score.ToString("R", CultureInfo.InvariantCulture) +
                    "; default StartBattleValue=30, rejected by score only if board>Level / HP<=0.");
        }
        else if (strategy is RandomStrategy random)
        {
            IReadOnlyList<GameAction> candidates = random.GetCandidates(o, id);
            if (candidates.Count == 0 && log != null) log.AppendLine("No raw candidates. RandomStrategy has no candidate-reason API; candidate generation is shared public-observation logic.");
            for (int i = 0; i < candidates.Count; i++)
            {
                details.AppendLine(ActionSignature(candidates[i]));
                if (log != null) log.AppendLine("  #" + i + " " + DescribeAction(candidates[i]) + (selected != null && selected.ActionId == candidates[i].ActionId && ActionSignature(candidates[i]) == ActionSignature(selected) ? " | SELECTED" : string.Empty) +
                    " | reason=RandomStrategy.GetCandidates output; per-action reason/score is not exposed by its API.");
            }
            if (log != null) log.AppendLine("Candidate order is stable signature order; selection is uniform over candidates unless the preparation limit forces StartBattle.");
        }
        return details.ToString();
    }

    private static void LogPostAction(StringBuilder log, GameSession session)
    {
        ActionExecutionState s = session.CurrentState;
        log.AppendLine("After action: phase=" + s.Stage.Phase + ", Gold=" + s.Economy.Gold + ", HP=" + s.Economy.CurrentHP + ", Level=" + s.Economy.Level + ", XP=" + s.Economy.XP);
        log.AppendLine("  Board=" + string.Join(",", s.Board.InstanceIds.Select((id, i) => i + ":" + (id ?? "empty"))));
        log.AppendLine("  Hand=" + string.Join(",", s.Hand.Slots.Select((slot, i) => i + ":" + slot.Kind)));
    }

    private static void LogBattleStart(StringBuilder log, ActionExecutionState s, IPlayerStrategy strategy,
        GameSession session, bool enabled)
    {
        if (!enabled) return;
        log.AppendLine("\n[Battle start state]");
        int count = s.Board.InstanceIds.Count(id => id != null);
        log.AppendLine("Board placements=" + count + ", HP=" + s.Economy.CurrentHP + ", Gold=" + s.Economy.Gold + ", Level=" + s.Economy.Level);
        for (int i = 0; i < s.Board.SlotCount; i++)
        {
            string id = s.Board.InstanceIds[i];
            if (id == null) { log.AppendLine("  Board[" + i + "]=empty"); continue; }
            if (s.Ownership.TryGet(id, out CharacterInstanceView unit))
                log.AppendLine("  Board[" + i + "]=" + unit.DefinitionId + " Instance=" + unit.InstanceId + " ★" + unit.Star + " ATK=" + unit.Attack + " DEF=" + unit.Defense);
        }
        BattleState battle = s.Battle;
        log.AppendLine("Battle state present=" + (battle != null));
        if (battle != null)
        {
            log.AppendLine("Allies:");
            foreach (BattleUnitState ally in battle.Allies)
                log.AppendLine("  slot=" + ally.SlotIndex + " def=" + ally.DefinitionId + " instance=" + ally.InstanceId + " ★" + ally.Star + " ATK=" + ally.Attack + " DEF=" + ally.Defense + " HP=" + ally.CurrentHP + "/" + ally.MaxHP);
            log.AppendLine("Enemies:");
            foreach (BattleUnitState enemy in battle.Enemies)
                log.AppendLine("  slot=" + enemy.SlotIndex + " def=" + enemy.DefinitionId + " instance=" + enemy.InstanceId + " ATK=" + enemy.Attack + " DEF=" + enemy.Defense + " HP=" + enemy.CurrentHP + "/" + enemy.MaxHP + " Gap=" + enemy.Gap + " Cooldown=" + enemy.Cooldown);
            log.AppendLine("Battle RNG fingerprint=" + Fingerprint(battle.BattleRandomState));
        }
        ObservationBuildResult battleObservationResult = session.GetObservation();
        if (battleObservationResult != null && battleObservationResult.Succeeded && battleObservationResult.Observation != null)
        {
            SynergyObservation synergy = battleObservationResult.Observation.Synergy;
            if (synergy == null)
            {
                log.AppendLine("Battle-start synergy unavailable: ObservationBuilder returned no SynergyObservation.");
                if (battle != null)
                {
                    log.AppendLine("Battle initialization snapshot: AttributeBlock=" + battle.AttributeBlock + ", UnitMoveCount=" + battle.UnitMoves.Count);
                    foreach (UnitMoveState move in battle.UnitMoves)
                        log.AppendLine("  Unit " + move.UnitName + ": slots=" + string.Join(",", move.ParticipantSlotIndexes));
                }
            }
            else
            {
                log.AppendLine("Battle-start synergy: available=" + synergy.IsAvailable + ", AttributeBlockBonus=" + synergy.AttributeBlockBonus);
                foreach (SynergyUnitObservation unit in synergy.Units)
                    log.AppendLine("  Unit " + unit.UnitName + ": slots=" + string.Join(",", unit.ParticipantSlots));
                foreach (SynergyAttributeObservation attribute in synergy.Attributes)
                    log.AppendLine("  Attribute " + attribute.AttributeName + ": count=" + attribute.ParticipantSlots.Count + ", block=" + attribute.BlockBonus);
            }
        }
        else
        {
            log.AppendLine("Battle-start synergy unavailable: " + (battleObservationResult == null ? "ObservationBuildResult is null." : string.Join("; ", battleObservationResult.Errors)));
        }
        log.AppendLine("Enemy Formation (pre-battle slots):");
        if (s.EnemyBoard != null)
            for (int i = 0; i < s.EnemyBoard.SlotCount; i++)
                if (s.EnemyBoard.TryGetAt(i, out EnemyInstance enemy))
                    log.AppendLine("  Enemy[" + i + "]=" + (enemy == null ? "empty" : enemy.DefinitionId + " Instance=" + enemy.InstanceId + " ATK=" + enemy.Attack + " DEF=" + enemy.Defense + " HP=" + enemy.MaxHealth + " Gap=" + enemy.Gap + " Cooldown=" + enemy.Cooldown));
        log.AppendLine("Player Battle HP=" + (battle == null ? "n/a" : battle.PlayerCurrentHP.ToString(CultureInfo.InvariantCulture)) + ", Strategy=" + strategy.StrategyId);
        // Synergy is available in the pre-start observation; it is recorded at the decision point.
    }

    private static string DescribeAction(GameAction a)
    {
        if (a == null) return "<NoAction>";
        string args = a switch
        {
            SelectStageChoiceAction x => "choice=" + x.Choice,
            BuyCharacterAction x => "offer=" + x.OfferId + ", placement=" + x.Placement.Kind + ":" + x.Placement.SlotIndex,
            BuyItemAction x => "offer=" + x.OfferId + ", hand=" + x.HandSlot,
            RerollShopAction x => "shop=" + x.ShopKind,
            MoveOrSwapBoardAction x => x.SourceSlot + "->" + x.TargetSlot,
            MoveOrSwapHandAction x => x.SourceSlot + "->" + x.TargetSlot,
            MoveBoardToHandAction x => "board=" + x.BoardSlot + ", hand=" + x.HandSlot,
            MoveHandToBoardAction x => "hand=" + x.HandSlot + ", board=" + x.BoardSlot,
            SellCharacterAction x => "instance=" + x.InstanceId,
            SellItemAction x => "hand=" + x.HandSlot,
            BuyExperienceAction => "",
            UseHandItemAction x => "hand=" + x.HandSlot + ", target=" + x.Target.Kind + ":" + x.Target.SlotIndex,
            UseEventItemAction x => "target=" + x.Target.Kind + ":" + x.Target.SlotIndex,
            _ => ""
        };
        return a.Kind + "(" + args + ") id=" + a.ActionId.Value;
    }

    private static string ActionSignature(GameAction a) => DescribeAction(a).Split(new[] { " id=" }, StringSplitOptions.None)[0];
    private static string HeuristicSignature(GameAction a)
    {
        switch (a)
        {
            case SelectStageChoiceAction x: return "1:" + (int)x.Choice;
            case BuyCharacterAction x: return "2:" + x.OfferId + ":" + (int)x.Placement.Kind + ":" + x.Placement.SlotIndex;
            case BuyItemAction x: return "3:" + x.OfferId + ":" + x.HandSlot;
            case RerollShopAction x: return "4:" + (int)x.ShopKind;
            case BuyExperienceAction: return "5";
            case MoveOrSwapBoardAction x: return "6:" + x.SourceSlot + ":" + x.TargetSlot;
            case MoveOrSwapHandAction x: return "7:" + x.SourceSlot + ":" + x.TargetSlot;
            case MoveBoardToHandAction x: return "8:" + x.BoardSlot + ":" + x.HandSlot;
            case MoveHandToBoardAction x: return "9:" + x.HandSlot + ":" + x.BoardSlot;
            case SellCharacterAction x: return "10:" + x.InstanceId;
            case SellItemAction x: return "11:" + x.HandSlot;
            case UseHandItemAction x: return "12:" + TargetSignature(x.Target);
            case UseEventItemAction x: return "13:" + TargetSignature(x.Target);
            case EndEventAction: return "14";
            case StartBattleAction: return "15";
            default: return ((int)a.Kind).ToString(CultureInfo.InvariantCulture);
        }
    }
    private static string TargetSignature(GameActionTarget t) => (int)t.Kind + ":" + t.SlotIndex + ":" + t.EnemyCount + ":" + (t.IsPresent ? 1 : 0);
    private static bool IsReverseSignature(string last, GameAction next)
    {
        if (string.IsNullOrEmpty(last)) return false;
        string[] p = last.Split(':');
        if (p.Length < 3 || !int.TryParse(p[0], out int kind) || !int.TryParse(p[1], out int from) || !int.TryParse(p[2], out int to)) return false;
        if (next is MoveOrSwapBoardAction b && kind == 6) return b.SourceSlot == to && b.TargetSlot == from;
        if (next is MoveOrSwapHandAction h && kind == 7) return h.SourceSlot == to && h.TargetSlot == from;
        if (next is MoveBoardToHandAction b2h && kind == 9) return b2h.BoardSlot == to && b2h.HandSlot == from;
        if (next is MoveHandToBoardAction h2b && kind == 8) return h2b.HandSlot == to && h2b.BoardSlot == from;
        return false;
    }
    private static string ReadLastHeuristicSignature(StrategyState state)
    {
        if (state == null || state.SchemaVersion != HeuristicStrategy.StateSchemaVersion || !state.Payload.StartsWith("hs2|", StringComparison.Ordinal)) return string.Empty;
        try { string[] p = state.Payload.Substring(4).Split('|'); return p.Length == 4 ? Encoding.UTF8.GetString(Convert.FromBase64String(p[3])) : string.Empty; }
        catch (FormatException) { return string.Empty; }
    }
    private static int ReadHeuristicDecisionCount(StrategyState state)
    {
        if (state == null || state.SchemaVersion != HeuristicStrategy.StateSchemaVersion || !state.Payload.StartsWith("hs2|", StringComparison.Ordinal)) return 0;
        string[] p = state.Payload.Substring(4).Split('|');
        return p.Length == 4 && int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : 0;
    }
    private static string SafeState(StrategyState state) => state == null ? "null" : state.SchemaVersion + ":" + state.Payload;
    private static string Fingerprint(object value) => value == null ? "null" : SimulationStateFingerprint.Create(value);
    private static string WriteDiagnostic(string file, string content)
    {
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SimulationResults", "Diagnostics"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, file);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private sealed class TraceRun
    {
        public StringBuilder Log { get; }
        public string CanonicalTrace { get; }
        public string InitialFingerprint { get; }
        public string FinalFingerprint { get; }
        public string TerminalSummary { get; }
        public bool BattleStarted { get; }
        public string InitialObservationFingerprint { get; }
        public string InitialCandidateSet { get; }
        public string FirstAction { get; }
        public string BattleStartFingerprint { get; }
        public string BattleOutcome { get; }
        public int BattleGold { get; }
        public TraceRun(StringBuilder log, string trace, string initial, string final, string terminal, bool battleStarted,
            string observation, string candidates, string firstAction, string battleFingerprint, string battleOutcome, int battleGold)
        {
            Log = log; CanonicalTrace = trace; InitialFingerprint = initial; FinalFingerprint = final;
            TerminalSummary = terminal; BattleStarted = battleStarted; InitialObservationFingerprint = observation;
            InitialCandidateSet = candidates; FirstAction = firstAction; BattleStartFingerprint = battleFingerprint;
            BattleOutcome = battleOutcome; BattleGold = battleGold;
        }
    }
}
