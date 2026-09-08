using System;
using System.Collections.Generic;
using System.Text.Json;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>
/// Standalone account-global Work Mode document used by Scene-enabled saves. Work progress used to
/// live inside the legacy one-Buddy ProgressSave aggregate; splitting it into its own versioned file
/// prevents the Initial Demo -> Next Fest migration from dropping lifetime counters or an active
/// Work session when progress.json becomes account-only PlayerProgressState.
/// </summary>
public sealed record WorkProgressDocumentSave
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public WorkProgressSave Progress { get; init; } = new();
}

public readonly record struct WorkProgressDecodeResult(
    SaveDecodeStatus Status,
    WorkProgressState? State,
    string? Detail = null);

public static class WorkProgressSavePolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Serialize(WorkProgressState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var save = new WorkProgressDocumentSave
        {
            Progress = WorkProgressSave.FromSnapshot(state.Snapshot()),
        };
        Validate(save);
        return JsonSerializer.Serialize(save, Options);
    }

    public static WorkProgressDecodeResult Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new WorkProgressDecodeResult(SaveDecodeStatus.Malformed, null, "Save payload was empty.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            bool hasSchema =
                document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                document.RootElement.TryGetProperty("SchemaVersion", out schemaElement);
            if (!hasSchema || !schemaElement.TryGetInt32(out int schema))
                return new WorkProgressDecodeResult(SaveDecodeStatus.Malformed, null, "Missing schemaVersion.");
            if (schema > WorkProgressDocumentSave.CurrentSchemaVersion)
            {
                return new WorkProgressDecodeResult(
                    SaveDecodeStatus.UnsupportedFutureVersion,
                    null,
                    $"Work progress schema {schema} is newer than {WorkProgressDocumentSave.CurrentSchemaVersion}.");
            }
            if (schema != WorkProgressDocumentSave.CurrentSchemaVersion)
                return new WorkProgressDecodeResult(SaveDecodeStatus.Invalid, null, $"Unsupported Work progress schema {schema}.");

            WorkProgressDocumentSave save = JsonSerializer.Deserialize<WorkProgressDocumentSave>(json, Options)
                ?? throw new JsonException("Work progress payload was null.");
            Validate(save);
            return new WorkProgressDecodeResult(SaveDecodeStatus.Valid, save.Progress.CreateState());
        }
        catch (JsonException exception)
        {
            return new WorkProgressDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return new WorkProgressDecodeResult(SaveDecodeStatus.Invalid, null, exception.Message);
        }
    }

    private static void Validate(WorkProgressDocumentSave save)
    {
        if (save.SchemaVersion != WorkProgressDocumentSave.CurrentSchemaVersion || save.Progress is null)
            throw new ArgumentException("Unsupported Work progress document.", nameof(save));

        WorkProgressSave progress = save.Progress;
        if (progress.Revision < 0 || progress.KeyboardPresses < 0 || progress.MouseClicks < 0 ||
            progress.ClaimedLifetimeMilestoneIds is null)
        {
            throw new ArgumentException("Work progress counters or collections are invalid.", nameof(save));
        }
        ValidateIds(progress.ClaimedLifetimeMilestoneIds, "lifetime milestone");

        WorkSessionSave? session = progress.ActiveSession;
        if (session is null)
            return;
        if (session.SessionId == Guid.Empty || session.KeyboardPresses < 0 || session.MouseClicks < 0 ||
            session.EarnedRepeatPerSessionMilestoneIds is null)
        {
            throw new ArgumentException("Active Work session is invalid.", nameof(save));
        }
        ValidateIds(session.EarnedRepeatPerSessionMilestoneIds, "session milestone");
    }

    private static void ValidateIds(IReadOnlyList<string> ids, string kind)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !unique.Add(id))
                throw new ArgumentException($"Work {kind} IDs must be non-empty and unique.");
        }
    }
}
