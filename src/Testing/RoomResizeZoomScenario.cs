using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Milestone 1 resize/zoom regression: requests apply at a physics boundary,
/// preserve physical scale, clamp zoom to the room floor, and safely contain
/// bodies displaced by a shrinking room.
/// </summary>
public sealed class RoomResizeZoomScenario : IScenario
{
    public string Id => "room_resize_zoom";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("resize_zoom_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        PuppetPartBody head = lab.Buddy.Rig.GetPart(BuddyPartId.Head);
        float originalRadius = head.Radius;
        float originalMass = head.Mass;
        int initialApplyCount = lab.Boundaries.AppliedLayoutCount;

        lab.Boundaries.RequestLayout(new Vector2I(360, 270), 2.0);
        bool queuedUntilPhysics = lab.Boundaries.AppliedLayoutCount == initialApplyCount;
        bool appliedAtPhysics = await WaitForLayoutApply(tree, lab, initialApplyCount + 1);

        RoomLayout minimumLayout = lab.Boundaries.CurrentLayout;
        checks.Add(new StartupCheck("layout_applies_on_physics_boundary", queuedUntilPhysics && appliedAtPhysics &&
            lab.Boundaries.AppliedLayoutCount == initialApplyCount + 1,
            $"applied={lab.Boundaries.AppliedLayoutCount}"));
        checks.Add(new StartupCheck("minimum_room_clamps_zoom_but_retains_preference",
            Mathf.IsEqualApprox((float)minimumLayout.StoredZoom, 2.0f) &&
            Mathf.IsEqualApprox((float)minimumLayout.EffectiveZoom, 1.0f) &&
            Mathf.IsEqualApprox((float)minimumLayout.RoomWidth, RoomLayoutPolicy.MinimumRoomWidth) &&
            Mathf.IsEqualApprox((float)minimumLayout.RoomHeight, RoomLayoutPolicy.MinimumRoomHeight),
            $"stored={minimumLayout.StoredZoom:F2} effective={minimumLayout.EffectiveZoom:F2} room={minimumLayout.RoomWidth:F0}x{minimumLayout.RoomHeight:F0}"));

        head.GlobalPosition = new Vector2(900.0f, 700.0f);
        head.LinearVelocity = new Vector2(300.0f, 200.0f);
        int beforeContainmentApply = lab.Boundaries.AppliedLayoutCount;
        lab.Boundaries.RequestLayout(new Vector2I(360, 270), 2.0);
        await WaitForLayoutApply(tree, lab, beforeContainmentApply + 1);

        Rect2 bounds = lab.Boundaries.InnerBounds;
        bool contained = head.GlobalPosition.X <= bounds.End.X - head.Radius + 0.1f &&
                         head.GlobalPosition.Y <= bounds.End.Y - head.Radius + 0.1f;
        bool outwardVelocityRemoved = head.LinearVelocity.X <= 0.1f && head.LinearVelocity.Y <= 0.1f;
        checks.Add(new StartupCheck("resize_contains_outside_body_without_outward_impulse",
            lab.Containment.LastCorrectionCount > 0 && contained && outwardVelocityRemoved,
            $"corrections={lab.Containment.LastCorrectionCount} position={head.GlobalPosition} velocity={head.LinearVelocity}"));

        int beforeLargeApply = lab.Boundaries.AppliedLayoutCount;
        lab.Boundaries.RequestLayout(new Vector2I(960, 720), 2.0);
        await WaitForLayoutApply(tree, lab, beforeLargeApply + 1);
        RoomLayout largeLayout = lab.Boundaries.CurrentLayout;
        checks.Add(new StartupCheck("stored_zoom_restores_when_room_supports_it",
            Mathf.IsEqualApprox((float)largeLayout.EffectiveZoom, 2.0f) &&
            Mathf.IsEqualApprox((float)largeLayout.RoomWidth, 480.0f) &&
            Mathf.IsEqualApprox((float)largeLayout.RoomHeight, 360.0f),
            $"effective={largeLayout.EffectiveZoom:F2} room={largeLayout.RoomWidth:F0}x{largeLayout.RoomHeight:F0}"));
        checks.Add(new StartupCheck("zoom_does_not_rescale_physics",
            Mathf.IsEqualApprox(head.Radius, originalRadius) && Mathf.IsEqualApprox(head.Mass, originalMass),
            $"radius={head.Radius:F1} mass={head.Mass:F1}"));
        checks.Add(new StartupCheck("room_collision_contract",
            lab.Boundaries.CollisionLayer == CollisionLayers.RoomBounds &&
            lab.Boundaries.CollisionMask == CollisionLayers.MaskRoomBounds,
            $"layer={lab.Boundaries.CollisionLayer} mask={lab.Boundaries.CollisionMask}"));

        Vector2I[] aspectSizes =
        {
            new(480, 360), // 4:3
            new(640, 400), // 16:10
            new(640, 360), // 16:9
            new(840, 360), // 21:9
        };
        bool aspectLayoutsValid = true;
        foreach (Vector2I size in aspectSizes)
        {
            int beforeAspectApply = lab.Boundaries.AppliedLayoutCount;
            lab.Boundaries.RequestLayout(size, 2.0);
            aspectLayoutsValid &= await WaitForLayoutApply(tree, lab, beforeAspectApply + 1);
            RoomLayout layout = lab.Boundaries.CurrentLayout;
            aspectLayoutsValid &= layout.RoomWidth >= RoomLayoutPolicy.MinimumRoomWidth &&
                                  layout.RoomHeight >= RoomLayoutPolicy.MinimumRoomHeight &&
                                  ((RectangleShape2D)lab.Boundaries.Floor.Shape).Size.X == (float)layout.RoomWidth &&
                                  ((RectangleShape2D)lab.Boundaries.LeftWall.Shape).Size.Y == (float)layout.RoomHeight;
        }

        checks.Add(new StartupCheck("representative_aspects_rebuild_valid_room_geometry",
            aspectLayoutsValid, $"count={aspectSizes.Length}"));
        checks.Add(new StartupCheck("resize_updates_safe_recovery_pose",
            RecoveryPoseFits(lab), $"origin={lab.Buddy.Recovery.SafePoseOrigin} bounds={lab.Buddy.Recovery.SafeBounds}"));

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<bool> WaitForLayoutApply(SceneTree tree, BuddyLab lab, int targetCount)
    {
        for (int frame = 0; frame < 3; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Boundaries.AppliedLayoutCount >= targetCount)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RecoveryPoseFits(BuddyLab lab)
    {
        Rect2 bounds = lab.Buddy.Recovery.SafeBounds;
        Vector2 origin = lab.Buddy.Recovery.SafePoseOrigin;
        foreach (PuppetPartBody body in lab.Buddy.Rig.Parts)
        {
            PuppetPartDefinition? definition = lab.Buddy.Rig.Profile.FindPart(body.PartId);
            if (definition is null)
            {
                return false;
            }

            Vector2 center = origin + definition.RestPosition;
            if (center.X - definition.Radius < bounds.Position.X ||
                center.X + definition.Radius > bounds.End.X ||
                center.Y - definition.Radius < bounds.Position.Y ||
                center.Y + definition.Radius > bounds.End.Y)
            {
                return false;
            }
        }

        return true;
    }
}
