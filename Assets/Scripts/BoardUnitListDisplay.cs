using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays Units shared by multiple characters currently placed on the board.
/// This component is independent of CharacterDetailPanel visibility.
/// </summary>
public class BoardUnitListDisplay : MonoBehaviour
{
    public Transform boardUnitList;
    public Button boardUnitButtonTemplate;
    public UnitHoverInfoPanel unitHoverInfoPanel;

    [Min(0.05f)]
    public float refreshInterval = 0.2f;

    private readonly List<Button> generatedButtons = new List<Button>();
    private readonly List<TMP_Text> generatedNameTmpTexts = new List<TMP_Text>();
    private readonly List<TMP_Text> generatedCountTmpTexts = new List<TMP_Text>();
    private readonly List<Text> generatedNameLegacyTexts = new List<Text>();
    private readonly List<Text> generatedCountLegacyTexts = new List<Text>();
    private BoardSlot[] previousSlots = new BoardSlot[0];
    private OwnedCharacter[] previousCharacters = new OwnedCharacter[0];
    private CharacterData[] previousRoster = new CharacterData[0];
    private float refreshTimer;
    private int activeUnitSynergyBonus;

    public int ActiveUnitSynergyBonus => activeUnitSynergyBonus;

    private class UnitEntry
    {
        public string name;
        public int boardCount;
        public int rosterCount;
        public int sourceOrder;
        public readonly HashSet<CharacterData> rosterMembers = new HashSet<CharacterData>();
        public readonly List<BoardSlot> boardSlots = new List<BoardSlot>();
        public bool connected;
        public bool dimmed;
    }

    private void Awake()
    {
        if (boardUnitList == null) boardUnitList = transform;
        ResolveTemplate();
    }

    private void OnEnable()
    {
        RefreshBoardUnits();
        CacheBoardState();
    }

    private void Update()
    {
        refreshTimer += Time.unscaledDeltaTime;
        if (refreshTimer < refreshInterval) return;

        refreshTimer = 0f;
        if (HasStateChanged())
        {
            RefreshBoardUnits();
            CacheBoardState();
        }
    }

    public void RefreshBoardUnits()
    {
        if (boardUnitList == null) boardUnitList = transform;
        if (!ResolveTemplate()) return;

        BoardManager boardManager = FindAnyObjectByType<BoardManager>();
        ShopManager shopManager = FindAnyObjectByType<ShopManager>();
        List<UnitEntry> entries = new List<UnitEntry>();
        CharacterData[] roster = shopManager != null && shopManager.characters != null
            ? shopManager.characters
            : new CharacterData[0];

        if (boardManager != null && boardManager.boardArea != null)
        {
            BoardSlot[] slots = boardManager.boardArea.GetComponentsInChildren<BoardSlot>(true);
            int nextSourceOrder = 0;
            foreach (BoardSlot slot in slots)
            {
                if (slot == null || slot.placedCharacter == null || slot.placedCharacter.data == null)
                    continue;

                string[] units = slot.placedCharacter.data.units;
                if (units == null) continue;

                HashSet<string> unitsOnCharacter = new HashSet<string>();
                foreach (string unit in units)
                {
                    if (string.IsNullOrWhiteSpace(unit) || !unitsOnCharacter.Add(unit)) continue;

                    UnitEntry entry = entries.Find(item => item.name == unit);
                    if (entry == null)
                    {
                        entry = new UnitEntry
                        {
                            name = unit,
                            rosterCount = 0,
                            sourceOrder = nextSourceOrder++
                        };
                        foreach (CharacterData character in roster)
                        {
                            if (character == null || character.units == null) continue;
                            foreach (string characterUnit in character.units)
                            {
                                if (string.Equals(characterUnit, unit, System.StringComparison.Ordinal))
                                {
                                    entry.rosterMembers.Add(character);
                                    break;
                                }
                            }
                        }
                        entry.rosterCount = entry.rosterMembers.Count;
                        entries.Add(entry);
                    }
                    entry.boardCount++;
                    entry.boardSlots.Add(slot);
                }
            }
        }

        // Show only Units represented by at least two board characters and
        // whose complete membership is contained in the selected roster.
        entries.RemoveAll(entry => entry.boardCount < 2 ||
            shopManager == null || !shopManager.IsUnitComplete(entry.name));

        foreach (UnitEntry entry in entries)
        {
            entry.connected = boardManager != null &&
                AreUnitSlotsConnected(entry.boardSlots, boardManager.columns);
            entry.dimmed = entry.boardCount != entry.rosterCount || !entry.connected;
        }

        foreach (UnitEntry smallerEntry in entries)
        {
            foreach (UnitEntry largerEntry in entries)
            {
                if (largerEntry.boardCount != largerEntry.rosterCount ||
                    !largerEntry.connected ||
                    !IsStrictSubset(smallerEntry.rosterMembers, largerEntry.rosterMembers))
                    continue;

                smallerEntry.dimmed = true;
                break;
            }
        }

        activeUnitSynergyBonus = 0;
        foreach (UnitEntry entry in entries)
        {
            if (!entry.dimmed)
                activeUnitSynergyBonus += GetUnitSynergyBonus(entry.rosterCount);
        }

        entries.Sort((a, b) =>
        {
            int dimmedComparison = a.dimmed.CompareTo(b.dimmed);
            if (dimmedComparison != 0) return dimmedComparison;
            int countComparison = a.rosterCount.CompareTo(b.rosterCount);
            return countComparison != 0
                ? countComparison
                : a.sourceOrder.CompareTo(b.sourceOrder);
        });

        while (generatedButtons.Count < entries.Count)
        {
            Button button = Instantiate(boardUnitButtonTemplate, boardUnitList, false);
            button.name = boardUnitButtonTemplate.name + "_Generated_" + generatedButtons.Count;

            TMP_Text nameTmp = FindTmpText(button, "BoardUnitNameText", 0);
            TMP_Text countTmp = FindTmpText(button, "BoardUnitCountText", 1);
            Text nameLegacy = FindLegacyText(button, "BoardUnitNameText", 0);
            Text countLegacy = FindLegacyText(button, "BoardUnitCountText", 1);
            if (nameTmp == null && nameLegacy == null)
            {
                Debug.LogError("BoardUnitButton needs a TMP_Text or UI Text for its Unit name.", button);
                Destroy(button.gameObject);
                return;
            }

            generatedButtons.Add(button);
            generatedNameTmpTexts.Add(nameTmp);
            generatedCountTmpTexts.Add(countTmp);
            generatedNameLegacyTexts.Add(nameLegacy);
            generatedCountLegacyTexts.Add(countLegacy);
            Transform panel = FindChild(button.transform, "Panel");
            if (panel != null) panel.gameObject.SetActive(false);
        }

        for (int i = 0; i < generatedButtons.Count; i++)
        {
            bool visible = i < entries.Count;
            SetButtonVisible(generatedButtons[i], visible, visible && entries[i].dimmed);

            if (visible)
            {
                UnitButtonHover hover = generatedButtons[i].GetComponent<UnitButtonHover>();
                if (hover == null)
                {
                    hover = generatedButtons[i].gameObject.AddComponent<UnitButtonHover>();
                }
                hover.Initialize(entries[i].name, unitHoverInfoPanel, true, true);
            }

            string unitName = visible ? entries[i].name : string.Empty;
            string count = visible
                ? entries[i].boardCount + "/" + entries[i].rosterCount
                : string.Empty;
            SetText(generatedNameTmpTexts[i], generatedNameLegacyTexts[i], unitName);
            SetText(generatedCountTmpTexts[i], generatedCountLegacyTexts[i], count);
        }
    }

    private bool AreUnitSlotsConnected(List<BoardSlot> unitSlots, int columns)
    {
        if (unitSlots.Count < 2 || columns <= 0) return false;

        HashSet<BoardSlot> remaining = new HashSet<BoardSlot>(unitSlots);
        Queue<BoardSlot> queue = new Queue<BoardSlot>();
        BoardSlot first = unitSlots[0];
        remaining.Remove(first);
        queue.Enqueue(first);

        while (queue.Count > 0)
        {
            BoardSlot current = queue.Dequeue();
            int currentIndex = current.transform.GetSiblingIndex();
            int currentRow = currentIndex / columns;
            int currentColumn = currentIndex % columns;

            List<BoardSlot> connectedNeighbors = new List<BoardSlot>();
            foreach (BoardSlot candidate in remaining)
            {
                int candidateIndex = candidate.transform.GetSiblingIndex();
                int candidateRow = candidateIndex / columns;
                int candidateColumn = candidateIndex % columns;
                if (Mathf.Abs(currentRow - candidateRow) +
                    Mathf.Abs(currentColumn - candidateColumn) == 1)
                {
                    connectedNeighbors.Add(candidate);
                }
            }

            foreach (BoardSlot neighbor in connectedNeighbors)
            {
                remaining.Remove(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return remaining.Count == 0;
    }

    private int GetUnitSynergyBonus(int memberCount)
    {
        switch (memberCount)
        {
            case 2: return 2;
            case 3: return 5;
            case 4: return 9;
            case 5: return 14;
            case 6: return 20;
            case 7: return 27;
            default: return memberCount >= 8 ? 35 : 0;
        }
    }

    private bool IsStrictSubset(
        HashSet<CharacterData> candidate,
        HashSet<CharacterData> container)
    {
        if (candidate.Count >= container.Count) return false;
        foreach (CharacterData character in candidate)
            if (!container.Contains(character)) return false;
        return true;
    }

    private bool ResolveTemplate()
    {
        if (boardUnitList == null) boardUnitList = transform;
        if (boardUnitButtonTemplate == null && boardUnitList != null)
        {
            Transform template = FindChild(boardUnitList, "BoardUnitButton");
            boardUnitButtonTemplate = template != null
                ? template.GetComponent<Button>()
                : boardUnitList.GetComponentInChildren<Button>(true);
        }

        if (boardUnitList == null || boardUnitButtonTemplate == null)
        {
            Debug.LogWarning("Assign Board Unit List and Board Unit Button Template.", this);
            return false;
        }

        SetButtonVisible(boardUnitButtonTemplate, false);
        return true;
    }

    private bool HasStateChanged()
    {
        ShopManager shopManager = FindAnyObjectByType<ShopManager>();
        CharacterData[] roster = shopManager != null && shopManager.characters != null
            ? shopManager.characters
            : new CharacterData[0];
        if (roster.Length != previousRoster.Length) return true;
        for (int i = 0; i < roster.Length; i++)
        {
            if (roster[i] != previousRoster[i]) return true;
        }

        BoardManager boardManager = FindAnyObjectByType<BoardManager>();
        BoardSlot[] slots = boardManager != null && boardManager.boardArea != null
            ? boardManager.boardArea.GetComponentsInChildren<BoardSlot>(true)
            : new BoardSlot[0];
        if (slots.Length != previousSlots.Length) return true;
        for (int i = 0; i < slots.Length; i++)
        {
            OwnedCharacter character = slots[i] != null ? slots[i].placedCharacter : null;
            if (slots[i] != previousSlots[i] || character != previousCharacters[i]) return true;
        }
        return false;
    }

    private void CacheBoardState()
    {
        ShopManager shopManager = FindAnyObjectByType<ShopManager>();
        previousRoster = shopManager != null && shopManager.characters != null
            ? (CharacterData[])shopManager.characters.Clone()
            : new CharacterData[0];

        BoardManager boardManager = FindAnyObjectByType<BoardManager>();
        previousSlots = boardManager != null && boardManager.boardArea != null
            ? boardManager.boardArea.GetComponentsInChildren<BoardSlot>(true)
            : new BoardSlot[0];
        previousCharacters = new OwnedCharacter[previousSlots.Length];
        for (int i = 0; i < previousSlots.Length; i++)
        {
            previousCharacters[i] = previousSlots[i] != null ? previousSlots[i].placedCharacter : null;
        }
    }

    private TMP_Text FindTmpText(Button button, string childName, int fallbackIndex)
    {
        Transform child = FindChild(button.transform, childName);
        TMP_Text namedText = child != null ? child.GetComponent<TMP_Text>() : null;
        if (namedText != null) return namedText;
        TMP_Text[] texts = button.GetComponentsInChildren<TMP_Text>(true);
        return texts.Length > fallbackIndex ? texts[fallbackIndex] : null;
    }

    private Text FindLegacyText(Button button, string childName, int fallbackIndex)
    {
        Transform child = FindChild(button.transform, childName);
        Text namedText = child != null ? child.GetComponent<Text>() : null;
        if (namedText != null) return namedText;
        Text[] texts = button.GetComponentsInChildren<Text>(true);
        return texts.Length > fallbackIndex ? texts[fallbackIndex] : null;
    }

    private Transform FindChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform nested = FindChild(child, childName);
            if (nested != null) return nested;
        }
        return null;
    }

    private void SetText(TMP_Text tmpText, Text legacyText, string value)
    {
        if (tmpText != null) tmpText.text = value;
        if (legacyText != null) legacyText.text = value;
    }

    private void SetButtonVisible(Button button, bool visible, bool dimmed = false)
    {
        if (button == null) return;
        CanvasGroup group = button.GetComponent<CanvasGroup>();
        if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
        group.alpha = visible ? (dimmed ? 0.8f : 1f) : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;

        LayoutElement layout = button.GetComponent<LayoutElement>();
        if (layout == null) layout = button.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = !visible;
    }
}
