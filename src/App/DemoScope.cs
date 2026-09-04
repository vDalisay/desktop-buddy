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

    /// <summary>Workshop ships only in Steam exports; editor runs keep it for development and verification.</summary>
    public static bool IncludesWorkshop => OS.HasFeature("editor") || OS.HasFeature("steam");

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
}
