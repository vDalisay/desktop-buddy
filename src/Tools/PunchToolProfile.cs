using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Tuning for a cursor tool that winds back and lashes out: hold secondary and the tool drifts
/// behind the cursor like a drawn slingshot, let go and it is flung forward past it (owner
/// instruction 2026-08-22, the Boxing Glove). A <see cref="CursorToolProfile"/> that authors
/// none of this is simply not a punching tool, which is how every other tool keeps its exact
/// behaviour without anything branching on a tool name — the same seam
/// <see cref="SwingToolProfile"/> uses for the bat.
///
/// <para>Nothing here touches damage. Charge moves where the tool is <em>told</em> to be; the
/// tether drags it there, and whatever the solver reports on contact goes through the same pain
/// curve every other impact does. A charged punch hurts more because the glove is genuinely
/// travelling faster, not because a number was multiplied.</para>
/// </summary>
[GlobalClass]
public partial class PunchToolProfile : GameResource
{
    /// <summary>Ticks to a full wind-up. One second at the fixed 120 Hz.</summary>
    [Export(PropertyHint.Range, "1,600,1")] public int MaxChargeTicks { get; set; } = 120;

    /// <summary>How far behind the cursor a fully charged tool sits, in pixels.</summary>
    [Export(PropertyHint.Range, "1,400,0.5")] public float PullBackPx { get; set; } = 30.0f;

    /// <summary>How far past the cursor a fully charged release throws it, in pixels.</summary>
    [Export(PropertyHint.Range, "1,600,0.5")] public float LungePx { get; set; } = 270.0f;

    /// <summary>
    /// How far the wind-up rattles sideways at full charge, in pixels. The shake rides on the
    /// anchor like everything else here, so a strained hold shows as the glove fighting the
    /// tether rather than as a separate visual effect (owner instruction 2026-08-23).
    /// </summary>
    [Export(PropertyHint.Range, "0,40,0.5")] public float ChargeShakePx { get; set; } = 6.0f;

    /// <summary>
    /// Ticks the lunge takes to reach out and come back. The offset follows one half sine over
    /// this window, so the tool leaves and returns without a second timer to keep in step.
    /// </summary>
    [Export(PropertyHint.Range, "2,120,1")] public int LungeTicks { get; set; } = 14;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (MaxChargeTicks <= 0)
            errors.Add($"{nameof(MaxChargeTicks)} must be positive");
        if (LungeTicks <= 1)
            errors.Add($"{nameof(LungeTicks)} must be at least two ticks");
        if (!float.IsFinite(PullBackPx) || PullBackPx <= 0.0f)
            errors.Add($"{nameof(PullBackPx)} must be finite and positive");
        if (!float.IsFinite(LungePx) || LungePx <= 0.0f)
            errors.Add($"{nameof(LungePx)} must be finite and positive");
        if (!float.IsFinite(ChargeShakePx) || ChargeShakePx < 0.0f)
            errors.Add($"{nameof(ChargeShakePx)} must be finite and non-negative");

        // A punch that reaches no further than the wind-up pulled back would read as the tool
        // returning to the cursor rather than striking past it.
        if (float.IsFinite(LungePx) && float.IsFinite(PullBackPx) && LungePx <= PullBackPx)
            errors.Add($"{nameof(LungePx)} must exceed {nameof(PullBackPx)}");

        return errors;
    }
}
