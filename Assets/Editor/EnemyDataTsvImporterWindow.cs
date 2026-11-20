using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class EnemyDataTsvImporterWindow : EditorWindow
{
    private const string DataFolder = "Assets/Data2/EnemyData";
    private static readonly string[] RequiredHeaders =
    {
        "チェックボックス",
        "EnemyID",
        "EnemyName",
        "HP",
        "Attack",
        "Defense",
        "Target",
        "Cip",
        "AE",
        "Pig",
        "Icon"
    };

    private readonly List<ImportItem> previewItems = new List<ImportItem>();
    private readonly List<string> errors = new List<string>();
    private string tsvPath = string.Empty;
    private Vector2 scrollPosition;
    private bool previewReady;
    private int createCount;
    private int updateCount;
    private int skippedCount;

    private sealed class ImportItem
    {
        public string enemyId;
        public string enemyName;
        public int health;
        public int attack;
        public int defense;
        public string targetValue;
        public string cip;
        public string ae;
        public string pig;
        public Sprite icon;
        public EnemyData target;
    }

    [MenuItem("Tools/Enemy Data/TSV Importer")]
    private static void Open()
    {
        GetWindow<EnemyDataTsvImporterWindow>("Enemy TSV Importer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("EnemyData TSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "チェックボックスがTRUEの行だけをAssets/Data2/EnemyDataへ作成・更新します。EnemyIDで既存データを照合し、" +
            "アセット名はEnemyNameにします。Icon列は空欄、または画像未配置のパスでも取り込めます。画像がない場合はIcon未設定になります。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        tsvPath = EditorGUILayout.TextField("TSV File", tsvPath);
        if (GUILayout.Button("選択", GUILayout.Width(70)))
        {
            string selectedPath = EditorUtility.OpenFilePanel("EnemyData TSVを選択", Application.dataPath, "tsv");
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
            "更新: " + updateCount + "件    新規作成: " + createCount + "件    " +
            "FALSEでスキップ: " + skippedCount + "件    対象合計: " + previewItems.Count + "件",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!previewReady || previewItems.Count == 0))
        {
            if (GUILayout.Button("EnemyDataへ適用"))
                ApplyImport();
        }
    }

    private void ResetPreview()
    {
        previewReady = false;
        previewItems.Clear();
        errors.Clear();
        createCount = 0;
        updateCount = 0;
        skippedCount = 0;
    }

    private void BuildPreview()
    {
        ResetPreview();

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
            foreach (string header in RequiredHeaders)
            {
                if (!columns.ContainsKey(header))
                    errors.Add("必須列がありません: " + header);
            }
            if (errors.Count > 0)
                return;

            Dictionary<string, List<EnemyData>> existingById = LoadExistingEnemies(true);
            Dictionary<string, List<EnemyData>> existingByName = LoadExistingEnemies(false);
            HashSet<string> importedNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> importedIds = new HashSet<string>(StringComparer.Ordinal);
            int checkboxIndex = columns["チェックボックス"];

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0]))
                    continue;

                int lineNumber = rowIndex + 1;
                if (row.Count <= checkboxIndex)
                {
                    errors.Add("行 " + lineNumber + ": チェックボックス列がありません。");
                    continue;
                }

                string checkbox = GetCell(row, checkboxIndex).Trim();
                if (string.Equals(checkbox, "FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    skippedCount++;
                    continue;
                }

                if (!string.Equals(checkbox, "TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("行 " + lineNumber + ": チェックボックスはTRUEまたはFALSEで指定してください。");
                    continue;
                }

                if (row.Count <= MaxColumnIndex(columns))
                {
                    errors.Add("行 " + lineNumber + ": TRUEの行に必須列の値が不足しています。");
                    continue;
                }

                string name = GetCell(row, columns["EnemyName"]).Trim();
                string enemyId = GetCell(row, columns["EnemyID"]).Trim();
                if (string.IsNullOrWhiteSpace(enemyId))
                {
                    errors.Add("行 " + lineNumber + ": EnemyIDが空です。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add("行 " + lineNumber + ": EnemyNameが空です。");
                    continue;
                }
                if (!IsValidAssetName(name))
                {
                    errors.Add("行 " + lineNumber + ": EnemyNameをファイル名にできません: " + name);
                    continue;
                }
                if (!importedNames.Add(name))
                {
                    errors.Add("行 " + lineNumber + ": EnemyNameがTRUEの行で重複しています: " + name);
                    continue;
                }
                if (!importedIds.Add(enemyId))
                {
                    errors.Add("行 " + lineNumber + ": EnemyIDがTRUEの行で重複しています: " + enemyId);
                    continue;
                }

                if (!TryParseInteger(row, columns["HP"], "HP", lineNumber, out int health) ||
                    !TryParseInteger(row, columns["Attack"], "Attack", lineNumber, out int attack) ||
                    !TryParseInteger(row, columns["Defense"], "Defense", lineNumber, out int defense))
                    continue;

                string iconPath = GetCell(row, columns["Icon"]).Trim();
                if (!TryLoadIcon(iconPath, lineNumber, out Sprite icon))
                    continue;

                EnemyData target = null;
                string targetPath = DataFolder + "/" + name + ".asset";
                if (existingById.TryGetValue(enemyId, out List<EnemyData> idMatches))
                {
                    if (idMatches.Count > 1)
                    {
                        errors.Add("Assets/Data2/EnemyData内に同じEnemyIDのデータが複数あります: " + enemyId);
                        continue;
                    }
                    target = idMatches[0];
                }
                if (target == null &&
                    existingByName.TryGetValue(name, out List<EnemyData> matches))
                {
                    if (matches.Count > 1)
                    {
                        errors.Add("Assets/Data2/EnemyData内に同じEnemyNameのデータが複数あります: " + name);
                        continue;
                    }
                    target = matches[0];
                }
                if (target == null)
                    target = AssetDatabase.LoadAssetAtPath<EnemyData>(targetPath);

                UnityEngine.Object assetAtTargetPath = AssetDatabase.LoadMainAssetAtPath(targetPath);
                if (assetAtTargetPath != null && assetAtTargetPath != target)
                {
                    errors.Add("同名ファイルが既に存在し、別のEnemyDataが登録されています: " + targetPath);
                    continue;
                }

                previewItems.Add(new ImportItem
                {
                    enemyId = enemyId,
                    enemyName = name,
                    health = health,
                    attack = attack,
                    defense = defense,
                    targetValue = GetCell(row, columns["Target"]).Trim(),
                    cip = GetCell(row, columns["Cip"]).Trim(),
                    ae = GetCell(row, columns["AE"]).Trim(),
                    pig = GetCell(row, columns["Pig"]).Trim(),
                    icon = icon,
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
                if (item.target == null)
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
            foreach (ImportItem item in previewItems)
            {
                EnemyData enemy = item.target;
                if (enemy == null)
                {
                    enemy = ScriptableObject.CreateInstance<EnemyData>();
                    enemy.name = item.enemyName;
                    string assetPath = DataFolder + "/" + item.enemyName + ".asset";
                    AssetDatabase.CreateAsset(enemy, assetPath);
                }
                else
                {
                    Undo.RecordObject(enemy, "Update EnemyData from TSV");
                    string oldPath = AssetDatabase.GetAssetPath(enemy);
                    string targetPath = DataFolder + "/" + item.enemyName + ".asset";
                    if (!string.Equals(oldPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        string moveError = AssetDatabase.MoveAsset(oldPath, targetPath);
                        if (!string.IsNullOrEmpty(moveError))
                            throw new IOException("EnemyDataの名前を変更できませんでした: " + moveError);
                    }
                    enemy.name = item.enemyName;
                }

                enemy.enemyId = item.enemyId;
                enemy.enemyName = item.enemyName;
                enemy.icon = item.icon;
                enemy.health = item.health;
                enemy.attack = item.attack;
                enemy.defense = item.defense;
                enemy.target = item.targetValue;
                enemy.cip = item.cip;
                enemy.ae = item.ae;
                enemy.pig = item.pig;
                EditorUtility.SetDirty(enemy);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "適用エラー",
                "EnemyDataの適用中にエラーが発生しました。Consoleを確認してください。",
                "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "適用完了",
            "EnemyDataを更新しました。\n更新: " + updateCount + "件\n新規作成: " + createCount + "件",
            "OK");
        BuildPreview();
    }

    private Dictionary<string, List<EnemyData>> LoadExistingEnemies(bool byId)
    {
        Dictionary<string, List<EnemyData>> enemies =
            new Dictionary<string, List<EnemyData>>(StringComparer.Ordinal);
        string[] guids = AssetDatabase.FindAssets("t:EnemyData", new[] { DataFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemyData enemy = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            if (enemy == null)
                continue;

            string key = byId ? enemy.enemyId : enemy.enemyName;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!enemies.TryGetValue(key, out List<EnemyData> matches))
            {
                matches = new List<EnemyData>();
                enemies.Add(key, matches);
            }
            matches.Add(enemy);
        }
        return enemies;
    }

    private bool TryLoadIcon(string inputPath, int lineNumber, out Sprite icon)
    {
        icon = null;
        if (string.IsNullOrWhiteSpace(inputPath))
            return true;

        string normalizedPath = inputPath.Trim().Trim('"').Replace('\\', '/');
        string projectRoot = Directory.GetParent(Application.dataPath).FullName
            .Replace('\\', '/')
            .TrimEnd('/');
        string assetPath;
        if (normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            assetPath = normalizedPath;
        }
        else if (Path.IsPathRooted(normalizedPath))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(normalizedPath).Replace('\\', '/');
            }
            catch (Exception)
            {
                errors.Add("行 " + lineNumber + ": Iconのパスが不正です: " + inputPath);
                return false;
            }

            string rootPrefix = projectRoot + "/";
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("行 " + lineNumber + ": Iconは現在のUnityプロジェクト内を指定してください: " + inputPath);
                return false;
            }

            assetPath = fullPath.Substring(rootPrefix.Length);
        }
        else
        {
            errors.Add("行 " + lineNumber + ": IconはAssets相対パスまたはプロジェクト内の絶対パスで指定してください: " + inputPath);
            return false;
        }

        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("行 " + lineNumber + ": IconがAssetsフォルダー内にありません: " + inputPath);
            return false;
        }

        string assetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets"))
            .Replace('\\', '/')
            .TrimEnd('/');
        string fullAssetPath = Path.GetFullPath(
                Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)))
            .Replace('\\', '/');
        if (!fullAssetPath.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("行 " + lineNumber + ": IconはAssetsフォルダー内を指定してください: " + inputPath);
            return false;
        }

        // Enemy artwork may be added later. Keep importing the EnemyData with an empty icon.
        if (!File.Exists(fullAssetPath))
            return true;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        foreach (UnityEngine.Object asset in assets)
        {
            if (asset is Sprite sprite)
            {
                icon = sprite;
                return true;
            }
        }

        errors.Add("行 " + lineNumber + ": Icon画像がSpriteとして読み込めません: " + assetPath);
        return false;
    }

    private Dictionary<string, int> BuildHeaderMap(List<string> headerRow)
    {
        Dictionary<string, int> columns =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerRow.Count; i++)
        {
            string header = headerRow[i].Trim().TrimStart('\uFEFF');
            if (string.IsNullOrEmpty(header))
                continue;
            if (columns.ContainsKey(header))
                errors.Add("ヘッダー列が重複しています: " + header);
            else
                columns.Add(header, i);
        }
        return columns;
    }

    private int MaxColumnIndex(Dictionary<string, int> columns)
    {
        int maxIndex = -1;
        foreach (string header in RequiredHeaders)
        {
            if (columns.TryGetValue(header, out int index))
                maxIndex = Math.Max(maxIndex, index);
        }
        return maxIndex;
    }

    private bool TryParseInteger(
        List<string> row,
        int column,
        string columnName,
        int lineNumber,
        out int value)
    {
        string cell = GetCell(row, column).Trim();
        if (int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        errors.Add("行 " + lineNumber + ": " + columnName + "が整数ではありません: " + cell);
        return false;
    }

    private bool IsValidAssetName(string name)
    {
        if (name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return name.IndexOf('/') < 0 && name.IndexOf('\\') < 0;
    }

    private string GetCell(List<string> row, int index)
    {
        return index >= 0 && index < row.Count ? row[index] : string.Empty;
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
                if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
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
