using System;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Ui;

/// <summary>
/// Recovery and dock controls for the full-screen overlay only. Compact mode keeps its
/// controls inside the main window; this native window exists solely because full-screen Work
/// passes the main window through to the desktop.
/// </summary>
public partial class DesktopToolbarWindow : Window
{
    private const string DiagnosticsCategory = "ToolbarDiagnostics";
    public static readonly Vector2I ToolbarSize = new(480, 48);
    private const int HorizontalPadding = 16;

    private int _foregroundFrames;
    private bool _wasVisible;

    public HBoxContainer Bar { get; private set; } = null!;

    public void Configure()
    {
        Log.Info(DiagnosticsCategory, "Configuring native full-screen recovery toolbar.");

        Name = nameof(DesktopToolbarWindow);
        Title = "Desktop Buddy Controls";
        Size = ToolbarSize;
        MinSize = ToolbarSize;
        Borderless = true;

        // The recovery toolbar does not need per-pixel transparency. Keeping it opaque makes
        // the buttons unambiguous and avoids transparent-child-window compositor edge cases.
        Transparent = false;
        AlwaysOnTop = true;
        Unresizable = true;
        Unfocusable = false;
        MousePassthrough = false;
        ProcessMode = ProcessModeEnum.Always;

        // Windows/Godot rejects a transient always-on-top native window. The toolbar must stay
        // above the overlay, so it is an independent top-level window whose owner controls
        // visibility and placement explicitly.
        Transient = false;
        TransientToFocused = false;
        Exclusive = false;

        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.075f, 0.08f, 0.1f, 0.98f),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 5,
            ContentMarginBottom = 5,
        });
        AddChild(panel);

        Bar = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        Bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Bar.AddThemeConstantOverride("separation", 6);
        panel.AddChild(Bar);
        CloseRequested += Show;

        Log.Info(DiagnosticsCategory,
            $"Native toolbar configured: transient={Transient} alwaysOnTop={AlwaysOnTop} " +
            $"transparent={Transparent} visible={Visible} size={Size}.");
    }

    public override void _Process(double delta)
    {
        if (Visible && !_wasVisible)
            _foregroundFrames = 4;

        if (Visible && _foregroundFrames > 0)
        {
            _foregroundFrames--;
            RaiseAboveOwner(grabFocus: _foregroundFrames == 2);
        }

        _wasVisible = Visible;
    }

    public Button AddAction(string text, string name, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var button = new Button
        {
            Text = text,
            Name = name,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(72, 32),
        };
        button.Pressed += action;
        Bar.AddChild(button);
        return button;
    }

    public void RaiseAboveOwner(bool grabFocus = false)
    {
        if (!Visible)
            return;

        // Reassert topmost after the main overlay changes native mode/size. Toggling the flag
        // forces Windows to restack this independent HWND above the monitor-sized overlay.
        AlwaysOnTop = false;
        AlwaysOnTop = true;
        if (grabFocus)
            GrabFocus();
    }

    public void Place(Rect2I mainWindowRect)
    {
        int contentWidth = (int)Math.Ceiling(Bar.GetCombinedMinimumSize().X) + HorizontalPadding;
        int width = Math.Max(ToolbarSize.X, contentWidth);
        Vector2I wantedSize = new(width, ToolbarSize.Y);
        if (Size != wantedSize)
        {
            MinSize = wantedSize;
            Size = wantedSize;
        }

        int x = mainWindowRect.Position.X + Math.Max(0, (mainWindowRect.Size.X - width) / 2);
        int y = mainWindowRect.Position.Y + 8;
        Position = new Vector2I(x, y);
    }
}
