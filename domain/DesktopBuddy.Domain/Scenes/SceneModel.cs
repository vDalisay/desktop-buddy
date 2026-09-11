using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Scenes;

/// <summary>
/// Stable persistent identity for one Scene. Display names are never identity and never become
/// filesystem paths; Scene storage must use this ID (or another trusted mapping) as its durable key.
/// </summary>
public readonly struct SceneId : IEquatable<SceneId>, IComparable<SceneId>
{
    private static readonly Guid LegacyHomeValue =
        Guid.Parse("e6408bbb-61e9-47ad-b59f-5ea43db78100");

    private SceneId(Guid value) => Value = value;

    public Guid Value { get; }
    public bool IsValid => Value != Guid.Empty;
    public static SceneId LegacyHome => new(LegacyHomeValue);
    public static SceneId New() => new(Guid.NewGuid());

    public static SceneId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Scene ID cannot be empty.", nameof(value));
        return new SceneId(value);
    }

    public bool Equals(SceneId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SceneId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(SceneId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsValid ? Value.ToString("N") : string.Empty;
    public static bool operator ==(SceneId left, SceneId right) => left.Equals(right);
    public static bool operator !=(SceneId left, SceneId right) => !left.Equals(right);
}

/// <summary>Stable identity of one Buddy placement inside one Scene.</summary>
public readonly struct BuddyPlacementId : IEquatable<BuddyPlacementId>, IComparable<BuddyPlacementId>
{
    private static readonly Guid LegacyPrimaryValue =
        Guid.Parse("996593ef-0837-4c79-a894-470d3b8cc100");

    private BuddyPlacementId(Guid value) => Value = value;

    public Guid Value { get; }
    public bool IsValid => Value != Guid.Empty;
    public static BuddyPlacementId LegacyPrimary => new(LegacyPrimaryValue);
    public static BuddyPlacementId New() => new(Guid.NewGuid());

    public static BuddyPlacementId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Buddy placement ID cannot be empty.", nameof(value));
        return new BuddyPlacementId(value);
    }

    public bool Equals(BuddyPlacementId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is BuddyPlacementId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(BuddyPlacementId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsValid ? Value.ToString("N") : string.Empty;
    public static bool operator ==(BuddyPlacementId left, BuddyPlacementId right) => left.Equals(right);
    public static bool operator !=(BuddyPlacementId left, BuddyPlacementId right) => !left.Equals(right);
}

/// <summary>
/// Semantic spawn/return anchor for one persistent Buddy identity. No ragdoll transforms,
/// velocities, engine NodePaths or runtime object IDs are persisted here.
/// </summary>
public readonly record struct BuddyPlacement(
    BuddyPlacementId PlacementId,
    BuddyIdentityId BuddyIdentityId,
    CanonicalRoomPosition Position);

/// <summary>
/// Engine-free durable Scene root. The complete Room Decorator state is Scene-owned: both the
/// placed layout and purchased-but-unplaced storage travel with the Scene, together with their
/// semantic revision. The <see cref="Environment"/> property remains as a compatibility view for
/// callers that only need the placed layout. Systemic sandbox state remains separately versioned
/// and is intentionally not smuggled into this root yet.
/// </summary>
public sealed class SceneDocument
{
    public const int CurrentSchemaVersion = 2;
    public const int MaximumNameLength = 64;

    private readonly BuddyPlacement[] _placements;
    private readonly DecorationDefinitionId[] _ownedUnplaced;

    /// <summary>
    /// Compatibility constructor for callers that have only a layout. New Scene persistence should
    /// pass an <see cref="EnvironmentProgressSnapshot"/> so storage and revision cannot be dropped.
    /// </summary>
    public SceneDocument(
        SceneId sceneId,
        string name,
        EnvironmentLayout environment,
        IEnumerable<BuddyPlacement>? buddyPlacements = null,
        int schemaVersion = CurrentSchemaVersion)
        : this(
            sceneId,
            name,
            new EnvironmentProgressSnapshot(0, environment, []),
            buddyPlacements,
            schemaVersion)
    {
    }

    public SceneDocument(
        SceneId sceneId,
        string name,
        EnvironmentProgressSnapshot environmentProgress,
        IEnumerable<BuddyPlacement>? buddyPlacements = null,
        int schemaVersion = CurrentSchemaVersion,
        bool buddiesCollide = true)
    {
        if (!sceneId.IsValid)
            throw new ArgumentException("Scene document requires a stable Scene ID.", nameof(sceneId));
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported Scene document schema.");
        ValidateName(name);
        if (environmentProgress.Revision < 0 || environmentProgress.Layout is null)
            throw new ArgumentException("Scene environment progress is invalid.", nameof(environmentProgress));

        _placements = buddyPlacements?.ToArray() ?? [];
        var placementIds = new HashSet<BuddyPlacementId>();
        var buddyIds = new HashSet<BuddyIdentityId>();
        foreach (BuddyPlacement placement in _placements)
        {
            if (!placement.PlacementId.IsValid || !placement.BuddyIdentityId.IsValid)
                throw new ArgumentException("Buddy placements require stable placement and Buddy IDs.", nameof(buddyPlacements));
            if (!placementIds.Add(placement.PlacementId))
                throw new ArgumentException("A Scene cannot contain duplicate Buddy placement IDs.", nameof(buddyPlacements));
            if (!buddyIds.Add(placement.BuddyIdentityId))
            {
                throw new ArgumentException(
                    "The same Buddy identity cannot appear twice in one Scene; create a separate Buddy identity for a twin/clone.",
                    nameof(buddyPlacements));
            }
        }

        _ownedUnplaced = environmentProgress.OwnedUnplaced?.ToArray() ?? [];
        foreach (DecorationDefinitionId id in _ownedUnplaced)
        {
            if (id == default)
                throw new ArgumentException("Scene environment storage cannot contain an invalid decoration ID.", nameof(environmentProgress));
        }

        SchemaVersion = schemaVersion;
        SceneId = sceneId;
        Name = name;
        BuddiesCollide = buddiesCollide;
        EnvironmentProgress = new EnvironmentProgressSnapshot(
            environmentProgress.Revision,
            new EnvironmentLayout(
                environmentProgress.Layout.Decorations,
                environmentProgress.Layout.SchemaVersion),
            _ownedUnplaced);
    }

    public int SchemaVersion { get; }
    public SceneId SceneId { get; }
    public string Name { get; }
    public EnvironmentProgressSnapshot EnvironmentProgress { get; }
    public long EnvironmentRevision => EnvironmentProgress.Revision;
    public EnvironmentLayout Environment => EnvironmentProgress.Layout;
    public IReadOnlyList<DecorationDefinitionId> OwnedUnplaced => _ownedUnplaced;
    public IReadOnlyList<BuddyPlacement> BuddyPlacements => _placements;

    /// <summary>
    /// Whether this room's Buddies bump into each other. On by default: separate bodies that pass
    /// through each other read as ghosts rather than a cast (owner instruction 2026-09-10). It is a
    /// per-Scene setting, so one room can be a pile-up and another a calm gallery.
    /// </summary>
    public bool BuddiesCollide { get; }

    public static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumNameLength)
            throw new ArgumentException($"Scene names must contain 1-{MaximumNameLength} visible characters.", nameof(name));
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Scene names cannot start or end with whitespace.", nameof(name));
        foreach (char character in name)
        {
            if (char.IsControl(character))
                throw new ArgumentException("Scene names cannot contain control characters.", nameof(name));
        }
    }
}

/// <summary>
/// Deterministic semantic half of Initial Demo -> Next Fest migration. File copying/atomic commit is
/// deliberately outside this policy; it only constructs the destination Scene graph with reserved
/// IDs so retrying a failed migration targets the same objects rather than duplicating them.
/// </summary>
public static class LegacySceneMigrationPolicy
{
    public const string DefaultSceneName = "Home";

    public static SceneDocument CreateDefaultScene(
        BuddyIdentityId legacyBuddyIdentityId,
        EnvironmentLayout legacyEnvironment,
        CanonicalRoomPosition buddyPosition) =>
        CreateDefaultScene(
            legacyBuddyIdentityId,
            new EnvironmentProgressSnapshot(0, legacyEnvironment, []),
            buddyPosition);

    public static SceneDocument CreateDefaultScene(
        BuddyIdentityId legacyBuddyIdentityId,
        EnvironmentProgressSnapshot legacyEnvironment,
        CanonicalRoomPosition buddyPosition)
    {
        if (!legacyBuddyIdentityId.IsValid)
            throw new ArgumentException("Legacy migration requires a stable Buddy identity.", nameof(legacyBuddyIdentityId));
        if (legacyEnvironment.Layout is null)
            throw new ArgumentException("Legacy migration requires valid environment progress.", nameof(legacyEnvironment));

        return new SceneDocument(
            SceneId.LegacyHome,
            DefaultSceneName,
            legacyEnvironment,
            [
                new BuddyPlacement(
                    BuddyPlacementId.LegacyPrimary,
                    legacyBuddyIdentityId,
                    buddyPosition),
            ]);
    }
}
