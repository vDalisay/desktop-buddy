using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Painting;

public enum PaintPart { Head, Torso, LeftHand, RightHand, LeftFoot, RightFoot }
public enum PaintTool { Brush, Eraser }

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

public readonly record struct PaintHit(PaintPart Part, PaintPoint Uv, double Depth)
{
    public bool IsValid => Uv.IsFinite && double.IsFinite(Depth) &&
        Uv.X >= 0.0 && Uv.X <= 1.0 && Uv.Y >= 0.0 && Uv.Y <= 1.0;
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

public sealed class FrontalPaintMapper
{
    private readonly Primitive[] _primitives;
    private FrontalPaintMapper(Primitive[] primitives) => _primitives = primitives;

    public static FrontalPaintMapper CreateDefault() => new(new[]
    {
        new Primitive(PaintPart.Head, new PaintPoint(0.0, -1.42), 0.48, 0.48, false, 0.0),
        new Primitive(PaintPart.Torso, new PaintPoint(0.0, -0.15), 0.62, 0.78, false, 0.1),
        new Primitive(PaintPart.LeftHand, new PaintPoint(-1.02, -0.12), 0.34, 0.34, true, 0.2),
        new Primitive(PaintPart.RightHand, new PaintPoint(1.02, -0.12), 0.34, 0.34, false, 0.2),
        new Primitive(PaintPart.LeftFoot, new PaintPoint(-0.43, 1.08), 0.38, 0.30, true, 0.3),
        new Primitive(PaintPart.RightFoot, new PaintPoint(0.43, 1.08), 0.38, 0.30, false, 0.3),
    });

    public bool TryMap(PaintPoint point, out PaintHit hit)
    {
        if (!point.IsFinite) { hit = default; return false; }
        foreach (Primitive primitive in _primitives)
        {
            double localX = (point.X - primitive.Center.X) / primitive.RadiusX;
            double localY = (point.Y - primitive.Center.Y) / primitive.RadiusY;
            double radiusSquared = (localX * localX) + (localY * localY);
            if (radiusSquared > 1.0) continue;
            double u = 0.5 + (Math.Asin(Math.Clamp(localX, -1.0, 1.0)) / Math.PI);
            if (primitive.MirrorU) u = 1.0 - u;
            double v = 0.5 + (Math.Asin(Math.Clamp(localY, -1.0, 1.0)) / Math.PI);
            double depth = primitive.Depth - Math.Sqrt(Math.Max(0.0, 1.0 - radiusSquared));
            hit = new PaintHit(primitive.Part, new PaintPoint(u, v), depth);
            return true;
        }
        hit = default;
        return false;
    }

    private readonly record struct Primitive(
        PaintPart Part, PaintPoint Center, double RadiusX, double RadiusY, bool MirrorU, double Depth);
}
