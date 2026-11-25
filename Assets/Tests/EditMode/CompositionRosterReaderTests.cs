using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using UnityEngine;

namespace ProjectNST.GameCore.Tests
{
    public sealed class CompositionRosterReaderTests
    {
        private static Type FindEditorType(string name)
        {
            foreach (Assembly assembly in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            Assert.Fail("Editor type not loaded: " + name);
            return null;
        }

        private static object Parse(string json)
        {
            Type reader = FindEditorType("CompositionRosterDataReader");
            MethodInfo method = reader.GetMethod("ParseJson", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { json, "test" });
        }

        private static string Json(params string[] names)
        {
            var slots = new List<string>();
            for (int i = 0; i < names.Length; i++)
                slots.Add("{\"slotIndex\":" + i + ",\"characterAssetName\":\"" + names[i] + "\"}");
            return "{\"formationName\":\"Fixture\",\"slots\":[" + string.Join(",", slots) + "]}";
        }

        private static bool Succeeded(object result) => (bool)result.GetType().GetProperty("Succeeded").GetValue(result);
        private static string Error(object result) => (string)result.GetType().GetProperty("Error").GetValue(result);
        private static object Document(object result) => result.GetType().GetProperty("Document").GetValue(result);

        [Test]
        public void ValidDeckIsReadInSlotOrderRegardlessOfJsonEntryOrder()
        {
            string[] names = Enumerable.Range(0, 30).Select(i => "hero-" + i).ToArray();
            string[] reversedEntries = Enumerable.Range(0, 30).Reverse()
                .Select(i => "{\"slotIndex\":" + i + ",\"characterAssetName\":\"hero-" + i + "\"}").ToArray();
            object result = Parse("{\"formationName\":\"Alpha\",\"slots\":[" + string.Join(",", reversedEntries) + "]}");
            Assert.That(Succeeded(result), Is.True, Error(result));
            object document = Document(result);
            Assert.That(document.GetType().GetProperty("FormationName").GetValue(document), Is.EqualTo("Alpha"));
            CollectionAssert.AreEqual(names,
                (IEnumerable<string>)document.GetType().GetProperty("CharacterAssetNames").GetValue(document));
        }

        [Test]
        public void InvalidAndIncompleteDeckDocumentsAreRejected()
        {
            Assert.That(Succeeded(Parse("{broken")), Is.False);
            Assert.That(Succeeded(Parse("{\"slots\":[]}")), Is.False);
            Assert.That(Succeeded(Parse(Json(Enumerable.Range(0, 29).Select(i => "h" + i).ToArray()))), Is.False);
        }

        [Test]
        public void DuplicateSlotsEmptyMembersAndDuplicateCharactersAreRejected()
        {
            string[] names = Enumerable.Range(0, 30).Select(i => "h" + i).ToArray();
            string[] duplicateSlotEntries = Enumerable.Range(0, 30)
                .Select(i => "{\"slotIndex\":" + (i == 29 ? 28 : i) + ",\"characterAssetName\":\"h" + i + "\"}").ToArray();
            Assert.That(Succeeded(Parse("{\"slots\":[" + string.Join(",", duplicateSlotEntries) + "]}")), Is.False);
            string[] outOfRangeEntries = Enumerable.Range(0, 30)
                .Select(i => "{\"slotIndex\":" + (i == 29 ? 30 : i) + ",\"characterAssetName\":\"h" + i + "\"}").ToArray();
            Assert.That(Succeeded(Parse("{\"slots\":[" + string.Join(",", outOfRangeEntries) + "]}")), Is.False);
            names[7] = string.Empty;
            Assert.That(Succeeded(Parse(Json(names))), Is.False);
            names[7] = names[6];
            Assert.That(Succeeded(Parse(Json(names))), Is.False);
        }

        [Test]
        public void MissingPlayerPrefsDeckIsReportedAndLegacyReadDoesNotMigrateOrMutate()
        {
            Type reader = FindEditorType("CompositionRosterDataReader");
            string v2 = (string)reader.GetField("V2Key", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            string legacy = (string)reader.GetField("LegacyV1Key", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            bool hadV2 = PlayerPrefs.HasKey(v2), hadLegacy = PlayerPrefs.HasKey(legacy);
            string oldV2 = hadV2 ? PlayerPrefs.GetString(v2) : null;
            string oldLegacy = hadLegacy ? PlayerPrefs.GetString(legacy) : null;
            try
            {
                PlayerPrefs.DeleteKey(v2);
                PlayerPrefs.DeleteKey(legacy);
                MethodInfo read = reader.GetMethod("ReadFromPlayerPrefs", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(Succeeded(read.Invoke(null, null)), Is.False);

                PlayerPrefs.SetString(legacy, Json(Enumerable.Range(0, 30).Select(i => "h" + i).ToArray()));
                object legacyResult = read.Invoke(null, null);
                Assert.That(Succeeded(legacyResult), Is.True, Error(legacyResult));
                Assert.That((string)legacyResult.GetType().GetProperty("SourceKey").GetValue(legacyResult), Does.Contain("read-only"));
                Assert.That(PlayerPrefs.HasKey(v2), Is.False, "Reading legacy data must not write/migrate PlayerPrefs.");
            }
            finally
            {
                PlayerPrefs.DeleteKey(v2);
                PlayerPrefs.DeleteKey(legacy);
                if (hadV2) PlayerPrefs.SetString(v2, oldV2);
                if (hadLegacy) PlayerPrefs.SetString(legacy, oldLegacy);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void UnknownAmbiguousAndMissingDefinitionIdsAreRejected()
        {
            string[] names = Enumerable.Range(0, 30).Select(i => "h" + i).ToArray();
            object parsed = Parse(Json(names));
            Type reader = FindEditorType("CompositionRosterDataReader");
            Type reference = FindEditorType("CompositionRosterAssetReference");
            MethodInfo resolve = reader.GetMethod("ResolveDefinitionIds", BindingFlags.Static | BindingFlags.NonPublic);
            Array candidates = Array.CreateInstance(reference, 30);
            for (int i = 0; i < 30; i++)
                candidates.SetValue(Activator.CreateInstance(reference, names[i], "def-" + i), i);

            object resolved = resolve.Invoke(null, new object[] { Document(parsed), candidates });
            Assert.That(Succeeded(resolved), Is.True, Error(resolved));
            CollectionAssert.AreEqual(Enumerable.Range(0, 30).Select(i => "def-" + i),
                (IEnumerable<string>)resolved.GetType().GetProperty("DefinitionIds").GetValue(resolved));

            Array missing = Array.CreateInstance(reference, 29);
            for (int i = 0; i < missing.Length; i++) missing.SetValue(candidates.GetValue(i), i);
            object unknown = resolve.Invoke(null, new object[] { Document(parsed), missing });
            Assert.That(Succeeded(unknown), Is.False);

            Array ambiguous = Array.CreateInstance(reference, 31);
            for (int i = 0; i < 30; i++) ambiguous.SetValue(candidates.GetValue(i), i);
            ambiguous.SetValue(Activator.CreateInstance(reference, "h0", "different-id"), 30);
            object duplicateName = resolve.Invoke(null, new object[] { Document(parsed), ambiguous });
            Assert.That(Succeeded(duplicateName), Is.False);

            candidates.SetValue(Activator.CreateInstance(reference, "h4", string.Empty), 4);
            object missingId = resolve.Invoke(null, new object[] { Document(parsed), candidates });
            Assert.That(Succeeded(missingId), Is.False);
        }

        [Test]
        public void RosterFingerprintIsStableOrderedAndIncludedWithEventItemInConfigurationIds()
        {
            string[] ids = Enumerable.Range(0, 30).Select(i => "character-data:guid-" + i).ToArray();
            Type reader = FindEditorType("CompositionRosterDataReader");
            MethodInfo fingerprintMethod = reader.GetMethod("ComputeFingerprint", BindingFlags.Static | BindingFlags.NonPublic);
            string first = (string)fingerprintMethod.Invoke(null, new object[] { ids });
            string same = (string)fingerprintMethod.Invoke(null, new object[] { ids.ToArray() });
            string[] reordered = ids.ToArray();
            Array.Reverse(reordered);
            string changed = (string)fingerprintMethod.Invoke(null, new object[] { reordered });
            Assert.That(same, Is.EqualTo(first));
            Assert.That(changed, Is.Not.EqualTo(first));

            MethodInfo checkpoint = reader.GetMethod("CreateCheckpointConfigurationId", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo batch = reader.GetMethod("CreateBatchConfigurationId", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[]{typeof(string),typeof(string)}, null);
            string checkpointA = (string)checkpoint.Invoke(null, new object[] { first });
            string checkpointB = (string)checkpoint.Invoke(null, new object[] { changed });
            string batchId = (string)batch.Invoke(null, new object[] { first, "Item006" });
            Assert.That(checkpointA, Is.Not.EqualTo(checkpointB));
            Assert.That(batchId, Does.Contain(first));
            Assert.That(batchId, Does.Contain("event-item:Item006"));
            Assert.That(batchId, Does.Contain("HeuristicStrategy-v" + ProjectNST.GameCore.Strategy.HeuristicStrategy.StableVersion));
            Assert.That(batchId, Does.Contain("RandomStrategy-v" + ProjectNST.GameCore.Strategy.RandomStrategy.StableVersion));
            Assert.That(batchId, Does.EndWith("|pool-accounting:v2"));
        }

        [Test]
        public void BatchConfigurationIdIncludesThePlannedStrategyListExactlyOnce()
        {
            string fingerprint=(string)FindEditorType("CompositionRosterDataReader")
                .GetMethod("ComputeFingerprint",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{Enumerable.Range(0,30).Select(i=>"roster-"+i).ToArray()});
            Type reader=FindEditorType("CompositionRosterDataReader");
            MethodInfo method=reader.GetMethod("CreateBatchConfigurationId",BindingFlags.Static|BindingFlags.NonPublic,
                null,new[]{typeof(string),typeof(string),typeof(IEnumerable<string>)},null);
            string[] planned={"HeuristicStrategy-v9-purchasePower-ref3.5-board4-hand0.5"};
            string id=(string)method.Invoke(null,new object[]{fingerprint,"Item006",planned});
            Assert.That(id.Split(new[]{"|strategies:"},StringSplitOptions.None),Has.Length.EqualTo(2));
            Assert.That(id,Does.Contain("HeuristicStrategy-v9-purchasePower-ref3.5-board4-hand0.5"));
            Assert.That(id,Does.Not.Contain("RandomStrategy"));
            Assert.That(id,Does.EndWith("|pool-accounting:v2"));
            string compare=(string)method.Invoke(null,new object[]{fingerprint,"Item006",new[]{
                "HeuristicStrategy-v8","HeuristicStrategy-v9-purchasePower-ref3.5-board4-hand0.5"}});
            Assert.That(compare,Does.Contain("HeuristicStrategy-v8;HeuristicStrategy-v9"));
            Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,new object[]{fingerprint,"Item006",new[]{"HeuristicStrategy-v9","HeuristicStrategy-v9"}}));
        }

        [Test]
        public void FixedThirtyMemberRosterUsesDeterministicPerTrialCostAssignment()
        {
            CharacterDefinition[] roster = Enumerable.Range(0, 30)
                .Select(i => new CharacterDefinition("fixed-" + i, "Hero " + i, 1, null, null)).ToArray();
            var config = new CharacterShopConfiguration(new[] { 6, 6, 6, 6, 6 }, new[] { 15, 15, 15, 15, 15 },
                Enumerable.Range(0, 5).Select(_ => (IEnumerable<int>)new[] { 1, 1, 1, 1, 1 }),
                new[] { 3, 3, 3, 3, 3 });
            ulong seed = TrialSeedGenerator.Create(12345UL, 0UL).TrialSeed;
            AssignedCharacterCosts randomCosts = CharacterCostAssigner.Assign(roster, config,
                new SimulationRandomStreams(seed).Roster);
            AssignedCharacterCosts heuristicCosts = CharacterCostAssigner.Assign(roster.ToArray(), config,
                new SimulationRandomStreams(seed).Roster);
            AssignedCharacterCosts nextTrialCosts = CharacterCostAssigner.Assign(roster, config,
                new SimulationRandomStreams(TrialSeedGenerator.Create(12345UL, 1UL).TrialSeed).Roster);
            Assert.That(randomCosts.Count, Is.EqualTo(30));
            CollectionAssert.AreEquivalent(randomCosts.Costs, heuristicCosts.Costs);
            Assert.That(nextTrialCosts.Costs.Keys, Is.EquivalentTo(roster.Select(x => x.DefinitionId)),
                "Changing Trial Seed may change Cost assignments, but never the fixed 30-member roster.");
            for (int cost = 1; cost <= 5; cost++)
                Assert.That(randomCosts.Costs.Values.Count(value => value == cost), Is.EqualTo(6));
            Assert.That(randomCosts.Costs.Keys, Is.EquivalentTo(roster.Select(x => x.DefinitionId)));
        }
    }
}
