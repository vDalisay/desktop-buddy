using System;

namespace DesktopBuddy.Domain.Autonomy;

/// <summary>
/// Per-save personality traits (DECISIONS 2026-07-14 deferred jump personality,
/// confirmed 2026-07-24). Sampled exactly once at new-save creation from the dedicated
/// save-creation RNG stream, persisted, and never resampled on load — a buddy's
/// personality is part of its identity, not a per-session roll.
///
/// <see cref="ObstacleHopPropensity"/> is a deterministic <c>0–100</c> bucket rather than
/// a float so a save round-trip reproduces the exact value. It gates obstacle hops only:
/// pure-timer ambient jumping stays OFF (DECISIONS 2026-07-20 "too random").
///
/// <see cref="Preferences"/> carries what this buddy enjoys, on the same terms: sampled
/// once at creation, persisted, never rerolled (owner instruction 2026-07-27 — "every buddy
/// should be a bit different and have different preferences").
/// </summary>
public readonly record struct BuddyTraits(
    int ObstacleHopPropensity,
    FunPreferences Preferences)
{
    public const int MinPropensity = 0;
    public const int MaxPropensity = 100;

    /// <summary>Neutral default used before a save exists and by saveless test composition.</summary>
    public static BuddyTraits Default => new(50, FunPreferences.Default);

    /// <summary>Propensity-only construction for the existing hop-trait call sites.</summary>
    public BuddyTraits(int obstacleHopPropensity)
        : this(obstacleHopPropensity, FunPreferences.Default)
    {
    }

    /// <summary>Clamps a loaded or migrated value into the valid bucket range.</summary>
    public static BuddyTraits FromPersisted(int obstacleHopPropensity) =>
        FromPersisted(obstacleHopPropensity, FunPreferences.Default);

    /// <summary>Clamps a full loaded or migrated personality into its valid ranges.</summary>
    public static BuddyTraits FromPersisted(
        int obstacleHopPropensity,
        FunPreferences preferences) =>
        new(
            Math.Clamp(obstacleHopPropensity, MinPropensity, MaxPropensity),
            FunPreferences.FromPersisted(
                preferences.CatchDrain,
                preferences.PetDrain,
                preferences.TickleDrain,
                preferences.TreatDrain));

    /// <summary>
    /// Samples fresh traits for a <b>new save only</b>. The caller must pass the dedicated
    /// save-creation RNG stream, never the behavior or presentation stream (ARCHITECTURE
    /// §23) — mixing them would make a buddy's personality depend on how it was played.
    ///
    /// Propensity is uniform across the full <c>0–100</c> bucket range (owner decision 2:
    /// "sampled uniformly in an engineering-chosen range"), so the population spans buddies
    /// that never hop obstacles and buddies that always do. Tastes are drawn from the same
    /// stream immediately afterwards, so one seed yields one whole personality.
    /// </summary>
    public static BuddyTraits Sample(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        // Inclusive of both ends: a 0 buddy and a 100 buddy are both valid personalities.
        int propensity = random.NextInt(MinPropensity, MaxPropensity + 1);
        return new BuddyTraits(propensity, FunPreferences.Sample(random));
    }
}
