using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Physics;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Grab;

/// <summary>
/// World-level player grab tether (ARCHITECTURE.md Section 4). Acquires any
/// <see cref="RigidBody2D"/> — a buddy part or a loose object — through the same
/// contract, applies a bounded damped-elastic pull toward a cursor anchor at the
/// acquired local point each fixed tick, and releases with a capped throw
/// velocity. It owns acquisition, force, strain, and release only; the fear
/// decision and any damage calculation live elsewhere.
///
/// The cursor anchor is driven through <see cref="TryGrab"/>/<see cref="MoveCursor"/>
/// — the public API the Milestone 2 input layer will call; the laboratory drives
/// it directly until then.
/// </summary>
[GlobalClass]
public partial class GrabTetherController : Node2D
{
    [Export] public GrabTetherProfile Profile { get; set; } = null!;

    private RigidBody2D? _target;
    private PuppetPartBody? _leashedPart;
    private GrabStretchLimiter _stretch = new();
    private Vector2 _localGrabPoint;
    private Vector2 _cursorAnchor;
    private Vector2 _previousCursor;

    public bool IsInitialized { get; private set; }
    public bool IsGrabbing => GodotObject.IsInstanceValid(_target);
    public GrabState CurrentGrab { get; private set; }
    public GrabTelemetry Telemetry { get; private set; }
    public float LastReleaseSpeed { get; private set; }

    // --- Elastic limb telemetry (owner request 2026-07-25) ---
    /// <summary>Phase of the stretch → strain → snap sequence for the held limb.</summary>
    public GrabStretchState StretchState { get; private set; }
    /// <summary>Routed ticks left before a strained limb snaps back; 0 when not straining.</summary>
    public int StretchTicksRemaining { get; private set; }
    /// <summary>How far past the limit the cursor currently is, in px.</summary>
    public float StretchOverpull { get; private set; }
    /// <summary>Largest overpull of the current strain — what the fling scales from.</summary>
    public float PeakStretchOverpull => _stretch.PeakOverpull;
    /// <summary>Impulse applied by the most recent snap-back fling.</summary>
    public float LastSnapImpulse { get; private set; }
    /// <summary>Snap-back flings since this controller was initialized.</summary>
    public int SnapCount { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException("GrabTetherController requires a valid GrabTetherProfile.");
        }

        _stretch = new GrabStretchLimiter(new GrabStretchTuning(
            Profile.StretchLimitHandWidths,
            Profile.StretchShakeTicks,
            Profile.StretchShakeAmplitude,
            Profile.StretchShakeCycleTicks,
            Profile.StretchShakeRampTicks,
            Profile.StretchShakeRampMultiplier,
            Profile.StretchReleaseHysteresis,
            Profile.SnapImpulseBase,
            Profile.SnapImpulsePerOverpullPixel,
            Profile.MaximumSnapImpulse));
        IsInitialized = true;
    }

    /// <summary>Acquire <paramref name="target"/> at a world point; returns false for an invalid body.</summary>
    public bool TryGrab(RigidBody2D target, Vector2 worldPoint)
    {
        RequireInitialized();
        if (!GodotObject.IsInstanceValid(target))
        {
            return false;
        }

        _target = target;
        // Only a leashed buddy part stretches: a loose object has no arm, and the torso is
        // the anchor itself, so both keep the plain unlimited tether.
        _leashedPart = target as PuppetPartBody is { HasStretchLeash: true } part ? part : null;
        _stretch.Reset();
        StretchState = GrabStretchState.Slack;
        StretchTicksRemaining = 0;
        StretchOverpull = 0.0f;
        _localGrabPoint = target.ToLocal(worldPoint);
        _cursorAnchor = worldPoint;
        _previousCursor = worldPoint;
        CurrentGrab = new GrabState(true, target, worldPoint, worldPoint);
        Telemetry = new GrabTelemetry(true, 0.0f, Vector2.Zero, false, LastReleaseSpeed);
        return true;
    }

    /// <summary>Move the cursor anchor the tether pulls toward (sandbox coordinates).</summary>
    public void MoveCursor(Vector2 worldPoint) => _cursorAnchor = worldPoint;

    public void PhysicsTick(double delta)
    {
        RequireInitialized();
        if (!GodotObject.IsInstanceValid(_target))
        {
            _target = null;
            CurrentGrab = default;
            Telemetry = new GrabTelemetry(false, 0.0f, Vector2.Zero, false, LastReleaseSpeed);
            return;
        }

        Vector2 grabWorld = _target.ToGlobal(_localGrabPoint);
        float dt = (float)delta;
        Vector2 cursorVelocity = dt > 0.0f ? (_cursorAnchor - _previousCursor) / dt : Vector2.Zero;
        Vector2 pointVelocity = VelocityAt(_target, grabWorld);

        // A leashed limb may only be pulled to the stretch limit; past that it strains in
        // place, buzzes, and eventually snaps back and flings the buddy after the hand.
        Vector2 pullTarget = _cursorAnchor;
        if (_leashedPart is not null && GodotObject.IsInstanceValid(_leashedPart))
        {
            GrabStretchResult stretch = _stretch.Tick(
                ToNumerics(_leashedPart.StretchAnchorWorld),
                ToNumerics(_cursorAnchor),
                _leashedPart.Radius);

            StretchState = stretch.State;
            StretchTicksRemaining = stretch.ShakeTicksRemaining;
            StretchOverpull = stretch.Overpull;

            if (stretch.State == GrabStretchState.Snapped)
            {
                SnapBack(stretch);
                return;
            }

            pullTarget = ToGodot(stretch.ClampedTarget + stretch.ShakeOffset);
        }

        Vector2 error = pullTarget - grabWorld;
        Vector2 relativeVelocity = pointVelocity - cursorVelocity;

        var input = new GrabTetherInput(
            ToNumerics(error),
            ToNumerics(relativeVelocity),
            Profile.Stiffness,
            Profile.Damping,
            Profile.MaximumForce);
        GrabTetherResult result = GrabTether.Evaluate(input);

        Vector2 force = ToGodot(result.Force);
        _target.ApplyForce(force, grabWorld - _target.GlobalPosition);
        _previousCursor = _cursorAnchor;

        CurrentGrab = new GrabState(true, _target, _cursorAnchor, grabWorld);
        Telemetry = new GrabTelemetry(true, result.Extension, force, result.ForceClamped, LastReleaseSpeed);
    }

    /// <summary>
    /// The limb snapped: fling the anchor body along the stretch direction, then let go. The
    /// impulse goes to the anchor (the torso) so the whole buddy is launched and the limbs
    /// trail through the passive constraints, rather than one hand flicking off on its own.
    /// </summary>
    private void SnapBack(in GrabStretchResult stretch)
    {
        PuppetPartBody? anchor = _leashedPart?.StretchAnchor;
        if (anchor is not null && GodotObject.IsInstanceValid(anchor))
        {
            anchor.ApplyCentralImpulse(ToGodot(stretch.SnapDirection) * stretch.SnapImpulse);
        }

        LastSnapImpulse = stretch.SnapImpulse;
        SnapCount++;
        StretchState = GrabStretchState.Snapped;
        StretchTicksRemaining = 0;
        Release();
    }

    /// <summary>Release the target, preserving its motion capped to the throw-speed cap.</summary>
    public void Release()
    {
        if (GodotObject.IsInstanceValid(_target))
        {
            NumericsVector2 capped = GrabTether.CapReleaseVelocity(
                ToNumerics(_target.LinearVelocity), Profile.ThrowSpeedCap);
            _target.LinearVelocity = ToGodot(capped);
            LastReleaseSpeed = _target.LinearVelocity.Length();
        }

        _target = null;
        _leashedPart = null;
        _stretch.Reset();
        StretchTicksRemaining = 0;
        StretchOverpull = 0.0f;
        CurrentGrab = default;
        Telemetry = new GrabTelemetry(false, 0.0f, Vector2.Zero, false, LastReleaseSpeed);
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("GrabTetherController used before initialization.");
        }
    }

    private static Vector2 VelocityAt(RigidBody2D body, Vector2 worldPoint)
    {
        Vector2 offset = worldPoint - body.GlobalPosition;
        Vector2 perpendicular = new(-offset.Y, offset.X);
        return body.LinearVelocity + (perpendicular * body.AngularVelocity);
    }

    private static NumericsVector2 ToNumerics(Vector2 value) => new(value.X, value.Y);

    private static Vector2 ToGodot(NumericsVector2 value) => new(value.X, value.Y);
}
