using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Gun profile for a deliberately faster discrete projectile. It keeps the base gun's measured
/// no-CCD contact/impulse path, but replaces only the conservative 24 px/tick authoring guard with
/// a geometric swept-overlap guard that includes the projectile's own diameter. This is reusable
/// for future fast rounds; it does not change projectile physics or damage calculation.
/// </summary>
[GlobalClass]
public partial class FastProjectileGunProfile : GunProfile
{
    // Smallest buddy part is 30 px diameter in the trusted rig. Keeping three pixels of overlap
    // margin makes a 4 px-radius bullet safe through 35 px/tick: 30 + 8 - 3 = 35.
    public const float MinimumTargetDiameterPx = 30.0f;
    public const float RequiredOverlapMarginPx = 3.0f;

    public float MaximumFastTravelPerTickPx =>
        MinimumTargetDiameterPx + (ProjectileRadius * 2.0f) - RequiredOverlapMarginPx;

    public override Godot.Collections.Array<string> Validate()
    {
        Godot.Collections.Array<string> errors = base.Validate();

        // The base profile intentionally rejects anything above its conservative 24 px/tick lane.
        // Replace that one authoring error with the fast-profile geometric guard; every other gun
        // validation rule is inherited unchanged.
        string speedPrefix = $"{nameof(MuzzleSpeed)} must not exceed ";
        for (int index = errors.Count - 1; index >= 0; index--)
        {
            if (errors[index].StartsWith(speedPrefix, System.StringComparison.Ordinal))
                errors.RemoveAt(index);
        }

        float ticksPerSecond = Engine.PhysicsTicksPerSecond;
        float travelPerTick = ticksPerSecond > 0.0f ? MuzzleSpeed / ticksPerSecond : float.PositiveInfinity;
        if (!float.IsFinite(MaximumFastTravelPerTickPx) || MaximumFastTravelPerTickPx <= 0.0f ||
            !float.IsFinite(travelPerTick) || travelPerTick > MaximumFastTravelPerTickPx)
        {
            errors.Add(
                $"{nameof(MuzzleSpeed)} travels {travelPerTick:F2} px/tick but this fast projectile " +
                $"may travel at most {MaximumFastTravelPerTickPx:F2} px/tick for radius " +
                $"{ProjectileRadius:F1} while preserving discrete overlap margin");
        }

        return errors;
    }
}
