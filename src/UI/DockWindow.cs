using System;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Ui;

/// <summary>
/// A free-floating desktop window for one dock panel. These windows are independent top-level
/// HWNDs and remain above the transparent full-screen gameplay overlay, so their controls stay
/// visible and interactive in both compact and full-screen layouts.
/// Closing hides the window; the hosted panel is built once and retained.
/// </summary>
public partial class DockWindow : Window
{
    private const string DiagnosticsCategory = "DockWindow";

    private int _foregroundFrames;
    private bool _wasVisible;

    public int OpenCount { get; private set; }

    /// <summary>Raised whenever the window is shown, so the panel can refresh first.</summary>
    public event Action? Opening;

    public void Configure(string title, Vector2I size, Control content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Title = title;
        Name = title.Replace(" ", string.Empty) + "Window";
        Size = size;
        MinSize = size;
        Unresizable = false;
        Visible = false;

        // The full-screen gameplay overlay is always-on-top. Dock windows must join that native
        // topmost band and be independent, otherwise the overlay covers them and captures every
        // click before Windows can deliver it to their controls.
        AlwaysOnTop = true;
        Unfocusable = false;
        MousePassthrough = false;
        Transient = false;
        TransientToFocused = false;
        Exclusive = false;

        // The gameplay tree pauses for tray/editor transitions; dock windows keep working.
        ProcessMode = ProcessModeEnum.Always;

        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(content);
        CloseRequested += Hide;
        FocusEntered += RaiseAboveOverlay;

        Log.Info(DiagnosticsCategory,
            $"Configured {Name}: alwaysOnTop={AlwaysOnTop} transient={Transient} " +
            $"mousePassthrough={MousePassthrough} size={Size}.");
    }

    public override void _Process(double delta)
    {
        if (Visible && !_wasVisible)
            _foregroundFrames = 4;

        if (Visible && _foregroundFrames > 0)
        {
            _foregroundFrames--;
            RaiseAboveOverlay();
            if (_foregroundFrames == 2)
                GrabFocus();
        }

        _wasVisible = Visible;
    }

    /// <summary>Show or hide the window, placing a first open near the game window.</summary>
    public void Toggle(Vector2I anchor)
    {
        if (Visible)
        {
            Hide();
            return;
        }

        if (OpenCount == 0)
            Position = anchor;
        OpenCount++;
        Opening?.Invoke();
        Show();
        _foregroundFrames = 4;
        RaiseAboveOverlay();
        GrabFocus();

        Log.Info(DiagnosticsCategory,
            $"Opened {Name}: position={Position} size={Size} focus={HasFocus()}.");
    }

    private void RaiseAboveOverlay()
    {
        if (!Visible)
            return;

        // Reasserting the topmost flag forces Windows to restack this HWND above the main
        // monitor-sized overlay, even when that overlay changed mode or regained focus.
        AlwaysOnTop = false;
        AlwaysOnTop = true;
    }
}
