using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Sandbox;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Tools;

/// <summary>
/// Driver for every cursor gun (RAGDOLL §9.1/§9.2). It is deliberately thin: the aim
/// lifecycle is <see cref="CursorAim"/>, the cadence/magazine/reload rules are
/// <see cref="GunMachine"/>, and this component only reads the routed input frame,
/// feeds those two models, and launches pooled physical projectiles attributed to the
/// firing tool.
///
/// <para>Guns are authored <see cref="GunProfile"/> resources rather than separate
/// components, exactly as cursor-tethered tools are authored
/// <see cref="CursorToolProfile"/> resources: the Shotgun is a `.tres` plus a content
/// ID, not new input code.</para>
///
/// <para>Projectiles are bounded by their own preallocated pool and never enter the
/// <see cref="Objects.LooseObjectRegistry"/>, so they cannot consume one of the 24
/// loose-object slots (RAGDOLL §10). The pool keeps ticking while no gun is selected,
/// because a shot already in flight belongs to the room, not to the tool that is
/// currently in the player's hand.</para>
///
/// <para>A magazine persists per gun for the session rather than refilling whenever
/// the tool is reselected: putting a gun away mid-reload and drawing it again resumes
/// that reload instead of skipping it.</para>
/// </summary>
[GlobalClass]
public partial class CursorGunComponent : Node2D
{
    [Export] public Godot.Collections.Array<GunProfile> Profiles { get; set; } = new();
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;

    private readonly List<GunRuntime> _runtimes = new();
    private GunRuntime? _active;
    private Vector2 _cursor;
    private Vector2 _previousCursor;
    private bool _hasCursor;
    private bool _triggerHeld;
    private bool _triggerLatched;
    private bool _pendingReload;
    private int _pendingWheelSteps;

    public bool IsInitialized { get; private set; }

    /// <summary>True while a selected gun has a cursor to aim from.</summary>
    public bool IsActive => _active is not null;

    public GunProfile? ActiveProfile => _active?.Profile;

    /// <summary>The content ID a live gun attributes its shots to, or <c>null</c>.</summary>
    public string? ActiveContentId => _active?.Profile.ContentId;

    /// <summary>The cursor a live gun aims from, in sandbox coordinates.</summary>
    public Vector2 Cursor => _cursor;

    /// <summary>Unit aim direction including the wheel offset; zero before the pointer moves.</summary>
    public Vector2 AimForward { get; private set; }

    /// <summary>The wheel offset in force, in degrees; positive aims upward.</summary>
    public float AimOffsetDegrees => _active?.Aim.OffsetDegrees ?? 0.0f;

    /// <summary>
    /// Smoothed pointer speed in pixels per routed tick, and whether it is above the
    /// authored gate. Together they say whether the player is aiming right now, which is
    /// what decides when the wheel offset survives and when the next motion drops it.
    /// </summary>
    public float AimSmoothedSpeed { get; private set; }

    public bool AimIsSteering { get; private set; }

    public int RoundsRemaining => _active?.Phase.Rounds ?? 0;
    public bool IsReloading => _active?.Phase.IsReloading ?? false;
    public int ReloadTicksRemaining => _active?.Phase.ReloadTicksRemaining ?? 0;

    // Telemetry consumed by scenarios, journeys, and the laboratory panel.

    /// <summary>Shots that really left the barrel, aimed somewhere.</summary>
    public int ShotCount { get; private set; }
    public int DryFireCount { get; private set; }

    /// <summary>
    /// Rounds the model spent on a tick where the gun had no aim to fire along, so no
    /// projectile could be launched. This must stay zero: a round the player never saw
    /// leave the gun is the defect this counter was added to catch, and it is kept after
    /// the fix so a regression shows up as telemetry rather than as a mystery.
    /// </summary>
    public int ShotsSpentWithoutAim { get; private set; }
    public int ReloadStartCount { get; private set; }
    public int ReloadCompleteCount { get; private set; }
    public int ProjectilesLaunched { get; private set; }
    public int PoolExhaustedCount { get; private set; }

    /// <summary>Projectiles currently in flight across every gun's pool.</summary>
    public int ActiveProjectileCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _runtimes.Count; index++)
                count += _runtimes[index].LiveCount;
            return count;
        }
    }

    public void Initialize()
    {
        if (Profiles.Count == 0)
        {
            throw new InvalidOperationException(
                "CursorGunComponent requires at least one authored GunProfile.");
        }

        for (int index = 0; index < Profiles.Count; index++)
        {
            GunProfile? profile = Profiles[index];
            if (!GodotObject.IsInstanceValid(profile) || profile!.Validate().Count > 0)
            {
                throw new InvalidOperationException(
                    $"CursorGunComponent requires valid gun profiles (entry {index} is not).");
            }

            // Two profiles claiming one tool would make the live gun depend on array
            // order, which is the silent data ambiguity the catalogue spine exists to
            // prevent.
            for (int other = 0; other < index; other++)
            {
                if (Profiles[other]!.ContentId == profile.ContentId)
                {
                    throw new InvalidOperationException(
                        $"CursorGunComponent has two profiles for '{profile.ContentId}'.");
                }
            }
        }

        if (!GodotObject.IsInstanceValid(Pipeline) || !GodotObject.IsInstanceValid(Boundaries))
        {
            throw new InvalidOperationException(
                "CursorGunComponent requires the interaction pipeline and room boundaries.");
        }

        IsInitialized = true;
    }

    /// <summary>True when the named tool is one this component gives a gun to.</summary>
    public bool DrivesTool(ToolId tool) => AttributesContent(ContentIds.ForTool(tool));

    /// <summary>
    /// True when the content ID belongs to one of this component's authored guns.
    /// Identity, not liveness: a projectile's pain is attributed to the gun that fired
    /// it, and code reacting to that hit must not depend on the gun still being
    /// selected when the reaction arrives.
    /// </summary>
    public bool AttributesContent(string? contentId)
    {
        if (contentId is null)
            return false;

        for (int index = 0; index < Profiles.Count; index++)
        {
            if (GodotObject.IsInstanceValid(Profiles[index]) &&
                Profiles[index]!.ContentId == contentId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Move the cursor the active gun aims from (sandbox coordinates).</summary>
    public void MoveCursor(Vector2 worldPoint)
    {
        _cursor = ClampToPlayableBounds(worldPoint);
        _hasCursor = true;
    }

    /// <summary>
    /// Invalidates the cursor when the real pointer leaves the play window. The tool
    /// stays selected, but a gun nobody is holding must not keep aiming or firing.
    /// </summary>
    public void ClearCursor()
    {
        _hasCursor = false;
        _triggerHeld = false;
        _triggerLatched = false;
        _pendingReload = false;
        _pendingWheelSteps = 0;
        QueueRedraw();
    }

    /// <summary>Primary button state. A press edge fires exactly one shot.</summary>
    public void SetTriggerHeld(bool held)
    {
        _triggerHeld = held;
        if (held)
        {
            // Latched so a press and release that both land inside one routed tick
            // still fires; the model's own edge rule keeps it to a single shot.
            _triggerLatched = true;
        }
    }

    /// <summary>
    /// Records a trigger press whose release has already happened, so a click that began
    /// and ended between two routed ticks still fires. The level state is untouched: this
    /// says "the button went down since you last looked", not "it is down now".
    /// </summary>
    public void LatchTrigger() => _triggerLatched = true;

    /// <summary>The <c>buddy_reload</c> action was pressed (bound to <c>R</c>).</summary>
    public void RequestReload() => _pendingReload = true;

    /// <summary>Mouse-wheel aim offset: positive steps aim upward.</summary>
    public void ApplyWheel(int steps)
    {
        if (steps == 0)
            return;

        _pendingWheelSteps += steps;
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick()
    {
        RequireInitialized();

        GunProfile? wanted = _hasCursor ? ProfileFor(Pipeline.SelectedTool) : null;
        GunRuntime? runtime = wanted is null ? null : RuntimeFor(wanted);
        if (!ReferenceEquals(runtime, _active))
        {
            // Drawing or holstering resets the aim: forward comes from live pointer
            // travel, and a stale direction from the last time this gun was out would
            // let the first shot go somewhere the player never pointed.
            if (runtime is not null)
                runtime.Aim = CursorAimState.Initial;
            AimForward = Vector2.Zero;
            AimSmoothedSpeed = 0.0f;
            AimIsSteering = false;
            _previousCursor = _cursor;
            _active = runtime;
            // Holstering has to erase the drawn barrel: the ordinary redraw at the end
            // of this method is only reached while a gun is live.
            QueueRedraw();
        }

        // Live projectiles are the room's, not the current tool's: they keep flying
        // and keep resolving their own hits after the gun is put away.
        AdvanceProjectiles();

        bool trigger = _triggerHeld || _triggerLatched;
        bool reload = _pendingReload;
        int wheelSteps = _pendingWheelSteps;
        _triggerLatched = false;
        _pendingReload = false;
        _pendingWheelSteps = 0;

        if (_active is null)
        {
            _previousCursor = _cursor;
            return;
        }

        GunRuntime gun = _active;
        GunProfile profile = gun.Profile;

        Vector2 motion = _cursor - _previousCursor;
        CursorAimResult aim = CursorAim.Tick(new CursorAimInput(
            gun.Aim,
            new NumericsVector2(motion.X, motion.Y),
            wheelSteps,
            profile.ToAimConstants()));
        gun.Aim = aim.State;
        AimForward = aim.IsValid ? new Vector2(aim.Forward.X, aim.Forward.Y) : Vector2.Zero;
        AimSmoothedSpeed = aim.SmoothedSpeed;
        AimIsSteering = aim.IsSteering;

        // A trigger press while the gun has no aim is not a shot. There is no direction
        // to fire along, so nothing would leave the barrel, and spending the round anyway
        // is invisible: the player clicks, sees nothing, clicks again, and the magazine
        // empties into a reload they never asked for. That is the reported "it takes a few
        // clicks before ammo comes out" — the aim is wiped whenever the pointer leaves the
        // play area, which is exactly what sweeping across it does.
        bool aimed = aim.IsValid && AimForward != Vector2.Zero;
        GunResult shot = GunMachine.Tick(new GunInput(
            gun.Phase, trigger && aimed, reload, profile.ToGunConstants()));
        gun.Phase = shot.Phase;

        if (shot.ReloadStarted)
            ReloadStartCount++;

        if (shot.ReloadCompleted)
            ReloadCompleteCount++;

        if (shot.DryFired)
            DryFireCount++;

        if (shot.Fired)
        {
            if (AimForward != Vector2.Zero)
            {
                LaunchShot(gun, shot);
                ShotCount++;
            }
            else
            {
                // Unreachable while the trigger is gated on a valid aim, and kept anyway:
                // if that gate ever regresses, this counter says so out loud instead of
                // letting rounds disappear again.
                ShotsSpentWithoutAim++;
            }
        }

        _previousCursor = _cursor;
        QueueRedraw();
    }

    private void LaunchShot(GunRuntime gun, in GunResult shot)
    {
        GunProfile profile = gun.Profile;
        Vector2 forward = AimForward;
        Vector2 muzzle = ClampInsideRoom(
            _cursor + (forward * profile.MuzzleOffsetPx), profile.ProjectileRadius);
        for (int index = 0; index < shot.Projectiles; index++)
        {
            ProjectileBody? projectile = gun.TryTake();
            if (projectile is null)
            {
                // The pool is the bound; refusing is the honest outcome and it is
                // counted so a too-small pool shows up as telemetry, not as shots
                // that quietly never existed.
                PoolExhaustedCount++;
                break;
            }

            Vector2 direction = SpreadDirection(forward, index, shot.Projectiles, profile);
            projectile.Launch(muzzle, direction * profile.MuzzleSpeed);
            ProjectilesLaunched++;
        }
    }

    /// <summary>
    /// Fans a multi-projectile shot evenly across the authored cone. The pattern is
    /// deterministic rather than random: a seeded scenario has to be able to state
    /// where every pellet went, and a fixed fan is also a more readable spread than
    /// noise at these speeds.
    /// </summary>
    private static Vector2 SpreadDirection(
        Vector2 forward,
        int index,
        int count,
        GunProfile profile)
    {
        if (count <= 1 || profile.SpreadHalfAngleDegrees <= 0.0f)
            return forward;

        float fraction = (2.0f * index / (count - 1)) - 1.0f;
        return forward.Rotated(Mathf.DegToRad(fraction * profile.SpreadHalfAngleDegrees));
    }

    private void AdvanceProjectiles()
    {
        for (int index = 0; index < _runtimes.Count; index++)
            _runtimes[index].Advance();
    }

    private GunProfile? ProfileFor(ToolId tool)
    {
        string contentId = ContentIds.ForTool(tool);
        for (int index = 0; index < Profiles.Count; index++)
        {
            GunProfile? profile = Profiles[index];
            if (GodotObject.IsInstanceValid(profile) && profile!.ContentId == contentId)
                return profile;
        }

        return null;
    }

    /// <summary>
    /// The per-gun runtime, built on first use and kept for the session. Building it
    /// allocates its whole pool at once, on a tool-selection tick rather than on a
    /// firing tick, so nothing is allocated on the 120 Hz path.
    /// </summary>
    private GunRuntime RuntimeFor(GunProfile profile)
    {
        for (int index = 0; index < _runtimes.Count; index++)
        {
            if (ReferenceEquals(_runtimes[index].Profile, profile))
                return _runtimes[index];
        }

        var runtime = new GunRuntime(profile);
        runtime.BuildPool(this);
        _runtimes.Add(runtime);
        return runtime;
    }

    /// <summary>
    /// Whether the legacy 2D presentation draws this gun. The 3D presentation has its own
    /// gun (<c>CursorGunVisual3D</c>), and drawing both would put two barrels on one
    /// cursor.
    /// </summary>
    public bool DrawsLegacyGun { get; private set; } = true;

    public void SetLegacyVisualEnabled(bool enabled)
    {
        DrawsLegacyGun = enabled;
        QueueRedraw();
    }

    /// <summary>
    /// The drawn barrel mouth in world pixels, or zero when no gun is drawn. This is the
    /// authored visual length rather than the launch offset on purpose: profile validation
    /// ties the two together, and a check that read the launch offset for both sides of
    /// that comparison would be comparing a number with itself.
    /// </summary>
    public Vector2 VisualMuzzle2D => _active is null || !_hasCursor || AimForward == Vector2.Zero
        ? Vector2.Zero
        : _cursor + (AimForward * _active.Profile.VisualMuzzleTipPx);

    public override void _Draw()
    {
        // The legacy 2D view of the gun: a flat silhouette at the same dimensions the 3D
        // one is built to, so the two modes agree about where the player's barrel is.
        // Presentation only — the gun has no collider and nothing here touches gameplay.
        if (_active is null || !_hasCursor || AimForward == Vector2.Zero || !DrawsLegacyGun)
            return;

        GunProfile profile = _active.Profile;
        Vector2 origin = ToLocal(_cursor);
        Vector2 forward = AimForward;
        // Mirrored the same way the 3D silhouette is: rotating a side-on gun past vertical
        // would hang its grip in the air.
        Vector2 down = new Vector2(-forward.Y, forward.X) * (forward.X < 0.0f ? -1.0f : 1.0f);
        float length = profile.VisualLengthPx;
        float tip = profile.VisualMuzzleTipPx;

        Vector2 At(float along, float across) =>
            origin + (forward * (length * along)) + (down * (length * across));

        Vector2 Tip(float fromTip, float across) =>
            origin + (forward * (tip + (length * fromTip))) + (down * (length * across));

        DrawColoredPolygon(
            new[] { At(0.08f, -0.17f), At(0.60f, -0.17f), At(0.60f, 0.17f), At(0.08f, 0.17f) },
            profile.MuzzleColor);
        DrawColoredPolygon(
            new[] { At(0.54f, -0.10f), Tip(-0.04f, -0.10f), Tip(-0.04f, 0.10f), At(0.54f, 0.10f) },
            profile.MuzzleColor);
        DrawColoredPolygon(
            new[] { At(0.06f, 0.13f), At(0.26f, 0.13f), At(0.22f, 0.47f), At(0.04f, 0.47f) },
            profile.AccentColor);
        // The tip accent sits on the barrel mouth itself, which is the point of it.
        DrawColoredPolygon(
            new[] { Tip(-0.09f, -0.15f), Tip(0.0f, -0.15f), Tip(0.0f, 0.15f), Tip(-0.09f, 0.15f) },
            profile.AccentColor);
    }

    private Vector2 ClampToPlayableBounds(Vector2 worldPoint)
    {
        Rect2 bounds = Boundaries.InnerBounds;
        if (!bounds.HasArea())
            return worldPoint;

        return new Vector2(
            Mathf.Clamp(worldPoint.X, bounds.Position.X, bounds.End.X),
            Mathf.Clamp(worldPoint.Y, bounds.Position.Y, bounds.End.Y));
    }

    private Vector2 ClampInsideRoom(Vector2 position, float radius)
    {
        Rect2 bounds = Boundaries.InnerBounds;
        if (!bounds.HasArea())
            return position;

        float minimumX = bounds.Position.X + radius;
        float maximumX = bounds.End.X - radius;
        float minimumY = bounds.Position.Y + radius;
        float maximumY = bounds.End.Y - radius;
        if (maximumX < minimumX || maximumY < minimumY)
            return bounds.GetCenter();

        return new Vector2(
            Mathf.Clamp(position.X, minimumX, maximumX),
            Mathf.Clamp(position.Y, minimumY, maximumY));
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("CursorGunComponent used before initialization.");
    }

    /// <summary>
    /// One gun's live state: its immutable model phases plus its preallocated
    /// projectile pool. Held by the component, never by a Resource.
    /// </summary>
    private sealed class GunRuntime
    {
        private ProjectileBody[] _pool = Array.Empty<ProjectileBody>();

        public GunRuntime(GunProfile profile)
        {
            Profile = profile;
            Phase = GunPhase.FullyLoaded(profile.ToGunConstants());
            Aim = CursorAimState.Initial;
        }

        public GunProfile Profile { get; }

        public GunPhase Phase { get; set; }

        public CursorAimState Aim { get; set; }

        /// <summary>
        /// Projectiles in flight right now, counted on demand: a shot launched later
        /// in this same tick must be visible to a scenario immediately, not only after
        /// the next advance.
        /// </summary>
        public int LiveCount
        {
            get
            {
                int live = 0;
                for (int index = 0; index < _pool.Length; index++)
                {
                    if (_pool[index].State == ProjectileState.Live)
                        live++;
                }

                return live;
            }
        }

        public void BuildPool(Node parent)
        {
            _pool = new ProjectileBody[Math.Max(1, Profile.PoolCapacity)];
            string prefix = Profile.ContentId.Replace("tool.", string.Empty).Replace('_', '-');
            for (int index = 0; index < _pool.Length; index++)
            {
                var projectile = new ProjectileBody { Name = $"{prefix}-shot-{index + 1}" };
                projectile.Configure(Profile);
                parent.AddChild(projectile);
                _pool[index] = projectile;
            }
        }

        public ProjectileBody? TryTake()
        {
            for (int index = 0; index < _pool.Length; index++)
            {
                if (_pool[index].State == ProjectileState.Pooled)
                    return _pool[index];
            }

            return null;
        }

        public void Advance()
        {
            for (int index = 0; index < _pool.Length; index++)
            {
                ProjectileBody projectile = _pool[index];
                if (projectile.State == ProjectileState.Pooled)
                    continue;

                if (projectile.Advance(
                        Profile.ProjectileLifetimeTicks,
                        Profile.ProjectileMaxTravelPx,
                        Profile.ContactSettleTicks,
                        Profile.SpentLingerTicks))
                {
                    projectile.Park();
                }
            }
        }
    }
}
