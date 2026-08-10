using Godot;

namespace DesktopBuddy.Painting;

/// <summary>Cursor decorations shared by the buddy and room-background painters.</summary>
public static class PaintCursorGizmos
{
    public const float RingWidth = 1.5f;
    private static readonly Vector2 PickBlockSize = new(18, 18);
    private static readonly Vector2 PickBlockOffset = new(16, -26);

    public static void DrawBrushRing(CanvasItem canvas, Vector2 pointer, float screenDiameter) =>
        canvas.DrawArc(pointer, Mathf.Max(2f, screenDiameter / 2f), 0, Mathf.Tau, 32, Colors.White, RingWidth);

    public static void DrawPickPreview(CanvasItem canvas, Vector2 pointer, Color picked)
    {
        var block = new Rect2(pointer + PickBlockOffset, PickBlockSize);
        canvas.DrawRect(block, picked);
        canvas.DrawRect(block, Colors.Black, filled: false, width: 1f);
    }
}
