using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Painting;

namespace DesktopBuddy.Domain.Environment;

public enum EnvironmentPaintTool { Brush, Spray, Eraser, Fill, PickColor, Square, Circle, Line, CurvedLine }
public enum EnvironmentCurvePhase { Idle, BaselineDragging, AwaitFirstBend, FirstBendDragging, AwaitSecondBend, SecondBendDragging }

public static class EnvironmentCanvasPolicy
{
    public const int Size = 512;
    public const int BytesPerPixel = 4;
    public const int Bytes = Size * Size * BytesPerPixel;
    public const int MinBrushDiameter = 2;
    public const int MaxBrushDiameter = 96;
    public const int DefaultBrushDiameter = 16;
    public const int BrushStep = 2;
    public const int UndoDepth = 12;
    public const string RelativePath = "environment/background.png";
    public static EnvironmentColor DefaultColor => new(192, 192, 192);
}

/// <summary>
/// Room background paint surface. It deliberately clamps both axes, unlike buddy paint's cyclic U.
/// Shape/curve previews restore one captured baseline before rerasterising.
/// </summary>
public sealed class EnvironmentCanvas
{
    private readonly byte[] _pixels = new byte[EnvironmentCanvasPolicy.Bytes];
    private readonly LinkedList<byte[]> _undo = new();
    private byte[]? _strokeBase;
    private byte[]? _curveBase;
    private int _originX;
    private int _originY;
    private int _lastX = -1;
    private int _lastY = -1;
    private bool _stroking;
    private EnvironmentPaintTool _tool = EnvironmentPaintTool.Brush;
    private EnvironmentCurvePhase _curvePhase;
    private CubicPaintCurve _curve;
    private PaintCurveBend _firstBend;
    private PaintCurveBend _previewBend;
    private double _activeBendT;
    private ulong _sprayGestureSeed = 0xA0761D6478BD642FUL;
    private ulong _sprayPulseOrdinal;

    public EnvironmentCanvas() => FillAll(EnvironmentCanvasPolicy.DefaultColor);

    public long Revision { get; private set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public ReadOnlyMemory<byte> Pixels => _pixels;
    public EnvironmentCurvePhase CurvePhase => _curvePhase;
    public bool CurvePending => _curvePhase != EnvironmentCurvePhase.Idle;

    public EnvironmentPaintTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            if (CurvePending) CancelPendingCurve();
            else if (_stroking) End(double.NaN, double.NaN);
            _tool = value;
        }
    }

    public EnvironmentColor Color { get; set; } = new(0, 0, 0);

    private int _brushDiameter = EnvironmentCanvasPolicy.DefaultBrushDiameter;
    public int BrushDiameter
    {
        get => _brushDiameter;
        set => _brushDiameter = Math.Clamp(value, EnvironmentCanvasPolicy.MinBrushDiameter, EnvironmentCanvasPolicy.MaxBrushDiameter);
    }

    public void AdjustBrush(int steps) => BrushDiameter = (int)Math.Clamp(
        BrushDiameter + ((long)steps * EnvironmentCanvasPolicy.BrushStep),
        EnvironmentCanvasPolicy.MinBrushDiameter,
        EnvironmentCanvasPolicy.MaxBrushDiameter);

    public void Begin(double x, double y)
    {
        if (_tool == EnvironmentPaintTool.CurvedLine)
        {
            BeginCurve(x, y);
            return;
        }
        if (!TryPixel(x, y, out int px, out int py)) return;

        byte[] snapshot = (byte[])_pixels.Clone();
        _stroking = true;
        _originX = px;
        _originY = py;
        _lastX = px;
        _lastY = py;
        switch (_tool)
        {
            case EnvironmentPaintTool.Brush:
                Stamp(px, py, Color);
                break;
            case EnvironmentPaintTool.Spray:
                _sprayGestureSeed += 0x9E3779B97F4A7C15UL;
                _sprayPulseOrdinal = 0;
                Spray(px, py, Color, NextSpraySeed());
                break;
            case EnvironmentPaintTool.Eraser:
                Stamp(px, py, EnvironmentCanvasPolicy.DefaultColor);
                break;
            case EnvironmentPaintTool.Fill:
                Fill(px, py, Color);
                break;
            case EnvironmentPaintTool.PickColor:
                if (TrySample(px, py, out EnvironmentColor picked)) Color = picked;
                break;
            default:
                _strokeBase = (byte[])_pixels.Clone();
                break;
        }
        PushUndo(snapshot);
    }

    public void Continue(double x, double y)
    {
        if (_tool == EnvironmentPaintTool.CurvedLine)
        {
            ContinueCurve(x, y);
            return;
        }
        if (!_stroking || !TryPixel(x, y, out int px, out int py)) return;
        switch (_tool)
        {
            case EnvironmentPaintTool.Brush:
                Line(_lastX, _lastY, px, py, Color);
                break;
            case EnvironmentPaintTool.Spray:
                Spray(px, py, Color, NextSpraySeed());
                break;
            case EnvironmentPaintTool.Eraser:
                Line(_lastX, _lastY, px, py, EnvironmentCanvasPolicy.DefaultColor);
                break;
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
        if (_tool == EnvironmentPaintTool.CurvedLine)
        {
            EndCurve(x, y);
            return;
        }
        if (!_stroking) return;
        Continue(x, y);
        _stroking = false;
        _strokeBase = null;
        SettleGesture();
    }

    public bool CancelPendingCurve()
    {
        if (!CurvePending) return false;
        if (_curveBase is not null)
        {
            _curveBase.CopyTo(_pixels.AsSpan());
            Revision++;
        }
        ClearCurveState();
        return true;
    }

    public bool TryPick(double x, double y, out EnvironmentColor color)
    {
        color = default;
        return TryPixel(x, y, out int px, out int py) && TrySample(px, py, out color);
    }

    public bool Undo()
    {
        if (CancelPendingCurve()) return true;
        if (_stroking) End(double.NaN, double.NaN);
        if (_undo.Last is null) return false;
        byte[] restored = _undo.Last.Value;
        _undo.RemoveLast();
        restored.CopyTo(_pixels.AsSpan());
        Revision++;
        IsDirty = true;
        return true;
    }

    public void Reset()
    {
        CancelPendingCurve();
        if (_stroking) End(double.NaN, double.NaN);
        byte[] snapshot = (byte[])_pixels.Clone();
        FillAll(EnvironmentCanvasPolicy.DefaultColor);
        PushUndo(snapshot);
        SettleGesture();
    }

    public void Replace(ReadOnlySpan<byte> pixels)
    {
        CancelPendingCurve();
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
        CancelPendingCurve();
        if (_stroking) End(double.NaN, double.NaN);
        _undo.Clear();
        IsDirty = false;
    }

    private void BeginCurve(double x, double y)
    {
        if (!TryPixel(x, y, out int px, out int py)) return;
        PaintPoint pointer = new(px, py);
        switch (_curvePhase)
        {
            case EnvironmentCurvePhase.Idle:
                _curveBase = (byte[])_pixels.Clone();
                _originX = px;
                _originY = py;
                _curve = ClassicCurveGeometry.Straight(pointer, pointer);
                _curvePhase = EnvironmentCurvePhase.BaselineDragging;
                _stroking = true;
                PreviewCurve();
                break;
            case EnvironmentCurvePhase.AwaitFirstBend:
                _activeBendT = ClassicCurveGeometry.ClosestParameter(_curve, pointer);
                _previewBend = new PaintCurveBend(_activeBendT, pointer);
                _curvePhase = EnvironmentCurvePhase.FirstBendDragging;
                _stroking = true;
                break;
            case EnvironmentCurvePhase.AwaitSecondBend:
                _activeBendT = ClassicCurveGeometry.ClosestParameter(_curve, pointer);
                _previewBend = new PaintCurveBend(_activeBendT, pointer);
                _curvePhase = EnvironmentCurvePhase.SecondBendDragging;
                _stroking = true;
                break;
        }
    }

    private void ContinueCurve(double x, double y)
    {
        if (!_stroking || !TryPixel(x, y, out int px, out int py)) return;
        PaintPoint pointer = new(px, py);
        PaintPoint start = new(_originX, _originY);
        switch (_curvePhase)
        {
            case EnvironmentCurvePhase.BaselineDragging:
                _curve = ClassicCurveGeometry.Straight(start, pointer);
                PreviewCurve();
                break;
            case EnvironmentCurvePhase.FirstBendDragging:
                _previewBend = new PaintCurveBend(_activeBendT, pointer);
                _curve = ClassicCurveGeometry.BendOnce(_curve.Start, _curve.End, _previewBend);
                PreviewCurve();
                break;
            case EnvironmentCurvePhase.SecondBendDragging:
                _previewBend = new PaintCurveBend(_activeBendT, pointer);
                _curve = ClassicCurveGeometry.BendTwice(_curve.Start, _curve.End, _firstBend, _previewBend);
                PreviewCurve();
                break;
        }
    }

    private void EndCurve(double x, double y)
    {
        if (!_stroking) return;
        ContinueCurve(x, y);
        _stroking = false;
        switch (_curvePhase)
        {
            case EnvironmentCurvePhase.BaselineDragging:
                _curvePhase = EnvironmentCurvePhase.AwaitFirstBend;
                break;
            case EnvironmentCurvePhase.FirstBendDragging:
                _firstBend = _previewBend;
                _curvePhase = EnvironmentCurvePhase.AwaitSecondBend;
                break;
            case EnvironmentCurvePhase.SecondBendDragging:
                FinalizeCurve();
                break;
        }
    }

    private void PreviewCurve()
    {
        if (_curveBase is null) return;
        _curveBase.CopyTo(_pixels.AsSpan());
        Revision++;
        DrawCurve(_curve);
    }

    private void FinalizeCurve()
    {
        if (_curveBase is null)
        {
            ClearCurveState();
            return;
        }
        byte[] baseline = _curveBase;
        if (!baseline.AsSpan().SequenceEqual(_pixels))
        {
            PushUndo(baseline);
            IsDirty = true;
        }
        ClearCurveState();
    }

    private void ClearCurveState()
    {
        _curveBase = null;
        _curvePhase = EnvironmentCurvePhase.Idle;
        _stroking = false;
        _firstBend = default;
        _previewBend = default;
        _activeBendT = 0;
    }

    private void PushUndo(byte[] snapshot)
    {
        _undo.AddLast(snapshot);
        while (_undo.Count > EnvironmentCanvasPolicy.UndoDepth) _undo.RemoveFirst();
    }

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
        switch (_tool)
        {
            case EnvironmentPaintTool.Line: Line(_originX, _originY, px, py, Color); break;
            case EnvironmentPaintTool.Square: Rectangle(_originX, _originY, px, py); break;
            case EnvironmentPaintTool.Circle: Ellipse(_originX, _originY, px, py); break;
        }
    }

    private void DrawCurve(CubicPaintCurve curve)
    {
        IReadOnlyList<PaintPoint> points = ClassicCurveGeometry.Sample(curve, Math.Max(1.0, BrushDiameter * 0.12));
        for (int index = 1; index < points.Count; index++)
        {
            PaintPoint from = points[index - 1];
            PaintPoint to = points[index];
            Line((int)Math.Round(from.X), (int)Math.Round(from.Y), (int)Math.Round(to.X), (int)Math.Round(to.Y), Color);
        }
        if (points.Count == 1)
            Stamp((int)Math.Round(points[0].X), (int)Math.Round(points[0].Y), Color);
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
        for (int x = minX; x <= maxX; x++)
        {
            double offsetX = x - centerX;
            double offsetY = y - centerY;
            if ((offsetX * offsetX) + (offsetY * offsetY) > radiusSquared) continue;
            changed |= Write(x, y, color);
        }
        if (changed) Revision++;
    }

    private void Spray(int centerX, int centerY, EnvironmentColor color, ulong seed)
    {
        double radius = BrushDiameter / 2.0;
        PaintPoint[] offsets = SprayPattern.SampleUnitDisk(seed, SprayPattern.PointCountForDiameter(BrushDiameter));
        bool changed = false;
        foreach (PaintPoint offset in offsets)
        {
            int x = (int)Math.Round(centerX + (offset.X * radius));
            int y = (int)Math.Round(centerY + (offset.Y * radius));
            if (x < 0 || y < 0 || x >= EnvironmentCanvasPolicy.Size || y >= EnvironmentCanvasPolicy.Size) continue;
            changed |= Write(x, y, color);
        }
        if (changed) Revision++;
    }

    private ulong NextSpraySeed() => _sprayGestureSeed + (_sprayPulseOrdinal++ * 0x9E3779B97F4A7C15UL);

    private void Fill(int startX, int startY, EnvironmentColor color)
    {
        if (!TrySample(startX, startY, out EnvironmentColor target) || target == color) return;
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
            _pixels[index + 2] == color.Blue && _pixels[index + 3] == byte.MaxValue) return false;
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
