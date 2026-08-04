using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Deterministic Phase B journey core. The production editor controls are covered by the
/// Windows owner-input gate; this core proves the same working-copy, persistence, activation,
/// runtime-binding, and restart boundaries under the journey runner.
/// </summary>
public sealed class CharacterPaintSaveUseRestartScenario : IScenario
{
    public string Id => "character_paint_save_use_restart";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        RuntimePaintTextureBridge? runtimeBridge = null;
        try
        {
            var paintStore = new CharacterPaintStore(new CharacterFileSystem(), context.Root);
            var workspace = new PaintWorkspace();
            await context.Session.AttachPaintingAsync(paintStore, workspace);

            CharacterEditorActionResult created = context.Session.NewCharacter("Paint Journey Buddy");
            Guid id = context.Session.WorkingDocument?.Id ?? Guid.Empty;
            workspace.SelectedColor = new PaintColor(32, 144, 220);
            workspace.AdjustBrush(2);
            workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.48, 0.46), 0));
            workspace.ContinueGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.58, 0.52), 0));
            workspace.EndGesture();
            workspace.SelectedColor = new PaintColor(220, 72, 48);
            workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.42, 0.40), 0));
            workspace.ContinueGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.62, 0.62), 0));
            workspace.EndGesture();

            string headPainted = workspace.Surfaces[PaintPart.Head].ComputeHash();
            string torsoPainted = workspace.Surfaces[PaintPart.Torso].ComputeHash();
            bool paintedTwo = created.Completed && id != Guid.Empty && context.Session.IsDirty &&
                workspace.CanUndo &&
                workspace.Surfaces[PaintPart.Head].Pixels.Span.IndexOfAnyExcept((byte)0) >= 0 &&
                workspace.Surfaces[PaintPart.Torso].Pixels.Span.IndexOfAnyExcept((byte)0) >= 0;
            checks.Add(new StartupCheck(
                "b6_journey_paints_two_parts_and_tracks_dirty",
                paintedTwo,
                $"id={id} dirty={context.Session.IsDirty} undo={workspace.CanUndo}"));

            workspace.SelectedTool = PaintTool.Eraser;
            workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.50, 0.48), 0));
            workspace.EndGesture();
            bool eraseChanged = workspace.Surfaces[PaintPart.Head].ComputeHash() != headPainted;
            bool eraseUndo = workspace.Undo() &&
                workspace.Surfaces[PaintPart.Head].ComputeHash() == headPainted;
            checks.Add(new StartupCheck(
                "b6_journey_eraser_undo_is_exact",
                eraseChanged && eraseUndo,
                $"changed={eraseChanged} undo={eraseUndo}"));

            workspace.EraseAll();
            bool allBlank = workspace.Surfaces.Values.All(surface =>
                surface.Pixels.Span.IndexOfAnyExcept((byte)0) < 0);
            bool eraseAllUndo = workspace.Undo() &&
                workspace.Surfaces[PaintPart.Head].ComputeHash() == headPainted &&
                workspace.Surfaces[PaintPart.Torso].ComputeHash() == torsoPainted;
            checks.Add(new StartupCheck(
                "b6_journey_erase_all_confirmation_result_is_undoable",
                allBlank && eraseAllUndo,
                $"blank={allBlank} undo={eraseAllUndo}"));

            BuddyVisualRigTrustSnapshot trustBefore =
                context.Lab.VisualPresenter.RigView.CaptureTrustSnapshot();
            CharacterEditorActionResult use = await context.Session.UseCharacterAsync();
            bool queuedBeforeTick = use.Completed &&
                context.Coordinator.AppliedCharacterId != id;
            context.Coordinator.PhysicsTick();
            await context.Saves.FlushSelectionImmediatelyAsync();

            runtimeBridge = new RuntimePaintTextureBridge(context.Lab.VisualPresenter.RigView);
            runtimeBridge.Apply(context.Coordinator.AppliedPaintPayload);
            bool activated = queuedBeforeTick &&
                context.Coordinator.AppliedCharacterId == id &&
                context.Selection.ActiveCharacterId == id &&
                !context.Session.IsDirty &&
                context.Coordinator.AppliedPaintPayload.Count == 2 &&
                context.Lab.VisualPresenter.RigView.TrustedGeometryMatches(trustBefore);
            checks.Add(new StartupCheck(
                "b6_journey_save_and_use_activates_exact_paint",
                activated,
                $"queued={queuedBeforeTick} active={context.Coordinator.AppliedCharacterId} parts={context.Coordinator.AppliedPaintPayload.Count}"));

            CharacterPaintLoadResult persisted = await paintStore.LoadAsync(id);
            bool persistedExact = persisted.IsSuccess &&
                persisted.Surfaces.TryGetValue(PaintPart.Head, out byte[]? savedHead) &&
                persisted.Surfaces.TryGetValue(PaintPart.Torso, out byte[]? savedTorso) &&
                savedHead.AsSpan().SequenceEqual(workspace.Surfaces[PaintPart.Head].Pixels.Span) &&
                savedTorso.AsSpan().SequenceEqual(workspace.Surfaces[PaintPart.Torso].Pixels.Span);
            checks.Add(new StartupCheck(
                "b6_journey_saved_pngs_match_editor_pixels",
                persistedExact,
                $"loaded={persisted.IsSuccess} parts={persisted.Surfaces.Count}"));

            var restartSelection = new CharacterSelectionState(id);
            var restartMemory = new InMemoryProgressStore();
            var restartProgress = new DesktopBuddy.Domain.Persistence.BuddyProgressState(1.0);
            var restartSaves = new SaveCoordinator(
                restartProgress,
                restartMemory,
                restartProgress.Revision,
                restartSelection,
                restartSelection.Revision);
            var restartCoordinator = new CharacterSelectionCoordinator(
                context.Store,
                restartSelection,
                context.Lab.VisualPresenter.RigView,
                restartSaves);
            CharacterActivationResult startup = await restartCoordinator.LoadStartupAsync(
                CancellationToken.None);
            restartCoordinator.PhysicsTick();
            runtimeBridge.Apply(restartCoordinator.AppliedPaintPayload);

            bool restartExact = startup.WasQueued &&
                restartCoordinator.AppliedCharacterId == id &&
                restartCoordinator.AppliedPaintPayload.Count == 2 &&
                restartCoordinator.AppliedPaintPayload[PaintPart.Head].AsSpan()
                    .SequenceEqual(workspace.Surfaces[PaintPart.Head].Pixels.Span) &&
                restartCoordinator.AppliedPaintPayload[PaintPart.Torso].AsSpan()
                    .SequenceEqual(workspace.Surfaces[PaintPart.Torso].Pixels.Span) &&
                context.Lab.VisualPresenter.RigView.TrustedGeometryMatches(trustBefore);
            checks.Add(new StartupCheck(
                "b6_journey_restart_restores_selection_pixels_and_rig",
                restartExact,
                $"startup={startup.Status} active={restartCoordinator.AppliedCharacterId}"));
        }
        finally
        {
            runtimeBridge?.Dispose();
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }
}
