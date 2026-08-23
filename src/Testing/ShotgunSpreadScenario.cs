using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M5 Task 9 — the Shotgun, measured against RAGDOLL §9.2 and the plan's owner-accepted
/// dedup interpretation. The cadence half of the slice is a profile table on the existing
/// <see cref="GunMachine"/>, so what this scenario really exists to prove is the part that
/// is <b>not</b> data:
///
/// <list type="bullet">
///   <item>one press releases six pellets on the authored deterministic fan — angles
///   matching the platform's index formula exactly, no randomness;</item>
///   <item>every pellet of one press carries <b>one</b> interaction identity, so six
///   pellets into one part is one accepted impact rather than six, and a burst across
///   several parts scores once per covered part (§7.1–7.2 episode key);</item>
///   <item>one pellet hurts less than a pistol bullet, because a burst is six of them;</item>
///   <item>point blank, no pellet passes through what it was fired at;</item>
///   <item>the shotgun's authored punctuation reads bigger than the pistol's and still
///   cannot stack, and every shot ejects a shell that can never touch the buddy.</item>
/// </list>
///
/// <para>The per-part counts are <b>measured and reported</b>, never assumed to be six.
/// The buddy is alive and walks about between shots, so which parts a mid-range fan covers
/// is a fact about that moment; what is pinned is the invariant — accepted impacts equal
/// covered parts — plus the coverage being real (more than one part).</para>
/// </summary>
public sealed class ShotgunSpreadScenario : IScenario
{
    /// <summary>Ticks a burst's flight and settling is watched for before it is judged.</summary>
    private const int FlightTicks = 100;

    /// <summary>Shots fired back to back at the kick, to prove envelopes do not sum.</summary>
    private const int KickShots = 3;

    /// <summary>Attempts a measured shot makes to find a square, in-range geometry.</summary>
    private const int AimAttempts = 8;

    /// <summary>
    /// How far off the aimed point the centre pellet may pass and still count for the
    /// coverage burst. Generous on purpose: that burst is aimed at the torso, which is
    /// 28 px of target, and a tighter gate simply spends every attempt waiting for a
    /// walking buddy to stand where it stood half a second ago.
    /// </summary>
    private const float CoverageTolerancePx = 18.0f;

    /// <summary>
    /// The same tolerance for the point-blank burst, which is aimed at a 24 px head and
    /// has to land square: a graze off the rim measures the buddy's pose rather than the
    /// pellet, and the pain band recorded here is the one the owner tunes against.
    /// </summary>
    private const float PointBlankTolerancePx = 5.0f;

    /// <summary>Torso speed, in px/s, under which the buddy counts as standing still.</summary>
    private const float SettledSpeedPx = 8.0f;

    public string Id => "shotgun_spread";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("shotgun_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        CursorGunComponent gun = lab.CursorGuns;
        gun.ReseedSpread(seed);
        Rect2 room = lab.Boundaries.InnerBounds;

        // The real lab key, not a direct SelectTool: a tool nobody can reach is not
        // implemented, and `L` is the key the journey and the owner both press.
        gun.MoveCursor(room.GetCenter());
        await Tick(tree);
        await M4ObjectScenarioSupport.SendKey(tree, Key.L);
        await Tick(tree);

        // A firing lane on whichever side of the room the buddy is not on, pointed at the
        // near wall so every shell buries itself a few pixels away. The cadence, fan, and
        // kick legs are about what leaves the barrel; a lane that happened to cross the
        // buddy would knock it off its feet before the coverage measurement, and a prone
        // target presents entirely different parts to the fan. (It did exactly that: an
        // earlier "high and level" lane was level with a standing buddy's head.)
        Vector2 laneAim = lab.Buddy.Rig.Torso.GlobalPosition.X < room.GetCenter().X
            ? Vector2.Right
            : Vector2.Left;
        var lane = new Vector2(
            laneAim == Vector2.Right ? room.End.X - 100.0f : room.Position.X + 100.0f,
            room.GetCenter().Y);
        await AimAt(tree, gun, lane, laneAim);

        GunProfile? shotgun = gun.ActiveProfile;
        checks.Add(new StartupCheck(
            "the_lab_key_draws_a_loaded_shotgun",
            lab.Pipeline.SelectedTool == ToolId.Shotgun &&
            lab.Progress.IsToolUnlocked(ContentIds.ToolShotgun) &&
            gun.IsActive &&
            gun.ActiveContentId == ContentIds.ToolShotgun &&
            shotgun is not null &&
            gun.RoundsRemaining == shotgun.MagazineCapacity &&
            !gun.IsReloading,
            $"selected={lab.Pipeline.SelectedTool} owned=" +
            $"{lab.Progress.IsToolUnlocked(ContentIds.ToolShotgun)} active={gun.IsActive} " +
            $"content={gun.ActiveContentId} rounds={gun.RoundsRemaining}"));

        if (shotgun is null)
        {
            lab.QueueFree();
            return new ScenarioResult(false, checks, messages);
        }

        // --- The authored contract, restated against the real profile ---
        checks.Add(new StartupCheck(
            "the_authored_profile_matches_the_specification",
            // Re-authored 2026-08-22 (owner): faster cadence, a magazine long enough that the
            // reload stops reading as a cooldown, and a reload short enough to disappear.
            shotgun.MagazineCapacity == 12 &&
            shotgun.ShotIntervalTicks == 42 &&
            shotgun.ReloadTicks == 36 &&
            shotgun.ProjectilesPerShot == 6 &&
            Mathf.IsEqualApprox(shotgun.SpreadHalfAngleDegrees, 12.0f) &&
            Mathf.IsEqualApprox(shotgun.SpreadMaxHalfAngleDegrees, 20.0f) &&
            shotgun.ScattersPerShot &&
            shotgun.RequiresPumpBetweenShots &&
            shotgun.EjectsCasingOnShot &&
            !shotgun.DropsMagazineOnReload &&
            // Doubled from 3600 on 2026-08-22 (owner): the shove is what the shell reads as.
            shotgun.ContactShoveAtPointBlank * shotgun.ProjectilesPerShot == 7200.0f &&
            shotgun.PoolCapacity >= shotgun.MagazineCapacity * shotgun.ProjectilesPerShot,
            $"capacity={shotgun.MagazineCapacity} interval={shotgun.ShotIntervalTicks}ticks " +
            $"({shotgun.ShotIntervalTicks / 120.0f:F2}s) reload={shotgun.ReloadTicks}ticks " +
            $"({shotgun.ReloadTicks / 120.0f:F2}s) pellets={shotgun.ProjectilesPerShot} " +
            $"spread={shotgun.SpreadHalfAngleDegrees:F1}-{shotgun.SpreadMaxHalfAngleDegrees:F1}deg " +
            $"pump={shotgun.PumpTicks}ticks casing={shotgun.EjectsCasingOnShot} " +
            $"point_blank_shove={shotgun.ContactShoveAtPointBlank}x{shotgun.ProjectilesPerShot} " +
            $"pool={shotgun.PoolCapacity}"));

        // --- Mid range: the fan spreads, and every covered part scores once ---
        // The plan's coverage model, measured rather than assumed. Fired at the head/torso
        // seam from the range where the authored random cone opens across roughly a
        // buddy's chest and can straddle more than one part.
        //
        // Measured first, on an untouched buddy, and that ordering is load-bearing. Later
        // legs empty magazines into a wall, and pellets that ricochet off it are still
        // hard enough to knock a buddy over even though they are far too soft to score.
        // Every earlier arrangement of this scenario measured coverage against a target
        // lying on the floor, which presents a completely different set of parts to a
        // shot aimed where a standing buddy's chest would be.
        Coverage coverage = await MeasureCoverage(tree, lab, gun, shotgun);
        checks.Add(new StartupCheck(
            "a_mid_range_burst_scores_once_per_covered_part",
            // One part is enough since the pellets were halved (owner 2026-08-22): a
            // six-pixel pellet slips between parts where a twelve-pixel one straddled them,
            // so a burst covering two is no longer something every seed produces. What the
            // spread must still never do is score one part twice, which the per-burst flag
            // below is the real statement of.
            coverage.Best.Connections > 0 &&
            coverage.Best.CoveredParts >= 1 &&
            coverage.Best.Accepted == coverage.Best.CoveredParts &&
            coverage.EveryBurstScoredEachPartOnce &&
            coverage.Best.BestPain > 0.0f,
            $"bursts={coverage.Bursts} landed={coverage.Landed} " +
            $"best_covered_parts={coverage.Best.CoveredParts} " +
            $"best_accepted={coverage.Best.Accepted} " +
            $"one_episode_per_part_every_burst={coverage.EveryBurstScoredEachPartOnce} " +
            $"total_pain={coverage.Best.TotalPain:F2} range={coverage.Best.RangePx:F1}px " +
            $"{coverage.Report}"));

        // Back to the wall lane, with the action worked, for the mechanical legs. The gun
        // owes a pump for the last coverage shell: it never reloads any more, and it was the
        // reload that used to leave a fresh chamber here, so the stroke has to be paid
        // explicitly or the next press spends itself working the action.
        await SettleProjectiles(tree, gun, shotgun);
        await SelectAndAim(tree, lab, gun, ToolId.Shotgun, lane, laneAim);
        if (gun.NeedsPump)
        {
            await PressTrigger(tree, gun);
            await Idle(tree, gun, shotgun.PumpTicks);
        }

        await Idle(tree, gun, shotgun.ShotIntervalTicks);
        await M4ObjectScenarioSupport.WaitFor(
            tree, () => gun.ActiveMagazineCount == 0, shotgun.MagazineLingerTicks + 60);

        // --- One press, six pellets, inside this shot's randomized cone ---
        int launchedBefore = gun.ProjectilesLaunched;
        int casingsBeforeMagazine = gun.CasingsEjected;
        int shotsBeforeMagazine = gun.ShotCount;
        int registryBefore = lab.Objects.Count;
        Vector2 forward = gun.AimForward;
        gun.SetTriggerHeld(true);
        await Tick(tree);
        gun.SetTriggerHeld(false);

        List<ProjectileBody> volley = LivePellets(gun);
        int sharedId = gun.LastShotInteractionId;
        bool oneIdentity = volley.Count > 0;
        foreach (ProjectileBody pellet in volley)
            oneIdentity &= pellet.InteractionId == sharedId && sharedId != 0;

        float firstCone = gun.LastShotSpreadHalfAngleDegrees;
        (bool fanMatches, string fanReport) = CompareScatter(volley, forward, shotgun, firstCone);
        int registryPeak = lab.Objects.Count;
        checks.Add(new StartupCheck(
            "six_pellets_leave_on_one_press_inside_a_randomized_cone",
            volley.Count == shotgun.ProjectilesPerShot &&
            gun.ProjectilesLaunched - launchedBefore == shotgun.ProjectilesPerShot &&
            gun.RoundsRemaining == shotgun.MagazineCapacity &&
            gun.PoolExhaustedCount == 0 &&
            registryPeak == registryBefore &&
            fanMatches,
            $"live={volley.Count} launched={gun.ProjectilesLaunched - launchedBefore} " +
            $"rounds={gun.RoundsRemaining} exhausted={gun.PoolExhaustedCount} " +
            $"registry={registryBefore}->{registryPeak} | {fanReport}"));

        // The whole point of the seam: six pellets, one interaction.
        checks.Add(new StartupCheck(
            "every_pellet_of_one_press_shares_one_interaction_id",
            oneIdentity,
            $"shared_id={sharedId} ids=" + string.Join(
                ",", volley.ConvertAll(pellet => pellet.InteractionId.ToString()))));

        // --- Cadence: pump and shoot, for longer than any magazine would last ---
        // The Shotgun authors InfiniteMagazine (owner instruction 2026-08-22), so this leg
        // fires well past what the old five-shell magazine held and asserts that no reload
        // and no dry fire ever appear.
        // Continues straight on from the shell just fired, so the half-interval press at
        // the top of each pass is really half an interval after the last shot.
        int shotsBefore = gun.ShotCount;
        int reloadsBefore = gun.ReloadStartCount;
        int dryFiresBefore = gun.DryFireCount;
        launchedBefore = gun.ProjectilesLaunched;
        int projectilePeak = 0;
        float worstLaunchGapPx = 0.0f;
        int pumpStartsBefore = gun.PumpStartCount;
        float pumpSlidePeak = 0.0f;
        var cones = new HashSet<int> { Mathf.RoundToInt(firstCone * 1000.0f) };
        const int cadenceShots = 14;
        // The gun is walked between two widely separated spots as it fires, and that is what
        // makes this leg able to see a pooled pellet starting from its last spawn point: with
        // the cursor parked, an old spawn and a new one are the same place and the fault is
        // invisible (owner bug 2026-08-22).
        Vector2 lanePort = lane + new Vector2(-140.0f, 0.0f);
        Vector2 laneStarboard = lane + new Vector2(140.0f, 0.0f);
        for (int round = 1; round <= cadenceShots; round++)
        {
            await AimAt(tree, gun, round % 2 == 0 ? lanePort : laneStarboard, laneAim);
            // The click after every shot works the action; it never fires a second shell.
            await Idle(tree, gun, 1);
            int shotsAtPump = gun.ShotCount;
            await PressTrigger(tree, gun);
            bool pumpStarted = gun.IsPumping && gun.ShotCount == shotsAtPump;
            await Idle(tree, gun, shotgun.PumpTicks / 2);
            pumpSlidePeak = Mathf.Max(pumpSlidePeak, gun.PumpSlideOffsetPx);
            await Idle(tree, gun, shotgun.PumpTicks - (shotgun.PumpTicks / 2));

            await Idle(tree, gun, shotgun.ShotIntervalTicks);
            // Sampled from the press itself, one tick at a time, rather than once after it:
            // pellets cross the lane in a handful of ticks at the authored muzzle speed, so a
            // reading taken after the two-tick press can fall entirely between launch and
            // re-pooling and see nothing in the air at all.
            gun.SetTriggerHeld(true);
            for (int sample = 0; sample < 10; sample++)
            {
                if (sample == 1)
                    gun.SetTriggerHeld(false);
                await Tick(tree);
                projectilePeak = Mathf.Max(projectilePeak, gun.ActiveProjectileCount);
                registryPeak = Mathf.Max(registryPeak, lab.Objects.Count);

                // Straight after the shot, while the pellets have a tick or two of travel on
                // them at most, each one has to be near where it was told to start. A pooled
                // body that kept the physics server's old transform starts its second flight
                // wherever its first one died instead, which is a room away.
                if (sample > 1)
                    continue;
                foreach (ProjectileBody pellet in LivePellets(gun))
                {
                    worstLaunchGapPx = Mathf.Max(
                        worstLaunchGapPx,
                        pellet.GlobalPosition.DistanceTo(pellet.LaunchPosition));
                }
            }
            cones.Add(Mathf.RoundToInt(gun.LastShotSpreadHalfAngleDegrees * 1000.0f));
            if (!pumpStarted)
                break;
        }

        checks.Add(new StartupCheck(
            "pump_and_shoot_runs_on_without_ever_reloading",
            shotgun.InfiniteMagazine &&
            gun.ShotCount - shotsBefore == cadenceShots &&
            gun.PumpStartCount - pumpStartsBefore == cadenceShots &&
            pumpSlidePeak > 0.0f && !gun.IsPumping && gun.PumpSlideOffsetPx == 0.0f &&
            cones.Count > 1 &&
            gun.ProjectilesLaunched - launchedBefore ==
                cadenceShots * shotgun.ProjectilesPerShot &&
            gun.RoundsRemaining == shotgun.MagazineCapacity &&
            gun.DryFireCount == dryFiresBefore &&
            gun.ReloadStartCount == reloadsBefore &&
            !gun.IsReloading,
            $"fired={gun.ShotCount - shotsBefore} pumps={gun.PumpStartCount - pumpStartsBefore} " +
            $"pump_slide_peak={pumpSlidePeak:F1}px distinct_cones={cones.Count} " +
            $"pellets={gun.ProjectilesLaunched - launchedBefore} rounds={gun.RoundsRemaining} " +
            $"dry_fires={gun.DryFireCount - dryFiresBefore} " +
            $"infinite={shotgun.InfiniteMagazine} pumping={gun.IsPumping} " +
            $"slide={gun.PumpSlideOffsetPx:F2} reloading={gun.IsReloading} " +
            $"reload_starts={gun.ReloadStartCount - reloadsBefore}"));

        // Every shell in that run reused pool slots — 14 shots of six pellets against a pool
        // of 72 — so this is the leg that catches a slot starting from its last spawn point
        // (owner bug 2026-08-22).
        float launchTolerancePx = shotgun.MuzzleSpeed / Engine.PhysicsTicksPerSecond * 3.0f;
        checks.Add(new StartupCheck(
            "a_reused_pellet_starts_at_the_muzzle_and_not_at_its_last_spawn",
            worstLaunchGapPx > 0.0f && worstLaunchGapPx <= launchTolerancePx,
            $"worst_gap={worstLaunchGapPx:F1}px tolerance={launchTolerancePx:F1}px " +
            $"pool={shotgun.PoolCapacity} pellets_fired={cadenceShots * shotgun.ProjectilesPerShot}"));

        // Pellets are bounded by their own pool and never enter the FR-014 budget.
        checks.Add(new StartupCheck(
            "pellets_never_consume_a_loose_object_slot",
            registryPeak == registryBefore &&
            projectilePeak > 0 &&
            projectilePeak <= shotgun.PoolCapacity &&
            gun.PoolExhaustedCount == 0,
            $"registry_before={registryBefore} registry_peak={registryPeak} " +
            $"pellet_peak={projectilePeak} pool={shotgun.PoolCapacity} " +
            $"exhausted={gun.PoolExhaustedCount}"));

        // --- The press after all that still fires, because there is nothing to run out of ---
        int dryBefore = gun.DryFireCount;
        int shotsBeforeExtra = gun.ShotCount;
        // Still owes the action a stroke for the shell it just fired, exactly as before.
        await PressTrigger(tree, gun);
        await Idle(tree, gun, shotgun.PumpTicks + shotgun.ShotIntervalTicks);
        await PressTrigger(tree, gun);
        checks.Add(new StartupCheck(
            "the_press_after_an_old_magazines_worth_still_fires",
            gun.ShotCount == shotsBeforeExtra + 1 &&
            gun.DryFireCount == dryBefore &&
            !gun.IsReloading &&
            gun.ReloadStartCount == reloadsBefore,
            $"fired={gun.ShotCount - shotsBeforeExtra} dry_fires={gun.DryFireCount - dryBefore} " +
            $"reloading={gun.IsReloading} reload_starts={gun.ReloadStartCount - reloadsBefore}"));

        // --- Every shot's ejected shell rides the cosmetic casing lane ---
        // Verbatim the pistol magazine's rules: on no collision layer at all, masked only
        // against the room bounds, invisible to the pain pipeline and to the loose-object
        // registry, and it leaves on its own.
        bool ejected = gun.ActiveCasingCount > 0;
        MagazineBody? shell = FindLiveCasing(gun);
        uint shellLayer = shell?.CollisionLayer ?? 1u;
        uint shellMask = shell?.CollisionMask ?? 0u;
        long scoredBeforeProbe = lab.Pipeline.ScoredImpactCount;
        int shellContactsBefore = shell?.BuddyContactCount ?? 0;
        if (shell is not null && GodotObject.IsInstanceValid(shell))
        {
            // A contact probe rather than a mask reading alone: put it on the chest and
            // give it time to resolve. Nothing at all may happen.
            shell.GlobalPosition = lab.Buddy.Rig.Torso.GlobalPosition;
            shell.LinearVelocity = new Vector2(0.0f, 40.0f);
            shell.Sleeping = false;
            shell.ResetPhysicsInterpolation();
        }

        await Idle(tree, gun, 30);
        checks.Add(new StartupCheck(
            "every_shot_ejects_a_red_shell_that_cannot_touch_the_buddy",
            shotgun.EjectsCasingOnShot &&
            !shotgun.DropsMagazineOnReload &&
            ejected &&
            // One shell out per shot fired, whatever the magazine says — it never empties.
            gun.CasingsEjected - casingsBeforeMagazine == gun.ShotCount - shotsBeforeMagazine &&
            shell?.IsCasing == true &&
            shellLayer == 0u &&
            shellMask == CollisionLayers.RoomBounds &&
            lab.Pipeline.ScoredImpactCount == scoredBeforeProbe &&
            (shell?.BuddyContactCount ?? 0) == shellContactsBefore &&
            lab.Objects.Count == registryBefore,
            $"authored={shotgun.EjectsCasingOnShot} ejected={ejected} " +
            $"casings={gun.CasingsEjected - casingsBeforeMagazine} is_casing={shell?.IsCasing} layer={shellLayer} " +
            $"mask={shellMask} room_bounds={CollisionLayers.RoomBounds} " +
            $"scored={scoredBeforeProbe}->{lab.Pipeline.ScoredImpactCount} " +
            $"buddy_contacts={shellContactsBefore}->{shell?.BuddyContactCount ?? 0} " +
            $"registry={lab.Objects.Count}"));

        bool shellRepooled = await M4ObjectScenarioSupport.WaitFor(
            tree, () => gun.ActiveCasingCount == 0, shotgun.MagazineLingerTicks + 60);
        checks.Add(new StartupCheck(
            "every_shell_returns_to_its_pool_and_the_gun_stays_loaded",
            shellRepooled &&
            !gun.IsReloading &&
            gun.RoundsRemaining == shotgun.MagazineCapacity,
            $"rounds={gun.RoundsRemaining} live_shells={gun.ActiveCasingCount} " +
            $"linger={shotgun.MagazineLingerTicks}"));

        await SettleProjectiles(tree, gun, shotgun);
        checks.Add(new StartupCheck(
            "spent_and_expired_pellets_return_to_the_pool",
            gun.ActiveProjectileCount == 0 && lab.Objects.Count == registryBefore,
            $"active={gun.ActiveProjectileCount} registry={lab.Objects.Count}"));

        // --- Point blank: one part, one accepted impact, and nothing passes through ---
        await SettleProjectiles(tree, gun, shotgun);
        Burst pointBlank = await MeasurePointBlank(tree, lab, gun, shotgun);
        checks.Add(new StartupCheck(
            "point_blank_one_part_scores_exactly_once",
            // One accepted impact per part the fan covers, whichever parts those are. The
            // pellets are wide enough since the 2026-08-22 re-author to straddle the head and
            // torso even at point blank; what must never happen is one part scoring twice.
            pointBlank.Connections > 0 &&
            pointBlank.CoveredParts >= 1 &&
            pointBlank.Accepted == pointBlank.CoveredParts &&
            pointBlank.WorstPartRepeats == 1,
            $"pellets_connected={pointBlank.Connections}/{shotgun.ProjectilesPerShot} " +
            $"covered_parts={pointBlank.CoveredParts} accepted={pointBlank.Accepted} " +
            $"most_accepted_on_one_part={pointBlank.WorstPartRepeats} " +
            $"episodes={pointBlank.Episodes} pain={pointBlank.BestPain:F2} " +
            $"{pointBlank.Report}"));

        checks.Add(new StartupCheck(
            "point_blank_pellets_never_tunnel_through_the_target",
            pointBlank.Connections > 0 && !pointBlank.Tunneled,
            $"connected={pointBlank.Connections} tunneled={pointBlank.Tunneled} " +
            $"deepest_travel={pointBlank.DeepestTravelPx:F1}px " +
            $"far_surface={pointBlank.FarSurfacePx:F1}px ccd={pointBlank.CcdMode}"));

        float midShove = shotgun.ContactShoveAfter(
            (shotgun.ContactShoveFullRangePx + shotgun.ContactShoveZeroRangePx) * 0.5f);
        checks.Add(new StartupCheck(
            "shotgun_knockback_falls_with_travel_but_never_below_the_old_physical_hit",
            Mathf.IsEqualApprox(
                shotgun.ContactShoveAfter(0.0f) * shotgun.ProjectilesPerShot,
                7200.0f) &&
            midShove > 0.0f && midShove < shotgun.ContactShoveAtPointBlank &&
            shotgun.ContactShoveAfter(shotgun.ContactShoveZeroRangePx) == 0.0f &&
            gun.PeakShoveImpulse > 0.0f &&
            gun.PeakShoveImpulse <= shotgun.ContactShoveAtPointBlank,
            $"point_blank={shotgun.ContactShoveAfter(0.0f):F1}x{shotgun.ProjectilesPerShot} " +
            $"mid={midShove:F1} far={shotgun.ContactShoveAfter(shotgun.ContactShoveZeroRangePx):F1} " +
            $"delivered_peak={gun.PeakShoveImpulse:F1}; zero extra leaves the original contact impulse"));

        // --- One pellet hurts less than one bullet ---
        // Through the shared curve only: the difference is authored muzzle speed and
        // pellet size, and a burst is six of these. Recorded as a band rather than a
        // fixed number, because the target is alive and the contact geometry moves.
        await SettleProjectiles(tree, gun, shotgun);
        Burst pistol = await FirePistolPointBlank(tree, lab, gun);
        checks.Add(new StartupCheck(
            "a_single_pellet_sits_below_a_pistol_bullet",
            pointBlank.BestPain > 0.0f &&
            pistol.BestPain > 0.0f &&
            pointBlank.BestPain < pistol.BestPain,
            $"pellet_pain={pointBlank.BestPain:F2} (impulse " +
            $"{pointBlank.BestImpulse:F1}, muzzle {shotgun.MuzzleSpeed}, " +
            $"mass {shotgun.ProjectileMass}) bullet_pain={pistol.BestPain:F2} " +
            $"(impulse {pistol.BestImpulse:F1}) best_burst_pain={coverage.Best.TotalPain:F2}"));

        // --- The kick reads bigger than the pistol's, and still cannot stack ---
        await SettleProjectiles(tree, gun, shotgun);
        GunProfile? pistolProfile = ProfileFor(gun, ContentIds.ToolPistol);
        await SelectAndAim(tree, lab, gun, ToolId.Shotgun, lane, laneAim);
        lab.CameraKick.ResetPeak();
        Vector2 cameraBase = lab.Boundaries.WorldCamera.Position;
        int kicksBefore = lab.CameraKick.KickCount;
        float flashPeak = 0.0f;
        for (int shot = 0; shot < KickShots; shot++)
        {
            await FireReadyShot(tree, gun, shotgun);
            for (int tick = 0; tick < shotgun.ShotIntervalTicks; tick++)
            {
                flashPeak = Mathf.Max(flashPeak, gun.MuzzleFlashStrength);
                await Tick(tree);
            }
        }

        float kickPeak = lab.CameraKick.PeakOffsetPx;
        int kicks = lab.CameraKick.KickCount - kicksBefore;
        // Both axes are bounded by the amplitude, so one envelope reaches at most
        // amplitude * sqrt(2) from centre; past that is two envelopes that summed.
        float envelopeBound = shotgun.FireShakeAmplitudePx * 1.45f;
        await Idle(tree, gun, shotgun.FireShakeDecayTicks + 8);
        checks.Add(new StartupCheck(
            "the_shotgun_kick_reads_bigger_than_the_pistol_and_never_stacks",
            pistolProfile is not null &&
            shotgun.FireShakeAmplitudePx > pistolProfile.FireShakeAmplitudePx &&
            shotgun.RecoilKickPx > pistolProfile.RecoilKickPx &&
            kicks == KickShots &&
            kickPeak > pistolProfile.FireShakeAmplitudePx &&
            kickPeak <= envelopeBound &&
            flashPeak > 0.6f &&
            !lab.CameraKick.IsKicking &&
            lab.Boundaries.WorldCamera.Position.DistanceTo(cameraBase) < 0.001f,
            $"shake={shotgun.FireShakeAmplitudePx} vs pistol " +
            $"{pistolProfile?.FireShakeAmplitudePx} recoil={shotgun.RecoilKickPx} vs " +
            $"{pistolProfile?.RecoilKickPx} kicks={kicks} peak={kickPeak:F3}px " +
            $"bound={envelopeBound:F3}px flash_peak={flashPeak:F2} " +
            $"camera_returned=" +
            $"{lab.Boundaries.WorldCamera.Position.DistanceTo(cameraBase):F4}px"));

        checks.Add(new StartupCheck(
            "the_shotgun_is_remembered_as_harmful",
            lab.Progress.IsContentHarmful(ContentIds.ToolShotgun) &&
            CountFor(lab.Progress.Statistics.ToolPainMilli, ContentIds.ToolShotgun) > 0L,
            $"harmful={lab.Progress.IsContentHarmful(ContentIds.ToolShotgun)} " +
            $"pain_milli=" +
            $"{CountFor(lab.Progress.Statistics.ToolPainMilli, ContentIds.ToolShotgun)}"));

        messages.Add(
            $"point_blank: parts={pointBlank.CoveredParts} accepted={pointBlank.Accepted} " +
            $"pain={pointBlank.BestPain:F2} impulse={pointBlank.BestImpulse:F1}");
        messages.Add(
            $"coverage: best_parts={coverage.Best.CoveredParts} " +
            $"accepted={coverage.Best.Accepted} total_pain={coverage.Best.TotalPain:F2} " +
            $"landed={coverage.Landed}/{coverage.Bursts} | {coverage.Report}");
        messages.Add(
            $"pellet_pain={pointBlank.BestPain:F2} bullet_pain={pistol.BestPain:F2} " +
            $"muzzle={shotgun.MuzzleSpeed} mass={shotgun.ProjectileMass} " +
            $"radius={shotgun.ProjectileRadius}");

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static long CountFor(IReadOnlyDictionary<string, long>? counters, string contentId) =>
        counters is not null && counters.TryGetValue(contentId, out long value) ? value : 0L;

    private static async Task Tick(SceneTree tree) =>
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

    private static async Task AimAt(
        SceneTree tree,
        CursorGunComponent gun,
        Vector2 cursor,
        Vector2 direction) =>
        await M4ObjectScenarioSupport.AimGunOver(tree, gun, cursor, direction);

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
        await AimAt(tree, gun, cursor, direction);
    }

    /// <summary>One complete trigger pull: pressed for a tick, then released.</summary>
    private static async Task PressTrigger(SceneTree tree, CursorGunComponent gun)
    {
        gun.SetTriggerHeld(true);
        await Tick(tree);
        gun.SetTriggerHeld(false);
        await Tick(tree);
    }

    private static async Task FireReadyShot(
        SceneTree tree,
        CursorGunComponent gun,
        GunProfile profile)
    {
        if (gun.NeedsPump)
        {
            await PressTrigger(tree, gun);
            await Idle(tree, gun, profile.PumpTicks);
        }

        await Idle(tree, gun, profile.ShotIntervalTicks);
        await PressTrigger(tree, gun);
    }

    private static async Task Idle(SceneTree tree, CursorGunComponent gun, int ticks)
    {
        gun.SetTriggerHeld(false);
        for (int tick = 0; tick < ticks; tick++)
            await Tick(tree);
    }

    private static async Task SettleProjectiles(
        SceneTree tree,
        CursorGunComponent gun,
        GunProfile profile)
    {
        gun.SetTriggerHeld(false);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => gun.ActiveProjectileCount == 0,
            profile.ProjectileLifetimeTicks + profile.SpentLingerTicks + 16);
    }

    private static GunProfile? ProfileFor(CursorGunComponent gun, string contentId)
    {
        foreach (GunProfile? profile in gun.Profiles)
        {
            if (GodotObject.IsInstanceValid(profile) && profile!.ContentId == contentId)
                return profile;
        }

        return null;
    }

    /// <summary>Every pellet currently in flight, whichever gun fired it.</summary>
    private static List<ProjectileBody> LivePellets(CursorGunComponent gun)
    {
        var live = new List<ProjectileBody>();
        foreach (Node child in gun.GetChildren())
        {
            if (child is ProjectileBody { State: ProjectileState.Live } pellet)
                live.Add(pellet);
        }

        return live;
    }

    /// <summary>
    /// Where each pellet of a burst ended up, for the detail line. A fan that measured
    /// nothing is otherwise indistinguishable from a fan that never left the barrel, and
    /// this scenario has been both.
    /// </summary>
    private static string PelletReport(List<ProjectileBody> volley, Vector2 muzzle)
    {
        var parts = new List<string>();
        foreach (ProjectileBody pellet in volley)
        {
            if (!GodotObject.IsInstanceValid(pellet))
                continue;

            parts.Add(
                $"{pellet.State}:hit={pellet.HasHit}:travel=" +
                $"{pellet.GlobalPosition.DistanceTo(muzzle):F0}");
        }

        return string.Join(" ", parts);
    }

    private static MagazineBody? FindLiveCasing(CursorGunComponent gun)
    {
        foreach (Node child in gun.GetChildren())
        {
            if (child is MagazineBody { IsLive: true, IsCasing: true } magazine)
                return magazine;
        }

        return null;
    }

    /// <summary>
    /// Compares the launched pellets against the platform's own fan formula — index
    /// fraction <c>2i/(n-1) - 1</c> of the authored half-angle — sorted by signed angle so
    /// the comparison does not depend on which pool slots happened to be free. An even
    /// deterministic fan is the owner-accepted default (plan §3.1); a scatter would show
    /// up here as angles that do not land on the authored ladder.
    /// </summary>
    private static (bool Matches, string Report) CompareScatter(
        List<ProjectileBody> volley,
        Vector2 forward,
        GunProfile profile,
        float cone)
    {
        int count = profile.ProjectilesPerShot;
        if (volley.Count != count || forward == Vector2.Zero)
            return (false, $"expected {count} pellets along {forward}, saw {volley.Count}");

        var measured = new List<float>();
        foreach (ProjectileBody pellet in volley)
        {
            Vector2 launch = pellet.LaunchVelocity;
            measured.Add(launch == Vector2.Zero
                ? float.NaN
                : Mathf.RadToDeg(forward.AngleTo(launch)));
        }

        measured.Sort();
        bool matches = cone >= profile.SpreadHalfAngleDegrees &&
            cone <= profile.SpreadMaxHalfAngleDegrees;
        foreach (float angle in measured)
            matches &= float.IsFinite(angle) && Mathf.Abs(angle) <= cone + 0.01f;

        return (matches, $"scatter cone={cone:F2}deg angles=[{string.Join(" ", measured.ConvertAll(a => a.ToString("F2")))}]");
    }

    /// <summary>
    /// Fires one aimed burst and reports what the whole shot did: how many pellets
    /// connected, which parts the pipeline accepted an impact on, and whether any pellet
    /// passed beyond the far surface of what it was aimed at without ever reporting a
    /// contact — the geometric tunneling test the pistol uses, applied to six bodies.
    /// </summary>
    /// <summary>
    /// Fires bursts at the head/torso seam until one really covers the buddy, and reports
    /// both the best of them and whether the dedup invariant held on <b>every</b> burst
    /// that landed.
    ///
    /// <para>A volley rather than one shot, for the reason the gun-split scenario gives:
    /// the target is alive. Establishing a cursor aim costs the best part of half a second
    /// of pointer travel, and a buddy that takes two steps during it turns a measured
    /// spread into a fan of pellets past its shoulder. The invariant under test —
    /// <i>one accepted impact per covered part, never two</i> — is checked on every burst
    /// that connected; the coverage count comes from the best of them, and every burst is
    /// reported so a run that only ever grazed is visible rather than silently weak.</para>
    /// </summary>
    private static async Task<Coverage> MeasureCoverage(
        SceneTree tree,
        BuddyLab lab,
        CursorGunComponent gun,
        GunProfile profile)
    {
        const int Attempts = 9;
        var best = default(Burst);
        bool invariant = true;
        int landed = 0;
        var report = new List<string>();
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            if (gun.RoundsRemaining == 0 && !gun.IsReloading)
                gun.RequestReload();

            if (gun.IsReloading)
            {
                await M4ObjectScenarioSupport.WaitFor(
                    tree,
                    () => !gun.IsReloading && gun.RoundsRemaining == profile.MagazineCapacity,
                    profile.ReloadTicks + 60);
            }

            Burst burst = await FireBurst(
                tree,
                lab,
                gun,
                profile,
                standOffPx: 150.0f,
                tolerancePx: CoverageTolerancePx,
                atHead: false);
            report.Add(
                $"#{attempt} parts={burst.CoveredParts} accepted={burst.Accepted} " +
                $"repeats={burst.WorstPartRepeats} pain={burst.TotalPain:F2} " +
                $"range={burst.RangePx:F0}px {burst.Report}");
            if (burst.Accepted > 0)
            {
                landed++;
                invariant &= burst.Accepted == burst.CoveredParts && burst.WorstPartRepeats == 1;
            }

            if (burst.CoveredParts > best.CoveredParts)
                best = burst;

            await SettleProjectiles(tree, gun, profile);
            if (best.CoveredParts >= 2)
                break;

            await Idle(tree, gun, profile.ShotIntervalTicks);
        }

        return new Coverage(Attempts, landed, invariant, best, string.Join(" | ", report));
    }

    /// <summary>
    /// Fires point-blank head bursts until one lands square, and keeps the strongest. The
    /// same volley reasoning as the coverage measurement: the target is alive, and a burst
    /// that grazed the rim of a 24 px head would record a pain band that says more about
    /// where the buddy was standing than about the pellet.
    /// </summary>
    private static async Task<Burst> MeasurePointBlank(
        SceneTree tree,
        BuddyLab lab,
        CursorGunComponent gun,
        GunProfile profile)
    {
        const int Attempts = 5;
        var best = default(Burst);
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            if (gun.RoundsRemaining == 0 && !gun.IsReloading)
                gun.RequestReload();

            if (gun.IsReloading)
            {
                await M4ObjectScenarioSupport.WaitFor(
                    tree,
                    () => !gun.IsReloading && gun.RoundsRemaining == profile.MagazineCapacity,
                    profile.ReloadTicks + 60);
            }

            Burst burst = await FireBurst(
                tree,
                lab,
                gun,
                profile,
                standOffPx: 40.0f,
                tolerancePx: PointBlankTolerancePx,
                atHead: true);
            if (burst.BestPain > best.BestPain)
                best = burst;

            await SettleProjectiles(tree, gun, profile);
            if (best.Accepted > 0)
                break;

            await Idle(tree, gun, profile.ShotIntervalTicks);
        }

        return best;
    }

    /// <summary>
    /// Where a measured burst is aimed: the head point blank, where a fan this narrow lands
    /// entirely on one part; the torso for the coverage burst, because the buddy's hands
    /// hang beside its chest, so a fan wide enough to reach past them at this range covers
    /// the hand <b>and</b> the body behind it.
    /// </summary>
    private static Vector2 AimPoint(BuddyLab lab, bool atHead) => atHead
        ? lab.Buddy.Rig.Head.GlobalPosition
        : lab.Buddy.Rig.Torso.GlobalPosition;

    private static async Task<Burst> FireBurst(
        SceneTree tree,
        BuddyLab lab,
        CursorGunComponent gun,
        GunProfile profile,
        float standOffPx,
        float tolerancePx,
        bool atHead)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 muzzle = Vector2.Zero;
        float range = 0.0f;
        float radius = 0.0f;
        float lateralError = float.MaxValue;
        for (int attempt = 0; attempt < AimAttempts; attempt++)
        {
            await M4ObjectScenarioSupport.WaitFor(
                tree,
                () => lab.Buddy.Rig.Torso.LinearVelocity.Length() < SettledSpeedPx,
                240);

            // Point blank aims at the head, where a fan this narrow lands entirely on one
            // part. The spread leg aims at the head/torso seam, which is where the same
            // fan straddles two.
            Vector2 target = AimPoint(lab, atHead);
            radius = atHead ? lab.Buddy.Rig.Head.Radius : lab.Buddy.Rig.Torso.Radius;
            (Vector2 cursor, Vector2 direction) = M4ObjectScenarioSupport.StandOffFrom(
                room, target, radius + standOffPx + profile.MuzzleOffsetPx);
            await SelectAndAim(tree, lab, gun, ToolId.Shotgun, cursor, direction);

            // Re-read the target: establishing an aim costs the best part of half a second
            // of pointer travel, and the buddy walks. Judging the shot against where it
            // stood before that sweep is how this measurement first fired a whole fan into
            // the wall behind a target that had stepped aside.
            target = AimPoint(lab, atHead);
            muzzle = gun.Cursor + (gun.AimForward * profile.MuzzleOffsetPx);
            Vector2 toTarget = target - muzzle;
            range = toTarget.Length();
            // How far off the aimed point the centre pellet will pass, in pixels rather
            // than in degrees. At the spread range a tenth of a degree is worth most of a
            // part, so a dot-product tolerance that is generous point blank is useless
            // here; the fan is only worth measuring when it is really centred on the seam.
            lateralError = range > 0.01f
                ? Mathf.Abs(range * gun.AimForward.Cross(toTarget.Normalized()))
                : float.MaxValue;
            if (gun.AimForward.Dot(toTarget) > 0.0f && lateralError <= tolerancePx)
                break;
        }

        Vector2 farTarget = atHead
            ? lab.Buddy.Rig.Head.GlobalPosition
            : lab.Buddy.Rig.Torso.GlobalPosition;
        float farSurface = Mathf.Abs(farTarget.X - muzzle.X) + radius;

        var accepted = new List<AcceptedImpact>();
        int episodes = 0;
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.ContentId == ContentIds.ToolShotgun)
                accepted.Add(impact);
        }

        void OnEpisode(AcceptedContactEpisode episode)
        {
            if (episode.ContentId == ContentIds.ToolShotgun)
                episodes++;
        }

        lab.Pipeline.ImpactAccepted += OnImpact;
        lab.Pipeline.EpisodeAccepted += OnEpisode;

        int shotsBeforeBurst = gun.ShotCount;
        await FireReadyShot(tree, gun, profile);

        List<ProjectileBody> volley = LivePellets(gun);
        int firedThisBurst = gun.ShotCount - shotsBeforeBurst;
        var connected = new HashSet<ulong>();
        var beyond = new HashSet<ulong>();
        float deepest = 0.0f;
        float bestImpulse = 0.0f;
        RigidBody2D.CcdMode ccd = RigidBody2D.CcdMode.Disabled;
        if (volley.Count > 0)
            ccd = volley[0].ContinuousCd;

        for (int tick = 0; tick < FlightTicks; tick++)
        {
            foreach (ProjectileBody pellet in volley)
            {
                if (!GodotObject.IsInstanceValid(pellet))
                    continue;

                float travel = pellet.GlobalPosition.DistanceTo(muzzle);
                deepest = Mathf.Max(deepest, travel);
                bestImpulse = Mathf.Max(bestImpulse, pellet.DeliveredImpulse);
                if (pellet.HasHit)
                    connected.Add(pellet.GetInstanceId());
                else if (pellet.State == ProjectileState.Live && travel > farSurface)
                    beyond.Add(pellet.GetInstanceId());
            }

            await Tick(tree);
        }

        lab.Pipeline.ImpactAccepted -= OnImpact;
        lab.Pipeline.EpisodeAccepted -= OnEpisode;

        // `beyond` is deliberately not reconciled against `connected`: a pellet that sailed
        // over the target and only stopped at the wall behind it has still passed through
        // what it was fired at, and forgiving it because it hit something eventually is how
        // a tunneling check quietly stops checking. Only asserted for the point-blank
        // burst, where the whole fan is inside the target's silhouette and there is no
        // honest way to be past it.
        var perPart = new Dictionary<BuddyPart, int>();
        float bestPain = 0.0f;
        float totalPain = 0.0f;
        foreach (AcceptedImpact impact in accepted)
        {
            perPart[impact.Part] = perPart.TryGetValue(impact.Part, out int seen) ? seen + 1 : 1;
            bestPain = Mathf.Max(bestPain, impact.Pain);
            totalPain += impact.Pain;
        }

        int worst = 0;
        var partReport = new List<string>();
        foreach (KeyValuePair<BuddyPart, int> entry in perPart)
        {
            worst = Mathf.Max(worst, entry.Value);
            partReport.Add($"{entry.Key}x{entry.Value}");
        }

        return new Burst(
            connected.Count,
            perPart.Count,
            accepted.Count,
            Mathf.Max(worst, perPart.Count == 0 ? 0 : worst),
            episodes,
            bestPain,
            totalPain,
            bestImpulse,
            beyond.Count > 0,
            deepest,
            farSurface,
            range,
            ccd,
            $"parts=[{string.Join(" ", partReport)}] fired={firedThisBurst} " +
            $"pellets_in_air={volley.Count} pellets_long={beyond.Count} " +
            $"best_pellet_impulse={bestImpulse:F1} pipeline_max_raw={lab.Pipeline.MaxRawImpulse:F1} " +
            $"lateral_error={lateralError:F1}px muzzle={muzzle} aim_point={AimPoint(lab, atHead)} " +
            $"pellets=[{PelletReport(volley, muzzle)}]");
    }

    /// <summary>
    /// One point-blank pistol bullet, measured on the same shared curve, so the pellet
    /// band above is stated against something rather than against a remembered number.
    /// </summary>
    private static async Task<Burst> FirePistolPointBlank(
        SceneTree tree,
        BuddyLab lab,
        CursorGunComponent gun)
    {
        GunProfile? pistol = ProfileFor(gun, ContentIds.ToolPistol);
        if (pistol is null)
            return default;

        Rect2 room = lab.Boundaries.InnerBounds;
        float bestPain = 0.0f;
        float bestImpulse = 0.0f;
        for (int attempt = 0; attempt < AimAttempts; attempt++)
        {
            await M4ObjectScenarioSupport.WaitFor(
                tree,
                () => lab.Buddy.Rig.Torso.LinearVelocity.Length() < SettledSpeedPx,
                240);

            Vector2 target = lab.Buddy.Rig.Head.GlobalPosition;
            (Vector2 cursor, Vector2 direction) = M4ObjectScenarioSupport.StandOffFrom(
                room, target, lab.Buddy.Rig.Head.Radius + 40.0f + pistol.MuzzleOffsetPx);
            await SelectAndAim(tree, lab, gun, ToolId.Pistol, cursor, direction);

            void OnImpact(AcceptedImpact impact)
            {
                if (impact.ContentId != ContentIds.ToolPistol)
                    return;

                bestPain = Mathf.Max(bestPain, impact.Pain);
                bestImpulse = Mathf.Max(bestImpulse, impact.Impulse);
            }

            lab.Pipeline.ImpactAccepted += OnImpact;
            gun.SetTriggerHeld(true);
            await Tick(tree);
            gun.SetTriggerHeld(false);
            for (int tick = 0; tick < FlightTicks; tick++)
                await Tick(tree);
            lab.Pipeline.ImpactAccepted -= OnImpact;

            await M4ObjectScenarioSupport.WaitFor(
                tree, () => gun.ActiveProjectileCount == 0, pistol.ProjectileLifetimeTicks + 16);
            if (bestPain > 0.0f)
                break;

            await Idle(tree, gun, pistol.ShotIntervalTicks);
        }

        return new Burst(
            bestPain > 0.0f ? 1 : 0,
            bestPain > 0.0f ? 1 : 0,
            bestPain > 0.0f ? 1 : 0,
            1,
            0,
            bestPain,
            bestPain,
            bestImpulse,
            false,
            0.0f,
            0.0f,
            0.0f,
            RigidBody2D.CcdMode.Disabled,
            "pistol reference shot");
    }

    /// <summary>What a volley of bursts at a live buddy established about coverage.</summary>
    private readonly record struct Coverage(
        int Bursts,
        int Landed,

        /// <summary>Whether every landed burst scored each covered part exactly once.</summary>
        bool EveryBurstScoredEachPartOnce,
        Burst Best,
        string Report);

    /// <summary>What one whole trigger pull did, pellet by pellet and part by part.</summary>
    private readonly record struct Burst(
        int Connections,
        int CoveredParts,
        int Accepted,

        /// <summary>Most accepted impacts any single part took from this one shot.</summary>
        int WorstPartRepeats,
        int Episodes,
        float BestPain,
        float TotalPain,
        float BestImpulse,
        bool Tunneled,
        float DeepestTravelPx,
        float FarSurfacePx,
        float RangePx,
        RigidBody2D.CcdMode CcdMode,
        string Report);
}
