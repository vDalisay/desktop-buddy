using System;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Mood;

namespace DesktopBuddy.Domain.Persistence;

public readonly record struct SplitDamageProgressResult(
    long MilliCredits,
    bool TrustReset);

/// <summary>
/// Engine-free semantic router between one account-global <see cref="PlayerProgressState"/> and one
/// specific <see cref="BuddyIdentityState"/>. Runtime damage/care workers should depend on this
/// focused pair rather than regaining access to a monolithic per-Buddy wallet.
///
/// The ordering intentionally mirrors the legacy <see cref="BuddyProgressState"/> contract:
/// reward/statistics are accepted once by the shared player ledger, emotional/harmful-memory state
/// is applied only to the struck Buddy, then account-wide trust/care/mood statistics are observed.
/// </summary>
public sealed class BuddyProgressCoordinator
{
    public BuddyProgressCoordinator(PlayerProgressState player, BuddyIdentityState buddy)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Buddy = buddy ?? throw new ArgumentNullException(nameof(buddy));
    }

    public PlayerProgressState Player { get; }
    public BuddyIdentityState Buddy { get; }

    public SplitDamageProgressResult AcceptDamage(
        string contentId,
        float pain,
        PayoutRegion region,
        DamageConsciousness consciousness,
        double now,
        ImpactMoodEffect moodEffect = default)
    {
        long milliCredits = Player.AcceptDamageReward(
            contentId,
            pain,
            region,
            consciousness,
            now);
        bool trustReset = Buddy.ApplyImpactMood(contentId, pain, moodEffect);
        if (trustReset)
            Player.RecordTrustReset();
        Player.ObserveBuddyMood(Buddy.Mood);
        return new SplitDamageProgressResult(milliCredits, trustReset);
    }

    /// <summary>
    /// Applies a care mood delta to this Buddy and mirrors the legacy account statistics semantics:
    /// positive care counts as a care award; an upward trust-threshold crossing counts once as a
    /// trust reset. Negative/zero deltas never increment the care-award counter.
    /// </summary>
    public bool ApplyCareMood(float delta)
    {
        bool trustReset = Buddy.ApplyCareMood(delta);
        if (delta > 0.0f)
            Player.RecordCareAward();
        if (trustReset)
            Player.RecordTrustReset();
        Player.ObserveBuddyMood(Buddy.Mood);
        return trustReset;
    }

    public void RecordKnockout() => Player.RecordKnockout();

    public void RecordSuccessfulCatch() => Player.RecordSuccessfulCatch();
}
