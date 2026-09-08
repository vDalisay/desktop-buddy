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
                // Shipping builds omit the entire developer scenario tree — every distribution,
                // not just the browser one. A crafted argument must fall back to normal gameplay:
                // the runner is a live unlock otherwise, because scenarios set DemoScope overrides.
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
        IProgressStore runtimeStore = baseStore;

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
                // Scene-enabled builds choose the persistence format before progress.json is decoded.
                // A committed Scene manifest therefore remains authoritative even if the old
                // aggregate path is corrupt or belongs to an incompatible schema.
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

                // From this point onward the Scene manifest is the semantic commit point. Legacy
                // consumers may still use SaveCoordinator for local settings, but any accidental
                // progress write fails closed instead of overwriting account-only progress.json.
                runtimeStore = new LegacyProgressCompatibilityStore(
                    baseStore,
                    saveRoot,
                    saveFileSystem);
            }
            else
            {
                Task<LoadResult<ProgressSave>> progressTask =
                    baseStore.LoadProgressAsync(CancellationToken.None);
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

        // In Scene mode the helper already chose/committed the authoritative graph. Rehydrate this
        // object only as a compatibility view for legacy consumers; never roll another fresh state
        // here or traits/selection could diverge from the committed primary Buddy.
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

        // Scene builds retain this coordinator only for machine-local settings and compatibility
        // APIs. Do not attach Character, Work or Environment semantic state to it: each has a
        // Scene-owned persistence route and the compatibility store blocks aggregate writes anyway.
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
            // A first-run browser build must never gate its first rendered frame on durable
            // filesystem synchronization. The state stays dirty and the normal autosave path
            // persists it after gameplay is alive. This also protects experimental single-threaded
            // Web runtimes from turning a save backend regression into a permanent grey boot page.
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

        // Feature autoloads may exist before the sandbox enters the tree. Give them the
        // composition-root references directly so normal boot does not discover runtime services
        // by recursively walking the scene tree or bypass the injected persistence policy.
#if !DESKTOP_BUDDY_PUBLIC_WEB
        // The reduced distribution ships no room workspace, and its autoload is stripped from
        // project.godot to match, so there is nothing to configure. Its only other consumer is the
        // Workshop composition below, which that distribution also omits.
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

        // Let SandboxRoot initialize the real pointer and all tool controllers first. The
        // bridge is added afterwards so its initial Work-mode application cannot be undone
        // by LabPointerGrabComponent.Initialize during the parent's _Ready callback.
        AddChild(sandbox);

        // Every distribution walks the player through what it actually ships: itch.io drops the
        // Paint Room, Buddy Studio and Work Mode chapters rather than the whole walkthrough.
        TutorialStepIds.Active = DemoScope.TutorialSteps;
        if (TutorialStepIds.Active.Count < TutorialStepIds.Ordered.Count)
        {
            Log.Info(
                Category,
                $"First-session tutorial scoped to {TutorialStepIds.Active.Count} of " +
                $"{TutorialStepIds.Ordered.Count} steps by the active distribution scope.");
        }

#if !DESKTOP_BUDDY_PUBLIC_WEB
        if (DemoScope.IncludesWorkshop)
        {
            // Steam exports and editor runs compose Workshop; itch.io omits its services and menu command.
            var workshop = new WorkshopBootstrap { Name = nameof(WorkshopBootstrap) };
            workshop.Configure(characters, characterSelection, sandbox, environmentCustomization, commandRegistrar);
            AddChild(workshop);
        }
        else
        {
            Log.Info(Category, "Steam Workshop excluded by this build's distribution scope.");
        }
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
