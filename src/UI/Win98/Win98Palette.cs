using System;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// The three colours the player chooses for the interface (owner instruction 2026-08-23):
/// the window face every panel and button is made of, the bar colour every title bar and
/// selection uses, and the font colour of ordinary text.
///
/// <para>Every other shade the theme needs — the light and dark bevels, the hover fill, the
/// disabled grey — is derived from these three by <see cref="Win98ThemeFactory"/> rather than
/// being a fourth thing to pick. The ratios are chosen so the shipped grey/navy/black palette
/// derives back to exactly the shipped look, which is what makes "Default" a true restore.</para>
/// </summary>
public readonly record struct Win98Palette(Color Face, Color Bar, Color Text)
{
    /// <summary>The period defaults: 192 grey, navy, black.</summary>
    public static Win98Palette Default => new(
        Color.Color8(192, 192, 192),
        Color.Color8(0, 0, 128),
        Color.Color8(0, 0, 0));

    public bool IsDefault => this == Default;

    /// <summary>
    /// Reads a stored palette. Anything unparseable falls back to that channel's default:
    /// a corrupt settings file must not be able to leave the interface unreadable.
    /// </summary>
    public static Win98Palette Parse(string? face, string? bar, string? text) => new(
        ParseChannel(face, Default.Face),
        ParseChannel(bar, Default.Bar),
        ParseChannel(text, Default.Text));

    public string FaceHex => Format(Face);
    public string BarHex => Format(Bar);
    public string TextHex => Format(Text);

    private static Color ParseChannel(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;
        string trimmed = hex.Trim().TrimStart('#');
        return trimmed.Length is 6 or 8 && Color.HtmlIsValid(trimmed)
            ? Color.FromHtml(trimmed)
            : fallback;
    }

    private static string Format(Color color) =>
        color.ToHtml(includeAlpha: false).ToLowerInvariant();

    /// <summary>
    /// A shade of one palette colour, as a straight multiple of its channels. Scaling rather
    /// than blending toward white is what reproduces the originals exactly: the shipped
    /// bevels really are 255/192, 223/192 and 128/192 of the shipped face grey.
    /// </summary>
    public static Color Scaled(Color color, float factor) => new(
        Math.Clamp(color.R * factor, 0.0f, 1.0f),
        Math.Clamp(color.G * factor, 0.0f, 1.0f),
        Math.Clamp(color.B * factor, 0.0f, 1.0f),
        color.A);

    /// <summary>
    /// Whether text over this colour should be light. Perceived luminance, so a bright bar
    /// gets dark text instead of the period white that would vanish into it.
    /// </summary>
    public static bool WantsLightText(Color background) =>
        (0.299f * background.R) + (0.587f * background.G) + (0.114f * background.B) < 0.6f;
}
