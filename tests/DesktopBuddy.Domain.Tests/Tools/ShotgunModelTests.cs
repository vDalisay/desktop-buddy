using System;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Tools;

/// <summary>
/// The Shotgun's original contract from RAGDOLL §9.2 (FR-010.2), stated in routed ticks
/// at the project's fixed 120 Hz: magazine 5, minimum shot interval 0.9 s (108 ticks),
/// reload 2.0 s (240 ticks), six pellets per shot, unlimited reserve, one shot per
/// primary press. The shipped profile has since been re-authored faster and given an
/// infinite magazine (owner 2026-08-22, see gun_shotgun.tres and the shotgun_spread
/// scenario, which measure what actually ships); these rows keep the original numbers on
/// purpose, as the pump-gun cadence table the machine has to satisfy at any tuning.
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
    private const int PumpTicks = 24;

    /// <summary>Long enough to cover a stroke and the cadence window behind it.</summary>
    private const int BufferTicks = 140;

    private static readonly GunConstants Shotgun =
        new(Capacity, IntervalTicks, ReloadTicks, Pellets, true, PumpTicks);

    [Fact]
    public void ShotgunConstantsMatchTheAuthoredSeconds()
    {
        Assert.True(Shotgun.IsWellFormed());
        Assert.Equal(0.9, (double)IntervalTicks / TicksPerSecond, 6);
        Assert.Equal(2.0, (double)ReloadTicks / TicksPerSecond, 6);
        Assert.Equal(5, Shotgun.MagazineCapacity);
        Assert.Equal(6, Shotgun.ProjectilesPerShot);
        Assert.True(Shotgun.RequiresPumpBetweenShots);
        Assert.Equal(PumpTicks, Shotgun.PumpTicks);
    }

    /// <summary>
    /// The shipped Shotgun authors <see cref="GunConstants.InfiniteMagazine"/> (owner
    /// instruction 2026-08-22): pump and shoot, with no magazine break in it at all. Fired
    /// well past what the magazine holds, the rounds never come down, and neither the dry
    /// fire nor the reload that used to end a magazine ever appears.
    /// </summary>
    [Fact]
    public void AnInfiniteMagazineNeverEmptiesDryFiresOrReloads()
    {
        var gun = new Gun(Shotgun with { InfiniteMagazine = true });

        int fired = 0;
        int dryFires = 0;
        int reloads = 0;
        for (int shot = 0; shot < Capacity * 4; shot++)
        {
            GunResult result = gun.PullWhenReady();
            fired += result.Fired ? 1 : 0;
            dryFires += result.DryFired ? 1 : 0;
            reloads += result.ReloadStarted ? 1 : 0;
        }

        Assert.Equal(Capacity * 4, fired);
        Assert.Equal(0, dryFires);
        Assert.Equal(0, reloads);
        Assert.Equal(Capacity, gun.Rounds);
        Assert.False(gun.Phase.IsReloading);
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
        Assert.True(gun.Phase.ChamberEmpty);
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
    public void TheClickAfterAShotWorksThePumpEvenInsideTheCadenceWindow()
    {
        var gun = new Gun(Shotgun);
        gun.Tick(triggerHeld: true);

        GunResult pump = gun.PullAfter(1);

        Assert.True(pump.PumpStarted);
        Assert.False(pump.Fired);
        Assert.Equal(PumpTicks, gun.Phase.PumpTicksRemaining);
        Assert.True(gun.Phase.ChamberEmpty);

        gun.Idle(PumpTicks - 1);
        Assert.True(gun.Phase.IsPumping);
        GunResult completed = gun.Idle(1);
        Assert.True(completed.PumpCompleted);
        Assert.False(gun.Phase.ChamberEmpty);
        Assert.Equal(Capacity - 1, gun.Rounds);
    }

    [Fact]
    public void TheNextShellLandsExactlyOneIntervalAfterTheLast()
    {
        var gun = new Gun(Shotgun);
        gun.Tick(triggerHeld: true);

        GunResult onTheBoundary = gun.PullWhenReady();

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
            GunResult result = gun.PullWhenReady();
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

        GunResult sixth = gun.PullWhenReady();

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
        gun.PullWhenReady();

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
        gun.PullWhenReady();

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
        gun.PullWhenReady();
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
            gun.PullWhenReady();
            gun.Idle(ReloadTicks);
            Assert.Equal(Capacity, gun.Rounds);
        }
    }

    /// <summary>
    /// Test-side holder for the immutable phase, so each case reads as a sequence of
    /// player actions rather than as phase plumbing. Deliberately the same shape as
    /// <see cref="GunModelTests"/>'s: two guns, one machine.
    /// </summary>
    [Fact]
    public void APressDuringThePumpIsRememberedAndFiresAsSoonAsTheGunIsReady()
    {
        // The reported jam: mashing primary on a pump gun spends most presses into a
        // stroke or an interval that is still running, and the gun looks stuck.
        var buffered = new GunConstants(
            Capacity, IntervalTicks, ReloadTicks, Pellets, true, PumpTicks, PressBufferTicks: BufferTicks);
        var gun = new Gun(buffered);

        gun.Tick(triggerHeld: true);
        gun.Tick(triggerHeld: false);
        gun.PullAfter(1);            // works the action
        Assert.True(gun.Phase.IsPumping);

        gun.Tick(triggerHeld: false);
        gun.Tick(triggerHeld: true); // mashed while the stroke runs: remembered, not lost
        Assert.True(gun.Phase.BufferedPressTicks > 0);

        bool fired = false;
        for (int tick = 0; tick < IntervalTicks + PumpTicks; tick++)
        {
            GunResult result = gun.Tick(triggerHeld: false);
            if (!result.Fired)
                continue;

            fired = true;
            Assert.Equal(Pellets, result.Projectiles);
            break;
        }

        Assert.True(fired);
        Assert.Equal(0, gun.Phase.BufferedPressTicks);
    }

    [Fact]
    public void OneBufferedPressIsOneShellAndNeverABurst()
    {
        var buffered = new GunConstants(
            Capacity, IntervalTicks, ReloadTicks, Pellets, true, PumpTicks, PressBufferTicks: BufferTicks);
        var gun = new Gun(buffered);

        gun.Tick(triggerHeld: true);
        gun.Tick(triggerHeld: false);
        gun.PullAfter(1);
        gun.Tick(triggerHeld: false);
        gun.Tick(triggerHeld: true);

        int shots = 0;
        for (int tick = 0; tick < (IntervalTicks * 3) + PumpTicks; tick++)
        {
            if (gun.Tick(triggerHeld: false).Fired)
                shots++;
        }

        Assert.Equal(1, shots);
    }

    [Fact]
    public void WithoutAnAuthoredBufferAnEarlyPressIsStillDropped()
    {
        var gun = new Gun(Shotgun);

        gun.Tick(triggerHeld: true);
        gun.Tick(triggerHeld: false);
        gun.PullAfter(1);
        gun.Tick(triggerHeld: false);
        gun.Tick(triggerHeld: true);

        int shots = 0;
        for (int tick = 0; tick < (IntervalTicks * 2) + PumpTicks; tick++)
        {
            if (gun.Tick(triggerHeld: false).Fired)
                shots++;
        }

        Assert.Equal(0, shots);
    }

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

        public GunResult PullWhenReady()
        {
            if (Phase.ChamberEmpty)
            {
                GunResult pump = PullAfter(1);
                Assert.True(pump.PumpStarted);
                Idle(_constants.PumpTicks);
            }

            int wait = Math.Max(1, _constants.ShotIntervalTicks - Phase.TicksSinceShot - 1);
            return PullAfter(wait);
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
                PullWhenReady();
        }
    }
}
