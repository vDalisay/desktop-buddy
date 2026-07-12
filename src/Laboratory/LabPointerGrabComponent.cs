using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Grab;
using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>
/// Development-only pointer harness for the physics laboratory. It maps the mouse
/// onto the real <see cref="GrabTetherController"/> contract so a developer can
/// grab, drag, and throw the buddy or a loose object by hand while tuning — the
/// same TryGrab/MoveCursor/Release path the Milestone 2 input layer will drive.
///
/// It is inert outside debug builds: release/exported builds exclude the lab scene.
/// Headless activation is intentional because journeys synthesize events through
/// Godot's real input queue. Picking a body
/// under the cursor is an input concern and stays here; the tether keeps its pure
/// "acquire any body through one contract" role. The owning <see cref="App.BuddyLab"/>
/// calls <see cref="ResolvePendingInput"/> from its single fixed tick, where the
/// physics space state is valid.
/// </summary>
[GlobalClass]
public partial class LabPointerGrabComponent : Node2D
{
    private const float PickRadius = 10.0f;
    private const int MaxPickResults = 8;
    private const uint PickMask = CollisionLayers.BuddyParts | CollisionLayers.LooseObjects;
    private static readonly Color TetherColor = new("ff6b6b");

    [Export] public GrabTetherController Grab { get; set; } = null!;

    private bool _active;
    private bool _pendingPress;
    private bool _pendingRelease;
    private bool _pendingCancel;
    private bool _ownsGrab;
    private Vector2 _cursor;

    public Func<RigidBody2D, bool>? PickFilter { get; set; }

    public bool IsActive => _active;
    public int ReceivedInputCount { get; private set; }
    public BuddyPartId? LastPickedPart { get; private set; }
    public int SuccessfulPickCount { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Grab))
        {
            throw new InvalidOperationException("LabPointerGrabComponent requires an injected GrabTetherController.");
        }

        // Development affordance only. Headless journeys use the same queued-input path.
        _active = BuildInfo.IsDebugBuild;
        SetProcessInput(_active);
        SetProcessUnhandledInput(_active);
    }

    public override void _Input(InputEvent @event)
    {
        // Track all mouse motion so GUI-consumed events still keep the cursor truthful;
        // gameplay actions are handled only after GUI has declined the event.
        if (_active && @event is InputEventMouse mouse)
            _cursor = mouse.Position;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_active)
            return;

        ReceivedInputCount++;

        if (@event.IsActionPressed(InputActions.Primary))
        {
            _pendingPress = true;
        }
        else if (@event.IsActionReleased(InputActions.Primary))
        {
            _pendingRelease = true;
        }
        else if (@event.IsActionPressed(InputActions.Secondary))
        {
            // Right mouse cancels/drops without changing the selected tool.
            _pendingCancel = true;
        }
    }

    /// <summary>
    /// Applies the pointer's pending press/drag/release against the tether. Called
    /// by <see cref="App.BuddyLab"/> from the physics step so the space-state pick
    /// query is valid; safe to call every tick regardless of pause state.
    /// </summary>
    public void ResolvePendingInput()
    {
        if (!_active)
        {
            return;
        }

        if (!Grab.IsGrabbing)
            _ownsGrab = false;

        Vector2 cursor = GetViewport().GetCanvasTransform().AffineInverse() * _cursor;

        if (_pendingCancel)
        {
            _pendingCancel = false;
            _pendingPress = false;
            _pendingRelease = false;
            ReleaseIfGrabbing();
        }

        if (_pendingPress)
        {
            _pendingPress = false;
            if (!Grab.IsGrabbing && TryPick(cursor, out RigidBody2D? body))
            {
                if (Grab.TryGrab(body!, cursor))
                {
                    _ownsGrab = true;
                    LastPickedPart = body is PuppetPartBody part ? part.PartId : null;
                    SuccessfulPickCount++;
                }
            }
        }

        if (_ownsGrab && Grab.IsGrabbing)
        {
            Grab.MoveCursor(cursor);
        }

        if (_pendingRelease)
        {
            _pendingRelease = false;
            ReleaseIfGrabbing();
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_active || !Grab.IsGrabbing)
        {
            return;
        }

        // The harness node sits at the world origin, so world coordinates draw directly.
        GrabState grab = Grab.CurrentGrab;
        DrawLine(grab.GrabPoint, grab.CursorAnchor, TetherColor, 2.0f, true);
        DrawCircle(grab.CursorAnchor, 3.0f, TetherColor);
    }

    private void ReleaseIfGrabbing()
    {
        if (Grab.IsGrabbing)
        {
            Grab.Release();
        }
        _ownsGrab = false;
    }

    private bool TryPick(Vector2 world, out RigidBody2D? body)
    {
        body = null;
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
        {
            return false;
        }

        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = new CircleShape2D { Radius = PickRadius },
            Transform = new Transform2D(0.0f, world),
            CollisionMask = PickMask,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(query, MaxPickResults);
        float bestDistance = float.MaxValue;
        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (hit.TryGetValue("collider", out Variant colliderValue) &&
                colliderValue.AsGodotObject() is RigidBody2D candidate &&
                (PickFilter is null || PickFilter(candidate)))
            {
                float distance = candidate.GlobalPosition.DistanceSquaredTo(world);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    body = candidate;
                }
            }
        }

        if (body is not null)
            return true;

        // Some headless physics backends can return no overlap during the first
        // synchronization frame. Fall back only when the real query found nothing.
        float nearestDistance = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("buddy_parts"))
        {
            if (node is not RigidBody2D candidate || (PickFilter is not null && !PickFilter(candidate)))
                continue;
            float candidateDistance = candidate.GlobalPosition.DistanceSquaredTo(world);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                body = candidate;
            }
        }
        return body is not null && nearestDistance <= Mathf.Pow(PickRadius + 24.0f, 2);
    }
}
