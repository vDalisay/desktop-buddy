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
/// <para>Runtime gates remain defense-in-depth. Shipping Steam Demo also has a compile-time scope
/// that physically removes held-back implementation/resources, so editing feature tags cannot
/// recreate withheld content.</para>
/// </summary>
public static class DemoScope
{
    /// <summary>Cosmetics held back from the Demo. Invisible entries cannot be bought.</summary>
    private static readonly string[] FullReleaseOnlyContent =
    [
        "cosmetic.tops.utility_bib",
        "cosmetic.shoes.soft_steps",
    ];

#if !DESKTOP_BUDDY_NO_DEV_TOOLS
    /// <summary>
    /// Scenario-only seam that exercises full-release content in a Demo-scoped developer build.
    /// It is not compiled into anything shipped to players.
    /// </summary>
    internal static bool? FullReleaseOverride { get; set; }

    /// <summary>Scenario-only seam for the itch.io distribution scope.</summary>
    internal static bool? ItchIoOverride { get; set; }
#endif

    /// <summary>
    /// Local preview seam: a desktop run started with <c>--itch</c> adopts the reduced scope, so
    /// the itch surface can be inspected without a Web export and the pinned fork it needs.
    ///
    /// <para>Deliberately one-way. This can only turn the reduction <em>on</em>; there is no flag
    /// that turns it off, so no command line can widen what a shipped build offers.</para>
    /// </summary>
    private static readonly bool ItchIoRequestedLocally =
        Array.IndexOf(OS.GetCmdlineUserArgs(), "--itch") >= 0;

#if DESKTOP_BUDDY_NO_DEV_TOOLS
    public static bool IsItchIo => ItchIoRequestedLocally || OS.HasFeature("itch_io");
#else
    public static bool IsItchIo =>
        ItchIoOverride ?? (ItchIoRequestedLocally || OS.HasFeature("itch_io"));
#endif

#if DESKTOP_BUDDY_STEAM_DEMO
    // Fail closed in the physically reduced Demo. A modified .pck/custom feature list cannot turn
    // this binary into a Full Release because the decision is baked into managed code.
    public static bool IsFullRelease => false;
#elif DESKTOP_BUDDY_NO_DEV_TOOLS
    public static bool IsFullRelease => !IsItchIo && OS.HasFeature("full_release");
#else
    public static bool IsFullRelease =>
        !IsItchIo && (FullReleaseOverride ?? OS.HasFeature("full_release"));
#endif

    /// <summary>
    /// Workshop ships only in Steam exports; editor runs keep it for development and verification.
    /// The Initial Steam Demo intentionally keeps both supported Workshop package types.
    /// </summary>
    public static bool IncludesWorkshop =>
        !IsItchIo && (OS.HasFeature("editor") || OS.HasFeature("steam"));

    /// <summary>False for catalogue entries this build holds back.</summary>
    public static bool Includes(string? contentId) =>
        IsFullRelease || contentId is null ||
        Array.IndexOf(FullReleaseOnlyContent, contentId) < 0;

    /// <summary>
    /// False for Buddy Studio categories this build holds back. Accessories is on the list
    /// alongside Tops and Shoes: shared character schema/rendering stays, but Initial Demo does
    /// not expose or ship its held-back catalogue entries.
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
    /// point at features that distribution does not ship.
    /// </summary>
    public static IReadOnlyList<string> TutorialSteps =>
        IsItchIo ? TutorialStepIds.ItchIo : TutorialStepIds.Ordered;

    /// <summary>Whether the Room Decorator command is offered at all.</summary>
    public static bool IncludesRoomDecorator => IsFullRelease && !IsItchIo;

    /// <summary>
    /// Whether this build ships Gore Mode at all. Steam builds ship it and itch.io does not.
    /// This is asked in addition to the player's setting and again at composition roots.
    /// </summary>
    public static bool IncludesGore => !IsItchIo;
}
