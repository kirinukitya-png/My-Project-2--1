using System;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Characters;
using UnityEngine;

namespace ProjectNST.GameCore.Tests
{
    /// <summary>Exercises the real predefined-assembly mapper without adding an asmdef dependency on Assembly-CSharp.</summary>
    public sealed class CharacterDataMapperSynergyTests
    {
        [Test]
        public void MapperCopiesUnitAndAttributeTagsAndPreservesDefinitionMetadata()
        {
            Type characterDataType = FindProjectType("CharacterData");
            Type mapperType = FindProjectType("ProjectNST.GameUnityAdapter.CharacterDataMapper");
            if (characterDataType == null || mapperType == null)
                Assert.Ignore("Unity Assembly-CSharp is not loaded in this test host.");

            ScriptableObject source = ScriptableObject.CreateInstance(characterDataType);
            try
            {
                string[] attributes = { "Mage", "Mage", "  " };
                string[] units = { "UnitA", "UnitA" };
                SetField(characterDataType, source, "characterName", "Hero");
                SetField(characterDataType, source, "cost", 3);
                SetField(characterDataType, source, "attributes", attributes);
                SetField(characterDataType, source, "units", units);

                CharacterDefinition mapped = Map(mapperType, source, "asset-guid");
                Assert.That(mapped.DefinitionId, Is.EqualTo("asset-guid"));
                Assert.That(mapped.CharacterName, Is.EqualTo("Hero"));
                Assert.That(mapped.BaseCost, Is.EqualTo(3));
                CollectionAssert.AreEqual(attributes, mapped.Attributes);
                CollectionAssert.AreEqual(units, mapped.Units);
                Assert.That(GetField(characterDataType, source, "attributes"), Is.SameAs(attributes));
                Assert.That(GetField(characterDataType, source, "units"), Is.SameAs(units));

                attributes[0] = "Changed";
                units[0] = "Changed";
                Assert.That(mapped.Attributes[0], Is.EqualTo("Mage"));
                Assert.That(mapped.Units[0], Is.EqualTo("UnitA"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MapperTurnsNullAndEmptyTagsIntoEmptyGameCoreLists()
        {
            Type characterDataType = FindProjectType("CharacterData");
            Type mapperType = FindProjectType("ProjectNST.GameUnityAdapter.CharacterDataMapper");
            if (characterDataType == null || mapperType == null)
                Assert.Ignore("Unity Assembly-CSharp is not loaded in this test host.");

            ScriptableObject source = ScriptableObject.CreateInstance(characterDataType);
            try
            {
                SetField(characterDataType, source, "characterName", "Empty");
                SetField(characterDataType, source, "cost", 1);
                SetField(characterDataType, source, "attributes", null);
                SetField(characterDataType, source, "units", Array.Empty<string>());
                CharacterDefinition mapped = Map(mapperType, source, "empty-guid");
                Assert.That(mapped.Attributes, Is.Empty);
                Assert.That(mapped.Units, Is.Empty);
                Assert.That(mapped.BaseCost, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static Type FindProjectType(string fullName)
        {
            foreach (Assembly assembly in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
            {
                Type found = assembly.GetType(fullName, false);
                if (found != null) return found;
            }
            return null;
        }

        private static CharacterDefinition Map(Type mapperType, ScriptableObject source, string id)
        {
            MethodInfo method = mapperType.GetMethod("ToDefinition", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (CharacterDefinition)method.Invoke(null, new object[] { source, id });
        }

        private static void SetField(Type type, object target, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Missing CharacterData field " + fieldName);
            field.SetValue(target, value);
        }

        private static object GetField(Type type, object target, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Missing CharacterData field " + fieldName);
            return field.GetValue(target);
        }
    }
}
