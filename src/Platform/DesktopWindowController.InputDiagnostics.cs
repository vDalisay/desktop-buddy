using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Read-only native window diagnostics. This partial must never mutate window state: earlier
/// revisions rewrote Fullscreen into a borderless Windowed window from _Process, which fought
/// the layout transition in <see cref="DesktopWindowController.TrySetLayoutMode"/> and made the
/// reported native mode disagree with the requested one.
/// </summary>
public partial class DesktopWindowController
{
    private const string InputDiagnosticsCategory = "InputDiagnostics";

    private string? _lastInputDiagnosticSignature;

    /// <summary>Logs the native window state whenever it changes. Never writes to the window.</summary>
    private void LogWindowState(string reason)
    {
        if (_headless || !GodotObject.IsInstanceValid(GetWindow()))
            return;

        Window window = GetWindow();
        string state =
            $"layout={LayoutMode};input={InputMode};nativeMode={window.Mode};" +
            $"windowId={window.GetWindowId()};rect={new Rect2I(window.Position, window.Size)};" +
            $"transparent={window.Transparent};alwaysOnTop={window.AlwaysOnTop};" +
            $"passthrough={window.MousePassthrough};visible={window.Visible};" +
            $"hasFocus={window.HasFocus()};screen={window.CurrentScreen}";

        if (string.Equals(state, _lastInputDiagnosticSignature, System.StringComparison.Ordinal))
            return;

        _lastInputDiagnosticSignature = state;
        Log.Info(InputDiagnosticsCategory, $"[WindowState] {reason}: {state}");
    }
}
