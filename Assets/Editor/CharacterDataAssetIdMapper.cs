using System;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameUnityAdapter;
using UnityEditor;

/// <summary>Editor-only convenience mapper using Unity's persistent asset GUID.</summary>
public static class CharacterDataAssetIdMapper
{
    public static CharacterDefinition ToDefinition(CharacterData source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        string path = AssetDatabase.GetAssetPath(source);
        string guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid))
            throw new InvalidOperationException("CharacterData must be a saved asset to obtain a stable DefinitionId.");
        return CharacterDataMapper.ToDefinition(source, "character-data:" + guid);
    }
}
