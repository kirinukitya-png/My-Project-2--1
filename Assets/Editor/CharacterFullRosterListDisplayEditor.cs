using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(CharacterFullRosterListDisplay))]
public class CharacterFullRosterListDisplayEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Assets/Dataから全キャラを登録"))
            AssignAllCharacters();
    }

    private void AssignAllCharacters()
    {
        string[] assetGuids = AssetDatabase.FindAssets("t:CharacterData", new[] { "Assets/Data" });
        List<CharacterData> characters = new List<CharacterData>();

        foreach (string guid in assetGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            CharacterData character = AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);
            if (character != null)
                characters.Add(character);
        }

        characters.Sort((a, b) => string.Compare(
            AssetDatabase.GetAssetPath(a),
            AssetDatabase.GetAssetPath(b),
            StringComparison.OrdinalIgnoreCase));

        if (characters.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "CharacterDataが見つかりません",
                "Assets/Data内にCharacterDataアセットがありません。",
                "OK");
            return;
        }

        CharacterFullRosterListDisplay display = (CharacterFullRosterListDisplay)target;
        Undo.RecordObject(display, "Assign All CharacterData");

        SerializedProperty allCharacters = serializedObject.FindProperty("allCharacters");
        allCharacters.arraySize = characters.Count;
        for (int i = 0; i < characters.Count; i++)
            allCharacters.GetArrayElementAtIndex(i).objectReferenceValue = characters[i];

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(display);

        if (display.gameObject.scene.IsValid() && display.gameObject.scene.isLoaded)
            EditorSceneManager.MarkSceneDirty(display.gameObject.scene);

        Debug.Log("Registered " + characters.Count + " CharacterData assets from Assets/Data.", display);
    }
}
