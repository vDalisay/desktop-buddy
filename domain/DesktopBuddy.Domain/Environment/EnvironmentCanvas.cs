using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Environment;

public enum EnvironmentPaintTool { Brush, Eraser, Fill, PickColor, Square, Circle, Line }

public static class EnvironmentCanvasPolicy
{
    public const int Size = 512;
    public const int BytesPerPixel = 4;
    public const int Bytes = Size * Size * BytesPerPixel;
    public const int MinBrushDiameter = 2;
    public const int MaxBrushDiameter = 96;
    public const int DefaultBrushDiameter = 16;

    /// <summary>Whole-surface undo snapshots at 1 MiB each; twelve keeps the editor near 12 MiB.</summary>
    // ponytail: whole-surface snapshots, move to dirty-rectangle patches if the depth has to grow.
    public const int UndoDepth = 12;
    public const string RelativePath = "environment/background.png";
    public static EnvironmentColor DefaultColor => new(192, 192, 192);
}

/// <summary>
/// The room background as a painted 512x512 RGBA8 image in canonical 0..1 room space. Unlike the
/// character paint surface this canvas clamps at its edges instead of wrapping U, and it owns the
/// fill/shape rasterisation the character painter has no use for, so the two stay separate.
/// </summary>
public sealed class EnvironmentCanvas
{
    private readonly byte[] _pixels = new byte[EnvironmentCanvasPolicy.Bytes];
    private readonly LinkedList<byte[]> _undo = new();
    private byte[]? _strokeBase;
    private int _originX;
    private int _originY;
    private int _lastX = -1;
    private int _lastY = -1;
    private bool _stroking;

    public EnvironmentCanvas() => FillAll(EnvironmentCanvasPolicy.DefaultColor);

    public long Revision { get; private set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public ReadOnlyMemory<byte> Pixels => _pixels;
    public EnvironmentPaintTool Tool { get; set; } = EnvironmentPaintTool.Brush;
    public EnvironmentColor Color { get; set; } = new(0, 0, 0);

    private int _brushDiameter = EnvironmentCanvasPolicy.DefaultBrushDiameter;
    public int BrushDiameter
    {
        get => _brushDiameter;
        set => _brushDiameter = Math.Clamp(
            value, EnvironmentCanvasPolicy.MinBrushDiameter, EnvironmentCanvasPolicy.MaxBrushDiameter);
    }

    /// <summary>Pointer press. Canonical coordinates outside 0..1 are ignored.</summary>
    public void Begin(double x, double y)
    {
        if (!TryPixel(x, y, out int px, out int py)) return;
        byte[] snapshot = (byte[])_pixels.Clone();
        _stroking = true;
        _originX = px;
        _originY = py;
        _lastX = px;
        _lastY = py;
        switch (Tool)
        {
            case EnvironmentPaintTool.Brush: Stamp(px, py, Color); break;
            case EnvironmentPaintTool.Eraser: Stamp(px, py, EnvironmentCanvasPolicy.DefaultColor); break;
            case EnvironmentPaintTool.Fill: Fill(px, py, Color); break;
            case EnvironmentPaintTool.PickColor:
                if (TrySample(px, py, out EnvironmentColor picked)) Color = picked;
                break;
            default: _strokeBase = (byte[])_pixels.Clone(); break;
        }
        PushUndo(snapshot);
    }

    /// <summary>Pointer drag. Shapes redraw from the pre-drag image so the preview follows live.</summary>
    public void Continue(double x, double y)
    {
        if (!_stroking || !TryPixel(x, y, out int px, out int py)) return;
        switch (Tool)
        {
            case EnvironmentPaintTool.Brush: Line(_lastX, _lastY, px, py, Color); break;
            case EnvironmentPaintTool.Eraser: Line(_lastX, _lastY, px, py, EnvironmentCanvasPolicy.DefaultColor); break;
            case EnvironmentPaintTool.Square:
            case EnvironmentPaintTool.Circle:
            case EnvironmentPaintTool.Line:
                RestoreStrokeBase();
                DrawShape(px, py);
                break;
        }
        _lastX = px;
        _lastY = py;
    }

    public void End(double x, double y)
    {
        if (!_stroking) return;
        Continue(x, y);
        _stroking = false;
        _strokeBase = null;
        SettleGesture();
    }

    public bool TryPick(double x, double y, out EnvironmentColor color)
    {
        color = default;
        return TryPixel(x, y, out int px, out int py) && TrySample(px, py, out color);
    }

    public bool Undo()
    {
        if (_undo.Last is null) return false;
        byte[] restored = _undo.Last.Value;
        _undo.RemoveLast();
        restored.CopyTo(_pixels.AsSpan());
        Revision++;
        IsDirty = true;
        return true;
    }

    /// <summary>Reset paints the whole room back to the blank default; it stays undoable.</summary>
    public void Reset()
    {
        byte[] snapshot = (byte[])_pixels.Clone();
        FillAll(EnvironmentCanvasPolicy.DefaultColor);
        PushUndo(snapshot);
        SettleGesture();
    }

    /// <summary>Adopts stored pixels; the loaded image is the new clean baseline.</summary>
    public void Replace(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != EnvironmentCanvasPolicy.Bytes)
            throw new ArgumentException("The room canvas must be exactly 512x512 RGBA8.", nameof(pixels));
        pixels.CopyTo(_pixels);
        _undo.Clear();
        Revision++;
        IsDirty = false;
    }

    public byte[] ClonePixels() => (byte[])_pixels.Clone();

    public void MarkSaved()
    {
        _undo.Clear();
        IsDirty = false;
    }

    private void PushUndo(byte[] snapshot)
    {
        _undo.AddLast(snapshot);
        while (_undo.Count > EnvironmentCanvasPolicy.UndoDepth) _undo.RemoveFirst();
    }

    /// <summary>A gesture that changed nothing leaves neither an undo step nor unsaved changes.</summary>
    private void SettleGesture()
    {
        if (_undo.Last is null) return;
        if (_undo.Last.Value.AsSpan().SequenceEqual(_pixels)) _undo.RemoveLast();
        else IsDirty = true;
    }

    private void RestoreStrokeBase()
    {
        if (_strokeBase is null) return;
        _strokeBase.CopyTo(_pixels.AsSpan());
        Revision++;
    }

    private void DrawShape(int px, int py)
    {
        switch (Tool)
        {
            case EnvironmentPaintTool.Line: Line(_originX, _originY, px, py, Color); break;
            case EnvironmentPaintTool.Square: Rectangle(_originX, _originY, px, py); break;
            case EnvironmentPaintTool.Circle: Ellipse(_originX, _originY, px, py); break;
        }
    }

    private void Rectangle(int x0, int y0, int x1, int y1)
    {
        Line(x0, y0, x1, y0, Color);
        Line(x1, y0, x1, y1, Color);
        Line(x1, y1, x0, y1, Color);
        Line(x0, y1, x0, y0, Color);
    }

    private void Ellipse(int x0, int y0, int x1, int y1)
    {
        double centerX = (x0 + x1) / 2.0;
        double centerY = (y0 + y1) / 2.0;
        double radiusX = Math.Abs(x1 - x0) / 2.0;
        double radiusY = Math.Abs(y1 - y0) / 2.0;
        int steps = Math.Max(24, (int)((radiusX + radiusY) * 2));
        int previousX = 0, previousY = 0;
        for (int step = 0; step <= steps; step++)
        {
            double angle = step / (double)steps * Math.Tau;
            int x = (int)Math.Round(centerX + (Math.Cos(angle) * radiusX));
            int y = (int)Math.Round(centerY + (Math.Sin(angle) * radiusY));
            if (step > 0) Line(previousX, previousY, x, y, Color);
            previousX = x;
            previousY = y;
        }
    }

    private void Line(int x0, int y0, int x1, int y1, EnvironmentColor color)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int steps = Math.Max(1, Math.Max(dx, dy));
        for (int step = 0; step <= steps; step++)
        {
            double t = step / (double)steps;
            Stamp((int)Math.Round(x0 + ((x1 - x0) * t)), (int)Math.Round(y0 + ((y1 - y0) * t)), color);
        }
    }

    private void Stamp(int centerX, int centerY, EnvironmentColor color)
    {
        double radius = BrushDiameter / 2.0;
        double radiusSquared = radius * radius;
        int minX = Math.Max(0, (int)Math.Floor(centerX - radius));
        int maxX = Math.Min(EnvironmentCanvasPolicy.Size - 1, (int)Math.Ceiling(centerX + radius));
        int minY = Math.Max(0, (int)Math.Floor(centerY - radius));
        int maxY = Math.Min(EnvironmentCanvasPolicy.Size - 1, (int)Math.Ceiling(centerY + radius));
        bool changed = false;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double offsetX = x - centerX;
                double offsetY = y - centerY;
                if ((offsetX * offsetX) + (offsetY * offsetY) > radiusSquared) continue;
                changed |= Write(x, y, color);
            }
        }
        if (changed) Revision++;
    }

    /// <summary>Four-way flood fill over the contiguous run of the colour under the pointer.</summary>
    private void Fill(int startX, int startY, EnvironmentColor color)
    {
        if (!TrySample(startX, startY, out EnvironmentColor target)) return;
        if (target == color) return;
        var pending = new Stack<(int X, int Y)>();
        pending.Push((startX, startY));
        while (pending.Count > 0)
        {
            (int x, int y) = pending.Pop();
            if (x < 0 || y < 0 || x >= EnvironmentCanvasPolicy.Size || y >= EnvironmentCanvasPolicy.Size) continue;
            if (!TrySample(x, y, out EnvironmentColor current) || current != target) continue;
            Write(x, y, color);
            pending.Push((x + 1, y));
            pending.Push((x - 1, y));
            pending.Push((x, y + 1));
            pending.Push((x, y - 1));
        }
        Revision++;
    }

    private bool Write(int x, int y, EnvironmentColor color)
    {
        int index = ((y * EnvironmentCanvasPolicy.Size) + x) * EnvironmentCanvasPolicy.BytesPerPixel;
        if (_pixels[index] == color.Red && _pixels[index + 1] == color.Green &&
            _pixels[index + 2] == color.Blue && _pixels[index + 3] == byte.MaxValue)
        {
            return false;
        }
        _pixels[index] = color.Red;
        _pixels[index + 1] = color.Green;
        _pixels[index + 2] = color.Blue;
        _pixels[index + 3] = byte.MaxValue;
        return true;
    }

    private bool TrySample(int x, int y, out EnvironmentColor color)
    {
        color = default;
        if (x < 0 || y < 0 || x >= EnvironmentCanvasPolicy.Size || y >= EnvironmentCanvasPolicy.Size) return false;
        int index = ((y * EnvironmentCanvasPolicy.Size) + x) * EnvironmentCanvasPolicy.BytesPerPixel;
        color = new EnvironmentColor(_pixels[index], _pixels[index + 1], _pixels[index + 2]);
        return true;
    }

    private void FillAll(EnvironmentColor color)
    {
        for (int index = 0; index < _pixels.Length; index += EnvironmentCanvasPolicy.BytesPerPixel)
        {
            _pixels[index] = color.Red;
            _pixels[index + 1] = color.Green;
            _pixels[index + 2] = color.Blue;
            _pixels[index + 3] = byte.MaxValue;
        }
        Revision++;
    }

    private static bool TryPixel(double x, double y, out int pixelX, out int pixelY)
    {
        pixelX = 0;
        pixelY = 0;
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1) return false;
        pixelX = Math.Clamp((int)Math.Round(x * (EnvironmentCanvasPolicy.Size - 1)), 0, EnvironmentCanvasPolicy.Size - 1);
        pixelY = Math.Clamp((int)Math.Round(y * (EnvironmentCanvasPolicy.Size - 1)), 0, EnvironmentCanvasPolicy.Size - 1);
        return true;
    }
}
