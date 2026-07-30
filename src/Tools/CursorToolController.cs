using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Sandbox;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Tools;

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

    public event Action<CursorToolBody>? BodySpawned;
    public event Action<CursorToolBody>? BodyDespawned;

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
            if (!GodotObject.IsInstanceValid(profile) || profile!.Validate().Count > 0)
            {
                throw new InvalidOperationException(
                    $"CursorToolController requires valid tool profiles (entry {index} is not).");
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
        _swing.ChargeCompleted += OnChargeCompleted;

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
    public void SetChargeHeld(bool held) => _swing.SetChargeHeld(held);

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

    /// <summary>The impact context the live collider is carrying into the pain pipeline.</summary>
    public SwingImpactContext SwingContext => _swing.Context;

    /// <summary>True when the live tool authors grip/charge/swing handling.</summary>
    public bool IsSwingCapable => _swing.IsSwingCapable;

    /// <summary>True when the named tool would be gripped and swung rather than only dragged.</summary>
    public bool IsSwingCapableTool(ToolId tool) => ProfileFor(tool)?.IsSwingCapable == true;

    /// <summary>Fires once per charge when it reaches the cap — the tip glint edge.</summary>
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
        ChargedSwingDrive drive = _swing.Tick(body, _cursor, cursorVelocity, dt);
        body.SetSwingContext(drive.Context);
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
            Vector2 error = _cursor - body.GlobalPosition;
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

        _previousCursor = _cursor;
    }

    /// <summary>
    /// Holds an elongated tool square to the direction the player is swinging it,
    /// so it strikes with its barrel instead of tumbling. The target survives a
    /// pause in the swing, and the half-turn symmetry of a two-ended tool is folded
    /// out so it never spins around to present its other end.
    /// </summary>
    private void ApplyAlignment(CursorToolBody body, CursorToolProfile profile, Vector2 cursorVelocity)
    {
        if (profile.AlignStiffness <= 0.0f)
        {
            return;
        }

        (float angle, bool hasTarget) = AlignmentTorque.SwingAngleFor(
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
        float longAxisAngle = body.GlobalRotation + (Mathf.Pi * 0.5f);
        float alignError = AlignmentTorque.SymmetricError(_alignTarget, longAxisAngle);
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
        // Losing the collider abandons any grip, charge, or swing with it. The
        // worker keeps its epoch counter so a respawned tool cannot reuse a
        // swing identity the pain pipeline has already spent.
        _swing.Reset();
    }

    private void OnChargeCompleted()
    {
        if (!GodotObject.IsInstanceValid(_body) || _activeProfile?.Swing is not { } swing)
        {
            return;
        }

        _body!.StartChargeGlint(swing.GlintSeconds, swing.GlintSizePx);
    }

    public override void _ExitTree()
    {
        _swing.ChargeCompleted -= OnChargeCompleted;
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
        float insetY = insets.Y + profile.WallClearance;
        float minimumX = bounds.Position.X + insetX;
        float maximumX = bounds.End.X - insetX;
        float minimumY = bounds.Position.Y + insetY;
        float maximumY = bounds.End.Y - insetY;
        if (maximumX < minimumX || maximumY < minimumY)
            return bounds.GetCenter();

        return new Vector2(
            Mathf.Clamp(worldPoint.X, minimumX, maximumX),
            Mathf.Clamp(worldPoint.Y, minimumY, maximumY));
    }

    private static NumericsVector2 ToNumerics(Vector2 value) => new(value.X, value.Y);

    private static Vector2 ToGodot(NumericsVector2 value) => new(value.X, value.Y);
}
