using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Sandbox;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Tools;

public readonly record struct LooseObjectSwingHit(
    string ContentId,
    SwingImpactContext Context);

/// <summary>
/// Owns the lifecycle of every cursor-tethered physical tool (RAGDOLL §9.1).
/// While one of its authored tools is selected, that tool's collider exists and
/// is pulled toward the cursor by the same bounded damped-elastic tether as the
/// M1 grab (<see cref="GrabTether"/>), anchored at the body's center so the pull
/// is torque-free. An elongated tool additionally holds square to its own swing
/// through the bounded <see cref="AlignmentTorque"/> servo — the only rotation
/// this controller authors. Real swing speed and measured contact impulse drive
/// pain through the shared pipeline; this controller applies force and torque
/// only. Selecting any other tool despawns the collider.
///
/// Tools are authored <see cref="CursorToolProfile"/> resources rather than
/// separate controllers, so a new cursor-tethered tool is data plus content ID,
/// not new input code (the same shape the launcher took in M5 Task 3).
/// </summary>
[GlobalClass]
public partial class CursorToolController : Node2D
{
    [Export] public Godot.Collections.Array<CursorToolProfile> Profiles { get; set; } = new();
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;

    private CursorToolBody? _body;
    private CursorToolProfile? _activeProfile;
    private readonly ChargedSwingComponent _swing = new() { Name = "ChargedSwing" };
    private Vector2 _cursor;
    private Vector2 _previousCursor;
    private bool _hasCursor;
    private float _armingTravel;
    private float _alignTarget;
    private bool _hasAlignTarget;

    // --- Wind-up-and-lash-out (PunchToolProfile) ---
    // Reuses the pistol's steering feel for the facing rather than keeping a second
    // cursor-follow approximation: it is the direction the tool is drawn pointing, so the
    // punch goes where the player can see the fist is aimed.
    private static readonly CursorAimConstants PunchAimConstants = new(
        SmoothingHalfLifeTicks: 14.0f,
        MinimumAimSpeed: 0.35f,
        MaxTurnDegreesPerTick: 6.0f,
        DegreesPerWheelStep: 5.0f,
        MaximumOffsetDegrees: 60.0f);

    /// <summary>Wind-up rattle rate: a little under half a cycle a tick, so it reads as a shake.</summary>
    private const float ShakeRadiansPerTick = 1.9f;

    private CursorAimState _punchAimState = CursorAimState.Initial;
    private float _punchFacingAngle;
    private bool _hasPunchFacing;
    private bool _chargeHeld;
    private int _punchChargeTicks;
    private int _punchLungeTicks;
    private float _punchReleasedCharge;

    public event Action<CursorToolBody>? BodySpawned;
    public event Action<CursorToolBody>? BodyDespawned;
    public event Action<LooseObjectSwingHit>? LooseObjectSwingHit;

    public bool IsInitialized { get; private set; }
    public bool IsActive => GodotObject.IsInstanceValid(_body);
    public CursorToolBody? Body => GodotObject.IsInstanceValid(_body) ? _body : null;
    public bool HasCursor => _hasCursor;
    public Vector2 Cursor => _cursor;

    /// <summary>The profile currently driving a live collider, if any.</summary>
    public CursorToolProfile? ActiveProfile => IsActive ? _activeProfile : null;

    /// <summary>
    /// The content ID a live collider attributes its impacts to, or <c>null</c>
    /// when no cursor tool is active. Presentation and reaction code keys on this
    /// instead of naming one tool, so every cursor tool gets the same treatment.
    /// </summary>
    public string? ActiveContentId => ActiveProfile?.ContentId;

    public void Initialize()
    {
        if (Profiles.Count == 0)
        {
            throw new InvalidOperationException(
                "CursorToolController requires at least one authored CursorToolProfile.");
        }

        for (int index = 0; index < Profiles.Count; index++)
        {
            CursorToolProfile? profile = Profiles[index];
            if (!GodotObject.IsInstanceValid(profile))
            {
                throw new InvalidOperationException(
                    $"CursorToolController requires valid tool profiles (entry {index} is not live).");
            }

            Godot.Collections.Array<string> profileErrors = profile!.Validate();
            if (profileErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"CursorToolController requires valid tool profiles (entry {index}, " +
                    $"'{profile.ContentId}'): {string.Join("; ", profileErrors)}");
            }

            // Two profiles claiming one tool would make the active collider depend on
            // array order, which is exactly the kind of silent data ambiguity the
            // catalogue spine exists to prevent.
            for (int other = 0; other < index; other++)
            {
                if (Profiles[other]!.ContentId == profile.ContentId)
                {
                    throw new InvalidOperationException(
                        $"CursorToolController has two profiles for '{profile.ContentId}'.");
                }
            }
        }

        if (!GodotObject.IsInstanceValid(Pipeline) || !GodotObject.IsInstanceValid(Boundaries))
        {
            throw new InvalidOperationException(
                "CursorToolController requires the interaction pipeline and room boundaries.");
        }

        // The swing worker is a code-owned child rather than an authored one: it
        // holds no tuning of its own (all of that lives on the tool profile), so
        // exporting it would only give every scene a slot to wire wrongly.
        AddChild(_swing);
        _swing.ChargeGlintRequested += OnChargeGlintRequested;

        IsInitialized = true;
    }

    /// <summary>
    /// Primary button while a swing-capable tool is selected: grip the tool by
    /// its handle and hold it upright. Input components translate hardware into
    /// this; scenarios call it directly, the same seam <see cref="MoveCursor"/>
    /// established.
    /// </summary>
    public void SetGrip(bool held) => _swing.SetGrip(held);

    /// <summary>Secondary button while gripped: charge, and on release, swing.</summary>
    public void SetChargeHeld(bool held)
    {
        _swing.SetChargeHeld(held);
        if (held == _chargeHeld)
            return;

        _chargeHeld = held;
        // The release edge is the punch: what was wound up is spent, and the lunge runs on
        // its own clock from here whatever the player does with the button.
        if (!held && _punchChargeTicks > 0 && _activeProfile?.Punch is PunchToolProfile punch)
        {
            _punchReleasedCharge = PunchCharge;
            _punchLungeTicks = punch.LungeTicks;
            PunchCount++;
        }

        if (!held)
            _punchChargeTicks = 0;
    }

    /// <summary>How far the wind-up has come, 0..1. Zero for a tool that cannot punch.</summary>
    public float PunchCharge
    {
        get
        {
            if (_activeProfile?.Punch is not PunchToolProfile punch)
                return 0.0f;
            return Mathf.Clamp(_punchChargeTicks / (float)Mathf.Max(1, punch.MaxChargeTicks), 0.0f, 1.0f);
        }
    }

    /// <summary>True while the tool is being wound back.</summary>
    public bool IsPunchCharging => _punchChargeTicks > 0 && _chargeHeld;

    /// <summary>True while a released punch is still reaching out.</summary>
    public bool IsPunchLunging => _punchLungeTicks > 0;

    /// <summary>Lifetime punches thrown, so a scenario can assert the release fired.</summary>
    public int PunchCount { get; private set; }

    /// <summary>
    /// Where the tool is pointing, and so which way a punch travels. Only meaningful for a
    /// punch-capable tool; the glove's own visual reads this rather than deriving its own, so
    /// the fist the player sees and the direction the punch takes are one value.
    /// </summary>
    public float ToolFacingAngle => _punchFacingAngle;

    public bool HasToolFacing => _hasPunchFacing;

    /// <summary>True when the selected tool winds back on secondary rather than swinging.</summary>
    public bool IsPunchCapableTool(ToolId tool) => ProfileFor(tool)?.IsPunchCapable == true;

    /// <summary>The grip/charge/swing state of the live tool.</summary>
    public ChargedSwingState SwingState => _swing.State;

    /// <summary>Normalized charge, <c>0..1</c>.</summary>
    public float SwingCharge => _swing.Charge;

    /// <summary>Routed ticks of charge accrued — the quantity the five-second cap is stated in.</summary>
    public int SwingChargeTicks => _swing.ChargeTicks;

    /// <summary>Which way the next swing will go: <c>+1</c> right, <c>-1</c> left.</summary>
    public int SwingDirectionSign => _swing.DirectionSign;

    /// <summary>Monotonic identity of the running swing; <c>0</c> before the first release.</summary>
    public int SwingEpoch => _swing.SwingEpoch;
    public float ReleasedSwingCharge => _swing.ReleasedCharge;
    public int SwingTicksInState => _swing.SwingTicksInState;
    public Vector2 LatchedSwingPivot => _swing.LatchedPivot;
    public SwingPlan CurrentSwingPlan => _swing.CurrentPlan;

    /// <summary>The impact context the live collider is carrying into the pain pipeline.</summary>
    public SwingImpactContext SwingContext => _swing.Context;

    /// <summary>True when the live tool authors grip/charge/swing handling.</summary>
    public bool IsSwingCapable => _swing.IsSwingCapable;

    /// <summary>True when the named tool would be gripped and swung rather than only dragged.</summary>
    public bool IsSwingCapableTool(ToolId tool) => ProfileFor(tool)?.IsSwingCapable == true;

    /// <summary>Whether the authored tool is wielded by the hilt with its point leading.</summary>
    public bool IsThrustCapableTool(ToolId tool) => ProfileFor(tool)?.IsThrustCapable == true;

    /// <summary>
    /// True while the live tool is a blade and the player is bracing it. The impalement
    /// component reads this rather than the raw button, so "wielding" means one thing.
    /// </summary>
    public bool IsWieldingPointFirst =>
        IsActive && _activeProfile is not null && PointsFirst(_activeProfile);

    /// <summary>Fires once on the routed tick that charging begins.</summary>
    public event Action? ChargeStarted
    {
        add => _swing.ChargeStarted += value;
        remove => _swing.ChargeStarted -= value;
    }

    /// <summary>Fires once per charge when it reaches the cap.</summary>
    public event Action? ChargeCompleted
    {
        add => _swing.ChargeCompleted += value;
        remove => _swing.ChargeCompleted -= value;
    }

    /// <summary>Fires once on the release that committed a swing: charge, then epoch.</summary>
    public event Action<float, int>? SwingReleased
    {
        add => _swing.SwingReleased += value;
        remove => _swing.SwingReleased -= value;
    }

    /// <summary>True when the named tool is one this controller gives a collider to.</summary>
    public bool DrivesTool(ToolId tool) => AttributesContent(ContentIds.ForTool(tool));

    /// <summary>
    /// True when the content ID belongs to one of this controller's authored tools.
    /// This is deliberately about identity, not liveness: an impact is attributed to
    /// the tool that landed it, and code reacting to that impact must not depend on
    /// a collider still existing when the reaction is delivered.
    /// </summary>
    public bool AttributesContent(string? contentId)
    {
        if (contentId is null)
        {
            return false;
        }

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

    /// <summary>Returns the authored swing tuning for an attributed content ID.</summary>
    public SwingToolProfile? SwingProfileForContent(string? contentId)
    {
        if (contentId is null)
        {
            return null;
        }

        for (int index = 0; index < Profiles.Count; index++)
        {
            CursorToolProfile? profile = Profiles[index];
            if (GodotObject.IsInstanceValid(profile) && profile!.ContentId == contentId)
            {
                return profile.Swing;
            }
        }

        return null;
    }

    /// <summary>Move the cursor anchor the active tool is tethered to (sandbox coordinates).</summary>
    public void MoveCursor(Vector2 worldPoint)
    {
        _cursor = ClampToPlayableBounds(worldPoint);
        _hasCursor = true;
    }

    /// <summary>
    /// Invalidates the cursor anchor when the real pointer leaves the play
    /// window. The selected tool is preserved, but its physical actor must not
    /// remain pinned to the last in-bounds corner.
    /// </summary>
    public void ClearCursor()
    {
        _hasCursor = false;
        _swing.ReleaseInput();
        if (IsActive)
            Despawn();
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick(double delta)
    {
        RequireInitialized();
        RoutePendingImpactEvents();
        CursorToolProfile? wanted = _hasCursor ? ProfileFor(Pipeline.SelectedTool) : null;
        // A tool swap is a despawn and a respawn, never a reconfigure: the collider's
        // shape, mass, and attribution identity all belong to one profile.
        if (IsActive && !ReferenceEquals(wanted, _activeProfile))
        {
            Despawn();
        }

        if (wanted is not null && !IsActive)
        {
            Spawn(wanted);
        }

        _swing.Configure(_activeProfile);

        if (!IsActive)
        {
            return;
        }

        CursorToolBody body = _body!;
        CursorToolProfile profile = _activeProfile!;
        float dt = (float)delta;
        if (!body.IsImpactArmed)
        {
            _armingTravel += _cursor.DistanceTo(_previousCursor);
            if (_armingTravel >= profile.MinimumArmingTravel)
                body.ArmImpacts();
        }

        Vector2 cursorVelocity = dt > 0.0f ? (_cursor - _previousCursor) / dt : Vector2.Zero;
        TickPunch(profile, _cursor - _previousCursor);
        ChargedSwingDrive drive = _swing.Tick(body, _cursor, cursorVelocity, dt);
        body.SetSwingContext(drive.Context);
        body.ContinuousCd = drive.State == ChargedSwingState.Swinging
            ? RigidBody2D.CcdMode.CastShape
            : RigidBody2D.CcdMode.Disabled;
        body.SetChargeVisual(
            drive.State == ChargedSwingState.Charging ? drive.Charge : 0.0f,
            profile.Swing);

        if (drive.DrivesTether)
        {
            // Godot takes the offset from the body origin in global orientation.
            // It coincides with the centre of mass for these shapes, but the
            // distinction would matter the moment anyone authored an offset one.
            body.ApplyForce(drive.Force, drive.ForceOffset);
        }
        else
        {
            // A wielded blade is held by its hilt, so that is the point the tether closes
            // to the cursor — not the tool's centre, which would leave the sword balanced
            // on the pointer like a see-saw. The force is still applied at the centre of
            // mass: rotation belongs to the alignment servo alone, and an offset force
            // would fight it for the angle.
            Vector2 held = HeldByHilt(profile)
                ? body.ToGlobal(profile.HandleLocalOffset)
                : body.GlobalPosition;
            Vector2 error = PunchAnchor(profile) - held;
            Vector2 relativeVelocity = body.LinearVelocity - cursorVelocity;
            GrabTetherResult result = GrabTether.Evaluate(new GrabTetherInput(
                ToNumerics(error),
                ToNumerics(relativeVelocity),
                profile.Stiffness,
                profile.Damping,
                profile.MaximumForce));
            body.ApplyForce(ToGodot(result.Force));
        }

        if (drive.OwnsRotation)
        {
            // The upright hold and the swing arc are directed poses; the swing
            // alignment servo would fight them, so it stands down entirely.
            if (drive.AppliesTorque)
            {
                body.ApplyTorque(drive.Torque);
            }
        }
        else
        {
            // Alignment deliberately consumes the raw cursor velocity, not the
            // rate-limited anchor's: the barrel should steer toward where the
            // player is swinging, not toward where the limiter has caught up to.
            ApplyAlignment(body, profile, cursorVelocity);
        }

        // After the forces, so the sweep walks the step the solver is about to integrate.
        SweepStrikes(body, profile);

        _previousCursor = _cursor;
    }

    /// <summary>
    /// Routes solver contacts before the root's hit-lag gate. Roots call this
    /// before deciding whether to advance gameplay; the normal controller tick
    /// calls it again as an idempotent fallback for isolated compositions.
    /// </summary>
    public void RoutePendingImpactEvents()
    {
        if (GodotObject.IsInstanceValid(_body) &&
            _body!.TryConsumeLooseObjectSwingHit(out SwingImpactContext context))
        {
            LooseObjectSwingHit?.Invoke(new LooseObjectSwingHit(_body.ContentId, context));
        }
    }

    /// <summary>
    /// Holds an elongated tool square to the direction the player is swinging it,
    /// so it strikes with its barrel instead of tumbling. The target survives a
    /// pause in the swing, and the half-turn symmetry of a two-ended tool is folded
    /// out so it never spins around to present its other end.
    /// </summary>
    /// <summary>
    /// Advances the wind-up and the facing it will be spent along. Nothing here touches the
    /// body: the anchor this produces is fed to the same tether every tool follows, so a
    /// punch is the tool being dragged somewhere else rather than a second way to move it.
    /// </summary>
    private void TickPunch(CursorToolProfile profile, Vector2 cursorMotion)
    {
        if (profile.Punch is not PunchToolProfile punch)
        {
            _punchChargeTicks = 0;
            _punchLungeTicks = 0;
            _hasPunchFacing = false;
            _punchAimState = CursorAimState.Initial;
            return;
        }

        CursorAimResult aim = CursorAim.Tick(new CursorAimInput(
            _punchAimState,
            new NumericsVector2(cursorMotion.X, cursorMotion.Y),
            WheelSteps: 0,
            PunchAimConstants));
        _punchAimState = aim.State;
        if (aim.IsValid)
        {
            _punchFacingAngle = Mathf.Atan2(aim.Forward.Y, aim.Forward.X);
            _hasPunchFacing = true;
        }

        if (_chargeHeld)
            _punchChargeTicks = Math.Min(_punchChargeTicks + 1, punch.MaxChargeTicks);
        else if (_punchLungeTicks > 0)
            _punchLungeTicks--;
    }

    /// <summary>
    /// Where the tether is told to hold the tool this tick: behind the cursor while winding
    /// up, past it while lashing out, and the cursor itself the rest of the time.
    /// </summary>
    private Vector2 PunchAnchor(CursorToolProfile profile)
    {
        if (profile.Punch is not PunchToolProfile punch || !_hasPunchFacing)
            return _cursor;

        var facing = Vector2.FromAngle(_punchFacingAngle);
        if (_punchLungeTicks > 0)
        {
            // One half sine across the window: out and back, with no second timer to keep in
            // step with this one.
            float progress = 1.0f - (_punchLungeTicks / (float)Mathf.Max(1, punch.LungeTicks));
            float reach = Mathf.Sin(progress * Mathf.Pi) * punch.LungePx * _punchReleasedCharge;
            return _cursor + (facing * reach);
        }

        if (_punchChargeTicks > 0)
        {
            // A held wind-up rattles harder the longer it is held (owner instruction
            // 2026-08-23). It is a tick-driven oscillation, not noise, so the same hold always
            // looks the same and nothing needs a random source.
            Vector2 shake = facing.Orthogonal() *
                (Mathf.Sin(_punchChargeTicks * ShakeRadiansPerTick) * punch.ChargeShakePx * PunchCharge);
            return _cursor - (facing * (punch.PullBackPx * PunchCharge)) + shake;
        }

        return _cursor;
    }

    /// <summary>
    /// A blade hangs from its hilt whether or not it is being braced — that is simply where
    /// it is held. Keying this on the button too would snap the sword from hilt-held to
    /// centre-held the instant it was released.
    /// </summary>
    private static bool HeldByHilt(CursorToolProfile profile) => profile.IsThrustCapable;

    /// <summary>
    /// Whether the point is being driven this tick: an authored blade, with the player
    /// holding secondary. Let go and the blade stops steering and simply trails from the
    /// hilt, which is what "hold right mouse button" means (owner instruction 2026-08-25).
    /// </summary>
    private bool PointsFirst(CursorToolProfile profile) =>
        profile.IsThrustCapable && _chargeHeld;

    private void ApplyAlignment(CursorToolBody body, CursorToolProfile profile, Vector2 cursorVelocity)
    {
        if (profile.AlignStiffness <= 0.0f)
        {
            return;
        }

        bool wielded = PointsFirst(profile);
        if (profile.IsThrustCapable && !wielded)
        {
            // An unbraced blade holds no angle of its own. Letting the previous target keep
            // steering would make releasing the button do nothing visible.
            _hasAlignTarget = false;
            return;
        }

        (float angle, bool hasTarget) = wielded
            ? AlignmentTorque.ThrustAngleFor(
                cursorVelocity.X, cursorVelocity.Y, profile.MinimumAlignSpeed)
            : AlignmentTorque.SwingAngleFor(
                cursorVelocity.X, cursorVelocity.Y, profile.MinimumAlignSpeed);
        if (hasTarget)
        {
            _alignTarget = angle;
            _hasAlignTarget = true;
        }

        if (!_hasAlignTarget)
        {
            return;
        }

        // CapsuleShape2D runs along its own local Y, so the barrel points a quarter
        // turn from the body's rotation. The servo steers the barrel, not the frame.
        // A wielded blade steers its far end instead — local -Y, the end opposite the
        // authored hilt — and takes the plain wrapped error, because a sword pointing
        // backwards is wrong in a way a bat held upside down is not.
        float longAxisAngle = body.GlobalRotation + (Mathf.Pi * 0.5f);
        float alignError = wielded
            ? Domain.Physics.HangFrame.WrapAngle(_alignTarget - (longAxisAngle - Mathf.Pi))
            : AlignmentTorque.SymmetricError(_alignTarget, longAxisAngle);
        AlignmentTorqueResult torque = AlignmentTorque.Evaluate(new AlignmentTorqueInput(
            alignError,
            body.AngularVelocity,
            profile.AlignStiffness,
            profile.AlignDamping,
            profile.MaximumAlignTorque));
        if (torque.IsValid)
        {
            body.ApplyTorque(torque.Torque);
        }
    }

    private CursorToolProfile? ProfileFor(ToolId tool)
    {
        string contentId = ContentIds.ForTool(tool);
        for (int index = 0; index < Profiles.Count; index++)
        {
            CursorToolProfile? profile = Profiles[index];
            if (GodotObject.IsInstanceValid(profile) && profile!.ContentId == contentId)
            {
                return profile;
            }
        }

        return null;
    }

    private void Spawn(CursorToolProfile profile)
    {
        var body = new CursorToolBody { Name = NodeNameFor(profile) };
        body.Configure(profile);
        AddChild(body);
        body.GlobalPosition = _cursor;
        body.LinearVelocity = Vector2.Zero;
        body.AngularVelocity = 0.0f;
        _previousCursor = _cursor;
        _armingTravel = 0.0f;
        _hasAlignTarget = false;
        _alignTarget = 0.0f;
        _body = body;
        _activeProfile = profile;
        ResetStrikeSweep();
        BodySpawned?.Invoke(body);
    }

    private static string NodeNameFor(CursorToolProfile profile) =>
        profile.ContentId.Replace("tool.", string.Empty).Replace('_', '-');

    private void Despawn()
    {
        if (GodotObject.IsInstanceValid(_body))
        {
            CursorToolBody body = _body!;
            BodyDespawned?.Invoke(body);
            body.QueueFree();
        }

        _body = null;
        _activeProfile = null;
        _armingTravel = 0.0f;
        _hasAlignTarget = false;
        ResetStrikeSweep();
        // Losing the collider abandons any grip, charge, or swing with it. The
        // worker keeps its epoch counter so a respawned tool cannot reuse a
        // swing identity the pain pipeline has already spent.
        _swing.Reset();
    }

    private void OnChargeGlintRequested(ChargeGlintStage stage)
    {
        if (!GodotObject.IsInstanceValid(_body) || _activeProfile?.Swing is not { } swing)
        {
            return;
        }

        float sizePx = stage switch
        {
            ChargeGlintStage.OneSecond => swing.OneSecondGlintSizePx,
            ChargeGlintStage.ThreeSeconds => swing.ThreeSecondGlintSizePx,
            _ => swing.FiveSecondGlintSizePx,
        };
        _body!.StartChargeGlint(swing.GlintSeconds, sizePx);
    }

    public override void _ExitTree()
    {
        _swing.ChargeGlintRequested -= OnChargeGlintRequested;
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("CursorToolController used before initialization.");
        }
    }

    private Vector2 ClampToPlayableBounds(Vector2 worldPoint)
    {
        Rect2 bounds = Boundaries.InnerBounds;
        if (!bounds.HasArea())
            return worldPoint;

        // The inset uses the tool's widest reach in any orientation, because the
        // alignment servo is free to point an elongated tool at the wall.
        CursorToolProfile? profile = _activeProfile;
        if (profile is null && GodotObject.IsInstanceValid(Pipeline) && Pipeline.IsInitialized)
        {
            profile = ProfileFor(Pipeline.SelectedTool);
        }

        if (profile is null)
            return worldPoint;

        // Following anchors the tool's centre, so half its length reaches the
        // wall. Gripping anchors its handle, so the whole barrel does — and the
        // inset is measured over the angles the planned arc really visits rather
        // than assuming a full circle, which would needlessly forbid aiming
        // along a wall.
        float extent = Mathf.Max(profile.Radius, profile.Length * 0.5f);
        Vector2 insets = _swing.State is ChargedSwingState.Gripped or ChargedSwingState.Charging
            ? _swing.PivotInset
            : new Vector2(extent, extent);
        float insetX = insets.X + profile.WallClearance;
        float topInsetY = insets.Y + profile.WallClearance;
        // The handle may be taken all the way down to the floor while gripped
        // or charging. The bat itself remains a physical capsule and the room
        // collision decides how far it can follow; judging that obstruction is
        // now player skill instead of an invisible cursor-height restriction.
        float bottomInsetY =
            _swing.State is ChargedSwingState.Gripped or ChargedSwingState.Charging
                ? profile.WallClearance
                : insets.Y + profile.WallClearance;
        float minimumX = bounds.Position.X + insetX;
        float maximumX = bounds.End.X - insetX;
        float minimumY = bounds.Position.Y + topInsetY;
        float maximumY = bounds.End.Y - bottomInsetY;
        if (maximumX < minimumX || maximumY < minimumY)
            return bounds.GetCenter();

        return new Vector2(
            Mathf.Clamp(worldPoint.X, minimumX, maximumX),
            Mathf.Clamp(worldPoint.Y, minimumY, maximumY));
    }

    private static NumericsVector2 ToNumerics(Vector2 value) => new(value.X, value.Y);

    private static Vector2 ToGodot(NumericsVector2 value) => new(value.X, value.Y);
}
