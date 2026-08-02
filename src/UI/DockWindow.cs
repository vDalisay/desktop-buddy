using System;
using Godot;

namespace DesktopBuddy.Ui;

/// <summary>
/// A free-floating desktop window for one dock panel. Godot gives it a real title bar the
/// player drags, so it can sit anywhere on the desktop rather than inside the transparent
/// buddy box, and being its own HWND it is unaffected by Work-Mode click-through regions.
/// Closing hides it — the panel it hosts is built once and kept.
/// </summary>
public partial class DockWindow : Window
{
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
        // The gameplay tree pauses (tray, character editor); these windows keep working.
        ProcessMode = ProcessModeEnum.Always;

        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(content);
        CloseRequested += Hide;
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
        // A hidden window has no focus to take; do it after it exists on screen.
        GrabFocus();
    }
}
