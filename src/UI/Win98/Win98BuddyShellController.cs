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

        Frame.WindowTitle = "Desktop Buddy";
        Frame.StatusText = "Ready";
        Frame.SetActive(true);
        ApplyLayoutMode(Window.LayoutMode);
    }

    public override void _Process(double delta)
    {
        if (_dragging && Window.LayoutMode == WindowLayoutMode.Compact && !Window.WorkCompanionActive && DisplayServer.GetName() != "headless")
        {
            Vector2I pointer = DisplayServer.MouseGetPosition();
            Vector2I target = _dragStartWindowPosition + (pointer - _dragStartPointer);
            DisplayServer.WindowSetPosition(target, GetWindow().GetWindowId());
        }

        if (_resizing && Window.LayoutMode == WindowLayoutMode.Compact && !Window.WorkCompanionActive && DisplayServer.GetName() != "headless")
            ApplyResizeFromPointer(DisplayServer.MouseGetPosition());

        ApplyWindowTransparency(Window.LayoutMode, Frame.ViewportOpacity);

        // Focus-driven title colour, sampled rather than event-driven: Work Mode takes and
        // returns the window without a focus-lost/gained pair, which used to strand the bar grey.
        if (DisplayServer.GetName() != "headless")
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
        ApplyBackdropOpacity(Win98WindowFrame.CompactOpacity);
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
        if (Window.LayoutMode != WindowLayoutMode.Compact || Window.WorkCompanionActive || DisplayServer.GetName() == "headless" || _resizing)
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
        if (Window.LayoutMode != WindowLayoutMode.Compact || Window.WorkCompanionActive || DisplayServer.GetName() == "headless" || _dragging)
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
            ApplyBackdropOpacity(0.0f);
        }
        else
        {
            Frame.Visible = true;
            ApplyBackdropOpacity(Frame.ViewportOpacity);
        }
        ApplyWindowTransparency(mode, Frame.ViewportOpacity);
    }
}
