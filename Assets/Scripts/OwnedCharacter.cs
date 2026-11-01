using UnityEngine;

[System.Serializable]
public class OwnedCharacter
{
    public CharacterData data;

    [Range(1, 3)]
    public int star = 1;
    public int attack;
    public int defense;
    [System.NonSerialized] public int battleAttackBonus;
    [System.NonSerialized] public int battleDefenseBonus;
    public bool hasRolledStats;

    public OwnedCharacter(
        CharacterData characterData,
        CharacterStatSettings statSettings)
    {
        data = characterData;
        star = 1;
        TryRollStats(statSettings, star);
    }

    public int GetAttack(CharacterStatSettings statSettings)
    {
        return EnsureStats(statSettings) ? attack : 0;
    }

    public int GetDefense(CharacterStatSettings statSettings)
    {
        return EnsureStats(statSettings) ? defense : 0;
    }

    public bool TrySetStar(int newStar, CharacterStatSettings statSettings)
    {
        if (newStar < 1 || newStar > 3)
        {
            Debug.LogError("Character star must be between 1 and 3.");
            return false;
        }

        if (!TryRollStats(statSettings, newStar))
            return false;

        star = newStar;
        return true;
    }

    public bool EnsureStats(CharacterStatSettings statSettings)
    {
        if (hasRolledStats)
            return true;

        return TryRollStats(statSettings, star);
    }

    private bool TryRollStats(CharacterStatSettings statSettings, int statStar)
    {
        if (data == null)
        {
            Debug.LogError("Cannot roll stats for an OwnedCharacter without CharacterData.");
            return false;
        }

        if (statSettings == null)
        {
            Debug.LogError("CharacterStatSettings is required to roll character stats.", data);
            return false;
        }

        if (!statSettings.TryGetRandomStats(data.cost, statStar, out int rolledAttack, out int rolledDefense))
        {
            Debug.LogError(
                "No valid stat options for " + data.characterName +
                " (Cost " + data.cost + ", Star " + statStar + ").",
                data);
            return false;
        }

        attack = rolledAttack;
        defense = rolledDefense;
        hasRolledStats = true;
        return true;
    }
}
