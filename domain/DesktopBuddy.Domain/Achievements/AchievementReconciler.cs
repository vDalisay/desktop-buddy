using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Achievements;

/// <summary>
/// Platform port for monotonic achievement publishing. Implementations may be Steam, a test fake,
/// or another future platform; the domain never depends on a platform SDK.
/// </summary>
public interface IAchievementRemote
{
    bool IsAvailable { get; }
    bool TrySetAchievement(string apiName);
    bool TryFlush();
}

/// <summary>
/// Reconciles durable local qualification to a remote achievement service as desired state.
/// Successful state is remembered only for this process; each launch safely replays the small
/// qualified set once, while repeated ticks after a successful flush are no-ops.
/// </summary>
public sealed class AchievementReconciler
{
    private readonly AchievementProgressStore _store;
    private readonly IAchievementRemote _remote;
    private string? _lastSuccessfulFingerprint;

    public AchievementReconciler(AchievementProgressStore store, IAchievementRemote remote)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _remote = remote ?? throw new ArgumentNullException(nameof(remote));
    }

    public bool IsAvailable => _remote.IsAvailable;

    /// <summary>
    /// Attempts to mirror the complete locally-qualified set. The method is idempotent after a
    /// successful flush. If one Set call or the flush fails, the fingerprint remains dirty so a
    /// later retry replays the complete desired set.
    /// </summary>
    public bool TrySynchronize()
    {
        if (!_remote.IsAvailable)
            return false;

        var qualified = new List<AchievementDefinition>(AchievementCatalog.Baseline.Count);
        foreach (AchievementDefinition definition in AchievementCatalog.Baseline)
        {
            if (_store.IsQualified(definition.Id))
                qualified.Add(definition);
        }

        string fingerprint = Fingerprint(qualified);
        if (string.Equals(fingerprint, _lastSuccessfulFingerprint, StringComparison.Ordinal))
            return true;

        if (qualified.Count == 0)
        {
            _lastSuccessfulFingerprint = fingerprint;
            return true;
        }

        bool allSet = true;
        bool anySet = false;
        foreach (AchievementDefinition definition in qualified)
        {
            bool accepted = _remote.TrySetAchievement(definition.SteamApiName);
            allSet &= accepted;
            anySet |= accepted;
        }

        // Preserve any accepted subset even when another definition is temporarily missing or
        // unavailable. The batch stays dirty until every Set call and the flush succeed together.
        bool flushed = anySet && _remote.TryFlush();
        if (!allSet || !flushed)
            return false;

        _lastSuccessfulFingerprint = fingerprint;
        return true;
    }

    private static string Fingerprint(IReadOnlyList<AchievementDefinition> qualified)
    {
        if (qualified.Count == 0)
            return string.Empty;

        var ids = new string[qualified.Count];
        for (int index = 0; index < qualified.Count; index++)
            ids[index] = qualified[index].Id;
        return string.Join('\n', ids);
    }
}
