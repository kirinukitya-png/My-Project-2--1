using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class EncounterTsvUtility
{
    public static List<List<string>> Parse(string text)
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
            throw new FormatException("TSVに閉じていない引用符があります。");

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    public static Dictionary<string, int> BuildHeaderMap(
        List<string> headers,
        List<string> required,
        List<string> errors)
    {
        Dictionary<string, int> columns =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            string header = headers[i].Trim().TrimStart('\uFEFF');
            if (string.IsNullOrEmpty(header))
                continue;
            if (columns.ContainsKey(header))
                errors.Add("ヘッダー列が重複しています: " + header);
            else
                columns.Add(header, i);
        }

        foreach (string header in required)
        {
            if (!columns.ContainsKey(header))
                errors.Add("必須列がありません: " + header);
        }

        return columns;
    }

    public static string Cell(List<string> row, Dictionary<string, int> columns, string name)
    {
        int index = columns[name];
        return index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;
    }

    public static bool TryInteger(string value, int line, string label, List<string> errors, out int result)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) &&
            result >= 0)
            return true;

        errors.Add("行 " + line + ": " + label + "は0以上の整数で指定してください: " + value);
        return false;
    }

    public static string Read(string path)
    {
        using (StreamReader reader = new StreamReader(path, new UTF8Encoding(false), true))
            return reader.ReadToEnd();
    }

    public static string[] SplitCommaList(string value)
    {
        List<string> values = new List<string>();
        foreach (string part in value.Split(','))
        {
            string trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !values.Contains(trimmed))
                values.Add(trimmed);
        }
        return values.ToArray();
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string folder = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }
}

public class EnemyFormationDataTsvImporterWindow : EditorWindow
{
    private const string FormationFolder = "Assets/Data2/EnemyFormationData";
    private const string EnemyFolder = "Assets/Data2/EnemyData";
    private static readonly string[] RequiredHeaders = BuildRequiredHeaders();

    private readonly List<FormationImportItem> items = new List<FormationImportItem>();
    private readonly List<string> errors = new List<string>();
    private string tsvPath = string.Empty;
    private Vector2 scrollPosition;
    private bool previewReady;
    private int skippedCount;

    private sealed class FormationImportItem
    {
        public string id;
        public EnemyFormationData.FormationSlot[] slots;
        public EnemyFormationData target;
    }

    private static string[] BuildRequiredHeaders()
    {
        List<string> headers = new List<string> { "FormationID" };
        for (int i = 1; i <= 6; i++)
        {
            headers.Add("Encount" + i);
            headers.Add(i + "Gap");
            headers.Add(i + "CD");
        }
        return headers.ToArray();
    }

    [MenuItem("Tools/Enemy Data/Formation TSV Importer")]
    private static void Open()
    {
        GetWindow<EnemyFormationDataTsvImporterWindow>("Formation TSV Importer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("EnemyFormationData TSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "FormationIDごとに6枠のEnemyData参照、Gap、CDを作成・更新します。Enemy欄がすべて空のFormationはスキップします。" +
            "先にEnemyData TSV ImporterでEnemyDataを適用してください。",
            MessageType.Info);
        DrawFilePicker();

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(tsvPath) || !File.Exists(tsvPath)))
        {
            if (GUILayout.Button("読み込み・内容確認"))
                BuildPreview();
        }

        DrawErrors();
        if (errors.Count > 0 || !previewReady)
            return;

        EditorGUILayout.HelpBox(
            "適用対象: " + items.Count + "件    Enemy未設定でスキップ: " + skippedCount + "件",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(items.Count == 0))
        {
            if (GUILayout.Button("EnemyFormationDataへ適用"))
                ApplyImport();
        }
    }

    private void DrawFilePicker()
    {
        EditorGUILayout.BeginHorizontal();
        tsvPath = EditorGUILayout.TextField("TSV File", tsvPath);
        if (GUILayout.Button("選択", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFilePanel("EnemyFormationData TSVを選択", Application.dataPath, "tsv");
            if (!string.IsNullOrEmpty(path))
            {
                tsvPath = path;
                ResetPreview();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawErrors()
    {
        if (errors.Count == 0)
            return;

        EditorGUILayout.HelpBox("エラーがあるため適用できません。", MessageType.Error);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(220));
        foreach (string error in errors)
            EditorGUILayout.LabelField(error, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }

    private void ResetPreview()
    {
        previewReady = false;
        items.Clear();
        errors.Clear();
        skippedCount = 0;
    }

    private void BuildPreview()
    {
        ResetPreview();
        try
        {
            List<List<string>> rows = EncounterTsvUtility.Parse(EncounterTsvUtility.Read(tsvPath));
            if (rows.Count < 2)
            {
                errors.Add("ヘッダーとデータ行を含むTSVを指定してください。");
                return;
            }

            Dictionary<string, int> columns =
                EncounterTsvUtility.BuildHeaderMap(rows[0], new List<string>(RequiredHeaders), errors);
            if (errors.Count > 0)
                return;

            Dictionary<string, EnemyData> enemies = LoadEnemiesByName();
            Dictionary<string, EnemyFormationData> existing = LoadFormationsById();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                int line = rowIndex + 1;
                string id = EncounterTsvUtility.Cell(row, columns, "FormationID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    if (IsBlankRow(row))
                        continue;
                    errors.Add("行 " + line + ": FormationIDが空です。");
                    continue;
                }
                if (!ids.Add(id))
                {
                    errors.Add("行 " + line + ": FormationIDが重複しています: " + id);
                    continue;
                }

                EnemyFormationData.FormationSlot[] slots =
                    new EnemyFormationData.FormationSlot[6];
                bool hasEnemy = false;
                bool rowValid = true;
                for (int i = 1; i <= 6; i++)
                {
                    string enemyName = EncounterTsvUtility.Cell(row, columns, "Encount" + i);
                    if (!EncounterTsvUtility.TryInteger(
                            EncounterTsvUtility.Cell(row, columns, i + "Gap"),
                            line,
                            i + "Gap",
                            errors,
                            out int gap))
                        rowValid = false;
                    if (!EncounterTsvUtility.TryInteger(
                            EncounterTsvUtility.Cell(row, columns, i + "CD"),
                            line,
                            i + "CD",
                            errors,
                            out int cooldown))
                        rowValid = false;

                    EnemyData enemy = null;
                    if (!string.IsNullOrEmpty(enemyName))
                    {
                        hasEnemy = true;
                        if (!enemies.TryGetValue(enemyName, out enemy))
                        {
                            errors.Add("行 " + line + ": EnemyDataが見つかりません: " + enemyName);
                            rowValid = false;
                        }
                    }

                    slots[i - 1] = new EnemyFormationData.FormationSlot
                    {
                        enemy = enemy,
                        gap = gap,
                        cooldown = cooldown
                    };
                }

                if (!rowValid)
                    continue;
                if (!hasEnemy)
                {
                    skippedCount++;
                    continue;
                }

                existing.TryGetValue(id, out EnemyFormationData target);
                items.Add(new FormationImportItem { id = id, slots = slots, target = target });
            }

            previewReady = errors.Count == 0;
            if (!previewReady)
                items.Clear();
        }
        catch (Exception exception)
        {
            errors.Add("TSVを読み込めませんでした: " + exception.Message);
        }
    }

    private Dictionary<string, EnemyData> LoadEnemiesByName()
    {
        Dictionary<string, EnemyData> result =
            new Dictionary<string, EnemyData>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyData", new[] { EnemyFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemyData enemy = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            if (enemy == null)
                continue;

            string key = string.IsNullOrWhiteSpace(enemy.enemyName) ? enemy.name : enemy.enemyName;
            if (result.ContainsKey(key))
                errors.Add("EnemyDataに同じ名前が複数あります: " + key);
            else
                result.Add(key, enemy);
        }
        return result;
    }

    private Dictionary<string, EnemyFormationData> LoadFormationsById()
    {
        EncounterTsvUtility.EnsureFolder(FormationFolder);
        Dictionary<string, EnemyFormationData> result =
            new Dictionary<string, EnemyFormationData>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyFormationData", new[] { FormationFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemyFormationData formation = AssetDatabase.LoadAssetAtPath<EnemyFormationData>(path);
            if (formation == null || string.IsNullOrWhiteSpace(formation.formationId))
                continue;

            if (result.ContainsKey(formation.formationId))
                errors.Add("EnemyFormationDataに同じFormationIDが複数あります: " + formation.formationId);
            else
                result.Add(formation.formationId, formation);
        }
        return result;
    }

    private void ApplyImport()
    {
        if (!previewReady || errors.Count > 0)
            return;

        try
        {
            foreach (FormationImportItem item in items)
            {
                EnemyFormationData formation = item.target;
                if (formation == null)
                {
                    formation = ScriptableObject.CreateInstance<EnemyFormationData>();
                    formation.name = item.id;
                    AssetDatabase.CreateAsset(formation, FormationFolder + "/" + item.id + ".asset");
                }
                else
                {
                    Undo.RecordObject(formation, "Update EnemyFormationData from TSV");
                }

                formation.formationId = item.id;
                formation.slots = item.slots;
                EditorUtility.SetDirty(formation);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("適用エラー", "適用中にエラーが発生しました。Consoleを確認してください。", "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("適用完了", "EnemyFormationDataを" + items.Count + "件適用しました。", "OK");
        BuildPreview();
    }

    private bool IsBlankRow(List<string> row)
    {
        foreach (string cell in row)
        {
            if (!string.IsNullOrWhiteSpace(cell))
                return false;
        }
        return true;
    }
}

public class EncounterPoolDataTsvImporterWindow : EditorWindow
{
    private const string PoolFolder = "Assets/Data2/EncounterPoolData";
    private const string FormationFolder = "Assets/Data2/EnemyFormationData";

    private readonly List<PoolImportItem> items = new List<PoolImportItem>();
    private readonly List<string> errors = new List<string>();
    private string tsvPath = string.Empty;
    private Vector2 scrollPosition;
    private bool previewReady;

    private sealed class PoolImportItem
    {
        public string id;
        public int[] roundIndices;
        public string category;
        public EnemyFormationData[] formations;
        public EncounterPoolData target;
    }

    [MenuItem("Tools/Enemy Data/Encounter Pool TSV Importer")]
    private static void Open()
    {
        GetWindow<EncounterPoolDataTsvImporterWindow>("Encounter Pool TSV Importer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("EncounterPoolData TSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "EncountIDごとにRound、Category、Formation候補を作成・更新します。" +
            "先にEnemyFormationData TSV Importerを適用してください。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        tsvPath = EditorGUILayout.TextField("TSV File", tsvPath);
        if (GUILayout.Button("選択", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFilePanel("EncounterPoolData TSVを選択", Application.dataPath, "tsv");
            if (!string.IsNullOrEmpty(path))
            {
                tsvPath = path;
                ResetPreview();
            }
        }
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(tsvPath) || !File.Exists(tsvPath)))
        {
            if (GUILayout.Button("読み込み・内容確認"))
                BuildPreview();
        }

        if (errors.Count > 0)
        {
            EditorGUILayout.HelpBox("エラーがあるため適用できません。", MessageType.Error);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(220));
            foreach (string error in errors)
                EditorGUILayout.LabelField(error, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
            return;
        }

        if (!previewReady)
            return;

        EditorGUILayout.HelpBox("適用対象: " + items.Count + "件", MessageType.Info);
        using (new EditorGUI.DisabledScope(items.Count == 0))
        {
            if (GUILayout.Button("EncounterPoolDataへ適用"))
                ApplyImport();
        }
    }

    private void ResetPreview()
    {
        previewReady = false;
        items.Clear();
        errors.Clear();
    }

    private void BuildPreview()
    {
        ResetPreview();
        try
        {
            List<List<string>> rows = EncounterTsvUtility.Parse(EncounterTsvUtility.Read(tsvPath));
            if (rows.Count < 2)
            {
                errors.Add("ヘッダーとデータ行を含むTSVを指定してください。");
                return;
            }

            string[] required = { "EncountID", "Round", "FormationID", "Category" };
            Dictionary<string, int> columns =
                EncounterTsvUtility.BuildHeaderMap(rows[0], new List<string>(required), errors);
            if (errors.Count > 0)
                return;

            Dictionary<string, EnemyFormationData> formations = LoadFormationsById();
            Dictionary<string, EncounterPoolData> existing = LoadPoolsById();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.TrueForAll(string.IsNullOrWhiteSpace))
                    continue;

                int line = rowIndex + 1;
                string id = EncounterTsvUtility.Cell(row, columns, "EncountID");
                string roundValue = EncounterTsvUtility.Cell(row, columns, "Round");
                string formationValue = EncounterTsvUtility.Cell(row, columns, "FormationID");
                string category = EncounterTsvUtility.Cell(row, columns, "Category");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(roundValue) ||
                    string.IsNullOrWhiteSpace(formationValue) || string.IsNullOrWhiteSpace(category))
                {
                    errors.Add("行 " + line + ": EncountID、Round、FormationID、Categoryは必須です。");
                    continue;
                }
                if (!ids.Add(id))
                {
                    errors.Add("行 " + line + ": EncountIDが重複しています: " + id);
                    continue;
                }

                List<int> roundIndices = new List<int>();
                foreach (string round in EncounterTsvUtility.SplitCommaList(roundValue))
                {
                    if (!TryParseRound(round, out int roundIndex))
                    {
                        errors.Add("行 " + line + ": Roundは1-1〜5-5で指定してください: " + round);
                        continue;
                    }
                    if (!roundIndices.Contains(roundIndex))
                        roundIndices.Add(roundIndex);
                }

                List<EnemyFormationData> formationList = new List<EnemyFormationData>();
                foreach (string formationId in EncounterTsvUtility.SplitCommaList(formationValue))
                {
                    if (!formations.TryGetValue(formationId, out EnemyFormationData formation))
                    {
                        errors.Add("行 " + line + ": EnemyFormationDataが見つかりません: " + formationId);
                        continue;
                    }
                    formationList.Add(formation);
                }

                if (roundIndices.Count == 0 || formationList.Count == 0)
                    continue;

                existing.TryGetValue(id, out EncounterPoolData target);
                items.Add(new PoolImportItem
                {
                    id = id,
                    roundIndices = roundIndices.ToArray(),
                    category = category,
                    formations = formationList.ToArray(),
                    target = target
                });
            }

            previewReady = errors.Count == 0;
            if (!previewReady)
                items.Clear();
        }
        catch (Exception exception)
        {
            errors.Add("TSVを読み込めませんでした: " + exception.Message);
        }
    }

    private Dictionary<string, EnemyFormationData> LoadFormationsById()
    {
        Dictionary<string, EnemyFormationData> result =
            new Dictionary<string, EnemyFormationData>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyFormationData", new[] { FormationFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemyFormationData formation = AssetDatabase.LoadAssetAtPath<EnemyFormationData>(path);
            if (formation == null || string.IsNullOrWhiteSpace(formation.formationId))
                continue;

            if (result.ContainsKey(formation.formationId))
                errors.Add("EnemyFormationDataに同じFormationIDが複数あります: " + formation.formationId);
            else
                result.Add(formation.formationId, formation);
        }
        return result;
    }

    private Dictionary<string, EncounterPoolData> LoadPoolsById()
    {
        EncounterTsvUtility.EnsureFolder(PoolFolder);
        Dictionary<string, EncounterPoolData> result =
            new Dictionary<string, EncounterPoolData>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:EncounterPoolData", new[] { PoolFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EncounterPoolData pool = AssetDatabase.LoadAssetAtPath<EncounterPoolData>(path);
            if (pool == null || string.IsNullOrWhiteSpace(pool.encounterId))
                continue;

            if (result.ContainsKey(pool.encounterId))
                errors.Add("EncounterPoolDataに同じEncountIDが複数あります: " + pool.encounterId);
            else
                result.Add(pool.encounterId, pool);
        }
        return result;
    }

    private bool TryParseRound(string value, out int roundIndex)
    {
        roundIndex = -1;
        string[] parts = value.Split('-');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stage) ||
            !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int round) ||
            stage < 1 || stage > 5 || round < 1 || round > 5)
            return false;

        roundIndex = (stage - 1) * 5 + round - 1;
        return true;
    }

    private void ApplyImport()
    {
        if (!previewReady || errors.Count > 0)
            return;

        try
        {
            foreach (PoolImportItem item in items)
            {
                EncounterPoolData pool = item.target;
                if (pool == null)
                {
                    pool = ScriptableObject.CreateInstance<EncounterPoolData>();
                    pool.name = item.id;
                    AssetDatabase.CreateAsset(pool, PoolFolder + "/" + item.id + ".asset");
                }
                else
                {
                    Undo.RecordObject(pool, "Update EncounterPoolData from TSV");
                }

                pool.encounterId = item.id;
                pool.roundIndices = item.roundIndices;
                pool.category = item.category;
                pool.formations = item.formations;
                EditorUtility.SetDirty(pool);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("適用エラー", "適用中にエラーが発生しました。Consoleを確認してください。", "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("適用完了", "EncounterPoolDataを" + items.Count + "件適用しました。", "OK");
        BuildPreview();
    }
}
