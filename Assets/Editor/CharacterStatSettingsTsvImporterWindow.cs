using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class CharacterStatSettingsTsvImporterWindow : EditorWindow
{
    private const string DefaultAssetPath = "Assets/Data2/CharacterStatSettings.asset";

    private string tsvPath = string.Empty;
    private CharacterStatSettings targetSettings;
    private string importSummary = string.Empty;
    private string importError = string.Empty;

    [MenuItem("Tools/Character Stat Settings/TSV Importer")]
    private static void Open()
    {
        GetWindow<CharacterStatSettingsTsvImporterWindow>(
            "Character Stat Settings Importer");
    }

    private void OnEnable()
    {
        targetSettings = Selection.activeObject as CharacterStatSettings;
        if (targetSettings == null)
            targetSettings = AssetDatabase.LoadAssetAtPath<CharacterStatSettings>(DefaultAssetPath);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("CharacterStatSettings TSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Cost、Star1〜Star3ごとのAtk・Def・Weight列を読み込みます。" +
            "「攻撃力,防御力,重み」を1セルにまとめた旧形式も読み込めます。" +
            "空欄セルは候補として無視します。",
            MessageType.Info);

        targetSettings = (CharacterStatSettings)EditorGUILayout.ObjectField(
            "Character Stat Settings",
            targetSettings,
            typeof(CharacterStatSettings),
            false);

        EditorGUILayout.BeginHorizontal();
        tsvPath = EditorGUILayout.TextField("TSV File", tsvPath);
        if (GUILayout.Button("選択", GUILayout.Width(70)))
        {
            string selectedPath = EditorUtility.OpenFilePanel(
                "CharacterStatSettings TSVを選択",
                Application.dataPath,
                "tsv");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                tsvPath = selectedPath;
                importSummary = string.Empty;
                importError = string.Empty;
            }
        }
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(
                   targetSettings == null || string.IsNullOrEmpty(tsvPath) || !File.Exists(tsvPath)))
        {
            if (GUILayout.Button("CharacterStatSettingsへインポート"))
                Import();
        }

        if (!string.IsNullOrEmpty(importSummary))
            EditorGUILayout.HelpBox(importSummary, MessageType.Info);
        if (!string.IsNullOrEmpty(importError))
            EditorGUILayout.HelpBox(importError, MessageType.Error);
    }

    private void Import()
    {
        importSummary = string.Empty;
        importError = string.Empty;

        if (targetSettings == null)
        {
            importError = "インポート先のCharacterStatSettingsを指定してください。";
            return;
        }

        try
        {
            string text = File.ReadAllText(tsvPath, new UTF8Encoding(false, true));
            CharacterStatSettings.CostStats[] importedCosts =
                ParseTsv(text, out int optionCount);

            Undo.RecordObject(targetSettings, "Import CharacterStatSettings TSV");
            targetSettings.costs = importedCosts;
            EditorUtility.SetDirty(targetSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            importSummary = importedCosts.Length + "コスト、" + optionCount +
                            "候補をインポートしました。";
            Debug.Log(importSummary, targetSettings);
        }
        catch (Exception exception)
        {
            importError = exception.Message;
            Debug.LogError("CharacterStatSettings TSV import failed: " + exception.Message, this);
        }
    }

    private static CharacterStatSettings.CostStats[] ParseTsv(
        string text,
        out int optionCount)
    {
        optionCount = 0;
        string[] lines = text.TrimStart('\uFEFF').Split(
            new[] { "\r\n", "\n", "\r" },
            StringSplitOptions.None);
        if (lines.Length < 3)
            throw new FormatException("Cost見出しとデータ行を含むTSVを指定してください。");

        string[] firstHeader = lines[0].Split('\t');
        bool usesSeparateStatColumns = IsSeparateStatColumnFormat(firstHeader, lines);
        bool usesCombinedStatColumns = IsCombinedStatColumnFormat(firstHeader);
        if (!usesSeparateStatColumns && !usesCombinedStatColumns)
        {
            throw new FormatException(
                "列見出しはStar1〜Star3のAtk・Def・Weight列、または旧形式のStar1〜Star3列にしてください。");
        }
        int firstDataLine = usesSeparateStatColumns ? 2 : 1;

        List<CharacterStatSettings.StarStats>[,] options =
            new List<CharacterStatSettings.StarStats>[6, 4];
        for (int cost = 1; cost <= 5; cost++)
            for (int star = 1; star <= 3; star++)
                options[cost, star] = new List<CharacterStatSettings.StarStats>();

        bool[] foundCosts = new bool[6];
        int currentCost = 0;
        for (int lineIndex = firstDataLine; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] cells = line.Split('\t');
            if (cells.Length > 0 && !string.IsNullOrWhiteSpace(cells[0]))
            {
                string marker = cells[0].Trim();
                if (!marker.StartsWith("Cost", StringComparison.Ordinal) ||
                    !int.TryParse(
                        marker.Substring(4),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out currentCost) ||
                    currentCost < 1 ||
                    currentCost > 5)
                {
                    throw new FormatException(
                        "行 " + (lineIndex + 1) + ": Cost1〜Cost5の見出しが必要です。");
                }

                if (foundCosts[currentCost])
                    throw new FormatException(
                        "行 " + (lineIndex + 1) + ": Cost" + currentCost + "が重複しています。");
                foundCosts[currentCost] = true;
            }

            if (currentCost == 0)
                throw new FormatException(
                    "行 " + (lineIndex + 1) + ": 最初のCost見出しより前にデータがあります。");

            if (usesSeparateStatColumns)
            {
                if (cells.Length > 10)
                {
                    for (int column = 10; column < cells.Length; column++)
                        if (!string.IsNullOrWhiteSpace(cells[column]))
                            throw new FormatException(
                                "行 " + (lineIndex + 1) + ": 想定外の列にデータがあります。");
                }

                for (int star = 1; star <= 3; star++)
                {
                    int firstColumn = 1 + (star - 1) * 3;
                    string attackText = GetCell(cells, firstColumn);
                    string defenseText = GetCell(cells, firstColumn + 1);
                    string weightText = GetCell(cells, firstColumn + 2);
                    int populatedCells =
                        (string.IsNullOrWhiteSpace(attackText) ? 0 : 1) +
                        (string.IsNullOrWhiteSpace(defenseText) ? 0 : 1) +
                        (string.IsNullOrWhiteSpace(weightText) ? 0 : 1);
                    if (populatedCells == 0)
                        continue;
                    if (populatedCells != 3 ||
                        !TryParseNonNegativeInt(attackText, out int attack) ||
                        !TryParseNonNegativeInt(defenseText, out int defense) ||
                        !TryParseNonNegativeInt(weightText, out int weight))
                    {
                        throw new FormatException(
                            "行 " + (lineIndex + 1) +
                            "、Star" + star +
                            ": Atk・Def・Weightに非負整数をすべて入力してください。");
                    }

                    AddOption(options, currentCost, star, attack, defense, weight);
                }
            }
            else
            {
                if (cells.Length > 4)
                {
                    for (int column = 4; column < cells.Length; column++)
                        if (!string.IsNullOrWhiteSpace(cells[column]))
                            throw new FormatException(
                                "行 " + (lineIndex + 1) + ": 想定外の列にデータがあります。");
                }

                for (int star = 1; star <= 3; star++)
                {
                    int column = star;
                    if (column >= cells.Length || string.IsNullOrWhiteSpace(cells[column]))
                        continue;

                    string[] values = cells[column].Split(',');
                    if (values.Length != 3 ||
                        !TryParseNonNegativeInt(values[0], out int attack) ||
                        !TryParseNonNegativeInt(values[1], out int defense) ||
                        !TryParseNonNegativeInt(values[2], out int weight))
                    {
                        throw new FormatException(
                            "行 " + (lineIndex + 1) +
                            "、Star" + star +
                            ": 旧形式の値は「攻撃力,防御力,重み」の非負整数3つで指定してください。");
                    }

                    AddOption(options, currentCost, star, attack, defense, weight);
                }
            }
        }

        optionCount = CountOptions(options);
        CharacterStatSettings.CostStats[] costs =
            new CharacterStatSettings.CostStats[5];
        for (int cost = 1; cost <= 5; cost++)
        {
            if (!foundCosts[cost])
                throw new FormatException("Cost" + cost + "の見出しがありません。");

            for (int star = 1; star <= 3; star++)
            {
                List<CharacterStatSettings.StarStats> starOptions = options[cost, star];
                if (starOptions.Count == 0)
                    throw new FormatException(
                        "Cost" + cost + "、Star" + star + "に候補がありません。");

                long totalWeight = 0;
                foreach (CharacterStatSettings.StarStats stat in starOptions)
                    totalWeight += stat.weight;
                if (totalWeight <= 0 || totalWeight > int.MaxValue)
                    throw new FormatException(
                        "Cost" + cost + "、Star" + star +
                        "の重みの合計は1〜" + int.MaxValue + "にしてください。");
            }

            costs[cost - 1] = new CharacterStatSettings.CostStats
            {
                cost = cost,
                star1Options = options[cost, 1].ToArray(),
                star2Options = options[cost, 2].ToArray(),
                star3Options = options[cost, 3].ToArray()
            };
        }

        return costs;
    }

    private static bool IsSeparateStatColumnFormat(string[] firstHeader, string[] lines)
    {
        if (firstHeader.Length < 10 || lines.Length < 2)
            return false;

        if (firstHeader[1].Trim() != "Star1" ||
            firstHeader[4].Trim() != "Star2" ||
            firstHeader[7].Trim() != "Star3")
            return false;

        string[] secondHeader = lines[1].Split('\t');
        string[] expected = { "Atk", "Def", "Weight" };
        for (int star = 0; star < 3; star++)
        {
            for (int field = 0; field < expected.Length; field++)
            {
                int column = 1 + star * 3 + field;
                if (column >= secondHeader.Length ||
                    !string.Equals(
                        secondHeader[column].Trim(),
                        expected[field],
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private static bool IsCombinedStatColumnFormat(string[] headers)
    {
        return headers.Length >= 4 &&
               headers[1].Trim() == "Star1" &&
               headers[2].Trim() == "Star2" &&
               headers[3].Trim() == "Star3";
    }

    private static string GetCell(string[] cells, int index)
    {
        return index < cells.Length ? cells[index].Trim() : string.Empty;
    }

    private static bool TryParseNonNegativeInt(string text, out int value)
    {
        return int.TryParse(
                   text.Trim(),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value >= 0;
    }

    private static void AddOption(
        List<CharacterStatSettings.StarStats>[,] options,
        int cost,
        int star,
        int attack,
        int defense,
        int weight)
    {
        options[cost, star].Add(new CharacterStatSettings.StarStats
        {
            attack = attack,
            defense = defense,
            weight = weight
        });
    }

    private static int CountOptions(List<CharacterStatSettings.StarStats>[,] options)
    {
        int count = 0;
        for (int cost = 1; cost <= 5; cost++)
            for (int star = 1; star <= 3; star++)
                count += options[cost, star].Count;
        return count;
    }
}
