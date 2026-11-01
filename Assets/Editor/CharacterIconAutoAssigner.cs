using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class CharacterIconAutoAssigner
{
    private const string IconFolder = "Assets/Art/ICON";

    [MenuItem("Tools/Character Data/Assign Icons By Name")]
    public static void AssignIconsByName()
    {
        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport
        );

        Dictionary<string, List<Sprite>> spritesByCharacterName =
            LoadSpritesByCharacterName();

        string[] characterGuids =
            AssetDatabase.FindAssets("t:CharacterData");

        int assignedCount = 0;
        int missingCount = 0;
        int ambiguousCount = 0;

        foreach (string guid in characterGuids)
        {
            string assetPath =
                AssetDatabase.GUIDToAssetPath(guid);

            CharacterData character =
                AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);

            if (character == null ||
                string.IsNullOrWhiteSpace(character.characterName))
            {
                continue;
            }

            if (!spritesByCharacterName.TryGetValue(
                character.characterName,
                out List<Sprite> matchingSprites))
            {
                Debug.LogWarning(
                    "アイコンが見つかりません: " +
                    character.characterName,
                    character
                );

                missingCount++;
                continue;
            }

            if (matchingSprites.Count != 1)
            {
                Debug.LogWarning(
                    "同名のアイコンが複数あるため割り当てません: " +
                    character.characterName,
                    character
                );

                ambiguousCount++;
                continue;
            }

            character.icon = matchingSprites[0];
            EditorUtility.SetDirty(character);
            assignedCount++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log(
            "アイコン割り当て完了: " + assignedCount +
            "件、未一致: " + missingCount +
            "件、重複: " + ambiguousCount +
            "件、画像内Sprite名: " + spritesByCharacterName.Count + "種類"
        );
    }

    private static Dictionary<string, List<Sprite>>
        LoadSpritesByCharacterName()
    {
        Dictionary<string, List<Sprite>> result =
            new Dictionary<string, List<Sprite>>();

        string[] assetGuids =
            AssetDatabase.FindAssets("", new[] { IconFolder });

        foreach (string guid in assetGuids)
        {
            string assetPath =
                AssetDatabase.GUIDToAssetPath(guid);

            if (!assetPath.EndsWith(
                ".png",
                System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName =
                Path.GetFileNameWithoutExtension(assetPath);

            int separatorIndex = fileName.IndexOf('_');
            if (separatorIndex < 0 ||
                separatorIndex == fileName.Length - 1)
            {
                continue;
            }

            string characterName =
                fileName.Substring(separatorIndex + 1);

            Sprite sprite =
                AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);

            if (sprite == null)
            {
                TextureImporter importer =
                    AssetImporter.GetAtPath(assetPath) as TextureImporter;

                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();

                    sprite =
                        AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                }
            }

            if (sprite == null)
            {
                Debug.LogWarning(
                    "画像はありますがSpriteとして読み込めません: " +
                    assetPath
                );

                continue;
            }

            if (!result.TryGetValue(
                characterName,
                out List<Sprite> matchingSprites))
            {
                matchingSprites = new List<Sprite>();
                result.Add(characterName, matchingSprites);
            }

            matchingSprites.Add(sprite);
        }

        return result;
    }
}
