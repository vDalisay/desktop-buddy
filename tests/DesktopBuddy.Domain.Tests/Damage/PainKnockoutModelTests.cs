using DesktopBuddy.Domain.Damage;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Damage;

public sealed class PainKnockoutModelTests
{
    [Fact]
    public void RegisterPain_BelowThreshold_StaysConscious()
    {
        var model = new PainKnockoutModel();

        PainKnockoutState state = model.RegisterPain(99.0f, 0.0);

        Assert.Equal(DamageConsciousness.Conscious, state.Consciousness);
        Assert.Equal(99.0f, state.RollingPain);
        Assert.Equal(0, model.KnockoutCount);
    }

    [Fact]
    public void RegisterPain_ReachingThreshold_KnocksOutOnce()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(60.0f, 0.0);

        PainKnockoutState state = model.RegisterPain(40.0f, 1.0);

        Assert.Equal(DamageConsciousness.Unconscious, state.Consciousness);
        Assert.True(state.KnockoutActive);
        Assert.Equal(1, model.KnockoutCount);
    }

    [Fact]
    public void RollingWindow_SlidesOutOldEvents()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(60.0f, 0.0);

        // 6 s later the first event has fallen out of the 5 s window.
        PainKnockoutState state = model.RegisterPain(60.0f, 6.0);

        Assert.Equal(DamageConsciousness.Conscious, state.Consciousness);
        Assert.Equal(60.0f, state.RollingPain);
        Assert.Equal(0, model.KnockoutCount);
    }

    [Fact]
    public void Knockout_IgnoresRetriggerDuringTimer()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(100.0f, 0.0);
        Assert.Equal(1, model.KnockoutCount);

        // Hits during unconsciousness are valid elsewhere but never re-trigger/extend.
        model.RegisterPain(100.0f, 1.0);
        model.RegisterPain(100.0f, 3.9);

        Assert.Equal(1, model.KnockoutCount);
        Assert.True(model.Update(3.9).KnockoutActive);
    }

    [Fact]
    public void Knockout_WakesExactlyAtFourSeconds()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(100.0f, 0.0);

        Assert.True(model.Update(3.999).KnockoutActive);
        PainKnockoutState woken = model.Update(4.0);

        Assert.Equal(DamageConsciousness.Conscious, woken.Consciousness);
        Assert.False(woken.KnockoutActive);
    }

    [Fact]
    public void Waking_BeginsWithEmptyWindow()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(100.0f, 0.0);
        model.RegisterPain(80.0f, 1.0); // excluded while unconscious

        PainKnockoutState woken = model.Update(4.0);
        Assert.Equal(0.0f, woken.RollingPain);

        // A single 90-pain hit after waking must not knock out (window was empty).
        PainKnockoutState after = model.RegisterPain(90.0f, 4.1);
        Assert.Equal(DamageConsciousness.Conscious, after.Consciousness);
    }

    [Fact]
    public void UnconsciousHits_AreExcludedFromFutureWindow()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(100.0f, 0.0); // KO at t=0, wakes at t=4

        // Two 60-pain hits while unconscious; if they had counted, waking would re-KO.
        model.RegisterPain(60.0f, 2.0);
        model.RegisterPain(60.0f, 3.0);

        PainKnockoutState woken = model.Update(4.0);
        Assert.Equal(DamageConsciousness.Conscious, woken.Consciousness);
        Assert.Equal(1, model.KnockoutCount);
    }

    [Fact]
    public void ClearRollingPain_DoesNotShortenActiveKnockout()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(100.0f, 0.0);

        model.ClearRollingPain(); // Repair Kit mid-knockout

        Assert.True(model.Update(3.5).KnockoutActive);
        Assert.False(model.Update(4.0).KnockoutActive);
    }

    [Fact]
    public void ClearRollingPain_WhileConscious_EmptiesWindow()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(90.0f, 0.0);

        model.ClearRollingPain();

        // Fresh 90 after clearing must not sum with the cleared 90 to knock out.
        PainKnockoutState state = model.RegisterPain(90.0f, 0.1);
        Assert.Equal(DamageConsciousness.Conscious, state.Consciousness);
    }

    [Fact]
    public void Reset_ClearsKnockoutAndWindow()
    {
        var model = new PainKnockoutModel();
        model.RegisterPain(100.0f, 0.0);

        model.Reset();

        PainKnockoutState state = model.Update(0.5);
        Assert.Equal(DamageConsciousness.Conscious, state.Consciousness);
        Assert.Equal(0.0f, state.RollingPain);
    }
}
