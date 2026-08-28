using System;
using System.IO;
using System.Threading;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Sharing;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sharing;

public sealed class WorkshopHostileInputBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "desktop-buddy-workshop-hostile-boundary-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Oversized_manifest_returns_validation_instead_of_throwing()
    {
        string content = Path.Combine(_root, "oversized-manifest");
        Directory.CreateDirectory(content);
        File.WriteAllBytes(
            Path.Combine(content, ShareManifestPolicy.ManifestFileName),
            new byte[ShareManifestPolicy.MaximumManifestBytes + 1]);

        ShareFolderReadResult result = new ShareFolderReader().Read(content, ShareContentType.RoomPainting);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Validation.Issues, issue => issue.Code == ShareValidationCode.InvalidEncodedSize);
    }

    [Fact]
    public void Room_library_marks_precommit_cancellation_and_commits_no_entry()
    {
        string libraryRoot = Path.Combine(_root, "room-library");
        var library = new RoomPaintingLibraryStore(libraryRoot);
        byte[] pixels = new byte[EnvironmentCanvasPolicy.Bytes];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        RoomPaintingImportResult result = library.Import(
            "Cancelled Room",
            pixels,
            provenance: null,
            cancellation.Token);

        Assert.False(result.Success);
        Assert.True(result.IsCancelled);
        Assert.Null(result.Entry);
        Assert.Empty(library.List());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
