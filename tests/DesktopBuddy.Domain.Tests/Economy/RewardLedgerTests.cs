using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Economy;

public sealed class RewardLedgerTests
{
    // cashPerPain = 1 credit/pain keeps the arithmetic legible: milli = pain*mults*1000.
    private static RewardLedger Ledger(double cashPerPain = 1.0) => new(cashPerPain);

    [Theory]
    [InlineData(PayoutRegion.Head, 1.2f)]
    [InlineData(PayoutRegion.Torso, 1.0f)]
    [InlineData(PayoutRegion.Arms, 0.8f)]
    [InlineData(PayoutRegion.Legs, 0.8f)]
    public void Accept_AppliesRegionMultiplier(PayoutRegion region, float multiplier)
    {
        var ledger = Ledger();

        long milli = ledger.Accept(10.0f, region, DamageConsciousness.Conscious, 0.0);

        Assert.Equal((long)(10.0f * multiplier * 1000), milli);
    }

    [Fact]
    public void Accept_UnconsciousHalvesPayout()
    {
        var ledger = Ledger();

        long conscious = ledger.Accept(10.0f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.0);
        long unconscious = ledger.Accept(10.0f, PayoutRegion.Torso, DamageConsciousness.Unconscious, 1.0);

        Assert.Equal(10_000, conscious);
        Assert.Equal(5_000, unconscious);
    }

    [Fact]
    public void Accept_AccumulatesMilliCreditsWithoutDrift()
    {
        var ledger = Ledger(cashPerPain: 0.001);

        // 0.7 pain * torso 1.0 * 0.001 credit/pain = 0.0007 credit = 0.7 milli → rounds to 1.
        for (int i = 0; i < 1000; i++)
        {
            ledger.Accept(0.7f, PayoutRegion.Torso, DamageConsciousness.Conscious, i * 0.5);
        }

        // 1000 events * 1 milli each; integer accumulation, no float creep.
        Assert.Equal(1_000, ledger.BalanceMilliCredits);
        Assert.Equal(1, ledger.BalanceCredits);
    }

    [Fact]
    public void BalanceCredits_FloorsMilliCredits()
    {
        var ledger = Ledger();
        ledger.Accept(1.5f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.0); // 1500 milli

        Assert.Equal(1500, ledger.BalanceMilliCredits);
        Assert.Equal(1, ledger.BalanceCredits);
    }

    [Fact]
    public void PollFeedback_CoalescesRewardsWithinInterval()
    {
        var ledger = Ledger();
        ledger.Accept(10.0f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.0);
        ledger.Accept(10.0f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.1);
        ledger.Accept(10.0f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.2);

        // Still inside the 0.25 s interval → no burst has closed yet.
        Assert.Null(ledger.PollFeedback(0.2));

        // After the interval, the three rewards surface as one +$N.N burst.
        RewardFeedback? feedback = ledger.PollFeedback(0.3);
        Assert.NotNull(feedback);
        Assert.Equal(30_000, feedback!.Value.MilliCredits);
    }

    [Fact]
    public void PollFeedback_SeparatesRewardsBeyondInterval()
    {
        var ledger = Ledger();
        ledger.Accept(10.0f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.0);

        // A reward 0.5 s later opens a new burst; the first must have flushed.
        RewardFeedback? first = ledger.PollFeedback(0.5);
        Assert.NotNull(first);
        Assert.Equal(10_000, first!.Value.MilliCredits);

        ledger.Accept(20.0f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.5);
        RewardFeedback? second = ledger.PollFeedback(0.8);
        Assert.NotNull(second);
        Assert.Equal(20_000, second!.Value.MilliCredits);
    }

    [Fact]
    public void PollFeedback_ReturnsNullWhenNothingPending()
    {
        var ledger = Ledger();
        Assert.Null(ledger.PollFeedback(1.0));
    }

    [Fact]
    public void Accept_NeverDropsAnUnpolledBurst()
    {
        var ledger = Ledger();
        ledger.Accept(10.0f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.0);

        // A second accept beyond the interval, with no poll in between (presentation
        // hitch): the first burst must survive as queued feedback, not be overwritten.
        ledger.Accept(20.0f, PayoutRegion.Torso, DamageConsciousness.Conscious, 0.5);

        RewardFeedback? first = ledger.PollFeedback(0.5);
        Assert.NotNull(first);
        Assert.Equal(10_000, first!.Value.MilliCredits);

        RewardFeedback? second = ledger.PollFeedback(1.0);
        Assert.NotNull(second);
        Assert.Equal(20_000, second!.Value.MilliCredits);

        Assert.Null(ledger.PollFeedback(2.0));
        Assert.Equal(30_000, ledger.BalanceMilliCredits);
    }

    [Fact]
    public void Deposit_AddsBalanceWithoutFeedbackBurst()
    {
        var ledger = Ledger();

        ledger.Deposit(2_500); // e.g. accrued passive income

        Assert.Equal(2_500, ledger.BalanceMilliCredits);
        Assert.Equal(2, ledger.BalanceCredits);
        Assert.Null(ledger.PollFeedback(10.0)); // deposits never make +$N.N toasts
    }

    [Fact]
    public void Deposit_RejectsNegativeAmounts()
    {
        var ledger = Ledger();
        Assert.Throws<System.ArgumentOutOfRangeException>(() => ledger.Deposit(-1));
    }
}
