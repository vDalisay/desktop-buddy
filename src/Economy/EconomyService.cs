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

    /// <param name="catalogue">
    /// The authoritative FR-013 catalogue. Purchases resolve their price and eligibility
    /// from it, so no caller can name a price.
    /// </param>
    public EconomyService(BuddyProgressState progress, ToolCatalogue catalogue)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    }

    /// <summary>The catalogue this run sells from (ARCHITECTURE §11).</summary>
    public ToolCatalogue Catalogue => _catalogue;

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
        double now,
        ImpactMoodEffect moodEffect = default)
    {
        long milli = _progress.AcceptDamage(
            contentId, pain, region, consciousness, now, moodEffect);
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

    /// <summary>
    /// Atomically buys one catalogue entry. The service — not the caller — resolves the
    /// entry and its authoritative price, and rejects unknown, starting, unfinished, and
    /// otherwise non-purchasable entries. Failed attempts never spend, unlock, or emit a
    /// balance event.
    /// </summary>
    public PurchaseResult Purchase(string contentId) => PurchaseFrom(contentId, _catalogue);

    /// <summary>
    /// Same single-ledger purchase boundary for a feature-owned immutable catalogue. This is
    /// internal deliberately: dynamic entitlement policies may author an unbounded next entry,
    /// but UI/gameplay callers still cannot supply a price directly.
    /// </summary>
    internal PurchaseResult PurchaseFrom(string contentId, ToolCatalogue authoritativeCatalogue)
    {
        ArgumentNullException.ThrowIfNull(authoritativeCatalogue);
        PurchaseResult result = _progress.Purchase(contentId, authoritativeCatalogue);
        if (result.Succeeded)
        {
            BalanceChanged?.Invoke(result.BalanceMilliCredits);
        }

        return result;
    }

    /// <summary>
    /// Re-announces the current balance. The one caller is a confirmed progress reset, which
    /// rewrites the balance in place rather than through a spend or a deposit; the HUD still
    /// has to hear about it.
    /// </summary>
    public void NotifyBalanceChanged() => BalanceChanged?.Invoke(_progress.BalanceMilliCredits);

    /// <summary>Returns a completed coalesced reward burst, or <c>null</c>.</summary>
    public RewardFeedback? PollFeedback(double now) => _progress.PollRewardFeedback(now);
}
