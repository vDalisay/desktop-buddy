using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public enum BuddyPaintCurvePhase
{
    Idle,
    BaselineDragging,
    AwaitFirstBend,
    FirstBendDragging,
    AwaitSecondBend,
    SecondBendDragging,
}

/// <summary>Transparent input layer over the physics-free character preview.</summary>
public partial class PaintCanvasControl : Control
{
    private readonly FrontalPaintMapper _mapper = FrontalPaintMapper.CreateDefault();
    private readonly HashSet<PaintPart> _hiddenParts = [];
    private CharacterEditorHost? _host;
    private bool _painting;
    private bool _panning;
    private Vector2 _lastPointer;
    private Vector2 _strokePointer;
    private bool _sampling;
    private PaintColor _sampledColor;
    private double _sprayPulseAccumulator;
    private PaintStrokeAudio _strokeAudio = null!;

    private BuddyPaintCurvePhase _curvePhase;
    private PaintPoint _curveStart;
    private PaintPoint _curveEnd;
    private CubicPaintCurve _curve;
    private PaintCurveBend _firstCurveBend;
    private PaintCurveBend _previewCurveBend;
    private double _activeCurveBendT;

    public const double BaseCameraSize = 400.0;
    private const double SprayPulseSeconds = 0.05;
    private const int MaximumSprayCatchUpPulses = 4;
    private const float ScreenStepPixels = 1.5f;
    private const int MaxScreenSteps = 512;
    private const int MaxPenSampleSteps = 32;
    private const int MaxGeneratedReplacementPenSampleSteps = 8;
    /// <summary>Generated replacement parts map through real triangles, so the dot budget
    /// per pulse is capped there the way the pen's grid already is.</summary>
    private const int MaxGeneratedReplacementSprayDots = 192;
    private const double MinimumCurveBaselinePixels = 2.0;
    private const double SecondCurveBendSensitivity = 0.35;

    public PaintWorkspace Workspace { get; } = new();
    public PaintViewState View { get; } = new();
    public PaintPart? HoveredPart { get; private set; }
    public PaintPart? ActivePartFilter { get; set; }
    public bool PanToolActive { get; set; }
    public bool EyedropperToolActive { get; set; }
    public Vector2 ViewportSize { get; set; } = new(420, 360);
    public BuddyPaintCurvePhase CurvePhase => _curvePhase;
    public bool CurvePending => _curvePhase != BuddyPaintCurvePhase.Idle;

    public PaintPoint CameraCenter => new(
        View.Pan.X * (BaseCameraSize / 2.0),
        View.Pan.Y * (BaseCameraSize / 2.0));

    public event Action? WorkspaceChanged;
    public event Action? ViewChanged;
    public event Action<PaintPart?>? HoverChanged;
    public event Action<PaintColor>? ColorSampled;

    public override async void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
        _strokeAudio = new PaintStrokeAudio { Name = nameof(PaintStrokeAudio) };
        AddChild(_strokeAudio);
        Node? ancestor = GetParent();
        while (ancestor is not null && ancestor is not CharacterEditorHost)
            ancestor = ancestor.GetParent();
        if (ancestor is CharacterEditorHost host)
        {
            _host = host;
            await host.AttachPaintSessionAsync(this);
            WorkspaceChanged?.Invoke();
        }
    }

    /// <summary>Single tool-selection seam so changing away from Curve always cancels its preview.</summary>
    public void SelectPaintTool(PaintTool tool)
    {
        if (Workspace.SelectedTool == tool) return;
        CancelCurve();
        PanToolActive = false;
        EyedropperToolActive = false;
        Workspace.SelectedTool = tool;
        WorkspaceChanged?.Invoke();
        QueueRedraw();
    }

    public bool CancelCurve()
    {
        bool hadCurve = CurvePending || Workspace.PreviewActive;
        if (!hadCurve) return false;
        Workspace.CancelPreviewTransaction();
        ClearCurveState();
        if (_painting)
        {
            _painting = false;
            Input.UseAccumulatedInput = true;
        }
        WorkspaceChanged?.Invoke();
        QueueRedraw();
        return true;
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseMotion motion)
        {
            _lastPointer = motion.Position;
            if (_panning)
            {
                View.PanBy(new PaintPoint(
                    -motion.Relative.X / Math.Max(1.0, Size.X),
                    -motion.Relative.Y / Math.Max(1.0, Size.Y)));
                ViewChanged?.Invoke();
            }
            else
            {
                PaintHit? hit = Map(motion.Position);
                SetHover(hit?.Part);
                if (_sampling && hit is PaintHit sampleHit && TrySample(sampleHit, out PaintColor sampled))
                {
                    _sampledColor = sampled;
                    Workspace.SelectedColor = sampled;
                    ColorSampled?.Invoke(sampled);
                    WorkspaceChanged?.Invoke();
                }
                if (_painting)
                {
                    if (TryBucketFillConnector(hit))
                    {
                        // Connector painting intentionally collapses to a bucket-fill action.
                    }
                    else if (Workspace.SelectedTool == PaintTool.Curve)
                        ContinueCurve(motion.Position);
                    else if (!UsesScreenDabs(Workspace.SelectedTool))
                        PaintAlongTo(motion.Position);
                }
            }
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (input is InputEventMouseButton button)
        {
            _lastPointer = button.Position;

            if (button.ButtonIndex == MouseButton.Right && button.Pressed && CurvePending)
            {
                CancelCurve();
                AcceptEvent();
                return;
            }

            bool leftPan = button.ButtonIndex == MouseButton.Left &&
                (PanToolActive || Input.IsKeyPressed(Key.Space));
            if (button.ButtonIndex == MouseButton.Middle || leftPan)
            {
                if (button.Pressed && CurvePending) CancelCurve();
                _panning = button.Pressed;
                if (_panning) GrabFocus();
                AcceptEvent();
                return;
            }

            if (button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed)
                {
                    LatchPaintPrimaryHeld();
                    PaintHit? hit = Map(button.Position);
                    if (EyedropperToolActive)
                    {
                        if (hit is PaintHit sampleHit && TrySample(sampleHit, out PaintColor sampled))
                        {
                            _sampling = true;
                            _sampledColor = sampled;
                            Workspace.SelectedColor = sampled;
                            ColorSampled?.Invoke(sampled);
                            WorkspaceChanged?.Invoke();
                            QueueRedraw();
                        }
                        GrabFocus();
                        AcceptEvent();
                        return;
                    }

                    if (TryBucketFillConnector(hit))
                    {
                        GrabFocus();
                        AcceptEvent();
                        return;
                    }

                    if (Workspace.SelectedTool == PaintTool.Curve)
                    {
                        BeginCurve(button.Position);
                        GrabFocus();
                        AcceptEvent();
                        return;
                    }

                    // Screen-dab tools and the spray open the gesture without a hit: their marks
                    // are placed from screen-space samples below, not stamped from this one.
                    bool screenPlaced = UsesScreenDabs(Workspace.SelectedTool) ||
                        Workspace.SelectedTool == PaintTool.Spray;
                    Workspace.BeginGesture(screenPlaced ? null : hit);
                    if (UsesScreenDabs(Workspace.SelectedTool))
                        PaintScreenDab(button.Position, Workspace.SelectedTool == PaintTool.Eraser);
                    else if (Workspace.SelectedTool == PaintTool.Spray)
                        PaintSprayDab(button.Position);
                    _painting = true;
                    _sprayPulseAccumulator = 0;
                    _strokePointer = button.Position;
                    Input.UseAccumulatedInput = true;
                    WorkspaceChanged?.Invoke();
                    GrabFocus();
                }
                else if (_painting && Workspace.SelectedTool == PaintTool.Curve)
                {
                    EndCurve(button.Position);
                }
                else if (_painting)
                {
                    if (UsesScreenDabs(Workspace.SelectedTool))
                        PaintAlongTo(button.Position);
                    Workspace.EndGesture();
                    _painting = false;
                    _sprayPulseAccumulator = 0;
                    Input.UseAccumulatedInput = true;
                    WorkspaceChanged?.Invoke();
                }
                else if (EyedropperToolActive)
                {
                    _sampling = false;
                    QueueRedraw();
                }
                AcceptEvent();
                return;
            }

            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                int direction = button.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                if (Input.IsKeyPressed(Key.Ctrl))
                {
                    if (CurvePending) CancelCurve();
                    PaintPoint focus = CanvasToWorld(button.Position) * (2.0 / BaseCameraSize);
                    View.SetZoom(View.Zoom + (direction * 0.2), focus);
                    ViewChanged?.Invoke();
                }
                else if (!EyedropperToolActive)
                {
                    Workspace.AdjustBrush(direction);
                    if (CurvePending) RenderCurvePreview();
                    WorkspaceChanged?.Invoke();
                }
                QueueRedraw();
                AcceptEvent();
            }
        }
    }

    public override void _Process(double delta)
    {
        _strokeAudio.Set(_painting ? PaintStrokeAudio.For(Workspace.SelectedTool) : PaintStrokeSound.None);
        if (!_painting || Workspace.SelectedTool == PaintTool.Curve) return;
        if (Workspace.SelectedTool != PaintTool.Spray)
        {
            PaintAlongTo(GetLocalMousePosition());
            return;
        }

        _sprayPulseAccumulator += Math.Max(0, delta);
        int pulses = 0;
        while (_sprayPulseAccumulator >= SprayPulseSeconds && pulses++ < MaximumSprayCatchUpPulses)
        {
            _sprayPulseAccumulator -= SprayPulseSeconds;
            Vector2 sprayPointer = GetLocalMousePosition();
            if (TryBucketFillConnector(Map(sprayPointer)))
                return;
            PaintSprayDab(sprayPointer);
            WorkspaceChanged?.Invoke();
        }
        if (pulses >= MaximumSprayCatchUpPulses)
            _sprayPulseAccumulator = 0;
    }

    private void PaintAlongTo(Vector2 canvas)
    {
        Vector2 from = _strokePointer;
        if (from.IsEqualApprox(canvas))
            return;
        if (UsesScreenDabs(Workspace.SelectedTool))
        {
            bool square = Workspace.SelectedTool == PaintTool.Eraser;
            float distance = from.DistanceTo(canvas);
            float penSpacing = Math.Max(2f, VisibleBrushDiameter() * 0.35f);
            int penSteps = Math.Clamp((int)Math.Floor(distance / penSpacing), 0, MaxScreenSteps);
            if (penSteps == 0) return;
            for (int step = 1; step <= penSteps; step++)
            {
                Vector2 point = from.Lerp(canvas, Math.Min(1f, step * penSpacing / distance));
                PaintHit? hit = Map(point);
                if (TryBucketFillConnector(hit))
                    return;
                PaintScreenDab(point, square);
            }
            _strokePointer = from.Lerp(canvas, Math.Min(1f, penSteps * penSpacing / distance));
            WorkspaceChanged?.Invoke();
            return;
        }
        float spacing = StrokeSampleSpacing(Workspace.SelectedTool, VisibleBrushDiameter());
        int steps = Math.Clamp((int)Math.Ceiling(from.DistanceTo(canvas) / spacing), 1, MaxScreenSteps);
        for (int step = 1; step <= steps; step++)
        {
            Vector2 point = from.Lerp(canvas, step / (float)steps);
            PaintHit? hit = Map(point);
            if (TryBucketFillConnector(hit))
                return;
            Workspace.ContinueGesture(hit);
        }

        _strokePointer = canvas;
        WorkspaceChanged?.Invoke();
    }

    /// <summary>
    /// Whether the tool builds its footprint from screen-space samples rather than stamping one
    /// shape onto the surface. Those tools land as exactly the outline their cursor draws; the
    /// brush deliberately does not, and stays the ellipse the owner wants (2026-08-19).
    /// </summary>
    internal static bool UsesScreenDabs(PaintTool tool) => tool is PaintTool.Pen or PaintTool.Eraser;

    private void PaintPenDab(Vector2 center) =>
        PaintScreenDab(center, Workspace.SelectedTool == PaintTool.Eraser);

    /// <summary>
    /// Lays the footprint down as a grid of small dabs placed in screen space, each mapped to
    /// the surface on its own. <paramref name="square"/> selects the eraser's square block over
    /// the pen's round nib.
    /// </summary>
    private void PaintScreenDab(Vector2 center, bool square)
    {
        float radius = VisibleBrushDiameter() * 0.5f;
        float texturePixelSize = VisibleBrushDiameter() / Math.Max(1, Workspace.BrushDiameter);
        float sampleRadius = Math.Max(0f, radius - (texturePixelSize * PaintPolicy.MinBrushDiameter * 0.5f));
        int steps = PenSampleSteps(VisibleBrushDiameter(), Workspace.BrushDiameter);
        if (GodotObject.IsInstanceValid(_host?.PreviewRig) && _host!.PreviewRig.HasGeneratedReplacementPaintParts)
            steps = Math.Min(steps, MaxGeneratedReplacementPenSampleSteps);
        int sampleDiameter = PenSampleDiameter(VisibleBrushDiameter(), Workspace.BrushDiameter, steps);
        float spacing = steps <= 0 ? 0f : sampleRadius / steps;
        var hits = new List<PaintHit>((steps * 2 + 1) * (steps * 2 + 1));
        for (int y = -steps; y <= steps; y++)
        {
            for (int x = -steps; x <= steps; x++)
            {
                Vector2 offset = new(x * spacing, y * spacing);
                if (square
                    ? Math.Abs(offset.X) > sampleRadius || Math.Abs(offset.Y) > sampleRadius
                    : offset.LengthSquared() > sampleRadius * sampleRadius)
                {
                    continue;
                }
                if (Map(center + offset) is PaintHit hit)
                    hits.Add(hit);
            }
        }
        Workspace.StampScreenDab(
            hits,
            sampleDiameter,
            square ? PaintTool.Eraser : PaintTool.Pen);
    }

    /// <summary>
    /// One spray pulse, scattered across the cursor's screen-space disk and mapped dot by dot,
    /// so the dusted envelope is the circle the outline promises wherever it lands. Density is
    /// defined against the on-screen envelope, which is where the player judges it.
    /// </summary>
    private void PaintSprayDab(Vector2 center)
    {
        float radius = VisibleBrushDiameter() * 0.5f;
        if (radius <= 0f)
            return;

        int count = SprayPattern.PointCountForDiameter(Mathf.RoundToInt(VisibleBrushDiameter()));
        if (GodotObject.IsInstanceValid(_host?.PreviewRig) && _host!.PreviewRig.HasGeneratedReplacementPaintParts)
            count = Math.Min(count, MaxGeneratedReplacementSprayDots);

        PaintPoint[] offsets = SprayPattern.SampleUnitDisk(Workspace.NextSprayPulseSeed(), count);
        var hits = new List<PaintHit>(offsets.Length);
        foreach (PaintPoint offset in offsets)
        {
            var point = new Vector2(
                center.X + ((float)offset.X * radius),
                center.Y + ((float)offset.Y * radius));
            if (Map(point) is PaintHit hit)
                hits.Add(hit);
        }
        Workspace.StampScreenDots(hits);
    }

    private bool TryBucketFillConnector(PaintHit? hit)
    {
        if (!UsesConnectorBucketFill(Workspace.SelectedTool, hit) || hit is not PaintHit connector)
            return false;

        Workspace.BucketFill(connector);
        _painting = false;
        _sprayPulseAccumulator = 0;
        Input.UseAccumulatedInput = true;
        WorkspaceChanged?.Invoke();
        QueueRedraw();
        return true;
    }

    internal static bool UsesConnectorBucketFill(PaintTool tool, PaintHit? hit) =>
        hit is PaintHit { IsConnector: true } && tool is
            PaintTool.Brush or PaintTool.Pen or PaintTool.Spray or PaintTool.Fill or PaintTool.Curve;

    internal static float StrokeSampleSpacing(PaintTool tool, float visibleBrushDiameter) =>
        tool == PaintTool.Spray
            ? Math.Max(3f, visibleBrushDiameter * 0.4f)
            : Math.Max(ScreenStepPixels, visibleBrushDiameter * (float)PaintSurface.StampSpacingFactor);

    internal static int PenSampleSteps(float visibleBrushDiameter, int brushDiameter)
    {
        float texturePixelSize = visibleBrushDiameter / Math.Max(1, brushDiameter);
        float sampleRadius = Math.Max(
            0f,
            visibleBrushDiameter * 0.5f - texturePixelSize * PaintPolicy.MinBrushDiameter * 0.5f);
        float spacing = Math.Max(1.25f, texturePixelSize * PaintPolicy.MinBrushDiameter * 0.5f);
        return Math.Clamp((int)Math.Ceiling(sampleRadius / spacing), 1, MaxPenSampleSteps);
    }

    internal static int PenSampleDiameter(float visibleBrushDiameter, int brushDiameter, int sampleSteps)
    {
        float texturePixelSize = visibleBrushDiameter / Math.Max(1, brushDiameter);
        float sampleRadius = Math.Max(
            0f,
            visibleBrushDiameter * 0.5f - texturePixelSize * PaintPolicy.MinBrushDiameter * 0.5f);
        float actualSpacing = sampleRadius / Math.Max(1, sampleSteps);
        float denseSpacing = Math.Max(0.25f, texturePixelSize * PaintPolicy.MinBrushDiameter * 0.25f);
        float spacingRatio = actualSpacing / denseSpacing;
        int diameter = spacingRatio <= 1f
            ? PaintPolicy.MinBrushDiameter
            : (int)Math.Ceiling(PaintPolicy.MinBrushDiameter * spacingRatio * 1.5f);
        return Math.Clamp(diameter, PaintPolicy.MinBrushDiameter, brushDiameter);
    }

    private void BeginCurve(Vector2 canvas)
    {
        PaintPoint pointer = CanvasPoint(canvas);
        switch (_curvePhase)
        {
            case BuddyPaintCurvePhase.Idle:
                Workspace.BeginPreviewTransaction();
                _curveStart = pointer;
                _curveEnd = pointer;
                _curve = ClassicCurveGeometry.Straight(pointer, pointer);
                _curvePhase = BuddyPaintCurvePhase.BaselineDragging;
                break;
            case BuddyPaintCurvePhase.AwaitFirstBend:
                _activeCurveBendT = ClassicCurveGeometry.ClosestParameter(_curve, pointer);
                _previewCurveBend = new PaintCurveBend(_activeCurveBendT, pointer);
                _curvePhase = BuddyPaintCurvePhase.FirstBendDragging;
                break;
            case BuddyPaintCurvePhase.AwaitSecondBend:
                _activeCurveBendT = ClassicCurveGeometry.ClosestParameter(_curve, pointer);
                _previewCurveBend = new PaintCurveBend(_activeCurveBendT, pointer);
                _curvePhase = BuddyPaintCurvePhase.SecondBendDragging;
                break;
            default:
                return;
        }

        _painting = true;
        Input.UseAccumulatedInput = true;
        ContinueCurve(canvas);
        WorkspaceChanged?.Invoke();
    }

    private void ContinueCurve(Vector2 canvas)
    {
        if (!_painting || !Workspace.PreviewActive) return;
        PaintPoint pointer = CanvasPoint(canvas);
        switch (_curvePhase)
        {
            case BuddyPaintCurvePhase.BaselineDragging:
                _curveEnd = pointer;
                _curve = ClassicCurveGeometry.Straight(_curveStart, _curveEnd);
                break;
            case BuddyPaintCurvePhase.FirstBendDragging:
                _previewCurveBend = new PaintCurveBend(_activeCurveBendT, pointer);
                _curve = ClassicCurveGeometry.BendOnce(_curveStart, _curveEnd, _previewCurveBend);
                break;
            case BuddyPaintCurvePhase.SecondBendDragging:
                _previewCurveBend = new PaintCurveBend(_activeCurveBendT, pointer);
                CubicPaintCurve firstBendCurve = ClassicCurveGeometry.BendOnce(
                    _curveStart, _curveEnd, _firstCurveBend);
                PaintCurveBend softened = ClassicCurveGeometry.ScaleBendMovement(
                    firstBendCurve, _previewCurveBend, SecondCurveBendSensitivity);
                _curve = ClassicCurveGeometry.BendTwice(
                    _curveStart, _curveEnd, _firstCurveBend, softened);
                break;
        }
        RenderCurvePreview();
    }

    private void EndCurve(Vector2 canvas)
    {
        if (!_painting) return;
        ContinueCurve(canvas);
        _painting = false;
        Input.UseAccumulatedInput = true;

        switch (_curvePhase)
        {
            case BuddyPaintCurvePhase.BaselineDragging:
                if ((_curveEnd - _curveStart).Length < MinimumCurveBaselinePixels)
                {
                    CancelCurve();
                    return;
                }
                _curvePhase = BuddyPaintCurvePhase.AwaitFirstBend;
                break;
            case BuddyPaintCurvePhase.FirstBendDragging:
                _firstCurveBend = _previewCurveBend;
                _curvePhase = BuddyPaintCurvePhase.AwaitSecondBend;
                break;
            case BuddyPaintCurvePhase.SecondBendDragging:
                Workspace.FinalizePreviewTransaction();
                ClearCurveState();
                break;
        }
        WorkspaceChanged?.Invoke();
        QueueRedraw();
    }

    private void RenderCurvePreview()
    {
        if (!Workspace.PreviewActive) return;
        double spacing = Math.Max(0.75, Math.Min(2.0, VisibleBrushDiameter() * 0.2));
        IReadOnlyList<PaintPoint> points = ClassicCurveGeometry.Sample(_curve, spacing);
        var hits = new PaintHit?[points.Count];
        for (int index = 0; index < points.Count; index++)
            hits[index] = Map(new Vector2((float)points[index].X, (float)points[index].Y));
        Workspace.RenderPreviewPath(hits);
        WorkspaceChanged?.Invoke();
    }

    private void ClearCurveState()
    {
        _curvePhase = BuddyPaintCurvePhase.Idle;
        _curveStart = default;
        _curveEnd = default;
        _curve = default;
        _firstCurveBend = default;
        _previewCurveBend = default;
        _activeCurveBendT = 0;
    }

    private static PaintPoint CanvasPoint(Vector2 point) => new(point.X, point.Y);

    public override void _ExitTree()
    {
        if (CurvePending) CancelCurve();
        if (_painting)
        {
            Workspace.EndGesture();
            _painting = false;
            Input.UseAccumulatedInput = true;
        }
        _sprayPulseAccumulator = 0;
        _hiddenParts.Clear();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && CurvePending)
        {
            CancelCurve();
            AcceptEvent();
            return;
        }

        if (input is not InputEventKey { Pressed: true, CtrlPressed: true } key) return;

        bool handled = false;
        if (key.Keycode == Key.Z && !key.ShiftPressed)
        {
            if (CurvePending)
            {
                CancelCurve();
                handled = true;
            }
            else handled = Workspace.Undo();
        }
        else if (key.Keycode == Key.Y || (key.Keycode == Key.Z && key.ShiftPressed))
        {
            if (CurvePending)
            {
                CancelCurve();
                handled = true;
            }
            else handled = Workspace.Redo();
        }

        if (!handled) return;
        WorkspaceChanged?.Invoke();
        AcceptEvent();
    }

    public override void _Draw()
    {
        if (EyedropperToolActive)
        {
            if (_sampling)
            {
                Color color = Color.Color8(_sampledColor.R, _sampledColor.G, _sampledColor.B);
                PaintCursorGizmos.DrawPickPreview(this, _lastPointer, color);
            }
            return;
        }
        if (PanToolActive) return;
        float diameter = VisibleBrushDiameter();
        var footprint = new Vector2(diameter, diameter * (float)PaintWorkspace.BrushVerticalScale);
        PaintCursorGizmos.DrawBrushCursor(
            this,
            _lastPointer,
            footprint,
            PaintCursorGizmos.ShapeFor(Workspace.SelectedTool));
    }

    private float VisibleBrushDiameter() => (float)(Workspace.BrushDiameter * View.Zoom * Math.Min(Size.X, Size.Y) /
        (PaintPolicy.SurfaceSize * 2.0));

    public void ResetView()
    {
        if (CurvePending) CancelCurve();
        View.Reset();
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    public void SetPanNormalized(double x, double y)
    {
        if (CurvePending) CancelCurve();
        PaintPoint current = View.Pan;
        View.PanBy(new PaintPoint(x - current.X, y - current.Y));
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    public void SetPartVisible(PaintPart part, bool visible)
    {
        if (CurvePending) CancelCurve();
        if (visible) _hiddenParts.Remove(part);
        else _hiddenParts.Add(part);
        if (!visible && HoveredPart == part) SetHover(null);
        QueueRedraw();
    }

    public bool IsPartVisible(PaintPart part) => !_hiddenParts.Contains(part);

    public void ShowAllParts()
    {
        if (_hiddenParts.Count == 0) return;
        if (CurvePending) CancelCurve();
        _hiddenParts.Clear();
        QueueRedraw();
    }

    public PaintPart? PartAt(Vector2 canvasPosition) => Map(canvasPosition)?.Part;
    public PaintHit? HitAt(Vector2 canvasPosition) => Map(canvasPosition);

    private bool TrySample(PaintHit hit, out PaintColor color)
    {
        if (Workspace.Surfaces.TryGetValue(hit.Part, out PaintSurface? surface))
            return surface.TrySample(hit.Uv, out color);
        color = default;
        return false;
    }

    private PaintHit? Map(Vector2 canvas)
    {
        PaintPoint point = CanvasToWorld(canvas);
        double yaw = GodotObject.IsInstanceValid(_host) && GodotObject.IsInstanceValid(_host!.PreviewRig)
            ? Mathf.DegToRad(_host.PreviewRig.RotationDegrees.Y)
            : 0.0;

        PaintHit? hit;
        if (Math.Abs(yaw) <= 0.000001)
            hit = _mapper.TryMap(point, out PaintHit frontal) ? frontal : null;
        else
            hit = TryMapRotated(point, yaw, out PaintHit rotated) ? rotated : null;

        bool generatedReplacementHit = false;
        if (GodotObject.IsInstanceValid(_host?.PreviewRig) && _host!.PreviewRig.IsInitialized)
        {
            // A replaced part must not keep accepting paint through its now-hidden legacy
            // sphere/capsule. Ask the visible generated triangles for the exact UV instead.
            if (hit is PaintHit legacy && _host.PreviewRig.IsGeneratedReplacementPaintPart(legacy.Part))
                hit = null;

            var worldPoint = new Vector2((float)point.X, (float)point.Y);
            if (_host.PreviewRig.TryMapGeneratedReplacementPaintHit(worldPoint, out PaintHit generated) &&
                (hit is null || generated.Depth > hit.Value.Depth))
            {
                hit = generated;
                generatedReplacementHit = true;
            }
        }

        // Whatever is under the cursor is what gets painted. The connectors between the torso
        // and each limb are drawn in every pose, but they only accepted paint while Show limbs
        // was ticked, so the neck and the gaps beside the hands stayed the base colour no matter
        // how carefully they were clicked (owner report 2026-08-23).
        if (hit is null && TryMapLimbConnector(point, yaw, out PaintHit connector))
            hit = connector;

        // Generated foot hits already map their mesh UV into the limb-end half of the persistent
        // foot paint atlas. Legacy sphere hits still require the historical lane transform here.
        if (!generatedReplacementHit && hit is PaintHit limb && PaintUvRegion.IsLimb(limb.Part) && !limb.IsConnector)
            hit = limb with { Uv = PaintUvRegion.LimbEnd.MapLocal(limb.Uv) };

        return hit is PaintHit valid && IsPartVisible(valid.Part) &&
            (ActivePartFilter is null || valid.Part == ActivePartFilter.Value) ? valid : null;
    }

    private bool TryMapRotated(PaintPoint point, double yaw, out PaintHit hit)
    {
        double cos = Math.Cos(yaw);
        double sin = Math.Sin(yaw);
        bool found = false;
        PaintHit nearest = default;
        double nearestDepth = double.NegativeInfinity;

        foreach (PaintPartShape shape in _mapper.Shapes)
        {
            if (!IsPartVisible(shape.Part)) continue;

            double centerScreenX = shape.Center.X * cos;
            double centerDepth = (-shape.Center.X * sin) + (shape.Depth * cos);
            double screenX = point.X - centerScreenX;
            double yUp = -(point.Y - shape.Center.Y);

            if (!TryMapRotatedShape(shape, screenX, yUp, cos, sin, out PaintPoint uv, out double surfaceDepth))
                continue;

            double depth = centerDepth + surfaceDepth;
            if (found && depth <= nearestDepth) continue;
            nearest = new PaintHit(shape.Part, uv, depth);
            nearestDepth = depth;
            found = true;
        }

        hit = nearest;
        return found;
    }

    private static bool TryMapRotatedShape(
        PaintPartShape shape,
        double screenX,
        double yUp,
        double cos,
        double sin,
        out PaintPoint uv,
        out double surfaceDepth)
    {
        uv = default;
        surfaceDepth = 0.0;
        double radius = shape.Radius;
        if (radius <= 0.0) return false;

        double v;
        if (shape.Kind == PaintShapeKind.Sphere)
        {
            double planar = (screenX * screenX) + (yUp * yUp);
            if (planar > radius * radius) return false;
            surfaceDepth = Math.Sqrt(Math.Max(0.0, (radius * radius) - planar));
            v = Math.Acos(Math.Clamp(yUp / radius, -1.0, 1.0)) / Math.PI;
        }
        else
        {
            double mid = shape.HalfHeight - radius;
            if (mid < 0.0 || Math.Abs(screenX) > radius || Math.Abs(yUp) > shape.HalfHeight) return false;

            if (Math.Abs(yUp) <= mid)
            {
                surfaceDepth = Math.Sqrt(Math.Max(0.0, (radius * radius) - (screenX * screenX)));
                double band = mid <= 0.0 ? 0.5 : (mid - yUp) / (2.0 * mid);
                v = OneThird + (band * OneThird);
            }
            else
            {
                double capOffset = Math.Abs(yUp) - mid;
                double planar = (screenX * screenX) + (capOffset * capOffset);
                if (planar > radius * radius) return false;
                surfaceDepth = Math.Sqrt(Math.Max(0.0, (radius * radius) - planar));
                double capFraction = 2.0 / Math.PI * (yUp > 0.0
                    ? Math.Acos(Math.Clamp(capOffset / radius, -1.0, 1.0))
                    : Math.Asin(Math.Clamp(capOffset / radius, -1.0, 1.0)));
                v = yUp > 0.0 ? capFraction * OneThird : (2.0 * OneThird) + (capFraction * OneThird);
            }
        }

        double localX = (screenX * cos) - (surfaceDepth * sin);
        double localZ = (screenX * sin) + (surfaceDepth * cos);
        double uOffset = shape.Kind == PaintShapeKind.Capsule ? 0.5 : 0.0;
        double u = Wrap(uOffset + (Math.Atan2(localX, localZ) / Tau));
        uv = new PaintPoint(u, Math.Clamp(v, 0.0, 1.0));
        return true;
    }

    private PaintPoint CanvasToWorld(Vector2 canvas)
    {
        double width = Math.Max(1.0, Size.X);
        double height = Math.Max(1.0, Size.Y);
        double aspect = width / height;
        double verticalSpan = BaseCameraSize / View.Zoom;
        PaintPoint center = CameraCenter;
        return new PaintPoint(
            center.X + (((canvas.X / width) - 0.5) * verticalSpan * aspect),
            center.Y + (((canvas.Y / height) - 0.5) * verticalSpan));
    }

    private void SetHover(PaintPart? part)
    {
        if (HoveredPart == part) return;
        HoveredPart = part;
        HoverChanged?.Invoke(part);
    }

    private const double Tau = Math.PI * 2.0;
    private const double OneThird = 1.0 / 3.0;

    private static double Wrap(double u)
    {
        double wrapped = u - Math.Floor(u);
        return wrapped >= 1.0 ? 0.0 : wrapped;
    }
}
