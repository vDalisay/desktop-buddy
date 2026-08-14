using System;
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
    public const int BaseFontSize = 14;

    private static Theme? _shared;

    /// <summary>Interface scale, 1.0 to 2.0. Changed only through <see cref="ApplyScale"/>.</summary>
    public static float Scale { get; private set; } = 1.0f;

    /// <summary>
    /// One theme resource, handed to every panel. Sharing it is what makes
    /// <see cref="ApplyScale"/> live: rebuilding this one resource in place rescales the whole
    /// interface without rebuilding a single panel.
    /// </summary>
    public static Theme Create()
    {
        if (_shared is not null)
            return _shared;
        _shared = new Theme();
        Populate(_shared);
        return _shared;
    }

    /// <summary>Rescales fonts, paddings, and control art. Safe to call before any UI exists.</summary>
    public static void ApplyScale(float scale)
    {
        float clamped = Math.Clamp(scale, 1.0f, 2.0f);
        if (Math.Abs(clamped - Scale) < 0.001f && _shared is not null)
            return;

        Scale = clamped;
        Populate(Create());
    }

    /// <summary>A base-scale pixel count in current interface pixels.</summary>
    public static int Px(int pixels) => Math.Max(1, Mathf.RoundToInt(pixels * Scale));

    private static void Populate(Theme theme)
    {
        theme.DefaultFontSize = Px(BaseFontSize);

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

        // Trackbar: a thin sunken channel with a raised rectangular grabber and no filled
        // portion, which is what the period control looks like.
        var groove = Recessed(Face, 1);
        groove.ContentMarginTop = Px(1);
        groove.ContentMarginBottom = Px(1);
        groove.ContentMarginLeft = 0;
        groove.ContentMarginRight = 0;
        theme.SetStylebox("slider", "HSlider", groove);
        theme.SetStylebox("grabber_area", "HSlider", new StyleBoxEmpty());
        theme.SetStylebox("grabber_area_highlight", "HSlider", new StyleBoxEmpty());
        ImageTexture grabber = GrabberIcon(Face);
        theme.SetIcon("grabber", "HSlider", grabber);
        theme.SetIcon("grabber_highlight", "HSlider", GrabberIcon(Highlight));
        theme.SetIcon("grabber_disabled", "HSlider", grabber);
        theme.SetIcon("tick", "HSlider", TickIcon());

        // Square sunken check field rather than the default rounded switch.
        theme.SetIcon("unchecked", "CheckBox", CheckBoxIcon(checkMark: false));
        theme.SetIcon("checked", "CheckBox", CheckBoxIcon(checkMark: true));
        theme.SetIcon("unchecked_disabled", "CheckBox", CheckBoxIcon(checkMark: false));
        theme.SetIcon("checked_disabled", "CheckBox", CheckBoxIcon(checkMark: true));
        theme.SetStylebox("normal", "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("hover", "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("pressed", "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("hover_pressed", "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("focus", "CheckBox", FocusBox());

        // Drop-downs are ordinary raised buttons carrying a black arrow.
        theme.SetStylebox("normal", "OptionButton", Raised(Face, 2));
        theme.SetStylebox("hover", "OptionButton", Raised(Highlight, 2));
        theme.SetStylebox("pressed", "OptionButton", Recessed(Face, 2));
        theme.SetStylebox("focus", "OptionButton", FocusBox());
        theme.SetStylebox("disabled", "OptionButton", Raised(Face, 2));
        SetFontColors(theme, "OptionButton", Dark, Dark);
        theme.SetIcon("arrow", "OptionButton", ArrowIcon());

        theme.SetStylebox("panel", "PopupMenu", Raised(Face, 2));
        theme.SetStylebox("hover", "PopupMenu", Flat(Selection));
        theme.SetColor("font_color", "PopupMenu", Dark);
        theme.SetColor("font_hover_color", "PopupMenu", Light);

        theme.SetConstant("separation", "HBoxContainer", Px(Gap));
        theme.SetConstant("separation", "VBoxContainer", Px(Gap));
    }

    /// <summary>
    /// The etched hairline frame a period group box is drawn with: one shadow line offset by one
    /// white line, and no fill, so the panel behind shows through.
    /// </summary>
    public static StyleBoxFlat Etched()
    {
        var box = Flat(Colors.Transparent);
        box.DrawCenter = false;
        box.BorderWidthLeft = 1;
        box.BorderWidthTop = 1;
        box.BorderWidthRight = 1;
        box.BorderWidthBottom = 1;
        box.BorderColor = Shadow;
        box.ShadowColor = Light;
        box.ShadowSize = 1;
        box.ShadowOffset = new Vector2(1, 1);
        box.ContentMarginLeft = Px(8);
        box.ContentMarginRight = Px(8);
        box.ContentMarginTop = Px(8);
        box.ContentMarginBottom = Px(8);
        return box;
    }

    /// <summary>The raised trackbar handle: white top-left edge, black-over-grey bottom-right.</summary>
    private static ImageTexture GrabberIcon(Color fill)
    {
        int width = Px(11);
        int height = Px(21);
        return Draw(width, height, image =>
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Color color = fill;
                    if (x == width - 1 || y == height - 1)
                        color = Dark;
                    else if (x == width - 2 || y == height - 2)
                        color = Shadow;
                    else if (x == 0 || y == 0)
                        color = Light;
                    image.SetPixel(x, y, color);
                }
            }
        });
    }

    private static ImageTexture TickIcon() =>
        Draw(Px(1), Px(3), image =>
        {
            for (int y = 0; y < image.GetHeight(); y++)
                image.SetPixel(0, y, Shadow);
        });

    /// <summary>A 13x13 sunken white field, optionally carrying the check.</summary>
    private static ImageTexture CheckBoxIcon(bool checkMark)
    {
        int size = Px(13);
        return Draw(size, size, image =>
        {
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    Color color = Light;
                    if (x == size - 1 || y == size - 1)
                        color = Light;
                    else if (x == size - 2 || y == size - 2)
                        color = Highlight;
                    else if (x == 0 || y == 0)
                        color = Shadow;
                    else if (x == 1 || y == 1)
                        color = Dark;
                    image.SetPixel(x, y, color);
                }
            }

            if (!checkMark)
                return;

            // Two strokes, thickness following the scale: down-right into the corner, then
            // up-right. Drawn proportionally so the tick still fills a scaled box.
            int unit = Math.Max(1, size / 13);
            int thickness = Math.Max(2, unit * 2);
            DrawStroke(image, size * 3 / 13, size * 5 / 13, 1, 1, size * 3 / 13, thickness);
            DrawStroke(image, size * 6 / 13, size * 7 / 13, 1, -1, size * 4 / 13, thickness);
        });
    }

    private static ImageTexture ArrowIcon()
    {
        int width = Px(7);
        int height = Px(4);
        return Draw(width, height, image =>
        {
            for (int row = 0; row < height; row++)
            {
                int inset = row * width / (height * 2);
                for (int x = inset; x < width - inset; x++)
                    image.SetPixel(x, row, Dark);
            }
        });
    }

    /// <summary>One diagonal run of the check mark, clipped to the image.</summary>
    private static void DrawStroke(
        Image image,
        int x,
        int y,
        int stepX,
        int stepY,
        int length,
        int thickness)
    {
        for (int step = 0; step < length; step++)
        {
            for (int offset = 0; offset < thickness; offset++)
            {
                int pixelX = x + (step * stepX);
                int pixelY = y + (step * stepY) + offset;
                if (pixelX >= 0 && pixelY >= 0 &&
                    pixelX < image.GetWidth() && pixelY < image.GetHeight())
                {
                    image.SetPixel(pixelX, pixelY, Dark);
                }
            }
        }
    }

    private static ImageTexture Draw(int width, int height, Action<Image> paint)
    {
        Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        paint(image);
        return ImageTexture.CreateFromImage(image);
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
            ContentMarginLeft = Px(4),
            ContentMarginTop = Px(3),
            ContentMarginRight = Px(4),
            ContentMarginBottom = Px(3),
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
        box.BorderWidthLeft = Px(width);
        box.BorderWidthTop = Px(width);
        box.BorderWidthRight = Px(width);
        box.BorderWidthBottom = Px(width);
        box.BorderColor = border;
        box.ShadowColor = highlight;
        box.ShadowSize = 1;
        box.ShadowOffset = highlightOffset;
    }
}
