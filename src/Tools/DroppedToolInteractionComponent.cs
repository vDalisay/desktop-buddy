using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Transaction boundary for the Steam Demo's physical cursor-tool round trip. World instances
/// are transient loose objects; ownership and selected-tool state remain in persistent progress.
/// A tool is registered before selection changes on drop, and ownership/registry identity are
/// verified before selection changes on re-equip.
/// </summary>
[GlobalClass]
public partial class DroppedToolInteractionComponent : Node2D
{
    private const float PickRadius = 12.0f;
    private const int MaxPickResults = 8;

    private LooseObjectRegistry _objects = null!;
    private InteractionDamageComponent _pipeline = null!;
    private CursorToolController _cursorTools = null!;
    private GrabTetherController _grab = null!;
    private BuddyRoot _buddy = null!;

    public bool IsInitialized { get; private set; }

    public void Initialize(
        LooseObjectRegistry objects,
        InteractionDamageComponent pipeline,
        CursorToolController cursorTools,
        GrabTetherController grab,
        BuddyRoot buddy)
    {
        if (IsInitialized)
            return;
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(cursorTools);
        ArgumentNullException.ThrowIfNull(grab);
        ArgumentNullException.ThrowIfNull(buddy);
        if (!objects.IsInitialized || !pipeline.IsInitialized || !cursorTools.IsInitialized || !grab.IsInitialized)
            throw new InvalidOperationException("Dropped-tool interaction requires initialized gameplay dependencies.");

        _objects = objects;
        _pipeline = pipeline;
        _cursorTools = cursorTools;
        _grab = grab;
        _buddy = buddy;
        _pipeline.ToolChanged += OnToolChanged;
        IsInitialized = true;
    }

    /// <summary>
    /// Drops the currently visible compatible cursor tool and selects Grab. Failure leaves the
    /// equipped tool untouched; the loose body is not allowed to exist without registry identity.
    /// </summary>
    public bool TryDropSelected()
    {
        RequireInitialized();
        CursorToolProfile? profile = _cursorTools.ActiveProfile;
        CursorToolBody? heldBody = _cursorTools.Body;
        if (profile is null || heldBody is null ||
            !GodotObject.IsInstanceValid(profile) || !GodotObject.IsInstanceValid(heldBody) ||
            profile.WorldDrop is null || !GodotObject.IsInstanceValid(profile.WorldDrop) ||
            !_pipeline.Progress.IsToolUnlocked(profile.ContentId) ||
            _pipeline.SelectedTool != profile.Tool ||
            FindDropped(profile.ContentId) is not null)
        {
            return false;
        }

        var dropped = new DroppedCursorToolBody
        {
            Name = $"Dropped_{profile.ContentId.Replace('.', '_')}",
        };
        dropped.Configure(profile);
        AddChild(dropped);
        dropped.GlobalPosition = heldBody.GlobalPosition;
        dropped.GlobalRotation = heldBody.GlobalRotation;
        dropped.LinearVelocity = heldBody.LinearVelocity;
        dropped.AngularVelocity = heldBody.AngularVelocity;
        dropped.Sleeping = false;

        if (!_objects.TryRegister(dropped, profile.WorldDrop!, out _))
        {
            dropped.QueueFree();
            return false;
        }

        _pipeline.SelectTool(ToolId.Grab);
        if (_pipeline.SelectedTool == ToolId.Grab)
            return true;

        // Grab is a permanent entitlement, so this is defensive rollback rather than an expected
        // branch. If selection policy ever changes, no duplicate tool is left behind.
        RemoveDropped(dropped);
        return false;
    }

    /// <summary>Finds the nearest eligible dropped tool under one double-click and re-equips it.</summary>
    public bool TryReequipAt(Vector2 world)
    {
        RequireInitialized();
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
            return false;

        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = new CircleShape2D { Radius = PickRadius },
            Transform = new Transform2D(0.0f, world),
            CollisionMask = CollisionLayers.LooseObjects,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(query, MaxPickResults);
        DroppedCursorToolBody? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (!hit.TryGetValue("collider", out Variant colliderValue) ||
                colliderValue.AsGodotObject() is not DroppedCursorToolBody candidate ||
                !GodotObject.IsInstanceValid(candidate) || candidate.RuntimeId == 0)
            {
                continue;
            }

            float distance = candidate.GlobalPosition.DistanceSquaredTo(world);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }
        return nearest is not null && TryReequip(nearest);
    }

    /// <summary>
    /// Re-equips one live dropped tool. Rapid duplicate double-clicks are consumed by the body's
    /// single-use claim. Stale, unregistered, unknown, or unowned bodies are strict no-ops.
    /// </summary>
    public bool TryReequip(DroppedCursorToolBody body)
    {
        RequireInitialized();
        if (!GodotObject.IsInstanceValid(body) || body.RuntimeId == 0 ||
            _objects.FindBody(body.RuntimeId) != body ||
            !GodotObject.IsInstanceValid(body.ToolProfile) ||
            !_pipeline.Progress.IsToolUnlocked(body.ToolProfile.ContentId) ||
            !ContentIds.TryParseTool(body.ToolProfile.ContentId, out ToolId tool) ||
            !body.TryClaimReequip())
        {
            return false;
        }

        _pipeline.SelectTool(tool);
        if (_pipeline.SelectedTool != tool)
        {
            if (GodotObject.IsInstanceValid(body))
                body.ReleaseReequipClaim();
            return false;
        }

        // OnToolChanged recalls the same body for the normal case. Keep this explicit removal as
        // an idempotent fallback for the already-selected/desync case where no change event fires.
        if (GodotObject.IsInstanceValid(body) && body.RuntimeId != 0)
            RemoveDropped(body);
        return true;
    }

    public DroppedCursorToolBody? FindDropped(string contentId)
    {
        foreach (Node child in GetChildren())
        {
            if (child is DroppedCursorToolBody dropped && GodotObject.IsInstanceValid(dropped) &&
                dropped.RuntimeId != 0 &&
                string.Equals(dropped.ToolProfile.ContentId, contentId, StringComparison.Ordinal))
            {
                return dropped;
            }
        }
        return null;
    }

    private void OnToolChanged(ToolId _previous, ToolId selected)
    {
        // Selecting a physical tool through any existing UI/hotkey is also a recall operation.
        // This prevents an entitlement from being simultaneously equipped and lying in the room.
        string contentId = ContentIds.ForTool(selected);
        DroppedCursorToolBody? dropped = FindDropped(contentId);
        if (dropped is not null)
            RemoveDropped(dropped);
    }

    private void RemoveDropped(DroppedCursorToolBody body)
    {
        if (!GodotObject.IsInstanceValid(body))
            return;

        if (_buddy.ObjectInteraction.IsHolding &&
            _buddy.ObjectInteraction.TrackedRuntimeId == body.RuntimeId)
        {
            _buddy.ObjectInteraction.CancelActiveInteraction();
        }
        if (_grab.IsGrabbing && _grab.CurrentGrab.Target == body)
            _grab.Release(countsAsThrow: false);

        if (body.RuntimeId != 0)
            _objects.Unregister(body);
        body.QueueFree();
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("DroppedToolInteractionComponent used before initialization.");
    }

    public override void _ExitTree()
    {
        if (IsInitialized && GodotObject.IsInstanceValid(_pipeline))
            _pipeline.ToolChanged -= OnToolChanged;
    }
}
