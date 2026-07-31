using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The M5 gun split (plan Task D): one platform, two guns, and the difference between them
/// authored rather than coded. The Nerf Blaster is the toy the player owns first and the
/// Pistol is the real one, and the only thing that separates them is their
/// <see cref="GunProfile"/> — so the separation has to be <b>measured</b> through the
/// unmodified pain pipeline, which is the sacred rule this scenario exists to hold:
/// pain comes from a measured solver impulse and there is no per-gun damage multiplier
/// anywhere for a dart to be made harmless with.
/// </summary>
public sealed class NerfVersusPistolScenario : IScenario
{
    /// <summary>Ticks a shot's flight is sampled for before the check gives up on it.</summary>
    private const int FlightTicks = 90;

    /// <summary>How long a level trajectory is watched, in ticks, before it is judged.</summary>
    private const int TrajectoryTicks = 40;

    /// <summary>How many times an aimed shot re-derives its geometry before it fires.</summary>
    private const int AimAttempts = 4;

    /// <summary>Torso speed, in px/s, under which the buddy counts as standing still.</summary>
    private const float SettledSpeedPx = 8.0f;

    public string Id => "nerf_versus_pistol";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("gun_split_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        CursorGunComponent gun = lab.CursorGuns;
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 bench = room.GetCenter();

        // --- Both guns are real, selectable content, through the real lab keys ---
        gun.MoveCursor(bench);
        await Tick(tree);
        await M4ObjectScenarioSupport.SendKey(tree, Key.N);
        await Tick(tree);
        bool nerfSelected =
            lab.Pipeline.SelectedTool == ToolId.NerfBlaster &&
            gun.IsActive &&
            gun.ActiveContentId == ContentIds.ToolNerfBlaster;
        GunProfile nerf = gun.ActiveProfile!;

        await M4ObjectScenarioSupport.SendKey(tree, Key.J);
        await Tick(tree);
        bool pistolSelected =
            lab.Pipeline.SelectedTool == ToolId.Pistol &&
            gun.IsActive &&
            gun.ActiveContentId == ContentIds.ToolPistol;
        GunProfile pistol = gun.ActiveProfile!;

        checks.Add(new StartupCheck(
            "both_guns_are_selectable_in_the_lab",
            nerfSelected &&
            pistolSelected &&
            lab.Progress.IsToolUnlocked(ContentIds.ToolNerfBlaster) &&
            lab.Progress.IsToolUnlocked(ContentIds.ToolPistol) &&
            nerf.ContentId != pistol.ContentId,
            $"nerf_key_selected={nerfSelected} pistol_key_selected={pistolSelected} " +
            $"nerf_magazine={nerf.MagazineCapacity} pistol_magazine={pistol.MagazineCapacity} " +
            $"nerf_muzzle={nerf.MuzzleSpeed} pistol_muzzle={pistol.MuzzleSpeed}"));

        // --- A magazine belongs to its gun, not to the hand holding one ---
        // Fired away from the buddy: this is about bookkeeping across a swap, and a shot
        // that knocks the target over would be measuring something else.
        Vector2 skyward = new Vector2(1.0f, -0.6f).Normalized();
        await SelectAndAim(tree, lab, gun, ToolId.NerfBlaster, bench, skyward);
        int nerfLoaded = gun.RoundsRemaining;
        await FireBurst(tree, gun, nerf, 2);
        int nerfSpent = gun.RoundsRemaining;

        await SelectAndAim(tree, lab, gun, ToolId.Pistol, bench, skyward);
        int pistolLoaded = gun.RoundsRemaining;
        await FireBurst(tree, gun, pistol, 3);
        int pistolSpent = gun.RoundsRemaining;

        await SelectAndAim(tree, lab, gun, ToolId.NerfBlaster, bench, skyward);
        int nerfResumed = gun.RoundsRemaining;
        await SelectAndAim(tree, lab, gun, ToolId.Pistol, bench, skyward);
        int pistolResumed = gun.RoundsRemaining;

        checks.Add(new StartupCheck(
            "swapping_guns_preserves_each_magazine",
            nerfLoaded == nerf.MagazineCapacity &&
            pistolLoaded == pistol.MagazineCapacity &&
            nerfSpent == nerfLoaded - 2 &&
            pistolSpent == pistolLoaded - 3 &&
            nerfResumed == nerfSpent &&
            pistolResumed == pistolSpent,
            $"nerf={nerfLoaded}->{nerfSpent}->{nerfResumed} " +
            $"pistol={pistolLoaded}->{pistolSpent}->{pistolResumed}"));

        // Every stray round has to be out of the room before anything is measured: a
        // ricochet arriving mid-measurement carries the same content ID as the shot under
        // test and would be indistinguishable from it.
        await Settle(tree, gun, nerf, pistol);

        // --- Darts droop, bullets do not ---
        // Fired level and high, over the buddy's head, so the only thing bending the path
        // is the authored gravity scale. The lane starts well clear of the left wall: the
        // aim is established by pointer travel, and a cursor clamped against the edge of
        // the play area stops travelling, which leaves the shot pointing wherever the
        // approach did.
        var lane = new Vector2(room.Position.X + 110.0f, room.Position.Y + 44.0f);
        Trajectory dartPath = await MeasureTrajectory(
            tree, lab, gun, ToolId.NerfBlaster, lane, Vector2.Right);
        await Settle(tree, gun, nerf, pistol);
        Trajectory bulletPath = await MeasureTrajectory(
            tree, lab, gun, ToolId.Pistol, lane, Vector2.Right);
        await Settle(tree, gun, nerf, pistol);

        checks.Add(new StartupCheck(
            "darts_droop_and_bullets_fly_flat",
            dartPath.WasLevel &&
            bulletPath.WasLevel &&
            dartPath.Drop >= 2.0f &&
            bulletPath.Drop <= 0.25f &&
            dartPath.Ticks > 4 &&
            bulletPath.Ticks > 4,
            $"dart_drop={dartPath.Drop:F2}px over {dartPath.Ticks} ticks " +
            $"(gravity={nerf.ProjectileGravityScale}, launched {dartPath.Launch}) " +
            $"bullet_drop={bulletPath.Drop:F2}px over {bulletPath.Ticks} ticks " +
            $"(gravity={pistol.ProjectileGravityScale}, launched {bulletPath.Launch})"));

        // --- What the two guns do to the buddy, measured point blank ---
        Shot dart = await FirePointBlank(
            tree, lab, gun, ToolId.NerfBlaster, ContentIds.ToolNerfBlaster);
        await Settle(tree, gun, nerf, pistol);
        Shot bullet = await FirePointBlank(
            tree, lab, gun, ToolId.Pistol, ContentIds.ToolPistol);

        checks.Add(new StartupCheck(
            "nerf_dart_scores_no_meaningful_pain",
            dart.Connected && dart.Pain <= 0.0f && dart.MilliCredits == 0L,
            $"connected={dart.Connected} delivered_impulse={dart.DeliveredImpulse:F1} " +
            $"episode_impulse={dart.EpisodeImpulse:F1} pain={dart.Pain:F2} " +
            $"milli={dart.MilliCredits} mass={nerf.ProjectileMass} muzzle={nerf.MuzzleSpeed} " +
            dart.Geometry));

        // Ten times is not a tuning target, it is the size of the gap that makes the two
        // guns different weapons rather than two skins on one.
        checks.Add(new StartupCheck(
            "pistol_bullet_hurts_the_buddy",
            bullet.Connected &&
            bullet.Pain > 0.0f &&
            bullet.MilliCredits > 0L &&
            bullet.DeliveredImpulse > dart.DeliveredImpulse * 10.0f,
            $"connected={bullet.Connected} delivered_impulse={bullet.DeliveredImpulse:F1} " +
            $"pain={bullet.Pain:F2} milli={bullet.MilliCredits} part={bullet.Part} " +
            $"dart_impulse={dart.DeliveredImpulse:F1} " + bullet.Geometry + " " +
            $"separation={(dart.DeliveredImpulse > 0.0f ? bullet.DeliveredImpulse / dart.DeliveredImpulse : 0.0f):F1}x"));

        checks.Add(new StartupCheck(
            "only_the_pistol_is_remembered_as_harmful",
            lab.Progress.IsContentHarmful(ContentIds.ToolPistol) &&
            !lab.Progress.IsContentHarmful(ContentIds.ToolNerfBlaster),
            $"pistol_harmful={lab.Progress.IsContentHarmful(ContentIds.ToolPistol)} " +
            $"nerf_harmful={lab.Progress.IsContentHarmful(ContentIds.ToolNerfBlaster)}"));

        messages.Add(
            $"dart impulse={dart.DeliveredImpulse:F1} pain={dart.Pain:F2} | " +
            $"bullet impulse={bullet.DeliveredImpulse:F1} pain={bullet.Pain:F2} " +
            $"part={bullet.Part}");
        messages.Add(
            $"dart_drop={dartPath.Drop:F2}px bullet_drop={bulletPath.Drop:F2}px " +
            $"nerf_muzzle={nerf.MuzzleSpeed} pistol_muzzle={pistol.MuzzleSpeed}");

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task Tick(SceneTree tree) =>
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

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
        // Drawing a gun resets its aim on purpose, so every swap has to re-establish one
        // the way a hand does — there is nothing to fire along until the pointer travels.
        await M4ObjectScenarioSupport.AimGunOver(tree, gun, cursor, direction);
    }

    /// <summary>One complete trigger pull, then the authored cadence.</summary>
    private static async Task FireBurst(
        SceneTree tree,
        CursorGunComponent gun,
        GunProfile profile,
        int shots)
    {
        for (int shot = 0; shot < shots; shot++)
        {
            gun.SetTriggerHeld(true);
            await Tick(tree);
            gun.SetTriggerHeld(false);
            for (int tick = 0; tick < profile.ShotIntervalTicks; tick++)
                await Tick(tree);
        }
    }

    /// <summary>Waits until every round either expired or returned to its pool.</summary>
    private static async Task Settle(
        SceneTree tree,
        CursorGunComponent gun,
        GunProfile nerf,
        GunProfile pistol)
    {
        gun.SetTriggerHeld(false);
        int budget =
            Mathf.Max(nerf.ProjectileLifetimeTicks, pistol.ProjectileLifetimeTicks) +
            Mathf.Max(nerf.SpentLingerTicks, pistol.SpentLingerTicks) + 8;
        await M4ObjectScenarioSupport.WaitFor(tree, () => gun.ActiveProjectileCount == 0, budget);
    }

    /// <summary>
    /// Fires one level shot and reports how far it fell while the player could still see
    /// it. Drop is measured from the launch height rather than from the cursor: the shot
    /// is born at the muzzle, and the aim's own pitch is not part of this question.
    /// </summary>
    private static async Task<Trajectory> MeasureTrajectory(
        SceneTree tree,
        BuddyLab lab,
        CursorGunComponent gun,
        ToolId tool,
        Vector2 cursor,
        Vector2 direction)
    {
        await SelectAndAim(tree, lab, gun, tool, cursor, direction);
        gun.SetTriggerHeld(true);
        await Tick(tree);
        gun.SetTriggerHeld(false);

        ProjectileBody? shot = M4ObjectScenarioSupport.NewestLiveProjectile(gun);
        if (shot is null)
            return new Trajectory(0.0f, 0, Vector2.Zero);

        Vector2 launch = shot.LaunchVelocity;
        float launchY = shot.GlobalPosition.Y;
        float drop = 0.0f;
        int ticks = 0;
        for (int tick = 0; tick < TrajectoryTicks; tick++)
        {
            if (!GodotObject.IsInstanceValid(shot) || shot.State != ProjectileState.Live)
                break;

            drop = shot.GlobalPosition.Y - launchY;
            ticks = tick;
            await Tick(tree);
        }

        return new Trajectory(drop, ticks, launch);
    }

    /// <summary>
    /// One point-blank head shot with both halves of the answer: what the pipeline scored,
    /// and what the projectile itself delivered. The second is what proves a dart really
    /// connected on a shot the pain curve is expected to score at nothing — otherwise
    /// "harmless" and "missed" read identically.
    /// </summary>
    private static async Task<Shot> FirePointBlank(
        SceneTree tree,
        BuddyLab lab,
        CursorGunComponent gun,
        ToolId tool,
        string contentId)
    {
        // The buddy is alive, and an engaged cursor is something it walks over to look at.
        // Establishing an aim costs the best part of a second of pointer travel, so a
        // stand-off computed before the sweep can be stale by the time the trigger goes —
        // this scenario has fired the barrel straight past a head that stepped behind it.
        // The geometry is therefore re-derived until the shot is really square, and it is
        // reported either way: a miss and a harmless dart both score no pain, and the
        // whole point of these two checks is to tell those apart.
        float radius = lab.Buddy.Rig.Head.Radius;
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 muzzle = Vector2.Zero;
        Vector2 toHead = Vector2.Zero;
        float aimError = 0.0f;
        for (int attempt = 0; attempt < AimAttempts; attempt++)
        {
            // A target still walking will have moved again by the time the sweep ends.
            await M4ObjectScenarioSupport.WaitFor(
                tree,
                () => lab.Buddy.Rig.Torso.LinearVelocity.Length() < SettledSpeedPx,
                240);

            Vector2 target = lab.Buddy.Rig.Head.GlobalPosition;
            (Vector2 cursor, Vector2 direction) =
                M4ObjectScenarioSupport.StandOffFrom(room, target, radius + 40.0f);
            await SelectAndAim(tree, lab, gun, tool, cursor, direction);

            muzzle = gun.Cursor + (gun.AimForward * gun.ActiveProfile!.MuzzleOffsetPx);
            toHead = lab.Buddy.Rig.Head.GlobalPosition - muzzle;
            aimError = toHead.Length() > 0.01f
                ? gun.AimForward.Dot(toHead.Normalized())
                : 0.0f;
            if (aimError > 0.99f && toHead.Length() > radius)
                break;
        }

        AcceptedImpact? scored = null;
        float episodeImpulse = 0.0f;
        void OnImpact(AcceptedImpact impact)
        {
            if (scored is null && impact.ContentId == contentId)
                scored = impact;
        }

        void OnEpisode(AcceptedContactEpisode episode)
        {
            if (episode.ContentId == contentId)
                episodeImpulse = Mathf.Max(episodeImpulse, episode.Impulse);
        }

        lab.Pipeline.ImpactAccepted += OnImpact;
        lab.Pipeline.EpisodeAccepted += OnEpisode;

        gun.SetTriggerHeld(true);
        await Tick(tree);
        gun.SetTriggerHeld(false);

        ProjectileBody? shot = M4ObjectScenarioSupport.NewestLiveProjectile(gun);
        Vector2 launch = shot?.LaunchVelocity ?? Vector2.Zero;
        bool connected = false;
        float delivered = 0.0f;
        float travel = 0.0f;
        for (int tick = 0; tick < FlightTicks; tick++)
        {
            if (GodotObject.IsInstanceValid(shot) && shot is not null)
            {
                connected |= shot.HasHit;
                delivered = Mathf.Max(delivered, shot.DeliveredImpulse);
                travel = Mathf.Max(travel, shot.TravelledPx);
            }

            await Tick(tree);
        }

        lab.Pipeline.ImpactAccepted -= OnImpact;
        lab.Pipeline.EpisodeAccepted -= OnEpisode;
        return new Shot(
            connected,
            delivered,
            episodeImpulse,
            scored?.Pain ?? 0.0f,
            scored?.MilliCredits ?? 0L,
            scored?.Part.ToString() ?? "none",
            aimError,
            toHead.Length(),
            travel,
            launch);
    }

    private readonly record struct Trajectory(float Drop, int Ticks, Vector2 Launch)
    {
        /// <summary>
        /// True when the shot really left the barrel level. Without this the check could
        /// pass on a mis-aimed shot whose "drop" was the aim rather than the gravity.
        /// </summary>
        public bool WasLevel =>
            Launch.X > 0.0f && Mathf.Abs(Launch.Y) < Mathf.Abs(Launch.X) * 0.05f;
    }

    private readonly record struct Shot(
        bool Connected,
        float DeliveredImpulse,
        float EpisodeImpulse,
        float Pain,
        long MilliCredits,
        string Part,

        /// <summary>How squarely the barrel pointed at the head when the trigger went.</summary>
        float AimError,
        float RangePx,
        float TravelPx,
        Vector2 Launch)
    {
        public string Geometry =>
            $"aim_dot={AimError:F3} range={RangePx:F1}px travel={TravelPx:F1}px launch={Launch}";
    }
}
