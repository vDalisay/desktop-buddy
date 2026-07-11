using System;
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
    private Vector2 _localGrabPoint;
    private Vector2 _cursorAnchor;
    private Vector2 _previousCursor;

    public bool IsInitialized { get; private set; }
    public bool IsGrabbing => GodotObject.IsInstanceValid(_target);
    public GrabState CurrentGrab { get; private set; }
    public GrabTelemetry Telemetry { get; private set; }
    public float LastReleaseSpeed { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException("GrabTetherController requires a valid GrabTetherProfile.");
        }

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
        Vector2 error = _cursorAnchor - grabWorld;
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
