using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Platform.Steam;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sharing;

public sealed class WorkshopTransportCancellationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "desktop-buddy-workshop-cancellation-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Directory_transport_returns_typed_cancelled_results_without_remote_side_effects()
    {
        string emulatorRoot = Path.Combine(_root, "emulator");
        Directory.CreateDirectory(_root);
        var transport = new DirectoryWorkshopTransport(emulatorRoot, firstId: 8100);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        WorkshopCreateRemoteResult create = await transport.CreateItemAsync(cancellation.Token);
        Assert.Equal(WorkshopRemoteStatus.Cancelled, create.Status);
        Assert.Equal(0UL, create.PublishedFileId);
        Assert.Empty(Directory.EnumerateDirectories(emulatorRoot));

        string content = Path.Combine(_root, "content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "manifest.json"), "{}");

        WorkshopSubmitRemoteResult submit = await transport.SubmitUpdateAsync(
            new WorkshopRemoteUpdate(
                8100,
                "Cancelled Room",
                "Should never be committed",
                content,
                null,
                ["Room Painting"],
                "desktop-buddy:room:1"),
            progress: null,
            cancellation.Token);
        Assert.Equal(WorkshopRemoteStatus.Cancelled, submit.Status);
        Assert.False(Directory.Exists(Path.Combine(emulatorRoot, "8100.staging")));
        Assert.False(Directory.Exists(Path.Combine(emulatorRoot, "8100.previous")));

        WorkshopSubscriptionQueryResult subscriptions =
            await transport.GetSubscribedItemsAsync(cancellation.Token);
        Assert.Equal(WorkshopRemoteStatus.Cancelled, subscriptions.Status);
        Assert.Empty(subscriptions.Items);

        WorkshopInstalledItemResult install = await transport.EnsureInstalledAsync(
            8100,
            progress: null,
            cancellation.Token);
        Assert.Equal(WorkshopRemoteStatus.Cancelled, install.Status);
        Assert.Null(install.InstallFolder);
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
