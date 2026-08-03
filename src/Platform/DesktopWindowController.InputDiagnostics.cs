using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Platform;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Runtime diagnostics and a defensive invariant for the native input state. The semantic
/// layout may briefly disagree with the actual Windows mode during startup or a failed layout
/// transition; a visible windowed buddy box must never remain wholly mouse-passthrough.
/// </summary>
public partial class DesktopWindowController
{
    private const string InputDiagnosticsCategory = "InputDiagnostics";

    private ulong _inputDiagnosticFrame;
    private string? _lastInputDiagnosticSignature;

    public override void _Process(double delta)
    {
        _inputDiagnosticFrame++;
        if (_headless || !GodotObject.IsInstanceValid(GetWindow()))
            return;

        Window window = GetWindow();
        bool nativeFullscreen = window.Mode == Window.ModeEnum.Fullscreen;
        bool requestedPassthrough =
            LayoutMode == WindowLayoutMode.FullscreenOverlay &&
            InputMode == Domain.Platform.InputMode.Work;
        bool allowedPassthrough = requestedPassthrough && nativeFullscreen;
        bool actualPassthrough = window.MousePassthrough;

        if (actualPassthrough != allowedPassthrough)
        {
            Log.Warn(InputDiagnosticsCategory,
                "Native input policy mismatch detected: " +
                $"layout={LayoutMode} input={InputMode} nativeMode={window.Mode} " +
                $"requestedPassthrough={requestedPassthrough} " +
                $"allowedPassthrough={allowedPassthrough} actualPassthrough={actualPassthrough}. " +
                "Correcting the native window state now.");

            // The old WM_NCHITTEST path must not remain active while correcting Godot's
            // whole-window passthrough flag.
            _adapter.SetPlayModeCapture();
            window.MousePassthrough = allowedPassthrough;
            MainWindowMousePassthrough = allowedPassthrough;
            actualPassthrough = window.MousePassthrough;
        }

        string signature =
            $"frame={_inputDiagnosticFrame};layout={LayoutMode};input={InputMode};" +
            $"nativeMode={window.Mode};windowRect={new Rect2I(window.Position, window.Size)};" +
            $"visible={window.Visible};hasFocus={window.HasFocus()};" +
            $"requestedPassthrough={requestedPassthrough};" +
            $"allowedPassthrough={allowedPassthrough};actualPassthrough={actualPassthrough};" +
            $"adapterVisible={_adapter.IsWindowVisible};adapterNative={_adapter.IsNative}";

        bool startupSample = _inputDiagnosticFrame is 1 or 30 or 120 or 300;
        if (!startupSample && string.Equals(
                signature[(signature.IndexOf(';') + 1)..],
                _lastInputDiagnosticSignature,
                System.StringComparison.Ordinal))
        {
            return;
        }

        _lastInputDiagnosticSignature = signature[(signature.IndexOf(';') + 1)..];
        Log.Info(InputDiagnosticsCategory, signature);
    }
}
