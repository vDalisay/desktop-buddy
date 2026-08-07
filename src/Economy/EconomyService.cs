using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Economy;

/// <summary>
/// The sole runtime mutator of currency and unlocks (ARCHITECTURE §11). Damage rewards and
/// passive income both flow through here into the one per-run
/// <see cref="BuddyProgressState"/>, so there is exactly one place that can change the
/// balance and exactly one event the HUD subscribes to.
///
/// Deliberately not a <c>Node</c>: it owns no scene lifetime and must outlive any node that
/// uses it. The composition root creates it next to the progress state and injects both.
/// </summary>
public sealed class EconomyService
{
    private readonly BuddyProgressState _progress;
    private readonly ToolCatalogue _catalogue;

    public EconomyService(BuddyProgressState progress, ToolCatalogue catalogue)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    }

    public ToolCatalogue Catalogue => _catalogue;
    public event Action<long>? BalanceChanged;
    public long BalanceMilliCredits => _progress.BalanceMilliCredits;
    public long BalanceCredits => _progress.BalanceCredits;

    public long AcceptDamage(
        string contentId,
        float pain,
        PayoutRegion region,
        DamageConsciousness consciousness,
        double now,
        ImpactMoodEffect moodEffect = default)
    {
        long milli = _progress.AcceptDamage(
            contentId, pain, region, consciousness, now, moodEffect);
        if (milli != 0)
            BalanceChanged?.Invoke(_progress.BalanceMilliCredits);
        return milli;
    }

    public void DepositPassive(long milliCredits)
    {
        if (milliCredits <= 0)
            return;
        _progress.Deposit(milliCredits);
        BalanceChanged?.Invoke(_progress.BalanceMilliCredits);
    }

    /// <summary>
    /// Settles a Work milestone reward through the same authoritative balance owner as every
    /// other payout. The caller owns milestone idempotency; this method owns the balance.
    /// </summary>
    public void DepositWorkMilestone(long milliCredits)
    {
        if (milliCredits <= 0)
            return;
        _progress.Deposit(milliCredits);
        BalanceChanged?.Invoke(_progress.BalanceMilliCredits);
    }

    public bool Unlock(string contentId) => _progress.Unlock(contentId);
    public bool IsUnlocked(string contentId) => _progress.IsToolUnlocked(contentId);

    public PurchaseResult Purchase(string contentId)
    {
        PurchaseResult result = _progress.Purchase(contentId, _catalogue);
        if (result.Succeeded)
            BalanceChanged?.Invoke(result.BalanceMilliCredits);
        return result;
    }

    public void NotifyBalanceChanged() => BalanceChanged?.Invoke(_progress.BalanceMilliCredits);
    public RewardFeedback? PollFeedback(double now) => _progress.PollRewardFeedback(now);
}
