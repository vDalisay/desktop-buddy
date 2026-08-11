using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Stable visual rig shared by live gameplay and the character-editor preview. This node
/// owns trusted geometry, sockets, materials, connectors, and decal plates; callers may
/// supply resolved pose and narrow appearance values but can never replace geometry.
/// </summary>
[GlobalClass]
public partial class BuddyVisualRigView : Node3D
{
    public const float AccentPlateWorldSize = 32.0f;

    private readonly Node3D[] _sockets = new Node3D[PuppetRigProfile.RequiredPartCount];
    private readonly MeshInstance3D[] _partMeshes =
        new MeshInstance3D[PuppetRigProfile.RequiredPartCount];
    private readonly MeshInstance3D[] _partOutlines =
        new MeshInstance3D[PuppetRigProfile.RequiredPartCount];
    private readonly PartVisualDefinition[] _partDefinitions =
        new PartVisualDefinition[PuppetRigProfile.RequiredPartCount];
    private readonly float[] _meshRadii = new float[PuppetRigProfile.RequiredPartCount];
    private readonly Color[] _activeBaseColors =
        new Color[PuppetRigProfile.RequiredPartCount];
    private readonly float[] _scorchAmounts =
        new float[PuppetRigProfile.RequiredPartCount];
    private readonly Color[] _scorchColors =
        new Color[PuppetRigProfile.RequiredPartCount];
    private readonly Texture2D?[] _surfaceUnderlays =
        new Texture2D?[PuppetRigProfile.RequiredPartCount];
    private readonly MeshInstance3D?[] _paintLayers =
        new MeshInstance3D?[PuppetRigProfile.RequiredPartCount];

    private MeshInstance3D[] _connectorMeshes = Array.Empty<MeshInstance3D>();
    private MeshInstance3D?[] _connectorPaintLayers = Array.Empty<MeshInstance3D?>();
    private ConnectorVisualDefinition[] _connectorDefinitions =
        Array.Empty<ConnectorVisualDefinition>();
    private float[] _connectorAngles = Array.Empty<float>();
    private float[] _connectorAuthoringLengths = Array.Empty<float>();

    private BuddyVisualProfile _trustedProfile = null!;
    private IBuddyVisualTransformSource _geometrySource = null!;
    private BuddyLookMaterialLibrary _materials = null!;
    private FaceCompositor? _faceCompositor;
    private StandardMaterial3D? _facePlateMaterial;
    private StandardMaterial3D? _accentPlateMaterial;
    private string _displayedFace = string.Empty;
    private CompiledCharacterAppearance? _activeAppearance;
    private long _appearanceMutationCount;
    private long _partMaterialMutationCount;

    public bool IsInitialized { get; private set; }
    public Node3D BodyYaw { get; private set; } = null!;
    public Label3D FaceLabel { get; private set; } = null!;
    public MeshInstance3D? FacePlate { get; private set; }
    public MeshInstance3D TorsoAccentPlate { get; private set; } = null!;
    public FaceRenderState? LastFaceState { get; private set; }

    public int PartVisualCount => IsInitialized ? _partMeshes.Length : 0;
    public int ConnectorVisualCount => IsInitialized ? _connectorMeshes.Length : 0;
    public BuddyVisualProfile TrustedProfile => _trustedProfile;
    public IBuddyVisualTransformSource GeometrySource => _geometrySource;
    public CompiledCharacterAppearance? ActiveAppearance => _activeAppearance;
    public long AppearanceMutationCount => _appearanceMutationCount;
    public long PartMaterialMutationCount => _partMaterialMutationCount;

    /// <summary>
    /// The compositor remains an external sampler during A2. The rig owns only its plate
    /// and output binding; A4 replaces this with the parameterized compositor ownership.
    /// </summary>
    internal void SetFaceCompositor(FaceCompositor? compositor)
    {
        if (IsInitialized)
            throw new InvalidOperationException("Face compositor must be supplied before initialization.");

        _faceCompositor = compositor;
    }

    public void Initialize(
        BuddyVisualProfile trustedProfile,
        IBuddyVisualTransformSource geometrySource)
    {
        if (IsInitialized)
            return;

        if (!GodotObject.IsInstanceValid(trustedProfile))
            throw new InvalidOperationException("BuddyVisualRigView requires a trusted visual profile.");
        ArgumentNullException.ThrowIfNull(geometrySource);

        Godot.Collections.Array<string> errors = trustedProfile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid buddy visual profile: {string.Join("; ", errors)}");
        }

        _trustedProfile = trustedProfile;
        _geometrySource = geometrySource;
        _materials = new BuddyLookMaterialLibrary(trustedProfile.Look);
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        BodyYaw = new Node3D
        {
            Name = "BodyYaw",
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        AddChild(BodyYaw);

        BuildParts();
        BuildConnectors();
        IsInitialized = true;
        ApplyBuiltInAppearance();
    }

    public void ApplyPose(in BuddyVisualPoseFrame frame)
    {
        EnsureInitialized();

        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyVisualPartPose pose = frame.Part((BuddyPartId)index);
            Node3D socket = _sockets[index];
            socket.GlobalPosition = pose.GlobalPosition;
            socket.GlobalRotation = pose.GlobalRotation;
        }

        UpdateConnectors(frame);
        UpdateFace(frame);
    }

    /// <summary>
    /// Applies only the narrow appearance boundary. Trusted meshes, sockets, connector
    /// definitions, profile, transform source, and presentation tuning remain untouched.
    /// Feature values are retained for A3/A4 renderers; A2 mutates only part material colors.
    /// </summary>
    public void ApplyAppearance(in CompiledCharacterAppearance appearance)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(appearance);
        if (Equals(_activeAppearance, appearance))
            return;

        BuddyVisualRigTrustSnapshot trust = CaptureTrustSnapshot();
        ApplyPartColorSet(appearance.PartColors);
        ApplyCosmeticAppearance(appearance);
        _activeAppearance = appearance;
        _appearanceMutationCount++;

        if (!TrustedGeometryMatches(trust))
        {
            throw new InvalidOperationException(
                "Applying a character appearance changed trusted visual geometry.");
        }
    }

    public void ApplyBuiltInAppearance()
    {
        if (!IsInitialized)
            return;

        bool changed = _activeAppearance is not null;
        for (int index = 0; index < _partMeshes.Length; index++)
            changed |= SetActiveBaseColor(index, _partDefinitions[index].Color);

        _activeAppearance = null;
        ClearCosmeticAppearance();
        if (changed)
            _appearanceMutationCount++;

        if (GodotObject.IsInstanceValid(TorsoAccentPlate))
            TorsoAccentPlate.Visible = false;
    }

    public void SetPartScorch(BuddyPartId partId, float amount, Color scorchColor)
    {
        int index = CheckedPartIndex(partId);
        if (!IsInitialized)
            return;

        _scorchAmounts[index] = Mathf.Clamp(amount, 0.0f, 1.0f);
        _scorchColors[index] = scorchColor;
        ApplyPartColor(index);
    }

    public void SetEndpointConnectorScorch(
        BuddyPartId endpoint,
        float amount,
        Color scorchColor)
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

    internal void SetSurfaceUnderlay(BuddyPartId partId, Texture2D? texture)
    {
        int index = CheckedPartIndex(partId);
        _surfaceUnderlays[index] = texture;
        // Bridges unbind during teardown, which can run after the rig's nodes are freed.
        if (!IsInitialized || _paintLayers[index] is not MeshInstance3D layer ||
            !GodotObject.IsInstanceValid(layer))
        {
            return;
        }

        if (layer.MaterialOverride is StandardMaterial3D material)
            material.AlbedoTexture = texture;
        layer.Visible = texture is not null;

        for (int connectorIndex = 0; connectorIndex < _connectorDefinitions.Length; connectorIndex++)
        {
            if (ConnectorPaintPart(_connectorDefinitions[connectorIndex]) != partId ||
                _connectorPaintLayers[connectorIndex] is not MeshInstance3D connectorLayer ||
                !GodotObject.IsInstanceValid(connectorLayer))
                continue;
            if (connectorLayer.MaterialOverride is StandardMaterial3D connectorMaterial)
                connectorMaterial.AlbedoTexture = texture;
            connectorLayer.Visible = texture is not null && _connectorMeshes[connectorIndex].Visible;
        }
    }

    internal Texture2D? SurfaceUnderlay(BuddyPartId partId) =>
        _surfaceUnderlays[CheckedPartIndex(partId)];

    public Node3D GetPartSocket(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        EnsureInitialized();
        return _sockets[index];
    }

    public Node3D GetConnectorVisual(int index)
    {
        EnsureInitialized();
        if (index < 0 || index >= _connectorMeshes.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Unknown connector visual.");

        return _connectorMeshes[index];
    }

    public MeshInstance3D GetPartMesh(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        EnsureInitialized();
        return _partMeshes[index];
    }

    public MeshInstance3D GetPartOutline(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        EnsureInitialized();
        return _partOutlines[index];
    }

    internal PartVisualDefinition GetPartDefinition(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        EnsureInitialized();
        return _partDefinitions[index];
    }

    public float PartMeshRadius(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        EnsureInitialized();
        return _meshRadii[index];
    }

    public StandardMaterial3D OutlineMaterial
    {
        get
        {
            EnsureInitialized();
            return _materials.OutlineMaterial;
        }
    }

    public Color PartAlbedo(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        return IsInitialized &&
            _partMeshes[index].MaterialOverride is StandardMaterial3D material
                ? material.AlbedoColor
                : Colors.White;
    }

    public Color AuthoredPartAlbedo(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        return IsInitialized ? _partDefinitions[index].Color : Colors.White;
    }

    public Color ActiveBasePartAlbedo(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        return IsInitialized ? _activeBaseColors[index] : Colors.White;
    }

    public float PartScorchAmount(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        return IsInitialized ? _scorchAmounts[index] : 0.0f;
    }

    public Color PartScorchColor(BuddyPartId partId)
    {
        int index = CheckedPartIndex(partId);
        return IsInitialized ? _scorchColors[index] : Colors.Transparent;
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

    private void ApplyPartColorSet(in PartColorSet colors)
    {
        SetActiveBaseColor((int)BuddyPartId.Head, ToGodotColor(colors.Head));
        SetActiveBaseColor((int)BuddyPartId.Torso, ToGodotColor(colors.Torso));
        SetActiveBaseColor((int)BuddyPartId.LeftHand, ToGodotColor(colors.LeftHand));
        SetActiveBaseColor((int)BuddyPartId.RightHand, ToGodotColor(colors.RightHand));
        SetActiveBaseColor((int)BuddyPartId.LeftFoot, ToGodotColor(colors.LeftFoot));
        SetActiveBaseColor((int)BuddyPartId.RightFoot, ToGodotColor(colors.RightFoot));
    }

    private bool SetActiveBaseColor(int index, Color color)
    {
        if (_activeBaseColors[index] == color)
            return false;

        _activeBaseColors[index] = color;
        ApplyPartColor(index);
        return true;
    }

    private bool ApplyPartColor(int index)
    {
        if (_partMeshes[index].MaterialOverride is not StandardMaterial3D material)
            return false;

        float scorch = _scorchAmounts[index];
        Color baseColor = _activeBaseColors[index];
        Color wanted = scorch <= 0.0f
            ? baseColor
            : baseColor.Lerp(_scorchColors[index], scorch);
        if (material.AlbedoColor == wanted)
            return false;

        material.AlbedoColor = wanted;
        // Paint sits on its own shell above the base colour, so it has to scorch with it.
        if (_paintLayers[index]?.MaterialOverride is StandardMaterial3D paint)
        {
            paint.AlbedoColor = scorch <= 0.0f
                ? Colors.White
                : Colors.White.Lerp(_scorchColors[index], scorch);
        }
        _partMaterialMutationCount++;
        return true;
    }

    private static Color ToGodotColor(Rgba32 color) => new(
        color.R / 255.0f,
        color.G / 255.0f,
        color.B / 255.0f,
        1.0f);

    private void BuildParts()
    {
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyPartId id = (BuddyPartId)index;
            PartVisualDefinition definition = _trustedProfile.FindPart(id)
                ?? throw new InvalidOperationException($"Missing visual definition for {id}.");
            float radius = _geometrySource.ReadRadius(id) * definition.MeshRadiusScale;
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

            var outline = new MeshInstance3D
            {
                Name = "Outline",
                Mesh = meshInstance.Mesh,
                MaterialOverride = _materials.OutlineMaterial,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            socket.AddChild(outline);
            _partOutlines[index] = outline;

            // Paint shell: the same trusted mesh, grown just clear of the body so the painted
            // pixels read above the base colour while blank pixels are discarded and reveal it.
            var paintLayer = new MeshInstance3D
            {
                Name = "Paint",
                Mesh = meshInstance.Mesh,
                MaterialOverride = _materials.CreatePaintMaterial(),
                Visible = false,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            if (PaintUvRegion.IsLimb((PaintPart)id) && paintLayer.MaterialOverride is StandardMaterial3D limbPaint)
                limbPaint.Uv1Scale = new Vector3(0.5f, 1.0f, 1.0f);
            socket.AddChild(paintLayer);
            _paintLayers[index] = paintLayer;

            if (id == BuddyPartId.Head)
                BuildFace(socket, radius);
            else if (id == BuddyPartId.Torso)
                BuildAccentPlate(socket, radius);
        }
    }

    private void BuildConnectors()
    {
        int count = _trustedProfile.Connectors.Count;
        _connectorMeshes = new MeshInstance3D[count];
        _connectorPaintLayers = new MeshInstance3D?[count];
        _connectorDefinitions = new ConnectorVisualDefinition[count];
        _connectorAngles = new float[count];
        _connectorAuthoringLengths = new float[count];

        for (int index = 0; index < count; index++)
        {
            ConnectorVisualDefinition definition = _trustedProfile.Connectors[index]
                ?? throw new InvalidOperationException(
                    $"Missing connector definition at index {index}.");
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

            if (ConnectorPaintPart(definition) is not null)
            {
                var paintLayer = new MeshInstance3D
                {
                    Name = "Paint",
                    Mesh = mesh,
                    MaterialOverride = _materials.CreatePaintMaterial(),
                    Visible = false,
                    PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
                };
                if (paintLayer.MaterialOverride is StandardMaterial3D connectorPaint)
                {
                    connectorPaint.Uv1Scale = new Vector3(0.5f, 1.0f, 1.0f);
                    connectorPaint.Uv1Offset = new Vector3(0.5f, 0.0f, 0.0f);
                }
                instance.AddChild(paintLayer);
                _connectorPaintLayers[index] = paintLayer;
            }
        }
    }

    private static BuddyPartId? ConnectorPaintPart(ConnectorVisualDefinition definition)
    {
        BuddyPartId endpoint = definition.PartA == BuddyPartId.Torso ? definition.PartB : definition.PartA;
        return endpoint is BuddyPartId.LeftHand or BuddyPartId.RightHand or BuddyPartId.LeftFoot or BuddyPartId.RightFoot
            ? endpoint
            : null;
    }

    private void BuildFace(Node3D socket, float radius)
    {
        if (_faceCompositor is not null)
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
                    Size = new Vector2(
                        FaceCompositor.PlateWorldSize,
                        FaceCompositor.PlateWorldSize),
                },
                Position = new Vector3(
                    0.0f,
                    0.0f,
                    radius + _trustedProfile.FaceDepthEpsilon),
                MaterialOverride = _facePlateMaterial,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            socket.AddChild(FacePlate);
            return;
        }

        FaceLabel = new Label3D
        {
            Name = "Face",
            FontSize = _trustedProfile.FaceTextSize,
            PixelSize = _trustedProfile.FacePixelSize,
            Modulate = _trustedProfile.FaceColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector3(
                0.0f,
                0.0f,
                radius + _trustedProfile.FaceDepthEpsilon),
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        socket.AddChild(FaceLabel);
    }

    private void BuildAccentPlate(Node3D socket, float radius)
    {
        _accentPlateMaterial = new StandardMaterial3D
        {
            ResourceName = "BuddyTorsoAccentPlateMaterial",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        TorsoAccentPlate = new MeshInstance3D
        {
            Name = "TorsoAccentPlate",
            Mesh = new QuadMesh
            {
                Size = new Vector2(AccentPlateWorldSize, AccentPlateWorldSize),
            },
            Position = new Vector3(
                0.0f,
                0.0f,
                radius + _trustedProfile.FaceDepthEpsilon),
            MaterialOverride = _accentPlateMaterial,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            Visible = false,
        };
        socket.AddChild(TorsoAccentPlate);
    }

    private void UpdateConnectors(in BuddyVisualPoseFrame frame)
    {
        for (int index = 0; index < _connectorMeshes.Length; index++)
        {
            ConnectorVisualDefinition definition = _connectorDefinitions[index];
            int a = (int)definition.PartA;
            int b = (int)definition.PartB;
            Vector2 aPosition = frame.Part(definition.PartA).Rendered.Position;
            Vector2 bPosition = frame.Part(definition.PartB).Rendered.Position;
            Vector2 offset = bPosition - aPosition;
            float separation = offset.Length();
            float surfaceGap = separation - _meshRadii[a] - _meshRadii[b];
            bool minimumLengthClamped = surfaceGap < _trustedProfile.ConnectorMinimumLength;
            float length = Mathf.Max(_trustedProfile.ConnectorMinimumLength, surfaceGap);
            Vector2 center = (aPosition + bPosition) * 0.5f;

            if (separation > Mathf.Epsilon)
            {
                Vector2 direction = offset / separation;
                Vector3 mappedOffset = WorldPlaneMapping.To3D(offset);
                _connectorAngles[index] =
                    Mathf.Atan2(mappedOffset.Y, mappedOffset.X) - Mathf.Pi * 0.5f;
                if (!minimumLengthClamped)
                {
                    center = aPosition + direction * (_meshRadii[a] + length * 0.5f);
                }
            }

            MeshInstance3D connector = _connectorMeshes[index];
            connector.GlobalPosition = ResolveLanePosition(
                center,
                definition.DepthOffset,
                frame.BodyYawRadians,
                frame.Torso.Rendered.Position);
            connector.GlobalRotation = new Vector3(
                0.0f,
                frame.BodyYawRadians,
                _connectorAngles[index]);
            connector.Scale = new Vector3(
                1.0f,
                length / _connectorAuthoringLengths[index],
                1.0f);
        }
    }

    private void UpdateFace(in BuddyVisualPoseFrame frame)
    {
        LastFaceState = frame.FaceState;
        if (_faceCompositor is not null)
        {
            if (_faceCompositor.IsInitialized &&
                _facePlateMaterial is { AlbedoTexture: null } &&
                _faceCompositor.OutputTexture is { } texture)
            {
                _facePlateMaterial.AlbedoTexture = texture;
            }

            return;
        }

        if (_displayedFace != frame.FallbackFace)
        {
            _displayedFace = frame.FallbackFace;
            FaceLabel.Text = frame.FallbackFace;
        }

        FaceLabel.GlobalRotation = new Vector3(0.0f, 0.0f, frame.FallbackFaceRotation);
    }

    private PrimitiveMesh CreatePartMesh(BuddyPartId partId, float radius)
    {
        if (partId == BuddyPartId.Torso)
        {
            return new CapsuleMesh
            {
                Radius = radius,
                Height = radius * _trustedProfile.CapsuleHeightScale,
            };
        }

        return new SphereMesh
        {
            Radius = radius,
            Height = radius * 2.0f,
        };
    }

    private static Vector3 ResolveLanePosition(
        Vector2 worldPose2D,
        float depthOffset,
        float yawRadians,
        Vector2 torsoPosition)
    {
        Vector3 pose = WorldPlaneMapping.To3D(worldPose2D);
        if (!Mathf.IsZeroApprox(yawRadians))
        {
            Vector3 pivot = WorldPlaneMapping.To3D(torsoPosition);
            pose = pivot + new Basis(Vector3.Up, yawRadians) * (pose - pivot);
        }

        pose.Z += depthOffset;
        return pose;
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

    private void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("BuddyVisualRigView used before initialization.");
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
