using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Damage;

namespace DesktopBuddy.Domain.Economy;

/// <summary>Approved payout multipliers (RAGDOLL §7.4). No hidden per-tool multipliers.</summary>
public static class PayoutMultipliers
{
    public const float Conscious = 1.0f;
    public const float Unconscious = 0.5f;

    public static float Region(PayoutRegion region) => region switch
    {
        PayoutRegion.Head => 1.2f,
        PayoutRegion.Torso => 1.0f,
        PayoutRegion.Arms => 0.8f,
        PayoutRegion.Legs => 0.8f,
        _ => throw new ArgumentOutOfRangeException(nameof(region), region, "Unknown payout region."),
    };
}

/// <summary>A coalesced reward feedback burst (raw pain stays hidden, RAGDOLL §7.4).</summary>
public readonly record struct RewardFeedback(long MilliCredits, double TimeSeconds);

/// <summary>
/// Applies the approved reward formula and holds the money balance
/// (RAGDOLL §7.4): <c>money = pain × regionMultiplier × unconsciousMultiplier ×
/// cashPerPain</c>. The balance is signed 64-bit <b>milli-credits</b> (1000 per displayed
/// credit) so fractional rewards accumulate without float save drift; the HUD reads whole
/// credits. Accepted rewards within a <c>0.25 s</c> interval coalesce into a single
/// <c>+$N.N</c> feedback burst. Being grabbed adds no modifier; tool differences come only
/// from real contact and the shared pain curve.
/// </summary>
public sealed class RewardLedger
{
    public const double CoalesceSeconds = 0.25;
    public const long MilliCreditsPerCredit = 1000;

    private readonly double _cashPerPain;
    private readonly Queue<RewardFeedback> _completedBursts = new();

    private bool _burstOpen;
    private long _burstMilli;
    private double _burstStart;

    /// <param name="cashPerPain">Credits awarded per unit of pain (approved tuning).</param>
    public RewardLedger(double cashPerPain, long initialBalanceMilliCredits = 0)
    {
        if (cashPerPain < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(cashPerPain));
        }
        if (initialBalanceMilliCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialBalanceMilliCredits));
        }

        _cashPerPain = cashPerPain;
        BalanceMilliCredits = initialBalanceMilliCredits;
    }

    public long BalanceMilliCredits { get; private set; }

    /// <summary>Whole-credit balance for the HUD (floored, RAGDOLL §7.4).</summary>
    public long BalanceCredits => BalanceMilliCredits / MilliCreditsPerCredit;

    /// <summary>
    /// Applies one accepted damage event and returns the milli-credits awarded. The
    /// balance updates immediately; the value also joins the current coalescing burst.
    /// </summary>
    public long Accept(float pain, PayoutRegion region, DamageConsciousness consciousness, double now)
    {
        float consciousnessMultiplier = consciousness == DamageConsciousness.Unconscious
            ? PayoutMultipliers.Unconscious
            : PayoutMultipliers.Conscious;

        double credits = pain * PayoutMultipliers.Region(region) * consciousnessMultiplier * _cashPerPain;
        long milli = (long)Math.Round(credits * MilliCreditsPerCredit, MidpointRounding.AwayFromZero);

        BalanceMilliCredits += milli;

        if (_burstOpen && now - _burstStart <= CoalesceSeconds)
        {
            _burstMilli += milli;
        }
        else
        {
            // An elapsed burst that was never polled is queued, not overwritten — a
            // presentation hitch may delay a +$N.N toast but must never lose one.
            if (_burstOpen)
            {
                _completedBursts.Enqueue(new RewardFeedback(_burstMilli, _burstStart));
            }

            _burstOpen = true;
            _burstStart = now;
            _burstMilli = milli;
        }

        return milli;
    }

    /// <summary>
    /// Deposits already-earned milli-credits (passive income, RAGDOLL §8.3) directly
    /// into the balance. Deposits produce no <c>+$N.N</c> feedback burst — coalesced
    /// feedback is reserved for accepted damage rewards (§7.4).
    /// </summary>
    public void Deposit(long milliCredits)
    {
        if (milliCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliCredits));
        }

        BalanceMilliCredits += milliCredits;
    }

    /// <summary>
    /// Atomically spends an integer milli-credit amount. Insufficient funds leave the
    /// balance unchanged; purchase prices are validated by the catalogue/economy boundary.
    /// </summary>
    public bool TrySpend(long milliCredits)
    {
        if (milliCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliCredits));
        }

        if (milliCredits > BalanceMilliCredits)
        {
            return false;
        }

        BalanceMilliCredits -= milliCredits;
        return true;
    }

    /// <summary>
    /// Returns a completed feedback burst once its 0.25 s interval has elapsed, else null.
    /// Presentation polls this each tick to surface a single coalesced <c>+$N.N</c>.
    /// </summary>
    public RewardFeedback? PollFeedback(double now)
    {
        if (_completedBursts.Count > 0)
        {
            return _completedBursts.Dequeue();
        }

        if (_burstOpen && now - _burstStart > CoalesceSeconds)
        {
            var feedback = new RewardFeedback(_burstMilli, _burstStart);
            _burstOpen = false;
            _burstMilli = 0;
            return feedback;
        }

        return null;
    }
}
