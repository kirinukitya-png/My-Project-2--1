using UnityEngine;
[CreateAssetMenu(fileName = "NewItemData", menuName = "Game/Item Data")]
public class ItemData : ScriptableObject
{
    public string itemId;
    public string itemName;
    [TextArea] public string description;
    public Sprite icon;
    [Min(0)] public int purchasePrice;
    public string[] effects;
    public int[] effectValues;
    public string target;
    public string duration;
}