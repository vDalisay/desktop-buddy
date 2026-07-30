using System;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Tools;

/// <summary>
/// What <see cref="CursorToolController"/> should apply to the tool body this
/// tick. The worker computes it; the controller applies it. Nothing here reads
/// hardware input or the scene tree.
///
/// <see cref="DrivesTether"/> and <see cref="OwnsRotation"/> are the seams that
/// keep every non-swing tool on exactly the path it had before this component
/// existed: both are false for them, and the controller runs its original
/// centre-anchored tether and swing-alignment servo untouched.
/// </summary>
public readonly record struct ChargedSwingDrive(
    ChargedSwingState State,

    /// <summary>True when <see cref="Force"/> replaces the controller's own tether.</summary>
    bool DrivesTether,
    Vector2 Force,

    /// <summary>
    /// Where the force acts, as a world-oriented offset from the body origin.
    /// Zero while following (a torque-free centre pull); the rotated handle
    /// point while gripped or swinging, so the bat genuinely hangs from its grip
    /// instead of being pinned through its middle.
    /// </summary>
    Vector2 ForceOffset,

    /// <summary>True when the worker steers rotation and the alignment servo stands down.</summary>
    bool OwnsRotation,
    bool AppliesTorque,
    float Torque,
    SwingImpactContext Context,
    float Charge,
    int DirectionSign);

/// <summary>
/// The grip/charge/swing worker for one cursor tool (§4.3). It holds the pure
/// <see cref="ChargedSwingMachine"/> state, turns it into bounded force and
/// torque targets, and publishes semantic edges upward for presentation.
///
/// It deliberately has no <c>_PhysicsProcess</c>: the composition root ticks
/// every worker in one fixed order, and a component that scheduled itself would
/// break the laboratory's pause/step controls and the routed-tick accounting
/// that charge, swings, and hit lag are all measured in.
///
/// The Boxing Glove and every other tool whose profile authors no
/// <see cref="SwingToolProfile"/> never leaves <see cref="ChargedSwingState.Follow"/>
/// and never claims the tether or the rotation, so their behavior is exactly
/// what it was before this existed.
/// </summary>
[GlobalClass]
public partial class ChargedSwingComponent : Node
{
    private ChargedSwingPhase _phase = ChargedSwingPhase.Initial;
    private CursorToolProfile? _profile;
    private SwingToolProfile? _swing;
    private ChargedSwingConstants _constants;
    private Vector2 _anchor;
    private bool _hasAnchor;
    private bool _grip;
    private bool _chargeHeld;
    private long _routedTick;
    private long _releasedTick;
    private int _graceRemaining;
    private SwingImpactContext _published = SwingImpactContext.FreeSwing;
    private Vector2 _pivotInset;

    /// <summary>Fired once when a charge reaches the cap — the tip glint.</summary>
    public event Action? ChargeCompleted;

    /// <summary>Fired once on the release that committed a swing: charge, then epoch.</summary>
    public event Action<float, int>? SwingReleased;

    public ChargedSwingState State => _phase.State;
    public int ChargeTicks => _phase.ChargeTicks;
    public float Charge => ChargedSwing.ChargeProgress(_phase.ChargeTicks, MaxChargeTicks);
    public int SwingEpoch => _phase.SwingEpoch;
    public int DirectionSign => _phase.DirectionSign;
    public bool IsGripHeld => _grip;
    public bool IsChargeHeld => _chargeHeld;
    public float ReleasedCharge => _phase.ReleasedCharge;
    public int SwingTicksInState => _phase.TicksInState;
    public Vector2 LatchedPivot => ToGodot(_phase.Pivot);

    /// <summary>The immutable plan currently being executed, or an invalid default outside a swing.</summary>
    public SwingPlan CurrentPlan =>
        _swing is not null && _profile is not null &&
        _phase.State is ChargedSwingState.Swinging or ChargedSwingState.Recovery
            ? ChargedSwing.SwingPlanFor(
                _phase.ReleasedCharge, _profile.HandleToTipRadius, _constants)
            : default;

    /// <summary>The impact context the live body should carry right now.</summary>
    public SwingImpactContext Context => _published;

    /// <summary>Whether the tool currently being driven can be gripped and swung at all.</summary>
    public bool IsSwingCapable => _swing is not null;

    /// <summary>
    /// The reach a gripped pivot must keep from each wall, beyond whatever inset
    /// the following tool already uses. Zero for a tool that cannot be swung.
    /// </summary>
    public Vector2 PivotInset => _pivotInset;

    private int MaxChargeTicks => _swing?.MaxChargeTicks ?? SwingToolProfile.ConfirmedMaxChargeTicks;

    public void SetGrip(bool held) => _grip = held;

    public void SetChargeHeld(bool held) => _chargeHeld = held;

    /// <summary>
    /// Drops grip and charge without cancelling a swing already in flight. The
    /// arc is committed once the charge is let go, so a pointer leaving the
    /// window mid-swing must not strand the bat at an angle.
    /// </summary>
    public void ReleaseInput()
    {
        _grip = false;
        _chargeHeld = false;
    }

    /// <summary>
    /// Abandons everything: the pointer left, the tool was swapped, or the body
    /// despawned. The epoch counter deliberately survives, so a respawned tool
    /// can never reuse an epoch number an earlier body already spent — the pain
    /// pipeline keys its one-hit-per-swing claim on that number.
    /// </summary>
    public void Reset()
    {
        _grip = false;
        _chargeHeld = false;
        _hasAnchor = false;
        _releasedTick = 0L;
        _graceRemaining = 0;
        _published = SwingImpactContext.FreeSwing;
        _phase = ChargedSwingPhase.Initial with { SwingEpoch = _phase.SwingEpoch };
    }

    /// <summary>
    /// Point the worker at the tool it is driving. A profile with no swing data
    /// leaves the worker inert and the controller on its ordinary follow path.
    /// </summary>
    public void Configure(CursorToolProfile? profile)
    {
        if (ReferenceEquals(profile, _profile))
        {
            return;
        }

        _profile = profile;
        _swing = profile is not null && profile.IsSwingCapable ? profile.Swing : null;
        _constants = _swing?.ToConstants() ?? default;
        _pivotInset = ComputePivotInset(profile, _swing);
        Reset();
    }

    /// <summary>
    /// Advance one routed tick. <paramref name="cursor"/> is the raw pointer —
    /// the semantic anchor the player is authoring — and the worker decides how
    /// much of it the physical anchor is allowed to follow.
    /// </summary>
    public ChargedSwingDrive Tick(
        CursorToolBody body,
        Vector2 cursor,
        Vector2 cursorVelocity,
        float delta)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (_swing is null || _profile is null || delta <= 0.0f)
        {
            _published = SwingImpactContext.FreeSwing;
            return Inert();
        }

        SwingToolProfile swing = _swing;
        CursorToolProfile profile = _profile;
        if (_routedTick < long.MaxValue)
        {
            _routedTick++;
        }

        // Aim is pure cursor travel: the bat swings the way the mouse is moving,
        // so the swing always lands in front of where the player is dragging it.
        // No target is looked up and no proximity is guessed.
        int aim = ChargedSwing.SwingDirectionSign(
            cursorVelocity.X * delta, swing.DirectionTravelThreshold, _phase.DirectionSign);

        Vector2 handleOffset = profile.HandleLocalOffset.Rotated(body.GlobalRotation);
        Vector2 handlePoint = body.GlobalPosition + handleOffset;
        ChargedSwingState before = _phase.State;

        ChargedSwingResult result = ChargedSwingMachine.Tick(new ChargedSwingInput(
            _phase,
            _grip,
            _chargeHeld,
            aim,
            ToNumerics(handlePoint),
            profile.HandleToTipRadius,
            _constants));
        if (!result.IsValid)
        {
            // Malformed data must leave an inert tool rather than a NaN body.
            _published = SwingImpactContext.FreeSwing;
            return Inert();
        }

        _phase = result.Phase;
        if (result.ChargeCompleted)
        {
            ChargeCompleted?.Invoke();
        }

        if (result.SwingReleased)
        {
            _releasedTick = _routedTick;
            SwingReleased?.Invoke(result.ReleasedCharge, result.SwingEpoch);
        }

        PublishContext(result, swing);
        ReanchorOnStateChange(before, body, handlePoint);

        return _phase.State switch
        {
            ChargedSwingState.Follow => Follow(body, cursor, delta, swing, profile),
            ChargedSwingState.Swinging or ChargedSwingState.Recovery =>
                HoldPivot(body, handleOffset, handlePoint, swing, profile),
            _ => Grip(body, cursor, delta, handleOffset, handlePoint, swing, profile),
        };
    }

    /// <summary>
    /// The weak free swing. The raw cursor stays the semantic pointer, but the
    /// physical anchor advances toward it at a fixed cap, so a teleporting or
    /// very high-DPI pointer cannot manufacture a home-run-grade impulse out of
    /// one frame's travel. The authored cap equals today's benchmark swing speed
    /// by construction, so ordinary input is not slowed at all — the cap only
    /// bites on input faster than the current benchmark.
    /// </summary>
    private ChargedSwingDrive Follow(
        CursorToolBody body,
        Vector2 cursor,
        float delta,
        SwingToolProfile swing,
        CursorToolProfile profile)
    {
        Vector2 anchorVelocity = AdvanceAnchor(cursor, swing.FreeSwingAnchorSpeedCap * delta, delta);
        Vector2 force = Tether(
            _anchor - body.GlobalPosition,
            body.LinearVelocity - anchorVelocity,
            profile.Stiffness,
            profile.Damping,
            swing.FreeSwingForceCap);

        return new ChargedSwingDrive(
            ChargedSwingState.Follow,
            DrivesTether: true,
            force,
            Vector2.Zero,
            OwnsRotation: false,
            AppliesTorque: false,
            0.0f,
            _published,
            Charge,
            _phase.DirectionSign);
    }

    /// <summary>
    /// Gripped or charging: the tether closes to the <b>handle</b>, not the
    /// centre, and an unfolded servo holds the barrel up. The half-turn fold
    /// that stops a two-ended tool spinning around while following must not be
    /// used here — a real bat has a barrel end and a handle end, so upside-down
    /// is wrong rather than equivalent.
    ///
    /// Entering the grip re-anchors the body from its centre to its handle, and
    /// that reposition is rate-limited for the same reason the follow anchor is:
    /// picking a bat up is not an attack, and this state scores nothing at all.
    /// </summary>
    private ChargedSwingDrive Grip(
        CursorToolBody body,
        Vector2 cursor,
        float delta,
        Vector2 handleOffset,
        Vector2 handlePoint,
        SwingToolProfile swing,
        CursorToolProfile profile)
    {
        Vector2 anchorVelocity = AdvanceAnchor(cursor, swing.FreeSwingAnchorSpeedCap * delta, delta);
        Vector2 force = Tether(
            _anchor - handlePoint,
            PointVelocity(body, handleOffset) - anchorVelocity,
            profile.Stiffness,
            profile.Damping,
            profile.MaximumForce);

        // While charging the upright target leans away from the swing side —
        // the batter pulling back, which is what telegraphs the coming swing.
        float restAngle = ChargedSwing.RestAngleFor(_phase.State, _phase.DirectionSign, _constants);
        return new ChargedSwingDrive(
            _phase.State,
            DrivesTether: true,
            force,
            handleOffset,
            OwnsRotation: true,
            AppliesTorque: true,
            UprightTorque(body, restAngle, swing, profile),
            _published,
            Charge,
            _phase.DirectionSign);
    }

    /// <summary>
    /// Swinging and recovering: the pivot is the latched release point, not the
    /// moving cursor, so a last-second pointer flick cannot overwhelm the charge
    /// curve. The swing keeps its own much larger force cap because holding a
    /// pivot through a rotation is a centripetal load, not merely a constraint.
    ///
    /// The trajectory servo follows both the scripted barrel angle and its
    /// nonzero commanded velocity. An ordinary settling servo would damp the
    /// requested spin toward zero and arrive below the authored tip speed.
    /// </summary>
    private ChargedSwingDrive HoldPivot(
        CursorToolBody body,
        Vector2 handleOffset,
        Vector2 handlePoint,
        SwingToolProfile swing,
        CursorToolProfile profile)
    {
        bool swinging = _phase.State == ChargedSwingState.Swinging;
        Vector2 pivot = swinging ? ToGodot(_phase.Pivot) : handlePoint;
        Vector2 force = Tether(
            pivot - handlePoint,
            PointVelocity(body, handleOffset),
            swinging ? swing.SwingAnchorStiffness : profile.Stiffness,
            swinging ? swing.SwingAnchorDamping : profile.Damping,
            swinging ? swing.SwingAnchorForceCap : profile.MaximumForce);

        float torque = 0.0f;
        bool appliesTorque = !swinging;
        if (swinging)
        {
            SwingPlan plan = ChargedSwing.SwingPlanFor(
                _phase.ReleasedCharge, profile.HandleToTipRadius, _constants);
            SwingTrajectoryPoint target = ChargedSwing.SwingTrajectoryAt(
                _phase.TicksInState, plan, _phase.DirectionSign, _constants);
            SwingTrajectoryServoResult servo = SwingTrajectoryServo.Evaluate(
                new SwingTrajectoryServoInput(
                    target.BarrelAngle - body.GlobalRotation,
                    body.AngularVelocity,
                    target.TargetAngularVelocity,
                    swing.SwingServoStiffness,
                    swing.SwingServoDamping,
                    swing.SwingTorqueCap));
            if (target.IsValid && servo.IsValid)
            {
                // The pivot force is applied at the handle and therefore
                // contributes torque of its own. The trajectory servo commands
                // the desired *net* torque; compensate the handle-force moment
                // before applying the motor torque or the two add together and
                // overshoot the authored angular velocity by nearly 2x.
                float handleForceTorque = handleOffset.Cross(force);
                torque = Mathf.Clamp(
                    servo.Torque - handleForceTorque,
                    -swing.SwingTorqueCap,
                    swing.SwingTorqueCap);
                appliesTorque = true;
            }
        }
        else
        {
            torque = UprightTorque(body, 0.0f, swing, profile);
        }

        return new ChargedSwingDrive(
            _phase.State,
            DrivesTether: true,
            force,
            handleOffset,
            OwnsRotation: true,
            appliesTorque,
            torque,
            _published,
            Charge,
            _phase.DirectionSign);
    }

    /// <summary>What a tool with no swing data produces: nothing, so nothing changes.</summary>
    private ChargedSwingDrive Inert() => new(
        ChargedSwingState.Follow,
        DrivesTether: false,
        Vector2.Zero,
        Vector2.Zero,
        OwnsRotation: false,
        AppliesTorque: false,
        0.0f,
        _published,
        0.0f,
        _phase.DirectionSign);

    /// <summary>
    /// The context the body carries into the pain pipeline. A home-run epoch
    /// outlives its own state by the authored grace, because the contact that
    /// ends a swing is observed a tick after the solver produced it and would
    /// otherwise be scored as an innocent recovery touch.
    /// </summary>
    private void PublishContext(in ChargedSwingResult result, SwingToolProfile swing)
    {
        if (result.ImpactMode == SwingImpactMode.HomeRun)
        {
            _published = new SwingImpactContext(
                SwingImpactMode.HomeRun,
                result.SwingEpoch,
                result.ReleasedCharge,
                _releasedTick);
            _graceRemaining = swing.ContactObservationGraceTicks;
            return;
        }

        if (_graceRemaining > 0)
        {
            _graceRemaining--;
            return;
        }

        _published = new SwingImpactContext(result.ImpactMode, 0, 0.0f, 0L);
    }

    /// <summary>
    /// Following measures its tether error from the body's centre and gripping
    /// measures it from the handle, roughly half a bat apart. Carrying the
    /// anchor across that change unaltered would hand the tether a large error
    /// the instant the player pressed or released the grip and yank the bat —
    /// so the anchor is reseeded onto whichever point the new state pulls, and
    /// the rate limiter then lets it travel to the cursor no faster than any
    /// other motion. That is what makes picking the bat up a non-event.
    /// </summary>
    private void ReanchorOnStateChange(
        ChargedSwingState before,
        CursorToolBody body,
        Vector2 handlePoint)
    {
        if (before == _phase.State)
        {
            return;
        }

        _anchor = _phase.State switch
        {
            ChargedSwingState.Follow => body.GlobalPosition,
            ChargedSwingState.Gripped or ChargedSwingState.Charging => handlePoint,
            _ => _anchor,
        };
        _hasAnchor = true;
    }

    /// <summary>
    /// Move the physical anchor toward the pointer at the authored cap and
    /// report the velocity it actually travelled at — which is what the tether's
    /// damping term must see, so the bat is damped against the anchor it is
    /// chasing rather than against a pointer that jumped.
    /// </summary>
    private Vector2 AdvanceAnchor(Vector2 target, float maximumStep, float delta)
    {
        if (!_hasAnchor)
        {
            _anchor = target;
            _hasAnchor = true;
            return Vector2.Zero;
        }

        Vector2 previous = _anchor;
        _anchor = _anchor.MoveToward(target, maximumStep);
        return (_anchor - previous) / delta;
    }

    private static Vector2 Tether(
        Vector2 error,
        Vector2 relativeVelocity,
        float stiffness,
        float damping,
        float maximumForce)
    {
        GrabTetherResult result = GrabTether.Evaluate(new GrabTetherInput(
            ToNumerics(error),
            ToNumerics(relativeVelocity),
            stiffness,
            damping,
            maximumForce));
        return ToGodot(result.Force);
    }

    /// <summary>
    /// The barrel is local <c>-Y</c> and the collider's long axis is local Y, so
    /// a body rotation of zero already points the barrel straight up: the
    /// domain's barrel angle and the body's rotation are the same number, and no
    /// quarter-turn correction belongs here.
    /// </summary>
    private static float UprightTorque(
        CursorToolBody body,
        float barrelAngle,
        SwingToolProfile swing,
        CursorToolProfile profile)
    {
        AlignmentTorqueResult result = AlignmentTorque.Evaluate(new AlignmentTorqueInput(
            barrelAngle - body.GlobalRotation,
            body.AngularVelocity,
            swing.GripStiffness,
            swing.GripDamping,
            profile.MaximumAlignTorque));
        return result.IsValid ? result.Torque : 0.0f;
    }

    /// <summary>Velocity of a point rigidly attached to the body at a world-oriented offset.</summary>
    private static Vector2 PointVelocity(CursorToolBody body, Vector2 offset) =>
        body.LinearVelocity + (new Vector2(-offset.Y, offset.X) * body.AngularVelocity);

    /// <summary>
    /// The reach a gripped pivot needs from each wall, measured over the barrel
    /// angles the planned arc really passes through rather than assuming a full
    /// circle. A blanket full-length inset on both axes would be safe but would
    /// needlessly forbid aiming along a wall.
    /// </summary>
    private static Vector2 ComputePivotInset(CursorToolProfile? profile, SwingToolProfile? swing)
    {
        if (profile is null || swing is null)
        {
            return Vector2.Zero;
        }

        // The swept extent is the tip's lever arm plus the barrel's own
        // half-thickness, so the drawn edge of the bat clears the wall too.
        float reach = profile.HandleToTipRadius + profile.Radius;
        float swept = swing.WindupDegrees + swing.SweepDegrees + swing.FollowThroughDegrees;
        float maximumX = 0.0f;
        float maximumY = 0.0f;

        // Compass convention: zero is straight up and the angle grows the way the
        // swing travels. Sampling one aim sign is enough — the other is its
        // mirror, so the widest |sin| and |cos| are the same on both sides.
        for (float offset = 0.0f; offset <= swept; offset += 1.0f)
        {
            float radians = Mathf.DegToRad(swing.LeanDegrees + offset);
            maximumX = Mathf.Max(maximumX, Mathf.Abs(Mathf.Sin(radians)));
            maximumY = Mathf.Max(maximumY, Mathf.Abs(Mathf.Cos(radians)));
        }

        return new Vector2(reach * maximumX, reach * maximumY);
    }

    private static NumericsVector2 ToNumerics(Vector2 value) => new(value.X, value.Y);

    private static Vector2 ToGodot(NumericsVector2 value) => new(value.X, value.Y);
}
