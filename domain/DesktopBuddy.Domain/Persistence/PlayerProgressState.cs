using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>
/// Account-global persistent state for the multi-Buddy architecture. This is deliberately engine
/// free and contains no mood, hunger, traits, harmful memory, fun/novelty or live actor state.
///
/// The existing <see cref="BuddyProgressState"/> remains the compatibility owner for the Initial
/// Demo while runtime call sites are migrated. New multi-Buddy code must depend on this account
/// owner rather than cloning the old aggregate once per Buddy.
/// </summary>
public sealed class PlayerProgressState
{
    private readonly double _cashPerPain;
    private RewardLedger _ledger;
    private ToolSelection _tools;
    private readonly HashSet<string> _unlockedContent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _toolUses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _toolPainMilli = new(StringComparer.Ordinal);

    private long _scoredImpacts;
    private long _knockouts;
    private long _careAwards;
    private long _trustResets;
    private long _earnedMilliCredits;
    private long _successfulCatches;
    private long _totalPainMilli;
    private long _bestOneSecondMilliCredits;
    private long _bestThreeSecondMilliCredits;
    private long _bestTenSecondMilliCredits;
    private float _highestMood;
    private float _lowestMood;

    public PlayerProgressState(double cashPerPain, in PlayerProgressSnapshot snapshot)
    {
        if (snapshot.Revision < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Player revision cannot be negative.");
        if (string.IsNullOrWhiteSpace(snapshot.SelectedToolId))
            throw new ArgumentException("Selected tool ID is required.", nameof(snapshot));
        ArgumentNullException.ThrowIfNull(snapshot.UnlockedContentIds);

        _cashPerPain = cashPerPain;
        _ledger = new RewardLedger(cashPerPain, snapshot.BalanceMilliCredits);
        _tools = new ToolSelection();
        Revision = snapshot.Revision;
        Times = snapshot.Times;
        Extensions = CopyExtensions(snapshot.Extensions);

        foreach (string id in snapshot.UnlockedContentIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                _unlockedContent.Add(id);
        }
        _unlockedContent.Add(ContentIds.ToolGrab);

        if (ContentIds.TryParseTool(snapshot.SelectedToolId, out ToolId selected) &&
            _unlockedContent.Contains(ContentIds.ForTool(selected)))
        {
            _tools.Select(selected);
        }

        RestoreStatistics(snapshot.Statistics);
    }

    public long Revision { get; private set; }
    public double CashPerPain => _cashPerPain;
    public long BalanceMilliCredits => _ledger.BalanceMilliCredits;
    public long BalanceCredits => _ledger.BalanceCredits;
    public ToolId SelectedTool => _tools.Selected;
    public string SelectedToolId => ContentIds.ForTool(_tools.Selected);
    public CumulativeTimes Times { get; private set; }
    public ProgressExtensionData? Extensions { get; private set; }

    public bool IsUnlocked(string contentId) => _unlockedContent.Contains(contentId);

    public ProgressStatistics Statistics => new(
        _scoredImpacts,
        _knockouts,
        _careAwards,
        _trustResets,
        _earnedMilliCredits,
        _successfulCatches,
        _totalPainMilli,
        _bestOneSecondMilliCredits,
        _bestThreeSecondMilliCredits,
        _bestTenSecondMilliCredits,
        _highestMood,
        _lowestMood,
        new Dictionary<string, long>(_toolUses, StringComparer.Ordinal),
        new Dictionary<string, long>(_toolPainMilli, StringComparer.Ordinal));

    public PlayerProgressSnapshot Snapshot()
    {
        string[] unlocks = [.. _unlockedContent];
        Array.Sort(unlocks, StringComparer.Ordinal);
        return new PlayerProgressSnapshot(
            Revision,
            _ledger.BalanceMilliCredits,
            SelectedToolId,
            unlocks,
            Statistics,
            Times,
            CopyExtensions(Extensions));
    }

    public bool SelectTool(ToolId tool)
    {
        string contentId = ContentIds.ForTool(tool);
        if (_tools.Selected == tool || !_unlockedContent.Contains(contentId))
            return false;

        _tools.Select(tool);
        if (Extensions?.UnknownSelectedToolId is not null)
            Extensions = Extensions with { UnknownSelectedToolId = null };
        Touch();
        return true;
    }

    public bool Unlock(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            throw new ArgumentException("An unlock requires a stable content ID.", nameof(contentId));
        if (!_unlockedContent.Add(contentId))
            return false;
        Touch();
        return true;
    }

    public PurchaseResult Purchase(string contentId, ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        catalogue.TryGet(contentId, out CatalogueEntry entry);
        long price = entry.PriceMilliCredits;
        PurchaseStatus status = CataloguePolicy.EvaluatePurchase(
            catalogue,
            contentId,
            _unlockedContent.Contains(contentId ?? string.Empty),
            _ledger.BalanceMilliCredits);
        if (status != PurchaseStatus.Purchased)
            return new PurchaseResult(status, contentId ?? string.Empty, price, _ledger.BalanceMilliCredits);

        if (!_ledger.TrySpend(price))
        {
            return new PurchaseResult(
                PurchaseStatus.InsufficientFunds,
                contentId!,
                price,
                _ledger.BalanceMilliCredits);
        }

        _unlockedContent.Add(contentId!);
        Touch();
        return new PurchaseResult(PurchaseStatus.Purchased, contentId!, price, _ledger.BalanceMilliCredits);
    }

    /// <summary>
    /// Applies only the account side of an accepted damage event: payout and lifetime statistics.
    /// Buddy mood/harmful-memory mutation belongs to <see cref="BuddyIdentityState.ApplyImpactMood"/>.
    /// This split prevents damage to Buddy A from changing Buddy B while preserving one wallet.
    /// </summary>
    public long AcceptDamageReward(
        string contentId,
        float pain,
        PayoutRegion region,
        DamageConsciousness consciousness,
        double now)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            throw new ArgumentException("Damage attribution requires a stable content ID.", nameof(contentId));

        long milli = _ledger.Accept(pain, region, consciousness, now);
        _scoredImpacts++;
        _earnedMilliCredits += milli;
        long painMilli = (long)Math.Round(pain * 1000.0f, MidpointRounding.AwayFromZero);
        _totalPainMilli += painMilli;
        _toolPainMilli.TryGetValue(contentId, out long priorPain);
        _toolPainMilli[contentId] = priorPain + painMilli;
        Touch();
        return milli;
    }

    public void Deposit(long milliCredits)
    {
        if (milliCredits <= 0)
            return;
        _ledger.Deposit(milliCredits);
        _earnedMilliCredits += milliCredits;
        Touch();
    }

    public void RecordTrustReset()
    {
        _trustResets++;
        Touch();
    }

    public void RecordCareAward()
    {
        _careAwards++;
        Touch();
    }

    public void RecordKnockout()
    {
        _knockouts++;
        Touch();
    }

    public void RecordSuccessfulCatch()
    {
        _successfulCatches++;
        Touch();
    }

    public void RecordContentUse(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            throw new ArgumentException("A content use requires a stable ID.", nameof(contentId));
        _toolUses.TryGetValue(contentId, out long prior);
        _toolUses[contentId] = prior + 1;
        Touch();
    }

    public void ObserveBuddyMood(float mood)
    {
        if (!float.IsFinite(mood))
            throw new ArgumentOutOfRangeException(nameof(mood));
        float clamped = Math.Clamp(mood, -100.0f, 100.0f);
        float highest = Math.Max(_highestMood, clamped);
        float lowest = Math.Min(_lowestMood, clamped);
        if (highest == _highestMood && lowest == _lowestMood)
            return;
        _highestMood = highest;
        _lowestMood = lowest;
        Touch();
    }

    public void AccrueTime(double runSeconds, double activeSeconds, double hiddenSeconds)
    {
        if (runSeconds <= 0.0 && activeSeconds <= 0.0 && hiddenSeconds <= 0.0)
            return;
        Times = new CumulativeTimes(
            Times.RunSeconds + Math.Max(0.0, runSeconds),
            Times.ActiveSeconds + Math.Max(0.0, activeSeconds),
            Times.HiddenSeconds + Math.Max(0.0, hiddenSeconds));
        Touch();
    }

    public bool SetExtensionValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("An extension value requires a stable key.", nameof(key));
        ArgumentNullException.ThrowIfNull(value);

        var values = Extensions?.Values is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(Extensions.Values, StringComparer.Ordinal);
        if (values.TryGetValue(key, out string? existing) && string.Equals(existing, value, StringComparison.Ordinal))
            return false;

        values[key] = value;
        Extensions = new ProgressExtensionData(
            Extensions?.UnknownSelectedToolId,
            Extensions?.UnknownContentIds,
            values);
        Touch();
        return true;
    }

    public RewardFeedback? PollRewardFeedback(double now) => _ledger.PollFeedback(now);

    private void RestoreStatistics(in ProgressStatistics statistics)
    {
        _scoredImpacts = statistics.ScoredImpacts;
        _knockouts = statistics.Knockouts;
        _careAwards = statistics.CareAwards;
        _trustResets = statistics.TrustResets;
        _earnedMilliCredits = statistics.EarnedMilliCredits;
        _successfulCatches = statistics.SuccessfulCatches;
        _totalPainMilli = statistics.TotalPainMilli;
        _bestOneSecondMilliCredits = statistics.BestOneSecondMilliCredits;
        _bestThreeSecondMilliCredits = statistics.BestThreeSecondMilliCredits;
        _bestTenSecondMilliCredits = statistics.BestTenSecondMilliCredits;
        _highestMood = statistics.HighestMood;
        _lowestMood = statistics.LowestMood;
        _toolUses.Clear();
        _toolPainMilli.Clear();
        if (statistics.ToolUses is not null)
        {
            foreach ((string id, long count) in statistics.ToolUses)
                _toolUses[id] = count;
        }
        if (statistics.ToolPainMilli is not null)
        {
            foreach ((string id, long pain) in statistics.ToolPainMilli)
                _toolPainMilli[id] = pain;
        }
    }

    private static ProgressExtensionData? CopyExtensions(ProgressExtensionData? source)
    {
        if (source is null)
            return null;
        return new ProgressExtensionData(
            source.UnknownSelectedToolId,
            source.UnknownContentIds is null ? null : [.. source.UnknownContentIds],
            source.Values is null
                ? null
                : new Dictionary<string, string>(source.Values, StringComparer.Ordinal));
    }

    private void Touch() => Revision++;
}
