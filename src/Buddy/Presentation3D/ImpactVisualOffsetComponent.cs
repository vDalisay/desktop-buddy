using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Presentation-only victim jitter for a home-run freeze. It never writes a
/// physics transform; the visual presenter samples it on the render clock.
/// </summary>
[GlobalClass]
public partial class ImpactVisualOffsetComponent : Node
{
    private const float MaximumAmplitudePixels = 2.0f;
    private const float ShakeFrequencyHz = 40.0f;

    [Export] public SwingHitLagComponent HitLag { get; set; } = null!;

    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(HitLag) || !HitLag.IsInitialized)
        {
            throw new InvalidOperationException(
                "ImpactVisualOffsetComponent requires an initialized swing hit-lag component.");
        }

        IsInitialized = true;
    }

    public Vector3 OffsetFor(BuddyPartId partId)
    {
        if (!IsInitialized || !HitLag.IsActive ||
            HitLag.Current.StruckPart is not BuddyPart struck ||
            (int)struck != (int)partId ||
            HitLag.TotalTicks <= 0)
        {
            return Vector3.Zero;
        }

        float remaining = HitLag.RemainingTicks / (float)HitLag.TotalTicks;
        double elapsedSeconds = (Time.GetTicksUsec() - HitLag.StartedUsec) / 1_000_000.0;
        float wave = Mathf.Cos((float)(elapsedSeconds * Mathf.Tau * ShakeFrequencyHz));
        return new Vector3(MaximumAmplitudePixels * remaining * wave, 0.0f, 0.0f);
    }
}
