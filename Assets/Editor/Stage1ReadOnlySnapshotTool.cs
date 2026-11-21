// Copy this file into Assets/Editor/ in the Unity project.
// Editor-only, manual, read-only inspection utility for Stage 1.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class Stage1ReadOnlySnapshotTool
{
    [Serializable] private class Pair { public string key; public string value; }
    [Serializable] private class CharacterRecord
    {
        public string id, name, definition, units, attributes;
        public int cost, star, attack, defense, battleAttackBonus, battleDefenseBonus;
        public bool hasRolledStats;
    }
    [Serializable] private class SlotRecord
    {
        public int index, row, column;
        public string characterId, itemId, itemName, characterName;
        public bool occupied;
    }
    [Serializable] private class EnemyRecord
    {
        public int index, row, column, attackBonus, defenseBonus, healthBonus, gapBonus;
        public string definition, name;
    }
    [Serializable] private class DefinitionRecord { public string type, asset, fields; }
    [Serializable] private class Snapshot
    {
        public string capturedAt, phase, scene, fingerprintBefore, fingerprintAfter;
        public bool sourceStateUnchanged;
        public List<Pair> status = new List<Pair>();
        public List<CharacterRecord> characters = new List<CharacterRecord>();
        public List<SlotRecord> board = new List<SlotRecord>();
        public List<SlotRecord> hand = new List<SlotRecord>();
        public List<SlotRecord> shop = new List<SlotRecord>();
        public List<EnemyRecord> enemies = new List<EnemyRecord>();
        public List<Pair> poolAvailableCopies = new List<Pair>();
        public List<Pair> configuration = new List<Pair>();
        public List<string> warnings = new List<string>();
    }

    private static readonly Dictionary<object, string> InstanceIds = new Dictionary<object, string>(ReferenceComparer.Instance);
    private static int nextId = 1;

    [MenuItem("Tools/Stage 1/Capture Read-only Game Snapshot (Play Mode)")]
    private static void CaptureSnapshot()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("Stage 1 snapshot is available in Play Mode. Enter Play Mode and invoke this menu manually after initialization or after a normal player action.");
            return;
        }
        Snapshot snapshot = Capture(false);
        Debug.Log("Stage 1 Read-only Snapshot\n" + JsonUtility.ToJson(snapshot, true));
        Debug.Log("Snapshot captured manually. No game action was invoked. sourceStateUnchanged=" + snapshot.sourceStateUnchanged);
    }

    [MenuItem("Tools/Stage 1/Read Saved Scene Config (Edit Mode)")]
    private static void CaptureSavedConfig()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Exit Play Mode to read saved Scene Inspector values. This command does not save or modify the Scene.");
            return;
        }
        Snapshot snapshot = Capture(true);
        Debug.Log("Stage 1 Saved Scene Config\n" + JsonUtility.ToJson(snapshot, true));
    }

    [MenuItem("Tools/Stage 1/Map ScriptableObject Definitions (Read-only)")]
    private static void MapDefinitions()
    {
        string[] types = { "CharacterData", "EnemyData", "ItemData", "TargetData", "EffectData", "CharacterStatSettings", "EnemyFormationData", "EncounterPoolData" };
        var records = new List<DefinitionRecord>();
        foreach (string typeName in types)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeName))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null || asset.GetType().Name != typeName) continue;
                records.Add(new DefinitionRecord { type = typeName, asset = path, fields = ReadDefinitionFields(asset) });
            }
        }
        StringBuilder output = new StringBuilder("Stage 1 Definition Mapper (read-only)\n");
        foreach (DefinitionRecord record in records)
            output.Append(record.type).Append(" | ").Append(record.asset).Append("\n").Append(record.fields).Append("\n");
        Debug.Log(output.ToString());
    }

    private static Snapshot Capture(bool configOnly)
    {
        var result = new Snapshot
        {
            capturedAt = DateTime.Now.ToString("o"),
            phase = configOnly ? "SavedSceneEditMode" : "PlayModeManualCapture",
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        };
        MonoBehaviour[] components = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        var valid = new List<MonoBehaviour>();
        foreach (MonoBehaviour component in components)
            if (component != null && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded)
                valid.Add(component);

        object player = Find(valid, "PlayerData");
        object shop = Find(valid, "ShopManager");
        object boardManager = Find(valid, "BoardManager");
        object handManager = Find(valid, "HandManager");
        object stage = Find(valid, "StageManager");
        object enemyManager = Find(valid, "EnemyManager");
        object battle = Find(valid, "BattleManager");
        object statSettings = FindStatSettings(valid);

        result.fingerprintBefore = Fingerprint(player, shop, boardManager, handManager, stage, enemyManager);
        AddField(result.status, "gold", Get(player, "gold"));
        AddField(result.status, "hp", Get(player, "hp"));
        AddField(result.status, "initialHp", EditorApplication.isPlaying ? Get(player, "initialHp") : "not serialized; initialized from hp during Awake");
        AddField(result.status, "xp", Get(shop, "currentLevelExperience"));
        AddField(result.status, "level", GetProperty(shop, "CurrentLevel"));
        AddField(result.status, "stage", GetProperty(stage, "CurrentStageNumber"));
        AddField(result.status, "round", GetProperty(stage, "CurrentRoundNumber"));
        AddField(result.status, "awaitingStageSelection", GetProperty(stage, "IsAwaitingStageSelection"));
        AddField(result.status, "stageIndex", Get(shop, "currentStageIndex"));
        AddField(result.status, "stageChoice", Get(shop, "currentSelection"));
        AddField(result.status, "eventChancePercent", Get(shop, "eventChancePercent"));
        AddField(result.status, "battleInProgress", GetProperty(battle, "IsBattleInProgress"));

        ReadRoster(result, Get(shop, "characters"), "initialRoster");
        ReadOwned(result, Get(player, "ownedCharacters"));
        ReadBoard(result, boardManager);
        ReadHand(result, handManager);
        ReadShop(result, shop);
        ReadPool(result, shop);
        ReadEnemies(result, enemyManager);
        ReadConfiguration(result, player, shop, boardManager, handManager, enemyManager, statSettings);
        CheckReferences(result);
        CheckPlayerDataReference(result, player, boardManager);
        if (battle != null && Equals(GetProperty(battle, "IsBattleInProgress"), true))
            result.warnings.Add("Battle is in progress. Snapshot captures exposed state only; live coroutine combat state is not inspected.");
        if (player == null) result.warnings.Add("PlayerData component not found in loaded Scene.");
        if (shop == null) result.warnings.Add("ShopManager component not found in loaded Scene.");
        if (configOnly) result.warnings.Add("This is a read-only view of currently loaded Scene objects. It does not export configuration and does not save the Scene.");
        result.fingerprintAfter = Fingerprint(player, shop, boardManager, handManager, stage, enemyManager);
        result.sourceStateUnchanged = result.fingerprintBefore == result.fingerprintAfter;
        if (!result.sourceStateUnchanged) result.warnings.Add("Source fingerprint changed during capture. Review before trusting this snapshot.");
        return result;
    }

    private static void CheckReferences(Snapshot result)
    {
        var placed = new HashSet<string>();
        var duplicated = new HashSet<string>();
        foreach (SlotRecord slot in result.board)
            if (!string.IsNullOrEmpty(slot.characterId) && !placed.Add(slot.characterId)) duplicated.Add(slot.characterId);
        foreach (SlotRecord slot in result.hand)
            if (!string.IsNullOrEmpty(slot.characterId) && !placed.Add(slot.characterId)) duplicated.Add(slot.characterId);
        foreach (string id in duplicated) result.warnings.Add("Character instance appears in multiple Board/Hand slots: " + id);
        var owned = new HashSet<string>();
        foreach (CharacterRecord character in result.characters) owned.Add(character.id);
        foreach (string id in placed) if (!owned.Contains(id)) result.warnings.Add("Board/Hand references an instance absent from PlayerData.ownedCharacters: " + id);
        foreach (string id in owned) if (!placed.Contains(id)) result.warnings.Add("Owned instance is not in a Board/Hand slot; verify if reserve/unplaced ownership is valid: " + id);
        result.status.Add(new Pair { key = "integrity.ownedCount", value = owned.Count.ToString() });
        result.status.Add(new Pair { key = "integrity.placedUniqueCount", value = placed.Count.ToString() });
        result.status.Add(new Pair { key = "integrity.duplicateBoardHandRefs", value = duplicated.Count.ToString() });
        if (result.poolAvailableCopies.Count == 0) result.warnings.Add("Pool-to-offer/ownership conservation was not evaluated: runtime pool or baseline counts unavailable.");
        else result.warnings.Add("Pool conservation must be reviewed per CharacterData using initialPoolCountsByCost, remaining pool copies, current Shop offers, and owned star-equivalent copies (★1=1, ★2=3, ★3=9). This tool reports the inputs and does not alter/fix mismatches.");
    }
    private static void CheckPlayerDataReference(Snapshot result, object snapshotPlayer, object boardManager)
    {
        object boardPlayer = Get(boardManager, "playerData");
        if (snapshotPlayer != null && boardPlayer != null && !ReferenceEquals(snapshotPlayer, boardPlayer))
            result.warnings.Add("PlayerData reference mismatch: Snapshot PlayerData differs from BoardManager.playerData.");
    }

    private static void ReadRoster(Snapshot result, object value, string label)
    {
        if (value == null) { result.warnings.Add(label + " unavailable"); return; }
        int count = 0;
        foreach (object entry in Enumerate(value))
        {
            if (entry == null) continue;
            count++;
            AddField(result.status, label + "[" + (count - 1) + "]", Name(entry));
        }
        AddField(result.status, label + "Count", count);
    }

    private static void ReadOwned(Snapshot result, object value)
    {
        foreach (object owned in Enumerate(value))
        {
            if (owned == null) continue;
            object data = Get(owned, "data");
            if (data == null) continue;
            result.characters.Add(new CharacterRecord
            {
                id = Id(owned), name = Name(data), definition = AssetPath(data),
                cost = Int(Get(data, "cost")), star = Int(Get(owned, "star")),
                attack = Int(Get(owned, "attack")), defense = Int(Get(owned, "defense")),
                battleAttackBonus = Int(Get(owned, "battleAttackBonus")), battleDefenseBonus = Int(Get(owned, "battleDefenseBonus")),
                hasRolledStats = Bool(Get(owned, "hasRolledStats")),
                units = Join(Get(data, "units")), attributes = Join(Get(data, "attributes"))
            });
        }
    }

    private static void ReadBoard(Snapshot result, object manager)
    {
        object area = Get(manager, "boardArea");
        int columns = Int(Get(manager, "columns")), rows = Int(Get(manager, "rows"));
        result.configuration.Add(new Pair { key = "board.columns", value = Value(Get(manager, "columns")) });
        result.configuration.Add(new Pair { key = "board.rows", value = Value(Get(manager, "rows")) });
        var slots = ComponentsIn(area as Transform, "BoardSlot");
        for (int i = 0; i < slots.Count; i++)
        {
            object character = Get(slots[i], "placedCharacter");
            object item = Get(slots[i], "eventItemData");
            object data = Get(character, "data");
            bool hasCharacter = character != null && data != null;
            result.board.Add(new SlotRecord { index = i, row = columns > 0 ? i / columns : -1, column = columns > 0 ? i % columns : -1,
                occupied = hasCharacter || item != null, characterId = hasCharacter ? Id(character) : "", characterName = hasCharacter ? Name(data) : "", itemId = ReadString(item, "itemId"), itemName = ReadString(item, "itemName") });
        }
        if (area == null) result.warnings.Add("Board slots unavailable: BoardManager.boardArea was null.");
    }

    private static void ReadHand(Snapshot result, object manager)
    {
        object area = Get(manager, "handArea");
        int count = Int(Get(manager, "slotCount"));
        result.configuration.Add(new Pair { key = "hand.slotCount", value = Value(Get(manager, "slotCount")) });
        var slots = ComponentsIn(area as Transform, "HandSlot");
        for (int i = 0; i < slots.Count; i++)
        {
            object character = Get(slots[i], "ownedCharacter"), item = Get(slots[i], "itemData");
            object data = Get(character, "data");
            bool hasCharacter = character != null && data != null;
            result.hand.Add(new SlotRecord { index = i, row = -1, column = i, occupied = hasCharacter || item != null,
                characterId = hasCharacter ? Id(character) : "", characterName = hasCharacter ? Name(data) : "", itemId = ReadString(item, "itemId"), itemName = ReadString(item, "itemName") });
        }
        if (area == null) result.warnings.Add("Hand slots unavailable: HandManager.handArea was null.");
        if (slots.Count != count) result.warnings.Add("Hand slotCount differs from discovered HandSlot components.");
    }

    private static void ReadShop(Snapshot result, object manager)
    {
        object area = Get(manager, "shopArea");
        var slots = ComponentsIn(area as Transform, "ShopSlot");
        for (int i = 0; i < slots.Count; i++)
        {
            object character = Get(slots[i], "characterData"), item = Get(slots[i], "itemData");
            result.shop.Add(new SlotRecord { index = i, row = -1, column = i, occupied = character != null || item != null,
                characterName = Name(character), itemId = ReadString(item, "itemId"), itemName = ReadString(item, "itemName") });
            AddField(result.status, "shop[" + i + "].characterCost", Get(character, "cost"));
            AddField(result.status, "shop[" + i + "].itemPrice", Get(item, "purchasePrice"));
        }
        if (area == null) result.warnings.Add("Shop offers unavailable: ShopManager.shopArea was null.");
    }

    private static void ReadPool(Snapshot result, object manager)
    {
        object pool = Get(manager, "characterPool");
        if (!(pool is IDictionary dictionary)) { result.warnings.Add("Character pool unavailable (private characterPool was not a readable IDictionary)."); return; }
        foreach (DictionaryEntry entry in dictionary)
            result.poolAvailableCopies.Add(new Pair { key = Name(entry.Key), value = Value(entry.Value) });
    }

    private static void ReadEnemies(Snapshot result, object manager)
    {
        object dataBySlot = Get(manager, "enemyDataBySlot");
        int columns = Int(Get(manager, "columns"));
        int i = 0;
        foreach (object enemy in Enumerate(dataBySlot))
        {
            result.enemies.Add(new EnemyRecord { index = i, row = columns > 0 ? i / columns : -1, column = columns > 0 ? i % columns : -1,
                definition = AssetPath(enemy), name = ReadString(enemy, "enemyName"), attackBonus = Int(InvokeReadOnly(manager, "GetBattleAttackBonus", i)),
                defenseBonus = Int(InvokeReadOnly(manager, "GetBattleDefenseBonus", i)), healthBonus = Int(InvokeReadOnly(manager, "GetBattleHealthBonus", i)), gapBonus = Int(InvokeReadOnly(manager, "GetBattleGapBonus", i)) });
            i++;
        }
        result.configuration.Add(new Pair { key = "enemyBoard.columns", value = Value(Get(manager, "columns")) });
        result.configuration.Add(new Pair { key = "enemyBoard.rows", value = Value(Get(manager, "rows")) });
    }

    private static void ReadConfiguration(Snapshot result, object player, object shop, object board, object hand, object enemy, object statSettings)
    {
        string[] shopFields = { "shopSlotsByStage", "rerollCost", "stageGoldReward", "goldPerInterest", "interestGoldPerThreshold", "maxInterestGold", "emptyHandSlotsPerBonusGold", "bonusGoldPerEmptyHandSlots", "initialPoolCountsByCost", "charactersPerCost", "stageCostAppearanceRates", "requiredScoresByStage", "xpPurchaseGoldCost", "xpPerPurchase", "xpSalePurchaseGoldCost", "xpSalePerPurchase", "selectAExperienceReward", "selectBGoldReward", "selectBExperienceReward", "selectCGoldReward", "selectCExperienceReward", "selectDGoldReward", "selectDExperienceReward", "selectDHealAmount", "bossGoldReward", "bossExperienceReward", "eventItemId", "experienceToNextLevel" };
        AddFields(result.configuration, "player", player, new[] { "gold", "hp" });
        result.configuration.Add(new Pair { key = "player.initialHp", value = EditorApplication.isPlaying ? Value(GetProperty(player, "InitialHp")) : "not serialized; initialized from hp during Awake" });
        AddFields(result.configuration, "shop", shop, shopFields);
        AddFields(result.configuration, "board", board, new[] { "columns", "rows" });
        AddFields(result.configuration, "hand", hand, new[] { "slotCount" });
        AddFields(result.configuration, "enemy", enemy, new[] { "columns", "rows", "eventItemSlotIndex" });
        if (statSettings != null) result.configuration.Add(new Pair { key = "characterStatSettings.asset", value = AssetPath(statSettings) });
    }

    private static void AddFields(List<Pair> target, string prefix, object source, string[] names)
    {
        foreach (string name in names) target.Add(new Pair { key = prefix + "." + name, value = Value(Get(source, name)) });
    }

    private static string ReadDefinitionFields(ScriptableObject asset)
    {
        var parts = new List<string>();
        FieldInfo[] fields = asset.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (FieldInfo field in fields)
        {
            object value = field.GetValue(asset);
            if (value is UnityEngine.Object) value = AssetPath(value);
            else if (value is Array || value is IList) value = Join(value);
            parts.Add(field.Name + "=" + Value(value));
        }
        parts.Add("classification=" + Classification(asset.GetType().Name));
        return string.Join("; ", parts.ToArray());
    }

    private static string Classification(string type)
    {
        switch (type)
        {
            case "CharacterData": return "gameplay: characterName,cost,attributes,units | presentation: icon";
            case "EnemyData": return "gameplay: enemyId,enemyName,attack,defense,health,cip,ae,pig,target,stages,categories | presentation: icon";
            case "ItemData": return "gameplay: itemId,itemName,purchasePrice,effects,effectValues,target,duration | presentation: description,icon";
            case "TargetData": return "gameplay: targetId,candidateFilter,selectionMethod | presentation: targetText | legacy/unverified: selectSlot,noTarget,selectCharacter";
            case "EffectData": return "gameplay: effectId,effect,effectValue,target,duration | presentation: description";
            case "CharacterStatSettings": return "gameplay: costs and star option attack/defense/weight values";
            case "EnemyFormationData": return "gameplay: enemy slot references,gap,cooldown";
            case "EncounterPoolData": return "gameplay: stage/round/category/formation membership";
            default: return "unclassified; requires review";
        }
    }

    private static string Fingerprint(params object[] objects)
    {
        var b = new StringBuilder();
        foreach (object obj in objects)
        {
            if (obj == null) { b.Append("null|"); continue; }
            b.Append(obj.GetType().Name).Append('{');
            string[] fields = obj.GetType().Name == "PlayerData" ? new[] { "gold", "hp", "ownedCharacters" } :
                obj.GetType().Name == "ShopManager" ? new[] { "currentStageIndex", "currentSelection", "currentLevelExperience", "characterPool", "characters" } :
                obj.GetType().Name == "BoardManager" ? new[] { "columns", "rows", "boardArea" } :
                obj.GetType().Name == "HandManager" ? new[] { "slotCount", "handArea" } :
                obj.GetType().Name == "StageManager" ? new[] { "currentStageIndex", "awaitingStageSelection" } : new[] { "enemyDataBySlot", "currentFormation" };
            foreach (string name in fields)
            {
                object value = Get(obj, name);
                b.Append(name).Append('=');
                if (value is IDictionary map)
                { foreach (DictionaryEntry entry in map) b.Append(Name(entry.Key)).Append("=").Append(Value(entry.Value)).Append(";"); }
                else if (value is IEnumerable sequence && !(value is string))
                {
                    foreach (object item in sequence) AppendFingerprintValue(b, item);
                }
                else AppendFingerprintValue(b, value);
                b.Append('|');
            }
            if (obj.GetType().Name == "BoardManager") AppendSlotFingerprint(b, Get(obj, "boardArea"), "BoardSlot", "placedCharacter", "eventItemData");
            if (obj.GetType().Name == "HandManager") AppendSlotFingerprint(b, Get(obj, "handArea"), "HandSlot", "ownedCharacter", "itemData");
            if (obj.GetType().Name == "ShopManager") AppendSlotFingerprint(b, Get(obj, "shopArea"), "ShopSlot", "characterData", "itemData");
            b.Append('}');
        }
        return b.ToString().GetHashCode().ToString("X8");
    }

    private static void AppendSlotFingerprint(StringBuilder b, object area, string typeName, string first, string second)
    {
        Transform root = area as Transform;
        foreach (object slot in ComponentsIn(root, typeName))
        {
            AppendFingerprintValue(b, Get(slot, first));
            AppendFingerprintValue(b, Get(slot, second));
            AppendFingerprintValue(b, Get(Get(slot, first), "data"));
            AppendFingerprintValue(b, Get(Get(slot, first), "star"));
            AppendFingerprintValue(b, Get(Get(slot, first), "attack"));
            AppendFingerprintValue(b, Get(Get(slot, first), "defense"));
        }
    }

    private static void AppendFingerprintValue(StringBuilder b, object value)
    {
        if (value == null) { b.Append("null;"); return; }
        if (value is UnityEngine.Object) { b.Append(AssetPath(value)).Append(';'); return; }
        if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value is decimal)
        { b.Append(Value(value)).Append(';'); return; }
        object data = Get(value, "data");
        if (data != null)
        {
            b.Append(Id(value)).Append(':').Append(AssetPath(data)).Append(':')
                .Append(Value(Get(value, "star"))).Append(':').Append(Value(Get(value, "attack"))).Append(':')
                .Append(Value(Get(value, "defense"))).Append(';');
            return;
        }
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary) b.Append(Name(entry.Key)).Append('=').Append(Value(entry.Value)).Append(';');
            return;
        }
        b.Append(value.GetType().Name).Append(';');
    }

    private static object FindStatSettings(List<MonoBehaviour> components)
    {
        foreach (MonoBehaviour component in components)
        {
            FieldInfo[] fields = component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields)
                if (field.FieldType.Name == "CharacterStatSettings") return field.GetValue(component);
        }
        return null;
    }
    private static object Find(List<MonoBehaviour> components, string typeName)
    {
        foreach (MonoBehaviour component in components) if (component.GetType().Name == typeName) return component;
        return null;
    }
    private static List<object> ComponentsIn(Transform root, string typeName)
    {
        var result = new List<object>();
        if (root == null) return result;
        foreach (MonoBehaviour c in root.GetComponentsInChildren<MonoBehaviour>(true)) if (c != null && c.GetType().Name == typeName) result.Add(c);
        return result;
    }
    private static object Get(object source, string name)
    {
        if (source == null) return null;
        Type t = source.GetType();
        while (t != null)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(source);
            t = t.BaseType;
        }
        return null;
    }
    private static object GetProperty(object source, string name)
    {
        if (source == null) return null;
        PropertyInfo p = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(source, null) : null;
    }
    private static object InvokeReadOnly(object source, string name, int arg)
    {
        if (source == null) return null;
        MethodInfo method = source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);
        return method == null ? null : method.Invoke(source, new object[] { arg });
    }
    private static IEnumerable Enumerate(object value)
    {
        if (value is IEnumerable enumerable && !(value is string)) return enumerable;
        return new object[0];
    }
    private static string Name(object asset) { return ReadString(asset, "characterName") ?? ReadString(asset, "enemyName") ?? ReadString(asset, "itemName") ?? (asset == null ? "" : asset.ToString()); }
    private static string ReadString(object obj, string field) { object v = Get(obj, field); return v == null ? "" : v.ToString(); }
    private static string AssetPath(object obj) { return obj is UnityEngine.Object u ? AssetDatabase.GetAssetPath(u) : ""; }
    private static string Join(object value)
    {
        var parts = new List<string>();
        foreach (object item in Enumerate(value))
        {
            if (item == null) parts.Add("");
            else if (item is UnityEngine.Object) parts.Add(AssetPath(item));
            else if (item.GetType().IsPrimitive || item is string || item.GetType().IsEnum) parts.Add(item.ToString());
            else
            {
                var fields = new List<string>();
                foreach (FieldInfo field in item.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    fields.Add(field.Name + "=" + Value(field.GetValue(item)));
                parts.Add("{" + string.Join(";", fields.ToArray()) + "}");
            }
        }
        return string.Join(",", parts.ToArray());
    }
    private static int Int(object value) { return value == null ? 0 : Convert.ToInt32(value); }
    private static bool Bool(object value) { return value != null && Convert.ToBoolean(value); }
    private static string Value(object value)
    {
        if (value == null) return "<unavailable/null>";
        if (value is UnityEngine.Object) return AssetPath(value);
        if (value is Array || value is IList) return Join(value);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
    private static void AddField(List<Pair> list, string key, object value) { list.Add(new Pair { key = key, value = Value(value) }); }
    private static string Id(object instance)
    {
        if (instance == null) return "";
        if (!InstanceIds.TryGetValue(instance, out string id)) { id = "C" + nextId++; InstanceIds.Add(instance, id); }
        return id;
    }
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
        public int GetHashCode(object obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
    }
}









