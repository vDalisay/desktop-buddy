using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Work;

public enum WorkActivityKind
{
    KeyboardPress,
    MouseClick,
}

public enum WorkCounterKind
{
    TotalActions,
    KeyboardPresses,
    MouseClicks,
}

public enum WorkMilestoneScope
{
    CurrentSession,
    Lifetime,
}

public enum WorkMilestoneRepeatPolicy
{
    OnceLifetime,
    RepeatPerSession,
}

public readonly record struct WorkCounterSnapshot(long KeyboardPresses, long MouseClicks)
{
    public long TotalActions => SaturatingAdd(KeyboardPresses, MouseClicks);

    public long Value(WorkCounterKind kind) => kind switch
    {
        WorkCounterKind.TotalActions => TotalActions,
        WorkCounterKind.KeyboardPresses => KeyboardPresses,
        WorkCounterKind.MouseClicks => MouseClicks,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public WorkCounterSnapshot Add(WorkActivityKind activity, long count = 1)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        return activity switch
        {
            WorkActivityKind.KeyboardPress => this with
            {
                KeyboardPresses = SaturatingAdd(KeyboardPresses, count),
            },
            WorkActivityKind.MouseClick => this with
            {
                MouseClicks = SaturatingAdd(MouseClicks, count),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, null),
        };
    }

    public static long SaturatingAdd(long left, long right)
    {
        if (left < 0 || right < 0)
            throw new ArgumentOutOfRangeException(nameof(right), "Work counters cannot be negative.");
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
}

public readonly record struct WorkMilestoneDefinition(
    string Id,
    WorkCounterKind CounterKind,
    WorkMilestoneScope Scope,
    long Threshold,
    long RewardMilliCredits,
    WorkMilestoneRepeatPolicy RepeatPolicy,
    bool Visible = true);

public readonly record struct WorkMilestoneEarned(
    string MilestoneId,
    long RewardMilliCredits,
    WorkMilestoneRepeatPolicy RepeatPolicy,
    string TransactionId);

/// <summary>Immutable, validated trusted milestone data.</summary>
public sealed class WorkMilestoneCatalogue
{
    private readonly WorkMilestoneDefinition[] _definitions;

    public WorkMilestoneCatalogue(IEnumerable<WorkMilestoneDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.OrderBy(definition => definition.Threshold).ThenBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkMilestoneDefinition definition in _definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Id))
                throw new ArgumentException("Work milestone IDs cannot be blank.", nameof(definitions));
            if (!ids.Add(definition.Id))
                throw new ArgumentException($"Duplicate Work milestone ID '{definition.Id}'.", nameof(definitions));
            if (definition.Threshold <= 0)
                throw new ArgumentException($"Work milestone '{definition.Id}' must have a positive threshold.", nameof(definitions));
            if (definition.RewardMilliCredits < 0)
                throw new ArgumentException($"Work milestone '{definition.Id}' cannot have a negative reward.", nameof(definitions));
            if (definition.RepeatPolicy == WorkMilestoneRepeatPolicy.RepeatPerSession && definition.Scope != WorkMilestoneScope.CurrentSession)
                throw new ArgumentException($"Work milestone '{definition.Id}' repeats per session but is not session-scoped.", nameof(definitions));
        }
    }

    public IReadOnlyList<WorkMilestoneDefinition> Definitions => _definitions;
}

public readonly record struct WorkProgressSnapshot(
    long Revision,
    WorkCounterSnapshot Lifetime,
    IReadOnlyList<string> ClaimedLifetimeMilestoneIds,
    bool FirstEntryGlassesGranted);

/// <summary>
/// Run-lifetime owner of durable Work Mode progress. Session counters intentionally live in
/// <see cref="WorkSessionState"/> so starting a new session cannot accidentally rewrite lifetime history.
/// </summary>
public sealed class WorkProgressState
{
    private readonly HashSet<string> _claimedLifetime = new(StringComparer.Ordinal);

    public WorkProgressState(
        WorkCounterSnapshot lifetime = default,
        IEnumerable<string>? claimedLifetimeMilestoneIds = null,
        bool firstEntryGlassesGranted = false,
        long revision = 0)
    {
        if (lifetime.KeyboardPresses < 0 || lifetime.MouseClicks < 0 || revision < 0)
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        Lifetime = lifetime;
        FirstEntryGlassesGranted = firstEntryGlassesGranted;
        Revision = revision;
        if (claimedLifetimeMilestoneIds is not null)
            foreach (string id in claimedLifetimeMilestoneIds.Where(id => !string.IsNullOrWhiteSpace(id)))
                _claimedLifetime.Add(id);
    }

    public long Revision { get; private set; }
    public WorkCounterSnapshot Lifetime { get; private set; }
    public bool FirstEntryGlassesGranted { get; private set; }
    public IReadOnlyCollection<string> ClaimedLifetimeMilestoneIds => _claimedLifetime;

    public event Action? Changed;

    public void Record(WorkActivityKind kind, long count = 1)
    {
        if (count <= 0)
            return;
        Lifetime = Lifetime.Add(kind, count);
        Touch();
    }

    public bool ClaimLifetimeMilestone(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Milestone ID is required.", nameof(id));
        if (!_claimedLifetime.Add(id))
            return false;
        Touch();
        return true;
    }

    public bool MarkFirstEntryGlassesGranted()
    {
        if (FirstEntryGlassesGranted)
            return false;
        FirstEntryGlassesGranted = true;
        Touch();
        return true;
    }

    public WorkProgressSnapshot Snapshot()
    {
        string[] ids = _claimedLifetime.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return new WorkProgressSnapshot(Revision, Lifetime, ids, FirstEntryGlassesGranted);
    }

    private void Touch()
    {
        Revision = Revision == long.MaxValue ? long.MaxValue : Revision + 1;
        Changed?.Invoke();
    }
}

public sealed class WorkSessionState
{
    private readonly HashSet<string> _earnedSession = new(StringComparer.Ordinal);
    private readonly List<WorkMilestoneEarned> _pending = [];

    public WorkSessionState(Guid? sessionId = null)
    {
        SessionId = sessionId ?? Guid.NewGuid();
    }

    public Guid SessionId { get; }
    public WorkCounterSnapshot Counters { get; private set; }
    public IReadOnlyList<WorkMilestoneEarned> PendingRewards => _pending;

    public void Record(WorkActivityKind kind, long count = 1) => Counters = Counters.Add(kind, count);

    public IReadOnlyList<WorkMilestoneEarned> Evaluate(
        WorkProgressState lifetime,
        WorkMilestoneCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(catalogue);
        var newlyEarned = new List<WorkMilestoneEarned>();
        foreach (WorkMilestoneDefinition definition in catalogue.Definitions)
        {
            long value = definition.Scope == WorkMilestoneScope.CurrentSession
                ? Counters.Value(definition.CounterKind)
                : lifetime.Lifetime.Value(definition.CounterKind);
            if (value < definition.Threshold)
                continue;

            bool claimable = definition.RepeatPolicy switch
            {
                WorkMilestoneRepeatPolicy.OnceLifetime => lifetime.ClaimLifetimeMilestone(definition.Id),
                WorkMilestoneRepeatPolicy.RepeatPerSession => _earnedSession.Add(definition.Id),
                _ => false,
            };
            if (!claimable)
                continue;

            string transactionId = definition.RepeatPolicy == WorkMilestoneRepeatPolicy.OnceLifetime
                ? $"work:{definition.Id}:lifetime"
                : $"work:{SessionId:D}:{definition.Id}";
            var earned = new WorkMilestoneEarned(
                definition.Id,
                definition.RewardMilliCredits,
                definition.RepeatPolicy,
                transactionId);
            _pending.Add(earned);
            newlyEarned.Add(earned);
        }
        return newlyEarned;
    }

    public IReadOnlyList<WorkMilestoneEarned> DrainPendingRewards()
    {
        WorkMilestoneEarned[] copy = _pending.ToArray();
        _pending.Clear();
        return copy;
    }
}
