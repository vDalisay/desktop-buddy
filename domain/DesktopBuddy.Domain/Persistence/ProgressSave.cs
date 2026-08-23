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
    public long ScoredImpacts { get; init; }
    public long Knockouts { get; init; }
    public long CareAwards { get; init; }
    public long TrustResets { get; init; }
    public long EarnedMilliCredits { get; init; }
    public long SuccessfulCatches { get; init; }
    public long TotalPainMilli { get; init; }
    public long BestOneSecondMilliCredits { get; init; }
    public long BestThreeSecondMilliCredits { get; init; }
    public long BestTenSecondMilliCredits { get; init; }
    public float HighestMood { get; init; }
    public float LowestMood { get; init; }
    public Dictionary<string, long> ToolUses { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> ToolPainMilli { get; init; } = new(StringComparer.Ordinal);
}

public sealed record CumulativeTimesSave
{
    public double RunSeconds { get; init; }
    public double ActiveSeconds { get; init; }
    public double HiddenSeconds { get; init; }
}

public sealed record ProgressExtensionsSave
{
    public string? UnknownSelectedToolId { get; init; }
    public List<string> UnknownContentIds { get; init; } = [];
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.Ordinal);
}

public sealed record WorkProgressSave
{
    public long Revision { get; init; }
    public long KeyboardPresses { get; init; }
    public long MouseClicks { get; init; }
    public List<string> ClaimedLifetimeMilestoneIds { get; init; } = [];
    public bool FirstEntryGlassesGranted { get; init; }
    public WorkSessionSave? ActiveSession { get; init; }

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
    public Guid SessionId { get; init; }
    public long KeyboardPresses { get; init; }
    public long MouseClicks { get; init; }
    public List<string> EarnedRepeatPerSessionMilestoneIds { get; init; } = [];

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
    public Guid InstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public float CanonicalX { get; init; }
    public float CanonicalY { get; init; }
    public int RotationDegrees { get; init; }
    public DecorationRenderBand RenderBand { get; init; }
    public long PurchasePriceMilliCredits { get; init; }

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
    public long Revision { get; init; }
    public int LayoutSchemaVersion { get; init; } = EnvironmentLayout.CurrentSchemaVersion;
    public List<PlacedDecorationSave> PlacedDecorations { get; init; } = [];

    /// <summary>Definition IDs the player owns but has not placed; one entry per owned copy. Older
    /// saves have none, which is correct: nothing had been banked before storage existed.</summary>
    public List<string> OwnedUnplaced { get; init; } = [];

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
    public string ActivityId { get; init; } = ContentIds.FunCatch;
    public int Drain { get; init; } = FunPreferences.Default.CatchDrain;
    public float Interest { get; init; } = FunInterestModel.MaximumInterest;
    public bool Bored { get; init; }
}

/// <summary>Steam-Cloud-eligible semantic progress only (ARCHITECTURE §12).</summary>
public sealed record ProgressSave
{
    /// <summary>
    /// Schema 8 adds the atomic Environment progress state alongside the wallet.
    /// </summary>
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public long BalanceMilliCredits { get; init; }
    public List<string> UnlockedToolIds { get; init; } = [];
    public string SelectedToolId { get; init; } = ContentIds.ToolGrab;
    public Guid? ActiveCharacterId { get; init; }
    public float Mood { get; init; }
    public float Fullness { get; init; }
    public List<string> HarmfulContentIds { get; init; } = [];
    public int ObstacleHopPropensity { get; init; } = BuddyTraits.Default.ObstacleHopPropensity;
    public List<FunActivitySave> FunActivities { get; init; } = [];
    public ProgressStatisticsSave Statistics { get; init; } = new();
    public CumulativeTimesSave Times { get; init; } = new();
    public WorkProgressSave Work { get; init; } = new();
    public EnvironmentProgressSave Environment { get; init; } = new();
    public ProgressExtensionsSave Extensions { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }

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

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public int WindowX { get; init; }
    public int WindowY { get; init; }
    // A first run opens at a comfortable working size rather than the old 480x360 postage stamp:
    // the Win98 shell, its menus and the tutorial window all need room to be legible.
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 940;
    public int Monitor { get; init; }
    public int Dpi { get; init; } = 96;
    public int ZoomPercent { get; init; } = 100;

    /// <summary>Interface and font scale: 100, 125, 150, 175, or 200 percent.</summary>
    public int UiScalePercent { get; init; } = 100;
    public float MasterVolume { get; init; } = 1.0f;
    public float SfxVolume { get; init; } = 1.0f;
    public float UiVolume { get; init; } = 1.0f;

    /// <summary>Foreground frame cap; zero leaves the cap to V-sync.</summary>
    public int MaxFps { get; init; }

    /// <summary>Frame cap while hidden or throttled; zero uses the tuning profile's value.</summary>
    public int BackgroundMaxFps { get; init; }

    /// <summary>Hide the buddy while a full-screen application owns the foreground.</summary>
    public bool HideForFullscreenApps { get; init; } = true;

    /// <summary>"remember" (default), "work", or "play": which mode a launch starts in.</summary>
    public string StartupInputMode { get; init; } = "remember";

    /// <summary>Legacy broad Work-mode mute. Kept for existing users who explicitly chose it.</summary>
    public bool MuteInWorkMode { get; init; } = true;

    /// <summary>Mute only Work Mode's mechanical typing feedback; other SFX remain audible.</summary>
    public bool MuteWorkTyping { get; init; }

    /// <summary>
    /// Aesthetic UI preference: keep the Win98 visual language but allow short modern easing for
    /// preview/category transitions. ReducedMotion always overrides this and removes the motion.
    /// Missing values in older settings files default to true through the property initializer.
    /// </summary>
    public bool ModernUiMotion { get; init; } = true;
    public bool ReducedMotion { get; init; }
    public bool ScreenShake { get; init; } = true;
    public bool ReducedParticles { get; init; }
    public bool PhotosensitivitySafe { get; init; } = true;
    public int Msaa { get; init; } = 2;
    public bool VSync { get; init; } = true;
    public bool AlwaysOnTop { get; init; } = true;
    public string GlobalHotkey { get; init; } = "Ctrl+Shift+B";
    public bool LaunchWithWindows { get; init; }
    public string LastInputMode { get; init; } = "work";

    // Work Mode is a machine-local presentation. Zero width/height means use the default size.
    public int WorkWindowX { get; init; }
    public int WorkWindowY { get; init; }
    public int WorkWindowWidth { get; init; }
    public int WorkWindowHeight { get; init; }
    public bool WorkPositionSet { get; init; }
    public bool WorkAnimationsEnabled { get; init; } = true;
    public bool WorkShowLifetimeCounter { get; init; }

    /// <summary>The CRT pass over Work Mode's buddy and PC. On by default: it is the look.</summary>
    public bool WorkRetroFilter { get; init; } = true;

    // The player's interface palette, as "rrggbb" hex. Empty or unparseable values fall back to
    // the shipped grey/navy/black, so a hand-edited settings file cannot leave the UI unreadable.
    public string UiFaceColor { get; init; } = "c0c0c0";
    public string UiBarColor { get; init; } = "000080";
    public string UiTextColor { get; init; } = "000000";

    // Environment editor preferences are local UX state, not room progression. Reset Progress must
    // preserve them exactly like window placement and Work presentation preferences.
    public bool EnvironmentSnapToGrid { get; init; }
    public EnvironmentGridSize EnvironmentGridSize { get; init; } = EnvironmentGridSize.Medium;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}
