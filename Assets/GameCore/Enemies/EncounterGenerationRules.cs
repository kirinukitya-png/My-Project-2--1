using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Enemies
{
    public enum EncounterGenerationFailure
    {
        None,
        InvalidStageState,
        InvalidCategory,
        InvalidPoolList,
        NoCandidates,
        InvalidRandomState
    }

    /// <summary>Result of choosing one formation or resolving a legitimate no-enemy selection.</summary>
    public sealed class EncounterGenerationResult
    {
        public bool Succeeded { get; }
        public bool HasEncounter { get; }
        public EncounterGenerationFailure Failure { get; }
        public EnemyFormationDefinition SelectedFormation { get; }
        public string SelectedFormationId => SelectedFormation?.FormationId;
        public StageEnemyCategory Category { get; }
        public int RoundIndex { get; }
        public int CandidateCount { get; }
        public bool RandomConsumed { get; }
        public Pcg32State NextEncounterRandomState { get; }

        internal EncounterGenerationResult(bool succeeded, bool hasEncounter,
            EncounterGenerationFailure failure, EnemyFormationDefinition selectedFormation,
            StageEnemyCategory category, int roundIndex, int candidateCount,
            bool randomConsumed, Pcg32State nextRandomState)
        {
            Succeeded = succeeded;
            HasEncounter = hasEncounter;
            Failure = failure;
            SelectedFormation = selectedFormation;
            Category = category;
            RoundIndex = roundIndex;
            CandidateCount = candidateCount;
            RandomConsumed = randomConsumed;
            NextEncounterRandomState = nextRandomState;
        }
    }

    /// <summary>Pure, deterministic selection from ordered pools using only the Encounter stream.</summary>
    public static class EncounterGenerationRules
    {
        public static EncounterGenerationResult Generate(StageProgressState stage,
            IReadOnlyList<EncounterPoolDefinition> pools, Pcg32State encounterRandomState)
        {
            int roundIndex = stage == null ? -1 : stage.EncounterIndex;
            if (stage == null || stage.Phase != StageProgressPhase.Preparation ||
                stage.CurrentSelection == null || roundIndex < 0)
                return Failure(EncounterGenerationFailure.InvalidStageState,
                    StageEnemyCategory.None, roundIndex, 0, encounterRandomState);

            StageEnemyCategory category = stage.CurrentSelection.EnemyCategory;
            if (category == StageEnemyCategory.None)
                return new EncounterGenerationResult(true, false, EncounterGenerationFailure.None,
                    null, category, roundIndex, 0, false, encounterRandomState);
            if (category != StageEnemyCategory.Normal && category != StageEnemyCategory.Hard &&
                category != StageEnemyCategory.Boss)
                return Failure(EncounterGenerationFailure.InvalidCategory,
                    category, roundIndex, 0, encounterRandomState);
            if (pools == null)
                return Failure(EncounterGenerationFailure.InvalidPoolList,
                    category, roundIndex, 0, encounterRandomState);

            var candidates = new List<EnemyFormationDefinition>();
            for (int i = 0; i < pools.Count; i++)
            {
                EncounterPoolDefinition pool = pools[i];
                if (pool == null)
                    return Failure(EncounterGenerationFailure.InvalidPoolList,
                        category, roundIndex, candidates.Count, encounterRandomState);
                if (pool.Category != category || !pool.ContainsRound(roundIndex)) continue;
                for (int j = 0; j < pool.Formations.Count; j++)
                    candidates.Add(pool.Formations[j]);
            }

            if (candidates.Count == 0)
                return Failure(EncounterGenerationFailure.NoCandidates,
                    category, roundIndex, 0, encounterRandomState);

            Pcg32RandomSource random;
            try { random = Pcg32RandomSource.FromState(encounterRandomState); }
            catch (ArgumentException)
            {
                return Failure(EncounterGenerationFailure.InvalidRandomState,
                    category, roundIndex, candidates.Count, encounterRandomState);
            }

            // Match the legacy one-call selection shape: even a single candidate consumes one NextInt.
            int selectedIndex = random.NextInt(0, candidates.Count);
            return new EncounterGenerationResult(true, true, EncounterGenerationFailure.None,
                candidates[selectedIndex], category, roundIndex, candidates.Count,
                true, random.CaptureState());
        }

        private static EncounterGenerationResult Failure(EncounterGenerationFailure failure,
            StageEnemyCategory category, int roundIndex, int candidateCount,
            Pcg32State unchangedRandomState)
        {
            return new EncounterGenerationResult(false, false, failure, null,
                category, roundIndex, candidateCount, false, unchangedRandomState);
        }
    }
}
