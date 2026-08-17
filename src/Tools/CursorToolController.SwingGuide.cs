using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Capture-polish presentation for the charged baseball bat. Kept in a partial file so the
/// controller's physics/input authority remains unchanged: this file only observes its public/live
/// state and draws a non-interactive guide on the controller's existing 2D canvas.
/// </summary>
public partial class CursorToolController
{
    private static readonly Color SwingGuideShadow = new(0.05f, 0.05f, 0.05f, 0.78f);
    private static readonly Color SwingGuideFill = new(1.0f, 0.82f, 0.20f, 0.96f);
    private bool _swingGuideWasVisible;
    private int _swingGuideDirection = 1;
    private Vector2 _swingGuideCursor = new(float.NaN, float.NaN);

    public override void _Process(double delta)
    {
        bool shouldShow =
            IsInitialized &&
            IsActive &&
            HasCursor &&
            ActiveContentId == ContentIds.ToolBaseballBat &&
            SwingState is ChargedSwingState.Follow or ChargedSwingState.Gripped or ChargedSwingState.Charging;
        int direction = SwingDirectionSign < 0 ? -1 : 1;

        if (shouldShow != _swingGuideWasVisible ||
            direction != _swingGuideDirection ||
            !_swingGuideCursor.IsEqualApprox(Cursor))
        {
            _swingGuideWasVisible = shouldShow;
            _swingGuideDirection = direction;
            _swingGuideCursor = Cursor;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!_swingGuideWasVisible)
            return;

        Vector2 direction = Vector2.Right * _swingGuideDirection;
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Vector2 start = _swingGuideCursor + direction * 24.0f;
        Vector2 tip = _swingGuideCursor + direction * 48.0f;
        Vector2 headBase = tip - direction * 11.0f;
        Vector2 shadowOffset = Vector2.One * 1.5f;

        DrawLine(start + shadowOffset, tip + shadowOffset, SwingGuideShadow, 5.0f, antialiased: true);
        DrawColoredPolygon(new Vector2[]
        {
            tip + shadowOffset,
            headBase + perpendicular * 7.0f + shadowOffset,
            headBase - perpendicular * 7.0f + shadowOffset,
        }, SwingGuideShadow);

        DrawLine(start, tip, SwingGuideFill, 3.0f, antialiased: true);
        DrawColoredPolygon(new Vector2[]
        {
            tip,
            headBase + perpendicular * 6.0f,
            headBase - perpendicular * 6.0f,
        }, SwingGuideFill);
    }
}
