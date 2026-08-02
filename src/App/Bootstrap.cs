using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Automation;
using DesktopBuddy.Content;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Testing;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Main-scene composition root and the single entrypoint router. It parses the
/// headless runner / automation command line, composes the development-only
/// <see cref="AutomationDriver"/> when allowed, and routes to the sandbox
/// (normal boot), the scenario runner, or the journey runner. It holds no
/// gameplay logic — it only composes and routes (ARCHITECTURE.md Section 3).
/// </summary>
public partial class Bootstrap : Node
{
    private const string Category = "Bootstrap";

    public override async void _Ready()
    {
        RunnerArguments args;
        try
        {
            args = RunnerArguments.Parse(OS.GetCmdlineUserArgs());
        }
        catch (ArgumentException e)
        {
            Log.Error(Category, $"Invalid runner arguments: {e.Message}");
            QuitSafely(2);
            return;
        }

        bool headless = DisplayServer.GetName() == "headless";
        Log.Info(Category, $"Boot mode={args.Mode} automation={args.AutomationEnabled} headless={headless} debug={BuildInfo.IsDebugBuild}");

        ComposeAutomation(args);

        switch (args.Mode)
        {
            case RunnerMode.Scenario:
            case RunnerMode.Journey:
                BootTestRunner(args);
                break;

            default:
                await BootSandboxAsync();
                break;
        }
    }

    private void BootTestRunner(RunnerArguments args)
    {
        var packed = GD.Load<PackedScene>("res://scenes/test_runner.tscn");
        if (packed is null)
        {
            Log.Error(Category, "Missing res://scenes/test_runner.tscn; cannot run scenario/journey.");
            QuitSafely(2);
            return;
        }

        var host = packed.Instantiate<TestRunner>();
        host.Configure(args);
        AddChild(host);
    }

    private void ComposeAutomation(RunnerArguments args)
    {
        if (!args.AutomationEnabled)
            return;

        if (!BuildInfo.AutomationAllowed)
        {
            Log.Warn(Category, "Automation requested but this is not a debug build; ignoring.");
            return;
        }

        var driver = new AutomationDriver { Name = nameof(AutomationDriver) };
        driver.Configure(args);
        AddChild(driver);
    }

    private async Task BootSandboxAsync()
    {
        GameResource[] resources;
        try
        {
            resources = [CatalogueLoader.Definition];
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Catalogue load failed: {exception.Message}");
            QuitSafely(2);
            return;
        }

        StartupReport report = StartupValidator.Validate(resources);
        if (!report.Ok)
            Log.Error(Category, "Startup validation failed; sandbox may be unstable.");

        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        if (packed is null)
        {
            Log.Error(Category, "Missing res://scenes/sandbox.tscn; cannot boot sandbox.");
            return;
        }

        var sandbox = packed.Instantiate<SandboxRoot>();
        double cashPerPain = sandbox.Pipeline.RequirePainProfile().CashPerPain;
        string progressPath = ProjectSettings.GlobalizePath("user://progress.json");
        string settingsPath = ProjectSettings.GlobalizePath("user://settings.json");
        string characterRoot = ProjectSettings.GlobalizePath("user://characters");
        var store = new JsonProgressStore(progressPath, settingsPath);

        LoadResult<ProgressSave> progressLoad;
        LoadResult<LocalSettingsSave> settingsLoad;
        try
        {
            Task<LoadResult<ProgressSave>> progressTask =
                store.LoadProgressAsync(CancellationToken.None);
            Task<LoadResult<LocalSettingsSave>> settingsTask =
                store.LoadSettingsAsync(CancellationToken.None);
            await Task.WhenAll(progressTask, settingsTask);
            progressLoad = await progressTask;
            settingsLoad = await settingsTask;
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Progress load failed: {exception.Message}");
            QuitSafely(3);
            return;
        }

        if (progressLoad.Status == SaveLoadStatus.UnsupportedFutureVersion)
        {
            Log.Error(Category, $"Progress is from a newer build: {progressLoad.Detail}");
            QuitSafely(3);
            return;
        }

        bool newSemanticState = progressLoad.Status is
            SaveLoadStatus.NewSave or SaveLoadStatus.DefaultsRecovered;
        ProgressSave? loadedProgress = progressLoad.Value;
        BuddyProgressState progress = newSemanticState
            ? ProgressReset.CreateNewProgress(cashPerPain)
            : ProgressSavePolicy.CreateState(
                loadedProgress ?? throw new InvalidOperationException("Load returned no progress."),
                cashPerPain);
        var characterSelection = new CharacterSelectionState(
            newSemanticState ? null : loadedProgress?.ActiveCharacterId);
        var characters = new CharacterStore(
            new CharacterFileSystem(),
            characterRoot);
        var economy = new EconomyService(progress, CatalogueLoader.Catalogue);
        var saves = new SaveCoordinator(
            progress,
            store,
            newSemanticState ? -1 : progress.Revision,
            characterSelection,
            newSemanticState ? -1 : characterSelection.Revision);
        var settings = settingsLoad.Value ?? new LocalSettingsSave();

        if (progressLoad.QuarantinedPath is not null)
            Log.Warn(Category, $"Corrupt progress quarantined at {progressLoad.QuarantinedPath}.");
        if (settingsLoad.QuarantinedPath is not null)
            Log.Warn(Category, $"Corrupt settings quarantined at {settingsLoad.QuarantinedPath}.");

        if (newSemanticState)
        {
            try
            {
                await saves.FlushProgressAsync(force: true);
            }
            catch (Exception exception)
            {
                Log.Error(Category, $"Initial progress save failed; state remains dirty: {exception.Message}");
            }
        }

        var context = new RunContext(
            progress,
            economy,
            store,
            saves,
            settings,
            progressLoad.Status,
            TimeSource: null,
            CharacterSelection: characterSelection,
            Characters: characters);
        sandbox.Shell.ConfigureRuntime(settings, saves);
        sandbox.Configure(context);
        var characterRuntime = new CharacterSelectionRuntime
        {
            Name = nameof(CharacterSelectionRuntime),
        };
        characterRuntime.Configure(sandbox, context);
        sandbox.AddChild(characterRuntime);
        AddChild(sandbox);
    }

    private void QuitSafely(int exitCode)
    {
        GodotInteropShutdown.PrepareForQuit();
        GetTree().Quit(exitCode);
    }
}
