using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Read-only 3D presentation of the authoritative six-body 2D puppet. The owning scene
/// root initializes it and captures one pre-solver snapshot per engine physics tick.
/// </summary>
[GlobalClass]
public partial class BuddyVisualPresenter : Node3D
{
    private readonly BuddyVisualTransform[] _previous =
        new BuddyVisualTransform[PuppetRigProfile.RequiredPartCount];
    private readonly BuddyVisualTransform[] _current =
        new BuddyVisualTransform[PuppetRigProfile.RequiredPartCount];
    private readonly BuddyVisualTransform[] _rendered =
        new BuddyVisualTransform[PuppetRigProfile.RequiredPartCount];
    private readonly Node3D[] _sockets = new Node3D[PuppetRigProfile.RequiredPartCount];
    private readonly MeshInstance3D[] _partMeshes =
        new MeshInstance3D[PuppetRigProfile.RequiredPartCount];
    private readonly MeshInstance3D[] _partOutlines =
        new MeshInstance3D[PuppetRigProfile.RequiredPartCount];
    private readonly PartVisualDefinition[] _partDefinitions =
        new PartVisualDefinition[PuppetRigProfile.RequiredPartCount];
    private readonly float[] _meshRadii = new float[PuppetRigProfile.RequiredPartCount];
    private readonly float[] _velocityAngles = new float[PuppetRigProfile.RequiredPartCount];

    private MeshInstance3D[] _connectorMeshes = Array.Empty<MeshInstance3D>();
    private ConnectorVisualDefinition[] _connectorDefinitions =
        Array.Empty<ConnectorVisualDefinition>();
    private float[] _connectorAngles = Array.Empty<float>();
    private float[] _connectorAuthoringLengths = Array.Empty<float>();
    private IBuddyVisualTransformSource? _transformSource;
    private BuddyLookMaterialLibrary _materials = null!;
    private string _displayedFace = string.Empty;
    private bool _subscribedToRecovery;

    // Applied BodyYaw this frame = the development/scenario drive plus the facing
    // controller's eased three-quarter yaw scaled by the performance blend weight (so a
    // Tracking cut snaps the displayed yaw to zero without losing the committed side).
    // Yaw is applied to the resolved pose *before* the global camera-axis Z lane, so
    // changing a part's DepthOffset only changes projected depth, never screen-X
    // (M3_5_MATERIALS_AND_LOOK_PLAN.md transform contract).
    private float _yawRadians;
    private float _developmentYawRadians;

    // M3.6 Task 4: the head look-at angles applied on top of the body yaw, scaled by the
    // resolved gaze weight (normal performance, plus the explicit defend exception).
    // Head socket only — rotation, never position — so the gaze is physics-free by
    // construction and composes with any activity clip.
    private float _headLookYawRadians;
    private float _headLookPitchRadians;
    private float _activityHeadYawRadians;
    private PerformanceBlend? _defendGazeBlend;

    // M3.6 Task 1: per-part authored offsets (dev/scenario-driven until the activity
    // animator lands) and the blend weight the pose pipeline resolved this frame. The
    // final pose is tracked-body pose plus weight x clamped offset, applied before yaw
    // so offsets rotate with the body; connectors keep following the raw part poses.
    private readonly Vector3[] _developmentOffsets = new Vector3[PuppetRigProfile.RequiredPartCount];
    private float _performanceWeight;

    // Set while the owning root is holding the simulation (the laboratory pause). Tracking
    // still renders every frame — a single step must show the new body pose — but the
    // performance layer's SECONDS clock stops, so blends, eased turns, activity clips, and
    // the gaze freeze with the buddy instead of animating behind a frozen ragdoll.
    private bool _presentationHeld;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public BuddyVisualProfile Profile { get; set; } = null!;
    /// <summary>Optional M3.6 pose pipeline; when absent the presenter is pure M3.5 tracking.</summary>
    [Export] public BuddyPosePipeline? PosePipeline { get; set; }
    /// <summary>Optional M3.6 facing controller; when absent BodyYaw stays identity.</summary>
    [Export] public FacingController? Facing { get; set; }
    /// <summary>Optional M3.6 activity animator; when absent authored offsets are zero.</summary>
    [Export] public ActivityAnimator? Activities { get; set; }
    /// <summary>Optional M3.6 head look-at; when absent the head keeps its physics rotation.</summary>
    [Export] public HeadLookAtComponent? HeadLookAt { get; set; }
    /// <summary>Optional M3.6 face compositor. When wired the head carries the composed
    /// face plate (Task 5) and the M3.5 <see cref="Label3D"/> parity glyph is retired;
    /// when absent the glyph remains (uncomposed hosts, legacy tests).</summary>
    [Export] public FaceCompositor? Face { get; set; }
    /// <summary>
    /// Owner decision "Hit-Lag Shake Gets Its Own Offset Lane": unlike authored
    /// performance offsets, this one must remain visible while pose mode is
    /// Tracking. Only the hit-lag victim shake is allowed through this lane.
    /// </summary>
    [Export] public ImpactVisualOffsetComponent? ImpactVisualOffset { get; set; }

    public bool IsInitialized { get; private set; }
    public Node3D BodyYaw { get; private set; } = null!;
    public Label3D FaceLabel { get; private set; } = null!;

    /// <summary>The composed face plate (null when the Label3D fallback is active).</summary>
    public MeshInstance3D? FacePlate { get; private set; }

    private StandardMaterial3D? _facePlateMaterial;
    public int PartVisualCount => IsInitialized ? _partMeshes.Length : 0;
    public int ConnectorVisualCount => IsInitialized ? _connectorMeshes.Length : 0;

    /// <summary>
    /// Builds the stable socket hierarchy once. Supplying a source is the preview/posed
    /// presentation seam; normal gameplay uses the live authoritative rig wrapper.
    /// </summary>
    public void Initialize(IBuddyVisualTransformSource? transformSource = null)
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Profile))
        {
            throw new InvalidOperationException(
                "BuddyVisualPresenter requires an injected visual profile.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid buddy visual profile: {string.Join("; ", errors)}");
        }

        if (transformSource is null)
        {
            if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized)
            {
                throw new InvalidOperationException(
                    "The live BuddyVisualPresenter source requires an initialized buddy.");
            }

            _transformSource = new LiveBuddyVisualTransformSource(Buddy.Rig);
        }
        else
        {
            _transformSource = transformSource;
        }
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        // Cache the soft-toon lit materials and the shared ink outline material once. The
        // render path only reads these references; it never builds or mutates a material.
        _materials = new BuddyLookMaterialLibrary(Profile.Look);

        BodyYaw = new Node3D
        {
            Name = "BodyYaw",
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        AddChild(BodyYaw);

        BuildParts();
        BuildConnectors();
        SnapSnapshots();
        IsInitialized = true;
        TrySubscribeToRecovery();
        UpdateVisuals(0.0, 1.0f);
    }

    /// <summary>
    /// Captures the end of the previous solver step. Scene roots call this unconditionally
    /// before their pause/routing gate; this presenter deliberately owns no physics callback.
    /// </summary>
    public void CaptureTickSnapshot()
    {
        if (!IsInitialized || _transformSource is null)
        {
            return;
        }

        ReadSource(_previous);
    }

    public Node3D GetPartSocket(BuddyPartId partId)
    {
        int index = (int)partId;
        if (!IsInitialized || index < 0 || index >= _sockets.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(partId), partId, "Unknown visual part socket.");
        }

        return _sockets[index];
    }

    public Node3D GetConnectorVisual(int index)
    {
        if (!IsInitialized || index < 0 || index >= _connectorMeshes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Unknown connector visual.");
        }

        return _connectorMeshes[index];
    }

    public MeshInstance3D GetPartMesh(BuddyPartId partId)
    {
        int index = (int)partId;
        if (!IsInitialized || index < 0 || index >= _partMeshes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(partId), partId, "Unknown part mesh.");
        }

        return _partMeshes[index];
    }

    public MeshInstance3D GetPartOutline(BuddyPartId partId)
    {
        int index = (int)partId;
        if (!IsInitialized || index < 0 || index >= _partOutlines.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(partId), partId, "Unknown part outline.");
        }

        return _partOutlines[index];
    }

    /// <summary>The shared unshaded ink material every outline shell uses.</summary>
    public StandardMaterial3D OutlineMaterial => _materials.OutlineMaterial;

    /// <summary>
    /// Darkens one part toward <paramref name="scorchColor"/> by <paramref name="amount"/>
    /// (owner feedback 2026-08-01). This is the per-part mutation the material library was
    /// built for — every mesh already owns its own lit material instance precisely so a tint
    /// can never bleed onto an unrelated part through a shared one — so it is not a parallel
    /// visual channel, and the outline shell is deliberately left alone: the ink line is the
    /// buddy's silhouette, not its skin.
    ///
    /// <para>The authored <see cref="PartVisualDefinition.Color"/> stays the source of truth
    /// and is re-read every call, so the tint is absolute rather than cumulative and a mark
    /// that fades really does land back on the exact original albedo.</para>
    /// </summary>
    public void SetPartScorch(BuddyPartId partId, float amount, Color scorchColor)
    {
        int index = (int)partId;
        if (!IsInitialized || index < 0 || index >= _partMeshes.Length)
            return;

        if (_partMeshes[index].MaterialOverride is not StandardMaterial3D material)
            return;

        Color authored = _partDefinitions[index].Color;
        float clamped = Mathf.Clamp(amount, 0.0f, 1.0f);
        Color wanted = clamped <= 0.0f ? authored : authored.Lerp(scorchColor, clamped);
        if (material.AlbedoColor != wanted)
            material.AlbedoColor = wanted;
    }

    /// <summary>Darkens connectors only from their scorched non-torso endpoint.</summary>
    public void SetEndpointConnectorScorch(BuddyPartId endpoint, float amount, Color scorchColor)
    {
        if (!IsInitialized || endpoint == BuddyPartId.Torso)
            return;

        float clamped = Mathf.Clamp(amount, 0.0f, 1.0f);
        for (int index = 0; index < _connectorDefinitions.Length; index++)
        {
            ConnectorVisualDefinition definition = _connectorDefinitions[index];
            if (definition.PartA != endpoint && definition.PartB != endpoint)
                continue;

            if (_connectorMeshes[index].MaterialOverride is StandardMaterial3D material)
            {
                Color wanted = clamped <= 0.0f
                    ? definition.Color
                    : definition.Color.Lerp(scorchColor, clamped);
                if (material.AlbedoColor != wanted)
                    material.AlbedoColor = wanted;
            }
        }
    }

    public Color ConnectorAlbedo(int index) =>
        IsInitialized && index >= 0 && index < _connectorMeshes.Length &&
        _connectorMeshes[index].MaterialOverride is StandardMaterial3D material
            ? material.AlbedoColor
            : Colors.White;

    public Color AuthoredConnectorAlbedo(int index) =>
        IsInitialized && index >= 0 && index < _connectorDefinitions.Length
            ? _connectorDefinitions[index].Color
            : Colors.White;

    /// <summary>The albedo one part is currently rendered with, for scenario readouts.</summary>
    public Color PartAlbedo(BuddyPartId partId)
    {
        int index = (int)partId;
        if (!IsInitialized || index < 0 || index >= _partMeshes.Length)
            return Colors.White;

        return _partMeshes[index].MaterialOverride is StandardMaterial3D material
            ? material.AlbedoColor
            : Colors.White;
    }

    /// <summary>The authored albedo one part returns to when its mark has faded.</summary>
    public Color AuthoredPartAlbedo(BuddyPartId partId)
    {
        int index = (int)partId;
        return !IsInitialized || index < 0 || index >= _partDefinitions.Length
            ? Colors.White
            : _partDefinitions[index].Color;
    }

    /// <summary>
    /// Development/scenario-only yaw drive for the accepted ~30-degree three-quarter pose. It
    /// does not exist in normal composition and never touches physics — only the read-only
    /// visual sockets. Re-renders immediately so callers see the yawed pose without waiting a
    /// frame.
    /// </summary>
    public void SetDevelopmentYawDegrees(float degrees)
    {
        _developmentYawRadians = Mathf.DegToRad(degrees);
        if (IsInitialized)
        {
            UpdateVisuals(0.0, 1.0f);
        }
    }

    /// <summary>The total BodyYaw applied this frame (development + weighted facing),
    /// in degrees — the value scenarios feed their independent transform oracles.</summary>
    public float AppliedYawDegrees => Mathf.RadToDeg(_yawRadians);

    /// <summary>
    /// The part's currently rendered (interpolated) 2D world pose — the exact input the
    /// transform contract consumes. Scenarios use it to recompute the expected yawed/laned
    /// socket transform with independent math instead of trusting the presenter's own
    /// resolution as its oracle.
    /// </summary>
    public Vector2 RenderedPosition2D(BuddyPartId partId)
    {
        int index = (int)partId;
        if (!IsInitialized || index < 0 || index >= _rendered.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(partId), partId, "Unknown part.");
        }

        return _rendered[index].Position;
    }

    /// <summary>The part's mesh radius — scenarios derive the offset cap (fraction x radius).</summary>
    public float PartMeshRadius(BuddyPartId partId)
    {
        int index = (int)partId;
        if (!IsInitialized || index < 0 || index >= _meshRadii.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(partId), partId, "Unknown part.");
        }

        return _meshRadii[index];
    }

    /// <summary>
    /// Development/scenario-only authored offset for one part, in world units, pre-clamp.
    /// The pipeline's blend weight and the profile's per-part cap are always applied on
    /// top, so even a huge development offset cannot take the visual outside the bound.
    /// Real offset sources (activities, look-at) land in later M3.6 tasks through this
    /// same clamped path.
    /// </summary>
    public void SetDevelopmentOffset(BuddyPartId partId, Vector3 offset)
    {
        int index = (int)partId;
        if (index < 0 || index >= _developmentOffsets.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(partId), partId, "Unknown part.");
        }

        _developmentOffsets[index] = offset;
    }

    public override void _EnterTree() => TrySubscribeToRecovery();

    public override void _Process(double delta)
    {
        if (!IsInitialized)
        {
            return;
        }

        float fraction = Mathf.Clamp((float)Engine.GetPhysicsInterpolationFraction(), 0.0f, 1.0f);
        UpdateVisuals(delta, fraction);
    }

    public override void _ExitTree() => UnsubscribeFromRecovery();

    private void TrySubscribeToRecovery()
    {
        if (_subscribedToRecovery || !IsInitialized || !IsInsideTree() ||
            !GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            return;
        }

        Buddy.Recovery.HardRecovered += OnHardRecovered;
        _subscribedToRecovery = true;
    }

    private void UnsubscribeFromRecovery()
    {
        if (!_subscribedToRecovery)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
        }

        _subscribedToRecovery = false;
    }

    private void BuildParts()
    {
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyPartId id = (BuddyPartId)index;
            PartVisualDefinition definition = Profile.FindPart(id)
                ?? throw new InvalidOperationException($"Missing visual definition for {id}.");
            float radius = _transformSource!.ReadRadius(id) * definition.MeshRadiusScale;
            _partDefinitions[index] = definition;
            _meshRadii[index] = radius;

            var socket = new Node3D
            {
                Name = SocketName(id),
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            BodyYaw.AddChild(socket);
            _sockets[index] = socket;

            var meshInstance = new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = CreatePartMesh(id, radius),
                MaterialOverride = _materials.CreateLitMaterial(definition.Color),
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            socket.AddChild(meshInstance);
            _partMeshes[index] = meshInstance;

            // Inverted-hull outline shell: the same mesh Resource, front-face culled and
            // grown by the shared unshaded ink material. As a socket child at local identity
            // it inherits the socket's pose, scale, and final camera-space lane exactly;
            // only grow/culling differ from the primary mesh. Connectors and the face have
            // no shell (L4). Six part shells total.
            var outline = new MeshInstance3D
            {
                Name = "Outline",
                Mesh = meshInstance.Mesh,
                MaterialOverride = _materials.OutlineMaterial,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            socket.AddChild(outline);
            _partOutlines[index] = outline;

            if (id == BuddyPartId.Head)
            {
                BuildFace(socket, radius);
            }
        }
    }

    private void BuildConnectors()
    {
        int count = Profile.Connectors.Count;
        _connectorMeshes = new MeshInstance3D[count];
        _connectorDefinitions = new ConnectorVisualDefinition[count];
        _connectorAngles = new float[count];
        _connectorAuthoringLengths = new float[count];

        for (int index = 0; index < count; index++)
        {
            ConnectorVisualDefinition definition = Profile.Connectors[index]
                ?? throw new InvalidOperationException($"Missing connector definition at index {index}.");
            _connectorDefinitions[index] = definition;

            float authoringLength = definition.Radius * 2.0f;
            _connectorAuthoringLengths[index] = authoringLength;
            var mesh = new CapsuleMesh
            {
                Radius = definition.Radius,
                Height = authoringLength,
            };
            var instance = new MeshInstance3D
            {
                Name = $"Connector{index}",
                Mesh = mesh,
                MaterialOverride = _materials.CreateLitMaterial(definition.Color),
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            BodyYaw.AddChild(instance);
            _connectorMeshes[index] = instance;
        }
    }

    private void SnapSnapshots()
    {
        ReadSource(_current);
        for (int index = 0; index < _current.Length; index++)
        {
            _previous[index] = _current[index];
            _rendered[index] = _current[index];
            _velocityAngles[index] = WorldPlaneMapping.To3DRotationZ(_current[index].Rotation);
        }
    }

    private void ReadSource(BuddyVisualTransform[] destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = _transformSource!.ReadTransform((BuddyPartId)index);
        }
    }

    /// <summary>
    /// Holds or releases the performance layer's seconds clock. The laboratory pause calls
    /// this so a paused buddy is visually still: the M3.6 layer is driven by the rendered
    /// frame, which keeps arriving while the routed gameplay tick does not.
    /// </summary>
    public void SetPresentationHeld(bool held)
    {
        _presentationHeld = held;
        if (IsInitialized)
        {
            UpdateVisuals(0.0, 1.0f);
        }
    }

    private void UpdateVisuals(double delta, float fraction)
    {
        double performanceDelta = _presentationHeld ? 0.0 : delta;
        _performanceWeight = PosePipeline is { IsInitialized: true }
            ? PosePipeline.Evaluate(performanceDelta)
            : 0.0f;
        bool refusing = Buddy.Activity.IsRefusing;
        float facingYawDegrees = Facing is { IsInitialized: true }
            ? Facing.Evaluate(performanceDelta)
            : 0.0f;
        // A refusal is addressed directly to the player. Its visual body is frontal before
        // the head begins the left/right gesture; the facing model still eases toward zero
        // underneath, so release does not leave a stale forced orientation.
        _yawRadians = refusing
            ? 0.0f
            : _developmentYawRadians +
                (Mathf.DegToRad(facingYawDegrees) * _performanceWeight);
        if (Activities is { IsInitialized: true })
        {
            Activities.Evaluate(performanceDelta, _performanceWeight > 0.0f);
            _activityHeadYawRadians =
                Activities.YawRadiansFor((int)BuddyPartId.Head) * _performanceWeight;
        }
        else
        {
            _activityHeadYawRadians = 0.0f;
        }

        if (HeadLookAt is { IsInitialized: true })
        {
            LookAtAngles look = HeadLookAt.Evaluate(performanceDelta);
            float defendGazeWeight = ResolveDefendGazeWeight(performanceDelta);
            float gazeWeight = Mathf.Max(_performanceWeight, defendGazeWeight);
            // The refusal clip owns the head. Clear residual gaze yaw/pitch immediately so
            // its only motion is the requested vertical-axis turn with stable pitch/roll.
            _headLookYawRadians = refusing
                ? 0.0f
                : Mathf.DegToRad(look.YawDegrees) * gazeWeight;
            _headLookPitchRadians = refusing
                ? 0.0f
                : Mathf.DegToRad(look.PitchDegrees) * gazeWeight;
        }
        else
        {
            _headLookYawRadians = 0.0f;
            _headLookPitchRadians = 0.0f;
        }

        ReadSource(_current);

        // Resolve every part's interpolated pose first so the yaw pivot (the torso pose)
        // is current for all parts before any transform is applied.
        for (int index = 0; index < _rendered.Length; index++)
        {
            BuddyVisualTransform previous = _previous[index];
            BuddyVisualTransform current = _current[index];
            _rendered[index] = new BuddyVisualTransform(
                previous.Position.Lerp(current.Position, fraction),
                Mathf.LerpAngle(previous.Rotation, current.Rotation, fraction),
                previous.LinearVelocity.Lerp(current.LinearVelocity, fraction));
        }

        for (int index = 0; index < _rendered.Length; index++)
        {
            ApplyPartTransform(index, _rendered[index], delta);
        }

        if (Activities is { IsInitialized: true })
            Activities.SyncItemSocket();

        UpdateConnectors();
        UpdateFace();
    }

    private void ApplyPartTransform(int index, BuddyVisualTransform rendered, double delta)
    {
        PartVisualDefinition definition = _partDefinitions[index];
        float rotation = ResolveRotation(index, definition, rendered, delta);
        Node3D socket = _sockets[index];
        bool eatingHand = Activities is { IsInitialized: true, Current: ActivityId.Eat } &&
            index is (int)BuddyPartId.LeftHand or (int)BuddyPartId.RightHand;
        // Eating is a frontal two-hand performance: both hands sit beyond the face
        // plate and do not inherit far-limb sorting at three-quarter yaw.
        float depthOffset = eatingHand ? Profile.EatHandDepthOffset : definition.DepthOffset;
        float laneYawFade = eatingHand ? 0.0f : definition.LaneYawFade;
        // (1) mapped 2D pose Z=0 -> (2) add the clamped performance offset, then resolve
        // pose + BodyYaw with no lane component -> (3) add DepthOffset as a global
        // camera-axis Z addition -> (4) identical final transform to the primary mesh and
        // its outline shell (both socket children).
        socket.GlobalPosition = ResolveLanePosition(
            rendered.Position,
            depthOffset,
            ResolveFinalVisualOffset(index),
            laneYawFade);

        // The head additionally carries the weighted look-at: pitch about X, yaw added to
        // the body yaw about Y. Nothing else changes — the physics Z rotation is intact,
        // body yaw is untouched, and every other socket is exactly as before.
        if (index == (int)BuddyPartId.Head)
        {
            socket.GlobalRotation = new Vector3(
                _headLookPitchRadians,
                _yawRadians + _headLookYawRadians + _activityHeadYawRadians,
                rotation);
            return;
        }

        socket.GlobalRotation = new Vector3(0.0f, _yawRadians, rotation);
    }

    /// <summary>The head look-at yaw applied this frame, in degrees — a scenario oracle
    /// input (already scaled by the resolved gaze weight).</summary>
    public float AppliedHeadYawDegrees => Mathf.RadToDeg(_headLookYawRadians);

    /// <summary>The head look-at pitch applied this frame, in degrees.</summary>
    public float AppliedHeadPitchDegrees => Mathf.RadToDeg(_headLookPitchRadians);

    /// <summary>The refusal activity's applied visual head yaw, in degrees.</summary>
    public float AppliedActivityHeadYawDegrees => Mathf.RadToDeg(_activityHeadYawRadians);

    /// <summary>
    /// The blended, cap-clamped authored offset for a part this frame; zero whenever the
    /// pipeline holds Tracking. Clamped through the engine-free <see cref="BoundedOffset"/>
    /// so the visual can never stray more than the profile fraction of the part radius
    /// from the physics body (plan prime invariant 2).
    /// </summary>
    private Vector3 ResolvePerformanceOffset(int index)
    {
        if (_performanceWeight <= 0.0f)
        {
            return Vector3.Zero;
        }

        Vector3 raw = _developmentOffsets[index];
        if (Activities is { IsInitialized: true })
        {
            raw += Activities.OffsetFor(index);
        }

        if (raw == Vector3.Zero)
        {
            return Vector3.Zero;
        }

        float cap = PosePipeline!.Profile.OffsetCapRadiusFraction * _meshRadii[index];
        (float x, float y, float z) = BoundedOffset.Clamp(raw.X, raw.Y, raw.Z, cap);
        return new Vector3(x, y, z) * _performanceWeight;
    }

    private Vector3 ResolveFinalVisualOffset(int index)
    {
        Vector3 offset = ResolvePerformanceOffset(index);
        if (ImpactVisualOffset is { IsInitialized: true })
        {
            offset += ImpactVisualOffset.OffsetFor((BuddyPartId)index);
        }

        if (offset == Vector3.Zero)
        {
            return Vector3.Zero;
        }

        if (PosePipeline is not { IsInitialized: true })
        {
            return offset;
        }

        float cap = PosePipeline.Profile.OffsetCapRadiusFraction * _meshRadii[index];
        (float x, float y, float z) = BoundedOffset.Clamp(offset.X, offset.Y, offset.Z, cap);
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Applies BodyYaw to the mapped pose plus the performance offset (no lane), then adds
    /// the part's DepthOffset as a global camera-axis Z. At identity yaw with a zero offset
    /// this is exactly <c>To3D(pose)</c> with <c>Z = DepthOffset</c> — the M3.5 projection,
    /// bit-for-bit. At a scenario yaw the lane stays a pure depth change with no screen-X
    /// displacement.
    /// </summary>
    private Vector3 ResolveLanePosition(
        Vector2 worldPose2D,
        float depthOffset,
        Vector3 preYawOffset,
        float laneYawFade = 0.0f)
    {
        Vector3 yawed = ApplyBodyYaw(WorldPlaneMapping.To3D(worldPose2D) + preYawOffset);
        yawed.Z += depthOffset * ResolveLaneMultiplier(laneYawFade);
        return yawed;
    }

    private float ResolveLaneMultiplier(float laneYawFade)
    {
        if (laneYawFade <= 0.0f || Mathf.IsZeroApprox(_yawRadians))
        {
            return 1.0f;
        }

        float yawDegrees = Mathf.Abs(Mathf.RadToDeg(_yawRadians));
        float committedYawDegrees = Facing is { IsInitialized: true }
            ? Facing.Profile.FacingYawDegrees
            : yawDegrees;
        float progress = committedYawDegrees > 0.0f
            ? Mathf.Clamp(yawDegrees / committedYawDegrees, 0.0f, 1.0f)
            : 0.0f;
        float eased = progress * progress * (3.0f - (2.0f * progress));
        return 1.0f - (laneYawFade * eased);
    }

    /// <summary>
    /// Defending is a physics-dominated Tracking pose, but the angry buddy still watches
    /// the engaged glove. This independent weight eases in over the normal profile blend
    /// and cuts immediately when defense ends, so grab, pain/impact, and knockout forcing
    /// states retain the existing zero-gaze contract.
    /// </summary>
    private float ResolveDefendGazeWeight(double deltaSeconds)
    {
        if (PosePipeline is not { IsInitialized: true })
        {
            return 0.0f;
        }

        _defendGazeBlend ??= new PerformanceBlend(PosePipeline.Profile.PerformanceBlendSeconds);
        PresentationPoseMode mode = Buddy.CurrentToolReactionIntent.GuardActive
            ? PresentationPoseMode.Performance
            : PresentationPoseMode.Tracking;
        return _defendGazeBlend.Update(deltaSeconds, mode);
    }

    private Vector3 ApplyBodyYaw(Vector3 poseWithZeroZ)
    {
        if (_yawRadians == 0.0f)
        {
            return poseWithZeroZ;
        }

        Vector3 pivot = WorldPlaneMapping.To3D(_rendered[(int)BuddyPartId.Torso].Position);
        return pivot + new Basis(Vector3.Up, _yawRadians) * (poseWithZeroZ - pivot);
    }

    private float ResolveRotation(
        int index,
        PartVisualDefinition definition,
        BuddyVisualTransform rendered,
        double delta)
    {
        if (definition.RotationPolicy == VisualRotationPolicy.ScreenUpright)
        {
            return 0.0f;
        }

        if (definition.RotationPolicy == VisualRotationPolicy.Physics)
        {
            return WorldPlaneMapping.To3DRotationZ(rendered.Rotation);
        }

        if (rendered.LinearVelocity.LengthSquared() >=
            definition.VelocitySpeedDeadband * definition.VelocitySpeedDeadband)
        {
            float target = WorldPlaneMapping.To3DRotationZ(rendered.LinearVelocity.Angle());
            float weight = 1.0f - Mathf.Exp(-definition.VelocitySmoothing * (float)delta);
            _velocityAngles[index] = Mathf.LerpAngle(
                _velocityAngles[index], target, Mathf.Clamp(weight, 0.0f, 1.0f));
        }

        return _velocityAngles[index];
    }

    private void UpdateConnectors()
    {
        for (int index = 0; index < _connectorMeshes.Length; index++)
        {
            ConnectorVisualDefinition definition = _connectorDefinitions[index];
            int a = (int)definition.PartA;
            int b = (int)definition.PartB;
            Vector2 offset = _rendered[b].Position - _rendered[a].Position;
            float separation = offset.Length();
            float surfaceGap = separation - _meshRadii[a] - _meshRadii[b];
            bool minimumLengthClamped = surfaceGap < Profile.ConnectorMinimumLength;
            float length = Mathf.Max(Profile.ConnectorMinimumLength, surfaceGap);
            Vector2 center = (_rendered[a].Position + _rendered[b].Position) * 0.5f;

            if (separation > Mathf.Epsilon)
            {
                Vector2 direction = offset / separation;
                Vector3 mappedOffset = WorldPlaneMapping.To3D(offset);
                _connectorAngles[index] =
                    Mathf.Atan2(mappedOffset.Y, mappedOffset.X) - Mathf.Pi * 0.5f;

                // Unequal-radius parts have an asymmetric surface gap. Center the
                // connector between those surfaces; a center-to-center midpoint would
                // overlap the larger part and stop short of the smaller one. When the
                // gap is clamped to the minimum nub length, keep the stable midpoint.
                if (!minimumLengthClamped)
                {
                    center = _rendered[a].Position +
                        direction * (_meshRadii[a] + length * 0.5f);
                }
            }

            MeshInstance3D connector = _connectorMeshes[index];
            connector.GlobalPosition = ResolveLanePosition(center, definition.DepthOffset, Vector3.Zero);
            connector.GlobalRotation = new Vector3(0.0f, _yawRadians, _connectorAngles[index]);
            connector.Scale = new Vector3(1.0f, length / _connectorAuthoringLengths[index], 1.0f);
        }
    }

    /// <summary>
    /// Builds the head-front face: the Task 5 composed plate when a compositor is wired
    /// (a quad at surface + epsilon that inherits the socket transform fully — a real face
    /// rotates with the head, so the M3.5 sideways-glyph counter-rotation is retired with
    /// the glyph), or the legacy Label3D parity glyph otherwise.
    /// </summary>
    private void BuildFace(Node3D socket, float radius)
    {
        if (Face is not null)
        {
            _facePlateMaterial = new StandardMaterial3D
            {
                ResourceName = "BuddyFacePlateMaterial",
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            FacePlate = new MeshInstance3D
            {
                Name = "FacePlate",
                Mesh = new QuadMesh
                {
                    Size = new Vector2(FaceCompositor.PlateWorldSize, FaceCompositor.PlateWorldSize),
                },
                Position = new Vector3(0.0f, 0.0f, radius + Profile.FaceDepthEpsilon),
                MaterialOverride = _facePlateMaterial,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            socket.AddChild(FacePlate);
            return;
        }

        FaceLabel = new Label3D
        {
            Name = "Face",
            FontSize = Profile.FaceTextSize,
            PixelSize = Profile.FacePixelSize,
            Modulate = Profile.FaceColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector3(0.0f, 0.0f, radius + Profile.FaceDepthEpsilon),
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        socket.AddChild(FaceLabel);
    }

    private void UpdateFace()
    {
        if (Face is { } compositor)
        {
            // The compositor initializes after this presenter (it needs the activity
            // animator); until then the plate simply has no texture yet.
            if (compositor.IsInitialized)
            {
                compositor.Evaluate();
                if (_facePlateMaterial is { AlbedoTexture: null } &&
                    compositor.OutputTexture is { } texture)
                {
                    _facePlateMaterial.AlbedoTexture = texture;
                }
            }

            return;
        }

        string face = _transformSource!.ReadFace();
        if (_displayedFace != face)
        {
            _displayedFace = face;
            FaceLabel.Text = face;
        }

        // Body rotation plus the legacy local face rotation is either zero or the
        // sideways-ASCII quarter turn. Mapping their sum preserves the sign change at
        // the Y-flipped 2D→3D boundary and keeps the face screen-upright.
        float sourceHeadRotation = _current[(int)BuddyPartId.Head].Rotation;
        float faceRotation = sourceHeadRotation + _transformSource.ReadFaceDrawRotation();
        FaceLabel.GlobalRotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(faceRotation));
    }

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        SnapSnapshots();
        UpdateVisuals(0.0, 1.0f);
    }

    private PrimitiveMesh CreatePartMesh(BuddyPartId partId, float radius)
    {
        if (partId == BuddyPartId.Torso)
        {
            return new CapsuleMesh
            {
                Radius = radius,
                Height = radius * Profile.CapsuleHeightScale,
            };
        }

        return new SphereMesh
        {
            Radius = radius,
            Height = radius * 2.0f,
        };
    }

    private static string SocketName(BuddyPartId id) => id switch
    {
        BuddyPartId.Head => "HeadSocket",
        BuddyPartId.Torso => "TorsoSocket",
        BuddyPartId.LeftHand => "HandSocketL",
        BuddyPartId.RightHand => "HandSocketR",
        BuddyPartId.LeftFoot => "FootSocketL",
        BuddyPartId.RightFoot => "FootSocketR",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown buddy part."),
    };
}
