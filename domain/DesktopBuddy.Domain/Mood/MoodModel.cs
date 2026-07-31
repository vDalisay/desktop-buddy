using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Mood;

/// <summary>Persistent-mood bands that bias behaviour and face/posture (RAGDOLL §8.1).</summary>
public enum MoodBand
{
    Fearful,
    Wary,
    Neutral,
    Content,
    Delighted,
}

/// <summary>
/// Owns the hidden persistent mood and the learned harmful-history memory
/// (RAGDOLL §7.2, §8.1, §4.1). Mood is clamped to <c>[-100, +100]</c>; each accepted
/// harmful event lowers it by <c>min(10, pain × 0.1)</c> (Burning ticks use the same
/// formula, and entering knockout adds no separate penalty). While running, mood drifts
/// toward <c>0</c> at <c>0.5</c> points/minute using monotonic elapsed time supplied by
/// the caller — a closed/slept/clock-gap span is simply never handed in, so there is no
/// catch-up. Crossing upward from below <c>60</c> to <c>60</c>+ fires exactly one
/// trust reset (clears all harmful/fear records) and re-arms only after mood later falls
/// below <c>60</c>. Hard reposition preserves mood and history (§5), so there is no reset.
/// </summary>
public sealed class MoodModel
{
    public const float Min = -100.0f;
    public const float Max = 100.0f;
    public const float TrustResetThreshold = 60.0f;
    public const float MaxHarmReduction = 10.0f;
    public const double DriftPointsPerMinute = 0.5;

    private readonly HashSet<string> _harmfulTools = new(StringComparer.Ordinal);

    /// <param name="initialMood">Persisted mood, or <c>0</c> for a new save.</param>
    /// <param name="harmfulTools">
    /// Persisted harmful-history content IDs, restored without re-running the crossing
    /// rule — loading a delighted save with recorded harm is a restore, not a trust event.
    /// </param>
    public MoodModel(float initialMood = 0.0f, IEnumerable<string>? harmfulTools = null)
    {
        Mood = Math.Clamp(initialMood, Min, Max);

        if (harmfulTools is null)
        {
            return;
        }

        foreach (string contentId in harmfulTools)
        {
            if (!string.IsNullOrWhiteSpace(contentId))
            {
                _harmfulTools.Add(contentId);
            }
        }
    }

    public float Mood { get; private set; }

    public MoodBand Band => BandFor(Mood);

    /// <summary>Stable content IDs recorded as harmful (ARCHITECTURE §5).</summary>
    public IReadOnlyCollection<string> HarmfulTools => _harmfulTools;

    public bool IsToolHarmful(string contentId) => _harmfulTools.Contains(contentId);

    /// <summary>
    /// Applies an accepted harmful event: records the source as harmful and lowers mood by
    /// <c>min(10, pain × 0.1)</c>. Harm can only push mood down, so it never trust-resets.
    /// </summary>
    public void RegisterHarm(string contentId, float pain)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ArgumentException(
                "Harmful history requires a stable content ID (ARCHITECTURE §5).",
                nameof(contentId));
        }

        _harmfulTools.Add(contentId);
        SetMood(Mood - MoodLossForPain(pain));
    }

    /// <summary>The shared pain-sized mood loss, usable by transient annoyance too.</summary>
    public static float MoodLossForPain(float pain) =>
        Math.Min(MaxHarmReduction, Math.Max(0.0f, pain) * 0.1f);

    /// <summary>
    /// Applies a mood change (e.g. care rewards). Returns <c>true</c> when this change
    /// crosses upward through <c>60</c> and fires the trust reset.
    /// </summary>
    public bool ApplyMoodDelta(float delta) => SetMood(Mood + delta);

    /// <summary>
    /// Drifts mood toward <c>0</c> at <c>0.5</c> points/minute over the given monotonic
    /// elapsed seconds, never overshooting neutral.
    /// </summary>
    public void Drift(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0 || Mood == 0.0f)
        {
            return;
        }

        float amount = (float)(DriftPointsPerMinute / 60.0 * elapsedSeconds);
        float drifted = Mood > 0.0f
            ? Math.Max(0.0f, Mood - amount)
            : Math.Min(0.0f, Mood + amount);
        SetMood(drifted);
    }

    public static MoodBand BandFor(float mood) => mood switch
    {
        <= -61.0f => MoodBand.Fearful,
        <= -21.0f => MoodBand.Wary,
        <= 20.0f => MoodBand.Neutral,
        <= 60.0f => MoodBand.Content,
        _ => MoodBand.Delighted,
    };

    private bool SetMood(float value)
    {
        bool wasBelow = Mood < TrustResetThreshold;
        Mood = Math.Clamp(value, Min, Max);
        bool nowBelow = Mood < TrustResetThreshold;

        if (wasBelow && !nowBelow)
        {
            _harmfulTools.Clear();
            return true;
        }

        return false;
    }
}
