using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Original clean-room late-1990s desktop theme used by all game UI.</summary>
public static class Win98ThemeFactory
{
    public static readonly Color Face = Color.Color8(192, 192, 192);
    public static readonly Color Light = Color.Color8(255, 255, 255);
    public static readonly Color Highlight = Color.Color8(223, 223, 223);
    public static readonly Color Shadow = Color.Color8(128, 128, 128);
    public static readonly Color Dark = Color.Color8(0, 0, 0);
    public static readonly Color ActiveTitle = Color.Color8(0, 0, 128);
    public static readonly Color InactiveTitle = Color.Color8(128, 128, 128);
    public static readonly Color Selection = Color.Color8(0, 0, 128);
    /// <summary>Deliberately non-period hover blue: complements the navy selection without
    /// being mistaken for it, and keeps white hover text readable.</summary>
    public static readonly Color HoverSelection = Color.Color8(72, 132, 208);

    public const int Border = 2;
    public const int TitleBarHeight = 22;
    public const int StatusBarHeight = 22;
    /// <summary>Centered world inset that places its floor on the status bar's top edge.</summary>
    public const int ChromeHeight = 58;
    public const int ControlHeight = 24;
    public const int Gap = 4;

    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = 14 };

        SetFontColors(theme, "Label", Dark, Dark);
        SetFontColors(theme, "Button", Dark, Dark);
        SetFontColors(theme, "CheckBox", Dark, Dark);
        SetFontColors(theme, "LineEdit", Dark, Dark);

        theme.SetStylebox("panel", "PanelContainer", Raised(Face, 2));
        theme.SetStylebox("normal", "Button", Raised(Face, 2));
        theme.SetStylebox("hover", "Button", Raised(Highlight, 2));
        theme.SetStylebox("pressed", "Button", Recessed(Face, 2));
        theme.SetStylebox("focus", "Button", FocusBox());
        theme.SetStylebox("disabled", "Button", Raised(Face, 2));
        theme.SetColor("font_disabled_color", "Button", Shadow);

        theme.SetStylebox("normal", "LineEdit", Recessed(Light, 2));
        theme.SetStylebox("focus", "LineEdit", Recessed(Light, 2));
        theme.SetStylebox("read_only", "LineEdit", Recessed(Face, 2));

        theme.SetStylebox("panel", "ScrollContainer", Recessed(Light, 2));
        theme.SetStylebox("panel", "PopupPanel", Raised(Face, 2));
        theme.SetStylebox("panel", "ItemList", Recessed(Light, 2));
        theme.SetColor("font_color", "ItemList", Dark);
        theme.SetColor("font_selected_color", "ItemList", Light);
        theme.SetColor("font_hovered_color", "ItemList", Light);
        theme.SetColor("font_hovered_selected_color", "ItemList", Light);
        theme.SetStylebox("selected", "ItemList", Flat(Selection));
        theme.SetStylebox("selected_focus", "ItemList", Flat(Selection));
        // Hover must fill, not just recolor the glyphs: white text on the light list background
        // was unreadable.
        theme.SetStylebox("hovered", "ItemList", Flat(HoverSelection));
        theme.SetStylebox("hovered_selected", "ItemList", Flat(HoverSelection));
        theme.SetStylebox("hovered_selected_focus", "ItemList", Flat(HoverSelection));

        theme.SetConstant("separation", "HBoxContainer", Gap);
        theme.SetConstant("separation", "VBoxContainer", Gap);
        return theme;
    }

    public static StyleBoxFlat Raised(Color fill, int width = Border)
    {
        var box = Flat(fill);
        ConfigureBorder(box, width, Shadow, Light, new Vector2(-1, -1));
        return box;
    }

    public static StyleBoxFlat Recessed(Color fill, int width = Border)
    {
        var box = Flat(fill);
        ConfigureBorder(box, width, Dark, Shadow, new Vector2(1, 1));
        return box;
    }

    public static StyleBoxFlat Flat(Color fill)
    {
        return new StyleBoxFlat
        {
            BgColor = fill,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
            ContentMarginLeft = 4,
            ContentMarginTop = 3,
            ContentMarginRight = 4,
            ContentMarginBottom = 3,
        };
    }

    public static StyleBoxFlat FocusBox()
    {
        var box = Flat(Colors.Transparent);
        box.BorderWidthLeft = 1;
        box.BorderWidthTop = 1;
        box.BorderWidthRight = 1;
        box.BorderWidthBottom = 1;
        box.BorderColor = Dark;
        box.DrawCenter = false;
        return box;
    }

    private static void SetFontColors(Theme theme, string type, Color normal, Color focus)
    {
        theme.SetColor("font_color", type, normal);
        theme.SetColor("font_hover_color", type, normal);
        theme.SetColor("font_pressed_color", type, normal);
        theme.SetColor("font_hover_pressed_color", type, normal);
        theme.SetColor("font_focus_color", type, focus);
    }

    private static void ConfigureBorder(
        StyleBoxFlat box,
        int width,
        Color border,
        Color highlight,
        Vector2 highlightOffset)
    {
        box.BorderWidthLeft = width;
        box.BorderWidthTop = width;
        box.BorderWidthRight = width;
        box.BorderWidthBottom = width;
        box.BorderColor = border;
        box.ShadowColor = highlight;
        box.ShadowSize = 1;
        box.ShadowOffset = highlightOffset;
    }
}
