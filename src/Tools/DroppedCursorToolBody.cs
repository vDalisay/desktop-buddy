using System;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Registry-backed world form of an owned cursor tool. It deliberately remains a
/// <see cref="LooseObjectBody"/> so the existing Grab picker, throw attribution, rest tracking,
/// live-object cap and eviction policy all apply unchanged. The cursor profile supplies the
/// recognizable collider/drawing; its WorldDrop profile supplies loose-object policy.
/// </summary>
[GlobalClass]
public partial class DroppedCursorToolBody : LooseObjectBody
{
    private const int CircleSegments = 32;
    private const float OutlineWidth = 2.0f;
    private bool _reequipClaimed;

    public CursorToolProfile ToolProfile { get; private set; } = null!;
    public bool ReequipClaimed => _reequipClaimed;

    public void Configure(CursorToolProfile toolProfile)
    {
        ArgumentNullException.ThrowIfNull(toolProfile);
        if (!GodotObject.IsInstanceValid(toolProfile) ||
            toolProfile.WorldDrop is null || !GodotObject.IsInstanceValid(toolProfile.WorldDrop))
        {
            throw new InvalidOperationException("Dropped cursor tool requires an authored WorldDrop profile.");
        }

        ToolProfile = toolProfile;
        base.Configure(toolProfile.WorldDrop!);

        // LooseObjectBody creates the registry-compatible collider slot. Replace only its shape
        // so an elongated bat still collides as the same capsule players were holding rather
        // than becoming a 90px-wide circle because its registry envelope is 45px.
        CollisionShape2D? collider = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        collider ??= FindChild("CollisionShape2D", false, false) as CollisionShape2D;
        if (collider is null)
        {
            foreach (Node child in GetChildren())
            {
                if (child is CollisionShape2D shape)
                {
                    collider = shape;
                    break;
                }
            }
        }

        if (collider is null)
            throw new InvalidOperationException("Dropped cursor tool lost its loose-object collider.");

        collider.Shape = toolProfile.IsElongated
            ? new CapsuleShape2D { Radius = toolProfile.Radius, Height = toolProfile.Length }
            : new CircleShape2D { Radius = toolProfile.Radius };
        QueueRedraw();
    }

    /// <summary>
    /// Single-use guard for double-click re-equip. A second click delivered before QueueFree is
    /// processed becomes a no-op instead of selecting/removing the same object twice.
    /// </summary>
    public bool TryClaimReequip()
    {
        if (_reequipClaimed || RuntimeId == 0 || !GodotObject.IsInstanceValid(this))
            return false;
        _reequipClaimed = true;
        return true;
    }

    public void ReleaseReequipClaim() => _reequipClaimed = false;

    public override void _Draw()
    {
        if (!GodotObject.IsInstanceValid(ToolProfile))
        {
            base._Draw();
            return;
        }

        Color fill = ToolProfile.VisualColor;
        Color outline = ToolProfile.OutlineColor;
        if (!ToolProfile.IsElongated)
        {
            DrawCircle(Vector2.Zero, ToolProfile.Radius, fill, true, -1.0f, true);
            DrawArc(Vector2.Zero, ToolProfile.Radius, 0.0f, Mathf.Tau, CircleSegments, outline, OutlineWidth, true);
            return;
        }

        float halfShaft = (ToolProfile.Length * 0.5f) - ToolProfile.Radius;
        var shaft = new Rect2(-ToolProfile.Radius, -halfShaft, ToolProfile.Radius * 2.0f, halfShaft * 2.0f);
        var top = new Vector2(0.0f, -halfShaft);
        var bottom = new Vector2(0.0f, halfShaft);
        DrawRect(shaft, fill, true);
        DrawCircle(top, ToolProfile.Radius, fill, true, -1.0f, true);
        DrawCircle(bottom, ToolProfile.Radius, fill, true, -1.0f, true);
        DrawArc(top, ToolProfile.Radius, Mathf.Pi, Mathf.Tau, CircleSegments, outline, OutlineWidth, true);
        DrawArc(bottom, ToolProfile.Radius, 0.0f, Mathf.Pi, CircleSegments, outline, OutlineWidth, true);
        DrawLine(new Vector2(-ToolProfile.Radius, -halfShaft), new Vector2(-ToolProfile.Radius, halfShaft), outline, OutlineWidth, true);
        DrawLine(new Vector2(ToolProfile.Radius, -halfShaft), new Vector2(ToolProfile.Radius, halfShaft), outline, OutlineWidth, true);
    }
}
