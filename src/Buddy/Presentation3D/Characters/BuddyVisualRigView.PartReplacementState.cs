using DesktopBuddy.Buddy.Physics;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    /// <summary>
    /// Re-applies only active replacement visibility. This protects replacement visuals when a
    /// paint underlay is refreshed after appearance application, without touching layer visibility
    /// for any non-replaced part.
    /// </summary>
    public void EnforceActivePartReplacementVisibility()
    {
        if (_torsoVisualReplaced) SetPartReplacementState(BuddyPartId.Torso, true);
        if (_leftFootVisualReplaced) SetPartReplacementState(BuddyPartId.LeftFoot, true);
        if (_rightFootVisualReplaced) SetPartReplacementState(BuddyPartId.RightFoot, true);
    }
}
