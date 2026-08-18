using System;
using Godot;

namespace DesktopBuddy.Tools;

public partial class SwingToolProfile
{
    /// <summary>
    /// Optional whole-buddy shove delivered after an accepted charged-swing contact. This is
    /// physical knockback only: the shared solver impulse remains the sole input to pain/economy.
    /// Zero preserves the historical behavior for every swing profile that does not opt in.
    /// </summary>
    [Export(PropertyHint.Range, "0,20000,1,or_greater")]
    public float FullChargeBuddyShoveImpulse { get; set; }

    /// <summary>
    /// Shapes how strongly the extra shove is reserved for the top of the charge curve. Values
    /// above one keep partial swings ordinary while allowing a full five-second charge to read as
    /// a deliberate home run without raising the already-stable authored tip speed.
    /// </summary>
    [Export(PropertyHint.Range, "1,6,0.1")]
    public float BuddyShoveChargeExponent { get; set; } = 3.0f;

    public float BuddyShoveForCharge(float charge)
    {
        if (!float.IsFinite(FullChargeBuddyShoveImpulse) || FullChargeBuddyShoveImpulse <= 0.0f ||
            !float.IsFinite(BuddyShoveChargeExponent) || BuddyShoveChargeExponent <= 0.0f ||
            !float.IsFinite(charge) || charge <= 0.0f)
        {
            return 0.0f;
        }

        float normalized = Mathf.Clamp(charge, 0.0f, 1.0f);
        return FullChargeBuddyShoveImpulse * MathF.Pow(normalized, BuddyShoveChargeExponent);
    }
}
