using System;
using System.Diagnostics;
using DesktopBuddy.Domain.Lifecycle;

namespace DesktopBuddy.App;

public interface IMonotonicTimeSource
{
    double Seconds { get; }
}

public sealed class StopwatchTimeSource : IMonotonicTimeSource
{
    public double Seconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

/// <summary>Runtime adapter around the pure no-catch-up span policy.</summary>
public sealed class GameClock
{
    private readonly IMonotonicTimeSource _source;
    private readonly MonotonicSpanFilter _filter;

    public GameClock(IMonotonicTimeSource source, double maximumSpanSeconds)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _filter = new MonotonicSpanFilter(maximumSpanSeconds);
    }

    public int ExcludedSpanCount => _filter.ExcludedSpanCount;
    public bool TrySample(out double elapsedSeconds) =>
        _filter.TryAccept(_source.Seconds, out elapsedSeconds);
    public void Reset() => _filter.Reset();
}
