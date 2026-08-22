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

    /// <summary>
    /// How far the buddy squirms under the feather, and how fast. 17 Hz read as a buzzing
    /// vibration rather than a squirm, so the rate is down by ~90% and the throw with it
    /// (owner instruction 2026-08-22).
    /// </summary>
    private const float TickleAmplitudePixels = 0.55f;
    private const float TickleFrequencyHz = 1.8f;

    [Export] public SwingHitLagComponent HitLag { get; set; } = null!;

    /// <summary>
    /// The care stroke whose tickle contact makes him squirm (owner instruction 2026-08-21).
    /// Wired by the sandbox root rather than exported, so no scene has to be re-saved and a
    /// composition without care tools simply leaves it null.
    /// </summary>
    public Tools.CareStrokeComponent? Care { get; set; }

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
        // Only the part the feather is actually on squirms: shaking the whole buddy made a
        // touch on one foot look like a full-body seizure (owner instruction 2026-08-22). Two
        // frequencies, because a single sine reads as a vibration rather than a squirm. A
        // hit-lag freeze outranks it: that shake is the one being read.
        Vector3 tickle = Vector3.Zero;
        if (IsInitialized && Care is { IsInitialized: true, IsTickleContact: true } &&
            Care.ContactPart is BuddyPart tickled && (int)tickled == (int)partId)
        {
            double seconds = Time.GetTicksUsec() / 1_000_000.0;
            float lateral = Mathf.Sin((float)(seconds * Mathf.Tau * TickleFrequencyHz));
            float vertical = Mathf.Sin((float)(seconds * Mathf.Tau * TickleFrequencyHz * 0.62f));
            tickle = new Vector3(
                TickleAmplitudePixels * lateral,
                TickleAmplitudePixels * 0.45f * vertical,
                0.0f);
        }

        if (!IsInitialized || !HitLag.IsActive ||
            HitLag.Current.StruckPart is not BuddyPart struck ||
            (int)struck != (int)partId ||
            HitLag.TotalTicks <= 0)
        {
            return tickle;
        }

        float remaining = HitLag.RemainingTicks / (float)HitLag.TotalTicks;
        double elapsedSeconds = (Time.GetTicksUsec() - HitLag.StartedUsec) / 1_000_000.0;
        float wave = Mathf.Cos((float)(elapsedSeconds * Mathf.Tau * ShakeFrequencyHz));
        return new Vector3(MaximumAmplitudePixels * remaining * wave, 0.0f, 0.0f);
    }
}
