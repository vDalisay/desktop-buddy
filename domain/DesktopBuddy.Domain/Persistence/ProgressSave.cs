using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;

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

/// <summary>
/// One buddy's taste for a fun activity and how much novelty it currently has left. Both
/// halves persist: taste is identity sampled once at creation, and interest is live state
/// that fades with repetition and recharges with time (owner instruction 2026-07-27).
/// </summary>
public sealed record FunActivitySave
{
    /// <summary>Stable fun-activity content ID.</summary>
    public string ActivityId { get; init; } = ContentIds.FunCatch;

    /// <summary>Interest cost of one engagement — this buddy's taste for the activity.</summary>
    public int Drain { get; init; } = FunPreferences.Default.CatchDrain;

    /// <summary>Remaining novelty, <c>0–100</c>.</summary>
    public float Interest { get; init; } = FunInterestModel.MaximumInterest;

    /// <summary>
    /// The hysteresis latch. It must persist separately because an interest below the
    /// comeback threshold can still be fun when it has not yet reached zero.
    /// </summary>
    public bool Bored { get; init; }
}

/// <summary>Steam-Cloud-eligible semantic progress only (ARCHITECTURE §12).</summary>
public sealed record ProgressSave
{
    /// <summary>
    /// <c>5</c> adds <see cref="Fullness"/>. A schema-4 save loads with an empty stomach,
    /// which is the same state a new save starts in — the buddy is hungry after an upgrade,
    /// never mysteriously full.
    /// </summary>
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public long BalanceMilliCredits { get; init; }
    public List<string> UnlockedToolIds { get; init; } = [];
    public string SelectedToolId { get; init; } = ContentIds.ToolGrab;
    public float Mood { get; init; }

    /// <summary>
    /// Hidden appetite, in points of the <c>200</c>-point bar (owner decision 2026-07-29).
    /// Persisted like mood: a relaunch must not reset the buddy's stomach.
    /// </summary>
    public float Fullness { get; init; }
    public List<string> HarmfulContentIds { get; init; } = [];
    public int ObstacleHopPropensity { get; init; } = BuddyTraits.Default.ObstacleHopPropensity;

    /// <summary>
    /// Per-activity taste and remaining novelty. Written for every activity this build
    /// knows; an entry naming an activity a later build added is retained but inert.
    /// </summary>
    public List<FunActivitySave> FunActivities { get; init; } = [];

    public ProgressStatisticsSave Statistics { get; init; } = new();
    public CumulativeTimesSave Times { get; init; } = new();
    public ProgressExtensionsSave Extensions { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }

    public static ProgressSave FromSnapshot(in ProgressSnapshot snapshot)
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
            Extensions = extensions,
        };
    }

    /// <summary>
    /// Pairs each activity's taste (from the personality) with its live novelty. Interest
    /// defaults to full for any activity the snapshot did not report, so a state composed
    /// without fun data still writes a coherent payload.
    /// </summary>
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
    public int WindowWidth { get; init; } = 480;
    public int WindowHeight { get; init; } = 360;
    public int Monitor { get; init; }
    public int Dpi { get; init; } = 96;
    public int ZoomPercent { get; init; } = 100;
    public float MasterVolume { get; init; } = 0.5f;
    public float SfxVolume { get; init; } = 0.5f;
    public bool MuteInWorkMode { get; init; } = true;
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

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}
