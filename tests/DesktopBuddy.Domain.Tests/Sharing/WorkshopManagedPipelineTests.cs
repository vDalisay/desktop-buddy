using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.Sharing;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sharing;

public sealed class WorkshopManagedPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "desktop-buddy-workshop-tests",
        Guid.NewGuid().ToString("N"));

    public WorkshopManagedPipelineTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Share_folder_reader_accepts_exact_hashed_room_payload()
    {
        string content = CreateRoomShare("reader-ok", [1, 2, 3, 4, 5]);

        ShareFolderReadResult result = new ShareFolderReader().Read(content, ShareContentType.RoomPainting);

        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, result.Files[ShareManifestPolicy.RoomBackgroundPath]);
    }

    [Fact]
    public void Share_folder_reader_rejects_undeclared_files()
    {
        string content = CreateRoomShare("undeclared", [1, 2, 3]);
        File.WriteAllText(Path.Combine(content, "evil.txt"), "not declared");

        ShareFolderReadResult result = new ShareFolderReader().Read(content, ShareContentType.RoomPainting);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Validation.Issues, issue =>
            issue.Code == ShareValidationCode.UnexpectedFile && issue.Path == "evil.txt");
    }

    [Fact]
    public void Share_folder_reader_rejects_hash_mismatch()
    {
        string content = CreateRoomShare("hash", [1, 2, 3]);
        File.WriteAllBytes(
            Path.Combine(content, "environment", "background.png"),
            [9, 9, 9]);

        ShareFolderReadResult result = new ShareFolderReader().Read(content, ShareContentType.RoomPainting);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Validation.Issues, issue => issue.Code == ShareValidationCode.HashMismatch);
    }

    [Fact]
    public void Incoming_snapshot_is_independent_from_mutable_source()
    {
        string source = CreateRoomShare("snapshot", [1, 2, 3]);
        var staging = new WorkshopStagingStore(Path.Combine(_root, "staging"));

        WorkshopIncomingStaging copy = staging.SnapshotIncoming(source, Guid.NewGuid());
        File.WriteAllBytes(Path.Combine(source, "environment", "background.png"), [8, 8, 8]);

        ShareFolderReadResult copied = new ShareFolderReader().Read(copy.ContentRoot, ShareContentType.RoomPainting);
        Assert.True(copied.IsSuccess);
        Assert.Equal(new byte[] { 1, 2, 3 }, copied.Files[ShareManifestPolicy.RoomBackgroundPath]);
    }

    [Fact]
    public void Cancelled_incoming_snapshot_leaves_no_owned_operation()
    {
        string source = CreateRoomShare("cancelled-snapshot", [1, 2, 3]);
        var staging = new WorkshopStagingStore(Path.Combine(_root, "cancelled-staging"));
        Guid operationId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            staging.SnapshotIncoming(source, operationId, cancellation.Token));

        Assert.False(Directory.Exists(Path.Combine(staging.IncomingRoot, operationId.ToString("D"))));
    }

    [Fact]
    public void Room_import_from_staging_never_rereads_mutated_source()
    {
        byte[] pixels = new byte[EnvironmentCanvasPolicy.Bytes];
        pixels[0] = 17;
        pixels[1] = 34;
        pixels[2] = 51;
        pixels[3] = 255;
        byte[] png = PaintPngCodec.Encode(pixels);
        string source = CreateRoomShare("staged-room-import", png);
        var staging = new WorkshopStagingStore(Path.Combine(_root, "room-import-staging"));
        var library = new RoomPaintingLibraryStore(Path.Combine(_root, "room-import-library"));
        var importer = new RoomShareImporter(staging, library, () => DateTimeOffset.UnixEpoch);
        WorkshopIncomingStaging snapshot = staging.SnapshotIncoming(source, Guid.NewGuid());

        // Simulate Steam replacing the mutable install cache after Desktop Buddy has snapshotted it.
        File.WriteAllBytes(Path.Combine(source, "environment", "background.png"), [9, 9, 9]);
        File.Delete(Path.Combine(source, ShareManifestPolicy.ManifestFileName));

        RoomShareImportResult result = importer.ImportStaged(
            snapshot,
            new WorkshopImportSource(42, 1234, "Snapshot Room", "A calm blue room."),
            CancellationToken.None);

        Assert.True(result.Success, result.Detail);
        Assert.NotNull(result.Entry);
        Assert.Equal(pixels, library.LoadPixels(result.Entry!.Id));
        Assert.Equal("A calm blue room.", Assert.Single(library.List()).Description);
        Assert.False(Directory.Exists(snapshot.OperationRoot));
    }

    [Fact]
    public async Task Directory_transport_models_create_submit_subscribe_install_and_unsubscribe()
    {
        string emulatorRoot = Path.Combine(_root, "emulator");
        string content = CreateRoomShare("transport-content", [4, 3, 2, 1]);
        var transport = new DirectoryWorkshopTransport(emulatorRoot, firstId: 7000);

        WorkshopCreateRemoteResult created = await transport.CreateItemAsync(CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(7000UL, created.PublishedFileId);

        WorkshopSubmitRemoteResult submitted = await transport.SubmitUpdateAsync(
            new WorkshopRemoteUpdate(
                created.PublishedFileId,
                "Room One",
                "Description",
                content,
                null,
                ["Room Painting"],
                "desktop-buddy:room:1"),
            progress: null,
            CancellationToken.None);
        Assert.True(submitted.IsSuccess);

        WorkshopSubscriptionQueryResult subscribed = await transport.GetSubscribedItemsAsync(CancellationToken.None);
        Assert.True(subscribed.IsSuccess);
        PublishedWorkshopItem item = Assert.Single(subscribed.Items);
        Assert.Equal("Room One", item.DisplayName);
        Assert.Equal("Description", item.Description);
        Assert.Equal(ShareContentTypes.RoomPainting, item.ContentType);
        Assert.True(item.State.HasFlag(WorkshopItemState.Installed));

        WorkshopInstalledItemResult installed = await transport.EnsureInstalledAsync(
            item.PublishedFileId,
            progress: null,
            CancellationToken.None);
        Assert.True(installed.IsSuccess);
        Assert.True(File.Exists(Path.Combine(installed.InstallFolder!, "manifest.json")));

        WorkshopSubscriptionChangeResult unsubscribed = await transport.UnsubscribeAsync(
            item.PublishedFileId,
            CancellationToken.None);
        Assert.True(unsubscribed.IsSuccess, unsubscribed.Detail);
        Assert.Equal(item.PublishedFileId, unsubscribed.PublishedFileId);

        WorkshopSubscriptionQueryResult empty = await transport.GetSubscribedItemsAsync(CancellationToken.None);
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Items);

        PublishedWorkshopItem retainedDetails = Assert.Single((await transport.GetItemDetailsAsync(
            [item.PublishedFileId],
            CancellationToken.None)).Items);
        Assert.Equal("Room One", retainedDetails.DisplayName);
        Assert.Equal("Description", retainedDetails.Description);
        Assert.False(retainedDetails.State.HasFlag(WorkshopItemState.Subscribed));

        // Unsubscribing affects Steam subscription state, not Desktop Buddy's already-imported
        // local copies or, in the emulator, the immutable remote snapshot itself.
        Assert.True(File.Exists(Path.Combine(installed.InstallFolder!, "manifest.json")));
    }

    [Fact]
    public async Task Directory_transport_reports_cancelled_subscription_query_separately_from_zero_items()
    {
        var transport = new DirectoryWorkshopTransport(Path.Combine(_root, "cancelled-query"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        WorkshopSubscriptionQueryResult result = await transport.GetSubscribedItemsAsync(cancellation.Token);

        Assert.Equal(WorkshopRemoteStatus.Cancelled, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Directory_transport_reports_cancelled_unsubscribe_without_changing_subscription()
    {
        string content = CreateRoomShare("cancelled-unsubscribe-content", [7, 6, 5, 4]);
        var transport = new DirectoryWorkshopTransport(Path.Combine(_root, "cancelled-unsubscribe"), firstId: 7100);
        WorkshopCreateRemoteResult created = await transport.CreateItemAsync(CancellationToken.None);
        Assert.True(created.IsSuccess);
        WorkshopSubmitRemoteResult submitted = await transport.SubmitUpdateAsync(
            new WorkshopRemoteUpdate(
                created.PublishedFileId,
                "Keep Me",
                string.Empty,
                content,
                null,
                ["Room Painting"],
                "desktop-buddy:room:1"),
            null,
            CancellationToken.None);
        Assert.True(submitted.IsSuccess);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        WorkshopSubscriptionChangeResult result = await transport.UnsubscribeAsync(
            created.PublishedFileId,
            cancellation.Token);

        Assert.Equal(WorkshopRemoteStatus.Cancelled, result.Status);
        WorkshopSubscriptionQueryResult stillSubscribed = await transport.GetSubscribedItemsAsync(CancellationToken.None);
        Assert.Contains(stillSubscribed.Items, item => item.PublishedFileId == created.PublishedFileId);
    }

    [Fact]
    public void Imported_room_library_roundtrips_pixels_without_applying_them_anywhere()
    {
        var library = new RoomPaintingLibraryStore(Path.Combine(_root, "rooms"));
        byte[] pixels = new byte[EnvironmentCanvasPolicy.Bytes];
        pixels[0] = 20;
        pixels[1] = 40;
        pixels[2] = 60;
        pixels[3] = 255;

        RoomPaintingImportResult imported = library.Import("Friend Room", pixels);

        Assert.True(imported.Success);
        Assert.NotNull(imported.Entry);
        Assert.Equal(pixels, library.LoadPixels(imported.Entry!.Id));
        Assert.Equal("Friend Room", Assert.Single(library.List()).DisplayName);
    }

    [Fact]
    public void Staging_cleanup_removes_only_old_owned_operations()
    {
        var staging = new WorkshopStagingStore(Path.Combine(_root, "cleanup"));
        Guid oldId = Guid.NewGuid();
        Guid freshId = Guid.NewGuid();
        WorkshopPublishStaging old = staging.CreatePublish(oldId);
        WorkshopPublishStaging fresh = staging.CreatePublish(freshId);
        Directory.SetLastWriteTimeUtc(old.OperationRoot, DateTime.UtcNow.AddDays(-3));
        Directory.SetLastWriteTimeUtc(fresh.OperationRoot, DateTime.UtcNow);

        int removed = staging.CleanupStale(TimeSpan.FromDays(2), DateTimeOffset.UtcNow);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(old.OperationRoot));
        Assert.True(Directory.Exists(fresh.OperationRoot));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string CreateRoomShare(string name, byte[] payload)
    {
        string content = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(content, "environment"));
        File.WriteAllBytes(Path.Combine(content, "environment", "background.png"), payload);
        var file = new Sha256FileEntry
        {
            Path = ShareManifestPolicy.RoomBackgroundPath,
            Sha256 = Convert.ToHexString(SHA256.HashData(payload)),
            EncodedBytes = payload.LongLength,
        };
        ShareManifest manifest = ShareManifestPolicy.Create(
            ShareContentType.RoomPainting,
            "active-room",
            "test",
            [file]);
        File.WriteAllBytes(Path.Combine(content, ShareManifestPolicy.ManifestFileName), ShareManifestPolicy.Serialize(manifest));
        return content;
    }
}
