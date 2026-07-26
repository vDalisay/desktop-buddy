using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Mood;

public sealed class CareConsumableModelTests
{
    private static readonly CareConsumableTuning Food = CareConsumableTuning.LabFood;

    [Fact]
    public void LabFoodTuning_MatchesTheProvisionalMealNumbers()
    {
        // +10 mood and 60 s at 120 Hz, borrowed from FR-008.4 pending M5 calibration.
        Assert.Equal(10.0f, Food.MoodGain);
        Assert.Equal(7200, Food.CooldownTicks);
    }

    [Fact]
    public void Complete_GrantsMoodOnceAndStartsTheCooldown()
    {
        var model = new CareConsumableModel();

        Assert.True(model.TryBegin(ContentIds.CareLabFood, out int token, out ConsumeRejection why));
        Assert.Equal(ConsumeRejection.None, why);

        ConsumeResult result = model.Complete(token, Food);

        Assert.True(result.Applied);
        Assert.Equal(10.0f, result.MoodGain);
        Assert.Equal(7200, result.CooldownTicks);
        Assert.True(model.IsOnCooldown(ContentIds.CareLabFood));
        Assert.False(model.IsConsuming);
    }

    [Fact]
    public void Complete_IsIdempotentForAReplayedToken()
    {
        // The runtime converts an authoritative bite signal into success; a repeated or
        // late signal must not double-pay or restart the cooldown.
        var model = new CareConsumableModel();
        model.TryBegin(ContentIds.CareLabFood, out int token, out _);
        model.Complete(token, Food);

        ConsumeResult replay = model.Complete(token, Food);

        Assert.False(replay.Applied);
        Assert.Equal(0.0f, replay.MoodGain);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancel_NeverStartsACooldown(bool cancelWithStaleToken)
    {
        var model = new CareConsumableModel();
        model.TryBegin(ContentIds.CareLabFood, out int token, out _);

        ConsumeResult result = model.Cancel(cancelWithStaleToken ? token + 99 : token);

        // FR-008.10: a cancelled, dropped, missed, or interrupted use starts no cooldown.
        Assert.False(result.Applied);
        Assert.Equal(0, result.CooldownTicks);
        Assert.False(model.IsOnCooldown(ContentIds.CareLabFood));
    }

    [Fact]
    public void Cancel_ThenRetry_IsAllowedImmediately()
    {
        var model = new CareConsumableModel();
        model.TryBegin(ContentIds.CareLabFood, out int first, out _);
        model.Cancel(first);

        Assert.True(model.TryBegin(ContentIds.CareLabFood, out int second, out ConsumeRejection why));
        Assert.Equal(ConsumeRejection.None, why);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TryBegin_RefusesWhileOnCooldownUntilItExpiresExactly()
    {
        var model = new CareConsumableModel();
        model.TryBegin(ContentIds.CareLabFood, out int token, out _);
        model.Complete(token, Food);

        model.Tick(Food.CooldownTicks - 1);
        Assert.Equal(1, model.CooldownTicksRemaining(ContentIds.CareLabFood));
        Assert.False(model.TryBegin(ContentIds.CareLabFood, out _, out ConsumeRejection blocked));
        Assert.Equal(ConsumeRejection.OnCooldown, blocked);

        model.Tick();
        Assert.Equal(0, model.CooldownTicksRemaining(ContentIds.CareLabFood));
        Assert.True(model.TryBegin(ContentIds.CareLabFood, out _, out ConsumeRejection ok));
        Assert.Equal(ConsumeRejection.None, ok);
    }

    [Fact]
    public void TryBegin_RefusesASecondConcurrentConsume()
    {
        var model = new CareConsumableModel();
        model.TryBegin(ContentIds.CareLabFood, out _, out _);

        Assert.False(model.TryBegin(ContentIds.CareLabFood, out _, out ConsumeRejection why));
        Assert.Equal(ConsumeRejection.AlreadyConsuming, why);
    }

    [Fact]
    public void TryBegin_RejectsAMissingContentId()
    {
        var model = new CareConsumableModel();

        Assert.False(model.TryBegin(" ", out _, out ConsumeRejection why));
        Assert.Equal(ConsumeRejection.UnknownConsumable, why);
    }

    [Fact]
    public void Cooldowns_AreTrackedPerConsumable()
    {
        var model = new CareConsumableModel();
        model.TryBegin(ContentIds.CareLabFood, out int token, out _);
        model.Complete(token, Food);

        // A different consumable is unaffected by the food cooldown.
        Assert.True(model.IsOnCooldown(ContentIds.CareLabFood));
        Assert.False(model.IsOnCooldown("care.other"));
        Assert.True(model.TryBegin("care.other", out _, out _));
    }

    [Fact]
    public void Tick_DoesNotAdvanceOnZeroOrNegativeSpans()
    {
        var model = new CareConsumableModel();
        model.TryBegin(ContentIds.CareLabFood, out int token, out _);
        model.Complete(token, Food);

        model.Tick(0);
        model.Tick(-500);

        Assert.Equal(7200, model.CooldownTicksRemaining(ContentIds.CareLabFood));
    }

    [Fact]
    public void Reset_ClearsCooldownsAndTheOpenTransaction()
    {
        var model = new CareConsumableModel();
        model.TryBegin(ContentIds.CareLabFood, out int token, out _);
        model.Complete(token, Food);
        model.TryBegin("care.other", out _, out _);

        model.Reset();

        Assert.False(model.IsOnCooldown(ContentIds.CareLabFood));
        Assert.False(model.IsConsuming);
    }
}
