using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Paint UI localization boundary. Godot translations may override these keys; the locked
/// English table is the shipped fallback so an untranslated locale never displays a raw key.
/// </summary>
internal static class PaintUiText
{
    public const string Open = "character_editor.paint.open";
    public const string OpenTooltip = "character_editor.paint.open_tooltip";
    public const string AppearanceControls = "character_editor.paint.appearance_controls";
    public const string Brush = "character_editor.paint.tool.brush";
    public const string Eraser = "character_editor.paint.tool.eraser";
    public const string ColorTooltip = "character_editor.paint.color_tooltip";
    public const string BrushSize = "character_editor.paint.brush_size";
    public const string Undo = "character_editor.paint.undo";
    public const string EraseAll = "character_editor.paint.erase_all";
    public const string ZoomOut = "character_editor.paint.zoom_out";
    public const string ZoomIn = "character_editor.paint.zoom_in";
    public const string ResetView = "character_editor.paint.reset_view";
    public const string HoverHelp = "character_editor.paint.hover_help";
    public const string InputHelp = "character_editor.paint.input_help";
    public const string EraseAllTitle = "character_editor.paint.erase_all.title";
    public const string EraseAllBody = "character_editor.paint.erase_all.body";
    public const string Canvas = "character_editor.paint.canvas";
    public const string Status = "character_editor.paint.status";

    private static readonly IReadOnlyDictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Open] = "Paint",
            [OpenTooltip] = "Paint directly on the buddy body.",
            [AppearanceControls] = "Appearance Controls",
            [Brush] = "Brush",
            [Eraser] = "Eraser",
            [ColorTooltip] = "Choose an opaque paint color.",
            [BrushSize] = "Brush size",
            [Undo] = "Undo",
            [EraseAll] = "Erase All",
            [ZoomOut] = "Zoom −",
            [ZoomIn] = "Zoom +",
            [ResetView] = "Reset View",
            [HoverHelp] = "Move over a body part to paint.",
            [InputHelp] = "Left drag: paint • Wheel: brush size • Middle drag or Space+drag: pan • Ctrl+wheel: zoom",
            [EraseAllTitle] = "Erase all paint?",
            [EraseAllBody] = "This clears paint from all six body parts. You can undo it once confirmed.",
            [Canvas] = "Canvas",
            [Status] = "{0} • {1} • Zoom {2:0.0}×",
        };

    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        string translated = TranslationServer.Translate(key).ToString();
        if (!string.IsNullOrWhiteSpace(translated) &&
            !string.Equals(translated, key, StringComparison.Ordinal))
        {
            return translated;
        }
        return English.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    public static string Format(string key, params object[] arguments) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), arguments);

    public static bool HasEnglishFallback(string key) => English.ContainsKey(key);
}
