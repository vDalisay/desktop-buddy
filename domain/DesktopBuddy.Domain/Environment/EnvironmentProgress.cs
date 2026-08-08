using System;

namespace DesktopBuddy.Domain.Environment;

public readonly record struct EnvironmentProgressSnapshot(long Revision, EnvironmentLayout Layout);

public sealed class EnvironmentProgressState
{
    public EnvironmentProgressState(EnvironmentLayout? layout = null, long revision = 0)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        Layout = layout ?? new EnvironmentLayout();
        Revision = revision;
    }

    public long Revision { get; private set; }
    public EnvironmentLayout Layout { get; private set; }
    public event Action? Changed;
    public EnvironmentProgressSnapshot Snapshot() => new(Revision, Layout);

    public void Adopt(EnvironmentProgressSnapshot snapshot)
    {
        if (snapshot.Revision < 0 || snapshot.Layout is null)
            throw new ArgumentException("Environment progress snapshot is invalid.", nameof(snapshot));
        Revision = snapshot.Revision;
        Layout = snapshot.Layout;
        Changed?.Invoke();
    }

    public void Commit(EnvironmentLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (Revision == long.MaxValue)
            throw new InvalidOperationException("Environment revision is exhausted.");
        Layout = layout;
        Revision++;
        Changed?.Invoke();
    }
}
