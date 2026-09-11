using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Sandbox;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Tools;

/// <summary>One projectile flight that ended in a loose object.</summary>
public readonly record struct ProjectileStrike(string ContentId, LooseObjectBody Body);

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
    /// <summary>
    /// Salt for this component's scatter stream. Distinct per consumer family, per the
    /// <see cref="IRandomSource"/> contract: where a shotgun's pellets land must not be able
    /// to perturb where the buddy decides to walk.
    /// </summary>
    private const ulong SpreadStreamSalt = 0x5A17_D06E_5C47_7E11UL;

    /// <summary>
    /// The scatter stream before anything reseeds it. A gun that fired in a scene nobody
    /// seeded must still be reproducible, so this is a fixed constant rather than a clock.
    /// </summary>
    private const ulong DefaultSpreadSeed = 0x9E37_79B9_7F4A_7C15UL;

    /// <summary>Steps the per-shot cone and each pellet's angle are quantised to.</summary>
    private const int SpreadResolution = 4096;

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
    private int _flashTicks;
    private int _flashDuration;
    private int _recoilTicks;
    private int _recoilDuration;
    private IRandomSource _spread = new SeededRandomSource(DefaultSpreadSeed);

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

    // --- Presentation punctuation (real guns; a toy authors all of it off) ---

    /// <summary>
    /// Raised on the routed tick a round really leaves the barrel, with the gun that fired
    /// it. The camera kick lives outside this component — a gun does not own the camera —
    /// so the composition root listens here and drives it.
    /// </summary>
    public event Action<GunProfile>? ShotFired;

    /// <summary>Raised when a gun actually begins its authored reload.</summary>
    public event Action<GunProfile>? ReloadStarted;

    /// <summary>
    /// Raised when a pump-action gun works its slide between shots. Mechanically it is the
    /// shotgun's per-shot chambering, so presentation treats it as a reload of one shell.
    /// </summary>
    public event Action<GunProfile>? PumpStarted;

    /// <summary>
    /// Raised on the routed tick a shot connects with anything at all — the buddy, a loose
    /// object, a wall. Presentation only: what the hit does was already decided by the impact
    /// pipeline before this fires.
    /// </summary>
    public event Action<GunProfile>? ProjectileHit;

    /// <summary>One round connecting with a loose object, once per flight.</summary>
    public event Action<ProjectileStrike>? LooseObjectStruck;

    /// <summary>Muzzle flash strength, 1 on the firing tick and 0 once it has burned out.</summary>
    public float MuzzleFlashStrength =>
        _flashTicks <= 0 || _flashDuration <= 0 ? 0.0f : (float)_flashTicks / _flashDuration;

    /// <summary>Where the flash is drawn: the barrel mouth the round came out of.</summary>
    public Vector2 MuzzleFlashPoint { get; private set; }

    /// <summary>
    /// How far the drawn gun is pushed back right now, in world pixels. Presentation only:
    /// the aim model stays the single source of truth for direction, so recoil can never
    /// walk a burst off target.
    /// </summary>
    public Vector2 RecoilOffset2D { get; private set; }

    /// <summary>Cosmetic magazines currently lying on the floor.</summary>
    public int ActiveMagazineCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _runtimes.Count; index++)
                count += _runtimes[index].LiveMagazines;
            return count;
        }
    }

    /// <summary>Spent cases currently on the cosmetic ejection lane.</summary>
    public int ActiveCasingCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _runtimes.Count; index++)
                count += _runtimes[index].LiveCasings;
            return count;
        }
    }

    public int MagazinesDropped { get; private set; }
    public int CasingsEjected { get; private set; }

    public int RoundsRemaining => _active?.Phase.Rounds ?? 0;
    public bool IsReloading => _active?.Phase.IsReloading ?? false;
    public int ReloadTicksRemaining => _active?.Phase.ReloadTicksRemaining ?? 0;
    public bool IsPumping => _active?.Phase.IsPumping ?? false;
    public bool NeedsPump => _active?.Phase.ChamberEmpty ?? false;
    public int PumpTicksRemaining => _active?.Phase.PumpTicksRemaining ?? 0;

    /// <summary>Current pump-forend travel back toward the stock, in world pixels.</summary>
    public float PumpSlideOffsetPx
    {
        get
        {
            if (_active is not { Phase.IsPumping: true } gun || gun.Profile.PumpTicks <= 0)
                return 0.0f;

            float progress = 1.0f - ((float)gun.Phase.PumpTicksRemaining / gun.Profile.PumpTicks);
            float stroke = 1.0f - Mathf.Abs((2.0f * progress) - 1.0f);
            return gun.Profile.VisualLengthPx * gun.Profile.PumpSlideFraction *
                Mathf.Clamp(stroke, 0.0f, 1.0f);
        }
    }

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
    public int PumpStartCount { get; private set; }
    public int PumpCompleteCount { get; private set; }

    /// <summary>The randomized half-angle selected for the most recent real shot.</summary>
    public float LastShotSpreadHalfAngleDegrees { get; private set; }

    public float ShoveImpulseDelivered => _active?.ShoveImpulseDelivered ?? 0.0f;
    public float PeakShoveImpulse => _active?.PeakShoveImpulse ?? 0.0f;

    /// <summary>
    /// The identity every pellet of the last multi-projectile shot was stamped with, or
    /// <c>0</c> when the last shot was a single projectile that minted its own. Telemetry
    /// only: it exists so a scenario can prove the six pellets really are one interaction
    /// rather than inferring it from the count of accepted impacts.
    /// </summary>
    public int LastShotInteractionId { get; private set; }

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
            ClearGunPunctuation();
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

        AdvancePunctuation(profile);

        if (shot.ReloadStarted)
        {
            ReloadStartCount++;
            if (profile.DropsMagazineOnReload)
                DropMagazine(gun);
            ReloadStarted?.Invoke(profile);
        }

        if (shot.ReloadCompleted)
            ReloadCompleteCount++;

        if (shot.PumpStarted)
        {
            PumpStartCount++;
            PumpStarted?.Invoke(profile);
        }

        if (shot.PumpCompleted)
            PumpCompleteCount++;

        if (shot.DryFired)
            DryFireCount++;

        if (shot.Fired)
        {
            if (AimForward != Vector2.Zero)
            {
                LaunchShot(gun, shot);
                ShotCount++;
                BeginPunctuation(profile);
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

        // One trigger pull is one interaction. A spread gun's pellets share this identity,
        // so the impact router's (source, part) episode key makes six pellets arriving on
        // one part a single scored impact instead of six — the shotgun hurts by covering
        // parts, not by concentrating on one (RAGDOLL §7.1–7.2, DECISIONS M5 Task 9). A
        // single-projectile gun passes null and mints per launch exactly as it always has,
        // which is the behaviour the pistol and nerf regressions pin.
        int? sharedShotId = shot.Projectiles > 1 ? InteractionIds.Next() : null;
        LastShotInteractionId = sharedShotId ?? 0;
        // Drawn once for the whole shot, before any pellet: the cone is a property of the
        // trigger pull, and drawing it per pellet would be a different weapon.
        ChooseShotCone(profile);
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
            projectile.Launch(muzzle, direction * profile.MuzzleSpeed, sharedShotId);
            ProjectilesLaunched++;
        }

        if (profile.EjectsCasingOnShot)
            EjectCasing(gun);
    }

    /// <summary>
    /// Where pellet <paramref name="index"/> of a shot goes.
    ///
    /// <para>A gun that authors no <see cref="GunProfile.SpreadMaxHalfAngleDegrees"/> fans
    /// its pellets evenly across the one authored cone, index by index — the platform's
    /// original deterministic pattern, kept because a future gun may want it.</para>
    ///
    /// <para>A gun that does scatters instead: <see cref="ChooseShotCone"/> has already drawn
    /// this shot's half-angle from the authored band, and each pellet now draws its own angle
    /// inside it, so no two bursts are the same burst. Both draws come from this component's
    /// seeded stream, never from <see cref="System.Random"/>, so a replayed seed still
    /// reproduces every pellet exactly.</para>
    /// </summary>
    private Vector2 SpreadDirection(
        Vector2 forward,
        int index,
        int count,
        GunProfile profile)
    {
        if (count <= 1 || LastShotSpreadHalfAngleDegrees <= 0.0f)
            return forward;

        if (!profile.ScattersPerShot)
        {
            float fraction = (2.0f * index / (count - 1)) - 1.0f;
            return forward.Rotated(
                Mathf.DegToRad(fraction * LastShotSpreadHalfAngleDegrees));
        }

        return forward.Rotated(Mathf.DegToRad(
            ((Unit() * 2.0f) - 1.0f) * LastShotSpreadHalfAngleDegrees));
    }

    /// <summary>
    /// Draws the cone this one trigger pull opens to, and records it. A scattering gun takes
    /// a fresh half-angle from its authored band on every shot — the owner's feedback is that
    /// a shotgun which patterns identically twice does not read as a shotgun — and every gun
    /// without a band simply reports its single authored angle.
    /// </summary>
    private void ChooseShotCone(GunProfile profile)
    {
        if (!profile.ScattersPerShot)
        {
            LastShotSpreadHalfAngleDegrees = profile.SpreadHalfAngleDegrees;
            return;
        }

        LastShotSpreadHalfAngleDegrees = Mathf.Lerp(
            profile.SpreadHalfAngleDegrees, profile.SpreadMaxHalfAngleDegrees, Unit());
    }

    /// <summary>One draw from the scatter stream, in <c>[0,1)</c>.</summary>
    private float Unit() => _spread.NextInt(0, SpreadResolution) / (float)SpreadResolution;

    /// <summary>
    /// Reseeds the scatter stream. Called from whatever reseeds the rest of the simulation,
    /// so a replayed seed replays the pellet pattern too; the salt keeps this stream separate
    /// from the buddy's own decisions.
    /// </summary>
    public void ReseedSpread(ulong seed)
    {
        Seed = seed;
        _spread = new SeededRandomSource(seed ^ SpreadStreamSalt);
    }

    /// <summary>The seed the scatter stream is running from.</summary>
    public ulong Seed { get; private set; } = DefaultSpreadSeed;

    private void AdvanceProjectiles()
    {
        for (int index = 0; index < _runtimes.Count; index++)
        {
            int hits = _runtimes[index].Advance();
            for (int hit = 0; hit < hits; hit++)
                ProjectileHit?.Invoke(_runtimes[index].Profile);
        }
    }

    /// <summary>
    /// Starts this gun's authored punctuation on a real launch. Non-stacking by
    /// construction: a shot inside a live flash restarts the envelope rather than adding
    /// to it, so rapid fire cannot pile one effect on another.
    /// </summary>
    private void BeginPunctuation(GunProfile profile)
    {
        MuzzleFlashPoint = _cursor + (AimForward * profile.MuzzleOffsetPx);
        _flashDuration = profile.MuzzleFlashTicks;
        _flashTicks = profile.MuzzleFlashTicks;
        _recoilDuration = profile.RecoilTicks;
        _recoilTicks = profile.RecoilTicks;
        RecoilOffset2D = _recoilTicks <= 0 || _recoilDuration <= 0
            ? Vector2.Zero
            : -AimForward * profile.RecoilKickPx;
        ShotFired?.Invoke(profile);
    }

    private void ClearGunPunctuation()
    {
        _flashTicks = 0;
        _flashDuration = 0;
        _recoilTicks = 0;
        _recoilDuration = 0;
        MuzzleFlashPoint = Vector2.Zero;
        RecoilOffset2D = Vector2.Zero;
    }

    private void AdvancePunctuation(GunProfile profile)
    {
        if (_flashTicks > 0)
            _flashTicks--;

        if (_recoilTicks > 0)
            _recoilTicks--;

        RecoilOffset2D = _recoilTicks <= 0 || _recoilDuration <= 0
            ? Vector2.Zero
            : -AimForward * (profile.RecoilKickPx * ((float)_recoilTicks / _recoilDuration));
    }

    /// <summary>
    /// Throws a cosmetic magazine down and back out of the grip. It is pooled by this gun
    /// and never enters the loose-object registry — see <see cref="MagazineBody"/>.
    /// </summary>
    private void DropMagazine(GunRuntime gun)
    {
        MagazineBody? magazine = gun.TakeMagazine();
        if (magazine is null)
            return;

        GunProfile profile = gun.Profile;
        Vector2 forward = AimForward == Vector2.Zero ? Vector2.Right : AimForward;
        Vector2 grip = _cursor + (forward * (profile.VisualLengthPx * 0.12f)) +
                       new Vector2(0.0f, profile.VisualLengthPx * 0.28f);
        // Down and backwards out of the grip, with a lazy tumble.
        Vector2 velocity = (-forward * 40.0f) + new Vector2(0.0f, 30.0f);
        magazine.Drop(
            ClampInsideRoom(grip, profile.VisualLengthPx * 0.2f),
            velocity,
            forward.X >= 0.0f ? 6.0f : -6.0f,
            profile.MagazineLingerTicks);
        MagazinesDropped++;
    }

    private void EjectCasing(GunRuntime gun)
    {
        MagazineBody? casing = gun.TakeCasing();
        if (casing is null)
            return;

        GunProfile profile = gun.Profile;
        Vector2 forward = AimForward == Vector2.Zero ? Vector2.Right : AimForward;
        Vector2 up = new Vector2(forward.Y, -forward.X);
        Vector2 port = _cursor + (forward * (profile.VisualLengthPx * 0.28f)) +
                       (up * (profile.VisualLengthPx * 0.10f));
        casing.Drop(
            ClampInsideRoom(port, profile.VisualLengthPx * profile.CasingLengthFraction),
            (-forward * 55.0f) + (up * 95.0f),
            forward.X >= 0.0f ? 10.0f : -10.0f,
            profile.MagazineLingerTicks);
        CasingsEjected++;
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

        var runtime = new GunRuntime(profile)
        {
            LooseObjectStruck = strike => LooseObjectStruck?.Invoke(strike),
        };
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
        if (profile.Visual3DKind == GunVisual3DKind.Shotgun)
        {
            float slide = PumpSlideOffsetPx / length;
            DrawColoredPolygon(
                new[] { At(0.48f - slide, 0.05f), At(0.72f - slide, 0.05f),
                        At(0.72f - slide, 0.20f), At(0.48f - slide, 0.20f) },
                profile.AccentColor);
            // Twice the old stock length, extending behind the cursor.
            DrawColoredPolygon(
                new[] { At(-0.36f, -0.03f), At(0.20f, -0.03f),
                        At(0.20f, 0.16f), At(-0.36f, 0.16f) },
                profile.AccentColor);
        }
        // The tip accent sits on the barrel mouth itself, which is the point of it.
        DrawColoredPolygon(
            new[] { Tip(-0.09f, -0.15f), Tip(0.0f, -0.15f), Tip(0.0f, 0.15f), Tip(-0.09f, 0.15f) },
            profile.AccentColor);

        // The flash, when a gun authors one: three rays at the mouth, scale-popping down
        // over its authored ticks. Never drawn on a dry fire — it is started by a launch.
        float flash = MuzzleFlashStrength;
        if (flash <= 0.0f)
            return;

        Vector2 mouth = ToLocal(MuzzleFlashPoint);
        float reach = length * 0.30f * flash * profile.MuzzleFlashScale;
        var glow = new Color(1.0f, 0.93f, 0.62f, Mathf.Clamp(flash, 0.0f, 1.0f));
        DrawLine(mouth, mouth + (forward * reach), glow, 3.0f, true);
        DrawLine(mouth, mouth + (down * (reach * 0.55f)), glow, 2.0f, true);
        DrawLine(mouth, mouth - (down * (reach * 0.55f)), glow, 2.0f, true);
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
        private MagazineBody[] _magazines = Array.Empty<MagazineBody>();
        private MagazineBody[] _casings = Array.Empty<MagazineBody>();

        /// <summary>
        /// Raised for a round that connected with a loose object. Set by the owning component
        /// once, so the pool can report a hit without holding a reference back to it.
        /// </summary>
        public Action<ProjectileStrike>? LooseObjectStruck { get; set; }

        public GunRuntime(GunProfile profile)
        {
            Profile = profile;
            Phase = GunPhase.FullyLoaded(profile.ToGunConstants());
            Aim = CursorAimState.Initial;
        }

        public GunProfile Profile { get; }

        /// <summary>Extra knockback this gun's shots have delivered, summed over the session.</summary>
        public float ShoveImpulseDelivered { get; private set; }

        /// <summary>The largest single projectile shove this gun has delivered.</summary>
        public float PeakShoveImpulse { get; private set; }

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

        public int LiveMagazines
        {
            get
            {
                int live = 0;
                for (int index = 0; index < _magazines.Length; index++)
                {
                    if (_magazines[index].IsLive)
                        live++;
                }

                return live;
            }
        }

        public int LiveCasings
        {
            get
            {
                int live = 0;
                for (int index = 0; index < _casings.Length; index++)
                {
                    if (_casings[index].IsLive)
                        live++;
                }

                return live;
            }
        }

        public MagazineBody? TakeMagazine()
        {
            for (int index = 0; index < _magazines.Length; index++)
            {
                if (!_magazines[index].IsLive)
                    return _magazines[index];
            }

            return null;
        }

        public MagazineBody? TakeCasing()
        {
            for (int index = 0; index < _casings.Length; index++)
            {
                if (!_casings[index].IsLive)
                    return _casings[index];
            }

            return null;
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

            if (Profile.DropsMagazineOnReload)
            {
                _magazines = new MagazineBody[GunProfile.MagazinePoolCapacity];
                for (int index = 0; index < _magazines.Length; index++)
                {
                    var magazine = new MagazineBody { Name = $"{prefix}-magazine-{index + 1}" };
                    magazine.Configure(Profile);
                    parent.AddChild(magazine);
                    _magazines[index] = magazine;
                }
            }

            if (Profile.EjectsCasingOnShot)
            {
                _casings = new MagazineBody[GunProfile.CasingPoolCapacity];
                for (int index = 0; index < _casings.Length; index++)
                {
                    var casing = new MagazineBody { Name = $"{prefix}-casing-{index + 1}" };
                    casing.Configure(Profile, asCasing: true);
                    parent.AddChild(casing);
                    _casings[index] = casing;
                }
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

        /// <returns>How many projectiles connected on this tick.</returns>
        public int Advance()
        {
            int hits = 0;
            for (int index = 0; index < _pool.Length; index++)
            {
                ProjectileBody projectile = _pool[index];
                if (projectile.State == ProjectileState.Pooled)
                    continue;

                bool wasLive = projectile.State == ProjectileState.Live;

                bool finished = projectile.Advance(
                    Profile.ProjectileLifetimeTicks,
                    Profile.ProjectileMaxTravelPx,
                    Profile.ContactSettleTicks,
                    Profile.SpentLingerTicks);
                // After the advance, so the travel the falloff reads includes the step the
                // shot took into its target rather than the one before it.
                float shove = projectile.TryApplyContactShove(Profile);
                // What the shot hit, once per flight, so the sandbox can route the round at
                // whatever answers to being shot (owner instruction 2026-08-21).
                if (projectile.TryConsumeHitBody() is LooseObjectBody struck)
                    LooseObjectStruck?.Invoke(new ProjectileStrike(Profile.ContentId, struck));
                ShoveImpulseDelivered += shove;
                if (shove > PeakShoveImpulse)
                    PeakShoveImpulse = shove;
                // Live -> Spent is the tick the shot actually connected; an expiring shot
                // parks straight from Live and hit nothing.
                if (wasLive && projectile.State == ProjectileState.Spent)
                    hits++;
                if (finished)
                    projectile.Park();
            }

            for (int index = 0; index < _magazines.Length; index++)
            {
                if (_magazines[index].Advance())
                    _magazines[index].Park();
            }

            for (int index = 0; index < _casings.Length; index++)
            {
                if (_casings[index].Advance())
                    _casings[index].Park();
            }

            return hits;
        }
    }
}
