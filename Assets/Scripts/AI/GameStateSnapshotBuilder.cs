using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameStateSnapshotBuilder
{
    [Serializable]
    public class Snapshot
    {
        public SourceStatus stageStatus = new SourceStatus();
        public StageSnapshot stage = new StageSnapshot();
        public SourceStatus playerStatus = new SourceStatus();
        public PlayerSnapshot player = new PlayerSnapshot();
        public SourceStatus ownedCharactersStatus = new SourceStatus();
        public OwnedCharacterSnapshot[] ownedCharacters = new OwnedCharacterSnapshot[0];
        public SourceStatus boardStatus = new SourceStatus();
        public SlotSnapshot[] board = new SlotSnapshot[0];
        public SourceStatus handStatus = new SourceStatus();
        public SlotSnapshot[] hand = new SlotSnapshot[0];
        public SourceStatus shopStatus = new SourceStatus();
        public ShopSlotSnapshot[] shop = new ShopSlotSnapshot[0];

        public SourceStatus encyclopediaStatus = new SourceStatus();
        public int selectedInitialRosterCount;
        public string encyclopediaSource;
        public SourceStatus relevantCharactersStatus = new SourceStatus();
        public RelevantCharacterSnapshot[] relevantCharacters = new RelevantCharacterSnapshot[0];

        public SourceStatus unitMembershipStatus = new SourceStatus();
        public int unitCountBeforeFilter;
        public int unitCountAfterFilter;
        public int attributeCountBeforeFilter;
        public int attributeCountAfterFilter;
        public UnitSynergySnapshot[] completeUnitsInInitialRoster = new UnitSynergySnapshot[0];
        public AttributeSynergySnapshot[] sharedAttributesInInitialRoster = new AttributeSynergySnapshot[0];

        public SourceStatus boardSynergyStatus = new SourceStatus();
        public SynergySnapshot[] unitCountsOnBoard = new SynergySnapshot[0];
        public SynergySnapshot[] attributeCountsOnBoard = new SynergySnapshot[0];
        public string synergyNotes =
            "Complete units and shared attributes use the saved initial 30. Board arrays retain current raw counts.";
    }

    [Serializable]
    public class SourceStatus { public bool available; public string detail = "取得不可"; }

    [Serializable]
    public class StageSnapshot
    {
        public string displayedStage;
        public bool hasStageNumber;
        public int stageNumber;
        public bool hasRoundNumber;
        public int roundNumber;
        public bool hasEventStageValue;
        public bool eventStage;
    }

    [Serializable]
    public class PlayerSnapshot { public int gold; public int hp; public int maxHp; }

    [Serializable]
    public class OwnedCharacterSnapshot
    {
        public int characterIndex;
        public string assetName;
        public string characterName;
        public int cost;
        public int star;
        public bool statsAvailable;
        public string statsStatus;
        public int attack;
        public int defense;
        public int battleAttackBonus;
        public int battleDefenseBonus;
    }

    [Serializable]
    public class SlotSnapshot
    {
        public int slotIndex;
        public bool occupied;
        public string contentType;
        public string itemName;
        public string characterAssetName;
        public bool hasCharacterIndex;
        public int characterIndex;
        public string untrackedCharacterName;
    }

    [Serializable]
    public class ShopSlotSnapshot
    {
        public int slotIndex;
        public string contentType;
        public string characterAssetName;
        public string characterName;
        public bool hasCost;
        public int cost;
        public string itemName;
    }

    [Serializable]
    public class RelevantCharacterSnapshot
    {
        public string assetName;
        public string characterName;
        public string[] units = new string[0];
        public string[] attributes = new string[0];
    }

    [Serializable]
    public class UnitSynergySnapshot
    {
        public string name;
        public string[] characterNames = new string[0];
        public int totalMemberCount;
        public int selectedRosterMemberCount;
        public int boardCount;
        public bool boardCountAvailable;
    }

    [Serializable]
    public class AttributeSynergySnapshot
    {
        public string name;
        public int selectedRosterCount;
        public int boardCount;
        public bool boardCountAvailable;
    }

    [Serializable]
    public class SynergySnapshot
    {
        public string name;
        public int boardCount;
        public bool boardCountAvailable;
    }

    [Serializable]
    public class CompactAnalysisSnapshot
    {
        public bool stageAvailable;
        public string stage;
        public bool playerAvailable;
        public int gold;
        public int hp;
        public int maxHp;
        public bool ownedCharactersAvailable;
        public bool boardAvailable;
        public int boardSlotCount;
        public CompactSlot[] board = new CompactSlot[0];
        public bool handAvailable;
        public int handSlotCount;
        public CompactSlot[] hand = new CompactSlot[0];
        public bool shopAvailable;
        public int shopSlotCount;
        public CompactShopOffer[] shop = new CompactShopOffer[0];
        public bool encyclopediaAvailable;
        public bool relevantCharacterDataComplete;
        public int selectedInitialRosterCount;
        public string encyclopediaSource;
        public bool unitMembershipAvailable;
        public int completeUnitCountInInitialRoster;
        public int sharedAttributeCountInInitialRoster;
        public CompactCharacterData[] characters = new CompactCharacterData[0];
        public CompactOwnedCharacter[] ownedCharacters = new CompactOwnedCharacter[0];
        public CompactUnit[] units = new CompactUnit[0];
        public CompactAttribute[] attributes = new CompactAttribute[0];
        public CompactBoardTagCount[] otherBoardUnits = new CompactBoardTagCount[0];
        public CompactBoardTagCount[] otherBoardAttributes = new CompactBoardTagCount[0];
        public bool boardCountsAvailable;
        public string tagScope =
            "Unit/Attribute catalogs include only tags relevant to current owned, board, hand, or shop characters. Other board counts are raw, not confirmed synergies.";
    }

    [Serializable]
    public class CompactCharacterData
    {
        public string id;
        public string name;
        public int cost;
        public bool hasCost;
        public bool tagsAvailable;
        public string[] unitIds = new string[0];
        public string[] attributeIds = new string[0];
    }

    [Serializable]
    public class CompactOwnedCharacter
    {
        public string id;
        public string characterId;
        public int star;
        public bool statsAvailable;
        public int attack;
        public int defense;
        public int battleAttackBonus;
        public int battleDefenseBonus;
    }

    [Serializable]
    public class CompactSlot
    {
        public int slot;
        public string type;
        public string characterId;
        public string ownedId;
        public string itemName;
        public string untrackedName;
    }

    [Serializable]
    public class CompactShopOffer
    {
        public int slot;
        public string type;
        public string characterId;
        public int price;
        public string itemName;
    }

    [Serializable]
    public class CompactUnit
    {
        public string id;
        public string name;
        public int initialRosterMemberCount;
        public int totalMemberCount;
        public int boardCount;
        public int ownedCount;
        public int shopCount;
    }

    [Serializable]
    public class CompactAttribute
    {
        public string id;
        public string name;
        public int initialRosterCount;
        public int boardCount;
        public int ownedCount;
        public int shopCount;
    }

    [Serializable]
    public class CompactBoardTagCount
    {
        public string name;
        public int count;
    }
    public static Snapshot Build()
    {
        Snapshot snapshot = new Snapshot();
        StageManager stageManager = UnityEngine.Object.FindAnyObjectByType<StageManager>();
        ShopManager shopManager = UnityEngine.Object.FindAnyObjectByType<ShopManager>();
        PlayerData player = UnityEngine.Object.FindAnyObjectByType<PlayerData>();
        BoardManager boardManager = UnityEngine.Object.FindAnyObjectByType<BoardManager>();
        HandManager handManager = player != null && player.handManager != null
            ? player.handManager
            : UnityEngine.Object.FindAnyObjectByType<HandManager>();
        CharacterEncyclopediaStore.EncyclopediaData encyclopedia =
            CharacterEncyclopediaStore.CurrentData;

        BuildStage(snapshot, stageManager, shopManager);
        BuildPlayerAndOwnedCharacters(snapshot, player);
        List<BoardSlot> playerBoardSlots = BuildBoard(snapshot, boardManager, player);
        BuildHand(snapshot, handManager, player);
        BuildShop(snapshot, shopManager);
        BuildSynergies(snapshot, playerBoardSlots, encyclopedia);
        BuildRelevantCharacters(snapshot, playerBoardSlots, handManager, shopManager, encyclopedia);
        return snapshot;
    }

    private static void BuildStage(Snapshot snapshot, StageManager stageManager, ShopManager shopManager)
    {
        string displayedStage = null;
        if (shopManager != null)
        {
            snapshot.stage.hasEventStageValue = true;
            snapshot.stage.eventStage = shopManager.IsEventStage;
            if (shopManager.stageCountText != null)
                displayedStage = shopManager.stageCountText.text;
        }

        if (stageManager != null)
        {
            snapshot.stage.hasStageNumber = true;
            snapshot.stage.stageNumber = stageManager.CurrentStageNumber;
            snapshot.stage.hasRoundNumber = true;
            snapshot.stage.roundNumber = stageManager.CurrentRoundNumber;
        }

        if (!string.IsNullOrWhiteSpace(displayedStage))
        {
            snapshot.stage.displayedStage = displayedStage;
            snapshot.stageStatus.available = true;
            snapshot.stageStatus.detail = "ShopManagerのステージ表示とStageManagerから取得";
        }
        else if (stageManager != null)
        {
            snapshot.stage.displayedStage = stageManager.CurrentStageNumber + "-" + stageManager.CurrentRoundNumber;
            snapshot.stageStatus.available = true;
            snapshot.stageStatus.detail = "StageManagerから取得。イベント表記は取得できない場合があります。";
        }
        else
        {
            snapshot.stageStatus.available = false;
            snapshot.stageStatus.detail = "StageManagerとステージ表示テキストを取得できません。";
        }
    }

    private static void BuildPlayerAndOwnedCharacters(Snapshot snapshot, PlayerData player)
    {
        if (player == null)
        {
            snapshot.playerStatus.available = false;
            snapshot.playerStatus.detail = "PlayerDataがシーンにありません。";
            snapshot.ownedCharactersStatus.available = false;
            snapshot.ownedCharactersStatus.detail = "PlayerDataがシーンにありません。";
            return;
        }

        snapshot.player.gold = player.gold;
        snapshot.player.hp = player.hp;
        snapshot.player.maxHp = player.InitialHp;
        snapshot.playerStatus.available = true;
        snapshot.playerStatus.detail = "PlayerDataから取得";

        if (player.ownedCharacters == null)
        {
            snapshot.ownedCharactersStatus.available = false;
            snapshot.ownedCharactersStatus.detail = "PlayerData.ownedCharactersがnullです。";
            return;
        }

        List<OwnedCharacterSnapshot> characters = new List<OwnedCharacterSnapshot>();
        for (int i = 0; i < player.ownedCharacters.Count; i++)
        {
            OwnedCharacter owned = player.ownedCharacters[i];
            if (owned == null || owned.data == null)
                continue;

            OwnedCharacterSnapshot character = new OwnedCharacterSnapshot
            {
                characterIndex = i,
                assetName = owned.data.name,
                characterName = owned.data.characterName,
                cost = owned.data.cost,
                star = owned.star,
                statsAvailable = owned.hasRolledStats,
                statsStatus = owned.hasRolledStats ? "保存済み" : "未抽選"
            };

            // Avoid GetAttack/GetDefense because those methods can roll missing stats.
            if (owned.hasRolledStats)
            {
                character.attack = owned.attack;
                character.defense = owned.defense;
                character.battleAttackBonus = owned.battleAttackBonus;
                character.battleDefenseBonus = owned.battleDefenseBonus;
            }
            characters.Add(character);
        }

        snapshot.ownedCharacters = characters.ToArray();
        snapshot.ownedCharactersStatus.available = true;
        snapshot.ownedCharactersStatus.detail = "PlayerData.ownedCharactersから基本情報を取得";
    }

    private static List<BoardSlot> BuildBoard(Snapshot snapshot, BoardManager boardManager, PlayerData player)
    {
        List<BoardSlot> result = new List<BoardSlot>();
        if (boardManager == null || boardManager.boardArea == null)
        {
            snapshot.boardStatus.available = false;
            snapshot.boardStatus.detail = "BoardManagerまたはBoardManager.boardAreaがありません。";
            return result;
        }

        BoardSlot[] allSlots = boardManager.boardArea.GetComponentsInChildren<BoardSlot>(true);
        List<SlotSnapshot> entries = new List<SlotSnapshot>();
        int index = 0;
        foreach (BoardSlot slot in allSlots)
        {
            if (slot == null || !slot.acceptsPlayerCharacters)
                continue;

            result.Add(slot);
            OwnedCharacter character = slot.placedCharacter;
            bool hasIndex = TryFindCharacterIndex(character, player, out int characterIndex);
            entries.Add(new SlotSnapshot
            {
                slotIndex = index++,
                occupied = !slot.IsEmpty(),
                contentType = slot.IsEmpty() ? "empty" : "character",
                characterAssetName = character != null && character.data != null ? character.data.name : null,
                hasCharacterIndex = hasIndex,
                characterIndex = characterIndex,
                untrackedCharacterName = !hasIndex && character != null && character.data != null
                    ? character.data.characterName
                    : null
            });
        }

        snapshot.board = entries.ToArray();
        snapshot.boardStatus.available = true;
        snapshot.boardStatus.detail = "BoardManager.boardArea内のプレイヤー用BoardSlotから取得";
        return result;
    }

    private static void BuildHand(Snapshot snapshot, HandManager handManager, PlayerData player)
    {
        if (handManager == null || handManager.handArea == null)
        {
            snapshot.handStatus.available = false;
            snapshot.handStatus.detail = "HandManagerまたはHandManager.handAreaがありません。";
            return;
        }

        HandSlot[] slots = handManager.handArea.GetComponentsInChildren<HandSlot>(true);
        List<SlotSnapshot> entries = new List<SlotSnapshot>();
        for (int i = 0; i < slots.Length; i++)
        {
            HandSlot slot = slots[i];
            if (slot == null)
                continue;
            OwnedCharacter character = slot.ownedCharacter;
            bool hasIndex = TryFindCharacterIndex(character, player, out int characterIndex);
            entries.Add(new SlotSnapshot
            {
                slotIndex = i,
                occupied = !slot.IsEmpty(),
                contentType = character != null ? "character" : slot.itemData != null ? "item" : "empty",
                itemName = slot.itemData != null ? slot.itemData.itemName : null,
                characterAssetName = character != null && character.data != null ? character.data.name : null,
                hasCharacterIndex = hasIndex,
                characterIndex = characterIndex,
                untrackedCharacterName = !hasIndex && character != null && character.data != null
                    ? character.data.characterName
                    : null
            });
        }

        snapshot.hand = entries.ToArray();
        snapshot.handStatus.available = true;
        snapshot.handStatus.detail = "HandManager.handArea内のHandSlotから取得";
    }

    private static void BuildShop(Snapshot snapshot, ShopManager shopManager)
    {
        if (shopManager == null || shopManager.shopArea == null)
        {
            snapshot.shopStatus.available = false;
            snapshot.shopStatus.detail = "ShopManagerまたはShopManager.shopAreaがありません。";
            return;
        }

        ShopSlot[] slots = shopManager.shopArea.GetComponentsInChildren<ShopSlot>(true);
        List<ShopSlotSnapshot> entries = new List<ShopSlotSnapshot>();
        for (int i = 0; i < slots.Length; i++)
        {
            ShopSlot slot = slots[i];
            if (slot == null)
                continue;

            ShopSlotSnapshot entry = new ShopSlotSnapshot { slotIndex = i };
            if (slot.characterData != null)
            {
                entry.contentType = "character";
                entry.characterAssetName = slot.characterData.name;
                entry.characterName = slot.characterData.characterName;
                entry.hasCost = true;
                entry.cost = slot.characterData.cost;
            }
            else if (slot.itemData != null)
            {
                entry.contentType = "item";
                entry.itemName = slot.itemData.itemName;
            }
            else
            {
                entry.contentType = "empty";
            }
            entries.Add(entry);
        }

        snapshot.shop = entries.ToArray();
        snapshot.shopStatus.available = true;
        snapshot.shopStatus.detail = "ShopManager.shopArea内のShopSlotから取得";
    }

    private static void BuildSynergies(
        Snapshot snapshot,
        List<BoardSlot> boardSlots,
        CharacterEncyclopediaStore.EncyclopediaData encyclopedia)
    {
        Dictionary<string, CharacterEncyclopediaStore.EncyclopediaCharacter> characterByAsset =
            BuildCharacterLookup(encyclopedia);
        Dictionary<string, int> unitBoardCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> attributeBoardCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool boardDataComplete = snapshot.boardStatus.available;

        foreach (BoardSlot slot in boardSlots)
        {
            if (slot == null || slot.IsEmpty() || slot.placedCharacter.data == null)
                continue;

            string assetName = slot.placedCharacter.data.name;
            if (!characterByAsset.TryGetValue(assetName, out CharacterEncyclopediaStore.EncyclopediaCharacter stored))
            {
                boardDataComplete = false;
                continue;
            }
            AddTags(stored.units, unitBoardCounts);
            AddTags(stored.attributes, attributeBoardCounts);
        }

        bool encyclopediaValid = encyclopedia != null &&
            encyclopedia.selectedCharacterAssetNames != null &&
            encyclopedia.selectedCharacterAssetNames.Length == 30 &&
            encyclopedia.characters != null &&
            encyclopedia.characters.Length == 30;

        snapshot.encyclopediaStatus.available = encyclopediaValid;
        snapshot.encyclopediaStatus.detail = encyclopediaValid
            ? "persistentDataPathに保存された初期30体の図鑑"
            : "保存済みの初期30体図鑑を利用できません。";
        snapshot.selectedInitialRosterCount = encyclopedia != null && encyclopedia.characters != null
            ? encyclopedia.characters.Length
            : 0;
        snapshot.encyclopediaSource = CharacterEncyclopediaStore.SavePath;

        Dictionary<string, int> selectedAttributeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (encyclopediaValid)
        {
            foreach (CharacterEncyclopediaStore.EncyclopediaCharacter character in encyclopedia.characters)
                AddTags(character.attributes, selectedAttributeCounts);
        }

        List<AttributeSynergySnapshot> sharedAttributes = new List<AttributeSynergySnapshot>();
        foreach (KeyValuePair<string, int> pair in selectedAttributeCounts)
        {
            if (pair.Value < 2)
                continue;
            attributeBoardCounts.TryGetValue(pair.Key, out int boardCount);
            sharedAttributes.Add(new AttributeSynergySnapshot
            {
                name = pair.Key,
                selectedRosterCount = pair.Value,
                boardCount = boardCount,
                boardCountAvailable = boardDataComplete
            });
        }
        sharedAttributes.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        snapshot.attributeCountBeforeFilter = encyclopediaValid ? selectedAttributeCounts.Count : 0;
        snapshot.attributeCountAfterFilter = sharedAttributes.Count;
        snapshot.sharedAttributesInInitialRoster = sharedAttributes.ToArray();

        Dictionary<string, int> selectedAssetSet = new Dictionary<string, int>(StringComparer.Ordinal);
        if (encyclopediaValid)
            foreach (string assetName in encyclopedia.selectedCharacterAssetNames)
                if (!string.IsNullOrWhiteSpace(assetName) && !selectedAssetSet.ContainsKey(assetName))
                    selectedAssetSet.Add(assetName, 1);

        List<UnitSynergySnapshot> completeUnits = new List<UnitSynergySnapshot>();
        bool unitCatalogAvailable = encyclopediaValid && encyclopedia.unitCatalogAvailable &&
            encyclopedia.allUnitMemberships != null;
        if (unitCatalogAvailable)
        {
            foreach (CharacterEncyclopediaStore.UnitMembership unit in encyclopedia.allUnitMemberships)
            {
                if (unit == null || unit.characterAssetNames == null || unit.characterAssetNames.Length == 0)
                    continue;
                int selectedMembers = 0;
                foreach (string memberAsset in unit.characterAssetNames)
                    if (selectedAssetSet.ContainsKey(memberAsset))
                        selectedMembers++;
                if (selectedMembers != unit.characterAssetNames.Length)
                    continue;

                unitBoardCounts.TryGetValue(unit.unitName, out int boardCount);
                completeUnits.Add(new UnitSynergySnapshot
                {
                    name = unit.unitName,
                    characterNames = unit.characterNames != null
                        ? (string[])unit.characterNames.Clone()
                        : new string[0],
                    totalMemberCount = unit.characterAssetNames.Length,
                    selectedRosterMemberCount = selectedMembers,
                    boardCount = boardCount,
                    boardCountAvailable = boardDataComplete
                });
            }
        }
        completeUnits.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        snapshot.unitCountBeforeFilter = unitCatalogAvailable ? encyclopedia.allUnitMemberships.Length : 0;
        snapshot.unitCountAfterFilter = completeUnits.Count;
        snapshot.completeUnitsInInitialRoster = completeUnits.ToArray();
        snapshot.unitMembershipStatus.available = unitCatalogAvailable;
        snapshot.unitMembershipStatus.detail = !encyclopediaValid
            ? "初期30体の図鑑を利用できないため判定不可"
            : !unitCatalogAvailable
                ? encyclopedia.unitCatalogStatus
                : "保存済みfullCharacterRoster所属一覧と初期30体のassetNameを照合";

        snapshot.unitCountsOnBoard = BuildBoardCountList(unitBoardCounts, boardDataComplete);
        snapshot.attributeCountsOnBoard = BuildBoardCountList(attributeBoardCounts, boardDataComplete);
        snapshot.boardSynergyStatus.available = boardDataComplete;
        snapshot.boardSynergyStatus.detail = boardDataComplete
            ? "保存済みキャラクター図鑑を使い、盤面のUnit/Attribute人数を集計"
            : "図鑑にない盤面キャラクターがあるため、一部の盤面タグ人数は取得不可";
    }

    private static void BuildRelevantCharacters(
        Snapshot snapshot,
        List<BoardSlot> boardSlots,
        HandManager handManager,
        ShopManager shopManager,
        CharacterEncyclopediaStore.EncyclopediaData encyclopedia)
    {
        if (encyclopedia == null || encyclopedia.characters == null)
        {
            snapshot.relevantCharactersStatus.available = false;
            snapshot.relevantCharactersStatus.detail = "保存済み図鑑がありません。";
            return;
        }

        HashSet<string> relevantAssets = new HashSet<string>(StringComparer.Ordinal);
        if (snapshot.ownedCharacters != null)
            foreach (OwnedCharacterSnapshot owned in snapshot.ownedCharacters)
                if (owned != null && !string.IsNullOrWhiteSpace(owned.assetName))
                    relevantAssets.Add(owned.assetName);
        foreach (BoardSlot slot in boardSlots)
            if (slot != null && !slot.IsEmpty() && slot.placedCharacter.data != null)
                relevantAssets.Add(slot.placedCharacter.data.name);

        if (handManager != null && handManager.handArea != null)
            foreach (HandSlot slot in handManager.handArea.GetComponentsInChildren<HandSlot>(true))
                if (slot != null && slot.ownedCharacter != null && slot.ownedCharacter.data != null)
                    relevantAssets.Add(slot.ownedCharacter.data.name);

        if (shopManager != null && shopManager.shopArea != null)
            foreach (ShopSlot slot in shopManager.shopArea.GetComponentsInChildren<ShopSlot>(true))
                if (slot != null && slot.characterData != null)
                    relevantAssets.Add(slot.characterData.name);

        HashSet<string> allowedUnits = new HashSet<string>(StringComparer.Ordinal);
        foreach (UnitSynergySnapshot unit in snapshot.completeUnitsInInitialRoster)
            allowedUnits.Add(unit.name);
        HashSet<string> allowedAttributes = new HashSet<string>(StringComparer.Ordinal);
        foreach (AttributeSynergySnapshot attribute in snapshot.sharedAttributesInInitialRoster)
            allowedAttributes.Add(attribute.name);

        Dictionary<string, CharacterEncyclopediaStore.EncyclopediaCharacter> byAsset =
            BuildCharacterLookup(encyclopedia);
        List<string> assetNames = new List<string>(relevantAssets);
        assetNames.Sort(StringComparer.Ordinal);
        List<RelevantCharacterSnapshot> result = new List<RelevantCharacterSnapshot>();
        int missing = 0;
        foreach (string assetName in assetNames)
        {
            if (!byAsset.TryGetValue(assetName, out CharacterEncyclopediaStore.EncyclopediaCharacter stored))
            {
                missing++;
                continue;
            }
            result.Add(new RelevantCharacterSnapshot
            {
                assetName = stored.assetName,
                characterName = stored.characterName,
                units = FilterTags(stored.units, allowedUnits),
                attributes = FilterTags(stored.attributes, allowedAttributes)
            });
        }

        snapshot.relevantCharacters = result.ToArray();
        snapshot.relevantCharactersStatus.available = missing == 0;
        snapshot.relevantCharactersStatus.detail = missing == 0
            ? "盤面・手持ち・ショップに関係するキャラクターを保存済み図鑑と照合"
            : missing + "体が保存済み図鑑に見つかりません。推測せず除外しました。";
    }

    private static Dictionary<string, CharacterEncyclopediaStore.EncyclopediaCharacter> BuildCharacterLookup(
        CharacterEncyclopediaStore.EncyclopediaData encyclopedia)
    {
        Dictionary<string, CharacterEncyclopediaStore.EncyclopediaCharacter> result =
            new Dictionary<string, CharacterEncyclopediaStore.EncyclopediaCharacter>(StringComparer.Ordinal);
        if (encyclopedia == null || encyclopedia.characters == null)
            return result;
        foreach (CharacterEncyclopediaStore.EncyclopediaCharacter character in encyclopedia.characters)
        {
            if (character != null && !string.IsNullOrWhiteSpace(character.assetName) &&
                !result.ContainsKey(character.assetName))
                result.Add(character.assetName, character);
        }
        return result;
    }

    private static SynergySnapshot[] BuildBoardCountList(Dictionary<string, int> counts, bool available)
    {
        List<string> names = new List<string>(counts.Keys);
        names.Sort(StringComparer.Ordinal);
        List<SynergySnapshot> result = new List<SynergySnapshot>();
        foreach (string name in names)
            result.Add(new SynergySnapshot { name = name, boardCount = counts[name], boardCountAvailable = available });
        return result.ToArray();
    }

    private static void AddTags(string[] tags, Dictionary<string, int> counts)
    {
        if (tags == null)
            return;
        HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || !unique.Add(tag))
                continue;
            counts.TryGetValue(tag, out int count);
            counts[tag] = count + 1;
        }
    }

    private static string[] FilterTags(string[] tags, HashSet<string> allowed)
    {
        if (tags == null || allowed == null || allowed.Count == 0)
            return new string[0];
        List<string> result = new List<string>();
        foreach (string tag in tags)
            if (!string.IsNullOrWhiteSpace(tag) && allowed.Contains(tag))
                result.Add(tag);
        return result.ToArray();
    }

    private static bool TryFindCharacterIndex(OwnedCharacter character, PlayerData player, out int index)
    {
        index = 0;
        if (character == null || player == null || player.ownedCharacters == null)
            return false;
        for (int i = 0; i < player.ownedCharacters.Count; i++)
        {
            if (ReferenceEquals(player.ownedCharacters[i], character) && character.data != null)
            {
                index = i;
                return true;
            }
        }
        return false;
    }
    public static CompactAnalysisSnapshot CreateCompactSnapshot(Snapshot source)
    {
        CompactAnalysisSnapshot compact = new CompactAnalysisSnapshot();
        if (source == null)
            return compact;

        compact.stageAvailable = source.stageStatus != null && source.stageStatus.available;
        compact.stage = source.stage != null ? source.stage.displayedStage : null;
        compact.playerAvailable = source.playerStatus != null && source.playerStatus.available;
        if (source.player != null)
        {
            compact.gold = source.player.gold;
            compact.hp = source.player.hp;
            compact.maxHp = source.player.maxHp;
        }
        compact.ownedCharactersAvailable =
            source.ownedCharactersStatus != null && source.ownedCharactersStatus.available;
        compact.boardAvailable = source.boardStatus != null && source.boardStatus.available;
        compact.handAvailable = source.handStatus != null && source.handStatus.available;
        compact.shopAvailable = source.shopStatus != null && source.shopStatus.available;
        compact.boardCountsAvailable =
            source.boardSynergyStatus != null && source.boardSynergyStatus.available;
        compact.boardSlotCount = compact.boardAvailable && source.board != null ? source.board.Length : 0;
        compact.handSlotCount = compact.handAvailable && source.hand != null ? source.hand.Length : 0;
        compact.shopSlotCount = compact.shopAvailable && source.shop != null ? source.shop.Length : 0;
        compact.encyclopediaAvailable =
            source.encyclopediaStatus != null && source.encyclopediaStatus.available;
        compact.relevantCharacterDataComplete =
            source.relevantCharactersStatus != null && source.relevantCharactersStatus.available;
        compact.selectedInitialRosterCount = source.selectedInitialRosterCount;
        compact.encyclopediaSource = source.encyclopediaSource;
        compact.unitMembershipAvailable =
            source.unitMembershipStatus != null && source.unitMembershipStatus.available;
        compact.completeUnitCountInInitialRoster = source.unitCountAfterFilter;
        compact.sharedAttributeCountInInitialRoster = source.attributeCountAfterFilter;

        Dictionary<string, CompactCharacterData> characterByAsset =
            new Dictionary<string, CompactCharacterData>(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> unitNamesByAsset =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> attributeNamesByAsset =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        if (source.ownedCharacters != null)
        {
            foreach (OwnedCharacterSnapshot owned in source.ownedCharacters)
            {
                if (owned == null)
                    continue;
                CompactCharacterData character = GetOrCreateCompactCharacter(
                    characterByAsset, owned.assetName, owned.characterName);
                if (character != null && owned.cost > 0)
                {
                    character.cost = owned.cost;
                    character.hasCost = true;
                }
            }
        }

        if (source.relevantCharacters != null)
        {
            foreach (RelevantCharacterSnapshot relevant in source.relevantCharacters)
            {
                if (relevant == null)
                    continue;
                CompactCharacterData character = GetOrCreateCompactCharacter(
                    characterByAsset, relevant.assetName, relevant.characterName);
                if (character == null)
                    continue;
                character.tagsAvailable = true;
                unitNamesByAsset[relevant.assetName] = ToUniqueSet(relevant.units);
                attributeNamesByAsset[relevant.assetName] = ToUniqueSet(relevant.attributes);
            }
        }

        if (source.shop != null)
        {
            foreach (ShopSlotSnapshot offer in source.shop)
            {
                if (offer == null || offer.contentType != "character")
                    continue;
                CompactCharacterData character = GetOrCreateCompactCharacter(
                    characterByAsset, offer.characterAssetName, offer.characterName);
                if (character != null && offer.hasCost)
                {
                    character.cost = offer.cost;
                    character.hasCost = true;
                }
            }
        }

        List<string> assetNames = new List<string>(characterByAsset.Keys);
        assetNames.Sort(StringComparer.Ordinal);
        Dictionary<string, string> characterIdByAsset =
            new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < assetNames.Count; i++)
        {
            string assetName = assetNames[i];
            string characterId = "C" + (i + 1);
            characterIdByAsset.Add(assetName, characterId);
            characterByAsset[assetName].id = characterId;
        }

        Dictionary<int, string> ownedIdBySourceIndex = new Dictionary<int, string>();
        List<CompactOwnedCharacter> ownedCharacters = new List<CompactOwnedCharacter>();
        Dictionary<string, int> ownedCountByUnit = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> ownedCountByAttribute = new Dictionary<string, int>(StringComparer.Ordinal);
        if (source.ownedCharacters != null)
        {
            foreach (OwnedCharacterSnapshot owned in source.ownedCharacters)
            {
                if (owned == null ||
                    !characterIdByAsset.TryGetValue(owned.assetName, out string characterId))
                    continue;

                string ownedId = "O" + (owned.characterIndex + 1);
                ownedIdBySourceIndex[owned.characterIndex] = ownedId;
                ownedCharacters.Add(new CompactOwnedCharacter
                {
                    id = ownedId,
                    characterId = characterId,
                    star = owned.star,
                    statsAvailable = owned.statsAvailable,
                    attack = owned.attack,
                    defense = owned.defense,
                    battleAttackBonus = owned.battleAttackBonus,
                    battleDefenseBonus = owned.battleDefenseBonus
                });

                if (unitNamesByAsset.TryGetValue(owned.assetName, out HashSet<string> ownedUnits))
                    IncrementTagCounts(ownedUnits, ownedCountByUnit);
                if (attributeNamesByAsset.TryGetValue(owned.assetName, out HashSet<string> ownedAttributes))
                    IncrementTagCounts(ownedAttributes, ownedCountByAttribute);
            }
        }
        compact.ownedCharacters = ownedCharacters.ToArray();

        List<string> relevantUnitNames = new List<string>();
        List<string> relevantAttributeNames = new List<string>();
        HashSet<string> unitNameSet = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> attributeNameSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (HashSet<string> tags in unitNamesByAsset.Values)
            unitNameSet.UnionWith(tags);
        foreach (HashSet<string> tags in attributeNamesByAsset.Values)
            attributeNameSet.UnionWith(tags);

        Dictionary<string, UnitSynergySnapshot> completeUnitByName =
            new Dictionary<string, UnitSynergySnapshot>(StringComparer.Ordinal);
        if (source.completeUnitsInInitialRoster != null)
        {
            foreach (UnitSynergySnapshot unit in source.completeUnitsInInitialRoster)
            {
                if (unit == null)
                    continue;
                completeUnitByName[unit.name] = unit;
                if (unit.boardCount > 0)
                    unitNameSet.Add(unit.name);
            }
        }

        Dictionary<string, AttributeSynergySnapshot> sharedAttributeByName =
            new Dictionary<string, AttributeSynergySnapshot>(StringComparer.Ordinal);
        if (source.sharedAttributesInInitialRoster != null)
        {
            foreach (AttributeSynergySnapshot attribute in source.sharedAttributesInInitialRoster)
            {
                if (attribute == null)
                    continue;
                sharedAttributeByName[attribute.name] = attribute;
                if (attribute.boardCount > 0)
                    attributeNameSet.Add(attribute.name);
            }
        }

        relevantUnitNames.AddRange(unitNameSet);
        relevantUnitNames.Sort(StringComparer.Ordinal);
        relevantAttributeNames.AddRange(attributeNameSet);
        relevantAttributeNames.Sort(StringComparer.Ordinal);

        Dictionary<string, string> unitIdByName = new Dictionary<string, string>(StringComparer.Ordinal);
        List<CompactUnit> compactUnits = new List<CompactUnit>();
        for (int i = 0; i < relevantUnitNames.Count; i++)
        {
            string name = relevantUnitNames[i];
            string unitId = "U" + (i + 1);
            unitIdByName.Add(name, unitId);
            completeUnitByName.TryGetValue(name, out UnitSynergySnapshot complete);
            int boardCount = complete != null
                ? complete.boardCount
                : FindBoardCount(source.unitCountsOnBoard, name);
            int ownedCount = ownedCountByUnit.TryGetValue(name, out int ownedCountValue)
                ? ownedCountValue
                : 0;
            int shopCount = CountShopOffersWithTag(
                source.shop, characterIdByAsset, unitNamesByAsset, name);

            compactUnits.Add(new CompactUnit
            {
                id = unitId,
                name = name,
                initialRosterMemberCount = complete != null ? complete.selectedRosterMemberCount : 0,
                totalMemberCount = complete != null ? complete.totalMemberCount : 0,
                boardCount = boardCount,
                ownedCount = ownedCount,
                shopCount = shopCount,
            });
        }

        Dictionary<string, string> attributeIdByName =
            new Dictionary<string, string>(StringComparer.Ordinal);
        List<CompactAttribute> compactAttributes = new List<CompactAttribute>();
        for (int i = 0; i < relevantAttributeNames.Count; i++)
        {
            string name = relevantAttributeNames[i];
            string attributeId = "A" + (i + 1);
            attributeIdByName.Add(name, attributeId);
            sharedAttributeByName.TryGetValue(name, out AttributeSynergySnapshot shared);
            compactAttributes.Add(new CompactAttribute
            {
                id = attributeId,
                name = name,
                initialRosterCount = shared != null ? shared.selectedRosterCount : 0,
                boardCount = shared != null
                    ? shared.boardCount
                    : FindBoardCount(source.attributeCountsOnBoard, name),
                ownedCount = ownedCountByAttribute.TryGetValue(name, out int ownedAttributeCount)
                    ? ownedAttributeCount
                    : 0,
                shopCount = CountShopOffersWithTag(
                    source.shop, characterIdByAsset, attributeNamesByAsset, name)
            });
        }
        compact.units = compactUnits.ToArray();
        compact.attributes = compactAttributes.ToArray();

        List<CompactCharacterData> characters = new List<CompactCharacterData>();
        foreach (string assetName in assetNames)
        {
            CompactCharacterData character = characterByAsset[assetName];
            character.unitIds = ConvertTagNamesToIds(
                unitNamesByAsset.TryGetValue(assetName, out HashSet<string> units) ? units : null,
                unitIdByName);
            character.attributeIds = ConvertTagNamesToIds(
                attributeNamesByAsset.TryGetValue(assetName, out HashSet<string> attributes) ? attributes : null,
                attributeIdByName);
            characters.Add(character);
        }
        compact.characters = characters.ToArray();

        compact.board = CompactSlots(
            source.board, characterIdByAsset, ownedIdBySourceIndex);
        compact.hand = CompactSlots(
            source.hand, characterIdByAsset, ownedIdBySourceIndex);
        compact.shop = CompactShopOffers(source.shop, characterIdByAsset);

        compact.otherBoardUnits = BuildOtherBoardCounts(
            source.unitCountsOnBoard, unitIdByName);
        compact.otherBoardAttributes = BuildOtherBoardCounts(
            source.attributeCountsOnBoard, attributeIdByName);
        return compact;
    }

    private static CompactCharacterData GetOrCreateCompactCharacter(
        Dictionary<string, CompactCharacterData> characters,
        string assetName,
        string characterName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
            return null;
        if (!characters.TryGetValue(assetName, out CompactCharacterData character))
        {
            character = new CompactCharacterData
            {
                name = characterName,
                tagsAvailable = false
            };
            characters.Add(assetName, character);
        }
        else if (string.IsNullOrWhiteSpace(character.name))
        {
            character.name = characterName;
        }
        return character;
    }

    private static HashSet<string> ToUniqueSet(string[] values)
    {
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        if (values != null)
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
        return result;
    }

    private static void IncrementTagCounts(HashSet<string> tags, Dictionary<string, int> counts)
    {
        foreach (string tag in tags)
        {
            counts.TryGetValue(tag, out int count);
            counts[tag] = count + 1;
        }
    }

    private static string[] ConvertTagNamesToIds(
        HashSet<string> tagNames,
        Dictionary<string, string> idByName)
    {
        if (tagNames == null || tagNames.Count == 0)
            return new string[0];
        List<string> names = new List<string>(tagNames);
        names.Sort(StringComparer.Ordinal);
        List<string> ids = new List<string>();
        foreach (string name in names)
            if (idByName.TryGetValue(name, out string id))
                ids.Add(id);
        return ids.ToArray();
    }

    private static int FindBoardCount(SynergySnapshot[] counts, string name)
    {
        if (counts == null)
            return 0;
        foreach (SynergySnapshot count in counts)
            if (count != null && count.name == name)
                return count.boardCount;
        return 0;
    }

    private static int CountShopOffersWithTag(
        ShopSlotSnapshot[] offers,
        Dictionary<string, string> characterIdByAsset,
        Dictionary<string, HashSet<string>> tagsByAsset,
        string tag)
    {
        if (offers == null)
            return 0;
        int count = 0;
        foreach (ShopSlotSnapshot offer in offers)
            if (offer != null && offer.contentType == "character" &&
                !string.IsNullOrWhiteSpace(offer.characterAssetName) &&
                characterIdByAsset.ContainsKey(offer.characterAssetName) &&
                tagsByAsset.TryGetValue(offer.characterAssetName, out HashSet<string> tags) &&
                tags.Contains(tag))
                count++;
        return count;
    }

    private static CompactSlot[] CompactSlots(
        SlotSnapshot[] source,
        Dictionary<string, string> characterIdByAsset,
        Dictionary<int, string> ownedIdBySourceIndex)
    {
        if (source == null || source.Length == 0)
            return new CompactSlot[0];

        List<CompactSlot> result = new List<CompactSlot>();
        foreach (SlotSnapshot slot in source)
        {
            if (slot == null || !slot.occupied)
                continue;

            string characterId = null;
            if (!string.IsNullOrWhiteSpace(slot.characterAssetName))
                characterIdByAsset.TryGetValue(slot.characterAssetName, out characterId);
            string ownedId = null;
            if (slot.hasCharacterIndex)
                ownedIdBySourceIndex.TryGetValue(slot.characterIndex, out ownedId);

            result.Add(new CompactSlot
            {
                slot = slot.slotIndex,
                type = slot.contentType,
                characterId = characterId,
                ownedId = ownedId,
                itemName = slot.itemName,
                untrackedName = slot.untrackedCharacterName
            });
        }
        return result.ToArray();
    }

    private static CompactShopOffer[] CompactShopOffers(
        ShopSlotSnapshot[] source,
        Dictionary<string, string> characterIdByAsset)
    {
        if (source == null || source.Length == 0)
            return new CompactShopOffer[0];

        List<CompactShopOffer> result = new List<CompactShopOffer>();
        foreach (ShopSlotSnapshot offer in source)
        {
            if (offer == null || offer.contentType == "empty")
                continue;
            string characterId = null;
            if (!string.IsNullOrWhiteSpace(offer.characterAssetName))
                characterIdByAsset.TryGetValue(offer.characterAssetName, out characterId);
            result.Add(new CompactShopOffer
            {
                slot = offer.slotIndex,
                type = offer.contentType,
                characterId = characterId,
                price = offer.hasCost ? offer.cost : 0,
                itemName = offer.itemName
            });
        }
        return result.ToArray();
    }

    private static CompactBoardTagCount[] BuildOtherBoardCounts(
        SynergySnapshot[] counts,
        Dictionary<string, string> knownTagIds)
    {
        if (counts == null || counts.Length == 0)
            return new CompactBoardTagCount[0];

        List<CompactBoardTagCount> result = new List<CompactBoardTagCount>();
        foreach (SynergySnapshot entry in counts)
        {
            if (entry == null || knownTagIds.ContainsKey(entry.name))
                continue;
            result.Add(new CompactBoardTagCount { name = entry.name, count = entry.boardCount });
        }
        return result.ToArray();
    }
}


