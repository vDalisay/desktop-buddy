using System;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Platform.Steam;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Runtime scheduling wrapper around the platform-free desired-state reconciler. Qualification is
/// local-first; Steam is a best-effort mirror. New desired state gets an immediate attempt, while a
/// failed upload backs off monotonically so StoreStats is never hammered by the 5-second observer
/// tick. Successful state is a no-op until another local achievement qualifies.
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
        var remote = new GodotSteamAchievementRemote(identity, initializedBridge);
        _reconciler = new AchievementReconciler(store, remote);
    }

    public bool PublishingEnabled => _reconciler.IsAvailable;

    /// <summary>
    /// Reconciles the complete locally-qualified set. Steamworks SDK 1.61 loads current stats and
    /// achievements before process start, so RequestCurrentStats is unnecessary. Valve documents
    /// StoreStats as rate-limited; failed unchanged batches therefore retry at 60s, 120s, 240s and
    /// then 300s, while a genuinely new local qualification bypasses the wait once.
    /// </summary>
    public bool TrySynchronize()
    {
        if (!PublishingEnabled)
            return false;

        string fingerprint = CurrentFingerprint();
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

    private string CurrentFingerprint() => string.Join('\n', _store.QualifiedIds);
}
