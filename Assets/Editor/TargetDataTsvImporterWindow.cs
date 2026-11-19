using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class TargetDataTsvImporterWindow : EditorWindow
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string DataFolder = ResourcesFolder + "/TargetData";

    private string tsvPath = string.Empty;
    private Vector2 scrollPosition;
    private readonly List<string> errors = new List<string>();
    private readonly List<ImportItem> items = new List<ImportItem>();

    private sealed class ImportItem
    {
        public string targetText;
        public string targetId;
        public string candidateFilter;
        public string selectionMethod;
    }

    [MenuItem("Tools/Target Data/TSV Importer")]
    private static void Open()
    {
        GetWindow<TargetDataTsvImporterWindow>("Target TSV Importer");
    }

    public static void ImportFromCommandLine()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int pathArgumentIndex = Array.IndexOf(arguments, "-targetDataTsv");
        if (pathArgumentIndex < 0 || pathArgumentIndex + 1 >= arguments.Length)
            throw new ArgumentException("Pass the TSV path after -targetDataTsv.");

        TargetDataTsvImporterWindow importer = CreateInstance<TargetDataTsvImporterWindow>();
        importer.tsvPath = arguments[pathArgumentIndex + 1];
        importer.Import();
        if (importer.errors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", importer.errors.ToArray()));
        DestroyImmediate(importer);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("TargetData TSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "TargetID、説明文、候補の絞り込み、抽選方法を読み込み、" +
            "Assets/Resources/TargetDataへ作成・更新します。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        tsvPath = EditorGUILayout.TextField("TSV File", tsvPath);
        if (GUILayout.Button("選択", GUILayout.Width(70)))
        {
            string selectedPath = EditorUtility.OpenFilePanel(
                "TargetData TSVを選択",
                Application.dataPath,
                "tsv");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                tsvPath = selectedPath;
                errors.Clear();
                items.Clear();
            }
        }
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(tsvPath) || !File.Exists(tsvPath)))
        {
            if (GUILayout.Button("読み込み・適用"))
                Import();
        }

        if (errors.Count == 0)
            return;

        EditorGUILayout.HelpBox("インポートに失敗しました。", MessageType.Error);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(220));
        foreach (string error in errors)
            EditorGUILayout.LabelField(error, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }

    private void Import()
    {
        errors.Clear();
        items.Clear();

        try
        {
            string[] lines = File.ReadAllLines(tsvPath, new UTF8Encoding(false, true));
            if (lines.Length < 2)
            {
                errors.Add("ヘッダーとデータ行を含むTSVを指定してください。");
                return;
            }

            string[] headers = lines[0].TrimStart('\uFEFF').Split('\t');
            Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < headers.Length; i++)
                columns[headers[i].Trim()] = i;

            string[] requiredHeaders =
            {
                "TargetID",
                "説明文",
                "候補の絞り込み",
                "抽選方法"
            };
            foreach (string header in requiredHeaders)
            {
                if (!columns.ContainsKey(header))
                    errors.Add("必須列がありません: " + header);
            }
            if (errors.Count > 0)
                return;

            HashSet<string> importedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                    continue;

                string[] cells = lines[lineIndex].Split('\t');
                int lineNumber = lineIndex + 1;
                if (!TryGetCell(cells, columns, "TargetID", lineNumber, out string targetId) ||
                    !TryGetCell(cells, columns, "説明文", lineNumber, out string targetText) ||
                    !TryGetCell(cells, columns, "候補の絞り込み", lineNumber, out string candidateFilter) ||
                    !TryGetCell(cells, columns, "抽選方法", lineNumber, out string selectionMethod))
                    continue;

                targetText = targetText.Trim();
                targetId = targetId.Trim();
                candidateFilter = candidateFilter.Trim();
                selectionMethod = selectionMethod.Trim();
                if (string.IsNullOrWhiteSpace(targetText))
                {
                    errors.Add("行 " + lineNumber + ": TargetTextが空です。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(targetId) ||
                    targetId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    targetId.Contains("/") ||
                    targetId.Contains("\\"))
                {
                    errors.Add("行 " + lineNumber + ": TargetIDが空、またはAsset名に使えません。");
                    continue;
                }
                if (!importedIds.Add(targetId))
                {
                    errors.Add("行 " + lineNumber + ": TargetIDが重複しています: " + targetId);
                    continue;
                }

                if (!IsSupportedCandidateFilter(candidateFilter))
                {
                    errors.Add("行 " + lineNumber + ": 候補の絞り込みの形式または抽選指定が正しくありません: " + candidateFilter);
                    continue;
                }
                if (!IsValidSelectionMethod(selectionMethod, lineNumber))
                    continue;

                items.Add(new ImportItem
                {
                    targetText = targetText,
                    targetId = targetId,
                    candidateFilter = candidateFilter,
                    selectionMethod = selectionMethod
                });
            }

            if (errors.Count > 0)
                return;

            EnsureDataFolder();
            foreach (ImportItem item in items)
            {
                string assetPath = DataFolder + "/" + item.targetId + ".asset";
                TargetData targetData = AssetDatabase.LoadAssetAtPath<TargetData>(assetPath);
                if (targetData == null)
                {
                    targetData = CreateInstance<TargetData>();
                    targetData.name = item.targetId;
                    AssetDatabase.CreateAsset(targetData, assetPath);
                }
                else
                {
                    Undo.RecordObject(targetData, "Update TargetData from TSV");
                }

                targetData.targetText = item.targetText;
                targetData.targetId = item.targetId;
                targetData.candidateFilter = item.candidateFilter;
                targetData.selectionMethod = item.selectionMethod;
                EditorUtility.SetDirty(targetData);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string completionMessage = "TargetDataを" + items.Count + "件作成・更新しました。";
            if (Application.isBatchMode)
                Debug.Log(completionMessage);
            else
                EditorUtility.DisplayDialog("適用完了", completionMessage, "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            errors.Add("TSVを読み込めませんでした: " + exception.Message);
        }
    }

    private bool TryGetCell(
        string[] cells,
        Dictionary<string, int> columns,
        string header,
        int lineNumber,
        out string value)
    {
        int index = columns[header];
        if (index < cells.Length)
        {
            value = cells[index];
            return true;
        }

        value = string.Empty;
        errors.Add("行 " + lineNumber + ": " + header + "列の値がありません。");
        return false;
    }

    private bool IsSupportedCandidateFilter(string value)
    {
        if (value == "全マス" || value == "後列" || value == "中列" ||
            value == "前列" || value == "直線")
            return true;

        string[] parts = value.Split(',');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        bool uniform = parts.Length == 2 && parts[0] == "均等抽選";
        bool weighted = parts.Length >= 4 && parts[0] == "重み付き抽選" &&
                        (parts.Length - 2) % 2 == 0;
        if (!uniform && !weighted)
            return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
            return false;
        if (!weighted)
            return true;

        bool hasPositiveWeight = false;
        for (int i = 2; i < parts.Length; i += 2)
        {
            bool validPosition = parts[i] == "後列" || parts[i] == "中列" || parts[i] == "前列" ||
                                 parts[i] == "直線" || parts[i] == "else" ||
                                 (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cell) && cell >= 1 && cell <= 9);
            if (!validPosition ||
                !int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight) ||
                weight < 0)
                return false;
            hasPositiveWeight |= weight > 0;
        }
        return hasPositiveWeight;
    }
    private bool IsValidSelectionMethod(string value, int lineNumber)
    {
        string[] parts = value.Split(',');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        if (parts.Length == 1 && parts[0] == "全員選択")
            return true;

        bool uniform = parts.Length == 2 && parts[0] == "均等抽選";
        bool weighted = parts.Length >= 4 && parts[0] == "重み付き抽選" &&
                        (parts.Length - 2) % 2 == 0;
        if (!uniform && !weighted)
        {
            errors.Add("行 " + lineNumber + ": 抽選方法の形式が正しくありません: " + value);
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
        {
            errors.Add("行 " + lineNumber + ": 抽選数は1以上の整数にしてください。");
            return false;
        }

        if (weighted)
        {
            bool hasPositiveWeight = false;
            for (int i = 2; i < parts.Length; i += 2)
            {
                bool validPosition = parts[i] == "後列" || parts[i] == "中列" || parts[i] == "前列" ||
                                     parts[i] == "直線" || parts[i] == "前方人数0" ||
                                     parts[i] == "前方人数1" || parts[i] == "前方人数2" ||
                                     parts[i] == "else" ||
                                     (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cell) && cell >= 1 && cell <= 9);
                if (!validPosition ||
                    !int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight) ||
                    weight < 0)
                {
                    errors.Add("行 " + lineNumber + ": 重み付き抽選は「位置,0以上の重み」の組で指定してください。");
                    return false;
                }
                hasPositiveWeight |= weight > 0;
            }
            if (!hasPositiveWeight)
            {
                errors.Add("行 " + lineNumber + ": 重みは少なくとも1つを1以上にしてください。");
                return false;
            }
        }
        return true;
    }

    private void EnsureDataFolder()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(DataFolder))
            AssetDatabase.CreateFolder(ResourcesFolder, "TargetData");
    }
}
