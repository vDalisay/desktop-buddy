using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Platform.Steam;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sharing;

public sealed class MirroringSteamWorkshopTransportTests
{
    private const uint FullAppId = 5_114_950;
    private const uint DemoAppId = 5_228_990;

    [Fact]
    public async Task Publish_creates_demo_item_and_full_game_mirror()
    {
        var inner = new FakeTargetedTransport(DemoAppId, FullAppId);
        inner.CreateResults.Enqueue(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Success, 101, false));
        inner.CreateResults.Enqueue(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Success, 202, false));
        var transport = new MirroringSteamWorkshopTransport(inner, DemoAppId, FullAppId);

        WorkshopCreateRemoteResult created = await transport.CreateItemAsync(CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(101UL, created.PublishedFileId);
        Assert.Equal(new[] { DemoAppId, FullAppId }, inner.CreateTargets);

        var update = new WorkshopRemoteUpdate(
            created.PublishedFileId,
            "Buddy",
            "Description",
            "content",
            null,
            ["Buddy"],
            "desktop-buddy:buddy:1");
        WorkshopSubmitRemoteResult submitted = await transport.SubmitUpdateAsync(update, null, CancellationToken.None);

        Assert.True(submitted.IsSuccess);
        Assert.Equal(101UL, submitted.PublishedFileId);
        Assert.Contains("mirrored", submitted.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, inner.Submissions.Count);
        Assert.Equal((DemoAppId, 101UL), inner.Submissions[0]);
        Assert.Equal((FullAppId, 202UL), inner.Submissions[1]);
    }

    [Fact]
    public async Task Subscriptions_and_browser_stay_on_demo_workshop()
    {
        var inner = new FakeTargetedTransport(DemoAppId, FullAppId);
        var transport = new MirroringSteamWorkshopTransport(inner, DemoAppId, FullAppId);

        WorkshopSubscriptionQueryResult subscriptions = await transport.GetSubscribedItemsAsync(CancellationToken.None);
        transport.OpenWorkshopBrowser();

        Assert.True(subscriptions.IsSuccess);
        Assert.Equal(1, inner.SubscriptionQueries);
        Assert.Equal(DemoAppId, inner.LastBrowserAppId);
    }

    [Fact]
    public async Task Mirror_failure_reports_partial_publish_after_demo_copy_is_uploaded()
    {
        var inner = new FakeTargetedTransport(DemoAppId, FullAppId);
        inner.CreateResults.Enqueue(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Success, 303, false));
        inner.CreateResults.Enqueue(new WorkshopCreateRemoteResult(
            WorkshopRemoteStatus.Failed,
            0,
            false,
            Detail: "cross-app permission missing"));
        var transport = new MirroringSteamWorkshopTransport(inner, DemoAppId, FullAppId);

        WorkshopCreateRemoteResult created = await transport.CreateItemAsync(CancellationToken.None);
        Assert.True(created.IsSuccess);

        var update = new WorkshopRemoteUpdate(
            created.PublishedFileId,
            "Room",
            "Description",
            "content",
            null,
            ["Room Painting"],
            "desktop-buddy:room:1");
        WorkshopSubmitRemoteResult submitted = await transport.SubmitUpdateAsync(update, null, CancellationToken.None);

        Assert.False(submitted.IsSuccess);
        Assert.Equal(WorkshopRemoteStatus.Failed, submitted.Status);
        Assert.Contains("Published to the Demo Workshop", submitted.Detail!);
        Assert.Contains("full-game mirror", submitted.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(inner.Submissions);
        Assert.Equal((DemoAppId, 303UL), inner.Submissions[0]);
    }

    private sealed class FakeTargetedTransport : ITargetedSteamWorkshopTransport
    {
        public FakeTargetedTransport(uint runtimeAppId, uint workshopOwnerAppId)
        {
            RuntimeAppId = runtimeAppId;
            WorkshopOwnerAppId = workshopOwnerAppId;
        }

        public Queue<WorkshopCreateRemoteResult> CreateResults { get; } = new();
        public List<uint> CreateTargets { get; } = [];
        public List<(uint AppId, ulong PublishedFileId)> Submissions { get; } = [];
        public int SubscriptionQueries { get; private set; }
        public uint LastBrowserAppId { get; private set; }

        public bool IsAvailable => true;
        public bool IsInstalled => true;
        public bool IsInitialized => true;
        public string? UnavailableReason => null;
        public uint RuntimeAppId { get; }
        public uint WorkshopOwnerAppId { get; }

        public Task<WorkshopCreateRemoteResult> CreateItemAsync(CancellationToken token) =>
            CreateItemAsync(WorkshopOwnerAppId, token);

        public Task<WorkshopCreateRemoteResult> CreateItemAsync(uint consumerAppId, CancellationToken token)
        {
            CreateTargets.Add(consumerAppId);
            if (token.IsCancellationRequested)
            {
                return Task.FromResult(new WorkshopCreateRemoteResult(
                    WorkshopRemoteStatus.Cancelled,
                    0,
                    false));
            }

            return Task.FromResult(CreateResults.Count > 0
                ? CreateResults.Dequeue()
                : new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Success, (ulong)(1000 + CreateTargets.Count), false));
        }

        public Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
            WorkshopRemoteUpdate update,
            IProgress<WorkshopTransferProgress>? progress,
            CancellationToken token) =>
            SubmitUpdateAsync(WorkshopOwnerAppId, update, progress, token);

        public Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
            uint consumerAppId,
            WorkshopRemoteUpdate update,
            IProgress<WorkshopTransferProgress>? progress,
            CancellationToken token)
        {
            Submissions.Add((consumerAppId, update.PublishedFileId));
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Success,
                update.PublishedFileId,
                false));
        }

        public Task<WorkshopSubscriptionQueryResult> GetSubscribedItemsAsync(CancellationToken token)
        {
            SubscriptionQueries++;
            return Task.FromResult(WorkshopSubscriptionQueryResult.Success(Array.Empty<PublishedWorkshopItem>()));
        }

        public Task<WorkshopSubscriptionQueryResult> GetItemDetailsAsync(
            IReadOnlyList<ulong> publishedFileIds,
            CancellationToken token) =>
            Task.FromResult(WorkshopSubscriptionQueryResult.Success(Array.Empty<PublishedWorkshopItem>()));

        public Task<WorkshopSubscriptionChangeResult> UnsubscribeAsync(
            ulong publishedFileId,
            CancellationToken token) =>
            Task.FromResult(new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Success, publishedFileId));

        public Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
            ulong publishedFileId,
            IProgress<WorkshopTransferProgress>? progress,
            CancellationToken token) =>
            Task.FromResult(new WorkshopInstalledItemResult(
                WorkshopRemoteStatus.Success,
                publishedFileId,
                "content",
                0));

        public void OpenWorkshopBrowser() => OpenWorkshopBrowser(WorkshopOwnerAppId);

        public void OpenWorkshopBrowser(uint consumerAppId) => LastBrowserAppId = consumerAppId;

        public void OpenWorkshopItem(ulong publishedFileId) { }
    }
}
