namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>What one traced player action was. A closed set: the trace records behaviour,
/// never money, so a price or curve change can never regenerate the behaviour.</summary>
public enum BenchmarkEventKind
{
    /// <summary>One physics contact offered to the router (may be a graze or a duplicate).</summary>
    Contact,

    /// <summary>One care mood award, in mood points.</summary>
    Care,

    /// <summary>Time from here on counts as active interaction.</summary>
    ActiveStart,

    /// <summary>Time from here on counts as background/hidden running time.</summary>
    BackgroundStart,
}

/// <summary>
/// One element of a benchmark trace (M5 Tasks 11–13 §4.1). <paramref name="Magnitude"/> is
/// a contact impulse or a care mood delta — never a credit amount — and
/// <paramref name="BodyRegion"/> is the <see cref="Buddy.BuddyPart"/> ordinal the contact
/// landed on.
/// </summary>
public readonly record struct BenchmarkEvent(
    double AtSeconds,
    BenchmarkEventKind Kind,
    string ContentId,
    float Magnitude,
    int BodyRegion);
