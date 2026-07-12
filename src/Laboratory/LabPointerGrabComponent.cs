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
/// It is inert unless this is a non-headless debug build: release/exported builds
/// exclude the lab scene entirely, and headless scenarios drive the tether API
/// directly, so the harness must not fight their scripted cursor. Picking a body
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
    private Vector2 _cursor;

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

        // Live developer affordance only: never in release builds, and never
        // headless (scenarios own the tether cursor there).
        _active = BuildInfo.IsDebugBuild;
        SetProcessInput(_active);
    }

    public override void _Input(InputEvent @event)
    {
        if (!_active)
        {
            return;
        }
        ReceivedInputCount++;

        if (@event is InputEventMouse mouse)
            _cursor = mouse.Position;

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            _pendingPress = true;
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _pendingRelease = true;
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
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

        Vector2 cursor = DisplayServer.GetName() == "headless" ? _cursor : GetGlobalMousePosition();

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
                Grab.TryGrab(body!, cursor);
                LastPickedPart = body is PuppetPartBody part ? part.PartId : null;
                SuccessfulPickCount++;
            }
        }

        if (Grab.IsGrabbing)
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
    }

    private bool TryPick(Vector2 world, out RigidBody2D? body)
    {
        body = null;
        if (DisplayServer.GetName() == "headless")
        {
            float nearestDistance = float.MaxValue;
            foreach (Node node in GetTree().GetNodesInGroup("buddy_parts"))
            {
                if (node is not RigidBody2D candidate) continue;
                float candidateDistance = candidate.GlobalPosition.DistanceSquaredTo(world);
                if (candidateDistance < nearestDistance)
                {
                    nearestDistance = candidateDistance;
                    body = candidate;
                }
            }
            return body is not null && nearestDistance <= Mathf.Pow(PickRadius + 24.0f, 2);
        }

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
                colliderValue.AsGodotObject() is RigidBody2D candidate)
            {
                float distance = candidate.GlobalPosition.DistanceSquaredTo(world);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    body = candidate;
                }
            }
        }

        return body is not null;
    }
}
