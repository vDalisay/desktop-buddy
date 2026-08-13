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

    public static void DrawBrushRing(CanvasItem canvas, Vector2 pointer, Vector2 screenDiameter, float rotation)
    {
        const int segments = 40;
        var points = new Vector2[segments + 1];
        Vector2 radii = new(Mathf.Max(1f, screenDiameter.X / 2f), Mathf.Max(1f, screenDiameter.Y / 2f));
        for (int index = 0; index <= segments; index++)
        {
            float angle = Mathf.Tau * index / segments;
            points[index] = pointer + new Vector2(Mathf.Cos(angle) * radii.X, Mathf.Sin(angle) * radii.Y).Rotated(rotation);
        }
        canvas.DrawPolyline(points, Colors.White, RingWidth, antialiased: true);
    }

    public static void DrawBrushRing(CanvasItem canvas, Vector2[] points) =>
        canvas.DrawPolyline(points, Colors.White, RingWidth, antialiased: true);

    public static void DrawPickPreview(CanvasItem canvas, Vector2 pointer, Color picked)
    {
        var block = new Rect2(pointer + PickBlockOffset, PickBlockSize);
        canvas.DrawRect(block, picked);
        canvas.DrawRect(block, Colors.Black, filled: false, width: 1f);
    }
}
