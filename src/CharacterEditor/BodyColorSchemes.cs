using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.CharacterEditor;

/// <summary>One authored body colouring: the four part tints a buddy's body is made of.</summary>
public readonly record struct BodyColorScheme(
    string Id,
    string Name,
    Rgba32 Head,
    Rgba32 Torso,
    Rgba32 Hand,
    Rgba32 Foot);

/// <summary>
/// The body colourings the Studio offers as styles. The built-in blue buddy is the first and
/// free one, so the body he ships with is a choice like any other rather than the one look the
/// Studio could not put back (owner instruction 2026-08-22).
///
/// <para>Every other scheme is derived from one base tint through the same lighten steps the
/// built-in colours already use, so adding a colouring is a single line and none of them can
/// drift out of the built-in's proportions.</para>
/// </summary>
public static class BodyColorSchemes
{
    public static IReadOnlyList<BodyColorScheme> All { get; } =
    [
        new BodyColorScheme(
            "body.builtin_blue",
            "Built-in Blue",
            CharacterPartColors.BuiltInHead,
            CharacterPartColors.BuiltInTorso,
            CharacterPartColors.BuiltInHand,
            CharacterPartColors.BuiltInFoot),
        From("body.mint", "Mint", Rgba32.Parse("#45E0A3")),
        From("body.rose", "Rose", Rgba32.Parse("#E0457A")),
        From("body.sand", "Sand", Rgba32.Parse("#E0B045")),
        From("body.plum", "Plum", Rgba32.Parse("#8A5AE0")),
        From("body.slate", "Slate", Rgba32.Parse("#6C7A8A")),
    ];

    public static BodyColorScheme Default => All[0];

    public static bool TryGet(string id, out BodyColorScheme scheme)
    {
        foreach (BodyColorScheme candidate in All)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                scheme = candidate;
                return true;
            }
        }

        scheme = default;
        return false;
    }

    /// <summary>The scheme a document is currently wearing, or null when it is wearing none.</summary>
    public static BodyColorScheme? Match(CharacterPartColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        foreach (BodyColorScheme candidate in All)
        {
            if (candidate.Head == colors.Head && candidate.Torso == colors.Torso &&
                candidate.Hand == colors.LeftHand && candidate.Hand == colors.RightHand &&
                candidate.Foot == colors.LeftFoot && candidate.Foot == colors.RightFoot)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The part a scheme paints one body slot with.</summary>
    public static Rgba32 ColorFor(in BodyColorScheme scheme, CharacterPartSlot slot) => slot switch
    {
        CharacterPartSlot.Head => scheme.Head,
        CharacterPartSlot.Torso => scheme.Torso,
        CharacterPartSlot.LeftHand or CharacterPartSlot.RightHand => scheme.Hand,
        _ => scheme.Foot,
    };

    /// <summary>
    /// A one-off colouring built from any tint the player picked, for the Studio's colour
    /// swatches. It carries no id of its own, so it reads back as a custom body.
    /// </summary>
    public static BodyColorScheme Derive(Rgba32 torso) => From(string.Empty, "Custom Body", torso);

    /// <summary>Torso is the base; the built-in's own head/hand/foot steps are applied to it.</summary>
    private static BodyColorScheme From(string id, string name, Rgba32 torso) =>
        new(id, name, Lighten(torso, 0.22f), torso, Lighten(torso, 0.36f), Lighten(torso, 0.11f));

    private static Rgba32 Lighten(Rgba32 color, float amount) => new(
        Channel(color.R, amount),
        Channel(color.G, amount),
        Channel(color.B, amount));

    private static byte Channel(byte value, float amount) =>
        (byte)Math.Clamp((int)Math.Round(value + ((255 - value) * amount)), 0, 255);
}
