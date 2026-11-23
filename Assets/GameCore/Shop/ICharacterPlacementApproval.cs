using ProjectNST.GameCore.Characters;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>
    /// Read-only gate supplied by the caller. It must approve that the pending purchase
    /// can be accepted by the actual board/hand rules; this interface creates no slots or policy.
    /// </summary>
    public interface ICharacterPlacementApproval
    {
        bool CanAcceptPurchasedCharacter(CharacterInstance character);
    }
}
