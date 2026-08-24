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
    public const double BrushVerticalScale = 0.5;
    /// <summary>
    /// The gesture's undo "before" image. The union rectangle grows as the gesture wanders, but
    /// the pre-gesture pixels are snapshotted exactly once: re-capturing the whole growing union
    /// on every stroke step made a big brush allocate and memcpy the surface hundreds of times
    /// per stroke, which is what the editor's paint lag actually was (owner report 2026-08-19).
    /// The cropped patch is produced on demand, and only the callers that need bytes pay for it.
    /// </summary>
    private sealed class GesturePatchBuilder
    {
        private const int GrowthBlock = 32;
        private static readonly PaintRect FullSurface =
            new(0, 0, PaintPolicy.SurfaceSize, PaintPolicy.SurfaceSize);

        private byte[]? _snapshot;
        private byte[]? _cropped;
        private PaintRect _croppedRectangle;

        public PaintRect Rectangle { get; private set; }

        public byte[] Before
        {
            get
            {
                if (Rectangle.IsEmpty || _snapshot is null)
                    return Array.Empty<byte>();
                if (_cropped is not null && _croppedRectangle == Rectangle)
                    return _cropped;

                byte[] cropped = new byte[Rectangle.ByteCount];
                CopyPatch(_snapshot, FullSurface, cropped, Rectangle);
                _cropped = cropped;
                _croppedRectangle = Rectangle;
                return cropped;
            }
        }

        public void Expand(PaintSurface surface, PaintRect candidate, byte[] snapshotScratch)
        {
            if (candidate.IsEmpty) return;
            int x = Math.Max(0, candidate.X / GrowthBlock * GrowthBlock);
            int y = Math.Max(0, candidate.Y / GrowthBlock * GrowthBlock);
            int right = Math.Min(
                PaintPolicy.SurfaceSize,
                (candidate.X + candidate.Width + GrowthBlock - 1) / GrowthBlock * GrowthBlock);
            int bottom = Math.Min(
                PaintPolicy.SurfaceSize,
                (candidate.Y + candidate.Height + GrowthBlock - 1) / GrowthBlock * GrowthBlock);
            PaintRect union = PaintRect.Union(Rectangle, new PaintRect(x, y, right - x, bottom - y));

            // The snapshot must predate every mutation of this gesture, so it is taken on the
            // first expansion and never refreshed. It writes into a workspace-owned buffer:
            // a fresh megabyte per stroke is a large-object-heap allocation, and those are
            // what drove the gen2 collections behind the worst paint frame spikes.
            if (_snapshot is null)
            {
                surface.CopyPixelsTo(snapshotScratch);
                _snapshot = snapshotScratch;
            }
            if (union == Rectangle) return;
            Rectangle = union;
        }

        /// <summary>Copies the sub-rectangle <paramref name="destinationRect"/> out of a source
        /// image covering <paramref name="sourceRect"/>, or a source patch into a larger one.</summary>
        private static void CopyPatch(
            ReadOnlySpan<byte> source,
            PaintRect sourceRect,
            Span<byte> destination,
            PaintRect destinationRect)
        {
            int sourceStride = sourceRect.Width * PaintPolicy.BytesPerPixel;
            int destinationStride = destinationRect.Width * PaintPolicy.BytesPerPixel;
            int sourceX = (destinationRect.X - sourceRect.X) * PaintPolicy.BytesPerPixel;
            int sourceY = destinationRect.Y - sourceRect.Y;
            for (int row = 0; row < destinationRect.Height; row++)
            {
                source.Slice(((sourceY + row) * sourceStride) + sourceX, destinationStride)
                    .CopyTo(destination.Slice(row * destinationStride, destinationStride));
            }
        }
    }

    private readonly Dictionary<PaintPart, PaintSurface> _surfaces =
        Enum.GetValues<PaintPart>().ToDictionary(part => part, _ => new PaintSurface());
    private readonly PaintUndoHistory _history = new();
    private readonly Dictionary<PaintPart, GesturePatchBuilder> _gestureBefore = new();
    private readonly Dictionary<PaintPart, GesturePatchBuilder> _previewBefore = new();
    private readonly Dictionary<PaintPart, byte[]> _snapshotScratch = new();
    // Pen/Eraser/Spray screen dabs are frequent and can contain hundreds of micro-hits. Reuse
    // their transient collections so a drag does not create two managed allocations per nib.
    private readonly List<PaintHit> _screenDabSamples = new();
    private readonly Dictionary<PaintPart, PaintRect> _screenDabBounds = new();
    private PaintHit? _lastHit;
    private bool _gestureActive;
    private bool _previewActive;
    private bool _mirrorEnabled;
    private bool _paintBacksideEnabled;
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

    /// <summary>
    /// Mirrors every mutation across the texture's front-facing vertical plane. The same U
    /// reflection works for the sphere and capsule UV conventions because their front axes are
    /// respectively U=0 and U=0.5, both fixed points of U -> 1-U modulo one.
    /// </summary>
    public bool MirrorEnabled
    {
        get => _mirrorEnabled;
        set
        {
            if (_mirrorEnabled == value) return;
            EndGesture();
            CancelPreviewTransaction();
            _mirrorEnabled = value;
        }
    }

    /// <summary>
    /// Repeats every mutation half a circumference away, so painting the visible side can also
    /// paint the corresponding backside. This is visual paint data only and never rotates or
    /// mutates the buddy physics rig.
    /// </summary>
    public bool PaintBacksideEnabled
    {
        get => _paintBacksideEnabled;
        set
        {
            if (_paintBacksideEnabled == value) return;
            EndGesture();
            CancelPreviewTransaction();
            _paintBacksideEnabled = value;
        }
    }

    public PaintColor SelectedColor { get; set; } = PaintColor.White;
    public int BrushDiameter { get; private set; } = PaintPolicy.DefaultBrushDiameter;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public long UndoMemoryBytes => _history.MemoryBytes;
    /// <summary>
    /// Raised whenever <see cref="IsDirty"/> flips. The editor's Save and Reset buttons are
    /// derived from it, and a paint stroke reaches them through no other route: the session's
    /// own Changed event covers the document, not the pixels, so without this a stroke left
    /// both buttons stuck disabled until some unrelated change refreshed the UI.
    /// </summary>
    public event Action? DirtyChanged;

    private bool _isDirty;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
                return;

            _isDirty = value;
            DirtyChanged?.Invoke();
        }
    }
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
    /// Flood-fills the selected region and every enabled mirror/backside counterpart on the same
    /// buddy surface. Horizontal neighbours wrap across the texture U seam while vertical
    /// neighbours clip at the poles. All counterpart fills form one exact undo command.
    /// </summary>
    public bool BucketFill(PaintHit? hit)
    {
        CancelPreviewTransaction();
        EndGesture();
        if (hit is not PaintHit valid || !valid.IsValid)
            return false;

        var beforeByPart = new Dictionary<PaintPart, byte[]>();
        var afterByPart = new Dictionary<PaintPart, byte[]>();
        ApplyToVariants(valid, variant =>
        {
            if (!afterByPart.TryGetValue(variant.Part, out byte[]? after))
            {
                byte[] before = _surfaces[variant.Part].ClonePixels();
                beforeByPart.Add(variant.Part, before);
                after = (byte[])before.Clone();
                afterByPart.Add(variant.Part, after);
            }
            FloodFillPixels(after, variant.Uv, SelectedColor, PaintUvRegion.For(variant));
        });

        PaintRect whole = new(0, 0, PaintPolicy.SurfaceSize, PaintPolicy.SurfaceSize);
        var patches = new List<PaintPatch>();
        foreach ((PaintPart part, byte[] after) in afterByPart)
        {
            byte[] before = beforeByPart[part];
            if (before.AsSpan().SequenceEqual(after)) continue;
            _surfaces[part].Replace(after);
            patches.Add(new PaintPatch(part, whole, before, after));
        }
        if (patches.Count == 0) return false;
        _history.Push(new PaintCommand(patches));
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

        PaintTool mutation = GestureMutation();
        ApplyToVariants(valid, variant => StampVariant(variant, mutation, _gestureBefore));
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

        PaintTool mutation = GestureMutation();
        if (_lastHit is PaintHit previous)
        {
            ApplyToVariantPairs(previous, current, (prior, next) =>
            {
                if (SameLane(prior, next) && IsBridgeable(prior.Uv, next.Uv, PaintUvRegion.For(prior)))
                    StrokeVariant(prior, next, mutation, _gestureBefore);
                else
                    StampVariant(next, mutation, _gestureBefore);
            });
        }
        else
        {
            ApplyToVariants(current, variant => StampVariant(variant, mutation, _gestureBefore));
        }
        _lastHit = current;
    }

    public void StampPenDab(
        IReadOnlyList<PaintHit> hits,
        int sampleDiameter = PaintPolicy.MinBrushDiameter) =>
        StampScreenDab(hits, sampleDiameter, PaintTool.Pen);

    /// <summary>
    /// Stamps one small dab per supplied hit. The caller chooses the hits in SCREEN space and
    /// maps each one, so the footprint they form lands as exactly the shape the cursor outline
    /// draws — however the surface is stretched, rotated or wrapped underneath it. Stamping a
    /// single big footprint onto the surface cannot do that: the distortion across a wide brush
    /// both varies and rotates, which is what made a sprayed circle come out as a tilted ellipse
    /// over the middle of the buddy (owner report 2026-08-19).
    /// </summary>
    public void StampScreenDab(
        IReadOnlyList<PaintHit> hits,
        int sampleDiameter,
        PaintTool mutation)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (!_gestureActive || hits.Count == 0)
            return;
        if (_selectedTool is not (PaintTool.Pen or PaintTool.Eraser or PaintTool.Spray))
            return;
        sampleDiameter = Math.Clamp(
            sampleDiameter,
            PaintPolicy.MinBrushDiameter,
            PaintPolicy.MaxBrushDiameter);

        _screenDabSamples.Clear();
        _screenDabBounds.Clear();
        foreach (PaintHit hit in hits)
        {
            if (!hit.IsValid) continue;
            _screenDabSamples.Add(TransformHit(hit, mirror: false, backside: false));
            if (_mirrorEnabled)
                _screenDabSamples.Add(TransformHit(hit, mirror: true, backside: false));
            if (_paintBacksideEnabled)
                _screenDabSamples.Add(TransformHit(hit, mirror: false, backside: true));
            if (_mirrorEnabled && _paintBacksideEnabled)
                _screenDabSamples.Add(TransformHit(hit, mirror: true, backside: true));
        }

        foreach (PaintHit sample in _screenDabSamples)
        {
            PaintUvRegion region = PaintUvRegion.For(sample);
            PaintRect bounds = PaintSurface.StampBounds(
                sample.Uv,
                sampleDiameter,
                region: region);
            _screenDabBounds[sample.Part] = _screenDabBounds.TryGetValue(sample.Part, out PaintRect prior)
                ? PaintRect.Union(prior, bounds)
                : bounds;
        }
        foreach ((PaintPart part, PaintRect bounds) in _screenDabBounds)
            CaptureBefore(_gestureBefore, part, bounds);
        foreach (PaintHit sample in _screenDabSamples)
        {
            PaintUvRegion region = PaintUvRegion.For(sample);
            _surfaces[sample.Part].Stamp(
                sample.Uv,
                sampleDiameter,
                mutation,
                SelectedColor,
                region: region);
        }
    }

    /// <summary>
    /// Puts one texel down per supplied hit — the spray pulse. The scatter is chosen in SCREEN
    /// space by the caller for the same reason <see cref="StampScreenDab"/> exists, but each dot
    /// is a single pixel, so the buddy's airbrush dusts exactly the way Paint Room's does
    /// (owner instruction 2026-08-23).
    /// </summary>
    public void StampScreenDots(IReadOnlyList<PaintHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (!_gestureActive || hits.Count == 0 || _selectedTool != PaintTool.Spray)
            return;

        var samples = new List<PaintHit>(hits.Count);
        foreach (PaintHit hit in hits)
        {
            if (hit.IsValid)
                ApplyToVariants(hit, samples.Add);
        }

        // Undo is captured per part over the union of the dots, before any of them lands: a
        // pulse is one gesture step whether it wrote three texels or three hundred.
        var boundsByPart = new Dictionary<PaintPart, PaintRect>();
        foreach (PaintHit sample in samples)
        {
            PaintUvRegion region = PaintUvRegion.For(sample);
            PaintRect bounds = PaintSurface.StampBounds(
                sample.Uv,
                PaintPolicy.MinBrushDiameter,
                region: region);
            boundsByPart[sample.Part] = boundsByPart.TryGetValue(sample.Part, out PaintRect prior)
                ? PaintRect.Union(prior, bounds)
                : bounds;
        }
        foreach ((PaintPart part, PaintRect bounds) in boundsByPart)
            CaptureBefore(_gestureBefore, part, bounds);
        foreach (PaintHit sample in samples)
            _surfaces[sample.Part].Dot(sample.Uv, SelectedColor, PaintUvRegion.For(sample));
    }

    /// <summary>The next spray pulse's seed, stepped exactly as the internal pulse does.</summary>
    public ulong NextSprayPulseSeed() =>
        _sprayGestureSeed + (_sprayPulseOrdinal++ * 0x9E3779B97F4A7C15UL);

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
        RenderPreviewLane(samples, mirror: false, backside: false);
        if (_mirrorEnabled)
            RenderPreviewLane(samples, mirror: true, backside: false);
        if (_paintBacksideEnabled)
            RenderPreviewLane(samples, mirror: false, backside: true);
        if (_mirrorEnabled && _paintBacksideEnabled)
            RenderPreviewLane(samples, mirror: true, backside: true);
    }

    private void RenderPreviewLane(IReadOnlyList<PaintHit?> samples, bool mirror, bool backside)
    {
        PaintHit? previous = null;
        foreach (PaintHit? sample in samples)
        {
            if (sample is not PaintHit source || !source.IsValid)
            {
                previous = null;
                continue;
            }

            PaintHit current = TransformHit(source, mirror, backside);
            if (previous is PaintHit prior && SameLane(prior, current) &&
                IsBridgeable(prior.Uv, current.Uv, PaintUvRegion.For(prior)))
                StrokeVariant(prior, current, PaintTool.Brush, _previewBefore);
            else
                StampVariant(current, PaintTool.Brush, _previewBefore);
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
        ulong seed = _sprayGestureSeed + (_sprayPulseOrdinal++ * 0x9E3779B97F4A7C15UL);
        ApplyToVariants(hit, variant =>
        {
            const double aspect = 1.0;
            PaintUvRegion region = PaintUvRegion.For(variant);
            PaintRect bounds = PaintSurface.StampBounds(variant.Uv, BrushDiameter, aspect, region);
            CaptureBefore(_gestureBefore, variant.Part, bounds);
            _surfaces[variant.Part].Spray(variant.Uv, BrushDiameter, SelectedColor, seed, aspect, region);
        });
    }

    private void StampVariant(
        PaintHit hit,
        PaintTool mutation,
        Dictionary<PaintPart, GesturePatchBuilder> builders)
    {
        double aspect = FootprintAspect(mutation);
        PaintUvRegion region = PaintUvRegion.For(hit);
        CaptureBefore(builders, hit.Part, PaintSurface.StampBounds(hit.Uv, BrushDiameter, aspect, region));
        _surfaces[hit.Part].Stamp(hit.Uv, BrushDiameter, mutation, SelectedColor, aspect, region);
    }

    private void StrokeVariant(
        PaintHit from,
        PaintHit to,
        PaintTool mutation,
        Dictionary<PaintPart, GesturePatchBuilder> builders)
    {
        double aspect = FootprintAspect(mutation);
        PaintUvRegion region = PaintUvRegion.For(to);
        CaptureBefore(builders, to.Part, PaintSurface.StrokeBounds(from.Uv, to.Uv, BrushDiameter, aspect, region));
        _surfaces[to.Part].Stroke(from.Uv, to.Uv, BrushDiameter, mutation, SelectedColor, aspect, region);
    }

    private void ApplyToVariants(PaintHit hit, Action<PaintHit> action)
    {
        action(TransformHit(hit, mirror: false, backside: false));
        if (_mirrorEnabled)
            action(TransformHit(hit, mirror: true, backside: false));
        if (_paintBacksideEnabled)
            action(TransformHit(hit, mirror: false, backside: true));
        if (_mirrorEnabled && _paintBacksideEnabled)
            action(TransformHit(hit, mirror: true, backside: true));
    }

    /// <summary>
    /// The footprint's height as a fraction of its width, in surface pixels, for the tools that
    /// stamp directly onto the surface. Only the brush is squashed — the owner wants the brush
    /// to stay an ellipse (2026-08-19). The pen, the eraser and the spray build their footprint
    /// from screen-space samples instead, so this is only their fallback when a caller drives
    /// the workspace directly rather than through the canvas.
    /// </summary>
    private static double FootprintAspect(PaintTool mutation) =>
        mutation is PaintTool.Pen or PaintTool.Eraser ? 1.0 : BrushVerticalScale;

    private PaintTool GestureMutation() => _selectedTool switch
    {
        PaintTool.Eraser => PaintTool.Eraser,
        PaintTool.Pen => PaintTool.Pen,
        _ => PaintTool.Brush,
    };

    private void ApplyToVariantPairs(PaintHit from, PaintHit to, Action<PaintHit, PaintHit> action)
    {
        action(TransformHit(from, mirror: false, backside: false), TransformHit(to, mirror: false, backside: false));
        if (_mirrorEnabled)
            action(TransformHit(from, mirror: true, backside: false), TransformHit(to, mirror: true, backside: false));
        if (_paintBacksideEnabled)
            action(TransformHit(from, mirror: false, backside: true), TransformHit(to, mirror: false, backside: true));
        if (_mirrorEnabled && _paintBacksideEnabled)
            action(TransformHit(from, mirror: true, backside: true), TransformHit(to, mirror: true, backside: true));
    }

    private static PaintHit TransformHit(PaintHit source, bool mirror, bool backside)
    {
        PaintPart part = source.Part;
        PaintUvRegion region = PaintUvRegion.For(source);
        double u = region.LocalU(source.Uv.X);
        if (backside) u = WrapUnit(u + 0.5);
        if (mirror)
        {
            part = MirroredPart(part);
            u = WrapUnit(1.0 - u);
        }
        return source with { Part = part, Uv = new PaintPoint(region.AtlasU(u), source.Uv.Y) };
    }

    private static PaintPart MirroredPart(PaintPart part) => part switch
    {
        PaintPart.LeftHand => PaintPart.RightHand,
        PaintPart.RightHand => PaintPart.LeftHand,
        PaintPart.LeftFoot => PaintPart.RightFoot,
        PaintPart.RightFoot => PaintPart.LeftFoot,
        _ => part,
    };

    private static double WrapUnit(double value)
    {
        double wrapped = value - Math.Floor(value);
        return wrapped >= 1.0 ? 0.0 : wrapped;
    }

    private static bool SameLane(PaintHit from, PaintHit to) =>
        from.Part == to.Part && from.IsConnector == to.IsConnector;

    private static bool IsBridgeable(PaintPoint from, PaintPoint to, PaintUvRegion region)
    {
        double dx = Math.Abs(to.X - from.X);
        if (dx > region.Width * 0.5) dx = region.Width - dx;
        dx /= region.Width;
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
        builder.Expand(_surfaces[part], rectangle, SnapshotScratch(part));
    }

    /// <summary>
    /// One reusable pre-gesture snapshot buffer per part. Gesture and preview transactions are
    /// mutually exclusive by construction (each begins by ending the other), so a single buffer
    /// per part is enough for both.
    /// </summary>
    private byte[] SnapshotScratch(PaintPart part)
    {
        if (!_snapshotScratch.TryGetValue(part, out byte[]? buffer))
        {
            buffer = new byte[PaintPolicy.SurfaceBytes];
            _snapshotScratch.Add(part, buffer);
        }
        return buffer;
    }

    private static bool FloodFillPixels(
        byte[] pixels,
        PaintPoint uv,
        PaintColor replacement,
        PaintUvRegion region)
    {
        if (!uv.IsFinite || uv.X < 0.0 || uv.X > 1.0 || uv.Y < 0.0 || uv.Y > 1.0)
            return false;

        int size = PaintPolicy.SurfaceSize;
        int startX = Math.Clamp((int)Math.Round(region.PixelX(uv.X)),
            region.StartPixel, region.StartPixel + region.PixelWidth - 1);
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
            int wrappedX = region.WrapPixelX(x);
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
