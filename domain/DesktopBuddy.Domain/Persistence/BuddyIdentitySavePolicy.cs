using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Persistence;

public sealed record BuddyIdentityFunSave
{
    public string ActivityId { get; init; } = string.Empty;
    public int Drain { get; init; }
    public float Interest { get; init; }
    public bool Bored { get; init; }
}

/// <summary>Versioned document stored at <c>buddy-identities/&lt;buddy-id&gt;.json</c>.</summary>
public sealed record BuddyIdentitySave
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid BuddyIdentityId { get; init; }
    public long Revision { get; init; }
    public Guid? CharacterId { get; init; }
    public float Mood { get; init; }
    public float Fullness { get; init; }
    public List<string> HarmfulContentIds { get; init; } = [];
    public int ObstacleHopPropensity { get; init; } = BuddyTraits.Default.ObstacleHopPropensity;
    public List<BuddyIdentityFunSave> FunActivities { get; init; } = [];

    public static BuddyIdentitySave FromSnapshot(in BuddyIdentitySnapshot snapshot)
    {
        var interests = snapshot.FunInterest?.ToDictionary(entry => entry.Activity)
            ?? new Dictionary<FunActivityId, FunActivityInterest>();
        var fun = new List<BuddyIdentityFunSave>(FunInterestModel.ActivityCount);
        foreach (FunActivityId activity in Enum.GetValues<FunActivityId>())
        {
            FunActivityInterest interest = interests.TryGetValue(activity, out FunActivityInterest saved)
                ? saved
                : new FunActivityInterest(activity, FunInterestModel.MaximumInterest, false);
            fun.Add(new BuddyIdentityFunSave
            {
                ActivityId = ContentIds.ForFun(activity),
                Drain = snapshot.Traits.Preferences.DrainFor(activity),
                Interest = interest.Interest,
                Bored = interest.Bored,
            });
        }

        return new BuddyIdentitySave
        {
            BuddyIdentityId = snapshot.BuddyIdentityId.Value,
            Revision = snapshot.Revision,
            CharacterId = snapshot.CharacterId,
            Mood = snapshot.Mood,
            Fullness = snapshot.Fullness,
            HarmfulContentIds = [.. snapshot.HarmfulContentIds],
            ObstacleHopPropensity = snapshot.Traits.ObstacleHopPropensity,
            FunActivities = fun,
        };
    }

    public BuddyIdentitySnapshot CreateSnapshot()
    {
        Validate();
        var interests = new List<FunActivityInterest>(FunInterestModel.ActivityCount);
        FunPreferences preferences = FunPreferences.Default;
        foreach (BuddyIdentityFunSave entry in FunActivities)
        {
            ContentIds.TryParseFun(entry.ActivityId, out FunActivityId activity);
            preferences = activity switch
            {
                FunActivityId.Catch => preferences with { CatchDrain = entry.Drain },
                FunActivityId.Pet => preferences with { PetDrain = entry.Drain },
                FunActivityId.Tickle => preferences with { TickleDrain = entry.Drain },
                FunActivityId.Treat => preferences with { TreatDrain = entry.Drain },
                _ => preferences,
            };
            interests.Add(new FunActivityInterest(activity, entry.Interest, entry.Bored));
        }

        return new BuddyIdentitySnapshot(
            Persistence.BuddyIdentityId.From(BuddyIdentityId),
            Revision,
            CharacterId,
            Mood,
            Fullness,
            HarmfulContentIds.ToArray(),
            BuddyTraits.FromPersisted(ObstacleHopPropensity, preferences),
            interests);
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("Unsupported Buddy identity schema.");
        if (BuddyIdentityId == Guid.Empty || CharacterId == Guid.Empty)
            throw new ArgumentException("Buddy and Character identities cannot be empty GUIDs.");
        if (Revision < 0)
            throw new ArgumentException("Buddy identity revision cannot be negative.");
        if (!float.IsFinite(Mood) || Mood is < -100.0f or > 100.0f)
            throw new ArgumentException("Buddy mood must be finite and within -100..100.");
        if (!float.IsFinite(Fullness) || Fullness is < 0.0f or > 100.0f)
            throw new ArgumentException("Buddy fullness must be finite and within 0..100.");
        if (ObstacleHopPropensity is < BuddyTraits.MinPropensity or > BuddyTraits.MaxPropensity)
            throw new ArgumentException("Buddy hop propensity is outside the persisted range.");
        if (HarmfulContentIds is null || FunActivities is null)
            throw new ArgumentException("Buddy identity collections cannot be null.");

        var harmful = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in HarmfulContentIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !harmful.Add(id))
                throw new ArgumentException("Harmful content IDs must be non-empty and unique.");
        }

        var activities = new HashSet<FunActivityId>();
        foreach (BuddyIdentityFunSave entry in FunActivities)
        {
            if (entry is null || !ContentIds.TryParseFun(entry.ActivityId, out FunActivityId activity) ||
                !activities.Add(activity))
                throw new ArgumentException("Buddy fun activities must use unique known stable IDs.");
            if (entry.Drain is < FunPreferences.MinDrain or > FunPreferences.MaxDrain)
                throw new ArgumentException("Buddy fun drain is outside the persisted range.");
            if (!float.IsFinite(entry.Interest) ||
                entry.Interest is < FunInterestModel.MinimumInterest or > FunInterestModel.MaximumInterest)
                throw new ArgumentException("Buddy fun interest is outside the persisted range.");
        }
        if (activities.Count != FunInterestModel.ActivityCount)
            throw new ArgumentException("Buddy identity must persist every fun activity exactly once.");
    }
}

public readonly record struct BuddyIdentityDecodeResult(
    SaveDecodeStatus Status,
    BuddyIdentityState? State,
    string? Detail = null);

public static class BuddyIdentitySavePolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Serialize(BuddyIdentityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        BuddyIdentitySave save = BuddyIdentitySave.FromSnapshot(state.Snapshot());
        save.Validate();
        return JsonSerializer.Serialize(save, Options);
    }

    public static BuddyIdentityDecodeResult Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new BuddyIdentityDecodeResult(SaveDecodeStatus.Malformed, null, "Save payload was empty.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            bool hasSchema =
                document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                document.RootElement.TryGetProperty("SchemaVersion", out schemaElement);
            if (!hasSchema || !schemaElement.TryGetInt32(out int schema))
                return new BuddyIdentityDecodeResult(SaveDecodeStatus.Malformed, null, "Missing schemaVersion.");
            if (schema > BuddyIdentitySave.CurrentSchemaVersion)
            {
                return new BuddyIdentityDecodeResult(
                    SaveDecodeStatus.UnsupportedFutureVersion,
                    null,
                    $"Buddy identity schema {schema} is newer than {BuddyIdentitySave.CurrentSchemaVersion}.");
            }
            if (schema != BuddyIdentitySave.CurrentSchemaVersion)
                return new BuddyIdentityDecodeResult(SaveDecodeStatus.Invalid, null, $"Unsupported Buddy identity schema {schema}.");

            BuddyIdentitySave save = JsonSerializer.Deserialize<BuddyIdentitySave>(json, Options)
                ?? throw new JsonException("Buddy identity payload was null.");
            BuddyIdentitySnapshot snapshot = save.CreateSnapshot();
            return new BuddyIdentityDecodeResult(
                SaveDecodeStatus.Valid,
                new BuddyIdentityState(snapshot));
        }
        catch (JsonException exception)
        {
            return new BuddyIdentityDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new BuddyIdentityDecodeResult(SaveDecodeStatus.Invalid, null, exception.Message);
        }
    }
}
