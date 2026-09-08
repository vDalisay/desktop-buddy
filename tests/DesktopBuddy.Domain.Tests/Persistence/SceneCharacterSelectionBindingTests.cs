using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SceneCharacterSelectionBindingTests
{
    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot =
        OperatingSystem.IsWindows() ? @"C:\scene-character-selection-test" : "/scene-character-selection-test";

    [Fact]
    public async Task Selection_change_updates_buddy_identity_and_committed_scene_generation()
    {
        Guid originalCharacter = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid replacementCharacter = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var files = new MemoryFiles();
        SceneProgressCoordinator scenes = Coordinator(files, originalCharacter);
        await scenes.FlushAsync(force: true);
        var selection = new CharacterSelectionState(originalCharacter);
        using var binding = new SceneCharacterSelectionBinding(
            scenes,
            BuddyIdentityId.LegacyPrimary,
            selection);

        Assert.True(selection.SetActive(replacementCharacter));
        await binding.FlushAsync();

        Assert.True(scenes.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? buddy));
        Assert.NotNull(buddy);
        Assert.Equal(replacementCharacter, buddy!.CharacterId);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        BuddyIdentityState persisted = Assert.Single(loaded.BuddyIdentities);
        Assert.Equal(BuddyIdentityId.LegacyPrimary, persisted.BuddyIdentityId);
        Assert.Equal(replacementCharacter, persisted.CharacterId);
    }

    [Fact]
    public void Binding_rejects_compatibility_selection_that_disagrees_with_buddy_identity()
    {
        Guid character = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var files = new MemoryFiles();
        SceneProgressCoordinator scenes = Coordinator(files, character);
        var mismatch = new CharacterSelectionState(
            Guid.Parse("44444444-4444-4444-4444-444444444444"));

        Assert.Throws<InvalidOperationException>(() =>
            new SceneCharacterSelectionBinding(
                scenes,
                BuddyIdentityId.LegacyPrimary,
                mismatch));
    }

    private static SceneProgressCoordinator Coordinator(MemoryFiles files, Guid characterId)
    {
        var legacy = new BuddyProgressState(
            cashPerPain: CashPerPain,
            initialBalanceMilliCredits: 100_000);
        LegacyNextFestMigrationProjection projection = LegacyNextFestMigrationPolicy.Project(
            legacy.Snapshot(),
            characterId,
            new EnvironmentProgressSnapshot(0, new EnvironmentLayout(), []),
            new CanonicalRoomPosition(0.5f, 0.5f));
        return new SceneProgressCoordinator(
            BuildScopePolicy.Resolve(false, true, true, false),
            new PlayerProgressState(CashPerPain, projection.Player),
            new WorkProgressState(),
            [new BuddyIdentityState(projection.Buddy)],
            [projection.Scene],
            projection.Scene.SceneId,
            Store(files));
    }

    private static SceneProgressTransactionStore Store(MemoryFiles files) => new(SaveRoot, files);

    private sealed class MemoryFiles : IAtomicSaveFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public void WriteDurable(string path, string contents) => _files[path] = contents;
        public void Replace(string temporary, string primary, string backup)
        {
            _files[backup] = _files[primary];
            _files[primary] = _files[temporary];
            _files.Remove(temporary);
        }
        public void Move(string source, string destination)
        {
            _files[destination] = _files[source];
            _files.Remove(source);
        }
    }
}
