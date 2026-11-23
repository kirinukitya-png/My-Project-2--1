using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;
using UnityEngine;

namespace ProjectNST.GameCore.Tests
{
    public sealed class EnemyEncounterGenerationTests
    {
        private static StageProgressConfig Config()
        {
            return new StageProgressConfig(25, 5, 5, 4, 0, 0, 0, 1, 0, 0, 3, 6,
                0, 0, 10, 4, 2, true);
        }

        private static EnemyFormationDefinition Formation(string id)
        {
            var slots = new EnemyFormationSlotDefinition[6];
            for (int i = 0; i < slots.Length; i++) slots[i] = new EnemyFormationSlotDefinition(null, i, i + 1);
            return new EnemyFormationDefinition(id, slots);
        }

        private static StageProgressState PreparationAt(int encounterIndex, StageChoice choice = StageChoice.SelectA)
        {
            StageProgressConfig config = Config();
            StageProgressState state = StageProgressRules.CreateInitialState(config);
            for (int index = 0; index < encounterIndex; index++)
            {
                state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
                if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                    state = StageSelectionRules.Select(state, config, StageChoice.SelectA).NextState;
                state = StageProgressRules.BeginBattle(state).NextState;
                state = StageProgressRules.ResolveBattle(state, config, true).NextState;
                state = StageProgressRules.CompletePendingReward(state, config,
                    new Pcg32RandomSource((ulong)(index + 1), 2).CaptureState()).NextState;
                if (state.Phase == StageProgressPhase.EventPending)
                    state = StageProgressRules.CompleteEvent(state, false).NextState;
            }
            state = StageProgressRules.BeginCurrentEncounter(state, config).NextState;
            if (state.Phase == StageProgressPhase.AwaitingStageChoice)
                state = StageSelectionRules.Select(state, config, choice).NextState;
            return state;
        }

        private static void AssertStateEqual(Pcg32State expected, Pcg32State actual)
        {
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Increment, Is.EqualTo(expected.Increment));
        }

        [Test]
        public void FormationAndPool_DefensivelyCopyAndPreserveSlotAndCandidateOrder()
        {
            var slots = new List<EnemyFormationSlotDefinition>
            {
                new EnemyFormationSlotDefinition("E1", 4, 2), new EnemyFormationSlotDefinition("E1", 3, 1),
                new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition("E2", 0, 0),
                new EnemyFormationSlotDefinition(null, 0, 0), new EnemyFormationSlotDefinition(null, 0, 0)
            };
            EnemyFormationDefinition formation = new EnemyFormationDefinition("F1", slots);
            slots[0] = new EnemyFormationSlotDefinition("E9", 0, 0);
            Assert.That(formation.Slots.Count, Is.EqualTo(6));
            Assert.That(formation.Slots[0].EnemyDefinitionId, Is.EqualTo("E1"));
            Assert.That(formation.Slots[1].EnemyDefinitionId, Is.EqualTo("E1"));
            Assert.That(formation.Slots[0].Gap, Is.EqualTo(4));
            Assert.That(formation.Slots[0].Cooldown, Is.EqualTo(2));
            Assert.That(formation.Slots[2].HasEnemy, Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<EnemyFormationSlotDefinition>)formation.Slots).Clear());
            Assert.Throws<ArgumentException>(() => new EnemyFormationDefinition("bad", new[] { slots[0] }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyFormationSlotDefinition(null, -1, 0));

            var candidates = new List<EnemyFormationDefinition> { formation, formation, Formation("F2") };
            var rounds = new List<int> { 0, 4 };
            var pool = new EncounterPoolDefinition("P1", rounds, StageEnemyCategory.Hard, candidates);
            candidates.Clear(); rounds[0] = 8;
            Assert.That(pool.Formations.Count, Is.EqualTo(3));
            Assert.That(pool.Formations[0], Is.SameAs(pool.Formations[1]));
            Assert.That(pool.ContainsRound(0), Is.True);
            Assert.That(pool.ContainsRound(4), Is.True);
            Assert.That(pool.ContainsRound(8), Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)pool.RoundIndices).Clear());
            Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterPoolDefinition("P", new[] { -1 },
                StageEnemyCategory.Normal, Array.Empty<EnemyFormationDefinition>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterPoolDefinition("P", null,
                StageEnemyCategory.None, Array.Empty<EnemyFormationDefinition>()));
        }

        [Test]
        public void SelectionMapsAtoNormal_CtoHard_AndAutomaticRoundFourToBoss()
        {
            EnemyFormationDefinition normal = Formation("N");
            EnemyFormationDefinition hard = Formation("H");
            EnemyFormationDefinition boss = Formation("B");
            var pools = new[]
            {
                new EncounterPoolDefinition("normal", new[] { 0 }, StageEnemyCategory.Normal, new[] { normal }),
                new EncounterPoolDefinition("hard", new[] { 4 }, StageEnemyCategory.Hard, new[] { hard }),
                new EncounterPoolDefinition("boss", new[] { 3 }, StageEnemyCategory.Boss, new[] { boss })
            };
            Pcg32State checkpoint = new Pcg32RandomSource(5, 6).CaptureState();
            EncounterGenerationResult a = EncounterGenerationRules.Generate(PreparationAt(0), pools, checkpoint);
            EncounterGenerationResult c = EncounterGenerationRules.Generate(PreparationAt(4, StageChoice.SelectC), pools, checkpoint);
            EncounterGenerationResult b = EncounterGenerationRules.Generate(PreparationAt(3), pools, checkpoint);
            Assert.That(a.SelectedFormationId, Is.EqualTo("N"));
            Assert.That(a.Category, Is.EqualTo(StageEnemyCategory.Normal));
            Assert.That(a.RoundIndex, Is.EqualTo(0));
            Assert.That(c.SelectedFormationId, Is.EqualTo("H"));
            Assert.That(c.Category, Is.EqualTo(StageEnemyCategory.Hard));
            Assert.That(c.RoundIndex, Is.EqualTo(4));
            Assert.That(b.SelectedFormationId, Is.EqualTo("B"));
            Assert.That(b.Category, Is.EqualTo(StageEnemyCategory.Boss));
            Assert.That(b.RoundIndex, Is.EqualTo(3));
        }

        [TestCase(StageChoice.SelectB)]
        [TestCase(StageChoice.SelectD)]
        public void NoEnemyChoiceSucceedsWithoutEncounterOrRandomConsumption(StageChoice choice)
        {
            var state = PreparationAt(4, choice);
            Pcg32State checkpoint = new Pcg32RandomSource(17, 9).CaptureState();
            EncounterGenerationResult result = EncounterGenerationRules.Generate(state,
                Array.Empty<EncounterPoolDefinition>(), checkpoint);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.HasEncounter, Is.False);
            Assert.That(result.Category, Is.EqualTo(StageEnemyCategory.None));
            Assert.That(result.RandomConsumed, Is.False);
            AssertStateEqual(checkpoint, result.NextEncounterRandomState);
        }

        [Test]
        public void NoCandidatesIsConfigurationFailureAndDoesNotConsumeRandom()
        {
            Pcg32State checkpoint = new Pcg32RandomSource(21, 7).CaptureState();
            var pools = new[] { new EncounterPoolDefinition("wrong-round", new[] { 1 },
                StageEnemyCategory.Normal, new[] { Formation("F") }) };
            EncounterGenerationResult result = EncounterGenerationRules.Generate(PreparationAt(0), pools, checkpoint);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(EncounterGenerationFailure.NoCandidates));
            Assert.That(result.SelectedFormation, Is.Null);
            Assert.That(result.RandomConsumed, Is.False);
            AssertStateEqual(checkpoint, result.NextEncounterRandomState);
        }

        [Test]
        public void SingleCandidateConsumesOneNextIntAndDoesNotMutateInputCheckpoint()
        {
            Pcg32State checkpoint = new Pcg32RandomSource(31, 3).CaptureState();
            Pcg32RandomSource expected = Pcg32RandomSource.FromState(checkpoint);
            expected.NextInt(0, 1);
            var only = Formation("Only");
            EncounterGenerationResult result = EncounterGenerationRules.Generate(PreparationAt(0),
                new[] { new EncounterPoolDefinition("P", new[] { 0 }, StageEnemyCategory.Normal, new[] { only }) }, checkpoint);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CandidateCount, Is.EqualTo(1));
            Assert.That(result.SelectedFormation, Is.SameAs(only));
            Assert.That(result.RandomConsumed, Is.True);
            AssertStateEqual(expected.CaptureState(), result.NextEncounterRandomState);
            AssertStateEqual(checkpoint, Pcg32RandomSource.FromState(checkpoint).CaptureState());
        }

        [Test]
        public void DuplicateFormationEntriesRemainWeightedAndSelectionIsReproducible()
        {
            EnemyFormationDefinition a = Formation("A");
            EnemyFormationDefinition b = Formation("B");
            StageProgressState stage = PreparationAt(0);
            var pool = new[] { new EncounterPoolDefinition("P", new[] { 0 }, StageEnemyCategory.Normal,
                new[] { a, a, b }) };
            int countA = 0;
            for (ulong seed = 1; seed <= 600; seed++)
            {
                Pcg32State state = new SimulationRandomStreams(seed).Encounter.CaptureState();
                EncounterGenerationResult first = EncounterGenerationRules.Generate(stage, pool, state);
                EncounterGenerationResult repeated = EncounterGenerationRules.Generate(stage, pool, state);
                Assert.That(first.SelectedFormationId, Is.EqualTo(repeated.SelectedFormationId));
                AssertStateEqual(first.NextEncounterRandomState, repeated.NextEncounterRandomState);
                if (first.SelectedFormationId == "A") countA++;
            }
            Assert.That(countA, Is.InRange(350, 450), "A occurs twice in the three-entry candidate sequence.");
        }

        [Test]
        public void MatchingPoolsAggregateInInputOrderAndRetainCrossPoolDuplicates()
        {
            EnemyFormationDefinition a = Formation("A");
            EnemyFormationDefinition b = Formation("B");
            EnemyFormationDefinition c = Formation("C");
            var pools = new[]
            {
                new EncounterPoolDefinition("P1", new[] { 0 }, StageEnemyCategory.Normal, new[] { a, b }),
                new EncounterPoolDefinition("P2", new[] { 0 }, StageEnemyCategory.Normal, new[] { c, a })
            };
            for (ulong seed = 0; seed < 100; seed++)
            {
                Pcg32State checkpoint = new Pcg32RandomSource(seed, 19).CaptureState();
                Pcg32RandomSource expectedRandom = Pcg32RandomSource.FromState(checkpoint);
                int expectedIndex = expectedRandom.NextInt(0, 4);
                string[] orderedIds = { "A", "B", "C", "A" };
                EncounterGenerationResult result = EncounterGenerationRules.Generate(
                    PreparationAt(0), pools, checkpoint);
                Assert.That(result.CandidateCount, Is.EqualTo(4));
                Assert.That(result.SelectedFormationId, Is.EqualTo(orderedIds[expectedIndex]));
            }
        }

        [Test]
        public void InvalidPhaseAndInvalidRandomStateFailWithoutChangingStageOrCheckpoint()
        {
            StageProgressState initial = StageProgressRules.CreateInitialState(Config());
            Pcg32State invalid = default(Pcg32State);
            EncounterGenerationResult phaseFailure = EncounterGenerationRules.Generate(initial,
                Array.Empty<EncounterPoolDefinition>(), invalid);
            Assert.That(phaseFailure.Succeeded, Is.False);
            Assert.That(phaseFailure.Failure, Is.EqualTo(EncounterGenerationFailure.InvalidStageState));
            AssertStateEqual(invalid, phaseFailure.NextEncounterRandomState);

            EncounterGenerationResult rngFailure = EncounterGenerationRules.Generate(PreparationAt(0),
                new[] { new EncounterPoolDefinition("P", new[] { 0 }, StageEnemyCategory.Normal,
                    new[] { Formation("F") }) }, invalid);
            Assert.That(rngFailure.Succeeded, Is.False);
            Assert.That(rngFailure.Failure, Is.EqualTo(EncounterGenerationFailure.InvalidRandomState));
            Assert.That(rngFailure.RandomConsumed, Is.False);
            AssertStateEqual(invalid, rngFailure.NextEncounterRandomState);
        }

        [Test]
        public void EncounterGenerationLeavesOtherRandomStreamsUntouched()
        {
            SimulationRandomStreams streams = new SimulationRandomStreams(1234UL);
            SimulationRandomStreamsState before = streams.CaptureState();
            EncounterGenerationRules.Generate(PreparationAt(0), new[]
            {
                new EncounterPoolDefinition("P", new[] { 0 }, StageEnemyCategory.Normal, new[] { Formation("F") })
            }, before.Encounter);
            SimulationRandomStreamsState after = streams.CaptureState();
            AssertStateEqual(before.Shop, after.Shop);
            AssertStateEqual(before.Stats, after.Stats);
            AssertStateEqual(before.Battle, after.Battle);
            AssertStateEqual(before.Event, after.Event);
            AssertStateEqual(before.Roster, after.Roster);
            AssertStateEqual(before.Economy, after.Economy);
            AssertStateEqual(before.Encounter, after.Encounter);
        }

        [Test]
        public void FormationMapperRejectsAnUnmappedEnemyDefinitionReference()
        {
            Type formationType = FindType("EnemyFormationData");
            Type enemyType = FindType("EnemyData");
            Type mapperType = FindType("ProjectNST.GameUnityAdapter.EnemyEncounterDataMapper");
            if (formationType == null || enemyType == null || mapperType == null)
                Assert.Ignore("Unity encounter asset types or Mapper are unavailable.");

            ScriptableObject formationAsset = ScriptableObject.CreateInstance(formationType);
            ScriptableObject enemyAsset = ScriptableObject.CreateInstance(enemyType);
            try
            {
                SetField(formationType, formationAsset, "formationId", "F_invalid_ref_test");
                SetField(enemyType, enemyAsset, "enemyId", "E_missing_test");
                Type slotType = formationType.GetNestedType("FormationSlot", BindingFlags.Public);
                Assert.That(slotType, Is.Not.Null);
                Array slots = Array.CreateInstance(slotType, 6);
                for (int i = 0; i < slots.Length; i++)
                {
                    object slot = Activator.CreateInstance(slotType);
                    if (i == 0) SetField(slotType, slot, "enemy", enemyAsset);
                    slots.SetValue(slot, i);
                }
                SetField(formationType, formationAsset, "slots", slots);

                MethodInfo map = mapperType.GetMethod("ToFormationDefinition", BindingFlags.Public | BindingFlags.Static);
                Assert.That(map, Is.Not.Null);
                var knownEnemies = new Dictionary<string, EnemyDefinition>(StringComparer.Ordinal);
                Assert.Throws<TargetInvocationException>(() => map.Invoke(null,
                    new object[] { formationAsset, knownEnemies }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(formationAsset);
                UnityEngine.Object.DestroyImmediate(enemyAsset);
            }
        }

        [Test]
        public void MapsSavedSceneEncounterPoolsAndAllEnemyFormationsWithoutChangingAssets()
        {
            Type mapperType = FindType("ProjectNST.GameUnityAdapter.EnemyEncounterDataMapper");
            Type enemyMapperType = FindType("ProjectNST.GameUnityAdapter.EnemyDataMapper");
            Type enemyDataType = FindType("EnemyData");
            Type formationDataType = FindType("EnemyFormationData");
            Type poolDataType = FindType("EncounterPoolData");
            MethodInfo load = FindType("UnityEditor.AssetDatabase")?.GetMethod("LoadAssetAtPath",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(Type) }, null);
            MethodInfo guidToPath = FindType("UnityEditor.AssetDatabase")?.GetMethod("GUIDToAssetPath",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (mapperType == null || enemyMapperType == null || enemyDataType == null || formationDataType == null ||
                poolDataType == null || load == null || guidToPath == null)
                Assert.Ignore("Unity editor AssetDatabase or encounter Mapper is unavailable.");

            MethodInfo enemyMap = enemyMapperType.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
            MethodInfo formationMap = mapperType.GetMethod("ToFormationDefinition", BindingFlags.Public | BindingFlags.Static);
            MethodInfo poolMap = mapperType.GetMethod("ToEncounterPoolDefinition", BindingFlags.Public | BindingFlags.Static);
            Assert.That(enemyMap, Is.Not.Null);
            Assert.That(formationMap, Is.Not.Null);
            Assert.That(poolMap, Is.Not.Null);

            var enemyDefinitions = new Dictionary<string, EnemyDefinition>(StringComparer.Ordinal);
            string enemyFolder = Path.Combine(Application.dataPath, "Data2", "EnemyData");
            foreach (string file in Directory.GetFiles(enemyFolder, "*.asset"))
            {
                string path = "Assets/Data2/EnemyData/" + Path.GetFileName(file);
                object asset = load.Invoke(null, new object[] { path, enemyDataType });
                if (asset == null) continue;
                EnemyDefinition definition = (EnemyDefinition)enemyMap.Invoke(null, new[] { asset });
                Assert.That(enemyDefinitions.ContainsKey(definition.DefinitionId), Is.False);
                enemyDefinitions.Add(definition.DefinitionId, definition);
            }
            Assert.That(enemyDefinitions.Count, Is.EqualTo(44));

            var formationDefinitions = new Dictionary<string, EnemyFormationDefinition>(StringComparer.Ordinal);
            string formationFolder = Path.Combine(Application.dataPath, "Data2", "EnemyFormationData");
            foreach (string file in Directory.GetFiles(formationFolder, "*.asset"))
            {
                string path = "Assets/Data2/EnemyFormationData/" + Path.GetFileName(file);
                object asset = load.Invoke(null, new object[] { path, formationDataType });
                if (asset == null) continue;
                string id = (string)GetField(formationDataType, asset, "formationId");
                EnemyFormationDataSlotSnapshot before = SnapshotFormation(formationDataType, asset);
                EnemyFormationDefinition mapped = (EnemyFormationDefinition)formationMap.Invoke(null,
                    new object[] { asset, enemyDefinitions });
                Assert.That(mapped.Slots.Count, Is.EqualTo(6));
                Assert.That(SnapshotFormation(formationDataType, asset).Equals(before), Is.True, id + " asset changed");
                Assert.That(formationDefinitions.ContainsKey(mapped.FormationId), Is.False);
                formationDefinitions.Add(mapped.FormationId, mapped);
            }
            Assert.That(formationDefinitions.Count, Is.EqualTo(101));

            string scenePath = Path.Combine(Application.dataPath, "Scenes", "shop.unity");
            string scene = File.ReadAllText(scenePath);
            Match section = Regex.Match(scene, @"(?ms)^  encounterPoolData:\s*(.*?)^  enemyDataBySlot:");
            Assert.That(section.Success, Is.True, "Saved shop Scene must have an encounterPoolData section.");
            MatchCollection guids = Regex.Matches(section.Groups[1].Value, @"guid:\s*([a-fA-F0-9]{32})");
            Assert.That(guids.Count, Is.EqualTo(26));
            var mappedScenePools = new List<EncounterPoolDefinition>(guids.Count);
            foreach (Match guid in guids)
            {
                string path = (string)guidToPath.Invoke(null, new object[] { guid.Groups[1].Value });
                Assert.That(path, Is.Not.Empty);
                object asset = load.Invoke(null, new object[] { path, poolDataType });
                string originalId = (string)GetField(poolDataType, asset, "encounterId");
                string originalCategory = (string)GetField(poolDataType, asset, "category");
                int[] originalRounds = (int[])GetField(poolDataType, asset, "roundIndices");
                originalRounds = originalRounds == null ? null : (int[])originalRounds.Clone();
                EncounterPoolDefinition mapped = (EncounterPoolDefinition)poolMap.Invoke(null,
                    new object[] { asset, formationDefinitions });
                mappedScenePools.Add(mapped);
                Assert.That(GetField(poolDataType, asset, "encounterId"), Is.EqualTo(originalId));
                Assert.That(GetField(poolDataType, asset, "category"), Is.EqualTo(originalCategory));
                CollectionAssert.AreEqual(originalRounds, (int[])GetField(poolDataType, asset, "roundIndices"));
                Assert.That(mapped.Formations.Count, Is.GreaterThan(0), mapped.PoolId);
                Array sourceFormations = (Array)GetField(poolDataType, asset, "formations");
                Assert.That(mapped.Formations.Count, Is.EqualTo(sourceFormations.Length));
                for (int i = 0; i < sourceFormations.Length; i++)
                    Assert.That(mapped.Formations[i].FormationId,
                        Is.EqualTo(GetField(formationDataType, sourceFormations.GetValue(i), "formationId")));
                if (mapped.PoolId == "P026")
                {
                    Assert.That(mapped.Category, Is.EqualTo(StageEnemyCategory.Hard));
                    CollectionAssert.AreEqual(new[] { 3 }, mapped.RoundIndices);
                    Assert.That(mapped.Formations.Count, Is.EqualTo(1));
                }
            }

            Assert.That(mappedScenePools.Exists(pool => pool.PoolId == "P026"), Is.True);
            var hardPoolsAtThree = mappedScenePools.FindAll(pool =>
                pool.Category == StageEnemyCategory.Hard && pool.ContainsRound(3));
            Assert.That(hardPoolsAtThree.Count, Is.EqualTo(1));
            Assert.That(hardPoolsAtThree[0].PoolId, Is.EqualTo("P026"));
            Pcg32State checkpoint = new Pcg32RandomSource(101, 17).CaptureState();
            for (int encounterIndex = 0; encounterIndex < 25; encounterIndex++)
            {
                StageProgressState normalOrBoss = PreparationAt(encounterIndex, StageChoice.SelectA);
                EncounterGenerationResult reachable = EncounterGenerationRules.Generate(
                    normalOrBoss, mappedScenePools, checkpoint);
                Assert.That(reachable.Succeeded, Is.True,
                    "No saved Scene candidate for encounter " + encounterIndex + " category " +
                    normalOrBoss.CurrentSelection.EnemyCategory);
                if (Config().GetRoundNumber(encounterIndex) == 5)
                {
                    StageProgressState hard = PreparationAt(encounterIndex, StageChoice.SelectC);
                    EncounterGenerationResult hardResult = EncounterGenerationRules.Generate(
                        hard, mappedScenePools, checkpoint);
                    Assert.That(hardResult.Succeeded, Is.True,
                        "No saved Scene Hard candidate for reachable encounter " + encounterIndex);
                }
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static object GetField(Type type, object target, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Missing field " + type.Name + "." + fieldName);
            return field.GetValue(target);
        }

        private static void SetField(Type type, object target, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Missing field " + type.Name + "." + fieldName);
            field.SetValue(target, value);
        }

        private static EnemyFormationDataSlotSnapshot SnapshotFormation(Type type, object asset)
        {
            Array slots = (Array)GetField(type, asset, "slots");
            var values = new List<string>();
            foreach (object slot in slots)
            {
                if (slot == null) { values.Add("<null>"); continue; }
                Type slotType = slot.GetType();
                object enemy = GetField(slotType, slot, "enemy");
                values.Add((enemy == null ? "" : (string)GetField(enemy.GetType(), enemy, "enemyId")) + ":" +
                    GetField(slotType, slot, "gap") + ":" + GetField(slotType, slot, "cooldown"));
            }
            return new EnemyFormationDataSlotSnapshot(string.Join("|", values));
        }

        private sealed class EnemyFormationDataSlotSnapshot
        {
            private readonly string _value;
            public EnemyFormationDataSlotSnapshot(string value) { _value = value; }
            public override bool Equals(object obj) => obj is EnemyFormationDataSlotSnapshot other && _value == other._value;
            public override int GetHashCode() => _value.GetHashCode();
        }
    }
}

