using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>Lifetime counters that persist across runs (FR-015.1 statistics).</summary>
public readonly record struct ProgressStatistics(
    long ScoredImpacts,
    long Knockouts,
    long CareAwards,
    long TrustResets,
    long EarnedMilliCredits);

/// <summary>Cumulative wall-time accounting, in seconds (FR-015.1, FR-016.8).</summary>
public readonly record struct CumulativeTimes(
    double RunSeconds,
    double ActiveSeconds,
    double HiddenSeconds);

/// <summary>An immutable read of everything that persists, for snapshotting into a save DTO.</summary>
public readonly record struct ProgressSnapshot(
    long Revision,
    long BalanceMilliCredits,
    string SelectedToolId,
    IReadOnlyList<string> UnlockedToolIds,
    float Mood,
    IReadOnlyList<string> HarmfulContentIds,
    BuddyTraits Traits,
    ProgressStatistics Statistics,
    CumulativeTimes Times);

/// <summary>Why the semantic state changed, for low-frequency subscribers.</summary>
public enum ProgressChange
{
    ToolSelected,
    ToolUnlocked,
    BalanceChanged,
    TrustReset,
    CareApplied,
}

/// <summary>
/// The single per-run owner of persistent semantic state (ARCHITECTURE §12, FR-015.1).
/// One instance is composed per run by the composition root and injected into the workers
/// that need it; nothing that owns persistent state may be constructed inside a Godot node,
/// or the state dies with the node.
///
/// It holds the <see cref="MoodModel"/> (mood + harmful history), the
/// <see cref="RewardLedger"/> (balance), tool selection, unlocks, traits, statistics, and
/// cumulative times. It holds <b>no</b> live simulation state: pose, velocities, loose
/// objects, pain window, knockout, activity, grab, and cooldowns stay transient and are
/// never persisted (FR-015.2).
///
/// <para>
/// Currency and unlocks are mutated only through the runtime economy service
/// (ARCHITECTURE §11); the <c>Accept*</c>/<c>Deposit</c>/<c>Unlock</c> members here are its
/// implementation, not a second entry point for gameplay code.
/// </para>
///
/// <para>
/// <see cref="Revision"/> increments on every persistent mutation and is what the save
/// coordinator's dirty tracking compares. Mood drift is a persistent mutation, so a
/// running game bumps the revision continuously — that is why <see cref="Changed"/> fires
/// only for discrete semantic events and never for drift.
/// </para>
/// </summary>
public sealed class BuddyProgressState
{
    private readonly MoodModel _mood;
    private readonly RewardLedger _ledger;
    private readonly ToolSelection _tools = new();
    private readonly HashSet<string> _unlockedTools = new(StringComparer.Ordinal);

    private long _scoredImpacts;
    private long _knockouts;
    private long _careAwards;
    private long _trustResets;
    private long _earnedMilliCredits;

    /// <param name="cashPerPain">Approved shared economy coefficient (FR-012.5).</param>
    /// <param name="initialMood">Persisted mood, or <c>0</c> for a new save.</param>
    /// <param name="harmfulContentIds">Persisted harmful history, or <c>null</c> for a new save.</param>
    /// <param name="unlockedToolIds">
    /// Persisted unlocks. <c>null</c> seeds the FR-013.1 new-save set: Grab, Pet, Tickle,
    /// and Boxing Glove available immediately.
    /// </param>
    public BuddyProgressState(
        double cashPerPain,
        float initialMood = 0.0f,
        IEnumerable<string>? harmfulContentIds = null,
        IEnumerable<string>? unlockedToolIds = null,
        BuddyTraits? traits = null,
        ProgressStatistics statistics = default,
        CumulativeTimes times = default,
        long revision = 0)
    {
        _mood = new MoodModel(initialMood, harmfulContentIds);
        _ledger = new RewardLedger(cashPerPain);
        Traits = traits ?? BuddyTraits.Default;
        Revision = revision;

        if (unlockedToolIds is null)
        {
            // FR-013.1: a new save has all four launch-subset tools available.
            _unlockedTools.Add(ContentIds.ToolGrab);
            _unlockedTools.Add(ContentIds.ToolPet);
            _unlockedTools.Add(ContentIds.ToolTickle);
            _unlockedTools.Add(ContentIds.ToolBoxingGlove);
        }
        else
        {
            foreach (string id in unlockedToolIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _unlockedTools.Add(id);
                }
            }

            // Grab is never purchasable and never absent (RAGDOLL §9).
            _unlockedTools.Add(ContentIds.ToolGrab);
        }

        _scoredImpacts = statistics.ScoredImpacts;
        _knockouts = statistics.Knockouts;
        _careAwards = statistics.CareAwards;
        _trustResets = statistics.TrustResets;
        _earnedMilliCredits = statistics.EarnedMilliCredits;
        Times = times;
    }

    /// <summary>Discrete semantic changes only — never per-tick drift (see class remarks).</summary>
    public event Action<ProgressChange>? Changed;

    public long Revision { get; private set; }

    public float Mood => _mood.Mood;
    public MoodBand MoodBand => _mood.Band;
    public IReadOnlyCollection<string> HarmfulContentIds => _mood.HarmfulTools;
    public bool IsContentHarmful(string contentId) => _mood.IsToolHarmful(contentId);

    public long BalanceMilliCredits => _ledger.BalanceMilliCredits;
    public long BalanceCredits => _ledger.BalanceCredits;

    public ToolId SelectedTool => _tools.Selected;
    public string SelectedToolId => ContentIds.ForTool(_tools.Selected);
    public bool IsToolUnlocked(string contentId) => _unlockedTools.Contains(contentId);

    public BuddyTraits Traits { get; private set; }
    public CumulativeTimes Times { get; private set; }

    public ProgressStatistics Statistics => new(
        _scoredImpacts,
        _knockouts,
        _careAwards,
        _trustResets,
        _earnedMilliCredits);

    /// <summary>An immutable read of all persistent state for the save writer.</summary>
    public ProgressSnapshot Snapshot()
    {
        var harmful = new string[_mood.HarmfulTools.Count];
        int index = 0;
        foreach (string id in _mood.HarmfulTools)
        {
            harmful[index++] = id;
        }

        var unlocks = new string[_unlockedTools.Count];
        index = 0;
        foreach (string id in _unlockedTools)
        {
            unlocks[index++] = id;
        }

        Array.Sort(harmful, StringComparer.Ordinal);
        Array.Sort(unlocks, StringComparer.Ordinal);

        return new ProgressSnapshot(
            Revision,
            _ledger.BalanceMilliCredits,
            SelectedToolId,
            unlocks,
            _mood.Mood,
            harmful,
            Traits,
            Statistics,
            Times);
    }

    /// <summary>Explicit tool pick. Returns <c>false</c> when the selection is unchanged.</summary>
    public bool SelectTool(ToolId tool)
    {
        if (_tools.Selected == tool)
        {
            return false;
        }

        _tools.Select(tool);
        Touch();
        Changed?.Invoke(ProgressChange.ToolSelected);
        return true;
    }

    /// <summary>Records a permanent unlock. Returns <c>false</c> when already unlocked.</summary>
    public bool Unlock(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            throw new ArgumentException("An unlock requires a stable content ID.", nameof(contentId));
        }

        if (!_unlockedTools.Add(contentId))
        {
            return false;
        }

        Touch();
        Changed?.Invoke(ProgressChange.ToolUnlocked);
        return true;
    }

    /// <summary>
    /// Applies one accepted damage event: payout, harmful memory, and statistics in spec
    /// order. Returns the milli-credits awarded.
    /// </summary>
    public long AcceptDamage(
        string contentId,
        float pain,
        PayoutRegion region,
        DamageConsciousness consciousness,
        double now)
    {
        long milli = _ledger.Accept(pain, region, consciousness, now);
        _mood.RegisterHarm(contentId, pain);
        _scoredImpacts++;
        _earnedMilliCredits += milli;
        Touch();
        if (milli != 0)
        {
            Changed?.Invoke(ProgressChange.BalanceChanged);
        }

        return milli;
    }

    /// <summary>Deposits already-earned credits (passive income). Emits no <c>+$</c> burst.</summary>
    public void Deposit(long milliCredits)
    {
        if (milliCredits <= 0)
        {
            return;
        }

        _ledger.Deposit(milliCredits);
        _earnedMilliCredits += milliCredits;
        Touch();
        Changed?.Invoke(ProgressChange.BalanceChanged);
    }

    /// <summary>Applies a care mood delta and reports whether it fired the trust reset.</summary>
    public bool ApplyCareMood(float delta)
    {
        bool reset = _mood.ApplyMoodDelta(delta);
        if (delta > 0.0f)
        {
            _careAwards++;
        }

        if (reset)
        {
            _trustResets++;
        }

        Touch();
        Changed?.Invoke(reset ? ProgressChange.TrustReset : ProgressChange.CareApplied);
        return reset;
    }

    /// <summary>Records that a knockout began (statistics only; the window stays transient).</summary>
    public void RecordKnockout()
    {
        _knockouts++;
        Touch();
    }

    /// <summary>
    /// Drifts mood toward neutral over a monotonic elapsed span. Fires no
    /// <see cref="Changed"/> event: at runtime this is called continuously, and the save
    /// coordinator already coalesces on <see cref="Revision"/>.
    /// </summary>
    public void DriftMood(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0)
        {
            return;
        }

        _mood.Drift(elapsedSeconds);
        Touch();
    }

    /// <summary>Accrues cumulative time. Foreground and hidden spans are counted separately.</summary>
    public void AccrueTime(double runSeconds, double activeSeconds, double hiddenSeconds)
    {
        if (runSeconds <= 0.0 && activeSeconds <= 0.0 && hiddenSeconds <= 0.0)
        {
            return;
        }

        Times = new CumulativeTimes(
            Times.RunSeconds + Math.Max(0.0, runSeconds),
            Times.ActiveSeconds + Math.Max(0.0, activeSeconds),
            Times.HiddenSeconds + Math.Max(0.0, hiddenSeconds));
        Touch();
    }

    /// <summary>Assigns freshly sampled traits. Only new-save creation may call this.</summary>
    public void SeedTraits(BuddyTraits traits)
    {
        Traits = traits;
        Touch();
    }

    /// <summary>Returns a completed coalesced reward burst, or <c>null</c>.</summary>
    public RewardFeedback? PollRewardFeedback(double now) => _ledger.PollFeedback(now);

    private void Touch() => Revision++;
}
