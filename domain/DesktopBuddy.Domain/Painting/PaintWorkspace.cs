using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Painting;

public sealed record PaintPatch(PaintPart Part, PaintRect Rectangle, byte[] Before, byte[] After)
{
    public long MemoryBytes => Before.LongLength + After.LongLength;
}

public sealed record PaintCommand(IReadOnlyList<PaintPatch> Patches)
{
    public long MemoryBytes => Patches.Sum(patch => patch.MemoryBytes);
}

public sealed class PaintUndoHistory
{
    private readonly LinkedList<PaintCommand> _undo = new();
    private readonly LinkedList<PaintCommand> _redo = new();

    public long MemoryBytes { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(PaintCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Patches.Count == 0) return;
        if (command.MemoryBytes > PaintPolicy.UndoBudgetBytes)
            throw new InvalidOperationException("A paint command exceeds the complete undo budget.");

        ClearRedo();
        _undo.AddLast(command);
        MemoryBytes += command.MemoryBytes;
        TrimOldestUndo();
    }

    public bool TryUndo(out PaintCommand command)
    {
        if (_undo.Last is null)
        {
            command = EmptyCommand();
            return false;
        }
        command = _undo.Last.Value;
        _undo.RemoveLast();
        _redo.AddLast(command);
        return true;
    }

    public bool TryRedo(out PaintCommand command)
    {
        if (_redo.Last is null)
        {
            command = EmptyCommand();
            return false;
        }
        command = _redo.Last.Value;
        _redo.RemoveLast();
        _undo.AddLast(command);
        return true;
    }

    public bool TryPop(out PaintCommand command) => TryUndo(out command);

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        MemoryBytes = 0;
    }

    private void ClearRedo()
    {
        foreach (PaintCommand command in _redo)
            MemoryBytes -= command.MemoryBytes;
        _redo.Clear();
    }

    private void TrimOldestUndo()
    {
        while (_undo.Count > 0 && MemoryBytes > PaintPolicy.UndoBudgetBytes)
        {
            PaintCommand oldest = _undo.First!.Value;
            _undo.RemoveFirst();
            MemoryBytes -= oldest.MemoryBytes;
        }
    }

    private static PaintCommand EmptyCommand() => new(Array.Empty<PaintPatch>());
}

public sealed class PaintWorkspace
{
    private sealed class GesturePatchBuilder
    {
        public PaintRect Rectangle { get; private set; }
        public byte[] Before { get; private set; } = Array.Empty<byte>();

        public void Expand(PaintSurface surface, PaintRect candidate)
        {
            if (candidate.IsEmpty) return;
            PaintRect union = PaintRect.Union(Rectangle, candidate);
            if (union == Rectangle) return;

            byte[] expanded = surface.Capture(union);
            if (!Rectangle.IsEmpty)
                CopyPatch(Before, Rectangle, expanded, union);
            Rectangle = union;
            Before = expanded;
        }

        private static void CopyPatch(
            ReadOnlySpan<byte> source,
            PaintRect sourceRect,
            Span<byte> destination,
            PaintRect destinationRect)
        {
            int sourceStride = sourceRect.Width * PaintPolicy.BytesPerPixel;
            int destinationStride = destinationRect.Width * PaintPolicy.BytesPerPixel;
            int destinationX = (sourceRect.X - destinationRect.X) * PaintPolicy.BytesPerPixel;
            int destinationY = sourceRect.Y - destinationRect.Y;
            for (int row = 0; row < sourceRect.Height; row++)
            {
                source.Slice(row * sourceStride, sourceStride).CopyTo(
                    destination.Slice(((destinationY + row) * destinationStride) + destinationX, sourceStride));
            }
        }
    }

    private readonly Dictionary<PaintPart, PaintSurface> _surfaces =
        Enum.GetValues<PaintPart>().ToDictionary(part => part, _ => new PaintSurface());
    private readonly PaintUndoHistory _history = new();
    private readonly Dictionary<PaintPart, GesturePatchBuilder> _gestureBefore = new();
    private readonly Dictionary<PaintPart, GesturePatchBuilder> _previewBefore = new();
    private PaintHit? _lastHit;
    private bool _gestureActive;
    private bool _previewActive;
    private PaintTool _selectedTool = PaintTool.Brush;
    private ulong _sprayGestureSeed = 0xD1B54A32D192ED03UL;
    private ulong _sprayPulseOrdinal;

    /// <summary>Raised after a compound preview is committed or cancelled, including external session boundaries.</summary>
    public event Action? PreviewTransactionEnded;

    public PaintTool SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (_selectedTool == value) return;
            EndGesture();
            CancelPreviewTransaction();
            _selectedTool = value;
        }
    }

    public PaintColor SelectedColor { get; set; } = PaintColor.White;
    public int BrushDiameter { get; private set; } = PaintPolicy.DefaultBrushDiameter;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public long UndoMemoryBytes => _history.MemoryBytes;
    public bool IsDirty { get; private set; }
    public bool GestureActive => _gestureActive;
    public bool PreviewActive => _previewActive;
    public IReadOnlyDictionary<PaintPart, PaintSurface> Surfaces => _surfaces;

    public void AdjustBrush(int steps) => BrushDiameter = (int)Math.Clamp(
        BrushDiameter + ((long)steps * PaintPolicy.BrushStep),
        PaintPolicy.MinBrushDiameter,
        PaintPolicy.MaxBrushDiameter);

    public void SetBrushDiameter(int diameter) =>
        BrushDiameter = Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, PaintPolicy.MaxBrushDiameter);

    /// <summary>
    /// Flood-fills one connected region on a single buddy surface. Horizontal neighbours wrap
    /// across the texture U seam while vertical neighbours clip at the poles. The fill is one
    /// exact undo command regardless of region size.
    /// </summary>
    public bool BucketFill(PaintHit? hit)
    {
        CancelPreviewTransaction();
        EndGesture();
        if (hit is not PaintHit valid || !valid.IsValid)
            return false;

        PaintSurface surface = _surfaces[valid.Part];
        byte[] before = surface.ClonePixels();
        byte[] after = (byte[])before.Clone();
        if (!FloodFillPixels(after, valid.Uv, SelectedColor))
            return false;

        surface.Replace(after);
        PaintRect whole = new(0, 0, PaintPolicy.SurfaceSize, PaintPolicy.SurfaceSize);
        _history.Push(new PaintCommand(new[] { new PaintPatch(valid.Part, whole, before, after) }));
        IsDirty = true;
        return true;
    }

    public void BeginGesture(PaintHit? hit)
    {
        CancelPreviewTransaction();
        EndGesture();
        if (_selectedTool == PaintTool.Fill)
        {
            BucketFill(hit);
            return;
        }

        _gestureActive = true;
        _lastHit = null;
        if (_selectedTool == PaintTool.Spray)
        {
            _sprayGestureSeed += 0x9E3779B97F4A7C15UL;
            _sprayPulseOrdinal = 0;
        }
        if (hit is not PaintHit valid || !valid.IsValid) return;

        _lastHit = valid;
        if (_selectedTool == PaintTool.Spray)
        {
            SprayPulse(valid);
            return;
        }

        PaintTool mutation = _selectedTool == PaintTool.Eraser ? PaintTool.Eraser : PaintTool.Brush;
        CaptureBefore(_gestureBefore, valid.Part, PaintSurface.StampBounds(valid.Uv, BrushDiameter));
        _surfaces[valid.Part].Stamp(valid.Uv, BrushDiameter, mutation, SelectedColor);
    }

    public void ContinueGesture(PaintHit? hit)
    {
        if (!_gestureActive) return;
        if (hit is null || !hit.Value.IsValid) return;

        PaintHit current = hit.Value;
        if (_selectedTool == PaintTool.Spray)
        {
            SprayPulse(current);
            _lastHit = current;
            return;
        }

        PaintTool mutation = _selectedTool == PaintTool.Eraser ? PaintTool.Eraser : PaintTool.Brush;
        if (_lastHit is PaintHit previous && previous.Part == current.Part && IsBridgeable(previous.Uv, current.Uv))
        {
            CaptureBefore(_gestureBefore, current.Part, PaintSurface.StrokeBounds(previous.Uv, current.Uv, BrushDiameter));
            _surfaces[current.Part].Stroke(previous.Uv, current.Uv, BrushDiameter, mutation, SelectedColor);
        }
        else
        {
            CaptureBefore(_gestureBefore, current.Part, PaintSurface.StampBounds(current.Uv, BrushDiameter));
            _surfaces[current.Part].Stamp(current.Uv, BrushDiameter, mutation, SelectedColor);
        }
        _lastHit = current;
    }

    public void EndGesture()
    {
        if (!_gestureActive) return;
        CommitBuilders(_gestureBefore);
        _gestureActive = false;
        _lastHit = null;
    }

    public void BeginPreviewTransaction()
    {
        EndGesture();
        CancelPreviewTransaction();
        _previewActive = true;
    }

    public void RenderPreviewPath(IReadOnlyList<PaintHit?> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!_previewActive)
            throw new InvalidOperationException("BeginPreviewTransaction must be called before rendering a preview path.");

        RestoreBuilders(_previewBefore);
        PaintHit? previous = null;
        foreach (PaintHit? sample in samples)
        {
            if (sample is not PaintHit current || !current.IsValid)
            {
                previous = null;
                continue;
            }

            if (previous is PaintHit prior && prior.Part == current.Part && IsBridgeable(prior.Uv, current.Uv))
            {
                CaptureBefore(_previewBefore, current.Part, PaintSurface.StrokeBounds(prior.Uv, current.Uv, BrushDiameter));
                _surfaces[current.Part].Stroke(prior.Uv, current.Uv, BrushDiameter, PaintTool.Brush, SelectedColor);
            }
            else
            {
                CaptureBefore(_previewBefore, current.Part, PaintSurface.StampBounds(current.Uv, BrushDiameter));
                _surfaces[current.Part].Stamp(current.Uv, BrushDiameter, PaintTool.Brush, SelectedColor);
            }
            previous = current;
        }
    }

    public bool FinalizePreviewTransaction()
    {
        if (!_previewActive) return false;
        bool changed = CommitBuilders(_previewBefore);
        _previewActive = false;
        PreviewTransactionEnded?.Invoke();
        return changed;
    }

    public bool CancelPreviewTransaction()
    {
        if (!_previewActive) return false;
        RestoreBuilders(_previewBefore);
        _previewBefore.Clear();
        _previewActive = false;
        PreviewTransactionEnded?.Invoke();
        return true;
    }

    private void SprayPulse(PaintHit hit)
    {
        PaintRect bounds = PaintSurface.StampBounds(hit.Uv, BrushDiameter);
        CaptureBefore(_gestureBefore, hit.Part, bounds);
        ulong seed = _sprayGestureSeed + (_sprayPulseOrdinal++ * 0x9E3779B97F4A7C15UL);
        _surfaces[hit.Part].Spray(hit.Uv, BrushDiameter, SelectedColor, seed);
    }

    private static bool IsBridgeable(PaintPoint from, PaintPoint to)
    {
        double dx = Math.Abs(to.X - from.X);
        if (dx > 0.5) dx = 1.0 - dx;
        double dy = to.Y - from.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= MaxBridgeUvDistance;
    }

    private const double MaxBridgeUvDistance = 0.25;

    public void EraseAll()
    {
        CancelPreviewTransaction();
        EndGesture();
        PaintRect whole = new(0, 0, PaintPolicy.SurfaceSize, PaintPolicy.SurfaceSize);
        List<PaintPatch> patches = new();
        foreach ((PaintPart part, PaintSurface surface) in _surfaces)
        {
            byte[] before = surface.ClonePixels();
            if (Array.TrueForAll(before, value => value == 0)) continue;
            surface.Clear();
            patches.Add(new PaintPatch(part, whole, before, surface.ClonePixels()));
        }
        if (patches.Count == 0) return;
        _history.Push(new PaintCommand(patches));
        IsDirty = true;
    }

    public bool Undo()
    {
        if (CancelPreviewTransaction()) return true;
        EndGesture();
        if (!_history.TryUndo(out PaintCommand command)) return false;
        Restore(command, useAfter: false);
        IsDirty = true;
        return true;
    }

    public bool Redo()
    {
        if (CancelPreviewTransaction()) return true;
        EndGesture();
        if (!_history.TryRedo(out PaintCommand command)) return false;
        Restore(command, useAfter: true);
        IsDirty = true;
        return true;
    }

    public void MarkDirty() => IsDirty = true;

    public void MarkSaved()
    {
        CancelPreviewTransaction();
        EndGesture();
        _history.Clear();
        IsDirty = false;
    }

    public void Load(PaintPart part, ReadOnlySpan<byte> pixels)
    {
        CancelPreviewTransaction();
        EndGesture();
        _surfaces[part].Replace(pixels);
        _history.Clear();
        IsDirty = false;
    }

    private bool CommitBuilders(Dictionary<PaintPart, GesturePatchBuilder> builders)
    {
        List<PaintPatch> patches = new();
        foreach ((PaintPart part, GesturePatchBuilder builder) in builders)
        {
            if (builder.Rectangle.IsEmpty) continue;
            byte[] after = _surfaces[part].Capture(builder.Rectangle);
            if (!builder.Before.AsSpan().SequenceEqual(after))
                patches.Add(new PaintPatch(part, builder.Rectangle, builder.Before, after));
        }
        // A gesture that changed nothing must not push an undo entry, or Ctrl+Z spends
        // itself popping an empty command instead of reverting the last real stroke.
        if (patches.Count > 0)
        {
            _history.Push(new PaintCommand(patches));
            IsDirty = true;
        }
        builders.Clear();
        return patches.Count > 0;
    }

    private void RestoreBuilders(Dictionary<PaintPart, GesturePatchBuilder> builders)
    {
        foreach ((PaintPart part, GesturePatchBuilder builder) in builders)
        {
            if (!builder.Rectangle.IsEmpty)
                _surfaces[part].Restore(builder.Rectangle, builder.Before);
        }
    }

    private void Restore(PaintCommand command, bool useAfter)
    {
        foreach (PaintPatch patch in command.Patches)
            _surfaces[patch.Part].Restore(patch.Rectangle, useAfter ? patch.After : patch.Before);
    }

    private void CaptureBefore(
        Dictionary<PaintPart, GesturePatchBuilder> builders,
        PaintPart part,
        PaintRect rectangle)
    {
        if (!builders.TryGetValue(part, out GesturePatchBuilder? builder))
        {
            builder = new GesturePatchBuilder();
            builders.Add(part, builder);
        }
        builder.Expand(_surfaces[part], rectangle);
    }

    private static bool FloodFillPixels(byte[] pixels, PaintPoint uv, PaintColor replacement)
    {
        if (!uv.IsFinite || uv.X < 0.0 || uv.X > 1.0 || uv.Y < 0.0 || uv.Y > 1.0)
            return false;

        int size = PaintPolicy.SurfaceSize;
        int startX = Math.Clamp((int)Math.Round(uv.X * (size - 1)), 0, size - 1);
        int startY = Math.Clamp((int)Math.Round(uv.Y * (size - 1)), 0, size - 1);
        int startIndex = ((startY * size) + startX) * PaintPolicy.BytesPerPixel;
        byte targetR = pixels[startIndex];
        byte targetG = pixels[startIndex + 1];
        byte targetB = pixels[startIndex + 2];
        byte targetA = pixels[startIndex + 3];
        const byte replacementA = byte.MaxValue;

        if (targetA != 0 && targetR == replacement.R && targetG == replacement.G &&
            targetB == replacement.B && targetA == replacementA)
        {
            return false;
        }

        int[] queue = new int[size * size];
        int head = 0;
        int tail = 0;

        bool MatchesTarget(int index) => targetA == 0
            ? pixels[index + 3] == 0
            : pixels[index] == targetR && pixels[index + 1] == targetG &&
              pixels[index + 2] == targetB && pixels[index + 3] == targetA;

        void EnqueueIfTarget(int x, int y)
        {
            if (y < 0 || y >= size)
                return;
            int wrappedX = ((x % size) + size) % size;
            int index = ((y * size) + wrappedX) * PaintPolicy.BytesPerPixel;
            if (!MatchesTarget(index))
                return;

            pixels[index] = replacement.R;
            pixels[index + 1] = replacement.G;
            pixels[index + 2] = replacement.B;
            pixels[index + 3] = replacementA;
            queue[tail++] = (y * size) + wrappedX;
        }

        EnqueueIfTarget(startX, startY);
        while (head < tail)
        {
            int encoded = queue[head++];
            int x = encoded % size;
            int y = encoded / size;
            EnqueueIfTarget(x - 1, y);
            EnqueueIfTarget(x + 1, y);
            EnqueueIfTarget(x, y - 1);
            EnqueueIfTarget(x, y + 1);
        }

        return tail > 0;
    }
}
