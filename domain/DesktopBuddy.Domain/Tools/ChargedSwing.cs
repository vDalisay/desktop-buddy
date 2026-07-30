using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Tools;

/// <summary>
/// Where a charged-swing cursor tool is in its grip/charge/swing cycle
/// (`docs/M5_TASK4_HOME_RUN_BAT_FEEL_PLAN.md` §4.1). A tool whose profile
/// authors no swing never leaves <see cref="Follow"/>.
/// </summary>
public enum ChargedSwingState
{
    /// <summary>Cursor-tethered free follow — the weak secondary attack.</summary>
    Follow,

    /// <summary>Grip held: the tool hangs from its handle and is held upright.</summary>
    Gripped,

    /// <summary>Grip and charge held: charge accrues and the pose leans back.</summary>
    Charging,

    /// <summary>Charge released: the scripted home-run arc is running.</summary>
    Swinging,

    /// <summary>Post-swing lockout while the upright servo settles.</summary>
    Recovery,
}

/// <summary>
/// What a contact made in the current state is allowed to be. This is an
/// admission rule, never a damage multiplier: an admitted impact still scores
/// from the measured solver impulse through the shared pain curve.
/// </summary>
public enum SwingImpactMode
{
    /// <summary>Repositioning, leaning, and settling contacts score nothing.</summary>
    None,

    /// <summary>The weak free swing — ordinary per-source episode deduplication.</summary>
    WeakFreeSwing,

    /// <summary>A charged home-run epoch — at most one scored buddy impact.</summary>
    HomeRun,
}

/// <summary>
/// The authored constants one charged-swing tool needs. Supplied by the Godot
/// <c>SwingToolProfile</c> resource; kept engine-free here so every rule below
/// is unit-testable. Durations are in routed physics ticks, angles in degrees,
/// speeds in pixels per second.
///
/// Note what is deliberately absent: there is no authored sweep duration. The
/// tick count of the strike sweep is *derived* from the tip speed
/// (<see cref="ChargedSwing.SwingPlanFor"/>), because authoring both
/// over-determines the arc — with <see cref="SweepDegrees"/> fixed, a duration
/// and a speed are the same number stated twice, and nothing in the type system
/// would catch them disagreeing.
/// </summary>
public readonly record struct ChargedSwingConstants(
    int TicksPerSecond,
    int MaxChargeTicks,
    int WindupTicks,
    int FollowThroughTicks,
    int RecoveryTicks,
    float LeanDegrees,
    float WindupDegrees,
    float SweepDegrees,
    float FollowThroughDegrees,
    float TipSpeedUncharged,
    float TipSpeedFull,
    int MinimumSweepTicks,
    int MaximumSweepTicks)
{
    /// <summary>
    /// True when every constant is finite, positive where it must be, and
    /// correctly ordered. The Godot profile validator reports the individual
    /// failures; the pure rules use this as a single guard so a malformed
    /// profile yields an inert result instead of a NaN-poisoned body.
    /// </summary>
    public bool IsWellFormed() =>
        TicksPerSecond > 0 &&
        MaxChargeTicks > 0 &&
        WindupTicks > 0 &&
        FollowThroughTicks > 0 &&
        RecoveryTicks > 0 &&
        MinimumSweepTicks > 0 &&
        MaximumSweepTicks >= MinimumSweepTicks &&
        ChargedSwing.IsFiniteNonNegative(LeanDegrees) &&
        ChargedSwing.IsFinitePositive(WindupDegrees) &&
        // The release snap pulls the barrel farther behind the charge lean; a
        // windup that does not clear the lean would run the arc backwards.
        WindupDegrees > LeanDegrees &&
        ChargedSwing.IsFinitePositive(SweepDegrees) &&
        ChargedSwing.IsFiniteNonNegative(FollowThroughDegrees) &&
        ChargedSwing.IsFinitePositive(TipSpeedUncharged) &&
        ChargedSwing.IsFinitePositive(TipSpeedFull) &&
        TipSpeedFull > TipSpeedUncharged;
}

/// <summary>
/// The carried state of one charged-swing tool. Immutable: the caller stores
/// the <see cref="ChargedSwingResult.Phase"/> it was handed and feeds it back
/// next tick, so nothing about the swing lives in mutable adapter fields where
/// attribution could read a stale charge (§7, "No mutable LastSwingCharge").
/// </summary>
public readonly record struct ChargedSwingPhase(
    ChargedSwingState State,
    int TicksInState,
    int ChargeTicks,
    int SwingEpoch,
    int DirectionSign,
    Vector2 Pivot,
    float ReleasedCharge,
    bool ChargeCapReached)
{
    /// <summary>A freshly spawned tool: following the cursor, aimed right.</summary>
    public static ChargedSwingPhase Initial { get; } = new(
        ChargedSwingState.Follow,
        TicksInState: 0,
        ChargeTicks: 0,
        SwingEpoch: 0,
        DirectionSign: 1,
        Pivot: Vector2.Zero,
        ReleasedCharge: 0.0f,
        ChargeCapReached: false);
}

/// <summary>External facts for one charged-swing tick.</summary>
public readonly record struct ChargedSwingInput(
    ChargedSwingPhase Phase,
    bool GripHeld,
    bool ChargeHeld,

    /// <summary>
    /// The aim sign resolved this tick by
    /// <see cref="ChargedSwing.SwingDirectionSign"/>. Tracked while the player
    /// can still change their mind; ignored once the swing has been committed.
    /// </summary>
    int DirectionSign,

    /// <summary>World position of the handle grip point, latched as the swing pivot on release.</summary>
    Vector2 HandlePoint,

    /// <summary>Handle-to-tip lever arm, derived from the collider, never authored.</summary>
    float TipRadius,
    ChargedSwingConstants Constants);

/// <summary>Allocation-free result for one charged-swing tick.</summary>
public readonly record struct ChargedSwingResult(
    ChargedSwingPhase Phase,

    /// <summary>Normalized charge, <c>0..1</c>, meaningful while charging.</summary>
    float Charge,

    /// <summary>One-shot edge at the charge cap — this is the tip glint.</summary>
    bool ChargeCompleted,

    /// <summary>One-shot edge on the release that committed a swing.</summary>
    bool SwingReleased,

    /// <summary>The charge the running swing was released with, <c>0..1</c>.</summary>
    float ReleasedCharge,

    /// <summary>Monotonic swing identity; <c>0</c> before the first release.</summary>
    int SwingEpoch,
    int DirectionSign,
    Vector2 Pivot,
    SwingImpactMode ImpactMode,
    bool IsValid);

/// <summary>
/// The pure grip/charge/swing state machine (§4.1). Grip is the safe bail-out:
/// releasing it at any point before the charge is let go returns to the weak
/// free swing with no arc and no scoring contact. Everything the presentation
/// and damage layers need arrives as an edge or an immutable latched value, so
/// no consumer has to poll adapter state a tick later and hope it is still true.
/// </summary>
public static class ChargedSwingMachine
{
    public static ChargedSwingResult Tick(in ChargedSwingInput input)
    {
        ChargedSwingConstants constants = input.Constants;
        if (!constants.IsWellFormed() ||
            !ChargedSwing.IsFinitePositive(input.TipRadius) ||
            !IsFinite(input.HandlePoint))
        {
            return Inert(input.Phase);
        }

        ChargedSwingPhase phase = input.Phase;

        // Aim tracks the cursor right up to the release and is frozen after it,
        // so a pointer flick during the arc cannot redirect a committed swing.
        int aim = phase.State is ChargedSwingState.Swinging or ChargedSwingState.Recovery
            ? phase.DirectionSign
            : ChargedSwing.NormalizeSign(input.DirectionSign, phase.DirectionSign);

        bool chargeCompleted = false;
        bool swingReleased = false;

        switch (phase.State)
        {
            case ChargedSwingState.Follow:
                phase = input.GripHeld
                    ? Enter(phase, ChargedSwingState.Gripped)
                    : Advance(phase);
                break;

            case ChargedSwingState.Gripped:
                if (!input.GripHeld)
                {
                    phase = Enter(phase, ChargedSwingState.Follow);
                }
                else if (input.ChargeHeld)
                {
                    phase = Enter(phase, ChargedSwingState.Charging) with
                    {
                        ChargeTicks = 0,
                        ChargeCapReached = false,
                    };
                }
                else
                {
                    phase = Advance(phase);
                }

                break;

            case ChargedSwingState.Charging:
                if (!input.GripHeld)
                {
                    // Cancel. No swing, no epoch, no scoring contact.
                    phase = Enter(phase, ChargedSwingState.Follow) with { ChargeTicks = 0 };
                }
                else if (!input.ChargeHeld)
                {
                    float released = ChargedSwing.ChargeProgress(
                        phase.ChargeTicks, constants.MaxChargeTicks);
                    swingReleased = true;
                    phase = Enter(phase, ChargedSwingState.Swinging) with
                    {
                        ReleasedCharge = released,
                        SwingEpoch = phase.SwingEpoch + 1,
                        Pivot = input.HandlePoint,
                        DirectionSign = aim,
                    };
                }
                else
                {
                    int charged = Math.Min(phase.ChargeTicks + 1, constants.MaxChargeTicks);
                    bool capReached = phase.ChargeCapReached;
                    if (!capReached && charged >= constants.MaxChargeTicks)
                    {
                        chargeCompleted = true;
                        capReached = true;
                    }

                    phase = Advance(phase) with
                    {
                        ChargeTicks = charged,
                        ChargeCapReached = capReached,
                    };
                }

                break;

            case ChargedSwingState.Swinging:
            {
                SwingPlan plan = ChargedSwing.SwingPlanFor(
                    phase.ReleasedCharge, input.TipRadius, constants);
                int total = plan.IsValid ? plan.TotalTicks : 1;
                phase = phase.TicksInState + 1 >= total
                    ? Enter(phase, ChargedSwingState.Recovery)
                    : Advance(phase);
                break;
            }

            case ChargedSwingState.Recovery:
                if (phase.TicksInState + 1 >= constants.RecoveryTicks)
                {
                    phase = Enter(
                        phase,
                        input.GripHeld ? ChargedSwingState.Gripped : ChargedSwingState.Follow);
                }
                else
                {
                    phase = Advance(phase);
                }

                break;

            default:
                return Inert(input.Phase);
        }

        if (phase.State is not (ChargedSwingState.Swinging or ChargedSwingState.Recovery))
        {
            phase = phase with { DirectionSign = aim };
        }

        return new ChargedSwingResult(
            phase,
            Charge: ChargedSwing.ChargeProgress(phase.ChargeTicks, constants.MaxChargeTicks),
            ChargeCompleted: chargeCompleted,
            SwingReleased: swingReleased,
            ReleasedCharge: phase.State == ChargedSwingState.Swinging ? phase.ReleasedCharge : 0.0f,
            SwingEpoch: phase.SwingEpoch,
            DirectionSign: phase.DirectionSign,
            Pivot: phase.Pivot,
            ImpactMode: ModeFor(phase.State),
            IsValid: true);
    }

    /// <summary>
    /// What a contact in <paramref name="state"/> may become. Grip, charge, and
    /// recovery move the collider for reasons the player did not aim, so they
    /// admit nothing at all.
    /// </summary>
    public static SwingImpactMode ModeFor(ChargedSwingState state) => state switch
    {
        ChargedSwingState.Follow => SwingImpactMode.WeakFreeSwing,
        ChargedSwingState.Swinging => SwingImpactMode.HomeRun,
        _ => SwingImpactMode.None,
    };

    private static ChargedSwingPhase Enter(ChargedSwingPhase phase, ChargedSwingState state) =>
        phase with { State = state, TicksInState = 0 };

    private static ChargedSwingPhase Advance(ChargedSwingPhase phase) =>
        phase.TicksInState >= int.MaxValue - 1
            ? phase
            : phase with { TicksInState = phase.TicksInState + 1 };

    private static ChargedSwingResult Inert(ChargedSwingPhase phase) => new(
        phase,
        Charge: 0.0f,
        ChargeCompleted: false,
        SwingReleased: false,
        ReleasedCharge: 0.0f,
        SwingEpoch: phase.SwingEpoch,
        DirectionSign: phase.DirectionSign,
        Pivot: phase.Pivot,
        ImpactMode: SwingImpactMode.None,
        IsValid: false);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}

/// <summary>
/// The choreography of one released swing (§4.6). Every duration here except
/// the windup and follow-through tails is derived from the authored tip speed,
/// so charge makes the bat genuinely swing faster rather than multiplying an
/// outcome after the fact.
/// </summary>
public readonly record struct SwingPlan(
    int WindupTicks,
    int SweepTicks,
    int FollowThroughTicks,
    float SweepDegrees,
    float FollowThroughDegrees,

    /// <summary>Authored intent for the barrel tip about the handle pivot, px/s.</summary>
    float TargetTipSpeed,

    /// <summary>
    /// <see cref="TargetTipSpeed"/> over the lever arm, rad/s, unsigned. This is
    /// the intent the tick count was derived from; the realized rate differs by
    /// the tick rounding (a few percent at most), and
    /// <see cref="ChargedSwing.SwingTrajectoryAt"/> feeds forward the realized
    /// one so the servo and the arc cannot disagree.
    /// </summary>
    float TargetAngularVelocity,
    bool IsValid)
{
    public int TotalTicks => WindupTicks + SweepTicks + FollowThroughTicks;
}

/// <summary>One sampled point of the scripted swing arc.</summary>
public readonly record struct SwingTrajectoryPoint(
    /// <summary>
    /// Signed barrel angle in radians, <c>0</c> = barrel straight up, growing
    /// the way the swing travels. Continuous and unwrapped, so a swing that
    /// passes through a half turn never jumps.
    /// </summary>
    float BarrelAngle,

    /// <summary>Signed feed-forward angular velocity in radians per second.</summary>
    float TargetAngularVelocity,
    bool IsValid);

/// <summary>
/// The pure arithmetic of the charged swing: charge accrual, charge feedback,
/// the derived arc, aim, and hit-lag duration. Engine-free and allocation-free;
/// a non-finite input yields an inert value rather than propagating NaN.
/// </summary>
public static class ChargedSwing
{
    private const float DegreesToRadians = MathF.PI / 180.0f;

    /// <summary>Charge as <c>0..1</c>, linear and clamped at both ends.</summary>
    public static float ChargeProgress(int ticks, int maxTicks)
    {
        if (maxTicks <= 0 || ticks <= 0)
        {
            return 0.0f;
        }

        return ticks >= maxTicks ? 1.0f : (float)ticks / maxTicks;
    }

    /// <summary>
    /// Shake magnitude for a charge level: <c>charge² · maxAmplitude</c>. The
    /// square is the point — early charge is barely a tremor and the last second
    /// is violent, so the player feels the cap approaching instead of reading a
    /// meter. Maximum lands exactly at full charge.
    /// </summary>
    public static float ShakeAmplitude(float charge, float maxAmplitude)
    {
        if (!float.IsFinite(charge) || !IsFiniteNonNegative(maxAmplitude))
        {
            return 0.0f;
        }

        float clamped = Math.Clamp(charge, 0.0f, 1.0f);
        return clamped * clamped * maxAmplitude;
    }

    /// <summary>
    /// A deterministic two-frequency wobble. The frequencies are incommensurate
    /// on purpose: a single sine reads as a mechanical oscillation, and a shake
    /// that visibly loops stops looking like strain. Presentation only — the
    /// physics body is never displaced by this (§7).
    /// </summary>
    public static Vector2 ShakeOffset(
        float timeSeconds,
        float amplitude,
        float primaryHz,
        float secondaryHz)
    {
        if (!float.IsFinite(timeSeconds) ||
            !IsFiniteNonNegative(amplitude) ||
            !IsFiniteNonNegative(primaryHz) ||
            !IsFiniteNonNegative(secondaryHz))
        {
            return Vector2.Zero;
        }

        float primary = MathF.Tau * primaryHz * timeSeconds;
        float secondary = MathF.Tau * secondaryHz * timeSeconds;

        // The 0.6/0.4 split keeps each axis inside the authored amplitude while
        // letting both frequencies contribute audibly to the motion.
        float x = (0.6f * MathF.Sin(primary)) + (0.4f * MathF.Sin(secondary));
        float y = (0.6f * MathF.Cos(secondary)) + (0.4f * MathF.Cos(primary));
        return new Vector2(x * amplitude, y * amplitude);
    }

    /// <summary>
    /// Derive the whole arc from the released charge. Tip speed interpolates the
    /// authored endpoints, the angular rate follows from the lever arm, and the
    /// sweep duration follows from the rate — so a stronger charge shortens the
    /// swing and raises its speed from one number, and no authored duration can
    /// ever contradict an authored speed.
    /// </summary>
    public static SwingPlan SwingPlanFor(
        float charge,
        float tipRadius,
        in ChargedSwingConstants constants)
    {
        if (!float.IsFinite(charge) ||
            !IsFinitePositive(tipRadius) ||
            !constants.IsWellFormed())
        {
            return default;
        }

        float clamped = Math.Clamp(charge, 0.0f, 1.0f);
        float tipSpeed = constants.TipSpeedUncharged +
                         ((constants.TipSpeedFull - constants.TipSpeedUncharged) * clamped);
        float omega = tipSpeed / tipRadius;
        if (!IsFinitePositive(omega))
        {
            return default;
        }

        float sweepRadians = constants.SweepDegrees * DegreesToRadians;
        float sweepSeconds = sweepRadians / omega;
        int sweepTicks = (int)MathF.Round(
            sweepSeconds * constants.TicksPerSecond, MidpointRounding.AwayFromZero);
        sweepTicks = Math.Clamp(
            sweepTicks, constants.MinimumSweepTicks, constants.MaximumSweepTicks);

        return new SwingPlan(
            constants.WindupTicks,
            sweepTicks,
            constants.FollowThroughTicks,
            constants.SweepDegrees,
            constants.FollowThroughDegrees,
            tipSpeed,
            omega,
            IsValid: true);
    }

    /// <summary>
    /// Sample the arc at <paramref name="tick"/> ticks into the swing. The
    /// angle is monotonic for the whole arc and the velocity term is what makes
    /// this a trajectory rather than a target: a settling servo handed only a
    /// moving angle damps itself toward a standstill and never reaches the
    /// commanded swing speed.
    ///
    /// <paramref name="directionSign"/> mirrors the arc: the canonical rightward
    /// swing leans back, snaps farther back, then sweeps down, under, and up
    /// through the contact zone, and a leftward swing is its exact reflection.
    /// </summary>
    public static SwingTrajectoryPoint SwingTrajectoryAt(
        int tick,
        in SwingPlan plan,
        int directionSign,
        in ChargedSwingConstants constants)
    {
        if (!plan.IsValid || !constants.IsWellFormed())
        {
            return default;
        }

        int sign = NormalizeSign(directionSign, 1);
        float secondsPerTick = 1.0f / constants.TicksPerSecond;
        float lean = constants.LeanDegrees * DegreesToRadians;
        float windup = constants.WindupDegrees * DegreesToRadians;
        float sweep = plan.SweepDegrees * DegreesToRadians;
        float follow = plan.FollowThroughDegrees * DegreesToRadians;

        int clampedTick = Math.Max(tick, 0);
        float angle;
        float rate;

        if (clampedTick < plan.WindupTicks)
        {
            // The release snap: one last pull farther behind the shoulder.
            float span = plan.WindupTicks * secondsPerTick;
            float u = (float)clampedTick / plan.WindupTicks;
            angle = -(lean + ((windup - lean) * u));
            rate = -(windup - lean) / span;
        }
        else if (clampedTick < plan.WindupTicks + plan.SweepTicks)
        {
            // The constant-rate plateau that carries the contact zone.
            float span = plan.SweepTicks * secondsPerTick;
            float u = (float)(clampedTick - plan.WindupTicks) / plan.SweepTicks;
            angle = -(windup + (sweep * u));
            rate = -sweep / span;
        }
        else if (clampedTick < plan.TotalTicks)
        {
            // Ease-out tail: momentum wrapping over the front shoulder.
            float span = plan.FollowThroughTicks * secondsPerTick;
            float u = (float)(clampedTick - plan.WindupTicks - plan.SweepTicks) /
                      plan.FollowThroughTicks;
            float eased = 1.0f - ((1.0f - u) * (1.0f - u));
            angle = -(windup + sweep + (follow * eased));
            rate = -follow * 2.0f * (1.0f - u) / span;
        }
        else
        {
            angle = -(windup + sweep + follow);
            rate = 0.0f;
        }

        angle *= sign;
        rate *= sign;
        if (!float.IsFinite(angle) || !float.IsFinite(rate))
        {
            return default;
        }

        return new SwingTrajectoryPoint(angle, rate, IsValid: true);
    }

    /// <summary>
    /// The barrel angle the upright servo holds outside the swing: straight up
    /// while gripped, leaned back away from the aim while charging. The lean is
    /// the telegraph — it is how the player reads which way the swing will go
    /// before it happens.
    /// </summary>
    public static float RestAngleFor(
        ChargedSwingState state,
        int directionSign,
        in ChargedSwingConstants constants)
    {
        if (!constants.IsWellFormed())
        {
            return 0.0f;
        }

        if (state != ChargedSwingState.Charging)
        {
            return 0.0f;
        }

        return -NormalizeSign(directionSign, 1) * constants.LeanDegrees * DegreesToRadians;
    }

    /// <summary>
    /// How long the whole game freezes for a scored hit, in routed ticks:
    /// linear in released charge between the authored endpoints. Capped so even
    /// a full-charge home run stays a punctuation rather than a hitch.
    /// </summary>
    public static int HitLagTicks(float charge, int minTicks, int maxTicks)
    {
        if (minTicks < 0)
        {
            minTicks = 0;
        }

        if (maxTicks < minTicks)
        {
            maxTicks = minTicks;
        }

        if (!float.IsFinite(charge))
        {
            return minTicks;
        }

        float clamped = Math.Clamp(charge, 0.0f, 1.0f);
        int ticks = (int)MathF.Round(
            minTicks + ((maxTicks - minTicks) * clamped), MidpointRounding.AwayFromZero);
        return Math.Clamp(ticks, minTicks, maxTicks);
    }

    /// <summary>
    /// The swing goes the way the cursor is travelling, and nothing else — the
    /// player aims by moving rather than by the game guessing who they meant.
    /// Travel below <paramref name="travelThreshold"/> is hand jitter and leaves
    /// the previous aim alone, so a bat held nearly still does not flicker.
    /// </summary>
    public static int SwingDirectionSign(float cursorTravelX, float travelThreshold, int lastSign)
    {
        int fallback = NormalizeSign(lastSign, 1);
        if (!float.IsFinite(cursorTravelX) || !IsFiniteNonNegative(travelThreshold))
        {
            return fallback;
        }

        if (MathF.Abs(cursorTravelX) < travelThreshold)
        {
            return fallback;
        }

        int sign = MathF.Sign(cursorTravelX);
        return sign == 0 ? fallback : sign;
    }

    internal static int NormalizeSign(int sign, int fallback) => sign switch
    {
        > 0 => 1,
        < 0 => -1,
        _ => fallback >= 0 ? 1 : -1,
    };

    internal static bool IsFinitePositive(float value) =>
        float.IsFinite(value) && value > 0.0f;

    internal static bool IsFiniteNonNegative(float value) =>
        float.IsFinite(value) && value >= 0.0f;
}

/// <summary>Input for the per-epoch home-run admission gate.</summary>
public readonly record struct SwingImpactAdmissionResult(
    /// <summary>Whether this contact may be scored at all.</summary>
    bool Admitted,

    /// <summary>Whether this contact consumes the epoch's single home-run hit.</summary>
    bool ClaimsEpoch,
    bool IsValid);

/// <summary>
/// Which contacts a swing is allowed to score. A home-run epoch spends itself on
/// its first hit that actually hurt, so a bat sweeping across a shoulder, an arm,
/// and a head is one home run and not three — while a zero-pain graze costs the
/// player nothing and leaves the attack live. The weak free swing keeps the
/// ordinary per-source episode deduplication it has always used.
///
/// This gate decides *whether* an impact is scored, never *how hard*: the
/// admitted event still carries the measured solver impulse into the shared pain
/// curve with no multiplier anywhere.
/// </summary>
public static class SwingImpactAdmission
{
    public static SwingImpactAdmissionResult Evaluate(
        SwingImpactMode mode,
        int epoch,
        bool alreadyClaimed,
        bool scoredPain) => mode switch
    {
        SwingImpactMode.None => new SwingImpactAdmissionResult(false, false, true),
        SwingImpactMode.WeakFreeSwing => new SwingImpactAdmissionResult(true, false, true),
        SwingImpactMode.HomeRun when epoch <= 0 =>
            new SwingImpactAdmissionResult(false, false, false),
        SwingImpactMode.HomeRun => new SwingImpactAdmissionResult(
            Admitted: !alreadyClaimed,
            ClaimsEpoch: !alreadyClaimed && scoredPain,
            IsValid: true),
        _ => new SwingImpactAdmissionResult(false, false, false),
    };
}
