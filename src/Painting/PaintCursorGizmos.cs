using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Painting;

/// <summary>The outline a paint tool's cursor draws.</summary>
public enum PaintCursorShape
{
    /// <summary>Matches a brush footprint that is wider than it is tall.</summary>
    Ellipse,

    /// <summary>A true circle, for tools whose footprint is not vertically squashed.</summary>
    Circle,

    /// <summary>A square, for the eraser.</summary>
    Square,
}

/// <summary>
/// Cursor decorations shared by the buddy and room-background painters.
///
/// <para>The two painters keep their own tool enums, but the cursor rule is one rule and both
/// <c>ShapeFor</c> overloads live here so it stays that way: eraser is square, everything whose
/// footprint is a true circle draws a circle, and only the vertically-squashed brush draws an
/// ellipse. Owner instruction 2026-08-19: Paint Buddy and Paint Room controls stay unified
/// unless the owner says otherwise, so a change to one overload wants the same change to the
/// other.</para>
/// </summary>
public static class PaintCursorGizmos
{
    public const float RingWidth = 1.5f;
    private static readonly Vector2 PickBlockSize = new(18, 18);
    private static readonly Vector2 PickBlockOffset = new(16, -26);

    public static PaintCursorShape ShapeFor(PaintTool tool) => tool switch
    {
        PaintTool.Eraser => PaintCursorShape.Square,
        // Pen, Spray, Fill and Curve all put down a round footprint, so they all get the same
        // round outline the pen always had.
        PaintTool.Pen or PaintTool.Spray or PaintTool.Fill or PaintTool.Curve => PaintCursorShape.Circle,
        _ => PaintCursorShape.Ellipse,
    };

    public static PaintCursorShape ShapeFor(EnvironmentPaintTool tool) => tool switch
    {
        EnvironmentPaintTool.Eraser => PaintCursorShape.Square,
        EnvironmentPaintTool.Brush => PaintCursorShape.Ellipse,
        _ => PaintCursorShape.Circle,
    };

    /// <summary>
    /// Draws the cursor outline for <paramref name="shape"/>. <paramref name="footprint"/> is the
    /// full on-screen width and height of the tool's footprint; the circle and square use its
    /// width so they stay regular however the footprint is squashed.
    /// </summary>
    public static void DrawBrushCursor(
        CanvasItem canvas,
        Vector2 pointer,
        Vector2 footprint,
        PaintCursorShape shape)
    {
        switch (shape)
        {
            case PaintCursorShape.Square:
                float side = Mathf.Max(2f, footprint.X);
                canvas.DrawRect(
                    new Rect2(pointer - (new Vector2(side, side) * 0.5f), side, side),
                    Colors.White,
                    filled: false,
                    width: RingWidth);
                return;
            case PaintCursorShape.Circle:
                DrawBrushRing(canvas, pointer, new Vector2(footprint.X, footprint.X), 0f);
                return;
            default:
                DrawBrushRing(canvas, pointer, footprint, 0f);
                return;
        }
    }

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
