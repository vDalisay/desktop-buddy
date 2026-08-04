using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Painting;

public sealed record PaintPatch(PaintPart Part, PaintRect Rectangle, byte[] Before)
{
    public long MemoryBytes => Before.LongLength;
}

public sealed record PaintCommand(IReadOnlyList<PaintPatch> Patches)
{
    public long MemoryBytes => Patches.Sum(patch => patch.MemoryBytes);
}

public sealed class PaintUndoHistory
{
    private readonly LinkedList<PaintCommand> _commands = new();
    public long MemoryBytes { get; private set; }
    public bool CanUndo => _commands.Count > 0;

    public void Push(PaintCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Patches.Count == 0) return;
        if (command.MemoryBytes > PaintPolicy.UndoBudgetBytes)
            throw new InvalidOperationException("A paint command exceeds the complete undo budget.");
        while (_commands.Count > 0 && MemoryBytes + command.MemoryBytes > PaintPolicy.UndoBudgetBytes)
        {
            PaintCommand oldest = _commands.First!.Value;
            MemoryBytes -= oldest.MemoryBytes;
            _commands.RemoveFirst();
        }
        _commands.AddLast(command);
        MemoryBytes += command.MemoryBytes;
    }

    public bool TryPop(out PaintCommand command)
    {
        if (_commands.Last is null)
        {
            command = new PaintCommand(Array.Empty<PaintPatch>());
            return false;
        }
        command = _commands.Last.Value;
        _commands.RemoveLast();
        MemoryBytes -= command.MemoryBytes;
        return true;
    }

    public void Clear()
    {
        _commands.Clear();
        MemoryBytes = 0;
    }
}

public sealed class PaintWorkspace
{
    private sealed class GesturePatchBuilder
    {
        public PaintRect Rectangle { get; private set; }
        public byte[] Before { get; private set; } = Array.Empty<byte>();

        public void Expand(PaintSurface surface, PaintRect candidate)
        {
            if (candidate.IsEmpty)
                return;

            PaintRect union = PaintRect.Union(Rectangle, candidate);
            if (union == Rectangle)
                return;

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
    private readonly PaintUndoHistory _undo = new();
    private readonly Dictionary<PaintPart, GesturePatchBuilder> _gestureBefore = new();
    private PaintHit? _lastHit;
    private bool _gestureActive;

    public PaintTool SelectedTool { get; set; } = PaintTool.Brush;
    public PaintColor SelectedColor { get; set; } = PaintColor.White;
    public int BrushDiameter { get; private set; } = PaintPolicy.DefaultBrushDiameter;
    public bool CanUndo => _undo.CanUndo;
    public long UndoMemoryBytes => _undo.MemoryBytes;
    public bool IsDirty { get; private set; }
    public IReadOnlyDictionary<PaintPart, PaintSurface> Surfaces => _surfaces;

    public void AdjustBrush(int steps) => BrushDiameter = (int)Math.Clamp(
        BrushDiameter + ((long)steps * PaintPolicy.BrushStep),
        PaintPolicy.MinBrushDiameter,
        PaintPolicy.MaxBrushDiameter);
    public void SetBrushDiameter(int diameter) =>
        BrushDiameter = Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, PaintPolicy.MaxBrushDiameter);

    public void BeginGesture(PaintHit hit)
    {
        if (!hit.IsValid)
            throw new ArgumentException("A paint gesture must begin on a valid surface hit.", nameof(hit));
        EndGesture();
        _gestureActive = true;
        _lastHit = hit;
        CaptureBefore(hit.Part, PaintSurface.StampBounds(hit.Uv, BrushDiameter));
        _surfaces[hit.Part].Stamp(hit.Uv, BrushDiameter, SelectedTool, SelectedColor);
    }

    public void ContinueGesture(PaintHit? hit)
    {
        if (!_gestureActive) return;
        if (hit is null || !hit.Value.IsValid)
        {
            _lastHit = null;
            return;
        }
        PaintHit current = hit.Value;
        if (_lastHit is PaintHit previous && previous.Part == current.Part)
        {
            CaptureBefore(
                current.Part,
                PaintSurface.StrokeBounds(previous.Uv, current.Uv, BrushDiameter));
            _surfaces[current.Part].Stroke(
                previous.Uv,
                current.Uv,
                BrushDiameter,
                SelectedTool,
                SelectedColor);
        }
        else
        {
            CaptureBefore(current.Part, PaintSurface.StampBounds(current.Uv, BrushDiameter));
            _surfaces[current.Part].Stamp(
                current.Uv,
                BrushDiameter,
                SelectedTool,
                SelectedColor);
        }
        _lastHit = current;
    }

    public void EndGesture()
    {
        if (!_gestureActive) return;
        List<PaintPatch> patches = new();
        foreach ((PaintPart part, GesturePatchBuilder builder) in _gestureBefore)
        {
            if (builder.Rectangle.IsEmpty)
                continue;
            byte[] after = _surfaces[part].Capture(builder.Rectangle);
            if (!builder.Before.AsSpan().SequenceEqual(after))
                patches.Add(new PaintPatch(part, builder.Rectangle, builder.Before));
        }
        _undo.Push(new PaintCommand(patches));
        if (patches.Count > 0) IsDirty = true;
        _gestureBefore.Clear();
        _gestureActive = false;
        _lastHit = null;
    }

    public void EraseAll()
    {
        EndGesture();
        PaintRect whole = new(0, 0, PaintPolicy.SurfaceSize, PaintPolicy.SurfaceSize);
        List<PaintPatch> patches = new();
        foreach ((PaintPart part, PaintSurface surface) in _surfaces)
        {
            byte[] before = surface.ClonePixels();
            if (Array.TrueForAll(before, value => value == 0)) continue;
            patches.Add(new PaintPatch(part, whole, before));
            surface.Clear();
        }
        _undo.Push(new PaintCommand(patches));
        if (patches.Count > 0) IsDirty = true;
    }

    public bool Undo()
    {
        EndGesture();
        if (!_undo.TryPop(out PaintCommand command)) return false;
        foreach (PaintPatch patch in command.Patches)
            _surfaces[patch.Part].Restore(patch.Rectangle, patch.Before);
        IsDirty = true;
        return true;
    }

    public void MarkDirty() => IsDirty = true;

    public void MarkSaved()
    {
        EndGesture();
        _undo.Clear();
        IsDirty = false;
    }

    public void Load(PaintPart part, ReadOnlySpan<byte> pixels)
    {
        _surfaces[part].Replace(pixels);
        _undo.Clear();
        IsDirty = false;
    }

    private void CaptureBefore(PaintPart part, PaintRect rectangle)
    {
        if (!_gestureBefore.TryGetValue(part, out GesturePatchBuilder? builder))
        {
            builder = new GesturePatchBuilder();
            _gestureBefore.Add(part, builder);
        }
        builder.Expand(_surfaces[part], rectangle);
    }
}
