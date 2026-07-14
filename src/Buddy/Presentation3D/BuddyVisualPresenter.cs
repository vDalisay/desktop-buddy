using System;
using DesktopBuddy.Buddy.Physics;
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
    private string _displayedFace = string.Empty;
    private bool _subscribedToRecovery;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public BuddyVisualProfile Profile { get; set; } = null!;

    public bool IsInitialized { get; private set; }
    public Node3D BodyYaw { get; private set; } = null!;
    public Label3D FaceLabel { get; private set; } = null!;
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
                MaterialOverride = CreateUnshadedMaterial(definition.Color),
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            socket.AddChild(meshInstance);
            _partMeshes[index] = meshInstance;

            if (id == BuddyPartId.Head)
            {
                FaceLabel = new Label3D
                {
                    Name = "Face",
                    FontSize = Profile.FaceTextSize,
                    Modulate = Profile.FaceColor,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Position = new Vector3(0.0f, 0.0f, radius + Profile.FaceDepthEpsilon),
                    PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
                };
                socket.AddChild(FaceLabel);
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
                MaterialOverride = CreateUnshadedMaterial(definition.Color),
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

    private void UpdateVisuals(double delta, float fraction)
    {
        ReadSource(_current);
        for (int index = 0; index < _rendered.Length; index++)
        {
            BuddyVisualTransform previous = _previous[index];
            BuddyVisualTransform current = _current[index];
            var rendered = new BuddyVisualTransform(
                previous.Position.Lerp(current.Position, fraction),
                Mathf.LerpAngle(previous.Rotation, current.Rotation, fraction),
                previous.LinearVelocity.Lerp(current.LinearVelocity, fraction));
            _rendered[index] = rendered;
            ApplyPartTransform(index, rendered, delta);
        }

        UpdateConnectors();
        UpdateFace();
    }

    private void ApplyPartTransform(int index, BuddyVisualTransform rendered, double delta)
    {
        PartVisualDefinition definition = _partDefinitions[index];
        Vector3 position = WorldPlaneMapping.To3D(rendered.Position);
        position.Z = definition.DepthOffset;
        float rotation = ResolveRotation(index, definition, rendered, delta);
        Node3D socket = _sockets[index];
        socket.GlobalPosition = position;
        socket.GlobalRotation = new Vector3(0.0f, 0.0f, rotation);
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

            Vector3 position = WorldPlaneMapping.To3D(center);
            position.Z = definition.DepthOffset;
            MeshInstance3D connector = _connectorMeshes[index];
            connector.GlobalPosition = position;
            connector.GlobalRotation = new Vector3(0.0f, 0.0f, _connectorAngles[index]);
            connector.Scale = new Vector3(1.0f, length / _connectorAuthoringLengths[index], 1.0f);
        }
    }

    private void UpdateFace()
    {
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

    private static StandardMaterial3D CreateUnshadedMaterial(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

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
