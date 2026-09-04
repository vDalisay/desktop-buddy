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

/// <summary>Rendered regression gate for both locally generated Workshop preview images and Workshop window UX.</summary>
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
            Rect2I foreground = ForegroundBounds(buddy);
            bool buddyFramed = foreground.Size.Y >= 700 &&
                foreground.Size.X >= 280 &&
                foreground.Position.X >= 20 &&
                foreground.Position.Y >= 20 &&
                foreground.Position.X + foreground.Size.X <= buddy.GetWidth() - 20 &&
                foreground.Position.Y + foreground.Size.Y <= buddy.GetHeight() - 20;
            bool buddyOk = saved.IsSuccess && buddy.GetSize() == new Vector2I(1920, 1080) &&
                SampleDistinctColors(buddy) >= 3 && buddyFramed;
            checks.Add(new StartupCheck(
                "workshop_buddy_preview_is_framed_full_hd_front_view",
                buddyOk,
                $"saved={saved.Status} size={buddy.GetSize()} foreground={foreground} bytes={buddyPng.Length}"));

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
            var transport = new DirectoryWorkshopTransport(Path.Combine(root, "emulator"), firstId: 9200);
            var sharing = new WorkshopSharingCoordinator(
                transport,
                staging,
                new RoomShareExporter(staging, "scenario"),
                new RoomShareImporter(staging, rooms),
                new CharacterShareExporter(staging, store, "scenario"),
                new CharacterShareImporter(staging, store));
            var panel = new WorkshopPanel { Name = nameof(WorkshopPanel) };
            panel.Configure(sharing, rooms, new RoomHost(), new CharacterSelectionState(id), capture);
            tree.Root.AddChild(panel);
            panel.Open();

            bool win98Chrome = panel.Borderless &&
                panel.FindChild("TitleBar", true, false) is PanelContainer &&
                panel.FindChild("CloseBox", true, false) is Button &&
                panel.FindChild("Win98StatusBar", true, false) is PanelContainer;
            checks.Add(new StartupCheck(
                "workshop_window_uses_owned_win98_chrome",
                win98Chrome,
                $"borderless={panel.Borderless} title={panel.FindChild("TitleBar", true, false) is not null} status={panel.FindChild("Win98StatusBar", true, false) is not null}"));

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

            Button refresh = panel.FindChildren("*", nameof(Button), true, false)
                .OfType<Button>()
                .Single(button => button.Text == "Refresh Subscriptions");
            for (int frame = 0; frame < 30 && refresh.Disabled; frame++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            refresh.EmitSignal(Button.SignalName.Pressed);

            Button? unsubscribe = null;
            for (int frame = 0; frame < 180 && unsubscribe is null; frame++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                unsubscribe = panel.FindChildren("*", nameof(Button), true, false)
                    .OfType<Button>()
                    .FirstOrDefault(button => button.Text == "Unsubscribe");
            }
            bool unsubscribeComposed = unsubscribe is not null &&
                unsubscribe.TooltipText.Contains("Imported local copies are kept", StringComparison.Ordinal);
            if (unsubscribe is not null)
            {
                unsubscribe.EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 180 && panel.FindChild("Unsubscribe9200", true, false) is not null; frame++)
                    await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            WorkshopSubscriptionQueryResult remaining =
                await transport.GetSubscribedItemsAsync(CancellationToken.None);
            bool unsubscribeOk = unsubscribeComposed && remaining.IsSuccess &&
                remaining.Items.All(item => item.PublishedFileId != 9200UL);
            checks.Add(new StartupCheck(
                "workshop_subscription_row_can_unsubscribe_without_touching_local_content",
                unsubscribeOk,
                $"button={unsubscribeComposed} remaining={remaining.Items.Count} localCharacter={store.Paths.Directory(id)}"));

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

    private static Rect2I ForegroundBounds(Image image)
    {
        int width = image.GetWidth();
        int height = image.GetHeight();
        Color background = image.GetPixel(0, 0);
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color pixel = image.GetPixel(x, y);
            float difference = MathF.Abs(pixel.R - background.R) +
                MathF.Abs(pixel.G - background.G) +
                MathF.Abs(pixel.B - background.B) +
                MathF.Abs(pixel.A - background.A);
            if (difference < 0.08f) continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return maxX < minX || maxY < minY
            ? new Rect2I()
            : new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
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
