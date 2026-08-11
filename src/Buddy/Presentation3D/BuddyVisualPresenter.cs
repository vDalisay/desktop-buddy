using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Samples live gameplay and produces resolved visual pose frames. Stable scene-node,
/// geometry, material, connector, and decal ownership lives in <see cref="BuddyVisualRigView"/>.
/// </summary>
[GlobalClass]
public partial class BuddyVisualPresenter : Node3D
{
    private const float OrdinaryVelocityRotationScale = 0.28f;
    private const float FullVelocityRotationResponseSpeed = 180.0f;
    private const float LowVelocityReturnSmoothing = 8.0f;

    private readonly BuddyVisualTransform[] _previous =
        new BuddyVisualTransform[PuppetRigProfile.RequiredPartCount];
    private readonly BuddyVisualTransform[] _current =
        new BuddyVisualTransform[PuppetRigProfile.RequiredPartCount];
    private readonly BuddyVisualTransform[] _rendered =
        new BuddyVisualTransform[PuppetRigProfile.RequiredPartCount];
    private readonly float[] _velocityAngles =
        new float[PuppetRigProfile.RequiredPartCount];

    private IBuddyVisualTransformSource? _transformSource;
    private BuddyVisualRigView _rigView = null!;
    private bool _subscribedToRecovery;

    private float _yawRadians;
    private float _developmentYawRadians;
    private float _headLookYawRadians;
    private float _headLookPitchRadians;
    private float _activityHeadYawRadians;
    private PerformanceBlend? _defendGazeBlend;

    private readonly Vector3[] _developmentOffsets =
        new Vector3[PuppetRigProfile.RequiredPartCount];
    private float _performanceWeight;
    private bool _presentationHeld;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public BuddyVisualProfile Profile { get; set; } = null!;
    [Export] public BuddyPosePipeline? PosePipeline { get; set; }
    [Export] public FacingController? Facing { get; set; }
    [Export] public ActivityAnimator? Activities { get; set; }
    [Export] public HeadLookAtComponent? HeadLookAt { get; set; }
    [Export] public FaceCompositor? Face { get; set; }
    [Export] public ImpactVisualOffsetComponent? ImpactVisualOffset { get; set; }

    public bool IsInitialized { get; private set; }
    public BuddyVisualRigView RigView => _rigView;
    public Node3D BodyYaw => _rigView.BodyYaw;
    public Label3D FaceLabel => _rigView.FaceLabel;
    public MeshInstance3D? FacePlate => _rigView.FacePlate;
    public int PartVisualCount => IsInitialized ? _rigView.PartVisualCount : 0;
    public int ConnectorVisualCount => IsInitialized ? _rigView.ConnectorVisualCount : 0;
    public StandardMaterial3D OutlineMaterial => _rigView.OutlineMaterial;

    /// <summary>
    /// Builds the shared rig view once. Supplying a source is retained for existing tests;
    /// production gameplay uses the live authoritative rig wrapper.
    /// </summary>
    public void Initialize(IBuddyVisualTransformSource? transformSource = null)
    {
        if (IsInitialized)
            return;

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

        _rigView = new BuddyVisualRigView
        {
            Name = "RigView",
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        _rigView.SetFaceCompositor(Face);
        AddChild(_rigView);
        _rigView.Initialize(Profile, _transformSource);

        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        SnapSnapshots();
        IsInitialized = true;
        TrySubscribeToRecovery();
        UpdateVisuals(0.0, 1.0f);
    }

    public void CaptureTickSnapshot()
    {
        if (!IsInitialized || _transformSource is null)
            return;

        ReadSource(_previous);
    }

    public Node3D GetPartSocket(BuddyPartId partId) => _rigView.GetPartSocket(partId);
    public Node3D GetConnectorVisual(int index) => _rigView.GetConnectorVisual(index);
    public MeshInstance3D GetPartMesh(BuddyPartId partId) => _rigView.GetPartMesh(partId);
    public MeshInstance3D GetPartOutline(BuddyPartId partId) => _rigView.GetPartOutline(partId);

    public void SetPartScorch(BuddyPartId partId, float amount, Color scorchColor) =>
        _rigView.SetPartScorch(partId, amount, scorchColor);

    public void SetEndpointConnectorScorch(
        BuddyPartId endpoint,
        float amount,
        Color scorchColor) =>
        _rigView.SetEndpointConnectorScorch(endpoint, amount, scorchColor);

    public Color ConnectorAlbedo(int index) => _rigView.ConnectorAlbedo(index);
    public Color AuthoredConnectorAlbedo(int index) => _rigView.AuthoredConnectorAlbedo(index);
    public Color PartAlbedo(BuddyPartId partId) => _rigView.PartAlbedo(partId);
    public Color AuthoredPartAlbedo(BuddyPartId partId) => _rigView.AuthoredPartAlbedo(partId);

    public void SetDevelopmentYawDegrees(float degrees)
    {
        _developmentYawRadians = Mathf.DegToRad(degrees);
        if (IsInitialized)
            UpdateVisuals(0.0, 1.0f);
    }

    public float AppliedYawDegrees => Mathf.RadToDeg(_yawRadians);

    public Vector2 RenderedPosition2D(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        if (!IsInitialized)
            throw new InvalidOperationException("BuddyVisualPresenter used before initialization.");

        return _rendered[index].Position;
    }

    public float PartMeshRadius(BuddyPartId partId) => _rigView.PartMeshRadius(partId);

    public void SetDevelopmentOffset(BuddyPartId partId, Vector3 offset)
    {
        _developmentOffsets[CheckedPartIndex(partId)] = offset;
    }

    public void SetPresentationHeld(bool held)
    {
        _presentationHeld = held;
        if (IsInitialized)
            UpdateVisuals(0.0, 1.0f);
    }

    public float AppliedHeadYawDegrees => Mathf.RadToDeg(_headLookYawRadians);
    public float AppliedHeadPitchDegrees => Mathf.RadToDeg(_headLookPitchRadians);
    public float AppliedActivityHeadYawDegrees => Mathf.RadToDeg(_activityHeadYawRadians);

    public override void _EnterTree() => TrySubscribeToRecovery();

    public override void _Process(double delta)
    {
        if (!IsInitialized)
            return;

        float fraction = Mathf.Clamp(
            (float)Engine.GetPhysicsInterpolationFraction(),
            0.0f,
            1.0f);
        UpdateVisuals(delta, fraction);
    }

    public override void _ExitTree() => UnsubscribeFromRecovery();

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

        ResolveHeadLook(performanceDelta, refusing);
        ReadSource(_current);
        Interpolate(fraction);

        BuddyVisualPartPose head = ResolvePartPose(BuddyPartId.Head, delta);
        BuddyVisualPartPose torso = ResolvePartPose(BuddyPartId.Torso, delta);
        BuddyVisualPartPose leftHand = ResolvePartPose(BuddyPartId.LeftHand, delta);
        BuddyVisualPartPose rightHand = ResolvePartPose(BuddyPartId.RightHand, delta);
        BuddyVisualPartPose leftFoot = ResolvePartPose(BuddyPartId.LeftFoot, delta);
        BuddyVisualPartPose rightFoot = ResolvePartPose(BuddyPartId.RightFoot, delta);

        FaceRenderState? faceState = Face is { IsInitialized: true } compositor
            ? compositor.Evaluate()
            : null;
        string fallbackFace = faceState.HasValue
            ? string.Empty
            : _transformSource!.ReadFace();
        float fallbackFaceRotation = faceState.HasValue
            ? 0.0f
            : ResolveFallbackFaceRotation();

        var frame = new BuddyVisualPoseFrame(
            head,
            torso,
            leftHand,
            rightHand,
            leftFoot,
            rightFoot,
            _yawRadians,
            faceState,
            fallbackFace,
            fallbackFaceRotation);
        _rigView.ApplyPose(frame);

        if (Activities is { IsInitialized: true })
            Activities.SyncItemSocket();
    }

    private void ResolveHeadLook(double performanceDelta, bool refusing)
    {
        if (HeadLookAt is not { IsInitialized: true })
        {
            _headLookYawRadians = 0.0f;
            _headLookPitchRadians = 0.0f;
            return;
        }

        LookAtAngles look = HeadLookAt.Evaluate(performanceDelta);
        float defendGazeWeight = ResolveDefendGazeWeight(performanceDelta);
        float gazeWeight = HeadLookAt.CurrentSource == LookAtSource.Item
            ? 1.0f
            : Mathf.Max(_performanceWeight, defendGazeWeight);
        _headLookYawRadians = refusing
            ? 0.0f
            : Mathf.DegToRad(look.YawDegrees) * gazeWeight;
        _headLookPitchRadians = refusing
            ? 0.0f
            : Mathf.DegToRad(look.PitchDegrees) * gazeWeight;
    }

    private void Interpolate(float fraction)
    {
        for (int index = 0; index < _rendered.Length; index++)
        {
            BuddyVisualTransform previous = _previous[index];
            BuddyVisualTransform current = _current[index];
            _rendered[index] = new BuddyVisualTransform(
                previous.Position.Lerp(current.Position, fraction),
                Mathf.LerpAngle(previous.Rotation, current.Rotation, fraction),
                previous.LinearVelocity.Lerp(current.LinearVelocity, fraction));
        }
    }

    private BuddyVisualPartPose ResolvePartPose(BuddyPartId partId, double delta)
    {
        int index = (int)partId;
        BuddyVisualTransform rendered = _rendered[index];
        PartVisualDefinition definition = _rigView.GetPartDefinition(partId);
        float rotation = ResolveRotation(index, definition, rendered, delta);

        bool eatingHand = Activities is { IsInitialized: true, Current: ActivityId.Eat } &&
            partId is BuddyPartId.LeftHand or BuddyPartId.RightHand;
        float depthOffset = eatingHand ? Profile.EatHandDepthOffset : definition.DepthOffset;
        float laneYawFade = eatingHand ? 0.0f : definition.LaneYawFade;
        Vector3 position = ResolveLanePosition(
            rendered.Position,
            depthOffset,
            ResolveFinalVisualOffset(index),
            laneYawFade);

        Vector3 globalRotation;
        if (partId == BuddyPartId.Head)
        {
            float headYaw = HeadLookAt?.CurrentSource == LookAtSource.Item
                ? _headLookYawRadians + _activityHeadYawRadians
                : _yawRadians + _headLookYawRadians + _activityHeadYawRadians;
            globalRotation = new Vector3(
                _headLookPitchRadians,
                headYaw,
                rotation);
        }
        else
        {
            globalRotation = new Vector3(0.0f, _yawRadians, rotation);
        }

        return new BuddyVisualPartPose(rendered, position, globalRotation);
    }

    private Vector3 ResolvePerformanceOffset(int index)
    {
        if (_performanceWeight <= 0.0f)
            return Vector3.Zero;

        Vector3 raw = _developmentOffsets[index];
        if (Activities is { IsInitialized: true })
            raw += Activities.OffsetFor(index);
        if (raw == Vector3.Zero)
            return Vector3.Zero;

        float cap = PosePipeline!.Profile.OffsetCapRadiusFraction *
            _rigView.PartMeshRadius((BuddyPartId)index);
        (float x, float y, float z) = BoundedOffset.Clamp(raw.X, raw.Y, raw.Z, cap);
        return new Vector3(x, y, z) * _performanceWeight;
    }

    private Vector3 ResolveFinalVisualOffset(int index)
    {
        Vector3 offset = ResolvePerformanceOffset(index);
        if (ImpactVisualOffset is { IsInitialized: true })
            offset += ImpactVisualOffset.OffsetFor((BuddyPartId)index);
        if (offset == Vector3.Zero)
            return Vector3.Zero;
        if (PosePipeline is not { IsInitialized: true })
            return offset;

        float cap = PosePipeline.Profile.OffsetCapRadiusFraction *
            _rigView.PartMeshRadius((BuddyPartId)index);
        (float x, float y, float z) =
            BoundedOffset.Clamp(offset.X, offset.Y, offset.Z, cap);
        return new Vector3(x, y, z);
    }

    private Vector3 ResolveLanePosition(
        Vector2 worldPose2D,
        float depthOffset,
        Vector3 preYawOffset,
        float laneYawFade)
    {
        Vector3 yawed = ApplyBodyYaw(WorldPlaneMapping.To3D(worldPose2D) + preYawOffset);
        yawed.Z += depthOffset * ResolveLaneMultiplier(laneYawFade);
        return yawed;
    }

    private float ResolveLaneMultiplier(float laneYawFade)
    {
        if (laneYawFade <= 0.0f || Mathf.IsZeroApprox(_yawRadians))
            return 1.0f;

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

    private float ResolveDefendGazeWeight(double deltaSeconds)
    {
        if (PosePipeline is not { IsInitialized: true })
            return 0.0f;

        _defendGazeBlend ??= new PerformanceBlend(
            PosePipeline.Profile.PerformanceBlendSeconds);
        PresentationPoseMode mode = Buddy.CurrentToolReactionIntent.GuardActive
            ? PresentationPoseMode.Performance
            : PresentationPoseMode.Tracking;
        return _defendGazeBlend.Update(deltaSeconds, mode);
    }

    private Vector3 ApplyBodyYaw(Vector3 poseWithZeroZ)
    {
        if (_yawRadians == 0.0f)
            return poseWithZeroZ;

        Vector3 pivot = WorldPlaneMapping.To3D(
            _rendered[(int)BuddyPartId.Torso].Position);
        return pivot + new Basis(Vector3.Up, _yawRadians) * (poseWithZeroZ - pivot);
    }

    private float ResolveRotation(
        int index,
        PartVisualDefinition definition,
        BuddyVisualTransform rendered,
        double delta)
    {
        if (definition.RotationPolicy == VisualRotationPolicy.ScreenUpright)
            return 0.0f;
        if (definition.RotationPolicy == VisualRotationPolicy.Physics)
            return WorldPlaneMapping.To3DRotationZ(rendered.Rotation);

        float speed = rendered.LinearVelocity.Length();
        float deadband = definition.VelocitySpeedDeadband;
        float smoothingWeight;
        if (speed >= deadband)
        {
            float target = WorldPlaneMapping.To3DRotationZ(
                rendered.LinearVelocity.Angle());
            smoothingWeight = 1.0f -
                Mathf.Exp(-definition.VelocitySmoothing * (float)delta);
            _velocityAngles[index] = Mathf.LerpAngle(
                _velocityAngles[index],
                target,
                Mathf.Clamp(smoothingWeight, 0.0f, 1.0f));
        }
        else
        {
            // Ordinary idle/walk motion should settle toward an upright readable silhouette.
            // High-speed throws and impacts still receive the complete velocity-aligned angle.
            smoothingWeight = 1.0f -
                Mathf.Exp(-LowVelocityReturnSmoothing * (float)delta);
            _velocityAngles[index] = Mathf.LerpAngle(
                _velocityAngles[index],
                0.0f,
                Mathf.Clamp(smoothingWeight, 0.0f, 1.0f));
        }

        float visualScale = VelocityRotationResponse.Scale(
            speed,
            deadband,
            OrdinaryVelocityRotationScale,
            FullVelocityRotationResponseSpeed);
        return Mathf.LerpAngle(0.0f, _velocityAngles[index], visualScale);
    }

    private float ResolveFallbackFaceRotation()
    {
        float sourceHeadRotation = _current[(int)BuddyPartId.Head].Rotation;
        float faceRotation =
            sourceHeadRotation + _transformSource!.ReadFaceDrawRotation();
        return WorldPlaneMapping.To3DRotationZ(faceRotation);
    }

    private void SnapSnapshots()
    {
        ReadSource(_current);
        for (int index = 0; index < _current.Length; index++)
        {
            _previous[index] = _current[index];
            _rendered[index] = _current[index];
            _velocityAngles[index] =
                WorldPlaneMapping.To3DRotationZ(_current[index].Rotation);
        }
    }

    private void ReadSource(BuddyVisualTransform[] destination)
    {
        for (int index = 0; index < destination.Length; index++)
            destination[index] = _transformSource!.ReadTransform((BuddyPartId)index);
    }

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
            return;

        if (GodotObject.IsInstanceValid(Buddy) &&
            GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
        }

        _subscribedToRecovery = false;
    }

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        SnapSnapshots();
        UpdateVisuals(0.0, 1.0f);
    }

    private static int CheckedPartIndex(BuddyPartId partId)
    {
        int index = (int)partId;
        if (index < 0 || index >= PuppetRigProfile.RequiredPartCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partId),
                partId,
                "Unknown buddy part.");
        }

        return index;
    }
}
