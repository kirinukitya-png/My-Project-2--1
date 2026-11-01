using UnityEngine;
using UnityEngine.EventSystems;

public class HandSlot : MonoBehaviour, IPointerClickHandler
{
    public OwnedCharacter ownedCharacter;
    public ItemData itemData;

    [Header("Character Display")]
    public GameObject boardCharacterPrefab;

    private BoardCharacter boardCharacter;

    public bool IsEmpty()
    {
        return (ownedCharacter == null || ownedCharacter.data == null) && itemData == null;
    }

    public void SetCharacter(OwnedCharacter character)
    {
        itemData = null;
        if (character == null || character.data == null)
        {
            ownedCharacter = null;
            ClearBoardCharacter();
            return;
        }

        ownedCharacter = character;
        CreateBoardCharacter();
    }

    public void SetItem(ItemData item)
    {
        ownedCharacter = null;
        itemData = item;
        CreateBoardCharacter();
    }

    public OwnedCharacter RemoveCharacter()
    {
        OwnedCharacter character = ownedCharacter;
        ownedCharacter = null;
        ClearBoardCharacter();
        return character;
    }

    public ItemData RemoveItem()
    {
        ItemData item = itemData;
        itemData = null;
        ClearBoardCharacter();
        return item;
    }

    public void ClearContents()
    {
        ownedCharacter = null;
        itemData = null;
        ClearBoardCharacter();
    }

    private void CreateBoardCharacter()
    {
        ClearBoardCharacter();
        if (boardCharacterPrefab == null)
        {
            Debug.LogError("HandSlot requires a BoardCharacter Prefab.", this);
            return;
        }

        GameObject displayObject = Instantiate(boardCharacterPrefab, transform, false);
        displayObject.transform.localPosition = Vector3.zero;
        boardCharacter = displayObject.GetComponent<BoardCharacter>();
        if (boardCharacter == null)
        {
            Debug.LogError("BoardCharacter Prefab must have a BoardCharacter component.", displayObject);
            return;
        }

        boardCharacter.SetHandSlot(this);
        if (ownedCharacter != null)
            boardCharacter.SetCharacter(ownedCharacter);
        else if (itemData != null)
            boardCharacter.SetItem(itemData);
    }

    private void ClearBoardCharacter()
    {
        if (boardCharacter != null)
        {
            Destroy(boardCharacter.gameObject);
            boardCharacter = null;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            ClickSlot();
    }

    public void ClickSlot()
    {
        BoardManager boardManager = FindAnyObjectByType<BoardManager>();
        if (boardManager == null)
        {
            Debug.LogError("BoardManagerが見つかりません。", this);
            return;
        }

        if (boardManager.selectionType == BoardManager.SelectionType.Board)
        {
            if (boardManager.selectedBoardSlot != null)
                boardManager.ReturnSelectedBoardCharacterToHand(this);
            return;
        }

        if (boardManager.selectionType == BoardManager.SelectionType.Shop)
        {
            if (!IsEmpty())
            {
                Debug.Log("この手持ちスロットは使用中です。");
                return;
            }

            if (boardManager.selectedShopItem != null)
                boardManager.BuySelectedShopItemToHand(this);
            else
                boardManager.BuySelectedShopCharacterToHand(this);
            return;
        }

        if (boardManager.selectionType == BoardManager.SelectionType.Hand)
        {
            if (boardManager.selectedHandSlot == this)
            {
                boardManager.ClearSelection();
                return;
            }

            boardManager.MoveHandCharacter(boardManager.selectedHandSlot, this);
            return;
        }

        if (!IsEmpty())
        {
            if (itemData != null)
                boardManager.SelectHandItem(this);
            else
                boardManager.SelectHandCharacter(ownedCharacter, this);
        }
    }
}