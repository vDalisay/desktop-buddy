using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Damage;
using Godot;

namespace DesktopBuddy.Interaction;

/// <summary>
/// Empirical tuning for the contact→pain→money pipeline (RAGDOLL §7.1–7.4): the
/// piecewise-linear impulse→pain anchors consumed by the Domain
/// <see cref="PainCurve"/>, the shared minimum-impulse episode threshold, and
/// <c>cash-per-pain</c>. Logic holds no literals; final values come from the
/// laboratory. The first pain anchor is the curve floor — impulses at or below it
/// score zero pain, which is what keeps ordinary autonomous jump landings and
/// walking scuffs from paying money (ROADMAP M3 exit criterion).
/// </summary>
[GlobalClass]
public partial class PainConversionProfile : GameResource
{
    [Export] public float[] ImpulseAnchors { get; set; } = { 350.0f, 700.0f, 1500.0f, 3000.0f };
    [Export] public float[] PainAnchors { get; set; } = { 0.0f, 20.0f, 55.0f, 100.0f };
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float MinimumImpulse { get; set; } = 350.0f;
    [Export(PropertyHint.Range, "0,1000,0.001,or_greater")] public double CashPerPain { get; set; } = 1.0;

    /// <summary>Builds the immutable Domain curve from the anchor data (init-time only).</summary>
    public PainCurve BuildCurve()
    {
        var anchors = new List<PainAnchor>(ImpulseAnchors.Length);
        for (int index = 0; index < ImpulseAnchors.Length; index++)
        {
            anchors.Add(new PainAnchor(ImpulseAnchors[index], PainAnchors[index]));
        }

        return new PainCurve(anchors);
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (ImpulseAnchors is null || PainAnchors is null ||
            ImpulseAnchors.Length < 2 || ImpulseAnchors.Length != PainAnchors.Length)
        {
            errors.Add("ImpulseAnchors and PainAnchors must be equal-length arrays of at least two anchors");
            return errors;
        }

        for (int index = 0; index < ImpulseAnchors.Length; index++)
        {
            if (!float.IsFinite(ImpulseAnchors[index]) || !float.IsFinite(PainAnchors[index]))
            {
                errors.Add($"anchor {index} must be finite");
            }

            if (PainAnchors[index] < 0.0f)
            {
                errors.Add($"pain anchor {index} must be non-negative");
            }

            if (index > 0 && ImpulseAnchors[index] <= ImpulseAnchors[index - 1])
            {
                errors.Add("impulse anchors must be strictly increasing");
            }

            if (index > 0 && PainAnchors[index] < PainAnchors[index - 1])
            {
                errors.Add("pain anchors must be non-decreasing");
            }
        }

        if (!float.IsFinite(MinimumImpulse) || MinimumImpulse < 0.0f)
        {
            errors.Add($"{nameof(MinimumImpulse)} must be finite and non-negative");
        }
        else if (PainAnchors[0] <= 0.0f && MinimumImpulse < ImpulseAnchors[0])
        {
            errors.Add($"{nameof(MinimumImpulse)} must not be below the zero-pain curve floor");
        }

        if (!double.IsFinite(CashPerPain) || CashPerPain < 0.0)
        {
            errors.Add($"{nameof(CashPerPain)} must be finite and non-negative");
        }

        return errors;
    }
}
