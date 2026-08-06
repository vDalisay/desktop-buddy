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

/// <summary>
/// Bounded reversible paint history. New edits invalidate redo. Undo and redo share the same
/// memory cap so adding redo cannot silently double the editor's documented memory budget.
/// </summary>
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
        if (command.Patches.Count == 0)
            return;
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

    // Kept for source compatibility with the original history API.
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
    private readonly PaintUndoHistory _history = new();
    private readonly Dictionary<PaintPart, GesturePatchBuilder> _gestureBefore = new();
    private PaintHit? _lastHit;
    private bool _gestureActive;

    public PaintTool SelectedTool { get; set; } = PaintTool.Brush;
    public PaintColor SelectedColor { get; set; } = PaintColor.White;
    public int BrushDiameter { get; private set; } = PaintPolicy.DefaultBrushDiameter;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public long UndoMemoryBytes => _history.MemoryBytes;
    public bool IsDirty { get; private set; }
    public IReadOnlyDictionary<PaintPart, PaintSurface> Surfaces => _surfaces;

    public void AdjustBrush(int steps) => BrushDiameter = (int)Math.Clamp(
        BrushDiameter + ((long)steps * PaintPolicy.BrushStep),
        PaintPolicy.MinBrushDiameter,
        PaintPolicy.MaxBrushDiameter);

    public void SetBrushDiameter(int diameter) =>
        BrushDiameter = Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, PaintPolicy.MaxBrushDiameter);

    /// <summary>
    /// Starts a gesture. A miss (null or invalid hit) still opens the gesture so a press that
    /// begins off the character can paint once the drag crosses onto it; nothing is stamped
    /// while the pointer is off-surface.
    /// </summary>
    public void BeginGesture(PaintHit? hit)
    {
        EndGesture();
        _gestureActive = true;
        _lastHit = null;
        if (hit is not PaintHit valid || !valid.IsValid)
            return;

        _lastHit = valid;
        CaptureBefore(valid.Part, PaintSurface.StampBounds(valid.Uv, BrushDiameter));
        _surfaces[valid.Part].Stamp(valid.Uv, BrushDiameter, SelectedTool, SelectedColor);
    }

    public void ContinueGesture(PaintHit? hit)
    {
        if (!_gestureActive)
            return;
        // A miss keeps the previous hit: pointer samples arrive once per frame, so a quick drag
        // skips clean over the buddy's small silhouette. Dropping continuity here turned every
        // landing into an isolated dot.
        if (hit is null || !hit.Value.IsValid)
            return;

        PaintHit current = hit.Value;
        if (_lastHit is PaintHit previous && previous.Part == current.Part &&
            IsBridgeable(previous.Uv, current.Uv))
        {
            CaptureBefore(current.Part, PaintSurface.StrokeBounds(previous.Uv, current.Uv, BrushDiameter));
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
        if (!_gestureActive)
            return;

        List<PaintPatch> patches = new();
        foreach ((PaintPart part, GesturePatchBuilder builder) in _gestureBefore)
        {
            if (builder.Rectangle.IsEmpty)
                continue;
            byte[] after = _surfaces[part].Capture(builder.Rectangle);
            if (!builder.Before.AsSpan().SequenceEqual(after))
                patches.Add(new PaintPatch(part, builder.Rectangle, builder.Before, after));
        }
        _history.Push(new PaintCommand(patches));
        if (patches.Count > 0)
            IsDirty = true;
        _gestureBefore.Clear();
        _gestureActive = false;
        _lastHit = null;
    }

    /// <summary>
    /// Whether two samples on one part are close enough to join. Beyond this the pointer left
    /// the part and came back somewhere else, and a joining stroke would smear across the UV.
    /// </summary>
    private static bool IsBridgeable(PaintPoint from, PaintPoint to)
    {
        double dx = Math.Abs(to.X - from.X);
        if (dx > 0.5)
            dx = 1.0 - dx; // U is cyclic.
        double dy = to.Y - from.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= MaxBridgeUvDistance;
    }

    private const double MaxBridgeUvDistance = 0.25;

    public void EraseAll()
    {
        EndGesture();
        PaintRect whole = new(0, 0, PaintPolicy.SurfaceSize, PaintPolicy.SurfaceSize);
        List<PaintPatch> patches = new();
        foreach ((PaintPart part, PaintSurface surface) in _surfaces)
        {
            byte[] before = surface.ClonePixels();
            if (Array.TrueForAll(before, value => value == 0))
                continue;
            surface.Clear();
            patches.Add(new PaintPatch(part, whole, before, surface.ClonePixels()));
        }
        _history.Push(new PaintCommand(patches));
        if (patches.Count > 0)
            IsDirty = true;
    }

    public bool Undo()
    {
        EndGesture();
        if (!_history.TryUndo(out PaintCommand command))
            return false;
        Restore(command, useAfter: false);
        IsDirty = true;
        return true;
    }

    public bool Redo()
    {
        EndGesture();
        if (!_history.TryRedo(out PaintCommand command))
            return false;
        Restore(command, useAfter: true);
        IsDirty = true;
        return true;
    }

    public void MarkDirty() => IsDirty = true;

    public void MarkSaved()
    {
        EndGesture();
        _history.Clear();
        IsDirty = false;
    }

    public void Load(PaintPart part, ReadOnlySpan<byte> pixels)
    {
        _surfaces[part].Replace(pixels);
        _history.Clear();
        IsDirty = false;
    }

    private void Restore(PaintCommand command, bool useAfter)
    {
        foreach (PaintPatch patch in command.Patches)
            _surfaces[patch.Part].Restore(patch.Rectangle, useAfter ? patch.After : patch.Before);
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
