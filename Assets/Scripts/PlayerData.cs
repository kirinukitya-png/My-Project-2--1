using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PlayerData : MonoBehaviour
{
    [Header("�����R�C��")]
    public int gold = 10;
    public TMP_Text goldText;

    [Header("HP")]
    public int hp;
    public TMP_Text hpCount;

    [Header("�����L�����N�^�[")]
    public List<OwnedCharacter> ownedCharacters =
        new List<OwnedCharacter>();

    [Header("�莝��")]
    public HandManager handManager;

    private void Start()
    {
        UpdateHPUI();
    }

    public bool CanBuy(int cost)
    {
        return gold >= cost;
    }

    public bool SellCharacter(
    OwnedCharacter character)
    {
        if (character == null)
        {
            return false;
        }

        if (!ownedCharacters.Contains(character))
        {
            Debug.LogError(
                "���p�Ώۂ̃L�����N�^�[���������X�g�ɑ��݂��܂���I"
            );

            return false;
        }

        // �����_�ł͍w�����i�����̂܂ܔ��p�z�Ƃ���
        int sellPrice =
            character.data.cost;

        int returnedCopies = 1;
        if (character.star == 2)
        {
            returnedCopies = 3;
        }
        else if (character.star >= 3)
        {
            returnedCopies = 9;
        }

        ShopManager shopManager =
            FindAnyObjectByType<ShopManager>();

        if (shopManager != null)
        {
            shopManager.ReturnCharacterToPool(
                character.data,
                returnedCopies
            );
        }

        gold += sellPrice;

        ownedCharacters.Remove(
            character
        );

        UpdateGoldUI();

        Debug.Log(
            character.data.characterName +
            " �� " +
            sellPrice +
            "G �Ŕ��p���܂����I"
        );

        return true;
    }

    public void UpdateGoldUI()
    {
        if (goldText != null)
        {
            goldText.text = "GOLD: " + gold;
        }
    }

    public void UpdateHPUI()
    {
        if (hpCount != null)
            hpCount.text = hp.ToString();
    }
}