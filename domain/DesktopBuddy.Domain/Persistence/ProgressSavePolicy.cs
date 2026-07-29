using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Persistence;

public enum SaveDecodeStatus
{
    Valid,
    Malformed,
    UnsupportedFutureVersion,
    Invalid,
}

public readonly record struct SaveDecodeResult(
    SaveDecodeStatus Status,
    ProgressSave? Save,
    string? Detail = null);

public static class ProgressSavePolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Serialize(ProgressSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        Validate(save);
        return JsonSerializer.Serialize(save, Options);
    }

    public static SaveDecodeResult Decode(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                !schemaElement.TryGetInt32(out int schema))
            {
                return new SaveDecodeResult(SaveDecodeStatus.Malformed, null, "Missing schemaVersion.");
            }
            if (schema > ProgressSave.CurrentSchemaVersion)
            {
                return new SaveDecodeResult(
                    SaveDecodeStatus.UnsupportedFutureVersion,
                    null,
                    $"Schema {schema} is newer than {ProgressSave.CurrentSchemaVersion}.");
            }

            ProgressSave save = schema switch
            {
                1 => MigrateV3(MigrateV2(MigrateV1(document.RootElement))),
                2 => MigrateV3(MigrateV2(
                    JsonSerializer.Deserialize<ProgressSave>(json, Options)
                    ?? throw new JsonException("Progress payload was null."))),
                3 => MigrateV3(
                    JsonSerializer.Deserialize<ProgressSave>(json, Options)
                    ?? throw new JsonException("Progress payload was null.")),
                ProgressSave.CurrentSchemaVersion =>
                    JsonSerializer.Deserialize<ProgressSave>(json, Options)
                    ?? throw new JsonException("Progress payload was null."),
                _ => throw new JsonException($"Unsupported legacy schema {schema}."),
            };
            Validate(save);
            return new SaveDecodeResult(SaveDecodeStatus.Valid, save);
        }
        catch (JsonException exception)
        {
            return new SaveDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return new SaveDecodeResult(SaveDecodeStatus.Invalid, null, exception.Message);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or FormatException or OverflowException)
        {
            // A wrong-typed field inside a legacy payload is corruption, not a crash:
            // it must quarantine and recover like any other malformed save, never
            // escape to the composition root and take the launch down with it.
            return new SaveDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }
    }

    public static void Validate(ProgressSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (save.SchemaVersion != ProgressSave.CurrentSchemaVersion)
            throw new ArgumentException("Progress schema is not current.", nameof(save));
        if (save.UnlockedToolIds is null ||
            save.HarmfulContentIds is null ||
            save.FunActivities is null ||
            save.Statistics is null ||
            save.Times is null ||
            save.Extensions is null ||
            save.Extensions.UnknownContentIds is null ||
            save.Extensions.Values is null ||
            save.Statistics.ToolUses is null ||
            save.Statistics.ToolPainMilli is null)
        {
            throw new ArgumentException(
                "Progress payload is missing a required semantic collection.",
                nameof(save));
        }
        if (save.Revision < 0 || save.BalanceMilliCredits < 0)
            throw new ArgumentException("Revision and balance must be non-negative.", nameof(save));
        if (!float.IsFinite(save.Mood) || save.Mood is < -100.0f or > 100.0f)
            throw new ArgumentException("Mood must be finite and within [-100, 100].", nameof(save));
        if (save.ObstacleHopPropensity is < 0 or > 100)
            throw new ArgumentException("Obstacle hop propensity must be within [0, 100].", nameof(save));
        ValidateFunActivities(save.FunActivities);
        ValidateSeconds(save.Times.RunSeconds, nameof(save.Times.RunSeconds));
        ValidateSeconds(save.Times.ActiveSeconds, nameof(save.Times.ActiveSeconds));
        ValidateSeconds(save.Times.HiddenSeconds, nameof(save.Times.HiddenSeconds));
        ValidateIds(save.UnlockedToolIds, nameof(save.UnlockedToolIds));
        ValidateIds(save.HarmfulContentIds, nameof(save.HarmfulContentIds));
        ValidateCounters(save.Statistics);
        if (string.IsNullOrWhiteSpace(save.SelectedToolId))
            throw new ArgumentException("Selected tool ID is required.", nameof(save));
    }

    public static BuddyProgressState CreateState(ProgressSave save, double cashPerPain)
    {
        Validate(save);
        string selected = save.SelectedToolId;
        string? unknownSelected = null;
        bool selectedKnown = ContentIds.TryParseTool(selected, out ToolId parsed);
        if (!selectedKnown ||
            !save.UnlockedToolIds.Contains(ContentIds.ForTool(parsed), StringComparer.Ordinal))
        {
            // Both cases are "this build cannot activate that selection". Retain the
            // original either way: a tool this build knows but has not unlocked is
            // still data a later build (or a repaired unlock list) must not lose.
            unknownSelected = selected;
            selected = ContentIds.ToolGrab;
        }

        var activeUnlocks = save.UnlockedToolIds
            .Where(ContentIds.IsTool)
            .ToArray();
        var activeHarmful = save.HarmfulContentIds
            .Where(ContentIds.IsKnown)
            .ToArray();
        var unknownIds = save.Extensions.UnknownContentIds
            .Concat(save.UnlockedToolIds.Where(id => !ContentIds.IsTool(id)))
            .Concat(save.HarmfulContentIds.Where(id => !ContentIds.IsKnown(id)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var extensions = new ProgressExtensionData(
            unknownSelected ?? save.Extensions.UnknownSelectedToolId,
            unknownIds,
            new Dictionary<string, string>(save.Extensions.Values, StringComparer.Ordinal));
        (BuddyTraits traits, List<FunActivityInterest> funInterest) = ReadFun(save);
        return new BuddyProgressState(
            cashPerPain,
            save.Mood,
            activeHarmful,
            activeUnlocks,
            traits,
            new ProgressStatistics(
                save.Statistics.ScoredImpacts,
                save.Statistics.Knockouts,
                save.Statistics.CareAwards,
                save.Statistics.TrustResets,
                save.Statistics.EarnedMilliCredits,
                save.Statistics.SuccessfulCatches,
                save.Statistics.TotalPainMilli,
                save.Statistics.BestOneSecondMilliCredits,
                save.Statistics.BestThreeSecondMilliCredits,
                save.Statistics.BestTenSecondMilliCredits,
                save.Statistics.HighestMood,
                save.Statistics.LowestMood,
                save.Statistics.ToolUses,
                save.Statistics.ToolPainMilli),
            new CumulativeTimes(
                save.Times.RunSeconds,
                save.Times.ActiveSeconds,
                save.Times.HiddenSeconds),
            save.Revision,
            save.BalanceMilliCredits,
            selected,
            extensions,
            funInterest);
    }

    /// <summary>
    /// Migrates a payload written before fun activities existed. Such a buddy never had its
    /// tastes rolled, so it is given the neutral default and full novelty rather than a fresh
    /// sample: a personality is drawn once at creation, and rolling one here would hand the
    /// same save a different character on every load.
    /// </summary>
    private static ProgressSave MigrateV2(ProgressSave save) => save with
    {
        SchemaVersion = 3,
        FunActivities = DefaultFunActivities(),
    };

    /// <summary>
    /// Schema 3 stored novelty but not the hysteresis latch. Preserve that version's
    /// conservative reload behavior for existing saves; schema 4 then records the exact
    /// latch so future reloads no longer change the live fun verdict.
    /// </summary>
    private static ProgressSave MigrateV3(ProgressSave save) => save with
    {
        SchemaVersion = ProgressSave.CurrentSchemaVersion,
        FunActivities = save.FunActivities
            .Select(activity => activity with
            {
                Bored = activity.Interest < FunInterestModel.ComebackInterest,
            })
            .ToList(),
    };

    private static List<FunActivitySave> DefaultFunActivities()
    {
        var activities = new List<FunActivitySave>(FunInterestModel.ActivityCount);
        foreach (FunActivityId activity in Enum.GetValues<FunActivityId>())
        {
            activities.Add(new FunActivitySave
            {
                ActivityId = ContentIds.ForFun(activity),
                Drain = FunPreferences.Default.DrainFor(activity),
                Interest = FunInterestModel.MaximumInterest,
                Bored = false,
            });
        }

        return activities;
    }

    /// <summary>
    /// Migrates the pre-Task-0 integer-ID payload. Every legacy field is read through a
    /// <c>Try*</c> accessor: a v1 save is by definition written by an older build, so a
    /// wrong-typed field is corruption to be quarantined, never an exception to escape.
    /// </summary>
    private static ProgressSave MigrateV1(JsonElement root)
    {
        var unknown = new List<string>();
        int legacySelected = ReadInt32(root, "selectedTool", 0);
        string? mappedSelected = LegacyToolId(legacySelected, unknown);
        string selected = mappedSelected ?? ContentIds.ToolGrab;
        string? unknownSelected = mappedSelected is null ? $"legacy.tool.{legacySelected}" : null;
        List<string> unlocks = LegacyArray(root, "unlockedTools", unknown);
        List<string> harmful = LegacyArray(root, "harmfulTools", unknown);
        if (!unlocks.Contains(ContentIds.ToolGrab, StringComparer.Ordinal))
            unlocks.Add(ContentIds.ToolGrab);

        return new ProgressSave
        {
            SchemaVersion = 2,
            Revision = ReadInt64(root, "revision"),
            BalanceMilliCredits = ReadInt64(root, "balanceMilliCredits"),
            SelectedToolId = selected,
            UnlockedToolIds = unlocks,
            Mood = ReadSingle(root, "mood"),
            HarmfulContentIds = harmful,
            ObstacleHopPropensity = ReadInt32(
                root,
                "obstacleHopPropensity",
                BuddyTraits.Default.ObstacleHopPropensity),
            Extensions = new ProgressExtensionsSave
            {
                UnknownSelectedToolId = unknownSelected,
                UnknownContentIds = unknown,
            },
        };
    }

    private static List<string> LegacyArray(
        JsonElement root,
        string property,
        List<string> unknown)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(property, out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array)
            return result;
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int legacy))
                throw new JsonException($"{property} contains a non-integer legacy ID.");
            string? id = LegacyToolId(legacy, unknown);
            if (id is not null)
                result.Add(id);
        }
        return result;
    }

    private static string? LegacyToolId(int value, List<string> unknown)
    {
        if (Enum.IsDefined(typeof(ToolId), value))
            return ContentIds.ForTool((ToolId)value);
        string retained = $"legacy.tool.{value}";
        unknown.Add(retained);
        return null;
    }

    private static long ReadInt64(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
            return 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long result))
            throw new JsonException($"Legacy field '{property}' is not an integer.");
        return result;
    }

    private static int ReadInt32(JsonElement root, string property, int fallback)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
            return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new JsonException($"Legacy field '{property}' is not an integer.");
        return result;
    }

    private static float ReadSingle(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
            return 0.0f;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out float result))
            throw new JsonException($"Legacy field '{property}' is not a number.");
        return result;
    }

    /// <summary>
    /// A blank ID, an out-of-range drain, or a non-finite meter is corruption. An entry
    /// naming an activity this build does not know is <b>not</b>: it is a newer build's data
    /// passing through, and it is retained untouched.
    /// </summary>
    private static void ValidateFunActivities(IEnumerable<FunActivitySave> activities)
    {
        foreach (FunActivitySave activity in activities)
        {
            if (activity is null || string.IsNullOrWhiteSpace(activity.ActivityId))
                throw new ArgumentException("A fun activity entry is missing its ID.");
            if (activity.Drain is < FunPreferences.MinDrain or > FunPreferences.MaxDrain)
            {
                throw new ArgumentException(
                    $"Fun activity '{activity.ActivityId}' has an out-of-range drain.");
            }
            if (!float.IsFinite(activity.Interest) ||
                activity.Interest < FunInterestModel.MinimumInterest ||
                activity.Interest > FunInterestModel.MaximumInterest)
            {
                throw new ArgumentException(
                    $"Fun activity '{activity.ActivityId}' has an out-of-range interest.");
            }
        }
    }

    /// <summary>
    /// Rebuilds the personality's tastes and the live meters from the payload. Entries this
    /// build does not know are skipped; activities the payload omits keep the neutral
    /// default taste and full novelty.
    /// </summary>
    private static (BuddyTraits Traits, List<FunActivityInterest> Interest) ReadFun(
        ProgressSave save)
    {
        FunPreferences preferences = FunPreferences.Default;
        var interest = new List<FunActivityInterest>(FunInterestModel.ActivityCount);
        foreach (FunActivitySave entry in save.FunActivities)
        {
            if (!ContentIds.TryParseFun(entry.ActivityId, out FunActivityId activity))
                continue;

            preferences = activity switch
            {
                FunActivityId.Catch => preferences with { CatchDrain = entry.Drain },
                FunActivityId.Pet => preferences with { PetDrain = entry.Drain },
                FunActivityId.Tickle => preferences with { TickleDrain = entry.Drain },
                FunActivityId.Treat => preferences with { TreatDrain = entry.Drain },
                _ => preferences,
            };
            interest.Add(new FunActivityInterest(activity, entry.Interest, entry.Bored));
        }

        return (BuddyTraits.FromPersisted(save.ObstacleHopPropensity, preferences), interest);
    }

    private static void ValidateIds(IEnumerable<string> ids, string name)
    {
        if (ids.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"{name} contains a blank ID.");
    }

    private static void ValidateSeconds(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentException($"{name} must be finite and non-negative.");
    }

    private static void ValidateCounters(ProgressStatisticsSave statistics)
    {
        long[] counters =
        [
            statistics.ScoredImpacts,
            statistics.Knockouts,
            statistics.CareAwards,
            statistics.TrustResets,
            statistics.EarnedMilliCredits,
            statistics.SuccessfulCatches,
            statistics.TotalPainMilli,
            statistics.BestOneSecondMilliCredits,
            statistics.BestThreeSecondMilliCredits,
            statistics.BestTenSecondMilliCredits,
        ];
        if (counters.Any(value => value < 0) ||
            statistics.ToolUses.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0) ||
            statistics.ToolPainMilli.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0) ||
            !float.IsFinite(statistics.HighestMood) ||
            !float.IsFinite(statistics.LowestMood) ||
            statistics.HighestMood is < -100.0f or > 100.0f ||
            statistics.LowestMood is < -100.0f or > 100.0f)
        {
            throw new ArgumentException("Progress statistics payload is invalid.");
        }
    }
}
