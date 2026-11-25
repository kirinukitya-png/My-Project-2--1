using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Shop;
using UnityEngine;

namespace ProjectNST.GameCore.Tests
{
    public sealed class ItemDataMapperTests
    {
        private static readonly string[] ExpectedIds =
            { "Item001", "Item002", "Item003", "Item004", "Item005", "Item006", "Item007" };

        [Test]
        public void MapsAllSevenSavedItemDataAssetsWithoutChangingThem()
        {
            Type itemDataType = FindType("ItemData");
            Type mapperType = FindType("ProjectNST.GameUnityAdapter.ItemDataMapper");
            MethodInfo load = FindAssetLoadMethod();
            if (itemDataType == null || mapperType == null || load == null)
                Assert.Ignore("Unity ItemData mapper or AssetDatabase is unavailable in this test host.");

            MethodInfo mapMethod = mapperType.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
            Assert.That(mapMethod, Is.Not.Null);
            var mapped = new List<ItemDefinition>();
            foreach (string id in ExpectedIds)
            {
                string path = "Assets/Data2/ItemData/" + id + ".asset";
                object source = load.Invoke(null, new object[] { path, itemDataType });
                Assert.That(source, Is.Not.Null, "Missing saved asset: " + path);
                ItemDefinition definition = (ItemDefinition)mapMethod.Invoke(null, new[] { source });
                mapped.Add(definition);
                Assert.That(definition.DefinitionId, Is.EqualTo(id));
                Assert.That(GetField(itemDataType, source, "itemId"), Is.EqualTo(id));
            }

            Assert.That(mapped.Count, Is.EqualTo(7));
            CollectionAssert.AreEqual(new[] { "おののおの", "丸フラスコ", "チャイカアーマー", "ダイカガミ", "お紅茶", "魔法のステッキ", "鍵" },
                mapped.ConvertAll(item => item.DisplayName));
            CollectionAssert.AreEqual(new[] { 3, 2, 3, 3, 4, 10, 5 },
                mapped.ConvertAll(item => item.PurchasePrice));
            CollectionAssert.AreEqual(new[]
            {
                ItemEffectTarget.OneAlly, ItemEffectTarget.AllEnemies, ItemEffectTarget.OneAlly,
                ItemEffectTarget.AllEnemies, ItemEffectTarget.Player, ItemEffectTarget.OneAlly,
                ItemEffectTarget.OneAlly
            }, mapped.ConvertAll(item => item.Target));
            CollectionAssert.AreEqual(new[]
            {
                ItemEffectDuration.ThisBattle, ItemEffectDuration.ThisBattle, ItemEffectDuration.ThisBattle,
                ItemEffectDuration.ThisBattle, ItemEffectDuration.Immediate, ItemEffectDuration.Immediate,
                ItemEffectDuration.Immediate
            }, mapped.ConvertAll(item => item.Duration));
            CollectionAssert.AreEqual(new[] { 1, 1, 2, 1, 1, 1, 1 },
                mapped.ConvertAll(item => item.Effects.Count));
            Assert.That(mapped[0].PurchasePrice, Is.EqualTo(3));
            Assert.That(mapped[0].Effects[0].Kind, Is.EqualTo(ItemEffectKind.Attack));
            Assert.That(mapped[0].Effects[0].Value, Is.EqualTo(3));
            Assert.That(mapped[0].Target, Is.EqualTo(ItemEffectTarget.OneAlly));
            Assert.That(mapped[0].Duration, Is.EqualTo(ItemEffectDuration.ThisBattle));

            Assert.That(mapped[1].Effects[0].Kind, Is.EqualTo(ItemEffectKind.Defense));
            Assert.That(mapped[1].Effects[0].Value, Is.EqualTo(-1), "Item002's numeric effect value is the approved DEF -1.");
            Assert.That(mapped[1].Target, Is.EqualTo(ItemEffectTarget.AllEnemies));

            Assert.That(mapped[2].Effects.Count, Is.EqualTo(2));
            Assert.That(mapped[2].Effects[0].Kind, Is.EqualTo(ItemEffectKind.Attack));
            Assert.That(mapped[2].Effects[1].Kind, Is.EqualTo(ItemEffectKind.Defense));
            Assert.That(mapped[2].Effects[0].Value, Is.EqualTo(2));
            Assert.That(mapped[2].Effects[1].Value, Is.EqualTo(2));
            Assert.That(mapped[3].Effects[0].Kind, Is.EqualTo(ItemEffectKind.Damage));
            Assert.That(mapped[4].Effects[0].Value, Is.EqualTo(-4));
            Assert.That(mapped[4].Target, Is.EqualTo(ItemEffectTarget.Player));
            Assert.That(mapped[4].Duration, Is.EqualTo(ItemEffectDuration.Immediate));
            Assert.That(mapped[5].Effects[0].Kind, Is.EqualTo(ItemEffectKind.Star));
            Assert.That(mapped[5].Effects[0].Value, Is.EqualTo(2));
            Assert.That(mapped[6].Effects[0].Value, Is.EqualTo(1));
        }

        [Test]
        public void MapperRejectsMalformedItemDataAndDoesNotModifyInput()
        {
            Type itemDataType = FindType("ItemData");
            Type mapperType = FindType("ProjectNST.GameUnityAdapter.ItemDataMapper");
            if (itemDataType == null || mapperType == null)
                Assert.Ignore("Unity ItemData mapper is unavailable in this test host.");

            ScriptableObject source = ScriptableObject.CreateInstance(itemDataType);
            try
            {
                SetField(itemDataType, source, "itemId", "Test");
                SetField(itemDataType, source, "itemName", "Test Item");
                SetField(itemDataType, source, "purchasePrice", 2);
                string[] effects = { "Def" };
                int[] values = { -1 };
                SetField(itemDataType, source, "effects", effects);
                SetField(itemDataType, source, "effectValues", values);
                SetField(itemDataType, source, "target", "全ての敵");
                SetField(itemDataType, source, "duration", "このバトル中");
                MethodInfo map = mapperType.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
                ItemDefinition mapped = (ItemDefinition)map.Invoke(null, new object[] { source });
                Assert.That(mapped.Effects[0].Value, Is.EqualTo(-1));
                Assert.That(GetField(itemDataType, source, "effects"), Is.SameAs(effects));
                Assert.That(GetField(itemDataType, source, "effectValues"), Is.SameAs(values));

                SetField(itemDataType, source, "effectValues", Array.Empty<int>());
                Assert.Throws<TargetInvocationException>(() => map.Invoke(null, new object[] { source }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void EventItemResolverUsesAssignedAssetBeforeConfiguredIdAndFallsBackToItem006()
        {
            Type itemDataType = FindType("ItemData");
            Type shopType = FindType("ShopManager");
            Type menuType = FindType("NSTSimulationBatchMenu");
            MethodInfo load = FindAssetLoadMethod();
            if (itemDataType == null || shopType == null || menuType == null || load == null)
                Assert.Ignore("Unity Editor ItemData resolver is unavailable in this test host.");

            object sceneItem006 = load.Invoke(null, new object[] { "Assets/Data2/ItemData/Item006.asset", itemDataType });
            object preferred = ScriptableObject.CreateInstance(itemDataType);
            SetField(itemDataType, preferred, "itemId", "preferred-event-item");
            GameObject holder = new GameObject("EventItemResolverTest");
            try
            {
                object shop = holder.AddComponent(shopType);
                SetField(shopType, shop, "eventItemData", preferred);
                SetField(shopType, shop, "eventItemId", "Item006");
                Array pool = Array.CreateInstance(itemDataType, 1);
                pool.SetValue(sceneItem006, 0);
                SetField(shopType, shop, "itemDataPool", pool);
                MethodInfo resolve = menuType.GetMethod("ResolveEventItemData", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(resolve, Is.Not.Null);
                Assert.That(resolve.Invoke(null, new[] { shop }), Is.SameAs(preferred),
                    "Assigned eventItemData must take precedence over eventItemId.");

                SetField(shopType, shop, "eventItemData", null);
                Assert.That(resolve.Invoke(null, new[] { shop }), Is.SameAs(sceneItem006),
                    "The saved scene's eventItemId Item006 must resolve from itemDataPool.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
                UnityEngine.Object.DestroyImmediate((UnityEngine.Object)preferred);
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

        private static MethodInfo FindAssetLoadMethod()
        {
            Type assetDatabase = FindType("UnityEditor.AssetDatabase");
            return assetDatabase?.GetMethod("LoadAssetAtPath", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(Type) }, null);
        }

        private static object GetField(Type type, object target, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Missing ItemData field " + name);
            return field.GetValue(target);
        }

        private static void SetField(Type type, object target, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Missing ItemData field " + name);
            field.SetValue(target, value);
        }
    }
}
