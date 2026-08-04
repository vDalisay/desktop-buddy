using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    private CharacterFeatureRendererRegistry? _characterRendererRegistry;
    private ParametricFaceCompositor? _characterFaceCompositor;
    private BodyAccentCompositor? _characterAccentCompositor;
    private FaceRenderState? _previewFaceState;
    private bool _stitchedDollSurfaceInitialized;

    public FaceRenderKey? LastCharacterFaceRenderKey => _characterFaceCompositor?.LastRenderKey;
    public AccentRenderKey? LastCharacterAccentRenderKey => _characterAccentCompositor?.LastRenderKey;
    public long CharacterFaceRenderCount => _characterFaceCompositor?.RenderCount ?? 0;
    public long CharacterAccentRenderCount => _characterAccentCompositor?.RenderCount ?? 0;

    public override void _Process(double delta)
    {
        if (!IsInitialized)
            return;

        EnsureStitchedDollSurface();
        RefreshCharacterCompositors();
    }

    /// <summary>
    /// Preview-only semantic input. Runtime state continues to arrive through
    /// BuddyVisualPoseFrame; this override contains no gameplay authority.
    /// </summary>
    public void SetPreviewFaceState(in FaceRenderState state)
    {
        _previewFaceState = state;
        RefreshCharacterCompositors();
    }

    public void ClearPreviewFaceState()
    {
        _previewFaceState = null;
        RefreshCharacterCompositors();
    }

    /// <summary>
    /// Synchronizes the active compiled appearance and semantic state into the exact-key
    /// compositors. Safe to call after an appearance swap; equal keys allocate no viewport
    /// repaint and increment no render counter.
    /// </summary>
    public void RefreshCharacterCompositors()
    {
        if (!IsInitialized)
            return;

        EnsureStitchedDollSurface();
        EnsureCharacterCompositors();
        CompiledCharacterAppearance appearance = _activeAppearance ?? BuiltInCharacterAppearance.Value;
        FaceRenderState state = _previewFaceState ?? LastFaceState ??
            BuiltInCharacterAppearance.NeutralFaceState;

        _characterFaceCompositor!.SetAppearance(appearance);
        _characterFaceCompositor.SetState(state);
        _characterAccentCompositor!.SetAppearance(appearance.TorsoAccent);
        BindCharacterCompositorTextures();
    }

    /// <summary>
    /// Adds one transparent seam shell to each trusted part mesh. It is created once after
    /// initialization, shares the trusted geometry, renders above the player-paint shell, and
    /// never participates in physics, hit mapping, persistence, or appearance compilation.
    /// </summary>
    private void EnsureStitchedDollSurface()
    {
        if (_stitchedDollSurfaceInitialized)
            return;

        _stitchedDollSurfaceInitialized = true;
        if (_trustedProfile.Look.SeamTexture is null)
            return;

        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            var seamLayer = new MeshInstance3D
            {
                Name = "Stitches",
                Mesh = _partMeshes[index].Mesh,
                MaterialOverride = _materials.CreateSeamMaterial(),
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            _sockets[index].AddChild(seamLayer);
        }
    }

    private void EnsureCharacterCompositors()
    {
        if (_characterFaceCompositor is not null)
            return;

        _characterRendererRegistry = new CharacterFeatureRendererRegistry();
        Color outline = _trustedProfile.Look.OutlineColor;
        _characterFaceCompositor = new ParametricFaceCompositor(
            _characterRendererRegistry,
            outline);
        _characterAccentCompositor = new BodyAccentCompositor(
            _characterRendererRegistry,
            outline);
        _characterFaceCompositor.Initialize(this);
        _characterAccentCompositor.Initialize(this);
        EnsureCharacterFacePlate();
    }

    private void EnsureCharacterFacePlate()
    {
        if (GodotObject.IsInstanceValid(FaceLabel))
            FaceLabel.Visible = false;

        if (GodotObject.IsInstanceValid(FacePlate))
            return;

        _facePlateMaterial = new StandardMaterial3D
        {
            ResourceName = "ParametricBuddyFacePlateMaterial",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        FacePlate = new MeshInstance3D
        {
            Name = "FacePlate",
            Mesh = new QuadMesh
            {
                Size = new Vector2(
                    ParametricFaceCompositor.PlateWorldSize,
                    ParametricFaceCompositor.PlateWorldSize),
            },
            Position = new Vector3(
                0.0f,
                0.0f,
                PartMeshRadius(BuddyPartId.Head) + _trustedProfile.FaceDepthEpsilon),
            MaterialOverride = _facePlateMaterial,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        GetPartSocket(BuddyPartId.Head).AddChild(FacePlate);
    }

    private void BindCharacterCompositorTextures()
    {
        if (_facePlateMaterial is not null &&
            _characterFaceCompositor?.OutputTexture is { } faceTexture &&
            !ReferenceEquals(_facePlateMaterial.AlbedoTexture, faceTexture))
        {
            _facePlateMaterial.AlbedoTexture = faceTexture;
        }

        if (_accentPlateMaterial is not null &&
            _characterAccentCompositor?.OutputTexture is { } accentTexture &&
            !ReferenceEquals(_accentPlateMaterial.AlbedoTexture, accentTexture))
        {
            _accentPlateMaterial.AlbedoTexture = accentTexture;
        }

        if (GodotObject.IsInstanceValid(TorsoAccentPlate) &&
            _characterAccentCompositor is not null)
        {
            TorsoAccentPlate.Visible = _characterAccentCompositor.HasVisibleAccent;
        }
    }
}
