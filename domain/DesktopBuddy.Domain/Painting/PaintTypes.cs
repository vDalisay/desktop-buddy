using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Painting;

public enum PaintPart { Head, Torso, LeftHand, RightHand, LeftFoot, RightFoot }
public enum PaintTool { Brush, Spray, Curve, Eraser, Fill, Pen }

public readonly record struct PaintColor(byte R, byte G, byte B)
{
    public static PaintColor White { get; } = new(byte.MaxValue, byte.MaxValue, byte.MaxValue);
}

public readonly record struct PaintPoint(double X, double Y)
{
    public double Length => Math.Sqrt((X * X) + (Y * Y));
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public static PaintPoint operator +(PaintPoint left, PaintPoint right) => new(left.X + right.X, left.Y + right.Y);
    public static PaintPoint operator -(PaintPoint left, PaintPoint right) => new(left.X - right.X, left.Y - right.Y);
    public static PaintPoint operator *(PaintPoint point, double scalar) => new(point.X * scalar, point.Y * scalar);
}

public readonly record struct PaintHit(PaintPart Part, PaintPoint Uv, double Depth, bool IsConnector = false)
{
    public bool IsValid => Uv.IsFinite && double.IsFinite(Depth) &&
        Uv.X >= 0.0 && Uv.X <= 1.0 && Uv.Y >= 0.0 && Uv.Y <= 1.0;
}

/// <summary>Two atlas lanes share each limb's existing 512x512 surface.</summary>
public readonly record struct PaintUvRegion(double Start, double Width)
{
    public static PaintUvRegion Full { get; } = new(0.0, 1.0);
    public static PaintUvRegion LimbEnd { get; } = new(0.0, 0.5);
    public static PaintUvRegion LimbConnector { get; } = new(0.5, 0.5);
    public bool IsValid => Start >= 0.0 && Width > 0.0 && Start + Width <= 1.0;
    public int StartPixel => (int)Math.Round(Start * PaintPolicy.SurfaceSize);
    public int PixelWidth => (int)Math.Round(Width * PaintPolicy.SurfaceSize);

    public static bool IsLimb(PaintPart part) => part is
        PaintPart.LeftHand or PaintPart.RightHand or PaintPart.LeftFoot or PaintPart.RightFoot;

    public static PaintUvRegion For(PaintHit hit) => IsLimb(hit.Part)
        ? hit.IsConnector ? LimbConnector : LimbEnd
        : Full;

    public PaintPoint MapLocal(PaintPoint uv) => new(Start + (Wrap(uv.X) * Width), uv.Y);
    public double LocalU(double atlasU) => (atlasU - Start) / Width;
    public double AtlasU(double localU) => Start + (Wrap(localU) * Width);
    public double PixelX(double atlasU) => StartPixel + (LocalU(atlasU) * (PixelWidth - 1));
    public int WrapPixelX(int x) => StartPixel + (((x - StartPixel) % PixelWidth) + PixelWidth) % PixelWidth;

    private static double Wrap(double value)
    {
        double wrapped = value - Math.Floor(value);
        return wrapped >= 1.0 ? 0.0 : wrapped;
    }
}

public static class PaintPolicy
{
    public const int SurfaceSize = 512;
    public const int BytesPerPixel = 4;
    public const int SurfaceBytes = SurfaceSize * SurfaceSize * BytesPerPixel;
    public const int MinBrushDiameter = 4;
    public const int MaxBrushDiameter = 128;
    public const int DefaultBrushDiameter = 24;
    public const int BrushStep = 4;
    public const long WorkingSurfaceBudgetBytes = 6L * SurfaceBytes;
    public const long UndoBudgetBytes = 48L * 1024 * 1024;
    public const long EditingBudgetBytes = 64L * 1024 * 1024;
    public const int MaxEncodedPartBytes = 2 * 1024 * 1024;
    public const int MaxAggregateEncodedBytes = 12 * 1024 * 1024;
    public const int MaximumEncodedPngBytes = MaxEncodedPartBytes;
    public const int MaximumAggregateEncodedBytes = MaxAggregateEncodedBytes;

    public static IReadOnlyDictionary<PaintPart, string> WhitelistedPaths { get; } =
        new Dictionary<PaintPart, string>
        {
            [PaintPart.Head] = "paint/head.png",
            [PaintPart.Torso] = "paint/torso.png",
            [PaintPart.LeftHand] = "paint/left_hand.png",
            [PaintPart.RightHand] = "paint/right_hand.png",
            [PaintPart.LeftFoot] = "paint/left_foot.png",
            [PaintPart.RightFoot] = "paint/right_foot.png",
        };

    public static bool TryResolvePart(string path, out PaintPart part)
    {
        foreach ((PaintPart candidate, string allowed) in WhitelistedPaths)
        {
            if (string.Equals(path, allowed, StringComparison.Ordinal))
            {
                part = candidate;
                return true;
            }
        }
        part = default;
        return false;
    }

    public static bool IsWhitelistedPath(PaintPart part, string path) =>
        TryResolvePart(path, out PaintPart resolved) && resolved == part;
}

public sealed class PaintViewState
{
    public const double MinimumZoom = 1.0;
    public const double MaximumZoom = 8.0;
    public double Zoom { get; private set; } = MinimumZoom;
    public PaintPoint Pan { get; private set; }

    public void SetZoom(double zoom, PaintPoint focalCanvasPoint)
    {
        if (!double.IsFinite(zoom)) zoom = zoom > 0 ? MaximumZoom : MinimumZoom;
        double previous = Zoom;
        Zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        if (focalCanvasPoint.IsFinite && previous > 0.0)
        {
            double ratio = Zoom / previous;
            Pan = Clamp(new PaintPoint(
                focalCanvasPoint.X - ((focalCanvasPoint.X - Pan.X) * ratio),
                focalCanvasPoint.Y - ((focalCanvasPoint.Y - Pan.Y) * ratio)));
        }
    }

    public void PanBy(PaintPoint delta)
    {
        if (delta.IsFinite) Pan = Clamp(Pan + delta);
    }

    public void Reset()
    {
        Zoom = MinimumZoom;
        Pan = default;
    }

    private PaintPoint Clamp(PaintPoint value)
    {
        double envelope = Math.Max(1.0, Zoom);
        return new PaintPoint(
            Math.Clamp(value.X, -envelope, envelope),
            Math.Clamp(value.Y, -envelope, envelope));
    }
}

public enum PaintShapeKind { Sphere, Capsule }

/// <summary>
/// One trusted part silhouette in 2D world units (pixels, Y-down), ordered nearest-first by
/// <see cref="Depth"/> so overlapping parts resolve like the rendered depth lanes.
/// <see cref="HalfHeight"/> equals <see cref="Radius"/> for a sphere; for a capsule it is half
/// the mesh height including both caps.
/// </summary>
public readonly record struct PaintPartShape(
    PaintPart Part,
    PaintPoint Center,
    double Radius,
    double HalfHeight,
    PaintShapeKind Kind,
    double Depth);

public sealed class FrontalPaintMapper
{
    private readonly PaintPartShape[] _shapes;
    private FrontalPaintMapper(PaintPartShape[] shapes) => _shapes = shapes;

    public IReadOnlyList<PaintPartShape> Shapes => _shapes;

    public static FrontalPaintMapper Create(IEnumerable<PaintPartShape> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        return new(shapes.OrderByDescending(shape => shape.Depth).ToArray());
    }

    /// <summary>
    /// The trusted rest anatomy of `data/buddy/lab_puppet_rig.tres` plus the torso capsule
    /// height from `lab_buddy_visual.tres`, in the same world units the preview camera frames.
    /// The `paint_frontal_uv_mapping` scenario fails if the trusted resources drift from this.
    /// Every part mesh is an unmirrored primitive, so left and right limbs share one UV
    /// convention: screen-symmetric points map to reversed U.
    /// </summary>
    public static FrontalPaintMapper CreateDefault() => Create(new[]
    {
        new PaintPartShape(PaintPart.Head, new PaintPoint(0.0, -50.0), 24.0, 24.0, PaintShapeKind.Sphere, 96.0),
        new PaintPartShape(PaintPart.LeftHand, new PaintPoint(-38.0, -5.0), 15.0, 15.0, PaintShapeKind.Sphere, 48.0),
        new PaintPartShape(PaintPart.RightHand, new PaintPoint(38.0, -5.0), 15.0, 15.0, PaintShapeKind.Sphere, 48.0),
        new PaintPartShape(PaintPart.Torso, new PaintPoint(0.0, 0.0), 28.0, 35.0, PaintShapeKind.Capsule, 0.0),
        new PaintPartShape(PaintPart.LeftFoot, new PaintPoint(-22.0, 55.0), 17.0, 17.0, PaintShapeKind.Sphere, -48.0),
        new PaintPartShape(PaintPart.RightFoot, new PaintPoint(22.0, 55.0), 17.0, 17.0, PaintShapeKind.Sphere, -48.0),
    });

    public bool TryMap(PaintPoint point, out PaintHit hit)
    {
        if (!point.IsFinite) { hit = default; return false; }
        foreach (PaintPartShape shape in _shapes)
        {
            double x = point.X - shape.Center.X;
            double yUp = -(point.Y - shape.Center.Y);
            if (shape.Kind == PaintShapeKind.Capsule
                    ? TryMapCapsule(shape, x, yUp, out PaintPoint uv, out double z)
                    : TryMapSphere(shape, x, yUp, out uv, out z))
            {
                hit = new PaintHit(shape.Part, uv, shape.Depth + z);
                return true;
            }
        }
        hit = default;
        return false;
    }

    private static bool TryMapSphere(PaintPartShape shape, double x, double yUp, out PaintPoint uv, out double z)
    {
        uv = default;
        z = 0.0;
        double radius = shape.Radius;
        double planar = (x * x) + (yUp * yUp);
        if (radius <= 0.0 || planar > radius * radius)
            return false;

        z = Math.Sqrt(Math.Max(0.0, (radius * radius) - planar));
        uv = new PaintPoint(
            Wrap(Math.Atan2(x, z) / Tau),
            Math.Acos(Math.Clamp(yUp / radius, -1.0, 1.0)) / Math.PI);
        return true;
    }

    private static bool TryMapCapsule(PaintPartShape shape, double x, double yUp, out PaintPoint uv, out double z)
    {
        uv = default;
        z = 0.0;
        double radius = shape.Radius;
        double mid = shape.HalfHeight - radius;
        if (radius <= 0.0 || mid < 0.0 || Math.Abs(x) > radius || Math.Abs(yUp) > shape.HalfHeight)
            return false;

        double v;
        if (Math.Abs(yUp) <= mid)
        {
            z = Math.Sqrt(Math.Max(0.0, (radius * radius) - (x * x)));
            double band = mid <= 0.0 ? 0.5 : (mid - yUp) / (2.0 * mid);
            v = OneThird + (band * OneThird);
        }
        else
        {
            double capOffset = Math.Abs(yUp) - mid;
            double planar = (x * x) + (capOffset * capOffset);
            if (planar > radius * radius)
                return false;
            z = Math.Sqrt(Math.Max(0.0, (radius * radius) - planar));
            double capFraction = 2.0 / Math.PI * (yUp > 0.0
                ? Math.Acos(Math.Clamp(capOffset / radius, -1.0, 1.0))
                : Math.Asin(Math.Clamp(capOffset / radius, -1.0, 1.0)));
            v = yUp > 0.0 ? capFraction * OneThird : (2.0 * OneThird) + (capFraction * OneThird);
        }

        uv = new PaintPoint(Wrap(0.5 + (Math.Atan2(x, z) / Tau)), Math.Clamp(v, 0.0, 1.0));
        return true;
    }

    private const double Tau = Math.PI * 2.0;
    private const double OneThird = 1.0 / 3.0;

    private static double Wrap(double u)
    {
        double wrapped = u - Math.Floor(u);
        return wrapped >= 1.0 ? 0.0 : wrapped;
    }
}
