using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Shared popup-menu skin for the Win98 application shell and editor menus.</summary>
public static class Win98MenuStyle
{
    public static void Apply(PopupMenu popup)
    {
        popup.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        popup.AddThemeStyleboxOverride("hover", Win98ThemeFactory.Flat(Win98ThemeFactory.Selection));
        popup.AddThemeColorOverride("font_color", Win98ThemeFactory.Dark);
        popup.AddThemeColorOverride("font_hover_color", Win98ThemeFactory.Light);
        popup.AddThemeColorOverride("font_disabled_color", Win98ThemeFactory.Shadow);
    }
}
