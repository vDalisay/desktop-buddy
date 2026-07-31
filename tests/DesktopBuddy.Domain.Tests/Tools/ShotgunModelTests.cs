using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Tools;

/// <summary>
/// The Shotgun's authored contract from RAGDOLL §9.2 (FR-010.2), stated in routed ticks
/// at the project's fixed 120 Hz: magazine 5, minimum shot interval 0.9 s (108 ticks),
/// reload 2.0 s (240 ticks), six pellets per shot, unlimited reserve, one shot per
/// primary press.
///
/// <para>The whole point of these rows is that they are a <b>profile table</b> and not a
/// second state machine: every rule below is the same <see cref="GunMachine"/> the Pistol
/// runs, exercised at the Shotgun's numbers. A rule that needed new code to hold here
/// would mean the platform had been forked, which is exactly what the M5 gun spine was
/// built to avoid.</para>
/// </summary>
public sealed class ShotgunModelTests
{
    private const int TicksPerSecond = 120;
    private const int Capacity = 5;
    private const int IntervalTicks = 108;
    private const int ReloadTicks = 240;
    private const int Pellets = 6;

    private static readonly GunConstants Shotgun =
        new(Capacity, IntervalTicks, ReloadTicks, Pellets);

    [Fact]
    public void ShotgunConstantsMatchTheAuthoredSeconds()
    {
        Assert.True(Shotgun.IsWellFormed());
        Assert.Equal(0.9, (double)IntervalTicks / TicksPerSecond, 6);
        Assert.Equal(2.0, (double)ReloadTicks / TicksPerSecond, 6);
        Assert.Equal(5, Shotgun.MagazineCapacity);
        Assert.Equal(6, Shotgun.ProjectilesPerShot);
    }

    [Fact]
    public void ADrawnShotgunReleasesSixPelletsOnTheFirstPress()
    {
        var gun = new Gun(Shotgun);

        GunResult result = gun.Tick(triggerHeld: true);

        Assert.True(result.Fired);
        Assert.Equal(Pellets, result.Projectiles);
        Assert.Equal(Capacity - 1, gun.Rounds);
        Assert.Equal(1, gun.Phase.ShotEpoch);
    }

    [Fact]
    public void HoldingTheTriggerFiresExactlyOneShell()
    {
        var gun = new Gun(Shotgun);
        Assert.True(gun.Tick(triggerHeld: true).Fired);

        int firedWhileHeld = 0;
        int pelletsWhileHeld = 0;
        for (int tick = 0; tick < IntervalTicks * 3; tick++)
        {
            GunResult result = gun.Tick(triggerHeld: true);
            firedWhileHeld += result.Fired ? 1 : 0;
            pelletsWhileHeld += result.Projectiles;
        }

        Assert.Equal(0, firedWhileHeld);
        Assert.Equal(0, pelletsWhileHeld);
        Assert.Equal(Capacity - 1, gun.Rounds);
    }

    [Fact]
    public void APressInsideTheNinetyHundredthsSecondCadenceIsSpentWithoutFiringLater()
    {
        var gun = new Gun(Shotgun);
        gun.Tick(triggerHeld: true);

        // One tick short of the interval: refused, and it must not linger.
        Assert.False(gun.PullAfter(IntervalTicks - 2).Fired);

        int firedAfterwards = 0;
        for (int tick = 0; tick < IntervalTicks * 2; tick++)
            firedAfterwards += gun.Tick(triggerHeld: true).Fired ? 1 : 0;

        Assert.Equal(0, firedAfterwards);
        Assert.Equal(Capacity - 1, gun.Rounds);
    }

    [Fact]
    public void TheNextShellLandsExactlyOneIntervalAfterTheLast()
    {
        var gun = new Gun(Shotgun);
        gun.Tick(triggerHeld: true);

        GunResult onTheBoundary = gun.PullAfter(IntervalTicks - 1);

        Assert.True(onTheBoundary.Fired);
        Assert.Equal(Pellets, onTheBoundary.Projectiles);
        Assert.Equal(Capacity - 2, gun.Rounds);
    }

    [Fact]
    public void FiveShellsEmptyTheMagazineWithoutStartingAReload()
    {
        var gun = new Gun(Shotgun);

        int reloadStarts = 0;
        int pellets = 0;
        for (int shell = 0; shell < Capacity; shell++)
        {
            GunResult result = gun.PullAfter(IntervalTicks);
            Assert.True(result.Fired);
            pellets += result.Projectiles;
            reloadStarts += result.ReloadStarted ? 1 : 0;
        }

        Assert.Equal(Capacity * Pellets, pellets);
        Assert.Equal(0, gun.Rounds);
        Assert.Equal(0, reloadStarts);
        Assert.False(gun.Phase.IsReloading);
        Assert.Equal(Capacity, gun.Phase.ShotEpoch);
    }

    [Fact]
    public void TheSixthPressDryFiresAndStartsTheAutomaticReload()
    {
        var gun = new Gun(Shotgun);
        gun.Empty();

        GunResult sixth = gun.PullAfter(IntervalTicks);

        Assert.False(sixth.Fired);
        Assert.Equal(0, sixth.Projectiles);
        Assert.True(sixth.DryFired);
        Assert.True(sixth.ReloadStarted);
        Assert.True(gun.Phase.IsReloading);
        Assert.Equal(ReloadTicks, gun.Phase.ReloadTicksRemaining);
    }

    [Fact]
    public void TheTwoSecondReloadCompletesExactlyOnItsAuthoredTick()
    {
        var gun = new Gun(Shotgun);
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
    public void PressesDuringTheTwoSecondReloadAreIgnored()
    {
        var gun = new Gun(Shotgun);
        gun.Empty();
        gun.PullAfter(IntervalTicks);

        int fired = 0;
        int pellets = 0;
        for (int tick = 0; tick < ReloadTicks - 1; tick++)
        {
            GunResult result = gun.Tick(triggerHeld: tick % 2 == 1);
            fired += result.Fired ? 1 : 0;
            pellets += result.Projectiles;
        }

        Assert.Equal(0, fired);
        Assert.Equal(0, pellets);
        Assert.True(gun.Phase.IsReloading);
        Assert.Equal(0, gun.Rounds);
    }

    [Fact]
    public void AMidReloadPressCannotShortenOrRestartTheReload()
    {
        var gun = new Gun(Shotgun);
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
        var gun = new Gun(Shotgun);
        gun.Tick(triggerHeld: true);

        Assert.True(gun.Tick(triggerHeld: false, reloadRequested: true).ReloadStarted);

        gun.Idle(ReloadTicks - 1);
        Assert.True(gun.Idle(1).ReloadCompleted);
        Assert.Equal(Capacity, gun.Rounds);
    }

    [Fact]
    public void TheReloadActionOnAFullMagazineDoesNothing()
    {
        var gun = new Gun(Shotgun);

        GunResult result = gun.Tick(triggerHeld: false, reloadRequested: true);

        Assert.False(result.ReloadStarted);
        Assert.False(gun.Phase.IsReloading);
        Assert.Equal(Capacity, gun.Rounds);
    }

    [Fact]
    public void ShellReserveIsUnlimited()
    {
        var gun = new Gun(Shotgun);

        for (int magazine = 0; magazine < 4; magazine++)
        {
            gun.Empty();
            gun.PullAfter(IntervalTicks);
            gun.Idle(ReloadTicks);
            Assert.Equal(Capacity, gun.Rounds);
        }
    }

    /// <summary>
    /// Test-side holder for the immutable phase, so each case reads as a sequence of
    /// player actions rather than as phase plumbing. Deliberately the same shape as
    /// <see cref="GunModelTests"/>'s: two guns, one machine.
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
