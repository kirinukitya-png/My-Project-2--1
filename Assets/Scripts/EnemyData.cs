using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemy", menuName = "Game/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("Enemy")]
    public string enemyId;
    public string enemyName;
    public Sprite icon;

    [Header("Stats")]
    public int attack;
    public int defense;
    public int health;
    public string cip;
    public string ae;
    public string pig;
    public string target;

    [Header("Appears On Stages")]
    public int[] stages;

    [Header("Categories")]
    public string[] categories;
}
