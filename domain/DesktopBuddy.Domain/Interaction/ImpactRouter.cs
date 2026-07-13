using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Buddy;

namespace DesktopBuddy.Domain.Interaction;

/// <summary>
/// A raw physics contact reported to the router each fixed tick, before
/// deduplication. Time is monotonic runtime seconds (RAGDOLL §2), never wall clock.
/// </summary>
public readonly record struct ContactSample(
    int SourceInteractionId,
    BuddyPart TargetPart,
    float Impulse,
    float RelativeVelocity,
    double TimeSeconds);

/// <summary>
/// A deduplicated, attributed impact accepted from a contact episode. Carries the
/// measured impulse/velocity the pain-conversion curve needs (RAGDOLL §7.1); the
/// payout region is derived downstream from <see cref="TargetPart"/> (Task 2).
/// </summary>
public readonly record struct ImpactSample(
    int SourceInteractionId,
    BuddyPart TargetPart,
    float Impulse,
    float RelativeVelocity,
    double TimeSeconds);

/// <summary>
/// Converts authoritative physics contacts into deduplicated impact samples
/// (RAGDOLL §7.1–7.2). The contact-episode key is
/// <c>(SourceInteractionId, TargetPartId)</c>: the first valid contact in an episode
/// produces exactly one accepted sample, repeated resting/sliding callbacks are
/// suppressed, and a new episode for the same key cannot begin until the key has been
/// inactive for at least <see cref="ReArmSeconds"/> (default <c>0.15 s</c>). Contacts
/// whose impulse is below <see cref="MinimumImpulse"/> are ignored entirely — a graze
/// neither scores nor opens an episode that would mask a real hit.
/// </summary>
public sealed class ImpactRouter
{
    public const double DefaultReArmSeconds = 0.15;

    private readonly Dictionary<(int, BuddyPart), double> _lastContactTime = new();

    public ImpactRouter(double reArmSeconds = DefaultReArmSeconds, float minimumImpulse = 0.0f)
    {
        if (reArmSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(reArmSeconds));
        }

        if (minimumImpulse < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumImpulse));
        }

        ReArmSeconds = reArmSeconds;
        MinimumImpulse = minimumImpulse;
    }

    public double ReArmSeconds { get; }

    public float MinimumImpulse { get; }

    /// <summary>
    /// Offers a raw contact to the router. Returns the accepted <see cref="ImpactSample"/>
    /// when this contact opens a new episode, or <c>null</c> when it is a below-threshold
    /// graze or a suppressed continuation of an active episode.
    /// </summary>
    public ImpactSample? Offer(ContactSample sample)
    {
        if (sample.Impulse < MinimumImpulse)
        {
            return null;
        }

        var key = (sample.SourceInteractionId, sample.TargetPart);
        bool newEpisode =
            !_lastContactTime.TryGetValue(key, out double lastTime) ||
            sample.TimeSeconds - lastTime >= ReArmSeconds;

        // Every valid contact keeps the episode alive so a resting/sliding stream can
        // never accumulate the 0.15 s of inactivity a re-arm requires.
        _lastContactTime[key] = sample.TimeSeconds;

        if (!newEpisode)
        {
            return null;
        }

        return new ImpactSample(
            sample.SourceInteractionId,
            sample.TargetPart,
            sample.Impulse,
            sample.RelativeVelocity,
            sample.TimeSeconds);
    }

    /// <summary>
    /// Clears all episode state. Used by the centralized hard-reposition operation
    /// (RAGDOLL §5), which releases contacts and restores a known safe pose.
    /// </summary>
    public void Reset() => _lastContactTime.Clear();
}
