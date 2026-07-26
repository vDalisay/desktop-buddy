using System;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
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

    public EconomyService(BuddyProgressState progress)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    /// <summary>Raised after any balance change, carrying the new milli-credit balance.</summary>
    public event Action<long>? BalanceChanged;

    public long BalanceMilliCredits => _progress.BalanceMilliCredits;

    /// <summary>Whole-credit balance for the HUD (floored, RAGDOLL §7.4).</summary>
    public long BalanceCredits => _progress.BalanceCredits;

    /// <summary>
    /// Applies one accepted damage event — payout, harmful memory, statistics — and returns
    /// the milli-credits awarded. Care never routes here: care pays in mood, and its
    /// economic effect arrives through mood-scaled passive income (FR-008.8, FR-012.6).
    /// </summary>
    public long AcceptDamage(
        string contentId,
        float pain,
        PayoutRegion region,
        DamageConsciousness consciousness,
        double now)
    {
        long milli = _progress.AcceptDamage(contentId, pain, region, consciousness, now);
        if (milli != 0)
        {
            BalanceChanged?.Invoke(_progress.BalanceMilliCredits);
        }

        return milli;
    }

    /// <summary>
    /// Deposits accrued passive income. Produces no <c>+$N.N</c> burst — coalesced feedback
    /// stays reserved for accepted damage rewards (RAGDOLL §7.4).
    /// </summary>
    public void DepositPassive(long milliCredits)
    {
        if (milliCredits <= 0)
        {
            return;
        }

        _progress.Deposit(milliCredits);
        BalanceChanged?.Invoke(_progress.BalanceMilliCredits);
    }

    /// <summary>Records a permanent unlock. Returns <c>false</c> when already unlocked.</summary>
    public bool Unlock(string contentId) => _progress.Unlock(contentId);

    public bool IsUnlocked(string contentId) => _progress.IsToolUnlocked(contentId);

    /// <summary>Returns a completed coalesced reward burst, or <c>null</c>.</summary>
    public RewardFeedback? PollFeedback(double now) => _progress.PollRewardFeedback(now);
}
