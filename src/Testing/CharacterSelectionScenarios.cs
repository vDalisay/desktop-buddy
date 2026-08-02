using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

internal static class CharacterSelectionScenarioSupport
{
    public static CharacterDocument Character(Guid id, string name, string headColor) =>
        CharacterDocument.CreateDefault(id, name) with
        {
            PartColors = CharacterDocument.CreateDefault(id, name).PartColors with
            {
                Head = Rgba32.Parse(headColor),
            },
        };

    public static async Task<(BuddyLab Lab, string Root, CharacterStore Store)> CreateLabAsync(
        SceneTree tree,
        string scenario)
    {
        PackedScene packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn")
            ?? throw new InvalidOperationException("Missing buddy_lab scene.");
        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        string root = CharacterStoreScenarioSupport.CreateRoot(scenario);
        return (lab, root, new CharacterStore(new CharacterFileSystem(), root));
    }

    public static void Cleanup(BuddyLab lab, string root)
    {
        lab.QueueFree();
        CharacterStoreScenarioSupport.Cleanup(root);
    }

    public static SaveCoordinator Saves(
        CharacterSelectionState selection,
        InMemoryProgressStore store,
        out BuddyProgressState progress)
    {
        progress = new BuddyProgressState(cashPerPain: 1.0);
        return new SaveCoordinator(
            progress,
            store,
            progress.Revision,
            selection,
            selection.Revision);
    }

    public static ScenarioResult Result(IReadOnlyList<StartupCheck> checks, ulong seed) =>
        new(checks.All(static check => check.Passed), checks, [$"seed={seed}"]);
}

public sealed class CharacterSelectionMigrationScenario : IScenario
{
    public string Id => "character_selection_migration";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        ProgressSave v6 = new ProgressSave
        {
            SchemaVersion = 6,
            Revision = 8,
            UnlockedToolIds = [DesktopBuddy.Domain.Content.ContentIds.ToolGrab],
            SelectedToolId = DesktopBuddy.Domain.Content.ContentIds.ToolGrab,
            FunActivities = [],
        };
        string legacyJson = System.Text.Json.JsonSerializer.Serialize(v6);
        SaveDecodeResult decoded = ProgressSavePolicy.Decode(legacyJson);
        bool migrated = decoded.Status == SaveDecodeStatus.Valid &&
            decoded.Save?.SchemaVersion == 7 &&
            decoded.Save.ActiveCharacterId is null;
        checks.Add(new StartupCheck("a6_schema6_migrates_null_selection", migrated,
            $"status={decoded.Status} schema={decoded.Save?.SchemaVersion}"));

        Guid active = Guid.Parse("61000000-0000-4000-8000-000000000001");
        ProgressSave current = (decoded.Save ?? new ProgressSave()) with
        {
            ActiveCharacterId = active,
        };
        SaveDecodeResult roundTrip = ProgressSavePolicy.Decode(
            ProgressSavePolicy.Serialize(current));
        checks.Add(new StartupCheck("a6_selection_roundtrip", roundTrip.Save?.ActiveCharacterId == active,
            $"active={roundTrip.Save?.ActiveCharacterId}"));

        return Task.FromResult(CharacterSelectionScenarioSupport.Result(checks, seed));
    }
}

public sealed class CharacterSwapPhysicsInvariantScenario : IScenario
{
    public string Id => "character_swap_physics_invariant";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, Id);
        try
        {
            Guid id = Guid.Parse("62000000-0000-4000-8000-000000000002");
            await store.SaveAsync(
                CharacterSelectionScenarioSupport.Character(id, "Blue", "#2368D8"),
                CancellationToken.None);
            var selection = new CharacterSelectionState();
            var memory = new InMemoryProgressStore();
            SaveCoordinator saves = CharacterSelectionScenarioSupport.Saves(
                selection, memory, out BuddyProgressState progress);
            var coordinator = new CharacterSelectionCoordinator(
                store, selection, lab.VisualPresenter.RigView, saves);

            BuddyVisualRigTrustSnapshot trusted = lab.VisualPresenter.RigView.CaptureTrustSnapshot();
            var before = new BodyInvariant[PuppetRigProfile.RequiredPartCount];
            for (int index = 0; index < before.Length; index++)
                before[index] = BodyInvariant.Capture(lab.Buddy.Rig.GetPart((BuddyPartId)index));
            ProgressSnapshot progressBefore = progress.Snapshot();

            CharacterActivationResult queued = await coordinator.QueueUseCharacterAsync(id, CancellationToken.None);
            bool beforeTickUnchanged = selection.ActiveCharacterId is null &&
                lab.VisualPresenter.RigView.ActiveAppearance is null;
            coordinator.PhysicsTick();

            bool bodiesEqual = true;
            for (int index = 0; index < before.Length; index++)
                bodiesEqual &= before[index] == BodyInvariant.Capture(lab.Buddy.Rig.GetPart((BuddyPartId)index));
            bool invariant = queued.WasQueued && beforeTickUnchanged && bodiesEqual &&
                lab.VisualPresenter.RigView.TrustedGeometryMatches(trusted) &&
                progress.Snapshot() == progressBefore &&
                selection.ActiveCharacterId == id &&
                lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId == id;
            checks.Add(new StartupCheck("a6_swap_visual_only_at_fixed_tick", invariant,
                $"queued={queued.Status} before_tick={beforeTickUnchanged} bodies={bodiesEqual}"));
        }
        finally
        {
            CharacterSelectionScenarioSupport.Cleanup(lab, root);
        }

        return CharacterSelectionScenarioSupport.Result(checks, seed);
    }

    private readonly record struct BodyInvariant(
        Vector2 Position,
        Vector2 Velocity,
        float Rotation,
        float AngularVelocity,
        float Mass,
        float Radius,
        uint CollisionLayer,
        uint CollisionMask)
    {
        public static BodyInvariant Capture(PuppetPartBody body) => new(
            body.GlobalPosition,
            body.LinearVelocity,
            body.GlobalRotation,
            body.AngularVelocity,
            body.Mass,
            body.Radius,
            body.CollisionLayer,
            body.CollisionMask);
    }
}

public sealed class CharacterSelectionImmediateSaveScenario : IScenario
{
    public string Id => "character_selection_immediate_save";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, Id);
        try
        {
            Guid first = Guid.Parse("63000000-0000-4000-8000-000000000003");
            Guid second = Guid.Parse("63000000-0000-4000-8000-000000000004");
            await store.SaveAsync(CharacterSelectionScenarioSupport.Character(first, "First", "#D04444"), CancellationToken.None);
            await store.SaveAsync(CharacterSelectionScenarioSupport.Character(second, "Second", "#44A060"), CancellationToken.None);
            var selection = new CharacterSelectionState();
            var memory = new InMemoryProgressStore();
            SaveCoordinator saves = CharacterSelectionScenarioSupport.Saves(selection, memory, out _);
            var coordinator = new CharacterSelectionCoordinator(
                store, selection, lab.VisualPresenter.RigView, saves);

            await coordinator.QueueUseCharacterAsync(first, CancellationToken.None);
            await coordinator.QueueUseCharacterAsync(second, CancellationToken.None);
            coordinator.PhysicsTick();
            await saves.FlushSelectionImmediatelyAsync();

            bool lastWins = selection.ActiveCharacterId == second &&
                coordinator.AppliedCharacterId == second &&
                lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId == second;
            bool saved = memory.ProgressWriteCount >= 1 &&
                memory.Progress?.ActiveCharacterId == second && !saves.IsDirty;
            checks.Add(new StartupCheck("a6_last_request_wins", lastWins,
                $"selection={selection.ActiveCharacterId} applied={coordinator.AppliedCharacterId}"));
            checks.Add(new StartupCheck("a6_selection_immediate_save", saved,
                $"writes={memory.ProgressWriteCount} saved={memory.Progress?.ActiveCharacterId}"));
        }
        finally
        {
            CharacterSelectionScenarioSupport.Cleanup(lab, root);
        }

        return CharacterSelectionScenarioSupport.Result(checks, seed);
    }
}

public sealed class CharacterSelectionSaveFailureDirtyScenario : IScenario
{
    public string Id => "character_selection_save_failure_dirty";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, Id);
        try
        {
            Guid id = Guid.Parse("64000000-0000-4000-8000-000000000005");
            await store.SaveAsync(CharacterSelectionScenarioSupport.Character(id, "Failure", "#8855CC"), CancellationToken.None);
            var selection = new CharacterSelectionState();
            var memory = new InMemoryProgressStore
            {
                NextProgressFailure = new IOException("injected selection save failure"),
            };
            SaveCoordinator saves = CharacterSelectionScenarioSupport.Saves(selection, memory, out _);
            var coordinator = new CharacterSelectionCoordinator(
                store, selection, lab.VisualPresenter.RigView, saves);

            await coordinator.QueueUseCharacterAsync(id, CancellationToken.None);
            coordinator.PhysicsTick();
            for (int index = 0; index < 8 && saves.LastFailure is null; index++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            bool activeDespiteFailure = selection.ActiveCharacterId == id &&
                lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId == id;
            bool dirty = saves.LastFailure is IOException && saves.IsDirty;
            checks.Add(new StartupCheck("a6_save_failure_keeps_active_appearance", activeDespiteFailure,
                $"selection={selection.ActiveCharacterId}"));
            checks.Add(new StartupCheck("a6_save_failure_remains_dirty", dirty,
                $"failure={saves.LastFailure?.GetType().Name} dirty={saves.IsDirty}"));
        }
        finally
        {
            CharacterSelectionScenarioSupport.Cleanup(lab, root);
        }

        return CharacterSelectionScenarioSupport.Result(checks, seed);
    }
}

public sealed class CharacterActiveDeleteRevertsScenario : IScenario
{
    public string Id => "character_active_delete_reverts";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, Id);
        try
        {
            Guid id = Guid.Parse("65000000-0000-4000-8000-000000000006");
            await store.SaveAsync(CharacterSelectionScenarioSupport.Character(id, "Delete", "#AA7744"), CancellationToken.None);
            var selection = new CharacterSelectionState();
            var memory = new InMemoryProgressStore();
            SaveCoordinator saves = CharacterSelectionScenarioSupport.Saves(selection, memory, out _);
            var coordinator = new CharacterSelectionCoordinator(
                store, selection, lab.VisualPresenter.RigView, saves);
            await coordinator.QueueUseCharacterAsync(id, CancellationToken.None);
            coordinator.PhysicsTick();
            await saves.FlushSelectionImmediatelyAsync();

            CharacterDeleteResult deleted = await coordinator.DeleteCharacterAsync(id, CancellationToken.None);
            coordinator.PhysicsTick();
            await saves.FlushSelectionImmediatelyAsync();
            bool reverted = deleted.IsSuccess && selection.ActiveCharacterId is null &&
                lab.VisualPresenter.RigView.ActiveAppearance is null &&
                memory.Progress?.ActiveCharacterId is null &&
                !Directory.Exists(store.Paths.Directory(id));
            checks.Add(new StartupCheck("a6_delete_active_reverts_builtin", reverted,
                $"delete={deleted.Status} selection={selection.ActiveCharacterId}"));
        }
        finally
        {
            CharacterSelectionScenarioSupport.Cleanup(lab, root);
        }

        return CharacterSelectionScenarioSupport.Result(checks, seed);
    }
}

public sealed class CharacterSelectionFallbackScenario : IScenario
{
    public string Id => "character_selection_fallback";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, Id);
        try
        {
            Guid missing = Guid.Parse("66000000-0000-4000-8000-000000000007");
            var selection = new CharacterSelectionState(missing);
            var memory = new InMemoryProgressStore();
            SaveCoordinator saves = CharacterSelectionScenarioSupport.Saves(
                selection, memory, out BuddyProgressState resetProgress);
            var coordinator = new CharacterSelectionCoordinator(
                store, selection, lab.VisualPresenter.RigView, saves);

            CharacterActivationResult result = await coordinator.LoadStartupAsync(CancellationToken.None);
            coordinator.PhysicsTick();
            bool preserved = result.Status == CharacterActivationStatus.NotFoundFallback &&
                selection.ActiveCharacterId == missing &&
                coordinator.AppliedCharacterId is null &&
                lab.VisualPresenter.RigView.ActiveAppearance is null &&
                memory.ProgressWriteCount == 0;
            checks.Add(new StartupCheck("a6_startup_fallback_preserves_selection", preserved,
                $"status={result.Status} selected={selection.ActiveCharacterId} writes={memory.ProgressWriteCount}"));

            CharacterSelectionSnapshot beforeReset = selection.Snapshot();
            memory.NextProgressFailure = new IOException("reset failure");
            bool reset = await ProgressReset.ResetAsync(
                resetProgress, saves,
                characterSelection: selection);
            bool rollback = !reset && selection.Snapshot() == beforeReset;
            checks.Add(new StartupCheck("a6_reset_failure_restores_selection", rollback,
                $"reset={reset} selected={selection.ActiveCharacterId} revision={selection.Revision}"));
        }
        finally
        {
            CharacterSelectionScenarioSupport.Cleanup(lab, root);
        }

        return CharacterSelectionScenarioSupport.Result(checks, seed);
    }
}
