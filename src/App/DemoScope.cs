using System;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// What a given build ships. The public Demo is the default scope and the full release opts
/// in through the export preset's <c>full_release</c> custom feature: a build that forgets its
/// feature tag then ships too little rather than shipping something unfinished.
///
/// <para>Deliberately not authored data. Hiding these entries in the <c>.tres</c> files would
/// hide them from the full release too, and the point is one codebase that produces both
/// builds (owner decision 2026-08-20).</para>
/// </summary>
public static class DemoScope
{
    /// <summary>Cosmetics held back from the Demo. Invisible entries cannot be bought.</summary>
    private static readonly string[] FullReleaseOnlyContent =
    [
        "cosmetic.tops.utility_bib",
        "cosmetic.shoes.soft_steps",
    ];

    /// <summary>
    /// Set by scenarios that must exercise full-release content in a Demo-scoped build; hiding
    /// a feature must never quietly stop testing it. Null means "ask the build".
    /// </summary>
    internal static bool? FullReleaseOverride { get; set; }

    public static bool IsFullRelease => FullReleaseOverride ?? OS.HasFeature("full_release");

    /// <summary>False for catalogue entries this build holds back.</summary>
    public static bool Includes(string? contentId) =>
        IsFullRelease || contentId is null ||
        Array.IndexOf(FullReleaseOnlyContent, contentId) < 0;

    /// <summary>
    /// False for Buddy Studio categories this build holds back. Accessories is on the list
    /// alongside Tops and Shoes (owner instruction 2026-08-21): the torso accents exist in the
    /// catalogue and render, but the Demo's Studio never offers the category, so nothing in it
    /// can be bought, equipped or randomised into.
    /// </summary>
    public static bool Includes(CharacterFeatureSlot slot) =>
        IsFullRelease ||
        slot is not (CharacterFeatureSlot.Tops or CharacterFeatureSlot.Shoes or
            CharacterFeatureSlot.Accessories);

    /// <summary>Whether the Room Decorator command is offered at all.</summary>
    public static bool IncludesRoomDecorator => IsFullRelease;

    /// <summary>
    /// Set by scenarios that must exercise Gore Mode in a build without the feature tag.
    /// Null means "ask the build".
    /// </summary>
    internal static bool? GoreOverride { get; set; }

    /// <summary>
    /// Whether this build ships Gore Mode at all — the Settings toggle, the bleeding, the
    /// stains, and the Sword's impalement.
    ///
    /// <para>Gated on a <b>positive</b> <c>gore</c> custom feature rather than on the
    /// absence of an itch.io tag, for the same reason <see cref="IsFullRelease"/> is
    /// positive: a preset that forgets its tag must ship <i>less</i>, never more. The
    /// storefront builds that carry no gore tag get a game with no blood in it, which is
    /// the safe way round for a store listing to be wrong (owner instruction 2026-08-24:
    /// Steam ships it, itch.io does not).</para>
    ///
    /// <para>This is asked in addition to the player's setting, never instead of it. A
    /// hand-edited <c>settings.json</c> carrying <c>GoreEnabled</c> into a build without
    /// the feature must stay inert.</para>
    /// </summary>
    /// <para>Editor and development runs ship it too. Neither carries an export preset's
    /// custom features, and a feature the owner cannot reach by running the project is a
    /// feature that never gets tuned — the same reason
    /// <see cref="Platform.WindowsAutostart"/> special-cases the editor.</para>
    public static bool IncludesGore =>
        GoreOverride ?? (OS.HasFeature("gore") || OS.HasFeature("editor"));
}
