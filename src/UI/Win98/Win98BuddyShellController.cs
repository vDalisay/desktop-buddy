using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// W1 integration controller for the in-scene Win98 shell. The controller owns only
/// presentation/input routing; native window policy remains in DesktopWindowController.
/// Browser builds keep this visual shell inside one opaque canvas instead of trying to
/// reproduce Desktop Buddy's native transparent/topmost window composition.
/// </summary>
public partial class Win98BuddyShellController : CanvasLayer
{
    [Export] public DesktopWindowController Window { get; set; } = null!;
    [Export] public Win98WindowFrame Frame { get; set; } = null!;

    private Vector2I _dragStartWindowPosition;
    private Vector2I _dragStartPointer;
    private bool _dragging;
    private bool _wasMaximized;

    private bool _resizing;
    private int _resizeCorner = -1;
    private Vector2I _resizeStartPointer;
    private Rect2I _resizeStartWindowRect;
    private WorldEnvironment _backdrop = null!;

    private static bool BrowserCanvas => System.OperatingSystem.IsBrowser();

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

        Frame.WindowTitle = "Desktop Buddy";
        Frame.StatusText = "Ready";
        Frame.SetActive(true);
        ApplyLayoutMode(Window.LayoutMode);
    }

    /// <summary>A maximized window has no free rect to move or resize; only restored windows do.</summary>
    private bool CanReshapeWindow =>
        !BrowserCanvas &&
        Window.LayoutMode == WindowLayoutMode.Compact &&
        !Window.WorkCompanionActive &&
        DisplayServer.GetName() != "headless" &&
        GetWindow().Mode == Godot.Window.ModeEnum.Windowed;

    public override void _Process(double delta)
    {
        if (_dragging && CanReshapeWindow)
        {
            Vector2I pointer = DisplayServer.MouseGetPosition();
            Vector2I target = _dragStartWindowPosition + (pointer - _dragStartPointer);
            DisplayServer.WindowSetPosition(target, GetWindow().GetWindowId());
        }

        if (_resizing && CanReshapeWindow)
            ApplyResizeFromPointer(DisplayServer.MouseGetPosition());

        ApplyWindowTransparency(Window.LayoutMode, Frame.ViewportOpacity);

        // Focus-driven title colour, sampled rather than event-driven: Work Mode takes and
        // returns the window without a focus-lost/gained pair, which used to strand the bar grey.
        if (!BrowserCanvas && DisplayServer.GetName() != "headless")
            Frame.SetActive(GetWindow().HasFocus());
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Window))
        {
            Window.LayoutModeChanged -= OnLayoutModeChanged;
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
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            },
        };
        GetTree().Root.AddChild(_backdrop);
        ApplyBackdropOpacity(BrowserCanvas ? 1.0f : Win98WindowFrame.CompactOpacity);
    }

    private void ApplyBackdropOpacity(float opacity)
    {
        // Work Mode detaches the environment while it owns the window; skip until it returns.
        if (GodotObject.IsInstanceValid(_backdrop) && GodotObject.IsInstanceValid(_backdrop.Environment))
            _backdrop.Environment.BackgroundColor = new Color(
                Win98ThemeFactory.Face.R,
                Win98ThemeFactory.Face.G,
                Win98ThemeFactory.Face.B,
                opacity);
    }

    private void ApplyWindowTransparency(WindowLayoutMode mode, float opacity)
    {
        if (DisplayServer.GetName() == "headless")
            return;

        // The Web platform renders into a DOM canvas, not a composited desktop window.
        // The old per-frame compact-opacity path re-enabled Window.Transparent and
        // Viewport.TransparentBg after the browser-specific launch settings had disabled them,
        // leaving the exported WebGL framebuffer as the uniform Godot clear colour. Keep the
        // browser presentation opaque for the lifetime of the page.
        if (BrowserCanvas)
        {
            ApplyBackdropOpacity(1.0f);
            if (GetWindow().Transparent)
                GetWindow().Transparent = false;
            if (GetViewport().TransparentBg)
                GetViewport().TransparentBg = false;
            return;
        }

        if (Window.WorkCompanionActive)
        {
            ApplyBackdropOpacity(0.0f);
            if (!GetWindow().Transparent)
                GetWindow().Transparent = true;
            if (!GetViewport().TransparentBg)
                GetViewport().TransparentBg = true;
            return;
        }

        ApplyBackdropOpacity(mode == WindowLayoutMode.Compact ? opacity : 0.0f);
        bool transparent = mode != WindowLayoutMode.Compact || opacity < 1f;
        if (GetWindow().Transparent == transparent && GetViewport().TransparentBg == transparent)
            return;

        GetWindow().Transparent = transparent;
        GetViewport().TransparentBg = transparent;
    }

    private void OnMinimizeRequested()
    {
        if (BrowserCanvas)
        {
            Frame.StatusText = "Minimize is unavailable in browser play.";
            return;
        }

        if (DisplayServer.GetName() != "headless")
            GetWindow().Mode = Godot.Window.ModeEnum.Minimized;
    }

    // Maximize is an ordinary maximized window — same Win98 chrome, just monitor-sized.
    // The transparent full-screen overlay is F11 only.
    private void OnMaximizeRestoreRequested()
    {
        if (BrowserCanvas)
        {
            Frame.StatusText = "Window maximize is unavailable in browser play.";
            return;
        }

        if (DisplayServer.GetName() == "headless")
            return;

        if (Window.LayoutMode == WindowLayoutMode.FullscreenOverlay)
        {
            ToggleFullscreenOverlay();
            return;
        }

        Godot.Window native = GetWindow();
        native.Mode = native.Mode == Godot.Window.ModeEnum.Maximized
            ? Godot.Window.ModeEnum.Windowed
            : Godot.Window.ModeEnum.Maximized;
        Frame.StatusText = native.Mode == Godot.Window.ModeEnum.Maximized ? "Maximized" : "Ready";
    }

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
            return;
        ToggleFullscreenOverlay();
        GetViewport().SetInputAsHandled();
    }

    private void ToggleFullscreenOverlay()
    {
        if (BrowserCanvas)
        {
            Frame.StatusText = "Desktop overlay mode is unavailable in browser play.";
            return;
        }

        bool entering = Window.LayoutMode == WindowLayoutMode.Compact;
        bool headless = DisplayServer.GetName() == "headless";
        if (entering && !headless)
            _wasMaximized = GetWindow().Mode == Godot.Window.ModeEnum.Maximized;

        WindowLayoutMode target = entering ? WindowLayoutMode.FullscreenOverlay : WindowLayoutMode.Compact;
        if (!Window.TrySetLayoutMode(target, GetWindow().CurrentScreen))
        {
            Frame.StatusText = "Full-screen mode is unavailable on this system.";
            return;
        }

        // Leaving full-screen returns to whichever compact shape F11 was pressed from.
        if (!entering && !headless && _wasMaximized)
            Callable.From(() => GetWindow().Mode = Godot.Window.ModeEnum.Maximized).CallDeferred();
    }

    private void OnCloseRequested()
    {
        if (BrowserCanvas)
        {
            Frame.StatusText = "Close the browser tab to exit.";
            return;
        }

        if (DisplayServer.GetName() != "headless")
            GetWindow().EmitSignal(Godot.Window.SignalName.CloseRequested);
    }

    private void OnTitleDragStarted(Vector2 globalPointer)
    {
        if (!CanReshapeWindow || _resizing)
            return;

        _dragging = true;
        _dragStartPointer = DisplayServer.MouseGetPosition();
        _dragStartWindowPosition = GetWindow().Position;
        Frame.StatusText = "Moving window";
    }

    private void OnTitleDragMoved(Vector2 globalPointer)
    {
        // Native pointer sampling in _Process owns the drag. This event is retained so the
        // Win98 frame remains a reusable source of drag intent.
    }

    private void OnTitleDragEnded(Vector2 globalPointer)
    {
        _dragging = false;
        Frame.StatusText = "Ready";
    }

    private void OnResizeStarted(int corner, Vector2 globalPointer)
    {
        if (!CanReshapeWindow || _dragging)
            return;
        _resizing = true;
        _resizeCorner = corner;
        _resizeStartPointer = DisplayServer.MouseGetPosition();
        Window native = GetWindow();
        _resizeStartWindowRect = new Rect2I(native.Position, native.Size);
        Frame.StatusText = "Resizing window";
    }

    private void OnResizeEnded(int corner, Vector2 globalPointer)
    {
        _resizing = false;
        _resizeCorner = -1;
        Frame.StatusText = "Ready";
    }

    private void ApplyResizeFromPointer(Vector2I pointer)
    {
        Vector2I delta = pointer - _resizeStartPointer;
        Rect2I rect = _resizeStartWindowRect;
        Vector2I position = rect.Position;
        Vector2I size = rect.Size;
        bool left = _resizeCorner is 0 or 2;
        bool top = _resizeCorner is 0 or 1;
        if (left)
        {
            position.X += delta.X;
            size.X -= delta.X;
        }
        else size.X += delta.X;
        if (top)
        {
            position.Y += delta.Y;
            size.Y -= delta.Y;
        }
        else size.Y += delta.Y;
        size.X = System.Math.Max(size.X, Domain.Physics.RoomLayoutPolicy.MinimumRoomWidth);
        size.Y = System.Math.Max(size.Y, Domain.Physics.RoomLayoutPolicy.MinimumRoomHeight);
        GetWindow().Position = position;
        GetWindow().Size = size;
    }

    private void OnLayoutModeChanged(WindowLayoutMode mode) => ApplyLayoutMode(mode);

    private void ApplyLayoutMode(WindowLayoutMode mode)
    {
        if (mode == WindowLayoutMode.FullscreenOverlay)
        {
            Frame.Visible = false;
            ApplyBackdropOpacity(BrowserCanvas ? 1.0f : 0.0f);
        }
        else
        {
            Frame.Visible = true;
            ApplyBackdropOpacity(BrowserCanvas ? 1.0f : Frame.ViewportOpacity);
        }
        ApplyWindowTransparency(mode, Frame.ViewportOpacity);
    }
}
