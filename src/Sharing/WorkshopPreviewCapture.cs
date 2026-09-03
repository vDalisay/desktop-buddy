using System;
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
            Size = new Vector2I(420, 360),
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
            world.AddChild(new Camera3D
            {
                Position = new Vector3(0, 0, 600),
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size = 400,
                Current = true,
            });
            world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, -20, 0) });

            await WaitForRenderAsync(token);
            Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty()) throw new InvalidOperationException("The active buddy did not produce a Workshop preview.");
            return image.SavePngToBuffer();
        }
        finally
        {
            paint?.Dispose();
            viewport.QueueFree();
        }
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
