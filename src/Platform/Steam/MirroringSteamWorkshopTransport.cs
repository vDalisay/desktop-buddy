using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// Presents one Workshop transport to the sharing layer while duplicating every new publish across
/// two Steam consumer AppIDs. The primary item belongs to the running app (the Demo), so Demo
/// players can subscribe/download it. The mirror belongs to the full game, so full-game players
/// see the same creation in their Workshop. Steam still treats the two PublishedFileIds as distinct
/// items, which is why this adapter keeps the pairing until the initial content upload finishes.
///
/// Consumption is intentionally stricter than Steam's website UI: a player may be able to click
/// Subscribe on the full game's public Workshop page, but this Demo adapter only exposes or installs
/// items whose SteamUGC consumer AppID is the running Demo AppID.
/// </summary>
public sealed class MirroringSteamWorkshopTransport : ISteamWorkshopTransport
{
    private readonly ITargetedSteamWorkshopTransport _inner;
    private readonly uint _primaryConsumerAppId;
    private readonly uint _mirrorConsumerAppId;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, WorkshopCreateRemoteResult> _pendingMirrors = new();

    private readonly record struct ConsumerScopeCheck(
        bool Allowed,
        WorkshopRemoteStatus Status,
        string? Detail = null);

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

    public async Task<WorkshopSubscriptionQueryResult> GetSubscribedItemsAsync(CancellationToken token)
    {
        WorkshopSubscriptionQueryResult result = await _inner.GetSubscribedItemsAsync(token);
        return FilterToPrimaryConsumer(result);
    }

    public async Task<WorkshopSubscriptionQueryResult> GetItemDetailsAsync(
        IReadOnlyList<ulong> publishedFileIds,
        CancellationToken token)
    {
        WorkshopSubscriptionQueryResult result = await _inner.GetItemDetailsAsync(publishedFileIds, token);
        return FilterToPrimaryConsumer(result);
    }

    public async Task<WorkshopSubscriptionChangeResult> UnsubscribeAsync(
        ulong publishedFileId,
        CancellationToken token)
    {
        ConsumerScopeCheck scope = await VerifyPrimaryConsumerAsync(publishedFileId, token);
        if (!scope.Allowed)
        {
            return new WorkshopSubscriptionChangeResult(
                scope.Status,
                publishedFileId,
                Detail: scope.Detail);
        }
        return await _inner.UnsubscribeAsync(publishedFileId, token);
    }

    public async Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token)
    {
        ConsumerScopeCheck scope = await VerifyPrimaryConsumerAsync(publishedFileId, token);
        if (!scope.Allowed)
        {
            return new WorkshopInstalledItemResult(
                scope.Status,
                publishedFileId,
                null,
                0,
                Detail: scope.Detail);
        }
        return await _inner.EnsureInstalledAsync(publishedFileId, progress, token);
    }

    public void OpenWorkshopBrowser() =>
        _inner.OpenWorkshopBrowser(_primaryConsumerAppId);

    public void OpenWorkshopItem(ulong publishedFileId) =>
        _inner.OpenWorkshopItem(publishedFileId);

    private WorkshopSubscriptionQueryResult FilterToPrimaryConsumer(WorkshopSubscriptionQueryResult result)
    {
        if (!result.IsSuccess)
            return result;

        PublishedWorkshopItem[] allowed = result.Items
            .Where(item => item.ConsumerAppId == _primaryConsumerAppId)
            .ToArray();
        int rejected = result.Items.Count - allowed.Length;
        if (rejected == 0)
            return result;

        string detail = $"Ignored {rejected} Workshop item(s) that are not consumable by Desktop Buddy Demo (AppID {_primaryConsumerAppId}).";
        return result with
        {
            Items = allowed,
            Detail = string.IsNullOrWhiteSpace(result.Detail) ? detail : $"{result.Detail} {detail}",
        };
    }

    private async Task<ConsumerScopeCheck> VerifyPrimaryConsumerAsync(
        ulong publishedFileId,
        CancellationToken token)
    {
        if (publishedFileId == 0)
            return new ConsumerScopeCheck(false, WorkshopRemoteStatus.Failed, "Published file ID is required.");
        if (token.IsCancellationRequested)
            return new ConsumerScopeCheck(false, WorkshopRemoteStatus.Cancelled, "Workshop operation cancelled.");

        WorkshopSubscriptionQueryResult details = await _inner.GetItemDetailsAsync([publishedFileId], token);
        if (!details.IsSuccess)
        {
            return new ConsumerScopeCheck(
                false,
                details.Status,
                details.Detail ?? "Steam could not verify this Workshop item's consumer AppID.");
        }

        PublishedWorkshopItem? item = details.Items.FirstOrDefault(item => item.PublishedFileId == publishedFileId);
        if (item is null || item.ConsumerAppId == 0)
        {
            return new ConsumerScopeCheck(
                false,
                WorkshopRemoteStatus.Unsupported,
                "Desktop Buddy Demo refused this Workshop item because Steam did not verify that it belongs to the Demo Workshop.");
        }

        if (item.ConsumerAppId != _primaryConsumerAppId)
        {
            return new ConsumerScopeCheck(
                false,
                WorkshopRemoteStatus.Unsupported,
                $"Desktop Buddy Demo cannot use Workshop item {publishedFileId}: it belongs to consumer AppID {item.ConsumerAppId}, not Demo AppID {_primaryConsumerAppId}.");
        }

        return new ConsumerScopeCheck(true, WorkshopRemoteStatus.Success);
    }
}
