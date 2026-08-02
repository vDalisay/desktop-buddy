using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Grab;

/// <summary>
/// What makes Power Grab different from Normal Grab (M5 §1.2): four multipliers over the
/// authored <see cref="GrabTetherProfile"/> plus its own release cap. There is deliberately
/// no stretch field here — the limit is always built from the shared tether profile, so the
/// two variants cannot drift apart on maximum reach.
///
/// <para>These five numbers are the owner feel gate's only knobs. Two of them are coupled:
/// the tether is a PD spring, so its damping ratio is <c>c / (2√(k·m))</c>. Scaling stiffness
/// without scaling damping by <b>√</b>(that factor) makes Power <i>less</i> damped than
/// Normal — more overshoot, the opposite of "controllable". Move them together by that rule.
/// <see cref="MaximumForceMultiplier"/> is the knob a player actually feels: at 220 stiffness
/// a 50 px error already demands ~11 000 units against a 6 000 clamp, so on any real drag the
/// tether is force-clamped and stiffness is inert.</para>
/// </summary>
[GlobalClass]
public partial class PowerGrabProfile : GameResource
{
    /// <summary>How much harder the tether pulls toward the cursor. Secondary knob.</summary>
    [Export(PropertyHint.Range, "1,10,0.1")] public float StiffnessMultiplier { get; set; } = 2.5f;

    /// <summary>√(<see cref="StiffnessMultiplier"/>), which holds the damping ratio constant.</summary>
    [Export(PropertyHint.Range, "0.5,10,0.1")] public float DampingMultiplier { get; set; } = 1.58f;

    /// <summary>The force clamp, and so the strength the player actually feels. Tune first.</summary>
    [Export(PropertyHint.Range, "1,10,0.1")] public float MaximumForceMultiplier { get; set; } = 3.0f;

    /// <summary>Applied to a deliberate throw's velocity before the cap. Never to a cancel.</summary>
    [Export(PropertyHint.Range, "1,5,0.05")] public float ReleaseVelocityMultiplier { get; set; } = 1.6f;

    /// <summary>
    /// The Power throw's own safe ceiling, replacing <see cref="GrabTetherProfile.ThrowSpeedCap"/>
    /// on an intentional release only.
    ///
    /// <para><b>Do not raise this above 1900.</b> Room walls are 16 px thick and the tick is
    /// 120 Hz, so 1920 px/s clears a full wall in one step and buddy parts run with CCD
    /// disabled. The 1300 default is 10.8 px/tick — 68% of a wall — and already crosses the
    /// 480×360 room in 0.37 s against 0.53 s at the Normal 900 cap. Going faster stops
    /// reading as a throw and starts reading as a teleport; going past 1900 tunnels.</para>
    /// </summary>
    [Export(PropertyHint.Range, "0.1,100000,1,or_greater")] public float ReleaseSpeedCap { get; set; } = 1_300.0f;

    /// <summary>The tunnelling speed for a 16 px wall at the 120 Hz fixed step.</summary>
    public const float TunnellingSpeedCap = 1_900.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        RequireMultiplier(errors, StiffnessMultiplier, nameof(StiffnessMultiplier));
        RequireMultiplier(errors, DampingMultiplier, nameof(DampingMultiplier));
        RequireMultiplier(errors, MaximumForceMultiplier, nameof(MaximumForceMultiplier));
        RequireMultiplier(errors, ReleaseVelocityMultiplier, nameof(ReleaseVelocityMultiplier));

        if (!float.IsFinite(ReleaseSpeedCap) || ReleaseSpeedCap <= 0.0f)
        {
            errors.Add($"{nameof(ReleaseSpeedCap)} must be finite and positive");
        }
        else if (ReleaseSpeedCap > TunnellingSpeedCap)
        {
            errors.Add(
                $"{nameof(ReleaseSpeedCap)} must not exceed {TunnellingSpeedCap} " +
                "px/s: a grabbed part has CCD disabled and would tunnel the room walls");
        }

        return errors;
    }

    private static void RequireMultiplier(
        Godot.Collections.Array<string> errors,
        float value,
        string name)
    {
        // A multiplier below 1 would make Power weaker than Normal, which is not a tuning
        // choice — it is a data mistake that would ship as a mysteriously bad tool.
        if (!float.IsFinite(value) || value < 1.0f)
        {
            errors.Add($"{name} must be finite and at least 1");
        }
    }
}
