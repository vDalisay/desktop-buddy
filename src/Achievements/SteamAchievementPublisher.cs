using System;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Platform.Steam;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Runtime scheduling wrapper around desired-state reconciliation. A newly changed local desired
/// set receives one immediate attempt; an unchanged failed set backs off at 60s, 120s, 240s, then
/// 300s. Successful state is an in-process no-op until another achievement qualifies.
/// </summary>
public sealed class SteamAchievementPublisher
{
    private const ulong InitialRetryMilliseconds = 60_000;
    private const ulong MaximumRetryMilliseconds = 300_000;

    private readonly AchievementProgressStore _store;
    private readonly AchievementReconciler _reconciler;
    private string? _lastAttemptFingerprint;
    private ulong _nextRetryAtMilliseconds;
    private ulong _retryDelayMilliseconds = InitialRetryMilliseconds;

    public SteamAchievementPublisher(
        AchievementProgressStore store,
        SteamAppIdentity identity,
        Node? initializedBridge)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _reconciler = new AchievementReconciler(
            store,
            new GodotSteamAchievementRemote(identity, initializedBridge));
    }

    public bool PublishingEnabled => _reconciler.IsAvailable;

    public bool TrySynchronize()
    {
        if (!PublishingEnabled)
            return false;

        string fingerprint = string.Join('\n', _store.QualifiedIds);
        ulong now = Time.GetTicksMsec();
        bool desiredStateChanged = !string.Equals(
            fingerprint,
            _lastAttemptFingerprint,
            StringComparison.Ordinal);

        if (!desiredStateChanged &&
            _nextRetryAtMilliseconds != 0 &&
            now < _nextRetryAtMilliseconds)
        {
            return false;
        }

        if (desiredStateChanged)
        {
            _nextRetryAtMilliseconds = 0;
            _retryDelayMilliseconds = InitialRetryMilliseconds;
        }

        _lastAttemptFingerprint = fingerprint;
        bool synchronized = _reconciler.TrySynchronize();
        if (synchronized)
        {
            _nextRetryAtMilliseconds = 0;
            _retryDelayMilliseconds = InitialRetryMilliseconds;
            return true;
        }

        _nextRetryAtMilliseconds = now + _retryDelayMilliseconds;
        _retryDelayMilliseconds = Math.Min(
            MaximumRetryMilliseconds,
            _retryDelayMilliseconds * 2);
        return false;
    }
}
