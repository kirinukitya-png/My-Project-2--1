using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Enemies;
using UnityEngine;

namespace ProjectNST.GameCore.Tests
{
    public sealed class EnemyDefinitionStateTests
    {
        private static EnemyDefinition CreateDefinition(int attack = 5, int defense = 2, int health = 10)
        {
            return new EnemyDefinition("E001", "Enemy", attack, defense, health,
                new[] { "AE001", "AE002" }, new[] { "AE003" }, new[] { "AE004" },
                "T002,T005", new[] { "T002", "T005" }, new[] { 1, 3 }, new[] { "Normal", "Boss" });
        }

        [Test]
        public void DefinitionCopiesEveryFieldAndCollectionsAreReadOnly()
        {
            string[] cip = { "AE001" };
            int[] stages = { 1, 2 };
            string[] categories = { "Normal" };
            var definition = new EnemyDefinition("E9", "Test", -2, -1, -3,
                cip, null, Array.Empty<string>(), "T002", new[] { "T002" }, stages, categories);
            cip[0] = "changed";
            stages[0] = 99;
            categories[0] = "changed";

            Assert.That(definition.DefinitionId, Is.EqualTo("E9"));
            Assert.That(definition.DisplayName, Is.EqualTo("Test"));
            Assert.That(definition.Attack, Is.EqualTo(-2));
            Assert.That(definition.Defense, Is.EqualTo(-1));
            Assert.That(definition.Health, Is.EqualTo(-3));
            Assert.That(definition.CipEffectIds[0], Is.EqualTo("AE001"));
            Assert.That(definition.Stages[0], Is.EqualTo(1));
            Assert.That(definition.Categories[0], Is.EqualTo("Normal"));
            Assert.Throws<NotSupportedException>(() => ((IList<string>)definition.CipEffectIds)[0] = "mutated");
        }

        [Test]
        public void DefinitionRejectsMissingIdentityAndInvalidCollectionValues()
        {
            Assert.Throws<ArgumentException>(() => new EnemyDefinition(" ", "Enemy", 1, 1, 1,
                null, null, null, "T002", null, null, null));
            Assert.Throws<ArgumentException>(() => new EnemyDefinition("E1", " ", 1, 1, 1,
                null, null, null, "T002", null, null, null));
            Assert.Throws<ArgumentException>(() => new EnemyDefinition("E1", "Enemy", 1, 1, 1,
                new[] { " " }, null, null, "T002", null, null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyDefinition("E1", "Enemy", 1, 1, 1,
                null, null, null, "T002", null, new[] { -1 }, null));
        }

        [Test]
        public void FactoryAppliesCurrentInitialStatFormulaAndBuildsStableSlotIds()
        {
            EnemyDefinition definition = CreateDefinition();
            Assert.That(EnemyInstanceFactory.TryCreate(definition, 8, 2,
                3, -4, -20, 2, -1, 4, out EnemyInstance first, out EnemyInstanceFailure failure), Is.True);
            Assert.That(failure, Is.EqualTo(EnemyInstanceFailure.None));
            Assert.That(first.InstanceId, Is.EqualTo("enemy:encounter:8:slot:2"));
            Assert.That(first.DefinitionId, Is.EqualTo("E001"));
            Assert.That(first.SlotIndex, Is.EqualTo(2));
            Assert.That(first.Attack, Is.EqualTo(8));
            Assert.That(first.Defense, Is.EqualTo(-2));
            Assert.That(first.MaxHealth, Is.Zero);
            Assert.That(first.CurrentHealth, Is.Zero);
            Assert.That(first.Gap, Is.EqualTo(1));
            Assert.That(first.Cooldown, Is.EqualTo(4));
            Assert.That(first.InitialActionRound, Is.EqualTo(2));

            Assert.That(EnemyInstanceFactory.TryCreate(definition, 8, 3,
                3, -4, -20, 2, -1, 4, out EnemyInstance second, out _), Is.True);
            Assert.That(second.InstanceId, Is.Not.EqualTo(first.InstanceId));
            Assert.That(second.InstanceId, Is.EqualTo(EnemyInstanceFactory.CreateDeterministicId(8, 3)));
            Assert.That(first.InstanceId, Is.EqualTo(EnemyInstanceFactory.CreateDeterministicId(8, 2)));
        }

        [Test]
        public void FactoryRejectsInvalidSlotAndArithmeticOverflowWithoutPartialInstance()
        {
            Assert.That(EnemyInstanceFactory.TryCreate(CreateDefinition(), 0, 6, 0, 0, 0, 0, 0, 0,
                out EnemyInstance invalid, out EnemyInstanceFailure slotFailure), Is.False);
            Assert.That(invalid, Is.Null);
            Assert.That(slotFailure, Is.EqualTo(EnemyInstanceFailure.InvalidSlotIndex));
            Assert.That(EnemyInstanceFactory.TryCreate(CreateDefinition(int.MaxValue), 0, 0, 1, 0, 0, 0, 0, 0,
                out EnemyInstance overflow, out EnemyInstanceFailure overflowFailure), Is.False);
            Assert.That(overflow, Is.Null);
            Assert.That(overflowFailure, Is.EqualTo(EnemyInstanceFailure.ArithmeticOverflow));
        }

        [Test]
        public void EnemyBoardHasSixSlotsAndFunctionalPlacementCloneAndClear()
        {
            EnemyBoardState empty = EnemyBoardState.CreateEmpty();
            Assert.That(empty.Columns, Is.EqualTo(2));
            Assert.That(empty.Rows, Is.EqualTo(3));
            Assert.That(empty.SlotCount, Is.EqualTo(6));
            Assert.That(empty.TryGetAt(0, out EnemyInstance emptySlot), Is.True);
            Assert.That(emptySlot, Is.Null);
            Assert.That(empty.TryGetAt(6, out _), Is.False);

            Assert.That(EnemyInstanceFactory.TryCreate(CreateDefinition(), 1, 2, 0, 0, 0, 0, 0, 0,
                out EnemyInstance enemy, out _), Is.True);
            EnemyBoardOperationResult placed = empty.TryPlace(enemy);
            Assert.That(placed.Succeeded, Is.True);
            Assert.That(empty.TryGetAt(2, out _), Is.True);
            Assert.That(empty.TryGetAt(2, out emptySlot), Is.True);
            Assert.That(emptySlot, Is.Null, "Input state remains unchanged.");
            Assert.That(placed.NextState.TryGetAt(2, out EnemyInstance actual), Is.True);
            Assert.That(actual, Is.SameAs(enemy));
            Assert.That(placed.NextState.TryFindInstance(enemy.InstanceId, out int foundAt), Is.True);
            Assert.That(foundAt, Is.EqualTo(2));

            EnemyBoardState clone = placed.NextState.Clone();
            EnemyBoardState clearedNextStage = clone.ClearForNextStage();
            EnemyBoardState clearedCompleted = clone.ClearForCompleted();
            Assert.That(clearedNextStage.TryGetAt(2, out actual), Is.True);
            Assert.That(actual, Is.Null);
            Assert.That(clearedCompleted.TryGetAt(2, out actual), Is.True);
            Assert.That(actual, Is.Null);
            Assert.That(clone.TryGetAt(2, out actual), Is.True);
            Assert.That(actual, Is.SameAs(enemy));
        }

        [Test]
        public void EnemyBoardRejectsDuplicateInstanceAndOccupiedSlotAtomically()
        {
            Assert.That(EnemyInstanceFactory.TryCreate(CreateDefinition(), 2, 1, 0, 0, 0, 0, 0, 0,
                out EnemyInstance enemy, out _), Is.True);
            EnemyBoardOperationResult placed = EnemyBoardState.CreateEmpty().TryPlace(enemy);
            EnemyBoardOperationResult duplicate = placed.NextState.TryPlace(enemy);
            Assert.That(duplicate.Succeeded, Is.False);
            Assert.That(duplicate.Failure, Is.EqualTo(EnemyBoardFailure.DuplicateInstanceId));
            Assert.That(duplicate.NextState, Is.SameAs(placed.NextState));
            Assert.That(EnemyBoardState.TryCreate(2, 3, new[] { enemy, enemy },
                out EnemyBoardState invalid, out EnemyBoardFailure failure), Is.False);
            Assert.That(invalid, Is.Null);
            Assert.That(failure, Is.EqualTo(EnemyBoardFailure.DuplicateInstanceId));
            Assert.That(EnemyBoardState.TryCreate(2, 3, new[] { enemy },
                out EnemyBoardState valid, out failure), Is.True);
            Assert.That(valid.TryGetAt(6, out _), Is.False);
            Assert.That(EnemyBoardState.TryCreate(0, 3, null, out _, out failure), Is.False);
            Assert.That(failure, Is.EqualTo(EnemyBoardFailure.InvalidDimensions));
        }

        [Test]
        public void MapperMapsAllSavedEnemyAssetsAndKeepsAssetIdsUnique()
        {
            Type enemyType = FindType("EnemyData");
            Type mapperType = FindType("ProjectNST.GameUnityAdapter.EnemyDataMapper");
            MethodInfo load = FindAssetLoadMethod();
            if (enemyType == null || mapperType == null || load == null)
                Assert.Ignore("Unity EnemyData mapper or AssetDatabase is unavailable in this test host.");

            string directory = Path.Combine(Application.dataPath, "Data2", "EnemyData");
            var paths = Directory.GetFiles(directory, "*.asset");
            MethodInfo map = mapperType.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
            Assert.That(map, Is.Not.Null);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int mappedCount = 0;
            foreach (string fullPath in paths)
            {
                string path = "Assets/Data2/EnemyData/" + Path.GetFileName(fullPath);
                object asset = load.Invoke(null, new object[] { path, enemyType });
                Assert.That(asset, Is.Not.Null, "Missing asset: " + path);
                EnemyDefinition definition = (EnemyDefinition)map.Invoke(null, new[] { asset });
                Assert.That(definition.DefinitionId, Is.Not.Empty);
                Assert.That(ids.Add(definition.DefinitionId), Is.True, "Duplicate Enemy ID: " + definition.DefinitionId);
                Assert.That(definition.DisplayName, Is.Not.Empty);
                Assert.That(definition.TargetIds, Is.Not.Empty);
                mappedCount++;
            }
            Assert.That(mappedCount, Is.EqualTo(44));
        }

        [Test]
        public void MapperRejectsUnknownTargetReferenceWithoutChangingAsset()
        {
            Type enemyType = FindType("EnemyData");
            Type mapperType = FindType("ProjectNST.GameUnityAdapter.EnemyDataMapper");
            if (enemyType == null || mapperType == null)
                Assert.Ignore("Unity EnemyData mapper is unavailable in this test host.");
            ScriptableObject source = ScriptableObject.CreateInstance(enemyType);
            try
            {
                SetField(enemyType, source, "enemyId", "E-TEST");
                SetField(enemyType, source, "enemyName", "Test");
                SetField(enemyType, source, "target", "T999");
                MethodInfo map = mapperType.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
                Assert.Throws<TargetInvocationException>(() => map.Invoke(null, new object[] { source }));
                Assert.That(GetField(enemyType, source, "target"), Is.EqualTo("T999"));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
            {
                Type found = assembly.GetType(fullName, false);
                if (found != null) return found;
            }
            return null;
        }

        private static MethodInfo FindAssetLoadMethod()
        {
            Type assetDatabase = FindType("UnityEditor.AssetDatabase");
            return assetDatabase?.GetMethod("LoadAssetAtPath", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(Type) }, null);
        }

        private static object GetField(Type type, object target, string name)
        {
            return type.GetField(name, BindingFlags.Public | BindingFlags.Instance).GetValue(target);
        }

        private static void SetField(Type type, object target, string name, object value)
        {
            type.GetField(name, BindingFlags.Public | BindingFlags.Instance).SetValue(target, value);
        }
    }
}
