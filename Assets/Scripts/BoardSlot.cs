using UnityEngine;

public class BoardSlot : MonoBehaviour
{
    public OwnedCharacter placedCharacter;

    public GameObject boardCharacterPrefab;

    public PlayerData playerData;
    public EnemyData enemyData;
    public bool acceptsPlayerCharacters = true;

    [Header("Event Item")]
    [Tooltip("イベント(〇-〇.5)でEnemyAreaに置かれるアイテム。Handに置けず売却もできず、キャラクターに使用する。")]
    public ItemData eventItemData;

    public bool HasEventItem => eventItemData != null;

    public bool IsEmpty()
    {
        return placedCharacter == null ||
               placedCharacter.data == null;
    }

    public void SetPlayerData(
        PlayerData data)
    {
        playerData = data;
    }

    public void SetEnemyData(
        EnemyData data,
        Sprite enemyFrameSprite = null,
        EnemyDetailPanel enemyDetailPanel = null,
        int gap = 0,
        int attackBonus = 0,
        int defenseBonus = 0,
        int healthBonus = 0)
    {
        enemyData = data;
        eventItemData = null;
        ClearCharacterVisualOnly();
        placedCharacter = null;

        if (enemyData == null)
        {
            BoardManager.RefreshVisibleDetailPanels();
            return;
        }

        if (boardCharacterPrefab == null)
        {
            Debug.LogError("BoardSlot requires a BoardCharacter prefab to display EnemyData.", this);
            return;
        }

        GameObject enemyObject = Instantiate(boardCharacterPrefab, transform);
        enemyObject.transform.localPosition = Vector3.zero;

        BoardCharacter boardCharacter = enemyObject.GetComponent<BoardCharacter>();
        if (boardCharacter == null)
        {
            Debug.LogError("BoardCharacter prefab must have a BoardCharacter component.", enemyObject);
            Destroy(enemyObject);
            return;
        }

        boardCharacter.SetEnemyCharacter(enemyData, enemyFrameSprite, enemyDetailPanel, gap, attackBonus, defenseBonus, healthBonus);
        boardCharacter.SetBoardSlot(this);
        BoardManager.RefreshVisibleDetailPanels();
    }

    public void SetEventItem(ItemData item)
    {
        ClearCharacterVisualOnly();
        enemyData = null;
        placedCharacter = null;
        eventItemData = item;
        if (item == null)
        {
            BoardManager.RefreshVisibleDetailPanels();
            return;
        }

        if (boardCharacterPrefab == null)
        {
            Debug.LogError("BoardSlot requires a BoardCharacter prefab to display an event item.", this);
            return;
        }

        GameObject itemObject = Instantiate(boardCharacterPrefab, transform);
        itemObject.transform.localPosition = Vector3.zero;
        BoardCharacter boardCharacter = itemObject.GetComponent<BoardCharacter>();
        if (boardCharacter == null)
        {
            Debug.LogError("BoardCharacter prefab must have a BoardCharacter component.", itemObject);
            Destroy(itemObject);
            return;
        }

        boardCharacter.SetItem(item);
        boardCharacter.SetBoardSlot(this);
        BoardManager.RefreshVisibleDetailPanels();
    }

    public void ClearEventItem()
    {
        if (eventItemData == null)
            return;

        eventItemData = null;
        ClearCharacterVisualOnly();
        BoardManager.RefreshVisibleDetailPanels();
    }

    public void SetAcceptsPlayerCharacters(bool accepts)
    {
        acceptsPlayerCharacters = accepts;
    }

    public void PlaceCharacter(
        OwnedCharacter character)
    {
        ClearCharacterVisualOnly();
        placedCharacter = character;

        CreateCharacterVisual(
            character
        );
        BoardManager.RefreshVisibleDetailPanels();
    }

    public void SetCharacterWithoutDataChange(
        OwnedCharacter character)
    {
        ClearCharacterVisualOnly();

        placedCharacter = character;

        CreateCharacterVisual(
            character
        );
        BoardManager.RefreshVisibleDetailPanels();
    }

    public void ClearCharacterVisualOnly()
    {
        BoardCharacter[] boardCharacters =
            GetComponentsInChildren<BoardCharacter>(true);

        foreach (BoardCharacter boardCharacter in boardCharacters)
        {
            if (boardCharacter != null)
                Destroy(boardCharacter.gameObject);
        }
    }

    public void ClearCharacterVisual()
    {
        ClearCharacterVisualOnly();
        placedCharacter = null;
        BoardManager.RefreshVisibleDetailPanels();
    }

    void CreateCharacterVisual(
        OwnedCharacter character)
    {
        GameObject characterObject =
            Instantiate(
                boardCharacterPrefab,
                transform
            );

        characterObject.transform.localPosition =
            Vector3.zero;

        BoardCharacter boardCharacter =
            characterObject.GetComponent<BoardCharacter>();

        boardCharacter.SetCharacter(
            character
        );

        boardCharacter.SetBoardSlot(
            this
        );
    }

    public void ClickSlot()
    {
        // Kept for existing Unity Button bindings; board transfers use drag-and-drop.
    }
}