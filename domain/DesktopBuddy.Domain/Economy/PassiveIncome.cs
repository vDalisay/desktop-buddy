using System;

namespace DesktopBuddy.Domain.Economy;

/// <summary>
/// Mood-scaled passive income (RAGDOLL §8). The mood multiplier is piecewise-linear
/// through the approved anchors <c>0.25×</c> at mood <c>-100</c>, <c>1.0×</c> at <c>0</c>,
/// and <c>2.0×</c> at <c>+100</c>, applied to a base earn rate from approved tuning.
/// Earnings accrue in milli-credits over monotonic elapsed seconds supplied by the caller
/// — including the low-cost hidden-to-tray clock — with a fractional carry so no sub-milli
/// amount is lost or double-counted. A closed/slept/clock-gap span is never handed in, so
/// there is no catch-up income.
/// </summary>
public sealed class PassiveIncome
{
    private readonly double _baseCreditsPerSecond;
    private double _fractionalMilli;

    public PassiveIncome(double baseCreditsPerSecond)
    {
        if (baseCreditsPerSecond < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseCreditsPerSecond));
        }

        _baseCreditsPerSecond = baseCreditsPerSecond;
    }

    /// <summary>Piecewise-linear mood earning multiplier through the approved anchors.</summary>
    public static float MoodMultiplier(float mood)
    {
        float clamped = Math.Clamp(mood, -100.0f, 100.0f);
        return clamped < 0.0f
            ? Lerp(0.25f, 1.0f, (clamped + 100.0f) / 100.0f)
            : Lerp(1.0f, 2.0f, clamped / 100.0f);
    }

    /// <summary>
    /// Accrues passive income for the given mood over <paramref name="elapsedSeconds"/> and
    /// returns the whole milli-credits earned this call (fractional remainder carried).
    /// </summary>
    public long Accrue(float mood, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0)
        {
            return 0;
        }

        double credits = _baseCreditsPerSecond * MoodMultiplier(mood) * elapsedSeconds;
        _fractionalMilli += credits * RewardLedger.MilliCreditsPerCredit;

        long whole = (long)Math.Floor(_fractionalMilli);
        _fractionalMilli -= whole;
        return whole;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
