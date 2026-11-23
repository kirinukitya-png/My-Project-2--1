using System.Collections.Generic;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Enemies
{
    public enum EnemyEncounterSetupFailure
    {
        None,
        EncounterGenerationFailed,
        FormationGenerationFailed
    }

    /// <summary>Atomic proposal combining encounter selection and board materialization.</summary>
    public sealed class EnemyEncounterSetupResult
    {
        public bool Succeeded { get; }
        public bool HasEncounter { get; }
        public EnemyEncounterSetupFailure Failure { get; }
        public EncounterGenerationResult EncounterResult { get; }
        public EnemyFormationResult FormationResult { get; }
        public EnemyBoardState Board { get; }
        public Pcg32State NextEncounterRandomState { get; }
        public bool EncounterRandomConsumed { get; }

        internal EnemyEncounterSetupResult(bool succeeded, bool hasEncounter,
            EnemyEncounterSetupFailure failure, EncounterGenerationResult encounterResult,
            EnemyFormationResult formationResult, EnemyBoardState board,
            Pcg32State nextRandomState, bool randomConsumed)
        {
            Succeeded = succeeded;
            HasEncounter = hasEncounter;
            Failure = failure;
            EncounterResult = encounterResult;
            FormationResult = formationResult;
            Board = board;
            NextEncounterRandomState = nextRandomState;
            EncounterRandomConsumed = randomConsumed;
        }
    }

    /// <summary>Connects encounter selection to formation creation without committing state or RNG.</summary>
    public static class EnemyEncounterSetupRules
    {
        public static EnemyEncounterSetupResult Generate(StageProgressState stage,
            IReadOnlyList<EncounterPoolDefinition> pools, IReadOnlyList<EnemyDefinition> definitions,
            EnemyPreparationState preparation, Pcg32State encounterRandomState)
        {
            // Stage 7 events have no stage selection/encounter offer; they are a valid no-enemy phase.
            if (stage != null && stage.Phase == StageProgressPhase.EventPending &&
                stage.CurrentSelection == null)
                return new EnemyEncounterSetupResult(true, false, EnemyEncounterSetupFailure.None,
                    null, null, EnemyBoardState.CreateEmpty(), encounterRandomState, false);

            EncounterGenerationResult encounter = EncounterGenerationRules.Generate(
                stage, pools, encounterRandomState);
            if (!encounter.Succeeded)
                return new EnemyEncounterSetupResult(false, false,
                    EnemyEncounterSetupFailure.EncounterGenerationFailed, encounter, null, null,
                    encounterRandomState, false);

            if (!encounter.HasEncounter)
                return new EnemyEncounterSetupResult(true, false, EnemyEncounterSetupFailure.None,
                    encounter, null, EnemyBoardState.CreateEmpty(), encounterRandomState, false);

            EnemyFormationResult formation = EnemyFormationRules.CreateBoard(
                encounter.SelectedFormation, definitions, stage.EncounterIndex, preparation);
            if (!formation.Succeeded)
                return new EnemyEncounterSetupResult(false, false,
                    EnemyEncounterSetupFailure.FormationGenerationFailed, encounter, formation, null,
                    encounterRandomState, false);

            return new EnemyEncounterSetupResult(true, true, EnemyEncounterSetupFailure.None,
                encounter, formation, formation.Board, encounter.NextEncounterRandomState,
                encounter.RandomConsumed);
        }
    }
}
