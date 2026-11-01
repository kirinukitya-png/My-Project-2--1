using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class ShopCard :
    MonoBehaviour,
    IPointerClickHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [Header("Card Display")]
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text costText;
    public TMP_Text starText;

    [Header("Cost Sprites")]
    public Sprite cost1Sprite;
    public Sprite cost2Sprite;
    public Sprite cost3Sprite;
    public Sprite cost4Sprite;
    public Sprite cost5Sprite;

    private Image cardImage;
    private GameObject lightingPanel;

    private CharacterData characterData;

    private ShopSlot shopSlot;

    private float lightingRefreshTimer;

    private const float LightingRefreshInterval = 0.2f;

    private void Awake()
    {
        cardImage = GetComponent<Image>();
        Transform lightingTransform = transform.Find("Lighting");
        lightingPanel = lightingTransform != null
            ? lightingTransform.gameObject
            : null;
    }

    private void Update()
    {
        lightingRefreshTimer += Time.unscaledDeltaTime;
        if (lightingRefreshTimer < LightingRefreshInterval)
            return;

        lightingRefreshTimer = 0f;
        UpdateLighting();
    }

    public void SetCharacter(
        CharacterData data)
    {
        characterData = data;

        nameText.text =
            data.characterName;

        costText.text =
            data.cost.ToString();

        starText.text =
            "STAR 1";

        switch (data.cost)
        {
            case 1:
                cardImage.sprite = cost1Sprite;
                break;

            case 2:
                cardImage.sprite = cost2Sprite;
                break;

            case 3:
                cardImage.sprite = cost3Sprite;
                break;

            case 4:
                cardImage.sprite = cost4Sprite;
                break;

            case 5:
                cardImage.sprite = cost5Sprite;
                break;
        }

        if (data.icon != null)
        {
            iconImage.sprite =
                data.icon;
        }

        gameObject.SetActive(true);
        UpdateLighting();
    }

    public void SetShopSlot(
        ShopSlot slot)
    {
        shopSlot = slot;
    }

    public ShopSlot GetShopSlot()
    {
        return shopSlot;
    }

    public void ClearCharacter()
    {
        characterData = null;

        nameText.text = "";
        costText.text = "";
        starText.text = "";

        iconImage.sprite = null;
        if (lightingPanel != null)
            lightingPanel.SetActive(false);

        gameObject.SetActive(false);
    }

    private void UpdateLighting()
    {
        if (lightingPanel == null || characterData == null)
            return;

        bool hasMatchingCharacterInHandOrBoard =
            GetMatchingCharacterCountInHandAndBoard() >= 2;
        bool isInRosterBoard = IsCharacterInRosterBoard();
        lightingPanel.SetActive(hasMatchingCharacterInHandOrBoard || isInRosterBoard);
    }

    private int GetMatchingCharacterCountInHandAndBoard()
    {
        int count = 0;

        HandManager handManager = FindAnyObjectByType<HandManager>();
        if (handManager != null && handManager.handArea != null)
        {
            HandSlot[] handSlots =
                handManager.handArea.GetComponentsInChildren<HandSlot>(true);
            foreach (HandSlot slot in handSlots)
            {
                if (slot != null &&
                    !slot.IsEmpty() &&
                    slot.ownedCharacter != null &&
                    slot.ownedCharacter.data == characterData)
                {
                    count++;
                }
            }
        }

        BoardManager boardManager = FindAnyObjectByType<BoardManager>();
        if (boardManager != null && boardManager.boardArea != null)
        {
            BoardSlot[] boardSlots =
                boardManager.boardArea.GetComponentsInChildren<BoardSlot>(true);
            foreach (BoardSlot slot in boardSlots)
            {
                if (slot != null &&
                    !slot.IsEmpty() &&
                    slot.placedCharacter.data == characterData)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private bool IsCharacterInRosterBoard()
    {
        RosterBoardDisplay rosterBoard =
            FindAnyObjectByType<RosterBoardDisplay>(FindObjectsInactive.Include);
        if (rosterBoard == null)
            return false;

        Transform slotContainer = rosterBoard.slotContainer != null
            ? rosterBoard.slotContainer
            : rosterBoard.transform;
        RosterBoardSlot[] rosterSlots =
            slotContainer.GetComponentsInChildren<RosterBoardSlot>(true);

        foreach (RosterBoardSlot slot in rosterSlots)
        {
            if (slot != null && slot.characterData == characterData)
                return true;
        }

        return false;
    }

    public void OnPointerClick(
        PointerEventData eventData)
    {
        if (characterData == null)
        {
            return;
        }

        BoardManager boardManager =
            FindAnyObjectByType<BoardManager>();

        if (boardManager == null)
        {
            Debug.LogError(
                "BoardManager��������܂���I"
            );

            return;
        }

        // ���N���b�N
        if (eventData.button ==
            PointerEventData.InputButton.Left)
        {
            boardManager.SelectShopCharacter(
                characterData,
                this
            );

            return;
        }

        // �E�N���b�N
        // �� ���N���b�N�Ɠ����I������
        // �� �w���͂��Ȃ�
        if (eventData.button ==
            PointerEventData.InputButton.Right)
        {
            boardManager.ShowShopCharacterDetail(
               characterData,
               this
            );

            return;
        }
    }

    private bool isDragging = false;
    private UIDragCardPreview.Session dragPreviewSession;

    public void OnBeginDrag(
    PointerEventData eventData)
    {
        Debug.Log(
            "ShopCard OnBeginDrag"
        );

        if (characterData == null)
        {
            return;
        }

        isDragging = true;
        dragPreviewSession = UIDragCardPreview.Begin(gameObject, eventData);

        Debug.Log(
            characterData.characterName +
            " �̃h���b�O���J�n���܂����B"
        );
    }

    public void OnDrag(
        PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }
        UIDragCardPreview.Update(dragPreviewSession, eventData);
    }

    public void OnEndDrag(
        PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        UIDragCardPreview.End(ref dragPreviewSession);

        GameObject targetObject =
            eventData.pointerCurrentRaycast.gameObject;

        if (targetObject == null)
        {
            return;
        }

        if (UIDragCardPreview.IsRosterBoardTarget(targetObject))
            return;

        BoardManager boardManager =
            FindAnyObjectByType<BoardManager>();

        if (boardManager == null)
        {
            return;
        }

        // -----------------------------------------
        // �V���b�v �� �莝��
        // -----------------------------------------

        HandSlot targetHandSlot =
            targetObject.GetComponentInParent<HandSlot>();

        if (targetHandSlot != null)
        {
          

            boardManager.ClearSelection();

            boardManager.selectionType =
                BoardManager.SelectionType.Shop;

            boardManager.selectedShopCharacter =
                characterData;

            boardManager.selectedShopCard =
                this;

            boardManager.BuySelectedShopCharacterToHand(
                targetHandSlot
            );

            return;
        }

        // -----------------------------------------
        // �V���b�v �� �Ֆ�
        // -----------------------------------------

        BoardSlot targetBoardSlot =
            targetObject.GetComponentInParent<BoardSlot>();

        if (targetBoardSlot != null)
        {
           

            boardManager.ClearSelection();

            boardManager.selectionType =
                BoardManager.SelectionType.Shop;

            boardManager.selectedShopCharacter =
                characterData;

            boardManager.selectedShopCard =
                this;

            boardManager.BuySelectedShopCharacterToBoard(
                targetBoardSlot
            );

            return;
        }
    }
}
