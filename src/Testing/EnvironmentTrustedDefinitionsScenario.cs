using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Environment;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Persistence;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class EnvironmentTrustedDefinitionsScenario : IScenario
{
    public string Id => "environment_trusted_definitions";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        EnvironmentDecorationCatalogueResource authored = EnvironmentDecorationRegistry.Authored;
        DecorationCatalogue catalogue = authored.ToCatalogue();
        string[] validation = authored.Validate().ToArray();
        DecorationCategory[] categories = catalogue.Definitions.Select(item => item.Category).ToArray();
        checks.Add(new StartupCheck("environment_authored_catalogue_valid",
            validation.Length == 0 && catalogue.Definitions.Count == 6,
            $"definitions={catalogue.Definitions.Count} errors={string.Join(" | ", validation)}"));
        checks.Add(new StartupCheck("environment_launch_category_order",
            categories.SequenceEqual(Enum.GetValues<DecorationCategory>()),
            string.Join(",", categories)));

        var host = new Node2D { Name = "EnvironmentDefinitionScenarioHost" };
        tree.Root.AddChild(host);
        int physicsNodes = 0;
        bool bandsMatch = true;
        try
        {
            int index = 1;
            foreach (DecorationDefinition definition in catalogue.Definitions)
            {
                EnvironmentDecorationResource resource = authored.Find(definition.Id)
                    ?? throw new InvalidOperationException($"Missing authored visual for {definition.Id}.");
                var placed = new PlacedDecoration(
                    new PlacedDecorationId(new Guid(index++, 0, 0, new byte[8])),
                    definition.Id,
                    new CanonicalRoomPosition(.5f, .5f),
                    0,
                    definition.RenderBand,
                    definition.PriceMilliCredits);
                var presenter = new EnvironmentDecorationPresenter();
                host.AddChild(presenter);
                presenter.Configure(placed, resource);
                bandsMatch &= Mathf.IsEqualApprox(presenter.Position.Z, EnvironmentDecorationPresenter.ZFor(definition.RenderBand));
                physicsNodes += CountPhysics(presenter);
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            checks.Add(new StartupCheck("environment_visuals_are_non_physical",
                physicsNodes == 0 && host.GetChildCount() == 6,
                $"presenters={host.GetChildCount()} physics={physicsNodes}"));
            checks.Add(new StartupCheck("environment_render_bands_bounded", bandsMatch, "trusted z-band mapping"));
        }
        finally
        {
            host.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool passed = checks.All(check => check.Passed);
        return new ScenarioResult(passed, checks, [$"seed={seed}"]);
    }

    private static int CountPhysics(Node node)
    {
        int count = node is CollisionObject2D or Joint2D ? 1 : 0;
        foreach (Node child in node.GetChildren()) count += CountPhysics(child);
        return count;
    }
}

public sealed class EnvironmentBackgroundEditorScenario : IScenario
{
    public string Id => "environment_background_editor";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var progress = new BuddyProgressState(0.018);
        var environment = new EnvironmentProgressState();
        var saves = new SaveCoordinator(progress, new InMemoryProgressStore(), environment: environment);
        var presenter = new EnvironmentBackgroundPresenter { Name = "ScenarioBackgroundPresenter" };
        var editor = new EnvironmentBackgroundEditor { Name = "ScenarioBackgroundEditor" };
        editor.Configure(environment, saves, presenter);
        tree.Root.AddChild(presenter);
        tree.Root.AddChild(editor);
        try
        {
            editor.Open();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var blocker = editor.FindChild("EnvironmentBackgroundInputBlocker", true, false) as Control;
            var panel = editor.FindChild("PaintBackgroundPanel", true, false) as Control;
            var picker = editor.FindChild("BackgroundColorPicker", true, false) as ColorPickerButton;
            bool usable = editor.IsOpen && GodotObject.IsInstanceValid(blocker) && blocker!.Visible &&
                blocker.MouseFilter == Control.MouseFilterEnum.Stop && GodotObject.IsInstanceValid(panel) && panel!.Visible &&
                GodotObject.IsInstanceValid(picker);
            checks.Add(new StartupCheck("environment_background_editor_composed", usable,
                $"open={editor.IsOpen} blocker={blocker?.Visible} panel={panel?.Visible}"));

            Color selected = Color.Color8(12, 34, 56);
            picker!.EmitSignal(ColorPickerButton.SignalName.ColorChanged, selected);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            checks.Add(new StartupCheck("environment_background_live_preview",
                presenter.Current.Wall == new EnvironmentColor(12, 34, 56),
                $"wall={presenter.Current.Wall}"));
        }
        finally
        {
            editor.QueueFree();
            presenter.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        return new ScenarioResult(checks.All(check => check.Passed), checks, [$"seed={seed}"]);
    }
}

public sealed class EnvironmentStartupRegistrationScenario : IScenario
{
    public string Id => "environment_startup_registration";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        string sceneText = FileAccess.GetFileAsString("res://scenes/sandbox.tscn");
        string actualRootName = sceneText.Contains("[node name=\"Sandbox\" type=\"Node2D\"", StringComparison.Ordinal) &&
            sceneText.Contains("path=\"res://src/App/SandboxRoot.cs\"", StringComparison.Ordinal)
                ? "Sandbox"
                : string.Empty;
        var sandbox = new SandboxRoot { Name = actualRootName };
        var host = new Node { Name = "NormalBootHost" };
        host.AddChild(sandbox);
        SandboxRoot? found = EnvironmentCustomizationBootstrap.FindSandboxForStartup(host);
        bool actualNameDiffers = actualRootName == "Sandbox" && actualRootName != nameof(SandboxRoot);
        checks.Add(new StartupCheck("environment_normal_boot_finds_typed_sandbox",
            actualNameDiffers && ReferenceEquals(found, sandbox),
            $"sceneName={sandbox.Name} type={sandbox.GetType().Name} found={found?.Name}"));
        host.Free();

        var progress = new BuddyProgressState(0.018);
        var environment = new EnvironmentProgressState();
        var saves = new SaveCoordinator(progress, new InMemoryProgressStore(), environment: environment);
        var commandBar = new DesktopBuddy.UI.Win98.Win98CommandBarBootstrap { Name = "StartupTestCommandBar" };
        var bootstrap = new EnvironmentCustomizationBootstrap { Name = "StartupTestEnvironmentBootstrap" };
        tree.Root.AddChild(commandBar);
        tree.Root.AddChild(bootstrap);
        bootstrap.ComposeForStartupTest(environment, saves, commandBar);
        var backdrop = tree.Root.FindChild(nameof(EnvironmentBackgroundPresenter), true, false) as EnvironmentBackgroundPresenter;
        bool behindBuddy = GodotObject.IsInstanceValid(backdrop) && backdrop is Node3D &&
            backdrop!.GetChildren().OfType<MeshInstance3D>().Count() == 2 &&
            backdrop.GetChildren().OfType<MeshInstance3D>().All(mesh => mesh.Position.Z < 0f);
        checks.Add(new StartupCheck("environment_paint_background_command_registered",
            bootstrap.HasPaintBackgroundRegistration &&
            tree.Root.FindChild(nameof(EnvironmentBackgroundEditor), true, false) is EnvironmentBackgroundEditor,
            $"registered={bootstrap.HasPaintBackgroundRegistration}"));
        checks.Add(new StartupCheck("environment_background_behind_buddy_plane", behindBuddy,
            $"presenter={backdrop?.GetType().Name} z={EnvironmentBackgroundPresenter.BackdropZ}"));
        bootstrap.QueueFree();
        commandBar.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new ScenarioResult(checks.All(check => check.Passed), checks, [$"seed={seed}"]);
    }
}

public sealed class EnvironmentPlacementEngineScenario : IScenario
{
    public string Id => "environment_placement_engine";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        EnvironmentDecorationResource lamp = EnvironmentDecorationRegistry.Authored.Entries[0];
        var session = new EnvironmentEditSession(new EnvironmentLayout(), 250_000, EnvironmentDecorationRegistry.Domain);
        var host = new Node2D { Name = "EnvironmentPlacementHost" };
        var controller = new EnvironmentPlacementController { Name = "EnvironmentPlacementController" };
        tree.Root.AddChild(host);
        host.AddChild(controller);
        controller.Configure(session, host, new RoomScreenBounds(100, 50, 800, 600));
        session.Reserve(lamp.ToDefinition().Id, 250_000);
        controller.Begin(lamp);

        bool wallRejected = !controller.UpdatePointer(new Vector2(300, 200)) && !controller.GhostValid;
        bool floorAccepted = controller.UpdatePointer(new Vector2(300, 500)) && controller.GhostValid;
        EnvironmentEditResult first = controller.CommitGhost();
        session.Reserve(lamp.ToDefinition().Id, 250_000);
        controller.Begin(lamp);
        controller.UpdatePointer(new Vector2(300, 500));
        EnvironmentEditResult second = controller.CommitGhost();
        checks.Add(new StartupCheck("environment_free_pointer_anchor_validation",
            wallRejected && floorAccepted, $"wallRejected={wallRejected} floorAccepted={floorAccepted}"));
        checks.Add(new StartupCheck("environment_repeated_ghost_placement_costs_per_instance",
            first.Succeeded && second.Succeeded && session.WorkingLayout.Decorations.Count == 2 &&
            session.ProjectedBalanceMilliCredits == 100_000,
            $"placed={session.WorkingLayout.Decorations.Count} projected={session.ProjectedBalanceMilliCredits}"));

        controller.UpdateRoom(new RoomScreenBounds(200, 150, 1600, 1100));
        var ghost = host.FindChild("EnvironmentPlacementGhost", true, false) as Control;
        checks.Add(new StartupCheck("environment_ghost_preserves_canonical_position_on_resize",
            GodotObject.IsInstanceValid(ghost) && (ghost!.Position + ghost.Size * .5f).IsEqualApprox(new Vector2(600, 975)),
            $"ghost={ghost?.Position}"));
        controller._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        checks.Add(new StartupCheck("environment_escape_cancels_ghost", !controller.Active,
            $"active={controller.Active}"));

        host.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new ScenarioResult(checks.All(check => check.Passed), checks, [$"seed={seed}"]);
    }
}

public sealed class EnvironmentDecoratorScenario : IScenario
{
    public string Id => "environment_decorator";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var progress = new BuddyProgressState(0.018);
        progress.Deposit(250_000);
        var environment = new EnvironmentProgressState();
        var saves = new SaveCoordinator(progress, new InMemoryProgressStore(), environment: environment);
        var economy = new EconomyService(progress, new ToolCatalogue([]));
        var pointer = new LabPointerGrabComponent { Name = "EnvironmentDecoratorPointer" };
        pointer.SetProcessInput(true);
        pointer.SetProcessUnhandledInput(true);
        var boundaries = new BoundaryController { Name = "EnvironmentDecoratorBoundaries" };
        var visuals = new EnvironmentDecorationLayer { Name = "EnvironmentDecoratorVisuals" };
        visuals.Configure(environment, boundaries);
        var decorator = new EnvironmentDecorator { Name = "EnvironmentDecoratorScenario" };
        decorator.Configure(progress, economy, pointer, environment, saves, visuals);
        tree.Root.AddChild(pointer);
        tree.Root.AddChild(boundaries);
        tree.Root.AddChild(visuals);
        tree.Root.AddChild(decorator);
        try
        {
            decorator.Open();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var blocker = decorator.FindChild("EnvironmentDecoratorInputSurface", true, false) as Control
                ?? throw new InvalidOperationException("Decorator input surface was not composed.");
            var catalogue = decorator.FindChild("EnvironmentCatalog", true, false) as DesktopBuddy.UI.Win98.Win98CatalogGrid
                ?? throw new InvalidOperationException("Decorator catalogue was not composed.");
            var panel = decorator.FindChild("EnvironmentDecoratorPanel", true, false) as Control
                ?? throw new InvalidOperationException("Decorator panel was not composed.");
            bool composed = decorator.IsOpen && blocker.Visible &&
                blocker.MouseFilter == Control.MouseFilterEnum.Stop &&
                panel.Visible &&
                decorator.FindChild("EnvironmentCategories", true, false) is Control &&
                decorator.FindChild("EnvironmentCatalog", true, false) is Control;
            checks.Add(new StartupCheck("environment_decorator_composed", composed,
                $"open={decorator.IsOpen} blocker={blocker.Visible}"));
            checks.Add(new StartupCheck("environment_decorator_owns_pointer_input",
                !pointer.IsProcessingInput() && !pointer.IsProcessingUnhandledInput(),
                $"input={pointer.IsProcessingInput()} unhandled={pointer.IsProcessingUnhandledInput()}"));

            var titleBar = panel.FindChild("TitleBar", true, false) as Control
                ?? throw new InvalidOperationException("Decorator title bar was not composed.");
            Vector2 panelBeforeDrag = panel.Position;
            RoomInput(titleBar, new InputEventMouseButton { Position = new Vector2(8, 8), ButtonIndex = MouseButton.Left, Pressed = true });
            RoomInput(titleBar, new InputEventMouseMotion { Position = new Vector2(58, 38) });
            RoomInput(titleBar, new InputEventMouseButton { Position = new Vector2(58, 38), ButtonIndex = MouseButton.Left, Pressed = false });
            decorator._Process(0);
            checks.Add(new StartupCheck("environment_decorator_window_is_movable",
                !panel.Position.IsEqualApprox(panelBeforeDrag), $"before={panelBeforeDrag} after={panel.Position}"));

            EnvironmentDecorationResource lamp = EnvironmentDecorationRegistry.Authored.Entries[0];
            Vector2 viewport = tree.Root.GetViewport().GetVisibleRect().Size;
            Vector2 firstPoint = new(viewport.X * .7f, viewport.Y * .85f);
            var shop = new VBoxContainer { Name = "ShopItemList" };
            tree.Root.AddChild(shop);
            catalogue.Select(lamp.DefinitionId);
            Press(decorator, "EnvironmentBuyButton");
            checks.Add(new StartupCheck("environment_decorator_buy_reserves_one_copy",
                decorator.VisibleWorkingLayout.Decorations.Count == 0 && decorator.VisibleProjectedBalance == 175_000,
                $"placed={decorator.VisibleWorkingLayout.Decorations.Count} projected={decorator.VisibleProjectedBalance}"));
            Press(decorator, "EnvironmentPlaceButton");
            var placementChrome = decorator.FindChild("EnvironmentPlacementChrome", true, false) as Control;
            checks.Add(new StartupCheck("environment_decorator_placement_chrome_is_focused",
                decorator.PlacementMode && !panel.Visible && GodotObject.IsInstanceValid(placementChrome) && placementChrome!.Visible && !shop.Visible,
                $"mode={decorator.PlacementMode} panel={panel.Visible} chrome={placementChrome?.Visible} shop={shop.Visible}"));
            RoomInput(blocker, new InputEventMouseMotion { Position = firstPoint });
            RoomInput(blocker, new InputEventMouseButton { Position = firstPoint, ButtonIndex = MouseButton.Left, Pressed = true });
            Press(decorator, "EnvironmentPlacementDoneButton");
            checks.Add(new StartupCheck("environment_decorator_stages_placement_funds",
                decorator.VisibleWorkingLayout.Decorations.Count == 1 && decorator.VisibleProjectedBalance == 175_000 &&
                panel.Visible && shop.Visible &&
                IsVisible(decorator, "EnvironmentMoveButton") && IsVisible(decorator, "EnvironmentRotateButton") && IsVisible(decorator, "EnvironmentSellButton"),
                $"placed={decorator.VisibleWorkingLayout.Decorations.Count} projected={decorator.VisibleProjectedBalance} controls={IsVisible(decorator, "EnvironmentMoveButton")}"));

            Press(decorator, "EnvironmentMoveButton");
            Vector2 movedPoint = new(viewport.X * .85f, viewport.Y * .82f);
            RoomInput(blocker, new InputEventMouseMotion { Position = movedPoint });
            RoomInput(blocker, new InputEventMouseButton { Position = movedPoint, ButtonIndex = MouseButton.Left, Pressed = true });
            Press(decorator, "EnvironmentRotateButton");
            PlacedDecoration changed = decorator.VisibleWorkingLayout.Decorations.Single();
            checks.Add(new StartupCheck("environment_decorator_moves_and_rotates_selected_instance",
                changed.Position.X > .75f && changed.RotationDegrees == 90,
                $"position={changed.Position} rotation={changed.RotationDegrees}"));
            Press(decorator, "EnvironmentSellButton");
            checks.Add(new StartupCheck("environment_decorator_sell_reverses_staged_cost",
                decorator.VisibleWorkingLayout.Decorations.Count == 0 && decorator.VisibleProjectedBalance == 250_000,
                $"placed={decorator.VisibleWorkingLayout.Decorations.Count} projected={decorator.VisibleProjectedBalance}"));

            catalogue.Select(lamp.DefinitionId);
            Press(decorator, "EnvironmentBuyButton");
            Press(decorator, "EnvironmentPlaceButton");
            RoomInput(blocker, new InputEventMouseMotion { Position = firstPoint });
            RoomInput(blocker, new InputEventMouseButton { Position = firstPoint, ButtonIndex = MouseButton.Left, Pressed = true });
            Press(decorator, "EnvironmentPlacementCancelButton");
            checks.Add(new StartupCheck("environment_decorator_placement_cancel_restores_reservation",
                decorator.VisibleWorkingLayout.Decorations.Count == 0 && decorator.VisibleProjectedBalance == 250_000 && panel.Visible,
                $"placed={decorator.VisibleWorkingLayout.Decorations.Count} projected={decorator.VisibleProjectedBalance}"));

            catalogue.Select(lamp.DefinitionId);
            Press(decorator, "EnvironmentBuyButton");
            Press(decorator, "EnvironmentPlaceButton");
            RoomInput(blocker, new InputEventMouseMotion { Position = firstPoint });
            RoomInput(blocker, new InputEventMouseButton { Position = firstPoint, ButtonIndex = MouseButton.Left, Pressed = true });
            Press(decorator, "EnvironmentPlacementDoneButton");
            Press(decorator, "EnvironmentCancelButton");
            Press(decorator, "EnvironmentDiscardButton");
            checks.Add(new StartupCheck("environment_decorator_cancel_discards_layout_and_wallet",
                !decorator.IsOpen && environment.Layout.Decorations.Count == 0 && progress.BalanceMilliCredits == 250_000 &&
                pointer.IsProcessingInput() && pointer.IsProcessingUnhandledInput(),
                $"open={decorator.IsOpen} saved={environment.Layout.Decorations.Count} balance={progress.BalanceMilliCredits}"));

            var bootstrap = new EnvironmentCustomizationBootstrap { Name = "DecoratorLauncherBootstrap" };
            tree.Root.AddChild(bootstrap);
            bool launcherAttached = bootstrap.AttachDecorLauncherForStartupTest(decorator);
            var launcher = shop.FindChild("EnvironmentDecorateRoomButton", true, false) as Button;
            launcher!.EmitSignal(Button.SignalName.Pressed);
            checks.Add(new StartupCheck("environment_decorator_shop_launcher_opens_workspace",
                launcherAttached && decorator.IsOpen, $"attached={launcherAttached} open={decorator.IsOpen}"));
            catalogue.Select(lamp.DefinitionId);
            Press(decorator, "EnvironmentBuyButton");
            Press(decorator, "EnvironmentPlaceButton");
            RoomInput(blocker, new InputEventMouseMotion { Position = firstPoint });
            RoomInput(blocker, new InputEventMouseButton { Position = firstPoint, ButtonIndex = MouseButton.Left, Pressed = true });
            Press(decorator, "EnvironmentPlacementDoneButton");
            progress.Deposit(1_000);
            Press(decorator, "EnvironmentDoneButton");
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            checks.Add(new StartupCheck("environment_decorator_done_commits_layout_and_wallet",
                !decorator.IsOpen && environment.Layout.Decorations.Count == 1 && progress.BalanceMilliCredits == 176_000 &&
                pointer.IsProcessingInput() && pointer.IsProcessingUnhandledInput(),
                $"open={decorator.IsOpen} saved={environment.Layout.Decorations.Count} balance={progress.BalanceMilliCredits}"));

            launcher.EmitSignal(Button.SignalName.Pressed);
            catalogue.Select(lamp.DefinitionId);
            Press(decorator, "EnvironmentBuyButton");
            Press(decorator, "EnvironmentPlaceButton");
            RoomInput(blocker, new InputEventMouseMotion { Position = firstPoint });
            RoomInput(blocker, new InputEventMouseButton { Position = firstPoint, ButtonIndex = MouseButton.Left, Pressed = true });
            Press(decorator, "EnvironmentPlacementDoneButton");
            progress.Adopt(progress.Snapshot() with { Revision = progress.Revision + 1, BalanceMilliCredits = 10_000 });
            Press(decorator, "EnvironmentDoneButton");
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var status = decorator.FindChild("EnvironmentDecoratorStatus", true, false) as Label;
            checks.Add(new StartupCheck("environment_decorator_rejects_current_wallet_shortfall",
                decorator.IsOpen && environment.Layout.Decorations.Count == 1 && progress.BalanceMilliCredits == 10_000 &&
                status?.Text.StartsWith("Save failed:", StringComparison.Ordinal) == true,
                $"open={decorator.IsOpen} saved={environment.Layout.Decorations.Count} balance={progress.BalanceMilliCredits} status={status?.Text}"));
            Press(decorator, "EnvironmentCancelButton");
            Press(decorator, "EnvironmentDiscardButton");
            bootstrap.QueueFree();
            shop.QueueFree();
        }
        finally
        {
            decorator.QueueFree();
            visuals.QueueFree();
            boundaries.QueueFree();
            pointer.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        return new ScenarioResult(checks.All(check => check.Passed), checks, [$"seed={seed}"]);
    }

    private static void Press(Node root, string name) =>
        ((Button)(root.FindChild(name, true, false) ?? throw new InvalidOperationException($"Missing {name}.")))
            .EmitSignal(Button.SignalName.Pressed);

    private static void RoomInput(Control blocker, InputEvent input) =>
        blocker.EmitSignal(Control.SignalName.GuiInput, input);

    private static bool IsVisible(Node root, string name) => root.FindChild(name, true, false) is Control control && control.Visible;
}
