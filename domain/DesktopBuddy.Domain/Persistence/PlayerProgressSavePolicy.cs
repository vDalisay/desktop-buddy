using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>
/// Account-only successor to the legacy aggregate <see cref="ProgressSave"/>. Buddy mood, hunger,
/// harmful memory, traits and novelty are intentionally impossible to express in this DTO.
/// </summary>
public sealed record PlayerProgressSave
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public long BalanceMilliCredits { get; init; }
    public string SelectedToolId { get; init; } = ContentIds.ToolGrab;
    public List<string> UnlockedContentIds { get; init; } = [ContentIds.ToolGrab];
    public ProgressStatisticsSave Statistics { get; init; } = new();
    public CumulativeTimesSave Times { get; init; } = new();
    public ProgressExtensionsSave? Extensions { get; init; }

    public static PlayerProgressSave FromSnapshot(in PlayerProgressSnapshot snapshot) => new()
    {
        Revision = snapshot.Revision,
        BalanceMilliCredits = snapshot.BalanceMilliCredits,
        SelectedToolId = snapshot.SelectedToolId,
        UnlockedContentIds = [.. snapshot.UnlockedContentIds],
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
                : new Dictionary<string, long>(snapshot.Statistics.ToolUses, StringComparer.Ordinal),
            ToolPainMilli = snapshot.Statistics.ToolPainMilli is null
                ? new Dictionary<string, long>(StringComparer.Ordinal)
                : new Dictionary<string, long>(snapshot.Statistics.ToolPainMilli, StringComparer.Ordinal),
        },
        Times = new CumulativeTimesSave
        {
            RunSeconds = snapshot.Times.RunSeconds,
            ActiveSeconds = snapshot.Times.ActiveSeconds,
            HiddenSeconds = snapshot.Times.HiddenSeconds,
        },
        Extensions = snapshot.Extensions is null
            ? null
            : new ProgressExtensionsSave
            {
                UnknownSelectedToolId = snapshot.Extensions.UnknownSelectedToolId,
                UnknownContentIds = snapshot.Extensions.UnknownContentIds is null
                    ? []
                    : [.. snapshot.Extensions.UnknownContentIds],
                Values = snapshot.Extensions.Values is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(snapshot.Extensions.Values, StringComparer.Ordinal),
            },
    };

    public PlayerProgressSnapshot CreateSnapshot()
    {
        Validate();
        return new PlayerProgressSnapshot(
            Revision,
            BalanceMilliCredits,
            SelectedToolId,
            UnlockedContentIds.ToArray(),
            new ProgressStatistics(
                Statistics.ScoredImpacts,
                Statistics.Knockouts,
                Statistics.CareAwards,
                Statistics.TrustResets,
                Statistics.EarnedMilliCredits,
                Statistics.SuccessfulCatches,
                Statistics.TotalPainMilli,
                Statistics.BestOneSecondMilliCredits,
                Statistics.BestThreeSecondMilliCredits,
                Statistics.BestTenSecondMilliCredits,
                Statistics.HighestMood,
                Statistics.LowestMood,
                new Dictionary<string, long>(Statistics.ToolUses, StringComparer.Ordinal),
                new Dictionary<string, long>(Statistics.ToolPainMilli, StringComparer.Ordinal)),
            new CumulativeTimes(Times.RunSeconds, Times.ActiveSeconds, Times.HiddenSeconds),
            Extensions is null
                ? null
                : new ProgressExtensionData(
                    Extensions.UnknownSelectedToolId,
                    Extensions.UnknownContentIds.ToArray(),
                    new Dictionary<string, string>(Extensions.Values, StringComparer.Ordinal)));
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("Unsupported Player progress schema.");
        if (Revision < 0 || BalanceMilliCredits < 0)
            throw new ArgumentException("Player revision and balance cannot be negative.");
        if (string.IsNullOrWhiteSpace(SelectedToolId) || !ContentIds.TryParseTool(SelectedToolId, out _))
            throw new ArgumentException("Player selected tool must be a known stable tool ID.");
        if (UnlockedContentIds is null || Statistics is null || Times is null)
            throw new ArgumentException("Player progress required records cannot be null.");

        var unlocks = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in UnlockedContentIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !unlocks.Add(id))
                throw new ArgumentException("Player unlock IDs must be non-empty and unique.");
        }
        if (!unlocks.Contains(ContentIds.ToolGrab) || !unlocks.Contains(SelectedToolId))
            throw new ArgumentException("Player progress must own Grab and its selected tool.");

        ValidateNonNegativeStatistics(Statistics);
        if (!double.IsFinite(Times.RunSeconds) || !double.IsFinite(Times.ActiveSeconds) ||
            !double.IsFinite(Times.HiddenSeconds) ||
            Times.RunSeconds < 0.0 || Times.ActiveSeconds < 0.0 || Times.HiddenSeconds < 0.0)
            throw new ArgumentException("Player cumulative times must be finite and non-negative.");
        if (Extensions is not null &&
            (Extensions.UnknownContentIds is null || Extensions.Values is null))
            throw new ArgumentException("Player extension collections cannot be null.");
    }

    private static void ValidateNonNegativeStatistics(ProgressStatisticsSave statistics)
    {
        if (statistics.ScoredImpacts < 0 || statistics.Knockouts < 0 || statistics.CareAwards < 0 ||
            statistics.TrustResets < 0 || statistics.EarnedMilliCredits < 0 ||
            statistics.SuccessfulCatches < 0 || statistics.TotalPainMilli < 0 ||
            statistics.BestOneSecondMilliCredits < 0 || statistics.BestThreeSecondMilliCredits < 0 ||
            statistics.BestTenSecondMilliCredits < 0 ||
            !float.IsFinite(statistics.HighestMood) || !float.IsFinite(statistics.LowestMood) ||
            statistics.ToolUses is null || statistics.ToolPainMilli is null ||
            statistics.ToolUses.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0) ||
            statistics.ToolPainMilli.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
            throw new ArgumentException("Player statistics payload is invalid.");
    }
}

public readonly record struct PlayerProgressDecodeResult(
    SaveDecodeStatus Status,
    PlayerProgressSnapshot? Snapshot,
    string? Detail = null);

public static class PlayerProgressSavePolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Serialize(PlayerProgressState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        PlayerProgressSave save = PlayerProgressSave.FromSnapshot(state.Snapshot());
        save.Validate();
        return JsonSerializer.Serialize(save, Options);
    }

    public static PlayerProgressDecodeResult Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new PlayerProgressDecodeResult(SaveDecodeStatus.Malformed, null, "Save payload was empty.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            bool hasSchema =
                document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                document.RootElement.TryGetProperty("SchemaVersion", out schemaElement);
            if (!hasSchema || !schemaElement.TryGetInt32(out int schema))
                return new PlayerProgressDecodeResult(SaveDecodeStatus.Malformed, null, "Missing schemaVersion.");
            if (schema > PlayerProgressSave.CurrentSchemaVersion)
            {
                return new PlayerProgressDecodeResult(
                    SaveDecodeStatus.UnsupportedFutureVersion,
                    null,
                    $"Player progress schema {schema} is newer than {PlayerProgressSave.CurrentSchemaVersion}.");
            }
            if (schema != PlayerProgressSave.CurrentSchemaVersion)
                return new PlayerProgressDecodeResult(SaveDecodeStatus.Invalid, null, $"Unsupported Player progress schema {schema}.");

            PlayerProgressSave save = JsonSerializer.Deserialize<PlayerProgressSave>(json, Options)
                ?? throw new JsonException("Player progress payload was null.");
            return new PlayerProgressDecodeResult(SaveDecodeStatus.Valid, save.CreateSnapshot());
        }
        catch (JsonException exception)
        {
            return new PlayerProgressDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return new PlayerProgressDecodeResult(SaveDecodeStatus.Invalid, null, exception.Message);
        }
    }
}
