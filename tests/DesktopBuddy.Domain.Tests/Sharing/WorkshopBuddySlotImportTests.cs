using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Sharing;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sharing;

public sealed class WorkshopBuddySlotImportTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "desktop-buddy-workshop-slot-tests",
        Guid.NewGuid().ToString("N"));

    public WorkshopBuddySlotImportTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Full_capacity_rejects_new_workshop_buddy_before_local_commit()
    {
        WorkshopStagingStore staging = new(Path.Combine(_root, "staging-new"));
        CharacterStore source = NewStore("source-new");
        CharacterStore target = NewStore("target-new");
        Guid sourceId = Guid.Parse("a1000000-0000-4000-8000-000000000001");
        await source.SaveAsync(CharacterDocument.CreateDefault(sourceId, "Workshop Buddy"), CancellationToken.None);
        WorkshopIncomingStaging incoming = await ExportIncomingAsync(staging, source, sourceId, "new");
        var importer = new CharacterShareImporter(
            staging,
            target,
            canCreateNewCharacter: () => false);

        CharacterShareImportResult result = await importer.ImportStagedAsync(
            incoming,
            new WorkshopImportSource(9001, 123, "Workshop Buddy"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.LocalCharacterId);
        Assert.Equal(0, target.CountStoredCharacters());
        Assert.Contains("No free buddy slot", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(incoming.OperationRoot));
    }

    [Fact]
    public async Task Full_capacity_can_apply_workshop_skin_to_existing_buddy_without_consuming_slot()
    {
        WorkshopStagingStore staging = new(Path.Combine(_root, "staging-replace"));
        CharacterStore source = NewStore("source-replace");
        CharacterStore target = NewStore("target-replace");
        Guid sourceId = Guid.Parse("a2000000-0000-4000-8000-000000000001");
        Guid currentId = Guid.Parse("a2000000-0000-4000-8000-000000000002");

        CharacterDocument sourceDocument = CharacterDocument.CreateDefault(sourceId, "Workshop Buddy") with
        {
            PartColors = new CharacterPartColors
            {
                Head = Rgba32.Parse("#FF2233"),
                Torso = Rgba32.Parse("#44AA66"),
            },
        };
        await source.SaveAsync(sourceDocument, CancellationToken.None);
        await target.SaveAsync(CharacterDocument.CreateDefault(currentId, "Keep My Name"), CancellationToken.None);
        WorkshopIncomingStaging incoming = await ExportIncomingAsync(staging, source, sourceId, "replace");
        var importer = new CharacterShareImporter(
            staging,
            target,
            canCreateNewCharacter: () => false);

        CharacterShareImportResult result = await importer.ImportStagedAsync(
            incoming,
            new WorkshopImportSource(9002, 456, "Workshop Buddy"),
            currentId,
            CancellationToken.None);

        Assert.True(result.Success, result.Detail);
        Assert.Equal(currentId, result.LocalCharacterId);
        Assert.Equal(1, target.CountStoredCharacters());
        CharacterLoadResult loaded = await target.LoadAsync(currentId, CancellationToken.None);
        Assert.True(loaded.IsSuccess, loaded.Detail);
        Assert.NotNull(loaded.Document);
        Assert.Equal("Keep My Name", loaded.Document!.DisplayName);
        Assert.Equal(Rgba32.Parse("#FF2233"), loaded.Document.PartColors.Head);
        Assert.Equal(Rgba32.Parse("#44AA66"), loaded.Document.PartColors.Torso);
        Assert.False(Directory.Exists(incoming.OperationRoot));
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

    private CharacterStore NewStore(string name) => new(
        new CharacterFileSystem(),
        Path.Combine(_root, name));

    private static async Task<WorkshopIncomingStaging> ExportIncomingAsync(
        WorkshopStagingStore staging,
        CharacterStore source,
        Guid sourceId,
        string suffix)
    {
        Guid publishId = Guid.NewGuid();
        var exporter = new CharacterShareExporter(staging, source, "slot-test");
        ShareExportResult exported = await exporter.ExportAsync(
            sourceId,
            publishId,
            previewPng: null,
            CancellationToken.None);
        Assert.True(exported.Success, exported.Detail);
        Assert.NotNull(exported.Staging);

        Guid incomingId = Guid.NewGuid();
        WorkshopIncomingStaging incoming = staging.SnapshotIncoming(
            exported.Staging!.Value.ContentRoot,
            incomingId,
            CancellationToken.None);
        staging.Cleanup(publishId);
        return incoming;
    }
}
