using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CharacterEncyclopediaStore : MonoBehaviour
{
    private const int RequiredRosterSize = 30;
    private const string FileName = "character-encyclopedia.json";
    private static CharacterEncyclopediaStore instance;

    public static EncyclopediaData CurrentData { get; private set; }
    public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

    [Serializable]
    public class EncyclopediaData
    {
        public int schemaVersion = 1;
        public string createdAtUtc;
        public string[] selectedCharacterAssetNames = new string[0];
        public EncyclopediaCharacter[] characters = new EncyclopediaCharacter[0];
        public UnitMembership[] allUnitMemberships = new UnitMembership[0];
        public bool unitCatalogAvailable;
        public string unitCatalogStatus;
    }

    [Serializable]
    public class EncyclopediaCharacter
    {
        public string assetName;
        public string characterName;
        public string[] units = new string[0];
        public string[] attributes = new string[0];
    }

    [Serializable]
    public class UnitMembership
    {
        public string unitName;
        public string[] characterAssetNames = new string[0];
        public string[] characterNames = new string[0];
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        CurrentData = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeService()
    {
        if (instance != null)
            return;

        GameObject serviceObject = new GameObject(nameof(CharacterEncyclopediaStore));
        serviceObject.AddComponent<CharacterEncyclopediaStore>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(CaptureRosterAfterSceneInitialization());
    }

    private IEnumerator CaptureRosterAfterSceneInitialization()
    {
        // ShopManager applies the selected roster in Start; wait until that has completed.
        yield return new WaitForEndOfFrame();

        ShopManager shopManager = FindAnyObjectByType<ShopManager>();
        if (shopManager == null || shopManager.characters == null)
            yield break;

        if (!TryGetUniqueAssetNames(shopManager.characters, out string[] selectedAssetNames))
        {
            Debug.LogWarning(
                "初期30体の図鑑を作成できません。ShopManager.charactersに有効な異なるCharacterDataが30体必要です。",
                shopManager);
            yield break;
        }

        EncyclopediaData saved = LoadFromDisk();
        bool fullRosterAvailable = shopManager.fullCharacterRoster != null && shopManager.fullCharacterRoster.Length > 0;
        if (saved != null && SameRoster(saved.selectedCharacterAssetNames, selectedAssetNames) &&
            (saved.unitCatalogAvailable || !fullRosterAvailable))
        {
            CurrentData = saved;
            Debug.Log("保存済みキャラクター図鑑を再利用します: " + SavePath);
            yield break;
        }

        EncyclopediaData fresh = BuildEncyclopedia(shopManager, selectedAssetNames);
        CurrentData = fresh;
        if (SaveToDisk(fresh))
            Debug.Log("初期30体のキャラクター図鑑を作成・保存しました: " + SavePath);
    }

    private static bool TryGetUniqueAssetNames(CharacterData[] roster, out string[] assetNames)
    {
        assetNames = new string[0];
        if (roster == null)
            return false;

        List<string> names = new List<string>();
        HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (CharacterData character in roster)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.name) ||
                !unique.Add(character.name))
                continue;
            names.Add(character.name);
        }

        if (names.Count != RequiredRosterSize)
            return false;

        assetNames = names.ToArray();
        return true;
    }

    private static bool SameRoster(string[] saved, string[] current)
    {
        if (saved == null || current == null || saved.Length != RequiredRosterSize ||
            current.Length != RequiredRosterSize)
            return false;

        HashSet<string> savedSet = new HashSet<string>(saved, StringComparer.Ordinal);
        if (savedSet.Count != RequiredRosterSize)
            return false;

        foreach (string assetName in current)
            if (!savedSet.Contains(assetName))
                return false;
        return true;
    }

    private static EncyclopediaData BuildEncyclopedia(
        ShopManager shopManager,
        string[] selectedAssetNames)
    {
        Dictionary<string, CharacterData> selectedByAssetName =
            new Dictionary<string, CharacterData>(StringComparer.Ordinal);
        foreach (CharacterData character in shopManager.characters)
            if (character != null && !selectedByAssetName.ContainsKey(character.name))
                selectedByAssetName.Add(character.name, character);

        List<EncyclopediaCharacter> records = new List<EncyclopediaCharacter>();
        foreach (string assetName in selectedAssetNames)
        {
            if (!selectedByAssetName.TryGetValue(assetName, out CharacterData character))
                continue;

            records.Add(new EncyclopediaCharacter
            {
                assetName = character.name,
                characterName = character.characterName,
                units = CopyTags(character.units),
                attributes = CopyTags(character.attributes)
            });
        }

        EncyclopediaData data = new EncyclopediaData
        {
            createdAtUtc = DateTime.UtcNow.ToString("o"),
            selectedCharacterAssetNames = (string[])selectedAssetNames.Clone(),
            characters = records.ToArray()
        };

        CharacterData[] fullRoster = shopManager.fullCharacterRoster;
        if (fullRoster == null || fullRoster.Length == 0)
        {
            data.unitCatalogAvailable = false;
            data.unitCatalogStatus = "ShopManager.fullCharacterRosterが未設定です。Unit所属者の完全判定はできません。";
            data.allUnitMemberships = new UnitMembership[0];
            return data;
        }

        Dictionary<string, List<CharacterData>> membersByUnit =
            new Dictionary<string, List<CharacterData>>(StringComparer.Ordinal);
        HashSet<CharacterData> seenCharacters = new HashSet<CharacterData>();
        foreach (CharacterData character in fullRoster)
        {
            if (character == null || !seenCharacters.Add(character))
                continue;

            HashSet<string> unitsForCharacter = new HashSet<string>(StringComparer.Ordinal);
            foreach (string unit in CopyTags(character.units))
            {
                if (!unitsForCharacter.Add(unit))
                    continue;
                if (!membersByUnit.TryGetValue(unit, out List<CharacterData> members))
                {
                    members = new List<CharacterData>();
                    membersByUnit.Add(unit, members);
                }
                members.Add(character);
            }
        }

        List<string> unitNames = new List<string>(membersByUnit.Keys);
        unitNames.Sort(StringComparer.Ordinal);
        List<UnitMembership> catalog = new List<UnitMembership>();
        foreach (string unitName in unitNames)
        {
            List<CharacterData> members = membersByUnit[unitName];
            string[] memberAssetNames = new string[members.Count];
            string[] memberNames = new string[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                memberAssetNames[i] = members[i].name;
                memberNames[i] = members[i].characterName;
            }

            catalog.Add(new UnitMembership
            {
                unitName = unitName,
                characterAssetNames = memberAssetNames,
                characterNames = memberNames
            });
        }

        data.allUnitMemberships = catalog.ToArray();
        data.unitCatalogAvailable = true;
        data.unitCatalogStatus = "fullCharacterRosterのCharacterData.unitsから作成";
        return data;
    }

    private static EncyclopediaData LoadFromDisk()
    {
        try
        {
            if (!File.Exists(SavePath))
                return null;

            string json = File.ReadAllText(SavePath, Encoding.UTF8);
            EncyclopediaData data = JsonUtility.FromJson<EncyclopediaData>(json);
            if (data == null || data.schemaVersion != 1 ||
                data.selectedCharacterAssetNames == null || data.characters == null)
            {
                Debug.LogWarning("保存済みキャラクター図鑑の形式が不正なため、再作成します。");
                return null;
            }
            return data;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("保存済みキャラクター図鑑を読み込めません: " + exception.Message);
            return null;
        }
    }

    private static bool SaveToDisk(EncyclopediaData data)
    {
        try
        {
            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("キャラクター図鑑を保存できません: " + exception.Message);
            return false;
        }
    }

    private static string[] CopyTags(string[] tags)
    {
        if (tags == null)
            return new string[0];

        List<string> values = new List<string>();
        HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string tag in tags)
            if (!string.IsNullOrWhiteSpace(tag) && unique.Add(tag))
                values.Add(tag);
        return values.ToArray();
    }
}

