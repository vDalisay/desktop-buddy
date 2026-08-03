using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Platform;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Platform;

/// <summary>
/// Runtime diagnostics and defensive native-window invariants. On Windows a transparent
/// always-on-top native Fullscreen window can cover sibling top-level windows from the same
/// process. FullscreenOverlay therefore settles as a borderless monitor-sized Windowed window,
/// which preserves transparency, click-through, and reliable recovery-toolbar stacking.
/// </summary>
public partial class DesktopWindowController
{
    private const string InputDiagnosticsCategory = "InputDiagnostics";

    private ulong _inputDiagnosticFrame;
    private string? _lastInputDiagnosticSignature;
    private bool _overlayWindowStabilized;

    public override void _Process(double delta)
    {
        _inputDiagnosticFrame++;
        if (_headless || !GodotObject.IsInstanceValid(GetWindow()))
            return;

        Window window = GetWindow();
        bool semanticFullscreen = LayoutMode == WindowLayoutMode.FullscreenOverlay;

        if (semanticFullscreen && window.Mode == Window.ModeEnum.Fullscreen)
        {
            int monitor = System.Math.Clamp(FullscreenMonitor, 0, DisplayServer.GetScreenCount() - 1);
            Vector2I position = DisplayServer.ScreenGetPosition(monitor);
            Vector2I size = DisplayServer.ScreenGetSize(monitor);

            // Leave native Fullscreen first. A borderless monitor-sized window is visually
            // identical for this transparent overlay but does not suppress the sibling toolbar.
            window.Mode = Window.ModeEnum.Windowed;
            window.CurrentScreen = monitor;
            window.Borderless = true;
            window.Unresizable = true;
            window.Transparent = true;
            window.AlwaysOnTop = _compactSettings.AlwaysOnTop;
            GetViewport().TransparentBg = true;
            if (size.X > 0 && size.Y > 0)
            {
                window.Size = size;
                window.Position = position;
            }

            _overlayWindowStabilized = true;
            Log.Info(InputDiagnosticsCategory,
                $"Stabilized full-screen overlay as borderless window: monitor={monitor} " +
                $"rect={new Rect2I(position, size)} nativeMode={window.Mode}.");
        }
        else if (!semanticFullscreen)
        {
            _overlayWindowStabilized = false;
        }

        bool requestedPassthrough =
            semanticFullscreen && InputMode == DomainInputMode.Work;
        bool allowedPassthrough = requestedPassthrough;
        bool actualPassthrough = window.MousePassthrough;

        if (actualPassthrough != allowedPassthrough)
        {
            Log.Warn(InputDiagnosticsCategory,
                "Native input policy mismatch detected: " +
                $"layout={LayoutMode} input={InputMode} nativeMode={window.Mode} " +
                $"requestedPassthrough={requestedPassthrough} " +
                $"allowedPassthrough={allowedPassthrough} actualPassthrough={actualPassthrough}. " +
                "Correcting the native window state now.");

            _adapter.SetPlayModeCapture();
            window.MousePassthrough = allowedPassthrough;
            MainWindowMousePassthrough = allowedPassthrough;
            actualPassthrough = window.MousePassthrough;
        }

        string state =
            $"layout={LayoutMode};input={InputMode};nativeMode={window.Mode};" +
            $"overlayStabilized={_overlayWindowStabilized};" +
            $"windowRect={new Rect2I(window.Position, window.Size)};" +
            $"visible={window.Visible};hasFocus={window.HasFocus()};" +
            $"requestedPassthrough={requestedPassthrough};" +
            $"allowedPassthrough={allowedPassthrough};actualPassthrough={actualPassthrough};" +
            $"adapterVisible={_adapter.IsWindowVisible};adapterNative={_adapter.IsNative}";

        bool startupSample = _inputDiagnosticFrame is 1 or 30 or 120 or 300;
        if (!startupSample && string.Equals(
                state,
                _lastInputDiagnosticSignature,
                System.StringComparison.Ordinal))
        {
            return;
        }

        _lastInputDiagnosticSignature = state;
        Log.Info(InputDiagnosticsCategory, $"frame={_inputDiagnosticFrame};{state}");
    }
}
