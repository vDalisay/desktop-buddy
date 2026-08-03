using System;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>Transparent input layer over the physics-free character preview.</summary>
public partial class PaintCanvasControl : Control
{
    private readonly FrontalPaintMapper _mapper = FrontalPaintMapper.CreateDefault();
    private bool _painting;
    private bool _panning;
    private Vector2 _lastPointer;

    public PaintWorkspace Workspace { get; } = new();
    public PaintViewState View { get; } = new();
    public PaintPart? HoveredPart { get; private set; }

    public event Action? WorkspaceChanged;
    public event Action? ViewChanged;
    public event Action<PaintPart?>? HoverChanged;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseMotion motion)
        {
            _lastPointer = motion.Position;
            if (_panning)
            {
                View.PanBy(new PaintPoint(
                    motion.Relative.X / Math.Max(1.0, Size.X),
                    motion.Relative.Y / Math.Max(1.0, Size.Y)));
                ViewChanged?.Invoke();
            }
            else
            {
                PaintHit? hit = Map(motion.Position);
                SetHover(hit?.Part);
                if (_painting)
                {
                    Workspace.ContinueGesture(hit);
                    WorkspaceChanged?.Invoke();
                }
            }
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (input is InputEventMouseButton button)
        {
            _lastPointer = button.Position;
            bool spacePan = Input.IsKeyPressed(Key.Space) && button.ButtonIndex == MouseButton.Left;
            if (button.ButtonIndex == MouseButton.Middle || spacePan)
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
                    if (hit is PaintHit valid)
                    {
                        Workspace.BeginGesture(valid);
                        _painting = true;
                        WorkspaceChanged?.Invoke();
                    }
                    GrabFocus();
                }
                else if (_painting)
                {
                    Workspace.EndGesture();
                    _painting = false;
                    WorkspaceChanged?.Invoke();
                }
                AcceptEvent();
                return;
            }

            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                int direction = button.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                if (Input.IsKeyPressed(Key.Ctrl))
                {
                    View.SetZoom(View.Zoom + (direction * 0.2), CanvasToModel(button.Position));
                    ViewChanged?.Invoke();
                }
                else
                {
                    Workspace.AdjustBrush(direction);
                    WorkspaceChanged?.Invoke();
                }
                QueueRedraw();
                AcceptEvent();
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey key && key.Pressed && key.CtrlPressed && key.Keycode == Key.Z)
        {
            if (Workspace.Undo())
                WorkspaceChanged?.Invoke();
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        float diameter = (float)(Workspace.BrushDiameter * View.Zoom * Math.Min(Size.X, Size.Y) /
            (PaintPolicy.SurfaceSize * 2.0));
        DrawArc(_lastPointer, Math.Max(2, diameter / 2), 0, Mathf.Tau, 32, Colors.White, 1.5f);
    }

    public void ResetView()
    {
        View.Reset();
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    private PaintHit? Map(Vector2 canvas)
    {
        PaintPoint point = CanvasToModel(canvas);
        return _mapper.TryMap(point, out PaintHit hit) ? hit : null;
    }

    private PaintPoint CanvasToModel(Vector2 canvas)
    {
        double width = Math.Max(1.0, Size.X);
        double height = Math.Max(1.0, Size.Y);
        double normalizedX = ((canvas.X / width) - 0.5) * 3.4;
        double normalizedY = ((canvas.Y / height) - 0.5) * 4.0;
        return new PaintPoint(
            (normalizedX / View.Zoom) - View.Pan.X,
            (normalizedY / View.Zoom) - View.Pan.Y);
    }

    private void SetHover(PaintPart? part)
    {
        if (HoveredPart == part) return;
        HoveredPart = part;
        HoverChanged?.Invoke(part);
    }
}
