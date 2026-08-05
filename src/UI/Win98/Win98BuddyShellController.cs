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
    private Vector2 _dragStartPointer;
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
        // Reuse the existing root close-request path so save/quit policy remains intact.
        if (DisplayServer.GetName() != "headless")
            GetWindow().EmitSignal(Godot.Window.SignalName.CloseRequested);
    }

    private void OnTitleDragStarted(Vector2 globalPointer)
    {
        if (Window.LayoutMode != WindowLayoutMode.Compact || DisplayServer.GetName() == "headless")
            return;

        _dragging = true;
        _dragStartPointer = globalPointer;
        _dragStartWindowPosition = GetWindow().Position;
        Frame.StatusText = "Moving window";
    }

    private void OnTitleDragMoved(Vector2 globalPointer)
    {
        if (!_dragging || Window.LayoutMode != WindowLayoutMode.Compact)
            return;

        Vector2 delta = globalPointer - _dragStartPointer;
        GetWindow().Position = _dragStartWindowPosition + new Vector2I(
            Mathf.RoundToInt(delta.X),
            Mathf.RoundToInt(delta.Y));
    }

    private void OnTitleDragEnded(Vector2 globalPointer)
    {
        if (!_dragging)
            return;

        OnTitleDragMoved(globalPointer);
        _dragging = false;

        WindowSettings captured = Window.CaptureWindowSettings();
        Window.ApplyWindowSettings(Window.RecoverWindowSettings(captured));
        Frame.StatusText = "Ready";
    }

    private void OnLayoutModeChanged(WindowLayoutMode mode) => ApplyLayoutMode(mode);

    private void ApplyLayoutMode(WindowLayoutMode mode)
    {
        bool compact = mode == WindowLayoutMode.Compact;
        Frame.Visible = compact;
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
