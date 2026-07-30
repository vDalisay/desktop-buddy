using DesktopBuddy.App;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Provisional, laboratory-tunable tuning for a cursor tool that can be gripped,
/// charged, and swung — the Home-Run Bat handling described in
/// `docs/M5_TASK4_HOME_RUN_BAT_FEEL_PLAN.md` §4.9. A
/// <see cref="CursorToolProfile"/> that authors none of this is simply not a
/// charged-swing tool, which is how the Boxing Glove keeps its exact behavior
/// without anything branching on a tool name.
///
/// Charge changes how fast the bat really swings; it never multiplies pain or
/// payout. Everything downstream still measures the solver impulse through the
/// shared curve.
///
/// Note what is absent by design:
/// <list type="bullet">
/// <item>There is no sweep duration. Sweep ticks are <b>derived</b> from
/// <see cref="TipSpeedUncharged"/>/<see cref="TipSpeedFull"/> — authoring a
/// duration beside a speed over-determines the arc, and nothing would catch the
/// two disagreeing.</item>
/// <item>There is no grip-point fraction. The handle derives from the collider
/// as <c>(0, +Length/2 - Radius)</c>, so a grip point can never contradict the
/// shape it grips.</item>
/// </list>
/// </summary>
[GlobalClass]
public partial class SwingToolProfile : GameResource
{
    /// <summary>
    /// Owner-confirmed five seconds at the fixed 120 Hz tick. This is product
    /// behavior, not tuning: validation pins the exact value.
    /// </summary>
    public const int ConfirmedMaxChargeTicks = 600;

    [Export(PropertyHint.Range, "1,2000,1")] public int MaxChargeTicks { get; set; } = ConfirmedMaxChargeTicks;

    /// <summary>How far behind vertical the bat leans while charging — the telegraph.</summary>
    [Export(PropertyHint.Range, "0,89,0.1")] public float LeanDegrees { get; set; } = 35.0f;

    [Export(PropertyHint.Range, "1,120,1")] public int WindupTicks { get; set; } = 14;

    /// <summary>
    /// Barrel angle behind vertical at the top of the release snap, where the
    /// strike sweep begins. Must clear <see cref="LeanDegrees"/>, or the snap
    /// would run the arc backwards instead of pulling farther behind.
    /// </summary>
    [Export(PropertyHint.Range, "1,179,0.1")] public float WindupDegrees { get; set; } = 70.0f;

    /// <summary>The constant-rate plateau. Excludes the follow-through tail.</summary>
    [Export(PropertyHint.Range, "10,359,0.1")] public float SweepDegrees { get; set; } = 245.0f;

    [Export(PropertyHint.Range, "0,180,0.1")] public float FollowThroughDegrees { get; set; } = 25.0f;
    [Export(PropertyHint.Range, "1,120,1")] public int FollowThroughTicks { get; set; } = 10;

    /// <summary>
    /// Barrel-tip speed about the handle pivot. Not comparable to a cursor drag
    /// speed: the free swing translates the bat's centre with no pivot, so the
    /// two numbers measure different things and only tip-vs-tip comparisons mean
    /// anything.
    /// </summary>
    [Export(PropertyHint.Range, "1,20000,1,or_greater")] public float TipSpeedUncharged { get; set; } = 1800.0f;

    [Export(PropertyHint.Range, "1,20000,1,or_greater")] public float TipSpeedFull { get; set; } = 5500.0f;

    /// <summary>
    /// Validation bounds on the <b>derived</b> sweep, so an absurd authored tip
    /// speed cannot produce a swing too short for any contact to be observed in.
    /// </summary>
    [Export(PropertyHint.Range, "1,600,1")] public int MinimumSweepTicks { get; set; } = 5;

    [Export(PropertyHint.Range, "1,600,1")] public int MaximumSweepTicks { get; set; } = 60;

    [Export(PropertyHint.Range, "1,600,1")] public int RecoveryTicks { get; set; } = 42;

    /// <summary>
    /// Tether authority while swinging only. Holding the handle still while the
    /// bat rotates about it is a centripetal load of <c>m·ω²·rCom</c>, which at
    /// full charge is roughly eight times the ordinary follow tether's cap — a
    /// cap sized for following lets the "pivot" be dragged across the room while
    /// the tip never reaches its commanded speed.
    /// </summary>
    [Export(PropertyHint.Range, "1,10000000,1,or_greater")] public float SwingAnchorForceCap { get; set; } = 1_400_000.0f;

    /// <summary>
    /// How fast the free swing's physical anchor may chase the raw cursor. This
    /// bounds high-DPI and teleporting input so a flicked mouse cannot
    /// manufacture a home-run-grade impulse. The default equals today's
    /// benchmark swing speed by construction, so only <i>faster</i> input is
    /// bounded and existing behavior is unchanged.
    /// </summary>
    [Export(PropertyHint.Range, "1,20000,1,or_greater")] public float FreeSwingAnchorSpeedCap { get; set; } = 2400.0f;

    /// <summary>Tether authority while following. Defaults to the profile's ordinary maximum.</summary>
    [Export(PropertyHint.Range, "1,1000000,1,or_greater")] public float FreeSwingForceCap { get; set; } = 120_000.0f;

    /// <summary>Covers the documented one-tick contact-report delay.</summary>
    [Export(PropertyHint.Range, "0,30,1")] public int ContactObservationGraceTicks { get; set; } = 2;

    [Export(PropertyHint.Range, "0,64,0.1")] public float ShakeMaxAmplitudePx { get; set; } = 3.5f;
    [Export(PropertyHint.Range, "0.1,200,0.1")] public float ShakePrimaryHz { get; set; } = 33.0f;
    [Export(PropertyHint.Range, "0.1,200,0.1")] public float ShakeSecondaryHz { get; set; } = 41.0f;

    [Export(PropertyHint.Range, "0.01,4,0.01")] public float GlintSeconds { get; set; } = 0.35f;

    /// <summary>
    /// Owner-confirmed staged charge read: a small tip glint at one second,
    /// medium at three, and the largest at the five-second cap.
    /// </summary>
    [Export(PropertyHint.Range, "1,256,0.1")] public float OneSecondGlintSizePx { get; set; } = 7.0f;
    [Export(PropertyHint.Range, "1,256,0.1")] public float ThreeSecondGlintSizePx { get; set; } = 12.0f;
    [Export(PropertyHint.Range, "1,256,0.1")] public float FiveSecondGlintSizePx { get; set; } = 18.0f;

    /// <summary>
    /// Horizontal cursor travel per tick that counts as "aiming that way".
    /// Below it the previous aim persists, so a bat held nearly still does not
    /// flicker between sides.
    /// </summary>
    [Export(PropertyHint.Range, "0,200,0.1")] public float DirectionTravelThreshold { get; set; } = 6.0f;

    /// <summary>The owner-confirmed black handle wrap.</summary>
    [Export] public Color GripColor { get; set; } = new("141414");

    [Export(PropertyHint.Range, "0,600,1")] public int HitLagMinTicks { get; set; } = 6;
    [Export(PropertyHint.Range, "0,600,1")] public int HitLagMaxTicks { get; set; } = 60;

    /// <summary>Placeholder swing/impact audio level, routed through the existing bus layout.</summary>
    [Export(PropertyHint.Range, "-60,6,0.1")] public float AudioVolumeDb { get; set; } = -6.0f;

    [Export(PropertyHint.Range, "0,10000000,1,or_greater")] public float GripStiffness { get; set; } = 900_000.0f;
    [Export(PropertyHint.Range, "0,1000000,1,or_greater")] public float GripDamping { get; set; } = 120_000.0f;

    /// <summary>
    /// Dedicated linear pivot gains. The ordinary cursor tether is intentionally
    /// soft; reusing it during a 66 rad/s swing reaches the force cap only after
    /// the handle has already drifted far outside its laboratory tolerance.
    /// </summary>
    [Export(PropertyHint.Range, "0,1000000,1,or_greater")] public float SwingAnchorStiffness { get; set; } = 240_000.0f;
    [Export(PropertyHint.Range, "0,100000,1,or_greater")] public float SwingAnchorDamping { get; set; } = 1_000.0f;

    /// <summary>
    /// Dedicated angular trajectory gains. Grip settles to zero velocity;
    /// swinging tracks a discontinuous high-speed command, so sharing one
    /// damping value makes either the hold or the strike wrong.
    /// </summary>
    [Export(PropertyHint.Range, "0,10000000,1,or_greater")] public float SwingServoStiffness { get; set; } = 50_000.0f;
    [Export(PropertyHint.Range, "0,5000000,1,or_greater")] public float SwingServoDamping { get; set; } = 120_000.0f;
    [Export(PropertyHint.Range, "1,100000000,1,or_greater")] public float SwingTorqueCap { get; set; } = 70_000_000.0f;

    /// <summary>
    /// The engine-free constants the pure swing rules run on. Tick-rate comes
    /// from the engine rather than the data, so authored tick counts always mean
    /// what the project's fixed step says they mean.
    /// </summary>
    public ChargedSwingConstants ToConstants() => new(
        Engine.PhysicsTicksPerSecond,
        MaxChargeTicks,
        WindupTicks,
        FollowThroughTicks,
        RecoveryTicks,
        LeanDegrees,
        WindupDegrees,
        SweepDegrees,
        FollowThroughDegrees,
        TipSpeedUncharged,
        TipSpeedFull,
        MinimumSweepTicks,
        MaximumSweepTicks);

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();

        // The charge duration is a confirmed product decision, so a data edit
        // must fail loudly rather than quietly retune what the owner signed off.
        if (MaxChargeTicks != ConfirmedMaxChargeTicks)
        {
            errors.Add(
                $"{nameof(MaxChargeTicks)} must be the confirmed {ConfirmedMaxChargeTicks} " +
                $"(five seconds at 120 Hz), not {MaxChargeTicks}");
        }

        RequirePositive(errors, WindupTicks, nameof(WindupTicks));
        RequirePositive(errors, FollowThroughTicks, nameof(FollowThroughTicks));
        RequirePositive(errors, RecoveryTicks, nameof(RecoveryTicks));
        RequirePositive(errors, MinimumSweepTicks, nameof(MinimumSweepTicks));
        RequirePositive(errors, MaximumSweepTicks, nameof(MaximumSweepTicks));

        if (MaximumSweepTicks < MinimumSweepTicks)
        {
            errors.Add(
                $"{nameof(MaximumSweepTicks)} must not be below {nameof(MinimumSweepTicks)}");
        }

        if (ContactObservationGraceTicks < 0)
        {
            errors.Add($"{nameof(ContactObservationGraceTicks)} must be non-negative");
        }

        RequireFiniteNonNegative(errors, LeanDegrees, nameof(LeanDegrees));
        RequireFinitePositive(errors, WindupDegrees, nameof(WindupDegrees));
        RequireFinitePositive(errors, SweepDegrees, nameof(SweepDegrees));
        RequireFiniteNonNegative(errors, FollowThroughDegrees, nameof(FollowThroughDegrees));

        // A snap that does not clear the lean would pull the barrel forwards at
        // release, which reads as the bat stuttering rather than winding up.
        if (float.IsFinite(WindupDegrees) && float.IsFinite(LeanDegrees) &&
            WindupDegrees <= LeanDegrees)
        {
            errors.Add($"{nameof(WindupDegrees)} must exceed {nameof(LeanDegrees)}");
        }

        RequireFinitePositive(errors, TipSpeedUncharged, nameof(TipSpeedUncharged));
        RequireFinitePositive(errors, TipSpeedFull, nameof(TipSpeedFull));
        if (float.IsFinite(TipSpeedFull) && float.IsFinite(TipSpeedUncharged) &&
            TipSpeedFull <= TipSpeedUncharged)
        {
            errors.Add(
                $"{nameof(TipSpeedFull)} must exceed {nameof(TipSpeedUncharged)} — charge has to " +
                "make the bat genuinely swing faster, since nothing multiplies the outcome");
        }

        RequireFinitePositive(errors, SwingAnchorForceCap, nameof(SwingAnchorForceCap));
        RequireFinitePositive(errors, FreeSwingAnchorSpeedCap, nameof(FreeSwingAnchorSpeedCap));
        RequireFinitePositive(errors, FreeSwingForceCap, nameof(FreeSwingForceCap));
        RequireFinitePositive(errors, SwingTorqueCap, nameof(SwingTorqueCap));
        RequireFiniteNonNegative(errors, GripStiffness, nameof(GripStiffness));
        RequireFiniteNonNegative(errors, GripDamping, nameof(GripDamping));
        RequireFiniteNonNegative(errors, SwingAnchorStiffness, nameof(SwingAnchorStiffness));
        RequireFiniteNonNegative(errors, SwingAnchorDamping, nameof(SwingAnchorDamping));
        RequireFiniteNonNegative(errors, SwingServoStiffness, nameof(SwingServoStiffness));
        RequireFiniteNonNegative(errors, SwingServoDamping, nameof(SwingServoDamping));
        RequireFiniteNonNegative(errors, ShakeMaxAmplitudePx, nameof(ShakeMaxAmplitudePx));
        RequireFinitePositive(errors, ShakePrimaryHz, nameof(ShakePrimaryHz));
        RequireFinitePositive(errors, ShakeSecondaryHz, nameof(ShakeSecondaryHz));
        RequireFinitePositive(errors, GlintSeconds, nameof(GlintSeconds));
        RequireFinitePositive(errors, OneSecondGlintSizePx, nameof(OneSecondGlintSizePx));
        RequireFinitePositive(errors, ThreeSecondGlintSizePx, nameof(ThreeSecondGlintSizePx));
        RequireFinitePositive(errors, FiveSecondGlintSizePx, nameof(FiveSecondGlintSizePx));
        if (float.IsFinite(OneSecondGlintSizePx) &&
            float.IsFinite(ThreeSecondGlintSizePx) &&
            float.IsFinite(FiveSecondGlintSizePx) &&
            (OneSecondGlintSizePx >= ThreeSecondGlintSizePx ||
             ThreeSecondGlintSizePx >= FiveSecondGlintSizePx))
        {
            errors.Add(
                "charge glint sizes must be strictly ordered one-second < three-second < five-second");
        }
        RequireFiniteNonNegative(errors, DirectionTravelThreshold, nameof(DirectionTravelThreshold));

        // A single frequency reads as a mechanical oscillation and visibly loops;
        // the wobble only stops looking like strain when the two are unequal.
        if (float.IsFinite(ShakePrimaryHz) && float.IsFinite(ShakeSecondaryHz) &&
            Mathf.IsEqualApprox(ShakePrimaryHz, ShakeSecondaryHz))
        {
            errors.Add(
                $"{nameof(ShakePrimaryHz)} and {nameof(ShakeSecondaryHz)} must differ");
        }

        if (HitLagMinTicks < 0)
        {
            errors.Add($"{nameof(HitLagMinTicks)} must be non-negative");
        }

        if (HitLagMaxTicks < HitLagMinTicks)
        {
            errors.Add($"{nameof(HitLagMaxTicks)} must not be below {nameof(HitLagMinTicks)}");
        }

        if (!float.IsFinite(AudioVolumeDb))
        {
            errors.Add($"{nameof(AudioVolumeDb)} must be finite");
        }

        return errors;
    }

    /// <summary>
    /// The checks that need the collider this swing profile is attached to. The
    /// owning <see cref="CursorToolProfile"/> runs these, because the arc's
    /// feasibility is a fact about a shape and a mass, not about this data alone.
    /// </summary>
    public void ValidateAgainstCollider(
        Godot.Collections.Array<string> errors,
        CursorToolProfile owner)
    {
        // A round tool has no barrel to swing and no handle to grip.
        if (!owner.IsElongated)
        {
            errors.Add(
                $"{nameof(SwingToolProfile)} requires an elongated collider " +
                $"({nameof(owner.Length)} must exceed the collider's full width)");
            return;
        }

        if (Validate().Count > 0)
        {
            // Self-consistency failures are already reported; the derivations
            // below would only produce noise on top of them.
            return;
        }

        ChargedSwingConstants constants = ToConstants();
        float tipRadius = owner.HandleToTipRadius;
        SwingPlan slow = ChargedSwing.SwingPlanFor(0.0f, tipRadius, constants);
        SwingPlan fast = ChargedSwing.SwingPlanFor(1.0f, tipRadius, constants);
        if (!slow.IsValid || !fast.IsValid)
        {
            errors.Add(
                $"{nameof(SwingToolProfile)} does not derive a valid swing for this collider");
            return;
        }

        // The clamp exists so a bad tip speed cannot silently shorten the arc.
        // Landing on the clamp means the authored speed and the authored bounds
        // disagree, and the arc you get is not the arc you asked for.
        if (slow.SweepTicks <= MinimumSweepTicks || slow.SweepTicks >= MaximumSweepTicks ||
            fast.SweepTicks <= MinimumSweepTicks || fast.SweepTicks >= MaximumSweepTicks)
        {
            errors.Add(
                $"derived sweep ticks ({slow.SweepTicks} uncharged, {fast.SweepTicks} full) must " +
                $"fall strictly inside [{MinimumSweepTicks}, {MaximumSweepTicks}]");
        }

        // A tether that cannot hold the pivot turns the swing into a shove, and
        // every measured tip speed built on it would be reading the saturation.
        float required = SwingTrajectoryServo.PivotHoldForce(
            owner.Mass, fast.TargetAngularVelocity, owner.HandleToCenterOfMassRadius);
        if (SwingAnchorForceCap < required)
        {
            errors.Add(
                $"{nameof(SwingAnchorForceCap)} ({SwingAnchorForceCap:F0}) is below the " +
                $"{required:F0} needed to hold the handle pivot at full charge " +
                $"(m*w^2*r); raising the tip speed without raising this cap would " +
                "saturate the tether instead of swinging faster");
        }
    }

    private static void RequirePositive(
        Godot.Collections.Array<string> errors, int value, string name)
    {
        if (value <= 0)
        {
            errors.Add($"{name} must be positive");
        }
    }

    private static void RequireFinitePositive(
        Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            errors.Add($"{name} must be finite and positive");
        }
    }

    private static void RequireFiniteNonNegative(
        Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            errors.Add($"{name} must be finite and non-negative");
        }
    }
}
