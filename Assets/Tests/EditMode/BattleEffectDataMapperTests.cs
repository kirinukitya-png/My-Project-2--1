using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Battle;
using UnityEngine;

namespace ProjectNST.GameCore.Tests
{
    public sealed class BattleEffectDataMapperTests
    {
        private static Type EffectDataType => FindType("EffectData");
        private static Type MapperType => FindType("ProjectNST.GameUnityAdapter.BattleEffectDataMapper");

        [Test]
        public void AllEffectDataResourcesMapAndKeepTheirSourceValues()
        {
            Type dataType = EffectDataType;
            Assert.That(dataType, Is.Not.Null, "EffectData type was not found in loaded Unity assemblies.");
            object[] assets = Resources.LoadAll("EffectData", dataType);
            Assert.That(assets, Has.Length.EqualTo(10), "Expected the ten configured EffectData assets.");
            var before = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (object asset in assets)
                before.Add(GetField<string>(asset, "effectId"), new[]
                {
                    GetField<string>(asset, "effect"), GetField<int>(asset, "effectValue").ToString(),
                    GetField<string>(asset, "target"), GetField<string>(asset, "duration")
                });

            IReadOnlyDictionary<string, BattleEffectDefinition> mapped = MapAll(assets);
            Assert.That(mapped.Count, Is.EqualTo(10));
            for (int i = 1; i <= 10; i++)
            {
                string id = "AE" + i.ToString("000");
                Assert.That(mapped.ContainsKey(id), Is.True, "Missing mapping for " + id);
                Assert.That(Array.Exists(assets, asset => GetField<string>(asset, "effectId") == id), Is.True);
            }
            foreach (object asset in assets)
            {
                string id = GetField<string>(asset, "effectId");
                Assert.That(GetField<string>(asset, "effect"), Is.EqualTo(before[id][0]));
                Assert.That(GetField<int>(asset, "effectValue").ToString(), Is.EqualTo(before[id][1]));
                Assert.That(GetField<string>(asset, "target"), Is.EqualTo(before[id][2]));
                Assert.That(GetField<string>(asset, "duration"), Is.EqualTo(before[id][3]));
            }
        }

        [TestCase("AE001", BattleEffectType.Defense, -1, BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)]
        [TestCase("AE002", BattleEffectType.Gold, -1, BattleEffectTarget.Player, BattleEffectDuration.Immediate)]
        [TestCase("AE003", BattleEffectType.Gap, 1, BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)]
        [TestCase("AE004", BattleEffectType.Attack, 1, BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)]
        [TestCase("AE005", BattleEffectType.Attack, -1, BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)]
        [TestCase("AE006", BattleEffectType.Defense, 2, BattleEffectTarget.TriggerActor, BattleEffectDuration.Battle)]
        [TestCase("AE007", BattleEffectType.Block, -1, BattleEffectTarget.Player, BattleEffectDuration.Battle)]
        [TestCase("AE008", BattleEffectType.Defense, -2, BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)]
        [TestCase("AE009", BattleEffectType.Gap, 2, BattleEffectTarget.AttackTargets, BattleEffectDuration.Battle)]
        [TestCase("AE010", BattleEffectType.Gold, -3, BattleEffectTarget.Player, BattleEffectDuration.Immediate)]
        public void AeAssetValuesMatchTheConfirmedDefinitions(string id, BattleEffectType effect,
            int value, BattleEffectTarget target, BattleEffectDuration duration)
        {
            object asset = Array.Find(Resources.LoadAll("EffectData", EffectDataType),
                item => GetField<string>(item, "effectId") == id);
            Assert.That(asset, Is.Not.Null, "Missing EffectData asset " + id);
            BattleEffectDefinition definition = Map(asset);
            Assert.That(definition.EffectId, Is.EqualTo(id));
            Assert.That(definition.Effect, Is.EqualTo(effect));
            Assert.That(definition.Value, Is.EqualTo(value));
            Assert.That(definition.Target, Is.EqualTo(target));
            Assert.That(definition.Duration, Is.EqualTo(duration));
        }

        [Test]
        public void MapperRejectsUnknownEffectTargetAndDurationValues()
        {
            Type dataType = EffectDataType;
            Assert.That(dataType, Is.Not.Null);
            ScriptableObject asset = ScriptableObject.CreateInstance(dataType);
            try
            {
                SetField(asset, "effectId", "INVALID");
                SetField(asset, "effect", "NotAnEffect");
                SetField(asset, "target", "プレイヤー");
                SetField(asset, "duration", "即時効果");
                AssertMapperThrows<ArgumentException>(() => Map(asset));
                SetField(asset, "effect", "Gold");
                SetField(asset, "target", "unknown");
                AssertMapperThrows<ArgumentException>(() => Map(asset));
                SetField(asset, "target", "プレイヤー");
                SetField(asset, "duration", "unknown");
                AssertMapperThrows<ArgumentException>(() => Map(asset));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void DefinitionMapRejectsDuplicateEffectIds()
        {
            object[] assets = Resources.LoadAll("EffectData", EffectDataType);
            Assert.That(assets.Length, Is.GreaterThan(0));
            AssertMapperThrows<ArgumentException>(() => MapAll(new[] { assets[0], assets[0] }));
        }

        private static IReadOnlyDictionary<string, BattleEffectDefinition> MapAll(object[] assets)
        {
            MethodInfo method = MapperType?.GetMethod("ToDefinitionMap", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "BattleEffectDataMapper.ToDefinitionMap was not found.");
            return (IReadOnlyDictionary<string, BattleEffectDefinition>)method.Invoke(null,
                new object[] { ToTypedArray(assets) });
        }

        private static BattleEffectDefinition Map(object asset)
        {
            MethodInfo method = MapperType?.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "BattleEffectDataMapper.ToDefinition was not found.");
            return (BattleEffectDefinition)method.Invoke(null, new[] { asset });
        }

        private static void AssertMapperThrows<T>(TestDelegate action) where T : Exception
        {
            TargetInvocationException wrapped = Assert.Throws<TargetInvocationException>(action);
            Assert.That(wrapped.InnerException, Is.TypeOf<T>());
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

        private static Array ToTypedArray(object[] values)
        {
            Array typed = Array.CreateInstance(EffectDataType, values.Length);
            for (int i = 0; i < values.Length; i++) typed.SetValue(values[i], i);
            return typed;
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Missing EffectData field " + name);
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Missing EffectData field " + name);
            field.SetValue(target, value);
        }
    }
}
