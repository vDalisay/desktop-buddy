using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Sharing;

/// <summary>Captures local, trusted Workshop previews on Godot's render thread.</summary>
public partial class WorkshopPreviewCapture : Node
{
    private const int MaximumRoomPreviewDimension = 800;
    private const int MaximumRoomPreviewPixels = 500_000;
    private const int BuddyPreviewWidth = 1920;
    private const int BuddyPreviewHeight = 1080;
    private const float BuddyFrameFill = 0.80f;
    private const float MinimumBuddyCameraSize = 32.0f;
    private readonly CharacterPaintStore _paintStore;
    private readonly CharacterFeatureCatalog _featureCatalog;
    private readonly BuddyRoot _buddy;
    private readonly BuddyVisualPresenter _presenter;

    public WorkshopPreviewCapture(
        CharacterStore characters,
        BuddyRoot buddy,
        BuddyVisualPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(characters);
        _paintStore = characters.CreatePaintStore();
        _featureCatalog = characters.FeatureCatalog;
        _buddy = buddy ?? throw new ArgumentNullException(nameof(buddy));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        ProcessMode = ProcessModeEnum.Always;
    }

    public async Task<byte[]> CaptureRoomAsync(CancellationToken token)
    {
        EnsureReady();
        bool buddyVisible = _buddy.Visible;
        bool presenterVisible = _presenter.Visible;
        _buddy.Visible = false;
        _presenter.Visible = false;
        try
        {
            await WaitForRenderAsync(token);
            Image image = GetTree().Root.GetTexture().GetImage();
            if (image.IsEmpty()) throw new InvalidOperationException("The Play window did not produce a Workshop preview.");
            ResizeToFit(image, MaximumRoomPreviewDimension, MaximumRoomPreviewPixels);
            return image.SavePngToBuffer();
        }
        finally
        {
            _buddy.Visible = buddyVisible;
            _presenter.Visible = presenterVisible;
        }
    }

    public async Task<byte[]> CaptureBuddyAsync(Guid characterId, CancellationToken token)
    {
        EnsureReady();
        CharacterPaintLoadResult loaded = await _paintStore.LoadAsync(characterId, token);
        if (!loaded.IsSuccess || loaded.Character.Document is null)
            throw new InvalidOperationException(loaded.Detail ?? loaded.Character.Detail ?? "The active buddy could not be loaded for its preview.");
        CharacterCompileResult compiled = CharacterCompiler.Compile(loaded.Character.Document, _featureCatalog);
        if (!compiled.IsSuccess || compiled.Appearance is null)
            throw new InvalidOperationException(string.Join("; ", compiled.Errors));

        var viewport = new SubViewport
        {
            Name = "WorkshopBuddyPreviewViewport",
            Size = new Vector2I(BuddyPreviewWidth, BuddyPreviewHeight),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = true,
        };
        AddChild(viewport);
        RuntimePaintTextureBridge? paint = null;
        try
        {
            var world = new Node3D { ProcessMode = ProcessModeEnum.Always };
            viewport.AddChild(world);
            var source = new StaticBuddyVisualTransformSource(_buddy.Rig.Profile, Vector2.Zero);
            var rig = new BuddyVisualRigView
            {
                Name = "WorkshopBuddyPreviewRig",
                ProcessMode = ProcessModeEnum.Always,
            };
            rig.Initialize(_buddy.VisualProfile, source);
            world.AddChild(rig);
            rig.ApplyAppearance(compiled.Appearance);
            paint = new RuntimePaintTextureBridge(rig);
            paint.Apply(loaded.Surfaces);
            rig.ApplyCanonicalPreviewPose();

            Camera3D camera = CreateFramedBuddyCamera(rig);
            world.AddChild(camera);
            world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, -20, 0) });

            await WaitForRenderAsync(token);
            Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty()) throw new InvalidOperationException("The active buddy did not produce a Workshop preview.");
            if (image.GetWidth() != BuddyPreviewWidth || image.GetHeight() != BuddyPreviewHeight)
                image.Resize(BuddyPreviewWidth, BuddyPreviewHeight, Image.Interpolation.Lanczos);
            return image.SavePngToBuffer();
        }
        finally
        {
            paint?.Dispose();
            viewport.QueueFree();
        }
    }

    private static Camera3D CreateFramedBuddyCamera(BuddyVisualRigView rig)
    {
        if (!TryGetVisibleMeshBounds(rig, out Vector2 minimum, out Vector2 maximum))
        {
            return new Camera3D
            {
                Position = new Vector3(0, 0, 600),
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size = 180,
                Current = true,
            };
        }

        Vector2 size = maximum - minimum;
        Vector2 center = (minimum + maximum) * 0.5f;
        float aspect = BuddyPreviewWidth / (float)BuddyPreviewHeight;
        float requiredVerticalSpan = MathF.Max(size.Y, size.X / aspect);
        float cameraSize = MathF.Max(MinimumBuddyCameraSize, requiredVerticalSpan / BuddyFrameFill);
        return new Camera3D
        {
            Position = new Vector3(center.X, center.Y, 600),
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = cameraSize,
            Current = true,
        };
    }

    private static bool TryGetVisibleMeshBounds(Node root, out Vector2 minimum, out Vector2 maximum)
    {
        minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        bool found = false;
        var pending = new Stack<Node>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            Node node = pending.Pop();
            if (node is MeshInstance3D mesh && mesh.Visible && mesh.Mesh is not null)
            {
                Aabb localBounds = mesh.GetAabb();
                Vector3 start = localBounds.Position;
                Vector3 end = localBounds.End;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = new(
                        (corner & 1) == 0 ? start.X : end.X,
                        (corner & 2) == 0 ? start.Y : end.Y,
                        (corner & 4) == 0 ? start.Z : end.Z);
                    Vector3 world = mesh.GlobalTransform * local;
                    minimum.X = MathF.Min(minimum.X, world.X);
                    minimum.Y = MathF.Min(minimum.Y, world.Y);
                    maximum.X = MathF.Max(maximum.X, world.X);
                    maximum.Y = MathF.Max(maximum.Y, world.Y);
                    found = true;
                }
            }

            foreach (Node child in node.GetChildren())
                pending.Push(child);
        }

        return found && maximum.X > minimum.X && maximum.Y > minimum.Y;
    }

    private async Task WaitForRenderAsync(CancellationToken token)
    {
        for (int frame = 0; frame < 3; frame++)
        {
            token.ThrowIfCancellationRequested();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        token.ThrowIfCancellationRequested();
    }

    private void EnsureReady()
    {
        if (!IsInsideTree() || !GodotObject.IsInstanceValid(_buddy) ||
            !GodotObject.IsInstanceValid(_presenter))
            throw new InvalidOperationException("The Play window is not ready for a Workshop preview.");
    }

    private static void ResizeToFit(Image image, int maximumDimension, int maximumPixels)
    {
        int width = image.GetWidth();
        int height = image.GetHeight();
        int largest = Math.Max(width, height);
        long pixels = (long)width * height;
        if (largest <= maximumDimension && pixels <= maximumPixels) return;
        float scale = Math.Min(
            maximumDimension / (float)largest,
            MathF.Sqrt(maximumPixels / (float)pixels));
        image.Resize(
            Math.Max(1, Mathf.RoundToInt(width * scale)),
            Math.Max(1, Mathf.RoundToInt(height * scale)),
            Image.Interpolation.Lanczos);
    }
}
