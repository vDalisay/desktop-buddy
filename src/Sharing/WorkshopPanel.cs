using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Environment;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sharing;

/// <summary>
/// Small in-game Workshop surface. Full discovery remains in the Steam overlay; this window owns
/// publishing, subscriptions, validation/import, and explicit application of imported rooms.
/// Remote preview images are deliberately not rendered here.
/// </summary>
public partial class WorkshopPanel : Window
{
    private WorkshopSharingCoordinator? _sharing;
    private RoomPaintingLibraryStore? _rooms;
    private EnvironmentCustomizationBootstrap? _environment;
    private CharacterSelectionState? _selection;
    private readonly List<Button> _operationButtons = [];
    private LineEdit _title = null!;
    private TextEdit _description = null!;
    private Label _availability = null!;
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private VBoxContainer _subscriptions = null!;
    private VBoxContainer _roomLibrary = null!;
    private Button _publishBuddy = null!;
    private bool _built;
    private bool _busy;

    public bool IsOpen => Visible;

    public void Configure(
        WorkshopSharingCoordinator sharing,
        RoomPaintingLibraryStore rooms,
        EnvironmentCustomizationBootstrap environment,
        CharacterSelectionState selection)
    {
        _sharing = sharing ?? throw new ArgumentNullException(nameof(sharing));
        _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _selection.Changed -= OnCharacterSelectionChanged;
        _selection.Changed += OnCharacterSelectionChanged;
        if (_built) RefreshAvailability();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Title = "Desktop Buddy Workshop";
        Size = new Vector2I(760, 680);
        MinSize = new Vector2I(620, 520);
        Unresizable = false;
        Exclusive = false;
        Transient = false;
        AlwaysOnTop = true;
        Theme = Win98ThemeFactory.Create();
        CloseRequested += Hide;
        Build();
        _built = true;
        RefreshAvailability();
        Hide();
    }

    public override void _ExitTree()
    {
        if (_selection is not null) _selection.Changed -= OnCharacterSelectionChanged;
        base._ExitTree();
    }

    public void Open()
    {
        if (!_built || _sharing is null) return;
        RefreshAvailability();
        PopupCentered();
        _ = RefreshAsync();
    }

    private void Build()
    {
        var root = new PanelContainer { Name = "WorkshopRoot" };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face));
        AddChild(root);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        root.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        _availability = new Label
        {
            Text = "Steam Workshop: checking...",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_availability);

        var browseRow = new HBoxContainer();
        column.AddChild(browseRow);
        AddOperationButton(browseRow, "Browse Workshop...", () => _sharing?.OpenWorkshopBrowser());
        AddOperationButton(browseRow, "Refresh Subscriptions", () => _ = RefreshSubscriptionsAsync());
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        browseRow.AddChild(spacer);
        var legal = new Label { Text = "Publishing is subject to the Steam Workshop Legal Agreement." };
        legal.AddThemeFontSizeOverride("font_size", 11);
        browseRow.AddChild(legal);

        column.AddChild(SectionLabel("Publish"));
        _title = new LineEdit
        {
            PlaceholderText = "Workshop title",
            Text = "My Desktop Buddy Creation",
            MaxLength = 128,
        };
        column.AddChild(_title);
        _description = new TextEdit
        {
            PlaceholderText = "Optional description",
            CustomMinimumSize = new Vector2(0, 78),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        column.AddChild(_description);

        var publishRow = new HBoxContainer();
        column.AddChild(publishRow);
        AddOperationButton(publishRow, "Publish Room Painting", () => _ = PublishRoomAsync());
        _publishBuddy = AddOperationButton(publishRow, "Publish Active Buddy", () => _ = PublishBuddyAsync());

        var split = new HSplitContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SplitOffset = 365,
        };
        column.AddChild(split);

        var subscriptionColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        subscriptionColumn.AddChild(SectionLabel("My Subscriptions"));
        var subscriptionScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        subscriptionColumn.AddChild(subscriptionScroll);
        _subscriptions = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        subscriptionScroll.AddChild(_subscriptions);
        split.AddChild(subscriptionColumn);

        var roomColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        roomColumn.AddChild(SectionLabel("Imported Room Paintings"));
        var roomScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        roomColumn.AddChild(roomScroll);
        _roomLibrary = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        roomScroll.AddChild(_roomLibrary);
        split.AddChild(roomColumn);

        _progress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 14),
        };
        column.AddChild(_progress);
        _status = new Label
        {
            Text = "Ready.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 34),
        };
        column.AddChild(_status);
    }

    private async Task PublishRoomAsync()
    {
        if (_busy || _sharing is null || _environment is null) return;
        byte[] pixels = _environment.SnapshotRoomPaintingForSharing();
        await RunBusyAsync(async (progress, token) =>
        {
            WorkshopPublishResult result = await _sharing.PublishRoomAsync(
                pixels,
                _title.Text,
                _description.Text,
                progress,
                token);
            SetPublishStatus(result, "Room painting");
        });
    }

    private async Task PublishBuddyAsync()
    {
        if (_busy || _sharing is null || _selection?.ActiveCharacterId is not Guid id) return;
        await RunBusyAsync(async (progress, token) =>
        {
            WorkshopPublishResult result = await _sharing.PublishCharacterAsync(
                id,
                _title.Text,
                _description.Text,
                previewPng: null,
                progress,
                token);
            SetPublishStatus(result, "Buddy");
        });
    }

    private async Task RefreshAsync()
    {
        RefreshRoomLibrary();
        await RefreshSubscriptionsAsync();
    }

    private async Task RefreshSubscriptionsAsync()
    {
        if (_busy || _sharing is null) return;
        SetStatus("Refreshing Workshop subscriptions...");
        IReadOnlyList<PublishedWorkshopItem> items;
        try
        {
            items = await _sharing.GetSubscriptionsAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            SetStatus($"Could not refresh subscriptions: {exception.Message}");
            return;
        }
        RebuildSubscriptions(items);
        SetStatus(items.Count == 0 ? "No subscribed Desktop Buddy items found." : $"Found {items.Count} subscribed Workshop item(s).");
    }

    private void RebuildSubscriptions(IReadOnlyList<PublishedWorkshopItem> items)
    {
        Clear(_subscriptions);
        foreach (PublishedWorkshopItem item in items)
        {
            var row = new HBoxContainer();
            var label = new Label
            {
                Text = item.DisplayName,
                TooltipText = $"Steam Workshop item {item.PublishedFileId}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            row.AddChild(label);
            Button open = new() { Text = "Open" };
            open.Pressed += () => _sharing?.OpenWorkshopItem(item.PublishedFileId);
            row.AddChild(open);
            Button import = new() { Text = "Import" };
            import.Pressed += () => _ = ImportAsync(item);
            row.AddChild(import);
            _subscriptions.AddChild(row);
        }
    }

    private async Task ImportAsync(PublishedWorkshopItem item)
    {
        if (_busy || _sharing is null) return;
        await RunBusyAsync(async (progress, token) =>
        {
            WorkshopImportResult result = await _sharing.ImportSubscribedAsync(item, progress, token);
            switch (result.Status)
            {
                case WorkshopImportStatus.ImportedRoom:
                    SetStatus("Room painting imported. It was not applied automatically; choose Apply in Imported Room Paintings.");
                    RefreshRoomLibrary();
                    break;
                case WorkshopImportStatus.ImportedBuddy:
                    SetStatus("Buddy imported as a new local character. It was not activated automatically; select it in Buddy Studio/Paint Buddy when wanted.");
                    break;
                case WorkshopImportStatus.UnsupportedContent:
                    SetStatus(result.Detail ?? "This subscribed item is not a supported Desktop Buddy share.");
                    break;
                default:
                    SetStatus(result.Detail ?? "Workshop import failed.");
                    break;
            }
        });
    }

    private void RefreshRoomLibrary()
    {
        if (_rooms is null) return;
        Clear(_roomLibrary);
        IReadOnlyList<RoomPaintingLibraryEntry> rooms = _rooms.List();
        foreach (RoomPaintingLibraryEntry room in rooms)
        {
            var row = new HBoxContainer();
            var label = new Label
            {
                Text = room.DisplayName,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            row.AddChild(label);
            Button apply = new() { Text = "Apply" };
            apply.Pressed += () => _ = ApplyRoomAsync(room);
            row.AddChild(apply);
            _roomLibrary.AddChild(row);
        }
    }

    private async Task ApplyRoomAsync(RoomPaintingLibraryEntry entry)
    {
        if (_busy || _rooms is null || _environment is null) return;
        byte[]? pixels = _rooms.LoadPixels(entry.Id);
        if (pixels is null)
        {
            SetStatus("The imported room painting is missing or invalid.");
            return;
        }
        SetBusy(true);
        try
        {
            bool applied = await _environment.ApplySharedRoomPaintingAsync(pixels);
            SetStatus(applied ? $"Applied '{entry.DisplayName}'." : "Could not apply the imported room painting.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not apply room painting: {exception.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunBusyAsync(Func<IProgress<WorkshopTransferProgress>, CancellationToken, Task> operation)
    {
        if (_busy) return;
        SetBusy(true);
        _progress.Value = 0;
        var progress = new Progress<WorkshopTransferProgress>(OnProgress);
        try
        {
            await operation(progress, CancellationToken.None);
        }
        catch (Exception exception)
        {
            SetStatus($"Workshop operation failed: {exception.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnProgress(WorkshopTransferProgress progress)
    {
        if (!GodotObject.IsInstanceValid(this)) return;
        _progress.Value = progress.BytesTotal == 0 ? 0 : progress.Fraction * 100.0;
        SetStatus(progress.BytesTotal == 0 ? progress.Stage : $"{progress.Stage}: {progress.Fraction:P0}");
    }

    private void SetPublishStatus(WorkshopPublishResult result, string noun)
    {
        string text = result.Status switch
        {
            WorkshopPublishStatus.Published => $"{noun} published to Steam Workshop as item {result.PublishedFileId}.",
            WorkshopPublishStatus.NeedsLegalAgreement => result.Detail ?? "Steam requires the Workshop Legal Agreement.",
            WorkshopPublishStatus.Unavailable => result.Detail ?? "Steam Workshop is unavailable.",
            WorkshopPublishStatus.Cancelled => result.Detail ?? "Publishing was cancelled.",
            _ => result.Detail ?? $"{noun} could not be published.",
        };
        SetStatus(text);
    }

    private void RefreshAvailability()
    {
        bool available = _sharing?.IsAvailable == true;
        _availability.Text = available
            ? "Steam Workshop: available. Downloads are validated and imported as local copies."
            : "Steam Workshop: unavailable/offline. Desktop Buddy remains fully playable; local painting and Buddy Studio are unchanged.";
        _publishBuddy.Disabled = !available || _selection?.ActiveCharacterId is null || _busy;
        foreach (Button button in _operationButtons)
            button.Disabled = _busy || (!available && button.Text != "Browse Workshop...");
        RefreshRoomLibrary();
    }

    private void OnCharacterSelectionChanged(Guid? _) => RefreshAvailability();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshAvailability();
    }

    private void SetStatus(string text)
    {
        if (GodotObject.IsInstanceValid(_status)) _status.Text = text;
    }

    private Button AddOperationButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
        _operationButtons.Add(button);
        return button;
    }

    private static Label SectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    private static void Clear(Node container)
    {
        foreach (Node child in container.GetChildren()) child.QueueFree();
    }
}