using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Environment;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.Sharing;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Rendered regression gate for both locally generated Workshop preview images.</summary>
public sealed class WorkshopPreviewCaptureScenario : IScenario
{
    public string Id => "workshop_preview_capture";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (App.BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, Id);
        try
        {
            Guid id = Guid.Parse("92000000-0000-4000-8000-000000000001");
            CharacterDocument character = CharacterSelectionScenarioSupport.Character(
                id,
                "Workshop Preview Buddy",
                "#27A8F2");
            CharacterSaveResult saved = await store.SaveAsync(character, CancellationToken.None);
            var capture = new WorkshopPreviewCapture(store, lab.Buddy, lab.VisualPresenter)
            {
                Name = nameof(WorkshopPreviewCapture),
            };
            lab.AddChild(capture);

            byte[] buddyPng = await capture.CaptureBuddyAsync(id, CancellationToken.None);
            Image buddy = Decode(buddyPng);
            bool buddyOk = saved.IsSuccess && buddy.GetSize() == new Vector2I(420, 360) &&
                SampleDistinctColors(buddy) >= 3;
            checks.Add(new StartupCheck(
                "workshop_buddy_preview_is_rendered_front_view",
                buddyOk,
                $"saved={saved.Status} size={buddy.GetSize()} bytes={buddyPng.Length}"));

            bool buddyWasVisible = lab.Buddy.Visible;
            bool presenterWasVisible = lab.VisualPresenter.Visible;
            byte[] roomPng = await capture.CaptureRoomAsync(CancellationToken.None);
            Image room = Decode(roomPng);
            bool roomOk = room.GetWidth() <= 800 && room.GetHeight() <= 800 &&
                lab.Buddy.Visible == buddyWasVisible && lab.VisualPresenter.Visible == presenterWasVisible;
            checks.Add(new StartupCheck(
                "workshop_room_preview_captures_and_restores_buddy_visibility",
                roomOk,
                $"size={room.GetSize()} bytes={roomPng.Length} restored={lab.VisualPresenter.Visible}"));

            string sharingRoot = Path.Combine(root, "sharing");
            var staging = new WorkshopStagingStore(sharingRoot);
            var rooms = new RoomPaintingLibraryStore(Path.Combine(root, "rooms"));
            var sharing = new WorkshopSharingCoordinator(
                new DirectoryWorkshopTransport(Path.Combine(root, "emulator"), firstId: 9200),
                staging,
                new RoomShareExporter(staging, "scenario"),
                new RoomShareImporter(staging, rooms),
                new CharacterShareExporter(staging, store, "scenario"),
                new CharacterShareImporter(staging, store));
            var panel = new WorkshopPanel { Name = nameof(WorkshopPanel) };
            panel.Configure(sharing, rooms, new RoomHost(), new CharacterSelectionState(id), capture);
            tree.Root.AddChild(panel);
            panel.Open();
            Button publish = panel.FindChildren("*", nameof(Button), true, false)
                .OfType<Button>()
                .Single(button => button.Text == "Publish Active Buddy");
            publish.EmitSignal(Button.SignalName.Pressed);
            Control success = panel.FindChild("WorkshopPublishSuccessDialog", true, false) as Control
                ?? throw new InvalidOperationException("Workshop success confirmation was not composed.");
            for (int frame = 0; frame < 300 && !success.Visible; frame++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Button open = panel.FindChildren("*", nameof(Button), true, false)
                .OfType<Button>()
                .Single(button => button.Text == "Open Workshop Page...");
            bool popupOk = success.Visible && !open.Disabled;
            byte[] dialogPng = panel.GetTexture().GetImage().SavePngToBuffer();
            open.EmitSignal(Button.SignalName.Pressed);
            checks.Add(new StartupCheck(
                "workshop_publish_success_offers_item_page",
                popupOk && !success.Visible,
                $"shown={popupOk} dismissed={!success.Visible}"));
            panel.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            SaveArtifacts(buddyPng, roomPng, dialogPng);
        }
        finally
        {
            CharacterSelectionScenarioSupport.Cleanup(lab, root);
        }

        return CharacterSelectionScenarioSupport.Result(checks, seed);
    }

    private static Image Decode(byte[] png)
    {
        var image = new Image();
        Error loaded = image.LoadPngFromBuffer(png);
        if (loaded != Error.Ok || image.IsEmpty())
            throw new InvalidDataException($"Generated Workshop preview is not a valid PNG ({loaded}).");
        return image;
    }

    private static int SampleDistinctColors(Image image)
    {
        var colors = new HashSet<Color>();
        for (int y = 0; y < image.GetHeight(); y += 12)
        for (int x = 0; x < image.GetWidth(); x += 12)
            colors.Add(image.GetPixel(x, y));
        return colors.Count;
    }

    private static void SaveArtifacts(byte[] buddyPng, byte[] roomPng, byte[] dialogPng)
    {
        if (string.IsNullOrWhiteSpace(ScenarioArtifacts.Directory)) return;
        string directory = Path.GetFullPath(ScenarioArtifacts.Directory);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "workshop_buddy_preview.png"), buddyPng);
        File.WriteAllBytes(Path.Combine(directory, "workshop_room_preview.png"), roomPng);
        File.WriteAllBytes(Path.Combine(directory, "workshop_publish_success.png"), dialogPng);
    }

    private sealed class RoomHost : IRoomPaintingSharingHost
    {
        public byte[] SnapshotRoomPaintingForSharing() => new byte[EnvironmentCanvasPolicy.Bytes];

        public Task<bool> ApplySharedRoomPaintingAsync(
            ReadOnlyMemory<byte> pixels,
            CancellationToken token = default) => Task.FromResult(false);
    }
}
