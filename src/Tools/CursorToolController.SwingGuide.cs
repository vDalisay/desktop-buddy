using System.Collections.Generic;
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
    /// <summary>The Win98 active title bar, so a charging bat fills with the shell's own blue.</summary>
    private static readonly Color SwingGuideCharge = new(0.0f, 0.0f, 0.502f, 1.0f);
    private static readonly Color SwingGuideOutline = new(0.0f, 0.0f, 0.0f, 1.0f);
    private static readonly Color SwingGuideFill = new(1.0f, 1.0f, 1.0f, 1.0f);

    /// <summary>Arrow geometry, measured along the aim direction from the cursor.</summary>
    private const float SwingGuideStart = 24.0f;
    private const float SwingGuideTip = 48.0f;
    private const float SwingGuideHead = 11.0f;
    private const float SwingGuideHalfWidth = 3.0f;
    private const float SwingGuideHalfHead = 8.0f;

    /// <summary>Full-charge shake. Fast and tiny: a "this is as far as it goes" tell, not motion.</summary>
    private const float SwingGuideShakePixels = 1.6f;
    private const float SwingGuideShakePrimaryHz = 23.0f;
    private const float SwingGuideShakeSecondaryHz = 31.0f;

    private bool _swingGuideWasVisible;
    private int _swingGuideDirection = 1;
    private float _swingGuideCharge;
    private double _swingGuideShakeSeconds;
    private Vector2 _swingGuideCursor = new(float.NaN, float.NaN);

    private bool SwingGuideFullyCharged => _swingGuideCharge >= 1.0f;

    public override void _Process(double delta)
    {
        bool shouldShow =
            IsInitialized &&
            IsActive &&
            HasCursor &&
            ActiveContentId == ContentIds.ToolBaseballBat &&
            SwingState is ChargedSwingState.Follow or ChargedSwingState.Gripped or ChargedSwingState.Charging;
        int direction = SwingDirectionSign < 0 ? -1 : 1;
        float charge = SwingState == ChargedSwingState.Charging
            ? Mathf.Clamp(SwingCharge, 0.0f, 1.0f)
            : 0.0f;

        bool changed =
            shouldShow != _swingGuideWasVisible ||
            direction != _swingGuideDirection ||
            !Mathf.IsEqualApprox(charge, _swingGuideCharge) ||
            !_swingGuideCursor.IsEqualApprox(Cursor);

        _swingGuideWasVisible = shouldShow;
        _swingGuideDirection = direction;
        _swingGuideCharge = charge;
        _swingGuideCursor = Cursor;

        // The shake is its own animation: it has to keep redrawing while nothing else moves.
        if (shouldShow && SwingGuideFullyCharged)
        {
            _swingGuideShakeSeconds += delta;
            changed = true;
        }
        else
        {
            _swingGuideShakeSeconds = 0.0;
        }

        if (changed)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_swingGuideWasVisible)
            return;

        Vector2 direction = Vector2.Right * _swingGuideDirection;
        Vector2 perpendicular = new(-direction.Y, direction.X);
        // Two unequal frequencies, the same trick the bat's own strain wobble uses: one
        // frequency reads as a mechanical oscillation and visibly loops.
        Vector2 shake = Vector2.Zero;
        if (SwingGuideFullyCharged)
        {
            float time = (float)_swingGuideShakeSeconds;
            shake = new Vector2(
                Mathf.Sin(time * Mathf.Tau * SwingGuideShakePrimaryHz),
                Mathf.Sin(time * Mathf.Tau * SwingGuideShakeSecondaryHz)) * SwingGuideShakePixels;
        }

        Vector2 origin = _swingGuideCursor + shake;
        Vector2 start = origin + direction * SwingGuideStart;
        Vector2 tip = origin + direction * SwingGuideTip;
        Vector2 headBase = tip - direction * SwingGuideHead;

        // One closed outline for the whole arrow, so the black edge wraps the silhouette
        // instead of tracing a rectangle and a triangle that overlap down the middle.
        Vector2[] body =
        [
            start + perpendicular * SwingGuideHalfWidth,
            headBase + perpendicular * SwingGuideHalfWidth,
            headBase + perpendicular * SwingGuideHalfHead,
            tip,
            headBase - perpendicular * SwingGuideHalfHead,
            headBase - perpendicular * SwingGuideHalfWidth,
            start - perpendicular * SwingGuideHalfWidth,
        ];

        DrawColoredPolygon(body, SwingGuideFill);

        // The charge fills along the aim axis, from the flat tail toward the point, by clipping
        // the same silhouette against a moving plane rather than drawing a second arrow: the
        // fill then follows the head's taper for free.
        //
        // The frontier is solved for *area*, not for distance. Advancing it linearly looked full
        // long before the bat was, because the head is a triangle whose area fills fastest at its
        // base — so the arrow read as maxed out a good second before the third charge glint
        // (owner feedback 2026-08-20). Solving on area makes "how full it looks" track the charge,
        // and full now lands on the same tick as the glint.
        if (_swingGuideCharge > 0.0f)
        {
            float frontier = FrontierForAreaFraction(body, origin, direction, _swingGuideCharge);
            Vector2[] filled = ClipToFrontier(body, origin, direction, frontier);
            if (filled.Length >= 3)
                DrawColoredPolygon(filled, SwingGuideCharge);
        }

        DrawPolyline([.. body, body[0]], SwingGuideOutline, 1.5f, antialiased: true);
    }

    /// <summary>
    /// The depth along <paramref name="axis"/> at which the clipped silhouette holds
    /// <paramref name="fraction"/> of the whole polygon's area. Bisection rather than a closed
    /// form: the arrow is a seven-point concave outline, and twenty halvings of a range this
    /// small land well inside a pixel while staying correct if the geometry is ever retuned.
    /// </summary>
    private static float FrontierForAreaFraction(
        Vector2[] polygon,
        Vector2 origin,
        Vector2 axis,
        float fraction)
    {
        float low = SwingGuideStart;
        float high = SwingGuideTip;
        if (fraction >= 1.0f)
            return high;

        float total = Area(polygon);
        if (Mathf.IsZeroApprox(total))
            return high;

        float wanted = total * Mathf.Clamp(fraction, 0.0f, 1.0f);
        for (int step = 0; step < 20; step++)
        {
            float middle = (low + high) * 0.5f;
            if (Area(ClipToFrontier(polygon, origin, axis, middle)) < wanted)
                low = middle;
            else
                high = middle;
        }

        return (low + high) * 0.5f;
    }

    /// <summary>Unsigned shoelace area; the winding of the clipped result is not guaranteed.</summary>
    private static float Area(Vector2[] polygon)
    {
        if (polygon.Length < 3)
            return 0.0f;

        float sum = 0.0f;
        for (int index = 0; index < polygon.Length; index++)
        {
            Vector2 current = polygon[index];
            Vector2 next = polygon[(index + 1) % polygon.Length];
            sum += (current.X * next.Y) - (next.X * current.Y);
        }

        return Mathf.Abs(sum) * 0.5f;
    }

    /// <summary>
    /// Sutherland–Hodgman against the single half-plane "distance along <paramref name="axis"/>
    /// from <paramref name="origin"/> is at most <paramref name="frontier"/>". Convexity is not
    /// required for one clip plane, which is why the concave arrow silhouette survives it.
    /// </summary>
    private static Vector2[] ClipToFrontier(
        Vector2[] polygon,
        Vector2 origin,
        Vector2 axis,
        float frontier)
    {
        var clipped = new List<Vector2>(polygon.Length + 2);
        for (int index = 0; index < polygon.Length; index++)
        {
            Vector2 current = polygon[index];
            Vector2 next = polygon[(index + 1) % polygon.Length];
            float currentDepth = (current - origin).Dot(axis);
            float nextDepth = (next - origin).Dot(axis);
            bool currentInside = currentDepth <= frontier;
            bool nextInside = nextDepth <= frontier;

            if (currentInside)
                clipped.Add(current);

            if (currentInside != nextInside &&
                !Mathf.IsZeroApprox(nextDepth - currentDepth))
            {
                float t = (frontier - currentDepth) / (nextDepth - currentDepth);
                clipped.Add(current.Lerp(next, t));
            }
        }

        return [.. clipped];
    }
}
