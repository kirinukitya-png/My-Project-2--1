using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EnemyDetailPanel : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text attackText;
    public TMP_Text defenseText;
    public TMP_Text healthText;
    public TMP_Text targetText;

    public void ShowEnemy(EnemyData enemy)
    {
        if (enemy == null)
        {
            Debug.LogError("Cannot display a null EnemyData.", this);
            return;
        }

        ItemDetailPanel.HideOtherPanels(this);

        if (iconImage != null)
            iconImage.sprite = enemy.icon;

        if (nameText != null)
            nameText.text = enemy.enemyName;

        if (attackText != null)
            attackText.text = "ATK " + enemy.attack;

        if (defenseText != null)
            defenseText.text = "DEF " + enemy.defense;

        if (healthText != null)
            healthText.text = "HP " + enemy.health;

        UpdateTargetText(enemy.target);
        gameObject.SetActive(true);
    }

    private void UpdateTargetText(string targetValue)
    {
        if (targetText == null)
        {
            TMP_Text[] textComponents = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text textComponent in textComponents)
            {
                if (textComponent.gameObject.name == "TargetText")
                {
                    targetText = textComponent;
                    break;
                }
            }
        }

        if (targetText == null)
        {
            Debug.LogError("TargetText was not found in EnemyDetailPanel.", this);
            return;
        }

        List<string> targetIds = GetTargetIds(targetValue);
        List<string> descriptions = new List<string>();
        foreach (string targetId in targetIds)
        {
            TargetData targetData = Resources.Load<TargetData>("TargetData/" + targetId);
            if (targetData == null)
            {
                Debug.LogError(
                    "TargetData was not found for TargetID " + targetId + ".",
                    this);
                descriptions.Add(targetId);
                continue;
            }

            descriptions.Add(targetData.targetText);
        }

        string descriptionText = string.Join("\n", descriptions.ToArray());
        targetText.text = targetIds.Count > 1
            ? "以下の行動をターン毎に繰り返す。\n" + descriptionText
            : descriptionText;
    }

    private List<string> GetTargetIds(string targetValue)
    {
        List<string> targetIds = new List<string>();
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            targetIds.Add("T001");
            return targetIds;
        }

        string[] values = targetValue.Split(
            new[] { ',', ';', '|', '/', '、', ' ', '\t', '\r', '\n' },
            System.StringSplitOptions.RemoveEmptyEntries);
        foreach (string value in values)
        {
            string targetId = value.Trim();
            if (!string.IsNullOrEmpty(targetId))
                targetIds.Add(targetId);
        }

        if (targetIds.Count == 0)
            targetIds.Add("T001");
        return targetIds;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
