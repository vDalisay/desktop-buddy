using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// W1 integration controller for the in-scene Win98 shell. The controller owns only
/// presentation/input routing; native window policy remains in DesktopWindowController.
/// </summary>
public partial class Win98BuddyShellController : CanvasLayer
{
    [Export] public DesktopWindowController Window { get; set; } = null!;
    [Export] public Win98WindowFrame Frame { get; set; } = null!;

    private Vector2I _dragStartWindowPosition;
    private Vector2I _dragStartPointer;
    private bool _dragging;

    private bool _resizing;
    private int _resizeCorner = -1;
    private Vector2I _resizeStartPointer;
    private Rect2I _resizeStartWindowRect;

    private CanvasLayer _backdropLayer = null!;
    private ColorRect _backdropRect = null!;

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Window) || !GodotObject.IsInstanceValid(Frame))
            throw new System.InvalidOperationException("Win98BuddyShellController requires Window and Frame.");

        EnsureBackdropLayer();

        Frame.MinimizeRequested += OnMinimizeRequested;
        Frame.MaximizeRestoreRequested += OnMaximizeRestoreRequested;
        Frame.CloseRequested += OnCloseRequested;
        Frame.TitleDragStarted += OnTitleDragStarted;
        Frame.TitleDragMoved += OnTitleDragMoved;
        Frame.TitleDragEnded += OnTitleDragEnded;
        Frame.ResizeStarted += OnResizeStarted;
        Frame.ResizeEnded += OnResizeEnded;

        Window.LayoutModeChanged += OnLayoutModeChanged;
        Window.WindowFocusLost += OnWindowFocusLost;

        Frame.WindowTitle = "Desktop Buddy";
        Frame.StatusText = "Ready";
        Frame.SetActive(true);
        ApplyLayoutMode(Window.LayoutMode);
        SyncBackdropToFrame();
    }

    public override void _Process(double delta)
    {
        if (_dragging && Window.LayoutMode == WindowLayoutMode.Compact && DisplayServer.GetName() != "headless")
        {
            Vector2I pointer = DisplayServer.MouseGetPosition();
            Vector2I target = _dragStartWindowPosition + (pointer - _dragStartPointer);
            DisplayServer.WindowSetPosition(target, GetWindow().GetWindowId());
        }

        if (_resizing && Window.LayoutMode == WindowLayoutMode.Compact && DisplayServer.GetName() != "headless")
            ApplyResizeFromPointer(DisplayServer.MouseGetPosition());

        SyncBackdropToFrame();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Window))
        {
            Window.LayoutModeChanged -= OnLayoutModeChanged;
            Window.WindowFocusLost -= OnWindowFocusLost;
        }

        if (GodotObject.IsInstanceValid(_backdropLayer))
            _backdropLayer.QueueFree();
    }

    private void EnsureBackdropLayer()
    {
        _backdropLayer = new CanvasLayer
        {
            Name = "Win98BackdropLayer",
            Layer = -5,
        };
        _backdropRect = new ColorRect
        {
            Name = "BackdropRect",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = new Color(
                Win98ThemeFactory.Face.R,
                Win98ThemeFactory.Face.G,
                Win98ThemeFactory.Face.B,
                0.5f),
        };
        _backdropLayer.AddChild(_backdropRect);
        GetTree().Root.AddChild(_backdropLayer);
    }

    private void SyncBackdropToFrame()
    {
        if (!GodotObject.IsInstanceValid(Frame) || !GodotObject.IsInstanceValid(_backdropRect))
            return;

        Rect2 rect = Frame.ContentViewportRect;
        _backdropRect.Visible = Frame.Visible && rect.Size.X > 0f && rect.Size.Y > 0f;
        _backdropRect.Position = rect.Position;
        _backdropRect.Size = rect.Size;
    }

    private void OnMinimizeRequested()
    {
        if (DisplayServer.GetName() != "headless")
            GetWindow().Mode = Godot.Window.ModeEnum.Minimized;
    }

    private void OnMaximizeRestoreRequested()
    {
        WindowLayoutMode target = Window.LayoutMode == WindowLayoutMode.Compact
            ? WindowLayoutMode.FullscreenOverlay
            : WindowLayoutMode.Compact;

        if (!Window.TrySetLayoutMode(target, GetWindow().CurrentScreen))
            Frame.StatusText = "Full interaction mode is unavailable on this system.";
    }

    private void OnCloseRequested()
    {
        if (DisplayServer.GetName() != "headless")
            GetWindow().EmitSignal(Godot.Window.SignalName.CloseRequested);
    }

    private void OnTitleDragStarted(Vector2 globalPointer)
    {
        if (Window.LayoutMode != WindowLayoutMode.Compact || DisplayServer.GetName() == "headless" || _resizing)
            return;

        _dragging = true;
        _dragStartPointer = DisplayServer.MouseGetPosition();
        _dragStartWindowPosition = GetWindow().Position;
        Frame.StatusText = "Moving window";
    }

    private void OnTitleDragMoved(Vector2 globalPointer)
    {
    }

    private void OnTitleDragEnded(Vector2 globalPointer)
    {
        if (!_dragging)
            return;

        _dragging = false;
        WindowSettings captured = Window.CaptureWindowSettings();
        Window.ApplyWindowSettings(Window.RecoverWindowSettings(captured));
        Frame.StatusText = "Ready";
    }

    private void OnResizeStarted(int corner, Vector2 globalPointer)
    {
        if (Window.LayoutMode != WindowLayoutMode.Compact || DisplayServer.GetName() == "headless" || _dragging)
            return;

        _resizing = true;
        _resizeCorner = corner;
        _resizeStartPointer = DisplayServer.MouseGetPosition();
        _resizeStartWindowRect = new Rect2I(GetWindow().Position, GetWindow().Size);
        Frame.StatusText = "Resizing window";
    }

    private void OnResizeEnded(int corner, Vector2 globalPointer)
    {
        if (!_resizing)
            return;

        ApplyResizeFromPointer(DisplayServer.MouseGetPosition());
        _resizing = false;
        _resizeCorner = -1;
        WindowSettings captured = Window.CaptureWindowSettings();
        Window.ApplyWindowSettings(Window.RecoverWindowSettings(captured));
        Frame.StatusText = "Ready";
    }

    private void ApplyResizeFromPointer(Vector2I pointer)
    {
        Vector2I delta = pointer - _resizeStartPointer;
        Vector2I pos = _resizeStartWindowRect.Position;
        Vector2I size = _resizeStartWindowRect.Size;
        Vector2I minimum = new(
            Mathf.RoundToInt(Frame.CustomMinimumSize.X),
            Mathf.RoundToInt(Frame.CustomMinimumSize.Y));

        switch (_resizeCorner)
        {
            case Win98WindowFrame.ResizeTopLeft:
                pos += delta;
                size -= delta;
                break;
            case Win98WindowFrame.ResizeTopRight:
                pos.Y += delta.Y;
                size.X += delta.X;
                size.Y -= delta.Y;
                break;
            case Win98WindowFrame.ResizeBottomLeft:
                pos.X += delta.X;
                size.X -= delta.X;
                size.Y += delta.Y;
                break;
            case Win98WindowFrame.ResizeBottomRight:
                size += delta;
                break;
        }

        if (size.X < minimum.X)
        {
            int deficit = minimum.X - size.X;
            if (_resizeCorner == Win98WindowFrame.ResizeTopLeft ||
                _resizeCorner == Win98WindowFrame.ResizeBottomLeft)
                pos.X -= deficit;
            size.X = minimum.X;
        }

        if (size.Y < minimum.Y)
        {
            int deficit = minimum.Y - size.Y;
            if (_resizeCorner == Win98WindowFrame.ResizeTopLeft ||
                _resizeCorner == Win98WindowFrame.ResizeTopRight)
                pos.Y -= deficit;
            size.Y = minimum.Y;
        }

        DisplayServer.WindowSetPosition(pos, GetWindow().GetWindowId());
        DisplayServer.WindowSetSize(size, GetWindow().GetWindowId());
    }

    private void OnLayoutModeChanged(WindowLayoutMode mode) => ApplyLayoutMode(mode);

    private void ApplyLayoutMode(WindowLayoutMode mode)
    {
        Frame.Visible = true;
        Frame.SetViewportOpacity(mode == WindowLayoutMode.Compact ? 0.5f : 0.9f);
        if (GodotObject.IsInstanceValid(_backdropRect))
        {
            _backdropRect.Color = new Color(
                Win98ThemeFactory.Face.R,
                Win98ThemeFactory.Face.G,
                Win98ThemeFactory.Face.B,
                Frame.ViewportOpacity);
        }
        Frame.StatusText = mode == WindowLayoutMode.Compact ? "Ready" : "Full interaction mode";
    }

    private void OnWindowFocusLost()
    {
        _dragging = false;
        _resizing = false;
        Frame.SetActive(false);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn && GodotObject.IsInstanceValid(Frame))
            Frame.SetActive(true);
    }
}
