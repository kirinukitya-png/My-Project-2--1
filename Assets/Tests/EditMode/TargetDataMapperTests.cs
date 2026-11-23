using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Battle;
using UnityEngine;

namespace ProjectNST.GameCore.Tests
{
    public sealed class TargetDataMapperTests
    {
        private static Type TargetDataType => Type.GetType("TargetData, Assembly-CSharp");
        private static Type MapperType => Type.GetType("ProjectNST.GameUnityAdapter.TargetDataMapper, Assembly-CSharp");

        [Test]
        public void MapsAllTenSavedTargetAssetsWithoutChangingThem()
        {
            Type sourceType = TargetDataType;
            Type mapperType = MapperType;
            if (sourceType == null || mapperType == null)
                Assert.Ignore("Assembly-CSharp TargetData mapper is not loaded in this test host.");
            UnityEngine.Object[] assets = Resources.LoadAll("TargetData", sourceType);
            Assert.That(assets.Length, Is.EqualTo(10));
            MethodInfo map = mapperType.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
            Assert.That(map, Is.Not.Null);
            var mapped = assets.Select(asset => (BattleTargetDefinition)map.Invoke(null, new[] { asset }))
                .OrderBy(definition => definition.TargetId, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(Enumerable.Range(1, 10).Select(i => "T" + i.ToString("000")),
                mapped.Select(definition => definition.TargetId));

            Assert.That(mapped[0].Filter, Is.EqualTo(BattleTargetFilter.AllBoard));
            Assert.That(mapped[0].Selection, Is.EqualTo(BattleTargetSelection.Uniform));
            Assert.That(mapped[1].Weights.Count, Is.EqualTo(3));
            Assert.That(mapped[2].Weights[0].Position, Is.EqualTo(BattleTargetPosition.Back));
            Assert.That(mapped[3].Weights[0].Position, Is.EqualTo(BattleTargetPosition.SameEnemyRow));
            Assert.That(mapped[4].Filter, Is.EqualTo(BattleTargetFilter.FrontColumn));
            Assert.That(mapped[5].Filter, Is.EqualTo(BattleTargetFilter.MiddleColumn));
            Assert.That(mapped[6].Filter, Is.EqualTo(BattleTargetFilter.BackColumn));
            Assert.That(mapped[7].Filter, Is.EqualTo(BattleTargetFilter.SameEnemyRow));
            Assert.That(mapped[8].Filter, Is.EqualTo(BattleTargetFilter.RandomCells));
            Assert.That(mapped[8].CellCount, Is.EqualTo(2));
            Assert.That(mapped[8].CellSelection, Is.EqualTo(BattleTargetSelection.Uniform));
            Assert.That(mapped[9].CellCount, Is.EqualTo(2));
            Assert.That(mapped[9].CellSelection, Is.EqualTo(BattleTargetSelection.Weighted));
            Assert.That(mapped[9].CellWeights[0].Weight, Is.EqualTo(3));
            Assert.That(mapped[9].CellWeights[2].Weight, Is.Zero);

            foreach (UnityEngine.Object asset in assets)
            {
                string id = (string)sourceType.GetField("targetId").GetValue(asset);
                Assert.That(id, Is.EqualTo(asset.name));
                Assert.That(sourceType.GetField("candidateFilter").GetValue(asset), Is.Not.Null);
                Assert.That(sourceType.GetField("selectionMethod").GetValue(asset), Is.Not.Null);
            }
        }

        [Test]
        public void MapperRejectsUnknownTargetStringsAndInvalidCounts()
        {
            Type sourceType = TargetDataType;
            Type mapperType = MapperType;
            if (sourceType == null || mapperType == null)
                Assert.Ignore("Assembly-CSharp TargetData mapper is not loaded in this test host.");
            ScriptableObject source = ScriptableObject.CreateInstance(sourceType);
            try
            {
                sourceType.GetField("targetId").SetValue(source, "TX");
                sourceType.GetField("candidateFilter").SetValue(source, "unknown");
                sourceType.GetField("selectionMethod").SetValue(source, "全員選択");
                MethodInfo map = mapperType.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
                TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => map.Invoke(null, new object[] { source }));
                Assert.That(exception.InnerException, Is.TypeOf<FormatException>());

                sourceType.GetField("candidateFilter").SetValue(source, "均等抽選,0");
                exception = Assert.Throws<TargetInvocationException>(() => map.Invoke(null, new object[] { source }));
                Assert.That(exception.InnerException, Is.TypeOf<FormatException>());
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
