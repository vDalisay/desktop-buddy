using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// Owns room wall geometry and the world camera. Resize/zoom requests are
/// queued and applied only from the owning root's fixed-tick route. As an
/// <see cref="IImpactSource"/>, hard wall/floor impacts above the calibrated
/// threshold enter the pain pipeline attributed to the room boundary
/// (RAGDOLL §7.1); all four walls are one body and share one episode source.
/// </summary>
[GlobalClass]
public partial class BoundaryController : StaticBody2D, IImpactSource
{
    [Export] public BoundaryProfile Profile { get; set; } = null!;
    [Export] public CollisionShape2D Floor { get; set; } = null!;
    [Export] public CollisionShape2D Ceiling { get; set; } = null!;
    [Export] public CollisionShape2D LeftWall { get; set; } = null!;
    [Export] public CollisionShape2D RightWall { get; set; } = null!;
    [Export] public Camera2D WorldCamera { get; set; } = null!;

    // Optional 3D presentation camera (M3.5). Null-safe: scenes without a 3D camera
    // (e.g. dual_profile_lab.tscn) stay valid. Driven in lockstep with WorldCamera.
    [Export] public Camera3D? WorldCamera3D { get; set; }

    // View-plumbing constant: the orthographic camera's Z distance is provably invisible
    // to the orthographic result (only near/far clipping depends on it), so it is a code
    // constant rather than a visual-profile value.
    private const float CameraDistance = 500f;

    private RoomLayout _pendingLayout;
    private bool _hasPendingLayout;

    public event Action<RoomLayout, Rect2>? LayoutApplied;

    public RoomLayout CurrentLayout { get; private set; }
    public Rect2 InnerBounds { get; private set; }
    public int AppliedLayoutCount { get; private set; }
    public bool IsInitialized { get; private set; }

    public int InteractionId { get; } = InteractionIds.Next();

    public string ContentId => ContentIds.RoomBoundary;

    public void Initialize(Vector2I clientSize, double storedZoom)
    {
        if (IsInitialized)
        {
            return;
        }

        ValidateDependencies();
        RoomLayout initialLayout = RoomLayoutPolicy.Resolve(clientSize.X, clientSize.Y, storedZoom);
        CollisionLayer = CollisionLayers.RoomBounds;
        CollisionMask = CollisionLayers.MaskRoomBounds;
        IsInitialized = true;
        ApplyLayout(initialLayout);
    }

    public void RequestLayout(Vector2I clientSize, double storedZoom)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("BoundaryController must be initialized before requesting layout.");
        }

        _pendingLayout = RoomLayoutPolicy.Resolve(clientSize.X, clientSize.Y, storedZoom);
        _hasPendingLayout = true;
    }

    public void PhysicsTick()
    {
        if (!_hasPendingLayout)
        {
            return;
        }

        RoomLayout layout = _pendingLayout;
        _hasPendingLayout = false;
        ApplyLayout(layout);
    }

    private void ValidateDependencies()
    {
        if (!GodotObject.IsInstanceValid(Profile))
        {
            throw new InvalidOperationException("BoundaryController requires an injected profile.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid boundary profile: {string.Join("; ", errors)}");
        }

        ValidateRectangle(Floor, nameof(Floor));
        ValidateRectangle(Ceiling, nameof(Ceiling));
        ValidateRectangle(LeftWall, nameof(LeftWall));
        ValidateRectangle(RightWall, nameof(RightWall));
        if (!GodotObject.IsInstanceValid(WorldCamera))
        {
            throw new InvalidOperationException("BoundaryController requires an injected world camera.");
        }
    }

    private static void ValidateRectangle(CollisionShape2D shape, string name)
    {
        if (!GodotObject.IsInstanceValid(shape) || shape.Shape is not RectangleShape2D)
        {
            throw new InvalidOperationException($"BoundaryController {name} requires a RectangleShape2D.");
        }
    }

    private void ApplyLayout(RoomLayout layout)
    {
        float width = (float)layout.RoomWidth;
        float height = (float)layout.RoomHeight;
        float thickness = Profile.WallThickness;

        ConfigureHorizontal(Floor, width, thickness, new Vector2(width * 0.5f, height - thickness * 0.5f));
        ConfigureHorizontal(Ceiling, width, thickness, new Vector2(width * 0.5f, thickness * 0.5f));
        ConfigureVertical(LeftWall, height, thickness, new Vector2(thickness * 0.5f, height * 0.5f));
        ConfigureVertical(RightWall, height, thickness, new Vector2(width - thickness * 0.5f, height * 0.5f));

        WorldCamera.Position = new Vector2(width * 0.5f, height * 0.5f);
        WorldCamera.Zoom = Vector2.One * (float)layout.EffectiveZoom;

        if (GodotObject.IsInstanceValid(WorldCamera3D))
        {
            // Match the Camera2D framing through WorldPlaneMapping: (x, y) -> (x, -y, 0),
            // camera at (W/2, -H/2, +CameraDistance) looking -Z, vertical extent = RoomHeight.
            WorldCamera3D!.Projection = Camera3D.ProjectionType.Orthogonal;
            WorldCamera3D.KeepAspect = Camera3D.KeepAspectEnum.Height;
            WorldCamera3D.Size = height;
            WorldCamera3D.Position = new Vector3(width * 0.5f, -height * 0.5f, CameraDistance);
            WorldCamera3D.Rotation = Vector3.Zero; // identity basis looks down -Z
            // Global constraint 6: presenter-driven 3D nodes and this camera opt out of
            // engine interpolation so a queued layout change snaps exactly like the 2D camera.
            WorldCamera3D.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        }

        CurrentLayout = layout;
        InnerBounds = new Rect2(
            thickness,
            thickness,
            width - thickness * 2.0f,
            height - thickness * 2.0f);
        AppliedLayoutCount++;
        LayoutApplied?.Invoke(layout, InnerBounds);
    }

    private static void ConfigureHorizontal(
        CollisionShape2D node,
        float width,
        float thickness,
        Vector2 position)
    {
        ((RectangleShape2D)node.Shape).Size = new Vector2(width, thickness);
        node.Position = position;
    }

    private static void ConfigureVertical(
        CollisionShape2D node,
        float height,
        float thickness,
        Vector2 position)
    {
        ((RectangleShape2D)node.Shape).Size = new Vector2(thickness, height);
        node.Position = position;
    }
}
