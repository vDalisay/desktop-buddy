using System;
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
/// </summary>
public partial class WorkshopBootstrap : Node
{
    private const string Category = "Workshop";
    private const string BridgeScriptPath = "res://src/Platform/Steam/GodotSteamBridge.gd";
    private const string SteamAppIdProjectSetting = "steam/initialization/app_id";
    private CharacterStore? _characters;
    private CharacterSelectionState? _selection;
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

    public void Configure(CharacterStore characters, CharacterSelectionState selection)
    {
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ComposeServices();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!_servicesComposed || DisplayServer.GetName() == "headless")
        {
            if (_servicesComposed) SetProcess(false);
            return;
        }
        if (_commandRegistration is not null)
        {
            SetProcess(false);
            return;
        }

        Win98CommandBarBootstrap? commandBar = GetNodeOrNull<Win98CommandBarBootstrap>("/root/Win98CommandBarBootstrap");
        EnvironmentCustomizationBootstrap? environment = GetNodeOrNull<EnvironmentCustomizationBootstrap>("/root/EnvironmentCustomizationBootstrap");
        if (!GodotObject.IsInstanceValid(commandBar) || !GodotObject.IsInstanceValid(environment) ||
            _sharing is null || _rooms is null || _selection is null)
            return;

        _panel = new WorkshopPanel { Name = nameof(WorkshopPanel) };
        _panel.Configure(_sharing, _rooms, environment!, _selection);
        AddChild(_panel);
        _commandRegistration = commandBar!.RegisterTopLevelCommand(
            new TopLevelCommandDefinition(
                TopLevelCommandIds.Workshop,
                "Workshop",
                "Share and import room paintings and buddies through Steam Workshop.",
                TopLevelCommandIds.WorkshopOrder),
            _panel.Open,
            isEnabled: () => GodotObject.IsInstanceValid(_panel) && !_panel!.IsOpen);
        SetProcess(false);
        Log.Info(Category, $"Workshop UI composed; transport={_transport?.GetType().Name ?? "none"} available={_sharing.IsAvailable}.");
    }

    public override void _ExitTree()
    {
        _commandRegistration?.Dispose();
        _commandRegistration = null;
        if (GodotObject.IsInstanceValid(_panel)) _panel!.QueueFree();
        _panel = null;
        base._ExitTree();
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

        uint appId = ResolveSteamAppId();
        if (appId == 0)
        {
            return new NullSteamWorkshopTransport(
                $"No Steam AppID is configured. Set DESKTOP_BUDDY_STEAM_APP_ID for development/CI or provide the canonical GodotSteam project setting '{SteamAppIdProjectSetting}' in the Steam release/depot configuration.");
        }

        try
        {
            GDScript? script = GD.Load<GDScript>(BridgeScriptPath);
            if (script is null)
                return new NullSteamWorkshopTransport("The project-owned GodotSteam bridge script could not be loaded.");
            GodotObject instance = (GodotObject)script.New();
            if (instance is not Node bridge)
                return new NullSteamWorkshopTransport("The GodotSteam bridge did not instantiate as a Node.");
            bridge.Name = "GodotSteamBridge";
            AddChild(bridge);

            var transport = new GodotSteamWorkshopTransport { Name = nameof(GodotSteamWorkshopTransport) };
            AddChild(transport);
            if (!transport.Initialize(bridge, appId))
            {
                string reason = transport.UnavailableReason ?? "GodotSteam initialization failed.";
                Log.Warn(Category, reason);
                return new NullSteamWorkshopTransport(reason);
            }
            return transport;
        }
        catch (Exception exception)
        {
            Log.Warn(Category, $"Steam integration disabled: {exception.Message}");
            return new NullSteamWorkshopTransport(exception.Message);
        }
    }

    /// <summary>
    /// Development/CI can override the AppID without changing project files. Release builds may
    /// instead materialize GodotSteam's canonical project setting through their depot/export
    /// configuration. The AppID is not a secret, but it must not be hard-coded into this branch.
    /// </summary>
    internal static uint ResolveSteamAppId()
    {
        string environment = OS.GetEnvironment("DESKTOP_BUDDY_STEAM_APP_ID");
        if (uint.TryParse(environment, out uint environmentId) && environmentId != 0)
            return environmentId;

        Variant configured = ProjectSettings.GetSetting(SteamAppIdProjectSetting, 0);
        long projectId = configured.AsInt64();
        return projectId is > 0 and <= uint.MaxValue ? checked((uint)projectId) : 0;
    }

    private static string ResolveAppVersion()
    {
        Variant configured = ProjectSettings.GetSetting("application/config/version", "development");
        string value = configured.AsString();
        return string.IsNullOrWhiteSpace(value) ? "development" : value.Trim();
    }
}
