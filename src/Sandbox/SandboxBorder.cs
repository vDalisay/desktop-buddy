using System;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// Draws the simple, clearly visible box border of the transparent sandbox
/// (`DECISIONS.md` "Sandbox presentation": the play area reads as a box). It
/// reads the boundary layout and never drives physics. Presentation only.
/// </summary>
[GlobalClass]
public partial class SandboxBorder : Node2D
{
    private static readonly Color BorderColor = new("d0d8e0e0");

    [Export] public BoundaryController Boundaries { get; set; } = null!;
    [Export(PropertyHint.Range, "1,8,0.5")] public float LineWidth { get; set; } = 2.0f;

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Boundaries))
        {
            throw new InvalidOperationException("SandboxBorder requires an injected boundary.");
        }

        Boundaries.LayoutApplied += OnLayoutApplied;
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
        if (!Boundaries.IsInitialized)
        {
            return;
        }

        RoomLayout room = Boundaries.CurrentLayout;
        var box = new Rect2(0.0f, 0.0f, (float)room.RoomWidth, (float)room.RoomHeight);
        DrawRect(box, BorderColor, false, LineWidth, true);
    }

    private void OnLayoutApplied(RoomLayout _, Rect2 __) => QueueRedraw();
}
