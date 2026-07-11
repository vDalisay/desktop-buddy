using System;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>Development-only drawing for the physical room and its inner edge.</summary>
[GlobalClass]
public partial class LaboratoryBoundaryVisualizer : Node2D
{
    private static readonly Color OuterColor = new("64d8ff");
    private static readonly Color InnerColor = new("64d8ff80");

    [Export] public BoundaryController Boundaries { get; set; } = null!;

    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Boundaries) || !Boundaries.IsInitialized)
        {
            throw new InvalidOperationException(
                "LaboratoryBoundaryVisualizer requires initialized boundaries.");
        }

        Boundaries.LayoutApplied += OnLayoutApplied;
        IsInitialized = true;
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Boundaries))
        {
            Boundaries.LayoutApplied -= OnLayoutApplied;
        }
    }

    public override void _Draw()
    {
        if (!IsInitialized)
        {
            return;
        }

        RoomLayout room = Boundaries.CurrentLayout;
        var outer = new Rect2(0.0f, 0.0f, (float)room.RoomWidth, (float)room.RoomHeight);
        DrawRect(outer, OuterColor, false, 3.0f, true);
        DrawRect(Boundaries.InnerBounds, InnerColor, false, 1.0f, true);
    }

    private void OnLayoutApplied(RoomLayout _, Rect2 __) => QueueRedraw();
}
