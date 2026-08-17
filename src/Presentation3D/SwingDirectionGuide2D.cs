using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Presentation-only guide for the charged baseball bat. It reads the exact direction sign from
/// <see cref="CursorToolController"/> that the swing state machine will commit, so the helper can
/// never disagree with gameplay. The guide has no input, collision, damage or physics authority.
/// </summary>
public partial class SwingDirectionGuide2D : Node2D
{
    private static readonly Color Shadow = new(0.05f, 0.05f, 0.05f, 0.78f);
    private static readonly Color Fill = new(1.0f, 0.82f, 0.20f, 0.96f);

    private CursorToolController? _controller;
    private int _directionSign = 1;

    public bool IsInitialized { get; private set; }

    public void Initialize(CursorToolController controller)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(controller);
        if (!GodotObject.IsInstanceValid(controller) || !controller.IsInitialized)
            throw new ArgumentException("Swing direction guide requires an initialized cursor-tool controller.", nameof(controller));

        _controller = controller;
        ZIndex = 380;
        Visible = false;
        IsInitialized = true;
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(_controller))
        {
            Visible = false;
            return;
        }

        CursorToolController controller = _controller!;
        bool canGuide =
            controller.IsActive &&
            controller.HasCursor &&
            controller.ActiveContentId == ContentIds.ToolBaseballBat &&
            controller.SwingState is ChargedSwingState.Follow or ChargedSwingState.Gripped or ChargedSwingState.Charging;

        if (!canGuide)
        {
            Visible = false;
            return;
        }

        int nextSign = controller.SwingDirectionSign < 0 ? -1 : 1;
        if (nextSign != _directionSign)
        {
            _directionSign = nextSign;
            QueueRedraw();
        }

        GlobalPosition = controller.Cursor;
        Visible = true;
    }

    public override void _Draw()
    {
        Vector2 direction = Vector2.Right * _directionSign;
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Vector2 start = direction * 24.0f;
        Vector2 tip = direction * 48.0f;
        Vector2 headBase = tip - direction * 11.0f;

        DrawLine(start + Vector2.One * 1.5f, tip + Vector2.One * 1.5f, Shadow, 5.0f, antialiased: true);
        DrawColoredPolygon(new Vector2[]
        {
            tip + Vector2.One * 1.5f,
            headBase + perpendicular * 7.0f + Vector2.One * 1.5f,
            headBase - perpendicular * 7.0f + Vector2.One * 1.5f,
        }, Shadow);

        DrawLine(start, tip, Fill, 3.0f, antialiased: true);
        DrawColoredPolygon(new Vector2[]
        {
            tip,
            headBase + perpendicular * 6.0f,
            headBase - perpendicular * 6.0f,
        }, Fill);
    }
}
