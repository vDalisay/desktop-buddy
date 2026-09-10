using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Economy;

/// <summary>
/// The sole runtime mutator of currency and unlocks (ARCHITECTURE §11). During the staged
/// multi-Buddy migration it can be backed either by the legacy one-Buddy aggregate or by the
/// account-global <see cref="PlayerProgressState"/>. Damage against split state must additionally
/// name the target <see cref="BuddyProgressCoordinator"/>, so emotional state can never leak from
/// one Buddy into another while the wallet remains shared.
///
/// Deliberately not a <c>Node</c>: it owns no scene lifetime and must outlive any node that
/// uses it. The composition root creates it next to the progress state and injects both.
/// </summary>
public sealed class EconomyService
{
    private readonly BuddyProgressState? _legacyProgress;
    private readonly PlayerProgressState? _playerProgress;
    private readonly ToolCatalogue _catalogue;

    /// <param name="catalogue">
    /// The authoritative FR-013 catalogue. Purchases resolve their price and eligibility
    /// from it, so no caller can name a price.
    /// </param>
    public EconomyService(BuddyProgressState progress, ToolCatalogue catalogue)
    {
        _legacyProgress = progress ?? throw new ArgumentNullException(nameof(progress));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    }

    /// <summary>
    /// Account-global constructor used by the multi-Buddy/Scene runtime. No Buddy-owned semantic
    /// state is accepted here: callers must supply the struck Buddy coordinator to damage methods.
    /// </summary>
    public EconomyService(PlayerProgressState progress, ToolCatalogue catalogue)
    {
        _playerProgress = progress ?? throw new ArgumentNullException(nameof(progress));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    }

    /// <summary>The catalogue this run sells from (ARCHITECTURE §11).</summary>
    public ToolCatalogue Catalogue => _catalogue;

    /// <summary>Raised after any balance change, carrying the new milli-credit balance.</summary>
    public event Action<long>? BalanceChanged;

    public long BalanceMilliCredits =>
        _playerProgress?.BalanceMilliCredits ?? RequireLegacyProgress().BalanceMilliCredits;

    /// <summary>Whole-credit balance for the HUD (floored, RAGDOLL §7.4).</summary>
    public long BalanceCredits =>
        _playerProgress?.BalanceCredits ?? RequireLegacyProgress().BalanceCredits;

    /// <summary>
    /// Applies one accepted damage event to the legacy aggregate — payout, harmful memory and
    /// statistics together. Kept unchanged for the Initial Demo compatibility path.
    /// </summary>
    public long AcceptDamage(
        string contentId,
        float pain,
        PayoutRegion region,
        DamageConsciousness consciousness,
        double now,
        ImpactMoodEffect moodEffect = default)
    {
        BuddyProgressState progress = RequireLegacyProgress();
        long milli = progress.AcceptDamage(
            contentId, pain, region, consciousness, now, moodEffect);
        if (milli != 0)
            BalanceChanged?.Invoke(progress.BalanceMilliCredits);
        return milli;
    }

    /// <summary>
    /// Applies one accepted damage event in split multi-Buddy state. The service verifies that the
    /// coordinator belongs to this exact player account, then lets the coordinator mutate the one
    /// shared reward/statistics ledger and only the named Buddy's emotional state.
    /// </summary>
    public long AcceptDamage(
        BuddyProgressCoordinator target,
        string contentId,
        float pain,
        PayoutRegion region,
        DamageConsciousness consciousness,
        double now,
        ImpactMoodEffect moodEffect = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        PlayerProgressState player = RequirePlayerProgress();
        if (!ReferenceEquals(target.Player, player))
        {
            throw new InvalidOperationException(
                "A split damage target must belong to the same PlayerProgressState as the economy service.");
        }

        SplitDamageProgressResult result = target.AcceptDamage(
            contentId,
            pain,
            region,
            consciousness,
            now,
            moodEffect);
        if (result.MilliCredits != 0)
            BalanceChanged?.Invoke(player.BalanceMilliCredits);
        return result.MilliCredits;
    }

    /// <summary>
    /// Deposits accrued passive income. Produces no <c>+$N.N</c> burst — coalesced feedback
    /// stays reserved for accepted damage rewards (RAGDOLL §7.4).
    /// </summary>
    public void DepositPassive(long milliCredits)
    {
        if (milliCredits <= 0)
            return;

        if (_playerProgress is not null)
            _playerProgress.Deposit(milliCredits);
        else
            RequireLegacyProgress().Deposit(milliCredits);
        BalanceChanged?.Invoke(BalanceMilliCredits);
    }

    /// <summary>Records a permanent unlock. Returns <c>false</c> when already unlocked.</summary>
    public bool Unlock(string contentId) =>
        _playerProgress?.Unlock(contentId) ?? RequireLegacyProgress().Unlock(contentId);

    public bool IsUnlocked(string contentId) =>
        _playerProgress?.IsUnlocked(contentId) ?? RequireLegacyProgress().IsToolUnlocked(contentId);

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
        PurchaseResult result = _playerProgress is not null
            ? _playerProgress.Purchase(contentId, authoritativeCatalogue)
            : RequireLegacyProgress().Purchase(contentId, authoritativeCatalogue);
        if (result.Succeeded)
            BalanceChanged?.Invoke(result.BalanceMilliCredits);
        return result;
    }

    /// <summary>
    /// Re-announces the current balance. The one caller is a confirmed progress reset, which
    /// rewrites the balance in place rather than through a spend or a deposit; the HUD still
    /// has to hear about it.
    /// </summary>
    public void NotifyBalanceChanged() => BalanceChanged?.Invoke(BalanceMilliCredits);

    /// <summary>Returns a completed coalesced reward burst, or <c>null</c>.</summary>
    public RewardFeedback? PollFeedback(double now) =>
        _playerProgress is not null
            ? _playerProgress.PollRewardFeedback(now)
            : RequireLegacyProgress().PollRewardFeedback(now);

    /// <summary>Composition guard used while binding actor-local split progress.</summary>
    internal bool IsBackedBy(PlayerProgressState progress) =>
        _playerProgress is not null && ReferenceEquals(_playerProgress, progress);

    /// <summary>Compatibility composition guard for the existing aggregate path.</summary>
    internal bool IsBackedBy(BuddyProgressState progress) =>
        _legacyProgress is not null && ReferenceEquals(_legacyProgress, progress);

    private BuddyProgressState RequireLegacyProgress() =>
        _legacyProgress ?? throw new InvalidOperationException(
            "This EconomyService is backed by PlayerProgressState; use the split-state damage overload.");

    private PlayerProgressState RequirePlayerProgress() =>
        _playerProgress ?? throw new InvalidOperationException(
            "This EconomyService is backed by the legacy BuddyProgressState.");
}
