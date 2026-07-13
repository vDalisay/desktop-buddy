using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Mood;

public sealed class CareModelTests
{
    [Fact]
    public void AccumulateValidContact_AwardsOncePerThreeSeconds()
    {
        var care = new CareModel();

        // Feed 359 exact 1/120 s ticks: no award yet.
        int awards = 0;
        for (int tick = 0; tick < 359; tick++)
        {
            awards += care.AccumulateValidContact(CareKind.Pet, 1.0 / 120.0);
        }

        Assert.Equal(0, awards);

        // Tick 360 reaches exactly 3 s and yields one +1 despite binary-float noise.
        awards += care.AccumulateValidContact(CareKind.Pet, 1.0 / 120.0);
        Assert.Equal(1, awards);
    }

    [Fact]
    public void AccumulateValidContact_EmptySpaceHoldsNeverAward()
    {
        var care = new CareModel();

        // Held input over empty space feeds zero valid-contact time.
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(0, care.AccumulateValidContact(CareKind.Pet, 0.0));
        }

        Assert.Equal(0.0, care.ProgressSeconds(CareKind.Pet));
    }

    [Fact]
    public void AccumulateValidContact_PetAndTickleTrackIndependently()
    {
        var care = new CareModel();

        care.AccumulateValidContact(CareKind.Pet, 2.5);
        care.AccumulateValidContact(CareKind.Tickle, 1.0);

        // Neither has reached 3 s; each carries its own progress.
        Assert.Equal(2.5, care.ProgressSeconds(CareKind.Pet));
        Assert.Equal(1.0, care.ProgressSeconds(CareKind.Tickle));

        // Pet crosses 3 s; Tickle stays short.
        Assert.Equal(1, care.AccumulateValidContact(CareKind.Pet, 0.6));
        Assert.Equal(0, care.AccumulateValidContact(CareKind.Tickle, 0.5));
    }

    [Fact]
    public void AccumulateValidContact_CarriesRemainderPastReward()
    {
        var care = new CareModel();

        // 3.5 s of contact → one award, 0.5 s carried toward the next.
        Assert.Equal(1, care.AccumulateValidContact(CareKind.Tickle, 3.5));
        Assert.Equal(0.5, care.ProgressSeconds(CareKind.Tickle), 5);
    }

    [Fact]
    public void AccumulateValidContact_LargeSpanYieldsMultipleAwards()
    {
        var care = new CareModel();

        Assert.Equal(3, care.AccumulateValidContact(CareKind.Pet, 9.0));
    }

    [Fact]
    public void Reset_ClearsProgress()
    {
        var care = new CareModel();
        care.AccumulateValidContact(CareKind.Pet, 2.0);

        care.Reset();

        Assert.Equal(0.0, care.ProgressSeconds(CareKind.Pet));
    }
}
