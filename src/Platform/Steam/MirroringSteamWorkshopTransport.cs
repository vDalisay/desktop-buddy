using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// Presents one Workshop transport to the sharing layer while duplicating every new publish across
/// two Steam consumer AppIDs. The primary item belongs to the running app (the Demo), so Demo
/// players can subscribe/download it. The mirror belongs to the full game, so full-game players
/// see the same creation in their Workshop. Steam still treats the two PublishedFileIds as distinct
/// items, which is why this adapter keeps the pairing until the initial content upload finishes.
/// </summary>
public sealed class MirroringSteamWorkshopTransport : ISteamWorkshopTransport
{
    private readonly ITargetedSteamWorkshopTransport _inner;
    private readonly uint _primaryConsumerAppId;
    private readonly uint _mirrorConsumerAppId;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, WorkshopCreateRemoteResult> _pendingMirrors = new();

    public MirroringSteamWorkshopTransport(
        ITargetedSteamWorkshopTransport inner,
        uint primaryConsumerAppId,
        uint mirrorConsumerAppId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (primaryConsumerAppId == 0)
            throw new ArgumentOutOfRangeException(nameof(primaryConsumerAppId));
        if (mirrorConsumerAppId == 0)
            throw new ArgumentOutOfRangeException(nameof(mirrorConsumerAppId));
        if (primaryConsumerAppId == mirrorConsumerAppId)
            throw new ArgumentException("Mirroring requires two distinct Steam consumer AppIDs.", nameof(mirrorConsumerAppId));
        if (primaryConsumerAppId != inner.RuntimeAppId)
            throw new ArgumentException("The primary Workshop target must be the running Steam AppID.", nameof(primaryConsumerAppId));
        if (mirrorConsumerAppId != inner.WorkshopOwnerAppId)
            throw new ArgumentException("The mirror Workshop target must be the configured full-game Workshop owner AppID.", nameof(mirrorConsumerAppId));

        _primaryConsumerAppId = primaryConsumerAppId;
        _mirrorConsumerAppId = mirrorConsumerAppId;
    }

    public bool IsAvailable => _inner.IsAvailable;
    public bool IsInstalled => _inner.IsInstalled;
    public bool IsInitialized => _inner.IsInitialized;
    public string? UnavailableReason => _inner.UnavailableReason;

    public async Task<WorkshopCreateRemoteResult> CreateItemAsync(CancellationToken token)
    {
        WorkshopCreateRemoteResult primary = await _inner.CreateItemAsync(_primaryConsumerAppId, token);
        if (!primary.IsSuccess)
            return primary;

        WorkshopCreateRemoteResult mirror;
        if (token.IsCancellationRequested)
        {
            mirror = new WorkshopCreateRemoteResult(
                WorkshopRemoteStatus.Cancelled,
                0,
                false,
                Detail: "Publishing was cancelled before the full-game Workshop mirror was created.");
        }
        else
        {
            mirror = await _inner.CreateItemAsync(_mirrorConsumerAppId, token);
        }

        lock (_gate)
            _pendingMirrors[primary.PublishedFileId] = mirror;

        return primary with
        {
            NeedsLegalAgreement = primary.NeedsLegalAgreement || mirror.NeedsLegalAgreement,
            Detail = mirror.IsSuccess
                ? primary.Detail
                : $"The Demo Workshop item was created as {primary.PublishedFileId}, but the full-game mirror could not be created: {mirror.Detail ?? mirror.Status.ToString()}",
        };
    }

    public async Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
        WorkshopRemoteUpdate update,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token)
    {
        WorkshopCreateRemoteResult mirrorCreate;
        lock (_gate)
        {
            if (!_pendingMirrors.TryGetValue(update.PublishedFileId, out mirrorCreate))
            {
                return new WorkshopSubmitRemoteResult(
                    WorkshopRemoteStatus.Failed,
                    update.PublishedFileId,
                    false,
                    Detail: "The Demo Workshop publish lost its full-game mirror pairing before upload.");
            }
        }

        try
        {
            // Once CreateItem has committed either remote item, finish the initial content upload
            // even if the player presses Cancel. This mirrors the coordinator's orphan-prevention
            // rule and guarantees we never knowingly leave an empty mirror item behind.
            WorkshopSubmitRemoteResult primary = await _inner.SubmitUpdateAsync(
                _primaryConsumerAppId,
                update,
                progress,
                CancellationToken.None);

            WorkshopSubmitRemoteResult? mirror = null;
            if (mirrorCreate.IsSuccess)
            {
                var mirrorUpdate = update with
                {
                    PublishedFileId = mirrorCreate.PublishedFileId,
                    ChangeNote = string.IsNullOrWhiteSpace(update.ChangeNote)
                        ? "Mirrored from Desktop Buddy Demo"
                        : update.ChangeNote,
                };
                mirror = await _inner.SubmitUpdateAsync(
                    _mirrorConsumerAppId,
                    mirrorUpdate,
                    progress,
                    CancellationToken.None);
            }

            bool needsAgreement = primary.NeedsLegalAgreement ||
                mirrorCreate.NeedsLegalAgreement ||
                (mirror?.NeedsLegalAgreement ?? false);

            if (!primary.IsSuccess)
            {
                string suffix = mirror?.IsSuccess == true
                    ? $" The full-game mirror was still uploaded as item {mirror.Value.PublishedFileId}."
                    : string.Empty;
                return primary with
                {
                    NeedsLegalAgreement = needsAgreement,
                    Detail = $"The Demo Workshop item {update.PublishedFileId} could not be uploaded: {primary.Detail ?? primary.Status.ToString()}.{suffix}".Trim(),
                };
            }

            if (!mirrorCreate.IsSuccess)
            {
                return new WorkshopSubmitRemoteResult(
                    WorkshopRemoteStatus.Failed,
                    update.PublishedFileId,
                    needsAgreement,
                    Detail: $"Published to the Demo Workshop as item {update.PublishedFileId}, but the full-game mirror could not be created: {mirrorCreate.Detail ?? mirrorCreate.Status.ToString()}");
            }

            if (mirror is not { IsSuccess: true })
            {
                return new WorkshopSubmitRemoteResult(
                    WorkshopRemoteStatus.Failed,
                    update.PublishedFileId,
                    needsAgreement,
                    Detail: $"Published to the Demo Workshop as item {update.PublishedFileId}, but its full-game mirror {mirrorCreate.PublishedFileId} failed to upload: {mirror?.Detail ?? mirror?.Status.ToString() ?? "unknown Steam error"}");
            }

            return new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Success,
                update.PublishedFileId,
                needsAgreement,
                Detail: $"Published to the Demo Workshop as item {update.PublishedFileId} and mirrored to the full-game Workshop as item {mirrorCreate.PublishedFileId}.");
        }
        finally
        {
            lock (_gate)
                _pendingMirrors.Remove(update.PublishedFileId);
        }
    }

    public Task<WorkshopSubscriptionQueryResult> GetSubscribedItemsAsync(CancellationToken token) =>
        _inner.GetSubscribedItemsAsync(token);

    public Task<WorkshopSubscriptionQueryResult> GetItemDetailsAsync(
        IReadOnlyList<ulong> publishedFileIds,
        CancellationToken token) =>
        _inner.GetItemDetailsAsync(publishedFileIds, token);

    public Task<WorkshopSubscriptionChangeResult> UnsubscribeAsync(
        ulong publishedFileId,
        CancellationToken token) =>
        _inner.UnsubscribeAsync(publishedFileId, token);

    public Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token) =>
        _inner.EnsureInstalledAsync(publishedFileId, progress, token);

    public void OpenWorkshopBrowser() =>
        _inner.OpenWorkshopBrowser(_primaryConsumerAppId);

    public void OpenWorkshopItem(ulong publishedFileId) =>
        _inner.OpenWorkshopItem(publishedFileId);
}
