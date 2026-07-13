using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Damage;

/// <summary>One point on the empirical impulse→pain curve.</summary>
public readonly record struct PainAnchor(float Impulse, float Pain);

/// <summary>
/// Converts an accepted contact impulse into non-negative pain via a piecewise-linear
/// empirical curve (RAGDOLL §7.2). The anchor data is approved tuning carried in a
/// Godot <c>Resource</c>; the Domain holds only the interpolation. Below the first
/// anchor the curve saturates to its pain (typically <c>0</c>, so a soft touch scores
/// nothing); above the last anchor it saturates to the final pain. Anchors must be
/// strictly increasing in impulse and non-decreasing, non-negative in pain, so the
/// mapping is monotonic — a harder hit never yields less pain.
/// </summary>
public sealed class PainCurve
{
    private readonly PainAnchor[] _anchors;

    public PainCurve(IReadOnlyList<PainAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        if (anchors.Count < 2)
        {
            throw new ArgumentException("A pain curve needs at least two anchors.", nameof(anchors));
        }

        _anchors = anchors.ToArray();
        for (int i = 0; i < _anchors.Length; i++)
        {
            if (_anchors[i].Pain < 0.0f)
            {
                throw new ArgumentException("Pain anchors must be non-negative.", nameof(anchors));
            }

            if (i > 0 && _anchors[i].Impulse <= _anchors[i - 1].Impulse)
            {
                throw new ArgumentException("Pain anchors must be strictly increasing in impulse.", nameof(anchors));
            }

            if (i > 0 && _anchors[i].Pain < _anchors[i - 1].Pain)
            {
                throw new ArgumentException("Pain anchors must be non-decreasing in pain.", nameof(anchors));
            }
        }
    }

    public float PainFor(float impulse)
    {
        if (impulse <= _anchors[0].Impulse)
        {
            return _anchors[0].Pain;
        }

        PainAnchor last = _anchors[^1];
        if (impulse >= last.Impulse)
        {
            return last.Pain;
        }

        for (int i = 1; i < _anchors.Length; i++)
        {
            PainAnchor hi = _anchors[i];
            if (impulse <= hi.Impulse)
            {
                PainAnchor lo = _anchors[i - 1];
                float t = (impulse - lo.Impulse) / (hi.Impulse - lo.Impulse);
                return lo.Pain + t * (hi.Pain - lo.Pain);
            }
        }

        return last.Pain;
    }
}
