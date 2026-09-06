using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Shared physics-free offscreen Buddy preview surface used by Work Mode, Character Editor,
/// Workshop capture, and the tutorial portrait. The surface owns the isolated World3D, static
/// transform source, visual rig, orthographic camera and light; it never creates gameplay bodies,
/// solvers, autonomy, reactions or clocks.
///
/// <para>Continuous consumers bind a visible CanvasItem. Rendering is disabled while that owner is
/// hidden, rather than leaving a permanent UpdateMode.Always viewport alive in a desktop app.
/// Capture-only consumers leave the owner null and call <see cref="RequestSingleFrame"/>.</para>
/// </summary>
public sealed partial class BuddyPreviewSurface : SubViewport
{
    private CanvasItem? _visibilityOwner;
    private bool _continuous;
    private bool _configured;
    private bool _lastVisible;

    public Node3D WorldRoot { get; private set; } = null!;
    public StaticBuddyVisualTransformSource Source { get; private set; } = null!;
    public BuddyVisualRigView Rig { get; private set; } = null!;
    public Camera3D Camera { get; private set; } = null!;
    public DirectionalLight3D Light { get; private set; } = null!;

    /// <summary>
    /// Composes the complete offscreen preview stack. Existing editor rigs may be supplied so
    /// sessions holding a reference to that rig continue to do so after migration.
    /// </summary>
    public void Configure(
        string rigName,
        Vector2I viewportSize,
        bool transparentBackground,
        PuppetRigProfile rigProfile,
        BuddyVisualProfile visualProfile,
        float cameraSize,
        Vector3 cameraPosition,
        Vector3 lightRotationDegrees,
        float lightEnergy = 1.0f,
        Vector2? sourceOrigin = null,
        string face = ":|",
        CanvasItem? visibilityOwner = null,
        BuddyVisualRigView? existingRig = null,
        StaticBuddyVisualTransformSource? existingSource = null)
    {
        if (_configured || IsInsideTree())
            throw new InvalidOperationException("BuddyPreviewSurface must be configured exactly once before entering the tree.");
        if (viewportSize.X <= 0 || viewportSize.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportSize));
        if (!GodotObject.IsInstanceValid(rigProfile))
            throw new ArgumentNullException(nameof(rigProfile));
        if (!GodotObject.IsInstanceValid(visualProfile))
            throw new ArgumentNullException(nameof(visualProfile));
        if (!float.IsFinite(cameraSize) || cameraSize <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(cameraSize));

        if (string.IsNullOrWhiteSpace(Name.ToString()))
            Name = "BuddyPreviewSurface";
        Size = viewportSize;
        TransparentBg = transparentBackground;
        OwnWorld3D = true;
        RenderTargetUpdateMode = UpdateMode.Disabled;
        ProcessMode = ProcessModeEnum.Always;

        Source = existingSource ?? new StaticBuddyVisualTransformSource(
            rigProfile,
            sourceOrigin ?? Vector2.Zero,
            face);
        Rig = existingRig ?? new BuddyVisualRigView
        {
            Name = rigName,
            ProcessMode = ProcessModeEnum.Always,
        };
        if (existingRig is null)
            Rig.Initialize(visualProfile, Source);

        WorldRoot = new Node3D
        {
            Name = "PreviewWorld",
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(WorldRoot);
        WorldRoot.AddChild(Rig);

        Camera = new Camera3D
        {
            Name = "PreviewCamera",
            Position = cameraPosition,
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = cameraSize,
            Current = true,
        };
        WorldRoot.AddChild(Camera);

        Light = new DirectionalLight3D
        {
            Name = "PreviewLight",
            RotationDegrees = lightRotationDegrees,
            LightEnergy = lightEnergy,
        };
        WorldRoot.AddChild(Light);

        _visibilityOwner = visibilityOwner;
        _continuous = GodotObject.IsInstanceValid(visibilityOwner);
        _configured = true;
        SetProcess(_continuous);
    }

    public override void _Ready()
    {
        if (!_configured)
            throw new InvalidOperationException("BuddyPreviewSurface entered the tree before Configure.");

        // Capture-only surfaces must stay idle until their caller explicitly requests the frame.
        // Continuous surfaces, on the other hand, follow their visible container immediately.
        if (_continuous)
            SyncRenderMode(force: true);
        else
            RenderTargetUpdateMode = UpdateMode.Disabled;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_continuous)
            SyncRenderMode(force: false);
    }

    /// <summary>Changes orthographic framing without rebuilding the preview world.</summary>
    public void SetCameraFrame(Vector3 position, float size)
    {
        if (!GodotObject.IsInstanceValid(Camera))
            return;
        if (!float.IsFinite(size) || size <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(size));
        Camera.Position = position;
        Camera.Size = size;
        RequestSingleFrame();
    }

    /// <summary>
    /// Requests presentation refresh without breaking the lifecycle policy. Capture-only surfaces
    /// render exactly once. Continuous surfaces remain Always while visible and Disabled while
    /// hidden; they never get stranded in Once by an appearance/camera refresh.
    /// </summary>
    public void RequestSingleFrame()
    {
        if (_continuous)
        {
            SyncRenderMode(force: true);
            return;
        }

        RenderTargetUpdateMode = UpdateMode.Once;
    }

    /// <summary>
    /// Copies the current appearance and painted underlays from a trusted live visual rig. Work
    /// and the later tutorial portrait use this to match the player's current Buddy without
    /// sharing any gameplay authority.
    /// </summary>
    public void CopyPresentationFrom(
        BuddyVisualRigView live,
        CompiledCharacterAppearance? appearanceOverride = null)
    {
        ArgumentNullException.ThrowIfNull(live);
        CompiledCharacterAppearance? appearance = appearanceOverride ?? live.ActiveAppearance;
        if (appearance is not null)
            Rig.ApplyAppearance(appearance);
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyPartId part = (BuddyPartId)index;
            Rig.SetSurfaceUnderlay(part, live.SurfaceUnderlay(part));
        }
        RequestSingleFrame();
    }

    private void SyncRenderMode(bool force)
    {
        bool visible = GodotObject.IsInstanceValid(_visibilityOwner) && _visibilityOwner!.IsVisibleInTree();
        if (!force && visible == _lastVisible)
            return;
        _lastVisible = visible;
        RenderTargetUpdateMode = visible ? UpdateMode.Always : UpdateMode.Disabled;
    }
}
