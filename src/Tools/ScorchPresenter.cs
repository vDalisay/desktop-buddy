using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Buddy;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Paints the scorch marks a burnt buddy carries (owner feedback 2026-08-01). A deliberately
/// thin driver: <see cref="Domain.Damage.ScorchState"/> owns the accumulate/hold/fade rules,
/// <see cref="FireSprayerComponent"/> carries the per-part state on the routed tick, and this
/// only reads the resulting number and hands it to the two places a part's skin colour is
/// already decided.
///
/// <para>It writes through the <b>existing</b> per-part channels rather than adding one:
/// <see cref="BuddyVisualPresenter.SetPartScorch"/> tints the part's own lit material — which
/// the material library already gives every mesh its own instance of, precisely so a per-part
/// mutation cannot bleed — and <see cref="PuppetPartBody.SetScorch"/> darkens the legacy
/// circle's drawn fill. Neither touches the pose pipeline, the outline shell, the face plate,
/// or anything physics reads.</para>
///
/// <para>Both modes are written every tick even though only one is on screen. That costs a
/// handful of comparisons — both setters are no-ops when nothing changed — and it means
/// toggling presentation mid-burn shows a buddy scorched exactly as much as the one that was
/// on screen a frame earlier, rather than a clean one that catches up.</para>
/// </summary>
[GlobalClass]
public partial class ScorchPresenter : Node
{
    [Export] public FireSprayerComponent Sprayer { get; set; } = null!;
    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public BuddyVisualPresenter VisualPresenter { get; set; } = null!;

    public bool IsInitialized { get; private set; }

    /// <summary>Parts carrying any visible mark right now — the scenario's oracle.</summary>
    public int MarkedPartCount { get; private set; }

    /// <summary>The darkest tint applied on the last routed tick.</summary>
    public float PeakDarkness { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Sprayer) || !Sprayer.IsInitialized ||
            !GodotObject.IsInstanceValid(Rig) ||
            !GodotObject.IsInstanceValid(VisualPresenter))
        {
            throw new InvalidOperationException(
                "ScorchPresenter requires an initialized sprayer, the puppet rig, and the visual presenter.");
        }

        IsInitialized = true;
    }

    /// <summary>Applies this tick's marks. Called only from the owning root's routed tick.</summary>
    public void PhysicsTick()
    {
        if (!IsInitialized)
            return;

        Color scorch = Sprayer.Profile.ScorchColor;
        int marked = 0;
        float peak = 0.0f;
        System.Collections.Generic.IReadOnlyList<PuppetPartBody> parts = Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            PuppetPartBody part = parts[index];
            BuddyPartId id = part.PartId;
            float darkness = Sprayer.ScorchOf(id);
            part.SetScorch(darkness, scorch);
            VisualPresenter.SetPartScorch(id, darkness, scorch);
            if (darkness > 0.0f)
                marked++;
            peak = Mathf.Max(peak, darkness);
        }

        MarkedPartCount = marked;
        PeakDarkness = peak;
    }
}
