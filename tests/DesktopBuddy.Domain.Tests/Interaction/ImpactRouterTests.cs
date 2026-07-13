using DesktopBuddy.Domain.Interaction;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Interaction;

public sealed class ImpactRouterTests
{
    private static ContactSample Contact(
        double time,
        float impulse = 50.0f,
        int source = 1,
        string part = "head") =>
        new(SourceInteractionId: source, TargetPartId: part, Impulse: impulse, RelativeVelocity: 300.0f, TimeSeconds: time);

    [Fact]
    public void Offer_FirstContact_IsAcceptedOnce()
    {
        var router = new ImpactRouter();

        ImpactSample? accepted = router.Offer(Contact(0.0));

        Assert.NotNull(accepted);
        Assert.Equal("head", accepted!.Value.TargetPartId);
        Assert.Equal(50.0f, accepted.Value.Impulse);
    }

    [Fact]
    public void Offer_RepeatWithinEpisode_IsSuppressed()
    {
        var router = new ImpactRouter();
        router.Offer(Contact(0.0));

        // Resting/sliding callbacks every physics frame (~1/120 s apart).
        Assert.Null(router.Offer(Contact(0.008)));
        Assert.Null(router.Offer(Contact(0.016)));
        Assert.Null(router.Offer(Contact(0.10)));
    }

    [Fact]
    public void Offer_ReArmsAfterInactivityGap()
    {
        var router = new ImpactRouter();
        router.Offer(Contact(0.0));

        // A full 0.15 s of no contact for this key re-arms a new episode.
        ImpactSample? second = router.Offer(Contact(0.15));

        Assert.NotNull(second);
    }

    [Fact]
    public void Offer_SubReArmReContact_IsRejected()
    {
        var router = new ImpactRouter();
        router.Offer(Contact(0.0));

        // 0.14 s < 0.15 s re-arm window: still the same episode.
        Assert.Null(router.Offer(Contact(0.14)));
    }

    [Fact]
    public void Offer_ContinuousContactNeverReArms()
    {
        var router = new ImpactRouter();
        router.Offer(Contact(0.0));

        // Contact every frame across 1 s of real time: the gap never reaches 0.15 s,
        // so the resting stream stays a single suppressed episode.
        int accepted = 0;
        for (double t = 0.008; t <= 1.0; t += 0.008)
        {
            if (router.Offer(Contact(t)) is not null)
            {
                accepted++;
            }
        }

        Assert.Equal(0, accepted);
    }

    [Fact]
    public void Offer_DistinctKeysAreIndependent()
    {
        var router = new ImpactRouter();

        Assert.NotNull(router.Offer(Contact(0.0, source: 1, part: "head")));
        Assert.NotNull(router.Offer(Contact(0.0, source: 1, part: "torso")));
        Assert.NotNull(router.Offer(Contact(0.0, source: 2, part: "head")));

        // Each key suppresses only its own repeats.
        Assert.Null(router.Offer(Contact(0.01, source: 1, part: "head")));
        Assert.NotNull(router.Offer(Contact(0.20, source: 1, part: "head")));
    }

    [Fact]
    public void Offer_BelowMinimumImpulse_IsIgnoredAndDoesNotOpenEpisode()
    {
        var router = new ImpactRouter(minimumImpulse: 10.0f);

        // A graze scores nothing and must not open an episode masking a real hit.
        Assert.Null(router.Offer(Contact(0.0, impulse: 5.0f)));

        // A real hit 0.01 s later is still the first valid contact of a fresh episode.
        Assert.NotNull(router.Offer(Contact(0.01, impulse: 80.0f)));
    }

    [Fact]
    public void Reset_ClearsEpisodeState()
    {
        var router = new ImpactRouter();
        router.Offer(Contact(0.0));

        router.Reset();

        // After a hard reposition the same key opens a fresh episode immediately.
        Assert.NotNull(router.Offer(Contact(0.01)));
    }
}
