using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Automation;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Content;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Economy;
using DesktopBuddy.Onboarding;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Platform;
#if !DESKTOP_BUDDY_PUBLIC_WEB
using DesktopBuddy.Sharing;
#endif
#if !DESKTOP_BUDDY_NO_DEV_TOOLS
using DesktopBuddy.Testing;
#endif
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
#if DESKTOP_BUDDY_NO_DEV_TOOLS
                Log.Warn(Category, "Scenario/journey runner is unavailable in this build; starting normal sandbox.");
                await BootSandboxAsync();
#else
                BootTestRunner(args);
#endif
                break;

            default:
                await BootSandboxAsync();
                break;
        }
    }

#if !DESKTOP_BUDDY_NO_DEV_TOOLS
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
#endif

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
            GeneratedBuddyCosmeticCatalogueResource generatedCosmetics =
                GD.Load<GeneratedBuddyCosmeticCatalogueResource>(BuddyGeneratedCosmeticRegistry.CataloguePath)
                ?? throw new InvalidOperationException(
                    $"Missing generated cosmetic catalogue at {BuddyGeneratedCosmeticRegistry.CataloguePath}.");
            resources =
            [
                CatalogueLoader.Definition,
                CatalogueLoader.GeneratedDefinition,
                generatedCosmetics,
            ];
            _ = BuddyGeneratedCosmeticRegistry.Current.FeatureCatalog;
            _ = CatalogueLoader.Catalogue;
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
        bool browser = OperatingSystem.IsBrowser();
        string saveRoot = ProjectSettings.GlobalizePath("user://");
        string progressPath = ProjectSettings.GlobalizePath("user://progress.json");
        string settingsPath = ProjectSettings.GlobalizePath("user://settings.json");
        string characterRoot = ProjectSettings.GlobalizePath("user://characters");
        IAtomicSaveFileSystem saveFileSystem = browser
            ? new GodotBrowserAtomicSaveFileSystem()
            : new AtomicSaveFileSystem();
        var baseStore = new JsonProgressStore(progressPath, settingsPath, saveFileSystem);
        IProgressStore runtimeStore = DemoScope.IncludesScenes
            ? baseStore
            : new LegacyProgressCompatibilityStore(baseStore, saveRoot, saveFileSystem);

        Log.Info(
            Category,
            $"Loading persistence browser={browser} progress={progressPath} settings={settingsPath} scenes={DemoScope.IncludesScenes}");

        LoadResult<ProgressSave> progressLoad;
        LoadResult<LocalSettingsSave> settingsLoad;
        SceneProgressCoordinator? sceneProgress = null;
        try
        {
            Task<LoadResult<LocalSettingsSave>> settingsTask =
                baseStore.LoadSettingsAsync(CancellationToken.None);

            if (DemoScope.IncludesScenes)
            {
                Task<SceneBootCompatibility> sceneTask = LoadSceneProgressAsync(
                    baseStore,
                    saveFileSystem,
                    progressPath,
                    saveRoot,
                    cashPerPain,
                    browser,
                    CancellationToken.None);
                await Task.WhenAll(sceneTask, settingsTask);
                SceneBootCompatibility sceneBoot = await sceneTask;
                sceneProgress = sceneBoot.SceneProgress;
                progressLoad = sceneBoot.CompatibilityProgressLoad;
                runtimeStore = new LegacyProgressCompatibilityStore(
                    baseStore,
                    saveRoot,
                    saveFileSystem);
            }
            else
            {
                Task<LoadResult<ProgressSave>> progressTask =
                    runtimeStore.LoadProgressAsync(CancellationToken.None);
                await Task.WhenAll(progressTask, settingsTask);
                progressLoad = await progressTask;
            }

            settingsLoad = await settingsTask;
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Progress load failed: {exception.Message}");
            QuitSafely(3);
            return;
        }

        Log.Info(
            Category,
            $"Persistence loaded progress={progressLoad.Status} settings={settingsLoad.Status} " +
            $"semanticOwner={(sceneProgress is null ? "legacy" : "scene")}");

        if (progressLoad.Status == SaveLoadStatus.UnsupportedFutureVersion)
        {
            Log.Error(Category, $"Progress is from a newer build: {progressLoad.Detail}");
            QuitSafely(3);
            return;
        }

        bool newSemanticState = progressLoad.Status is
            SaveLoadStatus.NewSave or SaveLoadStatus.DefaultsRecovered;
        ProgressSave? loadedProgress = progressLoad.Value;

        BuddyProgressState progress = sceneProgress is not null
            ? ProgressSavePolicy.CreateState(
                loadedProgress ?? throw new InvalidOperationException("Scene compatibility load returned no progress."),
                cashPerPain)
            : newSemanticState
                ? ProgressReset.CreateNewProgress(cashPerPain)
                : ProgressSavePolicy.CreateState(
                    loadedProgress ?? throw new InvalidOperationException("Load returned no progress."),
                    cashPerPain);
        WorkProgressState workProgress = sceneProgress?.Work ??
            (newSemanticState
                ? new WorkProgressState()
                : loadedProgress?.Work?.CreateState() ?? new WorkProgressState());
        EnvironmentProgressState environmentProgress = newSemanticState && sceneProgress is null
            ? new EnvironmentProgressState()
            : loadedProgress?.Environment?.CreateState() ?? new EnvironmentProgressState();
        var characterSelection = new CharacterSelectionState(
            newSemanticState && sceneProgress is null ? null : loadedProgress?.ActiveCharacterId);
        ICharacterFileSystem characterFileSystem = browser
            ? new GodotBrowserCharacterFileSystem()
            : new CharacterFileSystem();
        var characters = new CharacterStore(
            characterFileSystem,
            characterRoot,
            featureCatalog: BuddyGeneratedCosmeticRegistry.Current.FeatureCatalog);
        var economy = sceneProgress is not null
            ? new EconomyService(sceneProgress.Player, CatalogueLoader.Catalogue)
            : new EconomyService(progress, CatalogueLoader.Catalogue);

        SaveCoordinator saves = sceneProgress is not null
            ? new SaveCoordinator(progress, runtimeStore, progress.Revision)
            : new SaveCoordinator(
                progress,
                runtimeStore,
                newSemanticState ? -1 : progress.Revision,
                characterSelection,
                newSemanticState ? -1 : characterSelection.Revision,
                workProgress,
                newSemanticState ? -1 : workProgress.Revision,
                environmentProgress,
                newSemanticState ? -1 : environmentProgress.Revision);
        var settings = settingsLoad.Value ?? new LocalSettingsSave();

        if (progressLoad.QuarantinedPath is not null)
            Log.Warn(Category, $"Corrupt progress quarantined at {progressLoad.QuarantinedPath}.");
        if (settingsLoad.QuarantinedPath is not null)
            Log.Warn(Category, $"Corrupt settings quarantined at {settingsLoad.QuarantinedPath}.");

        if (sceneProgress is null && newSemanticState && !browser)
        {
            try
            {
                await saves.FlushProgressAsync(force: true);
                Log.Info(Category, "Initial progress save completed.");
            }
            catch (Exception exception)
            {
                Log.Error(Category, $"Initial progress save failed; state remains dirty: {exception.Message}");
            }
        }
        else if (sceneProgress is null && newSemanticState)
        {
            Log.Info(Category, "Browser first-run save deferred until normal autosave; continuing boot.");
        }

        var context = new RunContext(
            progress,
            economy,
            runtimeStore,
            saves,
            settings,
            progressLoad.Status,
            TimeSource: null,
            CharacterSelection: characterSelection,
            Characters: characters,
            WorkProgress: workProgress,
            EnvironmentProgress: environmentProgress,
            SceneProgress: sceneProgress);
        sandbox.Shell.ConfigureRuntime(settings, saves);
        sandbox.Configure(context);

#if !DESKTOP_BUDDY_PUBLIC_WEB
        var environmentCustomization = GetNodeOrNull<DesktopBuddy.Environment.EnvironmentCustomizationBootstrap>(
            "/root/EnvironmentCustomizationBootstrap");
        environmentCustomization?.Configure(sandbox);
#endif
        GetNodeOrNull<DesktopBuddy.CharacterEditor.CharacterSlotUiBootstrap>(
            "/root/CharacterSlotUiBootstrap")?.Configure(sandbox, characters);
        var commandRegistrar = GetNodeOrNull<DesktopBuddy.UI.Win98.Win98CommandBarBootstrap>(
            "/root/Win98CommandBarBootstrap");

        var characterRuntime = new CharacterSelectionRuntime
        {
            Name = nameof(CharacterSelectionRuntime),
        };
        characterRuntime.Configure(sandbox, context);
        sandbox.AddChild(characterRuntime);

        AddChild(sandbox);

        TutorialStepIds.Active = DemoScope.TutorialSteps;
        if (TutorialStepIds.Active.Count < TutorialStepIds.Ordered.Count)
        {
            Log.Info(
                Category,
                $"First-session tutorial scoped to {TutorialStepIds.Active.Count} of " +
                $"{TutorialStepIds.Ordered.Count} steps by the active distribution scope.");
        }

#if !DESKTOP_BUDDY_PUBLIC_WEB
        Node? initializedSteamBridge = null;
        if (DemoScope.IncludesWorkshop)
        {
            var workshop = new WorkshopBootstrap { Name = nameof(WorkshopBootstrap) };
            workshop.Configure(characters, characterSelection, sandbox, environmentCustomization, commandRegistrar);
            AddChild(workshop);
            initializedSteamBridge = workshop.InitializedSteamBridge;
        }
        else
        {
            Log.Info(Category, "Steam Workshop excluded by this build's distribution scope.");
        }

#if DESKTOP_BUDDY_ACHIEVEMENTS
        if (DemoScope.IncludesAchievements)
        {
            if (sceneProgress is null)
                throw new InvalidOperationException("Achievement-enabled build must use Scene-owned progress.");

            var achievements = new DesktopBuddy.Achievements.AchievementBootstrap
            {
                Name = nameof(DesktopBuddy.Achievements.AchievementBootstrap),
            };
            achievements.Configure(sandbox, context, initializedSteamBridge);
            AddChild(achievements);
        }
#endif
#endif

        var guidance = new FirstSessionGuidanceController
        {
            Name = nameof(FirstSessionGuidanceController),
        };
        guidance.Configure(sandbox, context);
        AddChild(guidance);

        var inputBridge = new GameplayInputModeBridge
        {
            Name = nameof(GameplayInputModeBridge),
        };
        inputBridge.Configure(sandbox);
        sandbox.AddChild(inputBridge);

        Log.Info(Category, "Sandbox boot completed and gameplay scene is attached.");
        if (browser)
            GD.Print("DESKTOP_BUDDY_WEB_READY");
    }

    private void QuitSafely(int exitCode)
    {
        GodotInteropShutdown.PrepareForQuit();
        GetTree().Quit(exitCode);
    }
}