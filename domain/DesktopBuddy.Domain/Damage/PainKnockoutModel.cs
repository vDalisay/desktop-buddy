using System.Collections.Generic;

namespace DesktopBuddy.Domain.Damage;

/// <summary>Damage-side consciousness state driven by the knockout timer.</summary>
public enum DamageConsciousness
{
    Conscious,
    Unconscious,
}

/// <summary>Immutable snapshot of the rolling pain window and knockout state.</summary>
public readonly record struct PainKnockoutState(
    DamageConsciousness Consciousness,
    float RollingPain,
    bool KnockoutActive);

/// <summary>
/// Maintains the rolling <c>5 s</c> accepted-pain window and the fixed <c>4 s</c>
/// knockout (RAGDOLL §7.3). When the windowed pain sum reaches <c>100</c> while
/// conscious the buddy enters Unconscious once, a monotonic 4 s timer starts, and the
/// window clears. Further knockout triggers are ignored until the timer completes —
/// later hits neither restart nor extend it — and hits landed while unconscious stay
/// valid pain/reward/mood events (the caller applies those) but are excluded here, so
/// waking always begins with an empty window. All times are monotonic runtime seconds.
/// </summary>
public sealed class PainKnockoutModel
{
    public const double WindowSeconds = 5.0;
    public const float KnockoutThreshold = 100.0f;
    public const double KnockoutSeconds = 4.0;

    private readonly List<(double Time, float Pain)> _events = new();
    private bool _unconscious;
    private double _knockoutEndsAt;

    public int KnockoutCount { get; private set; }

    /// <summary>
    /// Records an accepted-pain event. Only pain landed while conscious enters the
    /// rolling window; unconscious hits are excluded (§7.3). Returns the resulting state.
    /// </summary>
    public PainKnockoutState RegisterPain(float pain, double now)
    {
        WakeIfElapsed(now);

        if (!_unconscious)
        {
            PruneOldEvents(now);
            _events.Add((now, pain));

            if (WindowedPain() >= KnockoutThreshold)
            {
                EnterKnockout(now);
            }
        }

        return StateAt(now);
    }

    /// <summary>Advances time without new pain: wakes at timer completion, prunes the window.</summary>
    public PainKnockoutState Update(double now)
    {
        WakeIfElapsed(now);
        if (!_unconscious)
        {
            PruneOldEvents(now);
        }

        return StateAt(now);
    }

    /// <summary>
    /// Repair Kit: clears transient/rolling pain but does NOT shorten an active
    /// knockout (RAGDOLL §7.3 last paragraph).
    /// </summary>
    public void ClearRollingPain() => _events.Clear();

    /// <summary>
    /// Centralized hard reposition (RAGDOLL §5): clears rolling pain and knockout,
    /// restoring the conscious baseline.
    /// </summary>
    public void Reset()
    {
        _events.Clear();
        _unconscious = false;
        _knockoutEndsAt = 0.0;
    }

    private void EnterKnockout(double now)
    {
        _unconscious = true;
        _knockoutEndsAt = now + KnockoutSeconds;
        _events.Clear();
        KnockoutCount++;
    }

    private void WakeIfElapsed(double now)
    {
        if (_unconscious && now >= _knockoutEndsAt)
        {
            _unconscious = false;
        }
    }

    private void PruneOldEvents(double now)
    {
        _events.RemoveAll(e => now - e.Time > WindowSeconds);
    }

    private float WindowedPain()
    {
        float sum = 0.0f;
        foreach ((double _, float pain) in _events)
        {
            sum += pain;
        }

        return sum;
    }

    private PainKnockoutState StateAt(double now) => new(
        _unconscious ? DamageConsciousness.Unconscious : DamageConsciousness.Conscious,
        _unconscious ? 0.0f : WindowedPain(),
        _unconscious);
}
