using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

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

    public const double BaseCameraSize = 400.0;
    private const double SprayPulseSeconds = 0.05;
    private const int MaximumSprayCatchUpPulses = 4;
    private const float ScreenStepPixels = 1.5f;
    private const int MaxScreenSteps = 512;

    public PaintWorkspace Workspace { get; } = new();
    public PaintViewState View { get; } = new();
    public PaintPart? HoveredPart { get; private set; }
    public PaintPart? ActivePartFilter { get; set; }
    public bool PanToolActive { get; set; }
    public bool EyedropperToolActive { get; set; }
    public Vector2 ViewportSize { get; set; } = new(420, 360);

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
                    PaintAlongTo(motion.Position);
            }
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (input is InputEventMouseButton button)
        {
            _lastPointer = button.Position;
            bool leftPan = button.ButtonIndex == MouseButton.Left &&
                (PanToolActive || Input.IsKeyPressed(Key.Space));
            if (button.ButtonIndex == MouseButton.Middle || leftPan)
            {
                _panning = button.Pressed;
                if (_panning) GrabFocus();
                AcceptEvent();
                return;
            }

            if (button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed)
                {
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

                    Workspace.BeginGesture(hit);
                    _painting = true;
                    _sprayPulseAccumulator = 0;
                    _strokePointer = button.Position;
                    Input.UseAccumulatedInput = false;
                    WorkspaceChanged?.Invoke();
                    GrabFocus();
                }
                else if (_painting)
                {
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
                    PaintPoint focus = CanvasToWorld(button.Position) * (2.0 / BaseCameraSize);
                    View.SetZoom(View.Zoom + (direction * 0.2), focus);
                    ViewChanged?.Invoke();
                }
                else if (!EyedropperToolActive)
                {
                    Workspace.AdjustBrush(direction);
                    WorkspaceChanged?.Invoke();
                }
                QueueRedraw();
                AcceptEvent();
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!_painting) return;
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
            Workspace.ContinueGesture(Map(GetLocalMousePosition()));
            WorkspaceChanged?.Invoke();
        }
        if (pulses >= MaximumSprayCatchUpPulses)
            _sprayPulseAccumulator = 0;
    }

    private void PaintAlongTo(Vector2 canvas)
    {
        Vector2 from = _strokePointer;
        float spacing = Workspace.SelectedTool == PaintTool.Spray
            ? Math.Max(3f, VisibleBrushDiameter() * 0.4f)
            : ScreenStepPixels;
        int steps = Math.Clamp((int)Math.Ceiling(from.DistanceTo(canvas) / spacing), 1, MaxScreenSteps);
        for (int step = 1; step <= steps; step++)
            Workspace.ContinueGesture(Map(from.Lerp(canvas, step / (float)steps)));

        _strokePointer = canvas;
        WorkspaceChanged?.Invoke();
    }

    public override void _ExitTree()
    {
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
        if (input is not InputEventKey { Pressed: true, CtrlPressed: true } key) return;

        bool handled = false;
        if (key.Keycode == Key.Z && !key.ShiftPressed)
            handled = Workspace.Undo();
        else if (key.Keycode == Key.Y || (key.Keycode == Key.Z && key.ShiftPressed))
            handled = Workspace.Redo();

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
        PaintCursorGizmos.DrawBrushRing(this, _lastPointer, VisibleBrushDiameter());
    }

    private float VisibleBrushDiameter() => (float)(Workspace.BrushDiameter * View.Zoom * Math.Min(Size.X, Size.Y) /
        (PaintPolicy.SurfaceSize * 2.0));

    public void ResetView()
    {
        View.Reset();
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    public void SetPanNormalized(double x, double y)
    {
        PaintPoint current = View.Pan;
        View.PanBy(new PaintPoint(x - current.X, y - current.Y));
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    public void SetPartVisible(PaintPart part, bool visible)
    {
        if (visible) _hiddenParts.Remove(part);
        else _hiddenParts.Add(part);
        if (!visible && HoveredPart == part) SetHover(null);
        QueueRedraw();
    }

    public bool IsPartVisible(PaintPart part) => !_hiddenParts.Contains(part);

    public void ShowAllParts()
    {
        if (_hiddenParts.Count == 0) return;
        _hiddenParts.Clear();
        QueueRedraw();
    }

    public PaintPart? PartAt(Vector2 canvasPosition) => Map(canvasPosition)?.Part;

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
