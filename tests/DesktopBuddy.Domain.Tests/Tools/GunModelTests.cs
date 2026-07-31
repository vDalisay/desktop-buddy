using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Tools;

/// <summary>
/// The Pistol's authored contract from RAGDOLL §9.2, stated in routed ticks at the
/// project's fixed 120 Hz: magazine 8, minimum shot interval 0.25 s (30 ticks),
/// reload 1.2 s (144 ticks), unlimited reserve, one shot per primary press.
/// </summary>
public sealed class GunModelTests
{
    private const int TicksPerSecond = 120;
    private const int Capacity = 8;
    private const int IntervalTicks = 30;
    private const int ReloadTicks = 144;

    private static readonly GunConstants Pistol = new(Capacity, IntervalTicks, ReloadTicks, 1);

    [Fact]
    public void PistolConstantsMatchTheAuthoredSeconds()
    {
        Assert.True(Pistol.IsWellFormed());
        Assert.Equal(0.25, (double)IntervalTicks / TicksPerSecond, 6);
        Assert.Equal(1.2, (double)ReloadTicks / TicksPerSecond, 6);
    }

    [Fact]
    public void ADrawnGunFiresOnTheFirstPress()
    {
        var gun = new Gun(Pistol);

        GunResult result = gun.Tick(triggerHeld: true);

        Assert.True(result.Fired);
        Assert.Equal(1, result.Projectiles);
        Assert.Equal(Capacity - 1, gun.Rounds);
        Assert.Equal(1, gun.Phase.ShotEpoch);
    }

    [Fact]
    public void HoldingTheTriggerFiresExactlyOnce()
    {
        var gun = new Gun(Pistol);
        Assert.True(gun.Tick(triggerHeld: true).Fired);

        int firedWhileHeld = 0;
        for (int tick = 0; tick < IntervalTicks * 4; tick++)
        {
            if (gun.Tick(triggerHeld: true).Fired)
                firedWhileHeld++;
        }

        Assert.Equal(0, firedWhileHeld);
        Assert.Equal(Capacity - 1, gun.Rounds);
    }

    [Fact]
    public void APressOneTickInsideTheIntervalIsSpentWithoutFiringLater()
    {
        var gun = new Gun(Pistol);
        gun.Tick(triggerHeld: true);

        // One tick short of the interval.
        Assert.False(gun.PullAfter(IntervalTicks - 2).Fired);

        // And the spent press must not escape once the interval elapses: the trigger
        // is still down, so there is no new edge and nothing queued behind it.
        int firedAfterwards = 0;
        for (int tick = 0; tick < IntervalTicks * 2; tick++)
            firedAfterwards += gun.Tick(triggerHeld: true).Fired ? 1 : 0;

        Assert.Equal(0, firedAfterwards);
        Assert.Equal(Capacity - 1, gun.Rounds);
    }

    [Fact]
    public void TheNextShotLandsExactlyOneIntervalAfterTheLast()
    {
        var gun = new Gun(Pistol);
        gun.Tick(triggerHeld: true);

        GunResult onTheBoundary = gun.PullAfter(IntervalTicks - 1);

        Assert.True(onTheBoundary.Fired);
        Assert.Equal(Capacity - 2, gun.Rounds);
        Assert.Equal(2, gun.Phase.ShotEpoch);
    }

    [Fact]
    public void EightShotsEmptyTheMagazineWithoutStartingAReload()
    {
        var gun = new Gun(Pistol);

        int reloadStarts = 0;
        for (int shot = 0; shot < Capacity; shot++)
        {
            GunResult result = gun.PullAfter(IntervalTicks);
            Assert.True(result.Fired);
            reloadStarts += result.ReloadStarted ? 1 : 0;
        }

        Assert.Equal(0, gun.Rounds);
        Assert.Equal(0, reloadStarts);
        Assert.False(gun.Phase.IsReloading);
        Assert.Equal(Capacity, gun.Phase.ShotEpoch);
    }

    [Fact]
    public void TheNinthPressDryFiresAndStartsTheAutomaticReload()
    {
        var gun = new Gun(Pistol);
        gun.Empty();

        GunResult ninth = gun.PullAfter(IntervalTicks);

        Assert.False(ninth.Fired);
        Assert.True(ninth.DryFired);
        Assert.True(ninth.ReloadStarted);
        Assert.True(gun.Phase.IsReloading);
        Assert.Equal(ReloadTicks, gun.Phase.ReloadTicksRemaining);
        Assert.Equal(0, gun.Rounds);
    }

    [Fact]
    public void TheAutomaticReloadCompletesExactlyOnItsAuthoredTick()
    {
        var gun = new Gun(Pistol);
        gun.Empty();
        gun.PullAfter(IntervalTicks);

        for (int tick = 1; tick < ReloadTicks; tick++)
        {
            GunResult result = gun.Idle(1);
            Assert.False(result.ReloadCompleted);
            Assert.Equal(0, gun.Rounds);
            Assert.Equal(ReloadTicks - tick, gun.Phase.ReloadTicksRemaining);
        }

        GunResult completion = gun.Idle(1);
        Assert.True(completion.ReloadCompleted);
        Assert.Equal(Capacity, gun.Rounds);
        Assert.False(gun.Phase.IsReloading);
    }

    [Fact]
    public void PressesDuringAReloadAreIgnored()
    {
        var gun = new Gun(Pistol);
        gun.Empty();
        gun.PullAfter(IntervalTicks);

        int fired = 0;
        for (int tick = 0; tick < ReloadTicks - 1; tick++)
        {
            // Mashing: a fresh press edge every other tick for the whole reload.
            fired += gun.Tick(triggerHeld: tick % 2 == 1).Fired ? 1 : 0;
        }

        Assert.Equal(0, fired);
        Assert.True(gun.Phase.IsReloading);
        Assert.Equal(0, gun.Rounds);
    }

    [Fact]
    public void AMidReloadPressCannotShortenOrRestartTheReload()
    {
        var gun = new Gun(Pistol);
        gun.Empty();
        gun.PullAfter(IntervalTicks);
        gun.Idle(ReloadTicks / 2);
        int remaining = gun.Phase.ReloadTicksRemaining;

        GunResult mash = gun.Tick(triggerHeld: true);

        Assert.False(mash.ReloadStarted);
        Assert.False(mash.DryFired);
        Assert.Equal(remaining - 1, gun.Phase.ReloadTicksRemaining);
    }

    [Fact]
    public void TheReloadActionRefillsAPartialMagazine()
    {
        var gun = new Gun(Pistol);
        gun.Tick(triggerHeld: true);

        GunResult started = gun.Tick(triggerHeld: false, reloadRequested: true);
        Assert.True(started.ReloadStarted);

        gun.Idle(ReloadTicks - 1);
        Assert.True(gun.Idle(1).ReloadCompleted);
        Assert.Equal(Capacity, gun.Rounds);
    }

    [Fact]
    public void TheReloadActionOnAFullMagazineDoesNothing()
    {
        var gun = new Gun(Pistol);

        GunResult result = gun.Tick(triggerHeld: false, reloadRequested: true);

        Assert.False(result.ReloadStarted);
        Assert.False(gun.Phase.IsReloading);
        Assert.Equal(Capacity, gun.Rounds);
    }

    [Fact]
    public void ReserveAmmunitionIsUnlimited()
    {
        var gun = new Gun(Pistol);

        for (int magazine = 0; magazine < 4; magazine++)
        {
            gun.Empty();
            gun.PullAfter(IntervalTicks);
            gun.Idle(ReloadTicks);
            Assert.Equal(Capacity, gun.Rounds);
        }
    }

    [Fact]
    public void AShotgunProfileReleasesItsWholePelletSpreadOnOnePress()
    {
        var shotgun = new Gun(new GunConstants(5, 108, 240, 6));

        GunResult result = shotgun.Tick(triggerHeld: true);

        Assert.True(result.Fired);
        Assert.Equal(6, result.Projectiles);
        Assert.Equal(4, shotgun.Rounds);
    }

    [Fact]
    public void AMalformedProfileLeavesTheGunInert()
    {
        var gun = new Gun(new GunConstants(0, 0, 0, 0));

        GunResult result = gun.Tick(triggerHeld: true);

        Assert.False(result.IsValid);
        Assert.False(result.Fired);
        Assert.False(result.ReloadStarted);
    }

    /// <summary>
    /// Test-side holder for the immutable phase, so each case reads as a sequence of
    /// player actions rather than as phase plumbing.
    /// </summary>
    private sealed class Gun
    {
        private readonly GunConstants _constants;

        public Gun(GunConstants constants)
        {
            _constants = constants;
            Phase = GunPhase.FullyLoaded(constants);
        }

        public GunPhase Phase { get; private set; }

        public int Rounds => Phase.Rounds;

        public GunResult Tick(bool triggerHeld, bool reloadRequested = false)
        {
            GunResult result = GunMachine.Tick(
                new GunInput(Phase, triggerHeld, reloadRequested, _constants));
            Phase = result.Phase;
            return result;
        }

        /// <summary>Trigger released for <paramref name="releasedTicks"/>, then pressed.</summary>
        public GunResult PullAfter(int releasedTicks)
        {
            Idle(releasedTicks);
            return Tick(triggerHeld: true);
        }

        public GunResult Idle(int ticks)
        {
            GunResult result = default;
            for (int tick = 0; tick < ticks; tick++)
                result = Tick(triggerHeld: false);
            return result;
        }

        /// <summary>Fires the whole magazine, honoring the cadence.</summary>
        public void Empty()
        {
            while (Rounds > 0)
                PullAfter(_constants.ShotIntervalTicks);
        }
    }
}
