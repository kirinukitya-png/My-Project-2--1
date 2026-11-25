using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ProjectNST.GameCore.Strategy;
using UnityEngine;

/// <summary>Unity-independent validated representation of a saved CompositionScene deck.</summary>
internal sealed class CompositionRosterDocument
{
    public string FormationName { get; }
    public IReadOnlyList<string> CharacterAssetNames { get; }

    internal CompositionRosterDocument(string formationName, IEnumerable<string> names)
    {
        FormationName = formationName ?? string.Empty;
        CharacterAssetNames = new ReadOnlyCollection<string>(names.ToArray());
    }
}

internal sealed class CompositionRosterReadResult
{
    public bool Succeeded { get; }
    public string Error { get; }
    public string SourceKey { get; }
    public CompositionRosterDocument Document { get; }

    internal CompositionRosterReadResult(bool succeeded, string error, string sourceKey,
        CompositionRosterDocument document)
    {
        Succeeded = succeeded;
        Error = error ?? string.Empty;
        SourceKey = sourceKey ?? string.Empty;
        Document = document;
    }
}

/// <summary>Plain name/ID pair so name resolution and ID validation do not depend on Unity types.</summary>
internal sealed class CompositionRosterAssetReference
{
    public string AssetName { get; }
    public string DefinitionId { get; }

    public CompositionRosterAssetReference(string assetName, string definitionId)
    {
        AssetName = assetName ?? string.Empty;
        DefinitionId = definitionId ?? string.Empty;
    }
}

internal sealed class CompositionRosterResolution
{
    public bool Succeeded { get; }
    public string Error { get; }
    public IReadOnlyList<string> DefinitionIds { get; }

    internal CompositionRosterResolution(bool succeeded, string error, IEnumerable<string> ids)
    {
        Succeeded = succeeded;
        Error = error ?? string.Empty;
        DefinitionIds = new ReadOnlyCollection<string>((ids ?? Array.Empty<string>()).ToArray());
    }
}

/// <summary>Reads the existing PlayerPrefs format without opening or mutating CompositionScene.</summary>
internal static class CompositionRosterDataReader
{
    internal const string V2Key = "CompositionScene.SavedDeck.v2.0";
    internal const string LegacyV1Key = "CompositionScene.SavedDeck.v1";
    internal const int ExpectedSlotCount = 30;

    [Serializable]
    private sealed class SavedDeckJson
    {
        public List<SavedSlotJson> slots;
        public string formationName;
    }

    [Serializable]
    private sealed class SavedSlotJson
    {
        public int slotIndex;
        public string characterAssetName;
    }

    internal static CompositionRosterReadResult ReadFromPlayerPrefs()
    {
        if (PlayerPrefs.HasKey(V2Key))
            return ParseJson(PlayerPrefs.GetString(V2Key), V2Key);

        // DeckSaveLoad migrates v1 to v2 on CompositionScene.Start. Read legacy data in-place
        // when v2 is absent; the batch reader deliberately does not write or delete PlayerPrefs.
        if (PlayerPrefs.HasKey(LegacyV1Key))
            return ParseJson(PlayerPrefs.GetString(LegacyV1Key), LegacyV1Key + " (read-only fallback)");

        return Failure("編成1の保存データがありません。CompositionSceneで編成1を保存してください。", V2Key);
    }

    internal static CompositionRosterReadResult ParseJson(string json, string sourceKey = V2Key)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Failure("編成保存JSONが空です。", sourceKey);

        SavedDeckJson parsed;
        try
        {
            parsed = JsonUtility.FromJson<SavedDeckJson>(json);
        }
        catch (Exception exception)
        {
            return Failure("編成保存JSONを解析できません: " + exception.Message, sourceKey);
        }

        if (parsed == null || parsed.slots == null)
            return Failure("編成JSONにslots配列がありません。", sourceKey);
        if (parsed.slots.Count != ExpectedSlotCount)
            return Failure("編成Slot数は30である必要があります。実際: " + parsed.slots.Count, sourceKey);

        var bySlot = new string[ExpectedSlotCount];
        var usedSlots = new HashSet<int>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < parsed.slots.Count; i++)
        {
            SavedSlotJson slot = parsed.slots[i];
            if (slot == null)
                return Failure("編成JSONのSlot " + i + " がnullです。", sourceKey);
            if (slot.slotIndex < 0 || slot.slotIndex >= ExpectedSlotCount)
                return Failure("Slot番号が範囲外です: " + slot.slotIndex, sourceKey);
            if (!usedSlots.Add(slot.slotIndex))
                return Failure("Slot番号が重複しています: " + slot.slotIndex, sourceKey);
            if (string.IsNullOrWhiteSpace(slot.characterAssetName))
                return Failure("キャラクターが未設定のSlotがあります: " + slot.slotIndex, sourceKey);
            if (!usedNames.Add(slot.characterAssetName))
                return Failure("同一キャラクター名が複数Slotにあります: " + slot.characterAssetName, sourceKey);
            bySlot[slot.slotIndex] = slot.characterAssetName;
        }

        if (usedNames.Count != ExpectedSlotCount)
            return Failure("編成キャラクターが30人揃っていません。", sourceKey);
        return new CompositionRosterReadResult(true, null, sourceKey,
            new CompositionRosterDocument(parsed.formationName, bySlot));
    }

    internal static CompositionRosterResolution ResolveDefinitionIds(
        CompositionRosterDocument document, IEnumerable<CompositionRosterAssetReference> assets)
    {
        if (document == null) return ResolveFailure("検証済み編成データがありません。");
        if (assets == null) return ResolveFailure("CharacterData Asset一覧がありません。");

        var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (CompositionRosterAssetReference asset in assets)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.AssetName)) continue;
            if (!byName.TryGetValue(asset.AssetName, out List<string> ids))
            {
                ids = new List<string>();
                byName.Add(asset.AssetName, ids);
            }
            ids.Add(asset.DefinitionId);
        }

        var orderedIds = new List<string>(ExpectedSlotCount);
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        for (int slot = 0; slot < document.CharacterAssetNames.Count; slot++)
        {
            string assetName = document.CharacterAssetNames[slot];
            if (!byName.TryGetValue(assetName, out List<string> matches))
                return ResolveFailure("CharacterData Asset名が見つかりません (Slot " + slot + "): " + assetName);
            if (matches.Count != 1)
                return ResolveFailure("CharacterData Asset名が曖昧です (Slot " + slot + "): " + assetName);
            string definitionId = matches[0];
            if (string.IsNullOrWhiteSpace(definitionId))
                return ResolveFailure("Definition IDを取得できません (Slot " + slot + "): " + assetName);
            if (!uniqueIds.Add(definitionId))
                return ResolveFailure("同じDefinition IDが複数Slotにあります: " + definitionId);
            orderedIds.Add(definitionId);
        }

        if (orderedIds.Count != ExpectedSlotCount)
            return ResolveFailure("Definition IDが30件揃っていません。");
        return new CompositionRosterResolution(true, null, orderedIds);
    }

    /// <summary>SHA-256 over length-prefixed ordered IDs; independent of runtime GetHashCode behavior.</summary>
    internal static string ComputeFingerprint(IEnumerable<string> orderedDefinitionIds)
    {
        if (orderedDefinitionIds == null) throw new ArgumentNullException(nameof(orderedDefinitionIds));
        string[] ids = orderedDefinitionIds.ToArray();
        if (ids.Length != ExpectedSlotCount || ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ExpectedSlotCount)
            throw new ArgumentException("A roster fingerprint requires exactly 30 unique non-empty Definition IDs.", nameof(orderedDefinitionIds));

        var canonical = new StringBuilder("composition-roster-v1|");
        canonical.Append(ids.Length.ToString(CultureInfo.InvariantCulture)).Append('|');
        foreach (string id in ids)
            canonical.Append(id.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(id);

        using (SHA256 sha = SHA256.Create())
        {
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
            var hex = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest) hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return "composition-roster-v1-" + hex;
        }
    }

    internal static string CreateCheckpointConfigurationId(string fingerprint) =>
        "unity-shop-scene-snapshot-v1|roster:" + RequireFingerprint(fingerprint);

    internal static string CreateBatchConfigurationId(string fingerprint, string eventItemDefinitionId) =>
        CreateBatchConfigurationId(fingerprint, eventItemDefinitionId, new[] {
            HeuristicStrategy.StableId + "-v" + HeuristicStrategy.StableVersion,
            RandomStrategy.StableId + "-v" + RandomStrategy.StableVersion });

    internal static string CreateBatchConfigurationId(string fingerprint, string eventItemDefinitionId,
        IEnumerable<string> strategyIdentities)
    {
        if (string.IsNullOrWhiteSpace(eventItemDefinitionId))
            throw new ArgumentException("Event Item Definition ID is required.", nameof(eventItemDefinitionId));
        if (strategyIdentities == null) throw new ArgumentNullException(nameof(strategyIdentities));
        string[] identities = strategyIdentities.ToArray();
        if (identities.Length == 0 || identities.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one complete Strategy ID/Version is required.", nameof(strategyIdentities));
        if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Length)
            throw new ArgumentException("Strategy identities must not be duplicated.", nameof(strategyIdentities));
        Array.Sort(identities, StringComparer.Ordinal);
        return "unity-shop-100-v1|roster:" + RequireFingerprint(fingerprint) + "|event-item:" + eventItemDefinitionId +
            "|strategies:" + string.Join(";", identities) + "|pool-accounting:v2";
    }

    private static string RequireFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("composition-roster-v1-", StringComparison.Ordinal))
            throw new ArgumentException("A Composition roster fingerprint is required.", nameof(value));
        return value;
    }

    private static CompositionRosterReadResult Failure(string error, string sourceKey) =>
        new CompositionRosterReadResult(false, error, sourceKey, null);

    private static CompositionRosterResolution ResolveFailure(string error) =>
        new CompositionRosterResolution(false, error, null);
}
