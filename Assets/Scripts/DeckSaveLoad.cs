using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DeckSaveLoad : MonoBehaviour
{
    private const int DeckSize = 30;
    private const string SaveKey = "CompositionScene.SavedDeck.v1";

    [Header("編成")]
    public RosterBoardDisplay rosterBoardDisplay;
    public CharacterFullRosterListDisplay rosterList;

    [Header("ボタン")]
    public Button saveButton;
    public Button loadButton;
    public TMP_Text statusText;

    [Serializable]
    private class SavedDeck
    {
        public List<SavedSlot> slots = new List<SavedSlot>();
    }

    [Serializable]
    private class SavedSlot
    {
        public int slotIndex;
        public string characterAssetName;
    }

    private void Start()
    {
        if (rosterBoardDisplay == null)
            rosterBoardDisplay = FindAnyObjectByType<RosterBoardDisplay>();
        if (rosterList == null)
            rosterList = FindAnyObjectByType<CharacterFullRosterListDisplay>();

        if (saveButton != null)
            saveButton.onClick.AddListener(SaveDeck);
        if (loadButton != null)
            loadButton.onClick.AddListener(LoadDeck);
    }

    private void OnDestroy()
    {
        if (saveButton != null)
            saveButton.onClick.RemoveListener(SaveDeck);
        if (loadButton != null)
            loadButton.onClick.RemoveListener(LoadDeck);
    }

    public void SaveDeck()
    {
        if (rosterBoardDisplay == null || rosterBoardDisplay.slotContainer == null)
        {
            ShowStatus("編成盤面が見つかりません。");
            return;
        }

        List<RosterBoardSlot> boardSlots = GetBoardSlots();
        if (boardSlots.Count != DeckSize)
        {
            ShowStatus("編成枠が30枠ではありません。");
            return;
        }

        SavedDeck deck = new SavedDeck();
        HashSet<CharacterData> uniqueCharacters = new HashSet<CharacterData>();
        HashSet<string> uniqueNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (RosterBoardSlot slot in boardSlots)
        {
            CharacterData character = slot.characterData;
            if (character != null &&
                (!uniqueCharacters.Add(character) ||
                 !uniqueNames.Add(character.name)))
            {
                ShowStatus("同じキャラクターを複数の枠に保存できません。");
                return;
            }

            deck.slots.Add(new SavedSlot
            {
                slotIndex = slot.slotIndex,
                characterAssetName = character != null ? character.name : string.Empty
            });
        }

        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(deck));
        PlayerPrefs.Save();
        ShowStatus("デッキを保存しました。");
    }

    public void LoadDeck()
    {
        if (!PlayerPrefs.HasKey(SaveKey))
        {
            ShowStatus("保存されたデッキがありません。");
            return;
        }

        if (rosterBoardDisplay == null || rosterList == null)
        {
            ShowStatus("編成盤面または全キャラクター名簿が見つかりません。");
            return;
        }

        SavedDeck deck;
        try
        {
            deck = JsonUtility.FromJson<SavedDeck>(PlayerPrefs.GetString(SaveKey));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("保存データを読み込めませんでした: " + exception.Message, this);
            ShowStatus("保存データを読み込めませんでした。");
            return;
        }

        List<RosterBoardSlot> boardSlots = GetBoardSlots();
        if (deck == null || deck.slots == null ||
            deck.slots.Count != DeckSize || boardSlots.Count != DeckSize)
        {
            ShowStatus("保存データまたは編成枠が30体分ではありません。");
            return;
        }

        Dictionary<string, CharacterData> rosterByAssetName =
            new Dictionary<string, CharacterData>(StringComparer.Ordinal);
        HashSet<string> ambiguousNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (CharacterData character in rosterList.GetFullRoster())
        {
            if (character == null)
                continue;

            if (rosterByAssetName.ContainsKey(character.name))
            {
                rosterByAssetName.Remove(character.name);
                ambiguousNames.Add(character.name);
            }
            else if (!ambiguousNames.Contains(character.name))
            {
                rosterByAssetName.Add(character.name, character);
            }
        }

        CharacterData[] charactersBySlot = new CharacterData[DeckSize];
        HashSet<int> usedSlotIndices = new HashSet<int>();
        HashSet<CharacterData> usedCharacters = new HashSet<CharacterData>();
        foreach (SavedSlot savedSlot in deck.slots)
        {
            if (savedSlot == null ||
                savedSlot.slotIndex < 0 ||
                savedSlot.slotIndex >= DeckSize ||
                !usedSlotIndices.Add(savedSlot.slotIndex))
            {
                ShowStatus("保存データに無効または重複したスロットがあります。");
                return;
            }

            if (string.IsNullOrEmpty(savedSlot.characterAssetName))
                continue;

            if (ambiguousNames.Contains(savedSlot.characterAssetName) ||
                !rosterByAssetName.TryGetValue(savedSlot.characterAssetName, out CharacterData character) ||
                !usedCharacters.Add(character))
            {
                ShowStatus("保存データに無効または重複したキャラクターがあります。");
                return;
            }

            charactersBySlot[savedSlot.slotIndex] = character;
        }

        if (!rosterBoardDisplay.ApplySavedDeck(charactersBySlot))
        {
            ShowStatus("保存デッキを編成盤面に適用できませんでした。");
            return;
        }

        ShowStatus("デッキをロードしました。");
    }

    private List<RosterBoardSlot> GetBoardSlots()
    {
        List<RosterBoardSlot> slots = new List<RosterBoardSlot>();
        if (rosterBoardDisplay == null || rosterBoardDisplay.slotContainer == null)
            return slots;

        slots.AddRange(rosterBoardDisplay.slotContainer.GetComponentsInChildren<RosterBoardSlot>(true));
        slots.Sort((a, b) => a.slotIndex.CompareTo(b.slotIndex));
        return slots;
    }

    private void ShowStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.gameObject.SetActive(true);
        }
        else
        {
            Debug.Log(message, this);
        }
    }
}