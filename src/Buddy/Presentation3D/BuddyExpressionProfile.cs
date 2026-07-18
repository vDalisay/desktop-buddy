using DesktopBuddy.App;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Typed M3.6 expressive-presentation tuning (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md).
/// Task 1 fields: the tracking-to-performance blend time, the post-impact cooldown that
/// forces Tracking after an accepted hit, and the per-part offset cap as a fraction of the
/// part radius (plan prime invariant 2 caps it at 0.5). Later M3.6 tasks extend this same
/// resource with facing, activity, look-at, and face tuning. Validation delegates into the
/// pure <see cref="ExpressionTuningData"/> so the numeric contract lives in dotnet tests.
/// </summary>
[GlobalClass]
public partial class BuddyExpressionProfile : GameResource
{
    [Export(PropertyHint.Range, "0.01,2,0.01")]
    public float PerformanceBlendSeconds { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "0,1200,1")]
    public int PostImpactCooldownTicks { get; set; } = 60;

    [Export(PropertyHint.Range, "0.01,0.5,0.01")]
    public float OffsetCapRadiusFraction { get; set; } = 0.5f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        foreach (string error in ToData().Validate())
        {
            errors.Add(error);
        }

        return errors;
    }

    /// <summary>Projects the exported Godot fields into the pure-logic validation image.</summary>
    public ExpressionTuningData ToData() => new(
        PerformanceBlendSeconds,
        PostImpactCooldownTicks,
        OffsetCapRadiusFraction);
}
