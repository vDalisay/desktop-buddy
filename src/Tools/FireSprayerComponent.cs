using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Sandbox;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Tools;

/// <summary>
/// Driver for the Fire Sprayer (RAGDOLL §9.2 Fire Sprayer row, §9.3 Burning, FR-010.6–
/// FR-010.9). A sibling of <see cref="CursorGunComponent"/> on the same thin-driver shape —
/// routed input frame in, domain models decide, pooled physical bodies out — and
/// deliberately not a <see cref="GunProfile"/>: <see cref="GunMachine"/> is a press-edge
/// cadence/magazine machine and this is hold-to-stream with no magazine, no reload, and no
/// press edge.
///
/// <para>The aim is the shared <see cref="CursorAim"/> the guns use (§9.1). The stream is
/// pooled <see cref="SprayDropletBody"/> instances that never enter the loose-object
/// registry (RAGDOLL §10). The timing of a burn is <see cref="BurningStatus"/>.</para>
///
/// <para><b>Burning is the only harm lane.</b> A droplet's contact does exactly two things:
/// it refreshes the burn and it records which part is alight. Pain arrives only from the
/// burn's own cadence, through
/// <see cref="InteractionDamageComponent.ApplyBlastImpulse"/> — the same sanctioned
/// contact-free entry the grenade blast uses, so the shared curve, the knockout window, the
/// payout, the harmful memory, and the <c>min(10, pain x 0.1)</c> mood loss are all
/// untouched machinery. Nothing here scales damage.</para>
///
/// <para>A burn keeps running while the tool is put away, and so does a stream already in
/// the air: both belong to the room rather than to whatever is in the player's hand.</para>
/// </summary>
[GlobalClass]
public partial class FireSprayerComponent : Node2D
{
    [Export] public FireSprayerProfile Profile { get; set; } = null!;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;

    /// <summary>The six authoritative parts, indexed by <see cref="BuddyPartId"/>.</summary>
    private readonly ScorchPhase[] _scorch = new ScorchPhase[6];

    private SprayDropletBody[] _pool = Array.Empty<SprayDropletBody>();
    private CursorAimState _aim = CursorAimState.Initial;
    private BurningPhase _burn = BurningPhase.None;
    private BuddyPartId _ignitionPart = BuddyPartId.Torso;
    private int _burnInteractionId;
    private int _emitCountdown;
    private int _dropletIndex;
    private Vector2 _cursor;
    private Vector2 _previousCursor;
    private bool _hasCursor;
    private bool _primaryHeld;
    private bool _pendingPrimaryLatch;
    private int _pendingWheelSteps;

    public bool IsInitialized { get; private set; }

    /// <summary>True while the sprayer is selected and has a cursor to aim from.</summary>
    public bool IsActive => IsInitialized && _hasCursor &&
                            Pipeline.SelectedTool == ToolId.FireSprayer;

    /// <summary>True on any tick the stream is really emitting.</summary>
    public bool IsSpraying { get; private set; }

    /// <summary>Unit aim direction including the wheel offset; zero before the pointer moves.</summary>
    public Vector2 AimForward { get; private set; }

    public float AimOffsetDegrees => _aim.OffsetDegrees;

    /// <summary>
    /// Smoothed pointer speed in pixels per routed tick, and whether it is above the authored
    /// gate. Exposed for the same reason the gun exposes them: "is the player aiming right
    /// now" is what decides when the wheel offset survives, and it is the only honest way for
    /// a scripted run to wait for an aim to come to rest instead of guessing a tick count.
    /// </summary>
    public float AimSmoothedSpeed { get; private set; }

    public bool AimIsSteering { get; private set; }

    public Vector2 Cursor => _cursor;

    /// <summary>Where the drawn nozzle mouth is, or zero when nothing is drawn.</summary>
    public Vector2 VisualMuzzle2D => !IsActive || AimForward == Vector2.Zero
        ? Vector2.Zero
        : _cursor + (AimForward * Profile.VisualMuzzleTipPx);

    // --- Burning, read by presenters, the arbiter bridge, and scenarios ---

    public bool IsBurning => _burn.IsBurning;
    public int BurnTicksRemaining => _burn.TicksRemaining;

    /// <summary>Which part the fire is on. Only meaningful while burning.</summary>
    public BuddyPartId IgnitionPart => _ignitionPart;

    /// <summary>The attribution id of the burn currently running; re-minted per episode.</summary>
    public int BurnInteractionId => _burnInteractionId;

    /// <summary>
    /// How scorched one part is right now, in <c>[0, MaxScorchDarkness]</c>. Presentation
    /// reads this and tints with it; nothing else in the game does anything with it (owner
    /// feedback 2026-08-01).
    /// </summary>
    public float ScorchOf(BuddyPartId part)
    {
        int index = (int)part;
        return index >= 0 && index < _scorch.Length ? _scorch[index].Darkness : 0.0f;
    }

    /// <summary>Whether one part's mark is holding at full strength before its fade begins.</summary>
    public bool ScorchIsHolding(BuddyPartId part)
    {
        int index = (int)part;
        return index >= 0 && index < _scorch.Length && _scorch[index].IsHolding;
    }

    /// <summary>Whether one part's mark is on its way back to clean skin.</summary>
    public bool ScorchIsFading(BuddyPartId part)
    {
        int index = (int)part;
        return index >= 0 && index < _scorch.Length && _scorch[index].IsFading;
    }

    /// <summary>The darkest any part is right now — the scenario's whole-buddy readout.</summary>
    public float PeakScorch
    {
        get
        {
            float peak = 0.0f;
            for (int index = 0; index < _scorch.Length; index++)
                peak = Mathf.Max(peak, _scorch[index].Darkness);
            return peak;
        }
    }

    /// <summary>
    /// Signed direction the buddy should run to get away from the fire: away from the
    /// player's cursor while the stream is live, else away from the nearer wall. The
    /// composition root hands this to the arbiter as its priority-3 flee direction.
    /// </summary>
    public float HazardFleeDirection { get; private set; } = 1.0f;

    // --- Telemetry consumed by scenarios, journeys, and the laboratory panel ---

    public int DropletsLaunched { get; private set; }
    public int PoolExhaustedCount { get; private set; }
    public int IgnitionCount { get; private set; }
    public int BurnPainEventCount { get; private set; }
    public float LastBurnPain { get; private set; }
    public float TotalBurnPain { get; private set; }
    public int SprayTicks { get; private set; }

    /// <summary>
    /// The pooled droplets, for a presenter that draws the stream. Exposed read-only: a
    /// presenter may look at where a droplet is and how far through its life it is, and may
    /// not launch, park, or move one — those are this component's, on the routed tick.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<SprayDropletBody> Droplets => _pool;

    /// <summary>
    /// Pooled droplets the reduced-particles rule currently lets draw. A computed counter
    /// rather than a drawn one, so it is a usable oracle in a headless run where nothing
    /// ever paints.
    /// </summary>
    public int DrawEnabledDropletCount
    {
        get
        {
            int enabled = 0;
            for (int index = 0; index < _pool.Length; index++)
            {
                if (_pool[index].DrawEnabled)
                    enabled++;
            }

            return enabled;
        }
    }

    public int ActiveDropletCount
    {
        get
        {
            int live = 0;
            for (int index = 0; index < _pool.Length; index++)
            {
                if (_pool[index].State == SprayDropletState.Live)
                    live++;
            }

            return live;
        }
    }

    /// <summary>Fired on the tick a fresh burn starts, at the ignition point.</summary>
    public event Action<Vector2>? Ignited;

    /// <summary>Fired on the tick the stream starts, and again when it stops.</summary>
    public event Action<bool>? SprayingChanged;

    /// <summary>Fired for each accepted burn pain event, with the pain it scored.</summary>
    public event Action<float>? BurnEventApplied;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0 ||
            !GodotObject.IsInstanceValid(Pipeline) ||
            !GodotObject.IsInstanceValid(Boundaries))
        {
            throw new InvalidOperationException(
                "FireSprayerComponent requires a valid sprayer profile, the interaction pipeline, and room boundaries.");
        }

        // The whole pool is built once here, on a composition tick rather than on a
        // spraying tick, so nothing is allocated on the 120 Hz path.
        _pool = new SprayDropletBody[Math.Max(1, Profile.PoolCapacity)];
        for (int index = 0; index < _pool.Length; index++)
        {
            var droplet = new SprayDropletBody { Name = $"fire-sprayer-droplet-{index + 1}" };
            droplet.Configure(Profile);
            AddChild(droplet);
            _pool[index] = droplet;
        }

        IsInitialized = true;
    }

    /// <summary>Move the cursor the sprayer aims from (sandbox coordinates).</summary>
    public void MoveCursor(Vector2 worldPoint)
    {
        _cursor = ClampToPlayableBounds(worldPoint);
        _hasCursor = true;
    }

    /// <summary>
    /// Invalidates the cursor when the real pointer leaves the play window. The tool stays
    /// selected, but a sprayer nobody is holding must not keep aiming or emitting.
    /// </summary>
    public void ClearCursor()
    {
        _hasCursor = false;
        _primaryHeld = false;
        _pendingPrimaryLatch = false;
        _pendingWheelSteps = 0;
        QueueRedraw();
    }

    /// <summary>
    /// Primary button state. There is no press edge: the stream runs while this is true and
    /// stops on the tick it goes false, which is the tool's whole cancel path.
    /// </summary>
    public void SetPrimaryHeld(bool held)
    {
        _primaryHeld = held;
        if (held)
            _pendingPrimaryLatch = true;
    }

    /// <summary>
    /// Records a press whose release already happened, so a click that began and ended
    /// between two routed ticks still puts one droplet in the air.
    /// </summary>
    public void LatchPrimary() => _pendingPrimaryLatch = true;

    /// <summary>Mouse-wheel aim offset: positive steps aim upward.</summary>
    public void ApplyWheel(int steps)
    {
        if (steps == 0)
            return;

        _pendingWheelSteps += steps;
    }

    /// <summary>
    /// Puts a burn out immediately. The entry point for the centralized hard reposition
    /// (DECISIONS "Fail-safe cleanup", which already promises Burning is cleared) and, when
    /// it ships, the Repair Kit (FR-010.10).
    /// </summary>
    public void ClearBurning()
    {
        _burn = BurningStatus.Clear(_burn);
        // Scorch goes out with the fire on this entry point and no other: the hard
        // reposition's contract is that it clears Burning "and other temporary statuses",
        // and a soot mark that survived a fail-safe reposition would be exactly the kind of
        // leftover that contract exists to prevent.
        for (int index = 0; index < _scorch.Length; index++)
            _scorch[index] = ScorchState.Clear(_scorch[index]);
        QueueRedraw();
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick()
    {
        RequireInitialized();

        bool active = IsActive;
        if (!active && _aim.HasAim)
        {
            // Drawing or holstering resets the aim, for the same reason a gun's does: a
            // stale direction would send the first droplet somewhere never pointed at.
            _aim = CursorAimState.Initial;
            AimForward = Vector2.Zero;
            AimSmoothedSpeed = 0.0f;
            AimIsSteering = false;
            _previousCursor = _cursor;
            QueueRedraw();
        }

        // A stream already in the air belongs to the room: droplets keep flying, keep
        // igniting, and keep re-pooling whatever tool is currently selected.
        AdvanceDroplets();

        int wheelSteps = _pendingWheelSteps;
        bool primary = _primaryHeld || _pendingPrimaryLatch;
        _pendingWheelSteps = 0;
        _pendingPrimaryLatch = false;

        if (active)
        {
            Vector2 motion = _cursor - _previousCursor;
            CursorAimResult aim = CursorAim.Tick(new CursorAimInput(
                _aim,
                new NumericsVector2(motion.X, motion.Y),
                wheelSteps,
                Profile.ToAimConstants()));
            _aim = aim.State;
            AimForward = aim.IsValid ? new Vector2(aim.Forward.X, aim.Forward.Y) : Vector2.Zero;
            AimSmoothedSpeed = aim.SmoothedSpeed;
            AimIsSteering = aim.IsSteering;
        }

        bool spraying = active && primary && AimForward != Vector2.Zero;
        if (spraying != IsSpraying)
        {
            IsSpraying = spraying;
            // The stream's cadence restarts with the stream, so a tap always puts a droplet
            // out on the tick the player pressed rather than whenever the phase happens to
            // come round.
            _emitCountdown = 0;
            SprayingChanged?.Invoke(spraying);
        }

        if (spraying)
        {
            SprayTicks++;
            if (_emitCountdown <= 0)
            {
                Emit();
                _emitCountdown = Math.Max(1, Profile.EmitIntervalTicks);
            }

            _emitCountdown--;
        }

        AdvanceBurning();
        AdvanceScorch();
        UpdateFleeDirection();
        _previousCursor = _cursor;
        QueueRedraw();
    }

    /// <summary>Immediate recovery cleanup on the authoritative physics clock.</summary>
    public void CancelImmediately()
    {
        RequireInitialized();
        ClearBurning();
        _primaryHeld = false;
        _pendingPrimaryLatch = false;
        if (IsSpraying)
        {
            IsSpraying = false;
            SprayingChanged?.Invoke(false);
        }

        for (int index = 0; index < _pool.Length; index++)
        {
            if (_pool[index].State != SprayDropletState.Pooled)
                _pool[index].Park();
        }
    }

    /// <summary>The number of parts carrying a visible mark, for readouts.</summary>
    public int ScorchedPartCount
    {
        get
        {
            int marked = 0;
            for (int index = 0; index < _scorch.Length; index++)
            {
                if (_scorch[index].IsMarked)
                    marked++;
            }

            return marked;
        }
    }

    private void Emit()
    {
        SprayDropletBody? droplet = TryTake();
        if (droplet is null)
        {
            // The pool is the bound; refusing is the honest outcome, and it is counted so a
            // too-small pool shows up as telemetry rather than as a stream that thins out.
            PoolExhaustedCount++;
            return;
        }

        Vector2 forward = AimForward.Rotated(Mathf.DegToRad(Profile.FanDegrees(_dropletIndex)));
        Vector2 nozzle = ClampInsideRoom(
            _cursor + (AimForward * Profile.MuzzleOffsetPx), Profile.DropletRadius);
        droplet.Launch(nozzle, forward * Profile.SprayDropletSpeed);
        _dropletIndex++;
        DropletsLaunched++;
    }

    private SprayDropletBody? TryTake()
    {
        for (int index = 0; index < _pool.Length; index++)
        {
            if (_pool[index].State == SprayDropletState.Pooled)
                return _pool[index];
        }

        return null;
    }

    private void AdvanceDroplets()
    {
        for (int index = 0; index < _pool.Length; index++)
        {
            SprayDropletBody droplet = _pool[index];
            if (droplet.State == SprayDropletState.Pooled)
                continue;

            bool finished = droplet.Advance(
                Profile.DropletLifetimeTicks, Profile.DropletMaxTravelPx);
            if (droplet.IgnitedPart is { } part)
            {
                ApplyFireContact(part, droplet.IgnitionPoint);
            }

            if (finished)
                droplet.Park();
        }
    }

    /// <summary>
    /// Fire touched the buddy. This is the whole of a droplet's effect: refresh the burn and
    /// remember where the fire is. It never becomes an impact.
    /// </summary>
    private void ApplyFireContact(BuddyPartId part, Vector2 worldPoint)
    {
        BurningApplyResult applied = BurningStatus.Apply(_burn, Profile.ToBurningConstants());
        if (!applied.IsValid)
            return;

        _burn = applied.Phase;
        _ignitionPart = part;
        if (applied.Ignited)
        {
            // One burn, one interaction id, re-minted whenever a lapsed burn reignites, so
            // rolling-pain bookkeeping sees a continuous burn as one source.
            _burnInteractionId = InteractionIds.Next();
            IgnitionCount++;
            Ignited?.Invoke(worldPoint);
        }
    }

    private void AdvanceBurning()
    {
        if (!_burn.IsBurning)
            return;

        BurningTickResult result = BurningStatus.Tick(_burn, Profile.ToBurningConstants());
        _burn = result.Phase;
        if (!result.PainEventDue)
            return;

        PuppetPartBody? part = FindPart(_ignitionPart);
        if (part is null)
            return;

        // Straight down the sanctioned contact-free entry: equivalent impulse in, shared
        // curve, knockout window, payout, harmful memory, and the shared mood rule out.
        // Burning deliberately survives knockout — a KO'd buddy lies there and burns.
        float pain = Pipeline.ApplyBlastImpulse(
            _burnInteractionId,
            ContentIds.ToolFireSprayer,
            (BuddyPart)(int)_ignitionPart,
            Profile.BurnEquivalentImpulse,
            part.GlobalPosition);
        BurnPainEventCount++;
        LastBurnPain = pain;
        TotalBurnPain += pain;
        BurnEventApplied?.Invoke(pain);
    }

    /// <summary>
    /// Advances every part's scorch mark. Only the part the fire is actually on counts as
    /// burning, so a stream that moves from a leg to the head leaves two marks at different
    /// strengths rather than darkening the whole buddy evenly.
    /// </summary>
    private void AdvanceScorch()
    {
        ScorchConstants constants = Profile.ToScorchConstants();
        for (int index = 0; index < _scorch.Length; index++)
        {
            bool burning = _burn.IsBurning && index == (int)_ignitionPart;
            if (!burning && !_scorch[index].IsMarked)
                continue;

            _scorch[index] = ScorchState.Tick(_scorch[index], burning, constants).Phase;
        }
    }

    private PuppetPartBody? FindPart(BuddyPartId partId)
    {
        System.Collections.Generic.IReadOnlyList<PuppetPartBody> parts = Pipeline.Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            if (parts[index].PartId == partId)
                return parts[index];
        }

        return null;
    }

    /// <summary>
    /// Where a burning buddy runs. Away from the player's cursor while the stream is live —
    /// the fire is coming from there — and otherwise away from the nearer wall, so panic in
    /// a corner still has somewhere to go.
    /// </summary>
    private void UpdateFleeDirection()
    {
        if (!_burn.IsBurning)
            return;

        float torsoX = Pipeline.Buddy.Rig.Torso.GlobalPosition.X;
        if (IsSpraying)
        {
            float away = torsoX - _cursor.X;
            HazardFleeDirection = Mathf.IsZeroApprox(away) ? HazardFleeDirection : Mathf.Sign(away);
            return;
        }

        Rect2 bounds = Boundaries.InnerBounds;
        if (!bounds.HasArea())
            return;

        float fromLeft = torsoX - bounds.Position.X;
        float fromRight = bounds.End.X - torsoX;
        HazardFleeDirection = fromLeft <= fromRight ? 1.0f : -1.0f;
    }

    /// <summary>
    /// Whether the legacy 2D presentation draws the sprayer. The 3D presentation has its own
    /// silhouette; drawing both would put two nozzles on one cursor.
    /// </summary>
    public bool DrawsLegacySprayer { get; private set; } = true;

    public void SetLegacyVisualEnabled(bool enabled)
    {
        DrawsLegacySprayer = enabled;
        QueueRedraw();
    }

    /// <summary>
    /// Applies the accessibility effect settings (FR-017.3). Presentation only: droplets
    /// thin out visually under reduced particles while the physical stream is untouched.
    /// </summary>
    public void ApplyEffectsSettings(Domain.Presentation.EffectsSettings settings)
    {
        int stride = settings.ParticleStride;
        for (int index = 0; index < _pool.Length; index++)
            _pool[index].DrawEnabled = index % stride == 0;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!IsActive || AimForward == Vector2.Zero || !DrawsLegacySprayer)
            return;

        // The legacy 2D view of the sprayer: a flat silhouette at the same dimensions the 3D
        // one is built to. Presentation only — it has no collider and nothing here touches
        // gameplay.
        Vector2 origin = ToLocal(_cursor);
        Vector2 forward = AimForward;
        Vector2 down = new Vector2(-forward.Y, forward.X) * (forward.X < 0.0f ? -1.0f : 1.0f);
        float length = Profile.VisualLengthPx;
        float tip = Profile.VisualMuzzleTipPx;

        Vector2 At(float along, float across) =>
            origin + (forward * (length * along)) + (down * (length * across));

        Vector2 Tip(float fromTip, float across) =>
            origin + (forward * (tip + (length * fromTip))) + (down * (length * across));

        // Tank, barrel, grip, and a flared nozzle ring: enough to read apart from a pistol
        // at a glance, which is the whole job of the silhouette.
        DrawColoredPolygon(
            new[] { At(0.02f, -0.26f), At(0.34f, -0.26f), At(0.34f, 0.14f), At(0.02f, 0.14f) },
            Profile.BodyColor);
        DrawColoredPolygon(
            new[] { At(0.30f, -0.09f), Tip(-0.02f, -0.09f), Tip(-0.02f, 0.09f), At(0.30f, 0.09f) },
            Profile.BodyColor);
        DrawColoredPolygon(
            new[] { At(0.08f, 0.12f), At(0.28f, 0.12f), At(0.24f, 0.46f), At(0.06f, 0.46f) },
            Profile.AccentColor);
        DrawColoredPolygon(
            new[] { Tip(-0.06f, -0.17f), Tip(0.0f, -0.17f), Tip(0.0f, 0.17f), Tip(-0.06f, 0.17f) },
            Profile.AccentColor);
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
            throw new InvalidOperationException("FireSprayerComponent used before initialization.");
    }
}
