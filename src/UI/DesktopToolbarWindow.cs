using System;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.UI.Win98;
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

    public HBoxContainer Bar { get; private set; } = null!;

    public void Configure()
    {
        Name = nameof(DesktopToolbarWindow);
        Title = "Desktop Buddy Controls";
        Size = ToolbarSize;
        MinSize = ToolbarSize;
        Borderless = true;

        // The recovery toolbar does not need per-pixel transparency. Keeping it opaque makes
        // the buttons unambiguous and avoids transparent-child-window compositor edge cases.
        Transparent = false;
        Unresizable = true;

        // Transient child of the main window: Windows keeps an owned window above its owner,
        // including a non-exclusive full-screen owner, so no topmost flag is needed here.
        DockWindow.ApplyOwnedWindowFlags(this);

        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            Theme = Win98ThemeFactory.Create(),
        };
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        AddChild(panel);

        Bar = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        Bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Bar.AddThemeConstantOverride("separation", 6);
        panel.AddChild(Bar);
        // The toolbar is the only recovery surface in full-screen Work; it must not be closable.
        CloseRequested += Show;

        Log.Info(DiagnosticsCategory,
            $"Native toolbar configured: transient={Transient} alwaysOnTop={AlwaysOnTop} " +
            $"transparent={Transparent} visible={Visible} size={Size}.");
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
