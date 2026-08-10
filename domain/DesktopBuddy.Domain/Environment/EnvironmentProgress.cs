using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Environment;

/// <summary>
/// <paramref name="OwnedUnplaced"/> is the player's storage: copies they paid for and later took
/// out of the room. A purchase is owned forever, so deleting a placed copy banks it here instead of
/// destroying it, and placing an owned copy again is free.
/// </summary>
public readonly record struct EnvironmentProgressSnapshot(
    long Revision,
    EnvironmentLayout Layout,
    IReadOnlyList<DecorationDefinitionId>? OwnedUnplaced = null);

public sealed class EnvironmentProgressState
{
    public EnvironmentProgressState(EnvironmentLayout? layout = null, long revision = 0,
        IEnumerable<DecorationDefinitionId>? ownedUnplaced = null)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        Layout = layout ?? new EnvironmentLayout();
        Revision = revision;
        OwnedUnplaced = ownedUnplaced?.ToArray() ?? [];
    }

    public long Revision { get; private set; }
    public EnvironmentLayout Layout { get; private set; }
    public IReadOnlyList<DecorationDefinitionId> OwnedUnplaced { get; private set; }
    public event Action? Changed;
    public EnvironmentProgressSnapshot Snapshot() => new(Revision, Layout, OwnedUnplaced);

    public void Adopt(EnvironmentProgressSnapshot snapshot)
    {
        if (snapshot.Revision < 0 || snapshot.Layout is null)
            throw new ArgumentException("Environment progress snapshot is invalid.", nameof(snapshot));
        Revision = snapshot.Revision;
        Layout = snapshot.Layout;
        OwnedUnplaced = snapshot.OwnedUnplaced ?? [];
        Changed?.Invoke();
    }

    public void Commit(EnvironmentLayout layout, IEnumerable<DecorationDefinitionId>? ownedUnplaced = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (Revision == long.MaxValue)
            throw new InvalidOperationException("Environment revision is exhausted.");
        Layout = layout;
        OwnedUnplaced = ownedUnplaced?.ToArray() ?? [];
        Revision++;
        Changed?.Invoke();
    }
}
