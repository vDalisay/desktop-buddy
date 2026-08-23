using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Original clean-room late-1990s desktop theme used by all game UI.</summary>
public static class Win98ThemeFactory
{
    // The nine shades the interface is drawn from. Three are the player's (the face, the bar
    // and the text); the rest are derived, so picking light pink brings its own bevels and
    // greys along instead of leaving grey edges around pink panels (owner instruction
    // 2026-08-23). They change only through ApplyPalette, and the default palette derives back
    // to the exact values that shipped.
    public static Color Face { get; private set; } = Win98Palette.Default.Face;
    public static Color Light { get; private set; } = Color.Color8(255, 255, 255);
    public static Color Highlight { get; private set; } = Color.Color8(223, 223, 223);
    public static Color Shadow { get; private set; } = Color.Color8(128, 128, 128);
    public static Color Dark { get; private set; } = Win98Palette.Default.Text;
    public static Color ActiveTitle { get; private set; } = Win98Palette.Default.Bar;
    public static Color InactiveTitle { get; private set; } = Color.Color8(128, 128, 128);
    public static Color Selection { get; private set; } = Win98Palette.Default.Bar;
    /// <summary>Deliberately non-period hover blue: complements the navy selection without
    /// being mistaken for it, and keeps white hover text readable.</summary>
    public static Color HoverSelection { get; private set; } = Color.Color8(72, 132, 208);

    /// <summary>Text drawn on top of <see cref="ActiveTitle"/>, light or dark to stay legible.</summary>
    public static Color TitleText { get; private set; } = Color.Color8(255, 255, 255);

    private static readonly Color AuthoredHover = Color.Color8(72, 132, 208);

    /// <summary>Bevel and grey ratios of the face colour: 255/192, 223/192 and 128/192.</summary>
    private const float LightFactor = 255.0f / 192.0f;
    private const float HighlightFactor = 223.0f / 192.0f;
    private const float ShadowFactor = 128.0f / 192.0f;

    /// <summary>The three colours currently in force.</summary>
    public static Win98Palette Palette { get; private set; } = Win98Palette.Default;

    private static readonly string[] ScrollBarTypes = ["VScrollBar", "HScrollBar"];

    public const int Border = 2;
    public const int TitleBarHeight = 32;
    public const int StatusBarHeight = 26;
    /// <summary>Centered world inset that places its floor on the status bar's top edge.</summary>
    public const int ChromeHeight = 72;
    public const int ControlHeight = 24;

    /// <summary>
    /// Title-bar commands are square, not oblong: Win98 draws minimise, maximise, close and the
    /// rest as equal boxes, and 20x18 read as stretched (owner report 2026-08-21). Every title
    /// bar builds its buttons from these two so they cannot drift apart again. 18x18 was a
    /// period-accurate size on a period-accurate screen and a stamp on a modern one; the size
    /// now matches what a browser puts in its own chrome (owner report 2026-08-22).
    /// </summary>
    public const int TitleButtonSize = 26;

    /// <summary>
    /// Gap between title-bar commands. The gap to the right of the last one comes from the
    /// title bar's own stylebox margin, which is the same width — an extra spacer child on the
    /// end of the row stacked with the separation and left the close button adrift (owner
    /// report 2026-08-21).
    /// </summary>
    public const int TitleButtonGap = 4;
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
        Repopulate(Create());
    }

    /// <summary>
    /// Re-skins the whole interface. The shared theme is repopulated and every style box the
    /// factory ever handed out is re-tinted in place, so panels built long ago follow along
    /// without being rebuilt: the same trick that makes <see cref="ApplyScale"/> live.
    /// </summary>
    public static void ApplyPalette(Win98Palette palette)
    {
        // Every settings edit re-applies the whole presentation, so this is called far more
        // often than the palette actually changes. Repainting the interface each time a volume
        // slider ticks is what made changing anything feel expensive.
        if (palette == Palette && _shared is not null)
            return;

        Palette = palette;
        Face = palette.Face;
        Light = Win98Palette.Scaled(palette.Face, LightFactor);
        Highlight = Win98Palette.Scaled(palette.Face, HighlightFactor);
        Shadow = Win98Palette.Scaled(palette.Face, ShadowFactor);
        Dark = palette.Text;
        ActiveTitle = palette.Bar;
        Selection = palette.Bar;
        // The shipped hover blue is hand-picked rather than a shade of the navy, so it is kept
        // literally for the default bar and derived for anything the player chooses.
        HoverSelection = palette.Bar == Win98Palette.Default.Bar
            ? AuthoredHover
            : palette.Bar.Lightened(0.4f);
        InactiveTitle = Shadow;
        TitleText = Win98Palette.WantsLightText(palette.Bar) ? Light : palette.Text;

        RetintRegisteredBoxes();
        RecolorTrackedTitleLabels();
        RepaintTrackedPainters();
        Repopulate(Create());
        PaletteChanged?.Invoke();
    }

    /// <summary>Raised once per real palette change, after the theme has been rebuilt.</summary>
    public static event Action? PaletteChanged;

    /// <summary>
    /// Rewrites the shared theme as one change instead of a hundred. Every SetStylebox on a
    /// live theme walks the whole control tree — re-reading fonts, re-measuring minimum sizes —
    /// so a hundred of them is a hundred full-tree passes and a visible freeze. Blocking the
    /// resource's own signal while it is rewritten collapses that into a single pass.
    /// </summary>
    private static void Repopulate(Theme theme)
    {
        theme.SetBlockSignals(true);
        try
        {
            Populate(theme);
        }
        finally
        {
            theme.SetBlockSignals(false);
        }
        theme.EmitChanged();
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

        // Scroll bars were the one common control the theme never claimed, so every scrolling
        // list kept Godot's default grey rails down its side - including after the player
        // repainted everything else (owner report 2026-08-23). A sunken light trough with a
        // raised face-coloured grabber is both the period look and palette-driven.
        foreach (string bar in ScrollBarTypes)
        {
            StyleBoxFlat trough = Recessed(Highlight, 1);
            trough.ContentMarginLeft = Px(6);
            trough.ContentMarginRight = Px(6);
            trough.ContentMarginTop = Px(6);
            trough.ContentMarginBottom = Px(6);
            theme.SetStylebox("scroll", bar, trough);
            theme.SetStylebox("scroll_focus", bar, trough);
            theme.SetStylebox("grabber", bar, Raised(Face, 2));
            theme.SetStylebox("grabber_highlight", bar, Raised(Highlight, 2));
            theme.SetStylebox("grabber_pressed", bar, Raised(Face, 2));
        }

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
        StyleBoxFlat box = NewFlat(fill);
        ConfigureBorder(box, width, Shadow, Light, new Vector2(-1, -1));
        return Register(box, BoxKind.Raised, fill, width);
    }

    public static StyleBoxFlat Recessed(Color fill, int width = Border)
    {
        StyleBoxFlat box = NewFlat(fill);
        ConfigureBorder(box, width, Dark, Shadow, new Vector2(1, 1));
        return Register(box, BoxKind.Recessed, fill, width);
    }

    /// <summary>
    /// The square, compact look every title-bar command shares. The ordinary Button stylebox
    /// carries content margins and the shell font size, which together give a minimum height
    /// well over <see cref="TitleButtonSize"/> — so a button asked to be 18x18 still rendered
    /// as an upright oblong (owner report 2026-08-21). Margins go, the glyph shrinks, and the
    /// row is told not to stretch it.
    /// </summary>
    public static void StyleTitleButton(Button button)
    {
        button.CustomMinimumSize = new Vector2(Px(TitleButtonSize), Px(TitleButtonSize));
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        button.ClipText = true;
        button.AddThemeFontSizeOverride("font_size", Px(16));
        button.AddThemeStyleboxOverride("normal", Compact(Raised(Face, 2)));
        button.AddThemeStyleboxOverride("hover", Compact(Raised(Highlight, 2)));
        button.AddThemeStyleboxOverride("pressed", Compact(Recessed(Face, 2)));
        button.AddThemeStyleboxOverride("hover_pressed", Compact(Recessed(Highlight, 2)));
        button.AddThemeStyleboxOverride("disabled", Compact(Raised(Face, 2)));
        button.AddThemeColorOverride("font_color", Dark);
        button.AddThemeColorOverride("font_hover_color", Dark);
        button.AddThemeColorOverride("font_pressed_color", Dark);
    }

    /// <summary>The same box with its content margins removed, so it can be square.</summary>
    private static StyleBoxFlat Compact(StyleBoxFlat box)
    {
        box.ContentMarginLeft = 0;
        box.ContentMarginTop = 0;
        box.ContentMarginRight = 0;
        box.ContentMarginBottom = 0;
        return box;
    }

    public static StyleBoxFlat Flat(Color fill) => Register(NewFlat(fill), BoxKind.Flat, fill, 0);

    private static StyleBoxFlat NewFlat(Color fill)
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

    // --- Live re-tinting -------------------------------------------------------------------
    // Style boxes are resources: every control handed one keeps a reference to that exact
    // object, so changing its colours repaints it. The factory therefore remembers what each
    // box it made was asked for - which palette shade, which bevel, which width - and rebuilds
    // those colours on demand. References are weak, so a freed panel's boxes are not kept
    // alive by this list.

    private enum BoxKind { Flat, Raised, Recessed }

    private enum Shade
    {
        None, Face, Light, Highlight, Shadow, Dark, ActiveTitle, InactiveTitle, Selection, HoverSelection,
    }

    private sealed record TintedBox(WeakReference<StyleBoxFlat> Box, BoxKind Kind, Shade Fill, int Width);

    private static readonly List<TintedBox> Tinted = new();
    private static int _tintedPruneAt = 512;
    private static readonly List<WeakReference<Label>> TitleLabels = new();
    private static readonly List<WeakReference<CanvasItem>> Painters = new();

    /// <summary>
    /// A label drawn on a title bar. Registered rather than merely coloured, so a palette
    /// change moves it with the bar underneath it instead of leaving white text on a pale one.
    /// </summary>
    public static Label TitleLabel(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);
        label.AddThemeColorOverride("font_color", TitleText);
        TitleLabels.Add(new WeakReference<Label>(label));
        if (TitleLabels.Count > 128)
            TitleLabels.RemoveAll(entry => !entry.TryGetTarget(out _));
        return label;
    }

    /// <summary>
    /// A control that paints palette colours itself in <c>_Draw</c>. Godot only repaints a
    /// control when something invalidates it, and a static colour changing is not something it
    /// can see, so those surfaces have to be asked (owner report 2026-08-23).
    /// </summary>
    public static T RepaintOnPaletteChange<T>(T item)
        where T : CanvasItem
    {
        ArgumentNullException.ThrowIfNull(item);
        Painters.Add(new WeakReference<CanvasItem>(item));
        if (Painters.Count > 128)
            Painters.RemoveAll(entry => !entry.TryGetTarget(out _));
        return item;
    }

    private static void RepaintTrackedPainters()
    {
        Painters.RemoveAll(entry => !entry.TryGetTarget(out _));
        foreach (WeakReference<CanvasItem> entry in Painters)
        {
            if (entry.TryGetTarget(out CanvasItem? item) && GodotObject.IsInstanceValid(item))
                item.QueueRedraw();
        }
    }

    private static StyleBoxFlat Register(StyleBoxFlat box, BoxKind kind, Color fill, int width)
    {
        Shade shade = ShadeOf(fill);
        if (shade == Shade.None)
            return box;

        Tinted.Add(new TintedBox(new WeakReference<StyleBoxFlat>(box), kind, shade, width));
        if (Tinted.Count > _tintedPruneAt)
        {
            // Prune dead references, then take the next sweep at twice what survived. Sweeping
            // on every registration past a fixed cap would make building a big panel quadratic,
            // and this list is appended to by every control the interface builds.
            Tinted.RemoveAll(entry => !entry.Box.TryGetTarget(out _));
            _tintedPruneAt = Math.Max(512, Tinted.Count * 2);
        }
        return box;
    }

    /// <summary>
    /// Which palette shade a caller asked for, by value. Call sites pass the factory's own
    /// colours, so an exact match is the whole test; anything else - a paint swatch, a
    /// transparent focus box - is not palette-driven and is left exactly as its owner built it.
    /// </summary>
    private static Shade ShadeOf(Color fill)
    {
        if (fill == Face) return Shade.Face;
        if (fill == Light) return Shade.Light;
        if (fill == Highlight) return Shade.Highlight;
        if (fill == Shadow) return Shade.Shadow;
        if (fill == Dark) return Shade.Dark;
        if (fill == ActiveTitle) return Shade.ActiveTitle;
        if (fill == InactiveTitle) return Shade.InactiveTitle;
        if (fill == Selection) return Shade.Selection;
        if (fill == HoverSelection) return Shade.HoverSelection;
        return Shade.None;
    }

    private static Color ColorOf(Shade shade) => shade switch
    {
        Shade.Face => Face,
        Shade.Light => Light,
        Shade.Highlight => Highlight,
        Shade.Shadow => Shadow,
        Shade.Dark => Dark,
        Shade.ActiveTitle => ActiveTitle,
        Shade.InactiveTitle => InactiveTitle,
        Shade.Selection => Selection,
        Shade.HoverSelection => HoverSelection,
        _ => Face,
    };

    private static void RetintRegisteredBoxes()
    {
        Tinted.RemoveAll(entry => !entry.Box.TryGetTarget(out _));
        foreach (TintedBox entry in Tinted)
        {
            if (!entry.Box.TryGetTarget(out StyleBoxFlat? box) || !GodotObject.IsInstanceValid(box))
                continue;

            // Only the colours are rebuilt. Owners that adjusted a box after taking it - a
            // squared-off title button, an outline with no centre - keep those adjustments.
            // Signals are blocked across the rewrite for the same reason the theme's are: each
            // property assignment would otherwise repaint every control holding this box.
            box.SetBlockSignals(true);
            try
            {
                box.BgColor = ColorOf(entry.Fill);
                switch (entry.Kind)
                {
                    case BoxKind.Raised:
                        ConfigureBorder(box, entry.Width, Shadow, Light, new Vector2(-1, -1));
                        break;
                    case BoxKind.Recessed:
                        ConfigureBorder(box, entry.Width, Dark, Shadow, new Vector2(1, 1));
                        break;
                }
            }
            finally
            {
                box.SetBlockSignals(false);
            }
            box.EmitChanged();
        }
    }

    private static void RecolorTrackedTitleLabels()
    {
        TitleLabels.RemoveAll(entry => !entry.TryGetTarget(out _));
        foreach (WeakReference<Label> entry in TitleLabels)
        {
            if (entry.TryGetTarget(out Label? label) && GodotObject.IsInstanceValid(label))
                label.AddThemeColorOverride("font_color", TitleText);
        }
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
