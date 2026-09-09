using System;
#if DESKTOP_BUDDY_ACHIEVEMENTS
using DesktopBuddy.Achievements;
#endif
using DesktopBuddy.App;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Environment;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sharing;

/// <summary>
/// Optional Steam social composition root. Failure to find GodotSteam, Steam, an AppID, or a
/// network connection only disables remote features; it never participates in sandbox startup.
/// UI/application dependencies are injected by the main composition root rather than discovered
/// by polling absolute scene-tree paths. A Steam client/session initialization failure keeps the
/// same bridge and transport alive and retries with backoff so optional Steam services recover
/// without rebinding application services.
/// </summary>
public partial class WorkshopBootstrap : Node
{
    private const string Category = "Workshop";
    private const string BridgeScriptPath = "res://src/Platform/Steam/GodotSteamBridge.gd";
    private const double SteamInitialRetrySeconds = 30.0;
    private const double SteamMaximumRetrySeconds = 300.0;

    private CharacterStore? _characters;
    private CharacterSelectionState? _selection;
    private IRoomPaintingSharingHost? _environment;
    private SandboxRoot? _sandbox;
    private CharacterSlotEntitlementState? _slots;
    private ITopLevelCommandRegistrar? _commandRegistrar;
    private WorkshopSharingCoordinator? _sharing;
    private WorkshopStagingStore? _staging;
    private RoomPaintingLibraryStore? _rooms;
    private WorkshopPanel? _panel;
#if DESKTOP_BUDDY_ACHIEVEMENTS
    private AchievementBootstrap? _achievements;
#endif
    private IDisposable? _commandRegistration;
    private ISteamWorkshopTransport? _transport;
    private Node? _steamBridge;
    private GodotSteamWorkshopTransport? _retrySteamTransport;
    private SteamAppIdentity? _retrySteamIdentity;
    private double _steamRetryCountdown;
    private double _steamRetryDelay = SteamInitialRetrySeconds;
    private bool _servicesComposed;

    internal WorkshopSharingCoordinator? Sharing => _sharing;
    internal ISteamWorkshopTransport? Transport => _transport;
    internal RoomPaintingLibraryStore? RoomLibrary => _rooms;
#if DESKTOP_BUDDY_ACHIEVEMENTS
    internal AchievementBootstrap? Achievements => _achievements;
#endif

    public void Configure(
        CharacterStore characters,
        CharacterSelectionState selection,
        SandboxRoot sandbox,
        IRoomPaintingSharingHost? environment = null,
        ITopLevelCommandRegistrar? commandRegistrar = null)
    {
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _slots = new CharacterSlotEntitlementState(sandbox.Progress, sandbox.Economy);
        _environment = environment;
        _commandRegistrar = commandRegistrar;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ComposeServices();
#if DESKTOP_BUDDY_ACHIEVEMENTS
        ComposeAchievements();
#endif
        if (DisplayServer.GetName() != "headless")
            ComposeUi();
        SetProcess(_retrySteamTransport is not null);
    }

    public override void _Process(double delta)
    {
        if (_retrySteamTransport is null || !_retrySteamIdentity.HasValue ||
            !GodotObject.IsInstanceValid(_steamBridge))
        {
            ClearSteamRetry();
            return;
        }

        _steamRetryCountdown -= Math.Max(0.0, delta);
        if (_steamRetryCountdown > 0.0)
            return;

        TryRecoverSteam();
    }

    public override void _ExitTree()
    {
        ClearSteamRetry();
        _commandRegistration?.Dispose();
        _commandRegistration = null;
        if (GodotObject.IsInstanceValid(_panel)) _panel!.QueueFree();
        _panel = null;
#if DESKTOP_BUDDY_ACHIEVEMENTS
        _achievements = null;
#endif
        _steamBridge = null;
        base._ExitTree();
    }

#if DESKTOP_BUDDY_ACHIEVEMENTS
    private void ComposeAchievements()
    {
        if (_sandbox is null)
        {
            Log.Warn(Category, "Achievement tracking was not composed because the sandbox was not injected.");
            return;
        }

        _achievements = new AchievementBootstrap { Name = nameof(AchievementBootstrap) };
        _achievements.Configure(
            _sandbox,
            _selection,
            _characters,
            _environment as IEnvironmentCustomizationEvents,
            _steamBridge);
        AddChild(_achievements);
    }
#endif

    private void ComposeUi()
    {
        if (_sharing is null || _rooms is null || _selection is null || _characters is null || _slots is null || _sandbox is null)
            return;
        if (_environment is null || _commandRegistrar is null)
        {
            Log.Warn(Category, "Workshop services are available, but UI hosts were not injected; leaving the in-game Workshop window uncomposed.");
            return;
        }

        var previews = new WorkshopPreviewCapture(_characters, _sandbox.Buddy, _sandbox.VisualPresenter)
        {
            Name = nameof(WorkshopPreviewCapture),
        };
        AddChild(previews);
        _panel = new WorkshopPanel { Name = nameof(WorkshopPanel) };
        _panel.Configure(_sharing, _rooms, _environment, _selection, previews);
        _panel.ConfigureBuddyImportPolicy(_characters, _slots);
        AddChild(_panel);
        _commandRegistration = _commandRegistrar.RegisterTopLevelCommand(
            new TopLevelCommandDefinition(
                TopLevelCommandIds.Workshop,
                "Workshop",
                "Share and import room paintings and buddies through Steam Workshop.",
                TopLevelCommandIds.WorkshopOrder),
            _panel.Open,
            isEnabled: () => GodotObject.IsInstanceValid(_panel) && !_panel!.IsOpen);
        Log.Info(Category, $"Workshop UI composed; transport={_transport?.GetType().Name ?? "none"} available={_sharing.IsAvailable}.");
    }

    private void ComposeServices()
    {
        if (_servicesComposed) return;
        if (_characters is null || _selection is null)
        {
            Log.Warn(Category, "Workshop bootstrap was not configured; leaving Steam integration disabled.");
            _servicesComposed = true;
            _transport = new NullSteamWorkshopTransport("Workshop composition was not configured.");
            return;
        }

        string sharingRoot = ProjectSettings.GlobalizePath("user://sharing/workshop");
        string roomLibraryRoot = ProjectSettings.GlobalizePath("user://shared_rooms");
        _staging = new WorkshopStagingStore(sharingRoot);
        _staging.CleanupStale(TimeSpan.FromDays(2), DateTimeOffset.UtcNow);
        _rooms = new RoomPaintingLibraryStore(roomLibraryRoot);
        _transport = ComposeTransport();

        string appVersion = ResolveAppVersion();
        var roomExporter = new RoomShareExporter(_staging, appVersion);
        var roomImporter = new RoomShareImporter(_staging, _rooms);
        var characterExporter = new CharacterShareExporter(_staging, _characters, appVersion);
        var characterImporter = new CharacterShareImporter(
            _staging,
            _characters,
            canCreateNewCharacter: () => _slots is null || _characters.CountStoredCharacters() < _slots.Capacity);
        _sharing = new WorkshopSharingCoordinator(
            _transport,
            _staging,
            roomExporter,
            roomImporter,
            characterExporter,
            characterImporter);
        _servicesComposed = true;
    }

    private ISteamWorkshopTransport ComposeTransport()
    {
        string emulator = OS.GetEnvironment("DESKTOP_BUDDY_WORKSHOP_EMULATOR");
        if (BuildInfo.IsDebugBuild && string.Equals(emulator, "1", StringComparison.Ordinal))
        {
            string emulatorRoot = ProjectSettings.GlobalizePath("user://sharing/workshop-emulator");
            Log.Info(Category, $"Using directory Workshop emulator at {emulatorRoot}.");
            return new DirectoryWorkshopTransport(emulatorRoot);
        }

        SteamAppIdentity identity = SteamAppIdentityResolver.Resolve();
        if (!identity.IsConfigured)
        {
            return new NullSteamWorkshopTransport(
                $"Steam identity is incomplete. Configure '{SteamAppIdentityResolver.RuntimeProjectSetting}' for the running app and '{SteamAppIdentityResolver.WorkshopOwnerProjectSetting}' for the base Workshop owner, or use the development environment overrides.");
        }

        Node? bridge = null;
        GodotSteamWorkshopTransport? transport = null;
        try
        {
            GDScript? script = GD.Load<GDScript>(BridgeScriptPath);
            if (script is null)
                return new NullSteamWorkshopTransport("The project-owned GodotSteam bridge script could not be loaded.");
            GodotObject instance = (GodotObject)script.New();
            if (instance is not Node bridgeNode)
                return new NullSteamWorkshopTransport("The GodotSteam bridge did not instantiate as a Node.");
            bridge = bridgeNode;
            bridge.Name = "GodotSteamBridge";
            AddChild(bridge);

            transport = new GodotSteamWorkshopTransport { Name = nameof(GodotSteamWorkshopTransport) };
            AddChild(transport);

            bool initialized = transport.Initialize(bridge, identity);
            if (!initialized && !transport.CanRetryInitialization)
            {
                string permanentReason = transport.UnavailableReason ?? "GodotSteam initialization failed.";
                Log.Warn(Category, permanentReason);
                DisposeFailedTransport(bridge, transport);
                return new NullSteamWorkshopTransport(permanentReason);
            }

            // One Steam bridge is owned by this composition root and injected into all optional
            // Steam adapters. A retryable initialization failure deliberately keeps this exact
            // bridge + transport object graph alive so existing coordinators recover in place.
            _steamBridge = bridge;
            ConnectOverlayPause(bridge);

            ISteamWorkshopTransport composed = WrapSteamTransport(transport, identity);
            if (!initialized)
            {
                ArmSteamRetry(transport, identity);
                Log.Warn(
                    Category,
                    $"Steam client/session unavailable: {transport.UnavailableReason ?? "initialization failed"} " +
                    $"Retrying in {SteamInitialRetrySeconds:0}s while local play remains available.");
                return composed;
            }

            Log.Info(
                Category,
                $"Steam Workshop initialized; runtimeAppId={identity.RuntimeAppId} workshopOwnerAppId={identity.WorkshopOwnerAppId} crossApp={identity.IsCrossApp} transport={composed.GetType().Name}.");
            return composed;
        }
        catch (Exception exception)
        {
            DisposeFailedTransport(bridge, transport);
            Log.Warn(Category, $"Steam integration disabled: {exception.Message}");
            return new NullSteamWorkshopTransport(exception.Message);
        }
    }

    private static ISteamWorkshopTransport WrapSteamTransport(
        GodotSteamWorkshopTransport transport,
        SteamAppIdentity identity)
    {
        if (!identity.IsCrossApp)
            return transport;

        // The transport captures the two authorized identities before steamInitEx. That lets this
        // wrapper be constructed even while initialization is retrying, while every actual remote
        // operation remains unavailable until the inner transport becomes initialized.
        return new MirroringSteamWorkshopTransport(
            transport,
            identity.RuntimeAppId,
            identity.WorkshopOwnerAppId);
    }

    private void ArmSteamRetry(GodotSteamWorkshopTransport transport, SteamAppIdentity identity)
    {
        _retrySteamTransport = transport;
        _retrySteamIdentity = identity;
        _steamRetryDelay = SteamInitialRetrySeconds;
        _steamRetryCountdown = SteamInitialRetrySeconds;
        SetProcess(true);
    }

    private void TryRecoverSteam()
    {
        GodotSteamWorkshopTransport transport = _retrySteamTransport!;
        SteamAppIdentity identity = _retrySteamIdentity!.Value;
        Node bridge = _steamBridge!;

        if (transport.Initialize(bridge, identity))
        {
            Log.Info(
                Category,
                $"Steam recovered in-session; runtimeAppId={identity.RuntimeAppId} workshopOwnerAppId={identity.WorkshopOwnerAppId}. " +
                "Optional Steam services are available without restarting.");
            ClearSteamRetry();
            return;
        }

        string reason = transport.UnavailableReason ?? "Steam initialization failed.";
        if (!transport.CanRetryInitialization)
        {
            Log.Warn(Category, $"Steam recovery stopped after a permanent initialization failure: {reason}");
            ClearSteamRetry();
            return;
        }

        _steamRetryDelay = Math.Min(SteamMaximumRetrySeconds, _steamRetryDelay * 2.0);
        _steamRetryCountdown = _steamRetryDelay;
        Log.Warn(Category, $"Steam is still unavailable: {reason} Retrying in {_steamRetryDelay:0}s.");
    }

    private void ClearSteamRetry()
    {
        _retrySteamTransport = null;
        _retrySteamIdentity = null;
        _steamRetryCountdown = 0.0;
        _steamRetryDelay = SteamInitialRetrySeconds;
        if (IsInsideTree())
            SetProcess(false);
    }

    /// <summary>
    /// Shift+Tab pauses the game. The bridge is the only live Steam object in the process, so
    /// the overlay callback rides along with the Workshop transport rather than earning a second
    /// initialization: no GodotSteam, no overlay, and nothing to pause for.
    /// </summary>
    private void ConnectOverlayPause(Node bridge)
    {
        if (_sandbox is null)
            return;

        SandboxRoot sandbox = _sandbox;
        bridge.Connect(
            "steam_overlay_toggled",
            Callable.From<bool>(active =>
            {
                if (!GodotObject.IsInstanceValid(sandbox) || !sandbox.Lifecycle.IsInitialized)
                    return;
                sandbox.Lifecycle.PauseCoordinator.Set(GameplayPauseReason.SteamOverlay, active);
            }));
    }

    private void DisposeFailedTransport(Node? bridge, GodotSteamWorkshopTransport? transport)
    {
        if (GodotObject.IsInstanceValid(transport))
        {
            if (ReferenceEquals(transport!.GetParent(), this)) RemoveChild(transport);
            transport.QueueFree();
        }
        if (GodotObject.IsInstanceValid(bridge))
        {
            if (ReferenceEquals(_steamBridge, bridge)) _steamBridge = null;
            if (ReferenceEquals(bridge!.GetParent(), this)) RemoveChild(bridge);
            bridge.QueueFree();
        }
    }

    private static string ResolveAppVersion()
    {
        Variant configured = ProjectSettings.GetSetting("application/config/version", "development");
        string value = configured.AsString();
        return string.IsNullOrWhiteSpace(value) ? "development" : value.Trim();
    }
}
