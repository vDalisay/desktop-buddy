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
    private readonly Dictionary<PaintPart, PaintSurface> _surfaces =
        Enum.GetValues<PaintPart>().ToDictionary(part => part, _ => new PaintSurface());
    private readonly PaintUndoHistory _undo = new();
    private readonly Dictionary<PaintPart, byte[]> _gestureBefore = new();
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
        CaptureBefore(hit.Part);
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
        CaptureBefore(current.Part);
        if (_lastHit is PaintHit previous && previous.Part == current.Part)
            _surfaces[current.Part].Stroke(previous.Uv, current.Uv, BrushDiameter, SelectedTool, SelectedColor);
        else
            _surfaces[current.Part].Stamp(current.Uv, BrushDiameter, SelectedTool, SelectedColor);
        _lastHit = current;
    }

    public void EndGesture()
    {
        if (!_gestureActive) return;
        List<PaintPatch> patches = new();
        PaintRect whole = new(0, 0, PaintPolicy.SurfaceSize, PaintPolicy.SurfaceSize);
        foreach ((PaintPart part, byte[] before) in _gestureBefore)
        {
            if (!before.AsSpan().SequenceEqual(_surfaces[part].Pixels.Span))
                patches.Add(new PaintPatch(part, whole, before));
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

    private void CaptureBefore(PaintPart part)
    {
        if (!_gestureBefore.ContainsKey(part))
            _gestureBefore[part] = _surfaces[part].ClonePixels();
    }
}
