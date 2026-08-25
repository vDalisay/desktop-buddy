using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.Sharing;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Headless integration gate for the complete offline Workshop seam. It deliberately exercises
/// the same exporter, immutable emulator snapshot, installed-folder import boundary, manifest/hash
/// validation, PNG codec, provenance, and local room library used by the Steam-backed flow.
/// No Steam client, network connection, or native GodotSteam binary is required.
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
                "workshop_emulator_export_is_valid",
                exportOk,
                exported.Detail ?? $"operation={publishOperation:D}"));
            if (!exportOk)
                return Result(checks, $"seed={seed}");

            WorkshopPublishStaging publish = exported.Staging!.Value;
            WorkshopCreateRemoteResult created = await transport.CreateItemAsync(CancellationToken.None);
            bool createOk = created.IsSuccess && created.PublishedFileId == 9100UL;
            checks.Add(new StartupCheck(
                "workshop_emulator_create_item",
                createOk,
                $"status={created.Status} id={created.PublishedFileId}"));
            if (!createOk)
                return Result(checks, $"seed={seed}");

            WorkshopSubmitRemoteResult submitted = await transport.SubmitUpdateAsync(
                new WorkshopRemoteUpdate(
                    created.PublishedFileId,
                    "Headless Room",
                    "Workshop emulator roundtrip scenario",
                    publish.ContentRoot,
                    publish.PreviewPath,
                    ["DesktopBuddy.RoomPainting", "FormatVersion.1"],
                    "desktop-buddy:room:1"),
                progress: null,
                CancellationToken.None);
            checks.Add(new StartupCheck(
                "workshop_emulator_submit_snapshot",
                submitted.IsSuccess,
                $"status={submitted.Status} detail={submitted.Detail}"));
            if (!submitted.IsSuccess)
                return Result(checks, $"seed={seed}");

            // The remote emulator owns an immutable submitted copy now. Deleting the local publish
            // transaction proves later install/import does not accidentally depend on live save data.
            staging.Cleanup(publishOperation);
            checks.Add(new StartupCheck(
                "workshop_emulator_publish_staging_cleanup",
                !Directory.Exists(publish.OperationRoot),
                publish.OperationRoot));

            IReadOnlyList<PublishedWorkshopItem> subscriptions =
                await transport.GetSubscribedItemsAsync(CancellationToken.None);
            PublishedWorkshopItem? subscribed = subscriptions.Count == 1 ? subscriptions[0] : null;
            bool subscriptionOk = subscribed is not null &&
                subscribed.PublishedFileId == created.PublishedFileId &&
                subscribed.ContentType == DesktopBuddy.Domain.Sharing.ShareContentTypes.RoomPainting &&
                subscribed.State.HasFlag(WorkshopItemState.Installed);
            checks.Add(new StartupCheck(
                "workshop_emulator_subscription_metadata",
                subscriptionOk,
                $"count={subscriptions.Count} type={subscribed?.ContentType}"));
            if (!subscriptionOk || subscribed is null)
                return Result(checks, $"seed={seed}");

            WorkshopInstalledItemResult installed = await transport.EnsureInstalledAsync(
                subscribed.PublishedFileId,
                progress: null,
                CancellationToken.None);
            bool installOk = installed.IsSuccess &&
                installed.InstallFolder is not null &&
                File.Exists(Path.Combine(installed.InstallFolder, "manifest.json"));
            checks.Add(new StartupCheck(
                "workshop_emulator_install_folder",
                installOk,
                $"status={installed.Status} folder={installed.InstallFolder}"));
            if (!installOk || installed.InstallFolder is null)
                return Result(checks, $"seed={seed}");

            RoomShareImportResult imported = importer.Import(
                installed.InstallFolder,
                new WorkshopImportSource(
                    subscribed.PublishedFileId,
                    installed.TimeUpdated,
                    subscribed.DisplayName),
                CancellationToken.None);
            bool importOk = imported.Success && imported.Entry is not null;
            checks.Add(new StartupCheck(
                "workshop_emulator_imports_local_copy",
                importOk,
                imported.Detail ?? $"quarantine={imported.QuarantinePath}"));
            if (!importOk || imported.Entry is null)
                return Result(checks, $"seed={seed}");

            byte[]? loaded = library.LoadPixels(imported.Entry.Id);
            bool exactRoundtrip = loaded is not null && original.AsSpan().SequenceEqual(loaded);
            checks.Add(new StartupCheck(
                "workshop_emulator_pixels_roundtrip_exactly",
                exactRoundtrip,
                $"loadedBytes={loaded?.Length ?? 0}"));

            RoomPaintingLibraryEntry[] libraryEntries = [.. library.List()];
            bool provenanceKept = libraryEntries.Length == 1 &&
                libraryEntries[0].WorkshopItemId == created.PublishedFileId;
            checks.Add(new StartupCheck(
                "workshop_emulator_preserves_provenance",
                provenanceKept,
                $"entries={libraryEntries.Length} workshopId={(libraryEntries.Length == 1 ? libraryEntries[0].WorkshopItemId : null)}"));

            return Result(checks, $"seed={seed} item={created.PublishedFileId}");
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
