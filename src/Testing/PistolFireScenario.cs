using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M5 cursor-gun platform gate (plan Task 5). The Pistol's authored contract from
/// RAGDOLL §9.2 is asserted against the real composition rather than the model alone:
/// aim follows pointer motion and the wheel, eight shots empty the magazine without
/// reloading, the ninth pull dry-fires into an automatic reload that mid-reload
/// presses cannot disturb, a real projectile's measured impulse scores pain attributed
/// to <c>tool.pistol</c>, a point-blank shot stops in the target instead of passing
/// through it, and bullets live in their own bounded pool instead of the FR-014
/// loose-object budget.
/// </summary>
public sealed class PistolFireScenario : IScenario
{
    private const int SettleTicks = 30;

    /// <summary>Pointer travel per tick for the reversal pin: brisk, deliberate aiming.</summary>
    private const float ReversalStepPx = 3.0f;

    /// <summary>
    /// The slop a hand leaves behind as it lets go of the mouse: backward first, then a
    /// little rocking. Small, and pointed the opposite way to the aim, which is the shape
    /// that used to turn the weapon completely round.
    /// </summary>
    private static readonly Vector2[] ReleaseJitter =
    {
        new(-1.0f, 0.0f),
        new(-1.0f, 1.0f),
        new(-1.0f, -1.0f),
    };

    public string Id => "pistol_fire";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("pistol_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        CursorGunComponent gun = lab.CursorGuns;
        lab.Pipeline.SelectTool(ToolId.Pistol);

        // A gun has no aim until the pointer really moves, so the cursor arrives the
        // way a player's does: from somewhere, heading somewhere.
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        (Vector2 standOff, Vector2 aim) =
            M4ObjectScenarioSupport.StandOffFrom(room, torso, 150.0f);
        await AimAt(tree, gun, standOff, aim);

        GunProfile profile = gun.ActiveProfile!;
        checks.Add(new StartupCheck(
            "selecting_the_pistol_arms_a_full_magazine",
            lab.Progress.IsToolUnlocked(ContentIds.ToolPistol) &&
            gun.IsActive &&
            gun.ActiveContentId == ContentIds.ToolPistol &&
            gun.RoundsRemaining == profile.MagazineCapacity &&
            !gun.IsReloading,
            $"owned={lab.Progress.IsToolUnlocked(ContentIds.ToolPistol)} active={gun.IsActive} " +
            $"content={gun.ActiveContentId} rounds={gun.RoundsRemaining} " +
            $"capacity={profile.MagazineCapacity}"));

        // --- Aim: travel, then the wheel, then travel clearing the wheel again ---
        // The wheel is used the way a player uses it: stop moving, scroll to raise the
        // shot, fire. "The next movement clears the offset" is the aim starting to steer
        // again, so the pointer has to have come to rest first — which is also why the
        // aim's own settling is waited for rather than assumed after a fixed count.
        Vector2 forwardFromMotion = gun.AimForward;
        bool settled = await SettleAim(tree, gun);
        gun.ApplyWheel(2);
        await Tick(tree);
        Vector2 raised = gun.AimForward;
        float raisedOffset = gun.AimOffsetDegrees;
        await AimAt(tree, gun, standOff, aim);
        checks.Add(new StartupCheck(
            "aim_follows_motion_and_the_wheel_offsets_it_until_the_next_motion",
            forwardFromMotion.Dot(aim) > 0.99f &&
            settled &&
            Mathf.IsEqualApprox(raisedOffset, profile.WheelDegreesPerStep * 2.0f, 0.01f) &&
            raised.Y < -0.05f &&
            Mathf.IsZeroApprox(gun.AimOffsetDegrees) &&
            gun.AimForward.Dot(aim) > 0.99f,
            $"motion_forward={forwardFromMotion} settled={settled} " +
            $"wheel_offset={raisedOffset:F1}deg wheel_forward={raised} " +
            $"cleared_offset={gun.AimOffsetDegrees:F1}deg cleared_forward={gun.AimForward}"));

        // --- Cadence and magazine: eight shots, no reload ---
        // Aimed along the room rather than at the buddy, so the magazine is spent on
        // cadence rather than on knocking the target out mid-measurement.
        Vector2 skyward = (aim + new Vector2(0.0f, -0.35f)).Normalized();
        await AimAt(tree, gun, standOff, skyward);
        int shotsBefore = gun.ShotCount;
        int reloadsBefore = gun.ReloadStartCount;
        int launchedBefore = gun.ProjectilesLaunched;
        int registryBefore = lab.Objects.Count;
        int registryPeak = registryBefore;
        int projectilePeak = 0;
        for (int shot = 0; shot < profile.MagazineCapacity; shot++)
        {
            await PressTrigger(tree, gun);
            projectilePeak = Mathf.Max(projectilePeak, gun.ActiveProjectileCount);
            registryPeak = Mathf.Max(registryPeak, lab.Objects.Count);
            await Idle(tree, gun, profile.ShotIntervalTicks - 1);
            projectilePeak = Mathf.Max(projectilePeak, gun.ActiveProjectileCount);
            registryPeak = Mathf.Max(registryPeak, lab.Objects.Count);
        }

        int firedMagazine = gun.ShotCount - shotsBefore;
        checks.Add(new StartupCheck(
            "eight_shots_empty_the_magazine_without_starting_a_reload",
            firedMagazine == profile.MagazineCapacity &&
            gun.ProjectilesLaunched - launchedBefore == profile.MagazineCapacity &&
            gun.RoundsRemaining == 0 &&
            gun.ReloadStartCount == reloadsBefore &&
            !gun.IsReloading,
            $"fired={firedMagazine} launched={gun.ProjectilesLaunched - launchedBefore} " +
            $"rounds={gun.RoundsRemaining} reload_starts={gun.ReloadStartCount - reloadsBefore} " +
            $"reloading={gun.IsReloading}"));

        // Projectiles are bounded by their own pool and never enter the FR-014 budget
        // (RAGDOLL §10) — the assertion Task 2 deferred until guns existed.
        checks.Add(new StartupCheck(
            "bullets_never_consume_a_loose_object_slot",
            registryPeak == registryBefore &&
            projectilePeak > 0 &&
            projectilePeak <= profile.PoolCapacity &&
            gun.PoolExhaustedCount == 0,
            $"registry_before={registryBefore} registry_peak={registryPeak} " +
            $"projectile_peak={projectilePeak} pool={profile.PoolCapacity} " +
            $"exhausted={gun.PoolExhaustedCount}"));

        // --- The ninth pull: dry fire into the automatic reload ---
        int dryBefore = gun.DryFireCount;
        await PressTrigger(tree, gun);
        bool autoReloadStarted = gun.IsReloading;
        int reloadTicksAtStart = gun.ReloadTicksRemaining;
        int dryFires = gun.DryFireCount - dryBefore;
        checks.Add(new StartupCheck(
            "the_ninth_press_dry_fires_and_starts_the_automatic_reload",
            dryFires == 1 &&
            autoReloadStarted &&
            gun.ReloadStartCount == reloadsBefore + 1 &&
            reloadTicksAtStart >= profile.ReloadTicks - 2,
            $"dry_fires={dryFires} reloading={autoReloadStarted} " +
            $"remaining={reloadTicksAtStart} authored={profile.ReloadTicks}"));

        // --- Mid-reload presses are ignored, and the reload still completes ---
        int shotsAtReload = gun.ShotCount;
        int completedBefore = gun.ReloadCompleteCount;
        for (int tick = 0; tick < profile.ReloadTicks / 2; tick++)
        {
            gun.SetTriggerHeld(tick % 2 == 1);
            await Tick(tree);
        }

        gun.SetTriggerHeld(false);
        bool stillReloading = gun.IsReloading;
        int shotsDuringReload = gun.ShotCount - shotsAtReload;
        await Idle(tree, gun, profile.ReloadTicks);
        checks.Add(new StartupCheck(
            "mid_reload_presses_are_ignored_and_the_reload_completes",
            shotsDuringReload == 0 &&
            stillReloading &&
            gun.ReloadCompleteCount == completedBefore + 1 &&
            gun.RoundsRemaining == profile.MagazineCapacity &&
            !gun.IsReloading,
            $"shots_during_reload={shotsDuringReload} still_reloading_at_half={stillReloading} " +
            $"completions={gun.ReloadCompleteCount - completedBefore} rounds={gun.RoundsRemaining}"));

        // Every bullet fired so far went into the room and must have returned to the
        // pool on its own: bounded lifetime, bounded travel, no accumulation.
        await Idle(tree, gun, profile.ProjectileLifetimeTicks + profile.SpentLingerTicks + 4);
        checks.Add(new StartupCheck(
            "spent_and_expired_bullets_return_to_the_pool",
            gun.ActiveProjectileCount == 0 && lab.Objects.Count == registryBefore,
            $"active={gun.ActiveProjectileCount} registry={lab.Objects.Count}"));

        // --- A real hit, fired point blank: the hardest case for both halves ---
        AcceptedImpact? hit = null;
        void OnImpact(AcceptedImpact impact)
        {
            if (hit is null && impact.ContentId == ContentIds.ToolPistol)
                hit = impact;
        }

        // Episodes as well as scored impacts: an episode that arrived and scored no
        // pain says the bullet connected but its measured impulse sat under the curve
        // floor, which is a tuning fact, not a plumbing failure. Reporting both is how
        // that distinction stays visible instead of showing up as a blank detail line.
        int pistolEpisodes = 0;
        float strongestEpisodeImpulse = 0.0f;
        void OnEpisode(AcceptedContactEpisode episode)
        {
            if (episode.ContentId != ContentIds.ToolPistol)
                return;

            pistolEpisodes++;
            strongestEpisodeImpulse = Mathf.Max(strongestEpisodeImpulse, episode.Impulse);
        }

        lab.Pipeline.ImpactAccepted += OnImpact;
        lab.Pipeline.EpisodeAccepted += OnEpisode;

        // Point blank, where a shot has the least room to be caught: the projectile
        // must stop on the surface it was aimed at instead of passing through it, and
        // it must still be carrying enough momentum for the solver to report an impulse
        // the shared curve can score. Both halves matter — see
        // GunProfile.MaximumTravelPerTickPx for why a faster bullet satisfies the first
        // and silently fails the second.
        var pointBlank = new PointBlankShot(lab, gun, profile);
        bool tunneled = await pointBlank.FireAsync(tree);
        for (int tick = 0; tick < 60 && hit is null; tick++)
            await Tick(tree);
        lab.Pipeline.ImpactAccepted -= OnImpact;
        lab.Pipeline.EpisodeAccepted -= OnEpisode;

        checks.Add(new StartupCheck(
            "a_bullet_hit_scores_pain_attributed_to_the_pistol",
            hit is { Pain: > 0.0f } scored &&
            scored.ContentId == ContentIds.ToolPistol &&
            scored.MilliCredits > 0L,
            $"content={hit?.ContentId} impulse={hit?.Impulse:F1} pain={hit?.Pain:F2} " +
            $"milli={hit?.MilliCredits} part={hit?.Part} episodes={pistolEpisodes} " +
            $"strongest_episode_impulse={strongestEpisodeImpulse:F1} " +
            $"delivered_impulse={pointBlank.DeliveredImpulse:F1} " +
            $"pipeline_max_raw={lab.Pipeline.MaxRawImpulse:F1}"));

        checks.Add(new StartupCheck(
            "point_blank_fire_never_tunnels_through_the_target",
            pointBlank.Connected && !tunneled,
            $"connected={pointBlank.Connected} deepest_travel={pointBlank.DeepestTravelPx:F1}px " +
            $"far_surface={pointBlank.FarSurfacePx:F1}px ccd={pointBlank.CcdMode}"));

        // The shot the player sees has to be the shot the physics fired: drawn along the
        // velocity it has at that instant, whatever the body itself is doing. The body is
        // free to spin (an off-centre hit does spin it, and taking that away halves the
        // impulse the pain pipeline scores), and it starts every flight square, so a
        // recycled pool slot cannot inherit an orientation from the shot before it.
        checks.Add(new StartupCheck(
            "the_bullet_visual_stays_glued_to_its_flight_path",
            pointBlank.WorstVisualAlignment > 0.99f &&
            Mathf.Abs(pointBlank.LaunchRotationRadians) < 0.001f,
            $"worst_alignment={pointBlank.WorstVisualAlignment:F3} " +
            $"launch_rotation={Mathf.RadToDeg(pointBlank.LaunchRotationRadians):F3}deg " +
            $"body_spin={Mathf.RadToDeg(pointBlank.MaxSpinRadians):F1}deg"));

        checks.Add(new StartupCheck(
            "harmful_history_and_statistics_name_the_pistol",
            lab.Progress.IsContentHarmful(ContentIds.ToolPistol) &&
            CountFor(lab.Progress.Statistics.ToolPainMilli, ContentIds.ToolPistol) > 0L,
            $"harmful={lab.Progress.IsContentHarmful(ContentIds.ToolPistol)} " +
            $"pain_milli={CountFor(lab.Progress.Statistics.ToolPainMilli, ContentIds.ToolPistol)}"));

        // --- Holstering: the trigger belongs to the gun, not to the pointer ---
        lab.Pipeline.SelectTool(ToolId.Grab);
        await Tick(tree);
        int shotsAtHolster = gun.ShotCount;
        await PressTrigger(tree, gun);
        await PressTrigger(tree, gun);
        checks.Add(new StartupCheck(
            "a_holstered_gun_cannot_fire",
            !gun.IsActive &&
            gun.ActiveContentId is null &&
            gun.ShotCount == shotsAtHolster,
            $"active={gun.IsActive} content={gun.ActiveContentId} " +
            $"shots={gun.ShotCount - shotsAtHolster}"));

        // --- The owner's "a few clicks before ammo comes out to the left" report ---
        // Sweeping the pointer across the play area brushes the window edge, and the
        // pointer-exit notification clears the cursor: the gun deactivates, its aim is
        // reset, and until fresh travel re-establishes one there is no direction to fire
        // along. Every click in that state used to eat a round in silence.
        lab.Pipeline.SelectTool(ToolId.Pistol);
        await Idle(tree, gun, profile.ProjectileLifetimeTicks + profile.SpentLingerTicks + 4);
        Vector2 bench = room.GetCenter();
        await AimAt(tree, gun, bench, Vector2.Right);
        int roundsBeforeRight = gun.RoundsRemaining;
        await PressTrigger(tree, gun);
        ProjectileBody? rightShot = NewestLiveProjectile(gun);
        Vector2 rightLaunch = rightShot?.LaunchVelocity ?? Vector2.Zero;
        bool firedRight = rightShot is not null &&
                          rightLaunch.X > 0.0f &&
                          gun.RoundsRemaining == roundsBeforeRight - 1;

        // Cadence out of the way first: this reproduction is about aim, and a press
        // refused for being inside the shot interval would hide the defect behind a
        // rule that has nothing to do with it.
        await Idle(tree, gun, profile.ShotIntervalTicks);
        gun.ClearCursor();
        await Tick(tree);
        gun.MoveCursor(bench);
        await Tick(tree);
        int spentWithoutAimBefore = gun.ShotsSpentWithoutAim;
        int roundsAtReentry = gun.RoundsRemaining;
        await PressTrigger(tree, gun);
        checks.Add(new StartupCheck(
            "pointer_reentry_click_without_motion_spends_no_round",
            gun.RoundsRemaining == roundsAtReentry &&
            gun.ShotsSpentWithoutAim == spentWithoutAimBefore &&
            !gun.IsReloading,
            $"rounds_before={roundsAtReentry} rounds_after={gun.RoundsRemaining} " +
            $"spent_without_aim={gun.ShotsSpentWithoutAim - spentWithoutAimBefore} " +
            $"reloading={gun.IsReloading}"));

        await Idle(tree, gun, profile.ShotIntervalTicks);
        await AimAt(tree, gun, bench, Vector2.Left);
        int roundsBeforeLeft = gun.RoundsRemaining;
        await PressTrigger(tree, gun);
        ProjectileBody? leftShot = NewestLiveProjectile(gun);
        Vector2 leftLaunch = leftShot?.LaunchVelocity ?? Vector2.Zero;
        checks.Add(new StartupCheck(
            "right_then_left_first_click_fires_left",
            firedRight &&
            leftShot is not null &&
            leftLaunch.X < 0.0f &&
            gun.RoundsRemaining == roundsBeforeLeft - 1,
            $"fired_right={firedRight} right_launch={rightLaunch} left_launch={leftLaunch} " +
            $"rounds_before={roundsBeforeLeft} rounds_after={gun.RoundsRemaining}"));

        // --- Aim feel: the "choppy, locked to different axes" report (§1.5) ---
        // Three properties of the aim rather than of the gun, so none of them pulls a
        // trigger: slow travel steers, letting go of the mouse never swings the weapon,
        // and a reversal is steered through instead of teleported. Each starts from a
        // settled rightward aim near the right of the room, which leaves the whole width
        // of the play area as the lane the pointer travels down.
        var aimLane = new Vector2(room.End.X - 12.0f, room.GetCenter().Y);

        await AimAt(tree, gun, aimLane, Vector2.Right);
        float aimedRight = gun.AimForward.Dot(Vector2.Right);
        // Deliberately under a pixel per tick, and derived from the authored gate so a
        // co-tuning session cannot leave it below the speed the aim steers at. The
        // retired raw threshold discarded every delta smaller than a whole pixel, which
        // is 120 px/s: a slow deliberate aim to the left steered nothing at all, and the
        // gun kept firing along the rightward direction the player had left behind.
        float creepPx = Mathf.Min(0.9f, profile.MinimumAimSpeedPxPerTick * 1.4f);
        int creepTicks = await DriftCursor(
            tree,
            gun,
            aimLane,
            new Vector2(-creepPx, 0.0f),
            300,
            () => gun.AimForward.Dot(Vector2.Left) > 0.999f);
        checks.Add(new StartupCheck(
            "slow_leftward_travel_steers_the_aim_left",
            aimedRight > 0.99f && creepTicks > 0,
            $"step={creepPx:F2}px/tick gate={profile.MinimumAimSpeedPxPerTick:F2}px/tick " +
            $"aimed_right={aimedRight:F3} ticks={creepTicks} forward={gun.AimForward} " +
            $"cursor={gun.Cursor}"));

        // A hand letting go of the mouse: a pixel of backward slop with a little rocking,
        // then stillness. Backward is the case that matters, because the old aim was the
        // last raw delta normalized — a single pixel of slop turned the weapon completely
        // round, and the shot after it went the other way.
        await AimAt(tree, gun, aimLane, Vector2.Right);
        Vector2 jitterCursor = aimLane;
        float worstJitter = gun.AimForward.Dot(Vector2.Right);
        foreach (Vector2 slop in ReleaseJitter)
        {
            jitterCursor += slop;
            gun.MoveCursor(jitterCursor);
            await Tick(tree);
            worstJitter = Mathf.Min(worstJitter, gun.AimForward.Dot(Vector2.Right));
        }

        // Then the hand simply stops. The gate has hysteresis rather than decay, so the
        // aim has to hold exactly where it was left instead of drifting back to anything.
        for (int tick = 0; tick < 90; tick++)
        {
            await Tick(tree);
            worstJitter = Mathf.Min(worstJitter, gun.AimForward.Dot(Vector2.Right));
        }

        checks.Add(new StartupCheck(
            "aim_never_flips_on_release_jitter",
            worstJitter > 0.99f && !gun.AimIsSteering,
            $"worst_alignment={worstJitter:F3} settled={!gun.AimIsSteering} " +
            $"smoothed_speed={gun.AimSmoothedSpeed:F3} forward={gun.AimForward}"));

        // The turn rate is the owner's co-tuning dial (plan §4.1), so it is pinned from
        // both sides against the authored numbers: a reversal may not cost less than the
        // slew itself allows — that would mean the aim snapped somewhere — and it may not
        // cost more than the slew plus the time the smoothed velocity needs to change its
        // mind. Re-record the measured count here whenever the dial moves.
        await AimAt(tree, gun, aimLane, Vector2.Right);
        int reversalTicks = await DriftCursor(
            tree,
            gun,
            aimLane,
            new Vector2(-ReversalStepPx, 0.0f),
            240,
            () => gun.AimForward.Dot(Vector2.Left) > 0.999f);
        int slewFloor = Mathf.CeilToInt(180.0f / profile.MaxAimTurnDegreesPerTick);
        int reversalBudget = slewFloor + Mathf.CeilToInt(3.0f * profile.AimSmoothingHalfLifeTicks);
        checks.Add(new StartupCheck(
            "sustained_reversal_completes_within_expected_ticks",
            reversalTicks >= slewFloor && reversalTicks <= reversalBudget,
            $"ticks={reversalTicks} slew_floor={slewFloor} budget={reversalBudget} " +
            $"turn={profile.MaxAimTurnDegreesPerTick:F1}deg/tick " +
            $"half_life={profile.AimSmoothingHalfLifeTicks:F0}ticks forward={gun.AimForward}"));

        messages.Add(
            $"creep_ticks={creepTicks} reversal_ticks={reversalTicks} " +
            $"worst_release_alignment={worstJitter:F3}");
        messages.Add(
            $"spent_without_aim={gun.ShotsSpentWithoutAim} " +
            $"body_spin={Mathf.RadToDeg(pointBlank.MaxSpinRadians):F1}deg " +
            $"worst_visual_alignment={pointBlank.WorstVisualAlignment:F3}");
        messages.Add(
            $"pain={hit?.Pain:F2} impulse={hit?.Impulse:F1} muzzle_speed={profile.MuzzleSpeed} " +
            $"projectile_peak={projectilePeak} deepest_travel={pointBlank.DeepestTravelPx:F1}px " +
            $"far_surface={pointBlank.FarSurfacePx:F1}px");
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

    /// <summary>
    /// Walks the cursor by a fixed step every tick from <paramref name="start"/> and
    /// reports how many ticks it took for <paramref name="until"/> to hold, or <c>-1</c>
    /// if it never did within <paramref name="maxTicks"/>.
    ///
    /// <para>Unlike <see cref="AimAt"/>, which delivers an aim, this delivers the travel
    /// itself: the shape and the speed of the pointer motion are what these checks are
    /// about, so they cannot be handed to a helper that decides them.</para>
    /// </summary>
    private static async Task<int> DriftCursor(
        SceneTree tree,
        CursorGunComponent gun,
        Vector2 start,
        Vector2 stepPerTick,
        int maxTicks,
        System.Func<bool> until)
    {
        Vector2 cursor = start;
        for (int tick = 1; tick <= maxTicks; tick++)
        {
            cursor += stepPerTick;
            gun.MoveCursor(cursor);
            await Tick(tree);
            if (until())
                return tick;
        }

        return -1;
    }

    /// <summary>
    /// Waits for a held-still pointer's smoothed aim to fall back below the steering gate,
    /// which is the state the wheel offset lives in.
    /// </summary>
    private static async Task<bool> SettleAim(SceneTree tree, CursorGunComponent gun) =>
        await M4ObjectScenarioSupport.WaitFor(tree, () => !gun.AimIsSteering, 240);

    private static ProjectileBody? NewestLiveProjectile(CursorGunComponent gun) =>
        M4ObjectScenarioSupport.NewestLiveProjectile(gun);

    /// <summary>One complete trigger pull: pressed for a tick, then released.</summary>
    private static async Task PressTrigger(SceneTree tree, CursorGunComponent gun)
    {
        gun.SetTriggerHeld(true);
        await Tick(tree);
        gun.SetTriggerHeld(false);
        await Tick(tree);
    }

    private static async Task Idle(SceneTree tree, CursorGunComponent gun, int ticks)
    {
        gun.SetTriggerHeld(false);
        for (int tick = 0; tick < ticks; tick++)
            await Tick(tree);
    }

    /// <summary>
    /// One point-blank shot with its flight sampled every tick. The measurement is
    /// deliberately geometric rather than a claim about the engine: the projectile's
    /// straight-line travel is compared with the distance to the far surface of the
    /// part it was aimed at, so a shot that skipped through would be visible as travel
    /// beyond it with no contact. This is not a hypothetical — a shape-cast projectile
    /// really did sail through a head in testing, and this is the check that caught it.
    /// </summary>
    private sealed class PointBlankShot
    {
        private readonly BuddyLab _lab;
        private readonly CursorGunComponent _gun;
        private readonly GunProfile _profile;

        public PointBlankShot(BuddyLab lab, CursorGunComponent gun, GunProfile profile)
        {
            _lab = lab;
            _gun = gun;
            _profile = profile;
        }

        public bool Connected { get; private set; }

        public float DeliveredImpulse { get; private set; }

        public float DeepestTravelPx { get; private set; }

        public float FarSurfacePx { get; private set; }

        public RigidBody2D.CcdMode CcdMode { get; private set; } = RigidBody2D.CcdMode.Disabled;

        /// <summary>
        /// The most this bullet's body ever rotated during a flight the player can see.
        /// Reported rather than bounded: the body really is free to spin, on purpose, and
        /// the point of the check below is that its drawing does not follow it.
        /// </summary>
        public float MaxSpinRadians { get; private set; }

        /// <summary>The orientation this flight started at; a reused slot must not inherit one.</summary>
        public float LaunchRotationRadians { get; private set; }

        /// <summary>
        /// The worst agreement, over the whole flight, between the direction the streak
        /// is drawn along and the direction the bullet is actually travelling.
        /// </summary>
        public float WorstVisualAlignment { get; private set; } = 1.0f;

        public async Task<bool> FireAsync(SceneTree tree)
        {
            _lab.Pipeline.SelectTool(ToolId.Pistol);
            // The head, not the torso: the buddy's hands hang beside its chest, and a
            // horizontal chest shot from beside it clips a hand first, which measures the
            // wrong contact.
            Vector2 target = _lab.Buddy.Rig.Head.GlobalPosition;
            float radius = _lab.Buddy.Rig.Head.Radius;
            Rect2 room = _lab.Boundaries.InnerBounds;
            (Vector2 cursor, Vector2 direction) =
                M4ObjectScenarioSupport.StandOffFrom(room, target, radius + 40.0f);
            await AimAt(tree, _gun, cursor, direction);
            for (int tick = 0; tick < SettleTicks; tick++)
                await Tick(tree);

            // Measured from the muzzle, which is where the projectile is really born.
            Vector2 muzzle = cursor + (direction * _profile.MuzzleOffsetPx);
            target = _lab.Buddy.Rig.Head.GlobalPosition;
            FarSurfacePx = Mathf.Abs(target.X - muzzle.X) + radius;

            _gun.SetTriggerHeld(true);
            await Tick(tree);
            _gun.SetTriggerHeld(false);

            ProjectileBody? bullet = FindLiveProjectile();
            if (bullet is not null)
            {
                CcdMode = bullet.ContinuousCd;
                LaunchRotationRadians = bullet.LaunchRotation;
            }

            bool beyondFarSurface = false;
            for (int tick = 0; tick < 120; tick++)
            {
                if (!GodotObject.IsInstanceValid(bullet) || bullet is null)
                    break;

                float travel = bullet.GlobalPosition.DistanceTo(muzzle);
                DeepestTravelPx = Mathf.Max(DeepestTravelPx, travel);
                Connected |= bullet.HasHit;
                DeliveredImpulse = Mathf.Max(DeliveredImpulse, bullet.DeliveredImpulse);

                // Sampled while the bullet is still visible, which includes the contact
                // settle window: that is exactly where an off-centre hit used to spin it.
                MaxSpinRadians = Mathf.Max(
                    MaxSpinRadians, Mathf.Abs(Mathf.Wrap(bullet.Rotation, -Mathf.Pi, Mathf.Pi)));
                Vector2 velocity = bullet.LinearVelocity;
                if (velocity.Length() > 1.0f)
                {
                    WorstVisualAlignment = Mathf.Min(
                        WorstVisualAlignment, bullet.VisualForward.Dot(velocity.Normalized()));
                }
                if (!Connected && travel > FarSurfacePx)
                {
                    // Past the far surface of the part it was aimed at, with no contact
                    // ever reported: that is the tunneling this check exists to catch.
                    beyondFarSurface = true;
                    break;
                }

                if (bullet.State == ProjectileState.Pooled)
                    break;

                await Tick(tree);
            }

            return beyondFarSurface;
        }

        private ProjectileBody? FindLiveProjectile()
        {
            foreach (Node child in _gun.GetChildren())
            {
                if (child is ProjectileBody { State: ProjectileState.Live } projectile)
                    return projectile;
            }

            return null;
        }
    }
}
