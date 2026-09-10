using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Domain.Persistence;

public enum SaveLoadStatus
{
    Loaded,
    NewSave,
    BackupRecovered,
    DefaultsRecovered,
    UnsupportedFutureVersion,
    Failed,
}

public readonly record struct LoadResult<T>(
    SaveLoadStatus Status,
    T? Value,
    string? Detail = null,
    string? QuarantinedPath = null)
    where T : class
{
    public bool HasValue => Value is not null;
}

public interface IProgressStore
{
    System.Threading.Tasks.Task<LoadResult<ProgressSave>> LoadProgressAsync(
        System.Threading.CancellationToken token);
    System.Threading.Tasks.Task<LoadResult<LocalSettingsSave>> LoadSettingsAsync(
        System.Threading.CancellationToken token);
    System.Threading.Tasks.Task SaveProgressAsync(
        ProgressSave data,
        System.Threading.CancellationToken token);
    System.Threading.Tasks.Task SaveSettingsAsync(
        LocalSettingsSave data,
        System.Threading.CancellationToken token);
}

public sealed record ProgressStatisticsSave
{
    public long ScoredImpacts { get; set; }
    public long Knockouts { get; set; }
    public long CareAwards { get; set; }
    public long TrustResets { get; set; }
    public long EarnedMilliCredits { get; set; }
    public long SuccessfulCatches { get; set; }
    public long TotalPainMilli { get; set; }
    public long BestOneSecondMilliCredits { get; set; }
    public long BestThreeSecondMilliCredits { get; set; }
    public long BestTenSecondMilliCredits { get; set; }
    public float HighestMood { get; set; }
    public float LowestMood { get; set; }
    public Dictionary<string, long> ToolUses { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> ToolPainMilli { get; set; } = new(StringComparer.Ordinal);
}

public sealed record CumulativeTimesSave
{
    public double RunSeconds { get; set; }
    public double ActiveSeconds { get; set; }
    public double HiddenSeconds { get; set; }
}

public sealed record ProgressExtensionsSave
{
    public string? UnknownSelectedToolId { get; set; }
    public List<string> UnknownContentIds { get; set; } = [];
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
}

public sealed record WorkProgressSave
{
    public long Revision { get; set; }
    public long KeyboardPresses { get; set; }
    public long MouseClicks { get; set; }
    public List<string> ClaimedLifetimeMilestoneIds { get; set; } = [];
    public bool FirstEntryGlassesGranted { get; set; }
    public WorkSessionSave? ActiveSession { get; set; }

    public static WorkProgressSave FromSnapshot(in WorkProgressSnapshot snapshot) => new()
    {
        Revision = snapshot.Revision,
        KeyboardPresses = snapshot.Lifetime.KeyboardPresses,
        MouseClicks = snapshot.Lifetime.MouseClicks,
        ClaimedLifetimeMilestoneIds = [.. snapshot.ClaimedLifetimeMilestoneIds],
        FirstEntryGlassesGranted = snapshot.FirstEntryGlassesGranted,
        ActiveSession = snapshot.ActiveSession.HasValue
            ? WorkSessionSave.FromSnapshot(snapshot.ActiveSession.Value)
            : null,
    };

    public WorkProgressState CreateState() => new(
        new WorkCounterSnapshot(KeyboardPresses, MouseClicks),
        ClaimedLifetimeMilestoneIds,
        FirstEntryGlassesGranted,
        Revision,
        ActiveSession?.CreateSnapshot());
}

public sealed record WorkSessionSave
{
    public Guid SessionId { get; set; }
    public long KeyboardPresses { get; set; }
    public long MouseClicks { get; set; }
    public List<string> EarnedRepeatPerSessionMilestoneIds { get; set; } = [];

    public static WorkSessionSave FromSnapshot(in WorkSessionSnapshot snapshot) => new()
    {
        SessionId = snapshot.SessionId,
        KeyboardPresses = snapshot.Counters.KeyboardPresses,
        MouseClicks = snapshot.Counters.MouseClicks,
        EarnedRepeatPerSessionMilestoneIds = [.. snapshot.EarnedRepeatPerSessionMilestoneIds],
    };

    public WorkSessionSnapshot CreateSnapshot() => new(
        SessionId,
        new WorkCounterSnapshot(KeyboardPresses, MouseClicks),
        EarnedRepeatPerSessionMilestoneIds);
}

public sealed record PlacedDecorationSave
{
    public Guid InstanceId { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public float CanonicalX { get; set; }
    public float CanonicalY { get; set; }
    public int RotationDegrees { get; set; }
    public DecorationRenderBand RenderBand { get; set; }
    public long PurchasePriceMilliCredits { get; set; }

    public static PlacedDecorationSave FromPlaced(in PlacedDecoration placed) => new()
    {
        InstanceId = placed.InstanceId.Value,
        DefinitionId = placed.DefinitionId.Value,
        CanonicalX = placed.Position.X,
        CanonicalY = placed.Position.Y,
        RotationDegrees = placed.RotationDegrees,
        RenderBand = placed.RenderBand,
        PurchasePriceMilliCredits = placed.PurchasePriceMilliCredits,
    };

    public PlacedDecoration CreatePlaced() => new(
        new PlacedDecorationId(InstanceId),
        new DecorationDefinitionId(DefinitionId),
        new CanonicalRoomPosition(CanonicalX, CanonicalY),
        RotationDegrees,
        RenderBand,
        PurchasePriceMilliCredits);
}

public sealed record EnvironmentProgressSave
{
    public long Revision { get; set; }
    public int LayoutSchemaVersion { get; set; } = EnvironmentLayout.CurrentSchemaVersion;
    public List<PlacedDecorationSave> PlacedDecorations { get; set; } = [];

    /// <summary>Definition IDs the player owns but has not placed; one entry per owned copy. Older
    /// saves have none, which is correct: nothing had been banked before storage existed.</summary>
    public List<string> OwnedUnplaced { get; set; } = [];

    public static EnvironmentProgressSave FromSnapshot(in EnvironmentProgressSnapshot snapshot) => new()
    {
        Revision = snapshot.Revision,
        LayoutSchemaVersion = snapshot.Layout.SchemaVersion,
        PlacedDecorations = snapshot.Layout.Decorations.Select(item => PlacedDecorationSave.FromPlaced(item)).ToList(),
        OwnedUnplaced = (snapshot.OwnedUnplaced ?? []).Select(id => id.Value).ToList(),
    };

    public EnvironmentProgressState CreateState() => new(
        LayoutSchemaVersion switch
        {
            // Schemas 1 and 2 carried flat wall/floor colours. Those are gone: the room background
            // is a painted image now, so older saves migrate by keeping only their decorations.
            1 or 2 or EnvironmentLayout.CurrentSchemaVersion =>
                new EnvironmentLayout(PlacedDecorations.Select(item => item.CreatePlaced())),
            _ => throw new ArgumentOutOfRangeException(nameof(LayoutSchemaVersion), "Unsupported Environment layout schema."),
        },
        Revision,
        OwnedUnplaced
            .Select(value => DecorationDefinitionId.TryCreate(value, out DecorationDefinitionId id) ? id : default)
            .Where(id => id != default));
}

public sealed record FunActivitySave
{
    public string ActivityId { get; set; } = ContentIds.FunCatch;
    public int Drain { get; set; } = FunPreferences.Default.CatchDrain;
    public float Interest { get; set; } = FunInterestModel.MaximumInterest;
    public bool Bored { get; set; }
}

/// <summary>Steam-Cloud-eligible semantic progress only (ARCHITECTURE §12).</summary>
public sealed record ProgressSave
{
    /// <summary>
    /// Schema 8 adds the atomic Environment progress state alongside the wallet.
    /// </summary>
    public const int CurrentSchemaVersion = 8;

    /// <summary>
    /// Explicit and attributed so the source generator deserializes through property setters
    /// rather than binding init accessors as constructor parameters, which would discard every
    /// initializer below for any field the JSON omits.
    /// </summary>
    [JsonConstructor]
    public ProgressSave() { }

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long Revision { get; set; }
    public long BalanceMilliCredits { get; set; }
    public List<string> UnlockedToolIds { get; set; } = [];
    public string SelectedToolId { get; set; } = ContentIds.ToolGrab;
    public Guid? ActiveCharacterId { get; set; }
    public float Mood { get; set; }
    public float Fullness { get; set; }
    public List<string> HarmfulContentIds { get; set; } = [];
    public int ObstacleHopPropensity { get; set; } = BuddyTraits.Default.ObstacleHopPropensity;
    public List<FunActivitySave> FunActivities { get; set; } = [];
    public ProgressStatisticsSave Statistics { get; set; } = new();
    public CumulativeTimesSave Times { get; set; } = new();
    public WorkProgressSave Work { get; set; } = new();
    public EnvironmentProgressSave Environment { get; set; } = new();
    public ProgressExtensionsSave Extensions { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }

    public static ProgressSave FromSnapshot(
        in ProgressSnapshot snapshot,
        Guid? activeCharacterId = null,
        WorkProgressSnapshot? work = null,
        EnvironmentProgressSnapshot? environment = null)
    {
        var extensions = new ProgressExtensionsSave
        {
            UnknownSelectedToolId = snapshot.Extensions?.UnknownSelectedToolId,
            UnknownContentIds = snapshot.Extensions?.UnknownContentIds is null
                ? []
                : [.. snapshot.Extensions.UnknownContentIds],
            Values = snapshot.Extensions?.Values is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(snapshot.Extensions.Values, StringComparer.Ordinal),
        };
        return new ProgressSave
        {
            Revision = snapshot.Revision,
            BalanceMilliCredits = snapshot.BalanceMilliCredits,
            UnlockedToolIds = [.. snapshot.UnlockedToolIds],
            SelectedToolId = snapshot.SelectedToolId,
            ActiveCharacterId = activeCharacterId,
            Mood = snapshot.Mood,
            Fullness = snapshot.Fullness,
            HarmfulContentIds = [.. snapshot.HarmfulContentIds],
            ObstacleHopPropensity = snapshot.Traits.ObstacleHopPropensity,
            FunActivities = BuildFunActivities(snapshot),
            Statistics = new ProgressStatisticsSave
            {
                ScoredImpacts = snapshot.Statistics.ScoredImpacts,
                Knockouts = snapshot.Statistics.Knockouts,
                CareAwards = snapshot.Statistics.CareAwards,
                TrustResets = snapshot.Statistics.TrustResets,
                EarnedMilliCredits = snapshot.Statistics.EarnedMilliCredits,
                SuccessfulCatches = snapshot.Statistics.SuccessfulCatches,
                TotalPainMilli = snapshot.Statistics.TotalPainMilli,
                BestOneSecondMilliCredits = snapshot.Statistics.BestOneSecondMilliCredits,
                BestThreeSecondMilliCredits = snapshot.Statistics.BestThreeSecondMilliCredits,
                BestTenSecondMilliCredits = snapshot.Statistics.BestTenSecondMilliCredits,
                HighestMood = snapshot.Statistics.HighestMood,
                LowestMood = snapshot.Statistics.LowestMood,
                ToolUses = snapshot.Statistics.ToolUses is null
                    ? new Dictionary<string, long>(StringComparer.Ordinal)
                    : new Dictionary<string, long>(
                        snapshot.Statistics.ToolUses,
                        StringComparer.Ordinal),
                ToolPainMilli = snapshot.Statistics.ToolPainMilli is null
                    ? new Dictionary<string, long>(StringComparer.Ordinal)
                    : new Dictionary<string, long>(
                        snapshot.Statistics.ToolPainMilli,
                        StringComparer.Ordinal),
            },
            Times = new CumulativeTimesSave
            {
                RunSeconds = snapshot.Times.RunSeconds,
                ActiveSeconds = snapshot.Times.ActiveSeconds,
                HiddenSeconds = snapshot.Times.HiddenSeconds,
            },
            Work = work.HasValue ? WorkProgressSave.FromSnapshot(work.Value) : new WorkProgressSave(),
            Environment = environment.HasValue
                ? EnvironmentProgressSave.FromSnapshot(environment.Value)
                : new EnvironmentProgressSave(),
            Extensions = extensions,
        };
    }

    private static List<FunActivitySave> BuildFunActivities(in ProgressSnapshot snapshot)
    {
        var activities = new List<FunActivitySave>(FunInterestModel.ActivityCount);
        foreach (FunActivityId activity in Enum.GetValues<FunActivityId>())
        {
            float interest = FunInterestModel.MaximumInterest;
            bool bored = false;
            if (snapshot.FunInterest is not null)
            {
                foreach (FunActivityInterest entry in snapshot.FunInterest)
                {
                    if (entry.Activity == activity)
                    {
                        interest = entry.Interest;
                        bored = entry.Bored;
                        break;
                    }
                }
            }

            activities.Add(new FunActivitySave
            {
                ActivityId = ContentIds.ForFun(activity),
                Drain = snapshot.Traits.Preferences.DrainFor(activity),
                Interest = interest,
                Bored = bored,
            });
        }

        return activities;
    }
}

/// <summary>Machine-local settings; never Steam Cloud progress.</summary>
public sealed record LocalSettingsSave
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long Revision { get; set; }
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    // A first run opens at a comfortable working size rather than the old 480x360 postage stamp:
    // the Win98 shell, its menus and the tutorial window all need room to be legible.
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 940;
    public int Monitor { get; set; }
    public int Dpi { get; set; } = 96;
    public int ZoomPercent { get; set; } = 100;

    /// <summary>Interface and font scale: 100, 125, 150, 175, or 200 percent.</summary>
    public int UiScalePercent { get; set; } = 100;
    /// <summary>
    /// A first launch starts at 80%, not full, so the sliders have somewhere to go up as well as
    /// down and an unattended desktop pet never opens at maximum volume.
    /// </summary>
    public float MasterVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 0.8f;
    public float UiVolume { get; set; } = 0.8f;

    /// <summary>Foreground frame cap; zero leaves the cap to V-sync.</summary>
    public int MaxFps { get; set; }

    /// <summary>Frame cap while hidden or throttled; zero uses the tuning profile's value.</summary>
    public int BackgroundMaxFps { get; set; }

    /// <summary>Hide the buddy while a full-screen application owns the foreground.</summary>
    public bool HideForFullscreenApps { get; set; } = true;

    /// <summary>"remember" (default), "work", or "play": which mode a launch starts in.</summary>
    public string StartupInputMode { get; set; } = "remember";

    /// <summary>Legacy broad Work-mode mute. Kept for existing users who explicitly chose it.</summary>
    public bool MuteInWorkMode { get; set; } = true;

    /// <summary>Mute only Work Mode's mechanical typing feedback; other SFX remain audible.</summary>
    public bool MuteWorkTyping { get; set; }

    /// <summary>
    /// Aesthetic UI preference: keep the Win98 visual language but allow short modern easing for
    /// preview/category transitions. ReducedMotion always overrides this and removes the motion.
    /// Missing values in older settings files default to true through the property initializer.
    /// </summary>
    public bool ModernUiMotion { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool ScreenShake { get; set; } = true;
    public bool ReducedParticles { get; set; }
    public bool PhotosensitivitySafe { get; set; } = true;

    /// <summary>
    /// Gore Mode: piercing hits open bleeding wounds that stain the room. Off by default,
    /// like every other content-sensitivity default here — the player opts in. Builds that
    /// do not ship the feature ignore this value entirely rather than trusting the file.
    /// </summary>
    public bool GoreEnabled { get; set; }
    public int Msaa { get; set; } = 2;
    public bool VSync { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public string GlobalHotkey { get; set; } = "Ctrl+Shift+B";
    public bool LaunchWithWindows { get; set; }
    public string LastInputMode { get; set; } = "work";

    // Work Mode is a machine-local presentation. Zero width/height means use the default size.
    public int WorkWindowX { get; set; }
    public int WorkWindowY { get; set; }
    public int WorkWindowWidth { get; set; }
    public int WorkWindowHeight { get; set; }
    public bool WorkPositionSet { get; set; }
    public bool WorkAnimationsEnabled { get; set; } = true;
    public bool WorkShowLifetimeCounter { get; set; }

    /// <summary>The CRT pass over Work Mode's buddy and PC. On by default: it is the look.</summary>
    public bool WorkRetroFilter { get; set; } = true;

    // The player's interface palette, as "rrggbb" hex. Empty or unparseable values fall back to
    // the shipped grey/navy/black, so a hand-edited settings file cannot leave the UI unreadable.
    public string UiFaceColor { get; set; } = "c0c0c0";
    public string UiBarColor { get; set; } = "000080";
    public string UiTextColor { get; set; } = "000000";

    // Environment editor preferences are local UX state, not room progression. Reset Progress must
    // preserve them exactly like window placement and Work presentation preferences.
    public bool EnvironmentSnapToGrid { get; set; }
    public EnvironmentGridSize EnvironmentGridSize { get; set; } = EnvironmentGridSize.Medium;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}
