using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Objects;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The real pistol's presentation punctuation (plan Task G): a very small camera kick, a
/// blast flare at the muzzle, and a magazine on the floor when it reloads. All three are
/// authored per gun, and the Nerf Blaster authors all three off — so the same scenario
/// proves the pistol has them and the toy does not.
///
/// <para>None of it may touch gameplay, and the checks are written to catch it if it does:
/// the camera kick lives in its own offset lane and unwinds to exactly where the room
/// layout put the camera, the flash is started by a launch rather than by a trigger pull,
/// and the dropped magazine is on no collision layer at all — it cannot touch the buddy,
/// cannot enter the pain pipeline, and never consumes one of the FR-014 loose-object
/// slots.</para>
/// </summary>
public sealed class PistolPunctuationScenario : IScenario
{
    /// <summary>Shots fired back to back at the shake, to prove envelopes do not sum.</summary>
    private const int BurstShots = 4;

    public string Id => "pistol_punctuation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("punctuation_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        CursorGunComponent gun = lab.CursorGuns;
        Rect2 room = lab.Boundaries.InnerBounds;
        // High and level, away from the buddy: this is about what firing looks like, not
        // about what a hit does.
        var lane = new Vector2(room.Position.X + 120.0f, room.Position.Y + 44.0f);

        await SelectAndAim(tree, lab, gun, ToolId.Pistol, lane, Vector2.Right);
        GunProfile pistol = gun.ActiveProfile!;
        Vector2 cameraBase = lab.Boundaries.WorldCamera.Position;

        // --- Screenshake: bounded by one envelope, however fast the trigger goes ---
        // The Pistol's 30-tick cadence is longer than its 8-tick kick, so real shots
        // cannot overlap today. Exercise the component's restart contract directly first;
        // that pins non-stacking for later gun profiles whose cadence may be shorter.
        lab.CameraKick.ResetPeak();
        int restartKicksBefore = lab.CameraKick.KickCount;
        for (int restart = 0; restart < BurstShots; restart++)
        {
            lab.CameraKick.Kick(pistol.FireShakeAmplitudePx, pistol.FireShakeDecayTicks);
            await Tick(tree);
        }
        float restartPeak = lab.CameraKick.PeakOffsetPx;
        int restartKicks = lab.CameraKick.KickCount - restartKicksBefore;
        float envelopeBound = pistol.FireShakeAmplitudePx * 1.45f;
        await Idle(tree, gun, pistol.FireShakeDecayTicks + 8);
        bool restartReturned = !lab.CameraKick.IsKicking &&
                               lab.Boundaries.WorldCamera.Position.DistanceTo(cameraBase) < 0.001f;

        lab.CameraKick.ResetPeak();
        int kicksBefore = lab.CameraKick.KickCount;
        int pistolShotCuesBefore = lab.ReactionAudio.PistolShotCount;
        float flashPeak = 0.0f;
        bool visualFlashSeen = false;
        for (int shot = 0; shot < BurstShots; shot++)
        {
            gun.SetTriggerHeld(true);
            await Tick(tree);
            visualFlashSeen |= lab.CursorGunVisual.IsFlashVisible;
            gun.SetTriggerHeld(false);
            for (int tick = 0; tick < pistol.ShotIntervalTicks; tick++)
            {
                flashPeak = Mathf.Max(flashPeak, gun.MuzzleFlashStrength);
                await Tick(tree);
                visualFlashSeen |= lab.CursorGunVisual.IsFlashVisible;
            }
        }

        checks.Add(new StartupCheck(
            "real_pistol_shots_play_imported_randomized_audio",
            lab.ReactionAudio.PistolShotCount == pistolShotCuesBefore + BurstShots &&
            lab.ReactionAudio.LastPlayedStream is AudioStreamRandomizer,
            $"cues={lab.ReactionAudio.PistolShotCount - pistolShotCuesBefore} " +
            $"stream={lab.ReactionAudio.LastPlayedStream?.GetType().Name} " +
            $"voices={lab.ReactionAudio.VoicePoolSize}"));

        float burstPeak = lab.CameraKick.PeakOffsetPx;
        int kicks = lab.CameraKick.KickCount - kicksBefore;
        // Both axes of the wobble are bounded by the amplitude, so one envelope can reach
        // at most amplitude * sqrt(2) from the centre. Anything past that is two envelopes
        // that summed.
        await Idle(tree, gun, pistol.FireShakeDecayTicks + 8);
        checks.Add(new StartupCheck(
            "screenshake_decays_and_never_stacks",
            restartKicks == BurstShots &&
            restartPeak > 0.0f &&
            restartPeak <= envelopeBound &&
            restartReturned &&
            kicks == BurstShots &&
            burstPeak > 0.0f &&
            burstPeak <= envelopeBound &&
            !lab.CameraKick.IsKicking &&
            lab.Boundaries.WorldCamera.Position.DistanceTo(cameraBase) < 0.001f,
            $"restart_kicks={restartKicks} restart_peak={restartPeak:F3}px " +
            $"real_kicks={kicks} real_peak={burstPeak:F3}px bound={envelopeBound:F3}px " +
            $"amplitude={pistol.FireShakeAmplitudePx} decay={pistol.FireShakeDecayTicks} " +
            $"kicking={lab.CameraKick.IsKicking} " +
            $"camera_returned={lab.Boundaries.WorldCamera.Position.DistanceTo(cameraBase):F4}px"));

        // --- The flash belongs to a launch, not to a trigger pull ---
        // Empty the magazine, then keep pulling: those pulls dry-fire, and a dry fire that
        // flashed would be the gun claiming a shot it never made.
        await EmptyMagazine(tree, gun, pistol);
        int dryBefore = gun.DryFireCount;
        int pistolReloadCuesBefore = lab.ReactionAudio.PistolReloadCount;
        float dryFlashPeak = 0.0f;
        gun.SetTriggerHeld(true);
        await Tick(tree);
        gun.SetTriggerHeld(false);
        for (int tick = 0; tick < pistol.MuzzleFlashTicks + 4; tick++)
        {
            dryFlashPeak = Mathf.Max(dryFlashPeak, gun.MuzzleFlashStrength);
            await Tick(tree);
        }

        checks.Add(new StartupCheck(
            "muzzle_flash_fires_only_on_real_launches",
            flashPeak > 0.9f &&
            (lab.Mode != PresentationMode.Mii3D || visualFlashSeen) &&
            gun.DryFireCount == dryBefore + 1 &&
            dryFlashPeak <= 0.0f,
            $"live_flash_peak={flashPeak:F2} visual_seen={visualFlashSeen} " +
            $"dry_flash_peak={dryFlashPeak:F2} " +
            $"dry_fires={gun.DryFireCount - dryBefore} authored_ticks={pistol.MuzzleFlashTicks}"));

        // The dry fire above started the automatic reload, which is what ejects the
        // magazine — the same path a player's ninth pull takes.
        int registryBefore = lab.Objects.Count;
        bool dropped = await M4ObjectScenarioSupport.WaitFor(
            tree, () => gun.ActiveMagazineCount > 0, 30);
        MagazineBody? magazine = FindLiveMagazine(gun);
        uint layer = magazine?.CollisionLayer ?? 1u;
        uint mask = magazine?.CollisionMask ?? 0u;

        // Let it fall and genuinely settle on the floor. A single low-speed frame at the
        // top of a bounce is not landing, so require 30 consecutive quiet ticks in the
        // floor band.
        int settledTicks = 0;
        for (int tick = 0; tick < 480 && settledTicks < 30; tick++)
        {
            await Tick(tree);
            bool quietOnFloor = magazine is not null &&
                                GodotObject.IsInstanceValid(magazine) &&
                                magazine.IsLive &&
                                magazine.GlobalPosition.Y >= room.End.Y - 20.0f &&
                                magazine.GlobalPosition.Y <= room.End.Y + 8.0f &&
                                magazine.LinearVelocity.Length() < 6.0f;
            settledTicks = quietOnFloor ? settledTicks + 1 : 0;
        }
        bool landed = settledTicks >= 30;

        // Do not accept a one-frame floor touch followed by tunnelling. The magazine must
        // remain settled inside the room for long enough that the live player can read it
        // as an object on the floor, while still comfortably short of its five-second
        // linger.
        await Idle(tree, gun, 90);
        bool stayedOnFloor = magazine is not null &&
                             GodotObject.IsInstanceValid(magazine) &&
                             magazine.IsLive &&
                             magazine.GlobalPosition.Y >= room.End.Y - 20.0f &&
                             magazine.GlobalPosition.Y <= room.End.Y + 8.0f &&
                             magazine.LinearVelocity.Length() < 6.0f;

        // A contact probe, not just a mask reading: the magazine is teleported onto the
        // buddy's chest and given time to resolve. Nothing may happen.
        int registryDuring = lab.Objects.Count;
        long scoredBefore = lab.Pipeline.ScoredImpactCount;
        int buddyContactsBefore = magazine?.BuddyContactCount ?? 0;
        Vector2 chest = lab.Buddy.Rig.Torso.GlobalPosition;
        Vector2 chestBefore = chest;
        if (magazine is not null && GodotObject.IsInstanceValid(magazine))
        {
            magazine.GlobalPosition = chest;
            magazine.LinearVelocity = new Vector2(0.0f, 40.0f);
            magazine.Sleeping = false;
            magazine.ResetPhysicsInterpolation();
        }

        await Idle(tree, gun, 30);
        float chestMoved = lab.Buddy.Rig.Torso.GlobalPosition.DistanceTo(chestBefore);

        checks.Add(new StartupCheck(
            "dropped_magazine_lands_and_cannot_touch_the_buddy",
            dropped &&
            landed &&
            stayedOnFloor &&
            magazine is { SawUpwardBounce: true } &&
            layer == 0u &&
            mask == CollisionLayers.RoomBounds &&
            lab.Pipeline.ScoredImpactCount == scoredBefore &&
            (magazine?.BuddyContactCount ?? 0) == buddyContactsBefore,
            $"dropped={dropped} landed={landed} stayed={stayedOnFloor} " +
            $"bounced={magazine?.SawUpwardBounce} y={magazine?.GlobalPosition.Y:F2} " +
            $"layer={layer} mask={mask} " +
            $"room_bounds_mask={CollisionLayers.RoomBounds} " +
            $"scored={scoredBefore}->{lab.Pipeline.ScoredImpactCount} " +
            $"buddy_contacts={buddyContactsBefore}->{magazine?.BuddyContactCount ?? 0} " +
            $"chest_moved={chestMoved:F2}px"));

        checks.Add(new StartupCheck(
            "real_pistol_reload_plays_randomized_audio",
            lab.ReactionAudio.PistolReloadCount == pistolReloadCuesBefore + 1 &&
            lab.ReactionAudio.LastPlayedStream is AudioStreamRandomizer,
            $"cues={lab.ReactionAudio.PistolReloadCount - pistolReloadCuesBefore} " +
            $"stream={lab.ReactionAudio.LastPlayedStream?.GetType().Name}"));

        checks.Add(new StartupCheck(
            "dropped_magazine_never_registers_as_a_loose_object",
            registryDuring == registryBefore && lab.Objects.Count == registryBefore,
            $"registry_before={registryBefore} during={registryDuring} " +
            $"after={lab.Objects.Count} magazines_dropped={gun.MagazinesDropped}"));

        // --- It leaves on its own ---
        bool repooled = await M4ObjectScenarioSupport.WaitFor(
            tree, () => gun.ActiveMagazineCount == 0, pistol.MagazineLingerTicks + 60);
        checks.Add(new StartupCheck(
            "dropped_magazines_return_to_their_pool",
            repooled && gun.MagazinesDropped > 0,
            $"repooled={repooled} live={gun.ActiveMagazineCount} " +
            $"dropped={gun.MagazinesDropped} linger={pistol.MagazineLingerTicks}"));

        // --- The toy authors none of it, and shows none of it ---
        int pistolShotCuesAfterRealPistol = lab.ReactionAudio.PistolShotCount;
        int pistolReloadCuesAfterRealPistol = lab.ReactionAudio.PistolReloadCount;
        await SelectAndAim(tree, lab, gun, ToolId.NerfBlaster, lane, Vector2.Right);
        GunProfile nerf = gun.ActiveProfile!;
        lab.CameraKick.ResetPeak();
        int nerfKicksBefore = lab.CameraKick.KickCount;
        int nerfMagazinesBefore = gun.MagazinesDropped;
        float nerfFlashPeak = 0.0f;
        await EmptyMagazine(tree, gun, nerf, sample: () =>
            nerfFlashPeak = Mathf.Max(nerfFlashPeak, gun.MuzzleFlashStrength));
        gun.SetTriggerHeld(true);
        await Tick(tree);
        gun.SetTriggerHeld(false);
        await Idle(tree, gun, 30);

        checks.Add(new StartupCheck(
            "the_nerf_blaster_authors_no_punctuation_and_shows_none",
            Mathf.IsZeroApprox(nerf.FireShakeAmplitudePx) &&
            nerf.MuzzleFlashTicks == 0 &&
            !nerf.DropsMagazineOnReload &&
            lab.CameraKick.KickCount == nerfKicksBefore &&
            lab.CameraKick.PeakOffsetPx <= 0.0f &&
            nerfFlashPeak <= 0.0f &&
            gun.MagazinesDropped == nerfMagazinesBefore &&
            lab.ReactionAudio.PistolShotCount == pistolShotCuesAfterRealPistol &&
            lab.ReactionAudio.PistolReloadCount == pistolReloadCuesAfterRealPistol,
            $"authored_shake={nerf.FireShakeAmplitudePx} flash_ticks={nerf.MuzzleFlashTicks} " +
            $"drops_magazine={nerf.DropsMagazineOnReload} kicks={lab.CameraKick.KickCount - nerfKicksBefore} " +
            $"peak={lab.CameraKick.PeakOffsetPx:F3}px flash_peak={nerfFlashPeak:F2} " +
            $"magazines={gun.MagazinesDropped - nerfMagazinesBefore} " +
            $"pistol_audio={lab.ReactionAudio.PistolShotCount}/{lab.ReactionAudio.PistolReloadCount}"));

        messages.Add(
            $"shake_restart_peak={restartPeak:F3}px over {restartKicks} live restarts; " +
            $"real_shot_peak={burstPeak:F3}px over {kicks} shots (bound {envelopeBound:F3}) " +
            $"flash_peak={flashPeak:F2} magazines_dropped={gun.MagazinesDropped}");

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task Tick(SceneTree tree) =>
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

    private static async Task Idle(SceneTree tree, CursorGunComponent gun, int ticks)
    {
        gun.SetTriggerHeld(false);
        for (int tick = 0; tick < ticks; tick++)
            await Tick(tree);
    }

    private static async Task SelectAndAim(
        SceneTree tree,
        BuddyLab lab,
        CursorGunComponent gun,
        ToolId tool,
        Vector2 cursor,
        Vector2 direction)
    {
        lab.Pipeline.SelectTool(tool);
        await Tick(tree);
        await M4ObjectScenarioSupport.AimGunOver(tree, gun, cursor, direction);
    }

    /// <summary>Fires the magazine dry at the authored cadence, sampling as it goes.</summary>
    private static async Task EmptyMagazine(
        SceneTree tree,
        CursorGunComponent gun,
        GunProfile profile,
        System.Action? sample = null)
    {
        while (gun.RoundsRemaining > 0 && !gun.IsReloading)
        {
            gun.SetTriggerHeld(true);
            await Tick(tree);
            gun.SetTriggerHeld(false);
            for (int tick = 0; tick < profile.ShotIntervalTicks; tick++)
            {
                sample?.Invoke();
                await Tick(tree);
            }
        }
    }

    private static MagazineBody? FindLiveMagazine(CursorGunComponent gun)
    {
        foreach (Node child in gun.GetChildren())
        {
            if (child is MagazineBody { IsLive: true } magazine)
                return magazine;
        }

        return null;
    }
}
