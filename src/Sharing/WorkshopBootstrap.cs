using System;
using DesktopBuddy.App;
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
/// network connection only selects the null transport; it never participates in sandbox startup.
/// UI/application dependencies are injected by the main composition root rather than discovered
/// by polling absolute scene-tree paths.
/// </summary>
public partial class WorkshopBootstrap : Node
{
    private const string Category = "Workshop";
    private const string BridgeScriptPath = "res://src/Platform/Steam/GodotSteamBridge.gd";
    private CharacterStore? _characters;
    private CharacterSelectionState? _selection;
    private IRoomPaintingSharingHost? _environment;
    private SandboxRoot? _sandbox;
    private ITopLevelCommandRegistrar? _commandRegistrar;
    private WorkshopSharingCoordinator? _sharing;
    private WorkshopStagingStore? _staging;
    private RoomPaintingLibraryStore? _rooms;
    private WorkshopPanel? _panel;
    private IDisposable? _commandRegistration;
    private ISteamWorkshopTransport? _transport;
    private bool _servicesComposed;

    internal WorkshopSharingCoordinator? Sharing => _sharing;
    internal ISteamWorkshopTransport? Transport => _transport;
    internal RoomPaintingLibraryStore? RoomLibrary => _rooms;

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
        _environment = environment;
        _commandRegistrar = commandRegistrar;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ComposeServices();
        if (DisplayServer.GetName() != "headless")
            ComposeUi();
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        _commandRegistration?.Dispose();
        _commandRegistration = null;
        if (GodotObject.IsInstanceValid(_panel)) _panel!.QueueFree();
        _panel = null;
        base._ExitTree();
    }

    private void ComposeUi()
    {
        if (_sharing is null || _rooms is null || _selection is null || _characters is null || _sandbox is null)
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
        var characterImporter = new CharacterShareImporter(_staging, _characters);
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
            if (!transport.Initialize(bridge, identity))
            {
                string reason = transport.UnavailableReason ?? "GodotSteam initialization failed.";
                Log.Warn(Category, reason);
                DisposeFailedTransport(bridge, transport);
                return new NullSteamWorkshopTransport(reason);
            }

            Log.Info(
                Category,
                $"Steam Workshop initialized; runtimeAppId={identity.RuntimeAppId} workshopOwnerAppId={identity.WorkshopOwnerAppId} crossApp={identity.IsCrossApp}.");
            return transport;
        }
        catch (Exception exception)
        {
            DisposeFailedTransport(bridge, transport);
            Log.Warn(Category, $"Steam integration disabled: {exception.Message}");
            return new NullSteamWorkshopTransport(exception.Message);
        }
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
