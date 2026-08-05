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

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Window) || !GodotObject.IsInstanceValid(Frame))
            throw new System.InvalidOperationException("Win98BuddyShellController requires Window and Frame.");

        Frame.MinimizeRequested += OnMinimizeRequested;
        Frame.MaximizeRestoreRequested += OnMaximizeRestoreRequested;
        Frame.CloseRequested += OnCloseRequested;
        Frame.TitleDragStarted += OnTitleDragStarted;
        Frame.TitleDragMoved += OnTitleDragMoved;
        Frame.TitleDragEnded += OnTitleDragEnded;

        Window.LayoutModeChanged += OnLayoutModeChanged;
        Window.WindowFocusLost += OnWindowFocusLost;

        Frame.WindowTitle = "Desktop Buddy";
        Frame.StatusText = "Ready";
        Frame.SetActive(true);
        ApplyLayoutMode(Window.LayoutMode);
    }

    public override void _Process(double delta)
    {
        if (!_dragging || Window.LayoutMode != WindowLayoutMode.Compact || DisplayServer.GetName() == "headless")
            return;

        // Poll the desktop cursor rather than relying on window-local mouse motion. Windows can
        // suspend redraw/motion delivery while a borderless window is being repositioned.
        Vector2I pointer = DisplayServer.MouseGetPosition();
        Vector2I target = _dragStartWindowPosition + (pointer - _dragStartPointer);
        DisplayServer.WindowSetPosition(target, GetWindow().GetWindowId());
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Window))
        {
            Window.LayoutModeChanged -= OnLayoutModeChanged;
            Window.WindowFocusLost -= OnWindowFocusLost;
        }
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
        if (Window.LayoutMode != WindowLayoutMode.Compact || DisplayServer.GetName() == "headless")
            return;

        _dragging = true;
        _dragStartPointer = DisplayServer.MouseGetPosition();
        _dragStartWindowPosition = GetWindow().Position;
        Frame.StatusText = "Moving window";
    }

    private void OnTitleDragMoved(Vector2 globalPointer)
    {
        // Movement is intentionally handled by _Process using desktop-space cursor coordinates.
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

    private void OnLayoutModeChanged(WindowLayoutMode mode) => ApplyLayoutMode(mode);

    private void ApplyLayoutMode(WindowLayoutMode mode)
    {
        bool compact = mode == WindowLayoutMode.Compact;

        // The application chrome remains visible in both modes. Full interaction mode uses a
        // denser backdrop while compact mode keeps the desktop and buddy visible through it.
        Frame.Visible = true;
        Frame.SetViewportOpacity(compact ? 0.5f : 0.9f);
        Frame.StatusText = compact ? "Ready" : "Full interaction mode";
    }

    private void OnWindowFocusLost()
    {
        _dragging = false;
        Frame.SetActive(false);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn && GodotObject.IsInstanceValid(Frame))
            Frame.SetActive(true);
    }
}
