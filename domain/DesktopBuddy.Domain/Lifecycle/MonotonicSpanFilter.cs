using System;

namespace DesktopBuddy.Domain.Lifecycle;

/// <summary>
/// Converts monotonic timestamps into accepted gameplay spans. First samples,
/// non-forward time, and discontinuities are baselines only and award nothing.
/// </summary>
public sealed class MonotonicSpanFilter
{
    private readonly double _maximumSpanSeconds;
    private bool _hasBaseline;
    private double _previousSeconds;

    public MonotonicSpanFilter(double maximumSpanSeconds)
    {
        if (!double.IsFinite(maximumSpanSeconds) || maximumSpanSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumSpanSeconds));
        _maximumSpanSeconds = maximumSpanSeconds;
    }

    public int ExcludedSpanCount { get; private set; }

    public bool TryAccept(double currentSeconds, out double elapsedSeconds)
    {
        elapsedSeconds = 0.0;
        if (!double.IsFinite(currentSeconds))
        {
            ExcludedSpanCount++;
            _hasBaseline = false;
            return false;
        }

        if (!_hasBaseline)
        {
            _previousSeconds = currentSeconds;
            _hasBaseline = true;
            return false;
        }

        double candidate = currentSeconds - _previousSeconds;
        _previousSeconds = currentSeconds;
        if (candidate <= 0.0 || candidate > _maximumSpanSeconds)
        {
            ExcludedSpanCount++;
            return false;
        }

        elapsedSeconds = candidate;
        return true;
    }

    /// <summary>Discards elapsed time until the next observation establishes a baseline.</summary>
    public void Reset() => _hasBaseline = false;
}
