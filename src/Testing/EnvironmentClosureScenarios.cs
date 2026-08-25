using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Environment;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Sandbox;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// ED6 catalogue/render gate. This replaces the original six-item vertical-slice assumptions with
/// the launch requirement: every category has at least two clean-room, validated, visual-only items.
/// </summary>
public sealed class EnvironmentTrustedDefinitionsClosureScenario(string id = "environment_trusted_definitions") : IScenario
{
    public string Id => id;

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        EnvironmentDecorationCatalogueResource authored = EnvironmentDecorationRegistry.Authored;
        EnvironmentDecorationResource[] entries = authored.Entries
            .Where(static entry => GodotObject.IsInstanceValid(entry))
            .ToArray();
        DecorationDefinition[] visible = entries
            .Select(static entry => entry.ToDefinition())
            .Where(static definition => definition.Visible)
            .ToArray();
        DecorationCategory[] expectedCategories = Enum.GetValues<DecorationCategory>();
        DecorationCategory[] actualCategories = visible
            .Select(static definition => definition.Category)
            .Distinct()
            .OrderBy(static category => (int)category)
            .ToArray();

        checks.Add(new StartupCheck(
            "environment_launch_catalogue_valid",
            authored.Validate().Count == 0 && visible.Length >= expectedCategories.Length * 2,
            $"visible={visible.Length} validationErrors={authored.Validate().Count}"));
        checks.Add(new StartupCheck(
            "environment_launch_has_two_per_category",
            expectedCategories.All(category => visible.Count(item => item.Category == category) >= 2),
            string.Join(", ", expectedCategories.Select(category =>
                $"{category}={visible.Count(item => item.Category == category)}"))));
        checks.Add(new StartupCheck(
            "environment_launch_categories_complete",
            actualCategories.SequenceEqual(expectedCategories),
            string.Join(",", actualCategories)));
        checks.Add(new StartupCheck(
            "environment_launch_ids_unique",
            visible.Select(static definition => definition.Id.Value)
                .Distinct(StringComparer.Ordinal).Count() == visible.Length,
            $"visible={visible.Length}"));

        bool visualOnly = true;
        bool bandsBounded = true;
        bool previewsValid = true;
        var host = new Node3D { Name = "EnvironmentClosureVisualHost" };
        tree.Root.AddChild(host);
        try
        {
            int ordinal = 1;
            foreach (DecorationDefinition definition in visible)
            {
                EnvironmentDecorationResource resource = EnvironmentDecorationRegistry.Find(definition.Id)
                    ?? throw new InvalidOperationException($"Missing authored visual for {definition.Id.Value}.");
                Texture2D preview = EnvironmentDecorationVisualFactory.CreatePreview(resource);
                previewsValid &= preview.GetWidth() == 48 && preview.GetHeight() == 48;

                var presenter = new EnvironmentDecorationPresenter { Name = $"ClosureDecoration{ordinal}" };
                host.AddChild(presenter);
                var placed = new PlacedDecoration(
                    IdFor(ordinal++),
                    definition.Id,
                    new CanonicalRoomPosition(.5f, .5f),
                    0,
                    definition.RenderBand,
                    definition.PriceMilliCredits);
                presenter.Configure(placed, resource, new Vector2(480, 360));
                visualOnly &= CountPhysics(presenter) == 0;
                bandsBounded &= Mathf.IsEqualApprox(
                    presenter.Position.Z,
                    EnvironmentDecorationPresenter.ZFor(definition.RenderBand));
            }
        }
        finally
        {
            host.Free();
        }

        checks.Add(new StartupCheck(
            "environment_launch_previews_render",
            previewsValid,
            "48x48 semantic previews"));
        checks.Add(new StartupCheck(
            "environment_launch_visuals_are_non_physical",
            visualOnly,
            "no physics or collision nodes"));
        checks.Add(new StartupCheck(
            "environment_launch_render_bands_are_bounded",
            bandsBounded,
            "presenter z matches trusted definition band"));

        return Task.FromResult(new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]));
    }

    private static int CountPhysics(Node node)
    {
        int count = node is CollisionObject2D or CollisionShape2D or Joint2D or
            CollisionObject3D or CollisionShape3D or CollisionPolygon3D ? 1 : 0;
        foreach (Node child in node.GetChildren()) count += CountPhysics(child);
        return count;
    }

    internal static PlacedDecorationId IdFor(int value) =>
        new(new Guid(value, 0, 0, new byte[8]));
}

/// <summary>Paint Background behavior that remains deterministic in headless/scenario execution.</summary>
public sealed class EnvironmentBackgroundEditorClosureScenario : IScenario
{
    public string Id => "environment_background_editor";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        string root = Path.Combine(Path.GetTempPath(), $"desktop-buddy-env-paint-{Guid.NewGuid():N}");
        var store = new EnvironmentPaintStore(new CharacterFileSystem(), root);
        var presenter = new EnvironmentBackgroundPresenter { Name = "EnvironmentClosureBackground" };
        var editor = new EnvironmentBackgroundEditor { Name = "EnvironmentClosureBackgroundEditor" };
        editor.Configure(presenter, store);
        tree.Root.AddChild(presenter);
        tree.Root.AddChild(editor);
        try
        {
            editor.Open();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Control? blocker = editor.FindChild("EnvironmentBackgroundInputBlocker", true, false) as Control;
            Control? panel = editor.FindChild("PaintBackgroundPanel", true, false) as Control;
            bool composed = editor.IsOpen && GodotObject.IsInstanceValid(blocker) && blocker!.Visible &&
                GodotObject.IsInstanceValid(panel) && panel!.Visible &&
                editor.FindChild("PaintBrushButton", true, false) is Button &&
                editor.FindChild("PaintSprayButton", true, false) is Button &&
                editor.FindChild("PaintPenButton", true, false) is Button &&
                editor.FindChild("PaintShapesButton", true, false) is MenuButton &&
                editor.FindChild("PaintBrushSizeRow", true, false) is Control &&
                editor.FindChild("PaintBackgroundCurrentColor", true, false) is ColorRect &&
                editor.FindChild("PaintCurrentColor", true, false) is null &&
                editor.FindChild("BackgroundUnsavedSpacer", true, false) is Control { SizeFlagsVertical: Control.SizeFlags.ExpandFill } &&
                editor.FindChild("BackgroundUnsavedActions", true, false) is HBoxContainer;
            checks.Add(new StartupCheck(
                "environment_background_editor_closure_composed",
                composed,
                $"open={editor.IsOpen} blocker={blocker?.Visible} panel={panel?.Visible}"));

            var panelPin = editor.FindChild("PaintBackgroundPinController", true, false) as Win98PinnablePanel;
            panelPin?.Float();
            Window? panelWindow = editor.FindChild("PaintBackgroundWindow", true, false) as Window;
            bool detachable = panelPin?.IsFloating == true && panelWindow is { Unresizable: false } &&
                editor.FindChild("PaintBackgroundPalettePinController", true, false) is null &&
                editor.FindChild("PaintBackgroundPaletteWindow", true, false) is null;
            panelPin?.Dock();
            checks.Add(new StartupCheck(
                "environment_background_whole_panel_detaches_resizable_without_nested_palette_window",
                detachable,
                $"panel={panelWindow?.Size}"));

            // A detached panel has to be exactly its window. Sized independently it overhangs,
            // and the overhang is clipped by the window edge - which took half the close button
            // with it at any interface scale above 1 (owner report 2026-08-24). The scale is
            // raised for the check because at 1 the authored floating size happens to be the
            // larger of the two and hides the fault.
            float scale = Win98ThemeFactory.Scale;
            Win98ThemeFactory.ApplyScale(1.6f);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            panelPin?.Float();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Vector2 floatingPanel = panel?.Size ?? Vector2.Zero;
            Vector2 floatingWindow = panelWindow?.Size ?? Vector2I.Zero;
            bool flush = panelWindow is not null && panel is not null && floatingPanel == floatingWindow;
            panelPin?.Dock();
            Win98ThemeFactory.ApplyScale(scale);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            checks.Add(new StartupCheck(
                "environment_background_detached_panel_fills_its_window_exactly",
                flush,
                $"panel={floatingPanel} window={floatingWindow}"));

            // The panel masks the room under it only while it is docked over it. Detached, its
            // rect belongs to its own window, and measuring it against the room blocked a
            // panel-sized corner of the canvas (owner report 2026-08-25).
            Vector2 underPanel = panel?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
            bool masksDocked = editor.PanelCoversPoint(underPanel);
            panelPin?.Float();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool masksFloating = editor.PanelCoversPoint(underPanel);
            panelPin?.Dock();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            checks.Add(new StartupCheck(
                "environment_background_detached_panel_masks_no_part_of_the_room",
                masksDocked && !masksFloating,
                $"docked={masksDocked} floating={masksFloating} point={underPanel}"));

            EnvironmentCanvas canvas = presenter.Canvas;
            canvas.Color = new EnvironmentColor(210, 40, 60);
            canvas.Tool = EnvironmentPaintTool.Brush;
            canvas.Begin(.25, .25);
            canvas.Continue(.45, .45);
            canvas.End(.45, .45);
            int brushPixels = OpaquePixels(canvas);
            bool brushUndo = canvas.Undo() && OpaquePixels(canvas) == 0;
            checks.Add(new StartupCheck(
                "environment_background_brush_undo_exact",
                brushPixels > 0 && brushUndo,
                $"painted={brushPixels} undo={brushUndo}"));

            canvas.Tool = EnvironmentPaintTool.Spray;
            canvas.BrushDiameter = 40;
            canvas.Begin(.5, .5);
            for (int pulse = 0; pulse < 8; pulse++) canvas.Continue(.5, .5);
            canvas.End(.5, .5);
            int sprayPixels = OpaquePixels(canvas);
            bool sprayUndo = canvas.Undo() && OpaquePixels(canvas) == 0;
            checks.Add(new StartupCheck(
                "environment_background_stationary_spray_is_one_undo",
                sprayPixels > 0 && sprayUndo,
                $"sprayPixels={sprayPixels} undo={sprayUndo}"));

            canvas.Tool = EnvironmentPaintTool.CurvedLine;
            canvas.Begin(.2, .3);
            canvas.End(.8, .3);
            bool baselineAwaiting = canvas.CurvePhase == EnvironmentCurvePhase.AwaitFirstBend;
            canvas.Begin(.4, .3);
            canvas.End(.4, .55);
            bool firstAwaiting = canvas.CurvePhase == EnvironmentCurvePhase.AwaitSecondBend;
            canvas.Begin(.65, .35);
            canvas.End(.65, .18);
            bool committed = canvas.CurvePhase == EnvironmentCurvePhase.Idle && canvas.CanUndo && OpaquePixels(canvas) > 0;
            bool curveUndo = canvas.Undo() && OpaquePixels(canvas) == 0;
            checks.Add(new StartupCheck(
                "environment_background_curve_compound_transaction",
                baselineAwaiting && firstAwaiting && committed && curveUndo,
                $"baseline={baselineAwaiting} first={firstAwaiting} committed={committed} undo={curveUndo}"));

            canvas.Tool = EnvironmentPaintTool.Brush;
            canvas.Begin(.1, .1);
            canvas.End(.2, .2);
            byte[] beforeSave = canvas.ClonePixels();
            var save = (Button)editor.FindChild("PaintSaveButton", true, false);
            Vector2 saveGlobal = save.GetGlobalRect().GetCenter();
            Vector2 saveLocal = saveGlobal - blocker!.GetGlobalRect().Position;
            blocker.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = saveLocal,
                GlobalPosition = saveGlobal,
            });
            blocker.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = saveLocal,
                GlobalPosition = saveGlobal,
            });
            bool uiClickStayedOutOfCanvas = panel!.Visible && canvas.IsDirty;
            save.EmitSignal(BaseButton.SignalName.Pressed);
            for (int frame = 0; frame < 60 && editor.IsOpen; frame++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            byte[]? restored = store.Load();
            checks.Add(new StartupCheck(
                "environment_background_save_and_exit_button_roundtrip",
                uiClickStayedOutOfCanvas && !editor.IsOpen && !canvas.IsDirty &&
                    restored is not null && restored.AsSpan().SequenceEqual(beforeSave),
                $"uiSafe={uiClickStayedOutOfCanvas} open={editor.IsOpen} storedBytes={restored?.Length ?? 0}"));
        }
        finally
        {
            editor.QueueFree();
            presenter.QueueFree();
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        return new ScenarioResult(checks.All(static check => check.Passed), checks, [$"seed={seed}"]);
    }

    private static int OpaquePixels(EnvironmentCanvas canvas)
    {
        byte[] pixels = canvas.ClonePixels();
        int count = 0;
        for (int index = 3; index < pixels.Length; index += EnvironmentCanvasPolicy.BytesPerPixel)
            if (pixels[index] != 0) count++;
        return count;
    }
}

/// <summary>Startup composition gate for the base -> wallpaper -> paint -> wall-decor ordering.</summary>
public sealed class EnvironmentStartupClosureScenario : IScenario
{
    public string Id => "environment_startup_registration";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var progress = new BuddyProgressState(0.018);
        var environment = new EnvironmentProgressState();
        var saves = new SaveCoordinator(progress, new InMemoryProgressStore(), environment: environment);
        var commandBar = new Win98CommandBarBootstrap { Name = "EnvironmentClosureCommandBar" };
        var bootstrap = new EnvironmentCustomizationBootstrap { Name = "EnvironmentClosureBootstrap" };
        tree.Root.AddChild(commandBar);
        tree.Root.AddChild(bootstrap);
        try
        {
            bootstrap.ComposeForStartupTest(environment, saves, commandBar);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            EnvironmentBackgroundPresenter? presenter = tree.Root.FindChild(
                nameof(EnvironmentBackgroundPresenter), true, false) as EnvironmentBackgroundPresenter;
            MeshInstance3D? baseQuad = presenter?.FindChild("EnvironmentBackgroundBaseQuad", true, false) as MeshInstance3D;
            MeshInstance3D? paintQuad = presenter?.FindChild("EnvironmentBackgroundQuad", true, false) as MeshInstance3D;
            bool layering = GodotObject.IsInstanceValid(baseQuad) && GodotObject.IsInstanceValid(paintQuad) &&
                Mathf.IsEqualApprox(baseQuad!.Position.Z, EnvironmentBackgroundPresenter.BackdropZ) &&
                Mathf.IsEqualApprox(paintQuad!.Position.Z, EnvironmentBackgroundPresenter.PaintZ) &&
                EnvironmentBackgroundPresenter.BackdropZ < EnvironmentDecorationPresenter.ZFor(DecorationRenderBand.Wallpaper) &&
                EnvironmentDecorationPresenter.ZFor(DecorationRenderBand.Wallpaper) < EnvironmentBackgroundPresenter.PaintZ &&
                EnvironmentBackgroundPresenter.PaintZ < EnvironmentDecorationPresenter.ZFor(DecorationRenderBand.WallDecoration);
            checks.Add(new StartupCheck(
                "environment_startup_background_layer_order",
                layering,
                $"base={baseQuad?.Position.Z} wallpaper={EnvironmentDecorationPresenter.ZFor(DecorationRenderBand.Wallpaper)} " +
                $"paint={paintQuad?.Position.Z} wallDecor={EnvironmentDecorationPresenter.ZFor(DecorationRenderBand.WallDecoration)}"));
            checks.Add(new StartupCheck(
                "environment_startup_background_command_registered",
                bootstrap.HasPaintBackgroundRegistration &&
                    tree.Root.FindChild(nameof(EnvironmentBackgroundEditor), true, false) is EnvironmentBackgroundEditor,
                $"registered={bootstrap.HasPaintBackgroundRegistration}"));
        }
        finally
        {
            bootstrap.QueueFree();
            commandBar.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        return new ScenarioResult(checks.All(static check => check.Passed), checks, [$"seed={seed}"]);
    }
}

/// <summary>Engine-free placement contract exposed through the scenario runner.</summary>
public sealed class EnvironmentPlacementClosureScenario(string id = "environment_placement_engine") : IScenario
{
    public string Id => id;

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var room = new RoomScreenBounds(100, 50, 800, 600);
        bool outsideRejected = !EnvironmentPlacement.TryMap(
            99, 400, room, DecorationAnchorKind.RoomSurface, false, EnvironmentGridSize.Medium, out _);
        bool wallAccepted = EnvironmentPlacement.TryMap(
            300, 200, room, DecorationAnchorKind.Wall, false, EnvironmentGridSize.Medium, out CanonicalRoomPosition wall);
        bool floorRejectedOnWall = !EnvironmentPlacement.TryMap(
            300, 200, room, DecorationAnchorKind.Floor, false, EnvironmentGridSize.Medium, out _);
        bool floorAccepted = EnvironmentPlacement.TryMap(
            700, 560, room, DecorationAnchorKind.Floor, false, EnvironmentGridSize.Medium, out CanonicalRoomPosition floor);
        checks.Add(new StartupCheck(
            "environment_placement_anchor_and_ui_bounds",
            outsideRejected && wallAccepted && floorRejectedOnWall && floorAccepted,
            $"outside={outsideRejected} wall={wall} floor={floor}"));

        bool snapped = EnvironmentPlacement.TryMap(
            413, 527, room, DecorationAnchorKind.RoomSurface, true, EnvironmentGridSize.Large, out CanonicalRoomPosition snappedPoint);
        float xSteps = snappedPoint.X * 8f;
        float ySteps = snappedPoint.Y * 8f;
        checks.Add(new StartupCheck(
            "environment_placement_grid_quantizes",
            snapped && Mathf.IsEqualApprox(xSteps, Mathf.Round(xSteps)) && Mathf.IsEqualApprox(ySteps, Mathf.Round(ySteps)),
            $"snapped={snappedPoint}"));

        CanonicalRoomPosition canonical = new(.375f, .8125f);
        (float x1, float y1) = EnvironmentPlacement.ToScreen(canonical, room);
        var resized = new RoomScreenBounds(240, 120, 1600, 1200);
        (float x2, float y2) = EnvironmentPlacement.ToScreen(canonical, resized);
        bool firstMapped = EnvironmentPlacement.TryMap(
            x1, y1, room, DecorationAnchorKind.RoomSurface, false, EnvironmentGridSize.Medium, out CanonicalRoomPosition mapped1);
        bool secondMapped = EnvironmentPlacement.TryMap(
            x2, y2, resized, DecorationAnchorKind.RoomSurface, false, EnvironmentGridSize.Medium, out CanonicalRoomPosition mapped2);
        bool resizeStable = firstMapped && secondMapped &&
            NearlyEqual(mapped1, canonical) && NearlyEqual(mapped2, canonical);
        checks.Add(new StartupCheck(
            "environment_placement_resize_mapping_stable",
            resizeStable,
            $"first={mapped1} second={mapped2}"));

        return Task.FromResult(new ScenarioResult(
            checks.All(static check => check.Passed), checks, [$"seed={seed}"]));
    }

    private static bool NearlyEqual(CanonicalRoomPosition left, CanonicalRoomPosition right) =>
        Math.Abs(left.X - right.X) < .0001f && Math.Abs(left.Y - right.Y) < .0001f;
}

/// <summary>Economy, rotation, cancellation, wallpaper, and permanent-ownership matrix.</summary>
public sealed class EnvironmentTransactionClosureScenario(string id) : IScenario
{
    public string Id => id;

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        EnvironmentDecorationResource lampResource = First(DecorationCategory.Lamp);
        EnvironmentDecorationResource wallpaperResource = First(DecorationCategory.Wallpaper);
        DecorationDefinition lamp = lampResource.ToDefinition();
        DecorationDefinition wallpaper = wallpaperResource.ToDefinition();
        long balance = Math.Max(1_000_000, (lamp.PriceMilliCredits * 4) + (wallpaper.PriceMilliCredits * 2));
        int nextId = 100;
        var session = new EnvironmentEditSession(
            new EnvironmentLayout(), balance, EnvironmentDecorationRegistry.Domain,
            () => EnvironmentTrustedDefinitionsClosureScenario.IdFor(nextId++));

        EnvironmentEditResult first = session.Place(lamp.Id, new CanonicalRoomPosition(.25f, .82f));
        EnvironmentEditResult second = session.Place(lamp.Id, new CanonicalRoomPosition(.6f, .82f));
        long duplicateDelta = session.PendingBalanceDeltaMilliCredits;
        bool duplicatePurchase = first.Succeeded && second.Succeeded &&
            first.InstanceId != second.InstanceId && duplicateDelta == -(lamp.PriceMilliCredits * 2);
        checks.Add(new StartupCheck(
            "environment_purchase_per_instance",
            duplicatePurchase,
            $"delta={duplicateDelta} count={session.WorkingLayout.Decorations.Count}"));

        long beforeTransform = session.PendingBalanceDeltaMilliCredits;
        session.Move(first.InstanceId, new CanonicalRoomPosition(.35f, .8f));
        EnvironmentEditResult rotated = session.Rotate(first.InstanceId, 1);
        bool transformFree = rotated.Succeeded && session.PendingBalanceDeltaMilliCredits == beforeTransform &&
            session.WorkingLayout.Decorations.Single(item => item.InstanceId == first.InstanceId).RotationDegrees == lamp.Rotation.StepDegrees;
        checks.Add(new StartupCheck(
            "environment_move_rotate_are_free",
            transformFree,
            $"rotation={session.WorkingLayout.Decorations.Single(item => item.InstanceId == first.InstanceId).RotationDegrees}"));

        EnvironmentEditCheckpoint checkpoint = session.Checkpoint();
        session.Remove(second.InstanceId);
        bool stagedDeleteRefunded = session.PendingBalanceDeltaMilliCredits == -lamp.PriceMilliCredits;
        session.Restore(checkpoint);
        bool cancelExact = session.PendingBalanceDeltaMilliCredits == beforeTransform &&
            session.WorkingLayout.Decorations.Count == 2;
        checks.Add(new StartupCheck(
            "environment_cancel_transaction_exact",
            stagedDeleteRefunded && cancelExact,
            $"refund={stagedDeleteRefunded} restored={cancelExact}"));

        var savedWallpaper = new PlacedDecoration(
            EnvironmentTrustedDefinitionsClosureScenario.IdFor(200),
            wallpaper.Id,
            new CanonicalRoomPosition(.5f, .5f),
            0,
            wallpaper.RenderBand,
            wallpaper.PriceMilliCredits);
        var wallpaperSession = new EnvironmentEditSession(
            new EnvironmentLayout([savedWallpaper]),
            balance,
            EnvironmentDecorationRegistry.Domain,
            () => EnvironmentTrustedDefinitionsClosureScenario.IdFor(201));
        EnvironmentEditResult replacement = wallpaperSession.Place(wallpaper.Id, new CanonicalRoomPosition(.5f, .5f));
        bool singleWallpaper = replacement.Succeeded &&
            wallpaperSession.WorkingLayout.Decorations.Count(item => item.RenderBand == DecorationRenderBand.Wallpaper) == 1 &&
            wallpaperSession.PendingBalanceDeltaMilliCredits == -wallpaper.PriceMilliCredits &&
            wallpaperSession.OwnedUnplacedCount(wallpaper.Id) == 1;
        checks.Add(new StartupCheck(
            "environment_wallpaper_single_slot_and_final_ownership",
            singleWallpaper,
            $"wallpapers={wallpaperSession.WorkingLayout.Decorations.Count} stored={wallpaperSession.OwnedUnplacedCount(wallpaper.Id)}"));

        var savedLamp = new PlacedDecoration(
            EnvironmentTrustedDefinitionsClosureScenario.IdFor(300), lamp.Id,
            new CanonicalRoomPosition(.3f, .82f), 0, lamp.RenderBand, lamp.PriceMilliCredits);
        var ownershipSession = new EnvironmentEditSession(
            new EnvironmentLayout([savedLamp]),
            1,
            EnvironmentDecorationRegistry.Domain,
            () => EnvironmentTrustedDefinitionsClosureScenario.IdFor(301));
        ownershipSession.Remove(savedLamp.InstanceId);
        bool banked = ownershipSession.OwnedUnplacedCount(lamp.Id) == 1 &&
            ownershipSession.PendingBalanceDeltaMilliCredits == 0;
        EnvironmentEditResult freeReplace = ownershipSession.Place(lamp.Id, new CanonicalRoomPosition(.7f, .82f));
        bool reusedForFree = freeReplace.Succeeded && ownershipSession.PendingBalanceDeltaMilliCredits == 0 &&
            ownershipSession.OwnedUnplacedCount(lamp.Id) == 0;
        checks.Add(new StartupCheck(
            "environment_saved_purchase_stays_owned",
            banked && reusedForFree,
            $"banked={banked} reused={reusedForFree}"));

        return Task.FromResult(new ScenarioResult(
            checks.All(static check => check.Passed), checks, [$"seed={seed}"]));
    }

    private static EnvironmentDecorationResource First(DecorationCategory category) =>
        EnvironmentDecorationRegistry.Authored.Entries.First(resource => resource.ToDefinition().Category == category);
}

/// <summary>Atomic progress round-trip through the same JSON schema used by a restart.</summary>
public sealed class EnvironmentRestartRestoreClosureScenario : IScenario
{
    public string Id => "environment_decor_restart_restore";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var progress = new BuddyProgressState(0.018);
        progress.Deposit(1_000_000);
        var environment = new EnvironmentProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, environment: environment);
        EnvironmentDecorationResource lamp = First(DecorationCategory.Lamp);
        EnvironmentDecorationResource sofa = First(DecorationCategory.Sofa);
        EnvironmentDecorationResource wallpaper = First(DecorationCategory.Wallpaper);
        int nextId = 400;
        var session = new EnvironmentEditSession(
            environment.Layout,
            progress.BalanceMilliCredits,
            EnvironmentDecorationRegistry.Domain,
            () => EnvironmentTrustedDefinitionsClosureScenario.IdFor(nextId++));
        EnvironmentEditResult lampPlaced = session.Place(lamp.ToDefinition().Id, new CanonicalRoomPosition(.2f, .82f));
        session.Place(sofa.ToDefinition().Id, new CanonicalRoomPosition(.55f, .82f));
        session.Place(wallpaper.ToDefinition().Id, new CanonicalRoomPosition(.5f, .5f));
        session.Rotate(lampPlaced.InstanceId, 1);
        await saves.CommitEnvironmentAsync(session);

        ProgressSave persisted = store.Progress ?? throw new InvalidOperationException("Environment progress was not persisted.");
        string json = ProgressSavePolicy.Serialize(persisted);
        SaveDecodeResult decoded = ProgressSavePolicy.Decode(json);
        EnvironmentProgressState restored = decoded.Save!.Environment.CreateState();
        bool roundTrip = decoded.Status == SaveDecodeStatus.Valid &&
            restored.Layout.Decorations.SequenceEqual(environment.Layout.Decorations) &&
            decoded.Save.BalanceMilliCredits == progress.BalanceMilliCredits;
        checks.Add(new StartupCheck(
            "environment_restart_restores_layout_rotation_wallpaper_and_wallet",
            roundTrip && restored.Layout.Decorations.Any(item => item.RotationDegrees != 0) &&
                restored.Layout.Decorations.Count(item => item.RenderBand == DecorationRenderBand.Wallpaper) == 1,
            $"decorations={restored.Layout.Decorations.Count} balance={decoded.Save.BalanceMilliCredits}"));

        PlacedDecoration savedLamp = restored.Layout.Decorations.First(item => item.DefinitionId == lamp.ToDefinition().Id);
        var storageSession = new EnvironmentEditSession(
            restored.Layout,
            decoded.Save.BalanceMilliCredits,
            EnvironmentDecorationRegistry.Domain,
            () => EnvironmentTrustedDefinitionsClosureScenario.IdFor(450),
            restored.OwnedUnplaced);
        storageSession.Remove(savedLamp.InstanceId);
        EnvironmentCommit storageCommit = storageSession.PrepareCommit();
        restored.Commit(storageCommit.Layout, storageCommit.OwnedUnplaced);
        ProgressSave withStorage = ProgressSave.FromSnapshot(
            progress.Snapshot(), environment: restored.Snapshot());
        EnvironmentProgressState storageRestored = ProgressSavePolicy.Decode(
            ProgressSavePolicy.Serialize(withStorage)).Save!.Environment.CreateState();
        checks.Add(new StartupCheck(
            "environment_restart_restores_owned_storage",
            storageRestored.OwnedUnplaced.Contains(lamp.ToDefinition().Id) &&
                storageRestored.Layout.Decorations.All(item => item.InstanceId != savedLamp.InstanceId),
            $"stored={storageRestored.OwnedUnplaced.Count}"));

        return new ScenarioResult(checks.All(static check => check.Passed), checks, [$"seed={seed}"]);
    }

    private static EnvironmentDecorationResource First(DecorationCategory category) =>
        EnvironmentDecorationRegistry.Authored.Entries.First(resource => resource.ToDefinition().Category == category);
}

/// <summary>
/// In-scene owner-journey automation up to the point where subjective DPI/pointer/readability checks
/// require a human. It deliberately uses the real decorator controls rather than domain shortcuts.
/// </summary>
public sealed class EnvironmentDecoratorClosureScenario(string id = "environment_decorator") : IScenario
{
    public string Id => id;

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var progress = new BuddyProgressState(0.018);
        progress.Deposit(300_000);
        var environment = new EnvironmentProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, environment: environment);
        var economy = new EconomyService(progress, new ToolCatalogue([]));
        var pointer = new LabPointerGrabComponent { Name = "EnvironmentClosurePointer" };
        pointer.SetProcessInput(true);
        pointer.SetProcessUnhandledInput(true);
        var boundaries = new BoundaryController { Name = "EnvironmentClosureBoundaries" };
        var visuals = new EnvironmentDecorationLayer { Name = "EnvironmentClosureVisuals" };
        visuals.Configure(environment, boundaries);
        var buddy2D = new Node2D { Name = "EnvironmentClosureBuddy2D", Visible = true };
        var buddy3D = new Node3D { Name = "EnvironmentClosureBuddy3D", Visible = true };
        var decorator = new EnvironmentDecorator { Name = "EnvironmentClosureDecorator" };
        decorator.Configure(progress, economy, pointer, buddy2D, buddy3D, environment, saves, visuals);
        tree.Root.AddChild(pointer);
        tree.Root.AddChild(boundaries);
        tree.Root.AddChild(visuals);
        tree.Root.AddChild(buddy2D);
        tree.Root.AddChild(buddy3D);
        tree.Root.AddChild(decorator);

        try
        {
            decorator.Open();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var blocker = decorator.FindChild("EnvironmentDecoratorInputSurface", true, false) as Control
                ?? throw new InvalidOperationException("Decorator input surface was not composed.");
            var catalogue = decorator.FindChild("EnvironmentCatalog", true, false) as Win98CatalogGrid
                ?? throw new InvalidOperationException("Decorator catalogue was not composed.");
            var panel = decorator.FindChild("EnvironmentDecoratorPanel", true, false) as Control
                ?? throw new InvalidOperationException("Decorator panel was not composed.");
            checks.Add(new StartupCheck(
                "environment_decorator_input_isolation",
                decorator.IsOpen && blocker.Visible && panel.Visible &&
                    !pointer.IsProcessingInput() && !pointer.IsProcessingUnhandledInput(),
                $"open={decorator.IsOpen} pointer={pointer.IsProcessingInput()}/{pointer.IsProcessingUnhandledInput()}"));
            var roomPin = decorator.FindChild("RoomDecoratorPinController", true, false) as Win98PinnablePanel;
            roomPin?.Float();
            Window? roomWindow = decorator.FindChild("RoomDecoratorWindow", true, false) as Window;
            var titleRow = (HBoxContainer)((PanelContainer)panel.FindChild("TitleBar", true, false)).GetChild(0);
            bool closeOutside = titleRow.GetChild(titleRow.GetChildCount() - 1).Name == "EnvironmentDecoratorCloseButton" &&
                titleRow.GetChild(titleRow.GetChildCount() - 2).Name == "PinBox";
            bool roomChrome = roomPin?.IsFloating == true && roomWindow is { Unresizable: false } &&
                decorator.FindChild("RoomDecoratorScroll", true, false) is ScrollContainer &&
                decorator.FindChild("EnvironmentDoneButton", true, false) is null &&
                ((Button)decorator.FindChild("EnvironmentMoveItemsButton", true, false)).Text == "Edit mode" &&
                ((Button)decorator.FindChild("EnvironmentDeleteItemsButton", true, false)).Text == "Delete mode" &&
                ((Button)decorator.FindChild("EnvironmentResetAllButton", true, false)).Text == "Reset Room" &&
                ((Button)decorator.FindChild("EnvironmentBuyButton", true, false)).CustomMinimumSize ==
                ((Button)decorator.FindChild("EnvironmentPlaceButton", true, false)).CustomMinimumSize && closeOutside;
            roomPin?.Dock();
            checks.Add(new StartupCheck(
                "environment_decorator_detaches_resizes_scrolls_and_uses_revised_actions",
                roomChrome,
                $"floating={roomPin?.IsFloating} window={roomWindow?.Size} review={decorator.FindChild("EnvironmentDoneButton", true, false) is not null}"));

            Press(blocker, Vector2.Zero);
            bool dockedOutsideClosed = !decorator.IsOpen;
            decorator.Open();
            roomPin?.Float();
            Press(blocker, Vector2.Zero);
            bool floatingOutsideStayedOpen = decorator.IsOpen;
            roomPin?.Dock();
            checks.Add(new StartupCheck(
                "environment_decorator_outside_click_closes_only_while_docked",
                dockedOutsideClosed && floatingOutsideStayedOpen,
                $"docked={dockedOutsideClosed} floating={floatingOutsideStayedOpen}"));

            EnvironmentDecorationResource lamp = EnvironmentDecorationRegistry.Authored.Entries
                .First(resource => resource.ToDefinition().Category == DecorationCategory.Lamp);
            DecorationDefinition lampDefinition = lamp.ToDefinition();
            Rect2 room = EnvironmentRoomRect.Resolve(decorator);
            Vector2 firstPoint = room.Position + new Vector2(room.Size.X * .3f, room.Size.Y * .84f);
            Vector2 secondPoint = room.Position + new Vector2(room.Size.X * .7f, room.Size.Y * .84f);
            var otherUi = new VBoxContainer { Name = "EnvironmentClosureOtherUi", Visible = true };
            tree.Root.AddChild(otherUi);

            catalogue.Select(lamp.DefinitionId);
            long beforePlace = decorator.VisibleProjectedBalance;
            bool ownedOnlyPlace = ((Button)decorator.FindChild("EnvironmentPlaceButton", true, false)).Disabled &&
                !((Button)decorator.FindChild("EnvironmentBuyButton", true, false)).Disabled;
            Press(decorator, "EnvironmentBuyButton");
            long boughtBalance = decorator.VisibleProjectedBalance;
            Press(decorator, "EnvironmentPlaceButton");
            long reservedBalance = decorator.VisibleProjectedBalance;
            checks.Add(new StartupCheck(
                "environment_decorator_buy_stores_and_owned_place_reserves",
                ownedOnlyPlace && beforePlace == 300_000 && boughtBalance == 300_000 - lampDefinition.PriceMilliCredits &&
                    reservedBalance == boughtBalance &&
                    decorator.PlacementMode && !panel.Visible && !otherUi.Visible && buddy2D.Visible && buddy3D.Visible,
                $"before={beforePlace} bought={boughtBalance} reserved={reservedBalance} placement={decorator.PlacementMode}"));
            PlaceAt(blocker, decorator, firstPoint, confirm: true);
            bool firstStaged = decorator.VisibleWorkingLayout.Decorations.Count == 1 &&
                decorator.VisibleOwnedCount(lampDefinition.Id) == 1 && panel.Visible && otherUi.Visible;

            catalogue.Select(lamp.DefinitionId);
            Press(decorator, "EnvironmentBuyButton");
            Press(decorator, "EnvironmentPlaceButton");
            PlaceAt(blocker, decorator, secondPoint, confirm: true);
            bool secondCharged = decorator.VisibleWorkingLayout.Decorations.Count == 2 &&
                decorator.VisibleProjectedBalance == 300_000 - (lampDefinition.PriceMilliCredits * 2) &&
                decorator.VisibleOwnedCount(lampDefinition.Id) == 2;
            checks.Add(new StartupCheck(
                "environment_decorator_duplicate_copies_charge_twice",
                firstStaged && secondCharged,
                $"count={decorator.VisibleWorkingLayout.Decorations.Count} balance={decorator.VisibleProjectedBalance}"));

            catalogue.Select(lamp.DefinitionId);
            Press(decorator, "EnvironmentBuyButton");
            Press(decorator, "EnvironmentPlaceButton");
            PlaceAt(blocker, decorator, firstPoint, confirm: false);
            Press(decorator, "EnvironmentPlacementCancelButton");
            bool stagedCancel = decorator.VisibleWorkingLayout.Decorations.Count == 2 &&
                decorator.VisibleProjectedBalance == 300_000 - (lampDefinition.PriceMilliCredits * 3) &&
                decorator.VisibleOwnedCount(lampDefinition.Id) == 3;
            checks.Add(new StartupCheck(
                "environment_decorator_cancelled_placement_returns_bought_copy_to_storage",
                stagedCancel,
                $"count={decorator.VisibleWorkingLayout.Decorations.Count} balance={decorator.VisibleProjectedBalance}"));

            Press(decorator, "EnvironmentDecoratorCloseButton");
            Press(decorator, "EnvironmentDiscardButton");
            bool discarded = !decorator.IsOpen && environment.Layout.Decorations.Count == 0 &&
                progress.BalanceMilliCredits == 300_000 && pointer.IsProcessingInput() && pointer.IsProcessingUnhandledInput();
            checks.Add(new StartupCheck(
                "environment_decorator_discard_restores_room_wallet_and_input",
                discarded,
                $"open={decorator.IsOpen} saved={environment.Layout.Decorations.Count} balance={progress.BalanceMilliCredits}"));

            decorator.Open();
            catalogue.Select(lamp.DefinitionId);
            Press(decorator, "EnvironmentBuyButton");
            Press(decorator, "EnvironmentPlaceButton");
            PlaceAt(blocker, decorator, firstPoint, confirm: true);
            Press(decorator, "EnvironmentDecoratorCloseButton");
            Press(decorator, "EnvironmentConfirmSaveButton");
            await SettleAsync(tree);
            bool saved = !decorator.IsOpen && environment.Layout.Decorations.Count == 1 &&
                progress.BalanceMilliCredits == 300_000 - lampDefinition.PriceMilliCredits;
            checks.Add(new StartupCheck(
                "environment_decorator_save_commits_once",
                saved,
                $"saved={environment.Layout.Decorations.Count} balance={progress.BalanceMilliCredits}"));

            decorator.Open();
            Press(decorator, "EnvironmentDeleteItemsButton");
            Press(blocker, firstPoint);
            Press(decorator, "EnvironmentDeleteDoneButton");
            Press(decorator, "EnvironmentDecoratorCloseButton");
            Press(decorator, "EnvironmentConfirmSaveButton");
            await SettleAsync(tree);
            bool banked = environment.Layout.Decorations.Count == 0 &&
                environment.OwnedUnplaced.Count(idValue => idValue == lampDefinition.Id) == 1 &&
                progress.BalanceMilliCredits == 300_000 - lampDefinition.PriceMilliCredits;
            checks.Add(new StartupCheck(
                "environment_decorator_saved_delete_banks_without_refund",
                banked,
                $"stored={environment.OwnedUnplaced.Count} balance={progress.BalanceMilliCredits}"));

            decorator.Open();
            catalogue.Select(lamp.DefinitionId);
            long storedBefore = decorator.VisibleProjectedBalance;
            Press(decorator, "EnvironmentPlaceButton");
            long storedReserved = decorator.VisibleProjectedBalance;
            PlaceAt(blocker, decorator, secondPoint, confirm: true);
            Press(decorator, "EnvironmentDecoratorCloseButton");
            Press(decorator, "EnvironmentConfirmSaveButton");
            await SettleAsync(tree);
            bool reused = storedBefore == storedReserved && environment.Layout.Decorations.Count == 1 &&
                environment.OwnedUnplaced.Count == 0 && progress.BalanceMilliCredits == storedBefore;
            checks.Add(new StartupCheck(
                "environment_decorator_owned_copy_replaces_for_free",
                reused,
                $"before={storedBefore} reserved={storedReserved} stored={environment.OwnedUnplaced.Count}"));

            checks.Add(new StartupCheck(
                "environment_decorator_has_no_sell_route",
                decorator.FindChild("EnvironmentSellButton", true, false) is null,
                "no Sell control"));
            otherUi.QueueFree();
        }
        finally
        {
            decorator.QueueFree();
            visuals.QueueFree();
            boundaries.QueueFree();
            pointer.QueueFree();
            buddy2D.QueueFree();
            buddy3D.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        return new ScenarioResult(checks.All(static check => check.Passed), checks, [$"seed={seed}"]);
    }

    private static void PlaceAt(Control blocker, Node decorator, Vector2 point, bool confirm)
    {
        RoomInput(blocker, new InputEventMouseMotion { Position = point });
        RoomInput(blocker, new InputEventMouseButton
        {
            Position = point,
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        });
        if (confirm) Press(decorator, "EnvironmentPlacementDoneButton");
    }

    private static async Task SettleAsync(SceneTree tree)
    {
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static void Press(Node root, string name) =>
        ((Button)(root.FindChild(name, true, false)
            ?? throw new InvalidOperationException($"Missing {name}.")))
        .EmitSignal(Button.SignalName.Pressed);

    private static void Press(Control blocker, Vector2 point) => RoomInput(blocker, new InputEventMouseButton
    {
        Position = point,
        ButtonIndex = MouseButton.Left,
        Pressed = true,
    });

    private static void RoomInput(Control blocker, InputEvent input) =>
        blocker.EmitSignal(Control.SignalName.GuiInput, input);
}
