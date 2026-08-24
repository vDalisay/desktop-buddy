using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;
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
                ["DesktopBuddy.RoomPainting", "FormatVersion.1"],
                "desktop-buddy:room:1"),
            progress: null,
            CancellationToken.None);
        Assert.True(submitted.IsSuccess);

        var subscribed = await transport.GetSubscribedItemsAsync(CancellationToken.None);
        PublishedWorkshopItem item = Assert.Single(subscribed);
        Assert.Equal(ShareContentTypes.RoomPainting, item.ContentType);
        Assert.True(item.State.HasFlag(WorkshopItemState.Installed));

        WorkshopInstalledItemResult installed = await transport.EnsureInstalledAsync(
            item.PublishedFileId,
            progress: null,
            CancellationToken.None);
        Assert.True(installed.IsSuccess);
        Assert.True(File.Exists(Path.Combine(installed.InstallFolder!, "manifest.json")));

        Assert.True(transport.SetSubscribed(item.PublishedFileId, false));
        Assert.Empty(await transport.GetSubscribedItemsAsync(CancellationToken.None));
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
