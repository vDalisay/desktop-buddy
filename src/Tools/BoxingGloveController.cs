using System;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Sandbox;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Tools;

/// <summary>
/// Owns the Boxing Glove tool lifecycle (RAGDOLL §9.1): while the glove is the
/// selected tool its physical collider exists and is pulled toward the cursor by
/// the same bounded damped-elastic tether mechanism as the M1 grab
/// (<see cref="GrabTether"/>), anchored at the body's center so the pull is
/// torque-free. Real swing speed and measured contact impulse drive pain through
/// the shared pipeline; this controller applies force only. Selecting any other
/// tool despawns the collider.
/// </summary>
[GlobalClass]
public partial class BoxingGloveController : Node2D
{
    [Export] public BoxingGloveProfile Profile { get; set; } = null!;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;

    private BoxingGloveBody? _glove;
    private Vector2 _cursor;
    private Vector2 _previousCursor;
    private bool _hasCursor;
    private float _armingTravel;

    public bool IsInitialized { get; private set; }
    public bool IsActive => GodotObject.IsInstanceValid(_glove);
    public BoxingGloveBody? Glove => GodotObject.IsInstanceValid(_glove) ? _glove : null;
    public bool HasCursor => _hasCursor;
    public Vector2 Cursor => _cursor;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException("BoxingGloveController requires a valid BoxingGloveProfile.");
        }

        if (!GodotObject.IsInstanceValid(Pipeline) || !GodotObject.IsInstanceValid(Boundaries))
        {
            throw new InvalidOperationException("BoxingGloveController requires the interaction pipeline and room boundaries.");
        }

        IsInitialized = true;
    }

    /// <summary>Move the cursor anchor the glove is tethered to (sandbox coordinates).</summary>
    public void MoveCursor(Vector2 worldPoint)
    {
        _cursor = ClampToPlayableBounds(worldPoint);
        _hasCursor = true;
    }

    /// <summary>
    /// Invalidates the cursor anchor when the real pointer leaves the play
    /// window. The selected tool is preserved, but its physical actor must not
    /// remain pinned to the last in-bounds corner.
    /// </summary>
    public void ClearCursor()
    {
        _hasCursor = false;
        if (IsActive)
            Despawn();
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick(double delta)
    {
        RequireInitialized();
        bool wantActive = Pipeline.SelectedTool == ToolId.BoxingGlove && _hasCursor;
        if (wantActive && _hasCursor && !IsActive)
        {
            Spawn();
        }
        else if (!wantActive && IsActive)
        {
            Despawn();
        }

        if (!IsActive)
        {
            return;
        }

        BoxingGloveBody glove = _glove!;
        float dt = (float)delta;
        if (!glove.IsImpactArmed)
        {
            _armingTravel += _cursor.DistanceTo(_previousCursor);
            if (_armingTravel >= Profile.MinimumArmingTravel)
                glove.ArmImpacts();
        }
        Vector2 cursorVelocity = dt > 0.0f ? (_cursor - _previousCursor) / dt : Vector2.Zero;
        Vector2 error = _cursor - glove.GlobalPosition;
        Vector2 relativeVelocity = glove.LinearVelocity - cursorVelocity;

        var input = new GrabTetherInput(
            ToNumerics(error),
            ToNumerics(relativeVelocity),
            Profile.Stiffness,
            Profile.Damping,
            Profile.MaximumForce);
        GrabTetherResult result = GrabTether.Evaluate(input);
        glove.ApplyForce(ToGodot(result.Force));
        _previousCursor = _cursor;
    }

    private void Spawn()
    {
        var glove = new BoxingGloveBody { Name = "BoxingGlove" };
        glove.Configure(Profile);
        AddChild(glove);
        glove.GlobalPosition = _cursor;
        glove.LinearVelocity = Vector2.Zero;
        _previousCursor = _cursor;
        _armingTravel = 0.0f;
        _glove = glove;
    }

    private void Despawn()
    {
        if (GodotObject.IsInstanceValid(_glove))
        {
            _glove!.QueueFree();
        }

        _glove = null;
        _armingTravel = 0.0f;
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("BoxingGloveController used before initialization.");
        }
    }

    private Vector2 ClampToPlayableBounds(Vector2 worldPoint)
    {
        Rect2 bounds = Boundaries.InnerBounds;
        if (!bounds.HasArea())
            return worldPoint;

        float inset = Profile.Radius + Profile.WallClearance;
        float minimumX = bounds.Position.X + inset;
        float maximumX = bounds.End.X - inset;
        float minimumY = bounds.Position.Y + inset;
        float maximumY = bounds.End.Y - inset;
        if (maximumX < minimumX || maximumY < minimumY)
            return bounds.GetCenter();

        return new Vector2(
            Mathf.Clamp(worldPoint.X, minimumX, maximumX),
            Mathf.Clamp(worldPoint.Y, minimumY, maximumY));
    }

    private static NumericsVector2 ToNumerics(Vector2 value) => new(value.X, value.Y);

    private static Vector2 ToGodot(NumericsVector2 value) => new(value.X, value.Y);
}
