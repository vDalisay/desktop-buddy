using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Small flat icons — the Build window's sliders, stats and actions, and the Delete, Duplicate and
/// Reset buttons of every other window (owner note 2026-09-11) — authored here as tiny SVGs in the
/// Win98 ink-and-fill style and rasterised once.
/// </summary>
public static class Win98Icons
{
    /// <summary>
    /// Gives an action button the icon its label's verb has, if any ("Delete...", "Remove Focused",
    /// "Reset Room" and so on), so the same action looks the same in every window.
    /// </summary>
    public static Button Decorate(Button button)
    {
        string verb = button.Text.Split(' ')[0].TrimEnd('.').ToLowerInvariant();
        string? icon = verb switch
        {
            "delete" or "remove" => "delete",
            "duplicate" => "duplicate",
            "reset" => "reset",
            _ => null,
        };
        if (icon is not null && button.Icon is null)
            button.Icon = Get(icon);
        return button;
    }

    private const string Ink = "#2a2118";
    private static readonly Dictionary<string, Texture2D> Cache = [];

    private static readonly Dictionary<string, string> Shapes = new()
    {
        ["mass"] = $"<path d='M5 6h6l2.5 8h-11z' fill='#8f9bab' stroke='{Ink}'/><circle cx='8' cy='4' r='2' fill='none' stroke='{Ink}'/>",
        ["bounce"] = $"<path d='M4 2h8M4 14h8M4 2l8 3-8 3 8 3-8 3' fill='none' stroke='{Ink}' stroke-width='1.5'/>",
        ["gravity"] = $"<path d='M8 2v10M4 8l4 5 4-5' fill='none' stroke='{Ink}' stroke-width='2'/>",
        ["length"] = $"<path d='M1 8h14M4 5l-3 3 3 3M12 5l3 3-3 3' fill='none' stroke='{Ink}' stroke-width='1.5'/>",
        ["thickness"] = $"<path d='M8 1v14M5 4l3-3 3 3M5 12l3 3 3-3' fill='none' stroke='{Ink}' stroke-width='1.5'/>",
        ["push"] = $"<rect x='3' y='10' width='10' height='5' fill='#8e8e8a' stroke='{Ink}'/><rect x='3' y='2' width='10' height='3' fill='#b4813f' stroke='{Ink}'/><path d='M8 5v5' stroke='{Ink}' stroke-width='2'/>",
        ["timer"] = $"<circle cx='8' cy='8' r='6.5' fill='#fff' stroke='{Ink}' stroke-width='1.5'/><path d='M8 4v4h3' fill='none' stroke='{Ink}' stroke-width='1.5'/>",
        ["strength"] = $"<rect x='1.5' y='5' width='8' height='6' rx='3' fill='none' stroke='{Ink}' stroke-width='1.6'/><rect x='6.5' y='5' width='8' height='6' rx='3' fill='none' stroke='{Ink}' stroke-width='1.6'/>",
        ["stretch"] = $"<path d='M1 8h2l1.5-4 3 8 3-8 1.5 4h3' fill='none' stroke='{Ink}' stroke-width='1.5'/>",
        ["stiffness"] = $"<circle cx='8' cy='8' r='2.2' fill='#d0d4d8' stroke='{Ink}'/><path d='M8 1.5a6.5 6.5 0 1 1-6.2 4.5' fill='none' stroke='{Ink}' stroke-width='1.5'/>",
        ["material"] = $"<rect x='2' y='4' width='12' height='8' fill='#b4813f' stroke='{Ink}'/><path d='M3 7h10M3 10h7' stroke='#7a5424'/>",
        ["size"] = $"<rect x='1' y='5' width='14' height='6' fill='#e8d9a0' stroke='{Ink}'/><path d='M4 5v3M7 5v2M10 5v3M13 5v2' stroke='{Ink}'/>",
        ["color"] = $"<path d='M8 2c3 4 4.5 6 4.5 8a4.5 4.5 0 0 1-9 0c0-2 1.5-4 4.5-8z' fill='#2e6fd8' stroke='{Ink}'/>",
        ["duplicate"] = $"<rect x='2' y='2' width='8' height='10' fill='#fff' stroke='{Ink}'/><rect x='6' y='5' width='8' height='10' fill='#fff' stroke='{Ink}'/>",
        ["delete"] = $"<path d='M3 4h10M6 4V2h4v2M4.5 4l1 10h5l1-10' fill='#d0d4d8' stroke='{Ink}'/><path d='M7 6v6M9 6v6' stroke='{Ink}'/>",
        ["reset"] = $"<path d='M13 8a5 5 0 1 1-2-4' fill='none' stroke='#000080' stroke-width='2'/><path d='M9 1l3 3-4 1z' fill='#000080'/>",
        ["unlink"] = $"<rect x='1' y='5' width='6' height='6' rx='3' fill='none' stroke='{Ink}' stroke-width='1.6'/><rect x='9' y='5' width='6' height='6' rx='3' fill='none' stroke='{Ink}' stroke-width='1.6'/><path d='M7.5 3l1 10' stroke='#c0392b' stroke-width='1.5'/>",
    };

    /// <summary>The icon at <paramref name="pixels"/> square, or null for a name this file does not draw.</summary>
    public static Texture2D? Get(string name, int pixels = 20)
    {
        string key = $"{name}@{pixels}";
        if (Cache.TryGetValue(key, out Texture2D? cached))
            return cached;
        if (!Shapes.TryGetValue(name, out string? shape))
            return null;

        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 16 16'>{shape}</svg>";
        var image = new Image();
        if (image.LoadSvgFromString(svg, pixels / 16.0f) != Error.Ok)
            return null;
        Texture2D texture = ImageTexture.CreateFromImage(image);
        Cache[key] = texture;
        return texture;
    }
}
