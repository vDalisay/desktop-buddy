using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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

    public static PaintRect StampBounds(PaintPoint uv, int diameter)
    {
        diameter = Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, PaintPolicy.MaxBrushDiameter);
        double centerX = uv.X * (PaintPolicy.SurfaceSize - 1);
        double centerY = uv.Y * (PaintPolicy.SurfaceSize - 1);
        double radius = diameter / 2.0;
        int minX = (int)Math.Floor(centerX - radius);
        int maxX = (int)Math.Ceiling(centerX + radius);
        int minY = Math.Clamp((int)Math.Floor(centerY - radius), 0, PaintPolicy.SurfaceSize - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(centerY + radius), 0, PaintPolicy.SurfaceSize - 1);
        return minX < 0 || maxX > PaintPolicy.SurfaceSize - 1
            ? new PaintRect(0, minY, PaintPolicy.SurfaceSize, (maxY - minY) + 1)
            : new PaintRect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
    }

    public static PaintRect StrokeBounds(PaintPoint from, PaintPoint to, int diameter)
    {
        to = NormalizeStrokeTarget(from, to);
        double distancePixels = (to - from).Length * PaintPolicy.SurfaceSize;
        double spacing = Math.Max(1.0, diameter * 0.22);
        int steps = Math.Max(1, (int)Math.Ceiling(distancePixels / spacing));
        PaintRect dirty = default;
        for (int step = 0; step <= steps; step++)
        {
            double t = step / (double)steps;
            PaintPoint point = from + ((to - from) * t);
            dirty = PaintRect.Union(dirty, StampBounds(point, diameter));
        }
        return dirty;
    }

    public PaintRect Stamp(
        PaintPoint uv,
        int diameter,
        PaintTool tool,
        PaintColor color)
    {
        diameter = Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, PaintPolicy.MaxBrushDiameter);
        double centerX = uv.X * (PaintPolicy.SurfaceSize - 1);
        double centerY = uv.Y * (PaintPolicy.SurfaceSize - 1);
        double radius = diameter / 2.0;
        // Horizontal bounds stay unclamped so a brush on the seam wraps to the far edge;
        // vertical bounds clamp, because the poles are not cyclic.
        int minX = (int)Math.Floor(centerX - radius);
        int maxX = (int)Math.Ceiling(centerX + radius);
        int minY = Math.Clamp((int)Math.Floor(centerY - radius), 0, PaintPolicy.SurfaceSize - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(centerY + radius), 0, PaintPolicy.SurfaceSize - 1);
        double radiusSquared = radius * radius;
        bool changed = false;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double dx = (x + 0.5) - centerX;
                double dy = (y + 0.5) - centerY;
                if ((dx * dx) + (dy * dy) > radiusSquared)
                    continue;

                // U wraps around the mesh, so a brush over the seam continues on the far edge
                // instead of being clipped.
                int wrappedX = ((x % PaintPolicy.SurfaceSize) + PaintPolicy.SurfaceSize) % PaintPolicy.SurfaceSize;
                int index = ((y * PaintPolicy.SurfaceSize) + wrappedX) * PaintPolicy.BytesPerPixel;
                byte r = tool == PaintTool.Eraser ? (byte)0 : color.R;
                byte g = tool == PaintTool.Eraser ? (byte)0 : color.G;
                byte b = tool == PaintTool.Eraser ? (byte)0 : color.B;
                byte a = tool == PaintTool.Eraser ? (byte)0 : byte.MaxValue;
                if (_pixels[index] == r && _pixels[index + 1] == g &&
                    _pixels[index + 2] == b && _pixels[index + 3] == a)
                {
                    continue;
                }
                _pixels[index] = r;
                _pixels[index + 1] = g;
                _pixels[index + 2] = b;
                _pixels[index + 3] = a;
                changed = true;
            }
        }

        if (!changed)
            return default;
        Revision++;
        return StampBounds(uv, diameter);
    }

    public PaintRect Stroke(
        PaintPoint from,
        PaintPoint to,
        int diameter,
        PaintTool tool,
        PaintColor color)
    {
        // U is cyclic: a stroke across the seam takes the short way round rather than dragging
        // paint the long way across the whole texture.
        to = NormalizeStrokeTarget(from, to);

        double distancePixels = (to - from).Length * PaintPolicy.SurfaceSize;
        double spacing = Math.Max(1.0, diameter * 0.22);
        int steps = Math.Max(1, (int)Math.Ceiling(distancePixels / spacing));
        PaintRect dirty = default;
        for (int step = 0; step <= steps; step++)
        {
            double t = step / (double)steps;
            PaintPoint point = from + ((to - from) * t);
            dirty = PaintRect.Union(dirty, Stamp(point, diameter, tool, color));
        }
        return dirty;
    }

    public byte[] Capture(PaintRect rectangle)
    {
        if (rectangle.IsEmpty)
            return Array.Empty<byte>();
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

    public void Replace(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != PaintPolicy.SurfaceBytes)
            throw new ArgumentException("Paint surface must be exactly 512x512 RGBA8.", nameof(pixels));
        pixels.CopyTo(_pixels);
        Revision++;
    }

    public void Clear()
    {
        if (Array.TrueForAll(_pixels, value => value == 0))
            return;
        Array.Clear(_pixels);
        Revision++;
    }

    public string ComputeHash() => Convert.ToHexString(SHA256.HashData(_pixels));

    private static PaintPoint NormalizeStrokeTarget(PaintPoint from, PaintPoint to)
    {
        if (Math.Abs(to.X - from.X) > 0.5)
            return new PaintPoint(to.X + (to.X < from.X ? 1.0 : -1.0), to.Y);
        return to;
    }
}
