using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class CharacterDataTsvImporterWindow : EditorWindow
{
    private const string DataFolder = "Assets/Data";
    private string tsvPath = string.Empty;
    private Vector2 scrollPosition;
    private readonly List<ImportItem> previewItems = new List<ImportItem>();
    private readonly List<string> errors = new List<string>();
    private bool previewReady;
    private int createCount;
    private int updateCount;

    private sealed class ImportItem
    {
        public int lineNumber;
        public string characterName;
        public string[] attributes;
        public string[] units;
        public CharacterData target;
    }

    [MenuItem("Tools/Character Data/TSV Importer")]
    private static void Open()
    {
        GetWindow<CharacterDataTsvImporterWindow>("Character TSV Importer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("CharacterData TSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "CharacterName、units、attributesの3列を読み込みます。CharacterNameで既存データを照合し、" +
            "unitsとattributesを更新します。" +
            "TSVにないキャラクターは新規作成します。costとiconは変更しません。AttackとDefenseは現在それぞれ10です。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        tsvPath = EditorGUILayout.TextField("TSV File", tsvPath);
        if (GUILayout.Button("選択", GUILayout.Width(70)))
        {
            string selectedPath = EditorUtility.OpenFilePanel("CharacterData TSVを選択", Application.dataPath, "tsv");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                tsvPath = selectedPath;
                previewReady = false;
                previewItems.Clear();
                errors.Clear();
            }
        }
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(tsvPath) || !File.Exists(tsvPath)))
        {
            if (GUILayout.Button("読み込み・内容確認"))
                BuildPreview();
        }

        if (!previewReady && errors.Count == 0) return;

        EditorGUILayout.Space();
        if (errors.Count > 0)
        {
            EditorGUILayout.HelpBox("エラーがあるため適用できません。", MessageType.Error);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(220));
            foreach (string error in errors)
                EditorGUILayout.LabelField(error, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.HelpBox(
            "更新: " + updateCount + "件    新規作成: " + createCount + "件    合計: " + previewItems.Count + "件",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!previewReady || previewItems.Count == 0))
        {
            if (GUILayout.Button("CharacterDataへ適用"))
                ApplyImport();
        }
    }

    private void BuildPreview()
    {
        previewItems.Clear();
        errors.Clear();
        previewReady = false;
        createCount = 0;
        updateCount = 0;

        try
        {
            string text;
            using (StreamReader reader = new StreamReader(tsvPath, new UTF8Encoding(false), true))
                text = reader.ReadToEnd();

            List<List<string>> rows = ParseTsv(text);
            if (rows.Count < 2)
            {
                errors.Add("ヘッダーとデータ行を含むTSVを指定してください。");
                return;
            }

            Dictionary<string, int> columns = BuildHeaderMap(rows[0]);
            if (errors.Count > 0) return;

            string[] requiredHeaders = { "CharacterName", "units", "attributes" };
            foreach (string header in requiredHeaders)
            {
                if (!columns.ContainsKey(header))
                    errors.Add("必須列がありません: " + header);
            }
            if (errors.Count > 0) return;

            Dictionary<string, List<CharacterData>> existingByName = LoadExistingCharacters();
            HashSet<string> tsvNames = new HashSet<string>(StringComparer.Ordinal);

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0])) continue;

                int lineNumber = rowIndex + 1;
                if (row.Count <= MaxColumnIndex(columns))
                {
                    errors.Add("行 " + lineNumber + ": 列数がヘッダーより少ないです。");
                    continue;
                }

                string name = GetCell(row, columns["CharacterName"]).Trim();
                if (string.IsNullOrEmpty(name))
                {
                    errors.Add("行 " + lineNumber + ": CharacterNameが空です。");
                    continue;
                }
                if (!tsvNames.Add(name))
                {
                    errors.Add("行 " + lineNumber + ": CharacterNameがTSV内で重複しています: " + name);
                    continue;
                }

                CharacterData target = null;
                if (existingByName.TryGetValue(name, out List<CharacterData> matches))
                {
                    if (matches.Count > 1)
                    {
                        errors.Add("Assets/Data内に同名CharacterDataが複数あります: " + name);
                        continue;
                    }
                    target = matches[0];
                }

                previewItems.Add(new ImportItem
                {
                    lineNumber = lineNumber,
                    characterName = name,
                    units = ParseList(GetCell(row, columns["units"])),
                    attributes = ParseList(GetCell(row, columns["attributes"])),
                    target = target
                });
            }

            if (errors.Count > 0)
            {
                previewItems.Clear();
                return;
            }

            foreach (ImportItem item in previewItems)
            {
                if (item.target == null) createCount++;
                else updateCount++;
            }

            previewReady = true;
        }
        catch (Exception exception)
        {
            errors.Add("TSVを読み込めませんでした: " + exception.Message);
        }
    }

    private void ApplyImport()
    {
        if (!previewReady || errors.Count > 0) return;

        int nextAssetNumber = FindNextAssetNumber();
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (ImportItem item in previewItems)
            {
                CharacterData character = item.target;
                if (character == null)
                {
                    character = ScriptableObject.CreateInstance<CharacterData>();
                    character.name = "CharacterData_" + nextAssetNumber.ToString("D3", CultureInfo.InvariantCulture);
                    character.characterName = item.characterName;
                    character.cost = 1;
                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                        DataFolder + "/" + character.name + ".asset");
                    AssetDatabase.CreateAsset(character, assetPath);
                }
                else
                {
                    Undo.RecordObject(character, "Update CharacterData from TSV");
                }

                character.characterName = item.characterName;
                character.attributes = item.attributes;
                character.units = item.units;
                EditorUtility.SetDirty(character);

                if (item.target == null) nextAssetNumber++;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("適用エラー", "処理中にエラーが発生しました。Consoleを確認してください。", "OK");
            return;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "適用完了",
            "CharacterDataを更新しました。\n更新: " + updateCount + "件\n新規作成: " + createCount + "件",
            "OK");
        BuildPreview();
    }

    private Dictionary<string, List<CharacterData>> LoadExistingCharacters()
    {
        Dictionary<string, List<CharacterData>> characters =
            new Dictionary<string, List<CharacterData>>(StringComparer.Ordinal);
        string[] guids = AssetDatabase.FindAssets("t:CharacterData", new[] { DataFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CharacterData character = AssetDatabase.LoadAssetAtPath<CharacterData>(path);
            if (character == null || string.IsNullOrWhiteSpace(character.characterName)) continue;

            if (!characters.TryGetValue(character.characterName, out List<CharacterData> matches))
            {
                matches = new List<CharacterData>();
                characters.Add(character.characterName, matches);
            }
            matches.Add(character);
        }
        return characters;
    }

    private int FindNextAssetNumber()
    {
        int maxNumber = 0;
        string[] guids = AssetDatabase.FindAssets("t:CharacterData", new[] { DataFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            const string prefix = "CharacterData_";
            if (fileName.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(fileName.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                maxNumber = Math.Max(maxNumber, number);
            }
        }
        return maxNumber + 1;
    }

    private Dictionary<string, int> BuildHeaderMap(List<string> headerRow)
    {
        Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerRow.Count; i++)
        {
            string name = headerRow[i].Trim().TrimStart('\uFEFF');
            if (string.IsNullOrEmpty(name)) continue;
            if (columns.ContainsKey(name))
                errors.Add("ヘッダー列が重複しています: " + name);
            else
                columns.Add(name, i);
        }
        return columns;
    }

    private int MaxColumnIndex(Dictionary<string, int> columns)
    {
        int maxIndex = -1;
        foreach (int index in columns.Values)
            maxIndex = Math.Max(maxIndex, index);
        return maxIndex;
    }

    private string GetCell(List<string> row, int index)
    {
        return index >= 0 && index < row.Count ? row[index] : string.Empty;
    }

    private string[] ParseList(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new string[0];
        List<string> values = new List<string>();
        foreach (string item in value.Split(','))
        {
            string trimmed = item.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !values.Contains(trimmed))
                values.Add(trimmed);
        }
        return values.ToArray();
    }

    private List<List<string>> ParseTsv(string text)
    {
        List<List<string>> rows = new List<List<string>>();
        List<string> row = new List<string>();
        StringBuilder cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && current == '\t')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (!quoted && (current == '\r' || current == '\n'))
            {
                if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else
            {
                cell.Append(current);
            }
        }

        if (quoted)
            throw new FormatException("閉じていない引用符があります。");

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
