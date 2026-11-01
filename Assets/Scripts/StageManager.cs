using System;
using UnityEngine;
using UnityEngine.UI;

public class StageManager : MonoBehaviour
{
    public enum StageSelectionButton
    {
        SelectA,
        SelectB,
        SelectC,
        SelectD
    }

    [Serializable]
    public class StageSelectionSettings
    {
        public bool showSelectionPanel = true;
        public StageSelectionButton firstButton = StageSelectionButton.SelectA;
        public StageSelectionButton secondButton = StageSelectionButton.SelectB;
    }

    [Header("Stage Selection UI")]
    public GameObject popoutSelectStagePanel;
    public Button selectAButton;
    public Button selectBButton;
    public Button selectCButton;
    public Button selectDButton;

    [Tooltip("要素0〜24が1-1〜5-5です。パネルを表示する場合は異なるボタンを2つ選びます。")]
    public StageSelectionSettings[] stageSelectionSettings =
    {
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings(),
        new StageSelectionSettings()
    };

    private const int TotalStages = 25;
    private int currentStageIndex;
    private bool awaitingStageSelection;

    public event Action<StageSelectionButton> SelectionCompleted;
    public event Action StageStarting;
    public event Action<StageSelectionButton> StageChoiceSelected;
    public bool IsAwaitingStageSelection => awaitingStageSelection;
    public int CurrentStageNumber => currentStageIndex / 5 + 1;
    public int CurrentRoundNumber => currentStageIndex % 5 + 1;

    private void Start()
    {
        if (selectAButton != null)
            selectAButton.onClick.AddListener(SelectStageA);
        if (selectBButton != null)
            selectBButton.onClick.AddListener(SelectStageB);
        if (selectCButton != null)
            selectCButton.onClick.AddListener(SelectStageC);
        if (selectDButton != null)
            selectDButton.onClick.AddListener(SelectStageD);
    }

    private void OnDestroy()
    {
        if (selectAButton != null)
            selectAButton.onClick.RemoveListener(SelectStageA);
        if (selectBButton != null)
            selectBButton.onClick.RemoveListener(SelectStageB);
        if (selectCButton != null)
            selectCButton.onClick.RemoveListener(SelectStageC);
        if (selectDButton != null)
            selectDButton.onClick.RemoveListener(SelectStageD);
    }

    public void BeginStageSelection(int stageIndex)
    {
        currentStageIndex = Mathf.Clamp(stageIndex, 0, TotalStages - 1);
        StageStarting?.Invoke();
        StageSelectionSettings settings = GetCurrentSettings();
        SetSelectionButtonsVisible(false, settings);

        if (settings == null)
        {
            Debug.LogError("ステージ選択設定がありません。InspectorでStageManagerの設定を確認してください。", this);
            awaitingStageSelection = true;
            if (popoutSelectStagePanel != null)
                popoutSelectStagePanel.SetActive(true);
            return;
        }

        if (!settings.showSelectionPanel)
        {
            awaitingStageSelection = false;
            if (popoutSelectStagePanel != null)
                popoutSelectStagePanel.SetActive(false);
            SelectionCompleted?.Invoke(StageSelectionButton.SelectA);
            return;
        }

        if (settings.firstButton == settings.secondButton)
        {
            Debug.LogError("ステージ選択では異なるボタンを2つ選択してください。", this);
            awaitingStageSelection = true;
            if (popoutSelectStagePanel != null)
                popoutSelectStagePanel.SetActive(true);
            return;
        }

        if (GetButton(settings.firstButton) == null ||
            GetButton(settings.secondButton) == null)
        {
            Debug.LogError("StageManagerのステージ選択ボタン参照を確認してください。", this);
            awaitingStageSelection = true;
            if (popoutSelectStagePanel != null)
                popoutSelectStagePanel.SetActive(true);
            return;
        }

        SetSelectionButtonsVisible(true, settings);
        awaitingStageSelection = true;
        if (popoutSelectStagePanel != null)
            popoutSelectStagePanel.SetActive(true);
    }

    private StageSelectionSettings GetCurrentSettings()
    {
        if (stageSelectionSettings == null ||
            currentStageIndex >= stageSelectionSettings.Length)
            return null;

        return stageSelectionSettings[currentStageIndex];
    }

    private Button GetButton(StageSelectionButton button)
    {
        switch (button)
        {
            case StageSelectionButton.SelectA: return selectAButton;
            case StageSelectionButton.SelectB: return selectBButton;
            case StageSelectionButton.SelectC: return selectCButton;
            case StageSelectionButton.SelectD: return selectDButton;
            default: return null;
        }
    }

    private void SetSelectionButtonsVisible(bool showSelectedButtons, StageSelectionSettings settings)
    {
        SetButtonActive(
            StageSelectionButton.SelectA,
            showSelectedButtons && settings != null &&
            (settings.firstButton == StageSelectionButton.SelectA ||
             settings.secondButton == StageSelectionButton.SelectA));
        SetButtonActive(
            StageSelectionButton.SelectB,
            showSelectedButtons && settings != null &&
            (settings.firstButton == StageSelectionButton.SelectB ||
             settings.secondButton == StageSelectionButton.SelectB));
        SetButtonActive(
            StageSelectionButton.SelectC,
            showSelectedButtons && settings != null &&
            (settings.firstButton == StageSelectionButton.SelectC ||
             settings.secondButton == StageSelectionButton.SelectC));
        SetButtonActive(
            StageSelectionButton.SelectD,
            showSelectedButtons && settings != null &&
            (settings.firstButton == StageSelectionButton.SelectD ||
             settings.secondButton == StageSelectionButton.SelectD));
    }

    private void SetButtonActive(StageSelectionButton button, bool active)
    {
        Button stageButton = GetButton(button);
        if (stageButton != null)
            stageButton.gameObject.SetActive(active);
    }

    public void SelectStageA()
    {
        CompleteStageSelection(StageSelectionButton.SelectA);
    }

    public void SelectStageB()
    {
        CompleteStageSelection(StageSelectionButton.SelectB);
    }

    public void SelectStageC()
    {
        CompleteStageSelection(StageSelectionButton.SelectC);
    }

    public void SelectStageD()
    {
        CompleteStageSelection(StageSelectionButton.SelectD);
    }

    private void CompleteStageSelection(StageSelectionButton selectedButton)
    {
        if (!awaitingStageSelection)
            return;

        StageSelectionSettings settings = GetCurrentSettings();
        if (settings == null ||
            (settings.firstButton != selectedButton &&
             settings.secondButton != selectedButton))
            return;

        awaitingStageSelection = false;
        if (popoutSelectStagePanel != null)
            popoutSelectStagePanel.SetActive(false);

        StageChoiceSelected?.Invoke(selectedButton);
        SelectionCompleted?.Invoke(selectedButton);
    }
}
