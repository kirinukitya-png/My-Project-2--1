using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class EffectDataTsvImporterWindow : EditorWindow
{
    private const string DataFolder = "Assets/Resources/EffectData";
    private static readonly string[] RequiredHeaders =
    {
        "AttackEffectsID",
        "説明文",
        "影響",
        "効果数値",
        "使用対象",
        "持続期間"
    };

    private static readonly HashSet<string> SupportedEffects = new HashSet<string>(StringComparer.Ordinal)
    {
        "Attack", "Def", "Gap", "Gold", "Damage", "Block"
    };

    private static readonly HashSet<string> SupportedTargets = new HashSet<string>(StringComparer.Ordinal)
    {
        "攻撃者", "攻撃対象", "プレイヤー"
    };

    private static readonly HashSet<string> SupportedDurations = new HashSet<string>(StringComparer.Ordinal)
    {
        "このバトル中", "即時効果"
    };

    private readonly List<ImportRow> rows = new List<ImportRow>();
    private readonly List<string> errors = new List<string>();
    private string tsvPath = string.Empty;
    private Vector2 scrollPosition;
    private bool previewReady;
    private int createCount;
    private int updateCount;

    private sealed class ImportRow
    {
        public string effectId;
        public string description;
        public string effect;
        public int effectValue;
        public string target;
        public string duration;
        public EffectData asset;
    }

    [MenuItem("Tools/Enemy Data/AE Data TSV Importer")]
    private static void Open()
    {
        GetWindow<EffectDataTsvImporterWindow>("AE Data Importer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Enemy Attack Effect (AE) TSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "AttackEffectsID、説明文、影響、効果数値、使用対象、持続期間を読み込み、" +
            DataFolder + "へEffectDataを作成・更新します。EffectIDで既存データを照合し、TSVにないデータは削除しません。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        tsvPath = EditorGUILayout.TextField("TSV File", tsvPath);
        if (GUILayout.Button("選択", GUILayout.Width(70)))
        {
            string selectedPath = EditorUtility.OpenFilePanel("AE Data TSVを選択", Application.dataPath, "tsv");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                tsvPath = selectedPath;
                ResetPreview();
            }
        }
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(tsvPath) || !File.Exists(tsvPath)))
        {
            if (GUILayout.Button("読み込み・内容確認"))
                BuildPreview();
        }

        if (!previewReady && errors.Count == 0)
            return;

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
            "更新: " + updateCount + "件    新規作成: " + createCount + "件    対象合計: " + rows.Count + "件",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!previewReady || rows.Count == 0))
        {
            if (GUILayout.Button("EffectDataへ適用"))
                ApplyImport();
        }
    }

    private void ResetPreview()
    {
        previewReady = false;
        rows.Clear();
        errors.Clear();
        createCount = 0;
        updateCount = 0;
    }

    private void BuildPreview()
    {
        ResetPreview();

        try
        {
            string text;
            using (StreamReader reader = new StreamReader(tsvPath, new UTF8Encoding(false), true))
                text = reader.ReadToEnd();

            List<List<string>> table = ParseTsv(text);
            if (table.Count < 2)
            {
                errors.Add("ヘッダーとデータ行を含むTSVを指定してください。");
                return;
            }

            Dictionary<string, int> columns = BuildHeaderMap(table[0]);
            foreach (string header in RequiredHeaders)
                if (!columns.ContainsKey(header))
                    errors.Add("必須列がありません: " + header);
            if (errors.Count > 0)
                return;

            int maxColumn = -1;
            foreach (string header in RequiredHeaders)
                maxColumn = Math.Max(maxColumn, columns[header]);

            Dictionary<string, List<EffectData>> existingById = LoadExistingById();
            HashSet<string> importedIds = new HashSet<string>(StringComparer.Ordinal);

            for (int rowIndex = 1; rowIndex < table.Count; rowIndex++)
            {
                List<string> cells = table[rowIndex];
                if (cells.Count == 1 && string.IsNullOrWhiteSpace(cells[0]))
                    continue;

                int lineNumber = rowIndex + 1;
                if (cells.Count <= maxColumn)
                {
                    errors.Add("行 " + lineNumber + ": 必須列の値が不足しています。");
                    continue;
                }

                string effectId = GetCell(cells, columns["AttackEffectsID"]).Trim();
                string description = GetCell(cells, columns["説明文"]).Trim();
                string effect = GetCell(cells, columns["影響"]).Trim();
                string target = GetCell(cells, columns["使用対象"]).Trim();
                string duration = GetCell(cells, columns["持続期間"]).Trim();

                if (string.IsNullOrWhiteSpace(effectId) || !IsValidAssetName(effectId))
                {
                    errors.Add("行 " + lineNumber + ": AttackEffectsIDが空、またはAsset名に使えません。");
                    continue;
                }
                if (!importedIds.Add(effectId))
                {
                    errors.Add("行 " + lineNumber + ": AttackEffectsIDが重複しています: " + effectId);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(description))
                {
                    errors.Add("行 " + lineNumber + ": 説明文が空です。");
                    continue;
                }
                if (!SupportedEffects.Contains(effect))
                {
                    errors.Add("行 " + lineNumber + ": 影響が未対応です: " + effect);
                    continue;
                }
                if (!SupportedTargets.Contains(target))
                {
                    errors.Add("行 " + lineNumber + ": 使用対象が未対応です: " + target);
                    continue;
                }
                if (!SupportedDurations.Contains(duration))
                {
                    errors.Add("行 " + lineNumber + ": 持続期間が未対応です: " + duration);
                    continue;
                }
                if (!int.TryParse(
                        GetCell(cells, columns["効果数値"]).Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int effectValue))
                {
                    errors.Add("行 " + lineNumber + ": 効果数値が整数ではありません: " +
                        GetCell(cells, columns["効果数値"]).Trim());
                    continue;
                }

                EffectData asset = null;
                if (existingById.TryGetValue(effectId, out List<EffectData> matches))
                {
                    if (matches.Count > 1)
                    {
                        errors.Add("Resources/EffectData内に同じEffectIDが複数あります: " + effectId);
                        continue;
                    }
                    asset = matches[0];
                }

                string targetPath = DataFolder + "/" + effectId + ".asset";
                UnityEngine.Object occupant = AssetDatabase.LoadMainAssetAtPath(targetPath);
                if (occupant != null && occupant != asset)
                {
                    errors.Add("同名の別Assetが存在します: " + targetPath);
                    continue;
                }

                rows.Add(new ImportRow
                {
                    effectId = effectId,
                    description = description,
                    effect = effect,
                    effectValue = effectValue,
                    target = target,
                    duration = duration,
                    asset = asset
                });
            }

            if (errors.Count > 0)
            {
                rows.Clear();
                return;
            }

            foreach (ImportRow row in rows)
            {
                if (row.asset == null)
                    createCount++;
                else
                    updateCount++;
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
        if (!previewReady || errors.Count > 0)
            return;

        try
        {
            EnsureDataFolder();
            foreach (ImportRow row in rows)
            {
                string targetPath = DataFolder + "/" + row.effectId + ".asset";
                EffectData effectData = row.asset;
                if (effectData == null)
                {
                    effectData = CreateInstance<EffectData>();
                    effectData.name = row.effectId;
                    AssetDatabase.CreateAsset(effectData, targetPath);
                }
                else
                {
                    Undo.RecordObject(effectData, "Update EffectData from TSV");
                    string oldPath = AssetDatabase.GetAssetPath(effectData);
                    if (!string.Equals(oldPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        string moveError = AssetDatabase.MoveAsset(oldPath, targetPath);
                        if (!string.IsNullOrEmpty(moveError))
                            throw new IOException("EffectDataを移動できませんでした: " + moveError);
                    }
                    effectData.name = row.effectId;
                }

                effectData.effectId = row.effectId;
                effectData.description = row.description;
                effectData.effect = row.effect;
                effectData.effectValue = row.effectValue;
                effectData.target = row.target;
                effectData.duration = row.duration;
                EditorUtility.SetDirty(effectData);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "適用エラー",
                "EffectDataの適用中にエラーが発生しました。Consoleを確認してください。",
                "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "適用完了",
            "EffectDataを更新しました。\n更新: " + updateCount + "件\n新規作成: " + createCount + "件",
            "OK");
        BuildPreview();
    }

    private Dictionary<string, List<EffectData>> LoadExistingById()
    {
        Dictionary<string, List<EffectData>> result =
            new Dictionary<string, List<EffectData>>(StringComparer.Ordinal);
        if (!AssetDatabase.IsValidFolder(DataFolder))
            return result;

        foreach (string guid in AssetDatabase.FindAssets("t:EffectData", new[] { DataFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EffectData data = AssetDatabase.LoadAssetAtPath<EffectData>(path);
            if (data == null || string.IsNullOrWhiteSpace(data.effectId))
                continue;

            if (!result.TryGetValue(data.effectId, out List<EffectData> matches))
            {
                matches = new List<EffectData>();
                result.Add(data.effectId, matches);
            }
            matches.Add(data);
        }
        return result;
    }

    private void EnsureDataFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(DataFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "EffectData");
    }

    private Dictionary<string, int> BuildHeaderMap(List<string> headerRow)
    {
        Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerRow.Count; i++)
        {
            string header = headerRow[i].Trim().TrimStart('\uFEFF');
            if (string.IsNullOrEmpty(header))
                continue;
            if (result.ContainsKey(header))
                errors.Add("ヘッダーが重複しています: " + header);
            else
                result.Add(header, i);
        }
        return result;
    }

    private string GetCell(List<string> cells, int index)
    {
        return index >= 0 && index < cells.Count ? cells[index] : string.Empty;
    }

    private bool IsValidAssetName(string value)
    {
        return value != "." && value != ".." &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            value.IndexOf('/') < 0 && value.IndexOf('\\') < 0;
    }

    private List<List<string>> ParseTsv(string text)
    {
        List<List<string>> parsedRows = new List<List<string>>();
        List<string> currentRow = new List<string>();
        StringBuilder currentCell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    currentCell.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && current == '\t')
            {
                currentRow.Add(currentCell.ToString());
                currentCell.Clear();
            }
            else if (!quoted && (current == '\r' || current == '\n'))
            {
                if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                currentRow.Add(currentCell.ToString());
                currentCell.Clear();
                parsedRows.Add(currentRow);
                currentRow = new List<string>();
            }
            else
            {
                currentCell.Append(current);
            }
        }

        if (quoted)
            throw new FormatException("閉じていない引用符があります。");
        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            parsedRows.Add(currentRow);
        }
        return parsedRows;
    }
}
