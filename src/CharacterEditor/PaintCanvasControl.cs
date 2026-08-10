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

    /// <summary>Vertical world extent the preview's orthographic camera frames at zoom 1.</summary>
    public const double BaseCameraSize = 400.0;

    public PaintWorkspace Workspace { get; } = new();
    public PaintViewState View { get; } = new();
    public PaintPart? HoveredPart { get; private set; }
    public PaintPart? ActivePartFilter { get; set; }
    public bool PanToolActive { get; set; }
    public bool EyedropperToolActive { get; set; }

    /// <summary>
    /// Retained for source compatibility with existing painting scenarios. Runtime pointer
    /// mapping deliberately uses this control's live Size because the stretched preview adopts
    /// the same post-layout rectangle.
    /// </summary>
    public Vector2 ViewportSize { get; set; } = new(420, 360);

    /// <summary>World-space point the camera is centred on, in 2D world units (Y-down).</summary>
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
                if (_panning)
                    GrabFocus();
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
                    _strokePointer = button.Position;
                    // Godot coalesces motion to one event per frame, which is far too few paint
                    // samples for a fast drag. Raw motion only costs while a stroke is live.
                    Input.UseAccumulatedInput = false;
                    WorkspaceChanged?.Invoke();
                    GrabFocus();
                }
                else if (_painting)
                {
                    Workspace.EndGesture();
                    _painting = false;
                    Input.UseAccumulatedInput = true;
                    WorkspaceChanged?.Invoke();
                }
                else if (!button.Pressed && EyedropperToolActive)
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

    /// <summary>
    /// Resamples the pointer every frame while the button is held, so paint keeps flowing even
    /// when no motion event arrives (mouse held still, or events dropped under load).
    /// </summary>
    public override void _Process(double delta)
    {
        if (!_painting)
            return;
        PaintAlongTo(GetLocalMousePosition());
    }

    /// <summary>
    /// Paints from the last stroke position to <paramref name="canvas"/>, sampling the segment
    /// in small screen-space steps. The buddy covers only a few dozen pixels, so one fast drag
    /// swings the mapped UV a third of the way around a limb; interpolating on screen instead
    /// keeps every step a short, mappable hop and lets part changes and silhouette misses fall
    /// where they actually happened.
    /// </summary>
    private void PaintAlongTo(Vector2 canvas)
    {
        Vector2 from = _strokePointer;
        int steps = Math.Clamp(
            (int)Math.Ceiling(from.DistanceTo(canvas) / ScreenStepPixels), 1, MaxScreenSteps);
        for (int step = 1; step <= steps; step++)
            Workspace.ContinueGesture(Map(from.Lerp(canvas, step / (float)steps)));

        _strokePointer = canvas;
        WorkspaceChanged?.Invoke();
    }

    /// <summary>Screen-space gap between pointer resamples inside one stroke segment.</summary>
    private const float ScreenStepPixels = 1.5f;
    private const int MaxScreenSteps = 512;

    public override void _ExitTree()
    {
        if (_painting)
        {
            Workspace.EndGesture();
            _painting = false;
            Input.UseAccumulatedInput = true;
        }
        _hiddenParts.Clear();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, CtrlPressed: true } key)
            return;

        bool handled = false;
        if (key.Keycode == Key.Z && !key.ShiftPressed)
            handled = Workspace.Undo();
        else if (key.Keycode == Key.Y || (key.Keycode == Key.Z && key.ShiftPressed))
            handled = Workspace.Redo();

        if (!handled)
            return;

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
        if (PanToolActive)
            return;

        float diameter = (float)(Workspace.BrushDiameter * View.Zoom * Math.Min(Size.X, Size.Y) /
            (PaintPolicy.SurfaceSize * 2.0));
        PaintCursorGizmos.DrawBrushRing(this, _lastPointer, diameter);
    }

    public void ResetView()
    {
        View.Reset();
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    /// <summary>Sets the viewport pan from the editor's classic horizontal/vertical scrollbars.</summary>
    public void SetPanNormalized(double x, double y)
    {
        PaintPoint current = View.Pan;
        View.PanBy(new PaintPoint(x - current.X, y - current.Y));
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    /// <summary>Controls whether a semantic body-part layer participates in preview hit-testing.</summary>
    public void SetPartVisible(PaintPart part, bool visible)
    {
        if (visible)
            _hiddenParts.Remove(part);
        else
            _hiddenParts.Add(part);

        if (!visible && HoveredPart == part)
            SetHover(null);
        QueueRedraw();
    }

    public bool IsPartVisible(PaintPart part) => !_hiddenParts.Contains(part);

    public void ShowAllParts()
    {
        if (_hiddenParts.Count == 0)
            return;
        _hiddenParts.Clear();
        QueueRedraw();
    }

    /// <summary>Part under a canvas-space pointer position, or null on a miss.</summary>
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
        double yaw = GodotObject.IsInstanceValid(_host) &&
            GodotObject.IsInstanceValid(_host!.PreviewRig)
                ? Mathf.DegToRad(_host.PreviewRig.RotationDegrees.Y)
                : 0.0;

        PaintHit? hit;
        if (Math.Abs(yaw) <= 0.000001)
            hit = _mapper.TryMap(point, out PaintHit frontal) ? frontal : null;
        else
            hit = TryMapRotated(point, yaw, out PaintHit rotated) ? rotated : null;

        return hit is PaintHit valid &&
            IsPartVisible(valid.Part) &&
            (ActivePartFilter is null || valid.Part == ActivePartFilter.Value)
                ? valid
                : null;
    }

    /// <summary>
    /// Maps the visible surface after the preview rig has been rotated around its Y axis.
    /// The previous frontal mapper always wrote to the front-facing U range, so painting the
    /// back or sides appeared on the front. This reconstructs the camera-facing surface point,
    /// transforms it back into each primitive's local coordinates, and derives the matching UV.
    /// </summary>
    private bool TryMapRotated(PaintPoint point, double yaw, out PaintHit hit)
    {
        double cos = Math.Cos(yaw);
        double sin = Math.Sin(yaw);
        bool found = false;
        PaintHit nearest = default;
        double nearestDepth = double.NegativeInfinity;

        foreach (PaintPartShape shape in _mapper.Shapes)
        {
            if (!IsPartVisible(shape.Part))
                continue;

            double centerScreenX = shape.Center.X * cos;
            double centerDepth = (-shape.Center.X * sin) + (shape.Depth * cos);
            double screenX = point.X - centerScreenX;
            double yUp = -(point.Y - shape.Center.Y);

            if (!TryMapRotatedShape(
                    shape,
                    screenX,
                    yUp,
                    cos,
                    sin,
                    out PaintPoint uv,
                    out double surfaceDepth))
            {
                continue;
            }

            double depth = centerDepth + surfaceDepth;
            if (found && depth <= nearestDepth)
                continue;

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
        if (radius <= 0.0)
            return false;

        double v;
        if (shape.Kind == PaintShapeKind.Sphere)
        {
            double planar = (screenX * screenX) + (yUp * yUp);
            if (planar > radius * radius)
                return false;

            surfaceDepth = Math.Sqrt(Math.Max(0.0, (radius * radius) - planar));
            v = Math.Acos(Math.Clamp(yUp / radius, -1.0, 1.0)) / Math.PI;
        }
        else
        {
            double mid = shape.HalfHeight - radius;
            if (mid < 0.0 || Math.Abs(screenX) > radius || Math.Abs(yUp) > shape.HalfHeight)
                return false;

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
                if (planar > radius * radius)
                    return false;

                surfaceDepth = Math.Sqrt(Math.Max(0.0, (radius * radius) - planar));
                double capFraction = 2.0 / Math.PI * (yUp > 0.0
                    ? Math.Acos(Math.Clamp(capOffset / radius, -1.0, 1.0))
                    : Math.Asin(Math.Clamp(capOffset / radius, -1.0, 1.0)));
                v = yUp > 0.0
                    ? capFraction * OneThird
                    : (2.0 * OneThird) + (capFraction * OneThird);
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
        if (HoveredPart == part)
            return;
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
