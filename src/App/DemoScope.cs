using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Persistence;
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
    /// Local preview seam: a desktop run started with <c>--itch</c> adopts the reduced scope, so
    /// the itch surface can be inspected without a Web export and the pinned fork it needs.
    ///
    /// <para>Deliberately one-way. This can only turn the reduction <em>on</em>; there is no flag
    /// that turns it off, so no command line can widen what a shipped build offers. Read once —
    /// autoloads consult <see cref="IsItchIo"/> during their own <c>_Ready</c>, which runs before
    /// the main scene, so this cannot depend on anything that boots.</para>
    /// </summary>
    private static readonly bool ItchIoRequestedLocally =
        Array.IndexOf(OS.GetCmdlineUserArgs(), "--itch") >= 0;

    /// <summary>
    /// The itch build is intentionally the strictest public scope. If an export is accidentally
    /// tagged with both <c>itch_io</c> and <c>full_release</c>, itch wins so held-back features
    /// cannot leak into that distribution.
    /// </summary>
    public static bool IsItchIo =>
        ItchIoOverride ?? (ItchIoRequestedLocally || OS.HasFeature("itch_io"));

    public static bool IsFullRelease =>
        !IsItchIo && (FullReleaseOverride ?? OS.HasFeature("full_release"));

    /// <summary>
    /// Workshop ships only in Steam exports; editor runs keep it for development and verification.
    ///
    /// <para>itch.io is excluded here rather than only by the <c>DESKTOP_BUDDY_PUBLIC_WEB</c>
    /// compile guard around the composition. That guard covers the Web export alone, so an editor
    /// run — and any future native itch build — still composed Workshop and showed its command
    /// (owner report 2026-09-07). The scope answers it now, and the compile guard stays as the
    /// second layer that keeps the code out of the public browser assembly entirely.</para>
    /// </summary>
    public static bool IncludesWorkshop =>
        !IsItchIo && (OS.HasFeature("editor") || OS.HasFeature("steam"));

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
    /// The first-session walkthrough this build teaches. Every distribution gets one; itch.io gets
    /// the shorter sequence, because its chapters on Work Mode, Paint Room and Buddy Studio would
    /// point at features that distribution does not ship (owner request 2026-09-07).
    /// </summary>
    public static IReadOnlyList<string> TutorialSteps =>
        IsItchIo ? TutorialStepIds.ItchIo : TutorialStepIds.Ordered;

    /// <summary>Whether the Room Decorator command is offered at all.</summary>
    public static bool IncludesRoomDecorator => IsFullRelease && !IsItchIo;

    /// <summary>
    /// Whether this build ships Gore Mode at all — the Settings toggle, the bleeding, and the
    /// blood the Sword and the guns draw. The Steam builds ship it and the itch.io build does
    /// not (owner instruction 2026-08-24), which is the same shape as Work Mode, Paint Room
    /// and Buddy Studio above rather than a mechanism of its own.
    ///
    /// <para>This is asked in addition to the player's setting, never instead of it, and it is
    /// asked again at the composition root rather than trusted from the Settings row — so a
    /// hand-edited <c>settings.json</c> carried onto the itch build stays inert.</para>
    /// </summary>
    public static bool IncludesGore => !IsItchIo;
}
