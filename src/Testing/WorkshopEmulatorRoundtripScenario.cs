using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.Sharing;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Headless integration gate for the complete offline Workshop seam. It deliberately exercises
/// the same exporters, immutable emulator snapshots, installed-folder import boundary,
/// manifest/hash validation, PNG codec, provenance, room library and character persistence used
/// by the Steam-backed flow. No Steam client, network connection, or native GodotSteam binary is
/// required.
/// </summary>
public sealed class WorkshopEmulatorRoundtripScenario : IScenario
{
    public string Id => "workshop_emulator_roundtrip";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "desktop-buddy-workshop-scenario",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<StartupCheck>();

        try
        {
            SteamAppIdentity identity = SteamAppIdentityResolver.Resolve();
            bool identityOk = identity.RuntimeAppId == SteamAppIdentityResolver.DesktopBuddyBaseAppId &&
                identity.WorkshopOwnerAppId == SteamAppIdentityResolver.DesktopBuddyBaseAppId &&
                !identity.IsCrossApp;
            checks.Add(new StartupCheck(
                "workshop_base_app_identity_is_configured",
                identityOk,
                $"runtime={identity.RuntimeAppId} owner={identity.WorkshopOwnerAppId} crossApp={identity.IsCrossApp}"));
            if (!identityOk)
                return Result(checks, $"seed={seed}");

            var staging = new WorkshopStagingStore(Path.Combine(root, "sharing"));
            var transport = new DirectoryWorkshopTransport(Path.Combine(root, "emulator"), firstId: 9100);
            bool roomPassed = await VerifyRoomRoundtripAsync(root, staging, transport, seed, checks);
            if (!roomPassed)
                return Result(checks, $"seed={seed}");

            bool buddyPassed = await VerifyBuddyRoundtripAsync(root, staging, transport, checks);
            return Result(checks, $"seed={seed} roomItem=9100 buddyItem=9101 buddyPassed={buddyPassed}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<bool> VerifyRoomRoundtripAsync(
        string root,
        WorkshopStagingStore staging,
        DirectoryWorkshopTransport transport,
        ulong seed,
        ICollection<StartupCheck> checks)
    {
        var library = new RoomPaintingLibraryStore(Path.Combine(root, "room-library"));
        var exporter = new RoomShareExporter(staging, "headless-scenario");
        var importer = new RoomShareImporter(
            staging,
            library,
            () => new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero));

        byte[] original = CreateDeterministicRoom(seed);
        Guid publishOperation = Guid.NewGuid();
        ShareExportResult exported = exporter.Export(original, publishOperation, CancellationToken.None);
        bool exportOk = exported.Success && exported.Staging is not null && exported.Manifest is not null;
        checks.Add(new StartupCheck(
            "workshop_emulator_room_export_is_valid",
            exportOk,
            exported.Detail ?? $"operation={publishOperation:D}"));
        if (!exportOk) return false;

        WorkshopPublishStaging publish = exported.Staging!.Value;
        WorkshopCreateRemoteResult created = await transport.CreateItemAsync(CancellationToken.None);
        bool createOk = created.IsSuccess && created.PublishedFileId == 9100UL;
        checks.Add(new StartupCheck(
            "workshop_emulator_room_create_item",
            createOk,
            $"status={created.Status} id={created.PublishedFileId}"));
        if (!createOk) return false;

        WorkshopSubmitRemoteResult submitted = await transport.SubmitUpdateAsync(
            new WorkshopRemoteUpdate(
                created.PublishedFileId,
                "Headless Room",
                "Workshop emulator room roundtrip scenario",
                publish.ContentRoot,
                publish.PreviewPath,
                ["Room Painting"],
                "desktop-buddy:room:1"),
            progress: null,
            CancellationToken.None);
        checks.Add(new StartupCheck(
            "workshop_emulator_room_submit_snapshot",
            submitted.IsSuccess,
            $"status={submitted.Status} detail={submitted.Detail}"));
        if (!submitted.IsSuccess) return false;

        // The remote emulator owns an immutable submitted copy now. Deleting the local publish
        // transaction proves later install/import does not accidentally depend on live save data.
        staging.Cleanup(publishOperation);
        checks.Add(new StartupCheck(
            "workshop_emulator_room_publish_staging_cleanup",
            !Directory.Exists(publish.OperationRoot),
            publish.OperationRoot));

        WorkshopSubscriptionQueryResult subscriptionQuery =
            await transport.GetSubscribedItemsAsync(CancellationToken.None);
        bool queryOk = subscriptionQuery.IsSuccess;
        checks.Add(new StartupCheck(
            "workshop_emulator_room_subscription_query_succeeds",
            queryOk,
            $"status={subscriptionQuery.Status} detail={subscriptionQuery.Detail}"));
        if (!queryOk) return false;

        PublishedWorkshopItem? subscribed = subscriptionQuery.Items.SingleOrDefault(item =>
            item.PublishedFileId == created.PublishedFileId);
        bool subscriptionOk = subscribed is not null &&
            subscribed.ContentType == ShareContentTypes.RoomPainting &&
            subscribed.State.HasFlag(WorkshopItemState.Installed);
        checks.Add(new StartupCheck(
            "workshop_emulator_room_subscription_metadata",
            subscriptionOk,
            $"count={subscriptionQuery.Items.Count} type={subscribed?.ContentType}"));
        if (!subscriptionOk || subscribed is null) return false;

        WorkshopInstalledItemResult installed = await transport.EnsureInstalledAsync(
            subscribed.PublishedFileId,
            progress: null,
            CancellationToken.None);
        bool installOk = installed.IsSuccess &&
            installed.InstallFolder is not null &&
            File.Exists(Path.Combine(installed.InstallFolder, ShareManifestPolicy.ManifestFileName));
        checks.Add(new StartupCheck(
            "workshop_emulator_room_install_folder",
            installOk,
            $"status={installed.Status} folder={installed.InstallFolder}"));
        if (!installOk || installed.InstallFolder is null) return false;

        RoomShareImportResult imported = await importer.ImportAsync(
            installed.InstallFolder,
            new WorkshopImportSource(
                subscribed.PublishedFileId,
                installed.TimeUpdated,
                subscribed.DisplayName),
            CancellationToken.None);
        bool importOk = imported.Success && imported.Entry is not null;
        checks.Add(new StartupCheck(
            "workshop_emulator_room_imports_local_copy",
            importOk,
            imported.Detail ?? $"quarantine={imported.QuarantinePath}"));
        if (!importOk || imported.Entry is null) return false;

        byte[]? loaded = library.LoadPixels(imported.Entry.Id);
        bool exactRoundtrip = loaded is not null && original.AsSpan().SequenceEqual(loaded);
        checks.Add(new StartupCheck(
            "workshop_emulator_room_pixels_roundtrip_exactly",
            exactRoundtrip,
            $"loadedBytes={loaded?.Length ?? 0}"));

        RoomPaintingLibraryEntry[] libraryEntries = [.. library.List()];
        bool provenanceKept = libraryEntries.Length == 1 &&
            libraryEntries[0].WorkshopItemId == created.PublishedFileId;
        checks.Add(new StartupCheck(
            "workshop_emulator_room_preserves_provenance",
            provenanceKept,
            $"entries={libraryEntries.Length} workshopId={(libraryEntries.Length == 1 ? libraryEntries[0].WorkshopItemId : null)}"));

        return exactRoundtrip && provenanceKept;
    }

    private static async Task<bool> VerifyBuddyRoundtripAsync(
        string root,
        WorkshopStagingStore staging,
        DirectoryWorkshopTransport transport,
        ICollection<StartupCheck> checks)
    {
        DateTimeOffset fixedTime = new(2026, 8, 25, 8, 5, 0, TimeSpan.Zero);
        var sourceCharacters = new CharacterStore(
            new CharacterFileSystem(),
            Path.Combine(root, "source-characters"),
            () => fixedTime);
        var importedCharacters = new CharacterStore(
            new CharacterFileSystem(),
            Path.Combine(root, "imported-characters"),
            () => fixedTime);

        Guid sourceId = Guid.Parse("91000000-0000-4000-8000-000000000001");
        CharacterDocument sourceDocument = CharacterDocument.CreateDefault(sourceId, "Headless Buddy");
        CharacterSaveResult sourceSaved = await sourceCharacters.SaveAsync(sourceDocument, CancellationToken.None);
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_source_saved",
            sourceSaved.IsSuccess,
            $"status={sourceSaved.Status} detail={sourceSaved.Detail}"));
        if (!sourceSaved.IsSuccess) return false;

        var exporter = new CharacterShareExporter(staging, sourceCharacters, "headless-scenario");
        var importer = new CharacterShareImporter(staging, importedCharacters, () => fixedTime);
        Guid publishOperation = Guid.NewGuid();
        ShareExportResult exported = await exporter.ExportAsync(
            sourceId,
            publishOperation,
            previewPng: null,
            CancellationToken.None);
        bool exportOk = exported.Success && exported.Staging is not null && exported.Manifest is not null &&
            exported.Manifest.ContentType == ShareContentTypes.BuddyCharacter;
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_export_is_valid",
            exportOk,
            exported.Detail ?? $"operation={publishOperation:D}"));
        if (!exportOk) return false;

        WorkshopPublishStaging publish = exported.Staging!.Value;
        WorkshopCreateRemoteResult created = await transport.CreateItemAsync(CancellationToken.None);
        bool createOk = created.IsSuccess && created.PublishedFileId == 9101UL;
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_create_item",
            createOk,
            $"status={created.Status} id={created.PublishedFileId}"));
        if (!createOk) return false;

        WorkshopSubmitRemoteResult submitted = await transport.SubmitUpdateAsync(
            new WorkshopRemoteUpdate(
                created.PublishedFileId,
                "Headless Buddy",
                "Workshop emulator buddy roundtrip scenario",
                publish.ContentRoot,
                previewFile: null,
                ["Buddy"],
                "desktop-buddy:buddy:1"),
            progress: null,
            CancellationToken.None);
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_submit_snapshot",
            submitted.IsSuccess,
            $"status={submitted.Status} detail={submitted.Detail}"));
        if (!submitted.IsSuccess) return false;

        staging.Cleanup(publishOperation);
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_publish_staging_cleanup",
            !Directory.Exists(publish.OperationRoot),
            publish.OperationRoot));

        WorkshopSubscriptionQueryResult subscriptions =
            await transport.GetSubscribedItemsAsync(CancellationToken.None);
        PublishedWorkshopItem? subscribed = subscriptions.IsSuccess
            ? subscriptions.Items.SingleOrDefault(item => item.PublishedFileId == created.PublishedFileId)
            : null;
        bool subscriptionOk = subscribed is not null &&
            subscribed.ContentType == ShareContentTypes.BuddyCharacter &&
            subscribed.State.HasFlag(WorkshopItemState.Installed);
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_subscription_metadata",
            subscriptions.IsSuccess && subscriptionOk,
            $"status={subscriptions.Status} count={subscriptions.Items.Count} type={subscribed?.ContentType}"));
        if (!subscriptions.IsSuccess || !subscriptionOk || subscribed is null) return false;

        WorkshopInstalledItemResult installed = await transport.EnsureInstalledAsync(
            subscribed.PublishedFileId,
            progress: null,
            CancellationToken.None);
        bool installOk = installed.IsSuccess && installed.InstallFolder is not null;
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_install_folder",
            installOk,
            $"status={installed.Status} folder={installed.InstallFolder}"));
        if (!installOk || installed.InstallFolder is null) return false;

        CharacterShareImportResult imported = await importer.ImportAsync(
            installed.InstallFolder,
            new WorkshopImportSource(
                subscribed.PublishedFileId,
                installed.TimeUpdated,
                subscribed.DisplayName),
            CancellationToken.None);
        bool freshIdentity = imported.Success &&
            imported.LocalCharacterId is Guid localId &&
            localId != Guid.Empty &&
            localId != sourceId;
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_import_uses_fresh_local_guid",
            freshIdentity,
            imported.Detail ?? $"source={sourceId:D} local={imported.LocalCharacterId}"));
        if (!freshIdentity || imported.LocalCharacterId is not Guid importedId) return false;

        CharacterLoadResult loaded = await importedCharacters.LoadAsync(importedId, CancellationToken.None);
        bool documentRoundtrip = loaded.IsSuccess &&
            loaded.Document is not null &&
            loaded.Document.Id == importedId &&
            loaded.Document.DisplayName == sourceDocument.DisplayName &&
            importedCharacters.CountStoredCharacters() == 1;
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_document_roundtrips_as_local_copy",
            documentRoundtrip,
            $"status={loaded.Status} id={loaded.Document?.Id} name={loaded.Document?.DisplayName}"));

        WorkshopProvenance? provenance = WorkshopProvenanceStore.TryRead(importedCharacters.Paths.Directory(importedId));
        bool provenanceKept = provenance is not null &&
            provenance.PublishedFileId == created.PublishedFileId &&
            provenance.ContentType == ShareContentTypes.BuddyCharacter;
        checks.Add(new StartupCheck(
            "workshop_emulator_buddy_preserves_provenance",
            provenanceKept,
            $"workshopId={provenance?.PublishedFileId} type={provenance?.ContentType}"));

        return documentRoundtrip && provenanceKept;
    }

    private static byte[] CreateDeterministicRoom(ulong seed)
    {
        byte[] pixels = new byte[EnvironmentCanvasPolicy.Bytes];
        uint state = unchecked((uint)(seed ^ 0xA5C39E71UL));
        for (int index = 0; index < pixels.Length; index += EnvironmentCanvasPolicy.BytesPerPixel)
        {
            state = unchecked((state * 1664525u) + 1013904223u);
            pixels[index] = (byte)(state >> 24);
            pixels[index + 1] = (byte)(state >> 16);
            pixels[index + 2] = (byte)(state >> 8);
            pixels[index + 3] = 255;
        }
        return pixels;
    }

    private static ScenarioResult Result(IReadOnlyList<StartupCheck> checks, params string[] messages)
    {
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
