using UnityEngine;

public class BoardSlot : MonoBehaviour
{
    public OwnedCharacter placedCharacter;

    public GameObject boardCharacterPrefab;

    public PlayerData playerData;
    public EnemyData enemyData;
    public bool acceptsPlayerCharacters = true;

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
        ClearCharacterVisualOnly();
        placedCharacter = null;

        if (enemyData == null)
            return;

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
    }

    public void SetCharacterWithoutDataChange(
        OwnedCharacter character)
    {
        ClearCharacterVisualOnly();

        placedCharacter = character;

        CreateCharacterVisual(
            character
        );
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
        if (!acceptsPlayerCharacters)
            return;

        BoardManager boardManager =
            FindAnyObjectByType<BoardManager>();

        if (boardManager == null)
        {
            Debug.LogError(
                "BoardManager��������܂���I"
            );

            return;
        }

        // �莝���L�����N�^�[��I��
        if (boardManager.selectionType ==
            BoardManager.SelectionType.Hand)
        {
            if (boardManager.selectedHandSlot != null && boardManager.selectedHandSlot.itemData != null)
                boardManager.UseHandItemOnBoard(boardManager.selectedHandSlot, this);
            else
                boardManager.PlaceSelectedHandCharacter(this);

            return;
        }

        // �ՖʃL�����N�^�[��I��
        if (boardManager.selectionType ==
            BoardManager.SelectionType.Board)
        {
            if (boardManager.selectedBoardSlot ==
                this)
            {
                boardManager.ClearSelection();

                Debug.Log(
                    "�I�����������܂����B"
                );

                return;
            }

            boardManager.MoveCharacter(
                boardManager.selectedBoardSlot,
                this
            );

            return;
        }

        // �V���b�v�L�����N�^�[��I��
        if (boardManager.selectionType ==
            BoardManager.SelectionType.Shop)
        {
            if (boardManager.selectedShopItem != null)
                boardManager.BuySelectedShopItemToFirstEmptyHandSlot();
            else
                boardManager.BuySelectedShopCharacterToBoard(this);

            return;
        }

        // �����I�����Ă��Ȃ�
        // �󂫃}�X�Ȃ牽�����Ȃ�
        // �L�����N�^�[������ꍇ����
        // BoardCharacter������I�������
    }
}