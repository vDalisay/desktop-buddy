using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

internal sealed class FailOnceCharacterFileSystem : ICharacterFileSystem
{
    private readonly ICharacterFileSystem _inner;
    private readonly Func<string, string, bool> _shouldFailMove;
    private bool _failed;

    public FailOnceCharacterFileSystem(
        ICharacterFileSystem inner,
        Func<string, string, bool> shouldFailMove)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _shouldFailMove = shouldFailMove ?? throw new ArgumentNullException(nameof(shouldFailMove));
    }

    public bool FailureTriggered => _failed;

    public bool FileExists(string path) => _inner.FileExists(path);
    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
    public void CreateDirectory(string path) => _inner.CreateDirectory(path);
    public IReadOnlyList<string> EnumerateDirectories(string path) => _inner.EnumerateDirectories(path);
    public string ReadAllText(string path) => _inner.ReadAllText(path);
    public byte[] ReadPrefix(string path, int maximumBytes) => _inner.ReadPrefix(path, maximumBytes);
    public byte[] ReadAllBytes(string path, int maximumBytes) => _inner.ReadAllBytes(path, maximumBytes);
    public void WriteAllTextDurable(string path, string content) => _inner.WriteAllTextDurable(path, content);
    public void WriteAllBytesDurable(string path, ReadOnlySpan<byte> content) => _inner.WriteAllBytesDurable(path, content);
    public void ReplaceFileWithBackup(string temporaryPath, string primaryPath, string backupPath) =>
        _inner.ReplaceFileWithBackup(temporaryPath, primaryPath, backupPath);
    public void MoveFile(string sourcePath, string destinationPath) => _inner.MoveFile(sourcePath, destinationPath);

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        if (!_failed && _shouldFailMove(sourcePath, destinationPath))
        {
            _failed = true;
            throw new IOException("Injected paint transaction commit failure.");
        }
        _inner.MoveDirectory(sourcePath, destinationPath);
    }

    public void DeleteFile(string path) => _inner.DeleteFile(path);
    public void DeleteDirectory(string path, bool recursive) => _inner.DeleteDirectory(path, recursive);
    public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);
    public bool IsReparsePoint(string path) => _inner.IsReparsePoint(path);
}

public sealed class PaintSaveFailurePreservesWorkingCopyScenario : IScenario
{
    public string Id => "paint_save_failure_preserves_working_copy";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            Guid id = Guid.Parse("63000000-0000-4000-8000-000000000063");
            var normalPaintStore = new CharacterPaintStore(new CharacterFileSystem(), context.Root);
            Dictionary<PaintPart, ReadOnlyMemory<byte>> baseline =
                PaintingScenarioSupport.Painted(PaintPart.Head);
            CharacterPaintSaveResult baselineSave = await normalPaintStore.SaveAsync(
                CharacterDocument.CreateDefault(id, "Failure Safety"), baseline);

            var faultFileSystem = new FailOnceCharacterFileSystem(
                new CharacterFileSystem(),
                static (source, destination) =>
                    source.EndsWith(".paint-staging", StringComparison.Ordinal) &&
                    !destination.EndsWith(".paint-previous", StringComparison.Ordinal));
            var faultPaintStore = new CharacterPaintStore(faultFileSystem, context.Root);
            var workspace = new PaintWorkspace();
            await context.Session.AttachPaintingAsync(faultPaintStore, workspace);
            CharacterEditorActionResult selected = await context.Session.SelectAsync(id);

            string diskHashBefore = (await normalPaintStore.LoadAsync(id)).Surfaces[PaintPart.Head]
                .AsSpan().ToArray().Aggregate(17, static (hash, value) => unchecked(hash * 31 + value)).ToString();
            string savedHash = workspace.Surfaces[PaintPart.Head].ComputeHash();
            workspace.SelectedColor = new PaintColor(220, 40, 70);
            workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
            workspace.EndGesture();
            string workingHashBeforeSave = workspace.Surfaces[PaintPart.Torso].ComputeHash();

            CharacterEditorActionResult failed = await context.Session.SaveAsync();
            string workingHashAfterFailure = workspace.Surfaces[PaintPart.Torso].ComputeHash();
            CharacterPaintLoadResult diskAfterFailure = await normalPaintStore.LoadAsync(id);
            string diskHashAfter = diskAfterFailure.Surfaces[PaintPart.Head]
                .AsSpan().ToArray().Aggregate(17, static (hash, value) => unchecked(hash * 31 + value)).ToString();

            checks.Add(new StartupCheck(
                "phase_b_save_failure_is_reported",
                baselineSave.IsSuccess && selected.Completed && !failed.Completed && faultFileSystem.FailureTriggered,
                failed.Detail ?? "injected failure"));
            checks.Add(new StartupCheck(
                "phase_b_save_failure_preserves_working_pixels_dirty_and_undo",
                workingHashAfterFailure == workingHashBeforeSave && context.Session.IsDirty && workspace.CanUndo,
                $"working={workingHashAfterFailure} dirty={context.Session.IsDirty} undo={workspace.CanUndo}"));
            checks.Add(new StartupCheck(
                "phase_b_save_failure_preserves_previous_disk_state",
                diskAfterFailure.IsSuccess && diskHashAfter == diskHashBefore &&
                diskAfterFailure.Surfaces.Count == 1 && diskAfterFailure.Surfaces.ContainsKey(PaintPart.Head),
                $"before={diskHashBefore} after={diskHashAfter}"));

            bool undoRestored = workspace.Undo() &&
                workspace.Surfaces[PaintPart.Torso].ComputeHash() != workingHashBeforeSave &&
                workspace.Surfaces[PaintPart.Head].ComputeHash() == savedHash;
            checks.Add(new StartupCheck(
                "phase_b_save_failure_keeps_byte_exact_undo",
                undoRestored,
                $"canUndo={workspace.CanUndo}"));

            workspace.SelectedColor = new PaintColor(220, 40, 70);
            workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
            workspace.EndGesture();
            CharacterEditorActionResult retried = await context.Session.SaveAsync();
            CharacterPaintLoadResult loadedRetry = await normalPaintStore.LoadAsync(id);
            checks.Add(new StartupCheck(
                "phase_b_failed_save_can_retry_successfully",
                retried.Completed && !context.Session.IsDirty && loadedRetry.IsSuccess &&
                loadedRetry.Surfaces.TryGetValue(PaintPart.Torso, out byte[]? retryTorso) &&
                retryTorso.AsSpan().SequenceEqual(workspace.Surfaces[PaintPart.Torso].Pixels.Span),
                $"retry={retried.Completed} dirty={context.Session.IsDirty}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }
        return CharacterEditorScenarioSupport.Result(checks, seed);
    }
}

public sealed class PaintRuntimeFidelityScenario : IScenario
{
    public string Id => "paint_runtime_fidelity";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            Guid id = Guid.Parse("64000000-0000-4000-8000-000000000064");
            var paintStore = new CharacterPaintStore(new CharacterFileSystem(), context.Root);
            Dictionary<PaintPart, ReadOnlyMemory<byte>> source = PaintingScenarioSupport.Painted(
                PaintPart.Head, PaintPart.Torso, PaintPart.LeftHand);
            CharacterPaintSaveResult saved = await paintStore.SaveAsync(
                CharacterDocument.CreateDefault(id, "Runtime Fidelity"), source);
            CharacterPaintLoadResult loaded = await paintStore.LoadAsync(id);

            bool decodedExact = saved.IsSuccess && loaded.IsSuccess && loaded.Surfaces.Count == source.Count &&
                source.All(pair => loaded.Surfaces.TryGetValue(pair.Key, out byte[]? bytes) &&
                    bytes.AsSpan().SequenceEqual(pair.Value.Span));
            checks.Add(new StartupCheck(
                "phase_b_runtime_fidelity_persisted_bytes_exact",
                decodedExact,
                $"parts={loaded.Surfaces.Count}"));

            BuddyVisualRigTrustSnapshot trustBefore = context.Lab.VisualPresenter.RigView.CaptureTrustSnapshot();
            long sequenceBefore = context.Coordinator.AppliedSequence;
            CharacterActivationResult queued = await context.Coordinator.QueueUseCharacterAsync(id, CancellationToken.None);
            checks.Add(new StartupCheck(
                "phase_b_runtime_paint_not_applied_before_fixed_tick",
                queued.WasQueued && context.Coordinator.AppliedSequence == sequenceBefore &&
                context.Coordinator.AppliedCharacterId != id,
                $"queued={queued.Status} sequence={context.Coordinator.AppliedSequence}"));

            context.Coordinator.PhysicsTick();
            bool payloadExact = context.Coordinator.AppliedCharacterId == id &&
                context.Coordinator.AppliedPaintSequence == context.Coordinator.AppliedSequence &&
                source.All(pair => context.Coordinator.AppliedPaintPayload.TryGetValue(pair.Key, out byte[]? bytes) &&
                    bytes.AsSpan().SequenceEqual(pair.Value.Span));
            checks.Add(new StartupCheck(
                "phase_b_runtime_activation_payload_exact",
                payloadExact,
                $"sequence={context.Coordinator.AppliedPaintSequence} parts={context.Coordinator.AppliedPaintPayload.Count}"));

            var bridge = new RuntimePaintTextureBridge(context.Lab.VisualPresenter.RigView);
            bridge.Apply(context.Coordinator.AppliedPaintPayload);
            long firstUploads = bridge.UploadCount;
            bridge.Apply(context.Coordinator.AppliedPaintPayload);
            long equalUploads = bridge.UploadCount;

            var changed = context.Coordinator.AppliedPaintPayload.ToDictionary(
                pair => pair.Key, pair => (byte[])pair.Value.Clone());
            changed[PaintPart.Head][0] ^= 0x7F;
            bridge.Apply(changed);
            long changedUploads = bridge.UploadCount;
            changed.Remove(PaintPart.Torso);
            bridge.Apply(changed);

            checks.Add(new StartupCheck(
                "phase_b_runtime_uploads_are_deduplicated_and_part_scoped",
                firstUploads == source.Count && equalUploads == firstUploads && changedUploads == firstUploads + 1,
                $"first={firstUploads} equal={equalUploads} changed={changedUploads}"));
            checks.Add(new StartupCheck(
                "phase_b_runtime_paint_does_not_mutate_source_or_rig",
                source[PaintPart.Head].Span.SequenceEqual(loaded.Surfaces[PaintPart.Head]) &&
                context.Lab.VisualPresenter.RigView.CaptureTrustSnapshot().Equals(trustBefore),
                "CPU bytes and trusted rig snapshot unchanged"));

            MeshInstance3D head = context.Lab.VisualPresenter.RigView.GetPartMesh(Buddy.Physics.BuddyPartId.Head);
            MeshInstance3D? paintLayer = head.GetParent().GetNodeOrNull<MeshInstance3D>("Paint");
            checks.Add(new StartupCheck(
                "phase_b_runtime_paint_remains_under_decals",
                paintLayer is { Visible: true } &&
                (context.Lab.VisualPresenter.RigView.FacePlate is null ||
                 context.Lab.VisualPresenter.RigView.FacePlate.Position.Z > BuddyLookMaterialLibrary.PaintShellGrowAmount),
                $"paint={paintLayer?.Visible} faceZ={context.Lab.VisualPresenter.RigView.FacePlate?.Position.Z}"));
            bridge.Dispose();
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }
        return CharacterEditorScenarioSupport.Result(checks, seed);
    }
}
