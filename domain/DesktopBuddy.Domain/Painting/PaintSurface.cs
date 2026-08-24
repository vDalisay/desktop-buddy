using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DesktopBuddy.Domain.Painting;

public readonly record struct PaintRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public int ByteCount => checked(Width * Height * PaintPolicy.BytesPerPixel);

    public static PaintRect Union(PaintRect left, PaintRect right)
    {
        if (left.IsEmpty) return right;
        if (right.IsEmpty) return left;
        int x = Math.Min(left.X, right.X);
        int y = Math.Min(left.Y, right.Y);
        int rightEdge = Math.Max(left.X + left.Width, right.X + right.Width);
        int bottom = Math.Max(left.Y + left.Height, right.Y + right.Height);
        return new PaintRect(x, y, rightEdge - x, bottom - y);
    }
}

public sealed class PaintSurface
{
    private readonly byte[] _pixels = new byte[PaintPolicy.SurfaceBytes];
    public long Revision { get; private set; }
    public ReadOnlyMemory<byte> Pixels => _pixels;

    /// <summary>
    /// Spacing between stamps along a stroke, as a fraction of the brush diameter. Work per
    /// unit of cursor travel is proportional to diameter/spacing, so this is the one constant
    /// that decides how much a big brush costs. At 0.08 consecutive stamps overlapped by 92%
    /// and a single 400px max-brush mirrored stroke cost 12.9ms of stamping, which is the
    /// big-brush paint lag the owner reported (2026-08-19). At 0.25 the same stroke costs
    /// 4.7ms and consecutive stamps still overlap by 75%, so an opaque round brush leaves a
    /// solid stroke with no scalloping. Covered by PaintStrokeContinuityTests.
    /// </summary>
    public const double StampSpacingFactor = 0.25;

    public static PaintRect StampBounds(
        PaintPoint uv,
        int diameter,
        double verticalScale = 1.0,
        PaintUvRegion region = default)
    {
        region = ValidRegion(region);
        diameter = Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, PaintPolicy.MaxBrushDiameter);
        verticalScale = ValidVerticalScale(verticalScale);

        double centerX = region.PixelX(uv.X);
        double centerY = uv.Y * (PaintPolicy.SurfaceSize - 1);
        double radiusX = diameter / 2.0;
        double radiusY = radiusX * verticalScale;
        int minX = (int)Math.Floor(centerX - radiusX);
        int maxX = (int)Math.Ceiling(centerX + radiusX);
        int minY = Math.Clamp((int)Math.Floor(centerY - radiusY), 0, PaintPolicy.SurfaceSize - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(centerY + radiusY), 0, PaintPolicy.SurfaceSize - 1);
        int regionEnd = region.StartPixel + region.PixelWidth - 1;
        return minX < region.StartPixel || maxX > regionEnd
            ? new PaintRect(region.StartPixel, minY, region.PixelWidth, (maxY - minY) + 1)
            : new PaintRect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
    }

    public static PaintRect StrokeBounds(
        PaintPoint from,
        PaintPoint to,
        int diameter,
        double verticalScale = 1.0,
        PaintUvRegion region = default)
    {
        region = ValidRegion(region);
        to = NormalizeStrokeTarget(from, to, region);
        PaintPoint delta = new((to.X - from.X) / region.Width, to.Y - from.Y);
        double distancePixels = delta.Length * PaintPolicy.SurfaceSize;
        double spacing = Math.Max(0.5, diameter * StampSpacingFactor);
        int steps = Math.Max(1, (int)Math.Ceiling(distancePixels / spacing));
        PaintRect dirty = default;
        for (int step = 0; step <= steps; step++)
        {
            double t = step / (double)steps;
            PaintPoint point = from + ((to - from) * t);
            dirty = PaintRect.Union(dirty, StampBounds(point, diameter, verticalScale, region));
        }
        return dirty;
    }

    /// <summary>
    /// Writes one texel. This is what a spray dot is: Paint Room dusts single pixels, and the
    /// buddy's spray stamped a whole minimum-diameter dab per dot instead, which came out as a
    /// spatter of blobs rather than an airbrush (owner report 2026-08-23). The caller owns the
    /// scatter and the undo bounds; this only puts the colour down.
    /// </summary>
    public bool Dot(PaintPoint uv, PaintColor color, PaintUvRegion region = default)
    {
        region = ValidRegion(region);
        int y = (int)Math.Round(uv.Y * (PaintPolicy.SurfaceSize - 1));
        if (y < 0 || y >= PaintPolicy.SurfaceSize)
            return false;

        int x = region.WrapPixelX((int)Math.Round(region.PixelX(uv.X)));
        int index = ((y * PaintPolicy.SurfaceSize) + x) * PaintPolicy.BytesPerPixel;
        if (!Write(index, color.R, color.G, color.B, byte.MaxValue))
            return false;

        Revision++;
        return true;
    }

    public PaintRect Stamp(
        PaintPoint uv,
        int diameter,
        PaintTool tool,
        PaintColor color,
        double verticalScale = 1.0,
        PaintUvRegion region = default)
    {
        region = ValidRegion(region);
        diameter = Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, PaintPolicy.MaxBrushDiameter);
        verticalScale = ValidVerticalScale(verticalScale);

        double centerX = region.PixelX(uv.X);
        double centerY = uv.Y * (PaintPolicy.SurfaceSize - 1);
        double radiusX = diameter / 2.0;
        double radiusY = radiusX * verticalScale;
        double inverseRadiusX = 1.0 / radiusX;
        double inverseRadiusY = 1.0 / radiusY;
        int minX = (int)Math.Floor(centerX - radiusX);
        int maxX = (int)Math.Ceiling(centerX + radiusX);
        int minY = Math.Clamp((int)Math.Floor(centerY - radiusY), 0, PaintPolicy.SurfaceSize - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(centerY + radiusY), 0, PaintPolicy.SurfaceSize - 1);
        int regionStart = region.StartPixel;
        int regionEnd = regionStart + region.PixelWidth - 1;
        bool wrapsRegion = minX < regionStart || maxX > regionEnd;
        bool changed = false;

        // The eraser is a square block, the way a Win98 eraser is; every other tool lays down
        // a round footprint (owner instruction 2026-08-19). Resolve the mutation colour once per
        // dab rather than once per pixel: Pen/Eraser screen dabs can invoke this hundreds of
        // times per visible nib, so tiny inner-loop costs multiply quickly.
        bool square = tool == PaintTool.Eraser;
        byte r = square ? (byte)0 : color.R;
        byte g = square ? (byte)0 : color.G;
        byte b = square ? (byte)0 : color.B;
        byte a = square ? (byte)0 : byte.MaxValue;
        for (int y = minY; y <= maxY; y++)
        {
            double dy = ((y + 0.5) - centerY) * inverseRadiusY;
            double dySquared = dy * dy;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = ((x + 0.5) - centerX) * inverseRadiusX;
                if (square ? Math.Abs(dx) > 1.0 || Math.Abs(dy) > 1.0 : (dx * dx) + dySquared > 1.0)
                    continue;

                // Most dabs do not cross a UV seam. Avoid two modulo operations per painted
                // pixel on that common path; only wrap the few samples that actually leave the
                // hit's atlas lane.
                int wrappedX = x >= regionStart && x <= regionEnd ? x : region.WrapPixelX(x);
                int index = ((y * PaintPolicy.SurfaceSize) + wrappedX) * PaintPolicy.BytesPerPixel;
                changed |= Write(index, r, g, b, a);
            }
        }

        if (!changed) return default;
        Revision++;
        // These are the same bounds StampBounds would calculate. Reuse the values already paid
        // for above instead of repeating all radius/region arithmetic for every micro-dab.
        return wrapsRegion
            ? new PaintRect(regionStart, minY, region.PixelWidth, (maxY - minY) + 1)
            : new PaintRect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
    }

    /// <summary>Sparse selected-colour dots in a circular envelope. U wraps; V clips.</summary>
    public PaintRect Spray(
        PaintPoint uv,
        int diameter,
        PaintColor color,
        ulong seed,
        double verticalScale = 1.0,
        PaintUvRegion region = default)
    {
        region = ValidRegion(region);
        diameter = Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, PaintPolicy.MaxBrushDiameter);
        verticalScale = ValidVerticalScale(verticalScale);
        double centerX = region.PixelX(uv.X);
        double centerY = uv.Y * (PaintPolicy.SurfaceSize - 1);
        double radius = diameter / 2.0;
        PaintPoint[] offsets = SprayPattern.SampleUnitDisk(seed, SprayPattern.PointCountForDiameter(diameter));
        bool changed = false;

        foreach (PaintPoint offset in offsets)
        {
            int x = (int)Math.Round(centerX + (offset.X * radius));
            int y = (int)Math.Round(centerY + (offset.Y * radius * verticalScale));
            if (y < 0 || y >= PaintPolicy.SurfaceSize)
                continue;
            int wrappedX = region.WrapPixelX(x);
            int index = ((y * PaintPolicy.SurfaceSize) + wrappedX) * PaintPolicy.BytesPerPixel;
            changed |= Write(index, color.R, color.G, color.B, byte.MaxValue);
        }

        if (!changed) return default;
        Revision++;
        return StampBounds(uv, diameter, verticalScale, region);
    }

    public PaintRect Stroke(
        PaintPoint from,
        PaintPoint to,
        int diameter,
        PaintTool tool,
        PaintColor color,
        double verticalScale = 1.0,
        PaintUvRegion region = default)
    {
        region = ValidRegion(region);
        to = NormalizeStrokeTarget(from, to, region);
        PaintPoint delta = new((to.X - from.X) / region.Width, to.Y - from.Y);
        double distancePixels = delta.Length * PaintPolicy.SurfaceSize;
        double spacing = Math.Max(0.5, diameter * StampSpacingFactor);
        int steps = Math.Max(1, (int)Math.Ceiling(distancePixels / spacing));
        PaintRect dirty = default;
        for (int step = 0; step <= steps; step++)
        {
            double t = step / (double)steps;
            PaintPoint point = from + ((to - from) * t);
            dirty = PaintRect.Union(dirty, Stamp(point, diameter, tool, color, verticalScale, region));
        }
        return dirty;
    }

    public bool TrySample(PaintPoint uv, out PaintColor color)
    {
        if (!uv.IsFinite || uv.X < 0.0 || uv.X > 1.0 || uv.Y < 0.0 || uv.Y > 1.0)
        {
            color = default;
            return false;
        }

        int x = Math.Clamp((int)Math.Round(uv.X * (PaintPolicy.SurfaceSize - 1)), 0, PaintPolicy.SurfaceSize - 1);
        int y = Math.Clamp((int)Math.Round(uv.Y * (PaintPolicy.SurfaceSize - 1)), 0, PaintPolicy.SurfaceSize - 1);
        int index = ((y * PaintPolicy.SurfaceSize) + x) * PaintPolicy.BytesPerPixel;
        if (_pixels[index + 3] == 0)
        {
            color = default;
            return false;
        }

        color = new PaintColor(_pixels[index], _pixels[index + 1], _pixels[index + 2]);
        return true;
    }

    public byte[] Capture(PaintRect rectangle)
    {
        if (rectangle.IsEmpty) return Array.Empty<byte>();
        byte[] result = new byte[rectangle.ByteCount];
        int target = 0;
        for (int row = 0; row < rectangle.Height; row++)
        {
            int source = (((rectangle.Y + row) * PaintPolicy.SurfaceSize) + rectangle.X) * PaintPolicy.BytesPerPixel;
            int length = rectangle.Width * PaintPolicy.BytesPerPixel;
            Buffer.BlockCopy(_pixels, source, result, target, length);
            target += length;
        }
        return result;
    }

    public void Restore(PaintRect rectangle, ReadOnlySpan<byte> bytes)
    {
        if (rectangle.ByteCount != bytes.Length)
            throw new ArgumentException("Paint restore data does not match its rectangle.", nameof(bytes));
        int source = 0;
        for (int row = 0; row < rectangle.Height; row++)
        {
            int target = (((rectangle.Y + row) * PaintPolicy.SurfaceSize) + rectangle.X) * PaintPolicy.BytesPerPixel;
            bytes.Slice(source, rectangle.Width * PaintPolicy.BytesPerPixel).CopyTo(_pixels.AsSpan(target));
            source += rectangle.Width * PaintPolicy.BytesPerPixel;
        }
        Revision++;
    }

    public byte[] ClonePixels() => (byte[])_pixels.Clone();

    /// <summary>Copies the whole surface into a caller-owned buffer, so a per-frame uploader
    /// does not have to allocate a megabyte to hand the same bytes to the renderer.</summary>
    public void CopyPixelsTo(Span<byte> destination)
    {
        if (destination.Length != PaintPolicy.SurfaceBytes)
            throw new ArgumentException("Paint surface copies must be exactly 512x512 RGBA8.", nameof(destination));
        _pixels.AsSpan().CopyTo(destination);
    }

    public void Replace(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != PaintPolicy.SurfaceBytes)
            throw new ArgumentException("Paint surface must be exactly 512x512 RGBA8.", nameof(pixels));
        pixels.CopyTo(_pixels);
        Revision++;
    }

    public void Clear()
    {
        if (Array.TrueForAll(_pixels, value => value == 0)) return;
        Array.Clear(_pixels);
        Revision++;
    }

    public string ComputeHash() => Convert.ToHexString(SHA256.HashData(_pixels));

    private bool Write(int index, byte r, byte g, byte b, byte a)
    {
        if (_pixels[index] == r && _pixels[index + 1] == g && _pixels[index + 2] == b && _pixels[index + 3] == a)
            return false;
        _pixels[index] = r;
        _pixels[index + 1] = g;
        _pixels[index + 2] = b;
        _pixels[index + 3] = a;
        return true;
    }

    private static double ValidVerticalScale(double value) =>
        double.IsFinite(value) && value > 0.0 ? Math.Clamp(value, 0.25, 8.0) : 1.0;

    private static PaintUvRegion ValidRegion(PaintUvRegion region) => region.IsValid ? region : PaintUvRegion.Full;

    private static PaintPoint NormalizeStrokeTarget(PaintPoint from, PaintPoint to, PaintUvRegion region)
    {
        if (Math.Abs(to.X - from.X) > region.Width * 0.5)
            return new PaintPoint(to.X + (to.X < from.X ? region.Width : -region.Width), to.Y);
        return to;
    }
}
