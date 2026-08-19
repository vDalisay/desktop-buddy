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
/// Deterministic Phase B journey core. Pointer strokes, wheel sizing, erasing, and keyboard
/// Undo enter through Godot's real input queue and the production PaintCanvasControl. Semantic
/// assertions then prove working-copy, persistence, activation, runtime binding, and restart.
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
        PaintCanvasControl? canvas = null;
        CanvasLayer? canvasLayer = null;
        try
        {
            var paintStore = new CharacterPaintStore(new CharacterFileSystem(), context.Root);
            canvas = new PaintCanvasControl
            {
                Name = "JourneyPaintCanvas",
                Position = Vector2.Zero,
                Size = new Vector2(420, 360),
                ViewportSize = new Vector2(420, 360),
                MouseFilter = Control.MouseFilterEnum.Stop,
                FocusMode = Control.FocusModeEnum.All,
                ZIndex = 4096,
            };
            // The lab's telemetry UI covers the viewport, and GUI picking honours canvas layers
            // before ZIndex, so the journey canvas needs its own layer above it to receive input.
            canvasLayer = new CanvasLayer { Name = "JourneyPaintLayer", Layer = 128 };
            tree.Root.AddChild(canvasLayer);
            canvasLayer.AddChild(canvas);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            canvas.GrabFocus();

            PaintWorkspace workspace = canvas.Workspace;
            await context.Session.AttachPaintingAsync(paintStore, workspace);
            CharacterEditorActionResult created = context.Session.NewCharacter("Paint Journey Buddy");
            Guid id = context.Session.WorkingDocument?.Id ?? Guid.Empty;

            int brushBeforeWheel = workspace.BrushDiameter;
            await SendWheel(tree, new Vector2(210, 135), up: true);
            int brushAfterWheel = workspace.BrushDiameter;

            workspace.SelectedColor = new PaintColor(32, 144, 220);
            await Stroke(tree, new Vector2(210, 135), new Vector2(220, 140));
            workspace.SelectedColor = new PaintColor(220, 72, 48);
            await Stroke(tree, new Vector2(210, 180), new Vector2(222, 190));

            string headPainted = workspace.Surfaces[PaintPart.Head].ComputeHash();
            string torsoPainted = workspace.Surfaces[PaintPart.Torso].ComputeHash();
            bool paintedTwo = created.Completed && id != Guid.Empty && context.Session.IsDirty &&
                workspace.CanUndo &&
                workspace.Surfaces[PaintPart.Head].Pixels.Span.IndexOfAnyExcept((byte)0) >= 0 &&
                workspace.Surfaces[PaintPart.Torso].Pixels.Span.IndexOfAnyExcept((byte)0) >= 0;
            checks.Add(new StartupCheck(
                "b6_journey_routes_pointer_and_wheel_through_input",
                brushAfterWheel > brushBeforeWheel && paintedTwo,
                $"brush={brushBeforeWheel}->{brushAfterWheel} head={canvas.PartAt(new Vector2(210, 135))} torso={canvas.PartAt(new Vector2(210, 180))}"));
            checks.Add(new StartupCheck(
                "b6_journey_paints_two_parts_and_tracks_dirty",
                paintedTwo,
                $"id={id} dirty={context.Session.IsDirty} undo={workspace.CanUndo}"));

            workspace.SelectedTool = PaintTool.Eraser;
            await Stroke(tree, new Vector2(210, 135), new Vector2(214, 138));
            bool eraseChanged = workspace.Surfaces[PaintPart.Head].ComputeHash() != headPainted;
            await SendUndo(tree);
            bool eraseUndo = workspace.Surfaces[PaintPart.Head].ComputeHash() == headPainted;
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
            // --- the eraser's footprint, measured in SCREEN space through the real mapping ---
            // A footprint stamped as one shape onto the surface comes out stretched and rotated
            // by whatever the surface is doing underneath it; the tools that promise a shape
            // build it out of screen-space samples instead (owner report 2026-08-19).
            var covered = new byte[PaintPolicy.SurfaceBytes];
            System.Array.Fill(covered, byte.MaxValue);
            foreach (PaintPart part in System.Enum.GetValues<PaintPart>())
                workspace.Load(part, covered);

            var target = new Vector2(210, 180);
            workspace.SelectedTool = PaintTool.Eraser;
            workspace.SetBrushDiameter(64);
            await Stroke(tree, target, target);
            float reach = canvas.VisibleBrushDiameterForPresentation * 0.5f;

            bool squareOnScreen = reach > 2f &&
                canvas.HitAt(target) is not null &&
                ErasedAt(canvas, workspace, target) &&
                ErasedAt(canvas, workspace, target + new Vector2(reach * 0.75f, 0)) &&
                ErasedAt(canvas, workspace, target + new Vector2(0, reach * 0.75f)) &&
                // The corner: inside a square, well outside the inscribed circle at 1.2 radii.
                ErasedAt(canvas, workspace, target + new Vector2(reach * 0.85f, reach * 0.85f)) &&
                !ErasedAt(canvas, workspace, target + new Vector2(reach * 1.6f, 0)) &&
                !ErasedAt(canvas, workspace, target + new Vector2(0, reach * 1.6f));
            checks.Add(new StartupCheck(
                "b6_journey_eraser_footprint_is_square_in_screen_space",
                squareOnScreen,
                $"reach={reach:F1} centre={ErasedAt(canvas, workspace, target)} " +
                $"corner={ErasedAt(canvas, workspace, target + new Vector2(reach * 0.85f, reach * 0.85f))} " +
                $"outside={ErasedAt(canvas, workspace, target + new Vector2(reach * 1.6f, 0))}"));
        }
        finally
        {
            runtimeBridge?.Dispose();
            canvas?.QueueFree();
            canvasLayer?.QueueFree();
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }

    /// <summary>
    /// Headless has no mouse device, so <see cref="Input.ParseInputEvent"/> never dispatches
    /// pointer events (keys still do). Pushing them at the root viewport enters the same GUI
    /// pipeline the platform layer feeds, so production _GuiInput handling is still what runs.
    /// </summary>
    private static void SendMouse(SceneTree tree, InputEvent input) => tree.Root.PushInput(input);

    /// <summary>
    /// One press/drag/release, delivered inside a single frame. Headless reports no real
    /// cursor, so letting _Process run mid-stroke would paint towards (0, 0) and make the
    /// stroke unreproducible; a real drag can legitimately arrive within one frame.
    /// </summary>
    /// <summary>
    /// Whether the surface pixel under a canvas point has been erased, resolved through the same
    /// hit mapping the stroke itself uses — which is what makes this a screen-space measurement
    /// rather than a surface-space one.
    /// </summary>
    private static bool ErasedAt(PaintCanvasControl canvas, PaintWorkspace workspace, Vector2 point)
    {
        if (canvas.HitAt(point) is not PaintHit hit || !hit.IsValid)
            return false;
        PaintUvRegion region = PaintUvRegion.For(hit);
        int x = Math.Clamp(
            (int)Math.Round(region.PixelX(hit.Uv.X)),
            0,
            PaintPolicy.SurfaceSize - 1);
        int y = Math.Clamp(
            (int)Math.Round(hit.Uv.Y * (PaintPolicy.SurfaceSize - 1)),
            0,
            PaintPolicy.SurfaceSize - 1);
        byte[] pixels = workspace.Surfaces[hit.Part].Capture(new PaintRect(x, y, 1, 1));
        return pixels.Length == PaintPolicy.BytesPerPixel && pixels[3] == 0;
    }

    private static async Task Stroke(SceneTree tree, Vector2 from, Vector2 to)
    {
        SendMouse(tree, new InputEventMouseMotion
        {
            Position = from,
            GlobalPosition = from,
        });
        SendMouse(tree, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left,
            Pressed = true,
            Position = from,
            GlobalPosition = from,
        });
        SendMouse(tree, new InputEventMouseMotion
        {
            ButtonMask = MouseButtonMask.Left,
            Position = to,
            GlobalPosition = to,
            Relative = to - from,
        });
        SendMouse(tree, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = to,
            GlobalPosition = to,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static async Task SendWheel(SceneTree tree, Vector2 position, bool up)
    {
        MouseButton button = up ? MouseButton.WheelUp : MouseButton.WheelDown;
        SendMouse(tree, new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = true,
            Factor = 1.0f,
            Position = position,
            GlobalPosition = position,
        });
        SendMouse(tree, new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = false,
            Position = position,
            GlobalPosition = position,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static async Task SendUndo(SceneTree tree)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = Key.Z,
            PhysicalKeycode = Key.Z,
            CtrlPressed = true,
            Pressed = true,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = Key.Z,
            PhysicalKeycode = Key.Z,
            CtrlPressed = true,
            Pressed = false,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
