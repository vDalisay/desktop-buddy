using System;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// What a given build ships. The public Demo is the default scope, the full release opts
/// in through the export preset's <c>full_release</c> custom feature, and the itch.io build
/// opts into a smaller <c>itch_io</c> surface. A build that forgets its feature tag therefore
/// ships too little rather than shipping something unfinished.
///
/// <para>Deliberately not authored data. Hiding these entries in the <c>.tres</c> files would
/// hide them from the full release too, and the point is one codebase that produces every
/// distribution build (owner decision 2026-08-20).</para>
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

    /// <summary>Scenario seam for the itch.io distribution scope. Null means "ask the build".</summary>
    internal static bool? ItchIoOverride { get; set; }

    /// <summary>
    /// The itch build is intentionally the strictest public scope. If an export is accidentally
    /// tagged with both <c>itch_io</c> and <c>full_release</c>, itch wins so held-back features
    /// cannot leak into that distribution.
    /// </summary>
    public static bool IsItchIo => ItchIoOverride ?? OS.HasFeature("itch_io");

    public static bool IsFullRelease =>
        !IsItchIo && (FullReleaseOverride ?? OS.HasFeature("full_release"));

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

    /// <summary>The reduced itch.io build omits Work Mode entirely.</summary>
    public static bool IncludesWorkMode => !IsItchIo;

    /// <summary>The itch.io build omits the Paint Background / Paint Room workspace entirely.</summary>
    public static bool IncludesPaintRoom => !IsItchIo;

    /// <summary>The itch.io build omits Buddy Studio entirely.</summary>
    public static bool IncludesBuddyStudio => !IsItchIo;

    /// <summary>
    /// The itch.io build omits the first-session tutorial because its authored walkthrough covers
    /// Work Mode, Paint Room and Buddy Studio, all of which are intentionally absent there.
    /// </summary>
    public static bool IncludesTutorial => !IsItchIo;

    /// <summary>Whether the Room Decorator command is offered at all.</summary>
    public static bool IncludesRoomDecorator => IsFullRelease && !IsItchIo;
}
