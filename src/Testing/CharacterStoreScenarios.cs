using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

internal static class CharacterStoreScenarioSupport
{
    public static string CreateRoot(string scenario)
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-a5", scenario,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static CharacterDocument Document(Guid id, string name) =>
        CharacterDocument.CreateDefault(id, name);

    public static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static ScenarioResult Result(
        IReadOnlyList<StartupCheck> checks,
        params string[] messages)
    {
        bool passed = checks.All(static check => check.Passed);
        return new ScenarioResult(passed, checks, messages);
    }
}

public sealed class EditorInvalidPrimaryBackupRecoveryScenario : IScenario
{
    public string Id => "editor_invalid_primary_backup_recovery";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string root = CharacterStoreScenarioSupport.CreateRoot(Id);
        var checks = new List<StartupCheck>();
        try
        {
            var fs = new CharacterFileSystem();
            DateTimeOffset fixedTime = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
            var store = new CharacterStore(fs, root, () => fixedTime);
            Guid id = Guid.Parse("10000000-0000-4000-8000-000000000001");
            CharacterSaveResult first = await store.SaveAsync(
                CharacterStoreScenarioSupport.Document(id, "First"), CancellationToken.None);
            CharacterSaveResult second = await store.SaveAsync(
                CharacterStoreScenarioSupport.Document(id, "Second"), CancellationToken.None);
            File.WriteAllText(store.Paths.Primary(id), "{broken");

            CharacterLoadResult loaded = await store.LoadAsync(id, CancellationToken.None);
            bool recovered = first.IsSuccess && second.IsSuccess &&
                loaded.Status == CharacterLoadStatus.BackupRecovered &&
                loaded.Document?.DisplayName == "First" &&
                loaded.QuarantinedPrimary is { } quarantine && File.Exists(quarantine) &&
                File.Exists(store.Paths.Backup(id));
            checks.Add(new StartupCheck("a5_corrupt_primary_recovers_backup", recovered,
                $"status={loaded.Status} name={loaded.Document?.DisplayName} quarantine={loaded.QuarantinedPrimary}"));

            bool rollingBackup = CharacterDocumentPolicy.DecodeAndMigrate(
                File.ReadAllText(store.Paths.Backup(id))).Document?.DisplayName == "First";
            checks.Add(new StartupCheck("a5_replacement_rolls_backup", rollingBackup,
                $"backup={store.Paths.Backup(id)}"));
        }
        finally
        {
            CharacterStoreScenarioSupport.Cleanup(root);
        }

        return CharacterStoreScenarioSupport.Result(checks, $"seed={seed}");
    }
}

public sealed class EditorInvalidQuarantineScenario : IScenario
{
    public string Id => "editor_invalid_quarantine";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string root = CharacterStoreScenarioSupport.CreateRoot(Id);
        var checks = new List<StartupCheck>();
        try
        {
            var fs = new CharacterFileSystem();
            DateTimeOffset fixedTime = new(2026, 8, 2, 12, 1, 0, TimeSpan.Zero);
            var store = new CharacterStore(fs, root, () => fixedTime);
            Guid id = Guid.Parse("20000000-0000-4000-8000-000000000002");
            Directory.CreateDirectory(store.Paths.Directory(id));
            File.WriteAllText(store.Paths.Primary(id), "not-json");
            File.WriteAllText(store.Paths.Backup(id), "also-not-json");

            CharacterLoadResult loaded = await store.LoadAsync(id, CancellationToken.None);
            string[] quarantines = Directory.GetFiles(store.Paths.Directory(id), "*.invalid-*");
            bool quarantinedBoth = loaded.Status == CharacterLoadStatus.Invalid &&
                quarantines.Length == 2 &&
                loaded.QuarantinedPrimary is not null &&
                loaded.QuarantinedBackup is not null &&
                !File.Exists(store.Paths.Primary(id)) &&
                !File.Exists(store.Paths.Backup(id));
            checks.Add(new StartupCheck("a5_invalid_primary_and_backup_quarantined", quarantinedBoth,
                $"status={loaded.Status} quarantines={quarantines.Length}"));
        }
        finally
        {
            CharacterStoreScenarioSupport.Cleanup(root);
        }

        return CharacterStoreScenarioSupport.Result(checks, $"seed={seed}");
    }
}

public sealed class EditorFutureSchemaFallbackScenario : IScenario
{
    public string Id => "editor_future_schema_fallback";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string root = CharacterStoreScenarioSupport.CreateRoot(Id);
        var checks = new List<StartupCheck>();
        try
        {
            var fs = new CharacterFileSystem();
            var store = new CharacterStore(fs, root,
                () => new DateTimeOffset(2026, 8, 2, 12, 2, 0, TimeSpan.Zero));
            Guid id = Guid.Parse("30000000-0000-4000-8000-000000000003");
            Directory.CreateDirectory(store.Paths.Directory(id));
            string valid = CharacterDocumentPolicy.Serialize(
                CharacterStoreScenarioSupport.Document(id, "Backup"));
            using JsonDocument parsed = JsonDocument.Parse(valid);
            var future = new Dictionary<string, object?>
            {
                ["schemaVersion"] = CharacterDocumentPolicy.CurrentSchemaVersion + 1,
                ["id"] = id,
                ["displayName"] = "Future",
                ["partColors"] = parsed.RootElement.GetProperty("partColors").Clone(),
                ["features"] = parsed.RootElement.GetProperty("features").Clone(),
            };
            string futureJson = JsonSerializer.Serialize(future);
            File.WriteAllText(store.Paths.Primary(id), futureJson);
            File.WriteAllText(store.Paths.Backup(id), valid);

            CharacterLoadResult loaded = await store.LoadAsync(id, CancellationToken.None);
            bool untouched = loaded.Status == CharacterLoadStatus.UnsupportedFutureVersion &&
                File.ReadAllText(store.Paths.Primary(id)) == futureJson &&
                File.ReadAllText(store.Paths.Backup(id)) == valid &&
                Directory.GetFiles(store.Paths.Directory(id), "*.invalid-*").Length == 0;
            checks.Add(new StartupCheck("a5_future_schema_is_not_quarantined", untouched,
                $"status={loaded.Status}"));
        }
        finally
        {
            CharacterStoreScenarioSupport.Cleanup(root);
        }

        return CharacterStoreScenarioSupport.Result(checks, $"seed={seed}");
    }
}

public sealed class LibraryLargeEnumerationScenario : IScenario
{
    public string Id => "library_large_enumeration";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string root = CharacterStoreScenarioSupport.CreateRoot(Id);
        var checks = new List<StartupCheck>();
        try
        {
            var fs = new CharacterFileSystem();
            var paths = new CharacterPaths(root);
            Directory.CreateDirectory(root);
            for (int index = 0; index < 500; index++)
            {
                Guid id = GuidFromIndex(index);
                Directory.CreateDirectory(paths.Directory(id));
                string name = $"Buddy {499 - index:D3}";
                File.WriteAllText(paths.Primary(id), CharacterDocumentPolicy.Serialize(
                    CharacterStoreScenarioSupport.Document(id, name)));
            }
            Directory.CreateDirectory(Path.Combine(root, "not-a-guid"));

            var library = new CharacterLibraryIndex(fs, root);
            IReadOnlyList<CharacterIndexEntry> page = await library.ReadPageAsync(
                20, 25, CancellationToken.None);
            bool pageBounded = page.Count == 25 && page.All(static entry => entry.IsEnabled);
            checks.Add(new StartupCheck("a5_library_page_is_bounded", pageBounded,
                $"rows={page.Count}"));

            bool metadataOnly = library.MetadataReadCount == 500 &&
                library.FullDocumentLoadCount == 0 &&
                library.ThumbnailReadCount == 0 &&
                library.MetadataBytesRead <= 500L * CharacterLibraryIndex.MaximumMetadataBytes;
            checks.Add(new StartupCheck("a5_library_uses_metadata_only", metadataOnly,
                $"reads={library.MetadataReadCount} bytes={library.MetadataBytesRead} " +
                $"full={library.FullDocumentLoadCount} thumbs={library.ThumbnailReadCount}"));

            bool sorted = page.Zip(page.Skip(1), static (left, right) =>
                    StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName) <= 0)
                .All(static value => value);
            checks.Add(new StartupCheck("a5_library_order_deterministic", sorted,
                $"first={page.FirstOrDefault()?.DisplayName} last={page.LastOrDefault()?.DisplayName}"));
        }
        finally
        {
            CharacterStoreScenarioSupport.Cleanup(root);
        }

        return CharacterStoreScenarioSupport.Result(checks, $"seed={seed}");
    }

    private static Guid GuidFromIndex(int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, index + 1);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

public sealed class CharacterStoreTransactionsScenario : IScenario
{
    public string Id => "character_store_transactions";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string root = CharacterStoreScenarioSupport.CreateRoot(Id);
        var checks = new List<StartupCheck>();
        try
        {
            var fs = new CharacterFileSystem();
            var store = new CharacterStore(fs, root);
            Guid sourceId = Guid.Parse("40000000-0000-4000-8000-000000000004");
            CharacterSaveResult saved = await store.SaveAsync(
                CharacterStoreScenarioSupport.Document(sourceId, "Source"), CancellationToken.None);
            CharacterLoadResult source = await store.LoadAsync(sourceId, CancellationToken.None);
            Guid duplicateId = Guid.Parse("50000000-0000-4000-8000-000000000005");
            CharacterDocument duplicate = source.Document! with
            {
                Id = duplicateId,
                DisplayName = "Copy",
            };
            CharacterSaveResult duplicated = await store.SaveAsync(duplicate, CancellationToken.None);
            CharacterLoadResult originalAgain = await store.LoadAsync(sourceId, CancellationToken.None);
            bool independent = saved.IsSuccess && duplicated.IsSuccess &&
                duplicateId != sourceId && originalAgain.Document?.DisplayName == "Source";
            checks.Add(new StartupCheck("a5_duplicate_fresh_guid", independent,
                $"source={sourceId:N} duplicate={duplicateId:N}"));

            CharacterDocument renamed = duplicate with { DisplayName = "Renamed" };
            await store.SaveAsync(renamed, CancellationToken.None);
            bool renameStablePath = Directory.Exists(store.Paths.Directory(duplicateId)) &&
                (await store.LoadAsync(duplicateId, CancellationToken.None)).Document?.DisplayName == "Renamed";
            checks.Add(new StartupCheck("a5_rename_keeps_guid_directory", renameStablePath,
                store.Paths.Directory(duplicateId)));

            CharacterDeleteResult deleted = await store.DeleteAsync(duplicateId, CancellationToken.None);
            bool selectedOnly = deleted.Status == CharacterDeleteStatus.Deleted &&
                !Directory.Exists(store.Paths.Directory(duplicateId)) &&
                Directory.Exists(store.Paths.Directory(sourceId));
            checks.Add(new StartupCheck("a5_delete_selected_directory_only", selectedOnly,
                $"status={deleted.Status}"));

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            CharacterSaveResult cancelledSave = await store.SaveAsync(
                CharacterStoreScenarioSupport.Document(sourceId, "Cancelled"), cancelled.Token);
            CharacterLoadResult afterCancel = await store.LoadAsync(sourceId, CancellationToken.None);
            checks.Add(new StartupCheck("a5_cancel_preserves_primary",
                cancelledSave.Status == CharacterSaveStatus.Cancelled &&
                afterCancel.Document?.DisplayName == "Source",
                $"save={cancelledSave.Status} name={afterCancel.Document?.DisplayName}"));

            Directory.CreateDirectory(Path.Combine(root, "../escape-probe"));
            bool traversalRejected = !store.Paths.IsUnderRoot(Path.Combine(root, "..", "escape-probe"));
            checks.Add(new StartupCheck("a5_path_traversal_rejected", traversalRejected,
                store.Paths.Root));
        }
        finally
        {
            CharacterStoreScenarioSupport.Cleanup(root);
        }

        return CharacterStoreScenarioSupport.Result(checks, $"seed={seed}");
    }
}
