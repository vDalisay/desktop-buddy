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

    // The buddy is a 3D presentation, and Godot composites every canvas item — including
    // negative CanvasLayers — on top of the 3D pass. A ColorRect backdrop therefore always
    // painted over the buddy; the window-body tint has to be the 3D clear colour instead.
    private WorldEnvironment _backdrop = null!;

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

        // DesktopWindowController re-applies its own transparency whenever it re-applies
        // window settings, so the opaque-compact choice has to be re-asserted (it no-ops
        // unless the flags actually drifted).
        ApplyWindowTransparency(Window.LayoutMode, Frame.ViewportOpacity);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Window))
        {
            Window.LayoutModeChanged -= OnLayoutModeChanged;
            Window.WindowFocusLost -= OnWindowFocusLost;
        }

        if (GodotObject.IsInstanceValid(_backdrop))
            _backdrop.QueueFree();
    }

    private void EnsureBackdropLayer()
    {
        _backdrop = new WorldEnvironment
        {
            Name = "Win98BackdropEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                // Ambient stays off: the lighting rig owns the buddy's look.
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            },
        };
        GetTree().Root.AddChild(_backdrop);
        ApplyBackdropOpacity(Win98WindowFrame.CompactOpacity);
    }

    private void ApplyBackdropOpacity(float opacity)
    {
        if (GodotObject.IsInstanceValid(_backdrop))
            _backdrop.Environment.BackgroundColor = new Color(
                Win98ThemeFactory.Face.R,
                Win98ThemeFactory.Face.G,
                Win98ThemeFactory.Face.B,
                opacity);
    }

    /// <summary>
    /// A fully opaque body cannot be produced by the clear colour alone: the window keeps
    /// per-pixel transparency, so the desktop still shows through. Compact at opacity 1 is a
    /// plain window, so drop transparency there; the fullscreen overlay always keeps it.
    /// </summary>
    private void ApplyWindowTransparency(WindowLayoutMode mode, float opacity)
    {
        if (DisplayServer.GetName() == "headless")
            return;

        bool transparent = mode != WindowLayoutMode.Compact || opacity < 1f;
        if (GetWindow().Transparent == transparent && GetViewport().TransparentBg == transparent)
            return;

        GetWindow().Transparent = transparent;
        GetViewport().TransparentBg = transparent;
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
        Vector2I minimum = GetWindow().MinSize;

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
        Frame.SetViewportOpacity(mode == WindowLayoutMode.Compact
            ? Win98WindowFrame.CompactOpacity
            : Win98WindowFrame.FullscreenOpacity);
        ApplyBackdropOpacity(Frame.ViewportOpacity);
        ApplyWindowTransparency(mode, Frame.ViewportOpacity);
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
