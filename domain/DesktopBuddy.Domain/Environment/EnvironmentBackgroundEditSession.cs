using System;

namespace DesktopBuddy.Domain.Environment;

public sealed class EnvironmentBackgroundEditSession
{
    private readonly EnvironmentLayout _baseline;

    public EnvironmentBackgroundEditSession(EnvironmentLayout baseline)
    {
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        Working = baseline.Background;
    }

    public EnvironmentBackground Working { get; private set; }
    public bool IsDirty => Working != _baseline.Background;
    public bool MatchesBaseline(EnvironmentLayout layout) =>
        layout.SchemaVersion == _baseline.SchemaVersion &&
        layout.Background == _baseline.Background &&
        System.Linq.Enumerable.SequenceEqual(layout.Decorations, _baseline.Decorations);

    public void SetColor(EnvironmentBackgroundZone zone, EnvironmentColor color) => Working = Working.WithColor(zone, color);
    public void Reset() => Working = EnvironmentBackground.Default;
    public void Cancel() => Working = _baseline.Background;
    public EnvironmentLayout PrepareLayout() => new(_baseline.Decorations, background: Working);
}
