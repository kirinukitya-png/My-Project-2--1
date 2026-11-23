using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Battle;

namespace ProjectNST.GameUnityAdapter
{
    /// <summary>Read-only conversion of TargetData's saved Japanese selectors into GameCore definitions.</summary>
    public static class TargetDataMapper
    {
        public static BattleTargetDefinition ToDefinition(TargetData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.targetId))
                throw new ArgumentException("TargetData requires a non-empty targetId.", nameof(source));

            BattleTargetFilter filter;
            BattleTargetSelection cellSelection = BattleTargetSelection.Uniform;
            int cellCount = 0;
            List<BattleTargetWeightRule> cellWeights = null;
            string candidate = (source.candidateFilter ?? string.Empty).Trim();
            switch (candidate)
            {
                case "全マス": filter = BattleTargetFilter.AllBoard; break;
                case "前列": filter = BattleTargetFilter.FrontColumn; break;
                case "中列": filter = BattleTargetFilter.MiddleColumn; break;
                case "後列": filter = BattleTargetFilter.BackColumn; break;
                case "直線": filter = BattleTargetFilter.SameEnemyRow; break;
                default:
                    if (!TryParseCellSelection(candidate, out cellSelection, out cellCount, out cellWeights))
                        throw new FormatException("Unsupported TargetData candidateFilter for " + source.targetId + ": " + candidate);
                    filter = BattleTargetFilter.RandomCells;
                    break;
            }

            if (!TryParseSelection(source.selectionMethod, out BattleTargetSelection selection,
                    out int targetCount, out List<BattleTargetWeightRule> weights))
                throw new FormatException("Unsupported TargetData selectionMethod for " + source.targetId + ": " + source.selectionMethod);
            if (filter == BattleTargetFilter.RandomCells && selection != BattleTargetSelection.All)
                throw new FormatException("Cell-targeting TargetData must select all occupants of sampled cells: " + source.targetId);

            return new BattleTargetDefinition(source.targetId, filter, selection, targetCount,
                weights, cellSelection, cellCount, cellWeights);
        }

        private static bool TryParseCellSelection(string raw, out BattleTargetSelection selection,
            out int count, out List<BattleTargetWeightRule> weights)
        {
            selection = BattleTargetSelection.Uniform; count = 0; weights = new List<BattleTargetWeightRule>();
            string[] parts = Split(raw);
            if (parts.Length < 2 || !TryMode(parts[0], out selection) || selection == BattleTargetSelection.All ||
                !int.TryParse(parts[1], out count) || count <= 0) return false;
            if (selection == BattleTargetSelection.Uniform) return parts.Length == 2;
            return ParseWeights(parts, weights);
        }

        private static bool TryParseSelection(string raw, out BattleTargetSelection selection,
            out int count, out List<BattleTargetWeightRule> weights)
        {
            selection = BattleTargetSelection.Uniform; count = 1; weights = new List<BattleTargetWeightRule>();
            string[] parts = Split(raw);
            if (parts.Length == 1 && parts[0] == "全員選択")
            { selection = BattleTargetSelection.All; return true; }
            if (parts.Length < 2 || !TryMode(parts[0], out selection) || selection == BattleTargetSelection.All ||
                !int.TryParse(parts[1], out count) || count <= 0) return false;
            if (selection == BattleTargetSelection.Uniform) return parts.Length == 2;
            return ParseWeights(parts, weights);
        }

        private static bool TryMode(string value, out BattleTargetSelection selection)
        {
            if (value == "均等抽選") { selection = BattleTargetSelection.Uniform; return true; }
            if (value == "重み付き抽選") { selection = BattleTargetSelection.Weighted; return true; }
            selection = BattleTargetSelection.Uniform; return false;
        }

        private static bool ParseWeights(string[] parts, List<BattleTargetWeightRule> rules)
        {
            if (parts.Length < 4 || (parts.Length - 2) % 2 != 0) return false;
            for (int i = 2; i < parts.Length; i += 2)
            {
                if (!TryPosition(parts[i], out BattleTargetPosition position) ||
                    !int.TryParse(parts[i + 1], out int weight) || weight < 0) return false;
                rules.Add(new BattleTargetWeightRule(position, weight));
            }
            return true;
        }

        private static bool TryPosition(string value, out BattleTargetPosition position)
        {
            switch (value)
            {
                case "前列": position = BattleTargetPosition.Front; return true;
                case "中列": position = BattleTargetPosition.Middle; return true;
                case "後列": position = BattleTargetPosition.Back; return true;
                case "直線": position = BattleTargetPosition.SameEnemyRow; return true;
                case "else": position = BattleTargetPosition.Else; return true;
                default: position = BattleTargetPosition.Else; return false;
            }
        }

        private static string[] Split(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
            string[] raw = value.Split(',');
            var result = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++) result[i] = raw[i].Trim();
            return result;
        }
    }
}
