using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Environment;
using DesktopBuddy.Persistence;
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
                bandsMatch &= presenter.ZIndex == EnvironmentDecorationPresenter.ZFor(definition.RenderBand);
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
        checks.Add(new StartupCheck("environment_paint_background_command_registered",
            bootstrap.HasPaintBackgroundRegistration &&
            tree.Root.FindChild(nameof(EnvironmentBackgroundEditor), true, false) is EnvironmentBackgroundEditor,
            $"registered={bootstrap.HasPaintBackgroundRegistration}"));
        bootstrap.QueueFree();
        commandBar.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new ScenarioResult(checks.All(check => check.Passed), checks, [$"seed={seed}"]);
    }
}
